using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 켜져 있는 퀘스트들을 내 위치(WhereIAM)에서 출발해 도는 최단 루트(직선거리)를 그린다.
// 마지막 퀘스트 지점에서 끝난다. MapContent/Markers 아래(ApiMarkers와 WhereIAM 사이)에 둔다.
//   - 한 목표에 위치가 여러 개면(퀘스트 아이템 후보 등) 그중 루트상 가장 유리한 한 곳만 방문
//   - 위치가 없으면(WhereIAM 미설정) 출발점 없이 가장 짧은 순서
// Play 중에만 동작한다 (편집 모드에서는 퀘스트가 전부 켜져 있어 루트가 의미 없음).
[RequireComponent(typeof(RectTransform))]
public class QuestRoute : MonoBehaviour
{
    public MapView mapView;
    public MapMarkerLayer markerLayer;
    public WhereIAM whereIAM;

    [Header("Look")]
    public TMP_FontAsset font;
    public Sprite badgeSprite;
    public Color lineColor = new Color(0.95f, 0.45f, 0.1f, 0.85f);
    [Tooltip("선 두께 (화면 px, 확대해도 유지)")]
    public float lineWidth = 4f;
    public Color badgeColor = new Color(0.95f, 0.45f, 0.1f);
    public Color badgeTextColor = Color.white;
    public float badgeSize = 22f;
    [Tooltip("순서 번호를 마커 오른쪽 위로 비켜 놓는 거리 (화면 px)")]
    public Vector2 badgeOffset = new Vector2(14f, 14f);

    [Header("Solver")]
    [Tooltip("목표 수가 이 이하이면 정확한 최단 경로, 넘으면 근사 계산")]
    public int exactLimit = 12;

    public float RouteLength { get; private set; }   // 게임 단위(m)
    public int StopCount { get; private set; }

    // [경로] 버튼으로 전체 켜기/끄기. 맵별로 저장된다.
    public bool RouteEnabled => markerLayer == null || markerLayer.Settings == null || markerLayer.Settings.routeEnabled;
    public event System.Action<bool> RouteEnabledChanged;

    public void SetRouteEnabled(bool enabled)
    {
        if (markerLayer == null || markerLayer.Settings == null || RouteEnabled == enabled) return;
        markerLayer.Settings.routeEnabled = enabled;
        markerLayer.SaveSettings();
        dirty = true;
        RouteEnabledChanged?.Invoke(enabled);
    }

