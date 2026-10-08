using UnityEngine;

public class Map : MonoBehaviour
{
     public Vector2 boundsA = new Vector2(698, -307);   // (x, z)
    public Vector2 boundsB = new Vector2(-371, 237);

    [ContextMenu("Apply Bounds")]
    void Apply()
    {
        float minX = Mathf.Min(boundsA.x, boundsB.x), maxX = Mathf.Max(boundsA.x, boundsB.x);
        float minZ = Mathf.Min(boundsA.y, boundsB.y), maxZ = Mathf.Max(boundsA.y, boundsB.y);

        float w = maxX - minX;   // 1069
        float h = maxZ - minZ;   // 544

        transform.position   = new Vector3((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);
        transform.localScale = new Vector3(w / 10f, 1f, h / 10f);
    }

    void Start() => Apply();
}

