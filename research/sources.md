Research sources — growth models, FCR, and temperature effects (initial notes)

Purpose
- Collect empirical sources and notes to calibrate growth curves (von Bertalanffy / Gompertz), FCR sensitivity, and temperature (Q10) effects for Atlantic salmon.

Key sources (start here)
1) "A modified von Bertalanffy growth model dependent on temperature and body size" — ScienceDirect
   - URL: https://www.sciencedirect.com/science/article/abs/pii/S0025556417305503
   - Notes: Paper extends VBGF with temperature dependence. Useful for making k a function of temperature.

2) "On the exponent in the Von Bertalanffy growth model" — PMC (NCBI)
   - URL: https://www.ncbi.nlm.nih.gov/pmc/articles/PMC5756614/
   - Notes: Discussion of exponents and parameterization; good theory background for choosing powers when converting length->weight.

3) "Utilization of feed resources in the production of Atlantic salmon in Norway (2020)" — ScienceDirect
   - URL: https://www.sciencedirect.com/science/article/pii/S235251342200312X
   - Notes: Industry-focused resource retention and feed utilization figures. Use for calibrating realistic FCR and retention.

4) Seafish — Atlantic salmon feed profile
   - URL: https://www.seafish.org/responsible-sourcing/aquaculture-farming-seafood/species-farmed-in-aquaculture/aquaculture-profiles/atlantic-salmon/feed/
   - Notes: Practical FCR values cited (~1.1–1.2 typical). Good commercial benchmark.

5) "Effects of Water Temperature on Growth ... of Atlantic Salmon Parr" — PMC
   - URL: https://pmc.ncbi.nlm.nih.gov/articles/PMC12561879/
   - Notes: Temperature treatments and growth responses; supports using Q10-like modifiers.

Quick parameter guide (provisional)
- Typical FCR for farmed Atlantic salmon: 1.1–1.3 (use 1.2 as a baseline)
- von Bertalanffy asymptotic weight Linf: order 3000–5000 g (pick 4000 g as a working default)
- VB k: small daily values; literature often reports k in units of year^-1; converting to days gives ~0.002–0.01/day depending on life-stage. Use k≈0.0025/day as a conservative starting point.
- Q10: commonly 2.0 (doubling metabolic rate per +10°C); use Q10=2.0 as default for temperature scaling.

Notes & next steps
- I will add these sources to research/sources.md and reference them in PHASE1_NOTES.md.
- Next: calibrate parameters with numerical fits if we can find species-specific Linf/k from tagged growth studies or production datasets. I can try to extract numeric estimates from the literature (if PDFs accessible) and propose calibrated values.
- Also recommend adding a small CSV of empirical datapoints (age, weight) when we find open datasets so we can fit k and Linf programmatically.

Saved: salmon-sim/research/sources.md
