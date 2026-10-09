#!/usr/bin/env python3
"""把正式設定檔 config/世界模擬遊戲設定檔.xlsx 的每個分頁匯出成 config/sheets/<分頁名>.csv。

用途：讓程式、自動測試與 AI 只讀需要的分頁，並讓每次修改都能逐格看出差異。
用法：
  python3 tools/config_export.py           # 重新產生 config/sheets/
  python3 tools/config_export.py --check   # 只檢查是否已同步（CI 使用），不同步時結束碼 1
需要 openpyxl（pip install openpyxl）。
"""
import csv, datetime, io, pathlib, sys
import openpyxl

ROOT = pathlib.Path(__file__).resolve().parent.parent
XLSX = ROOT / "config" / "世界模擬遊戲設定檔.xlsx"
OUT = ROOT / "config" / "sheets"


def cell_text(v):
    if v is None:
        return ""
    if isinstance(v, bool):
        return "TRUE" if v else "FALSE"
    if isinstance(v, float) and v.is_integer():
        return str(int(v))
    if isinstance(v, (datetime.datetime, datetime.date, datetime.time)):
        return v.isoformat()
    return str(v)


def render(ws):
    rows = []
    for row in ws.iter_rows(values_only=True):
        rows.append([cell_text(v) for v in row])
    while rows and not any(rows[-1]):  # 去掉尾端空白列
        rows.pop()
    width = max((max((i + 1 for i, x in enumerate(r) if x), default=0) for r in rows), default=0)
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\n")
    for r in rows:
        w.writerow((r + [""] * width)[:width])
    return buf.getvalue()


def safe_name(name):
    return "".join("_" if ch in '/\\:*?"<>|' else ch for ch in name) + ".csv"


def build():
    wb = openpyxl.load_workbook(XLSX)
    files = {safe_name(ws.title): render(ws) for ws in wb.worksheets}
    index = io.StringIO()
    w = csv.writer(index, lineterminator="\n")
    w.writerow(["順序", "分頁名稱", "檔名", "顯示狀態"])
    for i, ws in enumerate(wb.worksheets, 1):
        w.writerow([i, ws.title, safe_name(ws.title), ws.sheet_state])
    files["_分頁清單.csv"] = index.getvalue()
    return files


def main():
    check = "--check" in sys.argv
    files = build()
    existing = {p.name: p for p in OUT.glob("*.csv")} if OUT.exists() else {}
    if check:
        bad = [n for n, t in files.items() if n not in existing or existing[n].read_text(encoding="utf-8") != t]
        extra = [n for n in existing if n not in files]
        if bad or extra:
            print("config/sheets/ 與 Excel 不同步。請執行 python3 tools/config_export.py 後提交。")
            for n in (bad + extra)[:30]:
                print("  -", n)
            sys.exit(1)
        print(f"config/sheets/ 與 Excel 同步（{len(files)} 個檔案）。")
        return
    OUT.mkdir(parents=True, exist_ok=True)
    for n in extra_names(existing, files):
        existing[n].unlink()
    for n, t in files.items():
        (OUT / n).write_text(t, encoding="utf-8")
    print(f"已產生 {len(files)} 個檔案到 config/sheets/。")


def extra_names(existing, files):
    return [n for n in existing if n not in files]


if __name__ == "__main__":
    main()
