"""Signal helpers for the game's generated audio (roadmap Au2-01).

Everything here is deterministic (seeded numpy generators, FFT filtering) and
needs only numpy and scipy (requirements-dev.txt).

* Periodic building blocks: a loop is built so that its last sample runs on
  into its first. Noise is coloured in the frequency domain (circular), slow
  modulations are sums of whole cycles per loop, and events (a gull's call, a
  drop, a lap) are placed on a circle, so a 60 s loop has no seam at all.
* Loudness: ITU-R BS.1770 (K-weighting, 400 ms blocks, the -70 LUFS absolute
  and -10 LU relative gates) as EBU R 128 uses it, momentary maximum (400 ms)
  and true peak (4x oversampling), so every file's level is measured, not
  guessed (nobody listens to these in the pipeline).

Sources: EBU R 128 v5.0 (2023), https://tech.ebu.ch/publications/r128 (the
gating, -1 dBTP); ITU-R BS.1770 K-weighting filter (the shelf at
1681.97 Hz, +4.0 dB, and the RLB high-pass at 38.14 Hz) as specified there.
"""

from __future__ import annotations

import math
import wave
from pathlib import Path

import numpy as np
from scipy import signal

RATE = 44_100


# ── Loudness (ITU-R BS.1770 / EBU R 128) ──────────────────────────────────────

## The K-weighting's analogue parameters, from which the BS.1770 48 kHz
## coefficients are reproduced at any rate (B. De Man's derivation of the
## standard's filter: at 48 kHz this gives the published b and a).
_SHELF_F0, _SHELF_G, _SHELF_Q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
_SHELF_VB_EXP = 0.4996667741545416
_HP_F0, _HP_Q = 38.13547087602444, 0.5003270373238773


def _biquad_high_shelf(fs: float, f0: float, gain_db: float, q: float) -> tuple[np.ndarray, np.ndarray]:
    k = math.tan(math.pi * f0 / fs)
    vh = 10.0 ** (gain_db / 20.0)
    vb = vh ** _SHELF_VB_EXP
    a0 = 1.0 + k / q + k * k
    b = np.array([(vh + vb * k / q + k * k) / a0, 2.0 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0])
    a = np.array([1.0, 2.0 * (k * k - 1.0) / a0, (1.0 - k / q + k * k) / a0])
    return b, a


def _biquad_high_pass(fs: float, f0: float, q: float) -> tuple[np.ndarray, np.ndarray]:
    k = math.tan(math.pi * f0 / fs)
    a0 = 1.0 + k / q + k * k
    return np.array([1.0, -2.0, 1.0]), np.array([1.0, 2.0 * (k * k - 1.0) / a0, (1.0 - k / q + k * k) / a0])


def k_weight(x: np.ndarray, fs: int = RATE) -> np.ndarray:
    """The BS.1770 K-weighting (pre-filter shelf, then the RLB high-pass), per channel."""
    b1, a1 = _biquad_high_shelf(fs, _SHELF_F0, _SHELF_G, _SHELF_Q)
    b2, a2 = _biquad_high_pass(fs, _HP_F0, _HP_Q)
    y = signal.lfilter(b1, a1, x, axis=0)
    return signal.lfilter(b2, a2, y, axis=0)


def _block_powers(x: np.ndarray, fs: int, block_s: float = 0.4, overlap: float = 0.75) -> np.ndarray:
    """Mean-square power of every 400 ms block (75 % overlap), summed over channels."""
    y = k_weight(x if x.ndim == 2 else x[:, None], fs)
    n = int(round(block_s * fs))
    hop = int(round(n * (1.0 - overlap)))
    if len(y) < n:
        return np.array([np.sum(np.mean(y * y, axis=0))])
    starts = range(0, len(y) - n + 1, hop)
    return np.array([np.sum(np.mean(y[s:s + n] ** 2, axis=0)) for s in starts])


def _lufs(power: float | np.ndarray) -> float | np.ndarray:
    return -0.691 + 10.0 * np.log10(np.maximum(power, 1e-20))


