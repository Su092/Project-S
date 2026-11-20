# 铜管猎犬 · S03_HornHound

审阅版本 v001，2026-11-19。中性高度 1.08 m，1 款外观，共用一套 Generic 骨架。每款包含三档 LOD。参考人高 1.80 m。

## 打开资产

- [Blender 可编辑源文件](Source/S03_HornHound.blend)
- [基础模型 FBX](Exports/S03_HornHound_Model.fbx)
- [三分之四预览](Preview/Base_Hero.png)、[身高对照](Preview/Scale.png)、[全部动作视频](Preview/S03_HornHound_Actions.mp4)
- [资产清单与事件秒表](manifest.json)、[实测记录](validation.md)

## Unity 导入

1. 保持 Exports、Collision、Textures 和 Derivatives 的相对位置。模型导入 Scale Factor 1，使用文件单位，验证坐标为 +Y 上、+Z 前。
2. 基础模型选择 Generic 并创建 Avatar。动作 FBX 选择 Generic，复制基础模型的 Avatar。每个动作仅取同名 Clip，按 manifest.animations 的 loop 设置 Loop Time。关闭 Apply Root Motion。Unity 隔离工程测试版本为 6000.6.4f1。
3. 以 manifest.lods 中对应款式的节点分配 LODGroup，每一档只启用所属 Renderer。三种款式共用动作的单位可在相应模型文件之间切换。距离阈值由目标场景确定。
4. 材质使用 UV0 图集。BC 设为 sRGB，ORM 与 E 关闭 sRGB。ORM 的 R/G/B 对应 AO、粗糙度、金属度。法线为平坦常量，眼光及核心强度读取 E；运行时描边和两至三阶明暗由项目 URP 材质提供。
5. Collision 目录提供独立代理几何。按清单的用途与 bindBone 接到角色，语义事件按秒接入技能系统。姿势预览、灯光与相机保留在源文件及 Preview 中。
