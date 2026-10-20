# Ocean & Aquaculture Data Sources for LakskongenSim

Research date: 2026-10-12. Scope: can a **paid commercial game** ship derived snapshots and/or make live calls to each source, at what cost? License notes are engineering guidance, not legal advice.

## Executive summary

1. Everything LakskongenSim needs (SST, chlorophyll, waves, currents, wind, lice, biomass/MAB, harmful algae) is available from free public sources; only Sentinel-Hub-class processing has genuine paid tiers.
2. **Copernicus Marine** is the core: SST/currents (1/12°, daily, 1993→), waves (1/5°, 3-hourly, 1980→), biogeochemistry. Its licence explicitly allows commercial derivative works and redistribution with DOI citation. [high]
3. **MET Norway** `api.met.no` supplies coastal point forecasts (waves, currents, sea temp) and THREDDS/OPeNDAP model archives under CC BY 4.0/NLOD; requires an identifiable User-Agent but no key. [high]
4. **BarentsWatch FishHealth** is the practical site-level lice/disease API. In 2026 it is *not* industry-login-only: self-service client registration + OAuth2 client-credentials; data under NLOD with mandatory credits. [high]
5. **Fiskeridirektoratet (Fiskeridir)** publishes monthly biomass/stocking statistics per production area and county (2005/2017–2026, xlsx/csv/json) under NLOD; the underlying biomass register is exempt from public disclosure. [high]
6. **Algestatus (IMR/HI)** harmful-algae monitoring is web-only (weekly status reports), no API, and the site states "all rights reserved" → not game-safe without written permission. [high]
7. **NASA OceanColor** gives a deep chlorophyll/water-quality archive, openly usable; Earthdata login required for downloads. [high]
8. **EMODnet** (CC-BY 4.0) and **ERA5/C3S** (CC-BY since 2 Jul 2025; 0.25° hourly wind/wave backfill from 1940) cover gaps cheaply. [high]
9. Recommended architecture: build-time snapshot pipeline → derived JSON in `godot/data/`, plus an optional live client behind a config flag with graceful offline fallback (5–9 days, free-first). [medium]
10. Main risks: BarentsWatch "you cannot change the data content" wording vs. derived in-game numbers; Algestatus permission; CDSE/Sentinel Hub quotas; CMEMS funding horizon noted as 2028 in its SLA. [medium]

---

## Copernicus Marine Service (CMEMS) [high]

- **Available:** SST, salinity, currents, sea level, mixed layer, sea ice (GLOBAL_MULTIYEAR_PHY_001_030, 1/12° ≈ 8 km, daily/monthly, 1 Jan 1993 → present); waves (GLOBAL_MULTIYEAR_WAV_001_032 "WAVERYS", 1/5° ≈ 22 km, 3-hourly, 1 Jan 1980 → present); biogeochemistry incl. plankton/chlorophyll and primary production per service parameter list. Regional "Atlantic North West Shelf" products exist (per SLA coverage list); coastal Norway resolution varies by product.
- **Access:** free account; `copernicusmarine` Python toolbox, OPeNDAP/THREDDS, REST subsetting, S3. NetCDF-4; DOI per product (e.g. GLORYS `10.48670/moi-00021`, WAVERYS `10.48670/moi-00022`).
- **License:** free-of-charge, worldwide, non-exclusive, royalty-free, **perpetual**; may "modify, adapt, develop, create and distribute Value Added Products or Derivative Work … for any purpose" and redistribute originals. Commercial game use: **yes**. Required attribution (derived): *"Generated using E.U. Copernicus Marine Service Information; insert DOIs"*; for redistribution: *"E.U. Copernicus Marine Service Information; insert DOIs"*. SLA states services are free "until the end of the Copernicus Marine Service (planned on 30 June 2028)" — note this horizon in planning.

## Copernicus Data Space Ecosystem (CDSE) / Sentinel Hub [medium]

