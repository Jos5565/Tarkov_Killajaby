using UnityEngine;

// 길라잡이 창을 보고 있으면 기본 프레임, 다른 창(게임)을 보고 있으면 낮은 프레임.
// AppBootstrap이 한 번 만들고, 씬이 바뀌어도 유지된다 (로비 UI와는 별개의 오브젝트).
public class FrameRateGovernor : MonoBehaviour
{
    static FrameRateGovernor instance;

    int focusedRate = 30;
    int backgroundRate = 10;

    public static void Ensure(int focusedRate, int backgroundRate)
    {
        if (instance == null)
        {
            var go = new GameObject(nameof(FrameRateGovernor));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FrameRateGovernor>();
        }
        instance.focusedRate = Mathf.Max(1, focusedRate);
        instance.backgroundRate = Mathf.Max(1, backgroundRate);
        instance.Apply(Application.isFocused);
    }

    void OnApplicationFocus(bool focused) => Apply(focused);

    void Apply(bool focused) => Application.targetFrameRate = focused ? focusedRate : backgroundRate;
}
