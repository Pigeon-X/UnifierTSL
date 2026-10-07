param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release',
  [string]$Runtime = 'win-x64',
  [string]$OutputPath = 'artifacts\Pigeon.UnifierTSL.Manager',
  [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'maintenance\personal-overlay\Pigeon.UnifierTSL.Manager\Pigeon.UnifierTSL.Manager.csproj'
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

Write-Host "发布 Pigeon UnifierTSL Manager → $output" -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw '管理器发布失败' }

$sample = [ordered]@{
  managerName = 'Pigeon UnifierTSL Manager'
  startAllSequential = $true
  stopTimeoutSeconds = 6
  servers = @(
    [ordered]@{
      name = 'S1 生存'
      rootPath = 'D:\Terraria\UnifierTSL-S1'
      executable = 'UnifierTSL.exe'
      arguments = '-port 7777 -joinserver first'
      enabled = $true
      remark = '第 1 个控制台实例'
    },
    [ordered]@{
      name = 'S2 资源'
      rootPath = 'D:\Terraria\UnifierTSL-S2'
      executable = 'UnifierTSL.exe'
      arguments = '-port 7778 -joinserver first'
      enabled = $true
      remark = '第 2 个控制台实例'
    },
    [ordered]@{
      name = 'S3 建筑'
      rootPath = 'D:\Terraria\UnifierTSL-S3'
      executable = 'UnifierTSL.exe'
      arguments = '-port 7779 -joinserver first'
      enabled = $true
      remark = '第 3 个控制台实例'
    }
  )
} | ConvertTo-Json -Depth 5

Set-Content -LiteralPath (Join-Path $output 'manager.example.json') -Value $sample -Encoding UTF8
Get-ChildItem -LiteralPath $output | Select-Object Name,Length
Write-Host '管理器发布完成。' -ForegroundColor Green