- **Available:** Sentinel-1/2/3/5P products and Copernicus service layers; Sentinel-3 OLCI/SLSTR relevant for ocean colour and SST; STAC catalogue, OData, S3, openEO, Sentinel Hub Process/Batch/Statistical APIs, WMS/WMTS.
- **Access:** free CDSE account; quotas are **monthly** (requests, requests/min, processing units, processed products, credits) plus a rolling 30-day transfer limit (TB); exceeding transfer quota downgrades speed, not access. Sentinel Hub as a commercial service redirects pricing to `planet.com/pricing` (per-unit prices **not published** on the pages checked → treat as commercial negotiation). [low on pricing]
- **License:** Sentinels are free and open (Copernicus), commercial use allowed with Copernicus attribution per product; the *processing service* is what carries quotas/costs. Game-safe for derived snapshots; keep an attribution line and don't rely on unpaid live heavy processing. [medium]

## MET Norway — api.met.no [high]

- **Available:** Oceanforecast 2.0 (point GeoJSON: waves, wave direction, currents, sea conditions) for Northwestern Europe; Locationforecast for wind/air; raw model archives (NorKyst/FORECAST models) on `thredds.met.no` as NetCDF/OPeNDAP. No lice/biomass (not their domain).
- **Access:** HTTPS REST, no key; **mandatory identifiable User-Agent** (app name + contact; generic UAs get 403/429). No SLA; public-funded service, overuse → throttling/blocking; POST not supported.
- **License:** NLOD 2.0 + CC BY 4.0; commercial use allowed. Credit text: *"Data from MET Norway"* or *"Based on data from MET Norway"*; naming restrictions (no "Yr"/NRK/MET branding, no implied endorsement).

## Institute of Marine Research (IMR/HI) — Algestatus [high on facts; license unclear]

- **Available:** National harmful-algae monitoring: weekly status reports (Ukestatus week 41/2026 fetched), bloom status pages, weekly satellite chlorophyll maps, event reporting via Altinn "Algeskjema". No documented public API for machine consumption.
- **License:** No open licence published; site footer states "Kopirett Algestatus 2026. Alle rettigheter reservert" (all rights reserved). → **Not game-safe** as-is. Recommendation: contact IMR (Lars-Johan Naustvoll, listed on site) for permission, or model HAB events from CMEMS chlorophyll + game logic instead. HI research-data portal exists but no explicit open licence verified. [medium]

## Norwegian Directorate of Fisheries (Fiskeridirektoratet) [high]

