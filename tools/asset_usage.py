#!/usr/bin/env python3
"""第 08-29 章：素材盤點——哪些素材程式真的用到、哪些可以刪。

  python3 tools/asset_usage.py                  # 摘要＋用不到的檔案
  python3 tools/asset_usage.py --check          # CI：程式寫到或登記的素材都要有檔案（manifest 待製作的除外）、登記清單沒有用不到的名字
  python3 tools/asset_usage.py --sync           # 依盤點改寫 manifest「已使用」（用到＝V、已交付→已套用；沒用到＝空白、已套用→已交付）
  python3 tools/asset_usage.py --delete-unused  # 刪掉用不到的檔案（連同 .import）與 manifest 對應列，並刪掉沒有檔案也沒人用的待製作列
  python3 tools/asset_usage.py --registry       # 依盤點重寫 godot/Scripts/PixelParts.Batch5.cs（px2/ 用到與保留的檔案）

「用到」＝
1. godot/Scripts/*.cs（去掉註解）直接寫的 "res://assets/…" 路徑；
2. ArtAssets.cs 的常數 `const string X = "res://assets/…"`，X 在宣告與登記清單以外有被引用；
3. 字串常值等於某個素材的檔名主幹（px2/、px/、audio/sfx2/、audio/music2/ 底下，去掉副檔名），而且出現在登記清單以外；
4. 程式組檔名的整族：icons/tech_*.png（ArtAssets.TechDomainIcon）、emblems/、portraits/（Heraldry）、fonts/、holding/b13_*（第 08-40 章領地示意圖）；
5. assets/保留素材.csv 的樣式：已排定要用、還沒接上畫面的素材（寫明用途與章節）。
登記清單只是冒煙測試逐一讀取的清單，本身不算用到：ArtAssets.Used、ArtAssets.Batch5Audio、PixelParts.Batch5、PixelParts.Names。
"""
from __future__ import annotations

import csv
import fnmatch
import io
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = ROOT / "godot" / "Scripts"
ASSETS = ROOT / "godot" / "assets"
MANIFEST = ROOT / "assets" / "manifest.csv"
RESERVED = ROOT / "assets" / "保留素材.csv"
NAMED_DIRS = ("px2", "px", "audio/sfx2", "audio/music2")
FAMILIES = ("icons/tech_*.png", "emblems/*", "portraits/*", "fonts/*", "holding/b13_*.png")


def strip_comments(text: str) -> str:
    """去掉 // 與 /* */ 註解（字串裡的 // 不算）。"""
    out, i, n = [], 0, len(text)
    in_str = verbatim = False
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if verbatim:
                if c == '"' and i + 1 < n and text[i + 1] == '"':
                    out.append('"')
                    i += 1
                elif c == '"':
                    in_str = False
            elif c == "\\":
                out.append(text[i + 1] if i + 1 < n else "")
                i += 1
            elif c == '"' or c == "\n":
                in_str = False
            i += 1
            continue
        if text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == '"':
            in_str = True
            verbatim = i > 0 and text[i - 1] == "@"
        elif c == "'" and i + 2 < n and text[i + 2] == "'":
            out.append(text[i:i + 3])
            i += 3
            continue
        out.append(c)
        i += 1
    return "".join(out)


def cut(text: str, start: str, end: str) -> tuple[str, str]:
    """把 text 裡從 start 到其後第一個 end 的片段切出來：回傳（剩下的、切出的）。"""
    a = text.find(start)
    if a < 0:
        return text, ""
    b = text.find(end, a)
    b = len(text) if b < 0 else b + len(end)
    return text[:a] + text[b:], text[a:b]


def literals(text: str) -> list[str]:
    return [m.group(1) for m in re.finditer(r'"((?:[^"\\\n]|\\.)*)"', text)]


def load_code() -> tuple[str, list[tuple[str, str]], dict[str, str]]:
    """回傳（一般程式、登記清單〔（種類 image／audio／any, 內容）〕、ArtAssets 常數名→相對路徑）。"""
    code, registry, consts = [], [], {}
    for path in sorted(SCRIPTS.glob("*.cs")):
        text = strip_comments(path.read_text(encoding="utf-8"))
        if path.name == "PixelParts.Batch5.cs":
            registry.append(("image", text))
            continue
        if path.name == "PixelParts.cs":
            text, part = cut(text, "public static readonly string[] Names", "};")
            registry.append(("image", part))
        if path.name == "ArtAssets.cs":
            text, part = cut(text, "public static readonly string[] Used", ".ToArray();")
            registry.append(("any", part))
            text, part = cut(text, "private static IEnumerable<string> Batch5Audio", ";\n")
            registry.append(("audio", part))
            for m in re.finditer(r'const string (\w+)\s*=\s*"res://assets/([^"]+)";', text):
                consts[m.group(1)] = m.group(2)
            text = re.sub(r'const string \w+\s*=\s*"res://assets/[^"]+";', "", text)
        code.append(text)
    return "\n".join(code), registry, consts


