#!/usr/bin/env python3
"""P0-04 performance report and gate.

Reads a PerfHarness JSON (godot/scripts/tools/PerfHarness.gd), prints a
markdown table per segment and variant (the median over repeats of each
repeat's p50 / p95 / p99 frame time, and the delta against the first
variant, which is the in-run baseline), and evaluates the gate: the p95
frame time of every gated segment must stay within the budget (16.7 ms,
60 fps) on the gated variant.

The gate is warn-only until the Phase 1 exit (roadmap P0-04): it prints
PERF_GATE WARN lines and exits 0, unless --strict is given (or
PERF_GATE_STRICT=1), when an over-budget segment exits 1.

Usage:
  python3 tools/perf_report.py perf_smoke.json [--markdown out.md]
      [--variant high] [--segments farm,fjord,coast] [--strict]
"""

from __future__ import annotations

import argparse
import json
import os
import statistics
import sys
from pathlib import Path

BUDGET_MS = 16.7
GATED_SEGMENTS = ("farm", "fjord", "coast")


def load(path: str | Path) -> dict:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def summarize(report: dict) -> dict:
    """{segment: {variant: {"p50", "p95", "p99", "repeats", "draw_calls", ...}}}.

    Each value is the median over the repeats of that run's statistic, so one
    disturbed block (another process on the GPU) does not decide the result.
    Variants keep their first-seen order.
    """
    out: dict = {}
    for run in report.get("runs", []):
        seg = out.setdefault(run["segment"], {})
        var = seg.setdefault(run["variant"], {"_runs": []})
        var["_runs"].append(run)
    for seg in out.values():
        for name, var in seg.items():
            runs = var.pop("_runs")
            for key in ("p10", "p50", "p95", "p99"):
                var[key] = round(statistics.median(r["frame_ms"].get(key, 0.0) for r in runs), 2)
            # The least disturbed repeat: on a shared machine the closest to
            # the frame's own cost (reported beside the medians, never gated).
            best = min(runs, key=lambda r: r["frame_ms"]["p50"])
            var["best_p50"] = best["frame_ms"]["p50"]
            var["best_p95"] = best["frame_ms"]["p95"]
            var["repeats"] = len(runs)
            var["frames"] = sum(r["frames"] for r in runs)
            var["draw_calls"] = statistics.median(r.get("draw_calls", 0) for r in runs)
            var["shadow_draw_calls"] = statistics.median(r.get("shadow_draw_calls", 0) for r in runs)
            var["script_ms"] = round(statistics.median(r.get("script_phase_ms", {}).get("p50", 0.0) for r in runs), 2)
            var["vram_mb"] = max(r.get("vram_mb", 0.0) for r in runs)
    return out


def paired(report: dict) -> dict:
    """{segment: {variant: {"ratio_p10", "ratio_p50", "pairs"}}} for a --paired run.

    Each variant block is compared with the reference block run just before
    it (block index - 1, same segment and repeat): the ratio of their frame
    times, the median over the repeats. A background load that comes and goes
    over tens of seconds hits both blocks of a pair alike, so the ratio holds
    where the absolute times do not.
    """
    runs = report.get("runs", [])
    by_key = {(r["segment"], r["repeat"], r.get("block", -1)): r for r in runs}
    ratios: dict = {}
    for r in runs:
        block = r.get("block", -1)
        if block < 1 or block % 2 == 0:
            continue
        ref = by_key.get((r["segment"], r["repeat"], block - 1))
        if ref is None or ref["frame_ms"]["p50"] <= 0 or ref["frame_ms"].get("p10", 0) <= 0:
            continue
        entry = ratios.setdefault(r["segment"], {}).setdefault(r["variant"], {"p10": [], "p50": []})
        entry["p10"].append(r["frame_ms"]["p10"] / ref["frame_ms"]["p10"])
        entry["p50"].append(r["frame_ms"]["p50"] / ref["frame_ms"]["p50"])
    out: dict = {}
    for seg, variants in ratios.items():
        for name, e in variants.items():
            out.setdefault(seg, {})[name] = {
                "ratio_p10": round(statistics.median(e["p10"]), 3),
                "ratio_p50": round(statistics.median(e["p50"]), 3),
                "pairs": len(e["p10"]),
            }
    return out


