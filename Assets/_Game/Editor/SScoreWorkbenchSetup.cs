using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using S.View;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SScoreWorkbenchSetup
    {
        const string Request="Logs/score-workbench-setup.request";
        static SScoreWorkbenchSetup(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            File.Delete(Request);
            try{Build();File.WriteAllText("Logs/score-workbench-setup.log","PASS score layout, HUD, spawn cube and addresses ready; approved score art integrated; outlined controls and shared preview backplate integrated\n");}
            catch(Exception e){File.WriteAllText("Logs/score-workbench-setup.log","FAIL "+e);Debug.LogException(e);}
        }
        public static void AddDevice(GameObject mapRoot)
        {
            var map=mapRoot.GetComponent<FoundryArena>();
            if(mapRoot.transform.Find("ScoreWorkbench_DevCube"))return;
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name="ScoreWorkbench_DevCube";cube.transform.SetParent(mapRoot.transform,false);
            // Beside the actual spawn, avoiding overlap with the player's capsule.
            cube.transform.position=map.PlayerSpawn.position+new Vector3(1.5f,.5f,1.5f);
            cube.AddComponent<ScoreWorkbench>().DisplayName="配件装配装置";
        }
        [MenuItem("S/UI/Build Score Workbench Prototype")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // Apply the latest sprite and slice rules even if the PNG arrived before compilation.
            foreach(var name in new[]{"score_close","score_sort","score_preview_rail"})
                AssetDatabase.ImportAsset(UiAssets.DirOf(name)+name+".png",ImportAssetOptions.ForceUpdate);
            const string oldPath="Assets/_Game/Res/UIPrefabs/Inventory.prefab";
            const string newPath="Assets/_Game/Res/UIPrefabs/ScoreInventory.prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(oldPath)&&!AssetDatabase.LoadAssetAtPath<GameObject>(newPath))
            {
                var err=AssetDatabase.MoveAsset(oldPath,newPath);
                if(!string.IsNullOrEmpty(err))throw new Exception(err);
            }
            var ui=ScoreUiLayout.Build(AssetDatabase.LoadAssetAtPath<Font>(UiKit.FontAssetPath),AssetDatabase.LoadAssetAtPath<Sprite>(UiAssets.DirOf("score_page")+"score_page.png"));
            ScoreUiLayout.ApplyControlArt(ui.GetComponent<InvRefs>(),AssetDatabase.LoadAssetAtPath<Sprite>(UiAssets.DirOf("score_sort")+"score_sort.png"),AssetDatabase.LoadAssetAtPath<Sprite>(UiAssets.DirOf("score_close")+"score_close.png"));
            PrefabUtility.SaveAsPrefabAsset(ui,newPath);UnityEngine.Object.DestroyImmediate(ui);
            var hud=PrefabUtility.LoadPrefabContents(UiPrefab.AssetDir+"HUD.prefab");
            try
            {
                var v=hud.GetComponent<HudV2View>();v.ConfigureScorePreview(AssetDatabase.LoadAssetAtPath<Font>(UiKit.FontAssetPath),AssetDatabase.LoadAssetAtPath<Sprite>(UiAssets.DirOf("score_preview_rail")+"score_preview_rail.png"));
                v.ConfigureInstrumentFrames(AssetDatabase.LoadAssetAtPath<Sprite>(UiAssets.DirOf("hud_music_badge_mask")+"hud_music_badge_mask.png"),AssetDatabase.LoadAssetAtPath<Sprite>(UiAssets.DirOf("hud_portrait_frame")+"hud_portrait_frame.png"));
                v.ScoreIcon.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(UiAssets.DirOf("score_bag_icon")+"score_bag_icon.png");
                foreach(var border in v.ScorePreviewBorders)border.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(UiAssets.DirOf("score_slot_empty")+"score_slot_empty.png");
                PrefabUtility.SaveAsPrefabAsset(hud,UiPrefab.AssetDir+"HUD.prefab");
            }finally{PrefabUtility.UnloadPrefabContents(hud);}
            var map=PrefabUtility.LoadPrefabContents(FoundryArena.ActiveAssetPath);
            try{AddDevice(map);PrefabUtility.SaveAsPrefabAsset(map,FoundryArena.ActiveAssetPath);}
            finally{PrefabUtility.UnloadPrefabContents(map);}
            SAddressablesSetup.Build();AssetDatabase.SaveAssets();
        }
    }
}
