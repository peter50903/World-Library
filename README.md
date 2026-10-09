# 世界模擬遊戲（程式倉庫名稱：World-Library）

正在開發的「世界自主演化 → 神祇降臨 → 人物降臨」遊戲。正式遊戲預計使用 Godot（2D）＋ C#；世界模擬由純 .NET 8 程式庫 `WorldSimulation.Core`（模擬核心）負責，包括確定性亂數、排程等，不依賴 Godot。

## 立即試玩：第一個可執行切片（文字介面觀察者 v0）

請先安裝 [.NET 8 開發套件](https://dotnet.microsoft.com/download/dotnet/8.0)，在本程式倉庫的根目錄執行：

```bash
dotnet run --project src/WorldSimulation.ObserverCli -- --demo --seed 42
```

操作：`n` 推進一天；輸入 `1`～`1000` 快轉多天；`s` 查看人口與食物；`h` 查看事件歷史；`q` 離開。觀察與閱讀時，世界時間不會自行前進。

這個切片**真的執行世界排程**：使用世界時鐘（`WorldClock`）與排程器（`Scheduler`）推進時間，用隔離的亂數結算草稿（`RngResolutionDraft`）依種子確定性地試算每日資源收穫，進行食物生產、消耗、短缺的帳務，結算後增加一筆歷史。相同的種子、情境與輸入可以重現相同的事件與狀態。

**重要限制：這只是尚未平衡的示範情境**，不是完整的世界生成、人口生死、正式的 M1 經濟、Godot 介面、存檔，也不是正式的跨系統原子提交。示範用的人口、食物與消耗數字只是本地示範資料，**不是** 設定檔 `01_世界參數` 的正式值，也不會寫入該表。此切片只示範「世界時間 → 事件 → 變化 → 可讀歷史」的第一條可執行路徑。

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
