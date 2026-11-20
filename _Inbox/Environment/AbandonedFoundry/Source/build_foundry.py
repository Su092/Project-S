import bpy, math, json, os, csv, random
import numpy as np
from mathutils import Vector
from pathlib import Path
ROOT=Path(r'E:\S\_Inbox\Environment\AbandonedFoundry')
RES=Path(r'E:\S\Assets\_Game\Res')
for p in [ROOT/'Source',ROOT/'Reports',ROOT/'Previews',RES/'Model/Environment/AbandonedFoundry',RES/'Texture/Environment/AbandonedFoundry']:
 p.mkdir(parents=True,exist_ok=True)
MODEL=RES/'Model/Environment/AbandonedFoundry'; TEX=RES/'Texture/Environment/AbandonedFoundry'
random.seed(42)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene; scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
cols={}
for n in ['MODULES','LAYOUT','COLLISION','LOD','DECORATION','REFERENCE']:
 c=bpy.data.collections.new(n);scene.collection.children.link(c);cols[n]=c
def link(o,col):
 for c in list(o.users_collection):c.objects.unlink(o)
 cols[col].objects.link(o)
# Shared textures: periodic noise, stains, scratches; URP mask R metallic G AO B unused A smoothness.
N=1024; yy,xx=np.mgrid[0:N,0:N];u=xx/N;v=yy/N
rng=np.random.default_rng(25)
noise=rng.random((N,N))
macro=(np.sin(u*math.tau*3+np.sin(v*math.tau*2)) + np.cos(v*math.tau*5+np.sin(u*math.tau*4)))/4+.5
palette={'Concrete':(.29,.33,.35),'Steel':(.13,.19,.23),'Rust':(.26,.13,.065),'Trim':(.22,.27,.29),'Orange':(.7,.16,.025),'Cyan':(.02,.55,.65)}
materials={}
def png(name,arr):
 im=bpy.data.images.new(name,width=N,height=N,alpha=True)
 im.colorspace_settings.name='Non-Color' if name.endswith(('_Normal','_Mask')) else 'sRGB'
 im.pixels.foreach_set(arr.astype(np.float32).ravel());im.filepath_raw=str(TEX/(name+'.png'));im.file_format='PNG';im.save();return im
