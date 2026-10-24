param(
    [string]$Dotnet = 'dotnet',
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$')][string]$OutputName = 'vanillareach-friends-kit',
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,95}\.zip$')][string]$ArchiveName = 'VanillaReach-Friends-local-preview.zip'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$kit = Join-Path (Join-Path $projectRoot '.artifacts') $OutputName
$archive = Join-Path (Join-Path $projectRoot '.artifacts') $ArchiveName
New-Item -ItemType Directory -Path $kit -Force | Out-Null

foreach ($part in @(
    @{ Project='src/PclVersionBridge.App/PclVersionBridge.App.csproj'; Folder='client' },
    @{ Project='src/VanillaReach.Friends.Server/VanillaReach.Friends.Server.csproj'; Folder='server' }
)) {
    $destination = Join-Path $kit $part.Folder
    & $Dotnet publish (Join-Path $projectRoot $part.Project) -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($part.Folder)" }
    foreach ($notice in @('LICENSE','PRIVACY.md','THIRD-PARTY-NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination (Join-Path $destination $notice) -Force
    }
    $licenses = Join-Path $destination 'licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $projectRoot 'licenses') -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenses $_.Name) -Force
    }
}
$docs = Join-Path $kit 'docs'
New-Item -ItemType Directory -Path (Join-Path $docs 'verification') -Force | Out-Null
foreach ($document in @('friends-setup.md','friends-api.md','dependencies.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "docs/$document") -Destination (Join-Path $docs $document) -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/verification/friends-results.md') -Destination (Join-Path $docs 'verification/friends-results.md') -Force
if (Test-Path -LiteralPath (Join-Path $projectRoot 'docs/verification/desktop-polish-results.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/verification/desktop-polish-results.md') -Destination (Join-Path $docs 'verification/desktop-polish-results.md') -Force
}
foreach ($entry in @(
    @{ Name='Start-app.cmd'; Mode='' },
    @{ Name='Start-A.cmd'; Mode='A' }, @{ Name='Start-B.cmd'; Mode='B' },
    @{ Name='Stop-server.cmd'; Mode='stop' }, @{ Name='Test-mail.cmd'; Mode='mail' }
)) {
    $arguments = if ($entry.Mode) { " --friends-preview $($entry.Mode)" } else { '' }
    $body = "@echo off`r`nstart `"`" `"%~dp0client\VanillaReach.exe`"$arguments`r`n"
    [IO.File]::WriteAllText((Join-Path $kit $entry.Name),$body,[Text.Encoding]::ASCII)
}
$instructions = @'
原版直达 · 桌面技术预览（含好友本机测试）

普通使用：完整解压，双击 Start-app.cmd。侧栏可切换原版安装、整合包和好友。安装功能不要求账号。
好友本机测试按以下步骤；没有部署公共好友服务器。

1. 完整解压；双击 Start-A.cmd，进入“好友”并启用。
2. 注册测试邮箱/昵称。双击 Test-mail.cmd 复制本机验证代码，回好友页确认邮箱后登录。密码至少12位，包含大小写字母、数字和符号。
3. 双击 Start-B.cmd 打开第二个独立测试身份，按相同步骤注册。
4. 复制好友码互相申请，对方同意后才成为好友。
5. 在整合包页选择具体 Fabric 包版本，点“发送给好友…”。接收方查看并检查，再自主确认安装；完成后仍回 PCL 选版本。
6. 结束测试双击 Stop-server.cmd 停止这份套件启动的服务。退出登录在本预览版会撤销该账号的全部会话。

这是本机两客户端环境；邮件没有对外发送，也没有公共好友服务器。
原有安装免登录。仅支持 Modrinth Fabric 整合包与 Minecraft Java 原版正式版。
没有聊天、在线状态或一键联机；没有验证实际进入游戏。
.friends-local 内含测试数据库、密钥、邮件和本机凭据，不要上传或分享。
另一份套件占用59132端口时，请在原套件停止服务后再打开此包。

尚未验证：公网部署、真实邮件、两台实体电脑、游戏启动和干净系统。
已知小问题：前台自动刷新列表时，行按钮的键盘焦点可能丢失。
完整使用与验证说明见 docs/。保留许可及第三方声明一起分发。
'@
[IO.File]::WriteAllText((Join-Path $kit 'READ-ME-FIRST.txt'),$instructions,[Text.UTF8Encoding]::new($true))
# Explicit allowlist: never archive the sibling .friends-local database/inbox/profile.
$entries = @('client','server','docs','Start-app.cmd','Start-A.cmd','Start-B.cmd','Stop-server.cmd','Test-mail.cmd','READ-ME-FIRST.txt') | ForEach-Object { Join-Path $kit $_ }
Compress-Archive -LiteralPath $entries -DestinationPath $archive -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($archive+'.sha256'),"$hash  $([IO.Path]::GetFileName($archive))`n",[Text.Encoding]::ASCII)
Write-Output "Created $archive"
Write-Output "SHA256 $hash"
