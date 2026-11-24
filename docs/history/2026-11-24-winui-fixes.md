# WinUI 迁移修复与 Windows 11 外观（2026-11-24）

本文件汇总 2026-11-23 至 2026-11-24 的排查记录，替代先前散落在 `.planning/` 下的过程日志。原始设计与计划文档见同目录的 `2026-11-17-original-plan.md`、`2026-11-20-pinned-watcher-monitoring.md`、`2026-11-20-pinned-watcher-design.md`。

## 一、用户看到的两个问题

1. 运行 `publish/LogOrganizer.exe` 仍是迁移前的 WinForms 窗口（“桌面文件自动归档（托盘图标不可用）”）。该二进制是 2026-11-19 的旧产物，属于发布产物过期。
2. 重新发布后，WinUI 版本在 XAML 初始化阶段崩溃：
   - 对话框“启动失败：XAML parsing failed.”，`XamlParseException (0x802B000A)`，位置 `MainWindow.InitializeComponent()`；
   - 或在 `App.xaml` 合并 `XamlControlsResources` 后直接 `0xC000027B`（stowed exception fail-fast）。

## 二、根因（均由隔离实验证实，未猜测）

| # | 根因 | 证据 | 修复 |
| --- | --- | --- | --- |
| 1 | `App.xaml` 未合并 `XamlControlsResources`。WinUI 3 不像 UWP 会自动加载控件默认样式。 | 任何含标准控件的窗口在解析时抛 `0x802B000A`。 | `App.xaml` 加入 `ResourceDictionary.MergedDictionaries` + `XamlControlsResources`。 |
| 2 | csproj 设置了 `EnableDefaultApplicationDefinition=false`，`App.xaml` 不再作为 ApplicationDefinition 编译，应用级资源域不完整。 | 最小探针工程只加该属性即可复现同一 `0xC000027B`；单独使用自定义 `Main`、`DISABLE_XAML_GENERATED_MAIN`、`WindowsAppSDKBootstrapInitialize=false` 均不会崩溃。 | 删除该属性。 |
| 3 | `build.ps1` 默认“框架依赖 + 单文件”，WinUI 不支持该组合，`Application.Start` 报 `REGDB_E_CLASSNOTREG`。 | 默认发布产物无法启动。 | 默认改为自包含单文件；`-FrameworkDependent` 显式选择多文件框架依赖发布。 |
| 4 | `WindowsAppSDKBootstrapInitialize=false` 被无条件写死，框架依赖模式下引导器不会解析已安装的 Windows App Runtime。 | 框架依赖 exe 立即退出，事件日志为 `COMException 0x80040154` at `Microsoft.UI.Xaml.Application.Start`。 | 改为仅当 `WindowsAppSDKSelfContained == true` 时关闭引导器（自包含必须关闭，否则 `0xC0000142`）。 |

### 失败码对照

| 代码 | 含义 |
| --- | --- |
| `0x802B000A` | `XamlParseException`：XAML 解析失败（缺控件样式时最常见） |
| `0xC000027B` | stowed exception fail-fast，本项目中由 `EnableDefaultApplicationDefinition=false` 触发 |
| `0xC0000142` | 引导器初始化 + 自包含组合导致 DLL 初始化失败 |
| `0x80040154` | `REGDB_E_CLASSNOTREG`：框架依赖模式未启用引导器 |
| `0xE0434352` | 托管未处理异常对应的进程退出码 |

## 三、本轮结构调整

- 界面只保留一份实现：`src/LogOrganizer.WinForms` 改名为 `src/LogOrganizer.WinUI`，工程文件同步改名，命名空间早已是 `LogOrganizer.WinUI`。
- 删除迁移遗留的 WinForms 死代码：`ArchiveForm.cs`、`ConflictForm.cs`、`DropPanelForm.cs`、`SettingsForm.cs`、`TrayIcon.cs`，以及 csproj 里的 `<Compile Remove=...>` 排除项。
- `PendingItem` 移入 `LogOrganizer.Core`；原 WinUI 测试工程只测这一条记录类型，其断言并入 `Core.Tests` 后删除整个测试工程及 sln 条目。
- 构建产物清理：删除全部 `bin/obj` 与 `artifacts/`；`.gitignore` 补充 `artifacts/`、`.planning/`。

## 四、Windows 11 外观

- `AppSettings.UseMicaBackdrop`（布尔）升级为 `Backdrop` 枚举：`Mica` / `Acrylic` / `Solid`，默认 `Mica`。
- `SettingsStore` 读取旧配置时自动迁移：`UseMicaBackdrop: true` → `Mica`，`false` → `Solid`；保存后不再写回旧字段。
- `BackdropFactory` 统一做能力检测与创建；Windows 10、高对比度或材质不支持时退回纯色。
- 窗口根表面在材质模式下透明，卡片为半透明，标题栏与系统按钮颜色跟随浅色/深色主题。
- 设置窗口切换背景选项时即时预览。

