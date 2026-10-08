using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 선택 상태에 따라 색이 바뀌는 버튼 (로비의 맵/플레이어 카드, 맵 씬의 로비 버튼)
[RequireComponent(typeof(Button))]
public class SelectButton : MonoBehaviour
{
    [Serializable]
    public struct Palette
    {
        public Color border;
        public Color fill;
        public Color label;
        public Color icon;
    }

    // tarkov.dev 스타일 금색(#9A8866) 테마. 아이콘 이미지 자체가 금색이고, icon 색은 그 위에 곱해진다.
    public static readonly Palette DefaultNormal = new Palette
    {
        border = new Color(0.604f, 0.533f, 0.4f),
        fill = new Color(0.16f, 0.16f, 0.16f),
        label = new Color(0.604f, 0.533f, 0.4f),
        icon = Color.white,                          // 원본 금색 그대로
    };
    public static readonly Palette DefaultSelected = new Palette
    {
        border = new Color(0.765f, 0.694f, 0.565f),
        fill = new Color(0.765f, 0.694f, 0.565f),    // #C3B190
        label = new Color(0.08f, 0.08f, 0.08f),
        icon = Color.white,                          // 아이콘 칸은 그대로 (어두운 칸 + 금색 아이콘)
    };
    public static readonly Color DefaultIconBox = new Color(0.1f, 0.1f, 0.1f);

    public Image border;
    public Image fill;
    public Image icon;
    public TMP_Text label;
    public Palette normal;
    public Palette selected;

    public bool IsSelected { get; private set; }
    public Button Button => GetComponent<Button>();

    public void SetSelected(bool value)
    {
        IsSelected = value;
        Palette p = value ? selected : normal;
        if (border != null) border.color = p.border;
        if (fill != null) fill.color = p.fill;
        if (label != null) label.color = p.label;
        if (icon != null) icon.color = p.icon;
    }

    // 카드 생성: Card(테두리 Image + Button) > Fill / IconBox > Icon / Label
    //   size: 아이콘 칸 크기 계산용. layoutSize가 true면 LayoutElement로 이 크기를 요청한다.
    public static SelectButton Create(RectTransform parent, string text, Sprite iconSprite, bool showIconBox,
        float fontSize, Vector2 size, bool layoutSize, TMP_FontAsset font, Palette normal, Palette selected, Color iconBoxColor)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(SelectButton));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        if (layoutSize)
        {
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = size.x;
            element.preferredHeight = size.y;
        }

        float height = size.y;
        var card = go.GetComponent<SelectButton>();
        card.border = go.GetComponent<Image>();

        card.fill = CreateImage(rt, "Fill", null, Color.white);
        Inset((RectTransform)card.fill.transform, 1f, 1f, 1f, 1f);

        float labelLeft = 0f;
        if (showIconBox)
        {
            // 왼쪽 정사각형 아이콘 칸
            Image box = CreateImage(rt, "IconBox", null, iconBoxColor);
            var boxRt = (RectTransform)box.transform;
            boxRt.anchorMin = new Vector2(0f, 0f);
            boxRt.anchorMax = new Vector2(0f, 1f);
            boxRt.pivot = new Vector2(0f, 0.5f);
            boxRt.offsetMin = new Vector2(1f, 1f);
            boxRt.offsetMax = new Vector2(height - 1f, -1f);
            labelLeft = height;

            card.icon = CreateImage(boxRt, "Icon", iconSprite, Color.white);
            card.icon.preserveAspect = true;
            card.icon.enabled = iconSprite != null;
            Inset((RectTransform)card.icon.transform, height * 0.18f, height * 0.18f, height * 0.18f, height * 0.18f);
        }

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.SetParent(rt, false);
        Inset(labelRt, showIconBox ? labelLeft + 14f : 8f, 1f, 8f, 1f);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.enableAutoSizing = true;   // 긴 이름(타르코프 시내 등)은 칸에 맞게 줄어든다
        label.fontSizeMin = fontSize * 0.6f;
        label.fontSizeMax = fontSize;
        label.alignment = showIconBox ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        card.label = label;

        // 색은 SelectButton이 관리. Button은 마우스 오버/누름 때 살짝 어둡게만 한다.
        Button button = card.Button;
        button.targetGraphic = card.fill;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        card.normal = normal;
        card.selected = selected;
        card.SetSelected(false);
        return card;
    }

    static Image CreateImage(RectTransform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    // 부모를 꽉 채우되 left, bottom, right, top 만큼 안쪽으로
    public static void Inset(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }
}
