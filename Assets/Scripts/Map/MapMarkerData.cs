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
    // 지도 위 구역 이름 (마커가 아니라 글자. 필터에서 켜고 끄기 위해 종류로 둔다)
    AreaLabel,
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
    public string conditions;   // 설명에 없는 조건 (예: "헤드샷 · 40m 이상 · 21:00~05:00"). 없으면 빈 문자열
}

// 퀘스트에 필요한 아이템 (건네기·찾기·설치·판매·표시할 아이템, 열쇠)
[Serializable]
public class QuestRequirement
{
    public string objective;          // 관련 목표 ID (열쇠는 비어 있음)
    public string kind;               // give / find / plant / sell / mark / key
    public List<string> items = new List<string>();   // 아이템 이름. 여러 개면 그중 아무거나 (최대 몇 개만 저장)
    public List<string> itemIds = new List<string>(); // items와 같은 순서의 아이템 ID (이미지: Sprites/Items/{ID}.png)
    public int alternatives = 1;      // 고를 수 있는 아이템 전체 수 (items보다 많을 수 있음)
    public int count = 1;
    public bool foundInRaid;          // 레이드에서 찾은(인레이드) 아이템만 인정
}

// 이 맵에 위치가 있는 퀘스트의 전체 목표 (위치가 없는 목표 포함: "처치", "아이템 전달" 등)
[Serializable]
public class QuestData
{
    public string key;      // task id (MapMarker.key와 같음)
    public string name;
    public string trader;
    public List<QuestObjective> objectives = new List<QuestObjective>();
    public List<QuestRequirement> requirements = new List<QuestRequirement>();
    public bool noLocation;   // 지도 위치 정보가 없는 이 맵 퀘스트 (처치·탈출 등). 목록·상세에는 나오고 마커·경로는 없다
    public string guide;      // 공략 메모 "A → B → C" (Translations/quest_guides_{언어}.json). 없으면 빈 문자열
    public string wikiLink;   // 퀘스트 위키 주소 (퀘스트 목록에서 우클릭하면 연다)
}

// 지도 위 구역 이름 (예: 세관 "구골조"). tarkov-dev 지도 설정의 labels
[Serializable]
public class MapLabel
{
    public string text;
    public Vector2 position;      // 게임 좌표 (x, z)
    public float size = 100f;     // 글자 크기 비율 (%). 작은 건물·가게 이름은 60~90
    public float rotation;        // 시계 방향 각도 (길 이름 등)
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
    public List<MapLabel> labels = new List<MapLabel>();

    public QuestData FindQuest(string key) => string.IsNullOrEmpty(key) ? null : quests.Find(q => q.key == key);
}
