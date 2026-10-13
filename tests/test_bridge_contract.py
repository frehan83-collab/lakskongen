"""Bridge contract test: every GameBridge member GDScript touches must exist.

Guards the exact failure class that broke 10+ panels: snake_case signal
access against PascalCase C# signals, and calls to missing members.
Pure static analysis — needs no engine, runs in CI.
"""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
BRIDGE_DIR = REPO / "godot" / "scripts" / "autoload"


def _bridge_sources():
    # GameBridge is split into partials (GameBridge.Core/Farm/Market/Depth/...
    # .cs) plus a small anchor file. GDScript can call members on any part.
    files = sorted(BRIDGE_DIR.glob("GameBridge*.cs"))
    assert files, "contract harness broken: no GameBridge*.cs files found"
    return files

ACCESS_RE = re.compile(r"(?:game_bridge|_game_bridge)\.([A-Za-z_][A-Za-z0-9_]*)")
SIGNAL_RE = re.compile(r"\[Signal\]\s*public delegate void (\w+)EventHandler")
METHOD_RE = re.compile(r"public\s+(?:static\s+)?(?:[\w<>\[\],\s?.]+\s+)?(\w+)\s*\(")
MEMBER_RE = re.compile(r"public\s+(?:static\s+)?(?:[\w<>\[\],\s?.]+?)\s+(\w+)\s*(?:=>|\{|;)")

# C# keywords that the loose member regex may catch — never bridge members.
CS_KEYWORDS = frozenset(
    "if else for foreach while do switch case default return using lock "
    "try catch finally new typeof nameof sizeof checked unchecked fixed "
    "stackalloc throw await get set add remove class struct enum interface "
    "namespace override virtual abstract sealed partial readonly const event "
    "delegate void int string bool float double decimal long var object".split()
)


def _declared_members():
    src = "\n".join(p.read_text(encoding="utf-8") for p in _bridge_sources())
    names = set()
    names.update(m.group(1) for m in SIGNAL_RE.finditer(src))
    names.update(m.group(1) for m in METHOD_RE.finditer(src))
    for m in MEMBER_RE.finditer(src):
        if m.group(1) not in CS_KEYWORDS:
            names.add(m.group(1))
    # Godot built-ins every Node has
    names.update({"connect", "emit_signal", "is_connected", "get", "set", "has_method"})
    return names


def _gdscript_accesses():
    hits = {}  # member -> [files]
    guarded = {}  # file -> set(names covered by has_method guards)
    guard_re = re.compile(r'has_method\("(\w+)"\)')
    for path in list((REPO / "godot").rglob("*.gd")):
        try:
            src = path.read_text(encoding="utf-8")
        except (OSError, UnicodeError):
            continue
        rel = str(path.relative_to(REPO))
        guarded[rel] = set(guard_re.findall(src))
        for m in ACCESS_RE.finditer(src):
            hits.setdefault(m.group(1), []).append(rel)
    return hits, guarded


def test_bridge_contract():
    """Every game_bridge.<member> access in GDScript must exist on GameBridge.cs.

    NOTE: C# signal `FooBar` is NOT reachable as `foo_bar` from GDScript —
    names must match exactly (this exact mismatch broke 10 panels).
    """
    declared = _declared_members()
    assert "GameStateUpdated" in declared, "contract harness broken: no signals parsed"
    accesses, guarded = _gdscript_accesses()
    assert accesses, "contract harness broken: no GDScript accesses found"
    missing = {}
    for name, files in accesses.items():
        if name in declared:
            continue
        unguarded = [f for f in set(files) if name not in guarded.get(f, set())]
        if unguarded:
            missing[name] = sorted(unguarded)
    assert not missing, (
        "GDScript accesses missing GameBridge members:\n"
        + "\n".join(f"  {name} used in: {', '.join(fs[:4])}" for name, fs in sorted(missing.items()))
    )


def test_no_snake_case_signal_access():
    """C# signals are PascalCase; snake_case access fails at runtime.

    has_method() string guards are exempt (runtime-checked by design).
    """
    import re as _re

    snake = _re.compile(r"(?:game_bridge|_game_bridge)\.[a-z]+(?:_[a-z0-9]+)+")
    offenders = []
    for path in list((REPO / "godot").rglob("*.gd")):
        try:
            src = path.read_text(encoding="utf-8")
        except (OSError, UnicodeError):
            continue
        for m in snake.finditer(src):
            if m.group(0).endswith("has_method"):
                continue
            offenders.append(str(path.relative_to(REPO)))
            break
    assert not offenders, f"snake_case GameBridge access (runtime failure): {offenders}"


# ── C# autoload contract (F928) ──────────────────────────────────────────────
# GDScript reaching a C# autoload through get_node("/root/<Name>") must use the
# exact PascalCase member names the C# class declares. A has_method() guard on
# a name that does not exist is the silent failure that killed save/load (the
# menu asked for get_save_list/save_game/load_game; C# only had PascalCase).

