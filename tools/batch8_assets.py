#!/usr/bin/env python3
"""第 08-20d 章：第八批素材——正式 2D 地圖的建築全部由 GPT 重畫（像素風），以 KayKit 模型的算圖當造型與角度參考。

  python3 tools/batch8_assets.py   # 重寫 assets/GPT_第八批_像素建築.md、assets/reference/b8/*.png，並把清單同步進 assets/manifest.csv

使用者 2026-11-14：KayKit 烘焙的像素建築方向對，但要重新繪製比較好；交給 GPT 畫，一次多產一點。
每棟建築畫三個方向：門朝南（正面）、門朝東（側面）、門朝北（背面）；門朝西由程式把門朝東左右翻轉。
"""
from __future__ import annotations

import csv
import io
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kaykit_bake as kb  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
DOC = ROOT / "assets" / "GPT_第八批_像素建築.md"
REF = ROOT / "assets" / "reference" / "b8"
PX2 = ROOT / "godot" / "assets" / "px2"
MANIFEST = ROOT / "assets" / "manifest.csv"
CHAPTER = "08-20d"

STYLE = """你要為一款像素風「上帝視角世界模擬」遊戲**重畫全部建築**（風格參考：WorldBox，但像素密度高一倍、細節更多）。
每一列都附一張**參考圖**（`b8ref_…png`，是 3D 小模型的算圖）：請照參考圖的**造型、比例、屋頂形狀、門窗位置與朝向**重畫成手繪像素畫——參考圖只是示意，請畫得比它更精細、更有手工感。
請逐張生成下表每一個檔案，檔名照表、一張圖一個檔案；全部完成後打包成 zip。

**共同規格（每一張都要遵守）**
- **視角**：「俯視略帶正面」——鏡頭在南方上空往北看、俯角約 40 度：看得到屋頂的頂面與建築的南面，看不到另外兩側的牆。所有建築角度完全一致（與參考圖相同）。
- **方向**：同一棟建築有三張——`_s` 門朝南（正面，門在畫面正中下方）、`_e` 門朝東（門在畫面右側牆上、看到的是建築的側面）、`_n` 門朝北（看到的是屋後，門被擋住看不到）。三張是**同一棟房子**轉方向，屋頂、材質、細節要一致。
- **光源**：從左上方照來；左邊與屋頂朝上的面較亮、右邊較暗。不要畫落在地上的陰影（程式畫）。
- **乾淨的像素畫**：方正大像素、不得有柔邊、漸層與反鋸齒；每個物件外圍有 1 像素寬、比本體深的同色系描邊（不要純黑）。
- **大像素數**照表：例如 46×52 代表整棟寬 46、高 52 個大像素。請在 1024×1024 的圖中放大繪製（每個大像素是同樣大小的方塊），匯入時會縮回表中大小。細節要畫到這個像素數看得出來：木紋、一片片瓦片或茅草、石縫、窗框、門把、煙囪的磚。
- **灰階部分**：**屋頂、旗幟、篷布**用中灰色加明暗畫，不要上彩色（程式依各文化染色）。其他地方**不要用純灰**：石頭偏暖灰（帶一點米黃）、金屬偏藍黑也可以。
- 色彩明亮、對比清楚；人造物比自然景物鮮明，放在草地上要一眼跳出來。
- 單一物件、底部置中、四周留白，背景完全透明；不要地面、不要陰影底座。
- **牆面與木柵的「正面」**：橫向可以無縫重複（左右接起來看不出接縫），整條畫滿寬度。
- **動畫**（類型含「動畫」）：同一張圖橫向排 4 格、每格同大小、物件位置一致，只有動作不同。
- 畫面中不得出現任何文字、浮水印，或既有作品的角色與標誌。"""

# (群組, 名稱, KayKit 模型或既有素材, 寬, 方向, 畫什麼, 用在哪裡)
ITEMS: list[tuple[str, str, str, int, str, str, str]] = []


def add(group, name, ref, width, views, what, use):
    ITEMS.append((group, name, ref, width, views, what, use))


