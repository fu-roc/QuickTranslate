# QuickTranslate

一个极简的 Windows 划词翻译工具：选中文字按快捷键即出译文，没有广告、没有账号、没有多余功能。

## 两条链路

| 快捷键 | 流程 |
| --- | --- |
| `Alt+T`（可改） | 全局热键 → 模拟 `Ctrl+C` → 读剪贴板 → 百度翻译 → 光标旁悬浮窗显示译文 |
| `Alt+S`（可改） | 全局热键 → 屏幕框选 → `Windows.Media.Ocr` 本地识别 → 百度翻译 → 同一个悬浮窗显示译文 |

两条链路共用一个悬浮窗：`Esc` / 点击窗口外 / 再按一次快捷键关闭，点击译文即复制。失败时窗内显示中文原因，并提供「改用另一条链路」按钮，不会静默无反应。

两个快捷键都可以自定义：托盘右键 → 设置 → 点对应行右侧的「录制」，直接按下想要的组合键即可录入（`Esc` 取消录制，`Backspace` 清空）；也可以像以前一样手动填写字符串。点「恢复默认快捷键」可一键回到 `Alt+T` / `Alt+S`。

## 环境要求

- Windows 10 2004（build 19041）或更高，已验证 Windows 11 专业版 26300
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（装了 SDK 就不用单独装）
- 系统已安装 OCR 语言包：`设置 → 时间和语言 → 语言和区域`，添加 English (United States) 或中文并勾选文字识别
- 百度翻译开放平台的 APPID 与密钥（个人认证后有免费额度，个人划词用量用不完）

## 首次配置

1. 打开 <https://fanyi-api.baidu.com/>，注册并完成个人认证，开通「通用文本翻译 API」，在开发者信息页拿到 APPID 和密钥。
2. 构建并运行程序。首次启动会在 `%APPDATA%\QuickTranslate\config.json` 生成配置模板并自动打开设置窗口。
3. 填入 APPID、密钥；快捷键可用「录制」按键录入或手动填写；选好目标语言、按需勾选「开机自动启动」后保存。
4. 试着选中一段英文按 `Alt+T`。托盘图标右键可随时打开设置或退出。

配置文件示例（**不要把填好密钥的文件提交到任何仓库**）：

```json
{
  "AppId": "你的APPID",
  "Secret": "你的密钥",
  "TakeWordHotkey": "Alt+T",
  "OcrHotkey": "Alt+S",
  "TargetLanguage": "zh"
}
```

开机自启的状态**不在这个文件里**，见下一节。

## 自定义快捷键

- **修饰键**：`Alt` / `Ctrl`（也可写 `Control`）/ `Shift` / `Win`（也可写 `Windows`），至少要带一个，可任意组合。
- **主键**：`A-Z`、`0-9`、`F1-F24`、方向键、`Space`、`Tab`、`Enter`、`Esc`、`Backspace`、`Insert`、`Delete`、`Home`、`End`、`PageUp`、`PageDown`、`PrintScreen`、`NumPad0-9`，以及 `;` `=` `,` `-` `.` `/` `[` `]` `'`、反斜杠、反引号等符号键（`+` 用作分隔符，不能当主键）。
- **写法示例**：`Alt+T`、`Ctrl+Shift+F5`、`Win+Alt+D`、`Ctrl+Space`、`Ctrl+;`。大小写、修饰键顺序、别名不影响识别。
- **录制**：点「录制」后直接按键，程序会把按下的修饰键按 `Ctrl → Alt → Shift → Win` 的固定顺序规范化后写回输入框；只按下修饰键时会继续等待主键。
- **保存时校验**：格式不合法、两个快捷键重复、被系统保留的组合（`Ctrl+Alt+Del`、`Win+L`、`Win+Shift+S`、`Alt+F4` 等）、以及与内部取词冲突的 `Ctrl+C`，都会在设置窗口内给出中文原因；「保存」会等两个热键都注册成功才写盘，任一被其他软件占用则整体回滚，窗口不关闭、旧快捷键继续有效。
- 设置窗口打开期间旧热键会临时注销，避免你想录入的组合键被旧热键抢走并触发翻译；关闭设置窗口后自动恢复。
- 快捷键变化时会有一条托盘提示；配置仍写在 `%APPDATA%\QuickTranslate\config.json` 的 `TakeWordHotkey` / `OcrHotkey` 两个字段里。

## 开机自动启动（可选）

设置页最下方的「开机自动启动」勾选后，登录 Windows 时程序会自动在托盘运行。默认关闭，随时可取消。

