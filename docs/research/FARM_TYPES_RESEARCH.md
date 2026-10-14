# Farm types research dossier (2026-10-14)

Source material for `docs/FARM_TYPES_SPEC.md`, gathered by research agents with Firecrawl and web search. Every figure carries its source and year; the spec cites these as `[L:x]`, `[O:x]` and `[UX-x]`. Real company names appear here as research subjects only; the game uses code names (D17).

## research-land

# Land-based salmon grow-out: research dossier for Lakskongen (sim model + 3D site)

Research cut-off is 2026-10-14. Firecrawl returned HTTP 401 on every call, so all research used WebSearch and WebFetch. Primary PDFs I extracted are in `/private/tmp/claude-501/-Users-fredrik-j-hansen-lakskongen/1a7df52a-cf33-40ac-aaf8-eaf13ebf76df/scratchpad/landres/txt/`:
- `salme_q2_2026.txt`
- `salme_ar2025.txt`
- `se_utslipp.txt`
- `desmi_se.txt`

Values marked **(derived)** are my own arithmetic on sourced inputs. Values marked **(artist est.)** have no published source and need checking against photos. Units: HOG means head-on gutted and LW means live weight. Salmon Evolution's own figures give HOG/LW = 3.5/4.2 ≈ 0.83 ([R1], 2026).

---

## 0. The headline

- **Three different machines are sold under one label, "land-based".** Each has its own physics, costs, failure modes and look:
  1. **Hybrid flow-through (HFS).** Example: Salmon Evolution. An outdoor farm of large round tanks that reuses about 65% of its water.
  2. **Pure flow-through in rock pools.** Examples: Andfjord, Gigante. Square pools blasted below sea level; very cheap on energy, but it needs a rare site.
  3. **RAS halls.** Examples: Atlantic Sapphire, Proximar, Nordic Aqua. Everything sits inside big sheds and reuses 95–99% of its water; these farms can be built next to the market.
- **The core trade against sea pens:**
  - Land-based farms have no sea lice, about 97–99% survival and no lice-driven harvest stops.
  - They need no paid production capacity (MTB). The land permit is fee-free, but slow and discharge-limited.
  - In exchange they carry very high capex, electricity and liquid oxygen as life-critical inputs, single-point failures that kill whole tanks within minutes, a long ramp-up, and harvest weights that so far run low.
- **2026 reality:** the leading Norwegian operator (Salmon Evolution) still loses money at weak prices. Its unit cost is falling as utilisation rises, which is classic fixed-cost leverage. Atlantic Sapphire, the industry's warning story, was taken private in October 2026 after years of mortality events.

---

## 1. Case files (short)

### Salmon Evolution: Indre Harøy, Hustadvika (HFS)

**Phase 1**
- 12 grow-out tanks, Ø28 m, 13 m high, about 8 m of water, about 5,000 m³ each ([R3], 2021; [R5], c.2023). Another account gives 4,800 m³ each and 57,600 m³ in total ([R6], 2024).
- Capacity 7,900 t HOG per year. Capex NOK 1,578m accumulated against a guidance of NOK 1,560–1,590m ([R8], 2023).
- Production timeline:

| Event | Date | Source |
|---|---|---|
| First smolt stocked (about 100k fish, about 300 g, delivered by wellboat and pumped into the tank) | 26 Mar 2022 | [R11], 2022 |
| First harvest (3.75 kg HOG, about 7–8 months at sea level) | Nov 2022 | [R4], 2023 |
| All 12 tanks handed over | Apr 2023 | — |

**Phase 2**
- Final investment decision June 2024; construction began August 2024.
- First two tanks and the intake station handed over April 2026, two more July 2026, the rest during 2026.
- Budget up to NOK 2.65bn, including NOK 400m for 4 pre-grow-out tanks. It adds about 10,100 t, giving about 18,000 t HOG ([R1], 2026).
- Permits cover 36,000 t HOG including Phase 3 ([R1], 2026). The 2019 permit set a maximum standing biomass of 13,300 t and production of 28,800 t ([R14], 2019).

**Water**
- HFS reuses about 65% and takes in about 35% new seawater. Intakes sit at 25 m and 95 m and are blended for temperature. Intake water is filtered and UV-treated.
- Each tank is a separate biological zone ([R1], 2026).

**Biology and economics in 2025–26**

| Metric | Value | Source |
|---|---|---|
| Survival | about 97% | [R2], 2026 |
| Q2 2026 harvest | 945 t | [R1], 2026 |
| Average weight | 3.5 kg HOG (4.2 kg LW) | [R1], 2026 |
| Superior share | 96% | [R1], 2026 |
| Farming cost | NOK 70.7/kg (81.2 a year earlier) | [R1], 2026 |
| Price | NOK 69.7/kg | [R1], 2026 |
| 2026 harvest guidance | cut from 7,000 t to about 6,000 t | [R1], 2026 |

- **New production plan:** stock at about 400 g instead of about 130 g and harvest at about 5.2 kg LW. That shortens the cycle from about 11 to 8–9 months and allows 8 stockings per phase per year instead of 6 ([R1], 2026).
- **Feed-model mistake:** a premature change to the feed-factor model in early 2025 caused high FCR and harvest deviations in Q2–Q3 2025 ([R2], 2026).

**Logistics**
- Harvest is at the Vikenco slaughterhouse about 10 km away, capacity 120,000 t/yr ([R10], 2024). Smolt arrive by wellboat at the quay ([R11], 2022).
- In-house smolt come from Dale, plus an external supply contract ([R1], 2026).

### Andfjord Salmon: Kvalnes, Andøya (flow-through, pools in rock)

**Design**
- Square pools blasted into bedrock, with the floor below sea level. The first pool is about 20 m deep and holds about 1,000 t ([A6], 2019; [A8], 2021).
- Laminar flow pushes water across the pool from side to side. Water is renewed 15–17 times per day, roughly every 90 minutes; the company says this prevents sludge and H₂S ([A1], 2022; [A6], 2019).
- Intake is seasonal: 160 m in winter, 30 m in summer. The screen sits at 40 m, with PE pipes of 1.8 m and 2.2 m inside diameter ([A5], c.2022).
- Water temperature is a stated 7–12 °C year-round with no heating or cooling ([A5]).

**First cycle**
- Stocked June 2022 with 120 g smolt; reached about 1 kg after four months ([A2], 2022).
- Harvested July 2023: average about 3.4 kg, 97.5% survival, 91.1% superior ([A7], 2023).
- Price by size: 2–3 kg fish fetched about NOK 67/kg and 3–4 kg fish about NOK 80/kg ([A7], 2023).
- FCR 1.05 and about 1 kWh/kg ([A3], 2026).

**2026 status**
- Survival 98.04% across pools K0, K1 and K3; accumulated FCR 1.02; standing biomass 1,969 t ([A3], 2026).
- Installed capacity 6,000 t, rising to 11,000 t when K2 and K4 come on line.
- Sold about 450,000 post-smolt (562 t) to a sea farmer ([A3], 2026). This hybrid business model is a gameplay hook.
- Blended capex NOK 115/kg. The budget rose by NOK 500m because of concrete costs, design changes and weather ([A4], 2025).

### Atlantic Sapphire: Homestead, Florida (RAS)

**Facility**
- One building of about 390,000 sq ft, roughly 36,000 m² **(derived)**, where fish are hatched, grown and processed ([B10], n.d.). Fish flow through pipes to on-site processing ([B10b], 2025).
- Saline well water from 1,170 ft and 1,750 ft; disposal through a 2,705 ft injection well ([B11], n.d.).

**What went wrong**

| Date | Event | Source |
|---|---|---|
| Jun 2017 | Danish pilot lost about 250 t to hydrogen sulphide (H₂S) | [B6], 2017 |
| Jul 2020 | Emergency harvest of about 200k fish, blamed on construction stress | [B2], 2020 |
| Q1 2021 | Chiller breakdown; USD 11m spent on temporary chillers and generators | [B3], 2022 |
| Mar 2021 | Drum-filter design flaw sent particles into the biofilters, causing turbidity and possibly gas; about 500 t lost in one of six systems. Fix: split each system in half | [B1], 2021 |
| Aug 2021 | COVID-era liquid oxygen (LOX) shortage; feeding paused and 100k fish harvested under weight | [B4], 2021 |
| 2nd half 2022 | Rising mortality forced early harvests at about 2 kg HOG | [B12], 2022 |
| Feb 2025 | Overstocking left the system unable to feed the fish; a cull finished. The CEO said the focus had been on the machinery, not the fish | [B5], 2025 |

**Now**
- Survival 97–98% ([B5], 2025). Q1 2026: 1,504 t HOG at USD 9.32/kg ([B8], 2026).
- Bridge-loan rescue ([B9], 2026). Coral HoldCo took full ownership and the company applied to delist in October 2026 ([B9b], 2026).

### Other cases

**Proximar: Oyama, Japan (RAS)**
- Uses groundwater near Mount Fuji and is less than 2 hours by truck from Tokyo ([P3], 2021).
- 2025: 1,338 t HOG ([P1], 2026).
- Q2 2026 ([P2], 2026):
  - 726 t harvested at an average 2.56 kg HOG
  - 98.9% grow-out survival
  - average price NOK 64/kg
- Turbidity restricted feeding (cause still under investigation).
- Planned electrical maintenance stopped feeding for 3.5 days ([P1], 2026).

**Nordic Aqua Partners: Ningbo, China (RAS)**
- Stage 1+2 runs at 8,000 t/yr; the full build targets 20,000 t HOG.
- Q2 2026: 1,362 t at 4.4 kg HOG (5.3 kg LW), 96% superior ([N1], 2026).
- Long-term weight target about 7 kg LW for China's high-end market ([N2], 2026).

**Gigante Salmon: Rødøy (flow-through in rock basins)**
- Three basins (A, B, C). Trial harvest 694 t at 3.8 kg, 91% superior ([G1], 2025).
- First commercial harvest: 3.0 kg HOG, 94% superior, economic FCR 1.04 ([G2], 2026).
- Q1 2026: 930 t with only about 76% superior ([G3], 2026).
- Completion certificate 24 March 2026; target 16,000 t by 2028 ([G3]).
- Its sister project on Verholmen (19,087 t/yr) was **refused** by the county governor in Nordland. Reasons: untreated dissolved nitrogen equal to the sewage of 190,000 people, noise affecting red-listed birds, and encroachment on untouched nature ([G4], 2024).

**Fredrikstad Seafoods (Nordic Aquafarms, RAS)**
- Capacity 1,000–1,500 t, but produced 770 t in 2021 and lost NOK 61.5m.
- The building foundations failed and had to be rebuilt; the contractor was later ordered to pay NOK 60.9m. Cleaning the freshwater supply proved harder than expected.
- The company switched to yellowtail kingfish (about NOK 150/kg) ([F1], 2022).

**Nekst (Florø)** is a 20,000 t RAS plan on paper since 2017 and was never built ([K1], 2017–2018). It is a cautionary tale of "announced capacity".

**Post-smolt RAS (the hybrid strategy)**
- Grieg's Tytlandsvik produces 750 g post-smolt (3,000 t/yr) at a stated 4–5 kWh/kg ([T1], n.d.).
- Nofima: on-growing past 100 g on land only pays once lice costs are counted ([T2], n.d.).

---

## 2. Facts table for the sim model

| Parameter | Value / range | Unit | Source | Year |
|---|---|---|---|---|
| **Tank: HFS grow-out** | Ø28 × 13 m high, ~8 m water, 4,800–5,000 | m³ | [R3], [R5], [R6] | 2021–24 |
| Tanks per phase (Salmon Evolution) | 12 grow-out + 2 pre-grow-out | count | [R1], [R5] | 2026 |
| Phase 2+3 tanks (Salmon Evolution) | 48 in total | count | [R5] | c.2023 |
| **Pool: rock flow-through** | ~20 deep; ~1,000 t each | m; t | [A8], [A6] | 2019–21 |
| Rock pool volume | ~25,000–28,600 **(derived: 1,000 t ÷ 35–40 kg/m³)** | m³ | [A2] | 2022 |
| **Max density** | HFS 7 (min) to 85 (max); flow-through 35–40 at harvest; RAS post-smolt kept < 80 | kg/m³ | [R2]; [A2]; [S1] | 2026; 2022; 2023 |
| Density effect on growth | Growth (SGR) falls from ≥ 50; stress overload at 125 | kg/m³ | [S2] | 2017 |
| Sea-pen legal maximum, for contrast | 25 | kg/m³ | akvakulturdriftsforskriften § 46, lovdata.no/dokument/SF/forskrift/2008-06-17-822 | 2008 |
| **Production index** | HFS nominal 160 (revised 131–140); early RAS 120–140 | kg/m³/yr | [R6] | 2024 |
| **Water reuse** | Flow-through 0%; HFS ~65%; RAS 95–99% (code default 0.97) | % | [A5]; [R1] | 2022; 2026 |
| Water exchange, flow-through | 15–17 | changes/day | [A1] | 2022 |
| Water exchange, HFS | Each tank "replaced ~every 4 h"; P2+P3 intake 16,000 L/s, >50,000 L/s circulating at peak | h; L/s | [R5] | c.2023 |
| Specific flow, flow-through | ~0.3 **(derived)** | L/kg/min | [A1], [A2] | 2022 |
| Pumps, HFS Phase 1 | 110 centrifugal pumps, mostly 7,000–40,000 L/min each | count; L/min | [R5] | c.2023 |
| **Intake depth / temperature** | Salmon Evolution 25 m + 95 m blended; rearing target 12–14 °C (an outside commentary puts sub-thermocline water at ~10 °C) | m; °C | [R1], [R6], [R7] | 2024–26 |
| | Andfjord 160 m (winter) / 30–40 m (summer); 7–12 °C | m; °C | [A5], [A1] | 2022 |
| Temperature ceiling (maturation) | Keep ≤ 12 °C; going from 12 to 16 °C stimulates male maturation | °C | [S1], [S8] | 2023–24 |
| **Growth / cycle** | Salmon Evolution 130 g → harvest in ~11 months (company: ~16 months at sea); new plan 400 g → 5.2 kg LW in 8–9 months | months | [R2], [R1] | 2026 |
| | Andfjord 120 g → 1 kg in 4 months; harvest at 13 months, 3.4 kg | months | [A2], [A7] | 2022–23 |
| | Freshwater RAS (Freshwater Institute) 4–5 kg at 23–26 months post-hatch | months | [S9] | n.d. |
| Implied TGC (×1000) | Salmon Evolution 2.8–3.3 at 12–14 °C **(derived)**; Andfjord ≈ 3.3 at an assumed 12 °C **(derived)**. For comparison, the game's existing sea calibration is 2.8–3.2 | — | [R1], [R2], [A2] | 2022–26 |
| **Survival per cycle** | HFS ~97% (planned mortality 3–5%) | % | [R2] | 2026 |
| | Flow-through 97.5–98.04% | % | [A7], [A3] | 2023–26 |
| | RAS 97–99.7% (Proximar quarterly grow-out 98.9–99.7%; Atlantic Sapphire 97–98% after its crises) | % | [P1], [P2], [B5] | 2025–26 |
| Sea-pen mortality, Norway | 14.2% (2025), 15.4% (2024), 16.7% (2023) | % | [V1] | 2026 |
| **Harvest weight (actual)** | Salmon Evolution 3.5 HOG; Andfjord 3.4; Gigante 3.0–3.8; Proximar 2.1–2.6 HOG; Nordic Aqua 4.4 HOG; Atlantic Sapphire 2.4 | kg | [R1], [A7], [G1], [G2], [P1], [P2], [N1], [B5] | 2023–26 |
| Superior share | 76–99% (Gigante Q1 2026 76%; Proximar 98–99%) | % | [G3], [P2] | 2026 |
| **FCR** | Flow-through / HFS 1.02–1.1; RAS 1.1–1.3; sea pens ~1.3 (as quoted by the same source) | kg feed/kg | [A3], [G2], [R7], [S9] | 2024–26 |
| **Energy** | Flow-through ~1 | kWh/kg | [A1], [A3] | 2022–26 |
| | HFS 6.3–6.8 kWh/kg LW (~60,000 MWh/yr for Phase 1); Rabobank says "up to 8" | kWh/kg | [R2]; [R7] | 2024–26 |
| | RAS grow-out 6–10; "typically 4–5" in Rabobank's framing; post-smolt 3.4–5.4 simulated, 9.59 in another simulation | kWh/kg | [S3], [S4], [R7] | 2022–24 |
| Electricity price | Power component 66.2 øre/kWh (household, 2025) plus grid and tax; NOK 0.66–1.0/kWh is a sensible game range | NOK/kWh | [E1] SSB | 2026 |
| Energy cost | HFS ≈ NOK 4–7/kg LW **(derived)**; flow-through ≈ NOK 0.7–1 **(derived)** | NOK/kg | above | — |
| **Oxygen** | Salmon Evolution 0.55 kg O₂ per kg LW growth (printed as "kg/tonnes", which is almost certainly kg/kg) | kg O₂/kg | [R2] | 2026 |
| | Generic feed oxygen demand ~1.05 kg O₂ per kg feed (includes waste decay) | kg O₂/kg feed | [S5] | n.d. |
| LOX price example | ~50,000 ISK/t in Iceland, about NOK 4/kg **(derived conversion)**, so roughly NOK 2/kg LW **(derived)** | NOK/kg | [S6] | n.d. |
| **CO₂ limit** | < 15 mg/L in brackish water (growth already hit at 12); ~20 mg/L tolerated in freshwater RAS with high alkalinity | mg/L | [S1] | 2023 |
| **Sludge** | 0.15–0.20 kg dry matter per kg feed; Salmon Evolution dewaters to 20–25% solids and sends it to biogas | kg DM/kg feed; % | [S7], [R2] | n.d.; 2026 |
| **Capex** | Salmon Evolution P1 ≈ NOK 200k per tonne HOG capacity **(derived, 2019–23 prices)**; P2 ≈ NOK 262k/t **(derived)** | NOK/t capacity | [R8], [R1] | 2023, 2026 |
| | Industry-wide: actual builds > USD 30k/t against early assumptions of USD 15k/t | USD/t | [C1] | 2025 |
| | Andfjord NOK 115/kg "blended" (denominator not defined) | NOK/kg | [A4] | 2025 |
| Sea MTB auction, for contrast (land pays none) | NOK 195k (2018), 220k (2020), 154k (2022) per tonne MTB | NOK/t MTB | [L6] | 2025 |
| **Production cost** | Salmon Evolution farming EBITDA cost 70.7 (Q2 2026), 77.1 (2025); targets 60–65 by mid-2027 and ~53 at scale | NOK/kg HOG | [R1], [R9] | 2026 |
| | Depreciation per kg at Salmon Evolution ≈ NOK 21 at 945 t, so all-in ≈ NOK 91/kg HOG **(derived)** | NOK/kg HOG | [R1] | 2026 |
| | Sea average 2023: NOK 64.68/kg round weight, ≈ NOK 78/kg HOG **(derived)** | NOK/kg | [C2] | 2024 |
| | Model study 2018: land 43.60 vs sea 30.60 | NOK/kg | [C3] | 2018 |
| **Build time** | Salmon Evolution P2: decision Jun 2024 → first tanks Apr 2026 (~22 months) → complete end 2026 (~30 months) → run-rate mid-2027 | months | [R1], [R2] | 2026 |
| | Andfjord: site work 2020 → first harvest Jul 2023; Phase 2 delayed, contractor changed Dec 2025 | years | [A5], [A7], [A10] | 2022–25 |
| **Staffing** | Salmon Evolution group 89 FTE (2025, includes smolt site and HQ); parent company 33 | FTE | [R2] | 2026 |
| **Price** | Salmon Evolution first harvest about NOK 3/kg above the Nasdaq salmon index | NOK/kg | [R4] | 2023 |
| | Size penalty: Andfjord 2–3 kg at NOK 67 vs 3–4 kg at NOK 80; Proximar 3 kg+ fish NOK ~104 net vs average 61 | NOK/kg | [A7], [P1] | 2023–26 |

