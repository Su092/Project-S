using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using S.View;
namespace S.EditorTools
{
    public static class SMusicHitboxCheck
    {
        struct Volume { public Matrix4x4 matrix; public Bounds bounds; }
        public static void Validate()
        {
            var log=new List<string>();int poses=0,failed=0;float minimum=1;
            foreach(var asset in SMusicEnemyImporter.ReadPlan().assets)
            {
                var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SMusicEnemyImporter.Prefabs+"/"+asset.id+".prefab"));
                try
                {
                    var v=root.GetComponent<EnemyVisual>();v.Animator.enabled=false;
                    var bones=root.GetComponentsInChildren<Transform>(true);
                    var positions=bones.Select(b=>b.localPosition).ToArray();
                    var rotations=bones.Select(b=>b.localRotation).ToArray();
                    var scales=bones.Select(b=>b.localScale).ToArray();
                    float worst=1;string worstPose="";
                    foreach(var clip in v.Clips.Where(c=>c.name!=v.DeathState))
                    foreach(float fraction in new[]{0f,.25f,.5f,.75f,.99f})
                    {
                        for(int i=0;i<bones.Length;i++){bones[i].localPosition=positions[i];bones[i].localRotation=rotations[i];bones[i].localScale=scales[i];}
                        clip.SampleAnimation(v.Animator.gameObject,clip.length*fraction);
                        var boxes=v.HitColliders.Cast<BoxCollider>().Select(c=>new Volume{matrix=c.transform.worldToLocalMatrix,bounds=new Bounds(c.center,c.size+Vector3.one*.0002f)}).ToArray();
                        double area=0,covered=0;
                        foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.EndsWith("_LOD0")))
                        {
                            var mesh=new Mesh();skin.BakeMesh(mesh);
                            var vertices=mesh.vertices.Select(p=>skin.transform.TransformPoint(p)).ToArray();var tri=mesh.triangles;
                            for(int i=0;i<tri.Length;i+=3)
                            {
                                var a=vertices[tri[i]];var b=vertices[tri[i+1]];var c=vertices[tri[i+2]];
                                double weight=Vector3.Cross(b-a,c-a).magnitude*.5/4;
                                foreach(var point in new[]{(a+b+c)/3,a*.6f+b*.2f+c*.2f,a*.2f+b*.6f+c*.2f,a*.2f+b*.2f+c*.6f})
                                {
                                    area+=weight;
                                    foreach(var box in boxes)if(box.bounds.Contains(box.matrix.MultiplyPoint3x4(point))){covered+=weight;break;}
                                }
                            }
                            UnityEngine.Object.DestroyImmediate(mesh);
                        }
                        float ratio=area>0?(float)(covered/area):0;poses++;
                        if(ratio<worst){worst=ratio;worstPose=clip.name+"@"+fraction;}
                        if(ratio<.8f)failed++;
                    }
                    minimum=Mathf.Min(minimum,worst);
                    log.Add(asset.id+" boxes="+v.HitColliders.Length+" minimum surface coverage="+worst.ToString("P3")+" "+worstPose);
                    if(asset.family.StartsWith("S16")&&!v.HitColliders.Any(c=>c.name.Contains("WeakPoint")))throw new Exception("Missing ballerina weak point");
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            log.Add("RESULT "+(poses-failed)+"/"+poses+" poses >=80%; minimum="+minimum.ToString("P3"));
            File.WriteAllLines("Logs/music-hitboxes-coverage.log",log);
            if(failed>0)throw new Exception("Contour coverage failed: "+failed+" poses");
        }
    }
}
