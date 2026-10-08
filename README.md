# Tarkov 길라잡이(킬라자비)

타르코프(Escape from Tarkov)를 하면서 옆에 띄워두는 **위치 추적 지도 앱**입니다.
게임에서 찍은 스크린샷 파일명에 들어 있는 좌표로 내 위치를 지도에 표시하고,
탈출구 · 스폰 · 보스 · 퀘스트 위치와 퀘스트 최단 경로를 보여줍니다.

> Unity 6 · UGUI · Windows

## 주요 기능

### 로비
- **맵 선택**: 세관, 삼림, 해안선, 인터체인지, 연구소, 리저브, 등대, 타르코프 시내, 그라운드 제로, 터미널, 공장 (11개)
- **플레이어 타입 선택**: PMC / 스캐브
- **스크린샷 폴더 설정**: 비워두면 기본 폴더(`문서\Escape from Tarkov\Screenshots`) 사용

### 지도
- 마우스 휠 확대 · 드래그 이동 · 더블클릭으로 전체 보기
- **마커**: 탈출구(PMC 전용 / 스캐브 전용 / 공용, 진영별 색), 트랜짓, PMC · 스캐브 스폰, 저격 스캐브, 보스, 퀘스트 목표, 퀘스트 아이템
  - 진영에 맞는 탈출구만 표시하고, 스캐브로 들어가면 퀘스트 기능은 꺼집니다
  - 가까이 겹친 탈출구는 이름표 하나로 합쳐 표시합니다 (예: `Climber's Trail / Rock Passage / Cliff Descent`)
- **필터 패널**(왼쪽): 마커 종류별 표시 켜기/끄기
- **퀘스트 목록**(오른쪽): 이 맵에 위치가 있는 퀘스트 목록, 검색, 퀘스트별 켜기/끄기, ALL ON/OFF
- **툴팁**: 퀘스트 마커에 마우스를 올리면 **이 맵에서 하는 목표**만 보여주고, 지금 마커의 목표를 강조합니다

### 내 위치와 경로
- **스크린샷 감지**: 스크린샷 폴더를 감시해서, 새 스크린샷이 생기면 파일명의 좌표로 내 위치와 바라보는 방향을 갱신합니다
- **퀘스트 경로**: 켜져 있는 퀘스트들을 내 위치에서 출발해 도는 최단 순서(직선거리)를 선과 번호로 표시합니다. `경로` 버튼으로 켜고 끕니다
- **도착 탈출구**: 탈출구/트랜짓 이름표를 클릭하면 그곳을 경로의 끝으로 지정합니다(이름표가 주황색으로 바뀜). 마지막 목표에서 탈출구까지의 거리도 포함해 순서를 정하고, 같은 이름표를 다시 클릭하면 해제합니다
- **도착 알림**: 켜진 퀘스트 목표에서 30m 안에 들어오면 퀘스트와 목표를 화면 위쪽에 띄웁니다

### 저장
- 맵별로 켜고 끈 상태(퀘스트, 마커 종류, 필터 그룹 접힘, 경로, 도착 탈출구)를 저장해 다시 들어와도 유지됩니다
- 처음 들어간 맵은 퀘스트가 모두 꺼진 상태로 시작합니다

## 스크린샷 좌표

타르코프 스크린샷 파일명에는 촬영 위치와 회전값이 들어 있습니다.

```
2026-09-23[17-56]_120.79, 2.83, -73.11_0.01954, 0.75538, -0.02240, 0.65462_17.59 (0).png
                  └─ 위치 x, y, z ─────┘ └─ 회전(쿼터니언) x, y, z, w ──────┘
```

파일명에는 맵 정보가 없어서, 다른 맵에서 찍은 스크린샷도 지금 맵 위에 표시될 수 있습니다.

## 시작하기

