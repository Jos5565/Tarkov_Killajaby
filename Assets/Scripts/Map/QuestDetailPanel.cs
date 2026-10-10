using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 퀘스트 목록의 행에 마우스를 올리면 보여주는 퀘스트 상세 창 (QuestListPanel이 실행 중에 만든다).
//   퀘스트 이름
//   [상인] · 이 맵 목표 2개
//   목표
//   - 이 맵 목표 (밝게)
//   - 다른 맵/공통 목표 (흐리게)
//   필요 아이템
//   (아이콘) [건네기] 물리 비트코인 ×2 · 인레이드
// 위치: 마우스 왼쪽(화면 안쪽). 패널은 마우스를 받지 않는다 (목록 호버/스크롤 방해 없음).
[RequireComponent(typeof(RectTransform))]
public class QuestDetailPanel : MonoBehaviour
{
    public TMP_FontAsset font;
    public float width = 420f;
    public float titleFontSize = 20f;
    public float bodyFontSize = 15f;
    public float iconSize = 40f;
    [Tooltip("마우스에서 떨어진 거리 (패널은 마우스 왼쪽에 뜬다)")]
    public Vector2 cursorOffset = new Vector2(18f, 0f);
    public Color backgroundColor = new Color(0.07f, 0.07f, 0.07f, 0.96f);
    public Color titleColor = new Color(0.765f, 0.694f, 0.565f);   // #C3B190
    public Color headerColor = new Color(0.6f, 0.6f, 0.6f);
    public Color textColor = new Color(0.88f, 0.88f, 0.88f);
    public Color dimColor = new Color(0.5f, 0.5f, 0.5f);
    public Color iconBoxColor = new Color(1f, 1f, 1f, 0.06f);

    RectTransform panel, content;
    RectTransform canvasRect;
    Camera eventCamera;

    public string CurrentKey { get; private set; }

    // 캔버스 맨 위에 패널을 만든다
    public static QuestDetailPanel Create(Canvas canvas, TMP_FontAsset font)
    {
        var go = new GameObject("QuestDetail", typeof(RectTransform));
        go.SetActive(false);   // 글꼴을 넣은 뒤 켜야 그 글꼴로 만들어진다
        go.transform.SetParent(canvas.rootCanvas.transform, false);
        var detail = go.AddComponent<QuestDetailPanel>();
        detail.font = font;
        go.transform.SetAsLastSibling();
        go.SetActive(true);
        return detail;
    }

    void OnEnable() => Build();

    void OnDisable()
    {
        if (panel != null) Destroy(panel.gameObject);
        panel = null;
    }

    public void Show(QuestData quest, string fallbackName, Vector2 screenPos)
    {
        if (panel == null || quest == null) return;
        CurrentKey = quest.key;
        Fill(quest, fallbackName);
        panel.gameObject.SetActive(true);
        // 줄바꿈되는 글자는 폭이 정해진 뒤에 높이가 정해지므로 두 번 계산한다
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        Move(screenPos);
    }

    public void Hide()
    {
        CurrentKey = null;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    // 마우스 왼쪽에 패널 오른쪽 끝을 맞추고, 위아래는 화면 안으로
    public void Move(Vector2 screenPos)
    {
        if (panel == null || !panel.gameObject.activeSelf) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, eventCamera, out Vector2 local);

        Vector2 size = panel.rect.size;
        Rect bounds = canvasRect.rect;
        float x = local.x - cursorOffset.x;                       // 패널 오른쪽 끝
        x = Mathf.Max(x, bounds.xMin + size.x);                    // 왼쪽 화면 밖으로 나가지 않게
        float top = local.y + size.y * 0.5f;                       // 마우스 높이가 패널 가운데쯤
        top = Mathf.Min(top, bounds.yMax);
        top = Mathf.Max(top, bounds.yMin + size.y);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(x, top);
    }

    // ---- 내용 ----

