# Skills Audit — R5 / Brief E

*Date: 2026-10-12 · Sources: every repo `SKILL.md` (read in full), vendored Godot-MCP companion skills, the given ECC catalog plus extras verified on disk, `AUTONOMOUS_TOP_TIER_RUN.md` (§6/§9/§10), `RUN_LOG.md` (§1–§3).*

> State note: mid-write, the concurrent W0 session landed two Section 9 gap skills (`lakskongen-feature`, `lakskongen-screenshot-evidence`); this audit reflects that tree.

## 1. Executive summary

1. Audited 51 skills across 48 rows: 9 repo-pack, 37 given ECC, 5 extra ECC entries found on disk.
2. Verdict spread: **19 use / 9 extend / 23 ignore** — most ignores are deliberate stack mismatches (web/backend/mobile).
3. The repo pack plus `agentic-engineering`, `strategic-compact`, `terminal-ops` carry the operating model; research adds `deep-research`, `market-research`, `council`.
4. Section 9 gap list: **2 of 7 landed**; 5 remain, author-order: bridge-contract + save-compat (pre-W4), data-provenance (pre-W3), release (pre-W4 exit), research-brief (pre-re-dispatch).
5. Gates G-CI/G-TEST/G-SHOT/G-PERF/G-SAVE/G-LIC/G-COST (plan §10) are the acceptance language; RUN_LOG §1 defines raw proof.
6. Top risk class: generic skills contradicting repo law (coverage-first TDD, alternate verification frameworks, remote-push habits) — fenced in §4.
7. `fal-ai-media` and `video-editing` only behind G-COST sign-off and Steam AI-disclosure (S8/S6); default free-first.
8. All pre-existing repo skills' CLI entry points exist on disk — the gap is process coverage, not broken tooling.
9. Housekeeping: two output dirs masquerade as skills; companion skills duplicated across two paths (§2c).
10. Adopt §2 verdicts as R5 policy and §3 validation plans as the W0/W1 skill gate; re-audit at W1 close.

## 2. Inventory and verdicts

Verdict key: **use** = load as-is · **extend** = load but adapt to repo rules · **ignore** = do not load.

### 2a. Repo pack (project-local + vendored)

| Skill | Source | Verdict | Used by wave | Notes |
|---|---|---|---|---|
| sim-balance-probe | repo | use | W1 baseline, S10, W7 sweep (D1) | Multi-seed PlayerScriptProbe wrapper; p95 bands are tripwires — never tune-to-band (plan §11). |
| visual-qa | repo | use | W2 (S1–S3), W5/W6 store+demo shots, S7 | phash diff/gallery/baseline; promotions need a logged reason (RUN_LOG §1). |
| lakskongen-feature | repo (landed mid-write) | use | every build wave (W2–W7) | AGENTS.md checklist + evidence record; real-task validation still owed (plan §9). |
| lakskongen-screenshot-evidence | repo (landed mid-write) | use | S1/S2/S3/S7 gates, store art (W5/W6) | Wraps visual-qa; forced-state table + md5/phash numbers as G-SHOT evidence. |
| design-token-sync | repo | use | W2 (S3 contrast/ui_scale), W6 (S7) | Drift-check CI-gateable; Figma webhook dormant until the owner uses Figma. |
| godot-mcp-ops | repo | use | W0.1, W4 (S5 export-verify), W7 | JSON CLI with headless fallback; `export-verify` supports the S5 artifact gate. |
| blender-asset-pipeline | repo | use (conditional) | W6 (S7) | Only for new 3D assets; needs Blender MCP :9876 + manifest hashes in CI. |
| godot-mcp-toolkit | repo (vendored ×2) | use | W0.1 + MCP scene/script work (W2, W6) | Operator playbook for the connected godot MCP; matches the harness's headless limits. |
| mcp-extension-creator | repo (vendored ×2) | extend (defer) | R4/W0 if a tool gap is proven | Custom MCP tool authoring (e.g. screenshot captioner); unused unless W0.1 finds a gap. |

### 2b. ECC catalog — given list