- **实现**：写入当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下名为 `QuickTranslate` 的字符串值，指向当前 exe（路径带引号，兼容含空格的目录）。只写 HKCU，**不需要管理员权限**，也不创建开始菜单快捷方式或计划任务。
- **不写进 config.json**：注册表项本身就是唯一状态来源。这样在任务管理器「启动应用」里禁用、或被清理软件删掉之后，设置页会如实显示未启用，不会出现「配置说开了、系统其实没开」的两套说法。
- **搬家自愈**：程序被移动或重新发布后，下次启动会自动把旧的启动项路径更新为当前 exe；路径本来就正确时不会动注册表。
- **同名项保护**：如果那个 Run 值指向的是其他程序，程序不会覆盖或删除它，设置页会给出中文提示。
- **保存是事务式的**：热键全部注册成功、启动项写入成功、配置写盘成功，三件事都完成才生效；任一步失败会回滚已做的改动，窗口不关闭、配置不落盘。
- 也可以在 `任务管理器 → 启动应用` 里禁用；禁用后设置页的勾选框会显示为未勾选。若要彻底清除，取消勾选并保存即可删除该注册表值。

> **注意**：自启指向的是你当前运行的那个 exe。开发时从 `bin/Debug/.../QuickTranslate.exe` 启动，开机自启就指向该文件；清理构建目录会让它失效。要长期稳定使用，建议先 `dotnet publish` 到固定目录，从那里运行一次，再到设置里重新勾选保存。

## 构建与运行

```bash
dotnet build QuickTranslate.slnx -c Debug
dotnet run --project src/QuickTranslate/QuickTranslate.csproj -c Debug

# 需要拷到别的机器时
dotnet publish src/QuickTranslate/QuickTranslate.csproj -c Release -r win-x64 --self-contained false
```

发布为 framework-dependent，目标机需装 .NET 10 Desktop Runtime；若要单文件免安装，把 `--self-contained` 改为 `true`（体积约 60 MB）。

## 设计与已知限制

按需求刻意保留的行为，不是 bug：

- **不保存、不恢复剪贴板**：按 `Alt+T` 后剪贴板里留下的是选中的原文，之前复制的内容会被覆盖；点击译文才会把译文写入剪贴板。
- **只用模拟 `Ctrl+C` 取词**：热键触发的瞬间 `Alt` / 主键通常还按着，直接注入会变成 `Alt+Ctrl+C` 而复制失败，所以程序会先等按键全部松开（最多 300ms）、补发一次修饰键释放，再注入 `Ctrl+C`。管理员权限运行的窗口会被系统 UIPI 拦截；终端里 `Ctrl+C` 可能被当作中断信号；禁止复制的软件取不到词。这些情况会显示「未检测到选中文本」，并提供**手动**切换截图 OCR 的按钮（不会自动开始截图）。
- **截图框选只覆盖光标所在的那一块屏幕**，不支持跨屏拖拽。选区用 Win32 物理像素坐标确定，因此截图内容不受多屏不同 DPI 缩放影响；但覆盖层在副屏的视觉定位可能有偏移。
- **翻译必须联网**：走百度通用文本翻译 API，断网会明确报错。免费额度与接口规则由百度决定，可能变动。
- **OCR 依赖系统语言包**：没装对应语言包会提示去系统设置添加。识别超大图片前会按 `OcrEngine.MaxImageDimension` 自动缩放。
- **开机自启默认关闭**：可在设置页勾选，实现方式是 HKCU 的 Run 项，详见上文。不写计划任务、不需要管理员权限。
- 程序以 `asInvoker` 权限运行，不请求管理员权限。

## 目录结构

```
QuickTranslate/
├── QuickTranslate.slnx
└── src/QuickTranslate/
    ├── App.xaml(.cs)              启动编排、托盘、两条链路调度、配置事务
    ├── app.manifest               PerMonitorV2 高 DPI 声明
    ├── Models/AppConfig.cs        配置模型
    ├── Native/
    │   ├── NativeMethods.cs       RegisterHotKey、SendInput、DPI、光标坐标
    │   └── HotkeyCodec.cs         快捷键编解码、键表、录制与保留组合校验
    ├── Services/
    │   ├── ConfigService.cs       %APPDATA% 明文 JSON 配置
    │   ├── HotkeyService.cs       全局热键注册与分发
    │   ├── ClipboardService.cs    模拟 Ctrl+C 与剪贴板读取
    │   ├── BaiduTranslator.cs     签名、请求、错误码中文化
    │   ├── ScreenCaptureService.cs 物理像素截图
    │   ├── OcrService.cs          Windows.Media.Ocr 识别
    │   ├── AutoStartService.cs    HKCU Run 开机自启与路径自愈
    │   └── TrayService.cs         系统托盘
    └── Windows/
        ├── ResultWindow.xaml      光标旁悬浮结果窗
        ├── SelectionOverlayWindow.xaml  截图框选层
        └── SettingsWindow.xaml    5 项配置 + 开机自启 + 快捷键录制/恢复默认
```

## 验收标准

连续 7 天用它完成日常查词，且没有再打开有道词典。
