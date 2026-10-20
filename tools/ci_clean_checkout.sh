#!/usr/bin/env bash
#
# Run tools/ci.sh on a fresh clone of the committed HEAD (review F177).
#
# A working tree carries state a clean checkout does not: godot/.godot (the
# class cache and imported resources), locally installed git-ignored addons,
# build outputs. Gates that pass here can still fail for the next person who
# clones the repo; this script catches that. Uncommitted changes are NOT
# included — commit first, or pass a ref.
#
# Usage:
#   tools/ci_clean_checkout.sh            # HEAD
#   tools/ci_clean_checkout.sh <ref>      # any commit/branch
#
# Environment: GODOT_BIN (passed through to tools/ci.sh), KEEP_CLONE=1 to keep
# the clone for inspection.
#
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REF="${1:-HEAD}"
SHA="$(git -C "$REPO_ROOT" rev-parse --verify "$REF^{commit}")" || {
    echo "ERROR: unknown ref '$REF'" >&2
    exit 2
}

CLONE="$(mktemp -d -t lakskongen-clean.XXXXXX)"
cleanup() {
    if [[ "${KEEP_CLONE:-0}" == "1" ]]; then
        echo "Clone kept at $CLONE"
    else
        rm -rf "$CLONE"
    fi
}
trap cleanup EXIT

echo "Cloning $SHA into $CLONE ..."
git clone --quiet --no-hardlinks "$REPO_ROOT" "$CLONE/repo" || exit 2
git -C "$CLONE/repo" checkout --quiet "$SHA" || exit 2

echo "Running tools/ci.sh on the clean checkout ..."
(cd "$CLONE/repo" && bash tools/ci.sh)
status=$?
echo "Clean-checkout CI exit: $status"
exit "$status"
