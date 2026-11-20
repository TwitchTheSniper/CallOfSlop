using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--battle-check [SHIP] [--idle --ground [--map NAME]] [--shots N] [--per-tick K] [--gun NAME] [--range M] [--seed S] [--focus F] [--blasts N [--blast-damage D] [--blast-radius R]]</c>: a capital ship under railgun
/// fire, headless (docs/sandbox/design/capital-battles.md). The ship (default the Zero Day) is built on the Proving Ground and
/// held still in the air; a Zero Day (for its railgun) is held far off; then N shots are fired at it from its broadside, K in a
/// tick, each at a random point on its middle, through the real round (<see cref="ConstructWeapons.ShotFrom"/>), and a world
/// tick is stepped after each volley. It reports what each volley did (parts hit, parts broken, pieces split off, the bores
/// left) and what it cost: the rounds, the rebuild and the tick, against a tick with no shots. A dev tool: it allocates freely.
/// </summary>
public static class BattleCheck
{
    public static int Cli(string[] args)
    {
        string Arg(string name, string def) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? def;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var name = Arg("--battle-check", "Zero Day");
        if (name.StartsWith("--"))
            name = "Zero Day";
        var shots = int.Parse(Arg("--shots", "40"), inv);
        var perTick = Math.Max(1, int.Parse(Arg("--per-tick", "1"), inv));
        var gunName = Arg("--gun", "Zero Day Railgun");
        var range = float.Parse(Arg("--range", "300"), inv);
        var rng = new Random(int.Parse(Arg("--seed", "1"), inv));
        float? focus = Arg("--focus", "") is { Length: > 0 } fs ? float.Parse(fs, inv) : null; // (every shot within 6 m of this share along the ship, -0.5 to 0.5: to break it there)
        if (Maps.Find(Arg("--map", "Proving Ground")) is not { } yard)
        {
            Console.WriteLine("no Proving Ground map");
            return 1;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        if (args.Contains("--seam-hashes"))
        {
            foreach (var b in ConstructBuild.Blueprints)
            {
                var sw0 = Stopwatch.StartNew();
                var ww = new ConstructWorld();
                var cc = ConstructBuild.BuildOnGround(ww, b, ConstructWorld.Ground(World.Query, 0, 0), 0, out _);
                if (cc == null)
                    continue;
                cc.Dirty = true;
                cc.Rebuild();
                var hh = 17L;
                for (var q = 0; q < cc.SeamCount; q++)
                    hh = hh * 31 + (cc.SeamLinks[q].A * 7919L + cc.SeamLinks[q].B) * 131 + cc.SeamLinks[q].SA * 13 + cc.SeamLinks[q].SB;
                Console.WriteLine($"SEAMS {b.Name}: {cc.FreeformCount} welds, {cc.SeamCount} seams, hash {hh}");
            }
            return 0;
        }
        var w = new ConstructWorld();
        var sw = Stopwatch.StartNew();
        var bp = ConstructBuild.FindBlueprint(name);
        var ship = bp == null ? null : ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 0, 0), 0, out _);
        var gunBp = ConstructBuild.FindBlueprint("Zero Day Frigate");
        if (ship == null || gunBp == null)
        {
            Console.WriteLine($"{name}: doesn't build (or no Zero Day Frigate)");
            return 1;
        }
        if (args.Contains("--idle") && args.Contains("--ground"))
        {
            var dt0 = 1f / World.TickRate;
            var sw1 = Stopwatch.StartNew();
            for (var round = 0; round < 12; round++)
            {
                Array.Clear(ConstructPhysics.PartMs);
                var phys = 0.0;
                var worst = 0.0;
                for (var i = 0; i < 60; i++)
                {
                    var p0 = Stopwatch.GetTimestamp();
                    w.Step(World.Query, dt0);
                    var e = Stopwatch.GetElapsedTime(p0).TotalMilliseconds;
                    phys += e;
                    worst = Math.Max(worst, e);
                }
                var pm = ConstructPhysics.PartMs;
                Console.WriteLine($"ground {round}: world step {phys / 60:0.000} ms a tick (worst {worst:0.00}); physics list {pm[0] / 60:0.000}, bodies {pm[1] / 60:0.000}, contacts {pm[2] / 60:0.000}, solve {pm[3] / 60:0.000}, rest {pm[4] / 60:0.000}; asleep {ship.Asleep}, speed {ship.Vel.Length():0.000}, spin {ship.AngVel.Length():0.00000} (x radius {ship.AngVel.Length() * MathF.Max(1, ship.Radius / 8f):0.0000}), sleep ticks {ship.SleepTicks}, hover {ship.HoverOn} stab {ship.StabLive} thrust {ship.ThrustShow:0.00} active {ship.ActiveSignal}, bodies {w.Bodies.Count}, contacts {ConstructPhysics.LastContacts}, rays {ConstructPhysics.LastRays}");
            }
            return 0;
        }
        Hold(w, w.Bodies.ToArray(), new Vector3(0, 400f, 0));
        var shooter = ConstructBuild.BuildOnGround(w, gunBp, ConstructWorld.Ground(World.Query, 0, 0), 0, out _);
        if (shooter == null)
        {
            Console.WriteLine("Zero Day Frigate: doesn't build");
            return 1;
        }
        foreach (var b in w.Bodies.ToArray())
            if (b != ship && !ReferenceEquals(b, ship) && b.Pos.Y < 300f)
                Hold(w, [b], new Vector3(0, 3000f, 0)); // (the shooter and its craft: far up out of the way)
        ship.Rebuild();
        _ = ship.Collider;
        var gun = -1;
        for (var k = 0; k < shooter.Guns.Length && gun < 0; k++)
            if (shooter.Guns[k].Def != null && shooter.DefOf(shooter.Guns[k].Part).Name.Equals(gunName, StringComparison.OrdinalIgnoreCase))
                gun = k;
        if (gun < 0)
        {
            Console.WriteLine($"no {gunName} on the Zero Day");
            return 1;
        }
        Console.WriteLine($"{name}: {ship.PartCount} parts, {ship.LinkCount} links, built in {sw.Elapsed.TotalSeconds:0.0} s; gun {gunName} ({shooter.Guns[gun].Def!.Damage} damage)");

