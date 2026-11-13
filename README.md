# ProjectS — S

第一人称射击 + 局内构筑 roguelite。枪是容器，配件才是武器。

> 设计与数值文档**不在本工程**，在 `E:\Unity Development\Project\S\docs\`。
> 本文件只保留「打开工程就能跑」所需的信息。

## 环境

| 项 | 值 |
|----|-----|
| Unity | **6000.6.4f1** |
| 渲染 | URP 17.6（`Assets/Settings/PC_RPAsset`） |
| 输入 | Input System 1.19+（**仅新输入**，`activeInputHandler = 1`） |
| 运行时根 | `Assets/_Game/` |
| 数据 | `Assets/Resources/Data/sdata.json` |

## 运行

1. 用 Unity 6000.6.4f1 打开本工程
2. 打开 `Assets/Scenes/Main.unity`
3. Play

场景里只有一个 `S` 节点（`Bootstrap`），玩法对象由代码构建。
**UI 结构来自预制体**（`Assets/Resources/UI/Prefabs/`），详见下文「UI 预制体」。

## 操作

| 动作 | 键 |
|------|-----|
| 移动 / 视角 | WASD / 鼠标 |
| 射击 | 左键 |
| 装填 | R |
| 冲刺 | Shift |
| 跳跃 | 空格 |
| 切枪 | 1 / 2 |
| **背包（暂停）** | **Tab** |
| 暂停 / 关闭背包 | Esc |

## 验证

```powershell
# 全量（断言 → 场景 → 打包 → 实机自检）
powershell -ExecutionPolicy Bypass -File scripts\check.ps1

