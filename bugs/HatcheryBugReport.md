# Hatchery Blocking Issue Report

## Issue
The integration simulation fails/blocks when advancing time through the Hatchery phase. Eggs fail to hatch properly when time is advanced incrementally.

## Root Cause
In `godot/scripts/Hatchery.gd`, the `incubate_eggs` function iterates over `eggs.duplicate(true)`. In Godot 4, `.duplicate(true)` creates a deep copy of the array and its contents. 
When `e.days_to_hatch -= days` is executed, it modifies the cloned dictionary rather than the original in the `eggs` array. The original eggs never reach 0 `days_to_hatch`, preventing them from hatching, and `eggs.erase(e)` fails to remove the actual reference.

## Proposed Fix
Iterate backwards through the array to safely modify and remove elements in-place:

```gdscript
func incubate_eggs(days: int = 1) -> void:
    for i in range(eggs.size() - 1, -1, -1):
        var e = eggs[i]
        e.days_to_hatch -= days
        if e.days_to_hatch <= 0:
            _hatch(e)
            eggs.remove_at(i)
```