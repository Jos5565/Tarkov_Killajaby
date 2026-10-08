using System;
using System.Collections.Generic;
using UnityEngine;

// 로비에 표시할 맵 목록. config가 비어 있는 맵은 버튼만 보이고 선택할 수 없다(준비 중).
[CreateAssetMenu(fileName = "MapCatalog", menuName = "Tarkov/Map Catalog")]
public class MapCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string displayName;
        public Sprite icon;
        [Tooltip("비어 있으면 버튼 비활성화")]
        public MapConfig config;

        public bool IsAvailable => config != null;
    }

    public List<Entry> maps = new List<Entry>();
}
