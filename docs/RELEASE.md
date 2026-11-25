# 发布流程

版本号只有一处来源：根目录 `Directory.Build.props` 的 `<Version>`。改版本号后重新打包即可，其它文件不用手改。

## 一、本地打一个版本

```powershell
# 1. 改版本号
#    Directory.Build.props: <Version>0.03</Version>

# 2. 全量构建 + 测试
dotnet build LogOrganizer.sln -c Release -p:Platform=x64
tests\LogOrganizer.Core.Tests\bin\Release\net10.0\LogOrganizer.Core.Tests.exe

# 3. 打两个架构的包（自动下载微软官方 VC++ 运行库）
.\tools\make-release.ps1

# 4. 回到 100% 缩放、浅色主题的桌面，重拍截图（见第四节）
```

`tools/make-release.ps1` 产物（全部在 `artifacts\`，不进版本库）：

| 文件 | 内容 |
| --- | --- |
| `LogOrganizer-<版本>-win-x64.zip` | `LogOrganizer.exe` + `VC_redist.x64.exe` + `使用说明.txt` |
| `LogOrganizer-<版本>-win-arm64.zip` | `LogOrganizer.exe` + `VC_redist.arm64.exe` + `使用说明.txt` |
| `LogOrganizer-<版本>-win-x64.exe` | 干净的单文件，给只想下一个 exe 的人 |
| `LogOrganizer-<版本>-win-arm64.exe` | 同上（ARM64） |
| `SHA256SUMS.txt` | 上面四项的 SHA-256 |

参数：`-SkipArm64` / `-SkipX64` 只打一个架构，`-SkipRedist` 不下载运行库，`-Version 0.03` 覆盖版本号，`-RedistDir` 指定运行库缓存目录。

## 二、走 CI 打一个版本

推一个 `v*` tag，`.github/workflows/release.yml` 会：

1. 在 `windows-latest` 上还原、构建；
2. 跑 `tools/make-release.ps1`（runner 上 cross-build ARM64，同时下载两份 VC++ 运行库）；
3. 用 `GITHUB_TOKEN` 创建或更新对应 Release，附上四个文件与 `SHA256SUMS.txt`。

```powershell
git tag v0.03
git push origin v0.03
```

`build.yml` 在每次 push 到 `main` 与 PR 时跑构建、单元测试和两个架构的发布，产物作为 Actions artifact 保存。

## 三、ARM64 的边界

- ARM64 包由 x64 runner 交叉编译，只验证“能编译、能产出、哈希一致”。
- CI 与 x64 开发机都无法启动 ARM64 二进制，**实机启动由 Windows on ARM 用户验证**。
- 发布说明里必须如实写这一点，不能写成“已在 ARM 设备上测试”。

## 四、截图

截图脚本不入库（含本机窗口操作），但要求固定：

1. 用演示目录 `D:\Demo\_收件`（中转）与 `D:\Demo\Archive`（目标），不要出现真实工作路径。
2. 100% 缩放、浅色主题、单显示器；截图前最小化其它窗口，避免把别的应用内容拍进 Mica 背景。
3. 置顶按钮必须紧贴最小化按钮左侧、无间隙。
4. 保存到 `docs/screenshots/`：`main-mica.png`、`settings-mica.png`、`main-solid.png`。
5. 裁剪到 DWM 的可见边框（`DWMWA_EXTENDED_FRAME_BOUNDS`），不要带窗口阴影。

## 五、发布前检查

- [ ] `LICENSE`、`SECURITY.md`、`PRIVACY.md`、`THIRD-PARTY-NOTICES.md` 存在且日期/版本正确。
- [ ] `CHANGELOG.md` 有本次条目，版本号与 tag 一致。
- [ ] 单元测试全绿（当前 73 个）。
- [ ] `dotnet list LogOrganizer.sln package --include-transitive --vulnerable` 无输出告警。
- [ ] 仓库里没有本机路径、个人邮箱、企业信息（见 `docs/security-audit.md`）。
- [ ] 两个 zip 解压后都能看到 `LogOrganizer.exe`、`VC_redist.*.exe`、`使用说明.txt`。
- [ ] `SHA256SUMS.txt` 与附件实际哈希一致（`certutil -hashfile <文件> SHA256`）。
- [ ] Release 说明写清：x64 已实机验证 / ARM64 待实机验证、需要 VC++ 运行库、未签名会触发 SmartScreen。
- [ ] 发布说明写在该版本的 `docs/release-notes/<版本>.md`（去掉 `v`），`release.yml` 发现该文件就用它当正文；入库后随 tag 一起发布，别只在网页上手改。
- [ ] 手工确认：托盘右键六项都能执行（含“打开目标目录”在未设置时置灰）、两个窗口的最大化按钮可用且不能缩到最小尺寸以下、启用快捷键后按组合键能唤醒并聚焦单号框、组合被占用时设置页给出提示。
- [ ] 手工确认：主操作按钮（“归档”“保存设置”）鼠标悬停时外观不变、按住时加深、禁用时中性灰；深色主题下文字为深棕。