---

## 3. Norwegian permits (what land-based actually needs)

1. **Aquaculture permit for land**, under laksetildelingsforskriften § 7-2.
   - It is **fee-free**: there is no MTB to buy at auction.
   - The permit still caps biomass and production; Salmon Evolution's is 13,300 t standing biomass and 28,800 t per year ([R14], 2019).
   - Applications were **frozen from 20 Dec 2022 to 1 Jul 2025**, about 31 months.
   - The July 2025 rules tighten the boundary between land and sea, so that fee-free permits do not go to plants that in practice affect other farms like open pens ([L1], [L2], [L3], 2025).
2. **Mandatory disinfection of intake water** for new land plants, all species and life stages (from 2025). For freshwater it applies only where the source has migrating salmon or another disease risk. Existing plants are exempt unless they make a "material change" ([L2], 2025).
3. **Discharge permit** under the Pollution Control Act, from the county governor (Statsforvalteren).
   - This is the real capacity limit, covering particles, nitrogen and organic load. Effective filtration is a condition of operation.
   - In 2024 inspections, **68 of 77** land sites had violations; about three-quarters had weak discharge monitoring ([L4], 2025).
   - A refusal is possible, as at Gigante Verholmen ([G4], 2024).
4. **Biosecurity approval from Mattilsynet** (the food safety authority), covering water-treatment plans ([L5], n.d.).
5. **Municipal zoning plan and building permits.**
   - Indre Harøy's detailed zoning plan (planID 201905) covers about 864.5 daa. The industrial pad must be blasted down to a level of +3.60 m above sea ([R13], 2024).
6. **A separate permit for works in the sea**, for intake pipes, blasting and temporary fills.
   - Salmon Evolution Phase 2 (stage BTB): 7,030 m³ of temporary fill and 5,076 m³ blasted underwater.
   - Fee NOK 44,300. Works were restricted to protect salmon migration and a cod spawning ground 600 m away ([R12], 2024).
7. **The escape regulation for land plants** (technical standard, in force since 2018). "Escape-free" is a design requirement, not automatic ([L7], 2018). Salmon Evolution says "minimal" escape risk ([R2], 2026).

---

## 4. Failure modes (cause → early signal → mitigation)

| # | Failure | Cause | Signals | Speed / size | Mitigation | Source |
|---|---|---|---|---|---|---|
| 1 | **Power loss** | Grid outage; generator fails to start | Pumps stop, dissolved oxygen falls, fish crowd at the bottom | Oxygen below 5 mg/L in ~40 min (40 kg/m³, 12 °C), or **~15 min** with stressed fish; "all dead in about half an hour" | Generators that start automatically (23 s at Bakkafrost); emergency oxygen diffusers; two grid feeds | [F2] 2022; [F3] n.d. |
| 2 | **Oxygen supply failure** | Valve fault; LOX delivery gap | Oxygen alarm; oxygen-dosing flow drops | Inchmore: ~500k smolts, damage done in the first hour; Atlantic Sapphire 2021: 100k fish harvested under weight | Several LOX suppliers; on-site oxygen generator; reserve tank | [F4] 2018; [B4] 2021 |
| 3 | **CO₂ build-up** | Weak stripping or degassing at high density | Appetite falls, growth slows, CO₂ above 12–15 mg/L | Chronic: lost growth | Strippers; density limits; alkalinity control | [S1] 2023 |
| 4 | **H₂S poisoning** | Sludge going anaerobic (seawater has ~2,700 mg/L sulphate vs ~2 in freshwater) | Sudden behaviour change; a nitrate drop has been suggested as a warning sign but is unproven | Acute critical level 1.78 µM, about 60 µg/L **(derived)**; Atlantic Sapphire Denmark lost ~250 t | No dead zones; cleaning; H₂S sensors; high-exchange flow-through | [F5] 2018; [F6] 2023; [B6] 2017 |
| 5 | **Turbidity / particle overload** | Filter design flaw; maintenance mistake | Cloudy water; fish sit on the bottom | Atlantic Sapphire Mar 2021: ~500 t in one system; Proximar 2026: feeding restricted | Better centre drains; split systems; maintenance procedures | [B1] 2021; [P2] 2026 |
| 6 | **Disease spread in shared water** | One recirculation loop shared by many tanks | Mortality rises in several tanks at once | Whole system | One tank per biological zone (Salmon Evolution); half-size systems (Atlantic Sapphire) | [R1] 2026; [B1] |
| 7 | **Early maturation** | 24-hour light, warm water (14–16 °C), energy-rich feed | Colour change, slower growth, more downgrades | Up to ~80% of males in closed containment | All-female eggs; shorter day length; ≤ 12 °C | [S10] n.d.; [S8] 2024 |
| 8 | **Off-flavour (geosmin/MIB)** | Microbes in biofilters | Taste panel or fillet test | Product can't be sold until purged | Purge (depuration) 6–10 days without feed in clean water | [S11] n.d. |
| 9 | **Nitrate build-up** (RAS) | Biofilter end product | Rising NO₃ | 100–250 mg/L NO₃-N showed no clear growth or welfare effect | Denitrification; it matters more for the discharge permit | [S12] 2017; [S13] 2024 |
| 10 | **Cooling failure** (warm climates) | Chiller breakdown | Temperature rises | Atlantic Sapphire: USD 11m in emergency rental | Redundant chillers; cold deep intake | [B3] 2022 |
| 11 | **Intake water upset** | Well pump fault; salinity swings | Appetite falls | ~75 t of growth lost at Atlantic Sapphire | Redundant intakes and blending | [B5] 2025 |
| 12 | **Construction / commissioning stress** | Blasting and noise near live fish | Stress, mortality | ~200k emergency harvest (Atlantic Sapphire 2020) | Phased commissioning; half-filled first stockings (Salmon Evolution) | [B2] 2020; [R1] |
| 13 | **Overstocking / feed-model error** | Pushing utilisation; changing the model too early | FCR rises; harvest goes off plan | Atlantic Sapphire cull 2025; Salmon Evolution high FCR in 2025 | Conservative plans; stock larger smolt | [B5]; [R2] |
| 14 | **Discharge breach** | Weak filtration or monitoring | Inspection findings | Fines; permit risk | Drum filters, sludge dewatering, biogas | [L4] 2025 |
| 15 | **Capex overrun / bad contractor** | Concrete costs, weather, foundations | Budget drift | Andfjord +NOK 500m; Fredrikstad rebuilt its foundations | Contingency; fixed-price contracts | [A4] 2025; [F1] |

---

## 5. 3D anatomy (for the Blender artist)

**Archetype A: HFS tank farm.** This is the hero archetype and matches owner refs LAND 1 and LAND 2. LAND 1's ~70 tanks in two sizes fit Salmon Evolution's full Indre Harøy build-out: 12 + 48 grow-out tanks plus pre-grow-out tanks ([R5]).

- **Ground:**
  - A flat, blasted granite pad at +3.6 m above sea ([R13], 2024), on the site of a former quarry (Viken Pukkverk) ([R2]).
  - Rough rock cut faces and heath around the edge, with skerries offshore.
  - Colours: pad granite #6B7378 and wet rock #3E4448 (D13 tokens).
- **Grow-out tanks:**
  - Ø28 m, 13 m tall shell, ~8 m of water ([R3], 2021). Two rows of 6 per phase **(artist est.)**, with 3–5 m between shells **(artist est.)**.
  - Walls are two-tone in the owner's photo, grey over green or white; LAND 1 shows pale grey. The tanks were assembled on site from elements; the material is not published.
  - Lids are flat, white or light grey (Salmon Evolution photo) or dark (LAND 1). Lids control light, which matters for maturation ([S8]), and keep birds out.
  - Suggested colours: walls #C9CED1 with band #5E7A6A; lids #EDEFEF or #2A3036 **(artist est.)**.
- **Pre-grow-out / smolt tanks:** 2 per phase ([R1]). Suggested Ø14–18 m and 8–10 m tall **(artist est.)**.
- **Process halls:**
  - Long, low halls between the tank rows, housing pumps (110 in Phase 1), drum filters, CO₂ and nitrogen strippers, UV units, heat pumps and heat exchangers ([R5]; [R2]).
  - Six heat pumps in Phase 2 ([R15], 2024).
  - White or grey cladding with roof vents, as in LAND 1. Suggested 15–25 m wide and 8–12 m tall **(artist est.)**.
- **Intake pump stations** at the shoreline. Pipes run underwater to 25 m and 95 m depth, with two more intakes in Phase 2 ([R10], [R12]). LAND 1's curved basin is consistent with the pump-station and outfall works.
- **Wellboat quay** with a **fish-transfer pipe bridge**.
  - The bridge is an orange steel truss on legs running from the quay to the tanks.
  - Smolt are pumped from the wellboat into the tanks ([R11], 2022).
  - Suggested section 3–4 m square, legs every 20–30 m, colour #D9622B **(artist est.)**.
- **Harvest building:**
  - Salmon Evolution harvests off site at Vikenco ([R10]). LAND 1 and Atlantic Sapphire show an on-site hall.
  - Large dark-grey box, #3A4046 **(artist est.)**, about 60 × 120 m and 15 m tall **(artist est.)**.
- **Oxygen yard:** vertical vacuum-insulated LOX tanks. The supplier is Nippon Gases ([R2]). Suggested Ø3–3.5 m and 8–11 m tall each, white **(artist est.)**.
- **Backup generators:** containerised gensets, in 20 ft or 40 ft containers **(artist est.)**.
- **Substation:** small and orange (LAND 1). Power comes via neighbouring heavy industry ([R10]).
- **Sludge station:** dewatering plant and a container bay for trucks to the biogas plant ([R2]).
- **Feed silos** and a road with parking.

**Archetype B: rock-pool flow-through (Andfjord / Gigante)**
- Square pools blasted into bedrock, with the floor below sea level, about 20 m deep and ~1,000 t each ([A6], [A8]). Two rows of six pits ([A10], 2023).
- Pool volume ~25,000–28,600 m³ **(derived)**. Concrete walls are made in an on-site element factory ([A9], n.d.).
- Laminar inflow along one wall and outflow along the opposite one.
- Intake through a ~1 km tunnel entering at ~50 m depth ([A9]); intake screen at 40 m, pipes with 1.8 m and 2.2 m inside diameter ([A5]).
- The look: dark, flat water surfaces set into grey rock, close to the shore. This contrasts strongly with Archetype A.

**Archetype C: RAS hall (Atlantic Sapphire / Proximar / Nordic Aqua)**
- One huge insulated building of about 36,000 m² ([B10], **derived**) holding the hatchery, grow-out and processing.
- Outside: LOX tanks, gensets, chiller and cooling plant, wells and trucks.
- This archetype suits a "near-market" overseas expansion (Japan, China, USA) rather than the Norwegian coast.

---

## 6. What makes it interesting as gameplay (real trade-offs)

1. **How you pay for capacity: an MTB auction or concrete.**
   - Sea growth costs NOK 154k–220k per tonne MTB at auction ([L6]).
   - Land needs no MTB but costs ~NOK 200k–260k+ per tonne of capacity, plus 2–3 years of building, plus ramp-up ([R8], [R1], [C1]).
   - Unit cost on land is very sensitive to utilisation. Salmon Evolution's depreciation alone is about NOK 21/kg at low volume ([R1]).
   - The player chooses debt versus licence money, and speed versus certainty.
2. **Choose a technology for your site:**
   - Flow-through: ~1 kWh/kg, but it needs a rare cold, deep intake close to shore.
   - HFS: 6.3–6.8 kWh/kg with a stable temperature.
   - RAS: 6–10 kWh/kg, but it can sit beside the market (Tokyo, Shanghai, USA).
   - Every coastal plot has hidden attributes: depth to the 95 m water, temperature profile, grid capacity and nearby protected nature.
3. **The reuse slider**, Salmon Evolution's "sweet spot" of 65%:
   - More reuse means less pumping and heating.
   - It also means more filtration, more complexity and more H₂S, particle and turbidity risk ([R1]).
4. **Intake blending:** mixing 25 m and 95 m water sets the temperature, and therefore growth. Above about 12 °C, maturation and downgrade risk climb ([S1], [S8]).
5. **Stocking strategy:**
   - Stocking at 400 g instead of 130 g gives 8 cycles a year instead of 6, lower peak densities and a smaller harvest (5.2 kg LW). That buys lower risk at the cost of weight ([R1]).
   - The density cap (85 kg/m³) and the point where growth falls (≥ 50) make a real optimisation puzzle ([R2], [S2]).
6. **Price tension:** land farms are lice-free with 96–99% superior grade, but harvest weights are often under 3–4 kg, and fish under 3 kg take a big price discount ([A7], [P1]).
7. **Life-support supply chains:** power and LOX are hard dependencies.
   - Investments: auto-start generators, an oxygen generator, a second grid feed.
   - Events: grid outage (a 15–40 minute countdown), LOX shortage (Atlantic Sapphire 2021), chiller failure in warm-climate expansions.
   - This suits a calm-by-default, rare-but-sharp alarm style.
8. **Isolation versus efficiency:** one tank per biological zone (Salmon Evolution) costs more but contains outbreaks. Shared loops are cheaper, but one failure can take out a sixth of the farm (Atlantic Sapphire).
9. **Permits as a strategic layer:**
   - The land permit is fee-free, but the discharge permit is the true ceiling, and refusal is possible (Verholmen).
   - Inspections are frequent (68 of 77 sites had findings).
   - Moratoria and rule changes are possible (the 2022–25 freeze, the 2025 disinfection rule).
   - Building intake pipes has seasonal windows set by salmon migration and spawning grounds.
