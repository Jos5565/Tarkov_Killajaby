using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 퀘스트 위치 도착 알림. 내 위치(WhereIAM)가 갱신될 때 켜진 퀘스트 목표가 arrivalRadius 안에 있으면
// 화면 위쪽에 퀘스트 이름과 목표 목록을 띄운다 (도착한 목표는 강조). 근처에 없으면 닫힌다.
// 캔버스 아래 빈 오브젝트에 붙인다. Play 중에만 동작한다.
[RequireComponent(typeof(RectTransform))]
public class QuestArrivalPanel : MonoBehaviour
{
    public WhereIAM whereIAM;
    public MapMarkerLayer markerLayer;
    public TMP_FontAsset font;

    [Header("Arrival")]
    [Tooltip("도착으로 보는 수평 거리 (m)")]
    public float arrivalRadius = 30f;
    [Tooltip("이보다 높이 차이가 크면 다른 층으로 보고 제외 (m). 0이면 높이 무시")]
    public float maxHeightDifference = 10f;
    [Tooltip("한 번에 보여줄 최대 퀘스트 수 (가까운 순)")]
    public int maxQuests = 3;

    [Header("Look")]
    public float width = 520f;
    public string headerText = "퀘스트 위치 도착";
    public Color backgroundColor = new Color(0.07f, 0.07f, 0.07f, 0.95f);
    public Color borderColor = new Color(0.604f, 0.533f, 0.4f);   // #9A8866
    public Color titleColor = new Color(0.765f, 0.694f, 0.565f);  // #C3B190
    public Color subColor = new Color(0.6f, 0.6f, 0.6f);

    RectTransform panel;
    TMP_Text body;

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        Build();
        if (whereIAM != null) whereIAM.PoseChanged += OnPoseChanged;
        if (markerLayer != null)
        {
            // 마커가 늦게 만들어지거나, 그 자리에서 퀘스트를 켜면 다시 확인
            markerLayer.Built += Recheck;
            markerLayer.KeyVisibilityChanged += OnKeyChanged;
        }
    }

    void OnDisable()
    {
        if (whereIAM != null) whereIAM.PoseChanged -= OnPoseChanged;
        if (markerLayer != null)
        {
            markerLayer.Built -= Recheck;
            markerLayer.KeyVisibilityChanged -= OnKeyChanged;
        }
        if (panel != null) Destroy(panel.gameObject);
        panel = null;
    }

    void OnPoseChanged(WhereIAM w) => Check(w.GamePosition);
    void OnKeyChanged(string key, bool visible) => Recheck();

    void Recheck()
    {
        if (whereIAM != null && whereIAM.HasPosition) Check(whereIAM.GamePosition);
    }

    public void Close()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }

    // 근처 퀘스트 찾기: 켜진 퀘스트의 목표 위치 중 반경 안 (같은 퀘스트는 하나로 묶음)
    void Check(Vector3 me)
    {
        if (markerLayer == null || !markerLayer.IsBuilt) return;

        var nearby = new Dictionary<string, (float distance, HashSet<string> objectives)>();
        foreach (MapMarker m in markerLayer.Placed)
        {
            if (m.type != MarkerType.Quest && m.type != MarkerType.QuestItem) continue;
            if (string.IsNullOrEmpty(m.key) || !markerLayer.IsKeyVisible(m.key) || !markerLayer.IsVisible(m.type)) continue;

            float d = Vector2.Distance(new Vector2(me.x, me.z), new Vector2(m.position.x, m.position.z));
            if (d > arrivalRadius) continue;
            if (maxHeightDifference > 0f && Mathf.Abs(me.y - m.position.y) > maxHeightDifference) continue;

            if (!nearby.TryGetValue(m.key, out var entry)) entry = (d, new HashSet<string>());
            if (!string.IsNullOrEmpty(m.objective)) entry.objectives.Add(m.objective);
            nearby[m.key] = (Mathf.Min(entry.distance, d), entry.objectives);
        }

        if (nearby.Count == 0)
        {
            Close();
            return;
        }

        var parts = new List<string>();
        foreach (var (key, entry) in nearby.OrderBy(kv => kv.Value.distance).Take(maxQuests).Select(kv => (kv.Key, kv.Value)))
        {
            QuestData quest = markerLayer.GetQuest(key);
            if (quest == null) continue;
            string title = ColorUtility.ToHtmlStringRGB(titleColor);
            string sub = ColorUtility.ToHtmlStringRGB(subColor);
            parts.Add($"<size=120%><b><color=#{title}><noparse>{quest.name}</noparse></color></b></size>\n" +
                      $"<color=#{sub}>[<noparse>{quest.trader}</noparse>] 약 {entry.distance:0}m</color>\n" +
                      QuestText.Objectives(quest, entry.objectives));
        }
        if (parts.Count == 0)
        {
            Close();
            return;
        }

        body.text = string.Join("\n\n", parts);
        panel.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    // ---- UI ----

    void Build()
    {
        // 화면 위쪽 가운데: [테두리 > 배경 > (헤더 + 닫기) / 본문]
        var go = new GameObject("ArrivalPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel = (RectTransform)go.transform;
        panel.SetParent(transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = Vector2.zero;
        go.GetComponent<Image>().color = backgroundColor;
        go.AddComponent<Outline>().effectColor = borderColor;

        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 12, 16);
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 헤더: 제목 ...... [닫기]
        var header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        header.transform.SetParent(panel, false);
        var h = header.GetComponent<HorizontalLayoutGroup>();
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;

        TMP_Text title = CreateText(header.transform, headerText, 16f, borderColor, width - 36f - 40f);
        title.fontStyle = FontStyles.Bold;
        TMP_Text close = CreateText(header.transform, "닫기", 16f, subColor, 40f);
        close.alignment = TextAlignmentOptions.MidlineRight;
        close.raycastTarget = true;
        close.gameObject.AddComponent<MarkerFilterClick>().onClick = Close;

        body = CreateText(panel, "", 17f, Color.white, width - 36f);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.richText = true;

        panel.gameObject.SetActive(false);
    }

    TMP_Text CreateText(Transform parent, string text, float size, Color color, float preferredWidth)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredWidth = preferredWidth;
        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }
}
