param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release',
  [string]$CorePackagePath = 'D:\59934\Desktop\UTSL生存服',
  [string]$OutputPath = 'artifacts\UnifierTSL-UI'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$uiProject = Join-Path $repoRoot 'maintenance\personal-overlay\PGame-UTSLManager\PGame-UTSLManager.csproj'
if (-not (Test-Path -LiteralPath $uiProject)) { throw "找不到 UI 项目：$uiProject" }
if (-not (Test-Path -LiteralPath $CorePackagePath -PathType Container)) { throw "找不到 UTSL 核心包：$CorePackagePath" }

$output = if ([IO.Path]::IsPathRooted($OutputPath)) {
  $OutputPath
} else {
  Join-Path $repoRoot $OutputPath
}
$publishTemp = Join-Path $repoRoot ('artifacts\UnifierTSL-UI-publish-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

Write-Host '1/4 复制 UTSL 核心包' -ForegroundColor Cyan
robocopy $CorePackagePath $output /E /XD logs /XF *.sqlite *.log run*.txt probe*.txt smoke*.txt setup-code.txt | Out-Null
if ($LASTEXITCODE -gt 7) { throw "复制 UTSL 核心失败，robocopy 退出码 $LASTEXITCODE" }

$coreExe = Join-Path $output 'UnifierTSL.exe'
if (-not (Test-Path -LiteralPath $coreExe)) { throw "核心包缺少 UnifierTSL.exe：$coreExe" }
Move-Item -LiteralPath $coreExe -Destination (Join-Path $output 'UnifierTSL.Core.exe') -Force

Write-Host '2/4 发布 TSM 风格 UI' -ForegroundColor Cyan
$publishArgs = @(
  'publish', $uiProject,
  '-c', $Configuration,
  '-r', 'win-x64',
  '--self-contained=true',
  '-p:PublishSingleFile=true',
  '-p:IncludeNativeLibrariesForSelfExtract=true',
  '-p:PublishReadyToRun=false',
  '-o', $publishTemp
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'UI 发布失败' }

$uiExe = Join-Path $publishTemp 'PGame-UTSLManager.exe'
if (-not (Test-Path -LiteralPath $uiExe)) { throw "UI 发布目录缺少 PGame-UTSLManager.exe：$uiExe" }
Copy-Item -LiteralPath $uiExe -Destination (Join-Path $output 'UnifierTSL.exe') -Force

Write-Host '3/4 生成同目录管理器配置' -ForegroundColor Cyan
$managerConfig = [ordered]@{
  managerName = 'UnifierTSL'
  startAllSequential = $true
  stopTimeoutSeconds = 6
  startReadyTimeoutSeconds = 120
  servers = @(
    [ordered]@{
      name = 'S1 生存服'
      rootPath = '.'
      executable = 'UnifierTSL.Core.exe'
      arguments = ''
      enabled = $true
      remark = '由 UnifierTSL.exe 管理器调用同目录核心，默认端口 2020'
    },
    [ordered]@{
      name = 'S2 资源服'
      rootPath = 'S2'
      executable = 'UnifierTSL.Core.exe'
      arguments = ''
      enabled = $true
      remark = '按需复制 UTSL 核心到 S2 目录'
    },
    [ordered]@{
      name = 'S3 建筑服'
      rootPath = 'S3'
      executable = 'UnifierTSL.Core.exe'
      arguments = ''
      enabled = $true
      remark = '按需复制 UTSL 核心到 S3 目录'
    }
  )
}
$managerJson = $managerConfig | ConvertTo-Json -Depth 8
Set-Content -LiteralPath (Join-Path $output 'manager.json') -Value $managerJson -Encoding UTF8
Set-Content -LiteralPath (Join-Path $output 'manager.example.json') -Value $managerJson -Encoding UTF8

$uiReadme = @(
  '# UnifierTSL',
  '',
  '- 双击 `UnifierTSL.exe` 启动 TSM 风格管理器。',
  '- `UnifierTSL.exe` 是 UI 外壳，`UnifierTSL.Core.exe` 是原 UTSL 核心。',
  '- 默认游戏端口：2020。',
  '- 默认 REST 端口：7890。',
  '- 管理器会按 `manager.json` 顺序启动实例并等待端口就绪。',
  '- 额外实例可复制核心目录到 `S2` / `S3`，或修改 `manager.json` 的 rootPath。'
) -join "`r`n"
Set-Content -LiteralPath (Join-Path $output 'README_UI.md') -Value $uiReadme -Encoding UTF8

Write-Host '4/4 校验' -ForegroundColor Cyan
foreach ($required in @('UnifierTSL.exe','UnifierTSL.Core.exe','manager.json','lib\UnifierTSL.dll','config\config.json')) {
  if (-not (Test-Path -LiteralPath (Join-Path $output $required))) {
    throw "发布校验失败，缺少：$required"
  }
}

Get-ChildItem -LiteralPath $output -Force | Select-Object Name,Length
Write-Host "UnifierTSL UI 发布完成：$output" -ForegroundColor Green
