import bpy
from pathlib import Path
R=Path(r'E:\S\_Inbox\Environment\AbandonedFoundry')
bpy.ops.wm.open_mainfile(filepath=str(R/'Source/Wall_Straight4_Revised.blend'))
scene=bpy.context.scene
cs={}
for n in ['MODULES','LOD','COLLISION','LAYOUT','DECORATION']:
 c=bpy.data.collections.get(n) or bpy.data.collections.new(n)
 if c.name not in scene.collection.children:scene.collection.children.link(c)
 cs[n]=c
for o in list(scene.objects):
 dest='COLLISION' if o.name.startswith('COL_') else 'LOD' if o.name.endswith('_LOD1') else 'MODULES'
 for c in list(o.users_collection):c.objects.unlink(o)
 cs[dest].objects.link(o)
 o.hide_render=dest!='MODULES';o.hide_set(dest!='MODULES')
for c in list(bpy.data.collections):
 if c.name not in cs and not c.objects:bpy.data.collections.remove(c)
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(R/'Source/Wall_Straight4_Revised.blend'))
print('SOURCE_READY')
