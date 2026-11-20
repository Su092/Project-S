# 全模型赛璐璐贴图 · 已替换

用户已批准继续：先备份现有贴图渲染，再替换并生成同视角对比。

打开 **comparison/index.html** 查看 Unity 实际渲染前后对比，支持滑杆、切换游戏视角/剧院全景，下方有 35 个模型和特效的前后渲染。
- comparison/before/game.png：替换前原始游戏截图（1920×1080）。
- comparison/after/game.png：替换后同视角游戏截图（1920×1080）。
- comparison/game-comparison.jpg：前后静态对比与同一区域细节。
- comparison/before/theater.png、comparison/after/theater.png：剧院全景。
- comparison/before/models/、comparison/after/models/：相同预览灯光与机位的模型对比。
- comparison/render-verification.json：相机、FOV、游戏时间、分辨率一致性及画面差异记录。
- comparison/unity-import-check.log：33 张纹理、38 个材质的 Unity 导入检查。

33 张颜色贴图已在原路径替换。正式图保留各自原分辨率，透明通道逐像素取自原图，原 meta/GUID、材质、法线/ORM/发光等数据图不变。
备份位于 backup-original/，含原 PNG 与 .meta；import-plan-approved.json 与 import-completed.json 保存路径和校验值。可用 python tools/import_cel_textures.py validate 复核。

review.html 仍提供 APIMart 原始生成图与旧贴图备份的对照，正式入库图与生成原图的差异仅为尺寸适配和原透明通道恢复。
generated/ 保留 33 张原始生成结果；fx-color-revision/ 保留两张特效 RGB 修正版，审核与入库均采用修正版。所有新图均由对应旧图作为参考经 APIMart 生成。

模型预览是固定姿态的材质检查，游戏视角与全景使用实际游戏场景；渲染对比没有进行生成式修图。

补充模型预览已修正高分屏取景裁切：原图组使用备份贴图、现图组使用已替换贴图，在相同预览环境下补拍；替换前后的游戏与剧院场景截图未改动。最初模型截图保留在各自 models-initial/ 目录。
