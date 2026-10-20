#!/usr/bin/env python3
"""Build the vector layers in godot/data/geo/ from the cached raw sources.

Writes (lon/lat EPSG:4326; point layers also carry EPSG:25833 x/y in metres):
  production_areas.{national,regional}.geojson  PO1-PO13
  coastline.national.geojson                    Norway's land (N1000), national overview
  localities.geojson                            active aquaculture sites (no owner fields, D17)
  locality_areas.geojson                        their registered area polygons
  mooring_areas.geojson                         NYTEK installation / mooring areas
  offshore_areas.{national,regional}.geojson    Havbruk til havs areas, by stage
  exclusions.{national,regional}.geojson        salmon fjords, protected areas, coral closures
  fairways.{national,regional}.geojson          hovedled / biled centre lines
  constraint_points.geojson                     river mouths and coral reefs (with radii)
"""

from __future__ import annotations

import collections
import re
import zipfile

import numpy as np
import pyogrio
import shapely

import geo_rules as R
from geo_common import LONLAT_DECIMALS, OUT, raw_files, write_json
from geo_sim import production_area_table
from geo_vector import (arcgis_features, arcgis_geoms, collection, feature, read_zip_layer,
                        simplify_lines, simplify_polygonal, to_geojson_geometry, utm_to_lonlat_xy)

LEGAL_FORMS = {"AS", "ASA", "SA", "ANS", "DA", "KS", "BA", "SE", "AB", "NUF", "LTD", "HF", "IKS"}
ROMAN = re.compile(r"^(I|II|III|IV|V|VI|VII|VIII|IX|X|XI|XII)$")

# Salmon-fjord polygons trace the whole fjord coastline; at the regional level
# their outline is drawn at 300 m (the suitability grid carries the exact zone).
EXCLUSION_TOLERANCE_M = {("regional", "salmon_fjord"): 300.0}
EXCLUSION_MIN_HOLE_KM2 = {("regional", "salmon_fjord"): 0.5}

# Light production-area tints for the national map (drawn over the sea only).
# Four hues alternate so neighbouring areas always differ.
PO_TINTS = ["#CFE3EC", "#D6E6D2", "#E9E2CC", "#DCD8EC"]

SPECIES_GROUPS = (
    ("salmonid", ("laks", "regnbueørret", "ørret", "røye")),
    ("algae", ("tare", "tang", "søl", "alge", "salat", "rødkluft", "draugfjær")),
    ("shellfish", ("skjell", "østers", "hummer", "kreps", "bolle", "pølse", "krabbe", "snegl", "sjøpung")),
    ("marine_fish", ("torsk", "kveite", "rognkjeks", "gylt", "bergnebb", "leppefisk", "piggvar", "tunge",
                     "steinbit", "hyse", "sei", "lysing", "breiflabb", "berggylt", "grønngylt")),
)
GROUP_PRIORITY = ["salmonid", "marine_fish", "shellfish", "algae", "other"]
STAGES = {"Matfisk": "growout", "Settefisk": "smolt", "Stamfisk": "broodstock", "Havbeite": "ranching",
          "Akvakulturdyr til konsum": "growout", "Alger til konsum": "growout",
          "Alger tidlig fase": "nursery", "Akvakulturdyr til råstoff": "growout"}
UNITS = {"TN": "t", "STK": "pcs", "KG": "kg", "M3": "m3", "M2": "m2", "DA": "daa"}
PLACEMENT = {"SJØ": "sea", "LAND": "land", "HAV": "offshore"}
HTH_STAGES = (("fdir_hth_2019", "tilradning_2019"), ("fdir_hth_2022", "anbefaling_2022"),
              ("fdir_hth_2024", "konsekvensvurdering_2024"), ("fdir_hth_2026", "kan_utlyses_2026"))

# Old-county latitude bands (°N) used to tell same-named rivers apart.
COUNTY_LAT = {"Østfold": (58.7, 59.9), "Vestfold": (58.8, 60.3), "Vest-Agder": (57.9, 59.4),
              "Rogaland": (58.2, 59.9), "Hordaland": (59.4, 61.0), "Sogn og Fjordane": (60.8, 62.4),
              "Møre og Romsdal": (61.9, 63.7), "Sør-Trøndelag": (62.2, 64.4),
              "Nord-Trøndelag": (63.5, 65.6), "Nordland": (64.9, 69.4), "Troms": (68.3, 70.4),
              "Finnmark": (69.0, 71.3)}


