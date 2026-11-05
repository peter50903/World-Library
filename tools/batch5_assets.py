#!/usr/bin/env python3
"""第 08-18b 章第 6 步：第五批素材（方格世界全部重畫）。

一份清單同時產生兩樣東西，保證一致：
  python3 tools/batch5_assets.py          # 重寫 assets/GPT_第五批_方格世界.md，並把清單同步進 assets/manifest.csv
清單只在這裡改；已在 manifest 的列保留狀態與「已使用」欄，只更新規格、用途與提示詞。
使用者 2026-11-05：像素密度比 WorldBox 高一倍——下面清單以基準尺寸寫，輸出時所有像素數乘以 SCALE（插圖除外）。
"""
from __future__ import annotations

import csv
import io
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOC = ROOT / "assets" / "GPT_第五批_方格世界.md"
MANIFEST = ROOT / "assets" / "manifest.csv"
CHAPTER = "08-18b"

# (分組, 檔名（不含資料夾）, 類型, 規格, 畫什麼, 用在哪裡)
# 類型：地表方格＝可無縫拼接的方形地面；像素零件＝單一物件（去背、縮成規格）；像素零件（動畫）＝橫排 4 格；
#       介面圖示、介面插圖、介面；音樂；音效。
A: list[tuple[str, str, str, str, str, str]] = []

# 使用者 2026-11-05：細節要比 WorldBox 多——全部像素數乘 2（1280×720 時代插圖、400×200 橫幅不變）。
SCALE = 2


def scaled(spec: str) -> str:
    if "1280×720" in spec or "400×200" in spec:
        return spec
    return re.sub(r"(\d+)×(\d+)", lambda m: f"{int(m.group(1)) * SCALE}×{int(m.group(2)) * SCALE}", spec)


def add(group, name, kind, spec, what, use):
    A.append((group, name, kind, scaled(spec), what, use))


def variants(group, base, kind, spec, items, use):
    for i, what in enumerate(items, 1):
        add(group, f"{base}_{i}", kind, spec, what, use)


# ───────── A 地表方格（8×8 像素，可無縫拼接）─────────
T = "8×8 像素 PNG（不透明、四邊可無縫拼接）"
G = "A 地表方格"
biomes = {
    "grass_temperate": ("溫帶草地", "#5AA83C", ["亮綠草地，零星深綠小點", "亮綠草地，幾根較長的草葉", "略深的綠草地", "帶淺黃綠斑的草地"]),
    "grass_meadow": ("花原草地", "#6CC04A", ["嫩綠草地點綴白色小花", "嫩綠草地點綴黃色小花", "嫩綠草地點綴粉紫小花"]),
    "grass_steppe": ("乾草原", "#A8B04A", ["黃綠色短草地", "帶枯黃草莖的草地", "乾燥的黃綠草地"]),
    "floor_forest": ("溫帶林地面", "#367429", ["深綠林地，落葉碎點", "深綠林地，青苔斑", "深綠林地，小樹根"]),
    "floor_conifer": ("針葉林地面", "#2E5E35", ["墨綠地面，針葉碎點", "墨綠地面，小松果", "墨綠地面，青苔"]),
    "floor_autumn": ("紅葉林地面", "#B5652E", ["橘褐落葉地面", "紅橘落葉地面", "帶黃葉的橘褐地面"]),
    "ground_hill": ("丘陵地面", "#8A7D58", ["灰褐碎石地", "灰褐地面帶小石", "較亮的砂岩地面"]),
    "sand": ("沙灘", "#E3CF8A", ["乾沙，細小顆粒", "乾沙帶貝殼碎點", "略深的沙"]),
    "sand_wet": ("濕沙", "#C9B06A", ["靠水的濕沙，較深", "濕沙帶水痕"]),
    "soil": ("泥土", "#8A5A30", ["夯實泥土", "帶小石的泥土", "略乾的淺褐泥土"]),
    "ash": ("焦土", "#3B3330", ["黑灰焦土", "焦土帶餘燼橘點", "灰白的灰燼地"]),
    "mud": ("泥濘", "#6B5A3C", ["洪水退後的泥濘地", "帶積水反光的泥濘"]),
}
for key, (label, color, items) in biomes.items():
    variants(G, f"t_{key}", "地表方格", T, [f"{label}：{x}（主色 {color}）" for x in items], f"{label}的地面（同一種地形依生態區換用）")
water = {
    "sea_deep": ("深海", "#335FA6", 2), "sea": ("海", "#3B72B8", 2), "sea_shallow": ("近岸淺海", "#5793D0", 2),
    "river": ("河", "#4F8FD0", 2), "river_shallow": ("河淺灘", "#63A3DC", 2),
}
for key, (label, color, n) in water.items():
    variants(G, f"t_{key}", "地表方格", T, [f"{label}：水面，{'細小波紋' if i == 1 else '幾道淺色浪光'}（主色 {color}）" for i in range(1, n + 1)], f"{label}的水面")
