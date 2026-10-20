"""Text correctness: the static half of roadmap U1-03 (and D17).

Checks the sources without starting Godot:
  * translations.csv is well formed: one row per key, an English and a
    Norwegian text for every key (acceptance 2);
  * every key the code asks for through tr(), _tr_or() (GDScript and the C#
    bridge) or TranslationServer exists in both languages (acceptance 2);
  * tr() is never handed a literal text, only a key (acceptance 3);
  * every value of each enum that crosses the bridge, every ledger category
    and every scenario zone has a display name in both languages;
  * one FCR target: the bridge reads it from CalibrationRegistry and no
    GDScript file carries its own FCR target number (acceptance 4);
  * no real company or organisation name appears in player-facing text
    (D17, acceptance 6): string literals in GDScript and C#, scene text, the
    data files and the translations.
The runtime half (the label-dump lint on every screen in Norwegian, and the
nb-NO number and date formatter tests) lives in godot/tests/UiFlowTests.gd
(_test_text_is_norwegian_on_every_screen, _test_formatters_are_norwegian).
"""

from __future__ import annotations

import csv
import json
import re
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parent.parent
GODOT = REPO / "godot"
SIM = REPO / "src" / "LakskongenSim"
TRANSLATIONS = GODOT / "translations" / "translations.csv"
SKIP_DIRS = {".godot", "addons", "bin", "obj"}


def _files(root: Path, pattern: str) -> list[Path]:
    return sorted(p for p in root.rglob(pattern) if not SKIP_DIRS.intersection(p.relative_to(REPO).parts))


GD_SOURCES = [p for p in _files(GODOT, "*.gd") if "tests" not in p.relative_to(GODOT).parts[:1]]
BRIDGE_CS = sorted((GODOT / "scripts" / "autoload").glob("*.cs"))
SIM_CS = _files(SIM, "*.cs")


# ── Source tokenizers (strings vs code, comments dropped) ─────────────────────

def _scan(text: str, comment: str, block: bool) -> tuple[list[tuple[int, str]], str]:
    """Split source into its string literals [(line, value)] and its code with
    every string and comment blanked out. `comment` is the line-comment marker;
    `block` enables /* */ comments (C#). Handles "…", '…', C# @"…" and $"…"
    (interpolation holes stay part of the literal)."""
    strings: list[tuple[int, str]] = []
    code: list[str] = []
    i, n, line = 0, len(text), 1
    while i < n:
        c = text[i]
        if c == "\n":
            line += 1
            code.append(c)
            i += 1
        elif text.startswith(comment, i):
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif block and text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            line += text.count("\n", i, j)
            code.append("\n" * text.count("\n", i, j))
            i = j
        elif c in "\"'" or (block and c in "$@" and i + 1 < n and text[i + 1] in "\"$@"):
            k = i
            verbatim = False
            while k < n and text[k] in "$@":
                verbatim = verbatim or text[k] == "@"
                k += 1
            quote = text[k]
            if block and quote == "'":  # C# char literal
                j = text.find("'", k + 1 + (1 if text[k + 1:k + 2] == "\\" else 0))
                code.append(" ")
                i = j + 1
                continue
            j = k + 1
            buf = []
            while j < n and text[j] != quote:
                if text[j] == "\\" and not verbatim:
                    buf.append(text[j:j + 2])
                    j += 2
                    continue
                if text[j] == "\n":
                    line += 1
                buf.append(text[j])
                j += 1
            strings.append((line, "".join(buf)))
            code.append('""')
            i = j + 1
        else:
            code.append(c)
            i += 1
    return strings, "".join(code)


def gd_scan(path: Path) -> tuple[list[tuple[int, str]], str]:
    return _scan(path.read_text(encoding="utf-8"), "#", block=False)


def cs_scan(path: Path) -> tuple[list[tuple[int, str]], str]:
    return _scan(path.read_text(encoding="utf-8"), "//", block=True)


def strip_gd_comments(text: str) -> str:
    """GDScript source with comments removed but strings kept."""
    out = []
    for raw in text.split("\n"):
        in_str = None
        cut = len(raw)
        for idx, ch in enumerate(raw):
            if in_str:
                if ch == in_str and raw[idx - 1] != "\\":
                    in_str = None
            elif ch in "\"'":
                in_str = ch
            elif ch == "#":
                cut = idx
                break
        out.append(raw[:cut])
    return "\n".join(out)