### 요구 사항
- Unity **6000.3.13f1** (URP)
- 패키지 (`Packages/manifest.json`에 포함, 열면 자동 설치)
  - Input System, TextMeshPro, Newtonsoft Json (`com.unity.nuget.newtonsoft-json`)
  - [UGUITemplate](https://github.com/Jos5565/UGUITemplate.git) (RoundImage 등)

### 실행
1. 저장소를 받아 Unity Hub에서 프로젝트를 엽니다.
2. `Assets/Scenes/KillaJaby.unity`(로비)를 열고 Play 합니다.
3. 맵과 PMC/스캐브를 고르고 **입장**합니다.

Build Settings 씬 순서: `KillaJaby`(로비) → `Map`(지도)

> 지도와 마커, 패널은 Play 하지 않아도 에디터에서 미리 볼 수 있습니다(`[ExecuteAlways]`).
> 편집 모드에서 만든 미리보기 오브젝트는 씬에 저장되지 않습니다.

## 데이터 갱신

마커 데이터는 [tarkov.dev](https://tarkov.dev)가 사용하는 정적 JSON(`json.tarkov.dev`)에서 받습니다.

1. Unity 메뉴 **`Tools > Tarkov > API Data`**
2. Game Mode(`regular` / `pve`), Language(`ko` 권장)를 고릅니다.
3. **다운로드 + 마커 생성**: 원본 JSON을 받아 모든 맵의 마커 데이터를 만듭니다.
   - 이미 받아둔 JSON으로 다시 만들려면 **저장된 JSON으로 마커만 다시 생성**

| 위치 | 내용 |
|---|---|
| `Assets/Data/Api/Raw/` | 받은 원본 JSON (maps, tasks, traders + 번역) |
| `Assets/Data/Api/{맵}_markers.asset` | 맵별 마커 데이터 (MapConfig에 자동 연결) |
| `Assets/Data/Api/Translations/ko.json` | 직접 보완한 한글 번역. tarkov.dev 번역보다 우선 적용 |

- 퀘스트 **이름은 공식 한글 이름이 있으면 한글, 없으면 영어**, 퀘스트 **상세(목표, 퀘스트 아이템)는 한글**로 표시합니다.
- tarkov.dev에 한글 번역이 없는 문장은 `ko.json`에 직접 번역해 두었습니다. 데이터를 새로 받아도 유지됩니다.

## 프로젝트 구조

```
Assets/
├─ Scenes/
│  ├─ KillaJaby.unity          로비
│  └─ Map.unity                지도 (모든 맵 공용, 로비에서 고른 MapConfig로 바뀜)
├─ Scripts/
│  ├─ AppBootstrap.cs          프레임 제한, 백그라운드 실행
│  ├─ Lobby/                   로비 (맵/진영 선택, 스크린샷 폴더, 맵 씬 ↔ 로비 이동)
│  └─ Map/                     지도, 마커, 필터, 퀘스트 목록, 툴팁, 경로, 도착 알림, 스크린샷 감지
├─ Editor/
│  └─ TarkovApiWindow.cs       Tools > Tarkov > API Data
├─ SO/
│  ├─ MapCatalog.asset         로비 맵 목록
│  └─ Resources/Maps/          맵별 MapConfig (입장할 때 고른 맵만 로드)
├─ Data/
│  ├─ Maps/                    지도 이미지, maps.json
│  └─ Api/                     tarkov.dev 원본 JSON, 마커 데이터, 번역
└─ Sprites/                    PMC/스캐브 아이콘, 맵 아이콘, 마커 아이콘
```

### 주요 스크립트

| 스크립트 | 역할 |
|---|---|
| `MapConfig` | 맵 하나의 설정: 지도 이미지, 좌표 범위(bounds), 좌표 회전, 마커 데이터 |
| `MapView` | 지도 표시, 게임 좌표 → 지도 좌표 변환 |
| `MapZoomPan` | 확대 · 이동 |
| `MapMarkerLayer` | 마커 생성, 종류/퀘스트별 표시, 맵별 설정 저장 |
| `MarkerFilterPanel` / `QuestListPanel` | 왼쪽 필터 / 오른쪽 퀘스트 목록 |
| `MapTooltip` / `QuestText` | 마우스 오버 정보, 퀘스트 목표 서식 |
| `WhereIAM` / `ScreenshotWatcher` | 내 위치 표시 / 스크린샷 폴더 감시 |
| `QuestRoute` / `RouteSolver` | 퀘스트 경로 그리기 / 최단 순서 계산 |
| `QuestArrivalPanel` | 퀘스트 위치 도착 알림 |
| `LobbyController` / `GameSession` | 로비 화면 / 로비 선택값을 맵 씬으로 전달 |

## 맵 추가하기

1. 지도 이미지를 `Assets/Data/Maps/`에 넣습니다.
   - **가로·세로를 4의 배수**로 맞춥니다(투명 여백). 그래야 텍스처가 압축됩니다(BC7).
2. `Assets/SO/Resources/Maps/`에 MapConfig를 만듭니다 (`Create > Tarkov > Map Config`).
   - `normalizedName`: tarkov.dev 맵 이름 (예: `customs`, `streets-of-tarkov`)
   - `boundsA`, `boundsB`, `coordinateRotation`: tarkov.dev의 맵 메타데이터 값
   - `sceneName`: `Map`
3. `Assets/SO/MapCatalog.asset`에서 해당 맵의 `configPath`에 `Maps/{에셋 이름}`을 넣습니다.
4. **`Tools > Tarkov > API Data` → 저장된 JSON으로 마커만 다시 생성**

## 저장 위치

| 파일 | 위치 |
|---|---|
| 앱 설정 (스크린샷 폴더) | `%USERPROFILE%\AppData\LocalLow\Jos\TarkovKilaJaby\AppSettings.json` |
| 맵별 설정 | `%USERPROFILE%\AppData\LocalLow\Jos\TarkovKilaJaby\MapSettings\{맵}.json` |

## 알려진 제한

- 위치는 **스크린샷을 찍을 때만** 갱신됩니다.
- 스크린샷 파일명에 맵 정보가 없어, 다른 맵에서 찍은 스크린샷도 지금 맵에 표시될 수 있습니다.
- 경로는 **직선거리** 기준이라 강, 벽, 층을 고려하지 않습니다.
- 층별 지도는 아직 지원하지 않습니다 (기본 지도 한 장).
- **연구소**는 지도 이미지와 좌표 범위가 정확히 맞지 않아 마커가 조금 어긋날 수 있습니다.
- **터미널**은 tarkov.dev 데이터에 탈출구와 퀘스트가 아직 없습니다.

## 데이터 출처 · 라이선스

- 맵, 퀘스트, 상인 데이터와 맵 좌표 정보: [tarkov.dev](https://tarkov.dev)
  ([tarkov-dev](https://github.com/the-hideout/tarkov-dev) MIT, [tarkov-api](https://github.com/the-hideout/tarkov-api) GPL-3.0)
- 맵 아이콘: [Material Design Icons](https://pictogrammers.com/library/mdi/) (Apache License 2.0)
- 이 프로젝트의 코드: [MIT License](LICENSE)

Escape from Tarkov와 관련 상표, 게임 데이터의 권리는 Battlestate Games에 있습니다.
이 프로젝트는 Battlestate Games와 관련이 없는 비공식 팬 프로젝트입니다.
