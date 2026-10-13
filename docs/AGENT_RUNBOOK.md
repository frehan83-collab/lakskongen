# Agent Runbook — Long Autonomous Run (LakskongenSim)

*Purpose: the exact step-by-step operating procedure every agent session follows.
The WHAT (waves, briefs, D-criteria) lives in `docs/AUTONOMOUS_TOP_TIER_RUN.md`;
the STATE (what is done / next / blocked) lives in `docs/RUN_LOG.md` (single source
of truth). This doc is the HOW — the session loop plus the hard-won rules from
sessions 0–4. Read the other two first; obey this one every session.*

---

## 1. The fixed session loop (repeat every session, in order)

1. **Bootstrap.** Read `AGENTS.md` → `docs/AUTONOMOUS_TOP_TIER_RUN.md` →
   `docs/RUN_LOG.md` (Status header + tail of the Session ledger). Run `./tools/ci.sh`
   once on the current tree. Record the raw result.
2. **Audit the prior session.** Pick ONE claim from the last session report; re-run
   its cited command; compare raw output. Log PASS/FAIL. A failed audit marks
   downstream work *suspect* until re-verified (note it in Blocked/dead-ends).
3. **Claim before work.** Set `Active claim:` in the RUN_LOG Status header
   (session #, wave/item, ONE deliverable). If a stale claim exists (>1 session
   old), verify it or release it.
4. **Pre-register the done-criteria.** If the item has a failure threshold or a
   gate, write it in the item/file header *before* implementing. Never decide
   "done" after seeing the result (see §2 rule 6).
5. **Implement the smallest complete slice** behind its evidence gate
   (player-path test / screenshot diff / perf number / save-hash).
6. **CI.** Run `./tools/ci.sh`; record the raw result (15 steps, must end `CI PASSED`).
7. **Report.** Append the session report (RUN_LOG §10 template); update the Status
   header (done / next / blocked / gates / last-verified-artifact); update
   `AGENTS.md` if an SSOT fact changed; compact context at wave boundaries
   (`strategic-compact` skill).
8. **Continue** unless a STOP condition fired (§6).

Every session ENDS with: a RUN_LOG session report, an updated Status header, and a
5-line chat mirror. The "resume prompt" (top of the mission) restarts this loop.

---

## 2. Hard-won operating rules (sessions 0–4 — do not relearn these)

1. **Serialize `dotnet`.** Never run `dotnet build`/`dotnet test` while `./tools/ci.sh`
   is running — MSBuild node-reuse on the same solution causes silent
   contention/garbage output. CI first, or CI last; not concurrently.
2. **godot MCP repairs after CI.** Every CI run boots headless editors that rebuild
   the MCP registry and churn the project entry. Before any godot MCP tool use,
   run `python3 tools/mcp_bridge_alias.py`:
   - `exit 0` + `OK: auth probe authed` → good;
   - `exit 2` → on-disk token ≠ editor memory → restart the Godot editor, re-run.
   Full root cause + procedure: `docs/research/MCP_CAPABILITIES.md` §3.
3. **Screenshot harness determinism.** Harness is non-headless only (headless
   renders no pixels). `_shoot(force_hide_ui=true)` only for fjord shots. Sim clock
   is pinned after boot (run-to-run reproducibility). Forced states go through QA
   bridge methods (`StartQaScenario(seed)`, `SetQaPenLice`, `SpawnQaBloom`,
   `ClearQaBlooms`) — stage via public methods, never sim internals.
4. **Determinism everywhere.** Seeded QA runs; `SimRng` SplitMix64 streams from the
   frozen `SimRngStreams` enum (create eagerly in `Initialize`); never
   `System.Random`/`DateTime.Now`/`Guid.NewGuid()` in `src/`. Calibration numbers
   live in `CalibrationRegistry`/`BalancingConfig` — grep fails on literals in tick
   code. No clamps to hit realism bands.
5. **Add-only scoreboard.** Tests, CI config, thresholds, D1–D10 may only be
   strengthened. If a test looks wrong: write a `Dispute` row (RUN_LOG §8), mark the
   item blocked, stop. Editing the scoreboard to get green is a human gate.
6. **Evidence contract (RUN_LOG §1).** Every "done" = file path + exact command +
   raw output (commit SHA, CI result, test counts, screenshot paths, perf numbers).
   Never cite an earlier session's green — re-run it. No "should work"/"likely
   fine"/edited thresholds/skipped tests.
7. **Free-first.** No paid API/subscription without a human sign-off on a cost table
   (plan §7). Any spend proposal is itself a STOP condition.
8. **Bridge trio.** UI-facing engine features: C# partial in `GameBridge.<Area>.cs`
   + GDScript proxy in `SimProxies.gd` + forwarder in `SimBridge.gd`; the contract
   test (`tests/test_bridge_contract.py`) parses all partials.
9. **Repo is the only memory.** If it isn't committed, it didn't happen. One commit
   per completed deliverable; message format `run: <wave> <item> — <one-line outcome>`.

---

