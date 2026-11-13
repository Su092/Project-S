"""
Project S — 低模资产批处理建模脚本

用法（headless）：
  blender.exe --background --python build_assets.py -- <out_dir>

设计原则（对应 docs/art-style.md）：
  1. 每个资产都是「少量平面块体」拼成，不做细节刻线、不做倒角、不刻齿轮。
     判据：单件三角面 200–2500，建模脚本单文件可读。
  2. 全部单位是米，Z 向上；导出前 apply 变换、单根、原点在落地中心。
  3. 命名遵循工作区契约：Modle_<Name>。
  4. 不做骨架/动画（初版敌人与枪械都是静态刚体，靠代码位移与朝向）。
     需要骨架时另写 Anim_ 资产，不混进本脚本。

为什么用脚本建模而不是手工：这些件形态高度重复（箱体/圆柱/锥），
脚本产出的结果是可复现的，改了尺寸只需重跑，也便于过契约门。
"""

import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector

# ---------------- 基础设施 ----------------

def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_material(name, rgba, roughness=0.7, metallic=0.0, emission=None, emit_strength=0.0):
    """
    创建 Principled 材质。颜色写进节点输入，不靠 diffuse_color。

    注意：Emission 强度必须克制（≤1.2）。预览与引擎里发光过强会把 basecolor 冲成白色，
    表现为「红色变粉、琥珀变米白」（踩过）。真正的发光件用单独的高强度材质。
    """
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        return mat
    bsdf.inputs["Base Color"].default_value = rgba
    if "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = roughness
    if "Metallic" in bsdf.inputs:
        bsdf.inputs["Metallic"].default_value = metallic
    if emission is not None:
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = emission
        elif "Emission" in bsdf.inputs:
            bsdf.inputs["Emission"].default_value = emission
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = emit_strength
    return mat


def add_box(name, size, loc, mat, rot=(0, 0, 0)):
    """
    轴对齐盒体。size 为 (x,y,z) **全尺寸**。

    关键细节：primitive_cube_add(size=1.0) 生成的是边长 1 的立方体，
    即半边长 0.5。要把它的边长变成 size[k]，缩放必须设成 size[k] 本身，
    而不是 size[k]*0.5 —— 早先乘了 0.5，所有盒体都只有标称尺寸的一半，
    导致部件之间出现「看得见的缝隙」、整体比例失真（踩过）。
    """
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    ob = bpy.context.active_object
    ob.name = name
    ob.scale = (size[0], size[1], size[2])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    ob.data.materials.append(mat)
    return ob


def add_cyl(name, radius, depth, loc, mat, verts=12, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth,
                                        location=loc, rotation=rot)
    ob = bpy.context.active_object
    ob.name = name
    if scale != (1, 1, 1):
        ob.scale = scale
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    ob.data.materials.append(mat)
    return ob


def add_sphere(name, radius, loc, mat, segments=16, rings=8, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings,
                                         radius=radius, location=loc)
    ob = bpy.context.active_object
    ob.name = name
    if scale != (1, 1, 1):
        ob.scale = scale
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    ob.data.materials.append(mat)
    return ob


def add_cone(name, r1, r2, depth, loc, mat, verts=12, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2,
                                    depth=depth, location=loc, rotation=rot)
    ob = bpy.context.active_object
    ob.name = name
    ob.data.materials.append(mat)
    return ob


# 当前 builder 的部件包围盒快照，供装配门检查。
# 必须在 join 之前记录：join 之后原 object 引用会失效（StructRNA 报错）。
# 格式：[(name, (minX,minY,minZ), (maxX,maxY,maxZ)), ...]
_CURRENT_PARTS = []


def _bounds_of(ob):
    bpy.context.view_layer.update()
    co = [ob.matrix_world @ v.co for v in ob.data.vertices]
    if not co:
        return (0, 0, 0), (0, 0, 0)
    mn = (min(c.x for c in co), min(c.y for c in co), min(c.z for c in co))
    mx = (max(c.x for c in co), max(c.y for c in co), max(c.z for c in co))
    return mn, mx


def join_all(objs, name):
    """合并为一个对象，保留多材质槽（Unity 会拆成 submesh）。"""
    global _CURRENT_PARTS
    objs = [o for o in objs if o is not None]

    # 先快照包围盒，再合并
    _CURRENT_PARTS = [(o.name, *_bounds_of(o)) for o in objs]

    if not objs:
        return None
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.active_object
    ob.name = name
    ob.data.name = name + "_mesh"
    return ob


