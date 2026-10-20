"""Pinned source registry for the national map data (geo pipeline).

One entry per raw source. geo_fetch.py downloads exactly these endpoints, and
build_manifest.py writes godot/data/geo/LICENCES.md from the licence fields
here, so a layer cannot ship without a recorded licence.

Licences were checked on 2026-10-20 against each dataset's Geonorge metadata
record (kartkatalog.geonorge.no/metadata/<uuid>, field "Constraints") and, for
the regulation lists, against Lovdata. Only NLOD and CC BY 4.0 sources are
used: both allow commercial use with attribution. The optional Copernicus
Marine layers are used under the Copernicus Marine Service licence (free,
any purpose, attribution plus DOI); regulation texts are public domain.
"""

from __future__ import annotations

from dataclasses import dataclass, field

NLOD_2 = ("NLOD 2.0", "https://data.norge.no/nlod/no/2.0")
CC_BY_4 = ("CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/")
CMEMS_LICENCE = ("Copernicus Marine Service Licence",
                 "https://marine.copernicus.eu/user-corner/service-commitments-and-licence")

FDIR_AKVA = "https://gis.fiskeridir.no/server/rest/services/FiskeridirWFS_akva/MapServer"
FDIR_WFS = "https://gis.fiskeridir.no/server/rest/services/FiskeridirWFS/MapServer"
FDIR_VERN = "https://gis.fiskeridir.no/server/rest/services/FiskeridirWFS_vern/MapServer"
MDIR_REST = "https://kart.miljodirektoratet.no/arcgis/rest/services"
GEONORGE_DL = "https://nedlasting.geonorge.no/geonorge"
KV_DEPTH_WCS = "https://wms.geonorge.no/skwms1/wms.dtm2"

FDIR_ATTRIBUTION = "Fiskeridirektoratet"
FDIR_LICENCE_NOTE = (
    "Geonorge metadata: 'All bruk av data og tjenester fra Fiskeridirektoratet er underlagt "
    "Norsk lisens for offentlige data (NLOD)'; 'Fiskeridirektoratet skal oppgis som kilde'. "
    "The record links NLOD 1.0; NLOD 2.0 is the current version and both allow commercial use "
    "with attribution."
)


@dataclass(frozen=True)
class Source:
    """A raw source: where it comes from and the terms it is used under."""

    id: str
    title: str
    owner: str
    kind: str  # arcgis | download | wcs_tiles | download_list | derived
    url: str
    licence: str
    licence_url: str
    attribution: str
    metadata_url: str
    params: dict = field(default_factory=dict)
    note: str = ""


def _geonorge(uuid: str) -> str:
    return f"https://kartkatalog.geonorge.no/metadata/{uuid}"


