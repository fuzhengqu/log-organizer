# 公开发布前的安全审计（0.01）

审计日期：2026-11-24。目标：把仓库公开到 GitHub 之前，确认没有泄露、没有已知漏洞、没有明显可被利用的缺陷。

## 一、结论

| 项目 | 结果 |
| --- | --- |
| 密钥 / Token / 密码 | 通过（0 命中） |
| 个人信息 / 企业内部信息 / 本机路径 | 通过（0 命中） |
| 已知漏洞依赖（NuGet） | 通过（3 个工程均报“没有易受攻击的包”） |
| 代码复审（路径穿越、重解析点、覆盖删除、注册表、日志） | 通过，修复 1 项 |
| CI 权限 | 只读构建 + 发布 job 用内置 `GITHUB_TOKEN`，无自定义 secret |
| 提交身份 | 统一为 `fuzhengqu@users.noreply.github.com` |

## 二、扫描方法与结果

扫描范围：工作树中除 `.git`、`bin`、`obj`、`publish`、`artifacts` 之外的全部文本文件（61 个），二进制与截图不参与匹配。

密钥模式（正则，逐文件匹配）：通用 API key / client secret / bearer 长串 / 连接字符串 / 遥测 key / AWS access key / GitHub token / `sk-` 前缀 key / PEM 私钥头 / Slack token / 明文密码赋值。

- 命中数：**0**。

个人信息模式：公司域名、本机用户名、本机工作区目录名、本机日志目录。

- 命中数：**0**（本文件写入时已改写为描述性说法，不保留原始字符串）。
- 处理过的历史残留：`docs/history/2026-11-24-winui-fixes.md` 中的本机源码目录已改写为“受策略限制的目录”，未保留本机绝对路径。
- 代码中保留的 `ticket-file-organizer` / `TicketFileOrganizer` 前缀是**故意的兼容代码**（旧配置目录迁移、旧自启项清理），不含任何个人信息。

绝对路径模式 `[A-Z]:\\(Users|AI|Demo|Apps)`：命中 **0**。文档中的 `D:\Downloads`、`D:\Apps\LogOrganizer`、`D:\Demo\*` 都是示例路径，不是任何人的真实目录。

依赖漏洞：

```powershell
dotnet list LogOrganizer.sln package --include-transitive --vulnerable
# LogOrganizer.Core / LogOrganizer.WinUI / LogOrganizer.Core.Tests
# 在给定当前源的情况下，给定项目“...”没有易受攻击的包。
```

依赖过时（`--outdated`）只有一条：`Microsoft.WindowsAppSDK 1.8.260921001 → 2.7.0`。**故意不升**：Windows App SDK 2.x 是否继续支持 Windows 10 尚未确认，而 Win10 1809+ 支持是本项目的硬要求。该项在 README 的已知限制里写明。

## 三、代码复审

复审范围：`LogOrganizer.Core`（路径与移动逻辑）与 `LogOrganizer.WinUI`（注册表、键盘钩子、崩溃日志、托盘、标题栏）。

### 修复：归档时不再跟随文件夹内的联接点 / 符号链接

- 位置：`ArchiveService.CopyDirectory`（跨卷复制）与 `Transfer`（合并目录时的递归入口）。
- 问题：合并或跨卷复制一个**内含联接点**的文件夹时，`Directory.EnumerateFiles(..., AllDirectories)` 会进入联接点指向的目录，可能把中转目录之外的内容复制进归档目录；如果联接点指向自身祖先，还会造成无限递归。
- 修复：`CopyDirectory` 改为逐层手工递归，遇到目录级重解析点直接抛错停止；`Transfer` 在每次递归开始时拒绝目录级重解析点。文件级重解析点（OneDrive 等云端占位文件）仍然允许，避免阻塞正常归档。
- 验证：新增单元测试 `Archive_RefusesToFollowALinkInsideAFolderBeingMerged`，用 `mklink /J` 建立真实联接点，断言该项失败、联接点后的内容未被复制、外部目录未被修改。测试总数 62，全绿。

### 已确认无问题（含理由）

