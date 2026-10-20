"""Smoke tests for tools/merge/gd_decl_merge.py, the captain's three-way merge
of godot/tests/UiFlowTests.gd by top-level declaration (docs/geo/STAGE23_PLAN.md
§4, hot-file rules): both sides' new functions survive in place, a one-sided
change is taken, _run() is reported for a hand merge and never merged, and a
real conflict exits 1."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
TOOL = REPO / "tools" / "merge" / "gd_decl_merge.py"
SHIPPED = REPO / "godot" / "tests" / "UiFlowTests.gd"
sys.path.insert(0, str(TOOL.parent))

import gd_decl_merge as gm  # noqa: E402

BASE = """extends Node

## Header doc.

var _checks: int = 0

func _run() -> void:
\tawait _test_one()
\t_finish()

## The first test.
func _test_one() -> void:
\t_check(true, "one")

func _test_two() -> void:
\t_check(true, "two")

class _Helper:
\tfunc inner() -> int:
\t\treturn 1

func _finish() -> void:
\tpass
"""


def _names(text: str) -> list[str]:
    return [name for name, _ in gm.parse(text).blocks]


def _run(tmp_path: Path, base: str, ours: str, theirs: str) -> tuple[subprocess.CompletedProcess, str]:
    paths = []
    for name, text in (("base", base), ("ours", ours), ("theirs", theirs)):
        p = tmp_path / f"{name}.gd"
        p.write_text(text, encoding="utf-8", newline="")
        paths.append(str(p))
    out = tmp_path / "out.gd"
    proc = subprocess.run([sys.executable, str(TOOL), *paths, str(out)], capture_output=True, text=True)
    return proc, out.read_text(encoding="utf-8") if out.exists() else ""


def test_merging_the_shipped_suite_with_itself_is_byte_identical():
    text = SHIPPED.read_text(encoding="utf-8")
    parsed = gm.parse(text)
    assert "_run" in _names(text) and len(parsed.blocks) > 200
    result = gm.merge(parsed, parsed, parsed)
    assert result.text == text and not result.conflicts


def test_new_functions_from_both_sides_survive_and_run_is_left_for_the_captain(tmp_path):
    ours = BASE.replace("\t_finish()", "\tawait _test_ours()\n\t_finish()").replace(
        "func _test_two()", "func _test_ours() -> void:\n\t_check(true, \"ours\")\n\nfunc _test_two()")
    theirs = BASE.replace("\t_finish()", "\tawait _test_theirs()\n\t_finish()").replace(
        "class _Helper:", "## Theirs.\nfunc _test_theirs() -> void:\n\t_check(true, \"theirs\")\n\nclass _Helper:"
    ).replace('_check(true, "two")', '_check(true, "two, stronger")')
    proc, merged = _run(tmp_path, BASE, ours, theirs)
    assert proc.returncode == 0, proc.stdout
    assert _names(merged) == ["_checks", "_run", "_test_one", "_test_ours", "_test_two",
                              "_test_theirs", "_Helper", "_finish"]
    assert '"two, stronger"' in merged  # theirs' one-sided change is taken
    assert "## Theirs.\nfunc _test_theirs" in merged  # doc comments travel with their block
    run_block = dict(gm.parse(merged).blocks)["_run"]
    assert "\tawait _test_ours()" in run_block and "\tawait _test_theirs()" not in run_block
    assert "MANUAL _run() changed in theirs" in proc.stdout
    assert "  +\tawait _test_theirs()" in proc.stdout


def test_a_doc_comment_belongs_to_the_function_below_it(tmp_path):
    blocks = dict(gm.parse(BASE).blocks)
    assert blocks["_test_one"][:2] == ("## The first test.", "func _test_one() -> void:")
    theirs = BASE.replace("## The first test.", "## The first test, reworded.")
    proc, merged = _run(tmp_path, BASE, BASE, theirs)
    assert proc.returncode == 0 and "MANUAL" not in proc.stdout
    assert "## The first test, reworded.\nfunc _test_one" in merged


def test_a_function_both_sides_changed_differently_conflicts_and_keeps_ours(tmp_path):
    ours = BASE.replace('"one"', '"one, ours"')
    theirs = BASE.replace('"one"', '"one, theirs"')
    proc, merged = _run(tmp_path, BASE, ours, theirs)
    assert proc.returncode == 1
    assert "CONFLICT both changed _test_one" in proc.stdout
    assert '"one, ours"' in merged and '"one, theirs"' not in merged


def test_a_name_declared_twice_is_a_conflict(tmp_path):
    theirs = BASE + "\nfunc _test_one() -> void:\n\tpass\n"
    proc, _ = _run(tmp_path, BASE, BASE, theirs)
    assert proc.returncode == 1 and "declares a name twice" in proc.stdout
