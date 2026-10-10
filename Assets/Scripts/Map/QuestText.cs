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
            // 설명에 없는 조건 (헤드샷, 거리, 시간대, 무기, 장비)
            if (!string.IsNullOrEmpty(o.conditions))
                sb.Append(NewLine).Append($"<color=#{ColorUtility.ToHtmlStringRGB(HiddenColor)}><size=85%>   조건: <noparse>{o.conditions}</noparse></size></color>");
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
    // 같은 아이템을 여러 목표에서 요구하면 한 줄로 묶는다 (종류가 달라도: [건네기·설치]).
    // 예) "이거 혹시 패러디인가?": WI-FI 카메라 설치 목표 27개(맵 8곳) → 이 맵 ×3 · 전체 27개
    //     "위생 기준": 가스 분석기 건네기 2 + 설치 1 → ×3
    // 개수: 건네기·설치·판매·표시는 아이템을 하나씩 쓰므로 더하고, 찾기·열쇠는 그 아이템을 그대로
    //       건네거나 쓰는 것이라 더하지 않는다 (찾기 1 + 건네기 1 = 1개, 열쇠 + 건네기 = 1개)
    public class RequirementGroup
    {
        public QuestRequirement first;                     // 아이템 이름/ID
        public readonly List<string> kinds = new List<string>();
        public bool foundInRaid;
        public readonly HashSet<string> objectives = new HashSet<string>();
        readonly Dictionary<string, int> here = new Dictionary<string, int>();
        readonly Dictionary<string, int> total = new Dictionary<string, int>();

        public int thisMapCount => Combine(here);           // 이 맵 목표에 필요한 개수 (열쇠는 이 맵 것만 들어 있다)
        public int totalCount => Combine(total);
        public bool OnThisMap => thisMapCount > 0;

        public void Add(QuestRequirement r, bool onThisMap)
        {
            if (!kinds.Contains(r.kind)) kinds.Add(r.kind);
            foundInRaid |= r.foundInRaid;
            if (!string.IsNullOrEmpty(r.objective)) objectives.Add(r.objective);
            Count(total, r);
            if (onThisMap) Count(here, r);
        }

        static void Count(Dictionary<string, int> counts, QuestRequirement r)
        {
            counts.TryGetValue(r.kind, out int c);
            counts[r.kind] = c + r.count;
        }

        static int Combine(Dictionary<string, int> counts)
        {
            int used = counts.Where(kv => kv.Key != "find" && kv.Key != "key").Sum(kv => kv.Value);
            int find = counts.TryGetValue("find", out int f) ? f : 0;
            int key = counts.TryGetValue("key", out int k) ? k : 0;
            return Mathf.Max(used, Mathf.Max(find, key));
        }
    }

    public static List<RequirementGroup> GroupRequirements(QuestData quest)
    {
        var groups = new List<RequirementGroup>();
        if (quest?.requirements == null) return groups;

        var thisMapObjectives = new HashSet<string>(quest.objectives.Where(o => o.thisMap).Select(o => o.id));
        foreach (QuestRequirement r in quest.requirements)
        {
            string ids = string.Join(",", r.itemIds ?? new List<string>());
            RequirementGroup g = groups.Find(x => x.first.alternatives == r.alternatives &&
                                                  string.Join(",", x.first.itemIds ?? new List<string>()) == ids &&
                                                  x.first.items.SequenceEqual(r.items));
            if (g == null) groups.Add(g = new RequirementGroup { first = r });
            g.Add(r, string.IsNullOrEmpty(r.objective) || thisMapObjectives.Contains(r.objective));
        }
        // 이 맵에서 필요한 것 먼저 (같은 순서 유지)
        return groups.Where(g => g.OnThisMap).Concat(groups.Where(g => !g.OnThisMap)).ToList();
    }

    // "물리 비트코인 / 금목걸이 외 3종"
    public static string ItemNames(QuestRequirement r)
    {
        string items = string.Join(" / ", r.items);
        if (r.alternatives > r.items.Count) items += $" 외 {r.alternatives - r.items.Count}종";
        return items;
    }

    // 개수: 이 맵 ×3 · 전체 27개 / 다른 맵에서만 필요하면 ×24 (다른 맵)
    public static string CountText(RequirementGroup g, string dimHex)
    {
        if (!g.OnThisMap) return (g.totalCount > 1 ? $" ×{g.totalCount}" : "") + $" <color=#{dimHex}>(다른 맵)</color>";
        string text = g.thisMapCount > 1 ? $" ×{g.thisMapCount}" : "";
        if (g.totalCount > g.thisMapCount) text += $" <color=#{dimHex}>· 전체 {g.totalCount}개</color>";
        return text;
    }

    // 퀘스트에 필요한 아이템 (다른 맵 목표 포함. 챙겨 가거나 모아야 하므로)
    //   필요 아이템
    //   [건네기] 물리 비트코인 ×2 · 인레이드
    //   [설치] WI-FI 카메라 ×3 · 전체 27개   ← 강조할 목표의 아이템은 금색
    //   [열쇠] 기숙사 314호 열쇠
    public static string Requirements(QuestData quest, ICollection<string> highlight)
    {
        List<RequirementGroup> groups = GroupRequirements(quest);
        if (groups.Count == 0) return "";
        highlight = ToMainObjectives(quest, highlight);

        string hi = ColorUtility.ToHtmlStringRGB(HighlightColor);
        string normal = ColorUtility.ToHtmlStringRGB(NormalColor);
        string dim = ColorUtility.ToHtmlStringRGB(HiddenColor);
        var sb = new StringBuilder($"<color=#{dim}><size=85%>필요 아이템</size></color>");
        foreach (RequirementGroup g in groups)
        {
            string text = $"[{KindLabels(g)}] <noparse>{ItemNames(g.first)}</noparse>";
            string tail = CountText(g, dim) + (g.foundInRaid ? $" <color=#{dim}>· 인레이드</color>" : "");
            bool on = highlight != null && g.objectives.Overlaps(highlight);
            string color = on ? hi : g.OnThisMap ? normal : dim;
            sb.Append(NewLine);
            sb.Append(on ? $"<color=#{color}><b>{text}</b></color>{tail}" : $"<color=#{color}>{text}</color>{tail}");
        }
        return sb.ToString();
    }

    const char NewLine = (char)10;

    // [건네기·설치]
    public static string KindLabels(RequirementGroup g) => string.Join("·", g.kinds.Select(KindLabel));

    public static string KindLabel(string kind) => kind switch
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
