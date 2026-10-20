"""Tests for the quality gate's own tools (review steps 13-14).

The export gate passed when it exported nothing, and the autoload audit
failed every clean checkout on a git-ignored tooling addon. These pin the
fixed behaviour without needing Godot.
"""

import re
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

REPO = Path(__file__).parent.parent
sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "godot-mcp-ops"))

from autoload_audit import AutoloadAudit  # noqa: E402
from export_verify import ExportVerify, exportable_on_host  # noqa: E402

MAC = {"name": "macOS", "platform": "macOS", "export_path": "../build/macos/Game.app"}
LINUX = {"name": "Linux", "platform": "Linux", "export_path": "../build/linux/Game.x86_64"}


def _export_ops(tmp_path, presets, exported):
    def export(name, output_path):
        exported.append(name)
        Path(output_path).parent.mkdir(parents=True, exist_ok=True)
        Path(output_path).write_bytes(b"binary")
        return SimpleNamespace(returncode=0, stderr="")

    headless = SimpleNamespace(get_export_presets=lambda: presets, export=export)
    return SimpleNamespace(project_path=tmp_path / "godot", headless=headless)


def test_a_linux_host_exports_linux_presets_only():
    assert exportable_on_host([MAC, LINUX], "linux") == [LINUX]
    assert exportable_on_host([MAC, LINUX], "darwin") == [MAC, LINUX]


def test_the_export_gate_fails_when_it_exports_nothing(tmp_path, monkeypatch):
    exported = []
    result = ExportVerify(_export_ops(tmp_path, [], exported)).run()
    assert not result["success"]
    assert "exported nothing" in result["errors"][0]

    monkeypatch.setattr(sys, "platform", "linux")
    result = ExportVerify(_export_ops(tmp_path, [MAC], exported)).run()
    assert not result["success"], "a macOS-only preset list exports nothing on Linux"
    assert exported == []


def test_the_export_gate_exports_the_hosts_presets(tmp_path, monkeypatch):
    monkeypatch.setattr(sys, "platform", "linux")
    exported = []
    result = ExportVerify(_export_ops(tmp_path, [MAC, LINUX], exported)).run()
    assert result["success"], result["errors"]
    assert exported == ["Linux"]
    assert result["data"]["total"] == 1
    assert any("macOS" in w for w in result["warnings"])


def test_a_missing_git_ignored_tooling_autoload_warns_but_a_tracked_one_fails(tmp_path):
    subprocess.run(["git", "init", "-q", str(tmp_path)], check=True)
    (tmp_path / ".gitignore").write_text("tooling_addon/\n")
    project = tmp_path / "godot"
    (project / "scripts").mkdir(parents=True)
    (project / "scripts" / "Present.gd").write_text("extends Node\n")
    audit = AutoloadAudit(SimpleNamespace(project_path=project, _use_mcp=False, mcp_client=None))
    result = audit._analyze_autoloads([
        {"name": "Present", "script_path": "res://scripts/Present.gd"},
        {"name": "Tooling", "script_path": "res://addons/tooling_addon/server.gd"},
        {"name": "Ghost", "script_path": "res://scripts/Ghost.gd"},
    ], "headless")
    assert result["errors"] == ["Autoload 'Ghost': missing script at 'res://scripts/Ghost.gd'"]
    assert any("Tooling" in w and "git-ignored" in w for w in result["warnings"])
    assert not result["success"]


def test_every_ui_flow_test_is_run_exactly_once():
    """UiFlowTests.gd calls its tests by hand from _run(): a test that is
    written but never called passes silently (the check count in
    tools/run_ui_flow_tests.sh only notices checks that existed before)."""
    source = (REPO / "godot" / "tests" / "UiFlowTests.gd").read_text()
    defined = re.findall(r"^func (_test_\w+)\(", source, re.MULTILINE)
    run_body = source.split("\nfunc _run()", 1)[1].split("\nfunc ", 1)[0]
    run_body = re.sub(r"#[^\n]*", "", run_body)  # a commented-out call does not run
    called = re.findall(r"\b(_test_\w+)\(\)", run_body)
    assert len(defined) >= 29
    assert sorted(set(defined) - set(called)) == [], "defined but never run"
    assert sorted(set(called) - set(defined)) == [], "run but not defined"
    assert len(called) == len(set(called)), "a test runs twice"
