# Claude Code — World-Simulation-Game 開發規範

## 權威與責任

- Notion 正式規則／理由／決策與變更：https://app.notion.com/p/3ee6939dab0981f2b812e6636e103213
- Google Sheets 程式可讀設定／參數：https://docs.google.com/spreadsheets/d/136wf0Deyl9oAt4vTSIxwZXu3HUZZt7lrj9rX3Ja91Rw/edit
- 先用 `00D_設計總覽與待辦`、`00A_資料索引` 查出本項所需最小資料，不掃描全部分頁。未讀到的規則不得自行虛構。
- 使用者在 2026-10-09 已明確常設委託 GPT 自主完成**符合正式規則的限定技術微任務**：規劃、Issue、實作、測試、PR、獨立審查、在同Head完整CI成功後受保護合併、Issue收尾與Notion+Sheets同步。無需每次再請使用者回覆「合併」。**既定世界規則與世界數值不得任意改寫**；Claude Code只有真的需要外部人工作業或特定跨模組交接時才由使用者協助連接。
- 世界規則／參數不在GPT的工程自動授權可擅改範圍內；遇到尚未定案的世界內容先標記阻擋、轉做不受阻的基礎任務，不編造正式值。真正獲准變更規則時當輪同步 Notion＋Sheets，Claude 不得擅自更動權威來源。

## 每一項 GPT／Claude 有界工程任務：先 Preflight

讀取此次GPT常設委託內的單一有限 Issue、此檔以及指定的少量規格，花最少可行的上下文完成以下檢查：
1. 是否符合權威世界規則？是否缺規格／必須新增設計決定？
2. 目標程式範圍、技術版本和相依套件是否可行？是否有可存取的執行環境、SDK、權限？
3. 是否能**實際**執行預定的編譯與測試？若當前 Claude 環境沒有工具，有沒有已經核准且可用的 GitHub Actions CI 測試途徑？
4. 是否能在本輪小型任務範圍內完成，不會意外擴大任務或消耗過多 Pro 額度？

以**短文**報告：`FEASIBLE`／`FEASIBLE_WITH_LIMITATIONS`／`BLOCKED`／`NEEDS_DESIGN_DECISION`，附上證據及阻礙。只有前兩者，且限制不影響已有正式技術契約與驗收要求時，GPT才直接實作**當前一項微任務**；Claude接受交接時亦限單一任務。若阻擋或需要新決策，立即停下，不先大量寫碼。

## 實作與驗收

- Godot 2D + C# 是呈現層；純 .NET `WorldSimulation.Core` 不引用任何 Godot API；從最小 M0 開始。
- 世界 Canonical State 僅合法 Atomic Commit 更新；時間單調、ID 不重用、History 不改寫；RNG／Save／Ledger 必須遵守正式規則（**只有任務需實作相關模組時才讀相應詳細設定**）。
- GPT/Claude 建立功能分支；修改只限當次有界 Issue；留下真實 build／test 指令與結果。GPT讀取diff與新 Head CI原始證據獨立審查，只有安全閘門全通過才可依使用者**2026-10-09常設授權**呼叫受 `expected_head_sha` 鎖定的合併，並讀回main/PR/Issue，同輪同步Notion+Sheets。Claude不得自行合併或修改權威來源。
- `CI passed` 需檢查實際執行的 test；單純 SDK 環境 Smoke Test 不代表完成遊戲功能。若工具不足或測試未執行，清楚標記 `partial / blocked`，不得聲稱驗收通過。
- 不把舊同名倉庫過去的實驗 PR、檔案或 Issue 當新倉庫的已完成實作。

## Claude Pro 額度與授權

- 此專案採 **Claude Pro 訂閱內使用額度**。GPT 無法讀取剩餘額度，不得聲稱能精確預估任務成本。
- **GPT一次僅自主處理一個有界工程微任務**，不需逐次請示合併或下一Issue；Preflight可行時直接實作。Claude真正需人工作業時才請使用者溝通／交接；若額度、CI、SDK或權限阻擋，留下checkpoint，不虛稱完成。
- GPT可在**完成前一微任務驗收／合併及權威同步之後**自行選擇下一個有界Issue；不得同時展開無界多任務、背景循環重試或把整個M0一次實作。Claude僅接受被明確交接的一項任務，不會自行擴張。
- 不啟用額外付費用量、Anthropic API 金鑰、自動儲值、GitHub Claude自動接單。**GPT在新Head CI成功、diff獨立審查通過、scope符合正式規則且未遭平台阻擋後，可依使用者2026-10-09常設授權自行merge受保護PR**；Claude不能自行merge，也不可默認已被啟動或已消耗Pro額度。
- 不把私有 token／key 或整份私人 Sheets dump 寫到 Issue、PR 或 git commit。

## 入口

工程交接見 `docs/AI_HANDOFF.md`；新微任務由 GPT 按Notion+Sheets既定規則與本次常設授權依序自主啟動。**只有確實需要與Claude Code/外部帳號互動的階段才通知使用者**；程式寫入、驗收及合併不靠每張PR反覆人工批准。
