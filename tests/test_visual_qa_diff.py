"""Tests for the visual diff gate (review F1110).

An empty capture printed "All 28 shots within threshold" and exited 0, and
an explicit --threshold 0 was ignored. The rules live in visual_qa.verdict,
which imports no image library, so they run in every pytest job; the wiring
test needs the visual-qa skill's Pillow + imagehash.
"""

import sys
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "visual-qa"))

from visual_qa.verdict import MISSING, problems, resolve_threshold, shot_status  # noqa: E402


def _results(*statuses):
    return [{"shot": f"s{i}", "status": s, "distance": -1} for i, s in enumerate(statuses)]


def test_a_missing_shot_fails_the_gate():
    for status in MISSING:
        assert problems(_results("PASS", status)) == [_results("PASS", status)[1]]


def test_an_empty_capture_fails_every_shot():
    empty = _results(*["MISSING_CURRENT"] * 28)
    assert len(problems(empty)) == 28


def test_the_manifest_threshold_applies_and_an_explicit_zero_is_kept():
    assert resolve_threshold(None, {"threshold": 25}, 8) == 25
    assert shot_status(20, resolve_threshold(None, {"threshold": 25}, 8)) == "PASS"
    assert resolve_threshold(0, {"threshold": 25}, 8) == 0
    assert shot_status(20, resolve_threshold(0, {"threshold": 25}, 8)) == "FAIL"
    assert resolve_threshold(None, {}, 25) == 25


def test_diff_fails_on_an_empty_capture(tmp_path, monkeypatch, capsys):
    pytest.importorskip("imagehash")
    from visual_qa import diff, phash

    baseline, current = tmp_path / "baseline", tmp_path / "game"
    baseline.mkdir()
    current.mkdir()
    (baseline / "a.png").write_bytes(b"png")
    manifest = baseline / "manifest.json"
    manifest.write_text('{"threshold": 25, "shots": {"a": {"phash": "0"}}}')
    monkeypatch.setattr(phash, "BASELINE_DIR", baseline)
    monkeypatch.setattr(phash, "CURRENT_DIR", current)
    monkeypatch.setattr(phash, "MANIFEST", manifest)
    monkeypatch.setattr(diff, "SHOT_NAMES", ["a"])

    assert diff.main(["--fail-on-diff"]) == 1
    out = capsys.readouterr().out
    assert "MISSING_CURRENT" in out and "1/1 shots failed" in out
