"""The sim side of the geo data (geo stages 2-3, track A; docs/geo/STAGE23_PLAN.md).

A1 (the embedded readers): LakskongenSim.csproj embeds every geo file the
readers in src/LakskongenSim/Environment/Geo name, under the LogicalName the
reader asks for, through explicit names or extension-specific globs only (never
`*.*`, which would also embed the Godot .import sidecars beside the PNGs), and
every embedded file is a manifest-listed file under godot/data/geo.

A2/A3 (the geo bridge lane): GameBridge.Geo.cs declares the plan §3 verbs with
their keys and stays pull-only, SimProxies.gd and SimBridge.gd each carry one
sim·geo lane block with the forwarders, the determinism scan covers the
partial, GetCompetitorCompanies adds home_po_sites, and no Nordlaks literal
remains outside MapPanel.gd (D17).
"""

from __future__ import annotations

import fnmatch
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CSPROJ = REPO / "src" / "LakskongenSim" / "LakskongenSim.csproj"
SIM_DIR = CSPROJ.parent
GEO = REPO / "godot" / "data" / "geo"
GEO_SRC = SIM_DIR / "Environment" / "Geo"
RESOURCES_CS = GEO_SRC / "GeoResources.cs"


def _geo_item_group() -> ET.Element:
    root = ET.parse(CSPROJ).getroot()
    groups = [g for g in root.iter("ItemGroup") if g.get("Label") == "geo"]
    assert len(groups) == 1, "LakskongenSim.csproj needs exactly one ItemGroup Label=\"geo\""
    return groups[0]


def _embedded() -> dict[str, Path]:
    """LogicalName -> source file for every EmbeddedResource of the geo group, globs expanded."""
    out: dict[str, Path] = {}
    for item in _geo_item_group().iter("EmbeddedResource"):
        include = item.get("Include", "").replace("\\", "/")
        logical = item.get("LogicalName", "")
        directory = (SIM_DIR / Path(include).parent).resolve()
        pattern = Path(include).name
        files = sorted(p for p in directory.iterdir() if p.is_file() and fnmatch.fnmatchcase(p.name, pattern))
        assert files, f"{include} matches no file"
        for path in files:
            name = (logical.replace("%(Filename)", path.stem).replace("%(Extension)", path.suffix))
            assert name not in out, f"{name} is embedded twice"
            out[name] = path
    return out


def _reader_names() -> set[str]:
    """The logical names GeoResources.cs asks for: its constants and the 13 grid names."""
    text = RESOURCES_CS.read_text(encoding="utf-8")
    prefix = re.search(r'const string Prefix = "([^"]+)";', text).group(1)
    names = {prefix + m for m in re.findall(r'= Prefix \+ "([^"]+)";', text)}
    grid = re.search(r'\$"\{Prefix\}(Suitability\.po)\{productionArea:00\}(\.png)"', text)
    assert grid, "GeoResources.SuitabilityGrid's name format changed"
    names |= {f"{prefix}{grid.group(1)}{po:02d}{grid.group(2)}" for po in range(1, 14)}
    return names


def test_the_geo_group_uses_explicit_names_or_extension_globs_only():
    for item in _geo_item_group().iter("EmbeddedResource"):
        include = item.get("Include", "")
        name = Path(include.replace("\\", "/")).name
        assert "*.*" not in include and name != "*", f"{include}: never *.* or a bare * under data/geo"
        assert "**" not in include, f"{include}: no recursive glob under data/geo"
        if "*" in name:
            assert re.fullmatch(r"[\w.-]*\*\.(png|json|geojson)", name), f"{include}: glob must fix the extension"
        assert item.get("LogicalName", "").startswith("LakskongenSim.Geo."), include
    text = CSPROJ.read_text(encoding="utf-8")
    assert not re.search(r"data\\geo\\[^\"]*\*\.\*", text), "no *.* glob under data/geo anywhere in the csproj"


def test_the_csproj_embeds_every_file_the_readers_name():
    embedded = _embedded()
    assert set(embedded) == _reader_names()
    assert len(embedded) == 18
    assert not any(p.suffix == ".import" for p in embedded.values())


