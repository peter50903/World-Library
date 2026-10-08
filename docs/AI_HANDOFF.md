# GPT ↔ Claude Code｜開發交接

## 流程

1. **GPT 自主開始當前一張小型 Issue**：依使用者2026-10-09常設授權，查詢正式 Notion 規則與 Google Sheets 指定資料，編寫**一張小型 Issue**；內容含規格出處／版本、產物、有限範圍、技術方案、依賴、建置與測試路徑、驗收條件與禁止超出的範圍。
2. **授權閘門**：GPT可自主推進已有正式規則支持的單一工程微任務，無需每次請示。**只有實際需要與 Claude Code 溝通／操作帳號或額度時才向使用者交接**；Claude Pro 使用額度只有使用者能在 Claude Settings → Usage 查看，GPT 不會自行啟用付費API。
3. **GPT／Claude Preflight**：GPT先檢查技術、資料、環境、權限、測試工具與不確定性；若真需要Claude介入，由使用者人工交接相同的有界Issue與檢查結果；結論是 `FEASIBLE`、`FEASIBLE_WITH_LIMITATIONS`、`BLOCKED` 或 `NEEDS_DESIGN_DECISION`。
4. **GPT直接實作優先；Claude Implement（需交接時）**：僅在 Preflight 可行、遵守Notion+Sheets且不需新增世界玩法決策時，GPT可直接完成小型任務；跨模組複雜任務確需Claude時，準備有限交接包，由使用者與Claude互動後才執行。遇阻擋則記錄，不虛稱完成。
5. **CI 與 GPT 審查**：由GPT或已獲交接的Claude提交功能分支Draft PR，GPT確認同Head SHA的真實 .NET Release build／xUnit測試、PR diff、規則一致性及缺漏；不通過只能在當前Issue作有限修正，不提前merge。
6. **GPT受保護合併與後續同步**：依使用者2026-10-09「所有事情交給GPT、非Claude溝通不要再詢問」的**常設合併授權**，只有新Head CI全部成功且GPT完成獨立技術審查，才以 `expected_head_sha` merge，核對main/PR/Issue，並同步Notion＋Sheets。下一個有界Issue由GPT**前一項完成之後**自行開始，無須每張PR另外請使用者回「好」。被安全/權限封鎖時不得繞過。

## 規則與工程資料權威

- Notion 是設計與變更決策權威；Sheets 是可機讀參數權威。
- GitHub 只記載版本化實作、Issue、測試、PR。
- 新遊戲規則一旦決定，當輪由 GPT 同步 Notion＋Sheets，再繼續後續設計。
- Claude 不得猜未定值，不得自行編輯正式 Notion／Sheets，應提出需裁決問題。

## 安全及成本

- GPT可依常設授權**依序**啟動下一個小型任務並在嚴格CI/獨立驗收後合併；Claude Pro仍只用使用者已有訂閱額度，不能自動超額付費、Anthropic API Key、額外API計費或自行派Claude接單／讓Claude自動合併。
- GitHub Actions 的 .NET 建置是**獨立工程 CI**，不是 Claude Code，也不使用 Claude 訂閱額度；有可能消耗 GitHub Actions 分鐘。
- 先實際測通 .NET 8 SDK CI 才派第一個 M0 實作任務。臨時 SDK Smoke Test 的綠燈不等於專案有遊戲程式。
- 不預設私人倉庫的第三方 App／Secrets 有權訪問 Notion 或 Sheets。
