using UnityEngine;

// 타르코프와 같이 켜두는 앱이라 GPU 사용량을 제한하고,
// 게임 화면을 보는 동안(포커스 없음)에도 스크린샷 감지가 계속 돌도록 백그라운드 실행을 켠다.
public class AppBootstrap : MonoBehaviour
{
    public int targetFrameRate = 30;

    void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;
        Application.runInBackground = true;
    }
}
