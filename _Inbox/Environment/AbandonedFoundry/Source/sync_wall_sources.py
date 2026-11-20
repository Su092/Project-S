import bpy,shutil
from pathlib import Path
R=Path(r'E:\S\_Inbox\Environment\AbandonedFoundry')
p=R/'Source/Foundry_Modules.blend';shutil.copy2(p,R/'Source/Foundry_Modules_PreWallFlickerFix.blend')
bpy.ops.wm.open_mainfile(filepath=str(p))
names=['ENV_Foundry_Wall_Straight4_LOD0','ENV_Foundry_Wall_Straight4_LOD1','COL_Foundry_Wall_Straight4_00_Both']
originals={n:bpy.data.objects.get(n) for n in names}
with bpy.data.libraries.load(str(R/'Source/Wall_Straight4_Revised.blend'),link=False) as (a,b):b.objects=list(names)
for name,obj in zip(names,b.objects):
 if originals[name]:originals[name].data=obj.data
 bpy.data.objects.remove(obj,do_unlink=True)
bpy.ops.wm.save_as_mainfile(filepath=str(p))
bpy.ops.wm.open_mainfile(filepath=str(R/'Source/Foundry_Layout.blend'))
layout=R/'Foundry_Layout.fbx';shutil.copy2(layout,R/'Source/Foundry_Layout_PreWallFlickerFix.fbx')
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.context.scene.objects:
 if o.type=='MESH' and o.name.startswith('INST_'):o.hide_set(False);o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(layout),use_selection=True,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_anim=False,object_types={'MESH'},path_mode='STRIP',use_triangles=True)
print('SOURCE_AND_LAYOUT_SYNCED')
