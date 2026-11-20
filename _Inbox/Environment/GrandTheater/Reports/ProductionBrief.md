# 鸣奏大剧院 · Production Brief

PC 第一人称生存构筑室内地图。目标 Unity 6000.6.4f1 / URP 17.6.0。工程内独立工作目录 `E:/S/_Inbox/Environment/GrandTheater`。第二机位从二层回望与从侧台回看同等重要。

- 64×72m 轴线外轮廓。统一米制，交付坐标 +Y 向上、+Z 朝舞台。一层 Y=0，舞台 Y=1.2，二层 Y=6，主厅连续桶拱内侧最高 Y=18。
- 平面先行。两处一楼环路、二楼服务连桥、两边各两处双跑楼梯。8m 侧翼遇楼梯时，主线局部转到其内侧约 6m 宽的毗邻通道。各段贯通。四处楼梯用 20+20 级、3m 净宽和 2.5m 平台。主舞台中部至少 18×10m。
- 结构主网格 4m，局部 2m、1m 及 0.25m 定位。地板顶角、墙底角、道具底中心，非矩形单独记录。
- 模块计划 55–75 种，材质集 8 套以内，LOD0 全量实例约 45 万面内，普通构件 LOD1 目标 40–60%。贴图 1024 PNG，4m 一轮。UV0 平铺／Trim，UV1 独立光照岛。
- 按可见模块生成匹配代理。游戏移动用 Movement + Both，射击查询用 Shot + Both。有孔构件射击保留空隙，楼梯的平滑坡与逐级代理分离。
- 主调象牙白、午夜蓝、酒红、温暖木、哑金和灰石。管风琴、台口框、吊灯分件组装。无动画。
- 流程为 blender-director → environment-artist → anime-style → scene-assembly → UV / materials → lighting → optimization / LOD → collision → Unity export → QA。先检查灰盒通行。导出前用 1m 坐标标尺和不对称几何验证导入轴向。
