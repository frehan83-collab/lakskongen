# Sonnet Proposal — Systems Architect Perspective
**Agent:** Claude Sonnet 4.5  
**Role:** Systems Architect  
**Focus:** Simulation depth, system integration, "is this actually fun?"  
**Date:** 2026-03-20

---

## CRITICAL ASSESSMENT: What's Missing for "One More Turn"?

I've read VISION.md, BOARD.md, and analyzed the codebase (40 subsystems in SimulationEngine, comprehensive UI panels, full production chains). Here's the brutal truth:

**We have SYSTEMS. We don't yet have GAMEPLAY LOOPS.**

The difference:
- **System:** "DiseaseSystem tracks 5 diseases with infection rates"
- **Gameplay Loop:** "I just got a disease alert → I choose treatment → I watch lice count → decision pays off → dopamine → one more turn"

VISION.md says: *"By hour 2, players understand the depth is enormous, they've had wins AND scares, and they can SEE the path from small farmer to industry titan."*

**Current state:** We have depth. We're missing the *visible reward cycles* that make depth compelling.

---

## THE 3 MISSING FEATURES (Priority Order)

### 1. 🎯 **DECISION MOMENTS WITH VISIBLE CONSEQUENCES**
**What:** A "Decision Panel" system that surfaces critical choices with clear trade-offs and shows outcomes

**Why this matters for VISION:**
- VISION says: *"Regulatory chess isn't boring — it's a strategic puzzle"* → Right now it's invisible background math
- VISION says: *"One more turn syndrome — Always something about to complete"* → Need visible countdowns with decisions attached
- VISION says: *"Easy to start, hard to master"* → Decisions teach game depth naturally

**What to build:**

#### A. Decision Event Queue (C# Core)
```
src/LakskongenSim/Core/DecisionManager.cs
src/LakskongenSim/Core/DecisionEvent.cs
src/LakskongenSim/Core/DecisionOutcome.cs
```

**Core logic:**
- Storyteller + DiseaseSystem + Regulatory systems push decisions to queue
- Each decision has: 2-4 options, cost/benefit preview, countdown timer, consequence visibility
- Examples:
  - **Lice threshold exceeded:** "Treat now (50k NOK, 2% mortality) vs Wait (risk MAB limit, potential fine 200k)"
  - **Smolt ready:** "Transfer to Site 1 (high temp risk) vs Site 2 (higher license cost) vs Delay (growth penalty)"
  - **Contract offer:** "Accept Japan export (1.2x price, strict quality) vs EU bulk (0.9x price, stable)"
  - **Disease outbreak nearby:** "Cull affected pen (50% loss, contain spread) vs Treat (80k NOK, 70% survival)"

#### B. Decision Panel UI (GDScript)
```
godot/scripts/ui/DecisionPanel.gd
godot/scenes/ui/DecisionPanel.tscn
```

**UI Requirements:**
- **Popup modal when decision triggered** (not buried in log)
- **Timer countdown** (e.g., "Decide in 3 days or auto-default")
- **Trade-off preview:**
  ```
  Option A: Treat with Hydrogen Peroxide
    Cost: 45,000 NOK
    Mortality: +1-3%
    Lice reduction: 60-80%
    Resistance risk: Medium
  
  Option B: Mechanical Delousing
    Cost: 120,000 NOK
    Mortality: +5-8%
    Lice reduction: 85-95%
    Resistance risk: None
  
  Option C: Wait
    Cost: 0 NOK
    Risk: MAB breach in 5-7 days → 200k fine + shutdown
  ```
- **Outcome notification** 2-5 days later: "Your treatment reduced lice by 72%. No resistance detected."

#### C. Integration Points
**Files to modify:**
- `src/LakskongenSim/Events/Storyteller.cs` → Add `PushDecision(DecisionEvent)` method
- `src/LakskongenSim/Health/SeaLiceModel.cs` → Trigger decision when lice > threshold
- `src/LakskongenSim/Health/DiseaseSystem.cs` → Trigger decision on outbreak
- `src/LakskongenSim/Core/SimulationEngine.cs` → Daily DecisionManager.Update() call
- `godot/scripts/autoload/GameBridge.cs` → Add decision event signals

