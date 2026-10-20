Lakskongen-tune: growth & mortality calibration

Summary
- Added default von Bertalanffy (weight-based) parameters and a simple VB fitter script.
- Added unit tests that verify growth monotonicity and that chosen defaults fall within reasonable biological/industry ranges.

Sources & rationale
- von Bertalanffy model background and exponent for mass: On the exponent in the Von Bertalanffy growth model (PMC) https://www.ncbi.nlm.nih.gov/pmc/articles/PMC5756614/
- Industry FCR benchmarks: Seafish (FCR ~1.116) and Global Salmon Initiative (reports ~1.15) — used TypicalFCR = 1.15 (see https://www.seafish.org/ and https://globalsalmoninitiative.org/)
- Default Linf and k: chosen to reflect farmed Atlantic salmon reaching ~3–5 kg asymptotic weight (Linf_g=4000 g) and k chosen so that k*365 ≈ 0.9/year (k=0.0025 per day). These are typical order-of-magnitude values used in aquaculture literature and in tests.

Files added/changed
- scripts/vb_fit.py — heuristic fitter (CSV input: age_days, weight_g)
- src/LakskongenSim/Core/GameConstants.cs — added VB_Linf_g, VB_k_per_day, VB_t0_days, TypicalFCR; adjusted baseline mortality/growth defaults
- tests/test_constants.py — unit tests for parameter ranges
- existing tests/test_growth.py — left in place (monotonicity tests)

How to use
- Fit parameters from CSV: python scripts/vb_fit.py data.csv
- Defaults are loaded from GameConstants.cs by the simulation code.

Notes
- vb_fit.py is a simple heuristic (no external dependencies). For more robust fitting, consider adding numpy/scipy and implementing non-linear least squares (curve_fit).
