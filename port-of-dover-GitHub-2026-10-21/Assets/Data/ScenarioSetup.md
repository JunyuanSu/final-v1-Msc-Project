# Two-Scenario Traffic Prototype

当前 Scenario 参数均为 Development / Test Parameters，仅用于技术原型验证，不代表 Port of Dover 的真实交通状态。最终实验参数将在后续文献调研、可获得的港口数据以及 Pilot Test 基础上进一步校准。

## 配置与文件

- TrafficScenarioManager.cs：挂在 PortOfDover/Traffic/TrafficStates；负责本轮参数快照、生成、前车间距、队首等待、回收及控制状态。
- VehicleRouteFollower.cs：场景管理模式增加显式初始化、沿路线进度和受限移动；保留单车模式及原有转向、贴地方法。
- TrafficScenarioManagerEditor.cs：只读显示状态、锁定场景、Spawned/Active/Completed 与场景时间。
- Inspector 引用 TestTruck.prefab、EasternDocksRoute、Vehicles 容器和原 TestTruck 调试实例。无运行时名称搜索。
- 原单车实例保留但禁用；正式 Prefab 几何、材质及组件不变。
- 动态车辆生成在 Traffic/Vehicles/ScenarioVehicles 下。根和子对象递归使用 Layer 8（Vehicle），地面射线排除此层。

## 开发参数

| 参数 | FreeFlow | Congestion |
|---|---:|---:|
| Vehicle Count | 5 | 10 |
| Normal Speed | 10 m/s | 10 m/s |
| Spawn Interval | 8 s | 4 s |
| Queue Speed | 不使用 | 2.5 m/s |
| Processing Waiting Time | 不使用 | 12 s |
| Minimum Center Spacing | 16 m | 16 m |

车辆约 12 m 长；16 m 是沿水平路线进度的中心间距，不是保险杠净距。默认直路净空约 4 m；不能由此推断任意急弯均无车体相交。

## 路线与队列规则

RouteProgress = 当前水平段投影 + 前面各水平段长度之和；不受车辆 Y 调整影响。节点按直接子对象 sibling index 读取。
Manager 每帧按生成顺序从前车到后车执行；允许移动距离最多为前车进度减去16 m再减本车进度。Follower 在 speed × deltaTime 和这个上限中取较小值。管理模式中间节点需走完剩余短段才切换，防止 2 m 到达阈值让进度跳过约束；原单车中间节点行为不变。

WP_09–WP_12 为候车区；进入前先将本帧移动限制在区段边界。Congestion 进入后使用 Queue Speed；WP_12 的路线进度是抽象 Processing Point。只有最前方未处理且已到处理点的车辆开始累计等待12 s；到时且前方有空间才放行，每车只处理一次。后车沿线自然积累，不预分配停车位。离开处理点恢复 Normal Speed。

首次 Start 立即生成一辆。之后按场景时间间隔生成；入口不足16 m空间时等待，恢复后只生成一辆并重新计间隔，不集中补发。WP_14 仍使用原2 m到达阈值，到达后计为 Completed，立即停用并在帧末 Destroy；不堆积终点车辆，不使用对象池。

## 操作

进入 Play Mode，选择 TrafficStates，在 TrafficScenarioManager 组件菜单使用：

1. Start Scenario：Idle 时验证依赖并锁定全部本轮参数；Paused 时恢复原轮次。
2. Stop Scenario：Pause，冻结车辆整个 Transform、生成倒计时、处理点等待及场景时间，不修改 Time.timeScale。
3. Reset Scenario：动态容器立即停用、帧末删除；清空全部车辆、计数、等待和时间，回到 Idle。

Completed 后必须 Reset 再 Start。Inspector 修改仅下次 Reset/Start 生效；暂停恢复不会重新读取配置。运行中不要手工移动 Waypoint 或删除动态对象。
地面适配在正常排队等待期间继续更新；Pause 时完全冻结。射线未命中保持 Y。参数或关键依赖无效时不创建车辆，并报告明确错误。

## 已知边界

不模拟真实交通动力学、加速度、转弯半径或避障；平滑 yaw 在拐角可能短暂滞后。路线是既有人工模拟折线，不代表真实通行组织。底面中心单点检测与保持 upright 在坡面/LOD变化时仍可能造成车身边缘间隙或局部交叠。相机维持原状，开发检查可用 Scene View。

本轮不新增正式 UI、实验计时/数据系统、XR、交通可视化标签或真实参数校准。

## 实际 Play Mode 验收（2026-10-04）

使用 Unity 2022.3.55f1 实际运行。完整周期使用临时 Editor 工具以4倍时间加速验证；场景时间仍由 deltaTime 推进，产品代码不修改 Time.timeScale。另设15 FPS目标帧率检查较大步长；首车处理等待阶段切回1倍时间作观察。临时工具和帧率/倍率调整已移除或还原。

| 测试 | 结果 |
|---|---|
| FreeFlow → Reset → Congestion → Reset → FreeFlow | 5/10/5 辆全部完成回收，结束 Active=0 |
| 两轮 FreeFlow 场景耗时 | 124.61 s / 125.28 s；生成约每8 s；最小中心间距约80.13 m以上 |
| Congestion | 324.18 s；生成约每4 s；最小中心间距15.99994 m（浮点误差） |
| 低帧率 Congestion 补测 | 10辆全部完成，队列区最多同时10辆，最小中心间距15.99994 m |
| 车体重叠 | 对所有动态车辆 Body/Cab BoxCollider 使用 ComputePenetration 检查；默认参数两种场景及低帧率补测均无超过0.01 m的相互穿透 |
| Processing Point | 10次实际离点测量，等待12.003–12.293 s；离点不早于12 s，额外时间来自帧步长 |
| 暂停 | 早期生成阶段和队首处理等待阶段，车辆Transform、场景时间、处理计时保持不变 |
| 中途 Reset | Active=0、Spawned=0、Elapsed=0；无活动残留车辆，再次Start成功 |
| 参数锁定 | 运行中切换类型和数量不影响本轮；Reset/Start后新数量7进入锁定配置 |
| Ground Raycast | 全部动态根/子对象为Vehicle Layer，Ground Layers不包含该层 |
| 原单车回归 | 目标1→14完整运行，到终点停止，Reset恢复目标1并清除Finished，再次Start后前进约20 m |
| Console | 最终0 Error / 0 Warning |

低帧率补测同时检查沿路线间距与实际几何，结果只覆盖当前路线与开发参数，不保证任意新路线/车长/间距组合。地面LOD仍可能产生原有底面边缘间隙；测试期间Scene View远处瓦片出现过流式加载空缺，不影响已加载候车区域的队列检查。

首轮临时记录曾把目标索引切换误当作离点，导致等待记录无效；上表等待结果采用修正后的实际RouteProgress越过处理点测量，交通控制逻辑无需因此修改。

## 最终文件变更

新增：
- Assets/Scripts/TrafficScenarioManager.cs 和 .meta
- Assets/Editor/TrafficScenarioManagerEditor.cs 和 .meta
- Assets/Data/ScenarioSetup.md 和 .meta

修改：
- Assets/Scripts/VehicleRouteFollower.cs
- Assets/Scenes/PortOfDover.unity
- ProjectSettings/TagManager.asset
- Assets/Data/MovementSetup.md

删除既有文件：无。临时验证工具在交付前删除，不作为工程功能保留。