10. **Joining up the value chain:** a land plant can sell post-smolt to sea farms (Andfjord 2026), or feed your own sea sites with large smolt to shorten time at sea and lice exposure. This is the bridge between the existing sea game and the new farm type.
11. **Learning curve and staff:** operators improve over time (Salmon Evolution's cost fell from NOK 81 to 71/kg year on year), and the Atlantic Sapphire lesson is "fish over machinery". This could be a staff-competence stat that lowers incident probability.
12. **Waste becomes value:** sludge at 0.15–0.2 kg dry matter per kg feed can go to biogas or fertiliser. It is a discharge-compliance upgrade with a small revenue line.

**Calibration gaps in the current code** (`/Users/fredrik.j.hansen/lakskongen/src/LakskongenSim/Farm/RASFacility.cs`):
- `TankVolumeM3` is 3,000; the real value is 4,800–5,000.
- `FCR` is 1.30; the real range is 1.02–1.1 for flow-through/HFS and 1.1–1.3 for RAS.
- `RecirculationRate` is 0.97, which is only right for RAS; HFS is 0.65 and flow-through 0.
- `DailyEnergyKWh` is 50,000, which gives 18 GWh/yr. At 8,000 t that is about 2 kWh/kg, far below HFS (~60 GWh / 6.3–6.8 kWh/kg) and RAS (6–10).
- The oxygen baseline of 4 g O₂ per kg fish per hour is about 10× too high. Better: ~0.55 kg O₂ per kg growth.
- `GetCAPEX` Medium is NOK 1.2bn for 8,000 t; the real figure is ~NOK 1.6bn (2023) to ~2.1bn (2026 prices).
- `EmployeeCount` is 35; Salmon Evolution has 33 in the parent company and 89 in the group.
- `DailyWaterCost` (NOK 5/m³) does not apply to seawater; the cost is pumping energy.
- `SeaLiceRisk` and `EscapeRisk` are hard-coded to zero. Lice should stay at about zero, but escape should be small, not zero (2018 regulation), with a new risk if intake disinfection fails.

---

## 7. Sources (key → URL, year)

**Salmon Evolution**
- R1: storage.mfn.se/b5ae3f0e-9535-4da7-864f-5465e7ab4667/salme-q2-2026-report.pdf (2026)
- R2: storage.mfn.se/899895ea-e824-48d2-96d4-541bad97d6df/salme-2025-annual-report.pdf (2026)
- R3: fishfarmingexpert.com/article/thinking-big-tanks-take-shape-at-salmon-evolution-site/ (2021)
- R4: fishfarmingexpert.com/salmon-evolution/salmon-evolution-achieved-average-gutted-weight-of-375-kg-in-first-harvest/1486693 (2023)
- R5: desmi.com/media/akqdqcvs/salmon-evolution-norway.pdf (c.2023)
- R6: salmonbusiness.com/land-based-salmon-showdown-do-flow-through-systems-really-offer-better-growth-rates-than-ras (2024)
- R7: salmonbusiness.com/why-hybrid-flow-through-systems-are-no-panacea-for-salmon-farming (2024)
- R8: live.euronext.com/en/products/equities/company-news/2023-02-08-salmon-evolution-asa-results-fourth-quarter-2022 (2023)
- R9: salmonbusiness.com/salmon-evolution-targets-nok-53-kg-farming-cost-as-production-doubles/ (2026)
- R10: ilaks.no/du-kan-godt-si-at-anlegget-vart-er-en-slags-bronnbat-pa-land-med-foring/ (2024)
- R11: ilaks.no/lordag-satte-salmon-evolution-ut-sin-forste-smolt-i-tankene-pa-indre-haroy/ (2022)
- R12: inte.statsforvalteren.no/contentassets/9ae3227dab2a4387900eb701a7a3b3b3/salmon-evolution-norway---vedtak.pdf (2024)
- R13: hustadvika.kommune.no/_f/p26/i9090fb88-df05-4e8a-bf02-693812bb15ec/201905_b2_planbestemmelser.pdf (2024; seen via search summary)
- R14: ilaks.no/salmon-evolution-kjoper-tomt-for-utbygging/ (2019)
- R15: fishfarmermagazine.com/2024/06/27/salmon-evolution-signs-phase-two-construction-deal/ (2024)

**Andfjord Salmon**
- A1: fishfarmingexpert.com/andfjord-salmon/salmon-growing-well-in-andfjords-low-energy-on-land-farm/1460836 (2022)
- A2: fishfarmingexpert.com/andfjord-salmon-norway-salmon-farming/fish-growing-faster-than-forecast-in-land-based-farm/1447513 (2022)
- A3: mfn.se/nir/a/andfjord-salmon-group/andfjord-salmon-healthy-biological-performance-and-growth-returning-to-revenue-generation-q2-h1-2026-financial-results-ce522d30 (2026)
- A4: salmonbusiness.com/concrete-costs-and-weather-delays-push-land-based-farmers-capex-per-kilo-to-nok-115/ (2025)
- A5: uponor.com/en-en/r/fish-farming-with-the-smallest-possible-footprint (c.2022)
- A6: fishfarmingexpert.com/andfjord-flow-through-lofoten/on-land-salmon-farmer-valued-at-nok-1-billion/1326300 (2019)
- A7: weareaquaculture.com/news/37980 (2023)
- A8: fishfocus.co.uk/first-pool-at-andfjord-salmon-filled/ (2021)
- A9: bairdmaritime.com/fishing/aquaculture/andfjord-salmon-completes-inlet-waterway-for-land-based-salmon-farm (n.d.)
- A10: ilaks.no/?p=192789 (2023); placera.se/pressmeddelanden/andfjord-salmon-group-andfjord-salmon-new-contractors-engaged-at-kvalnes-20251211 (2025)

**Atlantic Sapphire**
- B1: fishfarmingexpert.com/article/atlantic-sapphire-blames-ras-design-weakness-for-us-fish-die-off (2021)
- B2: fishfarmingexpert.com/atlantic-sapphire-bluehouse-construction-work/construction-stress-confirmed-as-cause-of-atlantic-sapphire-emergency/1194583 (2020)
- B3: seafoodsource.com/news/premium/business-finance/atlantic-sapphire-doubles-revenue-posts-usd-132-million-in-losses-in-2021 (2022)
- B4: fishfarmingexpert.com/article/covid-driven-oxygen-shortage-forces-atlantic-sapphire-to-harvest-fish-early/ (2021)
- B5: salmonbusiness.com/atlantic-sapphires-costly-lessons-can-they-make-miami-work/ (2025)
- B6: ilaks.no/atlantic-sapphire-har-funnet-arsaken-til-massedod/ (2017)
- B8: inderes.dk/en/releases/atlantic-sapphire-q1-2026-operational-update (2026)
- B9: undercurrentnews.com/2026/03/30/investors-bail-out-atlantic-sapphire-as-10m-bridge-loan-signed-buys-time/ (2026)
- B9b: salmonbusiness.com (Coral HoldCo full ownership and delisting headlines, 13–14 Oct 2026)
- B10: bisnow.com/news/south-florida/industrial/salmon-miami-real-estate-industrial-86564 (n.d.)
- B10b: wlrn.org/business/2025-02-24/how-an-enormous-fish-farm-in-homestead-hopes-to-change-the-seafood-industry (2025)
- B11: fishfarmingexpert.com/article/atlantic-sapphire-digs-deep-into-past-for-water (n.d.)
- B12: seafoodsource.com/news/business-finance/atlantic-sapphire-share-price-plummets-after-release-of-revised-revenue-expectations (2022)

**Proximar, Nordic Aqua, Gigante, Fredrikstad, Nekst, post-smolt**
- P1: mfn.se/all/a/proximar-seafood/proximar-seafood-production-update-biomass-at-1-933-tonnes-and-harvested-339-tonnes-hog-in-q4-2025-af4cf6ca (2026)
- P2: bairdmaritime.com/fishing/aquaculture/proximar-seafood-salmon-harvest-rises-in-second-quarter-of-2026 (2026)
- P3: fishfarmingexpert.com/article/proximar-laying-firm-foundations-in-japan/ (2021)
- N1: salmonbusiness.com/nordic-aqua-partners-q2-harvest-hits-1362-tonnes/ (2026)
- N2: weareaquaculture.com/news/finance/nordic-aqua-partners-met-its-harvest-guidance-for-2025 (2026)
- G1: salmonbusiness.com/land-based-farmers-first-harvest-yields-694-tonnes-with-91-superior-share/ (2025)
- G2: salmonbusiness.com/gigante-salmon-records-94-superior-share-in-initial-harvest/ (2026)
- G3: mfn.se/ob/a/gigante-salmon/gigante-salmon-as-publication-of-q1-report-d2ed4d80 and inderes.dk/en/releases/gigante-salmon-publication-annual-report-2025-from-developer-to-fish-farmer (2026)
- G4: fishfarmingexpert.com/fish-farming-international-gigante-havbruk/concerns-about-discharges-and-noise-sink-plan-for-gigantes-second-salmon-farm/1754631 (2024)
- F1: salmonbusiness.com/salmon-on-land-fredrikstad-seafoods-lost-e6-2-million-last-year/ (2022); seafoodsource.com/news/aquaculture/gr-kj-r-dispute-settled-but-fredrikstad-seafoods-considering-switch-to-yellowtail-at-ra-ras (2023)
- K1: ilaks.no/planlegger-oppforing-av-verdens-storste-landbaserte-oppdrettsanlegg-i-floro/ (2017)
- T1: akvagroup.com/land-based/turn-key-delivery/case-stories/tytlandsvik/post-smolt-tytlandsvik-aqua-norway-1 (n.d.)
- T2: salmonbusiness.com/post-smolt-initiatives-are-a-very-safe-bet-will-drive-growth-for-farmers/ (n.d.)

**Science**
- S1: nofima.com/wp-content/uploads/sites/2/2023/10/83-86.pdf (2023)
- S2: nofima.com/publication/1396679/ (2017)
- S3: ntnu.no/bridge/node/22667 (c.2022)
- S4: Ayuso Virgili et al., Aquacultural Engineering vol. 101 (2023)
- S5: globalseafood.org/advocate/oxygen-demand-aquaculture-feed/ (n.d.)
- S6: worldfishing.net/aquaculture/aquaculture-in-iceland-unlocking-growth-with-affordable-self-generated-oxygen/1500910.article (n.d.)
- S7: conferences.aquaenviro.co.uk/proceedings/will-dried-sludge-fish-farming-become-contributor-competitor-utilization-municipal-sludge/rohold-lars-scanship (n.d.)
- S8: frontiersin.org/articles/10.3389/faquc.2024.1323818/full (2024)
- S9: was.org/Meeting/Program/PaperDetail/156601 (n.d.)
- S10: portal.nifa.usda.gov/web/crisprojectpages/1028389-production-of-all-female-atlantic-salmon-without-using-steroids.html (n.d.)
- S11: globalseafood.org/advocate/off-flavors-in-salmonids-raised-in-ras/ (n.d.)
- S12: usgs.gov/publications/investigating-influence-nitrate-nitrogen-post-smolt-atlantic-salmon-salmo-salar (2017)
- S13: salmonbusiness.com/is-your-salmon-safe-what-new-study-says-about-nitrate-levels-in-aquaculture/ (2024)

**Failure events**
- F2: fishfarmingexpert.com/biomarine-closed-containment-emergency-oxygen/buying-time-if-things-go-wrong-in-a-ras/1191347 (2022)
- F3: deif.com/cases/land-power/backup-power-system-provides-salmon-producer-power-in-seconds/ (n.d.)
- F4: fishfarmingexpert.com/fish-deaths-inchmore-marine-harvest/oxygen-fault-killed-500000-smolts-at-new-hatchery-reveals-marine-harvest/1332223 (2018)
- F5: fishfarmingexpert.com/hydrogen-sulphide-niva-norway/scientists-seek-to-get-measure-of-ras-gas-problem/1358851 (2018)
- F6: fishfarmingexpert.com/hydrogen-sulphide-recirculating-aquaculture-systems-technical-university-of-denmark/salmon-tolerance-to-hydrogen-sulphide-is-lower-than-previously-thought/1496253 (2023)

**Costs and statistics**
- C1: undercurrentnews.com/2025/12/08/land-based-salmon-capex-reality-diverges-sharply-from-early-assumptions/ (2025)
- C2: ilaks.no/?p=214481 (2024, Fiskeridirektoratet 2023 data)
- C3: snf.no/media/c2pd5pyp/r07_18_-2.pdf (2018)
- V1: vetinst.no/rapporter-og-publikasjoner/rapporter/2026/fiskehelserapporten-2025 (2026)
- E1: ssb.no/energi-og-industri/energi/statistikk/elektrisitetspriser (2026)

**Regulation**
- L1: salmonbusiness.com/revised-rules-reopen-pathway-for-land-based-salmon-farming-applications/ (2025)
- L2: ilaks.no/apner-for-soknader-om-fiskeoppdrett-pa-land-igjen/ (2025)
- L3: regjeringen.no/no/dokumenter/horing---forslag-til-regelverksendringer-for-regulering-av-landbasert-oppdrett/id2466720/ (2024–25)
- L4: salmonbusiness.com/nine-in-ten-land-based-fish-farms-in-breach-of-environmental-rules-says-regulator/ (2025)
- L5: salmonbusiness.com/regulator-u-turn-clears-path-for-40000-ton-land-based-salmon-farm (n.d.)
- L6: regjeringen.no/contentassets/b52278489b664ef98a8304fa65065db0/kapasitetsjustering-av-tillatelser-til-akvakultur-med-matfisk-i-sjo-av-laks-orret-og-regnbueorret-i-2026-horingsnotat.pdf (2025–26)
- L7: Fiskeridirektoratet consultation on the technical-standard regulation for land-based fish farms, in force 2018-01-01 (fiskeridir.no)

**Gaps I could not close:**
- Andfjord pool length and width (only depth and tonnage are published).
- Salmon Evolution tank wall material.
- Norwegian LOX price.
- A published per-kg energy figure for Proximar or Nordic Aqua.
- Exact Phase 2 grow-out tank count; it was planned to mirror Phase 1's 12.

## research-offshore

# Offshore, exposed and closed/semi-closed salmon farming: research dossier for Lakskongen

Prepared 2026-10-14. This was a read-only research phase: no repo edits, builds or commits.

**How the research was done:**
- Firecrawl returned HTTP 401 on every call, so I used WebSearch and WebFetch instead.
- Where possible I downloaded the primary documents and extracted their text: Fiskeridirektoratet development-licence reports, conversion decisions, the Menon 2023 study and an NTNU thesis. The texts are in `/private/tmp/claude-501/-Users-fredrik-j-hansen-lakskongen/1a7df52a-cf33-40ac-aaf8-eaf13ebf76df/scratchpad/offshore/txt/`.
- Reference-image colours were median-sampled with `/private/tmp/claude-501/-Users-fredrik-j-hansen-lakskongen/1a7df52a-cf33-40ac-aaf8-eaf13ebf76df/scratchpad/offshore/sample2.py`.
- Each number carries a reference key such as [R4]. The full URL and year for each key are in §11.
- "Derived" means my own arithmetic on sourced numbers. "Via search summary" means the figure came from a search-engine digest of that page, not a direct read of the page.

---

## 1. Key takeaways for the sim and the art

1. **What the reference images show:**
   - Images 1 and 2 show SalMar's **Ocean Farm 1 (OF1)**.
   - Image 3 is a photo of the real OF1 in Frohavet.
   - The computed brief calls OF1 "8-sided", but the structural model in an NTNU thesis has **12 side columns and 1 central column** [R6]. The thesis gives a circumference of 341.6 m against a diameter of 110 m. That matches a **regular 12-sided polygon** with a 55 m circumradius and **28.5 m sides** (derived).
2. **Real offshore units are only "semi-offshore" so far.**
   - OF1's 100-year design sea state is only **Hs 5 m** [R6]. One review gives 5.5 m for the site [R15].
   - Arctic Offshore Farming (AOF) was approved for **Hs 6.7 m** [R19].
   - The truly open-sea design, SalMar's Smart Fish Farm, is designed for a 100-year storm, quoted as **waves up to 31 m**. That is roughly Hs 15 m, which is my inference [R12][R29][R30].
   - The existing `OffshoreFarm.cs` says "OF1 rated for 12 m waves". That is wrong (see §8).
3. **The binding constraint is access, not survival.**
   - AOF had **106 cage-inaccessible occasions** and **92 barge-inaccessible days** over January 2022 to January 2024. **62% of those barge days fell in January to March** [R17].
   - Mattilsynet withdrew AOF's site approval from 2026-01-01. One stated reason was that the site was **inaccessible for periods of up to 3 months** [R18].
   - OF1, by contrast, lost **zero feeding days to weather** [R7].
   - Weather should therefore lock out **vessel operations** (boarding, crowding, wellboat work, crew change, resupply). It should not lock out feeding.
4. **The biological upside is real and documented.**
   - OF1 needed **no delousing in either of its first two cycles** [R7][R11].
   - OF1's mortality was about **½ of the PO6 average for the 2017 year-class and about ⅓ for 2019** [R7].
   - AOF needed **no delousing at the site** and had **3.6% mortality** [R17].
   - The national sea-phase mortality average was **14.2% in 2025** [R55].
5. **The big failures are operational and organisational, not "a wave broke it".**
   - OF1 tilted in 2018 because of open hatches and manholes plus a leaking ballast pipe. The alarm system had been switched off because it produced too many false alarms from more than **20,000 measuring points**. About **15,805 fish escaped** [R3][R10].
   - OF1's harvest-zone design caused crush injuries. That raised total mortality from **4% to 7%** [R3].
   - The Havfarm's first cycle lost about **19%** (about 25% in some cages) to winter sores, gills and lice [R26].
   - AOF had net tears in **November 2024** and boarding injuries [R17][R18].
6. **Regulation is a game in itself.**
   - Development licences were free, then converted to ordinary licences for about **NOK 11 million per licence** [R4].
   - The new offshore regime, outside the production areas, uses **area competitions**, **25-year licences**, and a rule that turns **1 tonne of moved coastal capacity into 2 tonnes offshore** [R35][R36].
   - As of the last confirmed step, no area competition had been held. The royal resolution of 2026-02-27 fixed the three areas, and the competence-requirement consultation ran from June to August 2026 [R33][R34].
7. **Closed and semi-closed pens in the sea are post-smolt tools, not grow-out tools.**
   - Mowi keeps fish in fresh water or closed cages until they reach **at least 700 g** [R49].
   - The cost is energy: about **3.87 kWh/kg in a closed pen against 0.07 kWh/kg in an open pen**, from one operator [R50]. Grid capacity is the limit.

---

## 2. Case files

### 2.1 SalMar Ocean Farm 1 ("Havmerden"): Håbranden, Frohavet, Frøya

**Timeline:**
- Design study began in 2012.
- Eight development licences were granted on 2016-02-26.
- Construction started in March 2016 at Wuchang/CSIC in Qingdao, and the unit was delivered in June 2017 [R1][R4][R5].
- It arrived on 2017-09-05 [R33].
- DNV issued the site certificate on 2017-09-27 and fish went in the same day [R2].
- Generation 1 was emptied on **2019-01-20**. Generation 2 was stocked from **2019-08-22** [R4].
- The licences were converted on 2020-07-07 [R4].
- DNV class "OI Offshore fish farming installation POSMOOR" followed in January 2020 [R5].
- The unit spent about a year at the yard for improvements in 2022–23 (Maccaferri case study, via search summary [R60]).
- SalMar's Q2 2025 report called its biology "very good". OF1 and AOF together harvested about **1,200 t per quarter** in Q1 and Q2 2025 [R18].

**Generation 1:**
- **1,038,439 smolt at 233 g** went in between 2017-09-27 and 2017-10-07 [R3].
- Feeding was surface feeding for the first 4 weeks, then subsea feeding only. The usual feeding depth was **6–7 m** [R3].
- Biological FCR was about **1.02** at 6 months [R2]. Growth beat SalMar's ST100 curve [R3].
- Condition factor was **1.24**, giving a lean, "wild-like" shape [R3].
- The size standard deviation fell from 23% to 18% [R3].
- Monthly mortality averaged **0.28%** over the first 12 months [R3].
- Total mortality was **7%**. It had been **4%** before the harvest-zone crush problem [R3].
- Cleaner fish were stocked at 10% lumpfish and 2.5% wrasse [R3].
- Adult female lice were 0.06 in November 2017 against a zone average of 0.08 [R14], and **0.01 in week 28 of 2018** [R8].
- Two cycles produced about **10,000 t** in total [R12]. Average weight was **5.39 kg** after one year [R11].

**Staffing:**
- About **3–4 people on board** in normal operation, with **remote operation from shore** possible [R1][R13].
- The rota is **3 shifts, 9 days on and 12 off, with a 2-day overlap** [R3].
- The plan was for **12 employees** in the rotation after the pilot (Kongsberg/SalMar 2016, via search summary [R13]).
- Management followed ISM-code practice, as on ships [R3].

**Cost:** The application estimated **NOK 690 million**. The final cost at start-up was **NOK 720 million**, and upgrades added **NOK 157 million**, for **NOK 877 million in total** [R4]. SalMar later spoke of "about NOK 1 billion" for design and development [R30].

### 2.2 Arctic Offshore Farming (AOF): Fellesholmen, Tromsø

**History:**
- Norway Royal Salmon developed the concept, which passed to SalMar Aker Ocean and then to SalMar.
- It received **7.68 development licences totalling 5,990 t MTB** on 2018-03-18 [R17].
- Installation took place in September–November 2021, and the site certificate came on 2021-12-10 [R17].

**Design:**
- Two semi-submersible steel cages plus one barge. Each cage has a **subsea hose** carrying feed, power, communications, air and dead fish [R17].
- Each cage has a **79–80 m diameter** and a steel ring **22 m** high [R20][R21].
- The upper pontoon has a **2 × 3 m** profile and the lower pontoon **6 × 4 m**. Columns are **1.5 m** in diameter [R21].
- Nets are double (inner and outer). In submerged operation the top of the net sits **about 10–11 m below the surface** [R17][R21].
- Four **air pockets** run along the inside of the lower pontoon so the fish can fill their swim bladders [R17].
- Design Hs was 5–15 m [R21]. The approved Hs is **6.7 m**, which the company thinks could rise to 10–12 m with reinforcement [R19].

**Generation 1 (stocked 2023-07-17, harvested December 2023 to January 2024) [R17]:**
- **1.2 million fish at about 2 kg**, moved in from another sea site.
- Total mortality **3.61%**: cage A 4.67%, cage B 2.55%.
- Harvest **4,897 t live weight** at **4.4 kg** live weight on average.
- Biological FCR **1.22**, economic FCR **1.30**, VF3 3.0, superior share 62%.
- Wounds rose after the siphonophore *Apolemia uvaria* ("perlesnormanet") appeared in November 2023.
- No delousing was needed at the site.
- Hs averaged **0.9 m**, with a maximum of **4.15 m** in December 2023. Surface current was **4–28 cm/s**.

**Operability [R17]:**
- The cages were **lowered whenever Hs exceeded 3 m**.
- Cage B was raised 5 times and spent 10 days raised, **7%** of the production period.
- Cage A was raised 6 times, **14.3%** of the period.
- In the production phase the barge was inaccessible on 19 days and the cages on 23 days. On 6 of those 23 days crew could board but vessels could not moor.
- There were over 250 boardings per year, and 4 boarding deviations in 2023, 2 of them injuries.

**Later events:**
- Net tears were found on 2024-11-19 [R18].
- Mattilsynet withdrew the site approval, effective 2026-01-01 [R18].
- The licences were still **converted on 2026-03-13**: 7 × 796 t plus 1 × 540 t, totalling **6,112 t**, for a fee of **NOK 130,320,784** [R19].

### 2.3 Nordlaks Havfarm 1 "Jostein Albert": Ytre Hadseløya, Vesterålen

**Hull and site:**
- The hull is **385 m long and 59.5 m wide**, purpose-built at CIMC Raffles in Yantai. It is not a converted ship [R23][R24].
- The moulded depth is **37.75 m** per Nordlaks [R23]. A towing approval gives "D 56.57 m" and a load weight of **37,500 t** [R24]. Another report says 33,000 t (via search summary).
- The site is about **5 km offshore**, described as the most exposed salmon farm in operation in Norway [R23].

**Pens:**
- **6 nets, each 47 × 47 m**, up to **56 m deep** and about **69,000 m³**, made of HDPE with copper [R23][R25].
- MTB is **10,000 t**. The 13 converted licences are 788 t each, about **10,244 t** [R22].
- The maximum density is **25 kg/m³** [R23].

**Mooring:**
- A turret in the bow lets the unit **weathervane** [R23].
- **11 anchors of 22 t**, each holding **300–450 t**, sit about **650 m** from the centre point [R23].
- The unit moves within a **450 m** swing radius [R23].
- It runs on shore power, with backup generators and flashing lights on its outer edges [R23].
- A closed dead-fish (silage) system is emptied by silage vessels, and feed and silage move ship-to-ship [R28].

**Biology:**
- **Generation 1:** **2.33 million fish** stocked from 2020-07-25 to 2020-09-06; the last fish was harvested on 2021-06-08 [R22]. Mortality was about **19%**, and **about 25% in cages 3–6**, from winter sores, gills and lice treated with teflubenzuron [R26]. Conversion was **refused on 2021-06-25** [R22].
- **Generation 2:** stocked 2021-07-13 to 2021-08-09 at **1.35–2.9 kg** [R22]. Mortality was **2.78%** at 2022-01-14 (range 1.11–4.64%). Lice peaked at **0.49 adult females** in week 51 [R22]. Conversion was **granted on 2022-03-11** [R22].
- **2025:** an algae bloom and ISA (ILA) at Nordlaks' other sites left **no fish to stock**. The Havfarm stayed empty for a generation, losses were estimated at **NOK 300 million**, and restocking was planned for summer 2026 [R27].

**Cost:** about **NOK 1 billion** for the unit and **NOK 800 million for two wellboats** [R26].

**Havfarm 2 (the movable version):** it received 8 licences in 2017 [R22]. I found no confirmation of current status.

### 2.4 SalMar Smart Fish Farm, Ocean Farm 2, and SalMar Ocean

**Smart Fish Farm (SFF):**
- **164 m wide, 106 m high** [R7]. A fact box adds an operating draft of **61 m**, freeboard of **20 m** and a production volume of **760,000 m³** [R12].
- About **3 million fish**, **23,000 t round weight** per year [R12][R30].
- Cost **NOK 1.5 billion** [R29]. The directorate's figure was NOK 1,458.7 million (iLaks, via search summary [R32]).
- **8 development licences (6,240 t)** [R30].
- The site in the Norwegian Sea, about **45 nm outside the baseline**, was cleared on 2023-09-25 [R33][R58]. It is described as **50 nm off Frøya**, with water at **7.5–13 °C** [R7].
- Work was **paused** in autumn 2023 because of regulatory uncertainty [R38].

**Ocean Farm 2:**
- **120 m in diameter and 70 m high**, holding **3–4 million fish**, with **MTB of 6,500–8,750 t** [R31].
- Final site clearance came on **2025-01-28**, at Frøya, 16 km north of OF1 [R31].
- SalMar has since signalled growth abroad, with a pilot zone 3–12 nm east of Shetland in 2025 (via search summary).

**SalMar Ocean:** became a wholly owned SalMar subsidiary in 2025. Its 2026 guidance is about **5,000 t gutted weight (GWT)** (SeafoodSource, via search summary; the page returned 403 [R62]).

### 2.5 Abroad (brief)

- **China, Deep Blue 1:** an octagonal steel cage with columns reaching **30 m** down, **50,000 m³**, about **300,000 salmon** [R59].
- **China, Guoxin 1:** a ship about **248 m** long with **15 tanks**, about **90,000 m³**, aiming for **3,700 t a year** [R59].
- **Chile, Ocean Arks:** a ship-based concept for sites with waves **up to 7 m** [R37].

### 2.6 Closed and semi-closed pens in the sea

| System | Type | Key specs | Results | Source |
|---|---|---|---|---|
| **Marine Donut** (Bluegreen; SalMar runs it at Seterneset, Molde) | Closed torus | **22,000 m³**, up to **50 kg/m³** | Test 1 (2024): **195,000 fish at about 3 kg**, about 101 days, about **1,000 t** harvested, **0.8% mortality**, no lice treatment, a 4° tilt during emptying. Cycle 2: 2025-05-04 to 2025-07-06 | [R46] |
| **FishGLOBE V5** | Closed, post-smolt | **3,500–3,600 m³**, **250 t**, **75 kg/m³** design density, intake **≥15 m** deep | 200,000 post-smolt: mortality **1.4%** in the globe against **4.9%** in net pens. Some eye, snout and fin damage, and signs of osmotic stress | [R47] |
| **Egget** (Hauge Aqua; Mowi licences sold on) | Closed egg | Mini version **21 m high × 15 m wide**, **1,850 m³** | 6 licences | via search summary; [R43] |
| **Preline/Pipefarm** (Lerøy) | Semi-closed horizontal pipe, laminar flow | **2,000 m³**, fish from 100 g to 600–800 g | **23% fewer lice**, **FCR down 6.7%**, mortality down 3.5% against reference pens | [R48] |
| **Aquatraz G3** (Midt-Norsk Havbruk) | Semi-closed steel | Lice skirt deepened from **8 to 13 m** | 2021: mortality **2.08%** against **10.97%** in the control pen, harvest weight **5.36** against **4.83 kg**, production cost **36.47** against **38.78 NOK/kg**, insurance **0.53** against **0.21 NOK/kg**. Serial-unit capex **NOK 50 million** | [R44] |
| **Mowi fleet** | Closed | **170,000 m³** of closed capacity, about **320,000 m³** with 11 cages by 2027. Norway's whole closed capacity equals about 24 normal pens | Post-smolt kept to **≥700 g** | [R49] |

**Energy:** Sulefisk reports **3.87 kWh/kg in its closed pen against 0.07 kWh/kg in open pens**. Sludge leaves its hatchery at **10–12% dry matter** because there is not enough power for drying [R50].

**Licence counts and status [R43]:**

| Concept | Licences | Status |
|---|---|---|
| Atlantis/Nautilus | 1 | More than 120 sites |
| Tubenet | 6 | |
| Akvafuture | 2 | 3 sites |
| Stadionbassenget | 3 | |
| Ecomerden | – | Status uncertain |
| Ocean Globe | – | Could not be verified |

**Lice barriers in open pens:**
- **Snorkel cage:** **75%** fewer newly settled lice and **43%** fewer treated cages in a full-cycle commercial test [R51]. Earlier trials gave 24–65% [R52].
- **Lice skirt:** **18%** average reduction (6–28%) on a single cage; **54%** average (0–80%) when every cage on the site has one [R53].
- **Submersible pen (Atlantis/Nautilus):** **60% fewer weeks** of treatment, with the pen submerged to about **30 m** [R54].
- **Risks of submerging:** swim-bladder problems and the "nakkeknekk" neck deformity, low oxygen, and a sudden air loss that could affect much of the population [R43].

---

## 3. Facts table (value, range, unit, source, year)

### 3.1 Structures

| Item | Value | Range / alternatives | Unit | Source | Year |
|---|---|---|---|---|---|
| OF1 diameter | 110 | – | m | [R1][R5] | 2016/2020 |
| OF1 overall height | 68 | 67 [R6], 69 [R5], 70 (press) | m | [R1] | 2016 |
| OF1 volume | 250,000 | 245,000 [R6] | m³ | [R1][R5] | 2016/2020 |
| OF1 operating draft | 43 | – | m | [R6] | 2018 |
| OF1 steel/structure weight | 5,600 | 7,700 (Bloomberg, basis unclear) | t | [R6][R61] | 2018 |
| OF1 plan shape | 12 side columns + 1 centre column; 6 side pontoons on alternate columns | – | – | [R6] | 2018 |
| OF1 circumference / side length | 341.6 / 28.5 (derived) | – | m | [R6] | 2018 |
| OF1 column diameters | 3.56 (centre and pontoon columns) / 2.8 (plain columns) | – | m | [R6] | 2018 |
| OF1 mooring | 8 catenary lines in 4 pairs; each about 1,000 m of 88 mm chain plus 100 m of 160 mm fibre; anchors about 1,100 m from the centre | – | – | [R6] | 2018 |
| OF1 water depth | 150 (site); designed for 100–300 | – | m | [R6][R1] | 2018/2016 |
| OF1 compartments | 3 (1 sliding and 2 fixed bulkheads) | – | – | [R1][R6] | 2016 |
| OF1 fish capacity | about 1.5 million | 1.6 million | fish | [R8] | 2018 |
| OF1 MTB | 6,240 (8 × 780) | – | t | [R4] | 2020 |
| AOF cage diameter | 79 | 80 (as built) | m | [R21][R20] | 2018/2021 |
| AOF cage steel height | 22 | 78.5 including net [R15] | m | [R20] | 2021 |
| AOF capacity per cage | 3,000 | – | t | [R21] | 2018 |
| AOF approved Hs | 6.7 | design 5–15; possibly 10–12 if reinforced | m | [R19][R21] | 2026/2018 |
| Havfarm length × beam | 385 × 59.5 | 430 × 54 in [R15] (wrong) | m | [R23][R24] | 2020+ |
| Havfarm depth | 37.75 | 56.57 ("D", towing approval) | m | [R23][R24] | 2020 |
| Havfarm pens | 6 × (47 × 47 m, up to 56 m deep, about 69,000 m³) | – | – | [R23][R25] | 2019+ |
| Havfarm anchors | 11 × 22 t, 300–450 t holding, about 650 m out; swing radius 450 m | – | – | [R23] | 2022+ |
| SFF size | 164 wide × 106 high, draft 61, freeboard 20 | 160 m diameter (2018) | m | [R7][R12] | 2022/2023 |
| SFF production volume | 760,000 | – | m³ | [R12][R30] | 2021/2023 |
| OF2 size | 120 diameter × 70 high | – | m | [R31] | 2025 |
| OF2 fish / MTB | 3–4 million / 6,500–8,750 | – | fish / t | [R31] | 2025 |
| Maximum stocking density (Norway) | 25 | – | kg/m³ | [R23] | 2022+ |
| Check: OF1 and Havfarm MTB ÷ volume | 25.0 and 24.2 | – | kg/m³ | derived | – |

### 3.2 Environment and exposure

| Item | Value | Range | Unit | Source | Year |
|---|---|---|---|---|---|
| OF1 100-year design Hs | 5 (Tz 11 s) | 5.5 [R15] | m | [R6] | 2018 |
| OF1 100-year current | 0.75 | – | m/s | [R6] | 2018 |
| OF1 model tests | Hs 1.5, 3, 4, 5; maximum single waves about 10 m | – | m | [R16][R6] | 2014–18 |
| SFF design waves | 31 (maximum wave) | 15 [Fish Farmer]; my inference: Hs about 15 | m | [R29][R12] | 2018/2023 |
| Havfarm wave rating | "up to 15" (review) | not confirmed by Nordlaks | m | [R15] | 2024 |
| 5% most exposed fjord sites in Norway | Hs (1-year) 1.5–2.2 | – | m | [R56] | 2017 |
| AOF site Hs, generation 1 | mean 0.9, maximum 4.15 | – | m | [R17] | 2024 |
| AOF surface current | 4–28 | – | cm/s | [R17] | 2024 |
| SFF site temperature | 7.5–13 | – | °C | [R7] | 2022 |
| Offshore areas' distance from baseline | 20–80 | – | nm | [R37] | 2023 |
| Production-area outer limit (traffic-light zone) | 1–8 | earlier 20/30 | nm | [R37] / search summary | 2023 |

### 3.3 Biology and performance

| Item | Value | Range | Unit | Source | Year |
|---|---|---|---|---|---|
| National sea-phase mortality 2025 | 14.2 | 15.4 (2024), 16.7 (2023); by area 9.4 (PO12–13) to 20.1 (PO4) | % | [R55] | 2026 |
| OF1 generation 1 total mortality | 7 | 4 before the harvest-zone fault | % | [R3] | 2019 |
| OF1 mortality against PO6 | about ½ (2017 class), about ⅓ (2019 class) | – | × | [R7] | 2022 |
| OF1 delousing treatments | 0 | across 2 cycles | count | [R7][R11] | 2022/2023 |
| OF1 lice | 0.01–0.06 | zone 0.08 | adult females/fish | [R8][R14] | 2017–18 |
| OF1 FCR | about 1.02 | at 6 months | – | [R2] | 2018 |
| OF1 condition factor | 1.24 | – | K | [R3] | 2019 |
| OF1 weight after 1 year | 5.39 | – | kg | [R11] | 2023 |
| OF1 production | about 10,000 | over 2 cycles | t | [R12] | 2023 |
| OF1 lost feeding days to weather | 0 | 11 starvation days before harvest | days | [R7] | 2022 |
| AOF generation 1 mortality | 3.61 | 2.55–4.67 | % | [R17] | 2024 |
| AOF generation 1 harvest | 4,897 live / 4,211 gutted | – | t | [R17] | 2024 |
| AOF FCR | 1.22 biological / 1.30 economic | – | – | [R17] | 2024 |
| Havfarm generation 1 mortality | about 19 | about 25 in cages 3–6 | % | [R26] | 2021 |
| Havfarm generation 2 mortality (to 2022-01-14) | 2.78 | 1.11–4.64 | % | [R22] | 2022 |
| Havfarm generation 2 lice peak | 0.49 | – | adult females/fish | [R22] | 2022 |
| Fish moved to offshore units | 1.35–2.9 (Havfarm); about 2 (AOF) | 0.233 (OF1 generation 1) | kg | [R22][R17][R3] | 2017–24 |

### 3.4 Economics

| Item | Value | Range | Unit | Source | Year |
|---|---|---|---|---|---|
| OF1 capex | 877 (690 estimate, 720 at start-up, +157 upgrades) | about 1,000 including development | NOK million | [R4][R30] | 2020/2021 |
| Havfarm capex | about 1,000 | – | NOK million | [R26] | 2021 |
| Havfarm wellboats (2) | 800 | – | NOK million | [R26] | 2021 |
| SFF capex | 1,500 | 1,458.7 | NOK million | [R29][R32] | 2018 |
| Capex per tonne of MTB | OF1 about 140k; Havfarm about 98k | – | NOK/t MTB | derived | – |
| Menon offshore unit, pilot | 3,000 for 20,000 t/yr; 2nd generation 2,000 | – | NOK million | [R37] | 2023 |
| Menon offshore opex | 56 (pilot) / 51 (2nd generation) | coastal 2021 base about 43 (derived: 56 ÷ 1.3) | NOK/kg | [R37] | 2023 |
| Offshore labour cost per kg | about 0.5 × coastal | – | × | [R37] | 2023 |
| Coastal average cost, 2023 | 64.68 | – | NOK/kg | [R57] | 2024 |
| Offshore cost, rough academic estimate | 31.65 (older cost base) | – | NOK/kg | [R64] | ? |
| Insurance: semi-closed against open | 0.53 / 0.21 | – | NOK/kg | [R44] | 2021 |
| Specialised offshore wellboat | up to 1,000 | upgrades: "tens of millions" | NOK million | [R37] | 2023 |
| Development-licence conversion fee | 10 (CPI-indexed): 11.046 in 2020; about 16.3 average in 2026 (derived: 130.3 ÷ 8) | – | NOK million per licence | [R4][R19] | 2020/2026 |

### 3.5 Operations and logistics

| Item | Value | Range | Unit | Source | Year |
|---|---|---|---|---|---|
| OF1 crew on board | 3–4 | – | people | [R1][R13] | 2016 |
| OF1 rota | 9 on / 12 off, 3 shifts, 2-day overlap | – | days | [R3] | 2019 |
| OF1 alarm points | >20,000 | – | points | [R3] | 2019 |
| Wellboat operating limit offshore | about 5 | – | m Hs | [R37] | 2023 |
| Crowding fish advised only below | 2 | – | m Hs | [R37] (citing Heskestad) | 2023 |
| Transport in 3–5.5 m waves | higher stress and mortality | – | – | [R37] (citing Iversen 2005) | 2023 |
| Largest wellboat hold | about 1,000 | against 20,000 t units | t fish | [R37] | 2023 |
| AOF lowering trigger | Hs >3 | – | m | [R17] | 2024 |
| AOF barge inaccessible | 92 days in about 25 months (about 12%); about 32% of Q1 days (derived) | – | days | [R17] | 2024 |
| AOF production-phase inaccessibility | barge 19 days (about 10%), cages 23 days (about 12%) | – | days | [R17] | 2024 |
| Longest inaccessible spell (AOF) | up to 3 months | – | – | [R18] | 2025 |
| New Zealand exposed vessels | operable in swell ≤1.5 m about **72%** of the year | – | – | [R15] | 2024 |
| Havfarm silage traffic | 2 silage vessels made 11 calls in about 8 weeks | – | – | [R28] | 2021 |

---

## 4. Regulation summary (Norway)

**Development licences ("utviklingstillatelser"):**
- Applications were open from **2015-11-20 to 2017-11-17** [R33]. There were **104 applications** (TU 2017, via search summary [R42]).
- **21 projects** received commitments for **102 licences**, about **78,000 t MTB** ([R41], via search summary). A Finance Ministry bill gives about 88,919 t committed (iLaks, via search summary). I give 78–89k t as the range.
- The licences are **free for up to 15 years**, and are for production technology only [R40].
- Each licence is normally **780 t**. Each project has milestone criteria plus a measurement programme with half-yearly reports and a final report [R4][R17].
- **Conversion** to an ordinary food-fish licence requires all criteria to be met (laksetildelingsforskriften §23c). The fee is **NOK 10 million, CPI-indexed**, paid within 90 days. Applicants have no legal right to conversion [R4].
- Conversion is not automatic. The **Havfarm was refused once** because it had too few valid cycles [R22]. **AOF was converted although its site had been withdrawn** [R19].

**Ordinary licences and the traffic-light system:**
- There are 13 production areas (PO).
- Every second year each area is coloured by lice impact on wild salmon. **Green allows up to +6%** capacity (sold partly at a fixed price and partly by auction). **Yellow means no change. Red means −6%** [R39].
- In 2024, 6 areas were green, 5 yellow and 2 red (PO3 and PO4), a cut of about **11,400 t** [R39].
- The production areas' outer limits were pulled in to **1–8 nm from the baseline** [R37].

**Offshore regime ("havbruk til havs"; laksetildelingsforskriften chapter 4, in force 2022-11-07, last amended 2026-01-14) [R35][R36]:**
- It applies **outside the production areas**, so it sits outside the traffic-light system.
- **Steps:**
  1. The King in Council opens areas.
  2. An overarching impact assessment is made.
  3. The ministry divides the areas into tender zones.
  4. A **competition** is held, either an auction or a fixed price with qualification, with minimum competence requirements.
  5. The winner gets an **area commitment ("områdetilsagn")**. It is non-transferable and lapses after 2 years unless a site application is made.
  6. The winner prepares an **area plan** and a **project-specific impact assessment** at its own cost.
  7. A licence is granted for each site and facility.
- Licences last **up to 25 years**. Any fee is set for each round.
- **No pooling** with other licences: no shared biomass ceiling, co-location or joint operation.
- **Capacity moved from coastal licences counts double offshore (1 t becomes 2 t), and cannot be moved back.**

**Areas:**
- The directorate recommended **Norskerenna sør, Frøyabanken nord and Trænabanken** on 2022-07-12.
- DNV's impact assessment came on 2024-10-22.
- The government decided to open the areas on 2025-09-02. A royal resolution fixed their outer limits on **2026-02-27**, excluding Forsvaret's Klepp exercise area and two particularly valuable and vulnerable (SVO) areas.
- A call for input on competence requirements ran from 2026-06-29 to 2026-08-14 [R33][R34].
- **No competition had been held at the last confirmed step.** The status after August 2026 is unverified.
- Miljødirektoratet advised against Frøyabanken nord because of the risk to wild salmon (via search summary).
- Estimates of potential: Trænabanken alone could produce **450,000 t a year** (Oslo Economics, cited by [R38]). Norskerenna sør could add 70–140 kt [R37]. Menon's scenario reaches **480 kt a year** with about **NOK 100 billion** of investment [R37].

**Tax:** the 25% resource rent tax applies to aquaculture from 2023. Offshore farming was **left out**, and that uncertainty is cited as one reason SalMar paused [R38][R63].

**Other permits:**
- Mattilsynet can **withdraw a site approval** on welfare or biosecurity grounds. Inaccessibility counts as such a ground [R18].
- The Directorate of Fisheries handles escape investigations [R18].
- Kystverket requires **synchronised yellow lights** at the outer points. For far-offshore units it applies IALA guidance [R58].
- Offshore units are built to DNV-RU-0503 and certified under NYTEK, as for OF1 [R4][R5].

---

## 5. Failure modes and incidents (real cases mapped to model hooks)

| Failure | Real case | Mechanism | Model hook |
|---|---|---|---|
| Ballast or hatch flooding causes tilt and escape | OF1, 2018-09-03 | Sliding-bulkhead vents and manhole left open, plus a ballast-pipe leak. Main alarms were off because of too many false alarms (>20k points). The ring dipped and about 15,805 fish escaped (about 1.5%) | [R3][R9][R10]: procedure-compliance and alarm-health state; an escape event scaled by how long the tilt lasts |
| Fish-handling design flaw | OF1 generation 1 | Join between the liftable net floor and the main net crushed fish. Mortality went from 4% to 7% | [R3]: mortality spikes during first-use handling of new technology |
| Net tear | OF1, September 2020 (≥6 m) [R60b]; AOF, 2024-11-19 (inner and outer net) | Fatigue or abrasion. Weak contingency planning drew criticism | [R18]: net-integrity value, inspection by ROV, preparedness score |
| Inaccessibility leads to welfare violations and site withdrawal | AOF, 2025 | Up to 3 months without handling, delousing or harvest | [R18]: accumulated "no-access days" fed into regulator risk |
| Boarding injuries | AOF, 2023 | 4 deviations, 2 injuries, from personnel boats and service boats | [R17]: an injury roll that rises with Hs; site-specific weather limits |
| Wounds from jellyfish | AOF, November 2023 | *Apolemia uvaria* | [R17]: lower quality grade |
| Winter sores, gills, lice | Havfarm generation 1 | Mixed smolt sources and quality; about 19–25% loss; a lice drug used despite a "chemical-free" claim | [R22][R26]: smolt-batch quality and source spread raise mortality |
| Upstream supply shock | Nordlaks, 2025 | Algae bloom plus ISA left no fish; the Havfarm sat empty a year at a cost of about NOK 300 million | [R27]: an idle megastructure still costs money; post-smolt supply chain |
| Equipment damage | OF1 2018 | Cable chain between the centre module and the sliding bulkhead; 14 improvement projects | [R3][R4]: wear-and-tear tasks |
| Yard stay | OF1, 2022–23 | About 1 year for upgrades | [R60]: downtime gap between cycles |
| Licence refusal | Havfarm, 2021 | Too few cycles and measurement gaps | [R22]: a reporting and evidence meter on the conversion path |
| Submerged-pen risks | Category-level | Swim-bladder deformity, low O2, air-pocket loss | [R43] |
| Closed-pen risks | Category-level | Single pump or light failure; collisions with rigid walls (about 1% brain haemorrhage in one system) | [R43] |
| Storm damage to moorings | No published OF1 or Havfarm mooring failure was found | Faroe example: a cage failed before stocking because its mooring was unfinished | WebSearch summary of FFE 2016. Keep catastrophic failure as a rare tail event |

---

## 6. 3D anatomy

Colours marked "sampled" are medians from the owner's reference images, not from sources. Hex values are albedo starting points to adjust under the D13 AgX look.

### 6.1 Ocean Farm 1 (hero asset)

**Plan:**
- A regular 12-sided polygon with a 55 m circumradius and 28.5 m sides. Overall diameter 110 m [R6].
- The structure stands 67–69 m in total and floats at **43 m** draft. By derivation, about 24 m of topsides stand above the waterline; the upper-structure mass sits about 17 m above the waterline.
- Photo 3 shows the outer ring well clear of the water with the net visible above it. The 2018 escape happened when the ring touched the sea, so the ring normally sits well above the surface.

**Columns:**
- 12 side columns in two kinds that alternate around the ring:
  - Diameter **3.56 m** with a side pontoon below. These are the 6 cone-ended "feet" visible in photo 3.
  - Diameter **2.8 m** without a pontoon [R6].
- One centre column, also 3.56 m, with a central pontoon [R6].

**Beams (diameters) [R6]:**
- Top outer ring 2.29 m.
- Bottom outer ring 2.05 m.
- Mid outer ring 1.0 m.
- **Mid outer diagonals** 1.0 m, giving the zig-zag bracing in every bay seen in photo 3.
- **Bottom radial beams** 1.75 m, the spokes to the centre.
- **Top cross beam** 2.05 m, the walkway or gantry across the diameter seen in render 2.

**Interior:** 1 **sliding bulkhead** (about 196 t) and 2 fixed bulkheads (about 172 t each) divide the volume into 3 zones. The sliding bulkhead is the fish-handling zone [R6][R1].

**Topsides:**
- A white control room and living-quarters block on the central column [R6]. Sampled white **#FEFDF9** (render); use about #E9ECEA.
- Grey service houses and towers, sampled **#80838D**.
- Feed silos, sampled **#777887**. The **capacity of the silos is unverified**.
- Cranes, a fish-transfer chute or arm, and handrails along every beam (render 2).

**Net:**
- **EcoNet**, a semi-rigid PET monofilament mesh: 2.5 mm wire, mesh 35 × 43 mm, 570 g/m² [R6]. It covers the sides and the bottom.
- In low sun it reads warm, sampled **#85604A** (photo 3). In flat light it is pale grey.

**Mooring:** 8 lines leave in 4 pairs from the pontoon columns. Fairleads are about 30 m below the waterline. The anchor circle is about **1,100 m** in radius in 150 m of water [R6]. For gameplay, add a faint mooring-footprint overlay.

**Hull yellow:**
- Render 2 lit **#ECC83F**, shade #5F511F.
- Photo 3 in low winter sun **#CC9D30**, shade #45320B.
- Suggested albedo: about **#E6BE2E** (safety yellow), with lower parts weathered and ochre.

**Sensors and systems** labelled in owner image 1:
- O2 and temperature sensors, subsea cameras, upward-looking echo sounders.
- AIS base station, marine broadband radio, motion reference unit, position reference sensors.
- Automation and control room.
- A shore control centre linked by radio, an ROV, and AUVs.

**Water:**
- Photo 3: near **#31424A**, far **#315A7A**.
- Render 2: **#404858**.

### 6.2 Arctic Offshore Farming cage (semi-submersible pen)

- 79–80 m diameter. Upper pontoon ring 2 × 3 m, lower pontoon ring 6 × 4 m, joined by **1.5 m** columns. Steel ring about 22 m high [R20][R21].
- Double net. Air pockets along the inside of the lower ring [R17].
- Two states: **raised** for work, and **submerged** with the top of the net about 10–11 m down and only the top ring visible [R17][R21].
- A crane on each cage. A separate feed barge with a slipway, connected to each cage by subsea hoses [R17].
- Hull colour **not verified**.

### 6.3 Havfarm "Jostein Albert"

- A 385 × 59.5 m steel hull with a moulded depth of 37.75 m [R23].
- **Six 47 × 47 m square openings** in a row, each net up to 56 m deep [R23][R25].
- A bow **turret** connected to 11 anchor lines in three groups [R23].
- Flashing lights on its outer edges [R23].
- Feed silos and silage tanks; vessels berth alongside [R28].
- Hull and superstructure colours **not verified**. Check press photos before modelling.

### 6.4 Vessels and the support fleet

- **Offshore service vessel:** orange-red hull, sampled **#BF551B**, with a white or grey superstructure (photo 3). The owner diagram shows DP service vessels with an integrated bridge and echo sounders and sonars.
- **Anchor handling:** three anchor-handling tugs towed OF1 into position (FFE photo caption, 2017).
- **Wellboats:** up to about 1,000 t of fish per trip [R37]. The A3-01 roadmap test sizes of 77 m and 116 m are consistent.
- **Other vessels:** silage vessels [R28], a crew boat or helicopter (Menon names both options) [R37], an ROV and AUVs.

### 6.5 Scale against existing assets

| Asset | Size | Source |
|---|---|---|
| Fjord pen (160 m circumference) | about 51 m diameter | [R45], derived |
| OF1 | 110 m | |
| AOF cage | 80 m | |
| SFF | 164 m | |
| Havfarm | 385 m long | |

The Havfarm is longer than any wellboat, and OF1 is about 2.2 times the diameter of a fjord pen. The default camera framing must allow for this.

### 6.6 Closed pens (low-poly kit)

| Pen | Shape and size |
|---|---|
| Donut | Torus holding 22,000 m³ |
| Globe | Sphere holding 3,500 m³ |
| Egg | 21 × 15 m, 1,850 m³ |
| Pipe | Oval tube holding 2,000 m³ |
| Aquatraz | Steel ring with a 13 m skirt |

Intake depths are **15–40 m**. Show the intake hose and the pump module [R46][R47].

---

## 7. Suggested sim parameter bands (derived from §3; for the sim designers)

| Parameter | Fjord open pen (baseline) | Semi-offshore (OF1, AOF, Havfarm class) | Open-ocean (SFF class) | Closed/semi-closed post-smolt pen |
|---|---|---|---|---|
| MTB per unit (t) | 780 per licence | 3,000–10,250 | 6,240–23,000 per year | 250–1,100 |
| Volume (m³) | – | 160k–414k | about 760k | 1,850–22,000 |
| Capex | – | NOK 0.72–1.0 billion (about 100–140k NOK/t MTB) | NOK 1.5–3.0 billion | NOK 50 million (Aquatraz) |
| Build to first fish | – | 18 months (OF1) to 29 months (Havfarm) | ≥2 years (OF2) | – |
| Design Hs (100-year) | – | 5–6.7 m | about 15 m | sheltered |
| Vessel-operation cut-offs | – | crowding ≤2 m Hs, wellboat ≤5 m, boarding about 3 m | same | – |
| Inaccessible days | – | about 10–12% overall, Q1 about 30%, worst spell up to 90 days | higher (no data) | – |
| Lice multiplier against fjord | 1.0 | about 0.1–0.3 (OF1/AOF needed no treatments); Havfarm at 5 km still needed treatment | about 0.05 (SINTEF modelling found no lice exposure over 2007–16 [R7]) | 0.0–0.25 |
| Mortality per cycle | 14.2% national | 2.5–7% when things go well; 19–25% in a bad first cycle | unknown | 0.8–2.1% |
| FCR | about 1.15–1.25 | 1.02–1.30 | – | about 1.21 |
| Crew | – | 3–4 on board, about 12 in rotation, 9/12 rota | – | – |
| Energy | 0.07 kWh/kg (open pen) | shore power or hybrid | – | about 3.9 kWh/kg |

---

## 8. Calibration deltas against existing code (read-only observations)

**`src/LakskongenSim/Farm/OffshoreFarm.cs`:**
- `PenCount = 6`, commented "OF1: 6 pens", and `PenVolumeM3 = 40,000`: OF1 is one volume of about 250k m³ split into 3 zones. Six pens of about 69k m³ is the Havfarm.
- `CapacityTonnes = 8,000`, commented "OF1 about 8,000 t": OF1's MTB is 6,240 t.
- `MaxWaveHeightM = 12`, commented "OF1 rated 12 m": OF1's 100-year Hs is about 5 m and AOF's approved Hs is 6.7 m.
- Storm lockdown cuts **feeding** above Hs 6 m: the data shows feeding continues. Lock out vessel operations at about 2, 3 and 5 m Hs instead.
- Escapes come only from waves above 8 m: real escapes came from operational errors and net tears.
- `ShipShaped` is commented "394 m converted ship hull": it is 385 m and purpose-built.
- Capex: 700 million NOK against an actual 720–877 million; 900 million against about 1,000 million.
- `ConstructionDays = 1095`: the real figure is 540–880.
- `EmployeeCount = 25` and a 14-day rotation: the real figures are about 12 in rotation on 9/12.
- `FCR = 1.12`: the real range is 1.02–1.30.

**`ContainmentTech.cs`:**
- Skirt `LiceExclusion = 0.35` sits between the sourced 0.18 (single cage) and 0.54 (whole site). It could depend on site-wide adoption.
- `SemiClosed` capex of 12 million NOK against about 50 million NOK for an Aquatraz serial unit.
- No energy or grid constraint for closed pens: the sourced figure is 3.87 kWh/kg.

---

## 9. What makes it interesting as gameplay

1. **The big bet.**
   - One unit holds 6–10 kt of MTB, about the output of 8–13 standard licences. Each fjord ring is about 51 m across (derived).
   - Capex is NOK 0.7–1.0 billion and the build takes 1.5–2.5 years.
   - Two paths to a licence. In both cases, a refusal or a failed tender stays with the player as a lasting setback:
     - **Development path:** free licences, then a measurement programme, half-yearly reports and a conversion fee. The real conversion can be refused.
     - **Offshore tender path:** compete for an area commitment, write an area plan and impact assessment, then turn coastal tonnes into double tonnes offshore.
   - This is the "monument decision" of Anno, or Frostpunk's generator: one choice that shapes the whole run.
2. **Weather windows are the core verb.**
   - Players schedule stocking, delousing, harvest, crew change, feed resupply and silage pickup into forecast windows. The cut-offs come from §3.5.
   - Show a 14-day **operability ribbon** per site: the colour and shape of each cell gives Hs against each operation's limit. This follows the brief's readability-first rule.
   - Q1 should feel like a siege, with about 30% of days lost. A 90-day lockout risks a regulator withdrawal (the AOF case).
3. **Buffers and autonomy.**
   - Show gauges for days of feed in the silos, silage tank fill, the crew rotation clock and spare net or bulkhead capacity.
   - The payoff is OF1's record of zero weather-lost feeding days. The tension is choosing whether to resupply now or gamble on the forecast.
4. **Ballast and draft as a hands-on control.**
   - Raise for work, lower when Hs passes 3 m (AOF).
   - While submerged: lice protection and storm safety, but swim-bladder and oxygen risk unless the air pockets are maintained.
   - It gives a visible, physical answer to a storm warning.
5. **Alarm fatigue as a systems puzzle.**
   - More than 20,000 sensor points produce false alarms, and players can mute alarm classes.
   - Muting raises the hidden risk of an OF1-style tilt (open hatch plus ballast leak). This teaches the real lesson in a way players can see, and it keeps the rule "alarms are rare and meaningful" honest.
6. **The payoffs are clear and good.**
   - Near-zero lice, no delousing, mortality at ⅓–½ of the area average, and faster growth.
   - These read as a visible "clean water" signal around the rig, and in the harvest report and P&L.
   - Leaner, "wild-like" fish (K 1.24) could feed into quality grade. There is **no sourced price premium**, so keep it a flavour stat.
7. **First-cycle teething problems are the realistic default.**
   - New technology arrives with a hidden defect deck that shows up as handling-zone injuries, cable-chain wear, net-system changes and mixed-smolt mortality.
   - Each fix becomes an upgrade project; OF1 needed 14 [R4].
   - The second cycle is markedly better: the Havfarm went from 19% to 2.8% mortality. This gives a satisfying arc, as with Two Point's clear before-and-after feedback.
8. **The logistics chain matters.**
   - A 20 kt unit against wellboats holding about 1 kt each means many trips, repeated crowding and fish stress. Dedicated vessels need to be bought or chartered: a DP service vessel, a silage vessel, a crew boat or helicopter, an ROV and AUVs.
   - Separate wellboats for smolt delivery and for harvest, for biosecurity (Menon) [R37].
   - These are Anno-style supply routes drawn on the map.
9. **Supply shock with an idle megastructure.**
   - Smolt or post-smolt supply is the weak link (the Nordlaks 2025 case).
   - This leads naturally into **closed post-smolt pens**: grow fish to 700 g–1 kg in closed pens, which cost energy and need grid capacity, then move robust fish offshore.
   - The game's existing `CohortTransferSystem.OffshoreStocking` is the place to connect this.
10. **The spectacle sells it.**
    - A safety-yellow 12-sided rig on deep blue water in low winter sun (photo 3). Spray running over the columns in a storm. Synchronised yellow nav lights at night. An orange service vessel alongside.
    - Natural photo-mode and main-menu shots, matching the brief's ref-2 low-oblique framing.
    - Keep the offshore palette cool, with saturation only on the yellow hull and the orange vessel.

---

## 10. Gaps, conflicts and things I could not verify

- **OF1 dimensions:** height 67/68/69/70 m; weight 5,600 against 7,700 t; volume 245k against 250k m³. I used [R1] and [R6] as primary.
- **OF1 feed silos:** capacity and count not found. The accommodation berth count was also not found.
- **Havfarm:** depth 37.75 against 56.57 m; weight 33,000 against 37,500 t; hull colours; design Hs; crew and accommodation; generation 3 and 4 results. All not verified.
- **SFF wave figure:** 15 against 31 m. My reading is Hs 15 / maximum wave 31; this is an inference.
- **Offshore area competition:** status after August 2026 unknown. Area sizes in km² were not found.
- **NS 9415 exposure-class boundaries:** I could not find a public table, so I did not cite any class values.
- **SalMar Q1/Q2 2026 offshore figures:** pages returned HTTP 403, so only search summaries were available.
- **Ocean Globe, Stadion and Ecomerden:** only licence-table entries [R43]; no specifications.
- **Insurance:** only the Aquatraz NOK/kg comparison. No offshore premium data.
- **Land-based (RAS) farming:** out of scope for this dossier; the owner's land-based images are not on disk.

---

## 11. References (key, URL, year)

- R1. Ocean Farming fact sheet, Fiskeridir (c.2016): https://www.fiskeridir.no/akvakultur/utviklingstillatelser/_/attachment/inline/db3cbdf3-d8e0-4e5e-8e8c-5578964ee649:d9885db23ead095df52e747ae012bbdaf1e35248/Ocean%20Farmimg%20Fakta-ark.pdf
- R2. OF1 half-year report 1 (2018): https://www.fiskeridir.no/Akvakultur/Tildeling-og-tillatelser/Saertillatelser/Utviklingstillatelser/Kunnskap-fra-utviklingsprosjektene/_/attachment/download/7310f2be-a540-4c92-a74c-11d3175abbcb:44be52ac9dee3e46c93a867adfe43ec61d7005d2/1-halvarlig%20sammendragsrapport.pdf
- R3. OF1 half-year report 2 (2019): https://www.fiskeridir.no/Akvakultur/Tildeling-og-tillatelser/Saertillatelser/Utviklingstillatelser/Kunnskap-fra-utviklingsprosjektene/_/attachment/download/53a2b6f0-b739-40cd-862d-27925d2e3cb7:ae85f3f9a2f6d12fa33e9a8085f587eb8f2cc2ed/2-halvarlig%20sammendragsrapport.pdf
- R4. Fiskeridir conversion decision, Ocean Farming (2020): https://www.fiskeridir.no/nyheter/0720/De-foerste-utviklingstillatelsene-er-konvertert/_/attachment/download/07bc99bc-9361-424c-bc4a-a24a107e512f:7dbaa10b4191d922d9b57f5fc38dcda5b98510e7/ocean-farming-AS-konvertering-utviklingstillatelser.pdf
- R5. DNV, OF1 class certificate (2020): https://www.dnv.com/news/ocean-farm-1-receives-first-ever-offshore-fish-farming-class-certificate-from-dnv-gl-165626
- R6. NTNU MSc thesis, "Numerical Modeling and Analysis of a Semi-submersible Fish-cage" (2018): https://hdl.handle.net/11250/2615064
- R7. Halse, SalMar Aker Ocean, "Velferd og fiskehelse til havs" (2022): https://gronnplattform.stiimaquacluster.no/wp-content/uploads/sites/2/2022/04/Marianne-Halse_SalmarAkerOcean_Velferd-og-fiskehelse-til-havs.pdf
- R8. SalmonBusiness (2018-07-19): https://www.salmonbusiness.com/salmars-gigantic-ocean-farm-gears-up-for-harvest/
- R9. FFE, OF1 tilt (2018-09-04): https://www.fishfarmingexpert.com/escape-ocean-farm-1-salmar/fish-escape-from-ocean-farm-1-as-structure-tilts/1262622
- R10. SalmonBusiness, 15,805 escaped (2018): https://www.salmonbusiness.com/salmar-satisfied-after-only-15805-salmon-escape-from-worlds-first-offshore-fish-farm/ ; Fish Focus (2019): https://fishfocus.co.uk/promising-results-for-salmar-ocean-farm-1-facility/
- R11. iLaks (2023-03-08): https://ilaks.no/den-faktiske-veksten-var-bedre-enn-forventet/
- R12. Nordnorsk Rapport (2023-01): https://www.nordnorskrapport.no/2023/01/ligger-fremtiden-i-smartanlegg-langt-til-havs/
- R13. Kongsberg (2016): https://www.kongsberg.com/maritime/news-and-events/news-archive/2016/worlds-first-offshore-aquaculture-development-project-receives-green-light
- R14. FFE, lice at OF1 (2017): https://www.fishfarmingexpert.com/lice-salmar/sea-lice-discovered-at-ocean-farm-1/1362396
- R15. Heasman et al., Frontiers in Aquaculture (2024): https://www.frontiersin.org/journals/aquaculture/articles/10.3389/faquc.2024.1444186/full
- R16. SINTEF blog, OF1 numerical modelling (c.2021): https://blog.sintef.com/ocean/numerical-modelling-with-the-ocean-farm-1-structure/
- R17. AOF summary report 1, Fiskeridir (2024): https://www.fiskeridir.no/Akvakultur/Tildeling-og-tillatelser/Saertillatelser/Utviklingstillatelser/Kunnskap-fra-utviklingsprosjektene/sammendragsrapport-1-gen-aof.pdf/_/attachment/download/c2a5a4e2-bcda-4bb8-a010-a42ba1225eab:da8c880dfb3281a9dd5d4a161ff7bf0d6d78b423/sammendragsrapport-1-gen-aof.pdf
- R18. FFE, Mattilsynet withdraws Fellesholmen (2025-12-12): https://www.fishfarmingexpert.com/biosecurity-fish-welfare-mattilsynet/food-safety-watchdog-withdraws-approval-for-offshore-fish-farm-site/2038393
- R19. FFE, AOF conversion (2026-03-16): https://www.fishfarmingexpert.com/arctic-offshore-farming-development-licences-directorate-of-fisheries/salmar-wins-approval-to-convert-6000-tonne-development-licences/2087626
- R20. SalmonBusiness, AOF pen in place (2021-09-27): https://www.salmonbusiness.com/nrs-salmon-pen-with-capacity-for-1-2-million-fish-now-in-place/
- R21. FFE, AOF go-ahead (2018-03-12): https://www.fishfarmingexpert.com/development-licences-norway/mega-cage-plan-is-given-the-go-ahead/1277394 ; FFE site approval: https://www.fishfarmingexpert.com/article/site-approved-for-3000-tonne-capacity-salmon-cage
- R22. Fiskeridir conversion decision, Nordlaks Havfarm 1 (2022-03-11): https://www.fiskeridir.no/akvakultur/utviklingstillatelser/_/attachment/inline/d08baacd-3f5e-4884-974f-c5a62215db85:0fd439ad06b3cb371c7d636d3f3183dd2e55e8fd/nordlaksn-oppdrett-as-vedtak-om-konvertering-av-utviklingstillatelser.pdf
- R23. Nordlaks, Havfarm pages (2022+, accessed 2026): https://nordlaks.com/ocean-farm-jostein-albert/ and https://nordlaks.no/havfarmen-jostein-albert/
- R24. ABL Group, Havfarm 1 transport approval (2020): https://abl-group.com/case-studies/havfarm-1-jostein-albert-single-voyage-transport-tow-approval-2020/
- R25. FFE (2019-12-12): https://www.fishfarmingexpert.com/article/10000-tonne-havfarm-takes-shape-in-china
- R26. NRK (2021-06-29): https://www.nrk.no/nordland/havfarmen-til-nordlaks-skulle-berge-oppdrettsnaeringa-_-i-deler-av-anlegget-dor-hver-fjerde-laks-1.15555653
- R27. iLaks (2025-08-29): https://ilaks.no/tar-tap-pa-300-millioner-nordlaks-dropper-utsett-pa-havfarmen-etter-tap-fra-alger-og-sykdom/
- R28. SalmonBusiness, silage vessels (2021-05-05): https://www.salmonbusiness.com/high-traffic-of-silage-vessels-from-the-gigantic-salmon-ocean-farm-jostein-albert/
- R29. iLaks, Smart Fishfarm (2018-06-08): https://ilaks.no/smart-fishfarm-er-dobbelt-sa-stor-som-ocean-farm-1-na-har-den-fatt-forelopig-ja-til-utviklingstillatelser/
- R30. FFE, OF2 and 23,000 t cage (2021-04-23): https://www.fishfarmingexpert.com/ocean-farm-1-ocean-farm-2-salmar/coming-soon-ocean-farm-2-and-a-23000t-super-cage/1188802
- R31. SalmonBusiness, OF2 approval (2025-02-12): https://www.salmonbusiness.com/next-sen-salmon-farming-salmar-secures-approval-for-ocean-farm-2-at-new-site/
- R32. iLaks, SFF licences (2019, via search summary): https://ilaks.no/salmar-kan-fa-atte-utviklingstillatelser-for-sin-nye-gigantfarm/
- R33. Haavind, offshore timeline (2026): https://haavind.no/havbruktilhavs/tidslinje/
- R34. Regjeringen, royal resolution (2026-02-27): https://www.regjeringen.no/no/aktuelt/kongelig-resolusjon-om-havbruk-til-havs-fastsatt/id3150415/ ; Kyst.no: https://www.kyst.no/havbruk-til-havs/apner-for-havbruk-til-havs-i-tre-omrader/2077617
- R35. Regjeringen, new offshore rules (2022-11-08): https://www.regjeringen.no/no/aktuelt/nye-regler-for-etablering-av-havbruk-til-havs/id2946173/
- R36. Lovdata, laksetildelingsforskriften chapter 4 (amended 2026-01-14): https://lovdata.no/dokument/SF/forskrift/2022-11-07-1929/KAPITTEL_4
- R37. Menon publication 131/2023, offshore value-chain analysis (2023): https://www.nho.no/contentassets/74e9017c98134ad2a5f89052ac7618d4/verdikjedeanalyse-av-havbruk-til-havs---menon-publikasjon-131-2023.pdf
- R38. Norsk Fisk (2025-09-24): https://norskfisk.no/2025/09/24/havbruk-til-havsstor-risiko-og-store-muligheter/
- R39. Regjeringen, traffic-light colouring (2024, via search summary): https://www.regjeringen.no/no/aktuelt/ny-fargelegging-i-trafikklyssystemet-for-havbruk/id3028522/
- R40. Regjeringen, development licences (2015, via search summary): https://www.regjeringen.no/no/aktuelt/nye-konsesjoner-skal-utvikle-framtidens-oppdrett/id2462544/
- R41. Regjeringen, evaluation of development licences (via search summary): https://www.regjeringen.no/contentassets/d2aef29fad5140fbb42fb85f9f9a16d9/evaluering-av-utviklingstillatelser-for-havbruksnaringen-og-vurdering-av-alternative-ordninger-for-fremtiden.pdf
- R42. TU, 104 applications (2017, via search summary): https://www.tu.no/akvakultur/104-sokte-utviklingstillatelser-for-oppdrett-siste-soker-kom-14-minutter-for-fristen/325324
- R43. Bui et al., HI report 2025-30 (2025): https://www.hi.no/hi/nettrapporter/rapport-fra-havforskningen-2025-30
- R44. Aunsmo, Aquatraz Eiterfjorden analysis (2021): https://www.fiskeridir.no/Akvakultur/Tildeling-og-tillatelser/Saertillatelser/Utviklingstillatelser/Kunnskap-fra-utviklingsprosjektene/_/attachment/download/b961df7e-fdc9-447a-8e72-248b462327ca:a5a5207d28d6de8a1bfaafc570449760ae32c201/Analyse-produksjonsdata-Aquatraz-Eiterfjorden-21-12-2021.pdf
- R45. Aquatraz G1 biology final report (2020): https://www.fiskeridir.no/Akvakultur/Tildeling-og-tillatelser/Saertillatelser/Utviklingstillatelser/Kunnskap-fra-utviklingsprosjektene/_/attachment/download/aeac4155-a691-4b34-b721-14d41bd3dd00:39bb005e415711eb0853f38b744db31e494ba4a1/AQT-G1-sluttrapport-biologiprogram-20200123.pdf
- R46. FFE, Marine Donut test (2024): https://www.fishfarmingexpert.com/bluegreen-development-permits-marine-donut/very-low-mortality-in-the-first-full-scale-test-of-marine-donut/1859328 ; iLaks (2025): https://ilaks.no/ti-ar-med-utvikling-gir-resultater/
- R47. Nofima/Frontiers, FishGLOBE V5 (2022): https://www.frontiersin.org/journals/animal-science/articles/10.3389/fanim.2022.871433/pdf
- R48. Lerøy Preline: https://leroyseafood.com/en/sustainability/preline ; The Fish Site (via search summary): https://thefishsite.com/articles/why-floating-closed-containment-aquaculture-could-be-dead-in-the-water
- R49. FFE, Mowi closed pens (2026-01-28): https://www.fishfarmingexpert.com/closed-cages-fish-welfare-mowi/floating-closed-pens-not-yet-ready-for-full-cycle-says-mowi-norway/2060357
- R50. FFE, grid capacity and energy (2026-03-20): https://www.fishfarmingexpert.com/closed-containment-electricity-consumption-grid-capacity/grid-capacity-is-a-current-problem-for-norways-closed-fish-farm-push/2091306
- R51. Geitung et al., snorkel cage 75% (2019): https://bora.uib.no/bora-xmlui/handle/1956/21031
- R52. Stien et al., snorkel trials (2016, via search summary): https://imr.brage.unit.no/imr-xmlui/handle/11250/2409383
- R53. SINTEF/FHF, Permaskirt (2015, via search summary): https://www.sintef.no/contentassets/29f559fa43b640549a5c56f36b9d391d/frank-og-lien-2015.pdf
- R54. GSA, Atlantis/Nautilus pens (via search summary): https://www.globalseafood.org/advocate/is-deep-water-aquaculture-the-way-for-salmon-farmers-to-beat-sea-lice/
- R55. Veterinærinstituttet, mortality in 2025 (2026): https://www.vetinst.no/nyheter/dodelighet-i-lakseoppdrett-i-2025
- R56. Lader (SINTEF), site wave exposure (2017): https://oivarn.folk.ntnu.no/MB-day/2017/06-FishFarmingSiteWaveExposure_PL.pdf
- R57. Fiskeridir, Nøkkeltall 2024 (2024, via search summary): https://www.fiskeridir.no/statistikk-tall-og-analyse/data-og-statistikk-om-akvakultur/statistiske-publikasjon-innen-akvakultur/_/attachment/inline/dce874d2-75c8-49c8-9dab-cfe1f4a2eb8d:db20b36be72564910d77c84dcac4078e89ec67d5/nokkeltall-havbruk-2024.pdf
- R58. Kystverket input on the Norskehavet site (45 nm; IALA) (2023, via search summary): https://www.fiskeridir.no/hoeringer/horing-om-klarering-av-lokalitet-for-akvakultur-i-norskehavet/_/attachment/download/ca2bfd13-0e44-4ba9-8659-167005c4f7ba:dc58c38d3c62fb5d106547ce37864d8dffe2168b/Kystverkets%20innspill%20til%20s%C3%B8knad%20om%20klarering%20av%20lokalitet%20i%20Norskehavet%20(L)(3838672).pdf
- R59. Maritime Executive, Guoxin 1: https://maritime-executive.com/article/china-delivers-first-tanker-sized-aquaculture-vessel ; ICSF, Deep Blue 1: https://icsf.net/?p=106415 (via search summary)
- R60. Maccaferri, OF1 net and yard stay (2022, via search summary; host unreachable): https://website.maccaferri.com/success-stories/ocean-farm-1-supply-of-kikkonet-net-and-net-structure
- R60b. IntraFish, OF1 crack/net damage (2020, via search summary): https://www.intrafish.com/aquaculture/salmar-discovers-another-crack-in-offshore-farm/2-1-875185
- R61. Bloomberg (2018, via search summary): https://www.bloomberg.com/news/articles/2018-07-30/this-300-million-deepwater-platform-houses-1-5-million-salmon
- R62. SeafoodSource, SalMar 2026 guidance (2026, page 403; via search summary): https://www.seafoodsource.com/news/business-finance/salmar-s-strong-start-to-2026-in-norway-brings-increased-harvest-guidance
- R63. FFE, offshore left out of resource rent tax (2023, via search title): https://www.fishfarmingexpert.com/2024-budget-ground-rent-tax-norway/dismay-as-norway-government-chooses-not-to-tax-offshore-salmon-projects/1577542
- R64. Samfunnsøkonomen, cost estimate (via search summary): https://www.samfunnsokonomen.no/artikkel/kostnader-og-nytte-ved-miljotiltak-i-norsk-lakseoppdrett/

## research-ux

# Farm types in Lakskongen: design rules from successful management sims

Research return, 2026-10-14. Read-only: nothing in the repo was edited, built or committed.

**Method.** Firecrawl failed again with HTTP 401, the same rotation problem as D26, so all research used WebSearch and WebFetch. Some pages returned 403 or 402 (Frontier, TechRaptor, Fandom) and are cited from search excerpts. Sources are marked as dev blog, wiki, review or aggregator; aggregator figures (vaporlens, SteamPulse) are flagged as weak. Every number is either sourced (URL and year) or marked **[proposal]**, meaning a tuning suggestion of mine rather than a fact.

---

## A. The ten rules that matter most

1. **Each farm type must change the constraint that limits the player, not just multiply output.**
   - Open pens are limited by biology (lice, temperature, O2).
   - Semi-closed pens are limited by pumps and energy.
   - Offshore is limited by weather windows and logistics.
   - Land-based RAS is limited by capital, water chemistry and power.
   - Sources: Factorio Space Age planets, the Anno 1800 Arctic, Against the Storm biomes.
2. **Unlock through a world event with choices, never through a research percentage.** Use a licence tender window (modelled on Norway's 2015–2017 development-licence scheme) and a land-permit track. Sources: the Anno 1800 New World expedition, Frostpunk 2 research and council.
3. **The player chooses the order.** Offshore and land-based are parallel branches, and both can be reached by the end. Source: Cities: Skylines II development trees.
4. **Show the site before the commitment.** A free ghost placement shows cost, yield, risk and "what you will have to watch". Sources: Anno 1800 blueprint mode, Farming Simulator soil maps, Planet Coaster 2 heatmaps.
5. **Big projects are built in visible phases, and each phase pays something off.** Sources: Anno monuments, Satisfactory's Space Elevator, the Anno 117 Amphitheatre.
6. **The first completion gets one skippable fly-to with scale contrast**, and photo mode is available from launch, not added later. Sources: Anno 117 (completion parade, photo mode added in a patch), JWE2 capture mode.
7. **Each type gets its own instrument.** Offshore gets a weather-window board; RAS gets a tank board with comfort bands. Sources: Planet Zoo needs ranges, Planet Zoo 2 aquarium filter network, Frostpunk 2 Whiteout bar, Timberborn drought warning.
8. **Failure is telegraphed, can be countered and can be recovered from, and an incident report shows the causal chain afterwards.** Sources: Timberborn, Against the Storm, RimWorld and Dwarf Fortress stories; JWE2's "escalation too fast" is the counter-example.
9. **Hazard comes from physical exposure, never from wealth.** Spending big must not attract disasters (the RimWorld anti-pattern).
10. **Late game needs flagships and a loop that continues after them, and prestige that is not cash.** Sources: Anno monuments, Cities: Skylines II signature buildings, Factorio's "And now what...", D16.

---

## B. Evidence by game

| Game | What it does with types, tiers or big projects | Lesson for Lakskongen | Source (year) |
|---|---|---|---|
| **Anno 1800** | The New World opens when the city reaches the Artisans tier and then runs an expedition: a ship, cargo, choice events and morale. | Unlock a new type with a narrative gate that has choices. | dotesports.com/general/news/how-to-unlock-the-new-world-in-anno-1800 (n.d.); game 2019 |
| Anno 1800: The Passage | The Arctic plays as a small outpost. Heat travels along street distance, not a radius. Arctic flu "spreads faster". Airships need gas from ice cliffs. The airship hangar is built in 4 stages. | A new region flips the layout rule and adds its own hazard. | anno-union.com/devblog-die-passage (Nov 27, 2019) |
| Anno 1800 | Blueprint mode lets the player plan with silhouetted buildings without spending resources. Fastest-selling Anno; Metacritic 81 on PC. | Free ghost placement of big projects. | en.wikipedia.org/wiki/Anno_1800 (2019 release) |
| **Anno 117** | Monuments are "multi-phase construction projects", one per island. The Amphitheatre has 4 stages, and its look changes at each stage. A festival parade starts on completion. | Visible phases and a completion moment. | anno-union.com/devblog-the-amphitheatre-a-monument-for-the-ages/ (Nov 6, 2025) |
| Anno 117 | Photo mode (free camera, time of day, DoF, tilt-shift) arrived only in Update 2.0, "as a first version". | Ship photo mode at launch. | anno-union.com/highlights-of-update-2-0-changes-and-improvements/ (Aug 4, 2026) |
| Anno 117 | TechRadar lists the Albion region as "lacking some depth". | A new type must carry new verbs, not only new art. | techradar.com/gaming/anno-117-pax-romana-review (2025) |
| **Cities: Skylines II** | Milestones run on XP earned from varied actions and feed development trees. The player chooses the order and can unlock everything by the end. Signature buildings are gated by conditions. | Gate by demonstrated competence; the player picks the branch order. | paradoxinteractive.com/games/cities-skylines-ii/features/game-progression (Aug 21, 2023) |
| Cities: Skylines II | The CEO apologized because the economy "did not meet the quality standard"; the info tab misreported profit. Economy 2.0 removed subsidies to be "more transparent". | Opaque economics kill trust. | pcgamesn.com/cities-skylines-2/broken-economy (Feb 12, 2024); shacknews.com/article/140113 (Jun 6, 2024) |
| **Frostpunk 2** | A council of 100 delegates; promises come with deadlines. District adjacency has trade-offs (extraction next to housing adds squalor). The Whiteout is shown on a weather bar ahead of time. PC Gamer 85/100. | Big choices with visible trade-offs, and storms with a timeline. | steamdeckhq.com/game-reviews/frostpunk-2/ (2024); dotesports.com/frostpunk/news/how-to-survive-a-whiteout-in-frostpunk-2 (2024); opencritic.com/game/11840 (2024) |
| **Factorio** | The devs complained "the rocket has no purpose". After launch the player was left with "And now what...", which they fixed with space science and infinite research. | The endgame needs a loop that continues. | factorio.com/blog/post/fff-187 (Apr 21, 2017) |
| Factorio: Space Age | Each planet flips one rule: Vulcanus (no ore, molten metal from lava), Fulgora (scrap recycling, lightning power), Gleba (spoilage, farming). Gleba draws complaints. | Distinct, not bigger. Even so, a single flipped rule can feel gimmicky. | pcgamer.com/games/sim/factorio-space-age-review (2024); vaporlens (aggregator, weak) |
| **Satisfactory** | Tier 0 HUB upgrades change the HUB's look. The Space Elevator has 5 phases, each unlocking tiers. Every delivery has a launch animation; the final phase rolls credits. | Phases with visible launches and real unlocks. | satisfactory.wiki.gg/wiki/Space_Elevator and /Milestone (wiki, read 2026; 1.0 in 2024) |
| **Planet Zoo** | Four core needs. Each factor shows the species' comfort range. Animals show stress through behaviour (hiding, lying down). | Comfort bands plus behavioural tells. | planetzoogame.com/help-centre/player-guides/the-animals (Frontier guide; game 2019) |
| **Planet Zoo 2** | Aquariums connect to a water-filter network. Salinity, temperature and cleanliness must be kept in range. Released Oct 13, 2026, so this is design intent only. | The closest genre analogue to a RAS tank board. | simulationdaily.com/news/planet-zoo-2-dives-underwater/ (Jul 23, 2026); shacknews.com/article/149328 (2026) |
| Planet Coaster 2 | Guest heatmaps arrived post-launch. The UI was called "atrocious"; Eurogamer gave it 2/5 for an exhausting UI. | Overlays matter; nested sub-menus do not work. | outsidergaming.com (2025); cogconnected.com/review/planet-coaster-2-review/ (2024); opencritic.com/game/17583 (2024) |
| **Jurassic World Evolution 2** | "A management sim that shines when things go wrong" (PC Gamer 72/100). Storms cut power and walls. Gamerbraves found the escalation too fast; GameGeeker says the opening throws comfort, power, disease and weather at the player all at once. | Drama works, but it must be paced and countered. | pcgamer.com/jurassic-world-evolution-2-review (2021); gamerbraves.com/?p=100817 (2021); gamegeeker.com/fr/games/2-1244460/review (n.d.) |
| **Two Point Hospital / Campus** | Reviewers praise the "readable art" and a "layered unlock tree". Rooms are placed as blueprints. In Campus, stars come from mixed challenges and each level introduces one new course. | One new type per level; every room reads at a glance. | gamegeeker.com/de/games/two-point-hospital-535930 (n.d.); pcworld.com/article/401952 (2018); pcgamesn.com/two-point-campus/review (2022) |
| **Against the Storm** | Each biome has baseline effects. The best rewards sit in dangerous and forbidden glades. The seasons run Drizzle 4:00, Clearance 4:00, Storm 2:00–4:00, shown on a timer. Players complain when the reward is not worth the danger. | Risk must pay; hazard runs on a visible cycle. | wiki.hoodedhorse.com/Against_the_Storm/Seasons (wiki, v1.5.4); steamcommunity threads (2023–24) |
| **Timberborn** | A drought warning shows the estimated days left, and guides treat it as the "final check". | Lead-time warnings with a number. | timberborn.wiki.gg/wiki/Drought (wiki, read 2026) |
| **Workers & Resources** | Materials must be delivered to the site per blueprint. Auto-build is "an expensive way of development". Reviews call it "very complicated" with a cluttered UI. | Offer a pay-to-rush route; keep the logistics readable. | wiki.hoodedhorse.com/Workers_Resources_Soviet_Republic/Construction_materials (wiki); opencritic.com/game/16932 |
| **Farming Simulator 25** | 400 licensed machines from 150 brands are the fantasy. Precision Farming soil sampling yields soil and yield maps. | The machinery fantasy works, and surveying is a paid verb that reveals a map. | mezha.media/en/reviews/farming-simulator-25-review-several-games-in-one (2024); farmingsimulator.wiki.gg (FS22) |
| **RimWorld** | Framed as a "story generator" (GDC 2017). Raid points scale with colony wealth, and players call that punishment for doing well. | Stories come from systems; do not tax investment with hazard. | gdcvault.com/play/1024232 (2017); rimworldwiki.com/wiki/Wealth_management; ludeon.com/forums (2018) |
| **Dwarf Fortress** | "Procedural mythmaking"; Legends mode keeps world history. | Keep a company history and incident reports. | pcgamer.com/dwarf-fortress-review (2022) |
| Captain of Industry | Players describe a "build/stabilize/build" cycle. The roadmap promises to "streamline & simplify early game". | Expect a stabilise phase after each new type. | coigame.com/Roadmap; steamcommunity threads (thin evidence) |
| Railway Empire / Transport Fever 2 | The era sets which locomotives and vehicles are available; vehicles carry wear and tear. | Tech eras can follow real history. | Railway Empire 1 manual (Kalypso PDF); gamepressure (thin) |
| **Aquaculture games** | AquaKultor's fish reach full size "in under two minutes" (SalmonBusiness). Aquaculture Land has 77% positive of 203 reviews (SteamPulse, crawl date unknown). The Aquaculture Simulator demo makes overfeeding poison the tank and over-aeration supersaturate it. Fishing: North Atlantic is praised for licensed vessels and weather data from Nova Scotia. | The niche is nearly empty. Authenticity sells; compressing biology too far kills meaning; water chemistry can be play. | salmonbusiness.com/?p=80244 (2018); steampulse.org/game/858630; kellwright-studios.itch.io/aquaculture-simulator; xboxaddict.com/Staff-Review/14532 (n.d.) |

---

## C. Design rules

### 1) Unlock pacing and the reveal

- **U1. Tease the type one act before it can be afforded.**
  - Satisfactory shows the next phase on the elevator, and Anno offers the expedition the moment Artisans are reached.
  - In Lakskongen: a rival (parody name, D17) tows a yellow octagonal hull past the player's fjord, and a news card appears.
  - The Leksikon entry unlocks, and the build menu shows a locked card with a silhouette and the exact condition.
  - The `forste_sesong` demo gets the teaser only. It sells the full game.
- **U2. Unlock through an event with choices, not a research node.**
  - Model offshore on the real development-licence window. It was created on 2015-11-19, and applications were accepted until 2017-11-17 (ilaks.no/ordningen-med-utviklingstillatelser-opphorer, 2017).
  - Applicants filed for about 156 licences totalling 121,260 t; Ocean Farming received a commitment for 8 licences, 6,240 t MTB (iLaks snapshot, ilaks.no/ocean-farming-over-forste-hinder, about 2016).
  - Game flow:
    1. An application window opens [proposal: 2 in-game years].
    2. The dossier fixes the concept, site and targets (escape-free, lice).
    3. Rivals bid, and an award is made.
    4. Milestone reviews follow.
    5. Conversion to commercial licences happens only if the targets are met. The real precedent: Fiskeridirektoratet refused to convert Nordlaks' Havfarm development permits in 2021 because the target criteria were unmet (fiskeridir.no conversion-rejection PDF, 2021).
  - This works like Frostpunk 2's promises with deadlines.
  - Later-game offshore uses the real areas:
    - Government decision on 2025-09-02.
    - Royal resolution on 2026-02-27 opening Norskerenna sør, Frøyabanken nord and Trænabanken (regjeringen.no royal-resolution PDF, 2026).
    - The PM says awards come in 2027 (ilaks.no/store-varsler-utdeling-av-oppdrettskonsesjoner-til-havs-i-2027, Jun 2, 2026).
- **U3. Land-based uses a different gate: a permit track, not a scarce-licence auction.**
  - Since June 2016, Norwegian land-based food-fish permits have no cap on numbers and carry no fee (ilaks.no/gront-lys-til-landets-forste-landbaserte-matfiskoppdrett, 2016).
  - The gate is a coastal plot, a county application and a separate discharge permit from Statsforvalteren.
  - Land-based is limited by capital and competence, not by licence scarcity, and that difference is itself a design pillar.
- **U4. Gate by demonstrated competence**, as Cities: Skylines II gates on XP and Two Point Campus on mixed challenges.
  - Offshore dossiers score better with a record free of escapes and lice breaches.
  - RAS requires one completed post-smolt or smolt cycle; the existing post-smolt facility is the stepping stone.
- **U5. One new type per act, each with a short pilot objective set.** This follows Two Point Campus (one new course per level) and avoids JWE2's everything-at-once opening.
  - [proposal] Act 1: open pens.
  - Act 2: lice skirt, semi-closed pen and post-smolt.
  - Act 3: the development-licence window, where the player picks offshore *or* land first.
  - Act 4: conversion and the second branch.
  - Late game: flagships.
- **U6. Let the player choose the order.** By the end of the campaign both branches are reachable (Cities: Skylines II development trees).

### 2) Mechanically distinct types

- **D1. Each type changes what limits the player** (Space Age planets, the Anno Arctic, Against the Storm biomes); see the spec cards in §D.
- **D2. Each type brings at least one verb no other type has and at least one failure class no other type has.**
  - A global modifier such as "lice −10%" is never acceptable as the type's identity.
  - [proposal] Acceptance: the Q4-01 sweep (D10) adds offshore-first and RAS-first strategy families, and no type may dominate across all three price regimes and all PO traffic-light states.
- **D3. Trade-offs are shown on one comparison card**, like Frostpunk 2's opposing axes. It has the same axes for every type:
  - CAPEX, OPEX per kg, and time to first harvest;
  - lice exposure, escape risk and weather exposure;
  - technical-failure risk and energy/CO2;
  - licence route.
- **D4. Placement matters**, as with Frostpunk 2 adjacency and Anno's fixed lodes.
  - Offshore needs exposure: significant wave height (Hs), current and depth, balanced against distance to port.
  - RAS needs a coastal plot with rock-blasting cost, seawater-intake depth and temperature, grid capacity, a wellboat quay and an outfall.
- **D5. Types complement each other in a portfolio**, like the Arctic outpost feeding the Anno empire.
  - Land post-smolt shortens sea time, which means fewer treatments at the open pens.
  - Offshore relieves fjord crowding.
  - Synergy, not replacement, keeps the open-pen game alive.
- **D6. Upgrades reveal information.**
  - The offshore reference diagram's labelled systems become an upgrade ladder, each with a data layer, in the way Precision Farming sampling produces maps:
    - O2/temperature sensors give a live water field;
    - subsea cameras give lice counts without crowding;
    - upward echo sounders give biomass accuracy;
    - the MRU (motion reference unit) gives a structure-motion trend;
    - DP and position reference allow operations in higher Hs;
    - AIS and marine broadband allow remote operation from the shore control centre.
  - The pieces in the land references (substation, intake basin, conveyor bridge, harvest hall) are each a capacity *and* a failure point.

### 3) Build and placement UX for big projects

- **B1. A site lens before commitment**, like Planet Coaster 2 heatmaps, Farming Simulator soil maps and Anno island resources.
  - Offshore lens: Hs climatology, current, depth, shipping lanes, protected and military zones, and the three real areas.
  - Land lens: plots, intake depth, grid, quay and neighbours.
  - This reuses G3-03's lens bar.
- **B2. Ghost placement is free** (Anno blueprint mode).
  - The preview card shows CAPEX, OPEX, capacity, the first-harvest date, the risk radar and a "what you will watch" strip (the instrument from rule 5).
- **B3. Construction runs in phases you can see, and each phase has a payoff** (Anno monuments, Satisfactory, Workers & Resources material deliveries).
  - Offshore:
    1. Yard contract.
    2. Hull rises at the yard (picture-in-picture).
    3. Tow, using a weather-window check.
    4. Mooring, with anchor tugs.
    5. Commissioning.
    6. First stocking.
    - Ocean Farm 1 was built in China and arrived after an 11-week voyage in September 2017 (fishfarmingexpert.com/article/world-s-first-offshore-fish-farm-on-its-way-to-norway/, 2017). Three anchoring tugs towed it into position (fishfarmingexpert.com/article/picture-special-anchoring-the-offshore-fish-farm, 2017).
  - Land:
    1. Blast the rock pad.
    2. Foundations.
    3. Tank rings rise.
    4. Water-treatment hall.
    5. Intake and outfall.
    6. Biofilter maturation.
    7. First smolt.
- **B4. Offer a turnkey contract at a premium as an alternative to managing the project yourself** (Workers & Resources auto-build). Turnkey removes the phase choices but not the time.
- **B5. Show the phase timeline in the world**: scaffolding, cranes and a small progress ring on the anchored card. Never a percentage in a list row (today's `OffshorePanel.gd:133`).

### 4) The "first time you see it" moment

- **F1.** On first completion, play one skippable, non-blocking fly-to [proposal: ≤ 20 s], after which time resumes.
  - Offshore: the tow arrival with tugs, then the first wellboat stocking.
  - Land: dusk, the tanks fill and the lights come on, then the orange conveyor bridge carries the first harvest to the hall.
  - Models: Anno 117's completion parade, Satisfactory's launch animation.
- **F2. Frame against a known object for scale.**
  - Ocean Farm 1 is 110 m in diameter, about 68–70 m high and 250,000 m³ (sources disagree on height; fishfarmingexpert.com and salmonbusiness.com, 2017).
  - Havfarm 1 is 385 m × 59.5 m and holds 10,000 t (fishfarmingexpert.com/article/in-the-shadow-of-the-giant-watch-a-video-of-the-havfarm, Aug 26, 2020).
  - Frame either next to a familiar workboat and an open-pen ring.
- **F3. Photo mode at launch**, with time of day, DoF and the low oblique from owner ref 2. Anno 117 added its photo mode later as "a first version" (2026).
- **F4. Repeat builds get a 3-second toast** and no cinematic [proposal]; the first one is special.

### 5) Operating UX per type: the instruments

- **O1. Open pen:** the anchored pen card with lice against the limit, sea temperature and O2. This is today's baseline.
- **O2. Offshore weather-window board** (Frostpunk 2 Whiteout bar, Against the Storm season timer, Timberborn days-left).
  - A 10-day Hs and wind strip, with a limit line for each operation: feeding, crew change, delousing, harvest and net inspection.
  - Jobs are dragged into green windows.
  - Also on the board: feed-silo days of autonomy, a ballast/trim gauge and an MRU motion trend.
- **O3. RAS tank board** (Planet Zoo comfort ranges, Planet Zoo 2 filter network).
  - The tank grid mirrors the 3D layout, with big grow-out tanks and smaller post-smolt tanks as in LAND 1.
  - Each tank is a ring showing O2, CO2, ammonia/nitrite, H2S risk, temperature and density in kg/m³ against its comfort band.
  - Below the grid: biofilter health, power and backup state, and water reuse versus intake.
- **O4. Semi-closed pens:** pump state, O2 inside versus outside, and energy cost per day.
- **O5. The world is the dashboard.** Each instrument is a lens in the 3D view first and a docked Ink panel second (D15, U3-01).
- **O6. Automation is an upgrade you can see**, never a hidden safeguard. Cities: Skylines II had to remove invisible subsidies to regain transparency (2024).

### 6) Failure drama done fairly

- **FD1. Lead time scales with severity** (Timberborn, Frostpunk 2).
  - [proposal] Offshore storms are forecast at least 3 in-game days ahead.
  - RAS chemistry drifts for at least 24 sim-hours before an H2S event, unless the player dismissed an explicit maintenance prompt.
- **FD2. Counter-play exists before, during and after.**
  - Offshore: before, fill silos, pre-harvest and deballast; during, a choice between crash-harvesting and riding it out; after, salvage, insurance and an investigation that unlocks a procedure upgrade.
  - RAS: before, choose an MBBR or fixed-bed filter; during, cut feed, flush and raise exchange; after, the same salvage and investigation steps.
- **FD3. Use real causal chains as templates and show them in an incident report** (RimWorld and Dwarf Fortress stories).
  - Offshore, Ocean Farm 1 in September 2018: water entered through a hatch, the structure tilted, the top of the net dipped under the waterline, and 15,805 fish escaped, about 1.5% of almost 1 M (salmonbusiness.com/salmar-satisfied-after-only-15805-salmon-escape-from-worlds-first-offshore-fish-farm/, Jan 24, 2019).
  - RAS: more than 100 H2S incidents in Norwegian smolt and post-smolt RAS since 2016, almost all in fixed-bed filters. The same piece says early projects never exceeded 50–60% of design capacity and recommends ≤ 80 kg/m³ (salmonbusiness.com/why-do-some-land-based-salmon-ras-succeed-while-others-fail/, Mar 25, 2025; an industry opinion piece by the RASLogic founder).
  - Use these numbers for the commissioning ramp shown in the build preview and for the choice of biofilter.
- **FD4. Losses are bounded and recoverable.**
  - One incident costs a share of the stock or one cycle, not the company, unless the player has compounded neglect.
  - D16's warning ladder applies.
  - JWE2 was criticised for escalation that came too fast.
- **FD5. No wealth-scaled hazard** (RimWorld). Risk comes only from physical exposure: Hs, temperature, grid, filter type and maintenance state.
- **FD6. Every hazard probability can be inspected through its drivers** in nested tooltips (U3-04). This answers Cities: Skylines II's opacity problem.

### 7) Late-game goals and prestige

- **LG1. Flagship projects**, like Anno monuments, Cities: Skylines II signature buildings and Satisfactory's Project Assembly:
  - an offshore cluster in a 2027-type area;
  - a full land campus. Salmon Evolution targets 31,500 t HOG across phases; Phase 1 is about 7,900–8,000 t HOG (fishfarmingexpert.com Q1-2024 update, 2024).
  - a Havfarm-class flagship.
- **LG2. A loop that continues after the flagship** (Factorio 2017): a capacity market, the next tender round, export markets.
- **LG3. Prestige on several axes, paying no cash (D16)**: escape-free years, the lice record, CO2 per kg and welfare. These are medals and Steam achievements, as with Two Point Campus stars.
- **LG4. Per-type challenge scenarios with parody names**, like Two Point campuses and Against the Storm biomes: a "Frohavet pilot" (offshore under development licences) and a "Harøy ramp-up" (land commissioning).
- **LG5. Company history (Legends) feeds the end screen (U2-03).**

---

## D. Spec cards per farm type

| Type | Fantasy | What limits you | Unique verbs | Unique failure: tell → counter-play | Instrument | Gate |
|---|---|---|---|---|---|---|
| Open net pen (exists) | Fjord farmer | Lice, temperature, O2, MTB | Delouse, skirt, fallow | Lice breach, bloom, jellyfish → count, treat, harvest early | Pen card, lice chart | Start |
| Semi-closed / closed in sea | Shield the fish | Pumps, energy, water exchange | Pump rate, intake depth | Pump or power outage drops O2 within hours → backup generator, diffusers, open skirt | Pump/O2 strip | Act 2, cheap step |
| Offshore semi-sub (Ocean Farm 1 class) | Steel island in open sea | Weather windows, logistics, crew | Schedule operations into windows, ballast/draft, crew rotation, DP vessel booking | Missed window halts feeding or delousing; ballast/hatch chain leads to escape → pre-fill silos, deballast, inspection procedure | Weather-window board, MRU, silo days | Development-licence tender, then area competition |
| Ship farm (Havfarm class) | One hull, six pens (Ship-Technology) | Shared systems on one hull | Redundancy upgrades | A single-system fault hits every pen → redundancy, spares | Same board plus a systems tree | Same tender, higher CAPEX |
| Land RAS / hybrid flow-through | Salmon factory on the granite | Capital, chemistry, power | Tank allocation and grading, temperature vs maturation, density, reuse vs intake, filter type | H2S/biofilter crash, grid loss, early maturation, commissioning shortfall → flush, cut feed, generator test, light regime | Tank board | Plot, county permit and discharge permit; needs smolt competence |

---

## E. Anti-patterns to avoid

1. A tier that only multiplies numbers, or a research node that grants a global percentage.
2. An opaque economy or invisible safeguards (Cities: Skylines II, 2024).
3. Everything at once in the opening hours (JWE2 per GameGeeker; Workers & Resources complexity).
4. Chaos that escalates faster than the player can respond, or storms with no warning (JWE2 per Gamerbraves, 2021).
5. Hazard that scales with wealth (RimWorld).
6. A flagship that ends the game ("And now what...", Factorio FFF-187, 2017).
7. UIs nested in sub-menus (Planet Coaster 2, 2024).
8. Construction micromanagement as tedium. A Workers & Resources player spent "an entire afternoon to build half"; a Frostpunk 2 reviewer found clearing ice "slightly tedious".
9. Danger whose reward is not worth it (Against the Storm forbidden glades): offshore must pay off in the sweep.
10. A single gimmick that frustrates (Gleba complaints).
11. A new region with art but no depth (Anno 117 Albion, 2025).
12. Pay-and-wait building with no site.
13. Biology compressed until it is meaningless (AquaKultor: full size in under two minutes).
14. A photo mode bolted on after launch (Anno 117, 2026).

---

## F. Where today's code breaks these rules (read-only findings)

- `src/LakskongenSim/Core/ResearchSystem.cs:536-560`
  - `deep-sea-farming` and `land-based-ras` are Tier 1 with no prerequisites, cost 0.9–1.2 M NOK and grant global effects (LiceReduction 0.10, DiseaseResistance 0.14).
  - Breaks U2, U4 and D2.
- `src/LakskongenSim/Core/SimulationEngine.Facilities.cs:20-70`
  - `BuildRASFacility(name, scale)` and `BuildOffshoreFarm(name, type)` take no site, licence or permit: pay, then wait.
  - Breaks U2, U3, B1 and D4.
- `src/LakskongenSim/Farm/OffshoreFarm.cs`
  - `:39` uses a flat `SeaLicePressure = 0.3`.
  - `:52` and `:134` cause damage only when Hs exceeds 12 m, so failure exists only in the extreme tail and there are no per-operation weather windows.
  - `:29` sets "Ocean Farm 1: 6 pens"; verify this, because the sources describe one 250,000 m³ structure and six pens belong to Havfarm.
- `src/LakskongenSim/Farm/RASFacility.cs`
  - `:151` floors O2 at 5.0 and `:164` floors the biofilter at 0.70, so no chemistry cascade can happen and there is no drama (FD1–FD3).
  - It uses 12 tanks of 3,000 m³, while Salmon Evolution uses 5,000 m³ tanks (fishfarmingexpert.com first-smolts article, 2022).
- `godot/scripts/ui/OffshorePanel.gd:133-137` and `RASPanel.gd:117,151`
  - These are list rows with a percentage and a single pH/temperature/O2 string: no site, no 3D presence, no instrument.
  - G5-02 binds assets only in Phase 5.

---

## G. Roadmap fit (proposed items for the merge captain)

| Proposed item | What it covers | Depends on |
|---|---|---|
| FT-S1 (sim, L) | Facility pathways: site-bound offshore and RAS builds, the development-licence tender event with milestone conversion, and the land permit track | S2-01, S2-02, S4-08 |
| FT-S2 (sim, L) | Distinct risk models: offshore weather windows per operation (on the S1-02 storm chain) and the ballast/hatch incident chain; RAS chemistry and power cascade with commissioning ramp; semi-closed pump/O2 | S1-01, S2-05, S4-03 |
| FT-U1 (ux, L) | Big-project build flow: site lens, ghost placement, comparison card, phase timeline. Extends U2-01's `BuildSiteDialog` | U3-01, G3-03 |
| FT-U2 (ux, M) | Weather-window board and tank board on the U3-03 panel kit; replaces the `OffshorePanel` and `RASPanel` rows | U3-03 |
| FT-G1 (graphics/assets, XL) | Extends G5-02 with construction-stage meshes, the tow-in and first-stocking camera sequence on the G1-03 rig, and photo mode at launch | — |
| FT-Q1 | Adds offshore-first and RAS-first families to Q4-01 | — |

- **Demo (D9):** teaser only (the rival hull, the locked card).
- **File conflicts:** the facility sim files are `SimulationEngine.Facilities.cs`, `OffshoreFarm.cs`, `RASFacility.cs` and `ResearchSystem.cs`. The UI files `OffshorePanel.gd` and `RASPanel.gd` are already scheduled for rebuild in U3-03, so FT-U2 must follow U3-03.

---

## H. Gaps and confidence

- **No primary source found** for any Two Point GDC talk on readability, or for detailed Railway Empire 2, Transport Fever 2, Captain of Industry or Fishing: Barents Sea critiques. Those rows rest on reviews, wikis or thin evidence.
- **Planet Zoo 2** launched 2026-10-13, so its row describes design intent from dev diaries, not reviews.
- **Aggregators** (vaporlens, SteamPulse) are machine summaries, and the SteamPulse crawl date is unknown.
- **No sourced numbers** were found for semi-closed pens.
- **Havfarm length** differs between sources: 385 m stationary (2020) versus 430 m in the original design.
- **Ocean Farm 1 height** differs between sources: 68–70 m.
- **Offshore tender details** (the area allocation regulation and deadlines) were not yet published as of these sources (iLaks, Jun 2026).
