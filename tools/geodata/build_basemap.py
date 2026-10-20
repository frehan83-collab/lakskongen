#!/usr/bin/env python3
"""Stylised base maps (Kyst & Stål): one national texture and one per PO.

Not a satellite image and not a GIS look: flat land-cover colours from
Kartverket N250 in the project's cool Nordic palette (design/tokens.json
"landscape"), relief from a hillshade of Kartverket DTM 50, the sea graded by
depth (Kartverket depth grid, EMODnet where it has gaps) with a pale rim along
the coast. Neighbouring countries are drawn flat and quiet.

Writes godot/data/geo/basemap/national.jpg, po01.jpg..po13.jpg and
po_index.national.png (production-area id per sea pixel of the national
texture: the game tints areas lightly by looking it up, and can pick an area
under the cursor). Georeference: basemap.json.
"""

from __future__ import annotations

import io

import numpy as np
from PIL import Image
from scipy import ndimage

import build_layers as L
from geo_common import OUT, write_json, write_png
from geo_raster import Grid, load_grids, rasterize

# National texture: EPSG:25833 bounds and pixel size (m).
NATIONAL_BOUNDS = (-200_000, 6_420_000, 1_200_000, 8_060_000)
NATIONAL_RES = 800
PO_MAX_PX = 1024
PO_MARGIN_M = 15_000
JPEG_QUALITY = {"national": 86, "po": 84}


def hexc(h: str) -> np.ndarray:
    return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], dtype=np.float64) / 255.0


# Palette. Land and snow from design/tokens.json "landscape" (granite #6B7378,
# heath #8F9A5A, spruce #2F4A3A, snow #F4F7FA, deep-water #0E2A33), lifted a
# step so relief reads; saturation stays on equipment and signals (art brief).
LAND = {
    "forest": hexc("#4A6352"), "open": hexc("#8E9680"), "bog": hexc("#8C8B6E"),
    "farmland": hexc("#93A07A"), "built": hexc("#A3A19B"), "snow": hexc("#EEF2F5"),
    "lake": hexc("#6F9DB1"), "river": hexc("#6F9DB1"), "foreign_land": hexc("#9AA2A7"),
}
HIGH_GRANITE = hexc("#A4AAAD")
HIGH_PALE = hexc("#C9CFD2")
SEA_STOPS = [(0, "#5B93A8"), (20, "#3F7D98"), (60, "#2D6987"), (150, "#215776"),
             (400, "#184566"), (1000, "#113655"), (2500, "#0D2A45")]
COAST_RIM = hexc("#8FBCCB")


