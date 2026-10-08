using UnityEngine;
using UnityEngine.EventSystems;

// 지도 확대(마우스 휠, 커서 기준)와 이동(드래그). Viewport에 붙인다.
// Viewport에 raycastTarget이 켜진 Graphic(투명 Image)이 있어야 입력을 받는다.
[RequireComponent(typeof(RectTransform))]
public class MapZoomPan : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler, IPointerClickHandler
{
    public MapView mapView;
    public RectTransform content;

    [Tooltip("화면 맞춤 배율 대비 최대 확대 배율")]
    public float maxZoom = 8f;
    [Tooltip("휠 한 칸당 확대 비율")]
    public float zoomStep = 1.2f;
    [Tooltip("더블클릭으로 화면 맞춤")]
    public bool doubleClickToReset = true;

    RectTransform viewport;
    Vector2 lastDragPoint;

    float MinScale => mapView.FitScale;
    float MaxScale => MinScale * maxZoom;

    void Awake() => viewport = (RectTransform)transform;

    public void OnScroll(PointerEventData e)
    {
        if (Mathf.Approximately(e.scrollDelta.y, 0f)) return;

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
        if (doubleClickToReset && e.clickCount == 2 && !e.dragging)
            ResetView();
    }

    public void ResetView() => mapView.FitToViewport();

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
    void Clamp()
    {
        Vector2 half = viewport.rect.size * 0.5f;
        Vector2 contentHalf = content.sizeDelta * content.localScale.x * 0.5f;
        // 확대 시에도 지도 가장자리 바깥으로 fitPadding만큼 더 끌 수 있게 한다
        Vector2 limit = Vector2.Max(contentHalf - half + Vector2.one * mapView.fitPadding, Vector2.zero);

        Vector2 pos = content.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, -limit.x, limit.x);
        pos.y = Mathf.Clamp(pos.y, -limit.y, limit.y);
        content.anchoredPosition = pos;
    }

    // 화면 좌표 → Viewport 중심 기준 로컬 좌표 (content.anchoredPosition과 같은 공간)
    bool TryGetLocalPoint(PointerEventData e, out Vector2 point)
    {
        bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, e.position, e.pressEventCamera, out point);
        point -= viewport.rect.center;
        return ok;
    }
}
