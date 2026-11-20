import bpy,bmesh,json,math,shutil
from pathlib import Path
from mathutils import Vector
R=Path(r'E:\S\_Inbox\Environment\AbandonedFoundry');N='ENV_Foundry_Wall_Straight4_'
src=bpy.data.objects[N+'LOD0'];src.update_from_editmode()
backup=R/'Source/WallRevision_Backup.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(backup),copy=True)
# Source snapshot respects user's unsaved changes. Work in an isolated scene.
oldscene=bpy.context.window.scene
scene=bpy.data.scenes.new('WallRevision_Export');bpy.context.window.scene=scene
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
work=bpy.data.collections.new('WallRevision_WORK');scene.collection.children.link(work)
# Split connected shells without changing coordinates or authored outer silhouettes.
bm=bmesh.new();bm.from_mesh(src.data);bm.verts.ensure_lookup_table();remaining=set(bm.verts);components=[]
while remaining:
 stack=[remaining.pop()];vs=set(stack)
 while stack:
  v=stack.pop()
  for e in v.link_edges:
   n=e.other_vert(v)
   if n in remaining:remaining.remove(n);vs.add(n);stack.append(n)
 components.append({v.index for v in vs})
bm.free()
parts=[]
for i,indices in enumerate(components):
 mesh=src.data.copy();ob=bpy.data.objects.new('Revision_Part_'+str(i),mesh);work.objects.link(ob)
 bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
 bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in indices],context='VERTS')
 bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
 bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001)
 bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
 parts.append(ob)
body=parts[0];bpy.context.view_layer.objects.active=body;body.select_set(True)
for operand in parts[1:]:
 mod=body.modifiers.new('Remove internal overlapping shells','BOOLEAN');mod.operation='UNION';mod.solver='EXACT';mod.object=operand
 bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(operand,do_unlink=True)
# Clean Boolean intersections and zero area faces.
bm=bmesh.new();bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()
# Keep explicitly authored metric UV0, remove Blender's primitive UVMap.
for uv in list(body.data.uv_layers):
 if uv.name!='UV0':body.data.uv_layers.remove(uv)
if not body.data.uv_layers:body.data.uv_layers.new(name='UV0')
def lightmap(o):
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 if o.data.uv_layers.get('UV1'):o.data.uv_layers.remove(o.data.uv_layers['UV1'])
 o.data.uv_layers.new(name='UV1');o.data.uv_layers.active_index=1
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT')
 o.data.uv_layers.active_index=0
lightmap(body)
low=body.copy();low.data=body.data.copy();work.objects.link(low)
# Collapse on derived mesh only. Preserve seam/end vertices using a weight mask.
vg=low.vertex_groups.new(name='NonSeamReduction')
for v in low.data.vertices:
 seam=abs(v.co.x)<1e-5 or abs(v.co.x-4)<1e-5 or abs(v.co.z)<1e-5 or abs(v.co.z-4)<1e-5
 vg.add([v.index],0 if seam else 1,'REPLACE')
bpy.context.view_layer.objects.active=low
dec=low.modifiers.new('LOD1 silhouette reduction','DECIMATE');dec.ratio=.5;dec.vertex_group=vg.name;dec.vertex_group_factor=1
bpy.ops.object.modifier_apply(modifier=dec.name);lightmap(low)
# Collision follows the new watertight visible solid, without filling visible voids.
coll=body.copy();coll.data=body.data.copy();work.objects.link(coll);coll.hide_render=True;coll.display_type='WIRE'
# The existing .blend object names must be temporarily released for exact FBX identifiers.
old0=bpy.data.objects.get(N+'LOD0');old1=bpy.data.objects.get(N+'LOD1');oldc=bpy.data.objects.get('COL_Foundry_Wall_Straight4_00_Both')
originals=[o for o in [old0,old1,oldc] if o]
savednames={o:o.name for o in originals}
for o in originals:o.name='BACKUP_'+o.name
body.name=N+'LOD0';low.name=N+'LOD1';coll.name='COL_Foundry_Wall_Straight4_00_Both'
report={'input_backup':str(backup),'parts_before':len(components),'operation':'Exact union removes interior overlaps while retaining edited exterior','objects':[]}
for o in [body,low,coll]:
 o.data.calc_loop_triangles();bm=bmesh.new();bm.from_mesh(o.data)
 loose=sum(not v.link_faces for v in bm.verts);nonmanifold=sum(not e.is_manifold for e in bm.edges)
 report['objects'].append({'name':o.name,'tris':len(o.data.loop_triangles),'zero_area_tris':sum(t.area<1e-10 for t in o.data.loop_triangles),'nonmanifold_edges':nonmanifold,'loose_vertices':loose,'dimensions':list(o.dimensions),'uv':[x.name for x in o.data.uv_layers]});bm.free()
 if nonmanifold or loose or report['objects'][-1]['zero_area_tris']:raise Exception('Invalid export mesh '+o.name+str(report))
report['lod_ratio']=report['objects'][1]['tris']/report['objects'][0]['tris']
for o in [body,low,coll]:o.select_set(True)
bpy.context.view_layer.objects.active=body
out=Path(r'E:\S\Assets\_Game\Res\Model\Environment\AbandonedFoundry\Wall_Straight4.fbx')
if out.exists():shutil.copy2(out,R/'Source/Wall_Straight4_PreRevision.fbx')
bpy.ops.export_scene.fbx(filepath=str(out),use_selection=True,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,bake_anim=False,object_types={'MESH'},path_mode='STRIP')
# Export a compact authoritative revised source using library write.
bpy.data.libraries.write(str(R/'Source/Wall_Straight4_Revised.blend'),{scene},path_remap='RELATIVE',fake_user=True)
# Update only wall prototypes / placed visible wall meshes in the currently edited Blender scene.
for old,new in [(old0,body),(old1,low),(oldc,coll)]:
 if old:old.data=new.data.copy()
for ob in oldscene.objects:
 if ob.type=='MESH' and ob.name.startswith('INST_') and ob.name.endswith('_Wall_Straight4'):ob.data=body.data
# Release exact names and restore edited scene selection.
for o in [body,low,coll]:o.name='EXPORTED_'+o.name
for o,n in savednames.items():o.name=n
bpy.context.window.scene=oldscene
reportpath=R/'Reports/WallRevision_Blender.json';reportpath.write_text(json.dumps(report,indent=2))
print(json.dumps(report))
