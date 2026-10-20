---
name: design-token-sync
description: Automates the design token → Godot theme pipeline: sync tokens, drift-check, Figma webhook PR automation, visual regression on key panels.
origin: project
---

# Design Token Sync — Lakskongen Design→Godot Pipeline

This skill provides a complete pipeline for keeping design tokens in sync from the Figma source of truth (`design/tokens.json`) through the Godot runtime theme (`godot/theme/tokens.json` → `LakskongenTheme`).

**Stack:** Python · Godot 4 headless · GitHub Actions · visual-qa skill

---

## Architecture

```
Figma (source of truth)
       │
       ▼
design/tokens.json  ←── tools/sync_tokens.py (sync engine)
       │
       ▼
godot/theme/tokens.json  ←── runtime copy consumed by LakskongenTheme
       │
       ▼
LakskongenTheme.reload_tokens()  ←── headless Godot invocation
       │
       ├── drift-check (CI gate)
       ├── figma-webhook → PR (auto-sync)
       └── visual-regression (Dashboard, FinancePanel, ToastNotification)
```

---

## Files in This Skill

```
.claude/skills/ecc/design-token-sync/
├── SKILL.md                           # This file
├── sync_tokens_extended.py            # Extended sync with reload + validation
├── drift_check.py                     # Deep drift detection (JSON + consts)
├── capture_panels.py                  # Headless Godot panel capture
├── visual_diff.py                     # Integration with visual-qa.diff
├── figma_webhook.py                   # Figma webhook payload parser
├── requirements.txt
└── .github/workflows/design-tokens.yml  # GitHub Action (copied to repo root)
```

---

## Commands

### 1. sync — Full sync: tokens.json → Godot → reload theme

```bash
# From lakskongen root
python -m design_token_sync.sync_tokens_extended --write
```

**What it does:**
1. Runs `tools/sync_tokens.py --write`: copies `design/tokens.json` → `godot/theme/tokens.json`,
   then runs headless Godot (`--script res://scripts/tools/build_theme.gd`) to rebuild the
   generated "Kyst & Stål" theme: `godot/theme/lakskongen_theme.tres` (Ink, the project theme in
   `project.godot [gui] theme/custom`), `lakskongen_chart_theme.tres` (Chart paper, document
   panels) and the fonts in `godot/theme/fonts/`. Each theme is stamped with the SHA-256 of
   `tokens.json` and of the builder sources.
2. Runs `tools/sync_tokens.py --check`: the token copy, the project setting and both stamps
   must match (no Godot needed).
3. Exits 0 on success, non-zero on failure.

**Output:**
```
Synced design/tokens.json -> godot/theme/tokens.json
build_theme: wrote res://theme/lakskongen_theme.tres (ink, … types, display font: …)
build_theme: wrote res://theme/lakskongen_chart_theme.tres (chart, … types, display font: …)
OK: godot/theme/tokens.json is in sync with design/tokens.json; generated themes are current
[design-token-sync] Sync complete ✓
```

---

### 2. drift-check — Deep drift detection (CI gate)

```bash
# From lakskongen root
python -m design_token_sync.drift_check [--threshold 0]
```

**What it does:**
1. Compares `design/tokens.json` vs `godot/theme/tokens.json` (byte-for-byte)
2. Parses `godot/theme/default_theme.gd` and extracts all `const` color/spacing/radius/font values
3. Compares runtime `_primary`, `_spacing_sm`, `_radius_sm`, `_font_body`, etc. against tokens
4. Reports drift per category: `colors`, `spacing`, `radii`, `fonts`, `shadows`, `grid`, `breakpoints`
5. Exit code 1 if any drift > threshold (default 0 = any drift fails)

**Output example:**
```
[design-token-sync] Drift check: design/tokens.json vs godot/theme/tokens.json vs LakskongenTheme consts
  colors:           ✓ in sync (33/33 match)
  spacing:          ✓ in sync (6/6 match)
  radii:            ✓ in sync (4/4 match)
  fonts.sizes:      ✓ in sync (7/7 match)
  shadows:          ✓ in sync (4/4 match)
  grid:             ✓ in sync (3/3 match)
  breakpoints:      ✓ in sync (5/5 match)
  LakskongenTheme consts:  ✓ in sync (all fallback consts match tokens)
[design-token-sync] No drift detected ✓
```

