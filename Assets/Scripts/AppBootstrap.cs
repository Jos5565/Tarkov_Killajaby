using UnityEngine;

// 타르코프와 같이 켜두는 앱이라 GPU 사용량을 제한하고,
// 게임 화면을 보는 동안(포커스 없음)에도 스크린샷 감지가 계속 돌도록 백그라운드 실행을 켠다.
// 게임 창을 보고 있는 동안(길라잡이 포커스 없음)은 프레임을 더 낮춘다 (FrameRateGovernor, 씬이 바뀌어도 유지).
public class AppBootstrap : MonoBehaviour
{
    [Tooltip("길라잡이 창을 보고 있을 때 프레임")]
    public int targetFrameRate = 30;
    [Tooltip("다른 창(게임)을 보고 있을 때 프레임. 위치 갱신은 스크린샷마다라서 낮아도 된다")]
    public int backgroundFrameRate = 10;

    void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.runInBackground = true;
        FrameRateGovernor.Ensure(targetFrameRate, backgroundFrameRate);
    }
}
