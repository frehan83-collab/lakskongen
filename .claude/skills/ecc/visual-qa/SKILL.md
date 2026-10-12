---
name: visual-qa
description: Visual QA workflow for Godot screenshot harness. Capture, diff via perceptual hash, PR diff gallery, baseline promotion.
origin: project
---

# Visual QA — Lakskongen Screenshot Harness

This skill provides a complete visual regression workflow for the Lakskongen Godot project's 23-shot screenshot harness.

**Stack:** Python · PIL (Pillow) · imagehash · GitHub Actions

---

## Shots Captured (23 total)

| Index | Shot ID | Description |
|-------|---------|-------------|
| 01 | `main_menu` | Main menu scene |
| 02 | `scenario_selection` | Scenario select panel |
| 03 | `dashboard` | Dashboard tab |
| 04 | `telemetry` | Telemetry tab |
| 05 | `map_overlay` | Map overlay (oxygen metric) |
| 06 | `finance` | Finance tab |
| 07 | `genetics` | Genetics panel |
| 07b | `crew` | Crew tab |
| 07c | `contracts` | Contracts tab |
| 07d | `offshore` | Offshore tab |
| 07e | `carbon_ledger` | Carbon ledger tab |
| 07f | `pen_management` | Pen management tab |
| 08 | `fjord_day` | Fjord day (clear, zoom fjord) |
| 08a | `fjord_live_sync` | Live sim→3D sync frame |
| 08b | `fjord_farm` | Farm zoom (asset closeup) |
| 09 | `fjord_night_aurora` | Night + aurora |
| 10 | `fjord_storm` | Storm weather |
| 11 | `fjord_underwater` | Underwater camera |
| 14 | `diag_no_tilt` | Diagnostic: tilt shift hidden |
| 15 | `diag_no_underwater` | Diagnostic: underwater canvas hidden |
| 16 | `diag_no_farm` | Diagnostic: farm hidden |

---

## Directory Layout

```
godot/screenshots/
├── game/                    # Current run outputs (from harness)
│   ├── 01_main_menu.png
│   ├── ...
│   └── harness.log
├── baseline/                # Committed baseline images + manifest
│   ├── 01_main_menu.png
│   ├── ...
│   └── manifest.json
```

### manifest.json Schema

```json
{
  "version": 1,
  "git_sha": "abc123...",
  "timestamp": "2026-10-09T16:30:00Z",
  "threshold": 8,
  "shots": {
    "01_main_menu": { "phash": "d4f2e1a0b3c8f9e2", "size": [1920, 1080] },
    "02_scenario_selection": { "phash": "a1b2c3d4e5f60718", "size": [1920, 1080] },
    ...
  }
}
```

---

## Commands

### 1. capture — Run harness, copy to baseline with manifest

```bash
# From lakskongen root
tools/capture_screenshots.sh
python -m visual_qa.capture
```

**What it does:**
1. Runs `tools/capture_screenshots.sh` (launches Godot non-headless)
2. Waits for completion, reads `godot/screenshots/game/*.png`
3. Computes perceptual hash (phash) for each image
4. Writes `godot/screenshots/baseline/manifest.json` with phash + timestamp + git SHA
5. Copies images to `baseline/`

**Output:** Baseline ready for diff comparison.

---

### 2. diff — Compare current vs baseline (phash distance)

```bash
python -m visual_qa.diff [--threshold 8] [--fail-on-diff]
```

**What it does:**
1. Loads `baseline/manifest.json`
2. For each shot in manifest, loads current from `game/` and baseline from `baseline/`
3. Computes phash distance (Hamming distance of 64-bit hash)
4. Reports PASS/FAIL per shot; overall FAIL if any distance > threshold
5. Exit code 1 on failure (for CI gating)

**Threshold:** Default 8 (≈ 12.5% bit difference). Lower = stricter.

**Output example:**
```
[visual-qa] Comparing 23 shots against baseline (threshold=8)
  01_main_menu:           dist=0  ✓
  02_scenario_selection:  dist=2  ✓
  03_dashboard:           dist=12 ✗  (FAIL)
  ...
[visual-qa] 1/23 shots exceeded threshold
```

---

### 3. pr-comment — GitHub Action: post diff gallery on PR

**Workflow file:** `.github/workflows/visual-qa.yml`

**Trigger:** `pull_request` (on push to PR branches)

**What it does:**
1. Runs `capture` on PR head (requires self-hosted runner with display)
2. Runs `diff` against `main` branch baseline
3. Generates side-by-side comparison images (baseline | current | diff heatmap)
4. Posts comment on PR with gallery markdown + summary table

**Gallery format:**
```markdown
## Visual QA Report

| Shot | Baseline | Current | Diff | Distance | Status |
|------|----------|---------|------|----------|--------|
| 03_dashboard | ![b](url) | ![c](url) | ![d](url) | 12 | ❌ FAIL |

<details><summary>All 23 shots</summary>
...full gallery...
</details>
```

**Requirements:** Self-hosted macOS/Linux runner with GPU/desktop session (Godot cannot render headless).

---

### 4. baseline-update — Promote current to baseline (with git commit)

```bash
python -m visual_qa.baseline_update [--message "chore: update visual baseline"]
```

**What it does:**
1. Runs `capture` to get fresh current shots
2. Computes phash for each
3. Overwrites `baseline/*.png` and `baseline/manifest.json`
4. Commits changes with provided message
5. Pushes to current branch

