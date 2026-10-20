#!/usr/bin/env python3
"""Suitability grids per production area and deterministic candidate sites.

Reads the master grids (build_grids.py) and the full-resolution layers
(build_layers.py helpers), scores every sea cell of every PO with the rules in
geo_rules.py, and writes:
  suitability/po01.png .. po13.png  500 m grid per PO, RGB: R = score byte
                                    (0 = no score, 1..255 = 0..1), G = reason
                                    code, B = depth / 4 m (0 = unknown or land)
  suitability.json                  grid georeference, codes, per-PO statistics
  suitability_rules.json            the rules (weights, thresholds, sources)
  candidates.json                   candidate new sites (open pens per PO and
                                    offshore units in Havbruk til havs areas)
                                    with score components, the binding
                                    constraint and a content hash
No randomness anywhere: ties break on position (north first, then west).
"""

from __future__ import annotations

import json
import zipfile

import numpy as np
import pyogrio
import shapely
from scipy import ndimage, signal
from scipy.spatial import cKDTree

import build_climatology as CL
import build_layers as L
import geo_rules as R
from geo_common import OUT, canonical_hash, load_fetch_manifest, raw_files, write_json, write_png
from geo_raster import Grid, disk, load_grids, rasterize
from geo_sim import production_area_table
from geo_vector import utm_to_lonlat_xy

REASONS = ["none", "land", "outside_po", "no_depth", "salmon_fjord", "national_salmon_river",
           "protected_area", "coral_closure", "coral_reef", "fairway", "farm_too_close",
           "too_shallow", "too_deep", "outside_planning_area"]
CODE = {r: i for i, r in enumerate(REASONS)}
COMPONENT_CODE = {"depth": 16, "exchange": 17, "farm_distance": 18, "temperature": 19, "river": 20,
                  "current": 21, "waves": 22, "oxygen": 23, "none": 24}
COMPONENT_NAME = {v: k for k, v in COMPONENT_CODE.items()}
# When several hard constraints hold, the first in this order is reported.
PRIORITY = ["salmon_fjord", "outside_planning_area", "national_salmon_river", "protected_area",
            "coral_closure", "coral_reef", "fairway", "farm_too_close", "no_depth", "too_shallow", "too_deep"]
PUBLISH_RES = 500
FAR = 1e9


class Inputs:
    """Every full-resolution input in EPSG:25833, loaded once."""

    def __init__(self) -> None:
        self.data, self.meta, self.g250, _ = load_grids()
        self.po_rows, self.by_po = L.production_areas()
        loc_rows, _ = L.localities(self.by_po)
        self.farms = np.array([[r["x"], r["y"]] for r in loc_rows if r["salmonid_sea"]])
        exc = L.exclusion_rows()
        self.polys = {k: [r["geom"] for r in exc if r["kind"] == k]
                      for k in ("salmon_fjord", "protected_area", "coral_closure")}
        self.fairway_lines = L.fairway_rows()
        self.polys["fairway"] = [shapely.buffer(r["geom"], r["buffer_m"]) for r in self.fairway_lines]
        self.trees = {k: shapely.STRtree(v) for k, v in self.polys.items()}
        outlets, _ = L.outlet_rows()
        self.national_rivers = np.array([[p["x"], p["y"]] for p in outlets if p["national"]])
        self.rivers = np.array([[p["x"], p["y"]] for p in outlets if not p["national"]])
        self.corals = np.array([[c["x"], c["y"]] for c in L.coral_points()])
        self.kd = {"farm": cKDTree(self.farms), "national_river": cKDTree(self.national_rivers),
                   "river": cKDTree(self.rivers), "coral": cKDTree(self.corals)}
        self.offshore = L.offshore_rows()
        table = production_area_table()
        c = R.COMPONENTS["temperature"]["ramp_c"]
        self.temp_score = {po: float(np.mean(R.trapezoid(np.array(t["sea_temp_monthly_c"]), *c)))
                           for po, t in table.items()}
        self.places = place_names()
        self.clim = CL.Fields() if CL.available() else None
        self.sim_temp = {po: t["sea_temp_monthly_c"] for po, t in table.items()}

    def masks(self, sub: Grid, kinds: tuple[str, ...]) -> dict[str, np.ndarray]:
        box = shapely.box(sub.x0, sub.y1 - sub.h * sub.res, sub.x0 + sub.w * sub.res, sub.y1)
        out = {}
        for k in kinds:
            idx = self.trees[k].query(box)
            out[k] = rasterize([self.polys[k][i] for i in sorted(idx)], sub)
        return out