**Design decisions:**
1. **Limit active decisions to 3 max** → Prevents decision paralysis
2. **Decisions pause at 4x+ speed** → Force player engagement with critical choices
3. **Tutorial teaches decision system in first 30 min** → Onboard to core loop early
4. **Auto-resolve timer** → Keeps game flowing even if player walks away

**Risks:**
- **Too many decisions = annoying** → Mitigation: Only surface decisions with >50k NOK impact or >5% mortality
- **Preview accuracy** → Show ranges (60-80%) not exact numbers, calibrate with real data
- **Player ignores system** → Make consequences VISIBLE (charts show impact, achievements tied to good decisions)

---

### 2. 🔄 **PRODUCTION CHAIN FEEDBACK LOOPS**
**What:** Make the Anno 1800-style production chain **interactive and goal-oriented** instead of passive display

**Why this matters for VISION:**
- VISION says: *"Production chains — Raw material → processing → sale. Players love optimization"*
- VISION says: *"Interconnected systems — Every production feeds another"*
- Current state: ProductionChainPanel.gd shows data beautifully BUT it's **read-only**. No interaction, no goals, no "just one more optimization."

**What to build:**

#### A. Bottleneck Analyzer (C# Core)
```
src/LakskongenSim/Processing/BottleneckAnalyzer.cs
src/LakskongenSim/Processing/ProductionGoal.cs
```

**Logic:**
- Scan production chain every week
- Identify slowest stage (e.g., "Smolt production 40% below sea site capacity")
- Calculate upgrade ROI: "Add 1 smolt tank → +20k smolt/year → +150 tons harvest → +4.2M NOK revenue (12-month payback)"
- Push notifications: "🔧 Bottleneck: Smolt shortage limiting 3 sea sites. Suggested action: Build Smolt Facility (1.2M NOK, 18-month ROI)"

#### B. Production Goals & Milestones
**New GameState fields:**
```csharp
public List<ProductionGoal> ActiveProductionGoals { get; set; } = new();
public List<ProductionMilestone> UnlockedMilestones { get; set; } = new();
```

**Examples:**
- **Goal:** "Produce 500 tons/year" → Progress bar, ETA based on current growth
- **Milestone:** "First 1,000 ton harvest" → Unlocks achievement + new research branch
- **Challenge:** "Achieve 95% HOG-to-fillet yield" → Requires processing tech + quality control

#### C. Interactive Production Chain UI
**Modify:**
```
godot/scripts/ui/ProductionChainPanel.gd
```

**New interactions:**
1. **Click bottleneck stage** → Show upgrade options (build hatchery, add tanks, hire crew)
2. **Set production target** → e.g., "Target: 800 tons Adult fish by Day 400" → UI shows gap + suggestions
3. **Hover stage** → Tooltip shows:
   - Current throughput (tons/year)
   - Capacity utilization (78%)
   - Downstream demand ("Sea sites need 15k more smolt/year")
   - Upgrade cost + ROI

**Files to modify:**
- `godot/scripts/ui/ProductionChainPanel.gd` → Add click handlers, goal display, bottleneck highlights
- `godot/scripts/autoload/SimBridge.gd` → Add production goal bridge methods
- `src/LakskongenSim/Core/GameBridge.cs` → Wire bottleneck signals

**Design decisions:**
1. **Color-code bottlenecks:** Green (flowing), Yellow (80%+ capacity), Red (bottleneck limiting growth), Blue (upgrade available)
2. **Auto-suggest next expansion** → "You have 4 sea sites but only 1 hatchery. Add hatchery to supply 2 more sites."
3. **ROI calculator built-in** → No spreadsheet required, show payback in game-time
4. **Tutorial scenario:** "Your First Bottleneck" → Walk player through identifying smolt shortage → building hatchery → seeing harvest increase

