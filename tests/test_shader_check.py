"""Tests for the CI shader gate (review F958).

The old gate only load()ed each shader under --headless, where the dummy
renderer parses nothing, so it passed shaders that do not compile. These pin
the two parts that replaced it: the static hint check and the parsing of a
real-renderer compile run. None of them needs Godot.
"""

import subprocess
import sys
import time
from pathlib import Path
from types import SimpleNamespace

import pytest

REPO = Path(__file__).parent.parent
sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "godot-mcp-ops"))

import godot_mcp_ops  # noqa: E402
import shader_check  # noqa: E402
from headless_runner import KILL_GRACE_S, HeadlessRunner  # noqa: E402
from shader_check import PROBE, ShaderCheck, parse_compile_log  # noqa: E402
from shader_check import STOPPED_EARLY, compile_progress, describe_exit  # noqa: E402
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


# ─── CI run 38616565780: the compile run on GitHub's Ubuntu runner ──────────
#
# Ubuntu 24.04's xvfb-run runs its command as `"$@" 2>&1`, so every marker the
# probe printed with printerr() reached stdout, and the check (which parsed
# stderr only) reported "the compile run stopped before it reached every
# shader" after a run that had in fact drawn all 18 and exited 0. The tests
# below run real processes: a stand-in Godot that speaks the probe's protocol
# on stderr, behind an xvfb-run with the same redirection as Ubuntu's.

FAKE_GODOT = """\
#!/bin/sh
# Stand-in for Godot running shader_compile_probe.gd: banner and renderer on
# stdout, the probe's markers on stderr. A shader named *crash* kills it.
while [ "$#" -gt 0 ] && [ "$1" != "--" ]; do shift; done
[ "$#" -gt 0 ] && shift
echo "Godot Engine v4.7.2.stable.mono.official - https://godotengine.org"
echo "Vulkan 1.4.318 - Forward+ - Using Device #0: Mesa - llvmpipe (LLVM 20.1.2, 256 bits)"
for shader in "$@"; do
  echo "SHADER_CHECK_BEGIN $shader" >&2
  case "$shader" in *crash*) kill -KILL $$ ;; esac
  echo "SHADER_CHECK_END $shader" >&2
done
echo "SHADER_CHECK_DONE count=$#" >&2
"""

# The stream handling of /usr/bin/xvfb-run on ubuntu-24.04 (xvfb 21.1.12).
UBUNTU_XVFB_RUN = """\
#!/bin/sh
[ "$1" = "-a" ] && shift
"$@" 2>&1
"""


def _script(path, text):
    path.write_text(text)
    path.chmod(0o755)
    return str(path)


def _real_compile(tmp_path, monkeypatch, shader_names):
    project = tmp_path / "godot"
    (project / "shaders").mkdir(parents=True)
    for name in shader_names:
        (project / "shaders" / name).write_text(GOOD_SHADER)
    godot = _script(tmp_path / "godot-fake", FAKE_GODOT)
    xvfb_run = _script(tmp_path / "xvfb-run", UBUNTU_XVFB_RUN)
    monkeypatch.setattr(shader_check, "display_prefix", lambda: [xvfb_run, "-a"])
    ops = SimpleNamespace(project_path=project, headless=HeadlessRunner(godot, project))
    return ShaderCheck(ops).run()


@pytest.mark.skipif(sys.platform == "win32", reason="POSIX shell stand-ins")
def test_markers_that_xvfb_run_moves_to_stdout_are_still_read(tmp_path, monkeypatch):
    result = _real_compile(tmp_path, monkeypatch, ["a.gdshader", "b.gdshader"])
    assert result["errors"] == []
    assert result["success"] and not result["skipped"]
    data = result["data"]
    assert data["renderer"] == "Vulkan 1.4.318 - Forward+ - Using Device #0: Mesa - llvmpipe (LLVM 20.1.2, 256 bits)"
    assert data["compile_runs"] == [{
        "shaders": 2, "reached": "res://shaders/b.gdshader", "finished": True,
        "exit": "exit code 0", "seconds": data["compile_runs"][0]["seconds"],
    }]
    assert "output_tail" not in data


