# Golden traces (F2-01)

The golden traces are the standing sim gate of the farm-types work
(`docs/FARM_TYPES_SPEC.md` §7, item F2-01): a recording of what every shipped
scenario does, day by day, on the pre-F2 code. A sim item that must not change
the game proves it by matching them; an item that does change the game
re-records them and ships a diff report that says what moved, where and from
which day.

- Recorder: `src/StateHashProbe/GoldenTraceRecorder.cs` (+ `GoldenTraceState.cs`,
  `GoldenPlayer.cs`, `GoldenTraceComparer.cs`, `GoldenTraceSet.cs`, `GoldenCli.cs`)
- Script: `tools/record_golden_traces.sh` (records any commit in a temporary git worktree)
- Fixtures: `src/LakskongenSim.Tests/Fixtures/golden_traces/` (27 `.jsonl.gz` and `manifest.json`, 1.8 MB)
- Tests: `src/LakskongenSim.Tests/Integration/GoldenTraceTests.cs` (trait `Category=GoldenTrace`)
- Pre-F2 save fixture: `src/LakskongenSim.Tests/Fixtures/save_v8_facilities.json`
  (`src/StateHashProbe/PreF2SaveFixture.cs`, `Integration/PreF2SaveFixtureTests.cs`)

## What is recorded

Every shipped scenario (the 9 of `godot/data/scenarios.json`) × seeds 1, 7 and
13 × 730 days, events on, played by the **golden player** (`golden-player/1`), a
scenario-agnostic sea farmer that decides from the game state only:

| Rule | Value |
|---|---|
| Delouse a site (hydrolicer) | adult females per fish above 0.4, at most every 14 days |
| Order a planned harvest of a site (S2-04 pipeline) | mean live weight ≥ 4.5 kg and no open order there |
| Restock an empty site (S2-01a/b checks) | after 30 fishless days, on days divisible by 7: as many smolts as `MaxStockableSmolts` allows and the cash above 1 M pays for, counting each smolt's price plus the feed to 4.5 kg (calibrated feed cost per kg produced), in thousands, at least 10,000 |
| Research | once, after the first harvest batch is sold: the cheapest node it can start, if cash covers it plus 1 M |

It never builds a site or a facility: F2-03 turns the facility verbs into the
new unit kinds and must leave these traces unchanged.

One file per run, `<scenario>.seed<NN>.jsonl.gz`, one JSON object per line:

1. **Header**: format (`lakskongen-golden-trace/1`), scenario, seed, days,
   snapshot interval, player version, the groups and what each holds, the line
   formats, the **excluded fields**, and `recordedOn` (commit, runtime id,
   .NET version, math fingerprint; metadata, never compared).
2. **A day line for days 0 to 730**, the state after that day's player commands:
   - `h`: SHA-256 (64 bit) of every group's canonical text and every stream
     state: it detects a divergence on the day it happens;
   - `g`: an 8-bit hash per group, in cause-before-effect order: `env` (weather
     and each zone's water), `market`, `pop` (each population by pen: count,
     biomass, each cohort's count, mean weight, CV, age, stage), `lice`, `mtb`
     (regime, per area licences, pooled MTB, standing biomass, open order; per
     site locality MTB), `mort` (S2-05 ledger by cause and removal kind), `ops`
     (harvest orders and counters, research, sustainability, capacity breaches,
     sites' gill/net/orders), `ledger` (P&L lines, balance sheet, lifetime
     totals, cost and revenue lines by category), `cash` (balance, debt,
     unbooked), `rivals` (each AI competitor), `rng`;
   - `rng`: each SimRng stream's draws that day (the SplitMix64 state advances
     by a fixed gamma per draw, so a state difference times the gamma's inverse
     is the draw count);
   - `ev`: event-log entries added since the previous line; `cmd`: the golden
     player's commands and outcomes; `st`: the scenario status when it changes.
3. **A snapshot line on days 0, 30, …, 720 and 730**: `v`, every group's exact
   values (`entity → field=value;…`, doubles round-trip, decimals without
   trailing zeros); `save`, SHA-256 (64 bit) of the GUID-normalized save
   (`PrepareForSave` + `SaveJson.Options`, what a save file holds); `tree`,
   16-bit digests of the save's nodes (every top-level member, then objects and
   arrays of up to 64 children to depth 3), listing only nodes that changed
   since the previous snapshot, and `gone`, the nodes that disappeared.

Excluded (also in every header): Guid-valued ids (process-random by design;
populations are keyed by pen, tank or holding, never by `Id`); fields outside the
day groups between snapshots (the save digest covers them every 30 days); event
descriptions (the day line lists event ids); `-0.0` and trailing decimal zeros
in the day groups (equal values); nothing outside `GameState` is needed, since a
save resumes bit-identically (`SaveLoadMatrixTests`).

## Verdicts

`GoldenTraceComparer.Compare(expected, actual)`:

- **identical**: every day line and every snapshot's values match. This is the
  game verdict the gate uses.
- **differs**: the first difference names the scenario, seed, day and field.
  A compact trace names the group on the day (`day 200, field pop`) and the
  next snapshot names the entity and its fields with both values
  (`day 210, pop › po3_nord/p01 › kg, c0.w: expected … actual …`); a
  `--detail` recording names the entity and field on the day itself. The
  report adds the days that differ per group (in compact traces a lower bound:
  an 8-bit group hash misses 1 differing day in 256, the day hash none) and
  the final cash and biomass.
