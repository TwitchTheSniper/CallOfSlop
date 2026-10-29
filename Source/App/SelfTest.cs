using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

// Focused runs: "--selftest-only "Save folder, Frenzy"" runs only the named sections (comma-separated, any case; a whole
// section name picks that one, otherwise every section starting with the text). Unknown names fail and list the valid
// ones; "--selftest-list" prints them. A section that uses an earlier one's results names it (Section(name, body,
// "Earlier")) and a filtered run brings it along; every other section passes run alone (checked one by one when the
// filter was added). selftest.log and its DONE line say FILTERED and which sections ran. On a feature
// branch's CI push, a "[tests: A, B]" tag in the commit message becomes --selftest-only "A, B"; main, the release
// workflow and a manual run without the "tests" input always run the full suite. Every section logs its time
// ("-- Name: 1.23 s") and the run ends with a SLOWEST line (the ten slowest sections).

/// <summary>
/// Headless checks, run with "CallOfSlop.exe --selftest" (no window, no audio, no network).
/// Writes selftest.log next to the game and exits with the number of failures.
///   1. Every map: geometry, spawns and waypoints, nothing spawning inside walls.
///   2. Every map: a full offline match with 7 bots for 90 simulated seconds (crashes, kills, stuck bots, tick cost).
///   2a. Bots: a nav graph and A* routes on every map, a bot walking to a far goal, the headshot share of bot kills
///       (Normal and Hard) and that personalities fight at different ranges.
///   3. Mantling: through a Courtyard window and onto a crate.
///   4. Weapon files: the Weapons and Attachments folders load, round-trip, keep the old ids, and old saves migrate.
///      Attachments: every attachment on every weapon gives sane stats; kits survive Sanitize.
///      Optics: every optic loads and fits sensibly, full scopes (4x+) only on long guns, sights stay aligned,
///      nothing blocks the sight line, and no gun part clashes with a fitted optic.
///      Sight pictures (SelfTestSights.cs): how much of the screen each lens fills, a clear view through it,
///      continuity from hip to full aim, the steadied lens, and the "sight" blocks of optic files.
///   5. Packet sizes: a full 32-player snapshot fits in one packet (more in SelfTestLobbies.cs).
///   6. Recoil: every weapon's profile is valid and deterministic, the classes feel as intended, recovery and
///      attachments work, and the visual shake never moves the aim.
/// It never touches settings, progress or loadout files.
/// </summary>
public static partial class SelfTest
{
    private static StreamWriter _log = null!;
    private static int _failures;

    /// <summary>Where Line and Fail go when no self-test is running (the "--check-map" tool borrows the map checks; SelfTestMapContract.cs).</summary>
    private static Action<string>? _lineSink, _failSink;

    private static void Line(string s)
    {
        if (_log == null)
        {
            _lineSink?.Invoke(s);
            return;
        }
        _log.WriteLine(s);
        _log.Flush();
    }

    private static void Fail(string s)
    {
        _failures++;
        if (_log == null)
        {
            _failSink?.Invoke(s);
            return;
        }
        Line("  FAIL: " + s);
    }

