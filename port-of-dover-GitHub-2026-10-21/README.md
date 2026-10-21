# Port of Dover

Unity 港口场景项目。Unity Editor 版本：**2022.3.55f1**。Cesium for Unity：**1.24.0**。

## 在 Windows 上打开

1. 使用 Git 克隆仓库，或下载 ZIP 并解压。
2. 在 Unity Hub 安装 Unity 2022.3.55f1，并添加本目录为项目。
3. 保持联网，等待依赖下载和资源导入。
4. 在 Cesium 中配置你自己的有效 Cesium ion 访问令牌。上传副本已移除原令牌。
5. 打开 `Assets/Scenes/PortOfDover.unity`，点击 Play。

地图地形、影像及建筑由 Cesium 在线加载；本仓库不包含完整离线地图。

## Windows 构建

在 File → Build Settings 中选择 Windows / x86_64。打开 PortOfDover 场景并点击 Add Open Scenes，再构建。分发整个构建输出目录。

## 跨设备协作

保留所有 Assets 内的 .meta 文件。Library、Temp 等缓存不提交。开始工作前拉取，保存并关闭 Unity 后提交并推送。

Cesium 访问令牌可能保存在 Assets/CesiumSettings/Resources/CesiumIonServers/ion.cesium.com.asset 中。每次提交前检查差异，勿将个人令牌提交到仓库。
