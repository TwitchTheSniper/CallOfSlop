using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>"Compound collision": a construct's collider is every solid box of every part (docs/sandbox/CONSTRUCTS.md, "Compound collision").</summary>
    private static void CheckCompoundCollision()
    {
        CollisionHulls();
        CollisionNest(false);
        CollisionNest(true);
        CollisionEdge();
        CollisionGap();
        CollisionHook();
        CollisionFastThin();
        CollisionHeldSlot();
        CollisionHeldSmooth();
        CollisionWedge();
        CollisionGround();
        CollisionStreamed();
        CollisionShapes();
        CollisionStack();
        CollisionDeterministic();
        CollisionCost();
    }

    private static ConstructPartDef Block => ConstructParts.Need("Frame Block");

    /// <summary>A free construct of the parts at those spots.</summary>
    private static Construct CollBody(ConstructWorld w, Vector3 at, params (ConstructPartDef Def, Vector3 Pos)[] parts)
    {
        var c = w.Create(at, Quaternion.Identity)!;
        foreach (var (d, p) in parts)
            w.AddPart(c, d, p, 0);
        c.Rebuild();
        return c;
    }

    /// <summary>The deepest a piece of <paramref name="c"/> sits inside the box (0: not inside), by the same test the physics uses.</summary>
    private static float CollDepth(Construct c, Vector3 min, Vector3 max)
    {
        var box = new WBox { C = (min + max) * 0.5f, X = Vector3.UnitX, Y = Vector3.UnitY, Z = Vector3.UnitZ, H = (max - min) * 0.5f, Hull = -1 };
        var col = c.Collider;
        var deep = 0f;
        Span<CPoint> pts = stackalloc CPoint[8];
        for (var g = 0; g < col.GroupCount; g++)
        {
            c.WorldFrame(col.Groups[g].Seg, out var q, out var t);
            var f = CFrame.Of(q, t);
            for (var k = col.Groups[g].First; k < col.Groups[g].First + col.Groups[g].Count; k++)
            {
                col.World(k, f, out var w);
                var n = ConstructNarrow.BoxBox(w, box, 0, pts, 0);
                for (var i = 0; i < n; i++)
                    deep = MathF.Max(deep, -pts[i].Gap);
            }
        }
        return deep;
    }

    /// <summary>Every shipped wedge and cylinder collides as its shape; an arc keeps its boxes.</summary>
    private static void CollisionHulls()
    {
        int wedges = 0, cylinders = 0, kept = 0;
        foreach (var d in ConstructParts.All)
            for (var b = d.SolidBoxes; b < d.Boxes.Length; b++)
            {
                var kind = PartShapes.KindOf(d.Boxes[b].Shape);
                if (kind is not ("wedge" or "cylinder" or "tube"))
                    continue;
                if (ConstructHulls.Of(d).Length == 0)
                {
                    if (d.Class == ConstructParts.Small && !d.ColliderBoxes)
                        kept++; // (a big class's smooth pipes and drums, 40 corners, and "collider": "boxes" machines: their solid boxes stand for them)
                    continue;
                }
                if (kind == "wedge")
                    wedges++;
                else
                    cylinders++;
            }
        Line($"compound collision: {wedges} wedge and {cylinders} cylinder boxes collide as their shape, {kept} kept their boxes");
        if (wedges < 40 || cylinders < 10 || kept > 6) // (kept: the Small Nuke's and a small engine piece's smooth cylinders, more than a hull's 32 corners)
            Fail($"compound collision: shapes without a real collider: {wedges} wedges, {cylinders} cylinders, {kept} on boxes");
        // A hull's ray and its bounds agree with a plain box's for a box-shaped hull.
        var points = new Vector3[8];
        for (var k = 0; k < 8; k++)
            points[k] = new Vector3((k & 1) != 0 ? 1 : -1, (k & 2) != 0 ? 1 : -1, (k & 4) != 0 ? 1 : -1);
        var h = ConvexHull.Of(points)!;
        if (h.V.Length != 8 || h.N.Length != 6 || h.Dirs.Length != 3 || !h.Ray(new Vector3(0, 5, 0), -Vector3.UnitY, 10, out var dist, out var n) || MathF.Abs(dist - 4) > 1e-3f || n != Vector3.UnitY)
            Fail($"compound collision: a cube's hull is wrong: {h.V.Length} corners, {h.N.Length} faces, {h.Dirs.Length} directions");
    }

    /// <summary>A U of blocks slides round a post (a map box, then an anchored construct), which no corner of it meets: the post goes into the U and stops at its back, and is never inside it.</summary>
    private static void CollisionNest(bool constructPost)
    {
        var q = new JointTestWorld();
        var w = new ConstructWorld();
        Vector3 pMin = new(-0.15f, 0, -3f), pMax = new(0.15f, 2f, -2.7f);
        if (constructPost)
        {
            var bd = ConstructParts.Need("Frame Block");
            var post = w.Create(new Vector3(0, -bd.BoundsMin.Y + 0.02f, -2.85f), Quaternion.Identity)!;
            w.AddPart(post, bd, Vector3.Zero, 0);
            post.Pinned = true;
            post.Rebuild();
            var half = (bd.BoundsMax - bd.BoundsMin) * 0.5f;
            pMin = post.Pos - half;
            pMax = post.Pos + half;
        }
        else
            q.Walls.Add((pMin, pMax));
        var b = Block;
        var s = b.BoundsMax.X - b.BoundsMin.X;
        var parts = new List<(ConstructPartDef, Vector3)>();
        for (var i = -3; i <= 3; i++)
            parts.Add((b, new Vector3(i * s, 0, 0))); // the back
        for (var k = 1; k <= 4; k++)
        {
            parts.Add((b, new Vector3(-3 * s, 0, -k * s))); // the arms, reaching towards -Z
            parts.Add((b, new Vector3(3 * s, 0, -k * s)));
        }
        var u = CollBody(w, new Vector3(0, -b.BoundsMin.Y + 0.01f, 0), parts.ToArray());
        u.Vel = new Vector3(0, 0, -9f);
        var deep = 0f;
        var far = 0f;
        for (var t = 0; t < 180; t++)
        {
            w.Step(q, World.TickDt);
            deep = MathF.Max(deep, CollDepth(u, pMin, pMax));
            far = MathF.Min(far, u.Pos.Z);
            u.Wake();
        }
        var backFace = u.Pos.Z - s * 0.5f;
        Line($"compound collision: a U of {u.PartCount} blocks round a {(constructPost ? "construct" : "map")} post: {-far:0.00} m in, back {backFace - pMax.Z:0.000} m from the post, deepest {deep * 100:0.0} cm");
        var word = constructPost ? "a construct post" : "a post";
        if (deep > 0.04f)
            Fail($"compound collision: {word} went {deep * 100:0.0} cm into the U");
        if (backFace - pMax.Z > 0.05f || backFace < pMax.Z - 0.04f)
            Fail($"compound collision: the U's back should stop at {word}: {backFace - pMax.Z:0.000} m off");
    }

    /// <summary>A thin plate 2 m wide dropped on a knife edge far from any of its corners rests on it.</summary>
    private static void CollisionEdge()
    {
        var q = new JointTestWorld();
        Vector3 eMin = new(-0.03f, 0, -3), eMax = new(0.03f, 1f, 3);
        q.Walls.Add((eMin, eMax));
        var w = new ConstructWorld();
        var plate = ConstructParts.Need("Deck Plate");
        var c = CollBody(w, new Vector3(0, 1.4f, 0), (plate, Vector3.Zero));
        var deep = 0f;
        for (var t = 0; t < 40; t++)
        {
            w.Step(q, World.TickDt);
            deep = MathF.Max(deep, CollDepth(c, eMin, eMax));
        }
        var bottom = c.WorldCom.Y - (plate.BoundsMax.Y - plate.BoundsMin.Y) * 0.5f;
        Line($"compound collision: a plate on a knife edge: bottom {bottom:0.000} m over the floor (edge at 1), deepest {deep * 100:0.0} cm");
        if (deep > 0.04f || bottom < 0.95f)
            Fail($"compound collision: the plate sank onto the edge (bottom {bottom:0.00}, {deep * 100:0.0} cm in)");
    }

    /// <summary>A beam slides through a gap 5 cm wider each side than it; one that doesn't fit stops at a pillar.</summary>
    private static void CollisionGap()
    {
        foreach (var fits in new[] { true, false })
        {
            var q = new JointTestWorld();
            var w = new ConstructWorld();
            var frame = w.Create(new Vector3(0, 0, 0), Quaternion.Identity)!;
            var b = Block;
            var beam = ConstructParts.Need("Frame Beam");
            var half = beam.BoundsMax.X;
            var gap = half + 0.05f + b.BoundsMax.X;
            for (var k = 0; k < 3; k++)
            {
                w.AddPart(frame, b, new Vector3(-gap, -b.BoundsMin.Y + 0.02f + k * 0.5f, 0), 0);
                w.AddPart(frame, b, new Vector3(gap, -b.BoundsMin.Y + 0.02f + k * 0.5f, 0), 0);
            }
            frame.Pinned = true;
            frame.Rebuild();
            var c = CollBody(w, new Vector3(fits ? 0 : 0.2f, -beam.BoundsMin.Y + 0.01f, -2.5f), (beam, Vector3.Zero));
            c.Vel = new Vector3(0, 0, fits ? 14f : 8f);
            for (var t = 0; t < 120; t++)
            {
                w.Step(q, World.TickDt);
                c.Wake();
            }
            Line($"compound collision: a beam at a gap ({(fits ? "fits" : "off line")}): reached z {c.Pos.Z:0.00}");
            if (fits && c.Pos.Z < 0.5f)
                Fail($"compound collision: the beam should go through the gap, stopped at z {c.Pos.Z:0.00}");
            if (!fits && c.Pos.Z > -0.9f)
                Fail($"compound collision: the beam should stop at the pillar, reached z {c.Pos.Z:0.00}");
        }
    }

    /// <summary>An upside-down U dropped over a bar rests on it, one leg either side; an L's low arm goes under a bar and its upright catches.</summary>
    private static void CollisionHook()
    {
        var b = Block;
        var s = b.BoundsMax.X - b.BoundsMin.X;
        {
            var q = new JointTestWorld();
            Vector3 mn = new(-0.1f, 2f, -3), mx = new(0.1f, 2.2f, 3);
            q.Walls.Add((mn, mx));
            var w = new ConstructWorld();
            var parts = new List<(ConstructPartDef, Vector3)>();
            for (var i = -2; i <= 2; i++)
                parts.Add((b, new Vector3(i * s, 0, 0)));
            for (var k = 1; k <= 2; k++)
            {
                parts.Add((b, new Vector3(-2 * s, -k * s, 0)));
                parts.Add((b, new Vector3(2 * s, -k * s, 0)));
            }
            var c = CollBody(w, new Vector3(0, 2.9f, 0), parts.ToArray());
            var deep = 0f;
            for (var t = 0; t < 180; t++)
            {
                w.Step(q, World.TickDt);
                deep = MathF.Max(deep, CollDepth(c, mn, mx));
            }
            Line($"compound collision: a hook over a bar: {c.Pos.Y:0.00} m up (the bar's top 2.2), deepest {deep * 100:0.0} cm");
            if (c.Pos.Y < 2.3f || deep > 0.04f)
                Fail($"compound collision: the hook should hang on the bar: {c.Pos.Y:0.00} m up, {deep * 100:0.0} cm in");
        }
        {
            var q = new JointTestWorld();
            Vector3 mn = new(-3, 0.6f, -1.6f), mx = new(3, 0.8f, -1.4f);
            q.Walls.Add((mn, mx));
            var w = new ConstructWorld();
            var parts = new List<(ConstructPartDef, Vector3)>();
            for (var k = 0; k <= 3; k++)
                parts.Add((b, new Vector3(0, 0, -k * s))); // the low arm, 0.5 m high
            for (var k = 1; k <= 2; k++)
                parts.Add((b, new Vector3(0, k * s, 0))); // the upright at its back
            var c = CollBody(w, new Vector3(0, -b.BoundsMin.Y + 0.01f, 0), parts.ToArray());
            c.Vel = new Vector3(0, 0, -8f);
            var deep = 0f;
            for (var t = 0; t < 120; t++)
            {
                w.Step(q, World.TickDt);
                deep = MathF.Max(deep, CollDepth(c, mn, mx));
                c.Wake();
            }
            Line($"compound collision: an L at a bar: stopped at z {c.Pos.Z:0.00}, deepest {deep * 100:0.0} cm");
            if (c.Pos.Z > -0.6f || c.Pos.Z < -1.3f || deep > 0.04f)
                Fail($"compound collision: the L's arm goes under the bar and the upright catches: z {c.Pos.Z:0.00}, {deep * 100:0.0} cm in");
        }
    }

    /// <summary>A thin plate at the speed cap, flat on and edge on, doesn't pass a 5 cm wall (a map box, then an anchored construct's panel).</summary>
    private static void CollisionFastThin()
    {
        var plate = ConstructParts.Need("Panel");
        foreach (var edgeOn in new[] { false, true })
            foreach (var construct in new[] { false, true })
            {
                var q = new JointTestWorld();
                var w = new ConstructWorld();
                float wallAt = 12;
                if (construct)
                {
                    var wall = w.Create(new Vector3(wallAt, 3f, 0), Quaternion.Identity)!;
                    for (var i = -2; i <= 2; i++)
                        for (var k = -2; k <= 2; k++)
                            w.AddPart(wall, plate, new Vector3(0, i * 1f, k * 1f), Rot24.Nearest(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), out _));
                    wall.Pinned = true;
                    wall.Rebuild();
                }
                else
                    q.Walls.Add((new Vector3(wallAt, 0, -4), new Vector3(wallAt + 0.05f, 6, 4)));
                var rot = edgeOn ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
                var c = w.Create(new Vector3(0, 3f, 0.2f), rot)!;
                w.AddPart(c, plate, Vector3.Zero, 0);
                c.Rebuild();
                c.Vel = new Vector3(ConstructPhysics.MaxSpeed - 1, 0, 0);
                var most = 0f;
                for (var t = 0; t < 45; t++)
                {
                    w.Step(q, World.TickDt);
                    most = MathF.Max(most, c.WorldCom.X);
                    c.Wake();
                }
                Line($"compound collision: a fast plate ({(edgeOn ? "edge" : "flat")} on) at a {(construct ? "construct" : "map")} wall at {wallAt}: furthest {most:0.00}");
                if (most > wallAt + 0.05f)
                    Fail($"compound collision: a plate at {ConstructPhysics.MaxSpeed - 1} m/s went through a thin wall ({(edgeOn ? "edge" : "flat")} on, {(construct ? "construct" : "map")}): x {most:0.00}");
            }
    }

    /// <summary>Holds a piece with the physics gun's spring and walks the aim along a line.</summary>
    private static void CollHold(Construct c, Vector3 at, float mass = 0)
    {
        c.GrabbedBy = 0;
        c.GrabLocal = c.Com;
        c.GrabTarget = at;
        c.GrabRot = Quaternion.Identity;
        c.GrabAccel = ConstructGrab.AccelFor(mass > 0 ? mass : c.Mass, true);
    }

    /// <summary>A held piece goes into a slot 3 cm wider each side than it (and between pieces it doesn't touch), and stops at one too narrow.</summary>
    private static void CollisionHeldSlot()
    {
        var b = Block;
        var s = b.BoundsMax.X - b.BoundsMin.X;
        foreach (var clear in new[] { 0.03f, -0.03f })
        {
            var q = new JointTestWorld();
            var w = new ConstructWorld();
            var jaws = w.Create(new Vector3(0, 2f, 0), Quaternion.Identity)!;
            var off = s + clear;
            for (var k = 0; k < 3; k++)
            {
                w.AddPart(jaws, b, new Vector3(-off, 0, k * s), 0);
                w.AddPart(jaws, b, new Vector3(off, 0, k * s), 0);
            }
            jaws.Pinned = true;
            jaws.Rebuild();
            var c = CollBody(w, new Vector3(0, 2f, -1.5f), (b, Vector3.Zero));
            for (var t = 0; t < 150; t++)
            {
                var along = -1.5f + MathF.Min(t, 90) * (1.5f + s) / 90f;
                CollHold(c, new Vector3(0, 2f, along));
                w.Step(q, World.TickDt);
            }
            Line($"compound collision: a held block at a slot ({clear * 100:0} cm clear a side): reached z {c.Pos.Z:0.00} (aim {s:0.00})");
            if (clear > 0 && c.Pos.Z < s - 0.05f)
                Fail($"compound collision: a held block should go into a slot it fits: z {c.Pos.Z:0.00}");
            if (clear < 0 && c.Pos.Z > -s * 0.5f - s * 0.5f + 0.03f)
                Fail($"compound collision: a held block should stop at a slot too narrow: z {c.Pos.Z:0.00}");
        }
    }

    /// <summary>A held piece dragged along a wall it is pressed against stays smooth.</summary>
    private static void CollisionHeldSmooth()
    {
        var q = new JointTestWorld();
        var w = new ConstructWorld();
        var wall = w.Create(new Vector3(0, 2f, 0), Quaternion.Identity)!;
        var b = Block;
        var s = b.BoundsMax.X - b.BoundsMin.X;
        for (var i = -8; i <= 8; i++)
            for (var k = 0; k < 3; k++)
                w.AddPart(wall, b, new Vector3(i * s, (k - 1) * s, 0), 0); // (a wall of 51 separate boxes: seams every half metre)
        wall.Pinned = true;
        wall.Rebuild();
        var c = CollBody(w, new Vector3(-3f, 2f, -s - 0.05f), (b, Vector3.Zero));
        var prev = c.Pos;
        var vel = Vector3.Zero;
        var worst = 0f;
        for (var t = 0; t < 200; t++)
        {
            CollHold(c, new Vector3(-3f + t * 0.03f, 2f, -s + 0.1f)); // (pressed 10 cm into the wall)
            w.Step(q, World.TickDt);
            var v = (c.Pos - prev) / World.TickDt;
            if (t > 30)
                worst = MathF.Max(worst, (v - vel).Length() / World.TickDt);
            vel = v;
            prev = c.Pos;
        }
        Line($"compound collision: a held block dragged along a seamed wall: reached x {c.Pos.X:0.00}, z {c.Pos.Z:0.00}, worst acceleration {worst:0} m/s2");
        if (c.Pos.X < 2.5f || c.Pos.Z > -s + 0.03f)
            Fail($"compound collision: the held block should slide along the wall: x {c.Pos.X:0.00}, z {c.Pos.Z:0.00}");
        if (worst > 300)
            Fail($"compound collision: a held block jerks on a wall's seams: {worst:0} m/s2");
    }

    /// <summary>A wedge collides as a slope: a block driven at it rides up, rays hit the slope, not the steps under it, and a player's steps stay within a step of it.</summary>
    private static void CollisionWedge()
    {
        var wedge = ConstructParts.Need("Wedge 1:1 1 m");
        var b = Block;
        var q = new JointTestWorld();
        var w = new ConstructWorld();
        var ramp = w.Create(new Vector3(0, 0.125f, 0), Quaternion.Identity)!;
        for (var i = -1; i <= 1; i++)
            w.AddPart(ramp, wedge, new Vector3(i * 1f, 0, 0), 0);
        ramp.Pinned = true;
        ramp.Rebuild();
        // Rays: straight down onto the slope in the middle of the middle wedge (the slope rises with z), and one just over it.
        const float Slope = 1f;
        var x = 0.375f;
        foreach (var z in new[] { 0.1f, 0.33f, 0.6f })
        {
            var rise = z + 0.125f;
            if (!w.Raycast(new Vector3(x, 3, z), -Vector3.UnitY, 5, out var hit) || MathF.Abs(hit.Point.Y - rise * Slope) > 0.01f)
                Fail($"compound collision: a ray down onto the wedge at z {z} should hit at y {rise:0.00}: {(hit.Body != null ? $"{hit.Point.Y:0.00}" : "a miss")}");
            else if (hit.Normal.Y < 0.6f || hit.Normal.Y > 0.8f)
                Fail($"compound collision: a ray on the wedge's slope has normal {hit.Normal}");
        }
        if (w.Raycast(new Vector3(x, 0.7f, -1), Vector3.UnitZ, 1.5f, out var over))
            Fail($"compound collision: a ray over the slope should miss, hit at z {over.Point.Z:0.00}");
        // A player's boxes (the hidden steps): within a step of the slope.
        var worst = 0f;
        for (var k = 0; k < wedge.SolidBoxes; k++)
        {
            var bx = wedge.Boxes[k];
            var z = bx.Center.Z;
            var slope = z + 0.125f - 0.125f;
            worst = MathF.Max(worst, MathF.Abs(bx.Center.Y + bx.Half.Y - slope));
        }
        Line($"compound collision: the wedge's walking steps stay within {worst * 100:0.0} cm of its slope");
        if (worst > World.StepHeight)
            Fail($"compound collision: a wedge's walking steps are {worst:0.00} m off its slope");
        var c = CollBody(w, new Vector3(0, -b.BoundsMin.Y + 0.01f, -1.2f), (b, Vector3.Zero));
        c.Vel = new Vector3(0, 0, 14f);
        var y0 = c.Pos.Y;
        var top = y0;
        for (var t = 0; t < 70; t++)
        {
            w.Step(q, World.TickDt);
            top = MathF.Max(top, c.Pos.Y);
        }
        Line($"compound collision: a block driven up a wedge rose {top - y0:0.00} m");
        if (top - y0 < 0.5f)
            Fail($"compound collision: a wedge should lift a body driven at it: it rose {top - y0:0.00} m");
    }

    private static PhysicsWorld CollGround(Func<float, float, float> height, out TerrainData data)
    {
        const int Count = 61;
        var h = new float[Count * Count];
        for (var j = 0; j < Count; j++)
            for (var i = 0; i < Count; i++)
                h[j * Count + i] = height(-30 + i, -30 + j);
        data = new TerrainData(Count, Count, 1f, -30, -30, h, new byte[h.Length], new TerrainSurface());
        return new PhysicsWorld(new List<Box>(), new HeightfieldCollision(data), 29, 29);
    }

    /// <summary>The ground: a plate across a ridge rests on it; a block in a crater, on a filled mound, under a dug roof.</summary>
    private static void CollisionGround()
    {
        var plate = ConstructParts.Need("Deck Plate");
        var b = Block;
        {
            var q = CollGround((x, z) => MathF.Max(0, 2 - MathF.Abs(x) * 0.25f), out _); // (gentle: the plate stays where it lands)
            var w = new ConstructWorld();
            var c = CollBody(w, new Vector3(0, 2.6f, 0), (plate, Vector3.Zero));
            var low = float.MaxValue;
            for (var t = 0; t < 60; t++)
            {
                w.Step(q, World.TickDt);
                low = MathF.Min(low, c.WorldCom.Y);
            }
            Line($"compound collision: a plate across a ridge at 2 m: lowest {low:0.000}");
            if (low < 1.97f)
                Fail($"compound collision: a plate sank through a ridge: {low:0.00}");
        }
        {
            var q = CollGround((x, z) => 0, out var data);
            data.Edits.Apply(new DigOp(DigKind.Crater, new Vector3(5, 0, 5), 3f).Quantised);
            var floor = data.GroundBelow(5, 5, 1);
            var w = new ConstructWorld();
            var c = CollBody(w, new Vector3(5, floor + 1.5f, 5), (b, Vector3.Zero));
            for (var t = 0; t < 120; t++)
                w.Step(q, World.TickDt);
            var bottom = c.WorldCom.Y - b.BoundsMax.Y;
            Line($"compound collision: a block in a crater with its floor at {floor:0.00}: bottom at {bottom:0.00}");
            if (bottom > -0.1f || bottom < floor - 0.1f)
                Fail($"compound collision: a block should rest in a crater: bottom {bottom:0.00}, floor {floor:0.00}");
            data.Edits.Apply(new DigOp(DigKind.FillDirt, new Vector3(-8, 0, -8), 2.5f).Quantised);
            var top = data.GroundBelow(-8, -8, 8);
            var d = CollBody(w, new Vector3(-8, top + 1.5f, -8), (b, Vector3.Zero));
            for (var t = 0; t < 120; t++)
                w.Step(q, World.TickDt);
            bottom = d.WorldCom.Y - b.BoundsMax.Y;
            Line($"compound collision: a block on filled ground with its top at {top:0.00}: bottom at {bottom:0.00}");
            if (MathF.Abs(bottom - top) > 0.6f)
                Fail($"compound collision: a block should rest on filled ground: bottom {bottom:0.00}, top {top:0.00}");
            // A closed hollow: a block thrown up inside stops at its roof.
            var at = new Vector3(10, -6, -10);
            data.Edits.Apply(new DigOp(DigKind.Scoop, at, 2.4f).Quantised);
            var roof = data.CeilingAbove(at.X, at.Z, at.Y);
            var e = CollBody(w, at - new Vector3(0, 0.6f, 0), (b, Vector3.Zero));
            e.Vel = new Vector3(0, 14, 0);
            var high = float.MinValue;
            for (var t = 0; t < 40; t++)
            {
                w.Step(q, World.TickDt);
                high = MathF.Max(high, e.WorldCom.Y + b.BoundsMax.Y);
            }
            Line($"compound collision: a block thrown up in a hollow with its roof at {roof:0.00}: top reached {high:0.00}");
            if (high > roof + 0.06f)
                Fail($"compound collision: a block went through the roof of a hollow: {high:0.00} past {roof:0.00}");
        }
    }

    /// <summary>Streamed ground: a plate lands on it, wherever its tiles are made (it never looks at the whole grid).</summary>
    private static void CollisionStreamed()
    {
        var map = MapFile.Load(StreamLabJson(true), "Dev/StreamLab.json");
        map.Validate();
        var q = new PhysicsWorld(map);
        var w = new ConstructWorld();
        var plate = ConstructParts.Need("Deck Plate");
        var ground = map.TerrainCollision!.HeightAt(5, 5);
        var c = CollBody(w, new Vector3(5, ground + 1.5f, 5), (plate, Vector3.Zero));
        var low = float.MaxValue;
        for (var t = 0; t < 120; t++)
        {
            w.Step(q, World.TickDt);
            low = MathF.Min(low, c.WorldCom.Y);
        }
        Line($"compound collision: a plate on streamed ground at {ground:0.00}: lowest {low:0.00}");
        if (low < map.TerrainCollision.HeightAt(c.WorldCom.X, c.WorldCom.Z) - 0.2f || c.Asleep == false && c.Vel.Length() > 1)
            Fail($"compound collision: a plate should rest on streamed ground: lowest {low:0.00}, ground {ground:0.00}");
    }

    /// <summary>Shaped pieces on their own: a wedge falls on its flat side, a rod rests on its round side, a wedge meets a wedge.</summary>
    private static void CollisionShapes()
    {
        var q = new JointTestWorld();
        var w = new ConstructWorld();
        var wedge = ConstructParts.Need("Wedge 1:2 0.5x1 m");
        var a = CollBody(w, new Vector3(0, 1f, 0), (wedge, Vector3.Zero));
        var b = CollBody(w, new Vector3(0, 2.2f, 0), (wedge, Vector3.Zero)); // (slope on slope: gentle enough to stay)
        var rod = ConstructParts.All.First(d => d.Name.StartsWith("Steel Rod 1", StringComparison.Ordinal));
        var r = CollBody(w, new Vector3(4, 1f, 0), (rod, Vector3.Zero));
        for (var t = 0; t < 240; t++)
            w.Step(q, World.TickDt);
        var bottom = a.Pos.Y + wedge.BoundsMin.Y;
        Line($"compound collision: a wedge's flat side {bottom:0.000} m over the floor, a wedge on it {b.Pos.Y - a.Pos.Y:0.00} m higher, a rod {r.WorldCom.Y:0.000} m up");
        if (MathF.Abs(bottom) > 0.02f)
            Fail($"compound collision: a wedge should rest on its flat side: {bottom:0.000}");
        // However it came to rest, the two real shapes aren't in each other.
        var ca = a.Collider;
        var cb = b.Collider;
        var sa = new ConstructNarrow.Shape();
        var sb = new ConstructNarrow.Shape();
        var deep = 0f;
        Span<CPoint> pts = stackalloc CPoint[8];
        sa.FromHull(ca.Hulls[0], CFrame.Of(a.Rot, a.Pos));
        sb.FromHull(cb.Hulls[0], CFrame.Of(b.Rot, b.Pos));
        var hits = ConstructNarrow.HullHull(sa, sb, 0, pts, 0);
        for (var i = 0; i < hits; i++)
            deep = MathF.Max(deep, -pts[i].Gap);
        Line($"compound collision: the two wedges end {deep * 100:0.0} cm in each other");
        if (deep > 0.03f)
            Fail($"compound collision: a wedge dropped on a wedge sank {deep * 100:0.0} cm into it");
        var round = rod.Boxes[rod.SolidBoxes].Half.X;
        if (MathF.Abs(r.WorldCom.Y - round) > 0.03f)
            Fail($"compound collision: a rod should rest on its round side at {round:0.000}: {r.WorldCom.Y:0.000}");
    }

    /// <summary>Ten blocks stacked stand for ten seconds, and fall asleep where they stand.</summary>
    private static void CollisionStack()
    {
        var q = new JointTestWorld();
        var w = new ConstructWorld();
        var b = Block;
        var s = b.BoundsMax.Y - b.BoundsMin.Y;
        var list = new List<Construct>();
        for (var k = 0; k < 10; k++)
            list.Add(CollBody(w, new Vector3(0, -b.BoundsMin.Y + k * (s + 0.002f), 0), (b, Vector3.Zero)));
        var top = list[^1];
        var start = top.Pos;
        for (var t = 0; t < 600; t++)
            w.Step(q, World.TickDt);
        var drift = new Vector2(top.Pos.X - start.X, top.Pos.Z - start.Z).Length();
        var asleep = list.Count(c => c.Asleep);
        Line($"compound collision: a stack of 10 after 10 s: top {top.Pos.Y:0.000} (was {start.Y:0.000}), drift {drift * 100:0.00} cm, {asleep} asleep");
        if (MathF.Abs(top.Pos.Y - start.Y) > 0.08f || drift > 0.06f)
            Fail($"compound collision: a stack of 10 didn't stand: top moved {top.Pos.Y - start.Y:0.000} down, {drift * 100:0.0} cm aside");
        if (asleep < 10)
            Fail($"compound collision: a stack of 10 should fall asleep: {asleep} did");
    }

    /// <summary>The same pile dropped twice ends exactly the same.</summary>
    private static void CollisionDeterministic()
    {
        static double Run()
        {
            var q = new JointTestWorld();
            var w = new ConstructWorld();
            var rng = new Random(5);
            for (var i = 0; i < 40; i++)
                ConstructBuild.PlaceNew(w, Block, new Vector3(rng.NextSingle() * 2, 1 + i * 0.4f, rng.NextSingle() * 2), rng.NextSingle() * 6);
            for (var t = 0; t < 150; t++)
                w.Step(q, World.TickDt);
            var sum = 0.0;
            foreach (var c in w.Bodies)
                sum = sum * 1.000001 + c.Pos.X + 3 * c.Pos.Y + 7 * c.Pos.Z + c.Rot.X;
            return sum;
        }
        double a = Run(), b = Run();
        if (a != b)
            Fail($"compound collision: the same pile came out different: {a} and {b}");
    }

    /// <summary>Wall and busiest-tick time and allocation of <paramref name="ticks"/> ticks of a scene, after a warm-up.</summary>
    private static void CostRun(string name, ConstructWorld w, IWorldQuery q, int warm, int ticks, Action? each = null)
    {
        for (var t = 0; t < warm; t++)
        {
            each?.Invoke();
            w.Step(q, World.TickDt);
        }
        var sw = new Stopwatch();
        double sum = 0, worst = 0;
        long bytes = 0;
        var contacts = 0;
        var sim = 0;
        for (var t = 0; t < ticks; t++)
        {
            each?.Invoke();
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            w.Step(q, World.TickDt);
            var ms = sw.Elapsed.TotalMilliseconds;
            bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            sum += ms;
            worst = Math.Max(worst, ms);
            contacts = Math.Max(contacts, ConstructPhysics.LastContacts);
            sim = Math.Max(sim, ConstructPhysics.LastSimulated);
        }
        Line($"collision cost, {name}: {sum / ticks:0.000} ms a tick (worst {worst:0.000}), up to {contacts} contacts, {sim} simulated, {w.Bodies.Count(b => !b.Asleep)} awake, {bytes} bytes in {ticks} ticks");
        if (bytes > 0)
            Fail($"compound collision: {name} allocated {bytes} bytes in {ticks} ticks");
    }

    /// <summary>A hull of <paramref name="parts"/> blocks in a flat 12-wide sheet, sliding.</summary>
    private static Construct CostHull(ConstructWorld w, Vector3 at, int parts, ConstructPartDef block)
    {
        var size = block.BoundsMax - block.BoundsMin;
        var c = w.Create(at, Quaternion.Identity)!;
        for (var i = 0; i < parts; i++)
            w.AddPart(c, block, new Vector3(i % 12 * size.X, 0, i / 12 * size.Z), 0);
        c.Rebuild();
        return c;
    }

    /// <summary>Rolling ground, half-metre cells.</summary>
    private static PhysicsWorld CostGround()
    {
        const int Count = 241;
        var h = new float[Count * Count];
        for (var j = 0; j < Count; j++)
            for (var i = 0; i < Count; i++)
                h[j * Count + i] = 0.4f * MathF.Sin(i * 0.35f) * MathF.Cos(j * 0.25f);
        return new PhysicsWorld(new List<Box>(), new HeightfieldCollision(new TerrainData(Count, Count, 0.5f, -60, -60, h, new byte[h.Length], new TerrainSurface())), 59, 59);
    }

    private static void CollisionCost()
    {
        var q = new JointTestWorld();
        var block = ConstructParts.Need("Frame Block");
        var size = block.BoundsMax - block.BoundsMin;
        // Warm the code paths once.
        var warm = new ConstructWorld();
        for (var i = 0; i < 40; i++)
            ConstructBuild.PlaceNew(warm, block, new Vector3(i % 6 * 0.3f, 1f + i / 6 * 0.3f, 0), 0);
        CostHull(warm, new Vector3(0, 3, 4), Construct.MaxParts, block).Vel = new Vector3(2, 0, 0);
        for (var t = 0; t < 120; t++)
            warm.Step(q, World.TickDt);
        // Many awake bodies: 300 blocks falling, spread out.
        var rng = new Random(7);
        var fall = new ConstructWorld();
        for (var i = 0; i < 300; i++)
            ConstructBuild.PlaceNew(fall, block, new Vector3(i % 17 * 2.5f, 30f + (float)rng.NextDouble() * 2, i / 17 * 2.5f), (float)rng.NextDouble() * 6);
        CostRun("300 falling blocks", fall, q, 8, 30);
        // A pile: 200 blocks dropped into one heap (bodies touching bodies, while it settles).
        var pile = new ConstructWorld();
        rng = new Random(11);
        for (var i = 0; i < 200; i++)
            ConstructBuild.PlaceNew(pile, block, new Vector3(i % 8 * size.X * 1.1f, 0.3f + i / 64 * size.Y * 1.2f, i / 8 % 8 * size.Z * 1.1f), (float)rng.NextDouble() * 0.3f);
        CostRun("a pile of 200", pile, q, 15, 60);
        // The biggest hull a construct holds, kept sliding on the floor (the per-construct cap is 96 parts, not 200).
        var hull = new ConstructWorld();
        var slide = CostHull(hull, new Vector3(0, -block.BoundsMin.Y + 0.02f, 0), Construct.MaxParts, block);
        CostRun($"one {Construct.MaxParts}-part hull sliding", hull, q, 20, 60, () =>
        {
            slide.Vel = new Vector3(3, slide.Vel.Y, 0);
            slide.Wake();
        });
        // The same hull and a few dozen blocks on rolling ground of half-metre cells (the worst of the ground's triangles).
        var rolling = CostGround();
        var land = new ConstructWorld();
        var track = CostHull(land, new Vector3(0, 1.6f, 0), Construct.MaxParts, block);
        for (var i = 0; i < 40; i++)
            ConstructBuild.PlaceNew(land, block, new Vector3(-20 + i % 8 * 2, 1.5f, 10 + i / 8 * 2), 0);
        CostRun($"one {Construct.MaxParts}-part hull and 40 blocks on half-metre ground", land, rolling, 30, 60, () =>
        {
            track.Vel = new Vector3(3, track.Vel.Y, 0);
            track.Wake();
        });
        // Many small awake bodies landing on that ground.
        var scatter = new ConstructWorld();
        rng = new Random(3);
        for (var i = 0; i < 300; i++)
            ConstructBuild.PlaceNew(scatter, block, new Vector3(-30 + i % 20 * 3f, 1.2f, -30 + i / 20 * 3f), (float)rng.NextDouble() * 6);
        CostRun("300 blocks landing on half-metre ground", scatter, rolling, 10, 30);
        // Eight of them stacked, the top ones kept sliding over each other.
        var many = new ConstructWorld();
        var hulls = new List<Construct>();
        for (var i = 0; i < 8; i++)
            hulls.Add(CostHull(many, new Vector3(i * 0.2f, 0.3f + i * size.Y * 1.5f, i * 0.2f), Construct.MaxParts, block));
        CostRun($"eight {Construct.MaxParts}-part hulls stacked", many, q, 30, 60, () =>
        {
            for (var i = 1; i < hulls.Count; i += 2)
            {
                hulls[i].Vel = new Vector3(1, hulls[i].Vel.Y, 0);
                hulls[i].Wake();
            }
        });
    }
}
