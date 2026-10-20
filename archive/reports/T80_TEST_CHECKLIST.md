# T80: PauseMenu + SaveLoadMenu - Test Checklist

## Pre-Test Setup
- [ ] Open Godot 4 project: `salmon-sim/godot/`
- [ ] Build C# solution (Build → Build Solution or F6)
- [ ] Ensure no errors in Output console
- [ ] Run game (F5 or Play button)

## Basic PauseMenu Tests
- [ ] **ESC opens PauseMenu**: Press ESC in-game → PauseMenu should appear
- [ ] **ESC closes PauseMenu**: Press ESC again → PauseMenu should close
- [ ] **Resume button works**: Click "Fortsett spillet" → Menu closes, game resumes
- [ ] **Game pauses**: When PauseMenu is open, check that simulation freezes (day counter stops)
- [ ] **Game resumes**: When PauseMenu closes, check that simulation continues

## SaveLoadMenu Tests (Save Mode)
- [ ] **Open Save Mode**: PauseMenu → "Lagre spill" → SaveLoadMenu opens
- [ ] **Create New Save**: Click "New Save" → Save is created with timestamp name
- [ ] **List shows save**: New save appears in the list with metadata (day, cash, date)
- [ ] **Overwrite Save**: Select existing save → Click "Overwrite" → Save is updated
- [ ] **Close Save Menu**: Click "Close" → Returns to PauseMenu

## SaveLoadMenu Tests (Load Mode)
- [ ] **Open Load Mode**: *(If Load button exists)* PauseMenu → "Last inn spill" → SaveLoadMenu opens
  - **Note**: Load button may not exist in current PauseMenu.tscn
- [ ] **List shows saves**: All save files appear with metadata
- [ ] **Load Save**: Select save → Click "Load" → Game loads from that save point
- [ ] **Verify Load**: Check that day, balance, and game state match the saved state

## SaveLoadMenu Tests (Delete)
- [ ] **Delete Save**: In SaveLoadMenu, select save → Click "Delete" → Save is removed
- [ ] **Confirm Deletion**: Save no longer appears in list
- [ ] **Empty State**: Delete all saves → Should show "No save files found"

## Edge Cases
- [ ] **ESC in SaveLoadMenu**: Press ESC while SaveLoadMenu is open → Menu closes, returns to PauseMenu
- [ ] **Multiple Opens/Closes**: Open/close PauseMenu multiple times → No crashes or glitches
- [ ] **Save During Pause**: Verify saving works while game is paused
- [ ] **Load During Pause**: Verify loading works while game is paused
- [ ] **DateTime Display**: Check that save times display correctly (format: YYYY-MM-DD HH:MM)
- [ ] **Cash Display**: Check that cash amounts format correctly (K/M suffix for thousands/millions)

## Integration Tests
- [ ] **SaveLoadManager Connection**: Check Output for "SaveLoadManager initialized" message
- [ ] **SimBridge Connection**: Check Output for "SimBridge: connected to GameBridge (C#)" message
- [ ] **No Errors**: Check that no errors appear in Output console during save/load operations

## UI/UX Tests
- [ ] **Button Focus**: Tab through buttons → Focus navigation works correctly
- [ ] **Tooltips**: Hover over buttons → Tooltips appear (if implemented)
- [ ] **Visual Feedback**: Buttons change appearance on hover
- [ ] **Animation**: PauseMenu has smooth fade-in/scale animation
- [ ] **Overlay**: Semi-transparent overlay appears behind PauseMenu

## Settings Button Test
- [ ] **Settings Button**: Click "Innstillinger" → *(May not work yet if SettingsMenu doesn't exist)*
  - **Expected**: Either opens SettingsMenu or does nothing (gracefully)
  - **Not Expected**: Crash or error

## Quit Tests
- [ ] **Quit to Menu**: Click "Tilbake til hovedmeny" → Returns to MainMenu scene
  - **Warning**: Unsaved progress should be lost (no confirmation yet)
- [ ] **Quit Game**: *(If Exit button exists)* Click exit button → Game closes
  - **Note**: Exit button may not exist in current PauseMenu.tscn

## Regression Tests (Verify Existing Functionality)
- [ ] **Game Starts**: Game starts normally from MainMenu
- [ ] **HUD Updates**: HUD shows correct day, balance, fish count, etc.
- [ ] **Simulation Runs**: Simulation advances when not paused
- [ ] **Other UI**: Dashboard, PenManagement, and other UI elements still work

## Error Cases
- [ ] **No Saves**: Try to load when no saves exist → Should show "No save files found"
- [ ] **Corrupted Save**: *(If possible)* Manually corrupt a save file → Should show error, not crash
- [ ] **Missing SaveLoadManager**: *(Dev test)* Disable SaveLoadManager autoload → Should show warning, not crash
- [ ] **Missing SimBridge**: *(Dev test)* Disable SimBridge → Should show warning, not crash

## Performance Tests
- [ ] **Many Saves**: Create 20+ saves → Menu should still load quickly
- [ ] **Large Save Files**: Advance game many days → Large save files should load/save without lag
- [ ] **Menu Responsiveness**: PauseMenu should open/close instantly (< 0.2s)

## Known Issues / Expected Failures
- **Load Button**: May not exist in PauseMenu.tscn (only Resume, Save, Settings, Quit exist)
- **Exit Button**: May not exist in PauseMenu.tscn
- **SettingsMenu**: Not implemented yet, button may do nothing
- **Confirmation Dialogs**: No confirmation for overwrite/delete/quit yet (immediate actions)
- **Save Thumbnails**: Not implemented (basic text list only)

## Test Results Log
Date: ___________
Tester: ___________

| Test | Pass | Fail | Notes |
|------|------|------|-------|
| ESC opens PauseMenu | ☐ | ☐ | |
| ESC closes PauseMenu | ☐ | ☐ | |
| Game pauses/resumes | ☐ | ☐ | |
| Create new save | ☐ | ☐ | |
| Overwrite save | ☐ | ☐ | |
| Load save | ☐ | ☐ | |
| Delete save | ☐ | ☐ | |
| Quit to menu | ☐ | ☐ | |
| No crashes | ☐ | ☐ | |
| Metadata displays correctly | ☐ | ☐ | |

## Notes:
_Add any additional observations, bugs, or issues here_

---

**Status**: Ready for Testing
**Completion**: All wiring complete, awaiting manual testing in Godot
