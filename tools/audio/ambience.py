"""The game's ambience loops, synthesised (roadmap Au2-01).

Every loop is exactly periodic (audio_dsp: circular noise, whole-cycle
modulation, events placed on a circle), so it repeats with no seam. Each
function takes a seeded generator and a length in samples and returns float
frames x channels at 44.1 kHz; gen_audio.py levels and encodes them.

What each one is modelled on (sources in godot/assets/audio/AUDIO_SOURCES.md):
* wind: band-limited noise whose bands follow a gust curve (0.02-0.2 Hz),
  the upper bands rising more in a gust; a gale adds narrow whistles and
  low buffeting.
* water lap: small waves slapping pen floats and a hull, clustered on the
  swell (4-12 s periods), each a short splash with now and then a bubble
  "glug" (an air pocket ringing at a few hundred Hz and rising).
* gulls: herring-gull-like calls (a first long "kee-ow" then a run of
  shorter notes, or a "kek-kek" alarm), harmonic voices with their
  strongest energy around 1-1.5 kHz, at distances from 30 to 250 m.
* diesel engines: a four-stroke's firing frequency is rpm x cylinders / 120
  (a feed barge's 6-cylinder generator set at 1500 rpm fires at 75 Hz, a
  wellboat's 8-cylinder main engine at 900 rpm at 60 Hz), each firing a
  short block-and-exhaust resonance; the barge adds its feed blower's
  blade-pass tone and pellets rattling in the feed pipes.
* rain on water: every drop a short impact click; larger drops add a
  bubble "plink" at 2-10 kHz (Medwin et al.), small ones a faint high ring.
"""

from __future__ import annotations

import math

import numpy as np
from scipy import signal

import audio_dsp as d
from audio_dsp import RATE


def _burst(rng: np.random.Generator, n: int, lo: float, hi: float, attack_s: float, decay_s: float) -> np.ndarray:
    """A short band-limited noise burst with an attack-decay envelope."""
    b, a = signal.butter(2, [lo / (RATE / 2), min(hi, RATE * 0.45) / (RATE / 2)], "band")
    return signal.lfilter(b, a, rng.standard_normal(n)) * d.decay_env(n, attack_s, decay_s)


def _glug(rng: np.random.Generator, f0: float, decay_s: float, rise: float = 1.3) -> np.ndarray:
    """A bubble's ring: a damped sine whose pitch rises as it surfaces."""
    n = int(decay_s * 6 * RATE)
    t = np.arange(n) / RATE
    f = f0 * (1.0 + (rise - 1.0) * (1.0 - np.exp(-t / (decay_s * 1.5))))
    return np.sin(2 * math.pi * np.cumsum(f) / RATE + rng.uniform(0, 2 * math.pi)) * d.decay_env(n, 0.002, decay_s)


def _stereo_noise(n: int, rng: np.random.Generator, response) -> np.ndarray:
    return np.stack([d.shaped_noise(n, rng, response), d.shaped_noise(n, rng, response)], axis=1)


# ── Wind ──────────────────────────────────────────────────────────────────────

WIND_BANDS_HZ = (110.0, 230.0, 460.0, 880.0, 1650.0, 3100.0)


def wind(rng: np.random.Generator, n: int, strength: float) -> np.ndarray:
    """Fjord wind: strength 0 (a breeze in the birches) to 1 (a gale)."""
    gust = d.lfo(n, rng, 0.02, 0.2 + 0.15 * strength)
    level = 0.35 + 0.65 * (0.5 + 0.5 * gust) ** (1.2 + strength)
    lag = int(0.15 * RATE)
    out = np.zeros((n, 2))
    for k, fc in enumerate(WIND_BANDS_HZ):
        tilt = (WIND_BANDS_HZ[0] / fc) ** (0.55 - 0.25 * strength)
        own = 0.85 + 0.15 * d.lfo(n, rng, 0.05, 0.4)
        env = level ** (1.0 + 0.45 * k) * own * tilt
        noise = _stereo_noise(n, rng, lambda f, c=fc: d.band(f, c / 1.7, c * 1.7, 2.0))
        out[:, 0] += noise[:, 0] * env
        out[:, 1] += noise[:, 1] * np.roll(env, lag)
    if strength > 0.5:
        for fc, q in ((380.0, 22.0), (540.0, 28.0), (720.0, 30.0)):
            howl = d.lfo(n, rng, 0.04, 0.3)
            env = np.clip(0.5 + 0.5 * howl, 0, 1) ** 3 * (0.5 + 0.5 * gust) ** 2 * 3.0 * (strength - 0.4)
            noise = _stereo_noise(n, rng, lambda f, c=fc, qq=q: d.resonance(f, c, qq))
            out += noise * env[:, None]
        buffet = _stereo_noise(n, rng, lambda f: d.band(f, 18.0, 70.0, 2.0))
        out += buffet * (1.4 * strength * (0.5 + 0.5 * gust) ** 2)[:, None]
    return out


