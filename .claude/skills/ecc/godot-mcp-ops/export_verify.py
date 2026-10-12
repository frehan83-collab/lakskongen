#!/usr/bin/env python3
"""
export_verify.py — Export Verification

Runs headless export for all presets in export_presets.cfg
and verifies output size > 0.
"""

import os
import subprocess
from typing import Any, Dict, List
from pathlib import Path


class ExportVerify:
    def __init__(self, ops):
        self.ops = ops
        self.project_path = ops.project_path

    def run(self) -> Dict[str, Any]:
        """Run export verification for all presets."""
        try:
            return self._verify_via_headless()
        except Exception as e:
            return {
                "success": False,
                "data": {},
                "errors": [f"Export verify failed: {str(e)}"],
                "warnings": []
            }

    def _verify_via_headless(self) -> Dict[str, Any]:
        """Use headless Godot to export each preset."""
        presets = self.ops.headless.get_export_presets()

        if not presets:
            return {
                "success": True,
                "data": {
                    "mode": "headless",
                    "presets": [],
                    "message": "No export presets found"
                },
                "errors": [],
                "warnings": ["No export presets found in export_presets.cfg"]
            }

        results = []
        all_errors = []
        all_warnings = []

        for preset in presets:
            preset_result = self._export_preset(preset)
            results.append(preset_result)

            if not preset_result["success"]:
                all_errors.extend(preset_result["errors"])
            all_warnings.extend(preset_result["warnings"])

        return {
            "success": len(all_errors) == 0,
            "data": {
                "mode": "headless",
                "presets": results,
                "total": len(presets),
                "passed": sum(1 for r in results if r["success"]),
                "failed": sum(1 for r in results if not r["success"])
            },
            "errors": all_errors,
            "warnings": all_warnings
        }

    def _export_preset(self, preset: Dict[str, Any]) -> Dict[str, Any]:
        """Export a single preset and verify output."""
        name = preset.get("name", "unknown")
        export_path = preset.get("export_path", "")

        if not export_path:
            return {
                "success": False,
                "preset": name,
                "errors": ["No export_path configured"],
                "warnings": []
            }

        # Resolve relative export path
        output_path = self.project_path / export_path
        output_path.parent.mkdir(parents=True, exist_ok=True)

        # Run export
        result = self.ops.headless.export(name, str(output_path))

        errors = []
        warnings = []

        if result.returncode != 0:
            errors.append(f"Export failed with code {result.returncode}: {result.stderr[:500]}")
            return {
                "success": False,
                "preset": name,
                "export_path": str(output_path),
                "errors": errors,
                "warnings": warnings
            }

        # Verify output exists and has size > 0
        if not output_path.exists():
            errors.append(f"Export output not found at {output_path}")
        else:
            size = self._get_output_size(output_path)
            if size == 0:
                errors.append(f"Export output is empty (0 bytes): {output_path}")
            else:
                # Verify it's a valid app bundle (macOS)
                if output_path.suffix == ".app":
                    if not self._verify_app_bundle(output_path):
                        warnings.append(f"Export output may be incomplete: {output_path}")

        return {
            "success": len(errors) == 0,
            "preset": name,
            "export_path": str(output_path),
            "size_bytes": size if output_path.exists() else 0,
            "errors": errors,
            "warnings": warnings
        }

    def _get_output_size(self, path: Path) -> int:
        """Get the size of export output, using recursive size for app bundles."""
        if path.suffix == ".app" and path.is_dir():
            # Use du for app bundles to get actual size
            try:
                result = subprocess.run(
                    ["du", "-sb", str(path)],
                    capture_output=True,
                    text=True,
                    timeout=10
                )
                if result.returncode == 0:
                    return int(result.stdout.split('\t')[0])
            except Exception:
                pass
            # Fallback: sum file sizes
            total = 0
            for root, dirs, files in os.walk(path):
                for f in files:
                    fp = Path(root) / f
                    try:
                        total += fp.stat().st_size
                    except Exception:
                        pass
            return total
        else:
            return path.stat().st_size

    def _verify_app_bundle(self, app_path: Path) -> bool:
        """Verify macOS .app bundle structure."""
        # Check for basic structure
        contents = app_path / "Contents"
        if not contents.exists():
            return False
        macos = contents / "MacOS"
        if not macos.exists():
            return False
        # Check for executable
        executables = list(macos.glob("*"))
        if not executables:
            return False
        # Check Info.plist
        if not (contents / "Info.plist").exists():
            return False
        return True