# 只跑结算断言（约 1.6 秒，不需要 Unity）
powershell -ExecutionPolicy Bypass -File scripts\check.ps1 -Task tests
```

判据：输出 `RESULT ALL PASS` 且退出码 0。

- 结算断言工程：`tools/loadout-tests/`（dotnet 8，直接链接 `Assets/_Game/Core` 源码）
- 实机自检：`Build/Windows/S.exe -scheck`，结果写 `selfcheck.log`，截图在 `Shots/`
- 冒烟（正常启动路径）：`-Task smoke`，覆盖主菜单/光标/遥测——这些在 `-scheck` 里被刻意关掉了
- 模型资产：`-Task assets` 单独跑（朝向契约 + 运行时生成 + 对照图 `Shots/models_all.png`）

> 跑 Unity 相关任务前**必须先关掉 Unity 编辑器**，否则工程锁被占用，
> batchmode 拿不到锁会静默退出或长时间干等。`check.ps1` 会检测到并直接报错。

## 模型朝向约定（**改模型前必读**）

**上 = +Y，前 = +Z。** 这条由 `S.EditorTools.SModelContract` 断言，`check.ps1` 每次全量都会跑。

Blender 里是 Z 上、-Y 前。FBX 导出（`axis_forward=-Z` / `axis_up=Y`）后，**Unity 会在根节点带一个
X≈-90°（270°）的旋转** —— 那正是 Z上→Y上 的轴转换，不是多余数据。因此：

| 该做 | 不该做 |
|------|--------|
| 保留导入自带的 `localRotation` | 把 `localRotation` 清零成 `identity` |
| 只改 `localPosition` / `localScale` | 手动补 `Euler(0,-90,0)` 之类「补偿旋转」 |
| 新增模型后跑 `-Task assets` | 看到模型歪了就转一下节点凑合 |

清零之后「上」会变成 Z 轴，敌人/柱子/墙全部躺倒；而按 Y 归一高度的代码会把「厚度」当高度，
于是模型被压扁、变小 —— 表现为「所有模型都变小且倒在地上」。

新增一件模型时，在 `SModelContract.Specs` 里登记一行（建模高度 + 正面特征材质名，旋转对称件填 `null`）。

## 光标与暂停（**加 UI 面板前必读**）

光标状态只有一个裁决点：`GameRoot.SyncCursorToState()`。

规则：**只要有面板在等玩家点击（暂停 / 背包 / 三选一 / 主菜单），光标就必须解锁**；
全部关掉之后才锁回去。新面板要纳入这个判断，**不要自己调 `PlayerController.LockCursor`**。

为什么必须集中：原先 `SetPaused` / `MenuView.Open` / `InventoryView.OpenBag` /
`PlayerController.Start` 四处各自写光标，谁最后执行谁生效。`Start` 之间的顺序由 Unity 决定，
于是「菜单开着却被锁光标」必然出现——表现是**打开游戏后鼠标动不了、点不到开始按钮**（踩过）。

同理，`PlayerController.Start` 只在「确实该由玩家操控」时才锁，`Bootstrap` 也不再无条件锁。

## 编辑器 Play 选项

`ProjectSettings/EditorSettings.asset` 里 **`m_EnterPlayModeOptionsEnabled` 必须为 0**（默认）。

曾经设成 `Enabled=1 / Options=3`（DisableDomainReload + DisableSceneReload）：那时按 Play 不会重载脚本域，
所有 `static` 字段跨局保留，`SAssets`/`SMat` 的缓存会指向上一局已销毁的对象。
这类残留极难定位，且只在「第二次及以后按 Play」出现——单次启动的自动化测试永远抓不到。
本项目不追求秒进 Play，宁可要确定的状态。

## 编辑器里鼠标失灵（已修，勿回退）

排查顺序是**从最可能的开始**，前两项都实测过：

### 1. 触摸模拟禁用了鼠标（本次真凶）

`Library/InputUserSettings.json` 里 **`simulateTouch` 必须为 `false`**。

开启时 `TouchSimulation` 会把鼠标设备**禁用**（`TouchSimulation.cs` 第 82 行
`InputSystem.DisableDevice(pointer, keepSendingEvents: true)`），再伪造一个 `Touchscreen` 接管。

**实测证据**（`Logs/mouse-live.txt`）：

```
Mouse      FastMouse   enabled=False   ← 被禁用
Pointer.current = Touchscreen          ← 指针被换成了触摸屏
mouse position / delta 恒为 0
Keyboard   FastKeyboard enabled=True   ← 键盘不受影响
```

这就是「键盘能用（回车能开局、WASD 能走）、鼠标完全没反应」的直接原因。
触发开关在 **Window > Analysis > Input Debugger** 里的 “Simulate Touch Input”。

`Library/` 是可再生的缓存目录，删了会被重建；所以这个偏好**可能被重置**。
`SInputFix` 每次都会把它写回 `false`，`check.ps1 -Task assets` 会校验。

### 2. 编辑器输入路由（次要，但也必须对）

`Assets/InputSystem.inputsettings.asset` 必须保持：

| 字段 | 值 | 原因 |
|------|-----|------|
| `m_EditorInputBehaviorInPlayMode` | `AllDeviceInputAlwaysGoesToGameView` (2) | 默认值 `PointersAndKeyboardsRespectGameViewFocus` 会在 **Game 视图失焦时把鼠标输入路由给编辑器**，游戏收不到。截图里 "Play Unfocused" 就是这个状态。 |
| `m_BackgroundBehavior` | `IgnoreFocus` (2) | 默认值 `ResetAndDisableNonBackgroundDevices` 会在失焦时**重置并禁用**设备。 |

**典型症状**：编辑器里按 Play，键盘正常、鼠标完全没反应；同一个构建打出的 exe 却一切正常——
因为这两项只影响编辑器，不影响播放器。

**为什么容易漏**：`InputSystem.settings` 是内存对象。工程里若没有 `*.inputsettings.asset`，
改它只改内存，重启编辑器就还原。必须**创建资产并注册到 `EditorBuildSettings`**
（键 `com.unity.input.settings`）才会被采用。`SInputFix` 两者都做。

## 鼠标点不动怎么查

按这个顺序排查（前三条是本项目已确认正常的，不用再查）：

1. **菜单能否显示** → 能显示说明 UI 构建正常
2. **光标是否可见且未锁定** → 冒烟的「菜单期间光标可见/未锁定」两条断言覆盖
3. **射线能否到达按钮** → 冒烟的「开始按钮可被 UI 点击」断言会走完整的
   射线 → IPointerClickHandler → 按钮回调 链路，不是程序化直达
4. **EventSystem 是否只有一个** → 多个会互相抢事件
5. **UI 模块是否绑定含 `UI` map 的 actions** → 若绑到只有 `Player` map 的资产，点击全部失效
6. **Game 视图是否获得焦点** → 编辑器层面，点一下 Game 视图再试

## UI 素材

素材在 `Assets/Resources/UI/`。按钮 / 槽位 / 条 / 稀有度标签 / 图标 / 分隔线 / 命中标记 / 受击晕影
由 `tools/gen_ui_assets_ai.py` 合成（**不要手工改 PNG**，改脚本重跑）。

### 三段流水线（不许跳步）

```
APIMart 出图          前期处理                合成                  接入引擎
tools/ui-ai-raw/ ──► tools/ui-ai-src/ ──► Assets/Resources/UI/ ──► Unity 导入
 RGB、黑底、4~8MB     抠图/尺寸/质量门        几何 + 9-slice         .meta 边距
