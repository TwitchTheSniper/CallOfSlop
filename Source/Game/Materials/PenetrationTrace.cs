using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Bullet penetration along one ray: from the first surface a bullet hits, it goes on through boxes it is strong
/// enough for (<see cref="MaterialPenetration"/>): each box's thickness along the ray (the ray's exit from that box or
/// part, also for oriented boxes) times its material's resistance is spent from the round's power. Boxes that touch
/// (a wall's lining, core and lining; a floor slab and the ceiling under it) make one surface; a bullet passes at most
/// <see cref="MaterialPenetration.MaxSurfaces"/> surfaces and either gets through a whole surface or stops at its front.
/// After each surface the damage is multiplied down (<see cref="FactorAt"/>). Never through the terrain, a map's ground
/// slab, its boundary barrier, a mover, a mesh, rock, asphalt or the Bocage hedgerows, nor anything thicker than its
/// material's limit (thick concrete). Only map data and the ray go in, so the server, the shooter's prediction and every
/// client's impact decals agree. A trace object is reused: no allocation per shot.
/// </summary>
public sealed class PenetrationTrace
{
    /// <summary>Most boxes walked for one ray (all its surfaces' pieces together).</summary>
    public const int MaxBoxes = 12;

    /// <summary>Gap (m) between two boxes along the ray up to which they count as one surface.</summary>
    public const float Touch = 0.015f;

    /// <summary>How far past a box's exit the next cast starts (m), so it doesn't find the same box again.</summary>
    public const float Step = 0.002f;

    /// <summary>One box a bullet passed through.</summary>
    public struct Piece
    {
        public ShapeId Shape;
        public MaterialKey Material;
        public float In, Out; // distances along the ray
        public Vector3 InNormal, OutNormal; // outward normals of the faces it went in and out through
        public int Surface; // which surface (0-based) it belongs to
    }

    /// <summary>Surfaces passed (each one or more touching boxes).</summary>
    public int Surfaces;

    /// <summary>Boxes passed, in order (<see cref="Pieces"/>[0..PieceCount]).</summary>
    public int PieceCount;

    public readonly Piece[] Pieces = new Piece[MaxBoxes];

    /// <summary>Where each passed surface ends (its last box's exit) and the damage multiplier from there on.</summary>
    public readonly float[] SurfaceEnd = new float[8], SurfaceFactor = new float[8];

    /// <summary>Power left when the bullet stopped or flew on.</summary>
    public float PowerLeft;

    /// <summary>Where the bullet ended: the front of the surface it couldn't pass, or the range limit.</summary>
    public float End;

    /// <summary>What stopped it (<see cref="StopReason"/>), for the self-test and the log.</summary>
    public StopReason Stopped;

    public enum StopReason : byte { Nothing, Range, Power, Thickness, Unpassable, SurfaceCap, BoxCap }

    /// <summary>Forget the last ray (no surfaces passed: <see cref="FactorAt"/> is 1 everywhere).</summary>
    public void Clear()
    {
        Surfaces = 0;
        PieceCount = 0;
        PowerLeft = 0;
        End = 0;
        Stopped = StopReason.Nothing;
    }

    /// <summary>The damage multiplier for something hit at distance <paramref name="t"/> along the last ray (1 before any surface).</summary>
    public float FactorAt(float t)
    {
        var f = 1f;
        for (var i = 0; i < Surfaces; i++)
            if (SurfaceEnd[i] <= t + 1e-3f)
                f = SurfaceFactor[i];
        return f;
    }

    /// <summary>Did the last ray pass any surface before distance <paramref name="t"/>?</summary>
    public bool PassedBefore(float t) => Surfaces > 0 && SurfaceEnd[0] <= t + 1e-3f;

