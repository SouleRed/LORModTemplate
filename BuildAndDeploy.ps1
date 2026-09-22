#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ModFolderName,
    [string]$WorkshopRoot,
    [string]$ProjectPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$modContainer = Join-Path $repoRoot 'MOD根目录'

function Assert-ChildPath([string]$Path, [string]$Parent) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ((Split-Path -Parent $full) -ne [IO.Path]::GetFullPath($Parent).TrimEnd('\')) {
        throw "必须是指定目录的直接子项：$full（父目录：$Parent）"
    }
}

function Assert-NoLinks([string]$Path, [switch]$Recurse) {
    # 防止链接/联接把替换或清理操作重定向到其他目录。
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "部署路径不能包含链接：$current" }
        }
        $current = Split-Path -Parent $current
    }
    if ($Recurse -and (Test-Path -LiteralPath $Path)) {
        foreach ($item in Get-ChildItem -LiteralPath $Path -Recurse -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "MOD 内不能包含链接：$($item.FullName)" }
        }
    }
}

function Resolve-WorkshopRoot([string]$Override, [string]$FolderName) {
    if (![string]::IsNullOrWhiteSpace($Override)) {
        $full = [IO.Path]::GetFullPath($Override).TrimEnd('\')
        if ((Split-Path -Leaf $full) -ne '1256670' -or !(Test-Path -LiteralPath $full -PathType Container)) {
            throw '-WorkshopRoot 必须指向已存在的 ...\steamapps\workshop\content\1256670 目录。'
        }
        return $full
    }

    $libraries = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $steamRoots = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($key in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1256670',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1256670',
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1256670'
    )) {
        $game = Get-ItemPropertyValue -LiteralPath $key -Name InstallLocation -ErrorAction SilentlyContinue
        if ($game -and (Test-Path -LiteralPath (Join-Path $game 'LibraryOfRuina.exe'))) {
            [void]$libraries.Add([IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $game)))).TrimEnd('\'))
        }
    }
    foreach ($entry in @(
        @{ Path = 'HKCU:\Software\Valve\Steam'; Name = 'SteamPath' },
        @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; Name = 'InstallPath' },
        @{ Path = 'HKLM:\SOFTWARE\Valve\Steam'; Name = 'InstallPath' }
    )) {
        $steam = Get-ItemPropertyValue -LiteralPath $entry.Path -Name $entry.Name -ErrorAction SilentlyContinue
        if ($steam) { [void]$steamRoots.Add([IO.Path]::GetFullPath($steam).TrimEnd('\')) }
    }
    foreach ($steam in $steamRoots) {
        [void]$libraries.Add($steam)
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (!(Test-Path -LiteralPath $vdf)) { continue }
        foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '(?m)^\s*"(?:path|\d+)"\s*"([^"]+)"')) {
            $library = $match.Groups[1].Value.Replace('\\', '\')
            if ([IO.Path]::IsPathRooted($library)) { [void]$libraries.Add([IO.Path]::GetFullPath($library).TrimEnd('\')) }
        }
    }
    $candidates = @($libraries | ForEach-Object {
        $workshop = Join-Path $_ 'steamapps\workshop\content\1256670'
        [pscustomobject]@{
            Path = $workshop
            HasMod = Test-Path -LiteralPath (Join-Path $workshop $FolderName)
            HasGame = Test-Path -LiteralPath (Join-Path $_ 'steamapps\appmanifest_1256670.acf')
            Exists = Test-Path -LiteralPath $workshop -PathType Container
        }
    })
    $matches = @($candidates | Where-Object HasMod)
    if ($matches.Count -eq 0) { $matches = @($candidates | Where-Object HasGame) }
    if ($matches.Count -eq 0) { $matches = @($candidates | Where-Object Exists) }
    if ($matches.Count -ne 1) { throw '无法唯一定位 Steam Workshop，请使用 -WorkshopRoot 指定。' }
    return $matches[0].Path
}

function Get-ModInfo([string]$Root) {
    $file = Join-Path $Root 'StageModInfo.xml'
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "缺少 $file" }
    [xml]$info = Get-Content -LiteralPath $file -Raw
    if (!$info.SelectSingleNode('/NormalInvitation/InvitationFile') -or
        [string]::IsNullOrWhiteSpace($info.NormalInvitation.Workshop.ID)) { throw "MOD 清单格式不完整：$file" }
    return $info
}

function Get-FileManifest([string]$Root) {
    $manifest = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force) {
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName)
        $manifest[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $manifest
}

function Assert-Manifest([string]$Root, [hashtable]$Expected) {
    $actual = Get-FileManifest $Root
    if ($actual.Count -ne $Expected.Count) { throw "文件数量校验失败：$Root" }
    foreach ($key in $Expected.Keys) {
        if ($actual[$key] -ne $Expected[$key]) { throw "文件 SHA256 校验失败：$Root\$key" }
    }
}

if (!(Test-Path -LiteralPath $modContainer -PathType Container)) { throw "缺少 $modContainer" }
if ([string]::IsNullOrWhiteSpace($ModFolderName)) {
    $mods = @(Get-ChildItem -LiteralPath $modContainer -Directory | Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName 'StageModInfo.xml') -PathType Leaf
    })
    if ($mods.Count -ne 1) { throw 'MOD根目录 中必须恰好有一个含 StageModInfo.xml 的文件夹；多个 MOD 时请使用 -ModFolderName 选择。' }
    $ModFolderName = $mods[0].Name
}
if ($ModFolderName -in '.', '..' -or $ModFolderName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
    $ModFolderName -match '[. ]$') { throw '-ModFolderName 只能是直接子文件夹名，不能包含路径。' }
$modSource = Join-Path $modContainer $ModFolderName
Assert-ChildPath $modSource $modContainer
Assert-NoLinks $modSource -Recurse
$info = Get-ModInfo $modSource
$packageId = $info.NormalInvitation.Workshop.ID

# 校验清单引用和全部 XML；无效源文件不得进入目录替换阶段。
foreach ($node in $info.SelectNodes('/NormalInvitation/InvitationFile/*/Path')) {
    $relative = $node.InnerText.TrimStart('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative)) { throw 'InvitationFile 包含空路径；不使用的节点应整个删除。' }
    $file = [IO.Path]::GetFullPath((Join-Path $modSource $relative))
    if (!$file.StartsWith($modSource + '\', [StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath $file -PathType Leaf)) { throw "清单引用不在 MOD 内或文件不存在：$relative" }
}
foreach ($file in Get-ChildItem -LiteralPath $modSource -Filter '*.xml' -Recurse -File) {
    try { $null = [xml](Get-Content -LiteralPath $file.FullName -Raw) }
    catch { throw "XML 格式错误：$($file.FullName)：$_" }
}

$workshopParent = Resolve-WorkshopRoot $WorkshopRoot $ModFolderName
$modTarget = Join-Path $workshopParent $ModFolderName
Assert-ChildPath $modTarget $workshopParent
Assert-NoLinks $workshopParent
Assert-NoLinks $modTarget -Recurse
if ($modTarget.StartsWith($repoRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $repoRoot.StartsWith($modTarget + '\', [StringComparison]::OrdinalIgnoreCase) -or $modTarget -eq $repoRoot) {
    throw 'Workshop 目标不能与仓库重叠。'
}
if (Test-Path -LiteralPath $modTarget) {
    $existing = Get-ModInfo $modTarget
    if ($existing.NormalInvitation.Workshop.ID -ne $packageId) {
        throw "同名目标的包 ID 与源目录不同，拒绝替换：$modTarget"
    }
}

# 临时目录位于 1256670 之外，避免游戏把暂存文件误识别为第二个 MOD。
$transactionParent = Join-Path (Split-Path -Parent $workshopParent) '.lor-template-deploy'
$transaction = Join-Path $transactionParent $ModFolderName
$staging = Join-Path $transaction 'staging'
Assert-ChildPath $transaction $transactionParent
Assert-NoLinks $transaction
if (Test-Path -LiteralPath $transaction) { throw "上次部署尚未清理，请检查暂存文件后再处理：$transaction" }

$dependencies = @('0Harmony.dll', 'NAudio.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll',
    'Mono.Cecil.dll', 'Mono.Cecil.Mdb.dll', 'Mono.Cecil.Pdb.dll', 'Mono.Cecil.Rocks.dll')
foreach ($name in $dependencies) {
    if (!(Test-Path -LiteralPath (Join-Path $repoRoot "必要程序集\MOD辅助DLL\$name") -PathType Leaf)) {
        throw "缺少运行时依赖：$name"
    }
}
Write-Host "MOD 源目录：$modSource"
Write-Host "包 ID：$packageId"
Write-Host "部署目标：$modTarget"
if (!$PSCmdlet.ShouldProcess($modTarget, "编译 $Configuration 并整体替换 MOD")) { return }

$build = & (Join-Path $repoRoot 'BuildTemplate.ps1') -Configuration $Configuration -ProjectPath $ProjectPath -PassThru
$assemblies = Join-Path $modSource 'Assemblies'
New-Item -ItemType Directory -Path $assemblies -Force | Out-Null
$generatedRecord = Join-Path $assemblies '.template-build.json'
$dllName = Split-Path -Leaf $build.DllPath
if (Test-Path -LiteralPath $generatedRecord -PathType Leaf) {
    $previous = Get-Content -LiteralPath $generatedRecord -Raw | ConvertFrom-Json
    if ($previous.Name -ne $dllName) {
        $oldDll = Join-Path $assemblies $previous.Name
        Assert-ChildPath $oldDll $assemblies
        if ([IO.Path]::GetExtension($oldDll) -ne '.dll') { throw '生成记录包含无效的 DLL 名称。' }
        if (Test-Path -LiteralPath $oldDll -PathType Leaf) {
            if ((Get-FileHash -LiteralPath $oldDll).Hash -ne $previous.Hash) {
                throw "旧生成 DLL 已被手动修改，请确认后移出 Assemblies：$oldDll"
            }
            Remove-Item -LiteralPath $oldDll
            Write-Host "已移除上次生成的旧名称 DLL：$oldDll（可重新编译旧工程恢复）"
        }
    }
}
Copy-Item -LiteralPath $build.DllPath -Destination $assemblies -Force
foreach ($name in $dependencies) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "必要程序集\MOD辅助DLL\$name") -Destination $assemblies -Force
}
if ((Get-FileHash -LiteralPath (Join-Path $assemblies (Split-Path -Leaf $build.DllPath))).Hash -ne $build.Hash) {
    throw 'DLL 复制到 MOD 源目录后校验失败。'
}
@{ Name = $dllName; Hash = $build.Hash } | ConvertTo-Json | Set-Content -LiteralPath $generatedRecord -Encoding utf8

New-Item -ItemType Directory -Path $workshopParent -Force | Out-Null
New-Item -ItemType Directory -Path $transactionParent -Force | Out-Null
New-Item -ItemType Directory -Path $transaction | Out-Null
try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    $expected = @{}
    foreach ($item in Get-ChildItem -LiteralPath $modSource -Recurse -Force) {
        $relative = [IO.Path]::GetRelativePath($modSource, $item.FullName)
        $destination = Join-Path $staging $relative
        if ($item.PSIsContainer) { New-Item -ItemType Directory -Path $destination -Force | Out-Null }
        elseif ($item.Name -notin '.gitkeep', '.template-build.json', 'Init.log', 'Error.txt') {
            $expected[$relative] = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
            Copy-Item -LiteralPath $item.FullName -Destination $destination
        }
    }
    Assert-Manifest $staging $expected
    Assert-ChildPath $modTarget $workshopParent
    Assert-NoLinks $modTarget -Recurse
    if (Test-Path -LiteralPath $modTarget) {
        Remove-Item -LiteralPath $modTarget -Recurse -Force
        Write-Host "已删除同名旧 MOD 目录：$modTarget"
    }
    Move-Item -LiteralPath $staging -Destination $modTarget
    Assert-Manifest $modTarget $expected
}
catch {
    throw "部署失败；请检查目标目录及本次暂存目录 $transaction。原始错误：$_"
}
# 此时 transaction 应为空，只做非递归清理。
Remove-Item -LiteralPath $transaction
Write-Host "部署完成：$modTarget"
Write-Host "DLL SHA256：$($build.Hash)"
