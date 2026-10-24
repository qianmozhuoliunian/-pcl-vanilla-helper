# 整合包安装 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在原版直达内搜索或导入受支持的 Fabric 整合包，完整安装到新的 PCL 隔离版本。

**Architecture:** Modrinth 目录、mrpack 解析与网络策略独立于现有原版模块。暂存所有内容并验证后复用提交器，只提交完整父版本和新的隔离实例。WPF 以新页签接入，原版功能保持可用。

**Tech Stack:** .NET 10 / WPF / CmlLib.Core 4.1.0 / System.IO.Compression / System.Text.Json / xUnit。

**Spec:** `docs/superpowers/specs/2026-10-22-modpack-install-design.md`（用户已确认范围；随后明确要求 run，本会话顺序执行）。

## Global Constraints

- 首版仅 Modrinth / .mrpack / Minecraft 正式版 / Fabric；拒绝其他加载器、未知必需依赖与非允许文件来源。
- 不改现有原版官方主机限制，不覆盖用户版本、存档或配置，不执行包内脚本。
- 包文件 512 MiB、索引 8 MiB、条目 20,000 个、单文件 1 GiB、实际展开总量 4 GiB、模组下载总量 8 GiB。
- 下载检查 SHA-1、SHA-512、长度；包文件与客户端 overrides 均受路径、链接、保留目录和大小检查。
- 安装可取消；同一时间一个安装；仅新实例写 PCL 隔离标志。
- 未实际验证的网络表现、PCL 版本与进入游戏情况分别注明。

## Review Focus

1. 本地文件预览后发生变化：安装前重新读并核对包哈希，不能安装与预览不符的内容（Task 3）。
2. 可选项、客户端覆盖层和大小写碰撞：规范允许的跨层覆盖生效，层内碰撞拒绝（Task 1/3）。
3. 用户已有原版父版本与预期不同：不覆盖、不将不完整内容当可复用父版本（Task 4）。
4. 搜索或版本请求乱序返回：旧结果不得替换新选择（Task 5）。
5. 提交成功前取消或路径被并发创建：不得出现可选择的半成品包实例（Task 4）。

## 文件结构与接口

新增 `Core/Modpacks/`：`ModpackModels.cs`（目录、版本与包计划记录）、`MrpackReader.cs`（格式/归档验证）、`ModpackNetwork.cs`（网络白名单/重试/大小限制）、`ModrinthCatalog.cs`（目录 API）、`ModpackContentSource.cs`（包与文件暂存）、`FabricInstallSource.cs`（加载器与库）、`ModpackInstallCoordinator.cs`（预览/执行/安全提交）。新增 `Core/Presentation/ModpackViewModel.cs`、`App/ModpackView.xaml[.cs]`。修改主窗口接入页签和共享安装锁；不把所有逻辑塞进主窗口。

共用记录采用不可变列表：`ModpackProject(Id, Slug, Title, Author, Description)`，`ModpackRelease(Id, ProjectId, Name, VersionNumber, GameVersions, ReleaseType, FileUrl, FileSize, Sha1, Sha512)`；`MrpackPlan(Name, VersionId, MinecraftVersion, FabricVersion, Files, Overrides)`；`PackPreview(ArchivePath, ArchiveSha512, Plan)`。

接口边界：`IModrinthCatalog.SearchAsync(string query, string? gameVersion, int offset, CancellationToken)` 返回分页 `ModpackSearchPage`；`GetVersionsAsync(string projectId, bool includePrerelease, CancellationToken)` 返回版本列表。`MrpackReader.ReadAsync(string archivePath, CancellationToken)` 返回 `PackPreview`。`IModpackInstallService.InstallAsync(PackPreview preview, string instanceId, string targetRoot, IReadOnlySet<string> optionalPaths, IProgress<InstallProgress> progress, CancellationToken)` 完成安装。

### Task 1: mrpack 解析与安全边界

