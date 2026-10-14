#!/usr/bin/env python3
"""
Blender Asset Pipeline for Lakskongen.

Reads blender/asset_manifest.json, builds all assets via Blender background mode,
validates outputs, and emits godot/assets/asset_manifest.json with hashes.

Usage:
    python3 blender_asset_pipeline.py [--manifest PATH] [--verify-only] [--asset ID]

Environment:
    BLENDER_BIN   Path to Blender executable (default: blender)
    BLENDER_MCP   Use MCP server instead of background mode (default: false)
"""

import argparse
import hashlib
import json
import os
import subprocess
import sys
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).parent.parent.parent.parent.parent
BLENDER_DIR = REPO_ROOT / "blender"
GODOT_ASSETS = REPO_ROOT / "godot" / "assets"
MANIFEST_IN = BLENDER_DIR / "asset_manifest.json"
MANIFEST_OUT = GODOT_ASSETS / "asset_manifest.json"

DEFAULT_MANIFEST = {
    "version": 1,
    "assets": [
        {
            "id": "salmon",
            "type": "fish",
            "scale_m": 0.9,
            "orientation": "nose_neg_z",
            "lod_levels": ["LOD0", "LOD1", "LOD2"],
            "materials": ["skin", "belly", "fin"],
            "tri_budget": {"LOD0": 284, "LOD1": 80, "LOD2": 12},
            "export_path": "godot/assets/models/fish/salmon_{lod}.glb",
            "build_script": "blender/build_salmon.py"
        },
        {
            "id": "feed_barge",
            "type": "boat",
            "scale_m": 12.0,
            "orientation": "bow_neg_z",
            "lod_levels": ["LOD0", "LOD1", "LOD2"],
            "materials": ["hull", "deck", "cabin", "trim"],
            "tri_budget": {"LOD0": 1200, "LOD1": 350, "LOD2": 60},
            "export_path": "godot/assets/models/boat/feed_barge_{lod}.glb",
            "build_script": "blender/build_feed_barge.py"
        },
        {
            "id": "wellboat",
            "type": "boat",
            "scale_m": 25.0,
            "orientation": "bow_neg_z",
            "lod_levels": ["LOD0", "LOD1", "LOD2"],
            "materials": ["hull", "deck", "superstructure", "tanks"],
            "tri_budget": {"LOD0": 1800, "LOD1": 500, "LOD2": 80},
            "export_path": "godot/assets/models/boat/wellboat_{lod}.glb",
            "build_script": "blender/build_wellboat.py"
        },
        {
            "id": "pen_rails",
            "type": "pen",
            "scale_m": 1.0,
            "orientation": "base_neg_z",
            "lod_levels": ["LOD0", "LOD1", "LOD2"],
            "materials": ["collar", "handrail", "walkway", "feedpipe"],
            "tri_budget": {"LOD0": 400, "LOD1": 120, "LOD2": 24},
            "export_path": "godot/assets/models/pen_cage/pen_rails_{lod}.glb",
            "build_script": "blender/build_pen_rails.py"
        },
        {
            "id": "net_variants",
            "type": "pen",
            "scale_m": 1.0,
            "orientation": "base_neg_z",
            "lod_levels": ["LOD0", "LOD1", "LOD2"],
            "materials": ["net_clean", "net_fouled", "net_damaged"],
            "tri_budget": {"LOD0": 600, "LOD1": 180, "LOD2": 36},
            "export_path": "godot/assets/models/pen_cage/net_{variant}_{lod}.glb",
            "build_script": "blender/build_net_variants.py",
            "variants": ["clean", "fouled", "damaged"]
        }
    ]
}


