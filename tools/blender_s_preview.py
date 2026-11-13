"""
Project S — 资产预览渲染

对每个 FBX 渲染一张 3/4 视角预览图，用于人工核对「模型是否像参考图」。
判据：能看出轮廓与配色，不是黑图、不是空场。

用法：
  blender.exe --background --python blender_s_preview.py -- <models_dir> <out_dir>
"""

import bpy
import os
import sys
import math
from mathutils import Vector


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def setup_render(res=512):
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = res
    sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False

    # 世界背景：中性灰，和参考图一致
    world = bpy.data.worlds.new("W")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.18, 0.19, 0.21, 1.0)
        bg.inputs[1].default_value = 1.0

    # 主光 + 补光
    for name, loc, rot, energy in (
        ("Key", (4.5, -5.5, 5.0), (math.radians(48), 0, math.radians(38)), 900),
        ("Fill", (-5.0, -3.0, 2.5), (math.radians(70), 0, math.radians(-52)), 260),
    ):
        bpy.ops.object.light_add(type="AREA", location=loc, rotation=rot)
        ob = bpy.context.active_object
        ob.name = name
        ob.data.energy = energy
        ob.data.size = 6.0

    # 相机：3/4 俯视
    bpy.ops.object.camera_add(location=(3.4, -4.4, 2.6))
    cam = bpy.context.active_object
    cam.data.lens = 62
    sc.camera = cam
    return cam


def frame_object(cam, ob):
    """把相机对准目标，并按包围盒尺寸调整距离。"""
    bpy.context.view_layer.update()

    # 注意：ob 自己可能是 Mesh，children_recursive 不含自身，
    # 早先只取子孙导致「目标集合为空 → 相机不动 → 渲染出纯背景」（踩过）。
    rs = []
    if ob.type == "MESH":
        rs.append(ob)
    rs.extend([r for r in ob.children_recursive if r.type == "MESH"])
    if not rs:
        return

    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for r in rs:
        for c in r.bound_box:
            w = r.matrix_world @ Vector(c)
            for k in range(3):
                mn[k] = min(mn[k], w[k])
                mx[k] = max(mx[k], w[k])

    center = (mn + mx) * 0.5
    size = mx - mn
    radius = max(size.x, size.y, size.z) * 0.5
    if radius < 1e-4:
        radius = 0.5

    dist = radius * 3.4 + 0.6
    d = Vector((0.62, -0.72, 0.36)).normalized()
    cam.location = center + d * dist
    direction = (center - cam.location).normalized()
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()

    # 近远裁剪切到合适范围，否则小物体可能被裁掉
    cam.data.clip_start = max(0.01, dist * 0.01)
    cam.data.clip_end = dist * 10.0


def import_fbx(path):
    bpy.ops.import_scene.fbx(filepath=path)


def check_not_blank(path):
    """
    判据：预览图不能是纯色。
    用 Blender 自带图像 API 采样若干像素，算亮度方差；接近 0 说明只渲染到背景。
    这条检查很关键：相机没对准时渲染依然「成功」，只靠退出码会漏掉（踩过）。
    """
    img = bpy.data.images.load(path)
    w, h = img.size
    px = list(img.pixels)   # RGBA float, 行优先
    if w == 0 or h == 0:
        return False, 0.0

    n = w * h
    step = max(1, n // 2500)   # 采样约 2500 点
    vals = []
    for i in range(0, n, step):
        o = i * 4
        lum = 0.2126 * px[o] + 0.7152 * px[o + 1] + 0.0722 * px[o + 2]
        vals.append(lum)

    if len(vals) < 2:
        return False, 0.0
    mean = sum(vals) / len(vals)
    var = sum((v - mean) ** 2 for v in vals) / len(vals)
    bpy.data.images.remove(img)
    return var > 0.0008, var   # 阈值经验值：纯背景约 <0.0005，有物体时通常 >0.003


def main():
    argv = sys.argv
    idx = argv.index("--") if "--" in argv else len(argv)
    args = argv[idx + 1:]
    if len(args) < 2:
        print("usage: blender_s_preview.py -- <models_dir> <out_dir>")
        return
    models_dir, out_dir = args[0], args[1]
    os.makedirs(out_dir, exist_ok=True)

    files = sorted(f for f in os.listdir(models_dir) if f.lower().endswith(".fbx"))
    print(f"previewing {len(files)} models")

    ok = 0
    for f in files:
        clear()
        cam = setup_render()
        import_fbx(os.path.join(models_dir, f))

        # FBX 导入后会有个根 Empty 或直接是 Mesh
        roots = [o for o in bpy.context.scene.objects if o.type in ("MESH", "EMPTY")]
        if not roots:
            print(f"SKIP {f}: no objects")
            continue

        target = next((o for o in roots if o.type == "MESH"), roots[0])
        frame_object(cam, target)

        name = os.path.splitext(f)[0]
        out = os.path.join(out_dir, name + ".png")
        bpy.context.scene.render.filepath = out
        bpy.ops.render.render(write_still=True)

        good, var = check_not_blank(out)
        if good:
            print(f"RENDER {name}  var={var:.5f}  OK")
            ok += 1
        else:
            print(f"RENDER {name}  var={var:.5f}  BLANK/EMPTY  <-- 相机或导入有问题")

    print(f"\nRESULT {ok}/{len(files)} rendered (non-blank)")


if __name__ == "__main__":
    main()
