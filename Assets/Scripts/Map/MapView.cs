using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// UGUI 지도 화면. MapContent(RectTransform)에 붙인다.
// 계층: Viewport(RectMask2D) > MapContent(this) > [Base, Layers, Markers]
// ExecuteAlways: Play 하지 않아도 편집 모드에서 지도를 미리 보여준다.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class MapView : MonoBehaviour
{
    public MapConfig config;
    public RectTransform viewport;
    public Image baseImage;
    public RectTransform layerRoot;
    public RectTransform markerRoot;

    [Tooltip("확대하지 않았을 때 지도 바깥 여백 (Canvas 기준 px, 상하좌우 각각)")]
    public float fitPadding = 40f;

    readonly List<Image> layerImages = new List<Image>();
    RectTransform content;
    int currentLayer = -1;

    public int CurrentLayer => currentLayer;
    public bool IsLoaded { get; private set; }

    // 지도 로드 후 (콘텐츠 크기 확정 후) 호출
    public event Action<MapView> Loaded;

    void Awake() => content = (RectTransform)transform;

    // 편집 모드 미리보기 (Play 중에는 Canvas 크기가 확정된 Start에서 로드)
    void OnEnable()
    {
        if (EditorPreview.IsEditMode && config != null) Load(config);
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        if (GameSession.HasSelection) config = GameSession.Map;   // 로비에서 고른 맵
        if (config != null) Load(config);
    }

    void OnDisable()
    {
        foreach (var img in layerImages)
            if (img != null) EditorPreview.DestroyLater(img.gameObject);
        layerImages.Clear();
        IsLoaded = false;
    }

#if UNITY_EDITOR
    // 편집 모드에서 config 등을 바꾸면 미리보기를 다시 그린다
    void OnValidate()
    {
        if (!EditorPreview.IsEditMode) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled && config != null) Load(config);
        };
    }
#endif

    [ContextMenu("Reload")]
    void Reload()
    {
        if (config != null) Load(config);
    }

    public void Load(MapConfig newConfig)
    {
        config = newConfig;
        if (content == null) content = (RectTransform)transform;
        if (config.baseSprite == null) return;

        // 콘텐츠 크기 = 기본 지도 이미지 원본 픽셀 크기
        content.sizeDelta = config.baseSprite.rect.size;
        baseImage.sprite = config.baseSprite;

        ClearLayers();

        foreach (var layer in config.layers)
            layerImages.Add(CreateLayerImage(layer));

        ShowLayer(-1);
        FitToViewport();

        IsLoaded = true;
        Loaded?.Invoke(this);
    }

    // -1 = 기본 지도만 표시
    public void ShowLayer(int index)
    {
        currentLayer = index;
        for (int i = 0; i < layerImages.Count; i++)
            layerImages[i].gameObject.SetActive(i == index);
    }

    public void ShowLayerForHeight(float height) => ShowLayer(config.GetLayerIndex(height));

    // 게임 좌표 → MapContent 기준 anchoredPosition (markerRoot 자식에 그대로 사용)
    public Vector2 GameToLocal(Vector3 gamePos)
    {
        Vector2 n = config.GameToNormalized(gamePos);
        Vector2 size = content.rect.size;
        return new Vector2((n.x - 0.5f) * size.x, (n.y - 0.5f) * size.y);
    }

    // 지도 전체가 Viewport에 들어오는 배율 (최소 줌)
    public float FitScale
    {
        get
        {
            Vector2 view = viewport.rect.size - Vector2.one * (fitPadding * 2f);
            Vector2 size = content.sizeDelta;
            return Mathf.Min(view.x / size.x, view.y / size.y);
        }
    }

    public void FitToViewport()
    {
        Canvas.ForceUpdateCanvases();
        float scale = FitScale;
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) return;   // Game 뷰가 없을 때 등

        content.localScale = new Vector3(scale, scale, 1f);
        content.anchoredPosition = Vector2.zero;
    }

    Image CreateLayerImage(MapLayer layer)
    {
        var go = new GameObject(layer.name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(layerRoot, false);

        // 층 이미지가 덮는 영역을 앵커로 지정 (기본은 지도 전체)
        Vector2 min = Vector2.zero, max = Vector2.one;
        if (layer.overrideBounds)
        {
            Vector2 a = config.GameToNormalized(new Vector3(layer.boundsA.x, 0, layer.boundsA.y));
            Vector2 b = config.GameToNormalized(new Vector3(layer.boundsB.x, 0, layer.boundsB.y));
            min = Vector2.Min(a, b);
            max = Vector2.Max(a, b);
        }
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.sprite = layer.sprite;
        img.raycastTarget = false;
        EditorPreview.MarkDontSave(go);
        return img;
    }

    void ClearLayers()
    {
        foreach (var img in layerImages)
            if (img != null) EditorPreview.Destroy(img.gameObject);
        layerImages.Clear();
        EditorPreview.ClearLeftovers(layerRoot);
    }
}
