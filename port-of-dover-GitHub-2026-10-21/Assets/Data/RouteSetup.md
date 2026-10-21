# EasternDocksRoute

本路线为开发阶段模拟路线，不代表 Port of Dover 官方车道、实测 GPS 轨迹或最终实验路线。

- Hierarchy：`PortOfDover/Traffic/Routes/EasternDocksRoute`。
- 15 个空 Waypoint，直接子对象按 sibling index 顺序 WP_00 → WP_14。
- Start：WP_00，Eastern Docks 入口方向附近。
- WP_00 → WP_08：入口弯道与内部通道。
- Queue Segment：WP_09 → WP_12，候车区模拟区段；只定义位置，没有停车或排队逻辑。
- End：WP_14，泊位方向的陆侧铺装区域，未延伸到渡轮或海面。
- Traffic、Routes、EasternDocksRoute 的 Scale 均为 (1,1,1)，路线根 Position/Rotation 为零。

## 布点与高度

在当前加载的 Cesium 卫星影像俯视图中人工选择各点，依据可见入口弯道、内部铺装通道、候车区和终点附近设施分布。使用一次性 Editor 工具将这些人工选择的画面位置通过 Raycast 转为表面位置，加 0.1 m 视觉偏移。不是从包围框、中心点或随机插值生成。
临时工具已删除。当前没有动态地形采样。

坐标是当前固定 Cesium 原点下的 Unity 局部坐标（米）；Y 是开发阶段局部高度，不是 WGS84 海拔或正式道路标高。Cesium LOD 会改变局部表面。候车区影像包含停放车辆，不代表这条模拟线路在现实运营中可通行。高度和直线段尚不是可直接用于严格贴地驾驶的几何。

## Local Position

| Waypoint | Local Position（米） |
|---|---|
| WP_00 | x: 948.42725, y: -448.9397, z: -353.23767 |
| WP_01 | x: 1006.8662, y: -458.13937, z: -379.87695 |
| WP_02 | x: 1086.5554, y: -444.15274, z: -385.20474 |
| WP_03 | x: 1168.9008, y: -450.71695, z: -358.56573 |
| WP_04 | x: 1243.2783, y: -446.16327, z: -318.60645 |
| WP_05 | x: 1293.7474, y: -450.43137, z: -270.65576 |
| WP_06 | x: 1322.9667, y: -450.95792, z: -214.71318 |
| WP_07 | x: 1370.7804, y: -450.7193, z: -169.42632 |
| WP_08 | x: 1429.2192, y: -450.2628, z: -142.78696 |
| WP_09 | x: 1474.3766, y: -450.86966, z: -196.06572 |
| WP_10 | x: 1434.532, y: -450.2663, z: -241.35239 |
| WP_11 | x: 1389.3748, y: -450.61597, z: -283.97522 |
| WP_12 | x: 1344.2174, y: -450.91873, z: -321.2704 |
| WP_13 | x: 1357.4989, y: -450.91, z: -361.2295 |
| WP_14 | x: 1397.3435, y: -450.62875, z: -398.52454 |

## 可视化脚本

新增 `Assets/Scripts/RouteVisualizer.cs`，仅挂载在 `Traffic/Routes/EasternDocksRoute`。
自动读取直接子对象，按 sibling index 画 Gizmos 小球与折线，并使用 Editor Handles 加粗折线与显示名称。线条为便于检查使用穿透显示，不可用“线可见”单独证明没有遮挡；已结合卫星影像检查。没有将坐标写入此脚本。
仅 Scene View 显示，没有 Update、Movement、Rigidbody、AI、运行时修改或第三方依赖。

## 验证：2026-09-11

- 场景导入、脚本编译与 Play Mode 检查完成。
- Scene View 放大检查后，路线形成入口 → 内部通道 → 候车区 → 泊位方向的连续折线；未发现明显跨海或穿建筑，未延伸进入码头栈桥。
- Play Mode 中路线折线与 15 个节点 Hierarchy 保持可见，没有新增路线节点或车辆运动。退出后保存。
- Console：0 Warning / 0 Error。
- 与修改前场景逐个序列化对象比较，唯一改变的既有对象是 Routes Transform 的子对象列表；Main Camera、Cesium、TestTruck 及其 Prefab 引用和实例参数保持不变。
- 最终保留一个新 C#：RouteVisualizer.cs；临时 Editor 工具已删除。
- 当前编辑器停留在路线俯视检查图，Hierarchy 展开全部节点。

本轮只完成路线定义。车辆原有总览可辨性、LOD 底面间隙限制保持原样。下一轮可单独开发基础 Movement，届时需另行处理路线高度适配与车辆朝向；当前没有 Movement。
