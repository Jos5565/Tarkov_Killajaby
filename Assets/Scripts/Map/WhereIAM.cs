using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

// 타르코프 스크린샷 파일명에서 위치/방향을 읽어 지도 위에 자신(마커)을 배치한다.
// 파일명 형식: 2026-09-23[17-56]_120.79, 2.83, -73.11_0.01954, 0.75538, -0.02240, 0.65462_17.59 (0).png
//                              ^ 위치 x, y, z           ^ 회전 쿼터니언 x, y, z, w
// MapContent/Markers 아래에 두고, 위쪽(+Y)이 정면인 마커 그래픽을 자식으로 둔다.
// ExecuteAlways: 편집 모드에서도 테스트 이미지 위치를 미리 보여준다.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class WhereIAM : MonoBehaviour
{
    static readonly Regex FileNamePattern = new Regex(
        @"_(?<px>-?\d+(\.\d+)?), (?<py>-?\d+(\.\d+)?), (?<pz>-?\d+(\.\d+)?)_(?<qx>-?\d+(\.\d+)?), (?<qy>-?\d+(\.\d+)?), (?<qz>-?\d+(\.\d+)?), (?<qw>-?\d+(\.\d+)?)_");

    public MapView mapView;

    [Header("Test")]
    [Tooltip("시작 시 이 이미지의 파일명으로 위치를 잡는다")]
    public Texture2D testImage;

    [Header("Options")]
    [Tooltip("높이(y)에 맞는 층을 자동으로 표시")]
    public bool autoSelectLayer = true;
    [Tooltip("지도를 확대해도 마커 크기 유지")]
    public bool keepScreenSize = true;

    [Header("Result (read only)")]
    [SerializeField] bool hasPosition;
    [SerializeField] Vector3 gamePosition;
    [SerializeField] Quaternion gameRotation = Quaternion.identity;
    [SerializeField] float yaw;

    RectTransform rect;
    bool layerDirty;

    public bool HasPosition => hasPosition;
    public Vector3 GamePosition => gamePosition;
    public Quaternion GameRotation => gameRotation;

    void Awake()
    {
        rect = (RectTransform)transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);   // GameToLocal은 지도 중심 기준
    }

    void Start()
    {
        if (testImage != null) ApplyFileName(testImage.name);
        else if (Application.isPlaying) gameObject.SetActive(false);   // 위치를 모르면 숨김
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (EditorPreview.IsEditMode && testImage != null && TryParse(testImage.name, out Vector3 p, out Quaternion r))
            SetGamePose(p, r);
    }
#endif

    [ContextMenu("Apply Test Image")]
    void ApplyTestImage()
    {
        if (testImage != null) ApplyFileName(testImage.name);
    }

    // 스크린샷 파일명(경로 포함 가능)으로 위치 갱신. 형식이 다르면 false.
    public bool ApplyFileName(string fileName)
    {
        if (!TryParse(fileName, out Vector3 position, out Quaternion rotation))
        {
            Debug.LogWarning($"[WhereIAM] 좌표를 찾을 수 없는 파일명: {fileName}");
            return false;
        }

        SetGamePose(position, rotation);
        if (Application.isPlaying) Debug.Log($"[WhereIAM] pos {position}, yaw {yaw:F1}°, map {mapView.config.GameToNormalized(position)}");
        return true;
    }

    public void SetGamePose(Vector3 position, Quaternion rotation)
    {
        gamePosition = position;
        gameRotation = rotation;
        yaw = rotation.eulerAngles.y;
        hasPosition = true;
        layerDirty = true;

        if (Application.isPlaying) gameObject.SetActive(true);
        PoseChanged?.Invoke(this);
    }

    // 위치가 바뀌면 호출 (루트 다시 계산용)
    public event System.Action<WhereIAM> PoseChanged;

    public static bool TryParse(string fileName, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        Match m = FileNamePattern.Match(fileName);
        if (!m.Success) return false;

        position = new Vector3(F(m, "px"), F(m, "py"), F(m, "pz"));
        rotation = new Quaternion(F(m, "qx"), F(m, "qy"), F(m, "qz"), F(m, "qw"));
        return true;
    }

    static float F(Match m, string group) => float.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    // MapView가 지도를 로드/확대한 뒤에 반영되도록 LateUpdate에서 배치
    void LateUpdate()
    {
        if (!hasPosition || mapView == null || mapView.config == null) return;

        // MapView.Load(Start)가 층을 초기화하므로 그 이후에 적용
        if (layerDirty)
        {
            layerDirty = false;
            if (autoSelectLayer) mapView.ShowLayerForHeight(gamePosition.y);
        }

        Vector2 local = mapView.GameToLocal(gamePosition);
        if (rect.anchoredPosition != local) rect.anchoredPosition = local;

        // 정면 방향도 지도 변환을 거쳐 계산 (맵 회전값과 무관하게 동작)
        Vector3 forward = gameRotation * Vector3.forward;
        Vector2 ahead = mapView.GameToLocal(gamePosition + new Vector3(forward.x, 0f, forward.z) * 10f);
        Vector2 dir = ahead - local;
        if (dir.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;   // 그래픽 정면 = +Y
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        if (keepScreenSize)
        {
            float s = 1f / mapView.transform.localScale.x;
            if (!Mathf.Approximately(rect.localScale.x, s)) rect.localScale = new Vector3(s, s, 1f);
        }
    }
}
