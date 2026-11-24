using System.Numerics;
using CallOfSlop.PartKit.Generators;

namespace CallOfSlop;

/// <summary>
/// What a Smelter draws across its squares (SmelterWorks lays them out: one rule for every peer; docs/sandbox/constructs/production.md
/// "Refiners as block machines"): each square of 2 or more's stacks, the charging belt from the Core's hopper onto the furnace tops with
/// its rollers turning and ore riding it, each square's hot-blast main and tuyeres, the tapholes and the runner to the Casting Bed, and
/// the pigs in the beds' moulds. While it works the stacks smoke, each square taps on its own clock (the taphole flares, a glow runs down
/// the runner, the pigs fill orange and cool to grey) and the belt carries ore. A 1 x 1 is a lone block: its own stub stack and door are
/// its part's "alone" boxes. Drawn within <see cref="DrawNear"/>. Allocates nothing.
/// </summary>
public static class SmelterRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Body = new(0.604f, 0.29f, 0.173f), Dark = new(0.133f, 0.149f, 0.169f), Steel = new(0.6f, 0.63f, 0.66f),
        Soot = new(0.17f, 0.17f, 0.17f), Rubber = new(0.1f, 0.1f, 0.11f), Ore = new(0.36f, 0.26f, 0.2f), Hot = new(1f, 0.5f, 0.12f),
        Iron = new(0.42f, 0.42f, 0.44f), Refractory = new(0.55f, 0.5f, 0.45f), Yellow = new(0.94f, 0.69f, 0.12f), Ember = new(0.35f, 0.14f, 0.08f);
    private const float TapSpeed = 2.5f, Glow = 1.4f, Cool = 3f; // the tap's glow along the runner (m/s, m long), the pigs' cooling (s)
    private static double _last;
    private static CPartBox _stack; // (the Hearth Block's stub stack: its cylinder shape, for the stacks drawn here)
    private static bool _looked;

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var now = ConstructRender.Now;
        var dt = (float)Math.Clamp(now - _last, 0, 0.1);
        _last = now;
        foreach (var c in world.Bodies)
        {
            if (c.MachineSquareCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (asm.SquareCount == 0 || asm.Core >= c.PartCount || !SmelterWorks.Is(c.Parts[asm.Core].D) || world.StoreOf(c, asm.Core) is not { } st)
                    continue;
                var working = !c.Parts[asm.Core].Broken && ProductionLines.Working(world, c, asm.Core, st, now);
                DrawSmelter(gfx, c, asm, working, now, dt);
            }
        }
    }

    private static void DrawSmelter(Renderer gfx, Construct c, in CMachineAsm asm, bool working, double now, float dt)
    {
        var d = c.Parts[asm.Core].D;
        ConstructRender.PartDrawn(c, asm.Core, out var pos, out var rot);
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var g = new SmelterWorks.Grid(d);
        var list = MachineBlocks.Squares(c, asm);
        var top = g.Floor + SmelterWorks.HearthTop;
        if (!_looked)
        {
            _looked = true;
            if (ConstructParts.Find("Hearth Block") is { } hb)
                foreach (var b in hb.Boxes)
                    if (b.Shape != 0 && b.Alone && b.Half.Y > _stack.Half.Y)
                        _stack = b;
        }
        Stacks(gfx, c, frame, g, list, top, working, dt);
        Belts(gfx, frame, g, list, top, working, now);
        Blast(gfx, frame, g, list, top);
        Runner(gfx, c, frame, g, list, working, now, dt);
    }

    // ---- The stacks: one a column on a square's back row, 1 + n m over the top, smoking while it works --------------------------

    private static void Stacks(Renderer gfx, Construct c, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, float top, bool working, float dt)
    {
        foreach (ref readonly var q in list)
        {
            if (q.N < 2)
                continue;
            var z = g.Z(q.Z0 + q.N - 1) + SmelterWorks.StackZ;
            float r = 0.2f + 0.04f * q.N, h = 1f + q.N;
            for (var x = q.X0; x < q.X0 + q.N; x++)
            {
                var cx = g.X(x);
                Round(gfx, frame, new Vector3(cx, top + 0.09f, z), new Vector3(r + 0.12f, 0.09f, r + 0.12f), Dark); // (its base collar)
                Round(gfx, frame, new Vector3(cx, top + 0.18f + (h - 0.18f) * 0.5f, z), new Vector3(r, (h - 0.18f) * 0.5f, r), Body);
                for (var k = 1; k <= 2; k++)
                    Round(gfx, frame, new Vector3(cx, top + h * k / 3f, z), new Vector3(r + 0.045f, 0.05f, r + 0.045f), Dark); // (its collars)
                Round(gfx, frame, new Vector3(cx, top + h - 0.07f, z), new Vector3(r + 0.025f, 0.07f, r + 0.025f), Soot); // (a sooty lip)
                if (working && Fx.On)
                {
                    var at = Vector3.Transform(new Vector3(cx, top + h + 0.1f, z), frame) + (gfx.Relative ? gfx.Origin : Vector3.Zero);
                    var up = Vector3.TransformNormal(Vector3.UnitY, frame);
                    for (var n = Fx.Share(3f * dt * (1f + r)); n > 0; n--)
                        Fx.EngineSmoke(at, c.Vel * 0.6f + up * (1.2f + r) + new Vector3(0.2f, 0, 0.1f), r * 1.6f, 3.6f, 0.6f);
                }
            }
        }
    }

    // ---- The charging belt: up from the Core's hopper spout along its row, cross belts to the squares off it ----------------------

    private static void Belts(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, float top, bool working, double now)
    {
        var beltY = g.Floor + SmelterWorks.BeltY;
        var z0 = g.Z(0) + SmelterWorks.BeltZ;
        var end = g.X(0) + g.Half;
        foreach (ref readonly var q in list)
            end = MathF.Max(end, q.Z0 <= 0 && q.Z0 + q.N > 0 ? g.X(q.X0 + q.N - 1) + g.Half - 0.15f : CrossX(g, q));
        // The incline from the spout (over the Core's +x top) up to the furnace tops, then the run along the row.
        var spout = new Vector3(g.X(0) + 0.66f, g.Floor + 2.32f, z0);
        var foot = new Vector3(g.X(0) + g.Half + 0.45f, beltY, z0);
        Slant(gfx, frame, spout, foot, SmelterWorks.BeltHalf, working, now);
        Run(gfx, frame, g, list, foot, new Vector3(end, beltY, z0), top, working, now, true);
        foreach (ref readonly var q in list)
        {
            if (q.Z0 <= 0 && q.Z0 + q.N > 0)
                continue;
            var x = CrossX(g, q);
            var zq = g.Z(q.Z0) - g.Half + q.N * g.Half; // (the square's middle)
            Run(gfx, frame, g, list, new Vector3(x, beltY + 0.004f, z0), new Vector3(x, beltY + 0.004f, zq), top, working, now, false);
        }
    }

    /// <summary>The cell joint nearest a square's middle, where its cross belt runs (clear of the stacks on the column middles).</summary>
    private static float CrossX(in SmelterWorks.Grid g, in CMachineSquare q) => g.X(q.X0 + q.N / 2) - g.Half;

    /// <summary>A flat belt from <paramref name="a"/> to <paramref name="b"/> (along x or z): its frame, rollers turning, ore riding it, posts over empty cells.</summary>
    private static void Run(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, Vector3 a, Vector3 b, float top, bool working, double now, bool alongX)
    {
        var len = Vector3.Distance(a, b);
        if (len < 0.1f)
            return;
        var dir = (b - a) / len;
        var side = alongX ? Vector3.UnitZ : Vector3.UnitX;
        const float half = SmelterWorks.BeltHalf;
        var lo = Vector3.Min(a, b) - side * half;
        var hi = Vector3.Max(a, b) + side * half;
        Box(gfx, frame, new Vector3(lo.X, a.Y - 0.16f, lo.Z), new Vector3(hi.X, a.Y - 0.06f, hi.Z), Dark); // (its frame)
        Box(gfx, frame, Vector3.Min(a, b) - side * (half + 0.04f) + new Vector3(0, -0.06f, 0), Vector3.Max(a, b) - side * half + new Vector3(0, 0.08f, 0), Yellow); // (its guard rails)
        Box(gfx, frame, Vector3.Min(a, b) + side * half + new Vector3(0, -0.06f, 0), Vector3.Max(a, b) + side * (half + 0.04f) + new Vector3(0, 0.08f, 0), Yellow);
        Box(gfx, frame, new Vector3(lo.X, a.Y - 0.004f, lo.Z) + side * 0.04f, new Vector3(hi.X, a.Y + 0.01f, hi.Z) - side * 0.04f, Rubber); // (the belt)
        var spin = Matrix4x4.CreateFromAxisAngle(side, working ? -(float)(now * 3.0 % Math.Tau) : 0f);
        for (var s = 0.25f; s < len; s += 0.5f)
        {
            var at = a + dir * s + new Vector3(0, -0.04f, 0);
            var size = alongX ? new Vector3(0.07f, 0.07f, half * 2f - 0.1f) : new Vector3(half * 2f - 0.1f, 0.07f, 0.07f);
            gfx.Draw(Matrix4x4.CreateScale(size) * spin * Matrix4x4.CreateTranslation(at) * frame, Steel); // (a roller, turning)
        }
        if (working)
        {
            var off = (float)(now * 0.8 % 0.7);
            for (var s = off; s < len; s += 0.7f)
            {
                var at = a + dir * s;
                Box(gfx, frame, at + new Vector3(-0.07f, 0.01f, -0.07f), at + new Vector3(0.07f, 0.1f, 0.07f), Ore); // (ore riding it)
            }
        }
        // A post where the belt crosses a cell with no block under it (a gap, or the run past the last square).
        for (var s = 1f; s < len; s += g.Cell)
        {
            var at = a + dir * s;
            var cx = (int)MathF.Round((at.X - g.OX) / g.Cell);
            var cz = (int)MathF.Round((at.Z - g.OZ) / g.Cell);
            if (cx > 0 && !SmelterWorks.Taken(list, cx, cz))
                Box(gfx, frame, new Vector3(at.X - 0.06f, g.Floor, at.Z - 0.06f), new Vector3(at.X + 0.06f, a.Y - 0.16f, at.Z + 0.06f), Steel);
        }
    }

    /// <summary>The incline: a belt from <paramref name="a"/> up to <paramref name="b"/> along x, its frame under it.</summary>
    private static void Slant(Renderer gfx, in Matrix4x4 frame, Vector3 a, Vector3 b, float half, bool working, double now)
    {
        var span = b - a;
        var len = span.Length();
        var tilt = Matrix4x4.CreateRotationZ(MathF.Atan2(span.Y, span.X));
        var mid = (a + b) * 0.5f;
        gfx.Draw(Matrix4x4.CreateScale(len, 0.1f, half * 2f) * tilt * Matrix4x4.CreateTranslation(mid + new Vector3(0, -0.11f, 0)) * frame, Dark);
        gfx.Draw(Matrix4x4.CreateScale(len, 0.014f, half * 2f - 0.08f) * tilt * Matrix4x4.CreateTranslation(mid) * frame, Rubber);
        foreach (var sz in (ReadOnlySpan<float>)[-1f, 1f])
            gfx.Draw(Matrix4x4.CreateScale(len, 0.14f, 0.04f) * tilt * Matrix4x4.CreateTranslation(mid + new Vector3(0, -0.02f, sz * (half + 0.02f))) * frame, Yellow);
        if (working)
        {
            var off = (float)(now * 0.8 % 0.7);
            for (var s = off; s < len; s += 0.7f)
                gfx.Draw(Matrix4x4.CreateScale(0.14f, 0.09f, 0.14f) * tilt * Matrix4x4.CreateTranslation(a + span * (s / len) + new Vector3(0, 0.05f, 0)) * frame, Ore);
        }
    }

    // ---- The hot-blast mains: along each square's free front columns, a tuyere into each, a riser over the roof at its -x end ------

    private static void Blast(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, float top)
    {
        var y = g.Floor + SmelterWorks.BlastY;
        const float r = SmelterWorks.BlastHalf;
        foreach (ref readonly var q in list)
        {
            var za = g.Z(q.Z0) - g.Half;
            var zm = za - SmelterWorks.BlastOut;
            int x = q.X0;
            while (x < q.X0 + q.N)
            {
                if (SmelterWorks.Taken(list, x, q.Z0 - 1))
                {
                    x++;
                    continue;
                }
                int x1 = x;
                while (x1 + 1 < q.X0 + q.N && !SmelterWorks.Taken(list, x1 + 1, q.Z0 - 1))
                    x1++;
                float xa = g.X(x) - g.Half + 0.12f, xb = g.X(x1) + g.Half - 0.12f;
                Box(gfx, frame, new Vector3(xa, y - r, zm - r), new Vector3(xb, y + r, zm + r), Dark); // (the main)
                for (var k = x; k <= x1; k++)
                {
                    var tx = g.X(k) + 0.62f;
                    Box(gfx, frame, new Vector3(tx - 0.05f, g.Floor + 1.2f, zm - 0.05f), new Vector3(tx + 0.05f, y - r, zm + 0.05f), Dark); // (a tuyere's drop)
                    Box(gfx, frame, new Vector3(tx - 0.05f, g.Floor + 1.14f, zm - 0.05f), new Vector3(tx + 0.05f, g.Floor + 1.24f, za - 0.004f), Dark); // (into the hearth)
                    Box(gfx, frame, new Vector3(tx - 0.08f, y - r - 0.06f, zm - 0.08f), new Vector3(tx + 0.08f, y - r, zm + 0.08f), Steel); // (its flange)
                }
                // The riser at its -x end over the roof's edge, and its elbow onto the top.
                Box(gfx, frame, new Vector3(xa - 0.004f, y + r, zm - r), new Vector3(xa + 2 * r, top + 0.45f, zm + r), Dark);
                Box(gfx, frame, new Vector3(xa - 0.004f, top + 0.45f - 2 * r, zm - r), new Vector3(xa + 2 * r, top + 0.45f, za + 0.6f), Dark);
                x = x1 + 1;
            }
        }
    }

    // ---- The tapholes, the runner and the Casting Beds' pigs ------------------------------------------------------------------

    private static void Runner(Renderer gfx, Construct c, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, bool working, double now, float dt)
    {
        var line = SmelterWorks.RunnerZ(g, list);
        var bed = SmelterWorks.RunnerEnd(g, list, out var ex, out var ez);
        float y0 = g.Floor + SmelterWorks.RunnerY0, y1 = g.Floor + SmelterWorks.RunnerY1;
        const float w = SmelterWorks.RunnerHalf;
        var first = float.MaxValue;
        var pigs = 0f; // (how hot the pigs are: the latest tap to reach the bed)
        for (var i = 0; i < list.Length; i++)
        {
            ref readonly var q = ref list[i];
            var col = SmelterWorks.TapColumn(list, q);
            if (col < 0)
                continue;
            var len = SmelterWorks.TapPath(g, list, q, col, out var p0, out var p1, out var p2, out var p3);
            first = MathF.Min(first, p0.X);
            // The taphole's spout and the spur out to the line.
            Box(gfx, frame, new Vector3(p0.X - 0.14f, y0 + 0.02f, p0.Y - 0.2f), new Vector3(p0.X + 0.14f, y1 + 0.04f, p0.Y - 0.004f), Dark);
            Trough(gfx, frame, new Vector2(p0.X, p0.Y - 0.2f), p1, y0, y1, w, alongX: false);
            if (!working)
                continue;
            var period = SmelterWorks.TapPeriod(q);
            var speed = MathF.Max(TapSpeed, len / (period - 1.5f));
            var phase = (float)((now + i * 1.7) % period);
            var s = phase * speed;
            if (s < len + Glow)
            {
                // The tap: the taphole flares, a glow runs down the runner.
                gfx.Draw(Scale(new Vector3(p0.X - 0.06f, g.Floor + SmelterWorks.TapY - 0.06f, p0.Y - 0.21f), new Vector3(p0.X + 0.06f, g.Floor + SmelterWorks.TapY + 0.06f, p0.Y - 0.19f)) * frame, Hot, mode: 2);
                GlowAlong(gfx, frame, p0, p1, p2, p3, MathF.Max(0, s - Glow), MathF.Min(len, s), y1 + 0.002f, w - 0.04f);
                if (phase < 0.5f && Fx.On)
                {
                    var at = Vector3.Transform(new Vector3(p0.X, g.Floor + SmelterWorks.TapY, p0.Y - 0.25f), frame) + (gfx.Relative ? gfx.Origin : Vector3.Zero);
                    for (var n = Fx.Share(30f * dt); n > 0; n--)
                        Fx.Emit(FxKind.Spark, at, c.Vel + new Vector3(Random.Shared.NextSingle() - 0.5f, 1.2f + Random.Shared.NextSingle(), Random.Shared.NextSingle() - 0.5f) * 2f, 0.015f, 0.4f);
                }
            }
            else if (bed)
                pigs = MathF.Max(pigs, 1f - (s - len - Glow) / speed / Cool);
        }
        if (first == float.MaxValue)
            return; // (no taphole has a way to the front: covered launders)
        // The line and its lip into the bed's sow (or its end past the last square).
        Trough(gfx, frame, new Vector2(first - w, line), new Vector2(ex + w, line), y0, y1, w, alongX: true);
        if (bed)
            Trough(gfx, frame, new Vector2(ex, line + w), new Vector2(ex, ez), y1 - 0.06f, y1 + 0.06f, w * 0.8f, alongX: false);
        else
            Box(gfx, frame, new Vector3(ex - 0.3f, g.Floor, line - 0.3f), new Vector3(ex + 0.3f, y0 + 0.1f, line + 0.3f), Dark); // (a sand pit at its end)
        // The pigs in every bed's moulds: grey iron, orange while a tap's iron is fresh.
        var colour = Vector3.Lerp(Iron, Hot, Math.Clamp(pigs, 0f, 1f));
        var mode = pigs > 0.05f ? 2 : 0;
        foreach (ref readonly var q in list)
            for (var k = 0; k < q.N; k++)
                if (((q.Ends >> k) & 1) != 0)
                {
                    float bx = g.X(q.X0 + q.N), bz = g.Z(q.Z0 + k), by = g.Floor + SmelterWorks.BedTop;
                    foreach (var (x0, z0, x1, z1) in SmelterParts.Moulds)
                        gfx.Draw(Scale(new Vector3(bx + (float)x0 + 0.01f, by + 0.004f, bz + (float)z0 + 0.01f), new Vector3(bx + (float)x1 - 0.01f, by + 0.026f, bz + (float)z1 - 0.01f)) * frame, colour, mode: mode);
                }
    }

    /// <summary>A refractory trough from <paramref name="a"/> to <paramref name="b"/> (x, z), its channel dark.</summary>
    private static void Trough(Renderer gfx, in Matrix4x4 frame, Vector2 a, Vector2 b, float y0, float y1, float w, bool alongX)
    {
        var lo = Vector2.Min(a, b);
        var hi = Vector2.Max(a, b);
        var s = alongX ? new Vector2(0, w) : new Vector2(w, 0);
        Box(gfx, frame, new Vector3(lo.X - s.X, y0, lo.Y - s.Y), new Vector3(hi.X + s.X, y1, hi.Y + s.Y), Refractory);
        Box(gfx, frame, new Vector3(lo.X - s.X * 0.55f, y1 - 0.004f, lo.Y - s.Y * 0.55f), new Vector3(hi.X + s.X * 0.55f, y1 + 0.001f, hi.Y + s.Y * 0.55f), Ember);
    }

    /// <summary>The glow of a tap on the runner's path from distance <paramref name="s0"/> to <paramref name="s1"/>.</summary>
    private static void GlowAlong(Renderer gfx, Matrix4x4 frame, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float s0, float s1, float y, float w)
    {
        var at = 0f;
        Leg(p0, p1);
        Leg(p1, p2);
        Leg(p2, p3);

        void Leg(Vector2 a, Vector2 b)
        {
            var len = Vector2.Distance(a, b);
            float u0 = Math.Clamp(s0 - at, 0, len), u1 = Math.Clamp(s1 - at, 0, len);
            at += len;
            if (u1 - u0 < 0.01f)
                return;
            var dir = (b - a) / len;
            var lo = Vector2.Min(a + dir * u0, a + dir * u1);
            var hi = Vector2.Max(a + dir * u0, a + dir * u1);
            var side = MathF.Abs(dir.X) > 0.5f ? new Vector2(0, w * 0.5f) : new Vector2(w * 0.5f, 0);
            gfx.Draw(Scale(new Vector3(lo.X - side.X, y, lo.Y - side.Y), new Vector3(hi.X + side.X, y + 0.012f, hi.Y + side.Y)) * frame, Hot, mode: 2);
        }
    }

    // ---- Helpers ----

    private static void Round(Renderer gfx, in Matrix4x4 frame, Vector3 centre, Vector3 half, Vector3 colour)
    {
        if (_stack.Shape == 0)
            gfx.Draw(Scale(centre - half, centre + half) * frame, colour);
        else
            gfx.Draw((_stack with { Half = half }).Model(0, centre) * frame, colour, shape: _stack.Shape);
    }

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, in Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour) => gfx.Draw(Scale(Vector3.Min(lo, hi), Vector3.Max(lo, hi)) * frame, colour);
}