def integrated_lufs(x: np.ndarray, fs: int = RATE) -> float:
    """Integrated loudness (gated), LUFS."""
    p = _block_powers(x, fs)
    p = p[_lufs(p) > -70.0]
    if p.size == 0:
        return -70.0
    relative = _lufs(np.mean(p)) - 10.0
    p = p[_lufs(p) > relative]
    return float(_lufs(np.mean(p))) if p.size else -70.0


def momentary_max_lufs(x: np.ndarray, fs: int = RATE) -> float:
    """The loudest 400 ms block, LUFS (how loud a short cue reads)."""
    return float(np.max(_lufs(_block_powers(x, fs))))


def true_peak_dbtp(x: np.ndarray) -> float:
    """True peak by 4x oversampling (EBU R 128's method), dBTP."""
    y = signal.resample_poly(x if x.ndim == 2 else x[:, None], 4, 1, axis=0)
    return float(20.0 * np.log10(max(np.max(np.abs(y)), 1e-12)))


def sample_peak_dbfs(x: np.ndarray) -> float:
    return float(20.0 * np.log10(max(np.max(np.abs(x)), 1e-12)))


def measure(x: np.ndarray, fs: int = RATE) -> dict:
    return {
        "integrated_lufs": round(integrated_lufs(x, fs), 1),
        "momentary_max_lufs": round(momentary_max_lufs(x, fs), 1),
        "true_peak_dbtp": round(true_peak_dbtp(x), 1),
    }


def normalise(x: np.ndarray, target_lufs: float, ceiling_dbtp: float, *, momentary: bool = False) -> np.ndarray:
    """Scale to a loudness (integrated, or the momentary maximum for a short cue),
    never above the true-peak ceiling. Gain only: nothing is compressed."""
    now = momentary_max_lufs(x) if momentary else integrated_lufs(x)
    gain_db = target_lufs - now
    peak_room = ceiling_dbtp - true_peak_dbtp(x)
    return x * 10.0 ** (min(gain_db, peak_room) / 20.0)


# ── Periodic building blocks ──────────────────────────────────────────────────

def freqs(n: int, fs: int = RATE) -> np.ndarray:
    return np.fft.rfftfreq(n, 1.0 / fs)


def shaped_noise(n: int, rng: np.random.Generator, response) -> np.ndarray:
    """White noise coloured by a magnitude response(f) in the frequency domain:
    exactly periodic in n (circular), unit RMS."""
    spec = np.fft.rfft(rng.standard_normal(n))
    spec *= response(freqs(n))
    spec[0] = 0.0
    y = np.fft.irfft(spec, n)
    return y / max(np.sqrt(np.mean(y * y)), 1e-12)


def band(f: np.ndarray, lo: float, hi: float, order: float = 2.0) -> np.ndarray:
    """A smooth band-pass magnitude (Butterworth-like skirts)."""
    hp = 1.0 / np.sqrt(1.0 + (np.maximum(lo, 1e-3) / np.maximum(f, 1e-3)) ** (2 * order)) if lo > 0 else 1.0
    lp = 1.0 / np.sqrt(1.0 + (f / hi) ** (2 * order)) if hi > 0 else 1.0
    return hp * lp


def pink(f: np.ndarray) -> np.ndarray:
    return 1.0 / np.sqrt(np.maximum(f, 20.0))


def resonance(f: np.ndarray, f0: float, q: float) -> np.ndarray:
    """A resonant peak (magnitude of a 2nd-order band-pass, 1 at f0)."""
    x = f / f0
    return 1.0 / np.sqrt(1.0 + q * q * (x - 1.0 / np.maximum(x, 1e-6)) ** 2)


