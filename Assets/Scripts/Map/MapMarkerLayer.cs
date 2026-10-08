using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MapConfig.markerData의 마커들을 지도 위에 배치한다. MapContent/Markers 아래에 둔다.
// 탈출구/트랜짓은 NameTag 프리팹, 나머지는 점(dot)으로 표시.
// ExecuteAlways: Play 하지 않아도 편집 모드에서 마커를 미리 보여준다.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class MapMarkerLayer : MonoBehaviour
{
    [Serializable]
    public class Style
    {
        public MarkerType type;
        public bool visible = true;
        [Tooltip("점 색. NameTag 타입은 아이콘 색")]
        public Color color = Color.white;
        [Tooltip("점 크기(px). NameTag는 nameTagScale을 사용")]
        public float size = 14f;
        [Tooltip("비우면 dotSprite(원). 퀘스트 아이콘 등")]
        public Sprite sprite;
        [Tooltip("마우스를 올리면 MapTooltip으로 이름/설명 표시")]
        public bool showInfo;
    }

    // 퀘스트 목록 패널용 요약 (key = task id)
    public class KeyInfo
    {
        public string key;
        public string name;
        public string group;
        public int count;
    }

    public MapView mapView;

    [Header("Prefabs")]
    public GameObject nameTagPrefab;
    public Sprite dotSprite;
    [Tooltip("NameTag 표시 배율 (화면 기준)")]
    public float nameTagScale = 0.5f;
    [Tooltip("지도 기준 마커 배율. 마커는 지도와 함께 확대/축소된다 (PMC/Scav 화면 폭이 달라도 지도 대비 크기 동일)")]
    public float markerScale = 2.3f;
    [Tooltip("켜면 지도를 확대해도 마커가 화면에서 같은 크기로 유지된다 (예전 방식)")]
    public bool keepScreenSize = false;
    [Tooltip("NameTag 프리팹 안에서 색을 바꿀 아이콘 Image 경로")]
    public string nameTagIconPath = "Icon";

    [Header("Styles")]
    public List<Style> styles = new List<Style>();

    readonly Dictionary<MarkerType, RectTransform> groups = new Dictionary<MarkerType, RectTransform>();
    readonly List<RectTransform> items = new List<RectTransform>();
    readonly List<float> itemScales = new List<float>();
    readonly Dictionary<string, List<GameObject>> keyItems = new Dictionary<string, List<GameObject>>();
    readonly HashSet<string> visibleKeys = new HashSet<string>();   // Play 중 켜진 퀘스트 (저장/불러오기)
    readonly List<KeyInfo> quests = new List<KeyInfo>();
    readonly List<MapMarker> placed = new List<MapMarker>();
    MapUserSettings settings;   // Play 중에만 사용. 편집 모드 미리보기는 전부 표시
    float lastMapScale = -1f;

    public bool IsBuilt { get; private set; }

    // 이 맵의 퀘스트 목록 (Quest/QuestItem 마커의 key로 묶음, 이름순)
    public IReadOnlyList<KeyInfo> Quests => quests;

    // 실제로 지도에 배치된 마커 (범위 밖/진영 제외 마커는 없음)
    public IReadOnlyList<MapMarker> Placed => placed;

    // 표시 여부가 바뀌면 호출 (필터 UI 갱신용)
    public event Action<MarkerType, bool> VisibilityChanged;
    public event Action<string, bool> KeyVisibilityChanged;
    public event Action Built;

    void OnEnable()
    {
        if (mapView == null) return;
        mapView.Loaded += OnMapLoaded;
        if (mapView.IsLoaded) Build();
    }

    void OnDisable()
    {
        if (mapView != null) mapView.Loaded -= OnMapLoaded;
        foreach (RectTransform group in groups.Values)
            if (group != null) EditorPreview.DestroyLater(group.gameObject);
        ResetLists();
    }

#if UNITY_EDITOR
    // 편집 모드에서 Inspector 값(색, 크기 등)을 바꾸면 미리보기를 다시 만든다
    void OnValidate()
    {
        if (!EditorPreview.IsEditMode) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled && mapView != null && mapView.IsLoaded) Build();
        };
    }
