extends Control

## In-game encyclopedia browser for Lakskongen
## Categories: Fish Species, Diseases, Equipment, Regulations, Feed, Markets

signal close_requested

# ── Colors (Norwegian ocean theme) ──────────────────────────────────────────
const BG_DEEP       := Color("#ECEFF4")
const BG_PANEL      := Color("#FFFFFF")
const BG_SIDEBAR    := Color("#F8F9FB")
const BG_ENTRY      := Color("#F8F9FB")
const BG_SELECTED   := Color("#0A4D68")
const BG_HOVER      := Color("#5E81AC")
const BG_SEARCH     := Color("#F8F9FB")
const BORDER_COLOR  := Color("#D8DEE9")
const TEXT_TITLE    := Color("#2E3440")
const TEXT_BODY     := Color("#4C566A")
const TEXT_MUTED    := Color("#7B8794")
const TEXT_ACCENT   := Color("#0A4D68")
const TEXT_RELATED  := Color("#88C0D0")
const CAT_COLORS := {
	"Fish Species": Color("#A3BE8C"),
	"Diseases":     Color("#BF616A"),
	"Equipment":    Color("#88C0D0"),
	"Regulations":  Color("#EBCB8B"),
	"Feed":         Color("#A3BE8C"),
	"Markets":      Color("#7B9BC0"),
}