G = "A 住屋"
add(G, "hut_1", "building_home_A_blue", 40, "sen", "村莊木屋：原木牆、矮石基、灰階木瓦斜頂、木門、一扇小窗、屋側一小堆柴", "村莊的住屋")
add(G, "hut_2", "building_home_A_blue", 40, "sen", "村莊木屋變化：屋頂有補丁、門邊掛一串乾草、窗台放陶罐", "村莊的住屋")
add(G, "house_1", "building_home_B_blue", 46, "sen", "城鎮兩層房：石砌一樓、二樓白灰牆與深色木骨架、灰階斜頂、磚砌煙囪、門前石階", "城鎮的住屋")
add(G, "house_2", "building_home_A_blue", 46, "sen", "城鎮一層半房：白灰牆木骨架、灰階斜頂上有老虎窗、窗下花箱", "城鎮的住屋")
add(G, "house_3", "building_home_B_blue", 46, "sen", "城鎮兩層房變化：側邊多一個小棚、二樓陽台欄杆", "城鎮的住屋")
add(G, "stone_house_1", "building_home_B_blue", 48, "sen", "城市石屋：整棟暖灰石塊牆、灰階瓦頂、拱形木門、石框窗、煙囪", "城市的住屋")
add(G, "stone_house_2", "building_home_B_blue", 48, "sen", "城市石屋變化：三層窄樓、屋頂有兩個煙囪", "城市的住屋")
add(G, "stone_house_3", "building_home_A_blue", 48, "sen", "城市石屋變化：轉角有小塔樓、門上有雨遮", "城市的住屋")

G = "B 公共建築"
add(G, "hall_village", "building_tavern_blue", 64, "sen", "村會所：大木屋、灰階屋頂、門前木平台與長凳、屋側掛村旗（灰階）", "村莊中心")
add(G, "hall_town", "building_barracks_blue", 72, "sen", "鎮會所：兩層石木建築、中央小鐘樓、灰階屋頂、門前兩根旗桿（旗灰階）", "城鎮中心")
add(G, "market", "building_market_blue", 68, "sen", "市集：幾個條紋篷布攤位（篷布灰階）、木箱、水果與布匹", "城鎮以上的市集")
add(G, "temple", "building_church_blue", 64, "sen", "神殿：石砌教堂、尖塔、彩色玻璃圓窗、灰階屋頂", "城市以上的信仰建築")
add(G, "smithy", "building_blacksmith_blue", 52, "sen", "鐵匠鋪：石砌爐灶冒火光、鐵砧、工具架、灰階屋頂的半開放工棚", "城鎮以上")
add(G, "workshop", "building_lumbermill_blue", 52, "sen", "工坊（木工）：鋸木台、木材堆、灰階屋頂工棚", "城鎮以上")
add(G, "tavern", "building_tavern_blue", 56, "sen", "酒館：兩層木構架、門上掛酒桶招牌、窗戶透暖光、灰階屋頂", "城鎮以上（之後的章節）")
add(G, "barracks", "building_barracks_blue", 64, "sen", "兵營：石牆、木門、武器架、屋頂插旗（灰階）", "城市以上（之後的章節）")
add(G, "watermill", "building_watermill_blue", 56, "sen", "水磨坊：木構架磨坊、側邊大水車（輪子在建築右側）、灰階屋頂", "臨河農村（之後的章節）")
add(G, "mine", "building_mine_blue", 56, "sen", "礦坑入口：岩石山壁上的木框坑道口、礦車軌道、礦車與石料", "礦村（之後的章節）")
add(G, "archery", "building_archeryrange_blue", 60, "sen", "射箭場：木棚、箭靶三個、箭桶", "城市以上（之後的章節）")
add(G, "shrine", "bld_shrine", 40, "sen", "小祠：石台上的小神龕、灰階小屋頂、香爐與花", "村莊以上的信仰（有信仰時）")
add(G, "kiln", "bld_kiln", 44, "sen", "窯：圓頂磚窯冒煙、柴堆、陶罐", "城鎮以上")
add(G, "granary", "bld_granary", 48, "sen", "穀倉：高腳木倉（防鼠）、灰階屋頂、麻袋", "村莊以上")
add(G, "school", "bld_school", 60, "sen", "學校：兩層石木建築、門前小庭院、灰階屋頂、鐘", "城市以上")
add(G, "scriptorium", "bld_scriptorium", 52, "sen", "抄經房：石砌建築、大窗、門前書架、灰階屋頂", "首都")
add(G, "hunting_lodge", "bld_hunting_lodge", 48, "sen", "獵人小屋：原木屋、門上掛鹿角、晾著毛皮", "營地與村莊（獵場）")
add(G, "fishing_hut", "bld_fishing_hut", 48, "sen", "漁夫高腳屋：木樁撐起的小屋、晾漁網、小木梯", "臨水聚落")

