# WP_08 Processing Point 工程验收

日期：2026-10-06。Unity 2022.3.55f1，实际 Editor Play Mode。

这只是 Processing Point 可移动性验证，不是正式的新 Congestion Scenario，也不是实际 Dover 交通校准。

## 产品代码范围

仅 TrafficScenarioManager.cs：将 Queue Start 不在 Processing Point 上游的硬错误改为 Warning，并更新 Tooltip / 错误说明。引用非空、Route 直接子节点归属、处理点非终点及其他既有校验保留。移动、生成、等待、间距、processed 状态、回收和暂停/重置实现不变。

Scene、VehicleRouteFollower、TestTruck prefab、DesktopCameraController 的测试前后 SHA256 一致。临时 Editor 验收脚本及 .meta 已删除。

## 方法

- 从磁盘重新打开未修改的 PortOfDover.unity，先验证默认引用 WP_09 / WP_12。
- 只在 Play Mode 将 Processing Point 临时指向 WP_08；Queue Start 始终为 WP_09。
- 原参数不变：10 辆、正常速度 10 m/s、生成间隔 4 s、慢行速度 2.5 m/s、处理等待 12 s、中心间距 16 m。
- 临时 timeScale=4 加速完整周期；测量单位为 Scenario 秒。结束恢复 timeScale=1。
- 临时探针在 LateUpdate 读取真实 Manager / Follower 状态，不替换或手动调用交通移动循环。
- 每车到点时间以 RouteProgress 首次达到处理点容差记录；释放以 processed 转换为 true 记录，同时核对 serviceElapsed >= 12 s 和实际释放顺序。
- 每帧检查未处理车辆不越点、相邻车辆 progress 间距，并对所有不同车辆的 Body/Cab BoxCollider 组合执行 Physics.ComputePenetration。
- 在首车服务中暂停至少 1 个真实秒，比较 Transform、progress、服务计时、场景计时、生成倒计时和计数；恢复后继续计时。随后中途 Reset，检查清零及活动车辆清除，再开始完整周期。
- 最终顺序：WP08 暂停/重置检查 → WP08 完整周期 → 恢复 WP12 → FreeFlow → Congestion → Reset / 退出 Play Mode。

## 最终实测

| 项目 | WP08 工程测试 | FreeFlow baseline | WP12 Congestion baseline |
|---|---:|---:|---:|
| Completed / Active | 10 / 0 | 5 / 0 | 10 / 0 |
| 场景完成时间（s） | 226.7372 | 124.3861 | 324.2198 |
| 最小中心间距（m） | 15.99994 | 80.01331 | 15.99994 |
| 最大测得车辆间穿透（m） | 0 | 0 | 0 |
| 最多同时停止的未处理车辆 | 9 | 0 | 10 |
| 停止队列累计 progress 范围（m） | 449.8809–577.8808 | 无 | 684.7049–828.7049 |

WP08 队首位于 WP_08，队尾最远到 WP_06 下游约 2.08 m。车辆依靠现有 spacing 自然排队，无额外停车机制。

| 车辆 | WP08 实测停留时间（Scenario s） |
|---|---:|
| TestTruck_00 | 12.007260 |
| TestTruck_01 | 12.009330 |
| TestTruck_02 | 12.000240 |
| TestTruck_03 | 12.008060 |
| TestTruck_04 | 12.000030 |
| TestTruck_05 | 12.003060 |
| TestTruck_06 | 12.000900 |
| TestTruck_07 | 12.001400 |
| TestTruck_08 | 12.004470 |
| TestTruck_09 | 12.008670 |

每辆车按 00–09 顺序独立等待并释放，10 辆均到终点完成回收。正常终点回收仍沿用 Follower 原有 2 m 到达容差。

WP08 测试中没有未处理车辆进入 2.5 m/s 状态。10 辆车在 WP_09 边界时均 processed=True、Speed=10；下游没有 processed 车辆速度异常样本。原有 WP_09 边界距离限制保留，但不会再次触发慢行或处理等待。

最终受监测运行：2 次预期配置 Warning（暂停/重置测试启动、完整周期启动各一次），0 额外 Warning，0 Error/Assert/Exception。FreeFlow / WP12 baseline 无该配置 Warning。

## 记录与限制

- 最终原始记录：WP08EngineeringValidation.txt。
- 一次较早补测在交通启动前出现 Cesium 域重载连接取消 Error 和 invalid GC handle Assert，保留于 WP08EngineeringValidation-reload-run.txt。未据此改动 Cesium；随后不修改脚本、不手动刷新地复跑，未重现这两条消息。
- 首轮探针曾把新生成车辆的首次采样计为停止，影响队列位置统计；已排除首次采样并补测。本文与最终原始记录使用修正后的统计。
- 这属于加速的真实 Play Mode 机制测试；不替代正常速度下的视觉可读性、任意点位组合、多个瓶颈或 XR 验收。
- 最终 WP08 scaled deltaTime 为 0.006843–0.040804 s；最后 WP12 baseline 为 0.006379–0.376814 s。停留时间离散和单精度累计会有帧级误差，因此保留 service timer 原始值供核对。

结论：对当前路线及测试参数，WP12→WP08 的处理点迁移可驱动等待、上游自然排队和按序释放，且恢复后的两个 baseline 均通过。本次没有增加 Zone、Trial 或多瓶颈系统。
