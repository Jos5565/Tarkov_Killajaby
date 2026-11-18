using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 지도 위쪽 가운데 보스 목록: 이 맵에 나올 수 있는 보스와 직접 남긴 상태 (BossMarks).
//   보스  [르샬라 75% · 미확인] [Knight 25% · 출현] [광신도 사제 30% · 처치]
//   - 클릭: 미확인 → 출현 → 처치 → 없음 (게임에서 읽는 정보가 아니라 메모. 새 레이드에서 지워진다)
//   - 마우스를 올리면 출현 구역 수, 누가 언제 표시했는지
//   - 파티 버전이면 파티원 표시도 보인다 (같은 보스는 가장 최근 표시)
// 보스와 확률은 지도의 보스 마커(MapMarkerLayer, 설명 "르샬라 75%, Knight 25% (구역)")에서 모은다.
// 지도 씬이 열리면 스스로 캔버스에 붙는다 (씬 수정 없음). Play 중에만 동작한다.
public class BossStatusPanel : MonoBehaviour
{
    public float fontSize = 15f;
    public float topMargin = 14f;

    static readonly Color Background = new Color(0.09f, 0.09f, 0.1f, 0.88f);
    static readonly Color TitleColor = new Color(0.8f, 0.8f, 0.8f);
    static readonly Color DimText = new Color(0.6f, 0.6f, 0.6f);
    static readonly Regex Entry = new Regex(@"^\s*(.+?)\s+(\d+)%");

    class Boss
    {
        public string name;
        public int chance;
        public int zones;
        public uint key;
        public Image background;
        public TMP_Text label;
    }