# ── Water lapping ─────────────────────────────────────────────────────────────

def water_lap(rng: np.random.Generator, n: int, laps_per_s: float = 2.2, wash: float = 0.06) -> np.ndarray:
    """Small waves lapping at pen floats and a hull on a calm day."""
    swell = d.lfo(n, rng, 0.08, 0.25)
    crest = (0.5 + 0.5 * swell) ** 2
    out = _stereo_noise(n, rng, lambda f: d.band(f, 140.0, 1700.0, 1.5) * d.pink(f) * 20.0)
    out *= (wash * (0.25 + 0.75 * crest))[:, None]
    count = int(laps_per_s * n / RATE)
    for _ in range(count):
        start = int(rng.integers(0, n))
        if rng.random() > 0.25 + 0.75 * crest[start]:
            continue
        gain = rng.uniform(0.35, 1.0)
        slap = _burst(rng, int(0.6 * RATE), rng.uniform(220, 420), rng.uniform(1800, 3200),
                      rng.uniform(0.006, 0.02), rng.uniform(0.05, 0.16))
        slosh = _burst(rng, int(0.6 * RATE), 70.0, rng.uniform(300, 450), rng.uniform(0.03, 0.07), rng.uniform(0.12, 0.25))
        mono = gain * (1.6 * slap + 0.5 * slosh)
        for _g in range(int(rng.integers(0, 3)) if rng.random() < 0.45 else 0):
            g = _glug(rng, rng.uniform(320, 900), rng.uniform(0.018, 0.04))
            at = int(rng.uniform(0.0, 0.08) * RATE)
            mono[at:at + len(g)] += 0.35 * gain * g[: max(0, len(mono) - at)]
        stereo = d.pan(mono, rng.uniform(-0.75, 0.75))
        d.place_circular(out[:, 0], start, stereo[:, 0])
        d.place_circular(out[:, 1], start, stereo[:, 1])
    ir = d.diffuse_ir(rng, 0.5, 0.12, 4000.0)
    wet = np.stack([d.circular_convolve(out[:, c], ir) for c in range(2)], axis=1)
    return out + 0.18 * wet


# ── Gulls ─────────────────────────────────────────────────────────────────────

def _gull_note(rng: np.random.Generator, dur: float, f_start: float, f_peak: float, f_end: float, harsh: float) -> np.ndarray:
    """One harmonic gull note: pitch rises to its peak in the first third, then
    falls; overtones shaped by two broad formants; a little rasp and breath."""
    m = int(dur * RATE)
    t = np.linspace(0.0, 1.0, m)
    rise = np.clip(t / 0.3, 0, 1)
    contour = np.where(t < 0.3, f_start + (f_peak - f_start) * np.sin(rise * math.pi / 2),
                       f_peak + (f_end - f_peak) * np.maximum((t - 0.3) / 0.7, 0.0) ** 0.8)
    contour = contour * (1.0 + 0.012 * d.lfo(m, rng, 8.0 / max(dur, 0.05), 30.0 / max(dur, 0.05)))
    phase = 2 * math.pi * np.cumsum(contour) / RATE
    voice = np.zeros(m)
    for h in range(1, 9):
        fh = contour * h
        weight = d.resonance(fh, 1250.0, 1.4) + 0.7 * d.resonance(fh, 2700.0, 1.6) + 0.15 * d.resonance(fh, 4300.0, 2.0)
        voice += weight * np.sin(h * phase) / h ** 0.35
    rasp = 1.0 - harsh * 0.5 * (1.0 + np.sin(2 * math.pi * rng.uniform(55, 85) * np.arange(m) / RATE))
    breath = _burst(rng, m, 2000.0, 5000.0, 0.01, dur) * 0.08
    env = np.clip(np.arange(m) / (0.012 * RATE), 0, 1) * np.clip((m - np.arange(m)) / (0.05 * RATE), 0, 1)
    return (voice * rasp + breath) * env


