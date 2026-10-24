# Lakskongen farm-types spec (F2): land-based and offshore farming, revision 2

_Lead design and technical direction. Written 2026-10-14 against `integration/wave1` at `90a0cf0` (wave1/gfx-3 is merged). This phase was read-only: no repo file was changed._

- **Item ids:** F2-01 to F2-35. "F2" means the second generation of the facility model.
- **What this spec replaces in the roadmap:**
  - It replaces roadmap item G5-02 entirely.
  - It takes over the post-smolt half of S4-11.
  - It removes the 07d offshore rebuild from U3-03; F2-33 replaces that panel.
- **Changes from revision 1:**
  - Golden traces come first.
  - The site-kind audit is explicit.
  - There is a reachability contract.
  - Warning deadlines now work on the real clock.
  - Every periodic task has a standing order (Driftsplan).
  - Three player-facing families replace the seven cards.
  - The offshore regulatory model is corrected, and post-smolt runs on seawater.
  - Energy and oxygen are now built from their physical parts.
  - Gray-box read tests run before any art budget is spent.
  - The schedule is honest.

**Citation keys**
- `[L:x]`: land dossier key x.
- `[O:x]`: offshore dossier key x.
- `[V:n]`: a source verified by the realism critic on 2026-10-14 (list at the end of §2).
- `[UX-x]`: a rule from the game-design research (UX-U1…U6, UX-D1…D6, UX-B1…B5, UX-F1…F4, UX-O1…O6, UX-FD1…FD6, UX-LG1…LG5). This avoids confusion with the roadmap decisions D1–D27.
- `[code]`: a value in the current repo.
- **[GB]**: a game-balance value chosen here with no direct source. It is a starting point, and the acceptance test named next to it decides the final value. **[GB/proposal]** marks a value that was a government consultation proposal, not adopted law.
- "(derived)": arithmetic on sourced numbers.

---

## 1. Vision

The owner wants land-based and offshore farming in the game, alongside the open net-pen sites. These farm types should match a top simulation for depth and a top game for looks. They must also be **reachable in a normal career**, readable at the default camera, and calm to operate.

**The fantasy ladder.**
- You start as a fjord farmer.
- Your first step off the water is a **post-smolt module** on the river delta. It grows big, robust fish for your own pens.
- From there you can become a **salmon factory on the granite**: a campus of round tanks on a blasted shore pad, with an orange pipe bridge running from the quay.
- Or you can run a **steel island in the open sea**: a safety-yellow ring 110 m across, with an orange service vessel alongside.
- Both sit where the fjord meets the sea. The default view (G1-03) already looks down the fjord toward the mouth, so the player's world grows outward into the background of the home screen. Camera bookmarks (§4) take the player there in one key press.

**Players see three families, not seven cards.** These are **Sjø** (open pens, with containment upgrades on the pen card), **Land** and **Hav**. Variants sit second inside each family:

| Family | Variants |
|---|---|
| Sjø | Åpen merd → semi-lukket / lukket (upgrades) |
| Land | Postsmoltmodul → Landanlegg (hybrid) → Landanlegg (RAS, stage 3) |
| Hav | Havmerd (OF1-class) → Skipsmerd (Havfarm-class, stage 3) |

**Each family changes what limits the player. None of them is just "more output"** [UX-D1].

| | Sjø (open net pen) | Land (post-smolt / HFS / RAS) | Hav (OF1-class / Havfarm-class) |
|---|---|---|---|
| You pay for capacity with | Licences: auction 180–310 k NOK/t MTB [V:1, L:L6] | Concrete and kilowatt-hours. The land permit is fee-free but capped by discharge [L:L1–L4] | Steel and vessels, plus free development licences that you must convert later, worth ≈ 238 M each at 2024 auction prices (derived [V:1]) |
| What limits you | Lice, temperature, O₂ | Capital, pump capacity, heat, power and oxygen supply, competence | Fish-handling windows, logistics, crew, alarm load |
| Signature verbs | Delouse, skirt, fallow | Choose a water strategy (reuse, intake blend, setpoint), plan harvests against the price curve, sell or transfer post-smolt | Book operations into forecast windows, decide when to tow, choose a shakedown, keep the unit handleable through winter |
| Signature dread | A lice breach | The 40 minutes after a blackout; the cohort that stops growing | Day 40 with lice over the limit, no 2-metre window on the fan chart, and MTB headroom running out |
| Payoff, honestly | Cheap kilos | No lice, about 97% survival, harvest when the price is best. Early weights run small (3.5 kg HOG [V:17]) and the first years run below capacity | When it works (OF1): mortality ½ to ⅓ of the area average and no delousing. When biology fails (Havfarm gen 1): about 19% loss. When you cannot reach the fish (AOF): you lose the site but keep the licences [O:R7, O:R26, V:5] |

**Pillars**
1. **Distinct, not bigger.** Each family brings at least one verb and one failure class that no other has [UX-D2].
2. **Show it before you commit.**
   - Ghost placement is free.
   - One comparison view uses the same axes for every variant, shown as aligned bars with pips.
   - It shows the median case *and* the bad case.
   - It also shows refusal odds, the cash runway and the binding constraint in your own company [UX-D3, UX-B1, UX-B2].
3. **Building is a show you can watch at any speed.**
   - Construction phases are visible.
   - Payoff beats are presentation events with their own real-time length, independent of sim speed.
   - The first completion gets one camera moment, which opens with a build timelapse [UX-B3, UX-F1].
4. **Telegraph, counter, recover, explain, in human time.**
   - Every warning that has a counter-verb and a deadline slows the game to 1× and then pauses before the deadline.
   - Short real-world events (blackouts, ballast deviations) are countered by standing orders and procedures, not by fast clicking.
   - An incident report shows the causal chain afterwards.
   - Hazards come from physical exposure, never from wealth [UX-FD1–FD6].
5. **Policy first, exceptions second.**
   - Every periodic task has a Driftsplan default: resupply, crew changes, generator tests, reports, grading and purges.
   - The boards show "neste planlagt" and the exceptions.
   - A competent player issues ≤ 3 commands per unit per in-game month (F2-17).
6. **A portfolio, not a replacement.**
   - Post-smolt feeds the sea sites.
   - Hav relieves the fjord.
   - Land sells lice-free kilos when the price is right.
   - The open-pen game stays central [UX-D5].
7. **Cool and calm.**
   - The environment palette stays cool. Saturated colour appears only on equipment and signals: the yellow hull, the orange bridge, the orange substation and the orange service vessel.
   - Alarms are rare and mean something, and that is measured (F2-17 #5) (owner brief).
   - The hero moods are dusk for land (ref L2) and low winter sun for offshore (ref O3).

---

## 2. Farm-type matrix

The columns use the player-facing Norwegian name in brackets. All seven variants are modelled. Three families ship first, and RAS and Havfarm-class follow in stage 3 (§7.5).
- **Open**: open net pen ("Åpen merd")
- **Semi/closed**: semi-closed or closed sea pen ("Semi-lukket / lukket merd"), as an upgrade on a Sjø site
- **OF1**: semi-submersible, 110 m ("Havmerd")
- **Havfarm**: ship-shaped, 385 m ("Skipsmerd")
- **HFS**: land, hybrid flow-through ("Landanlegg (hybrid)")
- **RAS**: land, full recirculation ("Landanlegg (RAS)")
- **Post-smolt**: seawater/brackish post-smolt module, the bridge to the sea ("Postsmoltmodul")

No real company names appear in the game (D17). "OF1-class" and "Havfarm-class" are code names only.

**Normalisation rule for the player-facing card.** Money is shown as NOK/kg HOG. Capacity cost is shown as **NOK per tonne of yearly HOG capacity, including the licence**, using 1.2 t HOG per year per t MTB (derived from an 18-month cycle at 25 kg/m³ **[GB]**). The catalogue computes every card value from the registry (F2-04) and, after F2-17, from measured probe medians.

### 2A. Economics and capacity

| | Open | Semi/closed | OF1 | Havfarm | HFS | RAS | Post-smolt |
|---|---|---|---|---|---|---|---|
| **Capex** | Site 6 M [code]. Licence at auction 180–310 k NOK/t (2018 ≈ 195 k, 2020 ≈ 220 k, 2022 leftover capacity ≈ 182 k, 2024 ≈ 305 k) [L:L6, V:1]. Traffic-light growth is sold at fixed prices of 156 k/t (2020) and 170 k/t (2024) [V:2]. ≈ 270 k NOK per t HOG/yr including the licence (derived) | Semi-closed 50 M per unit [O:R44]; closed 60 M per unit **[GB]** | 720 M contract + 157 M first-cycle fixes = 877 M [O:R4]. Conversion 8 × 13.8 M ≈ 110 M (2026) [V:2]. ≈ 132 k NOK per t HOG/yr including the licence (derived) | ≈ 1,000 M [O:R26]; "havgående" variant 1,400 M **[GB]** | ≈ 262 k NOK per t HOG/yr (derived [L:R1]) → 2.07 bn per full lot of 7,900 t. **Pilot lot** (4 + 1 tanks) 0.90 bn, expandable to a full lot for +1.25 bn **[GB]**. Pad blasting 40–120 M by plot **[GB]** | ≥ USD 30 k/t [L:C1] → 320 k NOK/t at FX 10.7 **[GB]** → 1.38 bn per 4,300 t HOG hall (derived) | 110 M per 2-tank module of 480 t/yr (≈ 1.1 M fish at 450 g) **[GB, ≈ 230 k NOK per t/yr]**; up to 4 modules per site |
| **Opex/kg** | 64.68 NOK/kg round ≈ 78 NOK/kg HOG (2023) [O:R57] (derived) | Aquatraz 36.47 vs 38.78 NOK/kg in the control pen (2021) [O:R44]. +3.87 kWh/kg [O:R50]. Insurance 0.53 vs 0.21 NOK/kg [O:R44] | 1.3× coastal (Menon pilot 56 vs ~43) [O:R37]; labour/kg 0.5× [O:R37]. Service-vessel charter 80–200 k/day, default 140 k **[GB]** | As OF1, plus silage logistics [O:R28] and shore power | Farming cost 70.7 NOK/kg HOG at ~50% utilisation (Q2 2026). Target 60–65 by mid-2027, ≈ 53 at scale [L:R1, L:R9]. Depreciation ≈ 21/kg at low volume (derived [L:R1]). 6.3–6.8 kWh/kg LW [L:R2] | HFS +5–15% **[GB]**; 4–10 kWh/kg [L:R7, L:S3, L:S4] | 3.4–5.4 kWh/kg [L:T1, L:S3]. Sale price 90 NOK/kg live **[GB]** |
| **Capacity** | 780 t MTB per licence; ≤ 200 k fish per pen; ≤ 25 kg/m³ [S2-01; O:R23] | Same MTB. Red-zone credit ×1.03 semi-closed, ×1.06 closed-float [code]. More than 25 kg/m³ only by dispensation (closed designs reach 50 [O:R46]) | 6,240 t MTB (8 × 780) [O:R4]. 250,000 m³ in 3 zones [O:R1, O:R6]. 1.0–1.5 M fish [O:R3, O:R8]. The 200 k-per-pen rule is waived by the unit's dispensation (mechanism **[GB]**; OF1 held 1.04 M fish in 3 zones [O:R3]) | 10,000 t MTB (13 × 788 = 10,244) [O:R22, O:R23]. 6 × ~69,000 m³ [O:R23, O:R25] | Full lot: 12 × Ø28 m tanks of ≈ 5,000 m³ + 2 pre-grow-out tanks [L:R1, L:R3, L:R5]. 7,900 t HOG/yr [L:R8]. ≤ 85 kg/m³ [L:R2]. Up to 4 lots on pad_a **[GB]**. The permit caps standing biomass and annual production [L:R14] | 16 × 2,500 m³ **[GB]** × 120–140 kg/m³/yr [L:R6] → 5,200 t LW ≈ 4,300 t HOG/yr (derived) | 2 × Ø16 m tanks (3,200 m³) per module at ≤ 80 kg/m³ [L:S1], production index 150 kg/m³/yr **[GB]**. Reference plant: 3,000 t/yr of 750 g fish [L:T1] |
| **Build** | Same day [code] | 60-day refit **[GB]** | 18 months at the yard [O:R1, O:R33] + 11-week voyage [UX: FFE 2017] + mooring and certificate ≈ 45 d → ≈ 670 d (derived; code says 1,095) | 29 months to first fish → 880 d [O dossier §7] | Zoning 1–2 years if the pad is not pre-zoned **[GB, L:R13]**. First tanks at 22 months, complete at 30 [L:R1] | 30 months + 60-day biofilter maturation **[GB]** | 548 d [code] + 45-day biofilter maturation **[GB]** |

### 2B. Biology and risk

| | Open | Semi/closed | OF1 | Havfarm | HFS | RAS | Post-smolt |
|---|---|---|---|---|---|---|---|
| **Cycle / growth** | 100 g → 4.5–5.5 kg in 14–18 months (S2-03 band) | Post-smolt to ≥ 700 g [O:R49]. Aquatraz harvest 5.36 vs 4.83 kg [O:R44] | 233 g → 5.39 kg in one year [O:R3, O:R11]; biological FCR ~1.02 [O:R2]. AOF: 2 → 4.4 kg, FCR 1.22 [O:R17] | Stocked at 1.35–2.9 kg [O:R22] | **Achieved:** 3.5 kg HOG (4.2 LW) average in Q2 2026, 2026 guidance cut for weak biomass growth [L:R1, V:17]. **Plan:** 400 g → 5.2 kg LW in 8–9 months [L:R1]. FCR 1.02–1.1 [L:A3, L:G2]. In game: competence 0.4 gives the achieved result, and 1.0 reaches the plan **[GB]** | 2.1–4.4 kg HOG achieved [L:P2, L:N1]; FCR 1.1–1.3 [L:R7, L:S9] | 80 → 300 g in ~82 d [code]; target 300–750 g [L:T1] |
| **Survival** | 14.2% **annual** mortality risk (2025), ≈ 18% per 16-month cycle (derived); PO range 9.4–20.1% [V:14, O:R55] | 97.9% Aquatraz [O:R44]; 99.2% Donut [O:R46]; 98.6% FishGLOBE [O:R47] | Good case 93–96% (4% before the harvest-zone fault, 7% total) [O:R3]; AOF 96.4% [O:R17]. Bad case: see Havfarm gen 1. Compared live with the game's own PO mortality, never as a fixed ratio | Gen 1 ≈ 81% (≈ 25% loss in wells 3–6) [O:R26]; gen 2 97.2% [O:R22] | ~97% [L:R2]; flow-through 97.5–98.0% [L:A7, L:A3] | 97–99.7% [L:P1, L:P2, L:B5]. Bad case: single events killed 250–500 t [L:B1, L:B6] | 97% [code]. FishGLOBE post-smolt 1.4% vs 4.9% in net pens [O:R47] |
| **Lice** | External pressure ×1.0 | Skirt: 18% on one pen, 54% site-wide [O:R53]. Exclusion 0.85 semi-closed, 0.95 closed [code] | **External** pressure ×0.10–0.15 **[GB]**, fitted to 0.01–0.06 adult females and 0 treatments in 2 cycles [O:R7, O:R8, O:R11]. The unit's own lice still reproduce, and units in one area infect each other | Peak 0.49 adult females, treated [O:R22] → external ×0.30 **[GB]** | 0 (intake disinfection [L:L2]) | 0 | 0 |
| **Escape** | ×1.0 [code] | ×0.2 semi-closed, ×0.05 closed [code] | Event-driven: the 2018 tilt released 15,805 fish (~1.5%) [O:R10]; net tears [O:R18, O:R60b] | Event-driven: shared-system fault **[GB]** | Outfall-screen event only (2018 standard [L:L7]) **[GB]** | Same as HFS | Same as HFS |
| **Footprint** | Untreated discharge; 0.07 kWh/kg [O:R50] | 3.87 kWh/kg [O:R50]; 30–85% of sludge collected [code] | Dispersal in strong current; vessel fuel (Scope 1) | Shore power with backup gensets [O:R23]; silage [O:R28] | 6.3–6.8 kWh/kg LW [L:R2]. Discharge permit for N, P and particles [L:L4]. Sludge 0.15–0.20 kg DM per kg feed [L:S7] | 4–10 kWh/kg; NO₃ counts toward the permit | 3.4–5.4 kWh/kg; seawater sulphate drives H₂S risk [V:6, V:7] |

### 2C. Licence, unlock and play

| | Open | Semi/closed | OF1 | Havfarm | HFS | RAS | Post-smolt |
|---|---|---|---|---|---|---|---|
| **Licence / permit path** | Growth-licence auction per PO [code, S2-01]; traffic light [O:R39] | Upgrade an existing site. More than 25 kg/m³ needs a dispensation case | **Development licences** [O:R4, O:R40, V:2, V:3]: free, ≤ 15 years, project target criteria and half-yearly reports, no resource rent until converted. **Conversion:** all criteria met; fee NOK 13.8 M per licence (Jan 2026; NOK 10 M indexed from Nov 2015); any growth bought on development licences is paid at the fixed prices; payment within 90 days or the commitment lapses; refusal can be appealed (Havfarm was refused in 2021 and granted in 2022 [O:R22, V:12]). Converted licences must be **placed in a PO** before use, and only then fall under the traffic light and resource rent. **Stage 3:** the offshore-area competition [O:R35, O:R36, V:15]; its 25-year term and its 1 t → 2 t capacity move are **[GB/proposal]** | Same as OF1; 13 licences [O:R22] | Zoning plan [L:R13]. Fee-free land permit capping standing biomass and production [L:L1–L3, L:R14]. Discharge permit [L:L4]. Sea-works permit (NOK 44,300) [L:R12]. Intake disinfection and biosecurity approval [L:L2, L:L5]. Grid connection queue **[GB]**. A moratorium is possible (2022–25 freeze [V:13]) | Same as HFS | Same as HFS, at module scale |
| **Unlock** | At start | After the first harvest | Research `deep-sea-farming`, then the development-licence round. The round opens after **1** completed sea cycle with 12 escape-free months **[GB]**. Consortium entry: 35% equity **[GB]** | Stage 3: after your own OF1-class unit completes a cycle (or in "Havpiloten") | Research `land-based-ras` + an operating post-smolt module + a zoned pad. The pilot lot comes first | Stage 3: after one HFS cycle; any pad | Research `land-based-ras` + the first harvest |
| **Signature failure → counter-play** | Lice breach → count, treat, fallow, harvest early | Pump or power failure: O₂ falls within hours inside a closed bag → genset, diffusers, emergency flush | **Cannot handle fish when needed.** AOF lost its site after up to 3 months without delousing or harvest [V:5]. Counters: an offshore wellboat or DP charter, handling before winter, choice of plot. **Tilt** from a hatch and ballast chain under alarm overload (> 20,000 points) [O:R3, O:R10]. Counters: alarm review, hatch procedure, fixing teething defects, shakedown | One shared-system fault hits all 6 wells. Mixed smolt sources bring winter sores (~19%) [O:R26]. Counters: redundancy, single-source smolt (your own post-smolt) | **Grid outage:** O₂ < 5 mg/L in ~40 min (15 with stressed fish) [L:F2]. Counters: a tested genset (23 s [L:F3]), diffusers, a second grid feed (lead time). **Growth shortfall** [V:17]. Counters: competence, CO₂ control, feed-plan discipline | **H₂S:** chronic from 1 µg/L [V:8], acute ~60 µg/L [L:F6], 250 t lost [L:B6]. Counters: moving-bed filter, cleaning, nitrate buffer, flush. **Off-flavour** → purge | **H₂S** in seawater loops [V:6, V:7]; **desmoltification** at low salinity; early maturation. Counters: salinity, photoperiod, ≤ 12 °C [L:S8] |
| **What the player monitors** | Lice vs limit, sea temperature, O₂ (pen card) | Pump state, O₂ in vs out, kWh/day, grid headroom | Weather-window board, days without a handling window, silo days, crew clock, alarm load, MTB headroom, net and structure "known as of" | As OF1, plus the systems tree | Lot board with the binding constraint (O₂, CO₂, TAN, temperature, density), energy/kg, LOX days, growth vs plan, harvest plan vs price curve | Lot board plus biofilter, H₂S index, off-flavour | Weight vs target, salinity, tier preview, destination MTB, sell vs transfer |

**Sources cited above** (the full lists are in the dossiers):
- **Land:**
  - Salmon Evolution: L:R1 Q2-2026 report; L:R2 annual report 2025; L:R3/R5/R6 tank geometry; L:R7 Rabobank via SalmonBusiness; L:R8 Q4-2022; L:R9 SalmonBusiness 2026; L:R12 sea-works permit 2024; L:R13 Hustadvika plan 201905; L:R14 iLaks 2019.
  - Other plants: L:A1/A3/A4/A7 Andfjord; L:B1/B4/B5/B6 Atlantic Sapphire; L:P1/P2 Proximar; L:N1 Nordic Aqua; L:G2/G4 Gigante; L:T1 Tytlandsvik.
  - Science: L:S1/S2 Nofima; L:S3 NTNU; L:S4 Aquacultural Engineering 101; L:S5 GSA O₂; L:S6 LOX; L:S7 Scanship; L:S8 Frontiers 2024; L:S9 Freshwater Institute; L:S10 USDA; L:S11 GSA off-flavour; L:S12/S13 nitrate.
  - Failures, costs and statistics: L:F2/F3/F5/F6; L:C1 Undercurrent 2025; L:E1 SSB.
  - Regulation: L:L1–L5, L:L6 MTB auction note, L:L7 escape regulation 2018.
- **Offshore:**
  - OF1: O:R1 fact sheet; O:R2/R3 half-year reports; O:R4 conversion 2020; O:R6 NTNU thesis; O:R7 Halse 2022; O:R8/R10/R11 press.
  - Exposure and AOF: O:R15 Heasman 2024; O:R17 AOF report 2024; O:R18/R19 AOF withdrawal and conversion.
  - Havfarm: O:R22 conversion 2022; O:R23/R25/R26/R28 hull, pens, gen-1 losses, silage.
  - Regulation and economics: O:R33–R36 offshore regime; O:R37 Menon 131/2023; O:R38/R63 resource rent; O:R39 traffic light; O:R40 development licences.
  - Closed pens and lice barriers: O:R43 HI 2025-30; O:R44 Aquatraz; O:R46 Marine Donut; O:R47 FishGLOBE; O:R49 Mowi closed pens; O:R50 grid; O:R53 skirt.
  - Statistics and other: O:R55 VI mortality; O:R57 key figures; O:R58 Kystverket IALA; O:R60b net damage.
- **Verified 2026-10-14 by the realism review:**
  - V:1 regjeringen.no results of the 2024 auction: 17,330 t for NOK 5.29 bn ≈ 305 k/t.
  - V:2 Fiskeridirektoratet decision 25/17287 (13.03.2026, AOF). Fee NOK 13,804,348 per licence; growth paid at the fixed prices of 156 k (2020) and 170 k (2024); 90-day payment; placement in a PO. Arithmetic: 8 × 13.8 M + ≈ 19 M growth ≈ the total of 130.3 M.
  - V:3 fiskeridir.no, resource rent tax on aquaculture: development licences are outside its scope.
  - V:4 BAHR newsletter on the rent tax.
  - V:5 SalmonBusiness 2025: regulator shuts down the AOF site; sector permit withdrawn from 1 Jan 2026; unreachable for up to 3 months.
  - V:6 FFE/NIVA 2018: sulphate ≈ 2,700 mg/L in seawater vs ≈ 2 mg/L in fresh water.
  - V:7 DTU sulphate-removal project for post-smolt RAS.
  - V:8 Nofima publication 2210049: 4 weeks at 1 and 5 µg/L H₂S gave 4.7% and 16% mortality.
  - V:9 DTU 2023 (S014765132301401X): nitrate buffers sulphate reduction.
  - V:10 SalmonBusiness: Salmon Evolution 95 m intake, stable ≈ 10 °C.
  - V:11 World Fishing: PTG Frionordica heat recovery, COP above 12.
  - V:12 iLaks: Nordlaks refused, then granted on appeal.
  - V:13 iLaks 2025: new land rules; 2022–25 freeze.
  - V:14 Veterinærinstituttet Fiskehelserapporten 2025: PO6 fell from 22.3% to 14.6%.
  - V:15 Royal resolution 27.02.2026, plus the Statsforvalteren Trøndelag consultation reply (25-year term and biomass bonus were proposals).
  - V:16 FFE 2023: offshore left out of the rent tax.
  - V:17 Salmon Evolution Q2 2026 release (placera.se): 3.5 kg HOG; 2026 guidance cut from 7,000 t to 6,000 t.
- **UX:** rules UX-U1…UX-LG5 from the UX research return.

---

## 3. Simulation model

### 3.1 Architecture decisions

0. **Golden traces before any code moves (F2-01).**
   - The existing determinism suites compare run A with run B. None of them compares the code before a change with the code after it.
   - F2-01 therefore records per-day numeric traces on the pre-change commit.
   - Every F2 sim item must then match those goldens on all shipped scenarios. The exceptions are the three items that change fjord behaviour on purpose: F2-04 narrows the research effects, F2-10 fixes containment, and F2-13 fixes the smolt-tier exploit. Each of those re-records the goldens with a diff report.
1. **Two production families, one fish list.**
   - **Everything in the water is a `SeaSite`.** An offshore unit is a `SeaSite` with `Kind = SiteKind.Offshore` and an `OffshoreUnit` component.
     - `OffshoreUnit` is a **new** class built from the live members of `OffshoreFarm`: `FarmType`, `StructuralIntegrity`, `CurrentWaveHeight`, `DaysSinceCrewRotation`, `CrewRotationDays`, `CAPEX`, `EmployeeCount`, `CarbonTracker`.
     - Where the meaning of a member changes, it is renamed and migrated: `MaxWaveHeightM` (12 m "max wave") becomes `DesignHsM` (100-year Hs, 5.5 m for OF1-class).
     - The unit's zones are S2-01b `Pen`s on the site: 3 for OF1-class, 6 for Havfarm-class.
     - Net state reuses `SeaSite.NetIntegrityPct` (0–100).
     - Being a `SeaSite` puts offshore fish inside site lice, wellboats, escapes, ISA/S4-02, sludge and S2-01 MTB enforcement. The traffic light is handled through a hook so that offshore fish cannot dilute the zone mean (§3.3).
   - **Everything on land is a `RASFacility`**, which keeps its name for save, bridge and test compatibility; players see "Landanlegg".
     - It reuses `RecirculationRate` as the reuse fraction r, `TargetWaterTemp` as the setpoint, `BiofilterEfficiency` as biofilter health, and `TankCount`/`TankVolumeM3` to generate legacy tanks.
     - It gains a plot, lots, physical tanks, a water system, power, permits and competence.
   - **`OffshoreFarm` and `PostSmoltFacility` become load-only DTOs.** `OffshoreFarm.Pens` stays serialized as migration input. The migration empties both lists. Post-smolt sites become `RASFacility` with `Template = PostSmoltRas`.
2. **Every non-hatchery population resolves to exactly one unit.**
   - `UnitResolver.Resolve(FishPopulation)` returns a `SeaSite` or a `RASFacility`.
   - Land fish already carry `SiteId` (`FacilityProduction.cs:241`); F2-03 adds their `ZoneId` from the plot.
   - Populations created by `Hatchery.OnNewCohort` (`SimulationEngine.cs:408-419`) have no site by design and are listed as exempt.
3. **Hooks with neutral defaults go in first; the lanes fill them later.**
   - F2-02 moves the facility logic out of the hot files. It installs hook methods in `Core/SimulationEngine.FarmTypeHooks.cs`, which is frozen after F2-02. Each hook dispatches to `Farm/Land/LandHooks.cs` (land lane) or `Farm/Offshore/OffshoreHooks.cs` (sea lane), so the two lanes never edit the same file.
   - The proof that the move is pure is the F2-01 goldens.
4. **A site-kind audit, not hope.**
   - About 99 `SeaSites` consumers in the sim and 58 in the bridge each get a decision: include, exclude or hook. The full table is in `docs/run/evidence/f2_03_sitekind_audit.md`.
   - Examples:
     - Offshore is excluded from `MaxSitesPerZone` and from the fjord `NorwegianEvents` site cards except ISA.
     - Offshore is included in escapes, harvest, wellboat jobs, the advisors' risk view and `SustainabilityManager`.
     - The bridge's `GetSeaSiteDetails` gets a `kind` field and `includeOffshore = false` by default. Its fjord consumers (`FishPenManager`, `FeedBargeVisualManager`, fjord lists) therefore stay untouched, and the index-keyed pen slots do not shift.
5. **Legacy verbs become test paths.**
   - `BuildOffshoreFarm` now creates an Offshore `SeaSite` on the first free compatible plot.
   - `BuildRASFacility` creates a land site on the first free pad without permits.
   - `BuildPostSmoltFacility` creates a post-smolt module.
   - All three skip permits and remain the scenario-start and test path.
   - Once F2-31 removes the build buttons from `RASPanel`/`OffshorePanel`, a contract test asserts that no GDScript outside QA calls them.
6. **No polymorphic lists; every state change is additive with defaults.**
7. **Driftsplan.**
   - Every periodic task is a policy evaluated in a fixed `Driftsplan` phase.
   - A policy issues the same engine commands a player would, and never draws RNG.
8. **Honest numbers.**
   - Forecasts are the exact conditional distribution of the model.
   - Catalogue values are either sourced registry values with their source key, or measured probe medians (F2-17).
   - Nothing on a card is invented.

### 3.1a The holding seam the hooks hang off (landed in wave 2, `wave2/sim-a-1`)

Before F2-01, the roadmap's S2-02 and S2-05 each built a seam for "the water and exposure whatever holds the fish gives it". Wave 2 merged them into one, and the F2-02 hooks in §3.3 hang off it.

- **One holding enum.** `Environment.HoldingKind { SeaPen, Offshore, LandTank }` names what holds a population. `Biology.FishHolding` keeps S2-05's spellings (`SeaPen`, `OffshoreStructure`, `LandBasedTank`) as constants of that enum. In F2 terms: a `SeaSite` with `Kind = Sea` holds `SeaPen` fish, a `SeaSite` with `Kind = Offshore` (and today's `OffshoreFarm`) holds `Offshore` fish, and a `RASFacility` (every land template, post-smolt included) holds `LandTank` fish.
- **One resolver, once per simulated step.** `SimulationEngine.HoldingOf(population)` returns a `PopulationHolding` (kind, ledger id, zone, sea site, land facility, offshore farm).
  - During a step it answers from a `HoldingIndex`, built at the start of the step from the facility, site and population lists with the precedence land tank, then offshore pen, then sea pen. The index holds references only, so values that change during the day are read live.
  - Between steps it scans the lists, because commands can move fish. A population created during a step is resolved on first use and added to the index.
  - A step costs O(populations): no hazard, growth, feeding or disease call scans the facility lists. `HoldingSeamTests` asserts one index per step and zero scans inside steps.
  - F2-03's `UnitResolver.Resolve` replaces the body of the index build (plots, `OffshoreUnit`, migrated land fish) and keeps the signature. The hatchery exemption stays: such a population resolves as a sea pen with no site.