    /// <summary>
    /// Walks a ray from <paramref name="o"/> along unit <paramref name="d"/> with <paramref name="power"/> through what it
    /// can pass, up to <paramref name="maxDist"/> and <paramref name="maxSurfaces"/>. Returns the distance it ends at (the
    /// front of what stopped it, or <paramref name="maxDist"/>). With no power it is just the first hit.
    /// </summary>
    public float Run(Map map, PhysicsWorld phys, float power, Vector3 o, Vector3 d, float maxDist, int maxSurfaces)
    {
        Clear();
        PowerLeft = power;
        var filter = QueryFilter.Shots;
        if (!phys.Raycast(o, d, maxDist, filter, out var hit))
            return Finish(maxDist, StopReason.Range);
        if (power <= 0 || maxSurfaces <= 0)
            return Finish(hit.Distance, power <= 0 ? StopReason.Power : StopReason.SurfaceCap);
        maxSurfaces = Math.Min(maxSurfaces, SurfaceEnd.Length);
        var factor = 1f;
        var layerCap = MaterialPenetration.MaxLayerCm;
        while (true)
        {
            if (Surfaces >= maxSurfaces)
                return Finish(hit.Distance, StopReason.SurfaceCap);
            var start = hit.Distance;
            var first = PieceCount;
            float cost = 0, cm = 0, kept = 1;
            var cur = hit;
            while (true)
            {
                if (PieceCount >= MaxBoxes)
                    return Rollback(first, start, StopReason.BoxCap);
                if (!Passable(map, phys, cur, o, d, out var key, out var exit, out var outNormal))
                    return Rollback(first, start, StopReason.Unpassable);
                var m = MaterialPenetration.Get(key);
                var thick = MathF.Max(0, exit - cur.Distance) * 100f;
                if (m.Stops || thick > m.MaxCm || cm + thick > layerCap)
                    return Rollback(first, start, StopReason.Thickness);
                var c = m.ResistPerCm * thick;
                if (cost + c > PowerLeft)
                    return Rollback(first, start, StopReason.Power);
                cost += c;
                cm += thick;
                kept = MathF.Min(kept, m.DamageKept);
                Pieces[PieceCount++] = new Piece
                {
                    Shape = cur.Shape, Material = key, In = cur.Distance, Out = exit, InNormal = cur.Normal, OutNormal = outNormal, Surface = Surfaces,
                };

                var from = exit + Step;
                RayHit next = default;
                var more = from < maxDist && phys.Raycast(o + d * from, d, maxDist - from, filter, out next);
                if (more)
                {
                    next.Distance += from;
                    if (next.Distance - exit <= Touch)
                    {
                        cur = next; // the next piece of the same wall (a lining, an overlapping box)
                        continue;
                    }
                }

                // Through this surface.
                var before = PowerLeft;
                PowerLeft -= cost;
                factor *= kept * (0.5f + 0.5f * (before > 0 ? Math.Clamp(PowerLeft / before, 0f, 1f) : 0f));
                SurfaceEnd[Surfaces] = exit;
                SurfaceFactor[Surfaces] = factor;
                Surfaces++;
                if (!more)
                    return Finish(maxDist, StopReason.Range);
                hit = next;
                break;
            }
        }
    }

    private float Finish(float end, StopReason why)
    {
        End = end;
        Stopped = why;
        return end;
    }

    private float Rollback(int first, float start, StopReason why)
    {
        PieceCount = first; // a surface it doesn't get all the way through stops it at its front
        return Finish(start, why);
    }

