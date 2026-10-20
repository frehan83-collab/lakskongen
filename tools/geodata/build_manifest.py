#!/usr/bin/env python3
"""Write godot/data/geo/LICENCES.md and godot/data/geo/manifest.json (last step).

LICENCES.md lists every shipped layer with its sources, licence, URL, fetch
date and attribution, generated from geo_sources.py and fetch_manifest.json.
manifest.json lists every file with its size, sha256, layer, format and
sources, the CRS conventions, reference points for the game's UTM 33
projection, and the size budget. No wall-clock time is written, so the same
cache always builds the same bytes.
"""

from __future__ import annotations

import hashlib

from pyproj import Transformer

from geo_common import OUT, load_fetch_manifest, write_json
from geo_sources import BY_ID

SIZE_BUDGET_BYTES = 8_000_000
COPERNICUS = ("cmems_arc_phy", "cmems_arc_bgc", "cmems_arc_wav")
BASE_SOURCES = ["kv_n250", "kv_dtm50", "kv_bathymetry", "emodnet_bathymetry"]
SUIT_SOURCES = ["fdir_localities", "fdir_salmon_fjords", "fdir_coral_closures", "hi_coral_reefs",
                "mdir_protected_areas", "mdir_anadromous_outlets", "lovdata_salmon_rivers", "kyv_fairway_lines",
                "kv_n250", "kv_bathymetry", "emodnet_bathymetry", "sim_po_temperature", "fdir_production_areas"]

# Shipped file (or glob prefix) -> (layer description, format, sources).
LAYERS = [
    ("production_areas.national.geojson", "Production areas PO1-PO13, national level of detail", "GeoJSON",
     ["fdir_production_areas", "sim_po_temperature"]),
    ("production_areas.regional.geojson", "Production areas PO1-PO13, regional level of detail", "GeoJSON",
     ["fdir_production_areas", "sim_po_temperature"]),
    ("coastline.national.geojson", "Norway's land outline for the national overview", "GeoJSON", ["kv_n1000"]),
    ("localities.geojson", "Active aquaculture localities (no owner fields, D17)", "GeoJSON",
     ["fdir_localities", "kv_n250"]),
    ("locality_areas.geojson", "Registered locality area polygons", "GeoJSON", ["fdir_locality_areas"]),
    ("mooring_areas.geojson", "NYTEK installation and mooring areas", "GeoJSON", ["fdir_mooring_areas"]),
    ("offshore_areas.national.geojson", "Havbruk til havs areas by stage, national", "GeoJSON",
     ["fdir_hth_2019", "fdir_hth_2022", "fdir_hth_2024", "fdir_hth_2026"]),
    ("offshore_areas.regional.geojson", "Havbruk til havs areas by stage, regional", "GeoJSON",
     ["fdir_hth_2019", "fdir_hth_2022", "fdir_hth_2024", "fdir_hth_2026"]),
    ("exclusions.national.geojson", "Salmon fjords, marine protected areas, coral closures (national)", "GeoJSON",
     ["fdir_salmon_fjords", "mdir_protected_areas", "fdir_coral_closures"]),
    ("exclusions.regional.geojson", "Salmon fjords, marine protected areas, coral closures (regional)", "GeoJSON",
     ["fdir_salmon_fjords", "mdir_protected_areas", "fdir_coral_closures"]),
    ("fairways.national.geojson", "Main fairways (hovedled)", "GeoJSON", ["kyv_fairway_lines"]),
    ("fairways.regional.geojson", "Main and secondary fairways with exclusion buffers", "GeoJSON",
     ["kyv_fairway_lines"]),
    ("constraint_points.geojson", "River mouths (national salmon rivers, other anadromous) and coral reefs",
     "GeoJSON", ["mdir_anadromous_outlets", "lovdata_salmon_rivers", "hi_coral_reefs"]),
    ("basemap/national.jpg", "Stylised national base map", "JPEG", BASE_SOURCES),
    ("basemap/po", "Stylised base map per production area", "JPEG", BASE_SOURCES),
    ("basemap/po_index.national.png", "Production area per sea pixel of the national base map", "PNG (8-bit grey)",
     ["fdir_production_areas", "kv_n250"]),
    ("basemap.json", "Georeference of the base maps", "JSON", []),
    ("suitability/po", "Suitability grid per production area (500 m)", "PNG (RGB)", SUIT_SOURCES),
    ("suitability.json", "Suitability grid georeference, codes and statistics", "JSON", SUIT_SOURCES),
    ("suitability_rules.json", "Scoring rules: weights, thresholds and their sources", "JSON", []),
    ("candidates.json", "Candidate new sites (open pens and offshore)", "JSON",
     SUIT_SOURCES + ["fdir_hth_2019", "fdir_hth_2022", "fdir_hth_2024", "fdir_hth_2026"]),
    ("ocean_climatology.json", "Per-PO ocean climatology (currents, waves, oxygen, temperature)", "JSON",
     list(COPERNICUS) + ["sim_po_temperature"]),
    ("LICENCES.md", "This licence record", "Markdown", []),
]

