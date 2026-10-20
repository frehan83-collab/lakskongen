"""The CI audio check (roadmap Au2-01, the audio floor).

godot/assets/audio/audio_manifest.json (written by tools/audio/gen_audio.py)
lists every audio file the game ships. Without decoding anything, this
checks that:
  * no audio file ships unlisted and no listed file is missing;
  * each file is the one the manifest measured (SHA-256), with the recorded
    duration and channels (read from the Ogg and WAV headers);
  * every ambience loop runs at least 30 s, and the five the floor asks for
    exist (fjord wind, water lap, gulls, the barge engine, rain);
  * the recorded levels sit in their bands (loops -24 +-1 LUFS integrated,
    true peak <= -1 dBTP; verb cues -19 +-1 LUFS momentary max);
  * every licence is recorded in AUDIO_SOURCES.md, THIRD_PARTY.md and
    credits.json, and an original file names a generator that exists;
  * every path the game's catalogue (MusicLibrary.gd) names exists and is
    listed, and the ambience mixer's layers and the verb cues resolve.
The runtime half (every Button clicks, the verbs play their cue, the bed
follows the weather) is in godot/tests/UiFlowTests.gd.
"""

from __future__ import annotations

import hashlib
import json
import re
import struct
import wave
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parent.parent
GODOT = REPO / "godot"
AUDIO = GODOT / "assets" / "audio"
MANIFEST = json.loads((AUDIO / "audio_manifest.json").read_text(encoding="utf-8"))
ROWS = {row["path"]: row for row in MANIFEST["files"]}
AUDIO_EXT = {".ogg", ".wav", ".mp3", ".flac", ".opus"}
ORIGINAL = "Original work of the project"
LOOP_MIN_S = 30.0
# The five ambiences Au2-01 asks for, by file.
REQUIRED_AMBIENCE = {
    "fjord wind": ["ambient/fjord_wind_light.ogg", "ambient/fjord_wind_strong.ogg"],
    "water lap": ["ambient/water_lap.ogg"],
    "gulls": ["ambient/gulls.ogg"],
    "barge engine": ["ambient/feed_barge.ogg"],
    "rain": ["ambient/rain_light.ogg", "ambient/rain_heavy.ogg"],
}
VERB_CUES = ["stock", "feed_plan", "delouse", "harvest", "auction", "storm"]


def _shipped() -> set[str]:
    return {p.relative_to(AUDIO).as_posix() for p in AUDIO.rglob("*") if p.suffix.lower() in AUDIO_EXT}


def _ogg_header(path: Path) -> tuple[float, int]:
    data = path.read_bytes()
    assert data[:4] == b"OggS", f"{path.name}: not an Ogg file"
    i = data.find(b"\x01vorbis")
    assert i >= 0, f"{path.name}: no Vorbis identification header"
    channels, rate = data[i + 11], struct.unpack("<I", data[i + 12:i + 16])[0]
    last = data.rfind(b"OggS")
    granule = struct.unpack("<q", data[last + 6:last + 14])[0]
    return granule / rate, channels


def _header(path: Path) -> tuple[float, int]:
    if path.suffix == ".ogg":
        return _ogg_header(path)
    with wave.open(str(path), "rb") as w:
        return w.getnframes() / w.getframerate(), w.getnchannels()


def _third_party_rows() -> dict[str, str]:
    rows: dict[str, str] = {}
    header: list[str] = []
    for line in (REPO / "THIRD_PARTY.md").read_text(encoding="utf-8").splitlines():
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if not header:
            header = cells
            continue
        if set(cells[0]) <= {"-", ":"}:
            continue
        rows[cells[header.index("Component")]] = cells[header.index("Licence")]
    return rows


def _catalogue_paths() -> dict[str, dict[str, str]]:
    """MusicLibrary.gd's dictionaries: name -> {key: res path}."""
    text = (GODOT / "scripts" / "MusicLibrary.gd").read_text(encoding="utf-8")
    out = {}
    for name, body in re.findall(r"const (\w+): Dictionary = \{(.*?)\n\}", text, re.S):
        out[name] = dict(re.findall(r'"(\w+)":\s*"(res://[^"]+)"', body))
    return out


# ── Every file listed, every listed file there ───────────────────────────────

def test_no_audio_file_ships_unlisted_and_none_is_missing():
    shipped = _shipped()
    listed = set(ROWS)
    assert shipped - listed == set(), f"audio files the manifest does not list: {sorted(shipped - listed)}"
    assert listed - shipped == set(), f"listed audio files that are missing: {sorted(listed - shipped)}"


@pytest.mark.parametrize("rel", sorted(ROWS))
def test_each_file_is_the_one_measured(rel):
    row = ROWS[rel]
    path = AUDIO / rel
    assert hashlib.sha256(path.read_bytes()).hexdigest() == row["sha256"], f"{rel} changed since it was measured"
    duration, channels = _header(path)
    assert abs(duration - row["duration_s"]) < 0.01 and channels == row["channels"], rel


# ── Loops ─────────────────────────────────────────────────────────────────────