## 3. Agent roles

| Agent | Use | Constraints |
|---|---|---|
| **Main agent** | owns the session loop; implements the claimed item; runs CI; writes reports | one deliverable per claim; serialize dotnet |
| **Research subagent** (`general`, background) | one per brief; Firecrawl-first; writes only `docs/research/<FILE>.md` | must not touch `src/` or `godot/`; every claim cited; cost table mandatory (R1–R4); 10-line exec summary; 2025–2026 sources with dates |
| **Code subagent** | mechanical fan-out only (rename, add XML docs) with a strict file list | review the diff before CI |

Parallelism: research subagents unbounded (they only write new docs); code subagents
at most 2 writing overlapping areas.

---

## 4. Remaining path (as of session 4: W0–W2 + S10 done)

| Wave | Item | Gate |
|---|---|---|
| **W3 — Realism × data** | **S4** data layer v2: embed chosen free datasets as offline JSON snapshots (CMEMS SST/waves/chl, MET Norway, Fiskeridir biomass/lice aggregates, BarentsWatch server-side, ERA5 wind/wave) + provenance (source/license/retrieval date); optional live client behind a config flag with offline fallback | G-LIC + player-path test showing realism bands shift with snapshot data |
| **W4 — Ship mechanics** | **S5**: Windows/Linux/macOS export presets + CI export job + artifact upload; version stamping; save-fuzz harness (10 000 cycles, 0 divergence); crash/log capture; Steam depot automation script (human runs the upload) | downloadable CI artifacts launch per OS; save fuzz clean |
| **W5 — Demo slice** | **S6**: polish `forste_sesong` (tutorial) + `po3_red_sone` (red-zone drama); first-15-min experience; demo build for playtests | D1 playtest protocol ready + demo build passes W4 gates |
| **W6 — Presentation** | **S7** ranked visual upgrades (from `docs/research/TECH_GRAPHICS.md`: shore/foam water, sky/HDR, terrain anti-tiling, GI tiers, instancing, underwater, decals; FFT parked) + **S8** audio identity (music/ambient/SFX, mix buses, ≈ −16 LUFS menu / −20 game) + store art | per-item G-SHOT + G-PERF; G-LIC + Steam AI disclosure for audio |
| **W7 — Hardening** | **S9** perf/soak (2-h soak, memory flat ±5%), save fuzz, career-divergence, release candidate; D2–D7 evidence assembly | G-PERF, G-SAVE, D2–D7 |

Already done (evidence in `docs/run/evidence/`): **W0** ignition (MCP repair, briefs
A–E dispatched, 2 skills authored, baseline `bc46c90`), **W1** decisions
(`docs/research/W1_DECISIONS.md`), **W2** S1 living-fjord (phash 16 pairs), S2
time-series UI, S3 accessibility floor (audit 17/17+10/10, 5 UI defect fixes,
visual baseline promoted), **S10** career-divergence probe (PASS, +299.5 t / +18.45M NOK).

Critical path: W3(S4) → W4(S5) → W5(S6) → W7(S9) → human gates.

---

## 5. Evidence gates catalog (plan §10)

| Gate | Proof | Applies to |
|---|---|---|
| G-CI | `./tools/ci.sh` 15/15 | every wave |
| G-TEST | player-path test showing outcome change | S1–S4, S10 |
| G-SHOT | captured + labeled frames, distinct under forced states | S1/S2/S3/S7, store art |
| G-PERF | perf report vs D3 budget (M1 Pro + Steam Deck) | S7, S9 |
| G-SAVE | save fuzz + ExactSaveHashTests + twin-run determinism | S4, S5, S9 |
| G-LIC | source/license/provenance note per dataset or asset | S4, S8 |
| G-COST | signed-off cost table before any paid dependency | R1–R4, S8 |

---

## 6. STOP conditions (RUN_LOG §3) — halt, write HANDOFF NEEDED

- **Human gate reached** (plan §12): Steam account/depot creds; playtest protocol;
  paid-service sign-off; capsule/trailer/price approval; legal; **any push to remote**.
- **Any spend proposal** (free-first, plan §7).
- **Circuit breakers** (log only): CI red in two consecutive sessions; same
  deliverable failed twice; a target metric unmoved across two waves.
- **All remaining unblocked work exhausted.**

---

## 7. Human gates — only the owner can close (plan §12)

1. Steam partner account: fee, KYC, tax, depot upload credentials.
2. Playtest: run the 5×2 h protocol + the no-dominant-strategy sweep (D1).
3. Cost sign-offs for any paid service.
4. Final approval of capsule art, trailer, price, Next Fest submission.
5. Legal: EULA, privacy policy, AI-content disclosure, age ratings.
6. Any push to a remote.

---

*Relevant SSOT pointers: `AGENTS.md` (engineering), `docs/AUTONOMOUS_TOP_TIER_RUN.md`
(waves/briefs/D-criteria), `docs/RUN_LOG.md` (protocol + live state),
`docs/research/W1_DECISIONS.md` (wave-order decisions).*
