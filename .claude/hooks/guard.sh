#!/bin/sh
# Guard rails for agent sessions: a Claude Code PreToolUse hook on the Bash and PowerShell tools (.claude/settings.json).
# It reads the hook's JSON on stdin. Exit 42 with the reason on stderr refuses the command, and the line in
# settings.json turns that into the hook's "block" (exit 2); every other exit lets the command through, so a guard
# that fails for its own reasons (a syntax error is sh's exit 2) never stops work. It also lets a command through
# whenever it cannot tell. Plain POSIX sh with git, sed, grep and tr only, so the same file runs under Linux's sh
# and the sh that Git for Windows brings (which is how settings.json starts it: through a git alias).
#
# What it refuses (.claude/README.md says why, and how to try each by hand):
#   1. Switching branches in a shared checkout: a main folder on a developer's PC, which other sessions and the
#      developer's own game build use at the same time. Worktrees and cloud sessions are never refused.
#   2. A commit that changes the wire format while this branch has not bumped ServerBrowser.ProtocolVersion.
#   3. A commit that changes SavePaths.SaveFiles or SaveFolders without the three lists that go with them.
LC_ALL=C
export LC_ALL

input=$(cat)
case $input in
    *git*) ;;
    *) exit 0 ;;
esac

# The value of a string field of the hook's JSON, unescaped ("command" and "cwd" are the only ones read).
json_str() {
    printf '%s' "$input" | tr '\n\r' '  ' |
        sed -n -E 's/.*"'"$1"'"[[:space:]]*:[[:space:]]*"(([^"\\]|\\.)*)".*/\1/p' |
        sed -e 's/\\\\/@@BSL@@/g' -e 's/\\n/ ; /g' -e 's/\\r//g' -e 's/\\t/ /g' -e 's/\\"/"/g' \
            -e 's/\\u0026/\&/g' -e 's/\\u003[cC]/</g' -e 's/\\u003[eE]/>/g' -e 's/\\u0027/'"'"'/g' -e 's/@@BSL@@/\\/g'
}

cmd=$(json_str command)
[ -n "$cmd" ] || exit 0
dir=$(json_str cwd)
[ -n "$dir" ] || dir=.

refuse() {
    printf '%s\n' "$1" >&2
    exit 42
}

