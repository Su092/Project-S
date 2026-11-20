# 过压罐系列 v002 修订简报

用途：PC FPS Roguelite / Unity URP，风格化工业科幻。

| 资产 | 中立尺寸（宽×深×高 m）| LOD 上限 | 纹理 | 骨骼上限 |
|---|---|---|---|---|
| PressureRunner | 1.05×1.10×1.55 | 3200 / 1600 / 700 | 1024，共用制造面图集 | 28 |
| PressureRunner_Elite_TriplePressure | 1.45×1.40×1.85 | 4500 / 2200 / 1000 | 复用普通版 | 36 |

三足支撑面中心根轴心。两版保留相同骨骼层级并新增三个同名压力面板骨骼；刚性单骨骼蒙皮。

本批修订：分离承压内胆与三片上部耐火外壳；过载动作按顺序张开面板、上顶阀杆、展开支腿。普通版增加一个仪表，精英版三个不同方向的仪表；不增加攻击方式。

执行链：blender-director → hard-surface / creature-artist → stylized-style → uv-workflow / texture-workflow → rigging / animation → lod-pipeline / collision-proxy → unity-export → qa-review。

验证顺序：各向剪影预览 → 三 LOD 面数、权重、拓扑检查 → 模型与五个动画 FBX 独立回导 → Unity 验证工程实际重新导入 → 更新双照明、动作、同尺度图 → 交付副本路径审计。

旧稿保留于 _RevisionBackups/pre_pressure_v002；收件目录 Review_v001 保持为上一版评审快照。当前仍是制作评审稿，最终纹理、全帧穿插及实机平台占用验收仍单列待办。
