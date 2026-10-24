# 从原版直达到轻量启动器：开源项目参考调研

日期：2026-10-24
状态：源码与文档调研；用户已决定暂缓独立启动器方向，保留现有辅助安装功能。以下仅为备查建议，不是当前开发计划。

## 结论

可以在现有工具上增加独立启动能力。建议保留 C# / WPF 和 CmlLib.Core，先完成 Windows 上的原版与已有 Fabric 整合包启动流程。

现有安装、文件校验、隔离实例、Modrinth 检索和桌面 UI 可以延续。主要缺口是 Minecraft 账号认证、Java 运行时选择与安装、实例启动配置、游戏进程管理，以及可理解的故障反馈。现有好友账号不能替代 Minecraft 账号。

这些能力在成熟启动器中已经存在。因此，“漂亮且能启动”本身不足以证明产品有竞争力。可以验证的方向是让普通玩家更容易找到、安装、管理并启动 Fabric 整合包，减少接触 Java、路径和启动参数的次数。是否更省事，需要真实用户完成同一任务来比较。

## 调研方法与边界

通过 GitHub 读取各项目默认开发分支的固定提交、许可证和下列源码文件，并与本地实现对照。本次没有构建或运行这些启动器，没有据此宣称它们或本项目兼容所有游戏版本，也没有复制上游源码、图标或 UI 资源。

源码能证明某种实现存在，不能单独证明其可靠性或用户偏好。开发分支中的变化不代表已经进入稳定发布版。

## 1. Prism Launcher：实例隔离与可观测的启动流程

提交：`6f5723937f558b06eb6795d16e0994299ba9c040`

检查文件：

