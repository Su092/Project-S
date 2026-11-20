import bpy,json,sys
from pathlib import Path
from mathutils import Matrix
ROOT=Path(r'E:\arjun988 blender\S_Foundry_Enemies');sys.path.insert(0,str(ROOT))
from build_enemies import export
man=json.loads((ROOT/'production_manifest.json').read_text())
for name,dims,budget,res,clips in man['assets']:
 out=ROOT/name;bpy.ops.wm.open_mainfile(filepath=str(out/(name+'.blend')))
 arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 import bmesh
 for ob in bpy.context.scene.objects:
  if ob.type!='MESH':continue
  bm=bmesh.new();bm.from_mesh(ob.data)
  internal=[f for f in bm.faces if sum(len(e.link_faces)>2 for e in f.edges)>=2]
  if internal:bmesh.ops.delete(bm,geom=internal,context='FACES_ONLY')
  loose=[e for e in bm.edges if not e.link_faces]
  if loose:bmesh.ops.delete(bm,geom=loose,context='EDGES')
  isolated=[v for v in bm.verts if not v.link_edges]
  if isolated:bmesh.ops.delete(bm,geom=isolated,context='VERTS')
  bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
 from build_enemies import design,animate,select
 from mathutils import Vector
 select([arm]);bpy.ops.object.mode_set(mode='EDIT')
 for eb in arm.data.edit_bones:eb.tail=eb.head+Vector((0,.12,0));eb.roll=0
 bpy.ops.object.mode_set(mode='OBJECT')
 arm.animation_data_clear()
 for act in list(bpy.data.actions):
  if act.name.startswith('AN_'+name+'_'):bpy.data.actions.remove(act)
 animate(design(name),arm,clips,Vector((1,1,1)))
 for act in bpy.data.actions:
  if act.get('loop'):
   for layer in act.layers:
    for strip in layer.strips:
     for bag in strip.channelbags:
      for fc in bag.fcurves:
       if len(fc.keyframe_points)>1:
        fc.keyframe_points[-1].co.y=fc.keyframe_points[0].co.y
        fc.keyframe_points[-1].handle_left.y=fc.keyframe_points[0].co.y
        fc.keyframe_points[-1].handle_right.y=fc.keyframe_points[0].co.y
 arm.animation_data.action=None
 for pb in arm.pose.bones:pb.matrix_basis=Matrix.Identity(4)
 lods=[bpy.data.objects.get('SM_'+name+'_LOD'+str(i)) for i in range(3)]
 metrics=json.loads((out/'qa_metrics.json').read_text())
 for o in lods:o.data.calc_loop_triangles()
 metrics['triangles']=[len(o.data.loop_triangles) for o in lods]
 (out/'qa_metrics.json').write_text(json.dumps(metrics,indent=2))
 sockets=[o for o in bpy.context.scene.objects if o.name.startswith('SOCKET_')]
 from build_enemies import design
 import numpy as np
 from mathutils import Vector
 des=design(name);pairs=[(Vector(p),arm.data.bones[bn].head_local) for bn,(p,parent) in des.bones.items() if bn!='root']
 affine=[np.linalg.lstsq(np.array([[p[a],1] for p,q in pairs]),np.array([q[a] for p,q in pairs]),rcond=None)[0] for a in range(3)]
 for ob in sockets:
  raw,bn=des.sockets[ob.name];pos=Vector([affine[a][0]*raw[a]+affine[a][1] for a in range(3)])
  bone=arm.data.bones[bn];ob.parent=arm;ob.parent_type='BONE';ob.parent_bone=bn
  tip=bone.matrix_local@Matrix.Translation((0,bone.length,0))
  ob.matrix_parent_inverse=tip.inverted();ob.matrix_basis=Matrix.Translation(pos)
 bpy.context.view_layer.update()
 proxies=list(bpy.data.collections.get('COL_Collision').objects)
 export(None,arm,lods,sockets,proxies,clips,out,name)
 for im in bpy.data.images:
  if im.source=='FILE':im.filepath=bpy.path.relpath(im.filepath,start=str(out))
 # Image-based final materials use relative external textures for portable delivery.
 bpy.context.scene.frame_set(1)
 bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'.blend')))
 print('FINALIZED',name,flush=True)
