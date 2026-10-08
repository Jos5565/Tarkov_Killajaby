using UnityEngine;

// [ExecuteAlways] 컴포넌트가 편집 모드(Play 전)에서 미리보기 오브젝트를 만들 때 사용.
// 편집 모드에서 만든 오브젝트는 DontSaveInEditor로 표시해 씬 파일에 저장되지 않게 한다.
public static class EditorPreview
{
    const HideFlags PreviewFlags = HideFlags.DontSaveInEditor;

    public static bool IsEditMode => !Application.isPlaying;

    public static void MarkDontSave(GameObject go)
    {
        if (!IsEditMode) return;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags |= PreviewFlags;
    }

    public static void Destroy(Object obj)
    {
        if (obj == null) return;
        if (IsEditMode) Object.DestroyImmediate(obj);
        else Object.Destroy(obj);
    }

    // OnDisable 안에서 사용. 비활성화 도중 DestroyImmediate는 오류가 나므로 편집 모드에서는 다음 틱에 지운다.
    public static void DestroyLater(Object obj)
    {
        if (obj == null) return;
#if UNITY_EDITOR
        if (IsEditMode)
        {
            UnityEditor.EditorApplication.delayCall += () => { if (obj != null) Object.DestroyImmediate(obj); };
            return;
        }
#endif
        Object.Destroy(obj);
    }

    // 스크립트 재컴파일 등으로 참조를 잃은 미리보기 오브젝트 정리
    public static void ClearLeftovers(Transform parent)
    {
        if (!IsEditMode || parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            if ((child.hideFlags & PreviewFlags) != 0) Object.DestroyImmediate(child);
        }
    }
}
