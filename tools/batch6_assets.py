#!/usr/bin/env python3
"""試玩回饋 2026-11-06 之 10：第六批素材（第五批沒有、這次新加的東西：動物、地下城怪物、勢力範圍）。

  python3 tools/batch6_assets.py          # 重寫 assets/GPT_第六批_動物與勢力.md，並把清單同步進 assets/manifest.csv
共同風格與第五批相同（沿用 tools/batch5_assets.py 的 STYLE、像素倍率與資料夾規則）；這裡只放新的項目。
"""
from __future__ import annotations

import csv
import importlib.util
import io
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOC = ROOT / "assets" / "GPT_第六批_動物與勢力.md"
MANIFEST = ROOT / "assets" / "manifest.csv"
CHAPTER = "08-18c"

_spec = importlib.util.spec_from_file_location("batch5", ROOT / "tools" / "batch5_assets.py")
batch5 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(batch5)  # type: ignore[union-attr]

A: list[tuple[str, str, str, str, str, str]] = []


def add(group, name, kind, spec, what, use):
    A.append((group, name, kind, batch5.scaled(spec), what, use))


P = "像素零件"
M = "像素零件（動畫）"

# ───────── H 野生動物（近景；有習性：成群、獨行、狼群）─────────
G = "H 野生動物"
for name, spec, what, use in [
    ("ani_deer_walk", "12×10", "公鹿側面走路 4 格（有鹿角，棕色、白肚）", "鹿群、獨行的鹿"),
    ("ani_deer_graze", "12×10", "公鹿低頭吃草 4 格（頭慢慢低下再抬起）", "停下來吃草"),
    ("ani_doe_walk", "11×9", "母鹿側面走路 4 格（沒有角，毛色較淺）", "鹿群裡的母鹿"),
    ("ani_fawn_walk", "8×7", "小鹿側面走路 4 格（身上白色斑點）", "鹿群裡的小鹿"),
    ("ani_goat_walk", "10×9", "山羊側面走路 4 格（灰白長毛、彎角、短鬍子）", "丘陵上的山羊群"),
    ("ani_goat_graze", "10×9", "山羊低頭吃草 4 格", "丘陵上停下吃草"),
    ("ani_wolf_walk", "12×8", "灰狼側面小跑 4 格（豎耳、蓬鬆尾巴）", "狼群跟在頭狼後面、獨狼遊蕩"),
    ("ani_wolf_run", "13×8", "灰狼全速奔跑 4 格（身體拉長、四腳離地）", "狼群追獵"),
    ("ani_wolf_alpha_walk", "14×9", "頭狼側面走路 4 格（比一般狼大一號、毛色較深、頸部鬃毛）", "狼群最前面的頭狼"),
    ("ani_wolf_howl", "10×10", "狼坐著仰頭嚎叫 4 格", "獨狼在巢穴附近"),
    ("ani_wolf_rest", "12×6", "狼趴在地上休息 4 格（尾巴輕擺）", "狼群在巢穴休息"),
]:
    add(G, name, M, f"單格 {spec}，橫排 4 格 PNG（透明背景）", what, use)
add(G, "ani_den", P, "16×10 像素 PNG（透明背景）", "狼穴：岩石下的黑色洞口、旁邊幾根骨頭", "狼群的巢穴格")
add(G, "ani_tracks", P, "8×8 像素 PNG（透明背景）", "地上一串淺淺的動物腳印", "動物常走的路（細節層）")

# ───────── I 地下城與怪物（近景；地下城門口有怪物踱步）─────────
G = "I 地下城與怪物"
for name, spec, what in [
    ("dun_cave", "28×20", "地下城入口：山壁上的黑色洞窟，洞口垂著藤蔓、地上散落碎石"),
    ("dun_ruin_gate", "28×24", "地下城入口：半埋在土裡的古代石門，門縫透出微弱紫光"),
    ("dun_crypt", "26×22", "地下城入口：長滿青苔的地下墓室入口，石階往下"),
]:
    add(G, name, P, f"{spec} 像素 PNG（透明背景）", what, "地下城所在格（依層數或種類換用）")
for name, spec, what in [
    ("mon_goblin_walk", "10×12", "哥布林側面走路 4 格（綠皮、尖耳、拿短棍）"),
    ("mon_skeleton_walk", "10×14", "骷髏兵側面走路 4 格（拿生鏽的劍）"),
    ("mon_troll_walk", "18×22", "巨魔側面走路 4 格（灰綠色、駝背、拿大木棒）"),
    ("mon_shadow_idle", "14×18", "暗影怪物原地浮動 4 格（深紫色身影、發光的眼睛、邊緣冒黑煙）"),
]:
    add(G, name, M, f"單格 {spec}，橫排 4 格 PNG（透明背景）", what, "地下城門口來回踱步的怪物")

