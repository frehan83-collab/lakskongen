# Autonomous Top-Tier Run — Mission Plan

*Created: 2026-10-12 · Status: READY TO EXECUTE · Scale: multi-wave, ~30–40 agent sessions*
*Companion docs: `docs/NEXT_LEVEL_PLAN.md` (phase roadmap), `docs/RESEARCH_TOP_TIER_BENCHMARK.md` (benchmark evidence), `AGENTS.md` (engineering SSOT)*

---

## 0. TL;DR

The sim is regulation-faithful and CI-green (684 tests, 15/15 CI). The gap to a
top-tier commercial winner is **dramatization, data-grounded authenticity, ship
mechanics, and presentation** — not more simulation accuracy. This run:

1. **Dispatches 5 parallel research subagents** (Firecrawl) — satellite/ocean data
   access & costs, tech stack & graphics, commercial release, MCP tooling, skills audit.
2. **Builds in waves** behind evidence gates: living fjord readout → time-series UI →
   accessibility → ship pipeline → demo slice → visual/audio upgrade → hardening.
3. **Converts the repo's own culture into leverage**: every item ships with a
   player-path test, a screenshot diff, and CI green — plus human gates marked
   explicitly where only the owner can decide.

Nothing in this plan requires paid services; every paid option is a decision item
with a cost table for the owner.

---

## 1. Definition of Done — the commercial bar

"Professional level, top tier, commercial winner" is pinned to measurable release
criteria. All must be true before a public demo/Next Fest submission:

| # | Criterion | Evidence |
|---|-----------|----------|
| D1 | All Phase 4 exit gates closed (blind playtest ≥3/5 "understand money"; no-dominant-strategy sweep with pre-registered failure criterion) | Playtest notes + sweep report |
| D2 | Steam demo approved & Next Fest submitted (windows met) | Steamworks screenshots |
| D3 | 60 FPS @ 1440p on M1 Pro; ≥30 FPS verified on Steam Deck | Perf gate + Deck profiler log |
| D4 | Save fuzzing: 10 000 randomized save/load/continue cycles, 0 divergences | Fuzz harness report |
| D5 | 2-hour soak: 0 crashes, memory flat (±5%) | Soak log |
| D6 | Screenshot suite: ≥24 labeled frames, state-driven distinctness, CVD-safe labels | `capture_screenshots.sh` diff report |
| D7 | Audio pass complete (music, ambient, SFX, mix buses) — no placeholder silence | Audio checklist + audition notes |
| D8 | Store page live with capsules/trailer/screenshots; EN + NB shipped; AI-content disclosure filed | Steam page URL |
| D9 | Review trajectory guardrail: post-launch "Mostly/Very Positive" (>80% positive over first 50 reviews) — *target metric, not guarantee* | Launch telemetry |
| D10 | Wishlist momentum: ≥10 000 wishlists by Next Fest, ≥30 000 by launch | Steamworks analytics |

---

## 2. Operating rules (inherited + new)

Inherited and enforced (from `AGENTS.md`): determinism-first, calibration SSOT,
`ITickSystem` + `ISnapshotable`, player-path tests, no clamps to hit bands,
screenshot diffs for visual claims, CI 15/15 stays green.

New for this run:

1. **Research-before-build.** No visual or data decision without a cited research
   memo (≤5 pages) in `docs/research/`.
2. **Parallel research, serial builds.** Subagents may run concurrently; code
   changes serialize behind CI.
3. **Cost guardrail.** No paid API/subscription may enter the build without a
   signed-off cost table (Section 7). Default = free tier.
4. **Human gates are explicit** (Section 12) — never silently bypassed, never
   claimed as "done" by the agent.
5. **Every wave ends with a wave report** appended to `docs/RUN_LOG.md`
   (what shipped, evidence links, open risks, next wave entry criteria).
6. **Context hygiene.** Compact at wave boundaries, not mid-debug
   (use the `strategic-compact` skill).

---

