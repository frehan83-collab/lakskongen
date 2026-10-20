# Lakskongen → Top-Tier: Benchmark Research Report
*Generated: 2026-10-12 | Sources: ~25 | Confidence: High on facts, Medium on recommendations*

## Executive Summary

Lakskongen already has the hardest asset in the farming-sim genre — a **deterministic, regulation-faithful simulation** (sea-lice traffic light, MAB/MTB, licence auctions, corporate finance, research/vertical integration). No existing aquaculture game has this depth. The gap between "functional sim" and "top-tier" is **not** more simulation accuracy — it is the three layers that turn a spreadsheet into a game: (1) making the player *feel* the consequences of the regulation, (2) a live 3D fjord as a *readout* of the sim, and (3) a "why did I lose money" narrative layer. The single most differentiated, evidence-backed lever is doubling down on the **traffic-light / sea-lice regulatory chess** — it is uniquely Norwegian, uniquely real, and uniquely unexploited by any competitor.

## 1. Realism — Lakskongen is already calibrated to reality, and that's its moat

The current engine numbers track the real industry closely:

- **Production cost ≈ 64.68 NOK/kg** (2023 average): smolt 6.91, feed 25.89, "other opex" 20.49, slaughter/freight 4.45, wages 3.78 ([Fiskeridirektoratet Key Figures 2024](https://www.fiskeridir.no/english/aquaculture/statistics-for-aquaculture/booklets/))
- **Feed is ~half the production cost** — the single largest input item ([Iversen 2025, Reviews in Aquaculture](https://onlinelibrary.wiley.com/doi/10.1111/raq.70088))
- **Mortality 14.2% in 2025**, down from 16.7% (2023) — but with extreme regional variance; PO3 (Karmøy–Sotra) hit 25.7% in 2023 before recovering to 15.2% ([Aquafeed, Feb 2026](https://www.aquafeed.com/newsroom/farming-news/norwegian-farmed-salmon-mortality-drops-below-15-in-2025); [SalmonBusiness, Aug 2026](https://www.salmonbusiness.com/norway-salmon-mortality-falls-21-in-first-half))
- **Sea lice is the dominant cost & welfare driver**: 5 delousings/gen = +3.3 NOK/kg; 10 = +5.8 NOK/kg ([Tandfonline post-smolt economics](https://www.tandfonline.com/doi/full/10.1080/13657305.2020.1737272))

### The traffic-light system is the signature depth — and it's real
- Norway splits its coast into **13 production areas** (PO1–PO13), each rated green (+6% capacity) / yellow (0) / red (−6%) every **two years**, based on modelled sea-lice-induced mortality of wild salmon smolts ([ASC regulatory review](https://www.asc-aqua.org/wp-content/uploads/2022/03/Regulatory-Processes-for-Setting-Sensitive-Period-and-Sea-lice-Thresholds-in-Major-Salmon-Producer-Jurisdictions.pdf))
- **PO3 (Karmøy to Sotra) got a red light for the third consecutive time** in the 2026–2027 round ([SalmonBusiness, June 2026](https://www.salmonbusiness.com/traffic-light-system-three-regions-qualify-for-production-increase))
- The threshold that matters: whether >30% of wild smolts would die from lice; the industry is actively **suing the state** over red lights (Mowi, Lerøy, Austevoll) — a ready-made source of in-game tension ([SeafoodSource](https://www.seafoodsource.com/news/aquaculture/norwegian-salmon-farmers-file-class-action-suit-against-government-over-traffic-light-system))
- On-farm lice threshold: **0.5 adult female lice/fish** (0.2 in spring weeks 16–21/21–26)

**Lakskongen already implements per-site lice, Mattilsynet orders, the traffic light, and ±6% MTB** (Phase 2). The realism work is *done at the data level*. What's missing is not accuracy — it's *dramatization*.

## 2. Gaming experience — the "story generator" layer is the gap

Top management sims succeed on **freedom + self-defined goals + drama**, not spreadsheet completeness:

- **Farming Simulator** (4M+ copies of FS25): the appeal is "relaxed pacing and freedom to do what you want, when you want" and players "finding their own definition of success" ([GIANTS, Dec 2025](https://giants-software.com/news.php?show=news-134); [Epic Games preview](https://store.epicgames.com/news/farming-simulator-25-preview-gps-rice-animals))
- **RimWorld** is explicitly a "**story generator**" — the sim exists to produce narrative drama the player experiences ([Wikipedia](https://en.wikipedia.org/wiki/RimWorld))
- **Factorio** (a Silicon Valley obsession): the value is *being alerted before things go bad* and *quickly finding what's wrong* ([PC Gamer](https://www.pcgamer.com/games/sim/factorio-has-silicon-valley-tycoons-like-elon-musk-under-its-spell-and-one-usd7-billion-ceo-is-letting-employees-expense-the-game); [Weitz management lessons](https://weitz.blog/p/factorio-management-lessons))
- **Meaningful friction** (not grind): on easy difficulty management games are "decent," on hard they become "great" because struggle forces strategy re-evaluation ([GameDeveloper](https://www.gamedeveloper.com/design/what-developers-need-to-know-about-making-a-management-simulator))

**Translation to Lakskongen:** the sim already generates the events (crises, red lights, competitor moves, price shocks). The player path to *feel* them is thin. The "story generator" equivalent would be a **cause-and-effect narrative**: "PO3 went red because your lice levels were in the top quartile → −6% MTB → you must cull 40 tonnes → here's what that cost you."

## 3. UI/UX — the "why did I lose money" layer is the killer feature

The exit gate ("≥3/5 playtesters say they understand why they made/lost money") is a *UI problem*, not a sim problem. Evidence-backed patterns:

- **Explain every failed action immediately** — silence is never acceptable; "you don't have enough resources" with a reason ([Board Game Arena UX guidelines](https://en.doc.boardgamearena.com/images/5/57/Guidelines_UX_new_compressed.pdf))
- **Causal explanation of every number** — "why did my price drop?" is the canonical question; the game must answer it (Lakskongen Phase 4.5 already added tooltips + AttentionInbox with delta NOK/days-out — this is the right direction)
- **Color-coded time series, not gauge walls** — EA's "165 gauge panels" dashboard was "overwhelming"; the fix was color-coded time series that show what's improving/worsening at a glance ([Grafana/EA, July 2025](https://grafana.com/blog/from-chaos-to-clarity-with-grafana-dashboards-how-video-game-company-ea-monitors-200-metrics))
- **Never encode information with color alone** — pair color with labels/shapes (the game already uses emoji+text priority badges; keep this) ([JHU accessible data viz](https://accessibility.jhu.edu/wp-content/uploads/sites/31/AccessibleDataVisualizationPresentation.pdf))
- **Accessibility floor: 32×32px minimum, 44×44px recommended** clickable targets; scalable UI ([RNIB Best Practice in Accessible Gaming 2024](https://www.rnib.org.uk/documents/2627/RNIB_Best_Practice_in_Accessible_Gaming.pdf))
- **One tooltip at a time**, embed guidance in the flow rather than a pre-game tutorial ([UX Collective game UX](https://uxdesign.cc/5-ux-tips-i-learned-working-in-gamedev-5f68285bf72d))

**Translation:** Lakskongen's Phase 4.5 (AttentionInbox, tooltips, auto-pause, ui_scale) already covers the accessibility floor and the "why" layer. The remaining UI gap is a **causal P&L story** — when the player loses money, a single screen should trace it to the 2–3 dominant causes (feed cost, lice treatments, a red-light cull, a market shock), not just show a negative balance.

## 4. Competitor landscape — an open niche

No one owns "deep regulatory salmon-farming sim." Existing/upcoming titles are either casual or adjacent:

- **Fishing Tycoon Simulator** (Zenith Soft, coming soon) — fleet/logistics/processing; *catch*-focused, not farm-focused ([Steam](https://store.steampowered.com/app/3570170/Fishing_Tycoon_Simulator))
- **BlueAquaEdu** — an EU **educational** aquaculture game ("reach 500 tons"), species + staff + disasters, but shallow economics ([BlueAquaEdu](https://portal.blueaquaedu.eu/game))
- **Ponics Tycoon** — aquaponics (fish + plants), casual ([ponicstycoon.com](https://ponicstycoon.com/))
- **Aquaculture Land** — freshwater fish-farm tycoon (YouTube trailer)
- **Fish Tycoon 2** — aquarium *breeding* casual, mixed reviews (55%) ([Steam](https://store.steampowered.com/app/845250))
- **See Salmon** by Hyper Games — a Norwegian **4-player installation exhibit** at a visitor centre, "compete in managing your own salmon farms" ([Hyper Games](https://www.hypergames.no/games/see-salmon))

**The gap:** every one of these treats salmon farming as *either* a casual idle sim *or* a science exhibit. None combines **regulatory depth (traffic light, lice, licence auctions) + corporate finance + a living 3D fjord + narrative cause-and-effect**. That combination is Lakskongen's defensible position.

## Key Takeaways (actionable, ranked by leverage)

1. **Double down on the traffic-light / sea-lice "regulatory chess."** It's real, uniquely Norwegian, and unused by competitors. Make red-light → forced cull → financial pain a *visible, felt* arc. The class-action lawsuit (farmers vs. state) is a ready-made narrative device.
2. **Build the causal P&L story screen** — "why did I lose money" tracing loss to its 2–3 dominant causes. This is *the* thing that makes the Phase 4 playtest exit gate pass (players understand why).
3. **Make the 3D fjord a live readout** (Phase 3 medium task already scoped: lice → collar tint, biomass → fish count, weather → waves, bloom → tint). This converts "deterministic sim" into "you can *see* your decisions."
4. **Keep the color + emoji/text dual encoding** for CVD safety (already done); push the accessibility floor to 44×44px targets and validate with a color-blindness simulator.
5. **Replace gauge walls with color-coded time series** on the finance/market panels — the EA finding that "165 gauges are overwhelming" applies directly to a P&L-heavy game.

## Sources

1. [Fiskeridirektoratet — Key figures from Norwegian Aquaculture 2024](https://www.fiskeridir.no/english/aquaculture/statistics-for-aquaculture/booklets/) — cost-per-kg breakdown
2. [Iversen et al. 2025, Reviews in Aquaculture](https://onlinelibrary.wiley.com/doi/10.1111/raq.70088) — feed = ~half cost, export share, employment multipliers
3. [Aquafeed — mortality drops below 15% in 2025](https://www.aquafeed.com/newsroom/farming-news/norwegian-farmed-salmon-mortality-drops-below-15-in-2025) — 14.2% vs 16.7%
4. [SalmonBusiness — mortality falls 21% in H1 2026](https://www.salmonbusiness.com/norway-salmon-mortality-falls-21-in-first-half) — PO-level variance
5. [Tandfonline — economics of on-growing post-smolts](https://www.tandfonline.com/doi/full/10.1080/13657305.2020.1737272) — lice delousing cost ladder
6. [ASC — regulatory processes for sea-lice thresholds](https://www.asc-aqua.org/wp-content/uploads/2022/03/Regulatory-Processes-for-Setting-Sensitive-Period-and-Sea-lice-Thresholds-in-Major-Salmon-Producer-Jurisdictions.pdf) — traffic-light mechanism, 13 PAs
7. [SalmonBusiness — three regions qualify for production increase](https://www.salmonbusiness.com/traffic-light-system-three-regions-qualify-for-production-increase) — PO3 red third time
8. [SeafoodSource — farmers file class action over traffic light](https://www.seafoodsource.com/news/aquaculture/norwegian-salmon-farmers-file-class-action-suit-against-government-over-traffic-light-system) — lawsuit tension
9. [GIANTS Software — FS25 sold 4M](https://giants-software.com/news.php?show=news-134) — commercial proof
10. [Epic Games — FS25 preview](https://store.epicgames.com/news/farming-simulator-25-preview-gps-rice-animals) — relaxed pacing/freedom appeal
11. [Wikipedia — RimWorld](https://en.wikipedia.org/wiki/RimWorld) — "story generator"
12. [PC Gamer — Factorio Silicon Valley](https://www.pcgamer.com/games/sim/factorio-has-silicon-valley-tycoons-like-elon-musk-under-its-spell-and-one-usd7-billion-ceo-is-letting-employees-expense-the-game) — alerting before failure
13. [Weitz — management lessons from Factorio](https://weitz.blog/p/factorio-management-lessons) — "find what's wrong quickly"
14. [GameDeveloper — making a management simulator](https://www.gamedeveloper.com/design/what-developers-need-to-know-about-making-a-management-simulator) — meaningful friction
15. [Board Game Arena UX guidelines](https://en.doc.boardgamearena.com/images/5/57/Guidelines_UX_new_compressed.pdf) — explain every failed action
16. [Grafana/EA — 200+ metrics dashboard](https://grafana.com/blog/from-chaos-to-clarity-with-grafana-dashboards-how-video-game-company-ea-monitors-200-metrics) — gauge walls overwhelm
17. [JHU — accessible data visualization](https://accessibility.jhu.edu/wp-content/uploads/sites/31/AccessibleDataVisualizationPresentation.pdf) — color+label dual encoding
18. [RNIB — Best Practice in Accessible Gaming 2024](https://www.rnib.org.uk/documents/2627/RNIB_Best_Practice_in_Accessible_Gaming.pdf) — 32/44px targets
19. [UX Collective — 5 game UX tips](https://uxdesign.cc/5-ux-tips-i-learned-working-in-gamedev-5f68285bf72d) — one tooltip at a time
20. [Fishing Tycoon Simulator — Steam](https://store.steampowered.com/app/3570170/Fishing_Tycoon_Simulator) — competitor
21. [BlueAquaEdu](https://portal.blueaquaedu.eu/game) — competitor (educational)
22. [Ponics Tycoon](https://ponicstycoon.com/) — competitor (aquaponics)
23. [Fish Tycoon 2 — Steam](https://store.steampowered.com/app/845250) — competitor (casual breeding)
24. [Hyper Games — See Salmon](https://www.hypergames.no/games/see-salmon) — Norwegian salmon-farm installation game
25. [Norwegian SciTech News — NTNU salmon simulator](https://norwegianscitechnews.com/2015/07/jump-in-the-sea-and-swim-with-salmon/) — 3D "swim with salmon" precedent

## Methodology

Ran 8 web searches (realism economics, farming-sim design, competitor games, UI/UX/accessibility, "why did I lose money" transparency, traffic-light regulation, data-viz best practices, serious-game precedents) plus Firecrawl search/scrape verification. Synthesized ~25 sources. Sub-questions investigated: (1) what are the real Norwegian salmon-farming economics? (2) what mechanics make top farming/management sims compelling? (3) who are the direct/adjacent competitors? (4) what UI/UX patterns drive comprehension and accessibility? (5) what is Lakskongen's differentiated position?
