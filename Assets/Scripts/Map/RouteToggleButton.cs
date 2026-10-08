using TMPro;
using UnityEngine;

// [경로] 버튼: 퀘스트 루트 표시를 켜고 끈다. 켜져 있으면 선택 색(밝은 금색)으로 표시.
// 이 오브젝트의 RectTransform 크기로 로비와 같은 스타일 버튼을 만든다. ExecuteAlways: 편집 모드에서도 보임.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class RouteToggleButton : MonoBehaviour
{
    public QuestRoute route;
    public string label = "경로";
    public TMP_FontAsset font;
    public float fontSize = 22f;
    [Range(0f, 1f)]
    [Tooltip("퀘스트를 쓸 수 없을 때(스캐브) 버튼 투명도")]
    public float unavailableAlpha = 0.35f;

    public SelectButton.Palette normal = SelectButton.DefaultNormal;
    public SelectButton.Palette selected = SelectButton.DefaultSelected;

    SelectButton card;

    void OnEnable()
    {
        Build();
        if (route != null) route.RouteEnabledChanged += OnRouteEnabledChanged;
    }

    void OnDisable()
    {
        if (route != null) route.RouteEnabledChanged -= OnRouteEnabledChanged;
        if (card != null) EditorPreview.DestroyLater(card.gameObject);
        card = null;
    }

    [ContextMenu("Reset Colors")]
    void ResetColors()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Reset Colors");
#endif
        normal = SelectButton.DefaultNormal;
        selected = SelectButton.DefaultSelected;
        Build();
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

    void Build()
    {
        if (card != null) EditorPreview.Destroy(card.gameObject);
        EditorPreview.ClearLeftovers(transform);

        var rt = (RectTransform)transform;
        card = SelectButton.Create(rt, label, null, false, fontSize, rt.rect.size, false,
            font, normal, selected, SelectButton.DefaultIconBox);
        SelectButton.Inset((RectTransform)card.transform, 0f, 0f, 0f, 0f);
        card.Button.onClick.AddListener(Toggle);
        card.SetSelected(route == null || route.RouteEnabled);

        // 스캐브: 퀘스트가 없으니 경로 버튼 비활성화 (자리는 유지해서 로비 버튼이 움직이지 않게)
        if (Application.isPlaying && route != null && route.markerLayer != null && !route.markerLayer.QuestsAvailable)
        {
            card.SetSelected(false);
            card.Button.interactable = false;
            card.gameObject.AddComponent<CanvasGroup>().alpha = unavailableAlpha;
        }
        EditorPreview.MarkDontSave(card.gameObject);
    }

    void Toggle()
    {
        if (route != null) route.SetRouteEnabled(!route.RouteEnabled);
    }

    void OnRouteEnabledChanged(bool enabled)
    {
        if (card != null) card.SetSelected(enabled);
    }
}
