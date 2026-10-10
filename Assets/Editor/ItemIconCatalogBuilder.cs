using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Sprites/Items/{아이템 ID}.png 로 ItemIconCatalog(Resources/ItemIcons.asset)를 다시 만든다.
// TarkovApiWindow가 마커를 다시 만들 때도 호출한다.
public static class ItemIconCatalogBuilder
{
    const string IconDir = "Assets/Sprites/Items";
    const string AssetPath = "Assets/SO/Resources/" + ItemIconCatalog.ResourcePath + ".asset";

    [MenuItem("Tools/Tarkov/Item Icons 갱신")]
    public static void Build()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ItemIconCatalog>(AssetPath);
        if (catalog == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            catalog = ScriptableObject.CreateInstance<ItemIconCatalog>();
            AssetDatabase.CreateAsset(catalog, AssetPath);
        }

        catalog.icons = AssetDatabase.FindAssets("t:Sprite", new[] { IconDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(path => new ItemIconCatalog.Entry
            {
                id = Path.GetFileNameWithoutExtension(path),
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path),
            })
            .Where(e => e.sprite != null)
            .OrderBy(e => e.id)
            .ToList();

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ItemIcons] 아이템 이미지 {catalog.icons.Count}개");
    }
}