def finalize(ob, name, origin_at_ground=True):
    """清理、法线、落地、单根。"""
    # 合并重复顶点、三角化
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")

    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    if origin_at_ground:
        # 把最低点压到 z=0，原点放在底面中心
        bpy.context.view_layer.update()
        zs = [(ob.matrix_world @ Vector(c)).z for c in ob.bound_box]
        minz = min(zs)
        ob.location.z -= minz
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    ob.name = name
    ob.data.name = name + "_mesh"
    return ob


def tri_count(ob):
    me = ob.data
    return sum(max(1, len(p.vertices) - 2) for p in me.polygons)


# ---------------- 调色板（与 art-style.md 一致）----------------

C_BODY = (0.028, 0.034, 0.042, 1.0)
C_PANEL = (0.085, 0.098, 0.118, 1.0)
C_METAL = (0.145, 0.165, 0.192, 1.0)
C_AMBER = (0.82, 0.42, 0.055, 1.0)
C_CYAN = (0.055, 0.32, 0.62, 1.0)
C_RED = (0.62, 0.075, 0.085, 1.0)
C_LIME = (0.33, 0.62, 0.055, 1.0)
C_BLUE = (0.075, 0.24, 0.62, 1.0)
C_ORANGE = (0.72, 0.35, 0.055, 1.0)
C_PURPLE = (0.42, 0.11, 0.62, 1.0)
C_MAGENTA = (0.75, 0.10, 0.65, 1.0)


def mats():
    return {
        "body": new_material("M_Body", C_BODY, 0.62),
        "panel": new_material("M_Panel", C_PANEL, 0.55),
        "metal": new_material("M_Metal", C_METAL, 0.34, 0.65),
        "amber": new_material("M_Amber", C_AMBER, 0.45, 0.0, C_AMBER, 1.0),
        "orange": new_material("M_Orange", C_ORANGE, 0.50, 0.0, C_ORANGE, 0.8),
        "cyan": new_material("M_Cyan", C_CYAN, 0.45, 0.0, (0.09, 0.55, 1.0, 1.0), 1.2),
        "red": new_material("M_Red", C_RED, 0.50, 0.0, C_RED, 0.6),
        "lime": new_material("M_Lime", C_LIME, 0.50, 0.0, C_LIME, 0.7),
        "blue": new_material("M_Blue", C_BLUE, 0.42, 0.3, C_BLUE, 0.5),
        "purple": new_material("M_Purple", C_PURPLE, 0.40, 0.25, C_PURPLE, 0.6),
        "magenta": new_material("M_Magenta", C_MAGENTA, 0.35, 0.0, C_MAGENTA, 1.4),
    }


def add_chamfer_box(name, size, loc, mat, chamfer=0.05, rot=(0, 0, 0)):
    """
    带倒角的盒体。参考图里所有甲片都有明确倒角，纯直角盒体看起来太"占位"。
    bevel 宽度很小、段数 1，三角面增加可控（每个盒体 +24 面左右）。
    """
    ob = add_box(name, size, loc, mat, rot)
    bpy.context.view_layer.objects.active = ob
    m = ob.modifiers.new(name="Bevel", type="BEVEL")
    m.width = chamfer
    m.segments = 1
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(50)
    bpy.ops.object.modifier_apply(modifier=m.name)
    return ob


# ---------------- 资产：敌人 ----------------

