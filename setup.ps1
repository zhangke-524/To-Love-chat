#requires -version 5.1
<#
    To Love · 依赖下载脚本

    这个脚本把「跑起来需要的东西」下载到项目目录里，下载完就能直接双击启动器：

      ollama\lib\ollama\llama-server.exe   推理引擎（llama.cpp 的 Windows 版，启动器就是从这里起服务）
      models\qwen2.5-7b\*.gguf             模型（Qwen2.5-7B-Instruct Q4_K_M，约 4.4 GB）

    两种模式：
      -Mode llamacpp   （默认）下载 llama.cpp 引擎 + GGUF 模型，配合 启动本地AI对话.exe 使用
      -Mode ollama     安装 Ollama、拉取 qwen2.5:7b，并把 config.json 指到 Ollama 的接口

    常用：
      powershell -ExecutionPolicy Bypass -File setup.ps1 -VerifyOnly      只检查现状，不下载
      powershell -ExecutionPolicy Bypass -File setup.ps1 -SkipModel       只补引擎
      powershell -ExecutionPolicy Bypass -File setup.ps1 -Engine cpu      强制用 CPU 版引擎
      powershell -ExecutionPolicy Bypass -File setup.ps1 -Mode ollama     走 Ollama 方案
#>
param(
    [ValidateSet('llamacpp', 'ollama')]
    [string]$Mode = 'llamacpp',

    [ValidateSet('auto', 'cuda', 'cpu', 'vulkan')]
    [string]$Engine = 'auto',

    # split = 官方两段式（和启动器里的文件路径完全对应）；single = 单文件
    [ValidateSet('auto', 'split', 'single')]
    [string]$ModelFormat = 'auto',

    [string]$RepoRoot = '',          # 默认是本脚本所在目录
    [string]$OllamaModel = 'qwen2.5:7b',
    [switch]$SkipEngine,
    [switch]$SkipModel,
    [switch]$VerifyOnly,
    [switch]$Force,
    [switch]$NoCudaRuntime           # CUDA 引擎但不额外下 cudart（系统里已有 CUDA 运行库时用）
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---------------------------------------------------------------- 基础工具

$script:LogPath = $null

function Write-Step([string]$text) {
    Write-Host ''
    Write-Host ('==> ' + $text) -ForegroundColor Cyan
}

function Write-Ok([string]$text)   { Write-Host ('    ' + $text) -ForegroundColor Green }
function Write-Info([string]$text) { Write-Host ('    ' + $text) }
function Write-Warn([string]$text) { Write-Host ('    ' + $text) -ForegroundColor Yellow }

function Fail([string]$text) {
    Write-Host ''
    Write-Host ('[失败] ' + $text) -ForegroundColor Red
    exit 1
}

function Format-Size([double]$bytes) {
    if ($bytes -ge 1GB) { return ([math]::Round($bytes / 1GB, 2)).ToString() + ' GB' }
    if ($bytes -ge 1MB) { return ([math]::Round($bytes / 1MB, 1)).ToString() + ' MB' }
    return ([math]::Round($bytes / 1KB, 0)).ToString() + ' KB'
}

function Get-FileSize([string]$path) {
    if (Test-Path -LiteralPath $path) { return (Get-Item -LiteralPath $path).Length }
    return 0
}

# 带断点续传的下载（优先 curl.exe，回退 .NET WebClient / Invoke-WebRequest）
function Get-Remote([string]$url, [string]$dest) {
    $dir = Split-Path -Parent $dest
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $before = Get-FileSize $dest
    Write-Info ('下载 ' + $url)
    if ($before -gt 0) { Write-Info ('  已有 ' + (Format-Size $before) + '，尝试续传') }

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    $downloaded = $false
    if ($curl) {
        # --ssl-no-revoke：有些机器的证书吊销检查被墙，不加会直接报 CRYPT_E_NO_REVOCATION_CHECK
        $curlArgs = @('-L', '--fail', '--ssl-no-revoke', '--retry', '3', '--retry-delay', '2',
                      '-C', '-', '-o', $dest, $url)
        & $curl.Source @curlArgs
        $code = $LASTEXITCODE
        if ($code -eq 0) {
            $downloaded = $true
        } else {
            Write-Warn ('curl 退出码 ' + $code + '，去掉证书校验再试一次')
            & $curl.Source @('-L', '--fail', '-k', '--retry', '2', '-C', '-', '-o', $dest, $url)
            if ($LASTEXITCODE -eq 0) { $downloaded = $true }
            else { Fail ('下载失败（curl 退出码 ' + $LASTEXITCODE + '）：' + $url) }
        }
    }
    if (-not $downloaded) {
        try {
            $wc = New-Object System.Net.WebClient
            $wc.Headers.Add('User-Agent', 'to-love-setup')
            $wc.DownloadFile($url, $dest)
            $wc.Dispose()
        } catch {
            Fail ('下载失败：' + $url + '  原因：' + $_.Exception.Message)
        }
    }

    $after = Get-FileSize $dest
    if ($after -le 0) { Fail ('下载后文件是空的：' + $dest) }
    Write-Ok ('完成 ' + (Format-Size $after) + ' -> ' + $dest)
}

# 下载并解压 zip 到指定目录（覆盖同名文件）
function Expand-RemoteZip([string]$url, [string]$destDir, [string]$name) {
    $tmp = Join-Path $env:TEMP ('to_love_' + $name + '.zip')
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    Get-Remote $url $tmp
    if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    Write-Info ('解压到 ' + $destDir)
    Expand-Archive -LiteralPath $tmp -DestinationPath $destDir -Force
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    Write-Ok '解压完成'
}

# 探测显卡，决定默认引擎版本
function Resolve-EngineKind {
    if ($Engine -ne 'auto') { return $Engine }
    $smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($smi) {
        try {
            $out = & $smi.Source -L 2>$null
            if ($out -and ($out -match 'GPU')) {
                Write-Info '检测到 NVIDIA 显卡，使用 CUDA 引擎'
                return 'cuda'
            }
        } catch { }
    }
    Write-Info '没检测到 NVIDIA 显卡，使用 CPU 引擎（也能跑，就是慢一些）'
    return 'cpu'
}

# ---------------------------------------------------------------- 项目路径

if (-not $RepoRoot) {
    if ($PSScriptRoot) { $RepoRoot = $PSScriptRoot } else { $RepoRoot = (Get-Location).Path }
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

$EngineDir = Join-Path $RepoRoot 'ollama\lib\ollama'      # 启动器里写死的引擎目录
$ModelDir  = Join-Path $RepoRoot 'models\qwen2.5-7b'     # 启动器里写死的模型目录
$ModelPart1 = Join-Path $ModelDir 'qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf'
$ModelPart2 = Join-Path $ModelDir 'qwen2.5-7b-instruct-q4_k_m-00002-of-00002.gguf'
$AppExe     = Join-Path $RepoRoot 'app\本地AI对话.exe'
$Launcher   = Join-Path $RepoRoot '启动本地AI对话.exe'
$ConfigPath = Join-Path $RepoRoot 'app\data\config.json'

$script:LogPath = Join-Path $RepoRoot 'setup.log'

Write-Host ''
Write-Host '  To Love · 依赖下载脚本' -ForegroundColor White
Write-Host '  ----------------------' -ForegroundColor DarkGray
Write-Info ('项目目录：' + $RepoRoot)
$verifyTag = ''
if ($VerifyOnly) { $verifyTag = '   仅检查' }
Write-Info ('模式：' + $Mode + '   引擎：' + $Engine + $verifyTag)
try { Start-Transcript -Path $script:LogPath -Append | Out-Null } catch { }

# ---------------------------------------------------------------- 现状检查

function Test-Installed {
    $engine = Test-Path -LiteralPath (Join-Path $EngineDir 'llama-server.exe')
    $size1 = Get-FileSize $ModelPart1
    $size2 = Get-FileSize $ModelPart2
    $hasModel = ($size1 -gt 1GB)                       # 两段式第一段 3.7 GB / 单文件 4.4 GB 都满足
    $hasPart2 = ($size2 -gt 100MB)

    $engineText = '缺失'
    if ($engine) { $engineText = '已就位' }
    $modelText = '缺失'
    if ($hasModel) { $modelText = '已就位 ' + (Format-Size $size1) }
    $part2Text = '缺失（单文件方案不需要）'
    if ($hasPart2) { $part2Text = '已就位 ' + (Format-Size $size2) }
    $appText = '缺 app\本地AI对话.exe'
    if (Test-Path -LiteralPath $AppExe) { $appText = '在' }

    Write-Host ''
    Write-Host '  当前状态' -ForegroundColor White
    Write-Info ('  引擎  ' + $engineText + '  ' + (Join-Path $EngineDir 'llama-server.exe'))
    Write-Info ('  模型1 ' + $modelText + '  ' + $ModelPart1)
    Write-Info ('  模型2 ' + $part2Text)
    Write-Info ('  程序  ' + $appText)
    return [pscustomobject]@{ Engine = $engine; Model = $hasModel; ModelPart2 = $hasPart2 }
}

$state = Test-Installed

if ($VerifyOnly) {
    Write-Step '仅检查模式，不做任何下载'
    if ($state.Engine -and $state.Model) {
        Write-Ok '依赖齐全，可以双击「启动本地AI对话.exe」'
    } else {
        Write-Warn '依赖不齐，去掉 -VerifyOnly 再跑一次就会开始下载'
    }
    try { Stop-Transcript | Out-Null } catch { }
    exit 0
}

# ---------------------------------------------------------------- Ollama 模式

function Install-Ollama {
    Write-Step '安装 Ollama'

    $cli = Get-Command ollama -ErrorAction SilentlyContinue
    if (-not $cli) {
        $candidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'),
            (Join-Path $env:ProgramFiles 'Ollama\ollama.exe')
        )
        foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { $cli = Get-Item -LiteralPath $c; break } }
    }

    if ($cli) {
        Write-Ok ('已安装：' + $cli.Source)
    } else {
        $winget = Get-Command winget -ErrorAction SilentlyContinue
        if ($winget) {
            Write-Info '用 winget 安装 Ollama.Ollama …'
            & $winget.Source install --id Ollama.Ollama -e --accept-source-agreements --accept-package-agreements
            if ($LASTEXITCODE -ne 0) { Write-Warn ('winget 失败（退出码 ' + $LASTEXITCODE + '），改用手动安装包') }
        } else {
            Write-Warn '没有 winget，直接下安装包'
        }

        if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
            $setup = Join-Path $env:TEMP 'OllamaSetup.exe'
            if (-not (Test-Path -LiteralPath $setup) -or $Force) {
                Get-Remote 'https://ollama.com/download/OllamaSetup.exe' $setup
            }
            Write-Info '运行安装包（会弹窗，点几下装完就行）…'
            Start-Process -FilePath $setup -Wait
        }
    }

    $ollama = $null
    foreach ($p in @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'),
        (Join-Path $env:ProgramFiles 'Ollama\ollama.exe'))) {
        if (Test-Path -LiteralPath $p) { $ollama = $p; break }
    }
    if (-not $ollama) {
        $cmd = Get-Command ollama -ErrorAction SilentlyContinue
        if ($cmd) { $ollama = $cmd.Source }
    }
    if (-not $ollama) { Fail '找不到 ollama.exe，装完请重开一个命令行窗口再跑本脚本' }
    Write-Ok ('ollama: ' + $ollama)
    return $ollama
}