- **save drift** is reported apart and never makes a trace "differ": the whole
  save changed while the game did not. An item that only adds save fields
  (F2-03's plots and kinds) drifts the save; the digest tree lists changed
  leaves, added nodes and removed nodes, and the PR explains each one.

## The tests

`dotnet test LakskongenSim.sln` runs them (≈7 s for the 27 comparisons on an M1
Pro; `--filter Category=GoldenTrace` runs them alone):

- `GoldenTraceSeed{1,7,13}Tests`: each shipped scenario re-recorded in process
  and compared with its fixture, day by day;
- `GoldenTraceHarnessTests`: the fixtures cover every shipped scenario × {1, 7,
  13} × 730 days with 26 snapshots and their headers list the excluded fields;
  recording twice gives the same bytes, in this process and in another one; a
  1-ulp raise of one cohort's weight on day 200 fails and names
  `po3_red_sone`, seed 7, day 200 and `pop › <pen>` (and `c0.w` in detail); a
  krone booked outside the ledger on day 300 is named as cash; the player
  harvests in every trace and restocks and researches in most.

**Platform.** .NET takes `Math.Sin`, `Exp`, `Pow`, `Log`, `Cbrt` and the other
transcendentals from the platform's C library, so bit-exact traces are
comparable only on a platform whose library agrees. The recorder hashes those
functions over fixed inputs (`MathFingerprint`, in the header and the
manifest). Where the fingerprint differs from the fixtures' (`osx-arm64`,
`8481617aed3cdd1d`), the fixture comparison only reports and the gate is the
base-vs-head recording on one machine below, which CI runs on every PR.

## Workflow for later sim items

Commit first: the script records commits, never the working tree.

**An item that must not change the game** (F2-02, F2-03, F2-05 … and every
refactor):

```bash
tools/record_golden_traces.sh --base "$(git merge-base HEAD main)"
# == diff report: /tmp/golden_traces.XXXX/out/diff_report.md  → "27 of 27 traces identical."
dotnet test LakskongenSim.sln --filter Category=GoldenTrace
```

Exit 0 means identical. Save drift in the report is allowed only for save
fields the item adds; list them in the PR.

**An item that changes the game on purpose** (F2-04 research nodes, F2-10
closed pens, F2-13 smolt tiers, any balance change):

```bash
git commit …                                   # the change
tools/record_golden_traces.sh --update         # re-record HEAD into the fixtures
tools/record_golden_traces.sh --base "$(git merge-base HEAD main)" \
    --report docs/run/evidence/<item>_golden_diff.md
git add src/LakskongenSim.Tests/Fixtures/golden_traces docs/run/evidence/<item>_golden_diff.md
git commit -m "run: <item> — re-record the golden traces (diff report)"
```

The report must show that only the runs the item is meant to touch changed
(F2-04: only runs that research its nodes), and a RUN_LOG §7 row says why.

**Finding where two commits part**: record both with `--detail` (exact values
every day; never commit these):

```bash
tools/record_golden_traces.sh --base <good> --commit <bad> --detail --scenarios po3_red_sone --seeds 7
```

**Other options**: `--against DIR` diffs a recording against a recorded set
(e.g. the fixtures); `--scenarios`, `--seeds`, `--days`, `--jobs` narrow or
widen a recording; `StateHashProbe golden diff A B --report FILE` diffs two
directories without recording.

**Changing the recorder or the player** changes the traces: bump
`GoldenTraceRecorder.Format` or `GoldenPlayer.Version`, re-record, and keep the
old meaning documented here.

## CI

- `verify.yml` / `dotnet` and `ci.yml` / `tools/ci.sh`: `dotnet test` runs the
  fixture comparison (enforced where the math fingerprint matches).
- `verify.yml` / `golden-traces` (pull requests): records the PR's merge base
  and its head on one runner with `tools/record_golden_traces.sh --base`, writes
  the report to the job summary and fails on any difference. A PR that changes
  the game on purpose fails it by design and carries the re-recorded fixtures
  and the report.

Measured on the recording machine (M1 Pro, shared): one recording of the 27
runs takes 6 s with 4 jobs; the script (temporary worktree, build, record) 10 s
a commit, base and head 20-25 s; the 27 comparisons in `dotnet test` 7 s.

## The pre-F2 save fixture

`src/LakskongenSim.Tests/Fixtures/save_v8_facilities.json` is a game save file
(the Godot `SaveLoadManager` wrapper: `SaveName`, `SaveTime`, `GameVersion`,
`SaveVersion` 8, `GameState`) at the save version current before any F2 schema
change (the spec's "v3" predates waves 1 and 2): `po3_red_sone` seed 3 on day 31
with one operational RAS facility with 2 stocked tanks, one SemiSubmersible
offshore farm with 2 stocked pens, one post-smolt facility with 2 batches and a
hatchery egg cohort. Its Guids are renumbered in order of appearance and its
save time is fixed, so `StateHashProbe golden save-fixture --out FILE`
regenerates it byte for byte on the pre-F2 commit. It is never regenerated
after that (later commits build the facilities as F2's unit kinds);
`PreF2SaveFixtureTests` pins its SHA-256 and that the pre-F2 engine loads it
with every fish and runs on. F2-03's `FacilityMigrationTests` migrate it.