def lfo(n: int, rng: np.random.Generator, lo_hz: float, hi_hz: float, fs: int = RATE) -> np.ndarray:
    """A smooth, periodic random curve in [-1, 1] with whole cycles per loop
    between lo_hz and hi_hz (gusts, swells)."""
    dur = n / fs
    k_lo = max(1, int(round(lo_hz * dur)))
    k_hi = max(k_lo, int(round(hi_hz * dur)))
    t = np.arange(n) / n
    y = np.zeros(n)
    for k in range(k_lo, k_hi + 1):
        amp = 1.0 / math.sqrt(k)
        y += amp * rng.standard_normal() * np.cos(2 * math.pi * k * t + rng.uniform(0, 2 * math.pi))
    peak = np.max(np.abs(y))
    return y / peak if peak > 0 else y


def place_circular(buf: np.ndarray, start: int, sig: np.ndarray) -> None:
    """Add sig into buf starting at sample `start`, wrapping round the end
    (an event near the loop's end continues at its start)."""
    n = len(buf)
    idx = (start + np.arange(len(sig))) % n
    np.add.at(buf, idx, sig)


def circular_convolve(x: np.ndarray, ir: np.ndarray) -> np.ndarray:
    """Periodic convolution (an effect tail wraps into the loop's start)."""
    n = len(x)
    h = np.zeros(n)
    m = min(len(ir), n)
    h[:m] = ir[:m]
    return np.fft.irfft(np.fft.rfft(x) * np.fft.rfft(h), n)


def circular_filter(x: np.ndarray, response) -> np.ndarray:
    """Filter one channel by a magnitude response(f), periodically (zero phase)."""
    n = len(x)
    return np.fft.irfft(np.fft.rfft(x) * response(freqs(n)), n)


def decay_env(n: int, attack_s: float, decay_s: float, fs: int = RATE) -> np.ndarray:
    t = np.arange(n) / fs
    return np.clip(t / max(attack_s, 1e-5), 0.0, 1.0) * np.exp(-t / max(decay_s, 1e-5))


def pan(mono: np.ndarray, position: float) -> np.ndarray:
    """Equal-power pan: -1 left, +1 right."""
    a = (position + 1.0) * math.pi / 4.0
    return np.stack([mono * math.cos(a), mono * math.sin(a)], axis=1)


def diffuse_ir(rng: np.random.Generator, length_s: float, decay_s: float, dark_hz: float, fs: int = RATE) -> np.ndarray:
    """A soft outdoor or small-room tail: decaying noise, darkened, unit energy."""
    n = int(length_s * fs)
    ir = rng.standard_normal(n) * np.exp(-np.arange(n) / fs / decay_s)
    b, a = signal.butter(2, dark_hz / (fs / 2), "low")
    ir = signal.lfilter(b, a, ir)
    return ir / np.sqrt(np.sum(ir * ir))


# ── Files ─────────────────────────────────────────────────────────────────────

def write_wav16(path: Path, stereo: np.ndarray, fs: int = RATE) -> None:
    """16-bit PCM WAV (channels from the array's second axis)."""
    data = stereo if stereo.ndim == 2 else stereo[:, None]
    pcm = np.clip(np.round(data * 32767.0), -32768, 32767).astype("<i2")
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(data.shape[1])
        w.setsampwidth(2)
        w.setframerate(fs)
        w.writeframes(pcm.tobytes())


def read_wav(path: Path) -> tuple[np.ndarray, int]:
    """A 16-bit or 32-bit-float PCM WAV as float frames x channels."""
    with wave.open(str(path), "rb") as w:
        ch, width, fs, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if width == 2:
        x = np.frombuffer(raw, "<i2").astype(np.float64) / 32768.0
    elif width == 3:
        b = np.frombuffer(raw, np.uint8).reshape(-1, 3)
        v = (b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16))
        v = np.where(v & 0x800000, v - 0x1000000, v)
        x = v.astype(np.float64) / 8388608.0
    elif width == 1:
        x = (np.frombuffer(raw, np.uint8).astype(np.float64) - 128.0) / 128.0
    else:
        raise ValueError(f"{path}: unsupported sample width {width}")
    return x.reshape(-1, ch), fs