### 置顶按钮避让

自绘标题栏里的置顶按钮原先横向完全落在系统“关闭”按钮下方。现在按 `AppWindow.TitleBar.RightInset / RasterizationScale` 计算右侧留白（无法获取时回退 146 DIP），并在 `Loaded`、窗口尺寸变化、`XamlRoot.Changed`、主题切换时重算。

实测（388×516 窗口、100% 缩放）：置顶按钮右边缘 1015，系统第一个标题栏按钮左边缘 1021，间隔 6 DIP；两次点击置顶开关状态 `Off → On → Off` 正常。

## 五、验证记录

| 检查 | 结果 |
| --- | --- |
| `dotnet build LogOrganizer.sln -c Release -p:Platform=x64` | 退出码 0，0 警告 0 错误 |
| `LogOrganizer.Core.Tests`（Microsoft.Testing.Platform 直接运行） | 61 个测试全部通过 |
| `build.ps1` 默认发布 | `publish/LogOrganizer.exe`，87,013,122 字节，SHA-256 `0669F901325C6D3C0609CAA0F5A3EB9664409C3B28BCF32150285A6D4198EDEB`，目录内仅此一个文件 |
| 自包含产物启动 | UIA 读到完整窗口与控件；`docs/screenshots/main-mica.png` |
| Mica 生效 | 同一窗口同一区域采样：Mica 模式均值 RGB(239,244,249)（随壁纸变化），纯色模式恒为 RGB(248,245,239)；对比见 `docs/screenshots/main-solid.png` |
| 设置窗口 | Mica 正常，背景下拉为 Mica / 亚克力 / 纯色；`docs/screenshots/settings-mica.png` |
| 旧配置迁移 | 真实旧文件 `UseMicaBackdrop: true` 启动后落盘为 `"Backdrop": "Mica"`；`false` 迁移为 `Solid` |
| 框架依赖发布 | `build.ps1 -FrameworkDependent` 输出 52 个文件、38.6 MB，可正常启动 |
| `git diff --check` | 通过（仅 CRLF 提示） |

> 说明：`dotnet test` 在本机曾因 VSTest testhost 缺少 `Newtonsoft.Json 13.0.1` 静默退出；测试工程改用 `MSTest.Sdk` 的 Microsoft.Testing.Platform 入口后可直接运行测试可执行文件，`dotnet test` 仍受宿主权限限制。

## 六、环境限制（非代码缺陷）

- 从受策略限制的目录启动 exe 时，写入 `%APPDATA%\log-organizer` 会被拒绝（`config.json.tmp` Access denied），界面显示“保存设置失败”。同样的二进制放到 `D:\` 根目录或 `%TEMP%` 即正常；20 行控制台程序可复现，目录 ACL 正常。这是本机环境策略，不是代码缺陷。日常使用请把 exe 放到 `D:\Apps\LogOrganizer` 等普通目录。
- 本机没有 `cdb.exe` / `windbg.exe` / `procdump.exe` / `dotnet-dump.exe`，因此留存的最小转储无法解析出托管栈；根因改由二分实验确定。

## 七、仍未验证

需要真实桌面会话的交互项：拖放导入、托盘菜单与双击、置顶/取消置顶时的静默期提示差异、归档冲突三选一、开机自启开关的实际注册与移除。

## 八、后续：0.01 公开版本（2026-11-24）

本次公开前又做了一轮收口，本文记录的历史结论不变，以下为增量：

- 项目改名为 `log-organizer` / `LogOrganizer.exe`；配置目录改为 `%APPDATA%\log-organizer` 并自动复制旧配置，旧名称的开机自启项在启动时清除。
- 运行时升级为 .NET 10 LTS 与 Windows App SDK `1.8.260921001`（保留 Win10 1809+ 支持），托盘库升级到 `2.4.1`。
- 置顶按钮改为标题栏第 4 个按钮：46×32 DIP，紧贴最小化按钮左侧，间隙 0；UIA 实测 `pin.Top == minimize.Top`、`pin.Height == 32`、`pin.Right == minimize.Left`，真实鼠标点击可切换且不触发拖窗。
- 归档窗口与设置窗口共用 `WindowChrome`（标题栏几何 + 系统按钮配色）与同一套卡片样式。
- 双击 exe 前会先检查 Windows 版本与 VC++ 运行库，缺失时弹窗说明而不是闪退。
- 发布拆成 `win-x64` 与 `win-arm64` 两个自包含单文件。
- 两个窗口的初始尺寸改为按 DPI 换算，修复 150% 缩放下窗口只有设计尺寸 2/3 的问题；设置窗口高度按显示器可用区域截断并内部滚动。
- 合并 / 跨卷复制文件夹时拒绝进入其中的联接点（`mklink /J` 用例覆盖），避免把链接指向的内容复制进归档目录。