**Files:** Create `Core/Modpacks/ModpackModels.cs`, `MrpackReader.cs`; Test `tests/PclVersionBridge.Tests/MrpackReaderTests.cs`, `ModpackTestFiles.cs`。

**Interfaces:** Produces 上述 `PackPreview` 与 `MrpackPlan`；文件记录含目标路径、两个哈希、大小、允许下载地址和是否可选；覆盖记录含原归档条目名、目标路径与层次。

- [ ] 写最小有效包、缺失环境、可选/服务端文件、未知依赖/加载器、坏哈希/URL、穿越/保留目录/重复/大小写碰撞、覆盖层顺序、归档上限的测试。断言计划内容或拒绝，不能只断言异常文本。
- [ ] 运行 `dotnet test --filter FullyQualifiedName~MrpackReaderTests`，确认缺失功能失败。
- [ ] 实现严格 JSON 和 ZIP 读入；逐层冲突检查；客户端文件选择；SHA-512 绑定预览内容。所有实际流读取有上限。
- [ ] 运行完整测试，全部通过后提交 `feat: validate client mrpack plans safely`。

### Task 2: 网络策略与 Modrinth 目录

**Files:** Create `Core/Modpacks/ModpackNetwork.cs`, `ModrinthCatalog.cs`; Test `ModpackNetworkTests.cs`, `ModrinthCatalogTests.cs`。

**Interfaces:** Produces `ModpackNetwork.CreateClient()` 和有上限的下载/JSON请求方法；目录实现 `IModrinthCatalog`。

- [ ] 写假 HTTP 传输层测试：构造搜索 facets 编码与分页；只保留 Fabric/mrpack/正式包版本，显式显示测试版；排序与主文件选择；无 token 的 User-Agent；私有地址/重定向越界/过大正文/429/410/坏 JSON 行为。
- [ ] 运行指定新测试，确认功能缺失导致失败。
- [ ] 实现 exact-host HTTPS 规则：API `api.modrinth.com`；内容 `cdn.modrinth.com`；Fabric `meta.fabricmc.net`、`maven.fabricmc.net`；游戏官方域名沿用已有规则。重定向逐次校验，最多 5 次，文件下载最多 3 次尝试，429 不自动循环。
- [ ] 运行完整测试，全部通过后提交 `feat: add bounded Modrinth catalog and downloads`。

### Task 3: 下载包内容与应用覆盖层

**Files:** Create `Core/Modpacks/ModpackContentSource.cs`; Test `ModpackContentSourceTests.cs`。

**Interfaces:** `DownloadArchiveAsync(ModpackRelease release, string destination, CancellationToken)` 返回 `PackPreview`；`StageAsync(PackPreview preview, string instanceDirectory, IReadOnlySet<string> optionalPaths, IProgress<InstallProgress>, CancellationToken)` 返回内容文件清单。

- [ ] 写 HTTP 内容流＋真实临时目录测试：两个哈希与长度不符拒绝；可选文件选择；覆盖层最终内容；取消/本地归档变化/下载失败不报告成功；未选择可选文件不会下载。
- [ ] 运行测试观察失败。
- [ ] 实现有界串行下载（首版不增加并发复杂度），失败只保留临时文件；重新验证预览绑定哈希；只展开选定、经过验证的条目，不使用无检查的 ExtractToDirectory。
- [ ] 运行完整测试，全部通过后提交 `feat: stage verified modpack content`。

### Task 4: Fabric 与 PCL 隔离实例安装

**Files:** Create `Core/Modpacks/FabricInstallSource.cs`, `ModpackInstallCoordinator.cs`; Modify `Core/Install/InstallCommitter.cs` 仅必要复用；Test `ModpackInstallCoordinatorTests.cs`, `FabricInstallSourceTests.cs`。

**Interfaces:** `IFabricInstallSource.StageAsync(MrpackPlan plan, string instanceId, string stagingRoot, IProgress<InstallProgress>, CancellationToken)` 返回父原版与子实例暂存清单；协调器实现 `IModpackInstallService`。

