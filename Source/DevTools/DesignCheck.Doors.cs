using System.Diagnostics;
using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// The design check's doors (DesignCheck.cs): <c>--doors</c>. Every door element of the built body (DesignParts.Doors.cs) opened with F
/// (ConstructEdits.Use, the player's own path) and shut again, each settling back where it was built, its collider moved in place (no rebuild),
/// and the world step's cost while it settles; a docking hatch is the server's (StepDocks), commanded instead. Then a joined
/// client's copy of the body (its Full, then the host's door events through the wire) agreeing on every door. And what one door move costs the
/// renderer (none: a design's doors are drawn apart from its merged mesh, ConstructRenderDesign.cs, DesignMover; the merge it would have cost is timed).
/// </summary>
public static partial class DesignCheck
{
    private static void Doors(Run r)
    {
        var (w, c) = (r.World, r.Body);
        c.Frozen = true; // (held where it landed: the person walks a still ship)
        c.EnsureDoors();
        var gear = Enumerable.Range(0, c.PartCount).Count(i => c.DefOf(i).Door is { Gear: > 0 } && DesignParts.IsDesign(c.DefOf(i))); // (landing legs: the helm's gear key works them, not F; DesignCheck.Flight.cs --legs)
        var doors = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Door is { Gear: 0 } && DesignParts.IsDesign(c.DefOf(i))).ToList();
        string Name(int part) => ElementOf(r, part) is { } e ? $"{e.Id} ({e.Name})" : $"part {part}";
        Console.WriteLine($"[design] doors: {doors.Count} door elements ({c.DoorCount} event doors listed): {string.Join(", ", doors.Select(Name))}");
        if (doors.Count == 0)
        {
            Console.WriteLine("[design] doors: none to check");
            return;
        }
        if (c.DoorCount != doors.Count + gear)
            r.Fail($"doors: {doors.Count} door elements but {c.DoorCount} event doors (each must be one: segment 0, its def a door)");
        var edits = new ConstructEdits { Query = World.Query, InfiniteParts = true };
        Vector3 Mid(int part)
        {
            c.RestBounds(part, out var lo, out var hi);
            return (lo + hi) * 0.5f;
        }
        double Settle(int ticks)
        {
            var worst = 0.0;
            for (var t = 0; t < ticks; t++)
            {
                worst = Math.Max(worst, TimedStep(w));
            }
            return worst;
        }
        int Ticks(int part) => ConstructDoors.GroupTicks(c, part) + 20; // (a boarding hatch's ladder waits for its lid, and its lid for it: one F moves both in turn)
        // All shut to start with (the walk, before, opened the doors between rooms).
        var wasOpen = doors.Where(i => ConstructDoors.TargetOpen(c, i)).ToList();
        foreach (var i in wasOpen)
            ConstructDoors.Command(w, c, c.DoorOf(i), false);
        if (wasOpen.Count > 0)
            Settle(wasOpen.Max(Ticks));
        // ---- Each door: F (or the server's command for a docking hatch), open, then shut again where it was.
        var problems = new List<string>();
        var worstStep = 0.0;
        var rebuilt = 0;
        foreach (var part in doors)
        {
            var (pos0, rot0) = (c.Parts[part].Pos, c.Parts[part].Rot);
            var dock = c.DefOf(part).Door!.Dock;
            foreach (var open in new[] { true, false })
            {
                var version = c.Collider.Version;
                if (dock)
                    ConstructDoors.Command(w, c, c.DoorOf(part), open);
                else if (edits.Use(w, 1, c.ToWorld(Mid(part)), c.Id, part) is { } no)
                {
                    problems.Add($"{Name(part)}: F refused: {no}");
                    break;
                }
                worstStep = Math.Max(worstStep, Settle(Ticks(part)));
                c.EnsureDoors();
                if (ConstructDoors.SettledOpen(c, part) != open || ConstructDoors.IsMoving(c, part))
                    problems.Add($"{Name(part)}: didn't {(open ? "open" : "shut")} ({(ConstructDoors.IsMoving(c, part) ? "still moving" : "settled the other way")})");
                if (c.Collider.Version != version)
                    rebuilt++;
                if (open && c.Parts[part].Pos == pos0 && c.Parts[part].Rot == rot0)
                    problems.Add($"{Name(part)}: open, its part never moved");
            }
            if (c.Parts[part].Pos != pos0 || c.Parts[part].Rot != rot0)
                problems.Add($"{Name(part)}: shut again at {c.Parts[part].Pos} turn {c.Parts[part].Rot}, not where it was built ({pos0} turn {rot0})");
        }
        Console.WriteLine($"[design] doors: {doors.Count - problems.Count} of {doors.Count} opened and shut by F (docking hatches by the server); the collider built again {rebuilt} times in {2 * doors.Count} moves; the world step's worst while one settled {worstStep:0.00} ms");
        foreach (var p in problems)
            Console.WriteLine($"[design]   {p}");
        if (problems.Count > 0)
            r.Fail($"doors: {problems.Count} door(s) wouldn't open or shut");
        if (rebuilt > 0)
            r.Fail($"doors: {rebuilt} door moves built the whole collider again (a door leaf must move in place: box-only, segment 0)");
        if (worstStep > 8)
            r.Fail($"doors: a world step took {worstStep:0.0} ms while a door settled (at most 8)");
        PassRoomDoors(r, edits, Settle, Ticks);
        Client(r, doors, edits, Mid, Settle, Ticks);
        MergeCost(r);
    }

    /// <summary>A person walked (the server's own movement) from where they stand towards body point goal: reached, or where they stopped.</summary>
    private static (bool Ok, Vector3 At) WalkOn(Run r, ref MoveState s, Vector3 goal)
    {
        var (w, c) = (r.World, r.Body);
        var best = float.MaxValue;
        var lastBest = 0;
        for (var t = 0; t < 20 * World.TickRate; t++)
        {
            var l = c.ToLocal(s.Position);
            var dist = new Vector2(goal.X - l.X, goal.Z - l.Z).Length();
            if (l.Y < goal.Y - 1f)
                return (false, l); // (fell)
            if (dist < 0.35f && MathF.Abs(l.Y - goal.Y) < 0.6f)
                return (true, l);
            if (dist < best - 0.05f)
                (best, lastBest) = (dist, t);
            else if (t - lastBest > World.TickRate)
                return (false, l);
            var wd = c.ToWorld(goal) - s.Position;
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward, Yaw = MathF.Atan2(wd.X, -wd.Z), SpeedPct = 100 });
            w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
        }
        return (false, c.ToLocal(s.Position));
    }

    /// <summary>Each door between rooms: shut, it stops a person walking through its doorway; F on it, and they pass; F again shuts it.</summary>
    private static void PassRoomDoors(Run r, ConstructEdits edits, Func<int, double> settle, Func<int, int> ticks)
    {
        var (w, c) = (r.World, r.Body);
        var (n, passed) = (0, 0);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.DefOf(i).Door == null || ElementOf(r, i) is not { } e || !e.Id.StartsWith("door", StringComparison.Ordinal))
                continue;
            n++;
            c.RestBounds(i, out var lo, out var hi);
            var size = hi - lo;
            var across = size.X < size.Z ? Vector3.UnitX : Vector3.UnitZ; // (its thin way: through the wall)
            var foot = new Vector3((lo.X + hi.X) * 0.5f, lo.Y + 0.02f, (lo.Z + hi.Z) * 0.5f);
            Vector3 a = foot - across * 0.9f, b = foot + across * 0.9f;
            var s = new MoveState { Position = c.ToWorld(a) };
            for (var t = 0; t < 30; t++)
            {
                World.Simulate(ref s, new InputCmd { Buttons = Buttons.None, SpeedPct = 100 });
                w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
            }
            var start = s;
            var (shutOk, shutAt) = WalkOn(r, ref s, b);
            s = start;
            var eye = s.Position + Vector3.Transform(Vector3.UnitY * 1.6f, c.Rot);
            var no = edits.Use(w, 1, eye, c.Id, i);
            settle(ticks(i));
            var (openOk, openAt) = WalkOn(r, ref s, b);
            edits.Use(w, 1, s.Position + Vector3.Transform(Vector3.UnitY * 1.6f, c.Rot), c.Id, i);
            settle(ticks(i));
            c.EnsureDoors();
            var ok = !shutOk && no == null && openOk && !ConstructDoors.SettledOpen(c, i);
            passed += ok ? 1 : 0;
            if (!ok)
                Console.WriteLine($"[design]   {e.Id} ({e.Name}): shut, {(shutOk ? "walked through it" : $"stopped at {shutAt.X:0.00}, {shutAt.Z:0.00}")}; F {(no == null ? "opened it" : $"refused: {no}")}; then {(openOk ? "through" : $"stuck at {openAt.X:0.00}, {openAt.Z:0.00}")}; shut again {!ConstructDoors.SettledOpen(c, i)}");
        }
        if (n == 0)
            return;
        Console.WriteLine($"[design] doors between rooms: {passed} of {n} kept a person out shut, let them through open (F) and shut again (F)");
        if (passed < n)
            r.Fail($"doors: {n - passed} door(s) between rooms didn't keep out, let through or shut");
    }

    /// <summary>
    /// The walk's doors (DesignCheck.Walk.cs): every door between rooms (a placed RoomDoor's leaf, element "door{id}") opened with F from
    /// its doorway, a person's eye there, and settled, so the walk passes through them; a hatch cover stays shut (it is the floor walked over).
    /// </summary>
    private static void OpenRoomDoors(Run r)
    {
        var (w, c) = (r.World, r.Body);
        c.EnsureDoors();
        var edits = new ConstructEdits { Query = World.Query, InfiniteParts = true };
        var (n, opened, ticks) = (0, 0, 0);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.DefOf(i).Door == null || ElementOf(r, i) is not { } e || !e.Id.StartsWith("door", StringComparison.Ordinal))
                continue;
            n++;
            c.RestBounds(i, out var lo, out var hi);
            var foot = new Vector3((lo.X + hi.X) * 0.5f, lo.Y, (lo.Z + hi.Z) * 0.5f);
            var refused = edits.Use(w, 1, c.ToWorld(foot + new Vector3(0, 1.6f, 0)), c.Id, i);
            if (refused != null)
                Console.WriteLine($"[design] walk: F on {e.Id} ({e.Name}) refused: {refused}");
            else
                opened++;
            ticks = Math.Max(ticks, ConstructDoors.GroupTicks(c, i) + 20);
        }
        for (var t = 0; t < ticks; t++)
            w.Step(World.Query, World.TickDt);
        c.EnsureDoors();
        var open = Enumerable.Range(0, c.PartCount).Count(i => c.DefOf(i).Door != null && ElementOf(r, i)?.Id.StartsWith("door", StringComparison.Ordinal) == true && ConstructDoors.SettledOpen(c, i));
        Console.WriteLine($"[design] walk: {n} doors between rooms, {opened} opened with F, {open} open now");
        if (open < n)
            r.Fail($"walk: {n - open} door(s) between rooms didn't open with F");
    }

    private static DesignParts.Element? ElementOf(Run r, int part)
    {
        var name = r.Body.DefOf(part).Name;
        var id = name.IndexOf(") ", StringComparison.Ordinal) is var k and > 0 ? name[(k + 2)..] : name;
        return r.Els.FirstOrDefault(e => e.Id == id);
    }

    private static int PartOfElement(Run r, string id)
    {
        for (var i = 0; i < r.Body.PartCount; i++)
            if (ElementOf(r, i)?.Id == id)
                return i;
        return -1;
    }

    /// <summary>A joined client's copy (the host's Full of the body, then each door's move as the wire sends it) agreeing with the host on every door.</summary>
    private static void Client(Run r, List<int> doors, ConstructEdits edits, Func<int, Vector3> mid, Func<int, double> settle, Func<int, int> ticks)
    {
        var (w, c) = (r.World, r.Body);
        var full = new PacketWriter(1 << 20);
        ConstructNet.WriteFull(full, c, 1);
        var client = new ConstructWorld();
        var rd = new PacketReader(full.Span);
        rd.U8();
        ConstructNet.Read(ref rd, client, 0);
        if (client.Get(c.Id) is not { } copy || copy.PartCount != c.PartCount)
        {
            r.Fail("client: the body's Full didn't make its copy");
            return;
        }
        copy.EnsureDoors();
        // Half the doors opened on the host (F, or the server's command), their events sent as the server sends them.
        w.DoorEvents.Clear();
        var opened = doors.Where((_, k) => k % 2 == 0).ToList();
        foreach (var part in opened)
            if (c.DefOf(part).Door!.Dock)
                ConstructDoors.Command(w, c, c.DoorOf(part), true);
            else
                edits.Use(w, 1, c.ToWorld(mid(part)), c.Id, part);
        var sent = 0;
        void Send()
        {
            for (var from = 0; from < w.DoorEvents.Count;)
            {
                var pw = new PacketWriter(4096);
                from = ConstructDoors.Write(pw, w.DoorEvents, from);
                var pr = new PacketReader(pw.Span);
                pr.U8();
                ConstructNet.Read(ref pr, client, 0);
                sent++;
            }
            w.DoorEvents.Clear();
        }
        Send(); // (the starts)
        settle(opened.Select(ticks).DefaultIfEmpty(0).Max());
        c.EnsureDoors();
        Send(); // (the settles)
        copy.EnsureDoors();
        var differ = 0;
        foreach (var part in doors)
            if (ConstructDoors.SettledOpen(c, part) != ConstructDoors.SettledOpen(copy, part) || c.Parts[part].Pos != copy.Parts[part].Pos || c.Parts[part].Rot != copy.Parts[part].Rot)
            {
                if (differ++ < 4)
                    Console.WriteLine($"[design]   client: part {part} open {ConstructDoors.SettledOpen(c, part)} at {c.Parts[part].Pos} on the host, open {ConstructDoors.SettledOpen(copy, part)} at {copy.Parts[part].Pos} on the client");
            }
        Console.WriteLine($"[design] client: {opened.Count} doors opened on the host, {sent} door packets; {doors.Count - differ} of {doors.Count} doors alike on the client (open or shut, place and turn)");
        if (differ > 0)
            r.Fail($"client: {differ} door(s) differ between the host and a joined client");
        foreach (var part in opened)
            ConstructDoors.Command(w, c, c.DoorOf(part), false);
        settle(opened.Select(ticks).DefaultIfEmpty(0).Max());
        w.DoorEvents.Clear();
    }

    /// <summary>What a door move would cost the renderer were the doors in the merged mesh: the merge's CPU (every element's look moved into the body's frame), timed headless.</summary>
    private static void MergeCost(Run r)
    {
        var c = r.Body;
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (DesignParts.LookOf(c.Parts[i].D) is { } l)
                n += l.Vertices;
        var (pos, nor) = (new float[n * 3], new float[n * 3]);
        var best = double.MaxValue;
        for (var round = 0; round < 5; round++)
        {
            var s = Stopwatch.GetTimestamp();
            var at = 0;
            for (var i = 0; i < c.PartCount; i++)
            {
                if (DesignParts.LookOf(c.Parts[i].D) is not { } l)
                    continue;
                ref var p = ref c.Parts[i];
                var q = Rot24.Quat(p.Rot);
                for (var v = 0; v < l.Vertices; v++, at++)
                {
                    var wv = p.Pos + Vector3.Transform(new Vector3(l.Pos[v * 3], l.Pos[v * 3 + 1], l.Pos[v * 3 + 2]), q);
                    var nv = Vector3.Transform(new Vector3(l.Normal[v * 3], l.Normal[v * 3 + 1], l.Normal[v * 3 + 2]), q);
                    (pos[at * 3], pos[at * 3 + 1], pos[at * 3 + 2]) = (wv.X, wv.Y, wv.Z);
                    (nor[at * 3], nor[at * 3 + 1], nor[at * 3 + 2]) = (nv.X, nv.Y, nv.Z);
                }
            }
            best = Math.Min(best, Ms(s));
        }
        Console.WriteLine($"[design] render: a merge of the whole ship ({n / 3} triangles) takes {best:0.00} ms of CPU before its upload; a door move costs none of it (doors drawn apart, a draw each)");
    }
}
