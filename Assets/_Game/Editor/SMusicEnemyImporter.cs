using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SMusicEnemyImporter
    {
        public const string Root = SMusicAssetLayout.EditorRoot;
        public const string Prefabs = "Assets/_Game/Res/Prefab/Enemies";
        [Serializable] public class ClipInfo { public string state, file; public float duration, contact; public bool loop; }
        [Serializable] public class Proxy { public string node, bone, purpose; }
        [Serializable] public class AssetInfo
        {
            public string id, family, model, collision, textureRoot;
            public float height, hover, moveSpeed;
            public bool common;
            public ClipInfo[] clips;
            public Proxy[] proxies;
        }
        [Serializable] public class FxInfo { public string id, model, texture; }
        [Serializable] public class Plan { public AssetInfo[] assets; public FxInfo[] effects; }
        public static Plan ReadPlan() => JsonUtility.FromJson<Plan>(File.ReadAllText(Root+"/import-plan.json"));
        static SMusicEnemyImporter(){ EditorApplication.update += Poll; }
        static void Poll()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach(var action in new[]{"import","effects","validate"})
            {
                string request="Logs/music-enemy-"+action+".request";
                if(!File.Exists(request))continue;
                File.Delete(request);
                try { if(action=="import")Build();else if(action=="effects"){foreach(var fx in ReadPlan().effects)BuildFx(fx);SAddressablesSetup.Build();AssetDatabase.SaveAssets();Validate();Progress("RESULT COMPLETE");}else Validate(); }
                catch(Exception e){File.WriteAllText("Logs/music-enemy-error.log",e.ToString());Debug.LogException(e);}
            }
        }
        static void Progress(string s){File.AppendAllText("Logs/music-enemy-import.log",s+"\n");}
        static string Norm(string p)=>p.Replace('\\','/');
        static T Save<T>(T obj,string path) where T:UnityEngine.Object => SMusicAssetWriter.Save(obj,path);
        static ModelImporter Model(string path,bool anim)
        {
            var m=AssetImporter.GetAtPath(path) as ModelImporter;
            if(!m)throw new InvalidOperationException("Not imported: "+path);
            m.globalScale=1;m.bakeAxisConversion=false;m.importAnimation=anim;
            m.animationType=ModelImporterAnimationType.Generic;
            m.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            m.materialImportMode=ModelImporterMaterialImportMode.None;
            m.importCameras=false;m.importLights=false;m.addCollider=false;
            m.isReadable=true;m.optimizeGameObjects=false;m.importBlendShapes=false;
            m.importNormals=ModelImporterNormals.Import;
            m.importTangents=ModelImporterTangents.CalculateMikk;
            m.animationCompression=ModelImporterAnimationCompression.Off;
            return m;
        }
        static Texture2D Tex(string path,bool color)
        {
            var t=AssetImporter.GetAtPath(path) as TextureImporter;
            if(!t)throw new FileNotFoundException(path);
            t.textureType=TextureImporterType.Default;t.sRGBTexture=color;
            t.mipmapEnabled=true;t.maxTextureSize=2048;t.alphaIsTransparency=false;
            t.textureCompression=TextureImporterCompression.CompressedHQ;
            t.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material Material(AssetInfo a)
        {
            string path=SMusicAssetLayout.Material(a.family)+"/MAT_"+a.family+".mat";
            var mat=new Material(Shader.Find("S/Music Character Cel"));
            mat.name="MAT_"+a.family;
            mat.SetTexture("_BaseMap",Tex(a.textureRoot+"_BC.png",true));
            mat.SetTexture("_ORMMap",Tex(a.textureRoot+"_ORM.png",false));
            mat.SetTexture("_EmissionMap",Tex(a.textureRoot+"_E.png",false));
            mat.SetColor("_BaseColor",Color.white);mat.SetColor("_EmissionColor",new Color(1,.73f,.28f)*.9f);
            mat.SetFloat("_Metallic",.65f);mat.enableInstancing=true;
            return Save(mat,path);
        }
        static AnimationClip Clip(AssetInfo a,ClipInfo info,Avatar avatar,Transform model,string dir)
        {
            var mi=Model(info.file,true);mi.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;mi.sourceAvatar=avatar;mi.SaveAndReimport();
            var settings=mi.defaultClipAnimations;
            if(settings.Length!=1)throw new InvalidOperationException("Expected one action: "+info.file);
            settings[0].name=info.state;settings[0].loopTime=info.loop;settings[0].loopPose=false;
            settings[0].lockRootRotation=true;settings[0].lockRootPositionXZ=true;settings[0].lockRootHeightY=true;
            mi.clipAnimations=settings;mi.SaveAndReimport();
            var imported=AssetDatabase.LoadAllAssetsAtPath(info.file).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
            var clip=new AnimationClip{name=info.state,frameRate=imported.frameRate};
            foreach(var original in AnimationUtility.GetCurveBindings(imported))
            {
                if(string.IsNullOrEmpty(original.path))continue; // Keep FBX coordinate conversion at wrapper.
                var binding=original;
                if(!model.Find(binding.path))
                {
                    string prefixed="ARM_"+a.family+"/"+binding.path;
                    if(model.Find(prefixed))binding.path=prefixed;
                    else throw new InvalidOperationException("Unbound "+a.id+"/"+info.state+": "+binding.path);
                }
                AnimationUtility.SetEditorCurve(clip,binding,AnimationUtility.GetEditorCurve(imported,original));
            }
            AnimationUtility.SetAnimationClipSettings(clip,AnimationUtility.GetAnimationClipSettings(imported));
            clip.EnsureQuaternionContinuity();
            if(Mathf.Abs(clip.length-info.duration)>.04f)throw new InvalidOperationException("Duration "+a.id+"/"+info.state+" "+clip.length);
            var saved=Save(clip,dir+"/"+a.family+"_"+info.state+".anim");
            saved.name=info.state;EditorUtility.SetDirty(saved);return saved;
        }
        // Combine same-material parts into one skinned draw per LOD while keeping the authored skeleton.
        static SkinnedMeshRenderer Merge(GameObject source,int level,Material material,string assetDir,string id)
        {
            var original=source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.EndsWith("_LOD"+level)).ToArray();
            if(original.Length==0)throw new InvalidOperationException("Missing LOD "+id+"/"+level);
            var bones=original.SelectMany(r=>r.bones).Distinct().ToArray();
            var verts=new List<Vector3>();var norms=new List<Vector3>();var uv=new List<Vector2>();
            var weights=new List<BoneWeight>();var tris=new List<int>();
            foreach(var r in original)
            {
                var mesh=r.sharedMesh;int offset=verts.Count;
                var matrix=source.transform.worldToLocalMatrix*r.transform.localToWorldMatrix;
                var normal=matrix.inverse.transpose;
                verts.AddRange(mesh.vertices.Select(v=>matrix.MultiplyPoint3x4(v)));
                norms.AddRange(mesh.normals.Select(n=>normal.MultiplyVector(n).normalized));
                uv.AddRange(mesh.uv);
                foreach(var w0 in mesh.boneWeights)
                {
                    var w=w0;
                    w.boneIndex0=Array.IndexOf(bones,r.bones[w.boneIndex0]);
                    if(w.weight1>0)w.boneIndex1=Array.IndexOf(bones,r.bones[w.boneIndex1]);
                    if(w.weight2>0)w.boneIndex2=Array.IndexOf(bones,r.bones[w.boneIndex2]);
                    if(w.weight3>0)w.boneIndex3=Array.IndexOf(bones,r.bones[w.boneIndex3]);
                    weights.Add(w);
                }
                tris.AddRange(mesh.triangles.Select(t=>t+offset));
            }
            var combined=new Mesh{name="SM_"+id+"_LOD"+level};
            if(verts.Count>65535)combined.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            combined.SetVertices(verts);combined.SetNormals(norms);combined.SetUVs(0,uv);
            combined.boneWeights=weights.ToArray();
            combined.bindposes=bones.Select(b=>b.worldToLocalMatrix*source.transform.localToWorldMatrix).ToArray();
            combined.SetTriangles(tris,0);combined.RecalculateBounds();combined.RecalculateTangents();
            combined=Save(combined,assetDir+"/SM_"+id+"_LOD"+level+".asset");
            var go=new GameObject(combined.name);go.transform.SetParent(source.transform,false);
            var renderer=go.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=combined;
            renderer.bones=bones;renderer.rootBone=source.GetComponentsInChildren<Transform>(true).First(t=>t.name=="root");
            renderer.sharedMaterial=material;renderer.updateWhenOffscreen=true;
            var bounds=combined.bounds;bounds.Expand(4);renderer.localBounds=bounds;
            foreach(var r in original)UnityEngine.Object.DestroyImmediate(r); // Preserve any transform used by animation.
            return renderer;
        }
        static Collider[] Collision(AssetInfo a,GameObject source,EnemyVisual visual) => SMusicHitboxBuilder.Build(source,a);
        [MenuItem("S/Enemies/Rebuild Music Enemy Prefabs")]
        public static void Build()
        {
            Directory.CreateDirectory("Logs");File.Delete("Logs/music-enemy-error.log");File.WriteAllText("Logs/music-enemy-import.log","START\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var plan=ReadPlan();
            var clipCache=new Dictionary<string,AnimationClip[]>();
            foreach(var a in plan.assets)
            {
                Progress("BUILD "+a.id);
                Model(a.model,false).SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(a.model);
                var avatar=AssetDatabase.LoadAllAssetsAtPath(a.model).OfType<Avatar>().First(x=>x.isValid);
                string dir=SMusicAssetLayout.Model(a.id);
                string clipsDir=SMusicAssetLayout.Animation(a.family);
                var material=Material(a);
                // Variant models have identical bind paths; share clips by family.
                if(!clipCache.TryGetValue(a.family,out var clips))
                {
                    clips=a.clips.Select(c=>Clip(a,c,avatar,model.transform,clipsDir)).ToArray();
                    clipCache[a.family]=clips;
                }
                string controllerPath=SMusicAssetLayout.Animation(a.id)+"/"+a.id+".controller";
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if(!controller)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                var sm=controller.layers[0].stateMachine;
                foreach(var state in sm.states)sm.RemoveState(state.state);
                foreach(var clip in clips){var state=sm.AddState(clip.name);state.motion=clip;state.writeDefaultValues=true;}
                sm.defaultState=sm.states.First(s=>s.state.name=="Idle").state;EditorUtility.SetDirty(controller);
                var root=new GameObject(a.id);
                try
                {
                    var source=UnityEngine.Object.Instantiate(model,root.transform,false);source.name=model.name;
                    var rs=new List<SkinnedMeshRenderer>();
                    for(int i=0;i<3;i++)rs.Add(Merge(source,i,material,dir,a.id));
                    var b=BoundsOf(rs[0]);
                    if(Mathf.Abs(b.size.y-a.height)>.035f)throw new InvalidOperationException("Height mismatch "+a.id+" "+b.size);
                    source.transform.localPosition+=Vector3.up*(a.hover-b.min.y);
                    var visual=root.AddComponent<EnemyVisual>();visual.AssetId=a.id;visual.Dimensions=b.size;
                    visual.Animator=source.GetComponentInChildren<Animator>();
                    visual.Animator.avatar=avatar;visual.Animator.runtimeAnimatorController=controller;visual.Animator.applyRootMotion=false;
                    visual.ClipNames=a.clips.Select(c=>c.state).ToArray();visual.Clips=clips;
                    visual.IdleState="Idle";visual.HitState="Hit";visual.DeathState=a.clips.Any(c=>c.state=="Death")?"Death":"Destroy";
                    visual.MoveState=new[]{"Run","Fly","FloatMove","Move","Walk"}.FirstOrDefault(n=>visual.ClipNames.Contains(n));
                    visual.MoveCycleMetres=a.moveSpeed;
                    visual.AttackState=new[]{"Attack_Primary","Slam_Strike","Strike_Plate_01","Vent_Windup_L","Pluck_Beat","Conduct_Stagger","Fire"}.First(n=>visual.ClipNames.Contains(n));
                    var attack=a.clips.First(c=>c.state==visual.AttackState);
                    visual.AttackContactNormalized=attack.contact>0?attack.contact/attack.duration:.5f;
                    var bones=source.GetComponentsInChildren<Transform>(true);
                    visual.Aim=bones.FirstOrDefault(t=>t.name=="Socket_HitCenter"||t.name=="Socket_Core");
                    visual.Muzzle=bones.FirstOrDefault(t=>t.name=="Socket_Muzzle"||t.name=="Socket_Mouth"||t.name=="Socket_NoteOrigin"||t.name=="Socket_BatonTip");
                    if(!visual.Aim)throw new InvalidOperationException("No aim "+a.id);
                    // Keep the floor-following controller below the visible hover; decorative wings/pipes do not block air.
                    visual.MovementHeight=Mathf.Min(a.height,a.family.StartsWith("S21")?1.44f:a.height);
                    visual.MovementRadius=Mathf.Clamp(Mathf.Min(b.size.x,b.size.z)*.30f,.18f,.55f);
                    visual.MovementHeight=Mathf.Max(visual.MovementHeight,visual.MovementRadius*2);
                    visual.MovementCenter=new Vector3(0,visual.MovementHeight*.5f,0);
                    visual.HitColliders=Collision(a,source,visual);
                    var group=root.AddComponent<LODGroup>();
                    group.SetLODs(rs.Select((r,i)=>new LOD(new[]{.18f,.075f,.012f}[i],new Renderer[]{r})).ToArray());group.RecalculateBounds();
                    SMusicNativeAssets.Detach(root,a.id);
                    PrefabUtility.SaveAsPrefabAsset(root,Prefabs+"/"+a.id+".prefab");
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
                AssetDatabase.SaveAssets();
            }
            foreach(var fx in plan.effects)BuildFx(fx);
            SAddressablesSetup.Build();AssetDatabase.SaveAssets();
            Validate();Progress("RESULT COMPLETE "+plan.assets.Length+" animated prefabs; "+plan.effects.Length+" effects");
        }
        static void BuildFx(FxInfo fx)
        {
            Model(fx.model,false).SaveAndReimport();
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fx.model));root.name=fx.id;
            try
            {
                var mat=new Material(Shader.Find("S/Music Character Cel"));mat.SetTexture("_BaseMap",Tex(fx.texture,true));
                mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Metallic",.4f);
                mat=Save(mat,SMusicAssetLayout.Material(fx.id)+"/MAT_"+fx.id+".mat");
                foreach(var r in root.GetComponentsInChildren<Renderer>(true))r.sharedMaterial=mat;
                var rs=root.GetComponentsInChildren<Renderer>(true);
                var levels=rs.GroupBy(r=>r.name.Contains("_LOD1")?1:r.name.Contains("_LOD2")?2:0).OrderBy(g=>g.Key).ToArray();
                var lod=root.GetComponent<LODGroup>();if(!lod)lod=root.AddComponent<LODGroup>();lod.SetLODs(levels.Select(g=>new LOD(g.Key==0?.08f:.012f,g.ToArray())).ToArray());lod.RecalculateBounds();
                SMusicNativeAssets.Detach(root,fx.id);
                PrefabUtility.SaveAsPrefabAsset(root,Prefabs+"/"+fx.id+".prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        public static Bounds BoundsOf(SkinnedMeshRenderer r)=>SEnemyImporter.MeshBounds(r);
        [MenuItem("S/Enemies/Validate Music Delivery")]
        public static void Validate()
        {
            var log=new List<string>();int pass=0,fail=0,clips=0;
            void Check(string s,bool ok){log.Add((ok?"PASS ":"FAIL ")+s);if(ok)pass++;else fail++;}
            foreach(var a in ReadPlan().assets)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"/"+a.id+".prefab");
                Check(a.id+" prefab",prefab);if(!prefab)continue;
                var go=UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var v=go.GetComponent<EnemyVisual>();
                    var r=go.GetComponentsInChildren<SkinnedMeshRenderer>().First(x=>x.name.EndsWith("_LOD0"));
                    var b=BoundsOf(r);Check(a.id+" locked height "+b.size.y,Mathf.Abs(b.size.y-a.height)<.035f);
                    Check(a.id+" floor or hover "+b.min.y,Mathf.Abs(b.min.y-a.hover)<.01f);
                    Check(a.id+" three merged LOD",go.GetComponent<LODGroup>().GetLODs().Length==3&&go.GetComponentsInChildren<SkinnedMeshRenderer>().Length==3);
                    Check(a.id+" cel material",r.sharedMaterial.shader.name=="S/Music Character Cel"&&r.sharedMaterial.GetTexture("_BaseMap"));
                    Check(a.id+" hit geometry",v.HitColliders.Length>0&&v.HitColliders.All(c=>c&&c.isTrigger&&c is BoxCollider box&&box.size.x>0&&box.size.y>0&&box.size.z>0));
                    Check(a.id+" avatar",v.Animator.avatar&&v.Animator.avatar.isValid);
                    Check(a.id+" states",!string.IsNullOrEmpty(v.MoveState)||a.id=="S29_NoteTurret");
                    var nodes=v.Animator.GetComponentsInChildren<Transform>(true);
                    var pose=nodes.Select(t=>(t,t.localPosition,t.localRotation,t.localScale)).ToArray();
                    var bone=nodes.First(t=>t.name=="root");var origin=bone.localPosition;
                    foreach(var clip in v.Clips)
                    {
                        foreach(var p in pose){p.t.localPosition=p.localPosition;p.t.localRotation=p.localRotation;p.t.localScale=p.localScale;}
                        clip.SampleAnimation(v.Animator.gameObject,clip.length*.51f);
                        var moved=pose.Any(p=>Vector3.Distance(p.t.localPosition,p.localPosition)>.0001f||Quaternion.Angle(p.t.localRotation,p.localRotation)>.05f);
                        Check(a.id+"/"+clip.name+" bound motion",moved);clips++;
                        Check(a.id+"/"+clip.name+" in place",Vector3.Distance(bone.localPosition,origin)<.002f);
                        Check(a.id+"/"+clip.name+" time",Mathf.Abs(clip.length-a.clips.First(c=>c.state==clip.name).duration)<.04f);
                    }
                    if(a.common)Check(a.id+" quick death",Mathf.Abs(v.ClipLength("Death")-.4f)<.015f);
                }
                finally{UnityEngine.Object.DestroyImmediate(go);}
            }
            foreach(var fx in ReadPlan().effects)Check(fx.id+" prefab",AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"/"+fx.id+".prefab"));
            log.Add("RESULT "+pass+"/"+(pass+fail)+" passed; sampled "+clips+" clips");
            File.WriteAllLines("Logs/music-enemy-validation.log",log);
            if(fail>0)throw new InvalidOperationException(log.Last());
        }
    }
}
