using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What an Assembler draws across its bed (design doc "Assembler on the block system", drawing 2; ConstructAssembling.cs): a runway rail
/// along each long side of the bed, on a leg at each end, over the clear height (the masts set it); a bridge on the
/// runway over each column of places that is building, with a trolley, a hoist and a four-axis arm a place (base turn, shoulder and
/// elbow reaching down, a wrist with a tool that flares while it welds); the part filling in on each working place from the base up, its
/// own boxes cut at a height that rises with the wave; the kit shuttle running from the Core along the bed's trench to each place at the
/// wave's start; the lift platen raising the part at its end; and a lamp on each leg, green at work, amber waiting. A bare Core draws
/// nothing here (its own arm is its part's). Posed from the store's progress byte and busy flag as this peer heard them, so host and
/// client draw the same. Looks only: nothing here collides. At most <see cref="MostDrawn"/> places are drawn building, the rest finished.
/// Drawn within <see cref="DrawNear"/>. Allocates nothing.
/// </summary>
public static class AssemblerRender
{
    public const float DrawNear = 150f;
    public const int MostDrawn = 16; // places drawn filling in (box by box); others show the part finished
    private const int MostBoxes = 400; // boxes of one part drawn filling in
    private static readonly Vector3 Yellow = new(0.94f, 0.76f, 0.1f), Dark = new(0.133f, 0.149f, 0.169f), Steel = new(0.6f, 0.63f, 0.66f),
        Brown = new(0.42f, 0.29f, 0.21f), Cable = new(0.08f, 0.09f, 0.1f), Hot = new(1f, 0.62f, 0.25f), Lamp = new(0.35f, 1f, 0.4f), Amber = new(1f, 0.62f, 0.12f);
    private const float RailH = 0.36f, RailW = 0.24f;

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var family = MachineBlocks.Families.IndexOf("assembler");
        if (family < 0)
            return;
        foreach (var c in world.Bodies)
        {
            if (c.MachineAsmCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (asm.Family != family || asm.W == 0 || asm.Core >= c.PartCount || c.Parts[asm.Core].D.BlockRole != BlockRole.Core)
                    continue;
                if (world.StoreOf(c, asm.Core) is not { } st)
                    continue;
                DrawBed(gfx, world, c, asm, st, ConstructRender.Now);
            }
        }
    }

    private static void DrawBed(Renderer gfx, ConstructWorld world, Construct c, in CMachineAsm asm, CStore st, double now)
    {
        ref readonly var p = ref c.Parts[asm.Core];
        var d = p.D;
        ConstructRender.PartDrawn(c, asm.Core, out var pos, out var rot);
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var fit = ConstructAssembling.FitOf(c, asm.Core);
        var cell = d.BlockCell;
        var half = cell * 0.5f;
        var o = d.BlockOrigin ?? Vector3.Zero;
        var bedTop = o.Y * 2f; // (the Core names its cell's middle at the bed layer's: the bed's top is twice that over the foot)
        float x0 = o.X + asm.X0 * cell - half, x1 = o.X + (asm.X0 + asm.W) * cell - half;
        float z0 = o.Z + asm.Z0 * cell - half, z1 = o.Z + (asm.Z0 + asm.L) * cell - half;
        var railY = bedTop + MathF.Max(fit.Clear, cell); // (the rail's foot: over the clear height)
        var alongX = x1 - x0 >= z1 - z0; // (the runway runs along the bed's longer side; a bridge spans the other)
        var working = !p.Broken && ProductionLines.Working(world, c, asm.Core, st, now);
        var waiting = !working && st.Busy;
        // ---- The runway: a rail just outside each long side, a leg at each end (a cap on the mast where one stands) ----
        const float off = 0.3f;
        float ra = alongX ? x0 - 0.2f : z0 - 0.2f, rb = alongX ? x1 + 0.2f : z1 + 0.2f; // (its ends)
        for (var s = 0; s < 2; s++)
        {
            var side = alongX ? (s == 0 ? z0 - off : z1 + off) : (s == 0 ? x0 - off : x1 + off);
            Rail(gfx, frame, alongX, ra, rb, side, railY);
            for (var e = 0; e < 2; e++)
            {
                var at = e == 0 ? ra + 0.15f : rb - 0.15f;
                var lo = Pt(alongX, at - 0.13f, 0.004f, side - 0.13f);
                var hi = Pt(alongX, at + 0.13f, railY - 0.004f, side + 0.13f);
                Box(gfx, frame, lo, hi, Brown); // (the leg)
                Box(gfx, frame, Pt(alongX, at - 0.2f, 0.004f, side - 0.2f), Pt(alongX, at + 0.2f, 0.05f, side + 0.2f), Dark); // (its foot plate)
                var lampOn = working ? MathF.Sin((float)now * MathF.Tau * 0.8f + s * 2 + e) > -0.4f : waiting;
                var colour = working ? Lamp : Amber;
                Box(gfx, frame, Pt(alongX, at - 0.09f, railY + RailH, side - 0.09f), Pt(alongX, at + 0.09f, railY + RailH + 0.05f, side + 0.09f), Dark);
                gfx.Draw(Scale(Pt(alongX, at - 0.06f, railY + RailH + 0.05f, side - 0.06f), Pt(alongX, at + 0.06f, railY + RailH + 0.19f, side + 0.06f)) * frame,
                    lampOn ? colour : colour * 0.2f, mode: lampOn ? 2 : 0);
            }
        }
        float sa = alongX ? z0 - off : x0 - off, sb = alongX ? z1 + off : x1 + off; // (a bridge's span, rail to rail)
        // ---- What it builds: the head recipe's part at each place a carriage takes ----
        var recipe = ConstructAssembling.RecipeOf(AssemblingTable.Current, p, d);
        var def = recipe?.Output;
        var turned = false;
        var places = def == null ? 0 : fit.Places(def, out turned);
        var atOnce = def == null ? 0 : fit.AtOnce(def);
        var progress = working ? st.Progress / 100f : 0f;
        int fx = 1, fz = 1;
        if (def != null && places > 0)
        {
            fit.Footprint(def, out fx, out fz, out _);
            if (turned)
                (fx, fz) = (fz, fx);
        }
        var across = Math.Max(1, asm.W / fx);
        var lastColumn = -1;
        var n = working ? Math.Min(atOnce, Math.Min(places, MostDrawn)) : 0;
        for (var k = 0; k < n; k++)
        {
            int i = k % across, j = k / across;
            var px = o.X + (asm.X0 - 0.5f + (i + 0.5f) * fx) * cell;
            var pz = o.Z + (asm.Z0 - 0.5f + (j + 0.5f) * fz) * cell;
            var place = new Vector3(px, bedTop, pz);
            var size = def!.BoundsMax - def.BoundsMin;
            var w = turned ? new Vector2(size.Z, size.X) : new Vector2(size.X, size.Z);
            var lift = progress > 0.9f ? Ease((progress - 0.9f) / 0.1f) * 0.12f : 0f;
            // The lift platen under the place, rising at the wave's end.
            Box(gfx, frame, new Vector3(px - 0.42f, bedTop + 0.004f, pz - 0.42f), new Vector3(px + 0.42f, bedTop + 0.018f + lift, pz + 0.42f), Steel);
            // The kit shuttle: from the Core's front along the trench to the place, then back.
            if (progress < 0.14f)
            {
                var u = Ease(progress / 0.14f);
                var sx = Lerp(o.X, px, u);
                var sz = Lerp(o.Z + half * 0.6f, pz, u);
                var trench = alongX ? new Vector3(sx, bedTop, z0 + 0.25f) : new Vector3(x0 + 0.25f, bedTop, sz);
                Box(gfx, frame, trench + new Vector3(-0.22f, 0.006f, -0.16f), trench + new Vector3(0.22f, 0.2f, 0.16f), Yellow);
                Box(gfx, frame, trench + new Vector3(-0.18f, 0.2f, -0.12f), trench + new Vector3(0.18f, 0.3f, 0.12f), Brown); // (its kit tote)
            }
            // The part filling in from its base, and the arm over it.
            var grown = progress < 0.14f ? 0f : Math.Clamp((progress - 0.14f) / 0.76f, 0f, 1f);
            var top = DrawGrowing(gfx, frame, def, place + new Vector3(0, 0.02f + lift, 0), turned, grown);
            var column = alongX ? i : j;
            if (column != lastColumn)
            {
                lastColumn = column;
                Bridge(gfx, frame, alongX, alongX ? px : pz, sa, sb, railY);
            }
            Arm(gfx, frame, place, w, top, railY, now + k * 0.7, grown is > 0f and < 1f);
        }
        if (n == 0)
        {
            // Idle: one bridge parked over the Core end of the bed, its arm stowed up.
            var at = alongX ? x0 + 0.5f : z0 + 0.5f;
            Bridge(gfx, frame, alongX, at, sa, sb, railY);
            var mid = alongX ? new Vector3(at, bedTop, (z0 + z1) * 0.5f) : new Vector3((x0 + x1) * 0.5f, bedTop, at);
            Arm(gfx, frame, mid, new Vector2(1, 1), railY - 1.2f, railY, now, false);
        }
    }

    /// <summary>A rail along x (or z) from <paramref name="a"/> to <paramref name="b"/> at <paramref name="side"/>, its foot at <paramref name="y"/>: an I-beam with a yellow edge.</summary>
    private static void Rail(Renderer gfx, Matrix4x4 frame, bool alongX, float a, float b, float side, float y)
    {
        const float fl = 0.05f, web = 0.06f;
        Box(gfx, frame, Pt(alongX, a, y, side - RailW * 0.5f), Pt(alongX, b, y + fl, side + RailW * 0.5f), Brown);
        Box(gfx, frame, Pt(alongX, a, y + RailH - fl, side - RailW * 0.5f), Pt(alongX, b, y + RailH, side + RailW * 0.5f), Brown);
        Box(gfx, frame, Pt(alongX, a + 0.004f, y + fl, side - web * 0.5f), Pt(alongX, b - 0.004f, y + RailH - fl, side + web * 0.5f), Dark);
        Box(gfx, frame, Pt(alongX, a + 0.004f, y + RailH - 0.004f, side - RailW * 0.5f + 0.004f), Pt(alongX, b - 0.004f, y + RailH + 0.012f, side - RailW * 0.5f + 0.05f), Yellow);
    }

    /// <summary>A bridge across the bed at <paramref name="at"/> (along the runway), from rail to rail, on two end trucks; a cable chain on its side.</summary>
    private static void Bridge(Renderer gfx, Matrix4x4 frame, bool alongX, float at, float sa, float sb, float railY)
    {
        var y = railY + RailH + 0.01f;
        Box(gfx, frame, Pt(alongX, at - 0.16f, y + 0.06f, sa), Pt(alongX, at + 0.16f, y + 0.4f, sb), Yellow); // (the box girder)
        Box(gfx, frame, Pt(alongX, at - 0.13f, y + 0.4f, sa + 0.1f), Pt(alongX, at + 0.13f, y + 0.414f, sb - 0.1f), Dark); // (its walkway grating)
        for (var e = 0; e < 2; e++)
        {
            var s = e == 0 ? sa : sb;
            Box(gfx, frame, Pt(alongX, at - 0.3f, y, s - 0.16f), Pt(alongX, at + 0.3f, y + 0.22f, s + 0.16f), Dark); // (an end truck on the rail)
        }
        Box(gfx, frame, Pt(alongX, at + 0.164f, y + 0.12f, sa + 0.2f), Pt(alongX, at + 0.22f, y + 0.2f, sb - 0.2f), Cable); // (the cable chain)
    }

    /// <summary>
    /// A trolley on the bridge over <paramref name="place"/>, its hoist down to over <paramref name="top"/>, and the four-axis arm: a base
    /// that turns, a shoulder and an elbow reaching down to the part, a wrist turning its tool round the part's footprint as it fills.
    /// </summary>
    private static void Arm(Renderer gfx, Matrix4x4 frame, Vector3 place, Vector2 footprint, float top, float railY, double now, bool welding)
    {
        var y = railY + RailH + 0.01f;
        Box(gfx, frame, place + new Vector3(-0.3f, y - place.Y + 0.4f, -0.3f), place + new Vector3(0.3f, y - place.Y + 0.62f, 0.3f), Brown); // (the trolley)
        var baseY = railY + 0.06f; // (the arm's base hangs under the bridge)
        Box(gfx, frame, place + new Vector3(-0.07f, baseY - place.Y, -0.07f), place + new Vector3(0.07f, y - place.Y + 0.4f, 0.07f), Dark); // (the hoist's drop)
        var turn = (float)(now * 0.9);
        var spread = new Vector2(MathF.Max(0.2f, footprint.X * 0.5f - 0.15f), MathF.Max(0.2f, footprint.Y * 0.5f - 0.15f));
        var tool = place + new Vector3(MathF.Cos(turn) * spread.X, 0, MathF.Sin(turn) * spread.Y);
        tool.Y = MathF.Min(baseY - 0.5f, MathF.Max(place.Y + 0.25f, top + 0.12f));
        var shoulder = place + new Vector3(0, baseY - place.Y - 0.12f, 0);
        Box(gfx, frame, shoulder + new Vector3(-0.16f, -0.08f, -0.16f), shoulder + new Vector3(0.16f, 0.1f, 0.16f), Steel); // (the turning base)
        // The elbow sits halfway, pushed out from the line between shoulder and tool.
        var mid = (shoulder + tool) * 0.5f;
        var outWay = new Vector3(tool.X - place.X, 0, tool.Z - place.Z);
        var len = outWay.Length();
        mid += (len > 0.01f ? outWay / len : Vector3.UnitX) * 0.3f + new Vector3(0, 0.15f, 0);
        Beam(gfx, frame, shoulder, mid, 0.1f, Yellow);
        Beam(gfx, frame, mid, tool + new Vector3(0, 0.1f, 0), 0.08f, Yellow);
        Box(gfx, frame, mid - new Vector3(0.07f), mid + new Vector3(0.07f), Dark); // (the elbow)
        Box(gfx, frame, tool + new Vector3(-0.05f, 0.02f, -0.05f), tool + new Vector3(0.05f, 0.14f, 0.05f), Dark); // (the wrist)
        var flare = welding && MathF.Sin((float)now * 23f) > -0.2f;
        gfx.Draw(Scale(tool + new Vector3(-0.03f, -0.04f, -0.03f), tool + new Vector3(0.03f, 0.02f, 0.03f)) * frame, flare ? Hot : Hot * 0.25f, mode: flare ? 2 : 0);
        if (flare)
            gfx.Draw(Scale(tool + new Vector3(-0.09f, -0.1f, -0.09f), tool + new Vector3(0.09f, -0.06f, 0.09f)) * frame, new Vector3(1f, 0.95f, 0.8f), mode: 2); // (the weld's bright spot)
    }

    /// <summary>The part on its place, its boxes drawn up to <paramref name="grown"/> of its height (box by box, from the base up): the top drawn (m, frame).</summary>
    private static float DrawGrowing(Renderer gfx, Matrix4x4 frame, ConstructPartDef def, Vector3 place, bool turned, float grown)
    {
        var height = def.BoundsMax.Y - def.BoundsMin.Y;
        var cut = def.BoundsMin.Y + height * grown;
        var m = Matrix4x4.CreateTranslation(-(def.BoundsMin.X + def.BoundsMax.X) * 0.5f, -def.BoundsMin.Y, -(def.BoundsMin.Z + def.BoundsMax.Z) * 0.5f)
            * (turned ? Matrix4x4.CreateRotationY(MathF.PI / 2) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(place) * frame;
        var count = Math.Min(def.Boxes.Length, MostBoxes);
        for (var bx = 0; bx < count; bx++)
        {
            ref readonly var box = ref def.Boxes[bx];
            if (box.Hidden || box.Center.Y - box.Half.Y > cut)
                continue;
            gfx.Draw(box.Model(0, box.Center) * m, box.Color, shape: box.Shape);
        }
        return place.Y + height * grown;
    }

    private static Vector3 Pt(bool alongX, float along, float y, float across) => alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);

    private static float Ease(float x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi)
    {
        var a = Vector3.Min(lo, hi);
        var b = Vector3.Max(lo, hi);
        return Matrix4x4.CreateScale(Vector3.Max(b - a, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((a + b) * 0.5f);
    }

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour) => gfx.Draw(Scale(lo, hi) * frame, colour);

    private static void Beam(Renderer gfx, Matrix4x4 frame, Vector3 a, Vector3 b, float thick, Vector3 colour)
    {
        var dir = b - a;
        var len = dir.Length();
        if (len < 0.001f)
            return;
        dir /= len;
        var up = MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
        var x = Vector3.Normalize(Vector3.Cross(up, dir));
        var y = Vector3.Cross(dir, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, dir.X, dir.Y, dir.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(thick, thick, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f) * frame, colour);
    }
}
