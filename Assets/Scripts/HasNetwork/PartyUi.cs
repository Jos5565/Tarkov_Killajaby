using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 파티 화면(로비 칸, 지도 패널)을 코드로 만드는 도우미. 씬은 건드리지 않는다.
public static class PartyUi
{
    // 파티원 색 (방장이 참가 순서대로 번호를 정하므로 모든 파티원 화면에서 같은 사람은 같은 색)
    public static readonly Color[] MemberColors =
    {
        new Color(0.95f, 0.55f, 0.15f),   // 주황
        new Color(0.35f, 0.75f, 1f),      // 하늘
        new Color(0.55f, 0.85f, 0.35f),   // 연두
        new Color(0.95f, 0.45f, 0.7f),    // 분홍
        new Color(0.95f, 0.85f, 0.3f),    // 노랑
        new Color(0.7f, 0.55f, 0.95f),    // 보라
        new Color(0.4f, 0.9f, 0.8f),      // 청록
        new Color(0.9f, 0.9f, 0.9f),      // 흰색
    };

    public static Color MemberColor(byte index) => MemberColors[index % MemberColors.Length];

    public static readonly Color Background = new Color(0.09f, 0.09f, 0.1f, 0.88f);
    public static readonly Color TextColor = new Color(0.88f, 0.88f, 0.88f);
    public static readonly Color SubColor = new Color(0.55f, 0.55f, 0.55f);
    public static readonly Color OkColor = new Color(0.45f, 0.85f, 0.45f);
    public static readonly Color ErrorColor = new Color(0.95f, 0.45f, 0.4f);
    public static readonly Color InputColor = new Color(0.16f, 0.16f, 0.17f);

    // tarkov.dev normalizedName → 화면 이름
    static readonly Dictionary<string, string> MapNames = new Dictionary<string, string>
    {
        { "customs", "세관" }, { "woods", "삼림" }, { "shoreline", "해안선" }, { "interchange", "인터체인지" },
        { "reserve", "리저브" }, { "lighthouse", "등대" }, { "streets-of-tarkov", "타르코프 시내" },
        { "ground-zero", "그라운드 제로" }, { "factory", "공장" }, { "the-lab", "연구소" },
        { "terminal", "터미널" }, { "the-labyrinth", "미궁" }, { "icebreaker", "쇄빙선" },
    };

    public static string MapName(string normalizedName) =>
        string.IsNullOrEmpty(normalizedName) ? "" : MapNames.TryGetValue(normalizedName, out string n) ? n : normalizedName;

    // "방금", "1분", "12분", "1시간"
    public static string Age(float seconds)
    {
        if (seconds < 60f) return "방금";
        if (seconds < 3600f) return $"{(int)(seconds / 60f)}분";
        return $"{(int)(seconds / 3600f)}시간";
    }

    // 씬에 있는 한글 글꼴
    public static TMP_FontAsset FindFont()
    {
        var lobby = UnityEngine.Object.FindAnyObjectByType<LobbyController>();
        if (lobby != null && lobby.font != null) return lobby.font;
        var route = UnityEngine.Object.FindAnyObjectByType<RouteToggleButton>(FindObjectsInactive.Include);
        if (route != null && route.font != null) return route.font;
        foreach (TMP_Text t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.font != null) return t.font;
        return null;
    }

    // 캔버스 맨 위, 마커 툴팁(MapTooltip)보다는 아래에 둔다
    public static void PlaceBelowTooltip(Transform t)
    {
        t.SetAsLastSibling();
        MapTooltip tooltip = MapTooltip.Instance != null ? MapTooltip.Instance : UnityEngine.Object.FindAnyObjectByType<MapTooltip>();
        Transform top = tooltip != null ? tooltip.transform : null;
        while (top != null && top.parent != t.parent) top = top.parent;
        if (top != null && top != t) t.SetSiblingIndex(top.GetSiblingIndex());
    }

    public static RectTransform Vertical(Transform parent, string name, float spacing, RectOffset padding = null, Color? background = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        if (background.HasValue) go.AddComponent<Image>().color = background.Value;
        return rt;
    }

    public static RectTransform Horizontal(Transform parent, string name, float spacing, float height = 0f)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        if (height > 0f) go.AddComponent<LayoutElement>().preferredHeight = height;
        return rt;
    }

    public static TMP_Text Text(Transform parent, string text, TMP_FontAsset font, float size, Color color,
        FontStyles style = FontStyles.Normal, bool flexible = false, bool wrap = false)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        if (flexible) go.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        label.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        label.richText = true;
        label.raycastTarget = false;
        return label;
    }

    public static SelectButton Button(Transform parent, string label, TMP_FontAsset font, Vector2 size, float fontSize, Action onClick)
    {
        SelectButton b = SelectButton.Create((RectTransform)parent, label, null, false, fontSize, size, true,
            font, SelectButton.DefaultNormal, SelectButton.DefaultSelected, SelectButton.DefaultIconBox);
        b.Button.onClick.AddListener(() => onClick());
        return b;
    }

    public static TMP_InputField Input(Transform parent, string placeholder, TMP_FontAsset font, float fontSize, float height, int characterLimit)
    {
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var element = go.GetComponent<LayoutElement>();
        element.preferredHeight = height;
        element.flexibleWidth = 1f;
        var bg = go.GetComponent<Image>();
        bg.color = InputColor;

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        var areaRt = (RectTransform)area.transform;
        areaRt.SetParent(rt, false);
        SelectButton.Inset(areaRt, 8f, 3f, 8f, 3f);

        TMP_Text hint = Text(areaRt, placeholder, font, fontSize, SubColor, FontStyles.Italic);
        SelectButton.Inset((RectTransform)hint.transform, 0f, 0f, 0f, 0f);
        TMP_Text text = Text(areaRt, "", font, fontSize, TextColor);
        text.richText = false;
        SelectButton.Inset((RectTransform)text.transform, 0f, 0f, 0f, 0f);

        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;
        input.textViewport = areaRt;
        input.textComponent = text;
        input.placeholder = hint;
        input.fontAsset = font;
        input.pointSize = fontSize;
        input.characterLimit = characterLimit;
        input.richText = false;
        return input;
    }
}
