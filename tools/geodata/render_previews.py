#!/usr/bin/env python3
"""Preview images of the shipped map data (matplotlib, Agg backend; no windows).

Reads only godot/data/geo/ (what the game will load), so a preview that looks
right also shows the published files agree with each other. Writes small JPGs
to docs/run/evidence/geo/: national.jpg and po01.jpg..po13.jpg.
"""

from __future__ import annotations

import io
import json
import sys
import zlib

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
from matplotlib.colors import LinearSegmentedColormap  # noqa: E402
from matplotlib.patches import PathPatch  # noqa: E402
from matplotlib.path import Path as MPath  # noqa: E402
from PIL import Image  # noqa: E402
from pyproj import Transformer  # noqa: E402

from geo_common import DOCS_EVIDENCE, OUT  # noqa: E402

TO_UTM = Transformer.from_crs("EPSG:4326", "EPSG:25833", always_xy=True)
INK = "#0B2A3A"
STYLE = {
    "sea_salmonid": dict(marker="o", s=10, c="#EDEFEF", edgecolors="#1C1E20", linewidths=0.5, zorder=6),
    "land_salmonid": dict(marker="s", s=12, c="#E07B39", edgecolors="#1C1E20", linewidths=0.5, zorder=6),
    "offshore_salmonid": dict(marker="D", s=30, c="#E8B92A", edgecolors="#1C1E20", linewidths=0.6, zorder=7),
    "other": dict(marker="o", s=5, c="#9FB4BB", edgecolors="none", zorder=5),
}
HEAT = LinearSegmentedColormap.from_list("heat", ["#1F5E73", "#3F9E8F", "#7FD69B", "#E8D36A", "#F2B705"])
STAGE_STYLE = {"tilradning_2019": ":", "anbefaling_2022": "--", "konsekvensvurdering_2024": "-.",
               "kan_utlyses_2026": "-"}


def load(name: str) -> dict:
    return json.loads((OUT / name).read_text(encoding="utf-8"))


def utm(coords: np.ndarray) -> np.ndarray:
    x, y = TO_UTM.transform(coords[:, 0], coords[:, 1])
    return np.column_stack([x, y])


def polygon_path(geom: dict) -> MPath:
    polys = geom["coordinates"] if geom["type"] == "MultiPolygon" else [geom["coordinates"]]
    verts, codes = [], []
    for poly in polys:
        for ring in poly:
            r = utm(np.asarray(ring, dtype=float))
            verts.extend(r.tolist())
            codes.extend([MPath.MOVETO] + [MPath.LINETO] * (len(r) - 2) + [MPath.CLOSEPOLY])
    return MPath(verts, codes)


def lines(geom: dict) -> list[np.ndarray]:
    parts = geom["coordinates"] if geom["type"] == "MultiLineString" else [geom["coordinates"]]
    return [utm(np.asarray(p, dtype=float)) for p in parts]


def decode_png(path) -> np.ndarray:
    """Decode the pipeline's own PNGs (8-bit, filter 0) without extra libraries."""
    raw = path.read_bytes()
    pos, idat, w = 8, b"", 0
    while pos < len(raw):
        n = int.from_bytes(raw[pos:pos + 4], "big")
        tag = raw[pos + 4:pos + 8]
        data = raw[pos + 8:pos + 8 + n]
        if tag == b"IHDR":
            w, h, ctype = int.from_bytes(data[0:4], "big"), int.from_bytes(data[4:8], "big"), data[9]
        elif tag == b"IDAT":
            idat += data
        pos += 12 + n
    ch = {0: 1, 2: 3, 4: 2}[ctype]
    buf = np.frombuffer(zlib.decompress(idat), np.uint8).reshape(h, w * ch + 1)
    return buf[:, 1:].reshape(h, w, ch)


def base(ax, img_meta: dict) -> tuple:
    img = Image.open(OUT / img_meta["file"])
    x0, y0, x1, y1 = img_meta["bounds_25833"]
    ax.imshow(np.asarray(img), extent=(x0, x1, y0, y1), zorder=0, interpolation="bilinear")
    ax.set_xlim(x0, x1)
    ax.set_ylim(y0, y1)
    ax.set_axis_off()
    return x0, y0, x1, y1


def draw_sites(ax, loc: dict, bounds, scale: float = 1.0) -> None:
    x0, y0, x1, y1 = bounds
    groups: dict[str, list] = {}
    for f in loc["features"]:
        p = f["properties"]
        if not (x0 <= p["x"] <= x1 and y0 <= p["y"] <= y1):
            continue
        key = p["kind"] if p["kind"] in STYLE else "other"
        groups.setdefault(key, []).append((p["x"], p["y"]))
    for key in ("other", "sea_salmonid", "land_salmonid", "offshore_salmonid"):
        if key in groups:
            xy = np.array(groups[key])
            st = dict(STYLE[key])
            st["s"] = st["s"] * scale
            ax.scatter(xy[:, 0], xy[:, 1], **st)


