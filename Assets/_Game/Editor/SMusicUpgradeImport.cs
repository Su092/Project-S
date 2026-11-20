using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using S.View;
namespace S.EditorTools
{
    public static class SMusicUpgradeImport
    {
        [MenuItem("S/UI/Import Music Upgrade")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            int count=0;
            foreach(var path in Directory.GetFiles(SRes.Root+"Texture/UI","offer_*.png",SearchOption.AllDirectories))
            {
                string p=path.Replace('\\','/');AssetDatabase.ImportAsset(p,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(p);
                if(!sprite)throw new Exception("Missing sprite "+p);
                if(UiAssets.DirOf(sprite.name)==null)throw new Exception("Uncatalogued "+p);
                count++;
            }
            string prefab=UiPrefab.AssetDir+"Offer.prefab";
            var root=PrefabUtility.LoadPrefabContents(prefab);
            try {
                if(!root.GetComponent<MusicOfferPresentation>())root.AddComponent<MusicOfferPresentation>();
                PrefabUtility.SaveAsPrefabAsset(root,prefab);
            } finally {PrefabUtility.UnloadPrefabContents(root);}
            SAddressablesSetup.Build();AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/music-upgrade-import.log","Imported and catalogued "+count+" sprites; Offer prefab updated; Addressables rebuilt.");
            Debug.Log("[MusicUpgrade] Import complete: "+count);
        }
    }
}
