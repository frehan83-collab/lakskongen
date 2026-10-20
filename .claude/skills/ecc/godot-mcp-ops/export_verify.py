#!/usr/bin/env python3
"""
export_verify.py — Export Verification

Runs headless export for all presets in export_presets.cfg
and verifies output size > 0.
"""

import os
import subprocess
import sys
from typing import Any, Dict, List
from pathlib import Path

# Preset platforms each host can export. A macOS .app bundle is built on macOS
# hosts only, so a Linux CI runner exports the Linux preset.
HOST_EXPORTABLE_PLATFORMS = {
    "darwin": {"macOS", "Linux"},
    "linux": {"Linux"},
}


def exportable_on_host(presets: List[Dict[str, Any]], host: str) -> List[Dict[str, Any]]:
    """The presets this host can export (host as in sys.platform)."""
    platforms = HOST_EXPORTABLE_PLATFORMS.get(host, {"Linux"})
    return [p for p in presets if p.get("platform") in platforms]


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
        runnable = exportable_on_host(presets, sys.platform)

        # Nothing exported is a failure, not a pass: the gate used to report
        # success with no presets (a clean checkout had none, review step 14).
        if not runnable:
            reason = ("No export presets found in export_presets.cfg" if not presets
                      else f"No preset exportable on host '{sys.platform}' "
                           f"(presets: {', '.join(p.get('platform', '?') for p in presets)})")
            return {
                "success": False,
                "data": {"mode": "headless", "presets": [], "message": reason},
                "errors": [f"Export gate exported nothing: {reason}"],
                "warnings": []
            }

        results = []
        all_errors = []
        all_warnings = [f"Preset '{p.get('name')}' ({p.get('platform')}) is not exported on host '{sys.platform}'"
                        for p in presets if p not in runnable]

        for preset in runnable:
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
                "total": len(runnable),
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