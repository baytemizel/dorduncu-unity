// Editor conveniences: the game's art is imported as sprites (no mipmaps, no power-of-two rescaling), a Main scene is created
// and added to the build on first open, and the menu offers to point the game at a local / remote API.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dorduncu.EditorTools
{
    public sealed class ArtImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains("Resources/Dorduncu/Art") && !assetPath.Contains("Dorduncu/Branding")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = assetPath.Contains("Branding") ? TextureImporterType.Default : TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaIsTransparency = true;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }

    [InitializeOnLoad]
    public static class ProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += EnsureScene;
        }

        [MenuItem("Dördüncü/Ana sahneyi oluştur")]
        public static void EnsureScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ScenePath)) { AddToBuild(); return; }
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera));
            cam.tag = "MainCamera";
            var c = cam.GetComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.14f, 0.08f, 0.04f);
            c.orthographic = true;
            new GameObject("Dördüncü (GameApp kendini kurar)");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuild();
            Debug.Log("[Dördüncü] Assets/Scenes/Main.unity oluşturuldu. Play ile oyna; ayarlar: Dördüncü menüsü.");
        }

        private static void AddToBuild()
        {
            if (!File.Exists(ScenePath)) return;
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes) if (s.path == ScenePath) return;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes) { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = list.ToArray();
        }

        [MenuItem("Dördüncü/API: yerel (http://127.0.0.1:8000)")]
        private static void LocalApi() => SetApi("http://127.0.0.1:8000");

        [MenuItem("Dördüncü/API: kapalı (yalnızca botlar)")]
        private static void NoApi() => SetApi("");

        private static void SetApi(string url)
        {
            PlayerPrefs.SetString("dorduncu.apiUrl", url);
            PlayerPrefs.Save();
            Debug.Log("[Dördüncü] API adresi: " + (url == "" ? "(config.json)" : url) + " — kalıcı ayar için Assets/Resources/Dorduncu/config.json");
        }

        [MenuItem("Dördüncü/Oturumu ve kayıtları sıfırla")]
        private static void Reset()
        {
            foreach (var k in new[] { "dorduncu.accessToken", "dorduncu.refreshToken", "dorduncu.deviceId", "dorduncu.name", "dorduncu.theme", "dorduncu.level" })
                PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }
    }
}
