#!/usr/bin/env bash
# Packages the Mac build (Apple Silicon only): CallOfSlop.app in bin/package-mac/CallOfSlop-<version>-mac.zip, plus
# update-mac.json (the "osx-arm64" entry release.yml adds to update.json's "platforms"). Runs on macOS (release.yml's
# Mac job; needs pwsh, sips, iconutil, codesign, ditto). The steps, in order:
#
#   publish  tools/package.ps1 -Runtime osx-arm64 -Step publish: a flat self-contained folder (no personal files),
#            bin/package-mac/CallOfSlop/ with the program, natives and data side by side (it runs as is; the workflow
#            bakes maps and nav graphs into it with --bake before the bundle step).
#   bundle   moves that folder into CallOfSlop.app: the program, .NET and the natives in Contents/MacOS, the data
#            folders (Maps, Weapons, Parts, Props, Baked...) in Contents/Resources (the game finds them there,
#            Source/App/AppFolders.cs), Info.plist (tools/mac/Info.plist with the version) and AppIcon.icns. READ ME
#            FIRST.txt (tools/mac/TESTERS-MAC.txt), MOVING YOUR SAVES.txt and NOTICE.txt stay next to the app.
#            Then ad-hoc signs the bundle (codesign -s -): not a Developer ID signature, so players still confirm the
#            first launch (READ ME FIRST.txt), but macOS sees a whole, unmodified app instead of a "damaged" one.
#   zip      ditto zip of the CallOfSlop folder (keeps the program's executable bit), its size and SHA-256, and
#            update-mac.json.
#
# Usage: tools/mac/package_mac.sh <step> <version> [tag]
set -euo pipefail

step="${1:?step: publish | bundle | zip}"
version="${2:?version 0.YYYY.MDD.N}"
tag="${3:-v$version}"
sha="${PACKAGE_SHA:-}"

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"
out="$root/bin/package-mac"
folder="$out/CallOfSlop"
app="$folder/CallOfSlop.app"
zipname="CallOfSlop-$version-mac.zip"

fail() { echo "package_mac failed: $*" >&2; exit 1; }

case "$step" in
publish)
    pwsh -NoProfile -File tools/package.ps1 -Version "$version" -Sha "$sha" -Tag "$tag" -Runtime osx-arm64 -Step publish -OutDir bin/package-mac
    [ -x "$folder/CallOfSlop" ] || fail "the CallOfSlop program wasn't published"
    file "$folder/CallOfSlop" | grep -q "arm64" || fail "the program isn't arm64: $(file "$folder/CallOfSlop")"
    # The Windows-only bits package.ps1 adds don't belong in a Mac app.
    rm -f "$folder"/*.bat "$folder"/*.exe
    ;;

bundle)
    [ -d "$folder" ] && [ -x "$folder/CallOfSlop" ] || fail "nothing published in $folder (run the publish step first)"
    [ ! -e "$app" ] || fail "$app already exists (publish again first)"
    staging="$out/flat"
    rm -rf "$staging"
    mv "$folder" "$staging"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    # Next to the app: what players read first.
    cp "tools/mac/TESTERS-MAC.txt" "$folder/READ ME FIRST.txt"
    mv "$staging/MOVING YOUR SAVES.txt" "$folder/"
    cp "$staging/NOTICE.txt" "$folder/NOTICE.txt" # (and in Resources: the licence travels with the app)
    rm -f "$staging/READ ME FIRST.txt" # package.ps1's Windows READ ME
    # Folders with code in them (.NET satellite assemblies) go with the program; every other folder is game data.
    for d in "$staging"/*/; do
        d="${d%/}"
        if [ -n "$(find "$d" -type f \( -name "*.dll" -o -name "*.dylib" \) -print -quit)" ]; then
            mv "$d" "$app/Contents/MacOS/"
        else
            mv "$d" "$app/Contents/Resources/"
        fi
    done
    # Loose data files the game reads from its data folder; everything else at the top is the program.
    for f in NOTICE.txt masters.txt; do
        [ -f "$staging/$f" ] && mv "$staging/$f" "$app/Contents/Resources/"
    done
    mv "$staging"/* "$app/Contents/MacOS/"
    rmdir "$staging"
    [ -d "$app/Contents/Resources/Maps" ] || fail "no Maps folder in Contents/Resources"
    [ -f "$app/Contents/MacOS/CallOfSlop.dll" ] || fail "no CallOfSlop.dll in Contents/MacOS"
    sed "s/@VERSION@/$version/g" tools/mac/Info.plist > "$app/Contents/Info.plist"
    plutil -lint "$app/Contents/Info.plist"
    printf 'APPL????' > "$app/Contents/PkgInfo"
    # The icon: tools/mac/AppIcon.png (tools/mac/make_icon.py) at every size macOS wants.
    iconset="$out/AppIcon.iconset"
    rm -rf "$iconset" && mkdir -p "$iconset"
    for s in 16 32 128 256 512; do
        sips -z $s $s tools/mac/AppIcon.png --out "$iconset/icon_${s}x${s}.png" >/dev/null
        sips -z $((s * 2)) $((s * 2)) tools/mac/AppIcon.png --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null
    done
    iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
    rm -rf "$iconset"
    # Ad-hoc signature over the whole bundle (no Apple account). Everything in Contents/MacOS is "code" to macOS, the
    # managed .dll and .json files too (their signatures go in extended attributes, which the zip keeps), so each file
    # there is signed first, then the app (which signs the main program and seals Resources).
    find "$app/Contents/MacOS" -type f ! -path "$app/Contents/MacOS/CallOfSlop" -print0 | xargs -0 -n 50 codesign --force --sign - --timestamp=none
    codesign --force --sign - --timestamp=none "$app"
    codesign --verify --deep --strict --verbose=2 "$app"
    echo "bundle: $app ($(du -sh "$app" | cut -f1))"
    ;;

zip)
    [ -f "$app/Contents/Info.plist" ] || fail "no bundle in $folder (run the bundle step first)"
    # No personal or debug files (package.ps1 already stripped the published folder; the bake step's log is gone too).
    if find "$folder" \( -name "*.log" -o -name "settings.txt" -o -name "progress*.txt" -o -name "loadout*.txt" -o -name "workbench.txt" \
        -o -name "audio_mix.json" -o -name "appearance.txt" -o -name "portable.txt" -o -name "*.bak" -o -name "update_check.txt" -o -name ".DS_Store" \) | grep .; then
        fail "personal files in the package (listed above)"
    fi
    rm -f "$out/$zipname"
    # ditto, as Finder's Compress does: Unix permissions (the program's executable bit) and the extended attributes
    # (the signatures of the files in Contents/MacOS) in __MACOSX side files, which Archive Utility puts back. The game's
    # own updater skips __MACOSX (it never sets the quarantine flag, so macOS doesn't re-check the seal).
    (cd "$out" && ditto -c -k --sequesterRsrc --noqtn --keepParent CallOfSlop "$zipname")
    size=$(stat -f %z "$out/$zipname")
    hash=$(shasum -a 256 "$out/$zipname" | cut -d' ' -f1)
    url="https://github.com/TwitchTheSniper/CallOfSlop/releases/download/$tag/$zipname"
    printf '{\n  "url": "%s",\n  "size": %s,\n  "sha256": "%s"\n}\n' "$url" "$size" "$hash" > "$out/update-mac.json"
    echo "Packaged $out/$zipname ($size bytes, sha256 $hash)"
    cat "$out/update-mac.json"
    ;;

*)
    fail "unknown step '$step' (publish | bundle | zip)"
    ;;
esac