def norm(s: str) -> str:
    return re.sub(r"[^a-zæøåäö0-9]", "", s.lower())


# --------------------------------------------------------------------------
# Production areas


def production_areas() -> tuple[list[dict], dict[int, object]]:
    table = production_area_table()
    feats = arcgis_features("fdir_production_areas")
    geoms = arcgis_geoms(feats)
    out, by_po = [], {}
    for f, g in zip(feats, geoms):
        po = int(f["properties"]["id"])
        by_po[po] = shapely.make_valid(g)
        out.append({"po": po, "props": f["properties"], "geom": by_po[po], "table": table[po]})
    if sorted(by_po) != list(range(1, 14)):
        raise SystemExit(f"expected PO1-PO13, got {sorted(by_po)}")
    return sorted(out, key=lambda r: r["po"]), by_po


def po_features(rows: list[dict], lod: str, labels: dict[int, tuple[float, float]] | None) -> list[dict]:
    feats = []
    for r in rows:
        po, g, p = r["po"], r["geom"], r["props"]
        sg = simplify_polygonal(g, lod) or g
        x0, y0, x1, y1 = (round(v) for v in g.bounds)
        props = {
            "id": po, "code": f"PO{po}", "name": r["table"]["name"], "source_name": p["name"],
            "traffic_light": {"grønn": "green", "gul": "yellow", "rød": "red"}.get(p.get("status"), None),
            "area_km2": int(p["areal"]), "sea_area_km2": int(p["sjoareal"]),
            "bbox_25833": [x0, y0, x1, y1],
            "tint": PO_TINTS[(po - 1) % len(PO_TINTS)],
            "sea_temp_monthly_c": r["table"]["sea_temp_monthly_c"],
        }
        if labels and po in labels:
            lx, ly = labels[po]
            ll = utm_to_lonlat_xy(np.array([[lx, ly]]))[0]
            props["label"] = [round(float(ll[0]), 4), round(float(ll[1]), 4)]
            props["label_25833"] = [round(lx), round(ly)]
        feats.append(feature(to_geojson_geometry(sg, LONLAT_DECIMALS[lod]), props))
    return feats


def po_labels(by_po: dict[int, object]) -> dict[int, tuple[float, float]]:
    """Label anchor per PO: the centroid of its coastal sea cells (<= 200 m deep),
    snapped to the nearest such cell so the label always sits on its own water."""
    from geo_raster import load_grids, rasterize

    data, _, g250, _ = load_grids()
    labels = {}
    for po in sorted(by_po):
        sub, rs, cs = g250.window(by_po[po].bounds)
        inside = rasterize([by_po[po]], sub)
        depth = data["depth"][rs, cs]
        coastal = inside & (data["sea_frac"][rs, cs] >= 0.5) & (np.nan_to_num(depth, nan=1e9) <= 200)
        rr, cc = np.nonzero(coastal if coastal.any() else inside)
        xs, ys = sub.centres()
        px, py = xs[cc], ys[rr]
        mx, my = px.mean(), py.mean()
        i = int(np.argmin((px - mx) ** 2 + (py - my) ** 2))
        labels[po] = (float(px[i]), float(py[i]))
    return labels


def po_of_points(xy: np.ndarray, by_po: dict[int, object], max_km: float = 30.0) -> list[int | None]:
    """PO containing each point; else the nearest PO within max_km; else None."""
    pts = shapely.points(xy)
    out: list[int | None] = [None] * len(xy)
    best = np.full(len(xy), np.inf)
    for po in sorted(by_po):
        d = shapely.distance(by_po[po], pts)
        closer = d < best
        best[closer] = d[closer]
        for i in np.nonzero(closer)[0]:
            out[i] = po
    return [o if best[i] <= max_km * 1000 else None for i, o in enumerate(out)]


# --------------------------------------------------------------------------
# Localities (D17: owner and operator fields are read only to sanitise names)


