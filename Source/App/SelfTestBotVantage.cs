using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Bot vantage points" (Source/Game/Bots/Vantage): the precomputed spots on a rooftop map, a hill map
/// and a city beat the lowest street node for height, sightlines and cover; a sniper bot walks to a spot before taking on
/// a distant target (holding its fire on the way) while an SMG bot goes straight at it; a held spot sets the stuck
/// detector's flag; shot from out of sight it marks the spot compromised and moves; an enemy within 8 m brings out the
/// sidearm (and it goes away again); two snipers never share a spot; King of the Hill spots overlook the hill; an Insane
/// squad's long gun gets an overwatch order; the decisions allocate nothing; and the precompute on the three heaviest maps
/// stays within its budget. Every scenario is a few seconds long (no long bot matches).
/// </summary>
public static partial class SelfTest
{
    /// <summary>Most a map's vantage precompute may take on the worker thread (CI runner, ms).</summary>
    private const double VantageBudgetMs = 2500;

    private static readonly List<string> VantageNotes = new();

    private static void VNote(string s)
    {
        Line(s);
        VantageNotes.Add(s);
    }

    private static void CheckBotVantage()
    {
        try
        {
            CheckVantageScoring();
            CheckVantageSniperVsSmg();
            CheckVantageHoldAndRelocate();
            CheckVantageSidearm();
            CheckVantageTwoSnipers();
            CheckVantageKingOfTheHill();
            CheckVantageSquadOverwatch();
            CheckVantageCost();
        }
        finally
        {
            if (VantageNotes.Count > 0)
                Line("::notice title=Bot vantage points::" + string.Join("%0A", VantageNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
            World.SetMap(Maps.All[0]);
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    private static WeaponDef VGun(WeaponClass c, Slot slot) =>
        Weapons.All.Where(w => w.Class == c && w.Slot == slot && !w.IsLauncher).OrderBy(w => w.Mode == FireMode.Semi ? 1 : 0).ThenBy(w => w.Id).First();

    private static WeaponDef VSniper => Weapons.All.Where(w => w.Class == WeaponClass.Sniper && w.Slot == Slot.Primary).OrderByDescending(w => w.FireInterval).ThenBy(w => w.Id).First();

    private static WeaponDef VPistol => Weapons.All.Where(w => w.Class == WeaponClass.Pistol && w.Slot == Slot.Secondary).OrderBy(w => w.Id).First();

    /// <summary>The scenario map: Hillfort (a hill with a fort on it: vantage points a bot can walk up to).</summary>
    private static Map VMap => Maps.Find("Hillfort") ?? Maps.All[0];

    /// <summary>Places a bot like <see cref="Game.SelfTestPlace"/> and starts it on a fresh life's bot state (nothing left over from the countdown).</summary>
    private static void VPlace(Game game, Player p, Vector3 feet, float yaw)
    {
        game.SelfTestPlace(p, feet, yaw);
        var b = p.Bot!;
        b.LifeTick = uint.MaxValue; // BotPerceive's per-life reset on the next tick
        b.VantageLife = uint.MaxValue; // ... and the vantage roll
        b.VState = VantageState.None;
        b.VantageSpot = -1;
        b.StuckCount = 0;
        b.ClearPath();
    }

    /// <summary>Hands a bot exactly these guns (no attachments) right now.</summary>
    private static void VArm(Player p, WeaponDef primary, WeaponDef secondary)
    {
        p.Primary = p.WantPrimary = primary.Id;
        p.Secondary = p.WantSecondary = secondary.Id;
        p.PrimaryKit = p.SecondaryKit = p.WantPrimaryKit = p.WantSecondaryKit = 0;
        p.Weapon = primary.Id;
        p.Bot!.Ammo = primary.Mag;
        p.Bot.ReloadUntil = 0;
    }

    /// <summary>
    /// A seeded game on <paramref name="map"/>: the given bots (Hard) on Blue, as many Red bots kept dead (minus one: the
    /// host is Red too) so nobody is moved over. The host is placed by the caller (or kept dead).
    /// </summary>
    private static (Game Game, List<Player> Blue) VScenario(Map map, int seed, GameMode mode, ref double now, params BotPersonality[] blue)
    {
        Game.SeedNextGame = seed;
        World.SetMap(map);
        var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
        };
        game.Mode = mode;
        NavGraph.Pump(double.MaxValue); // the nav graph and (synchronously, here) the vantage points
        foreach (var kind in blue)
            game.AddBot(BotDifficulty.Hard, kind, quiet: true);
        for (var i = 1; i < blue.Length; i++)
            game.AddBot(BotDifficulty.Normal, BotPersonality.Anchor, quiet: true);
        var bots = game.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).ToList();
        var squad = bots.Take(blue.Length).ToList();
        foreach (var p in squad)
            p.Team = 0;
        foreach (var p in bots.Skip(blue.Length))
        {
            p.Team = 1;
            p.Alive = false;
            p.RespawnAt = 1e9;
        }
        var host = game.Players[0];
        host.Team = 1;
        KeepHostDead(game);
        Tick(game, ref now, Game.CountdownSeconds + 0.5); // past the countdown (frozen, nobody can be hurt)
        foreach (var p in bots.Skip(blue.Length))
        {
            p.Alive = false;
            p.RespawnAt = 1e9;
        }
        KeepHostDead(game);
        return (game, squad);
    }

    private static void KeepHostDead(Game game)
    {
        var host = game.Players[0];
        host.Alive = false;
        host.RespawnAt = 1e9;
    }

    /// <summary>The main-area node with the lowest floor (a street, a valley): the "not a vantage point" reference.</summary>
    private static int LowestMainNode(NavGraph g)
    {
        var best = -1;
        foreach (var i in g.MainNodes)
            if (best < 0 || g.Nodes[i].Y < g.Nodes[best].Y - 0.01f || (MathF.Abs(g.Nodes[i].Y - g.Nodes[best].Y) <= 0.01f && FlatOf(g.Nodes[i]).Length() < FlatOf(g.Nodes[best]).Length()))
                best = i;
        return best;
    }

    private static bool LowNode(Vector3 q, float floor) => q.Y < floor + 0.6f;

    private static Vector3 Chest(Vector3 feet) => feet + new Vector3(0, 1.1f, 0);

    private static Vector3 EyeAt(Vector3 feet) => feet + new Vector3(0, World.EyeHeight, 0);

    // ---- 1. Scoring --------------------------------------------------------------------------------------------

    private static void CheckVantageScoring()
    {
        // Rooftops and Downtown: roofs you can't walk up to (spawn on them or drop off them) give no sniper spots, by design.
        var withLong = 0;
        foreach (var name in new[] { "Hillfort", "Adobe Mesa", "Main Street", "London", "Rooftops", "Downtown" })
        {
            var map = Maps.Find(name);
            if (map == null)
            {
                Fail($"vantage scoring: no map \"{name}\"");
                continue;
            }
            World.SetMap(map);
            NavGraph.Pump(double.MaxValue);
            var g = NavGraph.Current;
            var vm = VantageMap.For(g);
            if (vm == null || vm.Spots.Length == 0)
            {
                Fail($"{name}: no vantage points ({g.Nodes.Length} nav nodes)");
                continue;
            }
            foreach (var band in new[] { VantageBand.Long, VantageBand.MediumLong, VantageBand.Medium })
            {
                var spots = vm.Spots.Where(s => s.Score(band) > 0).OrderByDescending(s => s.Score(band)).ToList();
                if (spots.Count == 0)
                {
                    if (band != VantageBand.Medium || name != "Downtown")
                        VNote($"{name}: no {band} spots");
                    continue;
                }
                var best = spots[0];
                if (band == VantageBand.Long)
                    withLong++;
                var street = LowestMainNode(g);
                var ref0 = VantageMap.Evaluate(map, g, street);
                var line = $"{name} {band}: {spots.Count} spots; best at ({best.Pos.X:0},{best.Pos.Y:0.0},{best.Pos.Z:0}) +{best.Height:0.0} m over its surroundings, "
                           + $"{best.Overlook * 100:0}% below it, sight {best.Sight(band):0.0}, cover {best.Cover:0.0}{(best.Roofed ? " roofed" : "")}, score {best.Score(band):0.0}; "
                           + $"street node ({ref0.Pos.X:0},{ref0.Pos.Y:0.0},{ref0.Pos.Z:0}) score {ref0.Score(band):0.0}, sight {ref0.Sight(band):0.0}, cover {ref0.Cover:0.0}";
                VNote(line);
                if (band == VantageBand.Medium)
                    continue; // LMG spots favour cover over height: no height rule
                if (band == VantageBand.Long && (best.Pos.Y < ref0.Pos.Y + 1.5f || (best.Height < 0.8f && best.Overlook < 0.15f)))
                    Fail($"{name} {band}: the best spot isn't elevated (y {best.Pos.Y:0.0} vs street {ref0.Pos.Y:0.0}, +{best.Height:0.0} m, {best.Overlook * 100:0}% below)");
                if (band == VantageBand.MediumLong && best.Height < 0.8f && best.Overlook < 0.12f && best.LowCover == 0)
                    Fail($"{name} {band}: the best spot is neither raised nor behind a parapet (+{best.Height:0.0} m, {best.Overlook * 100:0}% below)");
                if (best.Sight(band) < 2f)
                    Fail($"{name} {band}: the best spot has no real sightlines ({best.Sight(band):0.0})");
                if (best.Cover <= 0f)
                    Fail($"{name} {band}: the best spot is in the open (cover {best.Cover:0.0})");
                if (best.Score(band) <= ref0.Score(band))
                    Fail($"{name} {band}: the street node scores as well as the best spot ({ref0.Score(band):0.0} vs {best.Score(band):0.0})");
            }
            // Spread out: no two kept spots within the minimum spacing on the same level.
            for (var i = 0; i < vm.Spots.Length; i++)
                for (var j = i + 1; j < vm.Spots.Length; j++)
                    if (MathF.Abs(vm.Spots[i].Pos.Y - vm.Spots[j].Pos.Y) < 2.5f && FlatOf(vm.Spots[i].Pos - vm.Spots[j].Pos).Length() < VantageMap.MinSpacing - 0.01f)
                    {
                        Fail($"{name}: vantage spots {i} and {j} are closer than {VantageMap.MinSpacing} m");
                        i = vm.Spots.Length;
                        break;
                    }
            if (vm.Spots.Any(s => g.Component[s.Node] != g.MainComponent || !g.MainNodes.Contains(s.Node)))
                Fail($"{name}: a vantage spot isn't in the main walkable area (unreachable)");
        }
        if (withLong < 3)
            Fail($"only {withLong} of the scoring maps have sniper vantage points (want 3+)");
    }

    // ---- 2. A sniper goes to a spot first, an SMG goes straight in ------------------------------------------------

    /// <summary>Two street-level main-area nodes <paramref name="minD"/>-<paramref name="maxD"/> m apart with a clear line between them (deterministic search).</summary>
    private static bool VDuelNodes(NavGraph g, float minD, float maxD, out Vector3 bot, out Vector3 host)
    {
        bot = host = default;
        var floor = g.Nodes[LowestMainNode(g)].Y;
        var n = g.MainNodes.Length;
        for (var k = 0; k < Math.Min(n, 600); k++)
        {
            var b = g.Nodes[g.MainNodes[(int)(k * 7919L % n)]];
            if (!LowNode(b, floor))
                continue;
            for (var m = 0; m < Math.Min(n, 400); m++)
            {
                var h = g.Nodes[g.MainNodes[(int)((m * 104729L + k) % n)]];
                var d = FlatOf(h - b).Length();
                if (d < minD || d > maxD || !LowNode(h, floor) || !ClearLine(EyeAt(b), Chest(h)))
                    continue;
                bot = b;
                host = h;
                return true;
            }
        }
        return false;
    }

    private static void CheckVantageSniperVsSmg()
    {
        var map = VMap;
        var now = 0.0;
        var (game, blue) = VScenario(map, 7301, GameMode.TeamDeathmatch, ref now, BotPersonality.Marksman, BotPersonality.Rusher);
        using var _ = game;
        var g = NavGraph.Current;
        if (!VDuelNodes(g, 34f, 55f, out var start, out var hostAt) && !VDuelNodes(g, 30f, 60f, out start, out hostAt))
        {
            Fail($"{map.Name}: no street spot 34-55 m from another with a clear line (sniper scenario)");
            return;
        }
        var sniper = blue[0];
        var smg = blue[1];
        PlaceHost(game, hostAt, start);
        VPlace(game, sniper, start, 0);
        var smgAt = FindNode(start, 1.5f, 5f, q => MathF.Abs(q.Y - start.Y) < 0.5f) ?? start + new Vector3(1.5f, 0, 0);
        VPlace(game, smg, smgAt, 0);
        VArm(sniper, VSniper, VPistol);
        VArm(smg, VGun(WeaponClass.SMG, Slot.Primary), VPistol);
        sniper.Bot!.ForceVantage = true;
        var host = game.Players[0];
        int marks0 = game.BotStats.Shots[(int)BotPersonality.Marksman], rush0 = game.BotStats.Shots[(int)BotPersonality.Rusher];
        double travelAt = -1, holdAt = -1, smgEngageAt = -1, smgShotAt = -1;
        var farShots = 0;
        var startDist = FlatOf(smg.Position - hostAt).Length();
        var smgClosest = startDist;
        var t0 = now;
        Tick(game, ref now, 30, () =>
        {
            var sb = sniper.Bot!;
            var shots = game.BotStats.Shots[(int)BotPersonality.Marksman];
            var dist = FlatOf(sniper.Position - host.Position).Length();
            if (sb.VState == VantageState.Travel && travelAt < 0)
                travelAt = now - t0;
            if (sb.VState == VantageState.Hold && holdAt < 0)
                holdAt = now - t0;
            if (holdAt < 0 && shots > marks0 && dist >= 30f)
                farShots += shots - marks0;
            marks0 = shots;
            if (smg.Bot!.Mode == BotMode.Engage && smgEngageAt < 0)
                smgEngageAt = now - t0;
            if (game.BotStats.Shots[(int)BotPersonality.Rusher] > rush0 && smgShotAt < 0)
                smgShotAt = now - t0;
            smgClosest = MathF.Min(smgClosest, FlatOf(smg.Position - host.Position).Length());
            return holdAt > 0 && now - t0 - holdAt > 2 && smgEngageAt > 0;
        });
        var sbr = sniper.Bot!;
        var spot = sbr.VantageOf is { } vm && sbr.VantageSpot >= 0 ? vm.Spots[sbr.VantageSpot] : default;
        VNote($"  sniper at the end: ({sniper.Position.X:0.0},{sniper.Position.Y:0.0},{sniper.Position.Z:0.0}), {sbr.Mode}/{sbr.VState} spot {sbr.VantageSpot} at "
              + $"({spot.Pos.X:0.0},{spot.Pos.Y:0.0},{spot.Pos.Z:0.0}), path {sbr.PathIdx}/{sbr.PathLen}, stuck {sbr.StuckCount}, picks {game.VantagePicks}");
        VNote($"{map.Name} sniper vs SMG at {FlatOf(hostAt - start).Length():0} m: sniper travelling at {travelAt:0.00} s, holding a spot at {holdAt:0.0} s "
              + $"(+{spot.Height:0.0} m, y {spot.Pos.Y:0.0} vs {start.Y:0.0}), {farShots} long shots before that; SMG engaging at {smgEngageAt:0.00} s, "
              + $"first shot {smgShotAt:0.00} s, closed from {startDist:0} to {smgClosest:0} m; {game.VantageArrivals} arrivals");
        if (travelAt < 0 || travelAt > 1.5)
            Fail($"the sniper didn't head for a vantage point straight away (travel at {travelAt:0.00} s)");
        if (holdAt < 0)
            Fail("the sniper never reached its vantage point in 30 s");
        if (farShots > 0)
            Fail($"the sniper fired {farShots} long-range shot(s) on its way to the spot (it should hold fire until set)");
        if (smgEngageAt < 0 || smgEngageAt > 5 || (smgShotAt < 0 && smgClosest > startDist - 5f))
            Fail($"the SMG bot didn't go straight at the target (engage {smgEngageAt:0.00} s, shot {smgShotAt:0.00} s, closed to {smgClosest:0} m)");
        if (holdAt > 0 && smgEngageAt > holdAt)
            Fail("the SMG bot engaged only after the sniper was set");
    }

    // ---- 3. Holding: the stuck flag, then relocation when shot from out of sight ------------------------------------

    private static void CheckVantageHoldAndRelocate()
    {
        var map = VMap;
        var now = 0.0;
        var (game, blue) = VScenario(map, 7302, GameMode.TeamDeathmatch, ref now, BotPersonality.Marksman);
        using var _ = game;
        var vm = VantageMap.For(NavGraph.Current);
        if (vm == null || vm.Spots.Length == 0)
        {
            Fail($"{map.Name}: no vantage points (hold test)");
            return;
        }
        var sniper = blue[0];
        var b = sniper.Bot!;
        var spotIndex = Enumerable.Range(0, vm.Spots.Length).OrderByDescending(i => vm.Spots[i].ScoreLong).First();
        var spot = vm.Spots[spotIndex];
        VPlace(game, sniper, spot.Pos, 0);
        VArm(sniper, VSniper, VPistol);
        b.ForceVantage = true;
        Tick(game, ref now, World.TickDt); // the new life's reset
        game.SelfTestVantageHold(sniper, spotIndex, now);
        b.StuckCount = 0;
        var stuckMax = 0;
        var held = 0;
        var ticks = 0;
        Tick(game, ref now, 2.5, () =>
        {
            ticks++;
            stuckMax = Math.Max(stuckMax, b.StuckCount);
            if (b.IsHoldingPosition)
                held++;
            return false;
        });
        VNote($"holding a spot on {map.Name}: IsHoldingPosition {held}/{ticks} ticks, stuck count max {stuckMax}, still {FlatOf(sniper.Position - spot.Pos).Length():0.0} m from it");
        if (held < ticks * 0.9 || !b.IsHoldingPosition)
            Fail($"a bot holding a vantage point doesn't report IsHoldingPosition ({held}/{ticks} ticks)");
        if (stuckMax > 0)
            Fail($"a bot holding a vantage point counted as stuck ({stuckMax})");
        if (b.VState != VantageState.Hold || b.VantageSpot != spotIndex)
            Fail($"the bot left its spot with nothing happening ({b.VState}, spot {b.VantageSpot})");

        // Shot by someone it can't see (out of the spot's sight, 10-30 m off): the spot is compromised, it moves.
        var eye = EyeAt(spot.Pos);
        var crouchEye = spot.Pos + new Vector3(0, World.EyeOf(Stance.Crouch), 0);
        var hidden = FindNode(spot.Pos, 10f, 30f, q => !ClearLine(eye, Chest(q)) && !ClearLine(eye, EyeAt(q)) && !ClearLine(crouchEye, Chest(q)) && !ClearLine(crouchEye, EyeAt(q)), salt: 3);
        if (hidden is not { } hostAt)
        {
            Fail($"{map.Name}: no hidden spot near the vantage point (relocation test)");
            return;
        }
        PlaceHost(game, hostAt, spot.Pos);
        var relocations = game.VantageRelocations;
        game.Players[0].LastShotTime = now; // the shot is heard (where it came from), the shooter isn't seen
        game.SelfTestDamageWith(game.Players[0], sniper, 15, game.Players[0].Weapon, now);
        var healthAfter = sniper.Health;
        Tick(game, ref now, 0.3);
        var moved = b.VantageSpot != spotIndex;
        VNote($"shot from out of sight at ({hostAt.X:0},{hostAt.Z:0}) (health {healthAfter}, teams {sniper.Team}/{game.Players[0].Team}, phase {game.Phase}): {b.VState}, spot {spotIndex} -> {b.VantageSpot}, compromised {game.SelfTestVantageCompromised(spotIndex, now)}, "
              + $"holding flag {b.IsHoldingPosition}");
        if (game.VantageRelocations == relocations || !moved)
            Fail("shot from a flank at a vantage point, the bot didn't relocate");
        if (!game.SelfTestVantageCompromised(spotIndex, now))
            Fail("the spot a bot was shot at from a flank isn't marked compromised");
        if (b.IsHoldingPosition && b.VState != VantageState.Hold)
            Fail("a relocating bot still reports IsHoldingPosition");
        if (b.VantageSpot >= 0 && FlatOf(vm.Spots[b.VantageSpot].Pos - hostAt).Length() < FlatOf(spot.Pos - hostAt).Length() - 8f)
            Fail("the new spot is much closer to the shooter than the old one");
    }

    // ---- 4. The sidearm up close ----------------------------------------------------------------------------------

    private static void CheckVantageSidearm()
    {
        var map = VMap;
        var now = 0.0;
        var (game, blue) = VScenario(map, 7303, GameMode.TeamDeathmatch, ref now, BotPersonality.Marksman);
        using var _ = game;
        var g = NavGraph.Current;
        if (!VDuelNodes(g, 4.5f, 6.5f, out var start, out var hostAt))
        {
            Fail($"{map.Name}: no pair of street nodes 5-6 m apart in plain view (sidearm test)");
            return;
        }
        var sniper = blue[0];
        VPlace(game, sniper, start, MathF.Atan2(hostAt.X - start.X, -(hostAt.Z - start.Z)));
        VArm(sniper, VSniper, VPistol);
        sniper.Bot!.ForceVantage = true;
        PlaceHost(game, hostAt, start);
        double drawnAt = -1;
        var tDraw = now;
        Tick(game, ref now, 2, () =>
        {
            if (sniper.Weapon == sniper.Secondary && drawnAt < 0)
                drawnAt = now - tDraw;
            return drawnAt > 0;
        });
        var close = FlatOf(sniper.Position - game.Players[0].Position).Length();
        // Gone: far off and out of sight. The rifle comes back out.
        var far = FindNode(start, 30f, 80f, q => !ClearLine(EyeAt(start), Chest(q)) && !ClearLine(sniper.Eye, Chest(q)), salt: 5);
        if (far is { } f)
            PlaceHost(game, f, start);
        else
            KeepHostDead(game);
        double backAt = -1;
        var tBack = now;
        Tick(game, ref now, 5, () =>
        {
            if (sniper.Weapon == sniper.Primary && backAt < 0)
                backAt = now - tBack;
            return backAt >= 0;
        });
        VNote($"sidearm: enemy at {close:0.0} m -> {Weapons.Get(sniper.Secondary).Name} out at {drawnAt:0.00} s; enemy gone -> {Weapons.Get(sniper.Primary).Name} back after {backAt:0.0} s");
        if (drawnAt < 0)
            Fail($"a sniper with an enemy {close:0.0} m away didn't switch to its sidearm");
        if (backAt < 0)
            Fail("the sniper never switched back to its rifle once the enemy was gone");
        if (sniper.Bot!.Ammo < 0 || sniper.Bot.Ammo > VSniper.Mag)
            Fail($"the rifle came back with a strange magazine ({sniper.Bot.Ammo}/{VSniper.Mag})");
    }

    // ---- 5. Two snipers, two spots ---------------------------------------------------------------------------------

    private static void CheckVantageTwoSnipers()
    {
        var map = VMap;
        var now = 0.0;
        var (game, blue) = VScenario(map, 7304, GameMode.TeamDeathmatch, ref now, BotPersonality.Marksman, BotPersonality.Marksman);
        using var _ = game;
        var g = NavGraph.Current;
        var start = g.Nodes[LowestMainNode(g)];
        VPlace(game, blue[0], start, 0);
        VPlace(game, blue[1], FindNode(start, 1.5f, 4f, q => MathF.Abs(q.Y - start.Y) < 0.5f) ?? start + new Vector3(1.5f, 0, 0), 0);
        foreach (var p in blue)
        {
            VArm(p, VSniper, VPistol);
            p.Bot!.ForceVantage = true;
        }
        KeepHostDead(game);
        var tPick = now;
        Tick(game, ref now, 4, () => blue.All(p => p.Bot!.VantageSpot >= 0) && now - tPick > 1.5);
        int a = blue[0].Bot!.VantageSpot, c = blue[1].Bot!.VantageSpot;
        var vm = VantageMap.For(g)!;
        var apart = a >= 0 && c >= 0 ? Vector3.Distance(vm.Spots[a].Pos, vm.Spots[c].Pos) : 0f;
        VNote($"two snipers on {map.Name}: spots {a} and {c}, {apart:0.0} m apart");
        if (a < 0 || c < 0)
            Fail($"two snipers didn't both pick a vantage point ({a}, {c})");
        else if (a == c || apart < VantageMap.MinSpacing - 0.01f)
            Fail($"two snipers took the same vantage point ({a}, {c}, {apart:0.0} m apart)");

        // The decisions allocate nothing (a pick and the decision, many times over).
        for (var i = 0; i < 20; i++)
        {
            game.SelfTestVantagePick(blue[0], now);
            game.SelfTestVantageDecide(blue[1], now);
        }
        // The least of a few identical rounds (SelfTestCpuRatio.cs): a real per-call allocation shows in every round, a one-off runtime allocation in one at most.
        var bytes = SteadyAllocBytes(() =>
        {
            for (var i = 0; i < 400; i++)
            {
                game.SelfTestVantagePick(blue[0], now);
                game.SelfTestVantageDecide(blue[1], now);
            }
        }, warmups: 0);
        VNote($"400 vantage picks and decisions: {bytes} bytes allocated");
        if (bytes > 0)
            Fail($"vantage picks/decisions allocate ({bytes} bytes over 400 calls)");
    }

    // ---- 6. King of the Hill: the spot overlooks the hill -----------------------------------------------------------

    private static void CheckVantageKingOfTheHill()
    {
        var map = VMap;
        var now = 0.0;
        var (game, blue) = VScenario(map, 7305, GameMode.KingOfTheHill, ref now, BotPersonality.Marksman);
        using var _ = game;
        var sniper = blue[0];
        VPlace(game, sniper, NavGraph.Current.Nodes[LowestMainNode(NavGraph.Current)], 0);
        VArm(sniper, VSniper, VPistol);
        sniper.Bot!.ForceVantage = true;
        Tick(game, ref now, World.TickDt * 2);
        var vm = VantageMap.For(NavGraph.Current)!;
        if (game.ActiveHill is not { } hill)
        {
            Fail("King of the Hill: no active hill");
            return;
        }
        var bit = -1;
        var hills = World.Map.Objectives.Hills;
        for (var i = 0; i < hills.Count && i < 12; i++)
            if (FlatOf(hills[i].Center - hill.Center).Length() < 1f)
                bit = i;
        var seeing = bit < 0 ? 0 : vm.Spots.Count(s => (s.Objectives & (1 << bit)) != 0 && s.ScoreLong > 0);
        var pick = game.SelfTestVantagePick(sniper, now);
        var sees = pick >= 0 && ((bit >= 0 && (vm.Spots[pick].Objectives & (1 << bit)) != 0) || ClearLine(EyeAt(vm.Spots[pick].Pos), hill.Center + new Vector3(0, 1.2f, 0)));
        VNote($"King of the Hill on {map.Name}: {seeing} long-range spots overlook hill {bit}; the sniper picked {pick} ({(sees ? "sees the hill" : "doesn't see it")}, "
              + $"{(pick >= 0 ? Vector3.Distance(vm.Spots[pick].Pos, hill.Center) : 0):0} m off)");
        if (seeing > 0 && !sees)
            Fail("King of the Hill: the sniper picked a spot that doesn't overlook the hill (and others do)");
    }

    // ---- 7. Insane squads: the long gun on overwatch -----------------------------------------------------------------

    private static void CheckVantageSquadOverwatch()
    {
        var map = VMap;
        var (game, squad) = SquadScenario(map, 7306);
        using var _ = game;
        var now = 0.0;
        Tick(game, ref now, Game.CountdownSeconds + 0.5); // past the countdown
        KeepHostDead(game);
        var marksman = squad.First(p => p.Bot!.Personality == BotPersonality.Marksman);
        VArm(marksman, VSniper, VPistol);
        var ordered = false;
        var tOrder = now;
        Tick(game, ref now, 5, () =>
        {
            ordered |= marksman.Bot!.Order.Overwatch && marksman.Bot.VantageSpot >= 0;
            return ordered && now - tOrder > 2;
        });
        var b = marksman.Bot!;
        VNote($"Insane squad on {map.Name}: overwatch orders {game.SquadStats.OverwatchOrders}, the marksman ({b.Role}) at spot {b.VantageSpot}, {b.VState}, "
              + $"{(b.VantageSpot >= 0 && b.VantageOf != null ? FlatOf(b.VantageOf.Spots[b.VantageSpot].Pos - marksman.Position).Length() : -1):0.0} m from it");
        if (!ordered || game.SquadStats.OverwatchOrders == 0)
            Fail("an Insane squad's sniper never got an overwatch order");
        if (squad.Count(p => p.Bot!.Order.Overwatch) > 1)
            Fail("more than one squad member on overwatch");
    }

    // ---- 8. The precompute's cost on the heaviest maps ---------------------------------------------------------------

    private static void CheckVantageCost()
    {
        foreach (var name in new[] { "Vuorikoski", "Silo", "Sprawl" })
        {
            var map = Maps.All.FirstOrDefault(m => m.Name == name);
            if (map == null)
            {
                Fail($"vantage cost: no map \"{name}\"");
                continue;
            }
            World.SetMap(map);
            var sw = Stopwatch.StartNew();
            NavGraph.Pump(double.MaxValue);
            var navMs = sw.Elapsed.TotalMilliseconds;
            var g = NavGraph.Current;
            var vm = VantageMap.For(g);
            if (vm == null)
            {
                Fail($"{name}: no vantage points after a full pump");
                continue;
            }
            // Two more builds on this thread, back to back (the cached one may predate props other sections broke): the same
            // spots both times (deterministic), and the time.
            sw.Restart();
            var again = VantageMap.BuildNow(map, g);
            var againMs = sw.Elapsed.TotalMilliseconds;
            var third = VantageMap.BuildNow(map, g);
            var same = again.Spots.Length == third.Spots.Length && again.Spots.Zip(third.Spots).All(z => z.First.Node == z.Second.Node && z.First.ScoreLong == z.Second.ScoreLong);
            VNote($"{name}: {g.Nodes.Length} nav nodes; vantage {vm.Spots.Length} spots from {vm.Candidates} candidates, {vm.InterestPoints} interest points, "
                  + $"{vm.Rays} rays: {vm.BuildMs:0} ms (worker), rebuilt in {againMs:0} ms; pump {navMs:0} ms; long/DMR/LMG spots "
                  + $"{vm.Spots.Count(s => s.ScoreLong > 0)}/{vm.Spots.Count(s => s.ScoreMediumLong > 0)}/{vm.Spots.Count(s => s.ScoreMedium > 0)}");
            if (!same)
                Fail($"{name}: building the vantage points twice gave different spots");
            if (Math.Min(vm.BuildMs, againMs) > VantageBudgetMs)
                Fail($"{name}: the vantage precompute took {Math.Min(vm.BuildMs, againMs):0} ms (budget {VantageBudgetMs:0} ms)");
            if (vm.Spots.Length == 0)
                Fail($"{name}: no vantage points at all");
        }
    }
}
