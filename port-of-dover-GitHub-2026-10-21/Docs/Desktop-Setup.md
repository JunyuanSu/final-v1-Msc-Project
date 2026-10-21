# Desktop 场景准备

开发场景：Assets/Scenes/PortOfDover.unity。双击打开后，在 Game View 选择 16:9 并进入 Play。
原始 SampleScene.unity 保留。Build Settings 未改变。没有新增 C#，无需挂载脚本。

## Hierarchy

```text
PortOfDover
├── Environment
│   └── CesiumGeoreference
│       ├── Cesium World Terrain
│       └── Cesium OSM Buildings
├── Traffic
│   ├── Vehicles
│   ├── Routes
│   └── TrafficStates
├── Visualization
│   ├── Queue
│   ├── Congestion
│   └── Labels
├── Experiment
│   ├── TaskManager
│   ├── Timer
│   └── DataLogger
├── UI
├── XR
├── Main Camera
└── Directional Light
```

新增分组都是空对象；TaskManager、Timer、DataLogger 等名称仅预留位置，尚无功能。
新增父对象使用 identity transform；CesiumGeoreference 原点保持 latitude 51.129、longitude 1.313、height 500，scale 1。
Cesium 组件、Token、Asset ID 未修改。未来不要缩放或移动顶层分组来调整观察视角。

Assets 新增 Scripts、Prefabs、Materials、Models、Data、UI、Traffic、Experiment、XR；保留 Scenes 和 CesiumSettings。

## 实验区域（原型候选）

选用 Eastern Docks 的入口—车辆候车区—渡轮码头走廊。暂不覆盖整个 Dover，也不涉及 Western Docks。
官方入口依据：https://www.portofdover.com/ferry/how-to-get-here/
码头设施参考：https://www.portofdover.com/ferry/facilities/

以下数值为开发规划值，并非官方设施边界或实测车道坐标：

- WGS84 边界：纬度 51.121–51.132，经度 1.321–1.345，约 1.7 × 1.2 km。
- 观察中心：纬度 51.1265，经度 1.333，椭球高 10 m。
- 高度是 WGS84 椭球高，不是离地高度；真实道路标高需在 Cesium 加载后核对。
- 边界仅用于规划，不会裁剪 Cesium 或限制瓦片加载。
- 伦理材料与既有实验设计未提供到工程，本区域尚未与其逐项核对。
- 下一模块开始前，在卫星影像中明确一条入口到候车区、再至码头的模拟路线；不要直接把包围框或中心点当作可行驶车道。

## 固定 Desktop Camera

Main Camera 位于 PortOfDover 下，无动态控制脚本。

| 属性 | 值 |
|---|---|
| Unity Position | (1400.134635, 509.840573, -1127.936985) |
| Rotation Euler | (49.635463, 0, 0) 度 |
| Vertical FOV | 60 度 |
| Near / Far | 0.3 / 10000 m |
| 建议 Game View | 16:9 |
| 观察方向 | 从中心南侧朝北，向下约 49.64 度 |

使用 WGS84 ECEF 转换到现有原点的 East / Up / North 坐标系。相机在目标局部坐标上方 1000 m、南侧 850 m；Unity Y 不是海拔高度。
Assets/Data/DesktopStudyArea.json 保存区域和相机参数，作为人工复现记录；当前没有脚本自动读取它。更改 JSON 不会自动改变 Camera。
这是一套固定原点下的 Desktop 视角；若未来改变原点或加入 origin shifting，需要另行实现地理锚定。

## 验证状态与验收

已完成：原始场景未修改；所有 Cesium MonoBehaviour 块与原场景完全一致；新场景 ID 唯一；Transform 父子引用相互匹配。
2026-09-11 补充验收：已在 Unity 2022.3.55f1 打开开发场景，Game View 为 2560×1440（16:9）。地形、影像与建筑正常显示；入口连接道路在左侧，候车区在中央，泊位在右下方。完成两次 Play Mode 运行，初始取景一致，Console 显示 0 警告、0 错误。已退出 Play Mode 并保存，当前停留在开发场景 Game View。

结论：Desktop 环境与观察视角已具备进入 Vehicle Prefab + Route + Movement 技术原型开发的条件。路线具体车道、道路高度以及与实验材料的一致性仍需在下一模块核对；这不是正式用户实验验收。

后续可重复执行以下验收：
1. 打开 PortOfDover 场景，确认 Hierarchy 和 Console 无场景反序列化错误。
2. Game View 设置 16:9，进入 Play，等待 Cesium 地形和影像加载。
3. 核对入口、候车区与泊位是否在观察范围中；如需调整，只调整 Main Camera 并同步 JSON。
4. 退出并重新进入 Play，确认视角一致，地理数据无变化。
5. 核对既有实验材料所要求的区域与路线，再开始 Vehicle Prefab + Route + Movement 模块。

后续继续遵循 Mac First、每次一个模块、模拟数据先行。XR 当前仅为空分组。