**Drift example:**
```
[design-token-sync] Drift check: design/tokens.json vs godot/theme/tokens.json vs LakskongenTheme consts
  colors:           ✗ DRIFT — "primary" differs: design=#0A4D68 vs tokens=#0D5F80
  spacing:          ✓ in sync
  LakskongenTheme consts:  ✗ DRIFT — PRIMARY const=#0A4D68 vs token=#0D5F80
[design-token-sync] 2 categories drifted
Run: python -m design_token_sync.sync_tokens_extended --write
Exit code: 1
```

---

### 3. figma-webhook — GitHub Action: receive Figma webhook, create sync PR

**Workflow file:** `.github/workflows/design-tokens.yml` (installed by this skill)

**Trigger:** `repository_dispatch` event with `event_type: figma_tokens_changed`

**What it does:**
1. Receives Figma webhook payload (via GitHub repository_dispatch)
2. Extracts tokens from payload (supports Figma REST API format + webhook format)
3. Writes to `design/tokens.json`
4. Runs `sync_tokens_extended --write`
5. Creates branch `design-tokens/sync-<sha>`
6. Opens PR with title `chore(design): sync tokens from Figma <sha>`
7. PR body includes token diff summary and visual-qa trigger link

**Figma webhook payload (example):**
```json
{
  "event_type": "figma_tokens_changed",
  "client_payload": {
    "file_key": "abc123",
    "tokens": { "colors": { "primary": "#0A4D68", ... }, "spacing": {...} },
    "version": "2.0.1",
    "changed_by": "designer@company.com",
    "timestamp": "2026-10-09T16:30:00Z"
  }
}
```

**Trigger from Figma (or curl):**
```bash
gh api repos/:owner/:repo/dispatches \
  -f event_type=figma_tokens_changed \
  -f client_payload='{"file_key":"abc123","tokens":{...},"version":"2.0.1"}'
```

---

### 4. visual-regression — Capture the UI shots before/after sync, diff by pHash

```bash
# From lakskongen root (a display is required: the harness renders for real)
python -m design_token_sync.capture_panels --before  # the committed theme
python -m design_token_sync.figma_webhook --write    # or edit design/tokens.json
python -m design_token_sync.sync_tokens_extended --write
python -m design_token_sync.capture_panels --after
python -m design_token_sync.visual_diff --threshold 25
```