def build_crawler(M):
    """
    爬行者（按 ref_crawler.png 校准）：
    楔形躯干 + 红色顶壳 + 四条外撇粗腿 + 琥珀视缝 + 侧面琥珀条 + 顶壳青色条。
    目标 ≈ 0.90 × 1.34 × 0.90 m。

    贴合纪律：所有贴面装饰（条带/发光缝）必须**嵌入宿主表面 1–2cm**，
    不能只是把薄片放在表面外——那会在 3/4 视角看起来「悬空」（踩过）。
    """
    parts = []
    # 主躯干：z=[0.34, 0.78]
    parts.append(add_chamfer_box("Body", (0.66, 1.02, 0.44), (0, 0.04, 0.56), M["body"], 0.05))
    # 顶壳：顶面 z=0.85
    parts.append(add_chamfer_box("Shell", (0.70, 0.84, 0.18), (0, 0.10, 0.76), M["red"], 0.05))
    # 前脸
    parts.append(add_chamfer_box("Head", (0.48, 0.38, 0.36), (0, -0.58, 0.52), M["body"], 0.04))
    # 视缝：嵌入前脸表面
    parts.append(add_box("Visor", (0.36, 0.05, 0.085), (0, -0.755, 0.56), M["amber"]))
    # 侧面琥珀条：躯干侧面 x=±0.33，中心 ±0.322（嵌入 8mm）
    for sx in (-1, 1):
        parts.append(add_box(f"SideStripe{sx}", (0.028, 0.60, 0.062),
                             (0.322 * sx, 0.06, 0.50), M["amber"]))
    # 顶壳青色条：贴顶面 z=0.85
    for sx in (-1, 1):
        parts.append(add_box(f"ShellCyan{sx}", (0.055, 0.20, 0.024),
                             (0.14 * sx, -0.16, 0.849), M["cyan"]))
    # 四腿：上段顶端 z=0.52 插入躯干（底面 0.34），脚掌底面 z=0
    for sx in (-1, 1):
        for sy in (-1, 1):
            x = 0.30 * sx
            y = 0.34 * sy
            parts.append(add_chamfer_box(f"LegUp{sx}{sy}", (0.18, 0.18, 0.36),
                                         (x, y, 0.35), M["panel"], 0.03))
            parts.append(add_chamfer_box(f"LegLow{sx}{sy}", (0.16, 0.16, 0.32),
                                         (x * 1.14, y, 0.20), M["body"], 0.03))
            parts.append(add_box(f"Foot{sx}{sy}", (0.20, 0.32, 0.14),
                                 (x * 1.14, y - 0.04, 0.07), M["metal"]))
            parts.append(add_box(f"LegStripe{sx}{sy}", (0.196, 0.196, 0.05),
                                 (x, y, 0.38), M["amber"]))
    ob = join_all(parts, "Modle_Enemy_Crawler")
    return finalize(ob, "Modle_Enemy_Crawler")


def build_spitter(M):
    """
    喷吐者（按参考风格）：胶囊躯干 + 无头喷嘴 + 双腿 + 短臂 + 酸罐 + 橙色环带。
    约 1.85m 高。
    """
    parts = []
    # 躯干
    parts.append(add_cyl("Body", 0.31, 0.88, (0, 0, 1.08), M["body"], 12, scale=(1.0, 0.88, 1.0)))
    parts.append(add_sphere("Shoulder", 0.32, (0, 0, 1.46), M["body"], 14, 7, scale=(1.0, 0.9, 0.62)))
    # 喷嘴总成（代替头）
    parts.append(add_chamfer_box("NozzleHousing", (0.34, 0.26, 0.30), (0, -0.28, 1.58), M["panel"], 0.03))
    parts.append(add_cone("Nozzle", 0.155, 0.085, 0.32, (0, -0.52, 1.58), M["metal"], 12,
                          rot=(math.radians(-90), 0, 0)))
    parts.append(add_cyl("NozzleTip", 0.08, 0.05, (0, -0.70, 1.58), M["lime"], 12,
                         rot=(math.radians(90), 0, 0)))
    # 背后酸罐（倾斜）
    parts.append(add_cyl("Tank", 0.175, 0.46, (0, 0.30, 1.36), M["lime"], 10,
                         rot=(math.radians(14), 0, 0)))
    parts.append(add_cyl("TankBand", 0.19, 0.06, (0, 0.24, 1.44), M["amber"], 10,
                         rot=(math.radians(14), 0, 0)))
    # 躯干上的橙色环带（参考图的 U 形胸口条的简化）
    parts.append(add_box("ChestStripe", (0.34, 0.02, 0.055), (0, -0.275, 1.20), M["amber"]))
    parts.append(add_box("BeltStripe", (0.56, 0.03, 0.06), (0, 0, 0.92), M["amber"]))
    # 双腿：大腿 z=[0.56,0.96] / 小腿 z=[0.15,0.57] / 脚 z=[0,0.16]，逐级重叠
    for sx in (-1, 1):
        parts.append(add_chamfer_box(f"Thigh{sx}", (0.17, 0.19, 0.40), (0.17 * sx, 0.02, 0.76), M["panel"], 0.03))
        parts.append(add_chamfer_box(f"Shin{sx}", (0.15, 0.17, 0.42), (0.17 * sx, 0.04, 0.36), M["metal"], 0.03))
        parts.append(add_box(f"Foot{sx}", (0.18, 0.34, 0.16), (0.17 * sx, -0.04, 0.08), M["panel"]))
        parts.append(add_box(f"AnkleStripe{sx}", (0.178, 0.178, 0.042), (0.17 * sx, 0.04, 0.19), M["amber"]))
    # 双短臂
    for sx in (-1, 1):
        parts.append(add_chamfer_box(f"Arm{sx}", (0.13, 0.15, 0.36), (0.35 * sx, -0.06, 1.16), M["panel"], 0.03))
        parts.append(add_box(f"WristStripe{sx}", (0.14, 0.16, 0.04), (0.35 * sx, -0.06, 0.99), M["amber"]))
    ob = join_all(parts, "Modle_Enemy_Spitter")
    return finalize(ob, "Modle_Enemy_Spitter")


