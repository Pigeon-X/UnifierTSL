param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release',
  [switch]$NoRestore,
  [switch]$BuildManager
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

if ($BuildManager) {
  Write-Host '=== 3. 编译 TSM 风格个人管理器 ===' -ForegroundColor Cyan
  $managerProject = Join-Path $repoRoot 'maintenance\personal-overlay\Pigeon.UnifierTSL.Manager\Pigeon.UnifierTSL.Manager.csproj'
  if (-not (Test-Path -LiteralPath $managerProject)) { throw "找不到管理器项目：$managerProject" }
  $managerArgs = @('build', $managerProject, '-c', $Configuration)
  if ($NoRestore) { $managerArgs += '--no-restore' }
  & dotnet @managerArgs
  if ($LASTEXITCODE -ne 0) { throw 'Pigeon UnifierTSL Manager 编译失败' }
}

Write-Host '个人版 UnifierTSL 编译完成。' -ForegroundColor Green