**What it does:**
1. Runs `tools/capture_screenshots.sh` (the project's screenshot harness, non-headless).
2. Copies the 16 UI shots (01–07g: menus, every document panel, the inbox) into
   `godot/screenshots/design-token-sync/{before,after}/`.
3. Compares before/after per shot by perceptual hash; 25 is the harness's measured
   run-to-run noise floor. It never touches `godot/screenshots/baseline/`.
4. Writes `visual_diff.json`; exit 1 if any shot exceeds the threshold or is missing.

---

## CI Integration

### Add to `tools/ci.sh`

Add this step to the CI pipeline (after `step_tokens`):

```bash
step_design_token_drift() {
    "$PYTHON" -m design_token_sync.drift_check
}
```

And in the step list:
```bash
run_step "Design token drift check"  step_design_token_drift
```

### GitHub Action (`.github/workflows/design-tokens.yml`)

Created by this skill at install. Key features:

```yaml
name: Design Token Sync

on:
  repository_dispatch:
    types: [figma_tokens_changed]
  workflow_dispatch:
    inputs:
      tokens_json:
        description: 'Raw tokens JSON (for manual trigger)'
        required: false

permissions:
  contents: write
  pull-requests: write

jobs:
  sync-and-pr:
    runs-on: [self-hosted, macos, gpu]  # needs display for visual-qa
    timeout-minutes: 15
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-python@v5
        with:
          python-version: "3.12"
      - run: pip install -r .claude/skills/ecc/design-token-sync/requirements.txt
      - name: Extract tokens from webhook
        run: python -m design_token_sync.figma_webhook --write
      - name: Sync tokens + reload theme
        run: python -m design_token_sync.sync_tokens_extended --write
      - name: Visual regression (key panels)
        run: |
          python -m design_token_sync.capture_panels --before
          python -m design_token_sync.sync_tokens_extended --write
          python -m design_token_sync.capture_panels --after
          python -m design_token_sync.visual_diff
      - name: Create PR with changes
        uses: peter-evans/create-pull-request@v6
        with:
          branch: design-tokens/sync-${{ github.sha }}
          title: 'chore(design): sync tokens from Figma'
          body: |
            Auto-generated PR from Figma webhook.
            Token changes synced and theme reloaded.
            Visual QA: [link to visual-qa workflow run]
```

---

## Installation

```bash
# From lakskongen root
pip install -r .claude/skills/ecc/design-token-sync/requirements.txt
```

**requirements.txt:**
```
pillow==10.2.0
imagehash==4.3.1
requests==2.31.0
pyyaml==6.0.1
```

---

## Local Usage

```bash
# Full sync cycle (manual)
python -m design_token_sync.sync_tokens_extended --write

# Drift check (run before commit)
python -m design_token_sync.drift_check

# Visual regression test (after token changes)
python -m design_token_sync.capture_panels --before
python -m design_token_sync.sync_tokens_extended --write
python -m design_token_sync.capture_panels --after
python -m design_token_sync.visual_diff

# Trigger Figma webhook manually
gh api repos/fredrikjh/lakskongen/dispatches \
  -f event_type=figma_tokens_changed \
  -f client_payload='{"tokens":{...},"version":"2.0.1"}'
```

---

## Configuration

`design_token_sync/config.py` (or env vars):

```python
# Paths
REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent
DESIGN_TOKENS = REPO_ROOT / "design" / "tokens.json"
GODOT_TOKENS = REPO_ROOT / "godot" / "theme" / "tokens.json"
THEME_SCRIPT = REPO_ROOT / "godot" / "theme" / "default_theme.gd"

# Visual regression
PANEL_SCENES = {
    "Dashboard": "res://ui/dashboard/Dashboard.tscn",
    "FinancePanel": "res://ui/finance/FinancePanel.tscn",
    "ToastNotification": "res://ui/common/ToastNotification.tscn",
}
SCREENSHOT_DIR = REPO_ROOT / "godot" / "screenshots" / "design-token-sync"
THRESHOLD = 8  # phash distance

# Godot
GODOT_BIN = os.environ.get("GODOT_BIN", "/Applications/Godot.app/Contents/MacOS/Godot")
```

---

## Godot Headless Invocation

The sync command uses this Godot invocation pattern:

```bash
# Reload tokens in headless mode
$GODOT_BIN --headless --path godot \
  -s res://theme/default_theme.gd \
  --reload-tokens \
  --quit
```

The `LakskongenTheme` script must have a `--reload-tokens` command-line handler. Add to `default_theme.gd` (or a separate autoload):

```gdscript
# In default_theme.gd or a dedicated CommandLineHandler autoload
func _main() -> void:
    if "--reload-tokens" in OS.get_cmdline_args():
        LakskongenTheme.reload_tokens()
        OS.exit(0)  # Success
    if "--check-tokens" in OS.get_cmdline_args():
        var ok = LakskongenTheme._tokens_loaded && LakskongenTheme._tokens_source == "res://theme/tokens.json"
        OS.exit(0 if ok else 1)
```

---

## Anti-Patterns

| Anti-pattern | Problem | Fix |
|--------------|---------|-----|
| Editing `godot/theme/tokens.json` directly | Overwritten by next sync | Only edit `design/tokens.json` |
| Skipping `reload_tokens()` after sync | Theme shows stale values | Always run full `sync` command |
| Running visual-regression headless | Godot produces blank frames | Use self-hosted runner with display |
| Hardcoding token values in GDScript | Drift from source of truth | Use `LakskongenTheme._primary` etc. |
| Committing baseline to PR branch | Pollutes history | Keep visual baselines on `main` only |

---

## Quick Reference

```bash
# Sync tokens (design → Godot → theme reload)
python -m design_token_sync.sync_tokens_extended --write

# CI drift gate
python -m design_token_sync.drift_check

# Visual regression on key panels
python -m design_token_sync.capture_panels --before
python -m design_token_sync.sync_tokens_extended --write
python -m design_token_sync.capture_panels --after
python -m design_token_sync.visual_diff

# Trigger Figma → PR flow
gh api repos/:owner/:repo/dispatches -f event_type=figma_tokens_changed -f client_payload='{"tokens":{...}}'
```