def gazetteer_tokens() -> set[str]:
    (zpath,) = raw_files("kv_n250")
    gdb = zipfile.ZipFile(zpath).namelist()[0].split("/")[0]
    _, _, _, fields = pyogrio.raw.read(f"/vsizip/{zpath}/{gdb}", layer="N250_Stedsnavn_tekstplassering",
                                       columns=["TextString"], read_geometry=False)
    toks: set[str] = set()
    for s in fields[0]:
        toks.update(re.findall(r"[A-ZÆØÅÄÖ0-9\-]+", str(s).upper()))
    return toks


def display_name(raw: str) -> str:
    words = []
    for w in raw.split():
        words.append(w if ROMAN.match(w.upper()) else w[:1].upper() + w[1:].lower())
    return " ".join(words)


def sanitise_name(name: str, holders: str, places: set[str], loknr: int) -> tuple[str, bool]:
    """Drop name tokens that are not place names and occur in a holder's name (D17)."""
    holder_toks = set(re.findall(r"[A-ZÆØÅÄÖ0-9\-]+", (holders or "").upper())) - LEGAL_FORMS
    words = name.split()
    keep = []
    changed = False
    for w in words:
        u = w.upper()
        bad = u in LEGAL_FORMS or (u in holder_toks and u not in places and not ROMAN.match(u)
                                   and len(u) >= 3)
        if bad:
            changed = True
        else:
            keep.append(w)
    if not keep or all(ROMAN.match(w.upper()) for w in keep) or not R.brand_free(" ".join(keep)):
        return f"Lokalitet {loknr}", True
    return " ".join(keep), changed


def species_group(species: list[str]) -> str:
    groups = set()
    for s in species:
        low = s.lower()
        for g, keys in SPECIES_GROUPS:
            if any(k in low for k in keys):
                groups.add(g)
                break
        else:
            groups.add("other")
    return next(g for g in GROUP_PRIORITY if g in groups) if groups else "other"


def localities(by_po: dict[int, object]) -> tuple[list[dict], dict]:
    feats = arcgis_features("fdir_localities")
    places = gazetteer_tokens()
    rows, renamed = [], 0
    for f in feats:
        p = f["properties"]
        if p.get("status_lokalitet") != "AKTIV" or not f.get("geometry"):
            continue
        x, y = f["geometry"]["coordinates"][:2]
        species = sorted({s.strip() for s in (p.get("til_arter") or "").split(",") if s.strip()})
        forms = [s.strip() for s in (p.get("til_produksjonsform") or "").split(",") if s.strip()]
        purposes = sorted({s.strip().lower() for s in (p.get("til_formaal") or "").split(",") if s.strip()})
        name, changed = sanitise_name(p.get("navn") or "", p.get("til_innehavere") or "", places, int(p["loknr"]))
        renamed += changed
        placement = PLACEMENT.get(p.get("plassering"), "sea")
        group = species_group(species)
        stages = sorted({STAGES.get(s, "other") for s in forms}) or ["other"]
        stage = next((s for s in ("growout", "smolt", "broodstock", "ranching", "nursery") if s in stages), "other")
        unit = UNITS.get(p.get("kapasitet_unittype") or "", (p.get("kapasitet_unittype") or "").lower())
        cap = p.get("kapasitet_lok")
        props = {
            "id": int(p["loknr"]), "name": display_name(name), "kind": f"{placement}_{group}",
            "stage": stage, "species": species,
            "capacity": None if cap is None else round(float(cap), 1), "capacity_unit": unit,
            "water": (p.get("vannmiljo") or "").lower() or None,
            "municipality": R.clean_name(display_name(p.get("kommune") or "") or None),
        }
        if unit == "t" and cap is not None:
            props["mtb_t"] = round(float(cap), 1)
        if "slaktemerd" in purposes:
            props["harvest_pen"] = True
        rows.append({"x": float(x), "y": float(y), "loknr": int(p["loknr"]), "props": props,
                     "po_registry": int(p["prodareacode"]) if p.get("prodareacode") else None,
                     "salmonid_sea": placement in ("sea", "offshore") and group == "salmonid"})
    rows.sort(key=lambda r: r["loknr"])
    pos = po_of_points(np.array([[r["x"], r["y"]] for r in rows]), by_po)
    for r, po in zip(rows, pos):
        r["props"]["po"] = r["po_registry"] or po
    stats = {"active": len(rows), "renamed_d17": renamed,
             "kinds": dict(sorted(collections.Counter(r["props"]["kind"] for r in rows).items()))}
    return rows, stats


