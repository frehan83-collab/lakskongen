"""Geometry helpers: projection, simplification per level of detail, GeoJSON encoding."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Iterable

import numpy as np
import pyogrio
import shapely
from pyproj import Transformer
from shapely.geometry import shape
from shapely.geometry.polygon import orient

from geo_common import RAW, SIMPLIFY_M, raw_files, round_coords

TO_LONLAT = Transformer.from_crs("EPSG:25833", "EPSG:4326", always_xy=True)
TO_UTM = Transformer.from_crs("EPSG:4326", "EPSG:25833", always_xy=True)

# Polygon parts smaller than this are dropped per level of detail (km²).
MIN_PART_KM2 = {"national": 4.0, "regional": 0.01}
# Holes (islands) smaller than this are filled per level of detail (km²).
MIN_HOLE_KM2 = {"national": 4.0, "regional": 0.05}


def utm_to_lonlat_xy(xy: np.ndarray) -> np.ndarray:
    lon, lat = TO_LONLAT.transform(xy[:, 0], xy[:, 1])
    return np.column_stack([lon, lat])


def lonlat_to_utm(lon: float, lat: float) -> tuple[float, float]:
    x, y = TO_UTM.transform(lon, lat)
    return float(x), float(y)


def arcgis_features(source_id: str) -> list[dict]:
    """Features of an ArcGIS source in EPSG:25833 (as fetched)."""
    path = next(p for p in raw_files(source_id) if p.name == "features.geojson")
    return json.loads(path.read_text(encoding="utf-8"))["features"]


def arcgis_geoms(features: list[dict]) -> list[Any]:
    return [shape(f["geometry"]) if f.get("geometry") else None for f in features]


def read_zip_layer(source_id: str, layer: str | None = None, columns: list[str] | None = None):
    """(geometries, {column: values}) of an OGR layer inside a downloaded zip."""
    import zipfile

    (zpath,) = raw_files(source_id)
    inner = zipfile.ZipFile(zpath).namelist()[0].split("/")[0]
    path = f"/vsizip/{zpath}/{inner}"
    meta, _, wkb, fields = pyogrio.raw.read(path, layer=layer, columns=columns)
    cols = {name: fields[i] for i, name in enumerate(meta["fields"])}
    return shapely.from_wkb(wkb), cols


def simplify_polygonal(geom: Any, lod: str, tolerance_m: float | None = None,
                       min_hole_km2: float | None = None) -> Any | None:
    """Simplify a (multi)polygon in metres; drop parts and holes below the LOD's minimum area.

    Dropping a hole only ever grows the published polygon, so a point outside
    the published polygon is also outside the source one.
    """
    if geom is None or geom.is_empty:
        return None
    g = shapely.make_valid(geom) if not geom.is_valid else geom
    g = shapely.simplify(g, tolerance_m or SIMPLIFY_M[lod], preserve_topology=True)
    parts = [p for p in shapely.get_parts(g) if p.geom_type == "Polygon"]
    min_area = MIN_PART_KM2[lod] * 1e6
    min_hole = (MIN_HOLE_KM2[lod] if min_hole_km2 is None else min_hole_km2) * 1e6
    parts = [shapely.Polygon(p.exterior, [r for r in p.interiors if shapely.Polygon(r).area >= min_hole])
             for p in parts if p.area >= min_area]
    if not parts:
        return None
    return shapely.MultiPolygon(parts) if len(parts) > 1 else parts[0]


def simplify_lines(geom: Any, tolerance_m: float) -> Any | None:
    if geom is None or geom.is_empty:
        return None
    g = shapely.simplify(geom, tolerance_m, preserve_topology=False)
    parts = [p for p in shapely.get_parts(g) if p.geom_type == "LineString" and p.length > 0]
    if not parts:
        return None
    return shapely.MultiLineString(parts) if len(parts) > 1 else parts[0]


def to_geojson_geometry(geom: Any, decimals: int) -> dict:
    """EPSG:25833 shapely geometry to a lon/lat GeoJSON geometry (RFC 7946 ring order)."""
    if geom.geom_type in ("Polygon", "MultiPolygon"):
        polys = [orient(p, sign=1.0) for p in shapely.get_parts(geom)]
        geom = shapely.MultiPolygon(polys) if len(polys) > 1 else polys[0]
    ll = shapely.transform(geom, utm_to_lonlat_xy)
    gj = json.loads(shapely.to_geojson(ll))
    gj["coordinates"] = round_coords(gj["coordinates"], decimals)
    return _dedupe_rings(gj)


def _dedupe_rings(gj: dict) -> dict:
    """Remove consecutive duplicate vertices created by rounding; keep rings closed."""

    def ring(r: list) -> list:
        out = [r[0]]
        for p in r[1:]:
            if p != out[-1]:
                out.append(p)
        if out[0] != out[-1]:
            out.append(out[0])
        return out

    t = gj["type"]
    if t == "Polygon":
        rings = [ring(r) for r in gj["coordinates"]]
        gj["coordinates"] = [r for r in rings if len(r) >= 4]
    elif t == "MultiPolygon":
        polys = []
        for poly in gj["coordinates"]:
            rings = [ring(r) for r in poly]
            if len(rings[0]) >= 4:
                polys.append([r for r in rings if len(r) >= 4])
        gj["coordinates"] = polys
    return gj


def feature(geom: dict, props: dict) -> dict:
    return {"type": "Feature", "properties": props, "geometry": geom}


def collection(features: Iterable[dict], meta: dict) -> dict:
    return {"type": "FeatureCollection", "meta": meta, "features": list(features)}


def raw_path(source_id: str) -> Path:
    return RAW / source_id
