using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>A run of decal triangles in the map's decal buffer: one 24 m chunk of one blend mode.</summary>
public readonly record struct DecalChunk(Vector3 Min, Vector3 Max, int First, int Count, DecalBlend Blend, float FadeEnd);

/// <summary>
/// A map's decals, projected onto its surfaces and ready to upload: triangles (<see cref="FloatsPerVertex"/> floats per
/// vertex, no index buffer), grouped into chunks by blend mode (multiply first) and 24 m squares so the renderer can
/// cull them by the view frustum and distance and merge neighbours into one draw.
/// </summary>
public sealed class DecalMesh
{
    /// <summary>Position (3), normal (3), uv (2), atlas layer, owner box (-1: none), fade distance, colour (rgb + alpha).</summary>
    public const int FloatsPerVertex = 15;

    public float[] Vertices = [];
    public int VertexCount;
    public DecalChunk[] Chunks = [];

    /// <summary>Decals asked for, placed, and not placed (no surface found, or a surface the decal doesn't allow).</summary>
    public int Requested, Placed, Missed;

    /// <summary>The boxes as they were when baked; a decal on box i shows only while the map's box i is still this.</summary>
    public Box[] Owners = [];

    /// <summary>Did the vertex budget cut decals off?</summary>
    public bool Truncated;

    public double BuildMs;

    public int Triangles => VertexCount / 3;

    public static readonly DecalMesh Empty = new();
}

/// <summary>
/// Projects a map's Decal and DecalScatter ops onto its surfaces (CPU, once per map). Each decal is an oriented box (the
/// projector): the surface is found with a ray, then every triangle of the boxes, terrain and meshes inside the projector
/// that faces it is clipped to the box, so decals wrap over edges and kerbs and stop at holes in the terrain. Triangles
/// keep the box they lie on as their "owner": when that box breaks or moves (a door opening) the renderer hides them.
/// Deterministic: the same map gives the same triangles on every PC.
/// </summary>
public static class DecalBaker
{
    /// <summary>Chunk size (metres) for culling.</summary>
    public const float ChunkSize = 24f;

    /// <summary>The most decal vertices one map may have (beyond it decals are left out and the log says so).</summary>
    public const int MaxVertices = 400_000;

    /// <summary>Surfaces less square-on to the projector than this (cosine) get no decal.</summary>
    public const float MinFacing = 0.25f;

    /// <summary>Decal vertices sit this far off the surface (plus a polygon offset when drawn).</summary>
    public const float SurfaceLift = 0.0015f;

    /// <summary>A placed decal: its projector box and look.</summary>
    public readonly record struct Placement(DecalDef Def, Vector3 Center, Vector3 Normal, Vector3 Up, Vector3 Right, float HalfW, float HalfH, float HalfD, Vector4 Color);