def _gull_bout(rng: np.random.Generator, pitch: float) -> np.ndarray:
    kind = rng.choice(["long", "kek", "mew"], p=[0.45, 0.3, 0.25])
    notes: list[tuple[float, np.ndarray]] = []
    if kind == "long":
        notes.append((0.0, 1.1 * _gull_note(rng, rng.uniform(0.33, 0.42), 900 * pitch, 1480 * pitch, 980 * pitch, 0.25)))
        at = notes[0][1].size / RATE + 0.12
        gap = rng.uniform(0.26, 0.32)
        for k in range(int(rng.integers(5, 10))):
            f = (1300 - 25 * k) * pitch
            notes.append((at, _gull_note(rng, rng.uniform(0.11, 0.16), 0.85 * f, f, 0.8 * f, 0.35)))
            at += gap
            gap *= 0.95
    elif kind == "kek":
        at = 0.0
        for _ in range(int(rng.integers(3, 7))):
            f = rng.uniform(1100, 1300) * pitch
            notes.append((at, 0.8 * _gull_note(rng, rng.uniform(0.06, 0.08), 0.95 * f, f, 0.9 * f, 0.5)))
            at += rng.uniform(0.15, 0.2)
    else:
        notes.append((0.0, 0.7 * _gull_note(rng, rng.uniform(0.38, 0.48), 1000 * pitch, 1260 * pitch, 880 * pitch, 0.1)))
    length = int((max(s for s, _ in notes) + 0.6) * RATE)
    out = np.zeros(length)
    for start, note in notes:
        i = int(start * RATE)
        out[i:i + len(note)] += note[: length - i]
    return out


def gulls(rng: np.random.Generator, n: int, bouts_per_min: float = 9.0, far: float = 1.0) -> np.ndarray:
    """Gulls over the fjord: a few birds calling now and then, near and far."""
    out = np.zeros((n, 2))
    birds = [(rng.uniform(0.86, 1.14), rng.uniform(30.0, 250.0) * far, rng.uniform(-0.85, 0.85)) for _ in range(5)]
    count = max(1, int(round(bouts_per_min * n / RATE / 60.0)))
    starts = np.sort(rng.uniform(0, n, count)).astype(int)
    for start in starts:
        pitch, dist, position = birds[int(rng.integers(0, len(birds)))]
        bout = _gull_bout(rng, pitch)
        cutoff = min(12000.0, 9000.0 * math.sqrt(30.0 / dist))
        b, a = signal.butter(2, cutoff / (RATE / 2), "low")
        bout = signal.lfilter(b, a, bout) * (30.0 / dist) ** 0.8
        stereo = d.pan(bout, float(np.clip(position + rng.uniform(-0.1, 0.1), -1, 1)))
        d.place_circular(out[:, 0], int(start), stereo[:, 0])
        d.place_circular(out[:, 1], int(start), stereo[:, 1])
    ir_l, ir_r = d.diffuse_ir(rng, 1.2, 0.28, 5000.0), d.diffuse_ir(rng, 1.2, 0.28, 5000.0)
    return out + 0.22 * np.stack([d.circular_convolve(out[:, 0], ir_l), d.circular_convolve(out[:, 1], ir_r)], axis=1)


# ── Diesel engines ────────────────────────────────────────────────────────────

def _firing_train(rng: np.random.Generator, n: int, firing_hz: float, cylinders: int) -> np.ndarray:
    period = RATE / firing_hz
    count = int(round(n / period))
    cyl_gain = 1.0 + 0.1 * rng.standard_normal(cylinders)
    train = np.zeros(n)
    for k in range(count):
        pos = int(round(k * period + rng.normal(0.0, 0.00025 * RATE))) % n
        train[pos] += cyl_gain[k % cylinders] * (1.0 + 0.05 * rng.standard_normal())
    return train


