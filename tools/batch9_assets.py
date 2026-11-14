#!/usr/bin/env python3
"""第 08-20e 章：第九批素材——正面視角（2D 寶可夢／RimWorld 式）的建築、圍牆、道路與水邊建築。

  python3 tools/batch9_assets.py            # 重寫 assets/GPT_第九批_正面像素素材.md、assets/reference/b9/*.png，並同步 assets/manifest.csv
  python3 tools/batch9_assets.py --import <交件資料夾>   # 匯入 GPT 交件到 godot/assets/px2/

使用者 2026-11-15：第八批透視錯誤、拼接有缺口。改成 RimWorld 或 2D 寶可夢那樣，建築完全正面、互不遮擋、
建築之間要有道路相通、勢力一定要被圍牆包住、只有碼頭、渡口、水車可以放進水裡；橫向的牆做成正面。
每個素材只畫一個方向，尺寸是方格（16 圖像素）的整數倍，素材填滿自己的保留範圍，所以不會互相遮擋。
參考圖是 Claude 用色塊畫的同尺寸草圖（輪廓、屋頂與門面分界、門窗位置），GPT 照草圖畫成精細像素畫。
"""
from __future__ import annotations

import csv
import io
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
DOC = ROOT / "assets" / "GPT_第九批_正面像素素材.md"
REF = ROOT / "assets" / "reference" / "b9"
PX2 = ROOT / "godot" / "assets" / "px2"
MANIFEST = ROOT / "assets" / "manifest.csv"
CHAPTER = "08-20e"
T = 16  # 一個畫面方格的圖像素（08-18b 第 1 節）
UP = 4  # 參考圖放大倍數

STYLE = """你要為一款像素風「上帝視角世界模擬」遊戲畫一批**正面視角**的素材（風格參考：2D 寶可夢的城鎮、RimWorld 的建築；像素密度高、細節多）。
每一列都附一張**參考圖**（`b9ref_…png`，是 Claude 用色塊畫的草圖，已放大 4 倍）：請照草圖的**外框、屋頂與門面的分界、門窗位置**重畫成精細的手繪像素畫，
輪廓不可超出草圖的範圍。另附 `b9ref_總覽.png`：這些素材拼成聚落的樣子，請照這個感覺畫。
請逐張生成下表每一個檔案，檔名照表、一張圖一個檔案；全部完成後打包成 zip。

**共同規格（每一張都要遵守）**
- **視角：完全正面**。像 2D 寶可夢的房子：看得到屋頂（畫面上半）與建築的南面（畫面下半），**左右對稱的正面透視**，
  不要斜角、不要等角（45 度）、不要看到東西兩側的牆。每棟只有這一個方向，門一律在南面。
- **房子的構造（正面立面，像像素風小木屋的正面圖）**：
  1. **屋頂是人字形（⌂）**：從正中的尖端往左右兩邊斜下，只看得到沿兩斜邊的**屋簷**（約 3 像素厚、灰階），屋簷兩端**伸出牆外**約 3 像素；
     不要畫成一整片朝向鏡頭的前坡，也不要畫成長方形的平屋頂。
  2. 屋簷底下是**大面積的三角山牆**（牆的材質，木屋是橫向木板），正中開一扇小窗或通風口；山牆底下一根橫樑。
  3. 橫樑下是**牆面**：木屋兩側有角柱，窗戶有十字窗框，木屋與白灰牆的窗兩旁有木遮板；門在正中。
  4. 牆底一條**比牆略寬的石基**（深藍灰色石塊），門的底部貼著石基。
  5. **煙囪、鐘樓一定是從屋頂伸出來**：下半被屋簷擋住、只露出屋頂以上的部分，不可以擋在山牆前面。屋頂尖端以外的上方兩角是透明的。
- **尺寸精確**：規格欄的寬×高就是成品的像素數（每個像素是一個「大像素」，一個地圖方格＝16×16）。
  建築**左右填滿到圖邊**（最多留 1 像素）、**底邊貼齊圖底**；門畫在門面底部、門底貼齊圖底（草圖的深褐色塊）。
  輸出時請直接給表中的實際尺寸；做不到的話給整數倍放大（例如 ×8，每個大像素是同大小的方塊），匯入時會縮回。
- **光源**從左上方來；不要畫落在地上的陰影（程式畫）。
- **乾淨的像素畫**：方正像素、不得有柔邊、漸層與反鋸齒；物件外圍 1 像素、比本體深的同色系描邊（不要純黑）。
  細節畫到看得出：一片片瓦或茅草、木紋、石縫、磚縫、窗框、門把。
- **灰階部分**：**屋頂、旗幟、篷布、帳篷布**用中灰色加明暗畫，不要上彩色（程式依各文化染色）。
  其他地方**不要用純灰**：石頭偏暖灰（帶一點米黃）、金屬偏藍黑。
- 色彩明亮、對比清楚，放在草地上要一眼跳出來。透明背景；不要地面、不要底座、不要陰影。
- **圍牆**：`_h` 是橫向牆的**正面**（看得到牆面與上緣），左右接起來要**無縫重複**；`_v` 是縱向牆的**俯視牆頂**，上下接起來要無縫重複；
  角樓蓋住橫牆與縱牆的接點，比橫牆高；`_gate_h` 是橫牆上的門（正面、門洞開著），`_gate_v` 是縱牆上的門（俯視、中間留路）。
  同一種牆的各片高度、厚度、顏色、上緣位置要完全一致，拼起來不能有縫。
- **道路**：俯視的地面方塊，**上下左右都要無縫重複**；同一種路的兩款只是石頭或磚的排法不同。
- **動畫**（規格寫「橫排 4 格」）：同一張圖橫向排 4 格、每格同大小、物件位置一致，只有動的部分不同。
- 畫面中不得出現任何文字、浮水印，或既有作品的角色與標誌。"""

# 顏色（草圖用；不是成品色）
C = {
    "roof": (150, 150, 150), "roof_d": (110, 110, 110), "roof_l": (182, 182, 182),
    "plaster": (232, 220, 192), "wood": (176, 122, 69), "log": (150, 98, 52), "stone": (184, 172, 150),
    "stone_d": (142, 132, 114), "brick": (168, 85, 63), "brick_d": (128, 60, 44), "door": (90, 58, 32),
    "win": (127, 166, 201), "thatch": (199, 154, 74), "dark": (46, 36, 28), "metal": (70, 76, 92),
    "goods": (196, 120, 60), "leaf": (90, 130, 60),
}

# (群組, 名稱, 寬格, 高格, 屋頂比例, 草圖種類, 參數, 畫什麼, 用在哪裡)
ITEMS: list[tuple] = []


