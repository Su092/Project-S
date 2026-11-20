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
    public static class SMusicSupplementImporter
    {
        static readonly string[] Ids={"FXM_S14_NoteGate","SM_MusicFollowSpot"};
        static SMusicSupplementImporter(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            string request="Logs/music-supplements-import.request";
            if(!File.Exists(request))return;
            File.Delete(request);
            try{Build();}
            catch(Exception e){File.AppendAllText("Logs/music-supplements-import.log","ERROR "+e+"\n");Debug.LogException(e);}
        }
        static T Save<T>(T obj,string path) where T:UnityEngine.Object => SMusicAssetWriter.Save(obj,path);
        static ModelImporter Configure(string path,bool animation)
        {
            var m=(ModelImporter)AssetImporter.GetAtPath(path);
            m.globalScale=1;m.bakeAxisConversion=false;m.importAnimation=animation;m.animationType=ModelImporterAnimationType.Generic;
            m.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;m.materialImportMode=ModelImporterMaterialImportMode.None;
            m.importLights=false;m.importCameras=false;m.addCollider=false;m.isReadable=true;m.optimizeGameObjects=false;
            m.importNormals=ModelImporterNormals.Import;m.animationCompression=ModelImporterAnimationCompression.Off;
            return m;
        }
        static Texture2D Texture(string path,bool srgb)
        {
            var t=(TextureImporter)AssetImporter.GetAtPath(path);
            t.textureType=TextureImporterType.Default;t.sRGBTexture=srgb;t.mipmapEnabled=true;t.maxTextureSize=1024;
            t.alphaSource=TextureImporterAlphaSource.FromInput;t.alphaIsTransparency=false;t.wrapMode=TextureWrapMode.Clamp;
            t.textureCompression=TextureImporterCompression.CompressedHQ;t.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material Body(string id)
        {
            string dir=SMusicAssetLayout.Material(id),tex=SMusicAssetLayout.Texture(id)+"/T_"+id;
            var m=new Material(Shader.Find("S/Music Character Cel")){name="MAT_"+id};
            m.SetTexture("_BaseMap",Texture(tex+"_BC.png",true));m.SetTexture("_ORMMap",Texture(tex+"_ORM.png",false));
            m.SetTexture("_EmissionMap",Texture(tex+"_E.png",false));m.SetFloat("_Metallic",.65f);
            m.SetColor("_EmissionColor",new Color(1,.75f,.35f)*.7f);m.enableInstancing=true;
            return Save(m,dir+"/MAT_"+id+".mat");
        }
        static Material Accent(string id,Material body)
        {
            string dir=SMusicAssetLayout.Material(id);Material m;
            if(id=="FXM_S14_NoteGate")
            {
                m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetTexture("_BaseMap",Texture(SMusicAssetLayout.Texture(id)+"/T_"+id+"_Curtain.png",false));m.SetColor("_BaseColor",new Color(1,1,1,.65f));
                m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",5);m.SetFloat("_DstBlend",10);
                m.SetFloat("_ZWrite",0);m.SetFloat("_Cull",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType","Transparent");m.renderQueue=3000;
            }
            else
            {
                m=new Material(body);
                // The lens is a dedicated mesh/material. Its exported UVs need not land on the body atlas emission patch.
                m.SetTexture("_BaseMap",Texture2D.whiteTexture);m.SetColor("_BaseColor",new Color(.65f,.52f,.3f));
                // Built-in whiteTexture is not serialized into a persistent material asset.
                var mask=new Texture2D(1,1,TextureFormat.RGBA32,false,true){name="T_SM_MusicFollowSpot_LensEmission"};
                mask.SetPixel(0,0,Color.white);mask.Apply();
                m.SetTexture("_EmissionMap",Save(mask,SMusicAssetLayout.Texture(id)+"/T_SM_MusicFollowSpot_LensEmission.asset"));
                m.SetColor("_EmissionColor",new Color(1,.8f,.4f)*3);
                m.SetFloat("_Metallic",.15f);
            }
            m.name="MAT_"+id+"_Accent";return Save(m,dir+"/"+m.name+".mat");
        }
        static Transform Node(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).Single(x=>x.name==name);
        static void Rig(GameObject source,string id)
        {
            string dir=SMusicAssetLayout.Animation(id),model=SMusicAssetLayout.Source(id,"Models",id+".fbx");
            var avatar=AssetDatabase.LoadAllAssetsAtPath(model).OfType<Avatar>().First(x=>x.isValid);
            var animator=source.GetComponent<Animator>();if(!animator)animator=source.AddComponent<Animator>();
            string controllerPath=dir+"/"+id+".controller";var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if(!controller)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm=controller.layers[0].stateMachine;foreach(var s in sm.states)sm.RemoveState(s.state);
            foreach(var name in new[]{"Idle","FocusIn","FocusOut","RigTest"})
            {
                string path=SMusicAssetLayout.Source(id,"Animations",id+"_"+name+".fbx");
                var mi=Configure(path,true);mi.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;mi.sourceAvatar=avatar;mi.SaveAndReimport();
                var settings=mi.defaultClipAnimations;
                if(settings.Length!=1)throw new InvalidOperationException("Expected one clip: "+path);
                settings[0].name=name;settings[0].loopTime=name=="Idle";settings[0].loopPose=false;
                settings[0].lockRootRotation=true;settings[0].lockRootPositionXZ=true;settings[0].lockRootHeightY=true;mi.clipAnimations=settings;mi.SaveAndReimport();
                var imported=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
                var clip=new AnimationClip{name=name,frameRate=imported.frameRate};
                foreach(var original in AnimationUtility.GetCurveBindings(imported))
                {
                    if(string.IsNullOrEmpty(original.path))continue;
                    var b=original;
                    if(!source.transform.Find(b.path))
                    {
                        var candidates=source.GetComponentsInChildren<Transform>(true).Select(t=>AnimationUtility.CalculateTransformPath(t,source.transform))
                            .Where(p=>p.EndsWith("/"+b.path)).ToArray();
                        if(candidates.Length!=1)throw new InvalidOperationException("Unbound lamp "+name+"/"+b.path);
                        b.path=candidates[0];
                    }
                    AnimationUtility.SetEditorCurve(clip,b,AnimationUtility.GetEditorCurve(imported,original));
                }
                AnimationUtility.SetAnimationClipSettings(clip,AnimationUtility.GetAnimationClipSettings(imported));clip.EnsureQuaternionContinuity();
                clip=Save(clip,dir+"/"+name+".anim");clip.name=name;EditorUtility.SetDirty(clip);
                var state=sm.AddState(name);state.motion=clip;state.writeDefaultValues=true;
            }
            sm.defaultState=sm.states.First(s=>s.state.name=="Idle").state;EditorUtility.SetDirty(controller);
            animator.avatar=avatar;animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var rig=source.AddComponent<MusicFollowSpotRig>();rig.Animator=animator;rig.Yaw=Node(source.transform,"yaw");rig.Pitch=Node(source.transform,"pitch");
            rig.LightOrigin=Node(source.transform,"Socket_LightOrigin");rig.HeadPivot=Node(source.transform,"Socket_HeadPivot");
        }
        [MenuItem("S/Enemies/Import Music Skill Supplements")]
        public static void Build()
        {
            File.WriteAllText("Logs/music-supplements-import.log","START\n");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string id in Ids)
            {
                string path=SMusicAssetLayout.Source(id,"Models",id+".fbx");Configure(path,false).SaveAndReimport();
                var root=new GameObject(id);
                try
                {
                    var source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),root.transform,false);
                    var body=Body(id);var accent=Accent(id,body);
                    foreach(var r in source.GetComponentsInChildren<Renderer>(true))
                    {
                        bool detail=r.name.Contains("_Curtain_")||r.name.Contains("_Lens_");
                        r.sharedMaterial=detail?accent:body;
                        if(detail&&id=="FXM_S14_NoteGate"){r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;}
                        if(r is SkinnedMeshRenderer skin){skin.updateWhenOffscreen=true;var b=skin.localBounds;b.Expand(3);skin.localBounds=b;}
                    }
                    foreach(var c in source.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(c);
                    foreach(var l in source.GetComponentsInChildren<LODGroup>(true))UnityEngine.Object.DestroyImmediate(l);
                    var rs=source.GetComponentsInChildren<Renderer>(true);var lod=root.AddComponent<LODGroup>();
                    lod.SetLODs(Enumerable.Range(0,3).Select(i=>new LOD(new[]{.1f,.04f,.003f}[i],rs.Where(r=>r.name.EndsWith("_LOD"+i)).ToArray())).ToArray());lod.RecalculateBounds();
                    if(id=="SM_MusicFollowSpot")Rig(source,id);
                    SMusicNativeAssets.Detach(root,id);
                    PrefabUtility.SaveAsPrefabAsset(root,SMusicEnemyImporter.Prefabs+"/"+id+".prefab");
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
                File.AppendAllText("Logs/music-supplements-import.log","BUILT "+id+"\n");
            }
            SAddressablesSetup.Build();AssetDatabase.SaveAssets();Validate();
            File.AppendAllText("Logs/music-supplements-import.log","RESULT COMPLETE\n");
        }
        static void Validate()
        {
            var lines=new List<string>();int pass=0,fail=0;
            void Check(string name,bool ok){lines.Add((ok?"PASS ":"FAIL ")+name);if(ok)pass++;else fail++;}
            foreach(var id in Ids)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(SMusicEnemyImporter.Prefabs+"/"+id+".prefab");
                Check(id+" prefab",prefab);if(!prefab)continue;
                Check(id+" no blocking colliders",prefab.GetComponentsInChildren<Collider>(true).Length==0);
                var lod=prefab.GetComponent<LODGroup>();Check(id+" 3 complete LODs",lod&&lod.lodCount==3&&lod.GetLODs().All(l=>l.renderers.Length==2));
                Check(id+" assigned shaders",prefab.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterial&&r.sharedMaterial.shader&&!ShaderUtil.ShaderHasError(r.sharedMaterial.shader)));
                if(id=="FXM_S14_NoteGate")
                {
                    var rs=lod.GetLODs()[0].renderers;var bounds=rs[0].bounds;foreach(var r in rs.Skip(1))bounds.Encapsulate(r.bounds);
                    Check("gate dimensions 2.2 x 3.6 x .35",Vector3.Distance(bounds.size,new Vector3(2.2f,3.6f,.35f))<.02f&&Mathf.Abs(bounds.min.y)<.01f);
                    var curtain=rs.First(r=>r.name.Contains("Curtain")).sharedMaterial;
                    Check("curtain transparent double sided depth test",curtain.GetFloat("_Cull")==0&&curtain.GetFloat("_ZWrite")==0&&curtain.renderQueue==3000&&curtain.GetTexture("_BaseMap"));
                }
                else
                {
                    var rig=prefab.GetComponentInChildren<MusicFollowSpotRig>();
                    var lens=prefab.GetComponentsInChildren<Renderer>(true).First(r=>r.name.Contains("_Lens_"));
                    Check("lamp emission mask persists in asset",lens.sharedMaterial.GetTexture("_EmissionMap")&&!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(lens.sharedMaterial.GetTexture("_EmissionMap"))));
                    Check("lamp rig sockets",rig&&rig.Yaw&&rig.Pitch&&rig.LightOrigin&&rig.HeadPivot);
                    Check("lamp Generic avatar",rig&&rig.Animator.avatar&&rig.Animator.avatar.isValid&&!rig.Animator.avatar.isHuman);
                    var clips=rig.Animator.runtimeAnimatorController.animationClips;
                    Check("lamp 4 clips and timing",clips.Length==4&&clips.All(c=>Mathf.Abs(c.length-(c.name=="RigTest"?2:c.name=="Idle"?1:.5f))<.02f));
                    foreach(var clip in clips)Check("lamp bound "+clip.name,AnimationUtility.GetCurveBindings(clip).All(b=>rig.transform.Find(b.path)));
                }
            }
            Check("Addressable names unique",SAddressablesSetup.VerifyUnique(out var message));
            lines.Add("RESULT "+pass+"/"+(pass+fail)+"; failed "+fail);File.WriteAllLines("Logs/music-supplements-validation.log",lines);
            if(fail>0)throw new InvalidOperationException("Supplement validation failed: "+fail);
        }
    }
}
