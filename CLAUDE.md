# 開發規範

**每次開發只讀：`docs/manual/00_總綱.md` ＋ 當前章節（總綱第 9 節目錄）＋ 該章列出的程式檔與設定列。**

- Notion 舊頁面（01～11、工作卡、問答板、工作日誌）已封存，不要讀、不要寫。
- 不寫工作日誌；進度只記在 commit 訊息與 PR 說明（格式見總綱第 6 節），完成章節時更新總綱目錄狀態。
- 規則有衝突或要修改時，依總綱第 2 節新增分支章節，不改舊文。
- 程式與數值由 Claude 決定；概念衝突才問使用者（總綱第 3 節）。
- 素材依 `assets/manifest.csv`（總綱第 7 節）；開發開始時先檢查使用者電腦上的 `D:\DESK\godot\世界圖書館_素材\世界圖書館_素材收件匣`（雲端收件匣的同步資料夾，用電腦連線工具直接複製；雲端工具下載大圖太耗量）。

建置與測試：
```bash
dotnet restore WorldSimulation.sln
dotnet build WorldSimulation.sln --no-restore --configuration Release
dotnet test WorldSimulation.sln --no-build --configuration Release
```
