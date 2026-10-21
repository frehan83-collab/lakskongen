# Geodata stages 2-3: implementation plan

Status: approved by the merge captain on 2026-10-20, with the rulings in §7. This plan turns the map data from stage 1 (`godot/data/geo/`, `docs/geo/GEODATA.md`) into a game feature. Stage 2 makes the simulation read the geography. Stage 3 adds the three-level map: national, production area (PO), site.

The plan came out of a planning run with three steps. Five read-only explorers mapped the code, three planners each wrote a plan from a different angle (player experience, simulation accuracy, merge safety), and a judge scored the plans and combined them. The winning plan was Plan 3: Risk and merge-safety first. The full working data, including the code maps, is not committed.

## 1. Overview

Stage 2 adds pure, embedded, Lazy readers to the sim:
- Utm33;
- the PO polygons, with the source traffic light and point-in-polygon;
- the 189 candidates, with cleaned place labels;
- a ZLibStream PNG decoder and the suitability grids;
- the published score weights;
- the 1,781 localities.
Each is hash-pinned to manifest.json, and the engine never touches the file system.

The open-sea plot hav_1 is bound per game to the Havbruk til havs candidate nearest the home production area. Its plot id, Hs calibration and lice band stay as they are, and only its display position and depth change. off_1 and off_2 stay coastal, so the lens never shows a 70 km open-ocean area with a 5 m coastal Hs100.

The 156 open-pen candidates become the places a player builds through U2-01. The Sjø step of the build dialog:
- lists the area's 12 candidates by rank and preselects the best free one, so tutorial step 16 is still BuildBtn then ConfirmBtn;
- shows a 'why here' with the score, the weighted components, the binding term, depth, nearest farm and river, ocean values, and an honest note that the score is a siting model.
A build writes an optional SeaSite.CandidateId, saved only when set. The licence gate stays per zone: a candidate rides on the player's farmed zone in its area, or on the area's catalogue zone.

Rivals are a static, seed-independent layout over the real salmonid localities. The six parody majors are weighted by biomass share and home area; the rest of the sites go to D17-clean generated independents. Scenario farms get authored real anchors, the 3D rival farms follow the home area's owners, and 'NordLaks Nord AS' is renamed. None of this enters a traced field, a new RNG stream or GameState.

Stage 3 rebuilds MapPanel as three levels:
- national: a stylised base map tinted by area, PO plates carrying the engine's traffic-light badge, type-coded and clustered locality icons in owner bands, dashed offshore areas, hatched salmon fjords, and candidate stars and hexagons;
- region: the area's base map with the raw-sampled suitability heat, constraint layers, rivals by owner and the 12 candidates;
- site: a card with 'Bygg her', 'Søk vekstlisens' or 'Vis i 3D'.
Breadcrumbs and a level-aware Esc tie the levels together. NorwayCoastline.gd, norway_coastline.json and the orphaned MiniMap.gd are retired.

A captain P0 commit lands first: the 28 geo .import files (data PNGs kept raw), an explicit export include_filter, and the merge scripts. Four tracks then run in parallel worktrees (A sim data, B candidates, C national map, D region and site map), each behind early foundation merges and named interfaces.

Verification is headless throughout: dotnet and pytest, plus UiFlowTests check files that sample the raw images on the CPU, and redraw and parse counters. MIN_CHECKS is set to the exact measured count after every merge. The in-game visual review is the named follow-up GEO-VIS, for when the owner allows windows.

The single golden run at integration re-records once. It should show 27 of 27 games identical, with save drift only from the D17 rename. Existing v11 saves load unchanged: no save version bump, no plot or zone id change, and the per-game plot catalogue is derived from the saved HomeProductionArea.

## 2. Tracks

### Track A: Sim geo core and rivals: embedded readers, PO areas and traffic light, PNG and suitability, hav_1 GeoSitePlotGeometry, rival layout with generated parody names, scenario anchors, D17 rename, geo bridge lane

- Branch: `geo/sim-data`
- Depends on: none
- Golden traces: Games are identical in all 27 runs:
- the golden player never builds, so State.SitePlots stays empty;
- hav_1's position and depth are display-only;
- the readers and the rival layout create no SimRng stream, write no GameState field and run on no tick;
- no traced CompetitorCompany field changes.

Save drift appears only on the renamed display names in new games, because loaded saves keep their stored names. No EventLog Name interpolates a rival name, so the golden 'ev' lines are unchanged.

The check uses --base with no --update; the captain re-records once at integration.

**Files it owns**

