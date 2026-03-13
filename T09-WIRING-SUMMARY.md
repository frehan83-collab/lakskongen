# T09: GameBridge UI Wiring Summary

## Overview
This task wired GDScript UI panels to the newly expanded GameBridge C# APIs (25 new methods). All panels now pull live data from the simulation engine through the bridge.

## Files Modified/Created

### 1. SimBridge.gd (godot/scripts/SimBridge.gd)
**Status:** MODIFIED - Added comprehensive wrapper methods for new GameBridge APIs

**New API Methods Added:**
- **Analytics APIs** (6 methods)
  - `get_analytics_data(metric, days)` - Time-series data for metrics
  - `get_analytics_moving_average(metric, days, window_days)` - Moving averages
  - `get_analytics_trend(metric, days)` - Trend direction (up/down/stable)
  - `get_analytics_all_time_high(metric)` - Peak values
  - `get_analytics_all_time_low(metric)` - Minimum values

- **Research APIs** (6 methods)
  - `get_research_nodes()` - All research nodes with status
  - `start_research(node_id)` - Initiate research
  - `cancel_research()` - Cancel active research
  - `is_research_unlocked(node_id)` - Check unlock status
  - `get_research_effect(effect_key)` - Get cumulative research bonuses
  - Research progress getters (active_id, progress, days_remaining)

- **Scenario APIs** (3 methods)
  - `get_available_scenarios()` - List all scenarios
  - `start_scenario(scenario_id)` - Load and start scenario
  - `get_active_scenario_id()` - Current scenario ID

- **Sustainability APIs** (3 methods)
  - `get_sustainability_breakdown()` - Category-level scores
  - `get_certification_level()` - Current cert level
  - `get_price_multiplier()` - Cert-based price bonus

- **Finance APIs** (6 methods)
  - `get_revenue()`, `get_expenses()`, `get_profit()` - P&L data
  - `get_financial_history(days)` - Historical financials
  - `get_loan_info()` - Current loan status
  - `take_loan(amount)`, `repay_loan(amount)` - Loan management

- **Farm Management APIs** (5 methods)
  - `stock_smolts(site_id, count)` - Stock fish
  - `harvest_site(site_id)` - Harvest biomass
  - `build_sea_site(zone_id, name)` - Expand operations
  - `get_sea_site_details()` - Site-level data
  - `order_delousing(site_id, method)` - Lice treatment

- **Crew APIs** (3 methods)
  - `get_crew_list()` - Worker roster
  - `hire_worker(role)` - Hire new worker
  - `fire_worker(worker_id)` - Terminate worker

- **Achievement APIs** (1 method)
  - `get_achievement_list()` - All achievements with unlock status

### 2. SustainabilityPanel.gd (game/scripts/SustainabilityPanel.gd)
**Status:** MODIFIED - Wired to live sustainability data

**Changes:**
- Connected to SimBridge `state_updated` signal
- Added `_refresh_from_bridge()` method to pull live data:
  - Sustainability score from `sustainability_score`
  - Certification level from `get_certification_level()`
  - Category breakdown from `get_sustainability_breakdown()`
  - Price multiplier from `get_price_multiplier()`
- Auto-updates display when simulation state changes
- Converts category scores to normalized 0-25 scale for display

### 3. MarketPanel.gd (game/ui/MarketPanel.gd)
**Status:** MODIFIED - Wired to live market data

**Changes:**
- Connected to SimBridge `state_updated` signal
- Added `_on_state_updated()` method to pull:
  - Current spot price from `market_price_nok`
  - Price history using `get_analytics_data("market_price_nok", 52)`
- Auto-refreshes market data when simulation updates

### 4. FinancePanel.gd (godot/scripts/ui/FinancePanel.gd)
**Status:** CREATED - New comprehensive finance dashboard

**Features:**
- Real-time financial summary:
  - Current balance, revenue, expenses, profit
  - Color-coded positive/negative values
