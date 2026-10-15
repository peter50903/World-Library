# 正式設定檔

| 檔案 | 用途 |
|---|---|
| `世界模擬遊戲設定檔.xlsx` | **唯一正式設定檔。** 人用 Excel 修改這一個。 |
| `sheets/*.csv` | 每個分頁自動產生的文字副本，給程式、自動測試與 AI 讀取。**請勿手動修改。** |
| `sheets/_分頁清單.csv` | 分頁名稱、檔名、顯示狀態對照。 |

規則與理由在 `docs/manual/`（開發手冊）；與手冊衝突時以手冊為準。

## 使用者：怎麼修改設定檔

1. **下載最新版：** 點上方的 `世界模擬遊戲設定檔.xlsx`，再按右上角的「Download」（下載圖示）。一律從這裡下載，不要用電腦裡的舊檔或 Google Drive 的舊版。
2. **用 Excel 修改：** 照平常方式新增或修改資料，存檔，**檔名不要改**。
3. **上傳：** 回到這個 `config` 資料夾，按右上角「Add file」→「Upload files」，把改好的檔案拖進去。
4. **送出：** 頁面下方選「Create a new branch for this commit and start a pull request」，按綠色按鈕，再按「Create pull request」。
5. **完成：** 下次請 Claude 開發時告訴它有設定檔修改的合併請求，Claude 會產生文字副本、逐格檢查你改了什麼；有疑問會在合併請求或對話中問你，最後由你按合併。

## AI：怎麼讀與改

- **讀：** 只讀需要的 `sheets/<分頁名>.csv`，不要整本讀；中英文欄名對照見 `sheets/00B_術語對照.csv`。
- **改：** 用 openpyxl 修改 xlsx 後依序執行 `python3 tools/config_compact.py`（重新壓縮、去除格式殘留）與 `python3 tools/config_export.py`，再執行 `python3 tools/config_refcheck.py`（跨表引用檢查），把 xlsx 與 `sheets/` 一起提交，開合併請求（逐格差異看 `sheets/` 的 diff）。
- **使用者上傳的合併請求：** 由 Claude 在該分支執行 `python3 tools/config_compact.py` 與 `python3 tools/config_export.py` 並推送，讓「設定檔同步檢查」通過，再審查 `sheets/` 的差異。
- **未定案的數值**可以先填暫定值，但該列「說明」（或「數值狀態」「Status」）開頭必須標【TEMP】；不清楚的內容以開發手冊為準，不相符的刪除。

## 不放進設定檔的東西

設定檔只放遊戲要用的 ID、數值、列舉、模板與規則細節。以下不放：工程進度（看 Git 提交紀錄）、修改歷史與整理紀錄（看 Git 提交紀錄）、已作廢或未採用的方案（決策過程記在開發手冊與 Git 歷史）、封存與審查摘要。2026-10-10 已依此清理一次。