- NEW src/LakskongenSim/Environment/Geo/GeoResources.cs, Utm33.cs, GeoJson.cs, GeoProductionAreas.cs, GeoCandidates.cs, PlaceLabels.cs, PngGrid.cs, GeoSuitability.cs, GeoSuitabilityRules.cs, GeoLocalities.cs, GeoSitePlotGeometry.cs (with GeoPlotCatalogs), ScenarioGeoAnchors.cs
- NEW src/LakskongenSim/Finance/RivalLayout.cs and ParodyNames.cs
- NEW src/LakskongenSim/Core/SimulationEngine.Geo.cs (partial: SelectGeoPlotCatalog, GeoBindingOf, rival layout cache, GetZoneAnchors) and src/LakskongenSim/Core/GeoViews.cs
- EDIT src/LakskongenSim/LakskongenSim.csproj (A only): append one ItemGroup Label="geo" after the existing EmbeddedResource ItemGroup (lines 34-50). Use explicit names or extension-specific globs, never *.*, because P0 puts .import files beside the PNGs.
- EDIT src/LakskongenSim/Farm/SitePlotCatalog.cs: an additive overload WithGeometry(ISitePlotGeometry, bool strict). The existing WithGeometry (:133-156) stays byte-identical in behaviour.
- EDIT src/LakskongenSim/Core/SimulationEngine.Scenarios.cs: one added line, SelectGeoPlotCatalog();, after SyncSeaTemperatureReference(); in StartScenario
- EDIT src/LakskongenSim/Core/SimulationEngine.Commands.cs: one added line, SelectGeoPlotCatalog();, directly before RehydrateSitePlotsAfterLoad(); (:745)
- EDIT the D17 rename, name strings only, ids unchanged: src/LakskongenSim/Finance/CompetitorAI.cs:422, src/LakskongenSim/Market/MarketIntelligence.cs:295, src/LakskongenSim/Finance/InvestmentManager.cs:638, and the one phrase in godot/data/encyclopedia_entries.json:631
- EDIT godot/scripts/autoload/GameBridge.Depth.cs: GetCompetitorCompanies (:946-985) ordering, plus the additive key home_po_sites only
- NEW godot/scripts/autoload/GameBridge.Geo.cs + .cs.uid (from a sandboxed headless --import)
- SHARED godot/scripts/bridge/SimProxies.gd and godot/scripts/SimBridge.gd: append one '# ═══ BEGIN sim·geo lane block' … 'END sim·geo lane block' at EOF, after the sea lane END (SimProxies.gd:907, SimBridge.gd:784). Nothing else.
- SHARED src/LakskongenSim.Tests/Core/DeterminismScanner.cs: add one BridgeScope line for GameBridge.Geo.cs. The allowlist stays at 73.
- SHARED tests/test_text_correctness.py BRANDS and tools/geodata/geo_rules.py D17_BRANDS: A adds 'Nordlaks' to both. Track C adds 'NordLaks' later.
- NEW src/LakskongenSim.Tests/Geo/*.cs, src/LakskongenSim.Tests/Finance/RivalLayoutTests.cs, ParodyNamesTests.cs; tests/test_geo_sim_contract.py; godot/tests/GeoBridgeChecks.gd plus exactly one _run() line
- HOT godot/translations/translations.csv: GEO_* and RIVAL_* keys only, in sorted position in the tail after CAPACITY_REASON_SITE_NOT_FOUND (line 1974)
- HOT tools/run_ui_flow_tests.sh: MIN_CHECKS (line 30) set to this branch's measured count, plus a '+N geo/sim-data' history fragment
- docs/BALANCE.md §1ab (A only), docs/geo/GEODATA.md §7.2 status lines, docs/RUN_LOG.md §7 rows (append only), docs/run/evidence/geo_sim_data_golden_diff.md
- Never touched: ProductionAreas.cs (regex-parsed by test_geodata.py:170 and geo_sim.py:25), ExpansionZones.cs, ExpansionViews.cs, site_plots.json, OffshoreCalibration.cs, SimulationEngine.cs, InDayTickSystems.cs, GameState.cs, any file under godot/data/geo

**Tasks**

- A1 foundation, merged early, with no engine wiring and no bridge:
- Embed candidates.json, production_areas.regional.geojson, suitability.json, suitability_rules.json and localities.geojson as LakskongenSim.Geo.<file>, and suitability/po01..po13.png as LakskongenSim.Geo.Suitability.<file>.
- GeoResources.ReadText and ReadBytes throw InvalidOperationException naming the missing LogicalName, following SitePlotCatalog.LoadEmbedded at :160-166.
- A1 Utm33.Forward(lonDeg, latDeg) -> (double X, double Y):
- a double-precision port of tests/test_geodata.py:44-59 (GRS80, k0 0.9996, lon0 15°, false easting 500000, Krüger alpha to n^4);
- the doc comment says it is for display and validation only (platform libm) and never feeds game state.
- A1 GeoProductionAreas.Default, read from the regional GeoJSON:
- records carry Number, Code, Name, OfficialTrafficLight, Tint, Label, Label25833, Bbox25833, AreaKm2 and SeaAreaKm2;
- AreaOf(lon, lat) is an even-odd test with MultiPolygon and hole support: pure arithmetic, safe for the sim;
- AreaAtUtm(x, y) tests rings projected once with Utm33, for the display and the bridge only;
- loading validates 13 ids and that each name equals ProductionAreas.Get(n).Name.
- A1 GeoCandidates.Default:
- one immutable record per site, with x/y as long and components as an ordinal-sorted list;
- OpenPensOf(po) in rank order, Offshore, Find(id);
- validation: unique ids, ranks 1..12 per PO, components in 0..1.
PlaceLabels.Clean: trim a trailing '-' (PO03-03 'Bømlafjord-'), capitalise the first letter (PO03-04 'hammar'), and use an override table for non-place labels (PO03-02 'Haugesund lufthavn' -> 'Haugesund').
- A1 PngGrid.Decode:
- signature and chunk parsing;
- require bit depth 8, interlace 0 and colour type 0 or 2;
- concatenate the IDATs, inflate with ZLibStream, and unfilter filters 0-4;
- anything else throws InvalidDataException naming the field.
GeoSuitability.CellAt(po, x, y): c = floor((x - ox)/500), r = floor((oy - y)/500), origin at the NW corner, row 0 north (test_geodata.py:411-412). R > 0 means legal, G is the reason, B×4 is the depth. Each PO is decoded lazily once per process.
GeoSuitabilityRules: EffectiveWeights, and ComponentCost(c, k) = w_k/Σw·(1 - c_k)·river_factor.
- A1 GeoLocalities.Default:
- 1,781 records with integer Id, Name, Kind, Stage, MtbT, Po (0 when null), integer X and Y, Municipality and HarvestPen, sorted by id;
- views Salmonid and SeaGrowoutProducing (sea salmonid grow-out without harvest pens: 994 sites, 3,722,295 t).
Merge A1 into geo/int as soon as it is green.
- A2 SitePlotCatalog.WithGeometry(geometry, strict): with strict on, it throws InvalidDataException naming any slot or pad id the catalogue does not know.
GeoSitePlotGeometry(homePo) : ISitePlotGeometry binds hav_1 only:
- choose the offshore candidate with the minimal integer squared distance from the home area's Label25833; ties go to higher score, then lower rank, then ordinal id;
- emit OffshoreSlot('hav_1', candidate area, X 0, Z -clamp(5200 × dKm/83, 4600, 9000), Exposure 1f, WaterDepthM depth_m), where dKm uses sqrt, which is IEEE-exact;
- LandPadSlots() is empty and IntakeReachM returns 0f. TerrainGenerator is not chained in.
GeoPlotCatalogs.ForHomeArea(po) is a Lazy strict-joined catalogue for each of 1..13; any other value returns SitePlotCatalog.Default.
- A2 SimulationEngine.Geo.cs:
- SelectGeoPlotCatalog() sets PlotCatalog = GeoPlotCatalogs.ForHomeArea(State?.HomeProductionArea ?? 0);
- call it at the end of StartScenario and before RehydrateSitePlotsAfterLoad, by which point RestoreScenarioFromState has assigned the area for pre-S2-02 saves;
- GeoBindingOf(plotId) returns candidate id, area, stage, stage_factor, po, depth, score, binding, real distance in km, x, y, lon, lat and near.
Nothing is saved, and the catalogue is a pure function of saved state plus embedded data.
- A2 GameBridge.Geo.cs, pull-only, no signals:
- GetGeoProductionAreas();
- GetGeoPlotBinding(plotId);
- GetGeoOffshoreCandidates() returns all 33, with bound_plot_id on the one bound to hav_1;
- GeoAreaAt(double x, double y) via AreaAtUtm.
The facade forwarders go in the sim·geo lane block.
- A3 ScenarioGeoAnchors:
- an authored table for the 16 starting-site entries of the 9 shipped scenarios, keyed by (zone id, PO); fjord_west appears in PO3, PO4 and PO6, and fjord_north in PO9 and PO12;
- each anchor is a producing sea grow-out locality in that PO and is distinct within its scenario;
- forste_sesong's fjord_west/PO3 sits at a Kvinnherad grow-out locality (owner decision OD9);
- a catalogue zone takes the first producing grow-out locality by id in the zone's municipality and the zone's own PO;
- otherwise the anchor is the PO's producing locality with the smallest StableHash.Of("geo_anchor", zoneId). This covers Finnøy and Vikna, which have no localities under those names; Sande, which has none at all; po13_lebesby, whose localities are all PO12; and mod or free-text zones.
GetZoneAnchors() covers State.Zones plus the catalogue zones.
- A3 RivalLayout.Build(roster, excludedLocalityIds), pure, ordinal sorts only, no RNG:
- the pool is SeaGrowoutProducing, the land_salmonid sites and the offshore locality 45196, minus the scenario anchors;
- each major gets round(TotalMABTonnes / 1,250,000 × 994) sea sites, drafted round-robin in roster order, preferring its home areas (Bergen -> 3 and 4, Frøya -> 6, Trondheim -> 6, Stokmarknes -> 9), then by StableHash.Of("rival_layout", companyId, localityId);
- land sites follow the same rule; 45196 goes to offshore_optimists, or to the largest surviving major after a merger;
- the rest go to independents grouped by (PO, municipality), at most 6 sites per group, with ids ind_<po2>_<nnn>.
The engine caches the layout by roster signature (ids plus rounded TotalMABTonnes) and exposes a build counter for tests.
- A3 ParodyNames:
- curated Norwegian coast and trade word lists written as code literals, so test_text_correctness' cs_scan sees them, plus 'AS';
- a deterministic StableHash pick with probing for uniqueness;
- a name is rejected if it matches BRAND_RE or contains any municipality or locality-name token;
- no CamelCase and no TextLint English words.
The owner reviews the word lists in BALANCE §1ab.
- A3 Bridge rival reads, cached on the C# side and pull-only:
- GetGeoSitesSignature();
- GetGeoSites(): packed arrays over all 1,781 localities (ids, x, y, kind byte, po, mtb_t, owner index, where -1 means non-salmonid, an anchor or no owner) plus owners [{id, name, is_major}];
- GetRivalSummary(po): sites by kind, sea grow-out MTB, top 5 owners;
- GetZoneAnchors().
GetCompetitorCompanies uses a stable sort by home_po_sites descending, then roster order, read from the cached layout. It returns the same 6 ids and keys plus home_po_sites.
- A3 D17 rename:
- 'NordLaks Nord AS', the MarketIntelligence producer 'NordLaks' and the InvestmentManager label get owner-approved near-miss parodies; ids stay the same;
- the encyclopedia phrase becomes generic ('et skipsformet havanlegg');
- old saves keep their stored names, with no load-time rewrite;
- add 'Nordlaks' to BRANDS (test_text_correctness.py:375-385) and D17_BRANDS (geo_rules.py:238-246).
Also add GameBridge.Geo.cs to DeterminismScanner.BridgeScope.
- A docs and evidence:
- BALANCE §1ab: the rival layout rule and the word lists for owner review, plus the hav_1 compression constants as [GameBalance];
- GEODATA §7.2 status lines;
- RUN_LOG §7 rows;
- tools/record_golden_traces.sh --base $(git merge-base HEAD geo/int) --report docs/run/evidence/geo_sim_data_golden_diff.md, never with --update on the branch.

**Acceptance criteria (each one checkable headless)**

- Builds and test suites:
- dotnet build LakskongenSim.sln and godot/LakskongenGame.csproj are green (TreatWarningsAsErrors);
- dotnet test LakskongenSim.sln has 0 failures, and these classes pass unedited: SitePlotCatalogTests, FarFieldTests, BuildFlowTests, OffshoreUnitBuildTests, DevelopmentLicenceTests, EmpireSystemsIntegrationTests, EmpireSaveRoundTripTests, PersistenceGapTests, LicenseAuctionTests, FarmTypeMoveTests, SaveLoadMatrixTests, PerfGateTests;
- python3 -m pytest tests/ -q is green.
- record_golden_traces.sh --base reports 27 of 27 games identical on every day. The only save drift is on the renamed name nodes (CompetitorAI snapshot, the legacy CompetitorCompanies DTO, IntelCompetitors producer names, and descriptions that quote them), each explained in the report.
- Every embedded resource's SHA-256 equals its godot/data/geo/manifest.json entry. A missing resource throws with its LogicalName.
- Utm33 matches all 6 manifest projection_check_25833 points within 0.01 m, and every candidate's lon/lat lands within 2 m of its x/y.
- Production areas:
- the 13 names equal ProductionAreas.Names;
- the official lights are PO1, PO12 and PO13 green, PO3 red, the rest yellow;
- AreaOf puts all 156 open-pen candidates in their own PO and all 33 offshore candidates in no PO;
- exactly the 8 enumerated localities (10388, 11063, 13131, 15316, 32517, 34237, 45152, 45153) disagree with their polygon;
- AreaAtUtm equals AreaOf at every candidate.
- Data parity:
- ocean_climatology sim_sea_temp_c equals the C# temperature table exactly, and every sst_c month lies within 2.0 °C;
- each open-pen score recomputes within 0.002, and the binding equals the component with the largest ComponentCost (or 'none' when every cost is 0).
- PngGrid round-trips synthetic grey and RGB images under each of filters 0-4 and refuses 16-bit and interlaced images with errors that name the field. All 13 grids decode to suitability.json's width × height, and every open-pen candidate's cell has R > 0.
- Geo plot catalogue, for every home area 1-13:
- hav_1 is bound by the nearest rule;
- off_1, off_2, every pad and the overflow rows equal SitePlotCatalog.Default;
- hav_1's Hs100M, PlotHsScale, lice band, Exposure and Name equal Default's;
- hav_1's Z lies in [-9000, -4600], with no TerrainGenerator(42) far-field seabed above -40 m within 600 m of it.
WithGeometry(strict: true) throws on an unknown id. Area 0 returns Default.
- For each of the 9 shipped scenarios, StartScenario binds its area's catalogue. Save, load and GetBuildPlots('hav') give the same result. The save's property-name set is unchanged, and an engine with no scenario keeps Default.
- Rival layout:
- identical across two processes and seeds 1, 7 and 13;
- every pool locality has exactly one owner, and 45196 belongs to offshore_optimists;
- each major's sea-site count is within ±1 of its share, every major has at least one site in a home area, and every PO with sites has at least 2 owners;
- 100 calls to GetGeoSites and GetCompetitorCompanies build the layout once;
- the SimRng.CaptureStates key set and the save hash are unchanged after the reads.
- Generated names are unique, none matches BRAND_RE or a municipality or locality token, and none trips the TextLint rules.

The 16 anchors are producing grow-out localities in their PO, distinct within each scenario. All 13 catalogue zones resolve to a locality in their own PO.
- No 'Nordlaks' or 'NordLaks' string literal remains in src/LakskongenSim, godot/scripts/autoload or godot/data. MapPanel.gd REGIONS is excepted until Track C removes it. GetCompetitorCompanies returns the same 6-id set, and _test_fjord_shows_rival_farms_and_wellboats passes unedited.
- No System.IO file API appears under src/LakskongenSim/Environment/Geo or Finance/Rival*, and no tick system references a Geo* or RivalLayout member (a source-scan test). GeoBridgeChecks pass headless, and MIN_CHECKS equals the measured count.

**Tests**

- src/LakskongenSim.Tests/Geo/Utm33Tests.cs and GeoResourceParityTests.cs
- src/LakskongenSim.Tests/Geo/GeoProductionAreasTests.cs: names, lights, point-in-polygon for candidates, the 8 enumerated localities, offshore outside, AreaAtUtm parity, synthetic MultiPolygon and hole shapes
- src/LakskongenSim.Tests/Geo/GeoCandidatesTests.cs: counts (12 per PO; offshore {2:4, 3:2, 6:9, 8:6, 11:6, 12:3, 13:3}), score recomputation, binding rule, PlaceLabels over all 189, errors that name the id and field
- src/LakskongenSim.Tests/Geo/PngGridTests.cs and GeoSuitabilityTests.cs
- src/LakskongenSim.Tests/Geo/GeoClimateParityTests.cs: the sim table against the climatology
- src/LakskongenSim.Tests/Geo/GeoSitePlotGeometryTests.cs and GeoPlotEngineTests.cs: the binding for each PO 1-13, only hav_1 moving, strict join, start and load identity, Default fallback
- src/LakskongenSim.Tests/Geo/ScenarioGeoAnchorsTests.cs, Finance/RivalLayoutTests.cs, Finance/ParodyNamesTests.cs (BRANDS read from tests/test_text_correctness.py the way SitePlotCatalogTests.DataFileText reads data), Integration/GeoReadPurityTests.cs (no stream, no save change, build-once counter)
- src/LakskongenSim.Tests/Geo/GeoNoFileSystemTests.cs: source scans for file IO and for tick-system references
- tests/test_geo_sim_contract.py:
- GameBridge.Geo.cs signatures and keys;
- exactly one sim·geo lane block per facade file, with the forwarders;
- the csproj embeds every file the readers name, with no *.* glob under data/geo;
- no NordLaks literal outside MapPanel.gd
- godot/tests/GeoBridgeChecks.gd (about 12 checks): 13 areas and their lights; hav_1 bound to an HTH id; 33 offshore candidates with exactly one bound; the PO3 rival summary has at least 2 owners with names that pass TextLint; zone anchors for the scenario; reads leave qa_state_digest unchanged
- DeterminismScanner.BridgeScope gains GameBridge.Geo.cs (stronger scan; allowlist stays 73). test_text_correctness BRANDS gains 'Nordlaks' (stronger D17 grep).

### Track B: Open-pen candidates in the U2-01/F2-31 flow with 'why here', CandidateId on built sites, player site locations, Sjø candidate picker, hav_1 provenance in the Hav lens

- Branch: `geo/sea-candidates`
- Depends on: A
- Golden traces: None. State changes only after a player build at a candidate, and the golden player never builds. CandidateId is null in every golden game and WhenWritingNull omits it, so there is no save drift and no stream is added. Expected: 27 of 27 identical.

**Files it owns**

- EDIT src/LakskongenSim/Farm/SeaSite.cs: one property after PlotId (:38): [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CandidateId { get; set; }. The house precedent is the property-level WhenWritingDefault at SeaLiceModel.cs:122-134.
- NEW src/LakskongenSim/Core/SimulationEngine.SeaCandidates.cs (partial) and src/LakskongenSim/Core/SeaCandidateViews.cs (SeaCandidateView, SeaCandidateBuildPlan, PlayerSiteLocation, static class SeaCandidateReasons). Never ExpansionReasons: test_expansion_contract.py:37 pins exactly 11.
- NEW godot/scripts/autoload/GameBridge.SeaCandidates.cs + .cs.uid. GameBridge.Expansion.cs is untouched, because its signatures are pinned by test_expansion_contract.py.
- SHARED SimProxies.gd and SimBridge.gd: append one 'sim·candidates lane block' directly after A's sim·geo END marker
- EDIT godot/scripts/bridge/SimActions.gd (B only): one func build_expansion_site_at, directly after build_expansion_site (:411)
- EDIT godot/scripts/ui/expansion/BuildSiteDialog.gd (B only, ≤ ~90 changed lines). NEW godot/scripts/ui/expansion/SeaCandidateList.gd and CandidateWhyHere.gd (+ .uid)
- EDIT godot/scripts/ui/facilities/PlotLens.gd _fill_facts (:123), additive rows only
- SHARED DeterminismScanner.cs BridgeScope: one line for GameBridge.SeaCandidates.cs, after A's
- NEW src/LakskongenSim.Tests/Geo/SeaCandidateTests.cs, tests/test_sea_candidates_contract.py, godot/tests/GeoExpansionChecks.gd plus one _run() line
- HOT translations.csv: SEA_CANDIDATE_* and GEO_PLOT_* keys only (sorted tail). No BUILD_* keys, so the sorted F2-31 block in test_build_flow.py stays untouched.
- HOT MIN_CHECKS (measured, '+N geo/sea-candidates'); RUN_LOG §7 rows; GEODATA §7.2 open-pen status line

**Tasks**

- Rebase onto A1 before writing engine code. Until then, build the UI scripts and the view records against stubs.
- CandidateId: grep every SeaSite copy, clone and digest path (snapshot, qa_state_digest, distressed-asset copies) and carry the field through each.
- SeaCandidateReasons, checked in this order:
1. engine_not_ready;
2. candidate_not_found (unknown id or not open_pen);
3. candidate_wrong_area (candidate.Po != ProductionAreaOfZone(zoneId));
4. candidate_taken (a player SeaSite holds it);
5. candidate_not_suitable (GeoSuitability cell not legal; an integrity guard).
- GetSeaCandidates(po), pure and in rank order. Each view carries:
- the GeoCandidate fields, Label (PlaceLabels.Clean), Weights and ComponentCosts;
- TakenBySiteId and Suggested (the first free candidate by rank);
- BuildZoneId: the player's game zone in that PO with the most fjord sites, ties by ordinal id, else ExpansionZones.OfArea(po).ZoneId;
- the combined verdict: the candidate checks first, then BuildVerdictOf(BuildZoneId).
- Build path:
- PlanSiteBuildAt(zoneId, candidateId) = PlanSiteBuild(zoneId) plus the candidate and its verdict, so the ledger values are identical.
- BuildSeaSiteAt(zoneId, candidateId, name) runs the candidate checks, calls the unchanged BuildSeaSite(zoneId, name) (Licence.cs:145) and then sets CandidateId.
- DisplayName stays SiteNames.NextFor. The candidate label is the site's place, which the bridge and UI show as 'ved <label>'.
- GetPlayerSiteLocations(): one row per fjord site with site_id, label, po, candidate_id, x, y and source. Source is 'candidate' when CandidateId is set, else 'anchor' from A's ScenarioGeoAnchors for the site's zone, which is approximate and labelled as such.
- Bridge GameBridge.SeaCandidates.cs:
- GetSeaCandidates(int productionArea);
- PlanSiteBuildAt(string zoneId, string candidateId);
- BuildExpansionSiteAt(string zoneId, string candidateId), returning BuildExpansionSite's keys plus candidate_id and emitting FarmActionCompleted('build_site', zoneId, true) and GameStateUpdated, as GameBridge.Expansion.cs:156-160 does;
- GetPlayerSiteLocations().
Facade: the proxies and forwarders go in the candidates block, and the verb in SimActions.
- SeaCandidateList.gd: a themed ScrollContainer of toggle buttons named Candidate_<id>. Each row shows rank, label, a score bar, a binding chip and depth. Taken candidates are disabled with a SEA_CANDIDATE_TAKEN_TIP tooltip naming the site. Arrow keys and Enter work.
CandidateWhyHere.gd shows:
- the score as 0-100;
- one bar per weighted component, with its weight, from the rules;
- the river factor;
- the binding sentence (SEA_CANDIDATE_BINDING_<TERM>);
- depth, nearest farm and river, and ocean current, Hs and O2;
- SEA_CANDIDATE_HONEST_NOTE: a siting model on open data; the game's water comes from the area and the zone.
- BuildSiteDialog changes:
- _refresh_sea (:511-513) shows PlotHeader (SEA_CANDIDATE_HEADER) and PlotHost holding the list and the why-here panel; PlotLens stays hidden.
- open_for_zone preselects the bridge's suggested candidate, so tutorial step 16 needs no new route.
- The ledger reads plan_site_build_at.
- _confirm_sea (:794) calls build_expansion_site_at when a candidate is chosen, else the legacy build_expansion_site (no free candidate).
- New open_for_candidate(zone_id, candidate_id), candidate_list() and selected_candidate().
- When opened as open_for_family('sjo') with no zone, the home area's candidates are listed and the BUILD_SEA_PICK_ZONE verdict stays until one is picked. The pick sets the zone to the row's build_zone_id.
- PlotLens._fill_facts: when get_geo_plot_binding(id).bound, add rows for the open-sea area and PO, the stage (GEO_PLOT_STAGE_<STAGE>) and the depth, and replace that plot's distance row with the real distance. Add GEO_PLOT_CLIMATE_NOTE: the sea state is the game's calibrated open-sea climate. off_1, off_2 and the pads are unchanged.
- Translations (en and no, no emoji): SEA_CANDIDATE_* for the header, the 8 component names, the binding terms (8 components plus stage and none), the 5 reasons, the tooltip, the note and the place line; GEO_PLOT_* for area, 4 stages, distance and the note. RUN_LOG §7 rows. GEODATA status line. Golden --base check.

**Acceptance criteria (each one checkable headless)**

- Every existing test passes unedited, including:
- ExpansionFlowTests (19);
- BuildFlowTests, including Assert.Empty(GetBuildPlots(Sjo)) at :378;
- FirstSeasonTutorialTests and FarmTypeMoveTests;
- test_expansion_contract.py and test_build_flow.py;
- UiFlowTests :6927, :7028 and :7162;
- BuildFlowChecks and FirstSeasonFlowChecks.
- record_golden_traces.sh --base: 27 of 27 identical, with no new save drift. A scenario-start save's JSON contains no 'CandidateId' token.
- GetSeaCandidates(po) returns that area's 12 candidates in rank order. Every read leaves the state digest and every SimRng stream state unchanged.
- PlanSiteBuildAt equals PlanSiteBuild on every ledger key. BuildSeaSiteAt charges the same cost, consumes the same licence and admits the same catalogue zone as BuildSeaSite, and it sets CandidateId. Each refusal returns its key in the listed order. A candidate is single-use.
- CandidateId survives save and load. save_v8_facilities.json and a v11 fixture load with CandidateId null.
- By input only in forste_sesong:
- step 16 builds the second site at the preselected PO03-01;
- get_player_site_locations lists it with source 'candidate' at PO03-01's x/y;
- the starting site is listed with source 'anchor';
- reopening the dialog shows PO03-01 disabled with its tooltip.
- «Nytt anlegg» -> Sjø with no zone lists the home area's candidates. Picking one shows its ledger and builds there. A keyboard-only path reaches and builds a candidate.
- With the candidate list, the dialog passes DialogLayoutLint, contrast, unthemed and TextLint at 1280x720 at 100 % and 125 %, and ConfirmBtn lies inside the viewport.
- The Hav lens shows hav_1's HTH area, stage and real distance, and off_1 has no geo rows. MIN_CHECKS equals the measured count.

**Tests**

- src/LakskongenSim.Tests/Geo/SeaCandidateTests.cs:
- listing and purity (BuildFlowTests' digest and stream helper);
- the plan equals PlanSiteBuild, and the build equals BuildSeaSite plus CandidateId;
- refusal order;
- BuildZoneId follows the AreaView order;
- save round trip, 'CandidateId' absent from a scenario-start save, old fixtures loading;
- player site locations (candidate and anchor sources).
- tests/test_sea_candidates_contract.py:
- every SeaCandidateReasons key has en and no text;
- the GameBridge.SeaCandidates.cs signatures and keys;
- the candidates block forwarding (the regex style of test_expansion_contract.py:72-86);
- the expansion UI never hard-codes a candidate reason and never reads .get('message');
- ExpansionReasons still holds exactly 11.
- godot/tests/GeoExpansionChecks.gd (about 28 checks): the tutorial build at PO03-01, the taken tooltip, the no-zone picker, keyboard path, why-here values equal to the bridge, lints at 2 scales, the Hav lens rows, previews leaving qa_state_digest unchanged
- DeterminismScanner BridgeScope gains GameBridge.SeaCandidates.cs (allowlist stays 73)

### Track C: MapPanel v2 foundation and national level: GDScript geo loader and projection, tinted base map, anchored PO plates with traffic-light badges, owner-banded clustered icons, offshore areas, salmon fjords, candidates, real detail column; retire NorwayCoastline and MiniMap; D17 close-out

- Branch: `geo/map-national`
- Depends on: A, B
- Golden traces: None: UI, tests and the D17 test lists only, with no sim or bridge C# change. verify/golden-traces reports identical.

**Files it owns**

- NEW godot/scripts/ui/map/GeoData.gd (class_name GeoData), GeoProjection.gd (class_name GeoProjection), MapPalette.gd, GeoMapView.gd, GeoSiteLayer.gd, GeoOverlayLayer.gd, GeoClusterer.gd, MapLabelPlacer.gd, MapMarker.gd, MapLegend.gd (+ .gd.uid each from a sandboxed headless --import)
- NEW godot/shaders/map_po_tint.gdshader (+ .uid)
- EDIT godot/scripts/ui/MapPanel.gd and godot/scenes/ui/MapPanel.tscn (C owns them):
- shrink MapPanel.gd below 800 lines by moving chart, marker and temp-bar code into map/*;
- keep every public API and _region_nodes;
- the root stays on lakskongen_chart_theme.tres (test_theme_tokens.py:311-321);
- leave a drill_into(area) stub and one Esc branch marked '# geo-drill hook' for Track D.
- EDIT godot/scripts/ui/SiteOverlayLegend.gd (per-area aggregation fix)
- DELETE godot/scripts/ui/NorwayCoastline.gd(+.uid), godot/scripts/ui/MiniMap.gd(+.uid), the orphaned godot/scripts/ui/MapPanelEnhanced.gd.uid, godot/data/norway_coastline.json. Mark docs/ENHANCED_MAP_SYSTEM.md superseded.
- EDIT godot/tests/ThemeLint.gd: add-only strengthening that models a TextureRect with meta 'lint_background'
- EDIT D17 close-out: add 'NordLaks' to tests/test_text_correctness.py BRANDS and tools/geodata/geo_rules.py D17_BRANDS, and remove 'NordLaks' from godot/tests/TextLint.gd ALLOWED_TOKENS (:26). Allowed only because C deletes REGIONS' key_operators (MapPanel.gd:188, :206).
- NEW godot/tests/GeoMapChecks.gd plus one _run() line; NEW tests/test_geo_map_contract.py
- HOT translations.csv: MAP_GEO_* keys only; MIN_CHECKS (measured, '+N geo/map-national'); RUN_LOG §7 rows; GEODATA §7.1 and the §7.3 national-bullet status lines
- Not touched: theme_builder.gd and the generated .tres themes. The map reads tokens.json 'landscape' through theme_tokens.gd read_tokens (:84).

**Tasks**

- C1 foundation, merged early. GeoData.gd is a static lazy cache:
- read_json(rel) uses FileAccess plus JSON.parse_string and casts ids with int(); on failure it records last_error and returns {} without push_error spam;
- basemap(key) and basemap_texture(key) (imported JPG);
- load_data_png(rel) uses FileAccess.get_file_as_bytes plus Image.load_png_from_buffer, never load() on a data PNG;
- production_areas(lod), offshore_areas(lod), salmon_fjords() (the salmon_fjord subset of exclusions.national), candidates(), suitability_grid(po), rules();
- a parse_count counter.
Localities are not parsed in GDScript: national icons come from A's GetGeoSites packed arrays.
- C1 GeoProjection.gd:
- utm33(lon, lat) -> PackedFloat64Array, in 64-bit float, with atanh, sinh and cosh written through log and exp so no unverified builtin is needed;
- to_px(x, y, bounds, res) subtracts the origin in 64-bit before narrowing to Vector2.
- C1 checks:
- projection of the 6 manifest points within 0.01 m;
- raw po_index values ⊆ 0..13;
- the raw po03.png lookup equals test_geodata's for PO3's 12 candidates.
Then merge C1.
- National chart, GeoMapView:
- a TextureRect of basemap/national.jpg, STRETCH_KEEP_ASPECT_CENTERED, with drawn_rect();
- map_po_tint.gdshader samples an ImageTexture of the raw po_index (filter_nearest, no mipmaps) and mixes uniform vec4 tints[14] from each area's GeoJSON 'tint' at about 35 % on index > 0, with hovered_id and selected_id uniforms;
- hit-testing reads the CPU po_index Image on sea and falls back to get_geo_area_at (A) on land and inner waters. po_index is an 800 m open-sea mask: the PO1 and PO3 labels and 27 open-pen candidates sample 0.
- PO plates:
- MapMarker Controls keyed po01..po13 in _region_nodes;
- the dot sits at label_25833 mapped into drawn_rect();
- each plate is a themed PanelContainer that keeps a child Label named 'NameLabel', so region_label_rects() still computes ≥ 26 rects;
- the engine's area traffic light (get_expansion_overview) is a badge on the plate;
- MapLabelPlacer is deterministic: 8 candidate offsets, first non-colliding wins, with a leader line;
- plates re-lay out on 'resized', which also fixes the +30/-50 px drift at MapPanel.gd:786;
- a click anywhere on a plate selects the area and marks map_area_home through the existing path;
- 8 curated city labels avoid the plates, and city_label_rects() keeps its meaning.
- GeoSiteLayer, drawn in one _draw and re-recorded only on resize, level change or layer toggle:
- localities from get_geo_sites() by kind: sea ring, orange land tank, buoy-yellow offshore 12-gon, small grey dots for other species;
- owner band tint: CompetitorFarmVisualManager.band_color_for(owner_id) for majors, slate for independents;
- GeoClusterer screen-grid clusters at national scale, with a count badge in the dominant kind and the majors' share;
- player sites from get_player_site_locations in salmon with a ring; anchored ones are marked approximate in the tooltip;
- a redraw_count counter;
- hover is drawn in a separate small CanvasItem.
- GeoOverlayLayer:
- offshore_areas.national in buoy yellow, dashed by stage (4 stages);
- the salmon fjords as a soft red hatch: Geometry2D.triangulate_polygon plus a procedural repeating hatch texture, with an outline fallback when triangulation is empty, so no engine error is logged;
- candidates as salmon stars (clustered per PO) and yellow hexagons, with hav_1's bound candidate highlighted (get_geo_offshore_candidates).
Layer chips (Anlegg, Kandidater, Havområder, Laksefjorder) have tooltips and push no warnings. MapLegend shows the icon language. _get_tooltip(at) answers for areas, sites and candidates.
- Detail column:
- replace REGIONS' invented stats and emoji (MapPanel.gd:832-872) with the engine light;
- an 'Offisielt trafikklys' chip from get_geo_production_areas;
- sea area;
- localities by kind and summed sea grow-out MTB (get_rival_summary);
- the top 3 rival owners.
Keep RegionExpansionSection, the area picker and «Nytt anlegg» as they are. Delete REGIONS.
- Fix _apply_overlay_to_markers (:390-405): aggregate SiteOverlayLegend intensity per production area through the overview's zones[].production_area.
- Retire NorwayCoastline.gd, MiniMap.gd and norway_coastline.json, and remove the orphaned MapPanelEnhanced.gd.uid. D17 close-out: 'NordLaks' is added to BRANDS and D17_BRANDS and removed from TextLint ALLOWED_TOKENS.
ThemeLint strengthening: a TextureRect with meta 'lint_background' is modelled as that colour. The map inset sets it to the token deep_water.
- Performance:
- national data loads on the first map open, once per process;
- on day_advanced and state_updated, only the tint, badge and player-site uniforms update, only while visible, and only when the overview diff (MapPanel.gd:575) or get_geo_sites_signature() changed;
- the layers never re-record on a tick.

**Acceptance criteria (each one checkable headless)**

- Every existing map-dependent check passes unedited:
- _test_map_labels_never_collide (≥ 26 and ≥ 3 rects, no collisions);
- the escape test (:641);
- Cmd+2;
- theme and contrast on tab map;
- every button answers;
- the U2-01 tests clicking _region_nodes['po03'] and ['po04'];
- BuildFlowChecks new_farm_button;
- the FirstSeasonFlowChecks input season through the home-plate pointer.
- Plates:
- each of the 13 dots lies within 1 px of its label_25833 mapped into drawn_rect(), at the default size and at 1280x720, and again after a resize;
- no plate overlaps another plate or a city label at either size (a new, stronger check);
- a synthesised click at each label pixel selects that PO (13 checks, using the po_index then polygon fallback).
- Cluster totals by kind equal get_geo_sites' counts (1,070 sea salmonid, 309 land salmonid, 1 offshore, 401 other). All 19 offshore areas and all 30 salmon fjords are drawn, filled or as an outline, with no engine error lines. 156 stars and 33 hexagons are represented, exactly one of them highlighted as hav_1.
- The detail column's official light, sea area, locality counts and top owners equal the bridge values, with no invented stat and no emoji. The lice overlay now tints a farmed area's plate (the bug fix).
- With the map open at 30x for 10 days, redraw_count of the site and overlay layers is unchanged; it changes on a resize. parse_count stays 1 across close and reopen. U2-07's p95 check stays green.
- No reference to NorwayCoastline, norway_coastline.json or MiniMap remains in godot/, tests/ or src/, with godot/data/geo/manifest.json excepted: its 'supersedes' block at :882-883 is hash-pinned data. MapPanel.gd is under 800 lines. test_text_correctness passes with 'NordLaks' in BRANDS.
- sync_tokens --check and test_theme_tokens pass. The shader appears in the GDScript parse scan; its real compile is GitHub's ci.sh shader step, because headless runs do not compile shaders (ci.sh:344-345). MIN_CHECKS equals the measured count.

**Tests**

- godot/tests/GeoMapChecks.gd (about 45 checks):
- projection; raw po_index and suitability decode;
- plate anchoring, overlap and resize at 2 sizes;
- click and hit-test for 13 areas;
- layer totals; triangulation fallback; the hav_1 highlight;
- detail values against the bridge;
- overlay fix;
- the 30x redraw counter and parse_count;
- layer chips answer;
- the ThemeLint lint_background case, with a planted low-contrast label flagged;
- _lint_screen('map national').
- tests/test_geo_map_contract.py:
- no NorwayCoastline, norway_coastline or MiniMap references (manifest.json excepted);
- GeoData reads data PNGs only through get_file_as_bytes and load_png_from_buffer;
- MapPanel.gd under 800 lines;
- MAP_GEO_* keys exist in en and no;
- no emoji under godot/scripts/ui/map;
- the public API names still exist (region_marker, home_area, new_farm_button, region_label_rects, city_label_rects, select_area, open_build, open_auction, open_build_family, expansion_section, build_dialog, auction_card).
- test_text_correctness BRANDS gains 'NordLaks', and TextLint loses its allowance: both stronger D17 guards.

### Track D: MapPanel v2 region and site levels: PO base map with raw suitability heat, regional constraint overlays, rivals by owner, candidate states, 2D site card with Bygg her / Søk vekstlisens / Vis i 3D, offshore hexagon routing, breadcrumbs and level-aware Esc

- Branch: `geo/map-region`
- Depends on: C, B, A
- Golden traces: None: UI only. verify/golden-traces reports identical.

**Files it owns**

- NEW godot/scripts/ui/map/GeoRegionData.gd, GeoRegionMath.gd, GeoRegionView.gd, GeoRegionOverlay.gd, GeoSiteCard.gd, MapBreadcrumbs.gd, RivalPresenceList.gd (+ .uid)
- NEW godot/shaders/map_suitability.gdshader (+ .uid)
- EDIT godot/scripts/ui/MapPanel.gd: only the body of C's drill_into stub, the '# geo-drill hook' Esc branch, and functions appended at the end (open_build_at, map_level). Anything else goes through the captain at merge.
- NEW godot/tests/GeoRegionChecks.gd plus one _run() line; add-only test functions in tests/test_geo_map_contract.py
- HOT translations.csv: MAP_REGION_* and MAP_SITE_* keys only; MIN_CHECKS (measured, '+N geo/map-region'); RUN_LOG §7 rows; GEODATA §7.3 regional and local status lines

**Tasks**

- Rebase onto C1 before writing loaders. Until then, build GeoRegionMath, the shader and the card layout.
GeoRegionData.gd loads on the first drill into an area and caches:
- locality_areas, mooring_areas, fairways.regional (with buffer_m), exclusions.regional and constraint_points (radius and hardness per kind from meta.kinds), all filtered by the area's bbox_25833;
- projected once into the area base map's pixels (basemap.json poNN bounds and res);
- pre-triangulated, with an outline fallback.
- GeoRegionMath.suit_uv(po, map_px) maps an area base-map pixel to suitability-grid UV, using suitability.json origin_25833 (NW corner, row 0 north) and res_m 500 against basemap.json bounds. The shader's suit_scale and suit_offset uniforms take the same numbers.
- GeoRegionView:
- basemap/poNN.jpg;
- the heat: a raw suitability/poNN.png ImageTexture (nearest, no mipmaps); R gives a cool-to-gold ramp at (R-1)/254; G codes 1-12 give a muted hatch, and code 13 (outside the planning area) is quieter still;
- the overlays;
- the area's localities in owner bands, from get_geo_sites filtered by po;
- player sites from get_player_site_locations;
- the 12 candidate stars in states free, suggested, taken or yours (get_sea_candidates);
- HTH hexagons in this area;
- a legend and layer chips with tooltips;
- a hover readout of score, reason (MAP_REGION_REASON_<code>) and depth from the CPU grid.
- Detail column at region level:
- RivalPresenceList: the top 5 owners with sites and MTB, from get_rival_summary(po);
- 12 focusable MapCandidate_<id> buttons in rank order with score bars: arrow keys cycle, Enter opens the card;
- every disabled control has a tooltip.
- GeoSiteCard:
- a 4x crop with the locality and mooring polygons, plus 2.5 km farm-distance and 5 km river rings;
- a candidate card shows the why-here values from get_sea_candidates. Its action is 'Bygg her' (open_build_at(build_zone_id, id) -> BuildSiteDialog.open_for_candidate) when can_build, or 'Søk vekstlisens' (open_auction(build_zone_id)) when the reason is growth_licence_required. In that case the subtitle says which zone's licence it is (MAP_SITE_LICENCE_ZONE).
- a player site card offers 'Vis i 3D', through the existing _view_site (MapPanel.gd:545-554);
- a rival locality card shows owner, kind, stage and MTB, with no action;
- the hav_1 hexagon offers 'Bygg her (Hav)' (open_build_family('hav') then build_dialog().choose_plot('hav_1')); other hexagons say they are not offered from your area.
- Navigation:
- drill_into(area) via a double-click, Enter on a focused plate or 'Utforsk området';
- the level stack is national, region, site;
- MapBreadcrumbs 'Norge › POn navn › sted', plus a back button;
- Esc steps up one level and is consumed only when the level is not national;
- a 160 ms modulate crossfade is cosmetic only, the level state is set synchronously, and the national view stays alive underneath.
- Performance: a repeat drill into the same area reads from cache. On day signals, while the region view is visible, re-read only the candidate states and player sites, and only when the overview diff or the signature changed.

**Acceptance criteria (each one checkable headless)**

- The existing escape test passes unchanged. New: drill into PO3, then Esc returns to national, then Esc returns to the dashboard. A breadcrumb click returns to national.
- For all 156 open-pen candidates, a CPU sample of the raw grid at GeoRegionMath.suit_uv(po, candidate px) is legal (R > 0) and equals test_geodata's lookup. This pins the shader transform without a GPU.
- The overlay counts drawn in PO3 and PO6 equal GeoRegionData's filtered counts, with no engine error lines and the outline fallback when triangulation is empty.
- In PO3 in po3_red_sone, the drawn rival icons equal get_geo_sites' owned localities with po 3, the major bands equal band_color_for(owner_id), and RivalPresenceList's order equals get_rival_summary(3).
- Every star and its mirror button open a card whose score, binding and components equal get_sea_candidates.
- 'Bygg her' opens the build dialog with that candidate preselected, and confirming builds there.
- Back at region level the candidate shows as 'yours', and a player icon sits at its x/y.
- In a PO3 game, a PO4 candidate offers 'Søk vekstlisens', which opens auction_card() for po04_solund.
- 'Vis i 3D' on a player site shows the fjord tab with that site selected. The hav_1 hexagon opens the Hav family with hav_1 chosen.
- Cache counters: a repeat drill does not increase parse_count, and with the map hidden no geo bridge call happens on day signals. Timings are printed as evidence, not gated.
- The region view and the site card pass DialogLayoutLint, contrast, unthemed and TextLint at 1280x720 at 100 % and 125 %. Every button answers without warnings. MIN_CHECKS equals the measured count.

**Tests**

- godot/tests/GeoRegionChecks.gd (about 38 checks):
- level navigation and Esc;
- UV pin over 156 candidates;
- overlay counts and the fallback;
- rival bands and presence order;
- star, button and card values;
- Bygg her -> build -> yours;
- Søk vekstlisens routing;
- Vis i 3D;
- hexagon routing;
- cache counters;
- lints at 2 scales;
- the keyboard path.
- tests/test_geo_map_contract.py (add-only functions): MAP_REGION_* and MAP_SITE_* keys exist in en and no; map_suitability.gdshader samples with filter_nearest; GeoRegionData reads data PNGs raw only

## 3. Interfaces

### GeoResources / Utm33 (track A)

```
internal static string GeoResources.ReadText(string logicalName); internal static byte[] GeoResources.ReadBytes(string logicalName) // throw InvalidOperationException naming the resource; public static (double X, double Y) LakskongenSim.Environment.Geo.Utm33.Forward(double lonDeg, double latDeg) // display and validation only, never game state
```

Used by: A readers, B (via A's readers)

### GeoProductionAreas (track A)

```
public sealed class GeoProductionAreas { static GeoProductionAreas Default; GeoProductionArea Get(int po); int AreaOf(double lonDeg, double latDeg) /* 0 = none, pure arithmetic */; int AreaAtUtm(double x, double y) /* display/bridge only */ } public sealed record GeoProductionArea(int Number, string Code, string Name, string OfficialTrafficLight, string Tint, (double Lon, double Lat) Label, (long X, long Y) Label25833, long[] Bbox25833, double AreaKm2, double SeaAreaKm2)
```

Used by: A GeoSitePlotGeometry and the bridge, C and D through the bridge

### GeoCandidates / PlaceLabels (track A)

```
public sealed class GeoCandidates { static GeoCandidates Default; IReadOnlyList<GeoCandidate> OpenPensOf(int po); IReadOnlyList<GeoCandidate> Offshore { get; } GeoCandidate? Find(string id); } public sealed record GeoCandidate(string Id, string Type, int Po, int Rank, double Lon, double Lat, long X, long Y, double Score, IReadOnlyList<KeyValuePair<string,double>> Components, string Binding, double RiverFactor, double DepthM, double Openness, double NearestFarmKm, double NearestRiverKm, string Near, string Label, GeoOcean? Ocean, string? Area, string? Stage, double? StageFactor); public static string PlaceLabels.Clean(string near)
```

Used by: B SimulationEngine.SeaCandidates.cs, A GeoSitePlotGeometry

### PngGrid / GeoSuitability / GeoSuitabilityRules (track A)

```
public static PngGrid PngGrid.Decode(byte[] png); public SuitabilityCell GeoSuitability.Default.CellAt(int po, long x25833, long y25833); public sealed record SuitabilityCell(bool InGrid, byte ScoreByte, byte Reason, double DepthM, bool Legal); IReadOnlyDictionary<string,double> GeoSuitabilityRules.Default.EffectiveWeights; double GeoSuitabilityRules.Default.ComponentCost(GeoCandidate c, string component)
```

Used by: B candidate_not_suitable guard, weights and costs in the views

### GeoLocalities (track A)

```
public sealed class GeoLocalities { static GeoLocalities Default; IReadOnlyList<GeoLocality> All, Salmonid, SeaGrowoutProducing; bool TryGet(int id, out GeoLocality l); } public sealed record GeoLocality(int Id, string Name, string Kind, string Stage, double? MtbT, int Po /*0 = none*/, int X, int Y, string Municipality, bool HarvestPen)
```

Used by: A RivalLayout and ScenarioGeoAnchors

### Geo plot catalogue (track A)

```
public SitePlotCatalog SitePlotCatalog.WithGeometry(ISitePlotGeometry geometry, bool strict); public sealed class GeoSitePlotGeometry : ISitePlotGeometry { public GeoSitePlotGeometry(int homeProductionArea); } public static SitePlotCatalog GeoPlotCatalogs.ForHomeArea(int po); internal void SimulationEngine.SelectGeoPlotCatalog(); public GeoPlotBinding? SimulationEngine.GeoBindingOf(string plotId); public sealed record GeoPlotBinding(string PlotId, string CandidateId, string Area, string Stage, double StageFactor, int Po, double DepthM, double Score, string Binding, double DistanceKm, long X, long Y, double Lon, double Lat, string Near) // plot ids off_1, off_2, hav_1 unchanged; only hav_1 bound
```

Used by: SimulationEngine.PlotCatalog (StartScenario; AdoptSavedState before RehydrateSitePlotsAfterLoad), B PlotLens rows via the bridge, C and D hexagon highlight and routing via the bridge

### Zone anchors and rival layout (track A)

```
public static GeoAnchor ScenarioGeoAnchors.AnchorOf(string zoneId, int productionArea); public sealed record GeoAnchor(int LocalityId, int X, int Y, string Name, string Source /*authored|municipality|fallback*/); public static RivalLayoutResult RivalLayout.Build(IReadOnlyList<CompetitorCompany> roster, IReadOnlySet<int> excludedLocalityIds); public sealed record RivalOwner(string Id, string Name, bool IsMajor); public static string ParodyNames.Of(int po, int index)
```

Used by: B GetPlayerSiteLocations (anchor source), A bridge rival reads

### Geo bridge lane (sim·geo lane block) (track A)

```
GameBridge.Geo.cs:
- GetGeoProductionAreas() -> Array<Dictionary> {id, code, name, official_traffic_light, tint, area_km2, sea_area_km2, label_x, label_y}
- GetGeoPlotBinding(string plotId) -> Dictionary {bound, plot_id, candidate_id, area, stage, stage_factor, po, depth_m, score, binding, distance_km, x, y, lon, lat, near}
- GetGeoOffshoreCandidates() -> Array<Dictionary> (+ bound_plot_id)
- GeoAreaAt(double x, double y) -> int
- GetGeoSitesSignature() -> String
- GetGeoSites() -> Dictionary {signature, ids, x, y: PackedInt32Array; kind, po: PackedByteArray; mtb_t: PackedFloat32Array; owner: PackedInt32Array (-1 none); owners: Array[{id, name, is_major}]}
- GetRivalSummary(int po) -> Dictionary {sites_by_kind, sea_growout_mtb_t, top_owners: Array[{id, name, sites, mtb_t}]}
- GetZoneAnchors() -> Array<Dictionary> {zone_id, production_area, locality_id, name, x, y, source}
SimBridge.gd: get_geo_production_areas(), get_geo_plot_binding(plot_id), get_geo_offshore_candidates(), get_geo_area_at(x, y), get_geo_sites_signature(), get_geo_sites(), get_rival_summary(po), get_zone_anchors()
```

Used by: B PlotLens, C national layers, detail column, hit-test fallback, D region rivals, hexagons, presence list

### GetCompetitorCompanies ordering (track A)

```
GameBridge.GetCompetitorCompanies() (GameBridge.Depth.cs:946): the same 6 ids and keys plus home_po_sites, stable-ordered by home_po_sites descending, then roster order, read from the cached layout
```

Used by: godot/scripts/visual/CompetitorFarmVisualManager.gd (unchanged code), UiFlowTests _test_fjord_shows_rival_farms_and_wellboats (unchanged)

### Sea candidate engine API (track B)

```
SeaSite.CandidateId : string? [JsonIgnore(Condition = WhenWritingNull)]; public IReadOnlyList<SeaCandidateView> SimulationEngine.GetSeaCandidates(int productionArea); public SeaCandidateBuildPlan PlanSiteBuildAt(string zoneId, string candidateId); public CommandResult BuildSeaSiteAt(string zoneId, string candidateId, string name = ""); public IReadOnlyList<PlayerSiteLocation> GetPlayerSiteLocations(); public static class SeaCandidateReasons { EngineNotReady, CandidateNotFound, CandidateWrongArea, CandidateTaken, CandidateNotSuitable }
```

Used by: B GameBridge.SeaCandidates.cs, C and D through the bridge

### Candidates bridge lane (sim·candidates lane block) (track B)

```
GameBridge.SeaCandidates.cs:
- GetSeaCandidates(int productionArea) -> Array<Dictionary> {id, po, rank, label, near, score, components{}, weights{}, component_costs{}, binding, river_factor, depth_m, openness, nearest_farm_km, nearest_river_km, ocean{} or {}, x, y, lon, lat, taken_by_site_id, suggested, build_zone_id, can_build, build_reason_key, build_label_key}
- PlanSiteBuildAt(string zoneId, string candidateId) -> Dictionary: the PlanSiteBuild keys plus candidate, candidate_reason_key, candidate_label_key
- BuildExpansionSiteAt(string zoneId, string candidateId) -> Dictionary: the BuildExpansionSite keys plus candidate_id
- GetPlayerSiteLocations() -> Array<Dictionary> {site_id, label, po, candidate_id, x, y, source}
SimBridge.gd: get_sea_candidates(area), plan_site_build_at(zone_id, candidate_id), build_expansion_site_at(zone_id, candidate_id) (via SimActions), get_player_site_locations()
```

Used by: B BuildSiteDialog, C player-site layer and candidate states, D region view and site card

### BuildSiteDialog candidate API (track B)

```
func open_for_candidate(zone_id: String, candidate_id: String) -> void; func candidate_list() -> Control; func selected_candidate() -> String // open_for_zone preselects the suggested candidate; signals built, view_site_requested, closed and ConfirmBtn unchanged
```

Used by: D via MapPanel.open_build_at, TutorialTargets (unchanged, via ConfirmBtn), GeoExpansionChecks.gd

### GeoData / GeoProjection / MapPalette (GDScript) (track C)

```
class_name GeoData: static func read_json(rel: String) -> Variant; basemap(key: String) -> Dictionary; basemap_texture(key: String) -> Texture2D; load_data_png(rel: String) -> Image; production_areas(lod: String) -> Array; offshore_areas(lod: String) -> Array; salmon_fjords() -> Array; candidates() -> Array; suitability_grid(po: int) -> Dictionary; rules() -> Dictionary; static var parse_count: int. class_name GeoProjection: static func utm33(lon: float, lat: float) -> PackedFloat64Array; static func to_px(x: float, y: float, bounds_25833: Array, res_m: float) -> Vector2. MapPalette: static func landscape(key: String) -> Color
```

Used by: D GeoRegionData, GeoRegionView, GeoSiteCard

### MapPanel v2 API (track C)

```
Kept:
- region_marker(area) -> Control, which has a NameLabel child
- home_area(), select_area(area), open_build(zone_id), open_auction(zone_id), open_build_family(family_id)
- new_farm_button(), build_dialog(), auction_card(), expansion_section()
- region_label_rects() -> Array[Rect2] (>= 26), city_label_rects() (>= 3)
- _region_nodes['po01'..'po13'], on_panel_shown() (national, home area selected, never drilled)
- signal region_selected(region_id), now emitted by select_area
New from C:
- func drill_into(area: int) -> bool (a stub until D)
- func chart() -> GeoMapView
- GeoMapView signals area_clicked(area: int), area_drill_requested(area: int), feature_clicked(kind: String, id: String); func drawn_rect() -> Rect2; func utm_to_local(x: float, y: float) -> Vector2
New from D:
- func map_level() -> String ('national'|'region'|'site')
- func open_build_at(zone_id: String, candidate_id: String) -> void
```

Used by: D, godot/scripts/tutorial/TutorialTargets.gd, AttentionInbox.gd, GameEventActions.gd, BuildFlowLink.gd, UiFlowTests.gd, BuildFlowChecks.gd, FirstSeasonFlowChecks.gd

### GeoRegionMath (track D)

```
static func suit_uv(po: int, map_px: Vector2) -> Vector2; static func suit_cell(po: int, x: float, y: float) -> Vector2i // the same numbers as the map_suitability.gdshader uniforms suit_scale and suit_offset
```

Used by: D GeoRegionView and GeoRegionChecks.gd

## 4. Merge plan

**Step 0. Captain P0 on geo/int, before any branch is cut.** Headless only, in a worktree, never the main checkout.
1. Run git fetch --all --prune, compare geo/int and main with origin, and check open PRs (other models may push).
2. Generate the imports: env GODOT_MCP_TOKEN_PATH=$(mktemp -d)/t perl -e 'alarm shift; exec @ARGV' 900 godot-mono --headless --path godot --import.
3. Set importer="keep" on the 14 data PNGs (suitability/po01..13.png, basemap/po_index.national.png). Use the texture import with mipmaps on, lossless, for the 14 JPGs.
4. Re-run --import until git status godot/data/geo is stable, then commit the 28 .import files.
5. Set include_filter on both presets (export_presets.cfg:12 and :281) to "data/geo/*.json,data/geo/*.geojson,data/geo/*.md,data/geo/suitability/*.png,data/geo/basemap/po_index.national.png".
6. Add tests/test_geo_packaging.py. It pins: keep on the 14 data PNGs; JPG imports present; the filter covers every geo file the game reads raw; test_geodata still passes, because shipped_files() ignores *.import (test_geodata.py:262-263).
7. Commit tools/merge/translations_merge.py and tools/merge/gd_decl_merge.py, recreated from the scratchpad's tr_merge.py and w3int/gdmerge.py, each with a smoke pytest.
8. Raise verify/dotnet's timeout from 30 to 45 minutes if the owner agrees (OD15).
9. Add the RUN_LOG §6 wave pre-registration, the §7 backfill row for geo stage 1 (PR #8), and the BALANCE §1ab reservation for A.
10. Gates: pytest, validate_scenes and the UI flow suite (still 1637).

**Step 1. Branches.** Cut geo/sim-data (A), geo/sea-candidates (B), geo/map-national (C) and geo/map-region (D) from geo/int@P0, each in its own .claude/worktrees worktree. Run --import once per worktree before any UI run. Stage explicit paths only; never git add -A, so locally generated .import files are not committed.

**Step 2. Early foundation merges** (--no-ff, as soon as each is green):
- A1: the readers, csproj, Utm33, PngGrid and their tests, with no wiring;
- C1: GeoData, GeoProjection and their checks.
B rebases onto A1 before its engine code, and D onto C1 before its loaders. C and D build against B's and A's interfaces and pick up the real bridges at their rebase.

**Step 3. Full merges in the order A, B, C, D.**
1. Each track rebases on the current geo/int and passes its gates:
   - dotnet build LakskongenSim.sln && dotnet build godot/LakskongenGame.csproj;
   - dotnet test LakskongenSim.sln;
   - python3 -m pytest tests/ -q;
   - bash tools/validate_scenes.sh;
   - bash tools/run_ui_flow_tests.sh with MIN_CHECKS at its measured count;
   - for every track, tools/record_golden_traces.sh --base $(git merge-base HEAD geo/int). A expects 27/27 identical with save drift only from the rename; B, C and D expect identical and no drift.
2. The captain merges --no-ff and re-runs the same gates on geo/int.
3. MIN_CHECKS is set to the measured merged count, which must equal 1637 plus the sum of the deltas. A gap is investigated, never absorbed.
4. Commits use 'run: geo <item> — <outcome>' with evidence in the body and no attribution lines.

**Hot-file rules:**
- translations.csv: new keys only, sorted, in the tail after CAPACITY_REASON_SITE_NOT_FOUND (line 1974). Prefixes: A GEO_* and RIVAL_*; B SEA_CANDIDATE_* and GEO_PLOT_*; C MAP_GEO_*; D MAP_REGION_* and MAP_SITE_*. Never edit an existing row. Merge with translations_merge.py.
- UiFlowTests.gd: exactly one _run() line per track after the FirstSeasonFlowChecks line (:181), in the order GeoBridgeChecks (A), GeoExpansionChecks (B), GeoMapChecks (C), GeoRegionChecks (D). No existing test function is edited in this wave. Merge with gd_decl_merge.py; _run() is merged by hand.
- run_ui_flow_tests.sh: each branch sets MIN_CHECKS and appends one history fragment; the captain writes the merged measured value. Only the captain may raise TIMEOUT_SECS, with a RUN_LOG row.
- SimProxies.gd and SimBridge.gd: A's sim·geo block at EOF, then B's sim·candidates block after A's END. Resolve conflicts by keeping both, A first. SimActions.gd is B only.
- GameBridge partials, one owner each: A owns Geo (new) and Depth (GetCompetitorCompanies only); B owns SeaCandidates (new). Nobody edits Expansion, Offshore or BuildFlow.
- LakskongenSim.csproj: A only.
- DeterminismScanner BridgeScope: A's line, then B's.
- test_text_correctness.py BRANDS and geo_rules.py D17_BRANDS: A adds 'Nordlaks', C adds 'NordLaks' (after REGIONS is gone). TextLint.gd and ThemeLint.gd: C only.
- MapPanel.gd/.tscn: C, with D limited to the marked hooks and appended functions. BuildSiteDialog.gd and PlotLens.gd: B only.
- godot/data/geo/** and export_presets.cfg: P0 only. Any other branch committing a file there is a merge error.
- RUN_LOG.md: append §7 rows only. BALANCE.md: A's §1ab only. GEODATA.md: status lines per track. AGENTS.md and HANDOFF.md: captain only.

**Step 4. Integration close.**
1. Run tools/record_golden_traces.sh --update once on geo/int, so the fixture save digests carry the rename.
2. Run tools/record_golden_traces.sh --base origin/main --report docs/run/evidence/geo_stage23_golden_diff.md. The expected verdict is 27 of 27 games identical on every day, with save drift only on the renamed name nodes, each explained.
3. Commit the fixtures and the report, and add a RUN_LOG §7 row.
4. If any game differs, stop. Under this plan that is a defect.
5. Run dotnet test unfiltered, then the full gates.
6. Update HANDOFF §6/§8 and AGENTS.md.
7. Push geo/int and open a PR to main. ci / Quality gate (its Xvfb shader step is the first real compile of map_po_tint and map_suitability), verify dotnet, python and golden-traces must all be green; Visual QA never runs on these runners. The owner merges.

**Step 5. Deferred follow-up GEO-VIS, queued in RUN_LOG §9.** When the owner allows windows: capture national, region and site at 1280x720 and 1920x1080 at 100 % and 125 %, plus the why-here dialog, the Hav lens and the 3D rival order. The owner approves any update to the 05_map_overlay, 41_expansion_* and 43_first_season_* baselines; never run baseline_update unattended.

**Rollback.** git revert -m 1 in the order D, C, B, A. No save format or id changes: a save holding a CandidateId loads on a reverted build and drops it silently. P0 can stay.

## 5. Risks

- **Golden traces turning red by accident.** Any new eager SimRng stream, GameState field, traced CompetitorCompany field or zone-water read would move games. Every track runs --base before merging, and a scan test pins the CaptureStates key set and keeps Geo* and RivalLayout off tick systems.
- **po_index is a coarse 800 m open-sea mask.** The PO1 and PO3 labels and 27 of the 156 open-pen candidates (all 12 in PO1) sample 0. Hit-testing must fall back to the PO polygons (get_geo_area_at), and no check may require a label or candidate to sit on a po_index pixel.
- **Shaders compile only on GitHub.** map_po_tint and map_suitability are compiled by GitHub's Xvfb shader step, not headless (ci.sh:344-345), so a compile error surfaces late. Keep them minimal and pin their math on the CPU (GeoRegionMath, raw-image sampling).
- **ThemeLint cannot see TextureRect backgrounds or _draw text** (ThemeLint.gd:173-192). Plates are themed PanelContainers, and the add-only lint_background meta covers the inset. Real legibility waits for GEO-VIS.
- **Duplicate .import files.** Every worktree's first --import generates 28 .import files. P0 commits them once at a fixed point, and tracks stage explicit paths only.
- **verify/dotnet is near its 30-minute timeout.** Geo tests must share Lazy statics, decode each PNG once per process and run no multi-day engine loops. OD15 proposes 45 minutes.
- **The UI suite's 720 s hang guard.** Four new check files could push the suite toward it. Each file spawns at most one scenario and should run in about 20 s or less; only the captain may raise TIMEOUT_SECS.
- **Label crowding.** Thirteen long PO names crowd the portrait national map, worst at 1280x720 (1 px is about 3.4 km). The deterministic placer, leader lines and the new plate-overlap checks at two sizes guard this; short names may still be needed after GEO-VIS.
- **D17 rename and pinned saves.** If a pinned save lacks a field the engine rebuilds from code defaults (for example IntelCompetitors), the rename could move FarmTypeMoveTests' whole-save hashes. Track A runs them unedited, and any re-pin needs a stronger replacement and a RUN_LOG §7 row.
- **Licence copy mismatch.** A candidate in an unfarmed area builds through the area's catalogue zone (for example 'Solund' for a site at 'Selje kloster'). The site card and dialog must say which zone's licence they mean, or players will be confused.
- **Weak score spread.** Scores inside an area are close and have no gameplay effect in this wave (OD3). The why-here leads with components, the binding term, distances and ocean values, plus the honest note, so a 0.94 is not read as better than a 0.92 in play.
- **Two traffic lights.** The engine light and the source light can confuse players. They carry distinct labels, and S3-02 owns reconciling them.
- **MapPanel is a hot file.** U3-01 and U3-03 will rework it, and TutorialTargets, AttentionInbox, GameEventActions and BuildFlowLink call into it. Its public API stays stable and the logic lives in map/* Controls.
- **Scenario anchors are authored choices.** forste_sesong's narrative and the owner's preference may differ; OD9 makes the table reviewable and display-only.
- **Geography mismatch in 3D.** The map shows a new site at Hille while the 3D fjord stays the single seed-42 scene, and hav_1's 3D Z must later agree with gfx-farm-1's OF1 placement. The copy stays honest, and real terrain is G2-01 phase 2.
- **Old saves keep 'NordLaks Nord AS'.** OD8 chooses no load-time rewrite, so the D17 residue stays until the player starts a new game.

## 6. Out of scope

- Land-pad geo candidates (flat shore land near deep water from N250 + DTM 50).
- Sim coupling of the geodata:
- candidate components feeding growth, lice, current, waves or oxygen (OD3 follow-up);
- the neighbour-farm lice field;
- rival lice and disease (S3-02, S4-04);
- re-basing CompetitorCompany counters on real sites;
- auction bidders filtered by presence;
- rivals claiming candidates;
- distressed assets on real localities;
- merging the MarketIntelligence and CompetitorAI rosters (beyond the D17 name fix).
- New offshore plot ids per HTH area (hav_2, hav_3), binding off_1 or off_2, any Hs100/HsScale refit, stage-gated development applications.
- Changing the growth licence from per zone to per area, retiring or renaming the 13 catalogue zones, and the source traffic light refusing applications in unfarmed areas (S3-02).
- Seeding the 3D site scene from a candidate's position, depth or ocean values, and real-region terrain (G2-01 phase 2). The site level is a 2D card plus the existing 3D jump.
- Re-running the geodata pipeline or changing any data file under godot/data/geo, including fixing `near` labels at the source; manifest hashes stay byte-identical. P0's .import sidecars are the only additions there.
- Promoting landscape tokens into the theme Palette (theme_builder.gd and regenerated .tres); the map reads tokens.json directly.
- Windowed work: visual QA, capture_screenshots.sh, perf_smoke.sh, local shader compiles, baseline updates and the GEO-VIS review.
- U3-01 docked IA, U3-02 emoji sweep outside the map, a HUD minimap, and reviving EmpirePanel, CompetitorDashboard and MnADashboard or the GetCompetitors stubs.
- Wiring TerrainGenerator as the production pad geometry; the pre-existing DeepIntakeReachM of 0 on every pad stays as it is.

## 7. Owner decisions and the captain's rulings

Under the owner's standing mandate (decide, then log in RUN_LOG §7), the captain took the recommended default on every decision below. The extra conditions are in **bold**. The owner can overrule any ruling. Each is reversible before the PR merges.

- **OD1: Which offshore plots bind to Havbruk til havs candidates?**
  - Ruling: hav_1 only, bound to the HTH candidate nearest the home area's label. off_1 and off_2 stay procedural. Three geo Hav slots (hav_1..hav_3 sharing hav_1's fitted class, as Plan 2 proposes) are a follow-up.
  - Why: The HTH areas are the open-ocean regime, which matches hav_1 (zone_role 'hav', OpenOcean, Hs100 13 m). off_1 and off_2 are coastal (5 and 12 km, Hs100 5.0 and 6.5 m), so binding them would misdescribe the sea (D16). New ids would mean calibration-class work, test replacements and save drift.
- **OD2: Where do hav_1's position and depth come from when the home area has no HTH candidate (PO1, 4, 5, 7, 9, 10)?**
  - Ruling: The nearest HTH candidate by straight-line distance from the home label, shown as 'nearest open-sea area' with its own PO. The 3D Z is -clamp(5200 × km/83, 4600, 9000).
  - Why: Every game shows a real open-sea area. The binding is display-only, so no lice, capacity or temperature rule moves. The compression keeps hav_1 in the open-sea band the far-field tests expect.
- **OD3: Do candidate components change the simulation?**
  - Ruling: Not in this wave: explanation only, with an honest note in the dialog. The first follow-up is Plan 1's bounded nearest-farm lice multiplier (1.10 at 2 km or less, falling to 0.90 at 10 km or more) on candidate sites only. It is golden-neutral but needs calibration 1.1.5 and a BALANCE section.
  - Why: It keeps this wave free of balance changes and calibration-version save drift. Scores inside an area are close (PO3 0.902-0.944), and temperature binds 108 of 189 candidates, so the gameplay effect needs its own tuning pass.
- **OD4: Should real farm density feed lice pressure in every fjord zone (Plan 2's neighbour field: PO3 ×1.28, PO9 ×0.83), and should the rival roster be re-based on real sites?**
  - Ruling: No, not in stages 2-3. Leave both to S3-02 and S4-04, which already plan rival lice. The rival layout stays display-only.
  - Why: Either change moves all 27 traces, needs FarmTypeHooksTests and FarmTypeMoveTests re-pins, and SeasonalLicePressure already stands for 'neighbouring farms and wild salmon', which would be double-counted.
- **OD5: How are production areas coloured on the national map?**
  - Ruling: Fill each area with its GeoJSON 'tint' at about 35 % on sea pixels, as GEODATA §7.3 says. Show the engine's traffic light as a badge on each PO plate. Show the source's light as a separate 'Offisielt trafikklys' chip in the detail column.
  - Why: Neighbouring areas stay distinguishable, the light shown matches the engine and the U2-01 tests (PO4 green), and the official status is visible without contradicting gameplay. S3-02 reconciles the two lights later.
- **OD6: Must a Sjø build require an explicit candidate pick?**
  - Ruling: No. The best free candidate by rank is preselected and the player can change it.
  - Why: Tutorial step 16 (BuildBtn then ConfirmBtn) and FirstSeasonFlowChecks' input-only season keep working with no new TutorialTargets route. The choice stays deterministic, never random.
- **OD7: Is the growth licence scoped per zone or per production area?**
  - Ruling: Keep U2-01's per-zone gate. A farmed or licensed zone opens all of its area's candidates through organic growth. An area the player does not farm needs the catalogue zone's licence, and the site card says which zone's licence it is.
  - Why: This avoids rewriting BuildGateKey, ApplyVerdictOf and ConsumeGrowthLicence and about 60 ExpansionFlowTests pins, and it behaves like the real pooled-per-area MTB. Plan 2's 'one licence per candidate' would make every new candidate cost a 150 MNOK auction.
- **OD8: D17: rename 'NordLaks Nord AS', MarketIntelligence's 'NordLaks', 'Acquire NordLaks Nord AS' and the encyclopedia's "Nordlaks' Havfarm" now?**
  - Ruling: Yes, in Track A, to near-miss parodies the owner picks. Ids stay the same, both spellings are added to BRANDS and D17_BRANDS, and old saves keep their stored names (no load-time rewrite). **The new names must not be near-misses of the real brand.** Use clearly different parody names. They are checked offline against the holder names in the raw registry cache (`~/.cache/lakskongen-geo`, never committed) as well as BRANDS.
  - Why: D17 is a hard constraint, and the new map shows rival owners on hundreds of icons. A load-time rewrite would move FarmTypeMoveTests' whole-save hashes on the facilities save. In new games the rename is save drift only.
- **OD9: Where do scenario starting farms appear on the map?**
  - Ruling: At authored real grow-out localities per (scenario zone, PO); forste_sesong's farm sits in Kvinnherad (Hardangerfjorden). They are display-only, marked 'approximate' in the tooltip, and excluded from the rival layout.
  - Why: Players should find their own farm on the coast. Authored anchors fit the scenario narratives better than a hash pick, and they change no sim state.
- **OD10: Approve the generated parody name vocabulary?**
  - Ruling: The owner reviews the word lists in BALANCE §1ab before Track A merges. Names never use place words, and the generator rejects BRANDS and every municipality and locality name. **The captain reviews the word lists before Track A merges**, so the merge does not wait for the owner. The owner can veto later. Generated names are also checked offline against the raw registry's holder names.
  - Why: Generated '<place> Havbruk'-style names could collide with real holders that the brand list cannot catch.
- **OD11: What does Esc do on the map?**
  - Ruling: It steps up one level: site to region, region to national. At national level it closes the tab. Opening the map shows national with the home area selected, never drilled in.
  - Why: This keeps _test_escape_returns_tabs_to_the_dashboard valid and gives drill-down the expected back behaviour.
- **OD12: What happens to MiniMap.gd?**
  - Ruling: Delete it (roadmap D6). A HUD minimap later can be a compact GeoMapView.
  - Why: No scene or script instantiates it, and it reads the coastline being retired.
- **OD13: Export include_filter: 'data/geo/*' or an explicit list?**
  - Ruling: An explicit list: data/geo/*.json, *.geojson, *.md, suitability/*.png and basemap/po_index.national.png, pinned by test_geo_packaging.py.
  - Why: 'data/geo/*' would also pack the raw JPGs next to their imported textures (about 2.3 MB more), and no CI gate checks the filter today.
- **OD14: Build MapPanel v2 now, or wait for U3-01 (docked IA) and U3-03 (cartography)?**
  - Ruling: Now, as self-contained Controls under godot/scripts/ui/map/ that U3-01 can re-host. This absorbs U3-03's no-collision label acceptance.
  - Why: The owner wants the best map now, and keeping the logic out of MapPanel's shell limits the later work to re-parenting.
- **OD15: Raise verify/dotnet's timeout from 30 to 45 minutes?**
  - Ruling: Yes, in P0. Separately, the new geo C# tests must stay under about 15 s in total.
  - Why: PRs #5 and #6 took 26:14 and 27:21 against the 30-minute limit, so any added test risks a timeout that looks like a failure.
- **OD16: Approve one golden re-record at integration and the deferred visual review?**
  - Ruling: Yes. Run one --update at integration close; the report should show 27 of 27 games identical and save drift only from the rename. The GEO-VIS windowed review waits until windows are allowed, and the owner approves any baseline update.
  - Why: HANDOFF §7 asks for a diff report on any deliberate change, and windowed Godot is not allowed while the owner works.

## 8. Corrections the judge made to the plans

The judge checked these claims against the code and found them wrong. The plan above already takes them into account.

- Plans 1 and 3 cite SeaLiceModel.cs:122 as the precedent for a `[JsonIgnore(Condition = WhenWritingNull)]` field. That attribute uses JsonIgnoreCondition.WhenWritingDefault (SeaLiceModel.cs:122-134), and no WhenWritingNull appears anywhere in src. For a string? the default is null, so either works; the plan names WhenWritingNull and cites the real precedent.
- Plan 1 says the 9 shipped scenarios have '17 starting zones'. godot/data/scenarios.json has 16 starting-site entries (1+1+1+1+1+2+3+3+3) using 13 distinct zone ids: fjord_west appears in PO3, PO4 and PO6, and fjord_north in PO9 and PO12.
- Plan 1 anchors catalogue zones to 'the first producing locality in the zone's municipality'. That puts po13_lebesby, a PO13 zone, in PO12: all Lebesby localities in localities.geojson have po 12 (grow-out 13143, 13337, 13813, 34697; smolt 10665, 13140). No locality is recorded under Finnøy, Vikna or Sande (Stavanger has 18 in PO2 and Nærøysund 33 in PO7). The rule has to filter by the zone's own PO.
- Plan 1's T3 acceptance says every open-pen candidate's po_index pixel, or one within 1 px, equals its PO for 156/156. That is false: 119 match exactly, 10 within 1 px, and 27 sample 0. Those 27 are all 12 PO1 candidates, plus PO02-01, PO02-07, PO03-01, PO03-06, PO04-12, PO09-12, PO10-12, PO12-12 and PO13-01/02/04/05/08/09/11. PO01-01 has no non-zero pixel within 39 px. po_index.national.png is an 800 m open-sea mask.
- Plan 3's Track C acceptance says every PO marker dot lies on a po_index pixel of its own PO. The label_25833 points of PO1 and PO3 sample 0 in po_index.national.png; the other 11 sample their own id.
- Plan 3's test_geo_map_contract would find 'no NorwayCoastline/norway_coastline reference in godot/' false even after retirement. godot/data/geo/manifest.json:882-883 names 'MapPanel.gd REGIONS[].pos/lonlat' and 'godot/data/norway_coastline.json' in its supersedes block, and that file is hash-checked, so it cannot be edited. The test must exclude it.
- Plan 3 says renaming the rival roster changes 'saved snapshots and event text (save drift, ev lines)'. The golden 'ev' field records each EventLog entry's Id or Name (GoldenTraceRecorder.cs:297-313). No EventLog Name interpolates a rival name, and CompetitorAI headlines go only to OnEventTriggered (SimulationEngine.cs:328-335), not State.EventLog. The rename is therefore save drift only, not an ev or game difference.
- Plan 2 gives PO12 a neighbour-lice multiplier of 0.86. Its own method reproduces 0.85 (index 0.81 at d0 10 km, floor 0.25, clamp 0.3-1.6). PO3 1.28, PO4 1.07, PO6 1.21 and PO9 0.83 reproduce exactly.
- Plan 1 says GDScript has no parse of the geo localities to worry about when T4 'can draw from T2's packed arrays' only as a fallback. The 618 KB localities.geojson would be parsed on the main thread in T3 by default. In the final plan, national icons come from the C# GeoLocalities through packed bridge arrays from the start.

## 9. Ideas taken from the other plans

- From Plan 1: the D17 rename happens in this wave. It covers CompetitorAI.cs:422 'NordLaks Nord AS', MarketIntelligence.cs:295 'NordLaks', InvestmentManager.cs:638 (dead code: nothing outside its own file references the class) and encyclopedia_entries.json:631 "Nordlaks' Havfarm". Ids stay the same and old saves keep their stored names. 'Nordlaks' is added to BRANDS now. 'NordLaks' goes into BRANDS, and comes out of TextLint ALLOWED_TOKENS, once MapPanel REGIONS is gone.
- From Plan 1: generated parody names (ParodyNames) built from curated non-place word lists with StableHash. They are rejected if they match BRANDS or any municipality or locality name. This replaces Plan 3's closed literal list and satisfies GEODATA §7.2's 'generated parody company names'.
- From Plan 1: the RivalLayout method. Majors get sea sites by their biomass share (round(TotalMABTonnes / 1.25 Mt × 994)) with home-area affinity. Locality 45196 goes to offshore_optimists. Independents are grouped by (PO, municipality), at most 6 sites per group. The layout is recomputed only when the roster signature changes, and nothing is written to GameState.
- From Plan 1: ScenarioGeoAnchors (display only). The 16 starting-site entries are authored as real grow-out localities keyed by (zone id, PO). Catalogue zones anchor to a locality in the zone's municipality within its own PO, else a deterministic fallback. Player sites are therefore drawn where they are, not only counted on the PO marker.
- From Plan 1: GetCompetitorCompanies orders the 3D rivals by their sites in the home production area. It returns the same 6 ids and keys plus home_po_sites.
- From Plan 1: map rival owner bands reuse CompetitorFarmVisualManager.band_color_for, so the map and the 3D fjord agree. The offshore hexagon for the bound hav_1 candidate routes to open_build_family('hav') and choose_plot('hav_1'); every other hexagon's tooltip says it is not offered from your area.
- From Plan 1: two add-only strengthenings. One checks that no two plates overlap and no plate overlaps a city label, at two MapArea sizes. The other teaches ThemeLint a 'lint_background' meta on TextureRect, so the base-map inset is modelled.
- From Plan 1: the JPG base maps are imported with mipmaps, because the 1866x2186 national map is drawn at about 0.4 scale. The data PNGs are kept raw.
- From Plan 2: the national tint follows GEODATA §7.3. Each production area is filled with its GeoJSON `tint` at 30-40 %, the engine's area traffic light is a badge on each PO plate, and the source light is a separate labelled 'Offisielt trafikklys' chip in the detail column.
- From Plan 2: stronger data tests. Candidate scores recompute from components × effective_weights × river_factor within 0.002 (measured: 0.0008), and the binding equals the largest weighted cost (0 mismatches). ocean_climatology sim_sea_temp_c equals the ProductionAreas.cs table exactly, and sst_c lies within 2.0 °C (measured: 1.54). The 8 localities that disagree with their PO polygon are pinned by id, and all 33 offshore candidates lie outside every PO polygon.
- From Plan 2: a strict SitePlotCatalog.WithGeometry(geometry, strict: true) overload that throws on an unknown id. This ends the silent drop at SitePlotCatalog.cs:139-153 for the geo join; the existing method is unchanged.
- From Plan 2: a Sjø candidate picker when the build dialog opens with no zone (open_for_family('sjo')). It lists the home area's candidates by rank and keeps the BUILD_SEA_PICK_ZONE verdict until a candidate is chosen.
- From Plan 2: lazy-parse checks. With the map closed for 30 days the geo caches stay empty, and a second drill-down into the same production area reads from cache. National icons come from the C# side as packed arrays, so GDScript never parses the 618 KB localities.geojson.
- From Plan 2: Esc steps up one level (site to region to national) and closes the tab only at national level. Opening the map never drills in, so _test_escape_returns_tabs_to_the_dashboard passes unchanged.
