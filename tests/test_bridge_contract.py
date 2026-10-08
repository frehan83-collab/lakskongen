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


if __name__ == "__main__":
    test_bridge_contract()
    test_no_snake_case_signal_access()
    print("bridge contract OK")
