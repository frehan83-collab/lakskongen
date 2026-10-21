"""Three-way merge of godot/translations/translations.csv by key.

Usage:
    python3 tools/merge/translations_merge.py BASE OURS THEIRS OUT

Inside a conflicted `git merge` (the plan's hot-file rule, docs/geo/STAGE23_PLAN.md §4):
    f=godot/translations/translations.csv
    git show :1:$f > /tmp/base.csv; git show :2:$f > /tmp/ours.csv; git show :3:$f > /tmp/theirs.csv
    python3 tools/merge/translations_merge.py /tmp/base.csv /tmp/ours.csv /tmp/theirs.csv $f

The file's older order (up to and including the anchor row) follows OURS; every
row after the anchor, plus every key only THEIRS has, becomes one block sorted
by key. Values merge three-way per key (a key one side deleted and the other
left alone is deleted). A key both sides changed differently, or added with
different values, is printed as CONFLICT and nothing is written (exit 1). OUT
may be the same path as OURS: every input is read before anything is written.
Rows are written with Python's csv module (QUOTE_MINIMAL, "\\n" line ends),
which round-trips the shipped file byte for byte.
"""

from __future__ import annotations

import csv
import io
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

ANCHOR = "CAPACITY_REASON_SITE_NOT_FOUND"


class MergeInputError(ValueError):
    """An input file the merge cannot trust (duplicate keys, bad rows, no anchor)."""


@dataclass(frozen=True)
class Table:
    header: tuple[str, ...]
    order: tuple[str, ...]
    rows: dict[str, tuple[str, ...]]


@dataclass(frozen=True)
class MergeResult:
    text: str
    conflicts: tuple[tuple[str, tuple | None, tuple | None, tuple | None], ...]
    head: int
    tail: int
    ours_only: int
    theirs_only: int


def parse(text: str, label: str) -> Table:
    rows = list(csv.reader(io.StringIO(text)))
    if not rows:
        raise MergeInputError(f"{label}: empty file")
    header, body = tuple(rows[0]), rows[1:]
    bad = [i + 2 for i, r in enumerate(body) if len(r) != len(header)]
    if bad:
        raise MergeInputError(f"{label}: rows {bad[:10]} do not have the header's {len(header)} columns")
    order = tuple(r[0] for r in body)
    dupes = sorted(k for k, n in Counter(order).items() if n > 1)
    if dupes:
        raise MergeInputError(f"{label}: duplicate keys {dupes[:10]}")
    return Table(header, order, {r[0]: tuple(r) for r in body})


def merge(base: Table, ours: Table, theirs: Table, anchor: str = ANCHOR) -> MergeResult:
    if ours.header != theirs.header:
        raise MergeInputError(f"headers differ: ours {ours.header} theirs {theirs.header}")
    if anchor not in ours.order:
        raise MergeInputError(f"ours has no anchor row {anchor}")

    merged: dict[str, tuple[str, ...]] = {}
    conflicts = []
    for key in sorted(set(ours.rows) | set(theirs.rows) | set(base.rows)):
        o, t, b = ours.rows.get(key), theirs.rows.get(key), base.rows.get(key)
        if o == t:
            val = o
        elif o == b:
            val = t
        elif t == b:
            val = o
        else:
            conflicts.append((key, b, o, t))
            val = o
        if val is not None:
            merged[key] = val

    cut = ours.order.index(anchor) + 1
    head = [k for k in ours.order[:cut] if k in merged]
    head_set = set(head)
    tail = sorted(k for k in merged if k not in head_set)

    out = io.StringIO()
    writer = csv.writer(out, lineterminator="\n")
    writer.writerow(ours.header)
    for key in head + tail:
        writer.writerow(merged[key])
    return MergeResult(
        text=out.getvalue(),
        conflicts=tuple(conflicts),
        head=len(head),
        tail=len(tail),
        ours_only=len(set(ours.rows) - set(theirs.rows)),
        theirs_only=len(set(theirs.rows) - set(ours.rows)),
    )


def _read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def main(argv: list[str]) -> int:
    if len(argv) != 4:
        print(__doc__, file=sys.stderr)
        return 2
    base_p, ours_p, theirs_p, out_p = argv
    try:
        base = parse(_read(base_p), "base")
        ours = parse(_read(ours_p), "ours")
        theirs = parse(_read(theirs_p), "theirs")
        result = merge(base, ours, theirs)
    except MergeInputError as err:
        print(f"ERROR {err}", file=sys.stderr)
        return 2
    if result.conflicts:
        for key, b, o, t in result.conflicts:
            print(f"CONFLICT {key}\n  base   {b}\n  ours   {o}\n  theirs {t}")
        print(f"{len(result.conflicts)} conflict(s); nothing written")
        return 1
    Path(out_p).write_text(result.text, encoding="utf-8", newline="")
    print(f"keys={result.head + result.tail} head={result.head} tail={result.tail} "
          f"ours_only={result.ours_only} theirs_only={result.theirs_only}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