def add(group, name, w, h, roof, kind, what, use, **kw):
    ITEMS.append((group, name, w, h, roof, kind, kw, what, use))


G = "A 住屋"
add(G, "tent_1", 2, 2, 1.0, "tent", "營地帳篷：獸皮與布（灰階）搭的人字帳、門簾掀開", "營地")
add(G, "tent_2", 2, 2, 1.0, "tent", "營地帳篷變化：圓錐帳、旁邊插一根長矛", "營地", cone=True)
add(G, "hut_1", 3, 3, 0.5, "house", "村莊木屋：原木牆、灰階木瓦斜頂、木門、一扇小窗", "村莊的住屋", mat="log", win=1)
add(G, "hut_2", 3, 3, 0.5, "house", "村莊木屋變化：屋頂有補丁、門邊掛乾草、窗台放陶罐", "村莊的住屋", mat="log", win=1, chimney=True)
add(G, "house_1", 3, 4, 0.45, "house", "城鎮兩層房：石砌一樓、二樓白灰牆與深色木骨架、灰階斜頂、磚煙囪", "城鎮的住屋", mat="plaster", win=2, floors=2, chimney=True)
add(G, "house_2", 3, 4, 0.5, "house", "城鎮一層半房：白灰牆木骨架、山牆上一扇圓窗、窗下花箱", "城鎮的住屋", mat="plaster", win=2)
add(G, "house_3", 3, 4, 0.45, "house", "城鎮兩層房變化：二樓陽台欄杆、門上小雨遮", "城鎮的住屋", mat="plaster", win=2, floors=2)
add(G, "stone_house_1", 3, 4, 0.42, "house", "城市石屋：暖灰石塊牆、灰階瓦頂、拱形木門、石框窗、煙囪", "城市的住屋", mat="stone", win=2, floors=2, chimney=True)
add(G, "stone_house_2", 3, 5, 0.36, "house", "城市石屋變化：三層窄樓、屋頂兩個煙囪", "城市的住屋", mat="stone", win=2, floors=3, chimney=True)
add(G, "stone_house_3", 3, 4, 0.42, "house", "城市石屋變化：門上有雨遮、一樓有店面櫥窗", "城市的住屋", mat="stone", win=2, floors=2)
add(G, "brick_house_1", 3, 4, 0.42, "house", "首都磚屋：紅磚牆、白石窗框、灰階瓦頂、煙囪", "首都的住屋", mat="brick", win=2, floors=2, chimney=True)
add(G, "brick_house_2", 4, 4, 0.4, "house", "首都磚造連棟屋：兩戶並排、兩扇門、灰階瓦頂", "首都的住屋", mat="brick", win=4, floors=2, doors=2)

G = "B 公共建築"
add(G, "hall_village", 4, 4, 0.5, "house", "村會所：大木屋、灰階屋頂、門前木平台、屋側掛村旗（灰階）", "村莊中心", mat="log", win=2, door=10)
add(G, "hall_town", 5, 5, 0.42, "house", "鎮會所：兩層石木建築、屋脊正中坐著一座小鐘樓（從屋頂後方伸出、下半被屋頂擋住）、門前兩根旗桿（旗灰階）", "城鎮中心", mat="stone", win=4, floors=2, door=12, tower=True)
add(G, "market", 5, 3, 0.45, "market", "市集：三個條紋篷布攤位（篷布灰階）、木櫃台上擺水果、布匹與陶罐", "城鎮以上的市集")
add(G, "temple", 5, 6, 0.5, "house", "神殿：石砌教堂、屋脊正中伸出一座尖塔（下半被屋頂擋住）、山牆上彩色玻璃圓窗、灰階屋頂", "城市以上的信仰建築", mat="stone", win=2, door=12, spire=True)
add(G, "smithy", 4, 3, 0.45, "house", "鐵匠鋪：石砌爐灶冒火光、鐵砧、工具架、灰階屋頂的半開放工棚", "城鎮以上", mat="stone", win=0, door=14, chimney=True)
add(G, "workshop", 4, 3, 0.45, "house", "木工坊：鋸木台、木材堆、灰階屋頂工棚", "城鎮以上", mat="wood", win=1, door=12)
add(G, "tavern", 4, 4, 0.42, "house", "酒館：兩層木構架、門上掛酒桶招牌、窗戶透暖光、灰階屋頂", "城鎮以上（之後的章節）", mat="plaster", win=4, floors=2, chimney=True)
add(G, "barracks", 5, 4, 0.4, "house", "兵營：石牆、木門、門邊武器架、屋頂插旗（灰階）", "城市以上（之後的章節）", mat="stone", win=4, door=12)
add(G, "archery", 4, 3, 0.45, "house", "射箭場：右半木棚、左半三個箭靶與箭桶", "城市以上（之後的章節）", mat="wood", win=0, door=8)
add(G, "mine", 4, 3, 0.0, "mine", "礦坑入口：岩石山丘上的木框坑道口、礦車軌道從門口伸出、礦石堆", "礦村")
add(G, "shrine", 2, 3, 0.45, "house", "小祠：石台上的小神龕、灰階小屋頂、香爐與花", "村莊以上的信仰（有信仰時）", mat="stone", win=0, door=6)
add(G, "kiln", 3, 3, 0.0, "kiln", "窯：圓頂磚窯冒煙、柴堆、陶罐", "城鎮以上")
add(G, "granary", 3, 3, 0.45, "granary", "穀倉：高腳木倉（防鼠）、灰階屋頂、麻袋", "村莊以上")
add(G, "school", 4, 4, 0.42, "house", "學校：兩層石木建築、屋頂小鐘、門前小庭院", "城市以上", mat="plaster", win=4, floors=2, tower=True)
add(G, "scriptorium", 4, 4, 0.42, "house", "抄經房：石砌建築、大拱窗、門邊書架", "首都", mat="stone", win=2, floors=2)
add(G, "hunting_lodge", 3, 3, 0.5, "house", "獵人小屋：原木屋、門上掛鹿角、牆邊晾毛皮", "營地與村莊（獵場）", mat="log", win=1)
add(G, "fishing_hut", 3, 3, 0.45, "granary", "漁夫高腳屋：木樁撐起的小屋、晾漁網、小木梯", "臨水聚落（岸上）")
add(G, "keep", 6, 7, 0.4, "keep", "王宮主樓：石砌城堡、兩側圓塔、灰階尖頂與旗幟、大拱門", "首都的主樓")
add(G, "well", 2, 2, 0.45, "well", "水井：石砌井圈、木架上灰階小屋頂、木桶與繩索", "不臨水的聚落廣場邊")
add(G, "watchtower", 2, 4, 0.3, "tower", "瞭望塔：石砌方塔、頂上木平台、灰階尖頂", "城市以上")
add(G, "windmill", 3, 5, 0.3, "windmill", "風車：石砌塔身、木扇葉、灰階尖頂；4 格扇葉依序轉 22.5 度", "城鎮以上", frames=4)
add(G, "site_1", 3, 3, 0.0, "site", "建地第一階段：地基與木樁圍線、木料堆", "人口快到下一棟房子時", stage=1)
add(G, "site_2", 3, 3, 0.0, "site", "建地第二階段：立起的木骨架、梯子", "人口快到下一棟房子時", stage=2)
add(G, "site_3", 3, 3, 0.0, "site", "建地第三階段：牆快完成、屋頂只有骨架、鷹架", "人口快到下一棟房子時", stage=3)
add(G, "ruin", 3, 3, 0.0, "ruin", "廢墟：燒黑倒塌的木屋、只剩半面牆", "毀掉的聚落")
add(G, "ruin_big", 5, 4, 0.0, "ruin", "大廢墟：倒塌的石砌大建築、斷柱與瓦礫", "毀掉的城市")

