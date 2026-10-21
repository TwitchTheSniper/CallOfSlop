using System.Numerics;

namespace CallOfSlop;

/// <summary>One prop standing on a map. <see cref="Yaw"/> is in radians; <see cref="Bounds"/> is its world bounding box.</summary>
public readonly record struct PropInstance(PropDef Def, Vector3 Position, float Yaw, float Scale, float Rand, int SolidStart, int SolidCount, Box Bounds)
{
    public bool IsBreakable => Def.Breakable != null;
}

/// <summary>
/// Props on a map: the PlaceProp and Clutter ops, collision (solid props add collider boxes to <see cref="Solid"/>), the
/// fingerprint, and the runtime state of breakable props (health, broken or not).
///
/// Collision stays plain boxes, so movement, bullets, line of sight, the collision grid and the bots' nav graph all treat
/// a solid prop like any other box. A turned prop gets a few boxes that follow its turned outline. Collider boxes are not
/// drawn as boxes (<see cref="IsPropCollider"/>): the prop renderer draws the model instead.
/// </summary>
public sealed partial class Map
{
    /// <summary>Every prop on the map, in placement order (PlaceProp ops first as they run, then Clutter at the end).</summary>
    public readonly List<PropInstance> PropInstances = new();

    /// <summary>Props from <see cref="PropInstances"/>[ClutterStart] on were scattered by Clutter ops (the ones before were placed one by one).</summary>
    public int ClutterStart;

    /// <summary>A fingerprint of every placed prop (0 when there are none), mixed into <see cref="ContentHash"/>.</summary>
    public uint PropHash;

    private bool[] _propMask = [];
    private readonly Dictionary<int, Box> _propOriginal = new();
    private readonly List<ClutterRequest> _clutter = new();

    private sealed record ClutterRequest(string[] Props, float X0, float Z0, float X1, float Z1, int Count, int Seed, float Width, bool Align, float Gap, bool Mirror, int OpIndex);

    /// <summary>Is <see cref="Solid"/>[i] a prop's collider box? (Those are drawn by the prop renderer, not as boxes.)</summary>
    public bool IsPropCollider(int i) => (uint)i < (uint)_propMask.Length && _propMask[i];

    /// <summary>
    /// A prop from the Props folder standing at (x, z), turned by <paramref name="yaw"/> degrees. On the ground (or the
    /// terrain) unless <paramref name="y"/> is given; with y = 0 it also stands on a low box under it, like a kerb.
    /// </summary>
    [MapOp]
    public void PlaceProp(string name, float x, float z, float yaw = 0, float scale = 1, float y = 0)
    {
        using var __ = Op([name, x, z, yaw, scale, y]);
        var scope = PropScope; // the pack's own props first, then the Props folder (MapPackAssets.cs)
        var def = scope.Find(name) ?? throw new InvalidOperationException($"unknown prop \"{name}\": {scope.Suggest(name)}");
        if (scale is < 0.1f or > 10f)
            throw new InvalidOperationException("scale is 0.1 to 10");
        var yawRad = yaw * (MathF.PI / 180f);
        var (fmin, fmax) = Footprint(def, x, z, yawRad, scale);
        float py;
        if (HasTerrain && _topAnchor == OpAnchor.World)
            py = y;
        else
        {
            var ground = HasTerrain ? LowestGroundUnder(fmin.X, fmin.Y, fmax.X, fmax.Y) : 0f;
            py = ground + y;
            if (y == 0 && def.GroundSnap)
            {
                var space = new BoxSpace(Solid);
                var top = space.SupportTop(x, z, ground - 0.05f, ground + 0.6f);
                if (top > py)
                    py = top;
            }
        }
        AddProp(def, new Vector3(x, py, z), yawRad, scale, null);
    }