def _block_ir(modes: tuple[tuple[float, float, float], ...]) -> np.ndarray:
    m = int(0.06 * RATE)
    t = np.arange(m) / RATE
    return sum(level * np.sin(2 * math.pi * f * t) * np.exp(-t / decay) for f, level, decay in modes)


def diesel(rng: np.random.Generator, n: int, rpm: float, cylinders: int, modes: tuple, clatter: float) -> np.ndarray:
    """A four-stroke diesel at a steady speed (mono): firing pulses through the
    block and exhaust, a half-order roll, mechanical clatter, low rumble."""
    firing = rpm * cylinders / 120.0
    pulses = d.circular_convolve(_firing_train(rng, n, firing, cylinders), _block_ir(modes))
    half_order = rpm / 120.0
    t = np.arange(n) / RATE
    pulses *= 1.0 + 0.15 * np.sin(2 * math.pi * half_order * t)
    env = d.circular_convolve(np.abs(pulses), np.hanning(int(0.004 * RATE)))
    env /= max(np.max(env), 1e-9)
    knock = d.shaped_noise(n, rng, lambda f: d.band(f, 1200.0, 5000.0, 2.0)) * env * clatter
    rumble = d.shaped_noise(n, rng, lambda f: d.band(f, 28.0, 120.0, 2.0)) * 0.5
    mono = pulses / max(np.sqrt(np.mean(pulses ** 2)), 1e-9) + knock + rumble
    return mono


def feed_barge(rng: np.random.Generator, n: int) -> np.ndarray:
    """A feed barge from a pen nearby (mono, for a 3D emitter): its generator
    set (6 cylinders, 1500 rpm: 75 Hz firing), the feed blower (blade-pass
    580 Hz) and pellets rattling in the pipes in dosing rounds."""
    engine = diesel(rng, n, 1500.0, 6, ((170.0, 1.0, 0.012), (410.0, 0.6, 0.008), (950.0, 0.25, 0.005)), 0.35)
    t = np.arange(n) / RATE
    wobble = 1.0 + 0.002 * d.lfo(n, rng, 0.1, 0.5)
    phase = 2 * math.pi * np.cumsum(580.0 * wobble) / RATE
    blower = 0.18 * np.sin(phase) + 0.07 * np.sin(2 * phase + 0.4)
    blower += 0.55 * d.shaped_noise(n, rng, lambda f: d.band(f, 300.0, 3200.0, 1.5) * (0.4 + d.resonance(f, 820.0, 2.0)))
    dosing = np.clip(d.lfo(n, rng, 0.05, 0.12) * 1.6 + 0.3, 0.0, 1.0)
    clicks = np.zeros(n)
    for pos in rng.integers(0, n, int(70 * n / RATE)):
        clicks[pos] += rng.uniform(0.3, 1.0) * dosing[pos]
    tick = _burst(rng, int(0.003 * RATE), 2500.0, 8000.0, 0.0003, 0.0008)
    rattle = d.circular_convolve(clicks, tick) * 6.0
    mono = engine + 0.6 * blower * (0.85 + 0.15 * np.sin(2 * math.pi * 0.25 * t)) + rattle
    return d.circular_filter(mono, lambda f: d.band(f, 0.0, 4500.0, 2.0))[:, None]


def boat_engine(rng: np.random.Generator, n: int) -> np.ndarray:
    """A wellboat under way (mono, 3D emitter): a medium-speed main engine
    (8 cylinders, 900 rpm: 60 Hz firing), the propeller's blade rate
    (4 blades on a 200 rpm shaft: 13.3 Hz) and the bow wash."""
    engine = diesel(rng, n, 900.0, 8, ((120.0, 1.0, 0.016), (290.0, 0.55, 0.01), (640.0, 0.2, 0.006)), 0.2)
    blade_hz = 200.0 * 4 / 60.0
    t = np.arange(n) / RATE
    thump = d.shaped_noise(n, rng, lambda f: d.band(f, 25.0, 160.0, 2.0)) * (0.5 + 0.5 * np.cos(2 * math.pi * blade_hz * t)) ** 3
    wash = d.shaped_noise(n, rng, lambda f: d.band(f, 180.0, 2600.0, 1.5) * d.pink(f) * 15.0)
    wash *= 0.45 * (0.7 + 0.3 * d.lfo(n, rng, 0.1, 0.4))
    mono = engine + 0.9 * thump + wash
    return d.circular_filter(mono, lambda f: d.band(f, 0.0, 3800.0, 2.0))[:, None]


