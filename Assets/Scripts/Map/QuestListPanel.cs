using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// EFTLibrary 스타일 퀘스트 목록 패널 (지도 오른쪽).
//   퀘스트  12/41                ALL ON/OFF
//   [ 퀘스트 검색            ]
//   ● 떡밥 뿌리기              (==●)   ← 행 클릭: 해당 퀘스트 마커 켜기/끄기
// 목록은 MapMarkerLayer.Quests(이 맵에 위치가 있는 퀘스트)로 만든다.
// ExecuteAlways: 편집 모드에서도 보임. 패널 크기는 이 오브젝트의 RectTransform을 따른다.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class QuestListPanel : MonoBehaviour
{
    public MapMarkerLayer markerLayer;
    [Tooltip("퀘스트를 쓸 수 없을 때(스캐브) 패널을 숨기고, 패널 자리만큼 비워둔 지도 영역을 오른쪽 끝까지 넓힌다")]
    public RectTransform mapViewport;

    [Header("Assets")]
    public TMP_FontAsset font;
    [Tooltip("원 (점, 스위치 손잡이)")]
    public Sprite circleSprite;
    [Tooltip("둥근 사각형 (검색창, 스위치 바탕) - Sliced")]
    public Sprite roundedSprite;

    [Header("Text")]
    public string title = "퀘스트";
    public string searchPlaceholder = "퀘스트 검색";
    public string allToggleLabel = "ALL ON/OFF";

    [Header("Look")]
    public float rowHeight = 30f;
    public float fontSize = 16f;
    public float titleFontSize = 18f;
    public Color backgroundColor = new Color(0.09f, 0.09f, 0.1f, 0.95f);
    public Color accentColor = new Color(0.95f, 0.45f, 0.1f);
    public Color textColor = new Color(0.88f, 0.88f, 0.88f);
    public Color subTextColor = new Color(0.55f, 0.55f, 0.55f);
    public Color inputColor = new Color(0.16f, 0.16f, 0.17f);
    public Color offColor = new Color(0.35f, 0.35f, 0.35f);

    class Row
    {
        public MapMarkerLayer.KeyInfo info;
        public GameObject go;
        public Image dot;
        public Image track;
        public RectTransform handle;
    }

    readonly List<Row> rows = new List<Row>();
    GameObject root;
    RectTransform listContent;
    TMP_Text countLabel;
    TMP_InputField search;

    void OnEnable()
    {
        // 스캐브: 퀘스트 패널 숨김 + 지도 영역 확장 (MapView가 Start에서 화면 맞춤을 하기 전에 적용됨)
        if (Application.isPlaying && markerLayer != null && !markerLayer.QuestsAvailable)
        {
            if (mapViewport != null)
            {
                Vector2 min = mapViewport.offsetMin, max = mapViewport.offsetMax;
                mapViewport.offsetMax = new Vector2(0f, max.y);
                mapViewport.offsetMin = min;
            }
            return;   // UI를 만들지 않음 (OnEnable 안에서 SetActive(false)는 오류가 나므로 쓰지 않는다)
        }

        Build();
        if (markerLayer == null) return;
        markerLayer.Built += BuildRows;
        markerLayer.KeyVisibilityChanged += OnKeyVisibilityChanged;
    }

    void OnDisable()
    {
        if (markerLayer != null)
        {
            markerLayer.Built -= BuildRows;
            markerLayer.KeyVisibilityChanged -= OnKeyVisibilityChanged;
        }
        EditorPreview.DestroyLater(root);
        root = null;
        rows.Clear();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!EditorPreview.IsEditMode) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled) Build();
        };
    }
