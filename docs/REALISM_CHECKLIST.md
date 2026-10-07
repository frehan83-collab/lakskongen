# Realism checklist — quarterly review (Jan / Apr / Jul / Oct)

The economy is calibrated to real Norwegian aquaculture data. That data
moves; stale calibration silently unbalances the game. Each quarter, check
the items below against the named sources and update the balancing config
(`BalancingConfig` in SimulationEngine.cs + market/zone constants). Record
the review date and any changes at the bottom of this file.

## Prices & costs (update triggers)

| Item | Game value | Real reference (Oct 2026) | Source to re-check |
|---|---|---|---|
| Salmon spot NOK/kg | BaseSalmonPricePerKg = 70 | 2024 avg 92.7, 2025 avg 77.2, Q3-26 ~65-73 | Fish Pool / Euronext SISALMONI via FishFarmingExpert, SalMar/Mowi quarterlies |
| Feed NOK/kg feed | FeedPricePerKgFeedNOK = 22.3 (registry) | ~20-24 delivered feed | Fiskeridir Key Figures, Mowi annual |
| Feed NOK/kg *produced* | price × FCR = 22.3 × 1.15 ≈ 25.6 | ~25.7 feed cost/kg produced (2024) | Fiskeridir Key Figures (Tab.38), Mowi annual |
| FCR | BaseFCR 1.15 | bFCR 1.14-1.15, econ 1.2-1.5 | SalMar sustainability reports |
| EBIT/kg band | (tune after first playtests) | Cyclical 8-25 (SalMar 23.4 FY24, 13.6 FY25) | SalMar/Mowi quarterlies |
| Resource rent tax | 25% effective, 70M basic deduction | Same (from 1 Jan 2023); watch Storting changes | Skatteetaten, Prop.1LS |
| MAB per licence | (check licence constants) | 780t standard (945t far north), ~150-200M NOK market value | Fiskeridir |

## Biology (update triggers)

| Item | Game value | Real reference (Oct 2026) | Source to re-check |
|---|---|---|---|
| Smolt size | (check Hatchery defaults) | Trad 100g, avg 155g, 21% post-smolt 250g+ | Fiskeridir smolt stats |
| Harvest size | (check growth targets) | 4.5-5.5 kg live weight | Mowi Handbook, SalMar |
| Sea mortality/cycle | (check mortality model) | Typical 14-16% (2024: 15.4%) | Vet Institute Fish Health Report (annual, ~Feb) |
| Lice action limit | (check traffic-light thresholds) | 0.5 adult females/fish (0.2 migration weeks) | Mattilsynet/BarentsWatch |
| Treatment mortality | (check delousing profile) | Thermal 5.4×, mechanical 6.3× vs medicinal; +0.5-1pp routine | NVI Walde et al. |
| RAS/post-smolt share | (check tech tree) | 1000g smolt standard strategy; 6000t+ RAS deliveries from Q2-2026 | Intrafish, AKVA |

## Regulation & events (update triggers)

- Traffic Light rulings (biennial; last Jun 2026: PO1/12/13 green, PO3 red) — NFD via SalmonBusiness/WeAreAquaculture.
- Policy fork watch: Apr-2025 White Paper (tradable lice quotas + mortality tax) vs TLS status quo — scenario branch material.
- Shock templates to keep fresh: algal blooms, tariff shifts (US 2nd→5th market 2025), PD/ISA outbreak counts (VetInst annual).

## Review log

| Date | Reviewer | Changes |
|---|---|---|
| 2026-10-05 | Spark (AI review) | Checklist created; feed-cost model flagged (12.5 vs ~25 reality) |
| 2026-10-07 | Balance consolidation | Feed rows reconciled to registry (22.3 price × 1.15 FCR ≈ 25.6); FeedSystem defaults sourced from CalibrationRegistry; new drift guard; see docs/BALANCE.md |
| | | |
