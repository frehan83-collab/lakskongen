# W1 — Decision memos (R1–R5) — session 0/1 boundary

*Compiled 2026-10-12 from the five research briefs (`DATA_SOURCES.md`,
`TECH_GRAPHICS.md`, `COMMERCIAL_RELEASE.md`, `MCP_EVALUATION.md`,
`SKILLS_AUDIT.md`). Statuses: **ADOPTED** (build now) / **DEFERRED** (revisit at
named wave) / **REJECTED** (with reason) / **GATE** (owner decision).*
*Cost exposure of everything ADOPTED: $0. No paid dependency is proposed.*

---

## 1. R1 — Data layer (feeds W3/S4)

**Decision: build-time snapshots only (Proposal A), free-first, with a credits
manifest; live client deferred to a later wave behind a config flag.**

| Source | Use in sim | License verdict | Status |
|---|---|---|---|
| Copernicus Marine (CMEMS) | SST, waves, currents, chl-a climatology for realism bands + seasonal curves | Commercial derivative OK w/ DOI citation | **ADOPTED** |
| MET Norway (api.met.no) | Coastal point forecasts/archives for calibration | NLOD/CC-BY; identifiable User-Agent required | **ADOPTED** |
| Fiskeridirektoratet | Biomass/stocking/lice *aggregate* statistics (NLOD) | NLOD, credit "Kilde: Fiskeridirektoratet" | **ADOPTED** |
| BarentsWatch FishHealth | Site-level lice benchmark (server-side fetch only) | NLOD + owner credits; "no modification" caveat → we use derived facts + attribution | **ADOPTED (server-side)** |
| ERA5/C3S (+EMODnet) | Wind/wave backfill, bathymetry depth | CC-BY since 2025-07-02 | **ADOPTED** |
| NASA OceanColor | Deep chl-a archive (optional, overlaps CMEMS) | Open, attribution appreciated | **DEFERRED** (redundant first pass) |
| CDSE/Sentinel Hub processing | Imagery processing service | Quotas OK; commercial pricing unpublished | **DEFERRED** (no paid negotiation) |
| Algestatus (IMR/HI) | HAB monitoring | "All rights reserved" — not game-safe | **REJECTED** (model blooms from CMEMS chl-a + game logic instead) |

Required ship note: attribution/credits screen entry listing Copernicus Marine
(DOl), MET Norway, Fiskeridirektoratet, BarentsWatch + data owners (Mattilsynet
etc.), ERA5/C3S. Snapshot provenance metadata (source, license, retrieval date)
is mandatory per G-LIC.

## 2. R2 — Graphics & stack (feeds W2/S1 + W6/S7)

**Decision: stay on Godot 4.7.2 (pin; do not chase 4.8-dev). Execute the ranked
list below in S7, each behind its screenshot + perf gate. FFT ocean parked.**

Engine notes: 4.7.2 stable; 4.8 at feature freeze with a Steam Deck controls
regression (GH-123704) — pin until fixed; Metal default on macOS; D3D12 default
on Windows.

Ranked upgrades (impact-first, gates from TECH_GRAPHICS.md §ranked list):

| # | Upgrade | Effort | Gate |
|---|---|---|---|
| 1 | Shore blending + foam + wet-line water pass | 3–5 d | `17_fjord_shore` Δ≥16 + control |
| 2 | Sky/atmosphere + HDR output + exposure | 2–4 d | `08`/`09` Δ≥9 |
| 3 | Terrain anti-tiling + triplanar cliffs | 3–5 d | `08` Δ≥9 + cliff Δ≥16 |
| 4 | GI/AA quality tiers (TAA, half-res SSR, SDFGI high tier) | 5–8 d | `10` Δ≥9 + 60 FPS M1 Pro |
| 5 | Instanced boats/vegetation/birds | 3–5 d | `08b` Δ≥9 + population control |
| 6 | Underwater upgrade (fog gradient, caustics, shafts) | 4–6 d | `11` Δ≥16 + control |
| 7 | Decals (static now, animated after 4.8) | 2–3 d | `18_fjord_decals` Δ≥16 |
| 8 | FFT ocean | 4–6 wk | **PARKED** until blind-playtest evidence |

S1 (living fjord readout) stays first — it is cheap, unlocks store screenshots,
and mostly wires existing systems (see `docs/run/plans/S1_LIVING_FJORD.md`).

## 3. R3 — Commercial release (anchors D2/D8, S5/S6)

