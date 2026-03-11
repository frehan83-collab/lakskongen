# 🐟 LAKSKONGEN — Vision & Architecture Document v2.0
## "From Egg to Export. Build your salmon empire in the fjords of Norway."

---

## CRITICAL MARKET INSIGHT

**Farming Simulator 25 just released a "Highlands Fishing" DLC** (Nov 2025) that adds basic salmon and trout aquaculture. This validates the market BUT it's surface-level — breed, raise, harvest with fishing boats. It's a feature, not a game.

**Our differentiator:** We go 100x deeper. FS25 aquaculture is like a farming game's "plant → harvest" loop. Lakskongen is the entire biological, financial, regulatory, and market ecosystem. We are to salmon farming what iRacing is to Need for Speed.

---

## WHAT MAKES GREAT SIM GAMES GREAT (Research Synthesis)

### From Farming Simulator's Success (6M copies sold, FS25):
1. **Real brands & equipment** — Authenticity creates trust and excitement
2. **Mod support** — 3.2 BILLION mod downloads. The community IS the game's longevity
3. **Production chains** — Raw material → processing → sale. Players love optimization
4. **Seasonal cycles** — Time pressure creates meaningful decisions
5. **Relaxing yet complex** — Easy to start, endless depth
6. **Visual spectacle** — Beautiful environments players want to inhabit

### From Anno 1800 (Production Chain Mastery):
1. **Interconnected systems** — Every production feeds another
2. **Scaling complexity** — Simple at start, overwhelmingly deep at endgame
3. **Multiple regions** — Different resources, different challenges
4. **Population tiers** — Unlock complexity gradually as you grow

### From Schedule I / Game Dev Tycoon (Indie Hits):
1. **Clear feedback loops** — Action → result → reward → reinvest
2. **Easy to pick up, hard to master**
3. **"One more turn" syndrome** — Always something about to complete
4. **Emergent stories** — Systems create unique narratives

### The Psychology of Farming Games:
1. **Continuous non-conflict growth** — Steady progress, not win/lose
2. **Visible results** — See your farm/empire grow visually
3. **Mastery curve** — Skills improve over time, knowledge compounds
4. **Satisfying cycles** — Plant → grow → harvest → sell → reinvest

---

## REVISED GAME PILLARS

### 1. 🧬 DEEP BIOLOGY
Not just "fish grow bigger." Real genetics with alleles, heritability, inbreeding depression. Diseases that spread through water. Lice that adapt to treatments. Fish behavior affected by light, temperature, oxygen, currents. This is Crusader Kings genetics meets aquaculture.

### 2. 🌊 LIVING FJORDS
The Norwegian coast isn't just a backdrop — it's a character. Seasonal light (midnight sun to polar night), tidal currents, temperature layers, algal blooms, storm damage. Each fjord has unique characteristics. Players learn to read the water.

### 3. 📊 MARKET MASTERY
Dynamic global salmon market driven by supply/demand, seasonality, trade events, currency fluctuations. Players trade on a Fish Pool-style exchange. Contract negotiation with processors. Build your brand. Chase premium markets (Japan sashimi grade) or volume markets (EU). Price crashes when Chile has a good year.

### 4. ⚖️ REGULATORY CHESS
Norway's real regulatory framework as gameplay. Traffic light system, MAB limits, lice thresholds, environmental monitoring. Compliance isn't boring — it's a strategic puzzle. Push limits for profit or play safe for sustainability ratings.

### 5. 🔧 TECHNOLOGY EVOLUTION
Start with wooden pens from the 1970s. Research and unlock modern RAS, semi-closed containment, laser delousing, offshore Ocean Farms, AI-driven feeding. Technology isn't just upgrades — it changes how you play.

### 6. 🏗️ EMPIRE BUILDING (Anno-style scaling)
Start with one small site. Grow to a vertically integrated empire: own hatchery, multiple sea sites across zones, processing plants (HOG → fillets → smoked → sashimi), logistics fleet, export contracts. Buy competitors. Go public. Become the next Mowi.

---

## PRODUCTION CHAINS (Anno-inspired)

