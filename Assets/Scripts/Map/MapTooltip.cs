using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 마커 정보 툴팁. 캔버스의 마지막 자식(가장 위)에 두는 빈 오브젝트에 붙인다.
//   제목 (퀘스트 이름)
//   부제 ([상인])
//   본문 (퀘스트: 전체 목표 목록, 이 마커의 목표 강조 / 보스: 확률)
[RequireComponent(typeof(RectTransform))]
public class MapTooltip : MonoBehaviour
{
    public static MapTooltip Instance { get; private set; }

    public TMP_FontAsset font;
    public float width = 380f;
    public float titleFontSize = 20f;
    public float bodyFontSize = 16f;
    public Vector2 cursorOffset = new Vector2(18f, -18f);
    public Color backgroundColor = new Color(0.07f, 0.07f, 0.07f, 0.95f);
    public Color titleColor = new Color(0.765f, 0.694f, 0.565f);   // #C3B190
    public Color subColor = new Color(0.6f, 0.6f, 0.6f);
    public Color bodyColor = new Color(0.88f, 0.88f, 0.88f);

    RectTransform panel;
    TMP_Text title, sub, body;
    RectTransform canvasRect;
    Camera eventCamera;

    public MapMarker Current { get; private set; }

    void OnEnable()
    {
        Instance = this;
        if (Application.isPlaying) Build();
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        if (panel != null) Destroy(panel.gameObject);
        panel = null;
    }

    public void Show(MapMarker marker, QuestData quest, Vector2 screenPos)
    {
        if (panel == null) return;
        Current = marker;

        title.text = marker.name;
        sub.text = string.IsNullOrEmpty(marker.group) ? "" : $"[{marker.group}]";
        sub.gameObject.SetActive(!string.IsNullOrEmpty(marker.group));
        body.text = BodyText(marker, quest);
        body.gameObject.SetActive(!string.IsNullOrEmpty(body.text));

        panel.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        Move(screenPos);
    }

    static string BodyText(MapMarker marker, QuestData quest)
    {
        if (quest == null)
            return marker.type == MarkerType.QuestItem ? $"퀘스트 아이템: {marker.detail}" : marker.detail;

        string objectives = QuestText.Objectives(quest, new[] { marker.objective });
        return marker.type == MarkerType.QuestItem
            ? $"퀘스트 아이템: <noparse>{marker.detail}</noparse>\n\n{objectives}"
            : objectives;
    }

    public void Move(Vector2 screenPos)
    {
        if (panel == null || !panel.gameObject.activeSelf) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, eventCamera, out Vector2 local);

        // 화면 오른쪽/아래로 넘치면 커서 반대편에 띄운다
        Vector2 size = panel.rect.size;
        Rect bounds = canvasRect.rect;
        bool flipX = local.x + cursorOffset.x + size.x > bounds.xMax;
        bool flipY = local.y + cursorOffset.y - size.y < bounds.yMin;
        panel.pivot = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        panel.anchoredPosition = local + new Vector2(flipX ? -cursorOffset.x : cursorOffset.x, flipY ? -cursorOffset.y : cursorOffset.y);
    }

    public void Hide()
    {
        Current = null;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    void Build()
    {
        Canvas canvas = GetComponentInParent<Canvas>().rootCanvas;
        canvasRect = (RectTransform)canvas.transform;
        eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        // 툴팁 위치는 캔버스 중심 기준 좌표로 계산하므로, 이 오브젝트를 캔버스 전체에 맞춘다
        var self = (RectTransform)transform;
        self.anchorMin = Vector2.zero;
        self.anchorMax = Vector2.one;
        self.offsetMin = self.offsetMax = Vector2.zero;

        var go = new GameObject("TooltipPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel = (RectTransform)go.transform;
        panel.SetParent(transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);

        var bg = go.GetComponent<Image>();
        bg.color = backgroundColor;
        bg.raycastTarget = false;   // 툴팁이 마우스를 가로채지 않게

        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 12);
        layout.spacing = 4f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = CreateText("Title", titleFontSize, FontStyles.Bold, titleColor);
        sub = CreateText("Sub", bodyFontSize, FontStyles.Normal, subColor);
        body = CreateText("Body", bodyFontSize, FontStyles.Normal, bodyColor);

        panel.gameObject.SetActive(false);
    }

    TMP_Text CreateText(string name, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(panel, false);
        go.GetComponent<LayoutElement>().preferredWidth = width - 28f;   // 고정 폭에서 줄바꿈

        var text = go.GetComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }
}
