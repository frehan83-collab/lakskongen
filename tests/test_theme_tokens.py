"""Kyst & Stål design tokens and the generated UI theme (roadmap U1-01, U1-02).

Pins the art direction of roadmap D13/D14 without starting Godot:
  * the palette is exactly the D13 table and every declared pair meets its
    WCAG minimum (U1-02 acceptance 1);
  * primary buttons are Ink text on salmon, never white on salmon;
  * the generated themes are current for the tokens and the builder, and the
    project theme is set in project.godot (U1-01);
  * type: Inter (tabular figures) plus a display face, no monospace in the
    player UI, a 13 px floor, and no per-node font-size override in scenes or
    UI scripts where a theme type variation exists (U1-02 acceptance 2 and 3);
  * every literal type variation used by a scene or script exists.
The runtime half (theme resolution and contrast on every screen) lives in
godot/tests/UiFlowTests.gd (_test_theme_and_contrast_on_every_screen).
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parent.parent
GODOT = REPO / "godot"
TOKENS = json.loads((REPO / "design" / "tokens.json").read_text(encoding="utf-8"))
INK_THEME = GODOT / "theme" / "lakskongen_theme.tres"
CHART_THEME = GODOT / "theme" / "lakskongen_chart_theme.tres"
PLAYER_UI_SCRIPTS = sorted((GODOT / "scripts" / "ui").glob("*.gd")) + sorted((GODOT / "ui").glob("*.gd"))
PLAYER_SCENES = sorted((GODOT / "scenes").rglob("*.tscn")) + sorted((GODOT / "ui").glob("*.tscn"))

sys.path.insert(0, str(REPO / "tools"))
import sync_tokens  # noqa: E402


# ── colour maths (WCAG 2.1) ───────────────────────────────────────────────────

def _lin(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _lum(hexstr: str) -> float:
    h = hexstr.lstrip("#")
    r, g, b = (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))
    return 0.2126 * _lin(r) + 0.7152 * _lin(g) + 0.0722 * _lin(b)


def ratio(fg: str, bg: str) -> float:
    a, b = _lum(fg), _lum(bg)
    return (max(a, b) + 0.05) / (min(a, b) + 0.05)


def token(path: str) -> str:
    node = TOKENS
    for part in path.split("."):
        node = node[part]
    return str(node)


# ── D13 palette and contrast ──────────────────────────────────────────────────

D13_INK = {"base": "#0B2A3A", "text": "#E8EEF0", "muted": "#9FB4BB", "ok": "#7FD69B", "warn": "#F2B705",
           "crit": "#FF8A80", "info": "#8CC7DA", "accent": "#F2876A"}
D13_CHART = {"base": "#F4F1EA", "text": "#1C1E20", "muted": "#5A6168", "ok": "#2E6B45", "warn": "#8A5A00",
             "crit": "#B3172F", "info": "#1F5E73", "accent": "#B5452B"}
# Ratios as roadmap D13 states them (one decimal).
D13_INK_RATIOS = {"text": 12.7, "muted": 6.9, "ok": 8.5, "warn": 8.2, "crit": 6.5, "info": 8.0, "accent": 6.0}
D13_CHART_RATIOS = {"text": 14.8, "muted": 5.6, "ok": 5.6, "warn": 5.3, "crit": 6.0, "info": 6.4, "accent": 4.8}


def test_palette_is_the_d13_table():
    assert {k: TOKENS["surfaces"]["ink"][k].upper() for k in D13_INK} == D13_INK
    assert {k: TOKENS["surfaces"]["chart"][k].upper() for k in D13_CHART} == D13_CHART
    assert TOKENS["surfaces"]["ink"]["base-alpha"] == pytest.approx(0.9)
    assert TOKENS["brand"]["salmon"].upper() == "#F2876A"
    assert TOKENS["brand"]["salmon-deep"].upper() == "#D9583B"


@pytest.mark.parametrize("surface,expected", [("ink", D13_INK_RATIOS), ("chart", D13_CHART_RATIOS)])
def test_d13_ratios_reproduce(surface, expected):
    colours = TOKENS["surfaces"][surface]
    got = {k: round(ratio(colours[k], colours["base"]), 1) for k in expected}
    # contrast.py rounds 12.74 to 12.7 and 4.83 to 4.8: allow the half step.
    for key, want in expected.items():
        assert abs(got[key] - want) <= 0.1, f"{surface}.{key}: {got[key]} vs D13 {want}"


@pytest.mark.parametrize("pair", TOKENS["contrast"]["pairs"], ids=lambda p: p["use"])
def test_declared_pair_meets_its_minimum(pair):
    r = ratio(token(pair["fg"]), token(pair["bg"]))
    assert r >= float(pair["min"]), f"{pair['use']}: {r:.2f} < {pair['min']}"


def test_every_surface_text_colour_is_declared_on_base_raised_and_sunken():
    declared = {(p["fg"], p["bg"]) for p in TOKENS["contrast"]["pairs"]}
    for surface in ("ink", "chart"):
        for role in ("text", "muted"):
            for bg in ("base", "raised", "sunken"):
                assert (f"surfaces.{surface}.{role}", f"surfaces.{surface}.{bg}") in declared
        for role in ("ok", "warn", "crit", "info", "accent"):
            for bg in ("base", "raised"):
                assert (f"surfaces.{surface}.{role}", f"surfaces.{surface}.{bg}") in declared


def test_white_on_salmon_is_not_allowed_and_ink_on_salmon_is():
    assert ratio("#FFFFFF", token("brand.salmon")) < 4.5  # the D13 "never" case
    assert token("brand.on-salmon").upper() == token("surfaces.ink.base").upper()
    assert ratio(token("brand.on-salmon"), token("brand.salmon")) >= 4.5


# ── generated theme ───────────────────────────────────────────────────────────

def _tres_items(path: Path) -> dict[str, str]:
    """Top-level 'Type/kind/name = value' lines of a generated Theme .tres."""
    items: dict[str, str] = {}
    in_resource = False
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("[resource]"):
            in_resource = True
            continue
        if in_resource and " = " in line:
            key, value = line.split(" = ", 1)
            items[key] = value
    return items


def _tres_subresources(path: Path) -> dict[str, dict[str, str]]:
    subs: dict[str, dict[str, str]] = {}
    current = None
    for line in path.read_text(encoding="utf-8").splitlines():
        m = re.match(r'^\[sub_resource type="\w+" id="([^"]+)"\]$', line)
        if m:
            current = subs.setdefault(m.group(1), {})
            continue
        if line.startswith("["):
            current = None
        elif current is not None and " = " in line:
            k, v = line.split(" = ", 1)
            current[k] = v
    return subs


def _color_hex(value: str) -> str:
    nums = [float(x) for x in re.findall(r"[-\d.]+", value)[:3]]
    return "#" + "".join(f"{round(n * 255):02X}" for n in nums)


def test_project_theme_is_set_and_generated_themes_are_current():
    assert sync_tokens.project_theme_setting() == "res://theme/lakskongen_theme.tres"
    assert sync_tokens.drift_messages() == []


def test_stale_builder_is_detected(tmp_path, monkeypatch):
    """The --check stamp must notice a builder change without starting Godot."""
    edited = tmp_path / "theme_builder.gd"
    edited.write_bytes(sync_tokens.BUILDER_SOURCES[3].read_bytes() + b"\n# edited\n")
    sources = list(sync_tokens.BUILDER_SOURCES)
    sources[3] = edited
    monkeypatch.setattr(sync_tokens, "BUILDER_SOURCES", sources)
    problems = sync_tokens.drift_messages()
    assert any("different theme builder" in p for p in problems)


def test_theme_stamps_match_the_token_file():
    want = hashlib.sha256((GODOT / "theme" / "tokens.json").read_bytes()).hexdigest()
    for path, surface in ((INK_THEME, "ink"), (CHART_THEME, "chart")):
        stamp = sync_tokens.theme_stamp(path)
        assert stamp["tokens_sha256"] == want
        assert stamp["surface"] == surface


@pytest.mark.parametrize("theme_path,surface", [(INK_THEME, "ink"), (CHART_THEME, "chart")])
def test_primary_buttons_are_ink_on_salmon(theme_path, surface):
    items = _tres_items(theme_path)
    subs = _tres_subresources(theme_path)
    for variation in ("PrimaryButton", "SmallPrimaryButton", "LargePrimaryButton"):
        assert _color_hex(items[f"{variation}/colors/font_color"]) == token("brand.on-salmon").upper()
        sb_id = re.search(r'SubResource\("([^"]+)"\)', items[f"{variation}/styles/normal"]).group(1)
        assert _color_hex(subs[sb_id]["bg_color"]) == token("brand.salmon").upper()


@pytest.mark.parametrize("theme_path,surface", [(INK_THEME, "ink"), (CHART_THEME, "chart")])
def test_base_label_colours_follow_the_surface(theme_path, surface):
    items = _tres_items(theme_path)
    assert _color_hex(items["Label/colors/font_color"]) == token(f"surfaces.{surface}.text").upper()
    assert _color_hex(items["BodyMuted/colors/font_color"]) == token(f"surfaces.{surface}.muted").upper()
    assert _color_hex(items["KpiCrit/colors/font_color"]) == token(f"surfaces.{surface}.crit").upper()


HC_INK_THEME = GODOT / "theme" / "lakskongen_theme_hc.tres"
HC_CHART_THEME = GODOT / "theme" / "lakskongen_chart_theme_hc.tres"
# The HUD glass: the top bar and tab rail, glass panels, chips over the world,
# every toast, tooltips. Ink on every surface; high-contrast Ink on the HC ones.
HUD_GLASS = ("Rail", "Glass", "Chip", "Toast", "ToastMuted", "ToastOk", "ToastWarn", "ToastCrit", "ToastInfo",
             "ToastAccent", "TooltipPanel")


def _alpha(value: str) -> float:
    nums = re.findall(r"[-\d.]+", value)
    return float(nums[3]) if len(nums) > 3 else 1.0


def _panel_box(items: dict[str, str], subs: dict[str, dict[str, str]], variation: str) -> dict[str, str]:
    return subs[re.search(r'SubResource\("([^"]+)"\)', items[f"{variation}/styles/panel"]).group(1)]


@pytest.mark.parametrize("hc_path,normal_path", [(HC_INK_THEME, INK_THEME), (HC_CHART_THEME, CHART_THEME)])
def test_high_contrast_reaches_the_hud_glass_toasts_and_tooltips(hc_path, normal_path):
    """U2-08 review: Glass, Rail, Chip, the toasts and the tooltip were built
    from the normal Ink palette in both high-contrast themes (0.9-alpha glass,
    a 1.5:1 hairline), so high contrast never reached the HUD. In the HC
    themes they are opaque, their edge at least 3:1 on the ink-hc base, and
    different from the normal theme's."""
    base = token("surfaces.ink-hc.base")
    hc_items, hc_subs = _tres_items(hc_path), _tres_subresources(hc_path)
    n_items, n_subs = _tres_items(normal_path), _tres_subresources(normal_path)
    for variation in HUD_GLASS:
        hc = _panel_box(hc_items, hc_subs, variation)
        normal = _panel_box(n_items, n_subs, variation)
        assert _alpha(hc["bg_color"]) == 1.0, f"{hc_path.name} {variation}: translucent {hc['bg_color']}"
        assert _alpha(hc["border_color"]) == 1.0, f"{hc_path.name} {variation}: faded edge {hc['border_color']}"
        assert ratio(_color_hex(hc["border_color"]), base) >= 3.0, f"{hc_path.name} {variation}: edge under 3:1"
        assert hc["bg_color"] != normal["bg_color"], f"{hc_path.name} {variation}: same as the normal theme"


