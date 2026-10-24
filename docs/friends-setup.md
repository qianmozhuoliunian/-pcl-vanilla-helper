# 好友功能：本机测试与服务配置

好友系统是独立、可选的原版直达功能。当前没有已部署的公共好友服务器。普通启动仍可免登录使用原版安装和整合包功能；不要把本机测试套件描述成公网多人服务。

## 本机测试套件

完整解压 `VanillaReach-Friends-local-preview.zip`，保留 `client/`、`server/`、许可和说明。

1. 双击 `Start-A.cmd` 打开第一位测试玩家的窗口，进入“好友”，注册一个仅供测试的邮箱和昵称。
2. 双击 `Test-mail.cmd`，在“本机测试收件箱”选择邮件并复制代码，回到好友页确认邮箱并登录。邮件只保存到本机，不会发往真实邮箱。
3. 双击 `Start-B.cmd`，为第二位测试玩家注册并完成同样步骤。两个窗口使用独立的本机凭据与备注目录。
4. 在A复制好友码，在B输入并申请；A同意。此时双方刷新后才出现好友。
5. 在“整合包”选择一个支持的 Fabric 包及其具体发布版本，点“发送给好友…”；选择朋友并核对来源版本。接收方打开卡片、查看来源，然后自主“查看并检查”。目录确认和最终安装仍由接收方操作。
6. 删除/拉黑后，对应旧申请和待处理卡片失效。退出会清除本机身份缓存；修改密码、退出所有设备或删除账号会撤销服务端会话。
7. 不再测试时双击 `Stop-server.cmd`，停止这个套件启动的后台服务。关闭客户端不会自动停止服务，方便A/B交替操作。

本机测试服务仅监听 `127.0.0.1:59132`。`.friends-local/` 保存测试数据库、身份保护密钥、测试邮件和两个客户端 profile；不要公开上传它。停止服务后可自行删除整个本机测试目录重新开始，不能以此代替公共服务账号删除流程。邀请码/验证代码与密码不要出现在截图中。

套件入口调用应用自己的显式 `--friends-preview` 模式，不修改系统执行策略，不要求管理员权限。启动时同时核对这份套件记录的进程、启动时间、程序路径和健康响应进程；另一份套件占用端口时会失败，不混用它的数据库。停止入口只停止所属进程，找不到时如实提示。其他程序占用端口、未完整解压或文件被隔离时会提示启动失败；不要为此关闭系统保护。测试收件箱每次打开重新读取最近的邮件。

## 从源码运行服务

需要 .NET10 SDK。项目：`src/VanillaReach.Friends.Server/VanillaReach.Friends.Server.csproj`。客户端仍为原有 WPF 项目；服务不随普通窗口自动启动。

显式开发环境配置：

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:59132'
$env:Friends__PublicOrigin='http://127.0.0.1:59132/'
$env:Friends__DataDirectory=Join-Path (Get-Location) '.artifacts/friends-local/data'
$env:Friends__DevelopmentInbox=Join-Path (Get-Location) '.artifacts/friends-local/inbox'
dotnet run --project src/VanillaReach.Friends.Server/VanillaReach.Friends.Server.csproj
```

在另一个终端启动客户端时设置 `VANILLAREACH_FRIENDS_SERVICE_URL=http://127.0.0.1:59132/`、`VANILLAREACH_FRIENDS_DEVELOPMENT=1`；用 `VANILLAREACH_PROFILE_DIRECTORY` 指定独立测试 profile。只用于明确的本机测试，公开地址不能用 HTTP。

## 公共服务部署前

由维护者提供自己的 HTTPS 服务与邮箱投递通道。程序旁 `friends-service.json` 示例（示例域名不能直接使用）：

```json
{"serviceUrl":"https://friends.example.org/","allowLoopbackDevelopment":false}
```

服务设置：`Friends:PublicOrigin`、持久化的 `Friends:DataDirectory`，以及 `Friends:Smtp:Host/Port/User/Password/From`。敏感值通过部署环境注入，不能提交到仓库；生产环境不得使用开发收件箱。跨源重定向被拒绝，游戏来源请求与好友认证请求各用独立客户端。

Identity 密钥和 SQLite 数据库都需要持久保存及受限访问。当前面向单实例部署；多副本不能依赖进程内互斥锁，应先重新设计数据库并发和部署策略。更新数据库结构前先做可恢复备份，不把预览数据库当作稳定迁移承诺。

公开运行前还需落实真实邮件验证/找回、HTTPS证书、备份恢复、账号删除在恢复后的重放、到期数据清理、日志保留、维护联系方式和实际地区可达性。两台不同电脑与实际公网服务的验证尚未在本机测试中完成；不以“本机两客户端通过”替代这些测试。当前卡片范围为 Modrinth Fabric＋MC 原版正式版，不提供聊天、游戏在线状态或直接联机。

## 验证命令与记录

```powershell
dotnet test PclVersionBridge.sln
dotnet run --project tests/PclVersionBridge.UiChecks/PclVersionBridge.UiChecks.csproj
dotnet run --project tests/VanillaReach.Friends.IntegrationChecks/VanillaReach.Friends.IntegrationChecks.csproj
```

维护者可以运行 `tools/package-friends-preview.ps1` 生成 Windows x64 自包含测试包；它只打包明确列出的程序、说明和许可目录，不包含 `.friends-local`。

具体结果见 [好友验证记录](verification/friends-results.md)。测试账号、邮件和图片夹具只存在隔离环境，不预置到用户好友列表。
