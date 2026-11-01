param(
  [string]$ApiBase = "",
  [int]$FirstSeconds = 180,
  [int]$MinIntervalSeconds = 900,
  [int]$MaxIntervalSeconds = 1200
)

$ErrorActionPreference = "Stop"
$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$dataDir = Join-Path $root "app\data"
$cfgPath = Join-Path $dataDir "config.json"
$convPath = Join-Path $dataDir "conversations.json"
$pendingPath = Join-Path $dataDir "proactive_pending.json"
$chatPath = Join-Path $dataDir "chat_rito.json"
$logPath = Join-Path $root "主动消息日志.txt"
$appName = "本地AI对话"

$script:api = $ApiBase
$script:model = "qwen2.5:7b"
$script:persona = ""

function Write-Log([string]$msg) {
  try {
    $line = "{0} {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg
    [IO.File]::AppendAllText($logPath, $line + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
  } catch {}
}

function Read-JsonFile([string]$path) {
  $raw = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
  if ($raw.Length -gt 0 -and [int]$raw[0] -eq 0xFEFF) { $raw = $raw.Substring(1) }
  return ($raw | ConvertFrom-Json)
}

function Write-JsonFile([string]$path, $obj, [switch]$AsArray) {
  $json = $obj | ConvertTo-Json -Depth 12
  if ($AsArray -and -not $json.TrimStart().StartsWith("[")) {
    $json = "[`r`n" + $json + "`r`n]"
  }
  [IO.File]::WriteAllText($path, $json + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
}

function Get-AppProcess {
  return (Get-Process -Name $appName -ErrorAction SilentlyContinue)
}

function Test-Api {
  try {
    Invoke-RestMethod -Uri ($script:api.TrimEnd("/") + "/models") -TimeoutSec 5 | Out-Null
    return $true
  } catch { return $false }
}

function Get-TargetConversation {
  $convs = @(Read-JsonFile $convPath)
  return ($convs | Sort-Object -Property updated -Descending | Select-Object -First 1)
}

function Get-RecentMessages {
  try {
    if (Test-Path $chatPath) {
      $flat = @(Read-JsonFile $chatPath) | Where-Object { $_.role -in @("user", "assistant") -and $_.content }
      return @($flat | Select-Object -Last 8)
    }
    $target = Get-TargetConversation
    if (-not $target) { return @() }
    $msgs = @($target.messages) | Where-Object { $_.role -in @("user", "assistant") -and $_.content }
    return @($msgs | Select-Object -Last 8)
  } catch { return @() }
}

function Merge-Pending {
  if (-not (Test-Path $pendingPath)) { return }
  try {
    $pending = @(Read-JsonFile $pendingPath)
    $pending = @($pending | Where-Object { $_.content })
    if ($pending.Count -eq 0) { Remove-Item $pendingPath -Force; return }
    if (Test-Path $chatPath) {
      $hist = @(Read-JsonFile $chatPath)
      foreach ($p in ($pending | Sort-Object ts)) {
        $hist += [pscustomobject]@{ role = "assistant"; content = [string]$p.content; ts = [long]$p.ts }
      }
      $hist = @($hist | Sort-Object -Property @{ Expression = { [long]$_.ts } })
      $backup = $chatPath + ".bak"
      Copy-Item -LiteralPath $chatPath -Destination $backup -Force
      Write-JsonFile $chatPath $hist -AsArray
      Read-JsonFile $chatPath | Out-Null
      Remove-Item $pendingPath -Force
      Write-Log ("merged {0} message(s) into wechat history" -f $pending.Count)
      return
    }
    $convs = @(Read-JsonFile $convPath)
    $target = $convs | Sort-Object -Property updated -Descending | Select-Object -First 1
    if (-not $target) { return }
    $newMsgs = @($target.messages)
    foreach ($p in ($pending | Sort-Object ts)) {
      $newMsgs += [pscustomobject]@{
        role = "assistant"
        content = [string]$p.content
        reasoning = ""
        error = ""
        ts = [long]$p.ts
      }
    }
    $newMsgs = @($newMsgs | Sort-Object -Property @{ Expression = { [long]$_.ts } })
    $target.messages = $newMsgs
    $maxTs = ($pending | Measure-Object -Property ts -Maximum).Maximum
    if ([long]$maxTs -gt [long]$target.updated) { $target.updated = [long]$maxTs }
    $backup = $convPath + ".bak"
    Copy-Item -LiteralPath $convPath -Destination $backup -Force
    Write-JsonFile $convPath $convs -AsArray
    Read-JsonFile $convPath | Out-Null
    Remove-Item $pendingPath -Force
    Write-Log ("merged {0} message(s)" -f $pending.Count)
  } catch {
    Write-Log ("merge failed: " + $_.Exception.Message)
    if (Test-Path ($convPath + ".bak")) { Copy-Item ($convPath + ".bak") $convPath -Force }
  }
}

function Invoke-ChatRaw([string]$url, [byte[]]$bytes) {
  $req = [System.Net.HttpWebRequest]::Create($url)
  $req.Method = "POST"
  $req.ContentType = "application/json; charset=utf-8"
  $req.Timeout = 300000
  $req.ReadWriteTimeout = 300000
  $req.ContentLength = $bytes.Length
  $stream = $req.GetRequestStream()
  $stream.Write($bytes, 0, $bytes.Length)
  $stream.Close()
  $resp = $req.GetResponse()
  try {
    $rs = $resp.GetResponseStream()
    $ms = New-Object System.IO.MemoryStream
    $rs.CopyTo($ms)
    $json = [Text.Encoding]::UTF8.GetString($ms.ToArray())
  } finally {
    $resp.Close()
  }
  return ($json | ConvertFrom-Json)
}

function New-ProactiveMessage {
  $history = Get-RecentMessages
  $list = New-Object System.Collections.ArrayList
  [void]$list.Add(@{ role = "system"; content = $script:persona })
  foreach ($m in $history) {
    [void]$list.Add(@{ role = [string]$m.role; content = [string]$m.content })
  }
  if (Test-Path $pendingPath) {
    foreach ($pm in @(Read-JsonFile $pendingPath)) {
      if ($pm.content) { [void]$list.Add(@{ role = "assistant"; content = [string]$pm.content }) }
    }
  }
  $hint = '（现在小条没有在跟你说话。你想主动找他说一句：一两句，像平时发消息那样，说说自己这边的小事、随口一句、吐槽或者关心都行；换个跟前面不一样的话题，不要重复最近说过的话或已经发过的消息，不要用括号写动作，不要加「我：」，不要连着问问题。）'
  [void]$list.Add(@{ role = "system"; content = $hint })
  $body = @{
    model = $script:model
    messages = $list
    temperature = 0.8
    max_tokens = 120
    stream = $false
  } | ConvertTo-Json -Depth 10
  $bytes = [Text.Encoding]::UTF8.GetBytes($body)
  $resp = Invoke-ChatRaw ($script:api.TrimEnd("/") + "/chat/completions") $bytes
  $text = ([string]$resp.choices[0].message.content).Trim()
  $text = $text -replace '^我[:：]\s*', ''
  $text = $text -replace '^["\u201C]', '' -replace '["\u201D]$', ''
  return $text.Trim()
}

if (Test-Path $cfgPath) {
  try {
    $cfg = Read-JsonFile $cfgPath
    if (-not $script:api) { $script:api = [string]$cfg.base_url }
    if ($cfg.model) { $script:model = [string]$cfg.model }
    if ($cfg.system_prompt) { $script:persona = [string]$cfg.system_prompt }
  } catch { Write-Log ("config read failed: " + $_.Exception.Message) }
}
if (-not $script:api) { $script:api = "http://127.0.0.1:11500/v1" }

$mutex = New-Object System.Threading.Mutex($false, "Local\LocalAIChatProactiveHelper")
try { $gotLock = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $gotLock = $true }
if (-not $gotLock) { exit }

Write-Log "helper start"
if (-not (Get-AppProcess)) { Merge-Pending }

$deadline = (Get-Date).AddSeconds(600)
while (-not (Get-AppProcess) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
if (-not (Get-AppProcess)) { Merge-Pending; Write-Log "app never started, exit"; exit }

$next = (Get-Date).AddSeconds($FirstSeconds)
while ($true) {
  if (-not (Get-AppProcess)) {
    Start-Sleep -Seconds 2
    if (-not (Get-AppProcess)) {
      Merge-Pending
      Write-Log "app closed, exit"
      break
    }
  }
  if ((Get-Date) -ge $next) {
    if (Test-Api) {
      try {
        $text = New-ProactiveMessage
        if ($text -and $text.Length -ge 2) {
          $pending = @()
          if (Test-Path $pendingPath) { $pending = @(Read-JsonFile $pendingPath) }
          $item = [pscustomobject]@{
            content = $text
            ts = [long]([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
          }
          $pending = @($pending) + @($item)
          Write-JsonFile $pendingPath $pending -AsArray
          Write-Log ("generated: " + $text)
        } else {
          Write-Log "empty result"
        }
        $next = (Get-Date).AddSeconds((Get-Random -Minimum $MinIntervalSeconds -Maximum ($MaxIntervalSeconds + 1)))
      } catch {
        Write-Log ("generate failed: " + $_.Exception.Message)
        $next = (Get-Date).AddSeconds(120)
      }
    } else {
      Write-Log "api not ready"
      $next = (Get-Date).AddSeconds(20)
    }
  }
  Start-Sleep -Seconds 3
}
