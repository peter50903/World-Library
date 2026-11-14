#!/usr/bin/env python3
"""第 08-20a 章：第七批素材（2D 等角插畫風原型：河邊農村、木柵城鎮、河流與樹林）。

  python3 tools/batch7_assets.py          # 重寫 assets/GPT_第七批_等角插畫.md，並把清單同步進 assets/manifest.csv

使用者 2026-11-14 選定的新地圖美術方向（附圖：等角視角、霧面低多邊形小模型、柔和日光、看得出厚度的地塊），
3D 程式拼模型太粗，改用「偽 3D」：建築、樹、岩石等由 GPT 照附圖畫成等角插畫精靈圖，地形與厚度由程式畫。
"""
from __future__ import annotations

import csv
import io
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOC = ROOT / "assets" / "GPT_第七批_等角插畫.md"
MANIFEST = ROOT / "assets" / "manifest.csv"
CHAPTER = "08-20a"

STYLE = """你要為一款「上帝視角世界模擬」遊戲畫一批**等角插畫風**素材。**請先看我附上的示意圖**：所有素材都要是那張圖的畫風——像擺在地圖上的小模型、霧面低多邊形、柔和日光、溫暖自然的色彩、有材質（木紋、石塊、瓦片、茅草）與結構細節（樑柱、門窗、煙囪、欄杆）。請逐張生成下表每一個檔案，檔名照表、一張圖一個檔案；全部完成後打包成 zip。

**共同規格（每一張都要遵守）**
- **視角**：標準 2:1 等角（地面的正方形格在畫面上是寬:高＝2:1 的菱形，約 26.6°）。鏡頭從東南方往西北看：看得到物件的頂面、左前面與右前面。所有素材角度完全一致。
- **光源**：柔和日光從畫面**左上方**照來；左前面較亮、右前面較暗。物件本身有明暗，但**不要畫落在地上的陰影**（陰影由程式畫）。
- **比例**：遊戲裡一格地面在最大放大時是 256×128 像素的菱形。一棟普通房子約佔四分之一格寬（底座約 80 像素寬）。請照表中「成品尺寸」的比例畫，物件的底部中心對齊畫布底部中心。
- **解析度**：請以 1024×1024（或表中比例的放大）繪製，匯入時會縮到成品尺寸；細節要能在成品尺寸下看清楚。
- **背景完全透明**；單一物件、四周留一點空白；不要地面底座（「方田」「木橋」這類本身就是地面的除外）。
- **地形紋理**（類型＝等角紋理）：**正上方俯視**的方形材質、整張填滿不透明、**四邊可以無縫拼接**；程式會把它斜貼到菱形地面上。不要畫物件、不要光影方向。「土層側面」是橫向可無縫拼接的長條。
- **動畫**（類型含「動畫」）：同一張圖橫向排 4 格、每格同大小、物件位置一致，只有動作不同。
- 畫面中不得出現任何文字、浮水印，或既有作品的角色與標誌。"""

A: list[tuple[str, str, str, str, str, str]] = []


def add(group, name, kind, spec, what, use):
    A.append((group, name, kind, spec, what, use))


S = "等角精靈"
M = "等角精靈（動畫）"
X = "等角紋理"

G = "A 房屋"
for name, size, what in [
    ("iso_house_timber", "96×96", "木構架小屋：白灰牆、深色木樑柱、紅陶瓦斜頂、石頭地基、木門與兩扇窗、石砌煙囪"),
    ("iso_house_timber_2", "104×120", "兩層木構架房：一樓石牆、二樓木構架、紅陶瓦斜頂、窗戶較多、煙囪"),
    ("iso_house_stone", "96×100", "石屋：灰色石塊牆、深紅瓦頂、木窗框、小煙囪"),
    ("iso_house_thatch", "92×84", "農村茅草屋：米黃土牆、厚茅草屋頂、木門、旁邊一小堆柴"),
    ("iso_house_thatch_2", "96×88", "農村茅草屋變化：屋頂較長、門前有小木棚"),
    ("iso_town_hall", "160×150", "鎮公所：兩層木構架大屋、石砌一樓、紅瓦屋頂中央有小鐘樓、門前兩根旗桿"),
]:
    add(G, name, S, f"{size} PNG（透明背景）", what, "聚落近景的建築（左右翻轉可得另一個方向）")

