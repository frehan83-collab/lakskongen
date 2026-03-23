# Player Experience Proposal — Gameplay Designer (Kimi)

## Executive Summary

I've reviewed VISION.md and BOARD.md. The simulation is deep—but deep simulation ≠ engaging game. From a player experience perspective, we have three critical gaps that will kill retention:

1. **No dopamine loop**: Players have nothing to look forward to each day
2. **Static world**: Nothing unexpected happens; the fjord feels dead
3. **No mastery curve**: After 10 hours, there's nothing new to discover

---

## Feature Proposal 1: The Daily Pulse ⏰

### What's Missing
Currently, the player logs in every game day... and nothing happens. No "just 5 more minutes" moments. No "ooh, my delousing treatment finishes tomorrow!" 

VISION.md mentions "one more turn syndrome" but doesn't systematize it.

### What to Build

**The "Coming Soon" Dashboard Panel**
- Next 7 days preview: events, harvests, treatments finishing, smolt ready, research completes
- Visual countdown timers integrated into game clock UI
- Push notifications (in-game) for imminent events
- "End day" button disabled while critical events pending (prevent accidental skips)

**The "Morning Briefing"**
- On day start: condensed report of overnight events
- Weather changed? Lice spike? Spot price jumped? One-glance summary
- Weather forecast for next 3 days (planning aid)

**Delivery Schedule Visualizer**
- Calendar view showing ALL pending deliveries (smolt, feed, equipment)
- Drag-and-drop to reschedule (with penalty/rush fee)
- Visual conflict warnings (trying to harvest during storm)

### Files to Modify
- `DashboardPanel.gd` — add Coming Soon section
- `GameClock.cs` — integrate event countdown hooks
- `SimulationEngine.cs` — broadcast imminent events (7-day, 1-day, day-of)
- New: `MorningBriefing.gd` — overnight summary UI
- New: `DeliveryScheduler.gd` — calendar + drag-drop

### Design Decisions
1. **Real-time vs game-time countdowns**: Use game-time, but show both (e.g., "3 days (5 min real time)")
2. **Notification stacking**: Max 3 simultaneous notifications, queue rest
3. **Urgency colors**: Green (7+ days) → Yellow (2-3 days) → Red (today)
4. **Skip protection**: Double-confirmation if trying to sleep past harvest day

### Why This Matters for Retention
This creates the "just until tomorrow" loop. Players will stay to harvest that one pen, wait for research to finish, see what weather does. The preview makes them *invested* in the near future.

### Risks
- UI clutter if not designed minimally
- Performance if querying too many systems every frame

### Vision Alignment Checklist
- [x] One more turn syndrome — **directly addresses**
- [x] First 2 hours — teaches planning, consequences
- [x] Easy to start — preview is intuitive
- [x] Visible results — countdowns show progress
- [x] Norwegian authenticity — weather forecasting is critical in aquaculture
- [ ] Real fish farmer use — they live by production calendars

---

## Feature Proposal 2: Living Fjord Events 🌊

### What's Missing
The world feels static. BOARD.md shows we have `NorwegianEvents` with 20 events... but where are the *systemic* emergent events that tie to *player actions*? Where's "neighboring farm had outbreak, now your zone is on watch"? Where's "algal bloom predicted, production down 30% next week"?

VISION.md says: "The Norwegian coast isn't just a backdrop — it's a character." But currently, it's wallpaper.

### What to Build

**Faction-Driven Regional Events**
- Environmental factions track pollution per zone
- "Water quality watch" declared if too many farms push limits
- Player choices affect faction standing → which events trigger
- Chain events: warning → minor incident → crisis if ignored

**Predictable Surprises** (oxymoron intended)
- Seasonal events: algae bloom warnings (predictable), mackerel invasions (random), ice drift blocking feed boats (winter)
- 7-day advance warnings with increasing accuracy
- Preparation vs reaction: prepare for predicted storm → minimize damage
- Ignore warning → full crisis

