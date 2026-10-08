using System;
using System.Collections.Generic;
using UnityEngine;

// 값은 씬/SO에 숫자로 저장되므로 순서를 바꾸지 말고 뒤에 추가한다
public enum MarkerType
{
    ExtractPmc,
    Transit,
    PmcSpawn,
    ScavSpawn,
    SniperScav,
    Boss,
    Quest,
    QuestItem,
    ExtractScav,
    ExtractShared,
}

[Serializable]
public class MapMarker
{
    public MarkerType type;
    public string key;       // 묶음 ID (퀘스트: task id). 같은 key끼리 함께 켜고 끈다
    public string name;      // 탈출구 이름, 퀘스트 이름, 보스 이름 등
    public string group;     // 퀘스트: 상인 이름
    public string objective; // 퀘스트 목표 ID. 같은 목표의 위치들은 그중 한 곳만 가면 된다 (루트 계산용)
    public string detail;    // 진영, 출현 확률, 목표 설명, 퀘스트 아이템 이름 등
    public Vector3 position; // 게임 좌표
}

[Serializable]
public class QuestObjective
{
    public string id;
    public string description;
    public bool optional;
}

// 이 맵에 위치가 있는 퀘스트의 전체 목표 (위치가 없는 목표 포함: "처치", "아이템 전달" 등)
[Serializable]
public class QuestData
{
    public string key;      // task id (MapMarker.key와 같음)
    public string name;
    public string trader;
    public List<QuestObjective> objectives = new List<QuestObjective>();
}

// tarkov.dev API에서 받아 변환한 맵 마커 데이터 (Tools > Tarkov > API Data 창에서 생성)
public class MapMarkerData : ScriptableObject
{
    public string mapNormalizedName;
    public string gameMode;
    public string language;
    public string downloadedAt;
    public List<MapMarker> markers = new List<MapMarker>();
    public List<QuestData> quests = new List<QuestData>();

    public QuestData FindQuest(string key) => string.IsNullOrEmpty(key) ? null : quests.Find(q => q.key == key);
}
