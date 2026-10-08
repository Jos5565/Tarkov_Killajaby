using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public static class MapSceneBuilder
{
    const string ConfigDir = "Assets/Data/Maps";
    const string CustomsConfigPath = ConfigDir + "/Customs.asset";
    const string CustomsImageDir = "Assets/Maps/Custom/";

    [MenuItem("Tools/Tarkov/Create Customs Config")]
    static void CreateCustomsConfig()
    {
        var existing = AssetDatabase.LoadAssetAtPath<MapConfig>(CustomsConfigPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            Debug.Log($"이미 있음: {CustomsConfigPath}");
            return;
        }

        Directory.CreateDirectory(ConfigDir);

        var config = ScriptableObject.CreateInstance<MapConfig>();
        config.baseSprite = LoadSprite("Customs 1.png");

        // 높이 범위는 아직 미정 (0, 0 = 자동 판정 안 됨)
        foreach (var file in new[] { "Underground_Level", "First_Floor", "Second_Floor", "Third_Floor" })
            config.layers.Add(new MapLayer { name = file, sprite = LoadSprite(file + ".png") });

        AssetDatabase.CreateAsset(config, CustomsConfigPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = config;
    }

    [MenuItem("Tools/Tarkov/Build Map Canvas")]
    static void BuildMapCanvas()
    {
        // Canvas (1920x1080 기준)
        var canvasGo = new GameObject("MapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Build Map Canvas");

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<AppBootstrap>();

        // 배경
        var background = CreateUI("Background", canvasGo.transform);
        Stretch(background);
        var bgImage = background.gameObject.AddComponent<Image>();
        bgImage.color = new Color(0.08f, 0.08f, 0.08f);
        bgImage.raycastTarget = false;

        // Viewport > MapContent > Base / Layers / Markers
        var viewport = CreateUI("Viewport", canvasGo.transform);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        // 휠/드래그 입력을 받기 위한 투명 Image
        viewport.gameObject.AddComponent<Image>().color = Color.clear;

        var content = CreateUI("MapContent", viewport);
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);

        var baseRt = CreateUI("Base", content);
        Stretch(baseRt);
        var baseImage = baseRt.gameObject.AddComponent<Image>();
        baseImage.raycastTarget = false;

        var layers = CreateUI("Layers", content);
        Stretch(layers);

        var markers = CreateUI("Markers", content);
        Stretch(markers);
        // 마커는 자주 바뀌므로 하위 Canvas로 분리해 지도 리빌드를 막는다
        markers.gameObject.AddComponent<Canvas>();
        markers.gameObject.AddComponent<GraphicRaycaster>();

        var view = content.gameObject.AddComponent<MapView>();
        view.viewport = viewport;
        view.baseImage = baseImage;
        view.layerRoot = layers;
        view.markerRoot = markers;
        view.config = AssetDatabase.LoadAssetAtPath<MapConfig>(CustomsConfigPath);

        var zoomPan = viewport.gameObject.AddComponent<MapZoomPan>();
        zoomPan.mapView = view;
        zoomPan.content = content;

        if (view.config != null)
        {
            baseImage.sprite = view.config.baseSprite;
            content.sizeDelta = view.config.baseSprite.rect.size;
        }
        else
        {
            Debug.LogWarning("MapConfig 없음. Tools/Tarkov/Create Customs Config 실행 후 MapView에 할당하세요.");
        }

        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(es, "Build Map Canvas");
        }

        Selection.activeGameObject = canvasGo;
    }

    static Sprite LoadSprite(string fileName)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CustomsImageDir + fileName);
        if (sprite == null) Debug.LogWarning($"Sprite 로드 실패: {CustomsImageDir + fileName}");
        return sprite;
    }

    static RectTransform CreateUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