**Risks:**
- **Feature creep into full factory-builder** → Mitigation: Keep it diagnostic + suggestive, not micromanagement
- **Wrong suggestions** → Calibrate analyzer with real Norwegian aquaculture ratios (1 hatchery per 3-4 sea sites typical)
- **Overwhelms new players** → Hide advanced analytics behind research unlock "Production Analysis I"

---

### 3. 📊 **CRISIS RECOVERY GAMEPLAY**
**What:** Turn disasters into compelling gameplay moments instead of save-scumming triggers

**Why this matters for VISION:**
- VISION says: *"By hour 2, players have had wins AND scares"* → Scares must be *recoverable* and *teach mastery*
- VISION says: *"Emergent stories — Systems create unique narratives"* → Crisis = story, IF player can fight back
- Current problem: Disease outbreak or market crash → player reloads save OR quits in frustration

**What to build:**

#### A. Crisis Response System (C# Core)
```
src/LakskongenSim/Events/CrisisResponseManager.cs
src/LakskongenSim/Events/CrisisRecoveryPlan.cs
```

**When crisis detected (disease outbreak, lice surge, market crash, bankruptcy warning):**
1. **Pause simulation** (or slow to 1x if already paused)
2. **Show Crisis Panel** with:
   - **Situation:** "PD outbreak in Site 3 — 45k fish infected, spreading to Site 2"
   - **Impact forecast:** "Without action: 70% mortality in 14 days, -12M NOK revenue"
   - **Response options:**
     - Emergency cull Site 3 (stop spread, lose 45k fish, insurance covers 60%)
     - Antibiotic treatment (80k NOK, 50-70% survival, 30-day withdrawal)
     - Quarantine + biosecurity (slow spread, 40% total loss, save Site 2)
   - **Loans available:** "Emergency loan: 5M NOK @ 8% APR"
   - **Industry support:** "Government crisis fund: 2M NOK grant (if ASC certified)"
3. **Track recovery:** Show "Days Since Crisis" counter + recovery progress bar
4. **Achievement unlock:** "Survived PD Outbreak" → Unlocks disease resistance research

#### B. Crisis Learning System
**After crisis resolved:**
- **Debrief screen:** "What happened: Lice count exceeded MAB threshold → +14 days to treat → spread to 3 sites. Next time: Treat earlier or improve biosecurity."
- **Unlock crisis-specific research:** e.g., "Emergency Response Protocol" (reduces future treatment cost 20%)
- **Memorial milestone:** "Biggest Crisis Overcome: PD Outbreak (Day 234, recovered by Day 267)"

#### C. UI Components
**New files:**
```
godot/scripts/ui/CrisisPanel.gd
godot/scenes/ui/CrisisPanel.tscn
godot/scripts/ui/RecoveryDashboard.gd
```

**CrisisPanel features:**
- **Dramatic visual** (red background, alarm icon, crisis name in bold)
- **2-4 response options with clear trade-offs**
- **"Call Advisor" button** → Tutorial hint system (e.g., "Experienced farmers recommend culling infected pen to save the rest")
- **"Review Finances" shortcut** → Jump to loan panel
- **Timeline:** "You have 7 days before MAB forces shutdown"

**RecoveryDashboard:**
- Small widget showing:
  - "Recovering from: ILA Outbreak"
  - "Progress: 67% (repopulated 2/3 sites)"
  - "Financial impact: -8.2M NOK (insurance covered 3.1M)"
  - ETA to pre-crisis production levels

**Files to modify:**
- `src/LakskongenSim/Events/CrisisCapitalismSystem.cs` → Add RecoveryPlan integration
- `src/LakskongenSim/Finance/LoanSystem.cs` → Add emergency loan flag (higher rate, faster approval)
- `src/LakskongenSim/Core/SimulationEngine.cs` → Wire CrisisResponseManager
- `godot/scripts/autoload/GameBridge.cs` → Crisis event signals

