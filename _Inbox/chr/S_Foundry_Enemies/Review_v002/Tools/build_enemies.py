import bpy, math, json, os, sys, traceback
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path(r'E:\arjun988 blender\S_Foundry_Enemies')
MAN=json.loads((ROOT/'production_manifest.json').read_text())
TAU=math.tau
def log(s): print(s,flush=True)
def select(obs):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 if obs:bpy.context.view_layer.objects.active=obs[0]
def move(o,c):
 for old in list(o.users_collection):old.objects.unlink(o)
 c.objects.link(o)
def bounds(obs):
 pts=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box]
 return [min(p[i] for p in pts) for i in range(3)],[max(p[i] for p in pts) for i in range(3)]
def png(path,arr,linear=False):
 n=arr.shape[0];im=bpy.data.images.new(path.stem,width=n,height=n,alpha=True)
 im.colorspace_settings.name='Non-Color' if linear else 'sRGB'
 im.pixels.foreach_set(arr.astype(np.float32).ravel());im.filepath_raw=str(path);im.file_format='PNG';im.save()
 return im
def atlas(folder,name,n):
 folder.mkdir(parents=True,exist_ok=True)
 # 8 reusable construction trims; deliberate shared UVs, never unique-detail overlap.
 y,x=np.mgrid[0:n,0:n];u=(x%(n//4))/(n//4);v=(y%(n//2))/(n//2)
 cell=(x//(n//4))+(y//(n//2))*4
 pal=np.array([[.157,.176,.192],[.796,.780,.733],[.529,.290,.196],[1,.310,.196],[1,.831,.604],[.24,.27,.28],[.796,.780,.733],[.10,.12,.13]])
 base=np.ones((n,n,4));base[:,:,:3]=pal[cell]
 edge=np.minimum.reduce([u,1-u,v,1-v]);wear=(edge<.025)&(np.sin(u*117+v*81)>.25)&(cell<3)
 base[wear,:3]=[.529,.290,.196]
 # Only maintenance trim has machined grooves and bolt recesses.
 grooves=(cell==5)&((np.abs(v-.25)<.015)|(np.abs(v-.75)<.015))
 hazard=(cell==6)&(v>.3)&(v<.7)&(((u+v)*8%2)<1)
 base[hazard,:3]=pal[0];base[grooves,:3]*=.65
 normal=np.ones_like(base);normal[:,:,:3]=[.5,.5,1]
 normal[:,:,1][grooves]=.58
 normal[:,:,0][wear]=.53
 metal=np.ones_like(base);metal[:,:,:3]=np.where(np.isin(cell,[0,5,7]),.8,.05)[:,:,None]
 smooth=np.ones_like(base);smooth[:,:,:3]=np.where(cell==1,.3,.42)[:,:,None];smooth[wear,:3]=.18
 emiss=np.ones_like(base);emiss[:,:,:3]=0;mask=np.isin(cell,[3,4]);emiss[mask,:3]=pal[cell[mask]]
 emask=np.ones_like(base);emask[:,:,:3]=mask[:,:,None]
 packed=metal.copy();packed[:,:,1:3]=0;packed[:,:,3]=smooth[:,:,0]
 ims={}
 for key,a,lin in [('BaseColor',base,False),('Normal',normal,True),('Metallic',metal,True),('Smoothness',smooth,True),('Emission',emiss,False),('EmissionMask',emask,True),('MetallicSmoothness',packed,True)]:
  p=folder/('T_'+name+'_'+key+'.png')
  ims[key]=bpy.data.images.load(str(p),check_existing=True) if p.exists() else png(p,a,lin)
  ims[key].colorspace_settings.name='Non-Color' if lin else 'sRGB'
 mat=bpy.data.materials.new('MAT_'+name);mat.use_nodes=True
 nd=mat.node_tree.nodes;lk=mat.node_tree.links;bs=nd.get('Principled BSDF')
 for key,inp in [('BaseColor','Base Color'),('Metallic','Metallic'),('Emission','Emission Color')]:
  t=nd.new('ShaderNodeTexImage');t.image=ims[key];lk.new(t.outputs['Color'],bs.inputs[inp])
 t=nd.new('ShaderNodeTexImage');t.image=ims['Smoothness'];inv=nd.new('ShaderNodeMath');inv.operation='SUBTRACT';inv.inputs[0].default_value=1;lk.new(t.outputs['Color'],inv.inputs[1]);lk.new(inv.outputs[0],bs.inputs['Roughness'])
 t=nd.new('ShaderNodeTexImage');t.image=ims['Normal'];nn=nd.new('ShaderNodeNormalMap');lk.new(t.outputs['Color'],nn.inputs['Color']);lk.new(nn.outputs[0],bs.inputs['Normal'])
 bs.inputs['Emission Strength'].default_value=.8
 return mat
class Builder:
 def __init__(self,name,elite=False):
  self.name=name;self.elite=elite;self.parts=[];self.bones={'root':((0,0,0),None),'body':((0,0,.5),'root')};self.sockets={};self.legs=[]
 def bone(self,n,p,parent='body'): self.bones[n]=(p,parent);return n
 def part(self,n,p,s,cell=0,bone='body',typ='box',**kw):
  d=dict(n=n,p=p,s=s,cell=cell,bone=bone,typ=typ);d.update(kw);self.parts.append(d);return d
 def box(self,n,p,s,cell=0,bone='body',**kw):return self.part(n,p,s,cell,bone,**kw)
 def cyl(self,n,p,r,h,cell=0,bone='body',axis='Z',**kw):return self.part(n,p,(r,r,h),cell,bone,'cyl',axis=axis,**kw)
 def ring(self,n,p,rx,rz,thick,depth,cell=1,bone='body',**kw):return self.part(n,p,(rx,rz,depth),cell,bone,'ring',thick=thick,**kw)
 def bar(self,n,a,b,w,cell=0,bone='body'):
  a,b=Vector(a),Vector(b);return self.part(n,(a+b)/2,(w,w,(b-a).length),cell,bone,'bar',direction=list(b-a))
 def leg(self,n,hip,knee,foot,w=.1):
  up=self.bone(n+'_upper',hip);lo=self.bone(n+'_lower',knee,up);ft=self.bone(n+'_foot',foot,lo)
  self.bar(n+'_upper',hip,knee,w*1.3,0,up);self.bar(n+'_lower',knee,foot,w,0,lo)
  self.cyl(n+'_hinge',knee,w*.85,w*1.8,5,lo,axis='Y')
  self.box(n+'_shoe',(foot[0],foot[1]-.025,foot[2]),(w*2.1,w*3,w*.7),1,ft)
  self.legs.append((n,hip,knee,foot))
 def socket(self,n,p,bone='body'):self.sockets['SOCKET_'+n]=(p,bone)
def design(name):
 e='Elite' in name;b=Builder(name,e)
 if name.startswith('Slag'):
  b.bones['body']=((0,.05,.43),'root')
  b.box('Chassis',(0,.02,.43),(.61,1.08,.27))
  for i in range(3):
   p=b.box('RefractoryPlate_'+str(i),(0,-.35+i*.34,.54+i*.045),(.69,.30,.13),1);p['tilt']=.12
  for side in [-1,1]:
   for j in range(2):
    y=-.36+j*.77
    b.leg(('L' if side<0 else 'R')+str(j),(side*.28,y,.46),(side*.44,y-.09,.32),(side*.44,y+.13,.045),.09 if not e else .115)
   jn=b.bone('jaw_'+str(side),(side*.23,-.49,.35))
   b.box('ClampArm_'+str(side),(side*.27,-.72,.34),(.18,.49,.15),0,jn)
   b.box('ClampTip_'+str(side),(side*.20,-.92,.34),(.28,.12,.17),1,jn)
   for t in range(3):b.box('Tooth_'+str(side)+str(t),(side*.14,-.57-t*.12,.34),(.15,.065,.10),2,jn,detail=True)
  b.box('IdentitySlit',(0,-.58,.47),(.23,.025,.025),3)
  ex=b.bone('exhaust',(0,.57,.48));b.box('SlagOutlet',(0,.57,.47),(.30,.15,.13),5,ex)
  if e:
   b.bone('press',(0,.20,.65))
   for s in [-1,1]:b.box('PressColumn',(s*.25,.18,.72),(.11,.33,.34),0)
   b.box('PressBridge',(0,.18,.87),(.65,.34,.14),1)
   b.box('PressRam',(0,.18,.72),(.35,.25,.22),5,'press')
   b.box('WideClamp',(-.30,-.84,.35),(.27,.23,.20),1,'jaw_-1')
  b.socket('Attack',(0,-1,.34));b.socket('Core',(0,0,.5));b.socket('Exhaust',(0,.66,.47),'exhaust')
 elif name.startswith('Furnace'):
  b.bones['body']=((0,0,1.22),'root')
  b.box('LoadFrame',(0,.03,1.60),(1.04,.66,1.22),0)
  for i in [-1,0,1]:b.box('FurnaceDoor_'+str(i),(i*.29,-.33,1.58),(.26,.20,1.12),1)
  b.box('ShoulderBeam',(0,0,2.32),(1.42 if e else 1.25,.66,.20),0)
  b.box('RecessedHead',(0,-.17,2.19),(.43,.40,.20),7)
  b.box('ObservationSlit',(0,-.38,2.18),(.32,.026,.026),3)
  for side in [-1,1]:
   b.leg('L' if side<0 else 'R',(side*.32,.02,.98),(side*.33,-.02,.55),(side*.34,-.10,.09),.21)
   b.cyl('BackCylinder',(side*.30,.43,1.6),.10,.83,5)
   b.cyl('BackPiston',(side*.30,.43,2),.045,.37,0)
  b.bone('shield',(-.63,-.03,1.9));b.bar('ShieldArm',(-.63,-.03,1.9),(-.64,-.34,1.40),.22,0,'shield')
  for j in range(2 if e else 1):
   bn=b.bone('gate_'+str(j),(-.65,-.45,1.7-j*.45),'shield')
   b.box('ShieldFrame',(-.67,-.45,1.45+(j-.5)*.48 if e else 1.45),(.65,.19,.65 if e else 1.19),0,bn)
   b.box('ShieldCeramic',(-.67,-.56,1.45+(j-.5)*.48 if e else 1.45),(.53,.13,.53 if e else 1.07),1,bn)
  b.bone('hammer',(.61,0,1.72));b.cyl('HammerHousing',(.63,-.18,1.53),.20,.57,0,'hammer','Y')
  b.bone('ram',(.63,-.42,1.53),'hammer');b.cyl('RamRod',(.63,-.48,1.53),.09,.36,5,'ram','Y')
  b.box('RamHead',(.63,-.70,1.53),(.34,.17,.33),1,'ram')
  if e:b.box('CentralLock',(0,-.26,2.48),(.30,.28,.21),5)
  b.socket('Attack',(.63,-.82,1.53),'ram');b.socket('Shield',(-.67,-.65,1.45),'shield');b.socket('BackVent',(0,.43,1.88))
 elif name.startswith('Pressure'):
  b.bones['body']=((0,0,.68),'root')
  # Load-bearing liner and three independently opening upper ceramic panels.
  b.part('PearVessel',(0,0,.83),(.43,.43,1),0,typ='lathe',profile=[(-.50,.18),(-.40,.27),(-.20,.38),(.14,.435),(.35,.375),(.46,.205)])
  b.part('LowerRefractory',(0,0,.83),(1,1,1),1,typ='lathe',profile=[(-.49,.18),(-.40,.28),(-.20,.40),(.015,.45)])
  for i in range(3):
   a=-math.pi/2+i*TAU/3
   bn=b.bone('pressure_panel_'+str(i),(.42*math.cos(a),.42*math.sin(a),.90))
   b.part('PressurePanel_'+str(i),(0,0,.83),(1,1,1),1,bn,'sector',start=a-TAU/6+.018,end=a+TAU/6-.018,profile=[(.035,.455),(.14,.46),(.35,.40),(.46,.23)],thick=.025)
  # Gauges are protected instruments, no barrel-like projection toward player.
  for i in range(3 if e else 1):
   a=(-math.pi/2 if i==0 else -math.pi/2+(-.55 if i==1 else .55))
   x=0 if i==0 else (-.49 if i==1 else .49)
   z=1.15 if i==0 else 1.08
   b.cyl('GaugeCase_'+str(i),(x,-.43 if i==0 else -.22,z),.082,.045,0,axis='Y',rot_z=a+math.pi/2)
   b.cyl('GaugeDial_'+str(i),(x,-.456 if i==0 else -.246,z),.066,.012,1,axis='Y',rot_z=a+math.pi/2)
   needle=b.box('GaugeNeedle_'+str(i),(x,-.466 if i==0 else -.256,z+.012),(.012,.012,.075),2)
   needle['rot_y']=-.5+i*.35

  b.ring('WaistBand',(0,0,.82),.47,.47,.075,.12,6,axis='Z')
  b.box('BulgingHatch',(0,-.415,.94),(.33,.14,.33),5)
  b.bone('valve',(0,0,1.29));b.cyl('SafetyStem',(.08,0,1.36),.055,.20,5,'valve')
  b.ring('EccentricValve',(.08,0,1.46),.18,.18,.045,.06,2,'valve',axis='Z')
  for side in [-1,1]:
   bn=b.bone('vent_'+str(side),(side*.28,.02,1.22))
   b.cyl('VentPipe',(side*.28,.02,1.27),.075,.18,0)
   b.cyl('VentCap',(side*.28,.02,1.37),.085,.04,1,bn)
   b.cyl('PressureSeam',(side*.28,.02,1.345),.071,.014,4,bn)
  for i in range(3):
   a=TAU*i/3-math.pi/2;v=Vector((math.cos(a),math.sin(a),0))
   b.leg('Tripod'+str(i),v*.20+Vector((0,0,.48)),v*.37+Vector((0,0,.29)),v*.43+Vector((0,0,.045)),.09 if not e else .12)
  if e:
   for side in [-1,1]:
    b.part('BufferVessel',(side*.49,0,.91),(.23,.23,.60),1,typ='lathe',profile=[(-.30,.12),(-.21,.23),(.20,.23),(.30,.12)])
    b.cyl('ShortManifold',(side*.36,0,.94),.09,.24,5,axis='X')
    bn=b.bone('bufferlid_'+str(side),(side*.49,0,1.22))
    b.cyl('BufferCap',(side*.49,0,1.24),.14,.07,1,bn)
  b.socket('Blast',(0,0,.87));b.socket('Vent_L',(-.28,0,1.4),'vent_-1');b.socket('Vent_R',(.28,0,1.4),'vent_1')
 elif name.startswith('Ring'):
  b.bones['body']=((0,0,0),'root')
  b.ring('EllipticalFrame',(0,0,0),.78,.59,.13,.23,1)
  b.ring('InnerSteelLiner',(0,0,0),.66,.47,.035,.25,0)
  b.bone('aim',(0,0,0));b.box('GunPod',(0,-.06,0),(.46,.46,.30),0,'aim')
  b.cyl('Trunnion',(0,0,0),.075,1.15,5,'aim','X')
  b.bone('recoil',(0,-.23,0),'aim');b.cyl('Bore',(0,-.35,0),.12,.42,7,'recoil','Y')
  b.ring('CeramicMuzzle',(0,-.57,0),.16,.16,.045,.11,1,'recoil')
  for side in [-1,1]:
   b.box('Indicator',(side*.21,-.30,.09),(.035,.025,.06),3,'aim')
   b.ring('Duct',(side*.54,.03,-.37),.20,.20,.065,.29,0,axis='Z')
   b.bone('fan_'+str(side),(side*.54,.03,-.37))
   for ang in [0,math.pi/2]:
    p=b.box('Impeller',(side*.54,.03,-.37),(.28,.065,.035),5,'fan_'+str(side));p['rot_z']=ang
  b.cyl('OffsetLens',(.19,-.32,.17),.075,.07,5,'aim','Y')
  if e:
   b.ring('RearHalfFrame',(0,.25,.05),.87,.66,.10,.15,0,start=0,end=math.pi)
   for i in [-1,0,1]:b.box('CrownFin',(i*.25,.10,.62),(.16,.32,.23-abs(i)*.05),1)
   for s in [-1,1]:b.box('RecoilRail',(s*.29,-.06,0),(.075,.62,.09),5)
  b.socket('Muzzle',(0,-.65,0),'recoil');b.socket('Aim',(0,-.7,0),'aim')
  b.socket('Thruster_L',(-.54,.03,-.55));b.socket('Thruster_R',(.54,.03,-.55))
 else:
  b.bones['body']=((0,0,2.5),'root')
  b.part('CrucibleShell',(0,0,3.4),(1,1,1),0,typ='lathe',profile=[(-1.45,1.05),(-1.1,1.42),(.6,2.05),(1.5,2.1),(1.65,1.95)])
  for i in range(3):
   a=i*TAU/3
   b.part('RefractorySector'+str(i),(0,0,3.5),(1,1,1),1,typ='sector',start=a+.07,end=a+TAU/3-.07,profile=[(-.8,1.7),(.6,2.19),(1.4,2.22)],thick=.19)
  b.ring('RimLock',(0,0,5.05),2.12,2.12,.17,.28,5,axis='Z')
  for i in range(6):
   a=TAU*i/6;v=Vector((math.cos(a),math.sin(a),0))
   b.leg('LoadLeg'+str(i),v*1.0+Vector((0,0,1.8)),v*1.95+Vector((0,0,1.1)),v*2.25+Vector((0,0,.13)),.27 if i%2==0 else .22)
  b.ring('SuspensionRing',(0,0,1.35),1.55,1.55,.23,.38,0,axis='Z')
  b.bone('shock',(0,0,.9));b.ring('ImpactRing',(0,0,.84),1.46,1.46,.22,.27,5,'shock',axis='Z')
  # Layered core: crucible plug, heat tiles, induction ring and directed launcher.
  b.cyl('CoreWell',(0,-1.62,3.2),.92,.25,7,axis='Y')
  b.cyl('CoreCeramic',(0,-1.79,3.2),.67,.13,1,axis='Y')
  b.cyl('IncandescentCore',(0,-1.87,3.2),.42,.09,4,axis='Y')
  b.bone('launcher',(0,-1.9,3.2))
  b.ring('InductionCoil',(0,-1.9,3.2),.81,.81,.10,.11,2,'launcher')
  b.ring('FiringRing',(0,-2.01,3.2),.96,.96,.09,.14,5,'launcher')
  for i in range(3):
   a=(i*120+90)*math.pi/180;cx,cz=math.cos(a),math.sin(a)
   bn=b.bone('door_'+str(i),(cx*.92,-1.96,3.2+cz*.92))
   b.part('PetalDoor'+str(i),(0,-2.10,3.2),(.94,.94,.25),1,bn,'petal',thick=.915,start=a-math.pi/3+.015,end=a+math.pi/3-.015)
   lk=b.bone('lock_'+str(i),(cx*1.10,-2.04,3.2+cz*1.10))
   b.box('LockDog',(cx*1.02,-2.26,3.2+cz*1.02),(.23,.23,.25),5,lk)
   sn=b.bone('spike_'+str(i),(cx*.92,cz*.92,.9))
   b.cyl('GroundStamp',(cx*.92,cz*.92,.65),.22,.65,0,sn)
   b.socket('Spike_0'+str(i+1),(cx*.92,cz*.92,.12),sn)
  for s in [-1,1]:
   bn=b.bone('service_'+str(s),(s*1.25,0,1.9))
   b.box('MaintenanceDoor',(s*1.25,0,1.75),(.18,.8,.6),1,bn)
   b.cyl('ShortFlue',(s*.85,1.38,4.76),.30,1,0)
   b.box('CableInterface',(s*.8,1.48,2.6),(.6,.25,.6),5)
   b.socket('Summon_'+('L' if s<0 else 'R'),(s*1.6,0,1.3),bn)
   b.socket('Exhaust_'+('L' if s<0 else 'R'),(s*.85,1.38,5.3))
  b.box('RearServiceDeck',(0,1.7,2.2),(2,.55,.20),0)
  b.socket('Core',(0,-1.98,3.2))
  for i,s in enumerate(['L','C','R']):b.socket('Volley_'+s,((i-1)*.6,-2.15,3.2),'launcher')
  b.socket('Shockwave',(0,0,0),'shock')
 return b
def meshpart(d,lod,col,mat):
 typ=d['typ'];n=[16,10,6][lod]
 if typ in ['box','bar']:
  bpy.ops.mesh.primitive_cube_add(size=1,location=d['p']);o=bpy.context.object;o.dimensions=d['s']
  select([o]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
  if lod<2:
   m=o.modifiers.new('Manufactured chamfer','BEVEL');m.width=min(d['s'])*(.09 if lod==0 else .045);m.segments=1;bpy.ops.object.modifier_apply(modifier=m.name)
  if typ=='bar':o.rotation_euler=Vector(d['direction']).to_track_quat('Z','Y').to_euler()
 elif typ=='cyl':
  bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=d['s'][0],depth=d['s'][2],location=d['p']);o=bpy.context.object
  if d.get('axis')=='Y':o.rotation_euler[0]=math.pi/2
  if d.get('axis')=='X':o.rotation_euler[1]=math.pi/2
 else:
  vs=[];fs=[]
  if typ in ['ring','petal']:
   rx,rz,depth=d['s'];t=d['thick'];start=d.get('start',0);end=d.get('end',TAU);steps=n*2
   for y in [-depth/2,depth/2]:
    for inner in [False,True]:
     for i in range(steps+1):
      a=start+(end-start)*i/steps;vs.append(((rx-t*inner)*math.cos(a),y,(rz-t*inner)*math.sin(a)))
   k=steps+1
   for i in range(steps):
    fs.extend([(i,i+1,2*k+i+1,2*k+i),(k+i,3*k+i,3*k+i+1,k+i+1),(i,k+i,k+i+1,i+1),(2*k+i,2*k+i+1,3*k+i+1,3*k+i)])
   if abs(end-start-TAU)>.0001:fs.extend([(0,2*k,3*k,k),(steps,k+steps,3*k+steps,2*k+steps)])
   if d.get('axis')=='Z':vs=[(x,z,y) for x,y,z in vs]
  else:
   prof=d['profile'];sector=typ=='sector';steps=n if not sector else max(4,n//2)
   start=d.get('start',0);end=d.get('end',TAU)
   profiles=[prof,[(z,r-d.get('thick',.1)) for z,r in prof]] if sector else [prof]
   for profile in profiles:
    for z,r in profile:
     for i in range(steps+1):
      a=start+(end-start)*i/steps;vs.append((r*math.cos(a),r*math.sin(a),z))
   k=steps+1;layers=len(prof)
   for j in range(layers-1):
    for i in range(steps):fs.append((j*k+i,j*k+i+1,(j+1)*k+i+1,(j+1)*k+i))
   if sector:
    off=k*layers
    for f in list(fs):fs.append(tuple(off+j for j in reversed(f)))
    for i in range(steps):
     fs.extend([(i,off+i,off+i+1,i+1),((layers-1)*k+i,(layers-1)*k+i+1,off+(layers-1)*k+i+1,off+(layers-1)*k+i)])
    for j in range(layers-1):
     for i in [0,steps]:fs.append((j*k+i,(j+1)*k+i,off+(j+1)*k+i,off+j*k+i))
   else:
    fs.extend([tuple(reversed(range(k))),tuple((layers-1)*k+i for i in range(k))])
  me=bpy.data.meshes.new(d['n']);me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new(d['n'],me);col.objects.link(o);o.location=d['p']
 o.name='SM_'+d['n'];move(o,col)
 for key,axis in [('tilt',0),('rot_y',1),('rot_z',2)]:
  if key in d:o.rotation_euler[axis]+=d[key]
 select([o]);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
 import bmesh
 bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
 while o.data.uv_layers:o.data.uv_layers.remove(o.data.uv_layers[0])
 uv=o.data.uv_layers.new(name='UV0_ConstructionTrim')
 # Face-local projections into deliberately reusable 8-cell trim atlas.
 cell=d['cell'];cx,cy=cell%4,cell//4
 for face in o.data.polygons:
  axis=max(range(3),key=lambda i:abs(face.normal[i]));axes=[i for i in range(3) if i!=axis]
  coords=[o.data.vertices[o.data.loops[j].vertex_index].co for j in face.loop_indices]
  lo=[min(p[i] for p in coords) for i in axes];hi=[max(p[i] for p in coords) for i in axes]
  for j,p in zip(face.loop_indices,coords):
   uv.data[j].uv=((cx+.04+.92*(p[axes[0]]-lo[0])/max(hi[0]-lo[0],.0001))/4,(cy+.04+.92*(p[axes[1]]-lo[1])/max(hi[1]-lo[1],.0001))/2)
 o.data.materials.append(mat)
 vg=o.vertex_groups.new(name=d['bone']);vg.add(list(range(len(o.data.vertices))),1,'REPLACE')
 o['rig_bone']=d['bone'];o['part_role']=d['n'];o['trim_cell']=cell
 return o
def build(spec):
 name,dims,budgets,res,clips=spec
 out=ROOT/name;out.mkdir(exist_ok=True);(out/'Exports').mkdir(exist_ok=True);(out/'Previews').mkdir(exist_ok=True)
 bpy.ops.wm.read_factory_settings(use_empty=True);sc=bpy.context.scene;sc.name=name;sc.unit_settings.system='METRIC';sc.unit_settings.scale_length=1
 cols={}
 for cn in ['Reference','Blockout','Geo','Rig','Collision','Preview','Export']:
  c=bpy.data.collections.new('COL_'+cn);sc.collection.children.link(c);cols[cn]=c
 b=design(name);base=name.split('_Elite')[0]
 mats=[atlas(ROOT/base/'Textures',base,res)]
 if name.startswith('Boss'):mats.append(atlas(out/'Textures','Boss_Mechanisms',2048))
 lods=[];parts0=[]
 for lod in range(3):
  c=bpy.data.collections.new('LOD'+str(lod));cols['Geo'].children.link(c)
  obs=[]
  for d in b.parts:
   if lod==2 and d.get('detail'):continue
   mat=mats[1] if len(mats)>1 and d['cell'] in [3,4,5] else mats[0]
   obs.append(meshpart(d,lod,c,mat))
  if lod==0:
   parts0=list(obs);bpy.context.view_layer.update();lo,hi=bounds(obs)
   scale=Vector([dims[i]/(hi[i]-lo[i]) for i in range(3)])
   offset=Vector([-(hi[0]+lo[0])/2,-(hi[1]+lo[1])/2,-(hi[2]+lo[2])/2 if name.startswith('Ring') else -lo[2]])
  for o in obs:
   for v in o.data.vertices:v.co=(v.co+offset)*scale
  # Retain separate editable parts in hidden source collection.
  if lod==0:
   for o in obs:
    cp=o.copy();cp.data=o.data.copy();cols['Blockout'].objects.link(cp);cp.name=o.name+'_Editable';cp.hide_render=True;cp.hide_set(True)
  select(obs);bpy.ops.object.join();o=obs[0];o.name='SM_'+name+'_LOD'+str(lod)
  o.data.calc_loop_triangles()
  if len(o.data.loop_triangles)>budgets[lod]:
   md=o.modifiers.new('Budget reduction','DECIMATE');md.ratio=(budgets[lod]-12)/len(o.data.loop_triangles);bpy.ops.object.modifier_apply(modifier=md.name)
  md=o.modifiers.new('Export triangulation','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=md.name)
  o.data.calc_loop_triangles();lods.append(o)
  if lod:o.hide_render=True;o.hide_set(True)
 def point(p):return (Vector(p)+offset)*scale
 armdata=bpy.data.armatures.new('ARM_'+name);arm=bpy.data.objects.new('ARM_'+name,armdata);cols['Rig'].objects.link(arm);select([arm]);bpy.ops.object.mode_set(mode='EDIT')
 for bn,(p,parent) in b.bones.items():
  bone=armdata.edit_bones.new(bn);bone.head=Vector((0,0,0)) if bn=='root' else point(p);bone.tail=bone.head+Vector((0,.12,0));bone.use_deform=True
  if parent:bone.parent=armdata.edit_bones[parent]
 bpy.ops.object.mode_set(mode='OBJECT');arm.show_in_front=True
 for o in lods:
  md=o.modifiers.new('Rigid single-bone skin','ARMATURE');md.object=arm;o.parent=arm
 sockets=[]
 for sn,(p,bn) in b.sockets.items():
  ob=bpy.data.objects.new(sn,None);cols['Rig'].objects.link(ob);ob.empty_display_size=.08;ob.parent=arm;ob.parent_type='BONE';ob.parent_bone=bn;bone=arm.data.bones[bn];ob.matrix_parent_inverse=(bone.matrix_local@Matrix.Translation((0,bone.length,0))).inverted();ob.matrix_basis=Matrix.Translation(point(p));bpy.context.view_layer.update();sockets.append(ob)
 # Convex proxies per substantial part, never enclosing inter-leg / ring air.
 proxies=[]
 for i,d in enumerate(b.parts):
  if d['cell'] in [3,4] or d.get('detail') or d['typ']=='ring':continue
  if min(d['s'])<.04:continue
  pd=dict(d);pd['cell']=0
  po=meshpart(pd,2,cols['Collision'],mats[0]);po.name='SM_Hit_'+d['n']+'_'+str(i)
  for v in po.data.vertices:v.co=(v.co+offset)*scale
  po.hide_render=True;po.display_type='WIRE';po['proxy_role']='hit';po['bind_bone']=d['bone'];proxies.append(po)
 # Each annular frame represented by segment boxes leaving actual opening free.
 for d in [d for d in b.parts if d['typ']=='ring']:
  rx,rz,dep=d['s'];start=d.get('start',0);end=d.get('end',TAU)
  for j in range(12):
   a=start+(end-start)*(j+.5)/12;r=Vector(((rx-d['thick']/2)*math.cos(a),0,(rz-d['thick']/2)*math.sin(a)))
   if d.get('axis')=='Z':r=Vector((r.x,r.z,0))
   pd=dict(n='FrameSegment',p=Vector(d['p'])+r,s=(d['thick'],dep,(end-start)*max(rx,rz)/12*.82),cell=0,bone=d['bone'],typ='box',rot_y=-a)
   if d.get('axis')=='Z':pd['s']=(d['thick'],(end-start)*max(rx,rz)/12*.82,dep);pd.pop('rot_y');pd['rot_z']=a
   po=meshpart(pd,2,cols['Collision'],mats[0]);po.name='SM_Hit_'+d['n']+'_'+str(j)
   for v in po.data.vertices:v.co=(v.co+offset)*scale
   po.hide_render=True;po.display_type='WIRE';po['proxy_role']='hit';po['bind_bone']=d['bone'];proxies.append(po)
 for o in list(proxies):
  # Movement follows loadbearing body/frame only; limbs remain separate hit proxies.
  if any(x in o.name for x in ['Chassis','LoadFrame','PearVessel','EllipticalFrame','CrucibleShell']):
   cp=o.copy();cp.data=o.data.copy();cols['Collision'].objects.link(cp);cp.name=o.name.replace('SM_Hit','SM_Move');cp['proxy_role']='movement';proxies.append(cp)
 for o in proxies:
  md=o.modifiers.new('Proxy rigid skin','ARMATURE');md.object=arm;o.parent=arm
 cols['Collision'].hide_render=True;cols['Collision'].hide_viewport=True
 cols['Blockout'].hide_render=True;cols['Blockout'].hide_viewport=True
 # Rulers.
 for rn,p,s in [('OneMetreCube',(-dims[0]/2-1.1,0,.5),(1,1,1)),('HumanTorso',(dims[0]/2+.6,0,1.15),(.42,.23,.60)),('HumanHead',(dims[0]/2+.6,0,1.68),(.23,.23,.24)),('HumanLegL',(dims[0]/2+.46,0,.45),(.14,.16,.9)),('HumanLegR',(dims[0]/2+.74,0,.45),(.14,.16,.9))]:
  meshpart(dict(n=rn,p=p,s=s,cell=5,bone='root',typ='box'),2,cols['Reference'],mats[0])
 cols['Reference'].hide_render=True
 animate(b,arm,clips,scale)
 sc.frame_set(1);arm.animation_data.action=None
 for p in arm.pose.bones:p.matrix_basis=Matrix.Identity(4)
 sc.render.fps=30
 # Metrics are measured, not budgets copied.
 report={'asset':name,'target_dimensions':dims,'triangles':[],'bone_count':len(armdata.bones),'materials':len(mats),'texture_resolution':res,'clips':clips,'axis_blender':'Z up, -Y forward','root':[0,0,0],'flight_pivot':name.startswith('Ring'),'status':'TECHNICAL_PASS_PENDING_VISUAL_AND_UNITY_QA','unverified':['Unity URP import/axes/normal map/Animator','joint intersections over every frame','foot contact under gameplay movement','LOD popping in engine','unique texel density: shared construction trim UVs','platform occupancy in actual skill states'],'proxy_count':len(proxies)}
 for o in lods:
  o.data.calc_loop_triangles();report['triangles'].append(len(o.data.loop_triangles))
 bpy.context.view_layer.update();lo,hi=bounds([lods[0]]);report['actual_dimensions']=[round(hi[i]-lo[i],5) for i in range(3)]
 assert all(n<=k for n,k in zip(report['triangles'],budgets)),report
 assert report['bone_count']<=(64 if name.startswith('Boss') else 36 if b.elite else 28)
 (out/'qa_metrics.json').write_text(json.dumps(report,indent=2))
 setup_preview(sc,cols,dims)
 bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'.blend')))
 export(b,arm,lods,sockets,proxies,clips,out,name)
 # Model FBX reimport happens in an entirely separate Blender process.
 preview(sc,cols,arm,lods,dims,out,name)
 log('COMPLETE '+name+' '+str(report['triangles']))
 return report
def animate(b,arm,clips,scale):
 arm.animation_data_create()
 for clip in clips:
  act=bpy.data.actions.new('AN_'+b.name+'_'+clip);act.use_fake_user=True;arm.animation_data.action=act
  cycle=any(x in clip for x in ['Idle','Run','Walk','Tracking','Aim'])
  count=61 if 'Idle' in clip else 31 if ('Run' in clip or 'Walk' in clip) else 46
  act['loop']=cycle;act['in_place']=True;act['phase_frames']='anticipation 1-16; contact/fire 17-23; recovery 24-'+str(count)
  for f in range(1,count+1):
   t=(f-1)/(count-1);wave=math.sin(t*TAU);attack=max(0,1-abs(t-.52)/.23)
   for pb in arm.pose.bones:pb.rotation_mode='XYZ';pb.location=(0,0,0);pb.rotation_euler=(0,0,0);pb.scale=(1,1,1)
   body=arm.pose.bones['body']
   if 'Idle' in clip:body.location.z=.008*wave
   if any(x in clip for x in ['Run','Walk','Turn']):
    body.location.z=.012*math.sin(t*TAU*2)
    for j,(n,hip,knee,foot) in enumerate(b.legs):
     # Baked analytical two-link IK. Stance moves backwards at uniform speed;
     # swing returns with clearance, feet stay level. Root remains at zero.
     upper=arm.pose.bones[n+'_upper'];lower=arm.pose.bones[n+'_lower'];shoe=arm.pose.bones[n+'_foot']
     h0=upper.bone.head_local.copy();k0=lower.bone.head_local.copy();f0=shoe.bone.head_local.copy()
     phase=(t+([0,.5,.5,0][j] if len(b.legs)==4 else (j%2)*.5))%1
     stride=min(.24,(h0-f0).length*.45)
     ftarget=f0.copy()
     if phase<.62:ftarget.y+=stride*(phase/.62-.5)
     else:
      swing=(phase-.62)/.38;ftarget.y+=stride*(.5-swing);ftarget.z+=min(.14,stride*.45)*math.sin(swing*math.pi)
     if clip.startswith('Turn'):
      sign=(1 if h0.x>=0 else -1)*(1 if clip.endswith('_L') else -1)
      ftarget.y=f0.y+(ftarget.y-f0.y)*sign
     h=h0+Vector((0,0,body.location.z));a=(k0-h0).length;c=(f0-k0).length
     direction=(ftarget-h);distance=min(direction.length,a+c-.00001);direction.normalize()
     along=(a*a-c*c+distance*distance)/(2*distance)
     pole=(k0-h0)-direction*(k0-h0).dot(direction)
     if pole.length<.0001:pole=Vector((0,-1,0))
     pole.normalize();k=h+direction*along+pole*math.sqrt(max(0,a*a-along*along))
     for pb,head,restvec,newvec in [(upper,h,k0-h0,k-h),(lower,k,f0-k0,ftarget-k)]:
      q=restvec.rotation_difference(newvec)
      rot=q.to_matrix().to_4x4()@pb.bone.matrix_local.to_3x3().to_4x4()
      rot.translation=head;pb.matrix=rot;bpy.context.view_layer.update()
     mat=shoe.bone.matrix_local.copy();mat.translation=ftarget;shoe.matrix=mat
     bpy.context.view_layer.update()
   if any(x in clip for x in ['Attack','Fire','Volley','Spike','Shockwave']):
    body.location.y=-.045*attack
    for bn in ['jaw_-1','jaw_1']:
     if bn in arm.pose.bones:arm.pose.bones[bn].rotation_euler.z=(1 if bn.endswith('_1') else -1)*(.25*math.sin(math.pi*min(t/.5,1))-.10*attack)
    for bn in ['ram','recoil']:
     if bn in arm.pose.bones:arm.pose.bones[bn].location.y=(-.20 if bn=='ram' else .10)*attack
    if 'press' in arm.pose.bones:arm.pose.bones['press'].location.z=-.09*attack
    if 'gate_1' in arm.pose.bones:arm.pose.bones['gate_1'].location.z=.08*math.sin(math.pi*t)
    if 'Shock' in clip:body.location.z=-.18*attack;arm.pose.bones['shock'].location.z=-.32*attack
    if 'Spike' in clip:
     for j in range(3):arm.pose.bones['spike_'+str(j)].location.z=-.30*max(0,1-abs(t-(.44+j*.07))/.18)
   if any(x in clip for x in ['Aim','Tracking']):
    bn='aim' if 'aim' in arm.pose.bones else 'launcher'
    if bn in arm.pose.bones:arm.pose.bones[bn].rotation_euler.z=.10*wave;arm.pose.bones[bn].rotation_euler.x=.06*math.sin(t*TAU)
   if any(x in clip for x in ['Overload','Explode']):
    hold=min(t*2,1)
    arm.pose.bones['valve'].location.z=.10*hold
    for i in range(3):
     bn='pressure_panel_'+str(i)
     if bn in arm.pose.bones:
      a=-math.pi/2+i*TAU/3
      opening=max(0,min((t-.18-i*.06)*2.5,1))
      arm.pose.bones[bn].location=(.045*math.cos(a)*opening,.045*math.sin(a)*opening,.025*opening)
    # Plant tripod feet wider during arming; this is a pose change, not root motion.
    for j,(n,hip,knee,foot) in enumerate(b.legs):
     arm.pose.bones[n+'_upper'].rotation_euler.y=.09*math.cos(TAU*j/3-math.pi/2)*hold
    for j,bn in enumerate(['vent_-1','vent_1','bufferlid_-1','bufferlid_1']):
     if bn in arm.pose.bones:arm.pose.bones[bn].rotation_euler.x=.40*max(0,min((t-j*.13)*3,1))
   if any(x in clip for x in ['Shield','Phase','Volley','Tracking','Stagger']):
    opening=(1-t if 'Close' in clip else t if 'Open' in clip or 'Phase' in clip else math.sin(math.pi*t))
    for j in range(3):
     bn='door_'+str(j)
     if bn in arm.pose.bones:
      a=(j*120+90)*math.pi/180;arm.pose.bones[bn].location.x=math.cos(a)*.60*opening;arm.pose.bones[bn].location.z=math.sin(a)*.60*opening
      arm.pose.bones['lock_'+str(j)].location.y=.16*opening
   if 'Summon' in clip:
    for side in [-1,1]:arm.pose.bones['service_'+str(side)].rotation_euler.y=side*.8*math.sin(math.pi*t)
   if any(x in clip for x in ['Hit','Stagger']):body.rotation_euler.x=.08*math.sin(math.pi*t)*math.exp(-t*2)
   if 'Death' in clip:
    body.location.z=-min(.3,abs(b.bones['body'][0][2])*.3)*t
    body.rotation_euler.x=.15*t
    for n,_,_,_ in b.legs:
     arm.pose.bones[n+'_upper'].rotation_euler.x=.4*t;arm.pose.bones[n+'_lower'].rotation_euler.x=-.65*t
    if 'Fall' in clip:body.location.z=-.45*t;body.rotation_euler.y=.5*t
    for j in range(3):
     if 'door_'+str(j) in arm.pose.bones:arm.pose.bones['door_'+str(j)].location.z=-.25*t
   for pb in arm.pose.bones:
    pb.keyframe_insert('location',frame=f,group=pb.name);pb.keyframe_insert('rotation_euler',frame=f,group=pb.name)
  # Preserve independently named actions without overlapping NLA playback.
  tr=arm.animation_data.nla_tracks.new();tr.name=act.name;st=tr.strips.new(act.name,1,act);tr.mute=True
 arm.animation_data.action=None
 for pb in arm.pose.bones:pb.matrix_basis=Matrix.Identity(4)
def export(b,arm,lods,sockets,proxies,clips,out,name):
 for o in lods:o.hide_set(False)
 select([arm]+lods+sockets)
 kwargs=dict(use_selection=True,axis_forward='-Z',axis_up='Y',add_leaf_bones=False,use_armature_deform_only=False,apply_scale_options='FBX_SCALE_UNITS',global_scale=1,path_mode='RELATIVE',mesh_smooth_type='FACE')
 bpy.ops.export_scene.fbx(filepath=str(out/'Exports'/(name+'_Model.fbx')),bake_anim=False,**kwargs)
 select([arm]+proxies)
 bpy.ops.export_scene.fbx(filepath=str(out/'Exports'/(name+'_CollisionReference.fbx')),bake_anim=False,**kwargs)
 select([arm])
 for clip in clips:
  act=bpy.data.actions.get('AN_'+name+'_'+clip);arm.animation_data.action=act;bpy.context.scene.frame_start=1;bpy.context.scene.frame_end=int(act.frame_range[1])
  bpy.ops.export_scene.fbx(filepath=str(out/'Exports'/('AN_'+name+'_'+clip+'.fbx')),bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,**kwargs)
 arm.animation_data.action=None
 for pb in arm.pose.bones:pb.matrix_basis=Matrix.Identity(4)
 for o in lods[1:]:o.hide_set(True)
def setup_preview(sc,cols,dims):
 sc.render.engine='CYCLES';sc.cycles.samples=8;sc.cycles.use_denoising=True
 sc.world=bpy.data.worlds.new('World_Review')
 sc.render.resolution_x=720;sc.render.resolution_y=720;sc.render.resolution_percentage=100
 sc.world.color=(.16,.16,.16)
 for n,p,power,size in [('Key',(3,-4,6),700,5),('Fill',(-4,-2,3),400,4),('Rim',(2,4,5),900,3)]:
  data=bpy.data.lights.new(n,'AREA');data.energy=power*max(1,dims[2]/2)**2;data.shape='DISK';data.size=size
  ob=bpy.data.objects.new('LGT_'+n,data);cols['Preview'].objects.link(ob);ob.location=Vector(p)*max(1,dims[2]/2);ob.rotation_euler=(Vector((0,0,dims[2]/2))-ob.location).to_track_quat('-Z','Y').to_euler()
 data=bpy.data.cameras.new('CAM_Review');cam=bpy.data.objects.new('CAM_Review',data);cols['Preview'].objects.link(cam);sc.camera=cam
 sc.view_settings.view_transform='AgX'
def preview(sc,cols,arm,lods,dims,out,name):
 sc.frame_set(1);arm.animation_data.action=None
 for pb in arm.pose.bones:pb.matrix_basis=Matrix.Identity(4)
 cam=sc.camera;z=0 if name.startswith('Ring') else dims[2]*.5
 target=Vector((0,0,z));r=max(dims)*2.4
 views={'Front':(0,-r,z),'Side':(r,0,z),'Back':(0,r,z),'ThreeQuarter':(r*.70,-r*.80,z+r*.45)}
 for lighting in ['Neutral','FoundryDark']:
  for ob in cols['Preview'].objects:
   if ob.type=='LIGHT':
    ob.data.color=(.53,.68,1) if lighting=='FoundryDark' and 'Key' in ob.name else (1,.69,.42) if lighting=='FoundryDark' and 'Rim' in ob.name else (1,1,1)
    ob.data.energy*=.5 if lighting=='FoundryDark' else 1
  sc.world.color=(.025,.035,.05) if lighting=='FoundryDark' else (.16,.16,.16)
  for vn,pos in views.items():
   cam.data.type='ORTHO';cam.data.ortho_scale=max(dims)*1.35;cam.location=pos;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
   sc.render.filepath=str(out/'Previews'/(lighting+'_'+vn+'.png'));bpy.ops.render.render(write_still=True)
  for dist in ([5,15,30,50] if name.startswith('Boss') else [5,15,30]):
   cam.data.type='PERSP';cam.data.lens=24;cam.location=(0,-dist,1.7);cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
   sc.render.filepath=str(out/'Previews'/(lighting+'_Game_'+str(dist)+'m.png'));bpy.ops.render.render(write_still=True)
 # Save final editor view with visible LOD0, rig and camera, reference scale.
 cam.data.type='ORTHO';cam.data.ortho_scale=max(dims)*1.35;cam.location=views['ThreeQuarter'];cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
 sc.frame_start=1;sc.frame_end=61
 bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'.blend')))
if __name__=='__main__':
 args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
 idx=[int(s) for s in args] if args else range(9)
 for i in idx:
  try:build(MAN['assets'][i])
  except Exception:
   (ROOT/('ERROR_'+str(i)+'.txt')).write_text(traceback.format_exc());traceback.print_exc();sys.exit(1)
