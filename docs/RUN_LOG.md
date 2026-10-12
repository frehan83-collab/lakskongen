# RUN LOG — Autonomous Top-Tier Run

> **Normative protocol for the long autonomous run.**
> `docs/AUTONOMOUS_TOP_TIER_RUN.md` defines **WHAT** (waves W0–W7, D1–D10, Briefs A–E).
> This file defines **HOW** sessions run, **what counts as evidence**, and **where the run stands**.
> The repo is the only cross-session memory. Chat dies with the context window — if it is not
> written here (or another committed file), it never happened.

---

## 0. Protocol — every session, in order

1. **Bootstrap.** Read `AGENTS.md` → `docs/AUTONOMOUS_TOP_TIER_RUN.md` → this file
   (Status header + tail of Session ledger). Run `./tools/ci.sh` once on the current tree.
2. **Audit the prior session.** Pick ONE claim from the last session report; re-run its
   cited command; compare raw output. Log PASS/FAIL. A failed audit marks downstream
   work *suspect* until re-verified (note it in Blocked/Dead-ends).
3. **Claim before work.** Set `Active claim:` in the Status header (session #, wave, ONE
   deliverable). If a stale claim exists (>1 session old), verify it or release it.
4. **Pre-register wave criteria.** When starting a new wave, fill its row in the Wave
   register *before* implementing — done means what the row said, not what is convenient.
5. **Implement the smallest complete slice** behind its evidence gate
   (player-path test / screenshot diff / perf number / save-hash).
6. **CI.** Run `./tools/ci.sh`; record the raw result.
7. **Report.** Append the session report (template in §10); update the Status header
   (done / next / blocked / gates / last-verified artifact); update `AGENTS.md` if a
   phase/SSOT fact changed; compact context at wave boundaries.
8. **Continue** unless a STOP condition (§3) fired.

---

## 1. Evidence contract

- Every "done" = **file path + exact command + raw output** (commit SHA, CI result, test
  counts, screenshot paths, perf numbers). Nothing else counts.
- **Never cite an earlier session's green** — re-run it yourself.
- **Screenshot claims**: path + which forced state + distinctness method (md5/diff).
- **Auto-fail fake-done blacklist**: "should work", "likely fine", edited thresholds,
  skipped/ignored/deleted tests, error suppression, claiming a gate without raw output.

## 2. Scoreboard is add-only

Tests, CI config, release thresholds and D1–D10 criteria may only be **strengthened**,
never weakened. If a test looks wrong: write a `Dispute` entry in §8, mark the item
blocked, and stop — editing the scoreboard is a human gate.

## 3. STOP conditions (halt the loop, write HANDOFF NEEDED)

- **Human gate reached** (plan §12): Steam account/depot credentials; playtest protocol;
  paid-service sign-off; capsule/trailer/price approval; legal; **any push to a remote**.
- **Any spend proposal** — always a human gate (free-first, plan §7).
- **Circuit breakers** (log, no human needed to trigger):
  - CI red in two consecutive sessions;
  - the same deliverable failed twice;
  - a target metric unmoved across two waves.
- **All remaining unblocked work exhausted.**

## 4. Status header — EDIT IN PLACE

- **Run phase:** `W2 — in progress (session 2): S1 DONE (phash 16); S2 DONE (Trender block + price spark, analytics test 2/2); next: W2/S3 accessibility floor`
- **Active claim:** `session 2 — W2/S2 time-series UI (shipped; CI result in session 2 report)`
- **Gate state:** `no human gates hit`
- **Last verified artifact:** `S2 evidence docs/run/evidence/session_02_s2_frames.txt (06_finance before↔after phash 18; 06b trend card 1920×1080); analytics series test 2/2`
- **Baseline freeze:** `main suite 684 passed / 1 skipped (session 0 rerun, raw log docs/run/evidence/session_00_dotnet_test.txt); CI 15/15; save-hash: comparative gate green (ExactSaveHashTests); golden SHA artifact deferred to W4 save-fuzz`

### Session-0 note
After CI is green, create a **local baseline commit** (do NOT push — pushing is a human
gate) so every evidence claim can cite a SHA. From then on, one commit per completed
deliverable, message format: `run: <wave> <item> — <one-line outcome>`.

---

## 5. Session ledger (append-only)

