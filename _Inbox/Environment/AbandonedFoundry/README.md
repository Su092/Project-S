# 失控铸造场 — 模块化环境交付
工作目录：E:/S/_Inbox/Environment/AbandonedFoundry
Unity 工程：E:/S，Unity 6000.6.4f1 / URP 17.6。

## 打开与巡检
打开 Assets/_Game/Res/Scene/Environment/AbandonedFoundry/Foundry_Arena.unity。
Play 后 WASD 移动，Shift 跑步，鼠标转向，左键射击碰撞探针，Esc 释放鼠标，右键重新锁定。
这是独立环境巡检场景，不包含敌人、Boss、掉落或生产战斗逻辑。16 个 Spawn_XX 和 PlayerSpawn 为接入标记。
模块 Prefab 已配置 LODGroup 和 Collider。普通方形结构使用 BoxCollider；柱、桶、门洞分件和坡面使用准确代理网格。没有依赖 FBX 命名前缀自动生成碰撞。

## 资源位置
- Source/Foundry_Modules.blend：模块源与总览排列，已内嵌贴图。
- Source/Foundry_Layout.blend：完整拼装源；COLLISION 中放置碰撞实例，默认不渲染。
- Foundry_Layout.fbx：全场可见网格布局，作为交换与查看用途；Unity 采用模块 Prefab 布局。
- Assets/_Game/Res/Model/Environment/AbandonedFoundry：51 份模块 FBX 和规范化 UnityMeshes。
- Assets/_Game/Res/Texture/Environment/AbandonedFoundry：共享 1024 颜色/法线/遮罩图，共 6 套，包含 Trim。
- Assets/_Game/Res/Material/Environment/AbandonedFoundry：URP Lit 共享材质。
- Assets/_Game/Res/Prefab/Environment/AbandonedFoundry：51 个已配置模块。
- Assets/FoundryTools：可重复执行的 Unity 导入、场景生成、巡检脚本。
- Reports/ModuleManifest.csv：实测尺寸、三角面、原点例外、材质槽、LOD、碰撞用途。
- Reports/UnityValidation.txt 与 BlenderValidation.json：实际运行结果。
- Previews：顶视、标注布局、模块总览、玩家视角、碰撞叠加，以及 Unity 截图。

## 拼装与坐标
1 Blender 单位 = 1 米。结构按 1m 主网格与 .25m 细分；地面连续 25×25 块 4m 模块。
Blender +Z 上、-Y 前；生成 Unity Prefab 使用 (x,z,-y)，+Y 上、+Z 前。
FBX 原始导入受工程旧规则影响存在手性转换；生成器将转换烘焙进可复用 UnityMeshes，不对每个布局实例添加负缩放。
Prefab 根节点作为实际拼装原点。地板/转接平台顶面原点、中央圆形模块圆心原点、管件端点原点是清单中注明的例外。
中央平台半径 9m，高 .25m，9–10m 环带为缓坡；能量柱底座在 (±10,±10)，可独立替换。
南侧高台整体向东偏移 18m，保留原定出生点 (0,1.2,-38) 开阔视野。其他三座在各边中段。
关卡保留非常宽的开放战斗空间；4–6m 是预期主线尺度，不表示所有开放场地都限制为走廊宽度。

## 碰撞接入
图层由编辑器查找空闲层显式建立，不硬编码层号：
- FoundryMovement：楼梯的角色移动坡面。射击查询必须排除此层。
- FoundryShot：楼梯真实台阶代理。角色层 FoundryPlayer 与它不碰撞。
- 其余地板/墙/掩体同时服务移动和射击。
示例控制器已配置分离。接入生产控制器、敌人、经验拾取和弹道时，请使用相同图层策略；环境交付没有改写现有战斗代码。
门洞由左右立柱和上梁三个碰撞体组成，绝不封住门洞中央。
能量柱完整件和残骸是不同 Prefab；破坏时替换对象即可同步切换对应 Collider。

## UV / LOD / 材质
材质 UV0 使用米制投影，4m 对应一张 1024 平铺图；圆柱投影密度随面方向变化。
UV1 独立打包用于光照；没有烘焙 lightmap，示例使用实时照明。
Mask: R=Metallic, G=AO, B=0, A=Smoothness；Normal 为切线空间，线性导入。
Trim 分四条 1m 带：钢边、警示、格栅、旧化边。
每个可见模块均有 LOD0/LOD1；主要掩体和复杂模块约 40%–60%。
简单构件、圆形拼接边和部分装饰保留最低必要拓扑，部分 LOD 比例不在目标范围，详见 BlenderValidation.json。
若要求所有非地板模块严格 300–3500 tris、所有模块严格 40%–60%，本版仍有例外，不能视作该项全数通过。没有为凑面数机械细分模型。

## 验证边界
已执行导入尺度与轴向断言、出生/刷怪点胶囊重叠、地面连通网格、Boss 环绕、8 条楼梯/坡道高度剖面、实际 CharacterController 上坡。
这些检查不替代生产角色、弹道、敌人导航和经验掉落系统的联调。
源码保留复现步骤；Tools > Foundry > Build Delivery Example 可重新生成。

射击抽样：对 10 类关键模块从六向共投射 13,500 条射线，碰撞相对 LOD0 可见表面提前超过 6cm 的样本为 0；详见 ShotCollisionQA.txt。
