using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Map boundaries (the map file's "boundary" section): playable areas that aren't rectangles. See <see cref="MapBoundary"/>
/// for the outline and its queries, and Maps/README.md ("Boundaries") for the file format.
///
/// <para><b>Enforcement.</b> The outline is enforced twice: an analytic clamp (<see cref="MapBoundary.ClampFeet"/>) in both
/// movers' arena clamp, grenades and debris, which nothing can get past at any height, and invisible barrier pieces
/// (<see cref="BoundaryBarriers"/>, one oriented box per outline segment, its inner face exactly on the outline) in the
/// physics world's BVH under <see cref="QueryLayers.Boundary"/>, so bullets, line of sight, bot navigation probes, grenades
/// and the swept mover's capsule meet a real surface there. The barrier is as tall as the visible edge (at least 2.5 m)
/// so shots over a low fence fly out instead of sparking off thin air.</para>
///
/// <para><b>Looks.</b> Walls, fences, hedges and rocks are cosmetic mesh placements (one shared unit box or rock mesh,
/// stretched per segment, drawn instanced by the mesh renderer); the props style places real props; the cliff style
/// raises the terrain outside the outline (<see cref="TerrainGen"/>). On flat maps the Ground op's floor is drawn as a
/// mesh following the outline plus <see cref="BoundarySpec.GroundMargin"/>, with a darker ground beyond fading into fog.</para>
///
/// <para><b>Fingerprint.</b> Maps without a boundary are untouched (same boxes, same <see cref="ContentHash"/>); maps with
/// one mix <see cref="BoundaryHash"/> in.</para>
/// </summary>
public sealed partial class Map
{
    /// <summary>The map's boundary (null: the classic rectangle from HalfX / HalfZ, exactly as before boundaries existed).</summary>
    public MapBoundary? Boundary { get; private set; }

    /// <summary>The invisible barrier: one oriented box per outline segment, outside the outline with its inner face on it.</summary>
    public readonly List<Obb> BoundaryBarriers = new();

    /// <summary>Fingerprint of the outline and barrier (0 without a boundary), mixed into <see cref="ContentHash"/>.</summary>
    public uint BoundaryHash { get; private set; }

    /// <summary>Mesh placements the boundary made (the first ones in <see cref="MeshPlacements"/>): edge pieces and the floor.</summary>
    public int BoundaryMeshCount { get; private set; }

    private bool _boundaryBuilt;

    /// <summary>Playable area in m²: the outline's area, or the rectangle's.</summary>
    public float PlayArea => Boundary?.Area ?? HalfX * 2 * HalfZ * 2;

    /// <summary>Is (x, z) inside the playable area, at least <paramref name="margin"/> from its edge?</summary>
    public bool InsideBounds(float x, float z, float margin = 0f) =>
        Boundary is { } b ? b.Inside(x, z, margin) : MathF.Abs(x) <= HalfX - margin && MathF.Abs(z) <= HalfZ - margin;

    /// <summary>Is box i drawn by the box renderer? (Not prop colliders, nor the ground slab of a map with a boundary, whose floor is a mesh.)</summary>
    public bool IsDrawnBox(int i) => !IsPropCollider(i) && !(Boundary != null && i == GroundIndex) && !IsZombieBox(i) && !IsBuildingBox(i) && !IsHiddenBox(i); // hidden colliders: MapColliders.cs; Zombies doors and windows: Program.Zombies.cs; buildings: BuildingRenderer

    /// <summary>
    /// Gives the map a boundary. Must come before the terrain and any op. HalfX / HalfZ become the bounding box of the
    /// outline (around the origin). Call <see cref="FinishBoundary"/> after the terrain (if any) to build the barrier
    /// and the edge, or use <see cref="ApplyBoundary"/> for a map without terrain.
    /// </summary>
    public void SetBoundary(BoundarySpec? spec)
    {
        if (Solid.Count > 0 || Spawns.Count > 0 || Waypoints.Count > 0 || TerrainSpec != null)
            throw new InvalidOperationException("SetBoundary must be called before the terrain and any op");
        Boundary = null;
        _boundaryBuilt = false;
        if (spec == null)
            return;
        if (spec.Shape == BoundaryShape.Rect)
        {
            // Write the size into the spec, so the file written back out describes the same rectangle on its own.
            if (spec.HalfX <= 0)
                spec.HalfX = HalfX;
            if (spec.HalfZ <= 0)
                spec.HalfZ = HalfZ;
        }
        var b = new MapBoundary(spec, HalfX, HalfZ);
        Boundary = b;
        HalfX = Math.Clamp(MathF.Max(MathF.Abs(b.Min.X), MathF.Abs(b.Max.X)), 5f, 500f);
        HalfZ = Math.Clamp(MathF.Max(MathF.Abs(b.Min.Y), MathF.Abs(b.Max.Y)), 5f, 500f);
    }

    /// <summary><see cref="SetBoundary"/> then <see cref="FinishBoundary"/> (maps built in code without terrain).</summary>
    public void ApplyBoundary(BoundarySpec spec)
    {
        SetBoundary(spec);
        FinishBoundary();
    }

    /// <summary>Builds the barrier pieces and the visible edge. Runs once, after the terrain and before the ops.</summary>
    public void FinishBoundary()
    {
        if (Boundary is not { } b || _boundaryBuilt)
            return;
        _boundaryBuilt = true;
        if (b.Spec.Edge.Style == BoundaryEdgeStyle.Cliff && !HasTerrain)
            throw new InvalidOperationException("a \"cliff\" edge needs terrain (add a \"terrain\" section, or use \"wall\")");
        BuildBarrier(b);
        var before = MeshPlacements.Count;
        switch (b.Spec.Edge.Style)
        {
            case BoundaryEdgeStyle.Wall:
                BuildWallEdge(b);
                break;
            case BoundaryEdgeStyle.Hedge:
                BuildWallEdge(b, hedge: true);
                break;
            case BoundaryEdgeStyle.Fence:
                BuildFenceEdge(b);
                break;
            case BoundaryEdgeStyle.Rocks:
                BuildRockEdge(b);
                break;
            case BoundaryEdgeStyle.Props:
                BuildPropEdge(b);
                break;
        }
        BoundaryMeshCount = MeshPlacements.Count - before;
        BoundaryHash = HashBoundary(b);
    }

    /// <summary>Map files: hand-placed hills, flags and team spawns must be inside the boundary (spawns and waypoints are moved in by <see cref="Validate"/>).</summary>
    public void CheckInsideBoundary(string source)
    {
        if (Boundary is not { } b)
            return;
        void Check(Vector3 p, string what)
        {
            if (!b.Inside(p.X, p.Z))
                throw new MapFileException($"{source}: {what} at ({p.X:0.#}, {p.Z:0.#}) is outside the boundary");
        }
        if (HillData != null)
            foreach (var h in HillData)
                Check(h.Center, "hill");
        if (FlagData != null)
            foreach (var f in FlagData)
                Check(f, "flag");
        if (TeamSpawnData != null)
            foreach (var team in TeamSpawnData)
                foreach (var t in team)
                    Check(t, "team spawn");
    }

    /// <summary>Lowest and highest ground along a segment and a strip <paramref name="width"/> outside it (0 and 0 on flat maps).</summary>
    private (float Lo, float Hi) GroundAlong(Vector2 a, Vector2 c, Vector2 outward, float width)
    {
        if (!HasTerrain)
            return (0f, 0f);
        var step = MathF.Max(0.5f, Terrain!.Cell);
        var n = Math.Max(2, (int)MathF.Ceiling(Vector2.Distance(a, c) / step));
        var m = Math.Max(1, (int)MathF.Ceiling(width / step));
        float lo = float.MaxValue, hi = float.MinValue;
        for (var k = 0; k <= n; k++)
        for (var s = 0; s <= m; s++)
        {
            var p = Vector2.Lerp(a, c, k / (float)n) + outward * (width * s / m);
            var g = GroundHeight(p.X, p.Y);
            lo = MathF.Min(lo, g);
            hi = MathF.Max(hi, g);
        }
        return (lo, hi);
    }

    private void BuildBarrier(MapBoundary b)
    {
        BoundaryBarriers.Clear();
        var t = b.BarrierThickness;
        var height = b.BarrierHeight;
        var pts = b.Outline;
        for (var i = 0; i < pts.Length; i++)
        {
            var a = pts[i];
            var c = pts[(i + 1) % pts.Length];
            var n = b.Normals[i];
            var len = Vector2.Distance(a, c);
            var u = (c - a) / len;
            var (lo, hi) = GroundAlong(a, c, n, t);
            var y0 = lo - 2f;
            var y1 = hi + height;
            var mid = (a + c) * 0.5f + n * (t * 0.5f);
            BoundaryBarriers.Add(new Obb(new Vector3(mid.X, (y0 + y1) * 0.5f, mid.Y), new Vector3(len * 0.5f, (y1 - y0) * 0.5f, t * 0.5f),
                new Vector3(u.X, 0, u.Y), Vector3.UnitY, new Vector3(n.X, 0, n.Y)));
        }
    }

    /// <summary>A unit box (1 x 1 x 1, standing on y = 0) stretched to <paramref name="length"/> along <paramref name="dir"/>, height and depth.</summary>
    private static Matrix4x4 Stretch(Vector3 baseCenter, Vector3 dir, float length, float height, float depth)
    {
        // Rows are the images of the unit box's axes (System.Numerics row vectors): x along the segment, y up, z = x cross y.
        var x = dir * length;
        var z = new Vector3(-dir.Z, 0, dir.X);
        z = (z.LengthSquared() > 1e-8f ? Vector3.Normalize(z) : Vector3.UnitZ) * depth;
        return new Matrix4x4(x.X, x.Y, x.Z, 0, 0, height, 0, 0, z.X, z.Y, z.Z, 0, baseCenter.X, baseCenter.Y, baseCenter.Z, 1);
    }

    private static MeshAsset UnitBox => MeshLibrary.Primitive(new PrimitiveSpec(PrimitiveKind.Box, 1, 1, 1));

    /// <summary>Wall (or hedge) pieces: one per outline segment, outside it, inner face on the outline, lengthened at outward corners so the tops close.</summary>
    private void BuildWallEdge(MapBoundary b, bool hedge = false)
    {
        var t = b.EdgeThickness;
        var h = b.EdgeHeight;
        var color = b.EdgeColor;
        var layer = ParseMeshMaterial(b.EdgeMaterial);
        var box = UnitBox;
        var pts = b.Outline;
        var n = pts.Length;
        var rng = new Foliage.Rng((uint)b.Spec.Edge.Seed * 2654435761u + 17u);
        for (var i = 0; i < n; i++)
        {
            var a = pts[i];
            var c = pts[(i + 1) % n];
            var nrm = b.Normals[i];
            var len = Vector2.Distance(a, c);
            var u = (c - a) / len;
            // Turn at each end: a left turn (outward corner of the playable area) leaves a wedge gap on the outside.
            float Extend(Vector2 dIn, Vector2 dOut)
            {
                var cross = dIn.X * dOut.Y - dIn.Y * dOut.X;
                var dot = Vector2.Dot(dIn, dOut);
                var turn = MathF.Atan2(cross, dot);
                return turn > 0 ? MathF.Min(t, t * MathF.Tan(turn * 0.5f)) : 0f;
            }
            var uPrev = Vector2.Normalize(a - pts[(i - 1 + n) % n]);
            var uNext = Vector2.Normalize(pts[(i + 2) % n] - c);
            var e0 = Extend(uPrev, u);
            var e1 = Extend(u, uNext);
            var (lo, hi) = GroundAlong(a, c, nrm, t);
            var top = hi + h;
            var pieceH = top - (lo - 0.5f);
            var tint = color;
            if (hedge)
            {
                top += (rng.Next() - 0.5f) * 0.3f; // a slightly uneven, living top
                pieceH = top - (lo - 0.5f);
                tint = color * (0.9f + rng.Next() * 0.2f);
            }
            var mid = (a + c) * 0.5f + u * ((e1 - e0) * 0.5f) + nrm * (t * 0.5f);
            AddMeshPlacement(new MeshPlacement("boundary " + (hedge ? "hedge" : "wall"), box, null,
                Stretch(new Vector3(mid.X, lo - 0.5f, mid.Y), new Vector3(u.X, 0, u.Y), len + e0 + e1, pieceH, t), new Vector4(tint, 1), layer));
        }
    }

    /// <summary>Points every <paramref name="spacing"/> metres along the outline (arc length), with the segment each is on.</summary>
    private static List<(Vector2 P, int Seg)> AlongOutline(MapBoundary b, float spacing, float start = 0f)
    {
        var list = new List<(Vector2, int)>();
        var pts = b.Outline;
        var count = Math.Max(3, (int)MathF.Round(b.Perimeter / MathF.Max(spacing, 0.2f)));
        var step = b.Perimeter / count; // evenly spaced all the way round, so the last gap matches the others
        var next = start * step;
        float walked = 0;
        for (var i = 0; i < pts.Length && list.Count < count; i++)
        {
            var a = pts[i];
            var c = pts[(i + 1) % pts.Length];
            var len = Vector2.Distance(a, c);
            while (next <= walked + len && list.Count < count)
            {
                list.Add((Vector2.Lerp(a, c, (next - walked) / len), i));
                next += step;
            }
            walked += len;
        }
        return list;
    }

    private void BuildFenceEdge(MapBoundary b)
    {
        var h = b.EdgeHeight;
        var post = MathF.Max(0.06f, b.EdgeThickness);
        var color = b.EdgeColor;
        var layer = ParseMeshMaterial(b.EdgeMaterial);
        var box = UnitBox;
        var posts = AlongOutline(b, b.EdgeSpacing);
        var tops = new Vector3[posts.Count];
        for (var k = 0; k < posts.Count; k++)
        {
            var (p, seg) = posts[k];
            var at = p + b.Normals[seg] * (post * 0.5f + 0.02f);
            var g = GroundHeight(at.X, at.Y);
            tops[k] = new Vector3(at.X, g, at.Y);
            var u = b.Outline[(seg + 1) % b.Outline.Length] - b.Outline[seg];
            u = Vector2.Normalize(u);
            AddMeshPlacement(new MeshPlacement("boundary fence post", box, null,
                Stretch(new Vector3(at.X, g - 0.3f, at.Y), new Vector3(u.X, 0, u.Y), post, h + 0.3f, post), new Vector4(color * 0.9f, 1), layer));
        }
        for (var k = 0; k < posts.Count; k++)
        {
            var p0 = tops[k];
            var p1 = tops[(k + 1) % posts.Count];
            var d = p1 - p0;
            var len = d.Length();
            if (len < 0.05f)
                continue;
            var dir = d / len;
            foreach (var frac in (ReadOnlySpan<float>)[0.42f, 0.84f])
            {
                var y = h * frac;
                var mid = (p0 + p1) * 0.5f + new Vector3(0, y - 0.06f, 0);
                AddMeshPlacement(new MeshPlacement("boundary fence rail", box, null, Stretch(mid, dir, len, 0.12f, post * 0.5f), new Vector4(color, 1), layer));
            }
        }
    }

    private void BuildRockEdge(MapBoundary b)
    {
        var size = b.EdgeHeight;
        var color = b.EdgeColor;
        var layer = ParseMeshMaterial(b.EdgeMaterial);
        var rock = MeshLibrary.Primitive(new PrimitiveSpec(PrimitiveKind.IcoSphere, 1, 1, 1, Detail: 1));
        var rng = new Foliage.Rng((uint)b.Spec.Edge.Seed * 2246822519u + 3u);
        foreach (var (p, seg) in AlongOutline(b, b.EdgeSpacing))
        {
            var s = size * (0.6f + rng.Next() * 0.4f);
            var sx = s * (0.9f + rng.Next() * 0.5f);
            var sz = s * (0.8f + rng.Next() * 0.4f);
            var sy = s * (0.55f + rng.Next() * 0.35f);
            var yaw = rng.Next() * MathF.Tau;
            var tilt = (rng.Next() - 0.5f) * 0.3f;
            var tint = color * (0.85f + rng.Next() * 0.3f);
            var at = p + b.Normals[seg] * (MathF.Min(sx, sz) * 0.3f); // straddles the outline, mostly outside
            var g = HasTerrain ? LowestGroundUnder(at.X - sx * 0.4f, at.Y - sx * 0.4f, at.X + sx * 0.4f, at.Y + sx * 0.4f) : 0f;
            var m = Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateRotationZ(tilt) * Matrix4x4.CreateRotationY(yaw)
                    * Matrix4x4.CreateTranslation(at.X, g - sy * 0.25f, at.Y);
            AddMeshPlacement(new MeshPlacement("boundary rock", rock, null, m, new Vector4(tint, 1), layer));
        }
    }

    private void BuildPropEdge(MapBoundary b)
    {
        var defs = new List<PropDef>();
        var scope = PropScope; // pack props first (MapPackAssets.cs)
        foreach (var name in b.Spec.Edge.Props)
        {
            if (scope.Find(name) is { } one)
                defs.Add(one);
            else
            {
                var cat = scope.InCategory(name);
                if (cat.Count == 0)
                    throw new InvalidOperationException($"boundary edge: unknown prop or category \"{name}\": {scope.Suggest(name)}");
                defs.AddRange(cat);
            }
        }
        if (defs.Count == 0)
            throw new InvalidOperationException("a \"props\" edge needs a \"props\" list (prop or category names)");
        var rng = new Foliage.Rng((uint)b.Spec.Edge.Seed * 3266489917u + 11u);
        foreach (var (p, seg) in AlongOutline(b, b.EdgeSpacing, 0.5f))
        {
            var def = defs[(int)(rng.NextU() % (uint)defs.Count)];
            var u = Vector2.Normalize(b.Outline[(seg + 1) % b.Outline.Length] - b.Outline[seg]);
            var yaw = MathF.Atan2(-u.Y, u.X); // the prop's x axis along the outline
            var depth = def.Bounds.Half.Z + MathF.Abs(def.Bounds.Center.Z);
            var at = p + b.Normals[seg] * (depth + 0.05f);
            var (fmin, fmax) = Footprint(def, at.X, at.Y, yaw, 1f);
            var g = HasTerrain ? LowestGroundUnder(fmin.X, fmin.Y, fmax.X, fmax.Y) : 0f;
            AddProp(def, new Vector3(at.X, g, at.Y), yaw, 1f, null);
        }
    }

    /// <summary>
    /// Flat maps with a boundary: the Ground op's floor as a mesh covering the outline plus the ground margin (a marching
    /// squares contour of the signed distance, so its edge follows the shape smoothly), and a much larger, darker ground
    /// a little lower that fades into the fog. The ground box itself stays as the collider (it isn't drawn).
    /// </summary>
    private void AddBoundaryGround(Vector3 color, Material material)
    {
        if (Boundary is not { } b)
            return;
        var margin = Math.Clamp(b.Spec.GroundMargin, 0f, 64f);
        const float cell = 1f;
        float x0 = b.Min.X - margin - cell, z0 = b.Min.Y - margin - cell;
        var nx = (int)MathF.Ceiling((b.Max.X - b.Min.X + 2 * margin + 2 * cell) / cell) + 1;
        var nz = (int)MathF.Ceiling((b.Max.Y - b.Min.Y + 2 * margin + 2 * cell) / cell) + 1;
        var v = new float[nx * nz];
        for (var j = 0; j < nz; j++)
        for (var i = 0; i < nx; i++)
        {
            float x = x0 + i * cell, z = z0 + j * cell;
            v[j * nx + i] = b.QuickClearance(x, z) > margin + cell ? (b.Contains(x, z) ? -1e6f : 1e6f) : b.SignedDistance(x, z) - margin;
        }
        var mb = new MeshBuilder();
        Span<Vector2> poly = stackalloc Vector2[8];
        void Flat(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            var a = mb.Vertex(new Vector3(p0.X, 0, p0.Y), Vector3.UnitY, p0 * 0.5f);
            var bb = mb.Vertex(new Vector3(p1.X, 0, p1.Y), Vector3.UnitY, p1 * 0.5f);
            var cc = mb.Vertex(new Vector3(p2.X, 0, p2.Y), Vector3.UnitY, p2 * 0.5f);
            var d = mb.Vertex(new Vector3(p3.X, 0, p3.Y), Vector3.UnitY, p3 * 0.5f);
            mb.Quad(a, bb, cc, d);
        }
        for (var j = 0; j + 1 < nz; j++)
        {
            // Runs of cells that are inside at all four corners become one long quad per row (few vertices on big maps).
            var run = -1;
            for (var i = 0; i + 1 <= nx; i++)
            {
                var full = i + 1 < nx && v[j * nx + i] <= 0 && v[j * nx + i + 1] <= 0 && v[(j + 1) * nx + i] <= 0 && v[(j + 1) * nx + i + 1] <= 0;
                if (full && run < 0)
                    run = i;
                if (!full && run >= 0)
                {
                    float xa = x0 + run * cell, xb = x0 + i * cell, za = z0 + j * cell, zb = za + cell;
                    Flat(new(xa, za), new(xb, za), new(xb, zb), new(xa, zb));
                    run = -1;
                }
            }
        }
        for (var j = 0; j + 1 < nz; j++)
        for (var i = 0; i + 1 < nx; i++)
        {
            if (v[j * nx + i] <= 0 && v[j * nx + i + 1] <= 0 && v[(j + 1) * nx + i] <= 0 && v[(j + 1) * nx + i + 1] <= 0)
                continue; // drawn by a run above
            // Corners counter-clockwise seen from above with +z down the screen: (i,j) (i+1,j) (i+1,j+1) (i,j+1).
            Span<float> val = [v[j * nx + i], v[j * nx + i + 1], v[(j + 1) * nx + i + 1], v[(j + 1) * nx + i]];
            Span<Vector2> cor = [new(x0 + i * cell, z0 + j * cell), new(x0 + (i + 1) * cell, z0 + j * cell), new(x0 + (i + 1) * cell, z0 + (j + 1) * cell), new(x0 + i * cell, z0 + (j + 1) * cell)];
            var count = 0;
            for (var k = 0; k < 4; k++)
            {
                var k2 = (k + 1) & 3;
                if (val[k] <= 0)
                    poly[count++] = cor[k];
                if ((val[k] <= 0) != (val[k2] <= 0))
                {
                    var f = val[k] / (val[k] - val[k2]);
                    poly[count++] = Vector2.Lerp(cor[k], cor[k2], Math.Clamp(f, 0f, 1f));
                }
            }
            if (count < 3)
                continue;
            var first = mb.Vertex(new Vector3(poly[0].X, 0, poly[0].Y), Vector3.UnitY, poly[0] * 0.5f);
            var prev = mb.Vertex(new Vector3(poly[1].X, 0, poly[1].Y), Vector3.UnitY, poly[1] * 0.5f);
            for (var k = 2; k < count; k++)
            {
                var cur = mb.Vertex(new Vector3(poly[k].X, 0, poly[k].Y), Vector3.UnitY, poly[k] * 0.5f);
                // Wound so the face points up (front faces are counter-clockwise seen from above, +y).
                mb.Triangle(first, cur, prev);
                prev = cur;
            }
        }
        var mesh = mb.Build();
        if (mesh.TriangleCount == 0)
            return;
        var asset = new MeshAsset($"boundary floor {Name} {b.Outline.Length} {margin}", [mesh], false, "none");
        var layer = (float)material;
        MeshPlacements.Insert(BoundaryMeshCount, new MeshPlacement("boundary floor", asset, null, Matrix4x4.Identity, new Vector4(color, 1), layer));
        BoundaryMeshCount++;
        // The far ground: one big low slab, darker, so the shape reads and the view fades into the fog instead of the sky.
        var far = MathF.Max(FarPlane * 1.5f, 400f);
        var c = b.BoxCenter;
        MeshPlacements.Insert(BoundaryMeshCount, new MeshPlacement("boundary far ground", UnitBox, null,
            Stretch(new Vector3(c.X, -0.5f, c.Y), Vector3.UnitX, far * 2, 0.35f, far * 2), new Vector4(color * 0.45f, 1), layer));
        BoundaryMeshCount++;
    }

    private uint HashBoundary(MapBoundary b)
    {
        var h = 2166136261u;
        void Mix(float f)
        {
            var bits = BitConverter.SingleToUInt32Bits(MathF.Round(f * 1000f) / 1000f);
            for (var i = 0; i < 4; i++)
            {
                h ^= (bits >> (i * 8)) & 0xFF;
                h *= 16777619u;
            }
        }
        Mix(b.Outline.Length);
        foreach (var p in b.Outline)
        {
            Mix(p.X);
            Mix(p.Y);
        }
        Mix((float)b.Spec.Edge.Style);
        foreach (var o in BoundaryBarriers)
        {
            Mix(o.Center.Y);
            Mix(o.Half.Y);
            Mix(o.Half.Z);
        }
        return h == 0 ? 1u : h;
    }
}
