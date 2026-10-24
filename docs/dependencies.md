# 依赖与许可清单

核对日期：2026-10-21。依据当前应用和测试项目的 `project.assets.json`、NuGet 包内 `.nuspec` 与上游许可文件。以下不列开发机包缓存的私人路径。

## 应用运行依赖

| 包 | 版本 | 许可 | 来源 |
| --- | --- | --- | --- |
| CmlLib.Core.Commons | 4.0.0 | MIT | [NuGet](https://www.nuget.org/packages/CmlLib.Core.Commons/4.0.0) |
| CmlLib.Core | 4.1.0 | MIT | [NuGet](https://www.nuget.org/packages/CmlLib.Core/4.1.0) |
| LZMA-SDK | 19.0.0 | MIT | [NuGet](https://www.nuget.org/packages/LZMA-SDK/19.0.0) |
| SharpZipLib | 1.4.2 | MIT | [NuGet](https://www.nuget.org/packages/SharpZipLib/1.4.2) |

随 Windows x64 自包含包另附 .NET Runtime 与 Windows Desktop/WPF 10.0.13（MIT，内部第三方组件见原始 notices）。NuGet 的 System.Text.Json 8.0.5 和 System.Threading.Tasks.Dataflow 8.0.1 虽见于 CmlLib 的依赖声明，但在当前 net10.0 还原结果中被框架提供的版本替代，不是额外随包复制的这两个旧包。

## 可选好友服务新增依赖（2026-10-23）

仅独立好友服务使用以下新增运行组件；WPF 客户端使用系统 Windows DPAPI，没有新增加密包。根据服务端 `project.assets.json` 与实际 NuGet 包元数据核对：

| 包/组件 | 版本 | 许可 | 本地许可 |
| --- | --- | --- | --- |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.0 | MIT | `licenses/AspNetCore-LICENSE.txt` |
| Microsoft.EntityFrameworkCore、Abstractions、Relational、Sqlite、Sqlite.Core；Microsoft.Data.Sqlite.Core | 10.0.0 | MIT | `licenses/EFCore-LICENSE.txt` |
| Microsoft.Extensions.DependencyModel | 10.0.0 | MIT | `licenses/DotNet-Runtime-LICENSE.txt` |
| SQLitePCLRaw.bundle_e_sqlite3、core、lib.e_sqlite3、provider.e_sqlite3 | 2.1.11 | Apache-2.0 | `licenses/SQLitePCLRaw-LICENSE.txt` |

EF Core Analyzers 10.0.0 为构建时组件（MIT）。服务端还使用对应 .NET / ASP.NET Core 运行时，发布时需连同上游 Runtime notices 分发。本表不表示已部署公共服务；完整好友配置见 [说明](friends-setup.md)。SQLitePCLRaw 的许可来自上游固定 `v2.1.11`，ASP.NET Core/EF Core 来自 `v10.0.0` 许可文件，不将根项目 MIT 套用到这些依赖。

## 构建与测试依赖

以下包不作为应用程序的独立运行依赖分发。源码只保留引用，由还原过程获取；未将包二进制或第三方测试源码放入仓库。

| 包 | 版本 | 许可 | 来源 |
| --- | --- | --- | --- |
| Microsoft.CodeCoverage | 17.14.1 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.CodeCoverage/17.14.1) |
| Microsoft.NET.ILLink.Tasks | 10.0.13 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.NET.ILLink.Tasks/10.0.13) |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1) |
| Microsoft.TestPlatform.ObjectModel | 17.14.1 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/17.14.1) |
| Microsoft.TestPlatform.TestHost | 17.14.1 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.TestPlatform.TestHost/17.14.1) |
| Newtonsoft.Json | 13.0.3 | MIT | [NuGet](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3) |
| coverlet.collector | 6.0.4 | MIT | [NuGet](https://www.nuget.org/packages/coverlet.collector/6.0.4) |
| xunit.abstractions | 2.0.3 | Apache-2.0（旧包为 URL 声明，另核对上游） | [NuGet](https://www.nuget.org/packages/xunit.abstractions/2.0.3) |
| xunit.analyzers | 1.18.0 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit.analyzers/1.18.0) |
| xunit.assert | 2.9.3 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit.assert/2.9.3) |
| xunit.core | 2.9.3 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit.core/2.9.3) |
| xunit.extensibility.core | 2.9.3 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit.extensibility.core/2.9.3) |
| xunit.extensibility.execution | 2.9.3 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit.extensibility.execution/2.9.3) |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.4) |
| xunit | 2.9.3 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit/2.9.3) |

`Microsoft.NET.ILLink.Tasks` 是单文件发布使用的构建工具；应用关闭 trimming，未把该工具当成程序功能。`xunit.abstractions/2.0.3` 没有 SPDX 表达式，包给出的旧许可 URL 已另与 [abstractions.xunit 上游许可](https://github.com/xunit/abstractions.xunit/blob/main/license.txt) 核对；其包未附独立许可文件，不宣称已复现其历史源提交。

## 文件、图形与文档

- 应用源代码、测试和本项目说明：根目录 MIT License；对外名称采用原版直达 · VanillaReach。
- 界面图形由 XAML 绘制；无随仓库分发的游戏/PCL 图标、截图、音乐或字体文件。Segoe UI 来自操作系统。
- Minecraft 游戏文件不是本项目组成部分，不上传源码仓库，不打包到程序 Release。
- 官方名称、商标与第三方组件不因本项目采用 MIT 而转让任何权利。

## 更新规则

更换依赖或 SDK 后重新还原、核对实际图谱与随包文件，更新本表和根目录第三方声明；检查 EXE 及 `licenses/` 一起分发。仅检查直接包引用、仅贴一个 MIT 链接或仅在 README 写“非官方”，都不能代替第三方声明。
