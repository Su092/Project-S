using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace S.EditorTools
{
    /// <summary>Imported files are authoring sources only; shipped prefabs use independent Unity assets.</summary>
    [InitializeOnLoad]
    public static class SMusicNativeAssets
    {
        static SMusicNativeAssets(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            const string request="Logs/music-native-assets.request";
            if(!File.Exists(request))return;
            File.Delete(request);
            try{ConvertAll();}
            catch(Exception e){File.AppendAllText("Logs/music-native-assets.log","ERROR "+e+"\n");Debug.LogException(e);}
        }
        static bool ModelSource(string path)
        {
            switch(Path.GetExtension(path).ToLowerInvariant())
            {case ".fbx":case ".blend":case ".obj":case ".dae":return true;default:return false;}
        }
        static string Safe(string name)=>string.Concat(name.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'?c:'_'));
        static UnityEngine.Object Extract(UnityEngine.Object source,string id,Dictionary<UnityEngine.Object,UnityEngine.Object> cache)
        {
            if(cache.TryGetValue(source,out var result))return result;
            if(!(source is Mesh||source is Avatar||source is AnimationClip||source is Material||source is Texture2D))
                throw new InvalidOperationException("Unsupported imported reference "+source.GetType().Name+": "+AssetDatabase.GetAssetPath(source)+"/"+source.name);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId);
            string dir=source is Mesh?SMusicAssetLayout.Model(id):source is Avatar||source is AnimationClip?SMusicAssetLayout.Animation(id):source is Material?SMusicAssetLayout.Material(id):SMusicAssetLayout.Texture(id);
            string path=dir+"/"+source.GetType().Name+"_"+Safe(source.name)+"_"+localId+".asset";
            result=AssetDatabase.LoadMainAssetAtPath(path);
            var clone=UnityEngine.Object.Instantiate(source);clone.name=source.name;clone.hideFlags=HideFlags.None;
            if(result)
            {
                if(result.GetType()!=clone.GetType())throw new InvalidOperationException("Native asset type mismatch "+path);
                SMusicAssetWriter.Copy(clone,result);UnityEngine.Object.DestroyImmediate(clone);EditorUtility.SetDirty(result);
            }
            else{AssetDatabase.CreateAsset(clone,path);result=clone;}
            cache[source]=result;
            // Embedded materials, if supplied by future deliveries, must not retain imported textures.
            Rewrite(result,id,cache);
            return result;
        }
        static void Rewrite(UnityEngine.Object obj,string id,Dictionary<UnityEngine.Object,UnityEngine.Object> cache)
        {
            using(var serialized=new SerializedObject(obj))
            {
                var property=serialized.GetIterator();bool changed=false;
                while(property.Next(true))
                {
                    if(property.propertyType!=SerializedPropertyType.ObjectReference)continue;
                    var value=property.objectReferenceValue;
                    if(!value||!ModelSource(AssetDatabase.GetAssetPath(value)))continue;
                    property.objectReferenceValue=Extract(value,id,cache);changed=true;
                }
                if(changed)serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        public static void Detach(GameObject root,string id)
        {
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                if(!t||!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))continue;
                PrefabUtility.UnpackPrefabInstance(t.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            }
            var cache=new Dictionary<UnityEngine.Object,UnityEngine.Object>();
            foreach(var component in root.GetComponentsInChildren<Component>(true))
            {
                if(!component)throw new InvalidOperationException("Missing script in "+id);
                Rewrite(component,id,cache);
            }
        }
        static string[] Ids()=>SMusicEnemyImporter.ReadPlan().assets.Select(a=>a.id)
            .Concat(SMusicEnemyImporter.ReadPlan().effects.Select(a=>a.id))
            .Concat(new[]{"FXM_S14_NoteGate","SM_MusicFollowSpot"}).ToArray();
        [MenuItem("S/Enemies/Convert Music Models To Native Assets")]
        public static void ConvertAll()
        {
            File.WriteAllText("Logs/music-native-assets.log","START\n");
            foreach(var id in Ids())
            {
                string path=SMusicEnemyImporter.Prefabs+"/"+id+".prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                try{Detach(root,id);PrefabUtility.SaveAsPrefabAsset(root,path);}
                finally{PrefabUtility.UnloadPrefabContents(root);}
                File.AppendAllText("Logs/music-native-assets.log","CONVERTED "+id+"\n");
            }
            AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Validate();SMusicEnemyImporter.Validate();
            File.AppendAllText("Logs/music-native-assets.log","RESULT COMPLETE\n");
        }
        public static void Validate()
        {
            var lines=new List<string>();int pass=0,fail=0;
            void Check(string name,bool ok){lines.Add((ok?"PASS ":"FAIL ")+name);if(ok)pass++;else fail++;}
            foreach(var id in Ids())
            {
                string path=SMusicEnemyImporter.Prefabs+"/"+id+".prefab";var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var deps=AssetDatabase.GetDependencies(path,true);var imported=deps.Where(ModelSource).ToArray();
                Check(id+" recursive model dependencies zero",imported.Length==0);
                if(imported.Length>0)lines.Add(string.Join("\n",imported));
                Check(id+" project-local dependencies",deps.All(d=>d.StartsWith("Assets/")||d.StartsWith("Packages/")));
                Check(id+" standalone prefab",PrefabUtility.GetPrefabAssetType(prefab)==PrefabAssetType.Regular);
                Check(id+" mesh resources valid",prefab.GetComponentsInChildren<MeshFilter>(true).All(f=>f.sharedMesh&&AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith(".asset"))&&
                    prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(s=>s.sharedMesh&&s.sharedMesh.vertexCount>0&&s.bones.All(b=>b)&&AssetDatabase.GetAssetPath(s.sharedMesh).EndsWith(".asset")));
                Check(id+" avatars independent",prefab.GetComponentsInChildren<Animator>(true).All(a=>!a.avatar||(a.avatar.isValid&&AssetDatabase.GetAssetPath(a.avatar).EndsWith(".asset"))));
            }
            lines.Add("RESULT "+pass+"/"+(pass+fail)+" passed; "+Ids().Length+" prefabs");File.WriteAllLines("Logs/music-native-validation.log",lines);
            if(fail>0)throw new InvalidOperationException("Native resource checks failed: "+fail);
        }
    }
}
