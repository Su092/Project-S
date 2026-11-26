# S 项目资产原料

Unity 使用的正式资产位于 `Assets/_Game/Res/` 和 `Assets/_Game/Resources/`。本目录保存重建这些资产所需的原料、源工程和交付记录。导入器按相对路径读取模型、贴图和清单。

| 内容 | 位置与用途 |
|---|---|
| 开始场景与选角 | `backstage/` 保存 Blender 工程和镜中角色的二维原图。 |
| 乐器与音符 | `weapons/MusicWeapons_v001/` 是六把起始乐器的导入源。`orchestra-notes/` 保存新增音符、乐器及规划中配件的 APIMart 图集。 |
| 敌人 | `chr/S_Music_Enemies/Reexport_20261119_135947/` 是音乐敌人的重导入交付。`Imported_20261119/` 中保留可编辑的 Blender 源文件和贴图。铸造厂敌人导入器读取 `chr/S_Foundry_Enemies/Review_v002/`。 |
| 场景 | `Environment/` 保存场景源工程、布局、导出件和制作记录。大剧院地图构建器从 `GrandTheater/Layout/` 读取布局与标记。 |
| 赛璐璐贴图 | `cel-texture-review-v1/` 保存采用的重绘原图、33 张替换前贴图及对应的 Unity meta、材质映射和校验清单。 |
| HUD、谱架与奖励界面 | `ui-ai-raw/`、`ui-ai-src/`、各 HUD 和曲谱目录保存分层原图、用户修订和加工输入。升级卡框取自 APIMart v2，谱线取自 v7，音符取自 v8。早期回执也用于追溯参考图。 |

`Shots/` 用来输出新一轮游戏截图。历史预览、逐帧动作图及重复加工件已经清理。重新出图时，沿用各自的 Blender、APIMart 和 Unity 工具即可。
