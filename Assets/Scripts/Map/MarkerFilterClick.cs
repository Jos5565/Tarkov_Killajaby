using System;
using UnityEngine;
using UnityEngine.EventSystems;

// MarkerFilterPanel이 만드는 행/화살표의 클릭 처리
public class MarkerFilterClick : MonoBehaviour, IPointerClickHandler
{
    public Action onClick;

    public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke();
}
