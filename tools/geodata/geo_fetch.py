#!/usr/bin/env python3
"""Fetch the raw geo sources into the cache and record what was fetched.

Usage:
    python tools/geodata/geo_fetch.py                 # fetch every missing source
    python tools/geodata/geo_fetch.py fdir_localities # fetch named sources
    python tools/geodata/geo_fetch.py --force ...     # re-fetch even if cached

Every endpoint is pinned in geo_sources.py. Raw files land in
~/.cache/lakskongen-geo/raw/<source id>/ (outside the repo). The fetch date,
URL, byte size and sha256 of every file go to tools/geodata/fetch_manifest.json,
which is committed: it is the record of exactly which data the shipped files
were built from. HTTP goes through curl so the system trust store applies.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
import urllib.parse
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from geo_common import FETCH_MANIFEST, RAW, curl_download, curl_text, load_fetch_manifest, sha256_file
from geo_sources import SOURCES, Source

# Extent of the bathymetry tiles (EPSG:25833, metres): Norway's coast, the
# production areas and the Havbruk til havs areas with a margin (from the
# fetched vector layers, rounded out to whole tiles).
WCS_EXTENT = (-250_000, 6_400_000, 1_250_000, 8_150_000)


def _arcgis_query(src: Source, dest: Path) -> tuple[list[dict], int]:
    meta_text = curl_text(f"{src.url}?f=json")
    meta = json.loads(meta_text)
    (dest / "layer.json").write_text(meta_text, encoding="utf-8")
    oid = meta.get("objectIdField") or next(
        f["name"] for f in meta["fields"] if f["type"] == "esriFieldTypeOID")
    page = int(src.params.get("page", 2000))
    features: list[dict] = []
    offset = 0
    while True:
        q = {
            "where": "1=1",
            "outFields": src.params.get("out_fields", "*"),
            "outSR": "25833",
            "orderByFields": oid,
            "resultOffset": offset,
            "resultRecordCount": page,
            "f": "geojson",
        }
        if "max_allowable_offset" in src.params:
            q["maxAllowableOffset"] = src.params["max_allowable_offset"]
        data = json.loads(curl_text(f"{src.url}/query?{urllib.parse.urlencode(q)}", timeout_s=600))
        if "error" in data:
            raise SystemExit(f"{src.id}: {data['error']}")
        batch = data.get("features", [])
        features.extend(batch)
        if len(batch) < page and not data.get("exceededTransferLimit") and not (
                data.get("properties") or {}).get("exceededTransferLimit"):
            break
        if not batch:
            break
        offset += len(batch)
    fc = {"type": "FeatureCollection", "crs": "EPSG:25833", "features": features}
    (dest / "features.geojson").write_text(json.dumps(fc, ensure_ascii=False), encoding="utf-8")
    return [{"name": "layer.json", "url": f"{src.url}?f=json"},
            {"name": "features.geojson", "url": f"{src.url}/query"}], len(features)


def _download(src: Source, dest: Path) -> tuple[list[dict], int | None]:
    name = src.url.rsplit("/", 1)[-1]
    curl_download(src.url, dest / name)
    return [{"name": name, "url": src.url}], None


def _download_list(src: Source, dest: Path) -> tuple[list[dict], int | None]:
    feed = curl_text(src.url)
    urls = sorted(set(re.findall(src.params["pattern"], feed)))
    if not urls:
        raise SystemExit(f"{src.id}: no files matched in {src.url}")

    def one(u: str) -> dict:
        name = u.rsplit("/", 1)[-1]
        if not (dest / name).exists():
            curl_download(u, dest / name)
        return {"name": name, "url": u}

    with ThreadPoolExecutor(max_workers=6) as pool:
        files = list(pool.map(one, urls))
    return files, len(files)


def _wcs_tiles(src: Source, dest: Path) -> tuple[list[dict], int]:
    p = src.params
    res, tile = int(p["res_m"]), int(p["tile_m"])
    x0, y0, x1, y1 = WCS_EXTENT
    jobs = []
    for ty in range(y0, y1, tile):
        for tx in range(x0, x1, tile):
            n = tile // res
            q = {
                "service": "WCS", "version": "1.0.0", "request": "GetCoverage",
                "coverage": p["coverage"], "crs": p["crs"],
                "bbox": f"{tx},{ty},{tx + tile},{ty + tile}",
                "width": n, "height": n, "format": p.get("format", "GTiff"),
                **p.get("extra", {}),
            }
            name = f"bathy_{tx}_{ty}_{res}m.tif"
            jobs.append((name, f"{src.url}?{urllib.parse.urlencode(q)}"))

    def one(job: tuple[str, str]) -> dict:
        name, url = job
        path = dest / name
        if not path.exists():
            curl_download(url, path, timeout_s=300)
        with path.open("rb") as f:
            magic = f.read(4)
        if magic not in (b"II*\x00", b"MM\x00*"):
            path.unlink()
            raise SystemExit(f"{src.id}: {name} is not a GeoTIFF (WCS exception?): {url}")
        return {"name": name, "url": url}

    with ThreadPoolExecutor(max_workers=4) as pool:
        files = list(pool.map(one, jobs))
    return files, len(files)


def _wcs_lonlat_tiles(src: Source, dest: Path) -> tuple[list[dict], int]:
    """WCS 1.0.0 tiles in the coverage's native EPSG:4326 (no server-side reprojection)."""
    p = src.params
    lon0, lat0, lon1, lat1 = p["bbox"]
    dlon, dlat = p["tile_deg"]
    rx, ry = p["res_deg"]
    jobs = []
    lat = lat0
    while lat < lat1 - 1e-9:
        lon = lon0
        while lon < lon1 - 1e-9:
            q = {
                "service": "WCS", "version": "1.0.0", "request": "GetCoverage",
                "coverage": p["coverage"], "crs": "EPSG:4326",
                "bbox": f"{lon:.4f},{lat:.4f},{lon + dlon:.4f},{lat + dlat:.4f}",
                "width": int(round(dlon / rx)), "height": int(round(dlat / ry)),
                "format": p.get("format", "image/tiff"),
            }
            jobs.append((f"emod_{lon:+08.3f}_{lat:+07.3f}.tif", f"{src.url}?{urllib.parse.urlencode(q)}"))
            lon += dlon
        lat += dlat

    def one(job: tuple[str, str]) -> dict:
        name, url = job
        path = dest / name
        if not path.exists():
            curl_download(url, path, timeout_s=300)
        with path.open("rb") as f:
            magic = f.read(4)
        if magic not in (b"II*\x00", b"MM\x00*"):
            path.unlink()
            raise SystemExit(f"{src.id}: {name} is not a GeoTIFF (WCS exception?): {url}")
        return {"name": name, "url": url}

    with ThreadPoolExecutor(max_workers=4) as pool:
        files = list(pool.map(one, jobs))
    return files, len(files)


