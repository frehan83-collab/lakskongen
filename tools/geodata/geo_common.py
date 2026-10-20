"""Shared helpers for the geo pipeline: paths, HTTP via curl, hashing, JSON output.

The pipeline is offline-first: geo_fetch.py fills a cache outside the repo
(~/.cache/lakskongen-geo, or $LAKSKONGEN_GEO_CACHE) and records each file's
sha256 in tools/geodata/fetch_manifest.json; every build step reads only the
cache and verifies those hashes, so the same cache always builds the same bytes.
"""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import zlib
from pathlib import Path
from typing import Any, Iterable

REPO = Path(__file__).resolve().parents[2]
TOOLS = REPO / "tools" / "geodata"
OUT = REPO / "godot" / "data" / "geo"
DOCS_EVIDENCE = REPO / "docs" / "run" / "evidence" / "geo"
FETCH_MANIFEST = TOOLS / "fetch_manifest.json"
CACHE = Path(os.environ.get("LAKSKONGEN_GEO_CACHE", Path.home() / ".cache" / "lakskongen-geo"))
RAW = CACHE / "raw"
WORK = CACHE / "work"

# Display coordinates are lon/lat (EPSG:4326); distances and grids use UTM 33N
# on ETRS89 (EPSG:25833), Norway's national map projection.
CRS_LONLAT = "EPSG:4326"
CRS_UTM33 = "EPSG:25833"

# Coordinate rounding in the published files: 3 decimals (~100 m) for the
# national level of detail, 4 decimals (~10 m) for the regional one; points
# and site polygons keep 5 decimals (~1 m).
LONLAT_DECIMALS = {"national": 3, "regional": 4}

# Douglas-Peucker tolerances (metres, in EPSG:25833) per level of detail.
SIMPLIFY_M = {"national": 1500.0, "regional": 120.0}


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def curl_download(url: str, dest: Path, timeout_s: int = 900) -> None:
    """Download with curl (system trust store, retries). Raises on HTTP errors."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    cmd = ["curl", "-sS", "-f", "-L", "--retry", "6", "--retry-delay", "5", "--retry-all-errors",
           "-m", str(timeout_s), "-o", str(tmp), url]
    subprocess.run(cmd, check=True)
    tmp.replace(dest)


def curl_text(url: str, timeout_s: int = 120) -> str:
    out = subprocess.run(["curl", "-sS", "-f", "-L", "--retry", "4", "-m", str(timeout_s), url],
                         check=True, capture_output=True)
    return out.stdout.decode("utf-8")


def load_fetch_manifest() -> dict:
    if not FETCH_MANIFEST.exists():
        return {}
    return json.loads(FETCH_MANIFEST.read_text(encoding="utf-8"))


def raw_files(source_id: str, verify: bool = True) -> list[Path]:
    """The cached files of a fetched source, sha256-checked against the manifest."""
    entry = load_fetch_manifest().get(source_id)
    if entry is None:
        raise SystemExit(f"{source_id}: not fetched; run tools/geodata/geo_fetch.py first")
    paths = []
    for f in entry["files"]:
        p = RAW / source_id / f["name"]
        if not p.exists():
            raise SystemExit(f"{source_id}: {p} missing from the cache; re-run geo_fetch.py")
        if verify and sha256_file(p) != f["sha256"]:
            raise SystemExit(f"{source_id}: {p.name} does not match its recorded sha256")
        paths.append(p)
    return paths


def write_json(path: Path, data: Any, compact: bool = True) -> int:
    """Write JSON deterministically (sorted keys, UTF-8, trailing newline). Returns bytes."""
    path.parent.mkdir(parents=True, exist_ok=True)
    if compact:
        text = json.dumps(data, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    else:
        text = json.dumps(data, ensure_ascii=False, sort_keys=True, indent=1)
    raw = (text + "\n").encode("utf-8")
    path.write_bytes(raw)
    return len(raw)


def canonical_hash(data: Any) -> str:
    """sha256 of the canonical JSON form (sorted keys, compact separators)."""
    text = json.dumps(data, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def round_coords(coords: Any, decimals: int) -> Any:
    """Round nested coordinate arrays; -0.0 becomes 0.0 so output bytes are stable."""
    if isinstance(coords, (int, float)):
        v = round(float(coords), decimals)
        return 0.0 if v == 0 else v
    return [round_coords(c, decimals) for c in coords]


def write_png(path: Path, width: int, height: int, rows: Iterable[bytes], color_type: int) -> int:
    """Minimal deterministic PNG writer (8-bit, no interlace, filter 0, zlib level 9).

    color_type 0 = grayscale (1 byte/px), 4 = grayscale+alpha (2 bytes/px),
    2 = RGB (3 bytes/px). Pure stdlib so tests can decode what this encodes.
    """
    import struct

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    raw = b"".join(b"\x00" + bytes(r) for r in rows)
    ihdr = struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
           + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)
    return len(png)
