using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// TMP 글자의 <link="id"> 부분을 클릭하면 onClick(id)을 부른다 (경로 1번 퀘스트 창의 목표 줄 클릭).
// 같은 오브젝트의 TMP_Text가 raycastTarget이어야 클릭을 받는다.
[RequireComponent(typeof(TMP_Text))]
public class TextLinkClick : MonoBehaviour, IPointerClickHandler
{
    public Action<string> onClick;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        var text = GetComponent<TMP_Text>();
        int index = TMP_TextUtilities.FindIntersectingLink(text, e.position, e.pressEventCamera);
        if (index >= 0 && index < text.textInfo.linkCount) onClick?.Invoke(text.textInfo.linkInfo[index].GetLinkID());
    }
}
