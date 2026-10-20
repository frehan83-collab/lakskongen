# National map data: production areas along the Norwegian coast

The map data in `godot/data/geo/` is built offline from real, free and open
Norwegian (and EU) data by the pipeline in `tools/geodata/`. It gives the game a
recognisably Norwegian coast, the exact production areas PO1-PO13, every
active aquaculture site, the Havbruk til havs offshore areas, the legal and
environmental no-go zones, and a sourced suitability score with deterministic
candidate sites for new farms. The game loads only these small files; it never
fetches data (roadmap D18).

This document covers each layer (source, licence, format, schema), the
coordinate systems, the suitability method, how to rebuild the data, and how
the next stage should use it. The per-layer licence record is
`godot/data/geo/LICENCES.md`, which the pipeline generates.

Data version: fetched 2026-10-20 (`tools/geodata/fetch_manifest.json`).
Content hash of the candidate list: `candidates.json` `content_sha256`.

## 1. What it replaces

| Today | Replaced by |
|---|---|
| `godot/data/norway_coastline.json` (Natural Earth 50 m, normalised 0..1, read by `NorwayCoastline.gd` for `MapPanel` and `MiniMap`) | `basemap/national.jpg` (stylised raster, UTM 33) plus `coastline.national.geojson` (Kartverket N1000 land outline) |
| `MapPanel.gd` `REGIONS[].pos` / `lonlat` (13 hand-placed PO points) | `production_areas.*.geojson`: the official polygons, with `label` (lon/lat) and `label_25833` anchors on each area's own coastal water |
| The procedural fallback positions in `site_plots.json` for offshore slots | `candidates.json` offshore candidates inside the Havbruk til havs areas (with depth); see §7.3 |

Nothing in the game reads the new files yet; that is the next stage (§7).
`norway_coastline.json` can be removed once `MapPanel` and `MiniMap` have moved.

## 2. Files

Total 5.88 MB of the 8 MB budget (`manifest.json` `total_bytes`;
`tests/test_geodata.py` enforces the budget and a 1.5 MB cap per file).

| File | Layer | Format | Size |
|---|---|---|---|
| `production_areas.national.geojson`, `.regional.geojson` | PO1-PO13 polygons (official, including their land part) | GeoJSON | 9-10 KB |
| `coastline.national.geojson` | Norway's land outline (N1000), for vector overlays | GeoJSON | 83 KB |
| `localities.geojson` | 1,781 active aquaculture sites, type-coded | GeoJSON points | 604 KB |
| `locality_areas.geojson` | 1,369 registered site area polygons | GeoJSON | 300 KB |
| `mooring_areas.geojson` | 1,332 NYTEK installation and mooring areas | GeoJSON | 284 KB |
| `offshore_areas.national.geojson`, `.regional.geojson` | Havbruk til havs areas, 19 polygons in 4 stages | GeoJSON | 8-11 KB |
| `exclusions.national.geojson`, `.regional.geojson` | 30 national salmon fjords, 884 marine protected areas, 19 coral closures | GeoJSON | 62 / 409 KB |
| `fairways.national.geojson`, `.regional.geojson` | 214 main and 926 secondary fairway lines with buffer widths | GeoJSON | 42 / 263 KB |
| `constraint_points.geojson` | 52 national salmon river mouths, 424 other anadromous river mouths, 1,774 coral reefs | GeoJSON points | 313 KB |
| `basemap/national.jpg` | Stylised national base map, 1866 x 2186 px, 800 m/px | JPEG | 428 KB |
| `basemap/po01.jpg` .. `po13.jpg` | Stylised base map per PO, 250 or 375 m/px, <= 1024 px | JPEG | 80-260 KB |
| `basemap/po_index.national.png` | PO id (1-13) per sea pixel of the national map, 0 elsewhere | PNG 8-bit grey | 26 KB |
| `basemap.json` | Bounds (EPSG:25833) and size of every base image | JSON | 3 KB |
| `suitability/po01.png` .. `po13.png` | Suitability grid per PO, 500 m | PNG RGB | 23-116 KB |
| `suitability.json` | Grid georeference, encoding, codes, per-PO statistics | JSON | 9 KB |
| `suitability_rules.json` | Every weight and threshold with its source | JSON | 12 KB |
| `candidates.json` | 156 open-pen candidates (12 per PO) and 33 offshore candidates | JSON | 112 KB |
| `ocean_climatology.json` | Per-PO monthly currents, waves, oxygen, temperature; 10 km grids | JSON | 96 KB |
| `LICENCES.md` | Source, licence, URL, fetch date, attribution per layer | Markdown | 29 KB |
| `manifest.json` | Every file with size and sha256, projection reference points, budget | JSON | small |

