# 世界模擬遊戲（程式倉庫名稱：World-Library）

正在開發的「世界自主演化 → 神祇降臨 → 人物降臨」遊戲。正式遊戲預計使用 Godot（2D）＋ C#；世界模擬由純 .NET 8 程式庫 `WorldSimulation.Core`（模擬核心）負責，包括確定性亂數、排程等，不依賴 Godot。

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
