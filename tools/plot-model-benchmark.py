#!/usr/bin/env python3
"""model-benchmark-*.json から README 用の比較図を作る。

    pip install matplotlib
    python tools/plot-model-benchmark.py
    python tools/plot-model-benchmark.py --input PromptValidation/results/xxx.json --markdown

入力を省略すると PromptValidation/results/model-benchmark-*.json の最新を使う。
出力は docs/images/ へ、ライト用とダーク用を別々に書き出す（GitHub の README から
<picture> で出し分ける前提。自動反転ではなく、ダーク面に合わせて選んだ色を使う）。

図のルール（意図的な設計なので変更時は理由を持って変えること）:

* 料金軸は対数。最安 $0.0006 と最高 $0.03 で 50 倍開くため、線形だと安いモデルが
  すべて底に潰れて読めない。
* プロバイダーを色で分けない。4 色を散布図（全ペア判定）で使うと、ライトかダークの
  どちらかで必ず判別性の下限を割る。プロバイダーは棒グラフでは行の並び、散布図では
  ラベルの文字が示す。マークは単色で、全点に直接ラベルを付ける。
* 背景は不透明で塗る。透過のままだと GitHub のダークモードで軸ラベルが消える。
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import FuncFormatter, LogLocator, NullFormatter

REPO_ROOT = Path(__file__).resolve().parent.parent
RESULTS_DIR = REPO_ROOT / "PromptValidation" / "results"
IMAGES_DIR = REPO_ROOT / "docs" / "images"

# 日本語を明示しないと matplotlib の既定（DejaVu Sans）で豆腐になる。
FONT_STACK = ["Yu Gothic", "Noto Sans JP", "Meiryo", "BIZ UDGothic", "MS Gothic"]

# dataviz の参照パレット。カテゴリのスロット1（青）と、各モードのクローム。
THEMES = {
    "light": {
        "surface": "#fcfcfb",
        "series": "#2a78d6",
        "primary": "#0b0b0b",
        "secondary": "#52514e",
        "muted": "#898781",
        "grid": "#e1e0d9",
        "baseline": "#c3c2b7",
    },
    "dark": {
        "surface": "#1a1a19",
        "series": "#3987e5",
        "primary": "#ffffff",
        "secondary": "#c3c2b7",
        "muted": "#898781",
        "grid": "#2c2c2a",
        "baseline": "#383835",
    },
}

PROVIDER_ORDER = ["OpenAI", "Gemini", "Anthropic", "PLaMo"]

# JSON に入るのは本体の ProviderDisplayName（設定画面と同じ製品名）。README の表は
# 他の箇所が会社名で揃っているので、表に出すときだけ読み替える。
PROVIDER_LABELS = {"Gemini": "Google", "PLaMo": "Preferred Networks"}


def latest_report() -> Path:
    candidates = sorted(RESULTS_DIR.glob("model-benchmark-*.json"))
    if not candidates:
        raise SystemExit(f"{RESULTS_DIR} に model-benchmark-*.json がありません。")
    return candidates[-1]


def load(path: Path) -> dict:
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def rows(report: dict) -> list[dict]:
    """図と表で共有する 1 モデル 1 行のデータ。料金が未確認のモデルは cost を None にする。"""
    by_id = {model["id"]: model for model in report["models"]}
    result = []
    for summary in report["summary"]:
        model = by_id[summary["modelId"]]
        cost = summary["medianCostUsd"] if summary["costUsdKnown"] else None
        result.append(
            {
                "id": summary["modelId"],
                "name": summary["displayName"],
                "provider": summary["provider"],
                "effort": model["effort"],
                "input_price": model["inputPricePerMillion"],
                "output_price": model["outputPricePerMillion"],
                "currency": model["currency"],
                "median_s": None
                if summary["medianElapsedMs"] is None
                else summary["medianElapsedMs"] / 1000,
                "min_s": None
                if summary["minElapsedMs"] is None
                else summary["minElapsedMs"] / 1000,
                "max_s": None
                if summary["maxElapsedMs"] is None
                else summary["maxElapsedMs"] / 1000,
                "cost": cost,
                "changes": summary["meanChangeCount"],
                "rejected": summary["rejectedCount"],
                "failures": summary["failureCount"],
                "protection": summary["protection"],
            }
        )
    return result


def pareto(points: list[dict]) -> list[dict]:
    """所要時間と料金の両方で他に負けていない点。速い順に見て、料金の最小値を更新した点だけ残す。"""
    usable = sorted(
        (p for p in points if p["median_s"] is not None and p["cost"] is not None),
        key=lambda p: p["median_s"],
    )
    best = None
    frontier = []
    for point in usable:
        if best is None or point["cost"] < best:
            best = point["cost"]
            frontier.append(point)
    return frontier


def spread_labels(values: list[float], minimum_gap: float) -> list[float]:
    """ラベルの y 位置を、元の順序を保ったまま最低間隔だけ空くよう押し広げる。"""
    order = sorted(range(len(values)), key=lambda index: values[index])
    adjusted = list(values)
    previous = None
    for index in order:
        if previous is not None and adjusted[index] - previous < minimum_gap:
            adjusted[index] = previous + minimum_gap
        previous = adjusted[index]
    return adjusted


def style(theme: dict) -> None:
    plt.rcParams.update(
        {
            "font.family": FONT_STACK,
            "font.size": 10,
            "axes.unicode_minus": False,
            "figure.facecolor": theme["surface"],
            "axes.facecolor": theme["surface"],
            "savefig.facecolor": theme["surface"],
            "text.color": theme["primary"],
            "axes.labelcolor": theme["secondary"],
            "xtick.color": theme["muted"],
            "ytick.color": theme["muted"],
        }
    )


def recede_axes(axes, theme: dict, *, left: bool = True, bottom: bool = True) -> None:
    for side in ("top", "right"):
        axes.spines[side].set_visible(False)
    for side, keep in (("left", left), ("bottom", bottom)):
        axes.spines[side].set_visible(keep)
        if keep:
            axes.spines[side].set_color(theme["baseline"])
            axes.spines[side].set_linewidth(1)
    axes.tick_params(length=0)


def draw_scatter(data: list[dict], theme: dict, meta: str, path: Path) -> None:
    style(theme)
    figure, axes = plt.subplots(figsize=(9.5, 6.2), dpi=200)

    points = [p for p in data if p["median_s"] is not None and p["cost"] is not None]
    frontier = pareto(data)

    # 両軸とも対数。時間は最速 2 秒と最遅 39 秒で 19 倍、料金は $0.0006 と $0.03 で 50 倍
    # 開くため、線形だと速くて安いモデルが左下の一角に固まって読めない。
    axes.set_xscale("log")
    axes.set_yscale("log")
    axes.grid(True, which="major", axis="both", color=theme["grid"], linewidth=1, zorder=0)
    axes.set_axisbelow(True)

    if len(frontier) > 1:
        # 階段線。「これより左下に行けるモデルは無い」という境界を目で追えるようにする。
        step_x, step_y = [], []
        for index, point in enumerate(frontier):
            if index > 0:
                step_x.append(point["median_s"])
                step_y.append(frontier[index - 1]["cost"])
            step_x.append(point["median_s"])
            step_y.append(point["cost"])
        axes.plot(step_x, step_y, color=theme["baseline"], linewidth=2, zorder=1)

    frontier_ids = {p["id"] for p in frontier}
    for point in points:
        on_frontier = point["id"] in frontier_ids
        axes.scatter(
            point["median_s"],
            point["cost"],
            s=150 if on_frontier else 90,
            color=theme["series"],
            edgecolors=theme["surface"],
            linewidths=2,
            alpha=1.0 if on_frontier else 0.55,
            zorder=3,
        )

    # ラベルは全点に付ける（単色なので、識別は文字だけが担う）。重なりを避けて縦にずらすので、
    # ずれた分だけ細い引き出し線で点と結ぶ（どのラベルがどの点か分からなくなるのを防ぐ）。
    log_costs = [math.log10(p["cost"]) for p in points]
    span = (max(log_costs) - min(log_costs)) or 1
    spread = spread_labels(log_costs, span * 0.052)
    for point, log_y in zip(points, spread):
        bold = point["id"] in frontier_ids
        # 横軸も対数なので、ずらす量は加算ではなく倍率で置く。
        leader_x = point["median_s"] * 1.035
        if abs(log_y - math.log10(point["cost"])) > span * 0.012:
            axes.plot(
                [point["median_s"], leader_x],
                [point["cost"], 10**log_y],
                color=theme["muted"],
                linewidth=0.8,
                alpha=0.5,
                zorder=2,
            )
        axes.annotate(
            point["name"],
            xy=(point["median_s"], point["cost"]),
            xytext=(leader_x * 1.02, 10**log_y),
            color=theme["primary"] if bold else theme["secondary"],
            fontsize=9,
            fontweight="bold" if bold else "normal",
            va="center",
            ha="left",
            zorder=4,
        )

    axes.set_xlim(
        min(p["median_s"] for p in points) * 0.82,
        max(p["median_s"] for p in points) * 1.95,
    )
    axes.xaxis.set_major_locator(LogLocator(base=10, subs=(1.0, 2.0, 3.0, 5.0), numticks=24))
    axes.xaxis.set_minor_locator(LogLocator(base=10, subs="auto", numticks=24))
    axes.xaxis.set_minor_formatter(NullFormatter())
    axes.xaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value:g}s"))
    axes.set_xlabel("1 回あたりの所要時間（秒・中央値、対数目盛）")
    axes.set_ylabel("1 回あたりの料金（USD・中央値、対数目盛）")
    # 10 の冪だけだと 1〜2 本しか目盛りが出ない。1/2/5 刻みで読めるようにする。
    axes.yaxis.set_major_locator(LogLocator(base=10, subs=(1.0, 2.0, 5.0), numticks=24))
    axes.yaxis.set_minor_locator(LogLocator(base=10, subs="auto", numticks=24))
    axes.yaxis.set_minor_formatter(NullFormatter())
    axes.yaxis.set_major_formatter(FuncFormatter(lambda value, _: f"${value:g}"))
    axes.set_title(
        "速いほど左・安いほど下。太字は「速さと安さの両方で他に負けていない」モデル",
        color=theme["secondary"],
        fontsize=10,
        loc="left",
        pad=14,
    )
    figure.suptitle(
        "校正 1 回の所要時間と料金", x=0.055, ha="left", fontsize=15, fontweight="bold"
    )
    figure.text(0.055, 0.015, meta, color=theme["muted"], fontsize=8, ha="left")
    recede_axes(axes, theme)

    figure.tight_layout(rect=(0, 0.035, 1, 0.94))
    path.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(path, facecolor=theme["surface"])
    plt.close(figure)


def draw_bars(data: list[dict], theme: dict, meta: str, path: Path) -> None:
    style(theme)
    figure, (left_axes, right_axes) = plt.subplots(
        1, 2, figsize=(11, 6.4), dpi=200, sharey=True, gridspec_kw={"width_ratios": [1.35, 1]}
    )

    # プロバイダーは色ではなく行の並びで示す。ブロック内は速い順。
    ordered = []
    for provider in PROVIDER_ORDER:
        block = [row for row in data if row["provider"] == provider]
        ordered.extend(sorted(block, key=lambda row: row["median_s"] or 1e9))
    ordered.extend(row for row in data if row["provider"] not in PROVIDER_ORDER)
    ordered.reverse()  # barh は下から積むので、表示順を上からにする

    positions = list(range(len(ordered)))
    labels = [row["name"] for row in ordered]

    # 所要時間は棒ではなく点＋範囲線。最遅の試行（PLaMo の 118 秒）と最速（Haiku の 1.7 秒）で
    # 70 倍開くので対数軸にする必要があり、対数軸の棒は「長さが値に比例しない」ため使えない。
    left_axes.set_xscale("log")
    for position, row in zip(positions, ordered):
        if row["min_s"] is None:
            continue
        left_axes.plot(
            [row["min_s"], row["max_s"]],
            [position, position],
            color=theme["muted"],
            linewidth=2,
            solid_capstyle="round",
            zorder=2,
        )
        left_axes.scatter(
            row["median_s"], position, s=90, color=theme["series"],
            edgecolors=theme["surface"], linewidths=2, zorder=3,
        )
        left_axes.text(
            row["max_s"] * 1.14,
            position,
            f"{row['median_s']:.1f}s",
            va="center",
            fontsize=8.5,
            color=theme["secondary"],
            zorder=3,
        )

    left_axes.set_yticks(positions, labels)
    left_axes.set_xlabel("所要時間（秒・対数目盛）　点＝中央値 / 線＝最小〜最大")
    left_axes.set_xlim(
        min((row["min_s"] or 1) for row in ordered) * 0.7,
        max((row["max_s"] or 1) for row in ordered) * 2.6,
    )
    left_axes.xaxis.set_major_locator(LogLocator(base=10, subs=(1.0, 2.0, 3.0, 5.0), numticks=24))
    left_axes.xaxis.set_minor_locator(LogLocator(base=10, subs="auto", numticks=24))
    left_axes.xaxis.set_minor_formatter(NullFormatter())
    left_axes.xaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value:g}s"))
    left_axes.grid(True, axis="x", color=theme["grid"], linewidth=1, zorder=0)
    left_axes.set_axisbelow(True)
    recede_axes(left_axes, theme)
    left_axes.tick_params(axis="y", labelcolor=theme["primary"], labelsize=9.5)

    right_axes.barh(
        positions,
        [row["changes"] or 0 for row in ordered],
        height=0.62,
        color=theme["series"],
        zorder=2,
    )
    for position, row in zip(positions, ordered):
        right_axes.text(
            (row["changes"] or 0) + 0.16,
            position,
            f"{row['changes']:.1f}",
            va="center",
            fontsize=8.5,
            color=theme["secondary"],
            zorder=3,
        )
    right_axes.set_xlabel("1 回あたりの提案件数（7 文章の平均）")
    right_axes.set_xlim(0, max((row["changes"] or 0) for row in ordered) * 1.24)
    right_axes.grid(True, axis="x", color=theme["grid"], linewidth=1, zorder=0)
    right_axes.set_axisbelow(True)
    recede_axes(right_axes, theme, left=False)

    # プロバイダーの区切り（色の代わりに位置で示す、その区切り線）。
    boundary = 0
    for provider in PROVIDER_ORDER:
        count = sum(1 for row in data if row["provider"] == provider)
        if count == 0:
            continue
        boundary += count
        if boundary < len(ordered):
            for axes in (left_axes, right_axes):
                axes.axhline(
                    len(ordered) - boundary - 0.5,
                    color=theme["baseline"],
                    linewidth=1,
                    zorder=1,
                )

    figure.suptitle(
        "モデル別の所要時間と提案の多さ",
        x=0.03,
        y=0.985,
        va="top",
        ha="left",
        fontsize=15,
        fontweight="bold",
    )
    figure.text(
        0.03,
        0.918,
        "行はプロバイダーごとにまとめ、区切り線の上から OpenAI / Gemini / Anthropic / PLaMo",
        color=theme["secondary"],
        fontsize=10,
        ha="left",
    )
    figure.text(0.03, 0.015, meta, color=theme["muted"], fontsize=8, ha="left")

    figure.tight_layout(rect=(0, 0.035, 1, 0.895))
    path.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(path, facecolor=theme["surface"])
    plt.close(figure)


def markdown_table(report: dict, data: list[dict]) -> str:
    protected = [text["id"] for text in report["texts"] if text["mustNotChangeCount"] > 0]
    header = (
        "| モデル | プロバイダー | 単価 入力/出力 (per 1M) | 所要時間 中央値 | 1回あたり料金 | 提案件数 | 引用保護 | 指示耐性 |\n"
        "|---|---|---|---|---:|---:|:-:|:-:|"
    )
    lines = [header]
    for row in sorted(data, key=lambda item: item["median_s"] or 1e9):
        unit = "¥" if row["currency"] == "JPY" else "$"
        price = f"{unit}{row['input_price']:g} / {unit}{row['output_price']:g}"
        elapsed = "—" if row["median_s"] is None else f"{row['median_s']:.1f} s"
        cost = "—" if row["cost"] is None else f"${row['cost']:.4f}"
        changes = "—" if row["changes"] is None else f"{row['changes']:.1f}"
        marks = []
        for text_id in protected:
            entry = next(
                (p for p in row["protection"] if p["textId"] == text_id), None
            )
            if entry is None or entry["judgedTrials"] == 0:
                marks.append("—")
            elif entry["cleanTrials"] == entry["judgedTrials"]:
                marks.append("✓")
            else:
                # 「無事故」を明記する。✗ の後ろに素の分数を置くと、数字が大きいほど
                # 悪いように読めてしまい（実際は逆）、一番効かせたい列が誤読される。
                marks.append(f"✗ 無事故 {entry['cleanTrials']}/{entry['judgedTrials']}")
        while len(marks) < 2:
            marks.append("—")
        provider = PROVIDER_LABELS.get(row["provider"], row["provider"])
        lines.append(
            f"| {row['name']} | {provider} | {price} | {elapsed} | {cost} | "
            f"{changes} | {marks[0]} | {marks[1]} |"
        )
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=None, help="ベンチマークJSON")
    parser.add_argument("--output-dir", type=Path, default=IMAGES_DIR)
    # 表は UTF-8 のファイルへ書く。Windows のコンソールは CP932 なので、
    # ✓ や ✗ を標準出力へ流すと UnicodeEncodeError で落ちる。
    parser.add_argument(
        "--markdown", type=Path, default=None, help="README 用の表を書き出すパス"
    )
    args = parser.parse_args()

    path = args.input or latest_report()
    report = load(path)
    data = rows(report)

    date = report["runStartedAt"][:10]
    fx = report.get("fxRate")
    meta = (
        f"計測 {date} / 手動用 effort / 7 文章 × {report['trialCount']} 試行 / "
        f"タイムアウト {report['timeoutSeconds']} 秒（全モデル共通）"
    )
    if fx:
        meta += f" / USD/JPY {fx['usdJpy']:g}（{fx['rateDate']}）"

    for mode, theme in THEMES.items():
        draw_scatter(data, theme, meta, args.output_dir / f"model-benchmark-scatter-{mode}.png")
        draw_bars(data, theme, meta, args.output_dir / f"model-benchmark-bars-{mode}.png")
        print(f"{args.output_dir / f'model-benchmark-scatter-{mode}.png'}")
        print(f"{args.output_dir / f'model-benchmark-bars-{mode}.png'}")

    if args.markdown is not None:
        args.markdown.parent.mkdir(parents=True, exist_ok=True)
        args.markdown.write_text(markdown_table(report, data) + "\n", encoding="utf-8")
        print(args.markdown)


if __name__ == "__main__":
    main()