    void Fill(QuestData quest, string fallbackName)
    {
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

        string name = string.IsNullOrEmpty(quest.name) ? fallbackName : quest.name;
        Text(content, $"<noparse>{name}</noparse>", titleFontSize, FontStyles.Bold, titleColor);

        int thisMap = quest.objectives.Count(o => o.thisMap);
        string sub = (string.IsNullOrEmpty(quest.trader) ? "" : $"[{quest.trader}]  ") +
                     $"이 맵 목표 {thisMap}개 / 전체 {quest.objectives.Count}개";
        Text(content, sub, bodyFontSize - 1f, FontStyles.Normal, headerColor);
        if (quest.noLocation)
            Text(content, "지도 위치 정보 없음 · 이 맵에서 아래 목표를 완료하세요", bodyFontSize - 1f, FontStyles.Italic, titleColor);

        if (quest.objectives.Count > 0)
        {
            Space(6f);
            Text(content, "목표", bodyFontSize - 1f, FontStyles.Bold, headerColor);
            // 지금 맵의 목표만 보여주고, 나머지는 "외 N개 목표 (다른 맵/공통)" 한 줄로 (지도 툴팁과 같은 형식)
            Text(content, QuestText.Objectives(quest, null), bodyFontSize, FontStyles.Normal, textColor);
        }

        if (!string.IsNullOrEmpty(quest.guide))
        {
            Space(6f);
            Text(content, "공략", bodyFontSize - 1f, FontStyles.Bold, headerColor);
            Text(content, $"<noparse>{quest.guide}</noparse>", bodyFontSize, FontStyles.Normal, titleColor);
        }

        if (quest.requirements != null && quest.requirements.Count > 0)
        {
            Space(6f);
            Text(content, "필요 아이템", bodyFontSize - 1f, FontStyles.Bold, headerColor);
            foreach (QuestText.RequirementGroup g in QuestText.GroupRequirements(quest)) ItemRow(g);
        }

        Footer(quest);
    }

    // 맨 아래 안내 (위키 링크가 있으면)
    void Footer(QuestData quest)
    {
        if (string.IsNullOrEmpty(quest.wikiLink)) return;
        Space(4f);
        Text(content, "우클릭: 위키 열기", bodyFontSize - 3f, FontStyles.Italic, dimColor);
    }

    // (아이콘들) [설치] WI-FI 카메라 ×3 · 전체 27개 · 인레이드   (다른 맵에서만 필요하면 흐리게)
    void ItemRow(QuestText.RequirementGroup g)
    {
        QuestRequirement r = g.first;
        var row = new GameObject("Item", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var rt = (RectTransform)row.transform;
        rt.SetParent(content, false);
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        // 고를 수 있는 아이템이 여러 개면 아이콘도 여러 개 (최대 3개)
        IEnumerable<string> ids = r.itemIds ?? Enumerable.Empty<string>();
        foreach (string id in ids.Take(3))
        {
            Sprite sprite = ItemIconCatalog.Get(id);
            if (sprite != null) Icon(rt, sprite, g.OnThisMap ? 1f : 0.45f);
        }

        string dim = ColorUtility.ToHtmlStringRGB(dimColor);
        string text = $"<color=#{dim}>[{QuestText.KindLabels(g)}]</color> <noparse>{QuestText.ItemNames(r)}</noparse>" +
                      QuestText.CountText(g, dim) +
                      (g.foundInRaid ? $" <color=#{dim}>· 인레이드</color>" : "");
        TMP_Text label = Text(rt, text, bodyFontSize, FontStyles.Normal, g.OnThisMap ? textColor : dimColor);
        label.GetComponent<LayoutElement>().flexibleWidth = 1f;
    }


    // ---- UI 생성 ----

    void Build()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvasRect = (RectTransform)canvas.rootCanvas.transform;
        eventCamera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;

        var self = (RectTransform)transform;
        self.anchorMin = Vector2.zero;
        self.anchorMax = Vector2.one;
        self.offsetMin = self.offsetMax = Vector2.zero;

        var go = new GameObject("QuestDetailPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
        panel = (RectTransform)go.transform;
        panel.SetParent(transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(1f, 1f);
        panel.sizeDelta = new Vector2(width, 0f);

        var bg = go.GetComponent<Image>();
        bg.color = backgroundColor;
        bg.raycastTarget = false;
        go.GetComponent<CanvasGroup>().blocksRaycasts = false;   // 패널이 마우스를 가로채지 않게

        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 12, 14);
        layout.spacing = 4f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;   // 폭은 width 고정
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        content = panel;

        panel.gameObject.SetActive(false);
    }

    TMP_Text Text(RectTransform parent, string text, float size, FontStyles style, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return label;
    }

    void Icon(RectTransform parent, Sprite sprite, float alpha)
    {
        var box = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        box.transform.SetParent(parent, false);
        var element = box.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = iconSize;
        element.preferredHeight = element.minHeight = iconSize;
        var bg = box.GetComponent<Image>();
        bg.color = iconBoxColor;
        bg.raycastTarget = false;

        var img = new GameObject("Sprite", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)img.transform;
        rt.SetParent(box.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(2f, 2f);
        rt.offsetMax = new Vector2(-2f, -2f);
        var image = img.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;   // 2칸, 3칸 아이템도 비율 유지
        image.color = new Color(1f, 1f, 1f, alpha);
        image.raycastTarget = false;
    }

    void Space(float height)
    {
        var go = new GameObject("Space", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(content, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
    }
}