**Decision: target Steam Next Fest February 2027 (Feb 22 – Mar 1) with the June
2027 window as fallback; never burn the one-shot on a weak demo.**

| Date | Milestone |
|---|---|
| Oct–Nov 2026 | Freeze demo scope; publish Coming Soon page |
| Dec 2026 | Demo feature-complete; playtests; capsule/screenshots/trailer ready |
| 2027-01-10 | Next Fest registration deadline (only if on track) |
| 2027-01-25 | Demo + store page submitted for review (press gate) |
| 2027-02-08 | All items submitted (final gate) |
| 2027-02-22 | Demo live before 10:00 PST |
| After 2027-03-01 | Release window; 10–15% launch discount configured |

Other adopted facts: price band **$19.99–24.99** (owner GATE at S6); demo = separate
App ID; Deck Verified ineligible for demos (so D3 Deck-verify applies to the full
game); AI-content disclosure required for player-facing generated content (none
planned yet — fal/audio use would trigger it, owner GATE); PEGI/IARC free, USK
via Valve self-rating; localization EN+NB now, SC/JA/KO or DE/FR considered later.

## 4. R4 — MCP tooling (applies at next harness restart)

**Decision: adopt Context7 + GitHub MCP at the next natural opencode restart;
apply the godot bridge env fix at the same time; everything else deferred.**

- **Context7** (ADOPTED, free): remote server snippet in `MCP_EVALUATION.md` §2.1.
- **GitHub MCP** (ADOPTED, free): prefer remote OAuth; local v2.0.2 binary verified as fallback.
- **Godot bridge fix** (READY): `environment: {"GODOT_MCP_PROJECT_PATH":"<repo>/godot"}`
  in `opencode.json` (V2 shape `mcp.servers`) + service restart → retires
  `tools/mcp_bridge_alias.py` (keep the script as fallback/reference).
- **fal.ai / ElevenLabs-class / ai-vision / Figma / SQLite** (DEFERRED to the
  waves that need them; any paid tier = owner GATE; ElevenLabs repo archived
  2026-08-20 — re-verify before adoption).
- **claude-devfleet**: not connected here; harness `subagent` covers the need.

## 5. R5 — Skills (feeds every wave)

ADOPTED statuses: repo pack (6 present) + the two new skills authored in W0
(`lakskongen-feature`, `lakskongen-screenshot-evidence`). Remaining to author
(.claude/skills/ecc/), recommended order:

| Skill | Before wave | Validates on |
|---|---|---|
| `lakskongen-bridge-contract` | W2 | first bridge touch (S2 time-series) |
| `lakskongen-save-compat` | W4 | save fuzz harness |
| `lakskongen-data-provenance` | W3 | first snapshot ingest (S4) |
| `lakskongen-release` | W4 exit | export/depot checklist |
| `lakskongen-research-brief` | next re-dispatch | a live brief |

Blind-spot fences from SKILLS_AUDIT §4 apply (generic skills never override
RUN_LOG §1–§2 or AGENTS.md).

## 6. Plan adjustments (applied to `AUTONOMOUS_TOP_TIER_RUN.md`)

1. W2 unchanged: S1 → S2 → S3 in that order (S1 first confirmed by R2's "cheap wins").
2. W3/S4 scope fixed: CMEMS + MET + Fiskeridir + BarentsWatch + ERA5 snapshots;
   provenance metadata mandatory; Algestatus excluded.
3. W6/S7 scope = ranked list §2 (8 items, FFT parked); engine pin 4.7.2; 4.8
   reassessed only after GH-123704 is fixed.
4. Ship calendar (S5/S6/D2/D8) anchored to the §3 table; Next Fest registration
   is a human gate.
5. MCP restarts carry the §4 config changes (single restart event).

## 7. Human-gate queue deltas

| New gate | Status | Needs from owner |
|---|---|---|
| Next Fest registration (Feb 2027; deadline 2027-01-10) | pending | store page + demo readiness decision |
| Price final ($19.99–24.99 band) | pending | owner at S6 |
| Any paid MCP/API (fal/audio/Sentinel Hub) | none requested | sign-off if/when proposed |
| Contacts for playtest (D1) | pending | schedule + testers |

## 8. Open questions carried forward

- BarentsWatch "cannot change the data content" vs derived in-game numbers —
  keep derived-only + credits; request written confirmation if counters doubt it.
- CMEMS funding horizon (2028) — snapshot locally so shipped data survives.
- CDSE quota sizing if Sentinel imagery ever becomes load-bearing (deferred).
