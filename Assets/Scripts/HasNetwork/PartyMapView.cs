using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Member = PartyProtocol.Member;

// 지도 화면의 파티 표시. 파티 버전이면 PartyEdition이 지도 캔버스에 붙인다. 파티에 참가 중일 때만 보인다.
//   왼쪽 아래 패널:
//     파티 3/5                ▼     ← 머리글 클릭: 접기/펴기 (저장)
//     ● 조스 ★     세관  방금        ← 줄 클릭: 그 파티원 위치로 화면 이동 (같은 맵일 때)
//       120m                        ← 같은 맵이고 둘 다 위치가 있으면 직선거리
//     ◌ 친구2      로비
//     [코드 복사] [파티 닫기]         ← 파티원은 [파티 나가기]
//   지도 위: 같은 맵 파티원의 화살표(내 마커와 같은 모양, 파티원 색) + 닉네임. 5분 넘은 위치는 흐리게
[RequireComponent(typeof(RectTransform))]
public class PartyMapView : MonoBehaviour
{
    public float width = 250f;
    public Vector2 margin = new Vector2(16f, 16f);
    public float fontSize = 15f;
    [Tooltip("이보다 오래된 위치는 흐리게 (초)")]
    public float staleSeconds = 300f;

    const string CollapsedPref = "Party.MapPanelCollapsed";

    PartySession session;
    TMP_FontAsset font;
    MapView mapView;
    MapZoomPan zoomPan;
    WhereIAM whereIAM;

    RectTransform panel, list, buttons;
    TMP_Text header;
    bool collapsed;
    float nextTick;
    readonly List<(ushort id, TMP_Text info)> rowLabels = new List<(ushort, TMP_Text)>();

    class PartyMarker
    {
        public RectTransform root, arrow;
        public TMP_Text label;
        public CanvasGroup group;
    }
    readonly Dictionary<ushort, PartyMarker> markers = new Dictionary<ushort, PartyMarker>();

    void OnEnable()
    {
        session = PartySession.Ensure();
        font = PartyUi.FindFont();
        mapView = FindAnyObjectByType<MapView>();
        zoomPan = FindAnyObjectByType<MapZoomPan>();
        whereIAM = FindAnyObjectByType<WhereIAM>(FindObjectsInactive.Include);
        collapsed = PlayerPrefs.GetInt(CollapsedPref, 0) == 1;
        BuildPanel();
        session.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (session != null) session.Changed -= Refresh;
        if (panel != null) Destroy(panel.gameObject);
        panel = null;
        foreach (PartyMarker m in markers.Values)
            if (m.root != null) Destroy(m.root.gameObject);
        markers.Clear();
    }

    // ---- 패널 ----

    void BuildPanel()
    {
        var self = (RectTransform)transform;
        SelectButton.Inset(self, 0f, 0f, 0f, 0f);

        panel = PartyUi.Vertical(transform, "PartyMapPanel", 4f, new RectOffset(10, 10, 8, 10), PartyUi.Background);
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
        panel.anchoredPosition = margin;
        panel.sizeDelta = new Vector2(width, 0f);
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        header = PartyUi.Text(panel, "", font, fontSize, PartyUi.TextColor, FontStyles.Bold);
        header.raycastTarget = true;
        header.gameObject.AddComponent<MarkerFilterClick>().onClick = () =>
        {
            collapsed = !collapsed;
            PlayerPrefs.SetInt(CollapsedPref, collapsed ? 1 : 0);
            PlayerPrefs.Save();
            Refresh();
        };

        list = PartyUi.Vertical(panel, "Members", 2f);
        buttons = PartyUi.Horizontal(panel, "Buttons", 6f);
    }

