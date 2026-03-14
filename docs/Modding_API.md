# Lakskongen — Modding API (Alpha)

**Version:** 0.4.0 (Mod support in early development)  
**Last Updated:** 2026-03-15  
**Status:** 🚧 **Alpha** — API subject to change

---

## 🎯 Vision

Mod support is a **first-class feature** in Lakskongen, inspired by Farming Simulator's 3.2 billion mod downloads. We want to empower the community to:

- Add new fish species (trout, cod, halibut)
- Create custom maps and fjords
- Design campaign scenarios
- Implement new technologies and equipment
- Modify game balance and rules
- Build total conversion mods

---

## 🛠️ Mod System Overview

### Mod Types

| Type | Description | Examples |
|------|-------------|----------|
| **Data Mods** | JSON configs for balance, species, tech tree | Custom growth rates, new diseases |
| **Asset Mods** | Graphics, sounds, models | Skins, UI themes, audio packs |
| **Script Mods** | GDScript/C# for gameplay logic | New game modes, AI behaviors |
| **Map Mods** | Custom fjords and zones | Scottish Highlands, Chilean coast |
| **Campaign Mods** | Story scenarios with objectives | "1970s Pioneer", "Crisis Management" |
| **Total Conversion** | Complete gameplay overhaul | "Trout Farming", "Offshore Oil" |

### Mod Format

Mods are packaged as Godot `.pck` or `.zip` files with a manifest:

```
my_mod/
├── mod.json              # Mod manifest
├── data/                 # JSON configs
│   ├── species.json
│   ├── tech_tree.json
│   └── balance.json
├── assets/               # Graphics/audio
│   ├── textures/
│   ├── models/
│   └── sounds/
├── scripts/              # GDScript/C# code
│   └── CustomFishBehavior.gd
└── scenes/               # Custom scenes
    └── MyFjord.tscn
```

---

## 📝 Manifest Format (mod.json)

```json
{
  "id": "my_salmon_mod",
  "name": "Super Salmon 2000",
  "version": "1.0.0",
  "author": "YourName",
  "description": "Adds genetically engineered super salmon with 2x growth rate",
  "gameVersion": "0.4.0",
  "dependencies": [],
  "entryPoint": "scripts/SuperSalmonMod.gd",
  "tags": ["species", "biology", "balance"],
  "thumbnail": "assets/thumbnail.png"
}
```

### Fields

- **id:** Unique identifier (lowercase, no spaces)
- **name:** Display name
- **version:** Semantic versioning (MAJOR.MINOR.PATCH)
- **author:** Creator name/handle
- **description:** Short description (&lt;200 chars)
- **gameVersion:** Minimum game version required
- **dependencies:** Other mods required (array of mod IDs)
- **entryPoint:** Main script file (optional for data-only mods)
- **tags:** Categories for discovery
- **thumbnail:** Preview image (512x512 PNG)

---

## 📊 Data Mods (JSON Configs)

### 1. Custom Fish Species

**File:** `data/species.json`

```json
{
  "species": [
    {
      "id": "rainbow_trout",
      "name": "Rainbow Trout",
      "scientificName": "Oncorhynchus mykiss",
      "baseGrowthRate": 0.95,
      "maxWeightKg": 12.0,
      "optimalTempRange": [10, 16],
      "diseaseResistance": 0.7,
      "marketPriceMultiplier": 0.85,
      "spawningSeasonStart": 270,
      "spawningSeasonEnd": 90,
      "feedEfficiency": 1.1
    }
  ]
}
```

**Fields:**
- **id:** Internal identifier
- **baseGrowthRate:** Growth multiplier (1.0 = salmon baseline)
- **maxWeightKg:** Asymptotic weight for growth curve
- **optimalTempRange:** [min, max] in °C
- **diseaseResistance:** 0.0-1.0 (affects infection rates)
- **marketPriceMultiplier:** Price relative to salmon
- **spawningSeasonStart/End:** Day of year (0-365)
- **feedEfficiency:** FCR multiplier

**Usage:**
The game automatically loads all species from enabled mods and adds them to the breeding UI.

---

### 2. Custom Technology

**File:** `data/tech_tree.json`

```json
{
  "technologies": [
    {
      "id": "laser_delouser_mk2",
      "name": "Laser Delouser Mk II",
      "description": "Advanced laser system with AI targeting",
      "tier": 4,
      "prerequisites": ["laser_delouser"],
      "cost": 5000000,
      "researchTimeDays": 180,
      "effects": {
        "liceReductionRate": 0.95,
        "powerConsumption": 50.0,
        "maintenanceCostPerDay": 500
      }
    }
  ]
}
```

**Effects Schema:**
- **liceReductionRate:** 0.0-1.0 (percentage killed per treatment)
- **powerConsumption:** kWh/day
- **maintenanceCostPerDay:** NOK
- **mortalityReduction:** Fish survival improvement
- **growthBonus:** Growth rate multiplier