function Start-OllamaServe([string]$ollama) {
    # 已经能连上就不重复起
    try {
        $r = Invoke-WebRequest -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 3 -UseBasicParsing
        if ($r.StatusCode -eq 200) { Write-Ok 'Ollama 服务已在运行'; return }
    } catch { }

    Write-Info '启动 ollama serve（后台）…'
    Start-Process -FilePath $ollama -ArgumentList 'serve' -WindowStyle Hidden
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        try {
            $r = Invoke-WebRequest -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2 -UseBasicParsing
            if ($r.StatusCode -eq 200) { Write-Ok '服务就绪'; return }
        } catch { }
    }
    Fail 'ollama serve 没起来，请手动运行 `ollama serve` 看看报错'
}

function Set-ConfigTo([string]$baseUrl, [string]$model) {
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        Write-Warn ('没有 ' + $ConfigPath + '，跳过配置修改')
        return
    }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmm'
    Copy-Item -LiteralPath $ConfigPath -Destination ($ConfigPath + '.bak-' + $stamp) -Force
    $cfg = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $cfg.base_url = $baseUrl
    $cfg.model = $model
    $json = $cfg | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($ConfigPath, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Ok ('config.json 已改为 ' + $baseUrl + ' / ' + $model + '（备份 .bak-' + $stamp + '）')
}