# ── Translations ──────────────────────────────────────────────────────────────

def load_rows() -> list[list[str]]:
    with TRANSLATIONS.open(encoding="utf-8", newline="") as f:
        return list(csv.reader(f))


ROWS = load_rows()
KEYS = {r[0]: (r[1], r[2]) for r in ROWS[1:] if len(r) == 3}


def test_translations_csv_is_well_formed():
    assert ROWS[0] == ["key", "en", "no"], ROWS[0]
    bad = [r for r in ROWS[1:] if len(r) != 3]
    assert not bad, f"rows without exactly key,en,no: {bad[:5]}"
    keys = [r[0] for r in ROWS[1:]]
    dupes = sorted({k for k in keys if keys.count(k) > 1})
    assert not dupes, f"duplicate keys: {dupes}"
    empty = [r[0] for r in ROWS[1:] if not r[1].strip() or not r[2].strip()]
    assert not empty, f"keys missing an English or Norwegian text: {empty}"
    bad_names = [k for k in keys if not re.fullmatch(r"[A-Z][A-Z0-9_]*", k)]
    assert not bad_names, f"keys must be UPPER_SNAKE: {bad_names[:10]}"


# A GDScript (%s %d %.1f) or .NET ({0} {1:N0}) placeholder; "%%" is a literal.
PLACEHOLDER = re.compile(r"%(?:%|[-+0#]*[0-9]*(?:\.[0-9]+)?[sdifxXoceEgGv])|\{[0-9]+(?::[^}]*)?\}")


def _placeholders(text: str) -> tuple[list[str], list[str]]:
    """The arguments a translated text consumes: GDScript conversion letters
    in order (positional; a float stays a float whatever its precision) and
    the .NET argument indexes as a sorted list (a language may reorder them)."""
    gd, net = [], []
    for m in PLACEHOLDER.finditer(text):
        tok = m.group(0)
        if tok == "%%":
            continue
        if tok.startswith("{"):
            net.append(tok.split(":")[0].strip("{}"))
        else:
            gd.append(tok[-1])
    return gd, sorted(net)


def test_english_and_norwegian_take_the_same_arguments():
    """Both languages are formatted with the same arguments: a placeholder
    missing in one language garbles the text ("not all arguments converted")
    or prints a raw "%s" there only."""
    bad = []
    for key, (en, no) in KEYS.items():
        a, b = _placeholders(en), _placeholders(no)
        if a != b:
            bad.append(f"{key}: en {a} / no {b}")
    assert not bad, "en and no placeholders differ:\n" + "\n".join(bad)


TR_LITERAL = re.compile(r'(?<![\w.])tr\(\s*(["\'])((?:\\.|(?!\1).)*)\1(\s*[%+])?')
TR_OR = re.compile(r'_tr_or(?:_format)?\(\s*"((?:\\.|[^"\\])*)"(\s*[%+])?')
TS_TRANSLATE = re.compile(r'TranslationServer\.[Tt]ranslate\(\s*"((?:\\.|[^"\\])*)"(\s*[%+])?')


def _requested_keys() -> list[tuple[str, str, bool]]:
    """[(where, key, is_template)] for every literal key the code requests."""
    out = []
    for path in GD_SOURCES:
        text = strip_gd_comments(path.read_text(encoding="utf-8"))
        for pat, grp in ((TR_LITERAL, 2), (TR_OR, 1), (TS_TRANSLATE, 1)):
            for m in pat.finditer(text):
                key = m.group(grp)
                templ = m.group(grp + 1) is not None or "%" in key
                out.append((f"{path.relative_to(REPO)}:{text.count(chr(10), 0, m.start()) + 1}", key, templ))
    for path in BRIDGE_CS:
        text = path.read_text(encoding="utf-8")
        for m in re.finditer(r'_tr_or\(\s*"([^"]*)"(\s*\+)?', text):
            out.append((f"{path.relative_to(REPO)}:{text.count(chr(10), 0, m.start()) + 1}", m.group(1), m.group(2) is not None))
    return out


