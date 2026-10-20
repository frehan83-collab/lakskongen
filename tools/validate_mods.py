#!/usr/bin/env python3
"""
Mod validation and cross-reference checker for Lakskongen Phase 4.6.

Validates:
1. All mod JSON files parse correctly
2. Mod manifests reference existing files
3. Mod data follows the ModLoader prefix-dispatch convention
4. Cross-references between mods are valid (no dangling dependencies,
   scenario initial_events reference known event IDs)
5. Mod IDs are unique across all mods

The naming/enum conventions here mirror src/LakskongenSim/Core/ModLoader.cs
and src/LakskongenSim/Data/Enums.cs exactly — this script is the CI guard
that keeps mod files and the loader in lockstep.

Usage:
    python3 tools/validate_mods.py [mods_directory]

Exit codes:
    0 — all mods valid (or no mods present)
    1 — validation errors found
"""

import json
import os
import re
import sys
from pathlib import Path

# ── Conventions mirrored from ModLoader.cs / Enums.cs ─────────────────────────

# ModLoader matches by filename PREFIX via StartsWith (lowercased), so the
# prefix is the type tag and everything after is free-form (e.g. "events.json",
# "events_winter_storm.json", "scenario_fjord_pioneer.json").
FILE_TYPE_PREFIXES = ("events", "research", "fish", "scenario")

# EventCategory enum (Events/EventTypes.cs)
EVENT_CATEGORIES = {
    "Weather", "Disease", "Market", "Regulation", "Equipment", "Community",
    "Investor", "Competitor", "PlanktonBloom", "PlanktonCrash", "Other",
}

# ResearchBranch enum (Core/ResearchSystem.cs)
RESEARCH_BRANCHES = {
    "Biology", "Technology", "Sustainability", "Processing", "Expansion",
    "Genetics", "Automation", "Environment", "Health", "Nutrition",
}

# ProductionRegion enum (Data/Enums.cs)
PRODUCTION_REGIONS = {
    "Nordland", "Troms", "Finnmark", "Trøndelag", "Vestland", "Rogaland",
    "MøreOgRomsdal", "Sørlandet",
}

# WinCondition enum (Data/Enums.cs)
WIN_CONDITIONS = {"Tutorial", "Profit", "Sustainability", "Survival", "Competition"}

# ModLoader only requires id/name/author non-empty; version defaults. Keep the
# same leniency here — a mod must not fail CI for a cosmetic ID choice.
ID_PATTERN = re.compile(r"^[A-Za-z][A-Za-z0-9_\-]*$")
SEMVER_PATTERN = re.compile(r"^\d+\.\d+\.\d+$")


class ValidationError:
    def __init__(self, mod_id: str, message: str, severity: str = "error"):
        self.mod_id = mod_id
        self.message = message
        self.severity = severity

    def __str__(self) -> str:
        return f"[{self.severity.upper()}] {self.mod_id}: {self.message}"


def find_mods_directory() -> Path | None:
    """Probe common mod directory locations."""
    candidates = [
        Path("godot/mods"),
        Path("mods"),
        Path(__file__).parent.parent / "godot" / "mods",
        Path(__file__).parent.parent / "mods",
    ]
    for candidate in candidates:
        if candidate.is_dir():
            return candidate
    return None


def validate_manifest(manifest: dict, mod_path: Path, all_mod_ids: set[str]) -> list[ValidationError]:
    """Validate a mod manifest against the ModManifest contract."""
    errors = []
    mod_id = manifest.get("id", "<unknown>")

    for field in ("id", "name", "author"):
        if not manifest.get(field):
            errors.append(ValidationError(mod_id, f"Missing required field: {field}"))

    mod_id_val = manifest.get("id", "")
    if mod_id_val and not ID_PATTERN.match(mod_id_val):
        errors.append(ValidationError(
            mod_id,
            f"Invalid ID '{mod_id_val}' — use letters/digits/underscore/hyphen, no spaces"))

    version = manifest.get("version", "")
    if version and not SEMVER_PATTERN.match(version):
        errors.append(ValidationError(mod_id, f"Invalid version '{version}' — must be MAJOR.MINOR.PATCH"))

    for file_path in manifest.get("files", []):
        file_name = os.path.basename(file_path).lower()
        if not any(file_name.startswith(p) for p in FILE_TYPE_PREFIXES):
            errors.append(ValidationError(
                mod_id,
                f"File '{file_path}' has no recognised type prefix (one of {', '.join(FILE_TYPE_PREFIXES)})"))

        full_path = mod_path / file_path
        if not full_path.exists():
            errors.append(ValidationError(mod_id, f"Referenced file not found: {file_path}"))

    for dep in manifest.get("dependencies", []):
        if dep not in all_mod_ids:
            errors.append(ValidationError(mod_id, f"Dependency '{dep}' not found in loaded mods"))

    return errors


