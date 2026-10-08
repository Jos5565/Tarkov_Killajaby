using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// 맵 씬에서 로비로 돌아가는 버튼. 이 오브젝트의 RectTransform 크기로 로비와 같은 스타일 버튼을 만든다.
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

    SelectButton card;

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
        card = null;
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
        card = SelectButton.Create(rt, label, icon, icon != null, fontSize, rt.rect.size, false,
            font, normal, selected, SelectButton.DefaultIconBox);
        SelectButton.Inset((RectTransform)card.transform, 0f, 0f, 0f, 0f);
        card.Button.onClick.AddListener(GoToLobby);
        EditorPreview.MarkDontSave(card.gameObject);
    }

    void Update()
    {
        if (!Application.isPlaying || !escapeKey) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) GoToLobby();
    }

    public void GoToLobby() => SceneManager.LoadScene(lobbySceneName);
}