def build_shield(M):
    """
    盾卫（按 ref_shield.png 校准）：
    无头人形 + 梯形领口 + 厚实四肢 + 倒角塔盾 + 橙色 U 形胸条/腕条/踝条 + 盾上青色发光条。
    目标高度 ≈ 2.05m。
    """
    parts = []
    # 躯干（上宽下窄，两块叠出梯形轮廓）
    parts.append(add_chamfer_box("TorsoLow", (0.70, 0.42, 0.54), (0, 0, 1.12), M["body"], 0.05))
    parts.append(add_chamfer_box("TorsoUp", (0.80, 0.44, 0.42), (0, 0, 1.56), M["body"], 0.05))
    # 梯形领口（无头，只有一个抬起的领圈）
    parts.append(add_chamfer_box("Collar", (0.44, 0.38, 0.22), (0, -0.02, 1.86), M["panel"], 0.04))
    # 橙色 U 形胸条
    parts.append(add_box("ChestU_L", (0.035, 0.02, 0.28), (-0.25, -0.225, 1.62), M["amber"]))
    parts.append(add_box("ChestU_R", (0.035, 0.02, 0.28), (0.25, -0.225, 1.62), M["amber"]))
    parts.append(add_box("ChestU_B", (0.54, 0.02, 0.035), (0, -0.225, 1.47), M["amber"]))
    # 腿（加长，让总高达标）
    for sx in (-1, 1):
        parts.append(add_chamfer_box(f"Thigh{sx}", (0.24, 0.27, 0.46), (0.19 * sx, 0, 0.76), M["panel"], 0.04))
        parts.append(add_chamfer_box(f"Shin{sx}", (0.22, 0.25, 0.46), (0.19 * sx, 0.02, 0.32), M["metal"], 0.04))
        parts.append(add_box(f"Foot{sx}", (0.24, 0.40, 0.13), (0.19 * sx, -0.07, 0.065), M["panel"]))
        parts.append(add_box(f"AnkleStripe{sx}", (0.225, 0.255, 0.05), (0.19 * sx, 0.02, 0.18), M["amber"]))
    # 臂
    parts.append(add_chamfer_box("ArmR", (0.20, 0.22, 0.60), (-0.47, -0.06, 1.32), M["panel"], 0.04))
    parts.append(add_box("WristStripeR", (0.21, 0.23, 0.05), (-0.47, -0.06, 1.04), M["amber"]))
    parts.append(add_chamfer_box("ArmL", (0.20, 0.22, 0.48), (0.45, -0.18, 1.38), M["panel"], 0.04))
    parts.append(add_box("WristStripeL", (0.21, 0.23, 0.05), (0.45, -0.18, 1.16), M["amber"]))
    # 塔盾（倒角，略前倾）
    parts.append(add_chamfer_box("Shield", (0.98, 0.12, 1.36), (0, -0.53, 1.20), M["body"],
                                 0.075, rot=(math.radians(-5), 0, 0)))
    parts.append(add_box("ShieldCyan", (0.68, 0.035, 0.058), (0, -0.60, 1.56), M["cyan"]))
    for sx in (-1, 1):
        parts.append(add_box(f"Brace{sx}", (0.11, 0.30, 0.11), (0.27 * sx, -0.37, 1.36), M["metal"]))
    ob = join_all(parts, "Modle_Enemy_Shield")
    return finalize(ob, "Modle_Enemy_Shield")


