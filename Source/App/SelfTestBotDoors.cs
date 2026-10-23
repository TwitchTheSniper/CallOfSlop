using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Bot doors" (Source/Game/Bots/Doors/BotDoors.cs): short scenarios at the Small House's front door on the
/// Building Yard. A bot whose route goes through the shut door opens it ahead of time and doesn't stop at it; two teammates
/// both get through (the first leaves it open for the second); two bots head on both get through; a bot alone shuts it
/// behind itself; a bot holding the room shuts the room's door; nobody moves one door more than twice in 10 s; every door
/// change reaches a client copy of the map. Then a co-op enemy rushing through a shut Silo blast-lock door (the user's bug:
/// they used to stand pressed against it), zombies bashing a door down, and no door shutting on an enemy or a zombie.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> DoorNotes = new();

    private static void DoorNote(string s)
    {
        Line(s);
        DoorNotes.Add(s);
    }

    private static void CheckBotDoors()
    {
        DoorNotes.Clear();
        try
        {
            CheckBotDoorScenarios();
            CheckCoopDoor();
            CheckZombieDoors();
        }
        finally
        {
            Line("::warning title=Bot doors (results)::" + string.Join("%0A", DoorNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
            World.SetMap(Maps.All[0]);
        }
    }

    /// <summary>The Building Yard with the Small House's shut wooden front door: a game with no bots yet, the host out of play.</summary>
    private sealed class DoorLab : IDisposable
    {
        public readonly Map Yard;
        public readonly Game Game;
        public readonly int Door;
        public readonly BuildingDoor D;
        public readonly Vector3 Out, In, Across;
        public readonly Map Client; // a client's copy of the map, fed the server's door events
        public double Now;
        public int MaxToggles; // the most any bot moved this door within 10 s
        public int Events;

        public DoorLab(Map yard, GameMode mode)
        {
            Yard = yard;
            yard.RestoreBuildings();
            ClutteredLargeNav(yard);
            Game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            Game.Mode = mode;
            Game.TeamBalance = TeamBalance.Off; // the scenarios pick the teams (autobalance would move a bot over and respawn it)
            Door = FindDoor(yard, "Small House", DoorMaterial.Wood, front: true);
            D = yard.BuildingDoors[Math.Max(0, Door)];
            Out = D.Plan.OtherSide;
            In = D.Plan.SwingSide;
            Across = Vector3.Normalize(new Vector3(In.X - Out.X, 0, In.Z - Out.Z));
            Client = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, yard.SourceFile!)), yard.SourceFile!);
            Game.BuildingEventTap = (kind, i, health) =>
            {
                Events++;
                Game.ApplyBuildingEvent(Client, kind, i, health, Vector3.Zero);
            };
            Step(Game.CountdownSeconds + 0.3); // past the countdown (play starting respawns everyone)
        }

        public Player AddBot(BotPersonality kind, byte team, Vector3 at, Vector3 goal, float? closeChance)
        {
            Game.AddBot(BotDifficulty.Normal, kind, quiet: true);
            var bot = Game.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).Last();
            bot.Team = team;
            bot.TeamChoice = team;
            Game.SelfTestPlace(bot, at, MathF.Atan2(goal.X - at.X, -(goal.Z - at.Z)));
            bot.Bot!.ForcedGoal = goal;
            bot.Bot.SelfTestDoorCloseChance = closeChance;
            return bot;
        }

        /// <summary>Runs the game; <paramref name="each"/> is called every tick (stop early by returning true).</summary>
        public void Step(double seconds, Func<bool>? each = null)
        {
            for (var t = 0.0; t < seconds; t += World.TickDt)
            {
                Now += World.TickDt;
                if (Game.Local is { } host)
                {
                    host.Alive = false; // out of play: nothing to fight
                    host.Health = 0;
                    host.WantsRespawn = false;
                }
                Game.Update(Now, World.TickDt);
                foreach (var p in Game.Players.Values)
                    if (p.Bot is { } b)
                        MaxToggles = Math.Max(MaxToggles, b.DoorTogglesIn(Door, Now, 10));
                if (each?.Invoke() == true)
                    break;
            }
        }

        /// <summary>
        /// A nav node about <paramref name="along"/> m from the door's plane (+ inside) and <paramref name="sideways"/> m off its
        /// axis, with a clear line to the doorway on its side (or that spot itself if there's none).
        /// </summary>
        public Vector3 Spot(float along, float sideways = 0)
        {
            var perp = new Vector3(-Across.Z, 0, Across.X);
            var want = new Vector3(D.Center.X, D.Plan.Closed.Min.Y, D.Center.Z) + Across * along + perp * sideways;
            var door = along > 0 ? In : Out;
            var nav = NavGraph.Current;
            var best = want;
            var bestD = 1.6f;
            for (var k = 0; k < nav.Nodes.Length; k++)
            {
                var q = nav.Nodes[k];
                var dist = FlatDistance(q, want);
                if (dist >= bestD || MathF.Abs(q.Y - want.Y) > 0.6f || MathF.Abs(Vector3.Dot(q - D.Center, Across) - along) > 0.9f || !NavGraph.Clear(q, door))
                    continue;
                best = q;
                bestD = dist;
            }
            return best;
        }

        public float Side(Player p) => Vector3.Dot(p.Position - D.Center, Across);

        public bool ClientMatches => Client.BuildingDoors[Door].Open == D.Open && Client.BuildingDoors[Door].Broken == D.Broken;

        public void Dispose()
        {
            Game.Dispose();
            Yard.RestoreBuildings();
        }
    }

    private static void CheckBotDoorScenarios()
    {
        if (Maps.Find("Building Yard") is not { } yard)
        {
            Fail("bot doors: no Building Yard");
            return;
        }
        var probe = FindDoor(yard, "Small House", DoorMaterial.Wood, front: true);
        if (probe < 0)
        {
            yard.RestoreBuildings();
            probe = FindDoor(yard, "Small House", DoorMaterial.Wood, front: true);
        }
        if (probe < 0)
        {
            Fail("bot doors: no shut front door on the Small House");
            return;
        }
        var spam = 0;
        var clientOk = true;

        // ---- 1. Through a shut door: opened ahead of time, no stopping at it ----
        using (var lab = new DoorLab(yard, GameMode.FreeForAll))
        {
            var bot = lab.AddBot(BotPersonality.Rusher, Game.NoTeam, lab.Spot(-5.5f), lab.Spot(2.2f), 0f);
            var start = lab.Now;
            double openedAt = -1, crossedAt = -1, stopped = 0;
            var openedFrom = 0f;
            var stallAt = float.NaN;
            var stallMode = BotMode.Roam;
            lab.Step(6, () =>
            {
                if (openedAt < 0 && lab.D.Open)
                {
                    openedAt = lab.Now - start;
                    openedFrom = -lab.Side(bot);
                }
                var near = FlatDistance(bot.Position, lab.D.Center) < 3f;
                if (near && crossedAt < 0 && new Vector2(bot.Sim.Velocity.X, bot.Sim.Velocity.Z).Length() < 0.5f)
                {
                    stopped += World.TickDt;
                    if (float.IsNaN(stallAt))
                    {
                        stallAt = lab.Side(bot);
                        stallMode = bot.Bot!.Mode;
                    }
                }
                if (crossedAt < 0 && lab.Side(bot) > 0.5f)
                    crossedAt = lab.Now - start;
                return crossedAt >= 0 && lab.Now - start > crossedAt + 0.5;
            });
            DoorNote($"through a shut door: opened at {openedAt:0.00} s from {openedFrom:0.00} m before the doorway, through at {(crossedAt < 0 ? "NEVER" : $"{crossedAt:0.00} s")}, "
                     + $"stood still {stopped:0.00} s near it (first at {stallAt:0.00} m from the doorway, {stallMode}, stuck count {bot.Bot!.StuckCount}, door waits {bot.Bot.DoorWaitTicks} ticks, mantling {bot.Sim.MantleTicks}, path {bot.Bot.PathIdx}/{bot.Bot.PathLen}, "
                     + $"at {FlatDistance(bot.Position, lab.D.Center):0.00} m from the door); bot opens {lab.Game.BotDoorOpens}");
            if (openedAt < 0 || crossedAt < 0)
                Fail("a bot routed through a shut door didn't open it and get through within 6 s");
            else if (openedFrom < 1.0f)
                Fail($"the bot opened the door only {openedFrom:0.00} m out (it should open it ahead, from about 2 m)");
            if (stopped > 0.5)
                Fail($"the bot stood still {stopped:0.00} s at the door (at most 0.5 s)");
            spam = Math.Max(spam, lab.MaxToggles);
            clientOk &= lab.ClientMatches;
        }

        // ---- 2. Two teammates through one door: both get through, the first leaves it open for the second ----
        using (var lab = new DoorLab(yard, GameMode.TeamDeathmatch))
        {
            var a = lab.AddBot(BotPersonality.Anchor, 0, lab.Spot(-4f, 0.4f), lab.Spot(2.2f, 0.5f), 1f);
            var b = lab.AddBot(BotPersonality.Anchor, 0, lab.Spot(-7f, -0.4f), lab.Spot(2.2f, -0.5f), 1f);
            var start = lab.Now;
            double aAt = -1, bAt = -1;
            var shutOnB = false;
            lab.Step(8, () =>
            {
                if (aAt < 0 && lab.Side(a) > 0.5f)
                    aAt = lab.Now - start;
                if (bAt < 0 && lab.Side(b) > 0.5f)
                    bAt = lab.Now - start;
                if (bAt < 0 && aAt >= 0 && !lab.D.Open)
                    shutOnB = true;
                return aAt >= 0 && bAt >= 0 && lab.Now - start > bAt + 2.5;
            });
            DoorNote($"two teammates, one door: first through at {aAt:0.00} s, second at {bAt:0.00} s (ended {lab.Side(a):0.0} / {lab.Side(b):0.0} m in, {FlatDistance(a.Position, b.Position):0.0} m apart, waits {a.Bot!.DoorWaitTicks}/{b.Bot!.DoorWaitTicks} ticks, door open {lab.D.Open}); shut on the second {shutOnB}; "
                     + $"afterwards open {lab.D.Open} (the last one through, alone, may shut it); opens {lab.Game.BotDoorOpens}, shuts {lab.Game.BotDoorCloses}");
            if (aAt < 0 || bAt < 0 || bAt > 6)
                Fail("two teammates didn't both get through one door within 6 s");
            if (shutOnB)
                Fail("a bot shut the door on a teammate following it through");
            spam = Math.Max(spam, lab.MaxToggles);
            clientOk &= lab.ClientMatches;
        }

        // ---- 3. Head on: one each way through the doorway ----
        using (var lab = new DoorLab(yard, GameMode.TeamDeathmatch))
        {
            lab.Game.ServerSetDoor(lab.Door, true, lab.Now);
            var a = lab.AddBot(BotPersonality.Tactician, 0, lab.Spot(-4.4f), lab.Spot(2.2f), 0f);
            var b = lab.AddBot(BotPersonality.Tactician, 0, lab.Spot(2.2f), lab.Spot(-4.4f), 0f);
            var start = lab.Now;
            double aAt = -1, bAt = -1;
            lab.Step(8, () =>
            {
                if (aAt < 0 && lab.Side(a) > 0.5f)
                    aAt = lab.Now - start;
                if (bAt < 0 && lab.Side(b) < -0.5f)
                    bAt = lab.Now - start;
                return aAt >= 0 && bAt >= 0;
            });
            DoorNote($"head on through an open doorway: in at {aAt:0.00} s, out at {bAt:0.00} s (ended {lab.Side(a):0.0} / {lab.Side(b):0.0} m in, waits {a.Bot!.DoorWaitTicks}/{b.Bot!.DoorWaitTicks} ticks, door open {lab.D.Open}, toggles {a.Bot.DoorToggles}/{b.Bot.DoorToggles})");
            if (aAt < 0 || bAt < 0 || Math.Max(aAt, bAt) > 6)
                Fail("two bots meeting head on in a doorway didn't both get through within 6 s");
            spam = Math.Max(spam, lab.MaxToggles);
            clientOk &= lab.ClientMatches;
        }

        // ---- 4. Alone: shuts the door behind it ----
        using (var lab = new DoorLab(yard, GameMode.TeamDeathmatch))
        {
            var bot = lab.AddBot(BotPersonality.Anchor, 0, lab.Spot(-4.9f), lab.Spot(2.3f), 1f);
            var start = lab.Now;
            double crossedAt = -1, shutAt = -1;
            lab.Step(7, () =>
            {
                if (crossedAt < 0 && lab.Side(bot) > 0.5f)
                    crossedAt = lab.Now - start;
                if (crossedAt >= 0 && shutAt < 0 && !lab.D.Open)
                    shutAt = lab.Now - start;
                return shutAt >= 0 && lab.Now - start > shutAt + 1;
            });
            DoorNote($"alone: through at {crossedAt:0.00} s, shut it behind itself at {(shutAt < 0 ? "NEVER" : $"{shutAt:0.00} s")}; toggles {bot.Bot!.DoorToggles}");
            if (crossedAt < 0 || shutAt < 0 || shutAt - crossedAt > 3)
                Fail("a bot alone (always shutting) didn't shut the door behind it within 3 s of passing");
            if (bot.Bot.DoorToggles > 2)
                Fail($"the bot moved the door {bot.Bot.DoorToggles} times (open and shut is 2)");
            spam = Math.Max(spam, lab.MaxToggles);
            clientOk &= lab.ClientMatches;
        }

        // Personalities: anchors and tacticians shut doors, rushers rarely.
        var chances = new[] { BotPersonality.Anchor, BotPersonality.Tactician, BotPersonality.Marksman, BotPersonality.Flanker, BotPersonality.Chaotic, BotPersonality.Rusher }
            .Select(k => (k, new BotBrain(BotDifficulty.Normal, k).DoorCloseChance)).ToArray();
        DoorNote("shut-behind chance: " + string.Join(", ", chances.Select(c => $"{c.k} {c.Item2:0.00}")));
        if (chances[0].Item2 < 0.7f || chances[1].Item2 < 0.6f || chances[^1].Item2 > 0.15f)
            Fail("anchors and tacticians should mostly shut doors behind them, rushers rarely");

        // ---- 5. Holding the room: shuts its door (walking over to it first) ----
        using (var lab = new DoorLab(yard, GameMode.TeamDeathmatch))
        {
            lab.Game.ServerSetDoor(lab.Door, true, lab.Now - 10);
            var anchor = lab.Spot(3.5f);
            var bot = lab.AddBot(BotPersonality.Anchor, 0, anchor, anchor, null);
            var b = bot.Bot!;
            b.ForcedGoal = null;
            lab.Step(0.1);
            b.Mode = BotMode.Roam;
            b.HoldUntil = lab.Now + 40;
            b.HoldAnchor = anchor;
            b.Waypoint = anchor;
            b.GoalUntil = lab.Now + 40;
            b.ShuffleAt = lab.Now + 100;
            var start = lab.Now;
            double shutAt = -1;
            lab.Step(8, () =>
            {
                if (shutAt < 0 && !lab.D.Open)
                    shutAt = lab.Now - start;
                return shutAt >= 0 && lab.Now - start > shutAt + 2;
            });
            DoorNote($"holding the room {FlatDistance(anchor, lab.D.Center):0.0} m from its open door: shut it at {(shutAt < 0 ? "NEVER" : $"{shutAt:0.00} s")}, "
                     + $"still shut {!lab.D.Open}, back {FlatDistance(bot.Position, anchor):0.0} m from its spot; toggles {b.DoorToggles}");
            if (shutAt < 0 || lab.D.Open)
                Fail("a bot holding a room didn't shut the room's door within 8 s (or opened it again)");
            spam = Math.Max(spam, lab.MaxToggles);
            clientOk &= lab.ClientMatches;
        }

        DoorNote($"door toggles: at most {spam} by one bot on one door within 10 s; a client's copy of the map matched the server's door every time: {clientOk}");
        if (spam > 2)
            Fail($"a bot moved one door {spam} times within 10 s (at most 2)");
        if (!clientOk)
            Fail("the bots' door changes didn't reach a client's copy of the map");
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>
    /// The user's bug: co-op enemies stood against a shut steel door on Silo (the blast locks' and access bunkers' 1.4 m
    /// metal doors, green paint). A rusher with the host beyond a shut door must open it and come through.
    /// </summary>
    private static void CheckCoopDoor()
    {
        if (CoopMap is not { } silo)
        {
            Fail("co-op doors: no Silo");
            return;
        }
        silo.RestoreBuildings();
        var now = 0.0;
        using var g = CoopGame(0, ref now);
        g.PveNoSpawns = true;
        g.ClearEnemies();
        var me = g.Local!;
        var nav = NavGraph.Current;
        // A shut metal door with room on both sides (snapped to nav nodes), nothing else close to the host's spot.
        int door = -1;
        Vector3 from = default, to = default, n = default;
        string building = "";
        var candidates = 0;
        foreach (var b in silo.PlacedBuildings.OrderByDescending(b => b.Def.Name == "Silo Blast Lock").ThenByDescending(b => b.Def.Name == "Silo Access Bunker"))
        {
            for (var i = b.DoorStart; i < b.DoorStart + b.DoorCount && door < 0; i++)
            {
                var d = silo.BuildingDoors[i];
                if (d.Open || d.Broken || d.Plan.Material != DoorMaterial.Metal)
                    continue;
                candidates++;
                var nn = Vector3.Normalize(new Vector3(d.Plan.SwingSide.X - d.Plan.OtherSide.X, 0, d.Plan.SwingSide.Z - d.Plan.OtherSide.Z));
                foreach (var (s, ea, ha) in Orientations())
                {
                    var floor = new Vector3(d.Center.X, d.Plan.Closed.Min.Y, d.Center.Z);
                    if (Snap(floor + nn * s * ea) is not { } a || Snap(floor - nn * s * ha) is not { } h)
                        continue;
                    if (Vector3.Dot(a - floor, nn * s) < 1f || Vector3.Dot(h - floor, nn * s) > -3.4f)
                        continue;
                    if (silo.BuildingDoors.Any(o => o != d && FlatDistance(o.Center, h) < 2.5f) || silo.BuildingDoors.Any(o => o != d && WalkThrough(o, a, h)))
                        continue;
                    door = i;
                    from = a;
                    to = h;
                    n = nn * s;
                    building = b.Def.Name;
                    break;
                }
            }
            if (door >= 0)
                break;
        }
        if (door < 0)
        {
            Fail($"co-op doors: none of Silo's {candidates} shut metal doors has room either side for the scenario");
            return;
        }
        var dd = silo.BuildingDoors[door];
        g.SelfTestPlace(me, to, 0);
        var e = g.SpawnEnemy(PveArch.Rusher, from + new Vector3(0, 0.05f, 0), now)!;
        e.Target = me.Id;
        e.LastSawTarget = now;
        e.LastSeen = me.Position;
        float Side() => Vector3.Dot(e.Position - dd.Center, -n);
        var start = now;
        double openedAt = -1, throughAt = -1;
        for (var t = 0.0; t < 8 && throughAt < 0; t += World.TickDt)
        {
            me.Health = World.MaxHealth;
            me.ImmuneUntil = now + 100;
            now += World.TickDt;
            g.SelfTestCoopTick(now);
            if (openedAt < 0 && dd.Open)
                openedAt = now - start;
            if (Side() > 0.4f)
                throughAt = now - start;
        }
        DoorNote($"co-op: a rusher behind the shut {dd.Plan.Width:0.0} m {dd.Plan.Material.ToString().ToLowerInvariant()} door of a {building} (door {door}), the host beyond: "
                 + $"opened at {(openedAt < 0 ? "NEVER" : $"{openedAt:0.00} s")}, through at {(throughAt < 0 ? $"NEVER (ended {Side():0.00} m past)" : $"{throughAt:0.00} s")}; "
                 + $"enemy door opens {g.PveDoorOpens}");
        if (openedAt < 0 || throughAt < 0 || throughAt > 6)
            Fail("co-op: an enemy behind a shut door didn't open it and come through within 6 s");

        // A door never shuts on a co-op enemy standing in the doorway.
        e.Sim.Position = new Vector3(dd.Center.X, dd.Plan.Closed.Min.Y + 0.02f, dd.Center.Z);
        var shut = g.ServerSetDoor(door, false, now);
        DoorNote($"shutting the door on an enemy in the doorway: {shut} (refused)");
        if (shut)
            Fail("a door shut on a co-op enemy standing in the doorway");
        g.ClearEnemies();
        silo.RestoreBuildings();
        return;

        Vector3? Snap(Vector3 p)
        {
            var k = nav.Nearest(p, needSight: false);
            if (k < 0)
                return null;
            var q = nav.Nodes[k];
            return FlatDistance(q, p) < 1.3f && MathF.Abs(q.Y - p.Y) < 0.6f ? q : null;
        }

        static IEnumerable<(float Side, float Enemy, float Host)> Orientations()
        {
            foreach (var host in new[] { 4.5f, 4f, 5.5f, 3.7f })
            foreach (var enemy in new[] { 1.8f, 2.4f })
            foreach (var side in new[] { 1f, -1f })
                yield return (side, enemy, host);
        }

        static bool WalkThrough(BuildingDoor o, Vector3 a, Vector3 b) => Game.WalkCrossesDoor(o, a, b, 0.1f);
    }

    /// <summary>Zombies swipe a shut building door down; a door never shuts on a zombie.</summary>
    private static void CheckZombieDoors()
    {
        if (Maps.Find("Building Yard") is not { } yard)
            return;
        yard.RestoreBuildings();
        World.SetMap(yard);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id);
        var i = FindDoor(yard, "Small House", DoorMaterial.Wood, front: true);
        if (i < 0)
        {
            Fail("zombie doors: no shut front door on the Small House");
            return;
        }
        var d = yard.BuildingDoors[i];
        var z = new Zombie { Id = 1, State = ZState.Chase, Health = 100, MaxHealth = 100 };
        z.Sim.Position = d.Plan.OtherSide + Vector3.Normalize(d.Plan.SwingSide - d.Plan.OtherSide) * 0.45f;
        z.Sim.Grounded = true;
        game.Horde.Add(z);
        var now = 0.0;
        var swipes = 0;
        while (!d.Broken && swipes < 40)
        {
            now += 0.1;
            var before = game.ZombieDoorSwipes;
            game.ZombieBashDoors(z, now);
            swipes += game.ZombieDoorSwipes - before;
        }
        var bashTime = now;
        var want = (d.Plan.Health + Game.ZombieDoorDamage - 1) / Game.ZombieDoorDamage;
        // A door doesn't shut on a zombie in the doorway (another door: this one is broken now).
        var k = FindDoor(yard, "Two-Storey House", DoorMaterial.Wood);
        var shutOnZombie = false;
        if (k >= 0)
        {
            var dk = yard.BuildingDoors[k];
            game.ServerSetDoor(k, true, now);
            z.Sim.Position = new Vector3(dk.Center.X, dk.Plan.Closed.Min.Y + 0.02f, dk.Center.Z);
            shutOnZombie = game.ServerSetDoor(k, false, now);
        }
        game.Horde.Clear();
        DoorNote($"zombies: {swipes} swipes ({bashTime:0.0} s) break a {d.Plan.Health} hp wooden door (expected {want}); shutting a door on a zombie in the doorway: {shutOnZombie} (refused)");
        if (!d.Broken || swipes != want)
            Fail($"a zombie pressed against a {d.Plan.Health} hp door broke it in {swipes} swipes (expected {want})");
        if (shutOnZombie)
            Fail("a door shut on a zombie standing in the doorway");
        yard.RestoreBuildings();
    }
}