G = "C 單一方向"
add(G, "keep", "building_castle_blue", 84, "s", "王宮（主樓）：石砌城堡、數座圓塔、灰階尖頂與旗幟、大門", "首都的主樓")
add(G, "well", "building_well_blue", 24, "s", "水井：石砌井圈、灰階小屋頂、木桶與繩索", "不臨水的聚落廣場邊")
add(G, "watchtower", "building_tower_A_blue", 30, "s", "瞭望塔：石砌圓塔、頂上木平台、灰階尖頂", "城市以上")
add(G, "tent_1", "bld_tent_1", 32, "s", "營地帳篷：獸皮與布（灰階）搭的圓錐帳篷、門簾掀開", "營地")
add(G, "tent_2", "bld_tent_2", 32, "s", "營地帳篷變化：長方形人字帳、旁邊插一根長矛", "營地")
add(G, "dock", "bld_dock", 48, "s", "木碼頭：伸進水裡的木棧道與繫船柱、一艘小船", "臨水聚落")
add(G, "site_1", "building_stage_A", 48, "s", "建地第一階段：打好的地基與木樁圍線、木料堆", "人口快到下一棟房子時")
add(G, "site_2", "building_stage_B", 48, "s", "建地第二階段：立起的木骨架、梯子", "人口快到下一棟房子時")
add(G, "site_3", "building_stage_C", 48, "s", "建地第三階段：牆快完成、屋頂只有骨架、鷹架", "人口快到下一棟房子時")
add(G, "ruin", "building_destroyed", 48, "s", "廢墟：燒黑倒塌的木屋、只剩半面牆", "毀掉的聚落")
add(G, "ruin_big", "building_destroyed", 68, "s", "大廢墟：倒塌的石砌大建築、斷柱與瓦礫", "毀掉的城市")

G = "D 城牆與木柵"
add(G, "wall_front", "wall_straight", 32, "s", "石城牆正面：暖灰石塊、上緣垛口、牆頂走道邊緣；橫向可無縫重複", "城市以上的城牆（上下兩側）")
add(G, "wall_tower", "building_tower_base_blue", 32, "s", "城牆圓塔：比牆高、垛口、灰階錐形頂、旗（灰階）", "城牆四角")
add(G, "wall_gate", "wall_straight_gate", 48, "s", "城門正面：拱形大門、木門敞開、兩側小塔", "城牆上下兩側的城門")
add(G, "wall_gate_side", "wall_straight_gate", 20, "e", "城門側面：從側面看的城門（左右牆上的城門）", "城牆左右兩側的城門")
add(G, "palisade_front", "fence_wood_straight", 32, "e", "木柵正面：一根根削尖的原木樁、中間一條橫樑；橫向可無縫重複", "城鎮的木柵（上下兩側）")
add(G, "palisade_tower", "building_tower_A_yellow", 24, "s", "木柵角樓：木造高腳哨塔、灰階小尖頂", "木柵四角")
add(G, "palisade_gate", "fence_wood_straight_gate", 40, "e", "木柵門正面：兩扇木門敞開、門上橫樑", "木柵上下兩側的門")

G = "E 動畫"
add(G, "windmill", "building_windmill_blue", 52, "anim", "風車：石砌塔身、木扇葉、灰階尖頂；4 格扇葉依序轉動（每格轉 22.5 度）", "城鎮以上")

YAW = {"s": 0.0, "e": 90.0, "n": 180.0}
VIEW_NAME = {"s": "門朝南（正面）", "e": "門朝東（側面）", "n": "門朝北（背面）"}


