#!/bin/bash
# merge.sh BRANCH "MESSAGE" : merge a branch into main, build (waiting for a quiet box, retrying), check start-up, push. Stops on a failed build.
# Run it from anywhere: it works in the checkout it lives in, which must be clean. It puts that checkout on main, so
# run it from the main checkout or a worktree kept for merging. The Windows PC's version is merge.ps1 beside it.
# Environment: MODEL and SESSION set the commit trailer (MODEL="Claude Opus 5.5" SESSION=https://claude.ai/code/session_...),
# DOTNET_ENV names a script to source first if dotnet needs one, SECTIONS the self-test sections to run ("" for none).
set -u
cd "$(dirname "$0")/../.." || exit 2
[ -n "${DOTNET_ENV:-}" ] && [ -f "$DOTNET_ENV" ] && . "$DOTNET_ENV"
export XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-/tmp} # the game needs an existing folder here even headless
[ $# -ge 2 ] || { echo 'usage: merge.sh BRANCH "MESSAGE"'; exit 2; }
[ -z "$(git status --porcelain)" ] || { echo "this checkout is not clean"; exit 2; }
L=${TMPDIR:-/tmp}/callofslop-merge
mkdir -p "$L"
M="Co-Authored-By: ${MODEL:-Claude Opus 5.5} <noreply@anthropic.com>"
[ -n "${SESSION:-}" ] && M="$M
Claude-Session: $SESSION"
git fetch -q origin main "+refs/heads/$1:refs/remotes/origin/$1" || exit 2
git checkout -q -B main origin/main || exit 2
git merge --no-ff -q "origin/$1" -m "$2

$M" || { echo "merge conflict"; git merge --abort; exit 3; }
echo merged
ok=1
for t in $(seq 1 10); do
  for w in $(seq 1 30); do pgrep -f "bincore/csc" >/dev/null || break; sleep 10; done
  dotnet build -c Release --no-restore > "$L/build.log" 2>&1 && { ok=0; break; }
  grep -q "error CS" "$L/build.log" && break
  sleep 20
done
echo "build $ok"; [ $ok = 0 ] || { grep "error CS" "$L/build.log" | head -3; exit 4; }
timeout 900 dotnet bin/Release/net10.0/CallOfSlop.dll --check-construct-parts > "$L/ccp.log" 2>&1; c=$?; echo "check $c"; [ $c = 0 ] || exit 5
# production work: the production sections by name, before the push (set SECTIONS="" to skip for docs-only merges)
SECTIONS=${SECTIONS-"Sandbox inventory,Gathering,Refining,Assembling,Extruder,Press stock,Construct storage,Wheel orders,Construct links and power"}
if [ -n "$SECTIONS" ]; then
  timeout 300 dotnet bin/Release/net10.0/CallOfSlop.dll --gen-parts all --check > "$L/genparts.log" 2>&1; g=$?; echo "gen-parts $g"; [ $g = 0 ] || exit 7
  ( cd bin/Release/net10.0 && rm -f selftest.log && timeout 600 dotnet CallOfSlop.dll --selftest-only "$SECTIONS" >/dev/null 2>&1; echo $? > "$L/selftest.exit" )
  t=$(cat "$L/selftest.exit"); echo "sections $t"; [ "$t" = 0 ] || { grep "FAIL" bin/Release/net10.0/selftest.log | head -5; exit 8; }
fi
git fetch -q origin main
git merge-base --is-ancestor origin/main HEAD || git merge -q origin/main -m "Merge origin/main

$M" || { echo "main moved: conflict"; exit 6; }
git push -q origin HEAD:main 2>/dev/null && echo pushed
