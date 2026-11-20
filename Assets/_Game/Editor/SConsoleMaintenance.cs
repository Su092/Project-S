using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SConsoleMaintenance
    {
        const string Request="Logs/console-maintenance.request";
        static SConsoleMaintenance(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            string action=File.ReadAllText(Request).Trim();File.Delete(Request);
            try
            {
                if(action=="audit")Audit();
                else if(action=="desktop")
                {
                    var windows=Resources.FindObjectsOfTypeAll<EditorWindow>();
                    var simulator=windows.Where(w=>w.GetType().FullName.Contains("DeviceSimulation")&&w.GetType().Name.Contains("Simulator")).ToArray();
                    File.WriteAllText("Logs/console-desktop-input.log","Closing mouse-capturing simulator windows: "+string.Join(", ",simulator.Select(w=>w.GetType().FullName)));
                    var game=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
                    foreach(var window in simulator)window.Close();
                    game.Show();game.Focus();
                }
                else if(action=="clear"){Audit();typeof(Editor).Assembly.GetType("UnityEditor.LogEntries").GetMethod("Clear",BindingFlags.Static|BindingFlags.Public).Invoke(null,null);}
            }
            catch(Exception e){File.WriteAllText("Logs/console-maintenance-error.log",e.ToString());}
        }
        public static void Audit()
        {
            var sb=new StringBuilder();var assembly=typeof(Editor).Assembly;var entries=assembly.GetType("UnityEditor.LogEntries");var entryType=assembly.GetType("UnityEditor.LogEntry");
            var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
            var countArgs=new object[]{0,0,0};entries.GetMethod("GetCountsByType",flags).Invoke(null,countArgs);
            sb.AppendLine("Errors="+countArgs[0]+" Warnings="+countArgs[1]+" Logs="+countArgs[2]);
            entries.GetMethod("StartGettingEntries",flags).Invoke(null,null);
            try
            {
                int count=(int)entries.GetMethod("GetCount",flags).Invoke(null,null);
                var get=entries.GetMethod("GetEntryInternal",flags);
                for(int i=0;i<count;i++)
                {
                    var entry=Activator.CreateInstance(entryType);get.Invoke(null,new object[]{i,entry});
                    var fields=entryType.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                    sb.AppendLine("--- "+i);
                    foreach(var field in fields.Where(f=>f.Name=="message"||f.Name=="mode"))sb.AppendLine(field.Name+": "+field.GetValue(entry));
                }
            }
            finally{entries.GetMethod("EndGettingEntries",flags).Invoke(null,null);}
            string path="Logs/console-audit-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt";File.WriteAllText(path,sb.ToString());
            var stage=PrefabStageUtility.GetCurrentPrefabStage();
            File.WriteAllText("Logs/console-audit-latest.txt","Archive: "+path+"\n"+sb.ToString()+"\nStage="+(stage?stage.assetPath:"main")+" dirty="+(stage&&stage.scene.isDirty)+"\nSelectable count="+Selectable.allSelectableCount);
        }
    }
}
