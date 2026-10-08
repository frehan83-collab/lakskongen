# Telemetry CSV Export

`godot/scripts/ui/DataExportPanel.gd` exposes two extra CSV actions alongside the
four existing dataset cards:

- **Export balance telemetry (CSV)** — one row per simulated day for the last
  `365` days (`TELEMETRY_DEFAULT_DAYS`).
- **Export depth snapshot (CSV)** — a single-row composite of the "depth"
  systems (pen price, supply pressure, insurance, resource rent, gill risk).

Both write to the folder shown in the panel's **Eksportmappe** field. If that
field is empty, the export falls back to `user://exports/`. On success the panel
logs the path, raises a success toast, and emits
`export_completed(path, rows)`. Missing metrics are written as empty cells; file
or directory failures are logged and reported via a critical toast.

## Balance telemetry

Filename: `balansetelemetri_dag<DDDD>.csv` (`<DDDD>` = current simulated day,
zero-padded).

Header:

```csv
day,balance_nok,total_biomass_kg,market_price_nok,fcr,lice_per_fish,temperature_c,mortality_rate,sustainability_score
```

| Column                 | Source metric (`SimBridge.get_analytics_data`) | Notes                              |
| ---------------------- | ---------------------------------------------- | ---------------------------------- |
| `day`                  | derived from `get_current_day()`               | simulation day number, 1-based     |
| `balance_nok`          | `balance_nok`                                  | cash balance in NOK                |
| `total_biomass_kg`     | `total_biomass_kg`                             | standing biomass                   |
| `market_price_nok`     | `market_price_nok`                             | spot/realised price per kg         |
| `fcr`                  | `fcr`                                          | feed conversion ratio              |
| `lice_per_fish`        | `lice_per_fish`                                | sea lice per fish                  |
| `temperature_c`        | `temperature_c`                                | water temperature                  |
| `mortality_rate`       | `mortality_rate`                               | daily mortality fraction           |
| `sustainability_score` | `sustainability_score`                         | 0–100 sustainability score         |

Rows cover `min(longest_series, 365)` days so every metric column is aligned to
the same day window. Numeric values are written with 4 decimal places; a missing
or non-finite value is left blank.

Example:

```csv
day,balance_nok,total_biomass_kg,market_price_nok,fcr,lice_per_fish,temperature_c,mortality_rate,sustainability_score
136,10250000.0000,482000.0000,74.2500,1.1200,0.3100,8.4000,0.0009,82.0000
137,10265000.0000,485100.0000,74.8000,,0.2900,8.3000,0.0011,83.0000
```

## Depth snapshot

Filename: `dybde_oyeblikksbilde_dag<DDDD>.csv`.

Header:

```csv
pen_price,supply_pressure,insurance_total_premium,resource_rent_paid,worst_gill_risk
```

| Column                    | Source                                                    | Notes                                             |
| ------------------------- | --------------------------------------------------------- | ------------------------------------------------- |
| `pen_price`               | `get_pen_price()`                                         | monthly pen price, NOK/kg (blank if ≤ 0)          |
| `supply_pressure`         | `get_player_supply_pressure()`                            | 0–1 own-supply pressure (blank if unavailable/<0) |
| `insurance_total_premium` | `get_insurance_status()["total_premium"]`                 | annual premium, NOK                               |
| `resource_rent_paid`      | `get_resource_rent()["paid"]`                             | accumulated resource rent, NOK                    |
| `worst_gill_risk`         | max `get_gill_risk(site_id)["risk"]` over `get_all_pens()`| worst site risk, 0–1                              |

Every column is guarded independently: if the underlying `SimBridge` getter is
unavailable the cell is blank rather than failing the export.