```

各段职责单一，别合并：

- **APIMart 负责材质**：金属拉丝的微观起伏、能量条的细丝、图标的字形。
- **前期处理负责抠图与规范化**：黑底转 alpha、颜色反解去黑边、降尺寸、质量门。
- **合成器负责几何**：圆角半径、描边宽度、9-slice 安全区。必须像素级确定，
  生图模型每次画的圆角半径都不同，9-slice 一拉伸就把圆角扯成椭圆。

一键跑完整条（推荐）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\check.ps1 -Task ui
```

它会按序执行 4 步，任一步失败即中止：**前期处理 → 合成 → 边距审计 → 重导贴图**。

### 第 1 段：前期处理（`tools/preprocess_ui_assets.py`）

```powershell
python tools\preprocess_ui_assets.py              # 处理全部
python tools\preprocess_ui_assets.py --only icon  # 只处理某一类
python tools\preprocess_ui_assets.py --audit-legacy  # 列出绕过流水线的贴图
```

这一步解决四个具体问题：

| 问题 | 实测 | 处理 |
|------|------|------|
| 图标是黑底白字、无 alpha | RGB，背景亮度 0.1 | 按亮度抠图，黑→透明、白→不透明、灰边→半透明 |
| 抠图残留黑边 | 半透明像素的 RGB 仍近黑 | 颜色反解：RGB 统一写纯白，只让 alpha 承担透明度 |
| 原图 2048²、体积 4~8MB | 实际最大只采样到 512px | 按规格降尺寸 |
| 同组图标大小/重量不一致 | 逐张判看不出离群 | 横向比墨量（alpha 总和），比值 >1.9 拦下 |

**质量门**（不通过就退出码 1，合成器会拒绝继续）：
空图、材质无纹理（σ<3）、黑底没抠净（四角残留>6）、字形碰到画布边缘、
字形占比超出区间、同组墨量比失调、不透明像素不是纯白。

### 第 2 段：合成

```powershell
python tools\gen_ui_assets_ai.py                 # 全部
python tools\gen_ui_assets_ai.py --only btn,bar  # 只重做某一类
python tools\gen_ui_assets_ai.py --list          # 看有哪些类
```

喂给它**未预处理的原图会直接报错退出** —— 原图没有 alpha，合成出来是黑方块。

脚本画出 21 张件（按钮 / 槽位 / 条 / 稀有度标签 / 图标 / 分隔线 / 命中标记 / 受击晕影）。

`panel` / `card_frame` / `bar_frame` / `banner` / `keycap` / `crosshair` / `vignette` /
`icon_bullet` / `icon_whole` / `icon_after` / `icon_gun` 这 11 张是早期 AI 出图，
**绕过了流水线**（2048²/1254²，合计约 12MB）。用 `--audit-legacy` 可随时列出。
迁移要点：降尺寸必须**同步重算 9-slice 边距**，因为边距是贴图像素，
缩放贴图会让同样的边距在画面上放大，圆角观感随之改变。

