using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace S.EditorTools {
[InitializeOnLoad] public static class SWeaponImportSession {
 static SWeaponImportSession(){EditorApplication.update+=Tick;}
 static void Tick(){
 if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Logs/weapon-save-close.request"))return;
 if(EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.isPlaying=false;return;}
 if(!EditorSceneManager.SaveOpenScenes())return;
 AssetDatabase.SaveAssets();File.Delete("Logs/weapon-save-close.request");File.WriteAllText("Logs/weapon-save-close.done","Saved open scenes and assets before exclusive import.");EditorApplication.Exit(0);
 }
}}
