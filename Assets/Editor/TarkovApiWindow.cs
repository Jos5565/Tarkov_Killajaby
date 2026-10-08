using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// tarkov.dev 웹사이트가 사용하는 정적 JSON 서버(json.tarkov.dev)에서 데이터를 받아
//   1) 원본 JSON: Assets/Data/Api/Raw/{mode}_{maps|tasks|traders}[_{lang}].json
//   2) 마커 SO:  Assets/Data/Api/{map}_markers.asset (MapConfig.markerData에 자동 연결)
// 로 저장한다. 받아둔 JSON만 있으면 오프라인에서도 SO를 다시 만들 수 있다.
//
// 정적 JSON은 이름이 번역 키로 들어 있고(예: ex_customs_scav_gasstation),
// 실제 이름은 {path}_{lang} 파일에 있다. (tarkov-dev/src/modules/api-request.mjs 와 같은 방식)
public class TarkovApiWindow : EditorWindow
{
    const string BaseUrl = "https://json.tarkov.dev/";
    const string DataDir = "Assets/Data/Api";
    const string RawDir = DataDir + "/Raw";
    // 직접 보완한 번역: {lang}.json의 data(번역 키 → 문장). tarkov.dev 번역보다 우선 적용
    const string TranslationDir = DataDir + "/Translations";
    const string FallbackLanguage = "en";
    const string PrefGameMode = "TarkovApi.GameMode";
    const string PrefLanguage = "TarkovApi.Language";

    static readonly string[] GameModes = { "regular", "pve" };
    static readonly string[] Languages = { "en", "ko", "ru", "ja", "zh", "de", "fr", "es" };
    static readonly string[] Datasets = { "maps", "tasks", "traders" };

    int gameModeIndex;
    int languageIndex;
    string status = "";
    bool busy;

    [MenuItem("Tools/Tarkov/API Data")]
    static void Open() => GetWindow<TarkovApiWindow>("Tarkov API");

    void OnEnable()
    {
        gameModeIndex = Mathf.Max(0, Array.IndexOf(GameModes, EditorPrefs.GetString(PrefGameMode, "pve")));
        languageIndex = Mathf.Max(0, Array.IndexOf(Languages, EditorPrefs.GetString(PrefLanguage, "en")));
    }

    string GameMode => GameModes[gameModeIndex];
    string Language => Languages[languageIndex];

    string RawPath(string dataset, string lang = null) =>
        $"{RawDir}/{GameMode}_{dataset}{(lang == null ? "" : "_" + lang)}.json";

    IEnumerable<string> RequiredFiles()
    {
        foreach (string dataset in Datasets)
        {
            yield return dataset;
            yield return $"{dataset}_{Language}";
            if (Language != FallbackLanguage) yield return $"{dataset}_{FallbackLanguage}";
        }
    }

