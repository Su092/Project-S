using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using S.View;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SConsoleRegression
    {
        const string Request="Logs/console-regression.request";
        const string Report="Logs/console-regression.log";
        static double at;
        static int Phase{get=>SessionState.GetInt("ConsoleRegressionPhase",0);set=>SessionState.SetInt("ConsoleRegressionPhase",value);}
        static int Weapon{get=>SessionState.GetInt("ConsoleRegressionWeapon",0);set=>SessionState.SetInt("ConsoleRegressionWeapon",value);}
        static SConsoleRegression()
        {
            EditorApplication.update+=Tick;
            Application.logMessageReceived+=(message,stack,type)=>{
                if(File.Exists(Request)&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert||type==LogType.Warning))
                    File.AppendAllText(Report,"UNEXPECTED "+type+" "+message+"\n"+stack+"\n");
            };
        }
        static void Check(string name,bool pass){File.AppendAllText(Report,(pass?"PASS ":"FAIL ")+name+"\n");if(!pass)throw new Exception(name);}
        static void Registry()
        {
            SSelectableRegistryCompatibility.Repair();
            int previous=Selectable.allSelectableCount;
            var scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("Registry regression",typeof(RectTransform),typeof(Canvas));
            SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                for(int i=0;i<16;i++){var button=new GameObject("Button"+i,typeof(RectTransform),typeof(Button));button.transform.SetParent(go.transform);}
                Check("preview buttons register beyond initial array capacity",Selectable.allSelectableCount==previous+16&&SSelectableRegistryCompatibility.IsConsistent());
                // Reproduce uGUI's runtime reset while Prefab/preview objects survive.
                typeof(Selectable).GetMethod("ResetStaticsOnLoad",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                Check("uGUI reset reproduces stale live registrations",!SSelectableRegistryCompatibility.IsConsistent());
                SSelectableRegistryCompatibility.Repair();
                Check("compatibility restores all live indices and registrations",Selectable.allSelectableCount==previous+16&&SSelectableRegistryCompatibility.IsConsistent());
                var buttons=go.GetComponentsInChildren<Button>();
                for(int i=0;i<buttons.Length;i+=2)buttons[i].gameObject.SetActive(false);
                Check("out-of-order disable preserves registry",Selectable.allSelectableCount==previous+8&&SSelectableRegistryCompatibility.IsConsistent());
                foreach(var button in buttons)button.gameObject.SetActive(true);
                int clicks=0;buttons[0].onClick.AddListener(()=>clicks++);buttons[0].onClick.Invoke();
                Check("reenable and button callbacks remain functional",clicks==1&&Selectable.allSelectableCount==previous+16&&SSelectableRegistryCompatibility.IsConsistent());
            }
            finally{UnityEngine.Object.DestroyImmediate(go);EditorSceneManager.ClosePreviewScene(scene);}
            Check("closing preview returns registry to baseline",Selectable.allSelectableCount==previous&&SSelectableRegistryCompatibility.IsConsistent());
        }
        static void Prefabs()
        {
            foreach(string path in Directory.GetFiles(SMusicWeaponImporter.Prefabs,"SW*.prefab"))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\','/'));var groups=prefab.GetComponentsInChildren<LODGroup>(true);
                Check(prefab.name+" has exactly one root LOD owner",groups.Length==1&&groups[0].gameObject==prefab);
                var lods=groups[0].GetLODs();Check(prefab.name+" retains all three LOD meshes",lods.Length==3&&lods.All(x=>x.renderers.Length==1&&x.renderers[0]));
                Check(prefab.name+" each renderer has one owner",lods.SelectMany(x=>x.renderers).Distinct().Count()==3);
                var clone=UnityEngine.Object.Instantiate(prefab);
                try{Check(prefab.name+" instantiates with one LODGroup",clone.GetComponentsInChildren<LODGroup>(true).Length==1);}
                finally{UnityEngine.Object.DestroyImmediate(clone);}
            }
        }
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(Phase==0)
                {
                    if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
                    var stage=PrefabStageUtility.GetCurrentPrefabStage();
                    if(stage&&stage.scene.isDirty)throw new Exception("Current prefab has unsaved edits; automatic stage test stopped without discarding.");
                    SessionState.SetString("ConsoleRegressionPreviousStage",stage?stage.assetPath:"");
                    File.WriteAllText(Report,"Console regression started\n");Registry();Prefabs();
                    PrefabStageUtility.OpenPrefab("Assets/_Game/Res/UIPrefabs/ScoreInventory.prefab");
                    Check("score prefab opens with consistent UI registry",SSelectableRegistryCompatibility.IsConsistent());
                    Phase=1;Weapon=0;EditorApplication.isPlaying=true;return;
                }
                if(Phase==1)
                {
                    var root=GameRoot.Instance;if(!EditorApplication.isPlaying||!root||root.Run==null||!root.Menu)return;
                    if(root.Menu.IsOpen){root.Menu.StartForTest();root.InvulnerableForTest=true;root.AutoAcceptOffers=true;}
                    if(EditorApplication.timeSinceStartup-at<.15)return;
                    Check("play mode with score prefab open keeps registry valid",SSelectableRegistryCompatibility.IsConsistent());
                    var guns=SMusicWeaponImporter.ReadPlan().assets;
                    if(Weapon<guns.Length){root.Weapons.StartWith(guns[Weapon].gunId);Weapon++;at=EditorApplication.timeSinceStartup;return;}
                    Phase=2;EditorApplication.isPlaying=false;return;
                }
                if(Phase==2)
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                    Check("exit play preserves edit-mode UI registry",SSelectableRegistryCompatibility.IsConsistent());
                    StageUtility.GoToMainStage();
                    Check("closing score prefab after play does not corrupt registry",SSelectableRegistryCompatibility.IsConsistent());
                    foreach(var name in new[]{"Menu","HUD","ScoreInventory"})
                    {
                        PrefabStageUtility.OpenPrefab("Assets/_Game/Res/UIPrefabs/"+name+".prefab");
                        Check("open "+name,SSelectableRegistryCompatibility.IsConsistent());
                        StageUtility.GoToMainStage();Check("close "+name,SSelectableRegistryCompatibility.IsConsistent());
                    }
                    string previous=SessionState.GetString("ConsoleRegressionPreviousStage","");
                    if(!string.IsNullOrEmpty(previous))PrefabStageUtility.OpenPrefab(previous);
                    bool clean=!File.ReadAllText(Report).Contains("UNEXPECTED ");
                    Check("no errors or warnings during registry, prefab and play tests",clean);
                    File.AppendAllText(Report,"RESULT PASS\n");Finish();SConsoleMaintenance.Audit();
                }
            }
            catch(Exception e){File.AppendAllText(Report,"FAIL "+e+"\n");Finish();}
        }
        static void Finish(){File.Delete(Request);Phase=0;Weapon=0;at=0;}
    }
}
