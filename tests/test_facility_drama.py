"""F2-24 weather drama and incident poses (farm-types spec §4 "Weather drama",
§5.4 Animation, §7.2 F2-24 acceptance 1-5).

The behaviour itself is godot/tests/FacilityDramaChecks.gd, run here when Godot
is installed (the UI flow suite's check count is frozen, SHIP_PLAN §3). These
checks hold the pieces together where Godot is not needed:

- the thresholds are the spec's (spray over Hs 3 m, a list of at most 3 degrees,
  steam under -2 degC) and the engine's own names are the ones asked for;
- the spray's 12 points are the OF1 columns of the built mesh, and the steam's
  vents are the roof units of the built halls (the Blender design, then the
  glbs' own vertices), so a redesigned hull or hall cannot leave them floating;
- the nav lanterns flash on the shader's global TIME at 1 Hz, in step across units;
- the presenters are wired: the unit takes the engine's sea and open incident,
  the campus its halls and the weather, and neither particle node is drawn in
  the planar mirror or from far off;
- the marker colour lint (tools/lint_markers.py) passes and fails where it should.
"""

import importlib.util
import json
import math
import os
import re
import shutil
import struct
import subprocess
import tempfile
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
VISUAL = GODOT / "scripts" / "visual"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


DRAMA = _read(VISUAL / "FacilityDrama.gd")
SPRAY = _read(VISUAL / "particles" / "UnitSpray.gd")
STEAM = _read(VISUAL / "particles" / "RoofSteam.gd")
UNIT = _read(VISUAL / "OffshoreUnitVisual.gd")
MANAGER = _read(VISUAL / "FarmFacilityVisualManager.gd")
CAMPUS = _read(VISUAL / "LandCampusBuilder.gd")
UNIT_MODEL = _read(VISUAL / "OffshoreUnitModel.gd")
SPEC = _read(REPO / "docs" / "FARM_TYPES_SPEC.md")
OFFSHORE_DESIGN = _read(REPO / "blender" / "offshore_designs.py")
LAND_DESIGN = _read(REPO / "blender" / "land_designs.py")
NAV_SHADER = _read(GODOT / "shaders" / "offshore_kit.gdshader")


def _const(text: str, name: str) -> float:
    m = re.search(rf"^const {name}(?:\s*:\s*[\w\[\]]+)?\s*:?=\s*(-?[\d.]+)", text, re.M)
    assert m, f"const {name} not found"
    return float(m.group(1))


def _py(text: str, name: str) -> float:
    """A module-level number of a Python source (R = 55.0  # ...)."""
    m = re.search(rf"^{name}\s*=\s*(-?[\d.]+)", text, re.M)
    assert m, f"{name} not found"
    return float(m.group(1))


def _export(text: str, name: str) -> float:
    """The default of a GDScript `@export var name: float = 55.0`."""
    m = re.search(rf"^@export var {name}\s*:\s*float\s*=\s*(-?[\d.]+)", text, re.M)
    assert m, f"@export var {name} not found"
    return float(m.group(1))


def _code(text: str) -> str:
    """A GDScript source without its comments (a `##` line or a trailing `# ...`)."""
    return "\n".join(line.split(" #")[0] for line in text.splitlines() if not line.lstrip().startswith("#"))


def _function(text: str, name: str) -> str:
    """The body of a GDScript function: from its `func` line to the next one."""
    start = text.index(f"func {name}(")
    end = text.find("\nfunc ", start + 1)
    return text[start:] if end < 0 else text[start:end]


def _vectors(text: str, name: str) -> list[tuple[float, float, float]]:
    """The Vector3 literals of `const NAME: Array[Vector3] = [ ... ]`."""
    start = text.index("= [", re.search(rf"^const {name}\b", text, re.M).start()) + 2
    depth, end = 0, start
    for end in range(start, len(text)):
        depth += {"[": 1, "]": -1}.get(text[end], 0)
        if depth == 0:
            break
    return [tuple(float(v) for v in m) for m in re.findall(r"Vector3\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)", text[start:end])]


