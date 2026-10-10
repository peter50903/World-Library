#!/usr/bin/env python3
"""TEMP 標記盤點（只讀，不修改任何檔案）。

列出 src/、godot/ 中每個含 `TEMP` 標記的程式檔與標記數，並可檢查
docs/TEMP_待裁決清單.md 的附錄 A 是否列出了所有這些檔案（議題 #75 驗收：清單無遺漏）。

用法：
  python3 tools/temp_inventory.py             # 列出檔案與標記數
  python3 tools/temp_inventory.py --check     # 清單漏列任何檔案時結束碼 1（CI 使用）
  python3 tools/temp_inventory.py --selftest  # 用內建反例確認檢查本身會抓到漏列
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


def missing(found, doc_text):
    """清單文字中沒有以 `路徑` 形式出現的檔案。"""
    return sorted(path for path in found if f"`{path}`" not in doc_text)


def selftest():
    found = {"src/A.cs": 2, "godot/B.cs": 1}
    ok = missing(found, "| `src/A.cs` | 2 |\n| `godot/B.cs` | 1 |") == []
    caught = missing(found, "| `src/A.cs` | 2 |") == ["godot/B.cs"]
    # 只出現在別的路徑字串裡（沒有反引號包住完整路徑）不算列出。
    partial = missing({"src/A.cs": 1}, "見 src/A.cs.bak") == ["src/A.cs"]
    results = {"完整清單通過": ok, "漏列被抓到": caught, "部分字串不算列出": partial}
    for name, passed in results.items():
        print(f"{'通過' if passed else '失敗'}：{name}")
    return 0 if all(results.values()) else 1


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
        lost = missing(found, DOC.read_text(encoding="utf-8"))
        if lost:
            print("錯誤：以下含 TEMP 的檔案沒有列在 docs/TEMP_待裁決清單.md 附錄 A：")
            for path in lost:
                print(f"  {path}")
            return 1
        print("通過：清單已列出所有含 TEMP 的檔案。")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