add(G, "t_ford_stones", "地表方格", T, "河淺灘上露出的幾顆踏腳石", "渡口")
for key, label, what in [
    ("road_dirt_1", "土路", "淺褐色夯實土路，兩道車轍"), ("road_dirt_2", "土路", "淺褐色土路，零星小石"),
    ("road_cobble_1", "石子路", "灰色圓石鋪的路"), ("road_cobble_2", "石子路", "灰褐色石子路，有幾塊深色石"),
    ("plaza_stone_1", "石板廣場", "淺灰方石板，細縫"), ("plaza_stone_2", "石板廣場", "米白方石板，交錯排列"),
    ("plaza_dirt", "夯土廣場", "踩實的淺褐土地"), ("yard", "屋前空地", "房屋周圍踩出的淺褐泥土"),
]:
    add(G, f"t_{key}", "地表方格", T, what, f"聚落的{label}")
for key, what in [
    ("field_plowed", "剛翻好的田：深褐泥土，橫向田壟"), ("field_sown", "播了種的田：深褐田壟上一排排小點"),
    ("field_sprout", "冒出嫩芽的田：泥土上一行行淺綠小苗"), ("field_green", "生長中的田：一行行綠色作物"),
    ("field_wheat", "成熟麥田：金黃麥穗，橫向一行行"), ("field_vegetable", "成熟菜田：一行行深綠帶紅點的蔬菜"),
    ("field_flax", "成熟亞麻田：藍紫色小花一行行"), ("field_stubble", "收割後的田：淺黃殘梗"),
    ("field_fallow", "荒廢的田：雜草叢生的泥土"), ("field_ridge", "田埂：窄窄的草土小路"),
]:
    add(G, f"t_{key}", "地表方格", T, what, "農田（一塊農田格分成 2×2 塊田區）")

# ───────── B 樹與植物 ─────────
G = "B 樹與植物"
P = "像素零件"
trees = [
    ("tree_broad", "16×20", ["溫帶闊葉樹：圓樹冠、亮綠", "溫帶闊葉樹：寬樹冠、深綠", "溫帶闊葉樹：高瘦、黃綠", "溫帶橡樹：樹冠分兩團"], "溫帶林"),
    ("tree_conifer", "12×22", ["松樹：三層深綠枝葉", "杉樹：瘦高墨綠", "雪松：寬底尖頂", "小松樹：矮胖"], "針葉林"),
    ("tree_autumn_red", "16×20", ["楓樹：鮮紅樹冠", "楓樹：暗紅帶橘", "小楓樹：紅葉稀疏"], "紅葉林"),
    ("tree_autumn_gold", "16×20", ["銀杏：金黃扇形葉樹冠", "黃葉樺樹：白樹幹黃葉", "橘黃闊葉樹"], "紅葉林"),
    ("tree_birch", "12×20", ["白樺：白色樹幹黑斑、亮綠葉", "白樺：瘦長"], "溫帶林與花原"),
    ("tree_palm", "14×22", ["椰子樹：彎曲樹幹、羽狀葉", "矮椰子樹"], "沙灘"),
    ("tree_dead", "14×20", ["枯樹：灰褐光禿枝幹", "被龍捲風吹斷的樹：斷幹、碎枝"], "天災之後"),
    ("tree_burnt", "14×20", ["燒焦的樹：黑色枝幹、餘燼紅點", "燒剩的樹幹：一截黑炭"], "野火之後"),
]
for base, spec, items, use in trees:
    variants(G, f"px_{base}", P, f"{spec} 像素 PNG（透明背景）", items, f"{use}的樹（每方格最多一棵）")
variants(G, "px_bush", P, "12×9 像素 PNG（透明背景）", ["圓灌木：深綠", "莓果灌木：綠葉紅果", "開花灌木：粉紅小花", "枯灌木：黃褐"], "林緣與草地的灌木")
variants(G, "px_sapling", P, "7×10 像素 PNG（透明背景）", ["闊葉幼苗", "松樹幼苗"], "伐木後長回中的幼樹")
variants(G, "px_stump", P, "10×7 像素 PNG（透明背景）", ["新砍的樹樁：淺色年輪", "舊樹樁：灰褐長青苔"], "伐木地")
add(G, "px_log", P, "16×6 像素 PNG（透明背景）", "倒在地上的一截原木", "伐木地、龍捲風之後")
decor = [
    ("grass_tuft", "7×7", ["一撮亮綠草", "一撮高草", "一撮黃綠草", "一撮帶穗的草"]),
    ("flower", "7×7", ["白色小花三朵", "黃色小花三朵", "紅色小花", "藍紫色小花", "粉紅小花", "橘色小花"]),
    ("flower_big", "12×14", ["大向日葵", "大朵粉紫花", "一叢紅色大花"]),
    ("rock", "10×8", ["灰色圓石", "兩顆小石", "扁平石塊", "長青苔的石頭"]),
    ("rock_big", "18×14", ["大岩石：灰褐、左上亮", "裂開的大岩石"]),
    ("mushroom", "8×8", ["紅帽白點蘑菇", "一叢褐色小蘑菇"]),
    ("reed", "8×10", ["水邊蘆葦", "香蒲"]),
    ("lily", "8×6", ["睡蓮葉", "開花的睡蓮"]),
    ("dry_tuft", "7×7", ["枯黃草叢", "乾枯的小灌木"]),
]
for base, spec, items in decor:
    variants(G, f"px_{base}", P, f"{spec} 像素 PNG（透明背景）", items, "地上散布的小東西（細節層，遠看不畫）")