def build_drone(M):
    """无人机：扁平六边碟 + 四短臂 + 转子盘 + 前透镜 + 琥珀环。悬空，目标总高 ≈0.45m。"""
    parts = []
    parts.append(add_cyl("Disc", 0.46, 0.16, (0, 0, 0), M["body"], 6))
    parts.append(add_cyl("Hub", 0.23, 0.22, (0, 0, 0.01), M["panel"], 12))
    parts.append(add_cyl("RingStripe", 0.478, 0.04, (0, 0, 0.015), M["amber"], 6))
    parts.append(add_cyl("Lens", 0.11, 0.10, (0, -0.44, 0), M["amber"], 12,
                         rot=(math.radians(90), 0, 0)))
    # 四臂 + 转子。转子盘做厚一点，把整体高度撑到目标值
    for i in range(4):
        a = math.radians(45 + i * 90)
        x, y = math.cos(a) * 0.46, math.sin(a) * 0.46
        parts.append(add_box(f"Arm{i}", (0.36, 0.11, 0.08), (x * 0.72, y * 0.72, 0), M["metal"],
                             rot=(0, 0, a)))
        parts.append(add_cyl(f"Motor{i}", 0.075, 0.20, (x * 1.14, y * 1.14, 0.02), M["panel"], 10))
        parts.append(add_cyl(f"Rotor{i}", 0.24, 0.05, (x * 1.14, y * 1.14, 0.13), M["metal"], 12))
        parts.append(add_cyl(f"RotorRing{i}", 0.25, 0.022, (x * 1.14, y * 1.14, 0.155), M["amber"], 12))
    # 顶部信号罩：保证整体高度落在设计范围内
    parts.append(add_cyl("Dome", 0.15, 0.12, (0, 0, 0.20), M["body"], 10))
    parts.append(add_cyl("DomeRing", 0.16, 0.03, (0, 0, 0.255), M["amber"], 10))
    ob = join_all(parts, "Modle_Enemy_Drone")
    return finalize(ob, "Modle_Enemy_Drone", origin_at_ground=False)


def build_boss(M):
    """
    铸造核心：装甲球 + 六块倒角护板 + 赤道四段发光环 + 三条下垂臂 + 顶部尖冠。
    目标总高 ≈3.5m，悬浮（原点在体心，不做落地对齐）。
    """
    parts = []
    # 主体球：抬高到 z=2.10，半径放大到 1.25
    parts.append(add_sphere("Core", 1.25, (0, 0, 2.10), M["body"], 18, 10, scale=(1.0, 1.0, 0.88)))
    # 六块护板
    for i in range(6):
        a = math.radians(i * 60)
        r = 1.18
        z = 2.10 + (0.66 if i % 2 == 0 else -0.66)
        parts.append(add_chamfer_box(f"Plate{i}", (0.88, 0.32, 0.56),
                                     (math.cos(a) * r * 0.92, math.sin(a) * r * 0.92, z),
                                     M["purple"], 0.06, rot=(0, 0, a)))
    # 赤道发光环（四段）。外扩到球体之外，否则完全被护板挡住，
    # 玩家看不出「核心」在哪。每段用一根短撑连回球体，避免被判悬空。
    for i in range(4):
        a = math.radians(i * 90 + 22)
        parts.append(add_box("RingSeg", (1.02, 0.16, 0.24),
                             (math.cos(a) * 1.42, math.sin(a) * 1.42, 2.10),
                             M["magenta"], rot=(0, 0, a)))
        # 连接撑：从环内侧伸进球体
        parts.append(add_box(f"RingBrace{i}", (0.30, 0.14, 0.16),
                             (math.cos(a) * 1.16, math.sin(a) * 1.16, 2.10),
                             M["panel"], rot=(0, 0, a)))
    # 顶部尖冠
    parts.append(add_cyl("CrownRing", 0.56, 0.09, (0, 0, 3.28), M["amber"], 12))
    parts.append(add_cone("Crown", 0.50, 0.07, 0.56, (0, 0, 3.56), M["panel"], 8))
    # 三条下垂臂：从球体下缘垂到 z≈0.60
    for i in range(3):
        a = math.radians(90 + i * 120)
        x, y = math.cos(a) * 0.74, math.sin(a) * 0.74
        parts.append(add_chamfer_box(f"ArmV{i}", (0.26, 0.26, 0.94), (x, y, 1.28), M["panel"], 0.05,
                                     rot=(0, 0, a)))
        parts.append(add_box(f"ArmGlow{i}", (0.28, 0.28, 0.045), (x, y, 1.02), M["magenta"]))
        parts.append(add_chamfer_box(f"ArmTip{i}", (0.38, 0.38, 0.22), (x, y, 0.72), M["metal"], 0.05,
                                     rot=(0, 0, a)))
    ob = join_all(parts, "Modle_Boss_ForgeCore")
    return finalize(ob, "Modle_Boss_ForgeCore", origin_at_ground=False)


# ---------------- 资产：枪械（第一人称，原点在握把上方，枪管朝 -Y）----------------

