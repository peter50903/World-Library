#!/usr/bin/env python3
"""第 08-20c 章：把 KayKit 低多邊形模型（godot/assets/kaykit/*.gltf，CC0）烘焙成正式 2D 地圖用的像素建築（godot/assets/px2/kk_*.png）。

- 視角：與正式地圖的建築相同的「俯視略帶正面」——鏡頭在南方上空往北看，俯角 TEMP_PITCH 度；光從左上。
- 方向：住屋與公共建築各烘四張（_s 門朝南＝正面、_e 門朝東、_n 門朝北＝屋後、_w 門朝西）；風車烘 4 幀扇葉轉動。
- 像素化：先以 4 倍解析度算圖，再縮成成品尺寸（與地表同一密度：每格半徑 48 圖像素），透明度二值化、顏色量化、外圍 1 像素深色描邊。
- 文化色：模型的藍色屋頂改成灰階，地圖上照第 08-18d 章的方式染成文化色；原本就是灰的石頭稍微加暖，避免被誤染。
- 只用 numpy 與 Pillow（與 tools/art_inbox.py 相同）。用法：python3 tools/kaykit_bake.py
"""
from __future__ import annotations

import csv
import io
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
KIT = ROOT / "godot" / "assets" / "kaykit"
OUT = ROOT / "godot" / "assets" / "px2"
MANIFEST = ROOT / "assets" / "manifest.csv"

TEMP_PITCH = 40.0  # 【TEMP】俯角（度）
SUPER = 4  # 超取樣倍率

# 成品名 → (模型, 成品寬（圖像素）, 是否烘四個方向, 屋頂染色比例)
SPRITES: dict[str, tuple[str, int, bool, float]] = {
    "kk_hut": ("building_home_A_blue", 40, True, 0.6),
    "kk_house": ("building_home_B_blue", 46, True, 0.6),
    "kk_stone_house": ("building_tavern_blue", 52, True, 0.55),
    "kk_hall_village": ("building_tavern_blue", 64, True, 0.55),
    "kk_hall_town": ("building_barracks_blue", 72, True, 0.5),
    "kk_market": ("building_market_blue", 68, True, 0.6),
    "kk_temple": ("building_church_blue", 64, True, 0.6),
    "kk_keep": ("building_castle_blue", 84, False, 0.5),
    "kk_well": ("building_well_blue", 24, False, 0.6),
    "kk_smithy": ("building_blacksmith_blue", 52, True, 0.55),
    "kk_workshop": ("building_lumbermill_blue", 52, True, 0.55),
    "kk_watchtower": ("building_tower_A_blue", 30, False, 0.4),
    "kk_wall_tower": ("building_tower_base_blue", 32, False, 0.3),
    "kk_wall_gate": ("wall_straight_gate", 48, False, 0.0),
}
WINDMILL = ("kk_windmill", "building_windmill_blue", 52)
DIRS = {"s": 0.0, "e": 90.0, "n": 180.0, "w": -90.0}


# ───────────────────────── glTF ─────────────────────────