### 2.1 Schemas

All GeoJSON is a `FeatureCollection` with a `meta` object (`crs`, `lod`,
`sources`, notes). Coordinates are lon/lat (EPSG:4326, RFC 7946 ring order,
rings closed). Precision: 3 decimals at the national level of detail, 4 at the
regional one, 5 for points and site polygons.

- **production_areas**: `id` 1-13, `code` "PO1".."PO13", `name` (the names in
  `ProductionAreas.cs`), `source_name` (the registry's spelling),
  `traffic_light` green/yellow/red (the traffic-light status in the source),
  `area_km2`, `sea_area_km2`, `bbox_25833`, `tint` (light hex colour; four
  hues alternate so neighbours differ), `sea_temp_monthly_c` (12 values, the
  sim's own table), `label` [lon, lat] and `label_25833` [x, y]: the centroid
  of the area's coastal sea cells (<= 200 m deep), snapped onto its water.
- **localities**: `id` (loknr), `name` (title case place name), `kind`
  (`<placement>_<group>`: placement sea/land/offshore, group
  salmonid/marine_fish/shellfish/algae/other), `stage`
  (growout/smolt/broodstock/ranching/nursery/other), `species` (registry
  names), `capacity` + `capacity_unit` (t, pcs, kg, m3, m2, daa), `mtb_t`
  (when the unit is tonnes), `water`, `municipality`, `po`, `x`/`y`
  (EPSG:25833 m), `harvest_pen` (when registered as a harvest pen). **No
  owner, holder, operator, licence or inspector field (D17)**: the registry's
  holder names are read only to clean site names and never written.
- **locality_areas**: `id` (loknr) per polygon. **mooring_areas**: `id`,
  `areatype` (production_unit / raft / mooring).
- **offshore_areas**: `stage` (`tilradning_2019`, `anbefaling_2022`,
  `konsekvensvurdering_2024`, `kan_utlyses_2026`), `name`, `area_id`,
  `area_km2`. The same area appears once per stage it reached.
- **exclusions**: `kind` (salmon_fjord, protected_area, coral_closure),
  `name`, `protection` (protected areas). The national file keeps protected
  areas from 10 km² up.
- **fairways**: `kind` (hovedled, biled), `name`, `buffer_m` (the exclusion
  half-width).
- **constraint_points**: `kind` (national_salmon_river, anadromous_river,
  coral_reef), `name` (rivers), `x`/`y`; the radius and whether it is hard are
  per kind in `meta.kinds`.
- **suitability/poNN.png** (`suitability.json` has origin, size, 500 m cell):
  R = score byte (0 = no score; score = (R - 1) / 254), G = reason code, B =
  mean depth / 4 m (0 = land or unknown). Codes: 1 land, 2 outside the PO,
  3 no depth, 4 salmon fjord, 5 national salmon river, 6 protected area,
  7 coral closure, 8 coral reef, 9 fairway, 10 too close to a farm, 11 too
  shallow, 12 too deep, 13 outside the planning area; scored cells carry
  16 + the binding component (16 depth, 17 exchange, 18 farm distance,
  19 temperature, 20 river, 21 current, 22 waves, 23 oxygen, 24 none). A 500 m
  cell shows its best 250 m sub-cell; a cell with no scored sub-cell shows the
  most frequent reason.
- **candidates.json** `sites[]`: `id` (`PO03-01`, `HTH05-02`), `type`
  (open_pen/offshore), `po`, `rank`, `lon`/`lat`, `x`/`y`, `score`,
  `components` (each 0..1), `binding` (the term that costs the most score),
  `river_factor`, `depth_m`, `openness`, `nearest_farm_km`, `nearest_river_km`,
  `near` (nearest Kartverket N250 place name), `ocean` (current m/s, Hs m, O2
  mmol/m³ when the Copernicus layers are in), and for offshore `area`,
  `stage`, `stage_factor`. `random: false`, `seed: null`, `content_sha256`
  over the canonical JSON of `sites`.
- **ocean_climatology.json** `areas[po]`: `monthly` (12 values each of
  `current_speed_ms`, `hs_m`, `o2_mmol_m3`, `sst_c`, and the sim's
  `sim_sea_temp_c` beside it) and `grid` (10 km, origin in EPSG:25833, row 0
  north, `null` off the area's sea).

## 3. Coordinate systems

- **Display**: every GeoJSON is lon/lat (EPSG:4326).
- **Metric**: ETRS89 / UTM 33N (EPSG:25833), metres, Norway's national map
  projection. Points carry `x`/`y`; all rasters (base maps, PO index,
  suitability grids, climatology grids) are north-up UTM 33 grids whose
  bounds are in `basemap.json`, `suitability.json` and
  `ocean_climatology.json`.
- To draw a lon/lat layer over a base map the game projects lon/lat to UTM 33,
  then to pixels: `px = (x - x0) / res`, `py = (y1 - y) / res`.
  `tests/test_geodata.py::utm33` is the reference forward projection (Krüger
  series to n⁴, GRS80, k0 0.9996, central meridian 15°E, false easting
  500 000 m); `manifest.json` `projection_check_25833` lists six points across
  Norway with their pyproj results, so a C# port can be unit-tested to 1 cm.

## 4. Sources and licences

Every source was checked for commercial use before it was used, against its
Geonorge metadata record (or the provider's own licence page). All used
sources allow commercial use with attribution. The full record, generated from
`tools/geodata/geo_sources.py`, is `godot/data/geo/LICENCES.md`; the in-game
credits (`godot/data/credits.json`, `THIRD_PARTY.md`) carry one entry per
provider with the attribution text.

| Provider | Data | Licence | Endpoint |
|---|---|---|---|
| Fiskeridirektoratet | Localities, site areas, NYTEK areas, PO1-PO13, Havbruk til havs (2019, 2022, 2024, 2026), national salmon fjords, coral closures | NLOD (attribution: Fiskeridirektoratet) | ArcGIS REST `gis.fiskeridir.no/server/rest/services/FiskeridirWFS_akva`, `FiskeridirWFS`, `FiskeridirWFS_vern` |
| Miljødirektoratet | Naturvernområder (protected areas), Lakseregisteret river mouths | NLOD 2.0 | ArcGIS REST `kart.miljodirektoratet.no/arcgis/rest/services/vern`, `anadrome_laksefisk` |
| Kystverket | Hovedled og biled (fairways) | NLOD 2.0 | Geonorge download (FGDB) |
| Havforskningsinstituttet | Korallrev (MAREANO coral reefs) | NLOD | Geonorge download (GML) |
| Kartverket | N1000, N250 (land cover, baseline, place names), DTM 50 (relief), depth grid (WCS `bathymetry50m`) | CC BY 4.0 | Geonorge download and WCS |
| EMODnet Bathymetry Consortium | DTM 2022 (depth where Kartverket's grid has gaps; the sea outside Norway) | CC BY 4.0 | WCS `ows.emodnet-bathymetry.eu` |
| Copernicus Marine Service | TOPAZ4 physics, ECOSMO oxygen, WAM wave climatology (Arctic MFC) | Copernicus Marine Service licence (free, any purpose, attribution + DOI) | Copernicus Marine Toolbox, owner's login |
| Lovdata | National salmon rivers (FOR-2009-06-22-961 vedlegg 2) | Public domain (åndsverkloven § 14) | transcribed into `geo_rules.py` |

Considered and **not used** (reasons in `LICENCES.md`): Kartverket's Sentinel-2
"Skyfritt" mosaic (CC BY 4.0, allowed, but only as multi-GB 10 m tiles and a
photographic look against the art brief), Geovekst's Sentinel-2 mosaic and
Kartverket's seamless DTM WCS (both Norge digitalt-lisens, not usable), the
2.6 GB Sjøkart depth database (the same agency's WCS grid is used), Kystverket's
fairway *area* polygons (211 of 1050 active sea salmon sites lie inside them, so
they are corridors, not no-go zones), MET Norway NorKyst-800 (not needed once
Copernicus physics is in) and the BarentsWatch API (needs client credentials).

## 5. Suitability and candidate sites

All rules live in `tools/geodata/geo_rules.py` and ship verbatim as
`suitability_rules.json`. The analysis grid is 250 m in EPSG:25833; the
published grid is 500 m. Nothing is random.

### 5.1 Hard constraints (cell excluded, reason recorded)

| Constraint | Rule | Source |
|---|---|---|
| National salmon fjord | No new food-fish farms for salmonids inside | FOR-2009-06-22-961 § 3 (Lovdata, amended 2023) |
| National salmon river | Sites at least 5 km from its mouth (52 mouths matched to the regulation's 52 rivers; Oldenelva has no Lakseregisteret outlet and lies inside the Nordfjorden salmon fjord) | FOR-2009-06-22-961 § 6 |
| Protected area | Inside a marine or marine-and-terrestrial naturvernområde | Naturmangfoldloven; Miljødirektoratet |
| Coral closure | Inside a coral-reef protection area | FOR-2016-01-08-8 |
| Coral reef | Within 500 m of a MAREANO reef record (GB: anchor spread ~3x depth) | Havforskningsinstituttet |
| Fairway | Within 300 m of a hovedled or 150 m of a biled centre line (GB, calibrated: no active sea salmon site lies within 250 m of a hovedled) | Kystverket; havne- og farvannsloven § 14 |
| Farm too close | Closer than 2.5 km to an active sea salmonid site | Mattilsynet guideline, 2.5 km for sites up to 3600 t MTB (as cited in Naturvernforbundet's 2020 hearing statement) |
| Outside the planning area (open pens) | More than 1 nm outside the baseline (grunnlinje): beyond municipal coastal plans, the Havbruk til havs regime | Plan- og bygningsloven § 1-2; baseline from Kartverket N250 |
| Depth | Shallower than 15 m or deeper than 300 m (open pens; GB) | Owner brief, GB |
| No depth / land / outside the PO | Data limits | - |

### 5.2 Score components (0..1) and weights

score = (Σ wᵢ cᵢ / Σ wᵢ) x river factor; 0 under a hard constraint.

| Component | Weight | Rule | Source |
|---|---|---|---|
| Depth | 0.30 | 0 at 15 m, 1 from 25 to 150 m, 0 at 300 m | Owner brief (open pens ~25-150 m), GB ramps |
| Water exchange | 0.25 | Openness proxy: share of sea within 3 km, 0 at 30 %, 1 at 80 % | GB proxy (enclosed basins exchange poorly) |
| Distance to farms | 0.25 | 0 at 2.5 km, 1 at 5 km from the nearest active sea salmonid site (lice pressure) | Mattilsynet 2022-01-21 (ref 2021/255437): 5 km recommended between salmon grow-out sites outside coordinated fallowing |
| Temperature | 0.20 | Mean over months of a window: 0 at 2 °C, 1 from 8 to 16 °C, 0 at 20 °C, on the area's climatology in `ProductionAreas.cs` | Calado et al. 2021 (optimum 13-16 °C, ITmax ~22 °C); Alanärä et al. 2001; HI Rapport 2025-69 |
| Current (Copernicus) | 0.15 | Annual mean speed, upper 30 m: 0 at 0.01, 1 from 0.05 to 0.40, 0 at 0.80 m/s | TOPAZ4, doi 10.48670/moi-00007; GB |
| Waves (Copernicus) | 0.20 | Highest monthly mean Hs: 1 up to 1.2 m, 0 at 2.8 m (GB, calibrated: existing sites median 0.5 m, 95th percentile 2.2 m) | WAM 3 km, doi 10.48670/moi-00008 |
| Oxygen (Copernicus) | 0.10 | Lowest monthly mean O2, upper 30 m: 0 at 180, 1 at 250 mmol/m³ | ECOSMO, doi 10.48670/moi-00006; GB |
| River factor | x | 0.6 at an anadromous river mouth rising to 1 at 5 km (GB, scaled on § 6's 5 km) | Lakseregisteret |

Without the Copernicus fields the three optional weights are 0 (neutral) and
the score uses the first four components; `suitability_rules.json`
`effective_weights` and `copernicus_layers_used` record which applied. In this
build they are in. At the models' scale the upper 30 m never falls below ~255
mmol/m³ O2 on this coast, so oxygen never binds; basin hypoxia in sill fjords
is deeper than the pens and not resolved. The models mask most fjords as land:
a fjord cell takes the value of the nearest model water cell.

The *binding constraint* of a cell or a candidate is the term that costs the
most score: component i costs wᵢ / Σw x (1 - cᵢ) x river factor; the river
factor costs base x (1 - factor).

### 5.3 Candidates

- **Open pens, per PO**: cells with no hard constraint and score >= 0.55, at
  least one cell clear of every exclusion polygon, 250 m beyond the river and
  coral radii, and outside every *shipped* (simplified) exclusion polygon and
  fairway buffer. Ranked by score (3 decimals), then more open water, then
  depth nearer 80 m, then north, then west; picked greedily with at least
  10 km between candidates (twice the Mattilsynet 5 km norm, so the 12
  candidates spread over the area; GB).
- **Offshore**: inside each Havbruk til havs area, at its latest stage (stage
  factor 0.85 for the 2019 recommendation up to 1.0 for the areas that can be
  announced by royal decree in 2026); depth 0 at 60 m, 1 from 100 to 350 m, 0
  at 600 m (GB: OF1-class units moor in ~100-300 m); the same hard constraints
  except the planning area; up to 3 per area, 10 km apart.
- 189 sites in this build: 156 open pens (12 per PO), 33 offshore. Binding
  constraints: temperature 108 (the colder and the warmest areas), current 24,
  stage 24, waves 18, none 9, exchange 3, farm distance 3.

### 5.4 Known limits

- Kartverket's public depth grid is 50 m (finer coastal depth is classified);
  it is sampled at 250 m, so narrow sounds can read as land or lose depth.
- The water-exchange proxy is geometric; tidal currents are not in TOPAZ4.
- Distance to farms uses the registry's site points, not the pens.
- The scores are a game's siting model with sourced inputs, not a licensing
  decision: Mattilsynet's distances are recommendations, and real cases weigh
  current modelling, disease zones and municipal plans.

## 6. Rebuilding the data

Raw downloads are cached outside the repo (`~/.cache/lakskongen-geo`, about
1.8 GB) with their sha256 in `tools/geodata/fetch_manifest.json`; every build
step verifies those hashes. Python packages are pinned in
`tools/geodata/requirements.txt` and installed into a venv outside the repo.

```bash
# Fetch what is missing, build everything, render the previews:
bash tools/geodata/run_pipeline.sh
# Build from the cache only (no network):
bash tools/geodata/run_pipeline.sh --build-only
# With the optional Copernicus Marine step (the owner logs in once):
~/.local/share/lakskongen-copernicus/venv/bin/copernicusmarine login
COPERNICUS_PYTHON=~/.local/share/lakskongen-copernicus/venv/bin/python \
  bash tools/geodata/run_pipeline.sh --build-only
```

Steps (each a script in `tools/geodata/`): `geo_fetch.py` (pinned endpoints in
`geo_sources.py`), `fetch_copernicus.py` (optional: checks only that the
toolbox's credentials file exists, never reads it; three small subsets, 58 MB;
reduces to monthly climatologies), `build_grids.py` (125 m land-cover classes,
250 m depth, relief, planning area), `build_layers.py` (vector layers, D17
name cleaning), `build_suitability.py` (grids, candidates, rules,
climatology), `build_basemap.py` (stylised rasters), `build_manifest.py`
(LICENCES.md, manifest.json, budget check), `render_previews.py` (matplotlib
Agg, no windows) to `docs/run/evidence/geo/`.

Determinism: two builds from the same cache are byte-identical (checked:
`manifest.json` identical, every file's sha256 in it); no wall-clock time is
written. To refresh the data, delete a source's cache folder (or pass
`--force <id>` to `geo_fetch.py`), rebuild, and review the diff of
`fetch_manifest.json` and `candidates.json`.

Tests: `python3 -m pytest tests/test_geodata.py` (stdlib + pytest; CI needs no
GIS package).

## 7. Integration guide (next stage)

### 7.1 Loading

- Read JSON/GeoJSON with `FileAccess` + `JSON.parse_string` (GDScript) or
  `System.Text.Json` (C#). Parse once and cache, as `NorwayCoastline.gd` does.
- **The data PNGs are data, not pictures**: `suitability/*.png` and
  `po_index.national.png` must keep exact byte values. Read them with
  `FileAccess.get_file_as_bytes` + `Image.load_png_from_buffer` (not `load()`),
  and sample with nearest filtering, no mipmaps, no VRAM compression.
  Status (geo P0, 2026-10-20): their 14 `.import` sidecars are committed with
  `importer="keep"` (*Keep File*), and the 14 JPEG base maps import as
  lossless textures with mipmaps (`detect_3d/compress_to=0`), pinned by
  `tests/test_geo_packaging.py`.
- Status (geo P0, 2026-10-20, ruling OD13): both export presets carry this
  explicit `include_filter`:
  `data/geo/*.json,data/geo/*.geojson,data/geo/*.md,data/geo/suitability/*.png,data/geo/basemap/po_index.national.png`.
  Exports pack every file the game reads raw and never the raw JPEGs (their
  imported textures ship as resources); `tests/test_geo_packaging.py` pins it.
- Status (geo C1, 2026-10-20): the map UI's loader and projection are in
  `godot/scripts/ui/map/`. `GeoData` is a static, parse-once cache
  (`read_json`, `basemap`, `basemap_texture`, `load_data_png`,
  `po_index_image`, `production_areas(lod)`, `offshore_areas(lod)`,
  `salmon_fjords()`, `candidates()`, `suitability_grid(po)`, `rules()`,
  `parse_count`, `parses_of(rel)`): ids cast to int, the parsed data shared
  read-only, the data PNGs decoded from raw bytes only, and a failure
  recorded in `last_error` with no engine error. It uses a `JSON` instance
  rather than `JSON.parse_string`, which push_errors on a broken file.
  `GeoProjection.utm33` reproduces the six reference points within the
  1 cm gate (measured at most 0.46 mm; 64-bit, atanh/sinh/cosh through log
  and exp); `to_px`, `to_cell` and
  `to_utm` subtract the origin in 64-bit. `godot/tests/GeoMapChecks.gd`
  (UiFlowTests) and `tests/test_geo_map_contract.py` pin them. The game
  never parses `localities.geojson` in GDScript; the icons come from the
  sim's packed arrays (Track A).
  Merged into geo/int (`4ea2cf8`); the captain's fix `e87371f` makes the id
  cast exact, so only a whole-number `id`, `po`, `rank` or `area_id` becomes
  an int and a fractional one keeps its float value.
- Credits: `godot/data/credits.json` already lists the seven map-data
  providers with their attribution notices (translation keys in
  `translations.csv`), so `CreditsScreen` shows them with no code change.

### 7.2 The sim (`src/LakskongenSim`)

- **Production areas**: `ProductionAreas.cs` stays the source of names,
  temperatures, lice weeks and licence sizes; the map files take those values
  from it (`build_layers.py` parses the C# table). A deterministic
  `GeoProductionAreas` reader can add the polygon (for "which PO is this
  point in") and `traffic_light` (the source's status) to `ProductionAreaInfo`,
  embedded like `site_plots.json` so the engine never touches the file system.
  Status (geo A1, 2026-10-20): `src/LakskongenSim/Environment/Geo/` reads
  the embedded `production_areas.regional.geojson`, `candidates.json`,
  `suitability.json`, `suitability_rules.json`, `localities.geojson` and the
  13 grids (LakskongenSim.csproj ItemGroup "geo", each SHA-256 equal to
  `manifest.json`), each parsed once per process: `GeoProductionAreas`
  (`AreaOf` lon/lat, `AreaAtUtm` for display), `GeoCandidates` (cleaned
  labels via `PlaceLabels`), `GeoSuitability.CellAt` on `PngGrid`,
  `GeoSuitabilityRules` (weights, costs, binding) and `GeoLocalities`. No
  engine wiring and no bridge yet (A2 and A3).
- **Zones and site validation**: a candidate or player-picked point is legal
  when its suitability cell (`origin + (c, r) * 500 m`) has R > 0; G says why
  not. Sample at the point's UTM coordinates (port `utm33` from the test, check
  it against `manifest.json` `projection_check_25833`). PNG decoding in C# is
  the stdlib loop in the test (zlib via `ZLibStream`).
  Status (geo A1, 2026-10-20): `Utm33.Forward` matches the six reference
  points to 1 cm (display and validation only, never game state);
  `PngGrid.Decode` takes 8-bit grey or RGB, non-interlaced, filters 0-4 and
  names the field it refuses; `GeoSuitability.CellAt(po, x, y)` samples
  floor((x - ox) / 500), floor((oy - y) / 500), row 0 north.
- **Site plots (F2-03, `SitePlotCatalog` / `ISitePlotGeometry`)**: implement a
  `GeoSitePlotGeometry` that turns `candidates.json` offshore sites into
  `OffshoreSlot` records (`X`/`Z` from the site's position relative to the
  local region's origin, `Exposure` 1, `WaterDepthM` = `depth_m`) and joins
  them by id with `site_plots.json`'s gameplay attributes. Open-pen candidates
  become the sea sites a player can apply for in U2-01 (licence auctions,
  build dialog), keyed by `id`, with `score`, `components` and `binding` as the
  dialog's "why here" explanation. Land pads have no geo candidates yet (a
  follow-up: flat shore land near deep water from N250 + DTM 50).
- **Rival farms**: `localities.geojson` sea and land salmonid sites, with
  `mtb_t` and `po`, give the real distribution of competitors per area; give
  them generated parody company names (D17: the data has none, by design).
  Status (geo A1, 2026-10-20): `GeoLocalities` reads all 1,781 sites;
  `SeaGrowoutProducing` (sea salmonid grow-out without harvest pens) is 994
  sites holding 3,722,295 t. The rival layout and the parody names are A3.
- **Determinism**: the files are inputs; their sha256 is in `manifest.json`.
  Golden traces are unaffected until the sim reads them; the first change that
  does must re-record them with a diff report (HANDOFF §7).

### 7.3 MapPanel and MiniMap (`godot/scripts/ui`)

- **National overview**: draw `basemap/national.jpg` as the background
  (`TextureRect`, bounds from `basemap.json`). Tint the production areas
  lightly with a shader that looks up `po_index.national.png` (nearest) and
  mixes the area's `tint` at ~30-40 % on sea pixels only; the same lookup
  answers "which area is under the cursor". Place PO labels at `label`.
  Draw sites from `localities.geojson` as type-coded icons by `kind` (sea pen
  ring, land tank, offshore unit; other species small and quiet), cluster
  them below a zoom threshold, draw `offshore_areas.national` (safety yellow,
  dashed by stage), `exclusions.national` salmon fjords as a soft red hatch,
  and candidates as salmon stars (open pens) and yellow hexagons (offshore).
  This replaces `NorwayCoastline.gd` and the hand-placed `REGIONS[].pos`.
- **Regional (PO) view**: `basemap/poNN.jpg` with the suitability heat from
  `suitability/poNN.png` (R through a cool-to-gold ramp, hard exclusions from
  G as a muted hatch, the planning-area limit quieter still), site polygons
  (`locality_areas`), mooring areas, fairways (`buffer_m`), exclusion
  outlines (`exclusions.regional`), river and coral points with their radii,
  and the area's 12 candidates with tooltips (score, binding, depth, nearest
  farm, `near`).
  Status (geo D, 2026-10-20, branch `geo/map-region`): built as
  `godot/scripts/ui/map/GeoRegionView.gd` and friends, reached by
  `MapPanel.drill_into(area)` (a double-click on an area, 'Utforsk området').
  `GeoRegionMath.suit_uv` maps a base-map pixel to the grid's UV (the
  numbers `godot/shaders/map_suitability.gdshader` takes: nearest, no
  mipmaps); the ramp's stops sit on the scored cells' spread (0.5, 0.7, 0.85,
  1.0) and its opacity rises with the score; codes 4-12 hatch, 13 hatches
  quieter, 1-3 stay clear. `GeoRegionData` caches each area's five vector
  layers in base-map pixels on the first drill (a repeat drill parses
  nothing); a feature is kept when it meets the base map's bounds. Rivals in
  owner bands (A's `get_geo_sites`), the company's sites (B's
  `get_player_site_locations`), the 12 candidates as stars and as keyboard
  buttons (B's `get_sea_candidates`), the Havbruk til havs hexagons with
  hav_1's filled, a legend in the layer chips and a hover readout (score,
  why, depth) from the CPU grid. Until A and B merge, their reads go through
  the marked stub adapter `GeoRegionBridge.gd`. Checked by
  `godot/tests/GeoRegionChecks.gd` and `tests/test_geo_map_contract.py`.
- **Local site view**: the existing 3D fjord scene; the candidate or site
  position, depth and `ocean` values seed it.
  Status (geo D, 2026-10-20): the site level is a 2D card
  (`GeoSiteCard.gd`) over a 4x crop of the area's base map (the heat's 500 m
  cells, site and mooring polygons, a 2.5 km ring and the river mouths' 5 km
  rings): a candidate's 'why here' with 'Bygg her' (the build dialog at the
  candidate) or 'Søk vekstlisens' (naming the zone's licence), a company
  site's 'Vis i 3D' (the existing single fjord, with the site selected), a
  rival's owner, kind, stage and MTB, and hav_1's 'Bygg her (hav)'. Seeding
  the 3D scene from a candidate stays G2-01 phase 2.
- Style: Kyst & Stål. The base maps already use the cool landscape palette
  (`design/tokens.json` "landscape"); keep saturation for icons and signals
  (buoy yellow for offshore, salmon for candidates, orange for land sites).

Previews of all of this, rendered from the shipped files only, are in
`docs/run/evidence/geo/` (`national.jpg`, `po01.jpg`..`po13.jpg`).
