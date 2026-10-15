#!/usr/bin/env python3
"""素材自動生成：讀 assets/manifest.csv，為「生成方式＝自動、狀態＝待製作」的圖片列呼叫 OpenAI 圖片 API。

- 產圖後依「規格」欄縮放到目標尺寸，存到 godot/assets/<檔名>，並把該列狀態改為「已交付」。
- 需要環境變數 OPENAI_API_KEY；沒有就印出說明並以結束碼 0 離開（不視為失敗）。
- 可選環境變數：OPENAI_IMAGE_MODEL（預設 gpt-image-1）、ASSET_LIMIT（一次最多幾張，預設 10）、ASSET_IDS（只做指定 ID，逗號分隔）。
- 只處理圖片；音樂與音效一律由 GPT 手動製作。
"""
from __future__ import annotations

import base64
import csv
import io
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "assets" / "manifest.csv"
ASSET_DIR = ROOT / "godot" / "assets"
API_URL = "https://api.openai.com/v1/images/generations"
IMAGE_TYPES = {"介面圖示", "地形圖塊", "疊層", "圖示", "介面", "介面視覺稿"}


def target_size(spec: str) -> tuple[int, int]:
    m = re.search(r"(\d+)\s*[×xX]\s*(\d+)", spec)
    if not m:
        raise ValueError(f"規格欄找不到尺寸：{spec}")
    return int(m.group(1)), int(m.group(2))


def api_size(w: int, h: int) -> str:
    if w > h * 1.2:
        return "1536x1024"
    if h > w * 1.2:
        return "1024x1536"
    return "1024x1024"


def generate(prompt: str, size: str, transparent: bool, key: str, model: str) -> bytes:
    body = {"model": model, "prompt": prompt, "size": size, "n": 1, "output_format": "png"}
    if transparent:
        body["background"] = "transparent"
    req = urllib.request.Request(
        API_URL,
        data=json.dumps(body).encode("utf-8"),
        headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=300) as resp:
        data = json.load(resp)
    return base64.b64decode(data["data"][0]["b64_json"])


def main() -> int:
    key = os.environ.get("OPENAI_API_KEY", "").strip()
    if not key:
        print("未設定 OPENAI_API_KEY：略過素材自動生成。")
        return 0
    from PIL import Image  # 只有真的要產圖時才需要 Pillow

    model = os.environ.get("OPENAI_IMAGE_MODEL", "").strip() or "gpt-image-1"
    limit = int(os.environ.get("ASSET_LIMIT", "10") or "10")
    only = {s.strip() for s in os.environ.get("ASSET_IDS", "").split(",") if s.strip()}

    with MANIFEST.open(encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        fields = reader.fieldnames
        rows = list(reader)

    done, failed = [], []
    for row in rows:
        if len(done) + len(failed) >= limit:
            break
        if row["生成方式"] != "自動" or row["狀態"] != "待製作" or row["類型"] not in IMAGE_TYPES:
            continue
        if only and row["ID"] not in only:
            continue
        w, h = target_size(row["規格"])
        transparent = "透明" in row["規格"]
        try:
            raw = generate(row["提示詞"], api_size(w, h), transparent, key, model)
            img = Image.open(io.BytesIO(raw)).convert("RGBA" if transparent else "RGB")
            img = img.resize((w, h), Image.Resampling.BOX)
            out = ASSET_DIR / row["檔名"]
            out.parent.mkdir(parents=True, exist_ok=True)
            img.save(out, format="PNG")
            row["狀態"] = "已交付"
            done.append(row["ID"])
            print(f"已產生 {row['ID']} → {out.relative_to(ROOT)}（{w}×{h}）")
        except (urllib.error.URLError, KeyError, ValueError, OSError) as e:
            detail = e.read().decode("utf-8", "replace")[:300] if isinstance(e, urllib.error.HTTPError) else str(e)
            failed.append(row["ID"])
            print(f"失敗 {row['ID']}：{detail}", file=sys.stderr)

    if done:
        with MANIFEST.open("w", encoding="utf-8", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=fields, lineterminator="\n")
            writer.writeheader()
            writer.writerows(rows)
    print(f"完成 {len(done)} 張，失敗 {len(failed)} 張。")
    return 1 if failed and not done else 0


if __name__ == "__main__":
    sys.exit(main())
