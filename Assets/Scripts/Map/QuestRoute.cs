using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 켜져 있는 퀘스트들을 내 위치(WhereIAM)에서 출발해 도는 최단 루트(직선거리)를 그린다.
// 마지막 퀘스트 지점에서 끝난다. MapContent/Markers 아래(ApiMarkers와 WhereIAM 사이)에 둔다.
//   - 탈출구/트랜짓 이름표를 클릭하면 그곳을 도착점으로 지정 (마지막 목표 → 탈출구까지 포함해 순서 계산)
//     같은 이름표를 다시 클릭하면 해제. 맵별로 저장된다.
//   - 한 목표에 위치가 여러 개면(퀘스트 아이템 후보 등) 그중 루트상 가장 유리한 한 곳만 방문
//   - 직접 완료 체크한 목표(QuestProgress)는 빼고 계산
//   - 도착 탈출구에 탈출 전 행동(레버, 열쇠 문 등, MapMarker.steps)이 있으면 그 위치들을 순서대로 초록 선과 초록 번호로 그린다
//     퀘스트 경로(주황)는 첫 행동 위치에서 끝나고, 거기서부터 행동1 → 행동2 → … → 탈출구가 초록이다.
//     켜진 퀘스트가 없거나 [경로]가 꺼져 있으면 내 위치에서 바로 초록으로 시작한다. 도착 탈출구를 고르지 않으면 그리지 않는다
//   - 위치가 없으면(WhereIAM 미설정) 출발점 없이 가장 짧은 순서
// Play 중에만 동작한다 (편집 모드에서는 퀘스트가 전부 켜져 있어 루트가 의미 없음).
[RequireComponent(typeof(RectTransform))]
public class QuestRoute : MonoBehaviour
{
    public MapView mapView;
    public MapMarkerLayer markerLayer;
    public WhereIAM whereIAM;
    [Tooltip("이름표 클릭을 받을 지도 Viewport. 비우면 씬에서 찾는다")]
    public MapZoomPan zoomPan;

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
    [Tooltip("도착으로 지정한 이름표 배경 색")]
    public Color destinationTagColor = new Color(0.95f, 0.45f, 0.1f, 0.95f);

    [Header("Extract Steps (도착 탈출구의 탈출 전 행동)")]
    public Color stepLineColor = new Color(0.25f, 0.85f, 0.45f, 0.9f);
    public Color stepBadgeColor = new Color(0.2f, 0.72f, 0.38f);
    [Tooltip("번호 옆 설명 글자 크기 (0이면 설명 없이 번호만)")]
    public float stepCaptionSize = 13f;
    public Color stepCaptionBackground = new Color(0.05f, 0.08f, 0.06f, 0.85f);

    [Header("Solver")]
    [Tooltip("목표 수가 이 이하이면 정확한 최단 경로, 넘으면 근사 계산")]
    public int exactLimit = 12;

    public float RouteLength { get; private set; }   // 게임 단위(m)
    public int StopCount { get; private set; }

    // 경로 1번 목표 (내 위치에서 경로상 가장 먼저 가는 곳). 없으면 null
    public MapMarker FirstStop { get; private set; }
    // 경로를 다시 계산할 때마다 (켜고 끄기, 위치 갱신, 도착 탈출구 변경)
    public event System.Action Recalculated;

    // 도착점으로 지정한 탈출구/트랜짓 (없으면 null)
    public MapMarker Destination { get; private set; }

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

    // 도착 탈출구 이름표를 클릭으로 지정/해제한다 (같은 것을 다시 고르면 해제)
    public void ToggleDestination(MapMarker marker)
    {
        if (markerLayer == null || markerLayer.Settings == null) return;
        bool same = marker != null && Destination != null && marker.name == Destination.name;
        markerLayer.Settings.routeEnd = same || marker == null ? "" : marker.name;
        markerLayer.SaveSettings();
        dirty = true;
    }