- [MinecraftInstance.cpp](https://github.com/PrismLauncher/PrismLauncher/blob/6f5723937f558b06eb6795d16e0994299ba9c040/launcher/minecraft/MinecraftInstance.cpp)
- [LaunchTask.cpp](https://github.com/PrismLauncher/PrismLauncher/blob/6f5723937f558b06eb6795d16e0994299ba9c040/launcher/launch/LaunchTask.cpp)
- [README 与许可说明](https://github.com/PrismLauncher/PrismLauncher/blob/6f5723937f558b06eb6795d16e0994299ba9c040/README.md)

已验证：

- 游戏目录由实例根目录派生，模组、配置和存档目录均基于实例的游戏目录。
- 创建启动任务时，按步骤组织元数据检查、Java 安装与校验、账号、依赖、内存、原生库和进程启动。
- 任务具有等待、运行、失败等状态，失败后停止后续步骤并做收尾。
- 启动任务提供敏感内容过滤接口。

适用于本项目的建议：

- 延续现有整合包实例隔离；把游戏版本、Fabric 版本、目录、Java 与内存配置归到实例。
- 显示具体启动阶段，允许启动前取消，避免界面一直显示“正在启动”。
- 导出诊断材料前移除令牌与私人路径。
- 失败不能只剩一个退出代码；保留阶段、退出状态与经过过滤的日志。

许可：项目 README 声明启动器代码为 GPL-3.0-only，标识及相关资源为 CC BY-SA 4.0。若采用其代码，需要按对应许可处理分发义务，不能仅附上本项目的 MIT 文本。本次只记录行为与架构观察。

## 2. HMCL：Java 自动选择与认证状态处理

提交：`0d20910ea302a907d6a01fec9244ab10d2993d5e`

检查文件：

- [JavaManager.java](https://github.com/HMCL-dev/HMCL/blob/0d20910ea302a907d6a01fec9244ab10d2993d5e/HMCL/src/main/java/org/jackhuang/hmcl/java/JavaManager.java)
- [MicrosoftService.java](https://github.com/HMCL-dev/HMCL/blob/0d20910ea302a907d6a01fec9244ab10d2993d5e/HMCLCore/src/main/java/org/jackhuang/hmcl/auth/microsoft/MicrosoftService.java)
- [HMCLGameLauncher.java](https://github.com/HMCL-dev/HMCL/blob/0d20910ea302a907d6a01fec9244ab10d2993d5e/HMCL/src/main/java/org/jackhuang/hmcl/game/HMCLGameLauncher.java)
- [中文说明与许可附加条款](https://github.com/HMCL-dev/HMCL/blob/0d20910ea302a907d6a01fec9244ab10d2993d5e/docs/README_zh_Hans.md)

已验证：

- Java 选择考虑游戏版本、实例清单、系统架构，以及强制和建议约束；并提供扫描、登记与下载安装入口。
- 微软认证分别处理首次认证、刷新、Xbox 认证、游戏所有权与游戏档案。
- 认证相关异常与网络、响应格式异常有区分。
- 游戏启动器建立在独立的底层启动实现之上，界面无需承担全部启动细节。

适用于本项目的建议：

- 默认自动选择满足实例要求的 Java，缺失时给出可理解的安装入口；高级用户可以手选。
- 认证超时、登录取消、未找到游戏档案等情况分别处理。
- 使用系统浏览器与适配的微软认证库；妥善保护凭据缓存，日志中不输出令牌。
- 不根据游戏版本号随意写死少数 Java 规则；验证版本元数据与加载器要求。

许可：GPLv3，并有修改版本识别、版权声明相关附加条款。当前 C# 项目没有必要为了复用其 Java 源码而重写技术栈。

## 3. PCL-CE：面向普通玩家的诊断组织方式

提交：`915ac29795efbdf6373417389e181e1504117048`

检查文件：

- [CrashAnalyzer.cs](https://github.com/PCL-Community/PCL-CE/blob/915ac29795efbdf6373417389e181e1504117048/Plain%20Craft%20Launcher%202/Modules/Minecraft/CrashAnalysis/CrashAnalyzer.cs)
- [PCL.Core JavaManager.cs](https://github.com/PCL-Community/PCL-CE/blob/915ac29795efbdf6373417389e181e1504117048/PCL.Core/Minecraft/Java/JavaManager.cs)
- [根许可证](https://github.com/PCL-Community/PCL-CE/blob/915ac29795efbdf6373417389e181e1504117048/LICENSE)
- [特殊目录许可](https://github.com/PCL-Community/PCL-CE/blob/915ac29795efbdf6373417389e181e1504117048/Plain%20Craft%20Launcher%202/LICENCE)
- [PCL.Core 说明](https://github.com/PCL-Community/PCL-CE/blob/915ac29795efbdf6373417389e181e1504117048/PCL.Core/README.md)

已验证：

- 崩溃分析入口把日志收集、导入、整理、检测与结果展示分开。
- Java 核心采用扫描器与解析器协作，并保存已登记运行时。
- 根许可证区分目录：PCL.Core 等目录采用 Apache-2.0，Plain Craft Launcher 2 目录另有自定义许可与使用指南。

适用于本项目的建议：

- 提供“发生了什么、下一步怎么做、查看详细日志”三层错误展示。
- 首版只解释有证据的已知故障，不承诺自动判断所有崩溃原因。
- 自定义许可目录只作此次产品观察记录，不以其实现为基础移植诊断代码。若以后考虑采用明确 Apache-2.0 的核心组件，仍需逐文件核对归属与依赖。

## 4. NexaCL：补充观察，不作为当前整体改造模板

提交：`bc5a787e1f3c1bc3d2eba5af3a4f7e8bb9c32149`

检查文件：

- [README](https://github.com/Nexa-MC/NexaCL/blob/bc5a787e1f3c1bc3d2eba5af3a4f7e8bb9c32149/README.md)
- [JavaRuntimeManagementService.cs](https://github.com/Nexa-MC/NexaCL/blob/bc5a787e1f3c1bc3d2eba5af3a4f7e8bb9c32149/Nexa.Services.Minecraft.Java/Minecraft/Java/JavaRuntimeManagementService.cs)
- [桌面项目文件](https://github.com/Nexa-MC/NexaCL/blob/bc5a787e1f3c1bc3d2eba5af3a4f7e8bb9c32149/Nexa.Desktop/Nexa.Desktop.csproj)
- [LICENSE](https://github.com/Nexa-MC/NexaCL/blob/bc5a787e1f3c1bc3d2eba5af3a4f7e8bb9c32149/LICENSE)

README 标识 Alpha 12；根许可证为 Apache-2.0。Java 管理代码中存在清单修订检查、托管运行时边界与删除前校验，值得关注。桌面入口依赖自有渲染、协议与服务组成，不能由“同为 .NET”推断适合整套迁入。此次未评估其稳定性与全部依赖许可。

## 5. 当前最适合继续复用的底层：CmlLib

本地已使用 CmlLib.Core 4.1.0；src/PclVersionBridge.Core/Download/CmlInstallSource.cs 当前创建安装器时显式禁用了 Java 文件提取。安装功能存在不代表 Java 管理与启动功能已经接通。

[上游源码与快速入门](https://github.com/CmlLib/CmlLib.Core) 提供构建游戏进程与启动的示例，并声明 MIT 许可。微软认证是另一个集成工作，应单独验证相关包的版本、许可和凭据存储。

[认证库的 Client ID 文档](https://cmllib.github.io/CmlLib.Core-wiki/en/auth.microsoft/xboxauthnet.game.msal/clientid/) 要求自己的应用注册，并提示 Minecraft API 对应用识别的要求。本文没有核实当前官方申请流程的全部条件，也没有验证本项目的应用凭据可用。因此登录接入必须作为首个可行性验证，不能承诺接上依赖便可公开发布。

## 建议的第一版范围

建议对用户只呈现以下主流程：

1. 我的游戏：选择已安装实例，显示版本、加载器与最近启动状态。
2. 登录 Minecraft 账号：与现有好友系统的账户入口明确区分。
3. 启动：自动确认 Java、依赖和实例目录，展示当前阶段。
4. 退出或失败：恢复界面；失败时展示经过核实的原因与可执行处理办法。
5. 保留当前原版、Modrinth Fabric 整合包安装入口。

首个验收目标：使用本项目自己的认证配置，在隔离目录中完成一次正版原版启动，实际到达游戏主菜单，退出后正确恢复状态；接着验证一个已支持的 Fabric 整合包。仅创建了 Java 进程或测试全部通过，不等同于完成此验收。

需要同时验证的边界：

- 登录取消、凭据过期、网络中断、Java 缺失与不匹配。
- 中文或空格路径、无效目录、启动失败、重复点击启动。
- 日志与故障报告不泄露令牌；实例操作不影响其他游戏与存档。

建议先完成以上验证，再决定是否扩大 Forge / NeoForge 支持、云端功能或更复杂的模组管理。现有好友功能继续保持可选，不成为本地启动的前提。

## 若未来重启此方向

先做一个独立的启动可行性验证，确认自有应用的认证可用、选定版本能进主菜单，再把验证过的流程接入现有界面。这样能尽早发现外部账号接入限制，并让后续界面设计建立在实际可用的能力上。
