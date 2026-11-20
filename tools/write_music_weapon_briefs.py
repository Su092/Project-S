"""Create self-contained weapon briefs from the local Blender skill templates."""
from pathlib import Path
import json,sys,csv
hub=Path(sys.argv[1]);root=Path(__file__).resolve().parents[1]
dest=hub/"Project/S/docs/weapon-modeling-prompts";dest.mkdir(parents=True,exist_ok=True)
D=[
dict(id="SW01_Violin",name="小提琴",gun="rifle",size="琴体 X 0.24 × Y 0.10 × Z 0.60m；独立弓长 0.65m、宽 0.025m；琴颈沿 +Z、演奏面朝 +Y。",budget=(12000,6000,2500),fire="FireLoop",duration=.70,
shape="双腰暖木琴体、两道 f 孔、四弦、完整卷首、深蓝下巴托与独立琴弓。",
parts=[("Body","厚面板、侧板、背板、金色包边与有内壁的 f 孔","body"),("Neck","乌木指板、琴颈、卷首","body"),("Strings","4 根弦、琴桥、尾板，所有弦端有连接","body"),("Bow","完整弓杆、弓毛、弓尖和弓根；不可焊死在琴上","bow"),("ScoreCartridge","可抽取象牙乐谱盒与有内壁的插槽","score_cartridge"),("Pegs","4 个调音轴，其中一个活动","tuning_peg")],
rig="root → body；body → bow、score_cartridge、tuning_peg。",
grip="左手托琴颈，右手抓弓根；右手挂点随 bow。输出点在琴桥前上方，不随弓横摆。",
action="琴弓沿弦面连续往复，主要反馈来自运弓；琴体共鸣位移 ≤5mm。弓毛不能穿进面板，不把换弓次数与发射次数强制绑定。",
reload="0–18%：移弓；18–42%：沿实际插槽抽出谱盒；42–62%：维护停留；62–82%：插回锁定；82–100%：轻转调音轴并回到演奏位。",
checks="完整卷首、琴桥弦路和两道 f 孔；琴背与谱盒插槽；弓杆/弓毛连接。"),
dict(id="SW02_Kalimba",name="拇指琴",gun="smg",size="X 0.25 × Y 0.055 × Z 0.19m，不含自由垂落腕带；演奏面朝 +Y，音舌自由端靠玩家（-Z）。",budget=(10000,5000,2000),fire="FireLoop",duration=.30,
shape="浅厚度圆角木共鸣箱、中央音孔、左右两手拨片分区、金属音舌、压桥与侧握边。",
parts=[("Body","封闭胡桃木箱体、圆音孔及内部暗腔","body"),("Tines","17 根音舌：左8/中1/右8，中央长两侧短，自由端朝玩家","tine_01..17"),("Bridge","压住音舌固定端的黄铜桥，不能让音舌悬浮","body"),("TuningCam","中央调律偏心轮、轴和机械止挡","tuning_cam"),("SideGrips","双侧凹握边，留出手指厚度","body"),("WristStraps","左右腕带、扣环和真实锚点","strap_L / strap_R")],
rig="root → body；body → tuning_cam、tine_01..tine_17、strap_L、strap_R。单根音舌根部固定，末端微弯，总骨骼建议 ≤32。",
grip="双手托侧边与背板，两只拇指接触音舌自由端；Socket_Thumb_L/R 在演奏区。输出点在远离玩家的桥上方朝 +Z。",
action="双拇指交替拨片，左右音舌分组微振动，自由端振幅 1–2mm；不能像门板一样翻动整根音舌。",
reload="音舌停止；中央调律轮转至维护角并回位；末段试音，所有音舌回到静止位置。",
checks="音舌总数按文字固定17，生成图密集细条数量不是测量依据；固定端在压桥下、自由端可被拇指接触；背板、腕带锚点齐全。"),
dict(id="SW03_FrameDrum",name="手鼓",gun="shotgun",size="鼓面直径 0.34m，鼓圈厚 0.085m，演奏面朝 +Y；背面握柄可向 -Y 额外伸出 0.055m。",budget=(10000,5000,1800),fire="Fire",duration=.60,
shape="单个圆形鼓面、两道同心共鸣纹、酒红鼓圈、金色张力件、背面握柄与前臂带；不是盾牌或双鼓。",
parts=[("Rim","有厚度的封闭鼓圈、包边、张力卡扣","body"),("Membrane","有厚度的鼓皮，外缘固定、中央可形变","membrane"),("BackGrip","背面横跨握柄，两端固定到鼓圈","body"),("ForearmStrap","前臂带与铆接点，保留手指插入空间","strap"),("TuningRatchet","调律棘轮、拨柄和轴","tuning_lever")],
rig="root → body；body → membrane、strap、tuning_lever。鼓皮边缘权重到 body，中心渐变到 membrane，不能整张鼓皮脱离鼓圈。",
grip="左手抓背面握柄，右手掌击近玩家一侧鼓面；Socket_Palm_R 标记接触；Socket_Output 在远侧鼓圈上方朝 +Z，不按朝天的鼓面法线发射。",
action="片段开头表现击打接触，中央内凹3–5mm，0.12s 内回弹、随后衰减；一次击打读取两个子弹配件，不要求连续打两下。",
reload="手掌离开鼓皮，中段拨动张力棘轮，鼓皮轻微张紧，末段检查后回到抬掌姿态；不拆掉鼓皮充当弹匣。",
checks="鼓皮到圈边连续、握柄两端有固定、前臂带可用。背面不得封成一个让手穿过去的实心圆盘。"),
dict(id="SW04_Cello",name="大提琴",gun="sniper",size="便携琴体 X 0.36 × Y 0.18 × Z 0.92m；弓长 0.72m；尾针收起；琴颈沿 +Z、演奏面朝 +Y。",budget=(16000,8000,3000),fire="Fire",duration=.85,
shape="宽深下琴腹、长乌木颈、四根重弦、完整卷首、空心黄铜瞄准环、肩部支撑带和折叠尾针；不是简单放大小提琴。",
parts=[("Body","宽下琴腹与厚侧板、背板、木纹漆面","body"),("Neck","长指板、卷首和4个调音轴","body"),("Strings","4根弦、较高琴桥与尾板","body"),("Bow","独立完整弓杆与弓毛","bow"),("AlignmentRing","空心校音/瞄准环及实心支座","aim_ring"),("Endpin","带铰点与锁扣的折叠尾针","endpin"),("Harness","肩带锚点与低位支撑托","strap"),("TuningPeg","一个活动调音轴","tuning_peg")],
rig="root → body；body → bow、aim_ring、endpin、strap、tuning_peg。瞄准环不随弓摆动。",
grip="左手托琴颈、右手执弓；支撑带托琴腹。Socket_Aim 位于透空瞄准环中心，+Z 对准 Socket_Output。",
action="单次有重量的运弓，发射反馈从开头发生，随后恢复；不添加蓄力前摇。瞄准环稳定，琴弓不长期遮住环中心。",
reload="弓移开，转动调音轴，检查尾部锁扣并回到演奏位；尾针只在 Inspect 展示折叠，不在换弹时扎地。",
checks="完整长颈与卷首、琴腹厚度、肩带锚点；瞄准环真正透空。原概念板的 charge 文案不是现有机制，严禁新增按住蓄力。"),
dict(id="SW05_Harp",name="竖琴",gun="beam",size="X 0.34 × Y 0.44 × Z 0.12m；框架位于 XY 平面，演奏面朝玩家（-Z），根节点发射方向 +Z。",budget=(14000,7000,2500),fire="FireLoop",duration=.30,
shape="闭合金色曲线三角框、午夜蓝镶边、7根弦、下共鸣箱、上调律梳和前臂托；不是弓箭或十字弓。",
parts=[("Frame","立柱、颈梁、下共鸣箱真实连接成闭合框","body"),("Strings","严格7根弦，各有上部调音钉和下部锚点","string_01..07"),("TuningComb","黄铜调律梳和旋转轴","tuning_comb"),("Inlays","7个紫色低亮度镶嵌件，不能悬空","body"),("ArmCradle","下缘软托、腕带和扣环","strap")],
rig="root → body；body → tuning_comb、string_01..string_07、strap。弦端固定，中段小幅位移；不得缩放整个框架模拟弹奏。",
grip="左前臂支撑下缘托，右手扫拨琴弦；Socket_Finger_R 为拨弦接触；输出挂点位于上梁外侧朝 +Z。",
action="连续扫拨与琴弦分组微振动；.30s 对应5个基础发射间隔。闪烁交由引擎材质参数，FBX 不烘焙共享材质发光。",
reload="停止扫拨，调律梳转入维护位置再回位；末段试音，琴弦与框架全程连接，不拆开框架。",
checks="必须能逐根沿7根弦追踪上下锚点；检查共鸣箱厚度和前臂托。图上的连续光流只是表现提案，不能把现有高射速弹丸改成按帧伤害。"),
dict(id="SW06_Accordion",name="手风琴",gun="launcher",size="半展开 X 0.50 × Y 0.27 × Z 0.19m；收合宽0.40m、最大展开宽0.64m；沿X拉合，控制面朝玩家（-Z）。",budget=(18000,9000,3500),fire="Fire",duration=.70,
shape="两个完整木质端箱、右键盘、左低音按钮、连续酒红风箱、手带与打孔乐谱卷；没有吹嘴或圆号喇叭口。",
parts=[("LeftHousing","左端箱、8个低音按钮、谱卷仓","housing_L"),("RightHousing","右端箱、12白键+8黑键及真实键盘转轴","housing_R"),("Bellows","10组完整折褶，顶底侧壁均连接到端箱","bellows_01..11"),("PianoKeys","首版4组活动键，其余键固定","key_01..04"),("BassButtons","2组活动按钮，小幅按压","bass_01 / bass_02"),("ScoreRoll","可抽取谱卷和有内壁的仓","score_roll"),("HandStraps","跟随各自端箱的左右手带与下承重肩带","strap_L / strap_R"),("TuningLatch","上缘调律锁扣","tuning_latch")],
rig="root → housing_L、housing_R、bellows_01..11；左箱下挂 bass_01/02、score_roll、strap_L；右箱下挂 key_01..04、strap_R、tuning_latch。风箱用折页旋转与平移，禁负缩放或单张胶皮拉伸。",
grip="左右握持挂点各跟随端箱，手指目标对应低音按钮/键盘；Socket_Output 独立挂在 root，位于风箱前上方、恒定朝 +Z，不随单边端箱摆动。",
action="半展开压合至0.40m，再回到半展开；前段压合体现一次重音，按键同步微动；各褶均匀开合、端箱不碰撞。",
reload="0–18%：停止拉合；18–42%：谱卷沿插槽抽出；42–62%：停留；62–82%：推入锁定；82–100%：短行程拉合试音并复位。",
checks="收合与最大展开时顶底侧壁都不能断开、穿插或凭空消失。12指配件槽，不是每次发射12枚弹；单次动作只承载现有一次 launcher 发射。")
]
cfg=root/"Assets/_Game/Res/Cfg/Gun.tsv"
lines=cfg.read_text(encoding="utf-8-sig").splitlines()
guns={r["id"]:r for r in csv.DictReader([lines[0]]+lines[3:],delimiter="\t") if r.get("id")}
fence=chr(96)*3
for d in D:
    aid=d["id"];g=guns[d["gun"]];p=dest/aid;p.mkdir(exist_ok=True)
    clips=[("Idle",2,True,"低幅待机，root固定"),("Move",1,True,"局部轻惯性，可由引擎总摆动替代"),
           ("Equip",.15,False,"展开至待机，换枪后半段"),("Unequip",.15,False,"局部件收纳，换枪前半段"),
           (d["fire"],d["duration"],d["fire"].endswith("Loop"),d["action"]),
           ("FireRecover",.15,False,"快速停止演奏后回Idle，不额外发弹"),
           ("Reload",float(g["reloadTime"]),False,d["reload"]),("Inspect",3,False,"展示可动件/背面结构，root固定")]
    if d["gun"]=="sniper":clips += [("AimIn",.2,False,"局部件稳定进入瞄准，不烘焙镜头FOV"),("AimIdle",1,True,"空心环稳定、弓不遮目标"),("AimOut",.2,False,"回普通待机")]
    part_rows="\n".join(f"| SM_{aid}_{n}_LOD0 | {desc} | {bone} |" for n,desc,bone in d["parts"])
    anim_rows="\n".join(f"| AN_{aid}_{n} | {t:.2f}s / 0–{round(t*60)} | {'是' if loop else '否'} | {desc} |" for n,t,loop,desc in clips)
    md=f"""# {aid} · {d['name']}建模任务书

> 将本文件与同目录两张 PNG 一起交给建模工具。本文件是待执行的制作任务，不代表已经完成模型。尺寸为本次建议的首版制作规格，不是从 AI 图片测出的现实尺寸；参考图负责外观，结构数量以文字为准。

## Production Brief

- Type：第一人称 Hero Prop，音律战斗乐器。
- Target：PC / Unity 6000.6.4f1 / URP 17.6，Generic 骨架。
- Style：02 复古动漫赛璐璐，清晰轮廓、克制装饰、暖木/象牙/金/午夜蓝/酒红。
- Poly Budget：LOD0 ≤ {d['budget'][0]} tris，LOD1 ≤ {d['budget'][1]}，LOD2 ≤ {d['budget'][2]}；总数含弓、带与活动件，不含检查用假手。
- Texture Budget：一组 2048×2048 BC/ORM/E，必要时加 N；最多2个材质槽。花纹走贴图，轮廓与可动件保留几何。
- Export：模型FBX + 单动作FBX + PNG + manifest.json + 可继续编辑的 .blend。
- Pivot：握持总根 root；以便携演奏位组装，不把原点自动移到地板。
- Animation：原地、机械/局部柔性动作，60fps；模型和动作的骨架路径必须完全相同。
- Scope：只制作 {aid} 单一款，不增加皮肤、能力或全身角色。

### Active Skills / 模板来源

采用用户指定的 <BLENDER_REPO>/.claude/skills/ 下 blender-director 的 Production Brief 与 Reference Analysis 模板。后续建模依次使用：
blender-director → prop-artist + anime-style → uv-workflow / materials → asset-optimization / lod-pipeline → rigging / animation → collision-proxy / unity-export / qa-review。
参考规范为 references/asset-pipeline.md、reference-analysis-template.md、naming-conventions.md、polycount-budgets.md。下文 MCP 步骤是交给建模工具的执行计划，本轮没有运行 Blender 建模。

## 同目录建模参考图

[完整造型](reference_01_hero.png) · [结构多视图](reference_02_structure.png)

![完整造型](reference_01_hero.png)

![结构多视图](reference_02_structure.png)

两张为 API 重新生成的干净背景参考；第二张引用第一张结果链式生成。不是严格标定的三视工程图，不能从像素直接推算米数，不能将整图当作模型贴图。图中视角间的遮挡差异、音舌/弦数量偏差、臆造细件按本任务书统一，并记入 validation.md。

## Reference Analysis

### 1. 主体、尺寸与轴向

- 游戏 ID：{d['gun']}；资产 ID：{aid}，二者映射写入 manifest。
- 主轮廓：{d['shape']}
- 首版尺寸（Unity绑定姿态 X宽/Y高/Z深）：{d['size']}
- 场景单位米；放置1.8m人体量尺与0.10m单位块，量尺不导出。
- Unity最终根 +Y向上、+Z为瞄准/发射方向、+X向右。演奏面方向按上文定义，不等同于发射方向。
- Blender +Z向上、-Y对应目标前方；导出后以实际Unity回读验证轴向，不能只凭参数猜测。

### 2. 轮廓层次与必须保留的特征

先主形体/厚度，再连接与活动缝，最后金边、浮雕和花纹。
{d['checks']}

### 3. 参考相机与第一人称检查

建立 CAM_{aid}_Hero 与 EMPTY_{aid}_CamTarget，先匹配主图三分之四机位再做细节；长焦/正交检查比例，不用广角掩盖结构错误。另建 Front / Back / Side 正交相机和 FP 相机。

FP初始相机位于原点面向+Z，将完整乐器置于画面下方：横向占屏约35–65%，不通过破坏米制比例来强行塞入画面。中心宽20%×高20%为主要瞄准留白区，大块实体不得持续覆盖。常规FOV从60°开始，同时检查本枪瞄准配置 {g['zoom']}°、16:9与21:9。相机参数不可烘焙进武器骨架。

仅用简单假手/前臂检查握持、手指接触和行程；归 PreviewOnly，不随每把武器导出重复的人物手臂。正式玩家手部资产后续共用；当前交付必须给出可驱动手部IK的挂点。

### 4. 灯光、颜色与材质

主光柔和左上、弱补光看清凹槽；浅灰背景，避免高光淹没弦和金属拨片。
以下为风格建议色，不声称经过图像精确取样：

| 用途 | 建议色 |
|---|---|
| 木色 | #914C35 |
| 象牙白 | #EAD9B5 |
| 暖金 | #C39A50 |
| 午夜蓝 | #263349 |
| 酒红 | #792C3D |
| 共鸣青 | #6ED3D5 |
| 暗线 | #302A35 |

BC不烘焙舞台灯、攻击光和外描边；木/漆/金属/布用粗糙度区分。发光仅为E遮罩区域，不对共享结构材质打发光关键帧。不把 Blender Shader-to-RGB 网络误当成可运行 Unity 材质。以现有赛璐璐着色器观感复核。

### 5. 功能分件

| 网格名称示例 | 几何与功能 | 绑定骨骼 |
|---|---|---|
{part_rows}

固定件可按材质合并降低draw call，活动件保持独立驱动。LOD1/2保持相同部件前缀、骨架路径，替换末尾LOD编号。合并必须保留蒙皮权重与绑定矩阵。

## 骨架、手部与输出挂点

- 骨架 ARM_{aid}，单根 root；应用对象变换，绑定姿态骨骼scale=(1,1,1)，不允许负缩放。
- 层级：{d['rig']}
- 握持/发射关系：{d['grip']}
- 必备 Socket_Grip_L、Socket_Grip_R、Socket_Output、Socket_Aim、Socket_Retune、Socket_Stow；有拇指/手掌/拨弦需求再加 Socket_Thumb_L/R、Socket_Palm_R 或 Socket_Finger_R。
- Socket_Output与Socket_Aim的本地+Z朝发射方向、+Y朝上；非专用瞄准款也须提供稳定对准点。可动手部挂点随对应部件运动。
- 骨骼建议≤64含挂点；每顶点最多4权重并归一。刚性木/金属件使用明确单骨权重，不能全模型自动权重后直接交付。
- 约束/驱动器可保留在源.blend编辑，FBX必须烘焙为实际位置/旋转曲线；非变形挂点不得被导出器过滤。
- 不烘焙相机震动或世界根位移；局部演奏反馈与武器总后座分开，避免叠加现有 WeaponMotion。
- 当前 WeaponMotion 是通用枪械表现，不能自动识别本包动画。后续引擎集成要单独绑定Animator和手部IK，本任务不能声称已自动接入。

## 现有玩法映射与动作

保留现有配置：{g['slotCount']}槽、容量{g['magSize']}、基础击发间隔{g['fireInterval']}s、每次读取{g['castCount']}个子弹配件、调律/换弹{g['reloadTime']}s。仅改变视觉包装，不新增蓄力、节奏输入或伤害事件。

动作60fps；表内末帧为长度计算终点，循环首末姿态一致。秒数为权威，Unity回读误差≤1/60s。root位置/旋转全程固定，无骨骼缩放曲线。

| Action / 文件前缀 | 时长 / 帧范围 | 循环 | 说明 |
|---|---|---|---|
{anim_rows}

- 高射速用持续演奏循环承载外观；弹丸时间由GunRuntime驱动，不用每发重播一个长动作。停止射击平滑进入FireRecover。
- Reload按0..1可定位，支持真实换弹时长、暂停、打断；结束部件复位。空仓不要求额外装填规则。
- normalizedMarkers只描述视觉接触/抽出/插回节点；不得嵌入直接发弹或扣血的AnimationEvent。
- Equip/Unequip各0.15s，总计对应现有0.3s换枪时段；可视轨迹留给引擎总根，FBX只驱动内部件。

## 集合、UV、清理与碰撞

建立 COL_{aid}_Reference / Blockout / Geo_LOD0 / Geo_LOD1 / Geo_LOD2 / Rig / Collision / Preview / Export。
对象分别用 SM_/ARM_/AN_/MAT_/T_/CAM_/LGT_ 前缀，ASCII命名、下划线、每名<64字符。

UV 0–1、密度尽量一致；近景握持面、按键/弦端优先分配。2048图集岛间距≥8px，不镜像文字或非对称装饰。
BC=sRGB，ORM=线性（R=AO、G=Roughness、B=Metallic），E=线性遮罩；可选N采用OpenGL +Y，注明Unity NormalMap导入类型。
导出前固定三角化，清掉孤立点、重复面和退化三角；凹腔/透孔保留实际厚壁，琴弦、音舌和风箱不能用无厚度相交面冒充完整部件。分别测试各LOD法线、UV、材质与权重。

第一人称武器不参与玩家/子弹实体阻挡；仅交付简化拾取代理 SM_{aid}_Pickup_COL，独立Collision集合和FBX，作为可选入库项。不能把渲染网格直接当高面数碰撞体。

## 命名、目录与导出

工作根建议 <BLENDER_REPO>/MusicWeapons/{aid}/。
交付根 <GAME:S>/_Inbox/weapons/MusicWeapons_v001/{aid}/。不要直接写Assets，不把整个交付目录拖入Unity。

{fence}text
{aid}/
  Source/{aid}.blend
  Exports/{aid}_Model.fbx
  Animations/AN_{aid}_Idle.fbx
  Animations/AN_{aid}_<Action>.fbx
  Textures/T_{aid}_BC.png
  Textures/T_{aid}_ORM.png
  Textures/T_{aid}_E.png
  Textures/T_{aid}_N.png          # 仅需要时
  Collision/{aid}_Collision.fbx
  Preview/Front.png
  Preview/Back.png
  Preview/Side.png
  Preview/Hero.png
  Preview/FP_Idle.png
  Preview/FP_Fire.png
  Preview/FP_Reload.png
  Preview/AnimationReview.mp4
  manifest.json
  validation.md
{fence}

模型FBX含同一骨架、三档LOD网格、公共挂点，不含动作、灯光、相机、量尺或检查假手。
每个动画FBX只含相同骨架和一个指定动作；不额外插根，不更改骨架/挂点路径，不混入其他NLA动作。

FBX参数基线：Scale=1；单位米；Forward=-Z、Up=Y；Apply Unit开；Add Leaf Bones关；Bake Space Transform关；仅选定对象；单动作导出。非变形挂点必须显式导出，不能被Only Deform Bones丢掉。以Unity回读结果为准，保留FBX导入坐标转换，不在运行时凭猜测叠90°旋转。

manifest.json至少包含：
- schemaVersion、assetId={aid}、gunId={d['gun']}、revision、units=m、轴向和实际导出参数；
- 主体/附件尺寸、rest及活动极限bbox、单位块回读记录；
- 各LOD名称/三角数/材质、骨骼路径/绑定矩阵、全部挂点的父骨骼与变换；
- 每动作state/file/fps=60/duration/loop/normalizedMarkers/rootMotion=false；
- 贴图分辨率/色彩空间/通道；每个必需输入文件的相对路径、类型、SHA-256；
- 碰撞标记optional；Source、Preview、报告不进入引擎输入白名单；
- validationStatus逐项pass/fail/degraded与未决问题；没验证不能写pass。

后续武器专用入库清单将资源分类至Model/Animation/Material/Texture/Prefab，保留GUID；不可复用敌人资产ID、不可依赖外部建模目录运行。

## Pipeline Phases / MCP Execution Plan

1. 输出并补全本Brief与Reference Analysis；已有场景先快照，禁止覆盖无关资产。
2. 检查Blender MCP，创建上述COL_、米制单位、量尺与参考相机。
3. 白模主形 → 对照相机 → FP握持与中心留白；通过后再补连接/活动缝。
4. 分件精修、清理拓扑/法线、稳定三角化；拓扑修改前保存版本。
5. UV → 材质 → 中性光和项目风格对照；有差异列表再修正，最多三轮。
6. 三档LOD → 绑定 → 极限姿态 → 所有动作；每阶段查询实际场景数据。
7. 独立碰撞 → 白名单导出 → 新场景回读FBX → Unity检查。
8. 交付下面的量化记录，未完成项写degraded，不把计划当已验收。

## 交付验收

- [ ] 轮廓、功能分件与参考匹配；视角矛盾按文字统一并记录。
- [ ] 用世界坐标顶点求bbox，静止尺寸相对本规格偏差≤2%；不能只读对象dimensions。
- [ ] LOD面数≤{d['budget'][0]}/{d['budget'][1]}/{d['budget'][2]}；每档UV、法线、权重和材质完整。
- [ ] 无退化三角、孤立点、翻面；合法开孔/装配缝有记录，自交/壁厚未查不能写pass。
- [ ] 最大活动幅度和Reload 0/25/50/75/100%不穿插、不拉面、不掉部件。
- [ ] FBX回读骨架、绑定姿态与挂点相同；逐帧采样全部动作，无根位移/缩放曲线。
- [ ] 循环端点位置误差<0.1mm、角度<0.1°；非循环结尾回到定义的Idle或收纳姿态。
- [ ] FP待机/射击/调律在常规及瞄准FOV看清演奏，不以实体长期遮中心。
- [ ] 材质真正使用BC/ORM/E，不缺贴图，不依赖Blender专属节点。
- [ ] 预览、manifest、哈希、验证报告齐全；.blend可编辑，FBX含真实网格与动作。
"""
    (p/"modeling-prompt.md").write_text(md,encoding="utf-8")
    d["clips"]=[dict(state=n,duration=t,loop=l,fps=60) for n,t,l,_ in clips]
    d["game_config"]=g
(dest/"spec-data.json").write_text(json.dumps(D,ensure_ascii=False,indent=2),encoding="utf-8")
readme="# 六款音律武器建模任务包\n\n每个子目录包含独立 modeling-prompt.md 和两张重新生成的干净建模参考图，复制单个子目录即可交付。\n\n| 资产 | 乐器 | 游戏ID | 任务书 |\n|---|---|---|---|\n"
for d in D:readme+=f"| {d['id']} | {d['name']} | {d['gun']} | [打开]({d['id']}/modeling-prompt.md) |\n"
readme+="\n模板采用用户指定Blender仓库的技能规范。参考图用于形体和结构，不是精确工程测量图；文字尺寸为首版制作规格。首图从已选概念重新生成，结构图再引用首图链式生成。原始提示词/任务回执：<GAME:S>/_Inbox/music-weapon-modeling-20261119。\n\n仅交付任务书与参考图，未执行建模或修改Unity正式资源。\n"
(dest/"README.md").write_text(readme,encoding="utf-8")
print("Wrote six independent modeling briefs:",dest)
