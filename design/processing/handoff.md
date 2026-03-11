Design Handoff: Processing Facility UX & Interaction Spec

Overview
- Purpose: UI/UX flows and mock screens for the Processing facility in Salmon Sim. Covers: build factory, upgrade capacity, configure product lines, buying/selling policies, view P&L and utilization, and contract workflows.
- Deliverables:
  - Mock screens (SVG): build-factory.svg, factory-dashboard.svg, contract-offer.svg
  - Interaction spec for contracts included below
  - Component-token mapping

1) User Flows (high level)
- Build Factory
  1. Player opens Processing > "Build Factory".
 2. Choose site (map/list), set factory type (smolt, processing, cold storage), select capacity tier.
 3. Preview cost, build time, expected throughput and required workforce.
 4. Confirm -> construction timer starts, resources reserved.

- Upgrade Capacity
 1. From Factory Dashboard, click "Upgrade".
 2. Select target tier; shows cost, downtime, throughput delta, ROI estimate.
 3. Schedule now or plan (defer). Confirm.

- Configure Product Lines
 1. Dashboard > Product Lines.
 2. Add product line: choose feedstock, recipe (filleting, smoking, packing), target quality settings.
 3. Assign machines and workforce. Save.

- Buying/Selling Policies
 1. Market > Policies. Set auto-buy rules (min stock, max price), auto-sell rules (min margin, preferred buyers).

- View P&L and Utilization
 1. Dashboard shows: daily/weekly revenue, costs (raw material, labor, energy), margins, utilization heatmap per machine.
 2. Export report (CSV/PDF).

2) Contract Interaction Spec
- Actors: Buyer, Seller (player factory), Broker
- Contract creation (seller offers)
  - Fields: Product, Quality grade, Quantity (kg), Price per kg, Delivery window, Penalties (late, quality), Logistics (pickup/dropoff), Deposit (%), Expiry.
  - UI: "Create Contract" modal with inline validation (price within market range), quality selector with tooltips describing grade.
  - After create: Offer moves to "Offers" list. Buyers can Accept/Counter/Decline.

- Accept/Decline/Counter
  - Accept: creates a binding contract. Schedule logistics: choose carrier, pickup date within delivery window, generate packing list.
  - Counter: propose new price or delivery; original offer returns to proposer for response.

- Logistics Scheduling
  - When contract confirmed, open Logistics tab: choose available carriers (with ETA and cost), schedule pickup, assign to a truck. Track status: Scheduled -> In Transit -> Delivered.

- Failure modes & resolution
  - Late delivery: apply penalty, optionally auto-compensate.
  - Quality disputes: initiate inspection workflow; hold payment until resolved.

3) Mock screens (files created alongside this doc)
- build-factory.svg: site selection, tier picker, cost preview
- factory-dashboard.svg: KPIs (P&L), utilization, product lines
- contract-offer.svg: create contract modal prototype

4) Component mapping to design tokens
- Color palette: primary #0077B6 (actions), accent #00B4D8, warn #FF6B6B, success #2DD4BF, bg #F8FAFC, surface #FFFFFF, text primary #0F172A
- Spacing: s-8, m-16, l-24, xl-32
- Typography: Title/24px/700, H2/18px/600, Body/14px/400, Mono/13px
- Components:
  - Button (primary/secondary/ghost)
  - Card (surface, elevation-1)
  - Modal (centered, max-width 720px)
  - Table (striped, sortable)
  - KPI Tile (value + sparkline)

5) Handoff notes for engineers
- All monetary values show currency in locale.
- Use optimistic UI for contract actions but reconcile with server.
- Expose hooks for scheduling API (carriers, ETA) and dispute resolution.

6) Files saved
- salmon-sim/design/processing/handoff.md
- salmon-sim/design/processing/build-factory.svg
- salmon-sim/design/processing/factory-dashboard.svg
- salmon-sim/design/processing/contract-offer.svg

If you want additional variants (mobile, dark mode) or PNG exports, tell me and I'll generate them.
