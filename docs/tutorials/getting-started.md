# Getting Started — 5-Minute Quickstart

> Start your first salmon farm and make your first harvest in under 5 minutes.

## Prerequisites

- **Godot 4.3+** (with .NET support) — [Download](https://godotengine.org/)
- **.NET 8.0 SDK** — [Download](https://dotnet.microsoft.com/download)
- **Git** — [Download](https://git-scm.com/)

## 1. Clone & Open (1 minute)

```powershell
git clone https://github.com/yourusername/lakskongen.git
cd lakskongen
```

Open `godot/project.godot` in Godot Editor.

## 2. Build the Simulation (30 seconds)

In Godot, click **Build** (or press `Ctrl+Shift+B`). This compiles the C# simulation library.

Alternatively, from terminal:
```powershell
cd src
dotnet build
```

## 3. Run the Game (30 seconds)

Press **F5** in Godot (or click the ▶ Play button). The main menu appears.

Click **New Game** → Enter your farm name → **Start**.

## 4. Your First Farm (2 minutes)

You start with:
- **10,000,000 NOK** starting capital
- **1 sea site** in a Norwegian production zone
- The in-game tutorial guides you through the basics

### Key Actions

| Action | How |
|--------|-----|
| **Stock fish** | Hatchery panel → Release smolts to sea site |
| **Feed fish** | Automatic (FeedSystem manages FCR) |
| **Monitor health** | Dashboard → Check lice count & disease alerts |
| **Advance time** | Speed controls: 1x / 2x / 4x / Pause |
| **Harvest** | When fish reach 4-6 kg → Harvest button |
| **Sell** | Choose spot market or accept contracts |

### Dashboard Overview

The HUD shows at a glance:
- 📅 **Day counter** and season
- 💰 **Balance** (NOK)
- 🐟 **Total fish count** and biomass
- 🌡️ **Water temperature**
- 📊 **Market price** (NOK/kg)
- 🦠 **Sea lice** per fish

## 5. Make Your First Harvest (1 minute)

1. Advance time until fish reach ~4-5 kg (about 12-18 months in-game)
2. Open **Production** panel
3. Click **Harvest** on your sea site
4. Choose **Spot Market** to sell immediately
5. Watch revenue flow into your balance!

## What's Next?

- 📖 [Building from Source](building-from-source.md) — Detailed build guide
- 🧬 [Architecture Deep Dive](../Architecture.md) — How the simulation works
- 🔧 [Creating Your First Mod](creating-your-first-mod.md) — Customize the game
- 🧪 [Running Tests](running-tests.md) — Verify everything works

## Tips for New Players

1. **Don't over-stock** — More fish = more disease risk + regulatory trouble
2. **Watch the lice** — Traffic Light System can restrict your growth
3. **Diversify contracts** — Don't sell everything on spot market
4. **Breed smart** — Use the genetics panel to select for balanced traits
5. **Save often** — The game auto-saves, but manual saves give you rollback points
