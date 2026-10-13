using UnityEngine;
using UnityEngine.EventSystems;

// 마커에 마우스를 올리면 MapTooltip에 마커 정보를 띄우고, 마커를 다른 마커들 위로 올린다 (MapMarkerLayer가 붙여줌)
// 퀘스트 마커를 우클릭하면 그 목표를 완료 체크/해제한다 (QuestProgress). 다른 클릭은 지도(MapZoomPan)로 넘긴다.
public class MapMarkerInfo : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler
{
    public MapMarkerLayer layer;
    public MapMarker marker;
    public QuestData quest;   // 퀘스트 마커일 때 전체 목표

    public void OnPointerEnter(PointerEventData e)
    {
        if (layer != null) layer.Lift((RectTransform)transform);
        if (MapTooltip.Instance != null) MapTooltip.Instance.Show(marker, quest, e.position);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right && quest != null && !string.IsNullOrEmpty(marker.objective))
        {
            QuestProgress.Toggle(marker.key, marker.objective);
            if (MapTooltip.Instance != null) MapTooltip.Instance.Show(marker, quest, e.position);   // 취소선 바로 반영
            return;
        }
        // 이 컴포넌트가 클릭을 받으면 지도로 안 넘어가므로 직접 넘긴다 (더블클릭 화면 맞춤 등)
        if (transform.parent != null)
            ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, e, ExecuteEvents.pointerClickHandler);
    }

    public void OnPointerMove(PointerEventData e)
    {
        if (MapTooltip.Instance != null) MapTooltip.Instance.Move(e.position);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (layer != null) layer.Drop((RectTransform)transform);
        if (MapTooltip.Instance != null) MapTooltip.Instance.Hide();
    }

    void OnDisable()
    {
        if (layer != null) layer.DropLater((RectTransform)transform);
        // 마우스를 올린 채로 퀘스트를 끄면 툴팁이 남지 않게
        if (MapTooltip.Instance != null && MapTooltip.Instance.Current == marker) MapTooltip.Instance.Hide();
    }
}
