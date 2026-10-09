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
    // 층 정의(높이 범위 + 건물 영역): tarkov.dev 웹사이트 소스의 지도 설정. API에는 층 정보가 없다
    const string FloorUrl = "https://raw.githubusercontent.com/the-hideout/tarkov-dev/main/src/data/maps.json";
    const string FloorPath = RawDir + "/tarkovdev_maps.json";

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

            SetStatus("다운로드 중: 층 정의 (tarkov-dev maps.json)");
            using (HttpResponseMessage res = await client.GetAsync(FloorUrl))
            {
                if (res.IsSuccessStatusCode) File.WriteAllText(FloorPath, await res.Content.ReadAsStringAsync(), Encoding.UTF8);
                else Debug.LogWarning($"[TarkovApi] 층 정의를 받지 못해 기존 파일을 사용합니다: HTTP {(int)res.StatusCode}");
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
            JArray floorMaps = File.Exists(FloorPath) ? JArray.Parse(File.ReadAllText(FloorPath)) : new JArray();

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
                data.markers = Convert(map, (JObject)mapsData["mobs"], tasksData, tradersData, tr, FloorLayers(floorMaps, config.normalizedName), out data.quests);
                EditorUtility.SetDirty(data);

                config.markerData = data;
                EditorUtility.SetDirty(config);

                log.AppendLine($"{config.name}: {Summary(data.markers)}");
            }

            // 로비 맵 버튼 툴팁용 보스 정보 (지도를 불러오지 않고 보여주기 위해 MapCatalog에 저장)
            foreach (string guid in AssetDatabase.FindAssets("t:MapCatalog"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (MapCatalog.Entry entry in catalog.maps)
                {
                    JObject map = mapsData["maps"]?.Children<JProperty>()
                        .Select(p => (JObject)p.Value)
                        .FirstOrDefault(m => (string)m["normalizedName"] == entry.normalizedName);
                    entry.bosses = map != null ? BossSummary(map, (JObject)mapsData["mobs"], tr) : new List<MapCatalog.BossInfo>();
                }
                EditorUtility.SetDirty(catalog);
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
    static List<MapMarker> Convert(JObject map, JObject mobs, JObject tasksData, JObject tradersData, Translator tr, JArray floors, out List<QuestData> quests)
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

        // 배틀패스 문서: 문서가 나올 수 있는 자리마다 종류별 마커. 한 자리에 여러 종류면 설명에 함께 적는다
        foreach (JToken loot in Items(map["lootLoose"]))
        {
            if (!TryPos(loot["position"], out Vector3 pos)) continue;
            var docs = Strings(loot["items"])
                .Select(id => BattlePassDocs.TryGetByItem(id, out BattlePassDocs.Doc doc) ? doc : (BattlePassDocs.Doc?)null)
                .Where(d => d.HasValue).Select(d => d.Value)
                .OrderBy(d => d.type).ToList();
            string detail = docs.Count > 1 ? "이 자리: " + string.Join(", ", docs.Select(d => d.name)) : BattlePassDocs.Title;
            foreach (BattlePassDocs.Doc doc in docs)
                markers.Add(new MapMarker { type = doc.type, name = doc.name, detail = detail, floor = FloorName(floors, pos), position = pos });
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

    // ---- 층 ----

    // tarkov-dev maps.json에서 이 맵(interactive 지도)의 layers. 없으면 빈 배열
    static JArray FloorLayers(JArray floorMaps, string normalizedName)
    {
        JToken group = floorMaps.FirstOrDefault(g => (string)g["normalizedName"] == normalizedName);
        JToken interactive = Items(group?["maps"]).FirstOrDefault(m => (string)m["projection"] == "interactive");
        return interactive?["layers"] as JArray ?? new JArray();
    }

    // tarkov.dev 웹사이트와 같은 방식: 높이가 층의 범위 안이고, 영역(bounds)이 있으면 그 안일 때 그 층.
    // 어느 층에도 안 들어가면 지상. 층 정의가 없는 맵(삼림 등)은 빈 문자열
    static string FloorName(JArray layers, Vector3 pos)
    {
        if (layers.Count == 0) return "";
        foreach (JToken layer in layers)
            foreach (JToken extent in Items(layer["extents"]))
            {
                if (!(extent["height"] is JArray h) || h.Count < 2) continue;
                if (pos.y < (float)h[0] || pos.y >= (float)h[1]) continue;
                var bounds = extent["bounds"] as JArray;
                if (bounds == null || bounds.Count == 0 || bounds.Any(b => InRect(b, pos)))
                    return FloorLabel((string)layer["name"]);
            }
        return "지상";
    }

    // bounds 항목: [[x1, z1], [x2, z2], "이름"]
    static bool InRect(JToken b, Vector3 pos)
    {
        if (!(b is JArray r) || r.Count < 2) return false;
        float x1 = (float)r[0][0], z1 = (float)r[0][1], x2 = (float)r[1][0], z2 = (float)r[1][1];
        return pos.x >= Mathf.Min(x1, x2) && pos.x <= Mathf.Max(x1, x2) && pos.z >= Mathf.Min(z1, z2) && pos.z <= Mathf.Max(z1, z2);
    }

    static string FloorLabel(string name) => name switch
    {
        "2nd Floor" or "Second Level" => "2층",
        "3rd Floor" => "3층",
        "4th Floor" => "4층",
        "5th Floor" => "5층",
        "Underground" => "지하",
        "Tunnels" => "지하 터널",
        "Bunkers" => "지하 벙커",
        "Garage" => "지하 차고",
        "Technical" => "지하 기술층",
        _ => name,
    };

    // 맵의 보스 목록을 이름별로 묶는다 (PvE의 PMC 봇 제외). 같은 보스가 여러 그룹이면 확률 범위와 그룹 수
    static List<MapCatalog.BossInfo> BossSummary(JObject map, JObject mobs, Translator tr) =>
        Items(map["bosses"])
            .Where(b => !((string)b["mob"] ?? "").StartsWith("pmc", StringComparison.OrdinalIgnoreCase))
            .GroupBy(b => MobName(b, mobs, tr))
            .Select(g => new MapCatalog.BossInfo
            {
                name = g.Key,
                chanceMin = g.Min(b => (float?)b["spawnChance"] ?? 0f),
                chanceMax = g.Max(b => (float?)b["spawnChance"] ?? 0f),
                groups = g.Count(),
                locations = g.SelectMany(b => Items(b["spawnLocations"]))
                    .Select(l => (string)l["spawnKey"] ?? (string)l["name"]).Distinct().Count(),
            })
            .ToList();

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
