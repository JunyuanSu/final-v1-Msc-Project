# TestTruck 静态 Prefab

## 资产与结构
- 正式 Prefab：`Assets/Prefabs/TestTruck.prefab`（唯一源）。
- 实例：`PortOfDover/Traffic/Vehicles/TestTruck`，共一辆。
- 子对象：Body、Cab，均为 Cube，带 BoxCollider；没有 Rigidbody。
- 材质：`Assets/Materials/TestTruckBody.mat`（洋红）、`TestTruckCab.mat`（青色），Standard 简单材质。
- 总尺寸：长 12 m、宽 2.5 m、高 3.6 m。
- Body：尺寸 (2.5, 3.6, 9)，局部位置 (0, 1.8, -1.5)。
- Cab：尺寸 (2.5, 3, 3)，局部位置 (0, 1.5, 4.5)。
- 根对象 Scale (1,1,1)，Prefab 原点位于底面中心；源 Prefab Position/Rotation 均为零。
- Forward = local +Z（青色驾驶室一侧），Up = local +Y，Right = local +X。

## 场景实例参数
- Position：(1420.000000, -449.510500, -245.000000)。
- Rotation Euler：(-3.3712, 110.4860, -16.4022) 度（与 356.6288,110.4860,343.5978 等价）。
- Scale：(1,1,1)。
- 坐标为当前固定 Cesium 原点下的 Unity 局部坐标，不是经纬度；Y 不是海拔。
- 位于 Eastern Docks 铺装停车区域附近；用已加载表面的向下射线与法线做视觉校准，底面中心加 0.03 m 偏移。
- 此位置不代表真实车道，Y 不代表真实道路标高。地形细节层级改变可能导致底面间隙变化。

## 脚本
最终 Assets 下新增 C# 数量为 0。构建时曾临时使用 Assets/Editor/TruckSetupUtility.cs 创建 Primitive、保存 Prefab、射线校准；未挂载对象，已连同 meta 删除。车辆没有交通或运动逻辑。

## 验证（2026-09-11）
已在 Unity 2022.3.55f1 验证：
- Prefab 正确导入，Hierarchy 实例链接至唯一源。
- Scene View 近景能辨认两个部件、重型车辆比例与 +Z 车头。
- 铺装区域附近未发现穿建筑。
- 进入 Play Mode 后 Inspector Position/Rotation/Scale 不变。
- 删除临时工具后重新编译，Console 0 Warning / 0 Error。
- 已退出 Play Mode 并保存，Scene View 留在车辆近景。

未完全通过的验收项：
- 固定 Main Camera 总览下车辆仅占少量像素，不能宣称“清楚可辨”；需要后续获准提供检查视角或选择交互，当前没有修改 Camera。
- Cesium 地形 LOD 切换时近景存在小幅底面间隙，单点法线校准不能保证完整车辆底面始终贴地；尚未通过所有加载状态下的严格贴地验收。

因此本模块资产已创建，不能标记全部视觉验收通过。未开发 Route、Movement、Traffic State、实验系统或 XR；不是正式用户实验版本。
