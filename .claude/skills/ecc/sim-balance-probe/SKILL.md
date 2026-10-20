---
name: sim-balance-probe
description: Multi-seed balance sweep for Lakskongen simulation scenarios. Runs N seeded careers per scenario, collects metrics, generates CSV + HTML reports with box plots, and performs Sobol sensitivity analysis. CI gate enforces p95 bands.
origin: project
---

# Sim Balance Probe — Lakskongen Multi-Seed Balance Sweep

This skill wraps the existing `PlayerScriptProbe` and `SimulationEngine` to run large-scale balance sweeps across scenarios and seeds, producing statistical reports and CI gates.

**Stack:** Python · .NET (via `dotnet run`) · matplotlib · pandas · SALib (Sobol) · NumPy/SciPy

---

## Directory Layout

```
.claude/skills/ecc/sim-balance-probe/
├── SKILL.md                   # This file
├── sim_balance_probe/
│   ├── __init__.py
│   ├── config.py              # Configuration (seeds, scenarios, bands, paths)
│   ├── runner.py              # Orchestrates .NET engine runs via CLI
│   ├── metrics.py             # Metric extraction from engine state
│   ├── report.py              # CSV + HTML report generation (box plots)
│   ├── sensitivity.py         # Sobol sensitivity analysis
│   ├── ci_gate.py             # p95 band enforcement
│   └── cli.py                 # Command-line entry point
├── requirements.txt
└── run_probe.py               # Thin wrapper for `python -m sim_balance_probe`
```

---

## Scenarios Tested

| Scenario ID | Name | Description |
|-------------|------|-------------|
| `po3_red_sone` | PO3 Rød Sone | Red-zone MAB cut, closed containment required |
| `forste_sesong` | Familiegården | Tutorial: single site, calm weather, easy market |
| `lofoten_eventyr` | Nordlands Utfordring | Harsh storms, premium prices |
| `laksebaronene` | Trønder-krigen | 3 AI competitors, market share battle |
| `krise_manager` | Finnmarks Gjenfødsel | Bankrupt + active ILA, survival |
| `barekraft_mester` | Lakskongen | Freeplay, 50M start, empire building |
| `isa_utbrudd` | ISA-utbruddet | Mandatory cull zone, biosecurity firewalls |

Additional scenarios loaded from `scenarios.json` are auto-discovered.

---

## Metrics Collected (per run)

| Metric | Source | Description |
|--------|--------|-------------|
| `mortality_pct_per_cycle` | `CareerResult.SeaMortalityPct` | Cumulative sea mortality % |
| `fcr` | `CareerResult.EconomicFcr` | Economic feed conversion ratio |
| `realized_price_per_kg` | `CareerResult.RealizedPricePerKg` | Harvest revenue / kg |
| `ending_equity_nok` | `FinanceManager.Balance - outstanding_loans` | Equity at career end |
| `peak_lice_per_fish` | `CareerResult.PeakAdultFemaleLice` | Max adult female lice/fish |
| `storm_escape_events` | `SimulationEngine.StormEscapeEvents` | Expected containment failures |
| `sustainability_score` | `State.SustainabilityScore` | 0–100 sustainability index |
| `total_loan_principal_nok` | `State.ActiveLoans.Sum(Principal)` | Outstanding debt |
| `harvested_kg` | `CareerResult.HarvestedKg` | Total biomass harvested |
| `days_to_harvest` | `CareerResult.DaysRun` | Simulated days elapsed |
| `event_count` | `CareerResult.EventLogEntries` | Storyteller events fired |
| `save_load_ok` | `CareerResult.SaveLoadRoundTripped` | Mid-run save/load succeeded |

---

## Commands

### 1. run — Execute multi-seed balance sweep

```bash
# From lakskongen root
python -m sim_balance_probe run \
  --scenarios po3_red_sone forste_sesong \
  --seeds 100 \
  --out-dir artifacts/balance_probe
```

