# GPT ↔ Claude Code｜開發交接

## 流程

1. **GPT 提案**：查詢正式 Notion 規則與 Google Sheets 指定資料，編寫**一張小型 Issue**；內容含規格出處／版本、產物、有限範圍、技術方案、依賴、建置與測試路徑、驗收條件與禁止超出的範圍。
2. **使用者批准本輪**：Claude Pro 帳號的當下剩餘額度由使用者在 Claude Settings → Usage 查看。GPT 無法查看或保證使用量一定不超過限制。
3. **Claude Preflight**：先檢查技術、資料、環境、權限、測試工具與不確定性；結論是 `FEASIBLE`、`FEASIBLE_WITH_LIMITATIONS`、`BLOCKED` 或 `NEEDS_DESIGN_DECISION`。
4. **Claude Implement（有條件）**：僅在 Preflight 可行且不須新增設計決策時，沿同一輪批准的小任務實作。否則停止，將可行替代方案回報 GPT。
5. **CI 與 GPT 審查**：Claude 提交功能分支 PR，附真實建置／測試輸出，GPT 看原始碼、PR diff、執行結果、規則一致性及缺漏；不通過則指定有限修正。
6. **使用者批准合併**：只有驗收通過並取得人類同意才可合併，下一 Issue 需另一次人類批准。

## 規則與工程資料權威

- Notion 是設計與變更決策權威；Sheets 是可機讀參數權威。
- GitHub 只記載版本化實作、Issue、測試、PR。
- 新遊戲規則一旦決定，當輪由 GPT 同步 Notion＋Sheets，再繼續後續設計。
- Claude 不得猜未定值，不得自行編輯正式 Notion／Sheets，應提出需裁決問題。

## 安全及成本

- 僅 Claude Pro 訂閱額度；不得自動超額付費、API Key、自動啟動下一個任务或自動合併。
- GitHub Actions 的 .NET 建置是**獨立工程 CI**，不是 Claude Code，也不使用 Claude 訂閱額度；有可能消耗 GitHub Actions 分鐘。
- 先實際測通 .NET 8 SDK CI 才派第一個 M0 實作任務。臨時 SDK Smoke Test 的綠燈不等於專案有遊戲程式。
- 不預設私人倉庫的第三方 App／Secrets 有權訪問 Notion 或 Sheets。