@pytest.mark.skipif(sys.platform == "win32", reason="POSIX shell stand-ins")
def test_a_crash_names_its_shader_and_the_rest_are_still_compiled(tmp_path, monkeypatch):
    result = _real_compile(tmp_path, monkeypatch, ["a.gdshader", "b_crash.gdshader", "c.gdshader"])
    assert not result["success"]
    assert result["errors"] == [
        "res://shaders/b_crash.gdshader: the compile run ended while it was on screen, with exit code 137, "
        "i.e. killed by SIGKILL (as a shell reports it) (last output in data.output_tail)"
    ]
    runs = result["data"]["compile_runs"]
    assert [(r["shaders"], r["reached"], r["finished"]) for r in runs] == [
        (3, "res://shaders/b_crash.gdshader", False),
        (1, "res://shaders/c.gdshader", True),
    ]
    # The crashed run's last lines (the shell may add its own "Killed" line).
    tail = result["data"]["output_tail"]
    assert "SHADER_CHECK_BEGIN res://shaders/b_crash.gdshader" in tail
    assert "SHADER_CHECK_END res://shaders/b_crash.gdshader" not in tail


def _fake_runs(monkeypatch, *outputs):
    """ShaderCheck ops whose compile runs return `outputs` (stdout, code) in turn."""
    monkeypatch.setattr(shader_check, "display_prefix", lambda: [])
    calls = []

    def run_with_display(args, prefix, timeout):
        calls.append(args[args.index("--") + 1:])
        stdout, code = outputs[len(calls) - 1]
        return SimpleNamespace(returncode=code, stdout=stdout, stderr=None)

    return calls, SimpleNamespace(run_with_display=run_with_display)


def test_a_run_that_dies_before_the_first_shader_reports_how_it_ended(tmp_path, monkeypatch):
    log = "Godot Engine v4.7.2\nERROR: Unable to create DisplayServer, all display drivers failed.\n"
    calls, headless = _fake_runs(monkeypatch, (log, 255))
    result = ShaderCheck(SimpleNamespace(project_path=_project(tmp_path, GOOD_SHADER), headless=headless)).run()
    assert result["errors"] == [
        f"{STOPPED_EARLY}: it ended before the first shader with exit code 255 (last output in data.output_tail)"
    ]
    assert result["data"]["output_tail"][-1] == "ERROR: Unable to create DisplayServer, all display drivers failed."
    assert len(calls) == 1


def test_a_timeout_names_the_shader_on_screen_and_moves_on(tmp_path, monkeypatch):
    project = _project(tmp_path, GOOD_SHADER)
    (project / "shaders" / "b.gdshader").write_text(GOOD_SHADER)
    hung = "SHADER_CHECK_BEGIN res://shaders/a.gdshader\n"
    rest = ("SHADER_CHECK_BEGIN res://shaders/b.gdshader\nSHADER_CHECK_END res://shaders/b.gdshader\n"
            "SHADER_CHECK_DONE count=1\n")
    calls, headless = _fake_runs(monkeypatch, (hung, None), (rest, 0))
    result = ShaderCheck(SimpleNamespace(project_path=project, headless=headless)).run()
    assert calls == [["res://shaders/a.gdshader", "res://shaders/b.gdshader"], ["res://shaders/b.gdshader"]]
    assert len(result["errors"]) == 1
    assert result["errors"][0].startswith(
        "res://shaders/a.gdshader: the compile run ended while it was on screen, with timed out after ")


def test_godot_exiting_non_zero_after_every_shader_fails_with_its_exit(tmp_path, monkeypatch):
    log = ("SHADER_CHECK_BEGIN res://shaders/a.gdshader\nSHADER_CHECK_END res://shaders/a.gdshader\n"
           "SHADER_CHECK_DONE count=1\n")
    _, headless = _fake_runs(monkeypatch, (log, -6))
    result = ShaderCheck(SimpleNamespace(project_path=_project(tmp_path, GOOD_SHADER), headless=headless)).run()
    assert result["errors"] == [
        "Godot ended the compile run with killed by SIGABRT after it reached every shader "
        "(last output in data.output_tail)"
    ]


