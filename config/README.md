# 正式設定檔

| 檔案 | 用途 |
|---|---|
| `世界模擬遊戲設定檔.xlsx` | **唯一正式設定檔。** 人用 Excel 修改這一個。 |
| `sheets/*.csv` | 每個分頁自動產生的文字副本，給程式、自動測試與 AI 讀取。**請勿手動修改。** |
| `sheets/_分頁清單.csv` | 分頁名稱、檔名、顯示狀態對照。 |

規則與理由在 Notion「02｜遊戲規則總綱」；與 Notion 衝突時以 Notion 為準。

## 使用者：怎麼修改設定檔

1. **下載最新版：** 點上方的 `世界模擬遊戲設定檔.xlsx`，再按右上角的「Download」（下載圖示）。一律從這裡下載，不要用電腦裡的舊檔或 Google Drive 的舊版。
2. **用 Excel 修改：** 照平常方式新增或修改資料，存檔，**檔名不要改**。
3. **上傳：** 回到這個 `config` 資料夾，按右上角「Add file」→「Upload files」，把改好的檔案拖進去。
4. **送出：** 頁面下方選「Create a new branch for this commit and start a pull request」，按綠色按鈕，再按「Create pull request」。
5. **完成：** 接下來不用做任何事。AI 會在下一輪輪值（一小時內）產生文字副本、逐格檢查你改了什麼，沒問題就合併；有疑問會在合併請求或 Notion 問答板問你。

## AI：怎麼讀與改

- **讀：** 只讀需要的 `sheets/<分頁名>.csv`，不要整本讀；中英文欄名對照見 `sheets/00B_術語對照.csv`。
- **改：** 用 openpyxl 修改 xlsx 後執行 `python3 tools/config_export.py`，把 xlsx 與 `sheets/` 一起提交，開合併請求由另一方審查（逐格差異看 `sheets/` 的 diff）。
- **使用者上傳的合併請求：** 由 Claude 在該分支執行 `python3 tools/config_export.py` 並推送，讓「設定檔同步檢查」通過，再審查 `sheets/` 的差異。
- **未定案的數值不得編造**，示範值放程式內並標 `TEMP`，不寫進這個檔案。
