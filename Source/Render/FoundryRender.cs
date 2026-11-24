using System.Numerics;
using CallOfSlop.PartKit.Generators;

namespace CallOfSlop;

/// <summary>
/// What a Foundry draws across its squares (FoundryWorks lays them out: one rule for every peer; docs/sandbox/constructs/production.md
/// "Refiners as block machines"): one crucible over each square of 2 or more on its trunnion stands, tipping toward the front to pour on its
/// own clock (a 1 x 1's crucible is its Crucible Block's own "alone" boxes, on the same clock), its melt glowing; the pour from each lip
/// into the launder, in the output's colour; the launder on its trestles along the front to the Mould Line, glowing while a pour runs down
/// it; the moulds filling in the output's colour; and the hood on four posts over the whole bed with its collared stack, smoking while it
/// works. Drawn within <see cref="DrawNear"/>. Allocates nothing.
/// </summary>
public static class FoundryRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Body = new(0.42f, 0.29f, 0.208f), Dark = new(0.133f, 0.149f, 0.169f),
        Steel = new(0.6f, 0.63f, 0.66f), Soot = new(0.17f, 0.17f, 0.17f), Lining = new(0.54f, 0.5f, 0.45f), Hot = new(1f, 0.5f, 0.12f),
        Ember = new(0.35f, 0.14f, 0.08f), Yellow = new(0.94f, 0.69f, 0.12f), Roof = new(0.36f, 0.38f, 0.4f), Lamp = new(1f, 0.9f, 0.7f);
    private const float StandIn = 0.09f, StandHalf = 0.065f; // a merged crucible's trunnion stands: in from the square's edge, and half as wide
    private const int MostBolts = 24; // (a hood course's bolts along one edge, about one each 0.9 m)
    private const float MouldEvery = 1.4f; // s: a mould fills on each pour's arrival, one after another along the line
    private static double _last;
    private static CPartBox _cyl, _pin; // (the Crucible Block's lone crucible and trunnion pin: their cylinder shapes, for what is drawn here)
    private static bool _looked;
    private static float SteelSurface, ShellSurface, PlatedSurface, SootSurface, HazardSurface; // (Materials/Surfaces.json rows, as Renderer.Draw takes them: found once)

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
                if (asm.SquareCount == 0 || asm.Core >= c.PartCount || !FoundryWorks.Is(c.Parts[asm.Core].D) || world.StoreOf(c, asm.Core) is not { } st)
                    continue;
                var working = !c.Parts[asm.Core].Broken && ProductionLines.Working(world, c, asm.Core, st, now);
                DrawFoundry(gfx, c, asm, st, working, now, dt);
            }
        }
    }

    private static void DrawFoundry(Renderer gfx, Construct c, in CMachineAsm asm, CStore st, bool working, double now, float dt)
    {
        var d = c.Parts[asm.Core].D;
        ConstructRender.PartDrawn(c, asm.Core, out var pos, out var rot);
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var g = new SmelterWorks.Grid(d);
        var list = MachineBlocks.Squares(c, asm);
        if (!_looked)
        {
            _looked = true;
            (SteelSurface, ShellSurface, PlatedSurface, SootSurface, HazardSurface) = (Surfaces.Code(Surfaces.Id("steel"), 0f), Surfaces.Code(Surfaces.Id("dirty steel"), 0.3f),
                Surfaces.Code(Surfaces.Id("plated steel"), 0.2f), Surfaces.Code(Surfaces.Id("soot"), 0f), Surfaces.Code(Surfaces.Id("hazard paint"), 0f));
            if (ConstructParts.Find("Crucible Block") is { } cb)
                foreach (var b in cb.Boxes)
                {
                    if (b.Shape != 0 && b.Alone && b.Work == BoxWork.Hinge && b.Half.Y > _cyl.Half.Y)
                        _cyl = b;
                    if (b.Shape != 0 && b.Alone && b.Work == BoxWork.None && b.Half.X > b.Half.Y && b.Half.X > _pin.Half.X)
                        _pin = b;
                }
        }
        var melt = MeltColour(c, asm.Core, d, st);
        Crucibles(gfx, frame, g, list, melt, working, now);
        Launder(gfx, frame, g, list, melt, working, now);
        Hood(gfx, c, frame, g, list, working, dt);
    }

    /// <summary>
    /// What it pours: the output's colour (what its bin holds; with the bin drained by a chute, the recipe it works: its set one, else the one its
    /// mixed input picks), else a dull iron (nothing yet).
    /// </summary>
    private static Vector3 MeltColour(Construct c, int core, ConstructPartDef d, CStore st)
    {
        var table = RecyclingTable.Current;
        if (st.Amount > 0)
            return table.ColourOf(st.Material);
        var recipe = ConstructRefining.SetOf(c.Parts[core], d) ?? (st.MixN > 0 ? ConstructRefining.MixRecipe(RefiningTable.Current, d, st) : null);
        return recipe != null ? table.ColourOf(recipe.Output) : new Vector3(0.42f, 0.42f, 0.44f);
    }

    /// <summary>The pour's glow: the output's colour lit hot.</summary>
    private static Vector3 PourColour(Vector3 melt) => Vector3.Lerp(Hot, melt, 0.35f);

    // ---- The crucibles: one over each square of 2 or more, on trunnion stands, tipping to pour ------------------------------------

    private static void Crucibles(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, Vector3 melt, bool working, double now)
    {
        var top = g.Floor + FoundryWorks.PitTop;
        foreach (ref readonly var q in list)
        {
            var n = q.N;
            var mid = FoundryWorks.Middle(g, q);
            var pouring = false;
            var share = working ? FoundryWorks.TiltShare(now, n, q.X0, q.Z0, out pouring) : 0f;
            if (n >= 2)
            {
                float r = FoundryWorks.Radius(n), h = FoundryWorks.Height(n), ty = g.Floor + FoundryWorks.Trunnion(n), y0 = g.Floor + FoundryWorks.Bottom(n);
                float rb = r * FoundryWorks.TaperBottom, rm = r * FoundryWorks.TaperMiddle, tilt = share * FoundryWorks.Tilt(n) * MathF.PI / 180f;
                var pivot = new Vector3(mid.X, ty, mid.Y);
                var tip = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(-Vector3.UnitX, tilt) * Matrix4x4.CreateTranslation(pivot) * frame;
                // The steel shell in three courses widening to the rim, a dark band over each joint and round the top, the refractory rim, the
                // crust round the melt's edge, the melt, the lip's spout: all tipped about the trunnions.
                Shell(gfx, tip, new Vector3(mid.X, y0 + h / 6f, mid.Y), new Vector3(rb, h / 6f - 0.004f, rb));
                Shell(gfx, tip, new Vector3(mid.X, y0 + h * 0.5f, mid.Y), new Vector3(rm, h / 6f - 0.004f, rm));
                Shell(gfx, tip, new Vector3(mid.X, y0 + h * 5f / 6f + 0.002f, mid.Y), new Vector3(r, h / 6f - 0.002f, r));
                Round(gfx, tip, new Vector3(mid.X, y0 + h / 3f, mid.Y), new Vector3(rm + 0.03f, 0.05f, rm + 0.03f), Dark);
                Round(gfx, tip, new Vector3(mid.X, y0 + h * 2f / 3f, mid.Y), new Vector3(r + 0.03f, 0.05f, r + 0.03f), Dark);
                Round(gfx, tip, new Vector3(mid.X, y0 + h - 0.08f, mid.Y), new Vector3(r + 0.03f, 0.05f, r + 0.03f), Dark);
                Round(gfx, tip, new Vector3(mid.X, y0 + h + 0.013f, mid.Y), new Vector3(r - 0.05f, 0.017f, r - 0.05f), Lining);
                var hot = working ? Vector3.Lerp(Hot, melt, 0.25f) * (0.85f + 0.15f * MathF.Sin((float)now * 5f + q.X0)) : Ember;
                Round(gfx, tip, new Vector3(mid.X, y0 + h + 0.026f, mid.Y), new Vector3(r - 0.1f, 0.008f, r - 0.1f), (working ? Vector3.Lerp(Ember, melt, 0.3f) : Soot) * 0.8f); // (the crust)
                gfx.Draw(Cyl(new Vector3(mid.X, y0 + h + 0.034f, mid.Y), new Vector3(r - 0.16f - 0.04f * n, 0.008f, r - 0.16f - 0.04f * n)) * tip, hot, mode: working ? 2 : 0, shape: _cyl.Shape);
                var sp = FoundryWorks.Spout(n);
                Box(gfx, tip, new Vector3(mid.X - 0.1f - 0.04f * n, y0 + h - 0.14f, mid.Y - r - sp), new Vector3(mid.X + 0.1f + 0.04f * n, y0 + h - 0.004f, mid.Y - r + 0.04f), Dark); // (the lip's spout)
                gfx.Draw(Scale(new Vector3(mid.X - 0.06f - 0.03f * n, y0 + h - 0.1f, mid.Y - r - sp + 0.02f), new Vector3(mid.X + 0.06f + 0.03f * n, y0 + h - 0.02f, mid.Y - r)) * tip, working ? hot : Ember, mode: working ? 2 : 0);
                // The trunnion stands in the square's 0.2 m margin past the rim (clear of the bands at r + 0.03): a foot, two legs, its
                // bearing and the pin from the shell's middle course into it; the tilt gear on the +x one's outer face, its motor at its foot.
                foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
                {
                    var ex = mid.X + sx * (n * g.Half - StandIn);
                    var inner = mid.X + sx * (rm - 0.02f);
                    Box(gfx, frame, new Vector3(ex - StandHalf, top, mid.Y - 0.5f - 0.1f * n), new Vector3(ex + StandHalf, top + 0.18f, mid.Y + 0.5f + 0.1f * n), Dark); // (its foot)
                    foreach (var sz in (ReadOnlySpan<float>)[-1f, 1f])
                        Leg(gfx, frame, new Vector3(ex, top + 0.18f, mid.Y + sz * (0.4f + 0.1f * n)), new Vector3(ex, ty - 0.1f, mid.Y), 0.05f, Dark);
                    Box(gfx, frame, new Vector3(ex - StandHalf, ty - 0.18f, mid.Y - 0.24f), new Vector3(ex + StandHalf, ty + 0.2f, mid.Y + 0.24f), Steel, surface: SteelSurface); // (its bearing)
                    gfx.Draw(Pin(new Vector3((ex + inner) * 0.5f, ty, mid.Y), new Vector3(MathF.Abs(ex - inner) * 0.5f, 0.1f, 0.1f)) * frame, Steel, shape: _pin.Shape, surface: SteelSurface);
                    if (sx > 0)
                    {
                        var gx = ex + StandHalf + 0.016f;
                        gfx.Draw(Pin(new Vector3(gx, ty, mid.Y), new Vector3(0.012f, 0.3f + 0.08f * n, 0.3f + 0.08f * n)) * Matrix4x4.CreateTranslation(-new Vector3(gx, ty, mid.Y)) * Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, -tilt) * Matrix4x4.CreateTranslation(new Vector3(gx, ty, mid.Y)) * frame, Yellow, shape: _pin.Shape); // (the sector gear, turning as it tips)
                        Box(gfx, frame, new Vector3(ex - StandHalf, top + 0.18f, mid.Y - 0.16f), new Vector3(gx + 0.012f, top + 0.5f, mid.Y + 0.16f), Dark); // (its motor, on the foot)
                        Box(gfx, frame, new Vector3(gx - 0.008f, top + 0.5f, mid.Y - 0.05f), new Vector3(gx + 0.008f, ty - 0.26f - 0.08f * n, mid.Y + 0.05f), Steel); // (the drive up to it)
                    }
                }
            }
            // The pour: from the lip down into the launder (or the spur in front of it), in the output's colour.
            if (pouring && FoundryWorks.Open(list, q))
            {
                var lip = FoundryWorks.Lip(n, mid.X, mid.Y, g.Floor, share);
                var w = 0.05f + 0.025f * n;
                gfx.Draw(Scale(new Vector3(lip.X - w, g.Floor + FoundryWorks.LaunderY1 - 0.02f, lip.Z - w), new Vector3(lip.X + w, lip.Y, lip.Z + w)) * frame, PourColour(melt), mode: 2);
            }
        }
    }

    // ---- The launder on its trestles along the front, its spurs, its lip into the Mould Line, and the moulds -------------------------

    private static void Launder(Renderer gfx, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, Vector3 melt, bool working, double now)
    {
        var line = FoundryWorks.LaunderZ(g, list);
        var bed = SmelterWorks.RunnerEnd(g, list, out var ex, out var ez);
        float y0 = g.Floor + FoundryWorks.LaunderY0, y1 = g.Floor + FoundryWorks.LaunderY1;
        const float w = FoundryWorks.LaunderHalf;
        var first = float.MaxValue;
        var lit = float.MaxValue; // (the launder glows from the first pouring crucible's lip to its end)
        foreach (ref readonly var q in list)
        {
            if (!FoundryWorks.Open(list, q))
                continue;
            var mid = FoundryWorks.Middle(g, q);
            first = MathF.Min(first, mid.X);
            var lip = FoundryWorks.Lip(q.N, mid.X, mid.Y, g.Floor, 1f);
            if (lip.Z > line + w) // (behind another square's front: a spur out to the line)
                Trough(gfx, frame, new Vector2(mid.X, line), new Vector2(mid.X, lip.Z + 0.2f), y0, y1, w, alongX: false);
            if (!working)
                continue;
            FoundryWorks.TiltShare(now, q.N, q.X0, q.Z0, out var pouring);
            if (pouring)
            {
                lit = MathF.Min(lit, mid.X);
            }
        }
        if (first == float.MaxValue)
            return; // (no crucible has a way to the front: covered launders)
        Trough(gfx, frame, new Vector2(first - w - 0.3f, line), new Vector2(ex + w, line), y0, y1, w, alongX: true);
        // Its trestles, one a cell along it.
        for (var x = first - 0.2f; x < ex + 0.01f; x += g.Cell)
        {
            Box(gfx, frame, new Vector3(x - 0.05f, g.Floor, line - w - 0.02f), new Vector3(x + 0.05f, y0, line - w + 0.08f), Dark);
            Box(gfx, frame, new Vector3(x - 0.05f, g.Floor, line + w - 0.08f), new Vector3(x + 0.05f, y0, line + w + 0.02f), Dark);
            Box(gfx, frame, new Vector3(x - 0.06f, y0 - 0.1f, line - w - 0.04f), new Vector3(x + 0.06f, y0 - 0.004f, line + w + 0.04f), Steel);
        }
        if (bed)
            Trough(gfx, frame, new Vector2(ex, line + w), new Vector2(ex, ez), y0, y1, w * 0.8f, alongX: false);
        else
        {
            // No Mould Line: it runs into a ladle on the floor past the last square.
            Round(gfx, frame, new Vector3(ex + 0.5f, g.Floor + 0.5f, line), new Vector3(0.45f, 0.5f, 0.45f), Dark);
            Round(gfx, frame, new Vector3(ex + 0.5f, g.Floor + 1.006f, line), new Vector3(0.38f, 0.006f, 0.38f), working ? Ember : Soot);
        }
        if (lit < float.MaxValue)
        {
            var glow = PourColour(melt) * (0.85f + 0.15f * MathF.Sin((float)now * 7f));
            gfx.Draw(Scale(new Vector3(lit - 0.1f, y1 + 0.002f, line - w * 0.5f), new Vector3(ex + w * 0.5f, y1 + 0.014f, line + w * 0.5f)) * frame, glow, mode: 2);
            if (bed)
                gfx.Draw(Scale(new Vector3(ex - w * 0.4f, y1 + 0.002f, MathF.Min(line, ez)), new Vector3(ex + w * 0.4f, y1 + 0.014f, MathF.Max(line, ez))) * frame, glow, mode: 2);
        }
        // The moulds: they fill one after another along each Mould Line as the pours come, in the output's colour, lit while fresh.
        var cycle = MouldEvery * FoundryParts.Moulds.Length + 2f;
        var at = working ? (float)(now % cycle) : 0f;
        foreach (ref readonly var q in list)
            for (var k = 0; k < q.N; k++)
                if (((q.Ends >> k) & 1) != 0)
                {
                    float bx = g.X(q.X0 + q.N), bz = g.Z(q.Z0 + k), by = g.Floor + FoundryWorks.MouldTop;
                    for (var m = 0; m < FoundryParts.Moulds.Length; m++)
                    {
                        var (x0, z0, x1, z1) = FoundryParts.Moulds[m];
                        var since = at - m * MouldEvery;
                        if (!working || since < 0)
                            continue; // (an empty mould: its sand shows)
                        var heat = Math.Clamp(1f - since / 3f, 0f, 1f);
                        var colour = Vector3.Lerp(melt, PourColour(melt), heat);
                        gfx.Draw(Scale(new Vector3(bx + (float)x0 + 0.04f, by + 0.004f, bz + (float)z0 + 0.04f), new Vector3(bx + (float)x1 - 0.04f, by + 0.004f + 0.02f * MathF.Min(1f, since + 0.2f), bz + (float)z1 - 0.04f)) * frame, colour, mode: heat > 0.05f ? 2 : 0);
                    }
                }
    }

    // ---- The hood on four posts over the whole bed, its eaves at the biggest square's n + 3 m, its collared stack ------------------

    private static void Hood(Renderer gfx, Construct c, in Matrix4x4 frame, in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, bool working, float dt)
    {
        var eaves = g.Floor + FoundryWorks.Hood(g, list, out var x0, out var x1, out var z0, out var z1);
        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f, hx = (x1 - x0) * 0.5f, hz = (z1 - z0) * 0.5f;
        const float post = 0.14f;
        // The posts, their base plates and knee braces; the eaves' frame.
        foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
            foreach (var sz in (ReadOnlySpan<float>)[-1f, 1f])
            {
                float px = cx + sx * (hx - post), pz = cz + sz * (hz - post);
                Box(gfx, frame, new Vector3(px - 0.28f, g.Floor, pz - 0.28f), new Vector3(px + 0.28f, g.Floor + 0.05f, pz + 0.28f), Dark); // (its base plate)
                Box(gfx, frame, new Vector3(px - post, g.Floor + 0.05f, pz - post), new Vector3(px + post, eaves, pz + post), Dark); // (a steel post: frames are Base, look.md "Machine look")
                Box(gfx, frame, new Vector3(px - post - 0.02f, g.Floor + 0.05f, pz - post - 0.02f), new Vector3(px + post + 0.02f, g.Floor + 1.2f, pz + post + 0.02f), Yellow); // (its guard, hazard yellow)
                Leg(gfx, frame, new Vector3(px, eaves - 1.2f, pz), new Vector3(px - sx * 1.1f, eaves - 0.1f, pz), 0.05f, Dark);
                Leg(gfx, frame, new Vector3(px, eaves - 1.2f, pz), new Vector3(px, eaves - 0.1f, pz - sz * 1.1f), 0.05f, Dark);
                Box(gfx, frame, new Vector3(px - 0.12f, eaves - 0.5f, pz - 0.12f), new Vector3(px + 0.12f, eaves - 0.3f, pz + 0.12f), Lamp, mode: working ? 2 : 0); // (a lamp under the eaves)
            }
        Box(gfx, frame, new Vector3(x0, eaves - 0.04f, z0), new Vector3(x1, eaves + 0.26f, z0 + 0.24f), Dark);
        Box(gfx, frame, new Vector3(x0, eaves - 0.04f, z1 - 0.24f), new Vector3(x1, eaves + 0.26f, z1), Dark);
        Box(gfx, frame, new Vector3(x0, eaves - 0.04f, z0 + 0.24f), new Vector3(x0 + 0.24f, eaves + 0.26f, z1 - 0.24f), Dark);
        Box(gfx, frame, new Vector3(x1 - 0.24f, eaves - 0.04f, z0 + 0.24f), new Vector3(x1, eaves + 0.26f, z1 - 0.24f), Dark);
        // The hood itself: three courses of plated steel stepping in toward the stack (the top one sooted), a steel batten and a row of bolts
        // along each course's front and back edges, a ridge vent along the top, a hazard fascia on the front eave, a gutter round the eaves.
        var rise = 0.8f + 0.12f * MathF.Max(hx, hz);
        for (var k = 0; k < 3; k++)
        {
            var inset = (k + 0.3f) / 3.3f;
            float ix = 0.2f + (hx - 1f) * inset * 0.75f, iz = 0.2f + (hz - 1f) * inset * 0.75f;
            float ya = eaves + 0.26f + rise * k / 3f, yb = eaves + 0.26f + rise * (k + 1) / 3f;
            float ax = x0 + ix - 0.003f * k, bx = x1 - ix + 0.003f * k, az = z0 + iz - 0.003f * k, bz = z1 - iz + 0.003f * k;
            Box(gfx, frame, new Vector3(ax, ya - 0.004f * k, az), new Vector3(bx, yb, bz), k == 2 ? Soot : Roof, surface: k == 2 ? SootSurface : PlatedSurface);
            foreach (var (ez, sz) in (ReadOnlySpan<(float, float)>)[(az, -1f), (bz, 1f)])
            {
                Box(gfx, frame, new Vector3(ax - 0.02f, yb - 0.1f, ez + sz * 0.004f), new Vector3(bx + 0.02f, yb + 0.003f, ez + sz * 0.03f), Steel, surface: SteelSurface); // (the batten)
                var bolts = Math.Min(MostBolts, (int)((bx - ax) / 0.9f));
                for (var j = 1; j < bolts; j++)
                {
                    var x = ax + (bx - ax) * j / bolts;
                    Box(gfx, frame, new Vector3(x - 0.025f, yb - 0.07f, ez + sz * 0.026f), new Vector3(x + 0.025f, yb - 0.02f, ez + sz * 0.045f), Dark); // (a bolt head)
                }
            }
        }
        {
            // The ridge vent on the top course, along x either side of the stack: a raised cap on dark louvres.
            float ix = 0.2f + (hx - 1f) * (2.3f / 3.3f) * 0.75f, ytop = eaves + 0.26f + rise;
            float vx0 = x0 + ix + 0.3f, vx1 = x1 - ix - 0.3f;
            var gap = 0.4f + 0.04f * MathF.Max(hx, hz) + 0.3f; // (either side of the stack: its radius and a hand's room)
            foreach (var (va, vb) in (ReadOnlySpan<(float, float)>)[(vx0, cx - gap), (cx + gap, vx1)])
                if (vb - va > 0.6f)
                {
                    Box(gfx, frame, new Vector3(va, ytop - 0.004f, cz - 0.22f), new Vector3(vb, ytop + 0.18f, cz + 0.22f), Dark); // (its louvres)
                    Box(gfx, frame, new Vector3(va - 0.04f, ytop + 0.18f, cz - 0.3f), new Vector3(vb + 0.04f, ytop + 0.24f, cz + 0.3f), Steel, surface: SteelSurface); // (its cap)
                }
        }
        Box(gfx, frame, new Vector3(x0 - 0.004f, eaves - 0.36f, z0 - 0.06f), new Vector3(x1 + 0.004f, eaves - 0.04f, z0 - 0.004f), Yellow, surface: HazardSurface); // (the hazard fascia on the front eave)
        Box(gfx, frame, new Vector3(x0 - 0.12f, eaves + 0.18f, z0 - 0.12f), new Vector3(x1 + 0.12f, eaves + 0.3f, z0 + 0.02f), Dark); // (the gutter)
        Box(gfx, frame, new Vector3(x0 - 0.12f, eaves + 0.18f, z1 - 0.02f), new Vector3(x1 + 0.12f, eaves + 0.3f, z1 + 0.12f), Dark);
        // The stack, from the hood's top to 12 m: a collar at its foot, two collars up it, a sooty lip; smoke while it works.
        var hoodTop = eaves + 0.26f + rise;
        var stackTop = MathF.Max(g.Floor + FoundryWorks.StackTop, hoodTop + 2f);
        var sr = 0.4f + 0.04f * MathF.Max(hx, hz);
        Round(gfx, frame, new Vector3(cx, hoodTop + 0.12f, cz), new Vector3(sr + 0.18f, 0.12f, sr + 0.18f), Steel, SteelSurface);
        Round(gfx, frame, new Vector3(cx, (hoodTop + stackTop) * 0.5f, cz), new Vector3(sr, (stackTop - hoodTop) * 0.5f, sr), Body);
        for (var k = 1; k <= 2; k++)
            Round(gfx, frame, new Vector3(cx, hoodTop + (stackTop - hoodTop) * k / 3f, cz), new Vector3(sr + 0.06f, 0.07f, sr + 0.06f), Steel, SteelSurface);
        Round(gfx, frame, new Vector3(cx, stackTop - 0.1f, cz), new Vector3(sr + 0.03f, 0.1f, sr + 0.03f), Soot);
        if (working && Fx.On)
        {
            var at = Vector3.Transform(new Vector3(cx, stackTop + 0.1f, cz), frame) + (gfx.Relative ? gfx.Origin : Vector3.Zero);
            var up = Vector3.TransformNormal(Vector3.UnitY, frame);
            for (var n = Fx.Share(4f * dt * (1f + sr)); n > 0; n--)
                Fx.EngineSmoke(at, c.Vel * 0.6f + up * (1.6f + sr) + new Vector3(0.2f, 0, 0.1f), sr * 1.8f, 4.2f, 0.65f);
        }
    }

    // ---- Helpers ----

    /// <summary>A refractory trough from <paramref name="a"/> to <paramref name="b"/> (x, z), its channel dark, a steel side.</summary>
    private static void Trough(Renderer gfx, in Matrix4x4 frame, Vector2 a, Vector2 b, float y0, float y1, float w, bool alongX)
    {
        var lo = Vector2.Min(a, b);
        var hi = Vector2.Max(a, b);
        var s = alongX ? new Vector2(0, w) : new Vector2(w, 0);
        Box(gfx, frame, new Vector3(lo.X - s.X, y0, lo.Y - s.Y), new Vector3(hi.X + s.X, y1, hi.Y + s.Y), Lining);
        Box(gfx, frame, new Vector3(lo.X - s.X * 0.55f, y1 - 0.004f, lo.Y - s.Y * 0.55f), new Vector3(hi.X + s.X * 0.55f, y1 + 0.001f, hi.Y + s.Y * 0.55f), Ember);
        Box(gfx, frame, new Vector3(lo.X - s.X - 0.02f, y0 + 0.04f, lo.Y - s.Y - 0.02f), new Vector3(hi.X + s.X + 0.02f, y0 + 0.12f, hi.Y + s.Y + 0.02f), Dark); // (its steel band)
    }

    /// <summary>A strut from <paramref name="a"/> to <paramref name="b"/>, <paramref name="half"/> thick.</summary>
    private static void Leg(Renderer gfx, in Matrix4x4 frame, Vector3 a, Vector3 b, float half, Vector3 colour)
    {
        var span = b - a;
        var len = span.Length();
        if (len < 0.01f)
            return;
        var dir = span / len;
        var turn = Vector3.Cross(Vector3.UnitY, dir);
        var q = turn.LengthSquared() < 1e-6f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.Normalize(turn), MathF.Acos(Math.Clamp(dir.Y, -1f, 1f)));
        gfx.Draw(Matrix4x4.CreateScale(half * 2f, len, half * 2f) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation((a + b) * 0.5f) * frame, colour);
    }

    private static Matrix4x4 Cyl(Vector3 centre, Vector3 half) => _cyl.Shape == 0 ? Scale(centre - half, centre + half) : (_cyl with { Half = half }).Model(0, centre);

    private static Matrix4x4 Pin(Vector3 centre, Vector3 half) => _pin.Shape == 0 ? Scale(centre - half, centre + half) : (_pin with { Half = half }).Model(0, centre);

    private static void Round(Renderer gfx, in Matrix4x4 frame, Vector3 centre, Vector3 half, Vector3 colour, float surface = 0) => gfx.Draw(Cyl(centre, half) * frame, colour, shape: _cyl.Shape, surface: surface);

    /// <summary>A course of a crucible's steel shell.</summary>
    private static void Shell(Renderer gfx, in Matrix4x4 frame, Vector3 centre, Vector3 half) => Round(gfx, frame, centre, half, Steel, ShellSurface);

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, in Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour, int mode = 0, float surface = 0) => gfx.Draw(Scale(Vector3.Min(lo, hi), Vector3.Max(lo, hi)) * frame, colour, mode: mode, surface: surface);
}