CS_AUTOLOADS = {
    "SaveLoadManager": "SaveLoadManager.cs",
    "EventBus": "EventBus.cs",
    "AudioManager": "AudioManager.cs",
    "MultiplayerBridge": "MultiplayerBridge.cs",
}
# Helper functions that return a C# autoload node (func name -> autoload).
CS_AUTOLOAD_GETTERS = {"_save_mgr": "SaveLoadManager"}
# Node/Object methods GDScript may legitimately call on any node.
NODE_BUILTINS = frozenset(
    "connect disconnect is_connected emit_signal has_method has_signal get set call "
    "call_deferred get_node get_node_or_null get_parent get_path queue_free is_inside_tree "
    "add_child remove_child get_children set_process set_physics_process get_class".split()
)
AUTOLOAD_VAR_RE = re.compile(
    r"var\s+(\w+)\s*(?::\s*\w+\s*)?:?=\s*(?:get_node(?:_or_null)?\(\s*\"/root/(\w+)\"\s*\)|(\w+)\(\))"
)


def _cs_declared(cs_file):
    src = (BRIDGE_DIR / cs_file).read_text(encoding="utf-8")
    names = set(m.group(1) for m in SIGNAL_RE.finditer(src))
    names.update(m.group(1) for m in METHOD_RE.finditer(src))
    names.update(m.group(1) for m in MEMBER_RE.finditer(src) if m.group(1) not in CS_KEYWORDS)
    return names


def _scopes(src):
    """Class-level text plus one text block per `func` body."""
    parts = re.split(r"(?m)^(?=func |static func )", src)
    return parts[0], parts[1:]


ASSIGN_RE = re.compile(
    r"(?m)^\s*(\w+)\s*=\s*(?:get_node(?:_or_null)?\(\s*\"/root/(\w+)\"\s*\)|(\w+)\(\))"
)


def _target(node, getter):
    return node if node in CS_AUTOLOADS else CS_AUTOLOAD_GETTERS.get(getter or "")


def _autoload_bindings(text):
    found = {}
    for m in AUTOLOAD_VAR_RE.finditer(text):
        target = _target(m.group(2), m.group(3))
        if target:
            found[m.group(1)] = target
    return found


def _class_var_assignments(class_text, src):
    """Class-level vars bound later by a plain assignment (e.g. in _ready())."""
    declared = set(re.findall(r"(?m)^(?:@onready\s+)?var\s+(\w+)", class_text))
    found = {}
    for m in ASSIGN_RE.finditer(src):
        target = _target(m.group(2), m.group(3))
        if target and m.group(1) in declared:
            found[m.group(1)] = target
    return found


def test_csharp_autoload_contract():
    declared = {name: _cs_declared(f) for name, f in CS_AUTOLOADS.items()}
    assert "SaveGame" in declared["SaveLoadManager"], "contract harness broken: SaveLoadManager not parsed"
    checked, problems = 0, []
    for path in sorted((REPO / "godot").rglob("*.gd")):
        if "addons" in path.parts:
            continue
        try:
            src = path.read_text(encoding="utf-8")
        except (OSError, UnicodeError):
            continue
        class_text, funcs = _scopes(src)
        class_vars = _autoload_bindings(class_text)
        class_vars.update(_class_var_assignments(class_text, src))
        for body in [class_text] + funcs:
            bindings = dict(class_vars)
            bindings.update(_autoload_bindings(body))
            for var, target in bindings.items():
                names = declared[target]
                for m in re.finditer(rf"\b{re.escape(var)}\.has_method\(\"(\w+)\"\)", body):
                    checked += 1
                    if m.group(1) not in names:
                        problems.append(f"{path.relative_to(REPO)}: has_method(\"{m.group(1)}\") — {target} has no such member")
                for m in re.finditer(rf"\b{re.escape(var)}\.(\w+)\s*\(", body):
                    name = m.group(1)
                    if name in NODE_BUILTINS:
                        continue
                    checked += 1
                    if name not in names:
                        problems.append(f"{path.relative_to(REPO)}: {var}.{name}() — {target} has no such member")
    assert checked > 0, "contract harness broken: no C# autoload calls found"
    assert not problems, "GDScript calls missing C# autoload members:\n  " + "\n  ".join(sorted(set(problems)))


# ── SimBridge facade parity (F945) ───────────────────────────────────────────
# UI code calls /root/SimBridge behind has_method(); a public SimProxies or
# SimActions func without a facade forwarder therefore fails silently.

GD_FUNC_RE = re.compile(r"(?m)^func ([a-z]\w*)\s*\(")


def test_simbridge_facade_parity():
    bridge_dir = REPO / "godot" / "scripts"
    facade = set(GD_FUNC_RE.findall((bridge_dir / "SimBridge.gd").read_text(encoding="utf-8")))
    assert "save_game" in facade, "contract harness broken: SimBridge.gd not parsed"
    missing = []
    for part in ("SimProxies.gd", "SimActions.gd"):
        funcs = GD_FUNC_RE.findall((bridge_dir / "bridge" / part).read_text(encoding="utf-8"))
        assert funcs, f"contract harness broken: no funcs parsed from {part}"
        missing += [f"{part}::{name}" for name in funcs if name not in facade]
    assert not missing, "SimBridge facade is missing forwarders:\n  " + "\n  ".join(missing)


if __name__ == "__main__":
    test_bridge_contract()
    test_no_snake_case_signal_access()
    test_csharp_autoload_contract()
    test_simbridge_facade_parity()
    print("bridge contract OK")
