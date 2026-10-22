using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Bots and foliage" (FoliageCover.cs, MapConcealment.cs, BotFoliage.cs): bots can't see through
/// foliage, by stance (someone lying in tall grass is hidden at 15 m, someone standing in it isn't), a hedge of bushes
/// hides a crouched player, tree canopies hide what's behind them but not what's under them, everyone is seen within
/// 2.5 m, walls still block and blind fire never goes through them, the cornfield concealment still works; blind fire goes
/// at the last known spot (never the real one), wide, and stops when the information goes stale; and the costs: per
/// query, no allocation, the grid build on the biggest foliage maps, and 31 bots on Sprawl (23 on Vuorikoski, its limit) for a few seconds.
/// Every run here is short (seconds), per the no-long-matches rule.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> BotFoliageNotes = new();

    private static void BfNote(string s)
    {
        Line(s);
        BotFoliageNotes.Add(s);
    }

    /// <summary>
    /// A flat lab: a strip of tall grass (z -30 to -4), a hedge of big bushes along z = 10 (x -6 to 6), a broadleaf tree,
    /// a 3 m wall along x = 20 and a 9 x 9 m cornfield (no plants near it, so the corn is tested alone).
    /// </summary>
    private const string FoliageSightLabJson = """
        { "name": "Foliage Sight Lab", "halfX": 40, "halfZ": 40,
          "ops": [
            { "op": "Ground", "color": [0.34, 0.42, 0.22] },
            { "op": "Block", "x0": 20, "y0": 0, "z0": -20, "x1": 21, "y1": 3, "z1": 20, "color": [0.55, 0.53, 0.5] },
            { "op": "Prop", "name": "Corn Patch A", "x": -30, "z": -30 }, { "op": "Prop", "name": "Corn Patch A", "x": -27, "z": -30 }, { "op": "Prop", "name": "Corn Patch A", "x": -24, "z": -30 },
            { "op": "Prop", "name": "Corn Patch A", "x": -30, "z": -27 }, { "op": "Prop", "name": "Corn Patch A", "x": -27, "z": -27 }, { "op": "Prop", "name": "Corn Patch A", "x": -24, "z": -27 },
            { "op": "Prop", "name": "Corn Patch A", "x": -30, "z": -24 }, { "op": "Prop", "name": "Corn Patch A", "x": -27, "z": -24 }, { "op": "Prop", "name": "Corn Patch A", "x": -24, "z": -24 },
            { "op": "Spawn", "x": 35, "z": 35 }, { "op": "Spawn", "x": -35, "z": 35 }, { "op": "Spawn", "x": 35, "z": -35 }, { "op": "Spawn", "x": 30, "z": 35 }
          ],
          "foliage": {
            "seed": 11,
            "regions": [ { "type": "grass", "density": 8, "rect": [-12, -30, 12, -4], "scale": [1.1, 1.3], "patchiness": 0 } ],
            "items": [
              { "type": "bush", "x": -6.0, "z": 10, "scale": 1.6 }, { "type": "bush", "x": -5.2, "z": 10.2, "scale": 1.6 }, { "type": "bush", "x": -4.4, "z": 9.8, "scale": 1.6 },
              { "type": "bush", "x": -3.6, "z": 10, "scale": 1.6 }, { "type": "bush", "x": -2.8, "z": 10.2, "scale": 1.6 }, { "type": "bush", "x": -2.0, "z": 9.8, "scale": 1.6 },
              { "type": "bush", "x": -1.2, "z": 10, "scale": 1.6 }, { "type": "bush", "x": -0.4, "z": 10.2, "scale": 1.6 }, { "type": "bush", "x": 0.4, "z": 9.8, "scale": 1.6 },
              { "type": "bush", "x": 1.2, "z": 10, "scale": 1.6 }, { "type": "bush", "x": 2.0, "z": 10.2, "scale": 1.6 }, { "type": "bush", "x": 2.8, "z": 9.8, "scale": 1.6 },
              { "type": "bush", "x": 3.6, "z": 10, "scale": 1.6 }, { "type": "bush", "x": 4.4, "z": 10.2, "scale": 1.6 }, { "type": "bush", "x": 5.2, "z": 9.8, "scale": 1.6 },
              { "type": "bush", "x": 6.0, "z": 10, "scale": 1.6 },
              { "type": "tree", "x": -25, "z": 15, "height": 9 }
            ]
          }
        }
        """;

    private static void CheckBotFoliage()
    {
        BotFoliageNotes.Clear();
        try
        {
            var lab = MapFile.Load(FoliageSightLabJson, "foliage-sight-lab.json");
            BotFoliageMaths(lab);
            BotFoliageCosts();
            BotFoliageScenarios(lab);
            BotFoliageSquad(lab);
            BotFoliageCpu();
        }
        finally
        {
            Map.FoliageCoverDisabled = false;
            World.SetMap(Maps.All[0]);
            if (BotFoliageNotes.Count > 0)
                Line("::warning title=Bots and foliage (results)::" + string.Join("%0A", BotFoliageNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static Vector3 BfChest(Vector3 feet, Stance s) => feet + new Vector3(0, World.HeightOf(s) * 0.65f, 0);

    private static Vector3 BfHead(Vector3 feet, Stance s) => feet + new Vector3(0, World.EyeOf(s) + 0.05f, 0);

    /// <summary>The bots' rule without walls: the chest, or else the head when only cover hides the chest.</summary>
    private static bool BfSees(Map m, Vector3 eye, Vector3 feet, Stance s) => !m.Concealed(eye, BfChest(feet, s)) || !m.Concealed(eye, BfHead(feet, s));

    // ---- 1. The sight rules on the lab (pure queries) ----------------------------------------------------------------

    private static void BotFoliageMaths(Map m)
    {
        var g = m.FoliageCoverGrid();
        if (g == null)
        {
            Fail("foliage sight lab: no foliage cover grid");
            return;
        }
        BfNote($"lab: {m.FoliageInstances.Length} plants -> {g.Nx} x {g.Nz} cells ({g.CoveredCells} covered), built in {g.BuildMs:0.0} ms; {m.ConcealingProps} corn patches");
        var eye = new Vector3(0, World.EyeHeight, 6); // a standing bot on open ground, 15 m from someone 5 m into the grass
        var inGrass = new Vector3(0, 0, -9);
        bool prone = BfSees(m, eye, inGrass, Stance.Prone), crouch = BfSees(m, eye, inGrass, Stance.Crouch), stand = BfSees(m, eye, inGrass, Stance.Stand);
        var proneEye = new Vector3(0, World.EyeOf(Stance.Prone), 6);
        var proneProne = BfSees(m, proneEye + new Vector3(0, 0, -12), inGrass + new Vector3(0, 0, -6), Stance.Prone); // both lying in the grass, 9 m apart
        var depth = m.FoliageDepth(eye, BfChest(inGrass, Stance.Prone));
        BfNote($"tall grass at 15 m: prone seen {prone} ({depth:0.0} m of cover to the chest), crouched seen {crouch}, standing seen {stand}; prone to prone 9 m apart seen {proneProne}");
        if (prone || !crouch || !stand || proneProne)
            Fail("tall grass: a bot should see a standing or crouched player's head above it, and not someone lying in it");

        // Up close everyone is seen.
        var close = BfSees(m, new Vector3(0, World.EyeHeight, -7), inGrass, Stance.Prone);
        // A hedge of bushes 1.4 m tall: a crouched player behind it is hidden; a standing one's head shows; off its end, seen.
        var far = new Vector3(0, World.EyeHeight, 26);
        var behind = new Vector3(0, 0, 8.4f);
        bool hedgeCrouch = BfSees(m, far, behind, Stance.Crouch), hedgeStand = BfSees(m, far, behind, Stance.Stand);
        var pastEnd = BfSees(m, far, new Vector3(11, 0, 8.4f), Stance.Crouch);
        BfNote($"within {Map.ConcealSeeRange} m: prone in the grass seen {close}; hedge of bushes: crouched behind it seen {hedgeCrouch}, standing behind it seen {hedgeStand}, crouched past its end seen {pastEnd}");
        if (!close)
            Fail($"foliage hides someone within {Map.ConcealSeeRange} m");
        if (hedgeCrouch || !hedgeStand || !pastEnd)
            Fail("bushes: a crouched player behind a hedge should be hidden, a standing one's head and someone past the hedge seen");

        // Tree canopy: hides a line through the leaves, not one under them.
        bool throughCrown = m.Concealed(new Vector3(-37, 6, 15), new Vector3(-13, 6, 15)), underCrown = m.Concealed(new Vector3(-37, 1.5f, 16.2f), new Vector3(-13, 1.5f, 16.2f));
        BfNote($"tree canopy: a line through the crown hidden {throughCrown}, under it hidden {underCrown}");
        if (!throughCrown || underCrown)
            Fail("tree canopies: a line through the leaves should be hidden, one under them not");

        // The corn still conceals on its own (no plants near it), and the open ground beside it doesn't.
        bool corn = m.Concealed(new Vector3(-27, World.EyeHeight, -18), new Vector3(-27, 1.2f, -28)), open = m.Concealed(new Vector3(-36, World.EyeHeight, -18), new Vector3(-36, 1.2f, -34));
        BfNote($"cornfield: into it hidden {corn}, beside it hidden {open}");
        if (!corn || open)
            Fail("cornfield concealment changed (into the corn should hide, open ground beside it not)");

        // Walls: through the wall is blocked for the game's sight line (and no cover there).
        World.SetMap(m);
        using var game = ModeGame(0, GameMode.FreeForAll);
        World.SetMap(m);
        var wall = game.SelfTestSightLine(new Vector3(16, World.EyeHeight, 0), new Vector3(25, 1.2f, 0));
        var hedgeLine = game.SelfTestSightLine(far, BfChest(behind, Stance.Crouch));
        var openLine = game.SelfTestSightLine(far, new Vector3(11, 1.2f, 8.4f));
        BfNote($"sight lines: through the wall {wall}, into the hedge {hedgeLine}, beside it {openLine}");
        if (wall != BotSightLine.Blocked || hedgeLine != BotSightLine.Covered || openLine != BotSightLine.Clear)
            Fail("SightLine: walls should block, foliage cover, open ground be clear");
    }

    // ---- 2. Costs: per query, allocation, the grid build on heavy maps ------------------------------------------------

    private static void BotFoliageCosts()
    {
        foreach (var name in new[] { "Vuorikoski", "Sprawl", "Bocage" })
        {
            if (Maps.Find(name) is not { } m || m.FoliageInstances.Length == 0)
            {
                BfNote($"{name}: not found or no plants (skipped)");
                continue;
            }
            var g = m.FoliageCoverGrid()!;
            var kb = g.Cells.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<Map.CoverCell>() / 1024;
            // Sight lines between random spots 5-60 m apart, at standing and prone heights.
            var rng = new Random(77);
            var lines = new (Vector3 A, Vector3 B)[2000];
            for (var i = 0; i < lines.Length; i++)
            {
                var ax = g.X0 + (float)rng.NextDouble() * g.Nx;
                var az = g.Z0 + (float)rng.NextDouble() * g.Nz;
                var ang = (float)rng.NextDouble() * MathF.Tau;
                var len = 5f + (float)rng.NextDouble() * 55f;
                float bx = ax + MathF.Cos(ang) * len, bz = az + MathF.Sin(ang) * len;
                var low = i % 3 == 0;
                lines[i] = (new Vector3(ax, m.GroundHeight(ax, az) + (low ? 0.45f : 1.6f), az), new Vector3(bx, m.GroundHeight(bx, bz) + (low ? 0.4f : 1.2f), bz));
            }
            var hidden = 0;
            foreach (var (a, b) in lines)
                if (m.Concealed(a, b))
                    hidden++;
            var sw = new Stopwatch();
            var before = GC.GetAllocatedBytesForCurrentThread();
            sw.Start();
            hidden = 0;
            for (var r = 0; r < 5; r++)
                foreach (var (a, b) in lines)
                    if (m.Concealed(a, b))
                        hidden++;
            var us = sw.Elapsed.TotalMilliseconds * 1000 / (lines.Length * 5);
            var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            BfNote($"{m.Name}: {m.FoliageInstances.Length} plants -> {g.Nx} x {g.Nz} cells ({g.CoveredCells} covered, {kb} KB), built in {g.BuildMs:0.0} ms on a worker; "
                   + $"a sight line costs {us:0.00} us ({hidden / 5} of {lines.Length} hidden), {alloc} bytes allocated over {lines.Length * 5} queries");
            if (alloc > 0)
                Fail($"{m.Name}: foliage sight queries allocate ({alloc} bytes)");
            if (us > 20)
                Fail($"{m.Name}: a foliage sight query takes {us:0.0} us (bots ask hundreds a second; budget 20 us)");
            if (g.BuildMs > 1500)
                Fail($"{m.Name}: building the foliage cover grid took {g.BuildMs:0} ms (budget 1500 ms on the worker)");
            // Deterministic: building it again gives the same cells.
            var again = Map.FoliageCover.Build(m.FoliageInstances);
            if (!System.Runtime.InteropServices.MemoryMarshal.AsBytes(again.Cells.AsSpan()).SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(g.Cells.AsSpan())))
                Fail($"{m.Name}: the foliage cover grid isn't the same when built twice");
        }
    }

    // ---- 3. Bots in a short scenario on the lab: sight by stance, blind fire, timeout, no wallhack -------------------

    private static void BotFoliageScenarios(Map lab)
    {
        World.SetMap(lab);
        using var game = ModeGame(1, GameMode.FreeForAll);
        World.SetMap(lab);
        NavGraph.Pump(double.MaxValue);
        var host = game.Players[0];
        var bot = game.Players.Values.First(p => p.Bot != null);
        var b = bot.Bot!;
        b.SelfTestHold = true; // it would walk over to investigate (and, pinned, think it's stuck)
        var now = 0.0;
        var botAt = new Vector3(0, 0, 6);
        game.SelfTestPlace(host, new Vector3(30, 0, 30), 0);
        game.SelfTestPlace(bot, new Vector3(30, 0, 34), 0);
        host.ImmuneUntil = 1e9;
        Tick(game, ref now, Game.CountdownSeconds + 1.0); // past the countdown
        bot.GrenadesLeft = 0;
        bot.EquipmentLeft = 0;

        // Run, keeping the bot pinned at its spot (it would walk over to investigate) and the host at its spot and stance.
        Vector3 hostAt = default;
        var aims = new List<Vector3>();
        static void Pin(Player p, Vector3 at) // back to its spot, keeping its stance (Teleport would stand it up)
        {
            p.Sim.Position = at;
            p.Sim.Velocity = Vector3.Zero;
            p.PrevPosition = at;
        }
        void Run(double seconds, Vector3 pin)
        {
            for (var t = 0.0; t < seconds; t += World.TickDt)
            {
                var shots = b.BlindShots;
                now += World.TickDt;
                game.Update(now, World.TickDt);
                Pin(bot, pin);
                Pin(host, hostAt);
                host.Health = World.MaxHealth;
                bot.Health = World.MaxHealth;
                if (b.BlindShots > shots)
                    aims.Add(b.LastBlindAim);
            }
        }
        void Start(Vector3 hostFeet, Buttons stance, Vector3 botFeet)
        {
            hostAt = hostFeet;
            game.HeldButtons = stance;
            game.SelfTestPlace(host, hostFeet, MathF.PI); // facing the bot
            host.Sim.Stance = (stance & Buttons.Prone) != 0 ? Stance.Prone : (stance & Buttons.Crouch) != 0 ? Stance.Crouch : Stance.Stand;
            host.ImmuneUntil = 1e9;
            b.Target = byte.MaxValue;
            var to = hostFeet - botFeet;
            game.SelfTestPlace(bot, botFeet, MathF.Atan2(to.X, -to.Z));
            bot.GrenadesLeft = 0;
            bot.EquipmentLeft = 0;
            aims.Clear();
        }

        // a) Standing in the tall grass 15 m away: seen (and shot at, aimed). Then lying down: hidden.
        var spotA = new Vector3(0, 0, -9);
        Start(spotA, Buttons.None, botAt);
        Run(1.5, botAt);
        var sawStanding = now - b.LastSeenTime < 0.2 && b.Target == host.Id;
        game.HeldButtons = Buttons.Prone;
        host.Sim.Stance = Stance.Prone;
        b.Ammo = bot.WeaponDef.Mag; // a full magazine for what follows (the aimed fight may have emptied it)
        b.ReloadUntil = 0;
        Run(0.3, botAt);
        var lineProne = game.SelfTestBotSees(bot, host);
        var shotsBefore = b.BlindShots;
        var lostProne = now - b.LastSeenTime > 0.2;
        BfNote($"bot vs host in tall grass at 15 m: standing seen {sawStanding}; prone {host.Sim.Stance}: line {lineProne}, out of sight {lostProne}");
        if (!sawStanding)
            Fail("a bot didn't see a player standing in knee-high grass 15 m away");
        if (host.Sim.Stance != Stance.Prone || lineProne != BotSightLine.Covered || !lostProne)
            Fail("a bot still sees a player lying in tall grass 15 m away");

        // b) Blind fire: the host crawls 9 m sideways to B (teleported: no new information). The bot fires around A, never at B.
        var spotB = new Vector3(9, 0, -12);
        hostAt = spotB;
        Pin(host, spotB);
        var lastSeen = b.LastSeenAt;
        aims.Clear();
        Run(Game.BlindMemory + 1.5, botAt);
        var lastInfo = b.LastSeenTime;
        var maxErr = 0.4f + Game.BlindErrGrowth * (float)Game.BlindMemory + 0.01f;
        var nearA = aims.Count == 0 ? 999 : aims.Max(a => FlatOf(a - lastSeen).Length());
        var nearB = aims.Count == 0 ? 999 : aims.Min(a => FlatOf(a - spotB).Length());
        var heights = aims.Count == 0 ? "-" : $"{aims.Min(a => a.Y):0.00}-{aims.Max(a => a.Y):0.00}";
        var stopAfter = lastInfo + Game.BlindMemory + 0.05;
        BfNote($"blind fire with {bot.WeaponDef.Name} ({bot.WeaponDef.Mag} rounds): {aims.Count} shots in {Game.BlindMemory + 1.5:0.0} s ({b.BlindShots - shotsBefore} incl. before), aim points up to {nearA:0.0} m from the last known spot "
               + $"(max error {maxErr:0.0} m), nearest {nearB:0.0} m from where the host really is, heights {heights} m; bursts {game.BotStats.BlindBursts}");
        if (aims.Count == 0)
            Fail("a bot didn't fire blind into the grass where it last saw someone");
        if (nearA > maxErr)
            Fail($"blind fire went {nearA:0.0} m from the last known spot (allowed {maxErr:0.0} m)");
        if (nearB < 2f)
            Fail($"blind fire went {nearB:0.0} m from the hidden player's real position (it must not know it)");
        if (aims.Any(a => a.Y > spotA.Y + 1.2f))
            Fail("blind fire aimed above body height (no head shots blind)");
        // c) It stops a few seconds after the information goes stale (and the target never came back into view).
        var shotsNow = b.BlindShots;
        Run(2.0, botAt);
        var lateShots = b.BlindShots - shotsNow;
        BfNote($"after {Game.BlindMemory:0} s without news: {lateShots} more blind shots (last info {now - lastInfo:0.0} s ago); target seen again {now - b.LastSeenTime < 0.5}");
        if (lateShots > 0 || now < stopAfter)
            Fail($"blind fire didn't stop {Game.BlindMemory:0} s after the bot last knew anything ({lateShots} shots later)");

        // d) A hedge between them: crouched behind it hidden, no blind fire without information; up close, seen.
        Start(new Vector3(0, 0, 8.4f), Buttons.Crouch, new Vector3(0, 0, 26));
        Run(1.5, new Vector3(0, 0, 26));
        var hedgeSeen = now - b.LastSeenTime < 0.5;
        var hedgeShots = aims.Count;
        Start(new Vector3(0, 0, -9), Buttons.Prone, new Vector3(0, 0, -6.9f));
        Run(1.0, new Vector3(0, 0, -6.9f));
        var closeSeen = now - b.LastSeenTime < 0.3;
        BfNote($"hedge: crouched behind it seen {hedgeSeen}, blind shots with no information {hedgeShots}; prone in the grass 2.1 m from the bot seen {closeSeen}");
        if (hedgeSeen || hedgeShots > 0)
            Fail("a bot saw (or fired at) a player crouched behind a hedge of bushes that it never saw");
        if (!closeSeen)
            Fail($"a bot didn't see a player lying in grass {Map.ConcealSeeRange} m away or closer");

        // e) No wallhack: behind the wall, never a target; a stale "last seen" there gives no blind fire (no cover, a wall).
        Start(new Vector3(25, 0, 0), Buttons.None, new Vector3(16, 0, 0));
        Run(0.2, new Vector3(16, 0, 0));
        b.LastSeenAt = new Vector3(25, 0, 0);
        b.LastSeenTime = now;
        var wallShots = b.BlindShots;
        var targeted = false;
        for (var k = 0; k < 90; k++)
        {
            Run(World.TickDt, new Vector3(16, 0, 0));
            targeted |= b.Target == host.Id;
        }
        BfNote($"wall: target through it {targeted}, blind shots at a spot behind it {b.BlindShots - wallShots}");
        if (targeted || b.BlindShots != wallShots)
            Fail("a bot saw or fired blind through a wall");
        game.HeldButtons = Buttons.None;
    }

    // ---- 3b. Insane squads: a squadmate's sighting gives the others an estimate, still not aimed fire ---------------------

    private static void BotFoliageSquad(Map lab)
    {
        World.SetMap(lab);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) { MapVoteEnabled = false };
        game.Mode = GameMode.TeamDeathmatch;
        World.SetMap(lab);
        NavGraph.Pump(double.MaxValue);
        game.AddBot(BotDifficulty.Insane, BotPersonality.Tactician, quiet: true);
        game.AddBot(BotDifficulty.Insane, BotPersonality.Flanker, quiet: true);
        game.AddBot(BotDifficulty.Normal, BotPersonality.Anchor, quiet: true);
        var bots = game.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).ToList();
        var host = game.Players[0];
        foreach (var p in bots.Take(2))
        {
            p.Team = 0;
            game.SelfTestSpawn(p);
        }
        bots[2].Team = 1; // kept dead, so the teams count as even
        bots[2].Alive = false;
        bots[2].RespawnAt = 1e9;
        host.Team = 1;
        var now = 0.0;
        // The spotter sees the host from beside the hedge's end; the other looks at the hedge from the far side.
        var hostAt = new Vector3(0, 0, 8.4f);
        Vector3 spotterAt = new(11, 0, 8.4f), blindAt = new(0, 0, 26);
        void Place()
        {
            game.SelfTestPlace(host, hostAt, MathF.PI);
            host.Sim.Stance = Stance.Crouch;
            host.ImmuneUntil = 1e9;
            game.SelfTestPlace(bots[0], spotterAt, -MathF.PI / 2);
            game.SelfTestPlace(bots[1], blindAt, 0);
            foreach (var p in bots.Take(2))
            {
                p.Bot!.SelfTestHold = true;
                p.GrenadesLeft = 0;
                p.EquipmentLeft = 0;
            }
        }
        Place();
        Tick(game, ref now, Game.CountdownSeconds + 1.0);
        Place();
        game.HeldButtons = Buttons.Crouch;
        var squad = game.Squads.FirstOrDefault(q => q.Team == 0);
        if (squad == null || squad.Members.Count != 2)
        {
            BfNote($"squad: the two Insane bots formed no squad on the lab ({game.Squads.Count} squads; skipped)");
            game.HeldButtons = Buttons.None;
            return;
        }
        var spotter = bots[0];
        var blind = bots[1].Bot!;
        var aims = new List<Vector3>();
        var blindSaw = false;
        var spotterSaw = false;
        for (var t = 0.0; t < 4.0; t += World.TickDt)
        {
            var shots = blind.BlindShots;
            now += World.TickDt;
            game.Update(now, World.TickDt);
            host.Sim.Position = hostAt;
            host.Health = World.MaxHealth;
            foreach (var p in bots.Take(2))
                p.Health = World.MaxHealth;
            blindSaw |= game.SelfTestBotSees(bots[1], host) == BotSightLine.Clear;
            spotterSaw |= now - spotter.Bot!.LastSeenTime < 0.05;
            if (blind.BlindShots > shots)
                aims.Add(blind.LastBlindAim);
        }
        var maxOff = aims.Count == 0 ? 0 : aims.Max(a => FlatOf(a - hostAt).Length());
        BfNote($"squad ({string.Join(", ", squad.Members.Select(m => m.Bot!.Role))}): the spotter saw the host {spotterSaw}, the other never saw it {!blindSaw}; "
               + $"it fired {blind.BlindShots} blind shots on the call ({aims.Count} from bursts, up to {maxOff:0.0} m off the real spot), mode {blind.Mode}/{blind.Order.Kind}");
        if (blindSaw)
            Fail("squad test: the hedge didn't hide the host from the second squad member");
        else if (spotterSaw && blind.BlindShots == 0)
            Fail("an Insane squadmate didn't fire blind into the hedge at the spot its squadmate called");
        game.HeldButtons = Buttons.None;
    }

    // ---- 4. CPU: 31 bots on a heavy foliage map for a few seconds ----------------------------------------------------

    private static void BotFoliageCpu()
    {
        foreach (var name in new[] { "Sprawl", "Vuorikoski" })
        {
            var map = Maps.Find(name);
            if (map == null || map.FoliageInstances.Length == 0)
            {
                BfNote($"CPU: {name} not found or has no plants (skipped)");
                continue;
            }
            var medians = new double[2];
            var stats = new string[2];
            var bots = 0;
            for (var pass = 0; pass < 2; pass++)
            {
                Map.FoliageCoverDisabled = pass == 1; // the same few seconds without foliage cover, for comparison
                Game.SeedNextGame = 4242;
                World.SetMap(map);
                using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                    bots: Math.Min(31, map.MaxPlayers - 1), botSkill: BotDifficulty.Hard, mapId: map.Id) { MapVoteEnabled = false };
                game.Mode = GameMode.TeamDeathmatch;
                NavGraph.Pump(double.MaxValue);
                Game.SettleMemory();
                var now = 0.0;
                var wall = new List<double>();
                var warm = (int)(Game.CountdownSeconds * World.TickRate) + World.TickRate;
                for (var i = 0; i < warm + 6 * World.TickRate; i++)
                {
                    now += World.TickDt;
                    var t0 = Stopwatch.GetTimestamp();
                    game.Update(now, World.TickDt);
                    if (i >= warm)
                        wall.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                }
                wall.Sort();
                medians[pass] = wall[wall.Count / 2];
                bots = game.BotCount;
                var st = game.BotStats;
                stats[pass] = $"tick median {medians[pass]:0.00} ms, p99 {wall[(int)(wall.Count * 0.99)]:0.00} ms, gun kills {st.GunKills}, blind shots {st.BlindShots} ({st.BlindBursts} bursts), footstep cues {st.FootstepCues}";
            }
            Map.FoliageCoverDisabled = false;
            BfNote($"CPU {bots} Hard bots on {map.Name} for 6 s: foliage cover on: {stats[0]}; off: {stats[1]}; difference {medians[0] - medians[1]:+0.00;-0.00} ms per tick");
            if (medians[0] > MaxTickWorkMs)
                Fail($"{bots} bots on {map.Name} with foliage cover: tick median {medians[0]:0.00} ms (budget {MaxTickWorkMs} ms)");
            if (medians[0] - medians[1] > 1.5)
                Fail($"foliage cover costs {medians[0] - medians[1]:0.00} ms per tick with {bots} bots on {map.Name} (budget 1.5 ms)");
        }
    }
}