def locality_point_features(rows: list[dict]) -> list[dict]:
    lonlat = utm_to_lonlat_xy(np.array([[r["x"], r["y"]] for r in rows]))
    out = []
    for r, (lon, lat) in zip(rows, lonlat):
        props = dict(r["props"], x=round(r["x"]), y=round(r["y"]))
        out.append(feature({"type": "Point", "coordinates": [round(float(lon), 5), round(float(lat), 5)]}, props))
    return out


def polygons_by_loknr(source_id: str, active: set[int], extra=("areatype",)) -> list[dict]:
    feats = arcgis_features(source_id)
    out = []
    for f in feats:
        p = f["properties"]
        if not f.get("geometry") or p.get("loknr") is None or int(p["loknr"]) not in active:
            continue
        g = shapely.make_valid(shapely.geometry.shape(f["geometry"]))
        g = shapely.simplify(g, 2.0, preserve_topology=True)
        if g.is_empty or g.area <= 0:
            continue
        props = {"id": int(p["loknr"])}
        for k in extra:
            if k in p:
                v = p.get(k)
                props[k] = (v or "mooring").lower() if k == "areatype" else v
        out.append((props["id"], props.get("areatype", ""), p.get("objectid") or p.get("OBJECTID") or 0,
                    feature(to_geojson_geometry(g, 5), props)))
    out.sort(key=lambda t: (t[0], t[1], t[2]))
    return [t[3] for t in out]


# --------------------------------------------------------------------------
# Offshore areas and constraints


def offshore_rows() -> list[dict]:
    rows = []
    for sid, stage in HTH_STAGES:
        feats = arcgis_features(sid)
        for f, g in zip(feats, arcgis_geoms(feats)):
            if g is None:
                continue
            p = f["properties"]
            rows.append({"geom": shapely.make_valid(g), "stage": stage,
                         "props": {"stage": stage, "name": R.clean_name(p.get("name")),
                                   "area_id": int(p.get("id") or 0), "area_km2": round(g.area / 1e6, 1)}})
    rows.sort(key=lambda r: (r["props"]["stage"], r["props"]["area_id"]))
    return rows


def exclusion_rows() -> list[dict]:
    rows = []
    feats = arcgis_features("fdir_salmon_fjords")
    for f, g in zip(feats, arcgis_geoms(feats)):
        rows.append({"kind": "salmon_fjord", "name": f["properties"].get("fjord"), "geom": shapely.make_valid(g),
                     "oid": f["properties"]["objectid"]})
    feats = arcgis_features("fdir_coral_closures")
    for f, g in zip(feats, arcgis_geoms(feats)):
        rows.append({"kind": "coral_closure", "name": f["properties"].get("omraade"), "geom": shapely.make_valid(g),
                     "oid": f["properties"]["objectid"]})
    feats = arcgis_features("mdir_protected_areas")
    for f, g in zip(feats, arcgis_geoms(feats)):
        p = f["properties"]
        if g is None or p.get("majorEcosystemType") not in ("Marin", "MarinOgTerrestrisk"):
            continue
        rows.append({"kind": "protected_area", "name": p.get("navn"), "geom": shapely.make_valid(g),
                     "oid": p["OBJECTID"], "verneform": p.get("verneform")})
    rows.sort(key=lambda r: (r["kind"], r["oid"]))
    return rows


def fairway_rows() -> list[dict]:
    geoms, cols = read_zip_layer("kyv_fairway_lines", "farled_hovedledogbiled", ["farledtype", "farlednavn"])
    rows = []
    for i, g in enumerate(geoms):
        kind = cols["farledtype"][i]
        rows.append({"kind": kind, "name": cols["farlednavn"][i], "geom": g, "idx": i,
                     "buffer_m": R.HARD["fairway"]["buffer_m"].get(kind, 150)})
    return rows