for key,base in palette.items():
 col=np.ones((N,N,4),dtype=np.float32); rough=np.clip(.5+.25*macro+.1*noise,0,1)
 stain=np.clip((macro-.6)*.15,0,.07)
 for i,c in enumerate(base):col[:,:,i]=np.clip(c*(.9+.08*noise+.05*macro-stain),0,1)
 if key in ['Steel','Trim']:
  rust=np.clip((macro-.72)*.35,0,.08)
  for i,c in enumerate((.35,.135,.045)):col[:,:,i]=col[:,:,i]*(1-rust)+rust*c
 if key=='Concrete':
  cracks=(np.abs(np.sin(u*math.tau*7 + .4*np.sin(v*math.tau*6)))<.018)&(macro>.52)
  col[cracks,:3]*=.5
 if key=='Trim':
  # Four 1m-high strips: steel flange, hazard strip, ribbed metal, oxidized edge.
  stripe=((xx+yy)//64)%2
  region=(yy>=256)&(yy<512)
  for i,c in enumerate((.58,.32,.055)):col[:,:,i][region]=np.where(stripe[region],c,.045)
  rib=(yy>=512)&(yy<768)&((xx%64)<8);col[rib,:3]*=.4
 al=png('T_Foundry_'+key+'_BaseColor',col)
 h=macro*.04+noise*.008
 dx=np.roll(h,-1,axis=1)-np.roll(h,1,axis=1);dy=np.roll(h,-1,axis=0)-np.roll(h,1,axis=0)
 normal=np.ones_like(col);normal[:,:,0]=.5-dx*2;normal[:,:,1]=.5-dy*2;normal[:,:,2]=1
 nm=png('T_Foundry_'+key+'_Normal',normal);nm.colorspace_settings.name='Non-Color'
 mask=np.ones_like(col);mask[:,:,0]=0 if key=='Concrete' else .75;mask[:,:,1]=1-stain*.4;mask[:,:,2]=0;mask[:,:,3]=1-rough
 png('T_Foundry_'+key+'_Mask',mask)
 mat=bpy.data.materials.new('MAT_Foundry_'+key);mat.use_nodes=True;nodes=mat.node_tree.nodes;bs=nodes.get('Principled BSDF')
 bs.inputs['Base Color'].default_value=(*base,1);bs.inputs['Roughness'].default_value=.8;bs.inputs['Metallic'].default_value=0 if key=='Concrete' else .65
 t=nodes.new('ShaderNodeTexImage');t.image=al;mat.node_tree.links.new(t.outputs['Color'],bs.inputs['Base Color'])
 nt=nodes.new('ShaderNodeTexImage');nt.image=nm;nn=nodes.new('ShaderNodeNormalMap');nn.inputs['Strength'].default_value=.4
 mat.node_tree.links.new(nt.outputs['Color'],nn.inputs['Color']);mat.node_tree.links.new(nn.outputs['Normal'],bs.inputs['Normal'])
 if key in ['Orange','Cyan']:
  bs.inputs['Emission Color'].default_value=(*base,1);bs.inputs['Emission Strength'].default_value=2
 materials[key]=mat
# Part definitions: box/cylinder/wedge/mesh. UVs are physical meters / 4.
def box(p,s,m='Steel',bevel=.025,detail=False):return dict(type='box',p=p,s=s,m=m,b=bevel,detail=detail)
def cyl(p,r,h,m='Steel',n=16):return dict(type='cyl',p=p,r=r,h=h,m=m,n=n)
def wedge(p,w,l,h,m='Concrete'):return dict(type='wedge',p=p,w=w,l=l,h=h,m=m)
KIT={};MAN=[];instances=[]
def mesh_part(part,lod=0):
 typ=part['type'];p=part['p']
 if typ=='box':
  bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=bpy.context.object;o.dimensions=part['s']
  bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
  if part.get('b',0) and (lod==0 or part.get('keep',False)):
   mod=o.modifiers.new('Edge wear','BEVEL');mod.width=part['b'];mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)
 elif typ=='cyl':
  bpy.ops.mesh.primitive_cylinder_add(vertices=part['n'] if lod==0 else max(8,part['n']//2),radius=part['r'],depth=part['h'],location=p);o=bpy.context.object
 elif typ=='wedge':
  w,l,h=part['w'],part['l'],part['h']
  vs=[(0,0,0),(w,0,0),(w,l,0),(0,l,0),(0,l,h),(w,l,h)]
  fs=[(0,3,2,1),(0,1,5,4),(0,4,3),(1,2,5),(3,4,5,2)]
  me=bpy.data.meshes.new('wedge');me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new('wedge',me);scene.collection.objects.link(o);o.location=p
 else:
  me=bpy.data.meshes.new('shape');me.from_pydata(part['verts'],[],part['faces']);me.update();o=bpy.data.objects.new('shape',me);scene.collection.objects.link(o);o.location=p
 if typ=='mesh':
  import bmesh
  bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
 o.data.materials.append(materials[part.get('m','Steel')])
 while o.data.uv_layers:o.data.uv_layers.remove(o.data.uv_layers[0])
 import bmesh
 bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.00001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
 uv=o.data.uv_layers.new(name='UV0')
 for face in o.data.polygons:
  normal=face.normal;axis=max(range(3),key=lambda k:abs(normal[k]));axes=[k for k in range(3) if k!=axis]
  for idx in face.loop_indices:
   co=o.data.vertices[o.data.loops[idx].vertex_index].co+o.location
   uv.data[idx].uv=(co[axes[0]]/4,co[axes[1]]/4)
 return o
def join_parts(parts,name,lod,col):
 obs=[mesh_part(dict(p,keep=(i%3==0)),lod) for i,p in enumerate(parts) if not(lod and p.get('detail',False))]
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join();o=obs[0];o.name=name
 scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
 # independent lightmap UV, packed by Blender
 o.data.uv_layers.new(name='UV1');o.data.uv_layers.active_index=1
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT');o.data.uv_layers.active_index=0
 link(o,col);o.data.calc_loop_triangles();return o
def add(cat,name,parts,coll=None,pivot='bottom joining corner',note=''):
 key=cat+'_'+name;base='ENV_Foundry_'+key
 obs=[join_parts(parts,base+'_LOD'+str(l),l,'MODULES' if l==0 else 'LOD') for l in range(2)]
 proxies=[]
 for i,p in enumerate(coll or []):
  q=dict(p);q['b']=0
  o=mesh_part(q);scene.cursor.location=(0,0,0);bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
  o.name='COL_Foundry_'+key+'_'+str(i).zfill(2)+('_Move' if p.get('use')=='movement' else '_Both')
  link(o,'COLLISION');o.hide_render=True;o.display_type='WIRE';proxies.append(o)
 # Preserve the artist-edited, fixed wall when regenerating the kit.
 revised=ROOT/'Source/Wall_Straight4_Revised.blend'
 if key=='Wall_Straight4' and revised.exists():
  keys=[base+'_LOD0',base+'_LOD1','COL_Foundry_Wall_Straight4_00_Both']
  with bpy.data.libraries.load(str(revised),link=False) as (available,loaded):
   loaded.objects=list(keys)
  for target,source in zip(obs+proxies,loaded.objects):
   if source is None:raise RuntimeError('Revised wall source missing required mesh')
   target.data=source.data.copy()
   for mi,mat in enumerate(target.data.materials):
    token=mat.name.split('.')[0].replace('MAT_Foundry_','')
    if token in materials:target.data.materials[mi]=materials[token]
   target.data.calc_loop_triangles()
   bpy.data.objects.remove(source,do_unlink=True)
  note='Artist revised wall; explicit triangles; watertight union; precise collision.'
 KIT[key]=dict(obs=obs,col=proxies)
 dims=list(obs[0].dimensions)
 row=dict(key=key,dimensions=[round(float(x),3) for x in dims],pivot=pivot,tris0=len(obs[0].data.loop_triangles),tris1=len(obs[1].data.loop_triangles),materials=[x.name for x in obs[0].data.materials],density='256 px/m planar; trim bands same metric UV',lod0=obs[0].name,lod1=obs[1].name,collision=[o.name for o in proxies],note=note)
 MAN.append(row)
 print('MODULE',key,row['tris0'],row['tris1'],flush=True)
def B(p,s,m='Concrete'):return box(p,s,m,0)
# Ground: upper walking plane at z=0, pivot at top joining corner documented.
for name,mat in [('Concrete','Concrete'),('Metal','Steel'),('Damaged','Concrete')]:
 ps=[box((2,2,-.125),(4,4,.25),mat,0)]
 if name=='Metal':
  for x in [.25,3.75]:ps.append(box((x,2,-.015),(.08,3.8,.03),'Trim',0))
 if name=='Damaged':
  for x,y in [(1,1),(3,2),(2.5,3.5)]:ps.append(box((x,y,.001),(.6,.25,.002),'Rust',0,True))
 add('Floor',name,ps,[B((2,2,-.125),(4,4,.25))],pivot='top joining corner (surface z=0)',note='Minimal planar module; LOD reduction exempt')
for name,s,mat in [('Border',(4,.25,.1),'Trim'),('Transition',(4,.25,.02),'Trim'),('Drain',(4,.5,.08),'Steel')]:
 ps=[box((s[0]/2,s[1]/2,s[2]/2),s,mat,0)]
 if name=='Drain':
  for x in np.arange(.1,4,.25):ps.append(box((x,.25,.045),(.07,.44,.08),'Trim',.01))
 add('Floor',name,ps,[],note='flush decorative overlay, no movement collision')
# Wall modules exactly x 0..4, y 0..1, z 0..4
wall=[box((2,.5,2),(4,1,4),'Concrete',0),box((2,.98,1.1),(3.8,.04,1.6),'Steel')]
for x in [.25,3.75]:
 wall += [box((x,.5,2),(.5,1,4),'Trim'),box((x,.99,3.3),(.3,.03,.5),'Rust',0,True)]
for x in [.75,1.5,2.5,3.25]:
 wall.append(box((x,.975,2),(.125,.05,3.5),'Trim',.008))
add('Wall','Straight4',wall,[B((2,.5,2),(4,1,4))])
for name in ['InnerCorner','OuterCorner']:
 ps=[box((2,.5,2),(4,1,4),'Concrete',0),box((.5,2.5,2),(1,3,4),'Concrete',0)]
 add('Wall',name,ps,[B((2,.5,2),(4,1,4)),B((.5,2.5,2),(1,3,4))],note='L footprint; rotate for inner/outer use. Minimal structure exception.')
for name,s in [('EndCap',(1,1,4)),('Pillar',(1,1,4)),('TopCap',(4,1,.25))]:
 add('Wall',name,[box(tuple(x/2 for x in s),s,'Trim')],[B(tuple(x/2 for x in s),s)])
ps=[box((.5,.5,2),(1,1,4),'Trim'),box((7.5,.5,2),(1,1,4),'Trim'),box((4,.5,3.5),(6,1,1),'Concrete',0)]
add('Wall','WideDoor8',ps,[B((.5,.5,2),(1,1,4)),B((7.5,.5,2),(1,1,4)),B((4,.5,3.5),(6,1,1))],note='6m wide x 3m high OPEN passage')
ps=[box((3,.25,1.5),(6,.5,3),'Steel')]
for x in np.arange(.25,6,.5):ps.append(box((x,.52,1.5),(.08,.08,2.9),'Trim'))
add('Wall','ClosedDoor',ps,[B((3,.25,1.5),(6,.5,3))])
# Platforms start bottom corner at z=0, top at 2.5.
ps=[box((2,2,2.25),(4,4,.5),'Steel',0)]
for x in [.25,3.75]:
 for y in [.25,3.75]:ps.append(box((x,y,1),(.5,.5,2),'Trim'))
add('Platform','Deck4',ps,[B((2,2,2.25),(4,4,.5))]+[B((x,y,1),(.5,.5,2)) for x in [.25,3.75] for y in [.25,3.75]])
add('Platform','Support',[box((.25,.25,1),(.5,.5,2),'Trim')],[B((.25,.25,1),(.5,.5,2))])
add('Platform','Edge',[box((2,.125,.25),(4,.25,.5),'Trim')],[])
steps=[];coll=[]
for i in range(10):
 h=(i+1)*.25;p=(2,(i+.5)*.5,h/2);s=(4,.5,h)
 steps.append(box(p,s,'Steel',0));steps.append(box((2,i*.5+.06,h-.025),(4,.12,.05),'Trim',0,True));coll.append(B(p,s))
ramp=wedge((0,0,0),4,5,2.5);ramp['use']='movement'
# shot-only steps; movement ramp distinct
for c in coll:c['use']='shot'
add('Platform','Stairs4x5',steps,coll+[ramp],note='10 x .25m risers; Move ramp vs Shot step colliders')
add('Platform','Ramp4x8',[wedge((0,0,0),4,8,2.5,'Steel')],[wedge((0,0,0),4,8,2.5)],note='17.35 degree slope; exact wedge collider; minimal geometry exception')
add('Platform','Landing',[box((2,2,-.125),(4,4,.25),'Steel',0)],[B((2,2,-.125),(4,4,.25))],pivot='top joining corner')
rail=[box((2,.125,1.05),(4,.25,.1),'Trim')]
for x in [.125,2,3.875]:rail.append(box((x,.125,.55),(.12,.12,1.1),'Trim'))
rail.append(box((2,.125,.5),(4,.08,.08),'Steel'))
add('Platform','Rail4',rail,[B((2,.125,1.05),(4,.25,.1))]+[B((x,.125,.55),(.12,.12,1.1)) for x in [.125,2,3.875]])
add('Platform','RailCorner',rail+[box((.125,2,1.05),(.25,4,.1),'Trim'),box((.125,3.875,.55),(.12,.12,1.1),'Trim')],[B((2,.125,1.05),(4,.25,.1)),B((.125,2,1.05),(.25,4,.1))])
add('Platform','RailEnd',[box((.125,.125,.55),(.25,.25,1.1),'Trim')],[B((.125,.125,.55),(.25,.25,1.1))])
# Props bottom center, industrial frame details.
for name,w,d,h,mat in [('CrateLow',2,2,1,'Steel'),('CrateHigh',2,2,2,'Steel'),('CrateGroup',4,2,2,'Steel'),('ShortWall',4,1,1,'Concrete'),('Cabinet',2,1,2,'Steel'),('MachineBase',3,2,1,'Steel')]:
 ps=[box((0,0,h/2),(w,d,h),mat)]
 for x in [-w/2+.09,w/2-.09]:
  for y in [-d/2+.09,d/2-.09]:ps.append(box((x,y,h/2),(.18,.18,h),'Trim'))
 for z in [.12,h-.12]:
  for y in [-d/2,d/2]:ps.append(box((0,y,z),(w,.08,.16),'Trim'))
 if name=='Cabinet':
  for z in np.arange(.4,1.7,.2):ps.append(box((0,-.515,z),(1.4,.03,.07),'Trim',0,True))
 if name=='MachineBase':
  ps += [cyl((0,0,1.2),.65,.4,'Rust',16)]
 add('Cover',name,ps,[B((0,0,h/2),(w,d,h))]+([cyl((0,0,1.2),.65,.4,'Steel',16)] if name=='MachineBase' else []),pivot='bottom center')
for name,centers in [('Barrel',[(0,0)]),('BarrelGroup',[(-.6,0),(.6,0),(0,1)])]:
 ps=[];cs=[]
 for x,y in centers:
  ps.append(cyl((x,y,.5),.5,1,'Rust',20));cs.append(cyl((x,y,.5),.5,1,'Rust',16))
  for z in [.15,.85]:ps.append(cyl((x,y,z),.515,.08,'Trim',20))
 add('Cover',name,ps,cs,pivot='bottom center')
# Central disk quarter sectors. top=.25; perimeter slope ring r9..10, leaving radius9 clear.
def sector(r0,r1,h0,h1,a0,a1,n=12):
 vs=[];fs=[]
 for i in range(n+1):
  a=math.radians(a0+(a1-a0)*i/n)
  for r,h in [(r0,0),(r1,0),(r0,h0),(r1,h1)]:vs.append((r*math.cos(a),r*math.sin(a),h))
 for i in range(n):
  k=i*4;j=k+4;fs += [(k+2,k+3,j+3,j+2),(k,j,j+1,k+1),(k+1,j+1,j+3,k+3)]
  if r0>0:fs.append((k,k+2,j+2,j))
 fs += [(0,1,3,2),(4*n,4*n+2,4*n+3,4*n+1)]
 return dict(type='mesh',p=(0,0,0),verts=vs,faces=fs,m='Steel')
for name,p in [('Quarter',sector(0,9,.25,.25,0,90)),('Edge',sector(9,10,.25,0,0,90))]:
 add('Core',name,[p],[p],pivot='circle center at ground; quarter radial exception',note='Preserve shared sector vertices at every LOD; minimum topology exception')
add('Core','Entry',[wedge((0,0,0),6,1,.25,'Steel')],[wedge((0,0,0),6,1,.25)],note='optional straight entrance replaces corresponding edge segment')
for name in ['EnergyBase','EnergyPillar','EnergyRuin']:
 ps=[];cs=[]
 if name=='EnergyBase':
  ps=[cyl((0,0,.25),1.25,.5,'Steel',24),cyl((0,0,.55),.85,.1,'Trim',24)];cs=[cyl((0,0,.25),1.25,.5,'Steel',24),cyl((0,0,.55),.85,.1,'Steel',24)]
 elif name=='EnergyPillar':
  ps=[cyl((0,0,1.75),.55,3.5,'Steel',24)]
  for z in [.2,1,2.5,3.3]:ps.append(cyl((0,0,z),.75,.2,'Trim',24))
  for a in range(0,360,90):
   x,y=.56*math.cos(math.radians(a)),.56*math.sin(math.radians(a));ps.append(box((x,y,1.8),(.12,.12,2.5),'Cyan',0))
  cs=[cyl((0,0,1.75),.55,3.5,'Steel',24)]+[cyl((0,0,z),.75,.2,'Steel',24) for z in [.2,1,2.5,3.3]]
 else:
  ps=[cyl((0,0,.35),.55,.7,'Rust',16),box((.6,0,.2),(.8,.5,.4),'Trim')];cs=[cyl((0,0,.35),.55,.7,'Steel',16),B((.6,0,.2),(.8,.5,.4))]
 add('Core',name,ps,cs,pivot='bottom center',note='independent replaceable prefab / collider hierarchy')
add('Core','RingMark',[dict(sector(8.65,8.8,.265,.265,0,90),m='Orange')],[],pivot='circle center',note='decal-like ring; minimal topology exception')
add('Core','LightStrip',[box((2,.06,.015),(4,.12,.03),'Cyan',0)],[])
# Decor pipe mesh via rings, preserves endpoint diameter across LOD.
def pipepath(points,r=.35,n=12):
 vs=[];fs=[]
 for i,p in enumerate(points):
  tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
  tangent.normalize();axis=tangent.cross(Vector((0,0,1)))
  if axis.length<.01:axis=tangent.cross(Vector((0,1,0)))
  axis.normalize();b=tangent.cross(axis)
  for k in range(n):
   q=Vector(p)+r*(axis*math.cos(k*math.tau/n)+b*math.sin(k*math.tau/n));vs.append(tuple(q))
 for i in range(len(points)-1):
  for k in range(n):a=i*n+k;b=i*n+(k+1)%n;fs.append((a,b,b+n,a+n))
 fs += [tuple(reversed(range(n))),tuple((len(points)-1)*n+k for k in range(n))]
 return dict(type='mesh',p=(0,0,0),verts=vs,faces=fs,m='Rust')
add('Decor','PipeStraight',[pipepath([(0,0,.5),(4,0,.5)])],[],pivot='pipe endpoint at ground datum',note='overhead only; no collider; simple topology LOD exception')
add('Decor','PipeElbow',[pipepath([(math.sin(a),1-math.cos(a),.5) for a in np.linspace(0,math.pi/2,9)])],[],pivot='pipe endpoint at ground datum')
add('Decor','PipeTee',[pipepath([(0,0,.5),(4,0,.5)]),pipepath([(2,0,.5),(2,2,.5)])],[])
add('Decor','PipeBracket',[box((.25,.25,1),(.5,.5,2),'Trim'),box((.75,.25,1.8),(1.5,.5,.25),'Trim')],[])
ps=[box((1,.5,.75),(2,1,1.5),'Steel')]
for z in np.arange(.2,1.5,.15):ps.append(box((1,-.03,z),(1.8,.08,.06),'Trim',.01))
add('Decor','Vent',ps,[])
add('Decor','CableTray',[box((2,.25,.125),(4,.5,.25),'Steel')]+[box((x,.25,.26),(.08,.5,.08),'Trim',0,True) for x in np.arange(.1,4,.3)],[])
add('Decor','WallLight',[box((.25,.15,.5),(.5,.3,1),'Steel'),box((.25,-.025,.5),(.3,.05,.65),'Cyan',0)],[])
add('Decor','Sign',[box((1,.05,.5),(2,.1,1),'Trim'),box((1,-.015,.5),(1.8,.03,.8),'Orange',0)],[])
add('Decor','Scrap',[box((0,0,.1),(1,.5,.2),'Rust'),box((.4,.1,.2),(.5,.8,.2),'Steel')],[],pivot='bottom center')
add('Vista','Chimney',[cyl((0,0,10),2,20,'Concrete',20)]+[cyl((0,0,z),2.15,.25,'Trim',20) for z in [2,6,10,14,18]],[],pivot='bottom center',note='outside playable boundary')
add('Vista','Furnace',[box((0,0,4),(8,6,8),'Steel'),box((0,-3.01,2),(4,.1,3),'Orange'),cyl((0,0,10),1.3,4,'Rust',16)],[],pivot='bottom center')
add('Vista','Factory',[box((0,0,5),(16,10,10),'Steel')]+[box((x,-5.01,6),(2,.08,3),'Trim') for x in [-6,-2,2,6]],[],pivot='bottom center')
# Shot purpose rename
for o in KIT['Platform_Stairs4x5']['col']:
 if not o.name.endswith('_Move'):o.name=o.name.replace('_Both','_Shot')
# Validate and export modules prior to layout; FBX axis conversion standard -Z forward +Y up.
for row in MAN:
 item=KIT[row['key']];row['collision']=[x.name for x in item['col']]
 assert row['tris0']<=3500,row
 for o in item['obs']:
  assert all(abs(s-1)<1e-5 for s in o.scale)
 bpy.ops.object.select_all(action='DESELECT')
 for o in item['obs']+item['col']:o.hide_set(False);o.select_set(True)
 bpy.context.view_layer.objects.active=item['obs'][0]
 bpy.ops.export_scene.fbx(filepath=str(MODEL/(row['key']+'.fbx')),use_selection=True,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,object_types={'MESH'},path_mode='STRIP')
 for o in item['obs']+item['col']:o.hide_set(True);o.hide_render=True
json.dump({'modules':MAN},open(ROOT/'Reports/modules.json','w'),indent=2)
# save module source with visible LOD0 spaced for overview, export origins already zero.
for i,(key,item) in enumerate(KIT.items()):
 for o in item['obs']+item['col']:o.location=(i%8*20,i//8*24,0)
 item['obs'][0].hide_set(False);item['obs'][0].hide_render=False
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Source/Foundry_Modules.blend'))
# Scene assembled from linked mesh instances
for item in KIT.values():
 for o in item['obs']+item['col']:o.hide_set(True);o.hide_render=True
def place(key,x,y,z=0,rot=0,group='Architecture'):
 idx=len(instances);rec=dict(key=key,x=x,y=y,z=z,rotation=rot,group=group);instances.append(rec)
 src=KIT[key]['obs'][0];o=bpy.data.objects.new('INST_'+str(idx)+'_'+key,src.data.copy());cols['LAYOUT'].objects.link(o);o.location=(x,y,z);o.rotation_euler[2]=math.radians(rot)
 return o
# 25x25 exact floor tiles, wall inner edge 50m. + Blender Y = Unity -Z.
for i in range(25):
 for j in range(25):place('Floor_'+('Metal' if (i in [11,12,13] or j in [11,12,13]) else ('Damaged' if (i*7+j)%19==0 else 'Concrete')),-50+i*4,-50+j*4,group='Floor')
for side in range(4):
 a=side*math.pi/2
 for i in range(25):
  x,y=-50+i*4,-51
  X,Y=x*math.cos(a)-y*math.sin(a),x*math.sin(a)+y*math.cos(a)
  for z in [0,4]:place('Wall_Straight4',X,Y,z,side*90)
  place('Wall_TopCap',X,Y,8,side*90)
# Four low decks at cardinal sides, dimensions 12x8, inner edge 32, outer40.
for side in range(4):
 a=side*math.pi/2
 def P(key,x,y,z=0,r=0,g='HighDecks'):
  return place(key,x*math.cos(a)-y*math.sin(a)+(18 if side==2 else 0),x*math.sin(a)+y*math.cos(a),z,r+side*90,g)
 for x in [-6,-2,2]:
  for y in [-40,-36]:P('Platform_Deck4',x,y)
 # stairs from inward y=-27 to deck edge -32, rotate 180; ramp outward? side approaches side edge x±6
 P('Platform_Stairs4x5',-6,-36,0,90) # along -X extends x=-11, top x=-11 wrong
 # overwrite last place needs ascending towards deck: start -11, -36 rotated -90 => +X
 instances.pop();o=cols['LAYOUT'].objects[-1] if False else None
 # remove explicitly last matching instance by index
 old=bpy.data.objects.get('INST_'+str(len(instances))+'_Platform_Stairs4x5')
 if old:bpy.data.objects.remove(old,do_unlink=True)
 P('Platform_Stairs4x5',-11,-32,0,-90)
 P('Platform_Ramp4x8',14,-36,0,90)
 # front rail split to leave sight windows and side access clear
 P('Platform_Rail4',-2,-32.25,2.5)
 for x in [-6,-2,2]:P('Platform_Rail4',x,-40,2.5)
 P('Cover_Cabinet',0,-38,2.5,0)
# exactly 18 cover groups, asymmetric, beyond r14.
cover_positions=[(-18,-14),(-27,-12),(-34,-22),(16,-15),(27,-18),(34,-7),(-17,12),(-28,17),(-36,8),(17,12),(27,20),(35,29),(-12,26),(6,24),(12,29),(-23,-26),(20,-28),(29,6)]
coverkeys=['CrateLow','CrateHigh','BarrelGroup','ShortWall','Cabinet','MachineBase']
for i,(x,y) in enumerate(cover_positions):
 place('Cover_'+coverkeys[i%6],x,y,0,[0,90,0,0,90,0][i%6],'CoverGroup_'+str(i+1).zfill(2))
 if i%3==0:place('Cover_Barrel',x+3,y,0,0,'CoverGroup_'+str(i+1).zfill(2))
for r in [0,90,180,270]:
 place('Core_Quarter',0,0,0,r,'BossCore');place('Core_Edge',0,0,0,r,'BossCore');place('Core_RingMark',0,0,0,r,'BossCore')
for x,y in [(-10,-10),(10,-10),(-10,10),(10,10)]:
 place('Core_EnergyBase',x,y,0,0,'Energy');place('Core_EnergyPillar',x,y,.6,0,'Energy')
for side in range(4):
 a=side*math.pi/2
 def D(key,x,y,z,r=0):return place(key,x*math.cos(a)-y*math.sin(a),x*math.sin(a)+y*math.cos(a),z,r+side*90,'Facility')
 for x in range(-48,48,4):D('Decor_PipeStraight',x,-49,6.5)
 for x in [-36,-20,20,36]:D('Decor_WallLight',x,-49,3);D('Decor_Vent',x,-50,5)
 for x in [-28,28]:D('Vista_Chimney',x,-59,0);D('Vista_Factory',x,-65,0)
 D('Vista_Furnace',0,-60,0)
# Spawn remains open at Blender (0,38,1.2), looks toward -Y.
spawns=[]
for side in range(4):
 a=side*math.pi/2
 for x in [-36,-18,18,36]:
  y=-45;spawns.append([round(x*math.cos(a)-y*math.sin(a),3),round(x*math.sin(a)+y*math.cos(a),3),0])
layout={'instances':instances,'spawns':[{'x':p[0],'y':p[1],'z':p[2]} for p in spawns],'player':{'x':0,'y':38,'z':1.2}}
json.dump(layout,open(ROOT/'Reports/layout.json','w'),indent=2)
# reference human, non exported
bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=8,radius=1,location=(0,38,.9));ref=bpy.context.object;ref.scale=(.3,.3,.9);ref.name='REF_Human_1p8m';link(ref,'REFERENCE');ref.hide_render=True
# scene lighting and cameras
world=bpy.data.worlds.new('Foundry overcast');scene.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.16,.21,.28,1);world.node_tree.nodes['Background'].inputs[1].default_value=.8
ld=bpy.data.lights.new('Cold daylight','SUN');lo=bpy.data.objects.new('Cold daylight',ld);scene.collection.objects.link(lo);lo.rotation_euler=(.45,-.5,-.4);ld.energy=2
for x,y in [(-10,-10),(10,-10),(-10,10),(10,10)]:
 ld=bpy.data.lights.new('Core glow','POINT');ld.energy=350;ld.color=(.05,.6,1);ld.shadow_soft_size=2
 lo=bpy.data.objects.new('Core glow',ld);scene.collection.objects.link(lo);lo.location=(x,y,3)
scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
def cam(name,pos,target,ortho=None,lens=24):
 d=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();d.lens=lens;d.clip_end=400
 if ortho:d.type='ORTHO';d.ortho_scale=ortho
 return o
cams=[cam('Top',(0,0,130),(0,0,0),175),cam('Player',(0,38,1.7),(0,0,2),lens=22),cam('Arena',(80,94,75),(0,0,0),lens=39)]
scene.camera=cams[2]
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Source/Foundry_Layout.blend'))
# Scene FBX excludes collision and editor cameras; modular json+prefabs is authoritative
bpy.ops.object.select_all(action='DESELECT')
for o in cols['LAYOUT'].objects:o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(ROOT/'Foundry_Layout.fbx'),use_selection=True,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_anim=False,object_types={'MESH'},path_mode='STRIP')
for camera in cams:
 scene.camera=camera;scene.render.filepath=str(ROOT/'Previews'/(camera.name+'.png'));bpy.ops.render.render(write_still=True)
