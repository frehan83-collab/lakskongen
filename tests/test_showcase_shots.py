"""F2-23 first sight, teaser, showcase shots (farm-types spec §6.1 item 1,
§6.4, §7.2 F2-23 acceptance 1-6).

The behaviour runs in the UI flow suite (godot/tests/FirstSightChecks.gd,
godot/tests/TeaserChecks.gd); the frames' content checks run in
res://qa/scenes/ShowcaseCapture.tscn and their numbers are the evidence in
docs/run/evidence/f2_23_showcase_metrics.json. These checks hold the data,
the shots and the recorded numbers to the spec's bars:

- the sequences' timelines (godot/data/first_sight.json): at most 20 s,
  opening with a 3-5 s timelapse, a 2 s reduced-motion cross-fade, a 3 s
  repeat toast; the teaser in forste_sesong after day 150, never from 300;
- the teaser touches no engine verb: it reads the scenario, the day and the
  tutorial and holds the clock, nothing more, and its card offers no build;
- the six spec shots exist with their content checks;
- the recorded frames meet the bars: shot 33's hull share and water
  saturation, shot 36's OF1 hull-hue pixels and campus lightness step and
  the chip in rain, snow and storm, shot 30's whole campus at -35..-45 deg.
"""

import json
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
DATA = json.loads((GODOT / "data" / "first_sight.json").read_text(encoding="utf-8"))
SHOTS = {s["id"]: s for s in json.loads((GODOT / "qa" / "shots.json").read_text(encoding="utf-8"))["shots"]}
METRICS = json.loads((REPO / "docs" / "run" / "evidence" / "f2_23_showcase_metrics.json").read_text(encoding="utf-8"))["shots"]
TEASER_SRC = (GODOT / "scripts" / "visual" / "TeaserDirector.gd").read_text(encoding="utf-8")
STAGE_SRC = (GODOT / "scripts" / "visual" / "CinematicStage.gd").read_text(encoding="utf-8")
FIRST_SIGHT_SRC = (GODOT / "scripts" / "visual" / "FirstSightDirector.gd").read_text(encoding="utf-8")
PRESENTER_SRC = (GODOT / "scripts" / "visual" / "FarmFacilityVisualManager.gd").read_text(encoding="utf-8")


def test_the_first_sight_timelines_keep_the_spec_limits():
    """§6.4: one sequence of <= 20 s opening with a 3-5 s build timelapse; a
    repeat build gets a 3 s toast; reduced motion is a still with a 2 s
    cross-fade."""
    assert DATA["max_seconds"] <= 20.0
    assert DATA["toast_seconds"] == 3.0
    assert DATA["reduced_motion"]["crossfade_seconds"] == 2.0
    for name, family in DATA["families"].items():
        start, end = family["timelapse_seconds"]
        assert start == 0.0 and 3.0 <= end - start <= 5.0, name
        times = [k["t"] for k in family["keys"]]
        assert times == sorted(times) and times[0] == 0.0 and times[-1] <= DATA["max_seconds"], name
        card = family["end_card_seconds"]
        assert card[0] < card[1] <= DATA["max_seconds"], name
        # The last key stands where the camera's own framing of the unit does: no jump on the way back.
        assert family["keys"][-1]["eye"][0] == "final" and family["keys"][-1]["look"][0] == "focus", name
    assert DATA["families"]["land"]["mood"] == "blaatime"
    lights = DATA["families"]["land"]["lights_on_seconds"]
    assert DATA["families"]["land"]["timelapse_seconds"][1] <= lights[0] < lights[1] <= DATA["max_seconds"]


