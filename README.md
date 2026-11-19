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

> **首次使用在线翻译**：请在设置里填写百度翻译开放平台的 APPID 和密钥。内置英汉词典可直接离线使用。

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
| 划词翻译 | 选中文字 → `Alt+T` | 模拟 `Ctrl+C` 取词，结果浮现在光标旁并保留当前输入焦点。目标为简体中文时，英文单词优先离线查词 |
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

划词查到英文单词时，悬浮窗会列出词典释义，点击一行即可复制。词库未命中、选中短语或句子，或目标为其他语言时，使用百度通用翻译。

**隐私与本地数据**：密钥用 Windows DPAPI（当前账户）加密后存进 `%APPDATA%\QuickTranslate\config.json`，换账户或换机器无法解密；配置原子写盘，损坏时先备份再提示。日志只记异常与关键状态，**不记翻译原文 / APPID / 密钥**，超过 1 MB 自动轮转。使用百度翻译时，待翻译文本会发送给百度 API。词典查询在本机完成。

## 下载

到 [GitHub Releases](https://github.com/fu-roc/QuickTranslate/releases/latest) 下最新版，两种产物内容相同，**二选一**：

| 产物 | 适合谁 | 说明 |
| --- | --- | --- |
| `...-framework-dependent.zip` 便携版 | 想绿色免安装 | 解压到固定目录，双击单文件 exe 即用；不写安装信息、不建卸载入口，升级就是替换 exe |
| `...-win-x64-setup-*.exe` 安装包 | 想要开始菜单和卸载入口 | 中文向导，默认装到 `C:\Program Files\QuickTranslate`，**系统级安装，双击时需 UAC 管理员授权**；可在「设置 → 应用」卸载，卸载不删配置 |

两者都依赖 .NET 10 Desktop Runtime。注意区分：**安装过程**需要管理员权限，**装好的程序**仍以普通权限（`asInvoker`）常驻托盘，不会请求提权。安装包不会创建开机自启项，需要请在设置页勾选。

覆盖安装时，安装程序会在复制文件前结束正在运行的托盘实例。安装完成后可勾选「运行 QuickTranslate」。

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

- 支持 Windows。在线翻译需要网络及百度翻译 API 密钥。
- 取词依赖模拟 `Ctrl+C`，不恢复原剪贴板内容；少数禁止复制的场景要用截图 OCR
- 悬浮结果窗为了「不抢焦点」收不到键盘消息，因此不支持 `Esc` 关闭；点一下窗口外的任意位置，或再按一次同一个快捷键即可关闭它，窗口本身不会自动消失
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

`TargetLanguage` 用于划词和截图 OCR。`MainWindowSourceLanguage` 和 `MainWindowTargetLanguage` 用于手动翻译，后者留空时跟随 `TargetLanguage`。

`Secret` 通过 Windows DPAPI（CurrentUser）加密保存。读取明文密钥时，程序会尝试加密并保存。配置无法解析或解密时会备份为 `config.json.bak` 并提示重新填写。配置通过同目录临时文件原子替换。

</details>

<details>
<summary><strong>自定义快捷键的完整规则</strong></summary>

- **修饰键**：`Alt` / `Ctrl`（也可写 `Control`）/ `Shift` / `Win`（也可写 `Windows`），至少要带一个，可任意组合。
- **主键**：`A-Z`、`0-9`、`F1-F24`、方向键、`Space`、`Tab`、`Enter`、`Esc`、`Backspace`、`Insert`、`Delete`、`Home`、`End`、`PageUp`、`PageDown`、`PrintScreen`、`NumPad0-9`，以及 `;` `=` `,` `-` `.` `/` `[` `]` `'`、反斜杠、反引号等符号键（`+` 用作分隔符，不能当主键）。
- **写法示例**：`Alt+T`、`Ctrl+Shift+F5`、`Win+Alt+D`、`Ctrl+Space`、`Ctrl+;`。大小写、修饰键顺序、别名不影响识别。
- **录制**：点「录制」后直接按键，程序按 `Ctrl → Alt → Shift → Win` 的固定顺序规范化后写回输入框；只按下修饰键时会继续等待主键，`Esc` 取消，`Backspace` 清空。
- **保存时校验**：检查格式、重复组合和系统占用情况。`Ctrl+C` 专用于取词。保存设置后应用自启选项，更新自启失败时尝试回滚配置。
- 设置窗口打开时暂停全局热键，关闭后按生效的设置注册。

</details>

<details>
<summary><strong>开机自启</strong></summary>

设置页勾选后写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下名为 `QuickTranslate` 的字符串值，指向当前 exe（路径带引号）。只写 HKCU，不需要管理员权限，不创建计划任务。

- 设置页根据当前用户的 Run 项显示自启状态。
- 启动时检查路径并提示问题。移动程序后，在设置页保存一次可更新启动路径。
- 若该 Run 值指向其他程序，程序不会覆盖或删除；归属判断除文件名外还核对文件版本信息里的产品名。

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

- **取词**：收到热键后释放修饰键，再向前台窗口发送 `Ctrl+C`。剪贴板没有更新时再尝试一次。
- 取词失败在 `%LOCALAPPDATA%\QuickTranslate\app.log` 留一行诊断（注入是否成功、剪贴板序号是否变化、前后文本长度），**不记录原文**；日志按 1 MB 轮转成 `app.log.old`，磁盘上限约 2 MB。
- **截图 OCR**：只覆盖光标所在那一块屏幕，不支持跨屏拖拽；选区用 Win32 物理像素坐标确定，不受多屏不同 DPI 缩放影响（覆盖层在副屏的视觉定位可能有偏移）。截图经 `InMemoryRandomAccessStream` 全内存流转，不往临时目录写 PNG；超大图片按 `OcrEngine.MaxImageDimension` 自动缩放。无对应系统语言包时会提示去系统设置添加。
- **OCR 预处理**：估计字高后缩放截图，对小字以约 24 像素为目标。识别结果过短时尝试另一倍率。用词的坐标重建空格与换行。
- **悬浮窗**：以 `ShowActivated=False` 显示，通过定时读取鼠标状态检测窗口外的完整点击。
- **主窗口**：主动打开时激活。按 `Esc` 隐藏，按 `Ctrl+Enter` 翻译。
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

内置英汉词库取自 ECDICT，提取词频靠前的 50,000 个单词及中文释义。更新数据时，运行 `python tools/build-dictionary.py <ecdict.csv>`。[词库源文件](https://github.com/skywind3000/ECDICT/blob/39cfb9672c0906b63569e0453c2e6abfb9ab182a/ecdict.csv) · [随程序打包的 MIT 许可](https://github.com/skywind3000/ECDICT/blob/39cfb9672c0906b63569e0453c2e6abfb9ab182a/LICENSE)

三种发布形态（Release / win-x64 实测）：

| 形态 | 关键参数 | 产物 | 体积 | 目标机要求 |
| --- | --- | --- | --- | --- |
| 框架依赖 | `--self-contained false` | 6 个文件 | 25 MB | .NET 10 Desktop Runtime |
| **框架依赖 + 单文件（默认）** | 再加 `-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true` | 1 个 exe | 25 MB | .NET 10 Desktop Runtime |
| 自包含单文件 | `--self-contained true` + 上面三个单文件参数 | 1 个 exe | **190 MB** | 无 |

体积主要来自 WinRT 投影 `Microsoft.Windows.SDK.NET.dll`（24 MB）；自包含还会打包整套 WPF 运行时，首次启动需自解压到系统临时目录。

生成产物（PowerShell，项目根目录）：

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1                 # 便携版 ZIP
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -SelfContained   # 便携版 zip，目标机零依赖
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -Exe .\artifacts\...\QuickTranslate\QuickTranslate.exe   # 安装包，-Exe 必填
```

安装包需要 [Inno Setup 6.5+](https://jrsoftware.org/isdl.php)。脚本从 PATH 或 Program Files 查找 `ISCC.exe`，也可用 `-ISCC` 指定。安装定义和简体中文语言文件在 `packaging/`。

带时间戳的产物位于 `artifacts/`。便携 ZIP 内含 EXE 和本说明，调试符号单独输出。默认发布版本取自 `QuickTranslate.csproj` 的 `<Version>`。

- 便携版解压到一个**不会再移动的固定目录**（如 `%LOCALAPPDATA%\QuickTranslate\`），从那里运行一次、到设置里点一次「保存」，自启路径才会指向它。手动替换 exe 升级前需先从托盘「退出」，否则文件被占用。
- 安装包默认装到固定路径，无「移动目录导致自启失效」问题；安装/卸载需 UAC，卸载项写 HKLM，不写任何自启项。安装 / 卸载时若程序正在运行，会在复制文件前自动结束它，不弹「请关闭所有实例」或「关闭应用」询问页；向导里点「取消」则完全不动运行中的软件。交互安装结束时默认勾选的「运行 QuickTranslate」会重新拉起新版（静默安装不自动启动）。
- 两种形态都没有代码签名，首次运行可能被 SmartScreen / Defender 拦截。

</details>

<a id="defender"></a>

<details>
<summary><strong>被 Windows Defender / SmartScreen 拦截时的处置</strong></summary>

构建产物未经代码签名，首次运行可能触发 Windows 安全软件提示。可核对文件来源，或将疑似误报的文件提交微软复核。[微软文件提交](https://www.microsoft.com/en-us/wdsi/filesubmission)

</details>

<details>
<summary><strong>目录结构</strong></summary>

- `src/QuickTranslate/` 是 WPF 应用，包含窗口、服务、Windows 原生接口和嵌入资源。
- `packaging/` 和根目录的 PowerShell 脚本用于发布便携版和构建安装包。
- `tools/build-dictionary.py` 生成内置英汉词库，`design/tray-icon/` 存放图标设计源文件。

</details>

<details>
<summary><strong>图标规范</strong></summary>

`src/QuickTranslate/Assets/QuickTranslate.ico` 是唯一的图标资源，一处替换、三处生效：exe / 窗口 / 快捷方式（`csproj` 的 `ApplicationIcon`）、托盘（内嵌资源，运行时按 `SystemInformation.SmallIconSize` 挑帧，单文件发布不依赖外部文件）、安装包向导（`setup.iss` 的 `SetupIconFile`）。

ICO 含 16 / 20 / 24 / 32 / 48 / 256 六帧 32bpp 带透明通道图像，深浅托盘都清晰。设计源在 `design/tray-icon/`：`design-spec.png` 是设计规范（主色 `#2F6BED`、译文白 `#FFFFFF`、深托盘 `#1F2430`、浅托盘 `#EFF1F5`），`png/` 是分尺寸源图。改图标请保持六帧齐全 —— 小尺寸帧有意省略卡片内文字线，不能拿 256px 帧直接缩放代替。

</details>

## 开源许可

[MIT License](LICENSE) · Copyright (c) 2026 fu-roc。可自用、修改、再分发；百度翻译 APPID / 密钥由使用者自行申请并承担其费用与风险。

如果它替你省掉了关广告的时间，欢迎给这个仓库点个 Star。
