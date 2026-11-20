import bpy,json,math
from pathlib import Path
R=Path(r'E:\S\_Inbox\Environment\AbandonedFoundry')
# Check true coplanar triangle overlap by convex polygon clipping, excluding shared edges (area=0).
def cross(a,b):return a[0]*b[1]-a[1]*b[0]
def area(ps):return abs(sum(cross(ps[i],ps[(i+1)%len(ps)]) for i in range(len(ps))))*.5 if len(ps)>2 else 0
def clip(poly,tri):
 if sum(cross(tri[i],tri[(i+1)%3]) for i in range(3))<0:tri=list(reversed(tri))
 for k in range(3):
  a,b=tri[k],tri[(k+1)%3];out=[]
  def dist(p):return cross((b[0]-a[0],b[1]-a[1]),(p[0]-a[0],p[1]-a[1]))
  for i,p in enumerate(poly):
   q=poly[(i+1)%len(poly)];dp,dq=dist(p),dist(q)
   if dp>=-1e-9:out.append(p)
   if (dp>0 and dq<0) or (dp<0 and dq>0):
    t=dp/(dp-dq);out.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
  poly=out
  if not poly:break
 return poly
results=[]
for name in ['ENV_Foundry_Wall_Straight4_LOD0','ENV_Foundry_Wall_Straight4_LOD1']:
 o=bpy.data.objects[name];me=o.data;me.calc_loop_triangles();tris=list(me.loop_triangles);overlaps=[]
 for i,t in enumerate(tris):
  a=[me.vertices[v].co for v in t.vertices];normal=t.normal
  for j in range(i+1,len(tris)):
   u=tris[j]
   if abs(normal.dot(u.normal))<.999999:continue
   b=[me.vertices[v].co for v in u.vertices]
   if max(abs(normal.dot(v-a[0])) for v in b)>.000001:continue
   ax=max(range(3),key=lambda k:abs(normal[k]));dims=[k for k in range(3) if k!=ax]
   p=[(v[dims[0]],v[dims[1]]) for v in a];q=[(v[dims[0]],v[dims[1]]) for v in b]
   ar=area(clip(p,q))
   if ar>1e-7:overlaps.append([i,j,ar])
 results.append({'name':name,'coplanar_positive_area_overlaps':len(overlaps),'details':overlaps[:10]})
(R/'Reports/WallRevision_Overlap.json').write_text(json.dumps(results,indent=2))
# Hide paired lower LOD in source view, avoiding simultaneous display.
bpy.data.objects['ENV_Foundry_Wall_Straight4_LOD1'].hide_set(True)
bpy.data.objects['COL_Foundry_Wall_Straight4_00_Both'].hide_set(True)
# Remove temporary export scene after the revised compact source has been written.
sc=bpy.data.scenes.get('WallRevision_Export')
if sc:
 for ob in list(sc.objects):bpy.data.objects.remove(ob,do_unlink=True)
 bpy.data.scenes.remove(sc)
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print(json.dumps(results))