def validate_events(events: list, mod_id: str) -> list[ValidationError]:
    """Validate mod event definitions against ModEventDefinition."""
    errors = []
    seen_ids: set[str] = set()

    for event in events:
        event_id = event.get("id", "<unknown>")

        if event_id in seen_ids:
            errors.append(ValidationError(mod_id, f"Duplicate event ID: {event_id}"))
        seen_ids.add(event_id)

        if not event.get("name"):
            errors.append(ValidationError(mod_id, f"Event '{event_id}' missing name"))

        category = event.get("category", "Other")
        if category not in EVENT_CATEGORIES:
            errors.append(ValidationError(
                mod_id,
                f"Event '{event_id}' has unknown category '{category}' "
                f"(falls back to Other in the loader; valid: {sorted(EVENT_CATEGORIES)})",
                severity="warning"))

        weight = event.get("weight", 1.0)
        if weight <= 0:
            errors.append(ValidationError(mod_id, f"Event '{event_id}' has non-positive weight"))

    return errors


def validate_research(nodes: list, mod_id: str) -> list[ValidationError]:
    """Validate mod research node definitions against ModResearchNode."""
    errors = []
    seen_ids: set[str] = set()

    for node in nodes:
        node_id = node.get("id", "<unknown>")

        if node_id in seen_ids:
            errors.append(ValidationError(mod_id, f"Duplicate research node ID: {node_id}"))
        seen_ids.add(node_id)

        if not node.get("name"):
            errors.append(ValidationError(mod_id, f"Research node '{node_id}' missing name"))

        branch = node.get("branch", "Biology")
        if branch not in RESEARCH_BRANCHES:
            errors.append(ValidationError(mod_id, f"Research node '{node_id}' has unknown branch '{branch}'"))

        tier = node.get("tier", 1)
        if tier < 1 or tier > 5:
            errors.append(ValidationError(mod_id, f"Research node '{node_id}' has invalid tier {tier}"))

    return errors


def validate_fish_species(species: list, mod_id: str) -> list[ValidationError]:
    """Validate mod fish species definitions against ModFishSpecies."""
    errors = []
    seen_ids: set[str] = set()

    for sp in species:
        sp_id = sp.get("id", "<unknown>")

        if sp_id in seen_ids:
            errors.append(ValidationError(mod_id, f"Duplicate species ID: {sp_id}"))
        seen_ids.add(sp_id)

        if not sp.get("name"):
            errors.append(ValidationError(mod_id, f"Species '{sp_id}' missing name"))

        temp_min = sp.get("optimal_temp_min", 0)
        temp_max = sp.get("optimal_temp_max", 0)
        if temp_min >= temp_max:
            errors.append(ValidationError(
                mod_id,
                f"Species '{sp_id}': optimal_temp_min ({temp_min}) >= optimal_temp_max ({temp_max})"))

    return errors


def validate_scenarios(scenarios: list, mod_id: str, all_event_ids: set[str]) -> list[ValidationError]:
    """Validate mod scenario definitions against ModScenario."""
    errors = []
    seen_ids: set[str] = set()

    for sc in scenarios:
        sc_id = sc.get("id", "<unknown>")

        if sc_id in seen_ids:
            errors.append(ValidationError(mod_id, f"Duplicate scenario ID: {sc_id}"))
        seen_ids.add(sc_id)

        if not sc.get("name"):
            errors.append(ValidationError(mod_id, f"Scenario '{sc_id}' missing name"))

        difficulty = sc.get("difficulty", 3)
        if difficulty < 1 or difficulty > 5:
            errors.append(ValidationError(mod_id, f"Scenario '{sc_id}' has invalid difficulty {difficulty}"))

        region = sc.get("starting_region", "")
        if region and region not in PRODUCTION_REGIONS:
            errors.append(ValidationError(
                mod_id,
                f"Scenario '{sc_id}' has unknown region '{region}' (valid: {sorted(PRODUCTION_REGIONS)})",
                severity="warning"))

        win = sc.get("win_condition", "")
        if win and win not in WIN_CONDITIONS:
            errors.append(ValidationError(
                mod_id,
                f"Scenario '{sc_id}' has unknown win_condition '{win}' "
                f"(falls back to Profit; valid: {sorted(WIN_CONDITIONS)})",
                severity="warning"))

        # Cross-reference: initial_events must name known event IDs.
        for evt_ref in sc.get("initial_events", []):
            if evt_ref not in all_event_ids:
                errors.append(ValidationError(
                    mod_id,
                    f"Scenario '{sc_id}' references unknown event '{evt_ref}' in initial_events"))

    return errors


