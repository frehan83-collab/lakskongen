# S3 accessibility floor evidence — session 3

*Gate: accessibility audit report + before/after frames. Status: **PASSED** —
token contrast/CVD audit all-green, 221 interactive controls ≥44 px, ui_scale
pair captured, three real UI bugs found & fixed, visual-qa tooling repaired +
baseline promoted.*

## 1. Token audit — `tools/a11y_audit.py` (new, zero-dependency)

Command: `python3.12 tools/a11y_audit.py --json docs/run/evidence/a11y_audit.json`

- **WCAG contrast**: 17/17 pairs PASS (real-usage pairs curated from the code:
  body/secondary/muted text, primary buttons, NC badges, AttentionInbox priority
  badges on their tinted backgrounds, toast dark fills).
- **CVD separation**: 10/10 status pairs PASS under simulated protanopia/
  deuteranopia/tritanopia (delta-E ≥ 16.3; threshold 15). Method: Viénot/Machado
  matrices + CIELAB ΔE76, inline (no third-party deps).
- Dataset: `docs/run/evidence/a11y_audit.json`.

## 2. Interactive targets — harness audit (new `_audit_a11y_targets`)

Run inside the screenshot harness (run22): **221 interactive controls across 9
tabs + HUD + GameUI chrome, 0 under 44×44 px.**
Raw JSON: `docs/run/evidence/a11y_targets.json`.

## 3. ui_scale regression pair

Harness captures `03b_scale80_dashboard` (80%) and `03c_scale125_dashboard`
(125%) — chrome + panels scale with no layout breakage (agent-viewed: tab bar
grows/shrinks, panels reflow; no clipping/overlap).

## 4. Real defects found and fixed (this is the substantive outcome)

| # | Defect | Fix |
|---|--------|-----|
| 1 | `NotificationCenter` entry titles: `Color.WHITE` on a white card → **invisible text** (ratio 1.0) | title → `#2E3440` (12.49:1) |
| 2 | `NotificationCenter` group headers + icons: bright severity fills reused as text (`#EBCB8B`≈1.6:1, `#A3D1DE`≈1.4:1 on light bg) | new `SEVERITY_TEXT_COLORS` (dark variants; ≥5.4:1) |
| 3 | `AttentionInbox` priority badges: lighten(0.4) fills vs dark text = 1.8–3.1:1 | fills lighten(0.78) + darker text map → all ≥4.85:1 |
| 4 | `GameUI._active_tab_id` initialised to `"dashboard"` while the boot switch could be tutorial-locked → **dashboard never rendered** (first `show_tab("dashboard")` short-circuits; panel appeared only after visiting another tab) | init `""`; first successful switch always runs |
| 5 | `toggle_attention_inbox` action missing from `project.godot` while `AttentionInbox.gd` polls it → engine error spam on every key event | action added (physical keycode 73 = I, per AGENTS.md) |

After-state evidence: `03_dashboard.png` (run21+) shows the full dashboard with
readable notification entries; `07g_attention_inbox.png` shows readable badges.

## 5. visual-qa tooling repaired (blocked the baseline promotion before)

- `config.py`: `ROOT` resolved 5 parents up (landed on `.claude/`) → fixed to 6
  (repo root); threshold default recalibrated 8 → **25** with rationale (below).
- `phash.py` / `baseline_update.py`: missing `import os` → NameError on manifest
  build; fixed.
- **Baseline promoted**: `godot/screenshots/baseline/` now holds the 28-shot set
  from run23 (pinned-sim harness) + manifest; `python -m visual_qa.diff` runs
  clean against its own baseline.
- **Repeatability control**: two consecutive pinned-sim runs measured a
  run-to-run noise floor of **12–22 phash distance** (transient toasts + panel
  enter animations on live UI still vary; the sim clock itself is pinned).
  Decision: tool default threshold set to 25 (evidence-backed calibration, not a
  regression gate weakening — structural regressions remain visible; the diff
  gallery stays the human review surface). Logged in RUN_LOG decisions.

## Commands / raw pointers

```
python3.12 tools/a11y_audit.py --json docs/run/evidence/a11y_audit.json   → RESULT: PASS
tools/capture_screenshots.sh                                              → "a11y targets: 221 interactive controls, 0 under 44px"
PYTHONPATH=.claude/skills/ecc/visual-qa python3.12 -m visual_qa.baseline_update --skip-harness --no-commit   → "Manifest saved with 28 shots"
```
