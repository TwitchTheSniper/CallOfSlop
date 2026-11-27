using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// The design check's walk across the passages and into the extensions (<c>--walk</c>, package J). A person (the server's own movement) is
/// stood in each passage's first room in front of its door and walked through it, along the passage's line (each point of its floor,
/// DesignScene.PassageFrames) and through the other door into the room in the other body; and stood in each room with a wall extension and
/// walked into it (its opening through the room's wall) and on out at its far wall, which must stop them inside. Each one that doesn't is listed
/// with what was in the way. The ladders up the sockets' connector shafts and a lift passage's car are climbed and ridden with the risers
/// (DesignCheck.Walk.cs Risers).
/// </summary>
public static partial class DesignCheck
{
    private static void PassageWalk(Run r, ShipDesign d, DesignScene.Check check, ConstructWorld w, Construct c, Vector3 shift)
    {
        var laid = check.Passages.Where(p => p.L != null).ToList();
        var connectors = 0;
        for (var i = 0; i < check.Links.Count; i++)
            if (DesignScene.ConnectorOf(d, check, i) != null)
                connectors++;
        var problems = new List<string>();
        int crossed = 0, exts = 0, walled = 0;
        Vector3 Local(double[] p) => new Vector3((float)p[0], (float)p[1], (float)p[2]) + shift;
        string Fmt(Vector3 v) => $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})";
        string Named(int i) => c.DefOf(i).Name is var n && n.IndexOf(") ", StringComparison.Ordinal) is var k and > 0 ? n[(k + 2)..] : n;
        void Tick(ref MoveState m, Buttons bt, float yaw)
        {
            World.Simulate(ref m, new InputCmd { Buttons = bt, Yaw = yaw, SpeedPct = 100 });
            w.BlockPlayer(ref m, World.HeightOf(m.Stance), server: true, who: 1);
        }
        // Stands a person at a body point; null when they can't stand there.
        MoveState? Stand(Vector3 at)
        {
            var s = new MoveState { Position = c.ToWorld(at + new Vector3(0, 0.05f, 0)) };
            for (var t = 0; t < 30; t++)
                Tick(ref s, Buttons.None, 0);
            return s.Grounded ? s : null;
        }
        // Walks them through each point in turn (within 0.3 m of it across): null when they got there, else why not.
        string? Go(ref MoveState s, List<Vector3> legs)
        {
            var lowest = legs.Min(v => v.Y);
            for (var leg = 0; leg < legs.Count; leg++)
            {
                var to0 = legs[leg];
                var best = float.MaxValue;
                var lastBest = 0;
                for (var t = 0; ; t++)
                {
                    var at = c.ToLocal(s.Position);
                    var to = new Vector2(to0.X - at.X, to0.Z - at.Z);
                    var dist = to.Length();
                    if (dist < 0.3f)
                        break;
                    if (at.Y < lowest - 1f)
                        return $"fell to {Fmt(at - shift)} on the way to point {leg + 1} of {legs.Count}";
                    if (dist < best - 0.05f)
                        (best, lastBest) = (dist, t);
                    else if (t - lastBest > World.TickRate || t > 30 * World.TickRate)
                    {
                        var dir = Vector3.Normalize(new Vector3(to.X, 0, to.Y));
                        var hit = -1;
                        for (var step = 0.1f; step <= 0.5f && hit < 0; step += 0.1f)
                            hit = ConstructRide.BlockedBy(c, -1, c.ToWorld(at + dir * step + new Vector3(0, 0.02f, 0)), World.PlayerHeight);
                        return $"stuck {dist:0.0} m short of point {leg + 1} of {legs.Count} at {Fmt(at - shift)}: in the way {(hit >= 0 ? Named(hit) : "nothing a step on")}";
                    }
                    var wd = c.ToWorld(to0) - s.Position;
                    Tick(ref s, Buttons.Forward, MathF.Atan2(wd.X, -wd.Z));
                }
            }
            return null;
        }
        // A point in front of a room's door, inside the room (its wall's inward side), at its floor's top.
        double[] Inside(Room room, double[] door, double by)
        {
            var b = d.Box(room);
            double[] q = [door[0], b[2] + DesignScene.FloorTop(room), door[2]];
            var e = new[] { (Math.Abs(door[0] - b[0]), 0, 1.0), (Math.Abs(door[0] - b[1]), 0, -1.0), (Math.Abs(door[2] - b[4]), 2, 1.0), (Math.Abs(door[2] - b[5]), 2, -1.0) }.MinBy(t => t.Item1);
            q[e.Item2] += e.Item3 * by;
            return q;
        }
        if (d.Stacked && (laid.Count > 0 || d.Rooms.Any(x => x.Exts.Count > 0)))
            problems.Add("a stacked design's passages and extensions aren't walked yet");
        else
        {
            foreach (var (k, lay) in laid)
            {
                var (ra, rb) = (d.RoomById(k.A), d.RoomById(k.B));
                if (lay is not { } l || ra == null || rb == null)
                    continue;
                var name = $"the {k.Kind.ToLowerInvariant()} {k.Id} from {DesignScene.Name(ra)} to {DesignScene.Name(rb)}";
                var fr = DesignScene.PassageFrames(d, k, l);
                var legs = new List<Vector3> { Local(Inside(ra, l.DoorA, 0.1)) };
                for (var i = 1; i + 1 < fr.Count; i++)
                    legs.Add(Local(fr[i].Floor));
                legs.Add(Local(Inside(rb, l.DoorB, 0.1)));
                legs.Add(Local(Inside(rb, l.DoorB, 0.8)));
                if (Stand(Local(Inside(ra, l.DoorA, 0.8))) is not { } s)
                {
                    problems.Add($"{name}: can't stand in front of its first door");
                    continue;
                }
                if (Go(ref s, legs) is { } fail)
                    problems.Add($"{name}: {fail} (its points {string.Join(" ", legs.Select(v => Fmt(v - shift)))})");
                else
                {
                    crossed++;
                    Console.WriteLine($"[design]   {name}: crossed ({fr.Count} points, {Math.Max(0, l.Clear):0.00} m clear), standing at {Fmt(c.ToLocal(s.Position) - shift)} in {DesignScene.Name(rb)}");
                }
            }
            // The wall extensions: in through the opening to its middle, then on out at its far wall, which holds them inside.
            foreach (var room in d.Rooms)
                foreach (var e in room.Exts)
                {
                    if (room.ToHull || room.WallThick < 0.005)
                        continue;
                    if (e.Side == "up")
                    {
                        // An up one (a vault over the ceiling, which is left out): its roof is met just under its top, over the room's middle.
                        exts++;
                        var bx = d.Box(room);
                        var top = Local([(bx[0] + bx[1]) / 2, bx[3] + e.Depth - 0.2, (bx[4] + bx[5]) / 2]);
                        var hit = -1;
                        var own = $"room{d.Rooms.IndexOf(room)}";
                        for (var i = 0; i < c.PartCount && hit < 0; i++)
                        {
                            if (c.DefOf(i).Hulls is not { Length: > 0 } hulls || !(Named(i) == own || Named(i).StartsWith(own + ".", StringComparison.Ordinal)))
                                continue;
                            ref var p = ref c.Parts[i];
                            var lp = Vector3.Transform(top - p.Pos, Quaternion.Conjugate(Rot24.Quat(p.Rot)));
                            foreach (var h in hulls)
                            {
                                var far = float.MinValue;
                                for (var f = 0; f + 1 < h.FaceStart.Length; f++)
                                    far = MathF.Max(far, Vector3.Dot(h.N[f], lp - h.V[h.FaceIdx[h.FaceStart[f]]]));
                                if (far < 0.25f)
                                    hit = i;
                            }
                        }
                        if (hit >= 0)
                        {
                            walled++;
                            Console.WriteLine($"[design]   {DesignScene.Name(room)}'s {e.Shape.ToLowerInvariant()} over its ceiling: its roof met {e.Depth:0.00} m up ({Named(hit)})");
                        }
                        else
                            problems.Add($"{DesignScene.Name(room)}'s {e.Shape.ToLowerInvariant()} over its ceiling: no roof met {e.Depth:0.00} m up ({(hit >= 0 ? Named(hit) : "nothing")} there)");
                        continue;
                    }
                    exts++;
                    var name = $"{DesignScene.Name(room)}'s {e.Shape.ToLowerInvariant()} extension on its {e.Side} wall";
                    var (s0, s1, wall, so) = DesignScene.ExtSpan(room, e);
                    var b = d.Box(room);
                    var floor = b[2] + DesignScene.FloorTop(room);
                    var alongZ = e.Side is "x0" or "x1";
                    double[] P(double o) => alongZ ? [wall + so * o, floor, (s0 + s1) / 2] : [(s0 + s1) / 2, floor, wall + so * o];
                    if (Stand(Local(P(-1.0))) is not { } s)
                    {
                        problems.Add($"{name}: can't stand in front of it, in the room");
                        continue;
                    }
                    if (Go(ref s, [Local(P(e.Depth * 0.5))]) is { } fail)
                    {
                        problems.Add($"{name}: can't walk into it: {fail}");
                        continue;
                    }
                    var near = c.ToLocal(s.Position) - shift;
                    var outW = Go(ref s, [Local(P(e.Depth + 1.5))]);
                    var at = c.ToLocal(s.Position) - shift;
                    var past = so * ((alongZ ? at.X : at.Z) - wall);
                    if (outW == null || past > e.Depth)
                        problems.Add($"{name}: walked out through its far wall, to {Fmt(at)} ({past:0.00} m out, it is {e.Depth:0.00} m deep)");
                    else if (Math.Abs(at.Y - floor) > 0.2)
                        problems.Add($"{name}: not on its floor at {Fmt(at)} (the floor at {floor:0.00})");
                    else
                    {
                        walled++;
                        Console.WriteLine($"[design]   {name}: walked in to {Fmt(near)} and held by its far wall {past:0.00} m out (it is {e.Depth:0.00} m deep)");
                    }
                }
        }
        // The designer draws the very same walls: its scene's passages and connector links each with the game's mesh.
        if (laid.Count > 0 || connectors > 0)
            using (var scene = System.Text.Json.JsonDocument.Parse(DesignScene.Json(d)))
            {
                int pm = 0, cm = 0;
                foreach (var k in scene.RootElement.GetProperty("passages"u8).EnumerateArray())
                    pm += k.TryGetProperty("mesh"u8, out _) ? 1 : 0;
                foreach (var k in scene.RootElement.GetProperty("links"u8).EnumerateArray())
                    cm += k.TryGetProperty("mesh"u8, out _) ? 1 : 0;
                Console.WriteLine($"[design] the designer's scene: {pm} of {laid.Count} passage(s) and {cm} of {connectors} connector shaft(s) drawn as the game builds them");
                if (pm != laid.Count || cm != connectors)
                    problems.Add("the designer draws a passage or a connector shaft without the game's mesh");
            }
        if (laid.Count > 0 || exts > 0 || connectors > 0)
            Console.WriteLine($"[design] passages: crossed {crossed} of {laid.Count} ({check.Passages.Count} in the design); extensions walled {walled} of {exts}; {connectors} connector shaft(s) (their ladders are climbed with the risers)");
        foreach (var p in problems)
            Console.WriteLine($"[design]   {p}");
        if (problems.Count > 0)
            r.Fail($"the passages and extensions: {problems.Count} a person can't cross or walks out of");
    }
}