    UILineGraphic line;
    readonly List<(RectTransform rt, Vector2 local)> badges = new List<(RectTransform, Vector2)>();
    bool dirty = true;
    float lastScale = -1f;

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        CreateLine();
        if (markerLayer != null)
        {
            markerLayer.Built += MarkDirty;
            markerLayer.KeyVisibilityChanged += OnKeyChanged;
            markerLayer.VisibilityChanged += OnTypeChanged;
        }
        if (whereIAM != null) whereIAM.PoseChanged += OnPoseChanged;
        dirty = true;
    }

    void OnDisable()
    {
        if (markerLayer != null)
        {
            markerLayer.Built -= MarkDirty;
            markerLayer.KeyVisibilityChanged -= OnKeyChanged;
            markerLayer.VisibilityChanged -= OnTypeChanged;
        }
        if (whereIAM != null) whereIAM.PoseChanged -= OnPoseChanged;
    }

    void MarkDirty() => dirty = true;
    void OnKeyChanged(string key, bool visible) => dirty = true;
    void OnTypeChanged(MarkerType type, bool visible) => dirty = true;
    void OnPoseChanged(WhereIAM w) => dirty = true;

    void LateUpdate()
    {
        if (!Application.isPlaying || mapView == null || !mapView.IsLoaded) return;

        if (dirty)
        {
            dirty = false;
            Recalculate();
            lastScale = -1f;
        }

        // 확대해도 선 두께/번호 크기가 화면에서 같게
        float scale = mapView.transform.localScale.x;
        if (Mathf.Approximately(scale, lastScale)) return;
        lastScale = scale;
        float inv = 1f / scale;

        line.SetThickness(lineWidth * inv);
        foreach (var (rt, local) in badges)
        {
            rt.localScale = new Vector3(inv, inv, 1f);
            rt.anchoredPosition = local + badgeOffset * inv;
        }
    }

    public void Recalculate()
    {
        ClearBadges();
        if (markerLayer == null || !markerLayer.IsBuilt || !RouteEnabled)
        {
            RouteLength = 0f;
            StopCount = 0;
            line.SetPoints(System.Array.Empty<Vector2>());
            return;
        }

        // 켜진 퀘스트의 목표들 (같은 목표의 위치들은 한 cluster)
        var clusters = new List<List<MapMarker>>();
        var byObjective = new Dictionary<string, List<MapMarker>>();
        foreach (MapMarker m in markerLayer.Placed)
        {
            if (m.type != MarkerType.Quest && m.type != MarkerType.QuestItem) continue;
            if (string.IsNullOrEmpty(m.key) || !markerLayer.IsKeyVisible(m.key) || !markerLayer.IsVisible(m.type)) continue;

            if (string.IsNullOrEmpty(m.objective))
            {
                clusters.Add(new List<MapMarker> { m });   // 목표 ID가 없는 예전 데이터: 위치마다 하나
                continue;
            }
            string id = m.key + "/" + m.objective;
            if (!byObjective.TryGetValue(id, out List<MapMarker> list))
            {
                byObjective.Add(id, list = new List<MapMarker>());
                clusters.Add(list);
            }
            if (!list.Any(o => (o.position - m.position).sqrMagnitude < 0.25f)) list.Add(m);   // 같은 자리 중복 제거
        }

        Vector2? start = whereIAM != null && whereIAM.HasPosition ? Ground(whereIAM.GamePosition) : (Vector2?)null;
        var points = clusters.Select(c => (IReadOnlyList<Vector2>)c.Select(m => Ground(m.position)).ToList()).ToList();
        RouteSolver.Result result = RouteSolver.Solve(points, start, exactLimit);

        RouteLength = result.length;
        StopCount = result.clusterOrder.Length;

        // 선: 출발점 → 1 → 2 → ... → 마지막 목표
        var path = new List<Vector2>();
        if (start.HasValue) path.Add(mapView.GameToLocal(whereIAM.GamePosition));
        var stops = new List<Vector3>();
        for (int i = 0; i < result.clusterOrder.Length; i++)
        {
            Vector3 pos = clusters[result.clusterOrder[i]][result.pointIndex[i]].position;
            stops.Add(pos);
            path.Add(mapView.GameToLocal(pos));
        }
        line.SetPoints(path);

        // 번호: 같은 자리에 여러 순서가 겹치면 "3·4"처럼 하나로
        var labels = new List<(Vector3 pos, string text)>();
        for (int i = 0; i < stops.Count; i++)
        {
            int same = labels.FindIndex(l => (l.pos - stops[i]).sqrMagnitude < 1f);
            if (same >= 0) labels[same] = (labels[same].pos, labels[same].text + "·" + (i + 1));
            else labels.Add((stops[i], (i + 1).ToString()));
        }
        foreach (var (pos, text) in labels) CreateBadge(mapView.GameToLocal(pos), text);

        if (StopCount > 0)
            Debug.Log($"[QuestRoute] 목표 {StopCount}곳, 약 {RouteLength:0}m (직선거리)");
    }

    static Vector2 Ground(Vector3 p) => new Vector2(p.x, p.z);

    // ---- UI ----

    void CreateLine()
    {
        if (line != null) return;
        var go = new GameObject("RouteLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(UILineGraphic));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);   // 좌표 원점 = 지도 중심 (GameToLocal과 같음)
        rt.sizeDelta = Vector2.zero;
        line = go.GetComponent<UILineGraphic>();
        line.color = lineColor;
        line.raycastTarget = false;
    }

    void CreateBadge(Vector2 local, string text)
    {
        var go = new GameObject("Stop " + text, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(Mathf.Max(badgeSize, badgeSize * 0.45f * text.Length + 8f), badgeSize);

        var img = go.GetComponent<Image>();
        img.sprite = badgeSprite;
        img.type = Image.Type.Sliced;
        img.color = badgeColor;
        img.raycastTarget = false;

        var labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.SetParent(rt, false);
        SelectButton.Inset(labelRt, 0f, 0f, 0f, 0f);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = badgeSize * 0.6f;
        label.fontStyle = FontStyles.Bold;
        label.color = badgeTextColor;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        badges.Add((rt, local));
    }

    void ClearBadges()
    {
        foreach (var (rt, _) in badges)
            if (rt != null) Destroy(rt.gameObject);
        badges.Clear();
    }
}