    // ---- Shapes ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Can a bullet go into the shape it hit at all, and if so: its material, the distance where the ray leaves it and
    /// the outward normal there. Boxes and props (axis-aligned), static oriented boxes; nothing else.
    /// </summary>
    private bool Passable(Map map, PhysicsWorld phys, in RayHit hit, Vector3 o, Vector3 d, out MaterialKey key, out float exit, out Vector3 outNormal)
    {
        key = MaterialKey.Default;
        exit = 0;
        outNormal = d;
        switch (hit.Kind)
        {
            case ShapeKind.Box or ShapeKind.Prop:
            {
                var i = hit.Shape.Index;
                if ((uint)i >= (uint)phys.BoxCount || i == map.GroundIndex)
                    return false; // the floor slab under a flat map is the ground
                key = hit.Kind == ShapeKind.Prop || map.IsPropCollider(i)
                    ? PropKey(map, i)
                    : MaterialKeys.Of(ImpactDecals.SurfaceOf(map, hit));
                var b = phys.Boxes[i];
                return Exit(b.Center - b.Half, b.Center + b.Half, o, d, out exit, out outNormal);
            }
            case ShapeKind.Obb:
            {
                var b = phys.GetObb(hit.Shape.Index);
                key = MaterialKey.Default; // no texture to go by
                if (!Exit(-b.Half, b.Half, b.ToLocal(o), b.DirToLocal(d), out exit, out var local))
                    return false;
                outNormal = b.DirToWorld(local);
                return true;
            }
            default:
                return false; // terrain, meshes, movers, boundary barriers: solid bulk or not ours to judge
        }
    }

    /// <summary>
    /// Where a ray leaves an axis-aligned box (the far slab distance) and the outward normal of that face. False if the
    /// ray misses the box or the box is behind it.
    /// </summary>
    public static bool Exit(Vector3 min, Vector3 max, Vector3 o, Vector3 d, out float tFar, out Vector3 normal)
    {
        var tNear = float.NegativeInfinity;
        tFar = float.PositiveInfinity;
        var axis = -1;
        for (var a = 0; a < 3; a++)
        {
            var da = d[a];
            if (MathF.Abs(da) < Geometry.ParallelEpsilon)
            {
                if (o[a] < min[a] || o[a] > max[a])
                {
                    normal = d;
                    return false;
                }
                continue;
            }
            var inv = 1f / da;
            var t1 = (min[a] - o[a]) * inv;
            var t2 = (max[a] - o[a]) * inv;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            tNear = MathF.Max(tNear, t1);
            if (t2 < tFar)
            {
                tFar = t2;
                axis = a;
            }
        }
        if (axis < 0 || tFar < 0 || tNear > tFar)
        {
            normal = d;
            return false;
        }
        normal = Geometry.Axis(axis, d[axis] > 0 ? 1 : -1);
        return true;
    }

    // ---- Props --------------------------------------------------------------------------------------------

    private static Map? _propMap;
    private static int _propSolids = -1, _propCount = -1;
    private static int[] _propOfSolid = [];

    /// <summary>The material of the prop a collider box belongs to (Default for a box no prop owns).</summary>
    public static MaterialKey PropKey(Map map, int solid)
    {
        if (_propMap != map || _propSolids != map.Solid.Count || _propCount != map.PropInstances.Count)
        {
            _propMap = map;
            _propSolids = map.Solid.Count;
            _propCount = map.PropInstances.Count;
            _propOfSolid = new int[map.Solid.Count];
            Array.Fill(_propOfSolid, -1);
            for (var k = 0; k < map.PropInstances.Count; k++)
            {
                var pi = map.PropInstances[k];
                for (var s = pi.SolidStart; s < pi.SolidStart + pi.SolidCount; s++)
                    if ((uint)s < (uint)_propOfSolid.Length)
                        _propOfSolid[s] = k;
            }
        }
        var idx = (uint)solid < (uint)_propOfSolid.Length ? _propOfSolid[solid] : -1;
        return idx < 0 ? MaterialKey.Default : MaterialPenetration.PropMaterial(map.PropInstances[idx].Def);
    }

    // ---- Impacts on both sides ----------------------------------------------------------------------------

    private static readonly PenetrationTrace Walker = new();

    /// <summary>Entry and exit marks placed since startup (for the log and the self-test).</summary>
    public static long EntryMarks, ExitMarks;

