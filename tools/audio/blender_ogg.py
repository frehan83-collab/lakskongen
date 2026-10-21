"""Ogg Vorbis encoder for tools/audio/gen_audio.py, run inside Blender.

The repository's machines have no stand-alone Ogg encoder (no ffmpeg, no
oggenc); Blender 5.2's Audaspace module (`aud`, built on FFmpeg's libvorbis)
encodes and decodes Ogg Vorbis. This script is the only part of the audio
pipeline that needs Blender:

    Blender --background --factory-startup --python-exit-code 1 \
        --python tools/audio/blender_ogg.py -- jobs.json

jobs.json: [{"wav": in.wav, "ogg": out.ogg, "channels": 1|2, "bitrate": bps,
             "decoded": check.wav}, ...]; a job without "wav" only decodes
its "ogg" (a shipped file measured). Each job encodes the WAV to Ogg
Vorbis and decodes the Ogg back to a 16-bit WAV (at 44.1 kHz), so the caller measures the
level of what the game will actually play. The Ogg stream's serial number is
random in FFmpeg's muxer; gen_audio.py rewrites it to a fixed value so a
rebuild is byte-identical.
"""

import json
import sys
import wave

import aud  # noqa: E402  (Blender's bundled Audaspace)
import numpy as np  # noqa: E402  (Blender's bundled numpy)


def write_wav16(path: str, frames: "np.ndarray") -> None:
    """The decoded float frames x channels as a 16-bit WAV (Python's wave module:
    Audaspace's own WAV writer fails on this build)."""
    pcm = np.clip(np.round(frames * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(frames.shape[1])
        w.setsampwidth(2)
        w.setframerate(44100)
        w.writeframes(pcm.tobytes())


def main() -> int:
    jobs_path = sys.argv[sys.argv.index("--") + 1]
    with open(jobs_path, encoding="utf-8") as f:
        jobs = json.load(f)
    for job in jobs:
        channels = aud.CHANNELS_STEREO if int(job["channels"]) == 2 else aud.CHANNELS_MONO
        if job.get("wav"):
            sound = aud.Sound(job["wav"])
            sound.write(job["ogg"], rate=44100, channels=channels, format=aud.FORMAT_FLOAT32,
                        container=aud.CONTAINER_OGG, codec=aud.CODEC_VORBIS, bitrate=int(job["bitrate"]))
        write_wav16(job["decoded"], aud.Sound(job["ogg"]).data())
        print("blender_ogg: done", job["ogg"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