# ── Encyclopedia data ─────────────────────────────────────────────────────────
const ENTRIES := [
	# ── Fish Species ──────────────────────────────────────────────────────────
	{
		"id": "atlantic_salmon",
		"category": "Fish Species",
		"title": "Atlantic Salmon (Salmo salar)",
		"icon": "🐟",
		"description": [
			"The Atlantic salmon is Norway's most commercially important farmed fish species, forming the backbone of the global aquaculture industry. Native to both sides of the North Atlantic Ocean, it has been farmed in Norwegian fjords since the 1970s.",
			"In the wild, Atlantic salmon are anadromous — born in freshwater rivers, migrating to the ocean to grow, then returning to freshwater to spawn. In aquaculture, the full lifecycle is managed in controlled environments.",
			"Commercially farmed Atlantic salmon reach harvest weight (4–6 kg) in 14–24 months at sea, depending on water temperature, feed quality, and genetics. The distinctive pink flesh colour comes from astaxanthin in the feed.",
			"Norway produces approximately 1.4 million tonnes of Atlantic salmon annually, accounting for roughly 50% of global supply. Key export markets include the EU, USA, Japan, and China.",
		],
		"related": ["smolt_stage", "parr_stage", "salmon_lifecycle", "sea_lice", "feed_types"],
	},
	{
		"id": "salmon_lifecycle",
		"category": "Fish Species",
		"title": "Salmon Lifecycle Overview",
		"icon": "🔄",
		"description": [
			"The Atlantic salmon lifecycle in aquaculture follows distinct biological stages, each requiring specific management conditions. Understanding these stages is fundamental to successful farming.",
			"Stage 1 — Egg (0–3 months): Fertilised eggs incubate in freshwater at 6–10°C. The eyed egg stage (visible eye spots) indicates healthy development. Mortality during incubation is a key quality indicator.",
			"Stage 2 — Alevin (3–5 months): Newly hatched fish with attached yolk sacs. They require no feeding during this stage as they absorb nutrients from the yolk. Alevins remain in gravel beds naturally.",
			"Stage 3 — Fry (5–8 months): First-feeding begins. Fry are intensely active and require high-energy starter feeds. Cannibalism can occur if size grading is not performed regularly.",
			"Stage 4 — Parr (8–18 months): The parr stage is characterised by distinctive vertical 'parr marks' on the flanks. Fish grow rapidly in freshwater RAS or flow-through tanks, reaching 60–100g before smoltification.",
			"Stage 5 — Smolt (18–24 months): Physiological transformation allowing seawater tolerance. Smolts lose parr marks, develop silver colouring, and undergo hormonal changes enabling osmoregulation in saltwater.",
			"Stage 6 — Post-smolt to Harvest (24–36 months): Sea phase growth from ~100g to 4–6 kg. This is the most economically significant phase, representing the core of open-net pen operations.",
		],
		"related": ["smolt_stage", "parr_stage", "atlantic_salmon", "pen_equipment"],
	},
	{
		"id": "smolt_stage",
		"category": "Fish Species",
		"title": "Smoltification",
		"icon": "✨",
		"description": [
			"Smoltification is the complex physiological and behavioural transformation that prepares juvenile salmon for transition from freshwater to seawater life. This process is triggered by a combination of day-length (photoperiod), temperature, and hormonal cues.",
			"During smoltification, the salmon undergoes gill restructuring to enable active salt secretion, skin colour changes from parr-marked brown to silvery, increased cortisol and thyroid hormone levels, and shifts in swimming behaviour toward downstream migration.",
			"In commercial aquaculture, smoltification is often manipulated using artificial lighting programs to produce out-of-season smolts (called 'S0' or 'S1' depending on age). This allows year-round sea stocking.",
			"The 'smolt window' — the period when fish are physiologically ready for sea transfer — is typically 4–8 weeks. Missing this window results in desmoltification, where fish partially revert and suffer higher seawater mortality.",
			"Quality smolts are assessed by seawater challenge tests (SWT), measuring survival after 24-hour seawater exposure. Good-quality smolts show >95% survival.",
		],
		"related": ["salmon_lifecycle", "parr_stage", "atlantic_salmon"],
	},
	{
		"id": "parr_stage",
		"category": "Fish Species",
		"title": "Parr Development",
		"icon": "🐠",
		"description": [
			"The parr stage represents the extended juvenile freshwater phase of Atlantic salmon, typically lasting 1–3 years in wild populations and 8–18 months in aquaculture. Parr are characterised by 8–12 dark vertical 'parr marks' (also called finger marks) along their flanks, which provide camouflage in river environments.",
			"In commercial freshwater facilities, parr are typically held in recirculating aquaculture systems (RAS) or flow-through tanks with carefully controlled water quality parameters: temperature 12–16°C, dissolved oxygen >85% saturation, pH 6.5–7.5, ammonia <0.5 mg/L.",
			"Feed conversion during the parr stage is highly efficient (FCR 0.9–1.1), making it one of the most cost-effective growth phases. High-density RAS systems allow 50–80 kg/m³ biomass density.",
			"Size grading throughout the parr phase is critical to reduce size variation (CV <15% is target) and prevent hierarchical feeding behaviour. Consistent size at smoltification improves seawater performance.",
		],
		"related": ["smolt_stage", "salmon_lifecycle", "atlantic_salmon", "rearing_equipment"],
	},
	# ── Diseases ──────────────────────────────────────────────────────────────
	{
		"id": "isa_virus",
		"category": "Diseases",
		"title": "ISA — Infectious Salmon Anaemia",
		"icon": "🔴",
		"description": [
			"Infectious Salmon Anaemia (ISA) is a serious viral disease caused by an orthomyxovirus (ISAv). It is classified as a notifiable disease by the World Organisation for Animal Health (WOAH) and Norwegian authorities. ISA outbreaks require mandatory reporting and can trigger production zone restrictions.",
			"Clinical signs include pale gills due to severe anaemia, exophthalmia (bulging eyes), ascites (fluid in the abdominal cavity), petechial haemorrhages in internal organs, and dark discolouration of the liver. Mortality rates can reach 20–100% over 4–8 weeks if untreated.",
			"ISAv spreads primarily through water (horizontal transmission), infected fish movements, and contaminated equipment. Sea lice have been proposed as mechanical vectors. The virus is relatively stable in seawater at low temperatures.",
			"There is no approved treatment. Control relies on biosecurity: zone restrictions, mandatory slaughter of infected sites, fallowing (resting) of sites for at least 6 months, and strict equipment disinfection.",
			"Norway experienced a major ISA crisis in 1990–1992, devastating the industry. This led to the development of strict biosecurity regulations that became a global model. An ISA vaccine is commercially available and widely used as prevention.",
		],
		"related": ["sea_lice", "pd_disease", "traffic_light_system", "escape_reporting"],
	},
	{
		"id": "pd_disease",
		"category": "Diseases",
		"title": "PD — Pancreas Disease",
		"icon": "🟠",
		"description": [
			"Pancreas Disease (PD) is caused by Salmonid Alphavirus (SAV), an RNA togavirus. It is one of the most economically costly diseases in Norwegian salmon farming, causing significant mortality and reduced growth. PD is endemic in western Norway and declared an eradicable disease in northern Norway.",
			"Clinical signs develop 4–8 weeks post-infection: reduced feed intake, lethargy, exocrine pancreatic necrosis, cardiomyopathy, and skeletal muscle degeneration causing characteristic 'rolling' and abnormal swimming. Affected fish often swim at the surface.",
			"Three SAV subtypes are found in Norway: SAV2 (most common along the west coast), SAV3 (historically important), and SAV2 variants. Outbreaks peak in spring and autumn corresponding to increased fish movements.",
			"An approved vaccine (Norvax Compact PD, CLYNAV) provides partial protection and significantly reduces mortality. Combined vaccines (PD+ISA+IPNV) are commonly used at seawater transfer.",
			"PD zones in northern Norway (north of 65°N) require strict surveillance. Detected cases must be reported within 24 hours and can result in harvest orders and zone restrictions.",
		],
		"related": ["isa_virus", "ihn_disease", "sea_lice", "traffic_light_system"],
	},
	{
		"id": "ihn_disease",
		"category": "Diseases",
		"title": "IHN — Infectious Haematopoietic Necrosis",
		"icon": "🟡",
		"description": [
			"Infectious Haematopoietic Necrosis (IHN) is caused by IHNv, a rhabdovirus. Though less prevalent in Norway than ISA or PD, IHN is a notifiable disease and must be reported to Mattilsynet (Norwegian Food Safety Authority) immediately upon suspicion.",
			"The disease primarily affects juvenile salmon and trout. Signs include darkened skin, pale gills, exophthalmia, abdominal distension, and haemorrhaging at fin bases. In acute outbreaks, mortality can reach 90% in fry and parr.",
			"IHNv is transmitted horizontally through water and vertically through eggs (though disinfection greatly reduces vertical transmission risk). The virus is more stable at low temperatures (4–10°C), making cold-water winter months higher risk.",
			"No approved treatment exists. Prevention includes egg disinfection with iodophors, strict biosecurity, monitoring of water source fish populations, and vaccination where available. An IHN vaccine (APEX-IHN) was approved for Atlantic salmon in Canada.",
		],
		"related": ["isa_virus", "bkd_disease", "pd_disease"],
	},
	{
		"id": "bkd_disease",
		"category": "Diseases",
		"title": "BKD — Bacterial Kidney Disease",
		"icon": "🟤",
		"description": [
			"Bacterial Kidney Disease (BKD) is a chronic bacterial infection caused by Renibacterium salmoninarum. It is one of the most difficult-to-eradicate fish diseases due to the bacterium's intracellular survival strategy and resistance to common antibiotics.",
			"BKD progresses slowly over months with signs including kidney hypertrophy with white granulomatous lesions, splenomegaly, abdominal distension, skin blisters, and exophthalmia. Chronic low-level mortality is typical rather than acute outbreaks.",
			"The bacterium can survive intracellularly in macrophages, evading the immune system. Vertical transmission through eggs is common, making broodstock health programmes essential. Horizontal transmission occurs through water and faeces.",
			"Erythromycin has been used historically but antibiotic resistance is a concern. In Norway, BKD must be controlled through biosecurity: egg disinfection, broodstock screening, and restriction of fish movements from infected sites.",
		],
		"related": ["isa_virus", "ihn_disease", "regulations_mab"],
	},
	{
		"id": "sea_lice",
		"category": "Diseases",
		"title": "Sea Lice (Lepeophtheirus salmonis)",
		"icon": "🦠",
		"description": [
			"Sea lice are ectoparasitic copepods that are the single most costly biological challenge in Norwegian salmon aquaculture, causing over 500 million NOK in direct treatment costs annually plus significant indirect costs from reduced growth and mortality.",
			"Lepeophtheirus salmonis (salmon louse) is the primary species. Its lifecycle has 10 moult stages: two nauplii (free-swimming), one copepodid (infective), four chalimus (attached), two pre-adult, and adult stages. Total development time is 3–5 weeks at 10°C.",
			"Norwegian regulations set the threshold at 0.5 adult female lice per fish average on a site. Exceeding this threshold triggers mandatory treatment. Sites near national salmon fjords have stricter limits (0.2 females/fish).",
			"Treatment options include: medicinal baths (hydrogen peroxide, azamethiphos, deltamethrin), in-feed medications (emamectin benzoate — Slice), and non-medicinal methods (thermolicer, hydrolicer, laser — Stingray). Resistance to multiple compounds is documented.",
			"The traffic light system (trafikklyssystemet) uses regional sea lice pressure as a key indicator for production growth permits. High lice pressure leads to red classification, restricting expansion and potentially requiring production cuts.",
		],
		"related": ["isa_virus", "traffic_light_system", "pen_equipment", "atlantic_salmon"],
	},
	# ── Equipment ─────────────────────────────────────────────────────────────
	{
		"id": "pen_equipment",
		"category": "Equipment",
		"title": "Sea Pens and Mooring Systems",
		"icon": "⬡",
		"description": [
			"Sea pens are the primary production unit for open-water salmon farming in Norway. Standard commercial pens have circumferences of 90–160 metres (diameter 29–51 m) and net depths of 15–35 metres, holding up to 2,000 tonnes of biomass.",
			"Modern pens consist of a high-density polyethylene (HDPE) floating collar, net cage, anti-predator net, and sinker ring at the bottom. Leading manufacturers include AKVA Group, Aqualine, and Botngaard. Pen prices range from 500,000 to 2,000,000 NOK.",
			"Mooring systems must withstand Norwegian weather conditions including storm swells of 3–5 metres significant wave height. Spread mooring (4–8 anchor points) is standard; single-point mooring is used for exposed sites. Norwegian Standard NS9415 governs structural requirements.",
			"Advanced pen designs for exposed sites include the Polarfarm semi-submersible, Ocean Farm 1 (semisubmersible steel ring), and HDPE deepwater pens that can submerge during storms. The Ocean Farm 1 has a diameter of 110 metres.",
			"Net pen integrity monitoring is required by regulation. Automatic tear detection systems using sensors and cameras are increasingly deployed. Net washing robots (e.g., WashTec) maintain mesh openness, critical for water flow and oxygen supply.",
		],
		"related": ["feeding_systems", "camera_systems", "regulations_mab", "sea_lice"],
	},
	{
		"id": "feeding_systems",
		"category": "Equipment",
		"title": "Automated Feeding Systems",
		"icon": "🎯",
		"description": [
			"Modern salmon farms use sensor-based automated feeding systems to optimise feed conversion ratio (FCR) and minimise uneaten pellet waste, which reduces environmental impact and operating costs.",
			"Pneumatic pellet transport systems deliver feed from 30–100 tonne feed barges to individual pen feeders via compressed air. Distribution centres control feed flow to multiple feeders simultaneously. Leading suppliers: AKVA Group (Fishtalk), Pentair AES (ACE), Observe (Observe Fish).",
			"Underwater camera-based systems (uEye cameras, hydroacoustic sensors) monitor feeding behaviour in real time. When uneaten pellets are detected sinking past a set depth (typically 3–5 m), the feeding rate is reduced. Machine learning models classify pellet detection with >95% accuracy.",
			"Hydroacoustic feeding systems (e.g., Vaki FishEye, BioSonic) use sonar to detect fish density distribution in the pen and feeding activity, enabling 3D biomass estimation and feed adjustment without diver inspections.",
			"Feed barges typically carry 30–80 days of feed supply and contain automated dosing equipment. Modern smart barges include integrated water quality monitoring, camera systems, and real-time connectivity.",
		],
		"related": ["pen_equipment", "feed_types", "feed_fcr", "camera_systems"],
	},
	{
		"id": "camera_systems",
		"category": "Equipment",
		"title": "Underwater Camera Systems",
		"icon": "📷",
		"description": [
			"Underwater camera technology has transformed salmon farm management, enabling continuous non-invasive monitoring of fish behaviour, health, feeding activity, and sea lice burden.",
			"Fixed underwater cameras are deployed at 3–8 metre depths facing feeding zones. Standard systems include wide-angle and zoom cameras with LED illumination. Image resolution has improved to 4K in modern systems, enabling individual fish tracking.",
			"AI-powered lice counting cameras (iFarm by Cermaq, AquaCloud, Stingray Aquaculture) count sea lice in real time as fish swim past camera stations. These systems achieve <10% counting error compared to manual counts, reducing the need for costly diver inspections.",
			"Biomass estimation systems combine camera-based fish length estimation with population models to provide continuous biomass tracking. The VAKI Biomass Daily system and Observe Biomass provide automated daily harvest weight estimates.",
			"Sea surface camera systems (drones, mast cameras) complement underwater cameras, enabling pen integrity checks, surface behaviour monitoring, and predator detection. Automated predator detection algorithms can trigger acoustic deterrents.",
		],
		"related": ["feeding_systems", "pen_equipment", "sea_lice"],
	},
	{
		"id": "rearing_equipment",
		"category": "Equipment",
		"title": "Freshwater RAS Systems",
		"icon": "🏭",
		"description": [
			"Recirculating Aquaculture Systems (RAS) have become the dominant technology for freshwater salmon smolt production, replacing traditional flow-through systems due to better biosecurity, water efficiency, and year-round production capability.",
			"A RAS consists of rearing tanks (typically circular or oval, 500–3,000 m³), mechanical filtration (drum filters, swirl separators), biological filtration (biofilters with nitrification bacteria), UV sterilisation, ozone treatment, CO₂ stripping (degassers), oxygen supplementation, and heat exchangers.",
			"Modern RAS operates at 95–99% water recirculation rate. Water parameters are maintained with high precision: temperature ±0.5°C, dissolved oxygen 90–110% saturation, pH 7.0–7.5, TAN (total ammonia nitrogen) <1.0 mg/L, CO₂ <10 mg/L.",
			"RAS investment costs are significant: 50,000–150,000 NOK per tonne of annual smolt production capacity. Operating costs include electricity (oxygenation, pumping, heating) typically 3–8 NOK per smolt.",
			"Post-smolt RAS (land-based systems growing salmon to 500g–2kg before sea transfer) is a growing segment, offering reduced sea phase duration and improved biosecurity. Companies including Andfjord Salmon and Salmon Evolution operate large post-smolt RAS facilities.",
		],
		"related": ["parr_stage", "smolt_stage", "pen_equipment"],
	},
	# ── Regulations ──────────────────────────────────────────────────────────
	{
		"id": "regulations_mab",
		"category": "Regulations",
		"title": "MAB — Maximum Allowed Biomass",
		"icon": "⚖️",
		"description": [
			"Maximum Allowed Biomass (MAB), known in Norwegian as MTB (Maksimalt Tillatt Biomasse), is the fundamental production capacity unit in Norwegian salmon aquaculture regulation. Each licence grants the holder a specific MAB measured in tonnes of live fish weight.",
			"Since the 2005 regulatory reform, production capacity is denominated in MAB rather than number of fish or pen volume. A standard commercial licence carries 780 tonnes MAB, though this has varied over time and with adjustment rounds.",
			"Farmers may distribute their licensed MAB across multiple sea sites within their licence area, but the total standing biomass across all active sites must never exceed the total licenced MAB. This creates important operational decisions about stocking distribution.",
			"MAB utilisation is a key profitability driver. High utilisation (>85%) indicates efficient use of licensed capacity. Regulatory restrictions (disease zones, environmental traffic light) can force below-capacity operations.",
			"Licence trading: MAB licences are freely tradeable between companies. Licence values have ranged from 30 million to over 200 million NOK per 780-tonne unit, reflecting the scarcity value created by Norway's limited issuance policy.",
		],
		"related": ["traffic_light_system", "escape_reporting", "pen_equipment"],
	},
	{
		"id": "traffic_light_system",
		"category": "Regulations",
		"title": "Traffic Light System (Trafikklyssystemet)",
		"icon": "🚦",
		"description": [
			"Norway's traffic light system (trafikklyssystemet) is a capacity-regulation framework introduced in 2017 that links permitted production growth to environmental impact indicators, primarily sea lice infestation levels on wild salmon.",
			"Norway's coast is divided into 13 production zones (PO1–PO13), running from Agder in the south to Finnmark in the north. Each zone is assessed every other year against environmental indicators.",
			"Three classifications:\n• Green (grønt lys): Sea lice impact on wild salmon <10% mortality. Farmers in the zone may apply to increase MAB by 6%.\n• Yellow (gult lys): Impact 10–30%. No change in production capacity.\n• Red (rødt lys): Impact >30%. Farmers must reduce MAB by 6% within 2 years.",
			"Sea lice impact is modelled using individual-based dispersal models incorporating oceanographic data, farm locations, stocking information, and lice abundance data from mandatory weekly counting protocols.",
			"The system has been controversial. Industry criticism focuses on model uncertainty and zone-level regulation affecting individual farms with good lice control. Environmental organisations argue thresholds are too permissive. Reviews are ongoing.",
		],
		"related": ["sea_lice", "regulations_mab", "escape_reporting"],
	},
	{
		"id": "escape_reporting",
		"category": "Regulations",
		"title": "Escape Reporting and Prevention",
		"icon": "🚨",
		"description": [
			"Fish escape is one of the most serious regulatory and environmental concerns in Norwegian salmon aquaculture. Escaped farmed salmon can interbreed with wild salmon, diluting local genetic adaptations, and compete for food and territory in rivers.",
			"Norwegian regulation (Akvakulturdriftsforskriften §37) requires that any escape — or suspected escape — be reported to Fiskeridirektoratet (Directorate of Fisheries) within 24 hours of discovery. Failure to report is a criminal offence.",
			"After discovery, the farmer must: estimate the number escaped, search downstream areas with nets, report to affected river owners, fund recapture operations in rivers, and submit a root-cause analysis within 1 month.",
			"Annual escape statistics in Norway have declined from hundreds of thousands in the 1990s to typically 100,000–400,000 fish per year in the 2010s, and below 200,000 in recent years, reflecting improved pen standards and operational practices.",
			"Prevention requirements under NS9415: bi-weekly net inspections, ROV or diver inspections every 6 months, automatic net tear detection for new installations, and minimum net mesh sizes of 20 mm for fish >500g. Mortality management systems must prevent dead fish accumulation that can damage nets.",
		],
		"related": ["regulations_mab", "traffic_light_system", "pen_equipment"],
	},
	# ── Feed ──────────────────────────────────────────────────────────────────
	{
		"id": "feed_types",
		"category": "Feed",
		"title": "Salmon Feed Types",
		"icon": "🌾",
		"description": [
			"Commercial salmon feed has evolved significantly from fishmeal-heavy formulations to complex multi-ingredient diets that balance performance, cost, and sustainability. Feed represents 50–60% of total production costs.",
			"Extruded pellets are the universal format for commercial salmon feed. Extrusion at 120–150°C gelatinises starch binders, sterilises the feed, and creates a porous pellet structure that allows oil coating after extrusion. Pellet sizes range from 0.5mm (first feeding) to 12mm (harvest-size fish).",
			"Pellet types by energy content:\n• Standard (low-energy): 19–21 MJ/kg. Used for smolts and periods of restricted feeding.\n• High-energy: 23–26 MJ/kg. Standard for growing salmon; achieved through higher fat inclusion.\n• Extra-high-energy: 26–28 MJ/kg. Used in winter or for rapid catch-up growth.",
			"Functional feeds include specific additives: immuno-stimulants (beta-glucans, nucleotides) for improved disease resistance, probiotics and prebiotics for gut health, carotenoids (astaxanthin, canthaxanthin) for flesh colour (target: 25–35 mg astaxanthin/kg feed), and omega-3 concentrate for final product quality.",
			"Major Norwegian feed producers: Cargill (EWOS), Skretting (Nutreco), BioMar, and Biofeed. The industry produces approximately 1.9 million tonnes of salmon feed annually in Norway.",
		],
		"related": ["feed_ingredients", "feed_fcr", "feeding_systems"],
	},
	{
		"id": "feed_ingredients",
		"category": "Feed",
		"title": "Feed Ingredients and Sustainability",
		"icon": "🌿",
		"description": [
			"The ingredient composition of salmon feed has undergone a fundamental transformation over 30 years. In 1990, feeds contained ~65% fishmeal and ~25% fish oil. By 2023, fishmeal content has dropped to ~12–15% and fish oil to ~10–15%, replaced largely by plant and land-animal ingredients.",
			"Current primary ingredients:\n• Plant proteins (soy, wheat gluten, pea protein, rapeseed): 40–50% of feed by weight. Provides protein and energy but may reduce palatability and affect gut health.\n• Marine proteins (fishmeal, krill meal, single-cell protein): 12–20%. Critical for amino acid balance and palatability.\n• Plant oils (rapeseed, soy): 15–20%. Energy source; lower omega-3 content than fish oil.\n• Fish oil: 8–15%. Primary EPA/DHA omega-3 source; critical for salmon health and product quality.",
			"Emerging sustainable ingredients gaining traction:\n• Insect meal (black soldier fly, Hermetia illucens): high protein, circular economy benefits.\n• Algae oil: direct omega-3 source bypassing forage fish, similar to fish oil profile.\n• Single-cell proteins (Calysta FeedKind, Nofima yeast): fermentation-based proteins with minimal land use.\n• GM soy with omega-3 (Camelina oil): plant-derived DHA/EPA.",
			"The FMFO ratio (fish in:fish out) measures how much wild fish is required to produce farmed salmon. Norwegian industry average has improved from ~5:1 in 1990 to ~1.2:1 today, a major sustainability achievement.",
		],
		"related": ["feed_types", "feed_fcr", "markets_nasdaq"],
	},
	{
		"id": "feed_fcr",
		"category": "Feed",
		"title": "FCR — Feed Conversion Ratio",
		"icon": "📊",
		"description": [
			"Feed Conversion Ratio (FCR) measures feed efficiency: the kilograms of feed required to produce one kilogram of fish weight gain. It is the most important operational efficiency metric in salmon farming.",
			"FCR formula: FCR = Feed consumed (kg) ÷ Biomass increase (kg). An FCR of 1.15 means 1.15 kg of feed produces 1.0 kg of fish growth.",
			"Industry benchmarks:\n• Exceptional: FCR < 1.05 (world-class operation)\n• Good: FCR 1.05–1.15\n• Average: FCR 1.15–1.25\n• Below average: FCR > 1.25",
			"Factors affecting FCR:\n• Water temperature (optimum 12–16°C for Atlantic salmon)\n• Dissolved oxygen levels (suboptimal O₂ increases FCR)\n• Feed quality and pellet stability\n• Feeding system accuracy (uneaten feed wastes investment)\n• Fish health status (diseased fish stop eating)\n• Crowding stress and hierarchical behaviour",
			"Economic impact: Improving FCR by 0.05 across a 780-tonne MAB licence at a feed cost of 12 NOK/kg represents savings of approximately 1.2 million NOK per harvest cycle. FCR improvement is the highest-return operational focus area.",
			"Biological FCR vs. Economic FCR: Biological FCR uses actual weight gain; economic FCR includes mortality (dead fish that consumed feed but add no revenue). Economic FCR is always higher. Distinguishing these two metrics is critical for accurate cost analysis.",
		],
		"related": ["feed_types", "feed_ingredients", "feeding_systems"],
	},
	# ── Markets ────────────────────────────────────────────────────────────────
	{
		"id": "markets_nasdaq",
		"category": "Markets",
		"title": "Nasdaq Salmon Index",
		"icon": "📈",
		"description": [
			"The Nasdaq Salmon Index (formerly the Fish Pool Index, NOS) is the benchmark price reference for Atlantic salmon in the spot market. Published daily by Nasdaq Commodities in Oslo, it reflects the volume-weighted average price per kilogram of fresh whole salmon (superior quality, head-on gutted, 3–6 kg) exported from Norway.",
			"The index has historically ranged from as low as 15 NOK/kg (2003 crisis) to over 100 NOK/kg (2016 peak, COVID-19 disruptions 2020, post-pandemic demand surge 2022–2023). Average prices have trended upward, reflecting increasing global demand outpacing supply growth.",
			"Price drivers:\n• Norwegian supply volume (harvest projections, biomass reports)\n• Global demand (EU consumer trends, export to Asia/USA)\n• Currency rates (NOK vs EUR/USD/JPY)\n• Disease outbreaks and regulatory restrictions\n• Seasonal patterns (Christmas premium in EU markets)\n• Competitor supply (Chile, UK, Canada, Faroe Islands)",
			"Fish Pool derivatives market: futures contracts are available for monthly salmon prices up to 18 months forward. Used by producers to hedge price risk and by buyers to fix costs. Contract size: 1 tonne of salmon.",
			"Price seasonality: Norwegian salmon prices typically show lowest prices in Q3 (July–September, peak Norwegian harvest season), highest prices in Q4/Q1 (Christmas demand, lower production from previous generation). Understanding seasonal patterns is essential for harvest timing decisions.",
		],
		"related": ["markets_contracts", "markets_seasonal", "feed_types"],
	},
	{
		"id": "markets_contracts",
		"category": "Markets",
		"title": "Contract Types and Sales Channels",
		"icon": "📋",
		"description": [
			"Norwegian salmon is sold through several commercial structures, each offering different trade-offs between price certainty and price upside.",
			"Spot sales: Negotiated weekly, typically Tuesday/Wednesday for Thursday/Friday delivery. Prices closely track the Nasdaq Salmon Index. Provides maximum exposure to market price movements — beneficial in rising markets, risky in falling markets. Most Norwegian salmon (60–70%) is sold spot.",
			"Fixed-price contracts: Long-term agreements (3–12 months) at a predetermined price per kg. Buyers include large retailers (Lidl, Carrefour, Walmart), foodservice companies, and processing plants. Provides revenue certainty for budgeting and debt servicing but forgoes upside in strong markets.",
			"Formula contracts: Price is indexed to Nasdaq Salmon Index with a negotiated discount/premium. Common for high-volume, long-term customer relationships. Discount reflects buyer's volume commitment and payment reliability.",
			"Value-added products: Fresh fillets, portion cuts, smoked salmon, and ready meals command premium margins but require processing investment or partnerships. Japanese sashimi market (raw salmon) and premium food service channels offer highest per-kg returns.",
			"Export markets by region: EU (France, Poland, Denmark top importers): ~70% of volume. Asian markets (Japan, China, Vietnam for processing): ~15%. North America (USA): ~8%. Domestic Norwegian: ~7%.",
		],
		"related": ["markets_nasdaq", "markets_seasonal"],
	},
	{
		"id": "markets_seasonal",
		"category": "Markets",
		"title": "Seasonal Market Patterns",
		"icon": "📅",
		"description": [
			"Atlantic salmon markets show consistent seasonal price patterns driven by both supply cycles and demand peaks. Understanding these patterns enables better harvest timing and contract strategies.",
			"Q1 (January–March): Post-Christmas demand pullback typically sees prices soften from December highs. Norwegian biomass is at seasonal lows (fish stocked previous autumn still growing), limiting supply. Prices often recover by March as Asian New Year demand kicks in.",
			"Q2 (April–June): Spring is traditionally a transitional period. Easter provides a short demand spike. Supply increases as spring-harvested fish enter the market. Overall prices moderate.",
			"Q3 (July–September): The summer trough. Norwegian harvest volumes peak as the industry processes large fish stocked 2 years prior. Consumer demand in European markets dips during summer. Price/kg typically 10–20% below annual average.",
			"Q4 (October–December): The Christmas premium. European retailers and food service aggressively source salmon for Christmas menus (peak consumption in France, UK, Germany, Scandinavia). December prices typically 15–30% above annual average. The Christmas market drives strategic harvest decisions throughout the year.",
			"Year-over-year variation: Disease outbreaks, currency fluctuations, and competing protein prices can amplify or dampen seasonal patterns significantly. The 2016 price spike (ISA outbreak in Chile reducing competing supply) and 2020 COVID-19 disruption demonstrate how exogenous factors can override seasonal norms.",
		],
		"related": ["markets_nasdaq", "markets_contracts", "regulations_mab"],
	},
]

