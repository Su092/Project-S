using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using S.View;

namespace S.EditorTools
{
    public static class SMusicWeaponImporter
    {
        public const string Delivery = "_Inbox/weapons/MusicWeapons_v001";
        public const string Source = "Assets/_Game/Editor/WeaponImport/Sources";
        public const string Res = "Assets/_Game/Res";
        public const string Prefabs = Res + "/Prefab/Weapons";
        [Serializable] public class Plan { public Asset[] assets; }
        [Serializable] public class Asset { public string assetId, gunId; public Input[] inputs; public Clip[] animations; public Tex[] textures; }
        [Serializable] public class Input { public string path, sha256; }
        [Serializable] public class Clip { public string state, file; public float duration, fps; public bool loop; }
        [Serializable] public class Tex { public string file, colorSpace; }
        static readonly List<string> log = new();
        static int pass, fail;
        public static Plan ReadPlan() => JsonUtility.FromJson<Plan>(File.ReadAllText(Delivery + "/import-plan.json"));
        static string Dir(string type, string id) => Res + "/" + type + "/Weapons/Music/" + id;
        static void Check(string name, bool ok)
        { log.Add((ok ? "PASS " : "FAIL ") + name); if (ok) pass++; else fail++; }
        static void Ensure(string path) { Directory.CreateDirectory(path); }
        static string Hash(string file) { using var h = SHA256.Create(); return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant(); }
        static IEnumerable<string> Required(Asset a) => new[] { "Exports/" + a.assetId + "_Model.fbx" }.Concat(a.animations.Select(c => c.file)).Concat(a.textures.Select(t => t.file));
        static string SourcePath(Asset a, string relative) => Source + "/" + a.assetId + "/" + relative;
        static void Preflight(Plan plan)
        {
            if (plan.assets.Length != 6 || plan.assets.Select(a=>a.gunId).Distinct().Count()!=6) throw new Exception("Expected six unique weapons");
            foreach (var a in plan.assets)
            {
                if (!a.assetId.StartsWith("SW") || a.assetId.Contains("/") || a.assetId.Contains("..")) throw new Exception("Invalid ID");
                foreach (string relative in Required(a))
                {
                    if (Path.IsPathRooted(relative) || relative.Contains("..") || relative.Contains("\\")) throw new Exception("Unsafe input path");
                    var entry = a.inputs.Single(x => x.path == relative);
                    string file = Delivery + "/" + a.assetId + "/" + relative;
                    if (!File.Exists(file) || Hash(file) != entry.sha256) throw new Exception("Input hash mismatch: " + file);
                }
                foreach (string state in new[]{"Idle","Equip","Unequip","FireRecover","Reload"})
                    if (!a.animations.Any(c=>c.state==state)) throw new Exception("Missing state " + a.assetId + "/" + state);
            }
        }
        [MenuItem("S/Weapons/Import Music Weapon Delivery")]
        public static void ImportAll()
        {
            if (EditorApplication.isPlaying) throw new Exception("Exit Play before import");
            var plan = ReadPlan(); Preflight(plan);
            File.WriteAllText("Logs/music-weapon-import.log", "START\n");
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var a in plan.assets)
                {
                    foreach (var relative in Required(a))
                    {
                        bool texture = relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                        string destination = texture ? Dir("Texture",a.assetId) + "/" + Path.GetFileName(relative) : SourcePath(a,relative);
                        Ensure(Path.GetDirectoryName(destination));
                        string input = Delivery+"/"+a.assetId+"/"+relative;
                        if(!File.Exists(destination)||Hash(destination)!=Hash(input))File.Copy(input,destination,true);
                    }
                    foreach(string kind in new[]{"Model","Animation","Material"}) Ensure(Dir(kind,a.assetId));
                }
                Ensure(Prefabs);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var a in plan.assets)
            {
                foreach (var relative in Required(a).Where(f=>f.EndsWith(".fbx")))
                {
                    var importer = (ModelImporter)AssetImporter.GetAtPath(SourcePath(a,relative));
                    importer.globalScale = 1; importer.useFileScale = true;
                    importer.isReadable = true; importer.importCameras = false; importer.importLights = false;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.importAnimation = relative.StartsWith("Animations/");
                    importer.animationCompression = ModelImporterAnimationCompression.Off;
                    importer.optimizeGameObjects = false; importer.meshCompression = ModelImporterMeshCompression.Off;
                    importer.preserveHierarchy = true; importer.importBlendShapes = true;
                    importer.SaveAndReimport();
                }
                foreach(var t in a.textures)
                {
                    var imp = (TextureImporter)AssetImporter.GetAtPath(Dir("Texture",a.assetId)+"/"+Path.GetFileName(t.file));
                    imp.textureType = TextureImporterType.Default; imp.sRGBTexture = t.colorSpace == "sRGB";
                    imp.maxTextureSize = 2048; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp;
                    imp.textureCompression = TextureImporterCompression.Uncompressed; imp.SaveAndReimport();
                }
                Build(a);
                File.AppendAllText("Logs/music-weapon-import.log","IMPORTED "+a.assetId+"\n");
            }
            ReplaceConfig(plan);
            SAddressablesSetup.Build(); SAddressablesSetup.UseAssetDatabasePlayMode();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Validate();
            File.AppendAllText("Logs/music-weapon-import.log","RESULT COMPLETE 6 weapons\n");
        }
        static void Build(Asset a)
        {
            string id = a.assetId;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath(a,"Exports/"+id+"_Model.fbx"));
            var root = new GameObject(id);
            try
            {
                var modelGo = UnityEngine.Object.Instantiate(model,root.transform,false);
                modelGo.name = "Model";
                var material = new Material(Shader.Find("S/Music Character Cel")) {name="MAT_"+id};
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Dir("Texture",id)+"/T_"+id+"_BC.png"));
                material.SetTexture("_ORMMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Dir("Texture",id)+"/T_"+id+"_ORM.png"));
                material.SetTexture("_EmissionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Dir("Texture",id)+"/T_"+id+"_E.png"));
                material.SetFloat("_WeaponFill",.75f);material.SetColor("_BaseColor",Color.white);material.SetColor("_EmissionColor",new Color(.13f,.42f,.48f));
                material = SMusicAssetWriter.Save(material,Dir("Material",id)+"/MAT_"+id+".mat");
                var renderers = modelGo.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r=>r.name).ToArray();
                foreach(var renderer in renderers)
                {
                    var mesh = UnityEngine.Object.Instantiate(renderer.sharedMesh); mesh.name = renderer.sharedMesh.name;
                    renderer.sharedMesh = SMusicAssetWriter.Save(mesh,Dir("Model",id)+"/"+mesh.name+".asset");
                    renderer.sharedMaterials = Enumerable.Repeat(material,renderer.sharedMesh.subMeshCount).ToArray();
                    renderer.updateWhenOffscreen = true;renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.localBounds = new Bounds(renderer.localBounds.center,renderer.localBounds.size*3);
                }
                var animator = modelGo.GetComponent<Animator>() ?? modelGo.AddComponent<Animator>();
                if(animator.avatar)
                {
                    var avatar = UnityEngine.Object.Instantiate(animator.avatar);avatar.name="Avatar_"+id;
                    animator.avatar=SMusicAssetWriter.Save(avatar,Dir("Animation",id)+"/Avatar_"+id+".asset");
                }
                animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                // FBX already contains an LODGroup; the runtime root owns these renderers.
                foreach(var inherited in modelGo.GetComponentsInChildren<LODGroup>(true))
                    UnityEngine.Object.DestroyImmediate(inherited);
                var lod = root.AddComponent<LODGroup>();var lods = new LOD[3];
                for(int i=0;i<3;i++)lods[i]=new LOD(i==0?.35f:i==1?.12f:.01f,renderers.Where(r=>r.name.EndsWith("_LOD"+i)).Cast<Renderer>().ToArray());
                lod.SetLODs(lods);lod.RecalculateBounds();
                var view = root.AddComponent<MusicWeaponVisual>();view.AssetId=id;view.GunId=a.gunId;view.Animator=animator;
                view.States=a.animations.Select(c=>c.state).ToArray();view.Clips=new AnimationClip[a.animations.Length];
                for(int i=0;i<a.animations.Length;i++)
                {
                    var spec=a.animations[i];
                    var original=AssetDatabase.LoadAllAssetsAtPath(SourcePath(a,spec.file)).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
                    var clip=UnityEngine.Object.Instantiate(original);clip.name=spec.state;
                    clip.events=Array.Empty<AnimationEvent>();
                    var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=spec.loop;settings.loopBlend=false;
                    AnimationUtility.SetAnimationClipSettings(clip,settings);
                    view.Clips[i]=SMusicAssetWriter.Save(clip,Dir("Animation",id)+"/AN_"+id+"_"+spec.state+".anim");
                }
                view.Output=modelGo.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Socket_Output");
                SetPose(view);
                PrefabUtility.SaveAsPrefabAsset(root,Prefabs+"/"+id+".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void SetPose(MusicWeaponVisual v)
        {
            // Meter-scale models; only the grip placement is adapted for the camera.
            switch(v.GunId)
            {
                case "rifle":v.ViewPosition=new Vector3(.01f,-.16f,-.08f);v.ViewEuler=new Vector3(-24,-20,-8);break;
                case "smg":v.ViewPosition=new Vector3(-.03f,-.07f,-.02f);v.ViewEuler=new Vector3(-35,-10,0);break;
                case "shotgun":v.ViewPosition=new Vector3(.01f,-.08f,.02f);v.ViewEuler=new Vector3(-38,-15,-8);break;
                case "sniper":v.ViewPosition=new Vector3(.06f,-.16f,-.06f);v.ViewEuler=new Vector3(-14,-15,-12);break;
                case "beam":v.ViewPosition=new Vector3(.10f,-.16f,.08f);v.ViewEuler=new Vector3(8,-20,-12);break;
                default:v.ViewPosition=new Vector3(.03f,-.10f,.10f);v.ViewEuler=new Vector3(5,-12,-5);break;
            }
        }
        static void ReplaceConfig(Plan plan)
        {
            const string path=Res+"/Cfg/Gun.tsv";var rows=File.ReadAllLines(path);
            var header=rows[0].Split('\t');int column=Array.IndexOf(header,"model");
            for(int i=3;i<rows.Length;i++)
            {
                var cells=rows[i].Split('\t');if(cells.Length<=column)continue;
                var a=plan.assets.SingleOrDefault(x=>x.gunId==cells[0]);if(a==null)continue;
                cells[column]=a.assetId;rows[i]=string.Join("\t",cells);
            }
            File.WriteAllLines(path,rows,new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        }
        [MenuItem("S/Weapons/Validate Music Weapons")]
        public static void Validate()
        {
            log.Clear();pass=fail=0;
            foreach(var a in ReadPlan().assets)
            {
                string path=Prefabs+"/"+a.assetId+".prefab";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(a.assetId+" prefab exists",prefab);if(!prefab)continue;
                Check(a.assetId+" no imported-model or Editor dependencies",AssetDatabase.GetDependencies(path,true).All(p=>!p.Contains("/Editor/")&&!p.EndsWith(".fbx")));
                var instance=UnityEngine.Object.Instantiate(prefab);var view=instance.GetComponent<MusicWeaponVisual>();
                var original=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath(a,"Exports/"+a.assetId+"_Model.fbx")));
                try
                {
                    var sourceRenderers=original.GetComponentsInChildren<SkinnedMeshRenderer>().OrderBy(x=>x.name).ToArray();
                    var actual=instance.GetComponentsInChildren<SkinnedMeshRenderer>().OrderBy(x=>x.name).ToArray();
                    Check(a.assetId+" three LODs",actual.Length==3&&instance.GetComponentsInChildren<LODGroup>(true).Length==1&&instance.GetComponent<LODGroup>().GetLODs().All(l=>l.renderers.Length==1));
                    Check(a.assetId+" axis forward +Z",Vector3.Dot(view.Output.forward,Vector3.forward)>.999f);
                    Check(a.assetId+" native mesh, bones and texture material",actual.All(r=>r.sharedMesh&&r.sharedMesh.vertexCount>0&&r.bones.All(b=>b)&&r.sharedMaterials.All(m=>m&&m.GetTexture("_BaseMap"))));
                    for(int i=0;i<a.animations.Length;i++)
                    {
                        var spec=a.animations[i];var clip=view.Clips[i];
                        Check(a.assetId+"/"+spec.state+" duration",Mathf.Abs(clip.length-spec.duration)<.017f);
                        var originalClip=AssetDatabase.LoadAllAssetsAtPath(SourcePath(a,spec.file)).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
                        bool bound=AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Transform)).All(b=>view.Animator.transform.Find(b.path));
                        Check(a.assetId+"/"+spec.state+" curve paths resolve",bound);
                        float maxError=0;bool finite=true;
                        foreach(float t in new[]{0f,.25f,.5f,.75f,.99f})
                        {
                            originalClip.SampleAnimation(original,spec.duration*t);
                            clip.SampleAnimation(view.Animator.gameObject,spec.duration*t);
                            for(int j=0;j<actual.Length;j++)
                            {
                                var m1=new Mesh();var m2=new Mesh();sourceRenderers[j].BakeMesh(m1);actual[j].BakeMesh(m2);
                                var vs=m1.vertices;var va=m2.vertices;
                                if(vs.Length!=va.Length)finite=false;
                                for(int k=0;k<Math.Min(vs.Length,va.Length);k++)
                                {
                                    var p=sourceRenderers[j].transform.TransformPoint(vs[k]);var q=actual[j].transform.TransformPoint(va[k]);
                                    float e=Vector3.Distance(p,q);if(float.IsNaN(e)||float.IsInfinity(e))finite=false;maxError=Mathf.Max(maxError,e);
                                }
                                UnityEngine.Object.DestroyImmediate(m1);UnityEngine.Object.DestroyImmediate(m2);
                            }
                        }
                        Check(a.assetId+"/"+spec.state+" animated geometry matches source "+maxError.ToString("F7")+"m",finite&&maxError<.0001f);
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(instance);}
            }
            log.Add("RESULT "+pass+"/"+(pass+fail)+" passed");File.WriteAllLines("Logs/music-weapon-validation.log",log);
            if(fail>0)throw new Exception("Music weapon validation failed: "+fail);
        }
        public static void Batch()
        {
            try{ImportAll();Debug.Log("WEAPON_IMPORT_COMPLETE");EditorApplication.Exit(0);}
            catch(Exception e){File.AppendAllText("Logs/music-weapon-import.log","ERROR "+e);Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
