using System;
using System.Collections.Generic;
using UnityEngine;

// 맵 하나에 대한 데이터. 새 맵은 이 에셋만 추가하면 된다.
[CreateAssetMenu(fileName = "MapConfig", menuName = "Tarkov/Map Config")]
public class MapConfig : ScriptableObject
{
    [Header("API")]
    public string normalizedName = "customs";   // tarkov.dev normalizedName
    public string displayName = "Customs";

    [Header("Lobby")]
    [Tooltip("로비에서 이 맵을 고르면 불러올 씬 (Build Settings에 등록 필요)")]
    public string sceneName;
    public Sprite icon;

    [Header("Coordinates (game x, z)")]
    public Vector2 boundsA = new Vector2(698, -307);
    public Vector2 boundsB = new Vector2(-371, 237);
    [Tooltip("tarkov.dev coordinateRotation (0 / 90 / 180 / 270)")]
    public int coordinateRotation = 180;

    [Header("Images")]
    public Sprite baseSprite;
    public List<MapLayer> layers = new List<MapLayer>();

    [Header("API Data")]
    [Tooltip("Tools > Tarkov > API Data 에서 받으면 자동으로 연결된다")]
    public MapMarkerData markerData;

    // 게임 좌표 → 지도 이미지 기준 0~1 (u: 왼→오, v: 아래→위). 범위 밖이면 0~1을 벗어난다.
    public Vector2 GameToNormalized(Vector3 gamePos) => GameToNormalized(gamePos, boundsA, boundsB);

    public Vector2 GameToNormalized(Vector3 gamePos, Vector2 a, Vector2 b)
    {
        Vector2 p = Rotate(new Vector2(gamePos.x, gamePos.z));
        Vector2 ra = Rotate(a), rb = Rotate(b);

        float minX = Mathf.Min(ra.x, rb.x), maxX = Mathf.Max(ra.x, rb.x);
        float minY = Mathf.Min(ra.y, rb.y), maxY = Mathf.Max(ra.y, rb.y);

        return new Vector2((p.x - minX) / (maxX - minX), (p.y - minY) / (maxY - minY));
    }

    public bool IsInBounds(Vector3 gamePos)
    {
        Vector2 n = GameToNormalized(gamePos);
        return n.x >= 0f && n.x <= 1f && n.y >= 0f && n.y <= 1f;
    }

    // 높이(y)에 해당하는 층 인덱스. 없으면 -1 (기본 지도).
    public int GetLayerIndex(float height)
    {
        for (int i = 0; i < layers.Count; i++)
            if (height >= layers[i].heightRange.x && height < layers[i].heightRange.y)
                return i;
        return -1;
    }

    Vector2 Rotate(Vector2 v)
    {
        float rad = coordinateRotation * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}

[Serializable]
public class MapLayer
{
    public string name;
    public Sprite sprite;
    [Tooltip("이 층으로 판정할 높이(y) 범위: x 이상, y 미만")]
    public Vector2 heightRange;

    [Tooltip("층 이미지가 기본 지도와 다른 영역을 덮을 때 체크")]
    public bool overrideBounds;
    public Vector2 boundsA;
    public Vector2 boundsB;
}
