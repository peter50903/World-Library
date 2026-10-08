# AI 開發規範（GPT／Claude 共用）

## 先讀這三頁，不要讀別的

1. [Notion「02｜遊戲規則總綱（AI 精簡版）」](https://app.notion.com/p/3f36939dab09811a8e1bf888e2f9030a)：全部遊戲規則的精簡版，含已作廢規則與待決清單。
2. [Notion「03｜給 GPT 的下一步製作指示」](https://app.notion.com/p/3f36939dab0981ba9906efe357a45813)：目前的任務順序、製作原則與禁止事項。**與本檔衝突時以 03 為準。**
3. [Notion「01｜目前實作狀態」](https://app.notion.com/p/3f36939dab09816c8366efc669451796)：一段最新進度。

需要細節時，只開總綱條目後面標示的單一規格分冊或單一 Google Sheets 分頁範圍。不要整本讀 Sheets，不要讀 Notion 封存頁或 Sheets `00D`／`封存_` 分頁。

## 資料權威

Notion ＞ [Google Sheets](https://docs.google.com/spreadsheets/d/136wf0Deyl9oAt4vTSIxwZXu3HUZZt7lrj9rX3Ja91Rw/edit) ＞ 本倉庫。Notion 定規則，Sheets 存 ID 與數值，本倉庫只代表程式做到哪裡。

## 硬性規則

- `WorldSimulation.Core` 是純 .NET，不得引用任何 Godot API；Godot 專案只做呈現。
- 未定案的世界數值不得編造；開發時放 Demo 設定檔並標 `TEMP`，不得寫回正式 Sheets 或 Notion 規則。
- 時間單調、EntityID 不重用、歷史不改寫、同種子可重現。
- 一次一個小任務；每個任務都要讓使用者看得到或能驗證新東西（詳見 03）。
- CI 必須在同一個 commit 真正執行建置與測試並通過才能合併；只有 SDK smoke test 不算通過。
- 不啟用付費服務、API 金鑰或自動加值；不把 token 或整份私人資料寫進 Issue、PR、commit。

## 建置與測試

```bash
dotnet restore WorldSimulation.sln
dotnet build WorldSimulation.sln --no-restore --configuration Release
dotnet test WorldSimulation.sln --no-build --configuration Release
```
