# WAVE 4 TASKS - Lakskongen Salmon-Sim

**Author:** Lars Pettersen, Code Reviewer / QC Lead
**Date:** March 11, 2026

## 1. High-Priority Bug Fixes (From QC)
- [ ] Fix null reference vulnerabilities in `LogisticsManager.cs` (handle missing vehicle capacity gracefully without throwing `InvalidOperationException`).
- [ ] Improve JSON error handling in `SaveManager.cs` for corrupted files.
- [ ] Add explicit validation to `Market.gd` prior to querying missing dictionary keys for `margin_multiplier`.
- [ ] Refactor hardcoded resource paths in `AudioManager.cs` to use configuration or exported Node variables.
- [ ] Add `try/catch` and safe-fallbacks for missing references across Godot UI nodes (e.g., `Dashboard.gd`).

## 2. New Features Needed
- [ ] **Sound Design & Ambient Audio:**
  - Create layered audio manager in Godot (coastal ambient noises, UI click responses, alarm states).
- [ ] **Advanced AI Storyteller Tuning:**
  - Introduce configurable cooldowns in `Storyteller.cs`.
  - Connect disease outbreaks to environmental factors (water quality, temp spikes).
- [ ] **Multiplayer Prep:**
  - Separate state transitions from rendering. Abstract `GameState` so it can be synchronized over network packets in Godot.
- [ ] **Save/Load UI:**
  - Build UI bindings for `SaveManager.cs`. Allow players to manage multiple save slots, autosaves, and overwrite warnings.
- [ ] **Settings Menu:**
  - Expose graphical, audio, and gameplay settings in a cohesive `Settings.tscn` overlay.

## 3. Polish Items
- [ ] **Animations & Particle Effects:**
  - Implement feed dispersal particle systems above the sea pens.
  - Add simple leap animations for jumping salmon based on `FishPopulation` density.
- [ ] **UI Transitions:**
  - Improve the market chart updating. Transition values smoothly rather than instant snaps.
  - Implement smooth fade-in/out for the Tutorial Overlays and Toast notifications.
- [ ] **Event Logging:**
  - Differentiate event severity in the UI using distinct colors/icons.

## 4. Performance Optimization Targets
- [ ] **C# -> Godot Marshalling:**
  - Minimize JSON overhead when serializing large `FishPopulation` states across the `GameBridge`.
  - Shift towards delta updates (only sending modified cohorts) rather than full state synchronization every tick.
- [ ] **UI Node Re-instantiation:**
  - Pool UI components in the `MarketPanel` and `EventLog` rather than constantly creating `Label.new()` on updates.
- [ ] **Lice Spread Computations:**
  - Optimize the cross-infection nested loops in `SeaLiceModel.cs` for large numbers of pens.