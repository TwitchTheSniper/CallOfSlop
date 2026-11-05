using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>"Grenades and constructs": thrown and launched things meet constructs (board report: grenades passed through vehicles): a thin plate at speed, one coming at it, a moving deck, a rocket, a blast's cover; then a real throw at a block, host and client.</summary>
public static partial class SelfTest
{
    private static void CheckGrenadeConstructs()
    {
        var yard = Maps.Find("Proving Ground")!;
        yard.RestoreProps();
        World.SetMap(yard);
        var problems = new List<string>();
        var w = new ConstructWorld();
        var keep = Throwables.Bodies;
        Throwables.Bodies = w;
        var high = new Vector3(0, 60, 0);
        var plateDef = ConstructParts.Need("Panel");
        var size = plateDef.BoundsMax - plateDef.BoundsMin;
        var thin = size.X < size.Y && size.X < size.Z ? Vector3.UnitX : size.Y < size.Z ? Vector3.UnitY : Vector3.UnitZ;
        var mid = (plateDef.BoundsMin + plateDef.BoundsMax) * 0.5f;
        float Through(Vector3 bodyVel, float throwSpeed)
        {
            var c = w.Create(high, Quaternion.Identity)!;
            w.AddPart(c, plateDef, Vector3.Zero, 0);
            c.Rebuild();
            c.Vel = bodyVel;
            var p = new Projectile { Pos = c.ToWorld(mid) - thin * 2.5f, Vel = thin * throwSpeed };
            for (var t = 0; t < 40; t++)
            {
                c.Pos += bodyVel * World.TickDt;
                Throwables.Step(p, World.TickDt);
                if (Vector3.Dot(p.Pos - c.ToWorld(mid), thin) > 0)
                    break;
            }
            var side = Vector3.Dot(p.Pos - c.ToWorld(mid), thin);
            w.Remove(c);
            return side;
        }
        var still = Through(default, 90f);
        var closing = Through(-thin * 30f, 60f);
        Line($"plate: past it by {still:0.00} m at 90 m/s, by {closing:0.00} m closing at 90 m/s (negative: this side)");
        if (still > 0 || closing > 0)
            problems.Add("a fast grenade tunnelled through a thin plate");

        // A moving deck: it settles and goes along, and falls when the deck goes.
        var deckDef = ConstructParts.Need("Deck Plate");
        var deck = w.Create(high, Quaternion.Identity)!;
        w.AddPart(deck, deckDef, Vector3.Zero, 0);
        deck.Rebuild();
        var deckTop = deck.ToWorld(new Vector3(0, deckDef.BoundsMax.Y, 0)).Y;
        var move = new Vector3(8, 0, 0);
        deck.Vel = move;
        var g = new Projectile { Pos = deck.ToWorld((deckDef.BoundsMin + deckDef.BoundsMax) * 0.5f) + new Vector3(0, 1.2f, 0), Vel = move };
        for (var t = 0; t < 180; t++)
        {
            deck.Pos += move * World.TickDt;
            Throwables.Step(g, World.TickDt);
        }
        var off = g.Pos - deck.ToWorld((deckDef.BoundsMin + deckDef.BoundsMax) * 0.5f);
        Line($"deck: riding {g.RideBody == deck.Id}, {off.X:0.00} m along, {g.Pos.Y - deckTop:0.00} m above after 3 s at 8 m/s");
        if (g.RideBody != deck.Id || g.Pos.Y < deckTop || g.Pos.Y > deckTop + 0.5f || MathF.Abs(off.X) > 0.5f)
            problems.Add("a grenade didn't settle on a moving deck and go with it");
        w.Remove(deck);
        Throwables.Step(g, World.TickDt);
        Throwables.Step(g, World.TickDt);
        if (g.RideBody != 0)
            problems.Add("a grenade stayed riding a deck that had gone");

        // A rocket, and a blast's cover.
        var wall = w.Create(high, Quaternion.Identity)!;
        w.AddPart(wall, plateDef, Vector3.Zero, 0);
        wall.Rebuild();
        var centre = wall.ToWorld(mid);
        var launcher = Weapons.All.First(d => d.IsLauncher && d.LauncherProjectile == LauncherProjectile.Rocket);
        var r = new Rocket { Def = launcher, Pos = centre - thin * 4f, Vel = thin * 120f };
        var contact = RocketContact.None;
        for (var t = 0; t < 30 && contact == RocketContact.None; t++)
            contact = RocketFlight.Step(r, null, out _);
        var covered = Throwables.ConstructCover(centre - thin, thin, 2f);
        var open = Throwables.ConstructCover(centre - thin + new Vector3(0, 30, 0), thin, 2f);
        Line($"rocket: {contact}, {Vector3.Dot(r.Pos - centre, thin):0.00} m from the plate; cover through it {covered}, in the open {open}");
        if (contact != RocketContact.World || Vector3.Dot(r.Pos - centre, thin) > 0)
            problems.Add("a rocket flew through a plate");
        if (!covered || open)
            problems.Add("a plate didn't cover a blast, or the open did");
        Throwables.Bodies = keep;

        // A real throw at a block: it comes back on both peers.
        const ushort port = 26401;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.5, ref now, clock);
        if (server.Players.TryGetValue(a.LocalId, out var sp))
        {
            server.Net.SimulateBadNetwork(80, 15, 4);
            a.Net.SimulateBadNetwork(80, 15, 4);
            for (var i = 0; i < 40 && !sp.Alive; i++)
            {
                sp.WantsRespawn = true;
                Pump(server, [a], 0.2, ref now, clock);
            }
            var blockDef = ConstructParts.Need("Frame Block");
            var block = ConstructBuild.PlaceNew(server.Constructs, blockDef, new FreePlacement { Pos = ConstructWorld.Ground(World.Query, 20, 20) + new Vector3(0, 1.3f, 0), Rot = Quaternion.Identity, Anchor = true })!;
            var mark = block.ToWorld((blockDef.BoundsMin + blockDef.BoundsMax) * 0.5f);
            sp.Sim.Position = ConstructWorld.Ground(World.Query, mark.X - 4, mark.Z) + new Vector3(0, 0.05f, 0);
            sp.Sim.Velocity = default;
            Pump(server, [a], 0.8, ref now, clock);
            var dir = Vector3.Normalize(mark - new Vector3(0, 0.5f, 0) - sp.Eye);
            a.LookYaw = MathF.Atan2(dir.X, -dir.Z);
            a.LookPitch = MathF.Asin(dir.Y);
            a.GrenadesLeft = 2;
            sp.GrenadesLeft = 2;
            sp.Grenade = ThrowableKind.Frag;
            a.LocalGrenade = ThrowableKind.Frag;
            a.TryThrow(false, Brutal.GlfwApi.Glfw.Time);
            float mostS = float.MinValue, mostC = float.MinValue;
            var seen = 0;
            var gap = 0f;
            for (var end = now + 1.2; now < end;)
            {
                Pump(server, [a], 0.03, ref now, clock);
                foreach (var s in server.Projectiles.Values)
                {
                    mostS = MathF.Max(mostS, s.Pos.X);
                    seen++;
                }
                foreach (var c in a.Projectiles.Values)
                    mostC = MathF.Max(mostC, c.Pos.X);
            }
            foreach (var s in server.Projectiles.Values)
                foreach (var c in a.Projectiles.Values)
                    gap = Vector3.Distance(s.Pos, c.Pos);
            Line($"throw: furthest {mostS:0.00} (host), {mostC:0.00} (client) against a block at {mark.X:0.00}; {gap:0.00} m apart after it");
            if (seen == 0 || mostS > mark.X || mostC > mark.X || gap > 2f)
                problems.Add("a real throw went through a block, or the peers disagreed");
        }
        else
            problems.Add("the client never joined");
        foreach (var p in problems)
            Fail($"grenades and constructs: {p}");
    }
}
