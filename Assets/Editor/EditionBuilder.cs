using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

// 일반 버전과 파티(네트워크) 버전을 같은 씬으로, 각자의 폴더에 빌드한다.
// 두 버전의 차이는 TKJ_PARTY 정의 기호뿐이다 (PartyEdition이 이 기호가 있을 때만 파티 기능을 붙인다).
//   일반 : 정의 기호 없음  → D:\Unity\build_Kilakjaby
//   파티 : TKJ_PARTY      → D:\Unity\build_Killajaby_forParty
// 씬은 Build Settings에 켜진 씬을 그대로 쓴다. Player Settings의 정의 기호는 건드리지 않는다 (빌드할 때만 추가).
// [둘 다 + 단일 exe]는 빌드가 끝나면 Tools/SingleExe/Build-SingleExe.cmd를 새 창으로 실행한다.
public static class EditionBuilder
{
    const string AppName = "TarkovKilaJaby";
    const string NormalFolder = "build_Kilakjaby";
    const string PartyFolder = "build_Killajaby_forParty";
    const string PartyDefine = "TKJ_PARTY";
    const string PartyInEditorMenu = "Tools/Tarkov/Party/에디터에서 파티 버전으로 실행";

    // 빌드 폴더는 프로젝트 폴더 옆 (D:\Unity\Tarkov_Killajaby → D:\Unity)
    static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    [MenuItem("Tools/Tarkov/Build/일반 버전")]
    static void BuildNormal() => Run(normal: true, party: false, singleExe: false);

    [MenuItem("Tools/Tarkov/Build/파티 버전")]
    static void BuildParty() => Run(normal: false, party: true, singleExe: false);

    [MenuItem("Tools/Tarkov/Build/둘 다")]
    static void BuildBoth() => Run(normal: true, party: true, singleExe: false);

    [MenuItem("Tools/Tarkov/Build/둘 다 + 단일 exe")]
    static void BuildBothAndPack() => Run(normal: true, party: true, singleExe: true);

    // 에디터에서 Play할 때 파티 버전으로 (에디터 설정에만 저장, 빌드에는 영향 없음)
    [MenuItem(PartyInEditorMenu)]
    static void TogglePartyInEditor()
    {
        bool on = !EditorPrefs.GetBool(PartyEdition.EditorPrefKey, false);
        EditorPrefs.SetBool(PartyEdition.EditorPrefKey, on);
        Debug.Log($"[EditionBuilder] 에디터 Play: {(on ? "파티 버전" : "일반 버전")}");
    }

    [MenuItem(PartyInEditorMenu, true)]
    static bool TogglePartyInEditorValidate()
    {
        Menu.SetChecked(PartyInEditorMenu, EditorPrefs.GetBool(PartyEdition.EditorPrefKey, false));
        return !EditorApplication.isPlaying;   // Play 중에 바꾸면 반만 적용되므로 막는다
    }

    static void Run(bool normal, bool party, bool singleExe)
    {
        // 저장하지 않은 씬/에셋이 있으면 빌드에 빠지므로 먼저 묻는다
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AssetDatabase.SaveAssets();

        string[] scenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes);
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("빌드할 씬이 없어요", "Build Settings에 켜진 씬이 없어요.", "확인");
            return;
        }

        var results = new List<string>();
        bool ok = true;
        if (normal) ok &= Build("일반", scenes, NormalFolder, null, results);
        if (party && ok) ok &= Build("파티", scenes, PartyFolder, new[] { PartyDefine }, results);

        string summary = string.Join("\n", results);
        if (ok && singleExe)
        {
            string cmd = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "SingleExe", "Build-SingleExe.cmd"));
            if (File.Exists(cmd))
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{cmd}\"\"") { UseShellExecute = true });
                summary += "\n\n단일 exe 만들기를 새 창에서 시작했어요.";
            }
            else summary += "\n\n단일 exe 도구를 찾지 못했어요: " + cmd;
        }
        EditorUtility.DisplayDialog(ok ? "빌드 완료" : "빌드 실패", summary, "확인");
    }

    static bool Build(string label, string[] scenes, string folder, string[] defines, List<string> results)
    {
        string exe = Path.Combine(OutputRoot, folder, AppName + ".exe");
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
            extraScriptingDefines = defines,
        };
        Debug.Log($"[EditionBuilder] {label} 버전 빌드 시작 → {exe} (정의 기호: {(defines == null ? "없음" : string.Join(", ", defines))})");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary s = report.summary;
        bool ok = s.result == BuildResult.Succeeded;
        string line = ok
            ? $"✔ {label} 버전: {folder} ({s.totalSize / (1024f * 1024f):0}MB, {s.totalTime.TotalSeconds:0}초)"
            : $"✘ {label} 버전: {s.result} (오류 {s.totalErrors}개, Console 확인)";
        results.Add(line);
        Debug.Log("[EditionBuilder] " + line);
        return ok;
    }
}