    /// <summary>
    /// A bullet that went through walls on its way from <paramref name="origin"/> to <paramref name="end"/> (the shot's
    /// origin and its tracer end, the same on every client): an impact decal where it went into each surface and a
    /// slightly bigger, rougher one where it came out (the far side's face), with a puff of dust or sparks on both sides.
    /// The mark at <paramref name="end"/> itself is <see cref="ImpactDecals.OnTracer"/>'s. Allocation-free; nothing when
    /// impact decals are off (servers, the headless self-test unless it turns them on). Returns the marks placed.
    /// </summary>
    public static int OnPenetratedTracer(Map map, Vector3 origin, Vector3 end, byte owner, double now, bool pellet, float scale)
    {
        if (ImpactDecals.Pool.Capacity == 0 || !ReferenceEquals(map, World.Map))
            return 0;
        var d = end - origin;
        var len = d.Length();
        if (len < 0.1f)
            return 0;
        d /= len;
        var w = Walker;
        w.Run(map, World.Physics, float.MaxValue, origin, d, MathF.Max(0, len - 0.03f), w.SurfaceEnd.Length);
        var placed = 0;
        for (var i = 0; i < w.PieceCount; i++)
        {
            ref var p = ref w.Pieces[i];
            var firstOfSurface = i == 0 || w.Pieces[i - 1].Surface != p.Surface;
            var lastOfSurface = i == w.PieceCount - 1 || w.Pieces[i + 1].Surface != p.Surface;
            if (firstOfSurface && Mark(map, p.Shape, origin + d * p.In, p.InNormal, d, owner, now, pellet, scale))
            {
                placed++;
                EntryMarks++;
            }
            if (lastOfSurface && Mark(map, p.Shape, origin + d * p.Out, p.OutNormal, d, owner, now, pellet, scale * ExitScale))
            {
                placed++;
                ExitMarks++;
            }
            if (firstOfSurface)
                Puff(origin + d * p.In, p.InNormal, p.Material);
            if (lastOfSurface)
                Puff(origin + d * p.Out, p.OutNormal, p.Material);
        }
        return placed;
    }

    /// <summary>Exit holes are this much bigger than the entry hole (splintered, blown out).</summary>
    public const float ExitScale = 1.3f;

    private static bool Mark(Map map, ShapeId shape, Vector3 point, Vector3 normal, Vector3 d, byte owner, double now, bool pellet, float scale)
    {
        var hit = new RayHit { Distance = 0, Point = point, Normal = normal, Shape = shape };
        return ImpactDecals.Place(map, hit, d, owner, now, pellet, MathF.Min(scale, ImpactDecals.MaxSize));
    }

    /// <summary>A few short-lived dust motes (sparks off metal) thrown off a face, through the shared particle pool.</summary>
    private static void Puff(Vector3 at, Vector3 normal, MaterialKey m)
    {
        var metal = m == MaterialKey.Metal;
        var color = metal ? new Vector3(1f, 0.7f, 0.35f) : m switch
        {
            MaterialKey.Wood => new Vector3(0.55f, 0.42f, 0.28f),
            MaterialKey.Brick => new Vector3(0.6f, 0.38f, 0.3f),
            MaterialKey.Plaster => new Vector3(0.85f, 0.83f, 0.78f),
            _ => new Vector3(0.6f, 0.58f, 0.55f),
        };
        var h = ImpactDecals.Hash(at);
        for (var k = 0; k < 4; k++)
        {
            var r1 = ((h >> (k * 8)) & 0xFF) / 255f - 0.5f;
            var r2 = ((h * 0x9E3779B1u >> (k * 8)) & 0xFF) / 255f - 0.5f;
            var side = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            var up = Vector3.Cross(normal, side);
            var vel = normal * (metal ? 4f : 1.6f) + side * r1 * 2f + up * r2 * 2f;
            FrenzyParticles.Spark(at + normal * 0.02f, vel, color, metal ? 0.015f : 0.03f, metal ? 0.25f : 0.45f, metal ? 9f : 2f, metal ? 1.5f : 3f, metal ? 0.035f : 0.01f, metal ? 1f : 0.7f);
        }
    }
}