    /// <summary>Runs the self-test: every section, or only those <paramref name="only"/> names (--selftest-only).</summary>
    /// <param name="only">Comma-separated section names (case-insensitive; exact name, else every section starting with
    /// it), or null for the full suite.</param>
    public static int Run(string? only = null)
    {
        var path = AppFolders.LogPath("selftest.log");
        // First pass: collect the section names without running anything, and resolve the filter against them.
        CollectNames();
        string[] picked = [];
        string? filterError = null;
        if (only != null)
        {
            (picked, filterError) = ResolveFilter(only, _names, _needs);
            _plan = picked.ToHashSet(StringComparer.Ordinal);
        }
        Brutal.GlfwApi.Glfw.Init(); // only for the clock (Glfw.Time); no window is created
        SavePaths.UseFolder(SelfTestSaveFolder, "self-test"); // never the real save folder (SelfTestSaves.cs)
        BlueprintStore.ReadOnly = true; // the blueprint library is only written by its own section, into a temp folder
        NavBake.Enabled = false; // every nav graph built live, whatever an earlier run cached ("Nav bake" turns it on in a temp folder)
        MapBake.Enabled = false; // every map built live too, and nothing cached ("Map bake" turns both on in a temp folder)
        MapBake.CacheWrites = false;
        using (_log = new StreamWriter(path, false))
        {
            Line($"Call of Slop self-test {DateTime.Now}");
            var sw = Stopwatch.StartNew();
            _times.Clear();
            if (filterError != null)
            {
                Fail(filterError);
                Line("");
                Line($"DONE in 0.0 s with {_failures} failure(s). FILTERED run (--selftest-only \"{only}\"): nothing ran.");
            }
            else
            {
                if (only != null)
                    Line($"FILTERED run (--selftest-only \"{only}\"): {picked.Length} of {_names.Count} sections: {string.Join(", ", picked)}");
                foreach (var dup in _names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                    Fail($"section name \"{dup.Key}\" is used {dup.Count()} times (each name must pick one section for --selftest-only)");
                foreach (var (name, before) in _needs)
                    foreach (var b in before.Where(b => !_names.Contains(b)))
                        Fail($"section \"{name}\" needs \"{b}\", which isn't a section");
                Sections();
                Line("");
                // Every stuck spell of the run's bot matches (the stuck-spot report, StuckSpots.cs), beside this log: CI uploads it,
                // and --stuck-report pools it with other runs and testers' reports.
                var spotsFile = AppFolders.LogPath("stuck-spots.json"); // the log folder: never inside a Mac app bundle (its seal)
                StuckSpots.Session.Write(spotsFile, only == null ? "self-test (full suite)" : $"self-test (--selftest-only \"{only}\")");
                Line($"STUCK SPOTS: {StuckSpots.Session.Count} spot(s) on {StuckSpots.Session.Maps.Count()} map(s) in the run's bot matches (stuck-spots.json)");
                var slowest = _times.OrderByDescending(t => t.Seconds).Take(10).Select(t => $"{t.Name} {t.Seconds:0.0} s");
                Line("SLOWEST: " + string.Join(", ", slowest));
                Line(only == null
                    ? $"DONE in {sw.Elapsed.TotalSeconds:0.0} s with {_failures} failure(s). Full suite: {_times.Count} sections."
                    : $"DONE in {sw.Elapsed.TotalSeconds:0.0} s with {_failures} failure(s). FILTERED run (--selftest-only \"{only}\"): {_times.Count} of {_names.Count} sections ran: {string.Join(", ", _times.Select(t => t.Name))}");
            }
        }
        Brutal.GlfwApi.Glfw.Terminate();
        return _failures;
    }

    /// <summary>
    /// Prints every section name, one per line, in the order the suite runs them (--selftest-list): to the terminal
    /// (the real standard output) and to callofslop.log (where Console goes).
    /// </summary>
    public static int List()
    {
        CollectNames();
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        foreach (var n in _names)
        {
            stdout.WriteLine(n);
            Console.WriteLine(n);
        }
        return 0;
    }

    /// <summary>
    /// Matches each comma-separated term against the section names, ignoring case: a term that is a whole section
    /// name picks that section; otherwise it picks every section whose name starts with it. A term that matches
    /// nothing is an error that lists the valid names, so a typo never passes as an empty run. A picked section also
    /// brings the sections it needs (<paramref name="needs"/>: what an earlier section leaves behind for it).
    /// </summary>
    internal static (string[] Picked, string? Error) ResolveFilter(string only, IReadOnlyList<string> names, IReadOnlyDictionary<string, string[]> needs)
    {
        var terms = only.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
            return ([], $"--selftest-only: no section names given. Valid names: {string.Join(", ", names)}");
        var picked = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var t in terms)
        {
            var exact = names.Where(n => n.Equals(t, StringComparison.OrdinalIgnoreCase)).ToArray();
            var hits = exact.Length > 0 ? exact : names.Where(n => n.StartsWith(t, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (hits.Length == 0)
                unknown.Add(t);
            picked.UnionWith(hits);
        }
        if (unknown.Count > 0)
            return ([], $"--selftest-only: unknown section(s) {string.Join(", ", unknown.Select(u => $"\"{u}\""))}. Valid names: {string.Join(", ", names)}");
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var n in picked.ToArray())
                if (needs.TryGetValue(n, out var before))
                    foreach (var b in before)
                        grew |= picked.Add(b);
        }
        return (names.Where(picked.Contains).ToArray(), null); // suite order, whatever order the terms came in
    }

    private static HashSet<string>? _plan; // sections to run; null = all
    private static readonly List<string> _names = new(); // every section name, filled by the collecting pass
    private static readonly Dictionary<string, string[]> _needs = new(); // section -> earlier sections whose results it uses
    private static readonly List<(string Name, double Seconds)> _times = new();
    private static bool _collecting;

    private static void CollectNames()
    {
        _plan = null;
        _names.Clear();
        _needs.Clear();
        _collecting = true;
        try
        {
            Sections();
        }
        finally
        {
            _collecting = false;
        }
    }

    /// <summary>The suite, in order. Called once to collect the names (nothing runs) and once to run.</summary>
    private static void Sections()
    {
        Section("Maps", CheckMaps);
        Section("Map files", CheckMapFiles);
        Section("Map reload", CheckMapReload);
        Section("Map packs", CheckMapPacks);
        Section("Big maps", CheckBigMaps);
        Section("Box batches", CheckBoxBatches); // SelfTestBigMaps.cs: the map's own boxes instanced by 24 m cell (BuildingRenderer.cs): each drawn box once, as drawn // SelfTestBigMaps.cs: load cost: parallel = one by one, per-map and stress-map budgets, linear in props
        Section("Pack assets", CheckPackAssets); // SelfTestPackAssets.cs: pack-local props, models and skies
        Section("High Ground pack", CheckHighGround); // SelfTestHighGround.cs: the terrain map pack
        Section("Foliage", CheckFoliage); // see SelfTest.Foliage.cs
        Section("Foliage no-go", CheckFoliageNoGo); // SelfTest.FoliageNoGo.cs: indoor / pavement rules and zones
        Section("Mantling", CheckMantle);
        Section("Terrain", CheckTerrain);
        Section("Slopes", CheckSlopes); // SelfTestSlopes.cs: terrain slope rules, High Ground routes and bot matches
        Section("Terrain holes", CheckTerrainHoles); // SelfTestTerrainHoles.cs: holes and caves in the terrain (mask, queries, falling, grenades, nav, Cave Test)
        Section("Movers", CheckMovers); // moving platforms (SelfTestMovers.cs)
        Section("Map logic", CheckMapLogic); // map events: refusals, the Logic Test map's doors and nav links, mover control, determinism, no allocations, a loopback (SelfTestMapLogic.cs)
        Section("Constructs", CheckConstructs); // SelfTestConstructs.cs: Sandbox construct parts, building with tools, driving, damage, the hinge, the wire (docs/sandbox/CONSTRUCTS.md)
        Section("Construct logic", CheckConstructLogic); // SelfTestConstructLogic.cs: milestone 2: moving joints, logic and wires, the linking tool, drivetrain parts, riding, run-over
        Section("Props", CheckProps); // map props, clutter and breakables (SelfTestProps.cs)
        Section("Buildings", CheckBuildings); // SelfTestBuildings.cs: building files, variants, doors, windows, replication, bots, Building Yard
        Section("Building faces", CheckBuildingFaces); // SelfTestBuildingFaces.cs: no coplanar same-facing faces of different surfaces (z-fighting) in any building or the map Building op
        Section("Stairs", CheckStairs); // SelfTestStairs.cs: every flight on every map and building walkable standing (2.1 m headroom, risers, width, landings)
        Section("Destruction", CheckDestruction); // SelfTestDestruction.cs: materials, chunks, damage per material, holes, collapse, falling pieces, replication, reset, budgets, nav
        Section("World: Paris Quarter", CheckWorldParis); // SelfTestWorldParis.cs: the World Tour Paris map (symmetry, building rooms, routes, cost)
        Section("Animated props", CheckAnimatedProps); // SelfTestAnimProps.cs: the "anim" block, Props/Animated and their gallery
        Section("Military props", CheckMilitaryProps); // SelfTestPropsMilitary.cs: Props/Military (fortifications, guns, ammo, tanks, planes, trucks) and the Military Props map
        Section("Flag pool", CheckFlagPool); // SelfTestFlags.cs: flag designs (Props/Flags), flag props flying them, random picks, overrides, map pools
        Section("Landmines", CheckLandmines); // SelfTestLandmines.cs: pressure plates, neutral blasts, chains, walls, Flak Jacket, bots, zombies, respawn, cost
        Section("Map shapes", CheckMapShapes); // SelfTestShapes.cs: boundaries that aren't rectangles
        Section("Attachments", CheckAttachments);
        Section("Optics", CheckOptics);
        Section("Attachment variety", CheckAttachmentVariety); // SelfTestAttachmentVariety.cs: slots per gun, downsides, class matrix, unlocks, models, saves
        Section("Sight pictures", CheckSightPictures); // SelfTestSights.cs
        Section("Sniper scopes", CheckSniperScopes); // SelfTestSniperScopes.cs: snipers start scoped, iron sights unlock later
        Section("Packets", CheckPackets);
        Section("Loadouts and perks", CheckLoadoutsAndPerks);
        Section("More perks", CheckMorePerks); // SelfTestMorePerks.cs: the 12 later perks, unlocks, old loadouts, Gun Game, bots, effects
        Section("Melee", CheckMelee);
        Section("Melee one-hit kill", CheckMeleeOneHit); // SelfTestMeleeMusket.cs: perks vs the one-hit melee kill
        Section("Musket", CheckMusket); // SelfTestMeleeMusket.cs: the Old Faithful Musket's one-shot ranges
        Section("Shotgun patterns", CheckShotgunPatterns); // SelfTestShotgunPatterns.cs: real unchoked baselines, choke percentages, one/two-shot ranges
        Section("Grenade range", CheckGrenadeRange); // SelfTestGrenades.cs: throw range, fuse vs flight time, bot throw aim
        Section("Health", CheckHealth); // SelfTestHealth.cs: Last Stand's shield and health regeneration
        Section("Kill streaks", CheckKillStreaks); // SelfTestStreaks.cs
        Section("More kill streaks", CheckMoreKillStreaks); // SelfTestMoreStreaks.cs: Smoke Screen to Juggernaut, counters, bots, packets, allocations
        Section("Kill notices", CheckKillNotices); // SelfTestKillNotices.cs: the killer's notice, kill feed, sentry armour
        Section("Recon reveal colours", CheckReconReveal); // SelfTestRecon.cs
        Section("Matches", CheckMatches);
        Section("Baked lighting", CheckBakedLighting); // SelfTestLighting.cs: a seeded scene baked: shadows, falloff, sun and sky, spot and area lights, prop lights, determinism, map bake read-back, bake key, no per-frame allocation
        Section("Light flicker", CheckLightFlicker); // SelfTestLightFlicker.cs: each flickering light its own slow soft wave, blended smoothly in the bake, map/prop overrides, flames follow their light
        Section("Light leaks", CheckLightLeaks); // SelfTestLightLeaks.cs: light stays on its side of thin walls (as the shader reads it), lights in walls moved out, prop lights where drawn, shadows from drawn parts only, the bake audit
        Section("Map bake", CheckMapBake); // SelfTestMapBake.cs: every shipped map read back from its bake = the live build, cache and baked folder, keys, mismatched or damaged files refused
        Section("Nav bake", CheckNavBake); // SelfTestNavBake.cs: baked and cached nav graphs = the live build, cache and baked folder, mismatched or damaged files refused, never a stalled tick
        Section("Nav determinism", CheckNavDeterminism); // SelfTestNavDeterminism.cs: the same graph built whole, a step per slice while the match breaks things, and in 50 ms slices
        Section("Bots", CheckBots, "Matches"); // judges the bot stats of the Matches section's games
        Section("Nav edge walks", CheckNavEdgeWalks); // SelfTestNavEdgeWalk.cs: the real mover walks every map's nav edges (no bots)
        Section("Bot gear level", CheckBotGearLevel); // SelfTestBotGear.cs: bot loadouts match the lobby's level (unlocks, jitter, personalities, joins, setting, no allocations)
        Section("Bot doors", CheckBotDoors); // SelfTestBotDoors.cs: bots open doors ahead, shut them behind / when holding, no spam; co-op enemies and zombies at doors
        Section("Bot stuck watch", CheckBotStuck); // SelfTestBotStuck.cs: stuck bots recover, then kill themselves (pit, wedge, holds, phases, log, Zombies, allocations)
        Section("Collision parity", CheckCollisionParity); // SelfTestNav.cs: old collision code vs the physics world
        Section("Physics", CheckPhysics); // SelfTestPhysics.cs: BVH, determinism, swept mover, costs
        Section("Map vote", CheckMapVote);
        Section("Map vote: humans only", CheckMapVoteHumansOnly); // SelfTestMapVote.cs: bots don't vote, ties, early end
        Section("Map vote: player fit", CheckMapVotePlayerFit); // SelfTestMapVote.cs: never a map too small for the humans here
        Section("Map select", CheckMapSelect); // SelfTestMapSelect.cs: the map select screen and True Random
        Section("Big lobbies", CheckBigLobbies); // SelfTestLobbies.cs: 32 players, per-map limits, snapshots, bandwidth
        Section("World Tour: Silo", CheckWorldSilo); // SelfTestWorldSilo.cs: the missile-silo map (caves, buried buildings, nav reach, fairness)
        Section("Cluttered pack", CheckCluttered); // SelfTestCluttered*.cs: the Cluttered map pack (dense props and foliage; after Big lobbies, so its 32-player match runs JIT-warm)
        Section("World Tour: Bocage", CheckWorldBocage); // SelfTestWorld.Bocage.cs: 05 Bocage (hedgerows, buildings, fairness)
        Section("World Tour: Main Street", CheckWorldMainStreet); // SelfTestWorldMainStreet.cs: 01 Main Street (corn concealment, terrain, fairness, reach)
        Section("World: Vuorikoski", CheckWorldFinland); // SelfTestWorldFinland.cs: the World Tour's Finland map (tunnels, fairness, costs; no bot matches)
        Section("World: Slopview Mall", CheckWorldMall); // SelfTestWorldMall.cs: World Tour map 02 (branding, decals, mall building nav, escalators)
        Section("World Tour: Village", CheckWorldVillage); // SelfTestWorld.Village.cs: 07 Cotswold Village (England, small)
        Section("World: London", CheckWorldLondon); // SelfTestWorldLondon.cs: 08 London (terrain, symmetry, objectives, building rooms and the deck, fair walks, cost; no bot matches)
        Section("Map contract", CheckMapContract); // SelfTestMapContract.cs: every map against its file's "test" block (shape, dressing, objectives, symmetry, nav reach and fair walks, void links, probes, fall kill, a short bot match, horizon)
        Section("Bot vantage points", CheckBotVantage); // SelfTestBotVantage.cs: long-range bots find vantage points (scoring, sniper vs SMG, hold, relocation, sidearm, squads, cost; short scenarios only)
        Section("Server browser", CheckServerBrowser);
        Section("Invite codes", CheckInviteCodes); // SelfTestListing.cs: round trip, typos, versions, the Join box
        Section("STUN", CheckStun); // SelfTestListing.cs: canned answers, fuzz, a loopback fake server
        Section("Server lists", CheckServerListBasics); // SelfTestListing.cs: info/list extensions, addresses, settings, browser merge
        Section("Automatic server list", CheckAutoServerList); // SelfTestListing.cs: host starts a list, second host joins it, learning, hand-over (loopback)
        Section("HUD layout", CheckHudLayout);
        Section("Keybinds", CheckKeybinds); // SelfTestKeybinds.cs: default binds (Interact F, equipment E, no shared keys) and the settings.txt migration
        Section("Weapon select", CheckWeaponSelect);
        Section("Loadout navigation", CheckLoadoutNav);
        Section("Hidden loadout slots", CheckHiddenSlots); // SelfTestHiddenSlots.cs: only unlocked slots listed, locked pick fallback, locked slots kept
        Section("Cosmetic loadout", CheckCosmeticLoadout); // SelfTestCosmeticLoadout.cs: the hat (appearance.txt + migration, server check, bots, replication, layout)
        Section("Main menu width", CheckMainMenuWidth); // SelfTestMainMenu.cs: the panel is button width; long text wraps
        Section("Appearance", CheckAppearance); // SelfTestAppearance.cs: the look (saves, migration, fallback, body, team colour, network, no hat in the loadout)
        Section("Scoreboard level", CheckScoreboardLevel); // SelfTestScoreboard.cs: no HP column, Level for people and bots, prestige label, replication, no allocation
        Section("Recoil", CheckRecoil);
        Section("Sprint spread", CheckSprintSpread); // SelfTestSprintSpread.cs: sprint inaccuracy per class, recovery, crosshair, server floor, bots
        Section("Death cam", CheckDeathCam);
        Section("Map objectives", CheckMapObjectives); // game modes (SelfTestModes.cs)
        Section("Ready-up", CheckReadyUp);
        Section("Team Deathmatch", CheckTeamDeathmatch);
        Section("King of the Hill", CheckKingOfTheHill);
        Section("Capture the Flag", CheckCaptureTheFlag);
        Section("Gun Game", CheckGunGame);
        Section("Mode state and matches", CheckModeMatches);
        Section("Mode modules", CheckModeModules); // SelfTestModeModules.cs: the mode registry, each module's rules and state, seeded bot matches per mode
        Section("Test tools", CheckTestTools); // SelfTestTestTools.cs: 0-bot matches and Kill yourself
        Section("Test Range", CheckTestRange); // SelfTestTestRange.cs: the endless mode registers, and nothing ends its match or changes its map
        Section("Score scaling", CheckScoreScaling); // SelfTestScoreScaling.cs: score limits that scale with the player count
        Section("Team pick", CheckTeamPick); // SelfTestTeams.cs: Join Blue / Red / Auto, the balance rule, bots giving way, switching
        Section("Pre-match lobby", CheckPreMatchLobby); // SelfTestPreMatchLobby.cs: lobby room over loopback (join, teams, ready, cancel, start, map change, late joiner, chat), offline rules, settings, no allocations
        Section("Return to lobby", CheckReturnToLobby); // SelfTestReturnToLobby.cs: the host's BACK TO LOBBY over loopback (clients stay, teams/loadouts/bots kept, a client leaving, a new match, Zombies cleared), the pause menu's exits
        Section("Two-process lobby", CheckTwoProcessLobby); // SelfTestTwoProcessLobby.cs: BACK TO LOBBY against a client in its own process (--net-client) over real RakNet with lag: TDM, Zombies, Horde Stress, a new match
        Section("Team balance", CheckTeamBalance); // SelfTestPreMatchLobby.cs: balance Off and Humans vs Bots over loopback (host + 2 clients + bots)
        Section("Arms", CheckArms);
        Section("Gun models", CheckGunModels); // SelfTestGunModels.cs
        Section("Legs", CheckLegs); // SelfTestLegs.cs: leg IK, gait, foot planting, foot IK on stairs and slopes, cost
        Section("Head bob", CheckHeadBob); // SelfTestHeadBob.cs: first-person camera bob and its setting
        Section("Scene graph", CheckSceneGraph); // SelfTestScene.cs: Source/Scene, ported rigs vs the old draw code
        Section("Branding", CheckBranding); // SelfTestBranding.cs: the BRUTAL logo splash
        Section("Music", CheckMusic); // SelfTestMusic.cs: procedural menu and lobby music, rendered headlessly
        Section("Weird hardbass", CheckWeirdHardbass); // SelfTestWeirdHardbass.cs: odd meters, swerves, toys, new voices, CPU vs the heaviest track
        Section("Frenzy music", CheckFrenzyMusic); // SelfTestFrenzy.cs: the kill-frenzy heat model and adaptive layer
        Section("Frenzy classes", CheckFrenzyClasses); // SelfTestFrenzyClasses.cs: per-weapon-class frenzy music styles
        Section("Frenzy tempo", CheckFrenzyTempo); // SelfTestFrenzyTempo.cs: levels 5 and 6 play faster, per class, on the bar grid
        Section("Frenzy buffs", CheckFrenzyBuffs); // SelfTestFrenzyBuffs.cs: server frenzy per player, replication, class rule, per-class buffs
        Section("Sound", CheckSound); // SelfTestSound.cs: hit sounds, footsteps (surfaces, cadence, voice cap), EFX canary
        Section("Gun samples", CheckGunSamples); // SelfTestGunSamples.cs: recorded gunshot layers (manifest, files, bakes, fallback, cost)
        Section("Loudener", CheckLoudener); // SelfTestLoudener.cs: the loudness stat: bots hear a loud gun further and a suppressed one less far, the shot carries, the flash grows
        Section("Gun mix", CheckGunMix); // SelfTestGunMix.cs: own shot vs other players' by distance, rendered offline through the mixing path
        Section("Audio mixer", CheckAudioMixer); // SelfTestAudioMixer.cs: defaults table, the baked SeabMix2 levels, identity with no audio_mix.json, old saves moving untouched levels to the new defaults, JSON round trip / unknown keys / clamping, slider isolation, mute, F8 migration, no allocations closed or metering
        Section("Text", CheckText); // SelfTestText.cs: player-visible text
        Section("Part text width", CheckPartTextWidth); // SelfTestPartTextWidth.cs: part descriptions wrap at a set width
        Section("Meshes", CheckMeshes); // primitives, glTF models, MeshPool, native canaries (SelfTestMeshes.cs)
        Section("GL 4.6", CheckGl46); // SelfTestGl46.cs: shader preludes for the 4.6 and 4.1 paths, --gl41, the 4.1 tier keeps 4.6 features off
        Section("Sky", CheckSky); // SelfTestSky.cs: sky presets, the "skybox" section, sun, fog at the horizon, image skies
        Section("Map horizons", CheckMapHorizons); // SelfTestSmokeFixes.cs: ground and fog past a boundary, older maps' fog unchanged
        Section("Gunsmith backdrop", CheckShowcaseBackdrop); // SelfTestSmokeFixes.cs: the showcase window is opaque around the 3D gun (no HUD through it)
        Section("GPU timers", CheckGpuStampRing); // SelfTestSmokeFixes.cs: the prop renderer's timestamp timer, scene build frames
        Section("Shot origins", CheckShotOrigins); // SelfTestShotOrigins.cs: every tracer and flash leaves the muzzle of the barrel that fired (view model and world model), classic guns and every standard build
        Section("Weapon builder", CheckWeaponBuilder); // SelfTestWeaponBuilder.cs: Zombies parts, builds, rules, multi-barrel shots
        Section("Workbench UI", CheckWorkbenchUi); // SelfTestWorkbenchUi.cs: one-receiver first-tier grips, linear list, snapping, swaps, copy assembly, missing parts, saves, no per-frame allocations
        Section("Workbench barrels and grips", CheckWorkbenchBarrels); // SelfTestWorkbenchBarrels.cs: barrels fit every receiver (sized by it), per-receiver rules, double-click removal, Chain fire, slot counts 1..9, new receivers
        Section("Part shapes", CheckPartShapes); // SelfTestPartShapes.cs: contoured part shapes (cylinder, tube, wedge, bevel, rounded, capsule, sphere, arc) stay in their box, face out, are shared and reach the drawn gun
        Section("Model connectivity", CheckModelConnectivity); // SelfTestModelConnectivity.cs: no part box floats free of its part or of an assembled weapon (allow-list), irons and with:SOCKET drawing rules
        Section("Parts mechanics", CheckPartsMechanics); // SelfTestPartsMechanics.cs: vertical/grid/rosette and offset splitters, bullet size and speed barrels (a seeded server shot), stronger muzzles, no allocations
        Section("Optic seating", CheckOpticSeating); // SelfTestOpticSeating.cs: every optic rests on the build top (no gap, not sunk in) on every grip x receiver x slot and every standard build, view and world model; the sight line is clear
        Section("Optic mounts", CheckOpticMounts); // SelfTestOpticMounts.cs: every optic sits on top of every receiver on every grip, never inside it; the aim point follows
        Section("Independent fire rates", CheckIndependentFireRates); // SelfTestFireRates.cs: volley receivers each at their own rate, trigger, feed, spool and burst; the server's per-receiver budget; bots; no allocations
        Section("Workbench actions", CheckWorkbenchActions); // SelfTestActions.cs: the fire type is an Action part: sockets, migration, fire timing per type, old builds' feel kept
        Section("Action placement", CheckActionPlacement); // SelfTestActionPlacement.cs: trigger groups under the receiver, pump/ramrod at the front, bolts and drives on the side; no cutting into handle, foregrip or magazine
        Section("Workbench incompatible parts", CheckWorkbenchIncompat); // SelfTestWorkbenchIncompat.cs: "Show incompatible parts" filtering, counts, short reasons, setting, no allocations
        Section("Workbench hidden categories", CheckWorkbenchHideCats); // SelfTestWorkbenchHideCats.cs: categories nothing fits are hidden, fallback to All, receiver brings them back, toggle shows all
        Section("Workbench grip list", CheckWorkbenchGrips); // SelfTestWorkbenchGrips.cs: "Show all grips" off = owned grips, on = every grip (unowned flagged, locked in Zombies), setting, no allocations
        Section("Workbench refused build", CheckWorkbenchRefused); // SelfTestWorkbenchRefused.cs: Build on an incomplete weapon marks exactly the required empty sockets (red rings), they clear as filled, no allocations
        Section("Workbench blueprints", CheckWorkbenchBlueprints); // SelfTestBlueprints.cs: the blueprint library (blueprints.txt, temp folder only): migration of the old slots once, edits round-trip and survive a restart, kinds, removed parts dropped, Zombies loads fill only owned parts and mark the rest (red rings), no allocations
        Section("Workbench row names", CheckWorkbenchNames); // SelfTestWorkbenchNames.cs: the list names each row by its own socket and part after opening builds in turn (unique model versions), every receiver with every socket filled
        Section("Part library", CheckPartLibrary); // SelfTestPartLibrary.cs: registry, fragments, extends, checks, normaliser, hot reload
        Section("Part coverage", CheckPartCoverage); // SelfTestPartCoverage.cs: 3+ feeds per bore (2 common/uncommon), universal ammo on every calibre, 3+ parts per category
        Section("Power curve", CheckPowerCurve); // SelfTestPowerCurve.cs: the rarity curve per category (best of a tier never below the tier under it, damage floors), late tiers only from their rounds (drops, box, slots at full heat and bet)
        Section("Late-game parts", CheckLateGameParts); // SelfTestPowerCurve.cs: the fire-rate receivers are gone and old builds on them migrate; every late-game part loads, fits, measures and connects; the All-Seeing Eye's marks
        Section("Standard builds", CheckStandardBuildSet); // SelfTestStandardBuilds.cs: the prefab build set: loads, unique names, counts per tier, every new part used, power rising by tier; builds have no damage cap, weapon files do
        Section("Zombies mode", CheckZombiesMode); // SelfTestZombies.cs: rounds, spawners, doors, boards, buyables, downs, replication, a bots-only run
        Section("Zombies flow", CheckZombiesFlow); // SelfTestZombiesFlow.cs: crawling while downed, revives vs crawling, spectating the living, ready-up between rounds, respawn at the round's end
        Section("Zombies category slots", CheckZombiesCategorySlots); // SelfTestZombiesCategorySlots.cs: a machine per part category: only its own parts, its own heat, prices, jackpot, distinct cabinets, the win every client sees
        Section("Zombies casino", CheckZombiesCasino); // SelfTestZombiesCasino.cs: casino tables: payout tables and house edges, blackjack dealer rules, roulette coverage, high-low and dice replays, points never negative, forfeits, the client copy, no allocation
        Section("Zombies slots", CheckZombiesSlots); // SelfTestZombiesSlots.cs: slot machines: the odds by round, heat, jackpot rounds, the reels, payouts, bad-luck protection, pull again at once after a loss, the network copy
        Section("Zombies slot consolation", CheckZombiesSlotConsolation); // SelfTestZombiesSlotConsolation.cs: a blank pays a consolation part (tier by bet, never nothing, heat kept, client copy); unlimited banked parts (300 copies, every kind on the wire)
        Section("Zombies no scrap", CheckZombiesNoScrap); // SelfTestZombiesNoScrap.cs: no scrap left in the code or tables, old files still load, salvage piles give parts, a seeded run never mentions scrap
        Section("Zombies more perks", CheckZombiesMorePerks); // SelfTestZombiesMorePerks.cs: 12 perks, each on sale at its own machine (both maps), the doubling price, each new perk's effect, lost on down, the wire, no allocation
        Section("Zombies bot shopping", CheckZombiesBotShop); // SelfTestZombiesBotShop.cs: co-op bots spend their points (priorities, reserve, back at their post, safety, revives first, deterministic, no allocation)
        Section("Zombies box free for all", CheckZombiesBoxGrab); // SelfTestZombiesBoxGrab.cs: the box offer's last seconds are anyone's: the window from the rules, reserved early, free late, one winner in a tick and no accidental spin, the buyer late, a client, co-op bots
        Section("Zombie bots shoot", CheckZombieBotsShoot); // SelfTestZombieBotsShoot.cs: co-op bots fire, hit, kill and reload with the start gun, a bought gun and after a reload
        Section("Ammo effects on players", CheckAmmoEffectsPlayers); // SelfTestAmmoEffectsPlayers.cs: special ammo flags hurt players too (splash, burn, chain, pierce, bounce, bayonet, stun, knockback), friendly fire respected, no allocation
        Section("Zombie animation", CheckZombieAnim); // SelfTestZombieAnim.cs: hands on boards and sill, rising, swipe, variety, network, no allocations
        Section("Zombie dismemberment", CheckZombieLimbs); // SelfTestZombieLimbs.cs: limb hits route to limbs, severing (shotgun, blast), one arm / no arms / one leg / crawler movement and attacks, the crawler hit box, a mixed horde keeps going, the gore cap, client sync, poses, no allocations
        Section("Zombie slugs", CheckZombieSlug); // SelfTestZombieSlug.cs: all four limbs lost is a slug: its low hit box (head shots kill), slugSpeed, a bite at your feet only, no windows, the horde walks over it, not in the alive cap, the stranded cull, client sync and the wriggling pose
        Section("Zombie bodies", CheckZombieBlock); // SelfTestZombieBlock.cs: zombie bodies block players: a wall of zombies stops a sprint, one is slid round, a gap lets you through, spawned or climbed on: pushed out, heads slide you off, crawlers block low, walking zombies held off, client prediction agrees, no allocation, 600-zombie cost
        Section("Zombie sounds", CheckZombieSounds); // SelfTestZombieSounds.cs: zombie voices and footsteps: synthesis, mixer rows, events, voice caps with a horde, no allocations, the head bit over the network
        Section("Zombie hordes", CheckZombieHordes); // SelfTestZombieHordes.cs: 24/100/300-zombie hordes on Blackmoor: tick cost, allocations, snapshot bytes, client cost; the alive curve; the horde controller
        Section("Zombies player scaling", CheckZombiesPlayerScaling); // SelfTestZombiesPlayerScaling.cs: round totals and alive caps for 1-4 players, a join mid-round raises them at once, a leave lowers them without ending the round
        Section("Zombies start round", CheckZombiesStartRound); // SelfTestZombiesStartRound.cs: Horde Test, a run started at round 30 with the test kit (economy, fill time, tick cost), the host's round skip, the settings reaching a client
        Section("Zombie types", CheckZombieTypes); // SelfTestZombieTypes.cs: zombie types: the rules (no ranged attack, the melee limit), each type's spawn, speed and attack, the brute's shove, plates shot off, the grabber's hold and release, the pouncer's leap, the bloater off by default, a wall climber up the tower, boss rounds, the spawn mix by round, a client copy, no allocations, tick cost
        Section("Zombies window entry", CheckZombiesWindowEntry); // SelfTestZombiesWindowEntry.cs: outside players mantle in through open windows (every window on Blackmoor and Ashgrave, both movers), never with a board left, never out from inside, not into locked areas; the outside log and the return after 10 s (points kept), the HUD countdown, no allocation
        Section("Zombie railings", CheckZombieRailings); // SelfTestZombieRailings.cs: stairs, railed balconies and galleries: the body-wide straight test (top rails, posts, no floor), up and down a railed lab balcony by the stairs round a pew in time with no stuck spell (replay, no allocation), the stuck watch's stages and report, Ashgrave's tower stairs and Organ Loft
        Section("Zombies cathedral", CheckZombiesCathedral); // SelfTestZombiesCathedral.cs: Ashgrave Cathedral: the full set on six floors (areas with heights), windows and doors walked, the bell siege (the horde climbs the tower in a pyramid, falls when shot; the reward), a client copy, a replay
        Section("Horde targets", CheckHordeTargets); // SelfTestHordeTargets.cs: a kited horde never turns together to a wrong spot (no false regroup; regrouping only closes up from behind), field drops
        Section("Horde respawn", CheckHordeRespawn); // SelfTestHordeRespawn.cs: the chased player goes down, is revived, bleeds out, respawns, leaves, rejoins (and Second Wind solo): the horde never stalls
        Section("Mass horde", CheckMassHorde); // SelfTestMassHorde.cs: the mass horde spike: 5k/10k/20k crowds on Mass Horde Test (tick cost, allocations, memory, network sketch), promotion/demotion, counts adding up, determinism, off elsewhere
        Section("Crowd agents", CheckCrowdAgents); // SelfTestCrowdAgents.cs: the far crowd's persistent visual zombies: no jumps or pops over a scripted run, counts, serial = parallel, view changes, claims/hints/ray test, horizon culling only hides what the ground hides, particle looks, prep budget
        Section("Mass points", CheckMassPoints); // SelfTestMassPoints.cs: shots and blasts into the mass tier pay what the same damage on individuals pays (volleys, Double Points, Insta-Kill, blasts), wounds kept through the flow, pierce, drops capped, blood and a sunk drawn zombie, ray cost and no allocation, counts adding up
        Section("Horde stress", CheckHordeStress); // SelfTestHordeStress.cs: Horde Stress at 600: staging, descent, merge at the rally point, advance on terrain, endless wave, counts, no allocation
        Section("Horde pace", CheckHordePace); // SelfTestHordePace.cs: the crowd and the mass move at the round's speed mix (round 1 and 20), classes kept through waking and sleeping, the drawn crowd's gaits, far hurt individuals rejoin the crowd and free their slots, counts, no allocation
        Section("Horde wake", CheckHordeWake); // SelfTestHordeWake.cs: waking and sleeping by sight: woken zombies mostly in view, unseen ones sleep first, no flicker over a turn, the horde keeps closing in, a wall ahead stays in front, the sight's ground test, co-op views, no allocation
        Section("Zombies drop items", CheckZombiesDropItems); // SelfTestZombiesDropItems.cs: drop the gun in hand (never the last) and loose parts, teammates pick up, same-tick grabs never duplicate, bots ignore pickups, the cap, despawn at the next round's end and by time, client sync
        Section("Zombies trader", CheckZombiesTrader); // SelfTestZombiesTrader.cs: the trader every 5th round (comes at 5/10/15, leaves on time, a fresh shelf of distinct parts below the round's favoured tier, prices by rarity and round), purchases and refusals, the last item wanted by two at once, the client copy, a bot buys one part a visit, no allocation
        Section("Zombies use reach", CheckZombiesUseReach); // SelfTestZombiesUseReach.cs: the use key reaches in 3D and in sight: every interactable on every Zombies map usable from its own floor; a machine used from in front, not from the floor above or below (prompt or a forged use) nor through a wall
        Section("Zombies grenades", CheckZombiesGrenades); // SelfTestZombiesGrenades.cs: the Zombies grenade thrown by the key (host and loopback client) kills a zombie cluster with credit and points, the blast reaction, the per-round refill 1 + floor((round - 1) / 3)
        Section("Modular grenades", CheckModularGrenades); // SelfTestModularGrenades.cs: grenade parts load, every legal combo builds, each fuse and payload against zombies, clusters, refill, loopback
        Section("Co-op", CheckCoop); // SelfTestPve.cs: missions, the spawn director, Silo Breach, out-of-sight spawns, recycling, 80 enemies (cost, allocations, replication, bandwidth), enemy fire, every objective, wipes and checkpoints
        Section("Friendly hits", CheckFriendlyHits); // SelfTestFriendlyHits.cs: friendly-flagged hits on teammates (bullets, melee, grenades), FFA, Zombies, sound, client marker
        Section("Name tags", CheckNameTags); // SelfTestNameTags.cs: HUD world-to-screen projection, name tag rules, layout, settings, no allocations
        Section("Prestige", CheckPrestige); // SelfTestPrestige.cs: prestige rules and saves, hat catalog, bots never wear hats, hat models, no gameplay effect
        Section("Decals", CheckDecals); // SelfTestDecals.cs: decal files, atlas, Decal/DecalScatter baking, impact pool rules, costs, the Decal Test map
        Section("Decal surfaces", CheckDecalSurfaces); // SelfTestDecalSurfaces.cs: the surface and object decal set (loads, surfaces, bold detail, paint cost) and the Decal Surfaces map
        Section("Pack decals", CheckPackDecals); // SelfTestPackDecals.cs: pack-local decals (Maps/<Pack>/Decals), shadowing, scope, atlas budget
        Section("Body model", CheckBodyModel); // SelfTestBody.cs: torso and head parts vs the old boxes and hit boxes, hat fit, seeded variants, cost
        Section("Frenzy visuals", CheckFrenzyVisuals); // SelfTestFrenzyVisuals.cs: per-class frenzy looks, level scaling, setting, no strobing, no allocations, CPU cost
        Section("Frenzy gun sound", CheckFrenzyGunSound); // SelfTestFrenzyGunSound.cs: per-class frenzy layers on your gun: data, level scaling, loudness, bakes, cost, setting
        Section("Insane bots", CheckInsaneBots); // SelfTestInsaneBots.cs: squads, roles, no wallhacks, suppress + flank, trades, regroup, Insane v Hard, CPU
        Section("Bots and foliage", CheckBotFoliage); // SelfTestBotFoliage.cs: no seeing through foliage (by stance), blind fire at estimates, timeouts, walls, corn, costs
        Section("Tactical gear", CheckTacticalGear); // SelfTestTactical.cs: the eight tactical items: effects, devices, replication, bots, unlocks, saves
        Section("Cursor capture", CheckCursorPolicy); // SelfTestCursor.cs: when the mouse is captured or free (phase x menus x click), the map vote keeps the cursor
        Section("Throw input", CheckThrowInput); // SelfTestThrowInput.cs: grenade and equipment keys end to end (every item, each match phase, offline and loopback, bots)
        Section("Launchers", CheckLaunchers); // SelfTestLaunchers.cs: launcher files, projectile flight, blasts, arming, lock-on vs streaks, ammo, sticky/cluster, packets, bots, no allocations
        Section("Bullet penetration", CheckPenetration); // SelfTestPenetration.cs: materials, weapon powers, walls passed or not, cap, exits, server shots, lag comp, decals, bots, map override, no allocations
        Section("Bot lag compensation", CheckBotLagComp); // SelfTestLagComp.cs: shots at the current tick rewind later-updated players right (no join-order bias)
        Section("Ragdoll soft limits", CheckRagdollSoft); // SelfTestRagdollSoft.cs: Ragdolls/Ragdolls.json, soft joint limits and backstops, the relax pull and sleep, the stronger shot push, a scripted fall
        Section("Active ragdolls", CheckRagdollActive); // SelfTestRagdollActive.cs: the kill packet's death fields, zombie death info, overkill thresholds, every reaction runs and ends limp, PCs agree, caps, no allocations
        Section("Ragdolls and knockback", CheckRagdolls); // SelfTestRagdoll.cs: ragdoll falls (flat, stairs, slope, wall, terrain), determinism, pushes, hats, cost; blast knockback, Flak Jacket, walls, teams, knock message, client prediction
        Section("Blast push", CheckBlastPush); // SelfTestBlastPush.cs: every blast throws survivors (frag, impact, each launcher, landmine, barrel, mortar), stagger, Flak/prone, floor, teammates, bots, feedback, no allocations
        Section("Blood overlay", CheckBloodOverlay); // SelfTestBlood.cs: screen splatters by health: steps, hit direction, regen / Stim fade, death, Subtle / Off, masks, no allocations, settings
        Section("Body blood", CheckBodyBlood); // SelfTestBlood.cs: marks by health on bodies: counts, parts, server recording, armour, two clients alike, packet, ragdolls, CPU for 32
        Section("Gibs", CheckGibs); // SelfTestGibs.cs: animal props (health, toys), gib bursts by size and gore setting, settle/fade/cap/splats, in-game breaks, respawn, two-client determinism, blast-kill gibs vs ragdolls, hats, zombies
        Section("Chat and voice", CheckChatVoice); // SelfTestChatVoice.cs: chat routing (all / team / dead / mute) over loopback, voice codec SNR, jitter buffer, proximity + occlusion, relay range / team radio, bandwidth, no allocations, settings
        Section("Voice boost", CheckVoiceBoost); // SelfTestVoiceBoost.cs: voice volume to 300%: sample/source split, boost, limiter (no clipping, recovery), soft knee, settings round trip, no allocations
        Section("Planet gravity", CheckPlanetGravity); // SelfTestPlanet.cs: point gravity frames, a walk round a planet, jumps, grenades, replays, horizon, ragdolls, Pocket Planet's nav and a bot pole to pole, costs
        Section("Weapon files", CheckWeaponFiles); // last: its file parsing and reflection mustn't disturb the tick timings above
        Section("Perf window", CheckPerfWindow); // SelfTestPerf.cs: the F9 performance window: scopes nest as the area tree says, out-of-order scopes caught, nothing timed off or on other threads, 0 allocations per frame timing or off, frame stats (1% low, worst frames), GPU pass timer never reads early / no queries while closed, F9 migration
        Section("UI frame spikes", CheckUiFrameSpikes); // SelfTestUiFrames.cs: headless ImGui frames with the HUD and the F9 window, no frame far over the median (CPU time; enforced on Windows), UI perf sub-areas
        Section("Frame allocations", CheckFrameAllocs); // SelfTestFrameAllocs.cs: a loopback host and client on Horde Stress with the HUD and the F9 window (closed, window, overlay) drawn headless: 0 B per steady frame on the main thread, by perf area
        Section("Frame cap", CheckFrameCap); // SelfTestFrameCap.cs: Max FPS: which cap applies (VSync, menus, background), 20-1000, the limiter schedule (exact rate, late wakes made up, no burst after a long frame), short real waits (never early, 0 allocations), settings round-trip
        Section("Weapon tweaks", CheckWeaponTweaks); // SelfTestWeaponTweaks.cs: the dev tweak window: live tweaks and kitted copies, class multipliers, revert / reset, Save into temp copies (minimal patch, loads back, --check-weapon), fingerprint and fits untouched, host -> client over loopback, F7, no allocations closed
        Section("Dev tools", CheckDevTools); // SelfTestDevTools.cs: the look-at inspector names the right box / prop / turned part and its map op, the prop editor's edits go live, round-trip through a change package (prop file and map op patch validate) and revert exactly, F6 / F5 / F10, no allocations closed or per look
        Section("Paste and packets", CheckPaste); // SelfTestPaste.cs: image paste crash, text boxes, received text, packet fuzzing (after the timings too)
        Section("Updates", CheckUpdates); // SelfTestUpdates.cs: versions, manifests, cache, check + install against a fake GitHub (no network, temp folders)
        Section("Update cleanup", CheckUpdateCleanup); // SelfTestUpdateCleanup.cs: build markers, what the cleanup removes and keeps in a fake install root (temp folders only), rollback, locks, resuming
        Section("Mac release", CheckMacRelease); // SelfTestMac.cs: app bundle data/log folders, Mac save folder, per-platform update.json, unzipping and opening CallOfSlop.app, a Mac check + install against a fake GitHub (temp folders only)
        Section("Save folder", CheckSaves); // SelfTestSaves.cs: save paths, portable/--save-dir, migration, atomic writes + .bak, newer-format guard (temp folders only)
        // space-b maps
        // space-d maps
        // space-a maps
        // gravity-maps
        Section("Gravity zones", CheckGravityZones); // SelfTestGravityZones.cs: several gravity regions on one map: fields, files, frames, a walker up a pipe onto a wall, a grenade on the wall, low-g jumps, replays, nav, costs
        Section("Space: gravity physics", CheckSpaceGravityMaps); // SelfTestSpaceGravity.cs: Space 03 Keelhaul Yard and 04 Twin Shard: zones, nav by area, stands in every gravity, the hatch jump and the Pebble loop (the rest is the Map contract's)
        Section("Gravity strength", CheckGravityStrength); // SelfTestGravityStrength.cs: the host's gravity setting: every field type scales, jumps and falls as 1/g, Pocket Planet 11 m/s2 and its 60% floor, nav unchanged, clients and the browser get it, seeded bot matches at 25% and 200%
        Section("Map kit", CheckMapKit); // SelfTestMapKit.cs: Source/MapKit: map files round-trip, generators reproduce their maps, MapChecks.Check agrees with the Space sections
        Section("Steam platform", CheckSteamPlatform); // SelfTestSteam.cs: Source/Platform/Steam: off without Steam, the start decision, the native library ships, the layer on a recorded backend, Steam ids over loopback
        Section("Steam transport", CheckSteamTransport); // SelfTestSteamTransport.cs: the transport interface (RakNet and Steam sockets on the recorded backend), a game over Steam, lobbies and the browser, Rich Presence, auth tickets, bans
    }

    /// <summary>One section of the suite. <paramref name="needs"/> names earlier sections whose results this one uses,
    /// so a filtered run (--selftest-only) of this section runs them first.</summary>
    private static void Section(string name, Action body, params string[] needs)
    {
        if (_collecting)
        {
            _names.Add(name);
            if (needs.Length > 0)
                _needs[name] = needs;
            return;
        }
        if (_plan != null && !_plan.Contains(name))
            return;
        Line("");
        Line($"== {name} ==");
        var t0 = Stopwatch.GetTimestamp();
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Fail($"{name} threw: {ex}");
        }
        var secs = Stopwatch.GetElapsedTime(t0).TotalSeconds;
        _times.Add((name, secs));
        Line($"-- {name}: {secs:0.00} s");
    }

    // ---- 1. Maps -------------------------------------------------------------------------

    private static void CheckMaps()
    {
        var names = new HashSet<string>();
        foreach (var m in Maps.All)
        {
            if (!names.Add(m.Name))
                Fail($"duplicate map name {m.Name}");
            var mats = m.Materials;
            var blocked = m.Spawns.Count(s => m.IsBlocked(s));
            var groundSpawns = m.Spawns.Count(s => s.Y == 0);
            Line($"{m.Id,2} {m.Name,-14} {m.Size,-6} {m.HalfX * 2:0}x{m.HalfZ * 2:0} m  boxes {m.Solid.Count,5}  spawns {m.Spawns.Count,2}  waypoints {m.Waypoints.Count,4}  materials {string.Join(",", mats.GroupBy(x => x).Select(g => $"{g.Key}:{g.Count()}"))}");
            if (m.Spawns.Count < m.WantedMaxPlayers)
                Fail($"{m.Name}: only {m.Spawns.Count} spawns (need {m.WantedMaxPlayers} for a full server)");
            if (blocked > 0)
                Fail($"{m.Name}: {blocked} spawn(s) still inside geometry");
            if (m.Waypoints.Count < 6)
                Fail($"{m.Name}: only {m.Waypoints.Count} waypoints");
            foreach (var b in m.Solid)
                if (!float.IsFinite(b.Center.X + b.Center.Y + b.Center.Z + b.Half.X + b.Half.Y + b.Half.Z) || b.Half.X <= 0 || b.Half.Y <= 0 || b.Half.Z <= 0)
                {
                    Fail($"{m.Name}: bad box {b.Center} {b.Half}");
                    break;
                }
            // Spawn exposure: is anyone standing on a spawn in plain sight of half the other spawns?
            _ = groundSpawns;
        }
    }

    // ---- 1b. Map files ----------------------------------------------------------------------

    /// <summary>Every map written to a file and read back must build exactly the same boxes, colours, spawns and waypoints.</summary>
    private static void CheckMapFiles()
    {
        var sources = Maps.All.Concat(Maps.Fallback()).ToList(); // the loaded maps (from files) and the code-defined fallback
        var checkedCount = 0;
        foreach (var original in sources)
        {
            var json = MapFile.ToJson(original);
            Map copy;
            try
            {
                copy = MapFile.Load(json, original.Name + " (round trip)", original.PackDir ?? ""); // same pack: its own props and models
            }
            catch (MapFileException ex)
            {
                Fail(ex.Message);
                continue;
            }
            if (original.SourceFile != null)
                copy.Validate(); // loaded maps have been validated; do the same to the copy
            var same = copy.Solid.Count == original.Solid.Count && copy.Solid.SequenceEqual(original.Solid)
                       && copy.Colors.SequenceEqual(original.Colors) && copy.Spawns.SequenceEqual(original.Spawns)
                       && copy.Waypoints.SequenceEqual(original.Waypoints) && copy.HalfX == original.HalfX && copy.HalfZ == original.HalfZ
                       && copy.Sky == original.Sky && copy.PrototypeGrid == original.PrototypeGrid;
            if (!same)
                Fail($"{original.Name}: map file round trip changed the map (boxes {original.Solid.Count} -> {copy.Solid.Count}, spawns {original.Spawns.Count} -> {copy.Spawns.Count})");
            checkedCount++;
        }
        Line($"{checkedCount} maps written and re-read identically; {Maps.All.Count(m => m.SourceFile != null)} of {Maps.All.Length} loaded from files in {Maps.Folder}");
        foreach (var e in Maps.LoadErrors)
            Fail("map file problem: " + e); // the shipped maps must all load
        if (Directory.Exists(Maps.Folder) && Maps.All.Count(m => m.SourceFile != null) < 24)
            Fail($"only {Maps.All.Count(m => m.SourceFile != null)} maps loaded from {Maps.Folder} (24 ship with the game)");

        // The example in the map makers' guide must actually load
        var guide = Path.Combine(Maps.Folder, "README.md");
        if (File.Exists(guide))
        {
            var text = File.ReadAllText(guide);
            var start = text.IndexOf("```json", StringComparison.Ordinal);
            var end = start < 0 ? -1 : text.IndexOf("```", start + 7, StringComparison.Ordinal);
            if (start < 0 || end < 0)
                Fail("Maps/README.md has no ```json example");
            else
            {
                try
                {
                    var ex = MapFile.Load(text[(start + 7)..end], "README example");
                    ex.Validate();
                    Line($"guide example '{ex.Name}' loads: {ex.Solid.Count} boxes, {ex.Spawns.Count} spawns, {ex.Waypoints.Count} waypoints");
                }
                catch (MapFileException ex)
                {
                    Fail("the example in Maps/README.md doesn't load: " + ex.Message);
                }
            }
        }
        else
            Fail("Maps/README.md (the map makers' guide) wasn't copied next to the game");

        // A broken file gives a useful message instead of a crash
        foreach (var (bad, expect) in new[]
                 {
                     ("{ \"name\": \"X\", \"halfX\": 20, \"halfZ\": 20, \"ops\": [ { \"op\": \"Bulding\" } ] }", "unknown"),
                     ("{ \"name\": \"X\", \"halfX\": 20, \"halfZ\": 20, \"ops\": [ { \"op\": \"Prop\", \"x\": 1 } ] }", "needs"),
                     ("{ \"name\": \"X\", \"halfX\": 20, \"halfZ\": 20, \"ops\": [ { \"op\": \"Ground\", \"color\": [0.5, 0.5, 0.5] } ] }", "spawn"),
                 })
        {
            try
            {
                MapFile.Load(bad, "bad.json");
                Fail($"a broken map file loaded without complaint (expected '{expect}')");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (Exception ex)
            {
                Fail($"broken map file gave the wrong error: {ex.Message}");
            }
        }
    }

    // ---- 1c. Map reload ----------------------------------------------------------------------

    /// <summary>Reloading the folder changes nothing when no file changed, and a mid-match swap never leaves anyone inside a wall.</summary>
    private static void CheckMapReload()
    {
        var before = Maps.All.Select(m => m.Name).ToArray();
        var gen = Maps.Generation;
        var r = Maps.Reload();
        if (r.Added.Length + r.Removed.Length + r.Changed.Length > 0 || r.Count != before.Length)
            Fail("reloading unchanged map files reported changes: " + r.Summary);
        if (!Maps.All.Select(m => m.Name).SequenceEqual(before) || Maps.Generation != gen + 1)
            Fail("reload changed the map list or didn't bump Generation");
        Line("reload: " + r.Summary);

        // Single player on Proto Box; then the "edited" map puts a big block where the player stands.
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: 3, botSkill: BotDifficulty.Normal, mapId: 0);
        var name = World.Map.Name;
        var me = game.Players.Values.First(p => p.Bot == null);
        me.Position = new Vector3(0, 0, 0);
        var edited = MapFile.Load($$"""
            { "name": "{{name}}", "halfX": 20, "halfZ": 20, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] },
              { "op": "Block", "x0": -3, "y0": 0, "z0": -3, "x1": 3, "y1": 6, "z1": 3, "color": "#808080" },
              { "op": "Prop", "x": 10, "z": 0, "w": 2, "h": 1, "d": 2, "color": "#f28026" },
              { "op": "RingSpawns", "count": 8, "frac": 0.8 }, { "op": "AutoWaypoints", "spacing": 6 } ] }
            """, "edited.json");
        edited.Validate();
        game.HotSwapMap(edited);
        var stuck = game.Players.Values.Where(p => p.Alive && edited.IsBlocked(p.Position)).Select(p => p.Name).ToList();
        if (World.Map != edited)
            Fail("hot swap didn't switch the map");
        if (stuck.Count > 0)
            Fail("hot swap left players inside geometry: " + string.Join(", ", stuck));
        Line($"hot swap: player moved from (0, 0) to ({me.Position.X:0.0}, {me.Position.Z:0.0}), nobody stuck");

        // Keep playing for a few seconds on the swapped map: bots must find new waypoints without errors.
        const double dt = 1.0 / World.TickRate;
        for (var t = 0.0; t < 5; t += dt)
            game.Update(t, dt);
        if (game.Players.Values.Any(p => float.IsNaN(p.Position.X) || p.Position.Y < -1))
            Fail("players went bad after the hot swap");
        World.SetMap(Maps.All[0]);
    }

    // ---- 1d. Map packs -------------------------------------------------------------------------

    /// <summary>
    /// Pack folders: the shipped Legacy pack (Classic, Collision Point and High Ground together), pack.json never loading as a map, folders without pack.json, loose files
    /// forming "Other", random picks and vote options staying inside the pack. Uses a throwaway folder in the temp
    /// directory (deleted afterwards), never the game's own Maps folder.
    /// </summary>
    private static void CheckMapPacks()
    {
        // The real Maps folder
        var classic = Maps.FindPack("Legacy");
        if (Directory.Exists(Maps.Folder))
        {
            if (classic == null)
                Fail("no Legacy pack (Maps/Legacy)");
            else if (classic.Maps.Count != 36)
                Fail($"Legacy has {classic.Maps.Count} maps (36: the 24 Classic, 6 Collision Point and 6 High Ground maps)");
            else if (classic.Description.Length == 0 || classic.Order != 1)
                Fail("Legacy/pack.json wasn't read (no description or order)");
            else if (classic.Maps[0].Name != "Proto Box" || Maps.All[0] != classic.Maps[0])
                Fail($"Legacy's first map is {classic.Maps[0].Name}, the list's {Maps.All[0].Name} (expected Proto Box: many tests use the first map)");
            else if (Maps.Packs.Any(p => p.Folder is "Classic" or "CollisionPoint" or "HighGround"))
                Fail("an old pack folder (Classic, CollisionPoint, HighGround) is still there: those maps moved to Maps/Legacy");
            // The old relative order: Classic 1-24, then Collision Point, then High Ground.
            var legacyOrder = classic?.Maps.Select(m => m.Name).ToArray() ?? [];
            var wantAfterClassic = new[] { "Blueprint", "Galleria", "Floodgate", "Trefoil", "Citadel", "Confluence", "Gravel Pit", "Hillfort", "Riverbend", "Adobe Mesa", "Ridgeline", "Alpine Pass" };
            if (legacyOrder.Length == 36 && (legacyOrder[23] != "Military Base" || !legacyOrder[24..].SequenceEqual(wantAfterClassic)))
                Fail($"Legacy's map order changed: {string.Join(", ", legacyOrder)}");
        }
        if (Maps.All.Any(m => m.Pack == null || !m.Pack.Maps.Contains(m)))
            Fail("a loaded map has no pack");
        if (Maps.Packs.Sum(p => p.Maps.Count) != Maps.All.Length)
            Fail("the packs don't add up to the map list");
        if (Maps.All.Any(m => m.SourceFile != null && Path.GetFileName(m.SourceFile).Equals(MapPack.FileName, StringComparison.OrdinalIgnoreCase)))
            Fail("a pack.json was loaded as a map");
        Line("packs: " + string.Join("; ", Maps.Packs.Select(p => $"{p.Name} ({p.SizeSummary}){(p.Folder != null ? "" : " [loose files]")}")));

        // A made-up Maps folder
        var dir = Path.Combine(Path.GetTempPath(), "callofslop-packtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            static string MapJson(string name, float half) => $$"""
                { "name": "{{name}}", "halfX": {{half}}, "halfZ": {{half}}, "ops": [
                  { "op": "Ground", "color": [0.4, 0.4, 0.4] },
                  { "op": "Prop", "x": 0, "z": 0, "w": 2, "h": 1, "d": 2, "color": "#f28026" },
                  { "op": "RingSpawns", "count": 8, "frac": 0.8 }, { "op": "AutoWaypoints", "spacing": 6 } ] }
                """;
            void Write(string rel, string text)
            {
                var path = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            Write("Alpha/pack.json", """{ "format": "call-of-slop-pack/1", "name": "Alpha Pack", "description": "Test pack.", "author": "Self-test", "order": 5 }""");
            Write("Alpha/a1.json", MapJson("Test A1", 20));
            Write("Alpha/deeper/a2.json", MapJson("Test A2", 50)); // nested: still Alpha
            Write("Alpha/a3.json", MapJson("Test A3", 100));
            Write("Beta/b1.json", MapJson("Test B1", 20)); // no pack.json: named after the folder
            Write("Gamma/pack.json", "{ this is not json");      // broken pack.json: still a pack, with an error
            Write("Gamma/g1.json", MapJson("Test G1", 20));
            Write("loose.json", MapJson("Test Loose", 20));      // loose file: the Other pack
            Write("b1-leftover.json", MapJson("Test B1", 20));   // identical loose copy (old build output): ignored quietly
            Write("Classic/pack.json", """{ "format": "call-of-slop-pack/1", "name": "Classic", "order": 1 }"""); // a retired pack folder left over from an old build
            Write("Classic/01-a1.json", MapJson("Test A1", 20));  // identical to Alpha's: retired folders load last, so it is ignored quietly and Alpha keeps it
            Write("Classic/02-c2.json", MapJson("Test C2", 20));  // a map of its own: still loads, as pack Classic

            var errors = new List<string>();
            var (all, packs) = Maps.LoadFolder(dir, errors);
            Line($"test folder: {all.Length} maps; packs {string.Join(", ", packs.Select(p => $"{p.Name} [{string.Join(", ", p.Maps.Select(m => m.Name))}]"))}; errors: {string.Join(" | ", errors)}");
            var names = packs.Select(p => p.Name).ToArray();
            if (!names.SequenceEqual(["Classic", "Alpha Pack", "Beta", "Gamma", MapPack.LooseName]))
                Fail($"test packs came out as {string.Join(", ", names)} (expected Classic, Alpha Pack, Beta, Gamma, {MapPack.LooseName})");
            if (all.Length != 7)
                Fail($"test folder loaded {all.Length} maps (expected 7: pack.json files and the leftover copies aren't maps)");
            if (all.FirstOrDefault(m => m.Name == "Test A1")?.Pack?.Name != "Alpha Pack" || all.FirstOrDefault(m => m.Name == "Test C2")?.Pack?.Name != "Classic")
                Fail("a leftover copy in a retired pack folder (Classic) pushed out the real map, or the folder's own map didn't load");
            if (errors.Count != 1 || !errors[0].Contains("Gamma", StringComparison.Ordinal))
                Fail($"expected exactly one error, for Gamma/pack.json; got {errors.Count}");
            var alpha = packs.FirstOrDefault(p => p.Name == "Alpha Pack");
            var beta = packs.FirstOrDefault(p => p.Name == "Beta");
            var loose = packs.FirstOrDefault(p => p.Name == MapPack.LooseName);
            if (alpha is not { Maps.Count: 3, Order: 5, Author: "Self-test", Folder: "Alpha" })
                Fail("Alpha's pack.json fields or its nested map didn't load");
            if (loose == null || loose.Maps.Count != 1 || loose.Maps[0].Name != "Test Loose" || loose.Folder != null)
                Fail("the loose map file didn't become the Other pack");
            if (beta == null || beta.Maps.Count != 1 || all.Any(m => m.Pack == null || !m.Pack.Maps.Contains(m)))
                Fail("maps and packs don't point at each other");

            if (alpha != null && beta != null)
            {
                // Random picks stay inside the selected pack; a pack that's gone falls back to every map.
                var rng = new Random(1);
                var s = new Settings { MapRandom = true, MapPack = "alpha pack" };
                var picks = Enumerable.Range(0, 200).Select(_ => s.PickMap(packs, all, rng)).ToList();
                if (picks.Any(m => m.Pack != alpha) || picks.Distinct().Count() != 3)
                    Fail("random map picked outside the pack (or never picked some of its maps)");
                s.MapPack = "No Such Pack";
                if (Enumerable.Range(0, 200).Select(_ => s.PickMap(packs, all, rng)).Distinct().Count() != all.Length)
                    Fail("a missing pack didn't fall back to all maps");
                s.MapRandom = false;
                s.MapName = "Test B1";
                if (s.PickMap(packs, all, rng).Name != "Test B1")
                    Fail("a chosen map wasn't the one started");

                // Vote options: the pack just played; a pack without 2 other maps falls back to everything.
                var pool = Game.VotePool("Test A1", alpha, all);
                if (pool.Count != 2 || pool.Any(m => m.Pack != alpha || m.Name == "Test A1"))
                    Fail($"vote options left the pack: {string.Join(", ", pool.Select(m => m.Name))}");
                if (Game.VotePool("Test B1", beta, all).Count != all.Length - 1)
                    Fail("a one-map pack didn't fall back to all maps for the vote");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }

        // The same with the real packs
        if (classic != null)
        {
            var s = new Settings { MapRandom = true, MapPack = classic.Name };
            if (Enumerable.Range(0, 200).Any(_ => s.PickMap().Pack != classic))
                Fail($"PickMap left the {classic.Name} pack");
            var played = classic.Maps[0];
            var pool = Game.VotePool(played.Name, classic, Maps.All);
            if (pool.Count != classic.Maps.Count - 1 || pool.Any(m => m.Pack != classic))
                Fail($"{classic.Name} vote options left the pack");
            Line($"PickMap and vote options stay inside {classic.Name} ({pool.Count} vote candidates after {played.Name})");
        }
    }

    // ---- 2. Matches ----------------------------------------------------------------------

    /// <summary>Most milliseconds of work one map's nav graph may take to build (Bots), on a machine as fast as <see cref="NavBuildReferenceMs"/> says.</summary>
    private const double MaxNavBuildMs = 3000;

    /// <summary>
    /// Every shipped map's nav graph but the over-limit ones, built one after another: under this on a GitHub runner or a
    /// gaming PC (about 9 s on the developer's PC in 2026-10; about 29 s on the 2-core agent box). A machine slower than this
    /// scales the per-map limit.
    /// </summary>
    private const double NavBuildReferenceMs = 15000;

    private static void CheckMatches()
    {
        var botStats = _matchBotStats = new BotStats();
        foreach (var m in Maps.All)
        {
            var sw = Stopwatch.StartNew();
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: Game.ClassicPlayers - 1, botSkill: BotDifficulty.Normal, mapId: m.Id);
            game.MapVoteEnabled = false; // stay on this map for the whole test
            Game.SettleMemory(); // as the game does when a session starts (the 31 games before this one left garbage)
            const double dt = 1.0 / World.TickRate;
            var now = 0.0;
            var lastPos = new Dictionary<byte, Vector3>();
            var moved = new Dictionary<byte, float>();
            var maxTickMs = 0.0;
            var slowTicks = 0; // ticks over the 16 ms budget (one-off spikes from GC or a busy CI machine are fine)
            var cpu = new TickCpu(); // the work done in each tick, apart from time the machine gave to other programs
            var phasesSeen = new HashSet<MatchPhase>();
            var killsDone = 0;
            var lastKillSum = 0;
            var lastMatch = game.MatchNumber;
            var tick = 0;
            // Made once (a lambda per tick would allocate in the measured window, and could set off a GC there).
            Func<string> context = () => $"t={tick / 60.0:0.00} s, {game.Phase} match {game.MatchNumber}, nav {(NavGraph.Current.Nodes.Length > 0 ? "ready" : "building")}, last feed line: {game.Feed.LastOrDefault().Text}";
            AllocProfile.Reset();
            long allocBytes = 0;
            var gcs = 0;
            try
            {
                for (var i = 0; i < 90 * World.TickRate; i++)
                {
                    now += dt;
                    tick = i;
                    AllocProfile.On = i > 60; // where the game allocates (after the first second's warm-up)
                    var gc0 = GC.CollectionCount(0);
                    var a0 = GC.GetAllocatedBytesForCurrentThread();
                    cpu.Start();
                    var t0 = Stopwatch.GetTimestamp();
                    game.Update(now, dt);
                    var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    cpu.Stop(ms, count: i > 60, context); // said only if the check fails
                    AllocProfile.On = false;
                    if (i > 60)
                    {
                        allocBytes += GC.GetAllocatedBytesForCurrentThread() - a0;
                        gcs += GC.CollectionCount(0) - gc0;
                    }
                    if (i > 60)
                    {
                        maxTickMs = Math.Max(maxTickMs, ms);
                        if (ms > 16)
                            slowTicks++;
                    }
                    phasesSeen.Add(game.Phase);
                    if (game.MatchNumber != lastMatch)
                    {
                        killsDone += lastKillSum; // scores reset at a new match
                        lastMatch = game.MatchNumber;
                    }
                    lastKillSum = 0;
                    foreach (var p in game.Players.Values)
                        lastKillSum += p.Kills;
                    if (i % 60 == 0)
                    {
                        foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                        {
                            if (lastPos.TryGetValue(p.Id, out var lp))
                                moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                            lastPos[p.Id] = p.Position;
                        }
                    }
                    foreach (var p in game.Players.Values)
                        if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                            throw new Exception($"player {p.Id} position is NaN");
                }
            }
            catch (Exception ex)
            {
                Fail($"{m.Name}: match crashed: {ex}");
                continue;
            }

            botStats.Add(game.BotStats);
            var bots = game.Players.Values.Where(p => p.Bot != null).ToList();
            var kills = killsDone + game.Players.Values.Sum(p => (int)p.Kills);
            var fallen = game.Players.Values.Count(p => p.Alive && (m.PointGravity is { } planet ? Vector3.Distance(p.Position, planet.Center) < m.PlanetRadius - 0.5f // a planet: inside the ball
                : m.FallKillY is { } kill && !m.HasTerrain ? p.Position.Y < kill - 0.5f // a void map: its floors aren't at y = 0 (Keelhaul Yard's hold is at -3), the void is below the fall-kill height (as the Map contract checks)
                : p.Position.Y < m.GroundHeight(p.Position.X, p.Position.Z, p.Position.Y) - 0.5f
                || (m.Terrain is { Voids: not null } td && td.SolidAt(p.Position.X, p.Position.Z, p.Position.Y + 0.3f)))); // below the floor (or the terrain; in a terrain cave, its floor), or inside cave rock
            var avgMove = moved.Count == 0 ? 0 : moved.Values.Average();
            var stuck = moved.Count(kv => kv.Value < 15f);
            Line($"{m.Name,-14} kills {kills,3}  matches {game.MatchNumber}  phases {string.Join("/", phasesSeen)}  avg bot travel {avgMove,5:0} m  stuck bots {stuck}  stuck self-kills {game.BotStuckKills}  stuck spots {StuckSpots.Session.On(m.Name).Count(x => x.MaxStage >= 2)} (+{StuckSpots.Session.On(m.Name).Count(x => x.MaxStage < 2)} stage 1)  worst tick {maxTickMs,5:0.00} ms (work {cpu.WorstText})  ({sw.Elapsed.TotalSeconds:0.0} s)");
            var measuredTicks = 90 * World.TickRate - 61;
            // What the game allocates per tick, and where (AllocProfile): garbage sets off GC pauses on the game thread, and a
            // pause inside a tick counts as that tick's work. Today a few hundred bytes at most (events: kills, respawns, a
            // nav build); a steady per-tick allocation (once: the snapshot's slow-field compare boxing, ~2.6 KB a tick on
            // every map) shows up here long before it fails the 8 ms rule by chance.
            var perTickAlloc = allocBytes / (double)measuredTicks;
            var allocText = $"{perTickAlloc:0} B/tick, {gcs} GC(s) in the match (by section: {AllocProfile.Top(6, measuredTicks)})";
            Line($"  allocations: {allocText}");
            if (perTickAlloc > MaxMatchAllocPerTick)
                Fail($"{m.Name}: a bot match allocates {allocText}, over {MaxMatchAllocPerTick} B/tick");
            if (bots.Count != Game.ClassicPlayers - 1)
                Fail($"{m.Name}: expected {Game.ClassicPlayers - 1} bots, got {bots.Count}");
            if (kills == 0 && m.Size != MapSize.Large)
                Fail($"{m.Name}: no kills in 90 s with 7 bots");
            if (fallen > 0)
                Fail($"{m.Name}: {fallen} player(s) below the floor");
            if (stuck > 1)
                Fail($"{m.Name}: {stuck} bots barely moved (stuck on geometry?)");
            if (game.BotStuckKills > 0)
                StuckNotes.Add($"{m.Name}: {game.BotStuckKills} stuck self-kill(s) in the 90 s match; last: {game.LastStuckLog}");
            if (game.BotStuckKills > 5)
                Fail($"{m.Name}: {game.BotStuckKills} stuck bot self-kills in 90 s (the stuck watch misfires, or a bad map problem; last: {game.LastStuckLog})");
            // The work a tick does (its thread's CPU time) is the real measure: no tick may use over half the 16 ms
            // budget. Wall-clock time also counts moments the machine gave the CPU to other programs: GitHub's 2-core
            // runners regularly stall a tick that did 0.1-0.5 ms of work for 20-60 ms (once 100-120 ms), so that rule
            // only catches many slow ticks or a huge one (e.g. blocking file I/O, which uses no CPU).
            if (cpu.WorstMs > MaxTickWorkMs)
                Fail($"{m.Name}: a server tick did {cpu.WorstMs:0.0} ms of work, JIT compiling left out (over {MaxTickWorkMs} ms, half the 16 ms budget; {cpu.WorstDetail})");
            if (slowTicks > 30 || maxTickMs > 250)
                Fail($"{m.Name}: {slowTicks} server ticks went over the 16 ms budget (worst {maxTickMs:0.0} ms)");
            else if (maxTickMs > 16)
                Line($"  note: {slowTicks} tick(s) over 16 ms, worst {maxTickMs:0.0} ms; the busiest tick did {cpu.WorstText} of work (the machine was busy elsewhere)");
            if (game.Local is { Alive: true } && game.MatchNumber == 1)
                Fail($"{m.Name}: the host was respawned without clicking Respawn");
        }
    }

    private static BotStats _matchBotStats = new();

    /// <summary>Most bytes a bot match tick may allocate on average (see CheckMatches; the busiest map today is about 560).</summary>
    private const double MaxMatchAllocPerTick = 1500;

    // ---- 2a. Bots ---------------------------------------------------------------------------

    /// <summary>
    /// Bot AI: a nav graph on every map, A* between far-apart spawns, bots actually walking to a far goal, the headshot
    /// share (Normal bots from the matches above, plus a few Hard matches), and personalities that play differently.
    /// </summary>
    private static void CheckBots()
    {
        var rng = new Random(7);
        var search = new NavSearch();
        var path = new int[128];
        double totalBuild = 0;
        var slowBuilds = new List<(string Name, double Ms, string Detail)>();
        var bakedSame = 0;
        foreach (var m in Maps.All)
        {
            World.SetMap(m);
            for (var wait = 0; wait < 1000 && NavGraph.Current == NavGraph.Empty; wait++)
                NavGraph.Pump(50); // usually already built (and cached) during the match on this map above
            var g = NavGraph.Current;
            totalBuild += g.BuildMs;
            // Baked equals live (BotNavBake.cs): this map's graph written as the release bakes it reads back bit for bit.
            if (g != NavGraph.Empty && g.PatchVersion == 0 && g.EdgeTo.Length == g.BuiltEdges)
            {
                var key = NavBake.KeyOf(m, m.ContentHash);
                var back = NavBake.Read(NavBake.Write(g, m, key), m.Name, m.ContentHash, key, out var why);
                if (back == null || !back.Serialize().AsSpan().SequenceEqual(g.Serialize()) || NavFingerprint(back) != NavFingerprint(g))
                    Fail($"{m.Name}: its baked nav graph doesn't read back as built ({why})");
                else
                    bakedSame++;
            }
            // A* between the spawns furthest apart (the routes bots need most).
            var pairs = 0;
            var unreachable = new List<string>();
            var expanded = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var spawns = m.Spawns;
            for (var i = 0; i < spawns.Count; i++)
            {
                var far = spawns.OrderByDescending(s => Vector3.Distance(s, spawns[i])).First();
                int a = g.Nearest(spawns[i]), b = g.Nearest(far, needSight: false);
                pairs++;
                if (search.Find(g, a, b, path) == 0)
                    unreachable.Add($"({spawns[i].X:0},{spawns[i].Z:0})->({far.X:0},{far.Z:0})");
                expanded += search.LastExpanded;
            }
            var perSearch = sw.Elapsed.TotalMilliseconds / Math.Max(1, pairs);
            var mainPct = g.Nodes.Length == 0 ? 0 : 100f * g.MainNodes.Length / g.Nodes.Length;
            Line($"nav {m.Name,-14} spacing {g.Spacing:0.0}  nodes {g.Nodes.Length,5}  edges {g.EdgeCount,6}  mantles {g.MantleEdges,4}  main {mainPct,3:0}%  "
                + $"build {g.BuildMs,5:0} ms  A* {perSearch:0.00} ms  unreachable {unreachable.Count}/{pairs}"
                + (unreachable.Count > 0 ? "  " + string.Join(" ", unreachable.Take(3)) : ""));
            if (g.Nodes.Length < 20 || g.EdgeCount < g.Nodes.Length)
                Fail($"{m.Name}: nav graph too small ({g.Nodes.Length} nodes, {g.EdgeCount} edges)");
            if (unreachable.Count > pairs / 4)
                Fail($"{m.Name}: {unreachable.Count} of {pairs} far spawn pairs have no path");
            if (g.BuildMs > MaxNavBuildMs)
                slowBuilds.Add((m.Name, g.BuildMs, $"mover walks: {g.WalkChecked} links, {g.WalkTicks * 1000.0 / Stopwatch.Frequency:0} ms"));
        }
        // The 3 s limit is for a GitHub runner or a gaming PC. A slower machine (the 2-core agent box builds everything about
        // 3x slower than the developer's PC) gets it scaled by how long all the other maps' graphs took here: a map that is slow
        // only because the machine is slow passes, one that got slower than the rest still fails.
        var machine = Math.Max(1.0, (totalBuild - slowBuilds.Sum(x => x.Ms)) / NavBuildReferenceMs);
        Line($"nav graphs for {Maps.All.Length} maps: {totalBuild / 1000:0.0} s of build work in total (spread over ticks in a real game); {bakedSame} baked and read back bit for bit; "
             + $"machine speed factor {machine:0.00} (the other maps vs {NavBuildReferenceMs / 1000:0} s), per-map limit {MaxNavBuildMs * machine:0} ms");
        foreach (var (name, ms, detail) in slowBuilds)
            if (ms > MaxNavBuildMs * machine)
                Fail($"{name}: nav graph took {ms:0} ms to build (limit {MaxNavBuildMs} ms x machine factor {machine:0.00}; {detail})");

        // Walk to a far goal: one bot alone, told where to go.
        foreach (var name in new[] { "ProtoBox", "Courtyard", "Rooftops", "Harbor", "Downtown", "Galleria" })
        {
            if (Maps.Find(name) is not { } m)
                continue;
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: 1, botSkill: BotDifficulty.Normal, mapId: m.Id);
            game.MapVoteEnabled = false;
            var bot = game.Players.Values.First(p => p.Bot != null);
            World.SetMap(m);
            for (var wait = 0; wait < 1000 && NavGraph.Current == NavGraph.Empty; wait++)
                NavGraph.Pump(50); // normally a slice per tick; here, just finish it
            var g = NavGraph.Current;
            if (g.MainNodes.Length < 2)
            {
                Fail($"{name}: no main nav area");
                continue;
            }
            // Start and goal: far apart in the main area, with a path between them.
            Vector3 from = default, to = default;
            var len = 0;
            for (var attempt = 0; attempt < 40 && len == 0; attempt++)
            {
                var a = g.MainNodes[rng.Next(g.MainNodes.Length)];
                var b = g.MainNodes[rng.Next(g.MainNodes.Length)];
                if (Vector3.Distance(g.Nodes[a], g.Nodes[b]) < MathF.Min(m.HalfX, m.HalfZ))
                    continue;
                len = search.Find(g, a, b, path);
                from = g.Nodes[a];
                to = g.Nodes[b];
            }
            if (len == 0)
            {
                Fail($"{name}: couldn't find a far pair of nodes with a path");
                continue;
            }
            var route = 0f;
            for (var i = 1; i < len; i++)
                route += Vector3.Distance(g.Nodes[path[i - 1]], g.Nodes[path[i]]);
            bot.Teleport(from);
            bot.Bot!.ForcedGoal = to;
            var limit = route / 4f + 8f; // sprinting is 13.5 m/s, walking 6: generous for corners, climbs and doors
            const double dt = 1.0 / World.TickRate;
            var now = 0.0;
            var took = -1.0;
            for (var i = 0; i < limit * World.TickRate; i++)
            {
                now += dt;
                game.Update(now, dt);
                if (Vector3.Distance(bot.Position, to) < 2.5f)
                {
                    took = now;
                    break;
                }
            }
            Line($"{name,-14} walk {Vector3.Distance(from, to),5:0} m straight, {route,5:0} m route: "
                + (took >= 0 ? $"arrived in {took:0.0} s" : $"NOT there after {limit:0} s (ended {Vector3.Distance(bot.Position, to):0.0} m away)")
                + $", stuck clock at the end {bot.Bot.StuckSecs:0.0} s, stuck self-kills {game.BotStuckKills}");
            if (took < 0)
                Fail($"{name}: bot didn't reach a goal {route:0} m away within {limit:0} s (from {from} to {to}: ended at {bot.Position}, mode {bot.Bot.Mode}, path {bot.Bot.PathIdx}/{bot.Bot.PathLen}, target {bot.Bot.Target}, deaths {bot.Deaths}; last stuck log: {game.LastStuckLog})");
            if (game.BotStuckKills > 0)
                Fail($"{name}: a bot walking a clear route killed itself as stuck ({game.LastStuckLog})");
        }

        // Hard bots: the headshot share with the most accurate aim.
        var hard = new BotStats();
        foreach (var m in Maps.All.Where((_, i) => i % 3 == 0))
        {
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: Game.ClassicPlayers - 1, botSkill: BotDifficulty.Hard, mapId: m.Id);
            game.MapVoteEnabled = false;
            var now = 0.0;
            var worst = 0.0;
            for (var i = 0; i < 90 * World.TickRate; i++)
            {
                now += 1.0 / World.TickRate;
                var t0 = Stopwatch.GetTimestamp();
                game.Update(now, 1.0 / World.TickRate);
                if (i > 60)
                    worst = Math.Max(worst, Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            }
            hard.Add(game.BotStats);
            Line($"Hard match on {m.Name}: {game.BotStats.GunKills} gun kills, worst tick {worst:0.00} ms");
        }
        var normal = _matchBotStats;
        Line($"headshots: normal {normal.GunHeadshots}/{normal.GunKills} ({normal.HeadshotRatio:P1}), hard {hard.GunHeadshots}/{hard.GunKills} ({hard.HeadshotRatio:P1})"
             + " (before this change: normal 51.4%, hard 51.0%)");
        // Melee kills in one hit: bots should still mostly shoot (they need a short wind-up in reach before swinging).
        foreach (var (stats, label) in new[] { (normal, "Normal"), (hard, "Hard") })
        {
            var share = stats.MeleeKills / (float)Math.Max(1, stats.MeleeKills + stats.GunKills);
            Line($"{label} bots: {stats.MeleeKills} melee kills vs {stats.GunKills} gun kills ({share:P1} melee)");
            if (share > 0.25f)
                Fail($"{label} bots: {share:P1} of their kills are melee, too many (at most 25%)");
        }
        foreach (var (stats, label) in new[] { (normal, "Normal"), (hard, "Hard") })
            if (stats.GunKills < 40)
                Fail($"{label} bots: only {stats.GunKills} gun kills, too few to judge");
            else if (stats.HeadshotRatio is < 0.10f or > 0.30f)
                Fail($"{label} bots: headshot share {stats.HeadshotRatio:P1} is outside 10-30%");

        // Personalities: how far away each one fights.
        var all = new BotStats();
        all.Add(normal);
        all.Add(hard);
        Line("engagement range: " + string.Join("  ", BotPersonalities.All.Select(p => $"{p.Name} {all.AverageRange(p.Kind):0.0} m ({all.Shots[(int)p.Kind]})")));
        if (all.AverageRange(BotPersonality.Rusher) >= all.AverageRange(BotPersonality.Marksman) * 0.8f)
            Fail($"Rushers fight at {all.AverageRange(BotPersonality.Rusher):0.0} m, Marksmen at {all.AverageRange(BotPersonality.Marksman):0.0} m: not different enough");
        if (BotPersonalities.All.Any(p => all.Shots[(int)p.Kind] == 0))
            Fail("a personality never fired a shot");
        World.SetMap(Maps.All[0]);
    }

    // ---- 2b. Map vote ---------------------------------------------------------------------

    private static void CheckMapVote()
    {
        for (var round = 0; round < 3; round++)
        {
            var start = Maps.All[round == 0 ? 0 : round * 7];
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: Game.ClassicPlayers - 1, botSkill: BotDifficulty.Hard, mapId: start.Id);
            const double dt = 1.0 / World.TickRate;
            var now = 0.0;
            byte[] ballot = [];
            byte[] tally = [];
            var hostVoted = false;
            var votedAt = -1.0;
            for (var i = 0; i < 700 * World.TickRate && game.MatchNumber == 1; i++)
            {
                now += dt;
                game.Update(now, dt);
                if (game.VoteOpen)
                {
                    ballot = game.VoteOptions.ToArray();
                    tally = game.VoteTally.ToArray();
                    if (!hostVoted)
                    {
                        game.CastVote(0); // the host votes for the first map
                        hostVoted = true;
                        votedAt = now;
                    }
                }
            }
            if (ballot.Length == 0)
            {
                Fail($"{start.Name}: no vote happened (match {game.MatchNumber}, phase {game.Phase})");
                continue;
            }
            var names = string.Join(", ", ballot.Select(id => $"{Maps.Get(id).Name} ({Maps.Get(id).Size})"));
            Line($"{start.Name}: ballot {names}; final tally {string.Join("/", tally)}; next map {World.Map.Name}");
            if (ballot.Length != Game.VoteChoices)
                Fail($"ballot has {ballot.Length} maps");
            if (ballot.Contains(start.Id))
                Fail("the map just played was on the ballot");
            if (start.Pack is { Maps.Count: >= 3 } pack && ballot.Any(id => Maps.Get(id).Pack != pack))
                Fail($"the ballot offered maps from outside the {pack.Name} pack");
            if (ballot.Select(id => Maps.Get(id).Size).Distinct().Count() < 3)
                Fail("ballot doesn't offer one map of each size");
            if (tally.Sum(t => t) != 1 || tally[0] != 1)
                Fail($"expected only the host's vote (bots don't vote), got tally {string.Join("/", tally)}");
            if (World.Map.Id != ballot[0])
                Fail($"the host was the only voter (for {Maps.Get(ballot[0]).Name}) but the next map is {World.Map.Name}");
            if (votedAt >= 0 && now - votedAt > Game.VoteEarlyEndSeconds + 0.5)
                Fail($"every human had voted, but the next match took {now - votedAt:0.0} s to start");
            if (game.MatchNumber != 2)
                Fail("the next match didn't start");
            // Everyone is on the new map, standing on solid ground.
            for (var i = 0; i < 2 * World.TickRate; i++)
            {
                now += dt;
                game.Update(now, dt);
            }
            var bad = game.Players.Values.Count(p => p.Alive && (World.Map.IsBlocked(p.Position) || p.Position.Y < -0.5f || MathF.Abs(p.Position.X) > World.Map.HalfX));
            if (bad > 0)
                Fail($"{bad} player(s) badly placed after switching to {World.Map.Name}");
        }
    }

    // ---- 2c. Server browser ---------------------------------------------------------------

    /// <summary>Host + master + browser on loopback: info in pongs, heartbeat, list query, ping.</summary>
    private static void CheckServerBrowser()
    {
        var info = new ServerInfo("Self-test server", "Harbor", 3, 8, 2, MatchPhase.Playing, 26123, ServerInfo.ProtocolVersion);
        var junk = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }.Concat(info.Encode()).ToArray();
        if (ServerInfo.Find(junk) != info)
            Fail("ServerInfo didn't survive encode/find");

        // Self-test hosts use fixed ports 26123-26311 (2026-10-28; they were 60123-60311): below every OS's ephemeral
        // range (Windows and macOS 49152-65535, Linux 32768-60999), so a client socket another check opened can't hold
        // one ("SocketPortAlreadyInUse", Windows main run 40485419142). New loopback checks pick a free port there.
        using var host = NetPeer.Host(26123, 4);
        using var master = NetPeer.Host(26124, 1);
        using var browser = NetPeer.Discovery();
        host.SetOfflineInfo(info.Encode());
        host.Advertise("127.0.0.1", 26124, MasterServerApp.Heartbeat(26123));
        var games = new Dictionary<string, (string Ip, ushort Port, DateTime Seen)>();
        List<(string, ushort)>? listed = null;
        ServerInfo? ponged = null;
        var asked = false;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 4 && (listed == null || ponged == null))
        {
            host.Poll((_, _) => { });
            MasterServerApp.Pump(master, games);
            if (!asked && games.Count > 0)
            {
                browser.Advertise("127.0.0.1", 26124, MasterServerApp.ListQuery);
                asked = true;
            }
            browser.PollRaw((from, data) =>
            {
                if ((DefaultMessageIDTypes)data[0] == DefaultMessageIDTypes.AdvertiseSystem && MasterServerApp.ParseList(data) is { } l)
                {
                    listed = l;
                    foreach (var (ip, port) in l)
                        browser.PingHost(ip, port);
                }
                else if ((DefaultMessageIDTypes)data[0] == DefaultMessageIDTypes.UnconnectedPong && ServerInfo.Find(data) is { } i)
                    ponged = i;
            });
            Thread.Sleep(10);
        }
        Line($"master saw {games.Count} game(s): {string.Join(", ", games.Keys)}; list reply {(listed == null ? "none" : string.Join(", ", listed.Select(x => $"{x.Item1}:{x.Item2}")))}; "
            + $"pong {(ponged is { } p ? $"'{p.Name}' map {p.MapName} {p.Players}/{p.MaxPlayers}" : "none")} after {sw.ElapsedMilliseconds} ms");
        if (games.Count != 1)
            Fail("master didn't record the heartbeat");
        if (listed is not { Count: 1 } || listed[0].Item2 != 26123)
            Fail("browser didn't get the game from the master");
        if (ponged != info)
            Fail("ping didn't return the server info");
    }

    // ---- 3. Mantling ---------------------------------------------------------------------

    private static void CheckMantle()
    {
        // Courtyard: house at (-20,-20) 14x12, north wall at z=-26..-25.7 with a window centred at x=-23.5 (sill 1.0, top 2.3).
        World.SetMap(Maps.Find("Courtyard")!);
        var s = new MoveState { Position = new Vector3(-23.5f, 0, -26.6f), Grounded = true };
        var yaw = MathF.PI; // facing +Z
        var mantled = false;
        for (var i = 0; i < 150; i++)
        {
            var b = Buttons.Forward | (i < 40 ? Buttons.Jump : Buttons.None);
            World.Simulate(ref s, new InputCmd { Buttons = b, Yaw = yaw, SpeedPct = 100 });
            mantled |= s.MantleTicks > 0;
        }
        Line($"window: mantled={mantled}, ended at ({s.Position.X:0.00}, {s.Position.Y:0.00}, {s.Position.Z:0.00}) stance {s.Stance}");
        if (!mantled)
            Fail("did not mantle into the Courtyard window");
        else if (s.Position.Z < -25.4f)
            Fail("mantled but didn't get through the window");

        // Same window, off-centre by 0.3 m: the sideways nudge should still line it up.
        s = new MoveState { Position = new Vector3(-23.2f, 0, -26.6f), Grounded = true };
        mantled = false;
        for (var i = 0; i < 150; i++)
        {
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | (i < 40 ? Buttons.Jump : Buttons.None), Yaw = yaw, SpeedPct = 100 });
            mantled |= s.MantleTicks > 0;
        }
        Line($"window off-centre: mantled={mantled}, ended at z {s.Position.Z:0.00}");
        if (!mantled || s.Position.Z < -25.4f)
            Fail("off-centre window mantle failed");

        // Proto Box crate: find any box 0.6-1.7 m tall and climb onto it.
        World.SetMap(Maps.All[0]);
        var crate = World.Map.Solid.FirstOrDefault(b => b.Min.Y < 0.01f && b.Max.Y > 0.6f && b.Max.Y < 1.7f);
        if (crate.Half == Vector3.Zero)
        {
            Line("no crate-sized box on Proto Box to test");
            return;
        }
        var start = new Vector3(crate.Center.X, 0, crate.Min.Z - World.PlayerRadius - 0.05f);
        s = new MoveState { Position = start, Grounded = true };
        mantled = false;
        var trace = new List<string>();
        for (var i = 0; i < 90; i++)
        {
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | (i < 20 ? Buttons.Jump : Buttons.None), Yaw = MathF.PI, SpeedPct = 100 });
            mantled |= s.MantleTicks > 0;
            if (i < 6 || i % 15 == 0)
                trace.Add($"t{i}:({s.Position.Y:0.00},{s.Position.Z:0.00},m{s.MantleTicks},g{(s.Grounded ? 1 : 0)})");
        }
        Line($"crate {crate.Center} half {crate.Half}: mantled={mantled}, ended at ({s.Position.X:0.00}, {s.Position.Y:0.00}, {s.Position.Z:0.00})");
        Line("  " + string.Join(" ", trace));
        if (!mantled)
            Fail("could not mantle onto a crate");
    }

    // ---- 3b. Loadouts and perks --------------------------------------------------------------

    private static void CheckLoadoutsAndPerks()
    {
        // Loadout slots: 1 at level 1, the last at max level, evenly spread, always increasing.
        var levels = Enumerable.Range(0, Progress.MaxLoadoutSlots).Select(Progress.LoadoutSlotLevel).ToArray();
        Line($"loadout slots unlock at levels {string.Join(", ", levels)} (max level {Progress.MaxLevel})");
        if (levels[0] != 1 || levels[^1] != Progress.MaxLevel || levels.Zip(levels.Skip(1)).Any(p => p.Second <= p.First))
            Fail("loadout slot levels must go 1 ... max level, strictly increasing");
        var gaps = levels.Zip(levels.Skip(1)).Select(p => p.Second - p.First).ToArray();
        if (gaps.Max() - gaps.Min() > 1)
            Fail($"loadout slots aren't evenly spaced: gaps {string.Join(",", gaps)}");
        var fresh = new Progress { ReadOnly = true };
        if (fresh.LoadoutSlots != 1)
            Fail($"a level-1 player has {fresh.LoadoutSlots} loadout slots (should be 1)");
        var maxed = new Progress { ReadOnly = true, Xp = Progress.TotalFor(Progress.MaxLevel) };
        if (maxed.LoadoutSlots != Progress.MaxLoadoutSlots)
            Fail($"a max-level player has {maxed.LoadoutSlots} loadout slots");

        // Perks: four per category, the starters at level 1, everything unlocks by max level.
        foreach (var c in new[] { PerkCategory.Movement, PerkCategory.Survival, PerkCategory.Damage })
        {
            var inCat = Perks.In(c).ToArray();
            Line($"{Perks.CategoryName(c)}: {string.Join(", ", inCat.Select(p => $"{p.Name} (lvl {Progress.PerkLevel(p.Id)})"))}");
            if (inCat.Length < 3)
                Fail($"{c} has only {inCat.Length} perks");
            if (!inCat.Any(p => Progress.PerkLevel(p.Id) == 1))
                Fail($"{c} has no starter perk");
        }
        if (Perks.All.Any(p => Progress.PerkLevel(p.Id) > Progress.MaxLevel))
            Fail("a perk unlocks past max level");

        // A level-1 player's loadout with locked things in it falls back to defaults.
        var l = new Loadout { Primary = Weapons.Find("Railspike")!.Id, Grenade = ThrowableKind.Smoke, Perks = new PerkSet(PerkId.Parkour, PerkId.LastStand, PerkId.PointBlank) };
        var eff = LoadoutStore.Effective(l, fresh);
        if (eff.Primary != Weapons.DefaultPrimary.Id || eff.Grenade != ThrowableKind.None || eff.Perks.Movement != PerkId.None
            || eff.Perks.Survival != PerkId.None || eff.Perks.Damage != PerkId.PointBlank)
            Fail($"locked items weren't swapped out: {Weapons.Get(eff.Primary).Name}, {eff.Grenade}, {eff.Perks}");

        // ---- Movement perks in the shared physics ----
        World.SetMap(Maps.All[0]);
        float Run(PerkId perk, Buttons stance, int ticks, byte boost = 0)
        {
            var st = new MoveState { Position = new Vector3(-14, 0, 14), Grounded = true, MovePerk = perk, BoostTicks = boost };
            for (var i = 0; i < ticks; i++)
                World.Simulate(ref st, new InputCmd { Buttons = Buttons.Right | stance, Yaw = 0, SpeedPct = 100 });
            return st.Position.X + 14;
        }
        var crouch = Run(PerkId.None, Buttons.Crouch, 60);
        var crouchLp = Run(PerkId.LowProfile, Buttons.Crouch, 60);
        var walk = Run(PerkId.None, Buttons.None, 40);
        var walkBoost = Run(PerkId.HeadStart, Buttons.None, 40, Perks.BoostTicks);
        Line($"1 s crouched: {crouch:0.00} m, with Low Profile {crouchLp:0.00} m;  0.67 s walking: {walk:0.00} m, boosted {walkBoost:0.00} m");
        if (crouchLp < crouch * 1.6f)
            Fail("Low Profile doesn't speed up crouching enough");
        if (walkBoost < walk * 1.25f)
            Fail("Head Start / Afterburner boost isn't faster");
        var sprintState = new MoveState { Position = new Vector3(-14, 0, 14), Grounded = true };
        var sprintTop = 0f;
        for (var i = 0; i < 40; i++)
        {
            World.Simulate(ref sprintState, new InputCmd { Buttons = Buttons.Right | Buttons.Sprint, Yaw = 0, SpeedPct = 100 });
            sprintTop = MathF.Max(sprintTop, new Vector2(sprintState.Velocity.X, sprintState.Velocity.Z).Length());
        }
        Line($"sprint top speed {sprintTop:0.00} m/s (walk {World.WalkSpeed} m/s)");
        if (MathF.Abs(sprintTop - World.SprintSpeed) > 0.1f || MathF.Abs(World.SprintSpeed - 13.5f) > 0.001f)
            Fail("sprinting should reach 13.5 m/s");
        var boosted = new MoveState { Position = new Vector3(-14, 0, 14), Grounded = true, BoostTicks = 10 };
        for (var i = 0; i < 20; i++)
            World.Simulate(ref boosted, new InputCmd { Yaw = 0, SpeedPct = 100 });
        if (boosted.BoostTicks != 0)
            Fail("speed boost doesn't wear off");

        // Parkour: a 3 m wall is out of reach normally (even jumping first) but climbable with the perk.
        var wallMap = MapFile.Load("""
            { "name": "Parkour test", "halfX": 20, "halfZ": 20, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] },
              { "op": "Block", "x0": -3, "y0": 0, "z0": 2, "x1": 3, "y1": 3.0, "z1": 4, "color": "#808080" },
              { "op": "Spawn", "x": 10, "z": 10 } ] }
            """, "parkour.json");
        World.SetMap(wallMap);
        bool Climbs(PerkId perk)
        {
            var st = new MoveState { Position = new Vector3(0, 0, 1.5f), Grounded = true, MovePerk = perk };
            var climbed = false;
            for (var i = 0; i < 90; i++)
            {
                World.Simulate(ref st, new InputCmd { Buttons = Buttons.Forward | (i < 30 ? Buttons.Jump : Buttons.None), Yaw = MathF.PI, SpeedPct = 100 });
                climbed |= st.Position.Y > 2.9f; // (it walks off the far side afterwards)
            }
            return climbed;
        }
        var normal = Climbs(PerkId.None);
        var parkour = Climbs(PerkId.Parkour);
        Line($"3 m wall: climbed without perk {normal}, with Parkour {parkour}");
        if (normal || !parkour)
            Fail("Parkour climb reach is wrong");
        World.SetMap(Maps.All[0]);

        // ---- Damage perks (pure maths) ----
        var all = new PerkSet(PerkId.None, PerkId.None, PerkId.PointBlank);
        if (MathF.Abs(Perks.AttackMult(all, 5, -Vector3.UnitZ, 0, 100, false) - Perks.PointBlankMult) > 0.001f || Perks.AttackMult(all, 12, -Vector3.UnitZ, 0, 100, false) != 1f)
            Fail("Point Blank range check is wrong");
        var flank = new PerkSet(PerkId.None, PerkId.None, PerkId.Flanker);
        // Victim faces -Z (yaw 0). A shot travelling -Z hits their back; travelling +Z hits their front.
        if (Perks.AttackMult(flank, 10, -Vector3.UnitZ, 0, 100, false) < 1.5f || Perks.AttackMult(flank, 10, Vector3.UnitZ, 0, 100, false) != 1f)
            Fail("Flanker back/front check is wrong");
        var opener = new PerkSet(PerkId.None, PerkId.None, PerkId.Opener);
        if (Perks.AttackMult(opener, 10, Vector3.UnitX, 0, World.MaxHealth, false) < 1.4f || Perks.AttackMult(opener, 10, Vector3.UnitX, 0, World.MaxHealth - 1, false) != 1f)
            Fail("Opener full-health check is wrong");

        // ---- Survival perks on the server ----
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var a = game.Players[0];
        var v = game.Players.Values.First(p => p.Id != 0);
        void Reset(PerkSet attacker, PerkSet victim)
        {
            game.SelfTestPerks(a, attacker);
            game.SelfTestPerks(v, victim);
            game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
            game.SelfTestPlace(v, new Vector3(5, 0, 8), 0);
        }
        Reset(PerkSet.Empty, new PerkSet(PerkId.None, PerkId.FlakJacket, PerkId.None));
        game.SelfTestDamage(a, v, 100, DamageKind.Explosive, 1);
        var flakHp = v.Health;
        Reset(PerkSet.Empty, new PerkSet(PerkId.None, PerkId.LastStand, PerkId.None));
        game.SelfTestDamage(a, v, 150, DamageKind.Bullet, 1);
        var standHp = v.Health;
        game.SelfTestDamage(a, v, 50, DamageKind.Bullet, 1.5);
        var immuneHp = v.Health;
        Line($"Flak Jacket: 100 blast -> {World.MaxHealth - flakHp} damage;  Last Stand: lethal hit -> {standHp} HP, next bullet hit (shielded) -> {immuneHp} HP (the full rules: section Health)");
        if (flakHp != World.MaxHealth - (int)MathF.Round(100 * Perks.FlakMult))
            Fail("Flak Jacket didn't reduce blast damage");
        if (standHp != 1 || immuneHp != 1)
            Fail("Last Stand didn't save the lethal hit and shield the next one");

        Reset(PerkSet.Empty, new PerkSet(PerkId.None, PerkId.Entrenched, PerkId.None));
        v.Sim.Stance = Stance.Crouch;
        game.SelfTestDamage(a, v, 50, DamageKind.Bullet, 1);
        var dugInHp = v.Health;
        if (dugInHp != World.MaxHealth - (int)MathF.Round(50 * Perks.EntrenchedMult))
            Fail($"Entrenched: crouched and still took {World.MaxHealth - dugInHp} of 50");

        Reset(new PerkSet(PerkId.Afterburner, PerkId.Bloodthirst, PerkId.None), PerkSet.Empty);
        a.Health = 30;
        game.SelfTestDamage(a, v, 200, DamageKind.Bullet, 1);
        Line($"Entrenched: 50 bullet damage -> {World.MaxHealth - dugInHp};  Bloodthirst kill: 30 HP -> {a.Health} HP;  Afterburner boost {a.Sim.BoostTicks} ticks");
        if (a.Health != World.MaxHealth)
            Fail("Bloodthirst didn't heal on a kill");
        if (a.Sim.BoostTicks != Perks.BoostTicks)
            Fail("Afterburner didn't boost on a kill");
        // No stacking: a kill during the boost doesn't refresh it; after it ends there's a cooldown.
        {
            a.Sim.BoostTicks = 100; // part-way through the boost
            Reset2();
            var during = a.Sim.BoostTicks;
            a.Sim.BoostTicks = 0; // boost over, still cooling down
            game.ServerTick = a.AfterburnerReadyTick - 1;
            Reset2();
            var cooling = a.Sim.BoostTicks;
            a.Sim.BoostTicks = 0;
            game.ServerTick = a.AfterburnerReadyTick;
            Reset2();
            var ready = a.Sim.BoostTicks;
            Line($"Afterburner: kill mid-boost leaves {during} ticks (no refresh); kill in cooldown {cooling}; kill after cooldown {ready}");
            if (during != 100 || cooling != 0 || ready != Perks.BoostTicks)
                Fail($"Afterburner stacking: mid-boost {during} (want 100), cooldown {cooling} (want 0), ready {ready} (want {Perks.BoostTicks})");
            void Reset2()
            {
                v.Health = World.MaxHealth;
                v.Alive = true;
                game.SelfTestDamage(a, v, 200, DamageKind.Bullet, 1);
            }
        }
        game.SelfTestPerks(a, new PerkSet(PerkId.HeadStart, PerkId.None, PerkId.None));
        if (a.Sim.BoostTicks != Perks.BoostTicks || a.Sim.MovePerk != PerkId.HeadStart)
            Fail("Head Start doesn't boost on spawn");
    }

    // ---- 3c. Melee ------------------------------------------------------------------------------

    private static void CheckMelee()
    {
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var a = game.Players[0];
        var v = game.Players.Values.First(p => p.Id != 0);
        game.SelfTestPerks(a, PerkSet.Empty);
        game.SelfTestPerks(v, PerkSet.Empty);

        // Attacker at z=5 facing +Z (yaw pi); victim 1.3 m ahead.
        game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
        game.SelfTestPlace(v, new Vector3(5, 0, 6.3f), 0); // facing the attacker
        game.SelfTestMelee(a, MathF.PI, 0, 1);
        var front = World.MaxHealth - (v.Alive ? v.Health : 0);
        var frontKill = !v.Alive;
        game.SelfTestPlace(v, new Vector3(5, 0, 6.3f), 0); // back on full health
        game.SelfTestMelee(a, MathF.PI, 0, 1.2); // still cooling down
        var spam = World.MaxHealth - v.Health;

        game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
        game.SelfTestPlace(v, new Vector3(5, 0, 6.3f), MathF.PI); // back turned
        game.SelfTestMelee(a, MathF.PI, 0, 3);
        var backstabKill = !v.Alive;

        game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
        game.SelfTestPlace(v, new Vector3(5, 0, 9), 0); // 4 m away: out of reach
        game.SelfTestMelee(a, MathF.PI, 0, 5);
        var far = World.MaxHealth - v.Health;

        game.SelfTestPlace(v, new Vector3(5, 0, 6.3f), 0);
        game.SelfTestMelee(a, 0, 0, 7); // facing the wrong way
        var behind = World.MaxHealth - v.Health;

        Line($"front hit {front} dmg, kills {frontKill} (second swing during cooldown does {spam}), backstab kills {backstabKill}, 4 m away {far} dmg, facing away {behind} dmg, kill name '{Throwables.KillName(Game.MeleeKillId)}'");
        if (!frontKill)
            Fail($"a front melee hit did {front} and didn't kill (melee should kill in one hit)");
        if (spam != 0)
            Fail("melee cooldown not enforced");
        if (!backstabKill)
            Fail("melee from behind didn't kill");
        if (far != 0 || behind != 0)
            Fail("melee hit something out of reach or behind the attacker");
    }

    // ---- 4. Attachments ------------------------------------------------------------------

    private static void CheckAttachments()
    {
        var combos = 0;
        foreach (var w in Weapons.All)
        {
            var list = Attachments.For(w);
            foreach (var a in list)
            {
                var kit = Attachments.With(0, a.Slot, a.Index);
                var d = Attachments.Apply(w, kit);
                combos++;
                if (d == w || d.Id != w.Id || d.Kit != kit)
                    Fail($"{w.Name} + {a.Name}: Apply didn't produce a kitted copy");
                if (d.Mag < 1 || d.Rpm <= 0 || d.Reload <= 0 || d.RangeM <= 0 || d.HipSpreadDeg <= 0 || d.RecoilDeg < 0 || d.AdsZoom < 1 || d.AdsTime <= 0)
                    Fail($"{w.Name} + {a.Name}: bad stats (mag {d.Mag}, range {d.RangeM}, ads {d.AdsTime})");
                if (Attachments.Sanitize(w, kit) != kit)
                    Fail($"{w.Name} + {a.Name}: Sanitize dropped a fitting attachment");
                if (Attachments.UnlockXp(w, a) is var x && (x < (a == Attachments.DefaultOptic(w) ? 0 : 50) || x > Attachments.MaxXp(w))) // a sniper's starting scope: 0
                    Fail($"{w.Name} + {a.Name}: unlock at {x} weapon XP (max {Attachments.MaxXp(w)})");
            }
            // A full kit: first fitting attachment in every slot
            uint full = 0;
            foreach (var a in list)
                if (Attachments.Slot(full, a.Slot) == 0)
                    full = Attachments.With(full, a.Slot, a.Index);
            var fd = Attachments.Apply(w, full);
            if (fd.Mobility is < 0.6f or > 1.25f)
                Fail($"{w.Name}: full kit mobility {fd.Mobility}");
            if (!ReferenceEquals(fd, Attachments.Apply(w, full)))
                Fail($"{w.Name}: kit cache returned a different object");
            // A kit with junk in every slot must come back clean
            if (Attachments.Sanitize(w, Attachments.FullKit) is var junk && Attachments.InKit(junk).Any(a => !a.Fits(w)))
                Fail($"{w.Name}: Sanitize let a non-fitting attachment through");
        }
        var counts = Weapons.All.Select(w => Attachments.For(w).Count).ToArray();
        Line($"{combos} weapon+attachment combinations checked; attachments per weapon {counts.Min()}-{counts.Max()}");

        // Pacing (rough targets): ~1,000 weapon XP and ~1,000 account XP per typical match (~7 kills).
        const float perMatch = 1000f;
        var matchesToMax = Weapons.All.Where(w => Attachments.For(w).Count > 0 && !w.IsLauncher).Select(w => Attachments.MaxXp(w) / perMatch).ToArray(); // (launchers: their own few attachments, quick to unlock)
        Line($"matches to max a gun: {matchesToMax.Min():0.0} - {matchesToMax.Max():0.0} (target 3-7), average {matchesToMax.Average():0.0}");
        if (matchesToMax.Min() < 2.5f || matchesToMax.Max() > 7.5f)
            Fail("attachment pacing outside 3-7 matches");
        var sixty = Weapons.All.Where(w => Attachments.For(w).Count > 0 && !w.IsLauncher).Select(w =>
        {
            var list = Attachments.For(w);
            var k = Math.Max(0, (int)MathF.Ceiling(list.Count * 0.6f) - 1);
            return Attachments.UnlockXp(w, list[k]);
        }).ToArray();
        var levelCosts = Enumerable.Range(1, Progress.MaxLevel - 1).Select(Progress.XpForNext).ToArray();
        Line($"XP for 60% of a gun's attachments: {sixty.Min():N0}-{sixty.Max():N0}, avg {sixty.Average():N0};  XP per level (= per new unlock): {levelCosts.Min():N0}-{levelCosts.Max():N0}, avg {levelCosts.Average():N0}");
        Line($"levels {Progress.MaxLevel}, total XP to unlock everything {Progress.TotalFor(Progress.MaxLevel):N0} (~{Progress.TotalFor(Progress.MaxLevel) / perMatch:0} matches)");
        var ratio = levelCosts.Average() / sixty.Average();
        if (ratio < 0.7 || ratio > 1.4)
            Fail($"level cost vs 60%-of-a-gun ratio {ratio:0.00} is off target");
    }

    // ---- 4b. Optics ------------------------------------------------------------------------

    private static void CheckOptics()
    {
        var optics = Attachments.BySlot[(int)AttachSlot.Optic];
        Line($"{optics.Length} optics: {string.Join(", ", optics.Select(o => $"{o.Index}={o.Name}"))}");
        if (optics.Length < 10)
            Fail($"only {optics.Length} optics loaded (10 ship with the game)");
        if (optics.Select(o => o.Index).Distinct().Count() != optics.Length || optics.Any(o => o.Index is < 1 or > Attachments.MaxPerSlot))
            Fail($"optic indices are not unique values in 1-{Attachments.MaxPerSlot}");
        if (optics.Select(o => o.Name.ToLowerInvariant()).Distinct().Count() != optics.Length)
            Fail("two optics share a name");
        // The shipped optics keep their network indices (kits send indices; saves use names).
        string[] shipped = ["Red Dot", "Holographic", "2.5x ACOG", "4x Scope", "8x Sniper Scope", "Pinhole Reflex", "Brick 2x Prism", "Tandem 3x Hybrid", "Variant 6x LPVO", "Longshot 12x"];
        for (var i = 0; i < shipped.Length; i++)
            if (Attachments.Find(shipped[i]) is not { Slot: AttachSlot.Optic } o || o.Index != i + 1)
                Fail($"optic '{shipped[i]}' should be optic #{i + 1} (found {(Attachments.Find(shipped[i]) is { } f ? "#" + f.Index : "nothing")})");

        // What each optic does on every weapon, fitting or not (so the sight maths never breaks for a modded file).
        var kinds = new HashSet<OpticKind>();
        var combos = 0;
        foreach (var w in Weapons.All)
        {
            var sh0 = GunModel.SightHeight(w);
            var z0 = GunModel.AdsZ(w);
            if (!float.IsFinite(sh0) || !float.IsFinite(z0))
                Fail($"{w.Name} irons: sight height {sh0}, ADS z {z0}");
            foreach (var o in optics)
            {
                var d = w.Clone();
                o.Apply(d);
                kinds.Add(d.Optic);
                combos++;
                var sh = GunModel.SightHeight(d);
                var z = GunModel.AdsZ(d);
                if (!float.IsFinite(sh) || !float.IsFinite(z) || sh <= 0 || sh > 0.2f || z is > -0.02f or < -0.7f)
                    Fail($"{w.Name} + {o.Name}: sight height {sh}, ADS z {z}");
                if (GunModel.FullScope(d) != d.AdsZoom >= 4f)
                    Fail($"{w.Name} + {o.Name}: full scope view at {d.AdsZoom}x");
                if (d.Optic != OpticKind.Iron && (Sights.Spec(d).Reticle == GunModel.Reticle.None || Sights.Lens(d) == null))
                    Fail($"{o.Name}: no reticle or no lens");
                // The cut-away is drawing only: it must never move the sight line.
                GunModel.AdsCutaway = 1f;
                var moved = GunModel.SightHeight(d) != sh || GunModel.AdsZ(d) != z;
                GunModel.AdsCutaway = 0f;
                if (moved)
                    Fail($"{w.Name} + {o.Name}: the cut-away moves the sight line");
            }
        }
        foreach (var k in Enum.GetValues<OpticKind>())
            if (k != OpticKind.Iron && !kinds.Contains(k))
                Fail($"no optic file uses optic kind {k}");
        Line($"{combos} weapon x optic sight lines checked; kinds used: {string.Join(", ", kinds.Order())}");

        // Scope view only from 4x, and the lens stays round on any screen shape.
        foreach (var (zoom, scoped) in new[] { (1.5f, false), (2.5f, false), (3f, false), (3.99f, false), (4f, true), (6f, true), (12f, true) })
        {
            var t = Weapons.DefaultPrimary.Clone();
            t.AdsZoom = zoom;
            if (GunModel.FullScope(t) != scoped)
                Fail($"full scope view at {zoom}x should be {scoped}");
        }
        if (Sights.HasLens(Weapons.DefaultPrimary) || Sights.Lens(Weapons.DefaultPrimary) != null)
            Fail("plain iron sights have a lens");

        // Fits: every class gets a sensible optic, nothing big on small guns.
        foreach (var cls in Enum.GetValues<WeaponClass>())
        {
            var guns = Weapons.All.Where(w => w.Class == cls).ToArray();
            foreach (var w in guns)
            {
                var fit = Attachments.For(w).Where(a => a.Slot == AttachSlot.Optic).Select(a => Attachments.Apply(w, Attachments.With(0, AttachSlot.Optic, a.Index))).ToArray();
                if (fit.Length == 0)
                    Fail($"{w.Name}: no optic fits");
                if (cls == WeaponClass.Pistol && fit.Any(d => d.AdsZoom > 2f))
                    Fail($"{w.Name}: a pistol takes a {fit.Max(d => d.AdsZoom)}x optic");
                if (cls is WeaponClass.Pistol or WeaponClass.SMG or WeaponClass.Shotgun && fit.Any(d => GunModel.FullScope(d)))
                    Fail($"{w.Name}: a {cls} takes a full scope");
                if (cls is not (WeaponClass.Sniper or WeaponClass.DMR) && fit.Any(d => d.AdsZoom > 8f))
                    Fail($"{w.Name}: a {cls} takes a {fit.Max(d => d.AdsZoom)}x scope");
            }
            if (guns.Length > 0)
                Line($"{cls}: optics {string.Join(", ", Attachments.For(guns[0]).Where(a => a.Slot == AttachSlot.Optic).Select(a => a.Name))}");
        }

        // Model checks, from the gun's box list: nothing blocks the sight line between the eye and the lens (behind the lens
        // plane the lens pass shows the view instead; reflex sights also cut their dot away), the cut-away only touches the
        // optic, and no gun part clashes with a fitted optic (alone, or with one other attachment).
        var built = 0;
        foreach (var w in Weapons.All)
        {
            var list = Attachments.For(w);
            foreach (var o in list.Where(a => a.Slot == AttachSlot.Optic))
            {
                var kit = Attachments.With(0, AttachSlot.Optic, o.Index);
                var d = Attachments.Apply(w, kit);
                var sight = GunModel.SightHeight(d);
                var eyeZ = -GunModel.AdsZ(d); // the eye in model space at full ADS (pose = translation (0, -sight, AdsZ))
                var lensZ = Sights.Lens(d)?.Center.Z ?? float.MinValue; // the lens plane: behind it the lens pass shows the view
                var aimed = GunModel.RecordParts(d, Sights.Cutaway(d, 1f));
                foreach (var (dx, dy) in new[] { (0f, 0f), (0.004f, 0f), (-0.004f, 0f), (0f, 0.004f), (0f, -0.004f) })
                {
                    var hit = aimed.FirstOrDefault(p => !p.Clear && p.Min.X < dx && p.Max.X > dx && p.Min.Y < sight + dy && p.Max.Y > sight + dy && p.Min.Z < eyeZ && p.Max.Z > lensZ + 1e-4f);
                    if (hit != default)
                    {
                        Fail($"{w.Name} + {o.Name}: at full ADS a {(hit.Optic ? "optic" : "gun")} part blocks the sight line ({dx}, {dy}) at z {hit.Min.Z:0.000}..{hit.Max.Z:0.000}");
                        break;
                    }
                }
                var closed = GunModel.RecordParts(d, 0f);
                var open = GunModel.RecordParts(d, 1f);
                if (!closed.Where(p => !p.Optic).SequenceEqual(open.Where(p => !p.Optic)))
                    Fail($"{w.Name} + {o.Name}: the cut-away changes non-optic parts");

                var others = list.Where(a => a.Slot != AttachSlot.Optic).Select(a => Attachments.With(kit, a.Slot, a.Index)).Prepend(kit);
                foreach (var k in others)
                {
                    built++;
                    var parts = k == kit ? closed : GunModel.RecordParts(Attachments.Apply(w, k), 0f);
                    if (Clash(parts) is { } why)
                    {
                        Fail($"{w.Name} [{Attachments.Describe(k)}]: {why}");
                        break;
                    }
                }
            }
            // The LMG carry handle is there with iron sights and comes off with an optic.
            if (w.Class == WeaponClass.LMG)
            {
                var ironTop = GunModel.RecordParts(w, 0f).Max(p => p.Max.Y);
                var dotTop = GunModel.RecordParts(Attachments.Apply(w, Attachments.With(0, AttachSlot.Optic, 1)), 0f).Where(p => !p.Optic).Max(p => p.Max.Y);
                if (!(ironTop > dotTop + 0.01f))
                    Fail($"{w.Name}: carry handle top {ironTop:0.000} m with irons vs {dotTop:0.000} m with a red dot (it should come off)");
            }
        }
        Line($"{built} kitted models checked for optic clashes and a clear sight line");
        if (GunModel.AdsCutaway != 0)
            Fail("RecordParts left the cut-away knob set");
    }

    /// <summary>Why a gun part clashes with the fitted optic (null if nothing does).</summary>
    private static string? Clash(List<GunModel.Part> parts)
    {
        var optic = parts.Where(p => p.Optic).ToArray();
        if (optic.Length == 0)
            return null;
        var mountBase = optic.Min(p => p.Min.Y);
        var feet = optic.Where(p => p.Min.Y <= mountBase + 0.004f).ToArray(); // rail, base plates, rings
        const float e = 0.0005f;
        foreach (var g in parts.Where(p => !p.Optic && !p.Clear))
        {
            foreach (var f in feet)
                if (g.Max.X > f.Min.X + e && g.Min.X < f.Max.X - e && g.Max.Z > f.Min.Z + e && g.Min.Z < f.Max.Z - e && g.Max.Y > mountBase + 0.0015f)
                    return $"a gun part rises to {g.Max.Y:0.000} m over the optic mount (base at {mountBase:0.000} m), z {g.Min.Z:0.000}..{g.Max.Z:0.000}";
            foreach (var o in optic)
                if (o.Min.Y > mountBase + 0.004f && g.Max.X > o.Min.X + 0.001f && g.Min.X < o.Max.X - 0.001f && g.Max.Y > o.Min.Y + 0.001f && g.Min.Y < o.Max.Y - 0.001f
                    && g.Max.Z > o.Min.Z + 0.001f && g.Min.Z < o.Max.Z - 0.001f)
                    return $"a gun part (y {g.Min.Y:0.000}..{g.Max.Y:0.000}, z {g.Min.Z:0.000}..{g.Max.Z:0.000}) runs into the optic (y {o.Min.Y:0.000}..{o.Max.Y:0.000})";
        }
        return null;
    }

    // ---- 4a. Weapon and attachment files -------------------------------------------------------

    /// <summary>Every public field that differs between two weapons, as "Field: a vs b".</summary>
    private static List<string> WeaponDiffs(WeaponDef a, WeaponDef b, bool includeId)
    {
        var diffs = new List<string>();
        foreach (var f in typeof(WeaponDef).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!includeId && f.Name == nameof(WeaponDef.Id))
                continue;
            var x = f.GetValue(a);
            var y = f.GetValue(b);
            if (!Equals(x, y))
                diffs.Add($"{f.Name}: {x} vs {y}");
        }
        return diffs;
    }

    /// <summary>The first ```json block of a guide, or null.</summary>
    private static string? GuideExample(string path)
    {
        if (!File.Exists(path))
            return null;
        var text = File.ReadAllText(path);
        var start = text.IndexOf("```json", StringComparison.Ordinal);
        var end = start < 0 ? -1 : text.IndexOf("```", start + 7, StringComparison.Ordinal);
        return start < 0 || end < 0 ? "" : text[(start + 7)..end];
    }

    private static void CheckWeaponFiles()
    {
        // ---- The shipped files all load ----
        foreach (var e in Weapons.LoadErrors.Concat(Attachments.LoadErrors))
            Fail("weapon/attachment file problem: " + e);
        var weaponFiles = Weapons.All.Count(w => w.SourceFile != null);
        var attachments = Attachments.BySlot.SelectMany(x => x).ToArray();
        Line($"{weaponFiles} of {Weapons.All.Length} weapons loaded from {Weapons.Folder}, {attachments.Length} attachments from {Attachments.Folder}");
        Line($"weapon set fingerprint {WeaponSet.Fingerprint:x8} (weapons {WeaponSet.WeaponsHash:x8}, attachments {WeaponSet.AttachmentsHash:x8})");
        if (weaponFiles < Weapons.LegacyNames.Length)
            Fail($"only {weaponFiles} weapons loaded from files ({Weapons.LegacyNames.Length} ship with the game)");
        if (attachments.Length < 21)
            Fail($"only {attachments.Length} attachments loaded from files (21 ship with the game)");
        if (attachments.Any(a => a.Index is < 1 or > Attachments.MaxPerSlot))
            Fail($"an attachment index is outside 1-{Attachments.MaxPerSlot} (kits pack {Attachments.SlotBits} bits per slot)");

        // ---- The original 50 weapons keep their ids (old saves and anything else that stored an id) ----
        for (var i = 0; i < Weapons.LegacyNames.Length; i++)
        {
            if (Weapons.Find(Weapons.LegacyNames[i]) is not { } w)
                Fail($"'{Weapons.LegacyNames[i]}' (legacy id {i}) isn't loaded");
            else if (w.Id != i)
                Fail($"'{w.Name}' has id {w.Id}, but it was {i} before weapons were files");
        }
        if (Weapons.DefaultPrimary.Name != "BR-1 Prototype" || Weapons.DefaultSecondary.Name != "P9 Sidearm")
            Fail($"default weapons are {Weapons.DefaultPrimary.Name} / {Weapons.DefaultSecondary.Name}");

        // ---- Round trip: written out and read back, nothing changes ----
        var roundTrips = 0;
        foreach (var w in Weapons.All)
        {
            try
            {
                var copy = WeaponFile.Load(WeaponFile.ToJson(w), w.Name + " (round trip)");
                var diffs = WeaponDiffs(w, copy, includeId: false);
                if (copy.Order != w.Order || copy.UnlockLevel != w.UnlockLevel || copy.Author != w.Author || copy.DefaultOptic != w.DefaultOptic)
                    diffs.Add("order/unlockLevel/author/defaultOptic");
                foreach (var d in diffs)
                    Fail($"{w.Name}: weapon file round trip changed {d}");
                roundTrips++;
            }
            catch (WeaponFileException ex)
            {
                Fail(ex.Message);
            }
        }
        foreach (var a in attachments.Where(a => a.Rule != null))
        {
            try
            {
                var copy = AttachmentFile.Load(AttachmentFile.ToJson(a), a.Name + " (round trip)");
                if (AttachmentFile.ToJson(copy) != AttachmentFile.ToJson(a))
                    Fail($"{a.Name}: attachment file round trip changed it");
                foreach (var w in Weapons.All)
                {
                    if (copy.Fits(w) != a.Fits(w))
                        Fail($"{a.Name}: round trip changed whether it fits the {w.Name}");
                    var x = w.Clone();
                    var y = w.Clone();
                    a.Apply(x);
                    copy.Apply(y);
                    foreach (var d in WeaponDiffs(x, y, includeId: true))
                        Fail($"{a.Name} on {w.Name}: round trip changed {d}");
                }
                roundTrips++;
            }
            catch (WeaponFileException ex)
            {
                Fail(ex.Message);
            }
        }
        Line($"{roundTrips} weapon and attachment files written and re-read identically");
        if (!WeaponFile.ToJson(Weapons.All[0]).Contains("\"recoil\": {", StringComparison.Ordinal))
            Fail("a written weapon file has no \"recoil\" block");

        // ---- The built-in fallback weapons (used when the folder is missing) ----
        var fallback = Weapons.Fallback();
        if (fallback.Count(w => w.Slot == Slot.Primary) != 1 || fallback.Count(w => w.Slot == Slot.Secondary) != 1)
            Fail("there must be exactly one built-in fallback weapon per slot");
        foreach (var fb in fallback)
        {
            try
            {
                WeaponFile.Validate(fb, fb.Name + " (fallback)");
            }
            catch (WeaponFileException ex)
            {
                Fail(ex.Message);
            }
            if (Weapons.Find(fb.Name) is { } file && WeaponDiffs(fb, file, includeId: false) is { Count: > 0 } d)
                Line($"note: the built-in fallback {fb.Name} differs from its file ({string.Join("; ", d)})");
        }

        // ---- Unlocks: the hand-tuned table still applies by name ----
        if (Weapons.Find("Courier") is { } courier && Progress.UnlockLevel(courier) != 2)
            Fail($"Courier unlocks at level {Progress.UnlockLevel(courier)} (should be 2)");
        if (Progress.UnlockLevel(Weapons.DefaultPrimary) != 1 || Progress.UnlockLevel(Weapons.DefaultSecondary) != 1)
            Fail("the default weapons aren't unlocked at level 1");
        if (Weapons.Find("Toy Blaster") is { } toy && Progress.UnlockLevel(toy) != Progress.MaxLevel)
            Fail($"Toy Blaster unlocks at level {Progress.UnlockLevel(toy)} (should be the max, {Progress.MaxLevel})");
        foreach (var w in Weapons.All)
            if (Progress.All.Count(u => u.Weapon == w) != 1)
                Fail($"{w.Name} appears {Progress.All.Count(u => u.Weapon == w)} times in the unlock table");

        // ---- progress.txt: old id-based weapon XP becomes name-based ----
        var old = Progress.Parse(["xp=5000", "wx16=1234", "wk9=3", "wx9=50", "wk17=2", "wx99=7", "wx.Courier=10", "wx11=999"]);
        var expect = new (string Name, int Xp)[] { ("BR-1 Prototype", 1234), ("Hornet SMG", 50), ("Vanguard AR", 200), ("Courier", 10) };
        foreach (var (name, xp) in expect)
            if (old.WeaponXp(name) != xp)
                Fail($"old save: {name} has {old.WeaponXp(name)} weapon XP (expected {xp})");
        if (Weapons.Find("BR-1 Prototype") is { } br && old.WeaponXp(br.Id) != 1234)
            Fail("weapon XP by id doesn't find the name-based entry");
        var saved = old.ToLines().ToArray();
        if (!saved.Contains("wx.BR-1 Prototype=1234") || saved.Any(l => l.Length > 2 && (l.StartsWith("wx", StringComparison.Ordinal) || l.StartsWith("wk", StringComparison.Ordinal)) && char.IsDigit(l[2])))
            Fail($"progress isn't saved with name keys: {string.Join(" | ", saved)}");
        var reread = Progress.Parse(saved);
        if (expect.Any(e => reread.WeaponXp(e.Name) != e.Xp) || reread.Xp != 5000)
            Fail("progress didn't survive a save and reload");
        if (!Progress.Parse(["wx.Some Modded Gun=77"]).ToLines().Contains("wx.Some Modded Gun=77"))
            Fail("weapon XP for a weapon that isn't loaded was dropped");
        Line($"old save migrated: {string.Join(", ", expect.Select(e => $"{e.Name} {old.WeaponXp(e.Name)}"))}");

        // ---- Multiplayer fingerprint ----
        if (WeaponSet.Mismatch(WeaponSet.WeaponsHash, (byte)Weapons.All.Length, WeaponSet.AttachmentsHash, (byte)attachments.Length) != null)
            Fail("our own weapon set doesn't match itself");
        var tweaked = Weapons.All[0].Clone();
        tweaked.Damage++;
        if (WeaponSet.Fnv([WeaponFile.ToJson(tweaked)]) == WeaponSet.Fnv([WeaponFile.ToJson(Weapons.All[0])]))
            Fail("changing a stat didn't change the fingerprint");
        var mismatch = WeaponSet.Mismatch(WeaponSet.WeaponsHash ^ 1, (byte)(Weapons.All.Length + 1), WeaponSet.AttachmentsHash, (byte)attachments.Length);
        if (mismatch == null || !mismatch.Contains("Weapons folder"))
            Fail($"a different weapon set wasn't reported clearly: {mismatch}");
        else
            Line($"mismatch message: {mismatch}");

        // ---- The examples in the guides load ----
        foreach (var (guide, isWeapon) in new[] { (Path.Combine(Weapons.Folder, "README.md"), true), (Path.Combine(Attachments.Folder, "README.md"), false) })
        {
            var json = GuideExample(guide);
            if (json == null)
                Fail($"{guide} (the modders' guide) wasn't copied next to the game");
            else if (json.Length == 0)
                Fail($"{guide} has no ```json example");
            else
            {
                try
                {
                    var name = isWeapon ? WeaponFile.Load(json, "README example").Name : AttachmentFile.Load(json, "README example").Name;
                    Line($"guide example '{name}' loads");
                }
                catch (WeaponFileException ex)
                {
                    Fail($"the example in {guide} doesn't load: {ex.Message}");
                }
            }
        }

        // ---- Broken files give useful messages ----
        const string ok = "\"name\": \"X\", \"class\": \"SMG\", \"damage\": 20, \"rangeM\": 10, \"rpm\": 600, \"mag\": 30, \"reload\": 2, \"hipSpreadDeg\": 2";
        foreach (var (bad, expectText, weapon) in new[]
                 {
                     ("{ \"name\": \"X\", \"class\": \"SMG\" }", "missing", true),
                     ("{ " + ok + ", \"damgae\": 5 }", "unknown field", true),
                     ("{ " + ok.Replace("\"rpm\": 600", "\"rpm\": 0") + " }", "between", true),
                     ("{ " + ok.Replace("\"SMG\"", "\"Laser\"") + " }", "isn't one of", true),
                     ("{ " + ok.Replace("\"X\"", "\"A=B\"") + " }", "name", true),
                     ("{ " + ok + ", }", "", true), // trailing commas are allowed
                     ("{ " + ok + ", \"recoil\": { \"vertcal\": 1 } }", "unknown recoil field", true),
                     ("{ " + ok + ", \"recoil\": { \"vertical\": 9 } }", "between", true),
                     ("{ " + ok + ", \"recoil\": 2 }", "expected", true),
                     ("{ \"name\": \"X\", \"slot\": \"Muzzle\", \"effects\": [ { \"stat\": \"recoil\", \"mul\": 0.8 } ] }", "unknown stat", false),
                     ("{ \"name\": \"X\", \"slot\": \"Muzzle\", \"effects\": [ { \"stat\": \"recoilDeg\", \"mul\": 0.8, \"add\": 1 } ] }", "only one", false),
                     ("{ \"name\": \"X\", \"slot\": \"Magazine\", \"effects\": [ { \"stat\": \"reload\", \"grow\": 2 } ] }", "grow", false),
                     ("{ \"name\": \"X\", \"slot\": \"Hat\" }", "isn't one of", false),
                     ("{ \"name\": \"X\", \"slot\": \"Stock\", \"fits\": { \"clases\": [\"SMG\"] } }", "unknown rule", false),
                 })
        {
            try
            {
                if (weapon)
                    WeaponFile.Load(bad, "bad.json");
                else
                    AttachmentFile.Load(bad, "bad.json");
                if (expectText.Length > 0)
                    Fail($"a broken {(weapon ? "weapon" : "attachment")} file loaded without complaint (expected '{expectText}')");
            }
            catch (WeaponFileException ex) when (expectText.Length > 0 && ex.Message.Contains(expectText, StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (Exception ex)
            {
                Fail($"{(weapon ? "weapon" : "attachment")} file gave the wrong error (expected '{expectText}'): {ex.Message}");
            }
        }
    }

    // ---- 6. Recoil ---------------------------------------------------------------------------

    /// <summary>Result of a simulated spray: the aim kicks per shot, the highest climb, and the aim after letting go.</summary>
    private sealed record SprayResult(List<(float Pitch, float Yaw)> Kicks, float Peak, float FinalPitch, float FinalYaw, bool Finite);

    /// <summary>
    /// Holds the trigger for <paramref name="shots"/> rounds (semi guns at their max rate, bursts with their pause) with a
    /// fixed frame time, then waits <paramref name="hold"/> seconds. Starts aiming at pitch 0, yaw 0.
    /// </summary>
    private static SprayResult SimulateSpray(WeaponDef d, Stance stance, float aimT, int shots, float dt, float hold = 2f, RecoilState? state = null)
    {
        var r = state ?? new RecoilState();
        float pitch = 0, yaw = 0, peak = 0;
        var finite = true;
        var kicks = new List<(float, float)>();
        double t = 10, next = t;
        var fired = 0;
        while (fired < shots)
        {
            while (fired < shots && t >= next)
            {
                var (kp, ky) = r.Shot(d, stance, aimT, t, pitch, yaw);
                kicks.Add((kp, ky));
                pitch += kp;
                yaw += ky;
                fired++;
                next += d.FireInterval;
                if (d.Mode == FireMode.Burst && fired % d.BurstCount == 0)
                    next += d.BurstDelay;
            }
            r.Update(dt, t, ref pitch, ref yaw);
            peak = MathF.Max(peak, pitch);
            t += dt;
        }
        for (var e = 0f; e < hold; e += dt, t += dt)
        {
            r.Update(dt, t, ref pitch, ref yaw);
            finite &= float.IsFinite(r.Camera.Pitch + r.Camera.Yaw + r.Camera.Roll + r.Camera.Fov + r.Kick.Back + r.Kick.Up + r.Kick.Roll + r.Kick.Side);
        }
        finite &= float.IsFinite(pitch) && float.IsFinite(yaw) && float.IsFinite(peak);
        return new SprayResult(kicks, peak, pitch, yaw, finite);
    }

    private static float Variance(IEnumerable<float> xs)
    {
        var a = xs.ToArray();
        var m = a.Average();
        return a.Sum(x => (x - m) * (x - m)) / a.Length;
    }

    // ---- Death cam and kill card ------------------------------------------------------------

    private static void CheckDeathCam()
    {
        static bool Finite(Vector3 v) => float.IsFinite(v.X + v.Y + v.Z);
        static float Open(Vector3 o, Vector3 d, float max) => max;
        var eye = new Vector3(0, 1.6f, 0);

        // 1. Easing: ends pinned, monotonic, NaN-safe.
        if (DeathCam.EaseInOut(0) != 0 || MathF.Abs(DeathCam.EaseInOut(1) - 1) > 1e-6f || MathF.Abs(DeathCam.EaseInOut(0.5f) - 0.5f) > 1e-4f
            || DeathCam.EaseOut(float.NaN) != 0 || DeathCam.EaseInOut(-3) != 0 || DeathCam.EaseInOut(9) != 1)
            Fail("death cam easing isn't pinned to 0..1");
        for (var i = 1; i <= 100; i++)
            if (DeathCam.EaseInOut(i / 100f) < DeathCam.EaseInOut((i - 1) / 100f) || DeathCam.EaseOut(i / 100f) < DeathCam.EaseOut((i - 1) / 100f))
                Fail($"death cam easing isn't monotonic at {i / 100f}");

        // 2. Turns to face a killer 12 m east (start facing north), smoothly, and zooms a little.
        var cam = new DeathCam();
        cam.Begin(eye, 0, 0, 100, Open);
        var killer = new Vector3(12, 1.6f, 0);
        var lastYaw = 0f;
        var maxStep = 0f;
        var heldStill = true;
        for (var f = 0; f <= 180; f++)
        {
            var now = 100 + f / 60.0;
            cam.Update(now, 1 / 60f, killer);
            if (!Finite(cam.Eye) || !float.IsFinite(cam.Yaw + cam.Pitch + cam.FovMul))
            {
                Fail($"death cam went non-finite at frame {f}");
                break;
            }
            if (now - 100 < DeathCam.HoldTime - 0.02 && MathF.Abs(cam.Yaw) > 1e-4f)
                heldStill = false;
            maxStep = MathF.Max(maxStep, MathF.Abs(World.WrapAngle(cam.Yaw - lastYaw)));
            lastYaw = cam.Yaw;
        }
        var toKiller = Vector3.Normalize(killer - cam.Eye);
        var facing = Vector3.Dot(cam.Forward, toKiller);
        Line($"turned to the killer: facing dot {facing:0.0000}, biggest per-frame turn {maxStep * 180 / MathF.PI:0.0} deg, fov x{cam.FovMul:0.00}, camera at {cam.Eye.X:0.00},{cam.Eye.Y:0.00},{cam.Eye.Z:0.00}");
        if (facing < 0.999f)
            Fail($"death cam doesn't end facing the killer (dot {facing})");
        if (!heldStill)
            Fail("death cam turned during its hold");
        if (maxStep > 0.12f)
            Fail($"death cam turn isn't smooth ({maxStep} rad in one frame)");
        if (cam.FovMul is < 0.6f or > 0.95f)
            Fail($"death cam zoom out of range (x{cam.FovMul})");
        if (cam.Eye.Y <= eye.Y + 0.5f || cam.Eye.Z <= 0.5f)
            Fail("death cam didn't rise and pull back in open space");

        // It keeps tracking a moving killer.
        for (var f = 0; f < 60; f++)
            cam.Update(103 + f / 60.0, 1 / 60f, killer + new Vector3(0, 0, -8 * f / 60f));
        var moved = killer + new Vector3(0, 0, -8 * 59 / 60f);
        if (Vector3.Dot(cam.Forward, Vector3.Normalize(moved - cam.Eye)) < 0.98f)
            Fail("death cam doesn't track a moving killer");

        // 3. Walls: the rest point stays clear of geometry (ray hits at 0.5 m in every direction).
        var rest = DeathCam.RestPoint(eye, 0, (o, d, max) => MathF.Min(max, 0.5f));
        if (Vector3.Distance(rest, eye) > 0.45f)
            Fail($"death cam pulled back into a wall ({Vector3.Distance(rest, eye)} m)");
        var nanRest = DeathCam.RestPoint(eye, float.NaN, (o, d, max) => float.NaN);
        if (!Finite(nanRest))
            Fail("death cam rest point went NaN");
        World.SetMap(Maps.All[0]);
        foreach (var sp in World.Map.Spawns.Take(6))
        {
            var e = sp + new Vector3(0, World.EyeHeight, 0);
            for (var y = 0; y < 8; y++)
            {
                // The camera rises, then pulls back: both legs must be clear, and it mustn't end inside a box.
                var r = DeathCam.RestPoint(e, y * MathF.Tau / 8, World.RaycastWorld);
                var raised = new Vector3(e.X, r.Y, e.Z);
                var up = r.Y - e.Y;
                var backLeg = r - raised;
                var blocked = (up > 1e-3f && World.RaycastWorld(e, Vector3.UnitY, up) < up - 1e-3f)
                              || (backLeg.Length() > 1e-3f && World.RaycastWorld(raised, Vector3.Normalize(backLeg), backLeg.Length()) < backLeg.Length() - 1e-3f)
                              || World.Map.Solid.Any(b => r.X > b.Min.X && r.X < b.Max.X && r.Y > b.Min.Y && r.Y < b.Max.Y && r.Z > b.Min.Z && r.Z < b.Max.Z);
                if (blocked)
                    Fail($"death cam rest point goes through a wall on {World.Map.Name} at spawn {sp}");
            }
        }

        // 4. No killer to look at: self-kill (drifts down, never NaN), killer = victim position, killer missing, NaN target.
        var self = new DeathCam();
        self.Begin(eye, 1, 0.2f, 0, Open);
        for (var f = 0; f <= 180; f++)
            self.Update(f / 60.0, 1 / 60f, null, drift: true);
        if (DeathCam.LookAt(eye, eye) != null)
            Fail("LookAt from a point to itself should be null");
        var same = new DeathCam();
        same.Begin(eye, 1, 0.2f, 0, (o, d, max) => 0); // boxed in, so the camera stays at the eye
        for (var f = 0; f <= 180; f++)
            same.Update(f / 60.0, 1 / 60f, eye); // the "killer" is exactly where the camera is
        var missing = new DeathCam();
        missing.Begin(eye, 1, 0.2f, 0, Open);
        for (var f = 0; f <= 180; f++)
            missing.Update(f / 60.0, f == 0 ? 0 : 1 / 60f, f % 2 == 0 ? null : new Vector3(float.NaN, 0, 0));
        foreach (var (name, c) in new[] { ("self-kill", self), ("killer = victim", same), ("killer missing", missing) })
            if (!Finite(c.Eye) || !float.IsFinite(c.Yaw + c.Pitch + c.FovMul) || MathF.Abs(World.WrapAngle(c.Yaw - 1)) > 1e-3f)
                Fail($"death cam with {name}: yaw {c.Yaw}, pitch {c.Pitch}, fov {c.FovMul}");
        if (self.Pitch > -0.3f)
            Fail("self-kill death cam doesn't tilt down to the body");
        Line($"self-kill pitch {self.Pitch:0.00}, killer = victim pitch {same.Pitch:0.00}, killer missing yaw {missing.Yaw:0.00}");

        // A target that only shows up late still eases in (no snap).
        var late = new DeathCam();
        late.Begin(eye, 0, 0, 0, Open);
        var lateStep = 0f;
        var prev = 0f;
        for (var f = 0; f <= 240; f++)
        {
            late.Update(f / 60.0, 1 / 60f, f < 90 ? null : new Vector3(-10, 1.6f, 5));
            lateStep = MathF.Max(lateStep, MathF.Abs(World.WrapAngle(late.Yaw - prev)));
            prev = late.Yaw;
        }
        if (lateStep > 0.12f)
            Fail($"death cam snapped when the kill message arrived late ({lateStep} rad in one frame)");

        // 5. Packets: round trip with the extras, and an old 4-byte packet still reads.
        var w = new PacketWriter();
        var pk = new KillPacket(3, 0, true, Weapons.DefaultPrimary.Id, 0x0A0B0C, 77, new Vector3(1, 2, 3));
        pk.Write(w);
        var rd = new PacketReader(w.Span[1..]);
        var back = KillPacket.Read(ref rd);
        if (w.Span[0] != (byte)Msg.Kill || w.Length != 1 + KillPacket.Size || back != pk)
            Fail($"kill packet round trip: {back} ({w.Length} bytes)");
        w.Begin(Msg.Kill).U8(3).U8(0).U8(0).U8(Game.MeleeKillId);
        var oldRd = new PacketReader(w.Span[1..]);
        var old = KillPacket.Read(ref oldRd);
        if (old.HasExtras || old.Kit != 0 || old.Source != null || old.KillId != Game.MeleeKillId)
            Fail($"old-style kill packet read wrong: {old}");
        Line($"kill packet: {w.Length} bytes old, {1 + KillPacket.Size} bytes now (protocol {ServerInfo.ProtocolVersion})");

        // 6. Kill info: a bot shoots the local player through the real damage path.
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var me = game.Players[game.LocalId];
        var bot = game.Players.Values.First(p => p.Id != game.LocalId);
        game.SelfTestPerks(me, PerkSet.Empty);
        game.SelfTestPerks(bot, PerkSet.Empty);
        game.SelfTestPlace(me, new Vector3(5, 0, 5), 0);
        game.SelfTestPlace(bot, new Vector3(5, 0, 15), MathF.PI);
        var gun = Weapons.All.First(x => Attachments.For(x).Any(a => a.Slot == AttachSlot.Optic));
        var optic = Attachments.For(gun).First(a => a.Slot == AttachSlot.Optic);
        bot.Primary = gun.Id;
        bot.PrimaryKit = Attachments.With(0, AttachSlot.Optic, optic.Index);
        bot.Health = 63;
        var killed = game.DebugKill(bot, me, gun.Id, head: true, 10);
        var info = game.LastDeath;
        if (!killed || info == null)
        {
            Fail($"a bot kill on the local player wasn't recorded (killed {killed})");
            return;
        }
        Line($"recorded: {info.Kind} by {info.KillerName} with {info.WeaponName} [{Attachments.Describe(info.Kit)}], headshot {info.Headshot}, {info.Distance:0.0} m, killer HP {info.KillerHp}, streak {info.KillerStreak}");
        if (info.Kind != KillKind.Gun || info.KillerId != bot.Id || !info.Headshot || info.KillerHp != 63 || info.Weapon?.Id != gun.Id
            || Attachments.Slot(info.Kit, AttachSlot.Optic) != optic.Index || info.KillerStreak != 1)
            Fail("kill info recorded wrong");
        if (MathF.Abs(info.Distance - 10) > 0.5f)
            Fail($"kill distance {info.Distance} (expected ~10 m)");

        // The camera picks the killer, their body once they die, and holds there if they respawn.
        var down = false;
        var tgt = DeathCam.PickTarget(info, bot, ref down, false);
        if (tgt is not { } t0 || Vector3.Distance(t0, bot.RenderEye) > 1e-3f)
            Fail("death cam doesn't target the living killer");
        bot.Alive = false;
        if (DeathCam.PickTarget(info, bot, ref down, true) is not { } t1 || t1.Y > bot.RenderPosition.Y + 1 || !down)
            Fail("death cam doesn't target the dead killer's body");
        bot.Alive = true;
        if (DeathCam.PickTarget(info, bot, ref down, true) != null)
            Fail("death cam followed the killer to their respawn");
        var gone = false;
        if (DeathCam.PickTarget(info, null, ref gone, false) == null || DeathCam.PickTarget(info, null, ref gone, true) != null)
            Fail("death cam with a disconnected killer: wrong target");

        // Other kinds: melee, throwables, self-kills, kill-streak ids this build may not know.
        var melee = KillInfo.Create(new KillPacket(bot.Id, me.Id, false, Game.MeleeKillId), bot, me, "Bot", 2, 0);
        var frag = KillInfo.Create(new KillPacket(bot.Id, me.Id, false, (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag), 0, 10, new Vector3(5, 0.2f, 7)), bot, me, "Bot", 1, 0);
        var selfKill = KillInfo.Create(new KillPacket(me.Id, me.Id, false, (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag), 0, 0, me.Eye), me, me, "You", -1, 0);
        var streak = KillInfo.Create(new KillPacket(bot.Id, me.Id, false, 233, 0, 100, null), null, me, "Player 9", -1, 0);
        Line($"melee '{melee.WeaponName}' {melee.Kind}, frag '{frag.WeaponName}' {frag.Kind} from {frag.Distance:0} m, self '{selfKill.WeaponName}' {selfKill.Kind}, id 233 '{streak.WeaponName}' {streak.Kind}");
        if (melee.Kind != KillKind.Melee || melee.Weapon != null || frag.Kind != KillKind.Throwable || frag.Weapon != null
            || selfKill.Kind != KillKind.Self || selfKill.Distance >= 0 || streak.Kind != KillKind.Streak || streak.Weapon != null || streak.WeaponName.Length == 0)
            Fail("kill kinds classified wrong");
        var selfDown = false;
        if (DeathCam.PickTarget(selfKill, me, ref selfDown, false) != null)
            Fail("self-kill death cam tries to turn");
    }

    private static void CheckRecoil()
    {
        const float dt = 1f / 240f;
        var all = Weapons.All;

        // ---- Every weapon has a valid profile (the shipped files spell theirs out) ----
        foreach (var w in all)
        {
            try
            {
                Recoil.Validate(w, w.Name);
            }
            catch (WeaponFileException ex)
            {
                Fail(ex.Message);
            }
            if (w.SourceFile != null && Weapons.LegacyNames.Contains(w.Name)
                && !File.ReadAllText(Path.Combine(Weapons.Folder, w.SourceFile)).Contains("\"recoil\"", StringComparison.Ordinal))
                Fail($"{w.Name}: the shipped file has no \"recoil\" block");
            if (w.RecoilDeg > 0 && Recoil.EffectiveKickDeg(w) <= 0)
                Fail($"{w.Name}: recoil profile has no kick at all");
        }
        // A file without a block gets its class's character (an LMG barely moves sideways when crouched).
        var bare = WeaponFile.Load("{ \"name\": \"X\", \"class\": \"LMG\", \"damage\": 20, \"rangeM\": 10, \"rpm\": 600, \"mag\": 100, \"reload\": 5, \"hipSpreadDeg\": 2 }", "bare.json");
        if (bare.RecoilCrouchHorizontal > 0.2f || bare.RecoilVertical < 1f)
            Fail($"an LMG file without a recoil block didn't get LMG defaults (crouch horizontal {bare.RecoilCrouchHorizontal})");
        var partial = WeaponFile.Load("{ \"name\": \"X\", \"class\": \"SMG\", \"damage\": 20, \"rangeM\": 10, \"rpm\": 600, \"mag\": 30, \"reload\": 2, \"hipSpreadDeg\": 2, \"recoil\": { \"vertical\": 0.5 } }", "partial.json");
        var bareSmg = WeaponFile.Load("{ \"name\": \"X\", \"class\": \"SMG\", \"damage\": 20, \"rangeM\": 10, \"rpm\": 600, \"mag\": 30, \"reload\": 2, \"hipSpreadDeg\": 2 }", "bare.json");
        if (partial.RecoilVertical != 0.5f || partial.RecoilJitter != bareSmg.RecoilJitter || partial.RecoilHorizontal != bareSmg.RecoilHorizontal)
            Fail("a partial recoil block didn't keep the class defaults for the fields it leaves out");

        // ---- Patterns are deterministic per seed ----
        foreach (var w in all)
        {
            for (var i = 0; i < 40; i++)
                if (Recoil.PatternDeg(w, i, 3) != Recoil.PatternDeg(w, i, 3))
                    Fail($"{w.Name}: shot {i} isn't deterministic");
            var calm = w.Clone();
            calm.RecoilJitter = 0;
            for (var i = 0; i < 40; i++)
                if (Recoil.PatternDeg(calm, i, 1) != Recoil.PatternDeg(calm, i, 7))
                {
                    Fail($"{w.Name}: the pattern (without jitter) changes between sprays at shot {i}");
                    break;
                }
            var a = SimulateSpray(w, Stance.Stand, 1, 30, dt);
            var b = SimulateSpray(w, Stance.Stand, 1, 30, dt);
            if (!a.Kicks.SequenceEqual(b.Kicks) || a.FinalPitch != b.FinalPitch)
                Fail($"{w.Name}: two identical sprays came out differently");
            if (!a.Finite)
                Fail($"{w.Name}: spray produced NaN or infinity");
        }
        if (Weapons.Find("Vanguard AR") is { } van)
        {
            var other = van.Clone();
            other.RecoilSeed = van.RecoilSeed + 1;
            if (Enumerable.Range(0, 20).All(i => Recoil.PatternDeg(van, i, 1) == Recoil.PatternDeg(other, i, 1)))
                Fail("a different seed gave the same pattern");
        }

        // ---- LMGs: crouched sideways movement is far below standing ----
        foreach (var w in all.Where(w => w.Class == WeaponClass.LMG))
        {
            var stand = SimulateSpray(w, Stance.Stand, 1, 30, dt).Kicks.Sum(k => MathF.Abs(k.Yaw));
            var crouch = SimulateSpray(w, Stance.Crouch, 1, 30, dt).Kicks.Sum(k => MathF.Abs(k.Yaw));
            var prone = SimulateSpray(w, Stance.Prone, 1, 30, dt).Kicks.Sum(k => MathF.Abs(k.Yaw));
            Line($"{w.Name}: sideways travel over 30 rounds aimed: standing {stand * 180 / MathF.PI:0.0} deg, crouched {crouch * 180 / MathF.PI:0.00}, prone {prone * 180 / MathF.PI:0.00}");
            if (!(crouch < stand * 0.25f && prone < stand * 0.15f))
                Fail($"{w.Name}: crouched/prone horizontal recoil isn't far below standing");
        }

        // ---- SMGs jump around sideways more than assault rifles ----
        float ClassHorizontalVariance(WeaponClass c) =>
            all.Where(w => w.Class == c).Select(w => Variance(SimulateSpray(w, Stance.Stand, 0, 30, dt).Kicks.Select(k => k.Yaw * 180 / MathF.PI)))
                .DefaultIfEmpty(0).Average();
        float ClassJitterRatio(WeaponClass c) =>
            all.Where(w => w.Class == c).Select(w =>
            {
                var k = SimulateSpray(w, Stance.Stand, 0, 30, dt).Kicks;
                return MathF.Sqrt(Variance(k.Select(x => x.Yaw))) / MathF.Max(1e-6f, k.Average(x => x.Pitch));
            }).DefaultIfEmpty(0).Average();
        var smgVar = ClassHorizontalVariance(WeaponClass.SMG);
        var arVar = ClassHorizontalVariance(WeaponClass.AssaultRifle);
        var smgRatio = ClassJitterRatio(WeaponClass.SMG);
        var arRatio = ClassJitterRatio(WeaponClass.AssaultRifle);
        Line($"per-shot sideways variance: SMGs {smgVar:0.000} deg^2, rifles {arVar:0.000} deg^2; sideways/up ratio SMGs {smgRatio:0.00}, rifles {arRatio:0.00}");
        if (!(smgVar > arVar && smgRatio > arRatio * 2))
            Fail("SMG horizontal recoil isn't more erratic than assault rifles'");

        // ---- Recovery returns most of the climb, never past where you started ----
        foreach (var w in all)
        {
            // One spray: guns too slow to chain shots into a spray (snipers, the musket) recover each shot on its own.
            var shots = w.FireInterval < Recoil.ResetTime(w) ? Math.Min(20, Math.Max(w.Mag, 3)) : 1;
            var sp = SimulateSpray(w, Stance.Stand, 1, shots, dt);
            // Of everything the gun kicked you up, how much came back (semi guns recover between shots too).
            var kicked = sp.Kicks.Sum(k => k.Pitch);
            var returned = kicked > 0 ? (kicked - sp.FinalPitch) / kicked : 1;
            if (w.RecoilRecoveryAmount >= 0.5f && returned < 0.5f)
                Fail($"{w.Name}: recovery only returned {returned:P0} of the climb");
            if (sp.FinalPitch < -1e-4f)
                Fail($"{w.Name}: recovery pulled the aim below where the spray started ({sp.FinalPitch})");
        }
        if (Weapons.Find("Vanguard AR") is { } ar)
        {
            // The player pulls down against the climb: recovery must not drag the aim further down.
            var st = new RecoilState();
            float pitch = 0, yaw = 0;
            for (var i = 0; i < 10; i++)
            {
                var (kp, ky) = st.Shot(ar, Stance.Stand, 1, 10 + i * ar.FireInterval, pitch, yaw);
                pitch += kp;
                yaw += ky;
            }
            pitch = 0; // pulled all the way back down by hand
            for (var t = 0f; t < 2; t += dt)
                st.Update(dt, 11 + t, ref pitch, ref yaw);
            if (pitch < -1e-4f)
                Fail($"recovery overshot after the player had already pulled down ({pitch})");
            // Frame-rate independence: the same spray at 30 and 300 fps ends up in the same place.
            var slow = SimulateSpray(ar, Stance.Stand, 1, 20, 1 / 30f);
            var fast = SimulateSpray(ar, Stance.Stand, 1, 20, 1 / 300f);
            if (MathF.Abs(slow.FinalPitch - fast.FinalPitch) > 0.05f * MathF.Max(slow.Peak, 1e-3f))
                Fail($"recovery depends on frame rate: {slow.FinalPitch:0.0000} at 30 fps vs {fast.FinalPitch:0.0000} at 300 fps");
            var hip = SimulateSpray(ar, Stance.Stand, 0, 30, dt);
            Line($"Vanguard AR, 30 rounds from the hip: climb {hip.Peak * 180 / MathF.PI:0.0} deg, back to {hip.FinalPitch * 180 / MathF.PI:0.0} deg 2 s after letting go");
        }

        // ---- The visual shake and punch never move the actual aim ----
        foreach (var w in all)
        {
            var still = w.Clone();
            still.RecoilRecoveryAmount = 0; // no recovery: any change to the aim after firing would come from the visuals
            var st = new RecoilState();
            float pitch = 0, yaw = 0;
            var (kp, ky) = st.Shot(still, Stance.Stand, 0, 10, pitch, yaw);
            pitch += kp;
            yaw += ky;
            float p0 = pitch, y0 = yaw;
            var moved = 0f;
            for (var t = 0f; t < 0.3f; t += dt)
            {
                st.Update(dt, 10 + t, ref pitch, ref yaw);
                moved = MathF.Max(moved, MathF.Abs(st.Camera.Pitch) + MathF.Abs(st.Camera.Yaw) + MathF.Abs(st.Camera.Roll));
            }
            if (pitch != p0 || yaw != y0)
                Fail($"{w.Name}: the camera shake changed the actual aim");
            if (w.RecoilDeg > 0 && (w.RecoilShake > 0 || w.RecoilPunch > 0) && moved <= 0)
                Fail($"{w.Name}: the gun has shake/punch but the camera never moved");
        }

        // ---- Attachments cut the axes they're meant to ----
        void Expect(string attachment, Func<WeaponDef, float> stat, bool lower, string what)
        {
            if (Attachments.Find(attachment) is not { } a)
            {
                Fail($"'{attachment}' isn't loaded");
                return;
            }
            var n = 0;
            foreach (var w in all.Where(a.Fits))
            {
                var k = Attachments.Apply(w, Attachments.With(0, a.Slot, a.Index));
                if (lower ? !(stat(k) < stat(w)) : !(stat(k) > stat(w)))
                    Fail($"{attachment} on {w.Name} doesn't {(lower ? "reduce" : "raise")} {what} ({stat(w):0.###} -> {stat(k):0.###})");
                n++;
            }
            if (n == 0)
                Fail($"'{attachment}' fits no weapon");
        }
        Expect("Compensator", Recoil.VerticalDeg, true, "vertical recoil");
        Expect("Vertical Grip", Recoil.VerticalDeg, true, "vertical recoil");
        Expect("Angled Grip", w => w.RecoilFirstShot, true, "first-shot kick");
        Expect("Muzzle Brake", Recoil.HorizontalDeg, true, "horizontal recoil");
        Expect("Muzzle Brake", w => w.RecoilShake, true, "camera shake");
        Expect("Heavy Stock", w => w.RecoilRecovery, false, "recovery speed");
        Expect("Heavy Stock", Recoil.EffectiveKickDeg, true, "recoil");
        Expect("Suppressor", w => w.RecoilShake, true, "camera shake");
        if (Attachments.Find("Compensator") is { } comp && Weapons.Find("Vanguard AR") is { } v2
            && Attachments.Apply(v2, Attachments.With(0, comp.Slot, comp.Index)) is var vc && Recoil.HorizontalDeg(vc) != Recoil.HorizontalDeg(v2))
            Fail("the Compensator changed horizontal recoil (it should only fight the climb)");

        // ---- Fully stacked kits stay sane: >= 40% recoil, >= 50% hip spread, mobility in bounds ----
        foreach (var w in all)
        {
            var list = Attachments.For(w);
            uint Greedy(Func<WeaponDef, float> score)
            {
                uint kit = 0;
                for (var sl = 0; sl < Attachments.SlotCount; sl++)
                {
                    var best = list.Where(a => (int)a.Slot == sl).OrderBy(a => score(Attachments.Apply(w, Attachments.With(0, a.Slot, a.Index)))).FirstOrDefault();
                    if (best != null && score(Attachments.Apply(w, Attachments.With(0, best.Slot, best.Index))) < score(w))
                        kit = Attachments.With(kit, best.Slot, best.Index);
                }
                return kit;
            }
            var lowRecoil = Attachments.Apply(w, Greedy(Recoil.EffectiveKickDeg));
            var lowSpread = Attachments.Apply(w, Greedy(x => x.HipSpreadDeg));
            var fast = Attachments.Apply(w, Greedy(x => -x.Mobility));
            var slow = Attachments.Apply(w, Greedy(x => x.Mobility));
            if (Recoil.VerticalDeg(lowRecoil) < Recoil.VerticalDeg(w) * 0.399f || Recoil.HorizontalDeg(lowRecoil) < Recoil.HorizontalDeg(w) * 0.399f)
                Fail($"{w.Name}: a fully kitted gun has under 40% of the stock recoil ({Attachments.Describe(lowRecoil.Kit)})");
            if (lowSpread.HipSpreadDeg < w.HipSpreadDeg * 0.499f)
                Fail($"{w.Name}: a fully kitted gun has under 50% of the stock hip spread ({Attachments.Describe(lowSpread.Kit)})");
            if (fast.Mobility > 1.25f || slow.Mobility < 0.6f)
                Fail($"{w.Name}: kitted mobility {slow.Mobility:0.00}-{fast.Mobility:0.00} is outside 0.6-1.25");
            if (StatBars.Control(lowRecoil) >= 1f || StatBars.Accuracy(lowSpread) >= 1f && StatBars.Accuracy(w) < 1f)
                Fail($"{w.Name}: a kitted extreme pins a stat bar at full, hiding the difference");
        }

        // ---- No NaNs anywhere, even with odd frame times ----
        foreach (var w in all)
            foreach (var stance in new[] { Stance.Stand, Stance.Crouch, Stance.Prone })
                foreach (var aim in new[] { 0f, 0.5f, 1f })
                {
                    var st = new RecoilState();
                    float pitch = 0.3f, yaw = 3.1f;
                    for (var i = 0; i < 60; i++)
                    {
                        var (kp, ky) = st.Shot(w, stance, aim, 10 + i * 0.05, pitch, yaw);
                        pitch += kp;
                        yaw = World.WrapAngle(yaw + ky);
                        st.Update(i % 7 == 0 ? 0f : i % 11 == 0 ? 0.5f : 0.004f, 10 + i * 0.05 + 0.01, ref pitch, ref yaw);
                    }
                    var (bp, by) = Recoil.BotKick(w, new Random(1));
                    if (!float.IsFinite(pitch + yaw + bp + by + st.Camera.Pitch + st.Camera.Yaw + st.Camera.Roll + st.Camera.Fov + st.Kick.Back + st.Kick.Up + st.Kick.Roll + st.Kick.Side))
                        Fail($"{w.Name}: NaN or infinity in recoil ({stance}, aim {aim})");
                }
        Line($"{all.Length} recoil profiles checked");
    }

    // ---- 5. Packets ----------------------------------------------------------------------

    private static void CheckPackets()
    {
        // A full 32-player snapshot as the server builds it (everyone's fast entry, an eighth of the slow ones).
        var fast = Enumerable.Range(0, Game.MaxPlayers).Select(i => new SnapPlayer { Id = (byte)i, Position = Vector3.One * i, Alive = true }).ToList();
        var slow = fast.Where(f => f.Id % Game.SlowRefreshEvery == 0).Select(f => new SnapSlow { Id = f.Id, PrimaryKit = 0xABCDEF, SecondaryKit = 0x123456 }).ToList();
        var parts = new List<PacketWriter>();
        var n = Snapshot.Build(new SnapHeader { Present = uint.MaxValue }, new SnapOwn(), fast, 0, slow, parts);
        var into = new Snapshot.Packet();
        var r = new PacketReader(parts[0].Span[1..]);
        Snapshot.Read(ref r, into);
        if (into.Slow.Any(x => x.PrimaryKit != 0xABCDEF || x.SecondaryKit != 0x123456))
            Fail("U24 round trip broke in the slow fields");
        Line($"full {Game.MaxPlayers}-player snapshot: {parts[0].Length} bytes in {n} packet(s) (limit {PacketWriter.Mtu}), {Snapshot.FastBytes} per player + {Snapshot.SlowBytes} per slow entry");
        if (n != 1 || parts[0].Length > PacketWriter.Mtu)
            Fail("a 32-player snapshot doesn't fit one packet");
        if (into.Fast.Count != Game.MaxPlayers)
            Fail($"snapshot decoded {into.Fast.Count} players");
    }

    // ---- HUD layout ------------------------------------------------------------------------

    /// <summary>The HUD safe area: centred, full height, width capped by the HUD width setting.</summary>
    private static void CheckHudLayout()
    {
        void Expect(float w, float h, HudWidth mode, float left, float width)
        {
            var (l, sw) = HudLayout.Span(w, h, mode);
            Line($"  {w}x{h} {HudLayout.Label(mode)}: left {l}, width {sw}");
            if (MathF.Abs(l - left) > 0.5f || MathF.Abs(sw - width) > 0.5f)
                Fail($"{w}x{h} at {HudLayout.Label(mode)}: got left {l} width {sw}, expected left {left} width {width}");
        }

        // 16:9 screen: the safe area is the whole screen whatever the setting.
        Expect(1920, 1080, HudWidth.Wide16x9, 0, 1920);
        Expect(1920, 1080, HudWidth.Wide21x9, 0, 1920);
        Expect(1920, 1080, HudWidth.Full, 0, 1920);
        // 3440x1440 (Dell AW3420DW, really 21.5:9): 2560 wide at 16:9, 3360 at 21:9.
        Expect(3440, 1440, HudWidth.Wide16x9, 440, 2560);
        Expect(3440, 1440, HudWidth.Wide21x9, 40, 3360);
        Expect(3440, 1440, HudWidth.Full, 0, 3440);
        // 32:9 (Samsung Odyssey G95NC): 2560 at 16:9, 3360 at 21:9.
        Expect(5120, 1440, HudWidth.Wide16x9, 1280, 2560);
        Expect(5120, 1440, HudWidth.Wide21x9, 880, 3360);
        Expect(5120, 1440, HudWidth.Full, 0, 5120);
        // Portrait / narrow windows: the whole screen.
        Expect(1080, 1920, HudWidth.Wide16x9, 0, 1080);
        Expect(800, 900, HudWidth.Wide21x9, 0, 800);
        // Degenerate (minimised window): no NaN, no negative width.
        var (dl, dw) = HudLayout.Span(0, 0, HudWidth.Wide16x9);
        if (float.IsNaN(dl) || float.IsNaN(dw) || dw < 0)
            Fail($"0x0 screen gave left {dl} width {dw}");

        // Default (settings files from before this option have no hud_width line, so they keep it).
        if (new Settings().HudWidth != HudWidth.Wide16x9)
            Fail("HUD width should default to 16:9");
    }

    // ---- Weapon select ---------------------------------------------------------------------

    /// <summary>The weapon-select screen's tabs hold every weapon exactly once, and its stat comparison points the right way.</summary>
    /// <summary>The loadout screen's page stack: Back goes back one page, and from the overview it signals "close".</summary>
    private static void CheckLoadoutNav()
    {
        var nav = new LoadoutNav();
        void Expect(LoadoutPage page, int depth, string what)
        {
            if (nav.Current != page || nav.Depth != depth)
                Fail($"{what}: expected {page} at depth {depth}, got {nav.Current} at depth {nav.Depth}");
        }
        Expect(LoadoutPage.Overview, 1, "new stack");
        if (!nav.Back())
            Fail("Back on the overview should signal close");
        Expect(LoadoutPage.Overview, 1, "after Back on the overview");

        // Overview > weapon select > attachments: Back returns to the weapon select, then the overview, then closes.
        nav.Open(LoadoutPage.Primary);
        nav.Open(LoadoutPage.Attachments, 7);
        Expect(LoadoutPage.Attachments, 3, "weapon select > attachments");
        if (nav.Arg != 7)
            Fail($"attachments page lost its weapon id (got {nav.Arg})");
        if (nav.Back())
            Fail("Back from attachments closed the screen");
        Expect(LoadoutPage.Primary, 2, "Back from attachments opened via the weapon select");
        nav.Back();
        Expect(LoadoutPage.Overview, 1, "Back from the weapon select");
        if (!nav.Back())
            Fail("Back on the overview (after a round trip) should signal close");

        // Overview card's "Edit attachments": Back returns straight to the overview.
        nav.Open(LoadoutPage.Attachments, 3);
        nav.Back();
        Expect(LoadoutPage.Overview, 1, "Back from attachments opened from a card");

        // Progression returns to whatever was before it.
        nav.Open(LoadoutPage.Secondary);
        nav.Open(LoadoutPage.Attachments, 2);
        nav.Open(LoadoutPage.Progression);
        Expect(LoadoutPage.Progression, 4, "progression over attachments");
        nav.Back();
        Expect(LoadoutPage.Attachments, 3, "Back from progression");
        if (nav.Arg != 2)
            Fail("the attachments page under progression lost its weapon id");

        // Re-opening a page already on the stack goes back down to it (no loops, bounded depth).
        nav.Open(LoadoutPage.Progression);
        nav.Open(LoadoutPage.Progression);
        Expect(LoadoutPage.Progression, 4, "progression opened twice");
        nav.Open(LoadoutPage.Secondary);
        Expect(LoadoutPage.Secondary, 2, "re-opening the weapon select");
        for (var i = 0; i < 50; i++)
            nav.Open(i % 2 == 0 ? LoadoutPage.Attachments : LoadoutPage.Progression, i);
        if (nav.Depth > 4)
            Fail($"stack grew to {nav.Depth} pages after alternating opens");

        // Opening the overview (or Reset) clears everything.
        nav.Open(LoadoutPage.Overview);
        Expect(LoadoutPage.Overview, 1, "opening the overview");
        nav.Open(LoadoutPage.Perks);
        nav.Reset();
        Expect(LoadoutPage.Overview, 1, "Reset");
        Line("  push/pop order, attachments > weapon select > overview > close, progression > previous page: ok");
    }

    private static void CheckWeaponSelect()
    {
        var seen = new int[Weapons.All.Length];
        foreach (var slot in new[] { Slot.Primary, Slot.Secondary })
        {
            var tabs = WeaponSelect.Tabs(slot);
            Line($"  {slot}: {string.Join(", ", tabs.Select(c => $"{WeaponSelect.TabName(c)} {WeaponSelect.InTab(c).Length} ({WeaponSelect.LockedCount(c, 1)} locked at level 1)"))}");
            if (tabs.Length == 0)
                Fail($"{slot} slot has no tabs");
            if (tabs.Distinct().Count() != tabs.Length)
                Fail($"{slot} slot has a tab twice");
            foreach (var c in tabs)
            {
                var guns = WeaponSelect.InTab(c);
                if (guns.Length == 0)
                    Fail($"empty tab {WeaponSelect.TabName(c)}");
                for (var i = 0; i < guns.Length; i++)
                {
                    var w = guns[i];
                    seen[w.Id]++;
                    if (w.Class != c || w.Slot != slot)
                        Fail($"{w.Name} ({w.ClassName}, {w.Slot}) is in the {slot} tab {WeaponSelect.TabName(c)}");
                    if (i > 0 && WeaponSelect.UnlockLevel(guns[i - 1]) > WeaponSelect.UnlockLevel(w))
                        Fail($"tab {WeaponSelect.TabName(c)} isn't sorted by unlock level ({guns[i - 1].Name} before {w.Name})");
                    if (WeaponSelect.UnlockLevel(w) != Progress.UnlockLevel(w))
                        Fail($"{w.Name}: cached unlock level {WeaponSelect.UnlockLevel(w)} != {Progress.UnlockLevel(w)}");
                }
            }
        }
        if (WeaponSelect.Tabs(Slot.Secondary) is not ([WeaponClass.Pistol] or [WeaponClass.Pistol, WeaponClass.Launcher]))
            Fail("the secondary slot should have just the Pistols and Launchers tabs");
        if (WeaponSelect.Tabs(Slot.Primary).Contains(WeaponClass.Launcher))
            Fail("launchers showing in the primary tabs");
        if (WeaponSelect.Tabs(Slot.Primary).Contains(WeaponClass.Pistol))
            Fail("pistols showing in the primary tabs");
        for (var id = 0; id < seen.Length; id++)
            if (seen[id] != 1)
                Fail($"{Weapons.All[id].Name} appears in {seen[id]} tabs (should be exactly 1)");

        // Scores stay in 0-1; comparing a gun with itself shows no change.
        foreach (var w in Weapons.All)
            for (var i = 0; i < WeaponSelect.StatCount; i++)
            {
                var s = (SelectStat)i;
                var v = WeaponSelect.Score(w, s);
                if (!(v >= 0 && v <= 1))
                    Fail($"{w.Name}: {WeaponSelect.StatName(s)} score {v} outside 0-1");
                if (WeaponSelect.Delta(w, w, s) != 0)
                    Fail($"{w.Name}: {WeaponSelect.StatName(s)} differs from itself");
            }

        // Deltas point the right way: better is positive whichever way the raw number goes.
        var baseGun = Weapons.DefaultPrimary.Clone();
        baseGun.Damage = 25;
        baseGun.Pellets = 1;
        baseGun.Rpm = 600;
        baseGun.RangeM = 30;
        baseGun.HipSpreadDeg = 3f;
        baseGun.RecoilDeg = 2f;
        baseGun.Mobility = 0.95f;
        baseGun.EquipTime = 0.5f;
        baseGun.AdsTime = 0.2f;
        void Expect(string what, Action<WeaponDef> change, SelectStat s, int sign)
        {
            var other = baseGun.Clone();
            change(other);
            var d = WeaponSelect.Delta(baseGun, other, s);
            Line($"  {what}: {WeaponSelect.StatName(s)} {(d > 0 ? "+" : "")}{d}");
            if (Math.Sign(d) != sign)
                Fail($"{what}: {WeaponSelect.StatName(s)} delta {d}, expected sign {sign}");
        }
        Expect("more damage", w => w.Damage = 35, SelectStat.Damage, 1);
        Expect("8 pellets", w => w.Pellets = 8, SelectStat.Damage, 1);
        Expect("slower fire", w => w.Rpm = 400, SelectStat.FireRate, -1);
        Expect("longer range", w => w.RangeM = 45, SelectStat.Range, 1);
        Expect("wider spread", w => w.HipSpreadDeg = 5f, SelectStat.Accuracy, -1);
        Expect("less recoil", w => w.RecoilDeg = 1f, SelectStat.Control, 1);
        Expect("faster movement", w => w.Mobility = 1.1f, SelectStat.Mobility, 1);
        Expect("slower aim", w => w.AdsTime = 0.35f, SelectStat.Handling, -1);
        Expect("quicker draw", w => w.EquipTime = 0.3f, SelectStat.Handling, 1);
        Expect("same gun", _ => { }, SelectStat.Range, 0);

        // Weapon "level" = attachments unlocked by weapon XP.
        var kitGun = Weapons.All.FirstOrDefault(w => Attachments.For(w).Count > 1);
        if (kitGun != null)
        {
            var max = Attachments.MaxXp(kitGun);
            var n = Attachments.For(kitGun).Count;
            if (WeaponSelect.UnlockedAttachments(kitGun, 0) != 0 || WeaponSelect.UnlockedAttachments(kitGun, max) != n)
                Fail($"{kitGun.Name}: unlocked attachments at 0 / max XP are {WeaponSelect.UnlockedAttachments(kitGun, 0)} / {WeaponSelect.UnlockedAttachments(kitGun, max)}, expected 0 / {n}");
            var mid = WeaponSelect.UnlockedAttachments(kitGun, max / 2);
            if (mid <= 0 || mid >= n)
                Fail($"{kitGun.Name}: {mid}/{n} attachments at half XP");
            Line($"  {kitGun.Name}: {mid}/{n} attachments at {max / 2} of {max} weapon XP");
        }
    }
}
