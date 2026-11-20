using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
namespace S.EditorTools
{
    /// <summary>Explicit, reviewed delivery files only. Never mirror a delivery directory into Assets.</summary>
    [InitializeOnLoad]
    public static class SMusicDeliveryImport
    {
        [Serializable] public class Entry { public string source,target,sha256,guid; }
        [Serializable] public class Contract { public string source,id; public float height; }
        [Serializable] public class Manifest { public int schemaVersion;public Entry[] files;public Contract[] contracts; }
        [Serializable] class SourceContract { public string assetId,units;public float heightMeters; }
        [Serializable] class Migration { public Entry[] moves;public string[] archive; }
        [Serializable] class Receipt { public string delivery,status;public int selected,changed,ignored;public Entry[] files; }
        const string Config=SMusicAssetLayout.EditorRoot+"/delivery-files.json";
        static SMusicDeliveryImport(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            foreach(string action in new[]{"migrate","check","import"})
            {
                string request="Logs/music-delivery-"+action+".request";if(!File.Exists(request))continue;
                string delivery=File.ReadAllText(request).Trim();File.Delete(request);
                string log="Logs/music-delivery-"+action+".log";File.WriteAllText(log,"START\n");
                try
                {
                    if(action=="migrate")Migrate();
                    else if(action=="check"){PreflightTests(delivery);ValidateLayout();}
                    else Import(delivery);
                    File.AppendAllText(log,"RESULT COMPLETE\n");
                }
                catch(Exception e){File.AppendAllText(log,"ERROR "+e+"\n");Debug.LogException(e);}
            }
        }
        public static Manifest Read()=>JsonUtility.FromJson<Manifest>(File.ReadAllText(Config));
        static string Hash(string path){using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        static string Inside(string root,string relative)
        {
            if(string.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.Replace('\\','/').Split('/').Any(x=>x==".."||x=="."))throw new InvalidDataException("Unsafe relative path: "+relative);
            string full=Path.GetFullPath(Path.Combine(root,relative)),prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Outside root: "+relative);
            for(var p=new FileInfo(full).Directory;p!=null&&p.FullName.StartsWith(prefix,StringComparison.OrdinalIgnoreCase);p=p.Parent)
                if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked source directory: "+relative);
            if(File.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked source file: "+relative);
            return full;
        }
        static HashSet<string> ExpectedTargets()
        {
            var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var plan=SMusicEnemyImporter.ReadPlan();
            foreach(var a in plan.assets){result.Add(a.model);foreach(var c in a.clips)result.Add(c.file);foreach(string suffix in new[]{"_BC.png","_ORM.png","_E.png"})result.Add(a.textureRoot+suffix);}
            foreach(var fx in plan.effects){result.Add(fx.model);result.Add(fx.texture);}
            foreach(string id in new[]{"FXM_S14_NoteGate","SM_MusicFollowSpot"})
            {
                result.Add(SMusicAssetLayout.Source(id,"Models",id+".fbx"));
                foreach(string suffix in new[]{"BC","ORM","E"})result.Add("Assets/_Game/Res/Texture/Enemies/Music/"+id+"/T_"+id+"_"+suffix+".png");
            }
            result.Add("Assets/_Game/Res/Texture/Enemies/Music/FXM_S14_NoteGate/T_FXM_S14_NoteGate_Curtain.png");
            foreach(string state in new[]{"Idle","FocusIn","FocusOut","RigTest"})result.Add(SMusicAssetLayout.Source("SM_MusicFollowSpot","Animations","SM_MusicFollowSpot_"+state+".fbx"));
            return result;
        }
        public static void Preflight(string root,Manifest manifest)
        {
            if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
            if(manifest.schemaVersion!=1||manifest.files==null||manifest.contracts==null||manifest.contracts.Length!=13)throw new InvalidDataException("Invalid delivery schema/contracts");
            var sources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var file in manifest.files)
            {
                string source=Inside(root,file.source);Inside(Directory.GetCurrentDirectory(),file.target);
                if(!sources.Add(file.source)||!targets.Add(file.target))throw new InvalidDataException("Duplicate source or destination: "+file.source);
                string ext=Path.GetExtension(source).ToLowerInvariant();
                if(ext!=".fbx"&&ext!=".png")throw new InvalidDataException("Only selected FBX/PNG accepted: "+source);
                if(Path.GetExtension(file.target).ToLowerInvariant()!=ext)throw new InvalidDataException("Type changed: "+file.target);
                if(!File.Exists(source)||new FileInfo(source).Length==0)throw new FileNotFoundException("Missing or empty required input",source);
            }
            if(!targets.SetEquals(ExpectedTargets()))throw new InvalidDataException("Delivery file list differs from importer input contract.");
            var ids=new HashSet<string>();
            foreach(var contract in manifest.contracts)
            {
                if(!ids.Add(contract.id))throw new InvalidDataException("Duplicate contract "+contract.id);
                var metadata=JsonUtility.FromJson<SourceContract>(File.ReadAllText(Inside(root,contract.source)));
                if(metadata.assetId!=contract.id||metadata.units!="m"||(contract.height>0&&Mathf.Abs(metadata.heightMeters-contract.height)>.005f))
                    throw new InvalidDataException("Delivery ID/units/height mismatch: "+contract.source);
            }
        }
        static Receipt Stage(string root,Manifest manifest)
        {
            Preflight(root,manifest); // Everything is checked before any target is changed.
            string backup="_Inbox/chr/S_Music_Enemies/ImportBackups/"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var receipt=new Receipt{delivery=Path.GetFullPath(root),status="staging",selected=manifest.files.Length,files=new Entry[manifest.files.Length]};
            receipt.ignored=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length-manifest.files.Length;
            var pending=new List<Entry>();
            for(int i=0;i<manifest.files.Length;i++)
            {
                var e=manifest.files[i];string hash=Hash(Inside(root,e.source));
                receipt.files[i]=new Entry{source=e.source,target=e.target,sha256=hash,guid=AssetDatabase.AssetPathToGUID(e.target)};
                if(File.Exists(e.target)&&Hash(e.target)==hash)continue;
                if(File.Exists(e.target))
                {
                    string copy=backup+"/"+e.target;Directory.CreateDirectory(Path.GetDirectoryName(copy));File.Copy(e.target,copy);
                    if(File.Exists(e.target+".meta"))File.Copy(e.target+".meta",copy+".meta");
                }
                pending.Add(e);
            }
            File.WriteAllText("Logs/music-delivery-receipt.json",JsonUtility.ToJson(receipt,true));
            foreach(var e in pending)SMusicAssetLayout.EnsureFolder(Path.GetDirectoryName(e.target));
            AssetDatabase.StartAssetEditing();
            try{foreach(var e in pending)File.Copy(Inside(root,e.source),e.target,true);}
            finally{AssetDatabase.StopAssetEditing();}
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            receipt.changed=pending.Count;receipt.status="staged";
            foreach(var e in receipt.files)
            {
                if(Hash(e.target)!=e.sha256)throw new IOException("Copy hash mismatch: "+e.target);
                var guid=AssetDatabase.AssetPathToGUID(e.target);
                if(!string.IsNullOrEmpty(e.guid)&&e.guid!=guid)throw new IOException("Input GUID changed: "+e.target);
                e.guid=guid;
            }
            File.WriteAllText("Logs/music-delivery-receipt.json",JsonUtility.ToJson(receipt,true));return receipt;
        }
        [MenuItem("S/Enemies/Import Music Delivery (Manifest)")]
        public static void Choose()
        {
            string root=EditorUtility.OpenFolderPanel("Select delivery root (includes SkillSupplements)","_Inbox/chr/S_Music_Enemies","");
            if(!string.IsNullOrEmpty(root))Import(root);
        }
        public static void Import(string root)
        {
            if(Directory.Exists(SMusicAssetLayout.LegacyRoot))throw new InvalidOperationException("Run music delivery migration first.");
            var before=SnapshotGuids();var receipt=Stage(root,Read());
            SMusicEnemyImporter.Build();SMusicSupplementImporter.Build();SMusicNativeAssets.Validate();
            SMusicHitboxCheck.Validate();ValidateLayout();AssertGuids(before);
            receipt.status="complete";File.WriteAllText("Logs/music-delivery-receipt.json",JsonUtility.ToJson(receipt,true));
        }
        static Dictionary<string,string> SnapshotGuids()=>AssetDatabase.GetAllAssetPaths()
            .Where(p=>p.StartsWith("Assets/_Game/Res/")&&!AssetDatabase.IsValidFolder(p))
            .ToDictionary(p=>p,p=>AssetDatabase.AssetPathToGUID(p));
        static void AssertGuids(Dictionary<string,string> before)
        {
            foreach(var kv in before)if(AssetDatabase.AssetPathToGUID(kv.Key)!=kv.Value)throw new InvalidOperationException("Runtime GUID changed: "+kv.Key);
        }
        public static void Migrate()
        {
            var recipe=JsonUtility.FromJson<Migration>(File.ReadAllText(SMusicAssetLayout.EditorRoot+"/migration.json"));
            var guids=new Dictionary<string,string>();var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var e in recipe.moves)
            {
                if(!targets.Add(e.target))throw new InvalidDataException("Duplicate migration target "+e.target);
                bool old=File.Exists(e.source),next=File.Exists(e.target);
                if(old==next)throw new InvalidOperationException("Expected exactly one migration path: "+e.source);
                guids[e.target]=AssetDatabase.AssetPathToGUID(old?e.source:e.target);
                SMusicAssetLayout.EnsureFolder(Path.GetDirectoryName(e.target));
            }
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(var e in recipe.moves)
                {
                    if(!File.Exists(e.source))continue;
                    string error=AssetDatabase.MoveAsset(e.source,e.target);if(!string.IsNullOrEmpty(error))throw new IOException(error);
                }
            }
            finally{AssetDatabase.StopAssetEditing();}
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);AssertGuids(guids);
            // Verify no asset outside the old tree still depends on the files we are archiving.
            var roots=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/")&&!p.StartsWith(SMusicAssetLayout.LegacyRoot+"/")&&!AssetDatabase.IsValidFolder(p)).ToArray();
            var deps=new HashSet<string>(AssetDatabase.GetDependencies(roots,true));
            foreach(string p in recipe.archive)if(deps.Contains(p))throw new InvalidOperationException("Still referenced; cannot archive "+p);
            string archive="_Inbox/chr/S_Music_Enemies/LegacyUnityArchive";
            foreach(string p in recipe.archive)
            {
                if(!File.Exists(p))continue;
                string target=archive+"/"+p.Substring(SMusicAssetLayout.LegacyRoot.Length+1);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if(File.Exists(target))throw new IOException("Archive already exists "+target);
                File.Move(p,target);if(File.Exists(p+".meta"))File.Move(p+".meta",target+".meta");
            }
            if(Directory.Exists(SMusicAssetLayout.LegacyRoot))
            {
                if(Directory.GetFiles(SMusicAssetLayout.LegacyRoot,"*",SearchOption.AllDirectories).Any(p=>!p.EndsWith(".meta")))throw new IOException("Unclassified legacy files remain");
                if(!AssetDatabase.DeleteAsset(SMusicAssetLayout.LegacyRoot))throw new IOException("Cannot remove empty legacy folders");
            }
            AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssertGuids(guids);SMusicNativeAssets.Validate();SMusicEnemyImporter.Validate();ValidateLayout();
            File.WriteAllLines("Logs/music-delivery-migration-guids.log",guids.Select(k=>k.Key+" "+k.Value));
            File.AppendAllText("Logs/music-delivery-migrate.log","MOVED "+recipe.moves.Length+" GUIDs preserved; ARCHIVED "+recipe.archive.Length+" files\n");
        }
        public static void ValidateLayout()
        {
            var expected=ExpectedTargets();foreach(string p in expected)if(!File.Exists(p))throw new FileNotFoundException("Missing imported input",p);
            foreach(string p in Directory.GetFiles(SMusicAssetLayout.EditorRoot+"/Sources","*",SearchOption.AllDirectories))
            {var normalized=p.Replace('\\','/');if(!p.EndsWith(".meta")&&!expected.Contains(normalized))throw new InvalidOperationException("Non-whitelisted source "+p);}
            if(Directory.Exists(SMusicAssetLayout.LegacyRoot))throw new InvalidOperationException("Legacy mixed folder remains");
            var roots=SMusicEnemyImporter.ReadPlan().assets.Select(a=>a.id).Concat(SMusicEnemyImporter.ReadPlan().effects.Select(a=>a.id)).Concat(new[]{"FXM_S14_NoteGate","SM_MusicFollowSpot"});
            foreach(var id in roots)
            {
                var deps=AssetDatabase.GetDependencies(SMusicEnemyImporter.Prefabs+"/"+id+".prefab",true);
                var invalid=deps.Where(p=>(p.StartsWith("Assets/")&&p.Contains("/Editor/"))||p.StartsWith(SMusicAssetLayout.LegacyRoot+"/")).ToArray();
                if(invalid.Length>0)throw new InvalidOperationException("Runtime depends on import source "+id+": "+string.Join(", ",invalid));
            }
            File.WriteAllText("Logs/music-delivery-layout.log","PASS selected input set; source whitelist; categorized paths; 29 prefabs without Editor/source dependencies\n");
        }
        static void PreflightTests(string root)
        {
            var results=new List<string>();Preflight(root,Read());results.Add("PASS complete delivery");
            void Reject(string name,Action<Manifest> mutate)
            {
                var m=Read();mutate(m);bool rejected=false;
                try{Preflight(root,m);}catch(InvalidDataException){rejected=true;}catch(FileNotFoundException){rejected=true;}
                if(!rejected)throw new Exception("Unexpected preflight acceptance: "+name);results.Add("PASS reject "+name);
            }
            Reject("missing required file",m=>m.files[0].source="missing.fbx");
            Reject("path traversal",m=>m.files[0].source="../escape.fbx");
            Reject("unreviewed blend",m=>m.files[0].source="Source/unreviewed.blend");
            Reject("duplicate destination",m=>m.files[1].target=m.files[0].target);
            Reject("outside destination",m=>m.files[0].target="../Assets/outside.fbx");
            Reject("missing whitelist entry",m=>m.files=m.files.Skip(1).ToArray());
            Reject("mismatched ID",m=>m.contracts[0].id="Unknown");
            Reject("changed height",m=>m.contracts[0].height+=1);
            results.Add("RESULT "+results.Count+"/"+results.Count+" passed; no writes during preflight");
            File.WriteAllLines("Logs/music-delivery-preflight.log",results);
        }
    }
}