G = "B 城鎮防禦"
add(G, "iso_palisade", S, "128×110 PNG（透明背景）",
    "木柵一段：削尖的原木樁一根根排成一直線，從畫面左下往右上延伸（沿菱形格的邊），內側有橫樑；長度約半格", "木柵（左右翻轉＝另一個方向的邊）")
add(G, "iso_palisade_corner", S, "96×120 PNG（透明背景）", "木柵轉角：兩段木柵在前方相交成直角，轉角一根較粗的木樁", "木柵四角")
add(G, "iso_watchtower", S, "72×150 PNG（透明背景）", "木造角樓：四根柱子、板牆、上方平台與欄杆、四角錐紅瓦尖頂、頂上一面小旗", "木柵四角與城門兩側")
add(G, "iso_gatehouse", S, "180×170 PNG（透明背景）", "木柵城門樓：兩座木塔夾著一道有屋頂的橋廊，下方大木門敞開；城門朝向畫面右下", "城鎮南面城門")

G = "C 市集與廣場"
add(G, "iso_stall_red", S, "96×88 PNG（透明背景）", "市集攤位：四根木柱、木檯面、紅白條紋篷布斜頂，檯上擺蔬果、木箱、陶罐", "城鎮十字路口的市集")
add(G, "iso_stall_blue", S, "96×88 PNG（透明背景）", "市集攤位變化：藍白條紋篷布，檯上擺布匹與麵包", "城鎮十字路口的市集")
add(G, "iso_well", S, "56×64 PNG（透明背景）", "石井：圓形石砌井口、兩根木柱、小瓦頂、轆轤與木桶", "廣場中央、農村")
add(G, "iso_crates", S, "48×44 PNG（透明背景）", "一堆木箱與兩個木桶", "市集、碼頭、磨坊旁的雜物")

G = "D 農村"
add(G, "iso_mill", S, "170×150 PNG（透明背景）", "河邊磨坊：一樓石砌、二樓木構架、紅瓦屋頂；建築右側牆面有水車的輪軸（水車另外畫）", "農村河岸")
add(G, "iso_waterwheel", M, "單格 72×72，橫排 4 格 PNG（透明背景）", "木製水車輪側面（輪圈、輪輻、葉片），4 格依序轉動一小格，葉片下方帶一點白色水花", "磨坊旁的水車（會轉）")
add(G, "iso_field_wheat", S, "256×140 PNG（透明背景）", "一整格方田：菱形的金黃麥田，田壟一條條清楚、四邊是木頭圍籬；田本身就是地面（不透明的菱形）", "農村的方田（每格一張）")
add(G, "iso_field_young", S, "256×140 PNG（透明背景）", "同上，但是剛播種的綠色幼苗田", "開墾中的田")
add(G, "iso_haystack", S, "48×44 PNG（透明背景）", "圓錐形乾草堆", "田邊")
add(G, "iso_bridge", S, "256×140 PNG（透明背景）", "跨河木橋：從畫面左下往右上跨過（沿菱形格的方向），木板橋面、兩側欄杆、兩根橋墩；不要畫河水", "河上的橋（左右翻轉＝另一個方向）")

G = "E 樹木與岩石"
for name, size, what in [
    ("iso_tree_pine", "64×110", "針葉樹：三層低多邊形深綠樹冠、短樹幹"),
    ("iso_tree_pine_2", "56×96", "較小的針葉樹，顏色略淺"),
    ("iso_tree_oak", "80×100", "闊葉樹：圓潤的低多邊形淺綠樹冠、短樹幹"),
    ("iso_tree_oak_2", "72×88", "闊葉樹變化：樹冠分成兩三團"),
    ("iso_tree_grove", "128×130", "一小片樹林：四五棵針葉與闊葉樹擠在一起"),
    ("iso_bush", "48×36", "低矮灌木叢"),
    ("iso_rock", "64×48", "一塊有稜角的灰色岩石"),
    ("iso_rocks", "96×64", "一群大小不一的岩石，縫隙長了一點草"),
]:
    add(G, name, S, f"{size} PNG（透明背景）", what, "森林、草地、丘陵、海岸")