- **One water.** `SimulationEngine.HoldingWaterOf(population)` (`HoldingWater`: temperature, O₂, salinity, area, kind) is the water at the fish.
  - A sea pen takes its zone's water. While an oxygen sag (the bloom deck's `hypoxia` card) runs at the site, the pen's water also carries the in-cage O₂ deficit (`waterOxygen.hypoxiaSagMaxDepth`).
  - An offshore pen takes its area's sea plus the structure's current-fed `OxygenBoost`.
  - A land tank takes the facility's controlled temperature and O₂.
  - Growth, appetite and feeding, the disease tick and the temperature-stress pass all read this water. It is the default body of `UnitWaterTemperatureC(pop)`, and the O₂ that `AppetiteFactorFor(pop)` reads (F2-07 adds Hs, F2-12 CO₂/TAN).
- **One environment for the hazards.** `SimulationEngine.ResolveCohortEnvironment(population)` returns `CohortEnvironment.From(water, holding id, CO₂, Hs)`. Its temperature and O₂ are that water's: only the ledger id, a land tank's CO₂ and the wave height are added.
  - `UnitMortalityTerms(pop)` hazards (F2-08 offshore causes, F2-15/F2-16 land causes) read this record and book a new `MortalityCause`.
  - Low oxygen has one hazard, `MortalityHazards.LowOxygenFraction` (none at or above 3 mg/L, US EPA 1986). It reads this record's O₂, so a land tank's O₂ failure (F2-15) or an offshore structure's exchange (F2-08) needs no new oxygen rule, only the water.
  - A pinned sea temperature (`ApplySeaTemperatureStress(t)`, tests and scenarios) reaches sea and offshore pens, never a land tank.