def outlet_rows() -> tuple[list[dict], list[str]]:
    """Anadromous river mouths, the national salmon rivers among them flagged."""
    feats = arcgis_features("mdir_anadromous_outlets")
    pts = []
    for f in feats:
        if not f.get("geometry"):
            continue
        x, y = f["geometry"]["coordinates"][:2]
        p = f["properties"]
        pts.append({"x": float(x), "y": float(y), "name": p.get("elvenavn") or "", "vassdrag": p.get("vassdragsNR"),
                    "oid": p["OBJECTID"], "national": False})
    lat = utm_to_lonlat_xy(np.array([[p["x"], p["y"]] for p in pts]))[:, 1]
    unmatched = []
    for name, county, vassdrag in R.NATIONAL_SALMON_RIVERS:
        lo, hi = COUNTY_LAT[county]
        if vassdrag:
            hits = [i for i, p in enumerate(pts) if p["vassdrag"] in vassdrag]
        else:
            hits = [i for i, p in enumerate(pts)
                    if norm(re.sub(r"\(.*\)", "", p["name"])) == norm(name) and lo <= lat[i] <= hi]
        if not hits:
            unmatched.append(f"{name} ({county})")
        for i in hits:
            pts[i]["national"] = True
            pts[i]["national_name"] = name
    pts.sort(key=lambda p: p["oid"])
    return pts, unmatched


def coral_points() -> list[dict]:
    geoms, cols = read_zip_layer("hi_coral_reefs", None, ["lokalId", "minimumsdybde", "maksimumsdybde"])
    out = []
    for i, g in enumerate(geoms):
        if g is None or g.is_empty:
            continue
        out.append({"x": g.x, "y": g.y, "id": str(cols["lokalId"][i]),
                    "depth_min": cols["minimumsdybde"][i], "depth_max": cols["maksimumsdybde"][i]})
    out.sort(key=lambda p: p["id"])
    return out


def norway_land_n1000():
    geoms, cols = read_zip_layer("kv_n1000", "N1000_Arealdekke_omrade", ["objtype"])
    land = [g for g, t in zip(geoms, cols["objtype"]) if t != "Havflate"]
    return shapely.union_all(shapely.make_valid(np.array(land, dtype=object)), grid_size=1.0)


# --------------------------------------------------------------------------


def point_feature(x: float, y: float, props: dict) -> dict:
    lon, lat = utm_to_lonlat_xy(np.array([[x, y]]))[0]
    return feature({"type": "Point", "coordinates": [round(float(lon), 5), round(float(lat), 5)]},
                   dict(props, x=round(x), y=round(y)))


