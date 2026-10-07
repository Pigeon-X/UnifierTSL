param(
  [ValidateSet('Check','UpdateMain','MergePersonal')]
  [string]$Mode = 'Check'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$personalBranch = 'personal/pigeon-maintenance'
if (-not (Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw "找不到仓库：$repo" }

Write-Host '=== Fetch upstream ===' -ForegroundColor Cyan
git -C $repo fetch upstream --tags --prune
if ($LASTEXITCODE -ne 0) { throw '上游 fetch 失败' }

$upstreamSha = (git -C $repo rev-parse upstream/main).Trim()
$mainSha = (git -C $repo rev-parse main).Trim()
$personalSha = (git -C $repo rev-parse $personalBranch).Trim()
Write-Host ('upstream/main = ' + $upstreamSha)
Write-Host ('main          = ' + $mainSha)
Write-Host ($personalBranch + ' = ' + $personalSha)

switch ($Mode) {
  'Check' {
    Write-Host ''
    Write-Host '=== main 落后 upstream/main 的提交 ===' -ForegroundColor Yellow
    git -C $repo log --oneline --decorate main..upstream/main
    if ($LASTEXITCODE -ne 0) { throw '比较 main 失败' }

    Write-Host ''
    Write-Host '=== personal 分支落后 upstream/main 的提交 ===' -ForegroundColor Yellow
    git -C $repo log --oneline --decorate $personalBranch..upstream/main
    if ($LASTEXITCODE -ne 0) { throw '比较 personal 失败' }
  }
  'UpdateMain' {
    Write-Host '=== 更新 main（只允许 fast-forward） ===' -ForegroundColor Cyan
    git -C $repo switch main
    if ($LASTEXITCODE -ne 0) { throw '切换 main 失败' }
    git -C $repo merge --ff-only upstream/main
    if ($LASTEXITCODE -ne 0) { throw 'main 不能 fast-forward，请人工检查' }
    git -C $repo push origin main
    if ($LASTEXITCODE -ne 0) { throw '推送 fork main 失败' }
    Write-Host 'main 已更新并推送到 origin。' -ForegroundColor Green
  }
  'MergePersonal' {
    Write-Host '=== 合并 main 到个人维护分支 ===' -ForegroundColor Cyan
    git -C $repo switch $personalBranch
    if ($LASTEXITCODE -ne 0) { throw '切换个人维护分支失败' }
    git -C $repo merge main
    if ($LASTEXITCODE -ne 0) {
      Write-Warning '合并存在冲突，请解决后 commit。'
      exit 2
    }
    Write-Host '个人维护分支已合并 main。' -ForegroundColor Green
  }
}