**Competitor Visible Actions**
- CompetitorAI already exists (T62) — make them VISIBLE
- News feed: "Mowi just harvested PO3 — spot price dropping"
- "Competitor had disease outbreak — your zone lice tolerance tightened"
- Market psychology events: panic selling, shortage rumors

**The "Fjord Tension" Meter**
- New UI: environmental stress visualization per zone
- Fills up from: overstocking, frequent treatments, escapes
- Thresholds trigger: inspections, temporary production halts, worse prices
- Empty meter = "green fjord" bonus (premium prices)

### Files to Modify
- `RegionalEventSystem.cs` — expand NorwegianEvents with chains
- `CompetitorAI.cs` — broadcast actions to event system
- `Zone.cs` — track environmental stress per site
- New: `FjordTensionPanel.gd` — visual meter + zone detail
- New: `NewsFeed.gd` — scroll of competitor/industry events
- `Storyteller.cs` — hook environmental stress into crisis generation

### Design Decisions
1. **Predictability vs randomness**: 70% predictable (season, trends), 30% surprise
2. **Agency preservation**: Player can prevent most crises with preparation
3. **No false choices**: Warnings must be actionable (not just atmospheric)
4. **Visual storytelling**: Events shown as newspaper headlines with images

### Why This Matters for Retention
Static simulations get boring. Living worlds create stories players tell each other. "Remember when that storm hit right as I was harvesting?" This transforms the game from spreadsheet to *adventure*.

### Risks
- Balance: too many events = overwhelming, too few = boring
- Require tuning event frequency per difficulty setting

### Vision Alignment Checklist
- [x] One more turn syndrome — "must check if anything happened overnight"
- [x] First 2 hours — first weather event teaches preparation
- [x] Easy to start — warnings are clear and actionable
- [x] Visible results — news feed, tension meter
- [x] Norwegian authenticity — fjord communities share news, weather is life
- [x] Real fish farmer use — industry gossip, weather watches are daily reality

---

## Feature Proposal 3: Mastery Unlock System 🎯

### What's Missing
Tech tree exists (T21), breeding UI exists (T25), but where's the *player progression*? After 10 hours, I've seen all the content. What keeps me playing for 100?

VISION.md mentions "unlock complexity gradually" but has no mastery system. BOARD.md has achievements — that's completionist, not mastery.

### What to Build

**The Three Mastery Tracks**

*Biologist Track* — Genetics & Health
- Level 1: Basic breeding (survival)
- Level 2: Heritability optimization
- Level 3: Inbreeding management
- Level 4: Disease resistance breeding
- Level 5: Custom strain development (named strains with tradeable genetics)
- Unlock: New traits, faster breeding cycles, disease vaccines

*Business Tycoon Track* — Markets & Empire
- Level 1: Spot market trading
- Level 2: Contract negotiation
- Level 3: Vertical integration (processing)
- Level 4: Brand building (premium markets)
- Level 5: Stock market + M&A
- Unlock: New market tiers, IPO access, acquisition targets

*Fjord Steward Track* — Sustainability & Politics
- Level 1: Basic compliance
- Level 2: Environmental monitoring
- Level 3: Stakeholder management
- Level 4: Industry leadership positions
- Level 5: Regulatory shaping (lobbying)
- Unlock: Green certifications, tax breaks, political favors

**Mastery Integration**
- Each track unlocks new UI panels, contracts, map regions
- Cross-track synergies: High sustainability + premium brand + disease-resistant fish = ultra-premium "Arctic Salmon"™
- Visual progression: Office upgrades from shed to headquarters
- Title progression: "Farm Hand" → "Farm Manager" → "Operations Director" → "CEO" → "Lakskongen"

**The "Challenge Contracts"**
- Unlock at Mastery 3 in each track
- Difficult puzzle-like contracts (e.g., "Deliver 50 tons sashimi-grade with zero lice treatments")
- Optional but prestigious
- Leaderboard for fastest/best completion