## 3. Run topology

### 3.1 Waves

| Wave | Name | Focus | Est. sessions | Parallel research |
|------|------|-------|---------------|-------------------|
| W0 | **Ignition** | MCP verification, research dispatch, quick wins | 1 | 5 subagents dispatched |
| W1 | **Decisions** | Consolidate research → decision memos; author first skills | 2 | — |
| W2 | **Feel layer II** | Living fjord readout + time-series UI + accessibility | 4–6 | from W1 findings |
| W3 | **Realism × data** | Data layer v2 (snapshots + optional live) | 3–4 | — |
| W4 | **Ship mechanics** | Exports ×3 OS, CI export job, save fuzzing, versioning | 3–4 | — |
| W5 | **Demo slice** | `forste_sesong` + `po3_red_sone` polished; first-15-min experience | 4–5 | — |
| W6 | **Presentation** | Visual upgrade track + audio identity + store art | 5–6 | may re-dispatch |
| W7 | **Hardening** | Perf/soak, save fuzz, career-divergence probe, release candidate | 4–5 | — |

Each wave's exit = CI green + wave report + its evidence gates (Section 10).
Waves are sequential; within a wave, independent items run in parallel sessions
where retries of the performance gate allow it.

### 3.2 Subagent policy

- **Research subagents** (`general`): background, one per brief, each writes its
  own `docs/research/*.md`. They must not touch `src/` or `godot/`.
- **Code subagents**: only for mechanical fan-out (e.g., "add missing XML docs",
  "rename across files") with a strict file list; review the diff before CI.
- **Parallelism rule:** at most 2 concurrently writing to overlapping areas;
  research agents are unbounded (they only write new docs).
- **Prompt contract for every research agent** (Section 6): Firecrawl-first,
  cite everything, cost table mandatory, 2025–2026 recency, confidence labels,
  output path fixed.

### 3.3 Session recipe (per wave)

1. `./tools/ci.sh` green from the previous wave.
2. Pick the highest-value item not blocked; read its brief from this plan.
3. Implement behind its evidence gate (test/screenshot first where practical).
4. Run CI; capture screenshots when UI/visual touched.
5. Append the wave report; compact context; hand off.

---

## 4. Workstream catalog

### W0.1 — MCP verification (immediate)
Verify every listed MCP server actually connects in this environment and map each
to a task it owns. **`godot` MCP (36 tools) is the biggest new lever** — scene
authoring/editing/screenshotting without blind GDScript edits. Confirm: editor
running + addon connected; enumerate usable tool names; write
`docs/research/MCP_CAPABILITIES.md`.

### R1 — Satellite & real-world data (research; Brief A)
Access, APIs, resolution, refresh rate, **licensing for paid-game redistribution**,
and **costs** for: Copernicus Marine (SST, chlorophyll, waves, currents), Copernicus
Data Space/Sentinel Hub, MET Norway, IMR/HI (algeinfo), Fiskeridirektoratet (lice,
biomass), BarentsWatch Fishhealth, NASA OceanColor, EMODnet, ERA5. Output:
`docs/research/DATA_SOURCES.md` + recommended integration (offline snapshots vs
optional live) + 2 proposals with effort estimates.

### R2 — Tech stack & graphics (research; Brief B)
Godot 4.7-current state (renderer features, Metal/M-series, Steam Deck/Proton),
water/terrain/atmosphere/GI/particles techniques ranked by impact/effort, competitor
tech teardown (Farming Simulator 25, Fishing: Barents Sea), asset pipeline
(Blender→GLB→LOD→KTX), and an explicit "stay on Godot vs migrate" verdict.
Output: `docs/research/TECH_GRAPHICS.md` + ranked upgrade list with perf budgets.

### R3 — Commercial release (research; Brief C)
Steam pipeline 2026 (fees, depots, steamcmd automation, capsule/screenshot specs),
Next Fest eligibility & calendar, comparable pricing & regional pricing, demo best
practices, AI-content disclosure rules, age ratings, localization ROI (EN/NB+),
Steam Deck Verified program. Output: `docs/research/COMMERCIAL_RELEASE.md` +
release timeline anchored to real 2026/2027 dates.

