# World-Simulation-Game

《世界模擬遊戲》正式實作倉庫（2026-10-08 **重新建立**；未沿用前次實驗倉庫的 Git 歷史）。

## 正式資料來源

- **Notion**：遊戲規則、設計理由、決策、變更紀錄。https://app.notion.com/p/3ee6939dab0981f2b812e6636e103213
- **Google Sheets**：結構化設定、參數、可供程式讀取的資料。https://docs.google.com/spreadsheets/d/136wf0Deyl9oAt4vTSIxwZXu3HUZZt7lrj9rX3Ja91Rw/edit
- 從 `00D_設計總覽與待辦` 定位規格，再由 `00A_資料索引` 查閱需要的工作表。
- **GitHub**：程式碼、測試、工程 Issue、PR；不取代 Notion／Sheets 的正式規則。

## 固定開發分工

GPT 依正式規則提出小範圍企劃與可執行方案 → Claude Code 先做短版技術可行性審查 → 可行才在當次已授權範圍內實作 → CI 執行真實測試 → GPT 驗收 → 使用者確認合併。詳見 [CLAUDE.md](CLAUDE.md) 與 [docs/AI_HANDOFF.md](docs/AI_HANDOFF.md)。

## 技術方向

Godot 2D + C# 呈現，獨立純 .NET 的 `WorldSimulation.Core` Headless 世界模擬核心，不引用 Godot API。分階段從 M0 開始，不一次製作整個世界。完整 M0 設計仍以正式 Notion／Sheets 為準。

## 目前狀態

**只建立工程規範與 .NET 8 CI 開發環境；尚無任何正式遊戲程式，也尚未宣稱 M0 完成。** 舊倉庫中未驗證的 .NET 骨架與 PR 不視為此新倉庫的已通過成果。

[.NET 8 CI](.github/workflows/dotnet-8-ci.yml) 初期僅檢查 SDK 能否建置臨時 class library。未加入真正的世界模擬 solution 前，**綠燈不代表遊戲測試已通過**。建置及測試過程可能消耗 GitHub Actions 分鐘數，但不會消耗 Claude Pro 額度。
