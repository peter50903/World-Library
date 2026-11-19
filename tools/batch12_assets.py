#!/usr/bin/env python3
"""第 08-29 章：第十二批素材（領地面板與建築）；第 08-40 章：也用來匯入第十三批（領地示意圖，加 --prefix B13_）。

  python3 tools/batch12_assets.py --list                  # 列出還沒交件的 B12_ 列（檔名、尺寸、提示詞）
  python3 tools/batch12_assets.py --import <交件資料夾>    # 匯入：縮到「規格」尺寸存到 godot/assets/<檔名>，狀態改「已交付」
  python3 tools/batch12_assets.py --prefix B13_ --import <交件資料夾>   # 第十三批（領地示意圖）

交件檔名＝manifest「檔名」最後一段（例 b12_smelter.png），交件資料夾的子資料夾也會找；
任何解析度都可以，長寬比要大致對。依「類型」處理：
- 面板建築、棋子：裁掉透明邊，等比縮進規格（BOX 平均），貼齊底邊、左右置中；
- 圖示：裁掉透明邊，等比縮進規格，置中；
- 牆段、介面：直接縮到規格（要跟第十一批同尺寸的牆段對齊、可以替換）；
- 縮小後限色（牆段、圖示 32 色，其他 48 色），透明度以一半為界（像素風不要半透明邊）。
- 第 08-40 章「插圖」（領地示意圖）：不透明；依規格的長寬比從中間裁（鋪滿），縮到規格，限 192 色存成 PNG。
需要 Pillow。
"""
from __future__ import annotations

import csv
import io
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "assets" / "manifest.csv"
ASSETS = ROOT / "godot" / "assets"
PREFIX = "B12_"


def rows() -> tuple[list[str], list[dict[str, str]]]:
    with MANIFEST.open(encoding="utf-8-sig", newline="") as fh:
        reader = csv.DictReader(fh)
        return list(reader.fieldnames or []), list(reader)


def size_of(spec: str) -> tuple[int, int]:
    m = re.match(r"\s*(\d+)\s*[×xX]\s*(\d+)", spec)
    if not m:
        raise ValueError(f"規格看不懂：{spec}")
    return int(m.group(1)), int(m.group(2))


def pixels(im) -> list:
    """逐像素（Pillow 12.3 起 getdata 改名 get_flattened_data，兩種版本都能用）。"""
    return list(im.get_flattened_data() if hasattr(im, "get_flattened_data") else im.getdata())


def limit_colors(im, colors: int):
    from PIL import Image

    alpha = im.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
    rgb = im.convert("RGB")
    opaque = [p for p, a in zip(pixels(rgb), pixels(alpha)) if a]
    if not opaque:
        return im
    strip = Image.new("RGB", (len(opaque), 1))
    strip.putdata(opaque)
    palette = strip.quantize(colors=max(2, min(colors, len(set(opaque)))), method=Image.Quantize.MEDIANCUT,
                             dither=Image.Dither.NONE)
    out = rgb.quantize(palette=palette, dither=Image.Dither.NONE).convert("RGBA")
    out.putalpha(alpha)
    return out


def fit(im, width: int, height: int, anchor: str):
    from PIL import Image

    box = im.getchannel("A").point(lambda v: 255 if v >= 128 else 0).getbbox()
    if box:
        im = im.crop(box)
    scale = min(width / im.width, height / im.height)
    if scale >= 1:
        k = max(1, int(scale))
        im = im.resize((im.width * k, im.height * k), Image.Resampling.NEAREST)
    else:
        im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.Resampling.BOX)
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    x = (width - im.width) // 2
    y = height - im.height if anchor == "bottom" else (height - im.height) // 2
    canvas.alpha_composite(im, (x, y))
    return canvas


def illustration(path: Path, width: int, height: int):
    """第 08-40 章：領地示意圖——依長寬比從中間裁（鋪滿），縮到規格，限色（檔案小一點）。"""
    from PIL import Image

    im = Image.open(path).convert("RGB")
    scale = max(width / im.width, height / im.height)
    crop_w, crop_h = round(width / scale), round(height / scale)
    left, top = (im.width - crop_w) // 2, (im.height - crop_h) // 2
    im = im.crop((left, top, left + crop_w, top + crop_h)).resize((width, height), Image.Resampling.LANCZOS)
    return im.quantize(colors=192, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")


def process(path: Path, kind: str, width: int, height: int):
    from PIL import Image

    if kind == "插圖":
        return illustration(path, width, height)
    im = Image.open(path).convert("RGBA")
    if kind in ("牆段", "介面"):
        im = im.resize((width, height), Image.Resampling.BOX if im.width > width else Image.Resampling.NEAREST)
        return limit_colors(im, 32)
    if kind == "圖示":
        return limit_colors(fit(im, width, height, "center"), 32)
    return limit_colors(fit(im, width, height, "bottom"), 48)


def main(argv: list[str]) -> int:
    fields, all_rows = rows()
    prefix = argv[argv.index("--prefix") + 1] if "--prefix" in argv and argv.index("--prefix") + 1 < len(argv) else PREFIX
    batch = [r for r in all_rows if r["ID"].startswith(prefix)]
    if "--list" in argv or not argv:
        todo = [r for r in batch if r["狀態"] == "待製作"]
        for r in todo:
            print(f"{r['檔名']}｜{r['規格']}｜{r['類型']}｜{r['提示詞']}")
        print(f"{prefix} {len(batch)} 張，待製作 {len(todo)} 張", file=sys.stderr)
        return 0
    if "--import" not in argv or argv.index("--import") + 1 >= len(argv):
        print(__doc__)
        return 2
    source = Path(argv[argv.index("--import") + 1])
    delivered = {p.name.lower(): p for p in source.rglob("*") if p.is_file() and p.suffix.lower() in (".png", ".webp", ".jpg")}
    done, missing = [], []
    for r in batch:
        name = Path(r["檔名"]).name.lower()
        stem = name.rsplit(".", 1)[0]
        match = delivered.get(name) or next((p for n, p in delivered.items() if n.rsplit(".", 1)[0] == stem), None)
        if match is None:
            if r["狀態"] == "待製作":
                missing.append(r["檔名"])
            continue
        width, height = size_of(r["規格"])
        out = ASSETS / r["檔名"]
        out.parent.mkdir(parents=True, exist_ok=True)
        process(match, r["類型"], width, height).save(out, optimize=True)
        if r["狀態"] != "已套用":
            r["狀態"] = "已交付"
        done.append(r["檔名"])
    buf = io.StringIO()
    writer = csv.DictWriter(buf, fieldnames=fields, lineterminator="\n")
    writer.writeheader()
    writer.writerows(all_rows)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")
    print(f"匯入 {len(done)} 張；還沒交 {len(missing)} 張")
    for m in missing:
        print(f"  缺：{m}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