PROJECTION_CHECK = [(5.3221, 60.3913), (10.3951, 63.4305), (14.4049, 67.2804), (18.9553, 69.6492),
                    (29.7417, 70.0742), (8.0000, 58.1500)]

NOT_USED = [
    ("Kartverket Sentinel-2 'Skyfritt opptak Norge' (2025)", "CC BY 4.0 with the attribution 'Laget av "
     "Sentinel-2 (2025) bilder fra Copernicus-programmet til EU, bearbeidet av Kartverket, basert på metoder "
     "utviklet fra CESBIO og CNES': commercial use is allowed. Not used: it is distributed only as 84 cells of "
     "10 m GeoTIFF (many GB), and a photographic base reads as a GIS look against the Kyst & Stål art brief; "
     "the stylised base is built from N250 land cover and DTM 50 relief instead."),
    ("Geovekst 'Ortofoto Sentinel-2 Skyfri mosaikk'", "Norge digitalt-lisens (restricted to Norge digitalt "
     "parties): not usable in a commercial game."),
    ("Kartverket 'Høyde DTM Sømløs WCS'", "Norge digitalt-lisens: not usable; the open DTM 50 (CC BY 4.0) is "
     "used for relief instead."),
    ("Kartverket 'Sjøkart - Dybdedata' (FGDB)", "CC BY 4.0, but 2.6 GB; the same agency's depth grid is "
     "fetched from its WCS at 250 m instead."),
    ("Kystverket 'Hovedled og biled, arealavgrensning'", "NLOD 2.0, fetched and checked: 211 of 1050 active "
     "sea salmon sites lie inside these corridor polygons, so they are not exclusion zones; the buffered "
     "centre lines are used."),
    ("MET Norway NorKyst-800", "Not needed: the Copernicus Marine physics reanalysis (owner's login) gives "
     "the current layer; NorKyst would need large OPeNDAP pulls for a climatology."),
    ("BarentsWatch Fishhealth API", "Needs an API client registration (credentials): not used. The "
     "production-area boundaries come from Fiskeridirektoratet's open service instead."),
]


