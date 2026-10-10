using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 마우스 올림/움직임/뗌을 콜백으로 넘기는 작은 컴포넌트 (퀘스트 목록 행 호버용)
public class PointerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public Action<PointerEventData> enter, move, exit;

    public void OnPointerEnter(PointerEventData e) => enter?.Invoke(e);
    public void OnPointerMove(PointerEventData e) => move?.Invoke(e);
    public void OnPointerExit(PointerEventData e) => exit?.Invoke(e);
    void OnDisable() => exit?.Invoke(null);
}
