"""Smoke tests for tools/merge/translations_merge.py, the captain's three-way
merge of godot/translations/translations.csv (docs/geo/STAGE23_PLAN.md §4,
hot-file rules): new keys land in one sorted tail after the anchor row, values
merge per key, and a real conflict writes nothing."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parent.parent
TOOL = REPO / "tools" / "merge" / "translations_merge.py"
SHIPPED = REPO / "godot" / "translations" / "translations.csv"
sys.path.insert(0, str(TOOL.parent))

import translations_merge as tm  # noqa: E402

ANCHOR = tm.ANCHOR
BASE = (
    "key,en,no\n"
    "UI_A,Alpha,Alfa\n"
    "UI_B,Beta,Beta\n"
    f"{ANCHOR},The site was not found,Fant ikke lokaliteten\n"
    "BUILD_X,Build,Bygg\n"
    "ZONE_Y,Zone,Sone\n"
)


def _run(tmp_path: Path, base: str, ours: str, theirs: str) -> tuple[subprocess.CompletedProcess, Path]:
    paths = []
    for name, text in (("base", base), ("ours", ours), ("theirs", theirs)):
        p = tmp_path / f"{name}.csv"
        p.write_text(text, encoding="utf-8", newline="")
        paths.append(str(p))
    out = tmp_path / "out.csv"
    proc = subprocess.run([sys.executable, str(TOOL), *paths, str(out)], capture_output=True, text=True)
    return proc, out


def test_merging_the_shipped_file_with_itself_is_byte_identical():
    text = SHIPPED.read_text(encoding="utf-8")
    table = tm.parse(text, "shipped")
    assert ANCHOR in table.order
    assert tm.merge(table, table, table).text == text


def test_both_sides_new_keys_join_one_sorted_tail_after_the_anchor(tmp_path):
    ours = BASE.replace("UI_B,Beta,Beta\n", "UI_B,Beta two,Beta to\n") + "MAP_GEO_TITLE,Map,Kart\n"
    theirs = BASE.replace("ZONE_Y,Zone,Sone\n", "") + "GEO_AREA,Area,Område\nRIVAL_OWNER,Owner,Eier\n"
    proc, out = _run(tmp_path, BASE, ours, theirs)
    assert proc.returncode == 0, proc.stdout + proc.stderr
    keys = [line.split(",", 1)[0] for line in out.read_text(encoding="utf-8").splitlines()]
    # Head keeps ours' order up to the anchor (with ours' changed value); the
    # tail is every later key, sorted; theirs' deletion of ZONE_Y wins.
    assert keys == ["key", "UI_A", "UI_B", ANCHOR, "BUILD_X", "GEO_AREA", "MAP_GEO_TITLE", "RIVAL_OWNER"]
    assert "UI_B,Beta two,Beta to\n" in out.read_text(encoding="utf-8")
    assert "theirs_only=2" in proc.stdout


def test_a_key_both_sides_changed_differently_conflicts_and_writes_nothing(tmp_path):
    ours = BASE.replace("UI_A,Alpha,Alfa", "UI_A,Alpha one,Alfa en")
    theirs = BASE.replace("UI_A,Alpha,Alfa", "UI_A,Alpha two,Alfa to")
    proc, out = _run(tmp_path, BASE, ours, theirs)
    assert proc.returncode == 1
    assert "CONFLICT UI_A" in proc.stdout
    assert not out.exists()


def test_the_same_key_added_twice_with_different_text_conflicts(tmp_path):
    proc, out = _run(tmp_path, BASE, BASE + "GEO_NEW,One,En\n", BASE + "GEO_NEW,Two,To\n")
    assert proc.returncode == 1 and "CONFLICT GEO_NEW" in proc.stdout
    assert not out.exists()


def test_duplicate_keys_or_a_missing_anchor_are_refused(tmp_path):
    proc, _ = _run(tmp_path, BASE, BASE + "UI_A,Again,Igjen\n", BASE)
    assert proc.returncode == 2 and "duplicate keys" in proc.stderr
    with pytest.raises(tm.MergeInputError, match="anchor"):
        no_anchor = tm.parse(BASE.replace(ANCHOR, "SOMETHING_ELSE"), "ours")
        tm.merge(no_anchor, no_anchor, no_anchor)
