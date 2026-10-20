# F2-08/F2-09: tests replaced by stronger ones (`wave4/sim-sea-2`)

RUN_LOG §2 keeps the scoreboard add-only: a test is replaced only by a stronger
one, recorded here and in RUN_LOG §7. F2-08 fills the zone lice hook on purpose
(spec §3.3 "Lice and zones": an offshore unit's larval output counts in its
production area's fjord zone means, its fish never do), so the two tests that
pinned every zone's mean to the pre-move mean were rewritten; the facilities
save's continuation was re-pinned because the migrated unit now runs F2-08's
day. Every other test runs unchanged.

## 1. `FarmTypeHooksTests.ZoneLiceContribution_IsEachPopulationsFishTimesItsLice_AndTheZoneMeanIsThePreMoveMean`

Replaced by `ZoneLiceContribution_IsEachPopulationsFishTimesItsLice_AndTheZoneMeanAddsOnlyTheUnitsOutput` (same file).

| The old test asserted | Why it cannot stand | The new test asserts |
|---|---|---|
| every non-offshore population's share is its fish × its lice, bit for bit | the first population of each fjord zone in the unit's area now also carries the unit's larvae | every population but each zone's first: fish × lice bit for bit; the first population of a zone in the unit's area: its own term + 0.08 × the unit's fish × lice per fish, bit for bit (the fixture's unit must carry lice, so the term is not vacuous) |
| an offshore population's share is empty | unchanged | unchanged (the unit's fish never reach a fjord zone) |
| every zone's mean is the pre-move mean bit for bit | the unit's output is added to its area's zones on purpose | a zone outside the unit's area (or unknown) is the pre-move mean bit for bit; a zone in it equals the engine's own sum bit for bit, the closed form to 12 digits, and is never below the pre-move mean; zone fish counts unchanged; the unit's own water area's mean stays empty |

## 2. `SiteKindAuditTests.FjordZoneSystems_NeverSeeAnOffshoreUnitOrALandSite`

Replaced by `FjordZoneSystems_NeverSeeAnOffshoreUnitsFishOrALandSite` (same file, category `fjord-zone`; the audit table's row renamed).

| The old test asserted | Why it cannot stand | The new test asserts |
|---|---|---|
| every fjord zone's traffic-light mean equals the bare game's (no unit) | the unit's larvae now count in its area's means | every zone's fish equal the bare game's; its lice are the bare game's + 0.08 × the unit's output over the zone's fish (12 digits), strictly above the bare game's; with the unit's lice set to zero every zone's mean is the bare game's again, bit for bit |
| no fjord zone is the unit's, the land site's or the post-smolt site's; the unit's own area's mean is empty | unchanged | unchanged |
| the other per-zone systems (water load, bloom deck, orders, auctions, transfers) | unchanged | unchanged (the rest of the test is as before) |

## 3. `FarmTypeMoveTests.PreF2FacilitiesSave_Runs365Days_ExactlyAsBeforeTheMove` (re-pinned, not weakened)

The migrated unit runs F2-08's day (its lice split, the handling requirement
and the coastal lice order kept to the escalation, its incidents from the
day's u5-u7, zone-by-zone delousing), so the continuation's hash moves. The
test now pins:

- `F2_08Recording` (as-saved/events `96df902f8982747b`, as-saved/no-events
  `926992e6e80a6173`, contained/events `d5b85ea28b241fd8`): the whole
  continuation, every day and the save every 30 days;
- `UnmovedByF2_08Recording` (`6d9787e8e64ba9f0`, `d4aebb3286b0f754`,
  `34c42fccd1a5c492`): every day's weather and zone water and every stream but
  the unit's own, chained; recorded on F2-07's engine (`93c79c5`, the same
  serialization cadence) and on F2-08's alike;
- that both moved from F2-07's pins (`NotEqual`), so a later change cannot
  silently revert F2-08's day;
- and more than before: the unit's lice model runs split with its plot's
  multiplier on the outside part only, every escape in the mortality ledger
  belongs to an incident, and the unit never takes the fjord's forced delousing.

What F2-07's unmoved set held (`env`, `market`, `pop`, `lice`, `mtb`, `mort`,
`rivals`) and F2-08's does not: the populations, lice, capacity and mortality
groups (the unit's own fish are in them, and the fjord zones' lice means carry
its larvae), and the market and the rivals, which move in the contained
variant from day 138 (its zone near red, the unit's larvae in the zone's mean).
`env` and every stream but the unit's own are pinned as before.
