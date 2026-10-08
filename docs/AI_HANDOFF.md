# AI 交接

本檔已精簡。現行流程、任務順序與禁止事項以 Notion
[「03｜給 GPT 的下一步製作指示」](https://app.notion.com/p/3f36939dab0981ba9906efe357a45813) 為準，
遊戲規則以 [「02｜遊戲規則總綱」](https://app.notion.com/p/3f36939dab09811a8e1bf888e2f9030a) 為準。

## 每個任務的固定流程

1. 開 Issue：使用者會看到什麼、範圍、驗收方式、規則出處（總綱節號＋必要的單一 Sheets 範圍）。
2. 開分支實作並加測試；PR 說明「如何親眼確認這個功能」。
3. 同一 commit 的 CI 全部通過、審查 diff 後才合併。
4. 更新 Notion「01｜目前實作狀態」那一段與 Sheets `00F`，再開下一個 Issue。
5. 規則沒寫或需要新設計決定時，不自行發明，列入「待使用者裁決」清單，先做不受影響的部分。

## 交給 Claude Code 時

由使用者把單一 Issue 連結貼給 Claude Code。Claude Code 先讀本倉庫 `CLAUDE.md` 與上述兩頁，只做該 Issue 範圍內的事。