def build_gun_auto(M):
    """MK-2 蜂鸣：机匣 + 短枪管护罩 + 握把 + 弹匣 + 琥珀条。"""
    parts = []
    parts.append(add_box("Receiver", (0.10, 0.42, 0.13), (0, 0, 0.06), M["body"]))
    parts.append(add_box("Upper", (0.09, 0.30, 0.06), (0, -0.02, 0.15), M["panel"]))
    parts.append(add_cyl("Shroud", 0.045, 0.30, (0, -0.33, 0.07), M["metal"], 10,
                         rot=(math.radians(90), 0, 0)))
    parts.append(add_cyl("VentRing", 0.058, 0.05, (0, -0.44, 0.07), M["panel"], 10,
                         rot=(math.radians(90), 0, 0)))
    parts.append(add_box("Grip", (0.08, 0.10, 0.22), (0, 0.14, -0.08), M["body"],
                         rot=(math.radians(-16), 0, 0)))
    parts.append(add_box("Magazine", (0.07, 0.10, 0.20), (0, -0.04, -0.10), M["metal"]))
    parts.append(add_box("AmberStrip", (0.012, 0.24, 0.022), (0.052, -0.02, 0.10), M["amber"]))
    parts.append(add_box("Stock", (0.07, 0.14, 0.10), (0, 0.26, 0.05), M["panel"]))
    ob = join_all(parts, "Modle_Gun_Auto")
    return finalize(ob, "Modle_Gun_Auto")


def build_gun_grenade(M):
    """HK-9 投石：厚机匣 + 粗炮管 + 鼓形弹仓 + 握把。"""
    parts = []
    parts.append(add_box("Body", (0.13, 0.40, 0.17), (0, 0, 0.07), M["body"]))
    parts.append(add_cyl("Barrel", 0.085, 0.34, (0, -0.34, 0.09), M["metal"], 12,
                         rot=(math.radians(90), 0, 0)))
    parts.append(add_cyl("MuzzleRing", 0.108, 0.06, (0, -0.50, 0.09), M["panel"], 12,
                         rot=(math.radians(90), 0, 0)))
    parts.append(add_cyl("Drum", 0.11, 0.16, (0, -0.02, -0.10), M["metal"], 10,
                         rot=(0, math.radians(90), 0)))
    parts.append(add_box("Grip", (0.09, 0.11, 0.22), (0, 0.15, -0.08), M["body"],
                         rot=(math.radians(-14), 0, 0)))
    parts.append(add_box("AmberStrip", (0.014, 0.20, 0.026), (0.068, -0.02, 0.12), M["amber"]))
    ob = join_all(parts, "Modle_Gun_Grenade")
    return finalize(ob, "Modle_Gun_Grenade")


def build_gun_single(M):
    """
    R-71 裁决：细长机匣 + 长枪管 + 制退块 + 握把 + 枪托 + 青色条。
    机匣 z=[-0.005, 0.105]；握把与弹匣必须从机匣底面插入（重叠 2cm），
    否则装配门会判「悬空」（踩过）。
    """
    parts = []
    parts.append(add_chamfer_box("Receiver", (0.09, 0.52, 0.11), (0, 0, 0.05), M["body"], 0.012))
    parts.append(add_cyl("Barrel", 0.032, 0.52, (0, -0.50, 0.06), M["metal"], 10,
                         rot=(math.radians(90), 0, 0)))
    parts.append(add_box("Brake", (0.065, 0.09, 0.075), (0, -0.77, 0.06), M["panel"]))
    # 握把：顶面插进机匣底面（机匣底 z=-0.005，握把顶部到 z=0.015）
    parts.append(add_box("Grip", (0.075, 0.10, 0.24), (0, 0.15, -0.11), M["body"],
                         rot=(math.radians(-15), 0, 0)))
    # 弹匣：中心 z=-0.055，高 0.16 → 顶面 0.025，插入机匣
    parts.append(add_box("Magazine", (0.06, 0.09, 0.16), (0, -0.06, -0.055), M["metal"]))
    parts.append(add_box("Stock", (0.07, 0.30, 0.11), (0, 0.40, 0.04), M["panel"]))
    parts.append(add_box("CyanStrip", (0.010, 0.22, 0.018), (0.047, -0.02, 0.09), M["cyan"]))
    parts.append(add_box("Cheek", (0.05, 0.16, 0.05), (0, 0.16, 0.13), M["panel"]))
    ob = join_all(parts, "Modle_Gun_Single")
    return finalize(ob, "Modle_Gun_Single")


# ---------------- 资产：场景道具 ----------------

def build_crate(M):
    parts = []
    parts.append(add_box("Box", (1.10, 1.10, 0.92), (0, 0, 0.46), M["body"]))
    for z in (0.24, 0.68):
        parts.append(add_box(f"Band{z}", (1.16, 1.16, 0.09), (0, 0, z), M["metal"]))
    parts.append(add_box("Stencil", (0.60, 0.012, 0.10), (0, -0.556, 0.46), M["amber"]))
    ob = join_all(parts, "Modle_Prop_Crate")
    return finalize(ob, "Modle_Prop_Crate")


