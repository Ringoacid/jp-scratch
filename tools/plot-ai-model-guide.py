#!/usr/bin/env python3
"""保存済みの校正結果から、一般向けガイドの全モデル待ち時間図を作る。APIは呼ばない。"""

from __future__ import annotations

import json
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt


ROOT = Path(__file__).resolve().parent.parent
REPORTS = [
    "model-benchmark-2026-08-06-r3.json",
    "gemini-3.7-flash-benchmark-2026-08-21-r3.json",
    "model-benchmark-2026-09-04-supplement-r3.json",
    "model-benchmark-2026-09-07-gpt-6-astra-r3.json",
    "model-benchmark-2026-09-23-manual-r3.json",
    "model-benchmark-2026-09-23-opus-5-5-manual-r3.json",
]
MODELS = {
    "gpt-6-luna": "GPT 6 Luna",
    "gpt-6-sol": "GPT 6 Sol",
    "gpt-6-astra": "GPT 6 Astra",
    "gpt-5.6-luna": "GPT 5.6 Luna",
    "gpt-5.6-terra": "GPT 5.6 Terra",
    "gpt-5.6-sol": "GPT 5.6 Sol",
    "gemini-3.1-pro-preview": "Gemini 3.1 Pro (Preview)",
    "gemini-3.6-flash": "Gemini 3.6 Flash",
    "gemini-3.7-flash": "Gemini 3.7 Flash",
    "gemini-3.8-flash": "Gemini 3.8 Flash",
    "gemini-3.1-flash-lite": "Gemini 3.1 Flash-Lite",
    "gemini-3.5-flash-lite": "Gemini 3.5 Flash Lite",
    "claude-fable-5": "Claude Fable 5",
    "claude-fable-5-1": "Claude Fable 5.1",
    "claude-opus-5": "Claude Opus 5",
    "claude-opus-5-5": "Claude Opus 5.5",
    "claude-sonnet-5": "Claude Sonnet 5",
    "claude-haiku-4-5-20251001": "Claude Haiku 4.5",
    "plamo-3.0-prime": "PLaMo 3.0 Prime",
}


def main() -> None:
    latest: dict[str, tuple[str, float]] = {}
    for filename in REPORTS:
        path = ROOT / "PromptValidation" / "results" / filename
        report = json.loads(path.read_text(encoding="utf-8"))
        if report["purpose"] != "Manual":
            raise ValueError(f"手動校正の結果ではありません: {path}")
        run_date = report["runStartedAt"]
        for result in report["summary"]:
            model_id = result["modelId"]
            median_ms = result["medianElapsedMs"]
            if median_ms is None:
                continue
            if model_id not in latest or run_date > latest[model_id][0]:
                latest[model_id] = (run_date, median_ms / 1000)

    unknown = set(latest) - set(MODELS)
    unmeasured = set(MODELS) - set(latest)
    if unknown or unmeasured:
        raise ValueError(f"モデル一覧と計測データが一致しません: {unknown=}, {unmeasured=}")

    measured = sorted(latest, key=lambda model_id: latest[model_id][1])
    labels = [MODELS[model_id] for model_id in measured]
    values = [latest[model_id][1] for model_id in measured]

    plt.rcParams["font.family"] = [
        "Yu Gothic", "Noto Sans JP", "Meiryo", "BIZ UDGothic", "MS Gothic"
    ]
    fig, ax = plt.subplots(figsize=(13.4, 10.8), dpi=160)
    fig.patch.set_facecolor("#ffffff")
    ax.set_facecolor("#ffffff")
    y = list(range(len(labels)))
    colors = ["#1765b2" if model_id == "gpt-6-luna" else "#7c91a6" for model_id in measured]
    ax.barh(y, values, height=0.64, color=colors)
    for row, value in enumerate(values):
        ax.text(value + 0.35, row, f"{value:.1f}秒", va="center", ha="left", fontsize=10.5, color="#263746")

    ax.set_yticks(y, labels, fontsize=11)
    ax.invert_yaxis()
    ax.set_xlim(0, 44)
    ax.set_xticks(range(0, 41, 5))
    ax.tick_params(axis="y", length=0, pad=9)
    ax.tick_params(axis="x", colors="#4d5965", labelsize=10)
    ax.grid(axis="x", color="#d9e0e6", linewidth=0.8)
    ax.set_axisbelow(True)
    for spine in ax.spines.values():
        spine.set_visible(False)
    ax.set_xlabel("校正1回の待ち時間（秒）", fontsize=11, labelpad=12)
    fig.suptitle("AI校正の待ち時間：固定19モデル", x=0.05, y=0.982, ha="left", fontsize=17, fontweight="bold")
    fig.text(0.05, 0.951, "手動校正・7文章を各3回試した中央値。短いほど待ち時間が少ない。", ha="left", fontsize=11, color="#4d5965")
    fig.text(0.05, 0.018, "計測日：2026年8月6日～9月23日。別の日・時間帯の結果をまとめた目安です。", ha="left", fontsize=9.5, color="#59636d")
    fig.subplots_adjust(left=0.31, right=0.95, top=0.92, bottom=0.09)

    output = ROOT / "docs" / "images" / "ai-model-guide-speed.png"
    fig.savefig(output, dpi=160, facecolor="#ffffff")
    plt.close(fig)
    print(output)


if __name__ == "__main__":
    main()
