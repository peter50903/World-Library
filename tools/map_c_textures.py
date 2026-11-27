#!/usr/bin/env python3
"""第 08-59 章：從使用者選定的近景樣張（docs/art_reference/map_c/sample_near.png）切出地面材質，做成無縫貼圖。

  python3 tools/map_c_textures.py          # 重新產生 godot/assets/map/ 底下的貼圖

做法：
1. 切一塊比貼圖大一圈（overlap）的區域；
2. 右邊多出來的那條淡入左邊、下面多出來的那條淡入上面（保留變異量的混合，接縫不會變糊），貼圖上下左右就能相接；
3. 去掉大範圍的明暗起伏（重複貼時才不會一眼看出同一塊），大範圍的深淺改由著色器加。
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SAMPLE = ROOT / "docs" / "art_reference" / "map_c" / "sample_near.png"
OUT = ROOT / "godot" / "assets" / "map"

# 名稱：(左, 上, 貼圖邊長, 接縫寬, 去掉大範圍明暗的比例)
TILES = {
    "terrain_grass": (884, 504, 512, 64, 0.75),
    "terrain_forest": (544, 0, 384, 64, 0.6),
    "water_detail": (1437, 0, 192, 48, 0.9),
}


def blend(a: np.ndarray, b: np.ndarray, w: np.ndarray, mean: np.ndarray) -> np.ndarray:
    """a、b 依 w 混合，保留變異量（一般的線性混合在接縫中間會變平、變糊）。"""
    mixed = a * (1 - w) + b * w
    scale = np.sqrt((1 - w) ** 2 + w ** 2)
    return (mixed - mean) / scale + mean


def seamless(src: np.ndarray, left: int, top: int, n: int, m: int) -> np.ndarray:
    patch = src[top:top + n + m, left:left + n + m]
    mean = patch.reshape(-1, 3).mean(0)
    w = np.linspace(0, 1, m)[None, :, None]
    wide = patch[:, :n].copy()
    wide[:, :m] = blend(patch[:, n:n + m], patch[:, :m], w, mean)
    h = np.linspace(0, 1, m)[:, None, None]
    tile = wide[:n].copy()
    tile[:m] = blend(wide[n:n + m], wide[:m], h, mean)
    return tile


def flatten(tile: np.ndarray, amount: float) -> np.ndarray:
    """除以大半徑模糊後的亮度（3×3 拼起來模糊，邊緣也是無縫的），去掉大範圍的明暗起伏。"""
    n = tile.shape[0]
    big = Image.fromarray(np.tile(tile, (3, 3, 1)).clip(0, 255).astype(np.uint8))
    blur = np.asarray(big.filter(ImageFilter.GaussianBlur(radius=n / 5)), dtype=np.float64)[n:2 * n, n:2 * n]
    mean = tile.reshape(-1, 3).mean(0)
    ratio = mean / np.maximum(blur, 1.0)
    return tile * (1 + (ratio - 1) * amount)


def wrap_blur(tile: np.ndarray, radius: float) -> np.ndarray:
    n = tile.shape[0]
    big = Image.fromarray(np.tile(tile, (3, 3, 1)).clip(0, 255).astype(np.uint8))
    return np.asarray(big.filter(ImageFilter.GaussianBlur(radius=radius)), dtype=np.float64)[n:2 * n, n:2 * n]


def tame(tile: np.ndarray, radius: float, limit: float) -> np.ndarray:
    """壓平中等大小、特別顯眼的一塊（一叢深色灌木、林間空地、白浪）：重複貼時最先被認出來的就是它們；細紋保留。"""
    mean = tile.reshape(-1, 3).mean(0)
    dev = wrap_blur(tile, radius) - mean
    excess = np.sign(dev) * np.maximum(np.abs(dev) - limit, 0.0)
    return tile - excess


def main() -> None:
    src = np.asarray(Image.open(SAMPLE).convert("RGB"), dtype=np.float64)
    OUT.mkdir(parents=True, exist_ok=True)
    for name, (left, top, n, m, amount) in TILES.items():
        tile = tame(flatten(seamless(src, left, top, n, m), amount), n / 40, 7.0)
        Image.fromarray(tile.clip(0, 255).round().astype(np.uint8)).save(OUT / f"{name}.png", optimize=True)
        print(f"{name}.png {n}x{n} mean={np.round(tile.reshape(-1, 3).mean(0) / 255, 3).tolist()}")


if __name__ == "__main__":
    main()
