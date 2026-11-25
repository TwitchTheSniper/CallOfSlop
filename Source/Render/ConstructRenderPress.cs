using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The swing-lid Press (ConstructPressLid.cs), drawn whole by its Core over its bed's rectangle: the press-lab/BUILD-SPEC.md geometry,
/// in a press frame whose u runs across the bed toward the hinge side (the frame across from the Core), v up from the machine's floor
/// and w along the bed, its origin the middle of the bed's footprint on the floor. The bed's own blocks draw nothing (ConstructPress.Covered)
/// and keep their collision; everything here is drawn only. A press is a hundred or so boxes, a few dozen of them only within
/// <see cref="PressDetail"/> metres. Allocates nothing.
/// </summary>
public static partial class ConstructRender
{
    private const float PressDetail = 45f; // (m: nearer, the hinge's cheeks, the hole liners, the jaws' screws and the die's marks are drawn too)

    private static readonly Vector3 PrPaint = Hex(0x2f5f8f), PrBase = Hex(0x22262b), PrDie = Hex(0x2f363d), PrSteel = Hex(0x98a1a9),
        PrDark = Hex(0x1d1f22), PrAccent = Hex(0xf2b01e), PrChrome = Hex(0xd3d8dc), PrOil = Hex(0x3a3f46);

