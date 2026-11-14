<p align="center">
  <img src="design/tray-icon/png/icon-256.png" width="88" alt="QuickTranslate">
</p>

<h1 align="center">QuickTranslate</h1>

<p align="center"><strong>选中文字，按一下快捷键，译文就浮现在光标旁。<br>没有广告，没有弹窗，没有会员，没有用不上的功能。</strong></p>

<p align="center">
  <a href="https://github.com/fu-roc/QuickTranslate/releases/latest"><img src="https://img.shields.io/github/v/release/fu-roc/QuickTranslate?color=2F6BED&amp;label=release" alt="Latest release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2B-blue.svg" alt="Windows 10 or later">
  <img src="https://img.shields.io/badge/.NET-10-purple.svg" alt=".NET 10">
</p>

<p align="center">
  <a href="https://github.com/fu-roc/QuickTranslate/releases/latest"><strong>下载最新版</strong></a>
  &nbsp;·&nbsp; <a href="#三步上手">3 步上手</a>
  &nbsp;·&nbsp; <a href="#常见问题">常见问题</a>
  &nbsp;·&nbsp; <a href="#开发者与维护者">技术细节</a>
</p>

---

想查一个单词，却要先关掉词典客户端的广告、会员弹窗和每日推荐？QuickTranslate 就是为这件事做的：一个只驻系统托盘的 Windows 小工具，平时看不见，选中文本按 `Alt+T` 就出译文；遇到不能复制的界面，框选一块屏幕走本地 OCR。

**一个单文件 exe，不开浏览器内核，不常驻窗口，不后台联网，[MIT 开源](LICENSE)。**

> **开始之前**：翻译走百度翻译开放平台，需要填一次你自己的 APPID / 密钥（个人认证后有免费额度，日常划词基本用不完）。这是一次性配置，不是注册软件账号，也不用在软件里登录。

## 为什么用它

| | 常见词典客户端 | QuickTranslate |
| --- | --- | --- |
| 广告 / 会员弹窗 | 有 | **没有** |
| 软件账号 | 通常要注册登录 | 不用，只填一次自己的 API 密钥 |
| 平时占屏 | 常驻窗口、悬浮条 | 只驻托盘，用时才出现 |
| 功能范围 | 词典、背单词、课程、资讯…… | 只做翻译 |
| 后台联网 | 自动检查更新、上报统计 | **只在翻译、以及你点「检查更新」时联网** |
| 费用 | 免费版有广告 / 会员订阅 | 开源免费，用你自己的百度免费额度 |
| 源码 | 闭源 | 可自己改、自己编译 |

## 三步上手

1. **下载并运行** — 到 [Releases](https://github.com/fu-roc/QuickTranslate/releases/latest) 拿 `...-win-x64-setup-*.exe`（系统级安装，需 UAC 授权）或 `...-framework-dependent.zip`（解压即用）。首次启动会自动打开设置窗口。
2. **填一次密钥** — 打开 <https://fanyi-api.baidu.com/>，注册并完成个人认证，开通「通用文本翻译 API」，把 APPID 和密钥填进设置页保存。**这一步只做一次。**
3. **开始用** — 在浏览器 / PDF / IDE 里选中一段英文，按 `Alt+T`。译文立刻出现在光标旁，点一下译文即复制。

想手动输入？**左键单击托盘图标**打开双栏主界面：左边写原文、右边看译文，`Ctrl+Enter` 翻译，源语言和目标语言都能单独选并记住。托盘右键还有设置、检查更新、退出。

> 环境：Windows 10 2004（build 19041）或更高（已在 Windows 11 验证），需要 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)；截图 OCR 还需要系统安装对应语言的 OCR 语言包（设置 → 时间和语言 → 语言和区域）。

## 它能做什么

