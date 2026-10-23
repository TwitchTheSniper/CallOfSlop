#!/usr/bin/env bash
# merge_check.sh - what the coordinator runs before committing a merge: build, then the sections the changed files
# need (tools/ci/sections_for.py), on the current checkout (a merge in progress counts: it reads the index and tree).
#
#   . /home/claude/dotnet-env.sh && tools/ci/merge_check.sh [BASE]        # BASE defaults to origin/main
#
# Exit code: the self-test's failure count (0 = clean). Prints the tag line, the FAIL lines and the DONE line.
set -u
cd "$(dirname "$0")/../.."
base=${1:-origin/main}
tag=$(python3 -I tools/ci/sections_for.py "$base") || { echo "$tag"; exit 2; }
echo "$tag"
sections=${tag#\[tests: }; sections=${sections%\]}
dotnet build -c Release --no-restore -v q -clp:NoSummary 2>&1 | grep -E " error " && exit 3
timeout 900 dotnet bin/Release/net10.0/CallOfSlop.dll --selftest-only "$sections" >/dev/null 2>&1
rc=$?
grep -E '^\s*FAIL|^DONE' bin/Release/net10.0/selftest.log
exit $rc
