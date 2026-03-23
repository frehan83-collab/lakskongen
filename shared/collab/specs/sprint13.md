# Sprint 13 Spec — "The Hook" (Orchestrator Decision)

## Background
3 agents proposed 9 features total. After cross-analysis, there is STRONG CONSENSUS on the core problem:

> **We have a 9/10 simulation that plays like a 5/10 game. The missing pieces are emotional hooks, decision moments, and visible progress.**

## Proposals Summary

| Agent | Feature 1 | Feature 2 | Feature 3 |
|-------|-----------|-----------|-----------|
| **Sonnet** (Architect) | Decision Moments + Consequences | Production Chain Feedback Loops | Crisis Recovery Gameplay |
| **Kimi** (Gameplay) | Daily Pulse ("one more day") | Living Fjord Events | Mastery Unlock System |
| **Gemini** (UX) | Visual Feedback on Pens | "Next Action" Suggestion UI | [Polish & Onboarding] |

## Cross-Analysis: What All 3 Agree On

1. ✅ **Player has no reason to play "one more turn"** — no countdowns, no visible progress, no upcoming events
2. ✅ **40 subsystems run invisibly** — player doesn't FEEL the simulation
3. ✅ **No decision moments** — game runs on autopilot, player watches numbers
4. ✅ **Crises punish instead of engaging** — leads to save-scumming, not mastery
5. ✅ **Missing mid-game guidance** — tutorial ends, player abandoned

## Orchestrator Decision: Sprint 13 Features

Merging the best ideas from all 3 proposals. Building the **minimum viable "one more turn" loop**:

### T83: Decision Engine + Daily Pulse (HIGHEST PRIORITY)
**Merged from:** Sonnet's Decision Moments + Kimi's Daily Pulse + Gemini's "Next Action" UI

**Scope:**
- `DecisionManager.cs` — surfaces critical choices from existing subsystems (lice, disease, contracts, harvest)
- `DecisionPanel.gd` — popup with trade-off preview, timer, 2-4 options
- `MorningBriefing.gd` — start-of-day summary (weather, events, market changes)
- "Coming Soon" section in Dashboard — next 7 days preview with countdowns
- "Next Action" suggestion widget in HUD
- Max 3 active decisions, auto-resolve timer, tutorial decision at minute 20

**Builder:** Sonnet (Anthropic) — complex multi-file, C# + GDScript integration
**Estimated:** 3-4 days equivalent

### T84: Crisis Response System
**Merged from:** Sonnet's Crisis Recovery + Kimi's Living Fjord Events

**Scope:**
- `CrisisResponseManager.cs` — crisis detection → response panel → recovery tracking
- `CrisisPanel.gd` — dramatic modal with response options + advisor hints
- `RecoveryDashboard.gd` — recovery progress widget
- Chain events: warning → escalation → crisis (from Kimi's predictable surprises)
- Tutorial crisis at day 60-90 (scripted, moderate)
- Achievement: "Phoenix Rising" for recovery
- Fjord Tension meter per zone (from Kimi)

**Builder:** Kimi (OpenRouter) — strong on emotional game design, reasoning model
**Estimated:** 3-4 days equivalent

### T85: Production Goals + Visual Feedback
**Merged from:** Sonnet's Production Feedback Loops + Gemini's Visual Feedback + Kimi's Mastery hooks

**Scope:**
- `BottleneckAnalyzer.cs` — weekly scan, identify slowest stage, calculate ROI
- `ProductionGoal.cs` — set targets ("500 tons/year"), track progress
- Interactive ProductionChainPanel — click bottleneck → see upgrade options
- Color-coded chain: Green (flowing) → Yellow (80%) → Red (bottleneck)
- Milestone unlocks: "First 1000-ton harvest" → new research branch
- Simple mastery seed: track player expertise across 3 domains (biology, business, sustainability) — full system in Sprint 14

**Builder:** DeepSeek V3.2 (OpenRouter) — strong on math/algorithms, optimization logic
**Estimated:** 2-3 days equivalent

## Rejected / Deferred

| Feature | Reason | When |
|---------|--------|------|
| Full Mastery System (Kimi) | Too large for one sprint, needs content. Seed it in T85. | Sprint 14 |
| Pen Visual Effects (Gemini) | Requires 3D/shader work, not blocking "one more turn" | Sprint 15 |
| Delivery Scheduler drag-drop (Kimi) | Nice-to-have, not core loop | Sprint 15 |
| Competitor News Feed (Kimi) | Good idea, deferred to after core loop works | Sprint 14 |
| Named fish strains (Kimi) | Emotional hook, but genetics system needs interaction layer first | Sprint 14 |

## Challenge Protocol for Build Phase

Each builder receives:
1. This spec
2. The OTHER builders' specs (for awareness)
3. `shared/collab/agents.md` with file ownership
4. Instruction to write questions to `shared/collab/questions/` if they need info from another builder's files

## Acceptance Criteria

- [ ] Player receives first decision within 20 game-days
- [ ] Dashboard shows "Coming Soon" events for next 7 days
- [ ] HUD shows "Next Action" suggestion
- [ ] Morning briefing appears on day start
- [ ] Crisis response panel shows on major disease/lice event
- [ ] Production chain shows bottleneck colors
- [ ] At least 1 production goal can be set and tracked
- [ ] Build green, no test regressions

## Vision Check

> "By hour 2, players understand the depth is enormous, they've had wins AND scares, and they can SEE the path from small farmer to industry titan."

With Sprint 13: ✅ Decisions show depth. ✅ Crisis creates scares. ✅ Production goals show the path.

---
*Spec written by: Opus (Orchestrator) — 2026-03-20 15:10 CET*
*Based on proposals from: Sonnet, Kimi, Gemini*
*Approved by: Pending Fredrik review*
