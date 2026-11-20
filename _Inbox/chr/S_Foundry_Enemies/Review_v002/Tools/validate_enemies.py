import bpy,json,math,sys,traceback
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(r'E:\arjun988 blender\S_Foundry_Enemies')
MAN=json.loads((ROOT/'production_manifest.json').read_text())
def bbox(obs):
 pts=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box]
 return [max(p[i] for p in pts)-min(p[i] for p in pts) for i in range(3)]
def check(i):
 name,dims,budget,res,clips=MAN['assets'][i];out=ROOT/name
 bpy.ops.wm.open_mainfile(filepath=str(out/(name+'.blend')))
 arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 r={'asset':name,'checks':{},'actions':{},'normals_note':'Degenerate triangles / manifold edges measured; visual normals and Unity tangent basis require engine review.'}
 lods=[bpy.data.objects.get('SM_'+name+'_LOD'+str(j)) for j in range(3)]
 for o in lods:
  me=o.data;me.calc_loop_triangles()
  import bmesh
  bm=bmesh.new();bm.from_mesh(me)
  deg=sum(f.calc_area()<1e-10 for f in bm.faces);nonman=sum(not e.is_manifold for e in bm.edges);bm.free()
  unweighted=sum(not v.groups or abs(sum(g.weight for g in v.groups)-1)>.0001 for v in me.vertices)
  uv=me.uv_layers.active
  r['checks'][o.name]={'triangles':len(me.loop_triangles),'degenerate_faces':deg,'nonmanifold_edges':nonman,'bad_weights':unweighted,'uv_outside_01':sum(any(c<0 or c>1 for c in p.uv) for p in uv.data),'material_slots':len(me.materials),'negative_scale':any(x<0 for x in o.scale)}
 r['checks']['textures_missing']=[im.filepath for im in bpy.data.images if im.source=='FILE' and not Path(bpy.path.abspath(im.filepath)).exists()]
 for clip in clips:
  act=bpy.data.actions.get('AN_'+name+'_'+clip);arm.animation_data.action=act
  frames=[1,int(act.frame_range[1])];poses=[]
  for f in frames:
   bpy.context.scene.frame_set(f);bpy.context.view_layer.update();poses.append({pb.name:pb.matrix.copy() for pb in arm.pose.bones})
  delta=max(abs(poses[0][bn][a][c]-poses[1][bn][a][c]) for bn in poses[0] for a in range(4) for c in range(4))
  root_delta=max(abs(poses[0]['root'][a][c]-poses[1]['root'][a][c]) for a in range(4) for c in range(4))
  r['actions'][clip]={'frames':frames,'loop':bool(act.get('loop')),'endpoint_max_matrix_delta':delta,'root_delta':root_delta}
 arm.animation_data.action=None
 for pb in arm.pose.bones:pb.matrix_basis=Matrix.Identity(4)
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.fbx(filepath=str(out/'Exports'/(name+'_Model.fbx')),automatic_bone_orientation=False)
 imported=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.endswith('_LOD0')]
 bpy.context.view_layer.update();actual=bbox(imported)
 r['roundtrip']={'fbx_imported':True,'dimensions':actual,'dimension_error':[abs(a-b) for a,b in zip(actual,dims)],'axis_test':'Blender inverse FBX roundtrip only; Unity +Z must be tested in Unity.','armatures':sum(o.type=='ARMATURE' for o in bpy.context.scene.objects)}
 # Actual independent reimport saved for reviewer.
 bpy.ops.wm.save_as_mainfile(filepath=str(out/'Exports'/(name+'_ReimportValidation.blend')))
 # Reimport one locomotion or hover clip and one attack clip.
 r['animation_fbx_imports']={}
 for clip in clips:
  bpy.ops.wm.read_factory_settings(use_empty=True)
  bpy.ops.import_scene.fbx(filepath=str(out/'Exports'/('AN_'+name+'_'+clip+'.fbx')))
  rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
  r['animation_fbx_imports'][clip]={'rigs':len(rigs),'actions':len(bpy.data.actions),'bones':len(rigs[0].data.bones) if rigs else 0}
 r['technical_pass']=all(v.get('nonmanifold_edges',0)==0 and v.get('degenerate_faces',0)==0 and v.get('bad_weights',0)==0 and v.get('uv_outside_01',0)==0 for v in r['checks'].values() if isinstance(v,dict)) and max(r['roundtrip']['dimension_error'])<.002
 r['verdict']='NO-SHIP: requires visual animation/LOD/collision and Unity verification; see pending items in qa_metrics.json'
 (out/'validation_report.json').write_text(json.dumps(r,indent=2))
 print('VALIDATED',name,r['technical_pass'],flush=True)
if __name__=='__main__':
 for i in range(9):
  try:check(i)
  except Exception:
   (ROOT/('VALIDATION_ERROR_'+str(i)+'.txt')).write_text(traceback.format_exc());traceback.print_exc()
