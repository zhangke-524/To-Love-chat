# To Love · 本地 AI 聊天（《出包王女》角色）

一个**完全离线**运行的微信风格 AI 聊天程序：界面是仿微信（WebBrowser + HTML），角色是《出包王女》的
14 个人物（另可自建角色、群聊），推理用本地 llama.cpp（OpenAI 兼容接口），不联网、不调用云端 API。

## 功能

- **微信式界面**：会话列表 / 通讯录（拼音首字母分组）/ 朋友圈 / 表情面板 / 会话置顶 / 群聊
- **角色人设 + 知识卡**：每个角色有自己的语气、口头禅和专业知识（园艺、烘焙、护理、剑术…），
  回复克制在 10～35 字，会「收话」，不连续追问
- **朋友圈**：角色自己发动态、给小条点赞评论；小条也能发表
- **拍一拍**：双击角色头像 → 灰字「你拍了拍 XX」→ 角色用自己的人设回你一句
- **小游戏中心**：五子棋 / 国际象棋 / 围棋 / 飞行棋，邀请角色对局，棋力按角色分 ★☆，
  可边下边聊（气泡）、悔棋、认输、终局大字、再来一次；角色赛后还会主动发消息/再邀一局
- **主动消息**：闲置一段时间角色会自己找你
- **完全离线**：模型、引擎、数据都在本地

## 目录

```
启动本地AI对话.exe        启动器（探后端、起 llama-server、改 config、开窗口）
启动AI-进度版.exe         同一启动器的进度条外壳版
启动聊天-主动消息.vbs     只启动聊天窗口
主动消息.ps1              旧版主动消息脚本（程序内已内置，保留备用）
使用说明.txt              面向使用者的说明
app/
  本地AI对话.exe          聊天程序（C# WinForms + WebBrowser）
  data/
    wechat.html           UI 全部逻辑（单文件，含游戏引擎）
    characters.json       角色人设
    knowledge.json        每个角色的知识卡
    moments.json          朋友圈内容
    config.json           模型地址 / 模型名 / 温度等
    avatar.png            头像
build/
  WeChatRito2.cs          聊天程序源码（`app/本地AI对话.exe` 由它编译）
  games.js                四棋引擎源码（内联进 wechat.html）
  LauncherProgress.cs     启动器源码
  ProgressLauncher2.cs    进度条启动器源码
dev/                      开发与测试脚本（不参与运行）
  ui_test.js              界面逻辑测试（node，163 项）
  game_test.js            棋类规则测试（node，45 项）
  sim_flight.js           飞行棋整局模拟
  sim_ui.js / sim_ui2.js  端到端对话模拟
  ModelTest.cs            检测器/提示词单元测试（csc 编译后跑）
  UiHost.cs               预览截图工具（WebBrowser 渲染 + PrintWindow）
  LikeTest.cs / ShotHost.cs
```

## 运行

1. 让本地后端就绪：任意提供 OpenAI 兼容接口的服务，默认
   `http://127.0.0.1:11500/v1`、模型名 `qwen2.5:7b`（都在 `app/data/config.json` 里改）。
   本项目不含 `models/`、`ollama/`（体积太大，见 `.gitignore`），需要自己准备：
   ```
   llama-server -m <你的 gguf> -c 4096 --port 11500
   ```
2. 双击 `启动本地AI对话.exe`（它会顺带拉起 `llama-server` 并打开聊天窗口）。

## 编译

聊天程序（输出名必须保持 `本地AI对话.exe`，IE 内核版本按 exe 文件名写注册表）：

```
csc /nologo /utf8output /optimize+ /target:winexe /out:app\本地AI对话.exe ^
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
    build\WeChatRito2.cs
```

改了 `build/games.js` 或 `app/data/wechat.html` 后，把 `games.js` 内联回 html 的
`/* 小游戏引擎 */ … </script>` 之间即可；改 html / exe 都要重启聊天窗口才生效。

## 测试

```
node dev/ui_test.js         # 界面逻辑（最小 DOM 桩）
node dev/game_test.js       # 四棋规则与 AI
node dev/sim_flight.js      # 30 局飞行棋整局模拟
```

`dev/ModelTest.cs` 需要先编译：

```
csc /nologo /utf8output /target:exe /main:ModelTest /out:dev\ModelTest.exe ^
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
    build\WeChatRito2.cs dev\ModelTest.cs
dev\ModelTest.exe unit http://127.0.0.1:11500/v1 qwen2.5:7b dummy.json
```

## 说明

- `models/`、`ollama/`、`app/_internal/` 不进仓库（模型权重与推理运行时）。
- 聊天记录 `app/data/chat_*.json`、记忆 `mem_*.json` 是本地生成的隐私数据，也已在 `.gitignore` 里排除。
- 本仓库只放源码、配置、人设数据和测试；界面字体走系统 `Segoe UI Emoji` + 微软雅黑。

## 许可

AGPL-3.0，见 `LICENSE`。