- **One pen (S2-01b, `wave2/sim-int-2`).** A sea pen's holding carries its production unit (`PopulationHolding.Pen`, a `Farm/Pen.cs` with `Id`, `Number`, `VolumeM3`, `PopulationId`), resolved with the holding once per step. The density stress on growth and FCR reads that pen's volume (`SimulationEngine.DensityVolumeM3`); a population in no pen (today's offshore pens and land tanks) keeps the wave-2 density proxy. F2-03's offshore zones and F2-12's land tanks become `Pen`s and are read the same way, with no new lookup; F2-10 adds `HasSkirt` to `Pen`.
- **Rules for the lanes.** A hazard or model never reads a zone, the weather or a facility list itself. A lane adds a holding kind by extending `HoldingOf`/`WaterOf` (and, from F2-02, its `*Hooks.cs`). It never adds a parallel lookup.

### 3.2 Data structures

```csharp
// Farm/SitePlot.cs (new, F2-03). Gameplay attributes come from godot/data/site_plots.json.
// Geometry comes from meta.json (G2-01 baked regions) or the procedural fallback.
public enum PlotKind { LandPad, OffshoreSlot }
public enum ExposureClass { Sheltered, OuterCoast, OuterCoastExposed, OpenOcean } // "Skjermet", "Ytre kyst", "Havkanten", "Havområde"
public sealed class SitePlot
{
    public string Id { get; init; } = "";                // "pad_a", "pad_b", "pad_delta", "off_1", "off_2", "hav_1", "overflow_land_1"…
    public PlotKind Kind { get; init; }
    public string ZoneRole { get; init; } = "region_po"; // "region_po" = the scenario's PO zone; "hav" = outside the PO system
    public string ZoneId { get; set; } = "";             // resolved at scenario start
    public double WorldX { get; init; }
    public double WorldZ { get; init; }                  // metres, shared with TerrainGenerator
    public double RealDistanceKm { get; init; }          // logistics distance; 3D compresses it
    // Land pads
    public double PadWidthM { get; init; }
    public double PadDepthM { get; init; }
    public int PhaseLots { get; init; }
    public double StaticLiftM { get; init; } = 13.0;     // pad +3.6 [L:R13] + 8 m water + 1.5 m freeboard (derived)
    public double DeepIntakeReachM { get; set; }         // computed from heightfield + bake (F2-19), never hand-authored
    public double PumpCapacityM3sPerLot { get; init; } = 5.0; // [GB]
    public double GridCapacityMW { get; init; }
    public int GridFeeds { get; set; } = 1;
    public double NatureSensitivity { get; init; }       // 0..1
    public bool PreZoned { get; set; }                   // scenario lever
    public bool HasRiverIntake { get; init; }            // pad_delta: brackish blending
    public decimal BlastCostNOK { get; init; }
    // Offshore slots
    public ExposureClass Exposure { get; init; }
    public double WaterDepthM { get; init; }
    public double Hs100M { get; init; }
    public double PlotHsScale { get; init; } = 1.0;      // fitted so the empirical 100-year level equals Hs100 (F2-06 #3)
    public double LiceExternalMin { get; init; }
    public double LiceExternalMax { get; init; }         // equal unless the plot is uncertain (hav_1)
    // State (saved)
    public string OwnerUnitId { get; set; } = "";        // "", a unit id, or "rival:<id>"
    public double LiceExternalRevealed { get; set; } = double.NaN; // drawn at scenario start, shown once a unit operates there
    public int WithdrawnFromDay { get; set; } = -1;      // site approval withdrawn (aquaculture plot lost)
}

// Farm/SeaSite.cs (additive, F2-03)
public SiteKind Kind { get; set; } = SiteKind.Fjord;         // enum SiteKind { Fjord, Offshore }
public string PlotId { get; set; } = "";
public OffshoreUnit? Offshore { get; set; }                   // non-null iff Kind == Offshore
public double AppliedContainmentCredit { get; set; } = 1.0;   // replaces the bool; zone exit divides by this stored factor (F2-10)

// Farm/Offshore/OffshoreUnit.cs (new, F2-03 shell; F2-07/F2-08 fill it)
public sealed class OffshoreUnit
{
    // Carried over from OffshoreFarm (same names and semantics)
    public OffshoreType FarmType { get; set; }                // SemiSubmersible = OF1-class, ShipShaped = Havfarm-class, Submersible = legacy only
    public double StructuralIntegrity { get; set; } = 1.0;
    public double CurrentWaveHeight { get; set; }             // today's Hs at the plot
    public int DaysSinceCrewRotation { get; set; }
    public int CrewRotationDays { get; set; } = 7;            // 9-on/12-off, 3 shifts [O:R3] (derived); legacy 14 is migrated
    public decimal CAPEX { get; set; }
    public int EmployeeCount { get; set; } = 12;              // in rotation [O:R13]
    public CarbonTracker CarbonTracker { get; set; } = new();
    // Renamed with migration
    public double DesignHsM { get; set; } = 5.5;              // was MaxWaveHeightM = 12; OF1 [O:R6, O:R15]
    // New
    public double EquityShare { get; set; } = 1.0;            // 0.35 for a consortium unit [GB]
    public string ConstructionProjectId { get; set; } = "";
    public double SiloTonnes { get; set; }
    public double SiloCapacityTonnes { get; set; } = 700;     // [GB; OF1 silo capacity unpublished]
    public double SilageTonnes { get; set; }
    public double SilageCapacityTonnes { get; set; } = 120;   // [GB]
    public HandlingRequirement Requirement { get; set; } = HandlingRequirement.None; // Delouse | Harvest | MtbRelief
    public int HandlingOverdueDays { get; set; }
    public double HatchProcedureLevel { get; set; } = 0.6;    // 0..1, raised by procedure upgrades
    public double AlarmLoad { get; set; } = 0.15;             // emergent, 0..1 (see §3.3)
    public List<SensorPackage> Sensors { get; set; } = new(); // {Kind, InstalledDay, LastServiceDay}
    public List<TeethingDefect> TeethingDefects { get; set; } = new(); // {Id, Kind, RevealDay, Revealed, Fixed}
    public int ShakedownEndDay { get; set; } = -1;
    public OffshoreIncident? ActiveIncident { get; set; }     // tilt | net_tear | mooring, with start day, stage and pre-drawn branches
    public int StructureKnownAsOfDay { get; set; }
    public int NetKnownAsOfDay { get; set; }
    public int CompletedCycles { get; set; }
    public string DispensationCaseId { get; set; } = "";      // waives 200k fish per pen
    public double DesignHsBonusM { get; set; }                // "Forsterkning" +1.0 m [GB]
}

// Environment/OffshoreMetocean.cs (new, F2-06; one per game in GameState.Metocean)
public sealed record SwellEvent(int Serial, int CreatedDay, int OnsetDay, int DurationDays,
                                double PeakHsM, double PeakTpS, double EstimateLogError);
public sealed class OffshoreMetocean
{
    public double ResidualX { get; set; }                     // AR(1) state of ln Hs
    public List<SwellEvent> Events { get; set; } = new();
    public int NextSerial { get; set; }
    public double BaseHsToday { get; set; }
    public double SwellHsToday { get; set; }
    public double VisibilityKmToday { get; set; }
}
public enum OffshoreOp { Feeding, Resupply, Silage, CrewChange, NetInspection, Stocking, Transfer, Crowding, Delousing, Harvest, Tow }
public enum VesselClass { Standard, DpWalkToWork, OffshoreWellboat, Helicopter }

// Farm/Land/LandTank.cs, LandLot.cs (new, F2-12). A tank wraps S2-01b's Pen for occupancy.
public enum LandTemplate { HfsLot, HfsPilotLot, RasHall, PostSmoltModule }
public enum TankRole { PostSmolt, PreGrowOut, GrowOut }
public enum BiofilterType { None, MovingBed, FixedBed }
public sealed class LandTank
{
    public Pen Pen { get; init; } = null!;                    // Id "ras_1/t07", VolumeM3, PopulationId, density check
    public TankRole Role { get; init; }
    public double DiameterM { get; init; }
    public string BioZoneId { get; init; } = "";              // HFS: one zone per tank [L:R1]; RAS: shared loop
    public double DoMgL { get; set; } = 9;
    public double Co2MgL { get; set; } = 6;
    public double TanMgL { get; set; } = 0.2;
    public double No3NMgL { get; set; } = 5;
    public double TemperatureC { get; set; } = 12;
    public double AppetiteFactor { get; set; } = 1;           // written by the land tick, read by Biology the next day
    public double H2sIndex { get; set; }
    public double GeosminIndex { get; set; }
    public double MaturationIndex { get; set; }
    public int SludgeAgeDays { get; set; }
    public int PurgeDaysRemaining { get; set; }
    public bool ShortDayPhotoperiod { get; set; }
}
public sealed class LandLot
{
    public string Id { get; init; } = "";
    public LandTemplate Template { get; init; }
    public string ConstructionProjectId { get; set; } = "";
    public List<LandTank> Tanks { get; set; } = new();
    public double HeatPumpMW { get; set; } = 8;               // [GB]
    public bool HeatRecovery { get; set; }
}
// RASFacility gains (additive): PlotId, Lots (List<LandLot>), DeepIntakeShare (w), SalinityPpt (post-smolt),
//   Biofilter, BiofilterMaturity, Power (LandPowerSystem: GridFeeds, GridCapacityMW, Gensets, LastGensetTestDay,
//   EmergencyDiffusers, LoxStoreTonnes, LoxCapacityTonnes, OnSiteO2Generator), NitrateDosing, Denitrification,
//   Discharge (DischargeAccount), Competence, CompetenceLevers, CompletedCycles, UtilisationCap, CohortPlan,
//   PendingDecisionId. RASFacility.Tanks (List<FishPopulation>) stays as the relinker's population list.

// Core/ConstructionProject.cs (new, F2-04; phase lists live in per-lane files)
public enum ContractForm { Managed, Turnkey }
public sealed class ConstructionPhase
{
    public string Key { get; init; } = "";
    public int PlannedDays { get; init; }
    public double CapexShare { get; init; }
    public double? MaxHsM { get; init; }
    public int[] ClosedMonths { get; init; } = Array.Empty<int>();
    public bool PlayerStarted { get; init; }                  // e.g. "tow"
}
public sealed class ConstructionProject
{
    public string Id { get; set; } = "";
    public string UnitId { get; set; } = "";
    public ContractForm Contract { get; set; }
    public List<ConstructionPhase> Phases { get; set; } = new();
    public int CurrentPhase { get; set; }
    public int PhaseStartDay { get; set; }
    public decimal PaidNOK { get; set; }
    public double OverrunFactor { get; set; } = 1.0;          // drawn per phase at its start
    public List<int> PhaseCompletedDays { get; set; } = new(); // drives the First Sight timelapse
    public bool Accelerated { get; set; }
}

// Finance/PermitCase.cs (new, F2-04)
public enum PermitKind { Zoning, LandAquaculture, Discharge, SeaWorks, Biosecurity, GridConnection,
                         DevelopmentLicence, Conversion, Dispensation, OffshoreArea }
public enum PermitStatus { Submitted, Approved, Refused, Appealed, Lapsed, Withdrawn }
public sealed class PermitCase
{
    public string Id { get; set; } = "";                      // "pc_N"
    public PermitKind Kind { get; set; }
    public string PlotId { get; set; } = "";
    public string UnitId { get; set; } = "";
    public int SubmittedDay { get; set; }
    public int DecisionDay { get; set; }
    public PermitStatus Status { get; set; }
    public double RefusalProbability { get; set; }            // shown to the player before submission
    public List<string> Mitigations { get; set; } = new();
    public Dictionary<string, double> Conditions { get; set; } = new(); // maxStandingT, maxProductionT, nitrogenTPerYear, maxDensityKgM3
}

// Finance/LicenceHolding.cs (S2-01's class; F2-09 adds)
public LicenceKind Kind { get; set; } = LicenceKind.Commercial; // Commercial | Development | OffshoreArea
public string BoundUnitId { get; set; } = "";
public int ExpiresDay { get; set; } = int.MaxValue;
public string PlacedZoneId { get; set; } = "";                  // "" = stranded or unplaced

// Finance/FacilityAssetRegister.cs (new, F2-04): FacilityAsset { UnitId, Capex, StartDay, LifeYears }
// for F2 units only. Depreciation is a non-cash P&L line (§3.6).
```

### 3.3 Per-tick logic

**Phase order.**
- In-day: Weather → **Metocean** (new, F2-06) → **Driftsplan** (new, F2-05) → HealthPrep → Biology → **FarmOpex** (runs `OffshoreTickSystem` and `LandSiteTickSystem`, then the remaining opex) → CarbonLedger → … → Sludge → TrafficLight.
- Post-tick: storm impacts (offshore skipped through a hook) → wellboat execution (gated through a hook) → **ConstructionAndPermits** (new: advances projects and resolves permit decisions).
- The new phases draw only from their own new streams and touch only F2 units, so the shipped-scenario goldens hold.
- **Landed (F2-05, `wave3/sim-core-4`):** the Driftsplan phase runs at the **start of every step** (`SimulationEngine.RunDriftsplan`, first in `Advance`, before the step resolves its holdings and the mortality ledger's day, and before Weather and Metocean), not between Metocean and HealthPrep: that is where a player's commands between days land, so a policy issues exactly the commands a scripted player would, with the same game every day (`DriftsplanTests`, the golden-trace day hash). Inside the step a treatment's deaths went to the next ledger day (mutation M1 in RUN_LOG §7). A policy reads the state the player reads, the end of the previous day, which already holds the metocean draw and the forecast it books against. Planning never draws: every evaluation is checked against every stream position (`DriftsplanPurityException`). A lane adds its policies through `DriftsplanPolicies.AddOffshorePolicies` / `AddLandPolicies` (partial methods in its own file) and its units' defaults with `ApplyDriftsplanDefaults(unitId)`.
- **Landed (F2-06 wiring and F2-07, `wave4/sim-sea-1`):** Metocean is the in-day phase `OffshoreSeaTickSystem` right after the weather (`Core/SimulationEngine.OffshoreSea.cs`); the sea (`GameState.Metocean`) is created with the first offshore unit, so a game without one has neither the state nor the stream. During a step the sea's day is `Weather.SimulationDay` (`State.Day` + 1). Each unit takes `HsAt` its plot's fitted scale from `facilities.offshore.plots`. `OffshoreTickSystem` runs each unit's day (`RunOffshoreUnitDay`, `Core/SimulationEngine.OffshoreOps.cs` and `.OffshoreUpkeep.cs`) on the unit's own stream `offshore_farm/<unit>`, exactly 8 uniforms a day (u0 the tow's shelter; u1-u7 the teething deck at commissioning, else u1-u4 the shakedown's reveals; u5-u7 reserved for F2-08).

**Metocean** (`OffshoreMetocean.TickDay`; stream `offshore_metocean`; **exactly 8 uniforms per day**, always drawn, sampled by inverse CDF).

1. **Base sea, an AR(1) on ln Hs.**
   - Formula: `ln B_t = μ_month + Δ_weather(state_t) + X_t`, with `X_t = φ·X_{t−1} + σ_ε·Φ⁻¹(u₀)`.
   - Parameters: φ = 0.75, σ_ε = 0.30, and Δ_weather = {Calm −0.30, Moderate 0, Storm +0.55, Severe +0.85} **[GB]**. The term couples the offshore sea to the fjord weather chain so the two never visibly disagree.
   - `μ_month` gives the off_1 reference climate: annual mean Hs ≈ 0.9 m, Jan ≈ 1.4 m, Jul ≈ 0.55 m [O:R17 anchors].
2. **Storm swell, a queue of discrete events known in advance.**
   - u₁ is the arrival test against the monthly rate λ = {Jan .10, Feb .09, Mar .08, Apr .05, May .03, Jun .02, Jul .02, Aug .03, Sep .05, Oct .07, Nov .09, Dec .10} per day **[GB]**.
   - The other draws (inverse CDF) set the event:
     - u₂ onset at +4…7 days;
     - u₃ peak Hs from a lognormal with median 2.2 m and σ_ln 0.30;
     - u₄ duration of 1–4 days;
     - u₅ Tp of 9–14 s;
     - u₆ the log-error of the forecast estimate, N(0, 1), scaled at display time by the lead.
   - The event shape is a 1-day ramp up, a plateau, and a 1-day ramp down.
   - **Overlapping events combine as a root sum of squares.**
3. **Visibility.** u₇ sets the day's visibility (low-visibility days by month **[GB]**). It gates the helicopter.
4. **Per plot.** `Hs_p = PlotHsScale_p × √(B_t² + Σ S_i,t²)`, and `Tp = max(4.5·√Hs, swell Tp)`.
   - PlotHsScale is fitted so that each plot's empirical 100-year level equals its `Hs100`: 5.0 m off_1, 6.5 m off_2, 13 m hav_1 (F2-06 #3).
   - The fjord weather stream is untouched.
5. **Operability today.** `OperationPossible(site, op, day, vessel)` is true when `Hs_p ≤ limit(op, vessel)`:

   | Operation | Standard | DP + walk-to-work | Offshore wellboat | Source |
   |---|---|---|---|---|
   | Crowding, delousing, harvest, stocking, transfer (fish handling) | 2.0 m | 2.0 m | 2.5 m | [O:R37]; offshore wellboat **[GB]** |
   | Wellboat alongside or in transit (pumping pauses above the handling limit) | 5.0 m | — | 5.0 m | [O:R37] |
   | Boarding, crew change | 2.0 m | 3.0 m | — | **[GB]**, fitted to AOF inaccessibility [O:R17] |
   | Net inspection (ROV from the unit's crane) | 3.0 m | 3.0 m | — | **[GB]** |
   | Resupply, silage | 2.5 m | 3.0 m | — | **[GB]** |
   | Tow | 2.5 m held for 3 days | — | — | **[GB]** |
   | Helicopter crew change | wind < 25 m/s and visibility ≥ 1.5 km | | | **[GB]**; 150 k per change [code] |
   | Feeding | No Hs limit; needs silo > 0. Appetite is 1.0 up to Hs 4 m, falling linearly to 0.7 at 6 m | | | [O:R7]; appetite **[GB]** |

6. **Forecast** (`MetoceanForecast`, pure, never draws).
   - Base part: the exact conditional lognormal mixture of the AR(1) at lead k, over `WeatherSystem.StateDistribution(k)`. `StateDistribution(k)` is a new pure method that generalises `StormRiskNext3Days`.
   - Known events: the posterior of the peak given its noisy estimate, with the error scaled by σ_ln·min(1, k/4).
   - Unknown events beyond the known horizon: a climatological term for events not yet created.
   - Output: P(Hs ≤ limit) per operation per day for 10 days, plus Hs p10/p50/p90.
   - Because the forecast is the model's own conditional distribution, it is calibrated by construction (F2-06 #5).

**OffshoreTickSystem** (stream `offshore_farm` + unit id; **exactly 8 uniforms per unit per day**; outcomes per zone come from these draws by inverse CDF over the zone hazards).

- **Silo and feed.**
  - Today's feed (from Biology) is subtracted from the silo.
  - When the silo will be empty tomorrow, `FeedAvailable = false`, and the Biology hook reads it the next day.
  - Resupply is a booked operation. The Driftsplan default books it at P ≥ 0.8 when the silo falls below 10 days.
- **Crew.**
  - The Driftsplan rota books a change every 7 days.
  - A missed change adds `CrewOverdueDays`.
  - The helicopter is the fallback.
- **Handling requirement and overdue days (the AOF lesson).**
  - A requirement is raised when one of these holds:
    - Lice are above the limit at two consecutive counts (S4-04 counting) → delousing.
    - Projected biomass is above 97% of MTB within 14 days, or harvest is ordered → harvest/MTB relief.
    - Net inspection is overdue by 30 days.
  - `HandlingOverdueDays` counts consecutive days with a requirement open and no completed handling operation.
  - Escalation:
    - **21 d** → warning;
    - **45 d** → Mattilsynet order to act within 30 days;
    - **75 d** → a site-withdrawal case opens **[GB thresholds, scaled from "up to 3 months" V:5]**.
  - Withdrawal:
    - The plot's aquaculture approval is lost; an appeal takes 120 d **[GB]**.
    - Fish must be harvested or moved by the deadline.
    - **The licences stay with the player as unplaced MTB** that must be placed in a PO or on another site [V:2, V:5].
    - `SiteApprovalWithdrawn` never deletes licences.
- **Alarm load** (emergent; there is no mute toggle).
  - `AlarmLoad = clamp(0.15 + 0.15·unfixedRevealedDefects + 0.10·sensorsPastService + 0.02·min(CrewOverdueDays, 10) − 0.30·alarmReviewActive, 0, 1)` **[GB]**.
  - High load stands for crews drowning in nuisance alarms from more than 20,000 points [O:R3]: the crew silences classes, so warnings get through less often.
  - Counter-verbs: `StartAlarmReview` (4 M, 30 d **[GB]**), `FixTeethingDefect`, sensor service through the Driftsplan.
- **Structure and net.**
  - Fatigue: `StructuralIntegrity −= 0.0005·(Hs/DesignHs)³` per day **[GB]**.
  - The player sees the value only as of the last ROV inspection, or continuously with `mru`.
  - Each day with Hs > DesignHs + DesignHsBonus carries a 2% mooring-failure hazard; the escape is capped at 10% **[GB, UX-FD4]**.
  - Net: `NetIntegrityPct −= 0.04 + 0.2·max(0, Hs − 3)` per day **[GB]**. Jellyfish wound days (*Apolemia* [O:R17]) fall on 5% of days in Nov–Dec **[GB]**.
  - A tear becomes possible below 70%. `subsea_cam` shows the net continuously.
- **Tilt chain (OF1-class).**
  - Hazard: `h = (1/9000)·(2 − HatchProcedureLevel)·(1 + 2·AlarmLoad)·(1 + 0.05·CrewOverdueDays)·teething`, where teething is ×3 while the ballast-pipe leak defect is unfixed **[GB]**. This gives ≈ 10% per year in a teething year and ≈ 5% per year once fixed.
  - When a tilt starts, all branches are pre-drawn: duration (1–24 h), whether the ballast deviation is caught (p = 1 − AlarmLoad), and whether the auto-close succeeds.
  - **Counter:** with `HatchProcedureLevel ≥ 0.8`, crew on board and no overdue crew, the standing order "Lukk luker ved ballastavvik" closes the hatches when the deviation is caught. That ends the tilt within 1 h. The player is not asked to click inside a 24-hour window that lasts 1 second.
  - Escape = 1.5% × min(1, duration_h/12) [O:R10]. The fish leave the zone populations and are recorded in `EscapeIncidents`, fines (Compliance) and Sustainability.
- **Teething deck.**
  - The first unit of each class draws 3–4 defects. They are **shown before construction** as "3–4 ukjente barnesykdommer; budsjett 157 M" [O:R4].
  - Each defect has a reveal day in cycle 1, pre-drawn at commissioning.
  - **Shakedown choice** ("Prøvedrift"): delaying first stocking by 60–120 d reveals each defect at 0.5 per 30 days **[GB]**, so it can be fixed before there are fish.
  - OF1-class deck:
    - harvest-zone crush, +3 pp mortality [O:R3];
    - cable-chain wear [O:R3];
    - net-system change [O:R4];
    - ballast-pipe leak [O:R10];
    - **"Biologien flytter ikke med"** (winter ulcers, +4–8 pp **[GB, after O:R26]**).
  - Havfarm-class deck (stage 3): mixed smolt sources, +10 pp [O:R26] (never drawn when all smolt come from one source); shared feed line; silage capacity [O:R28]; winter ulcers.
  - Later units of a class draw from a halved deck.
- **Lice and zones.**
  - `ExternalLicePressureFor(site)` multiplies only the **external** pressure by the plot's value. The unit's own lice model reproduces as on any `SeaSite`.
  - Offshore units in one area add each other's larval output to their external pressure.
  - `ZoneLiceContribution(site)` keeps offshore fish **out of the zone's fish-weighted mean**, so they cannot dilute it. It adds their total larval output (count × adult females) to the zone's `ExternalOutputLice` term, which feeds neighbouring sites and the traffic-light assessment. Building an offshore unit therefore can never make a neighbour look better.
- **Opex.**
  - Crew: 12 × 900 k/yr [O:R13 count; salary **GB**].
  - Charter: paid **from the booked day, including slipped days**.
  - Maintenance 2.5% and insurance 0.8% of capex per year **[GB]**.
  - The Havfarm-class unit buys shore power.
  - A consortium unit books every line × `EquityShare`, with a partner-settlement line for the rest.

**LandSiteTickSystem** (stream `ras_facility` + facility id; **exactly 10 uniforms per facility per day**; outcomes per zone come from these draws by inverse CDF).

- **Water temperature.**
  - `T_intake = (1 − w)·T25(zone, day) + w·T95(zone)`, where T95 ≈ 10 °C below the thermocline [V:10, L:R7] and T25 is the S2-02 zone series.
  - With a river intake (pad_delta), salinity = blend of river and fjord water, 12–35 ppt.
  - Tank T moves toward the setpoint within the lot's heat-pump MW.
- **Flows.**
  - Design loop flow `Q_tot` = 20 tank volumes per day **[GB, ≥ the ~15/day derived from L:R5]**.
  - `Q_new = min((1 − r)·Q_tot, plot pump capacity)`, and `Q_rec = Q_tot − Q_new`.
  - If the pumps cannot deliver `(1 − r)·Q_tot`, circulation falls. That is the cost of low reuse.
- **Energy, built from its parts** (kWh/day):
  - `E = Q_new·ρg(StaticLift + k_f·L_pipe(w))/η + Q_rec·ρg·H_loop/η + Q_rec·e_treat(r) + Q_new·1.11·|T_set − T_intake|/COP + E_base`.
  - Parameters: η 0.75, H_loop 4 m, COP 3.5, or 11 with `HeatRecovery` (after the claimed COP > 12 [V:11]), with `e_treat` and `k_f` **[GB]**.
  - Fit anchors (F2-12 #1):
    - Salmon Evolution-like plant at r 0.65 with heat recovery: 6.0–7.5 kWh/kg LW [L:R2].
    - r = 0, intake at setpoint, lift ≤ 2 m: 0.6–1.5 [L:A1, L:A3].
    - RAS at r 0.97: 4–10 [L:R7, L:S3, L:S4].
  - Price: the regional power price, 0.66–1.0 NOK/kWh [L:E1], through S2-02.
- **Oxygen, from metabolism.**
  - Fish demand = `k_O2(T)·feed`, with `k_O2` = 0.37 kg per kg feed at 12 °C **[GB, back-calculated from L:R2 and L:S5]**.
  - Nitrification adds 4.57 g O₂ per g TAN-N removed.
  - New water supplies `Q_new·(DO_sat(T,S) − 7.0)` **[GB]**.
  - `LOX = max(0, demand − supply)/0.75` **[GB injection efficiency]**.
  - Anchor: 0.45–0.65 kg per kg LW at the Salmon Evolution-like setup [L:R2].
  - Trucks deliver every 3 days. A LOX-shortage event halves deliveries for 10–30 d [L:B4, **GB duration**]. When the store is empty, DO falls to the level the new water alone supports.
- **CO₂**, steady-state mass balance per tank:
  - `C = (C_new·Q_new + P_CO₂)/(Q_new + η_s·Q_rec)`, with `P_CO₂ = 1.375 × fish O₂ use` and η_s = 0.85 HFS, 0.9 RAS **[GB]**.
  - Appetite is 1.0 up to 12 mg/L, falling linearly to 0.6 at 20 mg/L [L:S1, **GB slope**].
- **TAN, biofilter and nitrate.**
  - TAN is excreted at 0.03 kg per kg feed **[GB]**.
  - Removal = min(capacity × maturity × health, 0.6·TAN·Q_rec) **[GB]**. Maturity starts at 0.2 and reaches 1.0 by day 60 for RAS and post-smolt, and by day 30 for HFS **[GB]**.
  - Above 2 mg/L TAN, appetite falls and a mortality hazard starts **[GB]**.
  - NO₃-N accumulates and dilutes with new water; it has no growth effect up to 250 mg/L [L:S12, L:S13].
  - Nitrate counts toward the discharge permit and **buffers H₂S** (below).
- **Density.**
  - The growth multiplier is 1.0 up to 50 kg/m³, falling to 0.85 at 85 kg/m³ [L:S2, **GB shape**]. CO₂ and O₂ bite separately.
  - Stocking is refused when the projected peak exceeds 85 [L:R2].
- **Growth and competence (F2-15).**
  - The TGC multiplier comes from competence: 0.4 gives the achieved result, 1.0 gives the plan (§2B).
  - **Growth shortfall** ("tilvekstsvikt"): a cohort below 85% of plan for 30 d raises a warning that names its driver: CO₂, temperature, feed-plan change or gill health.
  - A feed-plan change at competence < 0.6 raises FCR by +0.12 for 60 d [L:R2 2025 event, **GB size**].
  - Competence starts at 0.4 and rises with:
    - research `land-based-ras`: +0.1;
    - hiring a driftsleder: +0.1, 2.5 M/yr;
    - a training programme: +0.05 per 6 months, up to +0.15, 3 M;
    - an external consultancy: +0.1 for 12 months, 8 M;
    - hiring staff ≥ 180 d before first fish: +0.1;
    - each completed cycle: +0.2 (all **[GB]**).
  - **Utilisation cap** = 0.55 + 0.45·(c − 0.4)/0.6 [UX: early plants never exceeded 50–60%; **GB**]. The milestones 55 → 70 → 85 → 100% are shown as "Oppkjøring".
  - Incident hazards are multiplied by (1.5 − 0.5c), after the lesson "fish over machinery" [L:B5].
- **H₂S** (index per bio-zone; index 0.3 ≈ 1 µg/L, 0.6 ≈ 5 µg/L, 1.0 ≈ 60 µg/L, on a log scale, derived from [V:8, L:F6]).
  - Daily rise = `0.02·(sludgeAge/30)·(salinity/35)·filter·nitrate`, **clamped to ≤ 0.10 per day**.
    - Sulphate factor: salinity/35 [V:6].
    - Filter: fixed bed 1.0, moving bed 0.3 [L:F5, V:7].
    - Nitrate: 1.0 at NO₃-N < 5 mg/L, falling to 0.3 at ≥ 20 mg/L [V:9].
    - All **[GB rates]**.
  - **Chronic band 0.3–0.7:** appetite −10% to −30% and a mortality hazard of 0.17%/day at 0.3 rising to 0.6%/day at 0.6 (derived from 4.7% and 16% over 28 days [V:8]).
  - The warning comes at 0.3 and the acute event at 1.0, which kills 5–30% of the bio-zone [L:B6, **GB**]. The clamp guarantees **≥ 7 sim-days** between warning and event.
- **Maturation** (`MaturationIndex`).
  - Daily rise = 0.004·max(0, T − 12) × light (continuous 1.0, short-day 0.3) × strain (all-female 0.1) [L:S1, L:S8, L:S10, **GB rate**].
  - At harvest, the maturing share of males (capped at 80%) is downgraded through the S2-04 grade.
- **Off-flavour** (RAS, r ≥ 0.9). Geosmin rises 0.01 per day × biofilter age factor; a purge removes 0.15 per day, so it clears in 6–10 d [L:S11]. An unpurged harvest is downgraded.
- **Power events** (all branches pre-drawn at creation, "draw, then decide").
  - Outage hazard: 1/730 per day with one grid feed, 1/7300 with two **[GB]**. Duration is lognormal with a median of 45 min and p90 of 240 **[GB]**.
  - The genset starts with p = reliability: 0.995 when tested, decaying 0.002 per day since the last test, down to 0.85 **[GB]**. Start time is 23 s [L:F3].
  - If it fails: `t₅ = 40 min·(40/ρ)·(12/T)`, or 15 min with stressed fish [L:F2, anchored]. Diffusers add 60 min **[GB]**.
  - Loss = clamp((outage − t₅ − diffusers)/30, 0, 0.9) of the bio-zone **[GB, UX-FD4]**.
  - **Standing orders ("Beredskapsplan") resolve covered outages inside the tick.** These produce an info entry and a "Lyset holdt" report.
  - An uncovered outage (genset failed and no order covers it), once S4-08 lands, becomes a **pending decision**: time is paused and the loss is held until the decision resolves.
    - The manual-start option succeeds with p = 0.4 + 0.4c **[GB]**, compared against the pre-drawn uniform.
    - The pending state is saved.
    - Before S4-08, the standing orders always resolve the outage.
  - An outage longer than 4 h multiplies biofilter health by 0.3 **[GB]**.
- **Discharge.**
  - N in = feed × 0.060 (35–40% protein feed) **[GB from realism review]**.
  - N retained = growth × 0.029 **[GB]**.
  - Captured with sludge: particulate share 0.25 × drum-filter efficiency 0.85 **[GB]**.
  - Removed by denitrification (upgrade): 50% of dissolved N **[GB]**.
  - The rest counts against the permit cap.
  - Annual inspection: an unmonitored plant has a finding probability of 0.88 (68 of 77 sites [L:L4]); with a monitoring upgrade it is 0.25 **[GB]**.
- **Sludge.** 0.175 kg DM per kg feed [L:S7] goes into `SludgeValorization` with a land source.
- **Drill ("Beredskapsøvelse").** Once, 60–120 d after commissioning (day pre-drawn), a drill runs the outage card, the standing orders and the incident report, with zero fish loss. It is the low-stakes first incident.

**Hooks** (`Core/SimulationEngine.FarmTypeHooks.cs`, installed by F2-02 with neutral defaults; lane implementations in `LandHooks.cs` and `OffshoreHooks.cs`):

| Hook | Call site (lines at the time of writing) | Default | Filled by |
|---|---|---|---|
| `ExternalLicePressureFor(site)` | HealthPrep lice, `SimulationEngine.cs:2543-2581` | 1.0 | F2-08 |
| `ZoneLiceContribution(site)` | `UpdateTrafficLightZones`, `SimulationEngine.cs:2863-2870` | today's (count × lice, count) | F2-08 |
| `SkipsFjordStormWear(site)` | `ApplyStormAndTemperatureImpacts`, `SimulationEngine.cs:1839` | false | F2-07 |
| `OperationPossible(site, jobKind, day)` | `ProcessDueWellboatJobs`, `SimulationEngine.cs:1559` (and S4-06's weather gate) | true | F2-06 |
| `FeedAvailableFor(pop)` | Biology feeding, `InDayTickSystems.cs` | true | F2-07 |
| `AppetiteFactorFor(pop)` | Biology feeding | 1.0 | F2-07 (Hs), F2-12 (CO₂/TAN) |
| `UnitWaterTemperatureC(pop)` | Growth `InDayTickSystems.cs:114-130`; disease `IntegratedDiseaseSystem.cs:146-150` | today's logic, moved | F2-12 |
| `UnitMortalityTerms(pop)` | Mortality call site in `InDayTickSystems.cs` | today's anchor × 1, no extra causes | F2-08 (offshore causes), F2-15/F2-16 (land) |
| `QualityMortalityMultiplier(pop)` | Same call site | 1.0 | F2-13 |
| `SmoltTierCostPerFish(tier, strain, source)` | `StockSmolts`, `SimulationEngine.Commands.cs:122` | today's (no premium) | F2-13 |
| `IsResourceRentLiable(unit, holding)` | `SellHarvest`, `SimulationEngine.Commands.cs:321` | today's (sea true, land false) | F2-04 (base), F2-09 (LicenceKind) |
| `CountsTowardSiteLimits(site)` | `MaxSitesPerZone`, RemoteOpsCentre budget | true | F2-03 (offshore false) |

**Landed (F2-02, `wave3/sim-core-1`).** The hooks are `internal` methods of `SimulationEngine` in
`Core/SimulationEngine.FarmTypeHooks.cs` (frozen); each dispatches to `Farm/Offshore/OffshoreHooks.cs`
(every sea site, offshore holdings) or `Farm/Land/LandHooks.cs` (land-tank holdings, the smolt hooks).
A population hook dispatches on `HoldingOf(pop).Kind` (a sea pen takes the neutral default in the hooks
file); a site hook goes to `OffshoreHooks` for every site, which checks F2-03's site kind. Call sites on
the wave-2 base (the line numbers above are from `a8767ef`):

| Hook | Call site | Notes |
|---|---|---|
| `ExternalLicePressureFor(site)` | `AdvanceSiteLiceModels`: `SetExternalPressure(SeasonalLicePressure(day) × hook)` | |
| `ZoneLiceContribution(site, pop)` | `ZoneLiceForTrafficLight(zoneId)`, read by `UpdateTrafficLightZones` | per population with the site that holds it (`HoldingOf(pop).Site`, null outside a site), returning `ZoneLiceShare(LiceWeightedFish, Fish)`: the pre-F2 mean is summed over populations, so a per-site signature would change its order |
| `SkipsFjordStormWear(site)` | `ApplyStormHazards` (net wear, escapes), `ApplyStormAndTemperatureImpacts` (storm deaths, the repair bill's site scale) | |
| `OperationPossible(site, jobKind, day)` | `ProcessDueWellboatJobs` | a job ruled out today stays queued and is tried the next day |
| `FeedAvailableFor(pop)` | biology feeding: `HoldingHusbandry.FeedAvailable` → ration 0 | |
| `AppetiteFactorFor(pop)` | biology feeding: multiplies the oxygen × gill appetite | |
| `UnitWaterTemperatureC(pop)` | disease tick (`IntegratedDisease.Update`); the growth water | `HoldingWaterOf(pop).TemperatureC`; `WaterOf` takes a tank's water from `LandHooks.TankWater` and an offshore pen's from `OffshoreHooks.PenWater`, so growth, disease and the hazards read one water |
| `UnitMortalityTerms(pop)` | biology mortality | `MortalityTerms(AnchorMultiplier, ExtraHazards)`: the multiplier scales the anchor hazard (`FishPopulation.AdvanceDays(..., anchorHazardMultiplier)`); each `UnitHazard(cause, dailyFraction)` is booked after it (`ApplyUnitMortalityHazards`) |
| `QualityMortalityMultiplier(pop)` | biology mortality: × the terms' anchor multiplier | |
| `SmoltTierCostPerFish(tier, strain, source)` | `PlaceSmolts` (`SimulationEngine.Pens.cs`) | `SmoltSource { Purchased, OwnFacility }`; every stocking today is `Purchased` |
| `IsResourceRentLiable(unitId, holdingKind)` | `FindHarvestHolding` (`SimulationEngine.Harvest.cs`) | |
| `CountsTowardSiteLimits(site)` | none yet | `MaxSitesPerZone` is enforced nowhere and the ROC budget is summed in `GameBridge.Farm.cs`; F2-03 wires it |

Moved with the hooks: the facility ticks (`Core/LandSiteTickSystem.cs`: tanks, post-smolt modules, land
carbon; `Core/OffshoreTickSystem.cs`: offshore farms, offshore carbon; FarmOpex keeps the order tanks →
offshore → post-smolt, the opex being one decimal sum) and the containment code
(`Core/SimulationEngine.Containment.cs`). `FarmTypeMoveTests` pins the pre-F2 facilities save's 365-day
continuation and greps the two hot files.

### 3.4 Commands

Engine partials return `CommandResult`. Every verb a policy can issue is the same verb the player issues.

| Verb | Partial | Fails with |
|---|---|---|
| `SetDriftsplan(unitId, policyKind, settings)`; `GetNextPlanned(unitId)` | `.Driftsplan` | unknown_policy, out_of_range (landed F2-05: also not_found for an unknown unit and not_applicable for a policy of another unit kind; `ClearDriftsplan`, `ApplyDriftsplanDefaults`, `GetPlannedJobs`) |
| `BuildLandSite(plotId, LandTemplate, ContractForm)` (pilot lot, full lot or post-smolt module) | `.LandSites` | not_unlocked, plot_taken, permits_missing, grid_capacity, insufficient_funds |
| `ExpandLandSite(siteId)` (pilot → full lot, next lot, next module); `PreOrderLot(siteId)`; `AccelerateConstruction(projectId)` | `.LandSites` | lots_exhausted, permits_missing, insufficient_funds |
| `StockLandTank(siteId, tankId, count, strainId)`; `GradeTank(siteId, from, to, share)` | `.LandSites` | not_operational, biofilter_immature, tank_occupied, density_exceeded, annual_capacity, util_cap |
| `SetVannstrategi(siteId, preset \| {r, w, setpointC})`; `SetSalinity(siteId, ppt)`; `InstallHeatRecovery(lotId)` | `.LandSites` | out_of_range, no_river_intake, grid_capacity |
| `PlanHarvest(siteId, tankGroupId, isoWeek)`; `HarvestLandTank(siteId, tankId, route)` (S2-04 pipeline) | `.LandSites` | purge_required (warning only), no_slot |
| `SetPhotoperiod`; `StartPurge`; `CleanTank`; `SetBiofilter(type)`; `SetNitrateDosing`; `InstallDenitrification` | `.LandSites` | not_found, purge_active, insufficient_funds |
| `TestGenerator`; `InstallO2Generator`; `InstallEmergencyDiffusers`; `SetStandingOrders(siteId, plan)` | `.LandSites` | insufficient_funds |
| `HireDriftsleder`; `StartTrainingProgramme`; `HireConsultancy` | `.LandSites` | already_active, insufficient_funds |
| `TransferTankToSea(siteId, tankId, seaSiteId, count)` (WellboatJob "transfer", 1–3 d); `SellPostSmolt(siteId, tankId, count)` | `.LandSites` | mab_exceeded, no_wellboat_slot, below_min_weight |
| `ApplyPermit(plotId, PermitKind, mitigations)`; `AppealDecision(caseId)`; `StartEnvironmentalSurvey(plotId)`; `StartGridProject(plotId, kind)` | `.FacilityPermits` | plot_taken, moratorium, already_pending, not_refused |
| `BuildOffshoreUnit(plotId, OffshoreType, ContractForm, equityShare)` | `.Offshore` | not_unlocked, licence_missing, exposure_exceeds_design, insufficient_funds |
| `StartTow(projectId)`; `ChooseShakedown(siteId, days)` | `.Offshore` | not_ready, window_unlikely (warning only) |
| `BookOffshoreOperation(siteId, op, day, VesselClass)`; `BookFirstGreenWindow(siteId, op)` | `.Offshore` | no_vessel. outside_window is a warning; the job slips day by day with charter paid |
| `ResupplyFeed`; `InspectOffshore(scope)`; `RepairOffshore(scope)` | `.Offshore` | no_window, insufficient_funds |
| `StartAlarmReview`; `StartProcedureUpgrade(kind)`; `InstallSensorPackage(pkg)`; `FixTeethingDefect(id)`; `InstallReinforcement` | `.Offshore` | not_found, insufficient_funds |
| `ApplyDevelopmentLicence(concept, plotId)`; `RequestConversion(siteId)`; `PayConversion(siteId)`; `PlaceLicences(holdingIds, zoneId)`; `BuyOutPartner(siteId)` | `.Offshore` | round_closed, gate_unmet, criteria_unmet, payment_lapsed, zone_full |
| `BidOffshoreArea(areaId, NOK)`; `MoveCapacityOffshore(fromZoneId, tonnes, areaId)` (stage 3) | `.Offshore` | round_closed, insufficient_mtb, flag_off |
| `UpgradeContainment(siteId, level, penIds)`; `SetClosedPenPumps(siteId, mode)`; `EmergencyFlush(siteId)`; `RequestDispensation(siteId, kgM3)` | `.Containment` | grid_capacity, insufficient_funds, dispensation_required |
| `ResolveFacilityDecision(decisionId, option)` (through S4-08's `ResolveDecision`) | `.LandSites` | not_pending |

**Landed (F2-12 wiring and F2-14, `wave4/sim-land-1`):** the land verbs as built. `.LandSites`: `CommissionLandSite(plotId, template, lots, name)` (the scenario/test build: ready at once, no permits), `StockLandTank`/`PreviewLandStocking` (not_found, tank_not_found, out_of_range, not_operational, biofilter_immature, tank_occupied, density_exceeded, annual_capacity, insufficient_funds; util_cap is F2-15's), `HarvestLandTank`, `GradeTank(siteId, from, to, share)` (the whole tank moves as it is, a share splits off through the ledger; density_exceeded on the target), `SetVannstrategi(siteId, r, w, setpointC, blend)`, `InstallHeatRecovery(lotId)`. `.FacilityPermits`: `ApplyLandPermits(plotId, template, lots, mitigations)` (the package a pad still needs for `lots` × the template's nameplate; plot_taken, lots_exhausted, not_unlocked, moratorium, already_pending, nothing_to_apply, insufficient_funds), `ApplyForZoning(plotId, mitigations)` (no research gate, not held by the moratorium), `AppealLandPermit(caseId)` (180 d, half the odds), `ReapplyLandPermit(caseId, mitigations)`, `StartGridProject(plotId, "capacity" | "second_feed")`, `PreviewLandBuild`/`PreviewLandExpansion`, `BuildLandSite(plotId, template, contract)`, `ExpandLandSite(siteId, contract)` (a pilot to a full lot, else the next lot or module), `AccelerateLandConstruction(projectId)`, `CancelLandConstruction(projectId)` (only before the first tanks), `LandConstructionViews()`. The environmental survey and the denitrification commitment are mitigations an application carries (no separate `StartEnvironmentalSurvey`); `PreOrderLot` is not built yet (RUN_LOG §8). Readings: RUN_LOG §7 (2026-10-17); numbers: docs/BALANCE.md §1v.

**Landed (F2-07, `wave4/sim-sea-1`):** the offshore verbs live in `.Offshore` (build, preview, construction view, tow, shakedown, fixes, sensors, repair, buy-out) and `.OffshoreOps` (bookings, previews, the unit's state and day). Where the code differs from the table: `StartTow(siteId)` and `PreviewTow(siteId)` take the unit; `BookOffshoreOperation(siteId, op, day, vessel, count, detail)` carries a stocking's smolts and strain or a delousing's method, refuses not_bookable (tow, harvest: the harvest is an S2-04 order gated by the unit's sea), invalid_day, invalid_count, unknown_method, no_vessel, and charters from the booked day; `PreviewOffshoreBooking` gives P(done by day) and the expected charter; `InspectOffshore(siteId)` books the ROV net inspection into the first green window; `RepairOffshore(siteId, "net" | "structure" | "all")`; `FixTeethingDefect(siteId, defectId)`; `InstallSensorPackage(siteId, kind)` and `ServiceSensors(siteId)`; `BuyOutPartner(siteId)` lands with the consortium here. `StartAlarmReview`, `StartProcedureUpgrade` and `InstallReinforcement` stay with F2-08, the licence verbs with F2-09 (`BuildOffshoreUnit` checks no development licence until then). The bridge's `RepairOffshore` is the engine's `RepairOffshore(site, "all")`.

**Landed (F2-13 and F2-15, `wave4/sim-land-2`):** `.PostSmolt`: `SetSalinity(siteId, ppt)` (not_found, wrong_site_kind, no_river_intake, out_of_range), `PreviewTransferTankToSea`/`TransferTankToSea(siteId, tankId, seaSiteId, count)` (0 = the whole tank; holding_empty, out_of_range, below_min_weight, site_not_found, offshore_unit, no_wellboat_slot, mab_exceeded and S2-01's mtb_*/pen keys at booking and again when the boat comes, insufficient_funds), `SellPostSmolt(siteId, tankId, count)` (wrong_site_kind on a grow-out site), and the post-smolt panel's `StartSmoltBatch`, `VaccinateSmoltBatch`, `TransferSmoltBatchToSea` (the bridge's stubs call them); `.CohortTransfers`: `PlanCohortTransfer`, `ExecuteCohortTransfer` (engine-owned, no random source; still immediate). `.LandBiology`: `HireDriftsleder`, `StartTrainingProgramme`, `HireConsultancy`, `HireLandStaffEarly` (wrong_site_kind on a post-smolt site, already_active, not_ready, insufficient_funds), `SetLandFeedPlan(siteId, rationPct)`, `QuoteLandHarvestPlan`/`PlanLandHarvest(siteId, tankIds, weekStartDay)` (already_planned, no_slot), `CancelLandHarvestPlan`; `StockLandTank` refuses util_cap. `.LandBoard` (reads, pure): `LandBoardOf`, `LandCompetenceViewOf`, `VannstrategiPresetsOf` and `ApplyVannstrategiPreset(siteId, "sparsom" | "balansert" | "hoy_tetthet")`, `LandHarvestPlannerOf`, `PostSmoltPlannerOf`. The cohort plan's daily hazard replaces the sea anchor for land tank fish (`LandHooks.MortalityTermsFor`); the plan grows at `production.planGrowthMultiplier` (0.92, [L:R1]). Readings: RUN_LOG §7 (2026-10-20); numbers: docs/BALANCE.md §1y; replaced tests: docs/run/evidence/f2_13_f2_15_replaced_tests.md.

**Landed (F2-08 and F2-09, `wave4/sim-sea-2`):** the F2-08 verbs live in `.OffshoreIncidents` (`StartAlarmReview(siteId)`, `StartProcedureUpgrade(siteId, "hatch")`, `InstallReinforcement(siteId)`: +1.0 m design Hs after 90 days) and `.OffshoreHandling` (`AppealSiteWithdrawal(siteId)`); the inspections and defect fixes stayed F2-07's verbs. The F2-09 verbs live in `.OffshoreLicences`: `ApplyDevelopmentLicence(concept, plotId)` (only "of1" in stage 2; refusals round_closed, invalid_concept, plot_not_found, not_an_offshore_slot, plot_taken, exposure_exceeds_design, gate_unmet, already_applied), `PreviewDevelopmentApplication(concept, plotId)` (the breakdown the case is filed with), `RequestConversion(siteId)`, `AppealConversion(siteId)`, `PayConversion(siteId)` (nothing_due, payment_lapsed, insufficient_funds) and `PlaceLicences(holdingIds, zoneId)` (not_found, zone_not_found, zone_full on a red zone). `BuildOffshoreUnit` now refuses licence_missing without an award for the plot and concept. The views the window board and the licence path read are `GetOffshoreHandlingState`, `GetDevelopmentRoundView` and `GetOffshoreLicenceView` (`.OffshoreSeaViews`). Where the code differs from the text: the round's company gate is the research, a first harvest sold and 365 escape-free days (the "completed sea cycle" read as the company's first sold harvest); the round's field is drawn when it opens from a private stream (`DrawRivalField`), not from a game stream, and the opening is a bus event without an event-log line, so no shipped scenario's trace moves; a pending conversion (first decision or appeal) is decided on the checklist as it stands the day before its decision day; converted licences placed in a zone of another production area pool there and leave the unit unstockable (licences_unplaced) until it has licences in its own area.

The bridge-owned verbs become thin calls into the engine:
- `UpgradeContainment`, `GameBridge.Farm.cs:1183-1220`;
- `PerformRASMaintenance`, `:1674`;
- `RepairOffshore`, `:1799`;
- the post-smolt stubs, `:1987-2104`;
- `ExecuteCohortTransfer`, `GameBridge.Depth.cs:1208-1247`, whose `new Random(seed)` disappears.

### 3.5 Events

New GameEventBus types (U2-06 severities). Each carries `UnitId`, `DeadlineDay` and `CounterVerb` (F2-05) and is also sent as a `visual_event` to the G3-02 presenter.

| Severity | Events |
|---|---|
| Critical | LandPowerOutageUncovered, H2sAcute, TiltEvent, NetTear, MooringFailure, SiteApprovalWithdrawn, MattilsynetOrder |
| Warning | HandlingOverdue, LandO2Low, LoxShortage, H2sChronic, BiofilterDegraded, OffFlavourDetected, MaturationRising, GrowthShortfall, DischargeNearCap, SiloLow, CrewOverdue, StormSwellForecast (≥ 4 d ahead), CapexOverrun, ConversionPaymentDue |
| Info | PermitDecision, ConstructionPhaseComplete, TowWindowLikely, DevLicenceRoundOpened, DevLicenceAwarded, ConversionDecision, TeethingDefectRevealed, OutageCovered, DrillCompleted, OffshoreAreaRoundOpened |

**Deadline rule** (F2-34, through U2-07's auto-slow API):
- A warning or critical with a counter-verb and `DeadlineDay − today ≤ 7` slows the game to 1×.
- At ≤ 1 sim-day remaining with no response, the game pauses.

Every critical event writes an **IncidentReport** to the EventLog with ≥ 3 causal steps, the drivers behind each probability, the losses and the procedure unlocked.

**Landed (F2-05, `wave3/sim-core-4`):** every type above is a `GameEventType` marked `[FarmTypeEvent]` with a catalog row declared through `F2Row` (severity as in the table, deadline rule, counter verb or `none`; `FarmTypeEventCatalogTests` reads this table), plus F2-05's own `DriftsplanCommandFailed` (warning, `open_driftsplan`). Deadlines the publisher sets: `SiteApprovalWithdrawn`, `MattilsynetOrder`, `HandlingOverdue`, `LoxShortage`, `H2sChronic`, `OffFlavourDetected`, `DischargeNearCap`, `SiloLow`, `StormSwellForecast`, `ConversionPaymentDue`. `HandlingOverdue` and `MattilsynetOrder` share a dedupe category (as do `H2sChronic` and `H2sAcute`), so the order escalates the warning on the bus and moves its deadline. Counter verbs: `book_operation`, `repair_unit`, `review_alarms`, `fix_defect`, `start_tow`, `pay_conversion` (the offshore tab until F2-33), `resolve_outage`, `adjust_water`, `order_oxygen`, `start_purge`, `build_competence` (the land tab until F2-32), `open_driftsplan`; `CapexOverrun` has none (a managed contract's overrun is owed). The notification meter (`engine.Notifications`) counts the warnings and criticals the bus delivered per unit and sim month against the §6.6 budget; it is not saved.

### 3.6 Economy and carbon hooks

| Flow | Booking |
|---|---|
| Phase payments (capex × share × overrun × equity) | `CostBooking.Capex` through the existing `TryPayCost` |
| Depreciation: land 20 years, offshore steel 25 years **[GB]** | New non-cash P&L line `FacilityDepreciation` for **F2 units only**, part of the corporate tax base. Cash reconciliation is unchanged. Depreciation of sea-site and processing capex is deliberately out of scope (§8) |
| Energy, LOX, charter, crew, maintenance, insurance, permit fees, grid connection fees | `Opex` |
| Smolt for land tanks; feed | `Cogs` (existing) |
| Post-smolt sales | Revenue through the ledger |
| Harvest | S2-04 pipeline. **Resource rent through `IsResourceRentLiable`, keyed on the licence, not the location** [V:3, V:4, V:16]: Development → no; Commercial (including converted) → yes wherever the unit sits; OffshoreArea → no unless the regulatory flag `offshoreRent` is on. Land → never. The existing 70 M standard deduction (`ResourceRentTax.StandardDeductionNok`) applies. **The rent base is ring-fenced:** land and non-liable offshore costs never reduce the sea rent base (`SimulationEngine.Tax.cs:57-63`) |
| Carbon | Land Scope 2 = kWh × the CarbonLedger grid factor; an on-site O₂ generator runs at 0.8 kWh/kg O₂ in Scope 2. Purchased LOX is 0.5–1.0 kWh/kg O₂ upstream (Scope 3) **[GB]**. Offshore Scope 1 = vessel days × fuel factor **[GB]**. Havfarm shore power is Scope 2. Feed is Scope 3 as today. The sludge biogas credit applies to land. The blocks at `SimulationEngine.cs:2273-2306` move into the tick systems in F2-02 |
| Information quality | A missing sensor package shows its value "as of" its last measurement, plus ±12% deterministic noise from `StableHash(day, siteId)` (no RNG). Biomass error with `echo` is ±3% **[GB]**. `ais_broadband` plus a shore control centre cuts the RemoteOpsCentre watch cost for offshore sites from 25 to 10 |
| Financing | The preview links `TakeLoan` pre-filled at 60% of the equity share of capex **[GB]**, and shows a 36-month cash runway (§6.2). The consortium (35% equity) is the offshore entry rung. There is no new loan product |

### 3.7 Determinism

**Streams**
- New frozen scopes in `SimRngStreams`:
  - `offshore_metocean` (no suffix; 8 per day);
  - `facility_permits` (suffix: case or round id; 2 per case at submission);
  - `facility_construction` (suffix: project id + phase; 2 per phase at its start).
- Existing scopes reused with id suffixes:
  - `offshore_farm` (8 per unit per day);
  - `ras_facility` (10 per facility per day).

**Rules**
- **Draw, then decide.** The draw count per tick is fixed whatever the branch outcome. Outcomes per zone come from the facility's draws, so building another lot never shifts a stream.
- Decision options compare against pre-drawn uniforms. Choosing an option never moves a stream.
- Forecasts, previews, lenses, policies and display noise never draw. Twin-lite projections (U2-05) clone the streams.
- New code uses no `DateTime`, `Guid` or `Random`. F2-04 extends `DeterminismScanner` to `GameBridge.LandSites.cs` and `GameBridge.Offshore.cs`, and the allowlist does not grow.
- **Nothing may sort or key by `FishPopulation.Id`** (an allowlisted Guid).
- Ids are deterministic: `ras_N`, `offshore_N`, `<site>/tNN`, `pc_N`, `cp_N`, `dlr_N`. Migrated units keep their ids.
- Stream positions persist through `runtime.SimStreamPositions`.

### 3.8 Save, load and versioning

- **GameState (additive):** `SitePlots` (state only), `ConstructionProjects`, `PermitCases`, `Metocean`, `DevelopmentRounds`, `OffshoreAreaRounds`, `FacilityAssets`, `PendingFacilityDecisions` (until S4-08's store takes over).
- **Game wrapper:** `godot/scripts/autoload/SaveLoadManager.cs` `CurrentSaveVersion` goes to **the next version at merge**. It is 3 today, and R3 bumps it once per phase, so there is no hard-coded "4". `MigrateSaveData` records the bump.
- **Library migration** (new `Core/SimulationEngine.FacilityMigration.cs`, called once from `RestoreFromLoad`, idempotent, keyed on non-empty legacy lists):
  1. **`OffshoreFarm` → Offshore `SeaSite`** with the same Id, on the first free compatible plot (design Hs ≥ Hs100), otherwise on an overflow slot.
     - `MaxWaveHeightM` → `DesignHsM` = class default; crew rotation 14 → 7.
     - Legacy pens are merged pairwise into zones by `CohortTransferSystem` merge rules (stage, quality and genome kept): SemiSubmersible gets 3 zones, ShipShaped 6, Submersible 3.
  2. **`PostSmoltFacility` → `RASFacility`** with the `PostSmoltModule` template on pad_delta, with modules = ceil(capacity / 1.1 M fish). Each `SmoltBatch` becomes a population in a post-smolt tank, keeping age, count, weight and vaccination state. The Id is kept.
  3. **Each legacy `RASFacility`** gets a pad (or an overflow pad) and lots = min(4, ceil(CapacityTonnesPerYear / 7,900)). `RASScale.Large` (20,000 t) becomes 3 lots. Tanks are generated from `TankCount`/`TankVolumeM3`, and existing tank populations are placed in tank order.
  4. **Overflow:** `site_plots.json` reserves `overflow_land[]` and `overflow_offshore[]` positions with neutral attributes. These are used only by migrated saves and are rendered like normal plots.
  5. **The relinker runs.**
- **Landed (F2-01, `wave2/sim-int-3`):** the pre-F2 save version is v8, so the fixture is `src/LakskongenSim.Tests/Fixtures/save_v8_facilities.json` (generator `src/StateHashProbe/PreF2SaveFixture.cs`, frozen; `PreF2SaveFixtureTests`); the golden traces, their format and the re-record workflow are in `docs/GOLDEN_TRACES.md`.
- **Landed (F2-03, `wave3/sim-core-2`):** save format v9; the migration is `SimulationEngine.MigrateFacilities` (after `AdoptSavedState` and at the start of a step, keyed on the legacy lists and land sites without a pad). Where the code differs from the sketch in §3.2: a land site's kind is `RASFacility.Kind` (`LandSiteKind { GrowOut, PostSmolt }`) until F2-12 brings `LandTemplate`; an offshore unit's carbon is its `SeaSite.CarbonTracker` (one tracker per sea site; `OffshoreUnit` has none); a plot is its own water area (`ZoneId` = plot id) in the scenario's production area; `GameState.SitePlots` holds plot state only once a unit takes a plot; `site_plots.json` reserves 4 overflow positions a kind and the catalogue generates more. The bridge has two verbs, `GetSeaSiteDetails()` (the fjord) and `GetAllSeaSiteDetails()` (every sea site), because a GDScript call never fills a C# default parameter. `CountsTowardSiteLimits` is wired (`RemoteOpsAttentionUsed`, `SitesCountingTowardLimits`); a fifth sea-site predicate, `CountsTowardAreaLicencePool`, lives in `OffshoreHooks`. The post-smolt section of `FacilityProductionTests` moved to `Farm/PostSmoltSiteTests.cs` (F2-13 adds `PostSmoltBridgeTests`). Audit: `docs/run/evidence/f2_03_sitekind_audit.md`; replaced tests: `docs/run/evidence/f2_03_replaced_tests.md`; decisions: RUN_LOG §7 (2026-10-16).
- **Pre-1.0 policy (R3):** saves stay officially unsupported, but this migration is tested against the fixture `src/LakskongenSim.Tests/Fixtures/save_v3_facilities.json`. **F2-01 generates the fixture on the pre-F2 commit**, before any schema change.

### 3.9 Calibration registry

- A new section `FacilitiesCalibration Facilities` maps to JSON `"facilities"`.
- **Every lane owns its own file.** `calibration.json` gains one include list (F2-04): `"facilitiesIncludes": ["calibration/facilities_land.json", "calibration/facilities_offshore.json", "calibration/facilities_closed_sea.json"]`. The C# records are `LandCalibration.cs`, `OffshoreCalibration.cs` and `ClosedSeaCalibration.cs`.
- **Sources are machine-checked attributes, not comments.** The loader skips comments (`ReadCommentHandling.Skip`). Every leaf property carries `[Range]` and exactly one of `[Source("L:R2")]` or `[GameBalance("why")]`. A reflection guard in `CalibrationGuardTests` enforces it, and S4-10's `Validate()` reads the same attributes.
- The registry goes from 1.0.5 to the next minor version at merge.
- **Landed (F2-04, `wave3/sim-core-3`):** registry 1.1.0. Where the code differs from the sketch: `FacilitiesCalibration` is a `partial` record and sim·core owns a fourth include, `facilities_common.json` (`facilities.common`: depreciation lives, construction risk, the farm-type catalogue's reference inputs); `calibration.json` lists `["calibration/facilities_common.json", "calibration/facilities_land.json", "calibration/facilities_offshore.json", "calibration/facilities_closed_sea.json"]` and a listed file that is missing keeps its section's code defaults. Each lane adds its section property to `FacilitiesCalibration` from its own record file and carries `[Range]` + `[Source]`/`[GameBalance]` on every leaf (the guard walks whatever the record holds). `[Range]` is `System.ComponentModel.DataAnnotations.RangeAttribute`; `[Source]` takes one or more keys. Construction phases draw a cost factor and a duration factor (truncated lognormals, median and p90 from the registry); permit cases draw the decision day and the outcome; the post-tick `ConstructionAndPermitsTickSystem` (after the wellboat jobs) advances projects, decides due cases and books depreciation. Decisions: RUN_LOG §7 (2026-10-16); golden diff: `docs/run/evidence/f2_04_golden_diff.md`.

```csharp
public sealed record LandEnergyCalibration
{
    [Range(5, 40)] [GameBalance("design loop exchange; >= 15/day derived from L:R5")] public double LoopTankVolumesPerDay { get; init; } = 20;
    [Range(1, 20)] [GameBalance("pump capacity per HFS lot, derived from L:R5 intake flows")] public double PumpCapacityM3sPerLot { get; init; } = 5.0;
    [Range(2, 5)] [GameBalance("heat-pump COP without recovery")] public double Cop { get; init; } = 3.5;
    [Range(6, 14)] [Source("V:11")] public double CopWithHeatRecovery { get; init; } = 11;
    [Range(1.0, 1.2)] [Source("physics: seawater 4.0 MJ/m3/K")] public double HeatKWhPerM3PerK { get; init; } = 1.11;
}
```

```json
"facilities_land.json": {
  "tanks": { "hfsDiameterM": 28.0, "hfsVolumeM3": 5000, "preGrowDiameterM": 16.0, "preGrowVolumeM3": 1600,
             "rasVolumeM3": 2500, "rasTanks": 16, "postSmoltTanksPerModule": 2 },
  "density": { "maxKgM3": 85, "kneeKgM3": 50, "growthAtMax": 0.85, "postSmoltMaxKgM3": 80 },
  "capacity": { "hfsLotTHog": 7900, "rasIndexKgM3Yr": 130, "postSmoltIndexKgM3Yr": 150, "hogPerLw": 0.83 },
  "capex": { "hfsLotNOK": 2070000000, "hfsPilotNOK": 900000000, "hfsPilotExpandNOK": 1250000000,
             "rasPerTHogNOK": 320000, "postSmoltModuleNOK": 110000000, "turnkeyPremium": 0.12,
             "overrunMedian": 0.07, "overrunP90": 0.25 },
  "energy": { "loopTankVolumesPerDay": 20, "pumpCapacityM3sPerLot": 5.0, "pumpEfficiency": 0.75, "loopHeadM": 4.0,
              "frictionMPerKm": 1.2, "treatKWhPerM3Rec": 0.02, "treatKWhPerM3RecRas": 0.05,
              "cop": 3.5, "copWithHeatRecovery": 11, "heatKWhPerM3PerK": 1.11, "baseKWhPerLotDay": 2000,
              "heatPumpMWPerLot": 8 },
  "oxygen": { "kgO2PerKgFeedAt12C": 0.37, "nitrificationGO2PerGTan": 4.57, "usableDoFloorMgL": 7.0,
              "injectionEfficiency": 0.75, "loxPriceNOKPerKg": 4.0, "loxDeliveryIntervalDays": 3,
              "onSiteGeneratorKWhPerKgO2": 0.8, "purchasedLoxUpstreamKWhPerKg": 0.75 },
  "co2": { "stripEfficiencyHfs": 0.85, "stripEfficiencyRas": 0.9, "co2PerO2": 1.375,
           "appetiteFullMgL": 12, "appetiteAt20": 0.6 },
  "tan": { "kgPerKgFeed": 0.03, "maturationDaysRas": 60, "maturationDaysHfs": 30, "appetiteLimitMgL": 2.0 },
  "h2s": { "rateCoeff": 0.02, "maxDailyRise": 0.10, "warn": 0.3, "high": 0.6, "acute": 1.0,
           "fixedBedFactor": 1.0, "movingBedFactor": 0.3, "nitrateLowMgL": 5, "nitrateHighMgL": 20,
           "nitrateFactorAtHigh": 0.3, "chronicMortPerDayAt03": 0.0017, "chronicMortPerDayAt06": 0.006,
           "acuteKillMin": 0.05, "acuteKillMax": 0.30 },
  "maturation": { "thresholdC": 12.0, "ratePerDegDay": 0.004, "shortDayFactor": 0.3, "allFemaleFactor": 0.1,
                  "maxMaleShare": 0.8 },
  "offFlavour": { "geosminPerDay": 0.01, "purgePerDay": 0.15, "purgeDaysMin": 6, "purgeDaysMax": 10 },
  "power": { "outagePerDayOneFeed": 0.00137, "outagePerDayTwoFeeds": 0.000137, "outageMedianMin": 45,
             "outageP90Min": 240, "gensetTested": 0.995, "gensetFloor": 0.85, "gensetDecayPerDay": 0.002,
             "gensetStartS": 23, "minutesTo5MgLAt40kg12C": 40, "minutesTo5MgLStressed": 15,
             "diffuserMinutes": 60, "manualStartBase": 0.4, "manualStartPerCompetence": 0.4 },
  "discharge": { "nPerKgFeed": 0.060, "nRetainedPerKgGrowth": 0.029, "particulateShare": 0.25,
                 "drumFilterEff": 0.85, "denitrificationRemoval": 0.5,
                 "inspectionFindingUnmonitored": 0.88, "inspectionFindingMonitored": 0.25 },
  "sludgeKgDmPerKgFeed": 0.175,
  "competence": { "start": 0.4, "perCycle": 0.2, "research": 0.1, "driftsleder": 0.1, "training": 0.15,
                  "consultancy": 0.1, "earlyHiring": 0.1, "utilisationCapStart": 0.55 },
  "growth": { "plan400gTo52kgMonthsMin": 8, "plan400gTo52kgMonthsMax": 9, "shortfallShare": 0.85,
              "shortfallDays": 30, "feedPlanFcrPenalty": 0.12, "feedPlanPenaltyDays": 60 },
  "postSmolt": { "salinityMinPpt": 12, "salinityMaxPpt": 35, "superSmoltG": 400, "largeG": 200,
                 "superSmoltMinPpt": 25, "desmoltDays": 60, "desmoltTransferMortalityMult": 2.0,
                 "salePriceNOKPerKgLive": 90, "tierPremiumLarge": 1.6, "tierPremiumSuper": 2.5 },
  "permits": { "zoningDaysMin": 365, "zoningDaysMax": 730, "landDays": 180, "dischargeDays": 240,
               "seaWorksDays": 120, "seaWorksClosedMonths": [4, 5, 6], "seaWorksFeeNOK": 44300,
               "biosecurityDays": 90, "appealDays": 180, "surveyRefusalCut": 0.3,
               "gridQueueDaysMin": 365, "gridQueueDaysMax": 1095, "gridFeeNOKPer10MW": 40000000 },
  "build": { "hfsFirstTanksDays": 670, "hfsCompleteDays": 910, "accelerateCostShare": 0.15,
             "accelerateTimeCut": 0.2, "preOrderDiscount": 0.10 },
  "depreciationYears": 20
}
```

```json
"facilities_offshore.json": {
  "of1": { "mtbT": 6240, "volumeM3": 250000, "zones": 3, "designHsM": 5.5, "capexNOK": 720000000,
           "teethingBudgetNOK": 157000000, "yardDays": 548, "voyageDays": 77, "mooringDays": 30,
           "certificationDays": 14, "siloT": 700, "silageT": 120, "crewInRotation": 12 },
  "havfarm": { "mtbT": 10000, "volumeM3": 414000, "zones": 6, "designHsM": 8.0, "capexNOK": 1000000000,
               "buildDays": 880, "siloT": 1500, "leeBonusMinM": 0.5, "leeBonusMaxM": 1.0 },
  "havgaende": { "designHsM": 15.0, "capexNOK": 1400000000 },
  "reinforcement": { "designHsBonusM": 1.0, "capexNOK": 60000000 },
  "opLimitsHsM": { "fishHandling": 2.0, "fishHandlingOffshoreWellboat": 2.5, "wellboatAlongside": 5.0,
                   "boarding": 2.0, "boardingDp": 3.0, "netInspection": 3.0, "resupply": 2.5, "resupplyDp": 3.0,
                   "tow": 2.5, "towHoldDays": 3, "appetiteFullHs": 4.0, "appetiteAt6m": 0.7,
                   "helicopterMaxWindMs": 25, "helicopterMinVisKm": 1.5 },
  "metocean": { "arPhi": 0.75, "arSigma": 0.30, "weatherDelta": [-0.30, 0.0, 0.55, 0.85],
                "muMonthOff1": [0.20, 0.15, 0.00, -0.25, -0.45, -0.60, -0.65, -0.55, -0.30, -0.10, 0.10, 0.20],
                "swellRatePerDay": [0.10, 0.09, 0.08, 0.05, 0.03, 0.02, 0.02, 0.03, 0.05, 0.07, 0.09, 0.10],
                "swellMedianM": 2.2, "swellSigmaLn": 0.30, "leadDaysMin": 4, "leadDaysMax": 7,
                "plotHsScale": { "off_1": 1.0, "off_2": 1.3, "hav_1": 2.4 } },
  "handling": { "warnDays": 21, "orderDays": 45, "withdrawalDays": 75, "orderActDays": 30, "appealDays": 120,
                "mtbReliefShare": 0.97, "netInspectionIntervalDays": 90 },
  "incidents": { "tiltBasePerDay": 0.000111, "tiltEscapeFraction": 0.015, "tiltFullDurationH": 12,
                 "autoCloseProcedureMin": 0.8, "mooringHazardOverDesign": 0.02, "mooringEscapeCap": 0.10,
                 "fatigueCoeff": 0.0005, "netDecayPctPerDay": 0.04, "netDecayPerMOver3": 0.2,
                 "netTearBelowPct": 70, "jellyDayShareNovDec": 0.05 },
  "alarmLoad": { "base": 0.15, "perDefect": 0.15, "perSensorPastService": 0.10, "perCrewOverdueDay": 0.02,
                 "reviewCut": 0.30, "reviewCostNOK": 4000000, "reviewDays": 30 },
  "teething": { "defectsMin": 3, "defectsMax": 4, "shakedownRevealPer30d": 0.5,
                "harvestZonePp": 3, "biologyPpMin": 4, "biologyPpMax": 8, "mixedSourcePp": 10, "ballastTiltMult": 3 },
  "costs": { "crewSalaryNOK": 900000, "charterNOKPerDay": 140000, "offshoreWellboatPremium": 1.5,
             "tugNOKPerDay": 150000, "maintenancePctCapex": 0.025, "insurancePctCapex": 0.008,
             "consortiumEquity": 0.35, "buyoutMultiple": 1.2, "depreciationYears": 25 },
  "licences": { "devLicenceT": 780, "devLicenceYears": 15, "conversionFeeNOKJan2026": 13804348,
                "conversionPaymentDays": 90, "fixedGrowthPriceNOKPerT2024": 170000, "appealDays": 180,
                "roundOpenDays": 730, "awardDelayDays": 180, "constructionStartDays": 365,
                "gateCompletedCycles": 1, "gateEscapeFreeDays": 365, "noveltyBonus": 0.3,
                "financingShareOfEquityCapex": 0.30 },
  "areaRound": { "moveRatio": 2.0, "termYears": 25, "commitmentLapseDays": 730, "slotsPerAward": 2,
                 "earliestGameDate": "2027-06-01", "flags": ["offshoreAreaRound", "oneCompanyPerArea", "offshoreRent"] },
  "lice": { "externalOff1": 0.12, "externalOff2": 0.10, "externalHav1Min": 0.05, "externalHav1Max": 0.30,
            "havfarmExternal": 0.30 }
}
```

```json
"facilities_closed_sea.json": {
  "semiClosedCapexPerUnitNOK": 50000000, "closedCapexPerUnitNOK": 60000000,
  "closedKWhPerKg": 3.87, "skirtExclusionSingle": 0.18, "skirtExclusionSite": 0.54,
  "closedDispensationMaxKgM3": 50, "semiClosedDispensationMaxKgM3": 35,
  "insuranceNOKPerKg": { "open": 0.21, "semiClosed": 0.53 }, "refitDays": 60
}
```

Source and GameBalance attributes carry the keys:
- O:R4, O:R6, O:R15, O:R17, O:R19, O:R23, O:R26, O:R37, O:R44, O:R46, O:R50, O:R53, V:2;
- L:R1, L:R2, L:R5, L:R12, L:S1, L:S7, L:S11, L:F2, L:F3, L:L4, L:T1, V:8, V:9, V:11.

### 3.10 Tests (add-only, with explicit stronger replacements)

**New .NET test files** under `src/LakskongenSim.Tests/`:
- `Integration/GoldenTraceTests.cs`, with `Fixtures/golden_traces/*.jsonl.gz` and `Fixtures/save_v3_facilities.json`
- `Integration/SiteKindAuditTests.cs`
- `Integration/FacilityMigrationTests.cs`
- `Farm/SitePlotCatalogTests.cs`
- `Finance/FacilityAssetRegisterTests.cs`
- `Core/DriftsplanTests.cs`
- `Environment/OffshoreMetoceanTests.cs`
- `Farm/OffshoreUnitTests.cs`
- `Integration/OffshoreCycleTests.cs`
- `Farm/OffshoreIncidentTests.cs`
- `Finance/DevelopmentLicenceTests.cs`
- `Finance/OffshoreAreaTests.cs`
- `Farm/ClosedPenPumpTests.cs`
- `Farm/LandWaterModelTests.cs`
- `Farm/PostSmoltBridgeTests.cs`
- `Finance/LandPermitTests.cs`
- `Integration/LandCommissioningTests.cs`
- `Integration/LandCycleTests.cs`
- `Farm/LandIncidentTests.cs`
- `Integration/FarmTypeStrategyTests.cs`
- `Integration/ReachabilityTests.cs`
- `Environment/TerrainPadTests.cs`

**Replacements.** Each replacement asserts everything the old test asserted, plus more. This is the D2 precedent. The PR checklist lists the old and new assertions side by side.

| Old test | Replaced by | Extra assertions |
|---|---|---|
| `FacilityProductionTests` offshore section (:184-202) | `OffshoreUnitTests` | The build creates an Offshore `SeaSite` with zones; lice model; escapes |
| `FacilityProductionTests` post-smolt section (:204-234) | `PostSmoltBridgeTests` | Tiers live; salinity |
| `SaveLoadMatrixTests` facility builds (:129-138) | The same test, builds through the new verbs | Every new kind and mid-incident state |
| `PersistenceGapTests` RAS part (:50-98) | Kept | New fields added to its round trip |

**Additions to existing tests**
- `SaveLoadMatrixTests` and `EngineTwinRunDeterminismTests`: one case per new kind and per mid-incident or pending-decision state.
- `CalibrationGuardTests`: the reflection guard (§3.9).
- `ContainmentTests`: the single skirt model and the stored credit factor.
- `TerrainGeneratorTests`: the golden re-promoted in F2-19 with evidence.
- `PlayerScriptProbe`: the five new families (F2-17).

**Other tests**
- Python:
  - `tests/test_land_kit_assets.py`, `tests/test_offshore_assets.py`, `tests/test_closed_pen_assets.py`, `tests/test_construction_kit_assets.py`;
  - `tests/test_vessel_assets.py` (list);
  - `tests/test_bridge_contract.py`: the legacy verbs may only be called from QA, and the new partials keep facade parity;
  - `tests/test_text_correctness.py` covers both encyclopedia files.
- `UiFlowTests.gd`, appended:
  - `_test_facilities_render`, `_test_farm_family_build_flow`, `_test_land_board`, `_test_window_board`, `_test_outage_decision`, `_test_deadline_slowdown`, `_test_first_sight`, `_test_unlock_reveal`;
  - the three replacements for `_test_ras_panel_stocks_and_harvests`, `_test_offshore_rows_are_legible` and `_test_offshore_sludge_button_sells_sludge`.

---

## 4. World placement

**Plot catalogue**
- `godot/data/site_plots.json` holds the **gameplay attributes** keyed by plot id, plus the procedural fallback positions.
- For G2-01 baked regions, positions come from `meta.json` `land_pads[]` / `offshore_slots[]`, which makes it the single geometric source.
- `TerrainGenerator` carves the pads from the same list, so headless and windowed heightfield hashes stay equal.
- **Intake reach is computed, never authored.** `IntakeReach(plot)` is the distance from the pad's quay edge to the nearest heightfield cell at −95 m or deeper, or to the world edge. Beyond the world edge, `bake_at` defines open ocean at 200 m depth. The deep-intake friction cost scales with that length.

**Land pads** (procedural geometry: mouth sill at z = −1300, strandflat beyond, world ±2000, basin floor −190)

| Plot | Centre (x, z) | Size (m) | Role | Attributes |
|---|---|---|---|---|
| `pad_a` "Storholmen" | (−450, −1720); moved by the F2-18 read gate from (−700, −1650), where the western headland hid 70% of the campus at the home view | 640 × 420, 4 lots | Hero campus: at full build-out 4 × (12 + 2) tanks + a post-smolt hall ≈ 64–70 tanks (LAND 1) | Shares a road with the shore base and the wellboat harbour (`channel_route` along −0.47). Quay on the fairway edge. Grid 40 MW, 1 feed. Sensitivity 0.2. Not pre-zoned in the sandbox **[GB]** |
| `pad_b` "Skjærvika" | (+750, −1640) | 480 × 360, 2 lots | Second campus or RAS hall | Grid 25 MW, 1 feed. Sensitivity 0.6, which raises refusal risk (after Verholmen [L:G4]). Longer deep intake (computed) |
| `pad_delta` "Elveøyra" | head delta, (0, +1450) | 260 × 180, up to 4 post-smolt modules | Post-smolt on **brackish or seawater**: a river intake plus a fjord-basin intake, blended to 12–35 ppt | Delta already flat at +2.5 m [code], so the pad is carved to +3.6 m. Pre-zoned **[GB]** |

- **Pad profile.** Each pad is carved to +3.6 m, the Indre Harøy plan level [L:R13], with 8–15 m cut faces and a 30 m blend.
- **Before and after.** An unbuilt pad renders as heath through a terrain `pad_state` uniform. The blast phase switches it to blasted granite #6B7378 (wet #3E4448) with rock props. The heightfield never changes at runtime.
- **Vegetation.** G2-02 vegetation excludes the pad masks.

**Offshore slots** (outside the bake, where `bake_at` returns open ocean with exposure 1). They are placed closer than revision 1 so they read from the home view. 3D distance is compressed, and the real distance drives logistics and labels.

| Plot | Position (x, z) | Exposure | Real distance | External lice | Hs100 | Depth |
|---|---|---|---|---|---|---|
| `off_1` "Ytre kyst" | (−450, −2750) | OuterCoast | 5 km | 0.12 **[GB]** | 5.0 m [O:R6] | 150 m |
| `off_2` "Havkanten" | (+850, −3400) | OuterCoastExposed | 12 km | 0.10 **[GB]** | 6.5 m **[GB; AOF approved 6.7 O:R19]** | 220 m |
| `hav_1` "Havområde" | (0, −5200) | OpenOcean (zone `hav_1`, outside the PO system) | 83 km (45 nm [O:R58]) | Band 0.05–0.30, drawn per game and revealed by operating there **[GB; O:R7 vs DNV caveat]** | 13 m **[GB, inferred from SFF design O:R12, O:R29]** | 300 m |

- **Design rating.** A unit may sit on a plot only if `DesignHs + bonus ≥ Hs100`.
  - OF1-class (5.5 m): off_1 only; off_2 after "Forsterkning" (+1.0 m).
  - Havfarm-class (8.0 m): off_1 and off_2.
  - The havgående variant (15 m): hav_1.
- **Scarcity is stated, not discovered.**
  - In scenarios with `rivalOffshore: true` (on everywhere except the demo scenarios), the first development round awards one rival unit, deterministic per seed. It takes off_2.
  - The map and the build flow show "1 av 3 havplasser ledig".
  - An offshore-area award (stage 3) creates 2 plots inside its polygon.
  - A rival exit through S4-01 distressed assets releases its plot.
- **Horizon.** In the −Z sector, the G2-01 far-field silhouette stays low (≤ 150 m apparent hills at ≥ 12 km), as in O3.

**Camera** (`FjordCamera.gd`)
- **Focus targets.** `register_focus_target(id, pos, framing_distance, pitch_deg, radius)`. Both clamps are extended with the union of the target discs: the focus clamp (`_world_half` ±1700) **and** the camera-position clamp (`_clamp_to_world` at `:247/262/268`).
- **Framings:**

  | Target | Distance | Pitch | Why |
  |---|---|---|---|
  | Full campus | 520 m | −40° | L1's angle, so lids read as a grid of circles |
  | One lot | 380 m | −40° | Same |
  | OF1 | 220 m | −18° | Low oblique as in O2/O3; the ring fills ~32% of frame width with the horizon 14% from the top (F2-18 measured; 300 m at −22° gave 24% and 4%) |
  | Havfarm | 700 m | −16° (amended by F2-21 from −25°: at the 700 m view's 45.6° field of view −25° put the horizon above the frame; −16° puts it 15.9 % from the top) | Whole hull in frame |

- **Horizon rule.** The G1-03 rule (horizon 10–25% from the top) holds at the FJORD/COAST defaults and the offshore framings. The land framings are FARM-like views and are exempt.
- **Bookmarks.** **G** cycles Fjord (home) → Land → Hav. It is rebindable (D15) and does not clash with Home = recentre or T = top-down.
- `fly_to_position` is reused.

**Map** (U3-03 MapPanel cartography)
- Land plots appear as pad glyphs with their permit state: free / søkt / godkjent / bygget.
- Offshore plots appear as hatched discs coloured by exposure class, with "1 av 3 ledig".
- The three real offshore areas (Norskerenna sør, Frøyabanken nord, Trænabanken [V:15]) appear as polygons once their regulatory flag opens a round.
- Rival-held plots carry the rival's colour band.

**Weather and exposure display**
- **Fjord:** the G3-03 sea-state lens (Shift+number) tints the water from the `sim_field` Hs channel.
- **Offshore:** the offshore slots lie outside the world-sized `sim_field`, so each unit carries a **chip**, for example "Hs 2,4 m · Tp 10 s · i morgen 3,1 · behandlingsvindu om 3 d".
- **Climatology at selection:** "Ytre kyst: forventet 1–3 uker uten behandlingsvindu per vinter".
- **Wave driver.** The EnvironmentDirector splits the target plot's Hs between wind sea and swell (Σa² = Hs²/8). A separate swell term with long bands (λ ≈ 160 and 280 m, beyond `SeaStateModel.BandWavelengthsM`' 128 m maximum) is added to the exposed-sea part of the water shader, so offshore swell reads as long, heavy swell rather than steep chop.
- **Weather drama.** Spray rises at the columns above Hs 3 m. Units heave on the G2-03 wave helper. Havfarm-class units weathervane toward the swell.

---

## 5. 3D assets

Assets are built headless by Python scripts, following the `build_vessels.py` export pattern.
- **Scripts:** `blender/build_land_base.py`, `build_offshore.py`, `build_closed_pens.py`, `build_construction_kit.py`, plus designs in `vessel_designs.py`, sharing `lk_meshkit.py`.
- **Build:** `Blender -b --factory-startup --python blender/<script> -- --asset --lod --out`. Builds are deterministic and byte-reproducible.
- **Export:** each LOD becomes a `.glb` with a JSON sidecar (triangles, measured dimensions, per-part triangles, per-part swatch report). Fixed node names form the Godot API. `generate_lods=false`.
- **One material per kit** (performance). Each kit exports one surface per LOD with a single `LK_FacilityKit` material (vertex colour plus a 1k trim atlas); the offshore kit uses `LK_OffshoreKit`. The named colours below are swatches in that atlas, verified by the sidecar's swatch report. Per-instance variants (lid colour, band, build progress, rotor phase) go through `INSTANCE_CUSTOM` in `facility_kit.gdshader`.
- **Instancing.** One `MultiMeshInstance3D` per tank type per LOD band **for the whole campus**. Static buildings are merged per lot. **LOD2 casts no shadow; buildings and units cast none from LOD1 on, but tanks cast in the LOD1 band** (F2-18: at the land framing all 64 tanks stand in that band, and without shadows they float).
- **Checks:** a manifest entry per asset (5% tolerance), preview renders at the framings in §4 and at the home view, and an evidence doc per item with reference side-by-sides. Owner references are never committed.
- **Folders:** `godot/assets/models/{land_kit,offshore,closed_pen,construction_kit}`, with shared materials in `facility_kit/`.
- **Budgets confirmed by the gray-box gate.** F2-18 runs **before** F2-25/F2-26 spend their triangle budgets, and it may change the budgets below.

### 5.1 Land kit

| Asset id | What | Dimensions (m) | Swatches (hex) | LOD0 / LOD1 / LOD2 tris |
|---|---|---|---|---|
| `tank_growout` | Shell, flat lid, rim walkway 1.2 m + handrail 1.0 m, ring stiffeners, lid hatch, lid work-lights; nodes `Wall`, `Band`, `Lid`, `Walkway`, `Feeder` | Ø28.0 × 13.0 shell; water 8.0 [L:R3, L:R5] | Wall #C9CED1; band #5E7A6A (lower 4 m, ref L2); lid #2A3036 (default, ref L1) or #EDEFEF (cosmetic, ref L2) | 2,400 / 600 / 96 |
| `tank_pregrow` | Same build; also the post-smolt tank | Ø16 × 9 (artist est. 14–18 × 8–10) | Same | 1,600 / 400 / 64 |
| `tank_feeder` | Rotor feeder hopper at the lid centre | Ø1.6 × 1.2 | #8A9096 | 220 / 60 / (in tank LOD2) |
| `process_hall_bay` | Repeatable hall bay with 2 roof vents and lit windows | 30 × 20 × 10 (artist est.) | #D5D9DB, roof #4A5056, window emissive #F2D8A0 | 900 / 160 / 12 |
| `harvest_hall` | Big dark hall, loading doors, blank logo panel | 120 × 60 × 15 (artist est.) | #3A4046, #D5D9DB | 3,000 / 450 / 24 |
| `pipe_bridge_span` | Truss span on legs; 2 pipes Ø0.9 + conveyor; leg height 6–14 parametric | 25 span, 3.5 × 3.5 section (artist est.) | #D9622B, pipes #9AA0A6 | 1,800 / 320 / 24 |
| `quay` | Concrete quay with wellboat berth, bollards and fenders | 120 × 25, deck +3.0 | #9EA3A6, fenders #1C1E20 | 2,500 / 400 / 30 |
| `intake_basin` | Curved basin wall (arc R 45 m, 140°) + pump house 22 × 12 × 9 | Wall 2 m thick, top +3.0 | #9EA3A6, #D5D9DB | 2,200 / 380 / 40 |
| `river_intake` | Weir and screen house (pad_delta) | 30 × 8 × 5 | #9EA3A6 | 600 / 120 / 12 |
| `lox_tank` | Vertical vacuum-insulated tank with legs and ladder | Ø3.2 × 10.5 (artist est.) | #E9ECEA | 640 / 120 / 12 |
| `genset_container` | 40 ft genset with exhaust stack | 12.19 × 2.44 × 2.90 | #4A5650 | 320 / 60 / 12 |
| `substation` | Transformer yard and fence | 18 × 10 × 6 | #D9622B, #8A9096 | 1,500 / 220 / 12 |
| `feed_silo_land` | Cluster of 4 silos | 4 × Ø4 × 14 | #8A8F96 | 900 / 160 / 12 |
| `sludge_station` | Dewatering house + container bay | 20 × 12 × 8 | #D5D9DB, #6E7B73 | 800 / 150 / 12 |

**As built (F2-25).** Each LOD is one node named by the asset id with one `LK_FacilityKit` surface, so a whole campus instances per tank type. The part names above (`Wall`, `Band`, `Lid`, `Walkway`, `Feeder`...) are trim-atlas strips: the shader swaps the lid swatch and drops the band per instance, stretches the pipe-bridge legs (a 10 m reference, `INSTANCE_CUSTOM.b`) and spins the feeder's rotor; the sidecar lists each part's triangles and swatch. `tank_feeder` is its own mesh at LOD0/LOD1 and rides in the tank at LOD2. The dark lid keeps the full shell diameter with the walkway cantilevered outside it (F2-18: the 40 px lid ellipse has no margin). Evidence: `docs/run/evidence/f2_25_land_kit.md`.

**Layouts**
- **Full HFS lot:** 12 grow-out tanks in two rows of 6, 4 m apart (each row 188 m long); a hall between the rows; 2 pre-grow-out tanks at the end of the rows. Footprint about 200 × 90 m.
- **Pilot lot:** 4 grow-out tanks plus 1 pre-grow-out tank in the first row positions, with the hall stub. Expanding fills the rest.
- **RAS template:** two 180 × 60 m halls (the `harvest_hall` geometry in pale cladding) plus the outdoor kit. It deliberately reads as boxes, distinct from HFS.
- **Post-smolt module:** 2 `tank_pregrow` units under a lean-to bay. Up to 4 modules sit side by side.

### 5.2 Offshore

| Asset id | What | Dimensions (m) | Swatches | LOD0 / LOD1 / LOD2 |
|---|---|---|---|---|
| `of1_class` | **Polygon count is an owner ruling before F2-26**: 12-gon per [O:R6] (default) or the 8-gon read of O2, shown side by side in evidence and recorded in RUN_LOG §7. Circumradius 55 (Ø110). Overall height 68; draft 43, so ~25 m above the waterline. 12 side columns alternating Ø3.56 (with cone-ended pontoon feet) and Ø2.8; centre column Ø3.56. Rings: top 2.29, bottom 2.05, mid 1.0. Mid diagonals 1.0 zig-zag in every bay; bottom radial beams 1.75; top cross gantry 2.05 with a 2 m walkway. Sliding bulkhead. Control/accommodation block 14 × 14 × 10; 4 silos Ø4 × 10; 2 knuckle cranes reaching 18 m; handrails 1.1 (artist est. where unsourced). Nodes: `Hull`, `Columns`, `Bracing`, `Topsides`, `Net`, `Bulkhead`, `Cranes`, `NavLights`, `Beacon`; part groups `Pontoons → Columns → Rings → Bracing → Topsides → Net` | [O:R6] | #E6BE2E; splash zone 0–4 m #B8902A; topsides #E9ECEA / #80838D; silos #777887; net via `pen_net_shader` (alpha-to-coverage), base #A8ADB0, warming toward #85604A in low sun through lighting rather than albedo; nav lights emissive #FFC23A | 60,000 / 12,000 / 1,500 |
| `havfarm_class` (stage 3) | Hull 385 × 59.5, moulded depth 37.75, freeboard 11 (artist est.). Six 47 × 47 wells in a row, nets to 56. Bow turret Ø14. Aft superstructure 40 × 59.5 × 18 (artist est.); silo row; edge nav lights | [O:R23, O:R25] | Hull #34424B **provisional**: hull colours are unverified, and the evidence doc must check press photos before sign-off. #E9ECEA, rails #E6BE2E | 60,000 / 12,000 / 1,500 |
| `mooring_line_kit` | Chain catenary + fibre segment + anchor. OF1: 8 lines in 4 pairs, fairleads −30 m, anchors at 1,100 m [O:R6]. Havfarm: 11 lines at 650 m [O:R23] | — | #3A3F44, fibre #C8C2B0 | 400 / 60 per line (underwater and in the lens only) |
| `offshore_service` | DP service vessel with walk-to-work gangway, aft crane, A-frame | 75 × 17 (artist est.) | Hull #C9531F (sampled #BF551B), #E9ECEA | 30,000 / 6,000 / 800 |
| `anchor_tug` | `offshore_service` mesh with a red hull | — | Swatch variant, no new geometry | — |
| `crew_boat` | Catamaran | 20 × 7 | #E9ECEA, #34424B, orange trim | 8,000 / 1,500 / 300 |
| `rov` / `auv` | Subsea props | 2.6 × 1.6 × 1.5 / 4.0 × Ø0.5 | #E6BE2E / #C9531F | 1,200 / 300 · 600 / 150 |

**As built (F2-26, F2-27).** `of1_class` follows the owner's 12-gon ruling (the 8-gon only in the evidence side-by-side): feet at the 43 m draft, rings at −35 / −10.5 / +14, decks at +15.4, structural height 68.4 m (feet to the control block and silo tops), mast and `Beacon` at +28.2; 46.6 k / 5.5 k / 1.2 k triangles. LOD0 keeps the node names above plus `Cranes/CraneA`, `Cranes/CraneB` (origins on their slew axes) and a `W2WLanding` marker; LOD1 and LOD2 are `Hull` + `Net`, LOD2 only what stands above z = −3 m. Solids share `LK_OffshoreKit` (`offshore_kit.gdshader`, a 15-strip trim atlas built with the land kit's code), nets `LK_OffshoreNet`; the part groups ride in TEXCOORD_1 and `OffshoreUnitModel.set_build_stage()` reveals them; the splash zone is painted from −2.5 to +4 m for heave. The fleet runs through `build_vessels.py`: `offshore_service` (its gangway a LOD0 node `Gangway`), `anchor_tug.tscn` (Hull material overrides only), `crew_boat`, and `rov` / `auv` with two LODs. Evidence: `docs/run/evidence/f2_26_of1.md`, `docs/run/evidence/f2_27_offshore_fleet.md`.

### 5.3 Closed-pen kit (stage 3) and construction kit (supersede G5-02)

| Asset id | Dimensions (m) | Swatches | LOD0 / LOD1 / LOD2 |
|---|---|---|---|
| `closed_donut` | Torus for 22,000 m³ [O:R46]: major radius 17, tube radius 8.1 (derived from V = 2π²Rr²), outer Ø50, freeboard 3 | #8E979C | 12,000 / 3,000 / 400 |
| `closed_globe` | Sphere for 3,500 m³ [O:R47], Ø18.8 (derived), 5 m above the waterline | #E9ECEA | 6,000 / 1,500 / 200 |
| `semi_closed_ring` | Steel collar Ø40 (artist est.) + 13 m skirt [O:R44] | #8A9096, skirt #C9CCC6 | 15,000 / 4,000 / 600 |
| `pump_module` | 6 × 3 × 3 + intake hose to 15–40 m [O:R46, O:R47] | #8A9096, #D9622B | 1,200 / 300 |
| `tower_crane` | Mast 50, jib 50 | #E6BE2E (temporary saturation) | 2,500 / 300 / 40 |
| `scaffold_ring` | Ø29.5 × 13, instanced on rising tanks | #8A9096 | 1,500 / 200 |
| `excavator`, `blast_mats` | Excavator 10 × 3.5 × 3.5; mats as a decal stack | #E6BE2E, rubber #1C1E20 | 2,000 / 300 |

**Yard picture-in-picture and the timelapse.** For offshore construction, the part groups are revealed phase by phase. The First Sight timelapse replays the same part groups (land: tank walls through the `INSTANCE_CUSTOM` build progress) from `ConstructionProject.PhaseCompletedDays`. No extra meshes are needed.

**As built (F2-28).** `tower_crane`, `scaffold_ring`, `excavator` and `blast_mats` come from `blender/build_construction_kit.py` (geometry in `construction_designs.py`) on the land kit's one material and atlas: each LOD is one mesh with one `LK_FacilityKit` surface, and the kit adds one atlas strip, `crane_yellow` #E6BE2E, appended as row 27 so the land kit's rows keep their indices. The crane has a 50.04 m mast, a 50.04 m jib and a 17 m counter-jib (1,620 / 274 / 34 triangles); the ring is Ø29.54 × 13.0 m (1,000 / 128) and clears the committed `tank_growout` plinth (R 14.3 m) by 5.6 cm; the excavator is 10.0 × 3.5 × 3.5 m (662 / 130); the mats are a 12 × 8 m two-layer blanket (264 / 50), a size the spec does not give (artist estimate). The excavator and the mats each get the row's 2,000 / 300. `construction_phase_sets` in `blender/asset_manifest.json` lists the assets of all 14 phase rows in §6.3, cross-checked against this text and the engine's phase keys (the tow uses F2-27's `anchor_tug.tscn`; mooring lists `mooring_line_kit` as pending for F2-29). The ring is as tall as the shell, so the build progress reveals it with the wall; it stands in the height band of the tank's walkway and must be struck when `tank_rings` ends (the walls then stand at `WALLS_TOP_PROGRESS`, below the walkway). **Drawn on the land campus** by `ConstructionKitVisual` (`godot/scripts/visual/`, a child of `LandCampusBuilder`), from the phase keys of the engine's view alone, so the first-sight timelapse plays it too: an excavator beside a blanket of mats in the middle of the blasted ground while the pad is blasted (the first 60% of `site_works`); a scaffold ring on each grow-out tank through `tank_rings`, revealed by the tank's own build progress and struck when the phase ends; a tower crane at the inland end of each lot through its `treatment_hall`, none on a post-smolt site nor where the pad leaves no room behind the lot. The ring's LOD1 is thickened (standards 0.5 m, a 0.6 m top band, platforms 0.3 m wider: outer Ø30.1, the same 128 triangles) so that it reads from the 350 m where it is drawn. The blast-day dust puff still has no owner. Evidence: `docs/run/evidence/f2_28_construction_kit.md`, `docs/run/evidence/construction_kit_presenter.md`.

### 5.4 Animation (compositor-friendly; vertex-shader driven for instanced parts)

- **Land.**
  - Tank rotor feeders spin at 0.5 rev/s while feeding, using an `INSTANCE_CUSTOM.a` phase.
  - The conveyor belt UV scrolls during harvest and transfer.
  - Tank walls "rise" through `INSTANCE_CUSTOM.r` build progress, discarding fragments above progress × 13 m.
  - **Lid work-lights and hall windows light at every dusk and night** (presentation clock, D20), not only during First Sight.
  - Roof vents steam below −2 °C air.
- **Offshore.**
  - Heave and roll from the G2-03 wave helper, damped for OF1-class.
  - Havfarm-class yaw eases toward the swell direction.
  - Cranes slew during resupply.
  - Nav lights blink in sync at 1 Hz at night (Kystverket synchronised yellow [O:R58]). The `Beacon` stays visible as a landmark down to fog transmittance 0.4.
  - Tilt incident: a ≤ 3° list pose until resolved.
- **Vessels.** The service vessel and crew boat bob with wakes (G2-03). Anchor tugs hold station during mooring and the tow.

### 5.5 How assets read at the default camera (with foreshortening and fog)

**Geometry.**
- The FJORD default is 900 m at −17°, so the camera stands ≈ 263 m up.
- With a vertical FOV of 45° at 1080p, px/m ≈ 1304/D.
- The depression angle to an object is atan(263/D), and a horizontal disc is foreshortened by sin(depression).
- F2-18 records the real distances from the shipped `home_focus`; the values below are representative. **Measured (F2-18, `docs/run/evidence/f2_18_read_gate.md`):** the home camera stands at (−385, 263, −322); pad_a (moved) 1.42 km, a tank 28 × 14 px with a 4 px lid, the campus 519 × 66 px; pad_b 1.76 km, a tank 25 × 11 px with a 2 px lid; OF1 2.44 km, 69 × 18 px; Havfarm 3.33 km, 153 × 17 px; the land framing lid ellipse 66 × 40 px.

| Object | Distance D | Width | Height on screen | Read |
|---|---|---|---|---|
| Ø28 tank | 1.8 km | 20 px | wall 9 px, lid ellipse 2.9 px | Pale drum, lid barely visible |
| Ø28 tank | 2.4 km | 15 px | wall 7 px, lid 1.7 px | A strip of pale stubs, **not** a grid of circles |
| Full campus | 2.4 km | ~350 px | ~25 px deep band | Landmark band at the mouth, carried by the dark harvest hall and the orange bridge |
| Tank at the land framing (520 m, −40°) | — | 70 px | lid ellipse 45 px | Grid of circles, as in L1 |
| OF1 | 2.9 km (off_1) | 49 px | ~12 px (topsides 11, ring 4.5) | Yellow sliver near the horizon row |
| Havfarm | 3.5 km (off_2), broadside | 143 px | ~7 px | Long dark bar with a white superstructure |

**Fog** (EnvironmentDirector, Koschmieder σ = 3.912/visibility). Transmittance at 2.9 km:
- overcast (20 km visibility): 0.57;
- rain (4.5 km): 0.08;
- snow (2.5 km): 0.011;
- storm (2 km): 0.003.

**Read rules**
1. **The home view shows landmarks, and the focus views show detail.** At the home view, land reads as a lit, banded campus. Offshore reads as a yellow sliver plus a synchronised beacon at night.
2. **A fixed-size status chip** (G3-03 marker, Ink chip with a shape glyph, **never a yellow fill**) appears over any unit whose fog transmittance falls below 0.4, **or while the sea-fog layer is up (`sea_fog ≥ 0.5`) and the unit lies beyond the layer's visibility**. This covers rain, snow, storm and sea fog, exactly when offshore drama happens. (F2-18: in sea fog the distance-fog transmittance stays at 0.42–0.69 while only 19–27% of the contrast survives, because the layer is height fog that `transmittance()` leaves out.)
3. **The bookmark key G** brings the player to the detail views in one press.
4. **LOD distances:** tanks LOD0 below 350 m and LOD1 below 1,200 m; units LOD0 below 500 m and LOD1 below 2,000 m.
5. **Budgets, against a P0-04 baseline recorded with 12 sea sites before F2-20** (measured at the P0-04 home view; at a unit's own framing, where its LOD0 and shadows are drawn, the counts measured at the wave-4b merge are regression ceilings, `tests/test_facility_presenter.py`):
   - a full 4-lot campus adds ≤ 20 opaque + ≤ 16 shadow draw calls;
   - one OF1-class unit plus its vessel adds ≤ 8 opaque + ≤ 6 shadow;
   - a full campus is ≤ 250 k LOD0 triangles inside 600 m;
   - added p95 ≤ 1.5 ms at FJORD with a campus, 2 units and 12 sites, and absolute p95 ≤ 16.7 ms.
6. **Colour semantics.**
   - The hull (#E6BE2E) sits near buoy yellow and the UI warn amber (#F2B705).
   - Status markers over offshore units are therefore Ink chips with glyphs, and a lint requires ΔE ≥ 20 between any world-anchored marker and the hull swatch.
   - The orange accents never carry status.

**As built (F2-19, F2-20).** The pads are carved by `TerrainGenerator.CarvePads` (golden `a7b58a2c48dd4a8f`, headless = windowed). The presenter (`FarmFacilityVisualManager`, `LandCampusBuilder`, `OffshoreUnitVisual`, `FacilityWorldCard`) draws `SimulationEngine.FacilityVisualViews`: the engine's lots and tanks and their construction projects. The §6.3 land rows map onto the land lane's phases as built (`site_works` = blast pad, then foundations over its last 40 %; `tank_rings`; `treatment_hall`; `sea_works` = intake, outfall, quay and bridge; `first_tanks` = commissioning, the first half of the grow-out tanks handed over; `completion` = the rest); the offshore rows are the sea lane's (`yard_contract`, `hull`, `voyage`, `tow`, `mooring`, `commissioning`). A shell under construction draws double-sided (`LK_FacilityKitBuild`). The quay side is laid out from the pipe bridge's line on every pad (the basin on one side, the harvest hall 10 m off it on the other) and every pad and lot count the engine allows is checked clear (`CampusLayoutCheck`, wave4b review). Evidence: `docs/run/evidence/f2_19_pads.md`, `docs/run/evidence/f2_20_presenter.md`, `docs/run/evidence/wave4b_int_gfx_farm_1.md`.

---

## 6. UX

### 6.1 Unlock and reveal

The player chooses the order; Land and Hav are parallel branches [UX-U6]. **Each locked card lists every gate, money included, with a progress bar for each**, for example "Fullfør 1 produksjonssyklus i sjø ✓ · 12 mnd uten rømming 8/12 · Forsk: Havbruk til havs · Finansiering 41/96 MNOK (35 % konsortium)".

1. **Teaser** (presentation only, with no sim state).
   - In `forste_sesong` it plays once, at the first moment after day 150 when no tutorial step is active (skipped if none comes before day 300).
   - A parody rival's yellow hull crosses the horizon beyond the mouth under tow and sails out of sight toward hav_1.
   - A news card follows, the Leksikon entry unlocks, and a locked "Hav" card appears.
   - It never offers a build and never changes engine state. This makes it a wishlist hook in the demo.
   - In full scenarios with `rivalOffshore: true`, the rival award in the first development round is a sim fact. The rival's unit then **teaches**: deterministic news about its teething defect, its winter without a handling window and, on some seeds, a tilt escape. The player sees the failure modes before committing 0.9 bn.
2. **Act 2**, after the first harvest:
   - semi-closed and closed upgrades on the Sjø pen card;
   - the **Postsmoltmodul** once `land-based-ras` is researched. This is the first entry rung: 110 M, 60% loan pre-filled.
3. **Act 3, two parallel entries.**
   - **Hav:** the development-licence round opens after **1** completed sea cycle with 12 escape-free months, and stays open for 2 in-game years [UX-U2].
     - The dossier fixes the concept (OF1-class), the plot (off_1) and the target criteria.
     - **Before submitting, the player sees their score breakdown against the rival field band** ("Sterk / Middels / Svak"): novelty +0.3 for the first of its class; competence (escape-free months, lice compliance); financing (cash + credit ≥ 30% of the equity share of capex) **[GB]**.
     - The award arrives after 180 d, and construction must start within 365 d.
     - **The consortium option** (35% equity, partner capital for the rest; buy-out later at book × 1.2) is the money rung.
   - **Land:** the pilot lot (0.9 bn) unlocks with `land-based-ras`, an operating post-smolt module and a zoned pad [UX-U4]. Pre-zoned pads in scenarios skip the zoning year.
4. **Act 4:**
   - conversion of the development licences (criteria, appeal, payment, placement);
   - expansion to full lots;
   - stage-3 variants (RAS hall after one HFS cycle; Skipsmerd after one OF1-class cycle);
   - the offshore-area round behind its regulatory flag, no earlier than in-game June 2027 [UX: iLaks 2026].

**Research gates.** `land-based-ras` and `deep-sea-farming` gain an `unlocks` field. Their effects narrow to their own unit kinds:
- `land-based-ras`: +0.1 competence on land sites;
- `deep-sea-farming`: −10% external lice pressure on offshore sites.

### 6.2 Site selection and placement

U2-01's `BuildSiteDialog` gains a first step, "Anleggstype": **families first, variants second**.

1. **Family cards** ("Sjø", "Land", "Hav"), each with its variants in short names.
   - **Comparison as aligned bars with pips** (no radar), on the same axes for every variant:
     - capacity cost per t HOG/yr including the licence;
     - farming cost NOK/kg HOG (median and bad case);
     - months to first harvest;
     - survival (median and bad case);
     - lice treatments per year;
     - weather exposure;
     - technical-failure exposure;
     - energy kWh/kg and CO₂;
     - licence route.
   - Every value comes from `GetFarmTypeCatalog()`. Before F2-17 these are sourced reference values tagged "bransjetall"; after it, measured game medians.
   - **Diagnosis strip:** "Din flaskehals: lus i PO3 (rød) → Land eller Hav fjerner den". It is computed from engine data using the vision table's "what limits you".
   - A link opens the matching challenge scenario ("Prøv Oppkjøringen").
2. **Site lens.**
   - Land: pads with intake reach, pump capacity, grid MW and feeds, zoning state, sensitivity, **refusal probability with its drivers**, blasting cost and quay.
   - Hav: plots with exposure class, Hs climatology ("forventet uker uten behandlingsvindu per vinter"), real distance, external-lice value or band, and free/held state.
3. **Ghost placement** (free; no engine state). A translucent asset sits on the plot with pipe routes to the intake, or the mooring footprint ring for offshore.
4. **Preview card.**
   - Capex and **payment schedule per phase**: managed (with its p50/p90 overrun band) vs turnkey (+12%, no overrun).
   - **36-month cash runway chart:** existing site income, phase payments and the pre-filled loan. **Payback year.**
   - Farming cost NOK/kg HOG at 55% and at full utilisation, and the "Oppkjøring" ramp.
   - **Licence value line (Hav):** "Utviklingstillatelser: 8 × 780 t, markedsverdi ≈ 1,9 mrd (auksjon 2024 ≈ 305 k/t); konverteringsgebyr ≈ 110 M; ingen grunnrente før konvertering".
   - **Permit timeline (Land):**
     - zoning 1–2 years unless pre-zoned;
     - land permit 180 d ∥ discharge 240 d ∥ sea works 120 d (no sea works in April–June) ∥ biosecurity 90 d;
     - grid project if needed.
     - Each case shows its refusal probability, drivers and mitigations: environmental survey, denitrification commitment, smaller first phase.
   - **Teething deck (Hav):** "3–4 ukjente barnesykdommer; budsjett 157 M; prøvedrift 60–120 d avslører dem".
   - The **"Dette må du følge med på"** strip previews the instrument.
5. **Confirm.** Land submits permit cases; Hav submits the dossier or starts the project. The result shows in 3D at once (blast flags, survey buoys).

### 6.3 Construction in 3D

Construction shows as world state with a progress ring on the anchored card, never as a percentage in a list row [UX-B5]. **Payoff beats are presentation events** (F2-22):
- each has its own real-time length of 6–15 s;
- they are queued and independent of sim speed;
- a camera-nudge chip ("Se slepet", "Se første fisk") flies the camera on click, and there is no forced cut;
- each phase completion posts a thumbnail toast that flies the camera there on click.

| Land phase | Visible in 3D | Player decision |
|---|---|---|
| Blast pad | Heath splat → blasted granite; blast mats; dust puff on each blast day | Accelerate (+15% cost of the remaining phase, −20% time) |
| Foundations | Concrete rings on the pad | Pre-order the next lot's tank elements (−10%, capital locked) |
| Tank rings rise | Walls rise through build progress; scaffold rings | Hire staff now (+0.1 competence, wages start) |
| Treatment hall | Hall bays appear; tower crane | — |
| Intake and outfall | Basin wall, pump house, pipe lines in the lens; quay | The sea-works window (not in April–June) |
| Commissioning | Lights on; biofilter maturation gauge | — |
| First smolt | Wellboat at the quay; the orange bridge carries fish | — |

| Offshore phase | Visible in 3D | Player decision |
|---|---|---|
| Yard contract | Picture-in-picture card of the asset | Managed, turnkey or consortium |
| Hull rises | Part groups appear phase by phase | — |
| Voyage | The unit approaches on the horizon | — |
| **Tow to the plot** | 3 anchor tugs | **"Start slep nå: P(vinduet holder 3 døgn) = 0,72"** from the forecast. Bad luck means sheltering for 5–15 d at charter cost, never structural loss |
| Mooring | Tugs at the anchor points; lines in the lens | — |
| Commissioning | Nav lights; crew boat | **Shakedown** 0 / 60 / 90 / 120 d |
| First stocking | Wellboat alongside | — |

### 6.4 First sight

- **Rules.**
  - One sequence of ≤ 20 s, skippable with Esc or a click, with sim time paused and resumed exactly.
  - It **opens with a 3–5 s build timelapse** replayed from the recorded phase states.
  - A repeat build of the same variant gets a 3 s toast.
  - With "Reduser bevegelse" on, it is a still with a 2 s cross-fade.
- **Land (LAND 2 mood).** Dusk with a pink-violet sky (the "Blåtime" preset). The camera starts low over the quay, follows the orange bridge to the tanks as the lid work-lights come on, then pulls up to −40° to frame the grid of circles, with the fjord pen grid behind it.
- **Offshore (O3 mood).** Low winter sun. The camera starts behind the orange service vessel and rises to the low oblique framing of the yellow ring on deep blue, with the fjord mouth and snowy hills on the horizon. The wellboat arrives.
- **Photo mode.** Both sequences end with the photo-mode prompt [UX-F3].

### 6.5 Instruments

Each instrument is a 3D lens first and a docked Ink panel second (U3-01, U3-03 kit). Status always uses colour plus shape (● ok, ▲ warn, ■ crit). **Every board leads with "Neste planlagt" (from the Driftsplan) and the exceptions.**

**Land board ("Tankkart")**
- **Default view:** one card per lot or post-smolt module, plus one per cohort group.
  - Each card has a single composite **"Vannmiljø"** glyph that names the binding constraint ("Begrenset av CO₂"), as in a bottleneck readout or Planet Zoo's worst-need view.
  - Exceptions sort first.
  - With 4 lots, the default view shows ≤ 12 glyphs.
- **Expanding a card** shows the tank grid in the 3D layout order. Each tank is a 5-segment ring (O₂, CO₂, TAN, temperature, density) against its comfort band. Per-tank verbs appear for exceptions only.
- **Side blocks:**
  - **Vannstrategi:** 3 named presets, each with predicted kWh/kg, growth and maturation risk from the engine's projection: Sparsom (r 0.75, max deep share, 12 °C), Balansert (r 0.65, seasonal blend, 12–13 °C), Høy tetthet (r 0.80, max deep share, 13 °C). The sliders (reuse, 25 m ↔ 95 m blend, setpoint, salinity for post-smolt) sit behind "Avansert".
  - **Strøm og oksygen:** grid feeds and any grid project; genset reliability with the test interval from the Driftsplan (cost shown); LOX days; diffusers.
  - **Biofilter:** type, health, maturity.
  - **Utslipp:** N year-to-date vs cap; next inspection.
  - **Energi:** kWh/kg over 30 days; NOK/day.
  - **Kvalitet:** maturation risk; off-flavour and purge status; growth vs plan.
- **Harvest planner** ("Du bestemmer når du selger"): tank groups mapped to harvest weeks against the forward seasonal price curve (`MarketSimulation` monthly offsets), with the S2-04 size grades.
- **Post-smolt planner:** weight vs target, salinity, tier preview (Standard / Large / SuperSmolt), destination MTB check, wellboat slot, and **sell vs transfer**. The preview shows the lice cycles saved at sea.
- **First-harvest report card:** land vs your fjord sites on survival, treatments and NOK/kg. This is the payoff moment.
- **Oppkjøring:** utilisation milestones 55 → 70 → 85 → 100% with a target date and the competence levers.

**Offshore weather-window board ("Værvinduer")**
- A 10-day Hs fan chart (p10/p50/p90) with limit lines at 2.0 / 2.5 / 3.0 / 5.0 m and the design-Hs line.
- Operation rows: Fôring (silo-driven), Påfyll, Mannskapsbytte, Notinspeksjon/ROV, Brønnbåt (stocking or transfer), Avlusing/slakt.
- Each cell: P ≥ 0.8 solid green ✓; 0.5–0.8 amber hatched ~; < 0.5 red ✕.
- **Default path:**
  - "Book første grønne vindu" per row;
  - Driftsplan auto-booking (silo buffer target, crew rota, inspection cadence, auto-filed half-year reports).
- **Power path:** dragging jobs into cells. **Entering drag mode pauses time.** A keyboard path (arrows + Enter) books too (D15).
- **Booking preview:** expected charter cost **including slipped days**, P(done by day X), silo days at that day, and the vessel-class choice (standard / DP + walk-to-work / offshore wellboat).
- **Gauges:**
  - **days without a handling window** against 21 / 45 / 75 while a requirement is open;
  - MTB headroom;
  - silo days;
  - crew clock;
  - **alarm load** with its drivers (unfixed defects, overdue sensor service, crew overdue) and the "Alarmgjennomgang" action;
  - net and structure "known as of" day;
  - MRU trend;
  - sensor ladder.

**Closed-pen strip** (pen anchored card): pump state, O₂ in vs out, kWh/day, grid headroom, dispensation state, "Nødspyling".

### 6.6 Alarms and counter-play

Alarms are routed through U2-06. Every alarm has before, during and after counters [UX-FD2], and **warnings respect the deadline rule** (§3.5):
- auto-slow to 1× at ≤ 7 sim-days;
- auto-pause at ≤ 1 day with no response;
- the toast shows the deadline as a sim date with a countdown bar.

- **Land power outage.**
  - **Covered** (the genset started, or the standing order covered it): an info entry and a "Lyset holdt" report.
  - **Uncovered** (after S4-08): a critical modal with time paused. The modal states "~40 min til O₂ < 5 mg/L" as information.
  - Each option shows its **projected DO curve, expected loss and cost** from a deterministic twin-lite projection:
    - "Start aggregat manuelt": success probability = 0.4 + 0.4 × competence, shown with its expected value;
    - "Åpne nødoksygen";
    - "Kutt fôring";
    - "Nødslakt".
- **H₂S.**
  - Chronic warning at 0.3, with "Rens / Øk vannutskifting / Kutt fôr / Doser nitrat" (nitrate dosing shows its discharge cost).
  - It drifts for **≥ 7 sim-days** before the acute event.
- **Storm swell ≥ 4 days ahead.** Warning with quick actions "Fyll siloer", "Avlus nå", "Slakt nå".
- **Handling overdue** at 21 (warning), 45 (Mattilsynet order, critical), 75 (withdrawal case). Each step shows the next window on the fan chart and the vessel options that would open it.
- **Ballastavvik.** The counter is procedure, not reflex: the standing order closes the hatches when HatchProcedureLevel ≥ 0.8. The report explains whether alarm load let the warning through.
- **After every critical event:** an **incident report** with ≥ 3 causal steps, the drivers behind each probability (nested tooltips, U3-04), the losses and the unlocked procedure upgrade. An example chain: defect left unfixed → alarm load 0.6 → ballast class silenced → no warning → 9-hour tilt → 1.1% escape.
- **Notification budget** (measured in F2-17): a policy-competent player sees ≤ 2 warnings per unit per in-game month and ≤ 1 critical per unit per in-game year.
- **Drill:** the first incident on each new land site is a drill with no loss.

### 6.7 Leksikon

- **File.** New entries go in the player-facing `godot/data/encyclopedia.json`: bilingual `en`/`nb`, fields `id`, `category`, `icon`, `title`, `paragraphs`, `related`. The file is D17-checked by `test_text_correctness.py`.
- **Category.** A new category `production` ("Production systems" / "Produksjonsformer").
- **Entries (19):** `land_based_farming`, `hybrid_flow_through`, `ras_systems`, `post_smolt`, `intake_water`, `biofilter`, `hydrogen_sulphide`, `off_flavour`, `early_maturation`, `discharge_permit`, `offshore_farming`, `semi_submersible`, `ship_farm`, `significant_wave_height`, `weather_window`, `development_licences`, `offshore_areas`, `alarm_load`, `closed_containment`.
- **Engine file.** The engine-side `encyclopedia_entries.json` keeps its consumer, but its real-name strings ("Nordlaks' Havfarm", Atlantic Sapphire, Andfjord) are purged for D17 in F2-35.

### 6.8 Tutorial hooks and scenarios

- **Coach marks.** No gating outside `forste_sesong` (D19). Each instrument shows non-blocking coach marks (`godot/data/coach_marks.json`) once: 3 cards each, dismissible.
- **Challenge scenarios** (parody companies; real place names are fine):
  - **"Havpiloten":** start with a converted coastal company, a round open and an OF1-class unit 60% built. Win by converting the development licences within 6 years.
  - **"Oppkjøringen":** start with a completed but empty pilot lot and a pre-zoned pad. Win by reaching 80% utilisation and a farming cost ≤ 65 NOK/kg HOG within 3 years.
- **Sandbox.** `barekraft_mester` changes from "victory ends the game" to **"Mål nådd — fortsett"**. Facility milestones become follow-on goals.

### 6.9 Medals

No cash (D16). Each medal is also a Steam achievement and is recorded in the company history. Medals target skill, not defaults.

| Medal | Condition |
|---|---|
| "Stål i havet" | First offshore unit commissioned |
| "Null avlusinger" | A full offshore cycle with zero treatments **and** mortality below the live PO average |
| "Vinterfast" | A winter at an **OpenOcean** plot without a handling-overdue warning |
| "Fabrikken på berget" | First land harvest |
| "Oppkjørt" | Utilisation cap at 100% |
| "Lyset holdt" | A grid outage **longer than 60 min** with zero loss |
| "Svovelfri" | 12 months with H₂S index < 0.3 on a seawater loop |
| "Fra land til hav" | A SuperSmolt transfer from your own module to your own site |
| "Konvertert" | Development licences converted and placed |
| "Rent utslipp" | A year inside the discharge permit with zero findings |
| "Rolig kontrollrom" | A year with alarm load < 0.2 on every offshore unit |

---

## 7. Roadmap items

**Conventions** (roadmap §2):
- Effort is in agent-days: S ≤ 2, M 3–5, L 6–10, XL 11–20.
- One PR per item, carrying RUN_LOG §1 evidence.
- Tests and thresholds are only ever strengthened. Replacements must be stronger and are listed in §3.10.
- New gates start in `qa/expected_failures.json`, and each item deletes its own entries.
- Shots go into `godot/qa/shots.json`.
- The merge captain raises `MIN_CHECKS`. `translations.csv` and the JSON data keep keys sorted.
- **Standing sim gate:** every sim item except F2-04, F2-10 and F2-13 keeps the shipped-scenario goldens (F2-01) unchanged. Those three re-record them with a diff report.
- The sim track runs three lanes with disjoint files: **sim·core**, **sim·sea** and **sim·land**.

### 7.1 sim

| ID | Title | track · size | files | acceptance |
|---|---|---|---|---|
| F2-01 | Golden-trace harness and pre-F2 fixtures | sim·core · S | New `src/StateHashProbe/GoldenTraceRecorder.cs`, `tools/record_golden_traces.sh` (records on any commit in a temp worktree), `src/LakskongenSim.Tests/Integration/GoldenTraceTests.cs`, `src/LakskongenSim.Tests/Fixtures/golden_traces/*.jsonl.gz`, `src/LakskongenSim.Tests/Fixtures/save_v3_facilities.json` and its generator probe | 1. Recording the same commit twice gives byte-identical files. 2. A mutation test that perturbs one cohort weight by 1 ulp on day 200 fails and names scenario, seed, day and field. 3. Traces cover every shipped scenario × seeds {1, 7, 13} × 730 days: a per-day hash of populations (count, biomass), cash, ledger lines, MTB, site lice and stream positions, plus a full snapshot every 30 days. Excluded fields are listed in the file header. 4. The v3 fixture holds 1 RAS facility with stocked tanks, 1 SemiSubmersible offshore farm with 2 stocked pens, 1 post-smolt facility with 2 batches and a hatchery cohort, and it loads on the pre-F2 commit. 5. The full comparison runs in ≤ 10 min in CI |
| F2-02 | Pure move and neutral hooks | sim·core · L | New `Core/LandSiteTickSystem.cs`, `Core/OffshoreTickSystem.cs`, `Core/SimulationEngine.FarmTypeHooks.cs`, `Core/SimulationEngine.Containment.cs`, `Farm/Land/LandHooks.cs`, `Farm/Offshore/OffshoreHooks.cs`. Moved out of `SimulationEngine.cs`: :2273-2306 (facility carbon), :2889-2907 (red credit), :2095 and :3090-3113 (containment opex and lice blocking). Moved out of `InDayTickSystems.cs`: :114-130 and :279-299. Hook call sites only: `SimulationEngine.cs` :1559, :1839, :2543-2581, :2863-2870; `InDayTickSystems.cs` (growth temperature, appetite, feeding, mortality); `SimulationEngine.Commands.cs` :122 and :321; `Health/IntegratedDiseaseSystem.cs:146-150` (one call) | 1. Every shipped scenario × 3 seeds matches the F2-01 goldens recorded on this PR's merge-base, day by day. 2. `ExactSaveHashTests`, `CrossProcessDeterminismTests`, `EngineTwinRunDeterminismTests` and `FacilityProductionTests` pass unchanged. 3. Grep finds no facility or containment tick code left in `SimulationEngine.cs` or `InDayTickSystems.cs`. 4. Each hook in §3.3 has a unit test proving its default equals the pre-move expression on a fixture state. 5. No new `GameState` field |
| F2-03 | Site kinds, plots, unit resolver and migration | sim·core · XL | New `Farm/SitePlot.cs`, `Farm/SitePlotCatalog.cs` (with the `ISitePlotGeometry` seam), `godot/data/site_plots.json`, `Farm/Offshore/OffshoreUnit.cs`, `Core/UnitResolver.cs`, `Core/SimulationEngine.FacilityMigration.cs`, `godot/scripts/autoload/GameBridge.LandSites.cs` and `GameBridge.Offshore.cs` (list verbs), `docs/run/evidence/f2_03_sitekind_audit.md`. Edited: `Core/GameState.cs`, `Farm/SeaSite.cs` (Kind, PlotId, Offshore, AppliedContainmentCredit), `Core/GameStateRelinker.cs`, `Core/SimulationEngine.Facilities.cs` (legacy verbs create the new kinds), `Core/DaySummary.cs:629-732`, `Core/AchievementSystem.cs` (SuperSmoltStocked), `Integration/DashboardDataProvider.cs`, `GameBridge.Farm.cs` (`GetSeaSiteDetails` gets `kind` and `includeOffshore=false`; parse fixes at :1625 and :1765), `godot/scripts/autoload/SaveLoadManager.cs` (next version), `SimProxies.gd`/`SimBridge.gd` (two marked append blocks), `tests/test_bridge_contract.py`; tests per §3.10 including the replacements | 1. The audit doc lists every `SeaSites`/`GetSeaSiteDetails` consumer in sim and bridge with an include, exclude or hook decision, and there is one C# test per category. 2. Shipped-scenario goldens are unchanged. 3. The v3 fixture migrates: the offshore farm becomes an Offshore `SeaSite` with the same Id and 3 zones; post-smolt batches become populations; fish count and biomass are equal to 1e-9; 30 days of continuation are bit-identical across two processes; running the migration twice changes nothing. 4. A synthetic save with 4 RAS facilities and 3 offshore farms migrates onto overflow plots with no lost fish. 5. Every non-hatchery population resolves to one unit with non-empty SiteId and ZoneId; hatchery populations are listed as exempt. 6. UI flow check: fjord pen positions are identical before and after building an offshore unit. 7. The replaced tests assert at least what they replaced (PR checklist). 8. The "Ship"/"Mega" parse defects have tests |
| F2-04 | Facility finance, calibration section, catalogue inputs, research gates | sim·core · M | New `Finance/FacilityAssetRegister.cs`, `Finance/PermitCase.cs`, `Core/ConstructionProject.cs`, `Farm/FarmTypeCatalog.cs`, `Calibration/FacilitiesCalibration.cs` (+ `SourceAttribute`, `GameBalanceAttribute`), `godot/data/calibration/` (include files). Edited: `Calibration/CalibrationRegistry.cs` (one property + include loader), `godot/data/calibration.json` (include list), `Core/SimulationEngine.Tax.cs` (rent ring-fence), `Core/ResearchSystem.cs` + `godot/data/research_nodes.json` (`unlocks`, narrowed effects), `Core/SimRng.cs` (new scopes), `src/LakskongenSim.Tests/Core/DeterminismScanner.cs` (scope), `Calibration/CalibrationGuardTests.cs` | 1. `FacilityDepreciation` = Σ capex/life per day for F2 units only; cash reconciliation over 730 d × 3 seeds is unchanged; non-F2 capex is untouched (ledger test). 2. On a mixed company, land opex and depreciation never reduce the sea rent base (ledger test). 3. Reflection guard: every `facilities` leaf has a `[Range]` and exactly one `[Source]` or `[GameBalance]`; the registry minor version is bumped at merge. 4. Research JSON/C# contract green; the two nodes' effects apply only to their unit kinds; the goldens are re-recorded with a diff report showing that only runs researching these nodes changed. 5. `GetFarmTypeCatalog()` returns 3 families and 7 variants with sourced reference values and their source keys; grep finds no farm-type number literal in `godot/scripts/ui`. 6. The scanner covers both new bridge partials; the allowlist is unchanged |
| F2-05 | Driftsplan framework, event deadlines, notification meter | sim·core · M | New `Core/Driftsplan.cs` (policy interface and phase), `Core/SimulationEngine.Driftsplan.cs`, `Core/NotificationMeter.cs`; additive `DeadlineDay`/`CounterVerb`/`UnitId` on `Events/GameEventBus.cs` event records; `Core/DriftsplanTests.cs` | 1. A test policy booking a weekly job issues exactly the commands a scripted player would, with an identical state hash after 365 d. 2. Policy evaluation never moves a stream position. 3. Reflection test: every F2 event type declares severity, deadline rule and counter verb (or `none`). 4. The meter counts warnings and criticals per unit per month and is excluded from the save hash. 5. Save with a plan active is bit-identical. 6. Shipped goldens are unchanged |
| F2-06 | Offshore metocean and honest forecast | sim·sea · L | New `Environment/OffshoreMetocean.cs`, `Environment/MetoceanForecast.cs`, `Environment/OperabilityWindows.cs`; `Environment/WeatherSystem.cs` (pure `StateDistribution(k)`, after S2-02); `Core/OffshoreTickSystem.cs`; `Farm/Offshore/OffshoreHooks.cs` (`OperationPossible`); `Calibration/OffshoreCalibration.cs`; `godot/data/calibration/facilities_offshore.json`; `GameBridge.Offshore.cs` (`GetSeaStateForecast`); `Environment/OffshoreMetoceanTests.cs` | Measured on the standalone component over 20 seeds × 50 years: 1. off_1 annual mean Hs 0.8–1.1 m and median annual maximum 3.5–4.8 m [O:R17]. 2. off_1 days with Hs > 2.0 m: 8–14% per year and 20–35% in Jan–Mar (fitted to O:R17 inaccessibility shares). 3. The empirical 100-year level per plot is within ±10% of Hs100 (5.0 / 6.5 / 13 m). 4. Every swell event with peak ≥ 3 m appears in the forecast ≥ 4 days before onset. 5. Reliability: realized frequency is within ±0.05 of the forecast probability in every decile, at leads 1, 3, 5, 7 and 10. 6. 1,000 forecast polls leave every stream position unchanged. 7. Exactly 8 uniforms are drawn per day. 8. The fjord weather series for seeds 1–10 is unchanged. 9. Save mid-event is bit-identical; shipped goldens are unchanged |
| F2-07 | Offshore unit: build, operations, logistics, Driftsplan | sim·sea · L | `Farm/Offshore/{OffshoreUnit,OffshoreProfiles,OffshorePolicies,OffshoreConstructionPhases}.cs`; `Core/OffshoreTickSystem.cs`; new `Core/SimulationEngine.Offshore.cs` (build incl. consortium, booking, resupply, crew, inspect, repair, sensors, tow, shakedown); `OffshoreHooks.cs` (feed, appetite, storm-wear skip); `GameBridge.Offshore.cs`; `Farm/OffshoreUnitTests.cs`, `Integration/OffshoreCycleTests.cs` | 1. On a 6 m Hs day feeding continues at appetite 0.7 while silo > 0 and stops at 0. 2. Charter is paid from the booked day including slipped days; the preview's expected cost and P(done by day X) match the engine distribution over 200 seeds within ±5%. 3. Fish handling never runs above its limit; a wellboat alongside pauses above 2.0 m and leaves above 5.0 m (forced series). 4. The tow starts only on `StartTow`; a failed window shelters 5–15 d with charter cost and no structural damage. 5. A 90-day shakedown reveals ≥ 1 defect in ≥ 70% of seeds; defects fixed before stocking never fire. 6. A consortium unit books 35% of every P&L line and capex payment; buy-out at book × 1.2. 7. No fjord storm wear on offshore units; opex/kg (ex feed, ex depreciation) is 1.15–1.45× an open pen in the same PO [O:R37]. 8. A policy-only unit runs 2 years with zero player commands and no silo-empty day at off_1 in ≥ 18/20 seeds. 9. Save and twin mid-tow and mid-booking are bit-identical; shipped goldens unchanged |
| F2-08 | Offshore incidents, lice coupling, handling overdue, regulator | sim·sea · L | New `Farm/Offshore/{TeethingDeck,AlarmLoad,OffshoreIncident}.cs`, `Finance/SiteWithdrawal.cs`; `Core/OffshoreTickSystem.cs`; `Core/SimulationEngine.Offshore.cs` (alarm review, procedure upgrades, defect fixes, inspections); `OffshoreHooks.cs` (external lice, zone contribution, mortality terms; after S2-05); `Farm/OffshoreIncidentTests.cs` | 1. Hs alone never causes an escape; tilt, net-tear and mooring escapes reach `EscapeIncidents`, fines and Sustainability. 2. The tilt hazard follows the §3.3 terms, and the bridge driver list equals them. 3. With procedure ≥ 0.8, crew aboard and no overdue crew, auto-close succeeds with p = 1 − AlarmLoad (±0.03 over 2,000 forced events). 4. The 21/45/75-day escalation fires on forced series; withdrawal removes the plot (120-day appeal) and leaves the licences as unplaced MTB, never deleted. 5. Pre-registered rate at off_1 with the policy "handle when required" and standard vessels: warning in 10–30% of winters, order in ≤ 5%, withdrawal 0% (20 seeds × 10 winters); with offshore-wellboat charter, warning ≤ 10%. 6. Building an offshore unit never improves a neighbouring fjord site's lice pressure or traffic-light colour (probe, 20 seeds). 7. The external multiplier scales external pressure only; two units in one area infect each other. 8. Incident reports have ≥ 3 causal steps that reconcile to event records. 9. Save and twin mid-tilt are identical |
| F2-09 | Development licences, conversion, placement | sim·sea · L | New `Finance/DevelopmentLicenceRound.cs`; `Finance/LicenceHolding.cs` (Kind, BoundUnitId, ExpiresDay, PlacedZoneId; additive after S2-01); `Core/SimulationEngine.Offshore.cs` (round, reports, conversion, appeal, payment, placement); `OffshoreHooks.cs` (`IsResourceRentLiable` by LicenceKind); `Finance/DevelopmentLicenceTests.cs` | 1. The round opens only after the gate (1 completed cycle, 12 escape-free months); ≥ 1 rival application in ≥ 80% of seeds; with `rivalOffshore` on, one rival award takes off_2; with it off, there is none. 2. The pre-submission score breakdown equals the engine score; the rival band is deterministic per seed. 3. Development licences are free, expire at 15 years and cannot be pooled; their harvests pay no rent and converted ones do (ledger). 4. Conversion needs every checklist criterion, including mortality ≤ the live PO average. The fee is 13,804,348 NOK per licence at Jan 2026, indexed by game CPI; growth on development licences is charged at fixed prices; unpaid after 90 d → lapse; refusal → a 180-day appeal decided on the criteria met by then. 5. Converted licences must be placed in a PO before use, and placed licences count in the traffic light. 6. Save and twin mid-round are identical |
| F2-10 | Closed and semi-closed sea pens | sim·sea · M | `Farm/ContainmentTech.cs` (per-unit capex, kWh/kg; OffshoreClosed not purchasable, migrated after its credit is un-applied); `Core/SimulationEngine.Containment.cs` (upgrade, pumps, flush, single skirt model, stored credit, dispensation); `Farm/Pen.cs` (`HasSkirt`, after S2-01b); `Farm/EnvironmentalFlexibility.cs` (delegates to one formula); `GameBridge.Farm.cs:1183-1220` (thin call); `Calibration/ClosedSeaCalibration.cs`, `facilities_closed_sea.json`; `Environment/ContainmentTests.cs` (add), `Farm/ClosedPenPumpTests.cs` | 1. Grep: no containment state mutation in the bridge. 2. Credit → upgrade → zone exit returns MTB exactly to the pre-credit value (stored factor). 3. One skirt model: 0.18 exclusion with one pen skirted, 0.54 with all [O:R53]; `HasLiceSkirt` is derived and the bridge coupling is gone. 4. Closed pens use 3.5–4.2 kWh/kg [O:R50]; an upgrade above grid headroom is refused. 5. A pump failure without backup takes DO below 5 mg/L inside the registry window; with a genset there is no loss. 6. Density > 25 kg/m³ only with an approved dispensation case. 7. Closed post-smolt to 700 g: median mortality ≤ 2.5% over 20 seeds [O:R46, O:R47] **[GB band]**. 8. `po3_red_sone` still wins ≥ 4/6 seeds (re-measured; tune data, not clamps); goldens re-recorded with a diff report |
| F2-11 | Havfarm-class identity and the offshore-area round | sim·sea · L | `Farm/Offshore/OffshoreProfiles.cs` (ShipShaped), new `Farm/Offshore/ShipShapedUnit.cs` (lee bonus, turret mooring, shared systems, shore power), new `Finance/OffshoreAreaRound.cs`; `Core/SimulationEngine.Offshore.cs`; `facilities_offshore.json` (flags); `Finance/OffshoreAreaTests.cs` | 1. Operations alongside on the lee side get +0.5–1.0 m effective limit (forced-heading test). 2. One shared-system fault hits all 6 wells; each redundancy upgrade halves its hazard. 3. Mixed-source stocking can draw the +10 pp defect; single-source stocking never does. 4. The havgående variant can use hav_1 and OF1-class cannot. 5. The area round runs only with its flag and no earlier than in-game June 2027; an award creates 2 plots in the polygon; the commitment lapses after 730 d without a site application; the move ratio and term are tagged **[GB/proposal]**. 6. Area harvests pay no rent unless `offshoreRent` is on. 7. Save and twin mid-round are identical |
| F2-12 | Land water, energy and tanks | sim·land · L | `Farm/RASFacility.cs` (additive); new `Farm/Land/{LandTank,LandLot,LandWaterModel,LandEnergyModel,LandSiteTemplates,LandHooks}.cs`; `Core/LandSiteTickSystem.cs`; new `Core/SimulationEngine.LandSites.cs` (scenario/test build on a plot, stock, grade, Vannstrategi, heat recovery); `Calibration/LandCalibration.cs`, `facilities_land.json`; `GameBridge.LandSites.cs`; `Farm/LandWaterModelTests.cs` | 1. Energy anchors on pad_a over an annual cycle: HFS r 0.65 with heat recovery 6.0–7.5 kWh/kg LW [L:R2]; r 0, intake at setpoint, lift ≤ 2 m: 0.6–1.5 [L:A1, L:A3]; RAS r 0.97: 4–10 [L:R7, L:S3, L:S4]. 2. LOX at the Salmon Evolution-like setup is 0.45–0.65 kg O₂/kg LW [L:R2], and rises < 15% from r 0 to r 0.9 at equal density. 3. CO₂ at 60 kg/m³, 12 °C, r 0.65 and default pumps is 10–15 mg/L, and exceeds 15 above 70–80 kg/m³ or above r 0.8. 4. A profit sweep over r and w finds an interior r (0.3–0.8) in ≥ 2 of 4 seasons on pad_a, and the best probe pins neither r nor w at a bound. 5. Stocking with a projected peak above 85 kg/m³ is refused. 6. Lots change tanks, energy and staff, and nameplate t/yr is enforced. 7. Land fish have 0 lice, sit outside the traffic light and pay no rent. 8. Shipped goldens unchanged |
| F2-13 | Post-smolt bridge on seawater | sim·land · M | `Farm/Land/LandSiteTemplates.cs` (PostSmoltModule, salinity); `Core/SimulationEngine.LandSites.cs` (StockLandTank for post-smolt, TransferTankToSea via WellboatJob "transfer", SellPostSmolt, SetSalinity); `Farm/CohortTransferSystem.cs` (engine-owned with lead time); `LandHooks.cs` (`QualityMortalityMultiplier`, `SmoltTierCostPerFish`; after S2-05); `GameBridge.Farm.cs:1987-2104` and `GameBridge.Depth.cs:1208-1247` (thin calls); `tests/test_bridge_contract.py`; `Farm/PostSmoltBridgeTests.cs` | 1. Grep: no money or fish mutation in the bridge for post-smolt or transfer, and no `new Random`. 2. ≥ 400 g with ≥ 25 ppt over the last 30 d → SuperSmolt; 200–400 g → Large. The mortality (0.67/0.82) and time (0.70/0.88) multipliers are live, and SuperSmolt sea phases are ≥ 20% shorter than Standard (20 seeds). 3. Salinity < 12 ppt for > 60 d doubles transfer mortality; the H₂S sulphate factor = salinity/35. 4. Strain and genome survive the transfer; a transfer takes 1–3 d and checks MTB at booking and execution. 5. `StockSmolts` with a tier and no own source charges ×1.6/×2.5, and with an own source no premium (exploit closed); goldens re-recorded with a diff report. 6. `SellPostSmolt` books revenue and removes the fish. 7. 1–4 modules scale tanks, capacity, energy and staff linearly |
| F2-14 | Land permits, zoning, grid and phased construction | sim·land · L | New `Finance/LandPermitSystem.cs`, `Core/SimulationEngine.FacilityPermits.cs`, `Farm/Land/LandConstructionPhases.cs`; `Core/SimulationEngine.LandSites.cs` (managed/turnkey, pilot lot, expand, accelerate, pre-order, cancel, grid projects); `Finance/LandPermitTests.cs`, `Integration/LandCommissioningTests.cs` | 1. No land build without approved zoning (unless pre-zoned), land, discharge, sea-works and biosecurity cases; each fail code renders a reason. 2. Decisions are deterministic per seed; the shown refusal probability equals the engine's; sensitivity 0.6 has ≥ 3× the refusal rate of 0.1; mitigations lower it by their registry amount; refused cases can be re-applied with a mitigation or appealed (180 d). 3. Sea works never run in Apr–Jun; the fee is NOK 44,300 [L:R12]. 4. Managed overrun over 200 seeds: median +5–10%, p90 ≤ +30% [L:A4 anchored]; turnkey +12% with zero overrun. 5. Phase payments are booked as Capex; cancelling keeps the sunk cost. 6. HFS first tanks at 22 months, complete at 30 from construction start [L:R1]; pilot + expansion = 2.15 bn. 7. A grid project queues 365–1,095 d with a connection fee, and a second feed only counts on completion. 8. The moratorium flag refuses new land applications with `moratorium` |
| F2-15 | Grow-out biology, competence and harvest planning | sim·land · L | New `Farm/Land/{CompetenceModel,LandCohortPlan}.cs`; `LandHooks.cs` (growth, mortality terms); `Core/LandSiteTickSystem.cs`; `Core/SimulationEngine.LandSites.cs` (PlanHarvest, competence levers); `Integration/LandCycleTests.cs` | 1. Competence 0.4: 400 g → 3.5–4.2 kg HOG in 9–11 months (20 seeds) [L:R1, V:17]. Competence 1.0: 400 g → 4.8–5.6 kg LW in 8–10 months at 12–13 °C [L:R1]. 2. FCR 1.02–1.15 for HFS, 1.1–1.3 for RAS. 3. A cohort below 85% of plan for 30 d raises GrowthShortfall naming its driver; a feed-plan change at competence < 0.6 raises FCR by the registry amount for 60 d. 4. The utilisation cap starts at 55% and rises per completed cycle and per lever, each with its cost (tests). 5. Planned harvests run in their week; realized price follows the seasonal curve; fish under 3 kg take the S2-04 size discount [L:A7, L:P1]. 6. Steady-state farming cost is 60–75 NOK/kg HOG ex depreciation at full utilisation [L:R1, L:R9] and 70–80 at 55%, with the energy share reported |
| F2-16 | Land incidents and the RAS hall template | sim·land · L | New `Farm/Land/{LandIncidentModel,DischargeAccount,LandPolicies}.cs`; `Core/LandSiteTickSystem.cs`; `Core/SimulationEngine.LandSites.cs` (genset test, O₂ generator, diffusers, clean, biofilter, photoperiod, purge, nitrate dosing, denitrification, standing orders, decision resolution after S4-08); `Farm/SludgeValorization.cs` (land source); `Farm/Land/LandSiteTemplates.cs` (RasHall); `Farm/LandIncidentTests.cs` | 1. Outage at 40 kg/m³ and 12 °C with no genset or diffusers: < 5 mg/L in 30–50 min [L:F2], and > 50% bio-zone loss when the outage exceeds 60 min; a tested genset gives zero loss [L:F3]. 2. All outage branches are drawn at creation; a decision option changes the loss but no stream position; save with a pending decision is bit-identical. 3. A covered outage raises no critical event. 4. In 200 seeded incidents, H₂S warning (0.3) precedes acute (1.0) by ≥ 5 sim-days in 100% of cases; the chronic band applies its registry effects [V:8]; fixed bed ≥ 3× moving bed; NO₃-N ≥ 20 mg/L cuts the rate to 0.3× [V:9]. 5. An unpurged RAS harvest is downgraded, and a 6–10 d purge clears it [L:S11]. 6. The maturing share is ≥ 3× higher at 15 °C continuous light than at 12 °C short-day [L:S8]. 7. The discharge ledger tracks N (feed × 0.060); a breach brings a fine and an inspection; the unmonitored finding probability is 0.88 [L:L4]. 8. Land sludge is 0.15–0.20 kg DM per kg feed [L:S7] and reaches the ledger. 9. Static test: no incident hazard reads a finance input (UX-FD5). 10. The drill fires once 60–120 d after commissioning with zero loss. 11. The RAS hall template builds with r 0.95–0.99 and a FixedBed default |
| F2-17 | Balance, reachability, scenarios, medals | sim·core · L | `src/LakskongenSim.Tests/Integration/PlayerScriptProbe.cs` (families post-smolt-first, land-first, offshore-first, consortium, policy-only; after Q4-01); new `Integration/FarmTypeStrategyTests.cs`, `Integration/ReachabilityTests.cs`; `Core/ScenarioSystem.cs:300-380` (facilities, plots, pre-zoning, `rivalOffshore`, continue after win; after S2-02); `godot/data/scenarios.json` (Havpiloten, Oppkjøringen, barekraft_mester continue); `Core/AchievementSystem.cs`, `godot/data/achievements.json` (conditions); `Farm/FarmTypeCatalog.cs` (measured bands); `docs/REALISM_CHECKLIST.md`, `docs/BALANCE.md`; RUN_LOG §6 pre-registration | 1. Reachability (sandbox growth family, ≥ 4/6 seeds): a post-smolt module is affordable by year 2, land or offshore construction starts by year 4, and the first harvest from either comes by year 7; the median first-harvest year per family is reported. 2. Pre-registered: D10 at 5 years still passes with the new families; at year 15 no family beats all others by > 25% median equity on ≥ 80% of seeds; offshore is best in ≥ 1 price-regime × traffic-light cell at year 10 and land at year 15; IRR and payback are reported per family; offshore median payback ≤ 8 years from the first capex payment. 3. Survival after S4-03: OF1-class at off_1 median cycle mortality 3–8%, p90 with the biology defect ≥ 12%, 0 delousing in ≥ 60% of seeds; Havfarm-class gen-1 with mixed sources 15–25% and with a single source 2–5% [O:R22, O:R26]; HFS 96–99% at competence ≥ 0.6 and 93–97% at 0.4. 4. The policy-only probe (1 land + 1 offshore unit, 5 years) ends within 10% of the best manual script; median player-visible commands ≤ 3 per unit per in-game month. 5. Notification budget (policy-competent probe): ≤ 2 warnings per unit-month and ≤ 1 critical per unit-year; ≥ 80% of warnings carry a counter verb. 6. Both challenge scenarios start with facilities and the probe wins ≥ 4/6 seeds; `barekraft_mester` continues after the goal. 7. Medal conditions fire on the player path, and the ledger shows no medal cash (D16). 8. Catalogue measured bands (median, p90) equal the probe output within 5%. 9. Every F2 registry key has a REALISM_CHECKLIST row (S4-10 `Validate()`) |

### 7.2 gfx

| ID | Title | track · size | files | acceptance |
|---|---|---|---|---|
| F2-18 | Gray-box read gate | gfx · S | New `godot/qa/scenes/FarmTypeGrayBox.tscn` + `godot/scripts/qa/FarmTypeGrayBox.gd` (primitive tanks and a 110 m ring at the plot positions); `godot/qa/shots.json` (shots `36_graybox_home_{clear,overcast,rain,snow,storm,fog}`, `36_graybox_land_focus`, `36_graybox_of1_focus`); `docs/run/evidence/f2_18_read_gate.md` | 1. Shots at the shipped home view in all 6 weather states record the measured pixel width and height of a tank, the campus and OF1, their depression angles, and the fog transmittance per plot. 2. At the land framing (520 m, −40°) a lid ellipse is ≥ 40 px tall; at the OF1 framing the ring fills 25–45% of frame width. 3. The evidence fixes the read rule per distance band (which LOD shows; where transmittance < 0.4 needs the chip), and confirms or changes the §5 budgets before F2-25/F2-26 start |
| F2-19 | Land pads and offshore slots in the terrain | gfx · M | `src/LakskongenSim/Environment/TerrainGenerator.cs` (pad carving, `LandPadSlots()`, `OffshoreSlots()`, `IntakeReach(plot)`; implements `ISitePlotGeometry`); `godot/scripts/environment/TerrainGeneratorBridge.cs`; `FjordTerrain.gd` (wrappers, `pad_state`); terrain shader (blasted-granite splat); `meta.json` schema (`land_pads[]`, `offshore_slots[]`); G2-02 vegetation mask; `TerrainGeneratorTests.cs` (golden re-promotion), `godot/tests/UiFlowTests.gd` (`TERRAIN_GOLDEN_HASH`); `Environment/TerrainPadTests.cs`; `docs/run/evidence/f2_19_pads.md` | 1. Seeds 1–10: pad height is 3.6 ± 0.3 m over ≥ 95% of each pad mask. 2. The headless hash equals the windowed hash; the new terrain golden and water bake are re-promoted with an art and bake check in evidence. 3. Pad and slot coordinates equal `site_plots.json` (procedural) or `meta.json` (baked) within 1 m; intake reach is computed (the authored field is absent). 4. Zero vegetation instances inside pad masks. 5. Every offshore slot lies outside the bake with exposure 1. 6. An unbuilt pad renders as heath: its mean hue in the shot region differs from a built pad by ΔE ≥ 10 |
| F2-20 | Facility presenter | gfx · L | New `godot/scripts/visual/{FarmFacilityVisualManager,LandCampusBuilder,OffshoreUnitVisual}.gd`, `godot/shaders/facility_kit.gdshader` (INSTANCE_CUSTOM build progress, rotor phase, lid swatch); `godot/scenes/GameScene.tscn` (two-line mount); FjordPresenter click route (after G3-02); `UiFlowTests.gd` (`_test_facilities_render`); P0-04 baseline record | 1. In a QA scenario with 1 HFS lot, 1 post-smolt site and 1 OF1-class unit, rendered tank and unit counts equal engine counts. 2. The visible state differs per construction phase; shot `32_land_construction` differs by pHash Δ ≥ 9 between phases 2 and 4. 3. Clicking a tank or unit opens its anchored card and focuses the camera (UI input). 4. Against the recorded 12-site P0-04 baseline, a full 4-lot campus adds ≤ 20 opaque + ≤ 16 shadow draws and one OF1 unit + vessel adds ≤ 8 + 6; LOD2 casts no shadow, and of LOD1 only the tanks do (F2-18). 5. Added p95 ≤ 1.5 ms at FJORD with a campus, 2 units and 12 sites, and absolute p95 ≤ 16.7 ms. 6. Removing a facility leaves the others' positions unchanged. 7. No feed barge or fjord pen grid appears for land or offshore units |
| F2-21 | Camera targets, bookmarks, offshore sea | gfx · M | `godot/scripts/environment/FjordCamera.gd` (`register_focus_target`, both clamps extended by target discs, per-target distance and pitch, bookmark cycle); `godot/project.godot` (bookmark input action); `EnvironmentDirector.gd` (wind/swell split from the target plot's Hs); water shader swell include (after G3-03/G3-04); `OffshoreUnitVisual.gd` (Hs chip); `UiFlowTests.gd` | 1. Focus and camera position reach every slot and pad, and the world clamp still holds elsewhere. 2. The OF1 framing fills 25–45% of frame width; the land framing at −35° to −45° shows the full campus. 3. The horizon row is 10–25% from the top at the FJORD/COAST defaults and the offshore framings. 4. At target Hs 4 m, Σa² = Hs²/8 ± 10% across the wind and swell bands, and swell energy sits at 150–300 m wavelength (spectrum test). 5. Chip Hs = `GetSeaStateForecast(site).today.hs` ± 0.05 m. 6. G cycles Fjord → Land → Hav by UI input |
| F2-22 | Logistics visuals and presentation beats | gfx · M | New `godot/scripts/visual/{OffshoreLogisticsVisualManager,PresentationEventQueue}.gd`; `ChannelBoat.gd` (open-sea leg); `WellboatVisualManager.gd` (quay and offshore destinations; after G2-03 and U2-05) | 1. Each sim job (resupply, crew change, wellboat, ROV, silage, tow) has a vessel visual, and its beat plays for 6–15 s of real time at both 1× and 30× (test). 2. No vessel sails for a job whose Hs limit is exceeded. 3. The service vessel moors within 30 m of its berth, with \|y − wave\| ≤ 0.1 m. 4. The wellboat berths at the land quay for smolt delivery (UI test). 5. The "Se slepet" chip flies the camera, and no beat forces a cut. 6. A phase completion posts a thumbnail toast that flies the camera there on click |
| F2-23 | First sight, teaser, showcase shots, night lighting | gfx · M | New `godot/scripts/visual/{FirstSightDirector,TeaserDirector}.gd`, `godot/data/first_sight.json`; EnvironmentDirector mood hook (Blåtime, low winter sun); night lighting for lids and halls; `godot/qa/shots.json` (`30_land_campus_day`, `31_land_dusk`, `32_land_construction`, `33_offshore_of1_winter_sun`, `35_offshore_storm`, `36_home_overcast_landmarks`); `docs/run/evidence/f2_23_first_sight.md` | 1. The first completion plays one ≤ 20 s sequence that opens with a 3–5 s timelapse; it is skippable, and the sim day and stream positions are unchanged. 2. A second completion of the same variant shows a toast only. 3. Reduced motion gives a still and a 2 s cross-fade. 4. Shot 33: yellow-hull pixel fraction ≥ 1.5% and mean water saturation ≤ 0.35. 5. Shot 36 (home view, overcast): OF1 hull-hue pixels ≥ 200 and campus silhouette ΔL ≥ 15 against the strandflat; in the rain, snow and storm variants the status chip is visible. 6. The teaser leaves the engine state hash unchanged, plays once in `forste_sesong` after day 150 outside any tutorial step, and never offers a build. 7. The art checklist against owner refs L1, L2, O2 and O3 is signed |
| F2-24 | Weather drama and incident poses | gfx · S | `OffshoreUnitVisual.gd`; `godot/scripts/visual/particles/ParticleEffects.gd` (spray emitter); nav-light sync; land vent steam; marker colour lint in `tools/` | 1. Spray appears only above Hs 3 m. 2. Nav-light phases are equal across units at night (1 Hz [O:R58]). 3. A tilt shows a ≤ 3° list until resolved. 4. Vent steam only below −2 °C air. 5. Lint: no world-anchored marker over a unit uses a yellow fill, and every marker is ≥ 20 ΔE from #E6BE2E |

**Landed (F2-21 and F2-23, `wave4b/gfx-farm-2`):** the files as listed, plus `scripts/environment/OpenSea.gd` (the open coast at the target plot), `scripts/visual/{CinematicStage,FirstSightOverlay,CampusLights}.gd`, `shaders/campus_light_glow.gdshader`, the engine's pure split `SimulationEngine.OpenSeaPartsAt` and `SeaStateModel.{SwellAmplitudes,WindForHeight}`, the capture scene `qa/scenes/ShowcaseCapture.tscn` and the checks `godot/tests/{CameraSeaChecks,FirstSightChecks,TeaserChecks}.gd`. Where the build differs from the text: the Havfarm framing is −16° (§4, amended); "no tutorial step is active" (§6.1 item 1) reads as the tutorial not running or its step a wait for the world ("grow", "sale"), since with the tutorial over alone the teaser would miss day 300 on 4 of 6 seeds; the teaser's news card is its own end card, and the Leksikon entry and the locked "Hav" card are F2-35's (`TeaserDirector.finished`); "once" is per timeline the director saw begin by day 150 (no save field); the offshore first sight's low sun is 6° under the clear-day palette; the night lights, the lids' work-light pools and the yard's lamps light at dusk on the presentation clock, compensated for the eye's exposure; a landmark lift keeps the hull and the pale walls legible through haze at the home view (§5.5 rule 1). Decisions and numbers: RUN_LOG §7 (2026-10-21 gfx-farm-2 rows); evidence `docs/run/evidence/f2_21_camera_sea.md`, `f2_23_first_sight.md`.

**Landed (F2-24, `gfx/weather-drama`):** the files as listed, with these differences. The spray is not in `ParticleEffects.gd` (that node is event-driven) but its own presenter, `particles/UnitSpray.gd`, a child of the unit's model; the steam is `particles/RoofSteam.gd`, a child of the campus like its lights; they share `particles/PuffKit.gd`, and the rules (one pure class) are `FacilityDrama.gd`. The spray is one emitter with a point at each of the 12 columns' waterline: none at or under Hs 3 m, 0.15 of its share just over, full from 6 m, its drops thrown faster in a heavier sea. The list is 2.6° (the spec's ≤ 3°), eased at 0.6°/s, while the engine's handling state names a `Tilt` incident (`get_offshore_handling_state`, a read the presenter had not taken, now among its approved reads with its pure-read pins: `tests/test_facility_presenter.py` and the xUnit `OffshoreHandlingViewTests`). The steam is one emitter with a point at each roof unit of every hall that stands whole (2 per process hall bay, 5 on the harvest hall): none at or over −2 °C, 0.30 of its share just under, full from −14 °C. Nav-light sync was already the shader's global `TIME` (pinned now); the marker lint is `tools/lint_markers.py`, F2-21's chip test unchanged. The behaviour's checks are `godot/tests/FacilityDramaChecks.gd`, run by `tests/test_facility_drama.py` where Godot is installed, because the UI flow suite's count is frozen (SHIP_PLAN §3). Frames and numbers: shots `47_*`, `docs/run/evidence/f2_24_weather_drama.md`.

### 7.3 art

| ID | Title | track · size | files | acceptance |
|---|---|---|---|---|
| F2-25 | Land kit | art · L | New `blender/build_land_base.py`; `blender/lk_meshkit.py` (append helpers); `blender/asset_manifest.json`; new `godot/assets/models/land_kit/*`, `godot/assets/models/facility_kit/LK_FacilityKit.tres`; new `tests/test_land_kit_assets.py`; `docs/run/evidence/f2_25_land_kit.md` | 1. Every §5.1 asset is within 5% of spec. 2. LOD0/1/2 triangles ≤ budget, and the build raises over `TRI_LIMITS`. 3. A rebuild reproduces byte-identical files. 4. Each LOD has one `LK_FacilityKit` surface, and the swatch report matches §5.1. 5. Preview at the land framing: lid ellipse ≥ 40 px; dark lids vs pale walls ΔL ≥ 30. 6. Evidence doc with reference side-by-sides (refs not committed) |
| F2-26 | Semi-submersible unit (OF1-class) | art · L | New `blender/build_offshore.py`; `blender/asset_manifest.json`; `godot/assets/models/offshore/of1_class_*` + `.tscn`; new `tests/test_offshore_assets.py`; `docs/run/evidence/f2_26_of1.md` | 1. The owner's polygon ruling is recorded in RUN_LOG §7, with the 12-gon vs 8-gon side-by-side against O2/O3, and the geometry follows it (default 12-gon, circumradius 55 ± 2.75 m). 2. Height 68 ± 3.4 m, waterline at the 43 m draft. 3. Column diameters 3.56 and 2.8 ± 5%. 4. Triangles ≤ 60 k / 12 k / 1.5 k. 5. Fixed node names, including `Beacon`. 6. The part groups for the yard picture-in-picture and the timelapse exist |
| F2-27 | Offshore fleet | art · M | `blender/vessel_designs.py` (`offshore_service`, `crew_boat`, `rov`, `auv`); `blender/build_vessels.py` (`TRI_LIMITS`, material map); `godot/assets/models/{offshore_service,crew_boat,subsea}/`; `tests/test_vessel_assets.py` (list) | 1. Service vessel 75 × 17 m and crew boat 20 × 7 m ± 5%. 2. The anchor tug is a swatch variant with zero new meshes. 3. Triangle budgets met. 4. The service vessel is visibly shorter than OF1's diameter, and the gangway node exists |
| F2-28 | Construction kit | art · M | New `blender/build_construction_kit.py`; manifest; `godot/assets/models/construction_kit/*`; new `tests/test_construction_kit_assets.py` | 1. Tower crane 50 m mast and jib ± 5%. 2. The scaffold ring (Ø29.5 m) fits `tank_growout`. 3. Triangle budgets met. 4. Every land and offshore construction phase in §6.3 has its asset set (manifest cross-check test) |
| F2-29 | Ship-shaped unit (Havfarm-class) and mooring spread | art · M | `blender/build_offshore.py`; manifest; `godot/assets/models/offshore/{havfarm_class,mooring_line_kit}_*`; `tests/test_offshore_assets.py` | 1. Hull 385 × 59.5 m ± 5%, with 6 wells of 47 × 47 m. 2. Triangle budgets met. 3. Mooring line counts are 8 (OF1) and 11 (Havfarm) at 1,100 m and 650 m radius. 4. The evidence doc records the hull-colour check against press photos before the swatch is final |
| F2-30 | Closed-pen kit | art · M | New `blender/build_closed_pens.py`; manifest; `godot/assets/models/closed_pen/*`; new `tests/test_closed_pen_assets.py` | 1. Donut volume 22,000 m³ ± 5%, Globe 3,500 m³ ± 5%, skirt 13 m. 2. Triangle budgets met. 3. Each containment level renders its own asset (UI flow test on a QA site per level) |

### 7.4 ui

| ID | Title | track · size | files | acceptance |
|---|---|---|---|---|
| F2-31 | Family-first build flow | ui · L | New `godot/scenes/ui/FarmFamilyCard.tscn`, `godot/scripts/ui/facilities/{FarmFamilyCard,ComparisonBars,FacilityBuildPreview,CashRunwayChart,PermitTimeline,DiagnosisStrip}.gd` + scenes; `godot/scenes/ui/BuildSiteDialog.tscn` + script (U2-01); `godot/scripts/ui/RASPanel.gd` and `OffshorePanel.gd` (build buttons removed); `tests/test_bridge_contract.py` (legacy verbs only from QA); `godot/translations/translations.csv`; `UiFlowTests.gd` | 1. UI input only: build dialog → Land → "Landanlegg (hybrid)" → pad_a. The preview shows capex, a payment schedule with the p50/p90 overrun band, a 36-month cash runway, the payback year, NOK/kg HOG, capacity cost per t HOG/yr including the licence, the first harvest, and the permit timeline with refusal odds and drivers. Confirm creates permit cases (engine state changed). 2. Three family cards come first and variants second; the aligned bars and pips equal `GetFarmTypeCatalog()`, with median and bad case for survival. 3. Locked cards list every gate, including money, with progress bars. 4. Ghost placement leaves the engine hash unchanged. 5. The diagnosis strip names the binding constraint from engine data. 6. The Hav preview shows the licence-value line and the consortium option. 7. Contrast and overlap lints are clean at 1280×720 and 125%, and a keyboard path runs the whole flow (D15). 8. Contract test: no GDScript outside QA calls `BuildRASFacility`, `BuildOffshoreFarm` or `BuildPostSmoltFacility` |
| F2-32 | Land boards | ui · L | New `godot/scripts/ui/facilities/{LandLotBoard,WaterEnvironmentGlyph,TankRing,VannstrategiPicker,HarvestPlanner,PostSmoltPlanner,OppkjoringCard}.gd` + scenes (U3-03 ChartKit); hub route entry; `godot/scripts/ui/RASPanel.gd` retired, with `_test_ras_panel_stocks_and_harvests` replaced; `UiFlowTests.gd` (`_test_land_board`); shot `38_tank_board` | 1. With 4 lots at 1280×720, the default board shows ≤ 12 status glyphs, each naming its binding constraint, and the worst tank is one click away. 2. Group order equals the presenter's 3D layout order. 3. Glyph values equal bridge values, and the CVD lint passes (shape + colour). 4. Vannstrategi presets show kWh/kg, growth and maturation risk equal to the engine projection (± 3%); grade, purge, harvest plan and genset test run by UI input. 5. The post-smolt planner's tier, salinity, MTB preview and sell-vs-transfer equal the engine result. 6. The replacement RAS flow test stocks, harvests and asserts engine state, covering at least the old checks. 7. EmptyState with no land site; no horizontal scroll at 1280 |
| F2-33 | Offshore window board | ui · L | New `godot/scripts/ui/facilities/{WeatherWindowBoard,HsFanChart,BookingPreview,DriftsplanCard}.gd` + scenes; hub route entry; site card containment strip and sludge action (moved from OffshorePanel); `godot/scripts/ui/OffshorePanel.gd` retired, with `_test_offshore_rows_are_legible` and `_test_offshore_sludge_button_sells_sludge` replaced by stronger checks; `UiFlowTests.gd` (`_test_window_board`); shot `37_window_board` | 1. Cells equal the `GetSeaStateForecast` probabilities. 2. Entering drag mode pauses time; a drop on a green cell books that day; red cells reject with a reason; the keyboard path books too. 3. "Book første grønne vindu" and Driftsplan auto-booking produce the same booking as the engine policy. 4. The booking preview shows expected charter cost including slipped days, P(done by day X) and silo days, equal to the engine values. 5. Days without a handling window (against 21/45/75), MTB headroom, silo days, crew clock and alarm load with its drivers are visible, and no mute toggle exists. 6. The containment ladder and sludge sale work from the site card by UI input. 7. Contrast lint is clean at 1280×720 |
| F2-34 | Alarms, decisions, incident reports, deadlines | ui · L | New `godot/scripts/ui/facilities/{CriticalDecisionCard,IncidentReport,DeadlineToast,DoProjectionChart}.gd` + scenes; U2-06 routing entries; U2-07 auto-slow/auto-pause registration; `UiFlowTests.gd` (`_test_outage_decision`, `_test_deadline_slowdown`) | 1. At 30× with no input, every counterable F2 warning in a forced event script gives ≥ 5 s of 1× time or a pause before its deadline. 2. Toasts show the deadline as a sim date with a countdown bar. 3. A forced uncovered outage pauses and shows options with their DO curve, expected loss and cost; choosing "Åpne nødoksygen" changes the engine loss (end to end); a covered outage shows no modal. 4. Incident reports list ≥ 3 causal steps that reconcile to engine records, with drivers in nested tooltips (U3-04). 5. In a 2-year policy-competent QA run, toasts per unit-month are ≤ 2 |
| F2-35 | Unlock reveal, Leksikon, coach marks, medals, scenario links | ui · M | New `godot/scripts/ui/facilities/UnlockNewsCard.gd` + scene; `godot/data/coach_marks.json`; `godot/data/encyclopedia.json` (category `production`, 19 entries); `godot/data/encyclopedia_entries.json` (D17 purge); `godot/data/achievements.json` (icons; after F2-17); win-screen "Fortsett" hook (U2-03 end screen); `translations.csv`; `UiFlowTests.gd` (`_test_unlock_reveal`) | 1. JSON test: every new entry has `en` and `nb`, valid `related` ids and an existing category; `test_text_correctness.py` (D17) passes on both encyclopedia files. 2. Locked card → news card → Leksikon works by UI input. 3. Coach marks show once per instrument, are dismissible and never block input. 4. The medal popup shows no NOK amount. 5. The unlock card's scenario link starts "Oppkjøringen" or "Havpiloten". 6. Winning `barekraft_mester` offers "Fortsett", and play continues (UI test) |

**Landed (F2-31, `wave4/ui-farm-1`):** the files as listed, plus `GateList.gd`, `PlotLens.gd`, `FarmGhostLayout.gd`, `BuildFlowLink.gd` (`scripts/ui/facilities/`), `scripts/environment/FarmGhost.gd`, the bridge partial `GameBridge.BuildFlow.cs` and the engine reads `Core/SimulationEngine.BuildFlow.cs` (`PreviewFarmBuild`: the lane preview and the money, `CanStartFarmType`) and `.BuildDiagnosis.cs` (`GetBuildDiagnosis`, `GetBuildPlots`). Where the build differs from §6.2: the comparison bars are the catalogue's seven axes (F2-04: capacity cost with the licence, farming cost, survival, external lice, energy, time to a working unit, capacity), not §6.2's nine (months to first harvest is the preview's; lice treatments, weather and technical-failure exposure and the licence route have no catalogue axis yet); the farming cost is the catalogue's one median, not "at 55 % and at full utilisation" with the Oppkjøring ramp (no calibration holds them: F2-15/F2-17); the site lens shows each slot's Jan/Jul mean Hs and Hs100 from the sea model, not "weeks without a handling window per winter" (that needs F2-06 runs per plot: F2-33); the teaser and the "Prøv Oppkjøringen" link are F2-35's (the smaller first phase is the pilot lot / full lot choice); the 3D result beat at confirm (blast flags, survey buoys) is F2-22's. Numbers: docs/BALANCE.md §1x; decisions: RUN_LOG §7 (2026-10-20 F2-31 rows).

### 7.5 Stages (parallel worktrees; items serial within a track; later items may start early once their dependencies merge)

| Stage | Window (2027) | sim·core | sim·sea | sim·land | gfx | art | ui |
|---|---|---|---|---|---|---|---|
| 1 | Jan 18 – Mar 12 (Phase 2 tail + Phase 3) | F2-01 → F2-02 (after S2-01a/b, S2-04, S2-05) → F2-03 → F2-04 | — | — | F2-18 (gfx-3 merged) → F2-19 (after G2-01, G2-02, F2-03) | F2-25 (after F2-18) → F2-26 (after the owner's polygon ruling) | F2-31 (after U3-01, U2-01, F2-04) |
| 2 | Mar 15 – May 7 (Phase 4) | F2-05 (after U2-06) | F2-06 (after S2-02) → F2-07 → F2-08 → F2-09 (after S2-01) | F2-12 (after S2-02) → F2-13 → F2-14 → F2-15 (may start) | F2-20 (after G3-02, F2-25, F2-26) → F2-21 (after G3-03, G3-04, F2-06) → F2-22 (after G2-03, U2-05, F2-07) | F2-27 → F2-28 | F2-31 (finish) → F2-33 (after U3-03, F2-08) |
| 3 | May 10 – Jul 30 (Phase 5 in parallel with ship work; never blocks Next Fest) | F2-17 (after both lanes, Q4-01, S4-03) | F2-10 (after S2-01b) → F2-11 (after S4-01) | F2-15 → F2-16 (decision part after S4-08) | F2-23 → F2-24 | F2-29 → F2-30 | F2-32 (after F2-15) → F2-34 (after S4-08, F2-16) → F2-35 (after F2-17, U3-02) |

**What ships when**
- **Core loop** (stages 1–2): post-smolt module, HFS pilot and full lots, and OF1-class with development licences. This is playable in internal builds by the Phase 4 exit.
- **Stage 3:** Havfarm-class, the offshore-area round, RAS hall, closed-pen art, incidents with decisions, and medals. Everything is cuttable to after 1.0 without breaking the core loop.
- **Next Fest demo:** only the presentation teaser (D9).

**Effort by track**

| Track | Items | Agent-days |
|---|---|---|
| sim·core | F2-01 S2, F2-02 L8, F2-03 XL14, F2-04 M5, F2-05 M5, F2-17 L8 | ≈ 42 |
| sim·sea | F2-06 L6, F2-07 L10, F2-08 L7, F2-09 L7, F2-10 M5, F2-11 L8 | ≈ 43 |
| sim·land | F2-12 L9, F2-13 M5, F2-14 L9, F2-15 L7, F2-16 L10 | ≈ 40 |
| gfx | F2-18 S2, F2-19 M5, F2-20 L10, F2-21 M5, F2-22 M5, F2-23 M5, F2-24 S2 | ≈ 34 |
| art | F2-25 L10, F2-26 L9, F2-27 M4, F2-28 M4, F2-29 M5, F2-30 M4 | ≈ 36 |
| ui | F2-31 L9, F2-32 L9, F2-33 L8, F2-34 L9, F2-35 M5 | ≈ 40 |
| **Total** | 35 items | **≈ 235** (revision 1 said 170; that was ~38% low) |

**Critical path**
- The **UI chain**: U3-01 → U3-03 → F2-33 → F2-32 → F2-34 (behind S4-08) → F2-35 (behind F2-17), landing around end of July 2027.
- The **sim·core chain**: F2-01 → F2-04 is ≈ 6 weeks inside stage 1, then F2-17 waits for Q4-01 and S4-03.
- sim·land is not on the critical path.

### 7.6 Additions to the §10 conflict map

| Hot file | F2 items | Rule |
|---|---|---|
| `Core/SimulationEngine.cs` (call sites :1559, :1839, :2095, :2273-2306, :2543-2581, :2863-2870, :2889-2907, :3090-3113), `Core/InDayTickSystems.cs` | F2-02 only | F2-02 owns these lines during its window: pure moves and hook calls. No other F2 item edits these files afterwards |
| `Core/SimulationEngine.Commands.cs` (:122, :321) | F2-02 (two hook calls) | Before S4-05 starts on `Commands.cs:97-170`. F2-13 fills its hooks in `LandHooks.cs` and never edits `Commands.cs` |
| `Health/IntegratedDiseaseSystem.cs:146-150` | F2-02 (one hook call) | Phase 3, before S4-02 takes ownership of Health. S4-02 keeps the call |
| `Core/SimulationEngine.FarmTypeHooks.cs` | F2-02 | Frozen afterwards. Lanes edit only `Farm/Land/LandHooks.cs` / `Farm/Offshore/OffshoreHooks.cs` |
| `Farm/SeaSite.cs`, `Core/GameState.cs`, `Core/GameStateRelinker.cs` | F2-03 | Additive, after S2-01b. Lanes add no fields here; F2-03 adds every field the lanes need |
| `Farm/Pen.cs` | F2-10 (`HasSkirt`) | After S2-01b; additive |
| `godot/scripts/autoload/GameBridge.Farm.cs` | F2-03 (:838-893, :1625, :1765), F2-13 (:1987-2104), F2-10 (:1183-1220) | Serial by stage (1 → 2 → 3); disjoint line ranges; after G2-04 |
| `GameBridge.Depth.cs:1208-1247` | F2-13 | Thin call only |
| `godot/scripts/autoload/SaveLoadManager.cs` | F2-03 | Also edited by U2-08. The save bump is "next version at merge" (R3 bumps every phase) |
| `Calibration/CalibrationRegistry.cs` / `godot/data/calibration.json` | F2-04 (one property, one include list) | Lanes edit only their own `godot/data/calibration/facilities_*.json` and C# record files |
| `GameBridge.LandSites.cs` / `GameBridge.Offshore.cs`; `SimProxies.gd`, `SimBridge.gd` | sim·land / sim·sea | One partial per lane. Each lane appends only inside its F2-03 marker block |
| `Environment/WeatherSystem.cs` | F2-06 (pure method) | After S2-02 |
| `Finance/LicenceHolding.cs` | F2-09 | After S2-01; additive |
| `src/LakskongenSim.Tests/Integration/PlayerScriptProbe.cs` | F2-17 | Shared with S2-01 and Q4-01. F2-17 runs after Q4-01 merges |
| `TerrainGenerator.cs`, `FjordTerrain.gd`, terrain shader, terrain golden hashes | F2-19 | Graphics track, after G2-02. Includes the golden re-promotion |
| `FjordCamera.gd`, `EnvironmentDirector.gd`, water shader includes | F2-21, F2-23 | After G3-03/G3-04; additive API; one camera owner per phase |
| `godot/scripts/environment/FishPenManager.gd`, `FeedBargeVisualManager.gd` | **None** | Untouched: `GetSeaSiteDetails` excludes offshore by default |
| `GameScene.tscn` | F2-20 (two lines) | Serialized with the U track |
| `WellboatVisualManager.gd`, `ChannelBoat.gd` | F2-22 | After G2-03 and U2-05 |
| `blender/asset_manifest.json` + generated manifest | F2-25 to F2-30 | Art track, serial |
| `RASPanel.gd`, `OffshorePanel.gd` | F2-31 (build buttons), F2-32 / F2-33 (retire) | After U3-01. U3-03 drops the 07d rebuild |
| `research_nodes.json` + `ResearchSystem.cs` | F2-04 only | Contract-paired |
| `scenarios.json`, `achievements.json` | F2-17, then F2-35 (icons) | Serial within stage 3 |
| `translations.csv`, `encyclopedia.json`, `encyclopedia_entries.json` | ui F2 items | Keys sorted |
| `UiFlowTests.gd`, `godot/qa/shots.json` | All ui and gfx F2 items | Append only; the merge captain raises `MIN_CHECKS` |

**Roadmap amendments** (logged in RUN_LOG §7 when they land):
- G5-02 is superseded by F2-25 to F2-30.
- S4-11 keeps only the orphaned-panel half; F2-13 takes post-smolt.
- U3-03 skips the 07d offshore panel; F2-33 replaces it.
- S4-06's weather gate calls `OperationPossible`.
- Q4-01's families include those of F2-17.
- S4-10's checklist rows cover every F2 key through the attributes.
- U2-07's auto-slow/auto-pause API is consumed by F2-34.
- The owner's OF1 polygon ruling is recorded before F2-26.

---

## 8. Risks and what is deliberately left out

### Risks

| # | Risk | Mitigation |
|---|---|---|
| FR1 | Scope: ≈ 235 agent-days on top of the roadmap | Farm types are not in the demo; only the teaser is (D9 demo is `forste_sesong` + `po3_red_sone`). Stage 3 is cuttable and never blocks Next Fest. The core loop (post-smolt, HFS, OF1-class) is complete at the Phase 4 exit |
| FR2 | One type dominates, or one is useless | F2-17 pre-registers 5-, 10- and 15-year horizons, IRR and payback, "best in ≥ 1 cell" per family, and the D10 criterion. Tuning goes through registry and scenario data, never clamps (R2). Land wins through price-timing and red-light cells, never by understating its costs |
| FR3 | Nobody reaches the content | The reachability contract (F2-17 #1), entry rungs (post-smolt module, pilot lot, consortium), sandbox "Mål nådd — fortsett", challenge scenarios that start with facilities, and locked cards that show every gate including money |
| FR4 | Offshore as a `SeaSite` silently changes fjord systems | Golden traces, the site-kind audit with tests per category, the `includeOffshore=false` bridge default, and the zone-contribution hook (no dilution exploit) |
| FR5 | Save migration | A fixture generated on the pre-F2 commit, idempotent migration, overflow plots, and a cross-process continuation test. Pre-1.0 saves stay officially unsupported (R3) |
| FR6 | Too many **[GB]** values | Every value is a registry key with a `[GameBalance]` reason and bounds. Acceptance bands are anchored to sourced figures: AOF access shares and Hs, energy and oxygen anchors, achieved weights, survival, the opex ratio and overrun |
| FR7 | Failures feel unfair at speed | The deadline rule with auto-slow/auto-pause (F2-34 #1), procedures and standing orders for minute-scale events, ≥ 7-day H₂S drift, drills, and the notification budget |
| FR8 | Performance with ~70 tanks, a 60 k-triangle unit, vessels and spray | One material per kit, one MultiMesh per tank type per LOD band, no shadows at LOD1+, `INSTANCE_CUSTOM`, separate opaque and shadow budgets against a recorded baseline |
| FR9 | Assets don't read at the default camera, or in weather | The F2-18 gray-box gate before any art budget is spent; beacons, the fog chip, bookmarks and −40° land framings; the home-view overcast shot |
| FR10 | Upstream items slip (S2-01, S2-02, S2-04, S2-05, U3-01, U3-03, G3-02, G3-03, S4-03, S4-08, Q4-01) | Each item names its dependencies. The decision part of F2-16 falls back to standing orders until S4-08 lands. Survival bands sit in F2-17, after S4-03 |
| FR11 | Real-world names | Code names only. Rivals are parodies (D17). The engine encyclopedia file is purged in F2-35. Real places and areas (Frohavet, Trænabanken) are kept as geography |
| FR12 | Regulation is still moving: the offshore-area rules and the MTB/lice-quota review after Meld. St. 24 (2024–25) | Proposals are tagged **[GB/proposal]** and sit behind regulatory flags (`offshoreAreaRound`, `oneCompanyPerArea`, `offshoreRent`, land `moratorium`). Nothing proposed is hard-coded as fact |

### Deliberately left out

- **Rock-pool flow-through (Andfjord/Gigante) as its own archetype.** Its physics is covered by HFS at r = 0 on a low-lift plot. One land look keeps the art budget on the owner's references.
- **Overseas near-market RAS** (Japan, China, USA). The game has no overseas regions.
- **Smart Fish Farm / OF2-scale structures and buildable AOF-class submersible pens.** `OffshoreType.Submersible` stays loadable only, with a 6.7 m profile [O:R19]. There is no player depth slider: the ballast lesson is taught through procedure, inspection and alarm load.
- **Buying dedicated wellboats (NOK 800 M [O:R26]) and owning a fleet.** Logistics are chartered jobs, including the offshore-wellboat class.
- **Market supply from rival land and offshore units.** Rival units occupy plots, render and generate news, but their output does not enter the S3-01 supply index; S4-01 can add it.
- **Grid capacity markets and power-purchase agreements.** There is one regional power price table (S2-02) plus grid-connection projects.
- **A lice-quota regime replacing MTB.** Only the regulatory-flag hook exists; authoring the regime belongs to S4-07 or later.
- **Depreciation of non-F2 capex** (sea sites, processing plant). It would change fjord-only tax and cash. It goes to S4-09/Q4-02, and the dead `InvestmentSystem.DepreciateEquipment` is flagged there.
- **Truck traffic, helicopter visuals, and on-site processing beyond harvest capacity.** VAP stays in the processing system.
- **The sea-site sludge-hub fix** (40 M that buys nothing). It is not a farm-type issue and goes to Q4-02.
- **Species other than salmon.** Species modding is frozen.

---

## Critique log

**Where every critical and high issue was fixed**

| Issue | Fix |
|---|---|
| Content unreachable (GD, critical) | §6.1 gates and rungs; F2-17 #1; consortium; pilot lot; sandbox continue |
| Lead times vs the real clock (GD, critical) | §3.5 deadline rule; F2-34 #1; procedures for short events; H₂S ≥ 7 days |
| Unprovable pure move (Eng, critical) | F2-01 golden traces; standing sim gate |
| Site-kind blast radius (Eng, critical) | §3.1 decision 4; F2-03 audit; `includeOffshore=false` |
| Unlock timeline vs 5-year sweep (Eng, critical) | Compressed ladder; 10- and 15-year horizons in F2-17 |
| Post-smolt on fresh water (Realism) | Seawater/brackish salinity on pad_delta; sulphate factor salinity/35 |
| Stale auction band (Realism) | 180–310 k with 2024 ≈ 305 k; fixed prices kept separate; normalised card; licence-value line |
| Rent keyed on location (Realism) | Keyed on LicenceKind |
| AOF counting the wrong thing (Realism) | Handling-overdue days and withdrawal with licences kept |
| Wellboat limits (Realism) | Handling 2.0 m (2.5 m offshore wellboat), alongside 5.0 m, resupply 2.5 m, appetite falls with Hs |
| Chores (GD) | Driftsplan everywhere |
| Solved water levers (GD) | Pump capacity and an energy model built from parts make reuse buy density; interior-optimum test |
| Alarm-mute trap (GD) | Removed; emergent alarm load |
| Unreachable dread (GD) | Requirement-based overdue counter with pre-registered rates |
| Balance horizon (GD) | 10/15 years, IRR, payback |
| Camera read (GD) | Foreshortened table, gray-box gate, landmarks, chip, bookmarks |
| Sim-time beats (GD) | Presentation queue and timelapse |
| Seven-card overload (GD) | Three families |
| Tank board overload (GD) | Composite glyphs, ≤ 12 |
| Window board vs clock (GD) | Drag pauses, auto-book, charter on slipped days |
| Outage modal on every outage (GD) | Only when uncovered; projections and EV |
| Resolver premise (Eng) | Hatchery exempt |
| Legacy compatibility (Eng) | Legacy DTOs; listed replacements; DaySummary, achievement and dashboard ports |
| Panels are not orphans (Eng) | Staged retirement with stronger tests |
| Wrong Leksikon file (Eng) | `encyclopedia.json` + D17 purge |
| Duplicate fields (Eng) | Reuse and renames |
| Three skirt models (Eng) | One model, stored credit factor |
| No mortality mechanism (Eng) | `UnitMortalityTerms` hook; bands after S4-03 |
| Traffic-light dilution (Eng) | Zone-contribution hook |
| Schedule (Eng) | Splits, honest calendar, critical path, MVP |
| Conflict map (Eng) | Additions incl. Commands, Health, save version, probe, FishPenManager |
| Draw-call budgets (Eng) | One material per kit, separate shadow budgets, baseline |

**Rejected or modified**

- **"Land payback ≤ 7 years" (GD high, partly rejected).**
  - Real land grow-out is barely profitable at 2026 costs (V:17). Forcing a 7-year payback would understate land costs, which contradicts the realism review.
  - Kept: offshore median payback ≤ 8 years, land "best in ≥ 1 cell" at year 15, and IRR/payback reported for every family.
- **"Auto-shown live inset of an offshore unit when forecast Hs > 3 m" (GD high, optional part rejected).**
  - An unrequested inset breaks "calm by default".
  - Replaced by: bookmarks, the Hs chip, the storm-swell warning with its camera-nudge chip, and the fog chip.
- **"Each type best in ≥ 1 cell at year 10" (Eng/GD) → modified.**
  - Offshore at year 10, land at year 15, for the reason above.
- **"Put all capex in one asset register" (Eng medium) → rejected for now.**
  - It changes tax and cash for fjord-only careers and does not belong to farm types.
  - The register covers F2 units only, the rent base is ring-fenced, and sea-site depreciation goes to S4-09/Q4-02. `InvestmentSystem.DepreciateEquipment` is unused, so nothing double-counts.
- **"Training-wheels first incident" (GD medium) → modified.**
  - It is a real-world **drill** with zero loss, not a scripted fake incident. Drills are honest and keep draw-then-decide intact.
- **Teaser: Eng "outside the demo" vs GD "keep in the demo" → reconciled.**
  - It is presentation-only, with no sim state, and stays in the demo as a wishlist hook.
  - Rival awards that change sim state are gated by `rivalOffshore`, which is off in the demo scenarios.
- **"Fine-grained bid for a rival's plot" (GD medium) → narrowed.**
  - Plots are released only through S4-01 distressed-asset exits, with no new bidding system.
- **"Offshore-area one-company-per-area option and lice-quota regime" (Realism low) → partly rejected.**
  - One company per area is a flag in F2-11.
  - Authoring a lice-quota regime is out of farm-type scope (§8).
- **"Keep 5.0 m only as a survival limit" (Realism high) → applied as "wellboat may stay alongside or transit".**
  - Pumping pauses above the 2.0 m handling limit. This keeps the [O:R37] figure honest without making stocking robust.
- **"Make hav_1 a band 0.05–0.3" (Realism medium) → applied as a band drawn per game.**
  - The value is revealed only once a unit operates there. This turns the DNV uncertainty into information play rather than a fixed number.
- **Every other medium and low item was accepted as written.**