def _glb_points(path: Path, node: str | None = None) -> list[tuple[float, float, float]]:
    """The positions of a glb's meshes (all, or one node's), in glTF's own space
    (the nodes read carry no transform; Godot imports them as they are)."""
    data = path.read_bytes()
    off, doc, blob = 12, None, b""
    while off < len(data):
        length, kind = struct.unpack("<II", data[off:off + 8])
        body = data[off + 8:off + 8 + length]
        if kind == 0x4E4F534A:
            doc = json.loads(body)
        elif kind == 0x004E4942:
            blob = body
        off += 8 + length
    assert doc is not None
    roots = doc["scenes"][doc.get("scene", 0)]["nodes"]
    picked = [i for i, n in enumerate(doc["nodes"]) if "mesh" in n and (node is None or n.get("name") == node)]
    assert picked, f"no mesh node {node}"
    # The nodes read are scene roots with no transform: their vertices are in model space.
    assert all(i in roots and not any(k in doc["nodes"][i] for k in ("translation", "rotation", "scale", "matrix")) for i in picked)
    meshes = [doc["nodes"][i]["mesh"] for i in picked]
    out = []
    for mi in meshes:
        for prim in doc["meshes"][mi]["primitives"]:
            acc = doc["accessors"][prim["attributes"]["POSITION"]]
            view = doc["bufferViews"][acc["bufferView"]]
            start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
            stride = view.get("byteStride", 12)
            out.extend(struct.unpack_from("<fff", blob, start + i * stride) for i in range(acc["count"]))
    return out


# ── Thresholds ───────────────────────────────────────────────────────────────

def test_the_thresholds_are_the_specs():
    """Spec §4 "Spray rises at the columns above Hs 3 m"; §5.4 "a <= 3 degree
    list pose until resolved" and "Roof vents steam below -2 degC air"."""
    assert "Spray rises at the columns above Hs 3 m" in SPEC
    assert "Tilt incident: a ≤ 3° list pose until resolved" in SPEC
    assert "Roof vents steam below −2 °C air" in SPEC
    assert _const(DRAMA, "SPRAY_FROM_HS_M") == 3.0
    assert _const(DRAMA, "SPRAY_FULL_HS_M") > _const(DRAMA, "SPRAY_FROM_HS_M")
    assert 0.0 < _const(DRAMA, "SPRAY_MIN_LEVEL") < 1.0
    assert 0.5 <= _const(DRAMA, "LIST_DEG") <= 3.0
    assert _const(DRAMA, "STEAM_BELOW_C") == -2.0
    assert _const(DRAMA, "STEAM_FULL_C") < _const(DRAMA, "STEAM_BELOW_C")
    assert 0.0 < _const(DRAMA, "STEAM_MIN_LEVEL") < 1.0


def test_the_rules_are_pure_and_ask_the_engines_names():
    """FacilityDrama reads nothing: its inputs are handed to it. The incident is
    the engine's OffshoreIncidentKind name (the handling state gives
    kind.ToString(), so "Tilt")."""
    assert "SimBridge" not in _code(DRAMA) and "get_node" not in _code(DRAMA) and "extends RefCounted" in DRAMA
    engine = _read(REPO / "src" / "LakskongenSim" / "Farm" / "Offshore" / "OffshoreIncident.cs")
    assert re.search(r"^\s*Tilt = 0,", engine, re.M)
    assert re.search(r'^const TILT_KIND := "tilt"', DRAMA, re.M)
    assert "incident_kind.to_lower() == TILT_KIND" in DRAMA
    views = _read(REPO / "src" / "LakskongenSim" / "Core" / "SimulationEngine.OffshoreSeaViews.cs")
    assert 'incident?.Kind.ToString() ?? ""' in views
    bridge = _read(GODOT / "scripts" / "autoload" / "GameBridge.Offshore.cs")
    assert 'result["active_incident_kind"] = v.ActiveIncidentKind;' in bridge
    for wrapper in (GODOT / "scripts" / "SimBridge.gd", GODOT / "scripts" / "bridge" / "SimProxies.gd"):
        assert "func get_offshore_handling_state(site_id: String)" in _read(wrapper)


