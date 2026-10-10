#!/usr/bin/env python3
"""TEMP 標記盤點（只讀，不修改任何檔案）。

列出 src/、godot/ 中每個含 `TEMP` 標記的程式檔與標記數，並可檢查
docs/TEMP_待裁決清單.md 的附錄 A 是否逐檔列出正確的標記行數（議題 #75 驗收：清單無遺漏）。

用法：
  python3 tools/temp_inventory.py             # 列出檔案與標記數
  python3 tools/temp_inventory.py --check     # 路徑或標記數不符時結束碼 1（CI 使用）
  python3 tools/temp_inventory.py --selftest  # 8 項反例與正例驗證
"""
import pathlib, sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SCAN_DIRS = ("src", "godot")
SUFFIXES = {".cs", ".gd", ".tscn"}
SKIP_PARTS = {"obj", "bin", ".godot"}
DOC = ROOT / "docs" / "TEMP_待裁決清單.md"


def scan(root):
    """回傳 {相對路徑: TEMP 標記數}，只含標記數大於 0 的檔案。"""
    found = {}
    for d in SCAN_DIRS:
        base = root / d
        if not base.is_dir():
            continue
        for p in sorted(base.rglob("*")):
            if p.suffix not in SUFFIXES or not p.is_file():
                continue
            if SKIP_PARTS.intersection(p.relative_to(root).parts):
                continue
            n = sum(line.count("TEMP") > 0 for line in p.read_text(encoding="utf-8").splitlines())
            if n:
                found[p.relative_to(root).as_posix()] = n
    return found


def check_appendix(found, doc_text):
    """只核對附錄 A 表格；回傳需要修正的具體問題（不改寫來源）。"""
    lines = doc_text.splitlines()
    start = next((i + 1 for i, line in enumerate(lines) if line.startswith("## 附錄 A")), None)
    if start is None:
        return ["找不到『## 附錄 A』標記清單"]

    listed = {}
    errors = []
    for line in lines[start:]:
        if line.startswith("## "):
            break
        if not line.lstrip().startswith("| `"):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) < 3 or not cells[0].startswith("`") or not cells[0].endswith("`"):
            errors.append(f"附錄 A 表格格式錯誤：{line}")
            continue
        path = cells[0][1:-1]
        if path in listed:
            errors.append(f"附錄 A 重複檔案：{path}")
            continue
        raw_count = cells[1]
        if not raw_count.isdecimal() or int(raw_count) < 1:
            errors.append(f"附錄 A 標記數不是正整數：{path} = {raw_count!r}")
            continue
        listed[path] = int(raw_count)

    for path in sorted(found.keys() - listed.keys()):
        errors.append(f"漏列檔案：{path}（實際 {found[path]} 行含 TEMP）")
    for path in sorted(listed.keys() - found.keys()):
        errors.append(f"過期檔案：{path}（附錄列 {listed[path]}，實際無 TEMP 或檔案不存在）")
    for path in sorted(found.keys() & listed.keys()):
        if found[path] != listed[path]:
            errors.append(f"標記數不符：{path}（附錄 {listed[path]}，實際 {found[path]}）")
    return errors


def selftest():
    """確認自我檢查本身能抓出新增行、漏列、虛列和錯誤的表格。"""
    found = {"src/A.cs": 2, "godot/B.cs": 1}
    def table(a=2, b=1):
        return ("# D\n只在其他段落提到 `src/A.cs` 不算\n## 附錄 A｜TEMP 檔案\n"
                "| 檔案 | 標記數 | 類型 |\n|---|---|---|\n"
                f"| `src/A.cs` | {a} | D1 |\n| `godot/B.cs` | {b} | D2 |\n")
    cases = {
        "正確數量通過": check_appendix(found, table()) == [],
        "新增 TEMP 行未更新文件會失敗": any("標記數不符" in s for s in check_appendix(found, table(1))),
        "路徑只在正文出現不算附錄": any("漏列檔案" in s for s in check_appendix(found, table().replace("| `src/A.cs` | 2 | D1 |\n", ""))),
        "漏列檔案會失敗": any("漏列檔案" in s for s in check_appendix(found, table().replace("| `godot/B.cs` | 1 | D2 |\n", ""))),
        "過期路徑會失敗": any("過期檔案" in s for s in check_appendix(found, table() + "| `src/Stale.cs` | 1 | D3 |\n")),
        "重複路徑會失敗": any("重複檔案" in s for s in check_appendix(found, table() + "| `src/A.cs` | 2 | D1 |\n")),
        "非整數標記數會失敗": any("不是正整數" in s for s in check_appendix(found, table("unknown"))),
        "缺少附錄會失敗": any("找不到" in s for s in check_appendix(found, "見 `src/A.cs`、`godot/B.cs`")),
    }
    for name, passed in cases.items():
        print(f"{'通過' if passed else '失敗'}：{name}")
    return 0 if all(cases.values()) else 1


def main(argv):
    if "--selftest" in argv:
        return selftest()
    found = scan(ROOT)
    for path, n in found.items():
        print(f"{n:4d}  {path}")
    print(f"共 {len(found)} 個檔案、{sum(found.values())} 行含 TEMP。")
    if "--check" in argv:
        if not DOC.is_file():
            print(f"錯誤：找不到 {DOC.relative_to(ROOT)}")
            return 1
        errors = check_appendix(found, DOC.read_text(encoding="utf-8"))
        for error in errors:
            print(f"錯誤：{error}")
        if errors:
            return 1
        print("通過：附錄 A 每個 TEMP 檔案的路徑與標記數均與原始碼一致。")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
