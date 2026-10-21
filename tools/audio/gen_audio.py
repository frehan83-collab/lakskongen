#!/usr/bin/env python3
"""Build the game's generated audio and its manifest (roadmap Au2-01).

Synthesises every ambience loop (tools/audio/ambience.py) and verb cue
(tools/audio/cues.py) from seeded numpy generators, levels each one by
measured loudness (ITU-R BS.1770 / EBU R 128), encodes the loops to Ogg
Vorbis through Blender's Audaspace (tools/audio/blender_ogg.py: this machine
has no other Ogg encoder), decodes them back and measures what the game will
play, and writes godot/assets/audio/audio_manifest.json: one row per audio
file in the game (generated or third-party) with its kind, duration, level,
SHA-256, licence and source. tests/test_audio_floor.py (the CI audio check)
reads that manifest.

Levels (sources in godot/assets/audio/AUDIO_SOURCES.md):
* loops: -24 LUFS integrated, true peak <= -1 dBTP (the IESD console mix
  reference, 2015; EBU R 128's -1 dBTP ceiling); the game's mixer sets each
  layer's level relative to that;
* verb cues: -19 LUFS momentary maximum (the harvest stinger's own level, so
  every verb's payoff reads alike), true peak <= -3 dBTP.

Run (needs Blender 5.2 for the Ogg step; BLENDER_BIN overrides the path):
    python3 tools/audio/gen_audio.py           # build and write
    python3 tools/audio/gen_audio.py --check   # rebuild in a temp dir, compare bytes
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import struct
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ambience  # noqa: E402
import audio_dsp as d  # noqa: E402
import cues  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
AUDIO = REPO / "godot" / "assets" / "audio"
MANIFEST = AUDIO / "audio_manifest.json"
BLENDER = os.environ.get("BLENDER_BIN", "/Applications/Blender.app/Contents/MacOS/Blender")
ENCODER = Path(__file__).resolve().parent / "blender_ogg.py"

LOOP_S = 60.0
LOOP_LUFS, LOOP_CEILING_DBTP = -24.0, -1.5   # -1.5 before encoding leaves room for the codec
CUE_LUFS, CUE_CEILING_DBTP = -19.0, -3.0
OGG_BITRATE = {1: 80_000, 2: 112_000}
ORIGINAL = "Original work of the project"
GENERATOR = "tools/audio/gen_audio.py"

# (path under godot/assets/audio, kind, builder, seed, channels, what it is)
LOOPS = [
    ("ambient/fjord_wind_light.ogg", lambda r, n: ambience.wind(r, n, 0.2), 101, 2,
     "fjord wind: a breeze, gusting softly"),
    ("ambient/fjord_wind_strong.ogg", lambda r, n: ambience.wind(r, n, 0.9), 102, 2,
     "fjord wind: a gale, with whistles and buffeting"),
    ("ambient/water_lap.ogg", lambda r, n: ambience.water_lap(r, n), 201, 2,
     "water lap: small waves at pen floats and a hull on a calm day"),
    ("ambient/gulls.ogg", lambda r, n: ambience.gulls(r, n), 301, 2,
     "gulls: herring-gull-like calls, near and far"),
    ("ambient/feed_barge.ogg", lambda r, n: ambience.feed_barge(r, n), 401, 1,
     "barge engine: the feed barge's generator set, feed blower and pellets (3D emitter)"),
    ("ambient/boat_engine.ogg", lambda r, n: ambience.boat_engine(r, n), 402, 1,
     "boat engine: a wellboat's main engine, propeller and bow wash (3D emitter)"),
    ("ambient/rain_light.ogg", lambda r, n: ambience.rain(r, n, 450.0, 0.06, 0.15), 501, 2,
     "rain: light rain on the water"),
    ("ambient/rain_heavy.ogg", lambda r, n: ambience.rain(r, n, 2400.0, 0.16, 0.55), 502, 2,
     "rain: heavy rain on the water"),
    ("ambient/storm.ogg", lambda r, n: ambience.storm_sea(r, n), 601, 2,
     "storm: breaking waves, the sea's roar and a gale"),
    ("ambient/harbor_ambience.ogg", lambda r, n: ambience.harbour(r, n), 701, 2,
     "harbour: water at the pilings, an idling boat, mooring lines, a far gull"),
    ("ambient/underwater.ogg", lambda r, n: ambience.underwater(r, n), 801, 2,
     "underwater: low rumble, the muffled wash above, bubble streams"),
]

CUES = [
    ("sfx/verb_stock.wav", cues.stock, 1101, "verb cue: smolts pumped into a pen"),
    ("sfx/verb_feed_plan.wav", cues.feed_plan, 1102, "verb cue: a feeding plan set (blower, pellets, a tag)"),
    ("sfx/verb_delouse.wav", cues.delouse, 1103, "verb cue: a delousing treatment started"),
    ("sfx/verb_auction.wav", cues.auction, 1104, "verb cue: the auctioneer's gavel"),
    ("sfx/verb_storm.wav", cues.storm, 1105, "verb cue: distant thunder and a rising gust"),
]

# Files the game ships that this script does not make: their THIRD_PARTY.md
# row (component), licence and what they are. Measured here like the rest.
THIRD_PARTY_FILES = {
    "ui/button_click.wav": ("Kenney UI Audio", "CC0 1.0", "ui", "the global button click (every Button press)"),
    "ui/button_hover.wav": ("Kenney UI Audio", "CC0 1.0", "ui", "button hover"),
    "ui/error.wav": ("Kenney UI Audio", "CC0 1.0", "ui", "error cue (critical event)"),
    "ui/notification.wav": ("Kenney UI Audio", "CC0 1.0", "ui", "notification cue (warning event)"),
    "ui/panel_close.wav": ("Kenney UI Audio", "CC0 1.0", "ui", "panel close"),
    "ui/panel_open.wav": ("Kenney UI Audio", "CC0 1.0", "ui", "panel open"),
    "sfx/disease_alert.wav": ("Kenney UI Audio", "CC0 1.0", "cue", "alert: disease outbreak"),
    "sfx/lice_alert.wav": ("Freesound sounds", "CC0 1.0", "cue", "alert: lice warning"),
    "sfx/financial_crisis.wav": ("Freesound sounds", "CC0 1.0", "cue", "alert: market crash"),
    "sfx/achievement_unlock.wav": ("Freesound sounds", "CC0 1.0", "cue", "achievement (medal)"),
    "music/main_theme.ogg": ("Music by Kevin MacLeod", "CC BY 4.0", "music", "main theme (menu)"),
    "music/calm_sea.ogg": ("Music by Kevin MacLeod", "CC BY 4.0", "music", "calm sea"),
    "music/spring.ogg": ("Music by Kevin MacLeod", "CC BY 4.0", "music", "spring"),
    "music/summer.ogg": ("Music by Kevin MacLeod", "CC BY 4.0", "music", "summer"),
    "music/autumn.ogg": ("Music by Kevin MacLeod", "CC BY 4.0", "music", "autumn"),
    "music/winter.ogg": ("Music by Kevin MacLeod", "CC BY 4.0", "music", "winter"),
}
# Generated earlier by its own script (U2-05).
OWN_SCRIPTED = {
    "sfx/harvest_stinger.wav": ("tools/gen_harvest_stinger.py", "verb cue: the harvest's payoff stinger"),
}


def fix_ogg_serial(path: Path, serial: int) -> None:
    """FFmpeg's Ogg muxer picks a random stream serial; write a fixed one and
    recompute each page's CRC so the same audio gives the same bytes."""
    table = []
    for i in range(256):
        r = i << 24
        for _ in range(8):
            r = ((r << 1) ^ 0x04C11DB7) if (r & 0x80000000) else (r << 1)
        table.append(r & 0xFFFFFFFF)

    def crc(data: bytes) -> int:
        c = 0
        for b in data:
            c = ((c << 8) & 0xFFFFFFFF) ^ table[((c >> 24) & 0xFF) ^ b]
        return c

    data = bytearray(path.read_bytes())
    pos = 0
    while pos < len(data):
        if data[pos:pos + 4] != b"OggS":
            raise ValueError(f"{path}: not an Ogg page at {pos}")
        nseg = data[pos + 26]
        size = 27 + nseg + sum(data[pos + 27:pos + 27 + nseg])
        page = data[pos:pos + size]
        page[14:18] = struct.pack("<I", serial)
        page[22:26] = b"\0\0\0\0"
        page[22:26] = struct.pack("<I", crc(bytes(page)))
        data[pos:pos + size] = page
        pos += size
    path.write_bytes(bytes(data))


