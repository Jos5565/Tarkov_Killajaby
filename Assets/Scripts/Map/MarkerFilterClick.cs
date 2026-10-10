using System;
using UnityEngine;
using UnityEngine.EventSystems;

// MarkerFilterPanel/QuestListPanel이 만드는 행/화살표의 클릭 처리 (왼쪽 클릭, 우클릭 따로)
public class MarkerFilterClick : MonoBehaviour, IPointerClickHandler
{
    public Action onClick;
    public Action onRightClick;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right) onRightClick?.Invoke();
        else if (eventData.button == PointerEventData.InputButton.Left) onClick?.Invoke();
    }
}
