"""Suitability rules: every weight and threshold with its source (year + URL).

A value marked GB is game balance: chosen by the project, with the reasoning
given, where no official number exists. build_suitability.py reads only this
module, and writes it verbatim (RULES) into godot/data/geo/suitability_rules.json
so the game, the docs and the data cannot drift apart.

Score of a sea cell (0..1), open net pens:
    base   = sum(w_i * c_i) / sum(w_i)   over the components c_i below
    score  = base * river_factor
    excluded when any hard constraint holds (score 0, reason recorded).
Copernicus layers (currents, waves, oxygen) are optional components with
weight 0 until tools/geodata/fetch_copernicus.py has produced them; with
weight 0 they are neutral and the score is unchanged.
"""

from __future__ import annotations

# --- Hard constraints (cell excluded) --------------------------------------

HARD = {
    "salmon_fjord": {
        "rule": "No new food-fish farms for anadromous fish inside a national salmon fjord",
        "source": "Forskrift om særskilte krav til akvakulturrelatert virksomhet i eller ved nasjonale "
                  "laksevassdrag og nasjonale laksefjorder (FOR-2009-06-22-961) § 3, last amended 2023",
        "url": "https://lovdata.no/dokument/SF/forskrift/2009-06-22-961",
    },
    "national_salmon_river": {
        "radius_m": 5000,
        "rule": "Aquaculture sites at least 5 km from a national salmon river (its mouth)",
        "source": "FOR-2009-06-22-961 § 6 (amended 2023-02-28) and § 2",
        "url": "https://lovdata.no/dokument/SF/forskrift/2009-06-22-961",
    },
    "protected_area": {
        "rule": "Inside a marine or marine-and-terrestrial protected area (naturvernområde)",
        "source": "Naturmangfoldloven ch. V; Miljødirektoratet Naturvernområder (2026)",
        "url": "https://kartkatalog.geonorge.no/metadata/5857ec0a-8d2c-4cd8-baa2-0dc54ae213b4",
    },
    "coral_closure": {
        "rule": "Inside a coral-reef protection area (forbudsområde)",
        "source": "Forskrift om forbud mot fiske i korallrevområder (FOR-2016-01-08-8)",
        "url": "https://lovdata.no/dokument/SF/forskrift/2016-01-08-8",
    },
    "coral_reef": {
        "radius_m": 500,
        "rule": "Within 500 m of a recorded coral reef (MAREANO); GB: a mooring spread reaches about "
                "3x the water depth, so a 150 m site's anchors lie up to ~450-500 m out",
        "source": "Havforskningsinstituttet, Korallrev (2026); naturmangfoldloven § 1 / FOR-2016-01-08-8 "
                  "(reefs protected)",
        "url": "https://kartkatalog.geonorge.no/metadata/31edb985-138e-46a7-a910-a0c1cd9baf4c",
    },
    "fairway": {
        "buffer_m": {"Hovedled": 300, "Biled": 150},
        "rule": "Within the buffer of a main (hovedled) or secondary (biled) fairway centre line. GB, "
                "calibrated on the registry: no active sea salmon site lies within 250 m of a hovedled "
                "and 8 of 1050 within 100 m of a biled (Akvakulturregisteret 2026-10-20)",
        "source": "Kystverket, Hovedled og biled (2026); havne- og farvannsloven (LOV-2019-06-21-70) § 14",
        "url": "https://kartkatalog.geonorge.no/metadata/8ff1538a-a93c-4391-8d6f-3555fc37819c",
    },
    "farm_too_close": {
        "min_km": 2.5,
        "rule": "Closer than 2.5 km to an active sea site holding salmonids",
        "source": "Mattilsynet's establishment guideline as cited in Naturvernforbundet's 2020 hearing "
                  "statement: 2.5 km recommended minimum between sites of up to 3600 t MTB",
        "url": "https://naturvernforbundet.no/content/uploads/sites/20/2025/01/26.09.2020-Horingsuttalelse-"
               "vedrorende-soknad-om-utvidelse-av-akvakulturtillatelse-for-laks-og-orret-pa-lokalitet-37217-"
               "Dale-i-Harstad-kommune-.pdf",
    },
    "outside_planning_area": {
        "rule": "Open pens only: more than 1 nautical mile outside the baseline (grunnlinje), where "
                "municipal coastal-zone plans do not reach; sites farther out belong to the Havbruk til "
                "havs regime (offshore candidates)",
        "source": "Plan- og bygningsloven (LOV-2008-06-27-71) § 1-2: in the sea the Act applies out to "
                  "one nautical mile outside the baselines; baseline from Kartverket N250 (Grunnlinje)",
        "url": "https://lovdata.no/lov/2008-06-27-71/§1-2",
    },
    "too_shallow": {"depth_m": 15, "rule": "Shallower than 15 m (GB: a 10-15 m deep net needs clearance)"},
    "too_deep": {"depth_m": 300, "rule": "Deeper than 300 m for open pens (GB: mooring limit of coastal pen systems)"},
    "no_depth": {"rule": "No depth data (outside Kartverket's grid and EMODnet)"},
    "land": {"rule": "Land, lake or river cell (Kartverket N250)"},
    "outside_po": {"rule": "Outside the production area polygon"},
}

