# 🎨 LAKSKONGEN — Art Direction Brief

## Executive Summary

After deep research into 50+ successful simulation/tycoon games, art style trends, production feasibility, and what resonates with players on Steam in 2024-2026, here is the definitive art direction for Lakskongen.

---

## 🏆 RECOMMENDED STYLE: "Nordic Illustrated" — Flat Illustrated + Soft Lighting

**Think:** Dredge meets Anno 1800 meets Mini Motorways — but Norwegian.

A **clean, illustrated 2D style** with soft gradients, muted Nordic color palette, atmospheric lighting that shifts with weather/seasons, and a top-down/slight-tilt perspective for the main game view.

### Why This Style Wins

| Factor | Score | Reasoning |
|--------|-------|-----------|
| **Visual Identity** | ⭐⭐⭐⭐⭐ | No aquaculture sim looks like this. Instant recognition. |
| **Production Feasibility** | ⭐⭐⭐⭐ | AI-assisted + vector tools. Small team viable. |
| **Steam Appeal** | ⭐⭐⭐⭐⭐ | Clean illustrated styles consistently get wishlisted. |
| **Information Density** | ⭐⭐⭐⭐⭐ | Flat style = overlays, HUD, dashboards work seamlessly. |
| **Mood/Atmosphere** | ⭐⭐⭐⭐⭐ | Norwegian fjords, weather, seasons — illustrated style nails this. |
| **Scalability** | ⭐⭐⭐⭐ | Easy to add assets without breaking consistency. |
| **Performance** | ⭐⭐⭐⭐⭐ | 2D sprites = runs on anything. |

---

## 📊 Competitive Analysis: What Works in Sim Games

### The Spectrum of Successful Styles

| Game | Style | Sales | Why It Works |
|------|-------|-------|-------------|
| **Dave the Diver** | Pixel art + 3D hybrid | 4M+ | Charm, nostalgia, personality. But mixing 2D/3D is complex. |
| **Stardew Valley** | 16-bit pixel art | 30M+ | Warmth, accessibility. But saturated market for pixel farms. |
| **Dredge** | Low-poly 3D, watercolor feel | 1M+ | Atmospheric, moody, unique. Fishing/ocean done right. |
| **Prison Architect/RimWorld** | Minimal top-down | 4M+ each | Proves gameplay > graphics. But looks dated. |
| **Frostpunk** | Dark industrial 3D | 3M+ | Incredible atmosphere. But AAA budget required. |
| **Anno 1800** | Detailed realistic 3D | 2.5M+ | Beautiful but 100+ person team. |
| **Mini Motorways** | Minimalist flat | 1M+ | Clean, readable, calming. Proves less = more. |
| **Townscaper** | Procedural colorful 3D | 1M+ | Gorgeous with minimal assets. |
| **Two Point Hospital** | Cartoonish 3D | 4M+ | Humor + readability. Bright and fun. |
| **Islanders** | Clean low-poly | 500K+ | Minimalist beauty, instant Steam appeal. |
| **Aquaculture Land** | Basic pixel art | <10K | Only direct competitor. Looks amateur. LOW BAR TO BEAT. |

### Key Insight
> **The only existing fish farm game (Aquaculture Land) uses basic pixel art and sold poorly.** There is ZERO competition for a premium-looking aquaculture sim. Lakskongen can own this space visually.

---

## 🎨 The "Nordic Illustrated" Style — In Detail

### Visual DNA

```
CORE INFLUENCES:
├── Dredge — Atmospheric ocean/fishing mood, sense of place
├── Mini Motorways — Clean readability, soft gradients, UI elegance  
├── Islanders — Warm illustrated buildings, satisfying placement
├── Anno 1800 — Information density done beautifully
├── Frostpunk — Emotional weight through color temperature
└── Norwegian folk art (rosemaling) — Subtle cultural identity
```

### Color Palette

**Primary Palette — "Nordlys" (Northern Lights)**

