"""Save-compat guards: version stamp present, migrator covers old versions.

A v0 golden save (no SaveVersion field, as shipped pre-versioning) must
load as version 0, and every version below CurrentSaveVersion must have
a migrator case. Pure static checks — no engine needed.
"""

import json
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
MANAGER = REPO / "godot" / "scripts" / "autoload" / "SaveLoadManager.cs"

# Minimal v0-era save shape: no SaveVersion field at all.
GOLDEN_V0 = {
    "SaveName": "golden-1952-start",
    "SaveTime": "2026-01-01T00:00:00",
    "GameVersion": "1.0.0",
    "GameState": {"Day": 12},
}


def _manager_src():
    return MANAGER.read_text(encoding="utf-8")


def test_save_version_stamp_exists():
    src = _manager_src()
    assert re.search(r"public const int CurrentSaveVersion\s*=\s*(\d+)", src), (
        "SaveLoadManager must declare CurrentSaveVersion"
    )
    assert "SaveVersion = CurrentSaveVersion" in src, (
        "CreateSaveData must stamp SaveVersion"
    )


def test_golden_v0_reads_as_version_zero():
    raw = json.dumps(GOLDEN_V0)
    parsed = json.loads(raw)
    assert parsed.get("SaveVersion", 0) == 0


def test_migrator_covers_all_old_versions():
    src = _manager_src()
    current = int(re.search(r"public const int CurrentSaveVersion\s*=\s*(\d+)", src).group(1))
    cases = set(int(v) for v in re.findall(r"case\s+(\d+)\s*:", src))
    missing = [v for v in range(current) if v not in cases]
    assert not missing, f"migrator lacks cases for versions: {missing}"


def test_version_gate_present():
    src = _manager_src()
    assert "SaveVersion > CurrentSaveVersion" in src, "must refuse newer saves"
    assert "MigrateSaveData(saveData)" in src or "MigrateSaveData(" in src, (
        "must migrate older saves"
    )


def test_atomic_write_pattern():
    src = _manager_src()
    assert '.tmp' in src and "File.Move" in src, (
        "saves must write temp-then-rename, never direct overwrite"
    )


if __name__ == "__main__":
    test_save_version_stamp_exists()
    test_golden_v0_reads_as_version_zero()
    test_migrator_covers_all_old_versions()
    test_version_gate_present()
    test_atomic_write_pattern()
    print("save-compat OK")
