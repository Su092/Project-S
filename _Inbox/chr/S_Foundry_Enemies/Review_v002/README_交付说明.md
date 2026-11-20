# S 失控铸造场 · 敌人制作评审包 v002

本包涵盖全部 9 个敌人的首轮模型与技术交付，并非最终美术验收通过或游戏接入完成。完整待办见 QA_PENDING.md。

## 打开方式
先打开 index.html 查看全员同尺度图、模型预览、动作 GIF、碰撞叠加和统计。每个资产目录含可编辑 .blend、Exports/模型及逐动作 FBX、Previews/。COL_Blockout 保存独立可编辑零件，COL_Geo 为绑定后的三个 LOD。Reference 中有 1 米方块与 1.8 米人物量尺。

## 单位、方向、骨架
Blender 单位为米，Z 向上、-Y 向前。Unity 导入报告以 X 宽、Y 高、Z 深记录；brief 以宽×深×高记录，比较时交换后两项。
根骨骼位于原点。地面单位落地，环架单位轴心位于可见包围盒中心，推荐展示悬浮高度为机体半高 + 0.4 米；游戏可自行设置。
全部动作 30 fps，原地。Root 不产生游戏位移。每个动作单独 FBX；模型 FBX 含同骨架的三个 LOD。动作文件需按项目 Animator 设置连接 Generic Avatar。
精英与普通版共享骨骼命名和层级，额外机构使用新增骨骼；不同体型的动作复用仍需实机检查。

## 材质与外置图集
普通和精英各 1 个材质；Boss 为 2 个。精英使用同家族普通版 Textures，不重复增加图集。Boss 主体与机构各一套 2048。
BaseColor：sRGB；Normal：切线空间 OpenGL，Unity Texture Type=Normal map；
Metallic、Smoothness、EmissionMask：线性；
Emission：sRGB，独立发光遮罩，非发光区为黑色。
MetallicSmoothness：R=Metallic，G=0，B=0，A=Smoothness。直接连接 URP Lit 的 Metallic Map，Smoothness Source 取 Metallic Alpha。独立 Metallic、Smoothness 同时交付便于编辑。
BaseColor 接 Base Map，Normal 接 Normal Map，Emission 接 Emission Map，启用材质 Emission。
每个敌人的亮度可由独立材质实例或 MaterialPropertyBlock 的 _EmissionColor 调节；不要修改共享图集或全局材质亮度。
材质最终仅依赖外置图片与标准表面参数；不依赖 Blender Noise、Voronoi 等程序纹理。
采用 8 格制造表面 trim 图集，有意复用相同表面 UV；尚不是逐零件唯一 UV、高模烘焙或最终纹理密度验收稿。边缘局部锈蚀已绘制，未铺全局随机噪点。

## 碰撞
移动和受击代理单独交付为 *_CollisionReference.fbx，保留各自 proxy_role / bind_bone 信息的源 .blend。
环架采用分段代理，支腿逐段分离；中央空气和支腿间隙不以整盒封闭。
这些是绑定参考，Unity 不会因命名自动正确配置移动和受击逻辑。需在项目中按角色、骨骼、Layer 与武器规则显式绑定。
PressureRunner 家族额外提供 FragmentAssets.fbx/.blend，不含爆炸或碎片飞行逻辑。

## 验证范围
validation_report.json 是独立 Blender 进程的实际模型/逐动画 FBX 回导结果，包括尺寸、拓扑、权重、UV范围、根骨骼及循环首尾数据。
UnityImportReport.json 来自 Unity 6000.6.4f1 的独立验证工程，检查真实导入的尺寸、Generic Avatar、动作数量和法线数值。它不是 S 关卡内的实际战斗验证。
动作 GIF 是模型动作预览；Boss 每个技能在 Actions/<动作>/ 下另有正/侧/背/玩家低视角的预备、命中、恢复截图。
原有 S 游戏资产未被覆盖；交付目录不自动进入游戏资源表。
