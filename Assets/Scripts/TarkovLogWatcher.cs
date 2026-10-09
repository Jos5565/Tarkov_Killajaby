using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// 타르코프가 남기는 로그 파일을 따라 읽어 "레이드 시작(맵)"과 "퀘스트 완료"를 알려준다.
// 앱 시작 시 자동으로 만들어지고 씬이 바뀌어도 유지된다.
//
// 지키는 규칙
//   - 로그 파일을 읽기만 한다. 게임 프로세스, 메모리, 네트워크에는 손대지 않고 게임 입력도 보내지 않는다.
//   - 로그에서 맵 이름과 퀘스트 ID만 꺼내 쓴다. 계정 정보(프로필 ID, IP 등)는 읽은 줄째로 버리고 저장·전송하지 않는다.
//   - 로그 폴더를 못 찾거나 형식이 바뀌어 읽지 못하면 기능만 조용히 꺼진다(앱의 다른 기능에는 영향 없음).
//
// 로그 위치: {게임 폴더}\Logs\log_{날짜}_{버전}\{날짜}_{버전} application_000.log / backend_000.log
//   application: "scene preset path:maps/shoreline_preset.bundle rcid:shoreline.scenespreset.asset" (맵 로딩 시작)
//                "TRACE-NetworkGameCreate ... Location: bigmap" (레이드 접속. 1.2.0부터 PvE도)
//                "[Transit] ... Locations:Shoreline ->" (1.1.x PvE 레이드 시작. 1.2.0부터는 맵 이름이 없음)
//   backend    : 레이드 끝 = "/client/match/local/end" (1.1.x PvE), "/client/game/profile/select" (메뉴로 돌아와 프로필을 다시 읽음)
//   모든 줄은 "2026-10-10 02:59:59.521|..." 시각으로 시작한다. 파일마다 따로 읽으므로 순서는 이 시각으로 판단한다
//   퀘스트 완료 : "templateId": "<퀘스트 id> successMessageText" (type 12 알림)
//                예전 버전은 backend 로그에 한 줄로, 1.2.0부터는 push-notifications 로그에 여러 줄 JSON으로 남는다.
//                successMessageText는 퀘스트 완료 알림에만 쓰이므로 이 줄만 보고 판단한다 (두 로그 모두 읽고 중복 제거)
public class TarkovLogWatcher : MonoBehaviour
{
    public static TarkovLogWatcher Instance { get; private set; }

    // 레이드가 시작된 맵 (tarkov.dev normalizedName, 예: "customs"). 앱이 켜진 뒤에 시작된 레이드만 알린다
    public static event Action<string> RaidStarted;
    // 퀘스트 완료 (tarkov.dev task id). 앱이 켜진 뒤에 완료된 것만 알린다
    public static event Action<string> QuestCompleted;

    // 로그 폴더 (못 찾았으면 null)
    public string LogRoot { get; private set; }
    public bool IsActive => LogRoot != null && !disabled;
    // 지금 진행 중인 레이드의 맵 (앱을 켜기 전에 시작된 레이드 포함). 레이드가 끝났거나 모르면 null
    public string CurrentRaidMap => raidStartAt > raidEndAt ? raidMap : null;

    const float PollInterval = 1f;
    // 앱을 켰을 때 이 시간 안에 시작된(아직 끝나지 않은) 레이드면 방금 시작한 것으로 보고 알린다 (맵 로딩 중에 앱을 켠 경우)
    static readonly TimeSpan RecentRaid = TimeSpan.FromMinutes(10);
    const float FindInterval = 15f;
    const int MaxErrors = 10;