**Options:**
- `--scenarios` — Space-separated scenario IDs (default: all unlocked)
- `--seeds` — Number of seeds per scenario (default: 100)
- `--seed-start` — Starting seed value (default: 1)
- `--out-dir` — Output directory (default: `artifacts/balance_probe`)
- `--parallel` — Parallel jobs (default: CPU count)
- `--horizon-days` — Max career days (default: 1200)
- `--no-save-load` — Disable mid-run save/load round trip

**What it does:**
1. For each scenario × seed, invokes the .NET test harness headless
2. Collects all metrics into `balance_probe_<scenario>_<timestamp>.csv`
3. Generates `balance_probe_<scenario>_<timestamp>.html` with box plots
4. Runs Sobol sensitivity on key parameters
5. Exits non-zero if CI gate fails

**Output example:**
```
[sim-balance-probe] Running 2 scenarios × 100 seeds = 200 careers
[sim-balance-probe] po3_red_sone: 100/100 ✓ (avg mortality 15.2%, FCR 1.28, price 83.4)
[sim-balance-probe] forste_sesong: 100/100 ✓ (avg mortality 8.1%, FCR 1.19, price 85.7)
[sim-balance-probe] CSV: artifacts/balance_probe/balance_probe_po3_red_sone_20261009_163045.csv
[sim-balance-probe] HTML: artifacts/balance_probe/balance_probe_po3_red_sone_20261009_163045.html
[sim-balance-probe] Sobol indices written to artifacts/balance_probe/sobol_po3_red_sone_20261009_163045.json
[sim-balance-probe] CI GATE: PASS (all p95 bands satisfied)
```

---

### 2. sensitivity — Run Sobol sensitivity analysis only

```bash
python -m sim_balance_probe sensitivity \
  --scenario po3_red_sone \
  --samples 1000 \
  --params sea_mortality feed_cost lice_pressure storm_loss
```

**Parameters analyzed:**
- `sea_mortality` — Base mortality rate multiplier
- `feed_cost` — Feed cost per kg (NOK)
- `lice_pressure` — External lice pressure multiplier
- `storm_loss` — Storm containment failure probability

**Output:** `sobol_<scenario>_<timestamp>.json` with first-order (S1) and total-order (ST) indices.

---

### 3. report — Regenerate HTML report from existing CSV

```bash
python -m sim_balance_probe report \
  --csv artifacts/balance_probe/balance_probe_po3_red_sone_20261009_163045.csv
```

---

### 4. ci-gate — Check p95 bands against existing CSV (for CI)

```bash
python -m sim_balance_probe ci-gate \
  --csv artifacts/balance_probe/balance_probe_po3_red_sone_20261009_163045.csv
```

**Exit code:** 0 = PASS, 1 = FAIL (any band violated at p95)

---

## CI Gate Bands (p95)

| Metric | Lower Bound | Upper Bound | Source |
|--------|-------------|-------------|--------|
| `mortality_pct_per_cycle` | 14% | 16% | `docs/BALANCE.md` §2a |
| `fcr` | 1.15 | 1.35 | `docs/BALANCE.md` §2b |
| `realized_price_per_kg` | 70 | 96 | `docs/BALANCE.md` §2c |
| `ending_equity_nok` | 0 | — | Solvency guard |

**Failure modes:**
- Any metric's 95th percentile outside band → FAIL
- Insufficient samples (< 30) → WARN (gate passes but flagged)
- Non-finite values (NaN/Inf) → FAIL

---

## .NET Test Harness Integration

The probe uses the existing `PlayerScriptProbe.RunCareer(seed)` via a minimal console app:

```csharp
// SimBalanceProbeRunner.cs (auto-generated at runtime)
var result = PlayerScriptProbe.RunCareer(seed: seed, roundTripSaveLoad: true);
Console.WriteLine(JsonSerializer.Serialize(result));
```

The Python runner:
1. Compiles the test project (`dotnet build LakskongenSim.Tests`)
2. Invokes the runner for each seed
3. Parses JSON output
4. Aggregates into DataFrame

**No Godot required** — pure C# simulation engine.

---

## Python Dependencies

```txt
pandas>=2.2.0
numpy>=1.26.0
scipy>=1.12.0
matplotlib>=3.8.0
seaborn>=0.13.0
SALib>=1.5.0
jinja2>=3.1.0
```