# --- Score components (0..1) -----------------------------------------------

COMPONENTS = {
    "depth": {
        "weight": 0.30,
        "ramp_m": [15, 25, 150, 300],
        "rule": "Trapezoid: 0 at 15 m, 1 from 25 to 150 m, 0 at 300 m",
        "source": "Owner brief 2026-10-20 (open pens ~25-150 m); GB ramps",
    },
    "exchange": {
        "weight": 0.25,
        "radius_m": 3000,
        "ramp": [0.30, 0.80],
        "rule": "Water-exchange proxy: share of sea within 3 km (openness), 0 at 30 %, 1 at 80 %. "
                "GB proxy: enclosed basins (threshold fjords, pollen) have poor exchange; open sounds good",
        "source": "Proxy, documented in docs/geo/GEODATA.md; complemented by the Copernicus current layer",
    },
    "farm_distance": {
        "weight": 0.25,
        "ramp_km": [2.5, 5.0],
        "rule": "Distance to the nearest active sea salmonid site: 0 at 2.5 km, 1 at 5 km",
        "source": "Mattilsynet letter 2022-01-21 (ref 2021/255437) to Vanylven kommune: 'anbefalt "
                  "minsteavstand på 5 km i sjø mellom matfiskanlegg (laks) utenfor koordinerte "
                  "brakkleggingsområder'",
        "url": "https://www.hareid.kommune.no/_f/i7ae2a7f9-bb90-429d-b491-c067d7897454/"
               "mattilsynet-uttale-til-interkommunal-kystsoneplan-for-sore-sunnmore-dokid-274716.pdf",
    },
    "temperature": {
        "weight": 0.20,
        "ramp_c": [2.0, 8.0, 16.0, 20.0],
        "rule": "Mean over the 12 monthly means of the area (3 m): 0 at 2 °C, 1 from 8 to 16 °C, "
                "0 at 20 °C",
        "source": "Optimum 13-16 °C, ITmax ~22 °C: Calado et al. 2021, Animals 11:1800; growth slows "
                  "below ~4-5 °C: Alanärä et al. 2001 (as used in docs/BALANCE.md S2-03). Climatology: "
                  "ProductionAreas.cs (Havforskningsinstituttet, Rapport fra havforskningen 2025-69)",
        "url": "https://doi.org/10.3390/ani11061800",
    },
    # Optional Copernicus Marine layers (fetch_copernicus.py): weight 0 (neutral)
    # until fetched, then weight_when_available. Values come from the nearest
    # model water cell (the models mask most fjords as land).
    "current": {
        "weight": 0.0, "weight_when_available": 0.15,
        "ramp_ms": [0.01, 0.05, 0.40, 0.80],
        "rule": "Annual mean current speed in the upper 30 m (TOPAZ4 monthly means 2018-2022): 0 at "
                "0.01 m/s, 1 from 0.05 to 0.40 m/s, 0 at 0.8 m/s. GB: residual (non-tidal) currents at "
                "the 12.5 km model scale; existing sites span ~0.01-0.15 m/s (5-95 %)",
        "source": "Copernicus Marine ARCTIC_MULTIYEAR_PHY_002_003, https://doi.org/10.48670/moi-00007",
    },
    "waves": {
        "weight": 0.0, "weight_when_available": 0.20,
        "ramp_m": [-1.0, 0.0, 1.2, 2.8],
        "rule": "Highest monthly mean significant wave height (winter exposure): 1 up to 1.2 m, 0 at 2.8 m. "
                "GB, calibrated on the registry: active sea salmon sites have a median of 0.5 m and a 95th "
                "percentile of 2.2 m on this measure, so open pens sit inside the skerries, not on the "
                "exposed outer coast",
        "source": "Copernicus Marine ARCTIC_MULTIYEAR_WAV_002_013, https://doi.org/10.48670/moi-00008",
    },
    "oxygen": {
        "weight": 0.0, "weight_when_available": 0.10,
        "ramp_mmol": [180.0, 250.0],
        "rule": "Lowest monthly mean dissolved O2 in the upper 30 m: 0 at 180, 1 at 250 mmol/m3. GB: "
                "salmon need > ~70-80 % saturation (~250 mmol/m3 at 10 °C). At the model's 25 km scale "
                "the upper 30 m never falls below ~255 mmol/m3 on this coast, so this never binds; sill-"
                "fjord basin hypoxia is below the pens and not resolved",
        "source": "Copernicus Marine ARCTIC_MULTIYEAR_BGC_002_005, https://doi.org/10.48670/moi-00006",
    },
}