@pytest.mark.parametrize("hc_path", [HC_INK_THEME, HC_CHART_THEME])
def test_high_contrast_primary_buttons_are_black_on_salmon_at_7_to_1(hc_path):
    items = _tres_items(hc_path)
    subs = _tres_subresources(hc_path)
    for variation in ("PrimaryButton", "SmallPrimaryButton", "LargePrimaryButton"):
        fg = _color_hex(items[f"{variation}/colors/font_color"])
        assert fg == "#000000"
        for state in ("normal", "hover", "pressed"):
            sb_id = re.search(r'SubResource\("([^"]+)"\)', items[f"{variation}/styles/{state}"]).group(1)
            assert ratio(fg, _color_hex(subs[sb_id]["bg_color"])) >= 7.0, f"{variation} {state}"


# ── typography ────────────────────────────────────────────────────────────────

MIN_PX = int(TOKENS["fonts"]["min-size"])


def test_type_scale_has_a_13px_floor():
    assert MIN_PX == 13
    assert min(TOKENS["fonts"]["sizes"].values()) >= MIN_PX
    consts = re.findall(r"^const (FONT_\w+)\s*:= (\d+)", (GODOT / "theme" / "default_theme.gd").read_text(encoding="utf-8"), re.M)
    assert consts and all(int(v) >= MIN_PX for _, v in consts), consts


