# To Love · 本地 AI 聊天

一个**完全离线**运行的微信风格 AI 聊天程序：界面是仿微信（WinForms + WebBrowser + 单文件 HTML），
角色是《出包王女》的 14 个人物（可自建角色、可建群），推理用本地 [llama.cpp](https://github.com/ggml-org/llama.cpp)
（OpenAI 兼容接口），不联网、不调用任何云端 API。

- 模型：Qwen2.5-7B-Instruct（Q4_K_M，约 4.4 GB）
- 引擎：llama.cpp 的 `llama-server`（`127.0.0.1:11500`）
- 体积：**模型和引擎不进仓库**（见 `.gitignore`），用下面的脚本一键下载_

---

## 项目概览

| 部分 | 说明 |
|---|---|
| `app\本地AI对话.exe` | 聊天程序本体（C# WinForms 外壳 + WebBrowser 里跑 `wechat.html`） |
| `app\data\wechat.html` | 全部界面逻辑，单文件（会话、朋友圈、表情、游戏中心都在这） |
| `build\WeChatRito2.cs` | 聊天程序源码，编译产物就是上面的 exe |
| `build\games.js` | 四种棋的引擎源码，运行时内联进 `wechat.html` |
| `app\data\*.json` | 人设 / 知识卡 / 朋友圈内容 / 模型配置 |
| `启动本地AI对话.exe` | 启动器：探后端 → 起 `llama-server` → 打开聊天窗口 |

## 功能

**聊天**
- 微信式界面：会话列表、通讯录（拼音首字母分组）、搜索、会话置顶、未读小红点
- 14 个角色各有语气、口头禅、知识领域；回复克制在 10～35 字，会说「嗯」「行吧」，也会收话
- 表情面板、表情轮换（不老是同一个）
- 拍一拍：双击角色头像 → 灰字「你拍了拍 XX」→ 角色用自己的人设回你一句
- 群聊：选几个人建群，模型按「角色名：内容」分别发言
- 主动消息：闲置一段时间角色会自己找你

**朋友圈**
- 角色自己发动态、互相点赞评论；小条也能发表
- 点赞 / 评论 / 微信式「••」菜单

**小游戏（可边下边聊）**
- 五子棋、国际象棋、围棋、飞行棋
- 棋力按角色分 ★☆（小暗/古手川唯/梦梦/贾斯汀最难，小静最菜）
- 随机先后手、说「你先」让先、悔棋、认输、终局大字（WIN!/LOSE/平局）、再来一次
- 赛后角色会在聊天里说一句，还常常再邀一局；平时也会随机弹窗邀你

## 环境要求

- Windows 10 / 11（x64），**不需要显卡**也能跑（CPU 慢一些）
- .NET Framework 4.x（系统自带）
- 磁盘：模型约 4.4 GB，引擎约 0.4 GB
- 内存：7B Q4 建议 8 GB 以上

## 快速开始

### 方式一：一键脚本（推荐）

双击 **`下载依赖.bat`**，按提示选：

| 选项 | 干什么 |
|---|---|
| 1 | 下载 llama.cpp 引擎 + Qwen2.5-7B 模型（第一次用选这个） |
| 2 | 只补引擎（模型已经在 `models\qwen2.5-7b\` 里了） |
| 3 | 走 Ollama 方案（装 Ollama + `ollama pull qwen2.5:7b`） |
| 4 | 只检查现状，不下载 |

命令行等价用法：

```powershell
powershell -ExecutionPolicy Bypass -File setup.ps1              # 引擎 + 模型
powershell -ExecutionPolicy Bypass -File setup.ps1 -VerifyOnly  # 只检查
powershell -ExecutionPolicy Bypass -File setup.ps1 -SkipModel   # 只补引擎
powershell -ExecutionPolicy Bypass -File setup.ps1 -Engine cpu  # 强制 CPU 版引擎
powershell -ExecutionPolicy Bypass -File setup.ps1 -Mode ollama # Ollama 方案
```

脚本做的事：

1. 探测显卡（有 NVIDIA → CUDA 版引擎，否则 CPU 版；也可 `-Engine vulkan`）
2. 从 llama.cpp 官方 release 下 Windows 包，解压到 `ollama\lib\ollama\`
   （这个目录名是启动器里写死的，沿用旧布局）
3. 从 ModelScope 下官方两段式 GGUF 到 `models\qwen2.5-7b\`
   （`qwen2.5-7b-instruct-q4_k_m-00001/00002-of-00002.gguf`，与启动器路径一致）
4. 打印结果并写 `setup.log`

支持断点续传（`curl -C -`）；SSL 证书吊销检查被墙时会自动加 `--ssl-no-revoke` 重试。

### 方式二：手动准备

```
ollama\lib\ollama\llama-server.exe            ← 从 llama.cpp releases 的 llama-bXXXXX-bin-win-cpu-x64.zip 里解出来
ollama\lib\ollama\*.dll                       ← 同一个 zip 里的 DLL 一起解压
models\qwen2.5-7b\qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf
models\qwen2.5-7b\qwen2.5-7b-instruct-q4_k_m-00002-of-00002.gguf
```

也可以只放一个单文件模型（比如 `Qwen2.5-7B-Instruct-Q4_K_M.gguf`），重命名成
`...-00001-of-00002.gguf` 即可（llama.cpp 按文件内容判断，不看文件名）。

### 方式三：用 Ollama

```powershell
winget install Ollama.Ollama
ollama pull qwen2.5:7b
```

然后把 `app\data\config.json` 改成：

```json
{ "base_url": "http://127.0.0.1:11434/v1", "model": "qwen2.5:7b" }
```

双击 `app\本地AI对话.exe`（**不要**用启动器，启动器负责起的是 llama.cpp 引擎）。
`setup.ps1 -Mode ollama` 会自动完成这三步。

### 最后一步：启动

双击 **`启动本地AI对话.exe`**。它会：

1. 检查 `127.0.0.1:11500` 有没有后端
2. 没有就起 `ollama\lib\ollama\llama-server.exe -m models\... -c 4096 -t 8 -ngl 99 --no-webui`
3. 等模型加载（约 2 秒）后打开聊天窗口

## 配置

`app\data\config.json`：

| 字段 | 说明 |
|---|---|
| `base_url` | OpenAI 兼容接口地址，默认 `http://127.0.0.1:11500/v1` |
| `model` | 模型名，默认 `qwen2.5:7b`（llama-server 不校验，随便写也行） |
| `temperature` | 采样温度，默认 0.7～0.9 |
| `max_tokens` | 单条回复上限 |
| `system_prompt` | 全局系统提示（角色人设另在 `characters.json`） |

`app\data\characters.json` 是角色人设，`knowledge.json` 是每个角色的知识卡，
`moments.json` 是朋友圈内容——都可以直接改，改完重启窗口生效。

## 编译

聊天程序（**输出名必须是 `本地AI对话.exe`**，IE 内核版本按 exe 文件名写注册表）：

```
csc /nologo /utf8output /optimize+ /target:winexe /out:app\本地AI对话.exe ^
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
    build\WeChatRito2.cs
```

启动器：

```
csc /nologo /utf8output /optimize+ /target:winexe /out:启动本地AI对话.exe ^
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll build\ProgressLauncher2.cs
```

`build\games.js` 改完要**内联回** `app\data\wechat.html` 里 `/* 小游戏引擎` … `</script>` 那一段；
改了 `wechat.html` 或 exe 都要重启聊天窗口。

## 测试

```powershell
node dev\ui_test.js        # 界面逻辑（最小 DOM 桩，163 项）
node dev\game_test.js      # 四棋规则与 AI（45 项）
node dev\sim_flight.js     # 30 局飞行棋整局模拟
node dev\sim_ui.js         # 端到端对话模拟
```

C# 侧的单元测试：

```powershell
csc /nologo /utf8output /target:exe /main:ModelTest /out:dev\ModelTest.exe ^
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
    build\WeChatRito2.cs dev\ModelTest.cs

dev\ModelTest.exe unit http://127.0.0.1:11500/v1 qwen2.5:7b dummy.json
```

`dev\UiHost.cs` 是预览工具，可以不开主程序就把页面渲染成 PNG：

```powershell
csc /nologo /utf8output /target:winexe /out:dev\UiHost.exe ^
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll dev\UiHost.cs
dev\UiHost.exe app\data\wechat.html shot.png games
```

## 目录

```
启动本地AI对话.exe / 启动AI-进度版.exe   启动器
下载依赖.bat  setup.ps1                  依赖下载（引擎 + 模型）
app\本地AI对话.exe                       聊天程序
app\data\wechat.html                     界面与全部逻辑
app\data\characters.json / knowledge.json / moments.json / config.json
build\WeChatRito2.cs / games.js / *Launcher*.cs
dev\ui_test.js / game_test.js / sim_*.js / ModelTest.cs / UiHost.cs ...
```

## 常见问题

**端口 11500 被占用 / 起不来**
任务管理器里结束残留的 `llama-server.exe` 再启动；或改 `config.json` 的 `base_url` 和启动器里的 `-p`。

**显存不够**
把启动器参数 `-ngl 99` 改成 `-ngl 20`（部分层放显卡）或 `-ngl 0`（纯 CPU）。

**回复很慢**
CPU 跑 7B 大约 3～8 字/秒。可以先试 3B 模型（换成对应 gguf 路径即可），
或者用 CUDA 版引擎（`setup.ps1 -Engine cuda`）。

**下载很慢 / 中断**
脚本支持续传，重新跑一遍会接着下。GitHub 慢的话多试几次，或手动用镜像下同名文件放进对应目录。

**提示「模型还没准备好」**
`llama-server` 还在加载模型，等几秒；一直这样就看 `模型日志.txt` 和 `setup.log`。

**改了界面没反应**
`wechat.html` 在启动时会被复制到 `%TEMP%\rito_wechat_ui.html` 再加载，
**必须重启聊天窗口**才生效。

## 说明

- `models\`、`ollama\`、`app\_internal\` 不进仓库（模型权重和推理运行时，体积太大）
- `app\data\chat_*.json`（聊天记录）、`mem_*.json`（记忆）是本地生成的隐私数据，也已在 `.gitignore` 里排除
- 角色设定、台词均为同人创作，与原作者/版权方无关

## 许可

AGPL-3.0，见 [LICENSE](LICENSE)。
