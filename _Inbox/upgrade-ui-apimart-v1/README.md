> SUPERSEDED: current four-color design is ../upgrade-ui-apimart-v2/README.md. White/purple variants below are archived history only.

# 升级 UI · APIMart 原始素材第一版

供应方：APIMart；模型：gpt-image-2；分辨率档位：2k。
本批替代此前内置 image_generation 生成的候选；旧图保留，不覆盖、不混作 APIMart 产物。

## 信息框
一张素材板同时提供六个配色：上排白/银、绿、蓝；下排紫、金、红。
白绿蓝紫金为本轮预备品质配色，具体品质名称和规则尚未定义，不代表已新增品质系统。
红色用于衍生配件；蓝、金亦供子弹使用。当前界面默认仍为蓝框。
配色用于边框、装饰条和细节；正文统一浅米白，无文字或配件图标烘焙。
六框使用一致构图，生成细节略有差异，后续切图阶段需统一尺寸/九宫格边界。

## 已确认的演出
参考用户图 #6、#7：同一图标从线谱上上移至框体上部，同时展开信息框；实际只有三个图标。
线谱从框体后方穿过，进出采用非线性曲线，曲线起伏灵动，停驻阅读时缓和。
小金色音符单独切图后浮动，本轮素材板不切分。
选中后框体逆向收起，选中图标曲线飞入左上角当前武器图标；未选项缩回线谱，随后向右退场。
战斗暂停，UI 动效继续。满包后将走替换流程，本轮留空。
品质系统在 UI 更新完后另行新增。

## 文件
- 01-frames-all-colors.png：六色信息框原始素材板。
- 02-gold-notes.png：六枚金色音符原始素材板。上排右侧实际也为双横梁音符，与下排中间造型接近；作为装饰候选保留，不按严格乐理谱面验收。
- 03-gold-staff.png：初版线谱，实际只生成四线，保留为未采用原图。
- 03b-gold-staff-five-lines.png：补生成五线版本，状态以回执为准。
- generation-plan.json：完整提示词和参数。
- receipts/ 与 generation-receipt.json：API 任务号、原图 URL、文件哈希等回执。
- index.html：原图预览。

原图保留纯黑背景，尚未抠图、裁切、拆分、重采样或压缩。所有文件位于 _Inbox，未导入 Assets，未改动 Unity 页面。素材已登记枢纽 tools/apimart-assets.json。
