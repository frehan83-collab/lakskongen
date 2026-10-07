# Steam Store Assets — Lakskongen

> Asset specification for Steam store page, marketing materials, and launch campaign.

---

## 1. Capsule Images

All capsule images must be `.png` format, sRGB color space, no rounded corners, no alpha channel.

| Asset | Dimensions | Filename | Notes |
|-------|-----------|----------|-------|
| Header Capsule | 460 × 215 px | `capsule_header.png` | Shown on store page header, search results, "Featured & Recommended" |
| Main Capsule | 616 × 353 px | `capsule_main.png` | Used in browse/discovery areas |
| Small Capsule | 231 × 87 px | `capsule_small.png` | Wishlists, search results, top sellers |
| Vertical Capsule | 374 × 448 px | `capsule_vertical.png` | Special promotions, seasonal sales |
| Hero Image | 3840 × 1240 px | `hero.png` | Top of store page (optional but recommended) |
| Logo | 940 × 460 px | `logo.png` | Transparent background, game logo only |

**Design direction:**
- Primary art: Norwegian fjord with salmon pens at golden hour
- Logo: "LAKSKONGEN" in bold Nordic-inspired typography
- Color palette: Deep ocean blue (#1B3A5C), salmon pink (#FA8072), Nordic white
- Include Norwegian flag element subtly in branding

---

## 2. Screenshots

**Minimum:** 5 screenshots | **Recommended:** 10–14
**Format:** `.png` or `.jpg` | **Dimensions:** 1920 × 1080 px (16:9)
**Naming convention:** `screenshot_XX_description.png` (zero-padded)

| # | Subject | Filename | Key Elements |
|---|---------|----------|-------------|
| 01 | Dashboard Overview | `screenshot_01_dashboard.png` | Financial KPIs, fish population graphs, weather widget, alert panel |
| 02 | Fish Pen Management | `screenshot_02_fish_pens.png` | Sea site view, pen details, stocking density, feeding controls |
| 03 | Market Trading | `screenshot_03_market.png` | Spot price chart, contract offers, export destinations, OU mean-reversion graph |
| 04 | Processing Plant | `screenshot_04_processing.png` | Processing tiers, yield percentages, profit/loss breakdown |
| 05 | Norwegian Coast Map | `screenshot_05_map.png` | Region selection, production zones, sea temperatures, site locations |
| 06 | Tutorial Sequence | `screenshot_06_tutorial.png` | Guided onboarding, step-by-step instructions, first smolt stocking |
| 07 | Disease Management | `screenshot_07_disease.png` | Sea lice counts, SIR infection model, treatment options, delousing methods |
| 08 | Harvest & Logistics | `screenshot_08_harvest.png` | Wellboat scheduling, truck routing, cold chain management |
| 09 | Weather & Seasons | `screenshot_09_weather.png` | Seasonal variation, storm warnings, temperature effects on growth |
| 10 | Technology & Sustainability | `screenshot_10_tech.png` | Sustainability score, ASC certification, vertical integration tree |
| 11 | Regulation & Traffic Lights | `screenshot_11_regulation.png` | Production-zone traffic lights, MAB capacity bars, resource-rent tax panel, Price Council |
| 12 | Genetics & Breeding | `screenshot_12_genetics.png` | Breeding values, genomic selection, gene-editing traits with approval status |
| 13 | Precision Feeding | `screenshot_13_feeding.png` | AI feeding dashboard, watch-mode spread quality, FCR and waste metrics |
| 14 | Insurance & Mitigation | `screenshot_14_insurance.png` | Catastrophe policy cover, bloom early-warning, bubble curtain / O2 mitigation |

**Screenshot guidelines:**
- Capture at native 1080p with UI visible
- Show real gameplay data (not empty/placeholder states)
- Include variety: zoomed-in detail + overview shots
- No dev tools or debug overlays visible

---

## 3. Trailer

**Duration:** 1:45–2:00 | **Resolution:** 1920 × 1080 (min), 3840 × 2160 (ideal)
**Format:** `.mp4` H.264 | **Audio:** AAC stereo, 48kHz
**Filename:** `lakskongen_ea_trailer_v1.mp4`

### Structure

| Timecode | Section | Content | Audio |
|----------|---------|---------|-------|
| 0:00–0:05 | Cold open | Aerial drone shot over Norwegian fjords at dawn. Text: "Norway. The world's largest salmon producer." | Ambient ocean + seagulls |
| 0:05–0:15 | Hook | Quick cuts: pens in water, fish jumping, market graphs ticking, weather changing. Logo reveal: LAKSKONGEN | Main theme swell |
| 0:15–0:30 | Core loop | Player selects site → stocks smolt → feeds → grows → harvests → sells. Show the satisfying cycle. | Calm gameplay music |
| 0:30–0:45 | Deep systems | Sea lice management, disease SIR model, water chemistry, weather storms. Text overlays explaining depth. | Tension builds |
| 0:45–1:00 | Business empire | Financial statements, contracts, processing plants, wellboat logistics. Show scale. | Business/strategy music |
| 1:00–1:15 | Expansion | International markets (Japan, EU, USA), vertical integration, acquisitions, stakeholder politics | Epic scale music |
| 1:15–1:30 | Sustainability | ASC certification, environmental score, Norwegian regulation compliance. "Can you build sustainably?" | Reflective tone |
| 1:30–1:45 | Scale reveal | Zoom out from single pen → full Norwegian coast with dozens of operations | Music peaks |
| 1:45–2:00 | CTA | "LAKSKONGEN — Early Access 2026" + "Wishlist Now on Steam" + Steam logo | Main theme resolve |

### Trailer Production Notes
- Capture in-game footage at 60fps, edit at 30fps for cinematic feel
- Use seasonal transitions (spring → summer → autumn → winter) as visual metaphor
- Norwegian language option for some text overlays (authenticity)
- Include Steam Next Fest cut (30s version) from 0:00–0:15 + 0:45–1:00

---

## 4. Promotional Art

| Asset | Dimensions | Format | Filename | Usage |
|-------|-----------|--------|----------|-------|
| Key Art | 1920 × 1080 px | `.png` | `keyart_1080p.png` | Press kits, Steam community hub |
| Key Art (4K) | 3840 × 2160 px | `.png` | `keyart_4k.png` | High-res press |
| Social — Landscape | 1200 × 630 px | `.png` | `social_landscape.png` | Twitter/X, Facebook, LinkedIn |
| Social — Square | 1080 × 1080 px | `.png` | `social_square.png` | Instagram, Discord announcements |
| Social — Story | 1080 × 1920 px | `.png` | `social_story.png` | Instagram/TikTok stories |
| Steam Community Banner | 1920 × 622 px | `.png` | `community_banner.png` | Community hub header |
| Press Kit Header | 1280 × 720 px | `.png` | `presskit_header.png` | Press releases |

---

## 5. Community & Store Page Assets

| Asset | Specification | Filename |
|-------|--------------|----------|
| Steam Library Capsule | 600 × 900 px, `.png` | `library_capsule.png` |
| Library Hero | 3840 × 1240 px, `.png` | `library_hero.png` |
| Library Logo | 1280 × 720 px, `.png` (transparent) | `library_logo.png` |
| Achievement Icons | 64 × 64 px, `.png` (locked + unlocked pairs) | `achievement_XX_locked.png` / `achievement_XX_unlocked.png` |
| Trading Cards | 206 × 184 px (card), 1920 × 1080 (background) | `tradingcard_XX.png` |
| Emoticons | 54 × 54 px, `.png` | `emoticon_XX.png` |
| Profile Backgrounds | 1920 × 1080 px, `.jpg` | `profilebg_XX.jpg` |

---

## 6. File Organization

```
steam-assets/
├── capsules/
│   ├── capsule_header.png
│   ├── capsule_main.png
│   ├── capsule_small.png
│   └── capsule_vertical.png
├── screenshots/
│   ├── screenshot_01_dashboard.png
│   ├── screenshot_02_fish_pens.png
│   ├── ...
│   └── screenshot_10_tech.png
├── trailer/
│   ├── lakskongen_ea_trailer_v1.mp4
│   └── lakskongen_trailer_30s.mp4
├── promotional/
│   ├── keyart_1080p.png
│   ├── keyart_4k.png
│   ├── social_landscape.png
│   ├── social_square.png
│   └── social_story.png
├── library/
│   ├── library_capsule.png
│   ├── library_hero.png
│   └── library_logo.png
└── community/
    ├── achievements/
    ├── trading_cards/
    └── emoticons/
```

---

## 7. Asset Production Status

| Category | Required | Status | Priority |
|----------|----------|--------|----------|
| Header Capsule | ✅ Required | ❌ Not started | P0 — Launch blocker |
| Main Capsule | ✅ Required | ❌ Not started | P0 — Launch blocker |
| Small Capsule | ✅ Required | ❌ Not started | P0 — Launch blocker |
| Vertical Capsule | ✅ Required | ❌ Not started | P0 — Launch blocker |
| Screenshots (5+) | ✅ Required | ❌ Not started | P0 — Launch blocker |
| Trailer | ✅ Required | ❌ Not started | P1 — EA can ship with screenshots only |
| Key Art | ⭐ Recommended | ❌ Not started | P1 |
| Social Media | ⭐ Recommended | ❌ Not started | P1 |
| Library Assets | ⭐ Recommended | ❌ Not started | P2 |
| Community Items | Optional | ❌ Not started | P3 — Post-EA |

---

*Generated: 2026-03-15 | Sprint 10 — Pre-Launch*