# ───────── J 勢力範圍與層級（第 08-19 章）─────────
G = "J 勢力範圍與層級"
for name, spec, what in [
    ("terr_banner_village", "8×16", "插在地上的小三角旗：木桿、旗面灰階（程式染文化色）、隨風飄 4 格"),
    ("terr_banner_town", "10×20", "較大的長方旗：木桿頂端有小銅球、旗面灰階、隨風飄 4 格"),
    ("terr_banner_city", "12×24", "城市大旗：石座、高旗桿、燕尾旗面灰階、隨風飄 4 格"),
]:
    add(G, name, M, f"單格 {spec}，橫排 4 格 PNG（透明背景）", what, "勢力範圍邊界上的旗子（遠看前插在邊界轉角）")
add(G, "terr_border_post", P, "6×10 像素 PNG（透明背景）", "界碑：一根短石柱、頂端刻一道灰階條紋（程式染文化色）", "勢力範圍邊界的界碑")
for name, what in [
    ("tier_camp", "帳篷"), ("tier_village", "小木屋"), ("tier_town", "有鐘樓的房子"), ("tier_city", "城牆與塔"), ("tier_capital", "王冠"),
]:
    add(G, name, P, "10×10 像素 PNG（透明背景）", f"層級徽章：圓形銅牌上一個{what}", "名牌上名字旁的層級徽章（營、村、鎮、城、都）")
add(G, "terr_vassal_link", P, "10×10 像素 PNG（透明背景）", "隸屬徽章：小盾牌上一條往上的箭頭", "名牌上表示「隸屬於某城鎮」")

# ───────── K 音效 ─────────
G = "K 音效"
for name, spec, what in [
    ("sfx_deer", "1 秒", "鹿鳴：短促清亮的一聲"),
    ("sfx_wolf_howl", "3 秒", "遠處的狼嚎（不嚇人、帶點回音）"),
    ("sfx_goat", "1 秒", "山羊咩一聲"),
    ("sfx_monster", "2 秒", "地下城怪物低吼"),
    ("sfx_territory_grow", "1.5 秒", "勢力範圍擴大：短號角一聲"),
    ("sfx_vassal", "2 秒", "村莊歸附城鎮：鐘聲兩響"),
]:
    add(G, name, "音效", f"OGG，{spec}", what, "音效")

NOTE = """**和第五批的關係**
- 這一批只有第五批沒有的新東西，風格、尺寸規則、灰階染色、動畫排法完全照第五批的「給 GPT 的指示」。
- 第五批的 `mark_disaster.png`（紅色天災警示符號）不需要畫了（試玩回饋 2026-11-06：很出戲，地圖上不再顯示）。
- 第五批的花（`px_flower_*`、`px_flower_big_*`）照畫，但遊戲裡會用得很少（試玩回饋：花太多太雜）。"""


def write_doc():
    out = io.StringIO()
    out.write("# 第六批素材：野生動物、地下城怪物、勢力範圍（一次全部交給 GPT）\n\n")
    out.write("> 使用方式：把下面「給 GPT 的指示」與所有表格整份貼給 GPT，請它逐張生成，檔名照表；全部完成後打包成 zip。\n")
    out.write("> 拿到後把檔案放進 `D:\\DESK\\godot\\世界圖書館_素材`，告訴 Claude 同步即可。\n")
    out.write(f"> 本檔由 `tools/batch6_assets.py` 產生（共 {len(A)} 項）；修改請改那支程式再重新產生。\n\n")
    out.write(NOTE + "\n\n")
    out.write("## 給 GPT 的指示（請整段照貼）\n\n")
    out.write(batch5.STYLE.replace("重新繪製全部素材", "補畫一批新素材") + "\n")
    group = None
    n = 0
    for g, name, kind, spec, what, use in A:
        if g != group:
            group = g
            out.write(f"\n## {g}\n\n| # | 檔名 | 規格 | 畫什麼 | 用在哪裡 |\n|---|---|---|---|---|\n")
        n += 1
        file = batch5.folder(kind, name).split("/")[-1]
        out.write(f"| {n} | `{file}` | {spec} | {what} | {use} |\n")
    DOC.write_text(out.getvalue(), encoding="utf-8")


def sync_manifest():
    text = MANIFEST.read_text(encoding="utf-8")
    rows = list(csv.reader(io.StringIO(text)))
    header, body = rows[0], rows[1:]
    index = {r[0]: i for i, r in enumerate(body)}
    for g, name, kind, spec, what, use in A:
        rid = "B6_" + name.upper()
        prompt = f"{what}。規格 {spec}。（共同風格見 assets/GPT_第六批_動物與勢力.md）"
        row = [rid, batch5.folder(kind, name), kind, spec, use, CHAPTER, "GPT手動", "待製作", "", prompt]
        if rid in index:
            old = body[index[rid]]
            row[7], row[8] = old[7], old[8]
            body[index[rid]] = row
        else:
            body.append(row)
    # 第五批的紅色天災警示符號不再使用。
    for r in body:
        if r[0] == "B5_MARK_DISASTER" and r[7] == "待製作":
            r[7] = "停用"
    buf = io.StringIO()
    csv.writer(buf, lineterminator="\n").writerows([header] + body)
    MANIFEST.write_text(buf.getvalue(), encoding="utf-8")


if __name__ == "__main__":
    write_doc()
    sync_manifest()
    print(f"第六批素材 {len(A)} 項：已寫入 {DOC.relative_to(ROOT)} 與 manifest")