| 能力 | 怎么用 | 说明 |
| --- | --- | --- |
| 划词翻译 | 选中文字 → `Alt+T` | 模拟 `Ctrl+C` 取词，译文浮现在光标旁，**不抢走你正在打字的焦点** |
| 截图 OCR | `Alt+S` → 框选屏幕 | `Windows.Media.Ocr` 本地识别，图片全程只在内存里流转，适合不能复制的界面 |
| 双栏主界面 | 单击托盘图标 | 手动输入翻译，源语言 / 译文语言分别可选，`Ctrl+Enter` 触发，关闭只隐藏 |
| 悬浮结果窗 | 自动出现 | 点击外部即关闭；点译文即复制；出错给中文原因和「重试 / 打开设置」，不甩英文异常 |
| 自定义快捷键 | 设置页点「录制」 | 直接按下想要的组合键即可，冲突、系统保留组合都会给中文提示 |
| 标识符自动拆分 | 无需开关 | 见下 |
| 更多 | 托盘右键 | 开机自启（可选）、手动检查更新、退出 |

**一个用起来很爽的细节**：代码里的标识符，百度往往整串当成生造词。QuickTranslate 会在发送前把它还原成自然语言 ——

| 选中的原文 | 原样发给百度 | QuickTranslate |
| --- | --- | --- |
| `hello_world_this_is_a_sentence` | 你好世界这句话 | 你好，世界，这是一个句子 |
| `getUserNameById` | `getUserNameById`（原样返回） | 按 Id 获取用户名 |
| `HTTPServerResponseCode` | HTTPServer响应码 | HTTP 服务器响应代码 |
| `MAX_RETRY_COUNT` | `MAX_RETRY_COUNT`（原样返回） | 最大重试次数 |

规则收得很紧：只有整串无空白、且确实存在 `_` / `-` 或驼峰边界才改写；普通英文句子、中文、URL、文件路径、代码行一律原样发送，界面上显示的原文也不变。划词、OCR、主界面三条链路都生效。

**隐私与本地数据**：密钥用 Windows DPAPI（当前账户）加密后存进 `%APPDATA%\QuickTranslate\config.json`，换账户或换机器无法解密；配置原子写盘，损坏时先备份再提示。日志只记异常与关键状态，**不记翻译原文 / APPID / 密钥**，超过 1 MB 自动轮转。翻译文本本身会发送给百度 API（这是翻译服务的性质），除此之外程序不上传任何东西。

## 下载

