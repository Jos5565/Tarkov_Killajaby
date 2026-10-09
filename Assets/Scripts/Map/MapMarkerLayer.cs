using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
    [Tooltip("이름표 호버 감지용 지도 Viewport. 비우면 씬에서 찾는다")]
    public MapZoomPan zoomPan;

    [Header("Prefabs")]
    public GameObject nameTagPrefab;
    public Sprite dotSprite;
    [Tooltip("NameTag 표시 배율 (화면 기준)")]
    public float nameTagScale = 0.5f;
    [Tooltip("마커 배율. 지도 전체가 referenceViewSize에 맞춰 보일 때 마커가 화면에서 이 배율로 보인다. 마커는 지도와 함께 확대/축소된다")]
    public float markerScale = 1f;
    [Tooltip("마커 크기 기준이 되는 화면 영역(Canvas px). 맵 이미지 크기가 달라도 처음 화면에서 마커가 비슷한 크기로 보이게 맞춘다")]
    public Vector2 referenceViewSize = new Vector2(1480f, 1000f);
    [Tooltip("켜면 지도를 확대해도 마커가 화면에서 같은 크기로 유지된다 (예전 방식)")]
    public bool keepScreenSize = false;
    [Tooltip("NameTag 프리팹 안에서 색을 바꿀 아이콘 Image 경로")]
    public string nameTagIconPath = "Icon";
    [Tooltip("같은 종류 탈출구가 이 거리(m, 수평) 안에 모여 있으면 이름표 하나로 합친다. 0이면 합치지 않음")]
    public float nameTagMergeDistance = 25f;

    [Header("Transit Label")]
    [Tooltip("트랜짓 이름표 \"리저브로 이동\" → \"리저브 이동\" (이동은 작고 흐리게)")]
    public bool shortTransitLabel = true;
    public string transitSuffix = "이동";
    public Color transitSuffixColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    [Range(0.3f, 1f)]
    [Tooltip("이름 대비 '이동' 글자 크기")]
    public float transitSuffixScale = 0.7f;

    [Header("Styles")]
    public List<Style> styles = new List<Style>();

    readonly Dictionary<MarkerType, RectTransform> groups = new Dictionary<MarkerType, RectTransform>();
    readonly List<RectTransform> items = new List<RectTransform>();
    readonly List<float> itemScales = new List<float>();
    readonly Dictionary<string, List<GameObject>> keyItems = new Dictionary<string, List<GameObject>>();
    readonly HashSet<string> visibleKeys = new HashSet<string>();   // Play 중 켜진 퀘스트 (저장/불러오기)
    readonly List<KeyInfo> quests = new List<KeyInfo>();
    readonly List<MapMarker> placed = new List<MapMarker>();
    readonly List<(RectTransform rt, MapMarker marker)> nameTags = new List<(RectTransform, MapMarker)>();
    MapUserSettings settings;   // Play 중에만 사용. 편집 모드 미리보기는 전부 표시
    RectTransform hoverLayer;   // 마우스를 올린 마커를 잠시 옮겨 두는 맨 위 묶음
    (RectTransform rt, Transform parent, int index) lifted;
    bool dropPending;
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
        if (hoverLayer != null) EditorPreview.DestroyLater(hoverLayer.gameObject);
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
        CreateHoverLayer();

        int skipped = 0;
        foreach (MapMarker marker in MergeNameTags(data.markers))
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
            if (isNameTag) nameTags.Add((rt, marker));

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

    // 화면 좌표 아래에 있는 이름표(탈출구/트랜짓). 숨겨진 종류는 제외, 나중에 그려진(위에 있는) 것 우선.
    // 이름표는 마우스를 받지 않으므로(아래 퀘스트 툴팁용) 클릭은 이렇게 직접 찾는다.
    public bool TryGetNameTagAt(Vector2 screenPosition, Camera eventCamera, out MapMarker marker)
    {
        for (int i = nameTags.Count - 1; i >= 0; i--)
        {
            var (rt, m) = nameTags[i];
            if (rt == null || !rt.gameObject.activeInHierarchy) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rt, screenPosition, eventCamera)) continue;
            marker = m;
            return true;
        }
        marker = null;
        return false;
    }

    // 배치된 이름표 오브젝트 (없으면 null)
    public RectTransform GetNameTag(MapMarker marker) => nameTags.Find(n => n.marker == marker).rt;

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

    // 필터 패널 그룹 펼침 상태 (맵별 저장). Play 중 저장된 적 없으면 접힘, 편집 모드는 fallback(Inspector 값)
    public bool IsGroupExpanded(string title, bool fallback)
    {
        if (!Application.isPlaying) return fallback;
        EnsureSettings();
        return settings.expandedGroups.Contains(title);
    }

    public void SetGroupExpanded(string title, bool expanded)
    {
        if (!Application.isPlaying) return;
        EnsureSettings();
        settings.expandedGroups.Remove(title);
        if (expanded) settings.expandedGroups.Add(title);
        SaveSettings();
    }

    // 이 종류의 마커가 지도에 하나라도 있는지 (필터 패널에서 이 맵에 없는 항목 숨김용)
    public bool HasPlaced(MarkerType type) => placed.Exists(m => m.type == type);

    // ---- 마우스를 올린 마커를 맨 위로 ----

    // 마커를 다른 마커들 위에 그린다. 한 번에 하나만 (이전 것은 제자리로)
    public void Lift(RectTransform marker)
    {
        if (hoverLayer == null || marker == null || marker.parent == hoverLayer) return;
        Drop();
        lifted = (marker, marker.parent, marker.GetSiblingIndex());
        marker.SetParent(hoverLayer, false);   // 그룹과 hoverLayer는 크기·위치가 같아 좌표가 그대로 유지된다
    }

    // 원래 그룹, 원래 순서로 되돌린다
    public void Drop(RectTransform marker = null)
    {
        if (lifted.rt == null || (marker != null && marker != lifted.rt)) return;
        if (lifted.parent != null)
        {
            lifted.rt.SetParent(lifted.parent, false);
            lifted.rt.SetSiblingIndex(lifted.index);
        }
        lifted = default;
    }

    // 이름표는 마우스를 받지 않으므로(아래 퀘스트 툴팁용) 커서 위치로 직접 찾아 올린다.
    // 툴팁이 있는 마커(퀘스트, 보스, 문서)에 마우스가 올라가 있으면 그쪽이 우선
    void UpdateNameTagHover()
    {
        bool liftedIsTag = lifted.rt != null && nameTags.Exists(n => n.rt == lifted.rt);
        if (lifted.rt != null && !liftedIsTag) return;

        if (zoomPan == null) zoomPan = FindAnyObjectByType<MapZoomPan>();
        RectTransform hit = null;
        if (zoomPan != null && zoomPan.IsPointerInside && Mouse.current != null)
        {
            Vector2 pointer = Mouse.current.position.ReadValue();
            // 이미 올라와 있는 이름표 위면 유지 (겹친 이름표 사이에서 깜빡이지 않게)
            if (liftedIsTag && RectTransformUtility.RectangleContainsScreenPoint(lifted.rt, pointer, zoomPan.EventCamera)) return;
            if (TryGetNameTagAt(pointer, zoomPan.EventCamera, out MapMarker marker)) hit = GetNameTag(marker);
        }

        if (hit == lifted.rt) return;
        if (hit != null) Lift(hit);
        else Drop();
    }

    // OnDisable 안에서는 계층을 바꿀 수 없으므로 다음 LateUpdate에서 되돌린다
    public void DropLater(RectTransform marker)
    {
        if (lifted.rt != null && marker == lifted.rt) dropPending = true;
    }

    void CreateHoverLayer()
    {
        var go = new GameObject("Hover", typeof(RectTransform));
        hoverLayer = (RectTransform)go.transform;
        hoverLayer.SetParent(transform, false);
        hoverLayer.anchorMin = Vector2.zero;
        hoverLayer.anchorMax = Vector2.one;
        hoverLayer.offsetMin = hoverLayer.offsetMax = Vector2.zero;
        hoverLayer.SetAsLastSibling();
        EditorPreview.MarkDontSave(go);
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
        if (dropPending)
        {
            dropPending = false;
            Drop();
        }
        if (Application.isPlaying) UpdateNameTagHover();

        if (mapView == null) return;
        float mapScale = keepScreenSize ? mapView.transform.localScale.x : ReferenceFitScale() / markerScale;
        if (Mathf.Approximately(mapScale, lastMapScale)) return;
        lastMapScale = mapScale;

        float inv = 1f / mapScale;
        for (int i = 0; i < items.Count; i++)
        {
            float s = inv * itemScales[i];
            items[i].localScale = new Vector3(s, s, 1f);
        }
    }

    // 이 맵 이미지가 referenceViewSize 안에 꽉 차게 보일 때의 배율.
    // PMC/Scav처럼 실제 화면 폭이 달라도 바뀌지 않아서 지도 대비 마커 크기가 항상 같다.
    float ReferenceFitScale()
    {
        Vector2 size = ((RectTransform)mapView.transform).sizeDelta;
        if (size.x <= 0f || size.y <= 0f) return 1f;
        return Mathf.Min(referenceViewSize.x / size.x, referenceViewSize.y / size.y);
    }

    // 가까이 겹친 같은 종류 이름표(탈출구)를 하나로: 위치는 평균, 이름은 "A / B / C"
    // 예) 해안선 Climber's Trail / Rock Passage / Cliff Descent (서로 20m 이내)
    List<MapMarker> MergeNameTags(List<MapMarker> source)
    {
        var result = new List<MapMarker>(source.Count);
        var merged = new bool[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            if (merged[i]) continue;
            MapMarker a = source[i];
            if (!UsesNameTag(a.type) || nameTagMergeDistance <= 0f)
            {
                result.Add(a);
                continue;
            }

            var group = new List<MapMarker> { a };
            for (int j = i + 1; j < source.Count; j++)
            {
                MapMarker b = source[j];
                if (merged[j] || b.type != a.type) continue;
                Vector2 d = new Vector2(a.position.x - b.position.x, a.position.z - b.position.z);
                if (d.magnitude > nameTagMergeDistance) continue;
                group.Add(b);
                merged[j] = true;
            }
            if (group.Count == 1)
            {
                result.Add(a);
                continue;
            }

            Vector3 center = Vector3.zero;
            var names = new List<string>();
            foreach (MapMarker m in group)
            {
                center += m.position;
                if (!names.Contains(m.name)) names.Add(m.name);
            }
            result.Add(new MapMarker
            {
                type = a.type,
                name = string.Join(" / ", names),
                detail = a.detail,
                position = center / group.Count,
            });
        }
        return result;
    }

    public static bool UsesNameTag(MarkerType type) =>
        type == MarkerType.ExtractPmc || type == MarkerType.ExtractScav || type == MarkerType.ExtractShared ||
        type == MarkerType.Transit;

    RectTransform CreateNameTag(MapMarker marker, Style style, RectTransform parent)
    {
        GameObject go = Instantiate(nameTagPrefab, parent, false);
        go.name = $"{marker.type}_{marker.name}";

        TMP_Text label = go.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = marker.type == MarkerType.Transit && shortTransitLabel ? TransitLabel(marker.name) : marker.name;

        Transform icon = go.transform.Find(nameTagIconPath);
        if (icon != null && icon.TryGetComponent(out Image iconImage)) iconImage.color = style.color;

        // 이름표는 마우스를 받지 않는다: 아래에 가려진 퀘스트 마커에도 마우스가 닿아 툴팁이 뜨도록
        foreach (Graphic graphic in go.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        return (RectTransform)go.transform;
    }

    // "리저브로 이동", "해안선으로 지역이동" → "리저브 <작은 회색>이동</>". 형식이 다르면 그대로 둔다
    static readonly Regex TransitPattern = new Regex(@"^(?<map>.+?)\??\s*(으로|로)\s*(지역)?\s*이동$");

    string TransitLabel(string name)
    {
        string color = ColorUtility.ToHtmlStringRGBA(transitSuffixColor);
        string suffix = $"<size={transitSuffixScale * 100f:0}%><color=#{color}>{transitSuffix}</color></size>";
        var parts = name.Split(new[] { " / " }, StringSplitOptions.None);   // 합쳐진 이름표 "A / B"
        for (int i = 0; i < parts.Length; i++)
        {
            Match m = TransitPattern.Match(parts[i].Trim());
            parts[i] = m.Success ? $"<noparse>{m.Groups["map"].Value}</noparse> {suffix}" : $"<noparse>{parts[i]}</noparse>";
        }
        return string.Join(" / ", parts);
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
            info.layer = this;
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
        if (hoverLayer != null) EditorPreview.Destroy(hoverLayer.gameObject);
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
        nameTags.Clear();
        hoverLayer = null;
        lifted = default;
        dropPending = false;
        IsBuilt = false;
    }
}