# ── State ─────────────────────────────────────────────────────────────────────
var _current_category: String = "Fish Species"
var _current_entry_id: String = ""
var _search_text: String = ""
var _category_buttons: Dictionary = {}
var _entry_buttons: Dictionary = {}

# ── Node refs ─────────────────────────────────────────────────────────────────
var _overlay: ColorRect
var _category_list: VBoxContainer
var _entry_list: VBoxContainer
var _content_scroll: ScrollContainer
var _content_box: VBoxContainer
var _search_field: LineEdit
var _title_label: Label
var _icon_label: Label
var _desc_box: VBoxContainer
var _related_box: HBoxContainer
var _entry_count_label: Label

# ─────────────────────────────────────────────────────────────────────────────
func _ready() -> void:
	_build_ui()
	hide()

# ─────────────────────────────────────────────────────────────────────────────
func show_panel() -> void:
	_search_field.text = ""
	_search_text = ""
	show()
	modulate.a = 0.0
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 1.0, 0.35)
	_select_category("Fish Species")

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func hide_panel() -> void:
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 0.0, 0.25)
	tw.tween_callback(hide)

# ─────────────────────────────────────────────────────────────────────────────
func _build_ui() -> void:
	anchor_right = 1.0
	anchor_bottom = 1.0

	# Dim overlay
	_overlay = ColorRect.new()
	_overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	_overlay.color = Color(0.18, 0.20, 0.25, 0.60)
	add_child(_overlay)

	# Main panel container (centred)
	var panel_anchor := Control.new()
	panel_anchor.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(panel_anchor)

	var outer_margin := MarginContainer.new()
	outer_margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]:
		outer_margin.add_theme_constant_override("margin_" + side, 60)
	panel_anchor.add_child(outer_margin)

	var panel_bg := _make_box(BG_PANEL, 12)
	panel_bg.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	panel_bg.size_flags_vertical = Control.SIZE_EXPAND_FILL
	outer_margin.add_child(panel_bg)

	var panel_margin := MarginContainer.new()
	panel_margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]:
		panel_margin.add_theme_constant_override("margin_" + side, 0)
	panel_bg.add_child(panel_margin)

	var layout := VBoxContainer.new()
	layout.set_anchors_preset(Control.PRESET_FULL_RECT)
	panel_margin.add_child(layout)

	# Header
	layout.add_child(_build_header())

	# Horizontal divider
	layout.add_child(_make_separator())

	# Body: sidebar + content
	var body := HSplitContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.split_offset = 520
	layout.add_child(body)

	# Left: category + entry list
	body.add_child(_build_sidebar())

	# Right: entry content
	body.add_child(_build_content_area())