到 [GitHub Releases](https://github.com/fu-roc/QuickTranslate/releases/latest) 下最新版，两种产物内容相同，**二选一**：

| 产物 | 适合谁 | 说明 |
| --- | --- | --- |
| `...-framework-dependent.zip` 便携版（约 6 MB） | 想绿色免安装 | 解压到固定目录，双击单文件 exe 即用；不写安装信息、不建卸载入口，升级就是替换 exe |
| `...-win-x64-setup-*.exe` 安装包（约 5.5 MB） | 想要开始菜单和卸载入口 | 中文向导，默认装到 `C:\Program Files\QuickTranslate`，**系统级安装，双击时需 UAC 管理员授权**；可在「设置 → 应用」卸载，卸载不删配置 |

两者都依赖 .NET 10 Desktop Runtime。注意区分：**安装过程**需要管理员权限，**装好的程序**仍以普通权限（`asInvoker`）常驻托盘，不会请求提权。安装包不会创建开机自启项，需要请在设置页勾选。

覆盖升级直接用新版安装包再装一次即可，**不用先卸载**，也**不用手动退出**正在运行的托盘程序 —— 安装包会在复制文件前自动结束旧实例，装完可勾选「运行 QuickTranslate」重新拉起。

## 常见问题

**要付费吗？** 软件本身 MIT 开源免费。百度翻译 API 个人认证后有免费额度，日常划词基本用不完，超出按百度规则计费。

**双击提示「文件包含病毒或潜在的垃圾软件」/ SmartScreen？** 未签名 exe 的常见误报，不是病毒。原因是程序没有代码签名、全网没有下载信誉记录。按 [Defender / SmartScreen 处置步骤](#defender) 放行即可；根治办法是购买代码签名证书或向微软提交误报申诉。

**哪些软件里能用？** 浏览器、PDF 阅读器、IDE、Office 等只要能选中并复制文本都能用。管理员权限运行的窗口会被系统 UIPI 拦截，部分终端把 `Ctrl+C` 当中断信号，禁止复制的软件取不到词 —— 这些情况改用 `Alt+S` 截图 OCR。

**剪贴板会被改吗？** 会。取词靠模拟 `Ctrl+C`，按 `Alt+T` 后剪贴板里是选中的原文；点译文会把译文写进剪贴板。程序不主动恢复原有内容。

**断网能用吗？** 翻译必须联网（走百度 API）。本地 OCR 识别本身不联网，但识别完仍需翻译。

**支持 macOS / Linux 吗？** 目前只支持 Windows（WPF + Win32 全局热键 + `Windows.Media.Ocr`）。

**多显示器 / 不同 DPI？** 划词不受影响；截图框选只覆盖光标所在那一块屏幕，不支持跨屏拖拽。截图像素按物理坐标计算，不受缩放影响。

**能翻多长的文本？** 按 UTF-8 6000 字节自动分段（汉字约 2000 个），最多 8 段，超长会明确提示而不是静默截断。

## 已知限制

这些是刻意的设计取舍，不是 bug，完整说明见[技术细节](#开发者与维护者)：

- 只支持 Windows；翻译需联网且需自备百度 API 密钥
- 取词依赖模拟 `Ctrl+C`，不恢复原剪贴板内容；少数禁止复制的场景要用截图 OCR
- 悬浮结果窗为了「不抢焦点」，需要光标移入后 `Esc` 才生效；点击窗口外、鼠标移开 0.8 秒、12 秒超时都能关闭它
- 未做代码签名，首次运行可能被安全软件拦；日志按大小轮转，磁盘占用上限约 2 MB
- 开机自启默认关闭；更新检查只在手动点击时联网，不自动下载、不自动安装

---

## 开发者与维护者

以下内容供二次开发、排障和打包使用，日常使用无需阅读。

<details>
<summary><strong>配置文件与密钥加密</strong></summary>

配置在 `%APPDATA%\QuickTranslate\config.json`（**不要把填好密钥的文件提交到任何仓库**）：

```json
{
  "AppId": "你的APPID",
  "Secret": "dpapi:v1:……当前 Windows 账户加密后的密文……",
  "TakeWordHotkey": "Alt+T",
  "OcrHotkey": "Alt+S",
  "TargetLanguage": "zh",
  "MainWindowSourceLanguage": "auto",
  "MainWindowTargetLanguage": ""
}
```

`TargetLanguage` 是划词 / 截图 OCR 的目标语言；`MainWindowSourceLanguage` / `MainWindowTargetLanguage` 是主界面**独立**记住的语种偏好，后者留空表示还没在主界面单独选过、暂时跟随 `TargetLanguage`。两套语言互不影响：设置页保存时会显式带上这两个字段，不会把记住的语种冲掉。源语言和目标语言选成同一个会在前端拦住，避免百度 58001 白跑一次请求。

`Secret` 用 DPAPI（CurrentUser）加密后落盘，磁盘上不是明文；换到别的 Windows 账户/机器后无法解密，程序会保留原文件、备份 `config.json.bak` 并提示重新填写，不会静默清空。旧版本留下的明文配置会在首次启动时一次性加密迁移。写盘采用「临时文件 + 原子替换」，写一半断电/崩溃不会留下截断的 JSON。

开机自启的状态**不在这个文件里**，见下节。

</details>

<details>
<summary><strong>自定义快捷键的完整规则</strong></summary>

- **修饰键**：`Alt` / `Ctrl`（也可写 `Control`）/ `Shift` / `Win`（也可写 `Windows`），至少要带一个，可任意组合。
- **主键**：`A-Z`、`0-9`、`F1-F24`、方向键、`Space`、`Tab`、`Enter`、`Esc`、`Backspace`、`Insert`、`Delete`、`Home`、`End`、`PageUp`、`PageDown`、`PrintScreen`、`NumPad0-9`，以及 `;` `=` `,` `-` `.` `/` `[` `]` `'`、反斜杠、反引号等符号键（`+` 用作分隔符，不能当主键）。
- **写法示例**：`Alt+T`、`Ctrl+Shift+F5`、`Win+Alt+D`、`Ctrl+Space`、`Ctrl+;`。大小写、修饰键顺序、别名不影响识别。
- **录制**：点「录制」后直接按键，程序按 `Ctrl → Alt → Shift → Win` 的固定顺序规范化后写回输入框；只按下修饰键时会继续等待主键，`Esc` 取消，`Backspace` 清空。
- **保存时校验**：格式不合法、两个快捷键重复、被系统保留的组合（`Ctrl+Alt+Del`、`Win+L`、`Win+Shift+S`、`Alt+F4` 等）、以及与内部取词冲突的 `Ctrl+C`，都会在设置窗口内给出中文原因。「保存」先实测热键能否注册，再原子写盘、最后更新自启项，任一步失败都不留下新状态。
- 设置窗口打开期间旧热键临时注销，避免录入的组合被旧热键抢走；**关闭窗口后由唯一注册点按当前配置重新注册**，不会出现「两个键全失灵」。

</details>

<details>
<summary><strong>开机自启的实现与 Defender 行为检测</strong></summary>

设置页勾选后写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下名为 `QuickTranslate` 的字符串值，指向当前 exe（路径带引号）。只写 HKCU，不需要管理员权限，不创建计划任务。

- 注册表项是唯一状态来源，不写进 `config.json`；在任务管理器「启动应用」里禁用后，设置页会如实显示未启用。
- **启动时只读、不自动改写**：程序移动后只给一条托盘提示。故意不在启动阶段写注册表 —— 未签名 exe 自动写自启项会触发 Defender 的 `Behavior:Win32/Persistence.A!ml` 行为检测并把整个程序隔离。要修正旧路径，打开设置点一次「保存」即可（开关不必改动）；`Apply` 幂等，值已正确时一个字节都不写。
- 若该 Run 值指向其他程序，程序不会覆盖或删除；归属判断除文件名外还核对文件版本信息里的产品名。
- 自启指向的是当前运行的那个 exe：从 `bin/Debug/...` 启动就指向那里，清理构建目录会让它失效。长期使用建议先 `dotnet publish` 到固定目录，从那里运行一次再到设置里点「保存」。

</details>

<details>
<summary><strong>检查更新的行为</strong></summary>

托盘右键 → 检查更新，或设置页版本号右侧按钮，查询 GitHub 最新 Release 并与本地版本比较。

- **只在点击时联网**：启动阶段不发更新请求、不后台轮询、不写计划任务、不上传本机信息。唯一对外请求是 `GET https://api.github.com/repos/fu-roc/QuickTranslate/releases/latest`（带 `User-Agent`，GitHub API 强制要求）。
- 结果窗四种状态：正在检查 / 已是最新 / 发现新版本（版本号、发布说明摘要、安装包文件名）/ 失败（中文原因 + 重试）。
- **不自动下载、不自动运行**：只给「打开下载页面」，目标 URL 限定为 `https://github.com`，避免把接口返回的任意字符串交给系统 Shell。
- 网络不通、12 秒超时、GitHub 限流、草稿/预发布、版本号无法识别、JSON 解析失败都有中文原因；底层异常只写日志。
- 版本比较：`tag_name` 去掉 `v` 前缀与 `-beta` / `+commit` 后缀后按四段 `Version` 比较；本地版本取自 `QuickTranslate.csproj` 的 `<Version>`，与安装包版本同源。
- 公司网络拦截 `api.github.com` 时会显示「无法连接 GitHub」，可直接从 Releases 下载。取词、OCR、翻译、配置与自启完全不依赖这个接口。

</details>

<details>
<summary><strong>取词、OCR 与悬浮窗的实现细节</strong></summary>

- **不等用户松开热键**：浏览器在 `Alt` 的 keyup 后会激活菜单栏、把页面选区变成非活动状态（蓝色高亮变灰），那时 `Ctrl+C` 已经复制不到页面选区 —— 这正是「马上松 Alt 报未检测到选中文本、一直按住 Alt 反而正常」的原因。热键到达瞬间先补发修饰键 keyup 清掉残留 `Alt`（否则注入会变成 `Alt+Ctrl+C`），约 20ms 后立即注入 `Ctrl+C`，抢在焦点转移前完成复制。`RegisterHotKey` 会截获 keydown，前台窗口偶尔仍吞掉首次 `Ctrl/C` keydown，所以首轮剪贴板无变化时会清一次修饰键并补发第二次。
- 取词失败在 `%LOCALAPPDATA%\QuickTranslate\app.log` 留一行诊断（注入是否成功、剪贴板序号是否变化、前后文本长度），**不记录原文**；日志按 1 MB 轮转成 `app.log.old`，磁盘上限约 2 MB。
- **截图 OCR**：只覆盖光标所在那一块屏幕，不支持跨屏拖拽；选区用 Win32 物理像素坐标确定，不受多屏不同 DPI 缩放影响（覆盖层在副屏的视觉定位可能有偏移）。截图经 `InMemoryRandomAccessStream` 全内存流转，不往临时目录写 PNG；超大图片按 `OcrEngine.MaxImageDimension` 自动缩放。无对应系统语言包时会提示去系统设置添加。
- **悬浮窗不抢焦点**：`ShowActivated=False`，不调用 `Activate()`。窗口从不激活 → `Deactivated` 不触发、窗口外点击也收不到 WPF 鼠标事件，所以用**仅在显示期间运行的 60ms 轮询**（读全局左键状态 + 判断光标是否落在窗口内）实现「点击别处关闭」：不装全局钩子、不吞掉那次点击，只有观察到完整「按下 → 抬起」周期才判定，刚拖完选区按热键不会被自己松开的那一下关掉。鼠标从未进入窗口时不会自动消失，移入后再移开 0.8 秒收起，另有 12 秒超时兜底。
- **主界面相反**：它是你主动点开的常规窗口，需要打字、按 `Ctrl+Enter` 和 `Esc`，所以用 `Show()` + `Activate()`，不做「点击别处关闭」。两种窗口焦点策略不同是刻意设计；划词 / OCR 热键不依赖主界面是否可见。
- **长文本按字节分段**：百度单次 `q` 上限 6000 字节（汉字约 2000 个），按 UTF-8 字节切段并合并译文，最多 8 段，超长明确提示。
- 程序以 `asInvoker` 运行，不请求管理员权限；只有安装/卸载过程需要 UAC 提权。

</details>

<details>
<summary><strong>构建、产物与分发</strong></summary>

```bash
dotnet build QuickTranslate.slnx -c Debug
dotnet run --project src/QuickTranslate/QuickTranslate.csproj -c Debug

dotnet publish src/QuickTranslate/QuickTranslate.csproj -c Release -r win-x64 --self-contained false
```

三种发布形态（Release / win-x64 实测）：

| 形态 | 关键参数 | 产物 | 体积 | 目标机要求 |
| --- | --- | --- | --- | --- |
| 框架依赖 | `--self-contained false` | 6 个文件 | 25 MB | .NET 10 Desktop Runtime |
| **框架依赖 + 单文件（默认）** | 再加 `-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true` | 1 个 exe | 25 MB | .NET 10 Desktop Runtime |
| 自包含单文件 | `--self-contained true` + 上面三个单文件参数 | 1 个 exe | **190 MB** | 无 |

体积主要来自 WinRT 投影 `Microsoft.Windows.SDK.NET.dll`（24 MB）；自包含还会打包整套 WPF 运行时，首次启动需自解压到系统临时目录。

生成产物（PowerShell，项目根目录）：

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1                 # 便携版 zip，约 6 MB 压缩包
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -SelfContained   # 便携版 zip，目标机零依赖
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1          # 安装包，取 artifacts 里最新的 exe
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -Exe .\artifacts\...\QuickTranslate.exe -Version 1.1.0
```

安装包需要 [Inno Setup 6.5+](https://jrsoftware.org/isdl.php)（`ISCC.exe`），脚本依次查 `-ISCC`、PATH、常见安装目录、便携目录。`packaging/setup.iss` 是安装定义（`PrivilegesRequired=admin`、`{autopf}`、`{commonprograms}` / `{commondesktop}`，`runasoriginaluser` 避免程序继承管理员令牌，复制文件前自动结束运行实例、不用 `AppMutex` 弹窗也不用 `CloseApplications` 询问页）；`packaging/ChineseSimplified.isl` 是官方简体中文语言包。注意 Inno Setup 的 `Setup.exe` 自身 manifest 仍是 `asInvoker`，提权靠内部 `runas` 重启，不能用 manifest 判断安装级别。

产物在 `artifacts/`（已 gitignore），文件名带时间戳，每次生成新文件、不覆盖不删除。zip 内是单文件 exe 加本说明；Release 保留 portable PDB，符号文件单独放在 `artifacts/` 供排障，不塞进用户下载的 zip。程序集版本、设置页版本、安装包版本统一取自 `QuickTranslate.csproj` 的 `<Version>`，`build-installer.ps1` 自动解析。

- 便携版解压到一个**不会再移动的固定目录**（如 `%LOCALAPPDATA%\QuickTranslate\`），从那里运行一次、到设置里点一次「保存」，自启路径才会指向它。手动替换 exe 升级前需先从托盘「退出」，否则文件被占用。
- 安装包默认装到固定路径，无「移动目录导致自启失效」问题；安装/卸载需 UAC，卸载项写 HKLM，不写任何自启项。安装 / 卸载时若程序正在运行，会在复制文件前自动结束它，不弹「请关闭所有实例」或「关闭应用」询问页；向导里点「取消」则完全不动运行中的软件。交互安装结束时默认勾选的「运行 QuickTranslate」会重新拉起新版（静默安装不自动启动）。
- 两种形态都没有代码签名，首次运行可能被 SmartScreen / Defender 拦截。

</details>

<a id="defender"></a>

<details>
<summary><strong>被 Windows Defender / SmartScreen 拦截时的处置</strong></summary>

从未签名的 zip 或 GitHub 下载 exe，首次运行可能弹出「无法成功完成操作，因为文件包含病毒或潜在的垃圾软件」或「Windows 已保护你的电脑」，文件甚至会被直接删掉。**这不是病毒，是误报**：程序没有代码签名，Defender 对刚编译、全网没有下载记录的 exe 没有信誉数据。

本项目实测触发过 `Behavior:Win32/Persistence.A!ml`（严重级别 5，**行为型**「持久化」检测）：早期版本会在程序启动时自动改写 `HKCU\...\Run` 自启项，正好符合恶意软件驻留特征，Defender 因此隔离删除了 exe。**代码侧已修**：启动阶段只读检查、绝不写注册表；只有你在设置页点「保存」时才可能写一次，而且 `Apply` 幂等，值已正确时连这一次也不写。

如果仍被拦截，按这个顺序处理：

1. `Windows 安全中心 → 病毒和威胁防护 → 保护历史记录`，找到对应时间的条目，展开后点「操作 → 在设备上允许 / 还原」。
2. **先把 zip 完整解压到一个固定目录**（例如 `%LOCALAPPDATA%\QuickTranslate\`）再双击，不要直接在压缩软件里双击 zip 内的 exe —— 那会在系统临时目录解压运行，更可疑。
3. 给这个固定目录加一条范围最小的防病毒排除项（需管理员权限）：

   ```powershell
   Add-MpPreference -ExclusionPath "$env:LOCALAPPDATA\QuickTranslate"
   ```

   图形界面：`Windows 安全中心 → 病毒和威胁防护 → 管理设置 → 排除项 → 添加文件夹`。
4. 如果只是 SmartScreen 的「Windows 已保护你的电脑」，点「更多信息 → 仍要运行」，一次放行后不再提示。

根治办法是代码签名（OV / EV 证书，需购买），或提交微软误报申诉：<https://www.microsoft.com/en-us/wdsi/filesubmission>。本项目自用、不做正式分发，因此没有签名。

> 排除项等于让 Defender 不再扫描那个目录，请只对确认是自己构建的程序目录使用，不要对整个项目根目录、`Downloads` 或用户目录加白。

</details>

<details>
<summary><strong>目录结构</strong></summary>

```
QuickTranslate/
├── QuickTranslate.slnx
├── README.md
├── LICENSE                       MIT 许可
├── .gitignore
├── publish.ps1                  便携版 zip 发布脚本（保留 portable PDB）
├── build-installer.ps1           系统级 Inno Setup 安装包生成脚本（版本取自 csproj）
├── packaging/
│   ├── setup.iss                 系统级安装定义（无自启项，中文向导）
│   └── ChineseSimplified.isl     Inno Setup 官方简体中文语言包
├── design/tray-icon/             图标设计源：设计稿 + 16/20/24/32/48/256 分尺寸 PNG
├── artifacts/                   构建产物（gitignore，不进仓库）
└── src/QuickTranslate/
    ├── App.xaml(.cs)              启动编排、全局异常钩子、链路调度与取消、配置应用、窗口入口
    ├── app.manifest               PerMonitorV2 高 DPI 声明
    ├── QuickTranslate.csproj      单一版本来源（Version / AssemblyVersion / Product）
    ├── Assets/QuickTranslate.ico  应用与托盘图标（6 帧 32bpp ICO）
    ├── Models/AppConfig.cs        配置模型
    ├── Native/
    │   ├── NativeMethods.cs       RegisterHotKey、SendInput、DPI、光标坐标
    │   └── HotkeyCodec.cs         快捷键编解码、键表、录制与保留组合校验
    ├── Services/
    │   ├── ConfigService.cs       原子写盘、损坏备份、DPAPI 密钥加密
    │   ├── LogService.cs          轻量文件日志
    │   ├── AppVersion.cs          程序集版本显示
    │   ├── ErrorMapper.cs         底层异常 → 中文原因 + 建议动作
    │   ├── TranslationException.cs 错误分类（配置/网络/服务）
    │   ├── HotkeyService.cs       全局热键注册/校验与分发
    │   ├── ClipboardService.cs    模拟 Ctrl+C、剪贴板读取与复制（返回是否成功）
    │   ├── BaiduTranslator.cs     签名、按 UTF-8 字节分段、错误码中文化
    │   ├── IdentifierNormalizer.cs 下划线/驼峰标识符 → 空格分隔自然语言
    │   ├── ScreenCaptureService.cs 物理像素截图（裁剪到虚拟桌面）
    │   ├── OcrService.cs          全内存 Windows.Media.Ocr 识别
    │   ├── AutoStartService.cs    HKCU Run 开机自启（启动只读校验、仅显式保存时写入）
    │   ├── UpdateService.cs       GitHub Release 版本检查（仅手动触发、失败中文化）
    │   └── TrayService.cs         系统托盘（左键单击 / 双击 / 右键菜单）
    └── Windows/
        ├── MainWindow.xaml        双栏手动翻译主界面
        ├── ResultWindow.xaml      光标旁悬浮结果窗（不抢焦点、点击外部关闭）
        ├── SelectionOverlayWindow.xaml  截图框选层
        ├── SettingsWindow.xaml    5 项配置 + 开机自启 + 快捷键录制/恢复默认 + 检查更新
        └── UpdateWindow.xaml      检查更新结果窗
```

</details>

<details>
<summary><strong>图标规范</strong></summary>

`src/QuickTranslate/Assets/QuickTranslate.ico` 是唯一的图标资源，一处替换、三处生效：exe / 窗口 / 快捷方式（`csproj` 的 `ApplicationIcon`）、托盘（内嵌资源，运行时按 `SystemInformation.SmallIconSize` 挑帧，单文件发布不依赖外部文件）、安装包向导（`setup.iss` 的 `SetupIconFile`）。

ICO 含 16 / 20 / 24 / 32 / 48 / 256 六帧 32bpp 带透明通道图像，深浅托盘都清晰。设计源在 `design/tray-icon/`：`design-spec.png` 是设计规范（主色 `#2F6BED`、译文白 `#FFFFFF`、深托盘 `#1F2430`、浅托盘 `#EFF1F5`），`png/` 是分尺寸源图。改图标请保持六帧齐全 —— 小尺寸帧有意省略卡片内文字线，不能拿 256px 帧直接缩放代替。

</details>

## 开源许可

[MIT License](LICENSE) · Copyright (c) 2026 fu-roc。可自用、修改、再分发；百度翻译 APPID / 密钥由使用者自行申请并承担其费用与风险。

如果它替你省掉了关广告的时间，欢迎给这个仓库点个 Star。
