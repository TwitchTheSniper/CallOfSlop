# merge.ps1 BRANCH "MESSAGE" [-Model "Claude Opus 5.5"] [-Sections ""] : the Windows PC's merge.sh. Run from the main checkout
# (clean tree, the game closed): merge a branch into main, build, check start-up and the production sections, push.
# BRANCH is a local branch (a session's worktree branch) or, failing that, origin/BRANCH. Stops on the first failure.
param(
    [Parameter(Mandatory)][string]$Branch,
    [Parameter(Mandatory)][string]$Message,
    [string]$Model = "Claude Opus 5.5",
    [string]$Sections = "Sandbox inventory,Gathering,Refining,Assembling,Extruder,Press stock,Construct storage,Wheel orders,Construct links and power"
)
$root = Resolve-Path "$PSScriptRoot\..\.."
Set-Location $root
$logs = Join-Path $env:TEMP "callofslop-merge"
New-Item -ItemType Directory -Force $logs | Out-Null
$trailer = "Co-Authored-By: $Model <noreply@anthropic.com>"
$dll = "bin\Release\net10.0\CallOfSlop.dll" # through dotnet: the exe is a window program, which PowerShell does not wait for

if (git status --porcelain) { "main checkout is not clean"; exit 2 }
git fetch -q origin
git checkout -q main; if (-not $?) { exit 2 }
git merge -q --ff-only origin/main; if (-not $?) { "main differs from origin/main"; exit 2 }
git show-ref --verify --quiet "refs/heads/$Branch"
$ref = if ($?) { "refs/heads/$Branch" } else { "refs/remotes/origin/$Branch" }
git merge --no-ff -q $ref -m "$Message`n`n$trailer"
if (-not $?) { "merge conflict"; git merge --abort; exit 3 }
"merged $ref"

dotnet build -c Release --no-restore > "$logs\build.log"
if ($LASTEXITCODE -ne 0) { "build failed"; Select-String -Path "$logs\build.log" -Pattern "error CS" | Select-Object -First 3 | ForEach-Object { $_.Line }; exit 4 }
"build 0"
dotnet $dll --check-construct-parts > "$logs\ccp.log"; $c = $LASTEXITCODE; "check $c"; if ($c -ne 0) { exit 5 }
if ($Sections) {
    dotnet $dll --gen-parts all --check > "$logs\genparts.log"; $g = $LASTEXITCODE; "gen-parts $g"; if ($g -ne 0) { exit 7 }
    Remove-Item "bin\Release\net10.0\selftest.log" -ErrorAction SilentlyContinue
    dotnet $dll --selftest-only $Sections | Out-Null; $t = $LASTEXITCODE; "sections $t"
    if ($t -ne 0) { Select-String -Path "bin\Release\net10.0\selftest.log" -Pattern "FAIL" | Select-Object -First 5 | ForEach-Object { $_.Line }; exit 8 }
}
git fetch -q origin main
git merge-base --is-ancestor origin/main HEAD
if (-not $?) {
    git merge -q origin/main -m "Merge origin/main`n`n$trailer"
    if (-not $?) { "main moved: conflict"; exit 6 }
}
git push -q origin HEAD:main; if ($?) { "pushed" } else { exit 9 }
