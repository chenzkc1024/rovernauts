using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rovernauts.EditorTools
{
    /// <summary>Menu items (and batch-mode entry points) to regenerate the main scene and build the game.</summary>
    public static class RovernautsSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string WindowsBuildPath = "Builds/Windows/Rovernauts.exe";

        [MenuItem("Rovernauts/Rebuild Main Scene")]
        public static void BuildScene()
        {
            EnsureBaseMaterial();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Rovernauts Game").AddComponent<GameManager>();
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "CISC 486 Group 7";
            PlayerSettings.productName = "Rovernauts";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[Rovernauts] Main scene rebuilt at " + ScenePath);
        }

        [MenuItem("Rovernauts/Build Windows Player")]
        public static void BuildWindows()
        {
            if (!System.IO.File.Exists(ScenePath)) BuildScene();
            EnsureBaseMaterial();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = WindowsBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[Rovernauts] Build {report.summary.result}: {report.summary.outputPath} ({report.summary.totalErrors} errors)");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>The runtime clones this material for every colour; living in Resources keeps its shader in builds.</summary>
        static void EnsureBaseMaterial()
        {
            string path = $"Assets/Resources/{Visuals.BaseMaterialName}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            System.IO.Directory.CreateDirectory("Assets/Resources");
            var mat = new Material(Shader.Find("Standard")) { color = Color.white };
            mat.SetFloat("_Glossiness", 0.2f);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
        }
    }
}