# ───────── C 丘陵與礦 ─────────
G = "C 丘陵與礦"
variants(G, "px_hill", P, "30×20 像素 PNG（透明背景）", ["灰褐岩石山丘", "圓頂土丘帶草", "兩座相連的小山", "陡峭的岩峰"], "丘陵上的山頭")
variants(G, "px_ore", P, "12×10 像素 PNG（透明背景）", ["岩石嵌橘金色礦點", "岩石帶鐵紅礦紋", "岩石帶青綠銅礦", "岩石帶亮白銀礦點"], "礦脈的礦石（挖完就消失）")
add(G, "px_ore_meteor", P, "14×12 像素 PNG（透明背景）", "焦黑隕石塊，裂縫透出淡藍光", "隕石帶來的礦")
add(G, "px_mine_active", P, "20×16 像素 PNG（透明背景）", "開採中的礦坑入口：木頭撐架、一台礦車", "開採中的礦脈")
add(G, "px_mine_pit", P, "16×12 像素 PNG（透明背景）", "廢棄的淺坑，周圍碎石與斷木", "挖空的礦脈")

# ───────── D 建築（屋頂與布料以灰階繪製，程式染文化色）─────────
G = "D 建築"
bld = [
    ("bld_tent", "16×14", ["獸皮帳篷：灰階篷布、木桿", "圓錐帳篷：灰階篷布、有門簾"], "營地的住處"),
    ("bld_hut", "20×18", ["茅草木屋：原木牆、灰階茅草屋頂", "圓形泥木屋：灰階草頂"], "村莊的住屋"),
    ("bld_house", "24×22", ["木造房子：木牆、灰階斜瓦屋頂、一扇窗", "木造房子：有煙囪", "雙層木房：灰階屋頂"], "城鎮的住屋"),
    ("bld_stone_house", "24×24", ["石屋：石牆、灰階瓦屋頂", "石屋：有木製陽台", "雙層石屋：灰階屋頂、煙囪"], "城市的住屋"),
    ("bld_site", "24×22", ["建地第一階段：木樁圍出的輪廓與一堆木材", "建地第二階段：木頭骨架", "建地第三階段：骨架加上半面牆"], "正在蓋的房子"),
]
for base, spec, items, use in bld:
    variants(G, base, P, f"{spec} 像素 PNG（透明背景）", items, use)
for name, spec, what, use in [
    ("bld_hall_village", "32×28", "村莊會堂：大木屋、灰階屋頂、門前旗桿掛灰階旗", "村莊中心"),
    ("bld_hall_town", "36×32", "城鎮市政廳：石基木牆、灰階屋頂、小鐘樓", "城鎮中心"),
    ("bld_keep", "40×44", "城堡主樓：石造方塔、城垛、灰階旗幟", "首都"),
    ("bld_market", "34×26", "市集：三個攤位、灰階遮雨布、木箱貨物", "市集"),
    ("bld_shrine", "20×22", "祖靈祠：小木祠、石燈、灰階布條", "村莊的信仰設施"),
    ("bld_temple", "34×36", "神殿：石柱、三角山牆、灰階屋頂", "城市的神殿"),
    ("bld_smithy", "26×22", "鐵匠鋪：石牆、冒煙的煙囪、門口鐵砧", "鐵匠鋪"),
    ("bld_workshop", "26×22", "木工坊：木棚、門口一堆木板與鋸架", "木工坊"),
    ("bld_scriptorium", "26×24", "抄寫所：石屋、窗內燭光、屋頂灰階", "抄寫所（書寫）"),
    ("bld_kiln", "22×20", "陶窯：圓頂磚窯、旁邊一排陶罐", "陶窯"),
    ("bld_granary", "24×20", "穀倉：木造高腳倉、灰階屋頂", "囤糧"),
    ("bld_stockpile", "24×16", "囤糧圍欄：矮木欄圍著麻袋、木材、石塊", "聚落中心的囤糧"),
    ("bld_well", "12×14", "石井：圓石井圈、木架、小桶", "掘井的聚落"),
    ("bld_hunting_lodge", "24×20", "獵屋：原木小屋、牆上掛獸皮與鹿角", "獵屋"),
    ("bld_fishing_hut", "24×18", "漁屋：架在水邊的木屋、晾著漁網", "臨水聚落"),
    ("bld_dock", "24×12", "木棧碼頭：伸進水裡的木板，綁著一艘小船", "臨水聚落"),
    ("bld_watchtower", "16×30", "木造瞭望塔", "城鎮以上"),
    ("bld_school", "30×26", "學校：石屋、門前一棵樹與長椅", "學校"),
    ("bld_ruin", "24×16", "倒塌的石牆與焦黑木梁", "遺跡"),
    ("bld_ruin_big", "34×24", "大建築的廢墟：斷柱與瓦礫", "城市的遺跡"),
]:
    add(G, name, P, f"{spec} 像素 PNG（透明背景）", what, use)
