using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
namespace S.EditorTools
{
    /// <summary>
    /// Editor-only compatibility for uGUI 2.6's array-based Selectable registry.
    /// ResetStaticsOnLoad clears the registry while Prefab Mode controls can retain
    /// m_EnableCalled=true. Their next OnDisable then decrements an empty registry.
    /// Reconcile live registrations at lifecycle boundaries, without changing UI
    /// components or hiding exceptions. No per-frame or player-build work.
    /// Upstream fix: Unity-Technologies/uGUI, Selectable.ResetStaticsOnLoad.
    /// Once uGUI switches to its fixed List registry this bridge becomes a no-op.
    /// </summary>
    [InitializeOnLoad]
    public static class SSelectableRegistryCompatibility
    {
        const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly FieldInfo Items=typeof(Selectable).GetField("s_Selectables",Static);
        static readonly FieldInfo Count=typeof(Selectable).GetField("s_SelectableCount",Static);
        static readonly FieldInfo Index=typeof(Selectable).GetField("m_CurrentIndex",Instance);
        static readonly FieldInfo Enabled=typeof(Selectable).GetField("m_EnableCalled",Instance);
        static bool Supported=>Items?.FieldType==typeof(Selectable[])&&Count!=null&&Index!=null&&Enabled!=null;
        static SSelectableRegistryCompatibility()
        {
            EditorApplication.delayCall+=Repair;
            AssemblyReloadEvents.beforeAssemblyReload+=Repair;
            PrefabStage.prefabStageClosing+=_=>Repair();
            EditorApplication.playModeStateChanged+=_=>Repair();
        }
        // Runs after uGUI's AfterAssembliesLoaded reset, before gameplay UI starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeSceneLoad()=>Repair();
        public static void Repair()
        {
            if(!Supported)return;
            var live=Resources.FindObjectsOfTypeAll<Selectable>()
                .Where(s=>s&&!EditorUtility.IsPersistent(s)&&s.gameObject.scene.IsValid()).ToArray();
            var registered=live.Where(s=>(bool)Enabled.GetValue(s)&&s.isActiveAndEnabled).ToArray();
            var items=(Selectable[])Items.GetValue(null);int count=(int)Count.GetValue(null);
            bool valid=items!=null&&count>=0&&count<=items.Length&&count==registered.Length;
            if(valid)
            {
                for(int i=0;i<count;i++)
                    if(!items[i]||!(bool)Enabled.GetValue(items[i])||(int)Index.GetValue(items[i])!=i||!registered.Contains(items[i])){valid=false;break;}
            }
            if(valid)return;
            var rebuilt=new Selectable[Math.Max(10,Mathf.NextPowerOfTwo(Math.Max(1,registered.Length)))];
            for(int i=0;i<registered.Length;i++){rebuilt[i]=registered[i];Index.SetValue(registered[i],i);}
            foreach(var selectable in live.Except(registered)){Enabled.SetValue(selectable,false);Index.SetValue(selectable,-1);}
            Items.SetValue(null,rebuilt);Count.SetValue(null,registered.Length);
        }
        public static bool IsConsistent()
        {
            if(!Supported)return true;
            var items=(Selectable[])Items.GetValue(null);int count=(int)Count.GetValue(null);
            if(items==null||count<0||count>items.Length)return false;
            for(int i=0;i<count;i++)
                if(!items[i]||!(bool)Enabled.GetValue(items[i])||(int)Index.GetValue(items[i])!=i)return false;
            foreach(var s in Resources.FindObjectsOfTypeAll<Selectable>().Where(s=>s&&!EditorUtility.IsPersistent(s)&&s.gameObject.scene.IsValid()))
                if((bool)Enabled.GetValue(s)&&s.isActiveAndEnabled)
                {int index=(int)Index.GetValue(s);if(index<0||index>=count||items[index]!=s)return false;}
            return true;
        }
    }
}