def reference(ref: str, yaw: float, spin=None) -> Image.Image:
    """參考圖：KayKit 模型的平滑算圖（寬約 360 像素）；既有素材則放大成同樣大小。"""
    if (kb.KIT / f"{ref}.gltf").exists():
        tris, cols = kb.load(ref, spin)
        import math
        a = math.radians(yaw)
        xs = tris[..., 0] * math.cos(a) + tris[..., 2] * math.sin(a)
        img = kb.render(tris, cols, yaw, 360 / (xs.max() - xs.min()))
        ys, xs2 = np.nonzero(img[..., 3] > 0)
        img = img[ys.min():ys.max() + 1, xs2.min():xs2.max() + 1]
        return Image.fromarray((img * 255).astype(np.uint8), "RGBA")
    src = Image.open(PX2 / f"{ref}.png").convert("RGBA")
    k = max(1, 360 // src.width)
    return src.resize((src.width * k, src.height * k), Image.NEAREST)


def files():
    """每一列：(群組, 檔名, 規格, 參考圖, 畫什麼, 用在哪裡, 類型)。"""
    out = []
    REF.mkdir(parents=True, exist_ok=True)
    for group, name, ref, width, views, what, use in ITEMS:
        if views == "anim":
            frames = [reference(ref, 0.0, {"_fan": k * 22.5}) for k in range(4)]
            fw = max(f.width for f in frames)
            fh = max(f.height for f in frames)
            strip = Image.new("RGBA", (fw * 4, fh))
            for k, f in enumerate(frames):
                strip.alpha_composite(f, (k * fw + (fw - f.width) // 2, fh - f.height))
            refname = f"b8ref_{name}.png"
            strip.save(REF / refname)
            height = round(fh * width / fw)
            out.append((group, f"b8_{name}.png", f"{width * 4}×{height} PNG（透明背景，橫排 4 格）", refname, what, use, "像素零件（動畫）"))
            continue
        for v in views:
            img = reference(ref, YAW[v])
            refname = f"b8ref_{name}_{v}.png" if len(views) > 1 else f"b8ref_{name}.png"
            img.save(REF / refname)
            height = round(img.height * width / img.width)
            file = f"b8_{name}_{v}.png" if len(views) > 1 else f"b8_{name}.png"
            desc = f"{what}。{VIEW_NAME[v]}" if len(views) > 1 else what
            out.append((group, file, f"{width}×{height} PNG（透明背景）", refname, desc, use, "像素零件"))
    return out


def write_doc(rows) -> None:
    lines = [
        "# 第八批素材：正式地圖的建築全部重畫（像素風）",
        "",
        "> 使用方式：把「給 GPT 的指示」與下面所有表格整份貼給 GPT，並把 `assets/reference/b8/` 的參考圖一起附上（每列的「參考圖」欄是檔名），",
        "> 請它逐張生成，檔名照表；全部完成後打包成 zip。拿到後放進 `D:\\DESK\\godot\\世界圖書館_素材`，告訴 Claude 同步即可；縮放、去背、對齊由匯入工具處理。",
        f"> 本檔由 `tools/batch8_assets.py` 產生（共 {len(rows)} 張），規格依第 {CHAPTER} 章；修改請改那支程式再重新產生。",
        "> 參考圖太多一次附不完時，可以分組（A～E）分次貼，每次貼「給 GPT 的指示」＋該組表格＋該組參考圖。",
        "",
        "## 給 GPT 的指示（請整段照貼）",
        "",
        STYLE,
        "",
    ]
    group = None
    n = 0
    for g, file, spec, ref, what, use, kind in rows:
        if g != group:
            lines += ["", f"## {g}", "", "| # | 檔名 | 規格 | 參考圖 | 畫什麼 | 用在哪裡 |", "|---|---|---|---|---|---|"]
            group = g
        n += 1
        lines.append(f"| {n} | `{file}` | {spec} | `{ref}` | {what} | {use} |")
    DOC.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_manifest(rows) -> None:
    text = MANIFEST.read_text(encoding="utf-8")
    old = [r for r in csv.reader(io.StringIO(text))]
    header, body = old[0], [r for r in old[1:] if not r[0].startswith("B8_")]
    for g, file, spec, ref, what, use, kind in rows:
        body.append([f"B8_{Path(file).stem[3:].upper()}", f"px2/{file}", kind, spec, use, CHAPTER, "GPT手動", "待製作", "",
                     f"{what}。參考圖 assets/reference/b8/{ref}。規格 {spec}。（共同風格與視角見 assets/GPT_第八批_像素建築.md）"])
    buf = io.StringIO()
    csv.writer(buf, lineterminator="\n").writerows([header] + body)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")


def main() -> int:
    rows = files()
    write_doc(rows)
    write_manifest(rows)
    print(f"第八批：{len(rows)} 張，參考圖在 {REF.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