unquote() {
    u=$1
    u=${u#[\"\']}
    u=${u%[\"\']}
    printf '%s' "$u"
}

# A path named in the command, against the folder the command would be in by then.
resolve() {
    r=$(unquote "$1")
    case $r in
        "~"*) printf '%s' "$HOME${r#"~"}" ;;
        /* | [A-Za-z]:*) printf '%s' "$r" ;;
        *) printf '%s' "$dir/$r" ;;
    esac
}

# ---- 1. The shared checkout keeps its branch --------------------------------------------------------------------------

guard_branch() {
    sub=$1
    shift
    # Each cloud session has a clone of its own.
    [ "${CLAUDE_CODE_REMOTE:-}" = true ] && return 0
    paths=$(git -C "$tdir" rev-parse --path-format=absolute --git-dir --git-common-dir 2>/dev/null) || return 0
    gitdir=$(printf '%s\n' "$paths" | sed -n 1p)
    common=$(printf '%s\n' "$paths" | sed -n 2p)
    [ -n "$gitdir" ] && [ "$gitdir" = "$common" ] || return 0 # a linked worktree is its session's own
    [ "$(git -C "$tdir" config --get callofslop.ownCheckout 2>/dev/null)" = true ] && return 0
    cur=$(git -C "$tdir" symbolic-ref -q --short HEAD 2>/dev/null)

    target=""
    count=0
    makes=""
    for a in "$@"; do
        a=$(unquote "$a")
        case $a in
            --) [ "$sub" = checkout ] && return 0 ;; # paths follow: files are restored, the branch stays
            -p | --patch | --ours | --theirs | --merge | --pathspec-from-file=*) [ "$sub" = checkout ] && return 0 ;;
            -b | -B | -c | -C | --create | --force-create | --orphan | --detach | -d) makes=$a ;;
            -) target=- && count=$((count + 1)) ;;
            -*) ;;
            *) [ $count -eq 0 ] && target=$a
               count=$((count + 1)) ;;
        esac
    done
    if [ -z "$makes" ]; then
        [ $count -eq 1 ] || return 0 # none: nothing to switch to; several: a tree and paths
        [ "$target" = "$cur" ] && return 0
        if [ "$sub" = checkout ] && [ "$target" != - ]; then
            # One name: a branch or commit switches, anything else is a path.
            git -C "$tdir" rev-parse -q --verify "$target^{commit}" >/dev/null 2>&1 ||
                [ -n "$(git -C "$tdir" for-each-ref "refs/remotes/*/$target" 2>/dev/null)" ] || return 0
        fi
    fi
    top=$(git -C "$tdir" rev-parse --show-toplevel 2>/dev/null)
    refuse "Refused: 'git $sub' would move the shared checkout $top off '${cur:-its commit}'.
Other sessions and the developer's own build use this folder at the same time, so its branch stays where it is.
Work in a worktree of your own instead (it shares this folder's history and is never refused):
  git -C \"$top\" worktree add ../CallOfSlop-NAME -b YOUR-BRANCH origin/main
then run your commands there (CLAUDE.md, 'On the user's Windows PC', says how a new worktree is restored and built).
A developer who wants to switch branches in this folder by hand turns the guard off for it with
'git config callofslop.ownCheckout true'. An agent never sets that in a folder it did not make."
}

# ---- 2 and 3. Commits ---------------------------------------------------------------------------------------------------

# An enum's members without comments or spaces: stdin is the file, $1 the enum's name.
enum_body() {
    sed -n '/^public enum '"$1"'[ :]/,/^}/p' | sed -e 's://.*$::' | tr -d ' \t\r\n'
}

protocol_of() {
    sed -n -E 's/.*const ushort ProtocolVersion = ([0-9]+);.*/\1/p' | sed -n 1p
}

check_protocol() {
    why=""
    for spec in Source/Net/Net.cs:Msg Source/Game/GameSandbox.cs:CActKind Source/Constructs/ConstructNet.cs:CBodyKind; do
        f=${spec%%:*}
        e=${spec##*:}
        [ -f "$f" ] || continue
        was=$(git show "HEAD:$f" 2>/dev/null | enum_body "$e")
        now=$(enum_body "$e" <"$f")
        [ "$was" = "$now" ] || why="$why
  enum $e changed ($f)"
    done
    lines=$(git diff HEAD -U0 -- 'Source/*.cs' ':(exclude)Source/App/SelfTest*' ':(exclude)Source/Worlds/*' ':(exclude)Source/Maps/*' 2>/dev/null |
        grep -E '^[+-][^+-]' | grep -E '\.(U8|U16|U24|U32|U64|F32|F64|Cm|CmVec)\(' | sed -n 1,3p | cut -c1-150)
    [ -n "$lines" ] && why="$why
  packet reads or writes changed, for one:
$lines"
    [ -n "$why" ] || return 0
    vnow=$(protocol_of <Source/Net/ServerBrowser.cs)
    vbase=$(git show "$base:Source/Net/ServerBrowser.cs" 2>/dev/null | protocol_of)
    [ -n "$vnow" ] && [ -n "$vbase" ] || return 0
    [ "$vnow" = "$vbase" ] || return 0 # this branch has bumped it
    refuse "Refused: this commit looks like a wire-format change, and ServerBrowser.ProtocolVersion is still $vnow, as on main.$why
Any change to the bytes two peers exchange bumps ProtocolVersion (Source/Net/ServerBrowser.cs) with a comment line in
the history above it, in the same branch; new messages go at the end of Msg (CLAUDE.md, Conventions, 'Protocol').
If no byte on the wire changes (a refactor, a moved line, a file that is only saved), say so and commit again with
the words wire-same anywhere in the commit command, for example in the message: '... (wire-same: lines moved)'."
}

check_saves() {
    git diff HEAD -U0 -- Source/App/SavePaths.cs 2>/dev/null | grep -E '^[+-][^+-]' |
        grep -qE 'SaveFiles =|SaveFolders =' || return 0
    missing=""
    for f in tools/package.ps1 tools/MIGRATING.md .gitignore; do
        git diff --quiet "$base" -- "$f" 2>/dev/null && missing="$missing $f"
    done
    [ -n "$missing" ] || return 0
    refuse "Refused: SavePaths.SaveFiles or SaveFolders changed, and this branch has not touched:$missing
A new save file or folder is also added to the personal-file list in tools/package.ps1 (or testers ship their saves
to each other), to tools/MIGRATING.md (or players lose it when they move builds) and to .gitignore (CLAUDE.md,
Conventions, 'Saves'). If the lists really need no change (an entry was only renamed in code or reordered), commit
again with the words save-lists-same anywhere in the commit command."
}

guard_commit() {
    top=$(git -C "$tdir" rev-parse --show-toplevel 2>/dev/null) || return 0
    [ -f "$top/Source/Net/ServerBrowser.cs" ] || return 0
    cd "$top" 2>/dev/null || return 0
    git rev-parse -q --verify HEAD >/dev/null 2>&1 || return 0
    # A commit that finishes a merge, a cherry-pick or a revert carries other people's changes: not judged here.
    for h in MERGE_HEAD CHERRY_PICK_HEAD REVERT_HEAD; do
        git rev-parse -q --verify "$h" >/dev/null 2>&1 && return 0
    done
    base=$(git merge-base HEAD refs/remotes/origin/main 2>/dev/null) || base=HEAD
    case $cmd in
        *wire-same*) ;;
        *) check_protocol ;;
    esac
    case $cmd in
        *save-lists-same*) ;;
        *) check_saves ;;
    esac
}

# ---- The command, one simple command at a time ----------------------------------------------------------------------

nl='
'
segments=$(printf '%s\n' "$cmd" | tr ';|&(){}`' '\n\n\n\n\n\n\n\n')
set -f
old_ifs=$IFS
IFS=$nl
for seg in $segments; do
    IFS=$old_ifs
    set -- $seg
    # Leading NAME=value words and wrappers.
    while [ $# -gt 0 ]; do
        case $1 in
            [A-Za-z_]*=* | sudo | command | time | env | exec) shift ;;
            *) break ;;
        esac
    done
    [ $# -gt 0 ] || continue
    case $(unquote "$1") in
        cd | pushd | chdir | Set-Location | sl)
            shift
            [ "${1:-}" = -- ] && shift
            [ $# -gt 0 ] && dir=$(resolve "$1")
            ;;
        git | git.exe | */git | */git.exe | *\\git | *\\git.exe)
            shift
            tdir=$dir
            while [ $# -gt 0 ]; do
                case $1 in
                    -C) [ $# -gt 1 ] && tdir=$(dir=$tdir && resolve "$2")
                        shift
                        [ $# -gt 0 ] && shift ;;
                    -c) shift
                        [ $# -gt 0 ] && shift ;;
                    -*) shift ;;
                    *) break ;;
                esac
            done
            [ $# -gt 0 ] || continue
            case $1 in
                checkout | switch) guard_branch "$@" ;;
                commit) guard_commit ;;
            esac
            ;;
    esac
done
exit 0
