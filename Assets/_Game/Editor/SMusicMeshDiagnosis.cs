using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using S.View;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SMusicMeshDiagnosis
    {
        static SMusicMeshDiagnosis(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            const string request="Logs/music-mesh-diagnosis.request";
            if(!File.Exists(request))return;File.Delete(request);
            try{Run();}catch(Exception e){File.AppendAllText("Logs/music-mesh-diagnosis.log","ERROR "+e+"\n");Debug.LogException(e);}
        }
        static Vector3[] Vertices(SkinnedMeshRenderer r,Transform space)
        {
            var mesh=new Mesh();r.BakeMesh(mesh);
            var matrix=space.worldToLocalMatrix*r.transform.localToWorldMatrix;
            var points=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();UnityEngine.Object.DestroyImmediate(mesh);return points;
        }
        static float MatrixError(Matrix4x4 a,Matrix4x4 b){float m=0;for(int k=0;k<16;k++)m=Mathf.Max(m,Mathf.Abs(a[k]-b[k]));return m;}
        static void Log(string line)=>File.AppendAllText("Logs/music-mesh-diagnosis.log",line+"\n");
        [MenuItem("S/Enemies/Diagnose Music Skin Geometry")]
        public static void Run()
        {
            File.WriteAllText("Logs/music-mesh-diagnosis.log","START geometry comparison; meters\n");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                foreach(var a in SMusicEnemyImporter.ReadPlan().assets)
                {
                    var raw=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(a.model));
                    var baked=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SMusicEnemyImporter.Prefabs+"/"+a.id+".prefab"));
                    SceneManager.MoveGameObjectToScene(raw,scene);SceneManager.MoveGameObjectToScene(baked,scene);
                    raw.SetActive(false);baked.SetActive(false);
                    try
                    {
                        var visual=baked.GetComponent<EnemyVisual>();var target=visual.Animator.gameObject;
                        var orig=raw.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.EndsWith("_LOD0")).ToArray();
                        var merged=target.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name.EndsWith("_LOD0"));
                        Log("COUNTS "+a.id+" rawMesh="+orig.Sum(r=>r.sharedMesh.vertexCount)+" rawBaked="+orig.Sum(r=>Vertices(r,raw.transform).Length)+" mergedMesh="+merged.sharedMesh.vertexCount+" mergedBaked="+Vertices(merged,target.transform).Length);
                        for(int level=1;level<3;level++)
                        {
                            int expected=raw.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.EndsWith("_LOD"+level)).Sum(r=>r.sharedMesh.vertexCount);
                            var actual=target.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name.EndsWith("_LOD"+level)).sharedMesh;
                            Log("LODCOUNT "+a.id+" LOD"+level+" raw="+expected+" merged="+actual.vertexCount+" equal="+(expected==actual.vertexCount));
                        }
                        var probe=UnityEngine.Object.Instantiate(merged.sharedMesh);
                        var fresh=UnityEngine.Object.Instantiate(orig[0].sharedMesh);
                        int oldCount=probe.vertexCount;EditorUtility.CopySerialized(fresh,probe);
                        Log("COPY PROBE "+a.id+" old="+oldCount+" source="+fresh.vertexCount+" afterCopySerialized="+probe.vertexCount);
                        UnityEngine.Object.DestroyImmediate(probe);UnityEngine.Object.DestroyImmediate(fresh);
                        bool sameCount=orig.Sum(r=>r.sharedMesh.vertexCount)==merged.sharedMesh.vertexCount;
                        float mergedBindError=0;
                        for(int k=0;k<Mathf.Min(merged.bones.Length,merged.sharedMesh.bindposes.Length);k++)mergedBindError=Mathf.Max(mergedBindError,MatrixError(merged.sharedMesh.bindposes[k],merged.bones[k].worldToLocalMatrix*merged.transform.localToWorldMatrix));
                        Log("MERGED BIND "+a.id+" bones="+merged.bones.Length+" bindposes="+merged.sharedMesh.bindposes.Length+" restError="+mergedBindError.ToString("F6"));
                        float bindError=0;string bindPart="";int badWeight=0,badIndex=0;
                        foreach(var r in orig)
                        {
                            var m=r.sharedMesh;var conversion=raw.transform.worldToLocalMatrix*r.transform.localToWorldMatrix;
                            for(int j=0;j<r.bones.Length;j++)
                            {
                                float error=MatrixError(m.bindposes[j]*conversion.inverse,r.bones[j].worldToLocalMatrix*raw.transform.localToWorldMatrix);
                                if(error>bindError){bindError=error;bindPart=r.name+"/"+r.bones[j].name;}
                            }
                            foreach(var w in m.boneWeights)
                            {
                                if(Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)>.001f)badWeight++;
                                if(w.boneIndex0>=r.bones.Length||(w.weight1>0&&w.boneIndex1>=r.bones.Length)||(w.weight2>0&&w.boneIndex2>=r.bones.Length)||(w.weight3>0&&w.boneIndex3>=r.bones.Length))badIndex++;
                            }
                        }
                        Log(a.id+" raw parts="+orig.Length+" weightsInvalid="+badWeight+" indicesInvalid="+badIndex+" authoredVsRebuiltBind="+bindError.ToString("F6")+" part="+bindPart);
                        var rawPose=raw.GetComponentsInChildren<Transform>(true).Select(t=>(t,t.localPosition,t.localRotation,t.localScale)).ToArray();
                        var bakedPose=target.GetComponentsInChildren<Transform>(true).Select(t=>(t,t.localPosition,t.localRotation,t.localScale)).ToArray();
                        float max=0;string worst="";int mismatches=0;
                        foreach(var clip in sameCount?visual.Clips:Array.Empty<AnimationClip>())
                        {
                            float clipMax=0;float worstTime=0;string part="";
                            foreach(float f in new[]{0f,.1f,.25f,.5f,.75f,.9f,1f})
                            {
                                foreach(var p in rawPose){p.t.localPosition=p.localPosition;p.t.localRotation=p.localRotation;p.t.localScale=p.localScale;}
                                foreach(var p in bakedPose){p.t.localPosition=p.localPosition;p.t.localRotation=p.localRotation;p.t.localScale=p.localScale;}
                                clip.SampleAnimation(raw,clip.length*f);clip.SampleAnimation(target,clip.length*f);
                                var actual=Vertices(merged,target.transform);int cursor=0;
                                foreach(var r in orig)
                                {
                                    var points=Vertices(r,raw.transform);
                                    for(int k=0;k<points.Length;k++)
                                    {
                                        float error=sameCount?Vector3.Distance(points[k],actual[cursor+k]):0;
                                        if(error>clipMax){clipMax=error;worstTime=clip.length*f;part=r.name;}
                                    }
                                    cursor+=points.Length;
                                }

                            }
                            if(clipMax>.001f)mismatches++;
                            if(clipMax>max){max=clipMax;worst=clip.name+"@"+worstTime.ToString("F3")+" "+part;}
                            Log("  "+clip.name+" maxDelta="+clipMax.ToString("F6")+" time="+worstTime.ToString("F3")+" part="+part);
                        }
                        if(a.id=="S08_MetronomeGuard")
                        {
                            foreach(var p in rawPose){p.t.localPosition=p.localPosition;p.t.localRotation=p.localRotation;p.t.localScale=p.localScale;}
                            foreach(var p in bakedPose){p.t.localPosition=p.localPosition;p.t.localRotation=p.localRotation;p.t.localScale=p.localScale;}
                            foreach(var animator in raw.GetComponentsInChildren<Animator>(true))animator.enabled=false;
                            visual.Animator.enabled=false;visual.enabled=false;
                            var clip=visual.Clips.First(c=>c.name=="Sweep_Windup");clip.SampleAnimation(raw,.675f);clip.SampleAnimation(target,.675f);
                            foreach(var r in raw.GetComponentsInChildren<Renderer>(true))r.sharedMaterial=merged.sharedMaterial;
                            foreach(var lod in raw.GetComponentsInChildren<LODGroup>(true))lod.ForceLOD(0);
                            foreach(var lod in baked.GetComponentsInChildren<LODGroup>(true))lod.ForceLOD(0);
                            var cameraObject=new GameObject("SkinDiagnosticCamera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
                            var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.transform.position=new Vector3(5,3.5f,8);camera.transform.LookAt(new Vector3(0,1.5f,0));
                            camera.fieldOfView=36;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.24f,.27f,.33f);
                            raw.SetActive(true);baked.SetActive(false);SFoundryMapBuilder.Capture(camera,"Shots/music-s08-source-diagnosis.png");
                            raw.SetActive(false);baked.SetActive(true);SFoundryMapBuilder.Capture(camera,"Shots/music-s08-imported-diagnosis.png");baked.SetActive(false);
                            UnityEngine.Object.DestroyImmediate(cameraObject);
                        }
                        Log("SUMMARY "+a.id+" comparable="+sameCount+" maxDelta="+max.ToString("F6")+" mismatchedClips="+(sameCount?(mismatches+"/"+visual.Clips.Length):"SKIPPED_TOPOLOGY_MISMATCH")+" worst="+worst);
                    }
                    finally{UnityEngine.Object.DestroyImmediate(raw);UnityEngine.Object.DestroyImmediate(baked);}
                }
                Log("RESULT COMPLETE");
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
