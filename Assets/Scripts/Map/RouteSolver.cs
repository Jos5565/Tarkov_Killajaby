using System;
using System.Collections.Generic;
using UnityEngine;

// 퀘스트 루트 계산 (직선거리).
// 각 목표(cluster)는 후보 위치가 여러 개일 수 있고 그중 한 곳만 방문하면 된다.
// 출발점(start)에서 시작해 모든 목표를 한 번씩 방문하고 마지막 목표에서 끝나는 최단 경로를 찾는다.
// 도착점(end)이 있으면 마지막 목표에서 도착점(탈출구)까지의 거리도 포함해 순서를 정한다.
//   - 목표 수가 적으면 Held-Karp(비트마스크 DP)로 정확한 최단 경로
//   - 많으면 가까운 곳부터 잇기(greedy) + 2-opt 개선 (후보 위치는 순서마다 DP로 최적 선택)
public static class RouteSolver
{
    public struct Result
    {
        public int[] clusterOrder;   // 방문 순서 (cluster 인덱스)
        public int[] pointIndex;     // 각 순서에서 고른 후보 위치 인덱스 (clusters[c][pointIndex])
        public float length;
    }

    // start: 없으면 null (어디서 시작해도 됨). end: 없으면 null (마지막 목표에서 끝남)
    public static Result Solve(IReadOnlyList<IReadOnlyList<Vector2>> clusters, Vector2? start, Vector2? end = null, int exactLimit = 12, int maxExactStates = 2_000_000)
    {
        int n = clusters.Count;
        if (n == 0)
        {
            float direct = start.HasValue && end.HasValue ? Vector2.Distance(start.Value, end.Value) : 0f;
            return new Result { clusterOrder = Array.Empty<int>(), pointIndex = Array.Empty<int>(), length = direct };
        }

        int pointCount = 0;
        foreach (var c in clusters) pointCount += c.Count;

        if (n <= exactLimit && (long)(1 << n) * pointCount <= maxExactStates)
            return SolveExact(clusters, start, end, pointCount);
        return SolveHeuristic(clusters, start, end);
    }

    // ---- 정확한 해: dp[mask][p] = mask의 목표들을 방문하고 후보 p에서 끝나는 최소 거리 ----
    static Result SolveExact(IReadOnlyList<IReadOnlyList<Vector2>> clusters, Vector2? start, Vector2? end, int pointCount)
    {
        int n = clusters.Count;
        var pos = new Vector2[pointCount];
        var owner = new int[pointCount];
        var local = new int[pointCount];
        for (int c = 0, k = 0; c < n; c++)
            for (int i = 0; i < clusters[c].Count; i++, k++)
            {
                pos[k] = clusters[c][i];
                owner[k] = c;
                local[k] = i;
            }

        int masks = 1 << n;
        var dp = new float[masks * pointCount];
        var parent = new int[masks * pointCount];
        for (int i = 0; i < dp.Length; i++) dp[i] = float.PositiveInfinity;

        for (int p = 0; p < pointCount; p++)
        {
            int idx = (1 << owner[p]) * pointCount + p;
            dp[idx] = start.HasValue ? Vector2.Distance(start.Value, pos[p]) : 0f;
            parent[idx] = -1;
        }

        for (int mask = 1; mask < masks; mask++)
            for (int p = 0; p < pointCount; p++)
            {
                if ((mask & (1 << owner[p])) == 0) continue;
                float cost = dp[mask * pointCount + p];
                if (float.IsPositiveInfinity(cost)) continue;

                for (int q = 0; q < pointCount; q++)
                {
                    int bit = 1 << owner[q];
                    if ((mask & bit) != 0) continue;
                    int next = (mask | bit) * pointCount + q;
                    float c = cost + Vector2.Distance(pos[p], pos[q]);
                    if (c < dp[next])
                    {
                        dp[next] = c;
                        parent[next] = p;
                    }
                }
            }

        int full = masks - 1, last = -1;
        float best = float.PositiveInfinity;
        for (int p = 0; p < pointCount; p++)
        {
            float c = dp[full * pointCount + p] + (end.HasValue ? Vector2.Distance(pos[p], end.Value) : 0f);
            if (c < best)
            {
                best = c;
                last = p;
            }
        }

        var order = new int[n];
        var chosen = new int[n];
        int m = full;
        for (int i = n - 1, p = last; i >= 0; i--)
        {
            order[i] = owner[p];
            chosen[i] = local[p];
            int prev = parent[m * pointCount + p];
            m &= ~(1 << owner[p]);
            p = prev;
        }
        return new Result { clusterOrder = order, pointIndex = chosen, length = best };
    }

