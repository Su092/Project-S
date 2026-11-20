import bpy,json,hashlib
from pathlib import Path
ROOT=Path(r'E:\S\_Inbox\chr\S_Foundry_Enemies\Review_v002')
man=json.loads((ROOT/'production_manifest.json').read_text())
reports=[]
for name,dims,budgets,res,clips in man['assets']:
 p=ROOT/name
 bpy.ops.wm.open_mainfile(filepath=str(p/(name+'.blend')))
 missing=[];outside=[]
 for im in bpy.data.images:
  if im.source!='FILE':continue
  path=Path(bpy.path.abspath(im.filepath)).resolve()
  if not path.exists():missing.append(str(path))
  if not path.is_relative_to(ROOT):outside.append(str(path))
 expected=[p/'Exports'/(name+'_Model.fbx'),p/'Exports'/(name+'_CollisionReference.fbx')]
 expected += [p/'Exports'/('AN_'+name+'_'+clip+'.fbx') for clip in clips]
 expected += [p/'Previews'/('AN_'+clip+'.gif') for clip in clips]
 expected += [p/'Previews'/(light+'_'+view+'.png') for light in ['Neutral','FoundryDark'] for view in ['Front','Side','Back','ThreeQuarter','Game_5m','Game_15m','Game_30m']]
 expected += [p/'Previews'/(view+'.png') for view in ['Wireframe','CollisionOverlay','LOD0','LOD1','LOD2']]
 absent=[str(f.relative_to(ROOT)) for f in expected if not f.exists()]
 arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 lods=[bpy.data.objects['SM_'+name+'_LOD'+str(i)] for i in range(3)]
 counts=[]
 for ob in lods:ob.data.calc_loop_triangles();counts.append(len(ob.data.loop_triangles))
 cols=['COL_'+s for s in ['Reference','Blockout','Geo','Rig','Collision','Preview','Export']]
 row=dict(asset=name,missing_textures=missing,external_dependencies=outside,missing_deliverables=absent,missing_collections=[c for c in cols if not bpy.data.collections.get(c)],triangles=counts,budget_pass=all(a<=b for a,b in zip(counts,budgets)),bones=len(arm.data.bones),material_slots=[len(o.data.materials) for o in lods])
 reports.append(row)
 print(json.dumps(row),flush=True)
report=dict(scope='Delivery copy reopened in independent Blender process. File completeness, portable texture paths and measured budgets only.',assets=reports)
report['pass']=all(not any(r[k] for k in ['missing_textures','external_dependencies','missing_deliverables','missing_collections']) and r['budget_pass'] for r in reports)
(ROOT/'DeliveryAudit.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('DELIVERY_AUDIT',report['pass'],flush=True)