---

### 3. Custom Diseases

**File:** `data/diseases.json`

```json
{
  "diseases": [
    {
      "id": "custom_virus",
      "name": "Custom Viral Disease",
      "transmissionRate": 0.15,
      "recoveryRate": 0.05,
      "mortalityRate": 0.02,
      "incubationDays": 14,
      "symptoms": ["Lethargy", "Loss of appetite"],
      "treatmentCost": 25000,
      "treatmentEffectiveness": 0.7
    }
  ]
}
```

---

### 4. Balance Tweaks

**File:** `data/balance.json`

```json
{
  "growth": {
    "baseGrowthRateMultiplier": 1.2,
    "temperatureToleranceWider": true
  },
  "market": {
    "basePrice": 90.0,
    "seasonalVolatility": 0.4
  },
  "regulations": {
    "mabLimitMultiplier": 1.5,
    "liceThresholdFemales": 0.8
  }
}
```

**Note:** Balance mods should be marked as "gameplay-altering" to avoid confusion.

---

## 🎨 Asset Mods

### Textures

**Supported Formats:** PNG, JPG (WebP for compression)

**Texture Overrides:**
Place textures in `assets/textures/<original_name>.png` to replace game assets.

**Example:**
```
my_mod/
└── assets/
    └── textures/
        ├── fish_salmon.png       # Replaces default salmon texture
        └── ui_button_harvest.png # Replaces harvest button
```

### Models

**Supported Formats:** GLTF 2.0 (`.gltf` or `.glb`)

**Model Overrides:**
```
my_mod/
└── assets/
    └── models/
        └── fish_salmon.glb
```

**Guidelines:**
- Max 5000 polygons per model (performance)
- Use PBR materials (Godot Standard Material 3D)
- Include LOD meshes for distant rendering

### Audio

**Supported Formats:**
- **Music:** `.ogg` (Vorbis, looped)
- **SFX:** `.wav` (uncompressed, short)

**Audio Overrides:**
```
my_mod/
└── assets/
    └── sounds/
        ├── ocean_ambience.ogg
        └── harvest_splash.wav
```

---

## 🧩 Script Mods (GDScript)

### Entry Point

**File:** `scripts/MyMod.gd`

```gdscript
extends Node

func _ready():
    print("My Mod loaded!")
    ModAPI.register_species(load("res://my_mod/data/species.json"))
    ModAPI.register_event_handler("on_fish_harvested", _on_harvest)

func _on_harvest(data):
    print("Fish harvested: ", data.count)
    # Custom logic here
```

### ModAPI Interface (Planned)

**Available Functions:**

```gdscript
# Species
ModAPI.register_species(data: Dictionary)
ModAPI.get_species(id: String) -> Dictionary

# Events
ModAPI.register_event_handler(event_name: String, callback: Callable)
ModAPI.trigger_event(event_name: String, data: Dictionary)

# Market
ModAPI.set_price_modifier(species_id: String, multiplier: float)
ModAPI.get_current_price(species_id: String) -> float

# Save/Load
ModAPI.save_mod_data(mod_id: String, data: Dictionary)
ModAPI.load_mod_data(mod_id: String) -> Dictionary

# UI
ModAPI.add_menu_item(label: String, callback: Callable)
ModAPI.show_notification(message: String, type: int)
```

**Example: Custom Event Handler**

```gdscript
func _ready():
    ModAPI.register_event_handler("on_day_advanced", _check_for_event)

func _check_for_event(day: int):
    if day % 100 == 0:
        ModAPI.show_notification("Milestone: Day %d!" % day, ToastNotification.Type.SUCCESS)
```

---

## 🗺️ Map Mods

### Map Structure

**File:** `scenes/MyFjord.tscn`

**Requirements:**
- Extends `BaseFjordMap` scene
- Defines spawn points for sea sites
- Includes water surface mesh
- Collision shapes for boundaries

**Example Map Script:**

```gdscript
extends BaseFjordMap

func _ready():
    map_name = "Chilean Fjords"
    available_zones = [
        {
            "id": "chile_zone_1",
            "name": "Región de Los Lagos",
            "temperature_profile": [12, 14, 16, 15, 13, 11, 10, 9, 10, 11, 12, 13],
            "lice_pressure": 0.3
        }
    ]
    
    # Define site spawn points
    add_site_location(Vector3(100, 0, 200), "Site Alpha")
    add_site_location(Vector3(-50, 0, 150), "Site Beta")
```

---

## 🎮 Campaign Mods

### Campaign Definition

**File:** `data/campaign.json`

