"""
verdict — the pass/fail rules of the visual diff, kept free of image
libraries so they can be tested anywhere.

A missing shot fails the gate: an empty capture used to print "All 28 shots
within threshold" and exit 0, and an explicit --threshold 0 was ignored
(review F1110).
"""
from typing import Any

MISSING = ("MISSING_BASELINE", "MISSING_CURRENT", "MISSING_BASELINE_FILE")


def resolve_threshold(cli: int | None, manifest: dict[str, Any], default: int) -> int:
    """The CLI value when given (0 included), else the manifest's, else the default."""
    if cli is not None:
        return cli
    return int(manifest.get("threshold", default))


def shot_status(distance: int, threshold: int) -> str:
    return "FAIL" if distance > threshold else "PASS"


def problems(results: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Results that fail the gate: over the threshold, or missing."""
    return [r for r in results if r["status"] == "FAIL" or r["status"] in MISSING]