- [ ] 写真实文件系统测试：实例中 mods/config 和 PCL 隔离标志正确；原版父版本缺失时完整补齐；匹配已有父版本可复用；不匹配/空间不足/同名冲突/取消/提交竞争时存档与旧配置字节不变；只有完整版本可见。
- [ ] 运行新测试观察失败；检查固定 CmlLib 的真实 Fabric API 与产出的继承 JSON。
- [ ] 使用现有原版暂存源安装清单指定正式版，Fabric 官方元数据安装指定加载器，校验每个库、继承父版本和最终 JSON；只重命名新子实例 id，保留真实继承关系。写入生成的 `PCL/Setup.ini`、最小来源记录；在全部内容已验证后依次提交完整父版本、新实例。
- [ ] 运行完整测试，全部通过后提交 `feat: install Fabric packs as isolated PCL versions`。

### Task 5: 搜索、导入、预检与安装界面

**Files:** Create `Core/Presentation/ModpackViewModel.cs`, `App/ModpackView.xaml`, `.xaml.cs`; Modify `Core/Presentation/MainViewModel.cs`, `App/MainWindow.xaml`, `.xaml.cs`; Test `ModpackViewModelTests.cs`。

**Interfaces:** ViewModel 依赖目录、内容源与安装服务；暴露 SearchAsync、LoadVersionsAsync、PreviewAsync、ImportAsync、InstallAsync、Cancel，目录由主窗口共享。统一安装互斥状态传回主窗口。

- [ ] 写状态测试：预检前不能安装；修改目录/版本清除过期预览；旧请求不能覆盖新选择；错误可重试；无结果；取消；主窗口原版安装与包安装互斥。
- [ ] 运行指定测试观察失败。
- [ ] 实现页签和分步卡片：关键词/MC筛选、结果、版本、导入按钮、预检摘要与实例名、可选项、安装/取消、阶段进度、成功后打开目录。安装中锁定目录与页签，关闭窗口等待取消清理。中文错误不能包含完整私人路径或未经处理的远程响应。
- [ ] 运行完整测试和 WPF Release build；以真实窗口检查 560px 最小宽度、键盘导航和文字可读性；提交 `feat: add modpack search and installation UI`。

### Task 6: 真实验证、打包与文档

**Files:** Modify `README.md`, `PRIVACY.md`, `THIRD-PARTY-NOTICES.md`, `QUICKSTART.txt`, `App/PclVersionBridge.App.csproj`; Create `docs/releases/v0.2.0-preview.1.md`, `docs/modpack-verification.md`。

**Interfaces:** 消费全部已实现入口，输出可运行本地预览包和明确测试记录。

- [ ] 运行完整测试与 Release build；使用真实 API 选定轻量 Fabric 包并记录项目/版本 ID，在线下载和本地导入分别安装到隔离目录。
- [ ] 校验最终模组、配置、父/子元数据与库文件；在隔离的 PCL 2.99.0.0 检查实例识别和隔离设置，不碰真实用户游戏目录。
- [ ] 更新公开能力边界、来源、联网行为、使用步骤和实际验证范围。若网络或 PCL 操作受环境阻止，明确记录且不声称通过。
- [ ] 发布本地 Windows x64 自包含预览 ZIP（附许可证）；对当前整段改动做一次独立代码审查，修复重要问题并重新运行相关测试与完整测试。
- [ ] 记录证据与剩余限制，提交 `docs: document verified modpack preview`。GitHub 发布沿用干净公开历史，未经验证不发布新功能。

## 本机命令约定

工作目录为现有 `.worktrees/mvp`。SDK 使用 `.tools/dotnet/dotnet.exe`；设置 `DOTNET_CLI_HOME` 至本工作区 `.tools/dotnet-home`、关闭遥测，避免受限用户目录导致无输出退出。`dotnet` 在上述任务中指此固定 SDK。原工作区包含早先已授权品牌和发布准备改动，不删除或丢弃；不推送含私人邮箱的旧历史。每次提交只加入本任务指定文件，以 noreply 身份提交。
