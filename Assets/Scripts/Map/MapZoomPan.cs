using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 지도 확대(마우스 휠, 커서 기준)와 이동(드래그). Viewport에 붙인다.
// Viewport에 raycastTarget이 켜진 Graphic(투명 Image)이 있어야 입력을 받는다.
// 내 위치(WhereIAM)를 지금 배율 그대로 화면 가운데로 부드럽게 옮긴다 (지도 끝에서는 이동 범위까지만).
// 지도 아래 가운데 버튼 (Play 중에만 만든다):
//   [내 위치로]       누르면 한 번 가운데로
//   [위치 자동갱신]   켜져 있으면(밝은 색) 위치가 갱신될 때마다 가운데로. 상태는 저장된다
[RequireComponent(typeof(RectTransform))]
public class MapZoomPan : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler, IPointerClickHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    public MapView mapView;
    public RectTransform content;

    [Tooltip("화면 맞춤 배율 대비 최대 확대 배율")]
    public float maxZoom = 8f;
    [Tooltip("휠 한 칸당 확대 비율")]
    public float zoomStep = 1.2f;
    [Tooltip("더블클릭으로 화면 맞춤")]
    public bool doubleClickToReset = true;

    [Header("Follow Player")]
    [Tooltip("비우면 씬에서 찾는다")]
    public WhereIAM whereIAM;
    [Tooltip("가운데로 옮기는 시간 (초). 0이면 바로")]
    public float centerDuration = 0.35f;

    [Header("Follow Buttons")]
    [Tooltip("비우면 [경로] 버튼의 글꼴을 쓴다")]
    public TMP_FontAsset font;
    public string centerLabel = "내 위치로";
    public string autoCenterLabel = "위치 자동갱신";
    public Vector2 buttonSize = new Vector2(150f, 36f);
    public float buttonFontSize = 18f;
    [Tooltip("지도 아래쪽에서 띄우는 거리")]
    public float bottomMargin = 16f;

    const string AutoCenterPref = "Map.AutoCenter";

    // 위치가 갱신되면 자동으로 가운데로 (모든 맵 공통으로 저장, 기본 켜짐)
    public bool AutoCenter
    {
        get => PlayerPrefs.GetInt(AutoCenterPref, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(AutoCenterPref, value ? 1 : 0);
            PlayerPrefs.Save();
            if (autoCenterButton != null) autoCenterButton.SetSelected(value);
        }
    }

    GameObject buttonBar;
    SelectButton autoCenterButton;

    // 지도 클릭(드래그 아님). true를 돌려주면 처리된 것으로 보고 더블클릭 맞춤을 하지 않는다 (예: 탈출구 이름표 클릭)
    public event System.Func<PointerEventData, bool> Clicking;

    RectTransform viewport;
    Vector2 lastDragPoint;

    // 가운데로 옮기는 중 (드래그·휠을 하면 멈춘다)
    bool centering;
    Vector2 centerFrom, centerTo;
    float centerTime;

    // 마우스가 지도 위에 있는지 (필터 패널, 퀘스트 목록 등 다른 UI 위면 false)
    public bool IsPointerInside { get; private set; }
    public Camera EventCamera { get; private set; }

    public void OnPointerEnter(PointerEventData e)
    {
        IsPointerInside = true;
        EventCamera = e.enterEventCamera;
    }

    public void OnPointerExit(PointerEventData e) => IsPointerInside = false;

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (whereIAM == null) whereIAM = FindAnyObjectByType<WhereIAM>(FindObjectsInactive.Include);
        if (whereIAM != null) whereIAM.PoseChanged += OnPoseChanged;
        BuildButtons();
    }

    void OnDisable()
    {
        IsPointerInside = false;
        centering = false;
        if (whereIAM != null) whereIAM.PoseChanged -= OnPoseChanged;
        if (buttonBar != null) Destroy(buttonBar);
        buttonBar = null;
        autoCenterButton = null;
    }

    void OnPoseChanged(WhereIAM w)
    {
        if (AutoCenter) CenterOn(w.GamePosition);
    }

    // [내 위치로] 버튼
    public void CenterOnPlayer()
    {
        if (whereIAM != null && whereIAM.HasPosition) CenterOn(whereIAM.GamePosition);
    }

    // 게임 좌표를 화면(Viewport) 가운데로. 배율은 그대로
    public void CenterOn(Vector3 gamePosition)
    {
        if (mapView == null || !mapView.IsLoaded || content == null) return;
        Vector2 target = -mapView.GameToLocal(gamePosition) * content.localScale.x;
        centerTo = ClampPosition(target);
        if (centerDuration <= 0f)
        {
            content.anchoredPosition = centerTo;
            centering = false;
            return;
        }
        centerFrom = content.anchoredPosition;
        centerTime = 0f;
        centering = true;
    }

    void Update()
    {
        if (!centering) return;
        centerTime += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(centerTime / centerDuration);
        t = 1f - (1f - t) * (1f - t) * (1f - t);   // 끝에서 천천히
        content.anchoredPosition = Vector2.LerpUnclamped(centerFrom, centerTo, t);
        if (t >= 1f) centering = false;
    }

    float MinScale => mapView.FitScale;
    float MaxScale => MinScale * maxZoom;

    void Awake() => viewport = (RectTransform)transform;

    public void OnScroll(PointerEventData e)
    {
        if (Mathf.Approximately(e.scrollDelta.y, 0f)) return;
        centering = false;

        float oldScale = content.localScale.x;
        float newScale = Mathf.Clamp(oldScale * Mathf.Pow(zoomStep, Mathf.Sign(e.scrollDelta.y)), MinScale, MaxScale);
        if (Mathf.Approximately(oldScale, newScale)) return;

        // 커서 아래 지점이 확대 후에도 같은 화면 위치에 있도록 보정
        if (TryGetLocalPoint(e, out Vector2 cursor))
        {
            Vector2 pos = content.anchoredPosition;
            content.anchoredPosition = cursor - (cursor - pos) * (newScale / oldScale);
        }

        content.localScale = new Vector3(newScale, newScale, 1f);
        Clamp();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        centering = false;
        TryGetLocalPoint(e, out lastDragPoint);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!TryGetLocalPoint(e, out Vector2 point)) return;

        content.anchoredPosition += point - lastDragPoint;
        lastDragPoint = point;
        Clamp();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.dragging) return;
        if (Clicking != null && Clicking(e)) return;
        if (doubleClickToReset && e.clickCount == 2)
            ResetView();
    }

    public void ResetView()
    {
        centering = false;
        mapView.FitToViewport();
    }

    // 창 크기가 바뀌면 최소 배율과 이동 범위를 다시 맞춘다
    void OnRectTransformDimensionsChange()
    {
        if (viewport == null || mapView == null || content == null) return;
        if (content.sizeDelta.x <= 0f || content.sizeDelta.y <= 0f) return;   // 지도 로드 전

        float scale = Mathf.Clamp(content.localScale.x, MinScale, MaxScale);
        content.localScale = new Vector3(scale, scale, 1f);
        Clamp();
    }

    // 지도 밖으로 끌려나가지 않게 제한. 지도가 Viewport보다 작은 축은 가운데 고정.
    void Clamp() => content.anchoredPosition = ClampPosition(content.anchoredPosition);

    Vector2 ClampPosition(Vector2 pos)
    {
        Vector2 half = viewport.rect.size * 0.5f;
        Vector2 contentHalf = content.sizeDelta * content.localScale.x * 0.5f;
        // 확대 시에도 지도 가장자리 바깥으로 fitPadding만큼 더 끌 수 있게 한다
        Vector2 limit = Vector2.Max(contentHalf - half + Vector2.one * mapView.fitPadding, Vector2.zero);

        pos.x = Mathf.Clamp(pos.x, -limit.x, limit.x);
        pos.y = Mathf.Clamp(pos.y, -limit.y, limit.y);
        return pos;
    }

    // ---- 버튼 ----

    void BuildButtons()
    {
        if (buttonBar != null) return;
        if (font == null)
        {
            var route = FindAnyObjectByType<RouteToggleButton>(FindObjectsInactive.Include);
            if (route != null) font = route.font;
        }

        // 지도 아래 가운데: [내 위치로] [위치 자동갱신]
        buttonBar = new GameObject("FollowButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var bar = (RectTransform)buttonBar.transform;
        bar.SetParent(transform, false);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, bottomMargin);
        var layout = buttonBar.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = buttonBar.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        SelectButton center = SelectButton.Create(bar, centerLabel, null, false, buttonFontSize, buttonSize, true,
            font, SelectButton.DefaultNormal, SelectButton.DefaultSelected, SelectButton.DefaultIconBox);
        center.Button.onClick.AddListener(CenterOnPlayer);

        autoCenterButton = SelectButton.Create(bar, autoCenterLabel, null, false, buttonFontSize, buttonSize, true,
            font, SelectButton.DefaultNormal, SelectButton.DefaultSelected, SelectButton.DefaultIconBox);
        autoCenterButton.Button.onClick.AddListener(() => AutoCenter = !AutoCenter);
        autoCenterButton.SetSelected(AutoCenter);

        buttonBar.transform.SetAsLastSibling();   // 지도 위에 그린다
    }

    // 화면 좌표 → Viewport 중심 기준 로컬 좌표 (content.anchoredPosition과 같은 공간)
    bool TryGetLocalPoint(PointerEventData e, out Vector2 point)
    {
        bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, e.position, e.pressEventCamera, out point);
        point -= viewport.rect.center;
        return ok;
    }
}