if ($Mode -eq 'ollama') {
    $ollama = Install-Ollama
    Start-OllamaServe $ollama

    Write-Step ('拉取模型 ' + $OllamaModel + '（第一次要下 4 GB 左右）')
    & $ollama pull $OllamaModel
    if ($LASTEXITCODE -ne 0) { Fail ('ollama pull 失败（退出码 ' + $LASTEXITCODE + '）') }
    Write-Ok '模型已就绪'

    Write-Step '把聊天程序指向 Ollama'
    Set-ConfigTo 'http://127.0.0.1:11434/v1' $OllamaModel

    Write-Step '完成'
    Write-Info '现在直接双击 app\本地AI对话.exe 就能用（Ollama 在托盘里自己起服务）。'
    Write-Info '注意：这种方式不要用「启动本地AI对话.exe」，它负责起的是 llama.cpp 引擎。'
    try { Stop-Transcript | Out-Null } catch { }
    exit 0
}

# ---------------------------------------------------------------- llama.cpp 模式

function Get-LlamaRelease {
    Write-Step '查询 llama.cpp 的最新 Windows 构建'
    $headers = @{ 'User-Agent' = 'to-love-setup' }
    $rels = Invoke-RestMethod -Uri 'https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=20' -Headers $headers
    foreach ($r in $rels) {
        $has = $r.assets | Where-Object { $_.name -match '^llama-b\d+-bin-win-cpu-x64\.zip$' }
        if ($has) {
            Write-Ok ('使用 ' + $r.tag_name)
            return $r
        }
    }
    Fail '在最近 20 个 release 里没找到 Windows 二进制包（可能被代理挡了，稍后重试）'
}