Install:
```bash
pip install -r /Users/fredrik.j.hansen/lakskongen/.claude/skills/ecc/sim-balance-probe/requirements.txt
```

---

## CI Integration (GitHub Actions)

```yaml
# .github/workflows/balance-probe.yml
name: Balance Probe

on:
  pull_request:
    branches: [main]
  workflow_dispatch:

jobs:
  balance-probe:
    runs-on: ubuntu-latest
    timeout-minutes: 60
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - uses: actions/setup-python@v5
        with:
          python-version: '3.12'
          cache: pip
      - name: Install deps
        run: pip install -r .claude/skills/ecc/sim-balance-probe/requirements.txt
      - name: Build test project
        run: dotnet build src/LakskongenSim.Tests/LakskongenSim.Tests.csproj -c Release
      - name: Run balance probe
        run: |
          python -m sim_balance_probe run \
            --scenarios po3_red_sone forste_sesong \
            --seeds 50 \
            --out-dir artifacts/balance_probe
      - name: Upload artifacts
        uses: actions/upload-artifact@v4
        with:
          name: balance-probe-reports
          path: artifacts/balance_probe/
```

---

## Local Usage

```bash
# Quick smoke test (2 scenarios × 10 seeds)
python -m sim_balance_probe run --scenarios po3_red_sone forste_sesong --seeds 10

# Full sweep (all unlocked scenarios × 100 seeds)
python -m sim_balance_probe run --seeds 100

# Sensitivity only (1000 Sobol samples)
python -m sim_balance_probe sensitivity --scenario po3_red_sone --samples 1000

# CI gate check on existing results
python -m sim_balance_probe ci-gate --csv artifacts/balance_probe/balance_probe_po3_red_sone_latest.csv
```

---

## Configuration (`config.py`)

```python
# Scenarios to include (empty = all unlocked from ScenarioSystem)
SCENARIOS = ["po3_red_sone", "forste_sesong", "lofoten_eventyr", "laksebaronene", "krise_manager", "barekraft_mester", "isa_utbrudd"]

# Default seeds per scenario
DEFAULT_SEEDS = 100
SEED_START = 1

# Career horizon (days)
CAREER_HORIZON_DAYS = 1200

# Output directory
OUT_DIR = "artifacts/balance_probe"

# CI gate bands (p95)
CI_BANDS = {
    "mortality_pct_per_cycle": (14.0, 16.0),
    "fcr": (1.15, 1.35),
    "realized_price_per_kg": (70.0, 96.0),
    "ending_equity_nok": (0.0, float("inf")),
}

# Sobol parameters
SOBOL_PARAMS = {
    "sea_mortality": (0.8, 1.2),      # multiplier on base mortality
    "feed_cost": (0.85, 1.15),         # NOK/kg multiplier
    "lice_pressure": (0.7, 1.5),       # external pressure multiplier
    "storm_loss": (0.5, 2.0),          # failure prob multiplier
}
```

---

## Anti-Patterns

| Anti-pattern | Problem | Fix |
|--------------|---------|-----|
| Running without `dotnet build` | Stale test binary | Always build before run |
| Using Godot for headless runs | Slow, needs display | Use `SimulationEngine` directly via .NET CLI |
| Hardcoding scenario list | Misses new scenarios | Auto-discover from `ScenarioSystem.All` |
| Skipping save/load round trip | Misses serialization bugs | Keep enabled by default |
| Single seed per scenario | No statistical power | Minimum 30, default 100 |
| Ignoring Sobol indices | Blind to param sensitivity | Run sensitivity monthly |

---

## Quick Reference

```bash
# Full cycle: run → report → (if happy) baseline-update
python -m sim_balance_probe run --scenarios po3_red_sone forste_sesong --seeds 100
python -m sim_balance_probe report --csv artifacts/balance_probe/balance_probe_po3_red_sone_*.csv

# CI gate (used in GH Actions)
python -m sim_balance_probe ci-gate --csv artifacts/balance_probe/balance_probe_po3_red_sone_latest.csv

# Sensitivity deep dive
python -m sim_balance_probe sensitivity --scenario po3_red_sone --samples 2000
```