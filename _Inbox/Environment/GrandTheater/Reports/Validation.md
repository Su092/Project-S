# 鸣奏大剧院 · 验证报告

## 交付与验收环境

资产文件位于 `E:/S/_Inbox/Environment/GrandTheater`。用 Blender 5.3.0 制作，以 Unity 6000.6.4f1 和 URP 17.6.0 验收。Unity 物理和渲染检查在独立工程执行，渲染使用游戏工程 PC URP 配置的副本。

| 项目 | 实测 |
| --- | ---: |
| 模块库 | 76 种，含 LOD0 / LOD1 |
| 全场景实例 | 1,191，Unity 复用 70 种网格 |
| 全场景实例 LOD0 | 205,432 tris |
| 碰撞代理实例 | 1,135 |
| 共享材质 / PNG / 交付预览 | 8 / 24 / 11 |
| 单模块 LOD0 峰值 | 3,096 tris，SM_Theater_Organ_PipesCenter |

模块库含 6 个当前布局备用件。结构轴线 64 × 72 m，周边墙厚向轴线两侧展开，布局与图纸同步。大厅、舞台、二层与拱顶内侧标高分别为 0 / 1.2 / 6 / 18 m。侧翼遇楼梯时并入内侧通带，轴线跨度 6 m。二层侧廊扣除两侧栏杆底座后约 5.3 m。楼梯四组共用 20+20 级，0.15m 踢面、0.32m 踏面、2.5m 平台。扣除扶手后单跑约 2.74m。观众厅中心可划出 8m 主道，舞台中央保留至少 18 × 10m 连续空场。

## Unity 走查与射击

- 以直径 1m / 高 2m 的 Unity CharacterController，沿 13 条路线分别正反向走查，26 次全部通过。涵盖一层双环路、二层闭环、两组双入口包厢、四座折返楼梯，以及两处台阶和两处缓坡。最大平面偏移 1.29cm。
- 在 3,531 个点检查连续支撑和 2.80m 净空。最高支撑标高差 2.50cm。主要楼梯坡面和舞台标高接缝在抽样中吻合。
- 比对 9,720 条 Unity 射线的可见网格与 Shot + Both 代理，提前超过 6cm 命中 0 次。563 条射线按清单穿过帷幕、灯具等轻型装饰，类型分布见原始走查记录。
- 门洞使用左柱、右柱、拱顶三份代理。地板和实墙为 Both。楼梯连续坡面为 Movement，原台阶为 Shot。栏杆的 Movement 防坠面只参与移动，上部真实杆件参与射击，孔洞保持畅通。管风琴管组保留管间几何。每个代理均记录可见实例 ID 和放置变换。
- 代理分布为 Movement 133、Shot 213、Both 789。另有 207 个可见实例按设计免碰撞。完整记录见 [碰撞清单](CollisionManifest.csv)。
- 用三个 1m 方向标记、实际世界坐标和异形几何顶点检查导入量纲、手性和朝向。Unity 读得 X 右、Y 上、Z 朝舞台，异形锚点 (0.25, 0.50, 0.75)，顶点误差小于 0.001m，缩放 (1,1,1)。逐实例核对世界位置与碰撞对应。Unity 巡游场景另对旋转组件等 14 个世界包围盒抽样，误差小于 0.01mm。

## UV、LOD 与画面

- Blender 对 152 个 LOD 网格逐三角形检查 UV1，重叠、越界及退化均为 0。光照图岛间标称留白约 5.6px（以 1024 像素坐标计），平均覆盖 63.7%，最小 10.6%。极长、极薄部件受长宽比影响，覆盖较低。Unity 复查了所有模块的 UV0、UV1。
- 平铺 UV0 每 4m 重复一轮 1024 纹理，主要结构面约 256px/m。Trim 纵轴按带宽归一。BaseColor 使用 sRGB，Normal 是切线空间且设为线性，Mask 线性 RGBA 对应 Metallic / AO / 0 / Smoothness。材质最多 3 槽，常见简单件一到两槽，复杂护墙及重点装饰的槽位详见清单。
- 47 个模块 LOD1 落在 40%–60% 区间，清单标记其余例外。50 件 LOD0 少于 300 tris，主要为平板、直墙、薄片、栏杆段及采用简洁轮廓的道具。节省重复实例成本。
- SM_Theater_Roof_Vault4 的 LOD1 为 LOD0 的 66.7%。保留连续顶壳及拼接轮廓。
- Unity 中以预览取景做保守包围盒视锥统计，包含可能被遮挡的网格。未计入剔除收益或现场实时帧时。

| 取景 | 保守可见 LOD0 tris |
| --- | ---: |
| Entrance | 119,072 |
| Auditorium | 93,548 |
| Stage | 26,424 |
| Balcony | 102,436 |
| Backstage | 51,736 |

- 预览图由实际拼装渲染。Overview 使用展示剖切。源场景及游戏场景保留实体封顶。Unity 巡游包使用 URP Lit 和独立的少量实时阴影，已在 PC URP 配置渲染核验门厅、观众厅、二楼和后台。Blender 的四档明暗通过 EEVEE 预览，Unity 版本保留同一套表面贴图和主次照明，并呈现连续的光照过渡。色阶参数与建议烘焙灯参数见 [灯光清单](LightingManifest.json)。

## 接入 Unity

导入 `Export/GrandTheater_Unity6000.unitypackage`，打开 `Assets/GrandTheater/GrandTheater_Walkthrough.unity`，点击 Play。WASD 移动，鼠标查看，左键显示射线碰撞测试点，Esc 解锁鼠标。场景包依赖 URP。当前目标工程已启用 Input System。该包用于巡游、遮挡和碰撞验证，正式接入时使用游戏控制器、武器查询掩码与烘焙照明配置进行复测。

| 包内层号 | 用途 | 正式游戏查询 |
| --- | --- | --- |
| 8 | Movement | 移动 |
| 9 | Shot | 射线。巡游胶囊通过 excludeLayers 排除 |
| 10 | Both | 移动和射线 |

Unity 包中的层号与默认层名互相独立。正式工程如果分配了其他层号，按清单重映射后同步控制器和射击掩码。巡游包导入范围为 `Assets/GrandTheater`。自行拼装时按 `TheaterLayout.json` 放置 LOD 模块，在 ModelImporter 中启用 Bake Axis Conversion，保留文件 UV1。Unity 巡游中 LOD 切换相对屏高为 0.14，LOD1 保持可见。交换用完整布局 FBX 是 LOD0。可复用模块 FBX 同时含 LOD0 与 LOD1。

[Unity 物理原始结果](Validation_Physics.txt) · [Unity 原生场景原始结果](Validation_NativeUnity.txt) · [PC URP 渲染原始结果](Validation_URP.txt)
