#!/usr/bin/env python3
"""a11y_audit.py — Lakskongen token accessibility audit (WCAG contrast + CVD separation).

Reads the design-token SSOT (godot/theme/tokens.json, mirrored from
design/tokens.json) and checks:

  1. WCAG 2.1 contrast ratios for the pairs the UI ACTUALLY uses (curated from
     the code: badges use lightened fills + dark text; toasts use darkened
     fills + white text; buttons use primary + white text).
     AA thresholds: >=4.5 normal text, >=3.0 large text / non-text UI pieces.
  2. Colour-vision-deficiency separation: status fills pairwise CIELAB delta-E
     under simulated protanopia / deuteranopia / tritanopia (Viénot 1999 for
     protan/deutan, Machado 2009 approximation for tritan; severity 100).
     Threshold delta-E >= 15 keeps a pair distinguishable by colour alone;
     sub-threshold pairs are WARN (dual encoding: emoji + text carries meaning).

Exit 0 when all hard checks pass; 1 on a hard failure (CI-adoptable later).

Usage:  python3.12 tools/a11y_audit.py [--json docs/run/evidence/a11y_audit.json]
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
TOKENS = REPO / "godot/theme/tokens.json"

CVD_MIN_DELTA_E = 15.0

# --- colour helpers (no third-party deps) ------------------------------------

def hex_rgb(h: str) -> tuple[float, float, float]:
    h = h.lstrip("#")
    return tuple(int(h[i : i + 2], 16) / 255.0 for i in (0, 2, 4))  # type: ignore[return-value]


def rgb_hex(rgb: tuple[float, float, float]) -> str:
    return "#" + "".join(f"{round(max(0.0, min(1.0, c)) * 255):02X}" for c in rgb)


def rel_luminance(rgb: tuple[float, float, float]) -> float:
    def lin(c: float) -> float:
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    r, g, b = (lin(c) for c in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def wcag_ratio(fg: str, bg: str) -> float:
    l1, l2 = rel_luminance(hex_rgb(fg)), rel_luminance(hex_rgb(bg))
    hi, lo = max(l1, l2), min(l1, l2)
    return (hi + 0.05) / (lo + 0.05)


def mix(c: tuple[float, float, float], other: tuple[float, float, float], t: float):
    return tuple(a + (b - a) * t for a, b in zip(c, other))


def lightened(h: str, amount: float) -> str:
    return rgb_hex(mix(hex_rgb(h), (1.0, 1.0, 1.0), amount))


def darkened(h: str, amount: float) -> str:
    return rgb_hex(mix(hex_rgb(h), (0.0, 0.0, 0.0), amount))


def _srgb_to_lab(rgb: tuple[float, float, float]) -> tuple[float, float, float]:
    def lin(c: float) -> float:
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    r, g, b = (lin(c) for c in rgb)
    x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b
    y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b
    z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b
    xn, yn, zn = 0.95047, 1.0, 1.08883

    def f(t: float) -> float:
        return t ** (1 / 3) if t > 0.008856 else (7.787 * t + 16 / 116)

    fx, fy, fz = f(x / xn), f(y / yn), f(z / zn)
    return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz))


def delta_e76(a_lab: tuple[float, float, float], b_lab: tuple[float, float, float]) -> float:
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a_lab, b_lab)))


# Viénot/Brettel-style severity-100 approximations in linear sRGB.
CVD_MATRICES = {
    "protanopia": ((0.567, 0.433, 0.0), (0.558, 0.442, 0.0), (0.0, 0.242, 0.758)),
    "deuteranopia": ((0.625, 0.375, 0.0), (0.700, 0.300, 0.0), (0.0, 0.300, 0.700)),
    "tritanopia": ((0.950, 0.050, 0.0), (0.0, 0.433, 0.567), (0.0, 0.475, 0.525)),
}


def cvd_lab(h: str, kind: str) -> tuple[float, float, float]:
    r, g, b = hex_rgb(h)
    m = CVD_MATRICES[kind]
    out = tuple(sum(m[i][j] * (r, g, b)[j] for j in range(3)) for i in range(3))
    return _srgb_to_lab(out)  # type: ignore[arg-type]


def cvd_delta_e(a: str, b: str) -> dict[str, float]:
    return {kind: round(delta_e76(cvd_lab(a, kind), cvd_lab(b, kind)), 1) for kind in CVD_MATRICES}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", help="write a machine-readable report here")
    args = ap.parse_args()

    tokens = json.loads(TOKENS.read_text(encoding="utf-8"))
    c: dict[str, str] = tokens["colors"]

    # Pairs the UI actually renders (curated from the panel code; see commit log).
    toast_darken = 0.62  # ToastNotification.TOAST_SURFACE_DARKEN
    pairs: list[tuple[str, str, str, float, str]] = [
        (c["text"], c["bg"], "body text on app background", 4.5, "text"),
        (c["text"], c["surface"], "body text on cards", 4.5, "text"),
        (c["text-secondary"], c["surface"], "secondary text on cards", 4.5, "text"),
        (c["text-muted"], c["surface"], "muted/meta text on cards", 3.0, "large"),
        (c["text-inverse"], c["primary"], "text on primary buttons", 4.5, "text"),
        (c["danger-dark"], c["surface"], "danger badge text (NotificationCenter)", 4.5, "text"),
        ("#9A444C", lightened(c["danger"], 0.78), "critical priority badge (AttentionInbox)", 4.5, "text"),
        ("#7A6118", lightened(c["warning"], 0.78), "high priority badge (AttentionInbox)", 4.5, "text"),
        ("#33526F", lightened("#81A1C1", 0.78), "medium priority badge (AttentionInbox)", 4.5, "text"),
        (c["text-secondary"], lightened(c["text-muted"], 0.78), "low priority badge (AttentionInbox)", 4.5, "text"),
        ("#7A6118", c["surface"], "warning badge text (NotificationCenter)", 4.5, "text"),
        (c["primary"], c["surface"], "info badge text / headings", 4.5, "text"),
        (c["text-inverse"], darkened(c["danger"], toast_darken), "toast critical: white on darkened fill", 4.5, "text"),
        (c["text-inverse"], darkened(c["warning"], toast_darken), "toast warning: white on darkened fill", 4.5, "text"),
        (c["text-inverse"], darkened(c["info"], toast_darken), "toast info: white on darkened fill", 4.5, "text"),
        (c["text-inverse"], darkened(c["success"], toast_darken), "toast success: white on darkened fill", 4.5, "text"),
        (c["danger"], c["surface"], "critical accent rail/icon (non-text)", 3.0, "nontext"),
    ]

    hard_fail = False
    report: dict = {"token_file": str(TOKENS.relative_to(REPO)), "contrast": [], "cvd": []}

    print("=== WCAG contrast — real usage pairs (%s) ===" % TOKENS.relative_to(REPO))
    for fg, bg, label, minimum, kind in pairs:
        ratio = wcag_ratio(fg, bg)
        ok = ratio >= minimum
        hard_fail |= not ok
        print(f"  {'PASS' if ok else 'FAIL'}  {ratio:5.2f} (min {minimum}, {kind})  {label}  [{fg} on {bg}]")
        report["contrast"].append({"fg": fg, "bg": bg, "label": label, "ratio": round(ratio, 2), "min": minimum, "pass": ok})

    print("\n=== Status-colour separation under CVD (delta-E, min %.0f; WARN = dual-encoded) ===" % CVD_MIN_DELTA_E)
    status = ["success", "warning", "danger", "info", "accent"]
    for i, a in enumerate(status):
        for b in status[i + 1:]:
            if a not in c or b not in c:
                continue
            res = cvd_delta_e(c[a], c[b])
            worst = min(res.values())
            ok = worst >= CVD_MIN_DELTA_E
            print(f"  {'PASS' if ok else 'WARN'}  worst {worst:5.1f}  {a} vs {b}  " +
                  " ".join(f"{k[:6]}={v:.1f}" for k, v in res.items()))
            report["cvd"].append({"a": a, "b": b, "worst": worst, **res, "pass": ok})

    if args.json:
        out = Path(args.json)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"\nJSON report: {out}")

    print("\nRESULT:", "FAIL (contrast)" if hard_fail else "PASS (contrast; CVD WARNs are dual-encoded by policy)")
    return 1 if hard_fail else 0


if __name__ == "__main__":
    sys.exit(main())
