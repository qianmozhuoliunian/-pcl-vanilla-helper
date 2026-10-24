# Third-party notices / 第三方组件声明

原版直达（VanillaReach）的根目录 MIT License 只授权本项目拥有权利的代码与文档，不替代下面组件的许可证。发布二进制包时，必须保留本文件与整个 `licenses/` 目录。

## 随 Windows x64 自包含程序分发的组件

以下清单根据当前 NuGet 还原结果、包内 `.nuspec`、上游许可和 .NET 运行包核对，核对日期为 2026-10-21。

| 组件 | 版本 | 许可与版权声明 | 本地许可文本 |
| --- | --- | --- | --- |
| [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) | 4.1.0 | MIT; Copyright (c) 2023 AlphaBs（NuGet），Copyright (c) 2020 권세인(AlphaBs)（源码许可） | [LICENSE](licenses/CmlLib.Core-LICENSE.txt) |
| [CmlLib.Core.Commons](https://github.com/CmlLib/CmlLib.Core.Commons) | 4.0.0 | MIT; Copyright (c) 2024 AlphaBs（NuGet），Copyright (c) 2024 CmlLib（源码许可） | [LICENSE](licenses/CmlLib.Core.Commons-LICENSE.txt) |
| [LZMA-SDK](https://github.com/monemihir/LZMA-SDK) | 19.0.0 | NuGet 封装为 MIT; Copyright (c) 2020 Mihir Mone。不得与其他版本的 LZMA/7-Zip 许可混同 | [LICENSE](licenses/LZMA-SDK-LICENSE.txt) |
| [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) | 1.4.2 | MIT; Copyright © 2000-2022 SharpZipLib Contributors（NuGet），源码许可保留其原始 2000-2018 声明 | [LICENSE](licenses/SharpZipLib-LICENSE.txt) |
| [.NET Runtime](https://github.com/dotnet/runtime) | 10.0.13 | MIT; Copyright (c) .NET Foundation and Contributors；内部第三方组件另见上游 notices | [LICENSE](licenses/DotNet-Runtime-LICENSE.txt)、[third-party notices](licenses/DotNet-Runtime-THIRD-PARTY-NOTICES.txt) |
| [.NET Windows Desktop / WPF](https://github.com/dotnet/wpf) | 10.0.13 | MIT; Copyright (c) .NET Foundation and Contributors；内部第三方组件另见上游 notices | [LICENSE](licenses/WPF-LICENSE.txt)、[third-party notices](licenses/WPF-THIRD-PARTY-NOTICES.txt) |

所有 MIT 组件的完整许可文本均在对应文件中，不以超链接代替随包分发的声明。下载到用户电脑的 Minecraft 文件及其第三方库，继续适用其各自的许可，不因本工具而变为 MIT。

## 独立好友服务（开发预览）

好友服务另使用 ASP.NET Core Identity、EF Core/SQLite 10.0.0（MIT）与 SQLitePCLRaw 2.1.11（Apache-2.0）。完整许可随包位于 `licenses/AspNetCore-LICENSE.txt`、`licenses/EFCore-LICENSE.txt`、`licenses/SQLitePCLRaw-LICENSE.txt`，具体组件见 [依赖清单](docs/dependencies.md)。独立服务的 .NET/ASP.NET Core 运行时及附带 notices 也需一起保留。WPF 好友页的首字占位头像由界面文字组成，不使用 MC 皮肤、PCL 图标或生成的人物图片。

## 仅用于开发、构建和测试

Microsoft.NET.ILLink.Tasks、Microsoft.NET.Test.Sdk、Microsoft.TestPlatform.*、Microsoft.CodeCoverage、coverlet.collector、Newtonsoft.Json 和 xUnit 组件见[完整依赖清单](docs/dependencies.md)。它们的包缓存、SDK、测试二进制与报告不作为应用发布包的一部分分发。

xUnit 当前测试依赖采用 Apache-2.0；旧版 `xunit.abstractions` 包仅给出许可 URL，已另核对其[上游许可声明](https://github.com/xunit/abstractions.xunit/blob/main/license.txt)，未把缺失 SPDX 字段误记为“无许可”。

## 资源与品牌

整合包检索中的图标、名称、简介和统计来自 Modrinth 项目公开资料，由原发布者提供；它们不因此成为本项目 MIT 授权的内容。应用安装包不预置这些项目图片，使用时按需读取，保留项目来源入口。没有为此新增图像处理依赖，缩略显示使用 Windows/WPF 解码。

- 当前应用界面由仓库内 XAML 绘制；没有随包分发 PCL 图标、Minecraft 贴图、音乐、字体文件或游戏截图。
- 界面引用 Windows 已安装的 Segoe UI 字体，不把字体文件打包。
- PCL、Minecraft、Mojang、Microsoft 及各自标识属于其权利人。名称用于说明适用场景，不表示官方授权、赞助或关联。
- 本项目不随包分发 PCL EXE、Minecraft 游戏本体、游戏资源、账户配置、玩家存档或未授权模组。

## 许可文本来源与维护

- CmlLib.Core：包元数据指向的提交 `ffa2e908a9aa545c13175ec7b700472220e6d280` 中的 `LICENSE`。
- CmlLib.Core.Commons：包元数据指向的提交 `aa96916cde628dcd74d4e82b5bfb20589398af1e` 中的 `LICENSE`。
- SharpZipLib：`v1.4.2` 的 `LICENSE.txt`；包元数据指向提交 `33f64eb0f28cdd2b084cb822fcc224c7c5aba553`。
- LZMA-SDK：19.0.0 包内许可表达式 MIT，与上游仓库 `LICENSE` 核对；该包没有源提交字段，不能声称做过精确源码复现。
- .NET Runtime 与 WPF：`Microsoft.NETCore.App.Runtime.win-x64/10.0.13`、`Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.13` 包附带的许可与 Runtime notices；WPF notices 取自上游 `v10.0.13`。

更换 NuGet 包、目标框架、运行库版本或打包方式后，应重新核对依赖和声明。当前清单是这次构建的检查记录，不代表其他版本也已经检查。
