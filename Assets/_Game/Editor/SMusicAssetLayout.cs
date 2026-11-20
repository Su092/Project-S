using System.IO;
using UnityEditor;
namespace S.EditorTools
{
    public static class SMusicAssetLayout
    {
        public const string EditorRoot="Assets/_Game/Editor/MusicImport";
        public const string LegacyRoot="Assets/_Game/Res/Characters/Music";
        public static string Folder(string kind,string id)
        {
            string p="Assets/_Game/Res/"+kind+"/Enemies/Music/"+id;EnsureFolder(p);return p;
        }
        public static string Model(string id)=>Folder("Model",id);
        public static string Animation(string id)=>Folder("Animation",id);
        public static string Material(string id)=>Folder("Material",id);
        public static string Texture(string id)=>Folder("Texture",id);
        public static string Source(string id,string kind,string name)=>EditorRoot+"/Sources/"+id+"/"+kind+"/"+name;
        public static void EnsureFolder(string path)
        {
            path=path.Replace('\\','/');if(AssetDatabase.IsValidFolder(path))return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');
            if(!path.StartsWith("Assets/")||string.IsNullOrEmpty(parent))throw new System.ArgumentException(path);
            EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
    }
}