    // ---- 근사 해 ----
    static Result SolveHeuristic(IReadOnlyList<IReadOnlyList<Vector2>> clusters, Vector2? start, Vector2? end)
    {
        int n = clusters.Count;

        // 1) greedy: 현재 위치에서 가장 가까운 미방문 목표로. 출발점이 없으면 시작 목표를 바꿔가며 가장 짧은 것
        int[] bestOrder = null;
        float bestLen = float.PositiveInfinity;
        int tries = start.HasValue ? 1 : n;
        for (int first = 0; first < tries; first++)
        {
            var order = Greedy(clusters, start, start.HasValue ? -1 : first);
            float len = BestPoints(clusters, start, end, order, null);
            if (len < bestLen)
            {
                bestLen = len;
                bestOrder = order;
            }
        }

        // 2) 개선: 짧아지면 채택 (후보 위치는 매번 다시 최적 선택)
        //    2-opt  : 구간 [i..j]를 뒤집기
        //    Or-opt : 목표 하나를 다른 순서로 옮기기
        bool improved = true;
        for (int pass = 0; improved && pass < 50; pass++)
        {
            improved = false;
            for (int i = 0; i < n - 1; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var candidate = (int[])bestOrder.Clone();
                    Array.Reverse(candidate, i, j - i + 1);
                    TryAccept(candidate);
                }

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    var list = new List<int>(bestOrder);
                    int c = list[i];
                    list.RemoveAt(i);
                    list.Insert(j, c);
                    TryAccept(list.ToArray());
                }
        }

        void TryAccept(int[] candidate)
        {
            float len = BestPoints(clusters, start, end, candidate, null);
            if (len + 1e-3f < bestLen)
            {
                bestLen = len;
                bestOrder = candidate;
                improved = true;
            }
        }

        var chosen = new int[n];
        bestLen = BestPoints(clusters, start, end, bestOrder, chosen);
        return new Result { clusterOrder = bestOrder, pointIndex = chosen, length = bestLen };
    }

    static int[] Greedy(IReadOnlyList<IReadOnlyList<Vector2>> clusters, Vector2? start, int firstCluster)
    {
        int n = clusters.Count;
        var visited = new bool[n];
        var order = new List<int>(n);
        Vector2 cur = start ?? Vector2.zero;

        if (firstCluster >= 0)
        {
            visited[firstCluster] = true;
            order.Add(firstCluster);
            cur = clusters[firstCluster][0];
        }

        while (order.Count < n)
        {
            int bestC = -1, bestP = 0;
            float best = float.PositiveInfinity;
            for (int c = 0; c < n; c++)
            {
                if (visited[c]) continue;
                for (int i = 0; i < clusters[c].Count; i++)
                {
                    float d = Vector2.Distance(cur, clusters[c][i]);
                    if (d < best)
                    {
                        best = d;
                        bestC = c;
                        bestP = i;
                    }
                }
            }
            visited[bestC] = true;
            order.Add(bestC);
            cur = clusters[bestC][bestP];
        }
        return order.ToArray();
    }

    // 방문 순서가 정해졌을 때 각 목표의 후보 위치를 고르는 최단 경로 (층별 DP). chosen이 있으면 선택 결과를 채운다.
    static float BestPoints(IReadOnlyList<IReadOnlyList<Vector2>> clusters, Vector2? start, Vector2? end, int[] order, int[] chosen)
    {
        int n = order.Length;
        var cost = new float[n][];
        var from = new int[n][];

        for (int layer = 0; layer < n; layer++)
        {
            var pts = clusters[order[layer]];
            cost[layer] = new float[pts.Count];
            from[layer] = new int[pts.Count];

            for (int i = 0; i < pts.Count; i++)
            {
                if (layer == 0)
                {
                    cost[0][i] = start.HasValue ? Vector2.Distance(start.Value, pts[i]) : 0f;
                    continue;
                }

                var prev = clusters[order[layer - 1]];
                float best = float.PositiveInfinity;
                for (int j = 0; j < prev.Count; j++)
                {
                    float c = cost[layer - 1][j] + Vector2.Distance(prev[j], pts[i]);
                    if (c < best)
                    {
                        best = c;
                        from[layer][i] = j;
                    }
                }
                cost[layer][i] = best;
            }
        }

        var lastPts = clusters[order[n - 1]];
        int last = 0;
        float total = float.PositiveInfinity;
        for (int i = 0; i < lastPts.Count; i++)
        {
            float c = cost[n - 1][i] + (end.HasValue ? Vector2.Distance(lastPts[i], end.Value) : 0f);
            if (c < total)
            {
                total = c;
                last = i;
            }
        }

        if (chosen != null)
            for (int layer = n - 1, p = last; layer >= 0; layer--)
            {
                chosen[layer] = p;
                if (layer > 0) p = from[layer][p];
            }
        return total;
    }
}
