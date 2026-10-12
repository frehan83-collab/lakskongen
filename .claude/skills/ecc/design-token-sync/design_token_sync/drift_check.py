#!/usr/bin/env python3
"""Deep drift detection: design/tokens.json vs godot/theme/tokens.json vs LakskongenTheme consts"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

try:
    from .config import DESIGN_TOKENS, GODOT_TOKENS, THEME_SCRIPT
except ImportError:
    # Running as standalone script
    sys.path.insert(0, str(Path(__file__).parent))
    from config import DESIGN_TOKENS, GODOT_TOKENS, THEME_SCRIPT


def load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    return json.loads(path.read_text())


def extract_consts_from_theme() -> dict[str, Any]:
    """Extract all const values from default_theme.gd"""
    if not THEME_SCRIPT.exists():
        return {}
    
    content = THEME_SCRIPT.read_text()
    consts = {}
    
    # Pattern: const NAME := Value
    const_pattern = re.compile(r'const\s+(\w+)\s*:=\s*([^\n]+)')
    
    for match in const_pattern.finditer(content):
        name = match.group(1)
        value = match.group(2).strip().rstrip(',')
        
        # Parse Color("#HEX") or simple values
        if value.startswith('Color("'):
            hex_match = re.search(r'Color\("([^"]+)"\)', value)
            if hex_match:
                consts[name] = hex_match.group(1)
        elif value.isdigit() or (value.startswith('-') and value[1:].isdigit()):
            consts[name] = int(value)
        elif value.replace('.', '').isdigit():
            consts[name] = float(value)
        elif value.startswith('"') and value.endswith('"'):
            consts[name] = value[1:-1]
        else:
            consts[name] = value
    
    return consts


def compare_dicts(source: dict, target: dict, prefix: str = "") -> tuple[int, list[str]]:
    """Compare two dicts recursively, return (drift_count, messages)"""
    drift_count = 0
    messages = []
    all_keys = set(source.keys()) | set(target.keys())
    
    for key in sorted(all_keys):
        full_key = f"{prefix}.{key}" if prefix else key
        s_val = source.get(key)
        t_val = target.get(key)
        
        if key not in source:
            drift_count += 1
            messages.append(f"  {full_key}: MISSING in source (target={t_val})")
        elif key not in target:
            drift_count += 1
            messages.append(f"  {full_key}: MISSING in target (source={s_val})")
        elif s_val != t_val:
            drift_count += 1
            messages.append(f"  {full_key}: DRIFT — source={s_val} vs target={t_val}")
        else:
            messages.append(f"  {full_key}: ✓ ({s_val})")
    
    return drift_count, messages


def compare_colors(design: dict, godot: dict, consts: dict) -> tuple[int, list[str]]:
    drift_count = 0
    messages = ["colors:"]
    
    design_colors = design.get("colors", {})
    godot_colors = godot.get("colors", {})
    
    # Compare design vs godot tokens
    dc, msgs = compare_dicts(design_colors, godot_colors, "colors")
    drift_count += dc
    messages.extend(msgs)
    
    # Check LakskongenTheme consts against tokens
    const_color_map = {
        "PRIMARY": "primary",
        "PRIMARY_LIGHT": "primary-light",
        "PRIMARY_DARK": "primary-dark",
        "SECONDARY": "secondary",
        "SECONDARY_LIGHT": "secondary-light",
        "ACCENT": "accent",
        "ACCENT_LIGHT": "accent-light",
        "BG": "bg",
        "BG_DARK": "bg-dark",
        "SURFACE": "surface",
        "SURFACE_ELEVATED": "surface-elevated",
        "SURFACE_DARK": "surface-dark",
        "TEXT_PRIMARY": "text",
        "TEXT_SECONDARY": "text-secondary",
        "TEXT_MUTED": "text-muted",
        "TEXT_INVERSE": "text-inverse",
        "BORDER": "border",
        "BORDER_LIGHT": "border-light",
        "SUCCESS": "success",
        "SUCCESS_DARK": "success-dark",
        "WARNING": "warning",
        "WARNING_DARK": "warning-dark",
        "DANGER": "danger",
        "DANGER_DARK": "danger-dark",
        "INFO": "info",
        "WATER_BLUE": "water-blue",
        "SALMON_PINK": "salmon-pink",
        "FJORD_TEAL": "fjord-teal",
    }
    
    for const_name, token_key in const_color_map.items():
        if const_name in consts and token_key in design_colors:
            const_val = consts[const_name].upper()
            token_val = design_colors[token_key].upper()
            if const_val != token_val:
                drift_count += 1
                messages.append(f"  const.{const_name}: DRIFT — const={const_val} vs token={token_val}")
            else:
                messages.append(f"  const.{const_name}: ✓ ({const_val})")
    
    return drift_count, messages


def compare_spacing(design: dict, godot: dict, consts: dict) -> tuple[int, list[str]]:
    drift_count = 0
    messages = ["spacing:"]
    
    design_spacing = design.get("spacing", {})
    godot_spacing = godot.get("spacing", {})
    
    dc, msgs = compare_dicts(design_spacing, godot_spacing, "spacing")
    drift_count += dc
    messages.extend(msgs)
    
    const_spacing_map = {
        "SPACING_XS": "xs",
        "SPACING_SM": "sm",
        "SPACING_MD": "md",
        "SPACING_LG": "lg",
        "SPACING_XL": "xl",
    }
    
    for const_name, token_key in const_spacing_map.items():
        if const_name in consts and token_key in design_spacing:
            const_val = consts[const_name]
            token_val = design_spacing[token_key]
            if const_val != token_val:
                drift_count += 1
                messages.append(f"  const.{const_name}: DRIFT — const={const_val} vs token={token_val}")
            else:
                messages.append(f"  const.{const_name}: ✓ ({const_val})")
    
    return drift_count, messages


def compare_radii(design: dict, godot: dict, consts: dict) -> tuple[int, list[str]]:
    drift_count = 0
    messages = ["radii:"]
    
    design_radii = design.get("radii", {})
    godot_radii = godot.get("radii", {})
    
    dc, msgs = compare_dicts(design_radii, godot_radii, "radii")
    drift_count += dc
    messages.extend(msgs)
    
    const_radii_map = {
        "RADIUS_SM": "sm",
        "RADIUS_MD": "md",
        "RADIUS_LG": "lg",
    }
    
    for const_name, token_key in const_radii_map.items():
        if const_name in consts and token_key in design_radii:
            const_val = consts[const_name]
            token_val = design_radii[token_key]
            if const_val != token_val:
                drift_count += 1
                messages.append(f"  const.{const_name}: DRIFT — const={const_val} vs token={token_val}")
            else:
                messages.append(f"  const.{const_name}: ✓ ({const_val})")
    
    return drift_count, messages


def compare_fonts(design: dict, godot: dict, consts: dict) -> tuple[int, list[str]]:
    drift_count = 0
    messages = ["fonts.sizes:"]
    
    design_sizes = design.get("fonts", {}).get("sizes", {})
    godot_sizes = godot.get("fonts", {}).get("sizes", {})
    
    dc, msgs = compare_dicts(design_sizes, godot_sizes, "fonts.sizes")
    drift_count += dc
    messages.extend(msgs)
    
    const_font_map = {
        "FONT_XS": "xs",
        "FONT_SM": "sm",
        "FONT_BODY": "md",
        "FONT_LG": "lg",
        "FONT_HEADER": "xl",
        "FONT_TITLE": "xxl",
        "FONT_HERO": "hero",
    }
    
    for const_name, token_key in const_font_map.items():
        if const_name in consts and token_key in design_sizes:
            const_val = consts[const_name]
            token_val = design_sizes[token_key]
            if const_val != token_val:
                drift_count += 1
                messages.append(f"  const.{const_name}: DRIFT — const={const_val} vs token={token_val}")
            else:
                messages.append(f"  const.{const_name}: ✓ ({const_val})")
    
    return drift_count, messages


def compare_shadows(design: dict, godot: dict) -> tuple[int, list[str]]:
    drift_count = 0
    messages = ["shadows:"]
    
    design_shadows = design.get("shadows", {})
    godot_shadows = godot.get("shadows", {})
    
    dc, msgs = compare_dicts(design_shadows, godot_shadows, "shadows")
    drift_count += dc
    messages.extend(msgs)
    
    return drift_count, messages


def compare_grid(design: dict, godot: dict) -> tuple[int, list[str]]:
    drift_count = 0
    messages = ["grid:"]
    
    design_grid = design.get("grid", {})
    godot_grid = godot.get("grid", {})
    
    dc, msgs = compare_dicts(design_grid, godot_grid, "grid")
    drift_count += dc
    messages.extend(msgs)
    
    return drift_count, messages


def compare_breakpoints(design: dict, godot: dict) -> tuple[int, list[str]]:
    drift_count = 0
    messages = ["breakpoints:"]
    
    design_bp = design.get("breakpoints", {})
    godot_bp = godot.get("breakpoints", {})
    
    dc, msgs = compare_dicts(design_bp, godot_bp, "breakpoints")
    drift_count += dc
    messages.extend(msgs)
    
    return drift_count, messages


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Deep drift check: design tokens vs Godot tokens vs LakskongenTheme consts")
    parser.add_argument("--threshold", type=int, default=0, help="Max allowed drift count (default 0)")
    args = parser.parse_args(argv)
    
    print("[design-token-sync] Drift check: design/tokens.json vs godot/theme/tokens.json vs LakskongenTheme consts")
    
    design = load_json(DESIGN_TOKENS)
    godot = load_json(GODOT_TOKENS)
    consts = extract_consts_from_theme()
    
    if not design:
        print("[design-token-sync] ERROR: design/tokens.json not found or empty", file=sys.stderr)
        return 1
    if not godot:
        print("[design-token-sync] ERROR: godot/theme/tokens.json not found or empty", file=sys.stderr)
        return 1
    
    total_drift = 0
    all_messages = []
    
    # Compare each category
    for compare_fn in [
        compare_colors,
        compare_spacing,
        compare_radii,
        compare_fonts,
        compare_shadows,
        compare_grid,
        compare_breakpoints,
    ]:
        if compare_fn == compare_colors:
            dc, msgs = compare_fn(design, godot, consts)
        elif compare_fn in (compare_spacing, compare_radii, compare_fonts):
            dc, msgs = compare_fn(design, godot, consts)
        else:
            dc, msgs = compare_fn(design, godot)
        total_drift += dc
        all_messages.extend(msgs)
    
    # Print results
    for msg in all_messages:
        print(msg)
    
    print(f"\n[design-token-sync] Total drift: {total_drift}")
    
    if total_drift > args.threshold:
        print(f"[design-token-sync] Drift exceeds threshold ({args.threshold})", file=sys.stderr)
        print("[design-token-sync] Run: python -m design_token_sync.sync_tokens_extended --write", file=sys.stderr)
        return 1
    
    print("[design-token-sync] No drift detected ✓")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())