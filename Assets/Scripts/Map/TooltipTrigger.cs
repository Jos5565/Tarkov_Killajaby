using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 마우스를 올리면 MapTooltip에 글을 띄운다 (마커가 아닌 UI용, 예: 로비 맵 버튼).
// 내용은 마우스를 올릴 때마다 content()로 만든다.
public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public Func<(string title, string sub, string body)> content;

    public void OnPointerEnter(PointerEventData e)
    {
        if (MapTooltip.Instance == null || content == null) return;
        var (title, sub, body) = content();
        MapTooltip.Instance.Show(title, sub, body, e.position);
    }

    public void OnPointerMove(PointerEventData e)
    {
        if (MapTooltip.Instance != null) MapTooltip.Instance.Move(e.position);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (MapTooltip.Instance != null) MapTooltip.Instance.Hide();
    }

    void OnDisable()
    {
        if (MapTooltip.Instance != null) MapTooltip.Instance.Hide();
    }
}
