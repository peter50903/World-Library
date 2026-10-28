#!/usr/bin/env python3
"""美術收件匣匯入：把收件匣資料夾裡的圖片對到 assets/manifest.csv 的列，縮放到「規格」尺寸，存到 godot/assets/<檔名>。

- 檔名比對：收件匣檔名＝manifest「檔名」去掉斜線（例：ui/ui_icon_grid.png → uiui_icon_grid.png），或與檔名最後一段相同。
- 狀態：原本「已套用」的保持不變；其他改為「已交付」。
- 對不到的檔案只列出，不處理。
用法：python3 tools/art_inbox.py <收件匣資料夾>
需要 Pillow。由 .github/workflows/art-inbox.yml 在 GitHub 上執行（雲端硬碟只能從 GitHub 下載）。
"""
from __future__ import annotations

import csv
import io
import re
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "assets" / "manifest.csv"
ASSET_DIR = ROOT / "godot" / "assets"


def target_size(spec: str) -> tuple[int, int]:
    m = re.search(r"(\d+)\s*[×xX]\s*(\d+)", spec)
    if not m:
        raise ValueError(f"規格欄找不到尺寸：{spec}")
    return int(m.group(1)), int(m.group(2))


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    inbox = Path(sys.argv[1])
    text = MANIFEST.read_text(encoding="utf-8")
    rows = list(csv.DictReader(io.StringIO(text)))
    fields = list(rows[0].keys())
    by_flat = {r["檔名"].replace("/", ""): r for r in rows}
    by_base = {}
    for r in rows:
        by_base.setdefault(Path(r["檔名"]).name, []).append(r)

    done, unknown = [], []
    for f in sorted(inbox.rglob("*")):
        if not f.is_file() or f.suffix.lower() not in {".png", ".jpg", ".jpeg", ".webp"}:
            continue
        row = by_flat.get(f.stem + ".png")
        if row is None:
            candidates = by_base.get(f.stem + ".png", [])
            row = candidates[0] if len(candidates) == 1 else None
        if row is None:
            unknown.append(f.name)
            continue
        w, h = target_size(row["規格"])
        transparent = "透明" in row["規格"]
        img = Image.open(f).convert("RGBA" if transparent else "RGB")
        if img.size != (w, h):
            img = img.resize((w, h), Image.Resampling.LANCZOS)
        out = ASSET_DIR / row["檔名"]
        out.parent.mkdir(parents=True, exist_ok=True)
        img.save(out, format="PNG", optimize=True)
        if row["狀態"] != "已套用":
            row["狀態"] = "已交付"
        done.append(f"{row['ID']} ← {f.name}（{w}×{h}）")

    buf = io.StringIO()
    writer = csv.DictWriter(buf, fieldnames=fields, lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")
    print(f"匯入 {len(done)} 張：")
    for line in done:
        print("  " + line)
    if unknown:
        print(f"對不到美術清單的檔案 {len(unknown)} 個：" + "、".join(unknown))
    return 0


if __name__ == "__main__":
    sys.exit(main())
