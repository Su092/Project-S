using UnityEngine;
using UnityEditor;
namespace FoundryDelivery {
public class FoundryImportRules:AssetPostprocessor {
 public override int GetPostprocessOrder(){return 1000;}
 void OnPreprocessTexture(){
 if(!assetPath.Contains("/Environment/AbandonedFoundry/"))return;
 var t=(TextureImporter)assetImporter;bool normal=assetPath.EndsWith("_Normal.png"),mask=assetPath.EndsWith("_Mask.png");
 t.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;t.sRGBTexture=!normal&&!mask;
 t.alphaSource=TextureImporterAlphaSource.FromInput;t.wrapMode=TextureWrapMode.Repeat;t.maxTextureSize=1024;t.textureCompression=TextureImporterCompression.Uncompressed;t.anisoLevel=4;
 }
}
}