def asset_files() -> list[str]:
    return sorted(p.relative_to(ASSETS).as_posix() for p in ASSETS.rglob("*")
                  if p.is_file() and p.suffix != ".import")


def stem_index(files: list[str]) -> dict[str, list[str]]:
    index: dict[str, list[str]] = {}
    for f in files:
        folder, _, name = f.rpartition("/")
        if folder in NAMED_DIRS:
            index.setdefault(name.rsplit(".", 1)[0], []).append(f)
    return index


def reserved_patterns() -> list[tuple[str, str]]:
    if not RESERVED.exists():
        return []
    with RESERVED.open(encoding="utf-8-sig", newline="") as fh:
        return [(r["樣式"].strip(), r["用途"].strip()) for r in csv.DictReader(fh) if r.get("樣式", "").strip()]


def manifest_rows() -> tuple[list[str], list[dict[str, str]]]:
    with MANIFEST.open(encoding="utf-8-sig", newline="") as fh:
        reader = csv.DictReader(fh)
        return list(reader.fieldnames or []), list(reader)


def survey() -> dict:
    code, registry, consts = load_code()
    files = asset_files()
    present = set(files)
    stems = stem_index(files)
    used: dict[str, str] = {}
    referenced: set[str] = set()   # 程式寫到的路徑（含還沒交件的）

    def mark(path: str, why: str) -> None:
        if path.endswith("/"):
            return
        referenced.add(path)
        if path in present:
            used.setdefault(path, why)

    for lit in literals(code):
        if lit.startswith("res://assets/") and "{" not in lit:
            path = lit[len("res://assets/"):]
            if any(pattern.startswith(path) for pattern in FAMILIES):
                continue  # 程式組檔名的整族（例：第 08-40 章 holding/b13_{等級}_{地形}_{款式}.png）只寫了開頭
            mark(path, "程式路徑")
    for name, path in consts.items():
        if re.search(r"\b" + re.escape(name) + r"\b", code):
            mark(path, f"ArtAssets.{name}")
    for m in re.finditer(r'"((?:[^"\\\n]|\\.)*)"', code):
        paths = stems.get(m.group(1), [])
        if len({p.startswith("audio/") for p in paths}) > 1:
            # 同名的音效與圖（例：ui_tab）：同一行前面有播音效的呼叫就算音效，否則算圖。
            line = code[code.rfind("\n", 0, m.start()) + 1:m.start()]
            audio = re.search(r"\b(PlayUi|Sfx2|Music2|Audio|PlaySfx)\(", line) is not None
            paths = [p for p in paths if p.startswith("audio/") == audio]
        for p in paths:
            mark(p, "名稱")
    for f in files:
        if any(fnmatch.fnmatch(f, pattern) for pattern in FAMILIES):
            used.setdefault(f, "整族")
    reserved = reserved_patterns()
    for f in files:
        for pattern, why in reserved:
            if fnmatch.fnmatch(f, pattern):
                used.setdefault(f, "保留：" + why)
                break
    # 登記清單裡的名字與路徑（要有檔案；不能是用不到的）
    registered: set[str] = set()
    for kind, part in registry:
        for lit in literals(part):
            if lit.startswith("res://assets/"):
                registered.add(lit[len("res://assets/"):])
                continue
            for p in stems.get(lit, []):
                if kind == "any" or p.startswith("audio/") == (kind == "audio"):
                    registered.add(p)
            if lit not in stems and re.fullmatch(r"[a-z0-9_]+", lit):
                registered.add(f"?{lit}")
        for name in re.findall(r"\b([A-Z]\w+)\b", part):
            if name in consts:
                registered.add(consts[name])
    unused = [f for f in files if f not in used]
    return {"files": files, "used": used, "unused": unused, "referenced": referenced,
            "registered": registered, "consts": consts, "reserved": reserved}


def pending_paths() -> set[str]:
    _, rows = manifest_rows()
    return {r["檔名"] for r in rows if r["狀態"] == "待製作"}