        // The ship's long axis and its middle: shots come in from the broadside.
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        for (var i = 0; i < ship.PartCount; i++)
        {
            ship.RestBounds(i, out var lo, out var hi);
            mn = Vector3.Min(mn, ship.Pos + Vector3.Transform(lo, ship.Rot));
            mx = Vector3.Max(mx, ship.Pos + Vector3.Transform(hi, ship.Rot));
        }
        var size = mx - mn;
        var centre = (mn + mx) * 0.5f;
        var longX = size.X >= size.Z;
        Console.WriteLine($"bounds {size.X:0} x {size.Y:0} x {size.Z:0} m, long axis {(longX ? "X" : "Z")}");

        // What one structure change costs this body: a full rebuild, then its collider.
        for (var k = 0; k < 3; k++)
        {
            sw.Restart();
            ship.Dirty = true;
            ship.Rebuild();
            var rb = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            ship.ColliderChanged();
            _ = ship.Collider;
            var seamHash = 17L;
            for (var q = 0; q < ship.SeamCount; q++)
                seamHash = seamHash * 31 + (ship.SeamLinks[q].A * 7919L + ship.SeamLinks[q].B) * 131 + ship.SeamLinks[q].SA * 13 + ship.SeamLinks[q].SB;
            Console.WriteLine($"a full rebuild {rb:0.0} ms, its collider {sw.Elapsed.TotalMilliseconds:0.0} ms; {ship.SeamCount} seam links (hash {seamHash})");
        }

        // A tick with no shots, for the baseline.
        var dt = 1f / World.TickRate;
        for (var i = 0; i < 5; i++)
            w.Step(World.Query, dt);
        sw.Restart();
        for (var i = 0; i < 20; i++)
            w.Step(World.Query, dt);
        var baseMs = sw.Elapsed.TotalMilliseconds / 20;
        Console.WriteLine($"baseline tick {baseMs:0.00} ms ({w.Bodies.Count} bodies)");
        if (args.Contains("--idle"))
        {
            // Where an idle tick goes: the physics' parts, the ship left free on the ground (not held) with --free.
            if (args.Contains("--free"))
                foreach (var b in w.Bodies)
                    if (b == ship)
                    {
                        b.Anchored = false;
                        b.Pos -= new Vector3(0, 400f, 0);
                        b.PrevPos = b.Pos;
                    }
            for (var round = 0; round < 6; round++)
            {
                Array.Clear(ConstructPhysics.PartMs);
                sw.Restart();
                var phys = 0.0;
                for (var i = 0; i < 60; i++)
                {
                    var p0 = Stopwatch.GetTimestamp();
                    ConstructPhysics.Step(w, World.Query, dt);
                    phys += Stopwatch.GetElapsedTime(p0).TotalMilliseconds;
                }
                var pm = ConstructPhysics.PartMs;
                Console.WriteLine($"idle {round}: physics alone {phys / 60:0.000} ms a tick (list {pm[0] / 60:0.000}, bodies {pm[1] / 60:0.000}, contacts {pm[2] / 60:0.000}, solve {pm[3] / 60:0.000}, rest {pm[4] / 60:0.000}); ship asleep {ship.Asleep}, anchored {ship.Anchored}, speed {ship.Vel.Length():0.000}");
                sw.Restart();
                for (var i = 0; i < 60; i++)
                    w.Step(World.Query, dt);
                Console.WriteLine($"   world step {sw.Elapsed.TotalMilliseconds / 60:0.000} ms a tick");
            }
            return 0;
        }

