# Lakskongen — Re-review after remediation

_2026-10-14, branch `review-remediation`, code at `08ca977` (28 commits on top of `main`, not pushed). This closes step 15 of [CODE_REVIEW.md](CODE_REVIEW.md) ("Re-review after fixes"). Two inputs: a status audit of all 58 critical and high findings against the code (three read-only agents, one per finding group), and a fresh four-reviewer review of everything the branch changed (engine, bridge, UI, tests and tooling). Every finding below was checked against the code. Each fix commit lists its evidence, and most include a mutation check showing the new test fails without the fix._

## Summary

- **Baseline** (CODE_REVIEW.md, HEAD `1652043`): 3 critical, 55 high, 369 medium, 373 low.
- **The 58 critical and high findings:**
  - 53 are fixed, including all 3 criticals.
  - 3 are partial: F618 (by design), F383 (post-smolt has no player verb) and F177 (a missing local tooling addon is exempted; logged in RUN_LOG §7 for the owner).
  - 2 are open, and both wait on the owner. F1024's fix sits on a side branch, blocked by three legacy tests. F456 changes every career's storm statistics.
- **The re-review of the changes** found 43 issues: 1 critical, 3 high, 12 medium and 27 low.
  - The critical and all 3 highs are fixed, along with 11 of the 12 mediums. The open medium (F271) is dormant: no player UI reaches it.
  - Of the lows, 6 are resolved, 1 is partial, 1 is by design, 1 is a recorded dispute and 18 are open.
- **Beyond the 58:** 15 medium findings and 1 low from the original review are fixed on the branch, and 6 more are partly fixed (section 3). The other mediums were not re-audited one by one.
- **Gates** at `08ca977`:
  - `CI_REQUIRE_ALL=1 /bin/bash tools/ci.sh` passes 17/17 with no skipped step, run on stock bash 3.2.
  - .NET tests: 914 passed, 1 skipped. pytest: 54 passed. UI flow tests: 224 checks, 0 failures.
  - The shader gate compiles all 12 shaders under a real renderer.
  - All 28 screenshots are within the calibrated threshold.
- **Not verified:** none of the four GitHub workflows has ever run. Pushing is a human gate. The Linux runner's virtual display (Xvfb with Mesa) for the shader gate is untested until that first run.

## 1. The 58 critical and high findings

Verdicts come from reading the code at `44a0d89`. Rows fixed after that point cite their later commit. F457 and F480 were open at the audit and are fixed in `8f4e235`.

**Open (2)**

| ID | Severity | Finding | Where it stands |
|---|---|---|---|
| F456 | high | Storm-season multiplier is applied without renormalizing, so Severe weather becomes unreachable in storm… | The default weather chain still scales storms ×1.5 without renormalizing (only the new harsh_storms rule renormalizes). Fixing it shifts every career's storm statistics: owner decision. |
| F1024 | high | Dissolved-oxygen update is numerically unstable (gain about -9); the [0,14] clamp turns it into a daily… | Fixed on local branch `review/f1024-water-chemistry` (`8123ea1`), not merged: three legacy tests pass only with the broken oxygen signal (RUN_LOG §8 dispute). |

**Partial (3)**

| ID | Severity | Finding | Where it stands |
|---|---|---|---|
| F177 | high | Tracked `project.godot` depends on a git-ignored addon; a clean checkout fails the autoload audit | A fresh clone passes CI: the missing git-ignored MCP addon is reported as optional tooling rather than vendored or removed (decision logged in RUN_LOG §7 for the owner to confirm). |
| F383 | high | RAS / Offshore / PostSmolt facilities can never become operational: `StartConstruction` has no caller | RAS and offshore build, stock, grow and harvest; post-smolt has no bridge or UI verb, and the bridge's post-smolt stubs still charge cash without changing state. |
| F618 | high | HUD `_input` swallows `ui_accept` (Space/Enter) globally, breaking text fields and keyboard activation | Enter and typing are fixed; Space stays a global pause, also on a focused button: a recorded trade-off (RUN_LOG §7). |

**Fixed (53)**

