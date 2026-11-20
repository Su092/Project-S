# HUD V2 原始素材

本目录仅存原始生成结果与任务元数据。未裁切、未去底、未重采样、未导入 Unity、未制作预制体。

- 规格与拆件：<HUB>/Project/S/docs/hud-v2-assets.md
- 完整提示词：<HUB>/Project/S/docs/hud-v2-prompts.md
- 调用：<HUB>/.cursor/skills/apimart-image/SKILL.md；gpt-image-2，UI 全部 2k，n=1。
- generation-plan.json：每张原图的提示词、组件 ID 与格位。
- receipts/：任务 ID、状态、原始下载 URL、尺寸、格式、文件 SHA-256；结果 URL 为短期链接，本地原图为交付依据。
- 格位按从左到右、从上到下。黑底保留，后续加工阶段才制作透明图与精确配准。

| 原图 | 内容 | 格位 |
|---|---|---|
| [01_frames.png](raw/01_frames.png) | 基础框体 | weapon_rail, portrait_frame, attachment_socket, minimap_bezel, pause_button_base, item_slot |
| [02_bars.png](raw/02_bars.png) | 血量经验与 Boss 条 | hp_track_frame, hp_fill, xp_track_frame, xp_fill, boss_track_frame, boss_fill |
| [03_combat.png](raw/03_combat.png) | 准心换弹与右侧弹药弧 | crosshair, reload_spinner, ammo_arc_track, ammo_arc_fill |
| [04_markers.png](raw/04_markers.png) | 地图标记与操作符号 | map_player_heading, map_enemy, map_chest, pause_glyph, weapon_swap, read_cursor |
| [05_weapons.png](raw/05_weapons.png) | 六类武器独立图标 | gun_rifle, gun_smg, gun_shotgun, gun_sniper, gun_beam, gun_launcher |
| [06_portrait.png](raw/06_portrait.png) | 匿名角色头像 | operator_portrait |
| [07_states.png](raw/07_states.png) | 计时与状态叠层 | timer_plate, weapon_selected_ring, attachment_read_ring, utility_placeholder |
| [08_modifiers.png](raw/08_modifiers.png) | 补齐十二个配件字形 | rear_boost, twin_boost, forward_battery, rear_battery, crossfire, gap_lens, rear_frost, ring_burn, gap_bounce, rear_blast, whole_boost, whole_guidance |

## 原图检查结果

8/8 任务完成，8/8 文件可解码且 SHA-256 与下载回执一致，全部长边 2048。已逐张人工视觉查看，无烘焙文字或组件重叠。结果详见 validation.json。

原图阶段注意项：弹药底弧与填充生成为约 180° 右半圆（提示词目标为 90°），功能方向正确，后续加工时需按准心布局确定弧度并精确配准；本轮不裁弧。武器/头像细节需在后续实际显示尺寸下验收。黑底、原始留白、原始像素均保留。此检查不等于成品 Sprite 或游戏布局验收。