    public static DecalMesh Bake(Map map)
    {
        var sw = Stopwatch.StartNew();
        if (map.DecalRequests.Count == 0)
            return DecalMesh.Empty;
        var mesh = new DecalMesh { Requested = map.DecalCount };

        // The surfaces as built (broken props whole, doors shut), with everything decals don't land on parked out of the way.
        var boxes = new Box[map.Solid.Count];
        for (var i = 0; i < boxes.Length; i++)
            boxes[i] = map.IsDecalSurface(i) ? map.HashBox(i) : new Box(new Vector3(0, -1000f, 0), new Vector3(0.01f));
        mesh.Owners = boxes;
        var world = new PhysicsWorld(boxes, map.TerrainCollision, map.HalfX, map.HalfZ);
        AddMeshes(map, world);

        var placements = Resolve(map, world, mesh);
        Emit(map, world, placements, mesh);
        mesh.BuildMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[decals] baked '{map.Name}': {mesh.Placed}/{mesh.Requested} decals placed ({mesh.Missed} found no allowed surface), "
                          + $"{mesh.Triangles} triangles in {mesh.Chunks.Length} chunks, {mesh.BuildMs:0.0} ms" + (mesh.Truncated ? " (vertex budget reached: some left out)" : ""));
        return mesh;
    }

    /// <summary>Adds the Mesh / Model placements near any decal to the bake world (they're cosmetic, so the game world has none).</summary>
    private static void AddMeshes(Map map, PhysicsWorld world)
    {
        if (map.MeshPlacements.Count == 0)
            return;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var r in map.DecalRequests)
        {
            var pad = r.Reach + MathF.Max(r.MaxSize, 4f);
            min = Vector2.Min(min, new Vector2(r.X0 - pad, r.Z0 - pad));
            max = Vector2.Max(max, new Vector2(r.X1 + pad, r.Z1 + pad));
        }
        foreach (var p in map.MeshPlacements)
        {
            if (p.Source is "boundary floor" or "boundary far ground")
                continue; // a shaped map's drawn ground lies on its ground box (MapEdges.cs): decals use the box
            var b = p.Bounds;
            if (b.IsEmpty || b.Max.X < min.X || b.Min.X > max.X || b.Max.Z < min.Y || b.Min.Z > max.Y)
                continue;
            try
            {
                var col = p.BuildCollision();
                if (col.TriangleCount > 0)
                    world.AddMesh(TriangleMeshCollider.FromMesh(col, Matrix4x4.Identity));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException or IndexOutOfRangeException)
            {
                // no usable triangles: nothing to put decals on
            }
        }
    }

    private static readonly QueryFilter SurfaceFilter = new(QueryLayers.Boxes | QueryLayers.Terrain | QueryLayers.Meshes);

    // ---- Placing ------------------------------------------------------------------------------------------------

    private static List<Placement> Resolve(Map map, PhysicsWorld world, DecalMesh mesh)
    {
        var list = new List<Placement>();
        var placedAt = new List<Vector3>();
        foreach (var r in map.DecalRequests)
        {
            var defs = r.Names.Select(n => Decals.Find(n)).Where(d => d != null).Select(d => d!).ToArray();
            if (defs.Length == 0)
            {
                mesh.Missed += r.Scatter ? r.Count : 1;
                continue;
            }
            var rng = new DecalAtlas.Rng((uint)(r.Seed * 7919 + r.OpIndex * 104729 + 1));
            if (!r.Scatter)
            {
                if (TryPlace(map, world, r, defs[0], r.X0, r.Z0, r.Y, ref rng, out var p))
                {
                    list.Add(p);
                    mesh.Placed++;
                }
                else
                    mesh.Missed++;
                continue;
            }
            placedAt.Clear();
            var placed = 0;
            for (var attempt = 0; attempt < r.Count * 4 && placed < r.Count; attempt++)
            {
                var x = r.X0 + (r.X1 - r.X0) * rng.Next();
                var z = r.Z0 + (r.Z1 - r.Z0) * rng.Next();
                var y = r.Onto is DecalOnto.Floor or DecalOnto.Ceiling ? r.Y : r.Y + 0.3f + 2.1f * rng.Next();
                var def = defs[(int)(rng.Next() * defs.Length) % defs.Length];
                if (!TryPlace(map, world, r, def, x, z, y, ref rng, out var p))
                    continue;
                if (r.Gap > 0 && placedAt.Any(q => Vector3.DistanceSquared(q, p.Center) < r.Gap * r.Gap))
                    continue;
                placedAt.Add(p.Center);
                list.Add(p);
                placed++;
            }
            mesh.Placed += placed;
            mesh.Missed += r.Count - placed;
        }
        return list;
    }

    /// <summary>Finds the surface for one decal and builds its projector. False: no surface within reach, or one the decal doesn't allow.</summary>
    internal static bool TryPlace(Map map, PhysicsWorld world, DecalRequest r, DecalDef def, float x, float z, float y, ref DecalAtlas.Rng rng, out Placement placement)
    {
        placement = default;
        var baseY = (r.OnGround ? map.GroundHeight(x, z) : 0f) + y;
        Vector3 origin, dir;
        float reach;
        RayHit hit;
        switch (r.Onto)
        {
            case DecalOnto.Floor:
                origin = new Vector3(x, baseY + 1f, z);
                dir = -Vector3.UnitY;
                reach = r.Reach + 1f;
                break;
            case DecalOnto.Ceiling:
                origin = new Vector3(x, baseY, z);
                dir = Vector3.UnitY;
                reach = r.Reach;
                break;
            case DecalOnto.Wall:
            {
                origin = new Vector3(x, baseY, z);
                dir = default;
                reach = r.Reach;
                var best = float.MaxValue;
                ReadOnlySpan<Vector3> sides = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ];
                foreach (var d in sides)
                    if (world.Raycast(origin, d, r.Reach, SurfaceFilter, out var h) && h.Distance < best)
                    {
                        best = h.Distance;
                        dir = d;
                    }
                if (best == float.MaxValue)
                    return false;
                break;
            }
            default:
                origin = new Vector3(x, baseY, z);
                dir = r.Onto switch { DecalOnto.WallPosX => Vector3.UnitX, DecalOnto.WallNegX => -Vector3.UnitX, DecalOnto.WallPosZ => Vector3.UnitZ, _ => -Vector3.UnitZ };
                reach = r.Reach;
                break;
        }
        if (!world.Raycast(origin, dir, reach, SurfaceFilter, out hit) || hit.Distance <= 1e-4f)
            return false;
        if ((def.Surfaces & Classify(hit.Normal)) == 0)
            return false;

        // Size, turn and brightness (the rng is consumed the same way every time, so results never shift).
        var sizeRoll = rng.Next();
        var rotRoll = rng.Next();
        var tintRoll = rng.Next();
        var maxSide = MathF.Max(def.Size.X, def.Size.Y);
        float scale;
        if (r.MaxSize > 0)
            scale = (r.Size + (r.MaxSize - r.Size) * sizeRoll) / maxSide;
        else
            scale = 1f + (sizeRoll * 2f - 1f) * def.SizeVariation;
        var rotation = r.Rotation >= 0 ? r.Rotation : float.IsNaN(def.Rotation) ? rotRoll * 360f : def.Rotation;
        var bright = 1f + (tintRoll * 2f - 1f) * def.TintVariation;

        var n = -dir;
        var up = MathF.Abs(n.Y) > 0.9f ? -Vector3.UnitZ : Vector3.UnitY; // floors: the top of the picture points north (-z)
        if (n.Y < -0.9f)
            up = Vector3.UnitZ; // ceilings, seen from below
        var a = rotation * MathF.PI / 180f;
        var q = Quaternion.CreateFromAxisAngle(n, a);
        up = Vector3.Normalize(Vector3.Transform(up, q));
        var right = Vector3.Cross(up, n);
        float hw = def.Size.X * scale * 0.5f, hh = def.Size.Y * scale * 0.5f;
        var hd = Math.Clamp(0.3f * MathF.Max(hw, hh), 0.12f, 0.6f);
        var color = new Vector4(r.Tint * bright, def.Opacity);
        placement = new Placement(def, hit.Point, n, up, right, hw, hh, hd, color);
        return true;
    }

    /// <summary>Which way a surface with normal <paramref name="n"/> faces.</summary>
    public static DecalSurfaces Classify(Vector3 n) => n.Y > 0.7f ? DecalSurfaces.Floor : n.Y < -0.7f ? DecalSurfaces.Ceiling : DecalSurfaces.Wall;

    // ---- Clipping ------------------------------------------------------------------------------------------------

    private struct Collect : IBvhOverlapVisitor
    {
        public List<int> Items;

        public bool Visit(int item)
        {
            Items.Add(item);
            return false;
        }
    }

    private sealed class Builder
    {
        public readonly Dictionary<(int Blend, int Cx, int Cz), (List<float> V, float Fade)> Chunks = new();
        public int Vertices;
        public bool Full;
    }

    private static void Emit(Map map, PhysicsWorld world, List<Placement> placements, DecalMesh mesh)
    {
        var b = new Builder();
        var items = new List<int>();
        var tris = new List<int>();
        Span<Vector3> poly = stackalloc Vector3[16];
        Span<Vector3> tmp = stackalloc Vector3[16];
        var terrain = map.Terrain;
        foreach (var p in placements)
        {
            if (b.Full)
            {
                mesh.Truncated = true;
                break;
            }
            var key = ((int)p.Def.Blend, (int)MathF.Floor(p.Center.X / ChunkSize), (int)MathF.Floor(p.Center.Z / ChunkSize));
            if (!b.Chunks.TryGetValue(key, out var chunk))
                chunk = (new List<float>(), 0f);
            chunk.Fade = MathF.Max(chunk.Fade, p.Def.FadeEnd);
            var verts = chunk.V;

            // The projector's world bounds.
            var ext = new Vector3(
                MathF.Abs(p.Right.X) * p.HalfW + MathF.Abs(p.Up.X) * p.HalfH + MathF.Abs(p.Normal.X) * p.HalfD,
                MathF.Abs(p.Right.Y) * p.HalfW + MathF.Abs(p.Up.Y) * p.HalfH + MathF.Abs(p.Normal.Y) * p.HalfD,
                MathF.Abs(p.Right.Z) * p.HalfW + MathF.Abs(p.Up.Z) * p.HalfH + MathF.Abs(p.Normal.Z) * p.HalfD);
            Vector3 min = p.Center - ext, max = p.Center + ext;

            items.Clear();
            var visitor = new Collect { Items = items };
            world.Tree.Overlap(min, max, ref visitor);
            foreach (var item in items)
            {
                if (item < world.BoxCount)
                {
                    var box = world.Boxes[item];
                    if (box.Center.Y < -500f)
                        continue; // parked: not a decal surface
                    ClipBox(p, box, item, poly, tmp, verts, b);
                }
                else if (item >= world.BoxCount + world.ObbCount && item < world.BoxCount + world.ObbCount + world.MeshCount)
                {
                    var m = world.GetMesh(item - world.BoxCount - world.ObbCount);
                    tris.Clear();
                    var tv = new Collect { Items = tris };
                    m.Tree.Overlap(min, max, ref tv);
                    foreach (var t in tris)
                    {
                        m.Triangle(t, out var a, out var bb, out var c);
                        var n = Vector3.Cross(bb - a, c - a);
                        if (n.LengthSquared() < 1e-12f)
                            continue;
                        n = Vector3.Normalize(n);
                        if (Vector3.Dot(n, p.Normal) < 0)
                            n = -n; // mesh triangles are two-sided: use the side facing the projector
                        ClipTriangle(p, a, bb, c, n, n, n, -1, poly, tmp, verts, b);
                    }
                }
            }
            if (terrain != null)
                ClipTerrain(p, terrain, min, max, poly, tmp, verts, b);
            b.Chunks[key] = chunk;
        }

        // Assemble: multiply chunks first (drawn first), then alpha; each by row then column.
        var ordered = b.Chunks.OrderBy(k => k.Key.Blend == (int)DecalBlend.Multiply ? 0 : 1).ThenBy(k => k.Key.Cz).ThenBy(k => k.Key.Cx).ToList();
        var total = ordered.Sum(k => k.Value.V.Count);
        mesh.Vertices = new float[total];
        var chunks = new List<DecalChunk>();
        var w = 0;
        foreach (var (key, (v, fade)) in ordered)
        {
            if (v.Count == 0)
                continue;
            var cmin = new Vector3(float.MaxValue);
            var cmax = new Vector3(float.MinValue);
            for (var i = 0; i < v.Count; i += DecalMesh.FloatsPerVertex)
            {
                var pos = new Vector3(v[i], v[i + 1], v[i + 2]);
                cmin = Vector3.Min(cmin, pos);
                cmax = Vector3.Max(cmax, pos);
            }
            v.CopyTo(mesh.Vertices, w);
            chunks.Add(new DecalChunk(cmin, cmax, w / DecalMesh.FloatsPerVertex, v.Count / DecalMesh.FloatsPerVertex, (DecalBlend)key.Blend, fade));
            w += v.Count;
        }
        mesh.VertexCount = w / DecalMesh.FloatsPerVertex;
        mesh.Chunks = chunks.ToArray();
    }

    private static readonly (Vector3 N, Vector3 U, Vector3 V)[] Faces =
    [
        (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
        (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
        (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX),
        (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
        (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
        (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
    ];

    private static void ClipBox(in Placement p, in Box box, int owner, Span<Vector3> poly, Span<Vector3> tmp, List<float> verts, Builder b)
    {
        foreach (var (n, u, v) in Faces)
        {
            if (Vector3.Dot(n, p.Normal) < MinFacing)
                continue;
            var c = box.Center + n * Vector3.Dot(box.Half, Vector3.Abs(n));
            var hu = u * Vector3.Dot(box.Half, Vector3.Abs(u));
            var hv = v * Vector3.Dot(box.Half, Vector3.Abs(v));
            Vector3 a = c - hu - hv, bb = c + hu - hv, d = c + hu + hv, e = c - hu + hv;
            ClipTriangle(p, a, bb, d, n, n, n, owner, poly, tmp, verts, b);
            ClipTriangle(p, a, d, e, n, n, n, owner, poly, tmp, verts, b);
        }
    }

    private static void ClipTerrain(in Placement p, TerrainData t, Vector3 min, Vector3 max, Span<Vector3> poly, Span<Vector3> tmp, List<float> verts, Builder b)
    {
        if (max.Y < t.MinHeight - 0.01f)
            return;
        int cellsX = t.CountX - 1, cellsZ = t.CountZ - 1;
        var i0 = Math.Clamp((int)MathF.Floor((min.X - t.OriginX) / t.Cell), 0, cellsX - 1);
        var i1 = Math.Clamp((int)MathF.Floor((max.X - t.OriginX) / t.Cell), 0, cellsX - 1);
        var j0 = Math.Clamp((int)MathF.Floor((min.Z - t.OriginZ) / t.Cell), 0, cellsZ - 1);
        var j1 = Math.Clamp((int)MathF.Floor((max.Z - t.OriginZ) / t.Cell), 0, cellsZ - 1);
        if ((i1 - i0 + 1) * (j1 - j0 + 1) > 4096)
            return; // a projector that large over the terrain is a mistake; skip rather than stall
        for (var j = j0; j <= j1; j++)
        for (var i = i0; i <= i1; i++)
        {
            if (t.CellKind(i, j) == TerrainVoids.Open)
                continue; // a hole: no surface for the decal
            int a = j * t.CountX + i, bi = a + 1, c = a + t.CountX, d = c + 1;
            float ha = t.Heights[a], hb = t.Heights[bi], hc = t.Heights[c], hd = t.Heights[d];
            if (MathF.Max(MathF.Max(ha, hb), MathF.Max(hc, hd)) < min.Y || MathF.Min(MathF.Min(ha, hb), MathF.Min(hc, hd)) > max.Y)
                continue;
            float x0 = t.OriginX + i * t.Cell, z0 = t.OriginZ + j * t.Cell, x1 = x0 + t.Cell, z1 = z0 + t.Cell;
            Vector3 pa = new(x0, ha, z0), pb = new(x1, hb, z0), pc = new(x0, hc, z1), pd = new(x1, hd, z1);
            Vector3 na = t.VertexNormal(i, j), nb = t.VertexNormal(i + 1, j), nc = t.VertexNormal(i, j + 1), nd = t.VertexNormal(i + 1, j + 1);
            // The same diagonal as the terrain mesh (TerrainRenderer.SurfaceIndices), so the decal lies exactly on it.
            if (MathF.Abs(ha - hd) <= MathF.Abs(hb - hc))
            {
                ClipTriangle(p, pa, pc, pd, na, nc, nd, -1, poly, tmp, verts, b);
                ClipTriangle(p, pa, pd, pb, na, nd, nb, -1, poly, tmp, verts, b);
            }
            else
            {
                ClipTriangle(p, pa, pc, pb, na, nc, nb, -1, poly, tmp, verts, b);
                ClipTriangle(p, pb, pc, pd, nb, nc, nd, -1, poly, tmp, verts, b);
            }
        }
    }

    /// <summary>
    /// Clips triangle (a, b, c) to the projector box and appends the pieces as triangles. Works in the projector's unit
    /// space (-1..1 on each axis); normals are interpolated with barycentric weights of the original corners.
    /// </summary>
    private static void ClipTriangle(in Placement p, Vector3 a, Vector3 bb, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, int owner,
        Span<Vector3> poly, Span<Vector3> tmp, List<float> verts, Builder b)
    {
        var faceN = Vector3.Cross(bb - a, c - a);
        if (faceN.LengthSquared() < 1e-14f)
            return;
        faceN = Vector3.Normalize(faceN);
        if (Vector3.Dot(faceN, p.Normal) < 0)
            faceN = -faceN;
        var facing = Vector3.Dot(faceN, p.Normal);
        if (facing < MinFacing || (p.Def.Surfaces & Classify(faceN)) == 0)
            return;
        poly[0] = Local(p, a);
        poly[1] = Local(p, bb);
        poly[2] = Local(p, c);
        var count = 3;
        for (var axis = 0; axis < 3 && count > 0; axis++)
        {
            count = ClipPlane(poly, count, tmp, axis, 1f);
            count = ClipPlane(tmp, count, poly, axis, -1f);
        }
        if (count < 3)
            return;
        if (b.Vertices + (count - 2) * 3 > MaxVertices)
        {
            b.Full = true;
            return;
        }
        var la = Local(p, a);
        var lb = Local(p, bb);
        var lc = Local(p, c);
        var facingFade = Smooth(MinFacing, MinFacing + 0.3f, facing);
        for (var k = 1; k + 1 < count; k++)
        {
            Write(p, poly[0], la, lb, lc, na, nb, nc, owner, facingFade, verts);
            Write(p, poly[k], la, lb, lc, na, nb, nc, owner, facingFade, verts);
            Write(p, poly[k + 1], la, lb, lc, na, nb, nc, owner, facingFade, verts);
            b.Vertices += 3;
        }
    }

    private static Vector3 Local(in Placement p, Vector3 w)
    {
        var d = w - p.Center;
        return new Vector3(Vector3.Dot(d, p.Right) / p.HalfW, Vector3.Dot(d, p.Up) / p.HalfH, Vector3.Dot(d, p.Normal) / p.HalfD);
    }

    /// <summary>Sutherland-Hodgman against one face of the unit box: keeps the side where sign * coordinate &lt;= 1.</summary>
    private static int ClipPlane(Span<Vector3> src, int count, Span<Vector3> dst, int axis, float sign)
    {
        var n = 0;
        for (var i = 0; i < count; i++)
        {
            var cur = src[i];
            var next = src[(i + 1) % count];
            var dc = 1f - sign * cur[axis];
            var dn = 1f - sign * next[axis];
            if (dc >= 0)
            {
                if (n < dst.Length)
                    dst[n++] = cur;
            }
            if ((dc >= 0) != (dn >= 0))
            {
                var t = dc / (dc - dn);
                if (n < dst.Length)
                    dst[n++] = cur + (next - cur) * t;
            }
        }
        return n;
    }

    private static void Write(in Placement p, Vector3 l, Vector3 la, Vector3 lb, Vector3 lc, Vector3 na, Vector3 nb, Vector3 nc, int owner, float facingFade, List<float> verts)
    {
        // Barycentric weights of l in the original triangle (in projector space) for the normal.
        var bary = Barycentric(l, la, lb, lc);
        var n = na * bary.X + nb * bary.Y + nc * bary.Z;
        n = n.LengthSquared() > 1e-10f ? Vector3.Normalize(n) : p.Normal;
        if (Vector3.Dot(n, p.Normal) < 0)
            n = -n;
        var world = p.Center + p.Right * (l.X * p.HalfW) + p.Up * (l.Y * p.HalfH) + p.Normal * (l.Z * p.HalfD) + n * SurfaceLift;
        var depthFade = 1f - Smooth(0.6f, 1f, MathF.Abs(l.Z)); // soft where the projector's depth runs out (over steps)
        verts.Add(world.X); verts.Add(world.Y); verts.Add(world.Z);
        verts.Add(n.X); verts.Add(n.Y); verts.Add(n.Z);
        verts.Add(Math.Clamp(l.X * 0.5f + 0.5f, 0f, 1f)); verts.Add(Math.Clamp(l.Y * 0.5f + 0.5f, 0f, 1f));
        verts.Add(p.Def.Index); verts.Add(owner); verts.Add(p.Def.FadeEnd);
        verts.Add(p.Color.X); verts.Add(p.Color.Y); verts.Add(p.Color.Z); verts.Add(p.Color.W * facingFade * depthFade);
    }

    private static Vector3 Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
        float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1), d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
        var den = d00 * d11 - d01 * d01;
        if (MathF.Abs(den) < 1e-12f)
            return new Vector3(1, 0, 0);
        var v = (d11 * d20 - d01 * d21) / den;
        var w = (d00 * d21 - d01 * d20) / den;
        return new Vector3(1 - v - w, v, w);
    }

    private static float Smooth(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
