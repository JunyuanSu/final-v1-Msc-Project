# TestTruck Basic Movement

当前 Movement 为 Desktop 技术原型，不代表真实港口车辆动力学或实际行驶速度。

## 挂载和参数

- Script：Assets/Scripts/VehicleRouteFollower.cs。
- 挂载：PortOfDover/Traffic/Vehicles/TestTruck 场景实例。
- Inspector 显式 Route：PortOfDover/Traffic/Routes/EasternDocksRoute。
- 正式 TestTruck.prefab 未修改，Body、Cab、材质与尺寸沿用。
- Speed：10 m/s；Rotation Speed：6（指数平滑响应系数）。
- Waypoint Reach Distance：2 m（水平方向）。
- Ground Offset：0.05 m；Play On Start：true。
- Ground Layers：Everything；Ray Height：50 m；Ray Distance：200 m。
- Height Adjustment Speed：8 m/s，限制垂直位置调整速率。

## 行为

启动时按 sibling index 缓存 Route 直接子节点，初始化至 WP_00，目标索引设为 1。初始化清除原静态 Pitch/Roll，local +Z 朝向 WP_01；移动时保持 upright，用 Quaternion.Slerp 平滑调整 yaw。
按 speed × deltaTime 预算在水平面移动，依序处理节点。到达阈值内进入下一个节点；终点同样使用 2 m 阈值，停止位置不要求精确等于 WP_14。不循环，不生成或销毁车辆，候车区节点正常通过。
平滑朝向会在急弯短暂滞后运动方向；这是 Transform 原型，不模拟转弯半径。

每帧从车辆/目标节点高度较高者上方 50 m 向下 RaycastAll，排除车辆自身子 Collider、Trigger 和过陡表面，选择最近有效表面。根原点在底面中心，只加 Ground Offset，不加半车高。命中后平滑调整 Y；Reset 时直接校准。
未命中时保持当前 Y，继续水平运动，下一帧重试；不回落到 Y=0。Ground Layers 可在 Inspector 限定。当前没有区分道路与屋顶语义，因此仍依赖人工路线几何，不具备避障能力。

## 控制与复现

在 Play Mode 中通过组件右上角菜单调用：
1. Stop Movement：停止水平移动。
2. Reset To Start：停止、清除 IsFinished、回到 WP_00、目标设为 1、重建 upright 朝向和高度。
3. Start Movement：开始或继续行驶。到终点后需先 Reset，才可再次 Start。

公开方法为 ResetToStart()、StartMovement()、StopMovement()；只读状态为 CurrentWaypointIndex、IsMoving、IsFinished；Speed 属性可由未来控制器设置。没有正式 UI 或实验逻辑。
取消 Play On Start 后重新进入 Play，车辆初始化到起点并等待 StartMovement。Context Menu 的 Reset/Start 只在 Play Mode 有效，避免覆盖编辑时静态摆放。

## 当前视觉限制

底面中心单点检测不能让 12 m 长、保持 upright 的车身完全贴合粗糙地形，坡面或 LOD 变化仍可能出现底面边缘间隙/局部交叠。垂直平滑降低跳变，但不等于悬挂或多点轮胎贴地。固定总览相机下车辆较小，需在 Scene View 近景检查。未改变相机或 Cesium 配置。

## 实际运行验收

- 两次完整 Play Mode 路线运行：91.95 s、92.46 s（Editor 墙钟时间，含编辑器运行波动）。
- 两次目标索引均严格记录为 1 → 14；完成后 IsMoving=false，两秒后水平漂移均为 0。终点位于 WP_14 的 2 m 到达阈值内，没有自动循环。
- Reset 回到 (948.427, -448.752, -353.238)，目标索引 1，IsMoving=false、IsFinished=false；随后 StartMovement 完成第二次路线。
- 第二轮强制三秒 Raycast 未命中：deltaY=0，IsMoving=true；恢复检测后完成整条路线。
- 观察到的最大相邻 Editor 采样 Y 差约 0.422 m / 0.402 m，不是对任意 LOD 的保证。没有跳至 Y=0。
- Console 可见 0 Warning / 0 Error（有普通日志）；RouteVisualizer 正常。
- 原始验证记录：Docs/MovementValidation.txt。临时 Editor 验证工具已删除，不是运行时数据记录系统。
- 本轮未开发 Traffic State、多车辆或实验逻辑。

## Two-Scenario 扩展（2026-10-04）

新增 Initialize(route, initialSpeed, surfaceLayers)、只读 RouteProgress、TickMovement(deltaTime, maximumTravel)。Initialize 标记由 Manager 驱动，自动 Start/Update 不再重复初始化或推进；Standalone 保留原行为。Manager 不调用 Update 的另一份运动实现，而是直接复用 TickMovement。
RouteProgress 为当前水平段投影加此前累计段长；受管理车辆先走完中间节点剩余短段再切换，以免到达阈值造成进度跳跃。终点仍沿用2 m阈值。
动态车辆全部放到 Vehicle Layer，并从地面检测排除；正式 Prefab 没有新增组件，Follower 由 Manager 在生成后配置。
原 TestTruck 调试实例在场景中禁用。单车回归时须在 Play Mode 中先 Reset Scenario，再单独启用该实例，避免与动态车辆同时运行。