    UILineGraphic line;
    UILineGraphic stepLine;   // 탈출 전 행동 경로 (초록)
    Graphic destinationTag;   // 이름표 배경 (RoundImage)
    Color destinationTagOriginal;
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
        if (zoomPan == null) zoomPan = FindAnyObjectByType<MapZoomPan>();
        if (zoomPan != null) zoomPan.Clicking += OnMapClicking;
        QuestProgress.Changed += OnProgressChanged;
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
        if (zoomPan != null) zoomPan.Clicking -= OnMapClicking;
        QuestProgress.Changed -= OnProgressChanged;
    }

    // 지도 클릭이 이름표 위면 도착점 지정/해제. 이름표 위 더블클릭은 화면 맞춤도 하지 않는다.
    bool OnMapClicking(UnityEngine.EventSystems.PointerEventData e)
    {
        if (markerLayer == null || !markerLayer.TryGetNameTagAt(e.position, e.pressEventCamera, out MapMarker marker)) return false;
        if (e.clickCount == 1) ToggleDestination(marker);
        return true;
    }

    void MarkDirty() => dirty = true;
    void OnKeyChanged(string key, bool visible) => dirty = true;
    void OnTypeChanged(MarkerType type, bool visible) => dirty = true;
    void OnPoseChanged(WhereIAM w) => dirty = true;
    void OnProgressChanged(string key) => dirty = true;

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
        stepLine.SetThickness(lineWidth * inv);
        foreach (var (rt, local) in badges)
        {
            rt.localScale = new Vector3(inv, inv, 1f);
            rt.anchoredPosition = local + badgeOffset * inv;
        }
    }

    public void Recalculate()
    {
        ClearBadges();
        UpdateDestination();
        RouteLength = 0f;
        StopCount = 0;
        FirstStop = null;
        line.SetPoints(System.Array.Empty<Vector2>());
        stepLine.SetPoints(System.Array.Empty<Vector2>());
        if (markerLayer == null || !markerLayer.IsBuilt)
        {
            Recalculated?.Invoke();
            return;
        }

        Vector2? start = whereIAM != null && whereIAM.HasPosition ? Ground(whereIAM.GamePosition) : (Vector2?)null;
        // 도착 탈출구의 탈출 전 행동 (도착을 고른 경우에만)
        List<ExtractStep> steps = Destination != null && Destination.steps != null ? Destination.steps : new List<ExtractStep>();

        // 스캐브는 [경로] 버튼이 꺼져 있으니(퀘스트 없음) 도착 탈출구까지의 선만 그린다
        bool enabled = RouteEnabled || !markerLayer.QuestsAvailable;
        if (enabled) DrawQuestRoute(start, steps);
        if (steps.Count > 0) DrawSteps(start, steps);

        if (StopCount > 0 || Destination != null)
            Debug.Log($"[QuestRoute] 목표 {StopCount}곳{(Destination != null ? $" → {Destination.name}" : "")}" +
                      $"{(steps.Count > 0 ? $" (탈출 전 행동 {steps.Count}곳)" : "")}, 약 {RouteLength:0}m (직선거리)");
        Recalculated?.Invoke();
    }

    // 퀘스트 경로(주황): 내 위치 → 켜진 퀘스트 목표들 → 첫 행동 위치(없으면 도착 탈출구)
    void DrawQuestRoute(Vector2? start, List<ExtractStep> steps)
    {
        // 켜진 퀘스트의 목표들 (같은 목표의 위치들은 한 cluster)
        var clusters = new List<List<MapMarker>>();
        var byObjective = new Dictionary<string, List<MapMarker>>();
        foreach (MapMarker m in markerLayer.Placed)
        {
            if (m.type != MarkerType.Quest && m.type != MarkerType.QuestItem) continue;
            if (string.IsNullOrEmpty(m.key) || !markerLayer.IsKeyVisible(m.key) || !markerLayer.IsVisible(m.type)) continue;
            if (QuestProgress.IsDone(m.key, m.objective)) continue;

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

        Vector3? endPos = steps.Count > 0 ? steps[0].position : Destination != null ? Destination.position : (Vector3?)null;
        var points = clusters.Select(c => (IReadOnlyList<Vector2>)c.Select(m => Ground(m.position)).ToList()).ToList();
        Vector2? end = endPos.HasValue ? Ground(endPos.Value) : (Vector2?)null;
        RouteSolver.Result result = RouteSolver.Solve(points, start, end, exactLimit);

        RouteLength = result.length;
        StopCount = result.clusterOrder.Length;
        FirstStop = StopCount > 0 ? clusters[result.clusterOrder[0]][result.pointIndex[0]] : null;
        // 행동 단계가 있는데 들를 퀘스트가 없으면 주황 선 없이 초록 선이 내 위치에서 시작한다
        if (StopCount == 0 && steps.Count > 0) return;

        // 선: 출발점 → 1 → 2 → ... → 마지막 목표 → (첫 행동 위치 또는 도착 탈출구)
        var path = new List<Vector2>();
        if (start.HasValue) path.Add(mapView.GameToLocal(whereIAM.GamePosition));
        var stops = new List<Vector3>();
        for (int i = 0; i < result.clusterOrder.Length; i++)
        {
            Vector3 pos = clusters[result.clusterOrder[i]][result.pointIndex[i]].position;
            stops.Add(pos);
            path.Add(mapView.GameToLocal(pos));
        }
        if (endPos.HasValue) path.Add(mapView.GameToLocal(endPos.Value));
        line.SetPoints(path.Count >= 2 ? path : new List<Vector2>());

        // 번호: 같은 자리에 여러 순서가 겹치면 "3·4"처럼 하나로
        var labels = new List<(Vector3 pos, string text)>();
        for (int i = 0; i < stops.Count; i++)
        {
            int same = labels.FindIndex(l => (l.pos - stops[i]).sqrMagnitude < 1f);
            if (same >= 0) labels[same] = (labels[same].pos, labels[same].text + "·" + (i + 1));
            else labels.Add((stops[i], (i + 1).ToString()));
        }
        foreach (var (pos, text) in labels) CreateBadge(mapView.GameToLocal(pos), text, badgeColor, null);
    }

    // 탈출 전 행동 경로(초록): (퀘스트가 없으면 내 위치 →) 행동1 → 행동2 → … → 도착 탈출구, 번호와 설명
    void DrawSteps(Vector2? start, List<ExtractStep> steps)
    {
        var path = new List<Vector2>();
        if (StopCount == 0 && start.HasValue) path.Add(mapView.GameToLocal(whereIAM.GamePosition));
        foreach (ExtractStep s in steps) path.Add(mapView.GameToLocal(s.position));
        path.Add(mapView.GameToLocal(Destination.position));
        stepLine.SetPoints(path.Count >= 2 ? path : new List<Vector2>());

        for (int i = 0; i < steps.Count; i++)
            CreateBadge(mapView.GameToLocal(steps[i].position), (i + 1).ToString(), stepBadgeColor, steps[i].text);
    }

    // 저장된 도착 이름표 이름 → 지금 지도에 있는 이름표. 진영이 바뀌어 없는 탈출구면 지정하지 않는다.
    void UpdateDestination()
    {
        if (destinationTag != null) destinationTag.color = destinationTagOriginal;
        destinationTag = null;
        Destination = null;

        string endName = markerLayer != null && markerLayer.Settings != null ? markerLayer.Settings.routeEnd : null;
        if (string.IsNullOrEmpty(endName) || !markerLayer.IsBuilt) return;

        Destination = markerLayer.Placed.FirstOrDefault(m => MapMarkerLayer.UsesNameTag(m.type) && m.name == endName && markerLayer.IsVisible(m.type));
        if (Destination == null) return;

        RectTransform tag = markerLayer.GetNameTag(Destination);
        if (tag != null && tag.TryGetComponent(out destinationTag))
        {
            destinationTagOriginal = destinationTag.color;
            destinationTag.color = destinationTagColor;
        }
    }

    static Vector2 Ground(Vector3 p) => new Vector2(p.x, p.z);

    // ---- UI ----

    void CreateLine()
    {
        if (line != null) return;
        line = MakeLine("RouteLine", lineColor);
        stepLine = MakeLine("ExtractStepLine", stepLineColor);   // 나중에 만들어 주황 선 위에 그린다
    }

    UILineGraphic MakeLine(string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UILineGraphic));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);   // 좌표 원점 = 지도 중심 (GameToLocal과 같음)
        rt.sizeDelta = Vector2.zero;
        var graphic = go.GetComponent<UILineGraphic>();
        graphic.color = color;
        graphic.raycastTarget = false;
        return graphic;
    }

    // 순서 번호. caption이 있으면 번호 오른쪽에 어두운 바탕의 설명을 붙인다 (탈출 전 행동)
    void CreateBadge(Vector2 local, string text, Color color, string caption)
    {
        var go = new GameObject("Stop " + text, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(Mathf.Max(badgeSize, badgeSize * 0.45f * text.Length + 8f), badgeSize);

        var img = go.GetComponent<Image>();
        img.sprite = badgeSprite;
        img.type = Image.Type.Sliced;
        img.color = color;
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

        if (!string.IsNullOrEmpty(caption) && stepCaptionSize > 0f) CreateCaption(rt, caption);
        badges.Add((rt, local));
    }

    // 번호 오른쪽 설명: [어두운 바탕 + 글자], 번호와 함께 화면 크기 유지
    void CreateCaption(RectTransform badge, string caption)
    {
        var go = new GameObject("Caption", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var rt = (RectTransform)go.transform;
        rt.SetParent(badge, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(4f, 0f);
        var bg = go.GetComponent<Image>();
        bg.color = stepCaptionBackground;
        bg.raycastTarget = false;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 2, 3);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(rt, false);
        var label = textGo.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = $"<noparse>{caption}</noparse>";
        label.fontSize = stepCaptionSize;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
    }

    void ClearBadges()
    {
        foreach (var (rt, _) in badges)
            if (rt != null) Destroy(rt.gameObject);
        badges.Clear();
    }
}
