param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release',
  [string]$Runtime = 'win-x64',
  [string]$OutputPath = 'artifacts\PGame-UTSLManager',
  [switch]$FrameworkDependent,
  [string]$EmbeddedUtslPath = 'D:\59934\Desktop\UTSL生存服',
  [switch]$NoEmbeddedUtsl
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'maintenance\personal-overlay\PGame-UTSLManager\PGame-UTSLManager.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw "找不到管理器项目：$project" }

$output = if ([IO.Path]::IsPathRooted($OutputPath)) {
  $OutputPath
} else {
  Join-Path $repoRoot $OutputPath
}

$selfContained = -not $FrameworkDependent
$publishArgs = @(
  'publish', $project,
  '-c', $Configuration,
  '-r', $Runtime,
  "--self-contained=$($selfContained.ToString().ToLowerInvariant())",
  '-p:PublishSingleFile=true',
  '-p:IncludeNativeLibrariesForSelfExtract=true',
  '-p:PublishReadyToRun=false',
  '-o', $output
)

Write-Host "发布 PGame-UTSLManager → $output" -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw '管理器发布失败' }

$sample = [ordered]@{
  managerName = 'PGame-UTSLManager'
  startAllSequential = $true
  stopTimeoutSeconds = 6
  servers = @(
    [ordered]@{
      name = 'S1 生存服'
      rootPath = '.'
      executable = 'UnifierTSL.Core.exe'
      arguments = ''
      enabled = $true
      remark = '由 UnifierTSL.exe 管理器调用同目录核心'
    },
    [ordered]@{
      name = 'S2 资源'
      rootPath = 'S2'
      executable = 'UnifierTSL.Core.exe'
      arguments = ''
      enabled = $true
      remark = '第 2 个控制台实例'
    },
    [ordered]@{
      name = 'S3 建筑'
      rootPath = 'S3'
      executable = 'UnifierTSL.Core.exe'
      arguments = ''
      enabled = $true
      remark = '第 3 个控制台实例'
    }
  )
} | ConvertTo-Json -Depth 5

Set-Content -LiteralPath (Join-Path $output 'manager.example.json') -Value $sample -Encoding UTF8
$managerConfigPath = Join-Path $output 'manager.json'
if (-not (Test-Path -LiteralPath $managerConfigPath)) {
  Set-Content -LiteralPath $managerConfigPath -Value $sample -Encoding UTF8
}

if (-not $NoEmbeddedUtsl -and (Test-Path -LiteralPath $EmbeddedUtslPath -PathType Container)) {
  $payload = Join-Path $output 'UTSL'
  New-Item -ItemType Directory -Path $payload -Force | Out-Null
  Write-Host "内置 UTSL 运行文件 → $payload" -ForegroundColor Cyan
  robocopy $EmbeddedUtslPath $payload /E /XD logs /XF *.sqlite *.log run*.txt probe*.txt smoke*.txt setup-code.txt | Out-Null
  if ($LASTEXITCODE -gt 7) { throw "内置 UTSL 复制失败，robocopy 退出码 $LASTEXITCODE" }
}

Get-ChildItem -LiteralPath $output | Select-Object Name,Length
Write-Host '管理器发布完成。' -ForegroundColor Green