| 面 | 结论 |
| --- | --- |
| 单号进入路径 | `TicketNumber` 只接受 ASCII 数字，非空；不参与任何路径解释之外的处理。 |
| 源项目边界 | 中转归档要求源项目的父目录等于中转目录（只处理顶层），否则跳过并给出原因。 |
| 拖放源与目标的关系 | 拒绝“源在目标内”“目标在源内”的组合，避免把归档目录自身移走。 |
| 重命名冲突 | `IsSimpleName` 拒绝分隔符、`.`/`..`、非法文件名字符；重命名前再查一次目标是否已存在。 |
| 覆盖 | 只有用户在冲突对话框明确选“覆盖”才删除目标；默认按钮是“跳过”，不会静默覆盖。 |
| 失败保留源 | 跨卷复制到 `*.tfo-partial-<guid>` 暂存名，校验通过才提交；任何异常都清理暂存并保留源项目。 |
| 校验强度 | 目录比对文件数量 + 每个文件大小；文件比对大小。**不做逐字节哈希**（大文件代价高），已在 README 已知限制中写明。 |
| 注册表 | 只有 `HKCU\...\Run\LogOrganizer` 一个写入点，跟随用户勾选；启动时顺手清理旧值名。不写 HKLM，不申请提权。 |
| 键盘钩子 | `WakeKeyHook` 只在用户开启“空格/回车唤醒”时安装，忽略输入框内的按键，退出时 `UnhookWindowsHookEx`。 |
| 崩溃日志 | 只写 `%LOCALAPPDATA%\LogOrganizer\crash.log`，超过 1 MB 重建；内容是本机异常文本，不外发。 |
| 网络 | 源码中不存在 `HttpClient`、`Socket`、`WebRequest`、遥测 SDK 的调用。 |
| 单实例 | 命名互斥体 `Global\LogOrganizer.SingleInstance.v1`，防止两个实例同时移动同一批文件。 |
| 清单 | `asInvoker` + `longPathAware` + `PerMonitorV2`；无 `uiAccess`。 |
| CI | `build.yml` 只读权限；`release.yml` 用 `GITHUB_TOKEN` 创建 Release，无第三方 action、无自定义 secret。 |

## 四、仓库内容清点

入库：源码、测试、文档、许可证、截图（`docs/screenshots/*.png`）、`build.ps1`、`tools/make-release.ps1`、`.github/workflows/*.yml`。

不入库（`.gitignore`）：`bin/`、`obj/`、`publish*/`、`artifacts/`（含 VC++ 运行库缓存与打包产物）、`.vs/`、`TestResults/`、`_test-temp/`、`.planning/`、`.superpowers/`。

- 仓库中不存在可执行文件、NuGet 包、字体、图标素材或任何第三方二进制。
- `VC_redist.x64.exe` / `VC_redist.arm64.exe` 只在打包时从 `https://aka.ms/vs/17/release/` 下载到 `artifacts/`，随 Release 附件分发，不进入版本库。

## 五、未覆盖的部分

- 没有做代码签名：SmartScreen 会提示，用户需用 `SHA256SUMS.txt` 自行校验。
- 没有 ARM64 实机：ARM64 包只验证“交叉编译成功 + 哈希正确”，实机启动由用户验证。
- 没有第三方渗透测试；本审计是发布前的自检，不等于安全认证。
- Windows 10 1809 实机未覆盖（本机为 Windows 11）；1809 相关约束来自 Windows App SDK 官方支持矩阵。

## 六、复现本次审计

```powershell
# 依赖漏洞
dotnet list LogOrganizer.sln package --include-transitive --vulnerable

# 密钥与个人信息（在仓库根目录执行，跳过构建产物）
Get-ChildItem -Recurse -File |
  Where-Object { $_.FullName -notmatch '[\\/](\.git|bin|obj|publish|artifacts)[\\/]' -and
                 $_.Extension -notin '.png','.exe','.dll','.pdb','.ico' } |
  Select-String -Pattern 'api[_-]?key|client[_-]?secret|gh[pousr]_|sk-[A-Za-z0-9]{20,}|BEGIN [A-Z ]*PRIVATE KEY' -Encoding UTF8

# 单元测试（62 个）
dotnet build LogOrganizer.sln -c Release -p:Platform=x64
tests\LogOrganizer.Core.Tests\bin\Release\net10.0\LogOrganizer.Core.Tests.exe
```