def gate(summary: dict, variant: str | None, segments=GATED_SEGMENTS, budget=BUDGET_MS) -> list[str]:
    """Over-budget findings: one line per gated segment whose p95 exceeds the budget."""
    findings = []
    for seg in segments:
        variants = summary.get(seg)
        if not variants:
            findings.append(f"{seg}: not measured")
            continue
        name = variant if variant is not None else next(iter(variants))
        stats = variants.get(name)
        if stats is None:
            findings.append(f"{seg}: variant '{name}' not measured")
            continue
        if stats["p95"] > budget:
            findings.append(f"{seg}: p95 {stats['p95']:.2f} ms > {budget} ms (variant {name})")
    return findings


def markdown(report: dict, summary: dict) -> str:
    res = report.get("resolution", ["?", "?"])
    lines = [
        f"### {report.get('label', '')} — {res[0]}x{res[1]}, {report.get('adapter', '')}, "
        f"{report.get('godot', '')}, {report.get('timestamp', '')}",
        "",
        "| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |",
        "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for seg, variants in summary.items():
        base = next(iter(variants.values()))
        for name, s in variants.items():
            delta = s["p50"] - base["p50"]
            delta10 = s["p10"] - base["p10"]
            lines.append(
                f"| {seg} | {name} | {s['p10']:.2f} | {s['p50']:.2f} | {s['p95']:.2f} | {s['p99']:.2f} | "
                f"{s['best_p50']:.2f} / {s['best_p95']:.2f} | "
                f"{delta10:+.2f} | {delta:+.2f} | {s['draw_calls']:.0f} | {s['shadow_draw_calls']:.0f} | "
                f"{s['script_ms']:.2f} | {s['repeats']} |"
            )
    pairs = paired(report)
    if pairs:
        lines += [
            "",
            "Paired against the reference block run just before each variant (median ratio over repeats):",
            "",
            "| Segment | Variant | p10 ratio | p50 ratio | pairs |",
            "|---|---|---:|---:|---:|",
        ]
        for seg, variants in pairs.items():
            for name, p in variants.items():
                lines.append(f"| {seg} | {name} | {p['ratio_p10']:.3f} | {p['ratio_p50']:.3f} | {p['pairs']} |")
    boot = report.get("boot") or {}
    if boot:
        spikes = boot.get("spikes_over_50_ms", [])
        lines += [
            "",
            f"Boot: terrain ready at frame {boot.get('terrain_ready_frame')}, "
            f"max frame after the first 10: {boot.get('max_frame_after_first_10')} ms, "
            f"frames over 50 ms: {len(spikes)} "
            f"({', '.join(str(s['ms']) for s in spikes)})",
        ]
    return "\n".join(lines) + "\n"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("json")
    ap.add_argument("--markdown", help="write the table here as well")
    ap.add_argument("--variant", help="variant the gate judges (default: the first)")
    ap.add_argument("--segments", default=",".join(GATED_SEGMENTS))
    ap.add_argument("--budget", type=float, default=BUDGET_MS)
    ap.add_argument("--strict", action="store_true", default=os.environ.get("PERF_GATE_STRICT") == "1")
    args = ap.parse_args(argv)

    report = load(args.json)
    summary = summarize(report)
    table = markdown(report, summary)
    print(table)
    if args.markdown:
        Path(args.markdown).write_text(table, encoding="utf-8")
    findings = gate(summary, args.variant, tuple(s for s in args.segments.split(",") if s), args.budget)
    if not findings:
        print(f"PERF_GATE PASS: p95 <= {args.budget} ms on {args.segments}")
        return 0
    level = "FAIL" if args.strict else "WARN"
    for f in findings:
        print(f"PERF_GATE {level} {f}")
    return 1 if args.strict else 0


if __name__ == "__main__":
    sys.exit(main())
