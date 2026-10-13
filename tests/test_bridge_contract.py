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
    accesses, _guarded = _gdscript_accesses()
    assert accesses, "contract harness broken: no GDScript accesses found"
    # No has_method() exemption (F945): a guarded call to a member that does
    # not exist is dead code that hides a broken feature.
    missing = {}
    for name, files in accesses.items():
        if name not in declared:
            missing[name] = sorted(set(files))
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


# ── Bridge consumers: has_method guards and call arity (F945) ────────────────
# UI code reaches the bridges through local names (_sim, sim_bridge, gb, ...)
# and hid missing members behind has_method(), which turned panels inert (the
# loan card asked for get_loan_info/repay_loan, which existed nowhere) or fed
# them invented data. A wrong argument count fails only at runtime (the loan
# card called take_loan(amount) against take_loan(amount, term, rate)). Every
# guarded name and every call must exist on its target and fit its signature.

BRIDGE_FILES = {"SimBridge": "SimBridge.gd", "GameBridge": "GameBridge*.cs"}
# Orphaned panels: no scene, tab or script loads them. They call bridge methods
# that do not exist and fall back to invented data. Exempt only while they stay
# unreachable — wire them (bridge + tab) or delete them.
ORPHANED_PANELS = {"godot/scripts/ui/EmpirePanel.gd", "godot/scripts/ui/StakeholderPanel.gd"}
GD_SIGNATURE_RE = re.compile(r"(?m)^(?:static\s+)?func\s+([A-Za-z_]\w*)\s*\(([^)]*)\)")
CS_SIGNATURE_RE = re.compile(r"public\s+(?:static\s+|override\s+|virtual\s+)*[\w<>\[\],.?\s]+?\s+(\w+)\s*\(([^)]*)\)")
BRIDGE_BIND_RE = re.compile(
    r"(?m)^\s*(?:@onready\s+)?(?:var\s+)?(\w+)\s*(?::\s*\w+\s*)?:?=\s*"
    r"get_node(?:_or_null)?\(\s*\"/root/(SimBridge|GameBridge)\"\s*\)"
)
BRIDGE_GETTER_RE = re.compile(
    r"(?m)^func\s+(\w+)\s*\(\)[^:\n]*:\s*\n\s*return\s+get_node(?:_or_null)?\(\s*\"/root/(SimBridge|GameBridge)\"\s*\)"
)


def _split_top_level(text, openers="([{<", closers=")]}>"):
    """Split on commas outside brackets and strings."""
    parts, cur, depth, quote = [], "", 0, None
    for ch in text:
        if quote:
            cur += ch
            if ch == quote:
                quote = None
            continue
        if ch in "\"'":
            quote = ch
        elif ch in openers:
            depth += 1
        elif ch in closers:
            depth -= 1
        if ch == "," and depth == 0:
            parts.append(cur)
            cur = ""
        else:
            cur += ch
    parts.append(cur)
    return [p for p in parts if p.strip()]


def _call_arg_count(src, open_paren):
    """Number of arguments of the call whose '(' is at open_paren (None if unbalanced)."""
    depth, quote = 0, None
    for i in range(open_paren, len(src)):
        ch = src[i]
        if quote:
            if ch == quote:
                quote = None
            continue
        if ch in "\"'":
            quote = ch
        elif ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return len(_split_top_level(src[open_paren + 1:i], "([{", ")]}"))
    return None


def _signatures(target):
    """name -> list of (required, maximum) argument counts (overloads, defaults)."""
    sigs = {}
    if target == "SimBridge":
        src = (REPO / "godot" / "scripts" / BRIDGE_FILES[target]).read_text(encoding="utf-8")
        for m in GD_SIGNATURE_RE.finditer(src):
            params = _split_top_level(m.group(2), "([{", ")]}")
            sigs.setdefault(m.group(1), []).append((sum("=" not in p for p in params), len(params)))
        return sigs
    for path in _bridge_sources():
        src = path.read_text(encoding="utf-8")
        for m in CS_SIGNATURE_RE.finditer(src):
            params = _split_top_level(m.group(2))
            unbounded = any(p.split()[0] == "params" for p in params if p.split())
            required = sum("=" not in p and not p.split()[0] == "params" for p in params)
            sigs.setdefault(m.group(1), []).append((required, 10**6 if unbounded else len(params)))
    return sigs


def _bridge_bindings(text):
    found = {m.group(1): m.group(2) for m in BRIDGE_BIND_RE.finditer(text)}
    found.update({m.group(1) + "()": m.group(2) for m in BRIDGE_GETTER_RE.finditer(text)})
    return found


