# 音律敌人模型交付

共 10 名敌人。每个链接对应一个可独立导入的外观 FBX，内含三档 LOD。修订版标明采用的概念改模源文件。

| 编号 | 敌人 | 模型修订版 | 外观 FBX | 动作数 | 预览与验收 |
|---|---|---|---|---:|---|
| 02 | 战鼓甲虫 | v002 | [基础款](S02_DrumBeetle/Exports/S02_DrumBeetle_Model.fbx) | 6 | [预览](S02_DrumBeetle/Preview/ConceptReview_Hero.png) · [验收](S02_DrumBeetle/validation.md) |
| 03 | 铜管猎犬 | v002 | [基础款](S03_HornHound/Exports/S03_HornHound_Model.fbx) | 7 | [预览](S03_HornHound/Preview/ConceptReview_Hero.png) · [验收](S03_HornHound/validation.md) |
| 07 | 失谱幽灵 | v003 | [基础款](S07_LostScoreGhost/Exports/S07_LostScoreGhost_Model.fbx) | 6 | [预览](S07_LostScoreGhost/Preview/ConceptReview_Hero.png) · [验收](S07_LostScoreGhost/validation.md) |
| 08 | 节拍守卫 | v002 | [基础款](S08_MetronomeGuard/Exports/S08_MetronomeGuard_Model.fbx) · [Rehearsal](S08_MetronomeGuard/Exports/S08_MetronomeGuard_Rehearsal_Model.fbx) · [Ceremonial](S08_MetronomeGuard/Exports/S08_MetronomeGuard_Ceremonial_Model.fbx) | 11 | [预览](S08_MetronomeGuard/Preview/ConceptReview_Hero.png) · [验收](S08_MetronomeGuard/validation.md) |
| 11 | 铜钹蛾 | v002 | [基础款](S11_CymbalMoth/Exports/S11_CymbalMoth_Model.fbx) | 8 | [预览](S11_CymbalMoth/Preview/ConceptReview_Hero.png) · [验收](S11_CymbalMoth/validation.md) |
| 14 | 木琴棘龙 | v002 | [基础款](S14_XylophoneStegosaur/Exports/S14_XylophoneStegosaur_Model.fbx) · [Redwood](S14_XylophoneStegosaur/Exports/S14_XylophoneStegosaur_Redwood_Model.fbx) · [Ebony](S14_XylophoneStegosaur/Exports/S14_XylophoneStegosaur_Ebony_Model.fbx) | 23 | [预览](S14_XylophoneStegosaur/Preview/ConceptReview_Hero.png) · [验收](S14_XylophoneStegosaur/validation.md) |
| 16 | 八音盒舞姬 | v003 | [基础款](S16_MusicBoxBallerina/Exports/S16_MusicBoxBallerina_Model.fbx) | 14 | [预览](S16_MusicBoxBallerina/Preview/ConceptReview_Hero.png) · [验收](S16_MusicBoxBallerina/validation.md) |
| 19 | 响板飞鱼 | v002 | [基础款](S19_CastanetFish/Exports/S19_CastanetFish_Model.fbx) | 6 | [预览](S19_CastanetFish/Preview/ConceptReview_Hero.png) · [验收](S19_CastanetFish/validation.md) |
| 21 | 风笛野猪 | v003 | [基础款](S21_BagpipeBoar/Exports/S21_BagpipeBoar_Model.fbx) · [Parade](S21_BagpipeBoar/Exports/S21_BagpipeBoar_Parade_Model.fbx) · [Ceremony](S21_BagpipeBoar/Exports/S21_BagpipeBoar_Ceremony_Model.fbx) | 16 | [预览](S21_BagpipeBoar/Preview/ConceptReview_Hero.png) · [验收](S21_BagpipeBoar/validation.md) |
| 29 | 游吟指挥家 | v003 | [基础款](S29_WanderingConductor/Exports/S29_WanderingConductor_Model.fbx) | 16 | [预览](S29_WanderingConductor/Preview/ConceptReview_Hero.png) · [验收](S29_WanderingConductor/validation.md) |

角色主体共 16 个外观模型、113 个动作和 10 个碰撞 FBX。另含 20 个角色衍生资源 FBX，以及 8 个[技能补充资源 FBX](SkillSupplements/README.md)。合计 167 个 FBX。

已在 Unity 6000.6.4f1 独立工程验证导入。每个角色的 manifest.json 记录概念源文件、SHA-256、LOD 面数、动作和碰撞信息。使用时保留贴图目录，按各角色 README.md 配置 Generic Avatar 和 LODGroup。目标游戏场景的材质表现与战斗衔接仍待联调。