**Design decisions:**
1. **Always provide an option** → Even bankruptcy has "Take emergency loan + government bailout"
2. **Insurance system matters** → Crisis shows insurance payout clearly
3. **Recovery is a minigame** → Rebuilding becomes its own satisfying loop
4. **Achievements for recovery** → "Phoenix Rising" (recover from <10% production to 100%), "Crisis Manager" (survive 3 major crises)
5. **Tutorial crisis at Day 60-90** → Scripted moderate outbreak to teach system safely

**Risks:**
- **Crisis spam kills fun** → Mitigation: Max 1 major crisis per 60 days, cluster small events
- **Too easy to recover** → Calibrate: crisis should cost 5-15% annual revenue, insurance covers 40-60%
- **Player paralysis** → "Call Advisor" provides clear recommendation for new players
- **Save-scumming persists** → Ironman mode achievement, + recovery is MORE REWARDING than perfect play (unlock better research)

---

## VISION ALIGNMENT CHECKLIST

| Criterion | Feature 1: Decisions | Feature 2: Production Loops | Feature 3: Crisis Recovery |
|-----------|---------------------|----------------------------|---------------------------|
| **"One more turn" syndrome** | ✅ "Just handle this lice decision first..." | ✅ "Just optimize this bottleneck..." | ✅ "Just get back to 80% production..." |
| **First 2 hours experience** | ✅ Tutorial decision at Min 20 | ✅ First bottleneck at Min 45 | ✅ Scripted small crisis at Min 90 |
| **Easy to start, endlessly deep** | ✅ Start: 2 options. Late: 4 options + tech modifiers | ✅ Start: simple chain. Late: multi-site optimization | ✅ Start: clear advisor hints. Late: complex multi-crisis juggling |
| **Visible results** | ✅ Outcome notifications + stat changes | ✅ Production graphs spike after fixes | ✅ Recovery progress bar + milestone unlocks |
| **Norwegian authenticity** | ✅ Real Norwegian regulations (MAB, traffic lights) drive decisions | ✅ Real production ratios from industry data | ✅ Real crisis types (PD, ILA, algal blooms) |
| **Real farmer useful?** | ✅ Decision trade-offs mirror real farmer calculus | ✅ Bottleneck analyzer = real production planning | ✅ Crisis response = real biosecurity protocols |

---

## WHAT OTHER AGENTS MIGHT MISS (Preemptive Challenges)

### If a **Gameplay Designer** proposes these:

#### "Add a tutorial tooltip system"
**Challenge:** Tooltips teach MECHANICS. We need to teach MASTERY through DOING.
- **Why it's insufficient:** VISION says "easy to start, hard to master" — tooltips explain buttons, not strategy
- **Better:** Decision system TEACHES through consequence. "You chose cheap treatment → lice resistance developed → now you understand treatment rotation"

#### "More UI panels for data"
**Challenge:** We already have 15+ UI panels. More data ≠ more fun.
- **Why it's insufficient:** VISION says "visible results" not "visible data." Players need ACTION-RESULT loops, not dashboards
- **Better:** Interactive production chain with goals shows data IN CONTEXT of what player should DO

#### "Add a sandbox mode with no crises"
**Challenge:** Crises aren't bugs to remove — they're STORY GENERATORS.
- **Why it's wrong:** VISION explicitly says "had wins AND scares" and "emergent stories"
- **Better:** Difficulty settings adjust crisis SEVERITY (Easy = 50% damage, Hard = 150%) but keep crisis TYPES

### If a **UI Specialist** proposes:

#### "Redesign Dashboard with better graphs"
**Challenge:** Graphs don't create "one more turn" — GOALS do.
- **Missing:** Production chain needs interactive TARGETS, not prettier charts
- **Better:** "You're 120 tons short of your 500-ton goal. Expand Site 2 OR add hatchery?"