### 新增一张素材的完整步骤

1. 登记 `tools/apimart-assets.json`，`out` 指向 `tools/ui-ai-raw/`
2. APIMart 生成
3. 在 `preprocess_ui_assets.py` 的 `SPECS` 加规格（`material` 或 `glyph`），跑第 1 段
4. 在 `gen_ui_assets_ai.py` 的 `build()` 加合成逻辑，跑第 2 段
5. 跑 `check.ps1 -Task ui`（含边距审计与重导）

**9-slice 边距由 `SAssetPostprocessor.BorderFor` 按文件名自动配置**，不需要手工改 meta：

| 素材 | 边距 (L,B,R,T) | 最小控件 | 依据 |
|------|----------------|----------|------|
| `btn_primary` / `btn_primary_hover` / `btn_danger` | 16,14,16,14 | 108×40 | 背包 `BuyButton` |
| `bar_bg` | 3,3,3,3 | 360×14 | HUD 的 XP 条 |
| `slot_empty` | 18,18,18,18 | 60×84 | `ChainRow.Tail` |
| `slot_filled` | 6,6,6,6 | 20×20 | `Menu.BadgeBack` |
| `tag_*` | 1,1,1,1 | 272×16 | `OfferCard.TagTop` |
| `panel` | 20,20,20,20 | 380×228 | `VolumePanel` |

### ⚠ 边距必须按「控件真实像素」定，不是贴图尺寸

这是踩过的坑，代价是一整轮返工。贴图 512×96 会让人以为"可拉伸区 464×48 很安全"，
但 **Unity 的 `spriteBorder` 是贴图像素，1:1 映射到 canvas 单位**。实际控件只有 360×14 时，
上下边距 24+24=48 早就超过 14 —— Unity 会钳制边距并把圆角拉成直角，两端糊成一块。

判断公式：`border.x + border.z < 控件宽` 且 `border.y + border.w < 控件高`（还要留 8px 余量）。

工具：`python tools\audit_ui_slices.py` —— 解析全部 prefab 的 RectTransform
（含 anchor 拉伸，按 1600×900 参考分辨率递推绝对像素），逐条列出
`控件真实尺寸 vs 贴图尺寸 vs 边距`，有问题直接打印。**改完布局必须跑一次。**

### ⚠ 改完 `BorderFor` 必须重新导入贴图

`OnPreprocessTexture` 只在**首次导入**时执行。素材重新生成（PNG 变了）不等于重新导入，
Unity 若判定内容未变就复用旧 meta，`spriteBorder` 会保留旧值。症状极隐蔽：
改了代码、打包通过、自检也过，但画面上圆角照样是坏的。

```powershell
# 方法一：菜单 S/重新导入 UI 贴图
# 方法二：命令行
unity -batchmode -quit -projectPath . -executeMethod S.EditorTools.SUiTextureReimport.ReimportAll
```

`S.EditorTools.SUiTextureReimport.DumpBorders` 可打印每张图的实际边距用于核对。

### ⚠ 自检断言的生效链

`shots` 跑的是 `Build/Windows/S.exe`。注意 **`S.exe` 本身是 Mono 启动壳，不会随时间更新**，
代码全在 `globalgamemanagers` / `resources.assets` 里 —— 别拿 `S.exe` 的时间戳判断是否重新打包，
看 `Build/Windows/resources.assets` 的时间。

另外 `check.ps1` 的 `-Task shots` 用 `-scheck` 参数启动自检；手工复现时参数写错会跑到
其它分支、日志不更新，看起来像"断言没生效"。

新增素材时要同步做三件事，否则会**静默降级成纯色**：
1. 在 `UiAssets` 加属性（`Load("名", ref 缓存)`）
2. 加进 `UiAssets.LoadedCount()` 的数组
3. 在 `SelfCheck` 的「UI 素材全部加载」里 `Need("名", ...)` 点名

