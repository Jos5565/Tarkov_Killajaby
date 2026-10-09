using UnityEngine;
using UnityEngine.EventSystems;

// 마커에 마우스를 올리면 MapTooltip에 마커 정보를 띄우고, 마커를 다른 마커들 위로 올린다 (MapMarkerLayer가 붙여줌)
public class MapMarkerInfo : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public MapMarkerLayer layer;
    public MapMarker marker;
    public QuestData quest;   // 퀘스트 마커일 때 전체 목표

    public void OnPointerEnter(PointerEventData e)
    {
        if (layer != null) layer.Lift((RectTransform)transform);
        if (MapTooltip.Instance != null) MapTooltip.Instance.Show(marker, quest, e.position);
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