# ── Where the spray and the steam come from ──────────────────────────────────

def test_the_spray_points_are_the_columns_of_the_built_unit():
    """The 12 points at the design's circumradius, vertex k at TAU * k / 12 in
    Blender x/y (Godot x/-z), against the OF1 glb's own column vertices."""
    assert _py(OFFSHORE_DESIGN, "R") == _export(UNIT_MODEL, "circumradius_m") == 55.0
    sides = int(_py(OFFSHORE_DESIGN, "SIDES"))
    assert sides == int(_const(DRAMA, "COLUMN_COUNT")) == 12
    assert "var a := TAU * float(k) / float(count)" in DRAMA
    assert "Vector3(circumradius_m * cos(a), SPRAY_EMIT_HEIGHT_M, -circumradius_m * sin(a))" in DRAMA
    r = _py(OFFSHORE_DESIGN, "R")
    centres = [(r * math.cos(math.tau * k / sides), -r * math.sin(math.tau * k / sides)) for k in range(sides)]
    splash = [p for p in _glb_points(GODOT / "assets" / "models" / "offshore" / "of1_class_LOD0.glb", "Columns")
              if -3.0 <= p[1] <= 3.0 and math.hypot(p[0], p[2]) > 10.0]
    per_column = [0] * sides
    for x, _, z in splash:
        dist = [math.hypot(x - cx, z - cz) for cx, cz in centres]
        assert min(dist) < 2.2, (x, z)  # the large columns are 3.56 m across
        per_column[dist.index(min(dist))] += 1
    assert all(n >= 8 for n in per_column), per_column
    assert 0.0 < _const(DRAMA, "SPRAY_EMIT_HEIGHT_M") < 3.0


def _design_vents() -> dict[str, list[tuple[float, float, float, float, float]]]:
    """The roof units the Blender design puts on the halls, as Godot
    (x, top, z, size x, size z): Blender x/y/z to x/z/-y."""
    bay = re.search(r"def process_hall_bay\(b, lod\):.*?(?=\ndef )", LAND_DESIGN, re.S).group(0)
    ridge = float(re.search(r"eave, ridge = ([\d.]+), ([\d.]+)", bay).group(2))
    m = re.search(r"for x in \(([-\d.]+), ([-\d.]+)\):\s*\n\s*roof_unit\(b, x, 0\.0, ridge - ([\d.]+), ([\d.]+), ([\d.]+), ([\d.]+), lod\)", bay)
    assert m, "the bay's roof units moved"
    xs, below, sx, sy, sz = (float(m.group(1)), float(m.group(2))), float(m.group(3)), float(m.group(4)), float(m.group(5)), float(m.group(6))
    bays = [(x, ridge - below + sz, 0.0, sx, sy) for x in xs]
    hall = re.search(r"def harvest_hall\(b, lod\):.*?(?=\n# -)", LAND_DESIGN, re.S).group(0)
    h = float(re.search(r"x0, x1, y0, y1, h = [^,]+, [^,]+, [^,]+, [^,]+, ([\d.]+)", hall).group(1))
    m = re.search(r"for k, x in enumerate\(\(([^)]*)\)\):\s*\n\s*roof_unit\(b, x, ([-\d.]+) \+ ([\d.]+) \* \(k % 2\), h - ([\d.]+), ([\d.]+), ([\d.]+), ([\d.]+), lod\)", hall)
    assert m, "the harvest hall's roof units moved"
    hx = [float(v) for v in m.group(1).split(",")]
    y0, step, below, sx, sy, sz = (float(m.group(i)) for i in range(2, 8))
    halls = [(x, h - below + sz, -(y0 + step * (k % 2)), sx, sy) for k, x in enumerate(hx)]
    return {"process_hall_bay": bays, "harvest_hall": halls}


