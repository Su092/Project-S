# 风笛野猪 · S21_BagpipeBoar

v003 模型交付。单位为米，Generic 骨架，1.8 m 人体参考，设定高度 2.16 m。

## 模型文件

- [Base](Exports/S21_BagpipeBoar_Model.fbx)
- [Parade](Exports/S21_BagpipeBoar_Parade_Model.fbx)
- [Ceremony](Exports/S21_BagpipeBoar_Ceremony_Model.fbx)

[源文件](Source/S21_BagpipeBoar.blend) · [实机导入数据](validation.md) · [资产清单](manifest.json) · [动作预览](Preview/S21_BagpipeBoar_Actions.mp4)

## Unity 接入

保留 Exports、Textures、Collision 与 Derivatives 的相对位置。模型 Scale Factor 设为 1 并使用文件单位。Rig 选择 Generic，从模型创建 Avatar。导入 16 个独立动作时复制基础模型 Avatar，按 manifest.animations 设置循环和事件，关闭 Apply Root Motion。模型文件含三档网格，按 manifest.lods.nodes 将 Renderer 分配给 LODGroup。每次加载一个外观。

UV0 共用一套 2048 px 图集。BC 采用 sRGB，ORM 与 E 采用线性采样。ORM 的红、绿、蓝依次为 AO、粗糙度、金属度。法线采用平坦常量。纸页和薄布使用双面渲染，描边与色阶由目标 URP 材质控制。碰撞文件按清单的骨骼及用途绑定。Source 中保存可编辑工程及动作，Preview 中包含多角度视图和全动作录像。
