#!/usr/bin/env python3
"""Optional step: Copernicus Marine climatologies for the suitability score.

Run with a Python that has the Copernicus Marine Toolbox (pinned in
tools/geodata/requirements-copernicus.txt), after `copernicusmarine login`:

    <copernicus-venv>/bin/python tools/geodata/fetch_copernicus.py

The toolbox reads the credentials it saved itself; this script only checks that
the credentials file exists and never reads it. Without it the script prints
what to do and exits 0, and the pipeline scores without these layers (their
weights are 0, i.e. neutral; see geo_rules.COMPONENTS).

Requests are small: the Norwegian coast box, the upper 30 m, monthly means.
  - ARCTIC_MULTIYEAR_PHY_002_003 (TOPAZ4 reanalysis, 0.125°): thetao, vxo, vyo, 2018-2022
  - ARCTIC_MULTIYEAR_BGC_002_005 (ECOSMO reanalysis, 0.25°): o2, 2018-2022
  - ARCTIC_MULTIYEAR_WAV_002_013 (WAM 3 km hindcast, monthly climatology): VHM0
Each product covers the whole coast including the Skagerrak (>= 50°N), so all
13 areas are scored from the same models. The NetCDF files go to the cache;
their sha256 goes to fetch_manifest.json; the reduction below writes
~/.cache/lakskongen-geo/work/copernicus_fields.npz for build_climatology.py.
"""

from __future__ import annotations

import datetime as dt
import json
import sys
from pathlib import Path

import numpy as np

from geo_common import FETCH_MANIFEST, RAW, WORK, load_fetch_manifest, sha256_file

CREDENTIALS = Path.home() / ".copernicusmarine" / ".copernicusmarine-credentials"
BBOX = {"minimum_longitude": 4.0, "maximum_longitude": 32.0, "minimum_latitude": 57.5, "maximum_latitude": 71.8}
DEPTH = {"minimum_depth": 0.0, "maximum_depth": 30.0}
PERIOD = {"start_datetime": "2018-01-01T00:00:00", "end_datetime": "2022-12-31T23:59:59"}

REQUESTS = (
    {"id": "cmems_arc_phy", "dataset_id": "cmems_mod_arc_phy_my_topaz4_P1M", "dataset_version": "202506",
     "variables": ["thetao", "vxo", "vyo"], **BBOX, **DEPTH, **PERIOD},
    {"id": "cmems_arc_bgc", "dataset_id": "cmems_mod_arc_bgc_my_ecosmo_P1M", "dataset_version": "202105",
     "variables": ["o2"], **BBOX, **DEPTH, **PERIOD},
    {"id": "cmems_arc_wav", "dataset_id": "cmems_mod_arc_wav_my_3km-climatology_P1M-m", "dataset_version": "202311",
     "variables": ["VHM0"], **BBOX, "start_datetime": "2005-01-01T00:00:00", "end_datetime": "2005-12-31T23:59:59"},
)

LOGIN_HINT = """Copernicus Marine credentials not found (~/.copernicusmarine/.copernicusmarine-credentials).
The owner creates the account and runs, once:
    <copernicus-venv>/bin/copernicusmarine login
then:
    <copernicus-venv>/bin/python tools/geodata/fetch_copernicus.py
    bash tools/geodata/run_pipeline.sh --build-only
Until then the suitability score runs without currents, waves and oxygen (neutral weights)."""


def fetch() -> dict[str, Path]:
    import copernicusmarine

    out: dict[str, Path] = {}
    manifest = load_fetch_manifest()
    for req in REQUESTS:
        rid = req["id"]
        dest = RAW / rid
        dest.mkdir(parents=True, exist_ok=True)
        name = f"{req['dataset_id']}_{req['dataset_version']}.nc"
        path = dest / name
        if not path.exists():
            args = {k: v for k, v in req.items() if k != "id"}
            copernicusmarine.subset(**args, output_directory=str(dest), output_filename=name,
                                    file_format="netcdf", netcdf_compression_level=1,
                                    disable_progress_bar=True, overwrite=True)
        sha = sha256_file(path)
        prev = manifest.get(rid)
        same = bool(prev) and prev["files"][0]["sha256"] == sha
        manifest[rid] = {
            "fetched": prev["fetched"] if same else dt.date.today().isoformat(),
            "url": f"copernicusmarine.subset(dataset_id={req['dataset_id']!r}, "
                   f"dataset_version={req['dataset_version']!r})",
            "kind": "copernicus_subset", "records": None,
            "request": {k: v for k, v in req.items() if k not in ("id",)},
            "files": [{"name": name, "url": "https://marine.copernicus.eu", "bytes": path.stat().st_size,
                       "sha256": sha}],
        }
        FETCH_MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=1) + "\n",
                                  encoding="utf-8")
        out[rid] = path
        print(f"{rid}: {path.name} {path.stat().st_size / 1e6:.1f} MB")
    return out


def reduce(paths: dict[str, Path]) -> None:
    """Monthly climatologies on each product's own lon/lat grid (row 0 = south as in the files)."""
    import xarray as xr

    fields: dict[str, np.ndarray] = {}
    with xr.open_dataset(paths["cmems_arc_phy"]) as ds:
        lat, lon = ds["latitude"].values, ds["longitude"].values
        shallow = ds.sel(depth=ds["depth"].values[ds["depth"].values <= 30.0])
        speed = np.sqrt(shallow["vxo"] ** 2 + shallow["vyo"] ** 2).mean("depth", skipna=True)
        sst = ds["thetao"].isel(depth=0)
        month = ds["time"].dt.month
        fields["phy_lat"], fields["phy_lon"] = lat.astype(np.float64), lon.astype(np.float64)
        fields["current_speed_monthly"] = speed.groupby(month).mean("time").values.astype(np.float32)
        fields["thetao_monthly"] = sst.groupby(month).mean("time").values.astype(np.float32)
        fields["thetao_depth_m"] = np.array([float(ds["depth"].values[0])])
    with xr.open_dataset(paths["cmems_arc_bgc"]) as ds:
        o2 = ds["o2"].sel(depth=ds["depth"].values[ds["depth"].values <= 30.0]).mean("depth", skipna=True)
        fields["bgc_lat"], fields["bgc_lon"] = ds["latitude"].values.astype(np.float64), ds["longitude"].values.astype(
            np.float64)
        fields["o2_monthly"] = o2.groupby(ds["time"].dt.month).mean("time").values.astype(np.float32)
    with xr.open_dataset(paths["cmems_arc_wav"]) as ds:
        fields["wav_lat"], fields["wav_lon"] = ds["latitude"].values.astype(np.float64), ds["longitude"].values.astype(
            np.float64)
        fields["hs_monthly"] = ds["VHM0"].groupby(ds["time"].dt.month).mean("time").values.astype(np.float32)
    WORK.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(WORK / "copernicus_fields.npz", **fields)
    print("copernicus_fields.npz:", {k: v.shape for k, v in fields.items()})


def main() -> int:
    if not CREDENTIALS.exists():
        print(LOGIN_HINT)
        return 0
    paths = fetch()
    reduce(paths)
    return 0


if __name__ == "__main__":
    sys.exit(main())