def place_names() -> tuple[cKDTree, list[str]]:
    """Kartverket N250 place-name label positions (for 'near <place>' on candidates)."""
    (zpath,) = raw_files("kv_n250")
    gdb = zipfile.ZipFile(zpath).namelist()[0].split("/")[0]
    _, _, wkb, fields = pyogrio.raw.read(f"/vsizip/{zpath}/{gdb}", layer="N250_Stedsnavn_tekstplassering",
                                         columns=["TextString"])
    geoms = shapely.from_wkb(wkb)
    keep = [i for i, s in enumerate(fields[0]) if s and len(str(s)) >= 3 and not str(s)[0].isdigit()]
    cents = shapely.centroid(geoms[keep])
    xy = np.column_stack([shapely.get_x(cents), shapely.get_y(cents)])
    return cKDTree(xy), [str(fields[0][i]) for i in keep]


def nearest(kd: cKDTree, xy: np.ndarray) -> np.ndarray:
    if len(xy) == 0:
        return np.zeros(0)
    d, _ = kd.query(xy)
    return d


def openness(data: dict, g250: Grid, bounds, res: float) -> tuple[np.ndarray, Grid]:
    r = int(R.COMPONENTS["exchange"]["radius_m"] // res)
    sub, rs, cs = g250.window(bounds, margin=(r + 1) * res)
    k = disk(r)
    o = signal.fftconvolve(data["sea_frac"][rs, cs].astype(np.float64), k, mode="same") / k.sum()
    return np.clip(o, 0.0, 1.0), sub


def score_cells(inp: Inputs, sub: Grid, rs: slice, cs: slice, inside: np.ndarray, po: int | None,
                depth_ramp: list[float], open_pen: bool) -> dict[str, np.ndarray]:
    """All components and the hard reason for every cell of a window."""
    d = inp.data
    sea = d["sea_frac"][rs, cs] >= 0.5
    depth = d["depth"][rs, cs]
    interest = inside & sea
    rr, cc = np.nonzero(interest)
    xs, ys = sub.centres()
    pts = np.column_stack([xs[cc], ys[rr]])
    dist = {k: nearest(inp.kd[k], pts) for k in inp.kd}

    op_full, op_grid = openness(d, inp.g250, (sub.x0, sub.y1 - sub.h * sub.res, sub.x0 + sub.w * sub.res, sub.y1),
                                sub.res)
    oy = int(round((op_grid.y1 - sub.y1) / sub.res))
    ox = int(round((sub.x0 - op_grid.x0) / sub.res))
    open_cells = op_full[oy:oy + sub.h, ox:ox + sub.w][rr, cc]

    dep = depth[rr, cc]
    comp = {
        "depth": np.nan_to_num(R.trapezoid(dep, *depth_ramp), nan=0.0),
        "exchange": R.ramp(open_cells, *R.COMPONENTS["exchange"]["ramp"]),
        "farm_distance": R.ramp(dist["farm"] / 1000.0, *R.COMPONENTS["farm_distance"]["ramp_km"]),
        "temperature": np.full(len(rr), inp.temp_score.get(po, 1.0) if po else 1.0),
    }
    if inp.clim is not None and po is not None and len(rr):
        s = inp.clim.sample(pts[:, 0], pts[:, 1])
        comp["current"] = R.trapezoid(s["current_speed_ms"], *R.COMPONENTS["current"]["ramp_ms"])
        comp["waves"] = R.trapezoid(s["hs_m"], *R.COMPONENTS["waves"]["ramp_m"])
        comp["oxygen"] = R.ramp(s["o2_mmol_m3"], *R.COMPONENTS["oxygen"]["ramp_mmol"])
        ocean = {"current_speed_ms": s["current_speed_ms"], "hs_m": s["hs_m"], "o2_mmol_m3": s["o2_mmol_m3"]}
    else:
        ocean = {}
    river_c = np.clip(dist["river"] / R.RIVER_FACTOR["radius_m"], 0.0, 1.0)
    river_factor = R.RIVER_FACTOR["min_factor"] + (1.0 - R.RIVER_FACTOR["min_factor"]) * river_c

    masks = inp.masks(sub, ("salmon_fjord", "protected_area", "coral_closure", "fairway"))
    hard = {
        "salmon_fjord": masks["salmon_fjord"][rr, cc],
        "national_salmon_river": dist["national_river"] < R.HARD["national_salmon_river"]["radius_m"],
        "protected_area": masks["protected_area"][rr, cc],
        "coral_closure": masks["coral_closure"][rr, cc],
        "coral_reef": dist["coral"] < R.HARD["coral_reef"]["radius_m"],
        "fairway": masks["fairway"][rr, cc],
        "farm_too_close": dist["farm"] < R.HARD["farm_too_close"]["min_km"] * 1000.0,
        "no_depth": np.isnan(dep),
        "too_shallow": np.nan_to_num(dep, nan=1e9) < (R.HARD["too_shallow"]["depth_m"] if open_pen else depth_ramp[0]),
        "too_deep": np.nan_to_num(dep, nan=0) > (R.HARD["too_deep"]["depth_m"] if open_pen else depth_ramp[3]),
        "outside_planning_area": ~inp.data["plan_area"][rs, cs][rr, cc] if open_pen else np.zeros(len(rr), bool),
    }
    reason = np.zeros(len(rr), dtype=np.uint8)
    for k in reversed(PRIORITY):
        reason[hard[k]] = CODE[k]
    weights = {k: R.weight(k, inp.clim is not None) for k in comp}
    wsum = sum(weights.values())
    base = sum(weights[k] * comp[k] for k in comp) / wsum
    score = np.where(reason == 0, base * river_factor, 0.0)
    # Binding constraint: the term that costs the most score. A component costs
    # w_i / sum(w) x (1 - c_i) x river_factor; the river factor costs base x (1 - factor).
    names = list(comp) + ["river"]
    losses = np.vstack([weights[k] / wsum * (1.0 - comp[k]) * river_factor for k in comp]
                       + [base * (1.0 - river_factor)])
    binding = np.array([COMPONENT_CODE[names[i]] if losses[i, j] > 1e-9 else COMPONENT_CODE["none"]
                        for j, i in enumerate(np.argmax(losses, axis=0))], dtype=np.uint8) \
        if len(rr) else np.zeros(0, np.uint8)
    polymask = masks["salmon_fjord"] | masks["protected_area"] | masks["coral_closure"] | masks["fairway"]
    near_poly = ndimage.binary_dilation(polymask, iterations=1)[rr, cc]
    return {"rr": rr, "cc": cc, "x": pts[:, 0], "y": pts[:, 1], "depth": dep, "open": open_cells,
            "comp": comp, "river_c": river_c, "river_factor": river_factor, "reason": reason,
            "score": score, "binding": binding, "dist": dist, "near_poly": near_poly, "ocean": ocean}


def publish_grid(sub: Grid, inside: np.ndarray, cells: dict, sea: np.ndarray) -> tuple[Grid, list[bytes]]:
    """250 m cells to the 500 m published grid (best sub-cell wins), as RGB rows."""
    h, w = sub.h, sub.w
    score = np.zeros((h, w), np.float64)
    scored = np.zeros((h, w), bool)
    code = np.where(inside, CODE["land"], CODE["outside_po"]).astype(np.uint8)
    depth = np.zeros((h, w), np.float64)
    rr, cc = cells["rr"], cells["cc"]
    ok = cells["reason"] == 0
    code[rr, cc] = np.where(ok, cells["binding"], cells["reason"])
    score[rr[ok], cc[ok]] = cells["score"][ok]
    scored[rr[ok], cc[ok]] = True
    depth[rr, cc] = np.nan_to_num(cells["depth"], nan=0.0)
    f = PUBLISH_RES // int(sub.res)
    H, W = -(-h // f), -(-w // f)

    def pad(a, v):
        out = np.full((H * f, W * f), v, dtype=a.dtype)
        out[:h, :w] = a
        return out.reshape(H, f, W, f).transpose(0, 2, 1, 3).reshape(H, W, f * f)

    s, sc, cd, dp = pad(score, 0.0), pad(scored, False), pad(code, CODE["outside_po"]), pad(depth, 0.0)
    best = np.argmax(np.where(sc, s, -1.0), axis=2)
    any_scored = sc.any(axis=2)
    out_score = np.where(any_scored, np.take_along_axis(s, best[..., None], 2)[..., 0], 0.0)
    out_code = np.take_along_axis(cd, best[..., None], 2)[..., 0]
    # No scored sub-cell: report the most frequent code among the sub-cells
    # (ties to the lowest code), so a fjord stays a fjord and land stays land.
    counts = np.stack([(cd == c).sum(axis=2) for c in range(25)], axis=2)
    mode = np.argmax(counts, axis=2).astype(np.uint8)
    out_code = np.where(any_scored, out_code, mode).astype(np.uint8)
    dsum = dp.sum(axis=2)
    dn = (dp > 0).sum(axis=2)
    out_depth = np.where(dn > 0, dsum / np.maximum(dn, 1), 0.0)
    R_ = np.where(any_scored, 1 + np.round(out_score * 254), 0).astype(np.uint8)
    B_ = np.clip(np.round(out_depth / 4.0), 0, 255).astype(np.uint8)
    rgb = np.dstack([R_, out_code, B_])
    pub = Grid(sub.x0, sub.y1, PUBLISH_RES, H, W)
    return pub, [rgb[i].tobytes() for i in range(H)]


def pick(order: np.ndarray, xy: np.ndarray, spacing_m: float, limit: int, accept) -> list[int]:
    chosen: list[int] = []
    for i in order:
        if len(chosen) >= limit:
            break
        if chosen and np.min(np.hypot(*(xy[chosen] - xy[i]).T)) < spacing_m:
            continue
        if accept(i):
            chosen.append(int(i))
    return chosen


class PublishedCheck:
    """Candidates must sit outside the shipped (simplified) exclusion geometry too."""

    def __init__(self, inp: Inputs) -> None:
        fc = json.loads((OUT / "exclusions.regional.geojson").read_text(encoding="utf-8"))
        self.polys = [shapely.geometry.shape(f["geometry"]) for f in fc["features"]]
        self.tree = shapely.STRtree(self.polys)
        self.fairways = inp.fairway_lines

    def ok(self, x: float, y: float) -> bool:
        lon, lat = utm_to_lonlat_xy(np.array([[x, y]]))[0]
        p = shapely.Point(lon, lat)
        if len(self.tree.query(p, predicate="intersects")):
            return False
        pt = shapely.Point(x, y)
        # Shipped fairway lines are simplified by 50 m: keep that much extra clearance.
        return all(shapely.distance(r["geom"], pt) >= r["buffer_m"] + 60 for r in self.fairways
                   if shapely.distance(r["geom"], pt) < r["buffer_m"] + 500)


def candidate_record(inp: Inputs, cells: dict, i: int, kind: str, po: int | None, rank: int, extra: dict) -> dict:
    x, y = float(cells["x"][i]), float(cells["y"][i])
    lon, lat = utm_to_lonlat_xy(np.array([[x, y]]))[0]
    _, js = inp.places[0].query([x, y], k=8)
    near = next((inp.places[1][j] for j in js if R.brand_free(inp.places[1][j])), None)
    comps = {k: round(float(v[i]), 3) for k, v in cells["comp"].items()}
    comps["river"] = round(float(cells["river_c"][i]), 3)
    binding = extra.pop("binding", None) or COMPONENT_NAME[int(cells["binding"][i])]
    rec = {
        "id": extra.pop("id"), "type": kind, "po": po, "rank": rank,
        "lon": round(float(lon), 5), "lat": round(float(lat), 5), "x": round(x), "y": round(y),
        "score": round(float(cells["score"][i]), 3), "components": comps, "binding": binding,
        "river_factor": round(float(cells["river_factor"][i]), 3),
        "depth_m": int(round(float(cells["depth"][i]))), "openness": round(float(cells["open"][i]), 2),
        "nearest_farm_km": round(float(cells["dist"]["farm"][i]) / 1000, 2),
        "nearest_river_km": round(float(min(cells["dist"]["river"][i], cells["dist"]["national_river"][i])) / 1000, 2),
        "near": near,
    }
    if cells["ocean"]:
        rec["ocean"] = {k: round(float(v[i]), 3) for k, v in cells["ocean"].items()}
    rec.update(extra)
    return rec


def run_po(inp: Inputs, row: dict, check: PublishedCheck) -> tuple[dict, list[dict], tuple[Grid, list[bytes]]]:
    po, geom = row["po"], row["geom"]
    sub, rs, cs = inp.g250.window(geom.bounds)
    inside = rasterize([geom], sub)
    cells = score_cells(inp, sub, rs, cs, inside, po, R.COMPONENTS["depth"]["ramp_m"], open_pen=True)
    sea = inp.data["sea_frac"][rs, cs] >= 0.5
    grid = publish_grid(sub, inside, cells, sea)

    C = R.CANDIDATES
    ok = ((cells["reason"] == 0) & (cells["score"] >= C["min_score"]) & ~cells["near_poly"]
          & (cells["dist"]["national_river"] >= R.HARD["national_salmon_river"]["radius_m"] + 250)
          & (cells["dist"]["coral"] >= R.HARD["coral_reef"]["radius_m"] + 250))
    idx = np.nonzero(ok)[0]
    open_ = np.round(cells["open"][idx], 2)
    depth_off = np.abs(np.nan_to_num(cells["depth"][idx], nan=1e6) - C["ideal_depth_m"])
    order = idx[np.lexsort((cells["x"][idx], -cells["y"][idx], depth_off, -open_, -np.round(cells["score"][idx], 3)))]
    xy = np.column_stack([cells["x"], cells["y"]])
    chosen = pick(order, xy, C["spacing_km"] * 1000, C["per_po"], lambda i: check.ok(xy[i, 0], xy[i, 1]))
    cands = [candidate_record(inp, cells, i, "open_pen", po, n + 1, {"id": f"PO{po:02d}-{n + 1:02d}"})
             for n, i in enumerate(chosen)]

    counts = {REASONS[c]: int((cells["reason"] == c).sum()) for c in range(1, len(REASONS))
              if (cells["reason"] == c).any()}
    scored = cells["reason"] == 0
    stats = {
        "sea_cells_250m": int(len(cells["reason"])), "scored_cells_250m": int(scored.sum()),
        "excluded_cells_250m": counts,
        "mean_score": round(float(cells["score"][scored].mean()), 3) if scored.any() else 0.0,
        "max_score": round(float(cells["score"][scored].max()), 3) if scored.any() else 0.0,
        "temperature_score": round(inp.temp_score[po], 3), "candidates": len(cands),
    }
    return stats, cands, grid, (cells["x"], cells["y"])


def run_offshore(inp: Inputs, check: PublishedCheck) -> list[dict]:
    O = R.OFFSHORE
    latest: dict[int, dict] = {}
    order = [s for _, s in L.HTH_STAGES]
    for r in inp.offshore:
        aid = r["props"]["area_id"]
        if aid not in latest or order.index(r["stage"]) > order.index(latest[aid]["stage"]):
            latest[aid] = r
    out = []
    for aid in sorted(latest):
        r = latest[aid]
        sub, rs, cs = inp.g250.window(r["geom"].bounds)
        inside = rasterize([r["geom"]], sub)
        cells = score_cells(inp, sub, rs, cs, inside, None, O["depth_ramp_m"], open_pen=False)
        bonus = O["stage_bonus"][r["stage"]]
        cells["comp"] = {"depth": cells["comp"]["depth"]}
        cells["score"] = np.where(cells["reason"] == 0, cells["comp"]["depth"] * bonus * cells["river_factor"], 0.0)
        ok = (cells["reason"] == 0) & (cells["score"] >= O["min_score"]) & ~cells["near_poly"] \
            & (cells["dist"]["coral"] >= R.HARD["coral_reef"]["radius_m"] + 250)
        idx = np.nonzero(ok)[0]
        depth_off = np.abs(np.nan_to_num(cells["depth"][idx], nan=1e6) - O["ideal_depth_m"])
        order_ = idx[np.lexsort((cells["x"][idx], -cells["y"][idx], depth_off, -np.round(cells["score"][idx], 3)))]
        xy = np.column_stack([cells["x"], cells["y"]])
        chosen = pick(order_, xy, O["spacing_km"] * 1000, O["per_area"], lambda i, xy=xy: check.ok(xy[i, 0], xy[i, 1]))
        po = L.po_of_points(xy[chosen], inp.by_po, max_km=400.0) if chosen else []
        for n, i in enumerate(chosen):
            out.append(candidate_record(inp, cells, i, "offshore", po[n], n + 1, {
                "id": f"HTH{aid:02d}-{n + 1:02d}", "area": r["props"]["name"], "stage": r["stage"],
                "stage_factor": bonus,
                "binding": "depth" if cells["comp"]["depth"][i] < 1 else ("stage" if bonus < 1 else "none")}))
    return out


def main() -> None:
    inp = Inputs()
    check = PublishedCheck(inp)
    (OUT / "suitability").mkdir(parents=True, exist_ok=True)
    grids, sites, sizes, po_cells = {}, [], {}, {}
    for row in inp.po_rows:
        po = row["po"]
        stats, cands, (pub, rows), po_cells[po] = run_po(inp, row, check)
        name = f"suitability/po{po:02d}.png"
        sizes[name] = write_png(OUT / name, pub.w, pub.h, rows, color_type=2)
        grids[str(po)] = {"file": name, "origin_25833": [round(pub.x0), round(pub.y1)], "res_m": pub.res,
                          "width": pub.w, "height": pub.h, "stats": stats}
        sites += cands
        print(f"PO{po:2d}: {stats['scored_cells_250m']:7d} scored, mean {stats['mean_score']:.2f}, "
              f"{len(cands)} candidates, png {sizes[name] / 1024:.0f} KB")
    off = run_offshore(inp, check)
    sites += off
    print(f"offshore candidates: {len(off)}")
    copernicus = inp.clim is not None
    if copernicus:
        n = CL.main(po_cells, inp.sim_temp, {r["po"]: r["geom"].bounds for r in inp.po_rows})
        print(f"ocean_climatology.json {n / 1024:.0f} KB")
    else:
        stale = OUT / "ocean_climatology.json"
        if stale.exists():
            stale.unlink()

    fetched = load_fetch_manifest()
    doc = {
        "version": 1, "crs_grid": "EPSG:25833", "row0": "north",
        "encoding": {"R": "0 = no score; 1..255 = score 0..1 (score = (R - 1) / 254)",
                     "G": "reason code (codes)", "B": "mean depth / 4 m (0 = land or unknown)"},
        "codes": {**{str(i): r for i, r in enumerate(REASONS)},
                  **{str(v): f"scored, limited by {k}" for k, v in COMPONENT_CODE.items()}},
        "analysis_res_m": int(inp.g250.res), "grids": grids,
        "optional_layers": {k: R.weight(k, copernicus) > 0 for k in R.OPTIONAL},
    }
    write_json(OUT / "suitability.json", doc, compact=False)
    write_json(OUT / "suitability_rules.json", R.rules_document(copernicus), compact=False)
    sites.sort(key=lambda s: (s["type"] != "open_pen", s["po"] or 99, s["id"]))
    cand = {
        "version": 1, "random": False, "seed": None,
        "method": "Greedy maximum of the suitability score per production area (open pens) and per "
                  "Havbruk til havs area (offshore); ties break north first, then west. See "
                  "suitability_rules.json and docs/geo/GEODATA.md.",
        "inputs": {k: fetched[k]["fetched"] for k in sorted(fetched)},
        "content_sha256": canonical_hash(sites),
        "sites": sites,
    }
    write_json(OUT / "candidates.json", cand, compact=False)
    print(f"total sites {len(sites)}; png total {sum(sizes.values()) / 1024:.0f} KB")


if __name__ == "__main__":
    main()
