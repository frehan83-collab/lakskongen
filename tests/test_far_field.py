"""Far field round the play area (roadmap G2-01): static guards.

The world ended in flat ocean at the 4 km edge and read as an island. The
far field (TerrainGenerator.FarFieldHeightAt, FarFieldMesh, uploaded by
FjordTerrain.gd) surrounds it out to 16 km. These checks pin how the rest of
the renderer meets it:

- the camera may leave the world only as far as its orbit takes it, the
  focus keeps its old limit, and the bounds stay the only camera change
  besides the finer sight-line scan the longer COAST lines need;
- the ring uploads under the terrain's frame budget, with shadows only on
  the band next to the world, and height queries outside the world read it;
- the sea runs under all of it, and the terrain shader fades its wall
  structure with distance (the far field's cliffs would read as a
  venetian-blind pattern).

Needs no engine.
"""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
CAMERA = GODOT / "scripts" / "environment" / "FjordCamera.gd"
TERRAIN = GODOT / "scripts" / "environment" / "FjordTerrain.gd"
WATER_GRID = GODOT / "scripts" / "environment" / "WaterGrid.gd"
SHADER = GODOT / "shaders" / "terrain_shader.gdshader"
FAR_FIELD = REPO / "src" / "LakskongenSim" / "Environment" / "TerrainGenerator.FarField.cs"


def _gd(path: Path) -> str:
    """GDScript source without ## and # comments."""
    return "\n".join(line.split("#", 1)[0] for line in path.read_text(encoding="utf-8").splitlines())


def _func(source: str, name: str) -> str:
    """Body of a GDScript function (up to the next top-level declaration)."""
    match = re.search(rf"^func {name}\(.*?(?=^\S)", source + "\nEND", re.S | re.M)
    assert match, f"{name} not found"
    return match.group(0)


def _const(source: str, name: str) -> float:
    match = re.search(rf"const {name}\s*:?=\s*([0-9.]+)", source)
    assert match, f"{name} not found"
    return float(match.group(1))


def test_the_focus_keeps_its_limit_and_only_the_camera_leaves_the_world():
    cam = _gd(CAMERA)
    assert _const(cam, "EDGE_MARGIN") == 300.0
    # The focus is clamped to the world as before.
    assert "_clamp_to_world(" in _func(cam, "_set_focus_target")
    assert "_clamp_to_world(" in _func(cam, "fly_to_position")
    # The rig's positions use the camera bounds, never the focus bounds.
    rig = _func(cam, "solve_rig")
    assert rig.count("_clamp_camera(") == 3 and "_clamp_to_world(" not in rig
    # The camera's own bounds: the focus limit plus the longest orbit, and
    # FAR_FIELD_MARGIN inside the far field; the focus limit without one.
    half = _func(cam, "_camera_half")
    assert "MAX_DISTANCE" in half and "FAR_FIELD_MARGIN" in half and "far_field_half_extent()" in half
    assert _const(cam, "FAR_FIELD_MARGIN") >= 1000.0


def test_the_sight_line_scan_is_fine_enough_for_the_longer_coast_lines():
    # 2400 m COAST sight lines, now out over the far field's plateau: a scan
    # step under ~50 m (32 samples hid the focus in 3 of 200 seeded orbits).
    cam = _gd(CAMERA)
    assert 2400.0 / (_const(cam, "SIGHT_SAMPLES") + 1) <= 50.0


def test_the_ring_uploads_under_the_frame_budget_with_near_shadows_only():
    terrain = _gd(TERRAIN)
    upload = _func(terrain, "_upload_far_field")
    assert "CHUNK_FRAME_BUDGET_MS" in upload
    assert "SHADOW_CASTING_SETTING_OFF" in upload and '"band"' in upload
    assert "far_field_ready.emit()" in upload
    # Outside the world, heights come from the far field.
    height = _func(terrain, "get_height_at")
    assert "far_field_height_at(" in height


def test_the_sea_runs_under_all_of_the_far_field():
    extent = float(re.search(r"FarFieldExtentM\s*=\s*([0-9.]+)f", FAR_FIELD.read_text()).group(1))
    grid = _gd(WATER_GRID)
    ocean = float(re.search(r"var ocean_extent: float = ([0-9.]+)", grid).group(1))
    assert ocean >= extent


def test_the_wall_structure_fades_with_distance():
    code = "\n".join(line.split("//", 1)[0] for line in SHADER.read_text(encoding="utf-8").splitlines())
    start = float(re.search(r"WALL_FADE_START_M = ([0-9.]+)", code).group(1))
    end = float(re.search(r"WALL_FADE_END_M = ([0-9.]+)", code).group(1))
    assert 1500.0 <= start < end <= 6000.0
    assert "wall_near = 1.0 - smoothstep(WALL_FADE_START_M, WALL_FADE_END_M, dist)" in code
    # The wall noise, the band fetches, the ledges, their tone, the seepage
    # and the buttress bump all go with it.
    assert re.search(r"if \(slope_tan > 0\.8 && wall_detail > 0\.5 && wall_near > 0\.0\)", code)
    assert "if (cliff_near > 0.0) {" in code
    assert "* is_ledge * cliff_near;" in code
    assert "cliff_near * 0.85" in code
    assert "steep * wall_near" in code
    assert "if (cliff_near > 0.01 && wall_detail > 0.5)" in code
