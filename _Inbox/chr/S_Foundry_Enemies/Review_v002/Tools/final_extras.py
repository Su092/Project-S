import bpy,json,math,sys
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(r'E:\arjun988 blender\S_Foundry_Enemies');sys.path.insert(0,str(ROOT))
from build_enemies import meshpart,atlas,select,setup_preview
MAN=json.loads((ROOT/'production_manifest.json').read_text())
# Separate debris assets: four sealed, thick curved shell sections; no explosion logic or launch velocities.
for name in ['PressureRunner','PressureRunner_Elite_TriplePressure']:
 out=ROOT/name
 bpy.ops.wm.open_mainfile(filepath=str(out/(name+'.blend')))
 col=bpy.data.collections.new('COL_FragmentAssets');bpy.context.scene.collection.children.link(col)
 mat=bpy.data.materials.get('MAT_PressureRunner');obs=[]
 shell=next(o for o in bpy.context.scene.objects if o.name.startswith('SM_PearVessel') and 'Editable' in o.name)
 pts=[v.co for v in shell.data.vertices];lo=Vector([min(v[i] for v in pts) for i in range(3)]);hi=Vector([max(v[i] for v in pts) for i in range(3)])
 factor=Vector([(hi[i]-lo[i])/d for i,d in enumerate([.92,.92,.96])]);rawlo=Vector((-.46,-.46,.33))
 for i in range(4):
  d=dict(n=name+'_Fragment_'+str(i),p=(0,0,.83),s=(1,1,1),cell=1,bone='root',typ='sector',start=i*math.pi/2+.03,end=(i+1)*math.pi/2-.03,profile=[(-.35,.30),(-.20,.40),(.14,.46),(.35,.40)],thick=.035)
  o=meshpart(d,0,col,mat)
  for v in o.data.vertices:v.co=(v.co-rawlo)*factor+lo
  obs.append(o)
 select(obs)
 bpy.ops.export_scene.fbx(filepath=str(out/'Exports'/(name+'_FragmentAssets.fbx')),use_selection=True,bake_anim=False,axis_forward='-Z',axis_up='Y')
 bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'_FragmentAssets.blend')))
# Single shared metric stage. Models assembled from actual finished LOD0 data.
bpy.ops.wm.read_factory_settings(use_empty=True);sc=bpy.context.scene;cols={}
for n in ['Geo','Preview','Reference']:
 c=bpy.data.collections.new('COL_'+n);sc.collection.children.link(c);cols[n]=c
offset=0;labels=[]
for name,dims,_,_,_ in MAN['assets']:
 path=ROOT/name/(name+'.blend')
 with bpy.data.libraries.load(str(path),link=False) as (src,dst):
  dst.objects=['SM_'+name+'_LOD0']
 ob=dst.objects[0];cols['Geo'].objects.link(ob);ob.parent=None
 for m in list(ob.modifiers):ob.modifiers.remove(m)
 ob.location=(offset,0,dims[2]/2+.40 if name.startswith('Ring') else 0)
 labels.append((name,offset));offset+=dims[0]/2+.65
 # Next spacing accounts for following asset width through independent bounds below.
 ob['dimensions_brief']=dims
# Reposition using left/right actual bounds.
cursor=0
for ob in cols['Geo'].objects:
 bpy.context.view_layer.update();w=ob.dimensions.x;ob.location.x=cursor+w/2;cursor+=w+.65
 font=bpy.data.curves.new('Label','FONT');font.body=ob.name.replace('SM_','').replace('_LOD0','').replace('_Elite_','\n');font.size=.14;txt=bpy.data.objects.new('LABEL_'+ob.name,font);cols['Preview'].objects.link(txt)
 txt.location=(ob.location.x-w/2,-.6,-.35);txt.rotation_euler=(math.pi/2,0,0)
mat=bpy.data.materials.new('MAT_Ruler');mat.diffuse_color=(.2,.5,.7,1)
for n,p,s in [('1m',(cursor+.5,0,.5),(1,1,1)),('HumanTorso',(cursor+2,0,1.15),(.42,.22,.6)),('HumanHead',(cursor+2,0,1.68),(.23,.23,.24)),('HumanLegL',(cursor+1.87,0,.45),(.15,.18,.9)),('HumanLegR',(cursor+2.13,0,.45),(.15,.18,.9))]:
 meshpart(dict(n=n,p=p,s=s,cell=0,bone='root',typ='box'),2,cols['Reference'],mat)
setup_preview(sc,cols,[cursor+3,6,6])
sc.render.resolution_x=2800;sc.render.resolution_y=1000
cam=sc.camera;cam.data.type='ORTHO';cam.data.ortho_scale=cursor+4;cam.location=((cursor+3)/2,-30,8);target=Vector(((cursor+3)/2,0,2.1));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
for o in cols['Preview'].objects:
 if o.type=='LIGHT':o.location.x+=(cursor+3)/2;o.data.energy*=6;o.data.size=12;o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
sc.render.filepath=str(ROOT/'AllEnemies_SameScale.png');bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'AllEnemies_SameScale.blend'))
