[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSCommandPath)).TrimEnd('\')
$projectPath = Join-Path $repoRoot 'TESTMod v4\TESTMod.csproj'
$assembliesPath = Join-Path $repoRoot '必要程序集'
$dllPath = Join-Path $repoRoot "TESTMod v4\bin\$Configuration\TESTMod.dll"

if (!(Test-Path -LiteralPath $projectPath)) { throw "找不到模板工程：$projectPath" }
if (!(Test-Path -LiteralPath (Join-Path $assembliesPath '游戏原版DLL\Assembly-CSharp.dll')))
{
    throw '必要程序集校验失败：缺少游戏原版DLL/Assembly-CSharp.dll。'
}
if (!(Test-Path -LiteralPath (Join-Path $assembliesPath 'MOD辅助DLL\0Harmony.dll')))
{
    throw '必要程序集校验失败：缺少MOD辅助DLL/0Harmony.dll。'
}

Write-Host "编译 TESTMod v4 $Configuration DLL..."
& dotnet msbuild $projectPath `
    -t:Build `
    -p:Configuration=$Configuration `
    -p:Platform=AnyCPU `
    -restore
if ($LASTEXITCODE -ne 0) { throw "模板编译失败，退出代码：$LASTEXITCODE" }
if (!(Test-Path -LiteralPath $dllPath)) { throw "编译完成但找不到 DLL：$dllPath" }

$hash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
Write-Host "编译完成：$dllPath"
Write-Host "DLL SHA256：$hash"