### R4 — MCP & tooling evaluation (research; Brief D + Section 8)
Evaluate add-on MCP servers against this project; production-test the top 2.
Output: `docs/research/MCP_EVALUATION.md` + install instructions in
`opencode.json`/global config + verification log.

### R5 — Skills audit & authoring (Brief E + Section 9)
Audit the ~130 available skills against project needs; author the project-local
skill pack under `.claude/skills/`; validate by loading each via the skill tool.
Output: `.claude/skills/*/SKILL.md` + `docs/research/SKILLS_AUDIT.md`.

### S1 — Living fjord readout (Phase 5.5 #3)
Lice → collar tint, biomass → visual density, weather → waves/foam, bloom → tint,
wellboat/feed-barge events → 3D activity. `Sim3DSync.gd` + `FishPenManager.gd`
already exist (post-fix). Gate: harness frames show distinct, state-driven images
per forced state (red zone, bloom, storm, night).

### S2 — Time-series UI over gauge walls (Phase 5.5 #5)
Replace finance/market gauge clusters with color-coded sparklines/time series
(EA evidence). Reuse `State.DailySnapshots` (AnalyticsTracker) — no new sim state.
Gate: screenshot diff + a data test that series follow sim values.

### S3 — Accessibility floor (Phase 5.5 #4)
44×44 px interactive targets, CVD-safe audit of all UI palettes, contrast pass,
ui_scale regression; keep emoji+text dual encoding. Gate: accessibility audit
report + before/after screenshots.

### S4 — Data layer v2 (from R1 findings)
Embed selected free datasets as offline snapshots (JSON) for calibration/realism;
optional live fetch behind a config flag with graceful offline fallback. Keep the
existing `RealDataLayerTests` contract; add provenance metadata (source, license,
retrieval date). Gate: player-path test showing realism bands shift with snapshot
data; license compliance note per dataset.

### S5 — Ship pipeline
Windows/Linux/macOS export presets + CI export job + artifact upload; version
stamping; save fuzzing harness (10k runs); crash/log capture; Steam depot
automation script (human runs the upload). Gate: downloadable CI artifacts launch
on each OS (macOS verified in CI; Win/Linux at least smoke-tested on CI runners).

### S6 — Demo slice & Next Fest prep
Polish `forste_sesong` (tutorial) + `po3_red_sone` (red-zone drama); first-15-min
experience script; remove rough edges found in screenshots; demo build for
playtests. Gate: D1 playtest protocol ready + demo build passes S5 gates.

### S7 — Visual upgrade track (from R2 findings)
Execute the ranked list from R2: water (waves/foam/shore blend/underwater),
terrain material blending, sky/atmosphere, GI settings per platform, particles.
Each item: before/after screenshot diff + perf gate number.

### S8 — Audio identity
Music bed(s), ambient layers (fjord, weather), UI SFX, event stingers; mix buses
(`audio_bus_layout.tres` exists). Generated audio only with license-cleared tools
+ Steam AI disclosure updated. Gate: audio checklist + file inventory + loudness
targets (≈ −16 LUFS menu / −20 in game).

### S9 — Performance & soak hardening
Budget table per platform; profile hot paths (tick cost, render); fix regressions;
soak test; Steam Deck pass. Gate: perf report vs D3.

### S10 — Career-divergence probe (Phase 5.5 #1 gate, code-verifiable part)
A/B scripted career: red-light run vs suppressed-red run on identical seed; assert
measurable divergence (harvested kg, penalty revenue, ending balance) beyond a
pre-registered threshold. Gate: probe report. (The *empirical* playtest still
remains human.)

---

## 5. Wave dependency graph

