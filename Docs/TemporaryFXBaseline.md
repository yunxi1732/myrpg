# 第三阶段第 0 步：临时效果基线

记录日期：2026-11-07。状态：静态核对完成，运行外观及 Profiler 录制待完成；不得视为性能基线已采集。未修改运行时代码、场景和预制体。

## 实际绑定与生命周期

MainScene 中 EntityFX/PlayerFX 和 Enemy Skeleton.prefab 的 EntityFX GUID 引用核对如下（残影仅玩家使用）。路径均相对 Assets。

|效果|实际预制体|组件与现有表现（静态证据）|生命周期（游戏时间）|
|---|---|---|---|
|普通命中|Prefabs/FX/HitFX_00.prefab|SpriteRenderer + Animator；金黄色调；HitFX_00_AC / HitFX_00.anim，非循环、无动画事件|动画 0.4167 秒；EntityFX 在生成后 0.5 秒 Destroy|
|暴击命中|Prefabs/FX/HitFX_Critical.prefab|SpriteRenderer + Animator；红色调；HitFxCritical_AC / HitFX_Critical.anim，非循环、无动画事件|动画 0.5833 秒，但现有 0.5 秒 Destroy 会提前截断，记录现状，不在池化时顺带修正|
|飘字|Prefabs/FX/PopUpText.prefab|TextMeshPro + PopUpTextFX；初始白色 alpha=1；上移速度 1，消失速度 10，淡出速度 1|Start 设 timer=0.75；随后淡出约 1 秒，alpha<0 时脚本 Destroy；总计约 1.75 秒，受帧步长影响|
|残影|Prefabs/FX/AfterImageFX.prefab|SpriteRenderer + AfterImageFx；复制当前角色 sprite，初始白色 alpha=1|MainScene colorLoseRate=1.5，约 0.667 秒后脚本 Destroy；生成冷却 0.03 秒，实际频率受帧率和调用影响|

以上四个预制体均无 ParticleSystem；命中特效没有自毁脚本，由 EntityFX 延迟 Destroy；飘字和残影各自脚本自毁。所有预制体初始缩放为 (1,1,1)。外观描述来自序列化颜色和脚本，不代表已经目视验收。

EntityFX 命中位置随机偏移 x/y 各 ±0.5；普通命中 z 随机 -90～90 度，暴击 -45～45 度并根据角色朝向设置 y 翻转。飘字偏移当前使用整数 Random.Range：x=-1 或 0，y=3 或 4。保留现状。

## alpha < 50：单独问题记录

当前 PopUpTextFX 在淡出开始后检查 myText.color.a < 50。初始 alpha=1，因此第一次淡出更新就切换 speed=10，并非淡到某个百分比才加速。第 0 步保留此表现；若要改为 0.5 或其他阈值，必须单独确认目标、单独提交并重录外观基线，不与对象池迁移混合。

## 固定战斗录制与对比规程（待执行）

使用 MainScene 的同一存档、角色装备、骷髅数量和站位；记录 Unity 版本、运行平台、分辨率、VSync/帧率设置与设备。迁移前后使用相同配置。避免录制期间改变装备和技能配置。

1. 打开 Profiler，目标 Editor Playmode，CPU Usage、Memory 开启；Deep Profile 关闭，记录 Allocation Call Stacks 是否开启并在对比时保持一致。
2. 进入同一战斗位置，预热 10 秒，清空采样后录制 30 秒：前 10 秒普通攻击，接着 10 秒继续攻击并捕捉暴击，最后 10 秒重复冲刺生成残影。记录实际命中、暴击、飘字、冲刺次数；随机暴击与操作次数不同的片段不得直接据总 GC 比较。
3. 同时录屏，逐项截图普通命中、暴击、飘字淡出开始/结束、残影开始/结束；记录暂停和帧率对持续时间的影响。若未出现暴击，另录补充样本并标记。
4. 保存原始 Profiler .data，记录 CPU 主线程帧耗时中位数/峰值、GC Alloc 每帧及总量、GC 次数、Instantiate/Destroy 调用次数与耗时；分开统计预热、战斗和 GC 尖峰。
5. 对比时使用同一采集选项和操作序列，报告实际触发次数；检查首次创建与池预热后的稳定复用段，避免把预热成本隐去。

尚无录屏、实测时长或 .data 文件，性能数值留空；完成采集前不推进池化迁移。池化范围仅飘字、普通/暴击命中特效、残影；禁止为回收而全局停止角色 EntityFX 协程或 CancelInvoke，保留闪白、冰冻、燃烧等现有效果。
