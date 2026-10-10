using System;
using System.Collections.Generic;
using UnityEngine;

// 아이템 ID → 아이템 이미지(Sprite) 목록. 이미지는 Sprites/Items/{아이템 ID}.png (tarkov.dev base-image).
// Resources/ItemIcons.asset 으로 두고 처음 필요할 때 불러온다 (Tools > Tarkov > Item Icons 갱신이 목록을 만든다).
public class ItemIconCatalog : ScriptableObject
{
    public const string ResourcePath = "ItemIcons";

    [Serializable]
    public struct Entry
    {
        public string id;
        public Sprite sprite;
    }

    public List<Entry> icons = new List<Entry>();

    Dictionary<string, Sprite> lookup;

    static ItemIconCatalog instance;
    static bool loaded;

    // 없으면 null (목록 에셋이 없거나 이미지가 없는 아이템)
    public static Sprite Get(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        if (!loaded)
        {
            loaded = true;
            instance = Resources.Load<ItemIconCatalog>(ResourcePath);
        }
        if (instance == null) return null;

        if (instance.lookup == null)
        {
            instance.lookup = new Dictionary<string, Sprite>(instance.icons.Count);
            foreach (Entry e in instance.icons)
                if (!string.IsNullOrEmpty(e.id) && e.sprite != null) instance.lookup[e.id] = e.sprite;
        }
        return instance.lookup.TryGetValue(itemId, out Sprite sprite) ? sprite : null;
    }
}
