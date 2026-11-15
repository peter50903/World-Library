"""第 09-1a 章：第十一批素材匯入。

  python3 tools/batch11_assets.py --import <交件資料夾>

- 依 assets/GPT_第十一批_工作清單.csv 的「原尺寸」把交件縮成遊戲像素（任何解析度都可以，長寬比要對）：
  先用平均（BOX）縮小，再限色（地面 8 色、其他 24 色），透明度以一半為界。
- 地面方格（b11_g_*）不透明；縮小後再把左右、上下相對的邊各混一半，四邊可以無縫拼接。
- Wang 過渡圖（b11_w_*）不用交件：由兩種地面方格依 wang16.png 的四角位元（左上 1、右上 2、左下 4、右下 8，
  位元為 1 的角是第二種地）自動合成 4×4 共 16 格；分界線在格子中間有一點起伏，格子邊上照四角內插（相鄰格一定接得上）。
- 輸出到 godot/assets/px2/，並印出缺件與尺寸不合的檔名。
"""
import csv
import math
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
LIST = ROOT / "assets" / "GPT_第十一批_工作清單.csv"
OUT = ROOT / "godot" / "assets" / "px2"
T = 16

# Wang：檔名 → （第一種地、第二種地）；第二種為 None 表示透明。
WANG = {
    "b11_w_grass_sand": ("b11_g_grass_temperate_1", "b11_g_sand_1"),
    "b11_w_sand_shallow": ("b11_g_sand_1", "b11_g_water_shallow_1"),
    "b11_w_shallow_deep": ("b11_g_water_shallow_1", "b11_g_water_deep_1"),
    "b11_w_grass_dirt": ("b11_g_grass_temperate_1", "b11_g_plaza_dirt_1"),
    "b11_w_grass_stone": ("b11_g_grass_temperate_1", "b11_g_road_stone_1"),
    "b11_w_grass_brick": ("b11_g_grass_temperate_1", "b11_g_road_brick_1"),
    "b11_w_grass_field": ("b11_g_grass_temperate_1", "b11_g_field_wheat"),
    "b11_w_grass_forest": ("b11_g_grass_temperate_1", "b11_g_forest_floor_1"),
    "b11_w_grass_hill": ("b11_g_grass_temperate_1", "b11_g_hill_1"),
    "b11_w_hedge": ("hedge", None),
}


def rows():
    with LIST.open(encoding="utf-8") as f:
        return list(csv.DictReader(f))


def pixelize(src: Path, w: int, h: int, ground: bool) -> Image.Image:
    im = Image.open(src).convert("RGBA")
    if abs(im.width / im.height - w / h) > 0.08 * (w / h):
        raise ValueError(f"長寬比 {im.width}×{im.height} 與 {w}×{h} 不合")
    rgb = im.convert("RGB").resize((w, h), Image.BOX)
    q = rgb.quantize(8 if ground else 24, method=Image.Quantize.MEDIANCUT).convert("RGBA")
    if ground:
        return seamless(q)
    alpha = im.split()[3].resize((w, h), Image.BOX).point(lambda v: 255 if v > 127 else 0)
    q.putalpha(alpha)
    return q


def seamless(tile: Image.Image) -> Image.Image:
    """最右一欄改成最左一欄的顏色、最下一列改成最上一列的顏色：拼起來相鄰兩格的邊一樣，不會有硬邊。"""
    px = tile.load()
    w, h = tile.size
    for y in range(h):
        px[w - 1, y] = px[0, y]
    for x in range(w):
        px[x, h - 1] = px[x, 0]
    return tile


def hedge_tile() -> Image.Image:
    """樹籬材質：由灌木素材中央取一塊（交件的 b11_n_bush_1）。"""
    bush = Image.open(OUT / "b11_n_bush_1.png").convert("RGBA").resize((T * 2, T * 2), Image.NEAREST)
    tile = bush.crop((T // 2, T // 2, T // 2 + T, T // 2 + T)).convert("RGB")
    return seamless(tile.convert("RGBA"))


def wang(first: Image.Image, second: Image.Image | None) -> Image.Image:
    out = Image.new("RGBA", (T * 4, T * 4), (0, 0, 0, 0))
    a = first.load()
    b = second.load() if second is not None else None
    o = out.load()
    for idx in range(16):
        gx, gy = idx % 4 * T, idx // 4 * T
        nw, ne, sw, se = (idx >> 0) & 1, (idx >> 1) & 1, (idx >> 2) & 1, (idx >> 3) & 1
        for y in range(T):
            for x in range(T):
                fx, fy = (x + 0.5) / T, (y + 0.5) / T
                v = nw * (1 - fx) * (1 - fy) + ne * fx * (1 - fy) + sw * (1 - fx) * fy + se * fx * fy
                # 邊上照內插（相鄰格接得上）；中間加一點固定的起伏，分界不是直線。
                wobble = 0.14 * math.sin(math.pi * fx) * math.sin(math.pi * fy) * math.sin(5.0 * fx + 3.0 * fy + 1.7 * idx)
                if v + wobble >= 0.5:
                    o[gx + x, gy + y] = b[x, y] if b is not None else (0, 0, 0, 0)
                else:
                    o[gx + x, gy + y] = a[x, y]
    return out


def import_delivery(folder: Path) -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    missing, bad, done = [], [], 0
    for r in rows():
        name = r["檔名"]
        if name.startswith("b11_w_"):
            continue
        src = folder / name
        if not src.exists():
            missing.append(name)
            continue
        try:
            img = pixelize(src, int(r["原尺寸寬"]), int(r["原尺寸高"]), name.startswith("b11_g_"))
        except ValueError as e:
            bad.append(f"{name}：{e}")
            continue
        img.save(OUT / name)
        done += 1
    for name, (first, second) in WANG.items():
        try:
            a = hedge_tile() if first == "hedge" else Image.open(OUT / f"{first}.png").convert("RGBA")
            b = Image.open(OUT / f"{second}.png").convert("RGBA") if second else None
        except FileNotFoundError as e:
            bad.append(f"{name}：缺 {e.filename}")
            continue
        wang(a, b).save(OUT / f"{name}.png")
        done += 1
    print(f"匯入 {done} 張；缺 {len(missing)}；不合 {len(bad)}")
    for m in missing:
        print("  缺：", m)
    for b in bad:
        print("  不合：", b)
    return 0 if not bad else 1


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--import":
        sys.exit(import_delivery(Path(sys.argv[2])))
    print(__doc__)