def test_tr_is_never_given_a_literal_text():
    offenders = []
    for path in GD_SOURCES:
        text = strip_gd_comments(path.read_text(encoding="utf-8"))
        for m in TR_LITERAL.finditer(text):
            key = m.group(2)
            if not re.fullmatch(r"[A-Z][A-Z0-9_]*(%s[A-Z0-9_]*)?", key):
                line = text.count("\n", 0, m.start()) + 1
                offenders.append(f"{path.relative_to(REPO)}:{line}: tr({key!r})")
    assert not offenders, f"{len(offenders)} tr() calls with a literal text:\n" + "\n".join(offenders[:40])


def test_every_requested_key_exists_in_both_languages():
    missing = []
    for where, key, templ in _requested_keys():
        if templ:
            prefix = key.split("%")[0]
            if not any(k.startswith(prefix) for k in KEYS):
                missing.append(f"{where}: {key} (template)")
        elif key not in KEYS:
            missing.append(f"{where}: {key}")
    assert not missing, f"{len(missing)} keys missing from translations.csv:\n" + "\n".join(missing[:60])


SCENE_KEY_LINE = re.compile(r'^(?:text|tooltip_text|placeholder_text|title) = "([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)"$')


def test_scene_texts_that_are_keys_exist_in_both_languages():
    """A scene control whose text is a key (Godot translates it on screen)
    must find that key, or the player reads "UI_FJORD_HINT"."""
    missing = []
    for path in _files(GODOT, "*.tscn"):
        for no, line in enumerate(path.read_text(encoding="utf-8").split("\n"), 1):
            m = SCENE_KEY_LINE.match(line.strip())
            if m and m.group(1) not in KEYS:
                missing.append(f"{path.relative_to(REPO)}:{no}: {m.group(1)}")
    assert not missing, "scene texts name keys that translations.csv lacks:\n" + "\n".join(missing)


# ── Enum display tables ───────────────────────────────────────────────────────

ENUM_SOURCES = [
    ("Environment/WeatherSystem.cs", "WeatherState", "ENUM_WEATHER"),
    ("Data/Enums.cs", "ZoneColor", "ENUM_TRAFFIC_LIGHT"),
    ("Farm/ContainmentTech.cs", "ContainmentLevel", "ENUM_CONTAINMENT"),
    ("Farm/BarrierTech.cs", "BarrierKind", "ENUM_BARRIER"),
    ("Data/Enums.cs", "DelousingMethod", "ENUM_DELOUSING"),
    ("Data/Enums.cs", "GeneticTrait", "ENUM_TRAIT"),
    ("Farm/CohortTransferSystem.cs", "TransferReason", "ENUM_TRANSFER_REASON"),
    ("Logistics/NightShiftPolicy.cs", "NightShiftPolicy", "ENUM_NIGHT_SHIFT"),
    ("Data/Enums.cs", "DiseaseType", "ENUM_DISEASE"),
    ("Farm/SmoltQuality.cs", "SmoltQualityTier", "ENUM_SMOLT_TIER"),
    ("Market/ContractSystem.cs", "ContractType", "ENUM_CONTRACT_TYPE"),
    ("Biology/GeneticEngineering.cs", "RegulatoryStatus", "ENUM_GE_STATUS"),
    ("Core/AchievementSystem.cs", "AchievementTier", "ENUM_ACHIEVEMENT_TIER"),
]


def upper_snake(value: str) -> str:
    """Mirror of SimLabels.upper_snake (GDScript)."""
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", value.strip())
    s = re.sub(r"[^A-Za-z0-9]", "_", s)
    s = re.sub(r"_+", "_", s).strip("_")
    return s.upper()


def enum_members(rel: str, name: str) -> list[str]:
    _strings, code = cs_scan(SIM / rel)
    m = re.search(r"\benum\s+" + name + r"\b[^{]*\{([^}]*)\}", code)
    assert m, f"enum {name} not found in {rel}"
    return [re.split(r"[\s=]", part.strip())[0] for part in m.group(1).split(",") if part.strip()]


@pytest.mark.parametrize("rel,name,group", ENUM_SOURCES, ids=[e[1] for e in ENUM_SOURCES])
def test_every_enum_value_has_a_display_name(rel, name, group):
    members = enum_members(rel, name)
    assert members, name
    missing = [f"{group}_{upper_snake(m)}" for m in members if f"{group}_{upper_snake(m)}" not in KEYS]
    assert not missing, f"{name}: no display name for {missing}"


