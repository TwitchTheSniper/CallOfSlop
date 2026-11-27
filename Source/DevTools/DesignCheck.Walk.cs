using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// The design check's walk (DesignCheck.cs): <c>--walk</c>, a person (the server's own movement, World.Simulate and ConstructWorld.BlockPlayer)
/// stood in each room of the built body, then walking from its middle to every doorway of it (<see cref="DesignScene.Check.Doors"/>: a door in
/// a wall, a hole through its floor), listing each it can't stand in or reach and what was in the way; then deck gravity (the helm's up while
/// the ship rolls). <c>--rooms-only</c>: the same on a body of only the rooms' elements, frozen in the air (the rooms' colliders on their own,
/// with no hull or piece in the way). Risers (ladders, lifts) are package F's.
/// </summary>
public static partial class DesignCheck
{
    private static void Walk(Run r)
    {
        var d = ShipDesign.FromJson(r.Text);
        var check = DesignScene.Run(d);
        var roomsOnly = r.Has("--rooms-only");
        var w = r.World;
        var c = r.Body;
        if (roomsOnly)
        {
            w = new ConstructWorld();
            c = w.Create(r.Ground + new Vector3(0, 40, 0), Quaternion.Identity)!; // (its own world: the built ship isn't in it; inside the map's bounds)
            var first = -1;
            foreach (var e in r.Els)
            {
                if (e.Kind != "floor" || DesignParts.Parse(DesignParts.KeyOf(r.Hash, e.Id)) is not { } def)
                    continue;
                var at = w.AddPart(c, def, e.Origin, e.Rot, 0);
                if (first < 0)
                    first = at;
                else
                    w.AddLink(c, first, 0, at, 0);
            }
            c.Rebuild();
            c.Frozen = true;
            w.Step(World.Query, World.TickDt); // (into the world's grid with its parts' bounds)
        }
        // The design's frame to the body's: every element's part stands at its Origin, moved alike.
        var p0 = r.Els.ToList().FindIndex(e => !roomsOnly || e.Kind == "floor");
        var shift = c.Parts[roomsOnly ? 0 : p0].Pos - r.Els[p0].Origin;
        Vector3 Local(double x, double y, double z) => new Vector3((float)x, (float)y, (float)z) + shift;
        if (!roomsOnly)
            OpenRoomDoors(r); // (its doors between rooms opened with F first: DesignCheck.Doors.cs)
        var problems = new List<string>();
        int stood = 0, rooms = 0, ways = 0, reached = 0;
        void Tick(ref MoveState s, Buttons bt, float yaw)
        {
            World.Simulate(ref s, new InputCmd { Buttons = bt, Yaw = yaw, SpeedPct = 100 });
            w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
        }
        string Fmt(Vector3 v) => $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})";
        // What is in the way just ahead of a person at a body point, facing dir: the elements whose parts' boxes hold it.
        string InWay(Vector3 at, Vector3 dir)
        {
            var hits = new List<string>();
            // (first the part whose colliders (hulls, or boxes) hold a person a step on: the one really in the way, not just its bounds)
            for (var step = 0.1f; step <= 0.5f && hits.Count == 0; step += 0.1f)
                if (ConstructRide.BlockedBy(c, -1, at + dir * step + new Vector3(0, 0.02f, 0), World.PlayerHeight) is var bi and >= 0)
                    hits.Add($"[collider] {Named(bi)}");
            // (the boxes a rider meets, Construct.PlayerSolids: the box and its middle)
            for (var i = 0; i < c.PartCount && hits.Count < 3; i++)
                for (var b = 0; b < c.PlayerSolids(i) && hits.Count < 3; b++)
                {
                    c.SolidBox(i, b, out var centre, out var half);
                    var rel = Vector3.Abs(at + dir * 0.25f + new Vector3(0, 0.9f, 0) - centre);
                    if (rel.X < half.X + World.BodyRadius && rel.Z < half.Z + World.BodyRadius && rel.Y < half.Y + 0.8f)
                        hits.Add($"[box {b} of {Named(i)}: {Fmt(centre - half - shift)}..{Fmt(centre + half - shift)}]");
                }
            // (and the skin's plates a rider meets as triangles, ConstructRide.AddSkin: the plate and its bounds)
            for (var i = 0; i < c.PartCount && hits.Count < 3; i++)
            {
                ref var p = ref c.Parts[i];
                if (!p.D.HasSkin || p.D.HullSkin is not { } skin)
                    continue;
                var hulls = ConstructHulls.Of(p.D);
                var rot = Rot24.Quat(p.Rot);
                for (var h = 0; h < hulls.Length && hits.Count < 3; h++)
                {
                    if (h >= skin.Length || !skin[h])
                        continue;
                    var hull = hulls[h];
                    var pt = at + dir * 0.25f;
                    var hit = false;
                    for (var y = 0.05f; y < World.PlayerHeight && !hit; y += 0.2f)
                    {
                        var lp = Vector3.Transform(pt + new Vector3(0, y, 0) - p.Pos, Quaternion.Conjugate(rot));
                        var far = float.MinValue;
                        for (var f = 0; f + 1 < hull.FaceStart.Length; f++)
                            far = MathF.Max(far, Vector3.Dot(hull.N[f], lp - hull.V[hull.FaceIdx[hull.FaceStart[f]]]));
                        hit = far < World.BodyRadius;
                    }
                    if (!hit)
                        continue;
                    Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
                    foreach (var v in hull.V)
                    {
                        var wv = p.Pos + Vector3.Transform(v, rot) - shift;
                        (lo, hi) = (Vector3.Min(lo, wv), Vector3.Max(hi, wv));
                    }
                    hits.Add($"[plate {h} of {Named(i)}: {Fmt(lo)}..{Fmt(hi)}]");
                }
            }
            foreach (var probe in new[] { at + dir * 0.45f + new Vector3(0, 0.3f, 0), at + dir * 0.45f + new Vector3(0, 1.2f, 0) })
                for (var i = 0; i < c.PartCount && hits.Count < 4; i++)
                {
                    c.RestBounds(i, out var lo, out var hi);
                    if (probe.X < lo.X || probe.Y < lo.Y || probe.Z < lo.Z || probe.X > hi.X || probe.Y > hi.Y || probe.Z > hi.Z)
                        continue;
                    var what = Named(i);
                    if (!hits.Contains(what))
                        hits.Add(what);
                }
            return hits.Count == 0 ? "nothing's bounds there" : string.Join(", ", hits);
        }
        string Named(int i)
        {
            var name = c.DefOf(i).Name;
            var id = name.IndexOf(") ", StringComparison.Ordinal) is var k and > 0 ? name[(k + 2)..] : name;
            var el = r.Els.FirstOrDefault(e => e.Id == id);
            return el == null ? id : $"{id} ({el.Name})";
        }
        for (var ri = 0; ri < d.Rooms.Count; ri++)
        {
            var room = d.Rooms[ri];
            var bx = d.Box(room);
            if (d.Stacked)
            {
                problems.Add($"{DesignScene.Name(room)}: a stacked design's rooms aren't walked yet");
                continue;
            }
            rooms++;
            var name = $"{DesignScene.Name(room)} (deck {room.Deck + 1})";
            var floor = (float)(bx[2] + DesignScene.FloorTop(room));
            var mine = DesignScene.DoorsOf(d, room, check.Doors);
            // Its middle, or (a hatch's hole there, a ladder's rails) the nearest place in it clear of every hole in its floor and of every box a
            // person meets (a riser's rails stand in a shaft room: a start behind them walks into them, not round them).
            double mx = (bx[0] + bx[1]) / 2, mz = (bx[4] + bx[5]) / 2;
            bool InHole(double x, double z, double margin) => mine.Any(q => DesignScene.IsHole(q) && Math.Abs(q[1] - bx[2]) < DesignScene.DoorLevel &&
                                                             Math.Abs(x - q[0]) < DesignScene.DoorHalf(q) + margin && Math.Abs(z - q[2]) < DesignScene.DoorHalf(q) + margin);
            bool Boxed(double x, double z)
            {
                var col = Local(x, floor, z);
                for (var i = 0; i < c.PartCount; i++)
                    for (var b = 0; b < c.PlayerSolids(i); b++)
                    {
                        c.SolidBox(i, b, out var centre, out var half);
                        if (centre.Y - half.Y > col.Y + World.PlayerHeight || centre.Y + half.Y < col.Y + 0.1f)
                            continue; // (the floor under the feet, or over the head)
                        if (MathF.Abs(col.X - centre.X) < half.X + World.BodyRadius + 0.05f && MathF.Abs(col.Z - centre.Z) < half.Z + World.BodyRadius + 0.05f)
                            return true;
                    }
                return false;
            }
            if (InHole(mx, mz, 0.5) || Boxed(mx, mz))
            {
                var bestD = double.MaxValue;
                (double X, double Z) pick = (mx, mz);
                foreach (var margin in new[] { 0.5, 0.3, 0.15, 0.05 })
                {
                    for (var x = bx[0] + 0.4; x <= bx[1] - 0.4 + 1e-6; x += 0.05)
                        for (var z = bx[4] + 0.4; z <= bx[5] - 0.4 + 1e-6; z += 0.05)
                        {
                            var dd = (x - mx) * (x - mx) + (z - mz) * (z - mz);
                            if (dd < bestD && !InHole(x, z, margin) && !Boxed(x, z))
                                (bestD, pick) = (dd, (x, z));
                        }
                    if (bestD < double.MaxValue)
                        break;
                }
                (mx, mz) = pick;
            }
            var mid = Local(mx, floor + 0.05, mz);
            var s = new MoveState { Position = c.ToWorld(mid) };
            for (var t = 0; t < 30; t++)
                Tick(ref s, Buttons.None, 0);
            var at = c.ToLocal(s.Position);
            if (!s.Grounded || MathF.Abs(at.Y - floor - shift.Y) > 0.3f)
            {
                problems.Add($"{name}: can't stand in it: from {mx:0.00}, {mz:0.00} (holes {string.Join(" ", mine.Where(DesignScene.IsHole).Select(q => $"{q[0]:0.0},{q[2]:0.0} +-{DesignScene.DoorHalf(q):0.00}"))}) ended at {Fmt(at - shift)} (floor {floor:0.00}), {(s.Grounded ? "standing" : "falling")}");
                continue;
            }
            stood++;
            var start = s;
            foreach (var q in mine)
            {
                var hole = DesignScene.IsHole(q);
                if (hole && Math.Abs(q[1] - bx[2]) > DesignScene.DoorLevel)
                    continue; // (a hole in its ceiling: the room over it walks to it)
                ways++;
                var what = hole ? $"the hole at {q[0]:0.0}, {q[2]:0.0}" : $"the door at {q[0]:0.0}, {q[2]:0.0}";
                var goal = Local(q[0], floor, q[2]);
                s = start;
                var from = c.ToLocal(s.Position);
                // Round the holes in its floor on the way (a person walks round a hatch's well, not over it), each passed on the side in the room.
                var legs = new List<Vector3>();
                var line = new Vector2(goal.X - from.X, goal.Z - from.Z);
                if (line.Length() > 0.01f)
                {
                    var dir2 = Vector2.Normalize(line);
                    var side = new Vector2(-dir2.Y, dir2.X);
                    foreach (var o in mine.Where(o => o != q && DesignScene.IsHole(o) && Math.Abs(o[1] - bx[2]) < DesignScene.DoorLevel)
                                 .OrderBy(o => Vector2.Dot(new Vector2((float)o[0] + shift.X - from.X, (float)o[2] + shift.Z - from.Z), dir2)))
                    {
                        var oc = new Vector2((float)o[0] + shift.X, (float)o[2] + shift.Z);
                        var along = Vector2.Dot(oc - new Vector2(from.X, from.Z), dir2);
                        var off = Vector2.Dot(oc - new Vector2(from.X, from.Z), side);
                        var clear = (float)DesignScene.DoorHalf(o) + 0.5f;
                        if (along < 0 || along > line.Length() || MathF.Abs(off) > clear)
                            continue;
                        foreach (var sg in off > 0 ? new[] { -1f, 1f } : new[] { 1f, -1f })
                        {
                            var wp = oc + side * sg * clear;
                            if (wp.X - shift.X > bx[0] + 0.35 && wp.X - shift.X < bx[1] - 0.35 && wp.Y - shift.Z > bx[4] + 0.35 && wp.Y - shift.Z < bx[5] - 0.35)
                            {
                                legs.Add(new Vector3(wp.X - dir2.X * clear, floor + shift.Y, wp.Y - dir2.Y * clear));
                                legs.Add(new Vector3(wp.X + dir2.X * clear, floor + shift.Y, wp.Y + dir2.Y * clear));
                                break;
                            }
                        }
                    }
                }
                legs.Add(goal);
                string? fail = null;
                for (var leg = 0; leg < legs.Count && fail == null; leg++)
                {
                    var to0 = legs[leg];
                    var last = leg == legs.Count - 1;
                    var best = float.MaxValue;
                    var lastBest = 0;
                    for (var t = 0; ; t++)
                    {
                        var l = c.ToLocal(s.Position);
                        var to = new Vector2(to0.X - l.X, to0.Z - l.Z);
                        var dist = to.Length();
                        if (dist < (last && hole ? (float)DesignScene.DoorHalf(q) : 0.3f))
                            break; // (at the doorway, or over the hole: through it)
                        if (l.Y < from.Y - 1f)
                        {
                            var well = mine.FirstOrDefault(o => DesignScene.IsHole(o) && Math.Abs(l.X - shift.X - o[0]) < DesignScene.DoorHalf(o) + 0.3 && Math.Abs(l.Z - shift.Z - o[2]) < DesignScene.DoorHalf(o) + 0.3);
                            fail = $"fell {from.Y - l.Y:0.0} m at {Fmt(l - shift)}" + (well == null ? "" : $", through the hole at {well[0]:0.0}, {well[2]:0.0} ({2 * DesignScene.DoorHalf(well):0.0} m across: a hatch's well, which its cover closes once hatches are doors)");
                            break;
                        }
                        if (dist < best - 0.05f)
                            (best, lastBest) = (dist, t);
                        else if (t - lastBest > World.TickRate || t > 30 * World.TickRate)
                        {
                            var dir = Vector3.Normalize(new Vector3(to.X, 0, to.Y));
                            fail = $"stuck {dist:0.0} m short at {Fmt(l - shift)}: in the way {InWay(l, dir)}";
                            break;
                        }
                        var wd = c.ToWorld(to0) - s.Position;
                        Tick(ref s, Buttons.Forward, MathF.Atan2(wd.X, -wd.Z));
                    }
                }
                if (fail == null)
                    reached++;
                else
                    problems.Add($"{name}: can't reach {what}: {fail}");
            }
        }
        Console.WriteLine($"[design] walk{(roomsOnly ? " (the rooms alone)" : "")}: stood in {stood} of {rooms} rooms, reached {reached} of {ways} doorways ({check.Doors.Count} in the design)");
        foreach (var p in problems)
            Console.WriteLine($"[design]   {p}");
        if (problems.Count > 0)
            r.Fail($"the walk: {problems.Count} place(s) a person can't stand in or reach");
        if (!roomsOnly)
        {
            Risers(r, w, c);
            DeckGravity(r, d, w, c, shift);
        }
    }

    /// <summary>
    /// The risers (package F): every ladder of the body climbed up from its foot and down from its top as F starts it (LadderCheck.Try, the
    /// movement step's climb), and every lift ridden: a person stood on its car at the bottom, F on the car each stop up to the top and then
    /// back to the bottom, the world stepped (the pistons' motors, the rider carried by the car's segment) until the car rests, the rider
    /// checked standing on it at each stop.
    /// </summary>
    private static void Risers(Run r, ConstructWorld w, Construct c)
    {
        var problems = new List<string>();
        int ladders = 0, lifts = 0, climbs = 0, rides = 0;
        ConstructRide.Bodies = w;
        string Id(int i) => c.DefOf(i).Name is var n && n.IndexOf(") ", StringComparison.Ordinal) is var k and > 0 ? n[(k + 2)..] : n;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.LadderTop <= 0)
                continue;
            ladders++;
            ConstructRide.SettleWay(c, i); // (its hatches open, as F on it leaves them)
            foreach (var down in new[] { false, true })
            {
                var why = LadderCheck.Try(w, c, i, down);
                if (why == null)
                    climbs++;
                else
                    problems.Add($"{Id(i)} ({d.LadderTop - d.LadderFoot:0.0} m): can't climb {(down ? "down" : "up")} it: {why}");
            }
        }
        // The lifts: their cars.
        Span<int> slots = stackalloc int[Construct.MaxJoints];
        for (var i = 0; i < c.PartCount; i++)
        {
            if (!Id(i).EndsWith(".car", StringComparison.Ordinal) || !DesignParts.IsLift(c.DefOf(i)))
                continue;
            lifts++;
            var n = DesignParts.LiftJoints(c, i, slots);
            if (n == 0)
            {
                problems.Add($"{Id(i)}: its rams aren't on the body");
                continue;
            }
            var chain = slots[..n].ToArray();
            DesignParts.LiftTo(c, chain, 0);
            for (var t = 0; t < 30 * World.TickRate && chain.Any(j => c.Joints[j].Angle > 0.01f); t++)
                w.Step(World.Query, World.TickDt);
            // On its plate, in the middle.
            c.SolidBox(i, 0, out var centre, out var half);
            c.SegPose(c.Parts[i].Seg, out var sq, out var st);
            var plateTop = st + Vector3.Transform(centre + new Vector3(0, half.Y, 0), sq);
            var s = new MoveState { Position = c.ToWorld(plateTop + new Vector3(0, 0.05f, 0)) };
            void Tick(ref MoveState m)
            {
                World.Simulate(ref m, new InputCmd { Buttons = Buttons.None, SpeedPct = 100, RideBody = m.RideBody });
                w.BlockPlayer(ref m, World.HeightOf(m.Stance), server: true, who: 1);
            }
            for (var t = 0; t < 30; t++)
                Tick(ref s);
            var order = Enumerable.Range(1, n).Concat([0]).ToArray();
            foreach (var stop in order)
            {
                var eye = s.Position + new Vector3(0, 1.6f, 0);
                var said = DesignParts.LiftUse(c, i, eye);
                if (said is { Length: > 0 })
                {
                    problems.Add($"{Id(i)}: F on its car for stop {stop} said \"{said}\"");
                    break;
                }
                if (DesignParts.LiftStop(c, chain) != stop)
                {
                    problems.Add($"{Id(i)}: F on its car sent it to stop {DesignParts.LiftStop(c, chain)}, not {stop}");
                    break;
                }
                var ticks = 0;
                var worst = 0f;
                for (; ticks < 40 * World.TickRate; ticks++)
                {
                    w.Step(World.Query, World.TickDt);
                    Tick(ref s);
                    c.SegPose(c.Parts[i].Seg, out sq, out st);
                    var top = (st + Vector3.Transform(centre + new Vector3(0, half.Y, 0), sq)).Y;
                    worst = MathF.Max(worst, MathF.Abs(c.ToLocal(s.Position).Y - top));
                    var resting = true;
                    for (var k = 0; k < n; k++)
                        resting &= MathF.Abs(c.Joints[chain[k]].Angle - (k < stop ? c.Joints[chain[k]].Max : 0)) < 0.01f;
                    if (resting && ticks > 10)
                        break;
                }
                c.SegPose(c.Parts[i].Seg, out sq, out st);
                var plate = (st + Vector3.Transform(centre + new Vector3(0, half.Y, 0), sq)).Y;
                var feet = c.ToLocal(s.Position).Y;
                Console.WriteLine($"[design]   {Id(i)} to stop {stop}: {ticks / (float)World.TickRate:0.0} s, its plate at {plate:0.00} m, the rider's feet {feet - plate:+0.00;-0.00} m off it (at worst {worst:0.00} m), riding segment {s.RideOn}");
                if (ticks >= 40 * World.TickRate)
                    problems.Add($"{Id(i)}: the car never came to rest at stop {stop}");
                else if (MathF.Abs(feet - plate) > 0.1f || worst > 0.3f)
                    problems.Add($"{Id(i)}: the rider wasn't carried to stop {stop} (feet {feet - plate:+0.00;-0.00} m off its plate, {worst:0.00} m at worst)");
                else
                    rides++;
            }
        }
        // The designer draws the same risers: its scene's risers each with their mesh (a lift's car too), and the shafts' ladders.
        using (var scene = System.Text.Json.JsonDocument.Parse(DesignScene.Json(ShipDesign.FromJson(r.Text))))
        {
            var rs = scene.RootElement.GetProperty("risers");
            int meshed = 0, cars = 0;
            foreach (var k in rs.EnumerateArray())
                (meshed, cars) = (meshed + (k.TryGetProperty("mesh", out _) ? 1 : 0), cars + (k.TryGetProperty("car", out _) ? 1 : 0));
            Console.WriteLine($"[design] the designer's scene: {rs.GetArrayLength()} riser(s), {meshed} drawn as the game's parts ({cars} with a car), {scene.RootElement.GetProperty("shaftLadders").GetArrayLength()} shaft ladder(s)");
            if (meshed != rs.GetArrayLength())
                problems.Add($"the designer draws {rs.GetArrayLength() - meshed} riser(s) without the game's mesh");
        }
        Console.WriteLine($"[design] risers: {ladders} ladder(s) ({climbs} of {2 * ladders} climbs), {lifts} lift(s) ({rides} stops ridden)");
        foreach (var p in problems)
            Console.WriteLine($"[design]   {p}");
        if (problems.Count > 0)
            r.Fail($"the risers: {problems.Count} climb(s) or ride(s) that didn't work");
    }

    /// <summary>
    /// Deck gravity aboard (docs/sandbox/SHIP-GRAVITY.md): a person stood on the bridge's floor is in the body's frame, their up the helm's
    /// (ConstructRide.UpFor), and stays so while the ship (held still, not anchored) rolls 40 degrees under them: their feet keep the deck.
    /// </summary>
    private static void DeckGravity(Run r, ShipDesign d, ConstructWorld w, Construct c, Vector3 shift)
    {
        var room = d.Rooms.FirstOrDefault(q => q.Kind == "Bridge") ?? d.Rooms.FirstOrDefault();
        if (room == null || d.Stacked)
            return;
        if (c.DriveSeat < 0)
        {
            Console.WriteLine("[design] deck gravity: no helm drives it, so up is the body's face areas' (RideAxes): not checked");
            return;
        }
        var bx = d.Box(room);
        var floor = (float)(bx[2] + DesignScene.FloorTop(room));
        var mid = new Vector3((float)(bx[0] + bx[1]) / 2, floor + 0.05f, (float)(bx[4] + bx[5]) / 2) + shift;
        var (pos0, rot0, frozen0) = (c.Pos, c.Rot, c.Frozen);
        c.Frozen = true;
        c.Pos += new Vector3(0, 30, 0); // (off the ground, held where it is)
        var helmUp = ConstructRide.UpFor(c, Vector3.UnitY);
        var tilted = ConstructRide.UpFor(c, Vector3.Normalize(new Vector3(1, 1, 0))); // (a helm's up whatever the world's)
        var s = new MoveState { Position = c.ToWorld(mid) };
        void Tick(ref MoveState st)
        {
            World.Simulate(ref st, new InputCmd { Buttons = Buttons.None, SpeedPct = 100, RideBody = c.Id });
            w.BlockPlayer(ref st, World.HeightOf(st.Stance), server: true, who: 1);
        }
        for (var t = 0; t < 30; t++)
            Tick(ref s);
        var inFrame = s.RideBody == c.Id && s.RideUp != Vector3.Zero;
        var upOk = inFrame && Vector3.Dot(Vector3.Normalize(s.RideUp), helmUp) > 0.995f;
        // The ship rolls 20 degrees a second for two seconds, the person carried in its frame.
        var worst = 0f;
        for (var t = 0; t < 2 * World.TickRate; t++)
        {
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 20f * MathF.PI / 180f * World.TickDt);
            var beforeCom = c.WorldCom;
            c.Rot = Quaternion.Normalize(turn * c.Rot);
            c.Pos += beforeCom - c.WorldCom; // (about its centre of mass)
            if (s.RideBody == c.Id && s.RideUp != Vector3.Zero)
                ConstructRide.Place(ref s, c);
            Tick(ref s);
            var want = Vector3.Transform(helmUp, c.Rot);
            if (s.RideBody == c.Id)
                worst = MathF.Max(worst, MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(s.RideUpW), want), -1, 1)) * 180 / MathF.PI);
        }
        var local = c.ToLocal(s.Position);
        var drift = new Vector2(local.X - mid.X, local.Z - mid.Z).Length();
        var onDeck = s.RideBody == c.Id && MathF.Abs(local.Y - floor - shift.Y) < 0.3f && drift < 0.5f;
        Console.WriteLine($"[design] deck gravity ({ShipGravity.Mode}): in {DesignScene.Name(room)} in its frame {inFrame}, up {Fmt3(s.RideUp)} (the helm's {Fmt3(helmUp)}, asked with the world tilted {Fmt3(tilted)}); " +
                          $"rolled 40 degrees: up off the helm's by at most {worst:0.0} degrees, feet {local.Y - shift.Y - floor:0.00} m over the floor, {drift:0.00} m from where they stood, felt {Fmt3(s.RideFelt)} (on {s.RideFeltOn})");
        if (!inFrame || !upOk || Vector3.Dot(tilted, helmUp) < 0.999f)
            r.Fail("deck gravity: the person on the bridge isn't in the ship's frame with the helm's up");
        if (!onDeck || worst > 5f)
            r.Fail("deck gravity: rolling the ship took the person off its deck");
        (c.Pos, c.Rot, c.Frozen) = (pos0, rot0, frozen0);
    }

    private static string Fmt3(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";
}