| Token | Hex | Usage |
|-------|-----|-------|
| `FJORD_DEEP` | `#0B1929` | Deep water, night sky, primary BG |
| `FJORD_MID` | `#1A3A5C` | Mid-depth water, panel backgrounds |
| `FJORD_SHALLOW` | `#2E6B8A` | Shallow water, interactive elements |
| `GLACIER` | `#E8F4F8` | Text on dark, ice, winter elements |
| `SALMON` | `#E8825C` | Primary accent — salmon, alerts, warmth |
| `SALMON_DARK` | `#C4613A` | Pressed states, deep accent |
| `KELP` | `#2D8B5E` | Growth, health, profit, nature |
| `KELP_LIGHT` | `#5BC48A` | Positive trends, success states |
| `STORM` | `#4A5568` | Neutral, disabled, secondary text |
| `MIDNIGHT_SUN` | `#FFD166` | Gold, premium, achievements, sun |
| `AURORA_GREEN` | `#43D9AD` | Special events, rare items |
| `AURORA_PURPLE` | `#7B61FF` | Research, tech tree, innovation |
| `DANGER_RED` | `#E54D4D` | Disease, loss, warnings |
| `SNOW` | `#F7FAFC` | Winter overlay, clean whites |

**Seasonal Color Shifts:**
- 🌸 **Spring:** Warmer greens, light blues, pink accents (smolt release)
- ☀️ **Summer:** Bright, saturated, golden tones (midnight sun)
- 🍂 **Autumn:** Deep oranges, muted greens, warm shadows (harvest season)
- ❄️ **Winter:** Cool blues, white overlays, soft purples (Northern Lights)

### Typography

| Use | Font Style | Fallback |
|-----|-----------|----------|
| **Headers/Logo** | Custom Nordic slab serif (think: fjord carved in wood) | Josefin Slab, Lora |
| **Body/UI** | Clean geometric sans | Inter, DM Sans, Nunito |
| **Data/Numbers** | Monospace for financial dashboards | JetBrains Mono, Fira Code |
| **Flavor text** | Slightly weathered / hand-lettered feel | Caveat, Patrick Hand |

### Perspective & Layout

**Main Game View:** Top-down with ~15° tilt (dimetric/oblique)
- Enough angle to show building faces
- Flat enough for clean UI overlays
- Similar to Mini Motorways/Islanders approach

**Camera Modes:**
1. **Strategic View** — Full region, zoomed out, data overlays
2. **Farm View** — Single farm close-up, see individual pens
3. **Underwater View** — Cross-section showing fish, nets, depth (key differentiator!)

### Art Asset Categories

#### 1. 🌊 Environment/Terrain
- **Water:** Animated gradient tiles (deep → shallow), foam edges, wave patterns
- **Land:** Norwegian coastline, rocky shores, grass, trees (birch, pine)
- **Mountains:** Layered parallax backgrounds, snow-capped
- **Sky:** Dynamic gradient system (day/night cycle, weather, aurora borealis)
- **Fjords:** The star of the show — narrow water channels between mountains

#### 2. 🐟 Fish & Biology
- **Atlantic Salmon lifecycle:** Egg → Fry → Parr → Smolt → Adult → Harvest
- Each stage: distinct illustrated sprite with subtle animation (breathing, swimming)
- **Disease states:** Visual indicators (parasites, discoloration)
- **Size visualization:** Scaling sprites based on weight class
- **Underwater cross-section:** Schools of fish in pen, depth layers

#### 3. 🏗️ Buildings & Infrastructure
- **Hatchery:** Warm indoor facility, flowing water channels
- **Sea pens/cages:** Circular floating structures with nets (THE iconic asset)
- **Feed barges:** Industrial, functional design
- **Processing plant:** Factory building, conveyor hints
- **Wellboat:** Distinctive hull design for fish transport
- **Office/HQ:** Administrative building, upgradeable
- **Research lab:** Modern, clean design
- **Warehouse/cold storage:** Industrial with frost effects

#### 4. 🚢 Vehicles & Transport
- **Wellboat** (fish transport) — the most recognizable industry vehicle
- **Feed boat/barge** — delivery runs
- **Service boat** — small, for inspections
- **Truck** — road transport to market
- **Helicopter** (late-game luxury/emergency)

