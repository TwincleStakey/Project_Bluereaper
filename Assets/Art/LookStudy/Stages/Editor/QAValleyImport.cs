// Import settings for the Megascans log under Resources/QAValley: the normal map as a normal map, the AO linear.
using UnityEditor;

public class QAValleyImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/QAValley/")) return;
        var ti = (TextureImporter)assetImporter;
        if (assetPath.EndsWith("_Normal.jpg")) ti.textureType = TextureImporterType.NormalMap;
        if (assetPath.EndsWith("_AO.jpg")) ti.sRGBTexture = false;
        ti.maxTextureSize = 2048;
    }
}
