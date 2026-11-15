# 发行维护指南

本指南适用于 Windows 10/11 x64 的安装版和便携版。发行脚本只生成本地产物，不创建远程标签、不上传 GitHub Release，也不修改 CI。

## 环境与版本

- Windows 10/11 x64。
- .NET 10 SDK，遵循 `global.json` 中的基线和滚动规则。
- Inno Setup **6.7.4**，使用其 `ISCC.exe` 编译 `packaging/windows` 下的安装脚本。
- Git；使用下面的 GitHub 命令时，还需要已登录且有仓库写权限的 GitHub CLI `gh`。

根目录 `version.props` 的 `Version` 是统一版本来源。发布新版本时先修改该值，再同步 `CHANGELOG.md`、README 下载链接及对应的 `docs/releases/vX.Y.Z.md`，避免在项目或安装脚本中另设版本号。应用版本、安装版本和产物文件名应保持一致。

以下命令均在仓库根目录执行，以 v1.2.0 为例。`ISCC.exe` 路径按本机实际安装位置调整。

## 先验证，再生成发行文件

1. 检查工作区及差异，只提交本次发行相关修改，保留用户已有改动。发行源提交应已完成必要验证，且工作区干净。

   ```powershell
   git status --short
   git diff
   git diff --cached
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
   ```

2. 在可交互、无遮挡的桌面执行真实输入验收。执行期间不要操作鼠标键盘或切换窗口。该模式使用专用测试窗口和独立临时配置。

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1 -Desktop
   ```

   记录实际执行的命令、退出码、测试范围及未覆盖项。旧版验证记录不能代替新发行包的验证，也不能将未执行的桌面测试记为通过。

3. 需要单独检查自包含应用时，运行发布入口；产物位于 `artifacts/windows-x64/`。此命令只生成应用，不生成正式 ZIP 或安装包。

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1
   ```

4. 将最终源码、版本和文档变更提交后，从该干净提交生成完整发行包。

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
   ```

   默认输出到 `artifacts/releases/v1.2.0/`，包含以下四个待上传文件：

   | 文件 | 内容 |
   | --- | --- |
   | `YuyuMouseClick-v1.2.0-win-x64-setup.exe` | 当前用户安装包 |
   | `YuyuMouseClick-v1.2.0-win-x64-portable.zip` | 自包含便携应用与文档 |
   | `SHA256SUMS.txt` | 发行文件的 SHA-256 校验值 |
   | `release-manifest.json` | 版本、源码提交及产物信息 |

   同版本输出目录存在时，脚本拒绝覆盖。需要重新生成候选包时，用 `-OutputRoot` 指定其他输出根目录，不覆盖已有候选产物，例如：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' -OutputRoot '.\artifacts\release-candidate-2'
   ```

## 发行包验收

- 检查 EXE 文件版本、安装向导版本、产物文件名和清单版本都为目标版本；清单中的源码提交应对应实际构建源码。
- 完整解压便携 ZIP，确认包含 `钰宇鼠标连点器.exe`、`portable.flag`、`使用说明.md`、`更新日志.md`、`图标来源.md` 和 `图标许可.txt`。
- 从便携目录启动，修改设置、关闭并重新启动，确认读写的是旁边的 `data/settings.json`，且不会改写安装版配置。
- 运行安装包，检查中文向导、默认当前用户目录、开始菜单、可选桌面快捷方式和 Windows 卸载入口。
- 检查安装版使用 `%LOCALAPPDATA%\HQY.MouseClick\settings.json`，升级或卸载不会移除用户配置。涉及既有用户数据时先做备份，不覆盖未备份的数据。
- 核对安装版和便携版都能启动，并实际检查快捷键、点选和基本点击行为。仅成功编译不代表这些操作已通过。
- 使用 `Get-FileHash -Algorithm SHA256 -LiteralPath '<产物路径>'` 复核清单条目。包内文档或二进制发生变化后必须重新生成校验文件及发行清单，不手工修改既有校验值来掩盖变动。

将实际验收结论记录到对应版本文档或验证记录。如果验收触发源码修复，重新提交、构建并复验；最终发行标签必须指向清单中的源码提交。

## 创建标签及上传

发布前检查远端现有标签和 Release。不要覆盖既有版本，也不要强制移动已经发布的标签。以下远程写入操作须在获得当前任务的发布授权后执行。

```powershell
git status --short
git rev-parse HEAD
git ls-remote --tags origin 'refs/tags/v1.2.0*'
gh release view v1.2.0 --repo HQY-DEBUG/mouse_click
```

如果标签或 Release 已存在，先核对其提交和文件，不继续创建同名发行。若 GitHub CLI 返回网络或权限错误，应先解决错误，不能据此认定版本不存在。

确认标签尚不存在、工作区干净，且 `release-manifest.json` 的源码提交与待标记的提交一致后，创建并推送附注标签，再创建带四个附件的草稿 Release：

```powershell
git tag -a v1.2.0 -m '发布钰宇鼠标连点器 v1.2.0'
git push origin v1.2.0
gh release create v1.2.0 --repo HQY-DEBUG/mouse_click --verify-tag --draft --title '钰宇鼠标连点器 v1.2.0' --notes-file docs/releases/v1.2.0.md `
  artifacts/releases/v1.2.0/YuyuMouseClick-v1.2.0-win-x64-setup.exe `
  artifacts/releases/v1.2.0/YuyuMouseClick-v1.2.0-win-x64-portable.zip `
  artifacts/releases/v1.2.0/SHA256SUMS.txt `
  artifacts/releases/v1.2.0/release-manifest.json
```

若使用了其他 `-OutputRoot`，上面的附件路径也应同步调整。上传后检查标题、标签目标、发布说明及四个附件的名称、大小；下载草稿中的产物到另一目录，重新计算 SHA-256，并与上传前的清单比较。供包内使用或上传到 GitHub Release 的文档使用该标签下的仓库文件链接，更新版本时一并更新链接中的标签。

完成复核并确认当前任务授权覆盖正式发布后，将草稿公开：

```powershell
gh release edit v1.2.0 --repo HQY-DEBUG/mouse_click --draft=false
```

最后打开 [v1.2.0 Release](https://github.com/HQY-DEBUG/mouse_click/releases/tag/v1.2.0)，确认安装版、便携版、校验文件和发行清单均可下载，README 下载链接与实际附件一致。只报告实际完成的构建、验证与发布状态。
