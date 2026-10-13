using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 레이드 중에 직접 체크한 "완료한 목표" (로그로는 레이드 중 목표 달성을 알 수 없어서 사용자가 표시한다).
//   - 경로 1번 퀘스트 창에서 목표를 클릭하거나, 지도의 퀘스트 마커를 우클릭하면 켜고 끈다
//   - 체크한 목표는 취소선 + 흐리게 표시하고, 동선(QuestRoute)에서 빠진다
//   - 상인에게 퀘스트를 완료 처리하면(로그) 그 퀘스트의 체크는 지운다
// 앱을 다시 켜도 유지된다 (PlayerPrefs, 모든 맵 공통).
public static class QuestProgress
{
    const string Pref = "QuestProgress.Done";

    // 퀘스트 id → 체크한 목표 id
    static Dictionary<string, HashSet<string>> done;

    // 체크가 바뀐 퀘스트 id
    public static event Action<string> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        TarkovLogWatcher.QuestCompleted -= ClearQuest;
        TarkovLogWatcher.QuestCompleted += ClearQuest;
    }

    public static bool IsDone(string questKey, string objectiveId)
    {
        if (string.IsNullOrEmpty(questKey) || string.IsNullOrEmpty(objectiveId)) return false;
        Load();
        return done.TryGetValue(questKey, out HashSet<string> set) && set.Contains(objectiveId);
    }

    public static void Toggle(string questKey, string objectiveId)
    {
        if (string.IsNullOrEmpty(questKey) || string.IsNullOrEmpty(objectiveId)) return;
        Load();
        if (!done.TryGetValue(questKey, out HashSet<string> set)) done.Add(questKey, set = new HashSet<string>());
        if (!set.Remove(objectiveId)) set.Add(objectiveId);
        if (set.Count == 0) done.Remove(questKey);
        Save();
        Changed?.Invoke(questKey);
    }

    public static void ClearQuest(string questKey)
    {
        Load();
        if (questKey == null || !done.Remove(questKey)) return;
        Save();
        Changed?.Invoke(questKey);
    }

    // 저장 형식: "퀘스트id:목표id,목표id;퀘스트id:목표id"
    static void Load()
    {
        if (done != null) return;
        done = new Dictionary<string, HashSet<string>>();
        foreach (string entry in PlayerPrefs.GetString(Pref, "").Split(';'))
        {
            string[] parts = entry.Split(':');
            if (parts.Length != 2 || parts[0].Length == 0) continue;
            var set = new HashSet<string>(parts[1].Split(',').Where(s => s.Length > 0));
            if (set.Count > 0) done[parts[0]] = set;
        }
    }

    static void Save()
    {
        PlayerPrefs.SetString(Pref, string.Join(";", done.Select(kv => kv.Key + ":" + string.Join(",", kv.Value))));
        PlayerPrefs.Save();
    }
}
