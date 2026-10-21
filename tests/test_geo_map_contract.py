"""The map UI's contract with the geo data (geo stages 2-3, docs/geo/STAGE23_PLAN.md
Track C; Track D adds its own functions here, add-only).

C1, the foundation (godot/scripts/ui/map/GeoData.gd, GeoProjection.gd):
- GeoData reads the data PNGs (suitability/poNN.png, po_index.national.png)
  only as raw bytes (FileAccess.get_file_as_bytes + Image.load_png_from_buffer),
  never through load() or preload(), so every byte keeps its value.
- The tables godot/tests/GeoMapChecks.gd pins (the po_index histogram, the
  label anchors off the open-sea mask, PO3's 12 candidate cells) equal what
  test_geodata.py's own PNG decoder reads, so the headless GDScript checks and
  the stdlib reference read the same bytes.
- No emoji under godot/scripts/ui/map (D14).

C2, the national map (MapPanel v2): no reference to the retired coastline
and minimap files (manifest.json excepted); MapPanel.gd under 800 lines with
its public API and Track D's hooks; the MAP_GEO_* keys in both languages;
the plan's sim reads through the one adapter, which stubs nothing now that
Tracks A and B are merged; no GDScript parses localities.geojson; the tint
shader's raw nearest lookup; ThemeLint's lint_background.
"""

from __future__ import annotations

import ast
import csv
import re
from collections import Counter
from pathlib import Path

from test_geodata import GEO, decode_png, load

REPO = Path(__file__).resolve().parent.parent
MAP_SCRIPTS = REPO / "godot" / "scripts" / "ui" / "map"
GEO_DATA = MAP_SCRIPTS / "GeoData.gd"
CHECKS = REPO / "godot" / "tests" / "GeoMapChecks.gd"
EMOJI = re.compile("[\U0001F000-\U0001FAFF☀-➿️]")
## A call that loads a resource: load(...), preload(...), ResourceLoader.load(...).
RESOURCE_LOAD = re.compile(r"(?<![\w.])(?:pre)?load\(|ResourceLoader\.load\(")


def _gd_const(name: str) -> object:
    """A literal GDScript const of GeoMapChecks.gd (a list or dict of numbers
    and strings, which Python's literal syntax reads the same way)."""
    text = CHECKS.read_text(encoding="utf-8")
    m = re.search(rf"^const {name} := (\[.*?\]|\{{.*?^\}})", text, flags=re.MULTILINE | re.DOTALL)
    assert m, f"GeoMapChecks.gd has no const {name}"
    return ast.literal_eval(m.group(1))


def _strip_comments(line: str) -> str:
    return line.split("#", 1)[0] if not line.lstrip().startswith("##") else ""


def test_geodata_reads_data_pngs_only_as_raw_bytes():
    text = GEO_DATA.read_text(encoding="utf-8")
    assert "FileAccess.get_file_as_bytes(" in text and ".load_png_from_buffer(" in text
    for path in sorted(MAP_SCRIPTS.glob("*.gd")):
        for n, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            code = _strip_comments(line)
            if RESOURCE_LOAD.search(code):
                assert "png" not in code.lower(), f"{path.name}:{n}: a data PNG through load(): {line.strip()}"
    # GeoData loads resources only in basemap_texture's imported-JPEG branch,
    # after a data PNG has gone to load_data_png.
    start = text.index("static func basemap_texture")
    end = text.index("\nstatic func ", start + 1)
    branch = text[start:end]
    jpeg_branch = start + branch.index("load_data_png(file)")
    assert branch.index('if info["is_data"]:') < branch.index("load_data_png(file)") < branch.index("\telse:")
    # Comments blanked to spaces, so offsets in the code equal offsets in text.
    code = "\n".join(_strip_comments(ln).ljust(len(ln)) for ln in text.splitlines())
    loads = [m.start() for m in RESOURCE_LOAD.finditer(code)]
    assert loads and all(jpeg_branch < at < end for at in loads), loads


