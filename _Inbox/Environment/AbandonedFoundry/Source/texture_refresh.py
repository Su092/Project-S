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

for filename in ['Foundry_Modules.blend','Foundry_Layout.blend']:
 bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Source'/filename))
 for im in bpy.data.images:
  if im.source=='FILE' and 'T_Foundry_' in im.name:
   im.filepath=str(TEX/(im.name.split('.png')[0]+'.png'))
   if im.packed_file:im.unpack(method='REMOVE')
   im.reload()
 bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Source'/filename))
