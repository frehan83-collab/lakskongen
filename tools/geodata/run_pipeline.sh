#!/usr/bin/env bash
# Rebuild godot/data/geo from the pinned open sources (docs/geo/GEODATA.md).
#
#   bash tools/geodata/run_pipeline.sh               fetch missing sources, build, previews
#   bash tools/geodata/run_pipeline.sh --build-only  build from the cache only (no network)
#
# Environment:
#   GEO_VENV           reuse a venv (default: a new one under mktemp -d, outside the repo)
#   PYTHON             interpreter for a new venv (default: python3.12, else python3)
#   COPERNICUS_PYTHON  Python of the Copernicus Marine Toolbox venv; when set, the
#                      optional Copernicus step runs (fetch once, then reduce)
#   LAKSKONGEN_GEO_CACHE  raw/work cache (default: ~/.cache/lakskongen-geo)
# Raw data never enters the repo; tools/geodata/fetch_manifest.json records it.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
BUILD_ONLY=0
[[ "${1:-}" == "--build-only" ]] && BUILD_ONLY=1

PY_BIN="${PYTHON:-$(command -v python3.12 || command -v python3)}"
VENV="${GEO_VENV:-$(mktemp -d)/geo-venv}"
if [[ ! -x "$VENV/bin/python" ]]; then
  "$PY_BIN" -m venv "$VENV"
  "$VENV/bin/pip" install -q --upgrade pip
  "$VENV/bin/pip" install -q -r "$HERE/requirements.txt"
fi
PY="$VENV/bin/python"
cd "$HERE"

if [[ $BUILD_ONLY -eq 0 ]]; then
  "$PY" geo_fetch.py
fi
if [[ -n "${COPERNICUS_PYTHON:-}" ]]; then
  "$COPERNICUS_PYTHON" fetch_copernicus.py
fi

"$PY" build_grids.py
"$PY" build_layers.py
"$PY" build_suitability.py
"$PY" build_basemap.py
"$PY" build_manifest.py
"$PY" render_previews.py
echo "geo pipeline done: godot/data/geo (previews in docs/run/evidence/geo)"
