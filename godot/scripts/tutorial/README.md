# The first season (roadmap U2-02)

Første sesong teaches the game one verb at a time: the card says why, the
pointer shows where, the player does it, and the step completes on what the
player did. Only Første sesong runs it (owner decision D19); skipping it is
allowed but is no win.

| Piece | File | Job |
|---|---|---|
| Steps | `godot/data/tutorial_steps.json` (embedded in the sim) | 18 steps: `kind` (do/wait), `pace` (hold/slow/run), `completionCondition`, `highlightTarget`, `unlocks` (tabs that open at the step). Text keys `TUTORIAL_<ID>_TITLE/_TEXT/_DO` |
| Conditions | `src/LakskongenSim/Core/TutorialConditions.cs` | The closed vocabulary: `ui:<mark>` or an engine state only the player's verb or the world produces. No day count, no condition true by itself; an unknown name fails the load |
| Engine | `src/LakskongenSim/Core/TutorialSystem.cs` | Current step, `Mark`, `CheckProgress`, `Skip` (saved, no win), tab gate, progress (growth, funding) |
| Bridge | `godot/scripts/autoload/GameBridge.Tutorial.cs` | `TutorialMark`, `TutorialCheck`, `GetTutorialStatus`, `GetTutorialSteps`, `IsTutorialTabUnlocked` (SimBridge: `tutorial_mark`, `tutorial_check`, `get_tutorial_status`, ...) |
| Card | `godot/scripts/ui/TutorialUI.gd`, `scenes/ui/TutorialUI.tscn` | Shows the step, one dot per step, the clock's pace (hold = `hold_clock("tutorial")`, slow = 1× once, run = the player's), "Vis meg", "Hopp over" (asks first) |
| Pointer | `TutorialTargets.gd`, `TutorialHighlight.gd` (this folder) | A target id to the control on screen now, or the way there (its tab, the site's row, the area's marker); a ring and chevron over the game that never takes a click |
| Tab gate | `godot/scripts/autoload/TutorialState.gd` | A tab in a step's `unlocks` opens at that step, only in Første sesong and only while "Vis opplæring" is on |

UI marks the screens send: `tab:<id>` (GameUI: rail button, Cmd+digit,
"Mer"), `inbox` (the bell or V), `clock` (Space, a speed, N),
`harvest_report` (the report shown), `map_area_home` (the company's own area
picked on the map).

Adding a step: add it to the json (numbered in order), its three text keys
(sorted) in `translations.csv`, its condition to the vocabulary if it is new
(a mark the screen sends, or an engine state a verb produces), its target to
`TutorialTargets.resolve` through a getter on the screen (never a node path),
and the verb to the minimal players (`src/LakskongenSim.Tests/Tutorial/TutorialPlayer.cs`,
`godot/tests/FirstSeasonFlowChecks.gd` follows the pointer by itself).

Tests: `src/LakskongenSim.Tests/Tutorial/FirstSeasonTutorialTests.cs` and
`godot/tests/FirstSeasonFlowChecks.gd` (run by `UiFlowTests`: the season won
by clicks and keys only).

`TutorialManager.gd` is an inert autoload kept for old callers; it does nothing.
