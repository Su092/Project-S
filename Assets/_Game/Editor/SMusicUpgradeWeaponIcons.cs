using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace S.EditorTools
{
    /// <summary>Thumbnails of the current imported instruments. Baked preview geometry avoids expanded animation bounds.</summary>
    public static class SMusicUpgradeWeaponIcons
    {
        public static void Build()
        {
            foreach(var a in SMusicWeaponImporter.ReadPlan().assets.Where(a=>a.gunId=="smg"||a.gunId=="launcher"))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(SMusicWeaponImporter.Prefabs+"/"+a.assetId+".prefab");
                if(!prefab)throw new System.Exception(a.assetId+" prefab missing");
                var preview=new PreviewRenderUtility();
                var instance=Object.Instantiate(prefab);var cleanup=new List<Object>();
                try
                {
                    var visual=instance.GetComponent<S.View.MusicWeaponVisual>();
                    visual.Clips[System.Array.IndexOf(visual.States,"Idle")].SampleAnimation(visual.Animator.gameObject,0);
                    foreach(var animator in instance.GetComponentsInChildren<Animator>())animator.enabled=false;
                    foreach(var lod in instance.GetComponentsInChildren<LODGroup>())lod.enabled=false;
                    var skins=instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                    Bounds bounds=default;bool first=true;
                    foreach(var sk in skins){
                        sk.enabled=false;if(!sk.name.EndsWith("_LOD0"))continue;
                        var mesh=new Mesh();sk.BakeMesh(mesh);mesh.RecalculateBounds();cleanup.Add(mesh);
                        var go=new GameObject("IconMesh",typeof(MeshFilter),typeof(MeshRenderer));
                        go.transform.SetParent(sk.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;
                        var renderer=go.GetComponent<MeshRenderer>();
                        var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                        mat.SetTexture("_BaseMap",sk.sharedMaterial.GetTexture("_BaseMap"));mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Cull",0);cleanup.Add(mat);
                        renderer.sharedMaterials=Enumerable.Repeat(mat,mesh.subMeshCount).ToArray();
                        if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);
                    }
                    if(first)throw new System.Exception("No LOD0 geometry");
                    preview.AddSingleGO(instance);
                    var cam=preview.camera;cam.orthographic=true;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.clear;
                    cam.orthographicSize=Mathf.Max(bounds.size.y,Mathf.Max(bounds.size.z,bounds.size.x))*.61f;
                    cam.nearClipPlane=.01f;cam.farClipPlane=100;
                    // Imported kalimba playing face is +Y; accordion keys face -Z.
                    cam.transform.position=bounds.center+(a.gunId=="smg"?new Vector3(.1f,1f,-.16f):new Vector3(.18f,.12f,-1f)).normalized*(bounds.size.magnitude*3+1);
                    cam.transform.LookAt(bounds.center,a.gunId=="smg"?Vector3.forward:Vector3.up);
                    preview.BeginPreview(new Rect(0,0,256,256),GUIStyle.none);preview.Render(true);
                    var rendered=preview.EndPreview() as RenderTexture;
                    var active=RenderTexture.active;RenderTexture.active=rendered;
                    var tex=new Texture2D(256,256,TextureFormat.RGBA32,false);
                    tex.ReadPixels(new Rect(0,0,256,256),0,0);tex.Apply();RenderTexture.active=active;
                    if(tex.GetPixels().Count(c=>c.a>.1f)<100)throw new System.Exception("Empty thumbnail "+a.assetId+" bounds "+bounds);
                    string path=S.View.UiAssets.DirOf("hud_gun_"+a.gunId)+"hud_gun_"+a.gunId+".png";
                    File.WriteAllBytes(path,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                    Debug.Log("THUMB "+a.assetId+" "+bounds);
                }
                finally {preview.Cleanup();foreach(var item in cleanup)Object.DestroyImmediate(item);}
            }
            AssetDatabase.SaveAssets();
        }
    }
}
