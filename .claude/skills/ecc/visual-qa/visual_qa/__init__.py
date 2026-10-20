"""
visual-qa — Visual QA workflow for Lakskongen screenshot harness.
"""
__version__ = "1.0.0"

from .config import (
    THRESHOLD,
    BASELINE_DIR,
    CURRENT_DIR,
    MANIFEST,
    SHOT_NAMES,
)

__all__ = [
    "THRESHOLD",
    "BASELINE_DIR",
    "CURRENT_DIR",
    "MANIFEST",
    "SHOT_NAMES",
]