using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-fuelblast</c>: tanks with fuel in them going up (docs/sandbox/CONSTRUCTS.md, "Fuel blasts"). A command-line benchmark, not a self-test
/// section: it fails nothing. A host and a client over loopback; each scene is built far from the others, one tank is broken, and every server
/// tick is timed until the blasts are done: an empty and a full drum, a tanker barrel, a Fluid Tank of fuel and one of water, a row of drums, a
/// fused hull with tanks on it, a Medium Fuel Bunker. Runs twice (the first warms the JIT: read the last). One line a scene.
/// </summary>
public static partial class SelfTest
{
    public static int BenchFuelBlast(string[] args)
    {
        for (var run = 1; run <= 2; run++)
        {
            Console.WriteLine($"[fuel bench] run {run} of 2{(run == 1 ? " (warms the JIT: read the last)" : "")}");
            BenchFuelBlastOnce((ushort)(26634 + run));
        }
        return 0;
    }

    private static void BenchFuelBlastOnce(ushort port)
    {
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        var map = Maps.Find("Proving Ground") ?? Maps.All.First(m => m.Terrain != null);
        map.RestoreProps();
        map.Terrain?.Edits.Clear();
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: (byte)Array.IndexOf(Maps.All, map)) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Client", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [client], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [client], 0.25, ref now);
        var w = server.Constructs;
        var block = ConstructParts.Need("Frame Block");
        var drum = ConstructParts.Need("Fuel Drum");
        var origin = map.Spawns.Count > 0 ? map.Spawns[0] : Vector3.Zero;
        foreach (var p in server.Players.Values)
            if (p.Alive)
                p.Teleport(origin + new Vector3(-400, 0, -400));
        var scene = 0;
        Vector3 Spot()
        {
            var at = origin + new Vector3(60 + 90 * (scene % 4), 0, 60 + 90 * (scene / 4));
            scene++;
            at.Y = (map.Terrain?.Sample(at.X, at.Z) ?? 0) + 1.5f;
            return at;
        }
        // A frozen body of a block with a tank on it: the tank's index.
        (Construct C, int Tank) Tank(Vector3 at, ConstructPartDef def, float fuel)
        {
            var c = w.Create(at, Quaternion.Identity)!;
            w.AddPart(c, block, Vector3.Zero, 0);
            var t = w.AddPart(c, def, new Vector3(0, 1.2f, 0), 0);
            c.Frozen = c.Anchored = true;
            c.Rebuild();
            c.Fuel = fuel; // (after the first rebuild, which fills a body with no fuel set)
            return (c, t);
        }
        void FluidFill(Construct c, int part, Fluid f)
        {
            var d = c.DefOf(part);
            c.Parts[part].Cfg[d.CfgSlot[(int)CfgKey.Fluid]] = (int)f + 1;
            c.Parts[part].Cfg[d.CfgSlot[(int)CfgKey.Fill]] = 100;
        }
        void Run(string what, Construct c, int part, Action? hit = null)
        {
            Pump(server, [client], 0.5, ref now, clock);
            const double dt = 1.0 / World.TickRate;
            var (set0, land0, seen0, bodies0) = (w.FuelBlastsSet, w.ShellsLanded, client.ConstructBlastsSeen, w.Bodies.Count);
            FuelBlastTable.Current.BlastOf(ConstructWorld.FuelEnergyIn(c, part, FuelBlastTable.Current), out var radius, out var damage, out var crater);
            if (hit != null)
                hit();
            else
                w.DamagePart(c, part, c.Parts[part].Health + c.DefOf(part).Armour + 1, out _);
            var ticks = new List<double>();
            var cpu = new List<double>();
            long alloc = 0;
            var quiet = 0;
            for (var i = 0; i < 20 * World.TickRate; i++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                var c0 = ThreadCpu.NowMs();
                now += dt;
                server.Update(now, dt);
                cpu.Add(ThreadCpu.NowMs() - c0);
                ticks.Add(sw.Elapsed.TotalMilliseconds);
                alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                client.Update(now, dt);
                quiet = w.ShellBlastsWaiting == 0 && w.CookOffCount == 0 && !w.MegaBusy ? quiet + 1 : 0;
                if (quiet > World.TickRate)
                    break;
                Thread.Sleep(1);
            }
            Pump(server, [client], 0.3, ref now, clock);
            var sorted = ticks.OrderBy(v => v).ToList();
            Console.WriteLine($"[fuel bench] {what}: {w.FuelBlastsSet - set0} set off, {w.ShellsLanded - land0} blasts (the first {radius:0.0} m, {damage:0} at the middle, crater {crater:0.0} m), client saw {client.ConstructBlastsSeen - seen0}; bodies {bodies0} -> {w.Bodies.Count}; {ticks.Count} ticks, median {sorted[sorted.Count / 2]:0.00} ms, worst {sorted[^1]:0.00} ms, {ticks.Count(v => v > 8)} over 8 ms, worst CPU {cpu.Max():0.00} ms, {alloc / 1024} KB allocated");
        }

