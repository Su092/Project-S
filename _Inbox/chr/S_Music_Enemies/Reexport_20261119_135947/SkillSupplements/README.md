# 音律能力补充模型 · Step 2
交付状态：已建模、已导出、已通过 Unity 6000.6.4f1 基础导入核验。游戏视觉验收待完成，未标记最终 PASS。

## 资产
- SM_MusicFollowSpot：LOD 1732 / 866 / 288 tris；Generic root→yaw→pitch，四个遮扉骨骼，三个挂点。Idle 1秒、FocusIn/Out 各0.5秒、RigTest 2秒，30fps，根节点无世界位移。
- FXM_S14_NoteGate：LOD 482 / 238 / 180 tris；Unity 实测 2.2×3.6×0.35m，原点底边中心。复用原 MarchingNote 音符几何；完整透明音幕为独立双面平面。提供静态版，可选动作未制作。
每件包含 Source、Model、Animations（静态资产为空）、Collision、Textures、Preview、manifest.json、Unity 验证记录与 SHA256。

## 使用
1. Model FBX 包含全部三档 LOD，每档实体与镜片/音幕为独立网格；在 Unity 配置 LODGroup，避免三档同时显示。
2. BC 使用 sRGB；ORM、N、E、Alpha 使用线性。ORM 为 R=AO/G粗糙度/B金属，需要按目标管线接线；N 按法线贴图导入。E 为遮罩。
3. 追光灯仅镜片发光。使用 Socket_LightOrigin 的 +Z 出光。模型不含实际 Light/Camera。追踪由游戏控制，RigTest 仅验收。
4. 音幕 Curtain PNG 已带 alpha，并另附 Alpha 灰度贴图。材质使用双面透明，危险橙红可由游戏染色，建议单层 alpha blend/dither，按游戏管线验收排序；不要让透明纹理以不透明默认 FBX 材质运行。
5. 音符幕 COL 仅尺寸代理，禁止作为阻挡玩家的实体墙；灯具 COL 只覆盖灯头，运行默认装饰。
6. 源文件保存默认静止姿态、LOD0 可见；源文件保留灯光相机用于预览，FBX 不含它们。

## 核验与限制
Unity 已核验音幕尺寸、灯具挂点 +Z、动作时长、30fps、无丢权重和根位移。机械测试逐帧检查灯头/遮扉与支架表面，排除转轴设计接触区域，见 mechanical_check.json；不是全部部件之间的碰撞证明。
灯具骨架沿用原项目实际导出约定，禁用 bake_space_transform；启用该项会导致骨骼挂点朝向偏转，已通过 Unity 对照试验确认。网格变换已应用。
最终材质、透明排序、LOD 切换、动态追踪需在实际游戏场景验收。
Preview 提供正背侧与45度、人物比例图；音幕另含10m/20m及脚底离地1.3m的跳跃参照。
