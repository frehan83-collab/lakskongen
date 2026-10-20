"""Raster helpers on the master grid (EPSG:25833, row 0 = north)."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any, Iterable

import numpy as np
import shapely
from PIL import Image, ImageDraw

from geo_common import WORK


@dataclass(frozen=True)
class Grid:
    """A north-up grid window: top-left corner (x0, y1), cell size res, shape (h, w)."""

    x0: float
    y1: float
    res: float
    h: int
    w: int

    def centres(self) -> tuple[np.ndarray, np.ndarray]:
        xs = self.x0 + (np.arange(self.w) + 0.5) * self.res
        ys = self.y1 - (np.arange(self.h) + 0.5) * self.res
        return xs, ys

    def cell_of(self, x: np.ndarray, y: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        c = np.floor((np.asarray(x) - self.x0) / self.res).astype(np.int64)
        r = np.floor((self.y1 - np.asarray(y)) / self.res).astype(np.int64)
        return r, c

    def window(self, bounds: tuple[float, float, float, float], margin: float = 0.0) -> tuple["Grid", slice, slice]:
        """Sub-grid covering bounds (+margin), snapped to this grid's cells."""
        bx0, by0, bx1, by1 = bounds
        c0 = max(0, int(np.floor((bx0 - margin - self.x0) / self.res)))
        c1 = min(self.w, int(np.ceil((bx1 + margin - self.x0) / self.res)))
        r0 = max(0, int(np.floor((self.y1 - (by1 + margin)) / self.res)))
        r1 = min(self.h, int(np.ceil((self.y1 - (by0 - margin)) / self.res)))
        g = Grid(self.x0 + c0 * self.res, self.y1 - r0 * self.res, self.res, r1 - r0, c1 - c0)
        return g, slice(r0, r1), slice(c0, c1)


def load_grids() -> tuple[dict[str, np.ndarray], dict, Grid, Grid]:
    meta = json.loads((WORK / "grids.json").read_text(encoding="utf-8"))
    data = dict(np.load(WORK / "grids.npz"))
    x0, y0, x1, y1 = meta["extent"]
    g250 = Grid(x0, y1, meta["res"], (y1 - y0) // meta["res"], (x1 - x0) // meta["res"])
    g125 = Grid(x0, y1, meta["res_class"], (y1 - y0) // meta["res_class"], (x1 - x0) // meta["res_class"])
    return data, meta, g250, g125


def rasterize(geoms: Iterable[Any], grid: Grid) -> np.ndarray:
    """Boolean mask of cells whose centre lies inside any of the (multi)polygons.

    Each polygon is drawn in its own window (exterior on, holes off) and OR-ed
    in, so overlapping polygons never erase each other.
    """
    mask = np.zeros((grid.h, grid.w), dtype=bool)
    for geom in geoms:
        if geom is None or geom.is_empty:
            continue
        for poly in shapely.get_parts(geom):
            if poly.geom_type != "Polygon":
                continue
            sub, rs, cs = grid.window(poly.bounds, margin=grid.res)
            if sub.h <= 0 or sub.w <= 0:
                continue
            img = Image.new("1", (sub.w, sub.h), 0)
            draw = ImageDraw.Draw(img)
            draw.polygon(_pixel_ring(np.asarray(poly.exterior.coords), sub), fill=1)
            for hole in poly.interiors:
                draw.polygon(_pixel_ring(np.asarray(hole.coords), sub), fill=0)
            mask[rs, cs] |= np.array(img, dtype=bool)
    return mask


def _pixel_ring(coords: np.ndarray, grid: Grid) -> list[tuple[float, float]]:
    """Map coordinates to PIL pixel coordinates (an integer is a pixel centre)."""
    px = (coords[:, 0] - grid.x0) / grid.res - 0.5
    py = (grid.y1 - coords[:, 1]) / grid.res - 0.5
    return list(zip(px.tolist(), py.tolist()))


def disk(radius_cells: int) -> np.ndarray:
    r = radius_cells
    yy, xx = np.mgrid[-r:r + 1, -r:r + 1]
    return (xx * xx + yy * yy <= r * r).astype(np.float64)