#endif

    void OnKeyVisibilityChanged(string key, bool visible)
    {
        Row row = rows.Find(r => r.info.key == key);
        if (row != null) ApplyRowState(row);
        RefreshCount();
    }

    // ---- 동작 ----

    void ToggleAll()
    {
        bool allOn = rows.All(r => markerLayer.IsKeyVisible(r.info.key));
        foreach (Row row in rows) markerLayer.SetKeyVisible(row.info.key, !allOn);
    }

    void ApplySearch(string query)
    {
        query = query?.Trim() ?? "";
        foreach (Row row in rows)
            row.go.SetActive(query.Length == 0 || row.info.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    void ApplyRowState(Row row)
    {
        bool on = markerLayer.IsKeyVisible(row.info.key);
        row.dot.color = on ? accentColor : offColor;
        row.track.color = on ? accentColor : offColor;
        // 손잡이: 켜짐 = 오른쪽, 꺼짐 = 왼쪽
        row.handle.anchorMin = row.handle.anchorMax = new Vector2(on ? 1f : 0f, 0.5f);
        row.handle.pivot = new Vector2(on ? 1f : 0f, 0.5f);
        row.handle.anchoredPosition = new Vector2(on ? -2f : 2f, 0f);
    }

    void RefreshCount()
    {
        if (countLabel == null || markerLayer == null) return;
        int on = rows.Count(r => markerLayer.IsKeyVisible(r.info.key));
        countLabel.text = $"{on}/{rows.Count}";
    }

    // ---- UI 생성 ----

    void Build()
    {
        EditorPreview.Destroy(root);
        EditorPreview.ClearLeftovers(transform);
        rows.Clear();

        root = new GameObject("QuestPanelUI", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        var rootRt = (RectTransform)root.transform;
        rootRt.SetParent(transform, false);
        SelectButton.Inset(rootRt, 0f, 0f, 0f, 0f);
        root.GetComponent<Image>().color = backgroundColor;   // 패널 위 클릭이 지도로 넘어가지 않게 raycast 유지

        var layout = root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        BuildHeader(rootRt);
        BuildSearch(rootRt);
        BuildList(rootRt);
        BuildRows();

        EditorPreview.MarkDontSave(root);
    }

    void BuildHeader(RectTransform parent)
    {
        RectTransform header = CreateHorizontal(parent, "Header", 24f, 8f);
        CreateText(header, title, titleFontSize, FontStyles.Bold, textColor, flexible: false);
        countLabel = CreateText(header, "0/0", fontSize, FontStyles.Normal, subTextColor, flexible: true);

        TMP_Text all = CreateText(header, allToggleLabel, fontSize - 2f, FontStyles.Bold, accentColor, flexible: false);
        all.raycastTarget = true;
        all.gameObject.AddComponent<MarkerFilterClick>().onClick = ToggleAll;
    }

    void BuildSearch(RectTransform parent)
    {
        var go = new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 32f;
        var bg = go.GetComponent<Image>();
        bg.sprite = roundedSprite;
        bg.type = Image.Type.Sliced;
        bg.color = inputColor;

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        var areaRt = (RectTransform)area.transform;
        areaRt.SetParent(rt, false);
        SelectButton.Inset(areaRt, 10f, 4f, 10f, 4f);

        TMP_Text placeholder = CreateText(areaRt, searchPlaceholder, fontSize, FontStyles.Italic, subTextColor, flexible: false);
        SelectButton.Inset((RectTransform)placeholder.transform, 0f, 0f, 0f, 0f);
        TMP_Text text = CreateText(areaRt, "", fontSize, FontStyles.Normal, textColor, flexible: false);
        SelectButton.Inset((RectTransform)text.transform, 0f, 0f, 0f, 0f);

        // 자식(텍스트)을 먼저 만든 뒤 InputField를 붙인다
        search = go.AddComponent<TMP_InputField>();
        search.targetGraphic = bg;
        search.textViewport = areaRt;
        search.textComponent = text;
        search.placeholder = placeholder;
        search.fontAsset = font;
        search.pointSize = fontSize;
        search.onValueChanged.AddListener(ApplySearch);
    }

    void BuildList(RectTransform parent)
    {
        var go = new GameObject("List", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        go.GetComponent<LayoutElement>().flexibleHeight = 1f;   // 남은 높이를 모두 사용

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        var viewportRt = (RectTransform)viewport.transform;
        viewportRt.SetParent(rt, false);
        SelectButton.Inset(viewportRt, 0f, 0f, 0f, 0f);
        viewport.GetComponent<Image>().color = Color.clear;   // 빈 곳에서도 휠 스크롤

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        listContent = (RectTransform)content.transform;
        listContent.SetParent(viewportRt, false);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.sizeDelta = Vector2.zero;

        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 2f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = go.GetComponent<ScrollRect>();
        scroll.viewport = viewportRt;
        scroll.content = listContent;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
    }

    void BuildRows()
    {
        if (listContent == null) return;
        foreach (Row row in rows) EditorPreview.Destroy(row.go);
        rows.Clear();
        if (markerLayer == null) return;

        foreach (MapMarkerLayer.KeyInfo info in markerLayer.Quests)
        {
            var row = new Row { info = info };
            RectTransform rt = CreateHorizontal(listContent, info.name, rowHeight, 8f);
            row.go = rt.gameObject;

            // 행 전체 클릭으로 켜기/끄기
            rt.gameObject.AddComponent<Image>().color = Color.clear;
            rt.gameObject.AddComponent<MarkerFilterClick>().onClick = () =>
                markerLayer.SetKeyVisible(info.key, !markerLayer.IsKeyVisible(info.key));

            row.dot = CreateImage(rt, "Dot", circleSprite, accentColor, new Vector2(12f, 12f));
            TMP_Text label = CreateText(rt, info.name, fontSize, FontStyles.Normal, textColor, flexible: true);
            label.overflowMode = TextOverflowModes.Ellipsis;

            // 스위치: 둥근 바탕 + 원형 손잡이
            row.track = CreateImage(rt, "Switch", roundedSprite, accentColor, new Vector2(36f, 18f));
            row.track.type = Image.Type.Sliced;
            Image handle = CreateImage((RectTransform)row.track.transform, "Handle", circleSprite, Color.white, Vector2.zero);
            row.handle = (RectTransform)handle.transform;
            row.handle.sizeDelta = new Vector2(14f, 14f);   // 바탕(Switch)에는 레이아웃 그룹이 없어 직접 크기 지정

            ApplyRowState(row);
            EditorPreview.MarkDontSave(row.go);
            rows.Add(row);
        }

        if (search != null) ApplySearch(search.text);
        RefreshCount();
    }

    RectTransform CreateHorizontal(RectTransform parent, string name, float height, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 0, 0);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return rt;
    }

    TMP_Text CreateText(RectTransform parent, string text, float size, FontStyles style, Color color, bool flexible)
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
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    static Image CreateImage(RectTransform parent, string name, Sprite sprite, Color color, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var element = go.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = size.x;
        element.preferredHeight = size.y;

        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }
}
