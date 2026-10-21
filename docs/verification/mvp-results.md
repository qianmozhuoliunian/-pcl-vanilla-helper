# 本地技术预览验证记录

日期：2026-10-21。环境：Windows 10.0.26200，.NET SDK 10.0.402，PCL 正式版 2.99.0.0。所有写入测试均使用新建的隔离目录；没有向用户当前 PCL 的 `.minecraft` 写入游戏文件。

| 检查项 | 结果 | 证据与边界 |
| --- | --- | --- |
| 自动化测试 | 通过 | `dotnet clean PclVersionBridge.sln -c Release` 后运行 `dotnet test PclVersionBridge.sln -c Release --no-restore`：43/43 通过，0 失败、0 跳过。覆盖路径穿越与链接、哈希冲突、取消、空间不足、已有版本、版本列表及界面状态。 |
| 当前原版完整安装 | 通过 | 官方版本清单选中 `26.3`，安装到新建的临时 `.minecraft`；工具报告 `RESULT=SUCCESS FILES=5225`，版本 JSON 存在。未使用当前真实游戏目录。 |
| 安装后的独立校验 | 通过 | 用独立 Python 脚本从官方版本 JSON、资源索引和适用于 Windows 的库条目建立预期哈希，逐个检查目标文件。最终复验得到 `EXPECTED_KNOWN=5225 GAME_FILES=5225 LOCAL_CONFIG=4 VERIFIED=5225`，`UNKNOWN=0 MISSING_ASSETS=0 MISSING_LIBRARIES=0 MISSING_VERSIONS=0 BAD_HASH=0`；额外 4 个文件是 PCL 本地配置与测试时 CmlLib 写入的版本清单缓存。工具的实际 `CmlInstallSource.StageAsync` 在这个版本上再次运行并通过新增的必需文件检查：`RESTAGE=PASS FILES=5225`。 |
| 已有存档和版本保护 | 通过，模拟目录 | 自动化测试先写入存档、设置和另一版本，再安装假数据；原有字节保持不变。未在用户真实目录测试。 |
| PCL 2.99 识别 | 通过 | 将 PCL 2.99 复制到工作区内的隔离 D 盘目录，让它读取上述临时 `.minecraft`。`PCL/Log1.txt` 记录“从版本 jar 中的 version.json 获取到版本号：26.3”“自动选择 Minecraft 版本：…versions\\26.3\\”及“启动按钮：Minecraft 版本：…versions\\26.3\\”。测试结束后移除了临时目录的 junction。 |
| 窗口渲染 | 本机通过 | WPF 窗口实际 `Show()` 后等待官方版本列表加载，再渲染为本机截图；检查了目录、版本搜索/选择、固定的进度与安装按钮。该测试曾发现进度条只读绑定错误，修复后重跑成功。尚未做鼠标/键盘完整操作和多 DPI 设备测试。 |
| 游戏实际启动 | 未测 | 需要用户自己的正版账户及对应 Java 配置。本次仅证明 PCL 识别并选中，不能据此声称实际进入游戏，也不能证明 PCL 永不补下载其他文件。 |
| Windows 单文件自包含包 | 本机通过 | 使用 `PublishSingleFile=true` 和 `IncludeNativeLibrariesForSelfExtract=true` 构建，输出目录仅有一个 EXE，140,447,370 字节，SHA-256 为 `F65A26B3C35A281C8C091BA385B980EC4EB423F84395DB997507F92790868965`。启动后 8 秒进程仍响应，窗口标题正确；干净 Windows 机器尚未测试。 |
| 3 名目标玩家任务观察 | 未测 | 尚无外部用户参与。目录选择、错误文案和回到 PCL 的顺畅程度仍待观察。 |
| 中国大陆公开发行条件 | 未核清 | 本记录是技术测试，不是面向大陆公开分发下载功能的结论；见[设计文档第 7 节](../superpowers/specs/2026-10-21-pcl-version-bridge-design.md)。 |

## 命令与文件

本机使用工作区内 `.tools/dotnet/dotnet.exe`，等价于已安装到 PATH 的 `dotnet`：

```powershell
dotnet clean PclVersionBridge.sln --configuration Release
dotnet test PclVersionBridge.sln --configuration Release --no-restore
dotnet build src/PclVersionBridge.App/PclVersionBridge.App.csproj --configuration Release --no-restore
dotnet publish src/PclVersionBridge.App/PclVersionBridge.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o .artifacts/win-x64-single
```

真实安装日志、独立校验脚本和 PCL 隔离日志保存在本机 Git 忽略的 `.superpowers/` 与 `.artifacts/` 中，不进入仓库；它们包含本机临时路径。复核公开证据时应重新运行测试，不应把这些本地绝对路径写入 Issue。

## 已知限制

- 当前版本只安装原版正式版，不管理 Java、账号、启动、模组或用户存档；所谓“环境管理”目前只有设计路线，没有实现。
- 尚未显示预计下载量；目标磁盘空间检查发生在临时下载完成、提交前，因此磁盘空间很少时仍可能先在临时下载阶段失败。这与[首版设计](../superpowers/specs/2026-10-21-pcl-version-bridge-design.md)中的提前展示/检查目标存在差距。
- 安装取消的单元测试通过；窗口关闭时的等待流程已实现，但未做独立 UI 自动化测试。强制结束进程或断电仍可能留下 `.pcl-version-bridge-stage-*` 临时目录，后续应增加安全的恢复提示。
- 路径链接会在提交前和写入前再次检查，但无法保证对另一个进程在检查与创建目录之间进行的恶意并发替换完全免疫。用户应避免同时用其他程序修改同一 `.minecraft`。
- 下载可靠性依赖官方服务和网络状态。前期探测遇到一次 TLS 中断，重试后成功；当前适配器对 HTTP 中断最多重试 3 次。
- 只允许已核查的官方主机；若官方未来改变下载主机或清单结构，版本可能被安全检查拒绝，需要更新并重新验证。
