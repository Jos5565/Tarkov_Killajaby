using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 퀘스트 마커 크기 슬라이더 (지도 화면 [로비] 버튼 왼쪽, LobbyBackButton이 만든다).
//   퀘스트 마커 (최대 확대)          80%
//   [■■■■■■■■■■■■●────────]
// 값은 지도를 최대로 확대했을 때의 퀘스트 마커 크기(퀘스트 마커 Style 크기의 50% ~ 100%, 5% 단위).
// 전체 보기에서는 슬라이더와 상관없이 배틀패스 문서 마커와 같은 크기이고, 확대할수록 이 값 쪽으로 바뀐다 (MapMarkerLayer).
// 움직이면 바로 반영되고 AppSettings에 저장된다.
public static class QuestMarkerSizeSlider
{
    public struct Look
    {
        public TMP_FontAsset font;
        public float fontSize;
        public Color background, title, track, fill, handle;
    }

    public static GameObject Create(RectTransform parent, string label, Look look)
    {
        var root = new GameObject("QuestMarkerSize", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)root.transform;
        rt.SetParent(parent, false);
        var bg = root.GetComponent<Image>();
        bg.color = look.background;   // 지도 위에서 읽히게 + 아래 지도로 클릭이 새지 않게

        var content = new GameObject("Content", typeof(RectTransform));
        var contentRt = (RectTransform)content.transform;
        contentRt.SetParent(rt, false);
        SelectButton.Inset(contentRt, 10f, 8f, 10f, 6f);

        MakeText(contentRt, label, look, TextAlignmentOptions.TopLeft);
        TMP_Text value = MakeText(contentRt, "", look, TextAlignmentOptions.TopRight);

        // 슬라이더: 바탕 / 채움 / 손잡이
        var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        var sliderRt = (RectTransform)sliderGo.transform;
        sliderRt.SetParent(contentRt, false);
        sliderRt.anchorMin = new Vector2(0f, 0f);
        sliderRt.anchorMax = new Vector2(1f, 0f);
        sliderRt.pivot = new Vector2(0.5f, 0f);
        sliderRt.sizeDelta = new Vector2(-12f, 18f);
        sliderRt.anchoredPosition = Vector2.zero;

        Image track = MakeImage(sliderRt, "Background", look.track);
        SelectButton.Inset((RectTransform)track.transform, 0f, 6f, 0f, 6f);
        track.raycastTarget = true;   // 바탕을 클릭해도 그 위치로 이동
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        var fillAreaRt = (RectTransform)fillArea.transform;
        fillAreaRt.SetParent(sliderRt, false);
        SelectButton.Inset(fillAreaRt, 0f, 6f, 0f, 6f);
        Image fill = MakeImage(fillAreaRt, "Fill", look.fill);
        var fillRt = (RectTransform)fill.transform;
        fillRt.sizeDelta = Vector2.zero;
        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        var handleAreaRt = (RectTransform)handleArea.transform;
        handleAreaRt.SetParent(sliderRt, false);
        SelectButton.Inset(handleAreaRt, 0f, 0f, 0f, 0f);
        Image handle = MakeImage(handleAreaRt, "Handle", look.handle);
        var handleRt = (RectTransform)handle.transform;
        handleRt.sizeDelta = new Vector2(12f, 0f);
        handle.raycastTarget = true;

        var slider = sliderGo.GetComponent<Slider>();
        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = AppSettings.MinQuestMarkerScale;
        slider.maxValue = AppSettings.MaxQuestMarkerScale;
        slider.SetValueWithoutNotify(AppSettings.QuestMarkerScale);
        value.text = $"{AppSettings.QuestMarkerScale * 100f:0}%";
        slider.onValueChanged.AddListener(v =>
        {
            v = Mathf.Round(v * 20f) / 20f;   // 5% 단위
            slider.SetValueWithoutNotify(v);
            value.text = $"{v * 100f:0}%";
            if (Application.isPlaying) AppSettings.QuestMarkerScale = v;
        });
        return root;
    }

    static TMP_Text MakeText(RectTransform parent, string text, Look look, TextAlignmentOptions alignment)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        SelectButton.Inset(rt, 0f, 0f, 0f, 0f);
        var label = go.GetComponent<TextMeshProUGUI>();
        if (look.font != null) label.font = look.font;
        label.text = text;
        label.fontSize = look.fontSize;
        label.color = look.title;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    static Image MakeImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        SelectButton.Inset(rt, 0f, 0f, 0f, 0f);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }
}
