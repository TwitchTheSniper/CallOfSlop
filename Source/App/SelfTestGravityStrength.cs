using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Gravity strength" self-test: the host's gravity setting (Source/Game/GameGravity.cs, <see cref="World.GravityScale"/>).
/// It scales every field type (straight down, a planet, every zone of a zoned map); a jump's height and air time scale
/// as 1/g with both movers; a grenade's fall and a ragdoll follow it; replays stay exact; the bots' nav graph is the same
/// at any setting; a planet keeps its floor (Pocket Planet: 11 m/s2, 60%); the value reaches clients (the lobby state and a
/// late joiner's Welcome) and the browser ('G'); and short seeded bot matches at 25% and 200% leave nobody stuck, under the
/// map or outside it. 100% is exactly the old game (the Mode modules digests check that).
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> StrengthNotes = new();

    private static void StrengthLine(string s)
    {
        Line(s);
        StrengthNotes.Add(s);
    }

    private static void CheckGravityStrength()
    {
        StrengthNotes.Clear();
        var before = World.Map;
        var mover = World.Mover;
        try
        {
            StrengthFields();
            StrengthJumps();
            StrengthFalls();
            StrengthPlanet();
            StrengthNav();
            StrengthBrowser();
            StrengthNetwork();
            StrengthBots();
        }
        finally
        {
            World.GravitySetting = 100;
            World.Mover = mover;
            World.SetMap(before);
            if (StrengthNotes.Count > 0)
                Line("::notice title=Gravity strength (results)::" + string.Join("%0A", StrengthNotes.Select(n => n.Replace("%", "%25"))));
        }
    }

    private static void SetGravity(int percent)
    {
        World.GravitySetting = percent;
        World.ApplyGravitySetting();
    }

    /// <summary>Every kind of field scales by the setting: a plain map, Pocket Planet, Keelhaul Yard's hull, hold and rock.</summary>
    private static void StrengthFields()
    {
        bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;
        var plain = Maps.All[0];
        var planet = Maps.Find("Pocket Planet")!;
        var yard = Maps.Find("Keelhaul Yard")!;
        var ok = true;
        var notes = new List<string>();
        foreach (var pct in new[] { 50, 150, 200 })
        {
            var k = pct / 100f;
            World.SetMap(plain);
            SetGravity(pct);
            ok &= Near(GravityFrames.GravityAt(new Vector3(3, 1, 2)), new Vector3(0, -18 * k, 0));
            World.SetMap(planet);
            ok &= Near(GravityFrames.GravityAt(new Vector3(0, 57, 0)), new Vector3(0, -11 * MathF.Max(k, planet.MinGravityPercent / 100f), 0)); // (never under the planet's floor)
            World.SetMap(yard);
            ok &= Near(GravityFrames.GravityAt(new Vector3(29.6f, 16, 5)), new Vector3(18 * k, 0, 0)) // the hull wall: sideways
                  && Near(GravityFrames.GravityAt(new Vector3(-31, -2.5f, 15)), new Vector3(0, -7 * k, 0)) // the low-g hold
                  && Near(GravityFrames.GravityAt(new Vector3(0, 9 + 6.2f, 0)), new Vector3(0, -18 * k, 0)) // the rock's top
                  && Near(World.Physics.Gravity.GravityAt(new Vector3(0, 0.5f, -20)) * World.GravityScale, new Vector3(0, -18 * k, 0)); // what the capsule mover uses
            notes.Add($"{pct}%: plain {GravityFrames.GravityAt(Vector3.Zero).Y:0.0}");
        }
        World.SetMap(plain);
        SetGravity(100);
        var exact = World.GravityScale == 1f && GravityFrames.GravityAt(Vector3.Zero) == new Vector3(0, -World.Gravity, 0);
        StrengthLine($"fields: plain, planet, hull wall, low-g hold and rock all scale {ok} ({string.Join(", ", notes)}); 100% is exactly 18 {exact}");
        if (!ok || !exact)
            Fail($"gravity strength: a field doesn't scale with the setting (ok {ok}, exact at 100% {exact})");
    }

    /// <summary>A standing jump: height and air time scale as 1/g, with both movers; replays exact.</summary>
    private static void StrengthJumps()
    {
        World.SetMap(Maps.All[0]);
        (float Top, int Air) Jump(IMover m)
        {
            World.Mover = m;
            var s = new MoveState { Position = new Vector3(0, 0.4f, 0), Grounded = false };
            for (var i = 0; i < 90; i++)
                World.Simulate(ref s, new InputCmd { SpeedPct = 100 }); // settle on the floor (whatever stands there)
            var y0 = s.Position.Y;
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Jump, SpeedPct = 100 });
            var top = 0f;
            var air = 1;
            while (!s.Grounded && air < 600)
            {
                World.Simulate(ref s, new InputCmd { SpeedPct = 100 });
                top = MathF.Max(top, s.Position.Y - y0);
                air++;
            }
            return (top, air);
        }
        var ok = true;
        var parts = new List<string>();
        foreach (var m in new IMover[] { ClassicMover.Instance, SweptMover.Instance })
            foreach (var pct in new[] { 25, 50, 100, 200 })
            {
                SetGravity(pct);
                var (top, air) = Jump(m);
                var g = World.Gravity * pct / 100f;
                var wantTop = World.JumpSpeed * World.JumpSpeed / (2 * g);
                var wantAir = 2 * World.JumpSpeed / g;
                ok &= MathF.Abs(top - wantTop) < 0.06f * wantTop + 0.06f && air * World.TickDt > wantAir * 0.75f && air * World.TickDt < wantAir * 1.1f; // (the ground snap ends a slow fall a little early)
                parts.Add($"{(m is ClassicMover ? "classic" : "capsule")} {pct}% {top:0.00} m/{air * World.TickDt:0.00} s (want {wantTop:0.00}/{wantAir:0.00})");
            }
        SetGravity(100);
        StrengthLine("jumps: " + string.Join(", ", parts));
        if (!ok)
            Fail("gravity strength: a jump's height or air time doesn't scale as 1/g: " + string.Join(", ", parts));

        // Replays at 50%: the same commands, the same bits (prediction).
        SetGravity(50);
        var rng = new Random(77);
        var cmds = new InputCmd[240];
        for (var i = 0; i < cmds.Length; i++)
            cmds[i] = new InputCmd { Seq = (uint)i + 1, Buttons = (Buttons)rng.Next(16) | (rng.Next(5) == 0 ? Buttons.Jump : 0), Yaw = (float)rng.NextDouble() * 6f, SpeedPct = 100 };
        var a = new MoveState { Position = new Vector3(2, 0, 2), Grounded = true };
        var mid = a;
        for (var i = 0; i < cmds.Length; i++)
        {
            World.Simulate(ref a, cmds[i]);
            if (i == 119)
                mid = a;
        }
        for (var i = 120; i < cmds.Length; i++)
            World.Simulate(ref mid, cmds[i]);
        SetGravity(100);
        var same = a.Position == mid.Position && a.Velocity == mid.Velocity;
        StrengthLine($"replay at 50%: exact {same}");
        if (!same)
            Fail("gravity strength: a replay at 50% gave a different state");
    }

    /// <summary>A grenade dropped from rest falls in sqrt(2h/g); a ragdoll settles (unbroken) at 25% and 200%.</summary>
    private static void StrengthFalls()
    {
        World.SetMap(Maps.All[0]);
        var parts = new List<string>();
        var ok = true;
        var t100 = 0;
        foreach (var pct in new[] { 100, 25, 200 })
        {
            SetGravity(pct);
            var frag = new Projectile { Kind = ThrowableKind.Frag, Pos = new Vector3(0.5f, 10, 0.5f), Vel = Vector3.Zero };
            var t = 0;
            while (t < 1200 && !Throwables.Step(frag, World.TickDt))
                t++;
            if (pct == 100)
                t100 = t;
            var ratio = t / (float)Math.Max(1, t100);
            var want = MathF.Sqrt(100f / pct); // fall time goes as 1/sqrt(g)
            ok &= MathF.Abs(ratio - want) < 0.05f * want;
            Ragdolls.Clear();
            var body = Ragdolls.SpawnForTest(new RagdollDeath { Feet = new Vector3(3, 0, 3), Yaw = 0.3f, Stance = Stance.Stand, Victim = 7, Killer = 3, Source = new Vector3(3, 1.3f, 7), HasSource = true, GunPower = 3f },
                RagdollLook.Soldier, 0, new Vector3(0.5f));
            var steps = 0;
            for (; steps < 1500 && body.Live && !body.Sim.Broken && !body.Sim.Asleep; steps++)
                Ragdolls.StepAll();
            var settled = !body.Sim.Broken && body.Sim.Asleep && body.Sim.Pelvis.Y is > -0.05f and < 0.8f;
            ok &= settled;
            parts.Add($"{pct}%: grenade lands after {t * World.TickDt:0.00} s ({ratio:0.00} x 100%'s, want {want:0.00}), ragdoll asleep {body.Sim.Asleep} after {steps * Ragdoll.Step:0.0} s, pelvis {body.Sim.Pelvis.Y:0.00} m");
            Ragdolls.Clear();
        }
        SetGravity(100);
        StrengthLine("falls: " + string.Join("; ", parts));
        if (!ok)
            Fail("gravity strength: a grenade or a ragdoll doesn't fall with the setting: " + string.Join("; ", parts));
    }

    /// <summary>Pocket Planet: 11 m/s2, a 60% floor; at a 25% setting a sprint jump still comes back down onto the planet.</summary>
    private static void StrengthPlanet()
    {
        var m = Maps.Find("Pocket Planet")!;
        World.SetMap(m);
        SetGravity(25);
        var effective = World.GravityScale;
        var s = new MoveState { Position = Map.SurfaceDir(20, 30) * (m.PlanetRadius + 0.3f), Grounded = false };
        for (var i = 0; i < 60; i++)
            World.Simulate(ref s, new InputCmd { SpeedPct = 100 });
        var (yaw, _) = GravityFrames.Angles(s.Position, Vector3.UnitY);
        var prev = s.Position;
        for (var i = 0; i < 30; i++)
        {
            GravityFrames.Transport(ref yaw, ref _yawScratch, prev, s.Position);
            prev = s.Position;
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = yaw, SpeedPct = 100 });
        }
        World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | Buttons.Jump, Yaw = yaw, SpeedPct = 100 });
        var highest = 0f;
        var air = 0;
        while (!s.Grounded && air < 600)
        {
            GravityFrames.Transport(ref yaw, ref _yawScratch, prev, s.Position);
            prev = s.Position;
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = yaw, SpeedPct = 100 });
            highest = MathF.Max(highest, s.Position.Length() - m.PlanetRadius);
            air++;
        }
        SetGravity(100);
        var g = m.PointGravity!;
        var ok = g.Strength == 11f && m.MinGravityPercent == 60 && MathF.Abs(effective - 0.6f) < 1e-6f && s.Grounded && air < 5 * World.TickRate && highest < 7f;
        StrengthLine($"Pocket Planet: {g.Strength} m/s2, plays at no less than {m.MinGravityPercent}%; at a 25% setting the scale is {effective:0.00} and a sprint jump lands after {air * World.TickDt:0.00} s, {highest:0.0} m up at most");
        if (!ok)
            Fail($"gravity strength: Pocket Planet's gravity or its floor is wrong (strength {g.Strength}, floor {m.MinGravityPercent}%, scale {effective}, landed {s.Grounded} after {air} ticks, {highest:0.0} m up)");
    }

    private static float _yawScratch;

    /// <summary>The bots' graph is built at the map's own gravity: the same nodes and edges at 25%, 100% and 200%.</summary>
    private static void StrengthNav()
    {
        var parts = new List<string>();
        var ok = true;
        foreach (var name in new[] { "Stratos Station", "Keelhaul Yard" })
        {
            var m = Maps.Find(name)!;
            World.SetMap(m);
            SetGravity(100);
            var g100 = NavGraph.Build(m);
            SetGravity(25);
            var g25 = NavGraph.Build(m);
            SetGravity(200);
            var g200 = NavGraph.Build(m);
            SetGravity(100);
            var same = g25.Nodes.SequenceEqual(g100.Nodes) && g25.EdgeTo.SequenceEqual(g100.EdgeTo) && g200.Nodes.SequenceEqual(g100.Nodes) && g200.EdgeTo.SequenceEqual(g100.EdgeTo);
            ok &= same;
            parts.Add($"{name} {g100.Nodes.Length} nodes {g100.EdgeCount} edges, the same at 25% and 200% {same}");
        }
        StrengthLine("nav: " + string.Join("; ", parts));
        if (!ok)
            Fail("gravity strength: the bots' nav graph depends on the gravity setting: " + string.Join("; ", parts));
    }

    /// <summary>The server info carries a gravity other than 100% ('G', after the build) and reads it back.</summary>
    private static void StrengthBrowser()
    {
        var info = new ServerInfo("Host", "Proto Box", 2, 8, 1, MatchPhase.Playing, 60000, ServerInfo.ProtocolVersion, Build: "0.2026.1023.9", Gravity: 50, InstanceId: 7);
        var back = ServerInfo.Find(info.Encode());
        var plain = ServerInfo.Find((info with { Gravity = 100 }).Encode());
        var ok = back is { Gravity: 50, InstanceId: 7 } && back.Value.Build == info.Build && plain is { Gravity: 100 };
        StrengthLine($"server info: gravity 50% read back {back?.Gravity} (build {back?.Build}), 100% sends no tag and reads {plain?.Gravity}");
        if (!ok)
            Fail("gravity strength: the server info doesn't carry the gravity setting");
    }

    /// <summary>A host, two clients in the lobby room and a late joiner: every client ends with the host's setting.</summary>
    private static void StrengthNetwork()
    {
        var small = FindMap("Proto Box", m => m.MaxPlayers >= 8)!;
        const ushort port = 60247;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(small);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, mapId: small.Id);
        server.MapVoteEnabled = false;
        server.WarmupEnabled = true;
        server.LobbyEnabled = true;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        var first = a.GravityPercent;
        server.GravityPercent = 50; // changed in the lobby room: the lobby state carries it
        Pump(server, [a], 1.0, ref now, clock);
        var changed = a.GravityPercent;
        server.GravityPercent = 150;
        using var c = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Charlie", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id); // (its constructor resets the shared scale: the Welcome sets it back)
        Pump(server, [a, c], 1.5, ref now, clock);
        var ok = first == 100 && changed == 50 && a.GravityPercent == 150 && c.GravityPercent == 150 && c.LocalId != byte.MaxValue && World.GravityScale == 1.5f;
        StrengthLine($"network: a client starts at {first}%, sees the host's change to 50% as {changed}%, then 150% as {a.GravityPercent}%; a late joiner (id {c.LocalId}) gets {c.GravityPercent}% from its Welcome; scale {World.GravityScale}");
        if (!ok)
            Fail($"gravity strength: the host's gravity setting doesn't reach the clients ({first}, {changed}, {a.GravityPercent}, joiner {c.GravityPercent}, scale {World.GravityScale})");
    }

    /// <summary>Seeded 8-player bot matches at 25% and 200% (30 s each) on a void map, a zones map and a plain one.</summary>
    private static void StrengthBots()
    {
        var ok = true;
        foreach (var name in new[] { "Stratos Station", "Keelhaul Yard", "Proto Box" })
        {
            var m = FindMap(name, _ => true)!;
            foreach (var pct in new[] { 25, 200 })
            {
                Game.SeedNextGame = 4242 + pct;
                using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                    bots: Math.Min(Game.ClassicPlayers, m.MaxPlayers) - 1, botSkill: BotDifficulty.Normal, mapId: m.Id);
                game.MapVoteEnabled = false;
                game.GravityPercent = pct;
                var now = 0.0;
                const double dt = 1.0 / World.TickRate;
                var lastPos = new Dictionary<byte, Vector3>();
                var moved = new Dictionary<byte, float>();
                int under = 0, outside = 0;
                for (var i = 0; i < 30 * World.TickRate; i++)
                {
                    now += dt;
                    game.Update(now, dt);
                    if (i % 30 != 0)
                        continue;
                    foreach (var p in game.Players.Values.Where(p => p.Alive))
                    {
                        if (m.FallKillY is { } kill ? p.Position.Y < kill - 0.5f : p.Position.Y < m.GroundHeight(p.Position.X, p.Position.Z, p.Position.Y) - 0.5f)
                            under++;
                        if (m.Boundary is { } bd && !bd.Inside(p.Position.X, p.Position.Z, -0.5f))
                            outside++;
                        if (p.Bot == null)
                            continue;
                        if (lastPos.TryGetValue(p.Id, out var lp))
                            moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                        lastPos[p.Id] = p.Position;
                    }
                }
                var stuck = moved.Count(kv => kv.Value < 8f);
                var line = $"{name} at {pct}%: kills {game.Players.Values.Sum(p => (int)p.Kills)}, avg bot travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, stuck {stuck}, stuck kills {game.BotStuckKills}, "
                           + $"fall kills {game.FallKills}, under the map {under}, outside {outside}";
                StrengthLine("bots: " + line);
                if (under > 0 || outside > 0 || stuck > 1 || game.BotStuckKills > 1 || game.FallKills > 3)
                {
                    ok = false;
                    Fail("gravity strength: bots misbehave: " + line);
                }
            }
        }
        World.GravitySetting = 100;
        _ = ok;
    }
}
