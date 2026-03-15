# Creating Your First Mod

> Step-by-step guide to creating a custom fish species mod for Lakskongen.

## What We'll Build

A **Rainbow Trout** mod that adds a new fish species with custom growth rates, disease resistance, and market pricing.

## Prerequisites

- Lakskongen installed and running (see [Getting Started](getting-started.md))
- A text editor (VS Code recommended)
- Basic understanding of JSON

## Step 1: Create the Mod Directory

```
mods/
└── rainbow_trout/
    ├── mod.json          # Mod manifest
    ├── data/
    │   └── species.json  # Species definition
    └── assets/
        └── thumbnail.png # 512x512 preview image
```

Create this structure in the game's `mods/` directory:

```bash
mkdir -p mods/rainbow_trout/data
mkdir -p mods/rainbow_trout/assets
```

## Step 2: Create the Mod Manifest

Create `mods/rainbow_trout/mod.json`:

```json
{
  "id": "rainbow_trout",
  "name": "Rainbow Trout Species Pack",
  "version": "1.0.0",
  "author": "YourName",
  "description": "Adds rainbow trout as a farmable species with unique growth and market dynamics",
  "gameVersion": "0.4.0",
  "dependencies": [],
  "tags": ["species", "biology"],
  "thumbnail": "assets/thumbnail.png"
}
```

### Manifest Fields

| Field | Required | Description |
|-------|----------|-------------|
| `id` | ✅ | Unique ID (lowercase, no spaces) |
| `name` | ✅ | Display name |
| `version` | ✅ | Semver (MAJOR.MINOR.PATCH) |
| `author` | ✅ | Your name or handle |
| `description` | ✅ | Short description (< 200 chars) |
| `gameVersion` | ✅ | Minimum game version |
| `dependencies` | ❌ | Other required mod IDs |
| `entryPoint` | ❌ | Script file (for script mods) |
| `tags` | ❌ | Categories for discovery |
| `thumbnail` | ❌ | Preview image path |

## Step 3: Define the Species

Create `mods/rainbow_trout/data/species.json`:

```json
{
  "species": [
    {
      "id": "rainbow_trout",
      "name": "Rainbow Trout",
      "scientificName": "Oncorhynchus mykiss",
      "description": "A hardy freshwater/brackish species with fast early growth",
      "lifecycle": {
        "eggToFryDays": 45,
        "fryToSmoltDays": 180,
        "smoltToAdultDays": 365,
        "maxAgeDays": 2555
      },
      "growth": {
        "model": "vonBertalanffy",
        "wInfinityKg": 5.0,
        "kCoefficient": 0.0025,
        "optimalTempMinC": 10.0,
        "optimalTempMaxC": 18.0
      },
      "genetics": {
        "defaultTraits": {
          "GrowthRate": 0.2,
          "DiseaseResistance": 0.4,
          "SeaLiceResistance": -0.3,
          "FilletQuality": 0.1,
          "FeedConversion": 0.3,
          "StressTolerance": 0.5,
          "LateMaturation": -0.2,
          "ColdTolerance": 0.6
        }
      },
      "market": {
        "basePricePerKg": 55.0,
        "premiumWeightRangeKg": [1.5, 3.0],
        "premiumMultiplier": 1.15
      },
      "mortality": {
        "baselineDailyRate": 0.0003,
        "diseaseVulnerability": {
          "IPN": 0.6,
          "ISA": 0.3,
          "Furunculosis": 0.8
        }
      }
    }
  ]
}
```

### Understanding the Data

- **lifecycle**: How long each life stage takes
- **growth**: von Bertalanffy parameters (`W_inf` = max weight, `k` = growth speed)
- **genetics**: Default breeding values for 8 traits (0 = average, positive = above average)
- **market**: Base price and premium weight range
- **mortality**: Baseline death rate and disease vulnerabilities (0-1 scale)

## Step 4: Test Your Mod

1. Launch Lakskongen
2. Go to **Settings → Mods**
3. Your mod should appear in the mod list
4. Enable it and restart
5. Start a new game — Rainbow Trout should be available as a species

## Step 5: Iterate and Improve

### Common Adjustments

| What to Tweak | Where | Effect |
|---------------|-------|--------|
| Growth speed | `growth.kCoefficient` | Higher = faster growth |
| Max size | `growth.wInfinityKg` | Target harvest weight |
| Market value | `market.basePricePerKg` | Revenue per kg |
| Disease risk | `mortality.diseaseVulnerability` | Outbreak severity |
| Temperature range | `growth.optimalTempMinC/MaxC` | Geographic suitability |

### Balance Tips

- Compare with Atlantic Salmon defaults (`W_inf = 8-12 kg`, price = 100 NOK/kg)
- Trout should be cheaper but grow faster in fresh/brackish water
- Higher stress tolerance makes them beginner-friendly
- Lower sea lice resistance makes them poor for open sea pens

## Advanced: Adding a Script Entry Point

For more complex mods, add a GDScript entry point:

```gdscript
# mods/rainbow_trout/scripts/RainbowTroutMod.gd
extends Node

func _ready():
    print("Rainbow Trout mod loaded!")
    
    # Register custom behaviors
    var event_bus = get_node("/root/EventBus")
    event_bus.DayAdvanced.connect(_on_day_advanced)

func _on_day_advanced(day, season):
    # Custom logic: trout spawn in spring
    if season == "Spring" and day % 30 == 0:
        print("Rainbow trout spawning season!")
```

Update `mod.json` to include the entry point:
```json
"entryPoint": "scripts/RainbowTroutMod.gd"
```

## Mod Types Reference

| Type | Difficulty | Files Needed |
|------|-----------|-------------|
| **Data Mod** (this guide) | Easy | `mod.json` + JSON data |
| **Asset Mod** | Easy | `mod.json` + textures/audio |
| **Script Mod** | Medium | `mod.json` + GDScript/C# |
| **Map Mod** | Hard | `mod.json` + scenes + data |
| **Campaign Mod** | Hard | `mod.json` + scenarios + scripts |

## Next Steps

- 📖 [Modding API Reference](../Modding_API.md) — Full API documentation
- 📊 [API Reference](../API_Reference.md) — SimBridge and EventBus APIs
- 🤝 [Contributing Code](contributing-code.md) — Share your mod with the community
