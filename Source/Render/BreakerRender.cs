using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What a Scrap Breaker draws across its squares (BreakerWorks lays them out: one rule for every peer; docs/sandbox/constructs/production.md
/// "The Scrap Breaker"): over each square of 2 or more one pair of toothed rotors across it, counter-turning while it shreds, their bearing
/// blocks and drive, and the belt under them with its cleats running to the front; a 1 x 1's are its Shredder Block's own "alone" boxes.
/// For every square the funnel skirt round the bed's outer edges (hazard paint on its lip, the in side's white band along the rim, KEEP
/// CLEAR on its front, a flat deck over each concave corner of the bed so its top runs on round it), the discharge from its front onto
/// the collecting belt, and the belt along the front into the Core's hatch (turning end rollers, a hazard guard strip, the out side's
/// orange band and arrows to the Core). The wreck under way (CStore.JobDef) sinks into the rotors of the
/// square it landed on, squashed as it goes. Drawn within <see cref="DrawNear"/>. Allocates nothing.
/// </summary>
public static class BreakerRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Body = new(0.373f, 0.478f, 0.204f), Dark = new(0.133f, 0.149f, 0.169f), Steel = new(0.6f, 0.63f, 0.66f),
        Yellow = new(0.95f, 0.69f, 0.12f), Rubber = new(0.106f, 0.114f, 0.122f), Rust = new(0.42f, 0.24f, 0.14f),
        White = new(0.925f, 0.922f, 0.894f), Orange = new(0.94f, 0.47f, 0.094f), Ink = new(0.082f, 0.09f, 0.102f); // (MachineLook's In, Out and Ink)
    private const float CleatEvery = 0.5f, BeltSpeed = 0.8f; // the belts' cleats (m apart) and speed (m/s)
    private const int MostCleats = 24, MostBoxes = 256;
    private static CPartBox _cylX; // (the Shredder Block's lone drum: its cylinder shape, for what is drawn here)
    private static bool _looked;
    private static float SteelSurface, DirtySurface, HazardSurface; // (Materials/Surfaces.json rows, as Renderer.Draw takes them: found once)

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var now = ConstructRender.Now;
        foreach (var c in world.Bodies)
        {
            if (!c.HasRecycler || c.MachineAsmCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (asm.Core >= c.PartCount || !BreakerWorks.Is(c.Parts[asm.Core].D) || world.StoreOf(c, asm.Core) is not { } st)
                    continue;
                var working = !c.Parts[asm.Core].Broken && ProductionLines.Working(world, c, asm.Core, st, now);
                DrawBreaker(gfx, c, asm, st, working, now);
            }
        }
    }

    private static void Look()
    {
        _looked = true;
        (SteelSurface, DirtySurface, HazardSurface) = (Surfaces.Code(Surfaces.Id("steel"), 0f), Surfaces.Code(Surfaces.Id("dirty steel"), 0.3f), Surfaces.Code(Surfaces.Id("hazard paint"), 0f));
        if (ConstructParts.Find("Shredder Block") is { } sb)
            foreach (var b in sb.Boxes)
                if (b.Shape != 0 && b.Alone && b.Work == BoxWork.Spin && b.Half.X > 0.3f)
                {
                    _cylX = b; // (the drum: a cylinder along x)
                    break;
                }
    }

    private static void DrawBreaker(Renderer gfx, Construct c, in CMachineAsm asm, CStore st, bool working, double now)
    {
        var d = c.Parts[asm.Core].D;
        ConstructRender.PartDrawn(c, asm.Core, out var pos, out var rot);
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var g = new SmelterWorks.Grid(d);
        var list = MachineBlocks.Squares(c, asm);
        if (!_looked)
            Look();
        foreach (ref readonly var q in list)
        {
            var m = BreakerWorks.Middle(g, q);
            if (q.N >= 2)
            {
                Rotors(gfx, frame, g, q.N, m, working, now);
                Belt(gfx, frame, g, q.N, m, working, now);
            }
            Skirt(gfx, frame, g, list, q);
        }
        if (list.Length > 0)
            Collector(gfx, frame, g, list, working, now);
        Wreck(gfx, frame, g, list, st, now);
    }

    // ---- A square's shredder: the rotors, their bearings and drive, the belt under them ------------------------------------------------

    private static void Rotors(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, int n, Vector2 m, bool working, double now)
    {
        float r = BreakerWorks.RotorR(n), t = BreakerWorks.Teeth(n), ay = g.Floor + BreakerWorks.AxisY(n), gap = BreakerWorks.Gap(n), len = BreakerWorks.RotorLen(n);
        var rows = BreakerWorks.TeethRows(n);
        float x0 = m.X - len * 0.5f, pitch = len / rows, floor = g.Floor + BreakerWorks.BaseTop;
        var angle = working ? (float)(now * BreakerWorks.Spin % Math.Tau) : 0f;
        for (var side = 0; side < 2; side++)
        {
            var back = side == 1;
            var z = m.Y + (back ? gap : -gap);
            var turn = back ? -angle : angle; // (they turn against each other, pulling the wreck down between them)
            Shaped(gfx, frame, _cylX, new Vector3(m.X, ay, z), new Vector3(len * 0.5f, r * 0.8f, r * 0.8f), Steel, DirtySurface, turn); // (the drum)
            var count = back ? rows - 1 : rows;
            for (var k = 0; k < count; k++)
            {
                var x = x0 + (k + (back ? 1f : 0.5f)) * pitch; // (the back rotor's rows between the front one's: the teeth mesh)
                var colour = (k & 1) == 0 ? Dark : Yellow;
                var phase = turn + k * 0.6f;
                for (var bar = 0; bar < 2; bar++)
                    gfx.Draw(Matrix4x4.CreateScale(0.07f + 0.01f * n, 2f * t, 0.12f + 0.02f * n) * Matrix4x4.CreateRotationX(phase + bar * MathF.PI * 0.5f) * Matrix4x4.CreateTranslation(x, ay, z) * frame, colour);
            }
            // Its bearing blocks either end, on stands down to the pit floor.
            foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var bx = m.X + sx * (len * 0.5f + 0.13f);
                var bh = 0.14f + 0.03f * n;
                Box(gfx, frame, new Vector3(bx - 0.11f, ay - bh, z - bh), new Vector3(bx + 0.11f, ay + bh, z + bh), Dark);
                Box(gfx, frame, new Vector3(bx - 0.04f, ay - 0.03f, z - 0.03f), new Vector3(bx + 0.04f, ay + 0.03f, z + bh + 0.02f), Steel, surface: SteelSurface); // (its grease nipple's line)
                Box(gfx, frame, new Vector3(bx - 0.1f, floor + 0.004f, z - 0.09f), new Vector3(bx + 0.1f, ay - bh, z + 0.09f), Dark);
            }
        }
        // The drive at the +x end: a gearbox over both shafts' ends against the square's +x wall, its motor on the rim's back corner and a
        // yellow guard down to the gearbox.
        float h = n * g.Half, gx = m.X + len * 0.5f + 0.26f, top = g.Floor + BreakerWorks.Rim;
        Box(gfx, frame, new Vector3(gx, ay - 0.25f - 0.03f * n, m.Y - gap - 0.24f), new Vector3(gx + 0.12f, ay + 0.25f + 0.03f * n, m.Y + gap + 0.24f), Yellow);
        var mz = m.Y + h - 0.55f - 0.05f * n;
        var mr = 0.16f + 0.03f * n;
        Shaped(gfx, frame, _cylX, new Vector3(m.X + h - 0.75f - 0.1f * n, top + 0.02f + mr, mz), new Vector3(0.3f + 0.06f * n, mr, mr), Dark); // (the motor)
        Box(gfx, frame, new Vector3(gx - 0.02f, ay + 0.1f, mz - 0.12f), new Vector3(gx + 0.1f, top - 0.1f, mz + 0.12f), Yellow); // (its guard)
        Box(gfx, frame, new Vector3(m.X + h - 1.0f - 0.16f * n, top + 0.02f, mz - mr - 0.06f), new Vector3(m.X + h - 0.5f - 0.04f * n, top + 0.05f, mz + mr + 0.06f), Dark); // (its base frame on the rim)
    }

    /// <summary>The belt under a square's rotors, along z to its front face, its cleats running out while it shreds; its end rollers.</summary>
    private static void Belt(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, int n, Vector2 m, bool working, double now)
    {
        float h = n * g.Half, half = 0.3f + 0.25f * n, y0 = g.Floor + BreakerWorks.BeltY0, y1 = g.Floor + BreakerWorks.BeltY1;
        float z0 = m.Y - h + 0.06f, z1 = m.Y + h - 0.08f;
        Box(gfx, frame, new Vector3(m.X - half, y0, z0), new Vector3(m.X + half, y1, z1), Rubber);
        foreach (var z in (ReadOnlySpan<float>)[z0 + 0.05f, z1 - 0.05f])
            Box(gfx, frame, new Vector3(m.X - half - 0.03f, y0 - 0.002f, z - 0.04f), new Vector3(m.X + half + 0.03f, y1 + 0.004f, z + 0.04f), Steel, surface: SteelSurface); // (a roller's ends)
        foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
            Box(gfx, frame, new Vector3(m.X + sx * (half + 0.04f) - 0.02f, y0, z0), new Vector3(m.X + sx * (half + 0.04f) + 0.02f, y1 + 0.12f, z1), Dark); // (its skirt boards)
        var cleats = Math.Min(MostCleats, (int)((z1 - z0) / CleatEvery));
        var shift = working ? (float)(now * BeltSpeed % CleatEvery) : 0f;
        for (var k = 0; k < cleats; k++)
        {
            var z = z1 - 0.1f - shift - k * CleatEvery; // (running to the front, -z)
            if (z < z0 + 0.1f)
                break;
            Box(gfx, frame, new Vector3(m.X - half + 0.02f, y1, z - 0.015f), new Vector3(m.X + half - 0.02f, y1 + 0.035f, z + 0.015f), Dark);
        }
    }

    // ---- The funnel skirt round the bed's outer edges -----------------------------------------------------------------------------

    /// <summary>
    /// A square's share of the funnel skirt: along each of its outer cell edges (no square and not the Core beyond), a plate from the rim
    /// flaring out <see cref="BreakerWorks.SkirtOut"/> and up <see cref="BreakerWorks.SkirtUp"/>, hazard paint on its lip; where its run
    /// ends at a corner of the bed it reaches on past it, so neighbouring runs meet.
    /// </summary>
    private static void Skirt(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, in CMachineSquare q)
    {
        float y = g.Floor + BreakerWorks.Rim + 0.004f;
        for (var side = 0; side < 4; side++)
        {
            // (side 0: -z, 1: +z, 2: -x, 3: +x; along: the edge's own axis)
            int dx = side == 2 ? -1 : side == 3 ? 1 : 0, dz = side == 0 ? -1 : side == 1 ? 1 : 0;
            for (var k = 0; k < q.N; k++)
            {
                int cx = dx != 0 ? (dx > 0 ? q.X0 + q.N - 1 : q.X0) : q.X0 + k, cz = dz != 0 ? (dz > 0 ? q.Z0 + q.N - 1 : q.Z0) : q.Z0 + k;
                if (Bed(list, cx + dx, cz + dz))
                    continue;
                int ax = dz != 0 ? 1 : 0, az = dx != 0 ? 1 : 0; // (along the edge)
                bool lowOn = !Bed(list, cx - ax, cz - az), highOn = !Bed(list, cx + ax, cz + az);
                var lo = -g.Half - (lowOn ? BreakerWorks.SkirtOut : 0f); // (the run's end at a corner of the bed: on past it)
                var hi = g.Half + (highOn ? BreakerWorks.SkirtOut : 0f);
                var mid = new Vector3(g.X(cx), y, g.Z(cz));
                var outward = new Vector3(dx, 0, dz);
                var along = new Vector3(ax, 0, az);
                var a = mid + outward * (g.Half + 0.03f);
                var b = a + outward * BreakerWorks.SkirtOut + new Vector3(0, BreakerWorks.SkirtUp, 0);
                var mid2 = along * ((lo + hi) * 0.5f);
                Plate(gfx, frame, a + mid2, b + mid2, along, hi - lo, 0.03f, Body * 0.9f);
                // Its lip in hazard paint, reaching on past a corner of the bed only (where a neighbour's run goes on straight, the two lips
                // meet end to end: no overlap in one plane), and the in side's white band along the rim, on the skirt's upper face.
                float lipLo = lo - (lowOn ? 0.02f : 0f), lipHi = hi + (highOn ? 0.02f : 0f);
                var lipMid = along * ((lipLo + lipHi) * 0.5f);
                Plate(gfx, frame, b + lipMid - outward * 0.02f, b + lipMid + outward * 0.07f + new Vector3(0, 0.03f, 0), along, lipHi - lipLo, 0.06f, Yellow, HazardSurface); // (its lip)
                var up = Vector3.Normalize(Vector3.Cross(along, b - a));
                if (up.Y < 0)
                    up = -up;
                Plate(gfx, frame, a + mid2 + up * 0.02f, Vector3.Lerp(a, b, 0.14f) + mid2 + up * 0.02f, along, hi - lo - 0.01f, 0.01f, White); // (the in band)
                if (dz < 0 && k == q.N / 2)
                {
                    // KEEP CLEAR on the front run's outer face, once a square: a yellow plate with its black word bar.
                    var face = Vector3.Lerp(a, b, 0.45f) - up * 0.02f;
                    Plate(gfx, frame, face - (b - a) * 0.12f, face + (b - a) * 0.12f, along, 0.9f, 0.012f, Yellow);
                    Plate(gfx, frame, face - up * 0.008f - (b - a) * 0.04f, face - up * 0.008f + (b - a) * 0.04f, along, 0.7f, 0.008f, Ink);
                }
                // A concave corner of the bed (the run ends against a bigger square's side, which flares across it): a flat deck at the lip's
                // height over the corner, so the skirt's top runs on round it instead of dipping into a V. Drawn from the front and back runs only.
                if (dz != 0)
                    foreach (var sgn in (ReadOnlySpan<int>)[-1, 1])
                        if (Bed(list, cx + sgn * ax, cz + sgn * az) && Bed(list, cx + sgn * ax + dx, cz + sgn * az + dz))
                        {
                            var corner = mid + outward * (g.Half + 0.03f) + along * (sgn * (g.Half - 0.03f));
                            var span = BreakerWorks.SkirtOut + 0.07f;
                            var lo3 = corner - along * (sgn * span) + new Vector3(0, BreakerWorks.SkirtUp - 0.035f, 0);
                            var hi3 = corner + outward * span + new Vector3(0, BreakerWorks.SkirtUp - 0.007f, 0);
                            Box(gfx, frame, lo3, hi3, Body * 0.9f);
                        }
                foreach (var s in (ReadOnlySpan<float>)[-0.6f, 0.6f])
                    Plate(gfx, frame, a + along * s - outward * 0.01f + new Vector3(0, -0.25f, 0), Vector3.Lerp(a, b, 0.7f) + along * s + new Vector3(0, -0.04f, 0), along, 0.06f, 0.05f, Dark); // (a stay under it)
            }
        }
    }

    /// <summary>Whether cell (x, z) of the Core's frame is the bed (a square) or the Core: the skirt doesn't run there.</summary>
    private static bool Bed(ReadOnlySpan<CMachineSquare> list, int x, int z) => x == 0 && z == 0 || BreakerWorks.SquareAt(list, x, z) >= 0;

    // ---- The collecting belt along the front, each square's discharge onto it ------------------------------------------------------

    private static void Collector(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, bool working, double now)
    {
        var line = BreakerWorks.CollectorZ(g, list);
        float y0 = g.Floor + BreakerWorks.CollectorY0, y1 = g.Floor + BreakerWorks.CollectorY1, half = BreakerWorks.CollectorHalf;
        var far = float.MinValue;
        foreach (ref readonly var q in list)
        {
            if (!FoundryWorks.Open(list, q))
                continue; // (its belt runs on under the square in front: a covered way)
            var n = q.N;
            var m = BreakerWorks.Middle(g, q);
            var wide = 0.3f + 0.25f * Math.Min((int)n, 2);
            far = MathF.Max(far, m.X + wide + 0.1f);
            // The discharge: a dark mouth in its front wall, a steel apron sloping down onto the collecting belt with side cheeks.
            var face = m.Y - n * g.Half;
            Box(gfx, frame, new Vector3(m.X - wide, g.Floor + BreakerWorks.BaseTop + 0.004f, face - 0.03f), new Vector3(m.X + wide, g.Floor + 0.55f, face - 0.012f), Rubber);
            var a = new Vector3(m.X, g.Floor + BreakerWorks.BeltY1 + 0.01f, face - 0.03f);
            var b = new Vector3(m.X, y1 + 0.06f, line);
            Plate(gfx, frame, a, b, Vector3.UnitX, 2f * wide, 0.02f, Steel, SteelSurface);
            foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
                Plate(gfx, frame, a + new Vector3(sx * wide, 0.08f, 0), b + new Vector3(sx * wide, 0.08f, 0), Vector3.UnitX, 0.03f, 0.16f, Dark);
        }
        if (far == float.MinValue)
            return;
        var start = BreakerWorks.CoreInletX - half;
        // The belt's bed, its rubber, trestles a cell along it, hazard paint on their feet; an end stop past the far square.
        Box(gfx, frame, new Vector3(start, y0, line - half), new Vector3(far, y1 - 0.03f, line + half), Dark);
        Box(gfx, frame, new Vector3(start, y1 - 0.034f, line - half + 0.03f), new Vector3(far, y1, line + half - 0.03f), Rubber);
        for (var x = start + 0.3f; x < far; x += g.Cell)
            Box(gfx, frame, new Vector3(x - 0.06f, g.Floor + 0.002f, line - half - 0.05f), new Vector3(x + 0.06f, y0, line + half + 0.05f), Yellow, surface: HazardSurface);
        Box(gfx, frame, new Vector3(far + 0.06f, g.Floor, line - half - 0.03f), new Vector3(far + 0.14f, y1 + 0.25f, line + half + 0.03f), Yellow, surface: HazardSurface);
        // So it reads as a belt running one way: steel end rollers that turn while it works, a hazard guard strip along its open (front)
        // edge, and the out side's orange band with arrows to the Core along its front face.
        var roll = working ? -(float)(now * BeltSpeed / 0.07f % Math.Tau) : 0f;
        foreach (var rx in (ReadOnlySpan<float>)[start + 0.08f, far - 0.02f])
            Shaped(gfx, frame, _cylX, new Vector3(rx, y1 - 0.035f, line), new Vector3(half - 0.01f, 0.07f, 0.07f), Steel, SteelSurface, roll, alongZ: true);
        Box(gfx, frame, new Vector3(start + 0.004f, y1 - 0.02f, line - half - 0.035f), new Vector3(far - 0.004f, y1 + 0.045f, line - half + 0.012f), Yellow, surface: HazardSurface); // (the guard strip)
        var fz = line - half;
        Box(gfx, frame, new Vector3(start + 0.006f, y0 + 0.04f, fz - 0.006f), new Vector3(far - 0.006f, y0 + 0.07f, fz - 0.002f), Orange); // (the out band)
        for (var x = start + 0.5f; x < far - 0.3f; x += 1f)
            foreach (var sy in (ReadOnlySpan<float>)[-1f, 1f])
                gfx.Draw(Matrix4x4.CreateScale(0.09f, 0.02f, 0.006f) * Matrix4x4.CreateRotationZ(sy * 0.6f) * Matrix4x4.CreateTranslation(x + 0.037f, y0 + 0.055f + sy * 0.025f, fz - 0.01f) * frame, Orange); // (an arrow's two strokes: a "<" to the Core, -x)
        // The spur up to the Core's belt hatch, over the gap to its front.
        var front = g.OZ - g.Half - 0.012f;
        if (line + half < front - 0.01f)
            Box(gfx, frame, new Vector3(start, y0, line + half - 0.004f), new Vector3(start + 2f * half, y1 - 0.03f, front - 0.004f), Dark);
        if (!working)
            return;
        var cleats = Math.Min(MostCleats, (int)((far - start) / CleatEvery));
        var shift = (float)(now * BeltSpeed % CleatEvery);
        for (var k = 0; k < cleats; k++)
        {
            var x = far - 0.15f - shift - k * CleatEvery; // (running to the Core, -x)
            if (x < start + 0.1f)
                break;
            Box(gfx, frame, new Vector3(x - 0.015f, y1, line - half + 0.05f), new Vector3(x + 0.015f, y1 + 0.03f, line + half - 0.05f), Steel, surface: SteelSurface); // (light on the black rubber: the run shows)
        }
    }

    // ---- The wreck under way, sinking into the rotors -----------------------------------------------------------------------------

    private static void Wreck(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, CStore st, double now)
    {
        if (st.JobDef == ConstructParts.None || ConstructParts.Get(st.JobDef) is not { } def)
            return;
        // Where: the middle of the square it landed on (the Core's own mouth: its middle), sinking to that square's rotors' axis.
        var sq = BreakerWorks.SquareAt(list, st.JobCellX, st.JobCellZ);
        var m = sq >= 0 ? BreakerWorks.Middle(g, list[sq]) : new Vector2(g.OX, g.OZ);
        var deep = sq >= 0 ? BreakerWorks.AxisY(list[sq].N) : BreakerWorks.Lid - 0.4f;
        // How far: the job's share, run on between the server's sends at the pace the last two showed (never more than a send ahead).
        if (st.Progress != st.SinkSeen)
        {
            var dt = (float)(now - st.SinkAt);
            if (st.Progress > st.SinkSeen && dt > 0.05f && st.SinkAt > 0)
                st.SinkRate = (st.Progress - st.SinkSeen) / 100f / dt;
            else if (st.Progress < st.SinkSeen)
                st.SinkRate = 0f;
            st.SinkSeen = st.Progress;
            st.SinkAt = now;
        }
        var share = Math.Clamp(st.Progress / 100f + MathF.Min(0.35f, (float)(now - st.SinkAt) * st.SinkRate), 0f, 1f);
        var lid = g.Floor + BreakerWorks.Lid;
        var foot = new Vector3((def.BoundsMin.X + def.BoundsMax.X) * 0.5f, def.BoundsMin.Y, (def.BoundsMin.Z + def.BoundsMax.Z) * 0.5f);
        var pivot = new Vector3(m.X, lid, m.Y);
        var squash = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateScale(1f - 0.3f * share, 1f - 0.8f * share, 1f - 0.3f * share)
            * Matrix4x4.CreateTranslation(pivot - new Vector3(0, share * (lid - g.Floor - deep), 0)) * frame;
        var at = pivot - foot;
        var drawn = 0;
        foreach (ref readonly var box in def.Boxes.AsSpan())
        {
            if (box.Hidden || box.Bare || box.MergedOnly || ++drawn > MostBoxes)
                continue;
            gfx.Draw(box.Model(0, at + box.Center) * squash, Vector3.Lerp(box.Color, Rust, 0.35f) * 0.75f, 1f, mode: 0, shape: box.Shape);
        }
    }

    // ---- Helpers ----

    /// <summary>A shaped box (the drum's cylinder), turned about x by <paramref name="turn"/> about its middle.</summary>
    private static void Shaped(Renderer gfx, in Matrix4x4 frame, in CPartBox shape, Vector3 centre, Vector3 half, Vector3 colour, float surface = 0f, float turn = 0f, bool alongZ = false)
    {
        // (alongZ: the cylinder laid along z instead, a roller across a belt that runs along x; half.X is still its half length)
        var lay = alongZ ? Matrix4x4.CreateRotationY(MathF.PI * 0.5f) : Matrix4x4.Identity;
        var model = shape.Shape == 0 ? Scale(centre - (alongZ ? new Vector3(half.Y, half.Y, half.X) : half), centre + (alongZ ? new Vector3(half.Y, half.Y, half.X) : half))
            : (shape with { Half = half }).Model(0, Vector3.Zero) * Matrix4x4.CreateRotationX(turn) * lay * Matrix4x4.CreateTranslation(centre);
        gfx.Draw(model * frame, colour, shape: shape.Shape, surface: surface);
    }

    /// <summary>A plate from <paramref name="a"/> to <paramref name="b"/> (its middle line), <paramref name="width"/> along <paramref name="along"/> (a unit axis across that line), <paramref name="thick"/> thick.</summary>
    private static void Plate(Renderer gfx, in Matrix4x4 frame, Vector3 a, Vector3 b, Vector3 along, float width, float thick, Vector3 colour, float surface = 0f)
    {
        var span = b - a;
        var len = span.Length();
        if (len < 0.005f || width < 0.005f)
            return;
        var v = span / len;
        var w = Vector3.Normalize(Vector3.Cross(along, v));
        var c = (a + b) * 0.5f;
        var model = new Matrix4x4(along.X * width, along.Y * width, along.Z * width, 0, v.X * len, v.Y * len, v.Z * len, 0, w.X * thick, w.Y * thick, w.Z * thick, 0, c.X, c.Y, c.Z, 1);
        gfx.Draw(model * frame, colour, surface: surface);
    }

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, in Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour, int mode = 0, float surface = 0) => gfx.Draw(Scale(Vector3.Min(lo, hi), Vector3.Max(lo, hi)) * frame, colour, mode: mode, surface: surface);
}
