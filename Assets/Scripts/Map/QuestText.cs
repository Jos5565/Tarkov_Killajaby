using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 퀘스트 목표 목록을 TMP 서식 문자열로 만든다 (툴팁, 도착 알림 공용).
//   - 목표 A          ← 강조할 목표(지금 위치/마커의 목표)는 금색 굵게
//                       마커가 선택(보조) 목표면 그 목표가 돕는 메인 목표를 강조
//   - 목표 B          ← 이 맵의 나머지 목표는 회색
//   외 24개 목표 (다른 맵/공통)   ← 다른 맵/맵 무관 목표는 개수만
public static class QuestText
{
    public static Color HighlightColor = new Color(0.765f, 0.694f, 0.565f);   // #C3B190
    public static Color NormalColor = new Color(0.7f, 0.7f, 0.7f);
    public static Color HiddenColor = new Color(0.45f, 0.45f, 0.45f);

    public static string Objectives(QuestData quest, ICollection<string> highlight, bool onlyThisMap = true)
    {
        if (quest == null || quest.objectives.Count == 0) return "";

        // 맵 구분 정보가 없는 예전 데이터면 전부 표시
        bool filter = onlyThisMap && quest.objectives.Any(o => o.thisMap);
        highlight = ToMainObjectives(quest, highlight);

        string hi = ColorUtility.ToHtmlStringRGB(HighlightColor);
        string normal = ColorUtility.ToHtmlStringRGB(NormalColor);
        var sb = new StringBuilder();
        int hidden = 0;
        foreach (QuestObjective o in quest.objectives)
        {
            if (filter && !o.thisMap)
            {
                hidden++;
                continue;
            }
            if (sb.Length > 0) sb.Append('\n');
            // 설명에 '<'가 들어 있어도 서식으로 해석되지 않게 noparse
            string text = $"<noparse>{o.description}</noparse>" + (o.optional ? " (선택)" : "");
            bool on = highlight != null && highlight.Contains(o.id);
            sb.Append(on ? $"<color=#{hi}><b>- {text}</b></color>" : $"<color=#{normal}>- {text}</color>");
        }

        if (hidden > 0)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(HiddenColor)}><size=85%>외 {hidden}개 목표 (다른 맵/공통)</size></color>");
        }
        return sb.ToString();
    }

    // 퀘스트에 필요한 아이템 (다른 맵 목표 포함 전부. 챙겨 가거나 모아야 하므로)
    //   필요 아이템
    //   [건네기] 물리 비트코인 ×2 · 인레이드
    //   [설치] MS2000 마커           ← 강조할 목표의 아이템은 금색
    //   [열쇠] 기숙사 314호 열쇠
    public static string Requirements(QuestData quest, ICollection<string> highlight)
    {
        if (quest == null || quest.requirements == null || quest.requirements.Count == 0) return "";
        highlight = ToMainObjectives(quest, highlight);

        string hi = ColorUtility.ToHtmlStringRGB(HighlightColor);
        string normal = ColorUtility.ToHtmlStringRGB(NormalColor);
        string dim = ColorUtility.ToHtmlStringRGB(HiddenColor);
        var sb = new StringBuilder($"<color=#{dim}><size=85%>필요 아이템</size></color>");
        foreach (QuestRequirement r in quest.requirements)
        {
            string items = string.Join(" / ", r.items);
            if (r.alternatives > r.items.Count) items += $" 외 {r.alternatives - r.items.Count}종";
            string text = $"<noparse>{items}</noparse>" + (r.count > 1 ? $" ×{r.count}" : "");
            string fir = r.foundInRaid ? $" <color=#{dim}>· 인레이드</color>" : "";
            bool on = highlight != null && !string.IsNullOrEmpty(r.objective) && highlight.Contains(r.objective);
            string label = $"[{KindLabel(r.kind)}] ";
            sb.Append('\n');
            sb.Append(on ? $"<color=#{hi}><b>{label}{text}</b></color>{fir}" : $"<color=#{normal}>{label}{text}</color>{fir}");
        }
        return sb.ToString();
    }

    static string KindLabel(string kind) => kind switch
    {
        "give" => "건네기",
        "find" => "찾기",
        "plant" => "설치",
        "sell" => "판매",
        "mark" => "표시",
        "key" => "열쇠",
        _ => kind,
    };

    // 선택 목표는 바로 앞의 메인 목표를 돕는 보조 단계로 나온다 (예: 총열 확보 → 작업장 찾기(선택)).
    // 강조 대상이 선택 목표면 그 메인 목표로 바꾼다: 앞쪽에서 먼저 찾고(이 맵 목표 우선), 없으면 뒤쪽에서.
    static HashSet<string> ToMainObjectives(QuestData quest, ICollection<string> ids)
    {
        var result = new HashSet<string>();
        if (ids == null) return result;

        List<QuestObjective> list = quest.objectives;
        foreach (string id in ids)
        {
            int i = list.FindIndex(o => o.id == id);
            if (i < 0 || !list[i].optional)
            {
                result.Add(id);
                continue;
            }

            QuestObjective main =
                Find(list, i, -1, o => !o.optional && o.thisMap) ??
                Find(list, i, -1, o => !o.optional) ??
                Find(list, i, +1, o => !o.optional && o.thisMap) ??
                Find(list, i, +1, o => !o.optional);
            result.Add(main != null ? main.id : id);
        }
        return result;
    }

    static QuestObjective Find(List<QuestObjective> list, int from, int step, System.Func<QuestObjective, bool> match)
    {
        for (int i = from + step; i >= 0 && i < list.Count; i += step)
            if (match(list[i])) return list[i];
        return null;
    }
}