def draw_offshore(ax, off: dict, label: bool) -> None:
    for f in off["features"]:
        p = f["properties"]
        path = polygon_path(f["geometry"])
        ax.add_patch(PathPatch(path, facecolor="#E8B92A", alpha=0.10, edgecolor="none", zorder=3))
        ax.add_patch(PathPatch(path, facecolor="none", edgecolor="#E8B92A", lw=1.0,
                               ls=STAGE_STYLE.get(p["stage"], "-"), zorder=4))
        if label and p["stage"] == "kan_utlyses_2026":
            v = path.vertices
            ax.text(v[:, 0].mean(), v[:, 1].max(), p["name"], color="#E8B92A", fontsize=6,
                    ha="center", va="bottom", zorder=9)


def draw_candidates(ax, cand: dict, po: int | None) -> None:
    for s in cand["sites"]:
        if po is not None and s["po"] != po and s["type"] == "open_pen":
            continue
        if s["type"] == "open_pen":
            ax.scatter([s["x"]], [s["y"]], marker="*", s=90 if po else 28, c="#F2876A", edgecolors="#1C1E20",
                       linewidths=0.6, zorder=8)
            if po:
                ax.text(s["x"], s["y"], f"  {s['rank']}", color="white", fontsize=6, va="center", zorder=9)
        else:
            ax.scatter([s["x"]], [s["y"]], marker="h", s=60 if po else 26, c="#E8B92A", edgecolors="#1C1E20",
                       linewidths=0.6, zorder=8)


def legend(ax, items: list[tuple[str, dict]], loc="lower right") -> None:
    handles = [ax.scatter([], [], **st) for _, st in items]
    lg = ax.legend(handles, [t for t, _ in items], loc=loc, fontsize=6, frameon=True, facecolor=INK,
                   edgecolor="#2C4650", labelcolor="#E8EEF0", framealpha=0.85)
    lg.set_zorder(20)


def save(fig, name: str) -> int:
    buf = io.BytesIO()
    fig.savefig(buf, format="png", dpi=fig.dpi, facecolor=INK)
    plt.close(fig)
    img = Image.open(buf).convert("RGB")
    DOCS_EVIDENCE.mkdir(parents=True, exist_ok=True)
    img.save(DOCS_EVIDENCE / name, quality=72, optimize=True)
    return (DOCS_EVIDENCE / name).stat().st_size