def ogg_duration(path: Path) -> tuple[float, int]:
    """(seconds, channels) from the Vorbis identification header and the last granule."""
    data = path.read_bytes()
    i = data.find(b"\x01vorbis")
    channels, rate = data[i + 11], struct.unpack("<I", data[i + 12:i + 16])[0]
    last = data.rfind(b"OggS")
    return struct.unpack("<q", data[last + 6:last + 14])[0] / rate, channels


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run_blender(jobs: list[dict], work: Path) -> None:
    if not jobs:
        return
    jobs_file = work / "jobs.json"
    jobs_file.write_text(json.dumps(jobs), encoding="utf-8")
    cmd = [BLENDER, "--background", "--factory-startup", "--python-exit-code", "1",
           "--python", str(ENCODER), "--", str(jobs_file)]
    done = subprocess.run(cmd, capture_output=True, text=True, check=False)
    if done.returncode != 0:
        sys.stderr.write(done.stdout[-3000:] + done.stderr[-3000:])
        raise SystemExit(f"gen_audio: Blender failed ({done.returncode})")


def build(out_root: Path, work: Path) -> list[dict]:
    """Write every generated file under out_root; return their manifest rows."""
    n = int(LOOP_S * d.RATE)
    rows: list[dict] = []
    jobs = []
    for rel, builder, seed, channels, what in LOOPS:
        x = builder(np.random.default_rng(seed), n)
        x = x if channels == x.shape[1] else x.mean(axis=1, keepdims=True)
        x = d.normalise(x, LOOP_LUFS, LOOP_CEILING_DBTP)
        wav = work / (Path(rel).stem + ".wav")
        d.write_wav16(wav, x)
        out = out_root / rel
        out.parent.mkdir(parents=True, exist_ok=True)
        jobs.append({"wav": str(wav), "ogg": str(out), "channels": channels,
                     "bitrate": OGG_BITRATE[channels], "decoded": str(work / (Path(rel).stem + ".decoded.wav"))})
        rows.append({"path": rel, "kind": "loop", "what": what, "licence": ORIGINAL,
                     "source": f"generated: {GENERATOR} (seed {seed})", "component": ""})
    run_blender(jobs, work)
    for job, (rel, *_rest) in zip(jobs, LOOPS):
        fix_ogg_serial(Path(job["ogg"]), zlib.crc32(rel.encode()) & 0x7FFFFFFF)
    for rel, builder, seed, what in CUES:
        x = builder(np.random.default_rng(seed))
        x = d.normalise(x, CUE_LUFS, CUE_CEILING_DBTP, momentary=True)
        d.write_wav16(out_root / rel, x)
        rows.append({"path": rel, "kind": "cue", "what": what, "licence": ORIGINAL,
                     "source": f"generated: {GENERATOR} (seed {seed})", "component": ""})
    return rows


