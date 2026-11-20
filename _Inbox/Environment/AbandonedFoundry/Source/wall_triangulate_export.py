import bpy,bmesh,json,shutil
from pathlib import Path
R=Path(r'E:\S\_Inbox\Environment\AbandonedFoundry')
src=R/'Source/Wall_Straight4_Revised.blend'
shutil.copy2(src,R/'Source/Wall_Straight4_PreFlickerFix.blend')
bpy.ops.wm.open_mainfile(filepath=str(src))
for o in bpy.context.scene.objects:
 if o.type!='MESH':continue
 bm=bmesh.new();bm.from_mesh(o.data)
 bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method='FIXED',ngon_method='EAR_CLIP')
 bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
 o.data.update();o.hide_set(False);o.select_set(True)
# Explicit source triangulation prevents FBX from choosing a different diagonal on thin/concave Boolean faces.
bpy.ops.export_scene.fbx(filepath=str(Path(r'E:\S\Assets\_Game\Res\Model\Environment\AbandonedFoundry\Wall_Straight4.fbx')),use_selection=True,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,bake_anim=False,object_types={'MESH'},path_mode='STRIP',use_triangles=True)
for o in bpy.context.scene.objects:
 if o.type=='MESH':o.hide_set(not o.name.endswith('_LOD0'));o.hide_render=not o.name.endswith('_LOD0')
bpy.ops.wm.save_as_mainfile(filepath=str(src))
print('TRIANGULATED_EXPORT_OK')