def test_every_embedded_geo_file_is_a_manifest_file():
    files = json.loads((GEO / "manifest.json").read_text(encoding="utf-8"))["files"]
    for name, path in _embedded().items():
        rel = path.relative_to(GEO.resolve()).as_posix()
        assert rel in files, f"{name}: {rel} is not in manifest.json"


def test_the_geo_readers_never_open_a_file():
    """The engine never touches the file system (GEODATA §7.2): no File/Directory/
    Path/FileStream/StreamReader call in the readers (the C# GeoNoFileSystemTests
    scan is syntax-aware; this one guards the Python CI job)."""
    pattern = re.compile(r"\b(File|Directory|Path|FileStream|FileInfo|StreamReader|StreamWriter)\s*[.(]")
    hits = []
    for path in sorted(GEO_SRC.glob("*.cs")):
        for no, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            code = line.split("//", 1)[0]
            if pattern.search(code):
                hits.append(f"{path.relative_to(REPO)}:{no}: {line.strip()}")
    assert not hits, hits


# ── A2/A3: the geo bridge lane (plan §3 "Geo bridge lane") ──────────────────────

AUTOLOAD = REPO / "godot" / "scripts" / "autoload"
GEO_BRIDGE = AUTOLOAD / "GameBridge.Geo.cs"
PROXIES = REPO / "godot" / "scripts" / "bridge" / "SimProxies.gd"
FACADE = REPO / "godot" / "scripts" / "SimBridge.gd"
GD_ARRAY = "Godot.Collections.Array<Godot.Collections.Dictionary>"
GD_DICT = "Godot.Collections.Dictionary"

# C# verb -> (return type, parameters, facade func, facade params, facade return).
GEO_VERBS = {
    "GetGeoProductionAreas": (GD_ARRAY, "", "get_geo_production_areas", "", "Array[Dictionary]"),
    "GetGeoPlotBinding": (GD_DICT, "string plotId", "get_geo_plot_binding", "plot_id: String", "Dictionary"),
    "GetGeoOffshoreCandidates": (GD_ARRAY, "", "get_geo_offshore_candidates", "", "Array[Dictionary]"),
    "GeoAreaAt": ("int", "double x, double y", "get_geo_area_at", "x: float, y: float", "int"),
    "GetGeoSitesSignature": ("string", "", "get_geo_sites_signature", "", "String"),
    "GetGeoSites": (GD_DICT, "", "get_geo_sites", "", "Dictionary"),
    "GetRivalSummary": (GD_DICT, "int po", "get_rival_summary", "po: int", "Dictionary"),
    "GetZoneAnchors": (GD_ARRAY, "", "get_zone_anchors", "", "Array[Dictionary]"),
}

# The dictionary keys each verb's consumers (tracks B, C, D) read.
GEO_KEYS = {
    "GetGeoProductionAreas": ("id", "code", "name", "official_traffic_light", "tint", "area_km2", "sea_area_km2",
                              "label_x", "label_y"),
    "GetGeoPlotBinding": ("bound", "plot_id", "candidate_id", "area", "stage", "stage_factor", "po", "depth_m",
                          "score", "binding", "distance_km", "x", "y", "lon", "lat", "near", "home_po"),
    "GetGeoOffshoreCandidates": ("id", "candidate_id", "area", "stage", "stage_factor", "po", "depth_m", "score",
                                 "binding", "x", "y", "lon", "lat", "near", "bound_plot_id"),
    "GetGeoSites": ("signature", "ids", "x", "y", "kind", "po", "mtb_t", "owner", "owners", "kinds"),
    "GetRivalSummary": ("sites_by_kind", "sea_growout_mtb_t", "top_owners", "sites", "mtb_t", "is_major"),
    "GetZoneAnchors": ("zone_id", "production_area", "locality_id", "name", "x", "y", "source"),
}


def _member_body(src: str, name: str) -> str:
    """The text from a member's declaration to the next member."""
    start = re.search(rf"(?:public|private) [^\n(]*\b{name}\(", src)
    assert start, f"{name} is not declared"
    following = re.search(r"\n    (?:/// <summary>|public |private )", src[start.end():])
    return src[start.start():start.end() + (following.start() if following else len(src))]


