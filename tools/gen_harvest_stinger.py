#!/usr/bin/env python3
"""Deterministic generator of the harvest stinger (roadmap U2-05).

The payoff cue of a harvest: a short, warm rising figure in D major on a
struck-wood voice (marimba-like partials), a low muffled "hull" knock that
grounds it, and a soft bell chord that rings out over a small hall. Built
here from sines and seeded noise, so it is an original work of this project
(no third-party sample; nothing to attribute). It replaces the 0.36 s
`harvest_ready.wav` alert, which was a generic notification blip.

Writes godot/assets/audio/sfx/harvest_stinger.wav: 44.1 kHz, 16-bit stereo,
about 1.9 s, levelled to the game's other cues: -22 dBFS RMS over its first
half second (the achievement cue measures -25.5, the old harvest blip -24.1),
a little above them as the payoff it is, never above -3 dBFS peak.

Run:
    python3 tools/gen_harvest_stinger.py
"""

from __future__ import annotations

import os
import wave

import numpy as np

RATE = 44_100
LENGTH_S = 1.9
PEAK_DBFS = -3.0
ONSET_RMS_DBFS = -22.0
SEED = 2027
OUT = os.path.join(os.path.dirname(__file__), "..", "godot", "assets", "audio", "sfx", "harvest_stinger.wav")

# D major, rising: D5 F#5 A5 D6, then the chord rings (D5 A5 D6 F#6).
FIGURE = [(0.00, 587.33, -0.35), (0.09, 739.99, -0.12), (0.18, 880.00, 0.12), (0.29, 1174.66, 0.32)]
CHORD = [(0.42, 587.33), (0.42, 880.00), (0.42, 1174.66), (0.42, 1479.98)]
# A struck bar's partials (ratio, relative level, decay seconds): the
# 1 : 3.93 : 9.24 of a tuned marimba bar, upper partials dying fast.
WOOD = [(1.0, 1.0, 0.55), (3.93, 0.32, 0.12), (9.24, 0.08, 0.04)]
# A small bell's partials for the ringing chord.
BELL = [(1.0, 1.0, 1.1), (2.0, 0.35, 0.6), (3.01, 0.18, 0.35), (4.2, 0.06, 0.2)]


def envelope(t: np.ndarray, decay_s: float, attack_s: float = 0.004) -> np.ndarray:
    attack = np.clip(t / attack_s, 0.0, 1.0)
    return attack * np.exp(-t / decay_s)


def voice(t: np.ndarray, freq: float, partials: list[tuple[float, float, float]]) -> np.ndarray:
    out = np.zeros_like(t)
    for ratio, level, decay in partials:
        out += level * envelope(t, decay) * np.sin(2.0 * np.pi * freq * ratio * t)
    return out


def place(buffer: np.ndarray, start_s: float, signal: np.ndarray) -> None:
    i = int(start_s * RATE)
    n = min(len(signal), len(buffer) - i)
    if n > 0:
        buffer[i:i + n] += signal[:n]


def knock(rng: np.random.Generator) -> np.ndarray:
    """A low, muffled knock (a hull alongside): a 70 Hz thump plus lowpassed noise."""
    n = int(0.25 * RATE)
    t = np.arange(n) / RATE
    thump = np.sin(2.0 * np.pi * 70.0 * t) * envelope(t, 0.07, 0.002)
    noise = rng.standard_normal(n)
    alpha = 0.02  # one-pole lowpass (~140 Hz)
    for i in range(1, n):
        noise[i] = noise[i - 1] + alpha * (noise[i] - noise[i - 1])
    noise *= envelope(t, 0.05, 0.002) * 6.0
    return 0.5 * thump + 0.25 * noise


def hall(rng: np.random.Generator, n: int) -> np.ndarray:
    """A short, soft impulse response: decaying noise, darkened."""
    t = np.arange(n) / RATE
    ir = rng.standard_normal(n) * np.exp(-t / 0.35)
    alpha = 0.25
    for i in range(1, n):
        ir[i] = ir[i - 1] + alpha * (ir[i] - ir[i - 1])
    ir[0] = 0.0
    return ir / np.sqrt(np.sum(ir * ir))


def render() -> np.ndarray:
    rng = np.random.default_rng(SEED)
    n = int(LENGTH_S * RATE)
    left = np.zeros(n)
    right = np.zeros(n)
    note_t = np.arange(int(0.9 * RATE)) / RATE
    for start, freq, pan in FIGURE:
        s = 0.55 * voice(note_t, freq, WOOD)
        place(left, start, s * np.sqrt(0.5 * (1.0 - pan)))
        place(right, start, s * np.sqrt(0.5 * (1.0 + pan)))
    bell_t = np.arange(int(1.45 * RATE)) / RATE
    for (start, freq), pan in zip(CHORD, (-0.25, 0.25, -0.1, 0.1)):
        s = 0.22 * voice(bell_t, freq, BELL)
        place(left, start, s * np.sqrt(0.5 * (1.0 - pan)))
        place(right, start, s * np.sqrt(0.5 * (1.0 + pan)))
    k = knock(rng)
    place(left, 0.0, k)
    place(right, 0.0, k)

    ir_l = hall(rng, int(0.6 * RATE))
    ir_r = hall(rng, int(0.6 * RATE))
    wet = 0.22
    left = (1.0 - wet) * left + wet * np.convolve(left, ir_l)[:n]
    right = (1.0 - wet) * right + wet * np.convolve(right, ir_r)[:n]

    fade = np.ones(n)
    tail = int(0.25 * RATE)
    fade[-tail:] = np.linspace(1.0, 0.0, tail) ** 2
    stereo = np.stack([left * fade, right * fade], axis=1)
    onset_rms = np.sqrt(np.mean(stereo[: RATE // 2] ** 2))
    gain = 10.0 ** (ONSET_RMS_DBFS / 20.0) / onset_rms
    gain = min(gain, 10.0 ** (PEAK_DBFS / 20.0) / np.max(np.abs(stereo)))
    return stereo * gain


def write(path: str, stereo: np.ndarray) -> None:
    pcm = np.clip(np.round(stereo * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def main() -> None:
    path = os.path.normpath(OUT)
    stereo = render()
    write(path, stereo)
    onset = 20 * np.log10(np.sqrt(np.mean(stereo[: RATE // 2] ** 2)))
    print(f"wrote {path}: {len(stereo) / RATE:.2f} s, peak {20 * np.log10(np.max(np.abs(stereo))):.1f} dBFS, "
          f"onset RMS {onset:.1f} dBFS")


if __name__ == "__main__":
    main()