| ID | Severity | Finding | Commit |
|---|---|---|---|
| F521 | critical | Twin-lite projection draws from the live growth RNG stream, so UI reads change the simulation | `87edd97` |
| F928 | critical | Save/Load menu calls snake_case methods Godot never exposes, so manual save and load is dead | `27da699` |
| F1069 | critical | RAS, offshore and post-smolt facilities are never saved, so building one then save/load deletes it | `87edd97` |
| F054 | high | Zone fish biomass is overwritten per pen instead of summed, and goes stale after a harvest | `26399ac` |
| F056 | high | GameScene always starts a fresh default game, discarding the scenario chosen in the main menu | `27da699` |
| F197 | high | Solution does not build: test project fails to compile (CI step 1 and `verify.yml` go red) | `d93c8ec` |
| F243 | high | Active scenario, start day and AI progress are lost on save/load, so win/loss never fires again | `87edd97` |
| F244 | high | Shipped scenario start omits the engine argument, so insurance, resource-rent flag and starting disease… | `87edd97` |
| F268 | high | `HarvestSite` applies the certification price multiplier on top of a spot price that already contains it | `b1a5a37` |
| F269 | high | `StockSmolts` bypasses calibrated `SmoltCostPerFishNOK` and charges about 54.75 NOK per smolt | `b1a5a37` |
| F292 | high | `Initialize()` keeps the old `MarketSimulation` (and its stale RNG stream) on New Game and Load | `87edd97` |
| F342 | high | Annual tax payment ignores the `Spend()` result: tax is booked as paid even when cash never leaves | `b1a5a37` |
| F382 | high | Transfer into an empty site creates a `FishPopulation` with no Growth/Mortality injected; the next tick… | `26399ac` |
| F386 | high | Cohort rebuild/merge paths silently drop `SmoltQualityTier` (and Genetics on merge), so premium cohorts… | `26399ac` |
| F409 | high | Signed contracts, completed contracts and the contract week counter are never saved | `87edd97` |
| F453 | high | `Zone.UpdateCurrents` uses `HashCode.Combine(day, Id)`, which is randomized per process, so the sim is… | `87edd97` |
| F457 | high | Escape score compares a lifetime fish count against incident thresholds, so one escape zeroes the… | `8f4e235` |
| F478 | high | Per-population disease prevalence is not snapshotted, is keyed by object reference, and is never pruned | `87edd97` |
| F480 | high | Treatment efficacy is capped by the model's base table: Laser 0.90 becomes 0.50, Thermolicer 0.95… | `8f4e235` |
| F499 | high | CrisisCapitalism is never triggered: `TriggerCrisis` has zero callers, `OnCompetitorDistressed` zero… | `1467d03` |
| F616 | high | Main menu "Last Spill" (Load) and "Innstillinger" (Settings) buttons are inert | `27da699` |
| F617 | high | Cancelling the scenario picker leaves New Game/Load/Settings/Quit permanently disabled | `27da699` |
| F620 | high | Transparent full-row Button overlay sits above the enable CheckBox and swallows its clicks | `d2810e7` |
| F641 | high | First ESC press permanently hides the NotificationCenter root and swallows the key | `d2810e7` |
| F647 | high | Pen list is rendered into a zero-height ScrollContainer (list invisible in the accepted baseline) | `f05329b` |
| F738 | high | Clicking a 3D pen does nothing in GameScene: `pen_clicked` is emitted on the wrong node and the… | `f05329b` |
| F762 | high | Alerts are level-triggered: re-emitted on every refresh, flooding NotificationCenter, repeating the lice… | `8e97c4c` |
| F835 | high | "Live state untouched" test passes an empty state and cannot see that `Project()` consumes the live… | `87edd97` |
| F884 | high | Probe's mid-run save/load is checked only by counts; a resumed career diverges from the uninterrupted one | `87edd97` |
| F906 | high | Disease tests pass with the entire disease model removed | `860d2ca` |
| F943 | high | Finance tab loan UI is broken three ways: wrong arity, nonexistent repay/info methods, and… | `cabd03f` |
| F944 | high | Analytics tab (F10) never renders data: `Engine.has_singleton("SimBridge")` is false for autoloads and… | `cabd03f` |
| F945 | high | `SimBridge` facade silently drops 10 `SimProxies` methods and 1 `SimActions` method;… | `27da699`, `860d2ca`, `ca7d8f8`, `f5caca4` |
| F949 | high | Scenario goal tracker is never initialised in the player flow, so the goal HUD is stuck at 0%; the… | `216cc8f` |
| F957 | high | Tilt-shift divides 0/0 when `blur_amount == 0` (set by FjordCamera underwater), so the top and bottom… | `ca7d8f8` |
| F1001 | high | Shipped JSON research tree replaces the built-in tree with effect keys the engine never reads | `dde677b` |
| F1003 | high | Research `market_price_bonus` can never raise the price: a fraction is multiplied and then gated with `>… | `dde677b` |
| F1004 | high | 17 of 25 JSON research nodes are invisible and unresearchable: the UI knows 5 branches, the data and… | `dde677b` |
| F1005 | high | Scenario "ressursrente" is won on day 1: `profit_target` (25 M) is below the 60 M starting capital | `216cc8f` |
| F1006 | high | Balance-based "revenue" achievements fire on day 1 from starting capital and pay out up to 750 k NOK | `b1a5a37` |
| F1014 | high | Tutorial chain can stall: "first sale" requires balance above 10 M NOK and step 6 only triggers at lice… | `216cc8f`, `f5caca4` |
| F1027 | high | Research `mortality_reduction` is computed and then overwritten, so the research effect (and… | `dde677b` |
| F1071 | high | `CompetitorAI` is only partly persisted: acquired rivals come back and AI state resets, so loaded runs… | `87edd97` |
| F1072 | high | Scenario-start populations get a private unseeded `GrowthModel` and an unconfigured `MortalityModel`;… | `87edd97` |
| F1073 | high | Determinism and save gates are same-process and shallow, so they pass while 48 of 64 save/load… | `87edd97` |
| F1128 | high | Monthly "legacy" tax re-taxes the cumulative YTD base every month and never refunds, so effective tax… | `b1a5a37` |
| F1129 | high | MAB enforcement is dead on every realistic path: `CurrentBiomassTonnes`/`IsOverMAB` read a stocking-time… | `26399ac` |
| F1130 | high | Feed (the largest cost) is waived whenever cash is below one day's feed bill: COGS is booked, cash never… | `b1a5a37` |
| F1131 | high | Resource-rent tax can never be collected: three stacked defects (1000x unit error, standing-biomass… | `b1a5a37` |
| F1132 | high | Smolt purchases, site build and about 10 other cash outflows never reach the P&L, so every figure… | `b1a5a37` |
| F1133 | high | A Mattilsynet lice order is never closed by compliance: the player delouses, lice drop, and the engine… | `26399ac` |
| F1137 | high | Loss carry-forward: offset uses 80% of the loss stock rather than 80% of income, utilisation is capped… | `b1a5a37`, `87edd97` |
| F1138 | high | Loan principal repayments (and bond interest) are booked as operating expense; `RecordInterest` has no… | `b1a5a37` |

## 2. New findings from the re-review

Four reviewers read `main...44a0d89` (engine; bridge; UI, scenes and shaders; tests, tooling and workflows). Eight of their findings restate items left open in the original review (F271, F600, F665, F692, F708, F763, F947, F952). The fixes landed in `f5caca4` (re-review fixes 1), `b108cfb` (re-review fixes 2), `ed688c4` (CI tooling) and `08ca977` (bridge gate and 3D visuals).

| Reviewer | Critical | High | Medium | Low | Resolved |
|---|---|---|---|---|---|
| Engine | 0 | 0 | 2 | 9 | 1 medium, 4 low |
| Bridge | 0 | 1 | 4 | 5 | 1 high, 4 medium |
| UI | 0 | 1 | 3 | 4 | 1 high, 3 medium; 1 low by design |
| Tests and tooling | 1 | 1 | 3 | 9 | all critical, high and medium; 2 low; 1 low partial |
| **Total** | **1** | **3** | **12** | **27** | |

| Severity | Finding | Status |
|---|---|---|
| critical | CI "Godot MCP: Shader check" could not fail: it loaded shaders under `--headless`, where nothing is parsed (F958) | Fixed `ed688c4`: static hint check plus a compile under a real renderer |
| high | Market report and forecast getters drew from the saved market RNG stream, so opening the inbox changed the economy | Fixed `f5caca4` (stable-hash noise; `MarketForecastReadTests`) |
| high | The toast layer swallowed every ESC and hid all toasts for the session; the save list's ESC never fired (F665) | Fixed `b108cfb` |
| high | The bridge contract gate missed getter locals, NodePath bindings, autoload globals, direct chains and property reads, so dead calls passed | Fixed `08ca977`: also dynamic `call("…")` names and attribute reads (properties and signals); 16 dead calls fixed, four more orphaned UI scripts listed |
| medium | Buying a distressed asset charges CAPEX for listings that grant nothing (F271), now reachable through the bridge | Open, dormant: no player UI while EmpirePanel is orphaned |
| medium | Automatic sludge income satisfied the tutorial's first-sale gate | Fixed `f5caca4` |
| medium | `ValorizeSludgeNow` returned `decimal`, which Godot never exposes (F947) | Fixed `f5caca4` |
| medium | Per-session bridge fields survived New Game and Load (F064) | Fixed `f5caca4` |
| medium | Weather alerts were stamped with the previous day (a 3-day listing lasted 2) | Fixed `b108cfb` |
| medium | The vaccination-window alert was on nine months a year (F763) | Fixed `b108cfb` |
| medium | The N hotkey fired while typing | Fixed `b108cfb` |
| medium | The tutorial card ate ESC while shown | Fixed `b108cfb` |
| medium | ✕ on the Export and Research tabs did nothing | Fixed `b108cfb` |
| medium | `GODOT_BIN` was ignored by the godot-mcp-ops steps | Fixed `ed688c4` |
| medium | A skipped step counted as a pass; `$PYTHON` only half honoured | Fixed `ed688c4` (`CI_REQUIRE_ALL=1` in GitHub CI) |
| medium | No timeout on stock macOS, and an empty command array aborts bash 3.2 | Fixed `ed688c4` |
| low | Sea-temperature stress mortality hit land-based RAS tanks | Fixed `f5caca4` |
| low | A crisis loaded from a save lost its competitor name and discount | Fixed `f5caca4` |
| low | Loan ids repeated after an early repayment | Fixed `f5caca4` |
| low | Distressed listings stayed buyable up to 6 days after expiry | Fixed `f5caca4` |
| low | `validate_scenes.sh` passed without the validator's result line | Fixed `ed688c4` |
| low | GitHub action SHA pins unverified | Verified 2026-10-14 with `gh api`: all six pins match their tags |
| low | UI flow runner had no check floor and a narrow error grep | Partial `ed688c4`: exact check count plus an every-test-runs guard; C# exceptions and `push_error` are still not grepped |
| low | Space never activates a focused button | By design (F618 trade-off, RUN_LOG §7) |
| low | Seed-13 career bound loosened from -10M to -15M | Dispute (RUN_LOG §8) |
| low | `Initialize(seed)` never records `State.RandomSeed` | Fixed on `wave1/sim-2` (P0-07, `InitializeSeedTests`) |
| low | `AcceptContractOffer` signs whatever sits at the clicked index | Open |
| low | The SuperSmolt tier is unreachable (batches stop at 300 g) | Open |
| low | Vertical integration completes on wall-clock time; its CAPEX hook books an asset that never acts | Open, dormant (allowlisted F040) |
| low | New RealData layers: reversed-range statistics throw, a bare catch, culture-dependent cache keys | Open (not wired into the sim) |
| low | Mod scenarios cannot start on a fresh launch (F692) | Open |
| low | `AttemptAcquisitionTarget` mixes NOK millions with NOK | Open, dormant (no caller) |
| low | The post-smolt bridge stubs bill cash and change nothing (part of F383) | Open, dormant (no UI) |
| low | `ApplyScenario` double-initialises and drops a registered scenario | Open, dormant (no caller) |
| low | QA hooks are public and ungated (F708) | Open |
| low | The save list's two-step delete label goes stale | Open |
| low | The pen detail card still shows constants as live data (F952) | Open |
| low | "Export all" always reports success (F600) | Open |
| low | `ScenarioCampaignTests` ledger assertion cannot fail | Open |
| low | `CrossProcessDeterminismTests` does not enforce its probe timeout | Fixed on `wave1/sim-2` (P0-07, `ProbeRunner_KillsAHungProbe_AtItsTimeout`) |
| low | Determinism allowlist keyed by exact line text; the ban misses `DateTimeOffset.Now` and friends | Fixed on `wave1/sim-2` (P0-07: Roslyn scan keyed by symbol and count, wider ban) |
| low | "profitable-quarter" cannot unlock on day 1, so its day-1 test proves nothing | Open |
| low | The fake exporter in `test_ci_tools.py` models only success | Open |

## 3. Original medium and low findings fixed on the branch

The 369 original mediums were not re-audited one by one. These are fixed and tested (the commit messages name them):

- **Medium:**
  - `f5caca4`: F064 (bridge session state), F947 (sludge sale).
  - `44a0d89`: F071 (visual-qa/design-token workflows), F176 (pytest could not run in CI; the first run waits on the push gate), F178 (export presets tracked, the export gate exports).
  - `d2810e7`: F593 (tab ESC), F1061 (D/W/M keys).
  - `b108cfb`: F665 (toast ESC), F763 (vaccination window).
  - `b1a5a37`: F896 (probe feed double count).
  - `ed688c4`: F958 (shader gate).
  - `dde677b`: F1012 (scenario research ids).
  - `08ca977`: F744 (wellboat, feed-barge and rival-farm visuals), F745 (Sim3DSync day and zero values).
  - `33cc73f`: F1110 (visual diff gate).
- **Low:** `08ca977`: F940 (dead bloom sync).
- **Partly fixed:**
  - F023: actions pinned and scoped, but `ci.yml` and `verify.yml` still overlap.
  - F600: per-file exports report failure; "export all" does not.
  - F672: research tree layout fixed, but it still rebuilds on refresh.
  - F753: the fjord controller's dead storm call removed; the prototype files remain.
  - F952: the pen card shows each site's lice and oxygen; the other values are still constants.
  - F1013: scenario sites taken from the JSON; it remains a second source of truth.

## 4. Gates at `08ca977`

`tools/ci.sh` runs 17 steps. With `CI_REQUIRE_ALL=1` (set in GitHub CI) a skipped step fails the run. What the gates now catch that they did not at the baseline:

- **Shader check** (`ed688c4`):
  - Every uniform hint is checked against the Godot 4.7.2 keywords.
  - Every shader is compiled in a small window, or under `xvfb-run` on Linux.
  - The broken `hint_default_noise` shaders and a deliberate type error are caught.
- **Bridge contract** (`860d2ca`, `08ca977`): guarded names, call arity, and `decimal` signatures that Godot cannot marshal. It now also covers getter locals, autoload globals, direct `get_node` chains, NodePath-bound `_sim_bridge`, `get`/`set("prop")`, `"prop" in x`, dynamic `call("Name")` and attribute reads, skipping comments and string contents. 1,102 reaches are checked.
- **UI flow tests:**
  - The gate did not exist at the baseline (added in `27da699`); it now makes 224 checks with real key and mouse events.
  - The runner requires the exact count, and a pytest checks that every test function is called.
- **Determinism:** a save/load matrix (8 seeds × events on/off × 5 save days), cross-process twin runs, and RNG-free UI reads (`87edd97`, `f5caca4`).
- **Economy:** a ledger reconciliation over 730-day careers on three seeds (`b1a5a37`).
- **Clean checkout:** `tools/ci_clean_checkout.sh` runs the whole gate on a fresh clone (`c42150b`).
- **Visual diff:** a missing shot fails, and an explicit threshold is honoured (`33cc73f`). The harness fails on a mostly black frame (`ca7d8f8`).
- **Disease tests** fail without the model (`860d2ca`). **Treatment kill** is checked per method (`8f4e235`).

## 5. Waiting on the owner

| Item | Decision needed |
|---|---|
| Push gate | Approve pushing `review-remediation` (or a PR branch). Then ci, verify and visual-qa run for the first time; visual-qa and design-tokens need the self-hosted Mac runner. |
| F1024 water chemistry | The fix is on `review/f1024-water-chemistry` (`8123ea1`). Recalibrate the three legacy tests that depend on the broken oxygen signal (RUN_LOG §8), then merge. |
| F456 storm season | Renormalize the default weather chain. This shifts every career's storm statistics. |
| Seed-13 career bound | Confirm the -15M runaway guard, or make disaster careers survivable (RUN_LOG §8). |
| ISA outbreak scenario | Decide what an outbreak is in the SIR model; today it is inert (RUN_LOG §8). |
| Orphaned panels | Wire or delete Empire, Stakeholder, Genetics, CompetitorDashboard, Crisis, IPODialog, MarketChannelPanel and ProcessingMixPanel. Distressed-asset purchases (F271) wait on the Empire decision. |
| F177 MCP addon | Confirm the exemption (RUN_LOG §7), or vendor or remove the addon to clear the editor and export warnings. |
| ~~WellboatJob ids~~ | Decided (roadmap D8) and done on `wave1/sim-2` (P0-07): the id is day, site, kind and booking sequence; `Job_Defaults` asserts it. |

## 6. Open code work (no decision needed)

- **F383:** post-smolt facilities need bridge and UI verbs; the bridge's post-smolt stubs bill cash and do nothing.
- **F746:** three sim-to-3D sync layers overlap. Sim3DSync's own node paths resolve to nothing, so it only writes season and water uniforms that FjordVistaBridge also writes.
- **F750:** the terrain's noise parameters produce needle-like shards in every fjord frame (`08_fjord_day.png`). This is the largest visible defect.
- **Lows:** the 18 open lows in section 2, and the original mediums that were not re-audited.

## 7. Commits

Oldest first. Each message holds the commands, results and mutation checks behind it.

- `b081938` run: review chore — .mcp.json: ${HOME} instead of bare $HOME so Claude Code expands the blender MCP path
- `0242f7e` run: docs — agent runbook (session loop, hard-won rules, gates) + session-5 W3/S4 claim
- `1385500` run: review — full-repo code review baseline (3 critical / 55 high / 369 medium / 373 low) in docs/CODE_REVIEW.md
- `d93c8ec` run: W3 S4 — RealData v2 layers behind IRealDataProvider; build green (F197), wall clock removed
- `27da699` run: review F928/F616/F617/F056/F945 — player save/load reachable; menu flows fixed; headless UI gate
- `87edd97` run: review persistence + determinism — save/load continues exactly; same seed reproduces across processes
- `b1a5a37` run: review economy ledger — every krone booked once; tax, feed, loans, smolts, certification fixed; reconciliation gate
- `26399ac` run: review engine correctness — transfers wired, smolt tier kept, MAB live, zone biomass summed, lice orders close on compliance
- `26e766c` run: review lice mitigation — skirts, containment and barriers now lower the site's own lice
- `dde677b` run: review research tree — one canonical tree, every node and key acts, every node visible
- `216cc8f` run: review scenarios + tutorial — every scenario starts with a farm, the tutorial plays to the end, goals and rules are real
- `1467d03` run: review crisis capitalism — a rival's bankruptcy opens a crisis and lists its assets; listings keep time and survive a save
- `fa65b80` run: review facilities — RAS and offshore farms stock, grow and harvest; post-smolt batches reach the sea; facility CAPEX booked
- `cabd03f` run: review Godot panels 1 — loan card takes, shows and repays loans; analytics charts fill; data export writes CSVs
- `f05329b` run: review Godot panels 2 — a click on a 3D pen opens its details; the Fjord tab makes the vista clickable; the pen list is visible
- `d2810e7` run: review Godot panels 3 — Space pauses except while typing, Enter activates buttons, ESC closes the right thing, mod toggles take clicks
- `8e97c4c` run: review alerts — a lasting condition is announced once, not twice a tick; weather and disease alerts stay listed
- `fd254ae` run: review pens tab — each site shows its own lice and oxygen, not the company figures
- `ca7d8f8` run: review visual QA — the underwater view renders; baseline re-promoted after fixing what the shots showed
- `860d2ca` run: review test hardening — disease tests fail without the model; the bridge contract checks guards and arity
- `c42150b` run: review clean checkout — CI passes on a fresh clone; the Godot MCP audits check the checkout, not a running editor
- `44a0d89` run: review CI workflows — the export gate exports a build, pytest gets installed, workflows are pinned and scoped
- `f5caca4` run: review re-review fixes 1 — forecast reads draw no RNG, sludge sale works, a load resets bridge state, the tutorial needs a fish sale
- `b108cfb` run: review re-review fixes 2 — ESC no longer silences toasts or sticks on the tutorial card, N types in text fields, ✕ closes Export
- `ed688c4` run: review CI tooling — the shader gate compiles every shader, a skipped step fails GitHub CI, timeouts work on stock macOS
- `33cc73f` run: review visual diff gate — a missing screenshot fails the diff; an explicit threshold is honoured
- `8f4e235` run: review lice treatments + escape score — each method kills its own efficacy; one escape no longer zeroes the escape score for good
- `08ca977` run: review bridge gate + 3D visuals — the contract test follows every bridge reach; wellboats, feed barges and rival farms sail and sit on the fjord