@pytest.mark.parametrize("theme_path", [INK_THEME, CHART_THEME])
def test_generated_theme_has_no_size_below_the_floor(theme_path):
    sizes = [int(v) for k, v in _tres_items(theme_path).items() if "/font_sizes/" in k]
    assert sizes and min(sizes) >= MIN_PX
    assert int(re.search(r"^default_font_size = (\d+)$", theme_path.read_text(encoding="utf-8"), re.M).group(1)) >= MIN_PX


def test_two_font_families_inter_and_display():
    families = set()
    for font in (GODOT / "theme" / "fonts").glob("*.tres"):
        for path in re.findall(r'path="res://assets/fonts/([^"]+)"', font.read_text(encoding="utf-8")):
            families.add(path.split("-")[0])
    assert families <= {"Inter", "BarlowCondensed"}, families
    assert "Inter" in families
    tnum = 1953396077  # TextServer.name_to_tag("tnum")
    for font in (GODOT / "theme" / "fonts").glob("ui_*.tres"):
        text = font.read_text(encoding="utf-8")
        if "base_font = ExtResource" in text:
            assert f"{tnum}: 1" in text, f"{font.name} lacks tabular figures"


def test_no_monospace_in_the_player_ui():
    assert not list((GODOT / "assets" / "fonts").glob("JetBrains*"))
    for path in PLAYER_UI_SCRIPTS + PLAYER_SCENES + sorted((GODOT / "theme").glob("*.gd")):
        text = path.read_text(encoding="utf-8")
        assert "JetBrains" not in text and "font_mono" not in text, path


