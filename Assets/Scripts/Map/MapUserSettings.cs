using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 맵별 사용자 설정 (맵 씬에서 켜고 끈 상태). persistentDataPath/MapSettings/{map}.json 에 저장.
// 파일이 없으면(처음 실행) 퀘스트는 모두 꺼진 상태로 시작한다.
[Serializable]
public class MapUserSettings
{
    [Serializable]
    public struct TypeState
    {
        public MarkerType type;
        public bool visible;
    }

    public List<string> visibleQuests = new List<string>();     // 켜진 퀘스트 key (task id)
    public List<TypeState> types = new List<TypeState>();      // 왼쪽 필터 패널 종류별 표시
    public List<string> expandedGroups = new List<string>();   // 펼친 필터 그룹 제목 (처음에는 전부 접힘)
    public bool routeEnabled = true;                            // [경로] 버튼
    public string routeEnd = "";                                // 경로 도착 탈출구 이름표 이름 (비면 마지막 퀘스트에서 끝남)

    static string Dir => Path.Combine(Application.persistentDataPath, "MapSettings");
    static string FilePath(string map) => Path.Combine(Dir, map + ".json");

    // 저장된 파일이 없으면 null
    public static MapUserSettings Load(string map)
    {
        string path = FilePath(map);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonUtility.FromJson<MapUserSettings>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MapUserSettings] 읽기 실패, 초기 상태로 시작: {path}\n{e.Message}");
            return null;
        }
    }

    public void Save(string map)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath(map), JsonUtility.ToJson(this, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MapUserSettings] 저장 실패: {FilePath(map)}\n{e.Message}");
        }
    }

    public bool TryGetType(MarkerType type, out bool visible)
    {
        int i = types.FindIndex(s => s.type == type);
        visible = i >= 0 && types[i].visible;
        return i >= 0;
    }

    public void SetType(MarkerType type, bool visible)
    {
        types.RemoveAll(s => s.type == type);
        types.Add(new TypeState { type = type, visible = visible });
    }
}
