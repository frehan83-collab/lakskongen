# Achievement System Integration — 2026-03-13

**Sprint:** Achievement wiring + UI popup
**Engineer:** Jonas Karlsen (Senior Systems)

---

## Summary

Wired the existing `AchievementSystem` into `SimulationEngine` and plumbed the
unlock signal all the way through `GameBridge → SimBridge → AchievementPopup`.

---

## Changes

### `src/LakskongenSim/Core/SimulationEngine.cs`

- Added `public AchievementSystem Achievements { get; private set; }` property
- Added `public event Action<Achievement>? OnAchievementUnlocked` event
- `Initialize()`: constructs `new AchievementSystem(State)` and wires its
  `OnAchievementUnlocked` to re-fire on the engine
- `AdvanceOneDay()`: added **Phase 9 — ACHIEVEMENTS** after the analytics
  snapshot so every daily tick calls `Achievements.CheckAchievements()`

### `godot/scripts/autoload/GameBridge.cs`

- New signal: `[Signal] AchievementUnlockedEventHandler(name, description, rewardNok, tier)`
- Private fields: `_latestAchievementName`, `_latestAchievementDescription`
- `StartNewGame()` and `LoadGame()` both wire `SimulationEngine.OnAchievementUnlocked → HandleAchievementUnlocked`
- `HandleAchievementUnlocked()`: caches latest name/description, emits Godot signal
- New properties: `AchievementUnlockedCount`, `AchievementTotalCount`, `LatestAchievementName`, `LatestAchievementDescription`
- New method: `GetAchievementList()` → `Array<Dictionary>` with keys `id, name, description, tier, reward_nok, unlocked`

### `godot/scripts/SimBridge.gd`

- New signal: `achievement_unlocked(achievement_name, description, reward_nok, tier)`
- New vars: `achievement_unlocked_count`, `achievement_total_count`, `latest_achievement_name`, `latest_achievement_description`
- `_ready()`: connects `_game_bridge.achievement_unlocked → _on_achievement_unlocked`
- `_on_achievement_unlocked()`: updates local state, re-emits signal
- `_refresh_state()`: reads `AchievementUnlockedCount` / `AchievementTotalCount` from GameBridge on every tick

### `godot/scripts/ui/AchievementPopup.gd` *(new)*

- Procedurally-built `CanvasLayer` (layer 10) — no `.tscn` dependency
- Gold gradient banner anchored at top-centre; 560 × 140 px
- Shows: trophy emoji 🏆, "ACHIEVEMENT UNLOCKED" header, achievement name (22 pt),
  description, reward NOK, tier badge (colour-coded per tier)
- Dismiss button (×) in top-right corner
- Slide-down + fade-in on entry (0.4 s cubic ease-out)
- Auto-dismisses after **5 seconds** with fade-out (0.4 s cubic ease-in)
- Back-to-back unlocks handled cleanly (kills previous tween before showing next)
- Auto-connects to `SimBridge.achievement_unlocked` if SimBridge is present in `/root`

---

## Achievement count

The `AchievementSystem` ships with **27 registered achievements** across 6 tiers:

| Tier         | Count | Notes                                          |
|--------------|-------|------------------------------------------------|
| Beginner     | 4     | First 100 days basics                          |
| Novice       | 4     | Year 1–2 milestones                            |
| Intermediate | 5     | Year 2–5, multi-site, crisis survival          |
| Advanced     | 5     | Year 5–10, exports, vertical integration       |
| Master       | 5     | 10+ year elite milestones                      |
| Hidden       | 3     | Secret — perfect storm, speedrun, phoenix      |
| **Total**    | **27**|                                                |

Several conditions are stubbed (`return false // TODO`) for features not yet
implemented (FCR tracking, breeding values, ASC status, acquisitions). These will
fire naturally once the underlying systems are built.

---

## Integration guide

To activate the popup in a scene, either:

**Option A — Autoload** (recommended): add `AchievementPopup` as an autoload in
`project.godot`. The script self-connects to SimBridge on `_ready()`.

**Option B — Manual**: instantiate and add to your HUD, then connect manually:
```gdscript
var popup = preload("res://scripts/ui/AchievementPopup.gd").new()
add_child(popup)
SimBridge.achievement_unlocked.connect(popup.show_achievement)
```

---

## Testing checklist

- [ ] Advance past day 1 with a population — "First Smolt Release" should fire
- [ ] Advance to day 365 with positive balance — "Year One Survivor" fires
- [ ] Verify reward NOK is added to `State.Balance`
- [ ] Verify popup appears at top-centre with gold banner
- [ ] Verify popup auto-dismisses after 5 seconds
- [ ] Verify × button dismisses immediately
- [ ] Verify back-to-back unlocks don't stack broken tweens
- [ ] `SimBridge.achievement_unlocked_count` increments correctly
- [ ] `GameBridge.GetAchievementList()` returns all 27 entries with correct `unlocked` flags