    /// <summary>
    /// Scatters up to <paramref name="count"/> props from the list (names or category names; repeat a name to make it
    /// more common) over the rectangle between (x0, z0) and (x1, z1), or with <paramref name="width"/> &gt; 0 along a strip
    /// that wide from (x0, z0) to (x1, z1). Deterministic for a seed. Runs after every op, the foliage and the objectives
    /// are known, so it keeps solid props clear of spawns, waypoints and the routes between them, hills, flags, movers and
    /// gaps between walls (see Maps/README.md). With <paramref name="mirror"/> every prop also gets its twin turned 180
    /// degrees about the map's centre (at -x, -z, facing the other way), placed only when both fit, so the two halves of a
    /// point-symmetric map get the same props (count is then per half).
    /// </summary>
    [MapOp]
    public void Clutter(string[] props, float x0, float z0, float x1, float z1, int count, int seed = 0, float width = 0, bool align = false, float gap = 0.15f, bool mirror = false)
    {
        using var __ = Op([props, x0, z0, x1, z1, count, seed, width, align, gap, mirror]);
        if (props.Length == 0)
            throw new InvalidOperationException("\"props\" needs at least one prop or category name");
        var scope = PropScope;
        foreach (var n in props)
            if (scope.Find(n) == null && scope.InCategory(n).Count == 0)
                throw new InvalidOperationException($"unknown prop or category \"{n}\": {scope.Suggest(n)}");
        if (count is < 0 or > 2000)
            throw new InvalidOperationException("count is 0 to 2000");
        if (width < 0 || gap is < 0 or > 10)
            throw new InvalidOperationException("width must be 0 or more, gap 0 to 10");
        _clutter.Add(new ClutterRequest(props, x0, z0, x1, z1, count, seed, width, align, gap, mirror, Ops.Count - 1));
    }

    // ---- Adding a prop -------------------------------------------------------------------------------------

    /// <summary>The horizontal bounds (min x, min z) .. (max x, max z) of a prop placed at (x, z).</summary>
    private static (Vector2 Min, Vector2 Max) Footprint(PropDef def, float x, float z, float yaw, float scale)
    {
        var b = WorldBounds(def.Bounds, new Vector3(x, 0, z), yaw, scale);
        return (new Vector2(b.Min.X, b.Min.Z), new Vector2(b.Max.X, b.Max.Z));
    }

    /// <summary>The world axis-aligned bounds of a box in a prop's frame, for a prop at <paramref name="pos"/>.</summary>
    public static Box WorldBounds(Box local, Vector3 pos, float yaw, float scale)
    {
        float cs = MathF.Cos(yaw), sn = MathF.Sin(yaw);
        var c = local.Center * scale;
        var h = local.Half * scale;
        var hx = MathF.Abs(h.X * cs) + MathF.Abs(h.Z * sn);
        var hz = MathF.Abs(h.X * sn) + MathF.Abs(h.Z * cs);
        var center = new Vector3(pos.X + c.X * cs + c.Z * sn, pos.Y + c.Y, pos.Z - c.X * sn + c.Z * cs);
        return new Box(center, new Vector3(hx, h.Y, hz));
    }