```
BROODSTOCK
    └→ EGGS
        └→ FRY (needs: feed, freshwater, vaccines)
            └→ SMOLT (needs: light program, grow-out tanks)
                └→ SEA FISH (needs: feed, delousing, monitoring)
                    └→ HARVEST
                        ├→ HOG (gutting line)
                        │   ├→ FRESH WHOLE → Export EU/Asia
                        │   └→ FILLETS (fillet line)
                        │       ├→ FRESH FILLET → Retail/Export
                        │       ├→ SMOKED FILLET (smoking plant)
                        │       │   └→ RETAIL PACKS → Premium markets
                        │       └→ SASHIMI GRADE (ultra-cold chain)
                        │           └→ JAPAN/PREMIUM → Highest margin
                        ├→ BYPRODUCTS
                        │   ├→ FISH OIL → Supplement market
                        │   ├→ FISH MEAL → Feed industry
                        │   └→ FISH SKIN → Leather (novelty)
                        └→ ROE → Caviar market (seasonal premium)

FEED CHAIN:
    FISHMEAL + FISH OIL + SOY + RAPESEED + ASTAXANTHIN + INSECT MEAL
        └→ FEED PELLETS (own feed mill OR buy from supplier)
```

---

## TECH STACK DECISION

### Engine: **Godot 4.3+ with C#**

**Why Godot over Unity:**
- Zero royalties, zero runtime fees — critical for a commercial game
- Open source — can fix engine bugs ourselves, full control
- C# support mature in 4.3+
- Growing ecosystem, post-Unity-pricing-fiasco migration wave
- Perfect for 2D/2.5D isometric sim (our visual target)
- Built-in mod support potential through resource packs

**Why C# over GDScript:**
- Performance for simulation math (100k+ fish populations, genetics)
- Strong typing catches bugs early
- Rich ecosystem (NuGet packages for SQLite, math libraries)
- Familiar to more developers (future team expansion)
- Can use Arch ECS library for batch simulation

**Architecture:**
- **Simulation layer:** Pure C# — no Godot dependencies. Testable, fast. This is where genetics, population dynamics, market simulation, disease models run.
- **Presentation layer:** Godot scenes + GDScript/C# for UI, rendering, input
- **Data layer:** JSON + SQLite for saves, market history, configuration
- **Mod layer:** Resource-based mod system for custom equipment, scenarios, maps

---

## VISUAL DIRECTION

### Style: **Stylized Realism (Anno 1800 / Northgard meets Norwegian Coast)**

NOT photorealistic. NOT pixel art. Clean, atmospheric stylized 3D with:
- Beautiful fjord landscapes with volumetric fog
- Tilt-shift camera for that miniature/diorama feel
- Dynamic weather: rain, snow, northern lights, midnight sun, storms
- Underwater camera views of fish in pens
- Seasonal color palette shifts (green summer → golden autumn → white winter → blue spring)
- Satisfying animations: fish jumping, boats moving, processing lines working
- Night/day cycle with lighthouse beams and village lights

### Camera: **3D isometric with zoom levels**
- **Macro:** Norwegian coast overview — see all your sites
- **Region:** Fjord view — individual site and surroundings
- **Farm:** Close-up of pens, boats, feeding systems
- **Underwater:** Fish behavior, lice, net inspection
- **Interior:** Hatchery tanks, processing plant, office/dashboard

---

## SOUND DESIGN

- Ambient ocean waves, seagulls, wind
- Regional Norwegian folk-inspired soundtrack (Wardruna meets Sigur Rós vibe)
- Dynamic music layers: peaceful exploration → tense crisis → triumphant harvest
- ASMR-satisfying sounds: fish feeding splashes, processing machinery, ice cracking
- Weather sounds: rain on metal roofs, howling winter wind, thunder
- Voice lines: Norwegian-accented English for notifications ("Lice count is rising, boss")

---

## MOD SUPPORT (Farming Simulator's SECRET weapon)

FS25 has 3.2 BILLION mod downloads. Mods keep the game alive for YEARS.

**Our mod system from day one:**
- Custom fish species (trout, cod, halibut — community adds them)
- Custom equipment and buildings
- Custom maps/locations (Scotland, Chile, Canada, Alaska)
- Custom scenarios and campaigns
- Lua scripting for gameplay modifications
- Steam Workshop integration
- Mod validation and version management

---

## MULTIPLAYER (Phase 2-3)

- **Co-op:** Share a company, split responsibilities (one manages sea, one manages processing)
- **Competitive:** Same market — your supply affects prices for everyone
- **Trading:** Buy/sell licenses, contracts, fish between players
- **Leaderboards:** Best EBIT/kg, sustainability score, biggest harvest

---

## GAME FLOW — FIRST 2 HOURS (Critical for retention)

