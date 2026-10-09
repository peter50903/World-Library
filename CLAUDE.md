# AI 開發規範（GPT 與 Claude 共用）

## 先讀這三頁，不要讀別的

1. [Notion「02｜遊戲規則總綱（AI 精簡版）」](https://app.notion.com/p/3f36939dab09811a8e1bf888e2f9030a)：全部遊戲規則的精簡版，含已作廢規則與待決清單。
2. [Notion「03｜給 GPT 的下一步製作指示」](https://app.notion.com/p/3f36939dab0981ba9906efe357a45813)：目前的任務順序、製作原則與禁止事項。**與本檔衝突時以 03 為準。**
3. [Notion「01｜目前實作狀態」](https://app.notion.com/p/3f36939dab09816c8366efc669451796)：一段最新進度。

需要細節時，只開總綱條目後面標示的單一規格分冊，或設定檔的單一分頁範圍。不要整本讀設定檔，不要讀 Notion 封存頁或設定檔的 `00D`、`封存_` 開頭分頁。設定檔中英文欄位標題的中文意義見分頁 `00B_術語對照`。

## 與 GPT 的協作

協作流程以 Notion [「07｜GPT × Claude 協作流程」](https://app.notion.com/p/3f46939dab0981abb8c0c713e4c0f602) 為準：一方製作、另一方審查，所有合併請求都要對方「接受」才合併。每一輪有動作就寫 Notion [「06｜AI 工作日誌」](https://app.notion.com/p/72a2ba9e813d438184eb790fe0a65f92)；問題與 AI 代決記在 [「05｜AI 問答板」](https://app.notion.com/p/185d1382e1144d2d8fcd19b054e4008b)。分支命名 `gpt/…`、`claude/…`。

## 設定檔位置

唯一正式設定檔是本程式倉庫的 [`config/世界模擬遊戲設定檔.xlsx`](config/)（2026-10-10 起，Google Drive 資料夾只是舊版備份）。讀取時只開 `config/sheets/<分頁名>.csv`；修改 Excel 後必須執行 `python3 tools/config_export.py` 一起提交，並經對方審查。使用者上傳的修改依 [config/README.md](config/README.md) 處理。

## 資料權威

Notion ＞ [設定檔](config/) ＞ 本程式倉庫。Notion 定規則，設定檔存 ID 與數值，本程式倉庫只代表程式做到哪裡。

## 硬性規則

- 模擬核心 `WorldSimulation.Core` 是純 .NET 程式庫，不得引用任何 Godot 介面；Godot 專案只負責呈現。
- 未定案的世界數值不得假裝成定案。開發需要時可在正式設定檔填暫定值，但必須在該列「說明」（或「數值狀態」「Status」）開頭標【TEMP】（使用者 2026-10-10 授權）；程式內的示範值同樣標 `TEMP`。定案後改掉標記。設定檔內容不清楚時以 Notion 02 為準，不相符的刪除。
- 世界時間單調前進、實體 ID（`EntityID`）不重用、歷史不改寫、同一種子可重現。
- 一次一個小任務；每個任務都要讓使用者看得到或能驗證新東西（詳見 03）。
- 同一次提交（commit）的自動測試必須真正執行建置與測試並全部通過，才能合併；只驗證開發套件安裝的冒煙測試不算通過。
- 不啟用付費服務、API 金鑰或自動加值；不把存取權杖或整份私人資料寫進議題、合併請求或提交訊息。
- 文件與玩家可見文字一律使用繁體中文；英文只限程式識別名稱，且須附中文。

## 建置與測試

```bash
dotnet restore WorldSimulation.sln
dotnet build WorldSimulation.sln --no-restore --configuration Release
dotnet test WorldSimulation.sln --no-build --configuration Release
```
