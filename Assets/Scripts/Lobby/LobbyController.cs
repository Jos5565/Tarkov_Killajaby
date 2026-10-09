using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 로비 화면: 맵 선택(그리드) + 플레이어 타입 선택(PMC/Scav) + 입장 버튼.
// 맵끼리, 플레이어끼리 각각 하나만 선택된다 (SelectGroup).
// UI는 코드로 생성한다 (ExecuteAlways: 편집 모드에서도 보임). 캔버스 아래 빈 오브젝트에 붙인다.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class LobbyController : MonoBehaviour
{
    public MapCatalog catalog;

    [Header("Assets")]
    public TMP_FontAsset font;
    public Sprite pmcIcon;
    public Sprite scavIcon;

    [Header("Text")]
    public string mapTitle = "맵 선택";
    public string sideTitle = "플레이어 타입";
    public string pmcLabel = "PMC";
    public string scavLabel = "스캐브";
    public string enterLabel = "입장";
    public string folderTitle = "스크린샷 폴더";
    public string folderResetLabel = "기본값";
    public string folderCleanLabel = "폴더 정리";
    public string folderCleanConfirmLabel = "삭제 확인";
    [Tooltip("[폴더 정리]를 누른 뒤 이 시간(초) 안에 한 번 더 눌러야 삭제한다")]
    public float cleanConfirmSeconds = 4f;

    [Header("Boss Tooltip")]
    [Tooltip("맵 버튼에 마우스를 올리면 보스 정보를 보여준다")]
    public bool showBossTooltip = true;
    public string bossTooltipSub = "보스 출현 정보 (PvE 기준)";
    public float tooltipWidth = 360f;

    [Header("Game Log")]
    [Tooltip("타르코프에서 레이드를 시작하면(게임 로그) 그 맵으로 자동 입장")]
    public bool autoEnterOnRaid = true;
    public string logOnText = "게임 연동됨 · 레이드를 시작하면 그 맵으로 자동 이동합니다";
    public string logOffText = "게임 로그를 찾지 못해 자동 이동이 꺼져 있습니다";
    public Color logOffColor = new Color(0.55f, 0.55f, 0.55f);

    [Header("Version")]
    [Tooltip("좌측 하단 버전 표시. {0} = Player Settings의 Version (Application.version)")]
    public string versionFormat = "Version {0}";
    public float versionFontSize = 16f;
    public Vector2 versionMargin = new Vector2(20f, 14f);
    public Color versionColor = new Color(0.55f, 0.55f, 0.55f);

    [Header("Layout")]
    public int mapColumns = 7;
    [Tooltip("맵 버튼과 플레이어 타입 버튼 공통 크기")]
    public Vector2 cardSize = new Vector2(260f, 96f);
    public Vector2 enterButtonSize = new Vector2(260f, 56f);
    public Vector2 folderInputSize = new Vector2(760f, 48f);
    public float titleFontSize = 26f;
    public float cardFontSize = 26f;
    [Range(0f, 1f)]
    [Tooltip("선택할 수 없는 맵 버튼의 투명도")]
    public float unavailableAlpha = 0.35f;

    [Header("Colors")]
    public SelectButton.Palette normal = SelectButton.DefaultNormal;
    public SelectButton.Palette selected = SelectButton.DefaultSelected;
    public Color iconBoxColor = SelectButton.DefaultIconBox;
    public Color titleColor = new Color(0.8f, 0.8f, 0.8f);
    public Color folderOkColor = new Color(0.45f, 0.8f, 0.35f);
    public Color folderErrorColor = new Color(0.95f, 0.4f, 0.3f);

    readonly SelectGroup<MapCatalog.Entry> mapGroup = new SelectGroup<MapCatalog.Entry>();
    readonly SelectGroup<PlayerSide> sideGroup = new SelectGroup<PlayerSide>();
    GameObject panel;
    GameObject versionLabel;
    Button enterButton;
    TMP_InputField folderInput;
    TMP_Text folderStatus;
    TMP_Text logStatus;
    SelectButton cleanButton;
    float cleanConfirmUntil = -1f;   // 0 이상이면 삭제 확인 대기 중

    void OnEnable()
    {
        Build();
        if (Application.isPlaying) TarkovLogWatcher.RaidStarted += OnRaidStarted;
    }

    // Inspector의 ⋮ 메뉴 > Reset Colors: 색만 SelectButton 기본 테마로 되돌린다
    // (컴포넌트 Reset은 catalog/font 참조까지 지우므로 따로 둔다)
    [ContextMenu("Reset Colors")]
    void ResetColors()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Reset Colors");
#endif
        normal = SelectButton.DefaultNormal;
        selected = SelectButton.DefaultSelected;
        iconBoxColor = SelectButton.DefaultIconBox;
        Build();
    }

    void OnDisable()
    {
        TarkovLogWatcher.RaidStarted -= OnRaidStarted;
        EditorPreview.DestroyLater(panel);
        EditorPreview.DestroyLater(versionLabel);
        panel = null;
        versionLabel = null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!EditorPreview.IsEditMode) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled) Build();
        };
    }
