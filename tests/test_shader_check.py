"""Tests for the CI shader gate (review F958).

The old gate only load()ed each shader under --headless, where the dummy
renderer parses nothing, so it passed shaders that do not compile. These pin
the two parts that replaced it: the static hint check and the parsing of a
real-renderer compile run. None of them needs Godot.
"""

import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

REPO = Path(__file__).parent.parent
sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "godot-mcp-ops"))

import godot_mcp_ops  # noqa: E402
import shader_check  # noqa: E402
from shader_check import PROBE, ShaderCheck, parse_compile_log  # noqa: E402
from shader_hints import check_project_hints, check_shader_text  # noqa: E402

# Trimmed from a real run of shader_compile_probe.gd (Godot 4.7.2, Metal)
# over the two shaders that F958 found broken.
REAL_FAILED_RUN = """\
SHADER_CHECK_BEGIN res://shaders/fish_shader.gdshader
SHADER_CHECK_END res://shaders/fish_shader.gdshader
SHADER_CHECK_BEGIN res://shaders/fjord_sky.gdshader
SHADER ERROR: Expected valid type hint after ':'.
          at: (null) (res://shaders/fjord_sky.gdshader:13)
          GDScript backtrace (most recent call first):
              [0] _start (/repo/.claude/skills/ecc/godot-mcp-ops/shader_compile_probe.gd:49)
ERROR: Shader compilation failed.
   at: set_code (servers/rendering/renderer_rd/environment/sky.cpp:110)
ERROR: Parameter "version" is null.
   at: version_get_shader (./servers/rendering/renderer_rd/shader_rd.h:203)
SHADER_CHECK_END res://shaders/fjord_sky.gdshader
SHADER_CHECK_DONE count=2
"""

GOOD_SHADER = """\
shader_type spatial;
// uniform sampler2D commented_out : hint_default_noise;
/* uniform float also_commented : hint_nothing; */
uniform vec4 tint : source_color = vec4(0.1, 0.2, 0.3, 1.0);
uniform float strength : hint_range(0.0, 1.0, 0.01) = 0.5;
uniform int mode : hint_enum("Calm, still", "Storm") = 0;
uniform sampler2D noise : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float plain = 2.0;
group_uniforms waves;
instance uniform vec4 marker : source_color, instance_index(2);
"""


def test_the_hint_check_rejects_a_hint_godot_does_not_have():
    text = "shader_type sky;\n\nuniform sampler2D cloud_noise : hint_default_noise;\n"
    assert check_shader_text(text, "res://sky.gdshader") == [
        "res://sky.gdshader:3: unknown uniform hint 'hint_default_noise'"
    ]


def test_the_hint_check_rejects_godot_3_hint_names():
    text = "uniform vec4 albedo : hint_color;\nuniform sampler2D t : hint_albedo;\n"
    assert check_shader_text(text, "x") == [
        "x:1: unknown uniform hint 'hint_color'",
        "x:2: unknown uniform hint 'hint_albedo'",
    ]


def test_the_hint_check_reads_ranges_enums_defaults_and_comments():
    assert check_shader_text(GOOD_SHADER, "res://good.gdshader") == []


def test_comment_markers_inside_a_string_do_not_hide_the_next_hint():
    text = 'uniform int m : hint_enum("http://x", "/*y") = 0; uniform float f : hint_bogus;\n'
    assert check_shader_text(text, "x") == ["x:1: unknown uniform hint 'hint_bogus'"]


def test_godot_4_7_blit_hints_are_known():
    assert check_shader_text("uniform sampler2D source : hint_blit_source0;", "x") == []


def test_every_project_shader_uses_known_hints():
    assert check_project_hints(REPO / "godot") == []


def test_a_compile_run_reports_each_shader_error_once_where_godot_found_it():
    assert parse_compile_log(REAL_FAILED_RUN, 2) == [
        "res://shaders/fjord_sky.gdshader:13: Expected valid type hint after ':'."
    ]


def test_any_error_while_a_shader_is_drawn_fails_that_shader():
    """Deny by default: the RD backend reports a GLSL failure as "Error
    compiling <Stage> shader." and a probe script error leaves the shader
    undrawn; neither prints SHADER ERROR."""
    log = (
        "SHADER_CHECK_BEGIN res://a.gdshader\n"
        "ERROR: Error compiling Fragment shader.\n"
        "   at: compile_stages (servers/rendering/renderer_rd/shader_rd.cpp:300)\n"
        "SHADER_CHECK_END res://a.gdshader\n"
        "SHADER_CHECK_BEGIN res://b.gdshader\n"
        "SCRIPT ERROR: Invalid assignment of property 'sky_material'.\n"
        "SHADER_CHECK_END res://b.gdshader\n"
        "SHADER_CHECK_DONE count=2\n"
        "ERROR: 6 resources still in use at exit (run with --verbose for details).\n"
    )
    assert parse_compile_log(log, 2) == [
        "res://a.gdshader: ERROR: Error compiling Fragment shader.",
        "res://b.gdshader: SCRIPT ERROR: Invalid assignment of property 'sky_material'.",
    ]


