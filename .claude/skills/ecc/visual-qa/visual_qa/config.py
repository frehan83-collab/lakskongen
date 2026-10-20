"""
Configuration for visual-qa skill.
"""
import os
from pathlib import Path

ROOT = Path(__file__).parent.parent.parent.parent.parent.parent  # lakskongen root (config.py lives 6 levels below: .claude/skills/ecc/visual-qa/visual_qa/)

# Phash distance gate. Calibrated 2026-10-12 (session 3): two consecutive
# pinned-sim harness runs (baseline v2 vs rerun) measured a run-to-run noise
# floor of 12–22 (transient toasts + panel enter animations on live UI), so the
# previous default 8 produced false positives on this project. 25 keeps the
# tool useful for structural regressions while the diff gallery stays the
# human review surface for anything between the noise floor and 25.
THRESHOLD = int(os.environ.get("VISUAL_QA_THRESHOLD", "25"))
BASELINE_DIR = ROOT / "godot" / "screenshots" / "baseline"
CURRENT_DIR = ROOT / "godot" / "screenshots" / "game"
MANIFEST = BASELINE_DIR / "manifest.json"

SHOT_NAMES = [
    "01_main_menu",
    "02_scenario_selection",
    "03_dashboard",
    "03b_scale80_dashboard",
    "03c_scale125_dashboard",
    "04_telemetry",
    "05_map_overlay",
    "06_finance",
    "06b_finance_trends",
    "07_genetics",
    "07b_crew",
    "07c_contracts",
    "07d_offshore",
    "07e_carbon_ledger",
    "07f_pen_management",
    "07g_attention_inbox",
    "08_fjord_day",
    "08a_fjord_live_sync",
    "08b_fjord_farm",
    "09_fjord_night_aurora",
    "10_fjord_storm",
    "11_fjord_underwater",
    "12_fjord_lice_red_zone",
    "13_fjord_no_bloom",
    "13b_fjord_bloom",
    "14_diag_no_tilt",
    "15_diag_no_underwater",
    "16_diag_no_farm",
]