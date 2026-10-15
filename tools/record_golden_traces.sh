#!/usr/bin/env bash
#
# Record the golden traces (FARM_TYPES_SPEC F2-01) of any commit and diff them.
#
# A commit is recorded in a temporary git worktree (detached), built there and
# run from there, so the working tree is never touched, uncommitted changes are
# never recorded and the data files (godot/data) are that commit's own. The
# recorder is that commit's src/StateHashProbe (golden record): every shipped
# scenario x seeds 1, 7, 13 x 730 days, one .jsonl.gz per run and a
# manifest.json. See docs/GOLDEN_TRACES.md for the format and the workflow.
#
# Usage:
#   tools/record_golden_traces.sh [--commit REV] [--out DIR | --update]
#                                 [--base REV | --against DIR] [--report FILE]
#                                 [--detail] [--scenarios a,b] [--seeds 1,7,13]
#                                 [--days N] [--jobs N]
#
#   --commit REV   commit to record (default HEAD)
#   --out DIR      write the traces here (default: a new temporary directory)
#   --update       write them into src/LakskongenSim.Tests/Fixtures/golden_traces
#                  of this checkout (a re-record; commit it with the diff report)
#   --base REV     also record REV (into DIR/base; the commit goes to DIR/head)
#                  and diff base -> head: the F2 gate, e.g. --base $(git merge-base HEAD main)
#   --against DIR  diff the recording against a recorded set, e.g. the committed
#                  fixtures (src/LakskongenSim.Tests/Fixtures/golden_traces)
#   --report FILE  where the Markdown diff report goes (default DIR/diff_report.md)
#   --detail       record every day's exact values too (diagnosis only: a diff
#                  then names the entity and field on the day; never commit these)
#
# Exit: 0 recorded (and every diff identical), 1 a diff found differences,
#       2 usage, git or build error.
#
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURES="src/LakskongenSim.Tests/Fixtures/golden_traces"
RECORDER="src/StateHashProbe/GoldenTraceRecorder.cs"

COMMIT="HEAD"; OUT=""; UPDATE=0; BASE=""; AGAINST=""; REPORT=""
RECORD_ARGS=()

die() { echo "record_golden_traces: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --commit) COMMIT="${2:?--commit needs a revision}"; shift 2 ;;
        --out) OUT="${2:?--out needs a directory}"; shift 2 ;;
        --update) UPDATE=1; shift ;;
        --base) BASE="${2:?--base needs a revision}"; shift 2 ;;
        --against) AGAINST="${2:?--against needs a directory}"; shift 2 ;;
        --report) REPORT="${2:?--report needs a file}"; shift 2 ;;
        --detail) RECORD_ARGS+=(--detail true); shift ;;
        --scenarios|--seeds|--days|--jobs) RECORD_ARGS+=("$1" "${2:?$1 needs a value}"); shift 2 ;;
        -h|--help) sed -n '2,32p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) die "unknown option '$1' (see --help)" ;;
    esac
done
[[ -n "$BASE" && -n "$AGAINST" ]] && die "--base and --against exclude each other"
[[ $UPDATE -eq 1 && -n "$OUT" ]] && die "--update and --out exclude each other"
[[ $UPDATE -eq 1 && -n "$BASE" ]] && die "--update records one commit; diff it with --against afterwards"
command -v dotnet >/dev/null 2>&1 || die "dotnet not found"
[[ -n "$REPORT" && "$REPORT" != /* ]] && REPORT="$PWD/$REPORT"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/golden_traces.XXXXXX")" || die "mktemp failed"
WORKTREES=()
cleanup() {
    local wt
    for wt in "${WORKTREES[@]:-}"; do
        [[ -n "$wt" ]] && git -C "$REPO_ROOT" worktree remove --force "$wt" >/dev/null 2>&1
    done
    rm -rf "$WORK/worktrees"
}
trap cleanup EXIT

if [[ $UPDATE -eq 1 ]]; then
    OUT="$REPO_ROOT/$FIXTURES"
elif [[ -z "$OUT" ]]; then
    OUT="$WORK/out"
fi
mkdir -p "$OUT" || die "cannot create $OUT"
OUT="$(cd "$OUT" && pwd)"

# record REV DIR: build REV's recorder in a temporary worktree and record into DIR.
record() {
    local rev="$1" dir="$2" sha wt
    sha="$(git -C "$REPO_ROOT" rev-parse --verify --quiet "$rev^{commit}")" || die "not a commit: $rev"
    git -C "$REPO_ROOT" cat-file -e "$sha:$RECORDER" 2>/dev/null \
        || die "$rev ($sha) predates the golden-trace harness (F2-01): it has no $RECORDER"
    wt="$WORK/worktrees/${sha:0:12}"
    mkdir -p "$WORK/worktrees"
    git -C "$REPO_ROOT" worktree add --detach --quiet "$wt" "$sha" || die "git worktree add failed for $sha"
    WORKTREES+=("$wt")
    echo "== building $rev ($sha) in $wt"
    dotnet build "$wt/src/StateHashProbe/StateHashProbe.csproj" --nologo -v q -c Debug >"$WORK/build_${sha:0:12}.log" 2>&1 \
        || { tail -40 "$WORK/build_${sha:0:12}.log" >&2; die "build failed for $sha"; }
    echo "== recording $rev ($sha) into $dir"
    mkdir -p "$dir"
    # Run from the worktree root so the engine reads that commit's godot/data.
    # The engine's console chatter (tutorial loading, scenario verdicts) goes to a log.
    local log="$WORK/record_${sha:0:12}.log"
    ( cd "$wt" && dotnet "$wt/src/StateHashProbe/bin/Debug/net10.0/StateHashProbe.dll" golden record \
        --out "$dir" --commit "$sha" "${RECORD_ARGS[@]+"${RECORD_ARGS[@]}"}" ) >"$log" 2>&1 \
        || { tail -40 "$log" >&2; die "recording failed for $sha"; }
    grep '^recorded [0-9]* traces' "$log"
}

# diff EXPECTED ACTUAL: the Markdown report, exit 1 when a trace differs.
diff_sets() {
    local expected="$1" actual="$2" report="${REPORT:-$OUT/diff_report.md}" wt="${WORKTREES[${#WORKTREES[@]}-1]}"
    ( cd "$wt" && dotnet "$wt/src/StateHashProbe/bin/Debug/net10.0/StateHashProbe.dll" golden diff \
        "$expected" "$actual" --report "$report" )
    local status=$?
    echo "== diff report: $report"
    return $status
}

START=$SECONDS
status=0
if [[ -n "$BASE" ]]; then
    record "$BASE" "$OUT/base"
    record "$COMMIT" "$OUT/head"
    diff_sets "$OUT/base" "$OUT/head" || status=$?
else
    record "$COMMIT" "$OUT"
    if [[ -n "$AGAINST" ]]; then
        AGAINST="$(cd "$AGAINST" && pwd)" || die "no such directory: $AGAINST"
        diff_sets "$AGAINST" "$OUT" || status=$?
    fi
fi
echo "== traces in $OUT ($((SECONDS - START)) s)"
exit $status