# ── Header ────────────────────────────────────────────────────────────────────
func _build_header() -> Control:
	var hdr := HBoxContainer.new()
	hdr.custom_minimum_size.y = 64

	var hdr_bg := ColorRect.new()
	hdr_bg.color = Color("#FFFFFF")
	hdr_bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	hdr.add_child(hdr_bg)

	var m := MarginContainer.new()
	m.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for side in ["left", "right"]:
		m.add_theme_constant_override("margin_" + side, 24)
	for side in ["top", "bottom"]:
		m.add_theme_constant_override("margin_" + side, 12)
	hdr.add_child(m)

	var inner := HBoxContainer.new()
	inner.alignment = BoxContainer.ALIGNMENT_CENTER
	m.add_child(inner)

	var title_lbl := Label.new()
	title_lbl.text = tr("UI_ENCYCLOPEDIA_TITLE")
	title_lbl.add_theme_font_size_override("font_size", 22)
	title_lbl.add_theme_color_override("font_color", TEXT_TITLE)
	title_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inner.add_child(title_lbl)

	# Search bar
	_search_field = LineEdit.new()
	_search_field.placeholder_text = "🔍  Search entries…"
	_search_field.custom_minimum_size = Vector2(280, 38)
	_search_field.add_theme_font_size_override("font_size", 15)
	_search_field.add_theme_color_override("font_color", TEXT_BODY)
	_search_field.add_theme_color_override("font_placeholder_color", TEXT_MUTED)
	var search_style := StyleBoxFlat.new()
	search_style.bg_color = BG_SEARCH
	search_style.border_color = BORDER_COLOR
	search_style.border_width_bottom = 2
	search_style.corner_radius_top_left = 6
	search_style.corner_radius_top_right = 6
	search_style.corner_radius_bottom_left = 6
	search_style.corner_radius_bottom_right = 6
	search_style.content_margin_left = 12
	search_style.content_margin_right = 12
	_search_field.add_theme_stylebox_override("normal", search_style)
	_search_field.tooltip_text = "Søk i leksikon etter fisk, sykdommer, utstyr, regelverk og markeder."
	_search_field.text_changed.connect(_on_search_changed)
	inner.add_child(_search_field)

	# Close button
	var spacer := Control.new()
	spacer.custom_minimum_size.x = 20
	inner.add_child(spacer)

	var close_btn := _make_button("✕", 18, Color("#BF616A"))
	close_btn.custom_minimum_size = Vector2(44, 44)
	close_btn.tooltip_text = "Lukk leksikonet (Esc)"
	close_btn.pressed.connect(_on_close_pressed)
	inner.add_child(close_btn)

	return hdr

