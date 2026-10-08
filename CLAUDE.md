# Claude Code — World-Simulation-Game 開發規範

## 權威與責任

- Notion 正式規則／理由／決策與變更：https://app.notion.com/p/3ee6939dab0981f2b812e6636e103213
- Google Sheets 程式可讀設定／參數：https://docs.google.com/spreadsheets/d/136wf0Deyl9oAt4vTSIxwZXu3HUZZt7lrj9rX3Ja91Rw/edit
- 先用 `00D_設計總覽與待辦`、`00A_資料索引` 查出本項所需最小資料，不掃描全部分頁。未讀到的規則不得自行虛構。
- 使用者批准遊戲規則及最終合併；GPT 負責依正式規則提出有限範圍的企劃／執行方案、Issue、審查驗收；Claude Code 負責**先技術審查、可行才實作**。
- 如需變更世界規則／參數，**必須停下並提出待裁決項目**，交使用者批准後由 GPT 在當輪同步 Notion＋Sheets；不得由 Claude 擅自改寫兩個權威來源。

## 每一項 Claude 任務：先 Preflight

讀取當次被授權的微任務 Issue、此檔以及指定的少量規格，花最少可行的上下文完成以下檢查：
1. 是否符合權威世界規則？是否缺規格／必須新增設計決定？
2. 目標程式範圍、技術版本和相依套件是否可行？是否有可存取的執行環境、SDK、權限？
3. 是否能**實際**執行預定的編譯與測試？若當前 Claude 環境沒有工具，有沒有已經核准且可用的 GitHub Actions CI 測試途徑？
4. 是否能在本輪小型任務範圍內完成，不會意外擴大任務或消耗過多 Pro 額度？

以**短文**報告：`FEASIBLE`／`FEASIBLE_WITH_LIMITATIONS`／`BLOCKED`／`NEEDS_DESIGN_DECISION`，附上證據及阻礙。只有前兩者，且限制不影響經批准的驗收要求時，才在**同一輪已授權任務**直接實作。若阻擋或需要新決策，立即停下，不先大量寫碼。

## 實作與驗收

- Godot 2D + C# 是呈現層；純 .NET `WorldSimulation.Core` 不引用任何 Godot API；從最小 M0 開始。
- 世界 Canonical State 僅合法 Atomic Commit 更新；時間單調、ID 不重用、History 不改寫；RNG／Save／Ledger 必須遵守正式規則（**只有任務需實作相關模組時才讀相應詳細設定**）。
- 建立功能分支；修改只限本次批准的目標；留下真實 build／test 指令與結果；提交供 GPT 審查的 PR，不自行合併。
- `CI passed` 需檢查實際執行的 test；單純 SDK 環境 Smoke Test 不代表完成遊戲功能。若工具不足或測試未執行，清楚標記 `partial / blocked`，不得聲稱驗收通過。
- 不把舊同名倉庫過去的實驗 PR、檔案或 Issue 當新倉庫的已完成實作。

## Claude Pro 額度與授權

- 此專案採 **Claude Pro 訂閱內使用額度**。GPT 無法讀取剩餘額度，不得聲稱能精確預估任務成本。
- **一次只有一個經使用者批准的微任務**。Preflight 簡短且可行時直接實作；若額度警告、SDK／CI／權限問題或任務超出範圍，留下 checkpoint、立即停止。
- 不自行開始下一 Issue、不背景循環重試／跨模組開發、不把整個 M0 一次執行、不無限制掃描或重構。
- 不啟用額外付費用量、Anthropic API 金鑰、自動儲值、GitHub Claude 自動接單或自動合併；除非使用者明確另外批准。
- 不把私有 token／key 或整份私人 Sheets dump 寫到 Issue、PR 或 git commit。

## 入口

工程交接見 `docs/AI_HANDOFF.md`；下一個 M0 微任務由 GPT 在 CI 環境證實可用後再起草。**尚未批准任務時，不自行開始 coding。**