function Pick-Asset($rel, [string]$pattern, [string]$what) {
    $a = $rel.assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
    if (-not $a) { Fail ('这个 release 里没有 ' + $what + '（' + $pattern + '）') }
    return $a
}

if (-not $SkipEngine) {
    $kind = Resolve-EngineKind
    $rel = Get-LlamaRelease

    switch ($kind) {
        'cuda'   { $asset = Pick-Asset $rel '^llama-b\d+-bin-win-cuda-12\.4-x64\.zip$' 'CUDA 版引擎' }
        'vulkan' { $asset = Pick-Asset $rel '^llama-b\d+-bin-win-vulkan-x64\.zip$' 'Vulkan 版引擎' }
        default  { $asset = Pick-Asset $rel '^llama-b\d+-bin-win-cpu-x64\.zip$' 'CPU 版引擎' }
    }

    if ($state.Engine -and -not $Force) {
        Write-Step '引擎已经存在，跳过（要重装加 -Force）'
        Write-Info ('版本：' + (Split-Path -Leaf $asset.name))
    } else {
        Write-Step ('下载引擎 ' + $asset.name + '（' + (Format-Size $asset.size) + '）')
        Expand-RemoteZip $asset.browser_download_url $EngineDir 'engine'

        if ($kind -eq 'cuda' -and -not $NoCudaRuntime) {
            $cudart = Pick-Asset $rel '^cudart-llama-bin-win-cuda-12\.4-x64\.zip$' 'CUDA 运行库'
            Write-Step ('下载 CUDA 运行库 ' + $cudart.name + '（' + (Format-Size $cudart.size) + '）')
            Expand-RemoteZip $cudart.browser_download_url $EngineDir 'cudart'
        }

        if (-not (Test-Path -LiteralPath (Join-Path $EngineDir 'llama-server.exe'))) {
            Fail '装完还是找不到 llama-server.exe，看看解压出来的目录结构'
        }
        Write-Ok '引擎就位：' + (Join-Path $EngineDir 'llama-server.exe')
    }
} else {
    Write-Step '跳过引擎（-SkipEngine）'
}

if (-not $SkipModel) {
    $haveModel = (Get-FileSize $ModelPart1) -gt 1GB
    if ($haveModel -and -not $Force) {
        Write-Step ('模型已经存在，跳过（' + (Format-Size (Get-FileSize $ModelPart1)) + '，要重下加 -Force）')
    } else {
        if ($ModelFormat -eq 'auto') {
            $ModelFormat = 'split'
            Write-Info '默认用官方两段式文件（和启动器里的路径一致）'
        }

        if ($ModelFormat -eq 'split') {
            $base = 'https://modelscope.cn/models/Qwen/Qwen2.5-7B-Instruct-GGUF/resolve/master/'
            Write-Step '下载模型 part 1/2（约 3.7 GB）'
            Get-Remote ($base + 'qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf') $ModelPart1
            Write-Step '下载模型 part 2/2（约 0.64 GB）'
            Get-Remote ($base + 'qwen2.5-7b-instruct-q4_k_m-00002-of-00002.gguf') $ModelPart2
        } else {
            Write-Step '下载单文件模型（约 4.4 GB，hf-mirror）'
            Get-Remote 'https://hf-mirror.com/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf' $ModelPart1
        }

        $sz = Get-FileSize $ModelPart1
        if ($sz -lt 1GB) { Fail ('模型文件太小（' + (Format-Size $sz) + '），大概是下载没完成') }
        Write-Ok ('模型就位：' + $ModelPart1 + '  ' + (Format-Size $sz))
    }
} else {
    Write-Step '跳过模型（-SkipModel）'
}

# ---------------------------------------------------------------- 收尾

$final = Test-Installed
Write-Step '完成'
if ($final.Engine -and $final.Model) {
    Write-Ok '依赖齐全，双击「启动本地AI对话.exe」即可开始聊天。'
    Write-Info '启动器会自动起 llama-server（127.0.0.1:11500），窗口会在模型加载完后出现（约 2 秒）。'
} else {
    Write-Warn '还有缺的东西，检查上面的输出。'
}
Write-Info ('日志：' + $script:LogPath)
try { Stop-Transcript | Out-Null } catch { }
