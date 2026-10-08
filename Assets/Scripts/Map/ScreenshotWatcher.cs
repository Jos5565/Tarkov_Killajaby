using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using UnityEngine;

// 로비에서 지정한 스크린샷 폴더를 감시해, 새 스크린샷 파일명으로 내 위치(WhereIAM)를 갱신한다.
// 위치가 바뀌면 WhereIAM.PoseChanged → QuestRoute가 경로를 다시 계산한다.
//   - 시작 시: 폴더의 가장 최근 스크린샷으로 위치를 잡는다
//   - 실행 중: FileSystemWatcher로 새 파일 감지 + 혹시 놓친 경우를 위해 폴더 수정 시각을 주기적으로 확인
// Play 중에만 동작한다.
public class ScreenshotWatcher : MonoBehaviour
{
    public WhereIAM whereIAM;
    [Tooltip("맵에 들어왔을 때 폴더의 가장 최근 스크린샷으로 위치를 잡는다")]
    public bool applyLatestOnStart = true;
    [Tooltip("감시 이벤트를 놓쳤을 때를 대비한 폴더 확인 주기(초)")]
    public float pollInterval = 2f;

    public string Folder { get; private set; }
    public bool IsWatching => watcher != null;
    public string LastFile { get; private set; }

    static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp" };

    FileSystemWatcher watcher;
    readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();   // 감시 이벤트는 다른 스레드에서 온다
    DateTime lastFolderWrite;
    float nextPoll;

    void Start()
    {
        Folder = AppSettings.ScreenshotFolder;
        if (!Directory.Exists(Folder))
        {
            Debug.LogWarning($"[ScreenshotWatcher] 스크린샷 폴더가 없습니다: {Folder} (로비에서 경로를 확인하세요)");
            return;
        }

        lastFolderWrite = Directory.GetLastWriteTimeUtc(Folder);
        if (applyLatestOnStart)
        {
            string latest = FindLatest();
            if (latest != null) queue.Enqueue(latest);   // WhereIAM.Start(테스트 이미지) 이후 Update에서 적용
        }

        try
        {
            watcher = new FileSystemWatcher(Folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
            };
            watcher.Created += (s, e) => queue.Enqueue(e.FullPath);
            watcher.Renamed += (s, e) => queue.Enqueue(e.FullPath);
            watcher.EnableRaisingEvents = true;
            Debug.Log($"[ScreenshotWatcher] 감시 시작: {Folder}");
        }
        catch (Exception e)
        {
            // 감시를 못 해도 아래 주기 확인으로 동작한다
            Debug.LogWarning($"[ScreenshotWatcher] 폴더 감시 실패, 주기 확인만 사용: {e.Message}");
            watcher = null;
        }
    }

    void OnDestroy()
    {
        if (watcher == null) return;
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
        watcher = null;
    }

    void Update()
    {
        if (string.IsNullOrEmpty(Folder)) return;

        // 한 프레임에 여러 개가 들어오면 마지막 것만 쓴다
        string newest = null;
        while (queue.TryDequeue(out string path)) newest = path;
        if (newest != null) Apply(newest);

        // 감시 이벤트를 놓친 경우: 폴더가 바뀌었으면 최신 파일 확인
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + pollInterval;
        try
        {
            DateTime write = Directory.GetLastWriteTimeUtc(Folder);
            if (write == lastFolderWrite) return;
            lastFolderWrite = write;
            string latest = FindLatest();
            if (latest != null) Apply(latest);
        }
        catch (IOException) { }
    }

    void Apply(string path)
    {
        if (!IsImage(path)) return;
        string name = Path.GetFileNameWithoutExtension(path);
        if (name == LastFile || !WhereIAM.TryParse(name, out _, out _)) return;   // 좌표 없는 파일(다른 캡처 등)은 무시

        if (whereIAM != null && whereIAM.ApplyFileName(name)) LastFile = name;
    }

    // 좌표가 들어 있는 스크린샷 중 가장 최근 파일
    string FindLatest()
    {
        try
        {
            return Directory.EnumerateFiles(Folder)
                .Where(f => IsImage(f) && WhereIAM.TryParse(Path.GetFileNameWithoutExtension(f), out _, out _))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScreenshotWatcher] 폴더 읽기 실패: {e.Message}");
            return null;
        }
    }

    static bool IsImage(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}
