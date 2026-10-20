# Blender Asset Pipeline Skill

Provides a reproducible, validated Blender-to-Godot asset pipeline for the Lakskongen project.

## Purpose

Automates creation of all 3D assets (salmon, feed_barge, wellboat, pen_rails, net_variants) with LODs, validates geometric constraints, and emits a manifest for CI verification.

## Components

### 1. Asset Manifest (`blender/asset_manifest.json`)

Defines all assets with LODs, materials, scale, and validation rules:

```json
{
  "version": 1,
  "assets": [
    {
      "id": "salmon",
      "type": "fish",
      "scale_m": 0.9,
      "orientation": "nose_neg_z",
      "lod_levels": ["LOD0", "LOD1", "LOD2"],
      "materials": ["skin", "belly", "fin"],
      "tri_budget": { "LOD0": 284, "LOD1": 80, "LOD2": 12 },
      "export_path": "godot/assets/models/fish/salmon_{lod}.glb"
    }
  ]
}
```

### 2. CLI Tool: `blender_asset_pipeline.py`

```
python3 blender_asset_pipeline.py [--manifest PATH] [--verify-only] [--asset ID]
```

- Reads `blender/asset_manifest.json` (creates template if missing)
- Runs Blender in background mode to build all assets
- Validates outputs:
  - Scale: 0.9m unit fish (nose-to-tail-base)
  - Orientation: nose at -Z, tail at +Z (Godot local space)
  - Triangle counts per LOD
  - Material count matches manifest
- Emits `godot/assets/asset_manifest.json` with SHA256 hashes for CI verification

### 3. LOD Presets

| Asset | LOD0 | LOD1 | LOD2 |
|-------|------|------|------|
| salmon | 284 tris | ~80 tris | billboard (12 tris) |
| feed_barge | 1200 tris | 350 tris | 60 tris |
| wellboat | 1800 tris | 500 tris | 80 tris |
| pen_rails | 400 tris | 120 tris | 24 tris |
| net_variants | 600 tris | 180 tris | 36 tris |

### 4. CI Integration

Adds verification step to `tools/ci.sh`:
```bash
run_step "Blender asset pipeline verification"    step_verify_assets
```

The verification:
- Checks all exported GLB files exist
- Validates SHA256 hashes match manifest
- Runs triangle count verification
- Validates scale and orientation

## Usage

### Build All Assets
```bash
python3 blender_asset_pipeline.py
```

### Build Single Asset
```bash
python3 blender_asset_pipeline.py --asset salmon
```

### Verify Only (CI)
```bash
python3 blender_asset_pipeline.py --verify-only
```

## References

- Existing Blender scripts: `blender/build_salmon.py`
- MCP call pattern: `tools/blender_mcp_call.py`
- Blender MCP server on port 9876

## Godot Conventions

- Unit: 1 Blender unit = 1 meter
- Fish: 0.9m nose-to-tail-base, fins to ~1.1m total
- Axes: Blender -Y maps to Godot +Z (tail at +Z, nose at -Z)
- Export: GLB with materials, Y-up, no cameras/lights

## Asset Types

1. **salmon** - Atlantic salmon with LOD0/LOD1/LOD2
2. **feed_barge** - Feeding barge vessel
3. **wellboat** - Live fish transport vessel
4. **pen_rails** - Pen cage handrails, collars, walkways
5. **net_variants** - Net panels (clean, fouled, damaged variants)

## Validation Rules

Each asset must pass:
- [ ] File exists at export path
- [ ] SHA256 matches manifest
- [ ] Triangle count within ±10% of budget
- [ ] Material count matches manifest
- [ ] Scale: bounding box matches expected (0.9m for salmon)
- [ ] Orientation: nose at local -Z, tail at +Z