**Minute 0-5:** Cinematic. Drone shot over Norwegian fjords. Music swells. "Your grandfather's farm..." Tutorial NPC introduces you.

**Minute 5-15:** Take over small family farm. 1 license, 2 pens with fish already growing. Learn basics: check fish, feed them, monitor temperature.

**Minute 15-30:** First lice problem. Learn delousing. First small harvest. Sell on spot market. See the money come in. Feel the dopamine.

**Minute 30-60:** Expand. Buy more smolt. Make feed decisions. First disease scare. Unlock hatchery. Start understanding the depth.

**Minute 60-120:** First full production cycle complete. Multi-pen management. Market timing matters. Start researching technology. See the empire vision.

**The hook:** By hour 2, players understand the depth is enormous, they've had wins AND scares, and they can SEE the path from small farmer to industry titan.

---

## REVENUE MODEL

### Base Game: $29.99 / NOK 349
### DLC Roadmap (12-18 month cadence):
1. **"Chilean Waters"** — Chile map, different regulations, new diseases, Pacific market
2. **"The North Sea"** — Scotland, Faroes, offshore focus, oil industry neighbor mechanics
3. **"Future Farm"** — Land-based RAS, gene editing, vertical ocean farms, automation
4. **"The Trading Floor"** — Deep financial mechanics, IPO, stock market, mergers
5. **"Heritage Edition"** — Historical campaign: 1970s-2030s, evolving technology and regulations

### Ongoing Revenue:
- Cosmetic packs (boat skins, building styles, Norwegian regional themes)
- Seasonal events (Christmas market, Easter rush)
- Community marketplace (curated mods)

---

## DEVELOPMENT PHASES (Revised)

### Phase 1: Core Prototype (Months 1-3)
**Goal:** Playable core loop that's fun
- Hatchery → Sea → Harvest → Sell
- Basic genetics (3 traits)
- 1 zone, 1 site, 3 pens
- Simple market with seasonal prices
- Feed management
- Lice mechanic
- Basic 2D/isometric UI
- **PLAYTEST with real fish farmers for feedback**

### Phase 2: Deep Systems (Months 3-6)
- Full genetics (8 traits, breeding, genomic selection)
- Disease system (5 diseases)
- Environment simulation (temperature, O2, currents)
- Traffic light regulatory system
- Technology tree (Tier 1-2)
- Processing chain (HOG + fillets)
- Dynamic market with AI competitors
- Save/load system

### Phase 3: Visual Polish & Content (Months 6-10)
- 3D isometric renderer (fjord maps)
- Full 13-zone Norwegian coast
- All technology tiers
- Full processing chain
- Campaign scenarios (4 starting scenarios)
- Tutorial system
- Sound design and music
- Mod framework

### Phase 4: Early Access Launch (Month 10-12)
- Steam Early Access
- Community feedback integration
- Balance tuning
- Mod tools release
- Localization (NO, EN, DE, FR, ES, JA)

### Phase 5: Full Launch (Month 14-16)
- All scenarios
- Multiplayer (co-op)
- Achievement system
- Steam Trading Cards
- Console port preparation

---

## SUCCESS METRICS

| Metric | Target |
|--------|--------|
| Steam Wishlists (pre-launch) | 50,000+ |
| Early Access sales (month 1) | 10,000+ |
| Full launch sales (year 1) | 50,000+ |
| Steam review score | 80%+ positive |
| Average playtime | 20+ hours |
| Daily active players (month 3) | 2,000+ |
| Mod submissions (year 1) | 500+ |

---

## COMPETITIVE POSITIONING

**"Farming Simulator did it for tractors. We do it for the ocean."**

- FS25 Highlands Fishing DLC = surface-level aquaculture as a side feature
- Lakskongen = the DEFINITIVE salmon farming simulation
- No direct competitor exists in this depth category
- Norwegian salmon = $15B+ industry with zero games dedicated to it
- Cultural pride angle: Norwegians will LOVE this (like FS with German/American farmers)
- Educational value: Universities and industry training potential

---

## TEAM SCALING PLAN

**Phase 1 (Now):** Fredrik (domain expert/producer) + AI-assisted development
**Phase 2:** Add 1-2 developers (C#/Godot), 1 artist
**Phase 3:** Add sound designer, QA, marketing
**Launch:** Core team of 5-8 people

---

*Document created: 2026-03-10*
*Author: Fredrikke (Chief Software Engineer) & Fredrik J. Hansen (Domain Expert & Producer)*
*Version: 2.0 — "The Real Deal"*
