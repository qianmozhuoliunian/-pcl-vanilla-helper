# 首次发布步骤

实际仓库：[qianmozhuoliunian/-pcl-vanilla-helper](https://github.com/qianmozhuoliunian/-pcl-vanilla-helper)。软件名：原版直达 · VanillaReach。仓库名开头的连字符保留为所有者实际创建的名称。

仓库描述可填写：**原版装好，PCL 照用。面向 PCL 玩家的非官方 Minecraft Java 原版图形安装助手。**

## 1. 创建空仓库

所有者已创建上述 Public 空仓库。以下流程保留供维护者复核；新建其他仓库时不要勾选自动添加 README、许可证或 `.gitignore`，这些文件已经准备好。

如果需要 AI 协助上传，把新仓库链接交给助手即可。不要在聊天、Issue 或源码中粘贴访问令牌、密码或验证码。

## 2. 上传审阅后的源码

使用本地准备的 `pcl-vanilla-helper-source.zip`，解压后上传其中 `pcl-vanilla-helper/` 文件夹**里面的内容**，让 `README.md` 与 `LICENSE` 位于 GitHub 仓库根目录。不要只把源码 ZIP 当作仓库唯一文件。

源码包排除了旧 `.git` 历史、构建缓存和游戏文件。原开发仓库历史中有非匿名提交邮箱，因此首次上传使用这份干净源码快照；原开发仓库不作改写。

通过 Git 提交时，先确认署名为愿意公开的名字和 GitHub 提供的 noreply 邮箱。通过 GitHub 连接器提交时也应核对返回的提交署名，不要假定连接器会自动隐藏邮箱。

上传后检查 README 能正常显示，MIT 能被识别，以及 `.github/workflows/build.yml` 已包含在仓库中。首次 GitHub Actions 构建仍需实际运行后才能报告通过。

## 3. 发布 Windows 技术预览

仓库中的 `.github/workflows/release.yml` 用于预览版发布：先确认主分支构建通过，再使用一个尚未发布的新预览标签，并准备对应发布说明。工作流会重新测试、构建自包含程序、核对程序与随包许可清单、打包两份 ZIP、生成 SHA-256 并创建 Pre-release。只使用 GitHub 自动提供的仓库令牌，不需要额外粘贴访问令牌。`v0.1.0-preview.1` 已经发布，不能重复创建或覆盖。

首版发布固定 .NET SDK `10.0.402` 与运行库 `10.0.13`，对应已核查的依赖和随包许可文本。以后升级运行库时一并更新依赖清单与许可/notices。GitHub 构建还会写入预览版本号，附件校验值以该次 GitHub 构建生成的文件为准；本机检查记录中的 EXE 哈希只标识当时的本地文件。

如工作流不可用，可按下面的等价步骤手动发布。

源码上传并完成远端构建核验后，在 Releases 新建：

- Tag：`v0.1.0-preview.1`，指向已核验的源码提交。
- Title：`原版直达 v0.1.0-preview.1 · 原版装好，PCL 照用`。
- 正文：使用 [首版发布说明](releases/v0.1.0-preview.1.md)。
- 附件：`VanillaReach-win-x64-preview.zip`、`SHA256SUMS.txt`，可一并提供 `pcl-vanilla-helper-source.zip`。
- 勾选 **Pre-release**，保留实际进入游戏、干净系统和多设备运行尚未验证的说明。

程序包必须包含 `LICENSE`、`THIRD-PARTY-NOTICES.md`、`PRIVACY.md`、快速说明、依赖清单及 `licenses/`。不要单独上传 EXE，也不要附带游戏文件、PCL 程序、私人日志或账号信息。

发布后实际打开 Release 页面，核对附件名称和下载文件的 SHA-256，再将 README 的下载地址指向已核验的版本。已有发布标签应保持不动；下一版使用新的预览编号和对应发布说明。

## 已准备与尚未完成

首版已完成源码上传、GitHub Actions 构建与 Pre-release 发布，并实际下载附件核验了校验值、文件数量和许可文本。检查结果见 [发布检查](release-review.md)，已发布版本见 [v0.1.0-preview.1](https://github.com/qianmozhuoliunian/-pcl-vanilla-helper/releases/tag/v0.1.0-preview.1)。

尚未完成实际进入游戏、普通玩家干净 Windows 环境与多设备体验验证。后续版本仍应重新运行构建并验证实际下载附件，不直接沿用首版的检查结论。