G = "C 水邊（只有這些可以放進水裡）"
add(G, "dock", 3, 3, 0.0, "dock", "木碼頭（俯視）：從岸邊往南伸進水裡的木棧道、繫船柱、旁邊一艘小船；最上面一列在岸上", "臨水聚落")
add(G, "ferry", 2, 2, 0.0, "ferry", "渡船（俯視）：木筏、欄杆、一根撐篙；程式在兩岸渡口柱之間畫繩", "渡口（河上）")
add(G, "ferry_post", 1, 2, 0.0, "post", "渡口柱：岸邊的木柱、繞著繩子、旁邊一個小木牌（不寫字）", "渡口（兩岸各一）")
add(G, "watermill", 5, 4, 0.45, "watermill", "水磨坊：左三格是岸上的木構架磨坊（灰階屋頂、門在正面）、右兩格是伸進水裡的大水車；4 格水車依序轉 22.5 度", "臨河農村", frames=4)

G = "D 圍牆（依聚落等級：圍籬→木柵→石牆→磚牆）"
WALL_KINDS = [("fence", "圍籬", "低矮木圍籬：木樁加兩條橫桿"), ("palisade", "木柵", "削尖的原木樁、中間一條橫樑"),
              ("stone", "石牆", "暖灰石塊、上緣垛口"), ("brick", "磚牆", "紅磚、白石壓頂與垛口")]
WALL_SIZES = {  # 橫牆高、角樓寬高、橫牆門寬高（格）
    "fence": (1, (1, 1), (2, 1)), "palisade": (2, (1, 3), (2, 2)), "stone": (2, (2, 3), (3, 3)), "brick": (2, (2, 3), (3, 3)),
}
for key, zh, look in WALL_KINDS:
    hh, (cw, ch), (gw, gh) = WALL_SIZES[key]
    add(G, f"{key}_h", 1, hh, 0.0, "wall_h", f"{zh}橫段正面：{look}；左右無縫重複", f"{zh}的上下兩側", wall=key)
    add(G, f"{key}_v", 1, 1, 0.0, "wall_v", f"{zh}縱段俯視牆頂；上下無縫重複，寬度與橫段的厚度一致", f"{zh}的左右兩側", wall=key)
    add(G, f"{key}_corner", cw, ch, 0.0, "wall_corner", f"{zh}的角樓（正面）：蓋住橫段與縱段的接點，比橫段高" + ("；木樁比圍籬粗" if key == "fence" else ""), f"{zh}四角", wall=key)
    add(G, f"{key}_gate_h", gw, gh, 0.0, "wall_gate_h", f"{zh}橫段上的門（正面）：門洞開著、兩側門柱或小塔", f"{zh}上下兩側的門", wall=key)
    add(G, f"{key}_gate_v", 1, 3, 0.0, "wall_gate_v", f"{zh}縱段上的門（俯視）：上下兩格是門柱或小塔、中間一格留路", f"{zh}左右兩側的門", wall=key)

G = "E 道路（俯視地面方塊）"
for key, zh, look in [("dirt", "泥土路", "壓實的泥土、小石子、車轍"), ("stone", "石頭路", "大小不一的圓石、石縫"), ("brick", "磚地", "紅磚鋪地")]:
    add(G, f"road_{key}_1", 1, 1, 0.0, "road", f"{zh}：{look}；上下左右無縫重複", f"聚落的{zh}", road=key, var=1)
    add(G, f"road_{key}_2", 1, 1, 0.0, "road", f"{zh}變化：排法不同（{'人字形' if key == 'brick' else '石頭大小不同'}）；上下左右無縫重複", f"聚落的{zh}", road=key, var=2)


# ───────────────────────── 草圖 ─────────────────────────

def darker(c, k=0.7):
    return tuple(int(v * k) for v in c[:3])


def box(d, x0, y0, x1, y1, fill, outline=True):
    """含兩端的長方形（x1、y1 包含）。"""
    d.rectangle([x0, y0, x1, y1], fill=fill + (255,), outline=(darker(fill) + (255,)) if outline else None)


def roof(d, x0, y0, x1, y1, inset=2):
    """灰階斜頂（正面看是上窄下寬的梯形，加瓦片橫紋）。"""
    for y in range(y0, y1 + 1):
        k = inset * (y1 - y) // max(1, y1 - y0)
        d.line([x0 + k, y, x1 - k, y], fill=C["roof"] + (255,))
    for y in range(y0 + 3, y1, 3):
        k = inset * (y1 - y) // max(1, y1 - y0)
        d.line([x0 + k + 1, y, x1 - k - 1, y], fill=C["roof_d"] + (255,))
    d.line([x0, y1, x1, y1], fill=C["roof_d"] + (255,))
    d.line([x0 + inset, y0, x1 - inset, y0], fill=C["roof_l"] + (255,))


