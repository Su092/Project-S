using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SEnemyImporter
    {
        public const string Source = "_Inbox/chr/S_Foundry_Enemies/Review_v002";
        public const string Content = "Assets/_Game/Res/Characters/Foundry";
        public const string Prefabs = "Assets/_Game/Res/Prefab/Enemies";
        public static readonly string[] Names = { "SlagHound", "FurnaceWarden", "PressureRunner", "RingSentry",
            "SlagHound_Elite_MoldBreaker", "FurnaceWarden_Elite_TwinGate",
            "PressureRunner_Elite_TriplePressure", "RingSentry_Elite_Crown", EnemyAssets.Boss };
        public static readonly Vector3[] Sizes = { new(1.1f,.72f,1.65f), new(1.65f,2.45f,1.2f),
            new(1.05f,1.55f,1.1f), new(1.6f,1.25f,.9f), new(1.35f,1f,1.95f), new(2f,2.85f,1.45f),
            new(1.45f,1.85f,1.4f), new(2.05f,1.65f,1.15f), new(5.6f,5.8f,5f) };
        static double next;
        static SEnemyImporter() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2;
            if (File.Exists("Logs/enemy-import.request") || File.Exists("Logs/enemy-validate.request") || File.Exists("Logs/enemy-probe.request"))
            {
                var newest=Directory.GetFiles("Assets/_Game/Editor","*.cs").Max(File.GetLastWriteTimeUtc);
                if(newest>File.GetLastWriteTimeUtc("Library/ScriptAssemblies/Assembly-CSharp-Editor.dll"))
                { AssetDatabase.Refresh(); return; }
            }

            if (File.Exists("Logs/enemy-probe.request"))
            {
                File.Delete("Logs/enemy-probe.request"); Probe();
            }
            if (File.Exists("Logs/enemy-import.request"))
            {
                File.Delete("Logs/enemy-import.request");
                try { Build(); } catch(Exception e) { File.WriteAllText("Logs/enemy-import-error.log",e.ToString()); Debug.LogException(e); }
            }
            if (File.Exists("Logs/enemy-validate.request"))
            {
                File.Delete("Logs/enemy-validate.request");
                try { Validate(); } catch(Exception e) { File.WriteAllText("Logs/enemy-validate-error.log",e.ToString()); Debug.LogException(e); }
            }
        }
        static void Probe()
        {
            var lines=new List<string>();
            foreach(var file in new[]{"SlagHound_Model.fbx","AN_SlagHound_Attack_Clamp.fbx"})
            {
                string path=PathFor("SlagHound")+"/"+file;
                var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                lines.Add(file);
                foreach(var t in go.GetComponentsInChildren<Transform>(true))
                    lines.Add(AnimationUtility.CalculateTransformPath(t,go.transform)+" pos="+t.localPosition+" rot="+t.localEulerAngles+" scale="+t.localScale);
                foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")))
                {
                    foreach(var binding in AnimationUtility.GetCurveBindings(clip).Take(80))
                    {
                        var curve=AnimationUtility.GetEditorCurve(clip,binding);
                        lines.Add(binding.path+":"+binding.propertyName+" "+binding.type+" first="+curve.Evaluate(0)+" mid="+curve.Evaluate(clip.length*.52f));
                    }
                }
            }
            File.WriteAllLines("Logs/enemy-anim-probe.log",lines);
        }
        static string Family(string n) => n.Contains("_Elite_") ? n.Substring(0,n.IndexOf("_Elite_",StringComparison.Ordinal)) : n;
        static string PathFor(string n) => Content + "/" + n;
        static void Copy(string from,string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (!File.Exists(to) || !File.ReadAllBytes(from).SequenceEqual(File.ReadAllBytes(to))) File.Copy(from,to,true);
        }
        [MenuItem("S/Enemies/Import Foundry Delivery")]
        public static void Build()
        {
            Directory.CreateDirectory("Logs");
            File.Delete("Logs/enemy-import-error.log");
            File.Delete("Logs/enemy-import.log");
            foreach(var n in Names)
            {
                foreach(var f in Directory.GetFiles(Source+"/"+n+"/Exports","*.fbx"))
                    if (!f.Contains("Fragment")) Copy(f,PathFor(n)+"/"+Path.GetFileName(f));
                var tex = Source+"/"+n+"/Textures";
                if (Directory.Exists(tex))
                    foreach(var f in Directory.GetFiles(tex,"*.png")) Copy(f,PathFor(n)+"/Textures/"+Path.GetFileName(f));
            }
            Directory.CreateDirectory(Prefabs);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var n in Names) ImportAsset(n);
            AssetDatabase.SaveAssets();
            SAddressablesSetup.Build();
            AssetDatabase.SaveAssets();
            Validate();
            File.WriteAllText("Logs/enemy-import.log","RESULT Imported 9 Foundry enemy prefabs from Review_v002\n");
        }
        static ModelImporter ConfigureModel(string path,bool animation)
        {
            var mi=(ModelImporter)AssetImporter.GetAtPath(path);
            mi.globalScale=1f;mi.bakeAxisConversion=false;
            mi.importAnimation=animation;mi.animationType=ModelImporterAnimationType.Generic;
            mi.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            mi.materialImportMode=ModelImporterMaterialImportMode.None;
            mi.importCameras=false;mi.importLights=false;mi.importBlendShapes=false;
            mi.isReadable=true;mi.optimizeGameObjects=false;mi.addCollider=false;
            mi.importNormals=ModelImporterNormals.Import;
            mi.importTangents=ModelImporterTangents.CalculateMikk;
            mi.animationCompression=ModelImporterAnimationCompression.Off;
            return mi;
        }
        static Material MaterialFor(string family,string textureKey)
        {
            string dir=PathFor(family), path=dir+"/MAT_"+textureKey+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path); }
            Texture2D Tex(string suffix)
            {
                string tp=dir+"/Textures/T_"+textureKey+"_"+suffix+".png";
                var ti=(TextureImporter)AssetImporter.GetAtPath(tp);
                if (ti==null) throw new FileNotFoundException(tp);
                ti.textureType=suffix=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
                ti.sRGBTexture=suffix=="BaseColor"||suffix=="Emission";
                ti.mipmapEnabled=true;ti.alphaIsTransparency=false;ti.wrapMode=TextureWrapMode.Repeat;
                ti.maxTextureSize=2048;ti.textureCompression=TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            }
            mat.SetTexture("_BaseMap",Tex("BaseColor"));mat.SetColor("_BaseColor",Color.white);
            mat.SetTexture("_BumpMap",Tex("Normal"));mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap",Tex("MetallicSmoothness"));mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness",1f);mat.SetFloat("_SmoothnessTextureChannel",0);
            mat.SetTexture("_EmissionMap",Tex("Emission"));mat.SetColor("_EmissionColor",Color.white*.85f);
            mat.EnableKeyword("_EMISSION");mat.enableInstancing=true;
            EditorUtility.SetDirty(mat); return mat;
        }
        static void ImportAsset(string n)
        {
            string dir=PathFor(n),mp=dir+"/"+n+"_Model.fbx";
            ConfigureModel(mp,false).SaveAndReimport();
            string cp=dir+"/"+n+"_CollisionReference.fbx";
            ConfigureModel(cp,false).SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(mp);
            var avatar=AssetDatabase.LoadAllAssetsAtPath(mp).OfType<Avatar>().First(x=>x.isValid);
            var names=new List<string>();var clips=new List<AnimationClip>();
            foreach(var path0 in Directory.GetFiles(dir,"AN_*.fbx").OrderBy(x=>x))
            {
                var path=path0.Replace('\\','/');
                string state=Path.GetFileNameWithoutExtension(path).Substring(("AN_"+n+"_").Length);
                var mi=ConfigureModel(path,true);
                mi.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;mi.sourceAvatar=avatar;
                mi.SaveAndReimport();
                var settings=mi.defaultClipAnimations;
                if (settings.Length!=1) throw new InvalidOperationException("Expected one clip: "+path);
                settings[0].name=state;
                settings[0].loopTime=state.StartsWith("Idle")||state.StartsWith("Run")||state.StartsWith("Walk")||state=="Aim";
                settings[0].loopPose=false;settings[0].lockRootRotation=true;
                settings[0].lockRootHeightY=true;settings[0].lockRootPositionXZ=true;
                mi.clipAnimations=settings;mi.SaveAndReimport();
                var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(x=>!x.name.StartsWith("__preview__"));
                names.Add(state);clips.Add(RemapClip(n,state,clip,model.transform));
            }
            string controllerPath=dir+"/"+n+".controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller) controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm=controller.layers[0].stateMachine;
            foreach(var state in sm.states)sm.RemoveState(state.state);
            for(int i=0;i<clips.Count;i++){var state=sm.AddState(names[i]);state.motion=clips[i];state.writeDefaultValues=true;}
            string idle=names.First(x=>x.StartsWith("Idle"));
            sm.defaultState=sm.states.First(x=>x.state.name==idle).state;
            EditorUtility.SetDirty(controller);

            var root=new GameObject(n);
            var source=(GameObject)PrefabUtility.InstantiatePrefab(model,root.transform);
            var collision=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(cp),root.transform);
            try
            {
                var visual=root.AddComponent<EnemyVisual>();visual.AssetId=n;
                visual.Dimensions=Sizes[Array.IndexOf(Names,n)];
                bool flying=n.StartsWith("RingSentry");
                var renderers=source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var lod0=renderers.Single(x=>x.name.EndsWith("_LOD0"));
                var bounds=MeshBounds(lod0);
                source.transform.localPosition+=Vector3.up*(flying ? visual.Dimensions.y*.5f+.4f : -bounds.min.y);
                collision.transform.localPosition=source.transform.localPosition;
                var normal=MaterialFor(Family(n),Family(n));
                var mats=n==EnemyAssets.Boss?new[]{normal,MaterialFor(n,"Boss_Mechanisms")}:new[]{normal};
                foreach(var r in renderers){r.sharedMaterials=mats;r.updateWhenOffscreen=true;r.enabled=true;r.gameObject.SetActive(true);}
                var lod=root.AddComponent<LODGroup>();
                var levels=new List<LOD>();
                for(int i=0;i<3;i++)levels.Add(new LOD(new[]{.18f,.075f,.015f}[i],renderers.Where(x=>x.name.EndsWith("_LOD"+i)).Cast<Renderer>().ToArray()));
                lod.SetLODs(levels.ToArray());lod.RecalculateBounds();
                visual.Animator=source.GetComponentInChildren<Animator>();
                visual.Animator.avatar=avatar;visual.Animator.runtimeAnimatorController=controller;visual.Animator.applyRootMotion=false;
                visual.IdleState=idle;visual.MoveState=names.FirstOrDefault(x=>x.StartsWith("Run")||x.StartsWith("Walk"));
                visual.AttackState=names.FirstOrDefault(x=>x.StartsWith("Attack"))??(flying?"Fire":n.StartsWith("Pressure")?"Arm_Overload":"Volley");
                visual.HitState=names.FirstOrDefault(x=>x=="Hit"||x=="Hit_Front")??"Stagger";
                visual.DeathState=names.First(x=>x.StartsWith("Death"));
                visual.ClipNames=names.ToArray();visual.Clips=clips.ToArray();
                var bones=source.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
                visual.Muzzle=bones.TryGetValue("SOCKET_Muzzle",out var muzzle)?muzzle:
                    bones.TryGetValue("SOCKET_Volley_C",out muzzle)?muzzle:null;
                var aim=new GameObject("AimPoint").transform;aim.SetParent(bones["body"],false);
                aim.position=MeshBounds(lod0).center;visual.Aim=aim;
                visual.MovementHeight=flying?.55f:visual.Dimensions.y;
                visual.MovementRadius=Mathf.Min(visual.Dimensions.x,visual.Dimensions.z)*.27f;
                visual.MovementRadius=Mathf.Min(visual.MovementRadius,visual.MovementHeight*.48f);
                visual.MovementCenter=new Vector3(0,flying?visual.Dimensions.y*.5f+.4f:visual.MovementHeight*.5f,0);
                visual.HitColliders=BuildHitProxies(n,collision,bones);
                if(n==EnemyAssets.Boss)
                {
                    var snapshot=source.GetComponentsInChildren<Transform>(true).Select(t=>(t,t.localPosition,t.localRotation,t.localScale)).ToArray();
                    var doors=bones.Values.Where(t=>t.name.StartsWith("door_")||t.name.StartsWith("lock_")).ToArray();
                    var poses=doors.Select(t=>new EnemyVisual.PoseBinding{Bone=t,Closed=t.localPosition}).ToArray();
                    var open=clips[names.IndexOf("Shield_Open")];open.SampleAnimation(source,open.length);
                    foreach(var p in poses)p.Open=p.Bone.localPosition;
                    foreach(var t in snapshot){t.t.localPosition=t.localPosition;t.t.localRotation=t.localRotation;t.t.localScale=t.localScale;}
                    visual.ShieldPose=poses;
                }
                UnityEngine.Object.DestroyImmediate(collision);collision=null;
                PrefabUtility.SaveAsPrefabAsset(root,Prefabs+"/"+n+".prefab");
                File.AppendAllText("Logs/enemy-import-progress.log",n+" clips="+clips.Count+" hitProxies="+visual.HitColliders.Length+"\n");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        static AnimationClip RemapClip(string asset,string state,AnimationClip imported,Transform model)
        {
            // Animation-only FBX strips the ARM_<asset> wrapper which the mesh FBX retains.
            // Rebind every authored curve to the model hierarchy; do not rotate the actor root.
            string prefix="ARM_"+asset;
            if(!model.Find(prefix))throw new InvalidOperationException("Missing armature wrapper "+asset);
            var clip=new AnimationClip{name=state,frameRate=imported.frameRate,legacy=false};
            foreach(var original in AnimationUtility.GetCurveBindings(imported))
            {
                if (string.IsNullOrEmpty(original.path)) continue; // Preserve the model wrapper conversion.
                var binding=original;
                binding.path=prefix+(string.IsNullOrEmpty(original.path)?"":"/"+original.path);
                if(!model.Find(binding.path))throw new InvalidOperationException("Unbound animation curve "+binding.path);
                AnimationUtility.SetEditorCurve(clip,binding,AnimationUtility.GetEditorCurve(imported,original));
            }
            AnimationUtility.SetAnimationClipSettings(clip,AnimationUtility.GetAnimationClipSettings(imported));
            clip.EnsureQuaternionContinuity();
            string folder=PathFor(asset)+"/Animations";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            string path=folder+"/"+asset+"_"+state+".anim";
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);return existing;}
            AssetDatabase.CreateAsset(clip,path);return clip;
        }
        [Serializable] class ProxyData { public string name,bone; public float[] vertices; public int[] triangles; }
        [Serializable] class ProxyFile { public ProxyData[] proxies; }
        static Collider[] BuildHitProxies(string name,GameObject collision,Dictionary<string,Transform> bones)
        {
            var list=new List<Collider>();string dir=PathFor(name)+"/HitMeshes";
            Directory.CreateDirectory(dir);AssetDatabase.Refresh();
            var data=JsonUtility.FromJson<ProxyFile>(File.ReadAllText(PathFor(name)+"/CollisionProxies.json"));
            foreach(var p in data.proxies)
            {
                if(!bones.TryGetValue(p.bone,out var target))throw new InvalidOperationException("Missing proxy bone "+p.bone);
                var mesh=new Mesh();var vertices=new Vector3[p.vertices.Length/3];
                for(int i=0;i<vertices.Length;i++)
                {
                    var at=new Vector3(p.vertices[i*3],p.vertices[i*3+1],p.vertices[i*3+2]);
                    vertices[i]=target.InverseTransformPoint(at+collision.transform.localPosition);
                }
                mesh.vertices=vertices;mesh.triangles=p.triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.name=p.name;
                string path=dir+"/"+p.name+".asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}
                else AssetDatabase.CreateAsset(mesh,path);
                var proxy=new GameObject(p.name);proxy.layer=LayerMask.NameToLayer(WorldPhysics.ShotLayer);
                proxy.transform.SetParent(target,false);
                var col=proxy.AddComponent<MeshCollider>();col.sharedMesh=mesh;col.convex=true;col.isTrigger=true;
                list.Add(col);
            }
            if(list.Count==0)throw new InvalidOperationException("No hit proxies "+name);
            return list.ToArray();
        }
        public static Bounds MeshBounds(SkinnedMeshRenderer r)
        {
            var mesh=new Mesh();r.BakeMesh(mesh);
            var v=mesh.vertices;var b=new Bounds(r.transform.TransformPoint(v[0]),Vector3.zero);
            foreach(var p in v)b.Encapsulate(r.transform.TransformPoint(p));
            UnityEngine.Object.DestroyImmediate(mesh);return b;
        }
        [MenuItem("S/Enemies/Validate Foundry Prefabs")]
        public static void Validate()
        {
            var lines=new List<string>();int pass=0,fail=0,clipCount=0;
            void Check(string label,bool ok){lines.Add((ok?"PASS ":"FAIL ")+label);if(ok)pass++;else fail++;}
            foreach(var n in Names)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"/"+n+".prefab");
                Check(n+" prefab",prefab);if(!prefab)continue;
                var go=UnityEngine.Object.Instantiate(prefab);go.transform.position=Vector3.zero;
                try
                {
                    var v=go.GetComponent<EnemyVisual>();Check(n+" binding",v&&v.Animator&&v.Aim);
                    var rs=go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    var r=rs.Single(x=>x.name.EndsWith("_LOD0"));var b=MeshBounds(r);
                    Check(n+" dimensions "+b.size,(b.size-v.Dimensions).magnitude<.012f);
                    Check(n+" floor/hover "+b.min.y,Mathf.Abs(b.min.y-(n.StartsWith("Ring")?.4f:0))<.012f);
                    var front=go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="SOCKET_Attack"||t.name=="SOCKET_Muzzle"||t.name=="SOCKET_Volley_C"||t.name=="Tripod0_foot");
                    Check(n+" forward +Z "+(front?go.transform.InverseTransformPoint(front.position).z:0),front&&go.transform.InverseTransformPoint(front.position).z>.1f);
                    Check(n+" Generic Avatar",v.Animator.avatar&&v.Animator.avatar.isValid&&!v.Animator.avatar.isHuman);
                    Check(n+" 3 LOD",go.GetComponent<LODGroup>().GetLODs().All(l=>l.renderers.Length==1)&&rs.Length==3);
                    Check(n+" URP materials",rs.All(x=>x.sharedMaterials.All(m=>m&&m.shader.name=="Universal Render Pipeline/Lit"&&m.GetTexture("_BaseMap")&&m.GetTexture("_BumpMap"))));
                    Check(n+" clips",v.Clips.Length==v.ClipNames.Length&&v.Clips.All(c=>c&&c.length>0));
                    clipCount+=v.Clips.Length;
                    Check(n+" hit proxies",v.HitColliders.Length>0&&v.HitColliders.All(c=>c&&c.isTrigger&&c.gameObject.layer==LayerMask.NameToLayer(WorldPhysics.ShotLayer)));
                    Check(n+" no rigid split",go.GetComponentInChildren<EnemyMotion>()==null);
                    var root=v.Animator.GetComponentsInChildren<Transform>().First(t=>t.name=="root");
                    Vector3 origin=root.localPosition;
                    foreach(var c in v.Clips)
                    {
                        c.SampleAnimation(v.Animator.gameObject,c.length*.52f);
                        Check(n+"/"+c.name+" in place",Vector3.Distance(root.localPosition,origin)<.002f);
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(go);}
            }
            Check("56 delivered clips",clipCount==56);
            lines.Add("RESULT Enemy import "+pass+"/"+(pass+fail)+" passed");
            Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/enemy-import-check.log",lines);
            if(fail>0)throw new InvalidOperationException(lines.Last());
            Debug.Log(lines.Last());
        }
    }
}
