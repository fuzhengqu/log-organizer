# 启动时置顶监控中转目录实施计划

> **For agentic workers:** 本任务由当前会话原生执行。步骤用 checkbox 跟踪；不要提交或推送。

**Goal:** 启动时已置顶时保持中转目录监控开启，同时冻结静默期提示，并在取消置顶后恢复。

**Architecture:** 在 `InboxWatcher` 启动时支持“启动后冻结静默期”的选项：先启用监控并扫描现有项目，再冻结静默期。`TrayApplicationContext` 根据 `PanelPinned` 传入该选项；置顶不再调用会关闭文件系统监控的 `Pause()`。Core 测试直接验证该启动契约及置顶期间的新项目追踪。

**Tech Stack:** .NET 8、C#、MSTest 4.2.3、WinUI 3。

**Spec:** `docs/superpowers/specs/2026-11-20-pinned-watcher-monitoring-design.md`

## Global Constraints

- 纯 ASCII 数字工单号；仅用户明确点击归档才移动文件。
- 置顶期间持续监控中转目录，抑制静默期提示，新中转项目仍进入列表；取消置顶恢复提示。
- 保留失败项目并说明原因；不改变冲突处理、托盘生命周期或其他既有行为。
- 不提交、不推送，不覆盖或清理已有工作区内容。

## Review Focus

- 启动即置顶：文件监控必须处于运行状态，初始扫描的项目仍可见。
- 置顶期间新增文件：必须入列并重置被冻结的剩余静默期，不能触发提示。
- 取消置顶：等待的是冻结后的剩余静默期，而不是置顶期间流逝的时间。
- 普通启动及显式暂停：行为保持不变。
- 后台 FileSystemWatcher 回调：新测试给回调留出处理窗口，避免与可变测试时钟竞态。

---

### Task 1: 对齐置顶启动与运行时监控行为

**Files:**
- Modify: `src/LogOrganizer.Core/InboxWatcher.cs`
- Modify: `src/LogOrganizer.WinForms/TrayApplicationContext.cs`
- Test: `tests/LogOrganizer.Core.Tests/InboxWatcherTests.cs`

**Interfaces:**
- `InboxWatcher.Start(bool suspendQuietPeriod = false)`：启用 watcher、扫描中转目录；参数为 true 时冻结刚建立的静默期，但不暂停 watcher。
- `TrayApplicationContext.StartWatcher()` 根据 `_settings.PanelPinned` 调用启动接口；置顶不调用 `Pause()`。

- [x] **Step 1: Write the failing Core test**

在 `InboxWatcherTests` 中新增 `PinnedStartKeepsWatchingAndResumesQuietPeriodAfterNewActivity`：预置一个现有文件，以 `Start(suspendQuietPeriod: true)` 启动；断言 `IsRunning` 为 true 且现有文件已入列；推进时钟 7 秒后创建新文件并 `Rescan()`；等待 250ms 让 watcher 回调处理；断言 watcher 仍运行、两个项目都在列表中；推进时钟两分钟并确认没有 ready batch；恢复静默期后推进 9 秒确认未到期，再推进 1 秒确认两个项目都在 ready batch。

- [x] **Step 2: Run the focused test and verify it fails to compile**

Run: `dotnet build tests\LogOrganizer.Core.Tests\LogOrganizer.Core.Tests.csproj -c Release --no-restore -v minimal`
Expected: 编译失败，指出 `InboxWatcher.Start` 尚不接受 `suspendQuietPeriod` 参数。

- [x] **Step 3: Implement the startup contract**

在 `InboxWatcher.Start(bool suspendQuietPeriod = false)` 中保留 `EnableRaisingEvents = true` 与 `Rescan()`；完成扫描后仅在参数为 true 时调用 `ResetQuietPeriod()`。在 `TrayApplicationContext.StartWatcher()` 用 `_settings.PanelPinned` 传入该参数，并移除置顶时调用 `watcher.Pause()` 的分支。

- [x] **Step 4: Run tests and build**

Run: `dotnet build tests\LogOrganizer.Core.Tests\LogOrganizer.Core.Tests.csproj -c Release -v minimal`
Expected: BUILD SUCCEEDED。