for name, spec, what in [
    ("bld_bonfire", "14×14", "篝火：一圈石頭、木柴、跳動的火焰（4 格動畫，火焰高低變化）"),
    ("bld_windmill", "26×34", "風車磨坊：石造塔身、灰階屋頂、四片扇葉（4 格動畫，扇葉旋轉）"),
    ("bld_chimney_smoke", "8×16", "煙囪冒出的一縷白煙（4 格動畫，往上飄散）"),
]:
    add(G, name, "像素零件（動畫）", f"單格 {spec}，橫排 4 格 PNG（透明背景）", what, "聚落裡會動的東西")

# ───────── E 城牆（只有城市以上）─────────
G = "E 城牆"
for name, spec, what in [
    ("wall_front", "8×14", "石城牆正面一段：石磚、上方垛口；左右兩端可以無縫接在一起"),
    ("wall_top", "8×8", "石城牆從上往下看的牆頂一段：走道石板、兩側矮牆；上下兩端可以無縫接在一起"),
    ("wall_tower", "16×24", "城牆角塔：圓石塔、尖錐灰階屋頂"),
    ("wall_gate", "24×24", "城門：拱門、厚木門、兩側小塔"),
    ("wall_gate_side", "10×24", "左右兩側牆上的城門（從上往下看）：牆頂中間一道開口與木門"),
]:
    add(G, name, P, f"{spec} 像素 PNG（透明背景）", what, "城市的城牆（沿聚落佔地外框）")

# ───────── F 人（近景小人，4 格走路動畫；衣服以灰階繪製，程式染文化色）─────────
G = "F 人（近景小人）"
for name, what in [
    ("unit_man", "成年男性村民"), ("unit_woman", "成年女性村民"), ("unit_child", "小孩"), ("unit_elder", "拄拐杖的老人"),
    ("unit_farmer", "扛鋤頭的農夫"), ("unit_woodcutter", "扛斧頭的樵夫"), ("unit_miner", "拿十字鎬的礦工"),
    ("unit_hunter", "背弓的獵人"), ("unit_fisher", "拿釣竿的漁夫"), ("unit_builder", "拿槌子的工匠"),
    ("unit_smith", "繫皮圍裙的鐵匠"), ("unit_priest", "穿長袍的祭司"), ("unit_scholar", "抱書的學者"),
    ("unit_leader", "戴頭冠的村長"), ("unit_migrant", "背大包袱的遷徙者"), ("unit_settler", "扛小旗的開拓者"),
]:
    add(G, name, "像素零件（動畫）", "單格 8×10，橫排 4 格 PNG（透明背景）", f"{what}：側面走路 4 格，衣服主色用灰階", "近景走動的小人")
variants(G, "fx_bubble", P, "9×9 像素 PNG（透明背景）", ["想法泡泡：麵包（餓了）", "想法泡泡：愛心", "想法泡泡：笑臉", "想法泡泡：哭臉", "想法泡泡：槌子（工作）", "想法泡泡：Zzz（睡覺）", "想法泡泡：怒氣", "想法泡泡：綠色病菌（生病）", "想法泡泡：驚嘆號", "想法泡泡：音符（慶典）"], "小人頭上的想法泡泡")

# ───────── G 指示物（遠看的棋子）─────────
G = "G 指示物"
add(G, "tok_base", P, "24×24 像素 PNG（透明背景）", "圓形棋子底座：深褐木質、外圈一道灰階邊（程式染文化色）、底部陰影", "遠看的棋子底座")
add(G, "tok_rim_gold", P, "26×26 像素 PNG（透明背景）", "金色外圈，中間鏤空", "最高一級人數的棋子")
for name, what in [
    ("tok_camp", "帳篷"), ("tok_village", "木屋"), ("tok_town", "有鐘樓的房子"), ("tok_city", "城牆與塔"), ("tok_capital", "王冠"),
    ("tok_farmer", "鋤頭與麥穗"), ("tok_woodcutter", "斧頭"), ("tok_miner", "十字鎬"), ("tok_hunter", "弓箭"), ("tok_fisher", "魚"),
    ("tok_migrants", "包袱與腳印"), ("tok_settlers", "小旗"), ("tok_soldier", "劍與盾（之後的軍隊）"), ("tok_merchant", "錢袋（之後的商隊）"),
    ("tok_priest", "信仰符號（之後的傳教士）"),
]:
    add(G, name, "介面圖示", "16×16 像素 PNG（透明背景）", f"棋子圖案：{what}，白色為主、深色描邊", "疊在棋子底座上")
