# 世界模擬遊戲｜World-Library

正在開發的「世界自主演化 → 神祇降臨 → 人物降臨」遊戲。正式遊戲預計使用 Godot 2D + C#；純 .NET 8 `WorldSimulation.Core` 負責世界模擬、決定性RNG、排程等，不依賴 Godot。

## 立即試玩第一個可執行切片（Observer CLI v0）

請先安裝 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，在本倉庫根目錄執行：

```bash
dotnet run --project src/WorldSimulation.ObserverCli -- --demo --seed 42
```

操作：`n` 推進一日；輸入 `1`～`1000` 快轉多日；`s` 查看人口／食物；`h` 查事件歷史；`q` 離開。觀察與閱讀時世界時間不會自行前進。

這個切片**確實執行世界排程**：使用 `WorldClock` + `Scheduler` 推進時間，`RngResolutionDraft` 進行隔離的 Seed 決定性每日資源收穫試算，進行食物生產／消耗／不足量帳務，結算後增加一筆歷史。相同 Seed/情境/輸入可重現事件與狀態。

**重要限制：此CLI是一個非正式平衡的Demo scenario**，不是完整世界生成、人口生死、正式 M1 經濟、Godot UI、存檔或正式跨系統 Atomic Commit。Demo 預設人口、食物與消耗數字只是本地示範資料，**不是** Google Sheets `01_世界參數` 的正式值，也不會寫入該表。此切片只示範「世界時間 → 事件 → 變化 → 可讀歷史」的第一條可執行路徑。

## CI／驗收

```bash
dotnet restore WorldSimulation.sln
dotnet build WorldSimulation.sln --no-restore --configuration Release
dotnet test WorldSimulation.sln --no-build --configuration Release
```

GitHub Actions 還會用輸入腳本真正啟動 CLI，確認可推進事件、查看狀態與歷史。SDK smoke test 不等於遊戲功能已驗證。

## 文件快速入口（請勿每輪全文讀歷史）

1. [02｜遊戲規則總綱（AI 精簡版）](https://app.notion.com/p/3f36939dab09811a8e1bf888e2f9030a)：全部規則的精簡版。
2. [03｜給 GPT 的下一步製作指示](https://app.notion.com/p/3f36939dab0981ba9906efe357a45813)：任務順序與製作原則。
3. [01｜目前實作狀態](https://app.notion.com/p/3f36939dab09816c8366efc669451796)：一段最新進度。
- [Notion 入口頁](https://app.notion.com/p/3ee6939dab0981f2b812e6636e103213)、[Google Sheets 設定檔](https://docs.google.com/spreadsheets/d/136wf0Deyl9oAt4vTSIxwZXu3HUZZt7lrj9rX3Ja91Rw/edit)（只讀任務需要的單一分頁範圍）。

## 權威與開發規則

資料權威：Notion ＞ Google Sheets ＞ 本倉庫。AI 開發規範見 [CLAUDE.md](CLAUDE.md)。