@pytest.mark.parametrize("asset,const", [("process_hall_bay", "BAY_VENTS"), ("harvest_hall", "HARVEST_VENTS")])
def test_the_steam_vents_are_the_roof_units_of_the_halls(asset, const):
    """FacilityDrama's vents equal the design's roof units, and the built
    glb has a roof box top of that size at each."""
    vents = _vectors(DRAMA, const)
    design = _design_vents()[asset]
    assert len(vents) == len(design)
    for v, (x, top, z, sx, sz) in zip(vents, design):
        assert v == pytest.approx((x, top, z), abs=1e-6), (asset, v, (x, top, z))
    points = _glb_points(GODOT / "assets" / "models" / "land_kit" / f"{asset}_LOD1.glb")
    for x, top, z, sx, sz in design:
        face = [p for p in points if abs(p[1] - top) < 0.005 and abs(p[0] - x) <= sx / 2 + 0.01 and abs(p[2] - z) <= sz / 2 + 0.01]
        assert len(face) >= 4, (asset, x, z)
        lo_x, hi_x = min(p[0] for p in face), max(p[0] for p in face)
        lo_z, hi_z = min(p[2] for p in face), max(p[2] for p in face)
        assert (lo_x + hi_x) / 2 == pytest.approx(x, abs=0.02) and (lo_z + hi_z) / 2 == pytest.approx(z, abs=0.02)
        assert hi_x - lo_x == pytest.approx(sx, abs=0.02) and hi_z - lo_z == pytest.approx(sz, abs=0.02)


def test_the_nav_lanterns_flash_in_step_at_one_hertz():
    """Spec §5.4: "Nav lights blink in sync at 1 Hz at night" (Kystverket
    synchronised yellow): the phase is the global TIME, nothing per unit."""
    assert re.search(r"uniform float nav_flash_hz[^;]*=\s*1\.0\s*;", NAV_SHADER)
    assert "float ph = fract(TIME * nav_flash_hz);" in NAV_SHADER
    nav = NAV_SHADER[NAV_SHADER.index("if (nav) {"):]
    nav = nav[: nav.index("} else if (glow)")]
    assert "INSTANCE" not in nav and "NODE_POSITION" not in nav and "MODEL_MATRIX" not in nav
    assert not re.search(r"instance uniform[^;]*(phase|nav)", NAV_SHADER)
    assert 'OFFSHORE_KIT.set_shader_parameter("nav_lights", 1.0 if level > 0.5 else 0.0)' in MANAGER


# ── The presenters take their inputs ─────────────────────────────────────────

def test_the_unit_takes_the_engines_sea_and_open_incident():
    assert "spray = UnitSpray.new()" in UNIT and "unit.add_child(spray)" in UNIT
    assert "spray.build(FacilityDrama.column_points(unit.circumradius_m))" in UNIT
    assert 'spray.set_hs(float((forecast.get("today", {}) as Dictionary).get("hs", 0.0)) if bool(forecast.get("ok", false)) else 0.0)' in UNIT
    assert "list_deg = move_toward(list_deg, FacilityDrama.list_target_deg(incident_kind), FacilityDrama.LIST_RATE_DEG_S * delta)" in UNIT
    assert "atan(plane.z) * TILT_GAIN + list_rad" in UNIT and "unit.rotation = Vector3(0.0, heading, list_rad)" in UNIT
    assert 'unit.set_incident(str(sim.get_offshore_handling_state(id).get("active_incident_kind", "")))' in _function(MANAGER, "_refresh_seas")


def test_the_campus_takes_its_halls_and_the_weather():
    assert "steam = RoofSteam.new()" in CAMPUS and "steam.build(self)" in CAMPUS
    assert "steam.set_built(built_groups())" in CAMPUS
    update = _function(MANAGER, "update_weather_drama")
    assert "campus.steam.set_weather(_air_c, _wind)" in update and "unit.set_wind(_wind)" in update
    assert 'weather.call("get_air_temperature_c")' in update and 'weather.call("get_wind")' in update
    assert "update_weather_drama()" in _function(MANAGER, "_process")
    # A campus or a unit built later starts with the weather the presenter last had.
    create = _function(MANAGER, "_create")
    assert "campus.steam.set_weather(_air_c, _wind)" in create and "unit.set_wind(_wind)" in create
    weather = _read(GODOT / "scripts" / "environment" / "WeatherController.gd")
    assert "func get_air_temperature_c() -> float:" in weather and "func get_wind() -> Vector2:" in weather
    # A vent steams once its hall stands whole, as CampusLights' lamps do.
    assert "if built.has(str(source[\"group\"])):" in STEAM
    assert "for vent: Vector3 in FacilityDrama.vents_of(str(piece[0])):" in STEAM