    void Refresh()
    {
        if (panel == null) return;
        panel.gameObject.SetActive(session.IsInParty);
        if (!session.IsInParty)
        {
            UpdateMarkers();
            return;
        }

        List<Member> sorted = Sorted();
        string dots = collapsed ? "  " + string.Concat(sorted.Select(m => $"<color=#{ColorUtility.ToHtmlStringRGB(PartyUi.MemberColor(m.color))}>{Dot(m)}</color>")) : "";
        header.text = $"파티 {session.Members.Count}/{PartyProtocol.MaxMembers}{dots}  <size=70%><color=#888888>{(collapsed ? "▲" : "▼")}</color></size>";

        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
        for (int i = buttons.childCount - 1; i >= 0; i--) Destroy(buttons.GetChild(i).gameObject);
        rowLabels.Clear();
        list.gameObject.SetActive(!collapsed);
        buttons.gameObject.SetActive(!collapsed);

        if (!collapsed)
        {
            foreach (Member m in sorted) BuildRow(m);
            var size = new Vector2((width - 26f) * 0.5f, 28f);
            if (session.IsHost)
            {
                PartyUi.Button(buttons, "코드 복사", font, size, fontSize - 2f, () =>
                {
                    if (session.InviteCode == null) session.NewInvite();   // 만료됐으면 새로 만들어 복사
                    if (session.InviteCode != null) GUIUtility.systemCopyBuffer = session.InviteCode;
                });
                PartyUi.Button(buttons, "파티 닫기", font, size, fontSize - 2f, session.Leave);
            }
            else PartyUi.Button(buttons, "파티 나가기", font, size, fontSize - 2f, session.Leave);
        }
        UpdateMarkers();
    }

    void BuildRow(Member m)
    {
        RectTransform row = PartyUi.Horizontal(list, m.nickname, 6f, 24f);
        var bg = row.gameObject.AddComponent<Image>();
        bg.color = Color.clear;   // 줄 전체 클릭
        ushort id = m.id;
        row.gameObject.AddComponent<MarkerFilterClick>().onClick = () => CenterOn(id);

        PartyUi.Text(row, Dot(m), font, fontSize, PartyUi.MemberColor(m.color));
        string star = m.isHost ? " <color=#C3B190>★</color>" : "";
        string me = m.id == session.MyId ? " <color=#888888>(나)</color>" : "";
        PartyUi.Text(row, $"<noparse>{m.nickname}</noparse>{star}{me}", font, fontSize, PartyUi.TextColor, flexible: true);
        rowLabels.Add((m.id, PartyUi.Text(row, Info(m), font, fontSize - 2f, PartyUi.SubColor)));
    }

    // ● 같은 맵 / ◌ 로비·다른 맵 / ✕ 연결 끊김
    string Dot(Member m) => !m.connected ? "✕" : SameMap(m) ? "●" : "◌";

    string Info(Member m)
    {
        if (!m.connected) return "연결 끊김";
        if (m.state.status == PartyProtocol.Status.Lobby) return "로비";
        string text = SameMap(m) ? "" : PartyUi.MapName(m.state.map);
        if (m.state.hasPosition)
        {
            text = (text.Length > 0 ? text + " · " : "") + PartyUi.Age(PartySession.PositionAge(m));
            float? d = Distance(m);
            if (d.HasValue && m.id != session.MyId) text += $" · {d.Value:0}m";
        }
        return text.Length > 0 ? text : PartyUi.MapName(m.state.map);
    }

    bool SameMap(Member m) =>
        m.state.status == PartyProtocol.Status.InMap && GameSession.Map != null && m.state.map == GameSession.Map.normalizedName;

    // 나와 같은 맵이고 둘 다 위치가 있으면 직선거리 (층·벽 무시)
    float? Distance(Member m)
    {
        if (!SameMap(m) || !m.state.hasPosition || whereIAM == null || !whereIAM.HasPosition) return null;
        Vector3 me = whereIAM.GamePosition;
        return Vector2.Distance(new Vector2(me.x, me.z), new Vector2(m.state.x, m.state.z));
    }

    // 나 → 같은 맵(가까운 순) → 로비·다른 맵 → 연결 끊김
    List<Member> Sorted() =>
        session.Members
            .OrderBy(m => m.id == session.MyId ? 0 : !m.connected ? 3 : SameMap(m) ? 1 : 2)
            .ThenBy(m => Distance(m) ?? float.MaxValue)
            .ThenBy(m => m.id)
            .ToList();

    void CenterOn(ushort id)
    {
        Member m = session.Members.FirstOrDefault(x => x.id == id);
        if (m == null || !SameMap(m) || !m.state.hasPosition || zoomPan == null) return;
        zoomPan.CenterOn(new Vector3(m.state.x, m.state.y, m.state.z));
    }

    // ---- 지도 위 마커 ----

    void UpdateMarkers()
    {
        var keep = new HashSet<ushort>();
        if (session.IsInParty && mapView != null)
        {
            foreach (Member m in session.Members)
            {
                if (m.id == session.MyId || !SameMap(m) || !m.state.hasPosition) continue;
                keep.Add(m.id);
                if (!markers.TryGetValue(m.id, out PartyMarker marker)) markers[m.id] = marker = CreateMarker();
                ApplyMarker(marker, m);
            }
        }
        foreach (ushort id in markers.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            if (markers[id].root != null) Destroy(markers[id].root.gameObject);
            markers.Remove(id);
        }
    }