# overview of modules in source positions
for o in cols['LAYOUT'].objects:o.hide_render=True
for item in KIT.values():item['obs'][0].hide_render=False
scene.camera=cam('Modules',(155,-120,170),(65,75,0),210);scene.render.filepath=str(ROOT/'Previews/Modules.png');bpy.ops.render.render(write_still=True)
# collision overlay in layout; render proxies as translucent wireframe
for item in KIT.values():item['obs'][0].hide_render=True
for o in cols['LAYOUT'].objects:o.hide_render=False
wire=bpy.data.materials.new('QA Collision');wire.diffuse_color=(.1,1,.1,1);wire.use_nodes=True
bs=wire.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(.05,1,.05,1);bs.inputs['Emission Color'].default_value=(.05,1,.05,1);bs.inputs['Emission Strength'].default_value=1
for inst in instances:
 if inst['group']=='Floor':continue
 for src in KIT[inst['key']]['col']:
  ob=bpy.data.objects.new('QA_'+src.name,src.data.copy());cols['COLLISION'].objects.link(ob);ob.location=(inst['x'],inst['y'],inst['z']);ob.rotation_euler.z=math.radians(inst['rotation']);ob.data.materials.clear();ob.data.materials.append(wire)
  mod=ob.modifiers.new('Wire','WIREFRAME');mod.thickness=.035
scene.camera=cams[2];scene.render.filepath=str(ROOT/'Previews/CollisionOverlay.png');bpy.ops.render.render(write_still=True)
with open(ROOT/'Reports/ModuleManifest.csv','w',newline='',encoding='utf-8-sig') as f:
 w=csv.writer(f);w.writerow(['key','dimensions_m','pivot','tris_LOD0','tris_LOD1','ratio','materials','texel_density','LOD0','LOD1','collision','notes'])
 for r in MAN:w.writerow([r['key'],r['dimensions'],r['pivot'],r['tris0'],r['tris1'],round(r['tris1']/r['tris0'],3),';'.join(r['materials']),r['density'],r['lod0'],r['lod1'],';'.join(r['collision']),r['note']])
print('FOUNDRY_BUILD_DONE',len(MAN),len(instances),flush=True)
