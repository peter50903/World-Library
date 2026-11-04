#!/usr/bin/env python3
"""美術收件匣匯入：把收件匣資料夾裡的圖片對到 assets/manifest.csv 的列，縮放到「規格」尺寸，存到 godot/assets/<檔名>。

- 檔名比對：收件匣檔名＝manifest「檔名」去掉斜線（例：ui/ui_icon_grid.png → uiui_icon_grid.png），或與檔名最後一段相同。
- 狀態：原本「已套用」的保持不變；其他改為「已交付」。
- 對不到的檔案只列出，不處理。
- 動畫（類型含「動畫」，第 08-16 章）：原圖橫向等分 4 格，取 4 格共同的不透明範圍裁切，每格等比縮進規格的單格大小、底部置中（不拉伸變形）。
- 地形紋理（類型＝地形紋理，第 06-4 章）：先縮到「規格÷4」的邏輯像素、把四邊接縫融合成可無縫拼接，再每像素放大 4 倍（保持像素風、不模糊）。
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


def seamless_pixel_texture(img: Image.Image, w: int, h: int, scale: int = 4) -> Image.Image:
    """縮到邏輯像素（w/scale×h/scale）→ 融合接縫（中央用原圖、四邊用位移半張的圖，位移後的圖四邊本來就連續）→ 最近鄰放大。"""
    lw, lh = max(1, w // scale), max(1, h // scale)
    small = img.resize((lw, lh), Image.Resampling.BOX)
    rolled = Image.new(small.mode, (lw, lh))
    hx, hy = lw // 2, lh // 2
    for (sx, sy, dx, dy) in ((0, 0, hx, hy), (hx, 0, 0, hy), (0, hy, hx, 0), (hx, hy, 0, 0)):
        rolled.paste(small.crop((sx, sy, sx + (lw - hx if sx == 0 else hx), sy + (lh - hy if sy == 0 else hy))), (dx, dy))
    band = max(1, min(lw, lh) // 4)
    mask = Image.new("L", (lw, lh))
    for y in range(lh):
        for x in range(lw):
            edge = min(x, lw - 1 - x, y, lh - 1 - y)
            mask.putpixel((x, y), min(255, edge * 255 // band))
    merged = Image.composite(small, rolled, mask)
    return merged.resize((lw * scale, lh * scale), Image.Resampling.NEAREST)


def animation_strip(img: Image.Image, w: int, h: int, frames: int = 4) -> Image.Image:
    """橫排動畫：等分成 frames 格，取共同的不透明範圍，每格等比縮進 (w/frames)×h、底部置中。"""
    # 先清掉幾乎透明的雜訊（GPT 常留一層 alpha 1～30 的背景），再取共同範圍。
    alpha = img.getchannel("A").point(lambda a: 0 if a < 48 else a)
    img = img.copy()
    img.putalpha(alpha)
    src_w = img.width // frames
    cells = [img.crop((k * src_w, 0, (k + 1) * src_w, img.height)) for k in range(frames)]
    boxes = [c.getchannel("A").getbbox() for c in cells]
    boxes = [b for b in boxes if b]
    if boxes:
        box = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
        cells = [c.crop(box) for c in cells]
    fw = w // frames
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for k, c in enumerate(cells):
        scale = min(fw / c.width, h / c.height)
        c = c.resize((max(1, round(c.width * scale)), max(1, round(c.height * scale))), Image.Resampling.LANCZOS)
        out.alpha_composite(c, (k * fw + (fw - c.width) // 2, h - c.height))
    return out


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
        transparent = "透明背景" in row["規格"]  # 「不透明」也含「透明」二字，要比對整個詞
        img = Image.open(f).convert("RGBA" if transparent else "RGB")
        if row["類型"].startswith("地形紋理"):
            img = seamless_pixel_texture(img, w, h)
        elif "動畫" in row["類型"]:
            img = animation_strip(img, w, h)
        elif img.size != (w, h):
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
