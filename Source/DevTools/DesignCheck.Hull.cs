using System.Diagnostics;
using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>The design check's hollow hull (DesignCheck.cs, <c>--hollow</c>): what its colliders came to, and a person standing in each room.</summary>
public static partial class DesignCheck
{
    private const float PersonRadius = 0.35f, PersonHeight = 1.8f;

    /// <summary>
    /// The hull's colliders (DesignParts.LastHollow), then a capsule a person's size stood on each room's floor at its middle: every collider
    /// it touches is named (none should: the hull is plates round the rooms, and pieces are kept out of them).
    /// </summary>
    private static void Hollow(Run r)
    {
        Console.WriteLine($"[design] hollow hull: {DesignParts.LastHollow}");
        var d = ShipDesign.FromJson(r.Text);
        if (d.Stacked)
        {
            Console.WriteLine("[design] hollow: a stacked design's rooms aren't stood in (its decks are along z)");
            return;
        }
        var clear = 0;
        for (var ri = 0; ri < d.Rooms.Count; ri++)
        {
            var room = d.Rooms[ri];
            var b = d.Box(room);
            var (x, z, floor, ceiling) = ((float)(b[0] + b[1]) / 2, (float)(b[4] + b[5]) / 2, (float)b[2], (float)b[3]);
            var top = MathF.Min(floor + PersonHeight, ceiling - 0.05f);
            var foot = new Vector3(x, floor + PersonRadius + 0.05f, z);
            var head = new Vector3(x, MathF.Max(foot.Y, top - PersonRadius), z);
            var hits = new List<string>();
            foreach (var e in r.Els)
            {
                var back = Quaternion.Conjugate(Rot24.Quat(e.Rot));
                for (var hi = 0; hi < e.Hulls.Length; hi++)
                    for (var s = 0; s <= 8; s++)
                    {
                        var q = Vector3.Transform(Vector3.Lerp(foot, head, s / 8f) - e.Origin, back);
                        if (Depth(e.Hulls[hi], q) < PersonRadius)
                        {
                            hits.Add($"{e.Id} ({e.Name}) hull {hi}");
                            break;
                        }
                    }
            }
            if (hits.Count == 0)
                clear++;
            else
                Console.WriteLine($"[design] hollow: a person in room {ri} ({DesignScene.Name(room)}) at {x:0.0}, {floor:0.0}, {z:0.0} touches {string.Join(", ", hits.Take(6))}{(hits.Count > 6 ? $" and {hits.Count - 6} more" : "")}");
        }
        Console.WriteLine($"[design] hollow: {clear} of {d.Rooms.Count} rooms hold a standing person touching no collider");
        if (clear < d.Rooms.Count)
            r.Fail($"{d.Rooms.Count - clear} rooms where a standing person touches a collider");
        // Each socket's opening: how far the middle of its hole is from the skin's plates (open: past a person's radius), and from any piece's collider.
        foreach (var p in d.Pieces)
        {
            if (PieceKinds.Find(p.Kind)?.Opening is not { } op || d.Hull(p.Hull) is not { } h)
                continue;
            var w = DesignScene.Where(h, p);
            var o = new Vector3((float)w.O[0], (float)w.O[1], (float)w.O[2]);
            float Clear(bool bands)
            {
                var best = float.MaxValue;
                foreach (var e in r.Els)
                    if ((e.Kind == "band") == bands)
                    {
                        var q = Vector3.Transform(o - e.Origin, Quaternion.Conjugate(Rot24.Quat(e.Rot)));
                        foreach (var hl in e.Hulls)
                            best = MathF.Min(best, Depth(hl, q));
                    }
                return best;
            }
            var (skin, pieces) = (Clear(true), Clear(false));
            Console.WriteLine($"[design] hollow: {p.Kind}'s opening ({2 * op.Hx * p.Size * p.Hole:0.00} x {2 * op.Hz * p.Size * p.Hole:0.00} m) at {o.X:0.0}, {o.Y:0.0}, {o.Z:0.0}: its middle {(skin >= PersonRadius ? "is" : "is NOT")} clear of the skin's plates ({skin:0.00} m), {pieces:0.00} m from a piece's collider{(pieces < 0 ? " (inside one: a door's work, package E)" : "")}");
            if (skin < PersonRadius)
                r.Fail($"{p.Kind}'s opening is plated over");
        }
        if (r.Blueprint != null)
        {
            Tunnel(r);
            Rolled(r);
        }
    }

    /// <summary>The kind of element a body's part is (its key ends in the element's id), or "".</summary>
    private static string KindOf(Run r, Construct c, int part)
    {
        var name = c.DefOf(part).Name;
        foreach (var e in r.Els)
            if (name.EndsWith(" " + e.Id, StringComparison.Ordinal))
                return e.Kind;
        return "";
    }

    /// <summary>The first of a body's hull colliders a world ray meets (its distance, or NaN), and whose part it is.</summary>
    private static float RayHulls(Construct c, Vector3 o, Vector3 dir, float max) => RayHulls(c, o, dir, max, out _);

    private static float RayHulls(Construct c, Vector3 o, Vector3 dir, float max, out int part)
    {
        var best = float.NaN;
        part = -1;
        for (var k = 0; k < c.PartCount; k++)
        {
            var q = c.Rot * Rot24.Quat(c.Parts[k].Rot);
            var back = Quaternion.Conjugate(q);
            var lo = Vector3.Transform(o - c.ToWorld(c.Parts[k].Pos), back);
            var ld = Vector3.Transform(dir, back);
            foreach (var hl in c.DefOf(k).Hulls)
                if (hl.Ray(lo, ld, max, out var t, out _) && !(t >= best))
                    (best, part) = (t, k);
        }
        return best;
    }

    /// <summary>
    /// Tunnelling: a chest (a box body) dropped onto the spine from higher and higher, so it meets a thin plate faster each time, and where it
    /// comes to rest: on the skin, or through it into the ship.
    /// </summary>
    private static void Tunnel(Run r)
    {
        if (ConstructBuild.FindBlueprint("Chest") is not { } chest)
        {
            Console.WriteLine("[design] hollow: no Chest blueprint to drop");
            return;
        }
        foreach (var height in new[] { 3f, 20f, 80f, 320f, 1200f })
        {
            var world = new ConstructWorld();
            var ship = ConstructBuild.BuildOnGround(world, ConstructBuild.ParseBlueprint(r.Blueprint!, "design check.json"), r.Ground, 0, out var why);
            if (ship == null)
            {
                r.Fail($"the tunnelling drop's ship wasn't built: {why}");
                return;
            }
            // Down onto the spine: the spot nearest the middle of its length where a ray from above meets a band's plate first (not a piece).
            var (lo, hi) = (ship.LocalMin, ship.LocalMax);
            var mid = Vector3.Zero;
            var top = float.NaN;
            for (var k = 0; k <= 40 && float.IsNaN(top); k++)
            {
                var f = 0.5f + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) / 41f;
                var from = ship.ToWorld(new Vector3((lo.X + hi.X) / 2, hi.Y + 5, lo.Z + (hi.Z - lo.Z) * f));
                var t = RayHulls(ship, from, -Vector3.UnitY, 50, out var part);
                if (!float.IsNaN(t) && part >= 0 && KindOf(r, ship, part) == "band")
                    (mid, top) = (from, from.Y - t);
            }
            if (float.IsNaN(top))
            {
                Console.WriteLine("[design] hollow: no bare spine to drop on");
                return;
            }
            var box = ConstructBuild.BuildOnGround(world, chest, mid with { Y = r.Ground.Y + 200 }, 0, out why, beside: false);
            if (box == null)
            {
                r.Fail($"the chest wasn't built: {why}");
                return;
            }
            box.Pos += new Vector3(mid.X, top + 1.5f, mid.Z) - box.WorldCom;
            box.Vel = new Vector3(0, -MathF.Sqrt(2 * 9.81f * height), 0); // (1.5 m over the skin, as fast as it would come falling from that height)
            box.Wake();
            double worst = 0;
            var meet = MathF.Abs(box.Vel.Y);
            for (var k = 0; k < 4 * World.TickRate; k++)
            {
                worst = Math.Max(worst, TimedStep(world));
            }
            // Inside the ship: a collider of it straight above the chest's middle (the skin it came through).
            var above = RayHulls(ship, box.WorldCom, Vector3.UnitY, 30);
            var rest = box.WorldCom.Y - top;
            var through = !float.IsNaN(above);
            var at = Vector3.Transform(box.WorldCom - ship.Pos, Quaternion.Conjugate(ship.Rot));
            Console.WriteLine($"[design] hollow: a chest meeting the spine at {meet:0} m/s (from {height:0} m) rests with its middle {rest:+0.00;-0.00} m off the skin's top there, at {at.X:0.00}, {at.Y:0.00}, {at.Z:0.00} in the ship{(through ? $": THROUGH IT ({above:0.00} m under a collider)" : "")}, speed {box.Vel.Length():0.00} m/s; worst step {worst:0.0} ms");
            if (through)
                r.Fail($"a chest at {meet:0} m/s went through the hull's plates");
        }
    }

    /// <summary>The ship dropped rolled over on the ground (its side on the ground, many plates at once): the step's cost, and it resting.</summary>
    private static void Rolled(Run r)
    {
        var rolls = new[] { 25f, 90f }; // (each held to the budget: TimedStep leaves the contacts' first JIT out)
        for (var ri = 0; ri < rolls.Length; ri++)
        {
            var roll = rolls[ri];
            var world = new ConstructWorld();
            var ship = ConstructBuild.BuildOnGround(world, ConstructBuild.ParseBlueprint(r.Blueprint!, "design check.json"), r.Ground, 0, out var why);
            if (ship == null)
            {
                r.Fail($"the rolled drop's ship wasn't built: {why}");
                return;
            }
            var com = ship.WorldCom;
            ship.Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll * MathF.PI / 180) * ship.Rot;
            ship.Pos += com - ship.WorldCom + new Vector3(0, 12, 0);
            ship.Wake();
            double worst = 0, sum = 0;
            var worstAt = 0f;
            var n = 8 * World.TickRate;
            for (var k = 0; k < n; k++)
            {
                var ms = TimedStep(world);
                if (k > 10)
                    (worst, sum, worstAt) = (Math.Max(worst, ms), sum + ms, ms > worst ? k / (float)World.TickRate : worstAt);
            }
            var low = float.MaxValue;
            for (var k = 0; k < ship.PartCount; k++)
                foreach (var hl in ship.DefOf(k).Hulls)
                    foreach (var v in hl.V)
                        low = MathF.Min(low, ship.ToWorld(ship.Parts[k].Pos + Vector3.Transform(v, Rot24.Quat(ship.Parts[k].Rot))).Y);
            Console.WriteLine($"[design] hollow: dropped rolled {roll:0} deg from 12 m: {world.Bodies.Count} bodies, lowest collider {low - r.Ground.Y:+0.00;-0.00} m off the ground, speed {ship.Vel.Length():0.00} m/s, asleep {ship.Asleep}; step mean {sum / (n - 11):0.000} ms, worst {worst:0.00} ms at {worstAt:0.00} s");
            if (low < r.Ground.Y - 0.5f)
                r.Fail($"rolled {roll:0} deg it sank {r.Ground.Y - low:0.00} m into the ground");
            if (worst > 8)
                r.Fail($"rolled {roll:0} deg a step took {worst:0.0} ms");
        }
    }

    /// <summary>How far outside a hull a point is along its faces' planes (a lower bound of its distance; at most 0 inside).</summary>
    private static float Depth(ConvexHull h, Vector3 p)
    {
        var worst = float.MinValue;
        for (var f = 0; f + 1 < h.FaceStart.Length; f++)
            worst = MathF.Max(worst, Vector3.Dot(h.N[f], p - h.V[h.FaceIdx[h.FaceStart[f]]]));
        return worst;
    }
}