# ── Sidebar ───────────────────────────────────────────────────────────────────
func _build_sidebar() -> Control:
	var sidebar_bg := _make_box(BG_SIDEBAR, 0)
	sidebar_bg.custom_minimum_size.x = 260

	var vbox := VBoxContainer.new()
	vbox.set_anchors_preset(Control.PRESET_FULL_RECT)
	sidebar_bg.add_child(vbox)

	# Category header
	var cat_header := Label.new()
	cat_header.text = tr("UI_ENCYCLOPEDIA_CATEGORIES_HEADER")
	cat_header.add_theme_font_size_override("font_size", 12)
	cat_header.add_theme_color_override("font_color", TEXT_MUTED)
	cat_header.custom_minimum_size.y = 36
	var cat_hdr_style := StyleBoxFlat.new()
	cat_hdr_style.bg_color = Color("#F8F9FB")
	cat_hdr_style.border_color = BORDER_COLOR
	cat_hdr_style.border_width_bottom = 1
	cat_header.add_theme_stylebox_override("normal", cat_hdr_style)
	vbox.add_child(cat_header)

	# Category buttons
	var cat_scroll := ScrollContainer.new()
	cat_scroll.custom_minimum_size.y = 260
	cat_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	vbox.add_child(cat_scroll)

	var cat_list_wrap := VBoxContainer.new()
	cat_list_wrap.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	cat_scroll.add_child(cat_list_wrap)

	_category_list = VBoxContainer.new()
	_category_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	cat_list_wrap.add_child(_category_list)

	for cat in ["Fish Species", "Diseases", "Equipment", "Regulations", "Feed", "Markets"]:
		var btn := _make_category_button(cat)
		_category_buttons[cat] = btn
		_category_list.add_child(btn)

	# Divider
	vbox.add_child(_make_separator())

	# Entry list header
	var entry_header_row := HBoxContainer.new()
	var entry_header := Label.new()
	entry_header.text = tr("UI_ENCYCLOPEDIA_ENTRIES_HEADER")
	entry_header.add_theme_font_size_override("font_size", 12)
	entry_header.add_theme_color_override("font_color", TEXT_MUTED)
	entry_header.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	entry_header_row.add_child(entry_header)

	_entry_count_label = Label.new()
	_entry_count_label.add_theme_font_size_override("font_size", 11)
	_entry_count_label.add_theme_color_override("font_color", TEXT_MUTED)
	_entry_count_label.custom_minimum_size.x = 40
	entry_header_row.add_child(_entry_count_label)
	vbox.add_child(entry_header_row)

	# Entry list
	var entry_scroll := ScrollContainer.new()
	entry_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	entry_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	vbox.add_child(entry_scroll)

	var entry_list_wrap := VBoxContainer.new()
	entry_list_wrap.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	entry_scroll.add_child(entry_list_wrap)

	_entry_list = VBoxContainer.new()
	_entry_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	entry_list_wrap.add_child(_entry_list)

	return sidebar_bg

