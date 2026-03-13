# Real Norwegian Salmon Spot Prices — Market Data for O-U Calibration

Source: Akvafakta, SSB (Statistics Norway), Fish Farming Expert

## Recent Weekly Spot Prices (NOK/kg, 3-6kg class)

| Period | Price (NOK/kg) | Source |
|--------|---------------|--------|
| 2026 Week 7 | 90.40 | Akvafakta |
| 2026 Week 6 | 91.00 | Akvafakta |
| 2026 YTD avg | 84.86 | Akvafakta |
| 2026 Mar 2-8 (export) | ~93.60 | SSB |
| 2025 Week 52 | 98.93 | Akvafakta |
| 2025 Week 47 | 82.14 | Sitagri/SISALMONI |
| 2025 Week 46 | 76.62 | Akvafakta |
| 2025 YTD avg (same period as 2026) | 101.04 | Akvafakta |
| Frozen salmon (2026 Mar) | 70.92 | SSB |

## Key Statistics
- **Current range (2025-2026)**: ~75-100 NOK/kg
- **Historical range (2020-2026)**: ~40-120 NOK/kg
- **Seasonal pattern**: Prices tend to rise in spring (Easter demand), dip in summer (harvest glut), rise again autumn-winter
- **Volatility**: Weekly swings of 5-15 NOK/kg are common

## Ornstein-Uhlenbeck Calibration Parameters (Estimated)

Based on observed data 2020-2025:
- **μ (long-term mean)**: ~70-80 NOK/kg (2020-2025 average)
- **θ (mean-reversion speed)**: ~0.01-0.02/week (annual kappa ~0.5-1.0, moderate reversion over months)
- **σ (volatility)**: ~10-15 NOK/kg/√week (salmon prices are highly volatile, weekly swings 5-15 NOK/kg common)
- **Price floor**: ~40 NOK/kg (historical crisis low)
- **Price ceiling**: ~120 NOK/kg (historical peak)

## Historical Yearly Averages

Based on IndexMundi monthly data (Norwegian salmon export price, NOK/kg):

| Year | Average Price (NOK/kg) | Notes |
|------|------------------------|-------|
| 2020 | 62.68 | Average spot price for 2020 (FishFarmingExpert) |
| 2021 | 60.81 | Monthly average from IndexMundi (Feb-Dec) |
| 2022 | 82.26 | Monthly average |
| 2023 | 95.76 | Monthly average |
| 2024 | 94.35 | Monthly average |
| 2025 | 83.84 | Jan-Jun average (preliminary) |

## Data Sources for Further Research
- **SSB Statbank**: https://www.ssb.no/en/statbank/table/03024 (historical export data)
- **Fish Pool/SISALMONI**: https://fishpool.eu/sisalmoni/ (weekly benchmark, subscription needed)
- **Akvafakta**: Industry statistics (via Fish Farming Expert articles)
- **Barentswatch**: https://www.barentswatch.no (zone data, lice counts, temperatures)

## Export Volume Context
- Weekly export: ~16,000-20,000 tonnes (round weight)
- 2026 vs 2025: +16.5% volume increase
- Feed sales: ~145,000 tonnes/week (2026, +11% YoY)