@dataclass
class AssetSpec:
    id: str
    type: str
    scale_m: float
    orientation: str
    lod_levels: list[str]
    materials: list[str]
    tri_budget: dict[str, int]
    export_path: str
    build_script: str
    variants: list[str] | None = None
    # Hard triangle ceilings per LOD (roadmap budgets, e.g. pen LOD0 <= 60k).
    tri_max: dict[str, int] | None = None

    @classmethod
    def from_dict(cls, d: dict) -> "AssetSpec":
        return cls(
            id=d["id"],
            type=d["type"],
            scale_m=d["scale_m"],
            orientation=d["orientation"],
            lod_levels=d["lod_levels"],
            materials=d["materials"],
            tri_budget=d["tri_budget"],
            export_path=d["export_path"],
            build_script=d["build_script"],
            variants=d.get("variants"),
            tri_max=d.get("tri_max"),
        )


@dataclass
class AssetResult:
    asset_id: str
    lod: str
    variant: str | None
    export_path: str
    sha256: str
    file_size: int
    tri_count: int
    material_count: int
    bbox_size: list[float]
    passed: bool
    errors: list[str]


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(8192), b""):
            h.update(chunk)
    return h.hexdigest()


def load_manifest(path: Path) -> dict:
    if path.exists():
        with path.open() as f:
            return json.load(f)
    return DEFAULT_MANIFEST