# ── Content area ──────────────────────────────────────────────────────────────
func _build_content_area() -> Control:
	var content_bg := _make_box(BG_ENTRY, 0)
	content_bg.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	_content_scroll = ScrollContainer.new()
	_content_scroll.set_anchors_preset(Control.PRESET_FULL_RECT)
	_content_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	content_bg.add_child(_content_scroll)

	_content_box = VBoxContainer.new()
	_content_box.set_anchors_preset(Control.PRESET_TOP_WIDE)
	_content_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_content_scroll.add_child(_content_box)

	var content_margin := MarginContainer.new()
	content_margin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for side in ["left", "right"]:
		content_margin.add_theme_constant_override("margin_" + side, 40)
	for side in ["top", "bottom"]:
		content_margin.add_theme_constant_override("margin_" + side, 32)
	_content_box.add_child(content_margin)

	var inner_vbox := VBoxContainer.new()
	inner_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inner_vbox.add_theme_constant_override("separation", 16)
	content_margin.add_child(inner_vbox)

	# Icon + title row
	var title_row := HBoxContainer.new()
	title_row.add_theme_constant_override("separation", 16)
	inner_vbox.add_child(title_row)

	_icon_label = Label.new()
	_icon_label.add_theme_font_size_override("font_size", 48)
	_icon_label.text = tr("📖")
	title_row.add_child(_icon_label)

	_title_label = Label.new()
	_title_label.add_theme_font_size_override("font_size", 24)
	_title_label.add_theme_color_override("font_color", TEXT_TITLE)
	_title_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_title_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_row.add_child(_title_label)

	inner_vbox.add_child(_make_separator())

	# Description container
	_desc_box = VBoxContainer.new()
	_desc_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_desc_box.add_theme_constant_override("separation", 12)
	inner_vbox.add_child(_desc_box)

	inner_vbox.add_child(_make_separator())

	# Related entries
	var related_label := Label.new()
	related_label.text = tr("UI_ENCYCLOPEDIA_SEE_ALSO")
	related_label.add_theme_font_size_override("font_size", 12)
	related_label.add_theme_color_override("font_color", TEXT_MUTED)
	inner_vbox.add_child(related_label)

	_related_box = HBoxContainer.new()
	_related_box.add_theme_constant_override("separation", 8)
	inner_vbox.add_child(_related_box)

	# Placeholder content
	_show_welcome()

	return content_bg