#### 5. 🗺️ Map/Region
- **8 Norwegian regions** — each with distinct visual personality:
  - Rogaland: Flat coastline, oil rigs visible
  - Hordaland: Deep fjords, Bergen city
  - Møre og Romsdal: Dramatic mountains
  - Trøndelag: Gentle coast, farmland
  - Nordland: Lofoten islands, dramatic scenery
  - Troms: Arctic, Northern Lights
  - Finnmark: Remote, Arctic tundra
  - Sogn og Fjordane: Classic fjord postcard

#### 6. 📊 UI & HUD
- **Dashboard panels:** Frosted glass effect over game view
- **Icons:** Consistent illustrated icon set (64px base)
- **Charts/Graphs:** Smooth animated lines, branded colors
- **Notifications:** Toast style with fish-themed iconography
- **Menus:** Slide-in panels, not full-screen overlays (keep world visible)

#### 7. 🌤️ Weather & Effects
- **Rain:** Particle overlay + dimmed lighting
- **Snow:** White particles + terrain whitening
- **Storm:** Dark overlay + wave intensification + lightning flashes
- **Fog:** Soft gaussian blur on distant elements
- **Aurora Borealis:** Animated gradient ribbon (night in northern regions)
- **Seasons:** Full color palette shift + vegetation changes

---

## 🔧 Production Pipeline

### Phase 1: Foundation (Week 1-2)
1. **Color system** — Implement new palette in `LakskongenTheme.gd`
2. **Typography** — Import 2-3 fonts, set up font system
3. **Icon set** — 40-50 core icons (SVG → PNG atlas)
4. **UI Framework** — Redesign panels with new style tokens

### Phase 2: Environment (Week 3-4)
1. **Water tiles** — Animated tileset (deep/mid/shallow/shore)
2. **Terrain tiles** — Land, rock, grass, snow variants
3. **Sky system** — Gradient backgrounds with day/night
4. **Weather particles** — Rain, snow, fog overlays

### Phase 3: Assets (Week 5-8)
1. **Buildings** — All structures as illustrated sprites
2. **Fish** — Lifecycle stages with animation
3. **Vehicles** — Wellboat, trucks, service boats
4. **Map** — Illustrated Norwegian regions

### Phase 4: Polish (Week 9-10)
1. **Animations** — Idle animations, transitions
2. **Effects** — Particle systems, weather integration
3. **Sound-visual sync** — Visual feedback tied to audio
4. **Seasonal variations** — All assets in 4 season variants

### AI-Assisted Art Production

**Recommended Tools:**
| Tool | Use Case | Why |
|------|----------|-----|
| **Midjourney** | Concept art, mood boards, background illustrations | Best quality for illustrated style |
| **Stable Diffusion + LoRA** | Consistent sprite generation, icon sets | Train on our style for consistency |
| **Figma/Illustrator** | Clean up AI output, create final SVGs | Vector precision for UI |
| **Aseprite** | Sprite animation frames | If any pixel elements needed |
| **Godot Shader** | Water animation, lighting, weather effects | Runtime effects, zero assets needed |

**Workflow:**
```
Concept (Midjourney) → Clean-up (Figma/AI) → Export (PNG/SVG) → Godot Import → Shader Polish
```

**Quality Gate:** Every AI-generated asset must pass:
1. ✅ Consistent with style guide palette
2. ✅ Clean edges (no AI artifacts)
3. ✅ Works at target resolution (1920x1080 → 4K)
4. ✅ Readable at zoom-out level
5. ✅ Consistent with other assets in category

---

## 🎯 Visual Differentiators — What Makes Us Unique

### 1. The Underwater Cross-Section
No other management game shows what's happening BELOW the surface. A toggleable underwater view showing fish swimming, density, feeding behavior, and water quality is our visual signature.

### 2. Norwegian Light
Real-time lighting that shifts with latitude, season, and time of day. Northern Norway gets midnight sun in summer and polar night in winter. This hasn't been done in a tycoon game.

