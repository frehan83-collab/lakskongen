# F2-06/F2-07: tests replaced by stronger ones (`wave4/sim-sea-1`)

RUN_LOG §2 keeps the scoreboard add-only: a test is replaced only by a stronger
one, recorded here and in RUN_LOG §7. F2-06's wiring and F2-07's offshore unit
change the offshore unit's day on purpose (its open sea, its own day stream,
its logistics and its own ledger lines), so three tests that pinned the pre-F2
unit's day were rewritten. Every other test runs unchanged.

## 1. `OffshoreUnitTests.TheUnitsDay_BooksItsCrewEnergyAndCarbon_OnTheUnit`

Replaced by `TheUnitsDay_BooksItsLinesCrewChangesAndCarbon_OnTheUnit` (same file).

| The old test asserted | Why it cannot stand | The new test asserts |
|---|---|---|
| `OffshoreUnit.TickDay(...)` returns crew + 80,000 NOK energy + MTB maintenance + supply runs as `TotalDailyOPEX` | F2-07 removes the pre-F2 `TickDay` and its constants; the unit books its own lines in the engine (`RunOffshoreUnitDay`) from `facilities.offshore.costs` | after one engine day, each of `offshore_crew`, `offshore_energy`, `offshore_maintenance`, `offshore_insurance` is exactly its calibrated formula (crew 12 × 900,000 NOK a year; diesel litres × 7.5 NOK; 2.5 % and 0.8 % of capex a year), and none of it goes into the fjord's `operations` line |
| 4 crew rotations in 28 days at a flat 150,000 NOK | crew changes are now bookings on the Driftsplan rota, done only on a day the sea allows the boat (or by helicopter when overdue) | 120 days of the default rota: 12-18 changes done, each charged its vessel's day rate × days chartered (or the helicopter's price), no crew over the interval + 14 days, and the `offshore_charter` line holds at least their sum |
| one fuel entry a day on the unit's own tracker, a vessel entry, fuel count + 10 after 10 days | unchanged | unchanged (the same three assertions) |
| a 13 m day locks the crew in and does 0.05 structural damage (the pre-F2 12 m rule) | F2-07's structure fatigues by the spec's coefficient × (Hs / design Hs)³ every day | a forced 13 m day locks the unit down and its structure loses exactly the coefficient × (13 / design Hs)³ |

## 2. `SiteKindAuditTests.FjordEconomics_TheUnitPaysAndEmitsOnlyItsOwn` (the night-shift half)

The old test asserted that a game with the offshore unit and one without it
hold **equal** stream positions after 60 nights of the night-shift rota. With
F2-07 the unit's game holds two more streams of its own, `offshore_metocean`
(the open sea, from the first offshore unit) and `offshore_farm/<unit>` (the
unit's day, eight uniforms), so the two sets can no longer be equal. The new
assertions:

- every stream of the game without the unit equals the same stream of the game
  with it (the equality the old test held, over every shared stream);
- the unit's game has no stream beyond those two of its own (the filter is by
  exact name: `offshore_metocean` and `offshore_farm/<unit id>`);
- those two exist (`Assert.NotEmpty`) and 60 nights of the rota moved neither
  (new: the old test could not see a night shift that drew for the unit from a
  stream the fjord game lacks).

The day's crew and upkeep, the fuel and power entries and the night log are
asserted as before.

## 3. `FarmTypeMoveTests.PreF2FacilitiesSave_Runs365Days_ExactlyAsBeforeTheMove` (3 variants)

The old test pinned the pre-F2 facilities save's 365-day continuation to
`U2_03Recording` and re-verified each wave-3 item's drift as a save shape only
(`AsU2_04Wrote` → `U2_04Recording`, `AsF2_05Wrote` → `F2_05Recording`,
`AsF2_04Wrote` → `F2_04Recording`; the last one asserts an empty Driftsplan).
F2-06/F2-07 change the migrated unit's days on purpose: it is commissioned on
load with a full silo and its five Driftsplan defaults, takes off_1's sea every
day, draws its eight uniforms a day, and books its lines and charters. The
whole-save chain therefore moves and the rewrite chain (which proved earlier
items' claims on their own engines) can no longer be reproduced.

| Pin | Before | Now |
|---|---|---|
| whole continuation | `U2_03Recording` 04ed7001cefa3469 / f03695d59d7cce0f / fef57de957a6a41d (equal) | `F2_07Recording` 33753d12f7c40deb / 27dd7e4251d36b48 / 3d66542efa3911bf (equal); every earlier recording asserted unequal (history) |
| what F2-07 must not move | (none) | `UnmovedByF2_07Recording` f208f1c356f3a0ba / f6f34ab862ade197 / 2b152a90b3db43f4: every day's `env`, `market`, `pop`, `lice`, `mtb`, `mort` and `rivals` groups and every stream but the unit's own, chained over the 365 days. Recorded with the same code on `4f927a1` (the engine of `U2_03Recording`) and on this branch: identical, so F2-06/F2-07 move only cash, the ledger, the operations group (the unit's nets and structure) and the unit's own streams |
| the unit | id, plot, fish in its zones, carbon on its tracker | the same, plus: commissioned; 0 silo-empty days; 5 Driftsplan entries; crew changes done within 365/7 − 2 … 365/7 + 1 (51 measured); net inspections 365/30 ± 1 (12 measured); at least one resupply in the two variants with events (the no-events year's 450 t never bring the 700 t silo to its trigger); its day stream drew exactly 8 × 365 = 2,920 uniforms; the game holds its open sea |
| two runs | whole hash equal | whole hash and unmoved hash equal |

The unmoved chain is computed after the run's 30-day saves, as the whole chain
is: `PrepareForSave` moves traced state, so a chain without the saves differs
(measured: ee53b37346641b2b etc. without them, on both commits alike).