    /// <summary>
    /// The world boxes for one collider box of a turned prop. Turned by a multiple of 90 degrees it's one exact box;
    /// otherwise the turned rectangle is cut into 2-6 strips along its longer world axis, each strip's box covering its
    /// slice of the outline, so a crate at 45 degrees doesn't get a fat square around it.
    /// </summary>
    public static void ColliderBoxes(Box local, Vector3 pos, float yaw, float scale, List<Box> into)
    {
        var quarter = yaw / (MathF.PI / 2);
        if (MathF.Abs(quarter - MathF.Round(quarter)) < 0.004f)
        {
            into.Add(WorldBounds(local, pos, MathF.Round(quarter) * (MathF.PI / 2), scale));
            return;
        }
        float cs = MathF.Cos(yaw), sn = MathF.Sin(yaw);
        var c = local.Center * scale;
        var h = local.Half * scale;
        Span<Vector2> pts = stackalloc Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            var lx = c.X + ((i & 1) == 0 ? -h.X : h.X);
            var lz = c.Z + ((i & 2) == 0 ? -h.Z : h.Z);
            pts[i] = new Vector2(pos.X + lx * cs + lz * sn, pos.Z - lx * sn + lz * cs);
        }
        (pts[2], pts[3]) = (pts[3], pts[2]); // corner order round the outline: 0, 1, 3, 2
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in pts)
        {
            minX = MathF.Min(minX, p.X);
            maxX = MathF.Max(maxX, p.X);
            minZ = MathF.Min(minZ, p.Y);
            maxZ = MathF.Max(maxZ, p.Y);
        }
        var alongX = maxX - minX >= maxZ - minZ;
        float lo = alongX ? minX : minZ, ext = alongX ? maxX - minX : maxZ - minZ;
        var n = Math.Clamp((int)MathF.Ceiling(ext / 0.5f), 2, 6);
        float y0 = pos.Y + c.Y - h.Y, y1 = pos.Y + c.Y + h.Y;
        for (var k = 0; k < n; k++)
        {
            float a = lo + ext * k / n, b = lo + ext * (k + 1) / n;
            float pmin = float.MaxValue, pmax = float.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) & 3];
                float pa = alongX ? p.X : p.Y, qa = alongX ? q.X : q.Y;
                float pp = alongX ? p.Y : p.X, qp = alongX ? q.Y : q.X;
                if (pa >= a - 1e-5f && pa <= b + 1e-5f)
                {
                    pmin = MathF.Min(pmin, pp);
                    pmax = MathF.Max(pmax, pp);
                }
                for (var li = 0; li < 2; li++)
                {
                    var line = li == 0 ? a : b;
                    if ((pa - line) * (qa - line) <= 0 && MathF.Abs(qa - pa) > 1e-6f)
                    {
                        var v = pp + (qp - pp) * (line - pa) / (qa - pa);
                        pmin = MathF.Min(pmin, v);
                        pmax = MathF.Max(pmax, v);
                    }
                }
            }
            if (pmax - pmin < 0.01f)
                continue;
            var min = alongX ? new Vector3(a, y0, pmin) : new Vector3(pmin, y0, a);
            var max = alongX ? new Vector3(b, y1, pmax) : new Vector3(pmax, y1, b);
            into.Add(new Box((min + max) / 2, (max - min) / 2));
        }
    }

    private void AddProp(PropDef def, Vector3 pos, float yaw, float scale, BoxSpace? space)
    {
        var rand = (Foliage.Hash((uint)PropInstances.Count * 2654435761u ^ BitConverter.SingleToUInt32Bits(pos.X) ^ BitConverter.SingleToUInt32Bits(pos.Z) * 40503u) >> 8) / 16777216f;
        int start = -1, count = 0;
        if (def.IsSolid)
        {
            var boxes = new List<Box>(8);
            foreach (var c in def.Colliders)
                ColliderBoxes(c, pos, yaw, scale, boxes);
            start = Solid.Count;
            count = boxes.Count;
            if (_propMask.Length < Solid.Count + count)
                Array.Resize(ref _propMask, Math.Max(Solid.Count + count, _propMask.Length * 2 + 64));
            foreach (var b in boxes)
            {
                _propMask[Solid.Count] = true;
                _propOriginal[Solid.Count] = b;
                Solid.Add(b);
                Colors.Add(def.Parts[0].Color);
                space?.Add(b);
            }
            _materials = null;
        }
        PropInstances.Add(new PropInstance(def, pos, yaw, scale, rand, start, count, WorldBounds(def.Bounds, pos, yaw, scale)));
        _breakables = null;
    }

    // ---- Clutter (runs once the map is otherwise complete) ----------------------------------------------------

    /// <summary>Clearance a solid clutter prop keeps from any wall or box it can't step over, so it never closes a gap a player fits through.</summary>
    public const float ClutterClearance = 1.1f;

    public const float ClutterSpawnClearance = 2.5f;
    public const float ClutterWaypointClearance = 1.2f;
    public const float ClutterPathClearance = 0.6f;

    /// <summary>
    /// Called by the map loader after the ops, foliage, movers and objectives: places the Clutter ops' props and works out
    /// the prop fingerprint. Deterministic: the same file gives the same props on every PC.
    /// </summary>
    public void FinishProps()
    {
        ClutterStart = PropInstances.Count;
        if (_clutter.Count > 0)
        {
            var space = new BoxSpace(Solid);
            var ctx = new ClutterContext(this);
            foreach (var req in _clutter)
                PlaceClutter(req, space, ctx);
            InvalidateObjectives(); // automatic hills were probed before the clutter (it keeps clear of them, so they don't move)
            _clutter.Clear();
        }
        PropHash = HashProps();
    }

    /// <summary>What solid clutter keeps away from: spawns, waypoints and the routes between them, hills, flags and movers.</summary>
    private sealed class ClutterContext
    {
        public readonly List<Vector3> Spawns = new();
        public readonly List<Vector3> Waypoints = new();
        public readonly List<(Vector2 A, Vector2 B)> Paths = new();
        public readonly List<HillZone> Hills = new();
        public readonly List<Vector3> Flags = new();
        public readonly List<Box> Movers = new();
        public readonly List<(Vector2 Min, Vector2 Max)> Placed = new();

        public ClutterContext(Map m)
        {
            Spawns.AddRange(m.Spawns);
            if (m.TeamSpawnData != null)
                foreach (var team in m.TeamSpawnData)
                    Spawns.AddRange(team);
            Waypoints.AddRange(m.Waypoints);
            var obj = m.Objectives;
            Hills.AddRange(obj.Hills);
            Flags.AddRange(obj.Flags);
            foreach (var mv in m.Movers)
                Movers.Add(mv.Sweep);
            // Routes: each waypoint to its four nearest neighbours within 14 m (the grid lines bots walk along).
            var near = new List<(float D, int J)>();
            for (var i = 0; i < Waypoints.Count; i++)
            {
                near.Clear();
                for (var j = 0; j < Waypoints.Count; j++)
                {
                    if (j == i)
                        continue;
                    var d = Vector2.Distance(Flat(Waypoints[i]), Flat(Waypoints[j]));
                    if (d < 14f)
                        near.Add((d, j));
                }
                near.Sort();
                for (var k = 0; k < near.Count && k < 4; k++)
                    Paths.Add((Flat(Waypoints[i]), Flat(Waypoints[near[k].J])));
            }
        }
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    private void PlaceClutter(ClutterRequest req, BoxSpace space, ClutterContext ctx)
    {
        // The pick list: every name once (repeats add weight), a category stands for all its props.
        var pool = new List<PropDef>();
        var scope = PropScope;
        foreach (var n in req.Props)
        {
            if (scope.Find(n) is { } d)
                pool.Add(d);
            else
                pool.AddRange(scope.InCategory(n));
        }
        var weights = pool.Select(p => p.Weight).ToArray();
        var total = weights.Sum();
        if (pool.Count == 0 || total <= 0 || req.Count == 0)
            return;

        var rng = new Foliage.Rng((uint)req.Seed * 2654435761u ^ (uint)req.OpIndex * 97u ^ 0xc177u);
        var line = req.Width > 0;
        var a = new Vector2(req.X0, req.Z0);
        var b = new Vector2(req.X1, req.Z1);
        var along = b - a;
        var len = along.Length();
        var dir = len > 1e-4f ? along / len : Vector2.UnitX;
        var perp = new Vector2(-dir.Y, dir.X);
        var lineYaw = MathF.Atan2(dir.X, dir.Y) + MathF.PI / 2; // a prop's +X axis along the line

        var placed = 0;
        for (var attempt = 0; attempt < req.Count * 15 && placed < req.Count; attempt++)
        {
            // Always draw the same six numbers per attempt, so a rejected spot never shifts the next ones.
            float r0 = rng.Next(), r1 = rng.Next(), r2 = rng.Next(), r3 = rng.Next(), r4 = rng.Next(), r5 = rng.Next();
            var pick = r0 * total;
            var def = pool[^1];
            for (var i = 0; i < pool.Count; i++)
            {
                pick -= weights[i];
                if (pick < 0)
                {
                    def = pool[i];
                    break;
                }
            }
            var p = line ? a + along * r1 + perp * ((r2 - 0.5f) * req.Width)
                : new Vector2(req.X0 + (req.X1 - req.X0) * r1, req.Z0 + (req.Z1 - req.Z0) * r2);
            float yaw;
            if (req.Align && line)
                yaw = lineYaw + (r3 < 0.5f ? 0 : MathF.PI) + (r4 - 0.5f) * 2 * def.YawJitter * (MathF.PI / 180f);
            else if (def.YawStep > 0)
                yaw = (MathF.Floor(r3 * (360f / def.YawStep)) * def.YawStep + (r4 - 0.5f) * 2 * def.YawJitter) * (MathF.PI / 180f);
            else if (def.RandomYaw)
                yaw = r3 * MathF.Tau;
            else
                yaw = (r4 - 0.5f) * 2 * def.YawJitter * (MathF.PI / 180f);
            var scale = def.ScaleMin + (def.ScaleMax - def.ScaleMin) * r5;
            if (!TryClutterSpot(def, p, yaw, scale, req.Gap, space, ctx, out var pos))
                continue;
            if (req.Mirror)
            {
                // The twin at -p, turned 180 degrees: both or neither. They mustn't meet each other (near the centre).
                var twinYaw = yaw + MathF.PI;
                if (!TryClutterSpot(def, -p, twinYaw, scale, req.Gap, space, ctx, out var twin))
                    continue;
                var fa = WorldBounds(def.Bounds, pos, yaw, scale);
                var ft = WorldBounds(def.Bounds, twin, twinYaw, scale);
                var keep = req.Gap + (def.IsSolid ? ClutterClearance : 0f);
                if (fa.Min.X - keep < ft.Max.X && fa.Max.X + keep > ft.Min.X && fa.Min.Z - keep < ft.Max.Z && fa.Max.Z + keep > ft.Min.Z)
                    continue;
                PlaceClutterProp(def, pos, yaw, scale, space, ctx);
                PlaceClutterProp(def, twin, twinYaw, scale, space, ctx);
            }
            else
                PlaceClutterProp(def, pos, yaw, scale, space, ctx);
            placed++;
        }
        if (placed < req.Count)
            Console.WriteLine($"Map '{Name}': Clutter (op #{req.OpIndex + 1}) placed {placed} of {req.Count} props{(req.Mirror ? " (each with its mirrored twin)" : "")}; there was no clear room for the rest");
    }

    private void PlaceClutterProp(PropDef def, Vector3 pos, float yaw, float scale, BoxSpace space, ClutterContext ctx)
    {
        AddProp(def, pos, yaw, scale, space);
        var fb = WorldBounds(def.Bounds, pos, yaw, scale);
        ctx.Placed.Add((new Vector2(fb.Min.X, fb.Min.Z), new Vector2(fb.Max.X, fb.Max.Z)));
    }

    private bool TryClutterSpot(PropDef def, Vector2 p, float yaw, float scale, float gap, BoxSpace space, ClutterContext ctx, out Vector3 pos)
    {
        pos = default;
        var fb = WorldBounds(def.Bounds, new Vector3(p.X, 0, p.Y), yaw, scale);
        Vector2 min = new(fb.Min.X, fb.Min.Z), max = new(fb.Max.X, fb.Max.Z);
        var radius = (max - min).Length() / 2;
        var edge = def.IsSolid ? ClutterClearance : 0.3f;
        if (min.X < -HalfX + edge || max.X > HalfX - edge || min.Y < -HalfZ + edge || max.Y > HalfZ - edge)
            return false;
        if (Boundary is { } bd && !(bd.Inside(min.X, min.Y, edge) && bd.Inside(max.X, min.Y, edge) && bd.Inside(min.X, max.Y, edge)
                                    && bd.Inside(max.X, max.Y, edge) && bd.Inside(p.X, p.Y, edge)))
            return false; // inside the map's outline, clear of its edge

        // Ground: the terrain (lowest point under the footprint) or y = 0; no steep slopes, no big bumps under solid props.
        var ground = 0f;
        if (HasTerrain)
        {
            if (IsSteep(p.X, p.Y))
                return false;
            ground = LowestGroundUnder(min.X, min.Y, max.X, max.Y);
            var high = MathF.Max(MathF.Max(GroundHeight(min.X, min.Y), GroundHeight(max.X, max.Y)), MathF.Max(GroundHeight(min.X, max.Y), GroundHeight(max.X, min.Y)));
            if (high - ground > (def.IsSolid ? 0.35f : 0.6f))
                return false;
        }

        // Rest on the ground or a low box (kerb, step) that's level under the whole footprint.
        var rest = ground;
        if (def.GroundSnap)
        {
            const float inset = 0.04f;
            var lo = float.MaxValue;
            var hi = float.MinValue;
            for (var k = 0; k < 5; k++)
            {
                var x = k == 0 ? p.X : (k & 1) == 1 ? min.X + inset : max.X - inset;
                var z = k == 0 ? p.Y : k < 3 ? min.Y + inset : max.Y - inset;
                var top = MathF.Max(ground, space.SupportTop(x, z, ground - 0.05f, ground + 0.6f));
                lo = MathF.Min(lo, top);
                hi = MathF.Max(hi, top);
            }
            if (hi - lo > 0.06f)
                return false; // half on a kerb: it would float
            rest = hi;
        }
        var height = fb.Max.Y; // top of the model above its origin
        var bodyMin = new Vector3(min.X + 0.03f, rest + 0.03f, min.Y + 0.03f);
        var bodyMax = new Vector3(max.X - 0.03f, rest + MathF.Max(height, 0.05f), max.Y - 0.03f);
        if (space.Overlaps(bodyMin, bodyMax))
            return false;

        // Not on top of other clutter.
        foreach (var (pmin, pmax) in ctx.Placed)
            if (min.X - gap < pmax.X && max.X + gap > pmin.X && min.Y - gap < pmax.Y && max.Y + gap > pmin.Y)
                return false;

        if (def.IsSolid)
        {
            // Keep corridors open: nothing you can't step over within the clearance around it (a prop low enough to step
            // onto never closes a gap, so it needs none).
            var c = ClutterClearance;
            if (height > World.StepHeight && space.Overlaps(new Vector3(min.X - c, rest + World.StepHeight, min.Y - c), new Vector3(max.X + c, rest + World.PlayerHeight, max.Y + c)))
                return false;
            foreach (var s in ctx.Spawns)
                if (Vector2.Distance(Flat(s), p) < ClutterSpawnClearance + radius)
                    return false;
            foreach (var w in ctx.Waypoints)
                if (Vector2.Distance(Flat(w), p) < ClutterWaypointClearance + radius)
                    return false;
            foreach (var (pa, pb) in ctx.Paths)
                if (SegmentDistance(p, pa, pb) < ClutterPathClearance + radius)
                    return false;
            foreach (var h in ctx.Hills)
                if (Vector2.Distance(Flat(h.Center), p) < h.Radius + 1f + radius)
                    return false;
            foreach (var f in ctx.Flags)
                if (Vector2.Distance(Flat(f), p) < 3f + radius)
                    return false;
            foreach (var m in ctx.Movers)
                if (min.X - c < m.Max.X && max.X + c > m.Min.X && min.Y - c < m.Max.Z && max.Y + c > m.Min.Z)
                    return false;
        }
        pos = new Vector3(p.X, rest, p.Y);
        return true;
    }

    private uint HashProps()
    {
        if (PropInstances.Count == 0)
            return 0;
        var h = 2166136261u;
        void Mix(uint v)
        {
            for (var i = 0; i < 4; i++)
            {
                h ^= (v >> (i * 8)) & 0xFF;
                h *= 16777619u;
            }
        }
        Mix((uint)PropInstances.Count);
        foreach (var p in PropInstances)
        {
            foreach (var ch in p.Def.Name.ToLowerInvariant())
                Mix(ch);
            Mix(BitConverter.SingleToUInt32Bits(p.Position.X));
            Mix(BitConverter.SingleToUInt32Bits(p.Position.Y));
            Mix(BitConverter.SingleToUInt32Bits(p.Position.Z));
            Mix(BitConverter.SingleToUInt32Bits(p.Yaw));
            Mix(BitConverter.SingleToUInt32Bits(p.Scale));
            Mix((uint)p.SolidCount | (p.IsBreakable ? 0x10000u : 0u));
        }
        return h == 0 ? 1 : h;
    }

    // ---- Breakable props: runtime state (server decides, clients follow) ----------------------------------------

    private int[]? _breakables;

    /// <summary>Indices (into <see cref="PropInstances"/>) of every breakable prop.</summary>
    public int[] BreakableProps => _breakables ??= Enumerable.Range(0, PropInstances.Count).Where(i => PropInstances[i].IsBreakable).ToArray();

    private int[] _propHealth = [];
    private bool[] _propBroken = [];
    private readonly List<int> _brokenProps = new();

    /// <summary>Goes up whenever a prop breaks or the props are restored (the renderer re-uploads its visibility then).</summary>
    public int PropBreakVersion { get; private set; }

    /// <summary>Indices (into <see cref="PropInstances"/>) of the props broken right now, in the order they broke.</summary>
    public IReadOnlyList<int> BrokenProps => _brokenProps;

    public bool IsPropBroken(int i) => (uint)i < (uint)_propBroken.Length && _propBroken[i];

    /// <summary>Health left on a breakable prop (its full health until damaged; 0 once broken).</summary>
    public int PropHealth(int i)
    {
        if ((uint)i >= (uint)PropInstances.Count || PropInstances[i].Def.Breakable is not { } br)
            return 0;
        if (IsPropBroken(i))
            return 0;
        return (uint)i < (uint)_propHealth.Length && _propHealth[i] > 0 ? _propHealth[i] : br.Health;
    }

    private void EnsurePropState()
    {
        if (_propBroken.Length != PropInstances.Count)
        {
            _propBroken = new bool[PropInstances.Count];
            _propHealth = new int[PropInstances.Count];
        }
    }

    /// <summary>
    /// Server: applies damage of a kind to a breakable prop. Returns true when this damage breaks it (the caller then calls
    /// <see cref="BreakProp"/> and tells the clients). Kinds the prop doesn't break from do nothing.
    /// </summary>
    public bool DamageProp(int i, int amount, PropDamage kind)
    {
        if ((uint)i >= (uint)PropInstances.Count || PropInstances[i].Def.Breakable is not { } br || amount <= 0 || (br.BreakBy & kind) == 0 || IsPropBroken(i))
            return false;
        EnsurePropState();
        if (_propHealth[i] <= 0)
            _propHealth[i] = br.Health;
        _propHealth[i] -= amount;
        return _propHealth[i] <= 0;
    }

    /// <summary>
    /// Breaks a prop: it disappears and its collider boxes stop blocking anything (they're parked out of the world, so every
    /// query, the collision grid included, simply never meets them). Returns false if it was already broken.
    /// </summary>
    public bool BreakProp(int i)
    {
        if ((uint)i >= (uint)PropInstances.Count || IsPropBroken(i))
            return false;
        EnsurePropState();
        _propBroken[i] = true;
        _propHealth[i] = 0;
        _brokenProps.Add(i);
        var inst = PropInstances[i];
        for (var k = inst.SolidStart; k < inst.SolidStart + inst.SolidCount; k++)
        {
            var b = Solid[k];
            Solid[k] = new Box(new Vector3(b.Center.X, -1000f, b.Center.Z), new Vector3(0.001f));
            World.SolidChanged(this, k);
        }
        PropBreakVersion++;
        return true;
    }

    /// <summary>Puts every broken prop back and resets all prop health (a new match, a new session on this map).</summary>
    public void RestoreProps()
    {
        var damaged = false;
        for (var i = 0; i < _propHealth.Length; i++)
            damaged |= _propHealth[i] != 0;
        if (_brokenProps.Count == 0 && !damaged)
            return;
        var solidChanged = false;
        foreach (var i in _brokenProps)
        {
            var inst = PropInstances[i];
            for (var k = inst.SolidStart; k < inst.SolidStart + inst.SolidCount; k++)
            {
                Solid[k] = _propOriginal[k];
                solidChanged = true;
            }
        }
        _brokenProps.Clear();
        Array.Clear(_propBroken);
        Array.Clear(_propHealth);
        if (solidChanged)
            World.InvalidateGrid(this);
        PropBreakVersion++;
    }

    /// <summary>The box <see cref="ContentHash"/> uses for Solid[i]: a broken prop's original collider, so breaking never changes the fingerprint.</summary>
    internal Box HashBox(int i) => IsPropCollider(i) && _propOriginal.TryGetValue(i, out var o) ? o : ZombieHashBox(i, out var zb) ? zb : Solid[i]; // an open Zombies door counts as closed
}