add(G, "ui_nameplate", "介面", "64×16 像素 PNG（透明背景，九宮格可拉長）", "聚落名牌底框：深色圓角長條、左右兩端尖角裝飾、細金邊", "聚落頭頂的名牌")

# ───────── H 天災與特效（大型會動的物體＋留在地上的痕跡）─────────
G = "H 天災與特效"
A_ = "像素零件（動畫）"
for name, spec, what in [
    ("fx_tornado", "48×96", "龍捲風：半透明灰白漏斗雲、捲起的碎石與樹枝，旋轉"),
    ("fx_flood_wave", "48×24", "洪水浪頭：泥黃色湧浪、白色浪花，往前推"),
    ("fx_fire", "8×12", "一小團火焰：橘紅黃，跳動"),
    ("fx_fire_big", "24×32", "大火：連成一片的烈焰，跳動"),
    ("fx_smoke", "16×24", "灰黑濃煙一團，往上飄"),
    ("fx_lightning", "16×64", "閃電：白藍色鋸齒，第 1 格亮、之後漸暗"),
    ("fx_meteor", "24×24", "隕石：燃燒的岩塊與火尾，斜斜落下"),
    ("fx_impact", "48×48", "隕石撞擊：爆炸火光與塵土往外擴散"),
    ("fx_quake_dust", "32×16", "地震揚起的塵土，左右晃"),
    ("fx_rain", "48×48", "雨絲：斜斜的淺藍細線"),
    ("fx_heat_shimmer", "48×24", "熱浪：地面上淡淡的扭曲熱氣"),
    ("fx_plague_miasma", "32×32", "瘟疫瘴氣：暗綠紫色的霧，緩緩翻湧"),
    ("fx_spores", "24×24", "孢子：一群淡紫光點飄散"),
    ("fx_beast_attack", "16×16", "野獸撲擊：爪痕閃光"),
    ("fx_birth", "12×12", "新生：金色小光點往上飄"),
    ("fx_levelup", "16×16", "升級：金色光芒往上竄"),
]:
    add(G, name, A_, f"單格 {spec}，橫排 4 格 PNG（透明背景）", what, "天災與事件的動畫")
for name, spec, what in [
    ("dec_scorch", "24×24", "焦黑燒痕，邊緣不規則"), ("dec_crack", "32×16", "地面裂縫，深色鋸齒"), ("dec_crater", "40×32", "隕石坑：圓坑、周圍碎石"),
    ("dec_debris", "24×16", "龍捲風留下的碎木與瓦片"), ("dec_puddle", "16×10", "洪水退後的積水"), ("dec_ash", "12×8", "一小堆灰燼"),
    ("dec_bones", "12×8", "白骨（瘟疫、饑荒之後）"), ("dec_grave", "10×12", "木製墓碑"),
]:
    add(G, name, P, f"{spec} 像素 PNG（透明背景）", what, "災後留在地上的痕跡")
for name, what in [("mark_sick", "綠色骷髏：染病"), ("mark_hunger", "空碗：挨餓"), ("mark_disaster", "紅色驚嘆號：遭災"), ("mark_holy", "金色光環：聖地")]:
    add(G, name, "介面圖示", "12×12 像素 PNG（透明背景）", what, "聚落與小人頭上的標記")

# ───────── I 人像、紋章、信仰符號（重畫成同一風格；灰階部分由程式染色）─────────
G = "I 人像、紋章、信仰符號"
PP = "人像部件"
PS = "64×64 PNG（透明背景，部件疊在同一位置）"
for sex, label in [("m", "男"), ("f", "女")]:
    variants(G, f"pt_face_{sex}", PP, PS, [f"{label}性臉型（灰階膚色）：{x}" for x in ["圓臉", "方臉", "長臉", "瘦削", "寬下巴", "尖下巴"]], "代表人物人像：臉")
    variants(G, f"pt_hair_{sex}", PP, PS, [f"{label}性髮型（灰階）：{x}" for x in (["短髮", "平頭", "長直髮綁後", "捲髮", "光頭帶鬢角", "側分", "編髮", "亂髮"] if sex == "m" else ["長直髮", "盤髮", "雙辮", "短捲髮", "馬尾", "頭巾包髮", "披肩捲髮", "髮髻插簪"])], "代表人物人像：頭髮")