def test_simlabels_groups_match_the_enum_table():
    text = (GODOT / "scripts" / "bridge" / "SimLabels.gd").read_text(encoding="utf-8")
    groups = set(re.findall(r'const GROUP_\w+ := "([A-Z_]+)"', text))
    for _rel, name, group in ENUM_SOURCES:
        assert group in groups, f"SimLabels has no group {group} for {name}"


def test_every_ledger_category_has_a_display_name():
    cats = set()
    for path in SIM_CS:
        text = path.read_text(encoding="utf-8")
        cats.update(re.findall(r'CostBooking\.\w+\s*,\s*"([a-z0-9_]+)"', text))
        cats.update(re.findall(r'(?:ReceiveIncome|BookSettledCash)\([^;]*?"([a-z0-9_]+)"', text))
    assert len(cats) > 20, sorted(cats)
    missing = sorted(c for c in cats if f"LEDGER_{upper_snake(c)}" not in KEYS)
    assert not missing, f"ledger categories without a display name: {missing}"


def test_every_scenario_zone_has_a_display_name():
    scenarios = json.loads((GODOT / "data" / "scenarios.json").read_text(encoding="utf-8"))
    zones = {s.get("zone", "") for sc in scenarios for s in sc.get("starting_sites", [])}
    missing = sorted(z for z in zones if z and f"ZONE_{upper_snake(z)}" not in KEYS)
    assert not missing, f"scenario zones without a display name: {missing}"


def test_every_scenario_site_has_a_name():
    scenarios = json.loads((GODOT / "data" / "scenarios.json").read_text(encoding="utf-8"))
    nameless = [(sc["id"], s.get("id")) for sc in scenarios for s in sc.get("starting_sites", []) if not s.get("name")]
    assert not nameless, nameless


# ── One FCR target ────────────────────────────────────────────────────────────

# A number from 1.01 to 1.99 (1.0 is the neutral multiplier, not a target).
FCR_NUMBER = re.compile(r"\b1[.,](?!0+\b)[0-9]{1,2}\b")
FCR_TARGETISH = re.compile(r"[<>]=?\s*1[.,][0-9]|1[.,][0-9]+\s*[<>]|mål|target|terskel|threshold|strained|critical|goal",
                           re.I)


def test_fcr_target_comes_from_calibration_through_the_bridge():
    bridge = "\n".join(p.read_text(encoding="utf-8") for p in BRIDGE_CS)
    assert re.search(r"\.OptimalFCR\b", bridge) and re.search(r"\.PoorFCR\b", bridge), \
        "the bridge does not read the FCR target and ceiling from CalibrationRegistry"
    proxies = (GODOT / "scripts" / "bridge" / "SimProxies.gd").read_text(encoding="utf-8")
    assert "GetFcrTargets" in proxies, "SimProxies does not forward the FCR target"


def test_no_gdscript_file_carries_its_own_fcr_target():
    """An FCR target, threshold or comparison number in a GDScript file is a
    second target (FarmStateBanner, Telemetry, Dashboard and SimState each had
    one: 1.15, 1.25, 1.30, 1.35)."""
    offenders = []
    for path in GD_SOURCES:
        text = strip_gd_comments(path.read_text(encoding="utf-8"))
        for no, line in enumerate(text.split("\n"), 1):
            if re.search(r"fcr|fôrfaktor", line, re.I) and FCR_NUMBER.search(line) and FCR_TARGETISH.search(line):
                offenders.append(f"{path.relative_to(REPO)}:{no}: {line.strip()[:110]}")
    assert not offenders, "FCR targets outside CalibrationRegistry:\n" + "\n".join(offenders)


# A member whose name says target / optimal / poor FCR (not a temperature),
# given a number: "OptimalFCR { get; init; } = 1.15", "FCR_TARGET = 1.25".
_FCR_NAME = r"\b(?:\w*(?:Optimal|Target|Poor)_?FCR|\w*FCR_?\w*(?:Optimal|Target|Poor))(?!\w*Temp)\w*\b"
CS_FCR_TARGET_DECL = re.compile(_FCR_NAME + r"\s*(?:\{[^}]*\})?\s*=\s*[0-9]+(?:\.[0-9]+)?\b", re.I)


