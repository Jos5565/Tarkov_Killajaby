using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// 맵 씬에서 로비로 돌아가는 버튼. 이 오브젝트의 RectTransform 크기로 로비와 같은 스타일 버튼을 만든다.
// 버튼 왼쪽에는 퀘스트 마커 크기 슬라이더(QuestMarkerSizeSlider, 최대 확대 시 크기)를 둔다.
// ExecuteAlways: 편집 모드에서도 보임.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class LobbyBackButton : MonoBehaviour
{
    public string lobbySceneName = "KillaJaby";
    public string label = "로비";
    public TMP_FontAsset font;
    [Tooltip("비우면 아이콘 칸 없이 글자만")]
    public Sprite icon;
    public float fontSize = 22f;
    [Tooltip("Esc 키로도 로비로 이동")]
    public bool escapeKey = true;

    public SelectButton.Palette normal = SelectButton.DefaultNormal;
    public SelectButton.Palette selected = SelectButton.DefaultSelected;

    [Header("Quest Marker Size (버튼 왼쪽)")]
    public bool showQuestMarkerSlider = true;
    public string questMarkerSliderLabel = "퀘스트 마커 (최대 확대)";
    public float sliderWidth = 240f;
    public float sliderFontSize = 15f;
    [Tooltip("버튼과 슬라이더 사이 간격")]
    public float sliderGap = 12f;
    public Color sliderBackground = new Color(0.09f, 0.09f, 0.1f, 0.88f);
    public Color sliderTitleColor = new Color(0.8f, 0.8f, 0.8f);

    SelectButton card;
    GameObject slider;

    void OnEnable() => Build();

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

    void OnDisable()
    {
        if (card != null) EditorPreview.DestroyLater(card.gameObject);
        EditorPreview.DestroyLater(slider);
        card = null;
        slider = null;
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
        EditorPreview.Destroy(slider);
        EditorPreview.ClearLeftovers(transform);

        var rt = (RectTransform)transform;
        card = SelectButton.Create(rt, label, icon, icon != null, fontSize, rt.rect.size, false,
            font, normal, selected, SelectButton.DefaultIconBox);
        SelectButton.Inset((RectTransform)card.transform, 0f, 0f, 0f, 0f);
        card.Button.onClick.AddListener(GoToLobby);
        EditorPreview.MarkDontSave(card.gameObject);

        if (showQuestMarkerSlider) BuildSlider(rt);
    }

    // 버튼 왼쪽에 버튼과 같은 높이로
    void BuildSlider(RectTransform button)
    {
        slider = QuestMarkerSizeSlider.Create(button, questMarkerSliderLabel, new QuestMarkerSizeSlider.Look
        {
            font = font,
            fontSize = sliderFontSize,
            background = sliderBackground,
            title = sliderTitleColor,
            track = SelectButton.DefaultIconBox,
            fill = normal.border,
            handle = selected.fill,
        });
        var srt = (RectTransform)slider.transform;
        srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
        srt.pivot = new Vector2(1f, 0.5f);
        srt.anchoredPosition = new Vector2(-sliderGap, 0f);
        srt.sizeDelta = new Vector2(sliderWidth, button.rect.height);
        EditorPreview.MarkDontSave(slider);
    }

    void Update()
    {
        if (!Application.isPlaying || !escapeKey) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) GoToLobby();
    }

    public void GoToLobby() => SceneManager.LoadScene(lobbySceneName);
}