    void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        gameModeIndex = EditorGUILayout.Popup("Game Mode", gameModeIndex, GameModes);
        languageIndex = EditorGUILayout.Popup("Language", languageIndex, Languages);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetString(PrefGameMode, GameMode);
            EditorPrefs.SetString(PrefLanguage, Language);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Source", BaseUrl);
        EditorGUILayout.LabelField("Raw JSON", RawDir);
        bool hasRaw = File.Exists(RawPath("maps"));
        EditorGUILayout.LabelField("저장된 데이터", hasRaw ? File.GetLastWriteTime(RawPath("maps")).ToString("yyyy-MM-dd HH:mm") : "없음");

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(busy))
        {
            if (GUILayout.Button("다운로드 + 마커 생성", GUILayout.Height(30)))
                _ = DownloadAndBuild();

            using (new EditorGUI.DisabledScope(!hasRaw))
                if (GUILayout.Button("저장된 JSON으로 마커만 다시 생성"))
                    BuildAllMarkers();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(string.IsNullOrEmpty(status) ? "대기 중" : status, MessageType.None);
    }

    async Task DownloadAndBuild()
    {
        busy = true;
        try
        {
            Directory.CreateDirectory(RawDir);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

            foreach (string file in RequiredFiles())
            {
                SetStatus($"다운로드 중: {GameMode}/{file}");
                using HttpResponseMessage res = await client.GetAsync($"{BaseUrl}{GameMode}/{file}");
                string text = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                    throw new Exception($"{GameMode}/{file}: HTTP {(int)res.StatusCode}");

                File.WriteAllText($"{RawDir}/{GameMode}_{file}.json", text, Encoding.UTF8);
            }

            AssetDatabase.Refresh();
            BuildAllMarkers();
        }
        catch (Exception e)
        {
            SetStatus("실패: " + e.Message);
            Debug.LogException(e);
        }
        finally
        {
            busy = false;
            Repaint();
        }
    }

    void BuildAllMarkers()
    {
        try
        {
            JObject mapsData = LoadData(RawPath("maps"));
            JObject tasksData = LoadData(RawPath("tasks"));
            JObject tradersData = LoadData(RawPath("traders"));
            var tr = new Translator();
            foreach (string dataset in Datasets)
            {
                tr.Add(LoadData(RawPath(dataset, Language)), primary: true);
                tr.Add(LoadData(RawPath(dataset, FallbackLanguage)), primary: false);
            }
            tr.AddOverrides(LoadData($"{TranslationDir}/{Language}.json"));

            var log = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:MapConfig"))
            {
                var config = AssetDatabase.LoadAssetAtPath<MapConfig>(AssetDatabase.GUIDToAssetPath(guid));
                JObject map = mapsData["maps"]?.Children<JProperty>()
                    .Select(p => (JObject)p.Value)
                    .FirstOrDefault(m => (string)m["normalizedName"] == config.normalizedName);
                if (map == null)
                {
                    log.AppendLine($"{config.name}: '{config.normalizedName}' 맵을 찾지 못함");
                    continue;
                }

                MapMarkerData data = GetOrCreateMarkerData(config);
                data.mapNormalizedName = config.normalizedName;
                data.gameMode = GameMode;
                data.language = Language;
                data.downloadedAt = File.GetLastWriteTime(RawPath("maps")).ToString("yyyy-MM-dd HH:mm");
                data.markers = Convert(map, (JObject)mapsData["mobs"], tasksData, tradersData, tr, out data.quests);
                EditorUtility.SetDirty(data);

                config.markerData = data;
                EditorUtility.SetDirty(config);

                log.AppendLine($"{config.name}: {Summary(data.markers)}");
            }

            AssetDatabase.SaveAssets();
            SetStatus(log.ToString().TrimEnd());
            Debug.Log("[TarkovApi] 마커 생성 완료\n" + log);
        }
        catch (Exception e)
        {
            SetStatus("마커 생성 실패: " + e.Message);
            Debug.LogException(e);
        }
    }

    // tarkov.dev 웹사이트(src/pages/map/index.jsx)의 분류 방식을 따른다
    static List<MapMarker> Convert(JObject map, JObject mobs, JObject tasksData, JObject tradersData, Translator tr, out List<QuestData> quests)
    {
        quests = new List<QuestData>();
        var markers = new List<MapMarker>();
        string mapId = (string)map["id"];

        foreach (JToken e in Items(map["extracts"]))
        {
            if (!TryPos(e["position"], out Vector3 pos)) continue;
            string faction = (string)e["faction"];
            markers.Add(new MapMarker { type = ExtractType(faction), name = tr.Get(e["name"]), detail = faction, position = pos });
        }

        foreach (JToken t in Items(map["transits"]))
        {
            if (!TryPos(t["position"], out Vector3 pos)) continue;
            markers.Add(new MapMarker { type = MarkerType.Transit, name = tr.Get(t["description"]), position = pos });
        }

        // PvE의 PMC 봇(pmcBEAR/pmcUSEC)도 bosses에 들어 있으므로 제외
        var bosses = Items(map["bosses"])
            .Where(b => !((string)b["mob"] ?? "").StartsWith("pmc", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (JToken s in Items(map["spawns"]))
        {
            if (!TryPos(s["position"], out Vector3 pos)) continue;
            var sides = Strings(s["sides"]);
            var cats = Strings(s["categories"]);
            string zone = (string)s["zoneName"];
            var marker = new MapMarker { name = zone, position = pos };

            if (cats.Contains("boss"))
            {
                // 보스 스폰 위치: bosses[].spawnLocations[].spawnKey == spawns[].zoneName
                var matched = bosses
                    .Where(b => Items(b["spawnLocations"]).Any(l => (string)l["spawnKey"] == zone))
                    .GroupBy(b => (string)b["mob"]).Select(g => g.First())
                    .ToList();

                if (matched.Count > 0)
                {
                    marker.type = MarkerType.Boss;
                    marker.name = string.Join(", ", matched.Select(b => MobName(b, mobs, tr)));
                    marker.detail = string.Join(", ", matched.Select(b => $"{MobName(b, mobs, tr)} {(float)b["spawnChance"] * 100f:0}%")) + $" ({zone})";
                }
                else if (cats.Contains("bot") && sides.Contains("scav")) marker.type = MarkerType.ScavSpawn;
                else continue;
            }
            else if (cats.Contains("player"))
            {
                if (sides.Contains("pmc") || sides.Contains("all")) marker.type = MarkerType.PmcSpawn;
                else continue;
            }
            else if (cats.Contains("sniper")) marker.type = MarkerType.SniperScav;
            else if (sides.Contains("scav") && (cats.Contains("bot") || cats.Contains("all"))) marker.type = MarkerType.ScavSpawn;
            else continue;

            markers.Add(marker);
        }

        var questItems = tasksData["questItems"] as JObject;
        foreach (JProperty prop in tasksData["tasks"]?.Children<JProperty>() ?? Enumerable.Empty<JProperty>())
        {
            JToken task = prop.Value;
            string taskId = (string)task["id"] ?? prop.Name;
            string taskName = tr.Get(task["name"]);   // 공식 한글 이름이 있으면 한글, 없으면 영어 원문
            string trader = tr.Get(tradersData[(string)task["trader"] ?? ""]?["name"]);
            int markerCountBefore = markers.Count;

            foreach (JToken obj in Items(task["objectives"]))
            {
                string objectiveId = (string)obj["id"];
                foreach (JToken loc in Items(obj["possibleLocations"]))
                {
                    if ((string)loc["map"] != mapId) continue;
                    string itemName = tr.Get(questItems?[(string)obj["questItem"] ?? ""]?["name"]);
                    foreach (JToken p in Items(loc["positions"]))
                        if (TryPos(p, out Vector3 pos))
                            markers.Add(new MapMarker { type = MarkerType.QuestItem, key = taskId, objective = objectiveId, name = taskName, group = trader, detail = itemName, position = pos });
                }

                foreach (JToken zone in Items(obj["zones"]))
                {
                    if ((string)zone["map"] != mapId || !TryPos(zone["position"], out Vector3 pos)) continue;
                    markers.Add(new MapMarker { type = MarkerType.Quest, key = taskId, objective = objectiveId, name = taskName, group = trader, detail = tr.Get(obj["description"]), position = pos });
                }
            }

            // 이 맵에 마커가 하나라도 있는 퀘스트만 전체 목표 목록을 저장 (툴팁/도착 알림용)
            if (markers.Count == markerCountBefore) continue;
            var quest = new QuestData { key = taskId, name = taskName, trader = trader };
            var markedObjectives = new HashSet<string>(markers.Skip(markerCountBefore).Select(m => m.objective));
            foreach (JToken obj in Items(task["objectives"]))
            {
                string description = tr.Get(obj["description"]);
                if (string.IsNullOrEmpty(description)) continue;
                string id = (string)obj["id"];
                bool thisMap = markedObjectives.Contains(id) || Items(obj["maps"]).Any(m => (string)m == mapId);
                quest.objectives.Add(new QuestObjective { id = id, description = description, optional = (bool?)obj["optional"] ?? false, thisMap = thisMap });
            }
            quests.Add(quest);
        }

        return markers;
    }

    // extracts[].faction: "pmc" / "scav" / "shared"
    static MarkerType ExtractType(string faction) => faction switch
    {
        "pmc" => MarkerType.ExtractPmc,
        "scav" => MarkerType.ExtractScav,
        _ => MarkerType.ExtractShared,
    };

    static string MobName(JToken boss, JObject mobs, Translator tr)
    {
        string mob = (string)boss["mob"];
        return tr.Get(mobs?[mob ?? ""]?["name"] ?? mob);
    }

    static IEnumerable<JToken> Items(JToken token) => token as JArray ?? Enumerable.Empty<JToken>();

    static HashSet<string> Strings(JToken token) => new HashSet<string>(Items(token).Select(t => (string)t));

    static bool TryPos(JToken t, out Vector3 pos)
    {
        pos = default;
        if (!(t is JObject o) || o["x"] == null) return false;
        pos = new Vector3((float)o["x"], (float)o["y"], (float)o["z"]);
        return true;
    }

    // 파일의 "data" 객체. 없으면 빈 객체.
    static JObject LoadData(string path) =>
        File.Exists(path) ? JObject.Parse(File.ReadAllText(path))["data"] as JObject ?? new JObject() : new JObject();

    static MapMarkerData GetOrCreateMarkerData(MapConfig config)
    {
        if (config.markerData != null) return config.markerData;

        Directory.CreateDirectory(DataDir);
        string path = $"{DataDir}/{config.normalizedName}_markers.asset";
        var data = AssetDatabase.LoadAssetAtPath<MapMarkerData>(path);
        if (data == null)
        {
            data = CreateInstance<MapMarkerData>();
            AssetDatabase.CreateAsset(data, path);
        }
        return data;
    }

    static string Summary(List<MapMarker> markers) =>
        string.Join(", ", markers.GroupBy(m => m.type).Select(g => $"{g.Key} {g.Count()}"));

    void SetStatus(string text)
    {
        status = text;
        Repaint();
    }

    // 번역 키 → 이름. 직접 보완한 번역 → 선택 언어 → en → 키 그대로 순서로 찾는다.
    class Translator
    {
        readonly Dictionary<string, string> overrides = new Dictionary<string, string>();
        readonly Dictionary<string, string> primary = new Dictionary<string, string>();
        readonly Dictionary<string, string> fallback = new Dictionary<string, string>();

        public void AddOverrides(JObject data)
        {
            foreach (JProperty p in data.Properties())
                if (p.Value.Type == JTokenType.String) overrides[p.Name] = (string)p.Value;
        }

        public void Add(JObject data, bool primary)
        {
            var target = primary ? this.primary : fallback;
            foreach (JProperty p in data.Properties())
                if (p.Value.Type == JTokenType.String) target[p.Name] = (string)p.Value;
        }

        public string Get(JToken key)
        {
            string k = (string)key;
            if (string.IsNullOrEmpty(k)) return k;
            if (overrides.TryGetValue(k, out string v)) return v;
            return primary.TryGetValue(k, out v) ? v : fallback.TryGetValue(k, out v) ? v : k;
        }
    }
}
