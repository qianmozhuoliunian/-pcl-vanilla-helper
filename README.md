# 原版直达 · VanillaReach

**原版装好，PCL 照用。**

给喜欢 PCL、也想方便安装 Minecraft Java 原版的玩家，一个专注安装的小助手。

**不用换掉熟悉的 PCL，不用接触命令行：选好目录和版本，剩下的交给原版直达。** 安装完成后，回到 PCL 选择版本，继续用你熟悉的账号、Java 和启动设置。

项目仓库：[qianmozhuoliunian/-pcl-vanilla-helper](https://github.com/qianmozhuoliunian/-pcl-vanilla-helper)。本项目为独立社区工具，**不是 PCL 官方项目**，与 PCL、Mojang、Microsoft 均无隶属或认可关系。

> **当前状态：技术预览 `v0.1.0-preview.1`。** 已在本机完成原版 `26.3` 安装、文件校验和 PCL `2.99.0.0` 识别测试；GitHub Windows 构建环境中 43 项测试通过。尚未验证实际进入游戏、干净 Windows 环境和多设备体验。[查看验证记录](docs/verification/mvp-results.md)

**[下载 Windows x64 预览版（ZIP，约 61 MB）](https://github.com/qianmozhuoliunian/-pcl-vanilla-helper/releases/download/v0.1.0-preview.1/VanillaReach-win-x64-preview.zip)** · [发布说明与 SHA-256 校验文件](https://github.com/qianmozhuoliunian/-pcl-vanilla-helper/releases/tag/v0.1.0-preview.1)

## 为什么用它？

| 你想做的事 | 原版直达怎么帮你 |
| --- | --- |
| 继续用 PCL，只补上原版安装这一步 | 安装到你指定的 `.minecraft`，完成后回到 PCL 选版本 |
| 不想研究脚本和命令行 | 中文图形窗口，选择目录 → 选择版本 → 开始安装 |
| 安装新版本，也想找历史正式版 | 从官方清单获取正式版列表，输入版本号筛选；后续版本仍需满足当前安装规则 |
| 想知道文件有没有下载完整 | 按官方清单的 SHA-1 校验版本元数据、资源与库文件，再提交版本目录 |
| 担心覆盖已有游戏文件 | 同名版本不覆盖，共享文件遇到哈希冲突就停止；写入范围不包含存档、模组与配置 |

**少学一套操作，多留一点时间玩游戏。**

## 三步安装原版

使用环境：Windows 10/11 **x64**，能够访问 Minecraft/Mojang 官方下载服务。当前已构建的自包含程序无需另装 .NET SDK；干净系统运行尚未验证，预览版需要在不同设备上继续收集反馈。

1. **选对目录。** 在 PCL 的游戏目录设置中确认正在使用的 `.minecraft` 路径；在原版直达中点“浏览…”选择它，再点“确认此目录”。软件建议的路径仅供参考。
2. **选好版本。** 输入版本号筛选，或从列表选择一个原版正式版，点击“开始安装”。
3. **回到 PCL。** 等待“安装完成”，再在 PCL 的版本列表中刷新或重新进入列表，选择刚安装的版本。Java、账号和启动设置继续由 PCL 管理。

安装时请避免其他程序同时修改同一个游戏目录。不要用本工具安装到正在运行的游戏实例中。

下载 `VanillaReach-win-x64-preview.zip` 后，**完整解压，再双击 `VanillaReach.exe`**；保留同目录的说明与许可文件。GitHub 页面中的 Source code 是开发者源码，普通玩家无需下载它或安装开发工具。

[首版预览说明](docs/releases/v0.1.0-preview.1.md) · [维护者发布步骤](docs/publishing.md)

## 先把边界说清楚

- **当前支持：** Minecraft Java 原版正式版、官方源、版本搜索、下载进度、取消、失败后重新安装。
- **当前不支持：** 基岩版、快照版、Forge/Fabric/NeoForge、整合包、Mod 更新、Java 管理、账号登录、游戏启动、自动修复与回滚。
- **网络速度取决于官方源和你的网络。** 当前没有镜像加速，也不承诺“国内满速”或绕过网络限制。
- **已有同名版本会停止安装。** 这是保护已有文件的行为，不是自动修复入口；不建议为了重装直接删除游戏目录。
- **取消不等于撤销所有已写入文件。** 本次临时目录会尝试清理，已提交的有效共享资源可能保留；强制关机或结束进程也可能留下临时目录。
- **安装成功不等于保证能进游戏。** PCL 识别之外，启动还取决于账号、Java、硬件及目标游戏版本。未逐一测试全部历史或未来版本。

## 文件与隐私

源码和发布包不附带 Minecraft 游戏本体、资源文件、PCL 程序、账号或存档。运行时由电脑直接连接允许的官方 HTTPS 地址取得安装文件，项目不搭建游戏文件中转服务。

软件没有账号系统、广告、使用统计或崩溃自动上传功能，也不会读取浏览器密码或 PCL 的登录凭据。联网下载仍会让下载服务及网络中间环节看到 IP 等必要连接信息；本地缓存与错误提示也可能包含路径。[完整隐私说明](PRIVACY.md)

## 常见问题

**我必须换启动器吗？**

不用。这个工具负责安装原版；安装完成后继续在 PCL 中选择和启动。本机已验证 PCL `2.99.0.0` 能识别测试版本，不承诺所有启动器和所有版本都已验证。

**它能让我免费获得 Minecraft 的游玩授权吗？**

不能。软件开源免费不改变游戏本身的授权与账号要求，也不提供授权绕过功能。使用游戏应遵守 [Minecraft EULA](https://www.minecraft.net/en-us/eula) 和 [Usage Guidelines](https://www.minecraft.net/en-us/usage-guidelines)。

**为什么下载失败？**

先看窗口提示，检查官方源网络连接、目录权限和磁盘空间，再重试。当前采用临时下载后再提交的方式，需要为临时文件预留空间；最终目标空间检查发生在下载后，尚未提前显示总下载量。

**把仓库地址交给豆包或其他 AI，能让它安装吗？**

可以让 AI 帮你理解构建和使用步骤。普通玩家使用图形界面无需命令行，但从源码构建需要开发工具；AI 的推荐也不能代替检查下载来源、文件校验值和实际操作内容。

## 开发与构建

需要 Windows 与 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。在仓库根目录运行：

```powershell
dotnet test PclVersionBridge.sln --configuration Release
dotnet publish src/PclVersionBridge.App/PclVersionBridge.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o .artifacts/vanillareach-win-x64
```

程序入口为 `.artifacts/vanillareach-win-x64/VanillaReach.exe`。**分发时打包整个输出目录**，保留 `LICENSE`、`THIRD-PARTY-NOTICES.md`、`PRIVACY.md` 和 `licenses/`；不要只转发 EXE。自包含包带有 .NET 运行库，体积不代表纯安装逻辑的大小。

内部解决方案、目录和命名空间暂保留 `PclVersionBridge`，不影响对外名称与使用。

## 开源、致谢与贡献

本项目自行编写的代码与文档采用 **[MIT License](LICENSE)**：允许使用、修改和再分发，包括商业使用，须保留版权与许可声明。第三方组件继续适用各自的许可；游戏文件及第三方商标不包含在本项目的 MIT 授权中。

感谢 [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) 提供安装基础能力，以及 PCL 为玩家提供的启动和管理体验。[第三方声明](THIRD-PARTY-NOTICES.md) · [完整依赖清单](docs/dependencies.md) · [发布检查记录](docs/release-review.md)

欢迎提交可复现的问题和小范围改进。反馈时提供 Windows 版本、工具版本、所选 MC 版本、错误文字及步骤；**先遮住姓名、账户、私人路径、服务器地址及令牌**，不要上传整个 `.minecraft` 或 PCL 日志目录。

只读完整性检查、环境清单和快照恢复还在需求验证阶段，当前版本没有这些功能。是否开发，取决于真实需求、成熟替代方案和后续维护成本。