| # | Date | Wave | Claim | Evidence (path + cmd + result) | Audit of prior | CI |
|---|------|------|-------|-------------------------------|----------------|----|
| 2 | 2026-10-12 | W2/S2 | time-series UI: price spark + Trender block replace finance text wall | `docs/run/evidence/session_02_s2_frames.txt` (phash 18; 06b md5 53eb0715; analytics test 2/2); `session_02_ci_final_summary.txt`; commits pending | session-1 phash/md5 re-run: exact match 16/16 PASS | PASSED 15/15 |
| 1 | 2026-10-12 | W2/S1 | living fjord readout: forced-state frames (lice collar + bloom) distinct | `docs/run/evidence/session_01_s1_frames.txt` (phash 16 ×2, run14 set); `session_01_ci_final_summary.txt`; commits `8c58af7`, `956d586` | session-0 MCP claim re-run: alias+probe exit 0 PASS | PASSED 15/15 (1 red fixed same session) |

## 6. Wave register (pre-register BEFORE the wave)

| Wave | Done-criteria (pre-registered) | Status | Evidence/Report |
|------|-------------------------------|--------|-----------------|
| W0 | MCP inventory doc; Briefs A–E dispatched; `lakskongen-feature` + `lakskongen-screenshot-evidence` authored+validated; baseline frozen | **complete (session 0)** | session 0 report; `docs/research/MCP_CAPABILITIES.md`; commits bc46c90, 8e8a272, 673b6c2 |
| W1 | Decision memos for R1–R4; skills-gap list; plan wave order updated if findings demand | **complete (session 0)** | `docs/research/W1_DECISIONS.md` (R1–R5); `SKILLS_AUDIT.md` §3 author-order; plan companion-doc note |
| W2 | Living fjord frames distinct per forced state (lice/red, bloom, storm, night); time-series replaces gauge cluster; a11y audit passes | **in progress (S1 done s1 — phash 16; S2 done s2 — Trender block + price spark, analytics test 2/2; S3 pending)** | `session_01_s1_frames.txt`, `session_02_s2_frames.txt` |
| W3 | Chosen datasets embedded offline with provenance + license notes; player-path test shows band movement | not started | |
| W4 | 3-OS exports as CI artifacts; save fuzz 10k clean; version stamping; depot script written | not started | |
| W5 | Demo slice (`forste_sesong` + `po3_red_sone`) polished; first-15-min script; demo build passes W4 gates | not started | |
| W6 | Ranked visual items shipped (perf + screenshot gates each); audio pass; store art with licenses | not started | |
| W7 | D2–D7 evidence assembled; release candidate tagged | not started | |

## 7. Decision log (autonomous decisions)

| Date | Decision | Rationale | Reversible? |
|------|----------|-----------|-------------|
| 2026-10-12 | Baseline commit `bc46c90` = run-prep tree (local only, no push) | RUN_LOG §4 session-0 protocol | Yes (pre-push) |
| 2026-10-12 | Restarted the Godot editor (fresh token + registry); added `tools/mcp_bridge_alias.py` bridging the npm-bridge cwd key (repo root) to the editor's registry key (`.../godot`) | Registry + token were clobbered by headless CI instances; bridge key mismatch is structural. Full root cause: `docs/research/MCP_CAPABILITIES.md` §3 | Yes |
| 2026-10-12 | Deferred the durable fix (env-wrap `GODOT_MCP_PROJECT_PATH` in `opencode.json`) to the next natural harness restart | Mid-session opencode service restart risks the live run; alias tool covers the gap | Yes |
| 2026-10-12 | Killed two stale Godot processes (stuck headless `terrain_stats` from Sun; Friday editor) during the MCP repair | Both held ports/registry state; editor relaunched for a fresh MCP session | No (processes only) |
| 2026-10-12 | Parallel research runs via harness `subagent` (Briefs A–E), not claude-devfleet | No devfleet MCP tools registered in this environment; run plan §3.2 specifies subagents | Yes |
| 2026-10-12 | After every `./tools/ci.sh` run, re-run `python3 tools/mcp_bridge_alias.py` before using godot MCP tools (token now survives CI; `entries/` churn remains) | CI's editor-boot steps are sandboxed via `GODOT_MCP_TOKEN_PATH` in `tools/ci.sh`; headless editors still register/deregister the shared entry file | Yes |
| 2026-10-12 | Harness `_shoot(force_hide_ui)` made opt-in (fjord shots only) | Session-1 regression: the unconditional force-hide turned UI captures (03–07g) into fjord frames; UI shots now pass false, fjord shots true | Yes |
| 2026-10-12 | S2 scope: finance panel market-price spark + "Trender (90 d)" colour-coded series (Kontanter/Lus/FCR) replaces the last-10-days text wall; revenue/expense per-day rows dropped (no snapshot series exists; P&L statement + story bars retain the detail) | Plan §S2 "replace gauge walls with colour-coded time series"; free-first, no new sim state (DailySnapshots only) | Yes |
| 2026-10-12 | S1 collar tint: instance-uniform approach replaced with per-pen duplicated ShaderMaterial + status-color shader (ring takes the sim tint directly) | Instance uniforms were set at the node but never rendered (node-probe value vs pixel-sampled identical ring hue); per-pen material is the boring verified path | Yes |
| 2026-10-12 | QA forcing is bridge-level and player-unreachable: `StartQaScenario(seed)`, `SetQaPenLice`, `SpawnQaBloom`, `ClearQaBlooms`; harness pauses the sim around forced captures | Screenshot gates need deterministic states; po3_red_sone's semi-closed pens suppress natural lice growth below the tint threshold; paused sim prevents decay between set and capture | Yes |
| 2026-10-12 | W1 decisions are normative for wave order: S1→S2→S3 (W2), S4 = CMEMS/MET/Fiskeridir/BarentsWatch/ERA5 snapshots (W3), engine pinned 4.7.2, FFT ocean parked, Next Fest target 2027-02-22 | `docs/research/W1_DECISIONS.md` (§6 plan adjustments); owner gates added for Next Fest registration + price band | Yes |

