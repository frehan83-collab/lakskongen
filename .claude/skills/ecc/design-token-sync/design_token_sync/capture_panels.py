#!/usr/bin/env python3
"""Capture key UI panels via headless Godot for visual regression testing."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

try:
    from .config import GODOT_BIN, PANEL_SCENES, SCREENSHOT_AFTER, SCREENSHOT_BEFORE
except ImportError:
    # Running as standalone script
    sys.path.insert(0, str(Path(__file__).parent))
    from config import GODOT_BIN, PANEL_SCENES, SCREENSHOT_AFTER, SCREENSHOT_BEFORE


def ensure_dirs():
    SCREENSHOT_BEFORE.mkdir(parents=True, exist_ok=True)
    SCREENSHOT_AFTER.mkdir(parents=True, exist_ok=True)


def build_capture_script(panel_name: str, scene_path: str, output_dir: Path) -> str:
    """Generate a GDScript that captures a panel screenshot."""
    script = f'''
extends Node

@tool
class_name PanelCapture

var panel_name = "{panel_name}"
var scene_path = "{scene_path}"
var output_path = "{output_dir / f"{panel_name.lower()}.png"}"

func _ready() -> void:
    var scene = ResourceLoader.load(scene_path)
    if not scene:
        push_error("Failed to load scene: " + scene_path)
        OS.exit(1)
    
    var instance = scene.instantiate()
    if not instance:
        push_error("Failed to instantiate: " + scene_path)
        OS.exit(1)
    
    # Add to root viewport
    get_tree().root.add_child(instance)
    
    # Force layout
    get_tree().process_frame()
    get_tree().process_frame()
    
    # Capture screenshot
    var viewport = get_viewport()
    var texture = viewport.get_texture()
    var image = texture.get_image()
    
    if image.is_empty():
        push_error("Captured empty image")
        OS.exit(1)
    
    # Resize to standard 1920x1080 if needed
    if image.get_size() != Vector2(1920, 1080):
        image.resize(1920, 1080)
    
    var err = image.save_png(output_path)
    if err != OK:
        push_error("Failed to save PNG: " + str(err))
        OS.exit(1)
    
    print("Captured: " + output_path)
    OS.exit(0)
'''
    return script


def capture_panel(panel_name: str, scene_path: str, output_dir: Path) -> int:
    """Capture a single panel via headless Godot."""
    if not Path(GODOT_BIN).exists():
        print(f"[design-token-sync] ERROR: Godot not found at {GODOT_BIN}", file=sys.stderr)
        return 1
    
    project_path = Path(__file__).resolve().parent.parent.parent.parent / "godot"
    
    # Write capture script to a temp file
    import tempfile
    with tempfile.NamedTemporaryFile(mode='w', suffix='.gd', delete=False) as f:
        f.write(build_capture_script(panel_name, scene_path, output_dir))
        script_path = f.name
    
    try:
        cmd = [
            GODOT_BIN,
            "--headless",
            "--path", str(project_path),
            "-s", script_path,
            "--quit"
        ]
        
        print(f"[design-token-sync] Capturing {panel_name}...")
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
        
        if result.stdout:
            for line in result.stdout.strip().split('\n'):
                print(f"[design-token-sync] {line}")
        if result.stderr:
            for line in result.stderr.strip().split('\n'):
                if "push_error" not in line.lower():
                    print(f"[design-token-sync] {line}", file=sys.stderr)
        
        return result.returncode
    finally:
        Path(script_path).unlink(missing_ok=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Capture UI panels for visual regression")
    parser.add_argument("--before", action="store_true", help="Capture before sync (baseline)")
    parser.add_argument("--after", action="store_true", help="Capture after sync (test)")
    args = parser.parse_args(argv)
    
    if not args.before and not args.after:
        parser.error("Must specify --before or --after")
    
    output_dir = SCREENSHOT_BEFORE if args.before else SCREENSHOT_AFTER
    ensure_dirs()
    
    print(f"[design-token-sync] Capturing panels to {output_dir}")
    
    failed = 0
    for panel_name, scene_path in PANEL_SCENES.items():
        rc = capture_panel(panel_name, scene_path, output_dir)
        if rc != 0:
            print(f"[design-token-sync] FAILED: {panel_name}", file=sys.stderr)
            failed += 1
        else:
            output_file = output_dir / f"{panel_name.lower()}.png"
            if output_file.exists():
                print(f"[design-token-sync] ✓ {panel_name} → {output_file}")
            else:
                print(f"[design-token-sync] WARNING: Output file not found for {panel_name}", file=sys.stderr)
                failed += 1
    
    # Write manifest
    manifest = {
        "version": 1,
        "phase": "before" if args.before else "after",
        "panels": list(PANEL_SCENES.keys()),
        "threshold": 8,
    }
    (output_dir / "manifest.json").write_text(json.dumps(manifest, indent=2))
    
    if failed > 0:
        print(f"[design-token-sync] {failed}/{len(PANEL_SCENES)} panels failed", file=sys.stderr)
        return 1
    
    print(f"[design-token-sync] All {len(PANEL_SCENES)} panels captured successfully ✓")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())