def block_mean(a: np.ndarray, f: int) -> np.ndarray:
    h, w = a.shape[0] // f * f, a.shape[1] // f * f
    a = a[:h, :w]
    return a.reshape(h // f, f, w // f, f).mean(axis=(1, 3))


def resample(a: np.ndarray, shape: tuple[int, int]) -> np.ndarray:
    """Bilinear resample of a float grid to (h, w); NaN-safe via a weight channel."""
    valid = ~np.isnan(a)
    img = Image.fromarray(np.where(valid, a, 0).astype(np.float32), mode="F")
    wgt = Image.fromarray(valid.astype(np.float32), mode="F")
    size = (shape[1], shape[0])
    v = np.array(img.resize(size, Image.BILINEAR), dtype=np.float64)
    w = np.array(wgt.resize(size, Image.BILINEAR), dtype=np.float64)
    return np.where(w > 0.2, v / np.maximum(w, 1e-6), np.nan)


def hillshade(elev: np.ndarray, res: float, azimuth=315.0, altitude=40.0, z=2.2) -> np.ndarray:
    e = np.nan_to_num(elev, nan=0.0)
    gy, gx = np.gradient(e, res)
    slope = np.arctan(z * np.hypot(gx, gy))
    aspect = np.arctan2(-gx, gy)
    az, alt = np.radians(azimuth), np.radians(altitude)
    hs = np.sin(alt) * np.cos(slope) + np.cos(alt) * np.sin(slope) * np.cos(az - aspect)
    return np.clip(hs, 0.0, 1.0)


def sea_colour(depth: np.ndarray) -> np.ndarray:
    d = np.nan_to_num(depth, nan=200.0)
    xs = np.array([s[0] for s in SEA_STOPS], dtype=np.float64)
    cols = np.array([hexc(s[1]) for s in SEA_STOPS])
    t = np.log1p(np.clip(d, 0, 4000))
    tx = np.log1p(xs)
    return np.stack([np.interp(t, tx, cols[:, k]) for k in range(3)], axis=-1)


def render(data: dict, g250: Grid, g125: Grid, bounds, res: int) -> np.ndarray:
    """RGB (0..1) of the window bounds at res metres per pixel (res a multiple of 125)."""
    f = res // 125
    sub125, rs125, cs125 = g125.window(bounds)
    cls = data["cls125"][rs125, cs125]
    h, w = cls.shape[0] // f, cls.shape[1] // f
    from build_grids import CLASS

    frac = {k: block_mean((cls == v).astype(np.float32), f) for k, v in CLASS.items() if k != "none"}
    sub250, rs250, cs250 = g250.window(bounds)
    elev = data["elev"][rs250, cs250]
    depth = data["depth"][rs250, cs250]
    if res >= 250:
        k = res // 250
        elev_r = block_mean(np.nan_to_num(elev, nan=0.0), k)[:h, :w]
        dmask = block_mean((~np.isnan(depth)).astype(np.float64), k)[:h, :w]
        depth_r = np.where(dmask > 0, block_mean(np.nan_to_num(depth, nan=0.0), k)[:h, :w] / np.maximum(dmask, 1e-6),
                           np.nan)
    else:
        elev_r = np.nan_to_num(resample(elev, (h, w)), nan=0.0)
        depth_r = resample(depth, (h, w))
    hs = hillshade(elev_r, res, z=2.2 if res <= 400 else 3.5)
    shade = 0.68 + 0.45 * (hs - 0.55)  # ~0.43 .. 0.88 around a mid tone

    # Land colour per class, open land lightening with height toward granite and pale rock.
    e = elev_r
    t_high = np.clip((e - 350) / 650, 0, 1)[..., None]
    t_pale = np.clip((e - 1000) / 500, 0, 1)[..., None]
    open_c = LAND["open"] * (1 - t_high) + HIGH_GRANITE * t_high
    open_c = open_c * (1 - t_pale) + HIGH_PALE * t_pale
    land = np.zeros((h, w, 3))
    wsum = np.zeros((h, w, 1))
    for k in ("forest", "open", "bog", "farmland", "built", "snow"):
        fr = frac[k][:h, :w, None]
        land += fr * (open_c if k == "open" else LAND[k])
        wsum += fr
    land = land / np.maximum(wsum, 1e-6)
    land = land * shade[..., None] + (1 - shade[..., None]) * np.array([0.05, 0.08, 0.12]) * 0.6
    land = np.clip(land, 0, 1)

    foreign = LAND["foreign_land"] * (0.86 + 0.18 * (hs[..., None] - 0.55))
    water_in = (frac["lake"] + frac["river"])[:h, :w, None]
    sea_fr = frac["sea"][:h, :w, None]

    sea = sea_colour(depth_r)
    seabed = hillshade(-np.nan_to_num(depth_r, nan=0.0), res, z=6.0)
    sea = sea * (0.92 + 0.16 * (seabed[..., None] - 0.5))
    # Pale rim along the coast (2-3 px), the chart-like edge of the stylised map.
    landmask = (sea_fr[..., 0] < 0.5)
    dist = ndimage.distance_transform_edt(~landmask)
    rim = np.clip(1.0 - dist / 2.5, 0, 1)[..., None] * 0.55
    sea = sea * (1 - rim) + COAST_RIM * rim

    lw = wsum
    fl = frac["foreign_land"][:h, :w, None]
    total = lw + fl + water_in + sea_fr
    rgb = (land * lw + foreign * fl + LAND["lake"] * water_in + sea * sea_fr) / np.maximum(total, 1e-6)
    return np.clip(rgb, 0, 1)


def to_jpeg(rgb: np.ndarray, quality: int) -> bytes:
    img = Image.fromarray(np.round(rgb * 255).astype(np.uint8), mode="RGB")
    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=quality, optimize=True, progressive=False, subsampling=2)
    return buf.getvalue()