### Files to Modify
- New: `MasterySystem.cs` — tracks three progression tracks
- `GameState.cs` — persist mastery levels
- `GameBridge.cs` — expose mastery to UI
- New: `MasteryPanel.gd` — track progress, view unlocks
- `TutorialSystem.cs` — unlock tutorials per mastery level
- `ScenarioManager.cs` — challenge contracts

### Design Decisions
1. **Parallel progression**: Can advance all three simultaneously, but specialization rewarded
2. **No gating**: Mastery unlocks OPTIONAL content, never blocks core progression
3. **Visual celebration**: Big reveals at each level, office visual updates
4. **Replayability**: New game + mastery bonuses

### Why This Matters for Retention
The first 2 hours teach the ropes. Mastery gives 100+ hours of "what's next?" It transforms the game from simulation sandbox to RPG progression. Players can chase different goals on replay.

### Risks
- Content explosion: need unlockable content for every level
- Balance: mastery shouldn't make game trivial

### Vision Alignment Checklist
- [x] One more turn syndrome — "just until I unlock vertical integration"
- [x] First 2 hours — mastery preview shown early, aspirational
- [x] Easy to start — simple decision (what to focus on)
- [x] Visible results — office upgrades, new titles, unlock popups
- [x] Norwegian authenticity — real aquaculture career progression
- [x] Real fish farmer use — learning curve matches real expertise

---

## What Systems Architects Miss (Player Experience Critique)

### Critique 1: "Technically Correct, Emotionally Flat"
The genetics system (T25) is biologically accurate — but is it *fun*? Currently, breeding is an optimization problem. Where's the *delight* of creating your signature strain? 

**What's needed**: Named strains, visual differentiation (slightly different color/markings at high mastery), bragging rights export ("My Lakskongen Superior strain just won the Bergen Fish Show!")

### Critique 2: "Simulate Everything, Feedback Nothing"
The simulation tracks 40+ subsystems (T62-T76)... but does the player FEEL them? Currently, many systems run silently.

**What's needed**: 
- Audio cues: distinct sound for each event type
- Visual storytelling: don't just say "lice count 0.5", show fish scratching on nets
- Haptic feedback: screen shake during storms (on toggle)

### Critique 3: "The 'Why' Gap"
The VISION says "chase premium markets" but why would a player care? Currently, the answer is "higher margins" — that's a spreadsheet answer, not a *game* answer.

**What's needed**: 
- Champollion Gallery: Contracts from famous restaurants ("Maaemo requests 100kg for New Year's Eve")
- Prestige meters: visible brand value, sustainability reputation
- Rivalry: named competitors with personalities, trash talk when they beat you

### Critique 4: "Tutorial Ends, Support Ends"
T49 has a 23-step tutorial... then the player is abandoned. Where's the mid-game guidance?

**What's needed**: 
- Automated advisors that activate when struggling
- "Industry newsletter" with tips based on current game state
- Mastery-based challenges (see Proposal 3)

---

## Summary: The Gap Between Sim & Fun

| System | Technical State | Player Experience Gap |
|--------|----------------|----------------------|
| Genetics | ✅ Deep alleles, inbreeding | ❌ No emotional connection to fish |
| Market | ✅ O-U calibrated prices | ❌ No visible supply chain impact |
| Events | ✅ 20 Norwegian events | ❌ No systemic chain reactions |
| Competitors | ✅ CompetitorAI | ❌ Invisible; no rivalry feeling |
| Tutorial | ✅ 23 steps | ❌ Curve drops to flatline |
| Production | ✅ Full chain | ❌ No "signature product" pride |

**The Core Insight**: We have a 9/10 simulation that plays like a 5/10 game. The missing pieces aren't systems — they're *emotional hooks, predictable surprises, and mastery arcs*.

---

## Recommended Priority

1. **Daily Pulse** — implement first, biggest "one more turn" impact
2. **Mastery System** — implement second, creates 100+ hour curve  
3. **Living Fjord** — implement third, adds emergent storytelling

---

*Proposal by: Gameplay Designer (Kimi)*
*Date: 2026-03-20*
*For: Sprint 12 Planning — Challenge Phase*