#endif

    public void Build()
    {
        EditorPreview.Destroy(panel);
        EditorPreview.Destroy(versionLabel);
        EditorPreview.ClearLeftovers(transform);
        CreateVersionLabel();
        mapGroup.Clear();
        sideGroup.Clear();

        if (showBossTooltip) EnsureTooltip();

        // 세로로 쌓는 가운데 정렬 패널
        panel = new GameObject("LobbyPanel", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var panelRt = (RectTransform)panel.transform;
        panelRt.SetParent(transform, false);
        panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 0.5f);

        var vertical = panel.GetComponent<VerticalLayoutGroup>();
        vertical.childAlignment = TextAnchor.UpperCenter;
        vertical.spacing = 18f;
        vertical.childControlWidth = vertical.childControlHeight = true;
        vertical.childForceExpandWidth = vertical.childForceExpandHeight = false;

        var fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 맵 선택
        CreateTitle(panelRt, mapTitle);
        var maps = catalog != null ? catalog.maps : new List<MapCatalog.Entry>();
        RectTransform grid = CreateGrid(panelRt, Mathf.Clamp(maps.Count, 1, mapColumns));
        foreach (MapCatalog.Entry entry in maps)
        {
            string name = !string.IsNullOrEmpty(entry.displayName) ? entry.displayName : entry.configPath;
            Sprite icon = entry.icon;
            SelectButton card = CreateCard(grid, name, icon, true, cardFontSize, null);
            if (showBossTooltip && Application.isPlaying)
                card.gameObject.AddComponent<TooltipTrigger>().content = () => BossTooltip(entry);

            if (entry.IsAvailable)
            {
                mapGroup.Add(card, entry);
            }
            else
            {
                // 아직 준비되지 않은 맵: 보이기만 하고 누를 수 없음
                card.Button.interactable = false;
                card.gameObject.AddComponent<CanvasGroup>().alpha = unavailableAlpha;
            }
        }

        CreateSpace(panelRt, 20f);

        // 플레이어 타입 선택
        CreateTitle(panelRt, sideTitle);
        RectTransform sideRow = CreateRow(panelRt, 16f);
        sideGroup.Add(CreateCard(sideRow, pmcLabel, pmcIcon, true, cardFontSize, cardSize), PlayerSide.Pmc);
        sideGroup.Add(CreateCard(sideRow, scavLabel, scavIcon, true, cardFontSize, cardSize), PlayerSide.Scav);

        CreateSpace(panelRt, 20f);

        // 스크린샷 폴더 (맵 씬에서 이 폴더의 새 스크린샷으로 위치 갱신)
        CreateTitle(panelRt, folderTitle);
        RectTransform folderRow = CreateRow(panelRt, 10f);
        folderInput = CreateFolderInput(folderRow);
        SelectButton reset = CreateCard(folderRow, folderResetLabel, null, false, cardFontSize * 0.75f, new Vector2(120f, folderInputSize.y));
        reset.Button.onClick.AddListener(() => SetFolder(""));
        cleanButton = CreateCard(folderRow, folderCleanLabel, null, false, cardFontSize * 0.75f, new Vector2(140f, folderInputSize.y));
        cleanButton.Button.onClick.AddListener(CleanFolder);
        cleanConfirmUntil = -1f;
        folderStatus = CreateStatusText(panelRt);
        RefreshFolderStatus();
        logStatus = CreateStatusText(panelRt);
        RefreshLogStatus();

        CreateSpace(panelRt, 20f);

        // 입장
        SelectButton enter = CreateCard(panelRt, enterLabel, null, false, cardFontSize, enterButtonSize);
        enter.SetSelected(true);
        enterButton = enter.Button;
        enterButton.onClick.AddListener(Enter);

        mapGroup.Changed += _ => RefreshEnter();
        sideGroup.Changed += _ => RefreshEnter();

        // 진행 중인 레이드의 맵(게임 로그) → 이전 선택 → 첫 번째 맵 순으로 고른다. 진영은 이전 선택(기본 PMC)
        MapCatalog.Entry previous = FindAvailable(CurrentRaidMap()) ??
                                    maps.Find(m => m.IsAvailable && m.configPath == GameSession.MapPath);
        mapGroup.Select(previous ?? maps.Find(m => m.IsAvailable));
        sideGroup.Select(GameSession.Side);
        RefreshEnter();

        EditorPreview.MarkDontSave(panel);
    }

    void RefreshEnter()
    {
        if (enterButton != null) enterButton.interactable = mapGroup.HasValue && sideGroup.HasValue;
    }

    void Enter()
    {
        MapCatalog.Entry entry = mapGroup.Value;
        if (entry == null) return;

        MapConfig map = entry.LoadConfig();   // 입장할 때 고른 맵만 불러온다
        if (map == null)
        {
            Debug.LogWarning($"[Lobby] MapConfig를 찾을 수 없습니다: Resources/{entry.configPath}");
            return;
        }
        if (string.IsNullOrEmpty(map.sceneName))
        {
            Debug.LogWarning($"[Lobby] {map.name}의 sceneName이 비어 있습니다.");
            return;
        }

        GameSession.Map = map;
        GameSession.MapPath = entry.configPath;
        GameSession.Side = sideGroup.Value;
        SceneManager.LoadScene(map.sceneName);
    }

    // ---- 게임 로그 연동 ----

    // 레이드가 시작되면 그 맵을 고르고 바로 입장한다. 진영은 로그로 알 수 없어 로비에서 고른 값을 쓴다
    void OnRaidStarted(string normalizedName)
    {
        if (!autoEnterOnRaid || !isActiveAndEnabled) return;
        MapCatalog.Entry entry = FindAvailable(normalizedName);
        if (entry == null) return;   // 아직 지원하지 않는 맵 (미궁, 쇄빙선 등)

        Debug.Log($"[Lobby] 레이드 시작 감지 → {entry.displayName} 입장");
        mapGroup.Select(entry);
        if (sideGroup.HasValue) Enter();
    }

    static string CurrentRaidMap() =>
        Application.isPlaying && TarkovLogWatcher.Instance != null ? TarkovLogWatcher.Instance.CurrentRaidMap : null;

    MapCatalog.Entry FindAvailable(string normalizedName) =>
        string.IsNullOrEmpty(normalizedName) || catalog == null
            ? null
            : catalog.maps.Find(m => m.IsAvailable && m.normalizedName == normalizedName);

    void RefreshLogStatus()
    {
        if (logStatus == null) return;
        bool on = Application.isPlaying && TarkovLogWatcher.Instance != null && TarkovLogWatcher.Instance.IsActive;
        string text = !autoEnterOnRaid ? "" : on ? logOnText : logOffText;
        if (logStatus.text == text) return;
        logStatus.text = text;
        logStatus.color = on ? folderOkColor : logOffColor;
    }

    // ---- 스크린샷 폴더 ----

    void SetFolder(string path)
    {
        AppSettings.ScreenshotFolder = path;   // 비우면 기본 폴더
        if (folderInput != null) folderInput.SetTextWithoutNotify(AppSettings.Current.screenshotFolder);
        CancelCleanConfirm();
        RefreshFolderStatus();
    }

    void Update()
    {
        RefreshLogStatus();

        // 삭제 확인 대기 시간이 지나면 원래대로
        if (cleanConfirmUntil >= 0f && Time.unscaledTime > cleanConfirmUntil)
        {
            CancelCleanConfirm();
            RefreshFolderStatus();
        }
    }

    // [폴더 정리]: 첫 번째 클릭은 확인 요청, 대기 시간 안에 한 번 더 누르면 스크린샷을 영구 삭제한다 (휴지통을 거치지 않음).
    // 파일명에 좌표가 있는 타르코프 스크린샷만 지우고, 다른 이미지나 하위 폴더는 건드리지 않는다.
    void CleanFolder()
    {
        string folder = AppSettings.ScreenshotFolder;
        List<string> files = FindScreenshots(folder);

        if (cleanConfirmUntil < 0f)
        {
            if (files.Count == 0)
            {
                RefreshFolderStatus("지울 스크린샷이 없습니다 · ");
                return;
            }
            cleanConfirmUntil = Time.unscaledTime + cleanConfirmSeconds;
            if (cleanButton != null && cleanButton.label != null) cleanButton.label.text = folderCleanConfirmLabel;
            if (folderStatus != null)
            {
                folderStatus.text = $"스크린샷 {files.Count}개를 영구 삭제합니다. 삭제하려면 [{folderCleanConfirmLabel}]를 한 번 더 누르세요";
                folderStatus.color = folderErrorColor;
            }
            return;
        }

        CancelCleanConfirm();
        int deleted = 0, failed = 0;
        foreach (string file in files)
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception e)
            {
                failed++;
                Debug.LogWarning($"[Lobby] 스크린샷 삭제 실패: {file}\n{e.Message}");
            }
        }
        Debug.Log($"[Lobby] 스크린샷 {deleted}개 삭제 ({folder})" + (failed > 0 ? $", 실패 {failed}개" : ""));
        RefreshFolderStatus($"{deleted}개 삭제됨" + (failed > 0 ? $" (사용 중이라 {failed}개 실패)" : "") + " · ");
    }

    void CancelCleanConfirm()
    {
        cleanConfirmUntil = -1f;
        if (cleanButton != null && cleanButton.label != null) cleanButton.label.text = folderCleanLabel;
    }

    static List<string> FindScreenshots(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                return Directory.EnumerateFiles(folder).Where(ScreenshotWatcher.IsScreenshot).ToList();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] 스크린샷 폴더 읽기 실패: {folder}\n{e.Message}");
        }
        return new List<string>();
    }

    // prefix: 상태 문구 앞에 붙일 결과 메시지 (예: "12개 삭제됨 · ")
    void RefreshFolderStatus(string prefix = "")
    {
        if (folderStatus == null) return;
        string folder = AppSettings.ScreenshotFolder;
        bool usingDefault = string.IsNullOrWhiteSpace(AppSettings.Current.screenshotFolder);

        if (Directory.Exists(folder))
        {
            int count = FindScreenshots(folder).Count;
            folderStatus.text = prefix + $"폴더 확인됨 · 스크린샷 {count}개" + (usingDefault ? " (기본 폴더)" : "");
            folderStatus.color = folderOkColor;
        }
        else
        {
            folderStatus.text = $"폴더를 찾을 수 없습니다: {folder}";
            folderStatus.color = folderErrorColor;
        }
    }

    // ---- 보스 툴팁 ----

    // 맵 씬과 같은 MapTooltip을 캔버스 맨 위에 만든다 (Play 중에만)
    void EnsureTooltip()
    {
        if (!Application.isPlaying || MapTooltip.Instance != null) return;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var go = new GameObject("Tooltip", typeof(RectTransform));
        go.SetActive(false);   // 글꼴을 넣은 뒤 켜야 MapTooltip이 그 글꼴로 만들어진다
        go.transform.SetParent(canvas.rootCanvas.transform, false);
        var tooltip = go.AddComponent<MapTooltip>();
        tooltip.font = font;
        tooltip.width = tooltipWidth;
        go.transform.SetAsLastSibling();
        go.SetActive(true);
    }

    // 예)  레쉴라  75%  위치 3곳
    //      로그  50~100%  위치 5곳 · 5그룹
    (string title, string sub, string body) BossTooltip(MapCatalog.Entry entry)
    {
        string sub = entry.IsAvailable ? bossTooltipSub : bossTooltipSub + " · 지도 준비 중";
        if (entry.bosses == null || entry.bosses.Count == 0)
            return (entry.displayName, sub, "출현하는 보스가 없습니다");

        var sb = new StringBuilder();
        foreach (MapCatalog.BossInfo boss in entry.bosses)
        {
            string chance = Mathf.Approximately(boss.chanceMin, boss.chanceMax)
                ? Percent(boss.chanceMax)
                : $"{Percent(boss.chanceMin)}~{Percent(boss.chanceMax)}";
            string where = boss.locations > 0 ? $"위치 {boss.locations}곳" : "위치 정보 없음";
            if (boss.groups > 1) where += $" · {boss.groups}그룹";
            sb.AppendLine($"<b><noparse>{boss.name}</noparse></b>  <color=#C3B190>{chance}</color>  <color=#9A9A9A>{where}</color>");
        }
        return (entry.displayName, sub, sb.ToString().TrimEnd());
    }

    static string Percent(float chance) => $"{chance * 100f:0}%";

    // ---- UI 생성 ----

    // 좌측 하단: Version 0.1.1 (Edit > Project Settings > Player > Version)
    void CreateVersionLabel()
    {
        versionLabel = new GameObject("Version", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = (RectTransform)versionLabel.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = versionMargin;
        rt.sizeDelta = new Vector2(400f, versionFontSize * 1.5f);

        var label = versionLabel.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = string.Format(versionFormat, Application.version);
        label.fontSize = versionFontSize;
        label.color = versionColor;
        label.alignment = TextAlignmentOptions.BottomLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        EditorPreview.MarkDontSave(versionLabel);
    }

    // 금색 테두리 입력창. 비어 있으면 기본 폴더 경로를 흐리게 보여준다.
    TMP_InputField CreateFolderInput(RectTransform parent)
    {
        var go = new GameObject("FolderInput", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var element = go.GetComponent<LayoutElement>();
        element.preferredWidth = folderInputSize.x;
        element.preferredHeight = folderInputSize.y;
        var border = go.GetComponent<Image>();
        border.color = normal.border;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        var fillRt = (RectTransform)fill.transform;
        fillRt.SetParent(rt, false);
        SelectButton.Inset(fillRt, 1f, 1f, 1f, 1f);
        var fillImage = fill.GetComponent<Image>();
        fillImage.color = normal.fill;
        fillImage.raycastTarget = false;

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        var areaRt = (RectTransform)area.transform;
        areaRt.SetParent(rt, false);
        SelectButton.Inset(areaRt, 14f, 6f, 14f, 6f);

        TMP_Text placeholder = CreateInputText(areaRt, AppSettings.DefaultScreenshotFolder, new Color(normal.label.r, normal.label.g, normal.label.b, 0.45f));
        placeholder.fontStyle = FontStyles.Italic;
        TMP_Text text = CreateInputText(areaRt, "", new Color(0.88f, 0.88f, 0.88f));

        // 자식(텍스트)을 먼저 만든 뒤 InputField를 붙인다
        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = border;
        input.textViewport = areaRt;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.fontAsset = font;
        input.pointSize = text.fontSize;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.SetTextWithoutNotify(AppSettings.Current.screenshotFolder);
        input.onEndEdit.AddListener(SetFolder);   // 입력을 마치면(Enter/포커스 해제) 저장
        return input;
    }

    TMP_Text CreateInputText(RectTransform parent, string text, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        SelectButton.Inset(rt, 0f, 0f, 0f, 0f);
        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = 18f;
        label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    TMP_Text CreateStatusText(RectTransform parent)
    {
        var go = new GameObject("FolderStatus", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = 16f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    SelectButton CreateCard(RectTransform parent, string text, Sprite iconSprite, bool showIconBox, float fontSize, Vector2? size) =>
        SelectButton.Create(parent, text, iconSprite, showIconBox, fontSize, size ?? cardSize, size.HasValue,
            font, normal, selected, iconBoxColor);

    RectTransform CreateGrid(RectTransform parent, int columns)
    {
        var go = new GameObject("MapGrid", typeof(RectTransform), typeof(GridLayoutGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);

        var grid = go.GetComponent<GridLayoutGroup>();
        grid.cellSize = cardSize;
        grid.spacing = new Vector2(-1f, -1f);   // 이웃 칸과 테두리를 겹쳐 1px 선으로
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperLeft;
        return rt;
    }

    RectTransform CreateRow(RectTransform parent, float spacing)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return rt;
    }

    void CreateTitle(RectTransform parent, string text)
    {
        var go = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = titleFontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = titleColor;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
    }

    void CreateSpace(RectTransform parent, float height)
    {
        var go = new GameObject("Space", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
    }

    // 버튼 묶음 중 하나만 선택되게 한다 (ToggleGroup 대신)
    class SelectGroup<T>
    {
        readonly List<(SelectButton button, T value)> items = new List<(SelectButton, T)>();

        public T Value { get; private set; }
        public bool HasValue { get; private set; }
        public event Action<T> Changed;

        public void Add(SelectButton button, T value)
        {
            items.Add((button, value));
            button.Button.onClick.AddListener(() => Select(value));
        }

        public void Select(T value)
        {
            bool found = false;
            foreach (var (button, v) in items)
            {
                bool on = EqualityComparer<T>.Default.Equals(v, value);
                button.SetSelected(on);
                found |= on;
            }
            Value = found ? value : default;
            HasValue = found;
            Changed?.Invoke(Value);
        }

        public void Clear()
        {
            items.Clear();
            HasValue = false;
            Value = default;
            Changed = null;
        }
    }
}