### 3. Living Water
The fjords aren't static backgrounds — currents flow, tides rise and fall, waves respond to weather. The water IS the game.

### 4. Data as Art
Financial dashboards aren't boring spreadsheets — they're beautiful visualizations integrated into the world. Think: information as fjord-carved infographics.

### 5. The Weather Tells a Story
Every storm, every calm day, every season change is visually dramatic. Weather isn't just a number modifier — it's a visual event that creates emotional tension.

---

## 📐 Technical Specs

| Parameter | Value |
|-----------|-------|
| **Base resolution** | 1920×1080 |
| **Scale target** | 4K ready (vector where possible) |
| **Tile size** | 64×64 (terrain), 128×128 (buildings) |
| **Sprite format** | PNG with alpha (SVG source files kept) |
| **Color depth** | 32-bit RGBA |
| **Animation** | Sprite sheets, 6-12 fps for game elements |
| **Parallax layers** | 3-4 (sky, mountains, midground, water) |
| **UI rendering** | Godot CanvasLayer with shader effects |
| **Shader budget** | ≤5 active shaders at any time |

---

## 🆚 Styles We Rejected (And Why)

### ❌ Pixel Art
- **Pro:** Nostalgic, fast to produce, proven
- **Con:** Saturated market (every indie uses it), hard to show data density, "Aquaculture Land already did it poorly", limits the premium feel we want

### ❌ Realistic 3D
- **Pro:** Impressive, immersive, AAA feel
- **Con:** Requires massive team, Godot 4's 3D is not its strength, performance concerns, overkill for a management game

### ❌ Low-Poly 3D
- **Pro:** Trendy, good performance, Dredge proved it works for fishing
- **Con:** Harder to overlay dense UI, 3D camera management adds complexity, not ideal for our tab-heavy dashboard design

### ❌ Minimalist/Abstract (Mini Motorways style)
- **Pro:** Beautiful, cheap to produce, clean
- **Con:** Too abstract for a simulator that needs to show specific real-world things (salmon, pens, boats), limits emotional connection

### ❌ Cartoon/Chibi (Two Point style)
- **Pro:** Fun, approachable, good for humor
- **Con:** Doesn't match the serious/strategic tone we want, might not appeal to the management sim hardcore audience

### ✅ Illustrated Flat ("Nordic Illustrated")
- The sweet spot: beautiful enough to wow, simple enough to produce, data-friendly for dashboards, atmospheric for Norwegian setting, unique in the market.

---

## 📋 Immediate Next Steps

1. **[ ] Create mood board** — 20-30 reference images in Midjourney capturing the style
2. **[ ] Palette implementation** — Update `LakskongenTheme.gd` with new colors
3. **[ ] Icon set sprint** — Generate 50 core game icons
4. **[ ] Water shader prototype** — Animated water in Godot
5. **[ ] One building test** — Sea pen cage as proof-of-concept asset
6. **[ ] UI mockup** — Dashboard redesign in new style (Figma or HTML)

---

## 🎬 Reference & Inspiration Board

**Games:**
- Dredge (atmosphere, ocean mood)
- Mini Motorways (clean UI, readability)
- Islanders (warm illustrated buildings)
- Frostpunk (emotional color temperature)
- Anno 1800 (information density)
- Terra Nil (environmental beauty)
- Townscaper (procedural charm)

**Visual References:**
- Norwegian travel posters (vintage and modern)
- Hurtigruten cruise line design language
- Norwegian fjord photography (Lofoten, Hardanger)
- Rosemaling folk art (subtle pattern elements)
- Nordic noir design (clean, dark, purposeful)

**Art Resources:**
- gameuidatabase.com — UI screenshots from 1300+ games
- interfaceingame.com — Game interface reference
- iStock/Dreamstime Norwegian salmon farm illustrations

---

*This document is the north star for all visual decisions in Lakskongen. Every asset, every UI element, every color choice should trace back to this brief.*

**Style in one sentence:** *"A calm Norwegian fjord painted in soft watercolors, with the precision of a Bloomberg terminal underneath."*