def test_the_contract_check_finds_a_png_load():
    assert RESOURCE_LOAD.search('var img = load("res://data/geo/suitability/po03.png")')
    assert RESOURCE_LOAD.search('ResourceLoader.load(ROOT + "basemap/po_index.national.png")')
    assert not RESOURCE_LOAD.search("image.load_png_from_buffer(bytes)")
    assert not RESOURCE_LOAD.search("GeoData.load_data_png(file)")


def test_the_pinned_po_index_histogram_equals_the_reference_decoder():
    info = load("basemap.json")["images"]["po_index_national"]
    w, h, ch, rows = decode_png(GEO / info["file"])
    assert (w, h, ch) == (info["width"], info["height"], 1)
    counts = Counter(v for row in rows for v in row)
    assert set(counts) <= set(range(14))
    assert _gd_const("PO_INDEX_COUNTS") == [counts.get(v, 0) for v in range(14)]


def test_the_pinned_label_anchors_off_the_mask_equal_the_reference_decoder():
    info = load("basemap.json")["images"]["po_index_national"]
    _, _, _, rows = decode_png(GEO / info["file"])
    x0, _, _, y1 = info["bounds_25833"]
    off = []
    for f in load("production_areas.national.geojson")["features"]:
        x, y = f["properties"]["label_25833"]
        value = rows[int((y1 - y) // info["res_m"])][int((x - x0) // info["res_m"])]
        assert value in (0, f["properties"]["id"]), f["properties"]["code"]
        if value == 0:
            off.append(f["properties"]["id"])
    assert _gd_const("LABELS_OFF_MASK") == off == [1, 3]


def test_the_pinned_po3_candidate_cells_equal_the_reference_decoder():
    g = load("suitability.json")["grids"]["3"]
    w, h, ch, rows = decode_png(GEO / g["file"])
    assert (w, h, ch) == (g["width"], g["height"], 3)
    want = {}
    for s in load("candidates.json")["sites"]:
        if s["type"] != "open_pen" or s["po"] != 3:
            continue
        c = int((s["x"] - g["origin_25833"][0]) // g["res_m"])
        r = int((g["origin_25833"][1] - s["y"]) // g["res_m"])
        want[s["id"]] = [c, r, *rows[r][c * 3:c * 3 + 3]]
        assert want[s["id"]][2] > 0, f"{s['id']}: its grid cell is not scored"
    assert len(want) == 12
    assert _gd_const("PO3_CELLS") == want


def test_no_emoji_in_the_map_scripts():
    paths = sorted(MAP_SCRIPTS.glob("*.gd")) + [CHECKS]
    assert len(paths) >= 3
    for path in paths:
        text = path.read_text(encoding="utf-8")
        assert not EMOJI.search(text), (path.name, EMOJI.findall(text))


# ── C2, the national map (GeoMapView, MapPanel v2) ────────────────────────────

MAP_PANEL = REPO / "godot" / "scripts" / "ui" / "MapPanel.gd"
ADAPTER = MAP_SCRIPTS / "GeoBridgeAdapter.gd"
NATIONAL_CHECKS = REPO / "godot" / "tests" / "GeoMapNationalChecks.gd"
TINT_SHADER = REPO / "godot" / "shaders" / "map_po_tint.gdshader"
TRANSLATIONS = REPO / "godot" / "translations" / "translations.csv"
MANIFEST = REPO / "godot" / "data" / "geo" / "manifest.json"
## The reads the plan names (§3, sim·geo and sim·candidates lanes); the
## captain's C2 merge adds get_sea_candidates for the stars' places.
PLAN_READS = ["get_geo_production_areas", "get_geo_offshore_candidates", "get_geo_area_at",
              "get_geo_sites_signature", "get_geo_sites", "get_rival_summary", "get_player_site_locations",
              "get_sea_candidates"]
## MapPanel's public API: TutorialTargets, AttentionInbox, GameEventActions,
## BuildFlowLink and the UI tests call these (plan §3 "MapPanel v2 API").
PUBLIC_API = ["region_marker", "home_area", "new_farm_button", "region_label_rects", "city_label_rects",
              "select_area", "open_build", "open_auction", "open_build_family", "expansion_section",
              "build_dialog", "auction_card", "on_panel_shown", "drill_into", "chart"]
## Retired by Track C; the strings are built so this file does not match itself.
RETIRED = ["Norway" + "Coastline", "norway_" + "coastline", "Mini" + "Map"]
SKIP_DIRS = {".godot", "bin", "obj", "__pycache__", ".import"}
BINARY = {".png", ".jpg", ".jpeg", ".webp", ".glb", ".ogg", ".wav", ".mp3", ".ttf", ".otf", ".woff2",
          ".exr", ".hdr", ".res", ".scn", ".dll", ".so", ".dylib", ".pck", ".zip"}


def _text_files(root: Path):
    for path in sorted(root.rglob("*")):
        if not path.is_file() or SKIP_DIRS.intersection(path.relative_to(REPO).parts):
            continue
        if path.suffix.lower() in BINARY:
            continue
        yield path


def _code(path: Path) -> str:
    return "\n".join(_strip_comments(ln) for ln in path.read_text(encoding="utf-8").splitlines())


def test_no_reference_to_the_retired_map_files():
    """NorwayCoastline.gd, MiniMap.gd and norway_coastline.json are gone, and
    nothing in godot/, tests/ or src/ names them. manifest.json is excepted:
    its hash-pinned 'supersedes' block records what the geo data replaced."""
    hits = []
    for root in (REPO / "godot", REPO / "tests", REPO / "src"):
        for path in _text_files(root):
            if path == MANIFEST or path == Path(__file__).resolve():
                continue
            try:
                text = path.read_text(encoding="utf-8")
            except (UnicodeDecodeError, OSError):
                continue
            hits += [f"{path.relative_to(REPO)}: {word}" for word in RETIRED if word in text]
    assert not hits, "\n".join(hits[:20])
    for gone in ["godot/scripts/ui/NorwayCoastline.gd", "godot/scripts/ui/MiniMap.gd",
                 "godot/data/norway_coastline.json", "godot/scripts/ui/MapPanelEnhanced.gd.uid"]:
        assert not (REPO / gone).exists(), gone


def test_the_retired_check_finds_a_reference():
    assert any(w in 'const X = preload("res://scripts/ui/' + "Norway" + 'Coastline.gd")' for w in RETIRED)
    assert any(w in "var m := " + "Mini" + "Map.new()" for w in RETIRED)


def test_map_panel_is_under_800_lines_and_keeps_its_public_api():
    text = MAP_PANEL.read_text(encoding="utf-8")
    assert len(text.splitlines()) < 800, len(text.splitlines())
    for name in PUBLIC_API:
        assert re.search(rf"^func {name}\(", text, flags=re.MULTILINE), name
    assert re.search(r"^signal region_selected\(region_id: String\)", text, flags=re.MULTILINE)
    assert re.search(r"^var _region_nodes: Dictionary", text, flags=re.MULTILINE)
    # Track D's marked hooks: the drill stub, one Esc branch, the site hook.
    assert text.count("# geo-drill hook") == 1 and "func drill_into(" in text
    assert text.count("# geo-site hook") == 1 and "feature_clicked.connect(_on_feature_clicked)" in text
    # REGIONS and its invented stats are gone.
    assert "const REGIONS" not in text and "key_operators" not in text and "disease_risk" not in text


def test_map_geo_keys_exist_in_both_languages_and_are_used():
    """Every MAP_GEO_* key the map's code names exists with an English and a
    Norwegian text, and every MAP_GEO_* row is used. The map reads keys
    through TranslationServer.translate(key), which test_text_correctness's
    literal-key scan cannot follow."""
    with TRANSLATIONS.open(encoding="utf-8", newline="") as f:
        rows = {r[0]: r for r in csv.reader(f)}
    sources = sorted(MAP_SCRIPTS.glob("*.gd")) + [MAP_PANEL, NATIONAL_CHECKS]
    used = set()
    for path in sources:
        used |= set(re.findall(r'"(MAP_GEO_[A-Z0-9_]+)"', path.read_text(encoding="utf-8")))
    assert len(used) >= 40, len(used)
    missing = [k for k in sorted(used) if k not in rows or not rows[k][1].strip() or not rows[k][2].strip()]
    assert not missing, missing
    dead = [k for k in rows if k.startswith("MAP_GEO_") and k not in used]
    assert not dead, dead


def test_no_emoji_in_map_panel_and_the_national_checks():
    for path in (MAP_PANEL, NATIONAL_CHECKS, TINT_SHADER):
        text = path.read_text(encoding="utf-8")
        assert not EMOJI.search(text), (path.name, EMOJI.findall(text))


def test_the_sim_is_read_through_the_one_adapter():
    """The national map calls the plan's reads (§3) by name, in one adapter
    file; no other map script calls the facade for them. Tracks A and B are
    merged (the captain's C2 merge), so the adapter holds no stub: each read
    is the facade's own call, guarded only for a scene with no SimBridge, and
    nothing in it reads the geo files the sim embeds."""
    text = ADAPTER.read_text(encoding="utf-8")
    code = _code(ADAPTER)
    assert "STUB" not in text and "_stub" not in code
    assert "GeoData." not in code and "FileAccess" not in code and "read_json" not in code
    for name in PLAN_READS:
        assert f'has_read("{name}")' in code and f"_sim.{name}(" in code, name
    for path in sorted(MAP_SCRIPTS.glob("*.gd")) + [MAP_PANEL]:
        if path == ADAPTER:
            continue
        code = _code(path)
        for name in PLAN_READS:
            assert f".{name}(" not in code, f"{path.name} calls {name} outside the adapter"


def test_no_gdscript_parses_localities():
    """The 618 KB localities.geojson is never parsed in GDScript (plan §8): the
    national icons come from track A's packed arrays (get_geo_sites). This
    replaces C2's 'only the adapter's stub parses it', which the captain's
    merge made stronger by deleting the stub."""
    hits = [str(path.relative_to(REPO)) for path in sorted((REPO / "godot").rglob("*.gd"))
            if ".godot" not in path.parts and "localities.geojson" in _code(path)]
    assert not hits, hits


def test_the_tint_shader_samples_the_raw_index_nearest():
    text = TINT_SHADER.read_text(encoding="utf-8")
    decl = re.search(r"uniform sampler2D po_index\s*:\s*([^;]*);", text)
    assert decl and "filter_nearest" in decl.group(1) and "mipmap" not in decl.group(1)
    assert "uniform vec4 tints[14];" in text
    assert "uniform int hovered_id" in text and "uniform int selected_id" in text


def test_the_map_inset_is_modelled_by_theme_lint():
    """ThemeLint learns a TextureRect's lint_background (add-only), and the
    national map's base map sets it to the deep-water token."""
    lint = (REPO / "godot" / "tests" / "ThemeLint.gd").read_text(encoding="utf-8")
    assert 'c is TextureRect and c.has_meta(&"lint_background")' in lint
    view = (MAP_SCRIPTS / "GeoMapView.gd").read_text(encoding="utf-8")
    assert 'set_meta(&"lint_background", MapPalette.deep_water())' in view


def test_the_ui_layer_names_no_real_brand_spelling_of_the_rival():
    """D17 close-out, Track C's half: with MapPanel's REGIONS gone, nothing in
    godot/ spells the real brand 'NordLaks' (case-sensitive), and TextLint no
    longer exempts it. Adding it to BRANDS and D17_BRANDS waits for Track A's
    rename of the sim's roster (CompetitorAI, MarketIntelligence,
    InvestmentManager), which the merge order puts first."""
    brand = "Nord" + "Laks"
    hits = []
    for path in _text_files(REPO / "godot"):
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        if brand in text:
            hits.append(str(path.relative_to(REPO)))
    assert not hits, hits
    lint = (REPO / "godot" / "tests" / "TextLint.gd").read_text(encoding="utf-8")
    allowed = lint[lint.index("const ALLOWED_TOKENS"):lint.index("]", lint.index("const ALLOWED_TOKENS"))]
    assert f'"{brand}"' not in allowed


# ── Track D: the region and site levels (add-only) ────────────────────────────
# godot/scripts/ui/map/GeoRegionMath.gd, GeoRegionData.gd, GeoRegionView.gd,
# GeoSiteCard.gd, ..., godot/shaders/map_suitability.gdshader and
# godot/tests/GeoRegionChecks.gd.

import math  # noqa: E402

from test_geodata import utm33  # noqa: E402

SHADER = REPO / "godot" / "shaders" / "map_suitability.gdshader"
REGION_MATH = MAP_SCRIPTS / "GeoRegionMath.gd"
REGION_DATA = MAP_SCRIPTS / "GeoRegionData.gd"
REGION_CHECKS = REPO / "godot" / "tests" / "GeoRegionChecks.gd"
## The captain's D merge deleted GeoRegionBridge.gd (the stub adapter): the
## region and site levels read through GeoBridgeAdapter, Track C's one door.
TRACK_D_SCRIPTS = [MAP_SCRIPTS / f"{n}.gd" for n in (
    "GeoRegionMath", "GeoRegionData", "GeoRegionOverlay", "GeoRegionView",
    "GeoSiteCard", "MapBreadcrumbs", "RivalPresenceList", "GeoRegionDetail", "GeoDrillStack",
    "GeoRegionIcons", "GeoRegionRivals")]
REGION_REVIEW_CHECKS = REPO / "godot" / "tests" / "GeoRegionReviewChecks.gd"
## Keys built at run time (a prefix plus an id), listed so each one is pinned.
DYNAMIC_KEYS = (
    [f"MAP_REGION_REASON_{c}" for c in [*range(0, 14), *range(16, 25)]]
    + [f"MAP_REGION_CHIP_{g}" for g in ("HEAT", "RESTRICTIONS", "SITES", "CANDIDATES")]
    + [f"MAP_REGION_CHIP_{g}_TIP" for g in ("HEAT", "RESTRICTIONS", "SITES", "CANDIDATES")]
    + [f"MAP_REGION_LEGEND_{g}{t}" for g in ("CLOSED", "YOURS", "OFFSHORE") for t in ("", "_TIP")]
    + [f"MAP_REGION_STATE_{s}" for s in ("FREE", "SUGGESTED", "TAKEN", "YOURS")]
    + [f"MAP_SITE_TERM_{t}" for t in ("DEPTH", "EXCHANGE", "FARM_DISTANCE", "TEMPERATURE", "CURRENT",
                                       "WAVES", "OXYGEN", "RIVER", "STAGE", "NONE")]
    + [f"MAP_SITE_KIND_{k}" for k in ("SEA_SALMONID", "LAND_SALMONID", "OFFSHORE_SALMONID", "OTHER")]
    + [f"MAP_SITE_STAGE_{s.upper()}" for s in ("tilradning_2019", "anbefaling_2022",
                                                "konsekvensvurdering_2024", "kan_utlyses_2026")]
    + [f"MAP_SITE_LOCALITY_STAGE_{s}" for s in ("GROWOUT", "SMOLT", "BROODSTOCK", "RANCHING", "NURSERY", "OTHER")]
)


def _translation_rows() -> tuple[list[str], dict[str, list[str]]]:
    with TRANSLATIONS.open(encoding="utf-8", newline="") as f:
        rows = list(csv.reader(f))
    return rows[0], {r[0]: r for r in rows[1:]}


def _gd_const_in(path: Path, name: str) -> object:
    text = path.read_text(encoding="utf-8")
    m = re.search(rf"^const {name} := (\[.*?\]|\{{.*?^\}})", text, flags=re.MULTILINE | re.DOTALL)
    assert m, f"{path.name} has no const {name}"
    return ast.literal_eval(m.group(1))


def test_map_region_and_site_keys_exist_in_en_and_no():
    header, table = _translation_rows()
    en, no = header.index("en"), header.index("no")
    used: set[str] = set(DYNAMIC_KEYS)
    for path in [*TRACK_D_SCRIPTS, MAP_PANEL, REGION_CHECKS, REGION_REVIEW_CHECKS]:
        used |= set(re.findall(r'"(MAP_(?:REGION|SITE)_[A-Z0-9_]+)"', path.read_text(encoding="utf-8")))
    # A prefix completed at run time ("MAP_SITE_TERM_") is not a key itself.
    used = {k for k in used if not k.endswith("_")}
    missing = sorted(k for k in used if k not in table or not table[k][en].strip() or not table[k][no].strip())
    assert not missing, missing
    assert len(used) >= 140, len(used)


def test_the_region_keys_sit_sorted_in_the_tail():
    header, table = _translation_rows()
    keys = list(table)
    tail = keys[keys.index("CAPACITY_REASON_SITE_NOT_FOUND") + 1:]
    assert tail == sorted(tail)
    assert all(k in tail for k in keys if k.startswith(("MAP_REGION_", "MAP_SITE_")))


def test_map_suitability_shader_samples_the_raw_grid_nearest():
    text = SHADER.read_text(encoding="utf-8")
    assert text.startswith("shader_type canvas_item;")
    sampler = re.search(r"uniform sampler2D suit_tex\s*:\s*([^;]+);", text)
    assert sampler, "no suit_tex sampler"
    hints = {h.strip() for h in sampler.group(1).split(",")}
    assert "filter_nearest" in hints and "repeat_disable" in hints
    assert not any("mipmap" in h or "linear" in h for h in hints), hints
    for uniform in ("map_size", "suit_scale", "suit_offset"):
        assert re.search(rf"uniform vec2 {uniform}\b", text), uniform
    assert "UV * map_size * suit_scale + suit_offset" in text


def _shader_vec(text: str, name: str) -> list[float]:
    m = re.search(rf"const vec[34] {name} = vec[34]\(([^)]*)\);", text)
    assert m, name
    return [float(v) for v in m.group(1).split(",")]


def _shader_float(text: str, name: str) -> float:
    m = re.search(rf"const float {name} = ([0-9.]+);", text)
    assert m, name
    return float(m.group(1))


def _gd_colors(text: str, name: str) -> list[list[float]]:
    m = re.search(rf"^const {name}[^=]*= \[(.*?)^\]", text, flags=re.MULTILINE | re.DOTALL)
    assert m, name
    return [[float(v) for v in c.split(",")] for c in re.findall(r"Color\(([^)]*)\)", m.group(1))]


def test_the_shader_heat_equals_geo_region_math():
    shader = SHADER.read_text(encoding="utf-8")
    gd = REGION_MATH.read_text(encoding="utf-8")
    stops = [float(v) for v in re.search(r"const RAMP_STOPS: Array\[float\] = \[([^\]]*)\]", gd).group(1).split(",")]
    assert stops == [_shader_float(shader, f"STOP{i}") for i in range(3)] + [1.0]
    assert _gd_colors(gd, "RAMP_COLORS") == [_shader_vec(shader, f"RAMP{i}") for i in range(4)]
    for gd_name, sh_name in (("HEAT_ALPHA_LOW", "ALPHA_LOW"), ("HEAT_ALPHA_HIGH", "ALPHA_HIGH")):
        assert float(re.search(rf"const {gd_name} := ([0-9.]+)", gd).group(1)) == _shader_float(shader, sh_name)
    for gd_name, sh_name in (("HATCH_COLOR", "HATCH"), ("HATCH_QUIET_COLOR", "HATCH_QUIET")):
        gd_col = [float(v) for v in re.search(rf"const {gd_name} := Color\(([^)]*)\)", gd).group(1).split(",")]
        assert gd_col == _shader_vec(shader, sh_name), gd_name
    # The codes the hatch covers are the same on both sides.
    assert "g >= 4 && g <= 12" in shader and "g == 13" in shader
    assert "const HATCH_FIRST := 4" in gd and "const HATCH_LAST := 12" in gd
    assert "const CODE_OUTSIDE_PLANNING := 13" in gd


def test_geo_region_data_reads_the_geo_files_only_through_geodata():
    shader_preload = 'preload("res://shaders/map_suitability.gdshader")'
    for path in TRACK_D_SCRIPTS:
        code = "\n".join(_strip_comments(ln) for ln in path.read_text(encoding="utf-8").splitlines())
        assert not RESOURCE_LOAD.search(code.replace(shader_preload, "")), path.name
        assert "FileAccess" not in code and "load_png_from_buffer" not in code, path.name
        assert "localities.geojson" not in code, f"{path.name}: localities come from the sim's packed arrays"
    assert "GeoData.read_json(" in REGION_DATA.read_text(encoding="utf-8")


def test_suit_uv_puts_every_open_pen_candidate_in_its_own_cell():
    """The transform GeoRegionMath gives the shader (base-map pixel -> grid UV),
    recomputed in Python's float64: every candidate's UV lands in the cell
    test_geodata's (x - ox) // 500 lookup reads, and that cell is scored."""
    maps = load("basemap.json")["images"]
    grids = load("suitability.json")["grids"]
    decoded: dict[int, tuple] = {}
    n = 0
    for s in load("candidates.json")["sites"]:
        if s["type"] != "open_pen":
            continue
        n += 1
        po = s["po"]
        m, g = maps[f"po{po:02d}"], grids[str(po)]
        bx0, _, _, by1 = m["bounds_25833"]
        res, (ox, oy), w, h = m["res_m"], g["origin_25833"], g["width"], g["height"]
        px, py = (s["x"] - bx0) / res, (by1 - s["y"]) / res
        u = px * res / (500 * w) + (bx0 - ox) / (500 * w)
        v = py * res / (500 * h) + (oy - by1) / (500 * h)
        cell = (math.floor(u * w), math.floor(v * h))
        assert cell == ((s["x"] - ox) // 500, (oy - s["y"]) // 500), s["id"]
        if po not in decoded:
            decoded[po] = decode_png(GEO / g["file"])
        rows = decoded[po][3]
        assert rows[cell[1]][cell[0] * 3] > 0, f"{s['id']}: its cell is not scored"
    assert n == 156


def _overlay_counts(po: int) -> dict[str, int]:
    """GeoRegionData's features per layer in an area: the base map's bounds,
    each feature's projected extent (fairway buffer, point radius) meeting them."""
    info = load("basemap.json")["images"][f"po{po:02d}"]
    x0, _, _, y1 = info["bounds_25833"]
    res, w, h = info["res_m"], info["width"], info["height"]

    def ring(coords):
        out = []
        for lon, lat in coords:
            x, y = utm33(lon, lat)
            out.append(((x - x0) / res, (y1 - y) / res))
        return out

    def meets(points, grow=0.0):
        xs, ys = [p[0] for p in points], [p[1] for p in points]
        return not (min(xs) - grow >= w or max(xs) + grow <= 0 or min(ys) - grow >= h or max(ys) + grow <= 0)

    kinds = load("constraint_points.geojson")["meta"]["kinds"]
    files = {"locality_areas": "locality_areas.geojson", "mooring_areas": "mooring_areas.geojson",
             "fairways": "fairways.regional.geojson", "exclusions": "exclusions.regional.geojson",
             "constraint_points": "constraint_points.geojson"}
    counts = {}
    for layer, rel in files.items():
        n = 0
        for f in load(rel)["features"]:
            g, p = f["geometry"], f["properties"]
            if g["type"] == "Point":
                r = kinds.get(p["kind"], {}).get("radius_m", 0.0) / res
                x, y = (p["x"] - x0) / res, (y1 - p["y"]) / res
                n += -r <= x < w + r and -r <= y < h + r
            elif g["type"] == "LineString":
                line = ring(g["coordinates"])
                n += len(line) >= 2 and meets(line, (p.get("buffer_m") or 0.0) / res)
            else:
                polys = [g["coordinates"]] if g["type"] == "Polygon" else g["coordinates"]
                outer = []
                for poly in polys:
                    r = ring(poly[0]) if poly else []
                    if len(r) > 1 and r[0] == r[-1]:
                        r = r[:-1]
                    if len(r) >= 3:
                        outer.extend(r)
                n += bool(outer) and meets(outer)
        counts[layer] = n
    return counts


def test_the_pinned_overlay_counts_equal_the_reference_projection():
    pinned = _gd_const_in(REGION_CHECKS, "OVERLAY_COUNTS")
    assert sorted(pinned) == [3, 6]
    for po, want in pinned.items():
        assert _overlay_counts(po) == want, po


# ── The captain's D merge (add-only) ──────────────────────────────────────────

def _region_keys_used() -> set[str]:
    used: set[str] = set(DYNAMIC_KEYS)
    for path in [*TRACK_D_SCRIPTS, MAP_PANEL, REGION_CHECKS, REGION_REVIEW_CHECKS]:
        used |= set(re.findall(r'"(MAP_(?:REGION|SITE)_[A-Z0-9_]+)"', path.read_text(encoding="utf-8")))
    return {k for k in used if not k.endswith("_")}


def test_every_map_region_and_site_row_is_used():
    """No MAP_REGION_* or MAP_SITE_* row is left behind (C2's rule for its
    MAP_GEO_* rows): the locality-name row the rival card no longer shows
    (D17) went with it."""
    _, table = _translation_rows()
    used = _region_keys_used()
    dead = sorted(k for k in table if k.startswith(("MAP_REGION_", "MAP_SITE_")) and k not in used)
    assert not dead, dead


def test_all_pinned_overlay_counts_equal_the_reference_projection():
    """Every area's layer counts, a fairway's buffer and a point's radius
    counted as its extent (the review's PO10: six river mouths 3-5 km off the
    map whose 5 km rings reach onto it)."""
    pinned = _gd_const_in(REGION_REVIEW_CHECKS, "ALL_OVERLAY_COUNTS")
    assert sorted(pinned) == list(range(1, 14))
    for po, want in pinned.items():
        assert _overlay_counts(po) == want, po
    # PO3 and PO6 agree with GeoRegionChecks' own pins.
    region = _gd_const_in(REGION_CHECKS, "OVERLAY_COUNTS")
    assert all(pinned[po] == region[po] for po in region)


def test_a_double_click_opens_the_cards_the_site_level_has():
    """GeoMapView's CARD_KINDS (a double-click asks for the feature's card)
    are the kinds GeoDrillStack.open_feature answers, and MapPanel wires the
    chart's feature_drill_requested to it."""
    def kinds(path: Path, name: str) -> list[str]:
        m = re.search(rf"^const {name}: Array\[String\] = (\[[^\]]*\])", path.read_text(encoding="utf-8"), flags=re.M)
        assert m, f"{path.name} has no {name}"
        return ast.literal_eval(m.group(1))
    assert kinds(MAP_SCRIPTS / "GeoMapView.gd", "CARD_KINDS") == kinds(MAP_SCRIPTS / "GeoDrillStack.gd", "FEATURE_KINDS")
    panel = MAP_PANEL.read_text(encoding="utf-8")
    assert "_chart.feature_drill_requested.connect(_on_feature_drill_requested)" in panel


def test_no_map_script_shows_the_registry_locality_names():
    """D17: get_geo_sites carries the registry's locality names, and some
    localities are named after the company that holds them; no map script
    reads them (the rival card names a locality by its number)."""
    for path in sorted(MAP_SCRIPTS.glob("*.gd")) + [MAP_PANEL]:
        code = _code(path)
        assert '"names"' not in code and ".names" not in code, path.name