def test_font_licences_ship_with_the_fonts():
    assert "SIL OPEN FONT LICENSE Version 1.1" in (GODOT / "assets" / "fonts" / "OFL-Inter.txt").read_text(encoding="utf-8")
    if list((GODOT / "assets" / "fonts").glob("BarlowCondensed-*")):
        assert (GODOT / "assets" / "fonts" / "OFL-BarlowCondensed.txt").exists()


def test_no_per_node_font_or_colour_overrides_in_scenes():
    offenders = []
    for scene in PLAYER_SCENES:
        for n, line in enumerate(scene.read_text(encoding="utf-8").splitlines(), 1):
            if re.match(r"theme_override_(font_sizes|fonts|colors|styles)/", line):
                offenders.append(f"{scene.relative_to(GODOT)}:{n}: {line}")
    assert offenders == []


def test_no_per_node_font_overrides_in_ui_scripts():
    offenders = []
    for script in PLAYER_UI_SCRIPTS:
        for n, line in enumerate(script.read_text(encoding="utf-8").splitlines(), 1):
            if "add_theme_font_size_override(" in line or "add_theme_font_override(" in line:
                offenders.append(f"{script.relative_to(GODOT)}:{n}: {line.strip()}")
            if "ThemeDB.fallback_font" in line:
                offenders.append(f"{script.relative_to(GODOT)}:{n}: {line.strip()}")
    assert offenders == []


def test_drawn_text_respects_the_floor():
    offenders = []
    for script in PLAYER_UI_SCRIPTS:
        for n, line in enumerate(script.read_text(encoding="utf-8").splitlines(), 1):
            m = re.search(r"draw_(?:multiline_)?string\(.*?,\s*-1,\s*(\d+)\s*,", line)
            if m and int(m.group(1)) < MIN_PX:
                offenders.append(f"{script.relative_to(GODOT)}:{n}")
            m = re.match(r"\s*(?:const|var)\s+\w*(?:FONT_SIZE|LABEL_SIZE|font_size)\w*\s*:?=\s*(\d+)\b", line)
            if m and int(m.group(1)) < MIN_PX:
                offenders.append(f"{script.relative_to(GODOT)}:{n}: {line.strip()}")
    assert offenders == []


