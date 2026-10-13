using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Сборка игры. Меню: Real Peace → Собрать для Mac / Windows.
/// Из командной строки: -executeMethod GameBuilder.BuildMacBatch (см. Tools/build_mac.sh).
/// Mac — универсальная сборка (Apple Silicon + Intel).
/// </summary>
public static class GameBuilder
{
    const string Scene = "Assets/Scenes/Battle.unity";

    [MenuItem("Real Peace/Собрать для Mac (.app)")]
    public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/Mac/Real Peace.app");

    [MenuItem("Real Peace/Собрать для Windows (.exe)")]
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/RealPeace.exe");

    public static void BuildMacBatch()
    {
        bool ok = Build(BuildTarget.StandaloneOSX, "Builds/Mac/Real Peace.app");
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static void BuildWindowsBatch()
    {
        bool ok = Build(BuildTarget.StandaloneWindows64, "Builds/Windows/RealPeace.exe");
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Build(BuildTarget target, string path)
    {
        ProjectSetup.SetupMenu(); // URP, образцы материалов в Resources (иначе шейдеры не попадут в сборку)
        PlayerSettings.productName = "Real Peace";
        PlayerSettings.companyName = "Real Peace";
        PlayerSettings.bundleVersion = "0.2";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.realpeace.game");
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true; // бой не замирает, если переключиться в другое окно
        if (target == BuildTarget.StandaloneOSX)
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = path,
            target = target,
            options = BuildOptions.None,
        });
        bool ok = report.summary.result == BuildResult.Succeeded;
        Debug.Log($"[Real Peace] Сборка {target}: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} МБ, {report.summary.totalTime.TotalSeconds:0} с → {path}");
        return ok;
    }
}
