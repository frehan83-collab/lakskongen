Lakskongen — Design Handoff (local)

Overview
- This document explains the local design tokens, component naming conventions, and the Godot handoff process.

Files
- salmon-sim/design/tokens.json  — design tokens (colors, spacing, radii, fonts, icon sizes)
- salmon-sim/design/assets/      — exported SVG/PNG assets
- salmon-sim/design/prototypes/  — exported prototype images
- salmon-sim/design/handoff.md   — this file

Component naming conventions (match Godot node names)
- Button/Primary
- Button/Secondary
- Panel/Card
- Panel/Dashboard
- Icon/{name} (svg filenames should be icon-{name}.svg)
- Dashboard/FishKPIs
- Dashboard/FeedPanel
- Dashboard/EventLog
- Dashboard/MarketPanel

Theme & Godot handoff
- Use tokens.json to fill a Godot theme resource (theme.tres). Map tokens like:
  - "colors.bg" -> Theme.bg_color
  - "fonts.base" -> Theme/default_font
  - Spacing/units used as margin/padding constants in UI scenes

Export rules
- Icons: export SVG in salmon-sim/design/assets/svg/, and PNG @2x in salmon-sim/design/assets/png@2x/
- Atlases: use Godot's AtlasTexture or create atlas via TexturePacker; store atlas descriptor in salmon-sim/design/assets/atlas.json

Handoff checklist (for devs)
1. Copy tokens.json values into Godot theme.tres or use the mission-control script to generate a theme stub.
2. Import SVGs into Godot as vector textures or export to PNG if necessary.
3. Place exported assets under res://design/ in the Godot project.
4. Attach Dashboard.tscn to UI scenes and wire GameManager.get_metrics() as data source.

If you want Figma later, tokens.json is ready to import as tokens.
Godot theme properties (exact names to include):
- gui/theme_color_bg: colors.bg
- gui/theme_color_panel: colors.panel
- gui/theme_color_text: colors.text
- gui/theme_color_muted: colors.muted
- gui/theme_accent: colors.accent
- gui/theme_accent_2: colors.accent-2
- gui/theme_danger: colors.danger
- gui/theme_spacing_unit: spacing.unit
- gui/theme_radius_sm: radii.sm
- gui/theme_radius_md: radii.md
- gui/theme_radius_lg: radii.lg
- gui/theme_font_base: fonts.base
- gui/theme_font_size_xs: fonts.sizes.xs
- gui/theme_font_size_sm: fonts.sizes.sm
- gui/theme_font_size_md: fonts.sizes.md
- gui/theme_font_size_lg: fonts.sizes.lg
- gui/theme_font_size_xl: fonts.sizes.xl

Example Godot theme.tres snippet:
[gd_resource type="Theme" load_steps=2]
[resource]
[ext_resource path="res://assets/Inter-Regular.ttf" type="DynamicFontData" id=1]
[resource]
resource_name = "LakskongenTheme"

[theme]
# colors
color_bg = Color(0.06,0.09,0.13)
color_panel = Color(0.07,0.09,0.15)
color_text = Color(0.90,0.93,0.95)
color_muted = Color(0.60,0.66,0.70)
color_accent = Color(0.17,0.72,0.71)

# font sizes
font_size_md = 16
font_size_lg = 20

# radii (use as constants in control styles)
radius_sm = 6
radius_md = 10
radius_lg = 16
