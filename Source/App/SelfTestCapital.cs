using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const string CapDeckName = "Large Chassis Deck 4x4 m", CapPlateName = "Large Riveted Plate 4x4 m";

    /// <summary>Part by a socket's name.</summary>
    private static int CapSocket(ConstructPartDef d, string name) => Array.FindIndex(d.Sockets, s => s.Name == name);

    /// <summary>A part on a socket, through the calls an edit makes (<see cref="ConstructBuild.Place"/>, then Attach), upright when a spin allows. Returns the new part or -1.</summary>
    private static int CapAttach(ConstructWorld w, Construct c, int on, string socket, ConstructPartDef def, string by, out string? why)
    {
        why = null;
        var ts = CapSocket(c.DefOf(on), socket);
        var ds = CapSocket(def, by);
        if (ts < 0 || ds < 0)
        {
            why = $"no socket {socket} on {c.DefOf(on).Name} or {by} on {def.Name}";
            return -1;
        }
        ConstructGhost best = default;
        var found = false;
        for (var spin = 0; spin < 4; spin++)
        {
            if (ConstructBuild.Place(c, on, ts, def, ds, spin, out var g) is { } refused)
            {
                why = refused;
                continue;
            }
            if (!found || g.Rot == 0)
            {
                best = g;
                found = true;
            }
            why = null;
        }
        return found ? ConstructBuild.Attach(w, c, def, best) : -1;
    }

    /// <summary>
    /// A hull of about <paramref name="parts"/> parts (a grid of chassis decks 300 m long, a riveted plate on each), built through
    /// the calls an edit makes, about 100,000 t at 4,000 parts. Anchored to nothing: it lies on the ground.
    /// </summary>
    private static Construct? CapHull(ConstructWorld w, Vector3 at, int parts, out string? why)
    {
        why = null;
        var deck = ConstructParts.Need(CapDeckName);
        var plate = ConstructParts.Need(CapPlateName);
        var c = w.Create(at, Quaternion.Identity);
        if (c == null)
        {
            why = "no room for the hull";
            return null;
        }
        var length = 75;
        var width = Math.Max(1, (parts + 2 * length - 1) / 2 / length);
        var grid = new int[length * width];
        for (var x = 0; x < length; x++)
            for (var z = 0; z < width; z++)
            {
                int i;
                if (x == 0 && z == 0)
                    i = w.AddPart(c, deck, Vector3.Zero, 0);
                else if (z == 0)
                    i = CapAttach(w, c, grid[(x - 1) * width], "+x0", deck, "-x0", out why);
                else
                    i = CapAttach(w, c, grid[x * width + z - 1], "+z0", deck, "-z0", out why);
                if (i < 0)
                    return null;
                grid[x * width + z] = i;
                if (CapAttach(w, c, i, "top0.0", plate, "back0.0", out why) < 0)
                    return null;
            }
        return c;
    }

    /// <summary>
    /// "Capital parts" (docs/sandbox/CONSTRUCTS.md, "Size classes"): the big parts' files and rules; every part of every class
    /// snaps by every socket; a class mismatch is refused with the reason; a small part on a capital hull through an adapter, by
    /// the player's whole path; a 4,000-part body built through real calls, its rebuild, tick, ray, blocking, packet and draw
    /// measured, dragged with the physics gun; a tall stack stays up.
    /// </summary>
    private static void CheckCapitalParts()
    {
        CapFiles();
        CapSnaps();
        CapAdapterPath();
        CapBlueprint();
        CapBigBody();
        CapTall();
    }

    private static void CapFiles()
    {
        var mine = ConstructParts.All.Where(d => d.Folder.StartsWith("Capital", StringComparison.Ordinal)).ToList();
        var byClass = mine.GroupBy(d => d.Class).OrderBy(g => g.Key).Select(g => $"{ConstructParts.ClassNames[g.Key]} {g.Count()}");
        Line($"capital parts: {mine.Count} files ({string.Join(", ", byClass)}), {ConstructParts.All.Count} parts in all (at most {ConstructParts.MaxTypes}); " +
             $"caps: {Construct.MaxParts} parts, {Construct.MaxLinks} links, {Construct.MaxJoints} joints, {Construct.MaxWheels} wheels, {Construct.MaxGuns} guns, {Construct.MaxRoutes} routes, {Construct.MaxWires} wires");
        if (mine.Count < 300)
            Fail($"capital parts: only {mine.Count} loaded");
        var problems = new List<string>();
        foreach (var d in mine)
        {
            var cell = ConstructParts.ClassCell[d.Class];
            if (d.Group != ConstructParts.ClassNames[d.Class] && !string.Equals(d.Group, ConstructParts.ClassNames[d.Class], StringComparison.OrdinalIgnoreCase))
                problems.Add($"{d.Name}: group {d.Group}");
            if (d.Section.Length == 0)
                problems.Add($"{d.Name}: no section");
            var mixed = d.Sockets.Any(s => s.Class != d.Class);
            if (mixed != (d.Section == "Adapters"))
                problems.Add($"{d.Name}: {(mixed ? "a class mix that isn't an adapter" : "an adapter of one class")}");
            foreach (var s in d.Sockets)
            {
                var step = ConstructParts.ClassCell[s.Class];
                var axis = s.Dir >> 1;
                for (var a = 0; a < 3; a++)
                {
                    if (a == axis || d.Section is "Walls" or "Stairs" or "Plates" or "Armour" or "Decks" or "Roofs" or "Stacks" or "Shapes" or "Frames" && d.Name.Contains("Strut"))
                        continue;
                    var along = s.At[a] / step;
                    if (MathF.Abs(along - MathF.Round(along)) > 0.51f)
                        problems.Add($"{d.Name}: socket {s.Name} off its {step} m lattice ({s.At})");
                }
            }
            if (d.Mass <= 0 || !float.IsFinite(d.Mass))
                problems.Add($"{d.Name}: mass");
        }
        if (problems.Count > 0)
            Fail($"capital parts: {problems.Count} rule problems: {string.Join("; ", problems.Take(6))}");
    }

    private static void CapSnaps()
    {
        var w = new ConstructWorld();
        var tried = 0;
        var bad = new List<string>();
        foreach (var d in ConstructParts.All.Where(x => x.Folder.StartsWith("Capital", StringComparison.Ordinal)))
        {
            // On a node of each socket's own class, by every socket: the 24 turns and the lattice come out of Place.
            foreach (var cls in d.Sockets.Select(s => s.Class).Distinct())
            {
                var nodeDef = cls == 0 ? ConstructParts.Need("Square Beam 0.25 m") : ConstructParts.Need($"{char.ToUpperInvariant(ConstructParts.ClassNames[cls][0])}{ConstructParts.ClassNames[cls][1..]} Frame Node");
                var c = w.Create(new Vector3(0, 300, 0), Quaternion.Identity)!;
                w.AddPart(c, nodeDef, Vector3.Zero, 0);
                c.Rebuild();
                var free = Array.FindIndex(nodeDef.Sockets, s => s.Dir == 0);
                for (var s = 0; s < d.Sockets.Length; s++)
                {
                    if (d.Sockets[s].Class != cls || d.Sockets[s].Kind != SocketKind.Struct)
                        continue;
                    tried++;
                    if (ConstructBuild.Place(c, 0, free, d, s, 0, out var g) is { } why)
                    {
                        bad.Add($"{d.Name} {d.Sockets[s].Name}: {why}");
                        continue;
                    }
                    if (s == 0 || !bad.Any())
                        _ = g;
                }
                // One really attached: the link is there and the two share a socket point.
                var first = Array.FindIndex(d.Sockets, x => x.Class == cls && x.Kind == SocketKind.Struct);
                if (first >= 0 && ConstructBuild.Place(c, 0, free, d, first, 0, out var gh) == null)
                {
                    var i = ConstructBuild.Attach(w, c, d, gh);
                    if (i < 0 || c.LinkCount == 0)
                        bad.Add($"{d.Name}: it wouldn't attach");
                    else
                    {
                        c.Rebuild();
                        c.SocketAt(0, free, out var pa, out _);
                        c.SocketAt(i, first, out var pb, out _);
                        if (Vector3.Distance(pa, pb) > 0.02f)
                            bad.Add($"{d.Name}: {Vector3.Distance(pa, pb):0.000} m off its socket");
                    }
                }
                w.Remove(c);
            }
        }
        Line($"capital parts: {tried} sockets of every class placed on a node of their class; {bad.Count} refused");
        if (bad.Count > 0)
            Fail($"capital parts: {bad.Count} sockets don't snap: {string.Join("; ", bad.Take(6))}");
        // Across classes: refused, with the reason, by the build, the snap and the weld alike.
        var big = ConstructParts.Need("Capital Frame Node");
        var small = ConstructParts.Need("Square Beam 0.25 m");
        var b = w.Create(new Vector3(0, 300, 0), Quaternion.Identity)!;
        w.AddPart(b, big, Vector3.Zero, 0);
        b.Rebuild();
        var why2 = ConstructBuild.Place(b, 0, 0, small, 0, 0, out _);
        if (why2 == null || !why2.Contains("adapter"))
            Fail($"capital parts: a small part on a capital socket wasn't refused with the way round it (\"{why2}\")");
        // An adapter's front takes a small part, on the 1 cm grid.
        var adapter = ConstructParts.Need("Adapter Capital to Small");
        var a = ConstructBuild.Place(b, 0, 0, adapter, CapSocket(adapter, "back"), 0, out var ga);
        var ai = a == null ? ConstructBuild.Attach(w, b, adapter, ga) : -1;
        var beam = ai >= 0 ? CapAttach(w, b, ai, "front2.2", small, "-z", out var bw) : -1;
        if (beam < 0)
            Fail($"capital parts: a small beam didn't go on a capital node through an adapter ({a})");
        else
        {
            var p = b.Parts[beam].Pos * 100;
            if (MathF.Abs(p.X - MathF.Round(p.X)) > 0.02f || MathF.Abs(p.Y - MathF.Round(p.Y)) > 0.02f || MathF.Abs(p.Z - MathF.Round(p.Z)) > 0.02f)
                Fail($"capital parts: the small part through the adapter isn't on the 1 cm grid ({b.Parts[beam].Pos})");
        }
    }

    /// <summary>The player's whole path (lessons wa01): a capital hull on the yard; the adapter welded on, then a small wrench part taken from the menu, aimed at through the held-part frame and clicked on.</summary>
    private static void CapAdapterPath()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"capital parts: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("capital parts: no local player in the Sandbox game");
            return;
        }
        if (OpenSpot(g.Constructs) is not { } spot)
        {
            Fail("capital parts: no open ground on the yard");
            return;
        }
        var w = g.Constructs;
        var node = ConstructParts.Need("Medium Frame Node");
        var adapter = ConstructParts.Need("Adapter Medium to Small");
        var hull = ConstructBuild.PlaceNew(w, node, spot + new Vector3(0, 0.6f, 0), 0);
        if (hull == null)
        {
            Fail("capital parts: no medium node set down");
            return;
        }
        hull.Pinned = true;
        hull.Rebuild();
        var top = Array.FindIndex(node.Sockets, s => s.Dir == 2);
        var ai = ConstructBuild.Place(hull, 0, top, adapter, CapSocket(adapter, "back"), 0, out var ga) == null ? ConstructBuild.Attach(w, hull, adapter, ga) : -1;
        hull.Rebuild();
        // A wrench part comes to the hands from the menu, is aimed at the adapter and clicked on.
        var seat = ConstructParts.All.FirstOrDefault(d => d is { PhysicsOnly: false, Offered: true, Role: PartRole.Logic } && d.Sockets.Length > 0 && d.Name == "Lamp")
                   ?? ConstructParts.All.First(d => d is { PhysicsOnly: false, Offered: true, Role: PartRole.Logic } && d.Sockets.Length > 0);
        g.SandboxSend(CActKind.Tool, part: (byte)seat.Tool);
        g.SandboxTakePart(seat.Id);
        Run(g, ref now, 0.2);
        if (ai < 0 || g.SandboxHands.Hand(me.Id).Held != seat.Id)
        {
            Fail($"capital parts: the path didn't start (adapter {ai}, held {g.SandboxHands.Hand(me.Id).Held})");
            return;
        }
        var target = ConstructRender.PartCentre(hull, ai);
        StandBy(me, target, 2.0f);
        Run(g, ref now, 0.1);
        var eye = me.RenderEye;
        var dir = Vector3.Normalize(target - eye);
        var vp = Matrix4x4.CreateLookAt(eye, eye + dir, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 0.1f, 100f);
        Program.SandboxHeldFrame(g, eye, dir, new Vector2(1920, 1080), vp, false, now, out var sel, out var valid);
        var before = hull.PartCount;
        Program.SandboxHeldFrame(g, eye, dir, new Vector2(1920, 1080), vp, true, now + 0.02, out _, out _);
        Run(g, ref now, seat.AttachSeconds + 0.4);
        var on = hull.PartCount == before + 1;
        var linked = false;
        if (on)
            for (var e = hull.FirstLink(hull.PartCount - 1); e >= 0; e = hull.NextLink(e))
                linked |= hull.OtherEnd(e) == ai;
        Line($"capital parts: a {seat.Name} through the held-part path on an adapter on a medium node: ghost {sel} valid {valid}, attached {on}, linked to the adapter {linked}");
        if (sel < 0 || !valid || !on || !linked)
            Fail($"capital parts: the small part didn't go on the adapter by the player's path (ghost {sel}, valid {valid}, attached {on}, linked {linked})");
        // Straight on the node (no adapter): no ghost.
        g.SandboxTakePart(seat.Id);
        Run(g, ref now, 0.1);
        var nodeAt = ConstructRender.PartCentre(hull, 0);
        var eye2 = me.RenderEye;
        var dir2 = Vector3.Normalize(nodeAt - eye2);
        Program.SandboxHeldFrame(g, eye2, dir2, new Vector2(1920, 1080), vp, false, now, out var sel2, out _);
        var pickedNode = sel2 >= 0 && ConstructRender.Ghosts[sel2].Part == 0;
        if (pickedNode)
            Fail("capital parts: a small part offered a ghost straight on a medium node's socket");
    }

    /// <summary>The shipped example builds, a dome and a stack on a storey on columns, on its lattice.</summary>
    private static void CapBlueprint()
    {
        if (ConstructBuild.FindBlueprint("Town Block") is not { } bp || ConstructBuild.Build(new ConstructWorld(), bp, new Vector3(0, 40, 0), Quaternion.Identity, out var why) is not { } c)
        {
            Fail($"capital parts: the Town Block blueprint didn't build ({string.Join("; ", ConstructBuild.RefusedBlueprints)})");
            return;
        }
        c.Rebuild();
        var size = c.LocalMax - c.LocalMin;
        Line($"capital parts: Town Block: {c.PartCount} parts, {c.LinkCount} links, {c.Mass / 1000:0} t, {size.X:0}x{size.Y:0}x{size.Z:0} m");
        if (size.X > 17 || size.Z > 17 || size.Y < 30)
            Fail($"capital parts: Town Block came out {size.X:0}x{size.Y:0}x{size.Z:0} m (a part went on turned?)");
    }

    private static void CapBigBody()
    {
        var w = new ConstructWorld();
        var clock = Stopwatch.StartNew();
        var c = CapHull(w, new Vector3(0, 3, 0), 4000, out var why);
        var built = clock.Elapsed.TotalMilliseconds;
        if (c == null)
        {
            Fail($"capital parts: the 4,000-part hull didn't build: {why}");
            return;
        }
        c.Rebuild();
        double rebuild = double.MaxValue;
        for (var k = 0; k < 5; k++)
        {
            var t = Stopwatch.GetTimestamp();
            c.Rebuild();
            rebuild = Math.Min(rebuild, Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
        var size = c.LocalMax - c.LocalMin;
        var (boxes, walked, shaped) = ConstructRender.BatchCounts(c);
        var pw = new PacketWriter(1024);
        ConstructNet.WriteFull(pw, c);
        var full = pw.Length;
        var old = pw.Span[2..].ToArray();
        var kept = new int[Construct.MaxParts];
        ScaleNote($"capital body: {c.PartCount} parts, {c.LinkCount} links, {c.Mass / 1000:0} t, {size.X:0}x{size.Y:0}x{size.Z:0} m: built in {built:0} ms, rebuild {rebuild:0.00} ms, {full / 1024} KB whole, draws one batch of {boxes} boxes, {walked} parts walked and {shaped} contoured");
        if (c.PartCount < 3900 || c.Mass < 80_000_000f || size.X < 290)
            Fail($"capital parts: the test hull is too small ({c.PartCount} parts, {c.Mass / 1000:0} t, {size.X:0} m)");
        if (rebuild > 40)
            Fail($"capital parts: a rebuild of {c.PartCount} parts took {rebuild:0.0} ms");
        if (walked > 0 || boxes < c.PartCount)
            Fail($"capital parts: plain structure isn't all in the one draw ({boxes} boxes, {walked} parts walked)");
        if (!float.IsFinite(c.InvInertiaLocal.M11) || !(c.InvInertiaLocal.M11 > 0) || !(c.InvInertiaLocal.M22 > 0) || !(c.InvInertiaLocal.M33 > 0))
            Fail($"capital parts: the hull's inertia didn't invert ({c.InvInertiaLocal.M11}, {c.InvInertiaLocal.M22}, {c.InvInertiaLocal.M33})");

        // The tree answers what the walk does: a seeded sweep of rays and boxes against every part.
        var rng = new Random(7);
        var mismatches = 0;
        for (var k = 0; k < 40; k++)
        {
            var o = new Vector3(rng.NextSingle() * 320 - 10, 30, rng.NextSingle() * 110 - 2);
            var d = Vector3.Normalize(new Vector3(rng.NextSingle() - 0.5f, -1, rng.NextSingle() - 0.5f));
            var n = c.PartsOnRay(c.ToLocal(o), d, 80, kept);
            var near = new HashSet<int>(kept.Take(n));
            for (var i = 0; i < c.PartCount; i++)
            {
                c.PartBounds(i, out var mn, out var mx);
                if (Geometry.RayAabb(mn, mx, c.ToLocal(o), d, 80) != null && !near.Contains(i))
                    mismatches++;
            }
        }
        if (mismatches > 0)
            Fail($"capital parts: the part tree missed {mismatches} parts a ray meets");

        // On the ground, dragged by the physics gun.
        var q = new JointTestWorld();
        for (var t = 0; t < 40; t++)
            w.Step(q, World.TickDt);
        var settled = c.Pos;
        c.GrabbedBy = 0;
        c.GrabLocal = c.Com;
        c.GrabAccel = 2f;
        c.GrabRot = c.Rot;
        c.GrabTarget = c.WorldCom + new Vector3(4, 1.5f, 0);
        c.Wake();
        for (var t = 0; t < 30; t++)
            w.Step(q, World.TickDt);
        // The tick, steady and awake, a player standing on the deck, and rays.
        var ticks = new double[60];
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < ticks.Length; t++)
        {
            var s0 = Stopwatch.GetTimestamp();
            w.Step(q, World.TickDt);
            ticks[t] = Stopwatch.GetElapsedTime(s0).TotalMilliseconds;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Array.Sort(ticks);
        var moved = Vector3.Distance(c.Pos, settled);
        var up = Vector3.Transform(Vector3.UnitY, c.Rot).Y;
        var deckTop = c.ToWorld(new Vector3(40, c.LocalMax.Y, 20));
        var ms = new MoveState { Position = deckTop + new Vector3(0, 8, 0), Velocity = new Vector3(0, -1, 0) };
        var t1 = Stopwatch.GetTimestamp();
        Construct? stood = null;
        const int blocks = 400;
        for (var k = 0; k < blocks; k++)
        {
            ms.Position = deckTop + new Vector3(0, 0.02f, 0);
            ms.Velocity = new Vector3(0, -1, 0);
            stood = w.BlockPlayer(ref ms, 1.8f, true) ?? stood;
        }
        var blockUs = Stopwatch.GetElapsedTime(t1).TotalMicroseconds / blocks;
        var t2 = Stopwatch.GetTimestamp();
        const int rays = 2000;
        var hitsDown = 0;
        for (var k = 0; k < rays; k++)
            if (w.Raycast(c.ToWorld(new Vector3(k % 280, 40, k % 90)), -Vector3.UnitY, 80, out _))
                hitsDown++;
        var rayUs = Stopwatch.GetElapsedTime(t2).TotalMicroseconds / rays;
        ScaleNote($"capital body: tick median {ticks[ticks.Length / 2]:0.00} ms, worst {ticks[^1]:0.00} ms, {allocated} B allocated in {ticks.Length} ticks; dragged {moved:0.00} m, up {up:0.000}; a player's blocking {blockUs:0.0} us, a ray {rayUs:0.0} us ({hitsDown} of {rays} hit)");
        if (moved < 0.3f)
            Fail($"capital parts: the physics gun didn't drag the hull ({moved:0.00} m)");
        if (up < 0.99f || !float.IsFinite(c.Pos.X))
            Fail($"capital parts: the dragged hull tipped or blew up (up {up:0.000})");
        if (allocated > 0)
            Fail($"capital parts: {allocated} B allocated in {ticks.Length} ticks of the 4,000-part hull");
        if (TickCpu.Available && ticks[ticks.Length * 9 / 10] > 8)
            Fail($"capital parts: the 4,000-part hull's tick takes {ticks[ticks.Length * 9 / 10]:0.0} ms");
        if (ticks[ticks.Length / 2] > 8)
            Fail($"capital parts: the 4,000-part hull's median tick is {ticks[ticks.Length / 2]:0.0} ms (budget 8)");
        if (stood != c)
            Fail("capital parts: a player on the deck didn't stand on it");
        if (hitsDown < rays / 2)
            Fail($"capital parts: only {hitsDown} of {rays} rays down hit the deck");
        if (blockUs > 300 || rayUs > 200)
            Fail($"capital parts: blocking ({blockUs:0} us) or a ray ({rayUs:0} us) walks too much of the hull");
        // A small vehicle on the deck: what a mothership carries. (The body contacts are feat/collision's: this only measures them.)
        if (ConstructBuild.FindBlueprint("Buggy") is { } buggyBp && ConstructBuild.Build(w, buggyBp, c.ToWorld(new Vector3(60, c.LocalMax.Y + 1.2f, 40)), Quaternion.Identity, out _) is { } buggy)
        {
            c.GrabbedBy = 255;
            for (var t = 0; t < 60; t++)
                w.Step(q, World.TickDt);
            var ticks2 = new double[30];
            for (var t = 0; t < ticks2.Length; t++)
            {
                var s0 = Stopwatch.GetTimestamp();
                w.Step(q, World.TickDt);
                ticks2[t] = Stopwatch.GetElapsedTime(s0).TotalMilliseconds;
            }
            Array.Sort(ticks2);
            ScaleNote($"capital body: a buggy on the deck: tick median {ticks2[ticks2.Length / 2]:0.00} ms, worst {ticks2[^1]:0.00} ms; the buggy {buggy.Pos.Y - c.LocalMax.Y - c.Pos.Y:0.00} m above the deck");
            if (buggy.Pos.Y < c.Pos.Y + c.LocalMax.Y - 1f)
                Fail($"capital parts: a buggy fell through the deck (at {buggy.Pos.Y:0.0}, the deck at {c.Pos.Y + c.LocalMax.Y:0.0})");
            w.Remove(buggy);
        }
        // A patch is small; applied, it gives the new bytes.
        var plate = ConstructParts.Need(CapPlateName);
        var edge = c.PartCount - 2; // (the last deck: its side)
        var added = CapAttach(w, c, edge, "+x0", plate, "-x0", out var ew);
        c.Rebuild();
        ConstructNet.WriteFull(pw, c);
        var now = pw.Span[2..].ToArray();
        var patch = new PacketWriter(1024);
        ConstructPatch.Write(patch, c.Id, old, now);
        var reader = new PacketReader(patch.Span[2..]);
        reader.U16();
        reader.U32();
        reader.U32();
        var len = (int)reader.U32();
        var made = ConstructPatch.Apply(ref reader, old, len);
        ScaleNote($"capital body: one part added: {patch.Length} B patch against {now.Length} B whole");
        if (added < 0 || made == null || !made.AsSpan().SequenceEqual(now))
            Fail($"capital parts: a patch didn't rebuild the new bytes (added {added} {ew})");
        if (patch.Length > now.Length / 4)
            Fail($"capital parts: one part's patch is {patch.Length} B of {now.Length}");
        // Parts off the middle of it: it splits in linear time and what's left still answers.
        var t3 = Stopwatch.GetTimestamp();
        w.RemovePart(c, c.PartCount / 2);
        c.Rebuild();
        var removeMs = Stopwatch.GetElapsedTime(t3).TotalMilliseconds;
        Line($"capital body: a part off the middle: {removeMs:0.0} ms; {w.Bodies.Count} bodies");
        if (removeMs > 40)
            Fail($"capital parts: taking one part off took {removeMs:0.0} ms");
    }

    /// <summary>Tall stacks on a small deck (a traction city's skyline): dragged and stopped, they don't tip, and nothing shakes.</summary>
    private static void CapTall()
    {
        var w = new ConstructWorld();
        var deck = ConstructParts.Need("Large Chassis Deck 16x16 m");
        var stack = ConstructParts.Need("Large Smokestack 4x32 m");
        var c = w.Create(new Vector3(0, 3, 0), Quaternion.Identity)!;
        w.AddPart(c, deck, Vector3.Zero, 0);
        c.Rebuild();
        var placed = 0;
        foreach (var name in new[] { "top0.0", "top3.0", "top0.3", "top3.3" })
            if (CapAttach(w, c, 0, name, stack, "base0.0", out var why) >= 0)
                placed++;
            else
                Line($"capital tall: {name}: {why}");
        c.Rebuild();
        var q = new JointTestWorld();
        for (var t = 0; t < 120; t++)
            w.Step(q, World.TickDt);
        var rest = c.Pos;
        c.GrabbedBy = 0;
        c.GrabLocal = c.Com;
        c.GrabAccel = 2f;
        c.GrabRot = c.Rot;
        c.GrabTarget = c.WorldCom + new Vector3(3, 0.5f, 0);
        c.Wake();
        var least = 1f;
        var most = 0f;
        for (var t = 0; t < 200; t++)
        {
            w.Step(q, World.TickDt);
            least = MathF.Min(least, Vector3.Transform(Vector3.UnitY, c.Rot).Y);
            most = MathF.Max(most, c.AngVel.Length());
            if (t == 100)
                c.GrabbedBy = 255;
        }
        Line($"capital tall: {placed} stacks 32 m tall on a 16 m deck, {c.Mass / 1000:0} t, centre {c.Com.Y:0.0} m up: least upright {least:0.000}, most spin {most:0.000} rad/s, moved {Vector3.Distance(c.Pos, rest):0.00} m");
        if (placed < 4)
            Fail($"capital parts: only {placed} of 4 stacks went on the deck");
        if (least < 0.95f || most > 0.4f)
            Fail($"capital parts: the tall stacks tipped or shook (upright {least:0.000}, spin {most:0.000})");
    }

    /// <summary>
    /// "Capital network": a real host, a client and a late joiner over the transport. The 4,000-part hull reaches the client; an
    /// edit goes as a small patch and every copy matches the host's; a joiner gets it whole and later patches apply to its copy too.
    /// </summary>
    private static void CheckCapitalNetwork()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"capital network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26397;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue)
        {
            Fail("capital network: the client didn't join");
            return;
        }
        var w = server.Constructs;
        var hull = CapHull(w, new Vector3(-150, 300, -60), 4000, out var why);
        if (hull == null)
        {
            Fail($"capital network: the hull didn't build: {why}");
            return;
        }
        hull.Pinned = true;
        hull.Rebuild();
        var t0 = clock.Elapsed.TotalSeconds;
        bool Same(Game g) => g.Constructs.Get(hull.Id) is { } copy && ConstructNet.StructureHash(copy) == ConstructNet.StructureHash(hull);
        while (clock.Elapsed.TotalSeconds - t0 < 12 && !Same(a))
            Pump(server, [a], 0.1, ref now, clock);
        var arrive = clock.Elapsed.TotalSeconds - t0;
        ScaleNote($"capital network: the connected client had all {hull.PartCount} parts after {arrive:0.00} s");
        if (!Same(a))
        {
            Fail($"capital network: the client's copy of the hull differs ({a.Constructs.Get(hull.Id)?.PartCount} parts of {hull.PartCount}) after {arrive:0.0} s");
            return;
        }
        // An edit: a part added: a patch, not the whole body.
        var plate = ConstructParts.Need(CapPlateName);
        var added = CapAttach(w, hull, hull.PartCount - 2, "+x0", plate, "-x0", out var ew);
        var patches = a.Constructs.PatchesRead;
        var t1 = clock.Elapsed.TotalSeconds;
        hull.Rebuild();
        while (clock.Elapsed.TotalSeconds - t1 < 4 && !Same(a))
            Pump(server, [a], 0.05, ref now, clock);
        ScaleNote($"capital network: one part added reached the client in {clock.Elapsed.TotalSeconds - t1:0.00} s; patches read {a.Constructs.PatchesRead - patches}, misses {a.Constructs.PatchMisses}");
        if (added < 0 || !Same(a) || a.Constructs.PatchesRead == patches)
            Fail($"capital network: an edit didn't reach the client as a patch (added {added} {ew}, same {Same(a)}, patches {a.Constructs.PatchesRead - patches})");
        // A late joiner: the whole body, then a patch on it.
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        var t2 = clock.Elapsed.TotalSeconds;
        while (clock.Elapsed.TotalSeconds - t2 < 15 && !Same(b))
            Pump(server, [a, b], 0.1, ref now, clock);
        var join = clock.Elapsed.TotalSeconds - t2;
        ScaleNote($"capital network: a late joiner had the {hull.PartCount}-part hull after {join:0.00} s");
        if (!Same(b))
        {
            Fail($"capital network: the late joiner's copy differs ({b.Constructs.Get(hull.Id)?.PartCount} parts of {hull.PartCount}) after {join:0.0} s");
            return;
        }
        if (join > 8)
            Fail($"capital network: a late joiner took {join:0.0} s");
        // A part off: renumbers everything; both copies follow.
        w.RemovePart(hull, hull.PartCount / 2);
        hull.Rebuild();
        var t3 = clock.Elapsed.TotalSeconds;
        var hullId = hull.Id;
        while (clock.Elapsed.TotalSeconds - t3 < 6 && !(Same(a) && Same(b)))
            Pump(server, [a, b], 0.05, ref now, clock);
        Line($"capital network: a part off the middle: both copies match after {clock.Elapsed.TotalSeconds - t3:0.00} s; patches read {a.Constructs.PatchesRead}/{b.Constructs.PatchesRead}, misses {a.Constructs.PatchMisses}/{b.Constructs.PatchMisses}");
        if (!Same(a) || !Same(b))
            Fail("capital network: a part taken off didn't reach both copies");
        if (a.Constructs.PatchMisses + b.Constructs.PatchMisses > 2)
            Fail($"capital network: {a.Constructs.PatchMisses + b.Constructs.PatchMisses} patches didn't apply to their base");
        _ = hullId;
    }
}
