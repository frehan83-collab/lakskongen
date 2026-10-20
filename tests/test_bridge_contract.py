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
# Orphaned UI scripts: no scene, tab or script loads them. They call bridge
# methods and signals that do not exist (Empire and Stakeholder fall back to
# invented data). Exempt only while they stay unreachable — wire them (bridge
# + tab) or delete them.
ORPHANED_PANELS = {
    "godot/scripts/ui/EmpirePanel.gd",
    "godot/scripts/ui/StakeholderPanel.gd",
    "godot/scripts/ui/CompetitorDashboard.gd",
    "godot/scripts/ui/IPODialog.gd",
    "godot/scripts/ui/MarketChannelPanel.gd",
    "godot/scripts/ui/ProcessingMixPanel.gd",
}
GD_SIGNATURE_RE = re.compile(r"(?m)^(?:static\s+)?func\s+([A-Za-z_]\w*)\s*\(([^)]*)\)")
CS_SIGNATURE_RE = re.compile(r"public\s+(?:static\s+|override\s+|virtual\s+)*[\w<>\[\],.?\s]+?\s+(\w+)\s*\(([^)]*)\)")
BRIDGE_BIND_RE = re.compile(
    r"(?m)^\s*(?:@onready\s+)?(?:var\s+)?(\w+)\s*(?::\s*\w+\s*)?:?=\s*"
    r"get_node(?:_or_null)?\(\s*\"/root/(SimBridge|GameBridge)\"\s*\)"
)
BRIDGE_GETTER_RE = re.compile(
    r"(?m)^func\s+(\w+)\s*\(\)[^:\n]*:\s*\n\s*return\s+get_node(?:_or_null)?\(\s*\"/root/(SimBridge|GameBridge)\"\s*\)"
)
# Locals filled from a getter: `var sim := _sim()`.
GETTER_LOCAL_RE = re.compile(r"(?m)^\s*var\s+(\w+)\s*(?::\s*\w+\s*)?:?=\s*(\w+)\(\)\s*$")
# Bindings every script has (review R4): the autoload globals, direct
# get_node("/root/...") chains, and the names this project always gives
# SimBridge. `_sim_bridge` / `sim_bridge` arrive through NodePath exports or
# bind_simulation() parameters, which the patterns above cannot follow; that
# hid Sim3DSync and the 3D visual managers from this test entirely.
IMPLICIT_BINDINGS = {
    "SimBridge": "SimBridge",
    "GameBridge": "GameBridge",
    'get_node("/root/SimBridge")': "SimBridge",
    'get_node_or_null("/root/SimBridge")': "SimBridge",
    'get_node("/root/GameBridge")': "GameBridge",
    'get_node_or_null("/root/GameBridge")': "GameBridge",
    "_sim_bridge": "SimBridge",
    "sim_bridge": "SimBridge",
}
GD_VAR_RE = re.compile(r"(?m)^(?:@\w+(?:\([^)]*\))?\s+)*var\s+(\w+)")
GD_CONST_RE = re.compile(r"(?m)^const\s+(\w+)")
GD_SIGNAL_RE = re.compile(r"(?m)^signal\s+(\w+)")
# Node/Object properties and signals any bridge node has.
NODE_PROPERTIES = frozenset(
    "name owner script process_mode process_priority process_physics_priority "
    "process_thread_group unique_name_in_owner scene_file_path editor_description "
    "multiplayer physics_interpolation_mode auto_translate_mode".split()
)
NODE_SIGNALS = frozenset(
    "ready renamed tree_entered tree_exiting tree_exited child_entered_tree "
    "child_exiting_tree child_order_changed script_changed property_list_changed".split()
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
            # Godot finds a C# method by name and argument count and never fills
            # a C# default value (F2-03: GetSeaSiteDetails(bool x = false) called
            # with no argument was "Nonexistent function"), so GDScript passes
            # every parameter a default value stands for.
            required = sum(not p.split()[0] == "params" for p in params)
            sigs.setdefault(m.group(1), []).append((required, 10**6 if unbounded else len(params)))
    return sigs


def _properties(target):
    """Names a script can read with get("name") or test with "name" in node."""
    if target == "SimBridge":
        src = (REPO / "godot" / "scripts" / BRIDGE_FILES[target]).read_text(encoding="utf-8")
        return set(GD_VAR_RE.findall(src)) | set(GD_CONST_RE.findall(src)) | NODE_PROPERTIES
    names = set(NODE_PROPERTIES)
    for path in _bridge_sources():
        src = path.read_text(encoding="utf-8")
        names.update(m.group(1) for m in MEMBER_RE.finditer(src) if m.group(1) not in CS_KEYWORDS)
    return names


def _signals(target):
    """Signal names a script can reach as an attribute (x.signal.connect(...))."""
    if target == "SimBridge":
        src = (REPO / "godot" / "scripts" / BRIDGE_FILES[target]).read_text(encoding="utf-8")
        return set(GD_SIGNAL_RE.findall(src)) | NODE_SIGNALS
    names = set(NODE_SIGNALS)
    for path in _bridge_sources():
        names.update(SIGNAL_RE.findall(path.read_text(encoding="utf-8")))
    return names


def _strip_gd_comments(src):
    """Drop # comments outside strings, so prose never reads as a call."""
    lines = []
    for line in src.split("\n"):
        quote = None
        for i, ch in enumerate(line):
            if quote:
                if ch == quote:
                    quote = None
            elif ch in "\"'":
                quote = ch
            elif ch == "#":
                line = line[:i]
                break
        lines.append(line)
    return "\n".join(lines)


def _string_mask(text):
    """1 for every character inside a string literal (after its opening
    quote), so text in a message such as push_warning("SimBridge.x()") is not
    read as a call. Opening quotes stay 0: "prop" in x starts on one."""
    mask, quote, escaped = bytearray(len(text)), None, False
    for i, ch in enumerate(text):
        if quote:
            mask[i] = 1
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == quote:
                quote = None
        elif ch in "\"'":
            quote = ch
    return mask


def _ref(var):
    """Regex for a binding: an identifier on a word boundary, or a literal expression."""
    return rf"\b{re.escape(var)}" if re.fullmatch(r"\w+", var) else re.escape(var)


def _bridge_bindings(text):
    found = {m.group(1): m.group(2) for m in BRIDGE_BIND_RE.finditer(text)}
    found.update({m.group(1) + "()": m.group(2) for m in BRIDGE_GETTER_RE.finditer(text)})
    return found


def _bridge_consumer_problems(src, rel, signatures, properties=None, signals=None):
    """(checked, problems) for one GDScript source."""
    if properties is None:
        properties = {t: _properties(t) for t in BRIDGE_FILES}
    if signals is None:
        signals = {t: _signals(t) for t in BRIDGE_FILES}
    src = _strip_gd_comments(src)
    class_text, funcs = _scopes(src)
    class_bindings = dict(IMPLICIT_BINDINGS)
    class_bindings.update(_bridge_bindings(class_text))
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
        for m in GETTER_LOCAL_RE.finditer(body):
            if m.group(2) + "()" in bindings:
                bindings[m.group(1)] = bindings[m.group(2) + "()"]
        in_string = _string_mask(body)
        for var, target in bindings.items():
            sigs = signatures[target]
            ref = _ref(var)
            def matches(pattern):
                return (m for m in re.finditer(pattern, body) if not in_string[m.start()])
            for m in matches(ref + r"\.(get|set)\(\s*\"(\w+)\""):
                checked += 1
                if m.group(2) not in properties[target]:
                    problems.append(f"{rel}: {var}.{m.group(1)}(\"{m.group(2)}\") — {target} has no such property")
            # Not "x" in sim.method()[0]: that tests a key of the returned value.
            for m in matches(r"\"(\w+)\"\s+in\s+" + ref + r"(?![\w.(\[])"):
                checked += 1
                if m.group(1) not in properties[target]:
                    problems.append(f"{rel}: \"{m.group(1)}\" in {var} — {target} has no such property")
            for m in matches(ref + r"\.has_method\(\"(\w+)\"\)"):
                checked += 1
                if m.group(1) not in sigs:
                    problems.append(f"{rel}: {var}.has_method(\"{m.group(1)}\") — {target} has no such method")
            for m in matches(ref + r"\.(?:call|call_deferred|callv)\(\s*\"(\w+)\""):
                checked += 1
                if m.group(1) not in sigs:
                    problems.append(f"{rel}: {var}.call(\"{m.group(1)}\") — {target} has no such method")
            # Attribute reads (sim.current_day, sim.state_updated.connect):
            # a property, a signal, or a method used as a Callable.
            for m in matches(ref + r"\.(\w+)\b(?!\s*\()"):
                name = m.group(1)
                checked += 1
                if not (name in properties[target] or name in signals[target] or name in sigs):
                    problems.append(f"{rel}: {var}.{name} — {target} has no such property or signal")
            for m in matches(ref + r"\.(\w+)\s*\("):
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
    # 1,102 at the last count; 1,013 if the implicit bindings stop resolving.
    assert checked > 1050, f"contract harness broken: only {checked} bridge calls checked"
    assert not problems, "Bridge calls that fail at runtime:\n  " + "\n  ".join(sorted(set(problems)))


DECIMAL_SIGNATURE_RE = re.compile(
    r"public\s+(?:static\s+)?(?:[\w<>\[\],.?\s]+?\s+)?(\w+)\s*\(([^)]*)\)")


def test_gamebridge_signatures_are_marshalable():
    """Godot cannot marshal C# decimal: a public GameBridge method with decimal
    in its signature is never registered, so has_method() is false and the
    caller silently does nothing (F947: ValorizeSludgeNow, the sludge button)."""
    offenders = []
    for path in _bridge_sources():
        src = path.read_text(encoding="utf-8")
        for m in DECIMAL_SIGNATURE_RE.finditer(src):
            head = src[m.start():m.end()]
            if re.search(r"\bdecimal\b", head):
                line = src[:m.start()].count("\n") + 1
                offenders.append(f"{path.relative_to(REPO)}:{line} {m.group(1)}")
    assert not offenders, "GameBridge methods Godot cannot expose (decimal):\n  " + "\n  ".join(offenders)


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


def test_bridge_consumer_check_follows_every_way_scripts_reach_the_bridges():
    """Non-vacuity for review R4: these reaches were all invisible to the check,
    which is how Sim3DSync and the wellboat and competitor-farm visuals kept
    calling methods and reading properties that do not exist."""
    signatures = {t: _signatures(t) for t in BRIDGE_FILES}
    sample = (
        "@export var sim_bridge_path: NodePath = \"/root/SimBridge\"\n"
        "var _sim_bridge = null\n"
        "func _bridge() -> Node:\n"
        "\treturn get_node_or_null(\"/root/SimBridge\")\n"
        "func bind_simulation(sim_bridge) -> void:\n"
        "\tsim_bridge.get_all_wellboat_jobs()\n"
        "func _tick() -> void:\n"
        "\tvar sim := _bridge()\n"
        "\tsim.get_ghost_jobs()\n"
        "\tSimBridge.get_phantom()\n"
        "\tGameBridge.GetPhantom()\n"
        "\tget_node(\"/root/SimBridge\").get_spectre()\n"
        "\tvar day = _sim_bridge.get(\"day\")\n"
        "\tif \"night_mode\" in _sim_bridge:\n"
        "\t\t_sim_bridge.current_day\n"
        "\t# SimBridge.get_commented_out() is prose, not a call\n"
        "\tvar real = _sim_bridge.get(\"current_day\")\n"
    )
    _, problems = _bridge_consumer_problems(sample, "sample.gd", signatures)
    text = "\n".join(problems)
    for expected in (
        "sim_bridge.get_all_wellboat_jobs() — SimBridge has no such method",
        "sim.get_ghost_jobs() — SimBridge has no such method",
        "SimBridge.get_phantom() — SimBridge has no such method",
        "GameBridge.GetPhantom() — GameBridge has no such method",
        "get_spectre() — SimBridge has no such method",
        "_sim_bridge.get(\"day\") — SimBridge has no such property",
        "\"night_mode\" in _sim_bridge — SimBridge has no such property",
    ):
        assert expected in text, text
    assert "commented_out" not in text and "current_day" not in text, text


def test_bridge_consumer_check_reads_strings_keys_and_attributes_correctly():
    """Review of R4: a message string is not a call, a key test on a returned
    value is not a property test, Node properties are allowed, a failed set()
    says set(), and dynamic call() names and attribute reads are checked."""
    signatures = {t: _signatures(t) for t in BRIDGE_FILES}
    sample = (
        "var _sim_bridge = null\n"
        "func _tick() -> void:\n"
        "\tpush_warning(\"SimBridge.get_x() is missing\")\n"
        "\tif \"pen_id\" in _sim_bridge.get_all_pens()[0]:\n"
        "\t\tpass\n"
        "\t_sim_bridge.set(\"process_mode\", 0)\n"
        "\tif \"script\" in _sim_bridge:\n"
        "\t\tpass\n"
        "\t_sim_bridge.set(\"night\", 1)\n"
        "\tSimBridge.call_deferred(\"GetPhantom\")\n"
        "\tvar d = _sim_bridge.curent_day\n"
        "\t_sim_bridge.state_updated.connect(_tick)\n"
        "\tvar ok = _sim_bridge.current_day\n"
    )
    _, problems = _bridge_consumer_problems(sample, "sample.gd", signatures)
    text = "\n".join(problems)
    assert "get_x" not in text and "pen_id" not in text, text
    assert "process_mode" not in text and "\"script\"" not in text, text
    assert "_sim_bridge.set(\"night\") — SimBridge has no such property" in text, text
    assert "SimBridge.call(\"GetPhantom\") — SimBridge has no such method" in text, text
    assert "_sim_bridge.curent_day — SimBridge has no such property or signal" in text, text
    assert "state_updated" not in text and "current_day —" not in text, text


# ── S2-05: mortality ledger for the P&L story ────────────────────────────────
# The breakdown travels engine -> GameBridge partial -> SimProxies -> SimBridge
# facade with the same arguments, and every label the engine can emit is a
# translation key with English and Norwegian text.

def _id_switch(src: str, signature: str) -> list[str]:
    start = src.index(signature)
    body = src[start:src.index("};", start)]
    return re.findall(r'=> "([a-z_]+)"', body)


def test_mortality_breakdown_reaches_the_facade():
    partial = (BRIDGE_DIR / "GameBridge.Mortality.cs").read_text(encoding="utf-8")
    assert re.search(r"public Godot\.Collections\.Dictionary GetMortalityBreakdown\("
                     r'string siteId = "", int lastDays = 0\)', partial), "GameBridge.GetMortalityBreakdown missing"
    for key in ("success", "scope", "site_id", "from_day", "to_day", "total_deaths", "total_kg",
                "total_value_nok", "categories", "causes", "left_alive"):
        assert f'result["{key}"]' in partial, f"bridge breakdown lacks key {key}"
    for key in ("id", "category", "nvi_code", "label_key", "fish", "kg", "value_nok", "share"):
        assert re.search(r'\["' + key + r'"\] = (\(double\))?row\.', partial), f"bridge breakdown rows lack key {key}"

    scripts = REPO / "godot" / "scripts"
    proxies = (scripts / "bridge" / "SimProxies.gd").read_text(encoding="utf-8")
    assert re.search(r'func get_mortality_breakdown\(site_id: String = "", last_days: int = 0\) -> Dictionary: '
                     r"return game_bridge\.GetMortalityBreakdown\(site_id, last_days\)", proxies), \
        "SimProxies.get_mortality_breakdown must forward both arguments to GameBridge"
    facade = (scripts / "SimBridge.gd").read_text(encoding="utf-8")
    assert re.search(r'func get_mortality_breakdown\(site_id: String = "", last_days: int = 0\) -> Dictionary: '
                     r"return _proxies\.get_mortality_breakdown\(site_id, last_days\)", facade), \
        "SimBridge facade must forward get_mortality_breakdown"


def test_every_mortality_label_key_has_english_and_norwegian_text():
    import csv
    src = (REPO / "src" / "LakskongenSim" / "Biology" / "MortalityCause.cs").read_text(encoding="utf-8")
    keys = ["MORTALITY_CATEGORY_" + i.upper() for i in _id_switch(src, "public static string Id(MortalityCategory category)")]
    keys += ["MORTALITY_CAUSE_" + i.upper() for i in _id_switch(src, "public static string Id(MortalityCause cause)")]
    keys += ["FISH_REMOVAL_" + i.upper() for i in _id_switch(src, "public static string Id(FishRemoval removal)")]
    assert len(keys) == 7 + 11 + 4, f"contract harness broken: parsed {len(keys)} mortality ids"
    with (REPO / "godot" / "translations" / "translations.csv").open(encoding="utf-8", newline="") as f:
        rows = {r[0]: r for r in csv.reader(f) if len(r) == 3}
    missing = [k for k in keys if k not in rows or not rows[k][1].strip() or not rows[k][2].strip()]
    assert not missing, f"mortality labels without en/no text: {missing}"



# ── S2-01a: capacity v1 for U2-01's expansion flow ───────────────────────────
# Licences and MTB per area and site travel engine -> GameBridge partial ->
# SimProxies -> SimBridge facade with the same arguments; every reason and
# breach status the engine can emit is a translation key with en and no text.

def test_capacity_overview_reaches_the_facade():
    partial = (BRIDGE_DIR / "GameBridge.Capacity.cs").read_text(encoding="utf-8")
    assert "public Godot.Collections.Dictionary GetCapacityOverview()" in partial
    assert re.search(r"public Godot\.Collections\.Dictionary PreviewStocking\("
                     r'string siteId, int count, string strainId = "standard"\)', partial)
    assert "public Godot.Collections.Dictionary PreviewBuildSeaSite(string zoneId)" in partial
    for key in ("success", "regime", "fee_ceiling_nok", "areas", "sites", "licences", "breaches",
                "allowed", "reason_key", "label_key", "max_count", "area_mtb_t", "site_mtb_t",
                "licence_mtb_added_t", "locality_mtb_t", "area_licence_mtb_t", "cost_nok", "pens"):
        assert f'result["{key}"]' in partial, f"capacity bridge lacks key {key}"
    for key in ("production_area", "licence_mtb_t", "standing_t", "headroom_t", "projected_peak_t",
                "projected_peak_day", "breach_open", "order_deadline_day", "locality_mtb_t",
                "licences_registered", "mtb_t", "origin", "status_key", "fine_nok", "confiscated_nok",
                "forced_harvest_t", "sites_over_locality"):
        assert f'["{key}"] =' in partial, f"capacity rows lack key {key}"

    scripts = REPO / "godot" / "scripts"
    proxies = (scripts / "bridge" / "SimProxies.gd").read_text(encoding="utf-8")
    facade = (scripts / "SimBridge.gd").read_text(encoding="utf-8")
    assert re.search(r"func get_capacity_overview\(\) -> Dictionary: return game_bridge\.GetCapacityOverview\(\)", proxies)
    assert re.search(r'func preview_stocking\(site_id: String, count: int, strain_id: String = "standard"\) -> Dictionary: '
                     r"return game_bridge\.PreviewStocking\(site_id, count, strain_id\)", proxies)
    assert re.search(r"func preview_build_sea_site\(zone_id: String\) -> Dictionary: return game_bridge\.PreviewBuildSeaSite\(zone_id\)", proxies)
    assert re.search(r"func get_capacity_overview\(\) -> Dictionary: return _proxies\.get_capacity_overview\(\)", facade)
    assert re.search(r'func preview_stocking\(site_id: String, count: int, strain_id: String = "standard"\) -> Dictionary: '
                     r"return _proxies\.preview_stocking\(site_id, count, strain_id\)", facade)
    assert re.search(r"func preview_build_sea_site\(zone_id: String\) -> Dictionary: return _proxies\.preview_build_sea_site\(zone_id\)", facade)


def test_every_capacity_reason_and_breach_status_has_english_and_norwegian_text():
    import csv
    views = (REPO / "src" / "LakskongenSim" / "Core" / "CapacityViews.cs").read_text(encoding="utf-8")
    reasons = [v for v in re.findall(r'public const string \w+ = "([a-z_]*)";', views) if v]
    breach = (REPO / "src" / "LakskongenSim" / "Finance" / "CapacityBreach.cs").read_text(encoding="utf-8")
    statuses = re.findall(r'public const string \w+ = "([a-z_]+)";', breach)
    assert len(reasons) == 4 and len(statuses) == 3, f"contract harness broken: {reasons} {statuses}"
    keys = ["CAPACITY_REASON_" + r.upper() for r in reasons] + ["CAPACITY_BREACH_" + s.upper() for s in statuses]
    with (REPO / "godot" / "translations" / "translations.csv").open(encoding="utf-8", newline="") as f:
        rows = {r[0]: r for r in csv.reader(f) if len(r) == 3}
    missing = [k for k in keys if k not in rows or not rows[k][1].strip() or not rows[k][2].strip()]
    assert not missing, f"capacity labels without en/no text: {missing}"

# ── F2-03: site kinds and the farm-type lanes' slices ─────────────────────────
# GetSeaSiteDetails lists the fjord sites unless asked (offshore units must
# never shift the fjord's index-keyed pen slots); the land and sea lanes each
# own one partial and one marked block in SimProxies.gd and SimBridge.gd, and
# F2-03's list verbs reach the facade through them.

LANE_BLOCKS = {
    "land": ("GameBridge.LandSites.cs", ("GetLandSites", "GetLandPlots"), ("get_land_sites", "get_land_plots")),
    "sea": ("GameBridge.Offshore.cs", ("GetOffshoreUnits", "GetOffshorePlots"), ("get_offshore_units", "get_offshore_plots")),
}


def _lane_block(src: str, lane: str) -> str:
    begin = re.findall(rf"(?m)^.*BEGIN sim·{lane} lane block.*$", src)
    end = re.findall(rf"(?m)^.*END sim·{lane} lane block.*$", src)
    assert len(begin) == 1 and len(end) == 1, f"one BEGIN/END sim·{lane} lane block expected ({len(begin)}/{len(end)})"
    start = src.index(begin[0])
    stop = src.index(end[0])
    assert start < stop, f"sim·{lane} block ends before it begins"
    return src[start:stop]


def test_sea_site_details_list_the_fjord_unless_asked():
    farm = (BRIDGE_DIR / "GameBridge.Farm.cs").read_text(encoding="utf-8")
    assert re.search(r"public Godot\.Collections\.Array<Godot\.Collections\.Dictionary> GetSeaSiteDetails\(\)\s*"
                     r"=> SeaSiteDetails\(includeOffshore: false\);", farm), \
        "GetSeaSiteDetails() must list the fjord sites (SeaSiteDetails(includeOffshore: false))"
    assert re.search(r"public Godot\.Collections\.Array<Godot\.Collections\.Dictionary> GetAllSeaSiteDetails\(\)\s*"
                     r"=> SeaSiteDetails\(includeOffshore: true\);", farm), \
        "GetAllSeaSiteDetails() must list every sea site (SeaSiteDetails(includeOffshore: true))"
    assert "if (!includeOffshore && site.Kind != Farm.SiteKind.Fjord) continue;" in farm
    for key in ("kind", "plot_id"):
        assert re.search(r'\["' + key + r'"\]\s*= site\.', farm), f"the sea-site details lack key {key}"
    # GDScript calls the fjord verb with no argument (Godot never fills a C#
    # default), and the fjord's index-keyed consumers never read every sea site.
    callers = []
    for path in (REPO / "godot").rglob("*.gd"):
        if "addons" in path.parts:
            continue
        for call in re.findall(r"\bGetSeaSiteDetails\(([^)]*)\)", path.read_text(encoding="utf-8", errors="ignore")):
            callers.append((str(path.relative_to(REPO)), call.strip()))
    assert callers, "contract harness broken: no GetSeaSiteDetails caller found"
    assert all(arg == "" for _, arg in callers), f"a call passes an argument the C# verb does not take: {callers}"
    fjord_consumers = ("environment/FishPenManager.gd", "environment/FjordVistaBridge.gd",
                       "visual/FeedBargeVisualManager.gd", "bridge/SimActions.gd")
    for rel in fjord_consumers:
        src = (REPO / "godot" / "scripts" / rel).read_text(encoding="utf-8")
        assert not re.search(r"GetAllSeaSiteDetails|get_all_sea_site_details", src), f"{rel} reads every sea site"
    actions = (REPO / "godot" / "scripts" / "bridge" / "SimActions.gd").read_text(encoding="utf-8")
    assert re.search(r"func get_all_pens\(\)(?:(?!\nfunc )[\s\S])*?game_bridge\.GetSeaSiteDetails\(\)", actions), \
        "get_all_pens (the fjord's pen layout and barges read it) must call GetSeaSiteDetails()"


def test_each_farm_type_lane_owns_one_partial_and_one_marked_block():
    scripts = REPO / "godot" / "scripts"
    proxies = (scripts / "bridge" / "SimProxies.gd").read_text(encoding="utf-8")
    facade = (scripts / "SimBridge.gd").read_text(encoding="utf-8")
    for lane, (partial_name, verbs, funcs) in LANE_BLOCKS.items():
        partial = (BRIDGE_DIR / partial_name).read_text(encoding="utf-8")
        _lane_block(partial, lane)
        other = "sea" if lane == "land" else "land"
        assert f"sim·{other} lane block" not in partial, f"{partial_name} holds the {other} lane's block"
        for verb in verbs:
            assert re.search(rf"public Godot\.Collections\.Array<Godot\.Collections\.Dictionary> {verb}\(\)", partial), \
                f"{partial_name} lacks {verb}"
        proxy_block = _lane_block(proxies, lane)
        facade_block = _lane_block(facade, lane)
        for func, verb in zip(funcs, verbs):
            assert re.search(rf"func {func}\(\) -> Array\[Dictionary\]:\s*\n?\s*return _arr\(game_bridge\.{verb}\(\)\)",
                             proxy_block), f"SimProxies {lane} block lacks {func} -> {verb}"
            assert re.search(rf"func {func}\(\) -> Array\[Dictionary\]: return _proxies\.{func}\(\)", facade_block), \
                f"SimBridge {lane} block lacks {func}"


def test_the_facility_name_parsers_are_the_bridges():
    farm = (BRIDGE_DIR / "GameBridge.Farm.cs").read_text(encoding="utf-8")
    assert "FacilityNames.TryParseRasScale(scale, out var rasScale)" in farm
    assert "FacilityNames.TryParseOffshoreType(farmType, out var offshoreType)" in farm
    assert "Enum.TryParse<LakskongenSim.Farm.RASScale>" not in farm
    assert "Enum.TryParse<LakskongenSim.Farm.OffshoreType>" not in farm


# ── F2-31: the legacy facility builds are QA-only ─────────────────────────────
# BuildRASFacility, BuildOffshoreFarm and BuildPostSmoltFacility build a unit at
# once, with no permits, construction or preview; the family-first build flow
# (BuildSiteDialog) replaced them in the player UI (the RAS and Offshore tabs'
# build buttons are gone). Only QA code may still call them: the test runner
# (godot/tests), the QA scenes and scripts (godot/qa, godot/scripts/qa), and the
# facade's own forwarder chain (SimBridge.gd → SimProxies.gd → GameBridge).

LEGACY_BUILDS = {
    "build_ras_facility": "BuildRASFacility",
    "build_offshore_farm": "BuildOffshoreFarm",
    "build_post_smolt_facility": "BuildPostSmoltFacility",
}
LEGACY_CALL_RE = re.compile(r"\b(" + "|".join(list(LEGACY_BUILDS) + list(LEGACY_BUILDS.values())) + r")\s*\(")
QA_DIRS = ("godot/tests/", "godot/qa/", "godot/scripts/qa/")
FORWARDER_FILES = ("godot/scripts/SimBridge.gd", "godot/scripts/bridge/SimProxies.gd")


def _legacy_build_callers(sources):
    """[(file, line)] of every legacy build call outside QA and the forwarders.

    `sources` maps a repo-relative path to its GDScript text. In the facade
    files only the forwarder of the same verb may name it (its `func` line and
    its body's call to the next link).
    """
    found = []
    for rel, text in sorted(sources.items()):
        if rel.startswith(QA_DIRS):
            continue
        func = None
        for lineno, line in enumerate(_strip_gd_comments(text).splitlines(), 1):
            m = re.match(r"\s*func\s+(\w+)\s*\(", line)
            if m:
                func = m.group(1)
            elif line.strip() and not line[:1].isspace():
                func = None
            for call in LEGACY_CALL_RE.finditer(line):
                name = call.group(1)
                snake = name if name in LEGACY_BUILDS else next(k for k, v in LEGACY_BUILDS.items() if v == name)
                if rel in FORWARDER_FILES and func == snake:
                    continue
                found.append((rel, lineno, line.strip()))
    return found


def _gdscript_sources():
    out = {}
    for path in (REPO / "godot").rglob("*.gd"):
        rel = str(path.relative_to(REPO))
        if "/.godot/" in rel or "/addons/" in rel:
            continue
        out[rel] = path.read_text(encoding="utf-8")
    return out


def test_no_gdscript_outside_qa_calls_the_legacy_facility_builds():
    sources = _gdscript_sources()
    assert "godot/scripts/ui/RASPanel.gd" in sources and "godot/scripts/ui/OffshorePanel.gd" in sources
    callers = _legacy_build_callers(sources)
    assert not callers, "legacy facility builds called outside QA:\n" + "\n".join(f"{f}:{n}: {l}" for f, n, l in callers)
    # The forwarders are still there for QA, and QA still uses them.
    assert re.search(r"func build_ras_facility\(", sources["godot/scripts/SimBridge.gd"])
    assert any(LEGACY_CALL_RE.search(t) for r, t in sources.items() if r.startswith("godot/tests/"))


def test_the_legacy_build_check_catches_a_call_outside_qa():
    # A panel calling the facade, the bridge directly, or a facade function
    # that is not the verb's own forwarder is found; QA and the forwarders are not.
    planted = {
        "godot/scripts/ui/RASPanel.gd": "func _on_build():\n\t_sim.build_ras_facility(\"x\", \"medium\")\n",
        "godot/scripts/ui/OffshorePanel.gd": "func _on_build():\n\tgame_bridge.BuildOffshoreFarm(\"x\", \"semi\")\n",
        "godot/scripts/SimBridge.gd": "func other() -> bool: return _proxies.build_offshore_farm(\"x\", \"y\")\n"
                                      "func build_ras_facility(n: String, s: String) -> bool: return _proxies.build_ras_facility(n, s)\n",
        "godot/scripts/bridge/SimProxies.gd": "func build_ras_facility(n: String, s: String) -> bool:\n"
                                             "\treturn game_bridge.BuildRASFacility(n, s)\n",
        "godot/tests/UiFlowTests.gd": "func t():\n\t_sim.build_ras_facility(\"x\", \"small\")\n",
        "godot/scripts/ui/Note.gd": "# a comment naming build_ras_facility( is prose\n",
    }
    found = {(f, n) for f, n, _ in _legacy_build_callers(planted)}
    assert found == {("godot/scripts/ui/RASPanel.gd", 2), ("godot/scripts/ui/OffshorePanel.gd", 2),
                     ("godot/scripts/SimBridge.gd", 1)}, found


if __name__ == "__main__":
    test_bridge_contract()
    test_no_snake_case_signal_access()
    test_csharp_autoload_contract()
    test_simbridge_facade_parity()
    test_bridge_consumers_call_real_methods_with_valid_arity()
    test_bridge_consumer_check_catches_the_known_failures()
    test_gamebridge_signatures_are_marshalable()
    test_mortality_breakdown_reaches_the_facade()
    test_every_mortality_label_key_has_english_and_norwegian_text()
    test_capacity_overview_reaches_the_facade()
    test_every_capacity_reason_and_breach_status_has_english_and_norwegian_text()
    test_sea_site_details_list_the_fjord_unless_asked()
    test_each_farm_type_lane_owns_one_partial_and_one_marked_block()
    test_the_facility_name_parsers_are_the_bridges()
    print("bridge contract OK")
