"""Three-way merge of a GDScript test file by top-level declaration.

Usage:
    python3 tools/merge/gd_decl_merge.py BASE OURS THEIRS OUT

Inside a conflicted `git merge` (the plan's hot-file rule, docs/geo/STAGE23_PLAN.md §4):
    f=godot/tests/UiFlowTests.gd
    git show :1:$f > /tmp/base.gd; git show :2:$f > /tmp/ours.gd; git show :3:$f > /tmp/theirs.gd
    python3 tools/merge/gd_decl_merge.py /tmp/base.gd /tmp/ours.gd /tmp/theirs.gd $f
    # then merge the _run() lines by hand, as the MANUAL report says.

A block is a top-level `func`, `static func`, `var`, `const`, `class`, `enum` or
`signal` (column 0, optionally after inline annotations such as `@onready`) with
the comment and annotation lines directly above it; it runs to the next block. The head is everything before the first block.

The output is OURS, plus:
- every block THEIRS added, inserted after the nearest block that precedes it in
  THEIRS and exists in the output (appended at the end when there is none);
- every block THEIRS changed from BASE, unless OURS changed it too.
_run() is never merged: when THEIRS changed it, its added and removed lines are
printed under MANUAL for the captain to apply by hand.

Exit 0 on a clean merge; exit 1 when a CONFLICT was reported (both sides changed
or added the same block differently, the head changed on both sides, or a side
declares a name twice); OUT is still written with OURS kept for each conflict,
so it can be resolved by hand. OUT may be the same path as OURS.
"""

from __future__ import annotations

import difflib
import re
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

DECL = re.compile(r"^(?:@\w+(?:\([^)]*\))?\s+)*(?:static\s+)?(?:func|var|const|class|enum|signal)\s+(\w+)")
MANUAL_ONLY = "_run"


@dataclass(frozen=True)
class Parsed:
    head: tuple[str, ...]
    blocks: tuple[tuple[str, tuple[str, ...]], ...]


@dataclass(frozen=True)
class MergeResult:
    text: str
    log: tuple[str, ...]
    conflicts: tuple[str, ...]


def _attached(line: str) -> bool:
    return line.startswith("#") or line.startswith("@")


def parse(text: str) -> Parsed:
    lines = text.split("\n")
    starts: list[tuple[int, str]] = []
    for i, line in enumerate(lines):
        m = DECL.match(line)
        if m:
            j = i
            while j > 0 and _attached(lines[j - 1]):
                j -= 1
            starts.append((j, m.group(1)))
    if not starts:
        return Parsed(tuple(lines), ())
    blocks = []
    for k, (s, name) in enumerate(starts):
        e = starts[k + 1][0] if k + 1 < len(starts) else len(lines)
        blocks.append((name, tuple(lines[s:e])))
    return Parsed(tuple(lines[: starts[0][0]]), tuple(blocks))


def _duplicates(label: str, parsed: Parsed) -> list[str]:
    counts = Counter(name for name, _ in parsed.blocks)
    dupes = sorted(name for name, n in counts.items() if n > 1)
    return [f"CONFLICT {label} declares a name twice: {dupes}"] if dupes else []


def merge(base: Parsed, ours: Parsed, theirs: Parsed) -> MergeResult:
    log: list[str] = []
    conflicts: list[str] = []
    for label, parsed in (("base", base), ("ours", ours), ("theirs", theirs)):
        conflicts += _duplicates(label, parsed)

    bmap = dict(base.blocks)
    omap = dict(ours.blocks)
    tnames = {name for name, _ in theirs.blocks}
    result = list(ours.blocks)
    order = [name for name, _ in result]

    for idx, (name, blk) in enumerate(theirs.blocks):
        if name == MANUAL_ONLY:
            if name in bmap and blk != bmap[name]:
                diff = [d for d in difflib.unified_diff(bmap[name], blk, lineterm="", n=0)
                        if d[:1] in "+-" and not d.startswith(("+++", "---"))]
                log.append(f"MANUAL {MANUAL_ONLY}() changed in theirs; apply by hand:")
                log.extend(f"  {d}" for d in diff)
            continue
        if name not in bmap and name not in omap:
            anchor = next((prev for prev, _ in reversed(theirs.blocks[:idx]) if prev in order), None)
            pos = order.index(anchor) + 1 if anchor else len(result)
            result.insert(pos, (name, blk))
            order.insert(pos, name)
            log.append(f"added {name} after {anchor}")
        elif name in bmap and blk != bmap[name]:
            if name in omap and omap[name] != bmap[name]:
                if omap[name] != blk:
                    conflicts.append(f"CONFLICT both changed {name} (ours kept)")
            elif name in omap:
                result[order.index(name)] = (name, blk)
                log.append(f"theirs changed {name}")
            else:
                conflicts.append(f"CONFLICT theirs changed {name}, ours deleted it (left deleted)")
        elif name not in bmap and name in omap and omap[name] != blk:
            conflicts.append(f"CONFLICT both added {name} differently (ours kept)")

    for name in bmap:
        if name not in tnames and name in omap:
            log.append(f"theirs deleted {name} (kept from ours)")

    head = ours.head
    if ours.head != base.head and theirs.head != base.head and ours.head != theirs.head:
        conflicts.append("CONFLICT header changed on both sides (ours kept)")
    elif theirs.head != base.head:
        head = theirs.head
        log.append("header from theirs")

    text = "\n".join(list(head) + [line for _, blk in result for line in blk])
    log.append(f"blocks {len(result)}")
    return MergeResult(text=text, log=tuple(log), conflicts=tuple(conflicts))


def main(argv: list[str]) -> int:
    if len(argv) != 4:
        print(__doc__, file=sys.stderr)
        return 2
    base_p, ours_p, theirs_p, out_p = argv
    base, ours, theirs = (parse(Path(p).read_text(encoding="utf-8")) for p in (base_p, ours_p, theirs_p))
    result = merge(base, ours, theirs)
    Path(out_p).write_text(result.text, encoding="utf-8", newline="")
    for line in result.log + result.conflicts:
        print(line)
    return 1 if result.conflicts else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