variants(G, "pt_beard", PP, PS, [f"鬍鬚（灰階）：{x}" for x in ["短鬍", "落腮鬍", "八字鬍", "山羊鬍", "長鬍"]], "代表人物人像：鬍鬚")
variants(G, "pt_age", PP, PS, ["中年紋路：眼角與額頭細紋", "老年皺紋：深紋與老人斑"], "代表人物人像：年齡")
for job in ["leader", "farmer", "carpenter", "smith", "hunter", "shaman", "scholar", "merchant", "fisher", "builder"]:
    for sex, label in [("m", "男"), ("f", "女")]:
        add(G, f"pt_clothes_{job}_{sex}", PP, PS, f"{label}性{ {'leader':'村長','farmer':'農師','carpenter':'木匠','smith':'鐵匠','hunter':'獵人','shaman':'巫祝','scholar':'學者','merchant':'商人','fisher':'漁夫','builder':'工匠'}[job]}的衣著（主要布料灰階、皮革金屬保留原色）", "代表人物人像：衣著")
variants(G, "pt_acc", PP, PS, ["頭冠", "羽毛頭飾", "皮帽", "學者帽", "巫祝骨飾", "耳環"], "代表人物人像：配件")
add(G, "pt_frame", "介面", "72×72 PNG（透明背景）", "人像外框：木框嵌金角", "代表人物人像外框")
variants(G, "em_shield", "紋章部件", "48×56 PNG（透明背景，灰階）", ["尖底盾", "圓底盾", "方盾", "燕尾旗盾", "菱形盾", "雙層邊框盾"], "文化紋章：盾形")
variants(G, "em_charge", "紋章部件", "32×32 PNG（透明背景，白色）", ["樹", "浪", "狼", "鹿", "鷹", "熊", "魚", "麥穗", "山", "太陽", "月", "星", "塔", "劍", "斧", "錘", "冠", "火焰", "蛇", "花"], "文化紋章：圖案")
variants(G, "fs_symbol", "信仰符號", "32×32 PNG（透明背景，白色）", ["祖靈面具", "聖樹", "鹿角", "聖火", "眼", "新月", "螺旋", "太陽", "雙手", "星環", "水滴", "骷髏"], "信仰符號")

# ───────── J 介面 ─────────
G = "J 介面"
for name, what in [
    ("ui_panel", "面板底框：深綠灰石框、四角鉚釘（九宮格可拉長）"), ("ui_panel_light", "淺色羊皮紙面板（九宮格可拉長）"),
    ("ui_tooltip", "提示框：深色半透明圓角框、細金邊（九宮格）"), ("ui_tab", "分頁籤：未選"), ("ui_tab_active", "分頁籤：選中"),
    ("ui_button", "方形按鈕：深色石框"), ("ui_button_active", "方形按鈕：紅框亮起"), ("ui_bar_frame", "數值條外框"),
]:
    add(G, name, "介面", "48×48 PNG（透明背景）", what, "觀察者介面")
stats = ["人口", "成年人", "小孩", "老人", "男", "女", "出生", "死亡", "餓死", "食物", "房屋", "聚落", "領土", "知識", "文化", "信仰", "語言", "家系", "快樂", "生命", "年齡", "等級", "聲望", "金幣", "木材", "石材", "礦石", "獵物", "魚", "麥", "天災", "疾病", "地下城", "時代", "年份", "定位"]
for i, s in enumerate(stats, 1):
    add(G, f"ic_stat_{i:02d}", "介面圖示", "16×16 像素 PNG（透明背景）", f"數值小圖示：{s}", "提示框與詳情裡的小圖示")
laws = ["天災", "疾病與瘟疫", "地下城與怪物", "野生動物", "熱浪", "野火", "近親婚配", "開拓新村", "知識傳播", "文化分化", "信仰創立", "時代推進"]
for i, s in enumerate(laws, 1):
    add(G, f"ic_law_{i:02d}", "介面圖示", "24×24 像素 PNG（透明背景）", f"世界法規圖示：{s}", "世界法規頁的開關")
for key, label in [("hope", "希望時代：晨光中的草原與第一座營地"), ("pioneer", "開拓時代：開墾田地、伐木、遠方冒煙的新村"), ("conquest", "征服時代：城牆、旗幟與列隊的軍隊")]:
    add(G, f"ui_era_{key}", "介面插圖", "1280×720 PNG", f"時代插圖（像素風大圖）：{label}", "進入新時代時全畫面顯示")
for key, label in [("laws", "刻滿符文的兩塊石板"), ("history", "攤開的編年史大書與燭台"), ("world", "從高空看見的大陸與雲")]:
    add(G, f"ui_banner_{key}", "介面插圖", "400×200 PNG", f"視窗頂端的橫幅插圖：{label}", "世界法規、編年史、世界總覽視窗")

