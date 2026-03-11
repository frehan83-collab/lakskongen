Figma file and setup notes — Lakskongen

Figma file
- File key: Fo0ZgWpHXyGV2gggpgL3YK
- URL (provided by Fredrik): https://www.figma.com/design/Fo0ZgWpHXyGV2gggpgL3YK/Untitled?node-id=0-1

Summary of what I attempted
- Attempted to use the Figma REST API to edit the file (rename, create pages/frames, import assets).
- The Figma API is largely read-only for these kinds of edits from the public REST API; creating pages/frames and renaming the file is not supported by the API available to us here.
- Because of that limitation I prepared a detailed manual setup guide and the exact content to paste/create in Figma so a designer can perform the steps quickly and reproducibly.

Manual setup guide — exact steps to perform in Figma
1) Rename the file
   - Open the file URL above in Figma Web or Desktop.
   - Click the file title ("Untitled") in the top-left and rename to: "Lakskongen — Design System & Prototypes".

2) Create pages
   Create the following pages (in this order) using the left page panel + "+ Page":
   - 00_Tokens
   - 01_Components
   - 02_Screens
   - 03_Assets
   - 04_Prototypes

3) Populate 00_Tokens page (copy this content into text frames)
   - Create a top-level frame (name: Tokens / 00_Tokens) or leave as page-level sections.
   - Create three column frames: Colors, Spacing & Radii, Typography.

   Colors (create a vertical list of small color swatches with labels):
   - bg: #0F1720  (background)
   - panel: #121826  (panel backgrounds)
   - text: #E6EEF3  (main text)
   - muted: #9AA7B2  (muted text)
   - accent: #2AB7B5  (accent)
   - accent-2: #1F7A8C  (accent-2)
   - danger: #E06666  (danger)
   - colorblind-accent: #FFB000  (colorblind-friendly accent)

   Spacing & radii (create a simple list or scale diagram):
   - base unit: 8 px
   - small: 8 px
   - medium: 16 px
   - large: 24 px
   - xlarge: 40 px

   Radii:
   - sm: 6 px
   - md: 10 px
   - lg: 16 px

   Typography (include font stack, sizes & weights):
   - font stack: Inter, system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue'
   - sizes: xs 12, sm 14, md 16, lg 20, xl 24 (px)
   - weights: regular 400, medium 500, bold 700

   Icons (note): sizes supported: 16, 24, 32 px

   Suggested layer names & content to paste (example):
   - "Color / bg — #0F1720" (create a 120x40 rectangle filled with #0F1720 and a label to the right)
   - Repeat per color

4) Populate 03_Assets page (SVGs)
   - If you have SVG icons, place them in salmon-sim/design/assets/ locally and then in Figma do either:
     a) Drag & drop SVG files directly onto the 03_Assets page — each SVG will import as vector layers. Name each layer with the icon name (e.g. icon-play, icon-ship, icon-anchor).
     b) Select each imported SVG and choose "Create component" (right-click → Create component). Store them in a frame named "icons/16", "icons/24", "icons/32" or use auto-layout.
     c) For consistency, create a Components page (01_Components) and move the icon components there and publish as a library if needed.

   - If you want me to import programmatically, provide the assets/ folder with SVG files in the workspace (path: salmon-sim/design/assets/) or use the plugin included in this repo to import them from your Figma session.

5) Populate 02_Screens page (create frames for each screen)
   For each screen below create a Figma frame with the suggested size and inside it create labeled placeholder rectangles (use tokens colors):

   Common suggestions
   - Page background: use color token "bg" (#0F1720)
   - Panel backgrounds: use token "panel" (#121826)
   - Accent highlights/badges: use token "accent" (#2AB7B5) or "accent-2" (#1F7A8C)
   - Text: use token "text" (#E6EEF3) and muted text with "muted" (#9AA7B2)
   - Use radii: md (10px) for main panels, sm (6px) for buttons

   Frames to create (name each Frame exactly as the name shown):
   - Dashboard — suggested frame size: 1440 x 1024
     - Header bar: full width, height 72, panel color
     - Left nav: width 280, full height, panel color
     - Main canvas: remaining area, split into game view (left) and right-side stats panel
     - Bottom action bar: height 96

   - Processing Plant Builder — 1280 x 920
     - Toolbox/toolbar: left column (220px) panel
     - Canvas area: large grid area for placement (use a dark panel)
     - Properties panel: right column (320px)

   - Contract Manager — 1200 x 880
     - Contracts list: left column (360px) with list rows (muted text)
     - Contract detail: main area with contract terms and actions
     - Top filters/search: horizontal bar

   - WorkTab (crew priorities) — 1000 x 800
     - Crew list: left column
     - Priority board: kanban-style center
     - Details modal placeholder: floating rectangle (use accent color for CTA)

   - MapOverview (Norwegian coastline) — 1600 x 1200
     - Large map canvas filling frame (placeholder rectangle with panel color)
     - Top-right legend & controls (panel)
     - Bottom timeline/control bar

   - Financial Dashboard — 1280 x 900
     - KPI tiles across top (use panel tiles with accent highlights)
     - Timeseries chart area center
     - Cashflow table bottom

Programmatic helper plugin and local assets

- Added a Figma development plugin to automate these steps: salmon-sim/design/figma-plugin/ (files: manifest.json, code.js, ui.html). The plugin will create/append the pages 00_Tokens, 01_Components, 02_Screens, 03_Assets, 04_Prototypes, generate token frames, create screen placeholder frames, and import SVG files as components on the 03_Assets page.

- I copied available SVGs from the "processing" folder into: salmon-sim/design/assets/
  - build-factory.svg
  - contract-offer.svg
  - factory-dashboard.svg

How to use the plugin (Figma Desktop recommended):
1) Open the Figma file URL in Figma Desktop.
2) Menu → Plugins → Development → Import plugin from manifest... and select: C:\Users\freha\.openclaw\workspace\salmon-sim\design\figma-plugin\manifest.json
3) Run the plugin: Menu → Plugins → Development → Lakskongen — Populate Design File
4) In the plugin UI you can edit/paste the tokens JSON (pre-filled), choose SVG files to import (or leave blank to skip), then click "Populate Figma".
5) The plugin will append pages/frames and import the chosen SVGs as components on the 03_Assets page. When finished the plugin UI will display a JSON list of created node names and node IDs. Copy those node IDs into this file if you want them documented here.

Notes
- Direct creation of pages/frames via the public Figma REST API is not supported for these edits; running a plugin inside the Figma editor is the practical way to make these changes programmatically.
- I committed the plugin and assets to the local git branch 'feature/design-local' (commit created locally).

Next steps I can do for you:
- Push the feature/design-local branch to the remote and open a PR.
- Extend the plugin to also create basic components on 01_Components (buttons, inputs) automatically.
- Or, if you prefer manual edits, follow the manual steps above.

Timestamp: 2026-03-11 17:46:00 (Europe/Oslo)