# ─────────────────────────────────────────────────────────────────────────────
func _show_welcome() -> void:
	_title_label.text = tr("UI_ENCYCLOPEDIA_WELCOME")
	_icon_label.text = tr("📚")
	_clear_desc()
	var lbl := Label.new()
	lbl.text = tr("UI_ENCYCLOPEDIA_SELECT_INFO")
	lbl.add_theme_font_size_override("font_size", 16)
	lbl.add_theme_color_override("font_color", TEXT_BODY)
	lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_desc_box.add_child(lbl)
	_clear_related()

func _show_entry(entry_id: String) -> void:
	var entry := _get_entry(entry_id)
	if entry.is_empty():
		return
	_current_entry_id = entry_id

	_title_label.text = entry["title"]
	_icon_label.text = entry.get("icon", "📄")

	_clear_desc()
	for para: String in entry.get("description", []):
		var lbl := Label.new()
		lbl.text = para
		lbl.add_theme_font_size_override("font_size", 15)
		lbl.add_theme_color_override("font_color", TEXT_BODY)
		lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_desc_box.add_child(lbl)

	_clear_related()
	for rel_id: String in entry.get("related", []):
		var rel := _get_entry(rel_id)
		if rel.is_empty():
			continue
		var rel_btn := _make_button(rel.get("icon", "📄") + "  " + rel["title"], 13, TEXT_RELATED)
		rel_btn.pressed.connect(_select_entry.bind(rel_id))
		_related_box.add_child(rel_btn)

	# Scroll to top
	_content_scroll.scroll_vertical = 0

	# Update entry button visuals
	_refresh_entry_button_styles()

func _clear_desc() -> void:
	for c in _desc_box.get_children():
		c.queue_free()

func _clear_related() -> void:
	for c in _related_box.get_children():
		c.queue_free()

# ─────────────────────────────────────────────────────────────────────────────
func _select_category(cat: String) -> void:
	_current_category = cat
	_refresh_category_styles()
	_populate_entry_list()
	# Auto-select first visible entry
	var visible := _get_visible_entries()
	if visible.size() > 0:
		_select_entry(visible[0]["id"])

func _select_entry(entry_id: String) -> void:
	_current_entry_id = entry_id
	# If entry is in a different category, switch
	var entry := _get_entry(entry_id)
	if not entry.is_empty() and entry["category"] != _current_category:
		_current_category = entry["category"]
		_refresh_category_styles()
		_populate_entry_list()
	_show_entry(entry_id)

