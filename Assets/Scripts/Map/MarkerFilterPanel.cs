using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// tarkov.dev 스타일 마커 필터 패널.
//   - Extracts        ← 그룹 헤더: 화살표로 접기/펴기, 체크박스로 그룹 전체 켜기/끄기
//     [v] (icon) PMC  ← 항목: MarkerType 하나를 켜고 끈다
// 행 UI는 groups 설정으로 코드에서 생성한다 (ExecuteAlways: 편집 모드에서도 보임).
// 패널 오브젝트에는 배경 Image + VerticalLayoutGroup + ContentSizeFitter를 둔다.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class MarkerFilterPanel : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string label;
        public MarkerType type;
        [Tooltip("비우면 탈출구 타입은 extractIcon, 나머지는 dotIcon")]
        public Sprite icon;
    }

    [Serializable]
    public class Group
    {
        public string title;
        [Tooltip("편집 모드 미리보기에서 펼칠지. Play 중에는 맵별 저장값 (처음에는 접힘)")]
        public bool expanded = true;
        [Tooltip("이 맵에 마커가 있는 항목만 보여준다 (예: 배틀패스 문서는 맵마다 종류가 다름)")]
        public bool onlyPresent;
        public List<Entry> entries = new List<Entry>();
    }

    public MapMarkerLayer markerLayer;

    [Header("Look")]
    public TMP_FontAsset font;
    public Sprite boxSprite;
    public Sprite checkSprite;
    public Sprite extractIcon;
    public Sprite dotIcon;
    public float rowHeight = 28f;
    public float fontSize = 18f;
    public float headerFontSize = 20f;
    public float boxSize = 18f;
    public float iconSize = 20f;
    public float indent = 26f;
    public Color textColor = new Color(0.86f, 0.86f, 0.86f, 1f);
    public Color boxColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    public Color checkColor = Color.white;

    [Header("Groups")]
    public List<Group> groups = new List<Group>();

    [Header("Area Label")]
    [Tooltip("groups에 구역 이름(AreaLabel) 항목이 없으면 맨 아래에 이 그룹을 자동으로 붙인다")]
    public bool autoAreaLabelGroup = true;
    public string areaLabelGroupTitle = "지도";
    public string areaLabelEntryLabel = "구역 이름";

    readonly List<GameObject> rows = new List<GameObject>();
    readonly List<(MarkerType type, Image check)> entryChecks = new List<(MarkerType, Image)>();
    readonly List<(List<Entry> entries, Image check)> groupChecks = new List<(List<Entry>, Image)>();

    void OnEnable()
    {
        if (markerLayer != null)
        {
            markerLayer.VisibilityChanged += OnVisibilityChanged;
            markerLayer.Built += Build;   // onlyPresent 그룹은 마커가 배치된 뒤에 항목이 정해진다
        }
        Build();
    }

    void OnDisable()
    {
        if (markerLayer != null)
        {
            markerLayer.VisibilityChanged -= OnVisibilityChanged;
            markerLayer.Built -= Build;
        }
        foreach (GameObject row in rows) EditorPreview.DestroyLater(row);
        ResetLists();
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

    void OnVisibilityChanged(MarkerType type, bool visible) => Refresh();

    public void Build()
    {
        Clear();
        if (markerLayer == null) return;

        foreach (Group group in GroupsToShow())
        {
            // 로비에서 고른 플레이어 타입으로 쓸 수 없는 항목은 숨긴다 (예: PMC면 Scav 탈출구)
            List<Entry> entries = group.entries
                .Where(e => markerLayer.IsAvailable(e.type) && (!group.onlyPresent || markerLayer.HasPlaced(e.type)))
                .ToList();
            if (entries.Count == 0) continue;

            // 헤더: [-] [v] Title
            RectTransform header = CreateRow(0f);
            header.GetComponent<MarkerFilterClick>().onClick = () => ToggleGroup(entries);

            bool expanded = markerLayer.IsGroupExpanded(group.title, group.expanded);   // Play 중에는 맵별 저장값
            TMP_Text arrow = CreateLabel(header, expanded ? "−" : "+", headerFontSize, FontStyles.Bold, 14f);
            arrow.raycastTarget = true;
            arrow.gameObject.AddComponent<MarkerFilterClick>().onClick = () =>
            {
                markerLayer.SetGroupExpanded(group.title, !expanded);
                if (EditorPreview.IsEditMode) group.expanded = !expanded;
                Build();
            };

            groupChecks.Add((entries, CreateCheckBox(header)));
            CreateLabel(header, group.title, headerFontSize, FontStyles.Bold);

            if (!expanded) continue;

            // 항목: [v] (icon) Label
            foreach (Entry entry in entries)
            {
                RectTransform row = CreateRow(indent);
                row.GetComponent<MarkerFilterClick>().onClick = () =>
                    markerLayer.SetVisible(entry.type, !markerLayer.IsVisible(entry.type));

                entryChecks.Add((entry.type, CreateCheckBox(row)));
                CreateIcon(row, entry.icon != null ? entry.icon : DefaultIcon(entry.type), markerLayer.GetColor(entry.type));
                CreateLabel(row, entry.label, fontSize, FontStyles.Normal);
            }
        }

        Refresh();
    }

    // Inspector의 groups + (없으면) 구역 이름 그룹. 씬을 고치지 않고 새 항목을 보여주기 위함
    IEnumerable<Group> GroupsToShow()
    {
        foreach (Group group in groups) yield return group;
        if (!autoAreaLabelGroup || groups.Any(g => g.entries.Any(e => e.type == MarkerType.AreaLabel))) yield break;
        yield return new Group
        {
            title = areaLabelGroupTitle,
            onlyPresent = true,
            entries = { new Entry { label = areaLabelEntryLabel, type = MarkerType.AreaLabel } },
        };
    }

    // 체크 표시 갱신. 그룹은 전부 켜짐=체크, 일부만 켜짐=흐린 체크
    public void Refresh()
    {
        if (markerLayer == null) return;

        foreach (var (type, check) in entryChecks)
            check.enabled = markerLayer.IsVisible(type);

        foreach (var (entries, check) in groupChecks)
        {
            int on = entries.Count(e => markerLayer.IsVisible(e.type));
            check.enabled = on > 0;
            Color c = checkColor;
            c.a = on == entries.Count ? 1f : 0.35f;
            check.color = c;
        }
    }

    void ToggleGroup(List<Entry> entries)
    {
        bool allOn = entries.All(e => markerLayer.IsVisible(e.type));
        foreach (Entry e in entries) markerLayer.SetVisible(e.type, !allOn);
        Refresh();
    }

    Sprite DefaultIcon(MarkerType type) =>
        type == MarkerType.ExtractPmc || type == MarkerType.ExtractScav ||
        type == MarkerType.ExtractShared || type == MarkerType.Transit ? extractIcon : dotIcon;

    // ---- UI 생성 ----

    RectTransform CreateRow(float leftPadding)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement), typeof(Image), typeof(MarkerFilterClick));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset((int)leftPadding, 0, 0, 0);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var element = go.GetComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = rowHeight;

        go.GetComponent<Image>().color = Color.clear;   // 행 전체를 클릭 영역으로

        EditorPreview.MarkDontSave(go);
        rows.Add(go);
        return rt;
    }

    Image CreateCheckBox(RectTransform row)
    {
        Image box = CreateImage(row, "Box", boxSprite, boxColor, boxSize);
        box.type = Image.Type.Sliced;

        var checkGo = new GameObject("Check", typeof(RectTransform), typeof(Image));
        var checkRt = (RectTransform)checkGo.transform;
        checkRt.SetParent(box.transform, false);
        checkRt.anchorMin = Vector2.zero;
        checkRt.anchorMax = Vector2.one;
        checkRt.offsetMin = checkRt.offsetMax = Vector2.zero;

        var check = checkGo.GetComponent<Image>();
        check.sprite = checkSprite;
        check.color = checkColor;
        check.raycastTarget = false;
        EditorPreview.MarkDontSave(checkGo);
        return check;
    }

    void CreateIcon(RectTransform row, Sprite sprite, Color color)
    {
        Image icon = CreateImage(row, "Icon", sprite, color, iconSize);
        icon.preserveAspect = true;
    }

    Image CreateImage(RectTransform row, string name, Sprite sprite, Color color, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(row, false);

        var element = go.GetComponent<LayoutElement>();
        element.preferredWidth = element.preferredHeight = size;

        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        EditorPreview.MarkDontSave(go);
        return img;
    }

    TMP_Text CreateLabel(RectTransform row, string text, float size, FontStyles style, float width = -1f)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(row, false);

        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = textColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        if (width > 0f) go.GetComponent<LayoutElement>().preferredWidth = width;
        EditorPreview.MarkDontSave(go);
        return label;
    }

    void Clear()
    {
        foreach (GameObject row in rows) EditorPreview.Destroy(row);
        ResetLists();
        EditorPreview.ClearLeftovers(transform);
    }

    void ResetLists()
    {
        rows.Clear();
        entryChecks.Clear();
        groupChecks.Clear();
    }
}