def national(bm: dict, data: dict) -> None:
    meta = bm["images"]["national"]
    w, h = meta["width"], meta["height"]
    fig = plt.figure(figsize=(w / 200, h / 200), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    bounds = base(ax, meta)
    # Light PO tints from the index raster (sea pixels only).
    idx = decode_png(OUT / bm["images"]["po_index_national"]["file"])[..., 0]
    tints = {f["properties"]["id"]: f["properties"]["tint"] for f in data["po"]["features"]}
    rgba = np.zeros((h, w, 4))
    for po, hx in tints.items():
        c = [int(hx[i:i + 2], 16) / 255 for i in (1, 3, 5)]
        rgba[idx == po] = c + [0.38]
    ax.imshow(rgba, extent=(bounds[0], bounds[2], bounds[1], bounds[3]), zorder=1, interpolation="nearest")
    for f in data["po"]["features"]:
        # The official PO polygons also cover land; the overview shows them only as sea tints.
        p = f["properties"]
        lx, ly = TO_UTM.transform(*p["label"])
        ax.text(lx - 60_000, ly, f"PO{p['id']}", color="#E8EEF0", fontsize=8, weight="bold", ha="right",
                va="center", zorder=10)
    for f in data["exc_nat"]["features"]:
        if f["properties"]["kind"] == "salmon_fjord":
            ax.add_patch(PathPatch(polygon_path(f["geometry"]), facecolor="#FF8A80", alpha=0.55,
                                   edgecolor="none", zorder=3))
    draw_offshore(ax, data["off_nat"], label=True)
    draw_sites(ax, data["loc"], bounds, scale=0.8)
    draw_candidates(ax, data["cand"], None)
    legend(ax, [("Sea site, salmonids", STYLE["sea_salmonid"]), ("Land site, salmonids", STYLE["land_salmonid"]),
                ("Offshore site", STYLE["offshore_salmonid"]), ("Other species", STYLE["other"]),
                ("Candidate, open pen", dict(marker="*", s=40, c="#F2876A", edgecolors="#1C1E20")),
                ("Candidate, offshore", dict(marker="h", s=30, c="#E8B92A", edgecolors="#1C1E20")),
                ("National salmon fjord", dict(marker="s", s=30, c="#FF8A80", alpha=0.7))])
    print("national.jpg", save(fig, "national.jpg") // 1024, "KB")


def regional(bm: dict, data: dict, suit: dict, po: int) -> None:
    meta = bm["images"][f"po{po:02d}"]
    w, h = meta["width"], meta["height"]
    scale = 900 / max(w, h)
    fig = plt.figure(figsize=(w * scale / 100, h * scale / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    bounds = base(ax, meta)
    g = suit["grids"][str(po)]
    rgb = decode_png(OUT / g["file"])
    score = np.where(rgb[..., 0] > 0, (rgb[..., 0].astype(float) - 1) / 254, np.nan)
    code = rgb[..., 1]
    gx0, gy1 = g["origin_25833"]
    ext = (gx0, gx0 + g["width"] * g["res_m"], gy1 - g["height"] * g["res_m"], gy1)
    heat = HEAT(np.clip((score - 0.5) / 0.5, 0, 1))
    heat[..., 3] = np.where(np.isnan(score), 0, 0.62)
    ax.imshow(heat, extent=ext, zorder=2, interpolation="nearest")
    excluded = (code >= 3) & (code <= 15)
    ex = np.zeros((g["height"], g["width"], 4))
    ex[excluded] = [0.70, 0.09, 0.18, 0.30]
    ex[code == 13] = [0.05, 0.10, 0.20, 0.35]  # beyond the planning area: darker, quieter
    ax.imshow(ex, extent=ext, zorder=2, interpolation="nearest")
    for f in data["exc_reg"]["features"]:
        kind = f["properties"]["kind"]
        col = {"salmon_fjord": "#FF8A80", "protected_area": "#7FD69B", "coral_closure": "#C9A0FF"}[kind]
        ax.add_patch(PathPatch(polygon_path(f["geometry"]), facecolor="none", edgecolor=col, lw=0.8,
                               hatch="////" if kind == "salmon_fjord" else None, alpha=0.9, zorder=3))
    for f in data["fair"]["features"]:
        for ln in lines(f["geometry"]):
            ax.plot(ln[:, 0], ln[:, 1], color="#E8EEF0", lw=0.4 if f["properties"]["kind"] == "biled" else 0.8,
                    ls="--", alpha=0.6, zorder=3)
    for f in data["areas"]["features"]:
        ax.add_patch(PathPatch(polygon_path(f["geometry"]), facecolor="#EDEFEF", edgecolor="#1C1E20", lw=0.3,
                               zorder=5))
    pof = next(f for f in data["po_reg"]["features"] if f["properties"]["id"] == po)
    ax.add_patch(PathPatch(polygon_path(pof["geometry"]), facecolor="none", edgecolor="#E8EEF0", lw=1.0,
                           zorder=4))
    draw_offshore(ax, data["off_reg"], label=False)
    draw_sites(ax, data["loc"], bounds, scale=1.6)
    draw_candidates(ax, data["cand"], po)
    ax.text(0.02, 0.98, f"PO{po}  {pof['properties']['name']}", transform=ax.transAxes, color="#E8EEF0",
            fontsize=11, weight="bold", va="top", zorder=20,
            bbox=dict(facecolor=INK, edgecolor="none", alpha=0.8, pad=4))
    legend(ax, [("Suitability (low to high)", dict(marker="s", s=30, c="#3F9E8F")),
                ("", dict(marker="s", s=30, c="#F2B705")),
                ("Hard exclusion", dict(marker="s", s=30, c="#B3172F", alpha=0.5)),
                ("Sea site, salmonids", STYLE["sea_salmonid"]),
                ("Candidate (rank)", dict(marker="*", s=50, c="#F2876A", edgecolors="#1C1E20"))])
    print(f"po{po:02d}.jpg", save(fig, f"po{po:02d}.jpg") // 1024, "KB")


def main(argv: list[str]) -> None:
    bm, suit = load("basemap.json"), load("suitability.json")
    data = {
        "po": load("production_areas.national.geojson"), "po_reg": load("production_areas.regional.geojson"),
        "loc": load("localities.geojson"), "areas": load("locality_areas.geojson"),
        "exc_nat": load("exclusions.national.geojson"), "exc_reg": load("exclusions.regional.geojson"),
        "off_nat": load("offshore_areas.national.geojson"), "off_reg": load("offshore_areas.regional.geojson"),
        "fair": load("fairways.regional.geojson"), "cand": load("candidates.json"),
    }
    national(bm, data)
    pos = [int(a) for a in argv] or list(range(1, 14))
    for po in pos:
        regional(bm, data, suit, po)


if __name__ == "__main__":
    main(sys.argv[1:])