def test_the_sim_declares_the_fcr_target_once():
    """grep finds one FCR target constant: CalibrationRegistry.Biology. Every
    other target or ceiling (the legacy GameConfig defaults, the advisor's
    1.25) reads the registry."""
    places = []
    for path in SIM_CS:
        _strings, code = cs_scan(path)
        for no, line in enumerate(code.split("\n"), 1):
            if CS_FCR_TARGET_DECL.search(line):
                places.append(f"{path.relative_to(REPO)}:{no}: {line.strip()[:110]}")
    registry = [p for p in places if "Calibration/CalibrationRegistry.cs" in p]
    others = [p for p in places if p not in registry]
    assert len(registry) == 2, "CalibrationRegistry declares OptimalFCR and PoorFCR:\n" + "\n".join(places)
    assert not others, "FCR targets declared outside CalibrationRegistry:\n" + "\n".join(others)


# ── D17: parody names only ────────────────────────────────────────────────────

# Real companies, retailers, exchanges and organisations, matched as whole
# words with their capitals (ids such as "ge_growth_aquadvantage" stay legal).
BRANDS = [
    "Cermaq", "Bakkafrost", "Mowi", "Multiexport", "Whole Foods", "SalMar", "Salmar", "Lerøy",
    "Grieg", "Marine Harvest", "Nova Sea", "Norway Royal Salmon", "Måsøval", "Kvarøy",
    "SinkabergHansen", "Bremnes", "Hofseth", "Wilsgård", "Arnøy Laks", "Skagerak Salmon",
    "Eidsfjord", "AquaBounty", "AquAdvantage", "AquaGen", "SalmoBreed", "Benchmark Genetics",
    "Greenpeace", "WWF", "Bellona", "REMA 1000", "Coop Mega", "Skretting", "BioMar", "EWOS",
    "Cargill", "Nasdaq", "NASDAQ", "Fish Pool", "Euronext", "Oslo Børs", "Oslo Stock Exchange",
    "Stingray", "AquaCloud", "Nordlaks",
]
BRAND_RE = re.compile(r"(?<![\w-])(" + "|".join(re.escape(b) for b in BRANDS) + r")(?![\w-])")


def _player_text() -> list[tuple[str, str]]:
    out: list[tuple[str, str]] = []
    for path in GD_SOURCES:
        strings, _ = gd_scan(path)
        out += [(f"{path.relative_to(REPO)}:{ln}", s) for ln, s in strings]
    for path in BRIDGE_CS + SIM_CS:
        strings, _ = cs_scan(path)
        out += [(f"{path.relative_to(REPO)}:{ln}", s) for ln, s in strings]
    for path in _files(GODOT / "data", "*.json") + [TRANSLATIONS]:
        for ln, line in enumerate(path.read_text(encoding="utf-8").split("\n"), 1):
            out.append((f"{path.relative_to(REPO)}:{ln}", line))
    for path in _files(GODOT, "*.tscn"):
        for ln, line in enumerate(path.read_text(encoding="utf-8").split("\n"), 1):
            if re.match(r"\s*(text|tooltip_text|placeholder_text|title)\s*=", line):
                out.append((f"{path.relative_to(REPO)}:{ln}", line))
    return out


def test_no_real_company_names_in_player_text():
    hits = [f"{where}: {m.group(1)} in {text.strip()[:90]!r}"
            for where, text in _player_text() for m in BRAND_RE.finditer(text)]
    assert not hits, f"{len(hits)} real names in player-facing text (D17):\n" + "\n".join(hits[:60])


def test_brand_grep_catches_a_planted_name():
    """Guards the guard: the pattern matches names and spares ids/parodies."""
    assert BRAND_RE.search('buyers = ["Whole Foods"]')
    assert BRAND_RE.search("Mowi Scotland")
    assert not BRAND_RE.search("Mowier ASA")
    assert not BRAND_RE.search('"ge_growth_aquadvantage"')
    strings, _ = _scan('var a = "Cermaq" # Bakkafrost\n', "#", block=False)
    assert [s for _, s in strings] == ["Cermaq"]
    strings, _ = _scan('var b = $"x {y}"; // SalMar\n/* Grieg */ var c = @"Lerøy";', "//", block=True)
    assert [s for _, s in strings] == ["x {y}", "Lerøy"]
