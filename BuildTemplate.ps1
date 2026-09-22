#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ProjectPath,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSCommandPath)).TrimEnd('\')
if ([string]::IsNullOrWhiteSpace($ProjectPath))
{
    $projects = @(Get-ChildItem -LiteralPath $repoRoot -Filter '*.csproj' -File -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|packages|\.git|必要程序集|MOD根目录)\\' })
    if ($projects.Count -ne 1) { throw '无法唯一确定 DLL 工程，请使用 -ProjectPath 指定仓库内的 .csproj。' }
    $ProjectPath = $projects[0].FullName
}
elseif (![IO.Path]::IsPathRooted($ProjectPath)) { $ProjectPath = Join-Path $repoRoot $ProjectPath }
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
if (!$ProjectPath.StartsWith($repoRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetExtension($ProjectPath) -ne '.csproj' -or
    !(Test-Path -LiteralPath $ProjectPath -PathType Leaf)) { throw "找不到仓库内的工程：$ProjectPath" }

# packages.config 的自动还原需要 Visual Studio / Build Tools 自带的 MSBuild。
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = if (Test-Path -LiteralPath $vswhere) {
    & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1
}
if (!$msbuild) { throw '请安装 Visual Studio / Build Tools 的 .NET 桌面开发工作负载（含 .NET Framework 4.8 开发工具）。' }

$properties = @("-p:Configuration=$Configuration", '-p:Platform=AnyCPU')
Write-Host "编译 $ProjectPath ($Configuration)..."
& $msbuild $ProjectPath @properties -t:Build -restore -p:RestorePackagesConfig=true `
    "-p:RestoreRepositoryPath=$(Join-Path (Split-Path -Parent $ProjectPath) 'packages')" -nologo -verbosity:minimal | Out-Host
if ($LASTEXITCODE -ne 0) { throw "模板编译失败，退出代码：$LASTEXITCODE" }

# 从 MSBuild 取得实际产物，工程文件夹、csproj 和 AssemblyName 改名后无需改脚本。
$result = & $msbuild $ProjectPath @properties -getProperty:TargetPath,AssemblyName -nologo
if ($LASTEXITCODE -ne 0) { throw '无法读取 MSBuild 输出路径。' }
$metadata = ($result -join "`n" | ConvertFrom-Json).Properties
$dllPath = $metadata.TargetPath
if (!(Test-Path -LiteralPath $dllPath -PathType Leaf)) { throw "编译完成但找不到 DLL：$dllPath" }

$hash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
Write-Host "编译完成：$dllPath"
Write-Host "DLL SHA256：$hash"
if ($PassThru) {
    [pscustomobject]@{ ProjectPath = $ProjectPath; DllPath = $dllPath; AssemblyName = $metadata.AssemblyName; Hash = $hash }
}