# ── Rain on water ─────────────────────────────────────────────────────────────

def _drop_templates(rng: np.random.Generator, large: bool, count: int) -> list[np.ndarray]:
    out = []
    for _ in range(count):
        click = _burst(rng, int(0.004 * RATE), 2500.0, 16000.0, 0.0002, rng.uniform(0.0006, 0.0015))
        if large:
            ring = _glug(rng, rng.uniform(2000, 7000), rng.uniform(0.004, 0.012), rise=1.15) * rng.uniform(0.4, 0.9)
        else:
            ring = _glug(rng, rng.uniform(9000, 15000), rng.uniform(0.002, 0.005), rise=1.05) * rng.uniform(0.05, 0.2)
        m = max(len(click), len(ring))
        tpl = np.zeros(m)
        tpl[: len(click)] += click
        tpl[: len(ring)] += ring
        out.append(tpl)
    return out


def rain(rng: np.random.Generator, n: int, drops_per_s: float, large_share: float, wash: float) -> np.ndarray:
    """Rain on the fjord: drops on the water (stereo) over a hiss bed."""
    intensity = 0.8 + 0.2 * d.lfo(n, rng, 0.03, 0.12)
    out = np.zeros((n, 2))
    for large, share in ((False, 1.0 - large_share), (True, large_share)):
        templates = _drop_templates(rng, large, 24)
        count = int(drops_per_s * share * n / RATE)
        which = rng.integers(0, len(templates), count)
        pos = rng.integers(0, n, count)
        size = np.minimum(rng.pareto(3.0, count) + 0.4, 3.0)
        side = rng.uniform(0, 1, count)
        for k, tpl in enumerate(templates):
            sel = which == k
            for ch, w in ((0, np.sqrt(1 - side[sel])), (1, np.sqrt(side[sel]))):
                train = np.zeros(n)
                np.add.at(train, pos[sel], size[sel] * w * intensity[pos[sel]])
                out[:, ch] += d.circular_convolve(train, tpl)
    hiss = _stereo_noise(n, rng, lambda f: d.band(f, 1800.0, 14000.0, 1.5))
    splash = _stereo_noise(n, rng, lambda f: d.band(f, 350.0, 2200.0, 1.5))
    rms = max(np.sqrt(np.mean(out ** 2)), 1e-9)
    bed = (0.5 * hiss + wash * splash) * rms * intensity[:, None]
    return out + bed


# ── Composites ────────────────────────────────────────────────────────────────

def storm_sea(rng: np.random.Generator, n: int) -> np.ndarray:
    """A storm at the sites: breaking waves every 6-11 s, the sea's roar and a gale."""
    out = 0.8 * wind(rng, n, 1.0)
    roar = _stereo_noise(n, rng, lambda f: d.band(f, 40.0, 650.0, 1.5) * d.pink(f) * 10.0)
    out += roar * (0.6 + 0.4 * d.lfo(n, rng, 0.05, 0.2))[:, None]
    at = 0.0
    while at < n / RATE - 1.0:
        start = int(at * RATE)
        m = int(rng.uniform(3.5, 5.0) * RATE)
        rise, fall = rng.uniform(0.35, 0.8), rng.uniform(1.4, 2.8)
        t = np.arange(m) / RATE
        env = np.clip(t / rise, 0, 1) ** 2 * np.exp(-np.maximum(t - rise, 0) / fall)
        body = d.shaped_noise(m, rng, lambda f: d.band(f, 80.0, 3500.0, 1.5) * d.pink(f) * 10.0) * env
        foam = d.shaped_noise(m, rng, lambda f: d.band(f, 2000.0, 9000.0, 1.5)) * np.exp(-np.maximum(t - rise, 0) / (fall * 1.4)) * np.clip(t / rise, 0, 1)
        tail = np.clip((m - np.arange(m)) / (0.35 * m), 0.0, 1.0) ** 2
        crash = rng.uniform(0.7, 1.3) * (1.6 * body + 0.35 * foam) * tail
        stereo = d.pan(crash, rng.uniform(-0.6, 0.6))
        d.place_circular(out[:, 0], start, stereo[:, 0])
        d.place_circular(out[:, 1], start, stereo[:, 1])
        at += rng.uniform(6.0, 11.0)
    return out


