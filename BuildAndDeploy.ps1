[CmdletBinding()]
param(
    [string]$WorkshopRoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSCommandPath)).TrimEnd('\')
$projectPath = Join-Path $repoRoot 'TESTMod v4\TESTMod.csproj'
$modContainer = Join-Path $repoRoot 'MOD根目录'
if (!(Test-Path -LiteralPath $modContainer -PathType Container)) { throw "找不到仓库 MOD 根目录：$modContainer" }
$modDirectories = @(Get-ChildItem -LiteralPath $modContainer -Directory | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName 'StageModInfo.xml') -PathType Leaf
})
if ($modDirectories.Count -ne 1)
{
    throw 'MOD根目录 中必须恰好有一个含 StageModInfo.xml 的完整 MOD 文件夹。'
}
$modSource = $modDirectories[0].FullName
$modDirectoryName = $modDirectories[0].Name
$dllSource = Join-Path $repoRoot 'TESTMod v4\bin\Release\TESTMod.dll'

function Resolve-LibraryOfRuinaWorkshopRoot([string]$overridePath, [string]$modDirectoryName)
{
    if (![string]::IsNullOrWhiteSpace($overridePath))
    {
        $resolved = [IO.Path]::GetFullPath($overridePath).TrimEnd('\')
        if ((Split-Path -Leaf $resolved) -ne '1256670' -or !(Test-Path -LiteralPath $resolved -PathType Container))
        {
            throw "-WorkshopRoot 必须指向已存在的 ...\steamapps\workshop\content\1256670：$resolved"
        }
        return $resolved
    }

    $steamRoots = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $libraryRoots = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    function Add-ExistingRoot([Collections.Generic.HashSet[string]]$set, [string]$path)
    {
        if ([string]::IsNullOrWhiteSpace($path)) { return }
        $fullPath = [IO.Path]::GetFullPath($path.Replace('/', '\')).TrimEnd('\')
        if (Test-Path -LiteralPath $fullPath -PathType Container) { [void]$set.Add($fullPath) }
    }

    foreach ($registryLocation in @(
        @{ Path = 'HKCU:\Software\Valve\Steam'; Name = 'SteamPath' },
        @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; Name = 'InstallPath' },
        @{ Path = 'HKLM:\SOFTWARE\Valve\Steam'; Name = 'InstallPath' }
    ))
    {
        try { Add-ExistingRoot $steamRoots (Get-ItemPropertyValue -LiteralPath $registryLocation.Path -Name $registryLocation.Name -ErrorAction Stop) }
        catch { }
    }
    Add-ExistingRoot $steamRoots (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Steam')
    Add-ExistingRoot $steamRoots (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Steam')

    foreach ($steamRoot in @($steamRoots))
    {
        Add-ExistingRoot $libraryRoots $steamRoot
        $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (!(Test-Path -LiteralPath $libraryFile)) { continue }
        $content = Get-Content -LiteralPath $libraryFile -Raw
        foreach ($match in [regex]::Matches($content, '"path"\s+"(?<path>[^"]+)"'))
        {
            Add-ExistingRoot $libraryRoots ($match.Groups['path'].Value -replace '\\\\', '\')
        }
    }

    $candidates = @($libraryRoots | ForEach-Object {
        $workshop = Join-Path $_ 'steamapps\workshop\content\1256670'
        [pscustomobject]@{
            WorkshopRoot = $workshop
            HasInstalledMod = Test-Path -LiteralPath (Join-Path $workshop $modDirectoryName) -PathType Container
            HasGameManifest = Test-Path -LiteralPath (Join-Path $_ 'steamapps\appmanifest_1256670.acf')
            WorkshopExists = Test-Path -LiteralPath $workshop -PathType Container
        }
    })

    $matches = @($candidates | Where-Object HasInstalledMod)
    if ($matches.Count -eq 0) { $matches = @($candidates | Where-Object { $_.HasGameManifest -and $_.WorkshopExists }) }
    if ($matches.Count -eq 0) { $matches = @($candidates | Where-Object WorkshopExists) }
    if ($matches.Count -ne 1)
    {
        $found = ($matches.WorkshopRoot -join "`n  ")
        throw "无法唯一确定《废墟图书馆》的 Workshop 根目录。可使用 -WorkshopRoot 手动指定。`n  $found"
    }
    return [IO.Path]::GetFullPath($matches[0].WorkshopRoot).TrimEnd('\')
}

$workshopParent = Resolve-LibraryOfRuinaWorkshopRoot $WorkshopRoot $modDirectoryName
Write-Host "Workshop 根目录：$workshopParent"
$modTarget = Join-Path $workshopParent $modDirectoryName
$stagingTarget = Join-Path $workshopParent "$modDirectoryName.__deploy_staging"
$backupTarget = Join-Path $workshopParent "$modDirectoryName.__deploy_backup"

function Assert-WorkshopChild([string]$path, [string]$expectedName)
{
    $fullPath = [IO.Path]::GetFullPath($path).TrimEnd('\')
    $parent = [IO.Path]::GetFullPath((Split-Path -Parent $fullPath)).TrimEnd('\')
    if ($parent -ne $workshopParent -or (Split-Path -Leaf $fullPath) -ne $expectedName)
    {
        throw "部署目录安全检查失败：$fullPath"
    }
}

Assert-WorkshopChild $modTarget $modDirectoryName
Assert-WorkshopChild $stagingTarget "$modDirectoryName.__deploy_staging"
Assert-WorkshopChild $backupTarget "$modDirectoryName.__deploy_backup"

if (!(Test-Path -LiteralPath $projectPath)) { throw "找不到工程：$projectPath" }
if (!(Test-Path -LiteralPath $modSource)) { throw "找不到仓库 MOD 根目录：$modSource" }

$stageModInfo = Join-Path $modSource 'StageModInfo.xml'
if (!(Test-Path -LiteralPath $stageModInfo) -or
    (Get-Content -LiteralPath $stageModInfo -Raw) -notmatch '<ID>TESTMod</ID>')
{
    throw '仓库 MOD 根目录的 StageModInfo.xml 校验失败。'
}

Write-Host '[1/5] 编译 Release DLL...'
& dotnet msbuild $projectPath `
    -t:Build `
    -p:Configuration=Release `
    -p:Platform=AnyCPU `
    -restore
if ($LASTEXITCODE -ne 0) { throw "DLL 编译失败，退出代码：$LASTEXITCODE" }
if (!(Test-Path -LiteralPath $dllSource)) { throw "编译完成但找不到 DLL：$dllSource" }

$sourceHash = (Get-FileHash -LiteralPath $dllSource -Algorithm SHA256).Hash
$modDll = Join-Path $modSource 'Assemblies\TESTMod.dll'
Write-Host '[2/5] 将 DLL 复制到仓库 MOD 根目录...'
New-Item -ItemType Directory -Path (Split-Path -Parent $modDll) -Force | Out-Null
Copy-Item -LiteralPath $dllSource -Destination $modDll -Force
if ((Get-FileHash -LiteralPath $modDll -Algorithm SHA256).Hash -ne $sourceHash)
{
    throw '复制到 MOD 根目录后的 DLL 哈希校验失败。'
}

if (Test-Path -LiteralPath $backupTarget)
{
    throw "检测到上次部署留下的备份目录，请先人工确认后处理：$backupTarget"
}
if (Test-Path -LiteralPath $stagingTarget)
{
    Remove-Item -LiteralPath $stagingTarget -Recurse -Force
}

Write-Host '[3/5] 准备完整 MOD 临时目录...'
New-Item -ItemType Directory -Path $stagingTarget | Out-Null
Get-ChildItem -LiteralPath $modSource -Force |
    Copy-Item -Destination $stagingTarget -Recurse -Force

$stagingDll = Join-Path $stagingTarget 'Assemblies\TESTMod.dll'
if (!(Test-Path -LiteralPath $stagingDll)) { throw '临时目录缺少 TESTMod.dll。' }
$stagingHash = (Get-FileHash -LiteralPath $stagingDll -Algorithm SHA256).Hash
if ($sourceHash -ne $stagingHash) { throw '临时目录中的 DLL 哈希校验失败。' }
if (!(Test-Path -LiteralPath (Join-Path $stagingTarget 'StageModInfo.xml')))
{
    throw '临时目录缺少 StageModInfo.xml。'
}

Write-Host '[4/5] 整体替换 Workshop MOD 目录...'
$originalMoved = $false
try
{
    if (Test-Path -LiteralPath $modTarget)
    {
        Move-Item -LiteralPath $modTarget -Destination $backupTarget
        $originalMoved = $true
    }
    Move-Item -LiteralPath $stagingTarget -Destination $modTarget
}
catch
{
    if (!(Test-Path -LiteralPath $modTarget) -and $originalMoved -and (Test-Path -LiteralPath $backupTarget))
    {
        Move-Item -LiteralPath $backupTarget -Destination $modTarget
    }
    throw
}

$deployedDll = Join-Path $modTarget 'Assemblies\TESTMod.dll'
$deployedHash = (Get-FileHash -LiteralPath $deployedDll -Algorithm SHA256).Hash
if ($sourceHash -ne $deployedHash)
{
    throw '目录替换完成，但最终 DLL 哈希校验失败；旧目录仍保留在备份位置。'
}

Write-Host '[5/5] 清理部署备份...'
if (Test-Path -LiteralPath $backupTarget)
{
    Remove-Item -LiteralPath $backupTarget -Recurse -Force
}

Write-Host "部署完成：$modTarget"
Write-Host "DLL SHA256：$sourceHash"