## 8. Blocked / dead ends / disputes

| Item | Why | Unblock condition |
|------|-----|-------------------|
| Native `tools.godot.*` for the session | Bridge process dropped from this session's tool list during session-0 bridge recycling; probe/token verified healthy the whole time | Returns automatically at next session start; then re-run the alias repair |
| ~~S1 frame framing outage (runs 8–13)~~ **resolved** | Root cause: a GDScript `:=` type-inference error in `FishPenManager._set_ring_color` introduced mid-session broke script compilation → no pens in scene → shard frames. Caught by CI scene-load step; fixed; run14 frames correct. | — (closed; see session 1 report + `docs/run/evidence/session_01_s1_frames.txt`) |

## 9. Human-gate queue

| Gate | Status | Needs from owner |
|------|--------|------------------|
| Steam partner account + depot credentials | pending | account, fee, KYC, upload creds |
| Playtest protocol (5 × 2 h) | pending | schedule + testers |
| Strategy-sweep failure criterion | pending | pre-registered criterion |
| Paid-service sign-offs | none requested | — |
| Capsule / trailer / price / Next Fest submission | pending | final art + build |
| Legal: EULA, privacy, AI-content disclosure, ratings | pending | review |
| Push to remote | not started | standing approval or per-push review |

## 10. Session reports (newest at top; template at the end)

### Session 2: W2/S2 — time-series UI (gate PASSED)
- **Shipped:** finance panel market-price sparkline (30 d `market_price_nok`, trend-coloured) + **"📈 Trender (90 d)"** colour-coded series block (Kontanter / Lus per fisk / FCR from `State.DailySnapshots`, no new sim state) replacing the last-10-days text wall; harness `06b_finance_trends` scrolled capture; `_shoot(force_hide_ui)` made opt-in (fixes session-1 regression where UI shots were force-hidden into fjord frames); new `AnalyticsSeriesTests` (2 tests)
- **Evidence:** run18 `tools/capture_screenshots.sh` → 06_finance before↔after md5 `a878c941…`→`88fa8ac9…` **phash 18**; `06b_finance_trends` md5 `53eb0715…` (Trender card, agent-viewed: blue stable Kontanter, red rising-bad Lus/FCR); `dotnet test … --filter AnalyticsSeriesTests` → **2 passed** (newest series value == live `State.Balance` 6-dp; series extends with days); CI tail `session_02_ci_final_summary.txt`; detail `docs/run/evidence/session_02_s2_frames.txt`
- **Audit:** session-1 phash claim re-verified at bootstrap — md5s match the evidence file byte-for-byte; phash 16/16 recomputed → PASS
- **CI:** PASSED 15/15 (bootstrap + final; full suite includes the new tests)
- **Next:** session 3 claims **W2/S3 accessibility floor** (44×44 targets, CVD-safe palette audit, contrast pass, ui_scale regression) + W2 close items (baseline promotion, repeatability control)
- **Blocked:** none
- **Gates:** none hit

