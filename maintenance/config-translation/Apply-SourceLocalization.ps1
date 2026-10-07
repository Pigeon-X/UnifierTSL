param(
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mapPath = Join-Path $PSScriptRoot 'TransferPatch.json'
if (-not (Test-Path -LiteralPath $mapPath)) { throw "找不到映射文件：$mapPath" }

$tshockConfig = Join-Path $repoRoot 'src\Plugins\TShockAPI\Configuration\TShockConfig.cs'
$secureRest = Join-Path $repoRoot 'src\Plugins\TShockAPI\Rest\SecureRest.cs'
if (-not (Test-Path -LiteralPath $tshockConfig)) { throw "找不到 TShockConfig.cs：$tshockConfig" }
if (-not (Test-Path -LiteralPath $secureRest)) { throw "找不到 SecureRest.cs：$secureRest" }

$mapping = (Get-Content -LiteralPath $mapPath -Raw -Encoding UTF8 | ConvertFrom-Json).'执行列表'[0].'翻译列表'
$entries = @($mapping.PSObject.Properties)

function Apply-JsonPropertyName {
  param(
    [string]$Path,
    [string]$Member,
    [string]$JsonName,
    [switch]$Preview
  )

  $raw = [IO.File]::ReadAllText($Path)
  $newline = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
  $hasFinalNewline = $raw.EndsWith($newline)
  $lines = New-Object System.Collections.Generic.List[string]
  $parts = @($raw -split "`r?`n")
  if ($hasFinalNewline -and $parts.Count -gt 1 -and $parts[$parts.Count - 1] -eq '') { $parts = @($parts[0..($parts.Count - 2)]) }
  foreach ($line in $parts) { $lines.Add($line) }

  $declPattern = '^\s*(?:public|internal|protected)\s+[^;=()]+\b' + [regex]::Escape($Member) + '\b\s*(?:=|;|\{|$)'
  $declIndex = -1
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $declPattern) { $declIndex = $i; break }
  }
  if ($declIndex -lt 0) { return "MISSING $Member" }

  $indent = [regex]::Match($lines[$declIndex], '^\s*').Value
  $attribute = $indent + '[Newtonsoft.Json.JsonProperty("' + $JsonName + '")]'
  $existingIndex = -1
  $probe = $declIndex - 1
  while ($probe -ge 0) {
    $trim = $lines[$probe].Trim()
    if ($trim -eq '') { $probe--; continue }
    if ($trim.StartsWith('[')) {
      if ($trim -match '^\[Newtonsoft\.Json\.JsonProperty') { $existingIndex = $probe; break }
      $probe--; continue
    }
    if ($trim.StartsWith('//') -or $trim.StartsWith('/*') -or $trim.StartsWith('*')) { $probe--; continue }
    break
  }
  if ($existingIndex -ge 0) {
    if ($lines[$existingIndex] -eq $attribute) { return "OK $Member" }
    if ($Preview) { return "UPDATE $Member -> $JsonName" }
    $lines[$existingIndex] = $attribute
    if (-not $DryRun) { [IO.File]::WriteAllText($Path, (($lines -join $newline) + $(if ($hasFinalNewline) { $newline } else { '' })), (New-Object Text.UTF8Encoding($false))) }
    return "UPDATED $Member"
  }

  if ($Preview) { return "ADD $Member -> $JsonName" }
  $lines.Insert($declIndex, $attribute)
  if (-not $DryRun) { [IO.File]::WriteAllText($Path, (($lines -join $newline) + $(if ($hasFinalNewline) { $newline } else { '' })), (New-Object Text.UTF8Encoding($false))) }
  return "ADDED $Member"
}

$added = 0
$updated = 0
$ok = 0
$missing = New-Object System.Collections.Generic.List[string]
foreach ($entry in $entries) {
  $full = $entry.Name
  $jsonName = [string]$entry.Value
  $target = ''
  $member = ''
  if ($full.StartsWith('TShockAPI.Configuration.TShockSettings.')) {
    $target = $tshockConfig
    $member = $full.Substring('TShockAPI.Configuration.TShockSettings.'.Length)
  } elseif ($full.StartsWith('Rests.SecureRest/TokenData.')) {
    $target = $secureRest
    $member = $full.Substring('Rests.SecureRest/TokenData.'.Length)
  } else {
    continue
  }

  $result = Apply-JsonPropertyName -Path $target -Member $member -JsonName $jsonName -Preview:$DryRun
  switch -Regex ($result) {
    '^ADD'     { $added++ }
    '^UPDATE'  { $updated++ }
    '^OK'      { $ok++ }
    '^MISSING' { $missing.Add($result) }
  }
}

Write-Host ('映射总数: ' + $entries.Count)
Write-Host ('新增: ' + $added)
Write-Host ('更新: ' + $updated)
Write-Host ('已存在正确: ' + $ok)
Write-Host ('缺失成员: ' + $missing.Count)
if ($missing.Count -gt 0) {
  $missing | ForEach-Object { Write-Warning $_ }
  exit 1
}
