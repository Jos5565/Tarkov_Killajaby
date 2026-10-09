using System;
using System.Collections.Generic;
using UnityEngine;

// 로비에 표시할 맵 목록. configPath가 비어 있는 맵은 버튼만 보이고 선택할 수 없다(준비 중).
// MapConfig는 지도 이미지를 참조하므로 직접 참조하지 않고 Resources 경로로 두어,
// 로비에서는 아무 지도도 메모리에 올리지 않고 입장할 때 고른 맵만 불러온다.
[CreateAssetMenu(fileName = "MapCatalog", menuName = "Tarkov/Map Catalog")]
public class MapCatalog : ScriptableObject
{
    // 맵 버튼에 마우스를 올리면 보여주는 보스 정보 (Tools > Tarkov > API Data가 채운다)
    [Serializable]
    public class BossInfo
    {
        public string name;
        public float chanceMin;   // 같은 보스가 여러 그룹으로 나오면 그룹별 확률 범위
        public float chanceMax;
        public int groups = 1;
        public int locations;     // 출현 구역 수 (0이면 위치 정보 없음)
    }

    [Serializable]
    public class Entry
    {
        public string displayName;
        public Sprite icon;
        [Tooltip("Resources 기준 MapConfig 경로 (예: Maps/Customs). 비어 있으면 버튼 비활성화")]
        public string configPath;
        [Tooltip("tarkov.dev 맵 이름 (예: customs). 보스 정보를 채울 때 사용")]
        public string normalizedName;
        public List<BossInfo> bosses = new List<BossInfo>();

        public bool IsAvailable => !string.IsNullOrEmpty(configPath);

        public MapConfig LoadConfig() => IsAvailable ? Resources.Load<MapConfig>(configPath) : null;
    }

    public List<Entry> maps = new List<Entry>();
}