```json
{
  "id": "pioneer_1970s",
  "name": "The Pioneer (1970s)",
  "description": "Build Norway's first commercial salmon farm from scratch.",
  "startingConditions": {
    "cash": 50000,
    "year": 1975,
    "licenses": 1,
    "technology": ["wooden_pens", "basic_feed"]
  },
  "objectives": [
    {
      "id": "first_harvest",
      "description": "Harvest your first batch of salmon",
      "target": { "harvests": 1 },
      "reward": { "cash": 10000 }
    },
    {
      "id": "expand_to_3_sites",
      "description": "Expand to 3 sea sites",
      "target": { "sites": 3 },
      "reward": { "technology": "semi_closed_pens" }
    }
  ],
  "events": [
    {
      "day": 365,
      "type": "storm",
      "severity": "high",
      "message": "A winter storm damages your pens!"
    }
  ],
  "winCondition": {
    "description": "Reach 500,000 kg annual production",
    "target": { "annualProductionKg": 500000 }
  }
}
```

---

## 🔧 Mod Tools

### Validation Tool (Planned)

**Command:**
```bash
lakskongen-mod-validator my_mod.zip
```

**Checks:**
- Manifest syntax
- Missing dependencies
- Asset file formats
- Script syntax errors
- Balance sanity (no 1000x multipliers)

### Packaging Tool (Planned)

**Command:**
```bash
lakskongen-mod-pack my_mod/ -o my_mod.lkmod
```

**Output:**
- Compressed `.lkmod` (Godot PCK format)
- Steam Workshop compatible
- Checksum for integrity verification

---

## 📦 Steam Workshop Integration (Planned)

### Publishing Workflow

1. **Create mod** in `mods/my_mod/`
2. **Test locally** — Enable in Mod Manager
3. **Package** — Use mod pack tool
4. **Upload to Steam Workshop**
5. **Players subscribe** — Auto-download and install

### Mod Manager UI (Planned)

**Features:**
- Browse installed mods
- Enable/disable mods
- Check for updates
- Manage load order (for conflicting mods)
- Safe mode (disable all mods)

---

## ⚠️ Mod Safety & Sandboxing

### Allowed Operations
- ✅ Read game data (species, prices, state)
- ✅ Modify data through ModAPI
- ✅ Add new content (species, tech, maps)
- ✅ Subscribe to events

### Restricted Operations
- ❌ Direct file system access (use ModAPI for saves)
- ❌ Network requests (security risk)
- ❌ Arbitrary code execution (GDScript only, no native libs)
- ❌ Modifying other mods' data

**Reasoning:** Prevent malicious mods from stealing data or corrupting saves.

---

## 🧪 Testing Your Mod

### Local Testing

1. **Copy mod folder** to `godot/mods/`
2. **Enable in Mod Manager** (or edit `user://mods_enabled.json`)
3. **Launch game** and verify:
   - Mod loads without errors
   - New content appears in-game
   - No conflicts with base game
4. **Check console** for warnings/errors

### Debug Mode

**Enable debug logging:**
```gdscript
# In your mod script
ModAPI.enable_debug_logging(true)
```

**View logs:**
- **Godot Editor:** Output tab
- **Exported game:** `user://logs/mod_debug.log`

---

## 🤝 Community Guidelines

### Best Practices
- **Credit sources** for assets (CC licenses, etc.)
- **Version compatibility** — Update for major game releases
- **Documentation** — Include README with usage instructions
- **Balance testing** — Ensure mod doesn't break progression
- **Compatibility** — Test with popular mods

### Forbidden Content
- Stolen assets (copyright violations)
- Malicious code (viruses, data theft)
- Inappropriate content (NSFW, hateful)
- Exploits (intentional game-breaking bugs)

---

## 📚 Example Mods (Planned Official Releases)

### 1. "Rainbow Trout Species Pack"
- Adds rainbow trout as playable species
- New growth curves and market prices
- Trout-specific diseases

### 2. "Chilean Waters Map"
- Chilean fjord map with 8 zones
- Different regulations and market dynamics
- Spanish localization

### 3. "Hardcore Mode"
- 2x disease frequency
- 50% lower profit margins
- Strict environmental penalties

---

## 🔮 Future Mod API Roadmap

### Planned Features
- **Lua Scripting** — For safer, sandboxed logic
- **Visual Mod Editor** — Drag-and-drop for non-coders
- **Mod Marketplace** — Curated mods with donations
- **Multiplayer Mods** — Custom server rules
- **Asset Store Integration** — Sell premium mods

---

## 📞 Support

- **Mod Developer Discord:** *(TBD)*
- **Documentation:** https://docs.lakskongen.com/modding *(TBD)*
- **GitHub Examples:** https://github.com/lakskongen/example-mods *(TBD)*

---

## 📜 Licensing

**Mod License Requirements:**
- Mods using game assets must credit Lakskongen
- Open-source mods encouraged (MIT/GPL preferred)
- Commercial mods allowed (with revenue share TBD)

**Base Game Assets:**
- Modders may **reference** base game assets (textures, models)
- Modders may **not redistribute** base game files outside the game

---

**Happy Modding!** 🐟🛠️

**Status:** Alpha — API subject to change. Feedback welcome!