# ───────── K 音樂（OGG，可無縫循環）─────────
G = "K 音樂"
for name, spec, what in [
    ("music_theme", "2～3 分鐘", "主題曲：溫暖、略帶史詩感的民謠管弦（木笛、豎琴、弦樂），標題畫面"),
    ("music_hope_1", "3～4 分鐘", "希望時代：悠閒明亮的田園，木笛與魯特琴"),
    ("music_hope_2", "3～4 分鐘", "希望時代：輕快的鄉村舞曲，手鼓與提琴"),
    ("music_pioneer_1", "3～4 分鐘", "開拓時代：踏實前進感，大提琴與鼓"),
    ("music_pioneer_2", "3～4 分鐘", "開拓時代：森林與伐木的節奏感，口琴與撥弦"),
    ("music_conquest_1", "3～4 分鐘", "征服時代：莊嚴的號角與定音鼓"),
    ("music_conquest_2", "3～4 分鐘", "征服時代：宮廷與王權，管風琴與弦樂"),
    ("music_night", "3 分鐘", "夜晚：安靜的豎琴與蟲鳴氛圍"),
    ("music_tension", "2 分鐘", "天災與饑荒：低沉緊張的弦樂與鼓"),
    ("music_sorrow", "2 分鐘", "聚落滅亡、成為遺跡：哀傷的大提琴獨奏"),
    ("music_wonder", "2 分鐘", "創立信仰、重大發現：神秘的合唱與鐘聲"),
    ("music_chronicle", "3 分鐘", "閱讀編年史：沉靜的魯特琴與羽毛筆氛圍"),
]:
    add(G, name, "音樂", f"OGG，{spec}，可無縫循環", what, "背景音樂")

# ───────── L 音效（OGG）─────────
G = "L 音效"
for name, spec, what in [
    ("amb_forest", "30 秒循環", "森林：鳥鳴、樹葉沙沙"), ("amb_meadow", "30 秒循環", "草原：風聲、蟲鳴"), ("amb_sea", "30 秒循環", "海浪拍岸"),
    ("amb_river", "30 秒循環", "河水流動"), ("amb_village", "30 秒循環", "村莊：遠處人聲、雞鳴、敲打聲"), ("amb_city", "30 秒循環", "城市：市集喧鬧、馬車"),
    ("amb_night", "30 秒循環", "夜晚：蟋蟀、貓頭鷹"), ("amb_rain", "30 秒循環", "下雨"), ("amb_fire", "30 秒循環", "大火燃燒的劈啪聲"),
    ("sfx_found_village", "2 秒", "建立新聚落：短號角"), ("sfx_tier_up", "2 秒", "聚落升級：鐘聲與歡呼"), ("sfx_build", "1 秒", "蓋好一棟房子：幾下槌子聲"),
    ("sfx_chop", "0.5 秒", "砍樹一下"), ("sfx_tree_fall", "1.5 秒", "大樹倒下"), ("sfx_mine", "0.5 秒", "十字鎬敲石"),
    ("sfx_harvest", "1 秒", "收割：鐮刀刷過麥稈"), ("sfx_birth", "1 秒", "新生：輕柔的風鈴"), ("sfx_death", "2 秒", "死亡：遠處的喪鐘"),
    ("sfx_tech", "1.5 秒", "學會技術：明亮的琶音"), ("sfx_era", "4 秒", "進入新時代：莊嚴的號角齊鳴"), ("sfx_faith", "3 秒", "創立信仰：神秘合唱"),
    ("sfx_culture_split", "2 秒", "文化分化：兩聲不同音色的笛"), ("sfx_ruin", "3 秒", "聚落成為遺跡：低沉的風與碎石"),
    ("sfx_thunder", "3 秒", "雷聲"), ("sfx_tornado", "4 秒循環", "龍捲風呼嘯"), ("sfx_quake", "3 秒", "地震轟隆"), ("sfx_flood", "3 秒", "洪水湧來"),
    ("sfx_meteor", "3 秒", "隕石呼嘯落下與撞擊"), ("sfx_wildfire", "3 秒", "野火燃起"), ("sfx_heatwave", "3 秒", "熱浪：乾燥的風與蟬"),
    ("sfx_plague", "3 秒", "瘟疫：咳嗽與不祥的低音"), ("sfx_beast", "1.5 秒", "野獸襲擊：狼嚎與撲咬"), ("sfx_dungeon", "3 秒", "地下城出現：地底的低吼"),
    ("ui_click", "0.1 秒", "按鈕點擊：木頭輕敲"), ("ui_hover", "0.1 秒", "滑鼠移過：極輕的沙沙"), ("ui_open", "0.3 秒", "打開視窗：翻開書頁"),
    ("ui_close", "0.3 秒", "關閉視窗：合上書"), ("ui_tab", "0.1 秒", "切換分頁：紙張"), ("ui_toggle_on", "0.2 秒", "開關打開：清脆"),
    ("ui_toggle_off", "0.2 秒", "開關關閉：低沉"), ("ui_speed", "0.3 秒", "改變速度：沙漏翻轉"), ("ui_pause", "0.3 秒", "暫停：輕鐘"),
]:
    add(G, name, "音效", f"OGG，{spec}", what, "音效")


