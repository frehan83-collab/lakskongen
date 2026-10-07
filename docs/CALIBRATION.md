# Calibration

How Lakskongen keeps game balance tied to real Norwegian aquaculture data, and
how drift is caught before it ships.

There are two independent "single sources of truth" in this document:

1. **Game balance** — `CalibrationRegistry` + `godot/data/calibration.json`
2. **Design tokens / UI theme** — `design/tokens.json` → `godot/theme/tokens.json`

---

## 1. Balance: single source of truth

**Code record:** `src/LakskongenSim/Calibration/CalibrationRegistry.cs`
**Data file:** `godot/data/calibration.json`

`CalibrationRegistry` is an **immutable** (`init`-only records) source of truth
for every balance constant across economy, labor, infrastructure, operations,
health, finance, licensing, biology, environment, water quality, compliance,
research, market and feed. Because records are immutable after construction, no
subsystem can mutate a balance value at runtime and silently drift.

### Load order

`CalibrationRegistry.Load(dataDirectory?)` probes, in order:

1. `calibration.json` in an explicitly supplied `dataDirectory`.
2. Common repo/export locations (e.g. `godot/data/calibration.json`,
   `<baseDir>/godot/data/...`, `<baseDir>/data/...`, `../Resources/data/...`).
3. The **embedded** copy — `EmbeddedResource`
   `LakskongenSim.Calibration.calibration.json`, wired in `LakskongenSim.csproj`
   to point at `godot/data/calibration.json`.
4. Code defaults on the record.

Loading never throws: a malformed external file silently falls back to the next
candidate. `CalibrationRegistry.Default` is a process-wide cached instance.
`CalibrationRegistry.Parse(json)` is used by tests.

### Versioning

`CalibrationRegistry.DefaultVersion` (`"1.0.0"`) is the balancing/schema version.
**Bump it whenever a reconciled value changes** — this is enforced (see §2).

### Derived values

Some figures are derived, not stored. The canonical example is
`FeedCostPerKgProducedNOK = Feed.FeedPricePerKgFeedNOK * Feed.BaseFCR`
(≈ 22.3 × 1.15 ≈ 25.7 NOK/kg produced). It has no JSON property (`[JsonIgnore]`)
and a dedicated guard test exists specifically to stop regression to the old
flat `12.5 NOK/kg fish` model.

### Editing calibration.json

`godot/data/calibration.json` uses the C# property names (camelCase) as JSON
keys, e.g.:

```json
{
  "version": "1.0.0",
  "economy": { "baseSalmonPricePerKgNOK": 70, "smoltCostPerFishNOK": 12 },
  "feed": { "feedPricePerKgFeedNOK": 22.3, "baseFCR": 1.15 }
}
```

After any change, update `DefaultVersion` and run the guard tests.

---

## 2. Guard tests

**File:** `src/LakskongenSim.Tests/Calibration/CalibrationGuardTests.cs`

> Note: the task brief referenced `LakskongenSim.Tests/Calibration/...`; the file
> actually lives under the `src/` test project root at
> `src/LakskongenSim.Tests/Calibration/CalibrationGuardTests.cs`.

One test per domain. Each assertion names the **real reference** and the
**tolerance** that separates "calibrated" from "drifted", so art/reality/code
drift fails the suite loudly. Tests load via `CalibrationRegistry.Load()` (so an
external `calibration.json` can intentionally override the embedded copy).

