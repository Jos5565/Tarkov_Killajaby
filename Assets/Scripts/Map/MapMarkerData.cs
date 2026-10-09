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
    // 배틀패스 문서 (종류별로 필터에서 따로 켜고 끈다)
    DocFinancial,
    DocProject,
    DocBlueprints,
    DocTechnical,
    DocTest,
    DocUser,
    DocMedical,
}

// 배틀패스 문서: tarkov.dev 아이템 분류 "Battle Pass Document".
// 맵 데이터의 lootLoose(아이템이 놓일 수 있는 자리) 중 이 아이템이 나오는 자리를 마커로 만든다.
public static class BattlePassDocs
{
    public const string Title = "배틀패스 문서";

    public readonly struct Doc
    {
        public readonly string itemId;
        public readonly MarkerType type;
        public readonly string name;

        public Doc(string itemId, MarkerType type, string name)
        {
            this.itemId = itemId;
            this.type = type;
            this.name = name;
        }
    }

    public static readonly Doc[] All =
    {
        new Doc("6a31807f17005505b70d5827", MarkerType.DocFinancial, "회계 문서"),
        new Doc("6a3181f178450ec91c0ea1aa", MarkerType.DocProject, "프로젝트 문서"),
        new Doc("6a31824878450ec91c0ea1ae", MarkerType.DocBlueprints, "설계도 및 기술 문서"),
        new Doc("6a31830dde69ceafd805afa0", MarkerType.DocTechnical, "기술 문서"),
        new Doc("6a31828557705071410ca00e", MarkerType.DocTest, "테스트 문서"),
        new Doc("6a3182b72fd891345e047eef", MarkerType.DocUser, "사용자 문서"),
        new Doc("6a3182dc6cd8de21cf0a3a7d", MarkerType.DocMedical, "의료 문서"),
    };

    public static bool IsDoc(MarkerType type) => type >= MarkerType.DocFinancial && type <= MarkerType.DocMedical;

    public static bool TryGetByItem(string itemId, out Doc doc)
    {
        foreach (Doc d in All)
            if (d.itemId == itemId)
            {
                doc = d;
                return true;
            }
        doc = default;
        return false;
    }
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
    public string floor;     // 층 (배틀패스 문서: "지상", "2층", "지하" 등). 없으면 빈 문자열
    public Vector3 position; // 게임 좌표
}

[Serializable]
public class QuestObjective
{
    public string id;
    public string description;
    public bool optional;
    public bool thisMap;   // 이 맵에서 하는 목표 (목표의 maps에 이 맵이 있거나, 이 맵에 위치 마커가 있음)
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
