using UnityEngine;
using UnityEngine.SceneManagement;

// 파티(네트워크) 버전 스위치. 씬은 일반 버전과 같은 것을 쓰고, 파티 버전일 때만 파티 기능을 코드로 붙인다.
//   빌드  : Tools > Tarkov > Build > 파티 버전 이 TKJ_PARTY 정의 기호를 붙여 빌드한다 (일반 버전은 기호 없음)
//   에디터: Tools > Tarkov > Party > 에디터에서 파티 버전으로 실행 (에디터 설정에만 저장, 빌드에는 영향 없음)
// 파티 버전이면
//   로비(LobbyController가 있는 씬) → PartyLobbyPanel (오른쪽 위 파티 칸)
//   지도(MapView가 있는 씬)        → PartyMapView (왼쪽 아래 파티 패널 + 파티원 마커)
// 일반 버전에서는 아무것도 하지 않는다 (포트를 열거나 통신하지 않음).
public static class PartyEdition
{
    public const string EditorPrefKey = "TKJ.PartyEditionInEditor";

    public static bool Enabled
    {
        get
        {
#if TKJ_PARTY
            return true;
#elif UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false);
#else
            return false;
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (!Enabled) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PartySession.Ensure();

        var lobby = Object.FindAnyObjectByType<LobbyController>();
        if (lobby != null) Attach<PartyLobbyPanel>(lobby.GetComponentInParent<Canvas>());

        var map = Object.FindAnyObjectByType<MapView>();
        if (map != null) Attach<PartyMapView>(map.GetComponentInParent<Canvas>());
    }

    // 캔버스 맨 위(마커 툴팁보다는 아래)에 화면 전체 크기의 빈 오브젝트를 만들어 붙인다
    static void Attach<T>(Canvas canvas) where T : MonoBehaviour
    {
        if (canvas == null || Object.FindAnyObjectByType<T>() != null) return;
        var go = new GameObject(typeof(T).Name, typeof(RectTransform));
        go.SetActive(false);   // 자리를 잡은 뒤에 켜야 OnEnable에서 바로 그릴 수 있다
        go.transform.SetParent(canvas.rootCanvas.transform, false);
        go.AddComponent<T>();
        PartyUi.PlaceBelowTooltip(go.transform);
        go.SetActive(true);
    }
}
