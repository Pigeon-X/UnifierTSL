param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release',
  [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$apply = Join-Path $repoRoot 'maintenance\config-translation\Apply-SourceLocalization.ps1'
if (-not (Test-Path -LiteralPath $apply)) { throw "找不到汉化脚本：$apply" }

Write-Host '=== 1. 应用源码级中文配置映射 ===' -ForegroundColor Cyan
& $apply

Write-Host '=== 2. 编译 UnifierTSL ===' -ForegroundColor Cyan
$dotnetArgs = @('build', (Join-Path $repoRoot 'src\UnifierTSL\UnifierTSL.csproj'), '-c', $Configuration)
if ($NoRestore) { $dotnetArgs += '--no-restore' }
& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { throw 'UnifierTSL 编译失败' }

Write-Host '个人版 UnifierTSL 编译完成。' -ForegroundColor Green