NORD_V2 = re.compile(r"#(2E3440|3B4252|434C5E|4C566A|7B8794|616E88|A3BE8C|8FAD78|EBCB8B|D4B06A|D08770|BF616A|"
                     r"A54E56|88C0D0|81A1C1|5E81AC|0A4D68|0D5F80|083D53|ECEFF4|E5E9F0|D8DEE9|F8F9FB|A3D1DE)\b", re.I)


def test_the_v2_nordic_palette_is_gone_from_the_player_ui():
    offenders = []
    for path in PLAYER_UI_SCRIPTS + PLAYER_SCENES:
        for n, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if NORD_V2.search(line):
                offenders.append(f"{path.relative_to(GODOT)}:{n}: {line.strip()[:80]}")
    assert offenders == []


# ── variations ────────────────────────────────────────────────────────────────

def _declared_types(theme_path: Path) -> set[str]:
    return {key.split("/", 1)[0] for key in _tres_items(theme_path)}


def test_every_literal_type_variation_exists_in_both_themes():
    used: dict[str, str] = {}
    for scene in PLAYER_SCENES:
        for name in re.findall(r'^theme_type_variation = &"(\w+)"', scene.read_text(encoding="utf-8"), re.M):
            used.setdefault(name, str(scene.relative_to(GODOT)))
    for script in PLAYER_UI_SCRIPTS:
        text = script.read_text(encoding="utf-8")
        for name in re.findall(r'theme_type_variation\s*=\s*&"(\w+)"', text):
            used.setdefault(name, str(script.relative_to(GODOT)))
        for name in re.findall(r'set_role\([^,]+,\s*&"(\w+)"\)', text):
            used.setdefault(name, str(script.relative_to(GODOT)))
    assert used, "no variations found — the scan is broken"
    for theme_path in (INK_THEME, CHART_THEME):
        declared = _declared_types(theme_path)
        missing = {name: where for name, where in used.items() if name not in declared}
        assert missing == {}, f"{theme_path.name}: {missing}"


# ── surfaces ──────────────────────────────────────────────────────────────────

DOCUMENT_SCENES = ["Dashboard", "FinancePanel", "PenManagement", "ContractsPanel", "CrewPanel", "OffshorePanel",
                   "CarbonLedgerPanel", "TelemetryPanel", "MapPanel", "EncyclopediaPanel", "DataExportPanel",
                   "RASPanel", "AIFeedingPanel", "ProductionChainPanel", "SustainabilityPanel",
                   "WeatherDiseasePanel", "StockMarketPanel", "StakeholderPanel", "EmpirePanel", "GeneticsPanel"]


@pytest.mark.parametrize("name", DOCUMENT_SCENES)
def test_document_panels_sit_on_chart_paper(name):
    """D13: documents are Chart paper — on their root, so editor previews and
    standalone use (harness, tests) match the game."""
    text = (GODOT / "scenes" / "ui" / f"{name}.tscn").read_text(encoding="utf-8")
    ext = re.search(r'\[ext_resource type="Theme" path="res://theme/lakskongen_chart_theme.tres" id="([^"]+)"\]', text)
    assert ext, f"{name} does not load the Chart theme"
    root = re.search(r'^\[node name="[^"]+" type="[^"]+"\]\n((?:[^\[\n][^\n]*\n)*)', text, flags=re.M)
    assert root and f'theme = ExtResource("{ext.group(1)}")' in root.group(1), f"{name} root lacks the Chart theme"


def test_gameui_documents_area_is_chart_and_rail_is_ink():
    text = (GODOT / "scenes" / "GameUI.tscn").read_text(encoding="utf-8")
    assert re.search(r'\[node name="PanelContainer" type="Control"[^\]]*\]\n(?:[^\[\n][^\n]*\n)*theme = ExtResource\("2_chart"\)', text)
    assert 'theme_type_variation = &"Rail"' in text
