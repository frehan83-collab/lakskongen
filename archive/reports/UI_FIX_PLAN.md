# T60 UI Fix Plan

## Summary
- **Total files**: 26
- **Total issues**: 159
- **Strategy**: Focus on high-impact fixes that ensure consistency

## Fix Categories

### 1. Hardcoded Strings → tr()
**Impact**: High (localization broken)
**Effort**: Medium (need to add keys to strings.csv)

### 2. Missing Tooltips
**Impact**: Medium (accessibility/UX)
**Effort**: Low (just add tooltip_text)

### 3. Missing Keyboard Nav
**Impact**: High (accessibility)
**Effort**: Medium (add _input handlers + Esc support)

## Priority Tiers

### Tier 1: Core UI (MUST FIX)
- Dashboard.gd
- MainMenu.gd
- SettingsMenu.gd (already decent)
- SaveLoadMenu.gd
- HUD.gd
- PauseMenu.gd

### Tier 2: Feature Panels (SHOULD FIX)
- FinancePanel.gd
- ProductionChainPanel.gd
- TechTreePanel.gd
- MapPanel.gd
- ContractsPanel.gd
- SustainabilityPanel.gd

### Tier 3: Support Panels (NICE TO FIX)
- AnalyticsPanel.gd
- CrewPanel.gd
- GeneticsPanel.gd
- EncyclopediaPanel.gd
- DataExportPanel.gd
- ScenarioSelectPanel.gd
- ModManagerPanel.gd
- TutorialUI.gd

### Tier 4: Notifications (LOW PRIORITY)
- EventLog.gd
- NotificationCenter.gd
- AchievementPopup.gd
- AchievementNotification.gd
- ToastNotification.gd
- CreditsScreen.gd

## Implementation Approach

### Phase 1: Add missing tr() keys to strings.csv
Batch add all needed localization keys (Norwegian + English minimum)

### Phase 2: Fix core panels (Tier 1)
- Add keyboard nav (_input + ui_cancel)
- Add tooltips
- Replace hardcoded strings with tr()

### Phase 3: Fix feature panels (Tier 2)
Same pattern as Phase 2

### Phase 4: Polish support panels (Tier 3)
Focus on most visible issues

### Phase 5: Skip notifications (Tier 4)
These are transient UI - less critical

## Estimated Time
- Phase 1: 30-45 min (batch string work)
- Phase 2: 60-90 min (6 files, thorough fixes)
- Phase 3: 60-90 min (6 files, thorough fixes)  
- Phase 4: 30-45 min (selective fixes)
- **Total**: ~3-4 hours for comprehensive fix

## Practical Execution (Given Time Constraints)
Focus on **Phase 1 + Phase 2** for maximum impact.
Document remaining work for future iteration.