```
W0 ──┬─ dispatch R1..R5 (parallel) ── W1 (decisions + skills)
     └─ MCP verify
W1 ──┬─ S1, S2, S3  (feel layer II; parallel-friendly)
     └─ S10 quick probe
W2 ── S4 (data v2, needs R1 decision)
W3 ── S5 (ship mechanics; independent of S4)
W4 ── S6 (demo slice; needs S5 export path + S1/S2 UX)
W5 ── S7 (visual track; needs R2 decisions), S8 (audio), store art generation
W6 ── S9 hardening + release candidate; D8 store page assembly; human gates
```

Critical path: W0 → W1 → S5 → S6 → S9 → human gates.
Research R2's visual list feeds S7; R1 feeds S4; R3 feeds S5/S6/D8; R4/R5 feed
everything (tooling + skills).

---

## 6. Research briefs (copy-paste for subagents)

> Common preamble for every brief:
> *"You are a research subagent for the Lakskongen salmon-farming sim (Godot 4.7
> Mono + C#/.NET 10, repo: this worktree). Today is 2026-10-12. Use the Firecrawl
> MCP (`tools['firecrawl'].firecrawl_search` / `firecrawl_scrape` from the execute
> tool) as your primary research tool; use direct `webfetch` only for known URLs.
> Prefer 2025–2026 sources; include publication dates. NEVER invent URLs or
> numbers. Every claim gets a source link. Write your report to
> `docs/research/<FILE>.md` and finish with a 10-line executive summary. Do not
> modify any code or config."*

### Brief A — Satellite & real-world data access and costs → `docs/research/DATA_SOURCES.md`
Questions to answer, per source:
1. What is available (SST, chlorophyll-a, waves, currents, wind, lice counts, biomass/MAB, blooms)?
2. Access: API type (REST/OPeNDAP/STAC/WMS), auth needed, resolution (km, days), archive depth.
3. **License for a paid commercial game**, including shipping derived snapshots vs live calls; attribution text required.
4. **Costs**: free tier limits, paid tiers, per-call/per-GB pricing, commercial clauses — as a table: `Source | Data | Access | License (game-safe?) | Free tier | Paid from | Notes`.
5. Recommended architecture: build-time snapshot pipeline → `godot/data/*.json` + optional live client; fallback behavior offline.
Also verify the current `BarentsWatch Fishhealth` auth state and `Fiskeridirektoratet` open APIs.

### Brief B — Tech stack & graphics → `docs/research/TECH_GRAPHICS.md`
1. Godot state as of Oct 2026: latest stable (verify 4.7.x/4.8), renderer features relevant to an open-world fjord (Forward+, SDFGI, fog, TAA, mesh LOD, occlusion, MultiMesh, CompositorEffect, Water shaders), Metal/M-series notes, Steam Deck/Proton status.
2. Technique shortlist with impact/effort: ocean surface (FFT vs Gerstner + foam + shore blend + underwater), terrain material blending, sky/atmosphere, GI strategy per quality tier, instanced vegetation/boats, decals.
3. Competitor teardown (what shipping games do): Farming Simulator 25, Fishing: Barents Sea, Fishing: North Atlantic, Dredge (art direction), plus 1–2 current top tycoons.
4. Asset pipeline: Blender→GLB conventions, LODs, texture formats (KTX/Basis), import settings; how to validate in CI.
5. Verdict: stay on Godot (migration cost analysis one paragraph) + ranked upgrade list (impact, effort, perf budget, screenshot-gate definition).

### Brief C — Commercial release → `docs/research/COMMERCIAL_RELEASE.md`
Steam fees/setup/depots + `steamcmd` automation; capsule & screenshot specs (exact
pixel sizes, 2026 current); Next Fest: eligibility, calendar windows in 2026/2027,
required demo state; pricing + regional pricing benchmark for this niche; demo
conversion benchmarks (wishlist→sales, demo→wishlist); review-score guardrails;
Steam AI-content disclosure rules (music/art/voice) and what must be declared;
age ratings (PEGI/USK) and Norwegian specifics; Steam Deck Verified checklist;
localization ROI (EN/NB now, DE/FR next?); launch-discount conventions.
Deliver a realistic timeline dating from 2026-10-12.

