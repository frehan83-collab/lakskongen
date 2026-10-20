#!/usr/bin/env python3
"""Build the master rasters every later step samples (cache only, not shipped).

Grid: EPSG:25833, extent EXTENT, row 0 = north. Two resolutions:
  - 125 m: surface class from Kartverket N250 Arealdekke (sea, lake, river,
    forest, open land, bog, farmland, built-up, snow/glacier); cells N250
    does not cover are split into sea / foreign land with EMODnet.
  - 250 m: depth (m, positive down; Kartverket's surveyed 50 m grid first,
    EMODnet DTM 2022 where it has gaps), land elevation (Kartverket DTM 50,
    EMODnet outside Norway), and the sea fraction of each cell.
Output: ~/.cache/lakskongen-geo/work/grids.npz (+ grids.json with the extent).
"""

from __future__ import annotations

import io
import json
import zipfile

import numpy as np
import pyogrio
import shapely
from PIL import Image, ImageDraw

from geo_common import WORK, raw_files

EXTENT = (-250_000, 6_400_000, 1_250_000, 8_150_000)  # x0, y0, x1, y1 (EPSG:25833)
RES_CLASS = 125
RES = 250

# Surface classes (uint8). 0 = not covered by N250 (resolved with EMODnet).
CLASS = {
    "none": 0, "sea": 1, "lake": 2, "river": 3, "forest": 4, "open": 5, "bog": 6,
    "farmland": 7, "built": 8, "snow": 9, "foreign_land": 10,
}
N250_CLASS = {
    "Havflate": "sea", "Innsjø": "lake", "InnsjøRegulert": "lake", "Elv": "river", "Skog": "forest",
    "ÅpentOmråde": "open", "Steinbrudd": "open", "Steintipp": "open", "Myr": "bog",
    "DyrketMark": "farmland", "Tettbebyggelse": "built", "BymessigBebyggelse": "built",
    "Industriområde": "built", "Lufthavn": "built", "SnøIsbre": "snow",
}


def grid_shape(res: int) -> tuple[int, int]:
    x0, y0, x1, y1 = EXTENT
    return (y1 - y0) // res, (x1 - x0) // res


def _n250_path() -> str:
    (zpath,) = raw_files("kv_n250")
    gdb = zipfile.ZipFile(zpath).namelist()[0].split("/")[0]
    return f"/vsizip/{zpath}/{gdb}"


def rasterize_classes() -> np.ndarray:
    """N250 Arealdekke polygons to a 125 m class grid.

    The polygons form a planar partition; drawing them by descending area
    without their holes paints every hole's content (always a smaller polygon)
    after its container, so islands in lakes in islands come out right.
    """
    x0, _, _, y1 = EXTENT
    h, w = grid_shape(RES_CLASS)
    meta, _, wkb, fields = pyogrio.raw.read(_n250_path(), layer="N250_Arealdekke_omrade", columns=["objtype"])
    geoms = shapely.from_wkb(wkb)
    objtype = fields[0]
    codes = np.array([CLASS[N250_CLASS.get(t, "open")] for t in objtype], dtype=np.uint8)
    geoms = shapely.simplify(geoms, 25.0, preserve_topology=True)
    areas = shapely.area(geoms)
    # Stable order: area descending, then source order (deterministic for equal areas).
    order = np.lexsort((np.arange(len(geoms)), -areas))
    img = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(img)
    for i in order:
        g = geoms[i]
        parts = g.geoms if g.geom_type == "MultiPolygon" else [g]
        for p in parts:
            ext = np.asarray(p.exterior.coords)
            px = (ext[:, 0] - x0) / RES_CLASS - 0.5  # PIL: integer = pixel centre
            py = (y1 - ext[:, 1]) / RES_CLASS - 0.5
            if len(px) >= 3:
                draw.polygon(list(zip(px.tolist(), py.tolist())), fill=int(codes[i]))
    return np.array(img, dtype=np.uint8)


def _tile_origin(name: str) -> tuple[int, int]:
    _, tx, ty, _ = name.rsplit(".", 1)[0].split("_")
    return int(tx), int(ty)


