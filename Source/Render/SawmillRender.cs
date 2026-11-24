using System.Numerics;
using CallOfSlop.PartKit.Generators;

namespace CallOfSlop;

/// <summary>
/// What a Sawmill draws across its squares (SawmillWorks lays them out: one rule for every peer; docs/sandbox/constructs/production.md
/// "The Sawmill"): over each square of 2 or more one band headrig (its column, the arms, the upper wheel spinning while it saws, the band
/// with its teeth running down, the guide and guards), the track with its carriage stroking a log through the band on the square's own
/// clock, and the log deck behind; a 1 x 1's mill is its Saw Block's own "alone" boxes. For every square the sawdust duct up from its
/// headrig to the trunk into the Core's cyclone, the board's slide down to the plank chain along the front, the chain with its boards
/// running to the Plank Stacker, and each Stacker's pile growing and clearing. Drawn within <see cref="DrawNear"/>. Allocates nothing.
/// </summary>
public static class SawmillRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Body = new(0.369f, 0.42f, 0.227f), Dark = new(0.133f, 0.149f, 0.169f), Steel = new(0.6f, 0.63f, 0.66f),
        Yellow = new(0.95f, 0.69f, 0.12f), Bark = new(0.357f, 0.247f, 0.157f), Grain = new(0.769f, 0.604f, 0.388f), Board = new(0.824f, 0.671f, 0.447f),
        Teeth = new(0.25f, 0.27f, 0.3f), Duct = new(0.52f, 0.55f, 0.58f);
    private const float Spin = 3.2f; // rad/s: the upper wheels while they saw
    private const float ChainSpeed = 0.9f, ChainEvery = 1.7f; // the plank chain's boards (m/s, m apart)
    private const int MostOnChain = 40, MostSleepers = 24, MostLayers = 8;
    private const float LayerEvery = 1.6f; // s a layer of the Stacker's pile
    private static CPartBox _ring, _cylX, _cylY, _cylZ; // (the Saw Block's lone wheel ring, log and carriage wheel, the Core's drop pipe: their shapes, for what is drawn here)
    private static bool _looked;
    private static float SteelSurface, TreadSurface, DirtySurface, HazardSurface; // (Materials/Surfaces.json rows, as Renderer.Draw takes them: found once)

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var now = ConstructRender.Now;
        foreach (var c in world.Bodies)
        {
            if (c.MachineSquareCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (asm.SquareCount == 0 || asm.Core >= c.PartCount || !SawmillWorks.Is(c.Parts[asm.Core].D) || world.StoreOf(c, asm.Core) is not { } st)
                    continue;
                var working = !c.Parts[asm.Core].Broken && ProductionLines.Working(world, c, asm.Core, st, now);
                DrawSawmill(gfx, c, asm, st, working, now);
            }
        }
    }

    private static void Look()
    {
        _looked = true;
        (SteelSurface, TreadSurface, DirtySurface, HazardSurface) = (Surfaces.Code(Surfaces.Id("steel"), 0f), Surfaces.Code(Surfaces.Id("tread plate"), 0f),
            Surfaces.Code(Surfaces.Id("dirty steel"), 0.3f), Surfaces.Code(Surfaces.Id("hazard paint"), 0f));
        if (ConstructParts.Find("Saw Block") is { } sb)
            foreach (var b in sb.Boxes)
            {
                if (b.Shape == 0 || !b.Alone)
                    continue;
                if (b.Work == BoxWork.Spin && b.Half.Y > 0.3f && b.Half.X > _ring.Half.X)
                    _ring = b;
                if (b.Work == BoxWork.Stroke && b.Half.X > 0.3f)
                    _cylX = b;
                if (b.Work == BoxWork.Stroke && b.Half.X < 0.2f && b.Half.Z < b.Half.X)
                    _cylZ = b;
            }
        if (ConstructParts.Find("Sawmill") is { } core)
            foreach (var b in core.Boxes)
                if (b.Shape != 0 && b.Detail && b.Half.Y > 0.5f && b.Half.X < 0.1f)
                    _cylY = b;
    }

    private static void DrawSawmill(Renderer gfx, Construct c, in CMachineAsm asm, CStore st, bool working, double now)
    {
        var d = c.Parts[asm.Core].D;
        ConstructRender.PartDrawn(c, asm.Core, out var pos, out var rot);
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var g = new SmelterWorks.Grid(d);
        var list = MachineBlocks.Squares(c, asm);
        if (!_looked)
            Look();
        var board = BoardColour(c, asm.Core, d, st);
        foreach (ref readonly var q in list)
        {
            var n = q.N;
            float mx = g.X(q.X0) - g.Half + n * g.Half, mz = g.Z(q.Z0) - g.Half + n * g.Half;
            var cutting = false;
            var share = working ? SawmillWorks.CarriageShare(now, n, q.X0, q.Z0, out cutting) : 1f;
            if (n >= 2)
            {
                Mill(gfx, frame, g, n, mx, mz, share, cutting, working, now);
                LogDeck(gfx, frame, g, n, mx, mz);
            }
        }
        Chain(gfx, frame, g, list, board, working, now);
        Ducts(gfx, frame, g, list);
        Stackers(gfx, frame, g, list, board, working, st.Amount > 0, now);
    }

    /// <summary>The boards' colour: what the bin holds (planks, or cloth), else what its set recipe makes, else plain planks.</summary>
    private static Vector3 BoardColour(Construct c, int core, ConstructPartDef d, CStore st)
    {
        var table = RecyclingTable.Current;
        if (st.Amount > 0)
            return Vector3.Lerp(Board, table.ColourOf(st.Material), 0.7f);
        return ConstructRefining.SetOf(c.Parts[core], d) is { } r ? Vector3.Lerp(Board, table.ColourOf(r.Output), 0.7f) : Board;
    }

    // ---- A square's band mill: the headrig, the track, the carriage and its log ------------------------------------------------------

    private static void Mill(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, int n, float mx, float mz, float share, bool cutting, bool working, double now)
    {
        float floor = g.Floor, deck = floor + SawmillWorks.Deck, h = n * g.Half;
        float lr = SawmillWorks.LogR(n), r = SawmillWorks.WheelR(n), rim = SawmillWorks.RimHalf(n), len = SawmillWorks.Length(n), depth = SawmillWorks.CarDepth(n);
        float cut = SawmillWorks.ZCut(mz, n), back = SawmillWorks.ZBack(mz, n), logZ = SawmillWorks.LogZ(mz, n), wy = floor + SawmillWorks.WheelY(n), top = floor + SawmillWorks.Top(n);
        var (col0, col1) = SawmillWorks.Column(mz, n);
        float colHalf = SawmillWorks.ColumnHalf(n), axleZ = cut + r;
        float rail0 = cut + 0.2f, rail1 = cut + depth - 0.15f;
        float cy0 = floor + SawmillWorks.CarriageY0, cy1 = floor + SawmillWorks.CarriageY1, ry0 = floor + SawmillWorks.RailY0, ry1 = floor + SawmillWorks.RailY1;

        // The track: sleepers along it and two rails, behind the band, across the square.
        var sleepers = Math.Min(MostSleepers, (int)((2f * h - 0.4f) / 0.6f) + 1);
        for (var k = 0; k < sleepers; k++)
        {
            var x = mx - h + 0.2f + k * (2f * h - 0.4f) / Math.Max(1, sleepers - 1);
            Box(gfx, frame, new Vector3(x - 0.08f, deck + 0.002f, cut + 0.1f), new Vector3(x + 0.08f, ry0, cut + depth - 0.04f), Body * 0.7f);
        }
        foreach (var z in (ReadOnlySpan<float>)[rail0, rail1])
            Box(gfx, frame, new Vector3(mx - h + 0.05f, ry0, z - 0.03f), new Vector3(mx + h - 0.05f, ry1, z + 0.03f), Steel, surface: SteelSurface);
        // Buffers at the track's ends, hazard yellow.
        foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
            Box(gfx, frame, new Vector3(mx + sx * (h - 0.05f) - 0.04f, ry0, rail0 - 0.06f), new Vector3(mx + sx * (h - 0.05f) + 0.04f, ry1 + 0.14f, rail1 + 0.06f), Yellow, surface: HazardSurface);

        // The carriage at its place in the stroke: frame, bolsters, knees and dogs, wheels on the rails, the setworks box; its log.
        var cx = SawmillWorks.CarriageX(mx, n, share);
        float c0 = cx - len * 0.5f, c1 = cx + len * 0.5f, cz0 = cut + 0.06f, cz1 = cut + depth;
        Box(gfx, frame, new Vector3(c0, cy0, cz0), new Vector3(c1, cy1 - 0.06f, cz1), Dark);
        var bolsters = n + 1;
        for (var k = 0; k < bolsters; k++)
        {
            var x = c0 + 0.2f + k * (len - 0.4f) / (bolsters - 1);
            Box(gfx, frame, new Vector3(x - 0.05f, cy1 - 0.064f, cz0 + 0.02f), new Vector3(x + 0.05f, cy1, cz1 - 0.02f), Yellow);
            Box(gfx, frame, new Vector3(x - 0.04f, cy1 - 0.004f, logZ + lr + 0.01f), new Vector3(x + 0.04f, cy1 + 1.2f * lr, logZ + lr + 0.14f), Steel, surface: SteelSurface); // (a knee)
            Box(gfx, frame, new Vector3(x - 0.014f, cy1 + 0.6f * lr, logZ + lr - 0.02f), new Vector3(x + 0.014f, cy1 + 0.6f * lr + 0.05f, logZ + lr + 0.012f), Yellow); // (its dog)
        }
        foreach (var x in (ReadOnlySpan<float>)[c0 + 0.12f, c1 - 0.12f])
            foreach (var z in (ReadOnlySpan<float>)[rail0, rail1])
                Shaped(gfx, frame, _cylZ, new Vector3(x, (ry1 - 0.01f + cy0 + 0.07f) * 0.5f, z), new Vector3(0.07f, (cy0 + 0.07f - ry1 + 0.01f) * 0.5f, 0.045f), Steel, SteelSurface);
        Box(gfx, frame, new Vector3(cx - 0.14f, cy0 + 0.01f, cz1 - 0.004f), new Vector3(cx + 0.14f, cy1 - 0.07f, cz1 + 0.12f), Dark); // (the setworks box)
        Box(gfx, frame, new Vector3(cx - 0.05f, cy1 - 0.07f, cz1 + 0.02f), new Vector3(cx + 0.05f, cy1 + 0.12f, cz1 + 0.06f), Yellow); // (its lever)
        var logMid = new Vector3(cx, cy1 + lr, logZ);
        Shaped(gfx, frame, _cylX, logMid, new Vector3(len * 0.5f - 0.02f, lr, lr), Bark);
        foreach (var x in (ReadOnlySpan<float>)[c0 + 0.015f, c1 - 0.015f])
            Shaped(gfx, frame, _cylX, new Vector3(x, cy1 + lr, logZ), new Vector3(0.009f, lr - 0.03f, lr - 0.03f), Grain);
        // Where the log has passed the band its front is sawn: a pale face off the cut, a board peeling away from it.
        var lead = c1 - 0.02f;
        if (working && lead > mx + rim)
        {
            var x0 = MathF.Max(c0 + 0.02f, mx + rim);
            Box(gfx, frame, new Vector3(x0, cy1 + 0.25f * lr, cut - 0.016f), new Vector3(lead, cy1 + 1.75f * lr, cut - 0.004f), Grain);
            if (cutting)
                Box(gfx, frame, new Vector3(x0 + 0.05f, cy1 + 0.3f * lr, cut - 0.4f * lr - 0.05f), new Vector3(lead, cy1 + 1.7f * lr, cut - 0.4f * lr - 0.012f), Grain * 0.9f);
        }

        // The band: its cutting run through the log's path (teeth running down it while it saws), the back run in its guard, the slot's guard.
        Box(gfx, frame, new Vector3(mx - rim * 0.8f, deck - 0.03f, cut - 0.006f), new Vector3(mx + rim * 0.8f, wy, cut + 0.006f), Steel, surface: SteelSurface);
        if (working)
            for (var k = 0; k < 4; k++)
            {
                var y = deck + (wy - deck) * (1f - (float)((now * 1.4 + k * 0.25) % 1.0));
                Box(gfx, frame, new Vector3(mx - rim * 0.8f - 0.004f, y - 0.03f, cut - 0.012f), new Vector3(mx + rim * 0.8f + 0.004f, y + 0.03f, cut - 0.005f), Teeth);
            }
        Box(gfx, frame, new Vector3(mx - rim * 0.8f, deck - 0.03f, back - 0.006f), new Vector3(mx + rim * 0.8f, wy, back + 0.006f), Steel, surface: SteelSurface);
        Box(gfx, frame, new Vector3(mx - rim - 0.04f, deck + 0.004f, back - 0.06f), new Vector3(mx + rim + 0.04f, wy - 0.16f, back + 0.06f), Yellow, surface: HazardSurface);
        Box(gfx, frame, new Vector3(mx - rim - 0.1f, deck - 0.004f, cut - 0.1f), new Vector3(mx + rim + 0.1f, deck + 0.03f, cut + 0.05f), Dark);

        // The column: its foot plate, the column, a steel face plate and bolts, the strain housing on top with its handwheel, a ladder up its
        // side, the motor on its back with a belt guard up to the wheel's shaft.
        Box(gfx, frame, new Vector3(mx - colHalf - 0.12f, deck + 0.002f, col0 - 0.08f), new Vector3(mx + colHalf + 0.12f, deck + 0.06f, col1 + 0.08f), Dark);
        Box(gfx, frame, new Vector3(mx - colHalf, deck + 0.06f, col0), new Vector3(mx + colHalf, top - 0.3f, col1), Dark);
        Box(gfx, frame, new Vector3(mx - colHalf + 0.05f, deck + 0.4f, col0 - 0.012f), new Vector3(mx + colHalf - 0.05f, top - 0.6f, col0 + 0.004f), Steel, surface: DirtySurface);
        var rows = Math.Min(8, 2 + n);
        for (var k = 0; k < rows; k++)
        {
            var y = deck + 0.5f + k * (top - deck - 1.2f) / (rows - 1);
            foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
                Box(gfx, frame, new Vector3(mx + sx * (colHalf - 0.09f) - 0.025f, y - 0.025f, col0 - 0.026f), new Vector3(mx + sx * (colHalf - 0.09f) + 0.025f, y + 0.025f, col0 - 0.008f), Steel, surface: SteelSurface);
        }
        Box(gfx, frame, new Vector3(mx - colHalf - 0.04f, top - 0.3f, col0 - 0.04f), new Vector3(mx + colHalf + 0.04f, top - 0.05f, col1 + 0.04f), Body); // (the strain housing)
        Shaped(gfx, frame, _cylY, new Vector3(mx, top - 0.02f, (col0 + col1) * 0.5f), new Vector3(0.16f, 0.03f, 0.16f), Yellow); // (its handwheel)
        var ladderX = mx + colHalf + 0.03f;
        foreach (var z in (ReadOnlySpan<float>)[col0 + 0.06f, col1 - 0.06f])
            Box(gfx, frame, new Vector3(ladderX, deck + 0.06f, z - 0.02f), new Vector3(ladderX + 0.04f, top - 0.3f, z + 0.02f), Yellow);
        for (var y = deck + 0.35f; y < top - 0.4f; y += 0.3f)
            Box(gfx, frame, new Vector3(ladderX + 0.004f, y, col0 + 0.06f), new Vector3(ladderX + 0.036f, y + 0.03f, col1 - 0.06f), Yellow);
        Shaped(gfx, frame, _cylX, new Vector3(mx, deck + 0.3f + 0.1f * n, col1 + 0.18f + 0.04f * n), new Vector3(0.22f + 0.04f * n, 0.2f + 0.05f * n, 0.18f + 0.04f * n), Dark); // (the motor)
        Box(gfx, frame, new Vector3(mx - 0.09f, deck + 0.5f + 0.2f * n, col1 + 0.06f), new Vector3(mx + 0.09f, wy, col1 + 0.2f), Yellow); // (its belt guard)

        // The arms forward to the upper wheel's bearings, the guide over the log on its rod.
        foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
        {
            float a0 = mx + sx * (rim + 0.03f), a1 = mx + sx * (rim + 0.13f);
            Box(gfx, frame, new Vector3(a0, wy - 0.14f, axleZ - 0.14f), new Vector3(a1, wy + 0.14f, col0 + 0.02f), Dark);
            Box(gfx, frame, new Vector3(a0 - sx * 0.004f, wy - 0.18f, axleZ - 0.18f), new Vector3(a1 + sx * 0.004f, wy + 0.18f, axleZ + 0.18f), Steel, surface: SteelSurface);
        }
        float gy0 = cy1 + 2f * lr + 0.06f, gy1 = gy0 + 0.16f;
        Box(gfx, frame, new Vector3(mx - rim - 0.06f, gy0, cut - 0.1f), new Vector3(mx + rim + 0.06f, gy1, cut + 0.12f), Dark);
        Box(gfx, frame, new Vector3(mx + rim + 0.06f, gy0 + 0.02f, cut), new Vector3(mx + rim + 0.11f, wy - 0.12f, cut + 0.06f), Steel, surface: SteelSurface);

        // The upper wheel, turning about x while it saws: its ring, its tyre, three spokes and its hub; a guard over its top.
        var angle = working ? (float)(now * Spin % Math.Tau) : 0f;
        var hub = new Vector3(mx, wy, axleZ);
        Shaped(gfx, frame, _ring, hub, new Vector3(rim, r, r), Steel, SteelSurface, angle);
        Shaped(gfx, frame, _ring, hub, new Vector3(rim * 0.55f, r + 0.014f, r + 0.014f), Dark, 0f, angle);
        for (var k = 0; k < 3; k++)
            gfx.Draw(Matrix4x4.CreateScale(0.06f, 2f * r - 0.1f, 0.08f) * Matrix4x4.CreateRotationX(angle + k * MathF.PI / 3f) * Matrix4x4.CreateTranslation(hub) * frame, Yellow);
        Shaped(gfx, frame, _cylX, hub, new Vector3(rim + 0.05f, 0.1f + 0.02f * n, 0.1f + 0.02f * n), Dark, 0f, angle);
        Box(gfx, frame, new Vector3(mx - rim - 0.03f, wy + r + 0.04f, axleZ - r * 0.7f), new Vector3(mx + rim + 0.03f, wy + r + 0.1f, col0 + 0.02f), Yellow);
    }

    /// <summary>Behind the headrig's column: up to three logs waiting on two skids.</summary>
    private static void LogDeck(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, int n, float mx, float mz)
    {
        float lr = SawmillWorks.LogR(n), len = SawmillWorks.Length(n), deck = g.Floor + SawmillWorks.Deck;
        var (_, col1) = SawmillWorks.Column(mz, n);
        float z0 = col1 + 0.12f, z1 = mz + n * g.Half - 0.05f;
        var logs = Math.Min(3, (int)((z1 - z0) / (2f * lr + 0.06f)));
        if (logs <= 0)
            return;
        foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
            Box(gfx, frame, new Vector3(mx + sx * len / 3f - 0.06f, deck + 0.002f, z0), new Vector3(mx + sx * len / 3f + 0.06f, deck + 0.08f, z1), Dark);
        for (var k = 0; k < logs; k++)
        {
            var at = new Vector3(mx, deck + 0.08f + lr - 0.01f, z0 + lr + 0.02f + k * (2f * lr + 0.06f));
            Shaped(gfx, frame, _cylX, at, new Vector3(len * 0.5f - 0.03f, lr, lr), Bark * (0.9f + 0.05f * k));
            foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
                Shaped(gfx, frame, _cylX, at + new Vector3(sx * (len * 0.5f - 0.04f), 0, 0), new Vector3(0.016f, lr - 0.03f, lr - 0.03f), Grain);
        }
    }

    // ---- The plank chain along the front, each square's slide down to it, its boards running -----------------------------------------

    private static void Chain(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, Vector3 board, bool working, double now)
    {
        var line = SawmillWorks.ChainZ(g, list);
        var stacker = SmelterWorks.RunnerEnd(g, list, out var ex, out var ez);
        float y0 = g.Floor + SawmillWorks.ChainY0, y1 = g.Floor + SawmillWorks.ChainY1, deck = g.Floor + SawmillWorks.Deck;
        const float w = SawmillWorks.ChainHalf;
        var first = float.MaxValue;
        foreach (ref readonly var q in list)
        {
            if (!SawmillWorks.Open(list, q))
                continue;
            var n = q.N;
            float mx = g.X(q.X0) - g.Half + n * g.Half, mz = g.Z(q.Z0) - g.Half + n * g.Half;
            first = MathF.Min(first, mx - 0.4f);
            // The board slide from the band's front down to the chain, on its own side rails; a board on it while the mill saws.
            var zf = SawmillWorks.ZFront(mz, n) - 0.05f;
            var a = new Vector3(mx + 0.3f, deck + 0.03f, zf);
            var b = new Vector3(mx + 0.3f, y1 + 0.01f, line);
            Beam(gfx, frame, a, b, 0.24f, 0.02f, Steel, SteelSurface);
            foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
                Beam(gfx, frame, a + new Vector3(sx * 0.26f, 0.04f, 0), b + new Vector3(sx * 0.26f, 0.04f, 0), 0.02f, 0.05f, Dark);
            if (working)
            {
                var u = (float)((now * 0.6 + q.X0 * 0.37) % 1.0);
                Beam(gfx, frame, Vector3.Lerp(a, b, u) + new Vector3(-0.2f, 0.04f, 0), Vector3.Lerp(a, b, u) + new Vector3(0.2f, 0.04f, 0), 0.08f, 0.025f, board);
            }
        }
        if (first == float.MaxValue)
            return; // (no square has a way to the front: covered ways)
        var end = ex;
        // The chain's bed and its two chain strands, on trestles one a cell along it.
        Box(gfx, frame, new Vector3(first - 0.2f, y0, line - w), new Vector3(end + w, y1 - 0.03f, line + w), Dark);
        foreach (var sz in (ReadOnlySpan<float>)[-1f, 1f])
            Box(gfx, frame, new Vector3(first - 0.2f, y1 - 0.034f, line + sz * (w - 0.04f) - 0.02f), new Vector3(end + w, y1, line + sz * (w - 0.04f) + 0.02f), Steel, surface: SteelSurface);
        for (var x = first; x < end + 0.01f; x += g.Cell)
        {
            Box(gfx, frame, new Vector3(x - 0.05f, g.Floor, line - w + 0.01f), new Vector3(x + 0.05f, y0, line - w + 0.09f), Dark);
            Box(gfx, frame, new Vector3(x - 0.05f, g.Floor, line + w - 0.09f), new Vector3(x + 0.05f, y0, line + w - 0.01f), Dark);
            Box(gfx, frame, new Vector3(x - 0.07f, g.Floor + 0.004f, line - w - 0.04f), new Vector3(x + 0.07f, g.Floor + 0.03f, line + w + 0.04f), Yellow, surface: HazardSurface);
        }
        if (stacker)
        {
            var zEdge = ez + 0.1f - g.Half; // (the Stacker's front edge: SmelterWorks.RunnerEnd's z is 0.1 m in from its middle)
            if (zEdge > line + w)
                Box(gfx, frame, new Vector3(ex - w, y0, line + w - 0.004f), new Vector3(ex + w, y1 - 0.03f, zEdge - 0.02f), Dark); // (a spur to it)
        }
        else
            Box(gfx, frame, new Vector3(end + w, g.Floor, line - w - 0.04f), new Vector3(end + w + 0.08f, y1 + 0.3f, line + w + 0.04f), Yellow, surface: HazardSurface); // (an end stop)
        if (!working)
            return;
        var length = end - first;
        var boards = Math.Min(MostOnChain, (int)(length / ChainEvery) + 1);
        var shift = (float)(now * ChainSpeed % ChainEvery);
        for (var k = 0; k < boards; k++)
        {
            var x = first + shift + k * ChainEvery;
            if (x > end - 0.3f)
                break;
            Box(gfx, frame, new Vector3(x - 0.7f, y1 + 0.002f, line - 0.12f), new Vector3(x + 0.7f, y1 + 0.05f, line + 0.12f), board);
        }
    }

    // ---- The sawdust ducts: a riser from each headrig, a branch to the trunk, the trunk into the Core's cyclone ------------------------

    private static void Ducts(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list)
    {
        var yd = g.Floor + SawmillWorks.DuctY(list);
        var inlet = g.Floor + SawmillWorks.InletY;
        const float tr = 0.2f;
        float far = SawmillWorks.InletEnd, zt = SawmillWorks.CycloneZ;
        foreach (ref readonly var q in list)
        {
            var n = q.N;
            float mx = g.X(q.X0) - g.Half + n * g.Half, mz = g.Z(q.Z0) - g.Half + n * g.Half, dr = SawmillWorks.DuctR(n);
            var (col0, col1) = SawmillWorks.Column(mz, n);
            var zc = (col0 + col1) * 0.5f;
            var top = g.Floor + SawmillWorks.Top(n);
            far = MathF.Max(far, mx + dr);
            Shaped(gfx, frame, _cylY, new Vector3(mx, (top - 0.06f + yd) * 0.5f, zc), new Vector3(dr, (yd - top + 0.06f) * 0.5f, dr), Duct, SteelSurface); // (the riser)
            Shaped(gfx, frame, _cylY, new Vector3(mx, top + 0.1f, zc), new Vector3(dr + 0.04f, 0.04f, dr + 0.04f), Dark); // (its collar)
            if (MathF.Abs(zc - zt) > dr)
                Shaped(gfx, frame, _cylZ, new Vector3(mx, yd, (zc + zt) * 0.5f), new Vector3(dr, dr, MathF.Abs(zc - zt) * 0.5f), Duct, SteelSurface); // (its branch to the trunk)
            Box(gfx, frame, new Vector3(mx - dr - 0.03f, yd - dr - 0.03f, zc - dr - 0.03f), new Vector3(mx + dr + 0.03f, yd + dr + 0.03f, zc + dr + 0.03f), Dark); // (its elbow)
        }
        var x0 = SawmillWorks.InletEnd + (yd > inlet + 0.01f ? 2f * tr : 0f);
        Shaped(gfx, frame, _cylX, new Vector3((x0 + far) * 0.5f, yd, zt), new Vector3((far - x0) * 0.5f, tr, tr), Duct, SteelSurface); // (the trunk)
        for (var x = x0 + 1.5f; x < far - 0.5f; x += 3f)
            Shaped(gfx, frame, _cylX, new Vector3(x, yd, zt), new Vector3(0.04f, tr + 0.03f, tr + 0.03f), Dark); // (its flanges)
        if (yd > inlet + 0.01f)
        {
            // It drops to the cyclone's inlet: a bend down, the drop, a bend in.
            var dx = SawmillWorks.InletEnd + tr;
            Box(gfx, frame, new Vector3(dx - tr - 0.03f, yd - tr - 0.03f, zt - tr - 0.03f), new Vector3(dx + tr + 0.03f, yd + tr + 0.03f, zt + tr + 0.03f), Dark);
            Shaped(gfx, frame, _cylY, new Vector3(dx, (inlet + yd) * 0.5f, zt), new Vector3(tr, (yd - inlet) * 0.5f, tr), Duct, SteelSurface);
            Shaped(gfx, frame, _cylX, new Vector3((SawmillWorks.InletEnd + dx) * 0.5f, inlet, zt), new Vector3(tr * 0.5f + 0.004f, tr, tr), Duct, SteelSurface);
        }
    }

    // ---- The Plank Stackers' piles: a layer a few seconds, cleared at the top ------------------------------------------------------

    private static void Stackers(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, Vector3 board, bool working, bool holds, double now)
    {
        var layers = working ? (int)(now % (LayerEvery * (MostLayers + 1)) / LayerEvery) : holds ? 3 : 0;
        if (layers <= 0)
            return;
        foreach (ref readonly var q in list)
            for (var k = 0; k < q.N; k++)
                if (((q.Ends >> k) & 1) != 0)
                {
                    float bx = g.X(q.X0 + q.N), bz = g.Z(q.Z0 + k), by = g.Floor + SawmillWorks.StackerTop;
                    for (var l = 0; l < layers; l++)
                    {
                        var y = by + 0.004f + l * 0.07f;
                        for (var j = 0; j < 4; j++)
                        {
                            var z = bz - 0.63f + j * 0.42f;
                            Box(gfx, frame, new Vector3(bx - 0.78f, y, z - 0.16f), new Vector3(bx + 0.76f, y + 0.06f, z + 0.16f), board * (0.94f + 0.03f * ((l + j) & 1)));
                        }
                        Box(gfx, frame, new Vector3(bx - 0.5f, y + 0.06f, bz - 0.8f), new Vector3(bx - 0.44f, y + 0.07f, bz + 0.8f), Bark); // (stickers between the layers)
                        Box(gfx, frame, new Vector3(bx + 0.44f, y + 0.06f, bz - 0.8f), new Vector3(bx + 0.5f, y + 0.07f, bz + 0.8f), Bark);
                    }
                }
    }

    // ---- Helpers ----

    /// <summary>A shaped box (a cylinder or ring found on the parts), turned about x by <paramref name="turn"/> about its middle.</summary>
    private static void Shaped(Renderer gfx, in Matrix4x4 frame, in CPartBox shape, Vector3 centre, Vector3 half, Vector3 colour, float surface = 0f, float turn = 0f)
    {
        var model = shape.Shape == 0 ? Scale(centre - half, centre + half) : (shape with { Half = half }).Model(0, Vector3.Zero) * Matrix4x4.CreateRotationX(turn) * Matrix4x4.CreateTranslation(centre);
        gfx.Draw(model * frame, colour, shape: shape.Shape, surface: surface);
    }

    /// <summary>A flat strip from <paramref name="a"/> to <paramref name="b"/> in a plane through x (half <paramref name="hx"/> wide along x, <paramref name="hz"/> thick).</summary>
    private static void Beam(Renderer gfx, in Matrix4x4 frame, Vector3 a, Vector3 b, float hx, float hz, Vector3 colour, float surface = 0f)
    {
        var span = b - a;
        var len = span.Length();
        if (len < 0.01f)
            return;
        var dir = span / len;
        var turn = Vector3.Cross(Vector3.UnitY, dir);
        var q = turn.LengthSquared() < 1e-6f ? (dir.Y < 0 ? Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI) : Quaternion.Identity) : Quaternion.CreateFromAxisAngle(Vector3.Normalize(turn), MathF.Acos(Math.Clamp(dir.Y, -1f, 1f)));
        gfx.Draw(Matrix4x4.CreateScale(hx * 2f, len, hz * 2f) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation((a + b) * 0.5f) * frame, colour, surface: surface);
    }

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, in Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour, int mode = 0, float surface = 0) => gfx.Draw(Scale(Vector3.Min(lo, hi), Vector3.Max(lo, hi)) * frame, colour, mode: mode, surface: surface);
}
