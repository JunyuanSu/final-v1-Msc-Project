# 当前版本备份与 GitHub 上传说明

整理日期：2026-10-21。本目录是磁盘上已保存文件的独立源项目快照。原项目未修改；Unity 内未保存的修改不包含在本次备份中。尚未上传 GitHub。

## 包含内容

- Assets：全部场景、脚本、预制体、材质、XR 配置、数据配置及 .meta。
- Packages：manifest.json、packages-lock.json，保留当前依赖声明与锁定版本。
- ProjectSettings：完整项目配置；构建列表已包含 PortOfDover 主场景。
- Docs 和原 README.md：保留现有开发说明及历史验证记录。
- .gitignore、.gitattributes、本文和 FILE-MANIFEST.csv：上传规则、迁移说明与 SHA-256 校验清单。

未复制 Library、Temp、Logs、UserSettings、IDE 自动生成文件、.agents 本地代理技能。Unity-Skills 插件本身仍在 Packages 中声明，可以重新下载；本地代理技能目录不是 Unity 运行依赖。缓存会由 Unity 重新生成。

## 当前版本

- Unity：2022.3.55f1。
- Cesium for Unity：1.24.0。
- XR Interaction Toolkit：2.5.4。
- OpenXR：1.14.0。
- Unity-Skills：Git 依赖固定到 v2.9.1；恢复依赖时需安装 Git 并能访问 GitHub。
- 主场景：Assets/Scenes/PortOfDover.unity。

## 恢复与 Mac 迁移

通过 Unity Hub 使用相同编辑器版本，添加本目录（包含 Assets、Packages、ProjectSettings 的目录）。联网等待依赖下载与资源导入，配置自己的 Cesium ion 令牌，再打开主场景。地图依赖 Cesium 在线服务，本备份不是离线地图包。

副本清除了 Assets/CesiumSettings/Resources/CesiumIonServers/ion.cesium.com.asset 中默认令牌及 ID。配置令牌后它们可能重新写入资产；每次提交前检查差异，不要提交个人令牌。

当前代码存在两处 Windows 绝对路径，已原样备份，迁移前需调整：

- Assets/Scripts/IdentificationTrialController.cs 的 evaluationDirectory：指向原电脑项目外的 Data Collection 文件夹；同时核对场景中可能保存的 Inspector 值。
- Assets/Scripts/EvaluationDataLog.cs 的 DirectoryPath：指向原电脑项目外的 SJY project 文件夹。

因此，项目外已有实验输出数据不包含在本次源项目备份中。Mac 上的数据记录功能不能直接依赖这些路径，需要改为 Mac 可写目录或跨平台路径。本次没有修改程序行为，也没有进行 Mac、XR 设备或 Unity 运行验证。

## 上传 GitHub

先在 GitHub 创建空仓库，不预先添加 README、License 或 .gitignore。安装 Git 并完成 GitHub 身份认证。终端进入本副本目录，将下面的 OWNER/REPOSITORY 替换为实际仓库地址：

```sh
git init -b main
git add .
git diff --cached --stat
git diff --cached
git commit -m "Backup Port of Dover 2026-10-21"
git remote add origin https://github.com/OWNER/REPOSITORY.git
git push -u origin main
```

把此目录内的内容作为仓库根目录，包含所有 .meta 与两个隐藏配置文件。先检查差异再提交。不要在原项目外层误执行这些命令，也不要把 Library 或 Temp 加入仓库。

这是一份静态快照，不会自动同步原项目后续改动。上传后建议在仓库工作目录继续开发；每次保存、检查、commit 并 push，GitHub 才有最新备份。

## 本次检查

逐文件 SHA-256 对比：Assets、Packages、ProjectSettings、Docs 中仅上述 Cesium 配置因脱敏而不同，其余与源文件一致。未发现 Assets 缺少配套 .meta。已检查的 Cesium 令牌、JWT、GitHub 令牌及私钥格式未发现残留；不等同于全面安全审计。文件清单不含自身，后续修改后不再代表新版本。旧文档的验收结果属于历史记录，不代表此次重新运行验证。
