"""The verbs' sound cues, synthesised (roadmap Au2-01).

One short cue per player verb the event bus reports (the harvest has its
own stinger, tools/gen_harvest_stinger.py). Each returns stereo float frames
at 44.1 kHz; gen_audio.py levels them to the stinger's loudness and writes
16-bit WAVs. Naturalistic first, with a soft tonal tag where the verb is a
decision the player made (the feeding plan, the treatment), so the cue reads
as "done" without becoming a jingle.
"""

from __future__ import annotations

import math

import numpy as np

import audio_dsp as d
from audio_dsp import RATE
from ambience import _burst, _glug

# A struck wooden bar's partials (ratio, level, decay s), as in the harvest stinger.
WOOD = ((1.0, 1.0, 0.45), (3.93, 0.3, 0.1), (9.24, 0.07, 0.035))


def _place(buf: np.ndarray, start_s: float, sig: np.ndarray) -> None:
    i = int(start_s * RATE)
    m = min(len(sig), len(buf) - i)
    if m > 0:
        buf[i:i + m] += sig[:m]


def _wood(freq: float, dur: float = 0.8) -> np.ndarray:
    t = np.arange(int(dur * RATE)) / RATE
    return sum(level * d.decay_env(len(t), 0.003, decay) * np.sin(2 * math.pi * freq * ratio * t)
               for ratio, level, decay in WOOD)


def _room(rng: np.random.Generator, x: np.ndarray, length_s: float, decay_s: float, wet: float) -> np.ndarray:
    ir_l, ir_r = d.diffuse_ir(rng, length_s, decay_s, 5000.0), d.diffuse_ir(rng, length_s, decay_s, 5000.0)
    n = len(x)
    return np.stack([(1 - wet) * x[:, 0] + wet * np.convolve(x[:, 0], ir_l)[:n],
                     (1 - wet) * x[:, 1] + wet * np.convolve(x[:, 1], ir_r)[:n]], axis=1)


def _fade_tail(x: np.ndarray, tail_s: float) -> np.ndarray:
    m = int(tail_s * RATE)
    env = np.ones(len(x))
    env[-m:] = np.linspace(1.0, 0.0, m) ** 2
    return x * env[:, None]


def stock(rng: np.random.Generator) -> np.ndarray:
    """Smolts pumped into a pen: the delivery hose's rush, fish dropping into
    the water in a quick run of splashes, a last fuller splash."""
    n = int(1.9 * RATE)
    out = np.zeros((n, 2))
    t = np.arange(n) / RATE
    env = np.clip(t / 0.3, 0, 1) * np.clip((1.45 - t) / 0.45, 0, 1)
    rush = np.stack([_burst(rng, n, 350, 2600, 0.001, 10.0), _burst(rng, n, 350, 2600, 0.001, 10.0)], axis=1)
    out += 0.45 * rush * env[:, None]
    for k in range(11):
        at = 0.25 + k * 0.09 + rng.uniform(-0.02, 0.02)
        s = _burst(rng, int(0.2 * RATE), 400, 4200, 0.003, rng.uniform(0.035, 0.08)) * rng.uniform(0.5, 1.0)
        g = _glug(rng, rng.uniform(420, 900), rng.uniform(0.015, 0.03))
        s[: len(g)] += 0.4 * g[: len(s)]
        st = d.pan(s, rng.uniform(-0.5, 0.5))
        _place(out[:, 0], at, st[:, 0])
        _place(out[:, 1], at, st[:, 1])
    big = _burst(rng, int(0.5 * RATE), 120, 3000, 0.006, 0.16) * 1.4
    st = d.pan(big, 0.1)
    _place(out[:, 0], 1.32, st[:, 0])
    _place(out[:, 1], 1.32, st[:, 1])
    return _fade_tail(_room(rng, out, 0.6, 0.15, 0.18), 0.25)


def feed_plan(rng: np.random.Generator) -> np.ndarray:
    """A feeding plan set: a lever's wooden tick, the feed blower spinning up,
    pellets rattling through the pipe, then a soft two-note tag."""
    n = int(1.9 * RATE)
    out = np.zeros((n, 2))
    tick = _burst(rng, int(0.03 * RATE), 1800, 6000, 0.0005, 0.006)
    _place(out[:, 0], 0.0, tick)
    _place(out[:, 1], 0.0, tick)
    m = int(1.3 * RATE)
    t = np.arange(m) / RATE
    f = 580.0 - 400.0 * np.exp(-t / 0.25)
    phase = 2 * math.pi * np.cumsum(f) / RATE
    env = np.clip(t / 0.1, 0, 1) * np.clip((1.3 - t) / 0.4, 0, 1)
    blower = (0.25 * np.sin(phase) + 0.08 * np.sin(2 * phase) + 0.6 * _burst(rng, m, 400, 2600, 0.15, 10.0)) * env
    st = np.stack([blower, np.roll(blower, int(0.004 * RATE))], axis=1)
    _place(out[:, 0], 0.05, st[:, 0])
    _place(out[:, 1], 0.05, st[:, 1])
    for _ in range(140):
        at = rng.uniform(0.35, 1.25)
        c = _burst(rng, int(0.004 * RATE), 2500, 8000, 0.0002, 0.0008) * rng.uniform(0.3, 1.0)
        st = d.pan(c, rng.uniform(-0.4, 0.4))
        _place(out[:, 0], at, st[:, 0])
        _place(out[:, 1], at, st[:, 1])
    for at, freq in ((1.15, 659.26), (1.27, 987.77)):   # E5 then B5, a fifth up
        tag = 0.28 * _wood(freq, 0.7)
        _place(out[:, 0], at, tag)
        _place(out[:, 1], at, tag)
    return _fade_tail(_room(rng, out, 0.5, 0.12, 0.15), 0.2)