OPTIONAL = ("current", "waves", "oxygen")


def weight(name: str, copernicus: bool) -> float:
    """Effective weight of a component (optional layers only count when fetched)."""
    c = COMPONENTS[name]
    return c.get("weight_when_available", c["weight"]) if (copernicus and name in OPTIONAL) else c["weight"]

# Soft penalty near salmon-bearing rivers that are not national salmon rivers.
RIVER_FACTOR = {
    "radius_m": 5000,
    "min_factor": 0.6,
    "rule": "score x factor, the factor rising linearly from 0.6 at an anadromous river mouth to 1 at "
            "5 km. GB, scaled on the 5 km of FOR-2009-06-22-961 § 6 (lice pressure on wild salmonids)",
    "source": "Miljødirektoratet, Lakseregisteret (2026)",
}

# Offshore (Havbruk til havs) candidates: semi-submersible units in deep water.
OFFSHORE = {
    "depth_ramp_m": [60, 100, 350, 600],
    "rule": "Inside a Havbruk til havs area; depth trapezoid 0 at 60 m, 1 from 100 to 350 m, 0 at 600 m "
            "(GB: OF1-class units are moored in ~100-300 m; the 2019 report mapped areas outside the "
            "baseline in workable depths)",
    "source": "Fiskeridirektoratet / Havforskningsinstituttet, Havbruk til havs (2019, 2022, 2024, 2026)",
    "url": "https://www.fiskeridir.no/Akvakultur/Tema/Havbruk-til-havs",
    "stage_bonus": {"tilradning_2019": 0.85, "anbefaling_2022": 0.92, "konsekvensvurdering_2024": 0.96,
                    "kan_utlyses_2026": 1.0},
    "spacing_km": 10.0,
    "per_area": 3,
    "ideal_depth_m": 200,
    "ranking": "score (3 decimals) first; equal scores prefer depth nearer 200 m, then north, then west",
    "min_score": 0.5,
}

CANDIDATES = {
    "per_po": 12,
    "min_score": 0.55,
    "spacing_km": 10.0,
    "ideal_depth_m": 80,
    "rule": "Greedy, deterministic: highest score (3 decimals) first; equal scores prefer more open "
            "water, then depth nearer 80 m, then north, then west. No candidate within 10 km of a "
            "chosen one: twice the Mattilsynet 5 km norm (which already holds between new sites), so "
            "the 12 candidates of an area spread over it (GB)",
}

GRID_RES_M = 250

# National salmon rivers, FOR-2009-06-22-961 vedlegg 2 (name, county in the
# regulation's wording, Lakseregisteret vassdrag numbers). build_layers.py
# matches a river to Lakseregisteret's outlets by vassdrag number where one is
# given (the registry spells many names differently, e.g. "Vossovassdraget",
# "Gaula (Sunnfjord)"), otherwise by name inside the county's latitude band.
NATIONAL_SALMON_RIVERS = (
    ("Enningsdalselva", "Østfold", ("001.1Z",)), ("Numedalslågen", "Vestfold", None),
    ("Mandalselva", "Vest-Agder", ("022.Z",)), ("Bjerkreimselva", "Rogaland", None),
    ("Suldalslågen", "Rogaland", None), ("Ogna", "Rogaland", None), ("Håelva", "Rogaland", None),
    ("Figgjo", "Rogaland", None), ("Etneelva", "Hordaland", ("041.Z",)), ("Vosso", "Hordaland", ("062.Z",)),
    ("Nærøydalselva", "Sogn og Fjordane", ("071.Z",)), ("Lærdalselva", "Sogn og Fjordane", ("073.Z",)),
    ("Nausta", "Sogn og Fjordane", None), ("Vikja", "Sogn og Fjordane", None),
    ("Flåmselva", "Sogn og Fjordane", ("072.2Z",)), ("Årøyelva", "Sogn og Fjordane", ("077.Z",)),
    ("Oldenelva", "Sogn og Fjordane", None), ("Stryneelva", "Sogn og Fjordane", None),
    ("Gaula", "Sogn og Fjordane", ("083.Z",)), ("Eidselva", "Sogn og Fjordane", None),
    ("Rauma", "Møre og Romsdal", ("103.Z",)), ("Ørstaelva", "Møre og Romsdal", None),
    ("Driva", "Møre og Romsdal", ("109.Z",)), ("Surna", "Møre og Romsdal", None),
    ("Stordalselva-Norddalselva", "Sør-Trøndelag", ("135.Z", "135.AZ")),
    ("Gaula", "Sør-Trøndelag", ("122.Z",)),
    ("Orkla", "Sør-Trøndelag", None), ("Nidelva", "Sør-Trøndelag", ("123.Z",)),
    ("Stjørdalselva", "Nord-Trøndelag", ("124.Z",)), ("Figga", "Nord-Trøndelag", None),
    ("Steinkjervassdraget", "Nord-Trøndelag", None), ("Årgårdsvassdraget", "Nord-Trøndelag", None),
    ("Verdalselva", "Nord-Trøndelag", ("127.Z",)), ("Namsen", "Nord-Trøndelag", None),
    ("Roksdalsvassdraget", "Nordland", None), ("Vefsna", "Nordland", ("151.Z",)),
    ("Ranaelva", "Nordland", ("156.Z",)), ("Beiarelva", "Nordland", ("161.Z",)),
    ("Reisaelva", "Troms", ("208.Z",)), ("Målselva", "Troms", ("196.Z",)),
    ("Kvænangselva", "Troms", None), ("Altaelva", "Finnmark", None),
    ("Repparfjordelva", "Finnmark", None), ("Stabburselva", "Finnmark", ("223.Z",)),
    ("Lakselva", "Finnmark", ("224.Z",)), ("Børselva", "Finnmark", ("225.Z",)),
    ("Langfjordvassdraget", "Finnmark", ("233.Z",)), ("Tana", "Finnmark", ("234.Z",)),
    ("Komagelva", "Finnmark", None), ("Vestre Jakobselv", "Finnmark", None),
    ("Neidenelva", "Finnmark", None), ("Kongsfjordelva", "Finnmark", None),
)