### Brief D — MCP evaluation → `docs/research/MCP_EVALUATION.md`
For each candidate in Section 8: what it does, install method for opencode MCP
config, license/cost, suitability, verdict (adopt/verify/defer/reject). Then
**production-test the top 2** (install, call once, record evidence) and write exact
config snippets. Candidates to evaluate at minimum: fal.ai (media + vision),
Context7 (docs), ElevenLabs-class audio, a screenshot-vision option, GitHub,
Figma, SQLite, Steamworks community servers.

### Brief E — Skills audit → `docs/research/SKILLS_AUDIT.md`
List the locally available skills (ECC pack), map each to a run workstream, mark
`use / extend / ignore`; then define the gap list that Section 9 turns into new
project skills. Include a table: `Skill | Verdict | Used by wave`.

---

## 7. Cost & licensing framework

Rule: **free-first**. Any paid thing requires the owner's sign-off on a cost table.

Cost-table format (mandatory in R1–R4 outputs):

| Service | What we'd use it for | Free tier | Paid from | Est. run cost | License risk | Decision |
|---|---|---|---|---|---|---|

Decision defaults:
- Free/official (Copernicus, MET, Fiskeridirektoratet, NASA): adopt if game-redistribution allowed with attribution → ship attribution screen/credits.
- Free with registration (BarentsWatch Fishhealth): adopt server-side only.
- Paid AIS/satellite imagery: reject unless a concrete feature needs it.
- Media generation (fal.ai, audio): allowed for store art/trailer/audio if per-use cost is budgeted and Steam disclosure is filed — human gate.

---

## 8. MCP — answers and plan

**Already connected:** `godot` (36 tools: scene open/edit/save, screenshots, game
control), `blender`, `firecrawl`, `opencode`.

**Top additions to evaluate (Brief D):**

| MCP | Why | Priority |
|-----|-----|----------|
| fal.ai (media) | Steam capsules, key art, trailer shots; possibly **vision captioning for screenshot QA** | High |
| Context7 (docs) | Current Godot/.NET/Steamworks API docs during coding | High (cheap) |
| Audio-gen MCP (ElevenLabs-class) | Ambience/VO/SFX with disclosure | Medium |
| Screenshot-vision (any working captioner) | Closes the "agent can't see images" gap for visual QA | High |
| Figma | Design iteration if UI moves to Figma (tokens exist) | Low |
| GitHub | Nice-to-have; `gh` CLI already covers it | Defer |
| SQLite/telemetry | Only if a local analytics store becomes useful | Defer |

**Steamworks:** no first-party MCP expected; plan uses `steamcmd` + scripts run by
the human (2FA/credentials) — that is a human gate, not an agent task.

---

## 9. Skills plan

**Audit (Brief E):** map all locally available skills to workstreams; keep the
existing winners — `sim-balance-probe`, `visual-qa`, `design-token-sync`,
`agentic-engineering`, `strategic-compact`, `terminal-ops`, `github-ops`.

**New project-local skills to author under `.claude/skills/`:**

| Skill id | Purpose | First used by |
|----------|---------|---------------|
| `lakskongen-feature` | Checklist to add a subsystem the repo way (determinism, calibration, ITickSystem, snapshot, bridge trio, player-path test, CI) | every build wave |
| `lakskongen-save-compat` | Save-format rules + ExactSaveHashTests workflow + fuzzing | S5 |
| `lakskongen-bridge-contract` | C#↔GDScript conventions + pytest contract rules | S1–S3 |
| `lakskongen-screenshot-evidence` | Capture → label → distinctness/diff workflow on top of `visual-qa` | S1, S7 |
| `lakskongen-data-provenance` | Ingesting real datasets with license/provenance metadata | S4 |
| `lakskongen-release` | Export/preset/depot/Steam checklist driven by CI artifacts | S5, S6 |
| `lakskongen-research-brief` | The Section-6 brief format + Firecrawl workflow + cost-table template | R1–R4 |

