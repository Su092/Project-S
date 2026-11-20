# 全模型赛璐璐贴图 · 待审核

范围：当前音乐主题乐器、敌人、装置、特效模型、剧院场景的全部颜色贴图。33 张独立原图对应 38 个材质引用。

打开 review.html 查看全部原图与生成图，支持类别筛选、搜索、并排对照和透明叠加检查。review-status.json 与 review.csv 记录逐张状态；generated-overview-1.jpg 至 generated-overview-3.jpg 为缩略总览。

统一使用 APIMart gpt-image-2；每个任务使用对应原贴图作为参考，输出 2048×2048。提示词要求保留 UV 岛、色板分区、饰边与平铺布局，简化细碎纹理和连续明暗，保持既有材质配色。

原图在 Assets 中保持不变。新图只在本待审核目录中，尚未处理、入库或绑定。法线、ORM、发光等数据贴图不作绘画式重绘。

图生图不能保证逐像素 UV 对齐。审核时需特别检查小 UV 岛、色板边界、平铺接缝及透明覆盖；正式入库前需要贴回模型检查。

T_S08_BassWave 与 T_S21_VentGust 的首版将透明轮廓烘焙进 RGB，已通过 APIMart 分别重绘 RGB 色场。审核页使用 fx-color-revision/generated/ 下的修正版，原始首版仍保留追溯。批准入库后，须恢复原贴图的透明通道。其他带透明通道的贴图也须检查原透明覆盖，不能直接以生成图覆盖。

文件索引：
- source-inventory.json：原文件、尺寸、透明范围、SHA256 与材质对应。
- generation-plan.json：33 张主任务的提示词及参考文件。
- receipts/、generation-receipt.json：主任务回执。
- fx-color-revision/：2 张特效 RGB 修正版的计划、输出和回执。
- review-overrides.json：审核页最终候选文件映射。

所有结果待人工审核，尚未贴模验证。保持后续素材先审核、再处理入库的约定。未知提交状态下禁止重复生成。