# D17: no real company names in the game. The same list as BRANDS in
# tests/test_text_correctness.py (tests/test_geodata.py checks every shipped
# geo file against that canonical list). Some are also place names (Bremnes,
# Eidsfjord, Kvarøy); a name that contains one as a whole word is not shipped.
D17_BRANDS = (
    "Cermaq", "Bakkafrost", "Mowi", "Multiexport", "Whole Foods", "SalMar", "Salmar", "Lerøy",
    "Grieg", "Marine Harvest", "Nova Sea", "Norway Royal Salmon", "Måsøval", "Kvarøy",
    "SinkabergHansen", "Bremnes", "Hofseth", "Wilsgård", "Arnøy Laks", "Skagerak Salmon",
    "Eidsfjord", "AquaBounty", "AquAdvantage", "AquaGen", "SalmoBreed", "Benchmark Genetics",
    "Greenpeace", "WWF", "Bellona", "REMA 1000", "Coop Mega", "Skretting", "BioMar", "EWOS",
    "Cargill", "Nasdaq", "NASDAQ", "Fish Pool", "Euronext", "Oslo Børs", "Oslo Stock Exchange",
    "Stingray", "AquaCloud", "Nordlaks", "NordLaks",
)


def brand_free(name: str | None) -> bool:
    """False when the name contains a D17 brand as a whole word (any capitalisation)."""
    import re

    if not name:
        return True
    low = name.lower()
    return not any(re.search(rf"(?<![\w-]){re.escape(b.lower())}(?![\w-])", low) for b in D17_BRANDS)


def clean_name(name: str | None) -> str | None:
    """The name, or None when it would put a real company name in the game (D17)."""
    return name if brand_free(name) else None


def trapezoid(v, a: float, b: float, c: float, d: float):
    """0 below a, rising to 1 at b, 1 to c, falling to 0 at d (numpy or float)."""
    import numpy as np

    v = np.asarray(v, dtype=np.float64)
    up = np.clip((v - a) / max(b - a, 1e-9), 0.0, 1.0) if b > a else (v >= a).astype(np.float64)
    down = np.clip((d - v) / max(d - c, 1e-9), 0.0, 1.0) if d > c else (v <= d).astype(np.float64)
    return np.minimum(up, down)


def ramp(v, lo: float, hi: float):
    """0 at lo, 1 at hi, clamped."""
    import numpy as np

    return np.clip((np.asarray(v, dtype=np.float64) - lo) / (hi - lo), 0.0, 1.0)


def rules_document(copernicus: bool = False) -> dict:
    """Everything above, as the JSON the game ships (suitability_rules.json)."""
    return {
        "grid_res_m": GRID_RES_M,
        "copernicus_layers_used": copernicus,
        "effective_weights": {k: weight(k, copernicus) for k in COMPONENTS},
        "hard_constraints": HARD,
        "components": COMPONENTS,
        "river_factor": RIVER_FACTOR,
        "offshore": OFFSHORE,
        "candidates": CANDIDATES,
        "formula": "score = (sum w_i c_i / sum w_i) x river_factor; 0 when a hard constraint holds",
        "national_salmon_rivers": [{"name": n, "county": c} for n, c, _ in NATIONAL_SALMON_RIVERS],
    }