def test_the_teaser_plays_in_the_first_season_after_day_150_and_before_300():
    teaser = DATA["teaser"]
    assert teaser["scenario"] == "forste_sesong"
    assert teaser["after_day"] == 150 and teaser["skip_from_day"] == 300
    assert teaser["plot"] == "hav_1"
    assert teaser["seconds"] <= 20.0
    assert teaser["card_seconds"][0] < teaser["card_seconds"][1] <= teaser["seconds"]
    assert teaser["reduced_motion"]["crossfade_seconds"] == 2.0
    span = teaser["tow"]["seconds"]
    assert span[0] == 0.0 and span[1] <= teaser["seconds"]
    path = teaser["tow"]["path"]
    # Beyond the mouth (the sill at z = -1300; the sea at -Z), ending nearer hav_1 (0, -5200) than it began.
    assert all(z < -2300.0 for _, z in path)
    near = [((x - 0.0) ** 2 + (z + 5200.0) ** 2) ** 0.5 for x, z in path]
    assert near[-1] < near[0]
    # The rule in code is the data's: the defaults match and no day is a literal elsewhere.
    assert 'int(cfg.get("after_day", 150))' in TEASER_SRC and 'int(cfg.get("skip_from_day", 300))' in TEASER_SRC
    assert 'str(tutorial.get("kind", "")) != "wait"' in TEASER_SRC


def _calls(src: str) -> set[str]:
    """Methods a script calls on the SimBridge node it looks up (`sim.<m>(`)."""
    return set(re.findall(r"\bsim\.(?:call\(\")?([a-z_]+)", src))


def test_the_teaser_reads_the_game_and_never_offers_a_build():
    """§6.1 item 1: presentation only; it never offers a build and never
    changes engine state (the state hash: TeaserChecks). Replaced by a
    stronger one at the wave4b int merge (RUN_LOG §7): the reads gain the
    game's generation (a new or loaded game is a new timeline), a pure read
    of the event bus's counter (test_facility_presenter's
    test_the_game_generation_read_is_the_event_bus_counter), asked for by
    has_method only; and both cinematics wait for an open card, a menu or
    each other (CinematicStage.blocker)."""
    assert _calls(TEASER_SRC) == {"get_active_scenario_id", "get_tutorial_status", "get_game_generation",
                                  "has_method", "has_signal", "day_advanced"}
    assert re.findall(r'\bsim\.has_method\("([a-z_]+)"\)', TEASER_SRC) == ["get_game_generation"]
    for src in (TEASER_SRC, FIRST_SIGHT_SRC):
        assert "stage().blocker()" in src and "_under_modal()" in src
    assert _calls(STAGE_SRC) == {"has_method", "hold_clock", "release_clock"}
    # No other way into the engine or the build flow.
    for src in (TEASER_SRC, STAGE_SRC):
        assert "/root/GameBridge" not in src and "BuildSiteDialog" not in src and "BuildFlow" not in src
    # Its card offers "Fortsett" only.
    assert "overlay.offer_save(false)" in TEASER_SRC
    assert "continue_requested.connect" in TEASER_SRC
    # It waits for a first sight and holds its own clock reason.
    assert 'const CLOCK_HOLD := "teaser"' in TEASER_SRC
    assert "manager.first_sight.playing" in TEASER_SRC
    # Both cinematics borrow the world through the one stage.
    assert "CinematicStage.new(manager, CLOCK_HOLD)" in TEASER_SRC and "CinematicStage.new(manager, CLOCK_HOLD)" in FIRST_SIGHT_SRC
    assert "teaser = TeaserDirector.new()" in PRESENTER_SRC


SPEC_SHOTS = {
    "30_land_campus_day": {"campus", "orange"},
    "31_land_dusk": {"campus", "lights_ab", "sky"},
    "32_land_construction": {"campus"},
    "33_offshore_of1_winter_sun": {"of1", "chip"},
    "35_offshore_storm": {"of1", "storm", "chip"},
    "36_home_overcast_landmarks": {"landmarks", "chip"},
}


