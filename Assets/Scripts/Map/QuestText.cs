using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 퀘스트 목표 목록을 TMP 서식 문자열로 만든다 (툴팁, 도착 알림 공용).
//   - 목표 A          ← 강조할 목표(지금 위치/마커의 목표)는 금색 굵게
//   - 목표 B          ← 나머지는 회색
public static class QuestText
{
    public static Color HighlightColor = new Color(0.765f, 0.694f, 0.565f);   // #C3B190
    public static Color NormalColor = new Color(0.7f, 0.7f, 0.7f);

    public static string Objectives(QuestData quest, ICollection<string> highlight)
    {
        if (quest == null || quest.objectives.Count == 0) return "";

        string hi = ColorUtility.ToHtmlStringRGB(HighlightColor);
        string normal = ColorUtility.ToHtmlStringRGB(NormalColor);
        var sb = new StringBuilder();
        foreach (QuestObjective o in quest.objectives)
        {
            if (sb.Length > 0) sb.Append('\n');
            // 설명에 '<'가 들어 있어도 서식으로 해석되지 않게 noparse
            string text = $"<noparse>{o.description}</noparse>" + (o.optional ? " (선택)" : "");
            bool on = highlight != null && highlight.Contains(o.id);
            sb.Append(on ? $"<color=#{hi}><b>- {text}</b></color>" : $"<color=#{normal}>- {text}</color>");
        }
        return sb.ToString();
    }
}
