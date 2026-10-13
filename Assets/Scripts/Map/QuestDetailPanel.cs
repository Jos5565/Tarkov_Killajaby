using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 퀘스트 상세 창 (QuestListPanel이 실행 중에 두 개 만든다).
//   - 호버 창: 목록의 행에 마우스를 올리면 마우스 왼쪽에
//   - 고정 창: 경로 1번 퀘스트를 목록 왼쪽 옆에 늘 띄워 둔다 (PinBeside). 목록을 최소화하면 머리글 아래로
//     맨 위 줄의 [—] 버튼으로 최소화(맨 위 줄 + 퀘스트 이름만), [+]로 다시 펼친다. 상태는 저장된다.
//   퀘스트 이름
//   [상인] · 이 맵 목표 2개
//   목표
//   - 이 맵 목표 (밝게)
//   - 다른 맵/공통 목표 (흐리게)
//   필요 아이템
//   (아이콘) [건네기] 물리 비트코인 ×2 · 인레이드
// 패널은 마우스를 받지 않는다 (목록 호버/스크롤, 지도 조작 방해 없음).
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
    public Color captionColor = new Color(0.95f, 0.45f, 0.1f);    // 경로 색
    public Color buttonColor = new Color(1f, 1f, 1f, 0.08f);
    public float buttonSize = 22f;

    const string MinimizedPref = "QuestDetail.PinnedMinimized";

    RectTransform panel, content;
    RectTransform canvasRect;
    Camera eventCamera;

    public string CurrentKey { get; private set; }

    // 고정 창 최소화 (모든 맵 공통으로 저장)
    public bool Minimized
    {
        get => PlayerPrefs.GetInt(MinimizedPref, 0) == 1;
        set { PlayerPrefs.SetInt(MinimizedPref, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // 마지막 고정 창 내용 (최소화 버튼을 누르면 같은 내용으로 다시 그린다)
    Action repin;

    // 캔버스 맨 위에 패널을 만든다
    public static QuestDetailPanel Create(Canvas canvas, TMP_FontAsset font)
    {
        var go = new GameObject("QuestDetail", typeof(RectTransform));
        go.SetActive(false);   // 글꼴을 넣은 뒤 켜야 그 글꼴로 만들어진다
        go.transform.SetParent(canvas.rootCanvas.transform, false);
        var detail = go.AddComponent<QuestDetailPanel>();
        detail.font = font;
        go.transform.SetAsLastSibling();
        // 마커 툴팁(퀘스트 · 탈출구 상세)은 이 창보다 위에 그린다: 툴팁 바로 아래 순서로 넣는다
        // (Instance는 툴팁의 OnEnable에서 정해지므로 아직 없으면 씬에서 찾는다)
        MapTooltip found = MapTooltip.Instance != null ? MapTooltip.Instance : FindAnyObjectByType<MapTooltip>();
        Transform tooltip = found != null ? found.transform : null;
        while (tooltip != null && tooltip.parent != go.transform.parent) tooltip = tooltip.parent;
        if (tooltip != null) go.transform.SetSiblingIndex(tooltip.GetSiblingIndex());
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
        if (!Open(quest, fallbackName, null, null)) return;
        Move(screenPos);
    }

    // 고정 창: target(목록 패널) 왼쪽 옆, 위쪽을 맞춰서 띄운다.
    // caption은 제목 위 한 줄 (예: "경로 1번 · 약 120m"), highlight는 강조할 목표 ID
    // below면 target 아래쪽에 오른쪽 끝을 맞춰서 (최소화한 목록 머리글 아래)
    public void ShowPinned(QuestData quest, string fallbackName, string caption, ICollection<string> highlight, RectTransform target, float gap, bool below = false)
    {
        repin = () => ShowPinned(quest, fallbackName, caption, highlight, target, gap, below);
        if (!Open(quest, fallbackName, caption, highlight, true)) return;
        PinBeside(target, gap, below);
    }

    bool Open(QuestData quest, string fallbackName, string caption, ICollection<string> highlight, bool pinned = false)
    {
        if (panel == null || quest == null) return false;
        CurrentKey = quest.key;
        Fill(quest, fallbackName, caption, highlight, pinned);
        panel.gameObject.SetActive(true);
        // 줄바꿈되는 글자는 폭이 정해진 뒤에 높이가 정해지므로 두 번 계산한다
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        return true;
    }

    // 패널 오른쪽 위 = target 왼쪽 위 - gap (위아래는 화면 안으로)
    // below면 패널 오른쪽 위 = target 오른쪽 아래 - gap
    void PinBeside(RectTransform target, float gap, bool below)
    {
        if (target == null) return;
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);   // 0 왼쪽 아래, 1 왼쪽 위, 3 오른쪽 아래
        Vector2 corner = canvasRect.InverseTransformPoint(below ? corners[3] : corners[1]);
        Vector2 pos = below ? new Vector2(corner.x, corner.y - gap) : new Vector2(corner.x - gap, corner.y);
        pos.y = Mathf.Max(Mathf.Min(pos.y, canvasRect.rect.yMax), canvasRect.rect.yMin + panel.rect.height);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = pos;
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

    void Fill(QuestData quest, string fallbackName, string caption, ICollection<string> highlight, bool pinned)
    {
        // Destroy는 프레임 끝에 지워지므로 먼저 꺼서 레이아웃(높이 계산)에서 빼 둔다.
        // 안 그러면 같은 프레임에 여러 번 그릴 때 높이가 쌓여 고정 창 위치가 화면 밖으로 밀린다
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            GameObject child = content.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }

        if (pinned) CaptionRow(caption);
        else if (!string.IsNullOrEmpty(caption))
            Text(content, caption, bodyFontSize - 1f, FontStyles.Bold, captionColor);
        string name = string.IsNullOrEmpty(quest.name) ? fallbackName : quest.name;
        Text(content, $"<noparse>{name}</noparse>", titleFontSize, FontStyles.Bold, titleColor);
        if (pinned && Minimized) return;

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
            // 고정 창: 목표 줄을 클릭하면 완료 체크/해제 (QuestProgress). 호버 창은 마우스를 따라다녀 클릭할 수 없다
            TMP_Text objectives = Text(content, QuestText.Objectives(quest, highlight, links: pinned), bodyFontSize, FontStyles.Normal, textColor);
            if (pinned) MakeClickable(objectives).onClick = id => QuestProgress.Toggle(quest.key, id);
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

        Footer(quest, pinned);
    }

    // 패널 전체는 마우스를 받지 않으므로(CanvasGroup) 이 글자만 따로 받게 한다
    static TextLinkClick MakeClickable(TMP_Text label)
    {
        label.raycastTarget = true;
        var group = label.gameObject.AddComponent<CanvasGroup>();
        group.ignoreParentGroups = true;
        group.blocksRaycasts = true;
        return label.gameObject.AddComponent<TextLinkClick>();
    }

    // 고정 창 맨 위 줄: 경로 1번 퀘스트 · 약 120m ........ [—]
    void CaptionRow(string caption)
    {
        var row = new GameObject("Caption", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var rt = (RectTransform)row.transform;
        rt.SetParent(content, false);
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        TMP_Text label = Text(rt, caption ?? "", bodyFontSize - 1f, FontStyles.Bold, captionColor);
        label.GetComponent<LayoutElement>().flexibleWidth = 1f;

        // 버튼: 패널 전체는 마우스를 받지 않으므로(CanvasGroup) 버튼만 따로 받게 한다
        var go = new GameObject("MinimizeButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(CanvasGroup));
        go.transform.SetParent(rt, false);
        var element = go.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = buttonSize;
        element.preferredHeight = element.minHeight = buttonSize;
        var group = go.GetComponent<CanvasGroup>();
        group.ignoreParentGroups = true;
        group.blocksRaycasts = true;
        var bg = go.GetComponent<Image>();
        bg.color = buttonColor;
        var button = go.GetComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(() =>
        {
            Minimized = !Minimized;
            repin?.Invoke();
        });

        TMP_Text icon = Text((RectTransform)go.transform, Minimized ? "+" : "—", bodyFontSize, FontStyles.Bold, textColor);
        icon.alignment = TextAlignmentOptions.Center;
        var iconRt = (RectTransform)icon.transform;
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = iconRt.offsetMax = Vector2.zero;
    }

    // 맨 아래 안내: 고정 창은 목표 클릭 안내, 호버 창은 위키 (링크가 있으면)
    void Footer(QuestData quest, bool pinned)
    {
        if (pinned)
        {
            Space(4f);
            Text(content, "목표 클릭: 완료 체크 / 해제", bodyFontSize - 3f, FontStyles.Italic, dimColor);
            return;
        }
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