def test_every_ambience_file_is_a_loop_of_at_least_thirty_seconds():
    ambient = [rel for rel in ROWS if rel.startswith("ambient/")]
    assert ambient, "no ambience shipped"
    for rel in ambient:
        assert ROWS[rel]["kind"] == "loop", f"{rel}: an ambience file must be a loop"
    loops = [rel for rel, row in ROWS.items() if row["kind"] == "loop"]
    short = {rel: _header(AUDIO / rel)[0] for rel in loops if _header(AUDIO / rel)[0] < LOOP_MIN_S}
    assert short == {}, f"loops under {LOOP_MIN_S:.0f} s: {short}"


def test_the_five_ambiences_of_the_floor_exist():
    for what, files in REQUIRED_AMBIENCE.items():
        for rel in files:
            assert rel in ROWS and ROWS[rel]["kind"] == "loop", f"{what}: {rel}"


def test_loop_imports_loop():
    for rel, row in ROWS.items():
        if row["kind"] != "loop":
            continue
        imp = (AUDIO / (rel + ".import")).read_text(encoding="utf-8")
        assert re.search(r"^loop=true$", imp, re.M), f"{rel}: imported without loop=true"


# ── Levels ────────────────────────────────────────────────────────────────────

def test_loops_and_verb_cues_sit_in_their_level_bands():
    levels = MANIFEST["levels"]
    for rel, row in ROWS.items():
        if row["licence"] != ORIGINAL:
            continue
        if row["kind"] == "loop":
            assert abs(row["integrated_lufs"] - levels["loop_integrated_lufs"]) <= 1.0, rel
            assert row["true_peak_dbtp"] <= levels["loop_true_peak_max_dbtp"], rel
        elif rel.startswith("sfx/verb_"):
            assert abs(row["momentary_max_lufs"] - levels["cue_momentary_max_lufs"]) <= 1.0, rel
            assert row["true_peak_dbtp"] <= levels["cue_true_peak_max_dbtp"], rel


# ── Licences ──────────────────────────────────────────────────────────────────

def test_every_licence_is_recorded_in_the_three_places():
    sources = (AUDIO / "AUDIO_SOURCES.md").read_text(encoding="utf-8")
    third_party = _third_party_rows()
    third_party_text = (REPO / "THIRD_PARTY.md").read_text(encoding="utf-8")
    credits = json.loads((GODOT / "data" / "credits.json").read_text(encoding="utf-8"))
    credited = {e["name"]: e["licence"] for e in credits["third_party"]}
    team_licences = {m.get("licence", "") for m in credits["team"]}
    for rel, row in ROWS.items():
        assert f"`{rel}`" in sources, f"{rel}: not in AUDIO_SOURCES.md"
        assert row["licence"], f"{rel}: no licence"
        if row["licence"] == ORIGINAL:
            scripts = re.findall(r"tools/[\w/]+\.py", row["source"])
            assert scripts and all((REPO / s).exists() for s in scripts), f"{rel}: generator missing ({row['source']})"
            assert all(s in third_party_text for s in scripts), f"{rel}: THIRD_PARTY.md does not name {scripts}"
            assert ORIGINAL in team_licences, "credits.json does not record the project's original audio"
        else:
            component = row["component"]
            assert third_party.get(component) == row["licence"], f"{rel}: THIRD_PARTY.md row {component!r}"
            assert credited.get(component) == row["licence"], f"{rel}: credits.json entry {component!r}"


def test_no_retired_licence_lingers():
    """Au2-01 replaced every Mixkit file: the licence goes with them."""
    licences = {row["licence"] for row in ROWS.values()}
    assert not any("Mixkit" in lic for lic in licences)
    assert "Mixkit" not in _third_party_rows().get("Mixkit sound effects", "")


# ── The game's catalogue ──────────────────────────────────────────────────────

def test_every_catalogue_path_exists_and_is_listed():
    catalogue = _catalogue_paths()
    assert {"MUSIC_TRACKS", "AMBIENT_BASE", "AMBIENT_WEATHER", "ALERT_SFX", "VERB_SFX", "UI_SFX"} <= set(catalogue)
    for name, entries in catalogue.items():
        for key, res in entries.items():
            rel = res.removeprefix("res://assets/audio/")
            assert rel in ROWS, f"{name}.{key}: {res} is not a listed file"


def test_the_mixer_layers_and_the_verb_cues_resolve():
    catalogue = _catalogue_paths()
    mixer = (GODOT / "scripts" / "audio" / "AmbienceMixer.gd").read_text(encoding="utf-8")
    body = re.search(r"const LAYERS := \{(.*?)\n\}", mixer, re.S).group(1)
    layers = dict(re.findall(r'"(\w+)":\s*"(\w+)"', body))
    assert set(layers) >= {"water_lap", "wind_light", "wind_strong", "rain_light", "rain_heavy", "gulls", "storm"}
    for layer, key in layers.items():
        assert key in catalogue["AMBIENT_BASE"], f"mixer layer {layer} -> {key}"
        assert ROWS[catalogue["AMBIENT_BASE"][key].removeprefix("res://assets/audio/")]["kind"] == "loop"
    assert sorted(catalogue["VERB_SFX"]) == sorted(VERB_CUES)
    verbs = (GODOT / "scripts" / "audio" / "VerbSounds.gd").read_text(encoding="utf-8")
    for const in ("STOCK", "FEED_PLAN", "DELOUSE", "HARVEST", "AUCTION", "STORM"):
        assert re.search(rf'^const {const} := "(\w+)"', verbs, re.M), const
    for engine in ("feed_barge", "boat_engine"):
        assert engine in catalogue["AMBIENT_BASE"]