G = "F 地形紋理"
for name, size, what in [
    ("iso_tex_grass", "512×512", "草地：柔和的綠色，帶細微的深淺斑塊與小草"),
    ("iso_tex_forest_floor", "512×512", "森林地面：深綠帶落葉與苔蘚"),
    ("iso_tex_sand", "512×512", "沙灘：淡米黃沙地，帶細小的顆粒與波紋"),
    ("iso_tex_hill", "512×512", "丘陵：灰綠色岩地夾雜碎石與短草"),
    ("iso_tex_water", "512×512", "水面：清澈的藍綠色，柔和的波紋與亮光"),
    ("iso_tex_road", "512×512", "石子路：淺灰米色的鋪石路面"),
]:
    add(G, name, X, f"{size} PNG（不透明、四邊可無縫拼接）", what, "程式斜貼到菱形地面")
add(G, "iso_tex_cliff", X, "512×128 PNG（不透明、左右可無縫拼接）",
    "土層側面（地塊的切面）：最上面一條薄薄的草皮、往下是褐色表土、再往下是深褐土層夾著石塊", "地塊邊緣與河岸、海岸的側面（顯示地面厚度）")


def write_doc():
    groups: dict[str, list] = {}
    for g, *rest in A:
        groups.setdefault(g, []).append(rest)
    lines = [
        "# 第七批素材：等角插畫風（2D 等角原型）",
        "",
        "> 使用方式：把下面「給 GPT 的指示」與所有表格整份貼給 GPT，**並附上使用者 2026-11-14 選定的地圖示意圖**，請它逐張生成，檔名照表；全部完成後打包成 zip。",
        "> 拿到後把檔案放進 `D:\\DESK\\godot\\世界圖書館_素材\\世界圖書館_素材收件匣`，告訴 Claude 同步即可（`tools/art_inbox.py` 依檔名匯入）。",
        f"> 本檔由 `tools/batch7_assets.py` 產生（共 {len(A)} 項）；修改請改那支程式再重新產生。",
        "",
        "## 給 GPT 的指示（請整段照貼）",
        "",
        STYLE,
        "",
    ]
    n = 0
    for g, items in groups.items():
        lines += [f"## {g}", "", "| # | 檔名 | 成品尺寸 | 畫什麼 | 用在哪裡 |", "|---|---|---|---|---|"]
        for name, kind, spec, what, use in items:
            n += 1
            lines.append(f"| {n} | `{name}.png` | {spec} | {what} | {use} |")
        lines.append("")
    DOC.write_text("\n".join(lines), encoding="utf-8")


def sync_manifest():
    text = MANIFEST.read_text(encoding="utf-8")
    rows = list(csv.DictReader(io.StringIO(text)))
    fields = list(rows[0].keys())
    keep = [r for r in rows if not r["ID"].startswith("B7_")]
    old = {r["ID"]: r for r in rows if r["ID"].startswith("B7_")}
    for group, name, kind, spec, what, use in A:
        rid = "B7_" + name.upper()
        prompt = f"{what}。規格 {spec}。（共同風格與視角見 assets/GPT_第七批_等角插畫.md，並附上地圖示意圖）"
        prev = old.get(rid, {})
        keep.append({
            "ID": rid, "檔名": f"iso/{name}.png", "類型": kind, "規格": spec, "用途": use, "章節": CHAPTER,
            "生成方式": "GPT手動", "狀態": prev.get("狀態", "待製作"), "已使用": prev.get("已使用", ""), "提示詞": prompt,
        })
    buf = io.StringIO()
    writer = csv.DictWriter(buf, fieldnames=fields, lineterminator="\n")
    writer.writeheader()
    writer.writerows(keep)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")


if __name__ == "__main__":
    write_doc()
    sync_manifest()
    print(f"第七批素材 {len(A)} 項：已寫入 {DOC.relative_to(ROOT)} 與 manifest")