def gable_roof(d, x0, y0, x1, y1, wall, ov=3, t=3):
    """正面立面的人字頂（像 ⌂）：屋頂只看到沿三角形兩斜邊的屋簷（厚 t、左右各伸出牆外 ov），
    中間是大面積的三角山牆（牆材質、橫向木板紋、一扇小窗）；山牆底下一根橫樑。"""
    mid = (x0 + x1) / 2
    h = y1 - y0
    wx0, wx1 = x0 + ov, x1 - ov
    # 山牆（三角形，底邊＝牆寬）
    gy = y0 + t
    for y in range(int(gy), y1 + 1):
        half = (mid - wx0) * (y - gy) / max(1, y1 - gy)
        d.line([(mid - half, y), (mid + half, y)], fill=C[wall] + (255,))
        if wall in ("wood", "log") and (y - int(gy)) % 3 == 2:
            d.line([(mid - half + 1, y), (mid + half - 1, y)], fill=darker(C[wall], 0.8) + (255,))
    if h >= 12:
        cy = int(y0 + h * 0.62)
        box(d, int(mid) - 3, cy - 3, int(mid) + 2, cy + 2, C["win"])
        d.line([int(mid) - 1, cy - 3, int(mid) - 1, cy + 2], fill=C["door"] + (255,))
    # 屋簷：沿兩斜邊的厚帶，尖端在正中、兩端伸出牆外
    for k in range(t):
        d.line([(x0, y1 + 1 - k), (mid, y0 + t - 1 - k), (x1, y1 + 1 - k)], fill=(C["roof_l"] if k else C["roof_d"]) + (255,), width=1)
    d.line([(x0, y1 + 2), (mid, y0 + t), (x1, y1 + 2)], fill=C["roof_d"] + (255,))
    d.line([(x0, y1 + 1 - t), (mid, y0 - 1), (x1, y1 + 1 - t)], fill=C["roof"] + (255,))
    # 橫樑
    d.rectangle([wx0, y1 + 1, wx1, y1 + 2], fill=darker(C["wood"], 0.75) + (255,))


def roof_y(x, x0, x1, y0, y1):
    """人字頂外緣在 x 處的高度。"""
    mid = (x0 + x1) / 2
    return y0 + (y1 - y0) * abs(x - mid) / (mid - x0)


def material(d, x0, y0, x1, y1, mat):
    col = C[mat]
    box(d, x0, y0, x1, y1, col)
    if mat in ("stone", "brick"):
        step = 4 if mat == "stone" else 3
        for i, y in enumerate(range(y0 + step, y1, step)):
            d.line([x0 + 1, y, x1 - 1, y], fill=darker(col, 0.82) + (255,))
            for x in range(x0 + (i % 2) * step + 2, x1, step * 2):
                d.line([x, y - step + 1, x, y - 1], fill=darker(col, 0.82) + (255,))
    elif mat == "log":
        for y in range(y0 + 3, y1, 3):
            d.line([x0 + 1, y, x1 - 1, y], fill=darker(col, 0.8) + (255,))
    elif mat == "plaster":
        d.line([x0, y0, x0, y1], fill=C["wood"] + (255,))
        d.line([x1, y0, x1, y1], fill=C["wood"] + (255,))


