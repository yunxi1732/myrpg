# 已完成修改记录

仅记录已落地改动；编译和 Unity 操作结果分别记录，未验证不算通过。后续小修改只追加本文件，不再单独创建说明文档。

## 2026-11-03
- **第0步基线**：未改运行时代码；检查拾取、堆叠、移除、换装、合成、保存、读取的现有流程。测试存档及 SHA-256 清单保留在 `Baseline-2026-11-03/SaveBackup/`，原路径为 `C:/Users/谭心航/AppData/LocalLow/DefaultCompany/rpg/rpgData`。备份哈希一致；七项 Unity 操作均待验，基线运行验收未完成。实际操作依次检查数量增减、换装属性、合成材料与产物、退出重进后的存档一致性。
- **合成旧缺陷修复**：旧流程按配方数量检查却每条只扣1；现改为按配方数量调用 RemoveItem，未改配方及接口。Unity 用要求5份材料的配方验证：4份失败不消耗，5份成功清零，7份成功剩2；另查多材料配方。静态检查通过；Unity 编译、操作待验。
- **IOCContainer 精简迁移**：从 mmo-demo 提取 Register/Get，去掉 Unity 日志与模块初始化依赖。按显式类型保存引用，获取同一实例；重复注册覆盖，未注册或注册 null 返回 null；容器独立。原游戏仍用旧流程，未接入背包。Unity 执行 `Tools > RPG > Verify IOCContainer`，应输出 `[IOCContainer] PASS: 8 checks`，再运行原游戏检查移动、战斗、背包及存取档。静态检查通过；Unity 编译、菜单检查、游戏操作待验。
- **文档整理**：合并基线、合成修复、容器迁移说明；保留改造计划、本记录及存档备份，后续沿用。
- **Architecture 与层接口**：新增最小 IArchitecture、IModel、ISystem、IUtility、INeedInit，保留源 Regist/Get 命名。旧容器统一遍历初始化改为显式 InitAllModules：Utility → Model → System，层内按注册顺序；重复调用不重初始化，同对象多接口只初始化一次。架构重复类型注册、开始初始化后注册均抛异常；初始化失败停止且禁止重试。未引入登录、Hotfix、事件或命令框架，原游戏流程未接入。Unity 执行 `Tools > RPG > Verify Architecture` 应输出 `[Architecture] PASS`，覆盖顺序、次数、别名、注册限制和失败停止，再运行原游戏。静态检查通过；Unity 编译、菜单与游戏操作待验。
- **架构退出清理**：旧架构无释放入口；新增 Dispose，按实际初始化逆序调用模块 IDisposable（可选），同对象仅释放一次，重复清理无操作，清空全部注册引用。失败初始化的模块也参与清理；释放异常收集后统一抛出，不阻断其他模块释放。已释放架构禁止再次注册/初始化，重新运行创建新架构。新增可选 ArchitectureHost，在退出/销毁时清理，无静态实例，未修改现有场景。Unity 执行 Verify Architecture 检查逆序、重复、重建、异常清理及 Host 销毁；空测试场景挂 Host，连续两次 Play/Stop（含关闭 Domain Reload）确认无异常。静态检查通过；Unity 编译、菜单、Play/Stop 待验。
- **事件注册、发送与注销**：迁入精简 GameEventSystem，通过 Architecture.RegistEvent/SendEvent/UnRegistEvent 使用，保留按事件类型及 Action<object> 传参方式；重复注册重复通知，注销一次移除一次，注销最后一个后移除条目。架构释放先清空监听；释放后发送/注销无操作、注册抛 ObjectDisposedException；通知中释放也停止后续回调。实例之间不共享监听，尚未接入背包。Unity 执行 `Tools > RPG > Verify Events` 应输出 `[Events] PASS`，再回归 Verify Architecture 和原游戏。静态检查通过；Unity 编译、菜单及游戏操作待验。
- **普通背包 Model**：原 Inventory 自建列表/字典改为 InventoryModel 持有，Inventory.Start 显式注册初始化后将原公开字段绑定到 Model 同一集合；列表与字典共用 InventoryItem，无数据副本。保留拾取、容量、堆叠、移除、UI、存档规则及场景字段，材料仓库和装备栏未迁移。Inventory 销毁释放架构并清理自身单例引用；Model 不主动清空集合，避免影响退出保存。Unity 实际拾取同种装备两次，核对一格数量+2；Ctrl+点击验证减1和清零移除，满包拒绝拾取保持旧行为；每步执行 `Tools > RPG > Verify Inventory Model (Play Mode)` 应输出 `[InventoryModel] PASS`，核对共享集合与条目引用；退出重进核对存档。静态检查通过；Unity 编译、实际拾取/堆叠/移除及存档操作待验（原基线操作亦尚未实测）。
- **材料仓库 Model**：原 Inventory 自建 stash/stashDictionary 改为引用 InventoryModel.Stash/StashDictionary，共用条目对象；保留字段、GetStashList、拾取/堆叠/移除和合成扣料及显示路径，配方与存档格式不变。Unity 拾取相同材料两次核对堆叠，左 Ctrl+点击验证减1和清零；B 打开合成核对材料图标及需求数量，足量合成后核对剩余材料。每步执行 Verify Inventory Model (Play Mode) 应 PASS，另退出重进核对材料保存。静态检查通过；Unity 编译、操作及显示待验。
- **已装备数据 Model**：原 Inventory 创建 equipment/equipmentDictionary 改为引用 Model.Equipment/EquipmentDictionary；原 GetEquipmentList/GetEquipment 查询同一数据，穿戴、同类替换、旧装回包、属性加减及存档流程保留。Unity 记录属性P，穿戴A后应为P+A，替换同类型B后应为P+B、A回包；往返替换检查属性无累加。每次执行 Verify Inventory Model (Play Mode)，核对集合同源、条目引用、每类型唯一及装备查询；退出重进复查装备和属性。静态检查通过；Unity 编译、穿戴/替换/属性及查询操作待验。
- **初始化时机**：原 Inventory.Start 与 SaveManager.Start 无序竞争，且以 loadedItems 非空判断存档；现 Inventory 提前 Awake 创建 Model/绑定集合，Start 缓存含隐藏槽位，下一帧等待 SaveManager.HasLoaded 后仅填充一次。LoadedFromSave 明确区分新游戏与空存档；读档缓冲先清空、初始化后忽略重复背包 LoadData，未就绪不回写背包存档。保留原字段和接口。Unity 备份存档后测试无档新游戏初始物品、普通存档重进、空背包/仅装备存档不回填初始物品；连续重进及重复 LoadGame 检查物品/属性不增加，Verify Inventory Model 应 PASS，Console 无空引用。静态检查通过；Unity 编译及上述操作待验。
- **背包 System 添加规则**：原 Inventory 内添加/堆叠逻辑迁入 InventorySystem，Model 先于 System 注册初始化；原 AddItem/CanAddItem 转发，容量由槽位提供，UI 仍原处刷新。装备进普通背包，材料进仓库；保留满包连已有装备也拒绝、材料不受普通背包容量限制的基线。Unity 实际拾取两类物品检查归属和堆叠，填满背包后检查装备拒收、掉落保留及材料可收；运行 Verify Inventory Model 检查共享数据。Verify Inventory System 菜单另查独立规则，应 PASS。静态检查通过；Unity 编译、菜单及实际操作待验。