---

### Session 1: W2/S1 — living fjord readout (gate PASSED)
- **Shipped:** QA forcing bridge trio (`StartQaScenario` seeded boot, `SetQaPenLice`, `SpawnQaBloom`/`ClearQaBlooms`); collar-tint rendering fix (per-pen duplicated material — instance uniforms never rendered; shader uses the sim tint as the ring's status color); harness hardening (bounded frame wait, in-`_shoot` UI force-hide, sim-pause around forced captures, forced shots before the underwater shot); S1 plan marked done
- **Evidence:** `tools/capture_screenshots.sh` → `Captured 25 screenshot(s)`; lice pair `08b` vs `12` md5 `26110c9b…` vs `b456c0db…` **phash 16 (major)**; bloom pair `13` vs `13b` md5 `00c376d2…` vs `e904a449…` **phash 16 (major)**; forcing log lines (`lice force: 0.600 via QA set`, `bloom clear … 0.00`, `bloom force … 0.80 (bloom_spawned)`); all in `docs/run/evidence/session_01_s1_frames.txt`; CI raw tail `session_01_ci_final_summary.txt`
- **Audit:** session-0 MCP-repair claim re-verified at bootstrap (`python3 tools/mcp_bridge_alias.py` → exit 0 `auth probe authed`)
- **CI:** PASSED 15/15 (final). One red mid-session at scene validation — a GDScript `:=` inference error I introduced in `FishPenManager` (also the cause of a run8–13 pen-framing outage); caught by CI, fixed, re-verified with `bash tools/validate_scenes.sh` → PASSED. Not a circuit breaker (fixed same session)
- **Next:** session 2 claims W2/S2 time-series UI (wave register); S1 baseline promotion + repeatability control at W2 close
- **Blocked:** `tools.godot.*` absent for this session's tool list (bridge recycled in session 0; token/probe healthy; returns next session start — then re-run alias repair). No other blockers
- **Gates:** none hit

---

### Session 0: W0+W1 — ignition, MCP repair, research landed, decisions
- **Shipped:** baseline `bc46c90` (run-prep tree); `8e8a272` (MCP inventory + bridge-repair tool + 2 skills); `673b6c2` (five research docs, W1 decisions, S1 cold-start plan, CI token sandboxes, alias tool v2, AGENTS/run-plan pointers)
- **Evidence:** `./tools/ci.sh` → CI PASSED 15/15 (raw tails: `docs/run/evidence/session_00_ci_final_summary.txt`, `…_postfix_summary.txt`, `…_final2_summary.txt`); `dotnet test LakskongenSim.sln` → 684 passed / 1 skipped / 685 (`session_00_dotnet_test.txt`); MCP: `python3 tools/mcp_bridge_alias.py` → `OK: alias written …` + `OK: auth probe authed` exit 0 after a full CI (no editor restart); native tool calls raw results in `docs/research/MCP_CAPABILITIES.md` §2/§5
- **Audit:** first session — no prior claim to audit; instead audited the inherited state (CI green, 684/1 matches RUN_LOG baseline line)
- **CI:** PASSED 15/15 ×3 (start; post-export-sandbox; post-both-sandboxes)
- **Next:** session 1 claims **W2/S1 living fjord readout** — cold-start plan `docs/run/plans/S1_LIVING_FJORD.md`; gate G-SHOT (forced-state frames: lice/red, bloom, storm, night; md5 + phash ≥9/16) + G-TEST
- **Blocked:** none run-blocking. Note: `tools.godot.*` vanished from this session's tool list after bridge-process recycling (token/probe healthy; expected back next session start — Python WS fallback documented in MCP_CAPABILITIES §3)
- **Gates:** none hit. Owner queue additions from W1: Next Fest registration (deadline 2027-01-10), launch price band ($19.99–24.99), any paid MCP/API sign-off

---

### Template (copy per session)

```markdown
### Session <#>: <wave> — <claim>
- **Shipped:** <artifacts + file paths>
- **Evidence:** <command + raw result; commit SHA>
- **Audit:** <prior claim verified, result>
- **CI:** <./tools/ci.sh raw result>
- **Next:** <one line>
- **Blocked:** <list or none>
- **Gates:** <human gates hit or none>
```