def _bridge_consumer_problems(src, rel, signatures):
    """(checked, problems) for one GDScript source."""
    class_text, funcs = _scopes(src)
    class_bindings = _bridge_bindings(class_text)
    if rel.startswith("godot/scripts/bridge/"):
        class_bindings["game_bridge"] = "GameBridge"  # injected by the SimBridge facade
    declared = set(re.findall(r"(?m)^(?:@onready\s+)?var\s+(\w+)", class_text))
    # Class vars bound later by a plain assignment (e.g. _sim = get_node(...) in _ready).
    for m in BRIDGE_BIND_RE.finditer(src):
        if m.group(1) in declared:
            class_bindings[m.group(1)] = m.group(2)
    class_bindings.update({k: v for k, v in _bridge_bindings(src).items() if k.endswith("()")})
    checked, problems = 0, []
    for body in [class_text] + funcs:
        bindings = dict(class_bindings)
        bindings.update({k: v for k, v in _bridge_bindings(body).items() if not k.endswith("()")})
        for var, target in bindings.items():
            sigs = signatures[target]
            ref = re.escape(var) if var.endswith("()") else rf"\b{re.escape(var)}"
            for m in re.finditer(ref + r"\.has_method\(\"(\w+)\"\)", body):
                checked += 1
                if m.group(1) not in sigs:
                    problems.append(f"{rel}: {var}.has_method(\"{m.group(1)}\") — {target} has no such method")
            for m in re.finditer(ref + r"\.(\w+)\s*\(", body):
                name = m.group(1)
                if name in NODE_BUILTINS or name == "get_tree":
                    continue
                checked += 1
                if name not in sigs:
                    problems.append(f"{rel}: {var}.{name}() — {target} has no such method")
                    continue
                n = _call_arg_count(body, m.end() - 1)
                if n is not None and not any(lo <= n <= hi for lo, hi in sigs[name]):
                    want = " or ".join(f"{lo}..{hi}" if lo != hi else str(lo) for lo, hi in sigs[name])
                    problems.append(f"{rel}: {var}.{name}() with {n} argument(s) — {target} takes {want}")
    return checked, problems


def _is_referenced_elsewhere(rel):
    stem = Path(rel).stem
    own = {rel, str(Path(rel).with_suffix(".gd.uid"))}
    for path in (REPO / "godot").rglob("*"):
        if path.suffix not in (".gd", ".tscn") or "addons" in path.parts:
            continue
        r = str(path.relative_to(REPO))
        if r in own or r.endswith(f"/{stem}.tscn"):
            continue
        if stem in path.read_text(encoding="utf-8", errors="ignore"):
            return True
    return False


def test_bridge_consumers_call_real_methods_with_valid_arity():
    signatures = {t: _signatures(t) for t in BRIDGE_FILES}
    assert "take_loan" in signatures["SimBridge"], "contract harness broken: SimBridge.gd not parsed"
    assert "TakeLoan" in signatures["GameBridge"], "contract harness broken: GameBridge*.cs not parsed"
    checked, problems = 0, []
    for path in sorted((REPO / "godot").rglob("*.gd")):
        if "addons" in path.parts or path.name == "SimBridge.gd":
            continue
        rel = str(path.relative_to(REPO))
        src = path.read_text(encoding="utf-8", errors="ignore")
        n, found = _bridge_consumer_problems(src, rel, signatures)
        checked += n
        if rel in ORPHANED_PANELS:
            assert not _is_referenced_elsewhere(rel), (
                f"{rel} is no longer orphaned: give it a real bridge contract and drop it from ORPHANED_PANELS")
            continue
        problems += found
    assert checked > 300, f"contract harness broken: only {checked} bridge calls checked"
    assert not problems, "Bridge calls that fail at runtime:\n  " + "\n  ".join(sorted(set(problems)))


def test_bridge_consumer_check_catches_the_known_failures():
    """Non-vacuity: the F943 loan card and a guarded ghost method are flagged."""
    signatures = {t: _signatures(t) for t in BRIDGE_FILES}
    sample = (
        "var _sim: Node = null\n"
        "func _ready() -> void:\n"
        "\t_sim = get_node_or_null(\"/root/SimBridge\")\n"
        "func _on_take_loan() -> void:\n"
        "\t_sim.take_loan(5_000_000.0)\n"
        "\tif _sim.has_method(\"get_ghost_report\"):\n"
        "\t\tpass\n"
        "func _paint() -> void:\n"
        "\tvar gb := get_node_or_null(\"/root/GameBridge\")\n"
        "\tgb.TakeLoan(1.0, 2, 0.05, \"extra\")\n"
    )
    _, problems = _bridge_consumer_problems(sample, "sample.gd", signatures)
    text = "\n".join(problems)
    assert "take_loan() with 1 argument(s)" in text, text
    assert "has_method(\"get_ghost_report\") — SimBridge has no such method" in text, text
    assert "TakeLoan() with 4 argument(s)" in text, text


if __name__ == "__main__":
    test_bridge_contract()
    test_no_snake_case_signal_access()
    test_csharp_autoload_contract()
    test_simbridge_facade_parity()
    test_bridge_consumers_call_real_methods_with_valid_arity()
    test_bridge_consumer_check_catches_the_known_failures()
    print("bridge contract OK")
