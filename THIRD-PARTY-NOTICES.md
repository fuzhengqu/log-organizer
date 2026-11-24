# 第三方组件与许可

本程序随发布产物一起分发了下列第三方组件。本文件只做事实记录，不改变各组件的许可条款。

## 运行时组件（随程序分发）

| 组件 | 版本 | 许可 | 来源 |
| --- | --- | --- | --- |
| .NET Runtime / Windows Desktop Runtime | 10.0 LTS | MIT | https://github.com/dotnet/runtime |
| Windows App SDK（含 WinUI 3、Windows App Runtime） | 1.8.260921001 | Microsoft Windows App SDK 许可条款（允许随应用再分发） | https://github.com/microsoft/WindowsAppSDK |
| H.NotifyIcon.WinUI / H.NotifyIcon / H.GeneratedIcons.System.Drawing | 2.4.1 | MIT | https://github.com/HavenDV/H.NotifyIcon |
| Microsoft.Web.WebView2（Windows App SDK 传递依赖） | 1.0.3179.45 | Microsoft 可再分发许可 | https://learn.microsoft.com/microsoft-edge/webview2/ |
| System.Drawing.Common / System.Numerics.Tensors / Microsoft.Win32.SystemEvents | 10.0.0 / 9.0.0 / 10.0.0 | MIT | https://github.com/dotnet/runtime |

## 仅开发/测试使用（不随程序分发）

| 组件 | 版本 | 许可 |
| --- | --- | --- |
| MSTest.Sdk / MSTest.TestFramework / MSTest.TestAdapter | 4.5.3 | MIT |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.4654 | Microsoft 许可条款 |

## 许可兼容性提示

- .NET 运行时、H.NotifyIcon 与 MSTest 均为 MIT，与本项目的 GPL-3.0-only 兼容。
- **Windows App SDK 使用微软自有许可条款**（允许把运行时随应用一起再分发，但并非 MIT）。GPLv3 与这类平台组件共存的解释在业界尚有讨论空间；如果你计划把它用于商业分发或需要严格的法律确定性，建议在发布前做一次法务确认，或改用宽松许可证。
- 本项目不附带任何担保，详见 [LICENSE](LICENSE)。

## 商标

Windows、Windows 11、Windows App SDK 是 Microsoft Corporation 的商标。本项目与微软无隶属关系，也未获其背书。