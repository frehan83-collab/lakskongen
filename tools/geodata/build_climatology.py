#!/usr/bin/env python3
"""Per-PO ocean climatology from the Copernicus Marine fields (optional layers).

Input: ~/.cache/lakskongen-geo/work/copernicus_fields.npz (fetch_copernicus.py).
Output: godot/data/geo/ocean_climatology.json with, per production area,
  - monthly means over the area's coastal sea of current speed (upper 30 m),
    significant wave height and dissolved oxygen (upper 30 m), and Copernicus
    surface temperature beside the sim's own climatology (ProductionAreas.cs);
  - a 10 km grid of the annual values the suitability score uses.
The models are coarse (TOPAZ4 12.5 km, ECOSMO 25 km, WAM 3 km) and mask most
fjords as land: a fjord cell takes the value of the nearest model water cell.
When the npz is missing, available() is False and the score stays neutral.
"""

from __future__ import annotations

import numpy as np
from scipy import ndimage

from geo_common import OUT, WORK, write_json
from geo_vector import TO_LONLAT

FIELDS = WORK / "copernicus_fields.npz"
GRID_KM = 10
# (key in the npz, lat axis, lon axis, annual statistic, unit)
LAYERS = {
    "current_speed_ms": ("current_speed_monthly", "phy_lat", "phy_lon", "mean"),
    "hs_m": ("hs_monthly", "wav_lat", "wav_lon", "max"),
    "o2_mmol_m3": ("o2_monthly", "bgc_lat", "bgc_lon", "min"),
    "sst_c": ("thetao_monthly", "phy_lat", "phy_lon", "mean"),
}
COMPONENT_OF = {"current": "current_speed_ms", "waves": "hs_m", "oxygen": "o2_mmol_m3"}


def available() -> bool:
    return FIELDS.exists()


class Fields:
    """Monthly fields with NaNs filled from the nearest model water cell."""

    def __init__(self) -> None:
        raw = dict(np.load(FIELDS))
        self.layers = {}
        for name, (key, la, lo, stat) in LAYERS.items():
            a = raw[key].astype(np.float64)
            mask = np.isnan(a).all(axis=0)
            if mask.all():
                raise SystemExit(f"{name}: no valid data")
            _, (ri, ci) = ndimage.distance_transform_edt(mask, return_indices=True)
            filled = a[:, ri, ci]
            self.layers[name] = {"monthly": filled, "lat": raw[la], "lon": raw[lo], "stat": stat}

    def sample(self, x: np.ndarray, y: np.ndarray, monthly: bool = False) -> dict[str, np.ndarray]:
        """Values at EPSG:25833 points: annual statistic (or the 12 months)."""
        lon, lat = TO_LONLAT.transform(np.asarray(x, float), np.asarray(y, float))
        out = {}
        for name, L in self.layers.items():
            la, lo = L["lat"], L["lon"]
            row = (lat - la[0]) / (la[1] - la[0])
            col = (lon - lo[0]) / (lo[1] - lo[0])
            months = np.stack([ndimage.map_coordinates(m, [row, col], order=1, mode="nearest")
                               for m in L["monthly"]])
            if monthly:
                out[name] = months
            else:
                out[name] = {"mean": months.mean(axis=0), "max": months.max(axis=0),
                             "min": months.min(axis=0)}[L["stat"]]
        return out


def main(po_cells: dict[int, tuple[np.ndarray, np.ndarray]], sim_temp: dict[int, list[float]],
         po_bounds: dict[int, tuple[float, float, float, float]]) -> int:
    """Write ocean_climatology.json. po_cells: PO -> (x, y) of its scored sea cells."""
    f = Fields()
    doc = {
        "version": 1,
        "source": "Generated using E.U. Copernicus Marine Service Information (see LICENCES.md)",
        "period": {"physics_oxygen": "2018-2022 monthly means", "waves": "WAM 3 km monthly climatology"},
        "layers": {"current_speed_ms": "mean current speed, upper 30 m (TOPAZ4)",
                   "hs_m": "significant wave height (Arctic WAM 3 km)",
                   "o2_mmol_m3": "dissolved oxygen, upper 30 m (ECOSMO)",
                   "sst_c": "temperature, top model level (TOPAZ4)",
                   "sim_sea_temp_c": "the game's own climatology at 3 m (ProductionAreas.cs)"},
        "grid": {"crs": "EPSG:25833", "res_m": GRID_KM * 1000, "row0": "north",
                 "values": "annual statistic used by the score (current: mean, hs: highest month, o2: lowest month); "
                           "null outside the area's sea"},
        "areas": {},
    }
    for po in sorted(po_cells):
        xs, ys = po_cells[po]
        if len(xs) == 0:
            continue
        step = max(1, len(xs) // 4000)
        mon = f.sample(xs[::step], ys[::step], monthly=True)
        series = {k: [round(float(v), 3) for v in mon[k].mean(axis=1)] for k in LAYERS}
        series["sim_sea_temp_c"] = sim_temp[po]
        x0, y0, x1, y1 = po_bounds[po]
        res = GRID_KM * 1000
        gx0, gy1 = np.floor(x0 / res) * res, np.ceil(y1 / res) * res
        w, h = int(np.ceil((x1 - gx0) / res)), int(np.ceil((gy1 - y0) / res))
        c = np.floor((xs - gx0) / res).astype(int)
        r = np.floor((gy1 - ys) / res).astype(int)
        has = np.zeros((h, w), bool)
        has[r, c] = True
        rr, cc = np.nonzero(has)
        cx, cy = gx0 + (cc + 0.5) * res, gy1 - (rr + 0.5) * res
        ann = f.sample(cx, cy)
        grids = {}
        for k, scale in (("current_speed_ms", 3), ("hs_m", 2), ("o2_mmol_m3", 0)):
            g = [[None] * w for _ in range(h)]
            for i in range(len(rr)):
                v = round(float(ann[k][i]), scale)
                g[rr[i]][cc[i]] = int(v) if scale == 0 else v
            grids[k] = g
        doc["areas"][str(po)] = {"monthly": series, "grid": {"origin_25833": [int(gx0), int(gy1)], "width": w,
                                                              "height": h, **grids}}
    return write_json(OUT / "ocean_climatology.json", doc)
