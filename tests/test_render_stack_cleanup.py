"""P0-05 guard: the dead rendering stacks and the duplicate sim->3D sync stay gone.

The fjord had three parallel renderers (FjordEnvironment, the NordlandFjord
scene, a retro-terrain addon stub) and two sim->3D sync layers (Sim3DSync and
FjordVistaBridge) that wrote the same water uniforms. FjordEnvironment is the
renderer and FjordVistaBridge is the only presenter. Pure static analysis —
needs no engine, runs in CI.
"""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"

DELETED = [
    "godot/shaders/fjord_water.gdshader",
    "godot/shaders/water_wave_shader.gdshader",
    "godot/shaders/fjord_sky.gdshader",
    "godot/scenes/NordlandFjord.tscn",
    "godot/scripts/environment/NordlandFjordController.gd",
    "godot/scripts/environment/NordlandIntegration.gd",
    "godot/scripts/environment/FishPenMarker.gd",
    "godot/scripts/environment/WaterSurface.gd",
    "godot/scripts/environment/FjordTerrainGenerator.gd",
    "godot/scenes/environment/FjordTerrain3D.gd",
    "godot/addons/gd-retroterrain",
    "godot/scripts/environment/Sim3DSync.gd",
    "godot/scripts/visual/VisualAdapter3D.gd",
    "godot/scripts/visual/IVisualAdapter.gd",
    "godot/scenes/environment/FJORD_RENDERER.md",
]

# Names that must not be referenced by any shipped file or tool any more.
DEAD_NAMES = [
    "fjord_water.gdshader",
    "water_wave_shader",
    "fjord_sky.gdshader",
    "NordlandFjord",
    "NordlandIntegration",
    "FishPenMarker",
    "WaterSurface.gd",
    "FjordTerrainGenerator",
    "FjordTerrain3D",
    "gd-retroterrain",
    "Sim3DSync",
    "VisualAdapter3D",
    "IVisualAdapter",
]

SHIPPED_SUFFIXES = {".gd", ".tscn", ".tres", ".gdshader", ".gdshaderinc", ".cs", ".cfg", ".godot", ".json"}


def _shipped_files():
    for path in GODOT.rglob("*"):
        rel = path.relative_to(GODOT).as_posix()
        if rel.startswith((".godot/", "addons/", "screenshots/")):
            continue
        if path.is_file() and path.suffix in SHIPPED_SUFFIXES:
            yield path
    for root in (REPO / "src", REPO / "tools"):
        for path in root.rglob("*"):
            if path.is_file() and path.suffix in {".cs", ".sh", ".py", ".gd"} and "/bin/" not in path.as_posix() and "/obj/" not in path.as_posix():
                yield path


def test_dead_render_stacks_are_deleted():
    present = [p for p in DELETED if (REPO / p).exists()]
    assert present == [], f"dead rendering files came back: {present}"


def test_nothing_references_the_dead_render_stacks():
    hits = []
    for path in _shipped_files():
        text = path.read_text(encoding="utf-8", errors="replace")
        for name in DEAD_NAMES:
            if name in text:
                hits.append(f"{path.relative_to(REPO)}: {name}")
    assert hits == [], "references to deleted render stacks:\n  " + "\n  ".join(hits)


def test_fjord_environment_scene_has_one_presenter_and_no_dead_nodes():
    scene = (GODOT / "scenes" / "environment" / "FjordEnvironment.tscn").read_text(encoding="utf-8")
    nodes = re.findall(r'\[node name="([^"]+)"', scene)
    assert "Sim3DSync" not in nodes and "VisualAdapter3D" not in nodes, nodes
    # Every node_paths entry on the root must be assigned (stale entries were
    # left behind for nodes that no longer existed).
    root = re.search(r'\[node name="FjordEnvironment"[^\]]*node_paths=PackedStringArray\(([^)]*)\)\]', scene)
    assert root, "FjordEnvironment root node lost its node_paths"
    for prop in re.findall(r'"(\w+)"', root.group(1)):
        assert re.search(rf"(?m)^{prop} = NodePath\(", scene), f"node_paths lists unassigned property {prop}"


def test_only_the_vista_bridge_presents_sim_state_in_3d():
    """One sim->3D presenter: FjordVistaBridge syncs pens and environment."""
    writers = sorted(
        p.relative_to(REPO).as_posix()
        for p in (GODOT / "scripts").rglob("*.gd")
        if re.search(r"\bsync_pens_from_sim\(\s*\w", p.read_text(encoding="utf-8"))
        and "func sync_pens_from_sim" not in p.read_text(encoding="utf-8")
    )
    assert writers == ["godot/scripts/environment/FjordVistaBridge.gd"], writers
