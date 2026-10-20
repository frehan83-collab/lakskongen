#!/usr/bin/env python3
"""
Lakskongen — Steam store capsule generator ("Nordic Illustrated").

Builds a coherent, flat-illustrated Nordic fjord scene with PIL drawing
primitives and burns in the LAKSKONGEN wordmark + tagline using the bundled
Inter fonts. Deterministic: no RNG, no timestamps, no external tools.

Outputs (default: screenshots/steam_store/capsules/):
    main.png             616 x 353
    header.png           460 x 215
    small.png            462 x 174
    library_header.png   460 x 215
    library_hero.png     3840 x 1240  (art rendered small, upscaled, text crisp)
    page_background.png  1438 x 810
    library_logo.png     1280 x 720   (transparent wordmark / safe area)
    preview_all_capsules.png          (human review contact sheet)

Usage:
    python3 tools/gen_store_art.py
    python3 tools/gen_store_art.py --variant dawn
    python3 tools/gen_store_art.py --out /tmp/caps --no-preview
"""

from __future__ import annotations

import argparse
import math
from functools import lru_cache
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

REPO_ROOT = Path(__file__).resolve().parent.parent
FONT_DIR = REPO_ROOT / "godot" / "assets" / "fonts"
DEFAULT_OUT = REPO_ROOT / "screenshots" / "steam_store" / "capsules"

FONT_BOLD = FONT_DIR / "Inter-Bold.otf"
FONT_SEMI = FONT_DIR / "Inter-SemiBold.otf"
FONT_REG = FONT_DIR / "Inter-Regular.otf"

TITLE = "LAKSKONGEN"
TAGLINE = "Build Your Salmon Empire"

PALETTE = {
    "primary": "#0A4D68",
    "secondary": "#88C0D0",
    "accent": "#5E81AC",
    "bg": "#ECEFF4",
    "text": "#2E3440",
    "water_light": "#5CA8B8",
    "water_mid": "#2A6B7C",
    "water_deep": "#0F3A4A",
    "aurora": "#A3BE8C",
    "salmon": "#E8825C",
    "salmon_dark": "#C4613A",
}


def rgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))  # type: ignore[return-value]


@lru_cache(maxsize=256)
def load_font(path: Path, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(path), size)