@pytest.mark.parametrize("source", [SPRAY, STEAM], ids=["spray", "steam"])
def test_the_particles_stay_out_of_the_mirror_and_far_views(source):
    assert "layers = PlanarReflection.NO_MIRROR_LAYER" in source
    assert "cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF" in source
    assert "visibility_range_end = VISIBLE_M" in source
    assert "local_coords = false" in source
    assert 0.0 < _const(source, "VISIBLE_M") <= 3000.0


# ── The marker colour lint ───────────────────────────────────────────────────

def _lint():
    spec = importlib.util.spec_from_file_location("lint_markers", REPO / "tools" / "lint_markers.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_the_markers_over_units_pass_the_colour_lint():
    """Spec §5.5 rule 6 and F2-24 acceptance 5: no yellow fill, and 20 delta E off the hull."""
    lint = _lint()
    assert lint.lint() == []
    assert len(lint.markers()) >= 9


def test_the_colour_lint_fails_a_yellow_marker():
    lint = _lint()
    hull = lint.hull_yellow()
    assert lint.delta_e(hull, hull) == 0.0 and lint.is_yellow(hull)
    assert lint.is_yellow(lint.srgb("#FFD21F")) and lint.delta_e(lint.srgb("#FFD21F"), hull) < lint.MIN_DELTA_E
    # What the chip and the card really use is neither.
    assert not lint.is_yellow(lint.srgb("#F29A3C")) and lint.delta_e(lint.srgb("#F29A3C"), hull) >= lint.MIN_DELTA_E
    assert not lint.is_yellow(lint.srgb("#0B2A3A"))
    lint.markers = lambda: [("a yellow chip", lint.srgb("#E8B92A"))] * 9  # the feed barge's FEED_YELLOW, were it over a unit
    problems = lint.lint()
    assert any("delta E" in p for p in problems) and any("yellow fill" in p for p in problems)


# ── The behaviour, on the real nodes (needs Godot, imported) ─────────────────

def _godot() -> str | None:
    for candidate in (os.environ.get("GODOT_BIN"), shutil.which("godot-mono"), shutil.which("godot"), shutil.which("Godot"),
                      "/Applications/Godot_mono.app/Contents/MacOS/Godot", "/Applications/Godot.app/Contents/MacOS/Godot"):
        if candidate and os.access(candidate, os.X_OK):
            return candidate
    return None


@pytest.mark.skipif(_godot() is None, reason="Godot is not installed")
@pytest.mark.skipif(not (GODOT / ".godot" / "global_script_class_cache.cfg").exists(), reason="the Godot project is not imported")
def test_the_facility_drama_checks_pass_on_the_real_nodes():
    token = Path(tempfile.mkdtemp(prefix="lakskongen-mcp-token.")) / "mcp_token"
    run = subprocess.run(
        [_godot(), "--headless", "--audio-driver", "Dummy", "--path", str(GODOT), "-s", "res://tests/FacilityDramaChecks.gd"],
        capture_output=True, text=True, timeout=300, env={**os.environ, "GODOT_MCP_TOKEN_PATH": str(token)})
    out = run.stdout + run.stderr
    result = re.search(r"FACILITY_DRAMA_RESULT checks=(\d+) failures=(\d+)", out)
    assert result, out[-1500:]
    failed = [line.strip() for line in out.splitlines() if line.startswith("  FAIL")]
    assert result.group(2) == "0" and not failed, failed
    assert int(result.group(1)) >= 31 and run.returncode == 0
    assert "SCRIPT ERROR" not in out and "Parse Error" not in out.replace("assets/fonts", "")