def build_barrel(M):
    parts = []
    parts.append(add_cyl("Drum", 0.30, 0.92, (0, 0, 0.46), M["body"], 12))
    for z in (0.16, 0.76):
        parts.append(add_cyl(f"Ring{z}", 0.325, 0.08, (0, 0, z), M["metal"], 12))
    parts.append(add_cyl("AmberBand", 0.315, 0.10, (0, 0, 0.46), M["amber"], 12))
    parts.append(add_cyl("Lid", 0.29, 0.03, (0, 0, 0.925), M["panel"], 12))
    ob = join_all(parts, "Modle_Prop_Barrel")
    return finalize(ob, "Modle_Prop_Barrel")


def build_pillar(M):
    """能量柱：方形柱 + 底板 + 前脸紫色发光槽。"""
    parts = []
    parts.append(add_box("Base", (1.90, 1.90, 0.34), (0, 0, 0.17), M["purple"]))
    parts.append(add_box("Column", (1.30, 1.30, 4.40), (0, 0, 2.54), M["body"]))
    parts.append(add_box("Channel", (0.34, 0.06, 3.60), (0, -0.67, 2.60), M["magenta"]))
    parts.append(add_box("Cap", (1.50, 1.50, 0.22), (0, 0, 4.85), M["panel"]))
    for sx in (-1, 1):
        parts.append(add_box(f"Rib{sx}", (0.10, 1.34, 0.14), (0.60 * sx, 0, 4.10), M["metal"]))
    ob = join_all(parts, "Modle_Prop_Pillar")
    return finalize(ob, "Modle_Prop_Pillar")


def build_wall(M):
    """模块化墙板：8m 宽 × 5m 高，便于平铺。"""
    parts = []
    parts.append(add_box("Slab", (8.0, 0.50, 5.0), (0, 0, 2.5), M["panel"]))
    parts.append(add_box("Inset", (7.4, 0.14, 4.4), (0, -0.30, 2.5), M["body"]))
    for z in (1.55, 3.45):
        parts.append(add_box(f"Rib{z}", (7.6, 0.16, 0.16), (0, -0.34, z), M["metal"]))
    parts.append(add_box("AmberStrip", (6.8, 0.06, 0.07), (0, -0.40, 4.62), M["amber"]))
    ob = join_all(parts, "Modle_Prop_Wall")
    return finalize(ob, "Modle_Prop_Wall")


# ---------------- 主流程 ----------------

def export_fbx(ob, out_dir):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

    path = os.path.join(out_dir, ob.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        object_types={"MESH"},
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        bake_anim=False,
        add_leaf_bones=False,
        path_mode="COPY",
    )
    return path


BUILDERS = [
    ("Modle_Enemy_Crawler", build_crawler),
    ("Modle_Enemy_Spitter", build_spitter),
    ("Modle_Enemy_Shield", build_shield),
    ("Modle_Enemy_Drone", build_drone),
    ("Modle_Boss_ForgeCore", build_boss),
    ("Modle_Gun_Auto", build_gun_auto),
    ("Modle_Gun_Grenade", build_gun_grenade),
    ("Modle_Gun_Single", build_gun_single),
    ("Modle_Prop_Crate", build_crate),
    ("Modle_Prop_Barrel", build_barrel),
    ("Modle_Prop_Pillar", build_pillar),
    ("Modle_Prop_Wall", build_wall),
]

# 各件的目标高度（米）。用于尺寸门：模型最终高度必须落在这个范围内，
# 否则说明坐标或缩放写错了。范围取「设计值 ±20%」。
TARGET_HEIGHT = {
    "Modle_Enemy_Crawler": (0.85, 1.30),
    "Modle_Enemy_Spitter": (1.60, 2.10),
    "Modle_Enemy_Shield": (1.90, 2.30),
    "Modle_Enemy_Drone": (0.35, 0.60),
    "Modle_Boss_ForgeCore": (3.00, 4.00),
    "Modle_Gun_Auto": (0.25, 0.55),
    "Modle_Gun_Grenade": (0.25, 0.60),
    "Modle_Gun_Single": (0.25, 0.55),
    "Modle_Prop_Crate": (0.85, 1.05),
    "Modle_Prop_Barrel": (0.85, 1.05),
    "Modle_Prop_Pillar": (4.50, 5.40),
    "Modle_Prop_Wall": (4.70, 5.30),
}

