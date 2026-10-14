"""P0-04 performance smoke test and the G5-01 quality tiers (groundwork).

- tools/perf_report.py: per-segment percentiles over repeats, the paired
  A/B ratios, and a gate that is warn-only until the Phase 1 exit;
- the harness (godot/scripts/tools/PerfHarness.gd) and its wrapper
  (tools/perf_smoke.sh) measure FARM, FJORD, COAST and an opaque panel at
  2560x1440 with vsync off, and tools/ci.sh runs them as a warn-only step;
- the quality tiers (RenderQuality.gd): four tiers with one knob set, High
  the default and keeping MSAA 4x + SMAA (D12), Ultra the full-cost
  reference, cheaper tiers never costlier than the ones above them; only
  RenderQuality and the EnvironmentDirector write the render settings;
- the sky shader draws its clouds in a reduced-resolution pass when the tier
  asks and takes the engine's sky fog back out of it;
- the settings menu offers the same tiers;
- the baseline and the High measurement are recorded in docs/run/evidence.

Pure static analysis plus the report script: needs no engine.
"""

import importlib.util
import json
import os
import re
import stat
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
HARNESS = GODOT / "scripts/tools/PerfHarness.gd"
QUALITY = GODOT / "scripts/environment/RenderQuality.gd"
DIRECTOR = GODOT / "scripts/environment/EnvironmentDirector.gd"
SKY = GODOT / "shaders/sky_shader.gdshader"
EVIDENCE = REPO / "docs/run/evidence"


