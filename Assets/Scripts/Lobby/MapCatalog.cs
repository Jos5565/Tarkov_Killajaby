using System;
using System.Collections.Generic;
using UnityEngine;

// 로비에 표시할 맵 목록. configPath가 비어 있는 맵은 버튼만 보이고 선택할 수 없다(준비 중).
// MapConfig는 지도 이미지를 참조하므로 직접 참조하지 않고 Resources 경로로 두어,
// 로비에서는 아무 지도도 메모리에 올리지 않고 입장할 때 고른 맵만 불러온다.
[CreateAssetMenu(fileName = "MapCatalog", menuName = "Tarkov/Map Catalog")]
public class MapCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string displayName;
        public Sprite icon;
        [Tooltip("Resources 기준 MapConfig 경로 (예: Maps/Customs). 비어 있으면 버튼 비활성화")]
        public string configPath;

        public bool IsAvailable => !string.IsNullOrEmpty(configPath);

        public MapConfig LoadConfig() => IsAvailable ? Resources.Load<MapConfig>(configPath) : null;
    }

    public List<Entry> maps = new List<Entry>();
}