Run: `tests\LogOrganizer.Core.Tests\bin\Release\net8.0\LogOrganizer.Core.Tests.exe --no-ansi --report-trx`
Expected: Core 测试全部通过，TRX 写入默认 TestResults 目录。

Run: `dotnet build LogOrganizer.sln -c Release -p:Platform=x64 -v minimal`
Expected: BUILD SUCCEEDED，0 errors；并记录实际 warning 数。

- [x] **Step 5: Check the diff without disturbing existing work**

Run: `git diff --check; git status --short`
Expected: 无空白错误；仅确认改动与新增设计/计划文档及已有用户改动，保留所有既有内容；不提交、不推送。

---

## WinUI 桌面验收补充（2026-11-20）

- [x] 在临时副本和临时配置路径运行 UI Automation：窗口可启动；置顶切换状态可见且持久化；置顶期间创建的临时文件进入待归档列表；取消置顶状态可恢复；目标目录仍为空，证明列表/监控操作不会自动移动文件。
- [x] 调用“添加文件”按钮；应用保持响应，专用临时测试文件仍在临时收件目录、临时目标目录未变化。观察到的 OpenCode 宿主窗口无法归因为系统文件选择器，故只验证按钮调用与应用稳定性，未验证成功选择/添加文件。
- [x] 使用独立临时来源与目标，填入单号 `741` 并明确点击“归档”；确认文件移动到临时 `target\741` 且源文件消失，验证实际归档仅由明确动作触发。验收后删除临时文件及目录。
- [ ] 冲突归档隔离探测确认窗口进入“正在安全归档”、源和已有目标文件未变；UIA 未暴露模态 `ContentDialog`，向经 PID 校验的可见主 HWND 投递 Esc 也未结束流程，故重命名、覆盖、跳过决策均未验收。测试样本和空目录已清理。
- [x] 早期隔离验收曾在各轮后恢复启动前配置；2026-11-20 最终核对的 SHA-256 记录为 `C3F071B07EAFD44CA37BDBD1663FA9FDB8E6747E375BB6B2C08E6681337341CB`，偏好为未置顶、Mica、0.92、坐标 766/258。2026-11-23 后续复测的配置隔离假设被证实不成立，详见 `.superpowers/sdd/2026-11-20-pinned-watcher-monitoring/progress.md` 中的安全更正；当前配置仅恢复并核实了这些语义值，SHA-256 为 `C3F071B07EAFD44CA37BDBD1663FA9FDB8E6747E375BB6B2C08E6681337341CB`，不能声称与复测前字节一致。真实收件目录已确认为空后移除；未执行归档操作。
- [ ] 设置窗口、拖放、托盘菜单/双击及冲突选项（重命名/覆盖/跳过）仍未验收。2026-11-23 隔离重试中，标准发布副本主窗可启动，点击设置后进程退出；Windows Application Error 记录 `Microsoft.UI.Xaml.dll` 3.1.8、异常 `0xc000027b`、偏移 `0x3a7515`（12:39:06）。另一已存在的发布变体可启动，但其 UIA 主窗口树未暴露设置按钮，故不能验证设置对话框。冲突归档进入忙碌状态但 UIA 未暴露模态 `ContentDialog`，向 PID 校验的可见主 HWND 投递 Esc 仍未解除等待。崩溃原因未定位。WinForms 测试项目构建通过，但 VSTest 因查询父进程时 `Win32Exception (5): Access is denied` 中止，2 项测试均无运行结果。
- [ ] `dotnet test ...LogOrganizer.WinForms.Tests.csproj -c Release -p:EnableWinUITests=true` 在此环境中止于 VSTest testhost 对父进程查询的 Win32 access denied；WinForms 测试项目编译通过（0 warning/0 error），但 2 项测试未取得运行结果。Core 测试 57/57 通过。
- [ ] WinUI 项目普通设置下隔离发布版仍有 XAML 加载失败风险；临时副本中移除多项项目属性后的启动成功尚未逐项定位根因，故未把该组试验配置回写正式项目。