def main(labels: dict[int, tuple[float, float]] | None = None) -> dict:
    OUT.mkdir(parents=True, exist_ok=True)
    sizes, stats = {}, {}
    meta = {"crs": "EPSG:4326", "generator": "tools/geodata/build_layers.py"}

    rows, by_po = production_areas()
    labels = labels or po_labels(by_po)
    for lod in ("national", "regional"):
        fc = collection(po_features(rows, lod, labels), dict(meta, lod=lod, sources=["fdir_production_areas",
                                                                                     "sim_po_temperature"]))
        sizes[f"production_areas.{lod}.geojson"] = write_json(OUT / f"production_areas.{lod}.geojson", fc)

    land = simplify_polygonal(norway_land_n1000(), "national")
    fc = collection([feature(to_geojson_geometry(land, LONLAT_DECIMALS["national"]), {"kind": "land"})],
                    dict(meta, lod="national", sources=["kv_n1000"]))
    sizes["coastline.national.geojson"] = write_json(OUT / "coastline.national.geojson", fc)

    loc_rows, stats["localities"] = localities(by_po)
    fc = collection(locality_point_features(loc_rows), dict(meta, sources=["fdir_localities"],
                                                            note="No owner or operator fields (D17)."))
    sizes["localities.geojson"] = write_json(OUT / "localities.geojson", fc)
    active = {r["loknr"] for r in loc_rows}
    fc = collection(polygons_by_loknr("fdir_locality_areas", active, extra=()),
                    dict(meta, lod="regional", sources=["fdir_locality_areas"]))
    stats["locality_areas"] = len(fc["features"])
    sizes["locality_areas.geojson"] = write_json(OUT / "locality_areas.geojson", fc)
    fc = collection(polygons_by_loknr("fdir_mooring_areas", active), dict(meta, lod="regional",
                                                                           sources=["fdir_mooring_areas"]))
    stats["mooring_areas"] = len(fc["features"])
    sizes["mooring_areas.geojson"] = write_json(OUT / "mooring_areas.geojson", fc)

    off = offshore_rows()
    for lod in ("national", "regional"):
        feats = [feature(to_geojson_geometry(simplify_polygonal(r["geom"], lod) or r["geom"], LONLAT_DECIMALS[lod]),
                         r["props"]) for r in off]
        fc = collection(feats, dict(meta, lod=lod, sources=[s for s, _ in HTH_STAGES]))
        sizes[f"offshore_areas.{lod}.geojson"] = write_json(OUT / f"offshore_areas.{lod}.geojson", fc)
    stats["offshore_areas"] = len(off)

    exc = exclusion_rows()
    for lod in ("national", "regional"):
        feats = []
        for r in exc:
            g = simplify_polygonal(r["geom"], lod, EXCLUSION_TOLERANCE_M.get((lod, r["kind"])),
                                   EXCLUSION_MIN_HOLE_KM2.get((lod, r["kind"])))
            if g is None:
                continue
            # The national overview shows protected areas only from 10 km² up.
            if lod == "national" and r["kind"] == "protected_area" and g.area < 10e6:
                continue
            props = {"kind": r["kind"], "name": R.clean_name(r["name"])}
            if r.get("verneform"):
                props["protection"] = r["verneform"]
            feats.append(feature(to_geojson_geometry(g, LONLAT_DECIMALS[lod]), props))
        fc = collection(feats, dict(meta, lod=lod, sources=["fdir_salmon_fjords", "fdir_coral_closures",
                                                            "mdir_protected_areas"]))
        sizes[f"exclusions.{lod}.geojson"] = write_json(OUT / f"exclusions.{lod}.geojson", fc)
    stats["exclusions"] = dict(collections.Counter(r["kind"] for r in exc))

    fw = fairway_rows()
    for lod, kinds, tol in (("national", {"Hovedled"}, 1000.0), ("regional", {"Hovedled", "Biled"}, 50.0)):
        feats = []
        for r in fw:
            g = simplify_lines(r["geom"], tol) if r["kind"] in kinds else None
            if g is not None:
                feats.append(feature(to_geojson_geometry(g, LONLAT_DECIMALS[lod]),
                                     {"kind": r["kind"].lower(), "name": R.clean_name(r["name"]),
                                      "buffer_m": r["buffer_m"]}))
        fc = collection(feats, dict(meta, lod=lod, sources=["kyv_fairway_lines"]))
        sizes[f"fairways.{lod}.geojson"] = write_json(OUT / f"fairways.{lod}.geojson", fc)
    stats["fairways"] = dict(collections.Counter(r["kind"] for r in fw))

    outlets, unmatched = outlet_rows()
    corals = coral_points()
    feats = []
    for p in outlets:
        if p["national"]:
            feats.append(point_feature(p["x"], p["y"], {"kind": "national_salmon_river",
                                                        "name": R.clean_name(p["national_name"])}))
        else:
            feats.append(point_feature(p["x"], p["y"], {"kind": "anadromous_river",
                                                        "name": R.clean_name(re.sub(r"\s*\(.*\)", "", p["name"]))}))
    for c in corals:
        feats.append(point_feature(c["x"], c["y"], {"kind": "coral_reef"}))
    kinds = {
        "national_salmon_river": {"hard": True, "radius_m": R.HARD["national_salmon_river"]["radius_m"]},
        "anadromous_river": {"hard": False, "radius_m": R.RIVER_FACTOR["radius_m"]},
        "coral_reef": {"hard": True, "radius_m": R.HARD["coral_reef"]["radius_m"]},
    }
    fc = collection(feats, dict(meta, sources=["mdir_anadromous_outlets", "lovdata_salmon_rivers", "hi_coral_reefs"],
                                kinds=kinds, national_rivers_unmatched=unmatched))
    sizes["constraint_points.geojson"] = write_json(OUT / "constraint_points.geojson", fc)
    stats["constraint_points"] = {"national_salmon_river": sum(p["national"] for p in outlets),
                                  "anadromous_river": sum(not p["national"] for p in outlets),
                                  "coral_reef": len(corals), "national_rivers_unmatched": unmatched}
    for k, v in sorted(sizes.items()):
        print(f"{k:40s} {v / 1024:8.1f} KB")
    print(stats)
    return stats


if __name__ == "__main__":
    main()
