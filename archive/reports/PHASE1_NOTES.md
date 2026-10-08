Phase 1 - Core systems implemented (GDScript)

Implemented files:
- godot/scripts/data/Fish.gd: Fish data model with genetics, growth, mortality placeholders and serialization
- godot/scripts/Genetics.gd: Simple breeding and inheritance functions (Mendelian single-locus with mutation rate)
- godot/scripts/Hatchery.gd: Egg -> smolt progression, stocking functions, deterministic timing
- godot/scripts/TimeSystem.gd: Day advancement, time scaling and tick hooks
- godot/scripts/FeedManager.gd: Feed quality, FCR, and basic cost model
- godot/scripts/GameManager.gd: Orchestrates systems, exposes hooks for UI and autoload-ready APIs

Unit tests:
- tests/test_genetics.py: Python-based deterministic tests for Genetics inheritance and mutation
- tests/test_growth.py: Python deterministic tests for von Bertalanffy and Gompertz growth curve implementations

Notes & known issues:
- These are minimal, well-documented prototypes intended for integration into the Godot project. They are written in GDScript-compatible form but currently stored under godot/scripts since the repository uses mixed C#/Godot layout.
- No Godot project automated tests were run (Godot CLI not invoked). The Python tests cover deterministic genetics and growth behavior; Godot engine validation is still required.
- Git operations (branch, commit, push, PR) were not performed by this subagent due to environment constraints; please run the included git commands locally or request the main agent to perform them.

Research & calibration (added):
- Research sources with initial references and suggested parameter defaults saved at: research/sources.md
  - FCR baseline: 1.1–1.3 for Atlantic salmon (use 1.2 default)
  - VB Linf: ~3000–5000 g (use 4000 g default)
  - VB k (daily): ~0.002–0.01/day (use 0.0025/day default)
  - Q10 temperature factor: 2.0 (default)

Next steps:
- Integrate these GDScript files into the Godot project as autoload singletons (Project > Project Settings > Autoload).
- Add Godot unit tests using Godot's TestRunner and expand tests to growth, mortality, feed economics, and hatchery timing.
- Calibrate growth & FCR parameters using the literature and empirical datasets (I started collecting sources in research/sources.md).
- Implement UI hooks and debug scenes for interactive testing.

Files added:
- godot/scripts/data/Fish.gd
- godot/scripts/Genetics.gd
- godot/scripts/Hatchery.gd
- godot/scripts/TimeSystem.gd
- godot/scripts/FeedManager.gd
- godot/scripts/GameManager.gd
- tests/test_genetics.py
- tests/test_growth.py

If you want, I can now attempt to create a feature branch and commit these files and push a draft PR; confirm and provide git remote credentials/access if needed.