def _load_report_module():
    spec = importlib.util.spec_from_file_location("perf_report", REPO / "tools/perf_report.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


perf_report = _load_report_module()


def _run(segment, variant, repeat, p50, p95, block=-1, p10=None, p99=None):
    return {
        "segment": segment, "variant": variant, "repeat": repeat, "block": block, "frames": 100,
        "frame_ms": {"p10": p10 if p10 is not None else p50 * 0.95, "p50": p50, "p95": p95,
                     "p99": p99 if p99 is not None else p95 * 1.05},
        "draw_calls": 200, "shadow_draw_calls": 50, "script_phase_ms": {"p50": 0.8}, "vram_mb": 1200.0,
    }


def _report(runs):
    return {"label": "t", "resolution": [2560, 1440], "adapter": "test", "godot": "4.7.2", "runs": runs,
            "boot": {"terrain_ready_frame": 4, "max_frame_after_first_10": 12.0,
                     "spikes_over_50_ms": [{"frame": 0, "ms": 120.0}]}}


# ── tools/perf_report.py ──────────────────────────────────────────────────────

def test_summary_takes_the_median_over_repeats():
    report = _report([
        _run("fjord", "high", 0, 15.0, 16.0),
        _run("fjord", "high", 1, 40.0, 60.0),  # one disturbed block
        _run("fjord", "high", 2, 15.5, 16.4),
    ])
    s = perf_report.summarize(report)["fjord"]["high"]
    assert s["p50"] == 15.5 and s["p95"] == 16.4 and s["repeats"] == 3 and s["frames"] == 300


def test_the_gate_judges_p95_of_every_gated_segment():
    runs = [_run("farm", "high", 0, 14.0, 16.0), _run("fjord", "high", 0, 15.0, 17.5)]
    summary = perf_report.summarize(_report(runs))
    findings = perf_report.gate(summary, "high")
    assert any(f.startswith("fjord: p95 17.50") for f in findings)
    assert "coast: not measured" in findings
    assert not any(f.startswith("farm") for f in findings)
    # On budget everywhere: no findings.
    ok = [_run(seg, "high", 0, 14.0, 16.5) for seg in ("farm", "fjord", "coast")]
    assert perf_report.gate(perf_report.summarize(_report(ok)), "high") == []


def test_the_gate_is_warn_only_unless_strict(tmp_path, capsys, monkeypatch):
    monkeypatch.delenv("PERF_GATE_STRICT", raising=False)
    path = tmp_path / "perf.json"
    path.write_text(json.dumps(_report([_run(seg, "high", 0, 20.0, 25.0) for seg in ("farm", "fjord", "coast")])))
    assert perf_report.main([str(path)]) == 0
    assert "PERF_GATE WARN fjord: p95 25.00 ms > 16.7 ms" in capsys.readouterr().out
    assert perf_report.main([str(path), "--strict"]) == 1
    assert "PERF_GATE FAIL" in capsys.readouterr().out
    md = tmp_path / "perf.md"
    perf_report.main([str(path), "--markdown", str(md)])
    assert "| fjord | high |" in md.read_text()


def test_paired_ratios_compare_each_variant_with_the_block_before_it():
    runs = []
    for r in range(3):
        load = 1.0 + r  # a background load that changes between repeats
        runs += [
            _run("fjord", "ultra", r, 30.0 * load, 31.0 * load, block=0, p10=29.0 * load),
            _run("fjord", "high", r, 15.0 * load, 16.0 * load, block=1, p10=14.5 * load),
            _run("fjord", "ultra", r, 30.0 * load, 31.0 * load, block=2, p10=29.0 * load),
            _run("fjord", "ultra,ssao=false", r, 24.0 * load, 25.0 * load, block=3, p10=23.2 * load),
        ]
    pairs = perf_report.paired(_report(runs))["fjord"]
    assert pairs["high"]["ratio_p50"] == 0.5 and pairs["high"]["pairs"] == 3
    assert pairs["ultra,ssao=false"]["ratio_p50"] == 0.8
    assert "ultra" not in pairs


# ── The harness, its wrapper and the CI step ─────────────────────────────────

def test_the_harness_measures_every_zoom_and_the_panel_without_vsync():
    code = HARNESS.read_text(encoding="utf-8")
    assert re.search(r'SEGMENT_NAMES := \["farm", "fjord", "coast", "panel"\]', code)
    assert "VSYNC_DISABLED" in code and "Engine.max_fps = 0" in code
    for metric in ("p50", "p95", "p99", "gpu_ms", "draw_calls", "shadow_draw_calls", "vram_mb", "RENDER_INFO_TYPE_SHADOW"):
        assert metric in code, metric
    assert '"finance" if open else "fjord"' in code, "the panel segment opens an opaque panel (shot 06)"
    assert "PERF_RESULT" in code and "PERF_SEGMENT" in code
    assert (GODOT / "scenes/tools/PerfHarness.tscn").read_text(encoding="utf-8").count("PerfHarness.gd") == 1


def test_the_wrapper_runs_the_harness_at_1440p_and_reports():
    script = REPO / "tools/perf_smoke.sh"
    text = script.read_text(encoding="utf-8")
    assert os.stat(script).st_mode & stat.S_IXUSR
    assert 'RESOLUTION="${PERF_RESOLUTION:-2560x1440}"' in text
    assert 'SECONDS_PER_SEGMENT="${PERF_SECONDS:-10}"' in text
    assert "res://scenes/tools/PerfHarness.tscn" in text and "--headless" not in text
    assert "tools/perf_report.py" in text and "PERF_GATE_STRICT" in text


def test_ci_runs_the_smoke_test_warn_only_as_its_last_step():
    ci = (REPO / "tools/ci.sh").read_text(encoding="utf-8")
    steps = re.findall(r"^run_step .*?(step_\w+)\s*$", ci, re.M)
    assert steps[-1] == "step_perf_smoke"
    body = ci[ci.index("step_perf_smoke() {"):]
    body = body[: body.index("\n}\n")]
    # Warn-only: never skip_step (CI_REQUIRE_ALL=1 would fail a skip), WARN
    # when over budget or off the Mac runner, FAIL only in strict mode.
    assert "skip_step" not in body
    assert body.count('STEP_STATUS="WARN"') == 2
    assert 'STEP_STATUS="FAIL"' in body and "rc == 1" in body


# ── Quality tiers ─────────────────────────────────────────────────────────────

def _presets():
    text = QUALITY.read_text(encoding="utf-8")
    block = text[text.index("const PRESETS := {"):]
    block = block[: block.index("\n}\n")]
    tiers = {}
    for name, body in re.findall(r'"(low|medium|high|ultra)": \{(.*?)\n\t\}', block, re.S):
        knobs = dict(re.findall(r'"(\w+)": ([^,\n]+)', body))
        tiers[name] = {k: v.strip() for k, v in knobs.items()}
    return tiers


def _num(value):
    consts = {
        "Viewport.MSAA_DISABLED": 0, "Viewport.MSAA_2X": 1, "Viewport.MSAA_4X": 2, "Viewport.MSAA_8X": 3,
        "RenderingServer.SHADOW_QUALITY_HARD": 0, "RenderingServer.SHADOW_QUALITY_SOFT_VERY_LOW": 1,
        "RenderingServer.SHADOW_QUALITY_SOFT_LOW": 2, "RenderingServer.SHADOW_QUALITY_SOFT_MEDIUM": 3,
        "Sky.RADIANCE_SIZE_64": 1, "Sky.RADIANCE_SIZE_128": 2, "Sky.RADIANCE_SIZE_256": 3,
        "true": 1, "false": 0,
    }
    return consts[value] if value in consts else float(value)


def test_four_tiers_share_one_knob_set_and_high_is_the_default():
    tiers = _presets()
    assert list(tiers) == ["low", "medium", "high", "ultra"]
    keys = {name: set(knobs) for name, knobs in tiers.items()}
    assert len({frozenset(k) for k in keys.values()}) == 1, keys
    text = QUALITY.read_text(encoding="utf-8")
    assert "const DEFAULT_TIER := Tier.HIGH" in text
    assert 'const NAMES: Array[String] = ["low", "medium", "high", "ultra"]' in text


def test_high_keeps_msaa_4x_and_smaa_and_ultra_is_the_full_cost_reference():
    tiers = _presets()
    high, ultra = tiers["high"], tiers["ultra"]
    # D12: MSAA 4x + SMAA. The pen net resolves its threads with alpha-to-coverage.
    assert high["msaa"] == "Viewport.MSAA_4X" and high["screen_aa"] == "Viewport.SCREEN_SPACE_AA_SMAA"
    assert ultra["msaa"] == "Viewport.MSAA_4X" and ultra["screen_aa"] == "Viewport.SCREEN_SPACE_AA_SMAA"
    assert ultra["scale"] == "1.0" and ultra["ssao"] == "true" and ultra["cloud_pass"] == "0"
    # Behind an opaque panel every tier renders at half scale and pauses the sky (G1-06).
    for name, knobs in tiers.items():
        assert knobs["hidden_scale"] == "0.5" and knobs["hidden_pause"] == "true", name


def test_a_cheaper_tier_never_costs_more_than_the_one_above_it():
    tiers = _presets()
    order = ["low", "medium", "high", "ultra"]
    for knob in ("msaa", "scale", "shadow_atlas", "shadow_splits", "shadow_distance", "soft_shadows",
                 "cloud_steps", "cloud_octaves", "radiance_size", "ssao", "dof"):
        values = [_num(tiers[t][knob]) for t in order]
        assert values == sorted(values), f"{knob}: {values} must not fall from Low to Ultra"
    # Clouds move to cheaper passes going down (0 full, 1 half, 2 quarter).
    passes = [int(tiers[t]["cloud_pass"]) for t in order]
    assert passes == sorted(passes, reverse=True)
    # No SDFGI and no TAA in any tier (Metal #120496; TAA smears the vertex-animated water).
    text = QUALITY.read_text(encoding="utf-8")
    assert "sdfgi" not in text.lower() and "viewport.use_taa = false" in text


def test_only_the_quality_tiers_write_the_render_settings():
    patterns = [
        r"\.(msaa_3d|screen_space_aa|scaling_3d_scale|scaling_3d_mode|use_taa|radiance_size|directional_shadow_mode)\s*=(?!=)",
        r"directional_shadow_atlas_set_size|environment_set_ssao_quality|camera_attributes_set_dof_blur_quality",
        r"camera_attributes_set_dof_blur_bokeh_shape|directional_soft_shadow_filter_set_quality",
        r"\.(ssao_enabled|glow_enabled)\s*=(?!=)",
    ]
    compiled = [re.compile(p) for p in patterns]
    allowed = {"godot/scripts/environment/RenderQuality.gd", "godot/scripts/environment/EnvironmentDirector.gd"}
    writers = {}
    for path in sorted(GODOT.rglob("*")):
        rel = path.relative_to(REPO).as_posix()
        if path.suffix not in (".gd", ".cs") or "/addons/" in rel or "/.godot/" in rel:
            continue
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            code = line.split("#", 1)[0] if rel.endswith(".gd") else line.split("//", 1)[0]
            if any(c.search(code) for c in compiled):
                writers.setdefault(rel, []).append(lineno)
    assert set(writers) <= allowed, f"render settings written outside the quality tiers: {writers}"
    assert "godot/scripts/environment/RenderQuality.gd" in writers


def test_the_settings_menu_offers_the_same_tiers():
    menu = (GODOT / "scripts/ui/SettingsMenu.gd").read_text(encoding="utf-8")
    assert 'const QUALITY_TIERS: Array[String] = ["low", "medium", "high", "ultra"]' in menu
    assert '"quality": "high"' in menu and "RenderQuality.DIRECTOR_GROUP" in menu
    scene = (GODOT / "scenes/ui/SettingsMenu.tscn").read_text(encoding="utf-8")
    assert '[node name="QualityOption" type="OptionButton"' in scene
    csv = (GODOT / "translations/translations.csv").read_text(encoding="utf-8")
    for key, nb in (("UI_SETTINGS_QUALITY_LABEL", "Grafikkvalitet"), ("UI_SETTINGS_QUALITY_LOW", "Lav"),
                    ("UI_SETTINGS_QUALITY_MEDIUM", "Middels"), ("UI_SETTINGS_QUALITY_HIGH", "Høy"),
                    ("UI_SETTINGS_QUALITY_ULTRA", "Ultra")):
        assert re.search(rf"^{key},[^,]+,{nb}$", csv, re.M), key


# ── The sky's reduced-resolution clouds ───────────────────────────────────────

def _code(path):
    return "\n".join(line.split("//", 1)[0] for line in path.read_text(encoding="utf-8").splitlines())


def test_the_sky_can_draw_its_clouds_at_half_or_quarter_resolution():
    sky = _code(SKY)
    assert re.search(r"render_mode\s+use_half_res_pass,\s*use_quarter_res_pass;", sky)
    for name in ("AT_HALF_RES_PASS", "AT_QUARTER_RES_PASS", "HALF_RES_COLOR", "QUARTER_RES_COLOR"):
        assert name in sky, name
    for uniform in ("cloud_pass", "cloud_steps", "cloud_octaves", "sky_zenith", "sky_fog_k", "sky_fog_scatter"):
        assert re.search(rf"uniform \w+ {uniform}\b", sky), uniform
    # The zenith is one value per frame, not a march per pixel.
    assert "atmosphere(up_dir" not in sky
    # The engine fogs every sky pass: the composite takes that first fogging back out.
    assert re.search(r"cl\.rgb - sky_fog_k \* haze_color - sky_fog_scatter \* scatter", sky)


def test_the_director_writes_the_sky_quality_and_fog_terms():
    text = DIRECTOR.read_text(encoding="utf-8")
    for uniform in ("sky_zenith", "cloud_pass", "cloud_steps", "cloud_octaves", "sky_fog_k", "sky_fog_scatter"):
        assert re.search(rf'set_shader_parameter\("{uniform}"', text), uniform
    # The undo terms come from the same constants _apply_fog writes.
    terms = text[text.index("static func sky_fog_terms"):]
    terms = terms[: terms.index("\n\n")]
    assert "SKY_AFFECT" in terms and "AERIAL_PERSPECTIVE" in terms and "SUN_SCATTER" in terms


# ── Evidence ──────────────────────────────────────────────────────────────────

def test_the_baseline_and_high_are_recorded_in_the_evidence():
    md = (EVIDENCE / "p0_04_perf.md").read_text(encoding="utf-8")
    for heading in ("Baseline", "High", "Method", "Boot"):
        assert heading in md, heading
    for name in ("perf_baseline.json", "perf_tiers.json"):
        data = json.loads((EVIDENCE / "perf" / name).read_text(encoding="utf-8"))
        assert data["resolution"] == [2560, 1440], name
        segments = {r["segment"] for r in data["runs"]}
        assert {"farm", "fjord", "coast"} <= segments, name