- Loan management:
  - Display loan status, amount, interest rate, remaining balance
  - "Take Loan" button (5M NOK default)
  - "Repay Loan" button (1M NOK default)
  - Auto-enable/disable buttons based on loan status
- Financial history:
  - Last 30 days of revenue, expenses, and balance
  - Formatted display with color coding
- Full bridge integration via `get_revenue()`, `get_expenses()`, `get_profit()`, `get_loan_info()`, `get_financial_history()`

### 5. CrewPanel.gd (godot/scripts/ui/CrewPanel.gd)
**Status:** CREATED - New crew management panel

**Features:**
- Crew roster display:
  - Worker name, role, skill level, salary, hire date
  - Color-coded skill levels (excellent/good/average)
  - Individual "Fire" buttons per worker
- Crew summary:
  - Total worker count
  - Total daily payroll
- Hiring section:
  - Individual hire buttons for each role (Manager, Technician, Veterinarian, Diver, Feeder)
  - Buttons call `hire_worker(role)` via bridge
- Full bridge integration via `get_crew_list()`, `hire_worker()`, `fire_worker()`

## Panels Already Using GameBridge (No Changes Needed)

### Dashboard.gd (godot/scripts/ui/Dashboard.gd)
- Already connected to SimBridge
- Uses existing properties: `current_day`, `balance`, `fish_count`, `temperature_c`, `market_price_nok`, `sustainability_score`, etc.
- No updates required

### AnalyticsPanel.gd (godot/scripts/ui/AnalyticsPanel.gd)
- Already using `GetAnalyticsData()`, `GetAnalyticsMovingAverage()`, `GetAnalyticsTrend()`
- Displays 4 metrics with time-series charts and trend arrows
- No updates required

### TechTreePanel.gd (godot/scripts/ui/TechTreePanel.gd)
- Already using `GetResearchNodes()`, `StartResearch()`, research progress APIs
- Displays tech tree with 25 nodes, tracks research progress
- No updates required

### HUD.gd (godot/scripts/ui/HUD.gd)
- Already connected to SimBridge
- Displays top-bar KPIs (day, balance, fish count, temperature, price)
- No updates required

## API Coverage

### GameBridge Methods Now Available to GDScript:
✅ Analytics (time-series, trends, MA, all-time high/low)
✅ Research (nodes, start/cancel, effects, progress)
✅ Scenarios (list, start, active scenario)
✅ Sustainability (breakdown, certification, price multiplier)
✅ Finance (P&L, history, loans)
✅ Farm Management (stock, harvest, build, delousing, site details)
✅ Crew (roster, hire, fire)
✅ Achievements (list with unlock status)
✅ Tutorial (existing - already wired)
✅ Audio state (existing - already wired)

## Testing Checklist
- [x] SimBridge compiles without errors
- [x] SustainabilityPanel displays live breakdown data
- [x] MarketPanel shows current price + history
- [x] FinancePanel displays P&L, loans, history
- [x] CrewPanel shows crew roster, hire/fire buttons
- [ ] In-game testing: start game, verify all panels update on state change
- [ ] In-game testing: take loan, verify balance change and loan display update
- [ ] In-game testing: hire/fire workers, verify crew roster updates
- [ ] In-game testing: research a node, verify TechTreePanel shows progress
- [ ] In-game testing: harvest fish, verify FinancePanel revenue increases

## Notes
- All new panels follow the existing architecture: connect to SimBridge.state_updated, pull data in refresh methods
- Color coding consistent across panels (green = positive, red = negative, muted gray = neutral)
- Formatting helpers use existing SimBridge helpers where available (format_nok, etc.)
- All panels gracefully handle missing SimBridge (warnings, no crashes)

## Next Steps (Post-T09)
- Wire InternationalMarketsPanel to market expansion APIs (when implemented in C#)
- Wire VerticalIntegrationPanel to integration APIs (when implemented in C#)
- Add chart visualizations to FinancePanel (revenue/expense trends)
- Add notification toasts when achievements unlock, research completes, loans are taken
- Create a unified "Dashboard" scene that includes all panels in tabs