        double roundMs = 0, markMs = 0, rebuildMs = 0, tickMs = 0, worstTick = 0, worstRound = 0;
        var startParts = ship.PartCount;
        var startBodies = w.Bodies.Count;
        var fired = 0;
        var volleys = 0;
        var damaged = 0;
        var throughs = 0;
        for (var v = 0; fired < shots; v++)
        {
            var health0 = SumHealth(ship);
            var parts0 = ship.PartCount;
            sw.Restart();
            for (var s = 0; s < perTick && fired < shots; s++, fired++)
            {
                var along = focus is { } f ? f + (float)(rng.NextDouble() - 0.5) * 12f / MathF.Max(size.X, size.Z) : (float)(rng.NextDouble() * 0.8 - 0.4);
                var up = (float)(rng.NextDouble() * 0.6 - 0.3);
                var aim = centre + (longX ? new Vector3(size.X * along, size.Y * up, 0) : new Vector3(0, size.Y * up, size.Z * along));
                var side = rng.Next(2) == 0 ? -1f : 1f;
                var from = aim + (longX ? new Vector3(0, 0, side * (size.Z * 0.5f + range)) : new Vector3(side * (size.X * 0.5f + range), 0, 0));
                var dir = Vector3.Normalize(aim - from);
                var hit = ConstructWeapons.ShotFrom(w, shooter, gun, World.Query, from, dir, out _);
                if ((hit & CFireHit.Through) != 0)
                    throughs++;
            }
            var r = sw.Elapsed.TotalMilliseconds;
            roundMs += r;
            sw.Restart();
            MarkBores(w);
            markMs += sw.Elapsed.TotalMilliseconds;
            worstRound = Math.Max(worstRound, r);
            sw.Restart();
            if (w.Get(ship.Id) is { Dirty: true } live)
                live.Rebuild();
            rebuildMs += sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            w.Step(World.Query, dt);
            var t = sw.Elapsed.TotalMilliseconds;
            MarkBores(w);
            tickMs += t;
            worstTick = Math.Max(worstTick, t + r);
            volleys++;
            if (w.Get(ship.Id) == null)
            {
                Console.WriteLine("the ship is gone");
                break;
            }
            var lost = health0 - SumHealth(ship);
            if (lost > 0)
                damaged++;
            if (t + r > 20)
                Console.WriteLine($"volley {v}: slow, step parts {string.Join(", ", w.StepMs.Select(x => x.ToString("0.0")))}");
            Array.Clear(w.StepMs);
            if (v < 6 || v % 10 == 0)
                Console.WriteLine($"volley {v}: rounds {r:0.00} ms, tick {t:0.00} ms, parts {parts0} -> {ship.PartCount}, health lost {lost:0}, bodies {w.Bodies.Count}, bores {w.BoreMarkCount}");
        }
        // Torpedo-sized blasts at the focus (or anywhere on the middle), one a tick.
        var blasts = int.Parse(Arg("--blasts", "0"), inv);
        var blastDamage = float.Parse(Arg("--blast-damage", "30000"), inv);
        var blastRadius = float.Parse(Arg("--blast-radius", "12"), inv);
        for (var b = 0; b < blasts && w.Get(ship.Id) != null; b++)
        {
            var along = focus is { } f ? f + (float)(rng.NextDouble() - 0.5) * 12f / MathF.Max(size.X, size.Z) : (float)(rng.NextDouble() * 0.8 - 0.4);
            var across = (float)(rng.NextDouble() - 0.5) * 0.8f;
            var up = (float)(rng.NextDouble() - 0.5) * 0.8f;
            var at = centre + (longX ? new Vector3(size.X * along, size.Y * up, size.Z * across) : new Vector3(size.X * across, size.Y * up, size.Z * along));
            sw.Restart();
            ConstructWeapons.Explode(w, at, blastRadius, blastDamage, shooter, CFireKind.Blast);
            var e = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            w.Step(World.Query, dt);
            var t = sw.Elapsed.TotalMilliseconds;
            worstTick = Math.Max(worstTick, t + e);
            MarkBores(w);
            Console.WriteLine($"blast {b}: {e:0.00} ms, tick {t:0.00} ms, parts {ship.PartCount}, bodies {w.Bodies.Count}, break-ups {ConstructBreakUp.Snaps}");
        }
        for (var i = 0; i < 90 && w.Get(ship.Id) != null; i++)
        {
            sw.Restart();
            w.Step(World.Query, dt); // (the rounds that flew on land, the break's blasts go off)
            var t = sw.Elapsed.TotalMilliseconds;
            if (t > 20)
                Console.WriteLine($"after the shots, tick {i}: {t:0.0} ms (step parts {string.Join(", ", w.StepMs.Select(x => x.ToString("0.0")))}), bodies {w.Bodies.Count}");
            Array.Clear(w.StepMs);
            worstTick = Math.Max(worstTick, t);
            MarkBores(w);
        }
        Console.WriteLine($"{fired} shots in {volleys} volleys ({perTick} a tick): {throughs} went through a part, {damaged} volleys did damage");
        Console.WriteLine($"rounds {roundMs / Math.Max(1, fired):0.000} ms a shot (worst volley {worstRound:0.00} ms), rebuild {rebuildMs / Math.Max(1, volleys):0.00} ms a volley, marks (every PC) {markMs / Math.Max(1, fired):0.000} ms a shot, tick {tickMs / Math.Max(1, volleys):0.00} ms a volley (baseline {baseMs:0.00}), worst volley + tick {worstTick:0.00} ms");
        Console.WriteLine($"parts {startParts} -> {ship.PartCount}, bodies {startBodies} -> {w.Bodies.Count}, holes marked {w.BoreMarkCount}, parts bored {ConstructBores.Bored}, spall hits {ConstructBores.Spalled}, break-ups {ConstructBreakUp.Snaps}");
        foreach (var e in w.Events)
            if (e.Contains("broke up"))
                Console.WriteLine(e);
        var sizes = w.Bodies.GroupBy(b => b.PartCount switch { 1 => "1", < 10 => "2-9", < 100 => "10-99", < 1000 => "100-999", _ => "1000+" }).Select(gr => $"{gr.Key} parts: {gr.Count()} ({gr.Count(b => !b.Asleep)} awake)");
        Console.WriteLine($"bodies by size: {string.Join("; ", sizes)}; scrap {w.Events.Count(e => e.Contains("shot to scrap"))}, wrecks off {w.Events.Count(e => e.Contains("broke off"))}");
        return 0;
    }

    /// <summary>What every PC does with the tick's bores: the holes marked on its copy (here the server's own).</summary>
    private static void MarkBores(ConstructWorld w)
    {
        Span<Vector3> exits = stackalloc Vector3[16];
        for (var k = 0; k < w.FireCount; k++)
            if (w.Fires[k].Kind == CFireKind.Bore)
            {
                var ex = ConstructBores.Mark(w, w.Fires[k].At, w.Fires[k].To, w.Fires[k].Radius, 0, exits);
                if (ConstructBores.Trace)
                    Console.WriteLine($"  marked a bore {Vector3.Distance(w.Fires[k].At, w.Fires[k].To):0.0} m long: {ex} exits, {w.BoreMarkCount} holes in all");
            }
        w.FireCount = 0;
    }

    private static float SumHealth(Construct c)
    {
        var h = 0f;
        for (var i = 0; i < c.PartCount; i++)
            h += c.Parts[i].Health;
        return h;
    }

    private static void Hold(ConstructWorld w, Construct[] bodies, Vector3 off)
    {
        foreach (var b in bodies)
        {
            b.PrevPos = b.Pos;
            b.PrevRot = b.Rot;
            b.Pos += off;
            b.Vel = Vector3.Zero;
            b.AngVel = Vector3.Zero;
            b.Anchored = true;
        }
        ConstructRide.BeforePhysics(w);
    }
}