| Skill | Source | Verdict | Used by wave | Notes |
|---|---|---|---|---|
| agentic-engineering | ECC | use | W0–W7 | Run operating model; §9 keeper (eval-first, cost-aware). |
| strategic-compact | ECC | use | wave boundaries W2/W4/W6 | Plan §2.6: compact at boundaries, never mid-debug. |
| terminal-ops | ECC | use | every wave | Evidence-first execution mirrors RUN_LOG §1. |
| github-ops | ECC | extend | W4 versioning, W7 RC tag | Local repo ops only; remote push = human gate (RUN_LOG §3). |
| deep-research | ECC | use | R1–R5 | Method only — §6 brief format, Firecrawl-first, §7 cost table are SSOT. |
| e2e-testing | ECC | ignore | — | Playwright/web; repo equivalents: `capture_screenshots.sh` + `test_bridge_contract.py`. |
| verification-loop | ECC | extend | W7 | Borrow release-verification checks; RUN_LOG §1 defines proof. |
| security-review | ECC | use (narrow) | W4/W7 | steamcmd secrets, telemetry/privacy; no new dependency without G-COST. |
| tdd-workflow | ECC | use | S1–S4, S10 | Test-first per slice; player-path test outranks coverage %. |
| csharp-testing | ECC | use | S1–S10 (.NET tests) | xUnit patterns; repo conventions + determinism gates override. |
| python-patterns | ECC | use | R5 tooling, probe scripts | Repo skills ship Python CLIs; keep stdlib-first. |
| council | ECC | use | W1 decision memos | Structured disagreement for offline-vs-live and cost calls. |
| market-research | ECC | use | R3 | Pricing/benchmarks via fixed Brief C questions; cite sources. |
| content-engine | ECC | extend | W6 store page/announcements | Only after build+art exist; §14 honesty rule. |
| brand-voice | ECC | extend | W6 store copy | Voice from VISION.md + STEAM_STORE_COPY.md; AI disclosure filed. |
| article-writing | ECC | ignore | — | Optional devlog; store copy covered by content-engine/brand-voice. |
| seo | ECC | ignore | — | Steam discovery ≠ web SEO; no site in scope. |
| fal-ai-media | ECC | use (cost-gated) | W6 store/key art | G-COST sign-off first; Steam AI-disclosure updated (S8/S6). |
| video-editing | ECC | extend | W6/D8 trailer | Owner approves final cut; disclosure rules apply. |
| remotion-video-creation | ECC | ignore (revisit) | — | New React/Node stack; only if trailer needs motion graphics. |
| frontend-slides | ECC | ignore | — | No decks in this run. |
| api-design | ECC | ignore | — | Bridge is in-process C#↔GDScript, not REST. |
| deployment-patterns | ECC | ignore | — | No server; S5 = export presets + CI artifacts. |
| docker-patterns | ECC | ignore | — | No containers. |
| postgres-patterns | ECC | ignore | — | Saves are JSON; no database. |
| rust/golang/swiftui/kotlin-patterns | ECC | ignore | — | Wrong stacks (4 skills, combined). |
| investor-materials | ECC | ignore | — | No fundraising in this run. |
| blueprint | ECC | ignore | — | Plan + RUN_LOG encode steps/gates; re-planning creates drift. |
| eval-harness | ECC | ignore | — | Project evals are sim-balance-probe + PlayerScriptProbe. |
| continuous-learning | ECC | ignore | — | Memory SSOT is AGENTS.md/RUN_LOG; learned rules risk determinism drift. |
| agent-harness-construction | ECC | ignore | — | Adjacent to R5 authoring; repo skills are procedural docs. |
| claude-devfleet | ECC | ignore | — | Plan §3.2 subagent policy governs. |
| messages-ops | ECC | ignore | — | No SMS/DM surface; playtest coordination is human. |
| email-ops | ECC | ignore | — | Steam/legal comms are human gates. |

### 2c. ECC — additional entries found on disk (not in the given catalog)

| Skill | Source | Verdict | Used by wave | Notes |
|---|---|---|---|---|
| research-ops | ECC (extra) | extend | R1–R5 | Evidence-first research workflow; §6 preamble still mandatory. |
| workspace-surface-audit | ECC (extra) | extend | W0.1 | Repo/MCP surface audit overlaps MCP verification. |
| skill-stocktake | ECC (extra) | extend | R5/W1 | Quick-scan QA for newly authored skills before first real-task validation. |
| exa-search | ECC (extra) | ignore (defer) | — | Secondary to firecrawl; only if keyless limits bite (R4). |
| token-budget-advisor | ECC (extra) | ignore | — | Session-level; harness manages budgets; strategic-compact covers context. |

~100 further ECC entries exist on disk (web/backend/mobile/finance/healthcare/legal) — blanket ignore; none reference Godot, game shipping, or simulation.

**Housekeeping findings.** (1) `.claude/skills/artifacts/` and `.claude/skills/godot/assets/` are output dirs, not skills — relocate before R5 closes. (2) Companion skills are duplicated at `godot_mcp_toolkit/CompanionSkills/` and `godot/addons/godot_mcp_toolkit/CompanionSkills/` — one logical skill, two copies. (3) All pre-existing repo skills' CLI scripts exist (verified).

## 3. Gap analysis — Section 9 candidate skills

Status: **2 landed** (real-task validation still owed per plan §9 — load via skill tool, run once in its first wave), **5 missing**. Author-order: bridge-contract + save-compat pre-W4; data-provenance pre-W3; release pre-W4 exit; research-brief pre-re-dispatch.

