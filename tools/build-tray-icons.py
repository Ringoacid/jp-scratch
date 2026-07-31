#!/usr/bin/env python3
"""トレイアイコンの状態バリエーション（要件 3.1.1）を Assets/app.ico から生成する。

    python tools/build-tray-icons.py          # 生成
    python tools/build-tray-icons.py --check  # 既存ファイルと一致するかだけ確認（CI 用）

必要なもの: Python 3 と Pillow（`pip install pillow`）。開発時にアイコンを作り直すとき
だけ使う補助ツールで、ビルドや実行には要らない（生成物の .ico はリポジトリに入れてある）。

設計:

- **`Assets/app.ico` は手作りの正典**であり、このスクリプトは読むだけで書き換えない。
  状態アイコンは「元の絵にバッジを重ねただけ」に保ち、同じアプリだと一目で分かるようにする。
- **元の各サイズの絵をそのまま使う**。16px などの小サイズは 256px からの縮小ではなく
  app.ico が持つそのサイズのエントリを取り出して土台にする（手で詰めた小サイズの絵を壊さない）。
- **小サイズは DIB（BMP）で格納する。** `System.Drawing.Icon`（= NotifyIcon）は PNG 圧縮
  エントリを展開できないため、PNG にしてよいのは 256px だけ。Pillow の ICO 書き出しは
  256px 以外を BMP にするので既定のままでよいが、生成後に必ず検証する（`verify_layout`）。
- バッジは色だけに頼らず**形も変える**（三点の丸 / 三角 / 横棒の丸）。16px では潰れるが、
  色覚特性によらず区別できる手がかりを一つでも増やしておく。
"""

import argparse
import io
import struct
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
BASE_ICON = ROOT / "Assets" / "app.ico"

# app.ico が持つエントリと同じ構成で書き出す。
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]

# バッジを描くときの拡大率。この倍率で描いてから縮小することで縁を滑らかにする。
SUPERSAMPLE = 8

RING = (255, 255, 255, 255)  # バッジを本体から切り離す白い縁


def draw_dots(draw, box, color):
    """校正リクエスト中: 白い三点（処理中）。"""
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    r = (x1 - x0) * 0.075
    gap = (x1 - x0) * 0.22
    for dx in (-gap, 0.0, gap):
        draw.ellipse([cx + dx - r, cy - r, cx + dx + r, cy + r], fill=RING)


def draw_bang(draw, box, color):
    """API エラー: 白い「!」。"""
    x0, y0, x1, y1 = box
    cx = (x0 + x1) / 2
    w = (x1 - x0) * 0.085
    top = y0 + (y1 - y0) * 0.30
    bottom = y0 + (y1 - y0) * 0.63
    draw.rounded_rectangle([cx - w, top, cx + w, bottom], radius=w, fill=RING)
    dot = (x1 - x0) * 0.095
    dy = y0 + (y1 - y0) * 0.78
    draw.ellipse([cx - dot, dy - dot, cx + dot, dy + dot], fill=RING)


def draw_bar(draw, box, color):
    """月間上限到達: 白い横棒（これ以上進めない）。"""
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    half_w = (x1 - x0) * 0.28
    half_h = (y1 - y0) * 0.085
    draw.rounded_rectangle(
        [cx - half_w, cy - half_h, cx + half_w, cy + half_h], radius=half_h, fill=RING
    )


def badge_circle(draw, box, color, ring):
    x0, y0, x1, y1 = box
    draw.ellipse([x0, y0, x1, y1], fill=color, outline=RING, width=int(ring))


def badge_triangle(draw, box, color, ring):
    """角を丸めた警告三角。円のバッジと形で区別できるようにする。"""
    x0, y0, x1, y1 = box
    w = x1 - x0
    # 見た目の重心を円バッジと揃えるため、少し下へずらして高さを詰める。
    top = y0 + w * 0.04
    bottom = y1
    points = [((x0 + x1) / 2, top), (x1, bottom), (x0, bottom)]
    draw.line(points + [points[0]], fill=RING, width=int(ring * 3), joint="curve")
    draw.polygon(points, fill=color)


STATES = {
    # 名前: (バッジ色, バッジの形, 中の記号)
    "proofreading": ((16, 165, 199, 255), badge_circle, draw_dots),
    "error": ((214, 45, 45, 255), badge_triangle, draw_bang),
    "limit": ((240, 140, 0, 255), badge_circle, draw_bar),
}