_DUMMY_IMG = Image.new("RGB", (1, 1))
_DUMMY_DRAW = ImageDraw.Draw(_DUMMY_IMG)


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def mix(c1: tuple[int, int, int], c2: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return (int(lerp(c1[0], c2[0], t)), int(lerp(c1[1], c2[1], t)), int(lerp(c1[2], c2[2], t)))


def sample_stops(stops, t: float):
    if t <= stops[0][0]:
        return stops[0][1]
    if t >= stops[-1][0]:
        return stops[-1][1]
    for i in range(len(stops) - 1):
        t0, c0 = stops[i]
        t1, c1 = stops[i + 1]
        if t0 <= t <= t1:
            local = 0.0 if t1 == t0 else (t - t0) / (t1 - t0)
            return mix(c0, c1, local)
    return stops[-1][1]


def vgradient(img: Image.Image, W: int, y0: float, y1: float, stops) -> None:
    d = ImageDraw.Draw(img)
    y_start = max(0, int(y0))
    y_end = min(img.height, int(y1))
    span = max(1, y_end - y_start)
    prepared = [(t, rgb(c)) for t, c in stops]
    for y in range(y_start, y_end):
        color = sample_stops(prepared, (y - y_start) / span)
        d.line([(0, y), (W, y)], fill=color)


def composite(base: Image.Image, layer: Image.Image) -> Image.Image:
    preserve_alpha = base.mode == "RGBA"
    base = base.convert("RGBA")
    out = Image.alpha_composite(base, layer)
    return out if preserve_alpha else out.convert("RGB")


def new_layer(W: int, H: int) -> tuple[Image.Image, ImageDraw.ImageDraw]:
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    return layer, ImageDraw.Draw(layer)


# --------------------------------------------------------------------------
# Motif primitives
# --------------------------------------------------------------------------

def ridge_profile(n: float, harmonics) -> float:
    """Deterministic jagged ridge in [0, 1] from summed sines."""
    v = 0.0
    for amp, freq, phase in harmonics:
        v += amp * math.sin(2 * math.pi * freq * n + phase)
    return max(0.0, min(1.0, (v + 1.0) * 0.5))


def draw_mountain(img: Image.Image, W: int, H: int, horizon: float, layer) -> None:
    d = ImageDraw.Draw(img)
    steps = max(48, W // 6)
    height = layer["height"] * H
    points = []
    for i in range(steps + 1):
        n = i / steps
        y = horizon - height * ridge_profile(n, layer["harmonics"])
        points.append((n * W, y))
    poly = points + [(W, H), (0, H)]
    d.polygon(poly, fill=rgb(layer["color"]))

    if layer.get("snow"):
        snow_c = rgb(PALETTE["bg"])
        cap_w = layer.get("snow_w", 0.028) * H
        cap_d = layer.get("snow_d", 0.036) * H
        for i in range(1, steps):
            x_prev, y_prev = points[i - 1]
            x_cur, y_cur = points[i]
            x_next, y_next = points[i + 1]
            if y_cur <= y_prev and y_cur < y_next and y_cur < horizon - height * 0.72:
                d.polygon(
                    [(x_cur, y_cur), (x_cur - cap_w, y_cur + cap_d), (x_cur + cap_w, y_cur + cap_d)],
                    fill=snow_c,
                )


def draw_aurora(img: Image.Image, W: int, H: int, horizon: float) -> Image.Image:
    layer, d = new_layer(W, H)
    col = rgb(PALETTE["aurora"])
    ribbons = [
        (0.07, 0.05, 0.022, 0.6, 64),
        (0.13, 0.07, 0.030, 1.7, 82),
        (0.20, 0.045, 0.018, 2.6, 54),
    ]
    steps = 72
    for ytop, amp, thick, phase, alpha in ribbons:
        top, bot = [], []
        for i in range(steps + 1):
            n = i / steps
            yy = (
                H * ytop
                + H * amp * math.sin(2 * math.pi * n * 1.4 + phase)
                + H * amp * 0.4 * math.sin(2 * math.pi * n * 3.1 + phase * 1.7)
            )
            top.append((n * W, yy))
            bot.append((n * W, yy + H * thick))
        d.polygon(top + bot[::-1], fill=col + (alpha,))
    layer = layer.filter(ImageFilter.GaussianBlur(max(1.0, H * 0.012)))
    return composite(img, layer)


def draw_sun(img: Image.Image, W: int, H: int, sun) -> Image.Image:
    cx, cy = int(sun["x"] * W), int(sun["y"] * H)
    r = sun["r"] * H
    glow = rgb(sun["glow"])
    layer, d = new_layer(W, H)
    d.ellipse([cx - r * 2.4, cy - r * 2.4, cx + r * 2.4, cy + r * 2.4], fill=glow + (135,))
    layer = layer.filter(ImageFilter.GaussianBlur(max(1.0, r * 0.85)))
    img = composite(img, layer)

    core = rgb(sun["core"])
    core_layer, cd = new_layer(W, H)
    cd.ellipse([cx - r * 0.85, cy - r * 0.85, cx + r * 0.85, cy + r * 0.85], fill=core + (235,))
    core_layer = core_layer.filter(ImageFilter.GaussianBlur(max(1.0, r * 0.12)))
    return composite(img, core_layer)


def draw_reflection(img: Image.Image, W: int, H: int, horizon_y: float, sun) -> Image.Image:
    layer, d = new_layer(W, H)
    col = rgb(sun["glow"])
    cx = sun["x"] * W
    top = horizon_y
    bottom = H * 0.94
    span = max(1.0, bottom - top)
    dashes = 16
    for i in range(dashes):
        t = i / (dashes - 1)
        y = top + span * t
        width = W * (0.012 + 0.055 * t) * (1.0 - 0.35 * math.sin(t * 9.0))
        alpha = int(70 * (1.0 - t) + 8)
        cx_off = cx + W * 0.012 * math.sin(t * 12.0)
        d.rectangle(
            [cx_off - width / 2, y, cx_off + width / 2, y + max(1.0, H * 0.006)],
            fill=col + (max(0, alpha),),
        )
    return composite(img, layer)


def draw_scrim(img: Image.Image, W: int, H: int, strength: int = 200) -> Image.Image:
    layer, d = new_layer(W, H)
    start = int(H * 0.44)
    span = max(1, H - start)
    for y in range(start, H):
        t = (y - start) / span
        alpha = int(strength * (t ** 1.5))
        d.line([(0, y), (W, y)], fill=(8, 26, 36, alpha))
    return composite(img, layer)


def draw_pen(d: ImageDraw.ImageDraw, cx: float, cy: float, rx: float, ry: float) -> None:
    ring = rgb(PALETTE["bg"])
    mesh = rgb(PALETTE["secondary"])
    accent = rgb(PALETTE["accent"])
    lw = max(1, int(rx * 0.10))
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], outline=ring, width=lw)
    d.ellipse(
        [cx - rx * 0.86, cy - ry * 0.86, cx + rx * 0.86, cy + ry * 0.86],
        outline=mesh,
        width=max(1, lw // 2),
    )
    for i in range(12):
        a = 2 * math.pi * i / 12
        d.line(
            [(cx, cy), (cx + rx * 0.86 * math.cos(a), cy + ry * 0.86 * math.sin(a))],
            fill=mesh,
            width=1,
        )
    for f in (0.30, 0.56):
        d.ellipse([cx - rx * f, cy - ry * f, cx + rx * f, cy + ry * f], outline=mesh, width=1)
    d.ellipse(
        [cx - rx * 0.09, cy - ry * 0.09, cx + rx * 0.09, cy + ry * 0.09],
        fill=accent,
    )


def draw_fish(d: ImageDraw.ImageDraw, x: float, y: float, size: float, direction: int = 1) -> None:
    body = rgb(PALETTE["salmon"])
    fin = rgb(PALETTE["salmon_dark"])
    eye = rgb(PALETTE["text"])
    body_rx, body_ry = size, size * 0.46
    tail_x = x - body_rx * direction
    d.polygon(
        [
            (tail_x, y),
            (tail_x - direction * size * 0.75, y - size * 0.5),
            (tail_x - direction * size * 0.75, y + size * 0.5),
        ],
        fill=fin,
    )
    d.ellipse([x - body_rx, y - body_ry, x + body_rx, y + body_ry], fill=body)
    ex = x + body_rx * 0.48 * direction
    ey = y - body_ry * 0.22
    er = max(1.0, size * 0.10)
    d.ellipse([ex - er, ey - er, ex + er, ey + er], fill=eye)


# --------------------------------------------------------------------------
# Scene + wordmark
# --------------------------------------------------------------------------

def render_scene(W: int, H: int, variant: dict) -> Image.Image:
    img = Image.new("RGB", (W, H), rgb(variant["sky"][0][1]))
    horizon_y = H * variant["horizon"]

    vgradient(img, W, 0, horizon_y, variant["sky"])
    img = draw_sun(img, W, H, variant["sun"])
    if variant.get("aurora"):
        img = draw_aurora(img, W, H, horizon_y)

    for layer in variant["mountains"]:
        draw_mountain(img, W, H, horizon_y, layer)

    vgradient(img, W, horizon_y, H, variant["water"])
    img = draw_reflection(img, W, H, horizon_y, variant["sun"])

    d = ImageDraw.Draw(img)
    for pen in variant.get("pens", []):
        draw_pen(d, pen["x"] * W, pen["y"] * H, pen["r"] * H, pen["r"] * H * 0.42)
    for fish in variant.get("fish", []):
        draw_fish(d, fish["x"] * W, fish["y"] * H, fish["s"] * H, fish.get("dir", 1))

    img = draw_scrim(img, W, H, variant.get("scrim", 200))
    return img


def tracked_len(text: str, font: ImageFont.FreeTypeFont, tracking: float) -> float:
    return sum(font.getlength(ch) for ch in text) + tracking * max(0, len(text) - 1)


def fit_tracked(text: str, font_path: Path, target_w: float, tracking_ratio: float):
    lo, hi = 6, 700
    best = None
    for _ in range(26):
        mid = (lo + hi) // 2
        font = load_font(font_path, mid)
        tracking = mid * tracking_ratio
        width = tracked_len(text, font, tracking)
        if width <= target_w:
            best = (font, mid, tracking, width)
            lo = mid + 1
        else:
            hi = mid - 1
    if best is None:
        font = load_font(font_path, 6)
        best = (font, 6, 6 * tracking_ratio, tracked_len(text, font, 6 * tracking_ratio))
    return best


def draw_tracked(draw, x: float, y: float, text: str, font, tracking: float, fill) -> None:
    cursor = x
    for ch in text:
        draw.text((cursor, y), ch, font=font, fill=fill)
        cursor += font.getlength(ch) + tracking


def text_height(font, text: str) -> float:
    bbox = font.getbbox(text)
    return bbox[3] - bbox[1]


def draw_wordmark(img: Image.Image, W: int, H: int, layout: dict, variant: dict,
                  with_tagline: bool = True) -> Image.Image:
    d = ImageDraw.Draw(img)

    title_font, tsize, ttrack, twidth = fit_tracked(
        TITLE, FONT_BOLD, layout["title"] * W, layout.get("title_tracking", 0.10)
    )
    th = text_height(title_font, TITLE)

    tag_font = tsize_f = None
    tag_track = 0.0
    tag_w = 0.0
    tag_h = 0.0
    if with_tagline and layout.get("tagline"):
        tag_font, tsize_f, tag_track, tag_w = fit_tracked(
            TAGLINE, FONT_SEMI, layout["tagline"] * W, 0.02
        )
        tag_h = text_height(tag_font, TAGLINE)

    gap = tsize * 0.42
    rule_h = max(2, int(tsize * 0.06))
    block_h = th + gap + rule_h
    if with_tagline and layout.get("tagline"):
        block_h += gap * 0.9 + tag_h

    has_tag = bool(with_tagline and layout.get("tagline"))
    if layout["mode"] == "center":
        title_x = (W - twidth) / 2
        tag_x = (W - tag_w) / 2
        top = H * layout["center_y"] - block_h / 2
    else:
        title_x = layout["margin"] * W
        tag_x = title_x
        top = H * (1 - layout["bottom"]) - block_h

    rule_y = top + th + gap
    rule_w = min(twidth * 0.34, tsize * 3.4)
    rule_x = (W - rule_w) / 2 if layout["mode"] == "center" else title_x
    tag_y = rule_y + rule_h + gap * 0.9

    word = rgb(PALETTE["bg"])
    shadow = (8, 26, 36, 150)

    shadow_layer, sd = new_layer(W, H)
    off = max(1, int(tsize * 0.05))
    draw_tracked(sd, title_x + off, top + off, TITLE, title_font, ttrack, shadow)
    if has_tag:
        draw_tracked(sd, tag_x + off, tag_y + off, TAGLINE, tag_font, tag_track, shadow)
    img = composite(img, shadow_layer)
    d = ImageDraw.Draw(img)

    draw_tracked(d, title_x, top, TITLE, title_font, ttrack, word)
    d.rectangle([rule_x, rule_y, rule_x + rule_w, rule_y + rule_h],
                fill=rgb(PALETTE["aurora"]))
    if has_tag:
        draw_tracked(d, tag_x, tag_y, TAGLINE, tag_font, tag_track, rgb(PALETTE["secondary"]))

    return img


# --------------------------------------------------------------------------
# Variants — 3 Nordic motif treatments; one is emitted as the cohesive set.
# --------------------------------------------------------------------------

VARIANTS = {
    "aurora": {
        "label": "Aurora fjord",
        "horizon": 0.55,
        "sky": [(0.0, "#0F3A4A"), (0.62, "#2A6B7C"), (1.0, "#88C0D0")],
        "water": [(0.0, "#5CA8B8"), (0.45, "#2A6B7C"), (1.0, "#0F3A4A")],
        "aurora": True,
        "sun": {"x": 0.75, "y": 0.40, "r": 0.050, "core": "#ECEFF4", "glow": "#88C0D0"},
        "mountains": [
            {"height": 0.34, "color": "#88C0D0", "snow": True,
             "harmonics": [(0.55, 1.0, 0.3), (0.28, 2.3, 1.7), (0.14, 4.1, 0.9), (0.07, 7.7, 2.1)]},
            {"height": 0.24, "color": "#2A6B7C",
             "harmonics": [(0.50, 1.3, 2.0), (0.30, 2.9, 0.4), (0.16, 5.5, 1.2)]},
            {"height": 0.15, "color": "#0A4D68",
             "harmonics": [(0.50, 0.9, 1.1), (0.32, 2.1, 2.6), (0.18, 4.3, 0.2)]},
        ],
        "pens": [{"x": 0.63, "y": 0.70, "r": 0.105}, {"x": 0.80, "y": 0.80, "r": 0.080}],
        "fish": [{"x": 0.50, "y": 0.72, "s": 0.014}, {"x": 0.90, "y": 0.68, "s": 0.011, "dir": -1}],
        "scrim": 200,
    },
    "dawn": {
        "label": "Fjord dawn",
        "horizon": 0.58,
        "sky": [(0.0, "#0A4D68"), (0.55, "#5E81AC"), (1.0, "#ECEFF4")],
        "water": [(0.0, "#88C0D0"), (0.45, "#5CA8B8"), (1.0, "#2A6B7C")],
        "aurora": False,
        "sun": {"x": 0.30, "y": 0.34, "r": 0.075, "core": "#ECEFF4", "glow": "#A3BE8C"},
        "mountains": [
            {"height": 0.36, "color": "#5E81AC", "snow": True,
             "harmonics": [(0.55, 1.0, 0.3), (0.28, 2.3, 1.7), (0.14, 4.1, 0.9), (0.07, 7.7, 2.1)]},
            {"height": 0.25, "color": "#2A6B7C",
             "harmonics": [(0.50, 1.3, 2.0), (0.30, 2.9, 0.4), (0.16, 5.5, 1.2)]},
            {"height": 0.16, "color": "#0A4D68",
             "harmonics": [(0.50, 0.9, 1.1), (0.32, 2.1, 2.6), (0.18, 4.3, 0.2)]},
        ],
        "pens": [{"x": 0.66, "y": 0.72, "r": 0.105}, {"x": 0.84, "y": 0.82, "r": 0.078}],
        "fish": [{"x": 0.52, "y": 0.74, "s": 0.014}],
        "scrim": 175,
    },
    "midnight_sun": {
        "label": "Midnight sun",
        "horizon": 0.54,
        "sky": [(0.0, "#5E81AC"), (0.55, "#88C0D0"), (1.0, "#ECEFF4")],
        "water": [(0.0, "#5CA8B8"), (0.50, "#2A6B7C"), (1.0, "#0F3A4A")],
        "aurora": False,
        "sun": {"x": 0.70, "y": 0.36, "r": 0.090, "core": "#ECEFF4", "glow": "#E8825C"},
        "mountains": [
            {"height": 0.32, "color": "#88C0D0", "snow": True,
             "harmonics": [(0.55, 1.0, 0.3), (0.28, 2.3, 1.7), (0.14, 4.1, 0.9), (0.07, 7.7, 2.1)]},
            {"height": 0.23, "color": "#5E81AC",
             "harmonics": [(0.50, 1.3, 2.0), (0.30, 2.9, 0.4), (0.16, 5.5, 1.2)]},
            {"height": 0.15, "color": "#0A4D68",
             "harmonics": [(0.50, 0.9, 1.1), (0.32, 2.1, 2.6), (0.18, 4.3, 0.2)]},
        ],
        "pens": [{"x": 0.60, "y": 0.70, "r": 0.11}, {"x": 0.78, "y": 0.82, "r": 0.082}],
        "fish": [{"x": 0.46, "y": 0.73, "s": 0.014}, {"x": 0.90, "y": 0.70, "s": 0.012, "dir": -1}],
        "scrim": 190,
    },
}

CAPSULES = {
    "main": {
        "size": (616, 353),
        "layout": {"mode": "left", "title": 0.70, "tagline": 0.44, "margin": 0.065, "bottom": 0.11},
    },
    "header": {
        "size": (460, 215),
        "layout": {"mode": "left", "title": 0.70, "tagline": 0.48, "margin": 0.065, "bottom": 0.12},
    },
    "library_header": {
        "size": (460, 215),
        "layout": {"mode": "left", "title": 0.70, "tagline": 0.48, "margin": 0.065, "bottom": 0.12},
    },
    "small": {
        "size": (462, 174),
        "layout": {"mode": "center", "title": 0.80, "tagline": 0.0, "center_y": 0.60},
    },
    "page_background": {
        "size": (1438, 810),
        "layout": {"mode": "center", "title": 0.60, "tagline": 0.28, "center_y": 0.67},
    },
    "library_hero": {
        "size": (3840, 1240),
        "layout": {"mode": "center", "title": 0.42, "tagline": 0.18, "center_y": 0.68},
    },
}

SUPERSAMPLE = 2
HERO_DOWNSCALE = 4


def build_capsule(name: str, variant: dict) -> Image.Image:
    spec = CAPSULES[name]
    W, H = spec["size"]
    layout = spec["layout"]

    if name == "library_hero":
        rw, rh = W // HERO_DOWNSCALE, H // HERO_DOWNSCALE
        art = render_scene(rw, rh, variant)
        art = art.resize((W, H), Image.Resampling.LANCZOS)
        return draw_wordmark(art, W, H, layout, variant,
                             with_tagline=layout.get("tagline", 0) > 0)

    S = SUPERSAMPLE
    art = render_scene(W * S, H * S, variant)
    art = draw_wordmark(art, W * S, H * S, layout, variant,
                        with_tagline=layout.get("tagline", 0) > 0)
    return art.resize((W, H), Image.Resampling.LANCZOS)


def build_library_logo(variant: dict) -> Image.Image:
    W, H = 1280, 720
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    emblem_layer, ed = new_layer(W, H)
    cx, cy = W / 2, H * 0.30
    em_r = H * 0.11
    ed.ellipse(
        [cx - em_r, cy - em_r * 0.42, cx + em_r, cy + em_r * 0.42],
        outline=rgb(PALETTE["secondary"]) + (235,),
        width=max(3, int(em_r * 0.12)),
    )
    for i in range(16):
        a = 2 * math.pi * i / 16
        ed.line(
            [(cx, cy), (cx + em_r * math.cos(a), cy + em_r * 0.42 * math.sin(a))],
            fill=rgb(PALETTE["secondary"]) + (150,),
            width=2,
        )
    draw_fish(ed, cx + em_r * 0.15, cy, em_r * 0.42)
    img = Image.alpha_composite(img, emblem_layer)

    img = draw_wordmark(img, W, H, {
        "mode": "center",
        "title": 0.62,
        "tagline": 0.30,
        "center_y": 0.66,
        "title_tracking": 0.10,
    }, variant, with_tagline=True)
    return img


def build_preview(outputs: dict[str, Path], out_dir: Path) -> Path:
    margin = 60
    label_h = 46
    entries = sorted(outputs.items(), key=lambda kv: -(kv[1].stat().st_size if kv[1].exists() else 0))
    entries = [(k, p) for k, p in outputs.items() if k != "library_logo"]
    max_w = max(Image.open(p).width for _, p in entries)
    target_w = 1600
    scale = min(1.0, target_w / max_w)

    thumbs = []
    for name, path in entries:
        im = Image.open(path).convert("RGBA")
        w = int(im.width * scale)
        h = int(im.height * scale)
        im = im.resize((w, h), Image.Resampling.LANCZOS)
        thumbs.append((name, im))

    total_h = margin + sum(label_h + im.height + 34 for _, im in thumbs) + margin
    canvas = Image.new("RGB", (target_w + margin * 2, total_h), rgb("#121A22"))
    d = ImageDraw.Draw(canvas)
    label_font = load_font(FONT_SEMI, 26)
    dim_font = load_font(FONT_REG, 20)

    y = margin
    for name, im in thumbs:
        d.text((margin, y), f"{name}  ({im.width}x{im.height})", font=label_font,
               fill=rgb("#ECEFF4"))
        y += label_h
        canvas.paste(im, (margin, y), im)
        y += im.height + 34
    preview_path = out_dir / "preview_all_capsules.png"
    canvas.save(preview_path, "PNG", optimize=True)
    return preview_path


def verify(out_dir: Path) -> None:
    print("\nGenerated files:")
    expected = {
        "main.png": (616, 353),
        "header.png": (460, 215),
        "small.png": (462, 174),
        "library_header.png": (460, 215),
        "library_hero.png": (3840, 1240),
        "page_background.png": (1438, 810),
        "library_logo.png": (1280, 720),
    }
    ok = True
    for fname, (w, h) in expected.items():
        p = out_dir / fname
        if not p.exists():
            print(f"  MISSING {fname}")
            ok = False
            continue
        im = Image.open(p)
        match = im.size == (w, h)
        ok = ok and match
        status = "OK" if match else "WRONG"
        print(f"  [{status}] {fname}: {im.size[0]}x{im.size[1]} (mode {im.mode})")
    print("\nAll dimensions correct." if ok else "\nDimension check FAILED.")


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate Lakskongen Steam capsule art.")
    ap.add_argument("--out", default=str(DEFAULT_OUT), help="output directory")
    ap.add_argument("--variant", default="aurora", choices=sorted(VARIANTS),
                    help="motif variant for the cohesive set")
    ap.add_argument("--no-preview", action="store_true", help="skip contact sheet")
    args = ap.parse_args()

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    variant = VARIANTS[args.variant]

    print(f"Lakskongen store art — variant '{args.variant}' ({variant['label']})")
    print(f"Output: {out_dir}\n")

    outputs: dict[str, Path] = {}
    for name in CAPSULES:
        img = build_capsule(name, variant)
        path = out_dir / f"{name}.png"
        img.save(path, "PNG", optimize=True)
        outputs[name] = path
        print(f"  wrote {path.name}  {img.size[0]}x{img.size[1]}")

    logo = build_library_logo(variant)
    logo_path = out_dir / "library_logo.png"
    logo.save(logo_path, "PNG", optimize=True)
    outputs["library_logo"] = logo_path
    print(f"  wrote {logo_path.name}  {logo.size[0]}x{logo.size[1]}")

    if not args.no_preview:
        preview = build_preview(outputs, out_dir)
        print(f"  wrote {preview.name}")

    verify(out_dir)


if __name__ == "__main__":
    main()