/// <summary>Boxes filed into 4 m columns, for the placement queries of PlaceProp and Clutter (map loading only).</summary>
internal sealed class BoxSpace
{
    private const float Cell = 4f;
    private readonly Dictionary<long, List<Box>> _cells = new();
    private readonly List<Box> _huge = new();

    public BoxSpace(List<Box> boxes)
    {
        foreach (var b in boxes)
            Add(b);
    }

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    public void Add(Box b)
    {
        int x0 = (int)MathF.Floor(b.Min.X / Cell), x1 = (int)MathF.Floor(b.Max.X / Cell);
        int z0 = (int)MathF.Floor(b.Min.Z / Cell), z1 = (int)MathF.Floor(b.Max.Z / Cell);
        if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 400)
        {
            _huge.Add(b); // the ground slab and other giants: checked every time, there are few
            return;
        }
        for (var z = z0; z <= z1; z++)
        for (var x = x0; x <= x1; x++)
        {
            if (!_cells.TryGetValue(Key(x, z), out var l))
                _cells[Key(x, z)] = l = new List<Box>(4);
            l.Add(b);
        }
    }

    /// <summary>Does any box overlap the region strictly (touching faces don't count)?</summary>
    public bool Overlaps(Vector3 min, Vector3 max)
    {
        foreach (var b in _huge)
            if (Hit(b, min, max))
                return true;
        int x0 = (int)MathF.Floor(min.X / Cell), x1 = (int)MathF.Floor(max.X / Cell);
        int z0 = (int)MathF.Floor(min.Z / Cell), z1 = (int)MathF.Floor(max.Z / Cell);
        for (var z = z0; z <= z1; z++)
        for (var x = x0; x <= x1; x++)
            if (_cells.TryGetValue(Key(x, z), out var l))
                foreach (var b in l)
                    if (Hit(b, min, max))
                        return true;
        return false;

        static bool Hit(Box b, Vector3 min, Vector3 max) =>
            b.Max.X > min.X && b.Min.X < max.X && b.Max.Y > min.Y && b.Min.Y < max.Y && b.Max.Z > min.Z && b.Min.Z < max.Z;
    }

    /// <summary>The highest top between lo and hi of a box over the point (x, z), or -infinity.</summary>
    public float SupportTop(float x, float z, float lo, float hi)
    {
        var best = float.NegativeInfinity;
        foreach (var b in _huge)
            Check(b);
        if (_cells.TryGetValue(Key((int)MathF.Floor(x / Cell), (int)MathF.Floor(z / Cell)), out var l))
            foreach (var b in l)
                Check(b);
        return best;

        void Check(Box b)
        {
            var top = b.Max.Y;
            if (top >= lo && top <= hi && top > best && x >= b.Min.X && x <= b.Max.X && z >= b.Min.Z && z <= b.Max.Z)
                best = top;
        }
    }
}
