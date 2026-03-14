# Research: Mod Conflict & Versioning Best Practices

## Introduction
A robust modding framework must address two primary challenges to maintain game stability and a positive player experience:
1. **Mod Conflicts:** Ensuring that multiple mods can modify the same game data or behavior without breaking each other.
2. **Version Compatibility:** Managing the relationship between the game's core version, the modding API, and individual mods over time.

This document summarizes industry best practices and provides actionable strategies for the Lakskongen mod support framework (T35), specifically tailored for C# and the Godot Engine.

---

## 1. Loading Order and Priority Systems

### Dependency-Based Sorting (Directed Acyclic Graph)
The modern gold standard for loading mods is **Dependency Resolution**.
- **Mechanism:** Mods define `dependencies` (required) and `load_after`/`load_before` (optional) in their manifest (e.g., `mod.json`).
- **Implementation:** The mod loader builds a Directed Acyclic Graph (DAG) and uses a **Topological Sort** to determine the final load order.
- **Benefit:** Eliminates manual "load order" fiddling for users and ensures that mods which "patch" other mods always load later.

### Phase-Based Loading (The Factorio Model)
To allow mods to react to each other, the loading process should be split into distinct phases:
1. **Discovery:** Load all mod manifests and determine order.
2. **Registration (`data`):** Mods register their new items, fish types, or assets.
3. **Modification (`data-updates`):** Mods can modify data registered by *other* mods in the previous phase.
4. **Finalization (`data-final-fixes`):** A last-chance phase for global balancing or compatibility patches.

---

## 2. Data Conflict Resolution

### JSON Patching (Interoperability)
Avoid "Full File Overwrites." If two mods want to change the price of a Salmon, the one that loads last wins, and the other's changes are lost.
- **Strategy:** Use a **JSON Patch** approach (RFC 6902).
- **Example:** Instead of providing a new `salmon.json`, a mod provides a `salmon.patch` containing:
  ```json
  [
    { "op": "replace", "path": "/base_value", "value": 500 }
  ]
  ```
- **Benefit:** Multiple mods can patch different fields of the same object simultaneously.

### Namespacing and Prefixing
Collision between mod IDs is a common source of bugs.
- **Strategy:** Enforce a `mod_id` prefix for all registered content.
- **Pattern:** `mod_id:content_id` (e.g., `fancypack:golden_salmon`).
- **Benefit:** Prevents two different "More Fish" mods from both trying to register an item named `trout`.

### Merging Logic
When merging data structures:
- **Dictionaries:** Deep merge by default.
- **Arrays/Lists:** Provide options for `append`, `prepend`, or `replace` via metadata or patch operations.

---

## 3. Version Compatibility

### Semantic Versioning (SemVer)
Enforce **Major.Minor.Patch** versioning for both the game and mods.
- **Game Updates:** A Major update signals breaking API changes. A Minor update adds features but preserves backward compatibility.
- **Mod Dependencies:** Allow mods to specify version ranges (e.g., `requires: "api >= 1.2.0 < 2.0.0"`).

### API vs. Game Versioning
Decouple the **Modding API version** from the **Game version**.
- Even if the game's internal rendering logic changes (Game 1.4 -> 1.5), if the Modding Hooks for fish stats remain the same, the API version can stay at 1.0.0. This prevents unnecessary mod breakage.

### Backward Compatibility Layers
When breaking changes are unavoidable, provide a "Compatibility Singleton" that maps old field names or method calls to the new system, logging a deprecation warning.

---

## 4. Architectural Patterns (C# & Godot)

### The Registry Pattern (Godot-Friendly)
In Godot, centralize content in a `Registry` singleton.
- Instead of hardcoding fish in a scene, have a `FishRegistry` (C# Dictionary).
- Mods call `FishRegistry.Register(my_fish_resource)`.
- The game UI and simulation iterate over the registry to populate content.

### Event-Driven Hooks (Signals)
Leverage Godot **Signals** or C# **Events/Delegates**.
- **Pattern:** `Simulation.EmitSignal("fish_caught", fish_data)`.
- Mods connect to these signals to add logic (e.g., a "Stat Tracker" mod) without touching the core simulation code.

### Harmony (C# Runtime Patching)
For deep logic changes where hooks don't exist, use the **Harmony** library.
- **How it works:** It uses IL (Intermediate Language) manipulation to "wrap" original methods with `Prefix` and `Postfix` code at runtime.
- **Why it's great:** It allows multiple mods to patch the same method without overwriting each other's code.

### Godot PCK Loading
Godot's native modding relies on `.pck` or `.zip` files.
- **Caution:** Loading a PCK file *overwrites* files with the same path in `res://`. This is a "Full Overwrite" conflict.
- **Solution:** Reserve a specific path for mods (e.g., `res://mods/mod_name/`) and use a custom loader to handle data merging from these directories into the main game registry.

---

## 5. Actionable Insights for Lakskongen

1. **Adopt a Registry System:** Move all Salmon types, items, and building stats into a central registry that is populated at runtime.
2. **Implement JSON Patching:** Use a C# JSON library (like `Newtonsoft.Json.Patch` or `System.Text.Json` with custom logic) to handle data modifications.
3. **Manifests are Mandatory:** Every mod must include a `mod.json` with a unique ID, version, and dependency list.
4. **Hook-First Architecture:** Before a mod needs Harmony, provide explicit C# Events or Godot Signals for common simulation triggers.

---
*Created as part of T41 Research.*