def mosaic_wcs(source_id: str) -> np.ndarray:
    """Mosaic the 250 m WCS tiles (named bathy_<x>_<y>_250m.tif, lower-left origin)."""
    x0, _, _, y1 = EXTENT
    h, w = grid_shape(RES)
    out = np.full((h, w), np.nan, dtype=np.float32)
    for path in raw_files(source_id):
        tx, ty = _tile_origin(path.name)
        a = np.array(Image.open(path), dtype=np.float32)
        n = a.shape[0]
        r0 = (y1 - (ty + n * RES)) // RES
        c0 = (tx - x0) // RES
        out[r0:r0 + n, c0:c0 + n] = a
    return out


def mosaic_emodnet() -> np.ndarray:
    """EMODnet DTM (native lon/lat tiles) bilinearly resampled to the 250 m UTM grid.

    Values: negative = sea depth, positive = land height, NaN = no data. The
    resampling is NaN-aware (weights), so coastal cells are not dragged to 0.
    """
    from pyproj import Transformer
    from scipy import ndimage

    from geo_sources import source

    p = source("emodnet_bathymetry").params
    lon0, lat0, lon1, lat1 = p["bbox"]
    rx, ry = p["res_deg"]
    W = int(round((lon1 - lon0) / rx))
    H = int(round((lat1 - lat0) / ry))
    ll = np.full((H, W), np.nan, dtype=np.float32)  # row 0 = north (lat1)
    for path in raw_files("emodnet_bathymetry"):
        _, slon, slat = path.stem.split("_")
        a = np.array(Image.open(path), dtype=np.float32)
        c0 = int(round((float(slon) - lon0) / rx))
        r0 = int(round((lat1 - (float(slat) + a.shape[0] * ry)) / ry))
        ll[r0:r0 + a.shape[0], c0:c0 + a.shape[1]] = a
    ll[ll < -20000] = np.nan
    valid = ~np.isnan(ll)
    filled = np.where(valid, ll, 0.0).astype(np.float64)

    x0, _, _, y1 = EXTENT
    h, w = grid_shape(RES)
    tr = Transformer.from_crs("EPSG:25833", "EPSG:4326", always_xy=True)
    out = np.full((h, w), np.nan, dtype=np.float32)
    xs = x0 + (np.arange(w) + 0.5) * RES
    for r_start in range(0, h, 500):
        rows = np.arange(r_start, min(h, r_start + 500))
        ys = y1 - (rows + 0.5) * RES
        X, Y = np.meshgrid(xs, ys)
        lon, lat = tr.transform(X, Y)
        # Pixel-centre coordinates in the lon/lat mosaic.
        col = (lon - lon0) / rx - 0.5
        row = (lat1 - lat) / ry - 0.5
        v = ndimage.map_coordinates(filled, [row, col], order=1, mode="constant", cval=0.0)
        wgt = ndimage.map_coordinates(valid.astype(np.float64), [row, col], order=1, mode="constant", cval=0.0)
        out[rows[0]:rows[-1] + 1] = np.where(wgt > 0.25, v / np.maximum(wgt, 1e-9), np.nan)
    return out


def mosaic_dtm50() -> np.ndarray:
    """Kartverket DTM 50 tiles block-averaged to the 250 m grid (NaN = no data)."""
    x0, _, _, y1 = EXTENT
    h, w = grid_shape(RES)
    total = np.zeros(h * w, dtype=np.float64)
    count = np.zeros(h * w, dtype=np.float64)
    for path in raw_files("kv_dtm50"):
        z = zipfile.ZipFile(path)
        inner = [n for n in z.namelist() if n.endswith(".tif")][0]
        im = Image.open(io.BytesIO(z.read(inner)))
        tie = im.tag_v2[33922]
        scale = im.tag_v2[33550]
        a = np.array(im, dtype=np.float32)
        valid = a > -1000
        # GeoKey 1025 = 2 (PixelIsPoint): the tie point is the first pixel's centre.
        rows, cols = np.nonzero(valid)
        xs = tie[3] + cols * scale[0]
        ys = tie[4] - rows * scale[1]
        c = np.floor((xs - x0) / RES).astype(np.int64)
        r = np.floor((y1 - ys) / RES).astype(np.int64)
        ok = (c >= 0) & (c < w) & (r >= 0) & (r < h)
        idx = r[ok] * w + c[ok]
        total += np.bincount(idx, weights=a[valid][ok].astype(np.float64), minlength=h * w)
        count += np.bincount(idx, minlength=h * w)
    elev = np.full(h * w, np.nan, dtype=np.float32)
    has = count > 0
    elev[has] = (total[has] / count[has]).astype(np.float32)
    return elev.reshape(h, w)


