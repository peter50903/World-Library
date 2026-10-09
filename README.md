# 世界模擬遊戲（程式倉庫名稱：World-Library）

正在開發的「世界自主演化 → 神祇降臨 → 人物降臨」遊戲。正式遊戲預計使用 Godot（2D）＋ C#；世界模擬由純 .NET 8 程式庫 `WorldSimulation.Core`（模擬核心）負責，包括確定性亂數、排程等，不依賴 Godot。

## V0-03B｜同一世界亂數登錄生成 TEMP 聚落／資源點／人口群體

`DemoObserverWorld` 只建立一個 `RngRegistry`：地形用 `GEOGRAPHY` 流，聚落數量與位置用 `ECOLOGY_INIT` 流，人口群體人數用 `POP` 流，每日收穫仍用 `RESOURCE` 流；地圖與聚落在建立世界時生成一次並快取。Godot 不再自行生成地圖，而是由 `ObserverRoot` 注入核心已生成的結果。打開 Godot 後，地圖上的金色圓點就是核心生成的 2～4 個 TEMP 聚落；點擊聚落所在格，右側會顯示聚落名稱、資源點（農地／林地／漁場，依該格地形）與人口群體人數。聚落數量範圍、人口群體人數範圍與聚落名稱都是 `DemoSitesTempConfig` 中標示 `TEMP` 的暫定值，不是正式設定。右側「人口」仍是 V0-01 糧食帳的示範人口，與人口群體人數尚未整合（留待 V0-04）。

## V0-03A｜核心生成六角地形與點格查詢（TEMP，待Claude獨立審查）

在既有Godot 4.7.2 .NET專案中，地形已改由純.NET8核心的 `DemoHexWorld.Generate(seed)` 使用既定 `RngStreams.Geography` **地理亂數流**逐格生成，Godot不再有獨立的地形雜湊產生器。每次以相同世界種子生成完全相同的96格內容；改變種子會改變部分格的地形。點擊任何示範六角格可在側欄查看**核心原始格子**的偏移／軸向座標和地形類別。

此模組明確位於 `WorldSimulation.Core.World.Demo`，欄位、座標映射和草原／森林／水域**都是TEMP實作選擇**；正式座標方向、地形子類、聚落、資源點、路網、人口尚未定案，不得把本畫面當作正式世界生成。金色圓點自 V0-03B 起改為核心生成的 TEMP 聚落。

自動測試除原Core功能外，會直接比較同種子／不同種子的**每格資料**，驗證每格地形從地理專用亂數流依序產生；Godot Headless會模擬點擊、核對側欄顯示Core原資料，並確認沒有 `TerrainCode` 等自行生成地形的函式。

**工程門檻：** 此項涉及Core資料與亂數，按照Notion03，**須先由Claude於Notion05明確獨立審查接受，才能合併至main**；CI全綠不等於已獲Claude批准。

## V0-02｜Godot 觀察者畫面（第一個真正圖形介面，TEMP 示範）

安裝 **Godot 4.7.2 .NET 版本**及 **.NET 8 SDK**；一般 Godot（沒有 .NET/C# 支援）無法編譯此遊戲。使用 Godot 編輯器選「匯入」，開啟 `godot/project.godot`，點選右上角 **建置（Build）**，接著按 **F6/執行目前場景** 或 **F5/執行專案**。

可在專案根目錄手動先確認 C# 有效：

```bash
dotnet restore godot/WorldSimulation.Godot.csproj
dotnet build godot/WorldSimulation.Godot.csproj --no-restore --configuration Release
```

**預期畫面：** 中央約12×8的彩色2D六角地圖（草地、森林、湖泊；金色圓點僅是示範聚落標記）；右側是世界日期、人口、食物存量、缺糧、抽樣次數及「下一天」「快轉30天」兩個按鈕；下方是該世界實際日結算所產生的事件歷史。畫面開啟時日期為第0日，不會自動流逝；按按鈕會從同一個 `DemoObserverWorld` 真正推進、更新食物與歷史。

