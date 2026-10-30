using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Importação automática das texturas de VFX pela pasta em que estão (Assets/KonoSuba/VFX/...):
    ///   Flipbooks/  dados em cinzento (intensidade em RGB, cobertura em A): linear, mipmaps, clamp, alta qualidade;
    ///   Motion/     motion vectors (RG): linear, sem mipmaps (não misturar células), sem compressão;
    ///   Gradients/  gradient maps: sRGB, clamp, sem mipmaps, sem compressão;
    ///   Noise/      ruídos e flowmaps tileáveis: linear, repeat, mipmaps.
    /// Assim qualquer textura nova (do Blender, do Photoshop) entra já com as definições certas.
    /// </summary>
    public class VFXTextureImporter : AssetPostprocessor
    {
        public const string Root = "Assets/KonoSuba/VFX/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 4096;

            if (assetPath.Contains("/Flipbooks/"))
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
            }
            else if (assetPath.Contains("/Motion/"))
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            }
            else if (assetPath.Contains("/Gradients/"))
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            }
            else if (assetPath.Contains("/Noise/"))
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
            }
        }
    }
}