def test_a_fallback_to_another_renderer_fails_the_check(tmp_path, monkeypatch):
    """Vulkan missing on the runner: Godot drops to OpenGL, whose compiler is
    not the one the Forward+ game uses. That must not pass quietly."""
    fallback = "OpenGL API 4.5 (Core Profile) Mesa 25.2.8 - Compatibility - Using Device: Mesa - llvmpipe"
    log = (f"{fallback}\nSHADER_CHECK_BEGIN res://shaders/a.gdshader\nSHADER_CHECK_END res://shaders/a.gdshader\n"
           "SHADER_CHECK_DONE count=1\n")
    _, headless = _fake_runs(monkeypatch, (log, 0))
    result = ShaderCheck(SimpleNamespace(project_path=_project(tmp_path, GOOD_SHADER), headless=headless)).run()
    assert result["data"]["renderer"] == fallback
    assert result["errors"] == [
        f"Godot drew with the Compatibility renderer ('{fallback}'), not the project's Forward+: "
        "the shaders were not compiled the way the game compiles them"
    ]


def test_a_shader_the_window_never_draws_fails():
    log = (
        "SHADER_CHECK_BEGIN res://a.gdshader\n"
        "SHADER_CHECK_NOT_DRAWN res://a.gdshader only 0 of 8 frames were drawn in 2000 process ticks\n"
        "SHADER_CHECK_END res://a.gdshader\n"
        "SHADER_CHECK_DONE count=1\n"
    )
    assert parse_compile_log(log, 1) == [
        "res://a.gdshader: never drawn (only 0 of 8 frames were drawn in 2000 process ticks)"
    ]


def test_compile_progress_tells_where_a_run_stopped():
    log = "SHADER_CHECK_BEGIN res://a\nSHADER_CHECK_END res://a\nSHADER_CHECK_BEGIN res://b\n"
    assert compile_progress(log) == (["res://a", "res://b"], "res://b", False)
    assert compile_progress(log + "SHADER_CHECK_END res://b\nSHADER_CHECK_DONE count=2\n") == (
        ["res://a", "res://b"], None, True)


@pytest.mark.parametrize("code, text", [
    (0, "exit code 0"),
    (1, "exit code 1"),
    (-11, "killed by SIGSEGV"),
    (139, "exit code 139, i.e. killed by SIGSEGV (as a shell reports it)"),
    (None, "timed out after 300 s and was killed"),
])
def test_describe_exit(code, text):
    assert describe_exit(code, 300) == text


def test_the_missing_mcp_runtime_autoload_is_a_warning_not_an_error():
    log = ("ERROR: Failed to instantiate an autoload, can't load from path: "
           "res://addons/godot_mcp_toolkit/runtime/mcp_runtime_server.gd.\n")
    check = ShaderCheck(SimpleNamespace(project_path=REPO / "godot"))
    assert check._autoload_warnings(log) == [
        "Autoload 'res://addons/godot_mcp_toolkit/runtime/mcp_runtime_server.gd' is optional local tooling "
        "that is not installed (git-ignored); the compile run went on without it"
    ]
    assert parse_compile_log(log + "SHADER_CHECK_DONE count=0\n", 0) == []


@pytest.mark.skipif(sys.platform == "win32", reason="POSIX process groups")
def test_a_timed_out_run_is_killed_with_its_children_and_keeps_its_output(tmp_path):
    godot = _script(tmp_path / "godot-hangs", "#!/bin/sh\necho started\necho on stderr >&2\nsleep 60 &\nwait\n")
    started = time.monotonic()
    result = HeadlessRunner(godot, tmp_path).run_with_display([], [], timeout=1)
    assert time.monotonic() - started < KILL_GRACE_S + 5
    assert result.returncode is None
    assert result.stdout.splitlines() == ["started", "on stderr"]


def test_the_probe_counts_drawn_frames_and_reports_a_shader_never_drawn():
    source = PROBE.read_text()
    assert "frame_post_draw" in source and "SHADER_CHECK_NOT_DRAWN" in source