Each skill: `SKILL.md` with frontmatter (`name`, `description`), ≤150 lines,
pointing at repo docs instead of duplicating them. Validation: load via the skill
tool and run once on a real task in the wave that first uses it.

---

## 10. Evidence gates catalog

| Gate | Proof required | Applies to |
|------|----------------|------------|
| G-CI | `./tools/ci.sh` 15/15 | every wave |
| G-TEST | player-path test showing outcome change | S1–S4, S10 |
| G-SHOT | captured + labeled frames, distinct under forced states | S1, S2, S3, S7, store art |
| G-PERF | perf report vs D3 budget (M1 Pro + Deck) | S7, S9 |
| G-SAVE | save fuzz + ExactSaveHashTests + twin-run determinism | S4, S5, S9 |
| G-LIC | source/license/provenance note per dataset or asset | S4, S8 |
| G-COST | signed-off cost table before any paid dependency | R1–R4, S8 |

---

## 11. Risk register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Research recommends paid APIs that don't fit | Med | Low | Free-first rule; decision memo per source |
| Data license forbids in-game redistribution | Med | High | Brief A must answer "game-safe?" explicitly; fall back to synthesized distributions derived from published statistics (facts aren't copyrightable) |
| Scope creep across 7 waves | High | Med | Wave exit gates; one active build item per session; wave reports |
| Visual upgrade destabilizes perf | Med | High | Per-item perf gate; quality tiers (Low/High/Ultra) |
| Determinism regressions from new systems | Low | High | Existing gates; new systems follow `lakskongen-feature` |
| Steam AI-disclosure non-compliance | Med | High | Brief C answers rules; disclosure task in S8/S6 |
| Export/signing issues (macOS/Win) | Med | Med | S5 addresses early; human holds certs |
| Regenerated context loses state | Med | Med | Wave reports + AGENTS.md SSOT updates |

---

## 12. Human gates (only the owner can close)

1. Steam partner account: fee, KYC, tax forms, depot **upload credentials**.
2. Playtest: run the 5×2 h protocol and the strategy sweep (D1).
3. Cost sign-offs (Section 7) for any paid service.
4. Final approval of capsule art, trailer, price, and Next Fest submission.
5. Legal: EULA, privacy policy, AI disclosure verification, age ratings.

Everything else in this plan is designed to be executed autonomously by the agent
with verifiable evidence at every gate.

---

## 13. First 48 hours — dispatch order

1. **W0.1** — verify godot/blender/firecrawl MCP connectivity; write
   `docs/research/MCP_CAPABILITIES.md`.
2. **Dispatch Briefs A, B, C, D, E as 5 background subagents** (each writes its
   doc; no code changes).
3. While they run: author the two highest-value skills (`lakskongen-feature`,
   `lakskongen-screenshot-evidence`); run `./tools/ci.sh` once to baseline.
4. Land W1: consolidate research into decision memos; update this plan's wave W2/W3
   item order according to findings.
5. First build item after W1: **S1 (living fjord readout)** behind its screenshot
   gate — it unlocks store screenshots, the demo's wow moment, and exercises the
   new godot MCP tooling.

---

## 14. KPIs & reporting

- **Per wave:** CI status, tests total/Δ, items shipped vs planned, gates passed,
  open risks — appended to `docs/RUN_LOG.md`.
- **Run-level:** D1–D10 progress; research docs landed; skills authored; cost
  exposure (should stay 0 until sign-off); wishlist/perf/save-fuzz numbers.
- **Honesty rule:** anything not yet proven is listed as "open", never marked done.

---

*Plan owner: autonomous agent. Review cadence: after every wave report. This plan
supersedes ad-hoc task selection for the duration of the run; `AGENTS.md` remains
the engineering SSOT.*