def test_the_geo_bridge_declares_the_lanes_verbs_and_their_keys():
    src = GEO_BRIDGE.read_text(encoding="utf-8")
    for verb, (ret, params, *_rest) in GEO_VERBS.items():
        assert re.search(rf"public {re.escape(ret)} {verb}\({re.escape(params)}\)", src), f"{verb}({params}) -> {ret}"
    for verb, keys in GEO_KEYS.items():
        body = _member_body(src, verb)
        if verb == "GetGeoSites":
            body += _member_body(src, "RefreshGeoOwners")
        missing = [k for k in keys if f'["{k}"]' not in body]
        assert not missing, f"{verb} lacks keys {missing}"
    # Pull-only: the lane emits no signal and never advances or writes the game.
    assert "EmitSignal" not in src
    assert not re.search(r"\.(Advance|Build\w+|Apply\w+|StartScenario)\(", src), "the geo lane only reads"


def _geo_block(src: str) -> str:
    begin = re.findall(r"(?m)^.*BEGIN sim·geo lane block.*$", src)
    end = re.findall(r"(?m)^.*END sim·geo lane block.*$", src)
    assert len(begin) == 1 and len(end) == 1, f"one sim·geo lane block expected ({len(begin)}/{len(end)})"
    start, stop = src.index(begin[0]), src.index(end[0])
    assert start < stop
    return src[start:stop]


def test_each_facade_file_has_one_geo_lane_block_with_the_forwarders():
    proxies, facade = PROXIES.read_text(encoding="utf-8"), FACADE.read_text(encoding="utf-8")
    proxy_block, facade_block = _geo_block(proxies), _geo_block(facade)
    for verb, (_ret, _params, func, fparams, fret) in GEO_VERBS.items():
        assert re.search(rf"func {func}\({re.escape(fparams)}\) -> {re.escape(fret)}:\s*\n?\s*return .*game_bridge\.{verb}\(",
                         proxy_block), f"SimProxies geo block lacks {func} -> {verb}"
        args = ", ".join(p.split(":")[0] for p in fparams.split(", ")) if fparams else ""
        assert re.search(rf"func {func}\({re.escape(fparams)}\) -> {re.escape(fret)}: return _proxies\.{func}\({re.escape(args)}\)",
                         facade_block), f"SimBridge geo block lacks {func}"
        # Exactly once in each file, inside the block.
        assert len(re.findall(rf"(?m)^func {func}\(", proxies)) == 1
        assert len(re.findall(rf"(?m)^func {func}\(", facade)) == 1
    # The geo block sits after the sea lane block (track B's candidates block follows it).
    for src in (proxies, facade):
        assert src.index("END sim·sea lane block") < src.index("BEGIN sim·geo lane block")


def test_the_determinism_scan_covers_the_geo_bridge():
    scanner = (REPO / "src" / "LakskongenSim.Tests" / "Core" / "DeterminismScanner.cs").read_text(encoding="utf-8")
    scope = scanner[scanner.index("BridgeScope = new[]"):]
    scope = scope[:scope.index("};")]
    assert '"godot/scripts/autoload/GameBridge.Geo.cs"' in scope


def test_competitor_companies_keep_their_keys_and_add_home_po_sites_in_the_layouts_order():
    depth = (AUTOLOAD / "GameBridge.Depth.cs").read_text(encoding="utf-8")
    body = _member_body(depth, "GetCompetitorCompanies")
    for key in ("id", "name", "market_share", "position_x", "position_z", "strategy", "market_cap", "home_po_sites"):
        assert f'["{key}"]' in body, key
    assert "CompetitorsByHomePresence()" in body


NORDLAKS = re.compile(r"Nord[Ll]aks")