def folder(kind: str, name: str) -> str:
    if kind == "音樂":
        return f"audio/music2/{name}.ogg"
    if kind == "音效":
        return f"audio/sfx2/{name}.ogg"
    return f"px2/{name}.png"


STYLE = """你要為一款像素風「上帝視角世界模擬」遊戲重新繪製全部素材（風格參考：WorldBox）。請逐張生成下表每一個檔案，檔名照表、一張圖一個檔案；全部完成後打包成 zip。

**共同風格（每一張都要遵守）**
- 正上方俯視的地面；立體物件（樹、山、建築、人、城牆）是「俯視略帶正面」：看得到屋頂或樹冠頂面與正面，看不到側面。光源在左上。
- 乾淨的像素畫：方正大像素、不得有柔邊、漸層與反鋸齒；每個物件有 1 像素寬、比本體深的同色系描邊（不要純黑）。
- 色彩明亮飽和、對比清楚，但自然景物略低飽和；人造物（建築、人、棋子）顏色更鮮明，放在地圖上要一眼跳出來。
- 「大像素」數量照表：例如 32×40 代表整個物件寬 32、高 40 像素。這款遊戲的像素密度比 WorldBox 高一倍，請畫出更細的細節：葉片層次、樹皮紋理、木紋、瓦片一片片、石縫、窗框、衣服皺褶與配件。請在 1024×1024 的圖中放大繪製（每個大像素是同樣大小的方塊），匯入時會縮回表中大小。像素數就是成品的實際大小，不要比表中更粗或更細。
- 單一物件置中、四周留白，背景完全透明；不要地面、不要陰影底座（陰影由程式畫）。
- **灰階部分**：表中寫「灰階」的地方（屋頂、篷布、旗幟、衣服主色、人像膚色與頭髮、紋章盾形），請用中灰色加明暗畫，不要上彩色；程式會依文化染色。
- **地表方格**：16×16 大像素、整張填滿不透明、四邊可以無縫拼接（左右、上下接起來看不出接縫）。
- **動畫**（類型含「動畫」）：同一張圖橫向排 4 格、每格同大小、物件位置一致，只有動作不同。
- 畫面中不得出現任何文字、浮水印，或既有作品的角色與標誌。
- **音樂與音效**：中世紀奇幻、溫暖不刺耳；音樂可無縫循環、尾巴接回開頭；音效乾淨、無背景雜音、音量一致，輸出 OGG。"""


def write_doc():
    out = io.StringIO()
    out.write("# 第五批素材：方格世界全部重畫（一次全部交給 GPT）\n\n")
    out.write("> 使用方式：把「給 GPT 的指示」與下面所有表格整份貼給 GPT，請它逐張（逐首）生成，檔名照表；全部完成後打包成 zip。\n")
    out.write("> 拿到後把檔案放進 `D:\\DESK\\godot\\世界圖書館_素材`，告訴 Claude 同步即可；縮放、去背、對齊由匯入工具處理。\n")
    out.write(f"> 本檔由 `tools/batch5_assets.py` 產生（共 {len(A)} 項），規格依第 08-18b 章；修改請改那支程式再重新產生。\n\n")
    out.write("## 給 GPT 的指示（請整段照貼）\n\n")
    out.write(STYLE + "\n")
    group = None
    n = 0
    for g, name, kind, spec, what, use in A:
        if g != group:
            group = g
            out.write(f"\n## {g}\n\n| # | 檔名 | 規格 | 畫什麼 | 用在哪裡 |\n|---|---|---|---|---|\n")
        n += 1
        file = folder(kind, name).split("/")[-1]
        out.write(f"| {n} | `{file}` | {spec} | {what} | {use} |\n")
    DOC.write_text(out.getvalue(), encoding="utf-8")


def sync_manifest():
    text = MANIFEST.read_text(encoding="utf-8")
    rows = list(csv.reader(io.StringIO(text)))
    header, body = rows[0], rows[1:]
    index = {r[0]: i for i, r in enumerate(body)}
    for g, name, kind, spec, what, use in A:
        rid = "B5_" + name.upper()
        prompt = f"{what}。規格 {spec}。（第五批共同風格見 assets/GPT_第五批_方格世界.md）"
        row = [rid, folder(kind, name), kind, spec, use, CHAPTER, "GPT手動", "待製作", "", prompt]
        if rid in index:
            old = body[index[rid]]
            row[7], row[8] = old[7], old[8]
            body[index[rid]] = row
        else:
            body.append(row)
    buf = io.StringIO()
    csv.writer(buf, lineterminator="\n").writerows([header] + body)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")


if __name__ == "__main__":
    write_doc()
    sync_manifest()
    print(f"第五批素材 {len(A)} 項：已寫入 {DOC.relative_to(ROOT)} 與 manifest")
