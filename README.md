# QuickTranslate

一个极简的 Windows 划词翻译工具：选中文字按快捷键即出译文，没有广告、没有账号、没有多余功能。

## 两条链路

| 快捷键 | 流程 |
| --- | --- |
| `Alt+T`（可改） | 全局热键 → 模拟 `Ctrl+C` → 读剪贴板 → 百度翻译 → 光标旁悬浮窗显示译文 |
| `Alt+S`（可改） | 全局热键 → 屏幕框选 → `Windows.Media.Ocr` 本地识别 → 百度翻译 → 同一个悬浮窗显示译文 |

两条链路共用一个悬浮窗，**不抢走你正在打字的焦点**：点一下窗口后按 `Esc` / 鼠标移开 / 12 秒超时 / 再按一次快捷键关闭，点击译文即复制（复制失败会明确提示并允许重试）。关掉窗口会取消在途的翻译请求，迟到的结果不会再把窗口弹回来。失败时窗内显示中文原因，并按错误类别给出「打开设置」「重试」或「改用另一条链路」，不会把英文技术异常塞给你，也不会静默无反应。

两个快捷键都可以自定义：托盘右键 → 设置 → 点对应行右侧的「录制」，直接按下想要的组合键即可录入（`Esc` 取消录制，`Backspace` 清空）；也可以像以前一样手动填写字符串。点「恢复默认快捷键」可一键回到 `Alt+T` / `Alt+S`。

## 下载与安装