def sha256(path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def layer_of(rel: str):
    for key, desc, fmt, srcs in LAYERS:
        if rel == key or (key.endswith("/po") and rel.startswith(key)):
            return desc, fmt, srcs
    raise SystemExit(f"{rel}: no layer record in build_manifest.LAYERS (every shipped file needs one)")


def licences_md(files: dict, fetched: dict) -> str:
    used = sorted({s for f in files.values() for s in f["sources"]}, key=lambda s: list(BY_ID).index(s))
    out = [
        "# Map data licences (godot/data/geo)",
        "",
        "Generated by `tools/geodata/build_manifest.py` from `tools/geodata/geo_sources.py` and",
        "`tools/geodata/fetch_manifest.json`; do not edit by hand. Every licence below allows use in a",
        "commercial game with attribution. `docs/geo/GEODATA.md` documents the layers and the pipeline.",
        "",
        "## Sources",
        "",
        "| Source id | Dataset | Owner | Licence | Fetched | URL | Attribution |",
        "|---|---|---|---|---|---|---|",
    ]
    for sid in used:
        s = BY_ID[sid]
        when = fetched.get(sid, {}).get("fetched", "derived")
        lic = f"[{s.licence}]({s.licence_url})" if s.licence_url else s.licence
        out.append(f"| `{sid}` | {s.title} | {s.owner} | {lic} | {when} | "
                   f"{s.url} | {s.attribution} |")
    out += ["", "## Layers", "", "| File | Layer | Format | Sources | Licences |", "|---|---|---|---|---|"]
    for rel in sorted(files):
        f = files[rel]
        lic = sorted({BY_ID[s].licence for s in f["sources"]})
        out.append(f"| `{rel}` | {f['layer']} | {f['format']} | "
                   f"{', '.join(f'`{s}`' for s in f['sources']) or 'derived (no third-party data)'} | "
                   f"{', '.join(lic) or 'n/a'} |")
    out += ["", "## Attribution shown in the game", ""]
    seen = []
    for sid in used:
        a = BY_ID[sid].attribution
        owner_line = f"- {a} ({BY_ID[sid].licence})"
        if owner_line not in seen:
            seen.append(owner_line)
    out += seen
    out += ["", "## Licence notes", ""]
    for sid in used:
        if BY_ID[sid].note:
            out.append(f"- `{sid}`: {BY_ID[sid].note} Metadata: {BY_ID[sid].metadata_url}")
    out += ["", "## Considered and not used", ""]
    out += [f"- **{name}**: {why}" for name, why in NOT_USED]
    return "\n".join(out) + "\n"


def main() -> None:
    fetched = load_fetch_manifest()
    files = {}
    for path in sorted(OUT.rglob("*")):
        rel = path.relative_to(OUT).as_posix()
        if path.is_dir() or rel in ("manifest.json", "LICENCES.md") or path.name.startswith("."):
            continue
        if path.suffix == ".import":
            continue
        desc, fmt, srcs = layer_of(rel)
        files[rel] = {"layer": desc, "format": fmt, "sources": srcs}
    md = licences_md(files, fetched)
    (OUT / "LICENCES.md").write_text(md, encoding="utf-8")
    files["LICENCES.md"] = {"layer": "Licence record", "format": "Markdown", "sources": []}
    for rel, f in files.items():
        p = OUT / rel
        f["bytes"] = p.stat().st_size
        f["sha256"] = sha256(p)
    tr = Transformer.from_crs("EPSG:4326", "EPSG:25833", always_xy=True)
    check = []
    for lon, lat in PROJECTION_CHECK:
        x, y = tr.transform(lon, lat)
        check.append({"lon": lon, "lat": lat, "x": round(x, 3), "y": round(y, 3)})
    total = sum(f["bytes"] for f in files.values())
    used = sorted({s for f in files.values() for s in f["sources"]})
    doc = {
        "version": 1,
        "crs": {"display": "EPSG:4326 (lon, lat) in every GeoJSON", "metric": "EPSG:25833 (ETRS89 / UTM 33N), "
                "metres: x/y on points, grids and base maps, row 0 = north"},
        "projection_check_25833": check,
        "size_budget_bytes": SIZE_BUDGET_BYTES,
        "total_bytes": total,
        "files": files,
        "sources": {s: {"title": BY_ID[s].title, "owner": BY_ID[s].owner, "licence": BY_ID[s].licence,
                        "attribution": BY_ID[s].attribution,
                        "fetched": fetched.get(s, {}).get("fetched", "derived")} for s in used},
        "supersedes": {
            "godot/data/norway_coastline.json": "basemap/national.jpg + coastline.national.geojson",
            "MapPanel.gd REGIONS[].pos/lonlat": "production_areas.*.geojson properties.label / label_25833",
        },
    }
    write_json(OUT / "manifest.json", doc, compact=False)
    print(f"{len(files)} files, {total / 1e6:.2f} MB of {SIZE_BUDGET_BYTES / 1e6:.0f} MB budget")
    if total > SIZE_BUDGET_BYTES:
        raise SystemExit("over the size budget")


if __name__ == "__main__":
    main()
