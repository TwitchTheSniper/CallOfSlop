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
            foreach (var probe in new[] { at + dir * 0.45f + new Vector3(0, 0.3f, 0), at + dir * 0.45f + new Vector3(0, 1.2f, 0) })
                for (var i = 0; i < c.PartCount && hits.Count < 4; i++)
                {
                    c.RestBounds(i, out var lo, out var hi);
                    if (probe.X < lo.X || probe.Y < lo.Y || probe.Z < lo.Z || probe.X > hi.X || probe.Y > hi.Y || probe.Z > hi.Z)
                        continue;
                    var name = c.DefOf(i).Name;
                    var id = name.IndexOf(") ", StringComparison.Ordinal) is var k and > 0 ? name[(k + 2)..] : name;
                    var el = r.Els.FirstOrDefault(e => e.Id == id);
                    var what = el == null ? id : $"{id} ({el.Name})";
                    if (!hits.Contains(what))
                        hits.Add(what);
                }
            return hits.Count == 0 ? "nothing's bounds there" : string.Join(", ", hits);
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
            // Its middle, or (a hatch's hole there) the nearest place along it clear of every hole in its floor.
            double mx = (bx[0] + bx[1]) / 2, mz = (bx[4] + bx[5]) / 2;
            bool InHole(double x, double z) => mine.Any(q => DesignScene.IsHole(q) && Math.Abs(q[1] - bx[2]) < DesignScene.DoorLevel &&
                                                             Math.Abs(x - q[0]) < DesignScene.DoorHalf(q) + 0.5 && Math.Abs(z - q[2]) < DesignScene.DoorHalf(q) + 0.5);
            for (var k = 1; InHole(mx, mz) && k < 40; k++)
                mz = Math.Clamp((bx[4] + bx[5]) / 2 + (k % 2 == 0 ? 1 : -1) * 0.25 * ((k + 1) / 2), bx[4] + 0.5, bx[5] - 0.5);
            var mid = Local(mx, floor + 0.05, mz);
            var s = new MoveState { Position = c.ToWorld(mid) };
            for (var t = 0; t < 30; t++)
                Tick(ref s, Buttons.None, 0);
            var at = c.ToLocal(s.Position);
            if (!s.Grounded || MathF.Abs(at.Y - floor - shift.Y) > 0.3f)
            {
                problems.Add($"{name}: can't stand in its middle: ended at {Fmt(at - shift)} (floor {floor:0.00}), {(s.Grounded ? "standing" : "falling")}");
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
            DeckGravity(r, d, w, c, shift);
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