HANDLERS = {"arcgis": _arcgis_query, "download": _download, "download_list": _download_list,
            "wcs_tiles": _wcs_tiles, "wcs_lonlat_tiles": _wcs_lonlat_tiles}


def _is_cached(entry: dict | None, source_id: str) -> bool:
    if not entry:
        return False
    return all((RAW / source_id / f["name"]).exists() for f in entry["files"])


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("ids", nargs="*", help="source ids (default: all fetchable)")
    ap.add_argument("--force", action="store_true", help="re-fetch cached sources")
    args = ap.parse_args(argv)

    manifest = load_fetch_manifest()
    wanted = [s for s in SOURCES if s.kind in HANDLERS and (not args.ids or s.id in args.ids)]
    unknown = set(args.ids) - {s.id for s in SOURCES}
    if unknown:
        raise SystemExit(f"unknown source ids: {sorted(unknown)}")
    today = dt.date.today().isoformat()
    for src in wanted:
        if not args.force and _is_cached(manifest.get(src.id), src.id):
            print(f"{src.id}: cached ({manifest[src.id]['fetched']})")
            continue
        dest = RAW / src.id
        dest.mkdir(parents=True, exist_ok=True)
        print(f"{src.id}: fetching {src.url}")
        files, records = HANDLERS[src.kind](src, dest)
        for f in files:
            path = dest / f["name"]
            f["bytes"] = path.stat().st_size
            f["sha256"] = sha256_file(path)
        manifest[src.id] = {"fetched": today, "url": src.url, "kind": src.kind,
                            "records": records, "files": files}
        FETCH_MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=1) + "\n",
                                  encoding="utf-8")
        total = sum(f["bytes"] for f in files)
        print(f"{src.id}: {len(files)} file(s), {total / 1e6:.1f} MB, records={records}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