def check(s: dict) -> int:
    present = set(s["files"])
    pending = pending_paths()
    errors = []
    for p in sorted(s["referenced"] | {r for r in s["registered"] if not r.startswith("?")}):
        if p not in present and p not in pending:
            errors.append(f"缺檔：{p}（程式或登記清單寫到，manifest 也不是待製作）")
    for p in sorted(r for r in s["registered"] if not r.startswith("?") and r in present and r not in s["used"]):
        errors.append(f"登記清單有用不到的素材：{p}（從登記清單拿掉，或接上畫面／列進 assets/保留素材.csv）")
    for e in errors:
        print(f"::error::{e}")
    for p in s["unused"]:
        print(f"::warning::用不到的素材：godot/assets/{p}（第 08-29 章：python3 tools/asset_usage.py --delete-unused）")
    print(f"素材 {len(s['files'])} 個：用到 {len(s['used'])}、用不到 {len(s['unused'])}；錯誤 {len(errors)}")
    return 1 if errors else 0


def write_manifest(fields: list[str], rows: list[dict[str, str]]) -> None:
    buf = io.StringIO()
    writer = csv.DictWriter(buf, fieldnames=fields, lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")


def sync(s: dict) -> int:
    fields, rows = manifest_rows()
    changed = 0
    for r in rows:
        path = r["檔名"]
        if r["狀態"] == "待製作" or path not in set(s["files"]):
            continue
        in_use = path in s["used"] and not s["used"][path].startswith("保留")
        want = ("V" if in_use else "", "已套用" if in_use else ("已交付" if r["狀態"] == "已套用" else r["狀態"]))
        if (r["已使用"], r["狀態"]) != want:
            r["已使用"], r["狀態"] = want
            changed += 1
    write_manifest(fields, rows)
    print(f"manifest 改了 {changed} 列")
    return 0


def delete_unused(s: dict) -> int:
    gone = set(s["unused"])
    for p in s["unused"]:
        for f in (ASSETS / p, ASSETS / (p + ".import")):
            if f.exists():
                f.unlink()
    for d in sorted((p for p in ASSETS.rglob("*") if p.is_dir()), key=lambda p: -len(p.parts)):
        if not any(d.iterdir()):
            d.rmdir()
    fields, rows = manifest_rows()
    referenced = s["referenced"] | s["registered"]
    reserved = [p for p, _ in s["reserved"]]
    keep = []
    for r in rows:
        path = r["檔名"]
        stale_pending = (r["狀態"] == "待製作" and path not in referenced
                         and not any(fnmatch.fnmatch(path, p) for p in reserved))
        if path in gone or stale_pending:
            continue
        keep.append(r)
    write_manifest(fields, keep)
    print(f"刪掉 {len(gone)} 個檔案、manifest {len(rows) - len(keep)} 列")
    return 0


REGISTRY_GROUPS = (
    ("第 09-1a 章：第十一批（地圖符號；第 08-29 章領地面板保留的建築、圍牆、地面、小物件）", lambda n: n.startswith("b11_")),
    ("第 08-29 章：第十二批（領地面板缺的建築、廢墟、施工、圖示與棋子）", lambda n: n.startswith("b12_")),
    ("第 08-18d 章：第五、六批（圖示、棋子、介面、時代插圖；第 08-29 章領地面板保留的火、煙、裂痕、瓦礫）",
     lambda n: not n.startswith(("b11_", "b12_"))),
)


def write_registry(s: dict) -> int:
    keep = sorted(f[len("px2/"):-len(".png")] for f in s["used"] if f.startswith("px2/") and f.endswith(".png"))
    lines = ["namespace WorldSimulation.GodotClient;", "",
             '/// <summary>第 08-18d 章：像素素材（res://assets/px2/，密度每格半徑 <see cref="TempTexelsPerRadius"/> 圖像素）。',
             "/// 第 08-29 章：只登記程式用到與 assets/保留素材.csv 保留的（由 tools/asset_usage.py --registry 產生、--check 檢查）。</summary>",
             "public static partial class PixelParts", "{", "    public static readonly string[] Batch5 =", "    {"]
    for title, wanted in REGISTRY_GROUPS:
        names = [n for n in keep if wanted(n)]
        if not names:
            continue
        lines.append(f"        // {title}")
        for i in range(0, len(names), 8):
            lines.append("        " + ", ".join(f'"{n}"' for n in names[i:i + 8]) + ",")
    lines[-1] = lines[-1].rstrip(",")
    lines += ["    };", "}", ""]
    (SCRIPTS / "PixelParts.Batch5.cs").write_text("\n".join(lines), encoding="utf-8")
    print(f"PixelParts.Batch5 登記 {len(keep)} 個")
    return 0


def main(argv: list[str]) -> int:
    s = survey()
    if "--check" in argv:
        return check(s)
    if "--sync" in argv:
        return sync(s)
    if "--delete-unused" in argv:
        return delete_unused(s)
    if "--registry" in argv:
        return write_registry(s)
    for p in s["unused"]:
        print(p)
    print(f"素材 {len(s['files'])} 個：用到 {len(s['used'])}、用不到 {len(s['unused'])}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
