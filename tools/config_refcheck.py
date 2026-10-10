#!/usr/bin/env python3
"""設定檔跨表引用檢查（只讀，不修改任何檔案）。

檢查 config/sheets/00E_AI任務讀取路由.csv：
1. 「Sheets必讀範圍（有界）」欄中的分頁代號（例如 44E!A4:H12、45A～45G、02_種族）
   必須是目前設定檔中存在的分頁。其他欄（Notion 入口、規格分冊編號、GitHub 來源）是純文字，不檢查。
2. 已刪除的分頁代號（00D、01B、00F、52F）與已刪除的 00A「分頁編號」欄，不得在 00E 任何一欄再被引用。
另檢查 00A_資料索引 的欄名中不再有「分頁編號」。

用法：
  python3 tools/config_refcheck.py             # 檢查正式 config/sheets/（CI 使用），有問題時結束碼 1
  python3 tools/config_refcheck.py --selftest  # 用內建反例確認檢查本身會抓到錯誤
"""
import csv, io, pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SHEETS = ROOT / "config" / "sheets"
ROUTE = "00E_AI任務讀取路由.csv"
RANGE_COL = "Sheets必讀範圍（有界）"
REMOVED_CODES = ("00D", "01B", "00F", "52F")  # 2026-10-10 PR #76 刪除的分頁
REMOVED_WORDS = ("分頁編號",)  # 2026-10-10 PR #76 刪除的 00A 欄位

# 分頁代號：兩位數字＋可選一個大寫字母，前後不能緊接英數字（避免把 A4、H12 之類的儲存格位置誤判）。
CODE = re.compile(r"(?<![0-9A-Za-z])(\d{2}[A-Z]?)(?![0-9A-Za-z])")


def sheet_codes(names):
    """由 CSV 檔名（例如 44E_xxx.csv、01_世界參數.csv）取出分頁代號集合。"""
    return {n.split("_", 1)[0] for n in names if not n.startswith("_")}


def check_route(route_text, codes):
    errors = []
    rows = list(csv.reader(io.StringIO(route_text)))
    if not rows:
        return ["00E 是空的"]
    header = rows[0]
    if RANGE_COL not in header:
        return [f"00E 缺少欄位「{RANGE_COL}」"]
    col = header.index(RANGE_COL)
    for i, r in enumerate(rows[1:], start=2):
        task = r[0] if r else ""
        cell = r[col] if col < len(r) else ""
        for code in CODE.findall(cell):
            if code not in codes:
                errors.append(f"00E 第 {i} 列「{task}」引用不存在的分頁 {code}：{cell}")
        for c, text in enumerate(r):
            for code in REMOVED_CODES:
                if code in CODE.findall(text):
                    errors.append(f"00E 第 {i} 列「{task}」第 {c + 1} 欄仍引用已刪除分頁 {code}")
            for word in REMOVED_WORDS:
                if word in text:
                    errors.append(f"00E 第 {i} 列「{task}」第 {c + 1} 欄仍引用已刪除欄位「{word}」")
    return errors


def check_index_header(index_text):
    header = next(csv.reader(io.StringIO(index_text)), [])
    return [f"00A 欄名仍有已刪除欄位「{w}」" for w in REMOVED_WORDS if w in header]


def run(sheets_dir):
    names = [p.name for p in sheets_dir.glob("*.csv")]
    codes = sheet_codes(names)
    errors = [f"已刪除分頁 {c} 又出現在設定檔" for c in REMOVED_CODES if c in codes]
    errors += check_route((sheets_dir / ROUTE).read_text(encoding="utf-8"), codes)
    index = next(sheets_dir.glob("00A_*.csv"), None)
    if index:
        errors += check_index_header(index.read_text(encoding="utf-8"))
    return errors


def selftest():
    codes = {"00A", "01", "02", "44E", "45A", "45G"}
    head = f"任務,最小Notion入口,{RANGE_COL},GitHub精確來源,加讀條件／禁忌,讀取優先級\n"
    good = head + "甲,Notion 02,44E!A4:H12；45A～45G；02_種族；01_世界參數!A1:H,無,49–64分冊,按需\n"
    cases = [
        ("正常路由", good, 0),
        ("引用不存在分頁", head + "乙,x,99Z!A1:B2,無,無,按需\n", 1),
        ("引用已刪除 00D", head + "丙,x,00D依稽核ID查單列,無,無,最後讀\n", 2),  # 不存在＋已刪除
        ("其他欄引用已刪除欄位", head + "丁,x,00A!A1:G,無,只查名稱／分頁編號,按需\n", 1),
        ("Notion 欄的數字不誤判", head + "戊,02｜規則總綱；03｜製作指示,01_世界參數,無,90–94對應,按需\n", 0),
    ]
    ok = True
    for name, text, expect in cases:
        got = len(check_route(text, codes))
        flag = "通過" if got == expect else "失敗"
        ok &= got == expect
        print(f"  [{flag}] {name}：預期 {expect} 個錯誤，實際 {got} 個")
    got = len(check_index_header("分頁名稱,分類,分頁編號\n"))
    ok &= got == 1
    print(f"  [{'通過' if got == 1 else '失敗'}] 00A 欄名含分頁編號：預期 1 個錯誤，實際 {got} 個")
    return ok


def main():
    if "--selftest" in sys.argv:
        ok = selftest()
        print("自我測試通過。" if ok else "自我測試失敗。")
        sys.exit(0 if ok else 1)
    errors = run(SHEETS)
    if errors:
        print("設定檔跨表引用檢查失敗：")
        for e in errors:
            print("  -", e)
        sys.exit(1)
    print("設定檔跨表引用檢查通過（00E 路由的分頁代號都存在，無已刪除分頁或欄位的引用）。")


if __name__ == "__main__":
    main()