#### "Add more hotkeys and shortcuts"
**Challenge:** Efficiency ≠ engagement. Speed running a sim game misses the point.
- **Missing:** VISION says "relaxing yet complex" — not "twitch-reflex micromanagement"
- **Better:** Decisions that PAUSE game force thoughtful choices, not faster clicks

#### "Minimize decision popups for streamlined experience"
**Challenge:** This literally kills the core engagement loop.
- **Why it's wrong:** VISION first 2 hours: "First lice problem. Learn delousing." = DECISION MOMENT
- **Evidence:** Farming Simulator doesn't hide "your crop is ready" — that's the DOPAMINE

### If a **Systems Programmer** proposes:

#### "Optimize simulation to run 10x faster"
**Challenge:** Performance is good enough (T61: 0.04-1.03% of 60 FPS budget). Optimization NOW = premature.
- **Missing:** VISION says "Anno-style scaling" — we need MORE systems (vertical integration, multi-region), not faster math
- **Better:** Spend dev time on gameplay loops that make existing simulation COMPELLING

#### "Refactor genetics system for genomic selection accuracy"
**Challenge:** Simulation accuracy is table stakes, not the differentiator.
- **Why it's not priority:** VISION says "CK3 genetics meets aquaculture" = genetic STORYTELLING (inbreeding drama), not scientific paper accuracy
- **Better:** Add genetics decisions to DecisionManager: "Inbreed for pure growth trait (+15% risk) OR outcross for hybrid vigor?"

#### "Add machine learning to DiseaseSystem for realistic spread"
**Challenge:** Player can't understand ML black box = can't make strategic decisions.
- **Why it hurts gameplay:** VISION says "strategic puzzle" — ML removes player agency
- **Better:** Deterministic-but-complex disease with clear rules player can LEARN and MASTER

### Common mistakes sim games make at this stage:

#### ❌ **Mistake 1: "Depth first, hooks later"**
**What happens:** Build 50 systems, realize none are FUN, scramble to add gameplay in beta
**Evidence:** Our codebase has 40 subsystems but limited decision points
**Fix:** Decision/Production/Crisis systems add hooks to existing depth

#### ❌ **Mistake 2: "Realism = fun"**
**What happens:** Simulate every biological detail, players get bored
**Evidence:** VISION knows this — "stylized realism", "satisfying sounds", "ASMR processing"
**Fix:** Crisis recovery makes disasters DRAMATIC not depressing

#### ❌ **Mistake 3: "Players will discover the depth"**
**What happens:** 90% of players never see advanced features
**Evidence:** VISION explicitly says "Tutorial takes you through first full cycle by hour 2"
**Fix:** Decision system + bottleneck analyzer SURFACE depth through gameplay

#### ❌ **Mistake 4: "More features = better game"**
**What happens:** Feature bloat, no coherent core loop
**Evidence:** BOARD.md shows 76 completed tasks BUT VISION says "first 2 hours" is critical
**Fix:** These 3 features create THE CORE LOOP, everything else is content

---

## INTEGRATION RISKS & MITIGATION

### Risk 1: Decision fatigue (too many popups)
**Mitigation:**
- Limit to 3 active decisions max
- Only surface decisions >50k NOK impact OR >5% mortality
- Minor events stay in event log
- Tutorial teaches "ignore minor events, focus on critical decisions"

### Risk 2: Production chain too complex for new players
**Mitigation:**
- Hide bottleneck analyzer behind Research unlock
- Tutorial scenario walks through first bottleneck
- Difficulty Easy = clear single-step suggestions, Hard = multi-variable optimization

### Risk 3: Crisis system feels unfair
**Mitigation:**
- First crisis is tutorial (scripted, moderate, clear solution)
- Always provide viable recovery path
- Show probability ranges ("60-80% survival") not exact numbers
- Insurance + government support systems exist
- Achievements reward recovery (not just perfect play)