| Skill | Status | Purpose (trigger) | Must point at | Validation plan (prove it works) |
|---|---|---|---|---|
| lakskongen-feature | LANDED | Add/change a subsystem the repo way (engine change, state field, bridge trio, calibration constant). | AGENTS.md checklist/key systems; SimRngStreams eager-init; CalibrationRegistry/BalancingConfig; GameState/AdoptSavedState; ExactSaveHashTests, TwinRun, PlayerScriptProbe; tools/ci.sh; RUN_LOG §1. | 1) Load on the next W2 slice; 2) follow end-to-end, `./tools/ci.sh` raw tail → `docs/run/evidence/`; 3) verify the gates catch a deliberately omitted step; log in RUN_LOG. |
| lakskongen-screenshot-evidence | LANDED | Capture→label→distinctness/diff for visual claims (UI/3D change, G-SHOT gates, store shots). | tools/capture_screenshots.sh; ScreenshotHarness.tscn; visual-qa config/baseline; RUN_LOG §1 screenshot rule; SCREENSHOTS.md. | 1) Load on S1's first forced-state capture; 2) md5/phash distinctness table + diff vs baseline; 3) a zero-difference claim must fail the gate; record numbers. |
| lakskongen-bridge-contract | MISSING | C#↔GDScript trio + emission + signal-connect rules (any GameBridge.*.cs / SimProxies.gd / SimBridge.gd edit). | AGENTS.md patterns + interop notes; GameBridge.* partials; tests/test_bridge_contract.py; direct-connect rule (no `has_signal`). | 1) Add one trivial bridge method by the skill; 2) `pytest tests/test_bridge_contract.py` green; 3) headless boot clean; wrong-file placement must fail the test. |
| lakskongen-save-compat | MISSING | Save-format rules + hash workflow + fuzz ladder (serializable field, RNG stream, GameState collection change). | SaveJson.cs; GameState/AdoptSavedState/RestoreFromLoad; ExactSaveHashTests; G-SAVE; S5 fuzz harness (10k). | 1) Deliberate test field addition: ExactSaveHash + twin-run green; 2) 1k-cycle fuzz smoke; 3) reordered restore must fail loudly (negative test) before the 10k run. |
| lakskongen-data-provenance | MISSING | Real datasets + generated media with license/provenance metadata (S4 snapshots; AI assets in S8). | Plan §4 S4 + §7 (game-safe license test, cost table); R1 DATA_SOURCES; G-LIC; RealDataLayerTests; godot/data/. | 1) One R1 dataset ships with snapshot + {source, license, retrieval date}; 2) RealDataLayerTests + band-movement demo green; 3) license note answers "paid-game redistribution?" pre-commit. |
| lakskongen-release | MISSING | Export/preset/version-stamp/depot checklist from CI artifacts (export, RC tag, demo build). | Plan D2/D8, S5/S6; RELEASE_CHECKLIST.md; export_presets.cfg; godot-mcp-ops export-verify; RUN_LOG §9 (Steam creds). | 1) Dry-run on the first W4 CI artifact; 2) artifact launches (macOS in CI; Win/Linux smoke); 3) missing/corrupt artifact must block "done". |
| lakskongen-research-brief | MISSING | §6 brief template + Firecrawl workflow + §7 cost table (dispatching R1–R5). | Plan §6 preamble + §7; docs/research/ paths; RUN_LOG §1; firecrawl tool names. | 1) Re-run one small brief with the template; 2) output has per-claim sources + cost table + exec summary; 3) citation spot-check catches an invented URL. |

## 4. Risks — skills that could mislead, and how they are fenced

| Skill(s) | Mislead vector | Fence |
|---|---|---|
| tdd-workflow | Coverage-% targets / generic unit tests treated as "done". | AGENTS.md item 5 (player-path test); RUN_LOG §2 (tests only strengthen). |
| verification-loop, eval-harness | Their own "verification/comprehensive" results mistaken for run evidence. | RUN_LOG §1 is the only evidence contract; project evals = sim-balance-probe + PlayerScriptProbe. |
| e2e-testing (+ web-stack skills) | Playwright/web prescriptions absent from a Godot project. | Ignore list; Godot equivalents: `capture_screenshots.sh` + `test_bridge_contract.py`. |
| deep-research, market-research, research-ops | Open-ended scope or citations bypassing the §6 contract. | §6 brief format + Firecrawl-first + 2025–26 recency + §7 cost table mandatory; citation spot-checks. |
| fal-ai-media, video-editing, remotion-video-creation | Spend and AI-disclosure exposure. | G-COST sign-off; Steam disclosure (S8/S6); free-first default. |
| github-ops | Remote push/PR habits. | RUN_LOG §3 STOP — remote push is a human gate. |
| claude-devfleet | Parallel writers racing on overlapping code. | Plan §3.2: serial builds behind CI, ≤2 overlapping coders. |
| continuous-learning | Cached lessons contradicting determinism bans. | Nothing enters AGENTS.md without DeterminismBanTests green; RUN_LOG is memory SSOT. |
| visual-qa, lakskongen-screenshot-evidence | Silent re-baselining blesses regressions. | In-skill anti-patterns; RUN_LOG §1 auto-fail; `baseline_update` needs a logged reason. |
| sim-balance-probe | p95 bands become de-facto clamps (tuning to band). | Plan §11 "no clamps"; bands are tripwires; disputes to RUN_LOG §8 (human gate). |

**Method & limitations.** Repo skills read in full; ECC verdicts are per-run scope judgments (given-catalog descriptions; on-disk verification for §2c). Re-audit at W1 close — verdicts may shift with decision memos.