def main() -> None:
    data, meta, g250, g125 = load_grids()
    out_dir = OUT / "basemap"
    out_dir.mkdir(parents=True, exist_ok=True)
    doc = {"crs": "EPSG:25833", "row0": "north", "images": {}}
    sizes = {}

    x0, y0, x1, y1 = NATIONAL_BOUNDS
    rgb = render(data, g250, g125, NATIONAL_BOUNDS, NATIONAL_RES)
    jpg = to_jpeg(rgb, JPEG_QUALITY["national"])
    (out_dir / "national.jpg").write_bytes(jpg)
    sizes["basemap/national.jpg"] = len(jpg)
    h, w = rgb.shape[:2]
    doc["images"]["national"] = {"file": "basemap/national.jpg", "bounds_25833": [x0, y1 - h * NATIONAL_RES,
                                                                              x0 + w * NATIONAL_RES, y1],
                                 "res_m": NATIONAL_RES, "width": w, "height": h}

    # Production-area index of the national texture (sea pixels only).
    rows, by_po = L.production_areas()
    grid = Grid(x0, y1, NATIONAL_RES, h, w)
    sea = render_sea_mask(data, g125, NATIONAL_BOUNDS, NATIONAL_RES, (h, w))
    idx = np.zeros((h, w), np.uint8)
    for po in sorted(by_po):
        m = rasterize([by_po[po]], grid)
        idx[m & sea & (idx == 0)] = po
    sizes["basemap/po_index.national.png"] = write_png(out_dir / "po_index.national.png", w, h,
                                                       [idx[i].tobytes() for i in range(h)], color_type=0)
    doc["images"]["po_index_national"] = {"file": "basemap/po_index.national.png",
                                          "bounds_25833": doc["images"]["national"]["bounds_25833"],
                                          "res_m": NATIONAL_RES, "width": w, "height": h,
                                          "encoding": "8-bit grey: production area 1-13 on its sea, 0 elsewhere"}

    for r in rows:
        po = r["po"]
        bx0, by0, bx1, by1 = r["geom"].bounds
        bx0, by0, bx1, by1 = bx0 - PO_MARGIN_M, by0 - PO_MARGIN_M, bx1 + PO_MARGIN_M, by1 + PO_MARGIN_M
        long_side = max(bx1 - bx0, by1 - by0)
        res = int(max(125, np.ceil(long_side / PO_MAX_PX / 125) * 125))
        # Snap the window to the 125 m grid and to whole output pixels.
        bx0 = np.floor(bx0 / res) * res
        by1 = np.ceil(by1 / res) * res
        wpx = int(np.ceil((bx1 - bx0) / res))
        hpx = int(np.ceil((by1 - by0) / res))
        bounds = (bx0, by1 - hpx * res, bx0 + wpx * res, by1)
        rgb = render(data, g250, g125, bounds, res)
        jpg = to_jpeg(rgb, JPEG_QUALITY["po"])
        name = f"basemap/po{po:02d}.jpg"
        (OUT / name).write_bytes(jpg)
        sizes[name] = len(jpg)
        hh, ww = rgb.shape[:2]
        doc["images"][f"po{po:02d}"] = {"file": name, "po": po,
                                        "bounds_25833": [bounds[0], bounds[3] - hh * res, bounds[0] + ww * res,
                                                         bounds[3]],
                                        "res_m": res, "width": ww, "height": hh}
    write_json(OUT / "basemap.json", doc, compact=False)
    for k, v in sorted(sizes.items()):
        print(f"{k:34s} {v / 1024:7.1f} KB")
    print(f"total {sum(sizes.values()) / 1024:.0f} KB")


def render_sea_mask(data: dict, g125: Grid, bounds, res: int, shape: tuple[int, int]) -> np.ndarray:
    from build_grids import CLASS

    f = res // 125
    _, rs, cs = g125.window(bounds)
    m = block_mean((data["cls125"][rs, cs] == CLASS["sea"]).astype(np.float32), f)
    return m[:shape[0], :shape[1]] >= 0.5


if __name__ == "__main__":
    main()