## 2026-11-04
- **移除物品规则**：原 Inventory.RemoveItem 内规则移至 InventorySystem，保留原入口和 UI 刷新；普通背包和仓库每次减1，最后一个同时从列表/字典删除，不存在时无操作。原穿戴、合成和 Ctrl+点击继续通过兼容入口调用。Unity 运行 Verify Inventory System 检查2→1→删除及重复移除；游戏中对装备、材料各 Ctrl+点击两次，每步执行 Verify Inventory Model，检查UI槽位与数量；另确认穿戴和合成扣除正常。静态检查通过；Unity 编译、菜单与实际操作待验。
- **卸装规则**：原 UnequipItem 的装备列表/字典删除及 RemoveModifiers 迁至 InventorySystem，Inventory 原公开入口转发；未装备物品不重复扣属性。原换装仍按卸下旧装→回包→穿新装执行，UI刷新位置不变。Unity 准备同类型A/B且背包留空间：记录基础属性P，穿A为P+A，换B后A回包、装备查询为B、属性P+B，往返替换无累加；每步运行 Verify Inventory Model 检查装备记录一致。静态检查通过；Unity 编译、换装与属性操作待验。
- **穿戴与替换规则**：原 EquipItem 查找同类旧装、卸装、回包、穿新装、加属性、移除背包物品迁至 InventorySystem；Inventory 原入口转发并统一刷新UI，保留原先回包容量判断及操作顺序。Unity 背包留空间，记录基础属性P，穿A后P+A，换同类B后A回包且P+B；A/B往返至少5次检查数量守恒、每类型仅一件、属性不累加，每步 Verify Inventory Model 应 PASS。满包回包仍遵循旧容量规则，未夹带修复。静态检查通过；Unity 编译、换装及属性验收待执行。
- **合成规则**：原 canCraft 的全量材料检查、按配方数量扣除、添加产物迁至 InventorySystem.CanCraft；原入口保留日志和UI刷新。材料不足先返回不改数据，沿用此前已修复的精确扣料及原容量行为，未改配方资产。Verify Inventory System 增加缺少后续材料、数量不足、成功精确扣料/产物及配方不变检查。Unity B界面分别测试不足/足量和多材料合成，核对数量、产物与需求显示，再运行 Verify Inventory Model。静态检查通过；Unity 编译、菜单和实际合成待验。
- **药瓶与护甲冷却**：UseFlask/CanUseArmor 规则及计时移入 InventorySystem，Inventory 原方法转发 Time.time，flaskCooldown 继续供原UI读取。药瓶仍调用原 Effect(null)，护甲效果脚本不变；保留严格超过冷却结束才触发及换装不重置计时。无护甲返回false以避免空引用。Unity Verify Inventory System 检查首次/冷却中/边界/结束及独立计时；实际装备药瓶按1核对效果与UI冷却，重复按键确认不重复生效；穿效果护甲满足原触发条件，核对首次效果、冷却中不触发、结束后再触发。静态检查通过；Unity 编译、菜单、实际效果/UI待验。

## 待修改
- Blackhole 技能：黑洞里出现血条，需检查来源和显示。
- 背包药瓶：缺少对应 UI。
