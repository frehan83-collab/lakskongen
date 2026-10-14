#!/usr/bin/env python3
"""Configuration for design-token-sync skill."""

from __future__ import annotations

import os
import shutil
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent.parent

# Source and target token files
DESIGN_TOKENS = REPO_ROOT / "design" / "tokens.json"
GODOT_TOKENS = REPO_ROOT / "godot" / "theme" / "tokens.json"
THEME_SCRIPT = REPO_ROOT / "godot" / "theme" / "default_theme.gd"

# The harness shots a token change can affect: menus, every document panel and
# the inbox (tools/capture_screenshots.sh names). 3D shots are token-independent.
UI_SHOTS = [
    "01_main_menu", "02_scenario_selection", "03_dashboard", "03b_scale80_dashboard",
    "03c_scale125_dashboard", "04_telemetry", "05_map_overlay", "06_finance",
    "06b_finance_trends", "07_genetics", "07b_crew", "07c_contracts", "07d_offshore",
    "07e_carbon_ledger", "07f_pen_management", "07g_attention_inbox",
]

# Screenshot directories
SCREENSHOT_BASE = REPO_ROOT / "godot" / "screenshots" / "design-token-sync"
SCREENSHOT_BEFORE = SCREENSHOT_BASE / "before"
SCREENSHOT_AFTER = SCREENSHOT_BASE / "after"

# Perceptual hash threshold (0-64, lower = stricter)
# 25 = the harness's run-to-run noise floor (visual_qa/config.py), not 8.
THRESHOLD = int(os.environ.get("DESIGN_TOKEN_SYNC_THRESHOLD", "25"))

# Godot binary
GODOT_BIN = os.environ.get("GODOT_BIN") or shutil.which("godot-mono") or "/Applications/Godot.app/Contents/MacOS/Godot"

# Figma webhook
FIGMA_WEBHOOK_SECRET = os.environ.get("FIGMA_WEBHOOK_SECRET", "")

# GitHub
GITHUB_TOKEN = os.environ.get("GITHUB_TOKEN", "")