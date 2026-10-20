"""F2-01 golden traces: the committed set is complete and self-describing, and
the record script refuses bad options before it touches git (docs/GOLDEN_TRACES.md).
The bit-exact comparisons run in .NET (GoldenTraceTests)."""

import gzip
import json
import subprocess
from pathlib import Path

REPO = Path(__file__).parent.parent
TRACES = REPO / "src" / "LakskongenSim.Tests" / "Fixtures" / "golden_traces"
SCRIPT = REPO / "tools" / "record_golden_traces.sh"
SCENARIOS = REPO / "godot" / "data" / "scenarios.json"


def _manifest():
    return json.loads((TRACES / "manifest.json").read_text(encoding="utf-8"))


def test_the_manifest_lists_every_shipped_scenario_on_seeds_1_7_13_and_every_file():
    manifest = _manifest()
    shipped = sorted(s["id"] for s in json.loads(SCENARIOS.read_text(encoding="utf-8")))
    assert manifest["days"] == 730
    assert manifest["snapshotEveryDays"] == 30
    assert manifest["seeds"] == [1, 7, 13]
    assert sorted(manifest["scenarios"]) == shipped
    listed = {f["file"] for f in manifest["files"]}
    expected = {f"{s}.seed{seed:02d}.jsonl.gz" for s in shipped for seed in (1, 7, 13)}
    assert listed == expected
    on_disk = {p.name for p in TRACES.glob("*.jsonl.gz")}
    assert on_disk == expected


def test_every_trace_starts_with_a_header_that_lists_its_groups_and_exclusions():
    for entry in _manifest()["files"]:
        with gzip.open(TRACES / entry["file"], "rt", encoding="utf-8") as f:
            header = json.loads(f.readline())
        assert header["scenario"] == entry["scenario"]
        assert header["seed"] == entry["seed"]
        assert header["days"] == 730
        assert header["detail"] is False
        assert {"env", "market", "pop", "lice", "mtb", "mort", "ops", "ledger", "cash", "rivals", "rng"} <= set(header["groups"])
        assert any("Guid" in item for item in header["excluded"])


def _run_script(*args):
    return subprocess.run(["bash", str(SCRIPT), *args], capture_output=True, text=True, timeout=60)


def test_the_record_script_prints_its_usage():
    result = _run_script("--help")
    assert result.returncode == 0
    assert "--base REV" in result.stdout and "--update" in result.stdout


def test_the_record_script_refuses_conflicting_options_before_touching_git():
    result = _run_script("--base", "HEAD", "--against", "/nonexistent")
    assert result.returncode == 2
    assert "exclude each other" in result.stderr
    result = _run_script("--update", "--out", "/nonexistent")
    assert result.returncode == 2
    assert "exclude each other" in result.stderr
