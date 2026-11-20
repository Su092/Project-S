import bpy,sys,json,numpy as np
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).parent;sys.path.insert(0,str(ROOT))
from build_enemies import design,meshpart,select,export
import inspect,build_enemies
exec(inspect.getsource(build_enemies.meshpart).replace('n=[16,10,6][lod]','n=[16,10,4][lod]'),build_enemies.__dict__)
meshpart=build_enemies.meshpart
name='PressureRunner';out=ROOT/name
bpy.ops.wm.open_mainfile(filepath=str(out/(name+'.blend')))
arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
arm.animation_data.action=None
for pb in arm.pose.bones:pb.matrix_basis=Matrix.Identity(4)
b=design(name);pairs=[(Vector(p),arm.data.bones[bn].head_local) for bn,(p,parent) in b.bones.items() if bn!='root']
affine=[np.linalg.lstsq(np.array([[p[a],1] for p,q in pairs]),np.array([q[a] for p,q in pairs]),rcond=None)[0] for a in range(3)]
old=bpy.data.objects['SM_'+name+'_LOD2'];col=old.users_collection[0];mat=old.data.materials[0];obs=[]
for d in b.parts:
 if d.get('detail') or d['n'].startswith('Gauge'):continue
 # Inner liner is reduced in profile resolution; exterior panels retain thickness and articulation.
 if d['n']=='PearVessel':
  d=dict(d);d['profile']=[(-.50,.18),(-.20,.38),(.14,.435),(.46,.205)]
 ob=meshpart(d,2,col,mat)
 for v in ob.data.vertices:v.co=Vector([affine[a][0]*v.co[a]+affine[a][1] for a in range(3)])
 obs.append(ob)
select(obs);bpy.ops.object.join();ob=obs[0]
tri=ob.modifiers.new('Triangulate','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
ob.data.calc_loop_triangles();print('REBUILT_TRIANGLES',len(ob.data.loop_triangles),flush=True)
assert len(ob.data.loop_triangles)<=700
bpy.data.objects.remove(old,do_unlink=True);ob.name='SM_'+name+'_LOD2'
md=ob.modifiers.new('Rigid skin','ARMATURE');md.object=arm;ob.parent=arm
lods=[bpy.data.objects['SM_'+name+'_LOD'+str(i)] for i in range(3)]
clips=json.loads((ROOT/'production_manifest.json').read_text())['assets'][2][4]
export(b,arm,lods,[o for o in bpy.context.scene.objects if o.name.startswith('SOCKET_')],list(bpy.data.collections['COL_Collision'].objects),clips,out,name)
ob.hide_render=True;ob.hide_set(True)
met=json.loads((out/'qa_metrics.json').read_text());met['triangles'][2]=len(ob.data.loop_triangles);(out/'qa_metrics.json').write_text(json.dumps(met,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'.blend')))
import validate_enemies as v
v.check(2)
assert json.loads((out/'validation_report.json').read_text())['technical_pass']
code=(ROOT/'review_previews.py').read_text().replace("for spec in MAN['assets']:","for spec in [MAN['assets'][2]]:")
exec(compile(code,'review_previews.py','exec'),{'__name__':'__main__'})