SOURCES: tuple[Source, ...] = (
    Source(
        id="fdir_localities",
        title="Akvakultur - lokaliteter (Akvakulturregisteret)",
        owner="Fiskeridirektoratet",
        kind="arcgis",
        url=f"{FDIR_AKVA}/0",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=f"{FDIR_ATTRIBUTION}, Akvakulturregisteret",
        metadata_url=_geonorge("4ca8af5e-ffc7-4636-847d-4eca92c4a3b0"),
        note=FDIR_LICENCE_NOTE + " Owner/operator fields are dropped on read (D17).",
    ),
    Source(
        id="fdir_locality_areas",
        title="Flate ihht Akvakulturregisteret (locality area polygons)",
        owner="Fiskeridirektoratet",
        kind="arcgis",
        url=f"{FDIR_AKVA}/80",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=f"{FDIR_ATTRIBUTION}, Akvakulturregisteret",
        metadata_url=_geonorge("4ca8af5e-ffc7-4636-847d-4eca92c4a3b0"),
        note=FDIR_LICENCE_NOTE,
    ),
    Source(
        id="fdir_mooring_areas",
        title="NYTEK areal (installation and mooring areas)",
        owner="Fiskeridirektoratet",
        kind="arcgis",
        url=f"{FDIR_AKVA}/39",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=f"{FDIR_ATTRIBUTION}, NYTEK",
        metadata_url=_geonorge("0136a86b-99d9-4bab-be46-29084205fbbc"),
        params={"out_fields": "objectid,loknr,areatype,installationarea_index"},
        note=FDIR_LICENCE_NOTE + " Only loknr, areatype and the geometry are requested (no holder or "
                                 "inspector fields).",
    ),
    Source(
        id="fdir_production_areas",
        title="Produksjonsområder for akvakultur (PO1-PO13)",
        owner="Nærings- og fiskeridepartementet / Fiskeridirektoratet",
        kind="arcgis",
        url=f"{FDIR_AKVA}/43",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=f"{FDIR_ATTRIBUTION} (produksjonsområdeforskriften FOR-2017-01-16-61)",
        metadata_url=_geonorge("60a6a894-578c-428c-819d-c00cda835196"),
        note="Geonorge metadata: open data, 'No conditions apply to access and use'; served by "
             "Fiskeridirektoratet under its NLOD terms, credited the same way.",
    ),
    Source(
        id="fdir_hth_2019",
        title="Havbruk til havs: undersøkelsesområder, tilrådning 2019",
        owner="Fiskeridirektoratet / Havforskningsinstituttet",
        kind="arcgis",
        url=f"{FDIR_AKVA}/29",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=f"{FDIR_ATTRIBUTION}, Havbruk til havs",
        metadata_url=_geonorge("0136a86b-99d9-4bab-be46-29084205fbbc"),
        note=FDIR_LICENCE_NOTE,
    ),
    Source(
        id="fdir_hth_2022",
        title="Havbruk til havs: undersøkelsesområder, anbefaling 2022",
        owner="Fiskeridirektoratet",
        kind="arcgis",
        url=f"{FDIR_AKVA}/88",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=f"{FDIR_ATTRIBUTION}, Havbruk til havs",
        metadata_url=_geonorge("0136a86b-99d9-4bab-be46-29084205fbbc"),
        note=FDIR_LICENCE_NOTE,
    ),
    Source(
        id="fdir_hth_2024",
        title="Havbruk til havs: konsekvensvurdering 2024",
        owner="Fiskeridirektoratet",
        kind="arcgis",
        url=f"{FDIR_AKVA}/189",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=f"{FDIR_ATTRIBUTION}, Havbruk til havs",
        metadata_url=_geonorge("0136a86b-99d9-4bab-be46-29084205fbbc"),
        note=FDIR_LICENCE_NOTE,
    ),
    Source(
        id="fdir_hth_2026",
        title="Havbruk til havs: områder som kan utlyses (kgl.res. 2026)",
        owner="Fiskeridirektoratet",
        kind="arcgis",
        url=f"{FDIR_AKVA}/193",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=f"{FDIR_ATTRIBUTION}, Havbruk til havs",
        metadata_url=_geonorge("0136a86b-99d9-4bab-be46-29084205fbbc"),
        note=FDIR_LICENCE_NOTE,
    ),
    Source(
        id="fdir_salmon_fjords",
        title="Nasjonale laksefjorder",
        owner="Fiskeridirektoratet",
        kind="arcgis",
        url=f"{FDIR_WFS}/35",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=FDIR_ATTRIBUTION,
        metadata_url=_geonorge("b12d041d-03d4-4562-b333-bc9d7c9bd123"),
        note=FDIR_LICENCE_NOTE,
    ),
    Source(
        id="fdir_coral_closures",
        title="Korallrev - forbudsområder",
        owner="Fiskeridirektoratet",
        kind="arcgis",
        url=f"{FDIR_VERN}/6",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution=FDIR_ATTRIBUTION,
        metadata_url=_geonorge("3be8f59c-cf30-47b5-ab5d-61abab25942b"),
        note=FDIR_LICENCE_NOTE,
    ),
    Source(
        id="hi_coral_reefs",
        title="Korallrev (MAREANO / Havforskningsinstituttet)",
        owner="Havforskningsinstituttet",
        kind="download",
        url=f"{GEONORGE_DL}/KystOgFiskeri/Korallrev/GML/KystOgFiskeri_0000_Norge_25833_Korallrev_GML.zip",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution="Havforskningsinstituttet (MAREANO)",
        metadata_url=_geonorge("31edb985-138e-46a7-a910-a0c1cd9baf4c"),
        note="Geonorge metadata: NLOD, 'Ved bruk skal det refereres til Havforskningsinstituttet'.",
    ),
    Source(
        id="mdir_protected_areas",
        title="Naturvernområder",
        owner="Miljødirektoratet",
        kind="arcgis",
        url=f"{MDIR_REST}/vern/MapServer/0",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution="Miljødirektoratet (Naturbase)",
        metadata_url=_geonorge("5857ec0a-8d2c-4cd8-baa2-0dc54ae213b4"),
        params={"out_fields": "OBJECTID,naturvernId,navn,verneform,majorEcosystemType,iucn",
                "max_allowable_offset": 20, "page": 500},
        note="Geonorge metadata: NLOD 2.0, 'Som referanse til kartet oppgis Miljødirektoratet'. "
             "Fetched generalised server-side (maxAllowableOffset 20 m).",
    ),
    Source(
        id="mdir_anadromous_outlets",
        title="Anadrome laksefisk - utløpspunkt (Lakseregisteret)",
        owner="Miljødirektoratet",
        kind="arcgis",
        url=f"{MDIR_REST}/anadrome_laksefisk/MapServer/3",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution="Miljødirektoratet (Lakseregisteret)",
        metadata_url=_geonorge("2d02bb1b-9759-4a19-8cf8-ee41566c089c"),
        params={"page": 1000},
        note="Geonorge metadata: NLOD 2.0, 'Ved viderebruk skal Miljødirektoratet oppgis som kilde'.",
    ),
    Source(
        id="kyv_fairway_areas",
        title="Hovedled og biled, arealavgrensning",
        owner="Kystverket",
        kind="download",
        url=f"{GEONORGE_DL}/Samferdsel/HovedledBiledArealavgrensning/FGDB/"
            "Samferdsel_0000_Norge_25833_HovedledBiledArealavgrensning_FGDB.zip",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution="Kystverket",
        metadata_url=_geonorge("1e8d7811-87ea-429a-8d97-4ea6bbf6e010"),
        note="Geonorge metadata: NLOD 2.0, no further restrictions.",
    ),
    Source(
        id="kyv_fairway_lines",
        title="Hovedled og biled (centre lines)",
        owner="Kystverket",
        kind="download",
        url=f"{GEONORGE_DL}/Samferdsel/HovedledBiled/FGDB/Samferdsel_0000_Norge_25833_HovedledBiled_FGDB.zip",
        licence=NLOD_2[0], licence_url=NLOD_2[1],
        attribution="Kystverket",
        metadata_url=_geonorge("8ff1538a-a93c-4391-8d6f-3555fc37819c"),
        note="Geonorge metadata: NLOD 2.0, no further restrictions.",
    ),
    Source(
        id="kv_n1000",
        title="N1000 Kartdata",
        owner="Kartverket",
        kind="download",
        url=f"{GEONORGE_DL}/Basisdata/N1000Kartdata/FGDB/Basisdata_0000_Norge_25833_N1000Kartdata_FGDB.zip",
        licence=CC_BY_4[0], licence_url=CC_BY_4[1],
        attribution="© Kartverket",
        metadata_url=_geonorge("aee42bb6-d0e9-4d70-86fe-6ea76c381055"),
        note="Geonorge metadata: CC BY 4.0.",
    ),
    Source(
        id="kv_n250",
        title="N250 Kartdata",
        owner="Kartverket",
        kind="download",
        url=f"{GEONORGE_DL}/Basisdata/N250Kartdata/FGDB/Basisdata_0000_Norge_25833_N250Kartdata_FGDB.zip",
        licence=CC_BY_4[0], licence_url=CC_BY_4[1],
        attribution="© Kartverket",
        metadata_url=_geonorge("442cae64-b447-478d-b384-545bc1d9ab48"),
        note="Geonorge metadata: CC BY 4.0.",
    ),
    Source(
        id="kv_dtm50",
        title="DTM 50 (land elevation, 50 m grid)",
        owner="Kartverket",
        kind="download_list",
        url="http://nedlasting.geonorge.no/geonorge/ATOM-feeds/DTM50_AtomFeedTIFF.xml",
        licence=CC_BY_4[0], licence_url=CC_BY_4[1],
        attribution="© Kartverket",
        metadata_url=_geonorge("e25d0104-0858-4d06-bba8-d154514c11d2"),
        params={"pattern": r"https://[^\"]*_25833_DTM50_TIFF\.zip"},
        note="Geonorge metadata: CC BY 4.0. Used only for the hillshade of the base map.",
    ),
    Source(
        id="kv_bathymetry",
        title="Dybdedata terrengmodeller DTM WCS (bathymetry50m)",
        owner="Kartverket",
        kind="wcs_tiles",
        url=KV_DEPTH_WCS,
        licence=CC_BY_4[0], licence_url=CC_BY_4[1],
        attribution="© Kartverket (Dybdedata)",
        metadata_url=_geonorge("8896c751-f3e0-4e4c-86e2-7579cea1f111"),
        params={"coverage": "bathymetry50m", "crs": "EPSG:25833", "res_m": 250, "tile_m": 250_000},
        note="The WCS record says 'No conditions apply'; Kartverket's depth datasets it serves "
             "(Dybdedata - terrengmodeller, Sjøkart - Dybdedata) are CC BY 4.0, so CC BY 4.0 is "
             "recorded and credited. Not for navigation.",
    ),
    Source(
        id="emodnet_bathymetry",
        title="EMODnet Digital Bathymetry (DTM 2022), WCS coverage emodnet:mean",
        owner="EMODnet Bathymetry Consortium",
        kind="wcs_lonlat_tiles",
        url="https://ows.emodnet-bathymetry.eu/wcs",
        licence=CC_BY_4[0], licence_url=CC_BY_4[1],
        attribution="EMODnet Bathymetry Consortium (2022): EMODnet Digital Bathymetry (DTM 2022), "
                    "https://doi.org/10.12770/ff3aff8a-cff1-44a3-a2c8-1910bf109f85",
        metadata_url="https://sextant.ifremer.fr/geonetwork/srv/api/records/2e03bcd0-89a2-49fe-b0b0-a1bb4474861a",
        # Native EPSG:4326 (server-side reprojection to UTM leaves nodata seams at tile
        # edges); build_grids.py resamples to the EPSG:25833 grid. ~280 m N-S, 160-290 m E-W.
        params={"coverage": "emodnet:mean", "bbox": (-7.5, 57.0, 37.5, 73.5), "tile_deg": (2.5, 1.5),
                "res_deg": (0.005, 0.0025), "format": "image/tiff"},
        note="Fallback where Kartverket's surveyed depth grid has gaps (offshore banks, Skagerrak) and "
             "the sea/land split outside Norway. DTM 2022 metadata: 'Creative Commons Attribution 4.0 "
             "International'. Gaps in its own surveys are filled from GEBCO 2022 (public domain, "
             "attribution) and IBCAO v4.",
    ),
    *(
        Source(
            id=sid,
            title=title,
            owner="Copernicus Marine Service (E.U.)",
            kind="copernicus",
            url=f"https://doi.org/{doi}",
            licence=CMEMS_LICENCE[0], licence_url=CMEMS_LICENCE[1],
            attribution=f"Generated using E.U. Copernicus Marine Service Information; https://doi.org/{doi}",
            metadata_url=f"https://doi.org/{doi}",
            note="Optional layer, fetched by tools/geodata/fetch_copernicus.py with the owner's Copernicus "
                 "Marine login (the pipeline never reads the credentials). Licence to Use the Copernicus "
                 "Marine Service Products § 2.2: free, worldwide, royalty-free, for any purpose including "
                 "value-added and derivative products; § 2.4 requires the attribution text and the DOI.",
        )
        for sid, title, doi in (
            ("cmems_arc_phy", "Arctic Ocean Physics Reanalysis (TOPAZ4), ARCTIC_MULTIYEAR_PHY_002_003",
             "10.48670/moi-00007"),
            ("cmems_arc_bgc", "Arctic Ocean Biogeochemistry Reanalysis (ECOSMO), ARCTIC_MULTIYEAR_BGC_002_005",
             "10.48670/moi-00006"),
            ("cmems_arc_wav", "Arctic Ocean Wave Hindcast (WAM 3 km), ARCTIC_MULTIYEAR_WAV_002_013",
             "10.48670/moi-00008"),
        )
    ),
    Source(
        id="lovdata_salmon_rivers",
        title="Nasjonale laksevassdrag (FOR-2009-06-22-961 vedlegg 2)",
        owner="Nærings- og fiskeridepartementet (Lovdata)",
        kind="derived",
        url="https://lovdata.no/dokument/SF/forskrift/2009-06-22-961",
        licence="Public domain (åndsverkloven § 14)",
        licence_url="https://lovdata.no/lov/2018-06-15-40/§14",
        attribution="Forskrift om beskyttelse av laksebestander (FOR-2009-06-22-961)",
        metadata_url="https://lovdata.no/dokument/SF/forskrift/2009-06-22-961",
        note="Laws and regulations are not protected by copyright (åndsverkloven § 14). The river "
             "names are transcribed into geo_rules.py and matched to Lakseregisteret outlets.",
    ),
    Source(
        id="sim_po_temperature",
        title="Production-area sea temperature climatology (ProductionAreas.cs, S2-02)",
        owner="Lakskongen (derived from Havforskningsinstituttet, Rapport fra havforskningen 2025-69)",
        kind="derived",
        url="src/LakskongenSim/Environment/ProductionAreas.cs",
        licence="Project data",
        licence_url="",
        attribution="Havforskningsinstituttet (normal temperatures per production area)",
        metadata_url="https://www.hi.no/hi/nettrapporter/rapport-fra-havforskningen-2025-69",
        note="Read from the game's own table so the map and the sim use one climatology.",
    ),
)

BY_ID = {s.id: s for s in SOURCES}


def source(source_id: str) -> Source:
    """The registry entry for an id (KeyError for an unknown id)."""
    return BY_ID[source_id]