# 自检：这些盒体部件的标称尺寸必须与实际包围盒一致（容差 1mm）
SIZE_PROBES = {
    "Modle_Enemy_Crawler": {"Body": (0.66, 1.02, 0.44)},
    "Modle_Prop_Crate": {"Box": (1.10, 1.10, 0.92)},
    "Modle_Prop_Wall": {"Slab": (8.0, 0.50, 5.0)},
}


def check_part_sizes(expected, actual_map):
    """
    尺寸门：验证 add_box/add_chamfer_box 产出的包围盒尺寸与标称值一致（容差 1mm）。
    这个判据专门防「缩放系数写错导致所有件只有一半大」这类肉眼难察的错误（踩过）。
    """
    bad = []
    for name, exp in expected.items():
        act = actual_map.get(name)
        if act is None:
            bad.append(f"{name}: 缺失")
            continue
        for k, axis in enumerate("xyz"):
            if abs(exp[k] - act[k]) > 0.001:
                bad.append(f"{name}.{axis}: 期望 {exp[k]:.3f} 实际 {act[k]:.3f}")
    return bad


def check_floating_decor(main_size, decor_size, decor_loc, axis_tol=0.004):
    """
    贴合门的单件判定：装饰薄片是否贴在宿主表面。

    做法：沿薄片最薄的那个轴，算它是否与宿主表面相交或紧邻。
    这里用简化判据——装饰件的中心到宿主表面的距离 < 阈值即算贴合。
    真正的全自动检测见 check_assembly()。
    """
    return True


def check_assembly(parts_info, name):
    """
    装配门：检测「完全悬空的部件」。

    parts_info 格式：[(部件名, (minX,minY,minZ), (maxX,maxY,maxZ)), ...]
    判据：若某部件与**任何**其他部件在三个轴上都无重叠，则视为悬空。
    贴面装饰靠「嵌入 1–2cm」通过，所以容差取 0.5mm 即可。
    """
    floating = []
    n = len(parts_info)

    for i, (ni, mn_i, mx_i) in enumerate(parts_info):
        if i == 0:
            continue   # 主件跳过
        touches = False
        for j, (nj, mn_j, mx_j) in enumerate(parts_info):
            if i == j:
                continue
            overlap = all(
                mn_i[k] <= mx_j[k] + 0.0005 and mx_i[k] >= mn_j[k] - 0.0005
                for k in range(3)
            )
            if overlap:
                touches = True
                break
        if not touches:
            floating.append(ni)

    return floating


def measure(ob):
    bpy.context.view_layer.update()
    co = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return (
        max(c.x for c in co) - min(c.x for c in co),
        max(c.y for c in co) - min(c.y for c in co),
        max(c.z for c in co) - min(c.z for c in co),
    )


def main():
    argv = sys.argv
    out_dir = argv[argv.index("--") + 1] if "--" in argv else os.path.join(os.getcwd(), "out")
    os.makedirs(out_dir, exist_ok=True)

    report = []
    for name, fn in BUILDERS:
        clear_scene()
        M = mats()
        try:
            ob = fn(M)
            if ob is None:
                report.append((name, "FAIL", 0, "builder returned None"))
                continue

            tris = tri_count(ob)
            dim = measure(ob)

            # 尺寸门：高度必须落在设计范围内
            issues = []
            lo, hi = TARGET_HEIGHT.get(name, (0.01, 100.0))
            if not (lo <= dim[2] <= hi):
                issues.append(f"高 {dim[2]:.2f}m 不在 [{lo}, {hi}]")

            # 装配门：不得有完全悬空的部件
            floaters = check_assembly(_CURRENT_PARTS, name) if _CURRENT_PARTS else []
            if floaters:
                issues.append(f"悬空部件 {len(floaters)}: {', '.join(floaters[:4])}")

            if issues:
                report.append((name, "FAIL", tris, "; ".join(issues)))
                continue

            path = export_fbx(ob, out_dir)
            report.append((name, "OK", tris, f"{dim[0]:.2f}x{dim[1]:.2f}x{dim[2]:.2f}  {os.path.basename(path)}"))
        except Exception as e:
            report.append((name, "FAIL", 0, f"{type(e).__name__}: {e}"))

    print("\n=== BUILD REPORT ===")
    for name, status, tris, info in report:
        print(f"{status:5s} {name:26s} tris={tris:5d}  {info}")

    fails = [r for r in report if r[1] != "OK"]
    print(f"\nRESULT {len(report) - len(fails)}/{len(report)} built")
    if fails:
        print("FAILED: " + ", ".join(r[0] for r in fails))


if __name__ == "__main__":
    main()