**Use when:** Intentional visual changes (new UI, shader updates, asset swaps).

---

## Python Module Structure

```
visual_qa/
├── __init__.py
├── capture.py        # capture command
├── diff.py           # diff command
├── baseline_update.py # baseline-update command
├── phash.py          # perceptual hash utilities
├── gallery.py        # diff gallery generation
└── config.py         # shared config (threshold, paths)
```

---

## Installation

```bash
# From lakskongen root
pip install pillow imagehash
```

Add to `requirements.txt`:
```
pillow==10.2.0
imagehash==4.3.1
```

---

## CI Integration

### GitHub Actions (`.github/workflows/visual-qa.yml`)

```yaml
name: Visual QA

on:
  pull_request:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read
  pull-requests: write

jobs:
  visual-qa:
    runs-on: [self-hosted, macos, gpu]  # requires display
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0  # needed for baseline from main

      - name: Setup Python
        uses: actions/setup-python@v5
        with:
          python-version: "3.12"
          cache: pip

      - run: pip install -r requirements.txt

      - name: Capture current (PR head)
        run: |
          tools/capture_screenshots.sh
          python -m visual_qa.capture

      - name: Fetch main baseline
        run: |
          git fetch origin main
          git checkout origin/main -- godot/screenshots/baseline/

      - name: Diff
        id: diff
        run: |
          python -m visual_qa.diff --fail-on-diff || echo "DIFF_FAILED=true" >> $GITHUB_OUTPUT

      - name: Generate gallery
        if: always()
        run: python -m visual_qa.gallery

      - name: Comment on PR
        if: always()
        uses: actions/github-script@v7
        with:
          script: |
            const fs = require('fs');
            const body = fs.readFileSync('visual_qa_gallery.md', 'utf8');
            github.rest.issues.createComment({
              owner: context.repo.owner,
              repo: context.repo.repo,
              issue_number: context.payload.pull_request.number,
              body
            });
```

---

## Local Usage

```bash
# Full cycle: capture → diff → (if happy) baseline-update
tools/capture_screenshots.sh
python -m visual_qa.capture
python -m visual_qa.diff

# If diffs are expected (intentional changes):
python -m visual_qa.baseline_update --message "feat: new dashboard layout"

# Just diff against existing baseline:
python -m visual_qa.diff
```

---

## Configuration

`visual_qa/config.py` (or env vars):

```python
THRESHOLD = 8                    # phash distance fail threshold
BASELINE_DIR = "godot/screenshots/baseline"
CURRENT_DIR = "godot/screenshots/game"
MANIFEST = "manifest.json"
SHOT_NAMES = [                   # ordered list for consistent gallery
    "01_main_menu",
    "02_scenario_selection",
    "03_dashboard",
    "04_telemetry",
    "05_map_overlay",
    "06_finance",
    "07_genetics",
    "07b_crew",
    "07c_contracts",
    "07d_offshore",
    "07e_carbon_ledger",
    "07f_pen_management",
    "08_fjord_day",
    "08a_fjord_live_sync",
    "08b_fjord_farm",
    "09_fjord_night_aurora",
    "10_fjord_storm",
    "11_fjord_underwater",
    "14_diag_no_tilt",
    "15_diag_no_underwater",
    "16_diag_no_farm",
]
```

---

## Phash Details

- **Algorithm:** DCT-based perceptual hash (pHash) via `imagehash.phash()`
- **Hash size:** 64 bits (8×8 DCT coefficients)
- **Distance metric:** Hamming distance (bit differences)
- **Threshold guidance:**
  - 0–3: Nearly identical (compression, tiny render variance)
  - 4–8: Minor differences (font rendering, particle jitter)
  - 9–15: Noticeable visual change (layout, color, assets)
  - 16+: Major difference (scene change, broken render)

---

## Troubleshooting

| Issue | Fix |
|-------|-----|
| "no display" error | Run from desktop session, not SSH/headless CI |
| No screenshots in `game/` | Godot failed to launch; check `harness.log` |
| Phash distance always 0 | Images identical; verify harness actually ran |
| Baseline not found | Run `capture` first or fetch from `main` |
| Gallery images missing | Ensure `diff` ran before `gallery` |

---

## Anti-Patterns

| Anti-pattern | Problem | Fix |
|--------------|---------|-----|
| Running headless | Godot produces blank frames | Use self-hosted runner with display |
| Committing baseline to PR branch | Pollutes PR history | Keep baseline only on `main` |
| Hardcoding threshold in scripts | Can't tune per project | Use `config.py` + env override |
| Skipping `capture` before `diff` | Compares stale `game/` | Always `capture` then `diff` |
| Large thresholds (e.g., 20) | Misses real regressions | Keep ≤ 8; investigate outliers |

---

## Quick Reference

```bash
# Capture + baseline init (first time)
tools/capture_screenshots.sh && python -m visual_qa.capture

# Daily diff check
tools/capture_screenshots.sh && python -m visual_qa.diff

# PR workflow (automated via GH Actions)
# 1. Push PR → workflow runs capture+diff
# 2. Review gallery comment
# 3. If expected: baseline-update + push
# 4. Re-run workflow → passes

# Update baseline locally
python -m visual_qa.baseline_update -m "chore: update baseline for T123"
```