**資料可信度界限：** 本畫面、地形、六角座標、示範聚落位置與人口/食物數字均為 `TEMP` 的視覺/數值Fixture，**沒有**載入正式Drive Excel、未產生正式HexCell、RouteSegment、Population或世界生成；六角格相鄰不代表已存在可通行路線。真 `WorldClock`／`Scheduler`／`RngResolutionDraft`／Demo 食物結算與歷史是同一Core執行邏輯；正式AtomicCommit/Save/History Kernel仍待後續階段。Godot C#僅依賴Core；Core不依賴Godot。

**驗收現況：** GitHub Actions除了以 `Godot.NET.Sdk/4.7.2` 編譯C#、執行核心245項測試和文字介面操作，**還會下載並以官方SHA-256校驗 Godot 4.7.2 .NET 引擎，在無畫面模式載入真正場景並觸發「下一天」「快轉30天」按鈕**，查核推進至第31天、產生31筆事件歷史；因而不只是編譯測試。但CI無畫面執行**不代表顏色、中文字型、版面縮放已由真人目視驗收**，仍建議用Godot .NET編輯器開啟場景實際查看。12×8六角格現採 **TEMP 奇數列偏移顯示**，形成近矩形錯列視覺；世界正式座標制度仍未定案。

## 立即試玩：第一個可執行切片（文字介面觀察者 v0）

請先安裝 [.NET 8 開發套件](https://dotnet.microsoft.com/download/dotnet/8.0)，在本程式倉庫的根目錄執行：

```bash
dotnet run --project src/WorldSimulation.ObserverCli -- --demo --seed 42
```

操作：`n` 推進一天；輸入 `1`～`1000` 快轉多天；`s` 查看人口與食物；`h` 查看事件歷史；`q` 離開。觀察與閱讀時，世界時間不會自行前進。

這個切片**真的執行世界排程**：使用世界時鐘（`WorldClock`）與排程器（`Scheduler`）推進時間，用隔離的亂數結算草稿（`RngResolutionDraft`）依種子確定性地試算每日資源收穫，進行食物生產、消耗、短缺的帳務，結算後增加一筆歷史。相同的種子、情境與輸入可以重現相同的事件與狀態。

**重要限制：這只是尚未平衡的示範情境**，不是完整的世界生成、人口生死、正式的 M1 經濟、Godot 正式世界地圖、存檔，也不是正式的跨系統原子提交。示範用的人口、食物與消耗數字只是本地示範資料，**不是** 設定檔 `01_世界參數` 的正式值，也不會寫入該表。此切片只示範「世界時間 → 事件 → 變化 → 可讀歷史」的第一條可執行路徑。

## 自動測試與驗收

```bash
dotnet restore WorldSimulation.sln
dotnet build WorldSimulation.sln --no-restore --configuration Release
dotnet test WorldSimulation.sln --no-build --configuration Release
```

GitHub 的自動測試（GitHub Actions）還會用輸入腳本實際啟動文字介面，確認能推進事件、查看狀態與歷史。只驗證開發套件安裝的冒煙測試，不代表遊戲功能已驗證。

## 文件入口（請勿每次全文閱讀歷史）

1. [02｜遊戲規則總綱（AI 精簡版）](https://app.notion.com/p/3f36939dab09811a8e1bf888e2f9030a)：全部規則的精簡版。
2. [03｜給 GPT 的下一步製作指示](https://app.notion.com/p/3f36939dab0981ba9906efe357a45813)：任務順序與製作原則。
3. [01｜目前實作狀態](https://app.notion.com/p/3f36939dab09816c8366efc669451796)：一段最新進度。
- [Notion 入口頁](https://app.notion.com/p/3ee6939dab0981f2b812e6636e103213)、[設定檔資料夾（Google Drive）](https://drive.google.com/drive/folders/1T9G9deDLRYD4rILfT8H4p_dRoJYnCUBx)（只讀任務需要的單一分頁範圍）。

## 資料權威與開發規範

資料權威：Notion ＞ 設定檔 ＞ 本程式倉庫。AI 開發規範見 [CLAUDE.md](CLAUDE.md)。