    MapMarkerLayer markerLayer;
    TMP_FontAsset font;
    RectTransform panel;
    readonly List<Boss> bosses = new List<Boss>();
    float nextTick;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var view = FindAnyObjectByType<MapView>();
        if (view == null || FindAnyObjectByType<BossStatusPanel>() != null) return;
        Canvas canvas = view.GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var go = new GameObject(nameof(BossStatusPanel), typeof(RectTransform));
        go.SetActive(false);
        Transform root = canvas.rootCanvas.transform;
        go.transform.SetParent(root, false);
        // 지도 위, 마커 툴팁보다 아래
        go.transform.SetAsLastSibling();
        MapTooltip tooltip = MapTooltip.Instance != null ? MapTooltip.Instance : FindAnyObjectByType<MapTooltip>();
        Transform top = tooltip != null ? tooltip.transform : null;
        while (top != null && top.parent != root) top = top.parent;
        if (top != null) go.transform.SetSiblingIndex(top.GetSiblingIndex());
        go.AddComponent<BossStatusPanel>();
        go.SetActive(true);
    }

    void OnEnable()
    {
        var self = (RectTransform)transform;
        self.anchorMin = Vector2.zero;
        self.anchorMax = Vector2.one;
        self.offsetMin = self.offsetMax = Vector2.zero;

        markerLayer = FindAnyObjectByType<MapMarkerLayer>();
        var route = FindAnyObjectByType<RouteToggleButton>(FindObjectsInactive.Include);
        font = route != null ? route.font : null;
        if (markerLayer != null)
        {
            markerLayer.Built += Rebuild;
            if (markerLayer.IsBuilt) Rebuild();
        }
        BossMarks.Changed += Refresh;
    }

    void OnDisable()
    {
        if (markerLayer != null) markerLayer.Built -= Rebuild;
        BossMarks.Changed -= Refresh;
        if (panel != null) Destroy(panel.gameObject);
        panel = null;
        bosses.Clear();
    }

    string MapName => GameSession.Map != null ? GameSession.Map.normalizedName
        : markerLayer != null && markerLayer.mapView != null && markerLayer.mapView.config != null ? markerLayer.mapView.config.normalizedName : "";

    // 보스 마커 설명 "르샬라 75%, Knight 25% (ZoneScavBase)"에서 보스별 최대 확률과 구역 수
    void Collect()
    {
        bosses.Clear();
        if (markerLayer == null) return;
        string map = MapName;
        var byName = new Dictionary<string, Boss>();
        foreach (MapMarker m in markerLayer.Placed.Where(x => x.type == MarkerType.Boss))
        {
            string detail = m.detail ?? "";
            int paren = detail.LastIndexOf('(');
            if (paren > 0) detail = detail.Substring(0, paren);
            foreach (string part in detail.Split(','))
            {
                Match match = Entry.Match(part);
                if (!match.Success) continue;
                string name = match.Groups[1].Value.Trim();
                int chance = int.Parse(match.Groups[2].Value);
                if (!byName.TryGetValue(name, out Boss b))
                    byName[name] = b = new Boss { name = name, key = BossMarks.Key(map, name) };
                b.chance = Mathf.Max(b.chance, chance);
                b.zones++;
            }
        }
        bosses.AddRange(byName.Values.OrderByDescending(b => b.chance).ThenBy(b => b.name));
    }

    void Rebuild()
    {
        if (panel != null) Destroy(panel.gameObject);
        panel = null;
        Collect();
        if (bosses.Count == 0) return;

        var go = new GameObject("BossPanel", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        panel = (RectTransform)go.transform;
        panel.SetParent(transform, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -topMargin);
        go.GetComponent<Image>().color = Background;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 6, 6);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text(panel, "보스", fontSize, FontStyles.Bold, TitleColor);
        foreach (Boss b in bosses) BuildChip(b);
        Refresh();
    }

    void BuildChip(Boss b)
    {
        var go = new GameObject(b.name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(panel, false);
        b.background = go.GetComponent<Image>();
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 3, 4);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        b.label = Text(rt, "", fontSize, FontStyles.Normal, Color.white);

        var click = go.AddComponent<MarkerFilterClick>();
        click.onClick = () => BossMarks.Cycle(b.key);
        var hover = go.AddComponent<PointerHover>();
        hover.enter = e => ShowTooltip(b, e.position);
        hover.move = e => { if (MapTooltip.Instance != null) MapTooltip.Instance.Move(e.position); };
        hover.exit = _ => { if (MapTooltip.Instance != null) MapTooltip.Instance.Hide(); };
    }

    // 상태에 따라 색과 글자
    void Refresh()
    {
        foreach (Boss b in bosses)
        {
            if (b.label == null) continue;
            BossMarks.Status s = BossMarks.Current(b.key).status;
            string state = BossMarks.Label(s);
            b.label.text = s == BossMarks.Status.Absent
                ? $"<s><noparse>{b.name}</noparse></s> <size=85%>{b.chance}% · {state}</size>"
                : $"<noparse>{b.name}</noparse> <size=85%>{b.chance}% · {state}</size>";
            (Color bg, Color fg) = s switch
            {
                BossMarks.Status.Seen => (new Color(0.62f, 0.2f, 0.16f, 0.95f), Color.white),            // 출현: 붉은색 (조심)
                BossMarks.Status.Killed => (new Color(0.2f, 0.45f, 0.24f, 0.95f), Color.white),          // 처치: 초록
                BossMarks.Status.Absent => (new Color(0.16f, 0.16f, 0.17f, 0.95f), DimText),            // 없음: 흐리게 + 취소선
                _ => (new Color(0.24f, 0.24f, 0.26f, 0.95f), new Color(0.9f, 0.9f, 0.9f)),             // 미확인
            };
            b.background.color = bg;
            b.label.color = fg;
        }
    }

    void ShowTooltip(Boss b, Vector2 position)
    {
        if (MapTooltip.Instance == null) return;
        BossMarks.Mark mark = BossMarks.Current(b.key);
        string dim = ColorUtility.ToHtmlStringRGB(DimText);
        string body = "";
        if (mark.status != BossMarks.Status.None)
        {
            int minutes = Mathf.FloorToInt(Mathf.Max(0f, Time.realtimeSinceStartup - mark.time) / 60f);
            string who = string.IsNullOrEmpty(mark.by) ? "나" : $"<noparse>{mark.by}</noparse>";
            body = $"<b>{BossMarks.Label(mark.status)}</b> <color=#{dim}>· {who} · {(minutes < 1 ? "방금" : $"{minutes}분 전")}</color>\n\n";
        }
        body += $"<color=#{dim}><size=85%>클릭: 미확인 → 출현 → 처치 → 없음\n게임에서 읽는 정보가 아니라 직접 남기는 메모예요. 새 레이드가 시작되면 지워져요.</size></color>";
        MapTooltip.Instance.Show($"<noparse>{b.name}</noparse>", $"출현 확률 최대 {b.chance}% · 구역 {b.zones}곳", body, position);
    }

    void Update()
    {
        // "N분 전" 등은 툴팁을 다시 열 때 갱신되고, 파티원 표시가 바뀌면 BossMarks.Changed로 갱신된다
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + 5f;
        Refresh();
    }

    TMP_Text Text(RectTransform parent, string text, float size, FontStyles style, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.richText = true;
        label.raycastTarget = false;
        return label;
    }
}
