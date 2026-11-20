using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using S.View;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SMusicHitboxBuilder
    {
        const string Prefix="ContourHit_";
        static SMusicHitboxBuilder(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            const string request="Logs/music-hitboxes-build.request";if(!File.Exists(request))return;File.Delete(request);
            try{Rebuild();}catch(Exception e){File.AppendAllText("Logs/music-hitboxes-build.log","ERROR "+e+"\n");Debug.LogException(e);}
        }
        public static Collider[] Build(GameObject source,SMusicEnemyImporter.AssetInfo asset)
        {
            var skins=source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.EndsWith("_LOD0")).ToArray();
            var points=new Dictionary<Transform,List<Vector3>>();
            foreach(var skin in skins)
            {
                var baked=new Mesh();skin.BakeMesh(baked);var vertices=baked.vertices;var weights=skin.sharedMesh.boneWeights;
                for(int i=0;i<vertices.Length;i++)
                {
                    var w=weights[i];int index=w.boneIndex0;float strongest=w.weight0;
                    if(w.weight1>strongest){index=w.boneIndex1;strongest=w.weight1;}
                    if(w.weight2>strongest){index=w.boneIndex2;strongest=w.weight2;}
                    if(w.weight3>strongest){index=w.boneIndex3;strongest=w.weight3;}
                    var bone=skin.bones[index];if(!points.TryGetValue(bone,out var list)){list=new List<Vector3>();points[bone]=list;}
                    list.Add(bone.InverseTransformPoint(skin.transform.TransformPoint(vertices[i])));
                }
                UnityEngine.Object.DestroyImmediate(baked);
            }
            var weak=new HashSet<string>(asset.proxies.Where(p=>p.purpose=="WeakPoint").Select(p=>p.bone));
            var colliders=new List<Collider>();
            foreach(var item in points)
            {
                var bone=item.Key;var bounds=new Bounds(item.Value[0],Vector3.zero);
                foreach(var point in item.Value)bounds.Encapsulate(point);
                // Small seam margin, rather than a whole-enemy AABB that fills empty space.
                bounds.Expand(.025f);
                var go=new GameObject(Prefix+(weak.Contains(bone.name)?"WeakPoint_":"")+bone.name);
                go.layer=LayerMask.NameToLayer(WorldPhysics.ShotLayer);go.transform.SetParent(bone,false);
                var box=go.AddComponent<BoxCollider>();box.isTrigger=true;box.center=bounds.center;
                box.size=Vector3.Max(bounds.size,Vector3.one*.025f);colliders.Add(box);
            }
            if(colliders.Count==0)throw new InvalidOperationException("No contour geometry "+asset.id);
            return colliders.ToArray();
        }
        [MenuItem("S/Enemies/Rebuild Music Contour Hitboxes")]
        public static void Rebuild()
        {
            File.WriteAllText("Logs/music-hitboxes-build.log","START\n");
            foreach(var asset in SMusicEnemyImporter.ReadPlan().assets)
            {
                string path=SMusicEnemyImporter.Prefabs+"/"+asset.id+".prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var visual=root.GetComponent<EnemyVisual>();
                    foreach(var c in visual.HitColliders)
                    {
                        if(!c)continue;
                        // All authored proxy objects contain only a Transform and collider.
                        UnityEngine.Object.DestroyImmediate(c.gameObject);
                    }
                    visual.HitColliders=Build(visual.Animator.gameObject,asset);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                    File.AppendAllText("Logs/music-hitboxes-build.log",asset.id+" boxes="+visual.HitColliders.Length+"\n");
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();SMusicEnemyImporter.Validate();SMusicNativeAssets.Validate();
            SMusicHitboxCheck.Validate();File.AppendAllText("Logs/music-hitboxes-build.log","RESULT COMPLETE\n");
        }
    }
}
