# 更新日志

版本号规则：每次变更 +0.01（`0.01 → 0.02 → 0.03`），不跳号。重大缺陷或重大功能变更才允许改动 `0.0x` 之外的部分，并在本文件说明理由。

## 0.01 — 2026-11-24

首个公开版本。

- 唯一实现：WinUI 3 桌面外壳；删除旧 WinForms 外壳、编译排除项与相关测试工程。
- 运行时升级到 .NET 10 LTS（`net10.0`）与 Windows App SDK 1.8 最新补丁（`1.8.260921001`），托盘库升级到 `H.NotifyIcon.WinUI 2.4.1`。
- 双架构自包含单文件发布：`win-x64` 与 `win-arm64`，目标机无需安装 .NET 或 Windows App Runtime。
- 启动前置检查：Windows 版本低于 1809、缺少 VC++ 2015-2022 运行库时给出可读提示，不再闪退。
- 置顶开关并入标题栏，成为最小化按钮左侧的第 4 个按钮（46×32 DIP，零间隙，尺寸与悬停/按下表现和系统按钮一致）。
- 设置窗口与归档窗口统一：同一套扩展标题栏、材质、卡片与页脚；页脚显示版本号；窗口尺寸按 DPI 缩放，并在小屏上自动截断高度、内部滚动。
- 启动后焦点直接落在问题单号输入框，不再停在标题栏按钮上。
- 背景三态：Mica / 亚克力 / 纯色，不可用时自动退回纯色；旧 `UseMicaBackdrop` 字段自动迁移。
- 项目更名为 `log-organizer` / `LogOrganizer.exe`；配置目录改为 `%APPDATA%\log-organizer` 并自动迁移旧配置，清理旧名称的开机自启项。
- 安全加固：合并或跨卷复制文件夹时不再跟随其中的联接点 / 符号链接（新增单元测试，测试总数 62）。
- 新增许可证（GPL-3.0-only）、SECURITY / PRIVACY / THIRD-PARTY-NOTICES、`docs/security-audit.md` 与 `docs/RELEASE.md`，以及 `tools/make-release.ps1` 与 GitHub Actions 构建/发布流程。