def test_the_spec_shots_exist_with_their_content_checks():
    for sid, checks in SPEC_SHOTS.items():
        shot = SHOTS[sid]
        assert shot["scene"] == "res://qa/scenes/ShowcaseCapture.tscn", sid
        assert checks <= set(shot["checks"]), sid
    for weather in ("rain", "snow", "storm"):
        shot = SHOTS[f"36_home_{weather}_landmarks"]
        assert shot["weather"] == weather and "chip" in shot["checks"] and shot["camera"]["mode"] == "home"
    assert SHOTS["36_home_overcast_landmarks"]["weather"] == "overcast"
    assert SHOTS["33_offshore_of1_winter_sun"]["camera"] == {"mode": "target", "target": "unit:offshore"}
    assert SHOTS["30_land_campus_day"]["camera"] == {"mode": "target", "target": "unit:land"}
    # Every showcase shot was captured into the evidence.
    showcase = [sid for sid, s in SHOTS.items() if s.get("scene") == "res://qa/scenes/ShowcaseCapture.tscn"]
    assert set(showcase) <= set(METRICS), sorted(set(showcase) - set(METRICS))


def test_shot_33_meets_the_hull_and_water_bars():
    """Acceptance 4: yellow-hull pixel fraction >= 1.5 % and mean water saturation <= 0.35."""
    of1 = METRICS["33_offshore_of1_winter_sun"]["of1"]
    assert of1["frame_share"] >= 0.015
    assert of1["water"]["mean_saturation"] <= 0.35
    assert METRICS["33_offshore_of1_winter_sun"]["environment"]["sun_elevation_deg"] <= 8.0


def test_shot_36_meets_the_landmark_bars_and_shows_the_chip_in_bad_weather():
    """Acceptance 5: OF1 hull-hue pixels >= 200 and campus silhouette delta L
    >= 15 against the strandflat at the home view in overcast; the status
    chip visible in the rain, snow and storm variants."""
    marks = METRICS["36_home_overcast_landmarks"]["landmarks"]
    assert marks["of1"]["hull_hue_pixels"] >= 200
    assert marks["campus"]["delta_l"] >= 15.0
    assert marks["campus"]["pixels"] > 0 and marks["campus"]["strandflat_pixels"] > 0
    for weather in ("rain", "snow", "storm"):
        chip = METRICS[f"36_home_{weather}_landmarks"]["chip"]
        assert chip["visible"] and chip["on_screen"], weather


def test_the_land_and_storm_shots_meet_their_reads():
    """F2-21 accept 2 at shot 30 (the land framing shows the full campus at
    -35..-45 deg); shot 31 lit at dusk under a blue sky; shot 35 the drawn
    open sea at storm Hs with its swell and the chip's square status."""
    campus = METRICS["30_land_campus_day"]["campus"]
    assert campus["corners_in_frame"] == campus["corners"] == 8
    assert -45.0 <= campus["pitch_deg"] <= -35.0
    assert METRICS["30_land_campus_day"]["orange"]["pixels"] > 0
    dusk = METRICS["31_land_dusk"]
    assert dusk["lights_ab"]["lit_pixels_added"] > 0 and dusk["environment"]["light_level"] >= 0.9
    assert 0.55 <= dusk["sky"]["hue"] <= 0.75
    storm = METRICS["35_offshore_storm"]
    assert storm["storm"]["exposed_hs_m"] >= 4.0 and storm["storm"]["swell_hs_m"] > 0.0
    assert storm["chip"]["visible"] and storm["chip"]["shape"] == 2


def test_the_sequences_frames_keep_their_limits():
    """Acceptance 1 and 3 as captured: the sequences last <= 20 s; the
    reduced-motion still shows its card; the teaser's tow is on screen in its
    hull's yellow and its card offers "Fortsett" only."""
    for sid, rec in METRICS.items():
        if sid.startswith("34_first_sight"):
            fs = rec["first_sight"]
            assert fs["duration_s"] <= 20.0, sid
            if fs["reduced"]:
                assert fs["duration_s"] <= 8.0 and fs["card"] >= 0.99 and fs["still"] <= 0.001, sid
        if sid.startswith("34_teaser"):
            teaser = rec["teaser"]
            assert teaser["duration_s"] <= 20.0 and teaser["buttons"] == ["ContinueButton"], sid
            assert teaser["hull"]["on_screen"] and teaser["hull"]["hull_hue_pixels"] >= 200, sid