- **Available:** Aquaculture statistics (biomass per production area/county and "county before 2020", 2005/2017–2026 in xlsx/csv/**json**; stocking/batches; smolt release; sales; cleaner-fish stats); locality register via WMS (`gis.fiskeridir.no`, "Akvakultur lokaliteter"); licence/MTB capacity data lives in the aquaculture register (open APIs catalogued at `api.fiskeridir.no/catalog/`). The **Register for biomass data itself is exempt from public disclosure** (Forvaltningsloven §13); only the aggregates are public.
- **Access:** HTTPS file downloads; WMS; API catalogue (some APIs documented, "more will be added"); statistic bank with CSV export. No auth for open data.
- **License:** All Fiskeridir data under **NLOD** (links NLOD 1.0); commercial reuse permitted. Attribution one of: "Kilde: Fiskeridirektoratet" (and must not imply endorsement).
- **Lice counts:** site-level weekly lice counts are served via BarentsWatch FishHealth (see below), credited there to Mattilsynet; the old Fiskeridir lice-statistics web path returned 404 during research, so plan on the BW API path.

## BarentsWatch — FishHealth + Wave Forecast APIs [high]

- **Available (FishHealth):** aquaculture sites, salmon lice, diseases, treatments/countermeasures; WMS/WFS layer for disease localities (from Veterinærinstituttet). Also FiskInfo, polar lows, Saltstraumen, wave forecast (vulnerable coastal stretches; several open data sources).
- **Access (verified 2026):** developer portal live; **authentication mandatory** — self-register a client, OpenID Connect, `client_credentials` token from `id.barentswatch.no/connect/token`; REST + OpenAPI; per API terms, batch downloads must be single-threaded; high-traffic commercial use may incur server handling fees. Not a closed industry-login system.
- **License:** main rule **NLOD**; commercial use allowed with credit. Required credit: *"Data delivered by BarentsWatch"* (visible to end user) + data-owner credits, e.g. *"Information about lice, cleaner fish and treatments is provided by Mattilsynet."* Caveats: "you cannot change the data content"; special-access content (e.g. detailed fishing-gear info) is not NLOD; "v1/shipinfo" is commercial-licensed. For in-game *derived* numbers, either keep them verbatim-derived and credited, or seek written confirmation. [medium]

## NASA OceanColor / OB.DAAC [high]

- **Available:** chlorophyll-a, RRS, Kd, PAR, POC/PIC, NPP from SeaWiFS-era sensors through MODIS, VIIRS, OLCI and PACE OCI; L2–L4 products; global daily composites via the Ocean Color L3/L4 browser; direct HTTPS bulk access.
- **Access:** no-cost; **Earthdata Login required** to download; direct data access over HTTPS; OPeNDAP/ERDDAP-style tooling available. Spatial ~1–4 km (product-dependent), daily revisit.
- **License:** "NASA data are openly available without restriction" (Earthdata policy page); commercial use fine; no attribution legally required (NASA credit appreciated, and required by some product citations).

## EMODnet [high]

- **Available:** bathymetry, physics (currents, waves, ferrybox SST), chemistry, human activities, etc.; ERDDAP/WFS/WMS services.
- **Access:** free, no auth for most services.
- **License:** data products owned by the EU, **CC BY 4.0** unless indicated otherwise. Required attribution: *"This data product was created by EMODnet (https://emodnet.ec.europa.eu/en/), and is owned by the EU and licensed under the Creative Commons Attribution 4.0 International (CC BY 4.0) license."* Commercial use: yes.

## ERA5 / Copernicus Climate Data Store (C3S) [high]

- **Available:** ERA5 hourly single levels from 1940: 10 m wind, wave quantities (SWH etc.), air temp, radiation. Resolution: 0.25° atmosphere / 0.5° ocean waves (reanalysis); hourly; updated daily with ~5-day latency; GRIB on CDS; `cdsapi` download.
- **License:** since 2 Jul 2025 the Licence to use Copernicus Products was **replaced by CC-BY 4.0** across CDS/ADS/EWDS. Commercial use: yes. Attribution examples from the former licence remain good practice: *"Generated using Copernicus Climate Change Service information [Year]"* / *"Contains modified Copernicus Climate Change Service information [Year]"*; link the dataset DOI (e.g. ERA5 `10.24381/cds.adbb2d47`).

---

## Cost table

| Source | Data | Access | License (game-safe?) | Free tier | Paid from | Notes |
|---|---|---|---|---|---|---|
| Copernicus Marine | SST, currents, waves, chl/biogeochem | Toolbox/OPeNDAP/REST/S3 | **Yes** — commercial derivative + redistribution, DOI citation | 100% free | n/a; SLA notes funding to 2028 | Product DOIs required in credit |
| CDSE / Sentinel Hub | Sentinel-1/2/3/5P, OLCI chl/SST, STAC/S3, Process API | REST/OData/STAC/S3/openEO | **Yes for data (Copernicus attribution)**; processing-service terms apply | Free account w/ monthly quota (requests, PUs, transfer TB/30 days) | Commercial cloud credits/sponsorship on request; Sentinel Hub pricing now via planet.com/pricing — per-unit prices not published | Exceeding transfer quota slows, not blocks |
| MET Norway api.met.no | Coastal point waves/currents/sea temp; THREDDS model archive | REST (User-Agent required) + THREDDS/OPeNDAP | **Yes** — NLOD + CC BY 4.0, credit "Data from MET Norway" | Fully free, no key | Custom deliveries only (contact MET) | No SLA; overuse → throttle/ban |
| Fiskeridirektoratet | Biomass/stocking statistics (csv/json/xlsx), locality WMS, register APIs | HTTPS downloads, WMS, API catalog | **Yes** — NLOD, "Kilde: Fiskeridirektoratet" | Fully free | n/a | Raw biomass register not public |
| BarentsWatch (FishHealth, Wave) | Site lice/disease/treatments; coastal wave forecasts | REST + OAuth2 client credentials; WMS/WFS | **Yes with conditions** — NLOD; credits + "cannot change data content" caveat | Fully free | High-traffic server fees possible | Self-registration; single-thread batches |
| IMR/HI Algestatus | HAB status, weekly advisories, satellite chl maps | Web pages only | **Unclear → assume No** ("all rights reserved") | Free to read | n/a | Ask IMR for permission |
| NASA OceanColor/OB.DAAC | Chl-a, RRS, Kd, PAR, NPP (SeaWiFS→PACE) | HTTPS bulk, L3/L4 browser, Earthdata login | **Yes** — open, no restriction | Fully free | n/a | Login needed to download |
| EMODnet | Physics/chemistry, bathymetry | ERDDAP/WMS/WFS | **Yes** — CC BY 4.0 | Fully free | n/a | Use their attribution sentence |
| ERA5 / C3S (CDS) | Wind, waves, temp, radiation 1940→ | cdsapi/GRIB | **Yes** — CC BY 4.0 since 2 Jul 2025 | Fully free | n/a | 0.25° atm / 0.5° waves |

"Paid from" cells reflect only published tiers found during research; absence means no published commercial tier relevant to our volumes.

---

## Recommended architecture

**Pattern: build-time snapshots first, live optional.**

- **Snapshot pipeline (offline, runs in CI or manually):** fetch → reduce → derive → write compact JSON to `godot/data/` (e.g. `ocean_climatology.json`, `lice_region_distribution.json`, `biomass_weekly_profiles.json`, `hab_events.json`). Ship only *derived/aggregated* values (seasonal SST curves per region, wave-height distributions, typical lice pressure per area/week) — never raw source files. Keep a `tools/data_snapshots/credits.json` manifest with per-source DOI/attribution strings that a Credits screen renders.
- **Live client (behind config flag, default off):** `network.ocean_live=false` in settings; when enabled, one cached call per sim day per relevant endpoint (MET oceanforecast; optionally BW wave), cached under `user://ocean_cache/`, with hard timeout and immediate fallback to the snapshot if offline or throttled. Never on the tick path — apply as an async event feeding the existing environment model.
- **Legal guardrails:** attribution screen + README credits; no source logos; no implied endorsement; do not present live data as authoritative safety information; record DOIs for CMEMS/ERA5 products used.

### Proposal A — Free-first snapshots only (recommended start)

Scope: CMEMS subsetting (SST/chl/waves, monthly climatology + recent year), ERA5 wind/wave backfill, Fiskeridir biomass aggregates, MET point stats for a few farm coordinates. Deliverable: reproducible script + 3–4 JSONs + credits manifest + docs. Effort: **4–6 engineering days** (excluding legal review of BW wording). Cost: zero. Risk: lowest.

### Proposal B — A + optional live client

Adds: config flag, async client (MET + BW wave + optional CMEMS NRT), disk cache, retry/backoff, offline graceful degradation, debug overlay showing data provenance (snapshot vs live). Effort: **+2–3 days** (total 6–9). Cost: zero at our volumes; add `BarentsWatch` contact note in credits per their high-traffic policy.

---

## Open questions

1. **BarentsWatch "cannot change the data content":** does it forbid shipping derived lice-pressure numbers? (Interpretation risk [medium]; ask BW before ship.)
2. **Algestatus/HI permission:** commercial game use of HAB history — needs written permission or drop.
3. **Sentinel Hub/Planet per-unit pricing** not published; only relevant if we process Sentinel imagery ourselves (likely unnecessary: CMEMS covers chl/SST).
4. **CMEMS after 2028:** SLA states funding "until 30 June 2028"; licence is perpetual, but service continuity is a risk for *live* calls — snapshot-first design mitigates.
5. **CDSE quota fit:** need to estimate monthly transfer for our snapshot volumes before assuming the free tier suffices (likely tiny; verify).
6. **Site-level weekly lice granularity:** BW FishHealth is the path; confirm licence scope with data owners (Mattilsynet) if we redistribute near-verbatim lice series.
7. **MET archive depth** for coastal waves (THREDDS) not catalogued during this pass; needed only if we want 10-year wave hindcast beyond CMEMS/ERA5.

## Sources

- CMEMS Service Commitments & Licence (rev. Nov 2023 SLA): https://marine.copernicus.eu/user-corner/service-commitments-and-licence
- CMEMS GLORYS reanalysis product page (1/12°, 1993→): https://data.marine.copernicus.eu/product/GLOBAL_MULTIYEAR_PHY_001_030/description
- CMEMS WAVERYS product page (1/5°, 3-hourly, 1980→): https://data.marine.copernicus.eu/product/GLOBAL_MULTIYEAR_WAV_001_032/description
- CMEMS citing/DOIs guide: https://help.marine.copernicus.eu/en/articles/4444611-citing-copernicus-marine-products-and-services
- MET License page (NLOD 2.0 + CC BY 4.0, credit text): https://api.met.no/doc/License
- MET Terms of Service (User-Agent, no SLA): https://api.met.no/doc/TermsOfService
- MET FAQ (throttling/blocking, no pricing): https://api.met.no/doc/FAQ
- MET Oceanforecast docs (+THREDDS/OPeNDAP): https://api.met.no/weatherapi/oceanforecast/2.0/documentation
- MET licensing & crediting: https://www.met.no/en/free-meteorological-data/Licensing-and-crediting
- BarentsWatch FishHealth API: https://developer.barentswatch.no/docs/fishhealth
- BarentsWatch app registration & auth: https://developer.barentswatch.no/docs/appreg/
- BarentsWatch API terms (NLOD, credits, high-traffic fees): https://www.barentswatch.no/en/articles/api-terms-and-conditions/
- BarentsWatch Wave Forecast API: https://developer.barentswatch.no/docs/waveforecast
- Fiskeridir data licence (NLOD): https://www.fiskeridir.no/statistikk-tall-og-analyse/lisens-for-bruk-av-fiskeridirektoratets-data
- Fiskeridir open/sensitive data overview: https://www.fiskeridir.no/statistikk-tall-og-analyse/apne-og-sensitive-data
- Fiskeridir API catalogue: https://www.fiskeridir.no/statistikk-tall-og-analyse/api-katalog → https://api.fiskeridir.no/catalog/
- Fiskeridir biomass statistics (fylke; json/csv; 2005–2026): https://www.fiskeridir.no/statistikk-tall-og-analyse/data-og-statistikk-om-akvakultur/biomassestatistikk-etter-fylke
- Fiskeridir biomass statistics (production area): https://www.fiskeridir.no/statistikk-tall-og-analyse/data-og-statistikk-om-akvakultur/biomassestatistikk-etter-produksjonsomrade
- Aquaculture localities API (WMS, data.norge.no): https://data.norge.no/nb/data-services/c4bbabcf-e9c4-3a96-8ade-206303db10b5/akvakultur-lokaliteter
- Algestatus — national HAB monitoring (week 41/2026 status; "all rights reserved"): https://algestatus.hi.no/
- IMR news on algae monitoring tech (30.10.2024): https://www.hi.no/en/hi/news/2024/october/new-technology-improves-the-monitoring-of-harmful-algae
- CDSE Quotas & Limitations: https://documentation.dataspace.copernicus.eu/Quotas.html
- CDSE FAQ: https://documentation.dataspace.copernicus.eu/FAQ.html
- Sentinel Hub pricing (redirects to Planet): https://www.sentinel-hub.com/pricing/
- NASA Earthdata Ocean Color L3/L4 browser ("openly available without restriction… Earthdata Login required"): https://www.earthdata.nasa.gov/data/tools/ocean-color-level-3-4-browser
- NASA OB.DAAC data access & tools: https://www.earthdata.nasa.gov/centers/ob-daac/data-access-tools
- EMODnet Terms of Use (CC BY 4.0 + attribution): https://emodnet.ec.europa.eu/en/terms-use-emodnet-online-services-data-and-data-products
- ECMWF forum: CC-BY replaces old licence on 02 July 2025 (announcement 13 June 2025): https://forum.ecmwf.int/t/cc-by-licence-to-replace-licence-to-use-copernicus-products-on-02-july-2025/13464
- ERA5 hourly single levels (0.25°/0.5° waves, 1940→, CC-BY): https://cds.climate.copernicus.eu/datasets/reanalysis-era5-single-levels
- NLOD 2.0 licence text: https://data.norge.no/nlod/en/2.0/