> 第 3 条是踩过的坑：原来断言写的是 `uiLoaded >= 11`，新增素材全失败也照样通过。
> 现在改成精确点名，缺哪张直接报出来。

所有 `Apply*` 方法在素材缺失时返回 false，调用方保留原纯色 —— **缺图不能让界面变空白**。

## UI 预制体（**做界面前必读**）

界面结构**一律走预制体**，放在 `Assets/Resources/UI/Prefabs/`：

| 预制体 | 用途 |
|--------|------|
| `Menu` | 主菜单 + 局外解锁 + 音量；内含 `RowTemplate`（解锁行模板） |
| `HUD` | 血条 / 弹药 / 经验 / 威胁 / 轮转格位 / 准星 / Boss 条 |
| `Inventory` | 背包与改装；内含 `ChainRowTemplate`、`GunTabTemplate` |
| `Offer` | 三选一面板；内含 `CardTemplate` |
| `ItemCard` | 链内与背包里的配件卡 |
| `OfferCard` | 三选一候选卡 |
| `ChainRow` / `GunTab` | 配件位一行 / 枪页签 |

运行时脚本（`MenuView` / `HudView` / `InventoryView`）**只做两件事**：
把数据填进预制体上的 `*Refs` 引用，挂按钮回调。
**不要在 View 里 `new GameObject` + `AddComponent` 去拼可见元素** —— 那样布局又回到代码里，
改一个间距就得重编译，评审也看不出界面长什么样。

### 三条硬约束

1. **每个挂进预制体的 `MonoBehaviour` 必须单独成文件**，且文件名 = 类名。
   Unity 反序列化预制体时按文件名定位脚本；多个 MonoBehaviour 挤在一个文件里，
   预制体里只会剩下 `Missing (Mono Script)`，运行时报「缺少 XxxRefs（引用表未配置）」。
   （`SlotDropTarget` / `DraggableItem` / 各类 `*Refs` 因此都是独立文件。）
2. **引用靠序列化，不靠 `transform.Find("名字")`**。改名或调层级时序列化引用会跟着走，
   按名字查找会静默返回 null，表现为「按钮点了没反应」。
3. **模板节点保持非激活**（`RowTemplate` / `ChainRowTemplate` 等），运行时 `Instantiate` 后再 `SetActive(true)`。

### 改界面

```
Prefab 模式下直接改 Assets/Resources/UI/Prefabs/*.prefab  → 存的改动就是运行时生效的
```

`S/生成 UI 预制体`（`SUiPrefabFactory`）是**一次性资产作者**，用于从零重建全部预制体。
它会**覆盖**已有预制体 —— 手工微调过就不要再跑，除非想推倒重来。

> 少见的例外：瞬时反馈文字（浮动伤害数字、2.5s 的「背包已满」提示、空背包「（空）」）
> 由 `UiKit.MakeText` 现造。它们数量不定、生命周期极短、位置依赖上下文，
> 为每一种做预制体反而更难维护。**面板/按钮/卡片/行这类结构件一律用预制体。**

## 目录

```text
Assets/
  _Game/
    Core/     零 Unity 依赖：结算、轮转、局内状态、数据
    View/     玩法表现：玩家、武器、弹丸、敌人、HUD、背包
    App/      场景入口 Bootstrap、实机自检 SelfCheck
    Editor/   场景生成 / 打包 / 着色器写入 / 模型朝向契约与对照渲染
  Resources/Data/sdata.json    唯一数值来源
  Scenes/Main.unity            唯一场景（单入口节点）
  Settings/                    URP 资产
scripts/check.ps1              验证入口（UTF-8 BOM）
tools/loadout-tests/           结算断言（dotnet）
```

## 注意

- `Core/` 不得 `using UnityEngine`（`SDataLoader.cs` 是唯一例外），否则 dotnet 断言会失效
- 材质必须走 `SMat`，不要裸 `new Material(Shader.Find(...))` —— URP 着色器打包时会被剥离
- 新增 URP 着色器后跑一次 `S/写入 Always Included Shaders`