func _populate_entry_list() -> void:
	for c in _entry_list.get_children():
		c.queue_free()
	_entry_buttons.clear()

	var visible := _get_visible_entries()
	_entry_count_label.text = tr("  %d  ") % visible.size()

	for entry in visible:
		var btn := _make_entry_button(entry)
		_entry_buttons[entry["id"]] = btn
		_entry_list.add_child(btn)

	_refresh_entry_button_styles()

func _get_visible_entries() -> Array:
	var result: Array = []
	var search_lower := _search_text.to_lower()
	for e in ENTRIES:
		if e["category"] != _current_category:
			continue
		if search_lower != "" and not (e["title"].to_lower().contains(search_lower)):
			continue
		result.append(e)
	return result

func _get_entry(entry_id: String) -> Dictionary:
	for e in ENTRIES:
		if e["id"] == entry_id:
			return e
	return {}

# ── Style refresh ─────────────────────────────────────────────────────────────
func _refresh_category_styles() -> void:
	for cat in _category_buttons:
		var btn: Button = _category_buttons[cat]
		var is_sel: bool = (cat == _current_category)
		_style_category_button(btn, cat, is_sel)

func _refresh_entry_button_styles() -> void:
	for eid in _entry_buttons:
		var btn: Button = _entry_buttons[eid]
		var is_sel: bool = (eid == _current_entry_id)
		_style_entry_button_active(btn, is_sel)

# ── Widget factories ───────────────────────────────────────────────────────────
func _make_category_button(cat: String) -> Button:
	var btn := Button.new()
	btn.text = "  " + cat
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.custom_minimum_size = Vector2(0, 46)
	btn.add_theme_font_size_override("font_size", 15)
	_style_category_button(btn, cat, false)
	btn.pressed.connect(_select_category.bind(cat))
	return btn

func _style_category_button(btn: Button, cat: String, selected: bool) -> void:
	var cat_color: Color = CAT_COLORS.get(cat, TEXT_ACCENT)
	var normal := StyleBoxFlat.new()
	normal.bg_color = BG_SELECTED if selected else BG_SIDEBAR
	normal.border_color = cat_color if selected else Color(cat_color, 0.3)
	normal.border_width_left = 4 if selected else 2
	normal.content_margin_left = 16
	normal.content_margin_right = 12
	normal.content_margin_top = 8
	normal.content_margin_bottom = 8
	btn.add_theme_stylebox_override("normal", normal)
	btn.add_theme_stylebox_override("pressed", normal)
	var hover := normal.duplicate() as StyleBoxFlat
	hover.bg_color = BG_HOVER
	btn.add_theme_stylebox_override("hover", hover)
	btn.add_theme_color_override("font_color", cat_color if selected else TEXT_BODY)
	btn.add_theme_color_override("font_hover_color", cat_color)

func _make_entry_button(entry: Dictionary) -> Button:
	var btn := Button.new()
	btn.text = "  " + entry.get("icon", "") + "  " + entry["title"]
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.custom_minimum_size = Vector2(0, 40)
	btn.add_theme_font_size_override("font_size", 14)
	btn.add_theme_color_override("font_color", TEXT_BODY)
	btn.add_theme_color_override("font_hover_color", TEXT_ACCENT)
	_style_entry_button_active(btn, false)
	btn.pressed.connect(_select_entry.bind(entry["id"]))
	return btn

func _style_entry_button_active(btn: Button, selected: bool) -> void:
	var normal := StyleBoxFlat.new()
	normal.bg_color = BG_SELECTED if selected else Color(0, 0, 0, 0)
	normal.border_color = BORDER_COLOR if selected else Color(0, 0, 0, 0)
	normal.border_width_left = 3 if selected else 0
	normal.content_margin_left = 20
	normal.content_margin_right = 12
	normal.content_margin_top = 6
	normal.content_margin_bottom = 6
	btn.add_theme_stylebox_override("normal", normal)
	btn.add_theme_stylebox_override("pressed", normal)
	var hover := StyleBoxFlat.new()
	hover.bg_color = BG_HOVER
	hover.content_margin_left = 20
	hover.content_margin_right = 12
	hover.content_margin_top = 6
	hover.content_margin_bottom = 6
	btn.add_theme_stylebox_override("hover", hover)
	btn.add_theme_color_override("font_color", TEXT_ACCENT if selected else TEXT_BODY)

func _make_button(text: String, font_size: int, color: Color) -> Button:
	var btn := Button.new()
	btn.text = text
	btn.add_theme_font_size_override("font_size", font_size)
	btn.add_theme_color_override("font_color", color)
	btn.add_theme_color_override("font_hover_color", color.lightened(0.2))
	var style := StyleBoxFlat.new()
	style.bg_color = Color(color, 0.12)
	style.border_color = Color(color, 0.50)
	style.border_width_bottom = 1
	style.border_width_top = 1
	style.border_width_left = 1
	style.border_width_right = 1
	style.corner_radius_top_left = 6
	style.corner_radius_top_right = 6
	style.corner_radius_bottom_left = 6
	style.corner_radius_bottom_right = 6
	style.content_margin_left = 14
	style.content_margin_right = 14
	style.content_margin_top = 8
	style.content_margin_bottom = 8
	btn.add_theme_stylebox_override("normal", style)
	var hover_style := style.duplicate() as StyleBoxFlat
	hover_style.bg_color = Color(color, 0.25)
	btn.add_theme_stylebox_override("hover", hover_style)
	return btn

func _make_box(bg: Color, radius: int) -> PanelContainer:
	var pc := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = bg
	style.corner_radius_top_left = radius
	style.corner_radius_top_right = radius
	style.corner_radius_bottom_left = radius
	style.corner_radius_bottom_right = radius
	pc.add_theme_stylebox_override("panel", style)
	return pc

func _make_separator() -> HSeparator:
	var sep := HSeparator.new()
	sep.add_theme_color_override("color", BORDER_COLOR)
	return sep

# ── Signal handlers ───────────────────────────────────────────────────────────
func _on_search_changed(new_text: String) -> void:
	_search_text = new_text
	_populate_entry_list()
	var visible := _get_visible_entries()
	if visible.size() > 0:
		_select_entry(visible[0]["id"])

func _on_close_pressed() -> void:
	hide_panel()
	emit_signal("close_requested")

func _unhandled_key_input(event: InputEvent) -> void:
	if not visible:
		return
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		_on_close_pressed()
		get_viewport().set_input_as_handled()