def test_no_nordlaks_literal_remains_in_the_sim_the_scripts_or_the_data():
    """D17 (ruling OD8): the renamed rival, producer and label leave no
    'Nordlaks'/'NordLaks' string in the sim, the autoloads, the game's
    scripts or the game data. Track C's merge retired MapPanel.gd's REGIONS,
    the last exception, so every GDScript under godot/scripts is scanned too
    (the captain's C2 merge; this replaces '..._outside_the_map_panel')."""
    from test_text_correctness import cs_scan

    hits = []
    for path in sorted((REPO / "src" / "LakskongenSim").rglob("*.cs")) + sorted(AUTOLOAD.glob("*.cs")):
        if "/bin/" in path.as_posix() or "/obj/" in path.as_posix():
            continue
        strings, _ = cs_scan(path)
        hits += [f"{path.relative_to(REPO)}:{ln}: {s}" for ln, s in strings if NORDLAKS.search(s)]
    for path in sorted((REPO / "godot" / "data").rglob("*.json")):
        for ln, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if NORDLAKS.search(line) and "geo/manifest.json" not in path.as_posix():
                hits.append(f"{path.relative_to(REPO)}:{ln}")
    for path in sorted(AUTOLOAD.glob("*.gd")) + sorted((REPO / "godot" / "scripts").rglob("*.gd")):
        if NORDLAKS.search(path.read_text(encoding="utf-8")):
            hits.append(str(path.relative_to(REPO)))
    assert not hits, hits
    # The guard sees a planted literal.
    assert NORDLAKS.search('Name = "NordLaks Nord AS"') and NORDLAKS.search("Nordlaks' Havfarm")


def test_the_rival_layout_and_names_never_open_a_file():
    pattern = re.compile(r"\b(File|Directory|Path|FileStream|FileInfo|StreamReader|StreamWriter|Random)\s*[.(]")
    finance = REPO / "src" / "LakskongenSim" / "Finance"
    files = sorted(finance.glob("Rival*.cs")) + sorted(finance.glob("ParodyNames*.cs"))
    assert len(files) >= 2
    hits = []
    for path in files:
        for no, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if pattern.search(line.split("//", 1)[0]):
                hits.append(f"{path.relative_to(REPO)}:{no}: {line.strip()}")
    assert not hits, hits


# Keys the geo lane adds beyond plan §3 that the consumers read (tracks C and
# D: the offshore hexagons' rank, state and distance, the zone anchors'
# in_game; the national map's label lon/lat; the locality card's stage).
GEO_ADDITIVE_KEYS = {
    "GetGeoProductionAreas": ("label_lon", "label_lat"),
    "GetGeoOffshoreCandidates": ("rank", "distance_km", "bound"),
    "GetGeoSites": ("stage", "names", "stages"),
    "GetRivalSummary": ("po",),
    "GetZoneAnchors": ("in_game",),
}


def test_the_geo_bridge_keeps_the_additive_keys_its_consumers_read():
    """Captain's review of track A: the offshore list's rank, distance_km and
    bound (and the other additive keys) were not pinned anywhere."""
    src = GEO_BRIDGE.read_text(encoding="utf-8")
    for verb, keys in GEO_ADDITIVE_KEYS.items():
        body = _member_body(src, verb)
        missing = [k for k in keys if f'["{k}"]' not in body]
        assert not missing, f"{verb} lacks keys {missing}"


def test_the_offshore_list_never_measures_from_area_zero():
    """Captain's review of track A, finding 1: with no home area (a game with no
    scenario, or before any game) GetGeoOffshoreCandidates must not look up
    area 0's label (GeoProductionAreas.Get throws outside 1-13); it measures
    through GeoSitePlotGeometry.DistanceKmFrom, which has no distance there."""
    body = _member_body(GEO_BRIDGE.read_text(encoding="utf-8"), "GetGeoOffshoreCandidates")
    assert "GeoSitePlotGeometry.DistanceKmFrom(" in body
    assert "NationalReference).Label" not in body and "GeoProductionAreas.Default.Get(" not in body


def test_the_geo_lane_blocks_follow_the_sea_lane_with_nothing_between():
    """Hot-file rule (plan §4): each facade file gets the sim·geo block and
    nothing else, like the land and sea lanes: the BEGIN line follows the sea
    lane's END after one blank line, and every line of the lane is inside it."""
    for path in (PROXIES, FACADE):
        lines = path.read_text(encoding="utf-8").splitlines()
        begin = next(i for i, line in enumerate(lines) if "BEGIN sim·geo lane block" in line)
        assert lines[begin - 1] == "" and "END sim·sea lane block" in lines[begin - 2], \
            f"{path.relative_to(REPO)}: only a blank line between the sea lane's END and the geo lane's BEGIN"
