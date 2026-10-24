# 好友功能验证记录

日期：2026-10-24。范围：Windows 本机技术预览；没有已部署公共好友服务。

## 已运行

| 检查 | 实际结果 |
| --- | --- |
| Release Core 测试 | 134/134 通过，0 跳过 |
| Release 好友服务 HTTP/边界测试 | 19/19 通过，0 跳过 |
| Windows 原生组件检查 | 服务地址限制、DPAPI 往返与损坏/超大文件拒绝、账号备注隔离、最小/默认内容尺寸渲染通过 |
| 退出登录回归 | 先复现 3 项失败，再验证普通/全部退出使用旧身份刷新撤销；等待期间新账号不被旧结果覆盖，3/3 通过 |
| 测试套件所属服务回归 | 先复现误用外部健康服务，再验证缺少记录、旧启动时间、其他程序路径和不匹配响应均拒绝；停止操作不声称停止外部进程 |
| 两账号集成检查（来源夹具） | 注册/验证/登录、双向同意、精确卡片、原生列表同步、拉黑使卡片失效、退出撤销，全部通过 |
| 真实来源集成检查 | 生产 Modrinth 校验器与实际 `.mrpack` 下载/散列/预检通过；未执行游戏安装或启动 |
| 发布包解压启动 | Windows x64 自包含客户端与服务端发布成功；从 ZIP 解压后 A/B 两个窗口实际打开，共用经进程身份核对的服务；压缩包未含测试数据库、收件箱、登录凭据或调试符号 |

完整命令（Windows、.NET 10 SDK）：

```powershell
dotnet test PclVersionBridge.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/PclVersionBridge.UiChecks -c Release
dotnet run --project tests/VanillaReach.Friends.IntegrationChecks -c Release
dotnet run --project tests/VanillaReach.Friends.IntegrationChecks -c Release -- --live-source
```

两客户端检查使用真实本机 Kestrel、数据库、生产 FriendApiClient 和原生好友页；来源夹具与真实网络分开记录。注册的两位“本机测试玩家”只存在隔离测试目录，不预置给用户。实际来源检查使用 Modrinth 项目 `1KVo5zza`、版本 `LpdvzsNf`（Fabric、MC 1.20.1）以及来源提供的 SHA-512，下载原 `.mrpack` 并预检；不包含游戏安装和进入游戏结论。

## 审查与修正

独立审查提出两个 P2 问题：久置后退出没有使用刷新凭据完成远端撤销，以及第二份本机套件可能连接第一份套件服务。两项均已先复现再修正并通过针对性检查。未重复开展没有新增证据的整轮审查。

好友卡片仅包含指定项目、版本和归档散列；接收方重新核对服务端权限与 Modrinth 来源，再由本人确认目录和安装。未配置好友服务时，原有安装不要求账号。原有游戏文件、存档和配置不是好友数据。

## 限制与后续门槛

- 当前包只用于同一台电脑的两个客户端。公网部署、真实 SMTP 投递、两台实体电脑、实际游戏启动均未验证。
- 服务按单实例 SQLite 设计；初始预览使用 EnsureCreated，尚不承诺数据库升级迁移。公开部署前要落实 HTTPS、备份恢复、删除重放、日志保留和运维联系渠道。
- 原生渲染检查不能替代所有交互和辅助功能测试。已知小问题：前台列表自动刷新时，行按钮的键盘焦点可能丢失；对话框中的输入不重建。
- 构建期间关闭 NuGet 在线审计，不把构建成功描述成依赖漏洞扫描通过。
- 本轮仅提供本地构建产物和代码检查点，没有推送仓库或发布新的 GitHub Release。