def _creak(rng: np.random.Generator) -> np.ndarray:
    """A mooring line or fender working: a rough friction tone gliding in pitch."""
    dur = rng.uniform(0.3, 0.8)
    m = int(dur * RATE)
    t = np.linspace(0, 1, m)
    f = rng.uniform(120, 260) * (1.0 + 0.35 * np.sin(math.pi * t) * rng.choice([-1, 1]))
    phase = 2 * math.pi * np.cumsum(f * (1 + 0.04 * rng.standard_normal(m))) / RATE
    saw = sum(np.sin(k * phase) / k for k in range(1, 12))
    b, a = signal.butter(2, [300 / (RATE / 2), 3000 / (RATE / 2)], "band")
    return signal.lfilter(b, a, saw) * np.sin(math.pi * t) ** 1.5


def harbour(rng: np.random.Generator, n: int) -> np.ndarray:
    """At the quay: water at the pilings, a boat idling across the harbour,
    mooring lines working, a gull now and then."""
    out = water_lap(rng, n, laps_per_s=1.6, wash=0.1)
    ref = np.sqrt(np.mean(out ** 2))
    idle = d.circular_filter(boat_engine(rng, n)[:, 0], lambda f: d.band(f, 0.0, 700.0, 2.0))
    idle *= 0.35 * ref / max(np.sqrt(np.mean(idle ** 2)), 1e-9)
    out += np.stack([idle, np.roll(idle, int(0.012 * RATE))], axis=1)
    at = rng.uniform(1.0, 4.0)
    while at < n / RATE - 1.0:
        creak = _creak(rng)
        creak *= ref * rng.uniform(1.0, 2.0) / max(np.max(np.abs(creak)), 1e-9)
        stereo = d.pan(creak, rng.uniform(-0.7, 0.7))
        d.place_circular(out[:, 0], int(at * RATE), stereo[:, 0])
        d.place_circular(out[:, 1], int(at * RATE), stereo[:, 1])
        at += rng.uniform(5.0, 12.0)
    birds = gulls(rng, n, bouts_per_min=3.0, far=1.8)
    birds *= 0.25 * np.sqrt(np.mean(out ** 2)) / max(np.sqrt(np.mean(birds ** 2)), 1e-9)
    return out + birds


def underwater(rng: np.random.Generator, n: int) -> np.ndarray:
    """Below the surface: a low rumble on the swell, the muffled wash above,
    bubble streams now and then, a far engine's hum."""
    swell = 0.6 + 0.4 * d.lfo(n, rng, 0.06, 0.2)
    out = _stereo_noise(n, rng, lambda f: d.band(f, 18.0, 240.0, 2.0)) * swell[:, None]
    out += 0.3 * _stereo_noise(n, rng, lambda f: d.band(f, 90.0, 650.0, 2.0)) * swell[:, None]
    t = np.arange(n) / RATE
    out += (0.04 * (np.sin(2 * math.pi * 60.0 * t) + 0.5 * np.sin(2 * math.pi * 120.0 * t)))[:, None]
    at = rng.uniform(0.5, 3.0)
    while at < n / RATE - 1.0:
        stream = np.zeros(int(1.6 * RATE))
        for _ in range(int(rng.integers(5, 15))):
            g = _glug(rng, rng.uniform(450, 1500), rng.uniform(0.015, 0.035), rise=1.4)
            i = int(rng.uniform(0, 1.2) * RATE)
            stream[i:i + len(g)] += g[: len(stream) - i] * rng.uniform(0.3, 1.0)
        stereo = d.pan(0.35 * stream, rng.uniform(-0.6, 0.6))
        d.place_circular(out[:, 0], int(at * RATE), stereo[:, 0])
        d.place_circular(out[:, 1], int(at * RATE), stereo[:, 1])
        at += rng.uniform(4.0, 10.0)
    return out
