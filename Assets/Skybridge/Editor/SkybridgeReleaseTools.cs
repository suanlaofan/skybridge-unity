using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Skybridge.Editor
{
    public static class SkybridgeReleaseTools
    {
        private const string IconPath = "Assets/Skybridge/Art/Icons/SkybridgeIcon.png";
        private const string BackgroundPath = "Assets/Skybridge/Art/Icons/AndroidIconBackground.asset";

        [MenuItem("Skybridge/Release/Configure Game Icon")]
        public static void ConfigureApplicationIcons()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null) throw new BuildFailedException("The original Skybridge game icon is missing.");
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var background = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            if (background == null)
            {
                background = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                background.SetPixels(Enumerable.Repeat(new Color32(48, 57, 65, 255), 4).Select(color => (Color)color).ToArray());
                background.Apply();
                AssetDatabase.CreateAsset(background, BackgroundPath);
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var slot in slots)
                    slot.SetTextures(slot.minLayerCount > 1 ? new[] { background, icon } : new[] { icon });
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("SKYBRIDGE_RELEASE_ICON_PASS: default and all Android icon slots use the original Skybridge artwork.");
        }
    }
}
