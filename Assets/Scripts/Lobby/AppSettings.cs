using System;
using System.IO;
using UnityEngine;

// 앱 전체 설정 (맵과 무관). persistentDataPath/AppSettings.json 에 저장.
[Serializable]
public class AppSettings
{
    public string screenshotFolder = "";
    // 타르코프 로그 폴더 ({게임 폴더}\Logs). 비우면 자동으로 찾는다 (TarkovLogWatcher)
    public string gameLogFolder = "";

    // 타르코프 기본 스크린샷 폴더: 문서\Escape from Tarkov\Screenshots
    public static string DefaultScreenshotFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Escape from Tarkov", "Screenshots");

    static string FilePath => Path.Combine(Application.persistentDataPath, "AppSettings.json");

    static AppSettings current;

    public static AppSettings Current
    {
        get
        {
            if (current == null) current = Load();
            return current;
        }
    }

    // 입력값이 비어 있으면 기본 폴더
    public static string ScreenshotFolder
    {
        get => string.IsNullOrWhiteSpace(Current.screenshotFolder) ? DefaultScreenshotFolder : Current.screenshotFolder.Trim();
        set
        {
            Current.screenshotFolder = value?.Trim() ?? "";
            Current.Save();
        }
    }

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonUtility.FromJson<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AppSettings] 읽기 실패, 기본값 사용: {FilePath}\n{e.Message}");
        }
        return new AppSettings();
    }

    void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AppSettings] 저장 실패: {FilePath}\n{e.Message}");
        }
    }
}
