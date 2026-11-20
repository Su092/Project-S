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
    public static class SMusicGeometryCheck
    {
        const string LogPath="Logs/music-geometry-validation.log";
        static int pass,fail,poses;static float maximum;
        static SMusicGeometryCheck(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            foreach(string action in new[]{"repair","verify"})
            {
                string request="Logs/music-mesh-"+action+".request";
                if(!File.Exists(request))continue;File.Delete(request);
                try
                {
                    if(action=="repair")
                    {
                        File.WriteAllText("Logs/music-mesh-repair.log","START\n");
                        WriterRegression();
                        SMusicEnemyImporter.Build();
                        SMusicSupplementImporter.Build();
                        SMusicNativeAssets.Validate();
                    }
                    Validate();
                    File.AppendAllText("Logs/music-mesh-repair.log","RESULT "+action+" COMPLETE\n");
                }
                catch(Exception e){File.AppendAllText("Logs/music-mesh-repair.log","ERROR "+e+"\n");Debug.LogException(e);}
            }
        }
        static void Require(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
        static void WriterRegression()
        {
            const string path="Assets/_Game/Editor/MusicMeshCopyRegressionTemp.asset";
            Require(!File.Exists(path),"Temporary regression path already exists");
            var old=new Mesh{name="OldMesh"};
            old.vertices=new[]{Vector3.zero,Vector3.up,Vector3.right};old.triangles=new[]{0,1,2};
            old.colors=new[]{Color.red,Color.red,Color.red};
            AssetDatabase.CreateAsset(old,path);string guid=AssetDatabase.AssetPathToGUID(path);
            try
            {
                var source=new Mesh{name="UpdatedMesh",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                source.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.one,Vector3.forward,Vector3.left};
                source.normals=Enumerable.Repeat(Vector3.up,6).ToArray();
                source.tangents=Enumerable.Repeat(new Vector4(1,0,0,1),6).ToArray();
                source.SetUVs(0,Enumerable.Range(0,6).Select(i=>new Vector2(i,.5f)).ToList());
                source.SetUVs(1,Enumerable.Range(0,6).Select(i=>new Vector4(i,1,2,3)).ToList());
                source.bindposes=new[]{Matrix4x4.identity};
                source.boneWeights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=1},6).ToArray();
                source.subMeshCount=2;source.SetIndices(new[]{0,1,2},MeshTopology.Triangles,0);source.SetIndices(new[]{0,1,2},MeshTopology.Triangles,1,true,3);
                var delta=Enumerable.Repeat(Vector3.up*.1f,6).ToArray();source.AddBlendShapeFrame("Test",100,delta,new Vector3[6],new Vector3[6]);source.RecalculateBounds();
                var expected=UnityEngine.Object.Instantiate(source);
                var saved=SMusicAssetWriter.Save(source,path);
                SMusicAssetWriter.AssertGeometry(expected,saved);
                Require(saved.colors.Length==0,"Old colors retained");
                AssetDatabase.SaveAssets();AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);SMusicAssetWriter.AssertGeometry(expected,saved);
                Require(guid==AssetDatabase.AssetPathToGUID(path),"GUID changed");
                // Repeat the same delivery, then replace a skinned mesh with a simpler non-skinned collider.
                saved=SMusicAssetWriter.Save(UnityEngine.Object.Instantiate(expected),path);SMusicAssetWriter.AssertGeometry(expected,saved);
                var simple=new Mesh{name="Collider"};simple.vertices=new[]{Vector3.zero,Vector3.up*2,Vector3.right*2};simple.triangles=new[]{0,1,2};
                var simpleExpected=UnityEngine.Object.Instantiate(simple);saved=SMusicAssetWriter.Save(simple,path);
                SMusicAssetWriter.AssertGeometry(simpleExpected,saved);Require(saved.blendShapeCount==0&&saved.bindposes.Length==0,"Old deform data retained");
                UnityEngine.Object.DestroyImmediate(expected);UnityEngine.Object.DestroyImmediate(simpleExpected);
                File.WriteAllText("Logs/music-mesh-copy-regression.log","PASS changed vertex count, indices/baseVertex, UV2/4, normals, tangents, colors removal, weights, bindposes, blend shape, reimport persistence, repeat import, skinned to collider, GUID stability\nRESULT COMPLETE\n");
            }
            finally{AssetDatabase.DeleteAsset(path);}
        }
        static void Check(string text,bool ok)
        {
            File.AppendAllText(LogPath,(ok?"PASS ":"FAIL ")+text+"\n");if(ok)pass++;else fail++;
        }
        static Vector3[] Bake(SkinnedMeshRenderer r,Transform space)
        {
            var m=new Mesh();r.BakeMesh(m);
            var matrix=space.worldToLocalMatrix*r.transform.localToWorldMatrix;
            var points=m.vertices.Select(matrix.MultiplyPoint3x4).ToArray();UnityEngine.Object.DestroyImmediate(m);return points;
        }
        // Checks actual Animator output, not only SampleAnimation on the generated hierarchy.
        public static float AnimatorDelta(SMusicEnemyImporter.AssetInfo asset,EnemyVisual visual,AnimationClip clip,float normalizedTime)
        {
            var raw=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(asset.model));raw.SetActive(false);
            try
            {
                clip.SampleAnimation(raw,clip.length*normalizedTime);
                var merged=visual.Animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name.EndsWith("_LOD0"));
                var actual=Bake(merged,visual.Animator.transform);int offset=0;float maximum=0;
                foreach(var part in raw.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.EndsWith("_LOD0")))
                {
                    var expected=Bake(part,raw.transform);
                    for(int i=0;i<expected.Length;i++)maximum=Mathf.Max(maximum,Vector3.Distance(expected[i],actual[offset+i]));
                    offset+=expected.Length;
                }
                return offset==actual.Length?maximum:float.PositiveInfinity;
            }
            finally{UnityEngine.Object.DestroyImmediate(raw);}
        }
        public static void CaptureFixedGuard(EnemyVisual visual)
        {
            var cameraObject=new GameObject("FixedGuardReview");var camera=cameraObject.AddComponent<Camera>();
            var original=visual.transform.position;visual.transform.position=new Vector3(800,0,0);
            try
            {
                visual.Animator.Play("Sweep_Windup",0,.75f);visual.Animator.Update(0);
                visual.GetComponent<LODGroup>().ForceLOD(0);
                camera.transform.position=visual.transform.position+new Vector3(5,3.5f,8);camera.transform.LookAt(visual.transform.position+Vector3.up*1.5f);
                camera.fieldOfView=36;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.24f,.27f,.33f);camera.farClipPlane=25;
                SFoundryMapBuilder.Capture(camera,"Shots/music-s08-fixed.png");
            }
            finally{visual.transform.position=original;visual.GetComponent<LODGroup>().ForceLOD(-1);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
        [MenuItem("S/Enemies/Validate All Music Skin Geometry")]
        public static void Validate()
        {
            pass=fail=poses=0;maximum=0;File.WriteAllText(LogPath,"START all 17 animated prefabs / all three LODs\n");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                foreach(var a in SMusicEnemyImporter.ReadPlan().assets)
                {
                    var raw=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(a.model));
                    var built=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SMusicEnemyImporter.Prefabs+"/"+a.id+".prefab"));
                    SceneManager.MoveGameObjectToScene(raw,scene);SceneManager.MoveGameObjectToScene(built,scene);raw.SetActive(false);built.SetActive(false);
                    try
                    {
                        var visual=built.GetComponent<EnemyVisual>();var target=visual.Animator.gameObject;
                        var original=raw.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        var combined=target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        var rawPose=raw.GetComponentsInChildren<Transform>(true).Select(t=>(t,t.localPosition,t.localRotation,t.localScale)).ToArray();
                        var builtPose=target.GetComponentsInChildren<Transform>(true).Select(t=>(t,t.localPosition,t.localRotation,t.localScale)).ToArray();
                        for(int level=0;level<3;level++)
                        {
                            var parts=original.Where(r=>r.name.EndsWith("_LOD"+level)).ToArray();
                            var merged=combined.Single(r=>r.name.EndsWith("_LOD"+level));var mesh=merged.sharedMesh;
                            var expectedIndices=new List<int>();int offset=0;
                            foreach(var r in parts){expectedIndices.AddRange(r.sharedMesh.triangles.Select(t=>t+offset));offset+=r.sharedMesh.vertexCount;}
                            bool topology=offset==mesh.vertexCount&&expectedIndices.SequenceEqual(mesh.triangles);
                            Check(a.id+"/LOD"+level+" source topology",topology);
                            if(!topology)continue;
                            var targetWeights=mesh.boneWeights;bool weightsMatch=true;int start=0;
                            foreach(var r in parts)
                            {
                                var sourceWeights=r.sharedMesh.boneWeights;
                                for(int i=0;i<sourceWeights.Length;i++)
                                {
                                    var sw=sourceWeights[i];var tw=targetWeights[start+i];
                                    int[] si={sw.boneIndex0,sw.boneIndex1,sw.boneIndex2,sw.boneIndex3},ti={tw.boneIndex0,tw.boneIndex1,tw.boneIndex2,tw.boneIndex3};
                                    float[] sv={sw.weight0,sw.weight1,sw.weight2,sw.weight3},tv={tw.weight0,tw.weight1,tw.weight2,tw.weight3};
                                    for(int j=0;j<4;j++)
                                    {
                                        if(Mathf.Abs(sv[j]-tv[j])>.0001f){weightsMatch=false;continue;}
                                        if(sv[j]>0&&AnimationUtility.CalculateTransformPath(r.bones[si[j]],raw.transform)!=AnimationUtility.CalculateTransformPath(merged.bones[ti[j]],target.transform))weightsMatch=false;
                                    }
                                }
                                start+=sourceWeights.Length;
                            }
                            Check(a.id+"/LOD"+level+" source bone weights",weightsMatch);
                            foreach(var clip in visual.Clips)
                            {
                                float worst=0;
                                foreach(float fraction in new[]{0f,.1f,.25f,.5f,.75f,.9f,1f})
                                {
                                    foreach(var p in rawPose){p.t.localPosition=p.localPosition;p.t.localRotation=p.localRotation;p.t.localScale=p.localScale;}
                                    foreach(var p in builtPose){p.t.localPosition=p.localPosition;p.t.localRotation=p.localRotation;p.t.localScale=p.localScale;}
                                    clip.SampleAnimation(raw,clip.length*fraction);clip.SampleAnimation(target,clip.length*fraction);
                                    var actual=Bake(merged,target.transform);int cursor=0;
                                    foreach(var r in parts)
                                    {
                                        var expected=Bake(r,raw.transform);
                                        for(int i=0;i<expected.Length;i++)
                                        {
                                            float distance=Vector3.Distance(expected[i],actual[cursor+i]);
                                            if(float.IsNaN(distance)||float.IsInfinity(distance)){worst=float.PositiveInfinity;break;}
                                            worst=Mathf.Max(worst,distance);
                                        }
                                        cursor+=expected.Length;
                                    }
                                    poses++;
                                }
                                maximum=Mathf.Max(maximum,worst);Check(a.id+"/LOD"+level+"/"+clip.name+" max vertex delta "+worst.ToString("F7")+" m",worst<.001f);
                            }
                        }
                    }
                    finally{UnityEngine.Object.DestroyImmediate(raw);UnityEngine.Object.DestroyImmediate(built);}
                }
                File.AppendAllText(LogPath,"RESULT "+pass+"/"+(pass+fail)+" passed; "+poses+" posed LOD samples; maxDelta="+maximum.ToString("F7")+" m\n");
                if(fail>0)throw new InvalidOperationException("Geometry validation failed "+fail);
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