def load(name: str, spin: dict[str, float] | None = None):
    """讀模型：回傳 (三角形頂點 N×3×3, 每個三角形的貼圖顏色 N×3)。spin：名字含此字的零件繞本地軸轉幾度（風車扇葉＝Z 軸）。"""
    path = KIT / f"{name}.gltf"
    d = json.loads(path.read_text())
    buf = (KIT / d["buffers"][0]["uri"]).read_bytes()
    atlas = np.asarray(Image.open(KIT / "hexagons_medieval.png").convert("RGB"), dtype=np.float32) / 255.0

    def acc(i):
        a = d["accessors"][i]
        bv = d["bufferViews"][a["bufferView"]]
        n = {"VEC3": 3, "VEC2": 2, "SCALAR": 1}[a["type"]]
        dt = {5126: np.float32, 5123: np.uint16, 5125: np.uint32}[a["componentType"]]
        off = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        arr = np.frombuffer(buf, dt, a["count"] * n, off)
        return arr.reshape(a["count"], n) if n > 1 else arr

    def local(node):
        m = np.eye(4)
        if "matrix" in node:
            return np.array(node["matrix"]).reshape(4, 4).T
        x, y, z, w = node.get("rotation", [0, 0, 0, 1])
        r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                      [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                      [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
        m[:3, :3] = r * np.array(node.get("scale", [1, 1, 1]))
        m[:3, 3] = node.get("translation", [0, 0, 0])
        for key, deg in (spin or {}).items():
            if key in node.get("name", ""):
                a = math.radians(deg)
                rz = np.eye(4)
                rz[:2, :2] = [[math.cos(a), -math.sin(a)], [math.sin(a), math.cos(a)]]
                m = m @ rz
        return m

    tris, cols = [], []

    def walk(i, parent):
        node = d["nodes"][i]
        m = parent @ local(node)
        if "mesh" in node:
            for pr in d["meshes"][node["mesh"]]["primitives"]:
                p = acc(pr["attributes"]["POSITION"])
                uv = acc(pr["attributes"]["TEXCOORD_0"])
                ix = acc(pr["indices"]).reshape(-1, 3)
                p = (np.c_[p, np.ones(len(p))] @ m.T)[:, :3]
                tris.append(p[ix])
                px = (uv[ix].mean(1) * 1023).astype(int).clip(0, 1023)
                cols.append(atlas[px[:, 1], px[:, 0]])
        for c in node.get("children", []):
            walk(c, m)

    for i in d["scenes"][0]["nodes"]:
        walk(i, np.eye(4))
    return np.vstack(tris), np.vstack(cols)


# ───────────────────────── 算圖 ─────────────────────────

def render(tris, cols, yaw: float, px_per_unit: float):
    """正交投影算出 RGBA（浮點）。"""
    a = math.radians(yaw)
    ry = np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]])
    t = tris @ ry.T
    p = math.radians(TEMP_PITCH)
    fwd = -np.array([0, math.sin(p), math.cos(p)])
    right = np.array([1.0, 0, 0])
    up = np.cross(right, fwd)
    light = np.array([-0.55, 0.75, 0.35])
    light /= np.linalg.norm(light)
    n = np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-9
    n[(n @ -fwd) < 0] *= -1
    shade = 0.62 + 0.5 * np.clip(n @ light, 0, 1)
    col = np.clip(cols * shade[:, None], 0, 1)
    sx = t @ right * px_per_unit
    sy = -(t @ up) * px_per_unit
    dz = t @ -fwd
    x0, x1, y0, y1 = sx.min() - 2, sx.max() + 2, sy.min() - 2, sy.max() + 2
    w, h = int(math.ceil(x1 - x0)), int(math.ceil(y1 - y0))
    sx -= x0
    sy -= y0
    img = np.zeros((h, w, 4), np.float32)
    zb = np.full((h, w), -1e9, np.float32)
    for k in range(len(t)):
        x, y, z = sx[k], sy[k], dz[k]
        xa, xb = max(int(x.min()), 0), min(int(math.ceil(x.max())), w - 1)
        ya, yb = max(int(y.min()), 0), min(int(math.ceil(y.max())), h - 1)
        if xa > xb or ya > yb:
            continue
        gx, gy = np.meshgrid(np.arange(xa, xb + 1) + 0.5, np.arange(ya, yb + 1) + 0.5)
        den = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(den) < 1e-9:
            continue
        w0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / den
        w1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / den
        w2 = 1 - w0 - w1
        m = (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
        zz = w0 * z[0] + w1 * z[1] + w2 * z[2]
        sub = zb[ya:yb + 1, xa:xb + 1]
        mm = m & (zz > sub)
        sub[mm] = zz[mm]
        img[ya:yb + 1, xa:xb + 1][mm] = [*col[k], 1.0]
    return img


def pixelate(big: np.ndarray, width: int, roof: float) -> Image.Image:
    """縮成成品尺寸：平均縮小、透明度二值化、量化、屋頂藍改灰階、描邊。"""
    ys, xs = np.nonzero(big[..., 3] > 0)
    big = big[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h0, w0 = big.shape[:2]
    height = max(1, round(h0 * width / w0))
    pad = 1
    rgba = Image.fromarray((big * 255).astype(np.uint8), "RGBA")
    # 預乘透明度再平均，邊緣不會混到黑色。
    pre = big.copy()
    pre[..., :3] *= pre[..., 3:4]
    small = np.asarray(Image.fromarray((pre * 255).astype(np.uint8), "RGBA").resize((width, height), Image.BOX), dtype=np.float32) / 255
    alpha = small[..., 3]
    color = np.where(alpha[..., None] > 1e-3, small[..., :3] / np.maximum(alpha[..., None], 1e-3), 0)
    solid = alpha > 0.45
    # 原本就是灰的（石頭、金屬）稍微加暖，避免被當成要染色的灰階。
    mx, mn = color.max(-1), color.min(-1)
    gray = ((mx - mn) < 0.12) & ~((color[..., 2] > color[..., 0] + 0.12) & (color[..., 2] > color[..., 1] + 0.04))
    lum0 = color[gray].mean(-1, keepdims=True)
    color[gray] = np.clip(lum0 + [0.05, 0.01, -0.05], 0, 1)
    # 模型的藍色（屋頂、旗幟）改成灰階，地圖上整張染文化色（只有灰階會被染）。
    if roof > 0:
        r, g, b = color[..., 0], color[..., 1], color[..., 2]
        blue = (b > r + 0.12) & (b > g + 0.04)
        sel = blue & solid
        lum = (0.3 * r + 0.59 * g + 0.11 * b)[sel]
        v = np.clip(0.12 + lum * 1.05, 0.24, 0.8)
        color[sel] = np.stack([v, v, v], -1)
    # 顏色量化到 1/32 階（像素風的平塗）。
    color = np.round(color * 31) / 31
    out = np.zeros((height + 2 * pad, width + 2 * pad, 4), np.uint8)
    out[pad:pad + height, pad:pad + width, :3] = (color * 255).astype(np.uint8)
    out[pad:pad + height, pad:pad + width, 3] = np.where(solid, 255, 0)
    # 外圍描邊（與第五批素材相同的深色）。
    a = out[..., 3] > 0
    ring = np.zeros_like(a)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        ring |= np.roll(np.roll(a, dy, 0), dx, 1)
    ring &= ~a
    out[ring] = [31, 23, 18, 255]
    return Image.fromarray(out, "RGBA")


def bake(model: str, width: int, yaw: float, roof: float, spin=None) -> Image.Image:
    tris, cols = load(model, spin)
    # 先以單位寬算一次取得投影寬度，再決定每單位幾像素。
    a = math.radians(yaw)
    xs = tris[..., 0] * math.cos(a) + tris[..., 2] * math.sin(a)
    span = xs.max() - xs.min()
    ppu = width * SUPER / span
    return pixelate(render(tris, cols, yaw, ppu), width, roof)


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    made: list[tuple[str, str, int]] = []
    for name, (model, width, four, roof) in SPRITES.items():
        for d, yaw in (DIRS.items() if four else [("", 0.0)]):
            out = f"{name}_{d}" if d else name
            img = bake(model, width, yaw, roof)
            img.save(OUT / f"{out}.png")
            made.append((out, model, width))
    # 風車：4 幀，扇葉每幀轉 22.5 度（四片扇葉，轉 90 度一圈循環）。
    out, model, width = WINDMILL
    frames = [bake(model, width, 0.0, 0.5, {"_fan": k * 22.5}) for k in range(4)]
    fw = max(f.width for f in frames)
    fh = max(f.height for f in frames)
    strip = Image.new("RGBA", (fw * 4, fh), (0, 0, 0, 0))
    for k, f in enumerate(frames):
        strip.alpha_composite(f, (k * fw + (fw - f.width) // 2, fh - f.height))
    strip.save(OUT / f"{out}.png")
    made.append((out, model, width))
    # 牆面：直牆正面（橫向重複）。
    wall = bake("wall_straight", 64, 0.0, 0.0)
    wall.save(OUT / "kk_wall_front.png")
    made.append(("kk_wall_front", "wall_straight", 64))
    fence = bake("fence_wood_straight", 32, 90.0, 0.0)
    fence.save(OUT / "kk_palisade.png")
    made.append(("kk_palisade", "fence_wood_straight", 32))
    side = bake("wall_straight_gate", 20, 90.0, 0.0)
    side.save(OUT / "kk_wall_gate_side.png")
    made.append(("kk_wall_gate_side", "wall_straight_gate", 20))
    update_manifest(made)
    print(f"烘焙 {len(made)} 張到 {OUT.relative_to(ROOT)}")
    return 0


def update_manifest(made: list[tuple[str, str, int]]) -> None:
    text = MANIFEST.read_text(encoding="utf-8")
    rows = list(csv.reader(io.StringIO(text)))
    have = {r[0]: r for r in rows}
    for name, model, _ in made:
        key = name.upper()
        size = Image.open(OUT / f"{name}.png").size
        row = [key, f"px2/{name}.png", "像素零件", f"{size[0]}×{size[1]} PNG（透明背景）", "正式地圖的建築（第 08-20c 章：KayKit 模型烘焙）",
               "08-20c", "程式烘焙", "已套用", "V",
               f"tools/kaykit_bake.py 由 kaykit/{model}.gltf 烘焙（KayKit Medieval Hexagon Pack，CC0，Kay Lousberg）"]
        if key in have:
            have[key][:] = row
        else:
            rows.append(row)
    buf = io.StringIO()
    csv.writer(buf, lineterminator="\n").writerows(rows)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