def test_a_compile_run_that_stops_early_fails():
    stopped = "SHADER_CHECK_BEGIN res://a.gdshader\n"
    assert parse_compile_log(stopped, 1) == ["the compile run stopped before it reached every shader"]
    short = "SHADER_CHECK_DONE count=1\n"
    assert parse_compile_log(short, 3) == ["the compile run covered 1 of 3 shaders"]


def _project(tmp_path, shader_text):
    project = tmp_path / "godot"
    (project / "shaders").mkdir(parents=True)
    (project / "shaders" / "a.gdshader").write_text(shader_text)
    (project / ".godot").mkdir()
    (project / ".godot" / "cached.gdshader").write_text("uniform float x : hint_bogus;")
    return project


def test_without_a_display_the_compile_is_reported_skipped_not_passed(tmp_path, monkeypatch):
    monkeypatch.setattr(shader_check, "display_prefix", lambda: None)
    ops = SimpleNamespace(project_path=_project(tmp_path, GOOD_SHADER), headless=None)
    result = ShaderCheck(ops).run()
    assert result["success"] and result["skipped"]
    assert result["data"]["render_compile"] == "skipped"
    assert result["data"]["shader_files"] == ["res://shaders/a.gdshader"]

    bad = SimpleNamespace(project_path=_project(tmp_path / "bad", "uniform float x : hint_bogus;"), headless=None)
    assert ShaderCheck(bad).run()["errors"] == ["res://shaders/a.gdshader:1: unknown uniform hint 'hint_bogus'"]


def test_a_project_without_shaders_fails(tmp_path, monkeypatch):
    monkeypatch.setattr(shader_check, "display_prefix", lambda: [])
    project = tmp_path / "godot"
    project.mkdir()
    result = ShaderCheck(SimpleNamespace(project_path=project, headless=None)).run()
    assert not result["success"]
    assert result["errors"] == [f"No .gdshader files found under {project}"]
    assert result["data"]["render_compile"] == "skipped"


def test_with_a_display_every_shader_goes_through_the_compile_probe(tmp_path, monkeypatch):
    monkeypatch.setattr(shader_check, "display_prefix", lambda: ["xvfb-run", "-a"])
    calls = []

    def run_with_display(args, prefix, timeout):
        calls.append((args, prefix))
        return SimpleNamespace(returncode=0, stderr=REAL_FAILED_RUN.replace("count=2", "count=1"))

    headless = SimpleNamespace(run_with_display=run_with_display)
    ops = SimpleNamespace(project_path=_project(tmp_path, GOOD_SHADER), headless=headless)
    result = ShaderCheck(ops).run()

    args, prefix = calls[0]
    assert prefix == ["xvfb-run", "-a"]
    assert args[args.index("--script") + 1] == str(PROBE)
    assert args[args.index("--") + 1:] == ["res://shaders/a.gdshader"]
    assert "--headless" not in args
    assert not result["success"] and not result["skipped"]
    assert result["errors"] == ["res://shaders/fjord_sky.gdshader:13: Expected valid type hint after ':'."]


@pytest.mark.parametrize("outcome, code", [
    ({"success": True}, 0),
    ({"success": True, "skipped": True}, 3),
    ({"success": False, "skipped": True}, 1),
])
def test_the_cli_exit_code_tells_a_skip_from_a_pass(monkeypatch, capsys, outcome, code):
    monkeypatch.setattr(sys, "argv", ["godot_mcp_ops.py", "--no-mcp", "--godot-bin", "godot", "shader-check"])
    monkeypatch.setattr(godot_mcp_ops.GodotMCPOps, "run_shader_check", lambda self: dict(outcome))
    with pytest.raises(SystemExit) as exit_info:
        godot_mcp_ops.main()
    assert exit_info.value.code == code


def test_the_probe_is_a_scene_tree_script_that_reports_completion():
    source = PROBE.read_text()
    assert source.startswith("extends SceneTree")
    assert "SHADER_CHECK_DONE" in source and "CACHE_MODE_IGNORE" in source


def test_ci_hands_its_godot_to_every_godot_mcp_ops_step():
    ci = (REPO / "tools" / "ci.sh").read_text()
    invocations = [line for line in ci.splitlines() if '"$GODOT_MCP_OPS" --no-mcp' in line]
    assert len(invocations) == 3
    assert all('--godot-bin "$GODOT_BIN"' in line for line in invocations)
    assert "python_bin" not in ci, "every Python step uses $PYTHON"
    assert subprocess.run(["/bin/bash", "-n", str(REPO / "tools" / "ci.sh")]).returncode == 0
