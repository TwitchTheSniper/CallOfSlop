using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What an Extruder on Die Blocks draws over its blocks: the direct billet press of the Extruder Lab (Sean approved v12, 2026-11-24;
/// https://claude.ai/artifact/29CuAK9qB1fhuhhkGRUtXF, machine model lab-machines/extruder.js, style "direct", Core "silo"). In the press's
/// frame (x across the die from its middle, y up from the die's foot, z along the flow from the die face; the Core part's frame moved
/// there): a front platen carrying the die plate and a cylinder platen behind, tied by four chrome tie bars; a fixed long container from
/// the front platen back to the ram's rest face, on floor rails, with heater bands; the ram's crosshead and a long push bar, so the ram's
/// head never enters the container; a heated feed throat over the container's back end, hung from the frame, with a sight glass that
/// shows the charge filling and a slide gate under it; the chute to it from the Core's silo wall, aimed at the silo's axis; the butt
/// shear parked over the die; a hot saw on a trolley riding a beam across the line, its posts clear of the parked blade and hood; and a
/// puller carriage on its own rail along the tables' +x side, its hinged arm holding a pad sized to the section against the piece's
/// nose face at the section's mid height, flipping up at the saw, staying up as it runs home, lowering once while the next charge fills.
/// The silo's level glass is lit to the bin's level on every Extruder Core, a lone one too.
/// <para>
/// The step is the store's <see cref="CStore.Blocked"/> byte (<see cref="PressPhase"/>, ConstructExtruderPress.cs) and the time since
/// this peer saw it change (<see cref="CStore.OvenSince"/>), so a client draws what the host does; the ram and the puller follow the
/// real piece's length. Drawn within <see cref="DrawNear"/>, the small pieces within <see cref="DrawFine"/>. Allocates nothing.
/// </para>
/// </summary>
public static class ExtruderRender
{
    public const float DrawNear = 150f, DrawFine = 60f;
    /// <summary>Boxes and cylinders drawn by the last <see cref="Draw"/> (all machines).</summary>
    public static int LastDrawn;
    private static readonly Vector3 Red = new(0.71f, 0.271f, 0.169f), Base = new(0.133f, 0.149f, 0.169f), Steel = new(0.596f, 0.631f, 0.663f),
        Accent = new(0.949f, 0.69f, 0.118f), InC = new(0.925f, 0.922f, 0.894f), OutC = new(0.941f, 0.471f, 0.094f), Ink = new(0.082f, 0.09f, 0.102f),
        Hot = new(0.353f, 0.141f, 0.078f), Glow = new(1f, 0.5f, 0.13f), Graphite = new(0.227f, 0.247f, 0.275f), Chrome = new(0.784f, 0.808f, 0.827f),
        Lamp = new(0.24f, 1f, 0.48f), LampOff = new(0.11f, 0.29f, 0.17f), Heat = new(1f, 0.35f, 0.08f);
    // The press's fixed lengths (the Lab's direct style): container, the gap behind it, the charge, the butt left at the end.
    private const float Lc = 1.6f, Gap = Lc + 0.1f, Charge = Lc - 0.2f, Butt = 0.15f;
    private const float ZF0 = -0.7f; // the front platen's back face (z), where the container meets it
    private const float ZC1 = ZF0, ZC0 = ZC1 - Lc, F0 = ZC0 - Gap; // the container's working part, and the ram bar's tip at rest
    private const float Ls = Lc + Gap + 0.05f; // the push bar: long enough that the ram's head never enters the container
    private const float ZK0 = F0 - 0.08f; // the container's back end: it runs back to the ram's rest face
    private const float ZR1 = F0 - Ls - 0.55f, ZR0 = ZR1 - 0.9f; // the cylinder platen
    private const float ChL = 0.8f, ChH = 1f, ChZ = F0 + 0.05f + Charge * 0.5f; // the feed throat over the container's back end
    private const float SawZ = 0.3f; // the hot saw's line, just past the die face
    private static CPartBox? _cyl;
    private static bool _cylLooked;

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var now = ConstructRender.Now;
        LastDrawn = 0;
        foreach (var c in world.Bodies)
        {
            if (!c.HasExtruder || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var i = 0; i < c.PartCount; i++)
            {
                ref readonly var p = ref c.Parts[i];
                var d = p.D;
                if (!d.IsExtruder || d.ExtPress == null || d.BlockRole != BlockRole.Core || world.StoreOf(c, i) is not { } st)
                    continue;
                ConstructRender.PartDrawn(c, i, out var pos, out var rot);
                var core = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
                var at = pos + (gfx.Relative ? gfx.Origin : Vector3.Zero);
                if (Vector3.DistanceSquared(eye, at) > DrawNear * DrawNear * 4)
                    continue;
                Level(gfx, core, d, st);
                if (p.Broken || !MachineBlocks.TryOf(c, i, out var asm) || asm.W == 0 || asm.Core != i)
                    continue;
                DrawPress(gfx, c, i, asm, st, core, now, eye);
            }
        }
    }

    /// <summary>The silo's level glass, lit to how full the bin is (the Core's frame).</summary>
    private static void Level(Renderer gfx, Matrix4x4 core, ConstructPartDef d, CStore st)
    {
        var full = d.BulkCapacity > 0 ? Math.Clamp(st.Amount / (float)d.BulkCapacity, 0, 1) : 0;
        if (full <= 0.002f)
            return;
        Box(gfx, core, new Vector3(-0.8f, 2.82f, -0.05f), new Vector3(-0.782f, 2.82f + 2.16f * full, 0.05f), Glow * 0.8f, 2);
    }

    private static void DrawPress(Renderer gfx, Construct c, int part, in CMachineAsm asm, CStore st, Matrix4x4 core, double now, Vector3 eye)
    {
        var d = c.Parts[part].D;
        FindCylinder(d);
        // The die: the blocks' rectangle (as ConstructExtruder.Reach finds it), its foot and face; the press frame sits there.
        var cell = d.BlockCell;
        var o = d.BlockOrigin ?? (d.BoundsMin + d.BoundsMax) * 0.5f;
        var size = Vector2.Min(new Vector2(asm.W * cell, asm.L * cell), d.ExtMaxSection);
        var cx = o.X + (asm.X0 + (asm.W - 1) * 0.5f) * cell;
        var cy = o.Y + (asm.Z0 + (asm.L - 1) * 0.5f) * cell;
        var y0 = cy - size.Y * 0.5f;
        var tf = Matrix4x4.CreateTranslation(cx, y0, d.ExtDieAt.Z) * core;
        var mid = Vector3.Transform(new Vector3(0, size.Y * 0.5f, -3f), tf) + (gfx.Relative ? gfx.Origin : Vector3.Zero);
        var fine = Vector3.DistanceSquared(eye, mid) < DrawFine * DrawFine;
        float dieW = size.X, dieH = size.Y;
        // The section: the piece on the die, else the head order's or the settings' (cm), its foot just over the die's foot.
        var product = Product(c, part);
        float secW, secH, len = 0;
        if (product >= 0 && c.Parts[product].D is { IsExtrusion: true } pd)
            (secW, secH, len) = (pd.ExKey.W * 0.01f, pd.ExKey.H * 0.01f, pd.ExKey.L * 0.01f);
        else if (WorkOrders.HeadOf(c.Parts[part], out var order))
            (secW, secH) = (order.Key.W * 0.01f, order.Key.H * 0.01f);
        else
        {
            ConstructExtruder.Section(c, part, out _, out var w, out var h, out _);
            (secW, secH) = (w * 0.01f, h * 0.01f);
        }
        secW = Math.Clamp(secW, 0.05f, dieW);
        secH = Math.Clamp(secH, 0.01f, dieH);
        const float sy0 = ConstructExtruder.FootLift; // (the section's foot over the die's: ConstructExtruder.PieceY with tables)
        // The step, as this peer saw it change.
        var phase = ConstructExtruder.PhaseOf(st);
        if (st.OvenSeen != st.Blocked || st.OvenSince == 0)
            (st.OvenSeen, st.OvenSince) = (st.Blocked, now);
        var age = (float)(now - st.OvenSince);
        var lin = Math.Clamp(age / MathF.Max(0.1f, ConstructExtruder.PhaseSeconds(d, phase)), 0, 1);
        var working = st.Busy && phase == PressPhase.Extrude;
        var cutCm = ConstructExtruder.PressCutCm(c, part);
        var target = MathF.Max(cutCm > 0 ? cutCm * 0.01f : d.ExtMaxLength, 0.5f);
        // ---- The frame: front platen with the die plate, the cylinder platen, the main and side cylinders, four tie bars ----
        float bw2 = dieW * 0.5f + 0.35f, by1 = dieH + 0.35f;
        Box(gfx, tf, new Vector3(-bw2 - 0.3f, 0.004f, ZF0), new Vector3(bw2 + 0.3f, by1 + 0.3f, -0.02f), Red);
        Box(gfx, tf, new Vector3(-dieW * 0.5f - 0.05f, 0.004f, -0.02f), new Vector3(dieW * 0.5f + 0.05f, dieH + 0.05f, 0.002f), working ? Hot * 1.6f : Hot, 2);
        Box(gfx, tf, new Vector3(-secW * 0.5f - 0.02f, sy0 - 0.01f, 0.002f), new Vector3(secW * 0.5f + 0.02f, sy0 + secH + 0.02f, 0.006f), working ? Glow : Glow * 0.45f, 2); // (the aperture, glowing)
        Stripes(gfx, tf, -bw2 - 0.3f, bw2 + 0.3f, by1 + 0.18f, 0.12f, -0.02f, 0.03f);
        Box(gfx, tf, new Vector3(-bw2 - 0.3f, 0.004f, ZR0), new Vector3(bw2 + 0.3f, by1 + 0.3f, ZR1), Red);
        var midY = by1 * 0.5f;
        Cyl(gfx, tf, 2, new Vector3(0, midY, ZR0 - 1f), MathF.Min(2.2f, dieH * 0.8f + 0.8f), 2f, Base);
        for (var sx = -1; sx <= 1; sx += 2)
            Cyl(gfx, tf, 2, new Vector3(sx * bw2 * 0.7f, midY + 0.3f, ZR0 - 0.6f), 0.35f, 1.2f, Steel);
        for (var sx = -1; sx <= 1; sx += 2)
            for (var k = 0; k < 2; k++)
            {
                var ty = k == 0 ? 0.12f : by1 + 0.05f;
                Cyl(gfx, tf, 2, new Vector3(sx * bw2, ty, ZR0 * 0.5f - 0.1f), 0.22f, -ZR0 + 0.2f, Chrome);
                if (fine)
                {
                    Cyl(gfx, tf, 2, new Vector3(sx * bw2, ty, 0.06f), 0.36f, 0.14f, Base); // (its nuts)
                    Cyl(gfx, tf, 2, new Vector3(sx * bw2, ty, ZR0 - 0.06f), 0.36f, 0.14f, Base);
                }
            }
        // ---- The floor rails and the fixed container, back to the ram's rest face ----
        for (var sx = -1; sx <= 1; sx += 2)
        {
            var x = sx * (dieW * 0.5f + 0.05f);
            Box(gfx, tf, new Vector3(x - 0.12f, -0.002f, ZR1), new Vector3(x + 0.12f, 0.004f, ZF0), Ink);
            if (fine)
                Box(gfx, tf, new Vector3(x - 0.04f, 0.004f, ZR1), new Vector3(x + 0.04f, 0.04f, ZF0), Chrome);
        }
        Box(gfx, tf, new Vector3(-dieW * 0.5f - 0.25f, 0.04f, ZK0), new Vector3(dieW * 0.5f + 0.25f, dieH + 0.22f, ZC1 - 0.003f), Graphite);
        var heat = working || phase is PressPhase.Upset or PressPhase.Load ? Heat : Hot * 1.4f;
        for (var k = 0; k < 2; k++)
        {
            var y = 0.04f + (dieH + 0.18f) * (k == 0 ? 0.3f : 0.7f);
            Box(gfx, tf, new Vector3(-dieW * 0.5f - 0.27f, y - 0.06f, ZK0 + 0.1f), new Vector3(dieW * 0.5f + 0.27f, y + 0.06f, ZC1 - 0.1f), heat, 2);
        }
        Box(gfx, tf, new Vector3(-secW * 0.5f - 0.06f, sy0, ZK0 - 0.004f), new Vector3(secW * 0.5f + 0.06f, sy0 + secH + 0.1f, ZK0), Glow * 0.7f, 2); // (the charge glowing at its back end)
        if (fine)
            for (var sx = -1; sx <= 1; sx += 2) // (its shoes)
            {
                float a = sx * (dieW * 0.5f + 0.25f), b = sx * (bw2 + 0.42f);
                Box(gfx, tf, new Vector3(MathF.Min(a, b), dieH * 0.4f, ZK0 + 0.2f), new Vector3(MathF.Max(a, b), dieH * 0.4f + 0.3f, ZC1 - 0.2f), Base);
            }
        // ---- The ram: its crosshead on the tie bars and the long push bar, its tip at f ----
        var f = phase switch
        {
            PressPhase.Upset => F0 + (ZC1 - Charge - F0) * Smooth(lin),
            PressPhase.Extrude => ZC1 - Charge + (Charge - Butt) * Math.Clamp(len / target, 0, 1),
            PressPhase.Saw => ZC1 - Butt,
            PressPhase.Back => ZC1 - Butt + (F0 - ZC1 + Butt) * Smooth(lin),
            _ => F0,
        };
        var head = f - Ls - 0.25f;
        Box(gfx, tf, new Vector3(-bw2 + 0.05f, 0.1f, head - 0.25f), new Vector3(bw2 - 0.05f, by1 - 0.05f, head + 0.25f), Red);
        Box(gfx, tf, new Vector3(-secW * 0.5f, sy0, head + 0.25f), new Vector3(secW * 0.5f, sy0 + secH - 0.02f, f), Chrome);
        if (fine)
            for (var sx = -1; sx <= 1; sx += 2)
                for (var k = 0; k < 2; k++)
                    Cyl(gfx, tf, 2, new Vector3(sx * bw2, k == 0 ? 0.12f : by1 + 0.05f, head), 0.32f, 0.5f, Base); // (its bushes on the tie bars)
        // ---- The feed throat over the container's back end: sight glass, slide gate, hangers; the chute from the silo ----
        float chW = secW * 0.5f + 0.14f, chY = dieH + 0.3f;
        Box(gfx, tf, new Vector3(-chW - 0.08f, chY, ChZ - ChL * 0.5f), new Vector3(chW + 0.08f, chY + ChH, ChZ + ChL * 0.5f), Graphite);
        for (var k = 0; k < 2; k++)
        {
            var y = chY + ChH * (k == 0 ? 0.3f : 0.7f);
            Box(gfx, tf, new Vector3(-chW - 0.1f, y - 0.05f, ChZ - ChL * 0.5f - 0.02f), new Vector3(chW + 0.1f, y + 0.05f, ChZ + ChL * 0.5f + 0.02f), heat, 2);
        }
        Box(gfx, tf, new Vector3(chW + 0.08f, chY + 0.1f, ChZ - 0.12f), new Vector3(chW + 0.12f, chY + ChH - 0.1f, ChZ + 0.12f), Ink); // (the sight glass's frame)
        var lvl = phase switch { PressPhase.Fill => lin, PressPhase.Load => 1 - Smooth(Math.Clamp((lin - 0.2f) / 0.6f, 0, 1)), _ => 0f };
        if (lvl > 0.01f)
            Box(gfx, tf, new Vector3(chW + 0.12f, chY + 0.1f, ChZ - 0.08f), new Vector3(chW + 0.125f, chY + 0.1f + (ChH - 0.2f) * lvl, ChZ + 0.08f), Glow, 2);
        if (fine)
            for (var k = 1; k < 5; k++)
            {
                var y = chY + 0.1f + (ChH - 0.2f) * k / 5;
                Box(gfx, tf, new Vector3(chW + 0.12f, y - 0.01f, ChZ + 0.12f), new Vector3(chW + 0.14f, y + 0.01f, ChZ + 0.2f), InC);
            }
        for (var sz = -1; sz <= 1; sz += 2)
            Box(gfx, tf, new Vector3(-bw2, chY + 0.3f, ChZ + sz * (ChL * 0.5f + 0.06f) - 0.06f), new Vector3(bw2, chY + 0.42f, ChZ + sz * (ChL * 0.5f + 0.06f) + 0.06f), Red);
        var gate = phase == PressPhase.Load ? (2 * chW + 0.2f) * (lin < 0.2f ? Smooth(lin / 0.2f) : lin < 0.85f ? 1 : 1 - Smooth((lin - 0.85f) / 0.15f)) : 0;
        Stripes(gfx, tf, -chW + gate, chW + gate, chY - 0.04f, 0.06f, ChZ - ChL * 0.5f + 0.05f, ChL - 0.1f, flat: true);
        Cyl(gfx, tf, 1, new Vector3(0, chY + ChH + 0.3f, ChZ), 0.32f, 0.6f, Steel);
        Box(gfx, tf, new Vector3(-0.06f, chY + ChH + 0.6f, ChZ - 0.06f), new Vector3(0.06f, chY + ChH + 0.72f, ChZ + 0.06f), working ? Lamp : LampOff, 2);
        Chute(gfx, core, tf, new Vector3(cx, y0, d.ExtDieAt.Z), new Vector3(0, chY + ChH + 0.6f, ChZ));
        // ---- The butt shear, parked over the die's back ----
        const float zB = ZF0 - 0.2f;
        for (var sx = -1; sx <= 1; sx += 2)
            Box(gfx, tf, new Vector3(sx * (bw2 + 0.32f) - 0.08f, 0.004f, zB - 0.14f), new Vector3(sx * (bw2 + 0.32f) + 0.08f, by1 + 1.6f, zB + 0.04f), Base);
        Box(gfx, tf, new Vector3(-bw2 - 0.5f, by1 + 1.6f, zB - 0.2f), new Vector3(bw2 + 0.5f, by1 + 1.75f, zB + 0.1f), Red);
        Box(gfx, tf, new Vector3(-bw2 - 0.24f, by1 + 1f, zB - 0.02f), new Vector3(bw2 + 0.24f, by1 + 1.5f, zB + 0.03f), Chrome);
        Stripes(gfx, tf, -bw2 - 0.24f, bw2 + 0.24f, by1 + 1.5f, 0.2f, zB - 0.03f, 0.07f);
        Cyl(gfx, tf, 1, new Vector3(0, by1 + 1.9f, zB - 0.05f), 0.3f, 0.3f, Base);
        // ---- The hot saw on its beam, and the puller on its rail ----
        Saw(gfx, tf, secW, secH, sy0, phase == PressPhase.Saw ? Math.Clamp(lin * 1.25f, 0, 1) : 1f, fine);
        Puller(gfx, tf, st, dieW, secW, secH, sy0, phase, lin, len, product >= 0, MathF.Max(target, len) + 1.5f, fine);
    }

    /// <summary>The chute from the silo's wall (aimed at its axis) down to the feed throat's top. <paramref name="pressAt"/>: the press frame's origin in the Core's.</summary>
    private static void Chute(Renderer gfx, Matrix4x4 core, Matrix4x4 tf, Vector3 pressAt, Vector3 top)
    {
        var b = pressAt + top; // (the Core's frame; the silo's axis is its y axis)
        var h = new Vector2(b.X, b.Z);
        if (h.LengthSquared() < 1e-4f)
            return;
        h = Vector2.Normalize(h);
        var a = new Vector3(h.X * 0.72f, Math.Clamp(b.Y + 0.9f, 3.2f, 4.6f), h.Y * 0.72f);
        Beam(gfx, core, a, b, 0.34f, 0.24f, Steel, 0);
        Beam(gfx, core, a, b, 0.36f, 0.04f, Base, 0.14f); // (its lid)
    }

    /// <summary>A box from <paramref name="a"/> to <paramref name="b"/>, <paramref name="w"/> wide and <paramref name="h"/> deep, its up as near the frame's y as it goes, raised by <paramref name="lift"/>.</summary>
    private static void Beam(Renderer gfx, Matrix4x4 frame, Vector3 a, Vector3 b, float w, float h, Vector3 colour, float lift)
    {
        var dir = b - a;
        var len = dir.Length();
        if (len < 1e-3f)
            return;
        var z = dir / len;
        var x = Vector3.Cross(Vector3.UnitY, z);
        if (x.LengthSquared() < 1e-6f)
            x = Vector3.UnitX;
        x = Vector3.Normalize(x);
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(w, h, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f + y * lift) * frame, colour);
        LastDrawn++;
    }

    /// <summary>
    /// The hot saw (the Lab's 'drop' saw): posts clear of the parked blade and hood, a beam across the line, a trolley with its wheels on
    /// the beam and the arm hung from it, the blade cutting across the piece. <paramref name="a"/>: 0 to 1 through a cut (in, across, out, back).
    /// </summary>
    private static void Saw(Renderer gfx, Matrix4x4 tf, float secW, float secH, float sy0, float a, bool fine)
    {
        var dia = Math.Clamp(secH * 1.3f + 0.5f, 0.9f, 3.2f);
        float span = secW * 0.5f + dia * 0.5f + 0.35f, beamY = sy0 + secH + dia + 0.5f, px = span + dia * 0.56f + 0.35f, z = SawZ;
        for (var sx = -1; sx <= 1; sx += 2)
            Box(gfx, tf, new Vector3(sx * px - 0.1f, -0.3f, z - 0.12f), new Vector3(sx * px + 0.1f, beamY + 0.3f, z + 0.12f), Base);
        Box(gfx, tf, new Vector3(-px - 0.15f, beamY + 0.3f, z - 0.18f), new Vector3(px + 0.15f, beamY + 0.55f, z + 0.18f), Red);
        if (fine)
            Stripes(gfx, tf, -px, px, beamY + 0.32f, 0.2f, z - 0.21f, 0.03f);
        float yCut = sy0 - 0.05f + dia * 0.5f, yRest = beamY - dia * 0.5f - 0.3f, x, y;
        if (a < 0.15f)
            (x, y) = (span, yRest + (yCut - yRest) * Smooth(a / 0.15f));
        else if (a < 0.6f)
            (x, y) = (span - 2 * span * Smooth((a - 0.15f) / 0.45f), yCut);
        else if (a < 0.75f)
            (x, y) = (-span, yCut + (yRest - yCut) * Smooth((a - 0.6f) / 0.15f));
        else
            (x, y) = (-span + 2 * span * Smooth((a - 0.75f) / 0.25f), yRest);
        // The trolley on the beam, its wheels on the beam's top, the arm down to the blade's hub.
        Box(gfx, tf, new Vector3(x - 0.3f, beamY + 0.05f, z - 0.3f), new Vector3(x + 0.3f, beamY + 0.3f, z - 0.2f), Accent);
        Box(gfx, tf, new Vector3(x - 0.3f, beamY + 0.05f, z + 0.2f), new Vector3(x + 0.3f, beamY + 0.3f, z + 0.3f), Accent);
        Box(gfx, tf, new Vector3(x - 0.3f, beamY - 0.05f, z - 0.3f), new Vector3(x + 0.3f, beamY + 0.05f, z + 0.3f), Accent);
        if (fine)
            for (var sz = -1; sz <= 1; sz += 2)
                for (var sx = -1; sx <= 1; sx += 2)
                    Cyl(gfx, tf, 2, new Vector3(x + sx * 0.17f, beamY + 0.42f, z + sz * 0.24f), 0.16f, 0.06f, Base);
        Box(gfx, tf, new Vector3(x - 0.1f, y, z - 0.08f), new Vector3(x + 0.1f, beamY - 0.05f, z + 0.08f), Base);
        Box(gfx, tf, new Vector3(x - dia * 0.56f, y + 0.02f, z - 0.1f), new Vector3(x + dia * 0.56f, y + dia * 0.56f, z + 0.1f), OutC); // (its hood over the top half)
        var spin = Matrix4x4.CreateRotationZ(a * 60f) * Matrix4x4.CreateTranslation(x, y, z) * tf;
        Cyl(gfx, spin, 2, Vector3.Zero, dia, 0.035f, Chrome);
        if (!fine)
            return;
        for (var k = 0; k < 16; k++)
        {
            var ang = k / 16f * MathF.Tau;
            var tooth = Matrix4x4.CreateScale(0.07f, 0.08f, 0.044f) * Matrix4x4.CreateRotationZ(-ang) * Matrix4x4.CreateTranslation(MathF.Sin(ang) * dia * 0.5f, MathF.Cos(ang) * dia * 0.5f, 0) * spin;
            gfx.Draw(tooth, Base);
            LastDrawn++;
        }
    }

    /// <summary>
    /// The puller: a rail along the tables' +x side and a carriage on it; its arm, hinged at the section's mid height, holds a pad (the
    /// section's size, at most 1.2 x 0.9 m) against the piece's nose. Down while the piece grows; flips up at the saw and stays up running home;
    /// lowers once while the next charge fills.
    /// </summary>
    private static void Puller(Renderer gfx, Matrix4x4 tf, CStore st, float dieW, float secW, float secH, float sy0, PressPhase phase, float lin, float len, bool piece, float railEnd, bool fine)
    {
        const float home = 0.6f;
        var prX = dieW * 0.5f + 0.55f;
        if (piece)
            st.PressNose = MathF.Max(home, len + 0.18f);
        var pz = phase switch
        {
            PressPhase.Extrude or PressPhase.Saw when piece => st.PressNose,
            PressPhase.Back => st.PressNose + (home - st.PressNose) * Smooth(lin),
            _ => home,
        };
        var up = phase switch
        {
            PressPhase.Saw => Smooth(Math.Clamp(lin * 3, 0, 1)),
            PressPhase.Back => 1f,
            PressPhase.Fill => 1 - Smooth(lin),
            _ => 0f,
        };
        Box(gfx, tf, new Vector3(prX - 0.15f, -0.3f, 0.02f), new Vector3(prX + 0.15f, 0.4f, railEnd), Base);
        Box(gfx, tf, new Vector3(prX - 0.05f, 0.4f, 0.02f), new Vector3(prX + 0.05f, 0.48f, railEnd), Chrome);
        var car = Matrix4x4.CreateTranslation(0, 0, pz) * tf;
        Box(gfx, car, new Vector3(prX - 0.3f, 0.48f, -0.4f), new Vector3(prX + 0.3f, 1f, 0.4f), Accent);
        Box(gfx, car, new Vector3(prX - 0.32f, 0.6f, -0.25f), new Vector3(prX + 0.32f, 0.9f, 0.25f), Base);
        Box(gfx, car, new Vector3(prX - 0.29f, 1f, -0.06f), new Vector3(prX - 0.2f, 1.12f, 0.06f), up < 0.5f && phase == PressPhase.Extrude ? Lamp : LampOff, 2);
        // The arm: hinged on the carriage's inner face at the section's mid height, reaching across ahead of the nose.
        float armY = sy0 + secH * 0.5f - 0.07f, hx = prX - 0.3f, aL = hx;
        var arm = Matrix4x4.CreateRotationZ(-1.45f * up) * Matrix4x4.CreateTranslation(hx, armY, 0) * car;
        if (fine)
            Cyl(gfx, arm, 2, new Vector3(0, 0.07f, 0), 0.22f, 0.3f, Base);
        Box(gfx, arm, new Vector3(-aL, 0, -0.12f), new Vector3(0, 0.14f, 0.12f), Accent);
        float pw = MathF.Min(secW, 1.2f) * 0.5f, ph = MathF.Min(secH, 0.9f) * 0.5f, jz = phase == PressPhase.Extrude ? 0 : 0.06f;
        Box(gfx, arm, new Vector3(-aL - pw, 0.07f - ph, -0.18f + jz), new Vector3(-aL + pw, 0.07f + ph, -0.1f + jz), Base); // (the pad, on the nose)
    }

    /// <summary>The part on the Core's die, or -1 (its own links only).</summary>
    private static int Product(Construct c, int part)
    {
        var sock = c.Parts[part].D.ExtDieSocket;
        for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
        {
            ref readonly var l = ref c.Links[e >> 1];
            var mine = (e & 1) == 0 ? l.SA : l.SB;
            var other = c.OtherEnd(e);
            if (mine == sock && other >= 0 && other < c.PartCount)
                return other;
        }
        return -1;
    }

    private static void FindCylinder(ConstructPartDef d)
    {
        if (_cylLooked)
            return;
        _cylLooked = true;
        foreach (var b in d.Boxes)
            if (b.Shape != 0 && MathF.Abs(b.Half.X - b.Half.Z) < 0.01f && b.Half.Y > 1f)
            {
                _cyl = b; // (the silo's body: a "+y" cylinder)
                break;
            }
    }

    private static float Smooth(float t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    /// <summary>A cylinder along <paramref name="axis"/> (0 x, 1 y, 2 z) at <paramref name="centre"/>, <paramref name="dia"/> across and <paramref name="len"/> long.</summary>
    private static void Cyl(Renderer gfx, Matrix4x4 frame, int axis, Vector3 centre, float dia, float len, Vector3 colour, int mode = 0)
    {
        var turn = axis switch { 0 => Matrix4x4.CreateRotationZ(MathF.PI / 2), 2 => Matrix4x4.CreateRotationX(MathF.PI / 2), _ => Matrix4x4.Identity };
        var place = turn * Matrix4x4.CreateTranslation(centre) * frame;
        var size = new Vector3(dia, len, dia);
        if (_cyl is { } cyl)
            gfx.Draw((cyl with { Half = size * 0.5f, Center = Vector3.Zero }).Model(0, Vector3.Zero) * place, colour, mode: mode, shape: cyl.Shape);
        else
            gfx.Draw(Matrix4x4.CreateScale(size) * place, colour, mode: mode);
        LastDrawn++;
    }

    /// <summary>
    /// Hazard stripes along x from <paramref name="a0"/> to <paramref name="a1"/>: yellow and black blocks 0.25 m long, <paramref name="h"/> high,
    /// <paramref name="deep"/> deep from z <paramref name="at"/>; <paramref name="flat"/>: lying flat (h is then their thickness, deep their length along z).
    /// </summary>
    private static void Stripes(Renderer gfx, Matrix4x4 frame, float a0, float a1, float y, float h, float at, float deep, bool flat = false)
    {
        const float step = 0.25f;
        var k = 0;
        for (var a = a0; a < a1 - 0.01f; a += step, k++)
        {
            var b = MathF.Min(a + step, a1);
            gfx.Draw(Scale(new Vector3(a, y, at), new Vector3(b, y + h, at + deep)) * frame, (k & 1) == 0 ? Accent : Ink);
            LastDrawn++;
        }
    }

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour, int mode = 0)
    {
        gfx.Draw(Scale(lo, hi) * frame, colour, mode: mode);
        LastDrawn++;
    }
}