    // 타르코프 맵 ID → tarkov.dev normalizedName (MapConfig/MapCatalog와 같은 이름).
    // 레이드 시작 줄은 맵 ID(RezervBase), 맵 로딩 줄은 씬 이름(Rezerv_Base)을 써서 둘 다 둔다
    static readonly Dictionary<string, string> MapIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "bigmap", "customs" },
        { "factory4_day", "factory" },
        { "factory4_night", "factory" },
        { "factory_day", "factory" },
        { "factory_night", "factory" },
        { "Woods", "woods" },
        { "Shoreline", "shoreline" },
        { "Interchange", "interchange" },
        { "RezervBase", "reserve" },
        { "Rezerv_Base", "reserve" },
        { "laboratory", "the-lab" },
        { "Lighthouse", "lighthouse" },
        { "TarkovStreets", "streets-of-tarkov" },
        { "city", "streets-of-tarkov" },
        { "Sandbox", "ground-zero" },
        { "Sandbox_high", "ground-zero" },
        { "Sandbox_start", "ground-zero" },
        { "Labyrinth", "the-labyrinth" },
        { "Terminal", "terminal" },
    };

    static readonly Regex ScenePreset = new Regex(@"scene preset path:.*?rcid:(\w+)\.scenespreset", RegexOptions.Compiled);
    static readonly Regex TransitStart = new Regex(@"\[Transit\].*?Locations:\s*(\w+)", RegexOptions.Compiled);
    static readonly Regex PvpStart = new Regex(@"NetworkGameCreate.*?Location:\s*(\w+)", RegexOptions.Compiled);
    static readonly Regex RaidEnd = new Regex(@"/client/match/(local/end|exit)\b|/client/game/profile/select\b|""type""\s*:\s*""userMatchOver""", RegexOptions.Compiled);
    static readonly Regex QuestSuccess = new Regex(@"""templateId""\s*:\s*""([0-9a-f]{24}) successMessageText""", RegexOptions.Compiled);

    class Tail
    {
        public string path;
        public long offset;   // 여기까지 읽음. 끝나지 않은 줄은 다음에 다시 읽도록 offset을 그 줄 앞에 둔다
    }

    string sessionDir;
    Tail application, backend, notifications;
    bool live;                      // false: 앱을 켰을 때 이미 있던 내용을 읽는 중 (상태만 파악하고 알리지 않음)
    string raidMap;
    DateTime raidStartAt = DateTime.MinValue, raidEndAt = DateTime.MinValue;   // 로그 시각
    string firedMap;                                                            // 마지막으로 알린 레이드
    DateTime firedAt = DateTime.MinValue;
    readonly HashSet<string> completed = new HashSet<string>();
    float nextPoll, nextFind;
    int errors;
    bool disabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject(nameof(TarkovLogWatcher));
        DontDestroyOnLoad(go);
        go.AddComponent<TarkovLogWatcher>();
        QuestCompleted += MapUserSettings.RemoveQuestFromAll;   // 어느 맵에 켜 둔 퀘스트든 저장된 선택에서 뺀다
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (disabled || Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + PollInterval;
        try
        {
            Poll();
            errors = 0;
        }
        catch (Exception e)
        {
            // 게임이 파일을 쓰는 중이라 잠깐 못 읽는 경우가 있다. 계속 실패하면 기능을 끈다
            if (++errors >= MaxErrors)
            {
                disabled = true;
                Debug.LogWarning($"[TarkovLog] 로그를 읽지 못해 자동 기능을 끕니다: {e.Message}");
            }
        }
    }

    void Poll()
    {
        if (LogRoot == null)
        {
            if (Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + FindInterval;
            LogRoot = FindLogRoot();
            if (LogRoot == null) return;
            Debug.Log($"[TarkovLog] 로그 폴더: {LogRoot}");
        }

        // 게임을 새로 켜면 새 세션 폴더가 생긴다
        string newest = NewestSession(LogRoot);
        if (newest == null) return;
        if (newest != sessionDir)
        {
            bool firstAttach = sessionDir == null;
            sessionDir = newest;
            application = new Tail();
            backend = new Tail();
            notifications = new Tail();
            live = !firstAttach;   // 앱을 켰을 때 이미 있던 세션은 먼저 끝까지 읽어 현재 상태만 파악
            raidMap = null;
            raidStartAt = raidEndAt = DateTime.MinValue;
        }

        application.path = LatestFile(sessionDir, "application");
        backend.path = LatestFile(sessionDir, "backend");
        notifications.path = LatestFile(sessionDir, "push-notifications");
        // 세 로그의 새 줄을 시각 순서로 합쳐서 처리한다 (예: backend의 레이드 끝 → application의 다음 레이드 시작)
        var lines = new List<(DateTime at, int order, int kind, string line)>();
        Collect(lines, application, 0);
        Collect(lines, backend, 1);
        Collect(lines, notifications, 2);
        lines.Sort((a, b) => a.at != b.at ? a.at.CompareTo(b.at) : a.order.CompareTo(b.order));
        foreach (var (at, _, kind, line) in lines)
        {
            if (kind == 0) OnApplicationLine(line, at);
            else if (kind == 1) OnBackendLine(line, at);
            else OnQuestLine(line);
        }

        if (!live)
        {
            live = true;
            // 앱을 켜기 직전에 시작된 레이드(맵 로딩 중이거나 막 시작)는 지금 시작한 것처럼 알린다
            if (CurrentRaidMap != null && DateTime.Now - raidStartAt < RecentRaid) FireRaidStarted();
        }
    }

    // ---- 줄 해석: 필요한 값(맵 ID, 퀘스트 ID)만 꺼내고 줄은 버린다 ----

    // 시각이 없는 줄(여러 줄 JSON의 나머지)은 바로 위 줄의 시각을 쓴다
    static void Collect(List<(DateTime, int, int, string)> lines, Tail tail, int kind)
    {
        DateTime last = DateTime.MinValue;
        foreach (string line in ReadNew(tail))
        {
            if (TryLineTime(line, out DateTime at)) last = at;
            lines.Add((last, lines.Count, kind, line));
        }
    }

    void OnApplicationLine(string line, DateTime at)
    {
        Match m = ScenePreset.Match(line);
        if (!m.Success) m = TransitStart.Match(line);
        if (!m.Success) m = PvpStart.Match(line);
        if (!m.Success || !MapIds.TryGetValue(m.Groups[1].Value, out string map)) return;

        if (at < raidStartAt) return;
        raidMap = map;
        raidStartAt = at;
        if (live) FireRaidStarted();
    }

    // 같은 레이드를 맵 로딩 줄과 접속 줄이 한 번씩 알리므로, 그 사이에 레이드가 끝나지 않았으면 한 번만
    void FireRaidStarted()
    {
        string map = CurrentRaidMap;
        if (map == null || (map == firedMap && raidEndAt < firedAt)) return;
        firedMap = map;
        firedAt = raidStartAt;

        Debug.Log($"[TarkovLog] 레이드 시작: {map}");
        RaidStarted?.Invoke(map);
    }

    // 줄 맨 앞의 시각 "2026-10-10 02:59:59.521|"
    static bool TryLineTime(string line, out DateTime at)
    {
        at = default;
        return line.Length >= 23 && line[4] == '-' && DateTime.TryParseExact(line.Substring(0, 23), "yyyy-MM-dd HH:mm:ss.fff",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out at);
    }

    void OnBackendLine(string line, DateTime at)
    {
        if (RaidEnd.IsMatch(line))
        {
            if (at > raidEndAt) raidEndAt = at;
            return;
        }

        OnQuestLine(line);
    }

    void OnQuestLine(string line)
    {
        Match m = QuestSuccess.Match(line);
        if (!m.Success) return;
        string taskId = m.Groups[1].Value;
        if (!completed.Add(taskId) || !live) return;

        Debug.Log($"[TarkovLog] 퀘스트 완료: {taskId}");
        QuestCompleted?.Invoke(taskId);
    }

    // ---- 파일 읽기 (읽기 전용, 게임이 쓰고 있는 파일도 공유 모드로 연다) ----

    static IEnumerable<string> ReadNew(Tail tail)
    {
        if (tail.path == null) return Array.Empty<string>();

        byte[] buffer;
        int read = 0;
        using (var fs = new FileStream(tail.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            // 폴더 목록의 파일 크기는 게임이 파일을 열고 있는 동안 갱신되지 않으므로 실제로 열어서 길이를 본다
            if (fs.Length < tail.offset) tail.offset = 0;   // 파일이 새로 만들어짐
            if (fs.Length == tail.offset) return Array.Empty<string>();
            fs.Seek(tail.offset, SeekOrigin.Begin);
            buffer = new byte[fs.Length - tail.offset];
            while (read < buffer.Length)
            {
                int n = fs.Read(buffer, read, buffer.Length - read);
                if (n <= 0) break;
                read += n;
            }
        }

        if (read == 0) return Array.Empty<string>();
        // 마지막 줄바꿈까지만 쓴다. 줄바꿈 바이트(0x0A)는 UTF-8 글자 안에 나오지 않아서 한글이 중간에 잘리지 않는다
        int end = Array.LastIndexOf(buffer, (byte)0x0A, read - 1);
        if (end < 0) return Array.Empty<string>();   // 아직 줄이 끝나지 않음
        tail.offset += end + 1;
        return Encoding.UTF8.GetString(buffer, 0, end).Split((char)0x0A).Select(l => l.TrimEnd((char)0x0D));
    }

    static string NewestSession(string root) =>
        Directory.GetDirectories(root, "log_*").OrderByDescending(Directory.GetCreationTimeUtc).FirstOrDefault();

    // "{날짜}_{버전} application_000.log" 중 번호가 가장 큰 파일
    static string LatestFile(string session, string kind) =>
        Directory.GetFiles(session, $"* {kind}_*.log").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();

    // ---- 로그 폴더 찾기 ----

    // 1) 설정 파일에 직접 지정한 폴더  2) Steam 라이브러리  3) 각 드라이브의 흔한 설치 위치
    static string FindLogRoot()
    {
        var candidates = new List<string>();
        string custom = AppSettings.Current.gameLogFolder;
        if (!string.IsNullOrWhiteSpace(custom)) candidates.Add(custom.Trim());

        foreach (string library in SteamLibraries())
            candidates.Add(Path.Combine(library, "steamapps", "common", "Escape from Tarkov", "build", "Logs"));

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            string d = drive.RootDirectory.FullName;
            candidates.Add(Path.Combine(d, "Battlestate Games", "EFT", "Logs"));
            candidates.Add(Path.Combine(d, "Battlestate Games", "Escape from Tarkov", "Logs"));
            candidates.Add(Path.Combine(d, "SteamLibrary", "steamapps", "common", "Escape from Tarkov", "build", "Logs"));
        }

        foreach (string dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(dir) && Directory.GetDirectories(dir, "log_*").Length > 0) return dir;
            }
            catch (Exception) { }
        }
        return null;
    }

    // Steam이 기억하는 라이브러리 폴더들 (libraryfolders.vdf의 "path")
    static IEnumerable<string> SteamLibraries()
    {
        var steamDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
        };
        foreach (string steam in steamDirs)
        {
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            string text;
            try
            {
                if (!File.Exists(vdf)) continue;
                text = File.ReadAllText(vdf);
            }
            catch (Exception)
            {
                continue;
            }
            yield return steam;
            foreach (Match m in Regex.Matches(text, @"""path""\s+""([^""]+)"""))
                yield return m.Groups[1].Value.Replace(@"\\", @"\");
        }
    }
}