| Test | Anchors / tolerance |
|---|---|
| `Version_IsPinned` | `Version == DefaultVersion` |
| `Economy_MatchesRealReferences` | spot 62–78, smolt 8–20 |
| `Labor_MatchesRealReferences` | worker 36k–60k, manager 55k–85k |
| `Infrastructure_MatchesRealReferences` | capex ±50%; `MaxBiomassPerSite == MABLimit` |
| `Operations_MatchesRealReferences` | harvest 3–5, processing 7–11, transport 0.015–0.035 |
| `Health_MatchesRealReferences` | lice 0.49–0.51, sea mortality 0.13–0.17 |
| `Finance_MatchesRealReferences` | resource rent 0.24–0.26, corporate 0.22 |
| `Licensing_MatchesRealReferences` | MAB 779,000–781,000 kg |
| `Biology_MatchesRealReferences` | harvest weight 4.5–5.5 kg, FCR 1.14–1.15 |
| `Environment_MatchesRealReferences` | 8–14°C optimal, ≤22°C lethal |
| `WaterQuality_MatchesRealReferences` | DO 0.4–0.8, density 20–30 kg/m³ |
| `Compliance_MatchesRealReferences` | ASC premium 5–20%, organic 10–30% |
| `Research_MatchesRealReferences` | base 225k–900k, max 2.25M–9M |
| `Market_MatchesRealReferences` | long-term mean 74–90, 12 seasonal offsets |
| `Feed_MatchesRealReferences` | FCR 1.14–1.15, feed price 15–30 |
| `Feed_CostModel_PriceTimesFcrMatchesRealFeedCostPerKgProduced` | 25.7 ± 4, and **> 20** |

Named sources (as recorded in the test file and `docs/REALISM_CHECKLIST.md`):
Fish Pool / Euronext SISALMONI, Fiskeridir Key Figures, Mowi/SalMar quarterlies,
Vet Institute Fish Health Report, Mattilsynet/BarentsWatch, Skatteetaten
Prop.1 LS.

### Run the guards

```bash
dotnet test src/LakskongenSim.Tests --filter CalibrationGuardTests
```

---

## 3. Design tokens → theme pipeline

**Script:** `tools/sync_tokens.py`
**Source:** `design/tokens.json`
**Synced copy:** `godot/theme/tokens.json` (loaded at runtime by `LakskongenTheme`)

`design/tokens.json` is the single source of truth for the design system. The
sync script copies it byte-for-byte into the Godot project so art and code cannot
drift.

```bash
python3 tools/sync_tokens.py            # write/sync (default)
python3 tools/sync_tokens.py --write    # explicit write
python3 tools/sync_tokens.py --check    # CI guard: non-zero exit on drift
```

`--check` compares bytes and exits non-zero if `godot/theme/tokens.json` is
missing or differs, printing `Run: python3 tools/sync_tokens.py --write`. Wire
`--check` into CI. Only the Python standard library is used, so it runs anywhere.

Flow: `design/tokens.json` → `sync_tokens.py --write` → `godot/theme/tokens.json`
→ `LakskongenTheme` derives the UI theme at runtime.

---

## 4. Quarterly realism review

**File:** `docs/REALISM_CHECKLIST.md` — reviewed Jan / Apr / Jul / Oct.

Real aquaculture data moves, and stale calibration silently unbalances the game.
Each quarter, re-check every item against the named source and update
`godot/data/calibration.json` (bump `DefaultVersion`) plus any constants that
have not yet migrated into the registry. Record the review date and changes in
the checklist's **Review log**.

The checklist covers:

- **Prices & costs** — salmon spot (Fish Pool/Euronext SISALMONI via
  FishFarmingExpert, SalMar/Mowi quarterlies), feed cost/kg produced
  (Fiskeridir Key Figures Tab.38, Mowi annual), FCR (SalMar sustainability
  reports), EBIT/kg band, resource rent tax (Skatteetaten, Prop.1 LS), MAB and
  licence value (Fiskeridir).
- **Biology** — smolt size (Fiskeridir smolt stats), harvest size (Mowi
  Handbook, SalMar), sea mortality/cycle (Vet Institute Fish Health Report),
  lice action limit (Mattilsynet/BarentsWatch), treatment mortality (NVI Walde
  et al.), RAS/post-smolt share (Intrafish, AKVA).
- **Regulation & events** — traffic-light rulings (NFD via SalmonBusiness /
  WeAreAquaculture), the Apr-2025 White Paper policy fork, and shock templates
  (algal blooms, tariff shifts, PD/ISA counts from VetInst annual).

Closing the loop each quarter:

1. Review `docs/REALISM_CHECKLIST.md` against the named sources.
2. Update `godot/data/calibration.json` and bump `DefaultVersion`.
3. Run `CalibrationGuardTests`; adjust tolerances only with a documented reason.
4. Run `python3 tools/sync_tokens.py --check` for the design system.
5. Add a row to the checklist **Review log** (date, reviewer, changes).
