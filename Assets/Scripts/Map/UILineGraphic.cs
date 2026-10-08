using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 점들을 잇는 UGUI 선 (루트 표시용). 좌표는 이 RectTransform의 로컬 좌표(피벗 기준).
public class UILineGraphic : MaskableGraphic
{
    readonly List<Vector2> points = new List<Vector2>();
    float thickness = 4f;

    public void SetPoints(IEnumerable<Vector2> newPoints)
    {
        points.Clear();
        points.AddRange(newPoints);
        SetVerticesDirty();
    }

    public void SetThickness(float value)
    {
        if (Mathf.Approximately(thickness, value)) return;
        thickness = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float half = thickness * 0.5f;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 1e-6f) continue;
            dir.Normalize();

            // 양 끝을 반 두께만큼 늘려 꺾이는 곳의 틈을 메운다
            a -= dir * half;
            b += dir * half;
            Vector2 n = new Vector2(-dir.y, dir.x) * half;

            int start = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero);
            vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero);
            vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
