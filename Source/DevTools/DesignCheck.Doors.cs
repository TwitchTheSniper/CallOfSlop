using System.Diagnostics;
using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// The design check's doors (DesignCheck.cs): <c>--doors</c>. Every door element of the built body (DesignParts.Doors.cs) opened with F
/// (ConstructEdits.Use, the player's own path) and shut again, each settling back where it was built, its collider moved in place (no rebuild),
/// and the world step's cost while it settles; a docking hatch is the server's (StepDocks), commanded instead. Then a person from outside
/// through the airlock: stood in its throat, F on the outer hatch, into the chamber, F on the inner door, on to the room's doorway. Then a joined
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
        Airlock(r, edits, Mid, Settle, Ticks);
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

    /// <summary>A person from outside the airlock to the room behind it, through both its doors by F.</summary>
    private static void Airlock(Run r, ConstructEdits edits, Func<int, Vector3> mid, Func<int, double> settle, Func<int, int> ticks)
    {
        var (w, c) = (r.World, r.Body);
        var d = ShipDesign.FromJson(r.Text);
        var pi = d.Pieces.FindIndex(p => PieceKinds.Find(p.Kind)?.Socket == "airlock");
        if (pi < 0)
        {
            Console.WriteLine("[design] airlock: the design has none");
            return;
        }
        var p = d.Pieces[pi];
        var el = r.Els.FirstOrDefault(e => e.Id == $"p{pi}" || e.Id == $"p{pi}.0");
        if (el == null)
        {
            r.Fail("airlock: no element for it");
            return;
        }
        int outer = PartOfElement(r, el.Id + ".d0"), inner = PartOfElement(r, el.Id + ".d1");
        if (outer < 0 || inner < 0)
        {
            r.Fail($"airlock: its doors aren't parts (outer {outer}, inner {inner})");
            return;
        }
        // The design's frame to the body's, as the walk has it: every element's part stands at its Origin, moved alike.
        var p0 = PartOfElement(r, r.Els[0].Id);
        var shift = c.Parts[p0].Pos - r.Els[0].Origin;
        var h = d.Hull(p.Hull)!;
        var place = DesignScene.Where(h, p);
        Vector3 V(double[] v) => new((float)v[0], (float)v[1], (float)v[2]);
        Vector3 o = V(place.O) - V(place.Y) * (float)p.Recess, x = V(place.X), y = V(place.Y);
        var down = -Vector3.UnitY;
        var floorSide = Vector3.Dot(x, down) > 0 ? x : -x; // (on a side wall its X is up: the chamber's floor is its -X side)
        var hx = (float)(p.AirH / 2 * p.Size * p.Hole);
        var chamberFloor = o + floorSide * hx;
        Vector3 At(float depth, float overFloor) => chamberFloor - y * depth - floorSide * overFloor + shift; // (body frame: depth in from the skin)
        string Fmt(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";
        var legs = new List<string>();
        var ok = true;
        void Tick(ref MoveState s, Buttons bt, Vector3 toward)
        {
            var wd = c.ToWorld(toward) - s.Position;
            World.Simulate(ref s, new InputCmd { Buttons = bt, Yaw = MathF.Atan2(wd.X, -wd.Z), SpeedPct = 100 });
            w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
        }
        // What is just ahead of a person at body point l, heading for goal: the parts whose rest bounds hold a point there at knee or chest height.
        string Near(Vector3 l, Vector3 goal)
        {
            var dir = goal - l;
            dir.Y = 0;
            dir = dir.LengthSquared() > 1e-6f ? Vector3.Normalize(dir) : Vector3.UnitX;
            var hits = new List<string>();
            foreach (var probe in new[] { l + dir * 0.45f + new Vector3(0, 0.3f, 0), l + dir * 0.45f + new Vector3(0, 1.2f, 0) })
                for (var i = 0; i < c.PartCount && hits.Count < 5; i++)
                {
                    c.RestBounds(i, out var lo, out var hi);
                    if (probe.X >= lo.X && probe.Y >= lo.Y && probe.Z >= lo.Z && probe.X <= hi.X && probe.Y <= hi.Y && probe.Z <= hi.Z && ElementOf(r, i) is { } e && !hits.Contains($"{e.Id} ({e.Name})"))
                        hits.Add($"{e.Id} ({e.Name})");
                }
            return hits.Count == 0 ? "nothing's bounds" : string.Join(", ", hits);
        }
        bool WalkTo(ref MoveState s, Vector3 goal, string what)
        {
            var best = float.MaxValue;
            var lastBest = 0;
            for (var t = 0; t < 20 * World.TickRate; t++)
            {
                var l = c.ToLocal(s.Position);
                var dist = new Vector2(goal.X - l.X, goal.Z - l.Z).Length();
                if (l.Y < goal.Y - 1f)
                {
                    legs.Add($"{what}: fell to {Fmt(l - shift)}");
                    return false;
                }
                if (dist < 0.35f && MathF.Abs(l.Y - goal.Y) < 0.6f)
                {
                    legs.Add($"{what}: reached {Fmt(l - shift)} in {t} ticks");
                    return true;
                }
                if (dist < best - 0.05f)
                    (best, lastBest) = (dist, t);
                else if (t - lastBest > World.TickRate)
                {
                    legs.Add($"{what}: stuck {dist:0.0} m short at {Fmt(l - shift)}, facing ({goal.X - l.X:0.0}, {goal.Z - l.Z:0.0}); in the way {Near(l, goal)}");
                    return false;
                }
                Tick(ref s, Buttons.Forward, goal);
            }
            legs.Add($"{what}: out of time");
            return false;
        }
        // 1. A person on the throat's sill at the skin, the hatch shut: it keeps them out. Then F on it from there, and in to the chamber.
        var depth = (float)(p.AirD * p.Size);
        // (just outside the skin, level with the throat's sill, stepping straight in: nothing outside holds a person up there)
        var s = new MoveState { Position = c.ToWorld(At(-0.3f, 0.05f)) };
        var sill = s;
        legs.Add($"outside the outer hatch at {Fmt(c.ToLocal(s.Position) - shift)}, stepping in");
        if (WalkTo(ref s, At(depth * 0.6f, 0), "into the chamber with the hatch shut"))
        {
            legs.Add("the shut outer hatch let them through");
            ok = false;
        }
        s = sill;
        var eyeOut = s.Position + Vector3.Transform(Vector3.UnitY * 1.6f, c.Rot);
        if (edits.Use(w, 1, eyeOut, c.Id, outer) is { } no)
        {
            legs.Add($"F on the outer hatch from outside refused: {no}");
            ok = false;
        }
        settle(ticks(outer));
        c.EnsureDoors();
        ok &= ConstructDoors.SettledOpen(c, outer);
        ok = ok && WalkTo(ref s, At(depth * 0.6f, 0), "into the chamber, the hatch open");
        // 2. The inner door, shut, keeps them in the chamber; then F on the outer hatch behind (shut) and the inner door ahead (open).
        if (ok)
        {
            var inChamber = s;
            if (WalkTo(ref s, At(depth + 0.5f, 0), "on with the inner door shut"))
            {
                legs.Add("the shut inner door let them through");
                ok = false;
            }
            s = inChamber;
        }
        var eye = s.Position + Vector3.Transform(Vector3.UnitY * 1.6f, c.Rot);
        if (ok && edits.Use(w, 1, eye, c.Id, outer) is { } no2)
            legs.Add($"F on the outer hatch from inside refused: {no2}");
        if (ok && edits.Use(w, 1, eye, c.Id, inner) is { } no3)
        {
            legs.Add($"F on the inner door refused: {no3}");
            ok = false;
        }
        settle(Math.Max(ticks(outer), ticks(inner)));
        c.EnsureDoors();
        if (ok && (ConstructDoors.SettledOpen(c, outer) || !ConstructDoors.SettledOpen(c, inner)))
        {
            legs.Add($"the doors didn't cycle: outer {(ConstructDoors.SettledOpen(c, outer) ? "open" : "shut")}, inner {(ConstructDoors.SettledOpen(c, inner) ? "open" : "shut")}");
            ok = false;
        }
        // 3. On through the inner door to the room's doorway the socket opens into (the nearest of the design's doorways to the airlock).
        if (ok)
        {
            var check = DesignScene.Run(d);
            var way = check.Doors.Where(q => !DesignScene.IsHole(q)).OrderBy(q => Vector3.Distance(V(q), o)).FirstOrDefault();
            if (way == null)
                legs.Add("no doorway into a room near it");
            else
            {
                var room = V(way) + shift;
                ok = WalkTo(ref s, At(depth + 0.4f, 0) with { Y = room.Y }, "through the inner door") && WalkTo(ref s, room - Vector3.Normalize(y with { Y = 0 }) * 0.6f, "into the room");
            }
        }
        Console.WriteLine($"[design] airlock from outside: {(ok ? "through" : "not through")}");
        foreach (var l in legs)
            Console.WriteLine($"[design]   {l}");
        if (!ok)
            r.Fail("airlock: a person from outside couldn't get through it to the room");
        // Both shut again, for what follows.
        foreach (var part in new[] { outer, inner })
            if (ConstructDoors.TargetOpen(c, part))
                ConstructDoors.Command(w, c, c.DoorOf(part), false);
        settle(Math.Max(ticks(outer), ticks(inner)));
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
