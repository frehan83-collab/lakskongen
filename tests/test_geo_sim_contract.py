"""The sim side of the geo data (geo stages 2-3, track A; docs/geo/STAGE23_PLAN.md).

A1 (the embedded readers): LakskongenSim.csproj embeds every geo file the
readers in src/LakskongenSim/Environment/Geo name, under the LogicalName the
reader asks for, through explicit names or extension-specific globs only (never
`*.*`, which would also embed the Godot .import sidecars beside the PNGs), and
every embedded file is a manifest-listed file under godot/data/geo. The bridge
lane checks join this file in A2.
"""

from __future__ import annotations

import fnmatch
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CSPROJ = REPO / "src" / "LakskongenSim" / "LakskongenSim.csproj"
SIM_DIR = CSPROJ.parent
GEO = REPO / "godot" / "data" / "geo"
GEO_SRC = SIM_DIR / "Environment" / "Geo"
RESOURCES_CS = GEO_SRC / "GeoResources.cs"


def _geo_item_group() -> ET.Element:
    root = ET.parse(CSPROJ).getroot()
    groups = [g for g in root.iter("ItemGroup") if g.get("Label") == "geo"]
    assert len(groups) == 1, "LakskongenSim.csproj needs exactly one ItemGroup Label=\"geo\""
    return groups[0]


def _embedded() -> dict[str, Path]:
    """LogicalName -> source file for every EmbeddedResource of the geo group, globs expanded."""
    out: dict[str, Path] = {}
    for item in _geo_item_group().iter("EmbeddedResource"):
        include = item.get("Include", "").replace("\\", "/")
        logical = item.get("LogicalName", "")
        directory = (SIM_DIR / Path(include).parent).resolve()
        pattern = Path(include).name
        files = sorted(p for p in directory.iterdir() if p.is_file() and fnmatch.fnmatchcase(p.name, pattern))
        assert files, f"{include} matches no file"
        for path in files:
            name = (logical.replace("%(Filename)", path.stem).replace("%(Extension)", path.suffix))
            assert name not in out, f"{name} is embedded twice"
            out[name] = path
    return out


def _reader_names() -> set[str]:
    """The logical names GeoResources.cs asks for: its constants and the 13 grid names."""
    text = RESOURCES_CS.read_text(encoding="utf-8")
    prefix = re.search(r'const string Prefix = "([^"]+)";', text).group(1)
    names = {prefix + m for m in re.findall(r'= Prefix \+ "([^"]+)";', text)}
    grid = re.search(r'\$"\{Prefix\}(Suitability\.po)\{productionArea:00\}(\.png)"', text)
    assert grid, "GeoResources.SuitabilityGrid's name format changed"
    names |= {f"{prefix}{grid.group(1)}{po:02d}{grid.group(2)}" for po in range(1, 14)}
    return names


def test_the_geo_group_uses_explicit_names_or_extension_globs_only():
    for item in _geo_item_group().iter("EmbeddedResource"):
        include = item.get("Include", "")
        name = Path(include.replace("\\", "/")).name
        assert "*.*" not in include and name != "*", f"{include}: never *.* or a bare * under data/geo"
        assert "**" not in include, f"{include}: no recursive glob under data/geo"
        if "*" in name:
            assert re.fullmatch(r"[\w.-]*\*\.(png|json|geojson)", name), f"{include}: glob must fix the extension"
        assert item.get("LogicalName", "").startswith("LakskongenSim.Geo."), include
    text = CSPROJ.read_text(encoding="utf-8")
    assert not re.search(r"data\\geo\\[^\"]*\*\.\*", text), "no *.* glob under data/geo anywhere in the csproj"


def test_the_csproj_embeds_every_file_the_readers_name():
    embedded = _embedded()
    assert set(embedded) == _reader_names()
    assert len(embedded) == 18
    assert not any(p.suffix == ".import" for p in embedded.values())


def test_every_embedded_geo_file_is_a_manifest_file():
    files = json.loads((GEO / "manifest.json").read_text(encoding="utf-8"))["files"]
    for name, path in _embedded().items():
        rel = path.relative_to(GEO.resolve()).as_posix()
        assert rel in files, f"{name}: {rel} is not in manifest.json"


def test_the_geo_readers_never_open_a_file():
    """The engine never touches the file system (GEODATA §7.2): no File/Directory/
    Path/FileStream/StreamReader call in the readers (the C# GeoNoFileSystemTests
    scan is syntax-aware; this one guards the Python CI job)."""
    pattern = re.compile(r"\b(File|Directory|Path|FileStream|FileInfo|StreamReader|StreamWriter)\s*[.(]")
    hits = []
    for path in sorted(GEO_SRC.glob("*.cs")):
        for no, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            code = line.split("//", 1)[0]
            if pattern.search(code):
                hits.append(f"{path.relative_to(REPO)}:{no}: {line.strip()}")
    assert not hits, hits