def validate_mods(mods_dir: Path) -> list[ValidationError]:
    """Validate all mods in the given directory."""
    errors: list[ValidationError] = []

    if not mods_dir.exists():
        return [ValidationError("<system>", f"Mods directory not found: {mods_dir}")]

    mod_dirs = [d for d in mods_dir.iterdir() if d.is_dir() and not d.name.startswith(".")]
    if not mod_dirs:
        print(f"  No mod directories found in {mods_dir}")
        return errors

    # First pass: collect all mod IDs (for dependency + event cross-ref checks).
    all_mod_ids: set[str] = set()
    mod_manifests: dict[str, tuple[dict, Path]] = {}

    for mod_dir in sorted(mod_dirs):
        manifest_path = mod_dir / "manifest.json"
        if not manifest_path.exists():
            errors.append(ValidationError(mod_dir.name, "No manifest.json found"))
            continue

        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as e:
            errors.append(ValidationError(mod_dir.name, f"Invalid JSON in manifest.json: {e}"))
            continue

        mod_id = manifest.get("id", mod_dir.name)
        if mod_id in all_mod_ids:
            errors.append(ValidationError(mod_id, f"Duplicate mod ID '{mod_id}'"))
        all_mod_ids.add(mod_id)
        mod_manifests[mod_id] = (manifest, mod_dir)

    # Pre-pass: gather every known event ID across all mods (for scenario
    # cross-refs), independent of file ordering within or across manifests.
    all_event_ids: set[str] = set()
    for mod_id, (manifest, mod_dir) in mod_manifests.items():
        for file_path in manifest.get("files", []):
            full_path = mod_dir / file_path
            if not full_path.exists():
                continue
            if not os.path.basename(file_path).lower().startswith("events"):
                continue
            try:
                data = json.loads(full_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                continue
            if isinstance(data, list):
                all_event_ids.update(e.get("id", "") for e in data if isinstance(e, dict))

    # Second pass: validate each mod and its data files.
    for mod_id, (manifest, mod_dir) in sorted(mod_manifests.items()):
        errors.extend(validate_manifest(manifest, mod_dir, all_mod_ids))

        for file_path in manifest.get("files", []):
            full_path = mod_dir / file_path
            if not full_path.exists():
                continue  # already reported in manifest validation

            file_name = os.path.basename(file_path).lower()
            try:
                data = json.loads(full_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError as e:
                errors.append(ValidationError(mod_id, f"Invalid JSON in {file_path}: {e}"))
                continue

            if not isinstance(data, list):
                errors.append(ValidationError(mod_id, f"{file_path} must be a JSON array"))
                continue

            if file_name.startswith("events"):
                errors.extend(validate_events(data, mod_id))
            elif file_name.startswith("research"):
                errors.extend(validate_research(data, mod_id))
            elif file_name.startswith("fish"):
                errors.extend(validate_fish_species(data, mod_id))
            elif file_name.startswith("scenario"):
                errors.extend(validate_scenarios(data, mod_id, all_event_ids))

    return errors


def main() -> int:
    print("═" * 60)
    print("  Lakskongen Mod Validation (Phase 4.6)")
    print("═" * 60)

    mods_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else find_mods_directory()

    if mods_dir is None:
        print("  No mods directory found — skipping validation")
        return 0

    print(f"\n  Scanning: {mods_dir}")
    errors = validate_mods(mods_dir)

    if not errors:
        print("  ✅ All mods valid!")
        return 0

    actual_errors = [e for e in errors if e.severity == "error"]
    warnings = [e for e in errors if e.severity == "warning"]

    if warnings:
        print(f"\n  ⚠️  {len(warnings)} warning(s):")
        for w in warnings:
            print(f"    - {w}")

    if actual_errors:
        print(f"\n  ❌ {len(actual_errors)} error(s):")
        for e in actual_errors:
            print(f"    - {e}")
        return 1

    print("  ✅ All mods valid (with warnings)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
