#!/usr/bin/env python3
"""Configuration for design-token-sync skill."""

from __future__ import annotations

import os
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent.parent

# Source and target token files
DESIGN_TOKENS = REPO_ROOT / "design" / "tokens.json"
GODOT_TOKENS = REPO_ROOT / "godot" / "theme" / "tokens.json"
THEME_SCRIPT = REPO_ROOT / "godot" / "theme" / "default_theme.gd"

# Visual regression panel scenes (Godot resource paths)
PANEL_SCENES = {
    "Dashboard": "res://ui/dashboard/Dashboard.tscn",
    "FinancePanel": "res://ui/finance/FinancePanel.tscn",
    "ToastNotification": "res://ui/common/ToastNotification.tscn",
}

# Screenshot directories
SCREENSHOT_BASE = REPO_ROOT / "godot" / "screenshots" / "design-token-sync"
SCREENSHOT_BEFORE = SCREENSHOT_BASE / "before"
SCREENSHOT_AFTER = SCREENSHOT_BASE / "after"

# Perceptual hash threshold (0-64, lower = stricter)
THRESHOLD = int(os.environ.get("DESIGN_TOKEN_SYNC_THRESHOLD", "8"))

# Godot binary
GODOT_BIN = os.environ.get("GODOT_BIN", "/Applications/Godot.app/Contents/MacOS/Godot")

# Figma webhook
FIGMA_WEBHOOK_SECRET = os.environ.get("FIGMA_WEBHOOK_SECRET", "")

# GitHub
GITHUB_TOKEN = os.environ.get("GITHUB_TOKEN", "")