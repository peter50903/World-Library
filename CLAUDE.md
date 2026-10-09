# AI 開發規範（GPT 與 Claude 共用）

## 先讀這三頁，不要讀別的

1. [Notion「02｜遊戲規則總綱（AI 精簡版）」](https://app.notion.com/p/3f36939dab09811a8e1bf888e2f9030a)：全部遊戲規則的精簡版，含已作廢規則與待決清單。
2. [Notion「03｜給 GPT 的下一步製作指示」](https://app.notion.com/p/3f36939dab0981ba9906efe357a45813)：目前的任務順序、製作原則與禁止事項。**與本檔衝突時以 03 為準。**
3. [Notion「01｜目前實作狀態」](https://app.notion.com/p/3f36939dab09816c8366efc669451796)：一段最新進度。

需要細節時，只開總綱條目後面標示的單一規格分冊，或設定檔的單一分頁範圍。不要整本讀設定檔，不要讀 Notion 封存頁或設定檔的 `00D`、`封存_` 開頭分頁。設定檔中英文欄位標題的中文意義見分頁 `00B_術語對照`。

## 與 GPT／使用者的問答

所有往來問題記在 Notion [「05｜AI 問答板」](https://app.notion.com/p/185d1382e1144d2d8fcd19b054e4008b)，每題有編號。使用者說「請回答問題 #N」時，只讀第 N 題與其相關連結，把答案寫在「回答」欄並把狀態改為「已回答」。開始任務前先看有沒有回答者是 Claude 的「待回答」題目。

## 設定檔位置

唯一正式設定檔是 [Google Drive 設定檔資料夾](https://drive.google.com/drive/folders/1T9G9deDLRYD4rILfT8H4p_dRoJYnCUBx) 裡的 Excel 檔「世界模擬遊戲｜遊戲設定檔」。一律從這個資料夾讀取；更新時上傳為同一檔案的新版本，不另建新檔。

## 資料權威

Notion ＞ [設定檔](https://drive.google.com/drive/folders/1T9G9deDLRYD4rILfT8H4p_dRoJYnCUBx) ＞ 本程式倉庫。Notion 定規則，設定檔存 ID 與數值，本程式倉庫只代表程式做到哪裡。

## 硬性規則

- 模擬核心 `WorldSimulation.Core` 是純 .NET 程式庫，不得引用任何 Godot 介面；Godot 專案只負責呈現。
- 未定案的世界數值不得編造；開發時放在示範設定檔並標 `TEMP`（暫定），不得寫回正式設定檔或 Notion 規則。
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