def save_manifest(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w") as f:
        json.dump(data, f, indent=2)


def ensure_manifest() -> dict:
    """Create default manifest if missing."""
    if not MANIFEST_IN.exists():
        print(f"Creating default manifest at {MANIFEST_IN}")
        save_manifest(MANIFEST_IN, DEFAULT_MANIFEST)
    return load_manifest(MANIFEST_IN)


def run_blender_build(script_path: Path, lod: str, variant: str | None = None) -> tuple[int, str, str]:
    """Run Blender in background mode to build asset."""
    blender_bin = os.environ.get("BLENDER_BIN", "blender")
    env = os.environ.copy()
    env["LK_LOD"] = lod
    if variant:
        env["LK_VARIANT"] = variant

    cmd = [
        blender_bin,
        "--background",
        "--python", str(script_path),
        "--", "--lod", lod
    ]
    if variant:
        cmd.extend(["--variant", variant])

    result = subprocess.run(cmd, capture_output=True, text=True, env=env, cwd=REPO_ROOT)
    return result.returncode, result.stdout, result.stderr


def run_blender_mcp(script_path: Path, lod: str, variant: str | None = None) -> tuple[int, str, str]:
    """Run Blender via MCP server."""
    mcp_tool = REPO_ROOT / "tools" / "blender_mcp_call.py"
    env = os.environ.copy()
    env["LK_LOD"] = lod
    if variant:
        env["LK_VARIANT"] = variant

    cmd = [
        sys.executable, str(mcp_tool),
        "execute_blender_code",
        "--file", str(script_path)
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, env=env, cwd=REPO_ROOT)
    return result.returncode, result.stdout, result.stderr


def parse_build_output(stdout: str) -> dict:
    """Parse structured output from build script."""
    data = {"tri_count": 0, "bbox": None, "materials": 0}
    for line in stdout.splitlines():
        if line.startswith("SALMON_TRIS:") or line.startswith("TRIS:"):
            data["tri_count"] = int(line.split(":")[1].strip())
        elif line.startswith("BBOX:"):
            data["bbox"] = [float(x) for x in line.split(":")[1].strip().split()]
        elif line.startswith("MATERIALS:"):
            data["materials"] = int(line.split(":")[1].strip())
    return data


def validate_asset(spec: AssetSpec, lod: str, variant: str | None, export_path: Path) -> AssetResult:
    """Validate a single exported asset."""
    errors = []

    if not export_path.exists():
        errors.append(f"File not found: {export_path}")
        return AssetResult(
            asset_id=spec.id, lod=lod, variant=variant,
            export_path=str(export_path), sha256="", file_size=0,
            tri_count=0, material_count=0, bbox_size=[0, 0, 0],
            passed=False, errors=errors
        )

    file_hash = sha256_file(export_path)
    file_size = export_path.stat().st_size

    budget = spec.tri_budget.get(lod, 0)
    expected_materials = len(spec.materials)

    # Parse validation data from a sidecar JSON if available
    validation_file = export_path.with_suffix(".json")
    tri_count = 0
    material_count = 0
    bbox_size = [0.0, 0.0, 0.0]

    if validation_file.exists():
        with validation_file.open() as f:
            vdata = json.load(f)
            tri_count = vdata.get("tri_count", 0)
            material_count = vdata.get("material_count", 0)
            bbox_size = vdata.get("bbox_size", [0.0, 0.0, 0.0])
    else:
        # Fallback: estimate from file size (rough)
        tri_count = max(1, file_size // 40)
        material_count = expected_materials

    # Triangle count validation (±10%)
    if budget > 0:
        tolerance = budget * 0.1
        if not (budget - tolerance <= tri_count <= budget + tolerance):
            errors.append(f"Triangle count {tri_count} outside budget {budget}±{tolerance:.0f}")

    # Hard ceiling, independent of the pinned budget above.
    ceiling = (spec.tri_max or {}).get(lod)
    if ceiling is not None and tri_count > ceiling:
        errors.append(f"Triangle count {tri_count} over the {lod} ceiling {ceiling}")

    # Material count validation
    if material_count != expected_materials:
        errors.append(f"Material count {material_count} != expected {expected_materials}")

    # Scale validation (for fish: 0.9m nose-to-tail)
    if spec.type == "fish" and bbox_size[2] > 0:  # Z is length in Godot
        expected_len = spec.scale_m
        if not (expected_len * 0.9 <= bbox_size[2] <= expected_len * 1.2):
            errors.append(f"Scale mismatch: bbox Z={bbox_size[2]:.3f}m, expected ~{expected_len}m")

    passed = len(errors) == 0
    return AssetResult(
        asset_id=spec.id, lod=lod, variant=variant,
        export_path=str(export_path.relative_to(REPO_ROOT)),
        sha256=file_hash, file_size=file_size,
        tri_count=tri_count, material_count=material_count,
        bbox_size=bbox_size, passed=passed, errors=errors
    )


def build_asset(spec: AssetSpec, use_mcp: bool) -> list[AssetResult]:
    """Build all LODs/variants for an asset."""
    results = []
    script_path = REPO_ROOT / spec.build_script

    if not script_path.exists():
        print(f"  Build script not found: {script_path}")
        for lod in spec.lod_levels:
            for variant in (spec.variants or [None]):
                export_path = REPO_ROOT / spec.export_path.format(lod=lod, variant=variant or "")
                results.append(AssetResult(
                    asset_id=spec.id, lod=lod, variant=variant,
                    export_path=str(export_path.relative_to(REPO_ROOT)),
                    sha256="", file_size=0, tri_count=0, material_count=0,
                    bbox_size=[0, 0, 0], passed=False,
                    errors=[f"Build script missing: {script_path}"]
                ))
        return results

    variants = spec.variants or [None]

    for lod in spec.lod_levels:
        for variant in variants:
            print(f"  Building {spec.id} {lod}" + (f" {variant}" if variant else ""))

            if use_mcp:
                rc, stdout, stderr = run_blender_mcp(script_path, lod, variant)
            else:
                rc, stdout, stderr = run_blender_build(script_path, lod, variant)

            if rc != 0:
                print(f"    Build failed (rc={rc}): {stderr[:200]}")
                export_path = REPO_ROOT / spec.export_path.format(lod=lod, variant=variant or "")
                results.append(AssetResult(
                    asset_id=spec.id, lod=lod, variant=variant,
                    export_path=str(export_path.relative_to(REPO_ROOT)),
                    sha256="", file_size=0, tri_count=0, material_count=0,
                    bbox_size=[0, 0, 0], passed=False,
                    errors=[f"Build failed: {stderr[:200]}"]
                ))
                continue

            # Parse build output for validation data
            build_data = parse_build_output(stdout)

            # Write validation sidecar for verify-only mode
            export_path = REPO_ROOT / spec.export_path.format(lod=lod, variant=variant or "")
            validation_file = export_path.with_suffix(".json")
            validation_file.parent.mkdir(parents=True, exist_ok=True)
            with validation_file.open("w") as f:
                json.dump({
                    "tri_count": build_data.get("tri_count", 0),
                    "material_count": build_data.get("materials", 0),
                    "bbox_size": build_data.get("bbox", [0, 0, 0])
                }, f)

            result = validate_asset(spec, lod, variant, export_path)
            results.append(result)

            if result.passed:
                print(f"    ✓ {result.export_path} ({result.tri_count} tris, {result.material_count} mats)")
            else:
                print(f"    ✗ {result.export_path}: {', '.join(result.errors)}")

    return results


def verify_assets(manifest: dict) -> list[AssetResult]:
    """Verify all assets in manifest without building."""
    results = []
    for asset_dict in manifest["assets"]:
        spec = AssetSpec.from_dict(asset_dict)
        variants = spec.variants or [None]
        for lod in spec.lod_levels:
            for variant in variants:
                export_path = REPO_ROOT / spec.export_path.format(lod=lod, variant=variant or "")
                result = validate_asset(spec, lod, variant, export_path)
                results.append(result)
                if result.passed:
                    print(f"  ✓ {result.export_path}")
                else:
                    print(f"  ✗ {result.export_path}: {', '.join(result.errors)}")
    return results


def write_output_manifest(results: list[AssetResult]) -> None:
    """Write godot/assets/asset_manifest.json with hashes."""
    assets = {}
    for r in results:
        key = r.asset_id
        if r.variant:
            key += f"_{r.variant}"
        key += f"_{r.lod}"
        assets[key] = {
            "path": r.export_path,
            "sha256": r.sha256,
            "size": r.file_size,
            "tri_count": r.tri_count,
            "material_count": r.material_count,
            "bbox_size": r.bbox_size
        }

    output = {
        "version": 1,
        "generated_by": "blender_asset_pipeline",
        "assets": assets
    }
    save_manifest(MANIFEST_OUT, output)
    print(f"\nWrote manifest: {MANIFEST_OUT}")


def main():
    parser = argparse.ArgumentParser(description="Blender Asset Pipeline")
    parser.add_argument("--manifest", type=Path, default=MANIFEST_IN, help="Input manifest path")
    parser.add_argument("--verify-only", action="store_true", help="Only verify existing assets")
    parser.add_argument("--asset", type=str, help="Build only specific asset ID")
    parser.add_argument("--mcp", action="store_true", help="Use Blender MCP server")
    args = parser.parse_args()

    manifest = load_manifest(args.manifest)

    if args.asset:
        manifest["assets"] = [a for a in manifest["assets"] if a["id"] == args.asset]
        if not manifest["assets"]:
            print(f"Asset not found: {args.asset}")
            sys.exit(1)

    all_results = []

    if args.verify_only:
        print("Verifying assets...")
        all_results = verify_assets(manifest)
    else:
        print("Building assets...")
        use_mcp = args.mcp or os.environ.get("BLENDER_MCP", "").lower() == "true"
        for asset_dict in manifest["assets"]:
            spec = AssetSpec.from_dict(asset_dict)
            print(f"\nBuilding {spec.id}...")
            results = build_asset(spec, use_mcp)
            all_results.extend(results)

    write_output_manifest(all_results)

    # Summary
    passed = sum(1 for r in all_results if r.passed)
    failed = sum(1 for r in all_results if not r.passed)
    print(f"\n=== SUMMARY ===")
    print(f"Passed: {passed}")
    print(f"Failed: {failed}")

    if failed > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()