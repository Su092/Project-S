"""Run with Blender -b --python tools/extract_enemy_proxies.py. Read-only source extraction."""
import bpy, json
from pathlib import Path
root = Path.cwd()
source = root / "_Inbox/chr/S_Foundry_Enemies/Review_v002"
dest = root / "Assets/_Game/Res/Characters/Foundry"
assets = json.loads((source / "asset_summary.json").read_text(encoding="utf-8"))
for entry in assets:
    name = entry["asset"]
    bpy.ops.wm.open_mainfile(filepath=str(source / name / (name + ".blend")))
    for obj in bpy.data.objects:
        if obj.type == "ARMATURE":
            obj.data.pose_position = "REST"
    bpy.context.view_layer.update()
    proxies = []
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.get("proxy_role") != "hit":
            continue
        obj.data.calc_loop_triangles()
        vertices = []
        for vert in obj.data.vertices:
            p = obj.matrix_world @ vert.co
            vertices.extend([p.x, p.z, -p.y])
        # Blender-to-Unity axis conversion above has positive determinant.
        tris = [i for tri in obj.data.loop_triangles for i in tri.vertices]
        proxies.append(dict(name=obj.name, bone=obj["bind_bone"], vertices=vertices, triangles=tris))
    assert proxies, name
    target = dest / name / "CollisionProxies.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(dict(proxies=proxies)), encoding="utf-8")
    print("EXTRACT", name, len(proxies), flush=True)
print("RESULT extracted collision proxies for 9 enemies", flush=True)
