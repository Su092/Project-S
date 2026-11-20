using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SMusicCombatAssets
    {
        static SMusicCombatAssets(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            const string request="Logs/music-combat-assets.request";
            if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            File.Delete(request);
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string folder="Assets/_Game/Res/Texture/VFX/Music";
                foreach(string path in Directory.GetFiles(folder,"*.png"))
                {
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));
                    importer.textureType=TextureImporterType.Default;importer.alphaIsTransparency=true;importer.sRGBTexture=true;
                    importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=true;importer.maxTextureSize=2048;
                    importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
                }
                string p="Assets/_Game/Res/Shaders/MAT_MusicSpotBeam.mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(p);
                if(!mat){mat=new Material(Shader.Find("S/MusicVfx"));AssetDatabase.CreateAsset(mat,p);}
                mat.shader=Shader.Find("S/MusicVfx");mat.SetColor("_BaseColor",Color.white);
                mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/T_MusicSpotBeam.png"));EditorUtility.SetDirty(mat);
                SAddressablesSetup.Build();AssetDatabase.SaveAssets();
                File.WriteAllText("Logs/music-combat-assets.log","PASS 4 VFX textures imported; beam material; Addressables refreshed");
            }
            catch(Exception ex){File.WriteAllText("Logs/music-combat-assets.log",ex.ToString());Debug.LogException(ex);}
        }
    }
}