### Risk 4: Systems don't integrate cleanly
**Files that need coordination:**
- DecisionManager pushes to Storyteller (shared event queue)
- BottleneckAnalyzer reads from ProductionChainPanel (existing bridge methods)
- CrisisResponseManager triggers from DiseaseSystem/MarketSimulation/FinanceManager (already have crisis detection)
**Mitigation:** Use existing event bus (EventBus.cs), add new signal types, test integration with T44-style tests

### Risk 5: Performance impact
**Current performance:** 0.04-1.03% of 60 FPS budget (T61 benchmark)
**New systems overhead:**
- DecisionManager: ~0.01% (daily update, small queue)
- BottleneckAnalyzer: ~0.02% (weekly scan, simple math)
- CrisisResponseManager: ~0.01% (event-driven, not every frame)
**Total estimated: +0.04% → Still <2% of frame budget**

---

## FINAL VERDICT: Are these the right 3 features?

**Test against VISION.md's success metrics:**

| Metric | Current Risk | With These Features |
|--------|--------------|---------------------|
| **Average playtime: 20+ hours** | ⚠️ Risk: Players quit after 5 hours (no hooks) | ✅ Decision/Crisis loops = "just one more crisis to recover from" |
| **Steam review: 80%+ positive** | ⚠️ Risk: "deep but boring" reviews | ✅ "Every decision matters" reviews |
| **Daily active players (month 3): 2,000+** | ⚠️ Risk: One-and-done playstyle | ✅ Production goals + crisis recovery = replay value |
| **Mod submissions (year 1): 500+** | ✅ Already have mod framework | ✅ Crisis/decision mods will be huge (custom disasters) |

**Would a real Norwegian salmon farmer enjoy this?**
- ✅ Decisions mirror real daily choices (treat now vs wait, which site for smolt)
- ✅ Bottleneck analysis = actual production planning they do in Excel
- ✅ Crisis recovery = real biosecurity protocols and insurance claims

**Would this make Fredrik (producer) say "one more turn"?**
- ✅ Fredrik loves optimization (production chain) and crisis management (business background)
- ✅ Decision system teaches the depth that's already in codebase
- ✅ Each feature has clear "complete and see result" loops

**Alternative features considered and rejected:**
- ❌ Multiplayer co-op (BOARD T17 done, not blocking "one more turn")
- ❌ More processing plant types (Anno-style expansion, but no feedback loop yet)
- ❌ Weather disasters (already have WeatherSystem, missing RESPONSE gameplay)
- ❌ Employee management (vertical integration exists, not core loop)

---

## CONCLUSION

We're sitting on a Tier-1 simulation engine (40 subsystems, scientifically calibrated, 93% test pass rate) with **no core engagement loop visible to players in first 2 hours.**

These 3 features turn systems into gameplay:
1. **Decisions** = Surface the depth through player agency
2. **Production optimization** = Give depth a GOAL ("get to 500 tons")
3. **Crisis recovery** = Make setbacks COMPELLING not frustrating

**Estimated dev time:**
- Decision system: 3-4 days (C# core 1.5 days, UI 2 days, integration 0.5 days)
- Production feedback: 2-3 days (analyzer 1 day, UI modifications 1.5 days, calibration 0.5 days)
- Crisis response: 3-4 days (response manager 1.5 days, UI 2 days, tutorial integration 0.5 days)
**Total: 8-11 days for all 3 features**

**ROI:** These features determine whether VISION.md's *"By hour 2, players say 'one more turn'"* actually happens.

Without these: We ship a beautiful biology simulation that 90% of players quit after 3 hours.
With these: We ship **the game VISION.md describes.**

---

**Systems Architect Recommendation:** Build all 3 in parallel (minimal dependencies), integrate through existing EventBus, test with T44-style integration suite, validate with real player session (Fredrik plays first 2 hours with new features).

**Next:** Await challenges from other agents and Orchestrator's final spec.