def describe(root: Path, row: dict, work: Path) -> dict:
    """Add the measured facts (duration, channels, level, hash) to a row."""
    path = root / row["path"]
    if path.suffix == ".ogg":
        duration, channels = ogg_duration(path)
        decoded = work / ("m_" + path.stem + ".decoded.wav")
        if not decoded.exists():
            run_blender([{"ogg": str(path), "channels": channels, "bitrate": 0, "decoded": str(decoded)}], work)
        x, _fs = d.read_wav(decoded)
    else:
        x, fs = d.read_wav(path)
        duration, channels = len(x) / fs, x.shape[1]
    return {**row, "duration_s": round(duration, 3), "channels": channels, **d.measure(x),
            "sha256": sha256(path)}


def manifest_rows(root: Path, generated: list[dict], work: Path) -> list[dict]:
    rows = [describe(root, r, work) for r in generated]
    for rel, (component, licence, kind, what) in THIRD_PARTY_FILES.items():
        rows.append(describe(root, {"path": rel, "kind": kind, "what": what, "licence": licence,
                                    "source": f"third party: {component} (THIRD_PARTY.md)", "component": component}, work))
    for rel, (script, what) in OWN_SCRIPTED.items():
        rows.append(describe(root, {"path": rel, "kind": "cue", "what": what, "licence": ORIGINAL,
                                    "source": f"generated: {script}", "component": ""}, work))
    return sorted(rows, key=lambda r: r["path"])


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--check", action="store_true", help="rebuild in a temp dir and compare with the shipped files")
    args = ap.parse_args()
    with tempfile.TemporaryDirectory(prefix="lk_audio_") as tmp:
        work = Path(tmp)
        if args.check:
            out = work / "out"
            generated = build(out, work)
            bad = [r["path"] for r in generated if sha256(out / r["path"]) != sha256(AUDIO / r["path"])]
            print(f"gen_audio --check: {len(generated) - len(bad)} of {len(generated)} files byte-identical")
            for p in bad:
                print("  differs:", p)
            return 1 if bad else 0
        generated = build(AUDIO, work)
        rows = manifest_rows(AUDIO, generated, work)
    manifest = {
        "_comment": ("Every audio file the game ships (Au2-01): kind (loop, cue, ui, music), duration, level "
                     "(BS.1770: integrated and momentary-max LUFS, true peak dBTP; an Ogg is measured as "
                     "decoded), SHA-256, licence and source. Written by tools/audio/gen_audio.py; "
                     "tests/test_audio_floor.py is the CI audio check."),
        "levels": {"loop_integrated_lufs": LOOP_LUFS, "cue_momentary_max_lufs": CUE_LUFS,
                   "loop_true_peak_max_dbtp": -1.0, "cue_true_peak_max_dbtp": CUE_CEILING_DBTP},
        "files": rows,
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    for r in rows:
        print(f"{r['path']:32s} {r['kind']:5s} {r['duration_s']:7.2f}s ch{r['channels']} "
              f"I {r['integrated_lufs']:6.1f}  M {r['momentary_max_lufs']:6.1f}  TP {r['true_peak_dbtp']:5.1f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