def door_at(d, cx, bottom, w, h):
    box(d, cx - w // 2, bottom - h + 1, cx - w // 2 + w - 1, bottom, C["door"])


def sketch_house(W, H, roof_frac, kw):
    """正面立面（參考：像素風小木屋的正面）：屋簷伸出牆外、大山牆、牆面、帶遮板的窗、石基。"""
    im = Image.new("RGBA", (W, H))
    d = ImageDraw.Draw(im)
    rh = max(6, round(H * roof_frac))
    mat = kw.get("mat", "plaster")
    wall = mat if mat != "log" else "wood"
    ov, fh = 3, 3
    t = 3 if W < 64 else 4
    top = 0
    if kw.get("tower"):
        # 鐘樓：坐在屋脊上、下半被屋頂擋住（先畫，屋頂後畫）
        top = 14
        box(d, W // 2 - 4, 5, W // 2 + 3, top + 8, C["plaster"])
        box(d, W // 2 - 2, 8, W // 2 + 1, 12, C["dark"])
        d.polygon([(W // 2 - 6, 6), (W // 2, 0), (W // 2 + 5, 6)], fill=C["roof"] + (255,), outline=C["roof_d"] + (255,))
    if kw.get("spire"):
        # 尖塔：從屋脊後方伸出（先畫，屋頂後畫）
        sw = W // 6
        top = H // 4
        box(d, W // 2 - sw // 2, H // 8, W // 2 + sw // 2, top + 10, C["stone"])
        box(d, W // 2 - 1, H // 8 + 4, W // 2, H // 8 + 9, C["dark"])
        d.polygon([(W // 2, 0), (W // 2 - sw // 2 - 1, H // 8), (W // 2 + sw // 2 + 1, H // 8)], fill=C["roof"] + (255,), outline=C["roof_d"] + (255,))
    if kw.get("chimney"):
        cx = int(W * 0.7)
        ry = int(roof_y(cx, 0, W - 1, top + t, rh))
        box(d, cx, max(0, ry - 8), cx + 4, ry + 1, C["brick"])
    # 牆
    material(d, ov, rh + 3, W - 1 - ov, H - 1 - fh, mat)
    if mat in ("wood", "log", "plaster"):
        for x in (ov, W - 1 - ov - 1):
            d.rectangle([x, rh + 3, x + 1, H - 1 - fh], fill=darker(C["wood"], 0.75) + (255,))
    gable_roof(d, 0, top, W - 1, rh, wall, ov, t)
    # 窗（木屋與白灰牆的窗有左右遮板）
    floors = kw.get("floors", 1)
    top_wall = rh + 3
    fl = (H - fh - top_wall) / floors
    wins = kw.get("win", 2)
    doors = kw.get("doors", 1)
    dw = kw.get("door", 6)
    dh = min(11, int(fl) - 2)
    shutters = mat in ("wood", "log", "plaster")
    inner0, inner1 = ov + 2, W - 1 - ov - 2
    for f in range(floors):
        y = top_wall + int(f * fl) + 3
        for i in range(wins):
            x = int(inner0 + (i + 0.5) * (inner1 - inner0) / wins) - 2
            if f == floors - 1 and doors == 1 and abs(x + 2 - W // 2) < dw:
                continue
            box(d, x, y, x + 4, y + 5, C["win"])
            d.line([x + 2, y, x + 2, y + 5], fill=C["door"] + (255,))
            if shutters:
                d.rectangle([x - 2, y, x - 1, y + 5], fill=darker(C["wood"], 0.85) + (255,))
                d.rectangle([x + 5, y, x + 6, y + 5], fill=darker(C["wood"], 0.85) + (255,))
    for i in range(doors):
        cx = int((i + 0.5) * W / doors)
        door_at(d, cx, H - 1 - fh, dw, dh)
    # 石基（比牆略寬）；門前一階
    d.rectangle([1, H - fh, W - 2, H - 1], fill=(92, 96, 112, 255))
    d.line([1, H - fh, W - 2, H - fh], fill=(122, 126, 140, 255))
    return im


def sketch(kind, W, H, roof_frac, kw, frame=0):
    if kind == "house":
        return sketch_house(W, H, roof_frac, kw)
    im = Image.new("RGBA", (W, H))
    d = ImageDraw.Draw(im)
    if kind == "tent":
        if kw.get("cone"):
            d.polygon([(W // 2, 0), (1, H - 1), (W - 2, H - 1)], fill=C["roof"] + (255,), outline=C["roof_d"] + (255,))
            d.line([W - 3, 0, W - 3, H - 1], fill=C["wood"] + (255,))
        else:
            d.polygon([(W // 2, 2), (0, H - 1), (W - 1, H - 1)], fill=C["roof"] + (255,), outline=C["roof_d"] + (255,))
        d.polygon([(W // 2, H // 2), (W // 2 - 4, H - 1), (W // 2 + 4, H - 1)], fill=C["dark"] + (255,))
    elif kind == "market":
        sw = W // 3
        for i in range(3):
            x0 = i * sw
            for k, x in enumerate(range(x0, x0 + sw - 1, 3)):
                d.rectangle([x, 0, min(x + 2, x0 + sw - 2), int(H * roof_frac)], fill=(C["roof"] if k % 2 else C["roof_l"]) + (255,))
            box(d, x0 + 1, int(H * roof_frac) + 1, x0 + sw - 2, H - 1, C["wood"])
            box(d, x0 + 3, int(H * roof_frac) + 3, x0 + sw - 4, int(H * roof_frac) + 8, C["goods"])
    elif kind == "mine":
        d.polygon([(0, H - 1), (6, 8), (W // 2, 0), (W - 7, 6), (W - 1, H - 1)], fill=C["stone_d"] + (255,), outline=darker(C["stone_d"]) + (255,))
        box(d, W // 2 - 9, H - 18, W // 2 + 8, H - 1, C["wood"])
        box(d, W // 2 - 6, H - 15, W // 2 + 5, H - 1, C["dark"])
    elif kind == "kiln":
        d.ellipse([2, 6, W - 3, H + 12], fill=C["brick"] + (255,), outline=C["brick_d"] + (255,))
        box(d, W // 2 + 6, 0, W // 2 + 10, 10, C["brick_d"])
        box(d, W // 2 - 5, H - 10, W // 2 + 4, H - 1, C["dark"])
    elif kind == "granary":
        rh = int(H * roof_frac)
        gable_roof(d, 0, 0, W - 1, rh - 1, "wood")
        box(d, 2, rh, W - 3, H - 10, C["wood"])
        for x in (3, W // 2 - 1, W - 6):
            box(d, x, H - 9, x + 2, H - 1, C["log"])
        box(d, W // 2 - 3, H - 18, W // 2 + 2, H - 10, C["door"])
        for y in range(H - 8, H, 3):
            d.line([W // 2 - 3, y, W // 2 + 2, y], fill=C["log"] + (255,))
    elif kind == "keep":
        tw = W // 5
        for x0 in (0, W - tw):
            box(d, x0, 10, x0 + tw - 1, H - 1, C["stone"])
            d.polygon([(x0, 12), (x0 + tw // 2, 0), (x0 + tw - 1, 12)], fill=C["roof"] + (255,), outline=C["roof_d"] + (255,))
        material(d, tw, 22, W - tw - 1, H - 1, "stone")
        for x in range(tw, W - tw, 6):
            box(d, x, 18, x + 3, 22, C["stone"])
        box(d, W // 2 - 8, 6, W // 2 + 7, 24, C["stone"])
        d.polygon([(W // 2 - 9, 8), (W // 2, -4), (W // 2 + 8, 8)], fill=C["roof"] + (255,))
        for x in (tw + 6, W - tw - 11):
            box(d, x, 34, x + 4, 42, C["win"])
        box(d, W // 2 - 8, H - 22, W // 2 + 7, H - 1, C["door"])
        d.pieslice([W // 2 - 8, H - 30, W // 2 + 7, H - 14], 180, 360, fill=C["door"] + (255,))
    elif kind == "well":
        rh = int(H * roof_frac)
        roof(d, 1, 0, W - 2, rh - 1, inset=3)
        for x in (4, W - 6):
            box(d, x, rh, x + 1, H - 9, C["wood"])
        d.ellipse([2, H - 12, W - 3, H - 1], fill=C["stone"] + (255,), outline=C["stone_d"] + (255,))
        d.ellipse([7, H - 10, W - 8, H - 6], fill=C["dark"] + (255,))
    elif kind == "tower":
        rh = int(H * roof_frac)
        d.polygon([(W // 2, 0), (1, rh), (W - 2, rh)], fill=C["roof"] + (255,), outline=C["roof_d"] + (255,))
        box(d, 0, rh, W - 1, rh + 4, C["wood"])
        material(d, 3, rh + 5, W - 4, H - 1, "stone")
        box(d, W // 2 - 1, rh + 12, W // 2, rh + 18, C["dark"])
        door_at(d, W // 2, H - 1, 6, 9)
    elif kind == "windmill":
        body_top = int(H * roof_frac)
        material(d, 8, body_top, W - 9, H - 1, "stone")
        d.polygon([(W // 2, body_top - 14), (6, body_top + 2), (W - 7, body_top + 2)], fill=C["roof"] + (255,), outline=C["roof_d"] + (255,))
        door_at(d, W // 2, H - 1, 8, 10)
        cx, cy, r = W // 2, body_top - 2, W // 2 - 1
        for k in range(4):
            a = math.radians(frame * 22.5 + k * 90)
            ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
            d.line([cx, cy, ex, ey], fill=C["wood"] + (255,), width=3)
        box(d, cx - 1, cy - 1, cx + 1, cy + 1, C["dark"])
    elif kind == "watermill":
        bw = 3 * T
        h = sketch_house(bw, H, roof_frac, {"mat": "wood", "win": 1})
        im.alpha_composite(h, (0, 0))
        cx, cy, r = bw + T - 1, H - T - 4, T + 2
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=C["log"] + (255,), width=3)
        for k in range(4):
            a = math.radians(frame * 22.5 + k * 45)
            d.line([cx - r * math.cos(a), cy - r * math.sin(a), cx + r * math.cos(a), cy + r * math.sin(a)], fill=C["wood"] + (255,), width=2)
        box(d, bw - 1, cy - 2, cx, cy + 1, C["log"])
    elif kind == "site":
        st = kw["stage"]
        box(d, 2, H - 8, W - 3, H - 1, C["stone_d"])
        for x in (2, W // 2 - 1, W - 5):
            box(d, x, H - 8 - (st >= 2) * 24, x + 2, H - 1, C["wood"])
        if st >= 2:
            d.line([2, H - 32, W - 3, H - 32], fill=C["wood"] + (255,), width=2)
            d.line([4, H - 30, W // 2, H - 44], fill=C["wood"] + (255,), width=2)
            d.line([W - 5, H - 30, W // 2, H - 44], fill=C["wood"] + (255,), width=2)
        if st >= 3:
            material(d, 4, H - 26, W - 5, H - 9, "plaster")
        box(d, W - 14, H - 5, W - 3, H - 1, C["log"])
    elif kind == "ruin":
        pts = [(0, H - 1), (0, H // 2), (W // 5, H // 3), (W // 3, H // 2 + 4), (W // 2, H // 4), (W * 2 // 3, H // 2), (W - 1, H // 2 + 6), (W - 1, H - 1)]
        d.polygon(pts, fill=C["stone_d"] + (255,), outline=C["dark"] + (255,))
        for x in range(4, W - 4, 9):
            box(d, x, H - 6, x + 5, H - 1, C["stone"])
        box(d, W // 2 - 4, H - 14, W // 2 + 3, H - 1, C["dark"])
    elif kind == "dock":
        box(d, 4, 0, W - 5, H - 1, C["wood"])
        for y in range(3, H, 4):
            d.line([5, y, W - 6, y], fill=C["log"] + (255,))
        for y in (6, H - 6):
            for x in (2, W - 4):
                box(d, x, y, x + 2, y + 2, C["log"])
        box(d, W - 12, H - 14, W - 6, H - 3, C["door"])
    elif kind == "ferry":
        box(d, 2, 4, W - 3, H - 5, C["wood"])
        for x in range(5, W - 3, 4):
            d.line([x, 5, x, H - 6], fill=C["log"] + (255,))
        d.line([2, 4, W - 3, 4], fill=C["log"] + (255,), width=2)
        d.line([4, H - 8, W - 6, 6], fill=C["door"] + (255,), width=2)
    elif kind == "post":
        box(d, W // 2 - 3, 4, W // 2 + 2, H - 1, C["log"])
        for y in (10, 13, 16):
            d.line([W // 2 - 4, y, W // 2 + 3, y], fill=C["goods"] + (255,))
    elif kind.startswith("wall"):
        wall(d, kind, kw["wall"], W, H)
    elif kind == "road":
        road(d, kw["road"], kw["var"], W, H)
    return im


def wall_colors(key):
    return {"fence": (C["wood"], C["log"]), "palisade": (C["log"], darker(C["log"])), "stone": (C["stone"], C["stone_d"]),
            "brick": (C["brick"], C["brick_d"])}[key]


def wall_face(d, key, x0, y0, x1, y1):
    """橫牆正面（左右以 8 像素為週期，可無縫重複）。"""
    main, dark = wall_colors(key)
    if key == "fence":
        for x in range(x0, x1 + 1, 8):
            box(d, x, y0 + 3, x + 1, y1, main)
        for y in (y0 + 6, y0 + 11):
            d.line([x0, y, x1, y], fill=dark + (255,), width=2)
    elif key == "palisade":
        for x in range(x0, x1 + 1, 4):
            d.polygon([(x, y1), (x, y0 + 3), (x + 1, y0), (x + 3, y0 + 3), (x + 3, y1)], fill=main + (255,), outline=dark + (255,))
        d.line([x0, y1 - 9, x1, y1 - 9], fill=dark + (255,), width=2)
    else:
        for x in range(x0, x1 + 1, 8):
            d.rectangle([x, y0, x + 4, y0 + 4], fill=main + (255,))
        material(d, x0, y0 + 5, x1, y1, "stone" if key == "stone" else "brick")
        if key == "brick":
            d.line([x0, y0 + 5, x1, y0 + 5], fill=C["plaster"] + (255,), width=2)


def wall(d, kind, key, W, H):
    main, dark = wall_colors(key)
    thick = {"fence": 4, "palisade": 6, "stone": 10, "brick": 10}[key]
    if kind == "wall_h":
        wall_face(d, key, 0, 0 if H > T else 0, W - 1, H - 1)
    elif kind == "wall_v":
        x0 = (W - thick) // 2
        box(d, x0, 0, x0 + thick - 1, H - 1, main, outline=False)
        d.line([x0, 0, x0, H - 1], fill=dark + (255,))
        d.line([x0 + thick - 1, 0, x0 + thick - 1, H - 1], fill=dark + (255,))
        if key in ("stone", "brick"):
            box(d, x0 + 2, 0, x0 + thick - 3, H - 1, darker(main, 0.85), outline=False)
            for y in range(0, H, 4):
                d.rectangle([x0, y, x0 + 1, y + 1], fill=main + (255,))
                d.rectangle([x0 + thick - 2, y, x0 + thick - 1, y + 1], fill=main + (255,))
        elif key == "palisade":
            for y in range(1, H, 4):
                d.ellipse([x0 + 1, y, x0 + thick - 2, y + 3], fill=dark + (255,))
        else:
            box(d, x0, 0, x0 + thick - 1, 2, dark, outline=False)
            box(d, x0, 8, x0 + thick - 1, 10, dark, outline=False)
    elif kind == "wall_corner":
        if key == "fence":
            box(d, 4, 2, W - 5, H - 1, C["log"])
            box(d, 3, 0, W - 4, 3, C["wood"])
        elif key == "palisade":
            d.polygon([(W // 2, 0), (0, 10), (W - 1, 10)], fill=C["roof"] + (255,), outline=C["roof_d"] + (255,))
            box(d, 1, 11, W - 2, 18, C["wood"])
            for x in (2, W - 4):
                box(d, x, 19, x + 2, H - 1, C["log"])
            d.line([3, 22, W - 4, H - 6], fill=C["log"] + (255,))
        else:
            for x in range(0, W, 8):
                d.rectangle([x, 0, x + 4, 5], fill=main + (255,))
            material(d, 0, 6, W - 1, H - 1, "stone" if key == "stone" else "brick")
            box(d, W // 2 - 1, 16, W // 2, 24, C["dark"])
    elif kind == "wall_gate_h":
        if key == "fence":
            for x in (0, W - 3):
                box(d, x, 0, x + 2, H - 1, C["log"])
            d.line([3, 6, 9, 9], fill=main + (255,), width=2)
            d.line([W - 4, 6, W - 10, 9], fill=main + (255,), width=2)
        elif key == "palisade":
            for x in (0, W - 5):
                box(d, x, 0, x + 4, H - 1, C["log"])
            box(d, 0, 2, W - 1, 6, darker(C["log"]))
            d.polygon([(5, 9), (11, 12), (11, H - 1), (5, H - 1)], fill=main + (255,))
            d.polygon([(W - 6, 9), (W - 12, 12), (W - 12, H - 1), (W - 6, H - 1)], fill=main + (255,))
        else:
            tw = W // 4
            for x0 in (0, W - tw):
                for x in range(x0, x0 + tw, 8):
                    d.rectangle([x, 0, x + 4, 5], fill=main + (255,))
                material(d, x0, 6, x0 + tw - 1, H - 1, "stone" if key == "stone" else "brick")
            wall_face(d, key, tw, 10, W - tw - 1, H - 1)
            d.rectangle([W // 2 - 9, H - 22, W // 2 + 8, H - 1], fill=C["dark"] + (255,))
            d.pieslice([W // 2 - 9, H - 31, W // 2 + 8, H - 13], 180, 360, fill=C["dark"] + (255,))
    elif kind == "wall_gate_v":
        x0 = (W - thick) // 2
        for y0 in (0, H - T):
            if key in ("stone", "brick"):
                box(d, 1, y0 + 1, W - 2, y0 + T - 2, main)
                box(d, 4, y0 + 4, W - 5, y0 + T - 5, darker(main, 1.1) if key == "stone" else C["plaster"], outline=False)
            else:
                box(d, x0, y0, x0 + thick - 1, y0 + T - 1, main)
                box(d, x0 - 1, y0 + T - 4, x0 + thick, y0 + T - 1, dark)


def road(d, key, var, W, H):
    if key == "dirt":
        box(d, 0, 0, W - 1, H - 1, (184, 150, 98), outline=False)
        for i, (x, y) in enumerate([(2, 3), (9, 1), (13, 9), (5, 11), (11, 14), (1, 8)][: 4 + var]):
            d.point((x, y), fill=(150, 118, 74, 255))
            d.point((x + 1, y), fill=(206, 176, 128, 255))
    elif key == "stone":
        box(d, 0, 0, W - 1, H - 1, (120, 114, 104), outline=False)
        cells = [(0, 0, 7, 6), (8, 0, 15, 4), (8, 5, 15, 10), (0, 7, 5, 15), (6, 7, 15, 15)] if var == 1 else \
            [(0, 0, 4, 4), (5, 0, 10, 6), (11, 0, 15, 5), (0, 5, 4, 10), (5, 7, 10, 12), (11, 6, 15, 12), (0, 11, 4, 15), (5, 13, 10, 15), (11, 13, 15, 15)]
        for x0, y0, x1, y1 in cells:
            d.rectangle([x0, y0, x1 - 1, y1 - 1], fill=(168, 160, 146, 255))
    else:
        box(d, 0, 0, W - 1, H - 1, (150, 120, 108), outline=False)
        if var == 1:
            for r, y in enumerate(range(0, H, 4)):
                for x in range(-4 if r % 2 else 0, W, 8):
                    d.rectangle([x, y, x + 6, y + 2], fill=C["brick"] + (255,))
        else:
            for y in range(0, H, 8):
                for x in range(0, W, 8):
                    d.rectangle([x, y, x + 6, y + 2], fill=C["brick"] + (255,))
                    d.rectangle([x, y + 4, x + 2, y + 7], fill=C["brick"] + (255,))
                    d.rectangle([x + 4, y + 4, x + 6, y + 7], fill=C["brick_d"] + (255,))


def frames_of(kw):
    return kw.get("frames", 1)


def item_image(item) -> Image.Image:
    group, name, w, h, roof_frac, kind, kw, what, use = item
    W, H = w * T, h * T
    n = frames_of(kw)
    strip = Image.new("RGBA", (W * n, H))
    for k in range(n):
        strip.alpha_composite(sketch(kind, W, H, roof_frac, kw, k), (k * W, 0))
    return strip


# ───────────────────────── 總覽（自我檢查：照規則拼一個聚落） ─────────────────────────

def overview() -> Image.Image:
    cols, rows = 44, 30
    img = Image.new("RGBA", (cols * T, rows * T), (143, 179, 106, 255))
    d = ImageDraw.Draw(img)
    river_x = 36
    d.rectangle([river_x * T, 0, (river_x + 3) * T - 1, rows * T - 1], fill=(91, 155, 213, 255))
    get = {i[1]: item_image(i) for i in ITEMS}

    def tile(name, x, y):
        img.alpha_composite(get[name], (x * T, y * T))

    def put(name, x, bottom):
        """建築左下角在方格 (x, bottom)。"""
        im = get[name]
        img.alpha_composite(im if frames_of(next(i for i in ITEMS if i[1] == name)[6]) == 1 else im.crop((0, 0, im.width // 4, im.height)),
                           (x * T, (bottom + 1) * T - im.height))

    def road_line(kind, x0, y0, x1, y1):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                tile(f"road_{kind}_{1 + (x * 7 + y * 3) % 2}", x, y)

    def enclose(key, x0, y0, x1, y1, gates_h=(), gates_v=()):
        """圍牆：上下兩列是橫段正面（素材往上長出），左右是縱段俯視，四角是角樓。"""
        hh = WALL_SIZES[key][0]
        cw, ch = WALL_SIZES[key][1]
        gw = WALL_SIZES[key][2][0]
        for y in (y0, y1):
            x = x0 + 1
            while x < x1:
                if (x, y) in gates_h:
                    put(f"{key}_gate_h", x, y)
                    x += gw
                    continue
                put(f"{key}_h", x, y)
                x += 1
        for x in (x0, x1):
            y = y0 + 1
            while y < y1:
                if (x, y) in gates_v:
                    tile(f"{key}_gate_v", x, y)
                    y += 3
                    continue
                tile(f"{key}_v", x, y)
                y += 1
        for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
            put(f"{key}_corner", x - (cw - 1) // 2 if cw > 1 else x, y)

    # 石牆城鎮：x 1..18、y 2..19；南門在 (8,19)、東門在 (18,9)。下牆正面往上佔一列，所以最南的巷道在 y16。
    road_line("brick", 9, 4, 9, 18)
    road_line("brick", 2, 10, 17, 10)
    road_line("brick", 2, 16, 17, 16)
    road_line("stone", 9, 20, 9, 23)
    road_line("stone", 19, 10, 34, 10)
    road_line("stone", 40, 10, 43, 10)
    put("house_1", 2, 9)
    put("stone_house_1", 5, 9)
    put("hall_town", 11, 9)
    put("well", 16, 9)
    put("house_2", 2, 15)
    put("stone_house_2", 5, 15)
    put("market", 11, 15)
    enclose("stone", 1, 2, 18, 19, gates_h={(8, 19)}, gates_v={(18, 9)})
    # 木柵村莊：x 21..33、y 18..29；西門 (21,22)、東門 (33,22)，門中間一格是 y23 的路
    road_line("dirt", 10, 23, 21, 23)
    road_line("dirt", 22, 23, 34, 23)
    road_line("dirt", 25, 24, 25, 26)
    road_line("dirt", 22, 27, 32, 27)
    put("hut_1", 22, 22)
    put("hut_2", 26, 22)
    put("hunting_lodge", 30, 22)
    put("hut_2", 22, 26)
    put("granary", 26, 26)
    put("hut_1", 30, 26)
    enclose("palisade", 21, 18, 33, 29, gates_v={(21, 22), (33, 22)})
    # 水邊：渡口（兩岸渡口柱＋渡船＋繩）、水車（輪子在水裡）、碼頭（俯視，程式可轉向）
    d.line([35 * T + 8, 9 * T + 6, 40 * T + 8, 9 * T + 6], fill=(92, 58, 26, 255), width=2)
    put("ferry_post", 35, 10)
    put("ferry", 37, 10)
    put("ferry_post", 40, 10)
    road_line("dirt", 32, 11, 32, 15)
    road_line("dirt", 33, 15, 35, 15)
    put("watermill", 33, 14)
    img.alpha_composite(get["dock"].rotate(90, expand=True), (35 * T, 22 * T))
    return img


# ───────────────────────── 文件、manifest ─────────────────────────

def files():
    out = []
    REF.mkdir(parents=True, exist_ok=True)
    for old in REF.glob("*.png"):
        old.unlink()
    for item in ITEMS:
        group, name, w, h, roof_frac, kind, kw, what, use = item
        im = item_image(item)
        refname = f"b9ref_{name}.png"
        im.resize((im.width * UP, im.height * UP), Image.NEAREST).save(REF / refname)
        n = frames_of(kw)
        spec = f"{w * T}×{h * T}" if n == 1 else f"{w * T * n}×{h * T}（橫排 {n} 格，每格 {w * T}×{h * T}）"
        roof_note = f"；上方約 {round(roof_frac * 100)}% 是屋頂（灰階）" if 0 < roof_frac < 1 else ("；整張灰階（帳篷布）" if roof_frac >= 1 else "")
        out.append((group, f"b9_{name}.png", spec + " PNG（透明背景）", refname, what + roof_note, use, f"{w}×{h} 格", "像素零件" if n == 1 else "像素零件（動畫）"))
    ov = overview()
    ov.resize((ov.width * 2, ov.height * 2), Image.NEAREST).save(REF / "b9ref_總覽.png")
    return out


def write_doc(rows) -> None:
    lines = [
        "# 第九批素材：正面視角的建築、圍牆、道路與水邊建築（像素風）",
        "",
        "> 使用方式：把「給 GPT 的指示」與下面所有表格整份貼給 GPT，並把 `assets/reference/b9/` 的參考圖一起附上（每列「參考圖」欄是檔名；`b9ref_總覽.png` 每次都附），",
        "> 請它逐張生成，檔名照表；全部完成後打包成 zip。拿到後放進 `D:\\DESK\\godot\\世界圖書館_素材`，告訴 Claude 匯入即可。",
        f"> 本檔由 `tools/batch9_assets.py` 產生（共 {len(rows)} 張），規格依第 {CHAPTER} 章；修改請改那支程式再重新產生。",
        "> 參考圖太多一次附不完時，可以分組（A～E）分次貼，每次貼「給 GPT 的指示」＋該組表格＋該組參考圖＋總覽。",
        "",
        "## 給 GPT 的指示（請整段照貼）",
        "",
        STYLE,
        "",
    ]
    group = None
    n = 0
    for g, file, spec, ref, what, use, tiles, kind in rows:
        if g != group:
            lines += ["", f"## {g}", "", "| # | 檔名 | 規格（像素） | 佔地 | 參考圖 | 畫什麼 | 用在哪裡 |", "|---|---|---|---|---|---|---|"]
            group = g
        n += 1
        lines.append(f"| {n} | `{file}` | {spec} | {tiles} | `{ref}` | {what} | {use} |")
    DOC.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_manifest(rows) -> None:
    text = MANIFEST.read_text(encoding="utf-8")
    old = [r for r in csv.reader(io.StringIO(text))]
    header, body = old[0], [r for r in old[1:] if not r[0].startswith("B9_")]
    for g, file, spec, ref, what, use, tiles, kind in rows:
        body.append([f"B9_{Path(file).stem[3:].upper()}", f"px2/{file}", kind, spec, use, CHAPTER, "GPT手動", "待製作", "",
                     f"{what}。佔地 {tiles}。參考圖 assets/reference/b9/{ref}。規格 {spec}。（共同風格與視角見 assets/GPT_第九批_正面像素素材.md）"])
    buf = io.StringIO()
    csv.writer(buf, lineterminator="\n").writerows([header] + body)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")


def main() -> int:
    rows = files()
    write_doc(rows)
    write_manifest(rows)
    print(f"第九批：{len(rows)} 張，參考圖在 {REF.relative_to(ROOT)}")
    return 0


# ───────────────────────── 交件匯入 ─────────────────────────

def import_delivery(folder: Path) -> int:
    """GPT 交件：整數倍放大的先縮回表中尺寸；屋頂比例以下的灰色加一點暖色（不被文化色誤染）；更新 manifest 狀態。"""
    by_name = {f"b9_{i[1]}.png": i for i in ITEMS}
    rows = list(csv.reader(io.StringIO(MANIFEST.read_text(encoding="utf-8"))))
    done, bad = 0, []
    for r in rows:
        if not r[0].startswith("B9_"):
            continue
        file = r[1].split("/")[1]
        src = folder / file
        if not src.exists():
            continue
        _, name, w, h, roof_frac, kind, kw, *_ = by_name[file]
        W, H = w * T * frames_of(kw), h * T
        im = Image.open(src).convert("RGBA")
        if im.size != (W, H):
            kx, ky = im.width / W, im.height / H
            if abs(kx - ky) > 0.02 or kx < 1:
                bad.append(f"{file} {im.width}×{im.height}（應為 {W}×{H} 或整數倍）")
                continue
            im = im.resize((W, H), Image.NEAREST)
        px = im.load()
        top = 1.0 if roof_frac >= 1 else roof_frac
        for y in range(H):
            if y < round(H * top) + 4:  # 屋簷兩端略低於屋頂比例
                continue
            for x in range(W):
                cr, cg, cb, ca = px[x, y]
                if ca > 127 and max(cr, cg, cb) - min(cr, cg, cb) <= 20:
                    px[x, y] = (min(255, cr + 12), min(255, cg + 3), max(0, cb - 12), 255)
        for y in range(H):
            for x in range(W):
                cr, cg, cb, ca = px[x, y]
                px[x, y] = (cr, cg, cb, 255 if ca > 127 else 0)
        im.save(PX2 / file)
        r[7] = "已交付"
        done += 1
    buf = io.StringIO()
    csv.writer(buf, lineterminator="\n").writerows(rows)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")
    print(f"第九批匯入 {done} 張到 {PX2.relative_to(ROOT)}")
    for b in bad:
        print("尺寸不符：", b)
    return 0


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--import":
        raise SystemExit(import_delivery(Path(sys.argv[2])))
    raise SystemExit(main())
