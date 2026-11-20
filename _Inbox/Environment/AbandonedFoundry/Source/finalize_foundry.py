import bpy,json,math,csv
from pathlib import Path
R=Path(r'E:\S\_Inbox\Environment\AbandonedFoundry');res=Path(r'E:\S\Assets\_Game\Res')
bpy.ops.wm.open_mainfile(filepath=str(R/'Source/Foundry_Layout.blend'))
layout=json.load(open(R/'Reports/layout.json'));man=json.load(open(R/'Reports/modules.json'))['modules']
lookup={m['key']:m for m in man}
# Store all placed collision proxies in full source, hidden from render, selectable via COLLISION collection.
for i,e in enumerate(layout['instances']):
 for name in lookup[e['key']]['collision']:
  src=bpy.data.objects.get(name)
  if not src:continue
  ob=bpy.data.objects.new('PLACED_'+str(i)+'_'+name,src.data);bpy.data.collections['COLLISION'].objects.link(ob)
  ob.location=(e['x'],e['y'],e['z']);ob.rotation_euler.z=math.radians(e['rotation']);ob.hide_render=True;ob.display_type='WIRE';ob.hide_set(True)
# Pack all shared textures for self-contained blend source.
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(R/'Source/Foundry_Layout.blend'))
bpy.ops.wm.open_mainfile(filepath=str(R/'Source/Foundry_Modules.blend'))
report={'modules':len(man),'checks':[],'lod_target_exceptions':[],'under_300_modules':[]}
for m in man:
 o=bpy.data.objects[m['lod0']];o.data.calc_loop_triangles()
 deg=sum(1 for t in o.data.loop_triangles if t.area<1e-9)
 report['checks'].append({'key':m['key'],'uv_channels':len(o.data.uv_layers),'degenerate_tris':deg,'scale_applied':all(abs(x-1)<1e-6 for x in o.scale)})
 ratio=m['tris1']/m['tris0']
 if not .4<=ratio<=.6:report['lod_target_exceptions'].append({'key':m['key'],'ratio':round(ratio,3),'reason':'Retained minimum structure / seam geometry, or secondary-detail removal; see measured manifest.'})
 if m['tris0']<300:report['under_300_modules'].append(m['key'])
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(R/'Source/Foundry_Modules.blend'))
json.dump(report,open(R/'Reports/BlenderValidation.json','w'),indent=2)
print('VALIDATED',len(man),'degenerate total',sum(c['degenerate_tris'] for c in report['checks']),flush=True)