        var (e, et) = Tank(Spot(), drum, 0);
        Run("empty drum", e, et);
        var (f, ft) = Tank(Spot(), drum, 60);
        Run("full drum (60 l)", f, ft);
        var (h, ht) = Tank(Spot(), drum, 15);
        Run("a quarter drum (15 l)", h, ht);
        var tanker = ConstructParts.Need("Tanker Barrel 3 m");
        var (tb, tt) = Tank(Spot(), tanker, tanker.FuelCapacity);
        Run($"tanker barrel ({tanker.FuelCapacity:0} l)", tb, tt);
        var fluid = ConstructParts.Need("Large Fluid Tank");
        var (fl, flt) = Tank(Spot(), fluid, 0);
        FluidFill(fl, flt, Fluid.Fuel);
        Run("Large Fluid Tank of fuel", fl, flt);
        var (wa, wat) = Tank(Spot(), fluid, 0);
        FluidFill(wa, wat, Fluid.Water);
        Run("Large Fluid Tank of water", wa, wat);

        // A row of 24 drums, each on its own block, 2 m apart: one shot sets off the next.
        var row = Spot();
        var drums = new List<(Construct, int)>();
        for (var i = 0; i < 24; i++)
            drums.Add(Tank(row + new Vector3(2f * i, 0, 0), drum, 60));
        Run("a row of 24 full drums", drums[0].Item1, drums[0].Item2);

        // A fused 600-block hull with 16 full fuel tanks on it, fused in with it.
        var hullAt = Spot();
        var hull = CostHull(w, hullAt, 600, block);
        var tank = ConstructParts.Need("Fuel Tank");
        var size = block.BoundsMax - block.BoundsMin;
        var first = -1;
        for (var k = 0; k < 16; k++)
        {
            var i = w.AddPart(hull, tank, hull.Parts[k % 4 * 3 + k / 4 * 36].Pos + new Vector3(0, 0.5f, 0), 0); // (on its +y socket, 1.5 m apart)
            first = first < 0 ? i : first;
        }
        w.AutoLinkMany(hull, 0, hull.PartCount);
        hull.Frozen = hull.Anchored = true;
        hull.Rebuild();
        var tag = ConstructFuse.NewTag();
        for (var i = 0; i < hull.PartCount; i++)
            hull.Parts[i].Fuse = tag;
        hull.Dirty = true;
        hull.Rebuild();
        hull.Fuel = hull.FuelCapacity();
        Console.WriteLine($"[fuel bench] fused hull: {hull.PartCount} parts, {hull.UnitCount} unit(s), {hull.Fuel:0} l");
        Run("fused hull with 16 full tanks", hull, first);

        // The same hull with no fuel, hit by a plain shell's blast of the same size where the first tank was: what the blast's own walk costs.
        var plainAt = Spot();
        var plain = CostHull(w, plainAt, 600, block);
        w.AutoLinkMany(plain, 0, plain.PartCount);
        plain.Frozen = plain.Anchored = true;
        plain.Rebuild();
        var tag2 = ConstructFuse.NewTag();
        for (var i = 0; i < plain.PartCount; i++)
            plain.Parts[i].Fuse = tag2;
        plain.Dirty = true;
        plain.Rebuild();
        var spotAt = plain.ToWorld(plain.Parts[0].Pos + new Vector3(0, size.Y, 0));
        Run("fused hull, no tanks, a plain 3.9 m shell blast of 1200", plain, 0, () =>
        {
            w.AddShell(new CShell { Kind = ShellKind.Explosive, Pos = spotAt, Prev = spotAt, Tested = spotAt, Landed = true, Delay = 18, Blast = 3.9f, Damage = 1200f, Crater = 0.8f, By = 255 });
            w.ShellBlastsWaiting++;
        });

        var bunker = ConstructParts.Need("Medium Fuel Bunker");
        var (bk, bkt) = Tank(Spot() + new Vector3(0, 2, 0), bunker, bunker.FuelCapacity);
        Run($"Medium Fuel Bunker ({bunker.FuelCapacity:0} l)", bk, bkt);
        var capital = ConstructParts.Need("Capital Fuel Bunker");
        var (cb, cbt) = Tank(Spot() + new Vector3(0, 10, 0), capital, capital.FuelCapacity);
        var megas = w.MegasStarted;
        Run($"Capital Fuel Bunker ({capital.FuelCapacity:0} l)", cb, cbt);
        Console.WriteLine($"[fuel bench] massive blasts started: {w.MegasStarted - megas}, worst mega step {w.MegaStepWorstMs:0.00} ms");
    }
}