def render_frame(base: Image.Image, size: int, spec) -> Image.Image:
    """1 サイズ分の絵にバッジを合成する。"""
    color, shape, glyph = spec
    frame = base.convert("RGBA").copy()

    s = size * SUPERSAMPLE
    layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)

    # 16px では中の記号は 2〜3 ピクセルにしかならず、潰れて逆に色を濁らせる。
    # 小サイズでは記号を描かず、形と色だけの塊にしたほうが通知領域では読み取れる。
    detailed = size >= 32

    # 右下に、アイコン幅の 46%（小サイズは 54%）。小さくすると通知領域で色が見えない。
    d = s * (0.46 if detailed else 0.54)
    margin = s * 0.02
    box = (s - margin - d, s - margin - d, s - margin, s - margin)
    # 縁は「1 物理ピクセル強」に収める。太いと小サイズで白が塗り色を食う。
    ring = SUPERSAMPLE * (1.4 if detailed else 1.0)

    shape(draw, box, color, ring)
    if detailed:
        glyph(draw, box, color)

    layer = layer.resize((size, size), Image.LANCZOS)
    frame.alpha_composite(layer)
    return frame


def encode_dib(frame: Image.Image) -> bytes:
    """32bpp の DIB エントリ（BITMAPINFOHEADER + BGRA + AND マスク）を作る。

    Pillow の ICO 書き出しは全エントリを PNG にするか（既定）、全エントリを BMP にするか
    （`bitmap_format="bmp"`）しか選べず、app.ico と同じ「小サイズは DIB・256px だけ PNG」に
    ならない。ここだけ自前で組む。高さは ICO の規約で「実際の 2 倍」を書く。
    """
    width, height = frame.size

    buffer = io.BytesIO()
    frame.save(buffer, format="dib")
    data = buffer.getvalue()
    data = data[:8] + struct.pack("<I", height * 2) + data[12:]

    # AND マスク。32bpp ではアルファが優先されるが、app.ico も持っているので同じ構成にする。
    # 1 = 透明。行は 4 バイト境界へ揃える。
    alpha = frame.getchannel("A")
    stride = ((width + 31) // 32) * 4
    mask = bytearray()
    for y in range(height - 1, -1, -1):  # DIB はボトムアップ
        row = bytearray(stride)
        for x in range(width):
            if alpha.getpixel((x, y)) < 128:
                row[x // 8] |= 0x80 >> (x % 8)
        mask += row

    return data + bytes(mask)


def build(name: str) -> bytes:
    spec = STATES[name]
    frames = []
    for size in SIZES:
        base = Image.open(BASE_ICON)
        base.size = (size, size)  # app.ico のそのサイズのエントリを選ぶ
        base.load()
        frames.append(render_frame(base, size, spec))

    payloads = []
    for frame in frames:
        if frame.size[0] >= 256:
            buffer = io.BytesIO()
            frame.save(buffer, format="png")
            payloads.append(buffer.getvalue())
        else:
            payloads.append(encode_dib(frame))

    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = len(header) + len(frames) * 16
    directory = b""
    for frame, payload in zip(frames, payloads):
        width, height = frame.size
        directory += struct.pack(
            "<BBBBHHII",
            width if width < 256 else 0,
            height if height < 256 else 0,
            0,  # 色数（32bpp なので 0）
            0,  # 予約
            1,  # プレーン数
            32,  # ビット深度
            len(payload),
            offset,
        )
        offset += len(payload)

    return header + directory + b"".join(payloads)


def verify_layout(data: bytes, label: str) -> None:
    """サイズ構成と「256px だけ PNG」を確認する。ここが崩れると NotifyIcon が絵を出せない。"""
    _, _, count = struct.unpack("<HHH", data[:6])
    seen = []
    for i in range(count):
        w, h, *_rest, offset = struct.unpack("<BBBBHHII", data[6 + i * 16 : 22 + i * 16])
        size = w or 256
        is_png = data[offset : offset + 8] == b"\x89PNG\r\n\x1a\n"
        if size == 256 and not is_png:
            raise SystemExit(f"{label}: 256px が PNG になっていない")
        if size != 256 and is_png:
            raise SystemExit(
                f"{label}: {size}px が PNG で格納されている。"
                "System.Drawing.Icon は PNG エントリを展開できない（DIB で格納すること）"
            )
        seen.append(size)
    if seen != SIZES:
        raise SystemExit(f"{label}: サイズ構成が app.ico と違う {seen}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="書き換えず、既存と一致するかだけ見る")
    args = parser.parse_args()

    ok = True
    for name in STATES:
        data = build(name)
        verify_layout(data, name)
        path = ROOT / "Assets" / f"app-{name}.ico"

        if args.check:
            current = path.read_bytes() if path.exists() else b""
            if current != data:
                print(f"NG  {path.relative_to(ROOT)} が生成結果と一致しません")
                ok = False
            else:
                print(f"OK  {path.relative_to(ROOT)}")
            continue

        path.write_bytes(data)
        print(f"生成 {path.relative_to(ROOT)}  {len(data):,} bytes")

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