def delouse(rng: np.random.Generator) -> np.ndarray:
    """A delousing treatment started: the treatment pump's thump and hum, the
    water rushing through, bubbles rising, a soft settled two-note tag."""
    n = int(2.3 * RATE)
    out = np.zeros((n, 2))
    t = np.arange(int(0.4 * RATE)) / RATE
    thump = np.sin(2 * math.pi * 68.0 * t) * d.decay_env(len(t), 0.003, 0.08)
    _place(out[:, 0], 0.0, thump)
    _place(out[:, 1], 0.0, thump)
    m = int(1.7 * RATE)
    tm = np.arange(m) / RATE
    env = np.clip(tm / 0.25, 0, 1) * np.clip((1.7 - tm) / 0.5, 0, 1)
    hum = sum(np.sin(2 * math.pi * 118.0 * k * tm) / k for k in range(1, 6)) * 0.12
    rush = _burst(rng, m, 220, 2800, 0.2, 10.0) * 0.5
    st = np.stack([(hum + rush) * env, (hum + np.roll(rush, 177)) * env], axis=1)
    _place(out[:, 0], 0.05, st[:, 0])
    _place(out[:, 1], 0.05, st[:, 1])
    for _ in range(38):
        at = rng.uniform(0.2, 1.55)
        g = _glug(rng, rng.uniform(500, 1600), rng.uniform(0.012, 0.03), rise=1.5) * rng.uniform(0.2, 0.6)
        st = d.pan(g, rng.uniform(-0.6, 0.6))
        _place(out[:, 0], at, st[:, 0])
        _place(out[:, 1], at, st[:, 1])
    for at, freq in ((1.45, 587.33), (1.58, 880.0)):   # D5 then A5
        tag = 0.3 * _wood(freq, 0.7)
        _place(out[:, 0], at, tag)
        _place(out[:, 1], at, tag)
    return _fade_tail(_room(rng, out, 0.6, 0.15, 0.16), 0.25)


def auction(rng: np.random.Generator) -> np.ndarray:
    """The auctioneer's gavel: two strikes on a sounding block in a hall."""
    n = int(1.3 * RATE)
    out = np.zeros((n, 2))
    modes = ((780.0, 1.0, 0.045), (1720.0, 0.5, 0.025), (2950.0, 0.3, 0.015), (4300.0, 0.15, 0.008))
    for at, gain in ((0.0, 0.85), (0.32, 1.0)):
        m = int(0.4 * RATE)
        t = np.arange(m) / RATE
        f_jit = rng.uniform(0.99, 1.01)
        block = sum(level * np.sin(2 * math.pi * f * f_jit * t) * np.exp(-t / dec) for f, level, dec in modes)
        body = 0.7 * np.sin(2 * math.pi * 140.0 * t) * np.exp(-t / 0.06)
        click = np.zeros(m)
        click[: int(0.004 * RATE)] = _burst(rng, int(0.004 * RATE), 3000, 12000, 0.0001, 0.0008)
        strike = gain * (block + body + 0.6 * click) * np.clip(t / 0.0008, 0, 1)
        _place(out[:, 0], at, strike)
        _place(out[:, 1], at, strike)
    return _fade_tail(_room(rng, out, 0.9, 0.25, 0.25), 0.2)


def storm(rng: np.random.Generator) -> np.ndarray:
    """A storm warning: thunder rolling in the distance and a gust rising."""
    n = int(3.8 * RATE)
    t = np.arange(n) / RATE
    env = np.zeros(n)
    for _ in range(6):
        onset = rng.uniform(0.05, 0.9)
        env += rng.uniform(0.5, 1.0) * np.where(t >= onset, np.exp(-(t - onset) / rng.uniform(0.6, 1.6)), 0.0) \
            * np.clip((t - onset) / 0.25, 0, 1) ** 2
    env /= np.max(env)
    rumble = np.stack([d.shaped_noise(n, rng, lambda f: d.band(f, 25.0, 320.0, 2.0) * d.pink(f) * 10.0) for _ in range(2)], axis=1)
    crackle_env = np.exp(-t / 0.25) * (rng.random(n) < 0.02)
    crackle = np.convolve(crackle_env, _burst(rng, int(0.01 * RATE), 900, 5000, 0.0005, 0.003))[:n] * 0.2
    gust_env = np.clip((t - 0.4) / 1.2, 0, 1) * np.clip((3.6 - t) / 1.6, 0, 1)
    gust = np.stack([_burst(rng, n, 300, 1500, 0.001, 20.0), _burst(rng, n, 300, 1500, 0.001, 20.0)], axis=1)
    out = rumble * env[:, None] + crackle[:, None] + 0.35 * gust * gust_env[:, None]
    return _fade_tail(out, 0.5)