def planning_area(sea: np.ndarray) -> np.ndarray:
    """Sea inside the baseline (grunnlinje) plus one nautical mile beyond it.

    The Planning and Building Act applies at sea out to 1 nm outside the
    baselines (plan- og bygningsloven § 1-2), so open-pen sites are zoned in
    municipal coastal plans there; farther out is the Havbruk til havs regime.
    The baseline and the land border are drawn as a barrier, the outer ocean
    is flood-filled from the north-west corner, and what it cannot reach is
    inside.
    """
    from scipy import ndimage

    from geo_vector import read_zip_layer

    x0, _, _, y1 = EXTENT
    h, w = sea.shape
    geoms, cols = read_zip_layer("kv_n250", "N250_AdministrativeOmråder_grense", ["objtype"])
    img = Image.new("1", (w, h), 0)
    draw = ImageDraw.Draw(img)
    for g, t in zip(geoms, cols["objtype"]):
        if t not in ("Grunnlinje", "Riksgrense"):
            continue
        for part in shapely.get_parts(g):
            c = np.asarray(part.coords)
            px = (c[:, 0] - x0) / RES - 0.5
            py = (y1 - c[:, 1]) / RES - 0.5
            draw.line(list(zip(px.tolist(), py.tolist())), fill=1, width=3)
    barrier = np.array(img, dtype=bool)
    lab, _ = ndimage.label(sea & ~barrier)
    outside = lab == lab[0, 0]
    inside = sea & ~outside
    dist = ndimage.distance_transform_edt(~inside) * RES
    return sea & (dist <= 1852.0)


def main() -> None:
    WORK.mkdir(parents=True, exist_ok=True)
    print("rasterising N250 surface classes (125 m)...")
    cls = rasterize_classes()
    print("mosaicking depth and elevation (250 m)...")
    kv = mosaic_wcs("kv_bathymetry")
    em = mosaic_emodnet()
    dtm = mosaic_dtm50()

    # Cells N250 does not cover: EMODnet decides sea (<0) or foreign land.
    em125 = np.repeat(np.repeat(em, 2, axis=0), 2, axis=1)
    uncovered = cls == CLASS["none"]
    cls[uncovered & (em125 < 0)] = CLASS["sea"]
    cls[uncovered & ~(em125 < 0)] = CLASS["foreign_land"]

    h, w = grid_shape(RES)
    sea_frac = (cls == CLASS["sea"]).reshape(h, 2, w, 2).mean(axis=(1, 3)).astype(np.float32)

    depth = np.where(kv < 0, -kv, np.nan).astype(np.float32)
    fill = np.isnan(depth) & (em < 0)
    depth[fill] = -em[fill]
    depth_src = np.zeros((h, w), np.uint8)  # 0 none, 1 Kartverket, 2 EMODnet
    depth_src[kv < 0] = 1
    depth_src[fill] = 2
    # Relief: Kartverket DTM 50 on Norwegian land (its 100 km tiles also reach a
    # little into Sweden and Finland, which would show as squares), EMODnet's
    # land heights everywhere else.
    norway_land = ((cls != CLASS["sea"]) & (cls != CLASS["foreign_land"])).reshape(h, 2, w, 2).mean(axis=(1, 3)) > 0
    elev = np.where(norway_land & ~np.isnan(dtm), dtm, np.where(em > 0, em, np.nan)).astype(np.float32)

    plan = planning_area(sea_frac >= 0.5)
    np.savez_compressed(WORK / "grids.npz", cls125=cls, sea_frac=sea_frac, depth=depth,
                        depth_src=depth_src, elev=elev, plan_area=plan)
    (WORK / "grids.json").write_text(json.dumps({
        "extent": EXTENT, "res_class": RES_CLASS, "res": RES, "classes": CLASS,
        "depth_cells_kartverket": int((depth_src == 1).sum()),
        "depth_cells_emodnet": int((depth_src == 2).sum()),
    }, indent=1), encoding="utf-8")
    print(f"sea cells (250 m, >=50 % sea): {(sea_frac >= 0.5).sum()}, "
          f"depth from Kartverket {(depth_src == 1).sum()}, EMODnet {(depth_src == 2).sum()}")


if __name__ == "__main__":
    main()
