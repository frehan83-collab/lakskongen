# TODO Registry — Lakskongen
**Last updated:** 2026-03-24 14:20 CET

All TODOs are tracked here. None are blocking for alpha release.

## Finance Integration (2)
| File | Line | TODO | Priority |
|------|------|------|----------|
| ModLoader.cs | 403 | Route through FinanceManager in closure | Low |
| NorwegianEvents.cs | 13 | Refactor to use FinanceManager when EventDefinition updated | Low |

## Simulation Placeholders (2)
| File | Line | TODO | Priority |
|------|------|------|----------|
| SimulationEngine.cs | 1568 | Calculate global market share | Medium |
| SimulationEngine.cs | 1578 | Track retail brand | Medium |

## Data Layer Expansion (2) — PARTIALLY RESOLVED
| File | Line | TODO | Priority | Status |
|------|------|------|----------|--------|
| RealDataOrchestrator.cs | 83 | Add Mareano loader for bathymetry | Medium | Open |
| RealDataOrchestrator.cs | 84-85 | Add NMDC + Copernicus loaders | Medium | Open |

**✅ RESOLVED in Phase 12:**
- SalinityLayer, OxygenLayer, CurrentLayer — all created and wired into RealDataOrchestrator
- Tutorial stubs (research_project_started, crew_hired) — implemented with real conditions

## Interpolation (1)
| File | Line | TODO | Priority |
|------|------|------|----------|
| TemporalInterpolator.cs | 59 | Implement proper seasonal decomposition | Low |

## Summary
- **Total:** 7 TODOs (was 11)
- **Resolved:** 4
- **Blocking:** 0
- **High priority:** 0 (was 1)
- **Medium:** 4
- **Low:** 3
