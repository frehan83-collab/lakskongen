# Commercial Release — Steam Pipeline, Next Fest & Launch Benchmarks

Research brief for LakskongenSim. Compiled 2026-10-12 for the Steam demo/Next Fest decision and launch planning. All figures come from the linked sources; estimates are labelled; confidence is per section.

## Executive summary

1. Steam Direct is $100/app (recoupable after $1,000 AGR); store and build reviews are free, typically 3–5 business days each — plan 7.
2. The October 2026 Next Fest (Oct 19–26) is closed to us (registration ended Aug 31) — the realistic targets are **February 22 – March 1, 2027** (registration deadline Jan 10, 2027) or **June 2027** (dates TBA).
3. Next Fest is a one-shot-per-game event: a weak demo costs more than a 4-month delay; the decision gate is whether the demo is review-ready by ~Jan 18, 2027.
4. A demo is a separate App ID with its own depots/builds; for Next Fest the store page and demo build must pass review by Jan 25 (press preview) and all items by Feb 8, 2027.
5. Capsule specs (2026): header 920×430, small 462×174, main 1232×706, vertical 748×896, library capsule 600×900, library hero 3840×1240; ≥5 screenshots at 1920×1080 (16:9).
6. Niche pricing band is $19.99–$34.99 (FS25 $29.99, Two Point Museum $29.99, Fishing: Barents Sea $24.99, Manor Lords $39.99); our natural launch band is $19.99–$24.99.
7. Plan for 10–15% wishlist→Week-1-sales conversion (median 0.10–0.15×) and ~16% demo→wishlist conversion; 2025 Next Fest median demos added <500 wishlists.
8. Guardrail for reviews: ~75% positive is the floor where outcomes improve; half of 2025 Steam releases ended with <10 reviews (no score at all) — ship quality over scope.
9. AI rule (rewritten Jan 16, 2026): disclose AI-generated content consumed by players (music/art/voice/localization); coding/“efficiency” tools are exempt. Our content survey must be truthful or we risk hidden-in-Germany/removal.
10. Release with a 10–15% launch discount (Steam's suggested range, max 40%, 7–14 days, set before release); plan Deck Verified targets (controller-complete, 30 fps at 800p, ≥9px UI text) into the demo build if possible.

## 1. Steam pipeline as of 2026 — [high]

- **Partner fee**: $100 USD per app, one-time, non-refundable, recoupable after $1,000 Adjusted Gross Revenue ([Steam Direct Fee](https://partner.steamgames.com/doc/gettingstarted/appfee); [explainer](https://www.steampageanalyzer.com/blog/steam-direct-fee-explained)).
- **Store setup & review**: complete the store + build checklists, then "Mark as ready for review" (store page first). Each review takes 3–5 business days; submit ≥7 business days before anything date-critical. Review once, update freely ([Review Process](https://partner.steamgames.com/doc/store/review_process); [Release Process](https://partner.steamgames.com/doc/store/releasing)).
- **Demos**: a separate App ID created from the base game; own depots, builds, store presence and checklist. Share depots and Steam Cloud so saves carry into the full game ([Demos](https://partner.steamgames.com/doc/store/application/demos)). A store-page review is required **even if the demo has no separate page** ([Next Fest Oct 2026](https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/2026october)).
- **Depots & automation**: SteamPipe uploads via steamcmd + ContentBuilder (.vdf) ([Uploading to Steam](https://partner.steamgames.com/doc/sdk/uploading)). Options: official [SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD); open-source [game-ci/steam-deploy](https://github.com/game-ci/steam-deploy) GitHub Action ([GameCI docs](https://game.ci/docs/github/deployment/steam/)); community [cm2network/steamcmd](https://hub.docker.com/r/cm2network/steamcmd) Docker image. All need a Steam Guard build account and CI secret hygiene.
- **Demo approval flow**: store page → demo App ID/assets/depots → build upload → mark ready for review → live when approved. Next Fest review deadlines: Jan 25 / Feb 8, 2027 (§3).

## 2. Capsule & screenshot specs (verified against Steamworks docs, 2026-10-12) — [high]

| Asset | Requirement | Size | Notes |
|---|---|---|---|
| Header Capsule | Required | **920×430** | Top of store page; also used as library header if not set |
| Small Capsule | Required | **462×174** | Auto-generates 120×45 and 184×69; logo must stay legible |
| Main Capsule | Required | **1232×706** | Front-page carousel |
| Vertical Capsule | Required | **748×896** | Formerly "hero"; seasonal sales/front page |
| Screenshots | Required | **≥5, 1920×1080 min, 16:9** | Gameplay only; mark ≥4 "suitable for all ages" or they may not show on front-page hover |
| Page Background | Optional | **1438×810** | |
| Bundle Header | If bundling | **707×232** | |
| Library Capsule | Required | **600×900** | Auto half-size 300×450 |
| Library Header | Required | **920×430** | |
| Library Hero | Required | **3840×1240** | Safe area 860×380; no text in artwork |
| Library Logo | Required | **1280 wide and/or 720 tall** | Transparent PNG; logotype only |

Sources: [Store Graphical Assets](https://partner.steamgames.com/doc/store/assets/standard), [Library Assets](https://partner.steamgames.com/doc/store/assets/libraryassets). Capsule rules: title/logo only; no quotes or marketing text. Test the small capsule early — review fails when the logo is illegible at 120×45.

## 3. Next Fest: eligibility, calendar, disqualifiers — [high]

- **Eligibility**: unreleased before the fest ends; public store page; playable demo live before it begins; account in good standing; not a prologue/re-release; **one Next Fest per game, ever**. Playtests don't disqualify but aren't recommended during the fest ([Next Fest](https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest)).
- **Calendar**: Oct 2026 already ran (registration closed Aug 31). **February 2027**: Feb 22 – Mar 1; registration deadline **Jan 10, 2027**; demo + store page reviewed by **Jan 25** for the Press Preview (Feb 11); all items by **Feb 8**. Next edition: **June 2027** (dates TBA) ([Feb 2027](https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/feb_2027)).
- **Disqualifiers**: releasing before the fest concludes; prior participation; invalid/missing demo; selecting no categories removes you from the fest ([Steam Page Analyzer](https://www.steampageanalyzer.com/blog/steam-next-fest-october-2026)).
- **Recommendation (2026-10-12)**: target **Feb 2027** only if the demo is feature-complete by mid-December 2026 and assets can be submitted by Jan 18; otherwise target June 2027 and use Feb–May 2027 to polish.

## 4. Pricing benchmark for the niche (US prices via Steam appdetails API, checked 2026-10-12) — [medium–high]

| Title | Genre | Base / current US price |
|---|---|---|
| Farming Simulator 25 | farm sim | $29.99 |
| Two Point Museum | management sim (2025) | $29.99 (33% off → $20.09) |
| Manor Lords | strategy sim (EA) | $39.99 (35% off → $25.99) |
| Fishing: Barents Sea | commercial fishing sim | $24.99 |
| Fishing: North Atlantic EE | fishing sim | $34.99 |
| Ranch Simulator | farm/life sim | $24.99 (80% off → $4.99) |
| Coral Island | farm sim | $29.99 |
| RimWorld | colony sim | $34.99 |
| Stardew Valley | farm sim | $14.99 |
| Aquaculture Land | fish-farming sim (direct competitor) | $9.99 ($3.99 now) |

Context: median price of the top-50 best-selling new games fell to $15.64 by Oct 2025 ([Steam Page Analyzer](https://www.steampageanalyzer.com/blog/median-steam-game-2026)); 2025 success rates (1,000+ reviews): simulation 4.1%, management 3.4%, farming 8.3% ([Zukowski, Jan 2026](https://howtomarketagame.com/2026/01/27/what-the-hell-happened-in-2025/)). **Recommendation**: base **$19.99** via the Steamworks regional matrix, plus a $24.99–29.99 supporter edition (soundtrack/artbook); don't chase Aquaculture Land's $9.99 tier.

## 5. Demo best practices & conversion benchmarks — [medium]

- **Wishlist → Week-1 sales**: median **0.15×** for games launching with >25k wishlists; **0.10×** for games priced >$10; ranges vary 10–20×, not 10–20% ([GameDiscoverCo, 2025-10-17](https://newsletter.gamediscover.co/p/the-state-of-steam-wishlist-conversions); [Immutable benchmarks, 2026-08-23](https://www.immutable.com/insights/steam-benchmarks)).
- **Demo players → wishlist**: 12% (30th pct), **16.33% median**, 20% (70th pct) across surveyed Next Fest demos ([How To Market A Game, 2025-03-26](https://howtomarketagame.com/2025/03/26/benchmarks-how-many-wishlists-can-i-get-from-steam-next-fest/)).
- **Next Fest reality check**: ~2,300–2,900 demos per edition in 2025. Median demo added **<500 wishlists**; top 5% ~7,000, top 1% 30k+ ([GameDiscoverCo, Oct 2025](https://newsletter.gamediscover.co/p/who-won-october-2025s-steam-next)). A shallow demo (7 min median play for low performers vs 65 min for top, per [Steam Page Analyzer](https://www.steampageanalyzer.com/blog/median-steam-game-2026)) is a wasted one-shot.
- **Best practices**: polished vertical slice ending on an open loop; external playtests before the fest; push users to the demo (not a playtest); share depots/cloud saves; keep it updated all week; the NOK/margin loop must be legible within 20 minutes.

## 6. Review-score guardrails — [medium]

- Games crossing **~75% positive** hit a 1.63% commercial "hit rate" vs 0.60% below; improvement above 75% had nearly no additional predictive value in Q4 2025 data ([Turbine Games, 2026-05-27](https://turbine.games/2026/05/27/steam-indie-success-factors/)).
- Expectation setting for a first release: 48.7% of 2025 Steam releases ended the year with **fewer than 10 reviews — i.e., no score label at all**; only ~3% reached 1,000 reviews ([Zukowski/SteamDB, Jan 2026](https://howtomarketagame.com/2026/01/27/what-the-hell-happened-in-2025/)).
- **Realistic target**: 75–85% positive for a competent, deterministic sim with an honest store page. **Common failure modes**: launch-week bugs/save breaks (our save-format changes); mid-GPU performance; illegible UI at 1280×800; store page promising missing systems (Valve rejects those); a demo so complete testers feel done. All are testable in our harness (ExactSaveHashTests, twin runs, screenshot diffs).

## 7. Steam AI-content disclosure — [high]

Current policy (base: [Valve announcement, 2024-01-09](https://store.steampowered.com/news/group/4145017/view/3862463747997849618); narrowed 2026-01-16, [PC Gamer](https://www.pcgamer.com/software/ai/steam-updates-ai-disclosure-form-to-specify-that-its-focused-on-ai-generated-content-that-is-consumed-by-players-not-efficiency-tools-used-behind-the-scenes/), [RPS 2026-01-19](https://www.rockpapershotgun.com/valve-rewrite-steams-genai-disclosure-rules-to-more-explicitly-allow-ai-powered-efficiency-tools), [Content Survey doc](https://partner.steamgames.com/doc/gettingstarted/contentsurvey)):

- **Must declare — Pre-generated**: any AI-assisted content that ships and is consumed by players: art, sound, music, narrative, localization — **including the store page, marketing materials and Steam Community assets**. Disclose tool(s) and content category in the Content Survey; the disclosure appears publicly on the store page.
- **Must declare — Live-generated**: AI content generated during gameplay; requires describing the guardrails against illegal output. Adult Only Sexual Content that is live-generated is absolutely prohibited.
- **Exempt (since Jan 2026)**: "AI powered tools" for efficiency, e.g. code helpers — "Efficiency gains through the use of these tools is not the focus of this section."
- **Practical implication**: AI-generated music, VO, capsule/UI art must be listed; missing/denied disclosure is riskier than declaring. Players can report illegal live-generated content via a Steam overlay button.

## 8. Age ratings: PEGI, USK, Norway — [medium–high]

- **PEGI / IARC**: IARC self-rating on digital storefronts is free (storefront-funded) ([Game Developer, 2015](https://www.gamedeveloper.com/business/how-paying-for-content-ratings-is-hurting-devs-who-release-in-europe)). Direct PEGI licensing (console/physical) historically cost €260 (casual ≤450MB) up to €2,100 + €1,050/extra platform ([TIGA, 2014](https://tiga.org/news/tiga-urges-pegi-to-review-its-pricing-policy-to-help-indies); legacy rates — verify). PEGI adds "interactive risk categories" (monetisation, communication, engagement) from **June 2026** ([Reed Smith](https://www.reedsmith.com/articles/pegi-launches-interactive-risk-categories-overhauls-age-ratings-for-loot-boxes-in-game-spending-and-communication-features/)). We expect PEGI 3/7: no loot boxes, no real-money mechanics, no chat — verify in the questionnaire.
- **Germany (mandatory)**: since **2024-11-15** Steam hides games without a valid German rating. Truthfully completing Steam's content survey lets Valve issue a self-rating at no cost; direct USK rating is optional ([Steamworks Germany page](https://partner.steamgames.com/doc/gettingstarted/contentsurvey/germany)). USK fees if we go direct (valid from 2025-01-01, ex VAT): €1,500 full procedure (20 working days), €2,200 express, €3,800 ad hoc; statement procedure €400/600/800; event-demo €750/1,100 ([USK cost overview](https://usk.de/en/home/cost-overview/)).
- **Norway**: age limits for games are a **voluntary recommendation**, not law — "I Noreg er aldersgrensene på spel ei frivillig ordning og ikkje lovpålagde"; PEGI is the standard used, and Medietilsynet represents Norway in the PEGI council ([Medietilsynet](https://www.medietilsynet.no/digitale-medier/dataspill/dataspill-a--a/)). The 2015 audiovisual law deliberately excluded games ([Stortinget](https://www.stortinget.no/no/Saker-og-publikasjoner/Publikasjoner/Innstillinger/Stortinget/2014-2015/inns-201415-065/1/1/1)). No Norwegian fee applies; follow the PEGI label anyway for the home market.

## 9. Steam Deck Verified checklist — [high]

[Steamworks compatibility doc](https://partner.steamgames.com/doc/steamhardware/compat) (checked 2026-10-12). Verified requires **all** of:

- **Input**: full play with the Deck/Steam Controller default configuration, no settings fiddling; on-screen glyphs match the active input; text input via Steamworks keyboard API or controller-usable in-game entry.
- **Performance**: default configuration hits **30 fps at 800p on Deck** and **30 fps at 1080p on Steam Machine**.
- **Seamlessness**: no device-compatibility warnings; launchers must be fully controller-navigable (avoid launchers).
- **Display (Deck)**: support 1280×800 (preferred) or 1280×720; smallest text ≥9 px at 1280×800 (aim 12 px).
- **Proton**: blocking Proton bugs → Unsupported (Windows-only builds are fine if Proton-clean).
- **Process**: a completed standard build review is required first; request via the partner site; results typically within a week; ~1 week before auto-publish; **demos are generally not eligible** (exceptional cases only). Deck Verified guarantees Machine Verified.
- **Implication**: wire gamepad navigation and scalable text into the demo now — Deck-style verification is free marketing as Steam Machine ships.

## 10. Localization ROI — [medium]

Steam account-language data (FY2024, GDC 2025): Simplified Chinese 33.7%, English 33.5%, Russian 8.2%, Spanish-ES 4.6%, Portuguese-BR 2.8%, German 2.5%, Korean 2.2%, French 2.1%, Japanese 1.7% ([LingoBright](https://www.lingobright.com/statistics/steam-language-statistics/)).

Valve's guidance: "the less a language looks like English, the higher the expected return." Hooded Horse reported wishlist lifts of ~35–40% (FR/DE), >50% (Spanish) and >90% (East Asian), and one publisher went from <5% to >20% of units in China after localizing; German players are English-tolerant (61% buy English builds), so FR/DE act as "cheap completers" ([Steam Page Analyzer, 2026-06-10](https://www.steampageanalyzer.com/blog/steam-page-localization-guide)).

**Recommendation**: launch **EN + NB**; wave 2 = **Simplified Chinese + Japanese + Korean** if budget allows, else **German + French**; wave 3 = Russian/Brazilian Portuguese. Costs: page copy $100–300/language; in-game $0.08–0.20/word (Norwegian €0.10–0.17), LQA €43–53/h, plus one-time font/script engineering ([Steam Page Analyzer](https://www.steampageanalyzer.com/blog/steam-page-localization-guide); [GDO](https://www.gamedevoutsourcing.com/blog/game-localization-cost-guide); [Allcorrect](https://allcorrectgames.com/insights/localization-pricing/)).

## 11. Launch discount conventions — [high]

Steam's docs: "We generally suggest launch discounts around **10% to 15%**… There is a limit of **40%**." Launch discounts are optional, run **7–14 days**, and can only be configured before release. A 30-day discount cooldown then applies (seasonal sales exempt); discounts of 20%+ trigger wishlist notification emails ([Discounting](https://partner.steamgames.com/doc/marketing/discounts)). **Plan**: 10% for 14 days at launch (revenue-preserving), then hold full price until the first seasonal sale; stairstep 20→33→50 over the following 12–18 months.

## Cost & fee table

| Item | Cost | Confidence | Source |
|---|---|---|---|
| Steam Direct fee (per app) | **$100** one-time; recoupable after $1,000 AGR | high | [appfee](https://partner.steamgames.com/doc/gettingstarted/appfee) |
| Demo App ID | No documented extra fee; needs own depots/builds | high | [Demos](https://partner.steamgames.com/doc/store/application/demos) |
| Store page + build review | $0; 3–5 business days each | high | [Review Process](https://partner.steamgames.com/doc/store/review_process) |
| Steam Next Fest | $0 registration; demo required | high | [Next Fest](https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest) |
| IGF 2027 entry (festival marketing) | **$75** ($25 student); submissions Aug 10 – Sep 13, 2026 | high | [IGF](https://igf.com/submission-info/) |
| PEGI via IARC (digital stores) | $0 | medium | [Game Developer](https://www.gamedeveloper.com/business/how-paying-for-content-ratings-is-hurting-devs-who-release-in-europe) |
| PEGI direct licensing (console/physical) | €260 casual → €2,100 + €1,050/extra platform (legacy rates) | medium | [TIGA](https://tiga.org/news/tiga-urges-pegi-to-review-its-pricing-policy-to-help-indies) |
| USK direct (Germany, optional; Valve self-rating free) | €1,500 / €2,200 / €3,800; statement €400–800; excl VAT | high | [usk.de](https://usk.de/en/home/cost-overview/) |
| Store-page localization | ~$100–300 per language (page copy only) | medium | [Steam Page Analyzer](https://www.steampageanalyzer.com/blog/steam-page-localization-guide) |
| In-game localization (agency) | $0.08–0.20/word; Nordic €0.10–0.17/word; LQA €43–53/h | medium | [GDO](https://www.gamedevoutsourcing.com/blog/game-localization-cost-guide), [Allcorrect](https://allcorrectgames.com/insights/localization-pricing/) |
| Marketing | Free channels: Next Fest, curator outreach, IGF. Paid ads/PR are self-serve/quote-only, no standard fee | low | — |

## Release timeline (anchored to 2026-10-12)

| Date | Milestone |
|---|---|
| Oct–Nov 2026 | Decide Feb vs June 2027 Next Fest; freeze demo scope; publish Coming Soon page (allow ≥7 business days for review). |
| Dec 2026 | Demo feature-complete; external playtests; capsule set + ≥5 screenshots + trailer prepared. |
| Jan 10, 2027 | **Feb 2027 Next Fest registration deadline** (11:59 pm PST) — only register if demo is on track. |
| Jan 25, 2027 | Demo build + store page submitted for review (press-preview gate). |
| Feb 8, 2027 | All required items submitted for review (final participation gate). |
| Feb 11, 2027 | Next Fest press preview begins. |
| Feb 22 – Mar 1, 2027 | **Steam Next Fest: February 2027** — demo live before 10 am PST on Feb 22. |
| After Mar 1, 2027 | Release window opens (must release after fest concludes). Configure 10–15% launch discount before release. |
| June 2027 (fallback) | Next Fest June 2027 (dates TBA) if Feb readiness fails — do not burn the one-shot on a weak demo. |

## Sources

Full URLs are inline above. Primary sources by area: **Valve/Steamworks docs** (Direct Fee, Review/Release Process, Demos, Uploading, Assets, Discounting, Content Survey incl. Germany, Deck compatibility, Next Fest). **AI policy**: Valve 2024-01-09; PC Gamer 2026-01-16; RPS 2026-01-19. **Benchmarks**: GameDiscoverCo (2025-10-17; Oct 2025), How To Market A Game (2025-03-26; 2026-01-27), Turbine Games 2026-05-27, Immutable 2026-08-23, Steam Page Analyzer 2026-09-04. **Ratings/localization**: USK (2025 rates), Medietilsynet, Stortinget 2015, Reed Smith 2026, Allcorrect, GDO, IGF 2027. **Prices**: Steam `appdetails`/`storesearch` API, queried 2026-10-12.