    PartyMarker CreateMarker()
    {
        RectTransform parent = mapView.markerRoot != null ? mapView.markerRoot : (RectTransform)mapView.transform;
        var go = new GameObject("PartyMember", typeof(RectTransform), typeof(CanvasGroup));
        var root = (RectTransform)go.transform;
        root.SetParent(parent, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);   // GameToLocal은 지도 중심 기준
        root.sizeDelta = Vector2.zero;
        var group = go.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;   // 아래 퀘스트 마커 툴팁을 가리지 않게
        // 내 마커(WhereIAM)보다 아래에 그린다
        if (whereIAM != null && whereIAM.transform.parent == parent) root.SetSiblingIndex(whereIAM.transform.GetSiblingIndex());

        // 화살표: 내 마커 그래픽을 복사해서 파티원 색으로
        var arrowGo = new GameObject("Arrow", typeof(RectTransform));
        var arrow = (RectTransform)arrowGo.transform;
        arrow.SetParent(root, false);
        if (whereIAM != null && whereIAM.transform.childCount > 0)
        {
            foreach (Transform child in whereIAM.transform)
            {
                if (child.name == "Pulse" || !child.gameObject.activeSelf) continue;   // 위치 갱신 효과, 꺼진 그래픽은 빼고
                Transform copy = Instantiate(child, arrow, false);
                foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(behaviour is Graphic)) Destroy(behaviour);
            }
        }
        else
        {
            var img = arrowGo.AddComponent<Image>();
            arrow.sizeDelta = new Vector2(18f, 18f);
            img.raycastTarget = false;
        }

        TMP_Text label = PartyUi.Text(root, "", font, 14f, Color.white, FontStyles.Bold);
        var labelRt = (RectTransform)label.transform;
        labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, 0.5f);
        labelRt.pivot = new Vector2(0.5f, 1f);
        labelRt.anchoredPosition = new Vector2(0f, -16f);
        labelRt.sizeDelta = new Vector2(160f, 20f);
        label.alignment = TextAlignmentOptions.Top;
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color(0f, 0f, 0f, 0.8f);

        return new PartyMarker { root = root, arrow = arrow, label = label, group = group };
    }

    void ApplyMarker(PartyMarker marker, Member m)
    {
        Color color = PartyUi.MemberColor(m.color);
        foreach (Graphic g in marker.arrow.GetComponentsInChildren<Graphic>(true))
        {
            g.raycastTarget = false;
            g.color = new Color(color.r, color.g, color.b, g.color.a > 0f ? g.color.a : 1f);
        }
        marker.label.text = $"<noparse>{m.nickname}</noparse>";
        marker.label.color = color;
        float age = PartySession.PositionAge(m);
        marker.group.alpha = !m.connected ? 0.25f : age > staleSeconds ? 0.45f : 1f;
    }

    // 위치/방향/크기는 지도 확대에 맞춰 매 프레임
    void LateUpdate()
    {
        if (mapView == null || mapView.config == null) return;
        float inv = 1f / Mathf.Max(0.0001f, mapView.transform.localScale.x);
        foreach (var pair in markers)
        {
            Member m = session.Members.FirstOrDefault(x => x.id == pair.Key);
            if (m == null || pair.Value.root == null) continue;
            var pos = new Vector3(m.state.x, m.state.y, m.state.z);
            Vector2 local = mapView.GameToLocal(pos);
            pair.Value.root.anchoredPosition = local;
            pair.Value.root.localScale = new Vector3(inv, inv, 1f);   // 지도를 확대해도 화면 크기 유지

            Vector3 forward = Quaternion.Euler(0f, m.state.yaw, 0f) * Vector3.forward;
            Vector2 dir = mapView.GameToLocal(pos + forward * 10f) - local;
            if (dir.sqrMagnitude > 0.0001f)
                pair.Value.arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
        }
    }

    void Update()
    {
        // 경과 시간 · 거리 글자, 마커 투명도 (1초마다)
        if (Time.unscaledTime < nextTick || !session.IsInParty) return;
        nextTick = Time.unscaledTime + 1f;
        foreach (var (id, label) in rowLabels)
        {
            Member m = session.Members.FirstOrDefault(x => x.id == id);
            if (m != null && label != null) label.text = Info(m);
        }
        UpdateMarkers();
    }
}
