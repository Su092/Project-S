# _Inbox — 未处理资产暂存区

**本目录入库**（换机 `git pull` 就能拿到原料，不用重新生图）。放进来的东西是**原料**，不是交付件。

> 只有运行产物（`Shots/`、中间渲染）才 gitignore。

## 为什么单独放这里

工程里的 `Assets/` 只应该有「能直接进引擎」的东西。AI 原图、参考图、预览渲染这些原料混在里面会有三个问题：

1. Unity 会给每张图生成 `.meta` 和无用导入，拖慢首次打开
2. 分不清「这张抠过图没有」，流水线容易读错输入
3. 仓库里塞几十 MB 可再生的大图

## 现有子目录（S 工程）

| 目录 | 内容 | 下游 |
|------|------|------|
| `ui-ai-raw/` | APIMart 原始下载，未抠图未规范化 | `tools/preprocess_ui_assets.py` 的输入 |
| `ui-ai-src/` | 前期处理产物（已抠图 / 尺寸规范） | `tools/gen_ui_assets_ai.py` 的输入 |
| `art-refs/` | 3D 建模参考图（1k，仅对照用） | 人眼 + `blender_s_build_assets.py` |
| `art-preview/` | 模型预览渲染（`blender_s_preview.py` 产物） | 人眼核对 |

UI 流水线：`_Inbox/ui-ai-raw/` → `_Inbox/ui-ai-src/` → `Assets/Resources/UI/`

## 纪律

- **只有最终进引擎的产物才落到 `Assets/`**；中间态一律留在本目录。
- 本目录已入库：删掉某项就等于从所有机器删除，**不要**随手 `git clean`。
- 需要长期留存的参考图放本目录；要写进文档的只是**结论**（表格/文字），大图不进枢纽 `docs/`。
- 新建游戏时同样在工程根建 `_Inbox/`（含本 README）；`.gitignore` **不要**加 `/_Inbox/`。