#endif

    void OnMapLoaded(MapView view) => Build();

    public void Build()
    {
        Clear();
        EnsureSettings();

        MapMarkerData data = mapView.config.markerData;
        if (data == null)
        {
            Debug.LogWarning($"[MapMarkerLayer] {mapView.config.name}에 markerData가 없습니다. Tools > Tarkov > API Data에서 받아주세요.");
            return;
        }

        // styles 순서대로 그룹을 먼저 만든다 (뒤에 있을수록 위에 그려짐)
        foreach (Style s in styles) GetGroup(s.type, s);

        int skipped = 0;
        foreach (MapMarker marker in data.markers)
        {
            if (!IsAvailable(marker.type)) continue;
            if (!mapView.config.IsInBounds(marker.position)) { skipped++; continue; }

            Style style = GetStyle(marker.type);
            RectTransform group = GetGroup(marker.type, style);
            placed.Add(marker);

            bool isNameTag = UsesNameTag(marker.type) && nameTagPrefab != null;
            RectTransform rt = isNameTag ? CreateNameTag(marker, style, group) : CreateDot(marker, style, group);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = mapView.GameToLocal(marker.position);

            EditorPreview.MarkDontSave(rt.gameObject);
            items.Add(rt);
            itemScales.Add(isNameTag ? nameTagScale : 1f);

            if (!string.IsNullOrEmpty(marker.key))
            {
                if (!keyItems.TryGetValue(marker.key, out List<GameObject> list))
                    keyItems.Add(marker.key, list = new List<GameObject>());
                list.Add(rt.gameObject);
                rt.gameObject.SetActive(IsKeyVisible(marker.key));

                if (marker.type == MarkerType.Quest || marker.type == MarkerType.QuestItem)
                    AddQuest(marker);
            }
        }

        quests.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCulture));
        lastMapScale = -1f;
        IsBuilt = true;
        Built?.Invoke();
        if (Application.isPlaying)
            Debug.Log($"[MapMarkerLayer] 마커 {items.Count}개 표시 (범위 밖 {skipped}개 제외)");
    }

    public bool IsVisible(MarkerType type)
    {
        EnsureSettings();   // 저장된 종류별 표시 상태를 먼저 반영
        return IsAvailable(type) && GetStyle(type).visible;
    }

    // 로비에서 고른 플레이어 타입으로 쓸 수 없는 마커는 아예 만들지 않는다.
    //   PMC : 스캐브 전용 탈출구 제외
    //   Scav: PMC 전용 탈출구, 퀘스트(목표/아이템) 제외
    // 로비를 거치지 않고 실행했거나 편집 모드면 전부 표시.
    public bool IsAvailable(MarkerType type)
    {
        if (!Application.isPlaying || !GameSession.HasSelection) return true;
        if (GameSession.Side == PlayerSide.Pmc) return type != MarkerType.ExtractScav;
        return type != MarkerType.ExtractPmc && !IsQuestType(type);
    }

    public static bool IsQuestType(MarkerType type) => type == MarkerType.Quest || type == MarkerType.QuestItem;

    // 이 판에서 퀘스트 기능(목록, 경로, 도착 알림)을 쓸 수 있는지 (스캐브면 false)
    public bool QuestsAvailable => IsAvailable(MarkerType.Quest) || IsAvailable(MarkerType.QuestItem);

    public Color GetColor(MarkerType type) => GetStyle(type).color;

    // 퀘스트 전체 목표 (마커 데이터에 없으면 null)
    public QuestData GetQuest(string key) =>
        mapView != null && mapView.config != null && mapView.config.markerData != null ? mapView.config.markerData.FindQuest(key) : null;

    public bool IsKeyVisible(string key)
    {
        if (!Application.isPlaying) return true;
        EnsureSettings();
        return visibleKeys.Contains(key);
    }

    // 퀘스트 하나(같은 key의 모든 마커)를 켜고 끈다. Play 중에만 동작하며 맵별로 저장된다.
    public void SetKeyVisible(string key, bool visible)
    {
        if (!Application.isPlaying || IsKeyVisible(key) == visible) return;
        if (visible) visibleKeys.Add(key);
        else visibleKeys.Remove(key);
        settings.visibleQuests = new List<string>(visibleKeys);
        SaveSettings();

        if (keyItems.TryGetValue(key, out List<GameObject> list))
            foreach (GameObject go in list)
                if (go != null) go.SetActive(visible);
        KeyVisibilityChanged?.Invoke(key, visible);
    }

    void AddQuest(MapMarker marker)
    {
        KeyInfo info = quests.Find(q => q.key == marker.key);
        if (info == null) quests.Add(info = new KeyInfo { key = marker.key, name = marker.name, group = marker.group });
        info.count++;
    }

    public void SetVisible(MarkerType type, bool visible)
    {
        Style style = GetStyle(type);
        if (style.visible == visible || !IsAvailable(type)) return;

        style.visible = visible;
        if (groups.TryGetValue(type, out RectTransform group)) group.gameObject.SetActive(visible);
        if (Application.isPlaying)
        {
            EnsureSettings();
            settings.SetType(type, visible);
            SaveSettings();
        }
        VisibilityChanged?.Invoke(type, visible);
    }

    // 필터 패널 그룹 접힘 상태 (맵별 저장)
    public bool IsGroupExpanded(string title, bool fallback)
    {
        if (!Application.isPlaying) return fallback;
        EnsureSettings();
        return !settings.collapsedGroups.Contains(title);
    }

    public void SetGroupExpanded(string title, bool expanded)
    {
        if (!Application.isPlaying) return;
        EnsureSettings();
        settings.collapsedGroups.Remove(title);
        if (!expanded) settings.collapsedGroups.Add(title);
        SaveSettings();
    }

    // ---- 맵별 사용자 설정 ----

    // Play 중 이 맵의 설정 (편집 모드에서는 null). 값을 바꾼 뒤 SaveSettings() 호출
    public MapUserSettings Settings
    {
        get
        {
            EnsureSettings();
            return settings;
        }
    }

    // 로비에서 고른 맵 우선 (MapView.Start보다 먼저 불릴 수 있으므로)
    string SettingsKey => GameSession.HasSelection ? GameSession.Map.normalizedName : mapView.config.normalizedName;

    void EnsureSettings()
    {
        if (!Application.isPlaying || settings != null || mapView == null) return;

        settings = MapUserSettings.Load(SettingsKey) ?? new MapUserSettings();   // 처음이면 퀘스트 전부 꺼짐
        visibleKeys.Clear();
        visibleKeys.UnionWith(settings.visibleQuests);
        foreach (MapUserSettings.TypeState s in settings.types)
            GetStyle(s.type).visible = s.visible;
    }

    public void SaveSettings()
    {
        if (settings != null) settings.Save(SettingsKey);
    }

    // 마커 크기: 기본은 지도 기준 고정 배율(지도와 함께 확대/축소).
    // keepScreenSize면 줌이 바뀔 때마다 역보정해 화면 크기를 유지한다.
    void LateUpdate()
    {
        if (mapView == null) return;
        float mapScale = keepScreenSize ? mapView.transform.localScale.x : 1f / markerScale;
        if (Mathf.Approximately(mapScale, lastMapScale)) return;
        lastMapScale = mapScale;

        float inv = 1f / mapScale;
        for (int i = 0; i < items.Count; i++)
        {
            float s = inv * itemScales[i];
            items[i].localScale = new Vector3(s, s, 1f);
        }
    }

    static bool UsesNameTag(MarkerType type) =>
        type == MarkerType.ExtractPmc || type == MarkerType.ExtractScav || type == MarkerType.ExtractShared ||
        type == MarkerType.Transit;

    RectTransform CreateNameTag(MapMarker marker, Style style, RectTransform parent)
    {
        GameObject go = Instantiate(nameTagPrefab, parent, false);
        go.name = $"{marker.type}_{marker.name}";

        TMP_Text label = go.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = marker.name;

        Transform icon = go.transform.Find(nameTagIconPath);
        if (icon != null && icon.TryGetComponent(out Image iconImage)) iconImage.color = style.color;

        return (RectTransform)go.transform;
    }

    RectTransform CreateDot(MapMarker marker, Style style, RectTransform parent)
    {
        var go = new GameObject($"{marker.type}_{marker.name}", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = new Vector2(style.size, style.size);

        var img = go.GetComponent<Image>();
        img.sprite = style.sprite != null ? style.sprite : dotSprite;
        img.color = style.color;
        img.preserveAspect = true;
        img.raycastTarget = style.showInfo;
        if (style.showInfo)
        {
            var info = go.AddComponent<MapMarkerInfo>();
            info.marker = marker;
            info.quest = GetQuest(marker.key);
        }
        return rt;
    }

    Style GetStyle(MarkerType type)
    {
        Style style = styles.Find(s => s.type == type);
        if (style == null)
        {
            style = new Style { type = type };
            styles.Add(style);
        }
        return style;
    }

    // 타입별 그룹 오브젝트 (타입 단위로 켜고 끄기 위함)
    RectTransform GetGroup(MarkerType type, Style style)
    {
        if (groups.TryGetValue(type, out RectTransform group)) return group;

        var go = new GameObject(type.ToString(), typeof(RectTransform));
        group = (RectTransform)go.transform;
        group.SetParent(transform, false);
        group.anchorMin = Vector2.zero;
        group.anchorMax = Vector2.one;
        group.offsetMin = group.offsetMax = Vector2.zero;
        go.SetActive(style.visible);
        EditorPreview.MarkDontSave(go);

        groups.Add(type, group);
        return group;
    }

    void Clear()
    {
        foreach (RectTransform group in groups.Values)
            if (group != null) EditorPreview.Destroy(group.gameObject);
        ResetLists();
        EditorPreview.ClearLeftovers(transform);
    }

    void ResetLists()
    {
        groups.Clear();
        items.Clear();
        itemScales.Clear();
        keyItems.Clear();
        quests.Clear();
        placed.Clear();
        IsBuilt = false;
    }
}
