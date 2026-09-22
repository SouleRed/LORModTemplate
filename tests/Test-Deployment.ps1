#requires -Version 7.0
[CmdletBinding()]
param([switch]$KeepArtifacts)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$testRoot = Join-Path $tempParent ('LoRTemplate-Test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$fixture = Join-Path $testRoot '仓库 副本'
$workshop = Join-Path $testRoot 'steamapps\workshop\content\1256670'
New-Item -ItemType Directory -Path $fixture, $workshop | Out-Null
Write-Host "测试目录：$testRoot"

function Assert([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "FAIL: $Message" }
}
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    Assert ($null -ne $caught -and $caught.ToString() -match $Pattern) "Expected failure matching: $Pattern; actual: $caught"
}
function Assert-TestPath([string]$Path) {
    Assert ([IO.Path]::GetFullPath($Path).StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) 'Operation outside test directory'
}

try {
    foreach ($name in @('BuildTemplate.ps1', 'BuildAndDeploy.ps1', '必要程序集', 'MOD根目录')) {
        Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $fixture -Recurse
    }
    # Copy the project without packages/bin/obj: verifies first-use NuGet restore.
    $projectSource = Join-Path $repo 'TESTMod v4'
    $projectCopy = Join-Path $fixture 'DLL 工程'
    New-Item -ItemType Directory -Path $projectCopy | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $projectSource -Force) {
        if ($item.Name -notin 'bin', 'obj', 'packages', '.vs') {
            Copy-Item -LiteralPath $item.FullName -Destination $projectCopy -Recurse
        }
    }
    $projectFile = Join-Path $projectCopy 'TESTMod.csproj'
    $deploy = Join-Path $fixture 'BuildAndDeploy.ps1'
    $source = Join-Path $fixture 'MOD根目录\TESTMod'
    $target = Join-Path $workshop 'TESTMod'

    $sourceDll = Join-Path $source 'Assemblies\TESTMod.dll'
    $beforePreview = if (Test-Path -LiteralPath $sourceDll) { (Get-FileHash -LiteralPath $sourceDll).Hash } else { $null }
    & $deploy -WorkshopRoot $workshop -WhatIf
    $afterPreview = if (Test-Path -LiteralPath $sourceDll) { (Get-FileHash -LiteralPath $sourceDll).Hash } else { $null }
    Assert ($beforePreview -eq $afterPreview) 'WhatIf created or changed a DLL'
    Assert (!(Test-Path -LiteralPath $target)) 'WhatIf created a deployed folder'
    & $deploy -WorkshopRoot $workshop
    Assert (Test-Path -LiteralPath (Join-Path $target 'Assemblies\TESTMod.dll')) 'First deployment missing main DLL'
    Assert (Test-Path -LiteralPath (Join-Path $target 'Assemblies\MonoMod.RuntimeDetour.dll')) 'Missing runtime dependency'
    Assert (!(Test-Path -LiteralPath (Join-Path $target 'Assemblies\Assembly-CSharp.dll'))) 'Game assembly was deployed'
    Assert (@(Get-ChildItem -LiteralPath $target -Recurse -Force -Filter '.gitkeep').Count -eq 0) 'Placeholders leaked into deployment'
    Assert ((Get-FileHash -LiteralPath (Join-Path $target 'Assemblies\TESTMod.dll')).Hash -eq
        (Get-FileHash -LiteralPath (Join-Path $source 'Assemblies\TESTMod.dll')).Hash) 'DLL hash mismatch'

    # Whole-folder replacement must remove old-only files without retaining old versions.
    Set-Content -LiteralPath (Join-Path $target 'old-only.txt') -Value 'old version'
    [xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
    $projectXml.Project.PropertyGroup[0].AssemblyName = 'RenamedTemplate'
    $projectXml.Save($projectFile)
    & $deploy -WorkshopRoot $workshop
    Assert (!(Test-Path -LiteralPath (Join-Path $target 'old-only.txt'))) 'Old-only file survived replacement'
    Assert (!(Test-Path -LiteralPath (Join-Path $target 'Assemblies\TESTMod.dll'))) 'Stale main DLL survived AssemblyName rename'
    Assert (Test-Path -LiteralPath (Join-Path $target 'Assemblies\RenamedTemplate.dll')) 'Renamed main DLL missing'
    $stagingParent = Join-Path (Split-Path -Parent $workshop) '.lor-template-deploy'
    Assert (@(Get-ChildItem -LiteralPath $stagingParent -Force).Count -eq 0) 'Successful deployment left staging files or old versions'

    # Folder selection and literal Chinese/space/bracket names.
    $renamed = '我的接待 测试[1]'
    Assert-TestPath $source
    Rename-Item -LiteralPath $source -NewName $renamed
    $source = Join-Path $fixture "MOD根目录\$renamed"
    & $deploy -WorkshopRoot $workshop
    $renamedTarget = Join-Path $workshop $renamed
    Assert (Test-Path -LiteralPath (Join-Path $renamedTarget 'StageModInfo.xml')) 'Folder rename not used for deployment'
    Assert (Test-Path -LiteralPath $target) 'Unrelated old folder was removed'
    $before = (Get-FileHash -LiteralPath (Join-Path $renamedTarget 'Assemblies\RenamedTemplate.dll')).Hash

    Expect-Failure { & $deploy -WorkshopRoot $workshop -ModFolderName '..\bad' } '直接子文件夹名'
    Expect-Failure { & $deploy -WorkshopRoot (Split-Path -Parent $workshop) } '1256670'
    $second = Join-Path $fixture 'MOD根目录\第二个MOD'
    Copy-Item -LiteralPath $source -Destination $second -Recurse
    Expect-Failure { & $deploy -WorkshopRoot $workshop } '恰好有一个'
    & $deploy -WorkshopRoot $workshop -ModFolderName $renamed -WhatIf

    $manifestPath = Join-Path $source 'StageModInfo.xml'
    $manifestText = Get-Content -LiteralPath $manifestPath -Raw
    Set-Content -LiteralPath $manifestPath -Value ($manifestText.Replace('<ID>TESTMod</ID>', '<ID>DifferentMod</ID>'))
    Expect-Failure { & $deploy -WorkshopRoot $workshop -ModFolderName $renamed } '包 ID'
    Set-Content -LiteralPath $manifestPath -Value ($manifestText.Replace('\Data\StageInfo.xml', '..\outside.xml'))
    Expect-Failure { & $deploy -WorkshopRoot $workshop -ModFolderName $renamed } '清单引用'
    Set-Content -LiteralPath $manifestPath -Value $manifestText
    $stageFile = Join-Path $source 'Data\StageInfo.xml'
    $stageText = Get-Content -LiteralPath $stageFile -Raw
    Set-Content -LiteralPath $stageFile -Value '<broken'
    Expect-Failure { & $deploy -WorkshopRoot $workshop -ModFolderName $renamed } 'XML 格式错误'
    Set-Content -LiteralPath $stageFile -Value $stageText
    Assert ((Get-FileHash -LiteralPath (Join-Path $renamedTarget 'Assemblies\RenamedTemplate.dll')).Hash -eq $before) 'Rejected deployment changed target'

    # After deleting the old target, an installation failure leaves only the new staged files.
    function Move-Item {
        [CmdletBinding()]
        param([string]$LiteralPath, [string]$Destination)
        if ((Split-Path -Leaf $LiteralPath) -eq 'staging') { throw 'Injected directory switch failure' }
        Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination
    }
    try {
        Expect-Failure { & $deploy -WorkshopRoot $workshop -ModFolderName $renamed } '部署失败'
    }
    finally { Remove-Item Function:\Move-Item }
    Assert (!(Test-Path -LiteralPath $renamedTarget)) 'Deleted old target was unexpectedly restored'
    Assert (Test-Path -LiteralPath (Join-Path $stagingParent "$renamed\staging\Assemblies\RenamedTemplate.dll")) 'Failed deployment lost new staged files'
    Assert (@(Get-ChildItem -LiteralPath $stagingParent -Recurse -Force | Where-Object { $_.Name -match 'backup' }).Count -eq 0) 'Deployment created a backup'
    Expect-Failure { & $deploy -WorkshopRoot $workshop -ModFolderName $renamed } '上次部署尚未清理'
    Write-Host 'PASS: restore/build, WhatIf, first deployment, replacement without backups, renames, validation and failure handling.'
}
catch {
    Write-Host "测试失败，保留现场：$testRoot"
    throw
}
if (!$KeepArtifacts) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    Assert ((Split-Path -Parent $resolved) -eq $tempParent -and (Split-Path -Leaf $resolved) -match '^LoRTemplate-Test-[a-f0-9]{32}$') 'Unsafe cleanup target'
    Remove-Item -LiteralPath $resolved -Recurse -Force
    Write-Host '已清理本次生成的临时测试副本。'
}
