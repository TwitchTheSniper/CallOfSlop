#!/bin/sh
# Cloud sessions only: a SessionStart hook (.claude/settings.json) that puts the .NET 10 SDK in a fresh container, so
# a thread doesn't spend its first minutes installing it by hand. It does nothing anywhere else (a developer's PC has
# its own SDK), nothing when dotnet is already there (the container is cached after the first start, and the hook also
# runs on resume, clear and compact), and it never restores packages: `dotnet restore` stays the one deliberate step a
# session takes once, when it is going to build (CLAUDE.md, "Be gentle to Ahwoo").
# What it prints goes into the session's context, so it says one line. It always exits 0: a failed install is
# reported, and the session can still do work that needs no build.
[ "${CLAUDE_CODE_REMOTE:-}" = true ] || exit 0

# The game needs XDG_RUNTIME_DIR to be an existing folder, even headless.
if [ -n "${CLAUDE_ENV_FILE:-}" ] && [ -z "${XDG_RUNTIME_DIR:-}" ]; then
    mkdir -p /tmp/xdg-runtime && chmod 700 /tmp/xdg-runtime
    grep -q XDG_RUNTIME_DIR "$CLAUDE_ENV_FILE" 2>/dev/null ||
        echo 'export XDG_RUNTIME_DIR=/tmp/xdg-runtime' >>"$CLAUDE_ENV_FILE"
fi

command -v dotnet >/dev/null 2>&1 && exit 0
command -v apt-get >/dev/null 2>&1 || {
    echo "No dotnet and no apt-get in this container: install the .NET 10 SDK by hand before building."
    exit 0
}
log=${TMPDIR:-/tmp}/dotnet-install.log
sudo=""
[ "$(id -u)" = 0 ] || sudo="sudo -n"
if { $sudo apt-get update && DEBIAN_FRONTEND=noninteractive $sudo apt-get install -y dotnet-sdk-10.0; } >"$log" 2>&1 &&
    command -v dotnet >/dev/null 2>&1; then
    echo "The .NET SDK $(dotnet --version 2>/dev/null) was installed at session start. Before the first build run 'dotnet restore' once, then 'dotnet build -c Release --no-restore'."
else
    echo "The .NET SDK could not be installed at session start (see $log): 'apt-get update && apt-get install -y dotnet-sdk-10.0' by hand before building."
fi
exit 0
