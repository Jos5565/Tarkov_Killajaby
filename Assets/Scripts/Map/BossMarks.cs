using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 보스 상태 메모 (출현 / 처치 / 없음). 게임에서 읽어오는 정보가 아니라 사용자가 지도 위쪽 보스 목록(BossStatusPanel)을
// 클릭해 직접 남기는 표시다. (로그에는 보스 등장 · 처치가 남지 않고, 게임 메모리는 읽지 않는다)
//   - 클릭할 때마다 미확인 → 출현 → 처치 → 없음 → 미확인
//   - 파티 버전: 내 표시를 파티원에게 보내고, 파티원 표시도 함께 보여준다 (같은 보스는 가장 최근 표시)
//   - 새 레이드가 시작되면(로그) 모두 지운다. 앱을 끄면 사라진다 (레이드 한 판 동안의 메모)
public static class BossMarks
{
    public enum Status : byte { None = 0, Seen = 1, Killed = 2, Absent = 3 }

    public struct Mark
    {
        public uint boss;          // Key(맵, 보스 이름)
        public Status status;
        public float time;         // 표시한 시각 (Time.realtimeSinceStartup 기준)
        public string by;          // 표시한 사람 (내 표시는 null)
    }

    // 내 표시 (미확인으로 되돌린 것도 남겨서 파티원의 예전 표시를 덮는다)
    static readonly Dictionary<uint, Mark> mine = new Dictionary<uint, Mark>();
    // 파티원 표시 (파티원 번호 → 표시들)
    static readonly Dictionary<ushort, List<Mark>> remote = new Dictionary<ushort, List<Mark>>();

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        TarkovLogWatcher.RaidStarted -= OnRaidStarted;
        TarkovLogWatcher.RaidStarted += OnRaidStarted;
    }

    static void OnRaidStarted(string map) => Clear();

    public static void Clear()
    {
        mine.Clear();
        remote.Clear();
        Changed?.Invoke();
    }

    // 같은 맵 · 같은 보스 이름이면 모든 PC에서 같은 값
    public static uint Key(string map, string boss)
    {
        string s = $"{map}|{boss}";
        uint h = 2166136261;   // FNV-1a
        foreach (char c in s)
        {
            h ^= c;
            h *= 16777619;
        }
        return h == 0 ? 1u : h;
    }

    // 클릭: 지금 보이는 상태의 다음으로
    public static void Cycle(uint key)
    {
        Status next = Current(key).status switch
        {
            Status.None => Status.Seen,
            Status.Seen => Status.Killed,
            Status.Killed => Status.Absent,
            _ => Status.None,
        };
        mine[key] = new Mark { boss = key, status = next, time = Time.realtimeSinceStartup };
        Changed?.Invoke();
    }

    // 내 표시와 파티원 표시 중 가장 최근 것
    public static Mark Current(uint key)
    {
        Mark best = default;
        bool found = false;
        if (mine.TryGetValue(key, out Mark m))
        {
            best = m;
            found = true;
        }
        foreach (List<Mark> list in remote.Values)
            foreach (Mark r in list)
                if (r.boss == key && (!found || r.time > best.time))
                {
                    best = r;
                    found = true;
                }
        return found ? best : new Mark { boss = key, status = Status.None };
    }

    // 파티로 보낼 내 표시 (최근 것부터 최대 count개)
    public static List<Mark> Mine(int count) => mine.Values.OrderByDescending(m => m.time).Take(count).ToList();

    // 파티원 표시 받기 (그 사람의 전체 목록으로 바꾼다)
    public static void SetRemote(ushort member, string nickname, IEnumerable<Mark> marks)
    {
        var list = marks.Select(m => new Mark { boss = m.boss, status = m.status, time = m.time, by = nickname }).ToList();
        if (remote.TryGetValue(member, out List<Mark> old) && Same(old, list)) return;
        remote[member] = list;
        Changed?.Invoke();
    }

    public static void RemoveRemote(ushort member)
    {
        if (remote.Remove(member)) Changed?.Invoke();
    }

    public static void ClearRemote()
    {
        if (remote.Count == 0) return;
        remote.Clear();
        Changed?.Invoke();
    }

    static bool Same(List<Mark> a, List<Mark> b) =>
        a.Count == b.Count && a.Zip(b, (x, y) => x.boss == y.boss && x.status == y.status && Mathf.Abs(x.time - y.time) < 2f).All(v => v);

    public static string Label(Status s) => s switch
    {
        Status.Seen => "출현",
        Status.Killed => "처치",
        Status.Absent => "없음",
        _ => "미확인",
    };
}