    private static Vector3 Hex(int rgb) => new((rgb >> 16 & 0xFF) / 255f, (rgb >> 8 & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    private static readonly ushort PrCyl = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = 14 });
    private static readonly ushort PrCylFine = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = 24 });
    private static readonly ushort PrEdgeRing = PartShapes.For(new PartBox { Shape = "tube", Size = Vector3.One, Inner = 0.9f, Segments = 24 });
    private static readonly ushort PrCornerRing = PartShapes.For(new PartBox { Shape = "tube", Size = Vector3.One, Inner = 0.7f, Segments = 24 });
    private static float _prSteelS = -1, _prPaintS, _prHazS, _prChromeS, _prTreadS;

    /// <summary>The die's job, from its family: what shape its lower die and its tool take.</summary>
    private enum PressJob : byte { Draw, Bend, Blank, Ribs }

    private static PressJob JobOf(PressDie? die) => die?.Family switch
    {
        "Housing" or "Tank" or "Cylinder" or "Piston" or "Engine" or "Barrel" => PressJob.Draw,
        "Bracket" or "Arm" or "Beam" or "Spring" or "Shaft" => PressJob.Bend,
        "Gear" or "Button" or "Board" or "Control" or "Drive" => PressJob.Blank,
        _ => PressJob.Ribs,
    };

    /// <summary>Boxes in one frame, counted.</summary>
    private ref struct PressPen
    {
        public Renderer G;
        public float Shade, Opacity;
        public int N;

        public void Box(in Matrix4x4 m, float x0, float y0, float z0, float x1, float y1, float z1, Vector3 col, float surface = 0)
        {
            var size = new Vector3(MathF.Max(1e-4f, x1 - x0), MathF.Max(1e-4f, y1 - y0), MathF.Max(1e-4f, z1 - z0));
            G.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f) * m, col * Shade, Opacity, mode: 0, surface: surface);
            N++;
        }

        /// <summary>A shape (cylinder, tube: along its box's z) of this diameter and length, its axis along u (0), v (1) or w (2).</summary>
        public void Cyl(in Matrix4x4 m, int axis, Vector3 centre, float diameter, float length, Vector3 col, ushort shape, float surface = 0)
        {
            var turn = axis switch
            {
                0 => new Matrix4x4(0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1),
                1 => new Matrix4x4(1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1),
                _ => Matrix4x4.Identity,
            };
            G.Draw(Matrix4x4.CreateScale(diameter, diameter, length) * turn * Matrix4x4.CreateTranslation(centre) * m, col * Shade, Opacity, mode: 0, shape: shape, surface: surface);
            N++;
        }

        /// <summary>A rod between two points (a strut's barrel or rod).</summary>
        public void Rod(in Matrix4x4 m, Vector3 a, Vector3 b, float diameter, Vector3 col, float surface = 0)
        {
            var d = b - a;
            var len = d.Length();
            if (len < 1e-3f)
                return;
            var z = d / len;
            var x = Vector3.Normalize(Vector3.Cross(MathF.Abs(z.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, z));
            var y = Vector3.Cross(z, x);
            var at = (a + b) * 0.5f;
            G.Draw(Matrix4x4.CreateScale(diameter, diameter, len) * new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, at.X, at.Y, at.Z, 1) * m, col * Shade, Opacity, mode: 0, shape: PrCyl, surface: surface);
            N++;
        }

        /// <summary>A box with a rectangular pit cut down its middle (half sizes <paramref name="px"/>, <paramref name="pz"/>): four boxes.</summary>
        public void Ring(in Matrix4x4 m, float x0, float y0, float z0, float x1, float y1, float z1, float px, float pz, Vector3 col, float surface = 0)
        {
            px = Math.Clamp(px, x0 + 0.01f, x1 - 0.01f);
            pz = Math.Clamp(pz, z0 + 0.01f, z1 - 0.01f);
            Box(m, x0, y0, z0, -px, y1, z1, col, surface);
            Box(m, px, y0, z0, x1, y1, z1, col, surface);
            Box(m, -px, y0, z0, px, y1, -pz, col, surface);
            Box(m, -px, y0, pz, px, y1, z1, col, surface);
        }
    }

    /// <summary>A Press Core's press over its bed (nothing for a Core alone): its frames, bed, dies, jaws, lid and the piece on the die. Boxes drawn.</summary>
    private static int DrawPressLid(Renderer gfx, Construct c, int core, CStore st, bool working, in Matrix4x4 onBody, float shade, float opacity)
    {
        if (!MachineBlocks.TryOf(c, core, out var asm) || asm.W == 0 || asm.Core != core)
            return 0;
        if (_prSteelS < 0)
            (_prSteelS, _prPaintS, _prHazS, _prChromeS, _prTreadS) = (Surfaces.Code(Surfaces.Id("steel"), 0.2f), Surfaces.Code(Surfaces.Id("painted steel"), 0.25f),
                Surfaces.Code(Surfaces.Id("hazard paint"), 0.3f), Surfaces.Code(Surfaces.Id("chrome"), 0.05f), Surfaces.Code(Surfaces.Id("tread plate"), 0.3f));
        ref readonly var p = ref c.Parts[core];
        var d = p.D;
        var cell = d.BlockCell;
        var s = ConstructPress.HingeSide(asm);
        var outW = -s; // (the out end's way along w: w = -s x of the Core's cell frame, the flow its +x)
        var org = new Vector3((asm.X0 + (asm.W - 1) * 0.5f) * cell, -0.5f * cell, (asm.Z0 + (asm.L - 1) * 0.5f) * cell);
        // Press frame (u, v, w) = (s z, y, -s x) of the Core's cell frame: right-handed whichever side the hinge is.
        var cellFrame = new Matrix4x4(0, 0, s, 0, 0, 1, 0, 0, -s, 0, 0, 0, org.X, org.Y, org.Z, 1);
        var F = cellFrame * Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(MachineBlocks.CoreMid(c, core)) * onBody;
        var eye = gfx.CameraPosition - (gfx.Relative ? gfx.Origin : Vector3.Zero);
        var detail = Vector3.DistanceSquared(Vector3.Transform(Vector3.Zero, F), eye) < PressDetail * PressDetail;
        var pen = new PressPen { G = gfx, Shade = shade, Opacity = opacity };

        // ---- The numbers (press-lab/BUILD-SPEC.md) ----
        float Lh = asm.L * cell * 0.5f, Wh = asm.W * cell * 0.5f;
        var ut = ConstructPress.FrameWidth(asm, cell);
        var wx = MathF.Max(0.3f, Lh - ut); // (the clear half width between the frames)
        var deck = ConstructPress.DeckOf(asm, cell);
        var frameTop = ConstructPress.FrameTop(asm, cell);
        var bolsterTop = deck - 0.35f;
        ConstructPress.LidAt(st, Now, working, out var pose);
        var die = st.StrokeDie ?? ConstructPress.SizedDieOf(PressTable.Current, c, core);
        var job = JobOf(die);
        var piece = pose.Show == ConstructPress.BedShow.Blank ? st.StrokeBlank : pose.Show == ConstructPress.BedShow.Made ? (working ? st.StrokeLast : st.StrokeMade) : null;
        var sized = st.StrokeBlank ?? st.StrokeBlankPrev; // (the die's size: this stroke's blank, else the last)
        Vector3 bs = sized != null ? sized.BoundsMax - sized.BoundsMin : new Vector3(1f, 0.06f, 1f);
        var bedX = MathF.Max(0.2f, 2 * wx - 0.9f);
        float bX = Math.Clamp(bs.Z, 0.2f, bedX), bZ = Math.Clamp(bs.X, 0.2f, MathF.Max(0.2f, 2 * Wh - 0.6f)), tkFull = MathF.Max(0.01f, bs.Y), tk = MathF.Min(tkFull, 0.3f);
        var drawH = Math.Clamp(MathF.Min(0.75f - tk, 0.45f * MathF.Min(bX, bZ)), 0.05f, MathF.Max(0.05f, deck - 0.3f));
        var leaf = MathF.Max(0.1f, MathF.Min(bZ / 2, deck - 0.5f));
        var gearR = MathF.Max(0.08f, MathF.Min(bX, bZ) / 2 - 0.06f);
        var prot = job switch { PressJob.Draw => drawH, PressJob.Bend => leaf, PressJob.Blank => 2.2f * tk, _ => 0.06f };
        float hW = 2 * wx - 0.3f, hL = 2 * Wh - 0.3f;
        float dW = MathF.Min(hW, MathF.Max(bX + 0.6f, 1.4f)), dL = MathF.Min(hL, MathF.Max(bZ + 0.6f, 1.4f));

        // ---- The jaws: from the last blank's size (or fully open after a die swap) to this one's ----
        var prev = st.StrokeBlankPrev;
        float pX = prev != null ? Math.Clamp(prev.BoundsMax.Z - prev.BoundsMin.Z, 0.2f, bedX) : -1, pZ = prev != null ? Math.Clamp(prev.BoundsMax.X - prev.BoundsMin.X, 0.2f, MathF.Max(0.2f, 2 * Wh - 0.6f)) : -1;
        var j = working ? pose.Jaws : 1f;
        float pdW = pX > 0 ? MathF.Min(hW, MathF.Max(pX + 0.6f, 1.4f)) : dW, pdL = pZ > 0 ? MathF.Min(hL, MathF.Max(pZ + 0.6f, 1.4f)) : dL;
        float sx = float.Lerp(pdW / dW, 1, j), sz = float.Lerp(pdL / dL, 1, j);
        var pDH = pX > 0 ? Math.Clamp(MathF.Min(0.75f - tk, 0.45f * MathF.Min(pX, pZ)), 0.05f, MathF.Max(0.05f, deck - 0.3f)) : drawH;
        var sy = job == PressJob.Draw ? float.Lerp(pDH / drawH, 1, j) : job == PressJob.Bend && pZ > 0 ? float.Lerp((MathF.Min(pZ / 2, deck - 0.5f) + 0.2f) / (leaf + 0.2f), 1, j) : 1f;

        // ---- The bed and bolster, open under the die (a pit), and the side frames ----
        float pitX = hW / 2 + 0.004f, pitZ = hL / 2 + 0.004f;
        pen.Box(F, -Lh, 0.003f, -Wh, Lh, 0.05f, Wh, PrPaint, _prPaintS);
        pen.Ring(F, -Lh, 0.05f, -Wh, Lh, bolsterTop - 0.22f, Wh, pitX, pitZ, PrPaint, _prPaintS);
        pen.Ring(F, -wx + 0.06f, bolsterTop - 0.22f, -Wh + 0.06f, wx - 0.06f, bolsterTop - 0.02f, Wh - 0.06f, pitX, pitZ, PrDark);
        pen.Ring(F, -wx + 0.05f, bolsterTop - 0.02f, -Wh + 0.1f, wx - 0.05f, bolsterTop, Wh - 0.1f, pitX, pitZ, PrSteel, _prTreadS);
        for (var side = -1; side <= 1; side += 2)
        {
            float u0 = side < 0 ? -Lh : Lh - ut, u1 = u0 + ut;
            pen.Box(F, u0, 0.003f, -Wh, u1, frameTop, Wh, PrPaint, _prPaintS);
            pen.Box(F, u0 - 0.003f, frameTop - 0.25f, -Wh - 0.003f, u1 + 0.003f, frameTop - 0.12f, Wh + 0.003f, PrAccent, _prHazS);
            if (deck - 0.3f > 0.1f)
                pen.Box(F, u0 - 0.003f, deck - 0.3f, -Wh - 0.003f, u1 + 0.003f, deck - 0.17f, Wh + 0.003f, PrAccent, _prHazS);
            if (frameTop - 0.3f > deck + 0.2f)
                pen.Box(F, side < 0 ? u1 - 0.002f : u0 - 0.04f, deck + 0.05f, -Wh + 0.15f, side < 0 ? u1 + 0.04f : u0 + 0.002f, frameTop - 0.3f, Wh - 0.15f, PrDark); // (the inner face's liner)
        }

        // ---- The lower die (its top on the blank's line, reaching down into the pit) and the jaws' holder and fences ----
        var lower = Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateTranslation(0, deck, 0) * F;
        float e = pose.Eject;
        LowerDie(ref pen, in lower, job, dW, dL, bX, bZ, tk, drawH, leaf, gearR, outW, e, detail);
        var deckM = Matrix4x4.CreateTranslation(0, deck, 0) * F;
        float w2 = MathF.Min(dW * sx / 2, hW / 2 - 0.01f), l2 = MathF.Min(dL * sz / 2, hL / 2 - 0.01f);
        pen.Box(deckM, -hW / 2, -0.35f, -hL / 2, -w2, -0.002f, hL / 2, PrBase); // (the holder: the whole bed's size, its opening the die's)
        pen.Box(deckM, w2, -0.35f, -hL / 2, hW / 2, -0.002f, hL / 2, PrBase);
        pen.Box(deckM, -w2, -0.35f, -hL / 2, w2, -0.002f, -l2, PrBase);
        pen.Box(deckM, -w2, -0.35f, l2, w2, -0.002f, hL / 2, PrBase);
        {
            float fx = float.Lerp(pX > 0 ? pX / 2 + 0.066f : hW / 2 - 0.12f, bX / 2 + 0.066f, j), fz = float.Lerp(pZ > 0 ? pZ / 2 + 0.066f : hL / 2 - 0.12f, bZ / 2 + 0.066f, j);
            fx = MathF.Min(fx, hW / 2 - 0.12f);
            fz = MathF.Min(fz, hL / 2 - 0.12f);
            var fh = MathF.Max(0.03f, tk);
            for (var k = -1; k <= 1; k += 2)
            {
                pen.Box(deckM, k * fx - 0.06f, 0.002f, -fz + 0.004f, k * fx + 0.06f, fh, fz - 0.004f, PrAccent, _prHazS); // (fences across u, then along w: their ends 4 mm apart)
                pen.Box(deckM, -fx + 0.066f, 0.004f, k * fz - 0.06f, fx - 0.066f, fh - 0.003f, k * fz + 0.06f, PrAccent, _prHazS);
                pen.Box(deckM, k * hW / 2 - (k > 0 ? 0.2f : 0), 0.002f, -0.25f, k * hW / 2 + (k < 0 ? 0.2f : 0), 0.22f, 0.25f, PrDark); // (the screws' drive boxes)
                pen.Box(deckM, -0.25f, 0.003f, k * hL / 2 - (k > 0 ? 0.2f : 0), 0.25f, 0.221f, k * hL / 2 + (k < 0 ? 0.2f : 0), PrDark);
                if (detail)
                {
                    var lu = hW / 2 - 0.2f - fx - 0.06f;
                    if (lu > 0.02f)
                        pen.Cyl(deckM, 0, new Vector3(k * (fx + 0.06f + lu / 2), fh / 2 + 0.005f, 0), 0.08f, lu, PrChrome, PrCyl, _prChromeS);
                    var lw = hL / 2 - 0.2f - fz - 0.06f;
                    if (lw > 0.02f)
                        pen.Cyl(deckM, 2, new Vector3(0, fh / 2 + 0.006f, k * (fz + 0.06f + lw / 2)), 0.08f, lw, PrChrome, PrCyl, _prChromeS);
                }
            }
        }

        // ---- The hinge: a pedestal on the hinge side's frame that rises with the die, cheek plates either side of each knuckle ----
        const float Pin = 0.25f, BodyEnd = -0.38f, LidT = 0.55f;
        var hingeU = Lh - ut / 2;
        var lidW = ConstructPress.LidWidth(asm, cell);
        var hingeY = MathF.Max(deck + tkFull + prot + 0.36f, frameTop + prot + 0.004f); // (shut, the lid's underside; squeezed it comes down by the tool's reach, never under the frames' top)
        var squeezed = hingeY - prot;
        var holderD = MathF.Max(0.3f, squeezed - deck - tkFull - 0.056f); // (the upper die's holder: deep enough that its tool stands 6 cm over the blank when shut)
        var pTop = squeezed - 0.03f;
        var cheekFoot = MathF.Max(pTop, frameTop);
        if (pTop > frameTop + 0.01f)
        {
            pen.Box(F, Lh - ut, frameTop - 0.002f, -Wh + 0.002f, Lh - 0.002f, pTop, Wh - 0.002f, PrPaint, _prPaintS);
            pen.Box(F, Lh - ut - 0.003f, pTop - 0.06f, -Wh - 0.001f, Lh + 0.001f, pTop, Wh + 0.001f, PrBase);
        }
        var pinY = hingeY + Pin;
        if (detail)
            for (var kz = -Wh + 0.6f; kz < Wh; kz += 1.6f)
                for (var side = -1; side <= 1; side += 2)
                {
                    var cz = kz + side * 0.42f;
                    if (MathF.Abs(cz) >= Wh - 0.04f)
                        continue;
                    // (outboard of the pin only, below the open lid's sweep; its back corner stepped like the lab's 45 degree mitre; a dark pin slot on each face)
                    pen.Box(F, hingeU - 0.13f, cheekFoot, cz - 0.035f, Lh - 0.02f, pinY - 0.06f, cz + 0.035f, PrPaint, _prPaintS);
                    pen.Box(F, hingeU - 0.13f, pinY - 0.06f, cz - 0.033f, hingeU + 0.1f, pinY + 0.2f, cz + 0.033f, PrPaint, _prPaintS);
                    pen.Box(F, hingeU - 0.09f, cheekFoot + 0.03f, cz - 0.038f, hingeU + 0.09f, pinY + 0.1f, cz + 0.038f, PrDark);
                }

        // ---- The lid: turned about its pin, shut it lies across both frames; it squeezes down by the tool's reach ----
        var drop = (prot + 0.004f) * pose.Drop;
        var a = pose.Angle;
        float ca = MathF.Cos(a), sa = MathF.Sin(a);
        var lidM = Matrix4x4.CreateTranslation(0, -Pin, 0) * new Matrix4x4(ca, -sa, 0, 0, sa, ca, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1) * Matrix4x4.CreateTranslation(hingeU, pinY - drop, 0) * F;
        var latchW = MathF.Max(0.3f, Wh - 0.8f);
        float hx = -Lh + ut / 2 - hingeU, hr = 0.2f, hd = 0.26f;
        LidHoled(ref pen, in lidM, -lidW, 0, -Wh + 0.05f, BodyEnd, LidT, Wh - 0.05f, hx, hr, hd, latchW, PrPaint, _prPaintS);
        LidHoled(ref pen, in lidM, -lidW + 0.06f, LidT, -Wh + 0.3f, BodyEnd - 0.06f, LidT + 0.25f, Wh - 0.3f, hx, hr, hd, latchW, PrBase, 0);
        for (var i = 0; i <= asm.W; i++)
        {
            var rz = -Wh + 0.3f + (2 * Wh - 0.6f) * i / asm.W;
            LidHoled(ref pen, in lidM, -lidW + 0.064f, LidT + 0.004f, rz - 0.06f, BodyEnd - 0.064f, LidT + 0.45f, rz + 0.06f, hx, hr, hd, latchW, PrPaint, _prPaintS); // (a rib, one a bed cell and one more)
        }
        if (detail)
            for (var k = -1; k <= 1; k += 2)
            {
                var c0 = k * latchW;
                pen.Box(lidM, hx - hr, 0.002f, c0 - hd, hx + hr, LidT + 0.25f, c0 - hd + 0.02f, PrAccent, _prHazS); // (the holes' bright liners)
                pen.Box(lidM, hx - hr, 0.002f, c0 + hd - 0.02f, hx + hr, LidT + 0.25f, c0 + hd, PrAccent, _prHazS);
                pen.Box(lidM, hx - hr, 0.003f, c0 - hd + 0.02f, hx - hr + 0.02f, LidT + 0.249f, c0 + hd - 0.02f, PrAccent, _prHazS);
                pen.Box(lidM, hx + hr - 0.02f, 0.003f, c0 - hd + 0.02f, hx + hr, LidT + 0.249f, c0 + hd - 0.02f, PrAccent, _prHazS);
            }
        for (var k = -1; k <= 1; k += 2)
        {
            pen.Box(lidM, -1.38f, 0.12f, k < 0 ? -Wh - 0.3f : Wh - 0.05f, -1.02f, 0.5f, k < 0 ? -Wh + 0.05f : Wh + 0.3f, PrPaint, _prPaintS); // (the struts' lugs, past the lid's ends)
            pen.Cyl(lidM, 2, new Vector3(-1.2f, Pin + 0.15f, k * (Wh + 0.2f)), 0.14f, 0.34f, PrChrome, PrCyl, _prChromeS);
        }
        pen.Box(lidM, -lidW - 0.003f, 0.1f, -Wh + 0.047f, -lidW + 0.3f, 0.25f, Wh - 0.047f, PrAccent, _prHazS);
        for (var kz = -Wh + 0.6f; kz < Wh; kz += 1.6f)
        {
            pen.Cyl(lidM, 2, new Vector3(0, Pin, kz), 0.42f, 0.7f, PrDark, PrCyl); // (a hinge knuckle)
            pen.Box(lidM, BodyEnd - 0.02f, 0.06f, kz - 0.3f, -0.05f, Pin + 0.12f, kz + 0.3f, PrPaint, _prPaintS); // (its arm to the lid's body)
        }
        pen.Cyl(lidM, 2, new Vector3(0, Pin, 0), 0.16f, 2 * Wh - 0.1f, PrChrome, PrCyl, _prChromeS); // (the hinge pin)
        var upper = Matrix4x4.CreateTranslation(-hingeU, 0, 0) * lidM;
        UpperDie(ref pen, in upper, job, dW, dL, bX, bZ, tk, prot, holderD, gearR, outW);

        // ---- The struts, outside the frame's ends, from a foot on the hinge side to the lid's lugs ----
        for (var k = -1; k <= 1; k += 2)
        {
            var z = k * (Wh + 0.2f);
            var foot = new Vector3(Lh + 0.35f, 0.25f, z);
            var lug = new Vector3(hingeU + (-1.2f * ca + 0.15f * sa), pinY - drop + (1.2f * sa + 0.15f * ca), z);
            var mid = Vector3.Lerp(foot, lug, 0.5f);
            pen.Rod(F, foot, mid, 0.32f, PrOil);
            pen.Rod(F, mid, lug, 0.16f, PrChrome, _prChromeS);
        }

        // ---- The latches on the Core's frame: posts up through the lid's holes, hooks that swing over its top and ride down with it ----
        for (var k = -1; k <= 1; k += 2)
        {
            var lu = -Lh + ut / 2;
            var gy = hingeY + LidT + 0.3f;
            pen.Box(F, lu - 0.15f, frameTop - 0.002f, k * latchW - 0.2f, lu + 0.15f, gy, k * latchW + 0.2f, PrBase);
            var ha = (1 - pose.Latch) * ConstructPress.LatchOpen;
            float hc = MathF.Cos(ha), hs = MathF.Sin(ha);
            var hook = new Matrix4x4(hc, hs, 0, 0, -hs, hc, 0, 0, 0, 0, 1, 0, lu, gy - drop, k * latchW, 1) * F;
            pen.Box(hook, 0f, -0.05f, -0.12f, 0.35f, 0.08f, 0.12f, PrAccent, _prHazS);
        }

        // ---- The piece on the die: the blank, then the part (a drawn pan sunk in its cavity until the ejector lifts it) ----
        if (piece != null)
        {
            var o = d.BlockOrigin ?? (d.BoundsMin + d.BoundsMax) * 0.5f;
            var foot = (piece.BoundsMin + piece.BoundsMax) * 0.5f;
            foot.Y = piece.BoundsMin.Y;
            var sink = pose.Show == ConstructPress.BedShow.Made && job == PressJob.Draw ? drawH * (1 - e) : 0f;
            var at = o + new Vector3(org.X, org.Y + deck + 0.006f - sink, org.Z) - foot; // (in the Core's part frame: its cell frame's origin, then the bed's middle on the die)
            pen.N += DrawBlank(gfx, piece, 0, at, in p, in onBody, shade, opacity);
        }
        return pen.N;
    }

    /// <summary>A box of the lid with the latch posts' holes cut through it (at u <paramref name="hx"/>, w ±<paramref name="hw"/>): up to five boxes.</summary>
    private static void LidHoled(ref PressPen pen, in Matrix4x4 m, float x0, float y0, float z0, float x1, float y1, float z1, float hx, float hr, float hd, float hw, Vector3 col, float surface)
    {
        if (hx + hr <= x0 || hx - hr >= x1)
        {
            pen.Box(m, x0, y0, z0, x1, y1, z1, col, surface);
            return;
        }
        if (hx - hr > x0)
            pen.Box(m, x0, y0, z0, hx - hr, y1, z1, col, surface);
        if (hx + hr < x1)
            pen.Box(m, hx + hr, y0, z0, x1, y1, z1, col, surface);
        var z = z0;
        for (var k = -1; k <= 1; k += 2)
        {
            var c = k * hw;
            if (c - hd > z)
                pen.Box(m, MathF.Max(x0, hx - hr), y0, z, MathF.Min(x1, hx + hr), y1, MathF.Min(z1, c - hd), col, surface);
            z = MathF.Max(z, c + hd);
        }
        if (z < z1)
            pen.Box(m, MathF.Max(x0, hx - hr), y0, z, MathF.Min(x1, hx + hr), y1, z1, col, surface);
    }

    /// <summary>The lower die, shaped for its job (its top at y 0 of <paramref name="m"/>, the blank's line): a draw cavity, a bending slot, a blanking plate or rib grooves.</summary>
    private static void LowerDie(ref PressPen pen, in Matrix4x4 m, PressJob job, float dW, float dL, float bX, float bZ, float tk, float drawH, float leaf, float gearR, int outW, float eject, bool detail)
    {
        float x0 = -dW / 2, x1 = dW / 2, z0 = -dL / 2, z1 = dL / 2;
        var face = job switch { PressJob.Draw => Hex(0x55708a), PressJob.Bend => Hex(0x7f8a94), PressJob.Blank => Hex(0xa8763a), _ => Hex(0x8c96a0) };
        if (detail)
            pen.Box(m, x0, -0.35f, z0 - 0.01f, x0 + 0.3f, -0.25f, z0, PrAccent, _prHazS); // (a hazard corner: the die's front)
        switch (job)
        {
            case PressJob.Draw:
            {
                float cx = MathF.Min(bX / 2 - 0.1f + 0.004f, x1 - 0.05f), cz = MathF.Min(bZ / 2 - 0.1f + 0.004f, z1 - 0.05f), dep = drawH + tk + 0.01f, bse = -MathF.Max(0.35f, dep + 0.12f);
                cx = MathF.Max(cx, 0.05f);
                cz = MathF.Max(cz, 0.05f);
                pen.Box(m, x0, bse, z0, x1, -dep, z1, PrDie); // (under the cavity)
                pen.Box(m, x0, -dep, z0, -cx, 0, z1, PrDie);
                pen.Box(m, cx, -dep, z0, x1, 0, z1, PrDie);
                pen.Box(m, -cx, -dep, z0, cx, 0, -cz, PrDie);
                pen.Box(m, -cx, -dep, cz, cx, 0, z1, PrDie);
                pen.Box(m, -cx - 0.012f, -dep, -cz, -cx, -0.004f, cz, face, _prSteelS); // (the drawing faces)
                pen.Box(m, cx, -dep, -cz, cx + 0.012f, -0.004f, cz, face, _prSteelS);
                var lift = (dep - tk) * eject; // (the cavity's floor is the ejector's pad)
                pen.Box(m, -cx + 0.002f, -dep - 0.01f + lift, -cz + 0.002f, cx - 0.002f, -dep + lift, cz - 0.002f, face, _prSteelS);
                break;
            }
            case PressJob.Bend:
            {
                // (flat under the near half, a slot under the far half that the leaf is wiped down into; outW says which way is far)
                float sl = leaf + 0.2f, dep = sl + 0.15f, sw = tk + 0.32f;
                BoxW(ref pen, m, x0, -dep, z0, x1, 0, 0, outW, PrDie);
                BoxW(ref pen, m, x0, -dep, 0, x1, -sl, sw, outW, PrDie);
                BoxW(ref pen, m, x0, -dep, sw, x1, 0, z1, outW, PrDie);
                BoxW(ref pen, m, x0 + 0.002f, -sl, -0.012f, x1 - 0.002f, -0.004f, 0, outW, face, _prSteelS);
                BoxW(ref pen, m, x0 + 0.002f, -sl, sw, x1 - 0.002f, -0.004f, sw + 0.012f, outW, face, _prSteelS);
                if (detail)
                    for (var y = -0.25f; y > -sl + 0.05f; y -= 0.25f)
                        BoxW(ref pen, m, x0 + 0.004f, y - 0.012f, sw + 0.012f, x1 - 0.004f, y, sw + 0.016f, outW, PrAccent, _prHazS); // (depth marks)
                break;
            }
            case PressJob.Blank:
            {
                // (a die plate with the gear's hole through it: the plate round a square hole, a ring filling its corners, the cutting edge)
                var h = MathF.Min(gearR + 0.004f, MathF.Min(x1, z1) - 0.03f);
                pen.Ring(m, x0, -0.35f, z0, x1, 0, z1, h, h, PrDie);
                var corner = MathF.Min(2 * h / 0.7f, MathF.Min(x1, z1) * 2 - 0.01f);
                pen.Cyl(m, 1, new Vector3(0, -0.175f, 0), corner, 0.35f - 0.002f, PrDie, PrCornerRing);
                pen.Cyl(m, 1, new Vector3(0, 0.001f, 0), 2 * h / 0.9f, 0.004f, face, PrEdgeRing, _prSteelS);
                break;
            }
            default:
            {
                var xa = x0;
                for (var i = -1; i <= 1; i++)
                {
                    var g = i * bX * 0.28f;
                    pen.Box(m, xa, -0.35f, z0, g - 0.07f, 0, z1, PrDie);
                    pen.Box(m, g - 0.07f, -0.35f, z0, g + 0.07f, -0.08f, z1, face, _prSteelS);
                    xa = g + 0.07f;
                }
                pen.Box(m, xa, -0.35f, z0, x1, 0, z1, PrDie);
                break;
            }
        }
    }

    /// <summary>A box whose w runs the out way (<paramref name="outW"/> -1: mirrored along w).</summary>
    private static void BoxW(ref PressPen pen, in Matrix4x4 m, float x0, float y0, float z0, float x1, float y1, float z1, int outW, Vector3 col, float surface = 0)
    {
        if (outW < 0)
            (z0, z1) = (-z1, -z0);
        pen.Box(m, x0, y0, z0, x1, y1, z1, col, surface);
    }

    /// <summary>The upper die under the lid (<paramref name="m"/>: the lid's frame moved to the bed's middle): its holder plate and the tool that reaches <paramref name="prot"/> under it.</summary>
    private static void UpperDie(ref PressPen pen, in Matrix4x4 m, PressJob job, float dW, float dL, float bX, float bZ, float tk, float prot, float holderD, float gearR, int outW)
    {
        var face = job switch { PressJob.Draw => Hex(0x55708a), PressJob.Bend => Hex(0x7f8a94), PressJob.Blank => Hex(0xa8763a), _ => Hex(0x8c96a0) };
        var y0 = -holderD;
        pen.Box(m, -dW / 2, y0, -dL / 2, dW / 2, -0.002f, dL / 2, PrDie);
        pen.Box(m, -dW / 2 + 0.004f, -0.06f, -dL / 2 - 0.01f, dW / 2 - 0.004f, -0.004f, -dL / 2, PrAccent, _prHazS);
        switch (job)
        {
            case PressJob.Draw:
            {
                float px = MathF.Max(0.04f, bX / 2 - 0.1f - tk - 0.01f), pz = MathF.Max(0.04f, bZ / 2 - 0.1f - tk - 0.01f);
                pen.Box(m, -px, y0 - prot, -pz, px, y0 + 0.002f, pz, face, _prSteelS);
                if (px > 0.05f && pz > 0.05f)
                    pen.Box(m, -px + 0.04f, y0 - prot - 0.01f, -pz + 0.04f, px - 0.04f, y0 - prot, pz - 0.04f, PrDark);
                break;
            }
            case PressJob.Bend:
                BoxW(ref pen, m, -dW / 2 + 0.05f, y0 - prot, tk + 0.006f, dW / 2 - 0.05f, y0 + 0.002f, tk + 0.12f, outW, face, _prSteelS); // (the wiper, just past the fold line)
                BoxW(ref pen, m, -dW / 2 + 0.05f, y0 - prot - 0.02f, tk + 0.006f, dW / 2 - 0.05f, y0 - prot, tk + 0.04f, outW, PrDark);
                break;
            case PressJob.Blank:
                pen.Cyl(m, 1, new Vector3(0, y0 - prot / 2 + 0.001f, 0), 2 * gearR, prot + 0.002f, face, PrCylFine, _prSteelS); // (the gear punch)
                break;
            default:
                for (var i = -1; i <= 1; i++)
                    pen.Box(m, i * bX * 0.28f - 0.05f, y0 - prot, -bZ / 2 + 0.14f, i * bX * 0.28f + 0.05f, y0 + 0.002f, MathF.Max(-bZ / 2 + 0.15f, bZ / 2 - 0.14f), face, _prSteelS); // (rib bars)
                break;
        }
    }
}