到 [GitHub Releases](https://github.com/fu-roc/QuickTranslate/releases) 下载最新版，两种产物内容相同，**二选一**：

| 产物 | 适合 | 说明 |
| --- | --- | --- |
| `...-framework-dependent.zip` 便携版 | 想绿色免安装 | 解压到固定目录，双击单文件 exe 即用；不写安装信息、不建卸载入口，升级就是替换 exe |
| `...-win-x64-setup-*.exe` 安装包 | 想要开始菜单和卸载入口 | 中文安装向导，默认装到 `C:\Program Files\QuickTranslate`，**系统级安装：双击时需 UAC 管理员授权**，写 HKLM 卸载项与「所有用户」开始菜单；可在「设置 → 应用」卸载，卸载不会删你的配置 |

两者都需要目标机安装 .NET 10 Desktop Runtime（见下节）。注意区分：**安装过程**需要管理员权限，**装好的程序**仍以普通权限（`asInvoker`）常驻托盘，不会请求提权。安装包**不会创建开机自启项** —— 未签名程序写 `HKCU\...\Run` 会触发 Defender 的持久化行为检测；需要自启请在程序设置页勾选。未签名文件首次运行可能被 SmartScreen / Defender 拦截，处置见下文对应章节。

## 环境要求

- Windows 10 2004（build 19041）或更高，已验证 Windows 11 专业版 26300
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（装了 SDK 就不用单独装）
- 系统已安装 OCR 语言包：`设置 → 时间和语言 → 语言和区域`，添加 English (United States) 或中文并勾选文字识别
- 百度翻译开放平台的 APPID 与密钥（个人认证后有免费额度，个人划词用量用不完）

## 首次配置

1. 打开 <https://fanyi-api.baidu.com/>，注册并完成个人认证，开通「通用文本翻译 API」，在开发者信息页拿到 APPID 和密钥。
2. 从 Release 下载安装（或解压便携版），运行程序。首次启动会在 `%APPDATA%\QuickTranslate\config.json` 生成配置模板并自动打开设置窗口。
3. 填入 APPID、密钥；快捷键可用「录制」按键录入或手动填写；选好目标语言、按需勾选「开机自动启动」后保存。
4. 试着选中一段英文按 `Alt+T`。托盘图标右键可随时打开设置或退出。

配置文件示例（**不要把填好密钥的文件提交到任何仓库**）：

```json
{
  "AppId": "你的APPID",
  "Secret": "dpapi:v1:……当前 Windows 账户加密后的密文……",
  "TakeWordHotkey": "Alt+T",
  "OcrHotkey": "Alt+S",
  "TargetLanguage": "zh"
}
```

`Secret` 用 **DPAPI（CurrentUser）** 加密后落盘，磁盘上不是明文；因此该文件换到别的 Windows 账户/机器后无法解密，程序会保留原文件、备份一份并提示你重新填写，不会静默清空。旧版本留下的明文配置会在首次启动时一次性加密迁移。写盘采用「临时文件 + 原子替换」，写一半断电/崩溃不会留下截断的 JSON。

开机自启的状态**不在这个文件里**，见下一节。

## 自定义快捷键

- **修饰键**：`Alt` / `Ctrl`（也可写 `Control`）/ `Shift` / `Win`（也可写 `Windows`），至少要带一个，可任意组合。
- **主键**：`A-Z`、`0-9`、`F1-F24`、方向键、`Space`、`Tab`、`Enter`、`Esc`、`Backspace`、`Insert`、`Delete`、`Home`、`End`、`PageUp`、`PageDown`、`PrintScreen`、`NumPad0-9`，以及 `;` `=` `,` `-` `.` `/` `[` `]` `'`、反斜杠、反引号等符号键（`+` 用作分隔符，不能当主键）。
- **写法示例**：`Alt+T`、`Ctrl+Shift+F5`、`Win+Alt+D`、`Ctrl+Space`、`Ctrl+;`。大小写、修饰键顺序、别名不影响识别。
- **录制**：点「录制」后直接按键，程序会把按下的修饰键按 `Ctrl → Alt → Shift → Win` 的固定顺序规范化后写回输入框；只按下修饰键时会继续等待主键。
- **保存时校验**：格式不合法、两个快捷键重复、被系统保留的组合（`Ctrl+Alt+Del`、`Win+L`、`Win+Shift+S`、`Alt+F4` 等）、以及与内部取词冲突的 `Ctrl+C`，都会在设置窗口内给出中文原因；「保存」会先实测两个热键能否注册（占用检测），再原子写盘、最后更新自启项，任一步失败都不留下新状态，窗口不关闭、配置不生效。
- 设置窗口打开期间旧热键会临时注销，避免你想录入的组合键被旧热键抢走并触发翻译；**关闭设置窗口后由唯一的注册点按当前配置重新注册**（保存成功就是新键、失败或取消就是旧键），不会出现「两个键全失灵」。
- 快捷键变化时会有一条托盘提示；配置仍写在 `%APPDATA%\QuickTranslate\config.json` 的 `TakeWordHotkey` / `OcrHotkey` 两个字段里。

## 开机自动启动（可选）

设置页最下方的「开机自动启动」勾选后，登录 Windows 时程序会自动在托盘运行。默认关闭，随时可取消。

- **实现**：写入当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下名为 `QuickTranslate` 的字符串值，指向当前 exe（路径带引号，兼容含空格的目录）。只写 HKCU，**不需要管理员权限**，也不创建开始菜单快捷方式或计划任务。
- **不写进 config.json**：注册表项本身就是唯一状态来源。这样在任务管理器「启动应用」里禁用、或被清理软件删掉之后，设置页会如实显示未启用，不会出现「配置说开了、系统其实没开」的两套说法。
- **启动时只读、不自动改写**：程序被移动后，启动时只检查并给一条托盘提示。**故意不在启动阶段写注册表** —— 未签名 exe 自动写自启项会触发 Defender 的 `Behavior:Win32/Persistence.A!ml` 行为检测并把整个程序隔离，详见下一节。要修正旧路径，只需打开设置点一次「保存」（开关不必改动）；`Apply` 内部幂等，值已经正确时一个字节都不会写。
- **同名项保护**：如果那个 Run 值指向的是其他程序，程序不会覆盖或删除它，设置页会给出中文提示。归属判断除了 exe 文件名，还会核对文件版本信息里的产品名，避免误删恰好同名的第三方启动项。
- **保存是一致性的**：热键先做无副作用的注册可行性校验 → 配置原子写盘 → 自启项更新；自启写入失败会用旧配置回滚写盘。热键注册统一在设置窗口关闭后进行，不会出现「新键先生效、配置却没保存」。
- 也可以在 `任务管理器 → 启动应用` 里禁用；禁用后设置页的勾选框会显示为未勾选。若要彻底清除，取消勾选并保存即可删除该注册表值。

> **注意**：自启指向的是你当前运行的那个 exe。开发时从 `bin/Debug/.../QuickTranslate.exe` 启动，开机自启就指向该文件；清理构建目录会让它失效。要长期稳定使用，建议先 `dotnet publish` 到固定目录，从那里运行一次，再到设置里点一次「保存」（勾选状态不必改动）即可修正路径。

## 构建与运行

```bash
dotnet build QuickTranslate.slnx -c Debug
dotnet run --project src/QuickTranslate/QuickTranslate.csproj -c Debug

# 需要拷到别的机器时
dotnet publish src/QuickTranslate/QuickTranslate.csproj -c Release -r win-x64 --self-contained false
```

三种发布形态（本机实测，Release / win-x64）：

| 形态 | 关键参数 | 产物 | 体积 | 目标机要求 |
| --- | --- | --- | --- | --- |
| 框架依赖 | `--self-contained false` | 6 个文件 | 25 MB | .NET 10 Desktop Runtime |
| **框架依赖 + 单文件（推荐）** | 再加 `-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true` | 1 个 exe | 25 MB | .NET 10 Desktop Runtime |
| 自包含单文件 | `--self-contained true` + 上面三个单文件参数 | 1 个 exe | **190 MB** | 无 |

体积主要来自 WinRT 投影 `Microsoft.Windows.SDK.NET.dll`（24 MB）；自包含还会打包整套 WPF 运行时，首次启动需自解压到 `%TEMP%\.net`。

## 分发方式

两种产物都由脚本生成，并作为 [GitHub Release](https://github.com/fu-roc/QuickTranslate/releases) 附件发布。默认推荐 **zip 便携版**：无需管理员权限、不写安装信息、不建卸载入口，升级就是替换 exe，配置在 `%APPDATA%` 不受影响，与「极简、绿色」的目标一致。**安装包**是给需要系统级安装、所有用户共享的开始菜单与正规卸载入口的场景用的可选形态（v1.0.1 起由用户级改为系统级）。

生成产物（PowerShell，项目根目录执行）：

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1                 # 便携版 zip，25 MB，需 .NET 10 Desktop Runtime
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -SelfContained   # 便携版 zip，190 MB，目标机零依赖
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1          # 安装包，取 artifacts 里最新的 exe
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -Exe .\artifacts\...\QuickTranslate.exe -Version 1.0.2
```

生成安装包需要 [Inno Setup 6.5+](https://jrsoftware.org/isdl.php)（`ISCC.exe`）：脚本会依次在 `-ISCC` 参数、PATH、常见安装目录、便携目录里查找。`packaging/setup.iss` 是安装定义（`PrivilegesRequired=admin`、`{autopf}`、`{commonprograms}` / `{commondesktop}`，安装完成后的启动项用 `runasoriginaluser` 以免程序继承管理员令牌），`packaging/ChineseSimplified.isl` 是官方仓库的简体中文语言包。注意 Inno Setup 的 `Setup.exe` 自身 manifest 仍是 `asInvoker`，提权靠内部 `runas` 重启，不能用 manifest 判断安装级别。

产物都在 `artifacts/`（已加入 `.gitignore`），文件名带日期时间和形态 —— 每次运行都生成新文件，不覆盖旧包、不删除任何东西，避免同名文件正在被安全软件扫描时无法覆写。zip 内是单文件 exe 加本说明，`publish.ps1` 会自动找 `dotnet`（PATH 里没有时回退到 `C:\Program Files\dotnet`）。Release 现在保留 portable PDB，符号文件以 `<产物名>.pdb` 单独放在 `artifacts/` 供排障，不塞进用户下载的 zip。程序集版本、设置页显示的版本和安装包版本统一取自 `QuickTranslate.csproj` 的 `<Version>`，`build-installer.ps1` 会自动解析，不需要再手工传 `-Version`（传了则以传入值覆盖）。

- 解压到一个**不会再移动的固定目录**（如 `%LOCALAPPDATA%\QuickTranslate\`），从那里运行一次，再到设置里点一次「保存」—— 自启项记录的是 exe 绝对路径，移动目录会让它失效，保存时会自动把旧路径改成当前位置（勾选状态不用改）。
- 覆盖更新前先从托盘「退出」，否则 exe 被占用会替换失败。
- 目标机没装 .NET 10 Desktop Runtime 时，先装运行时，或改用 190 MB 的自包含单文件版。
- 安装包默认装到固定路径 `C:\Program Files\QuickTranslate`，因此没有「移动目录导致自启失效」的问题；安装和卸载都需要 UAC 管理员授权，卸载项写在 HKLM。但**它不写任何自启项**，需要自启仍要在设置页勾选一次（写的是当前用户的 HKCU Run，指向 Program Files 下固定的 exe）。安装前若程序正在运行，安装向导会提示先退出，避免 exe 被占用导致替换失败。
- 两种形态都没有代码签名，首次运行都可能被 SmartScreen 提示；根治只能买证书做签名，自用项目未做。

## 被 Windows Defender / SmartScreen 拦截

从 zip 或 GitHub 下载的**未签名 exe** 首次运行，可能弹出「无法成功完成操作，因为文件包含病毒或潜在的垃圾软件」或「Windows 已保护你的电脑」，文件甚至会被直接删掉。

**这不是病毒，是误报。** 程序没有代码签名，Defender 对一个刚编译出来、全网没有下载记录的 exe 本来就没有信誉数据。本项目实测触发过 `Behavior:Win32/Persistence.A!ml`（严重级别 5，**行为型**「持久化」检测）：早先版本会在**程序启动时自动改写** `HKCU\...\Run` 自启项，未签名 exe 主动写自启动注册表，正好符合恶意软件驻留的行为特征，Defender 因此隔离删除了 exe，连 Run 项一起清掉。

**代码侧已修**：启动阶段只读检查启动项、绝不写注册表；只有你在设置页点「保存」时才可能写一次，而且 `Apply` 是幂等的 —— 值已经指向当前 exe（包括路径正确时）连这一次也不写，只有真正开启/关闭或修正旧路径才产生写动作。

如果仍被拦截，按这个顺序处理：

1. 打开 `Windows 安全中心 → 病毒和威胁防护 → 保护历史记录`，找到对应时间的 `Behavior:Win32/Persistence.A!ml` 条目，展开后点「操作 → 在设备上允许 / 还原」，把被隔离的 exe 放回来。
2. **先把 zip 完整解压到一个固定目录**（例如 `%LOCALAPPDATA%\QuickTranslate\`），再从那里双击。不要直接在压缩软件里双击 zip 内的 exe —— 那会在 `%TEMP%` 临时目录里解压运行，从临时目录启动未签名程序更可疑。
3. 给这个固定目录加一条范围最小的防病毒排除项（需管理员权限）：

   ```powershell
   Add-MpPreference -ExclusionPath "$env:LOCALAPPDATA\QuickTranslate"
   ```

   图形界面：`Windows 安全中心 → 病毒和威胁防护 → 管理设置 → 排除项 → 添加文件夹`。
4. 如果只是 SmartScreen 的「Windows 已保护你的电脑」，点「更多信息 → 仍要运行」即可，一次放行后不再提示。

**根治办法是代码签名**（OV / EV 证书，需购买），或把 exe 提交给微软误报申诉：<https://www.microsoft.com/en-us/wdsi/filesubmission>。本项目自用、不做正式分发，因此没有签名。

> 排除项等于让 Defender 不再扫描那个目录，请只对确认是自己构建的程序目录使用，不要对整个项目根目录、`Downloads` 或用户目录加白。

## 设计与已知限制

按需求刻意保留的行为，不是 bug：

- **不保存、不恢复剪贴板**：按 `Alt+T` 后剪贴板里留下的是选中的原文，之前复制的内容会被覆盖；点击译文才会把译文写入剪贴板。
- **只用模拟 `Ctrl+C` 取词**：热键触发的瞬间 `Alt` / 主键通常还按着，直接注入会变成 `Alt+Ctrl+C` 而复制失败，所以程序会先等按键全部松开（最多 300ms），**只有确实还有键被按住时**才补发修饰键释放（不再无条件补发 5 个 keyup 干扰你随后按住的修饰键），再注入 `Ctrl+C`。管理员权限运行的窗口会被系统 UIPI 拦截；终端里 `Ctrl+C` 可能被当作中断信号；禁止复制的软件取不到词。这些情况会显示「未检测到选中文本」，并提供**手动**切换截图 OCR 的按钮（不会自动开始截图）。
- **截图框选只覆盖光标所在的那一块屏幕**，不支持跨屏拖拽。选区用 Win32 物理像素坐标确定，因此截图内容不受多屏不同 DPI 缩放影响；但覆盖层在副屏的视觉定位可能有偏移。
- **翻译必须联网**：走百度通用文本翻译 API，断网会明确报错。免费额度与接口规则由百度决定，可能变动。
- **OCR 依赖系统语言包**：没装对应语言包会提示去系统设置添加。识别超大图片前会按 `OcrEngine.MaxImageDimension` 自动缩放；截图只在内存里流转（`InMemoryRandomAccessStream`），不再往 `%TEMP%` 写 PNG，进程被杀也不会残留屏幕内容。
- **悬浮窗不抢焦点**：`ShowActivated=False`，不调用 `Activate()`；因此 `Esc` 需要先点一下窗口让它获得焦点，平时靠鼠标移开 / 12 秒超时 / 再按一次快捷键关闭。这是「不打断打字」与「全局 Esc」之间的取舍。
- **长文本按字节分段**：百度单次 `q` 上限是 **6000 字节**（汉字约 2000 个），程序按 UTF-8 字节切段并合并译文，最多 8 段；超长会明确提示，不再静默截断前 2000 字符。
- **密钥与日志**：密钥用 DPAPI 加密，配置损坏时会先备份 `config.json.bak` 再提示；运行日志在 `%LOCALAPPDATA%\QuickTranslate\app.log`（超过 1 MB 轮转成 `.old`），只记异常与关键状态，不记 APPID / 密钥 / 翻译原文。Release 产物保留 portable PDB，符号文件单独放在 `artifacts/` 不塞进便携 zip。
- **开机自启默认关闭**：可在设置页勾选，实现方式是 HKCU 的 Run 项，详见上文。不写计划任务、不需要管理员权限；启动阶段不写注册表，避免触发杀软的行为检测。
- **未做代码签名**：exe 可能被 Defender / SmartScreen 拦截，首次运行的处置见上文「被 Windows Defender / SmartScreen 拦截」。
- 程序以 `asInvoker` 权限运行，不请求管理员权限；只有**安装/卸载过程**需要 UAC 提权（v1.0.1 起安装包为系统级）。

## 目录结构

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
├── artifacts/                   构建产物（gitignore，不进仓库）
└── src/QuickTranslate/
    ├── App.xaml(.cs)              启动编排、全局异常钩子、链路调度与取消、配置应用
    ├── app.manifest               PerMonitorV2 高 DPI 声明
    ├── QuickTranslate.csproj      单一版本来源（Version / AssemblyVersion / Product）
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
    │   ├── ScreenCaptureService.cs 物理像素截图（裁剪到虚拟桌面）
    │   ├── OcrService.cs          全内存 Windows.Media.Ocr 识别
    │   ├── AutoStartService.cs    HKCU Run 开机自启（启动只读校验、仅显式保存时写入）
    │   └── TrayService.cs         系统托盘
    └── Windows/
        ├── ResultWindow.xaml      光标旁悬浮结果窗（不抢焦点）
        ├── SelectionOverlayWindow.xaml  截图框选层
        └── SettingsWindow.xaml    5 项配置 + 开机自启 + 快捷键录制/恢复默认 + 版本号
```

## 开源许可

本项目采用 [MIT License](LICENSE)，Copyright (c) 2026 fu-roc。可以自用、修改、再分发；百度翻译 APPID / 密钥由使用者自行申请并承担其费用与风险。

## 验收标准

连续 7 天用它完成日常查词，且没有再打开有道词典。
