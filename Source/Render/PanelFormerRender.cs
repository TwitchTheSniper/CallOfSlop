using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Panel Former's bed, drawn across its whole rectangle of Bed Blocks as one stretch former (Sean, 2026-11-24: the Panel Former Lab,
/// https://claude.ai/artifact/WNECeRGzmy6ps8hgPRcNQX). In the bed's own frame (x across, z along the roll toward the Out Table, y up from
/// the foot): roller lanes along both long sides at the roll line, the post zones outside them, both cut through by slots at each side
/// jaw's out-rigger rail; between the lanes the mouth of a closed pocket under the floor (it is as deep as the shape needs, the Lab's
/// cheat), lined with an always-on faint fill and strip lamps (glowing boxes, not real lights); in it a die of 0.3 m columns, each on its
/// own ram, their tops tilted to the head order's panel shape (<see cref="ConstructPanelFormer.Die"/>), rising to stretch the gripped
/// sheet; a changeover between two shapes runs the columns in a travelling wave. Two jaw sets: swing-out end gates on lift cylinders
/// for shapes along the bed, side jaws on thick carriages on out-rigger rails for shapes across it; the set the head order needs waits
/// ready at its sheet's edges, the other stays stowed. The pose is <see cref="ConstructPanelFormer.Pose"/>'s. Allocates nothing.
/// </summary>
public static class PanelFormerRender
{
    public const float DrawNear = 150f;
    private const float Fine = 40f; // within this the rollers, slot linings, the pocket under the floor and the die's tilted faces are drawn
    private static readonly Vector3 Green = new(0.365f, 0.498f, 0.235f), Trim = new(0.275f, 0.376f, 0.176f), Steel = new(0.6f, 0.63f, 0.66f),
        Base = new(0.133f, 0.149f, 0.169f), Accent = new(0.95f, 0.69f, 0.118f), Ink = new(0.082f, 0.09f, 0.102f), Tread = new(0.52f, 0.55f, 0.58f),
        DieSteel = new(0.52f, 0.55f, 0.58f), DieFace = new(0.82f, 0.84f, 0.86f), Cast = new(0.333f, 0.361f, 0.384f), Chrome = new(0.784f, 0.808f, 0.827f),
        Fill = new(0.16f, 0.175f, 0.185f), Lamp = new(1f, 0.953f, 0.839f);
    private const float WorkY = 1f; // the roll line (OutTableParts.WorkY): the lanes' rollers' tops
    private const float Post = 0.3f, LaneW = 0.45f; // each long side's post zone (outside the sheet) and roller lane
    private const float SlotLo = 0.2f, SlotHalf = 0.34f; // the side jaws' slots: their floor and half their width
    private const float Col = 0.3f, Ripple = 0.5f; // the die's columns, and the changeover wave's size (the Lab's 0.55 m x 0.5)
    private const float FaceThick = 0.05f;
    private static readonly float[] _grid = new float[41 * 41]; // the die's lifts at its columns' corners, this frame
    private static readonly float[] _rails = new float[32]; // the out-rigger rails' places along the bed, one side

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        foreach (var c in world.Bodies)
        {
            if (c.MachineAsmCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (asm.Core >= c.PartCount || asm.W == 0)
                    continue;
                var d = c.Parts[asm.Core].D;
                if (!d.IsPanelFormer || d.IsLaserCutter || d.BlockRole != BlockRole.Core)
                    continue;
                var st = world.StoreOf(c, asm.Core);
                DrawBed(gfx, c, asm, st, ConstructRender.Now, Vector3.DistanceSquared(eye, c.WorldCom) < (Fine + c.Radius) * (Fine + c.Radius));
            }
        }
    }

    private static void DrawBed(Renderer gfx, Construct c, in CMachineAsm asm, CStore? st, double now, bool fine)
    {
        var core = asm.Core;
        var d = c.Parts[core].D;
        ConstructRender.PartDrawn(c, core, out var pos, out var rot);
        var coreFrame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var cell = d.BlockCell;
        var o = d.BlockOrigin ?? Vector3.Zero;
        var alongZ = asm.OutSide is 4 or 5;
        var turn = asm.OutSide switch { 4 => 0f, 5 => MathF.PI, 0 => MathF.PI / 2, _ => -MathF.PI / 2 };
        float cx = o.X + (asm.X0 + (asm.W - 1) * 0.5f) * cell, cz = o.Z + (asm.Z0 + (asm.L - 1) * 0.5f) * cell;
        var m = Matrix4x4.CreateRotationY(turn) * Matrix4x4.CreateTranslation(cx, 0, cz) * coreFrame; // (the bed's frame)
        var sg = asm.OutSide is 4 or 0 ? 1f : -1f; // (the die's shape, laid by BedLook, read in the bed's frame)
        float wh = (alongZ ? asm.W : asm.L) * cell * 0.5f, lh = (alongZ ? asm.L : asm.W) * cell * 0.5f;
        var coreAt = Vector3.Transform(new Vector3(o.X - cx, 0, o.Z - cz), Matrix4x4.CreateRotationY(-turn)); // (the Core beside the bed: its rails dodge its base link)
        float laneO = wh - Post, laneI = MathF.Max(0.3f, laneO - LaneW), dieX = laneI - 0.1f, dieZ = lh - 0.2f;

        // ---- The pose: the stroke or changeover, the head order's sheet (the blank, then the formed panel) and which jaws grip it ----
        var pose = st != null ? ConstructPanelFormer.Pose(c, core, st, now) : new ConstructPanelFormer.FormPose { Morph = -1 };
        var die = pose.Die;
        var head = WorkOrders.HeadOf(c.Parts[core], out var order) && order.Name != null && ConstructParts.Find(order.Name) is { } panel ? panel : null;
        float blankL = 0, blankW = 0;
        if (head != null && ConstructPanelFormer.FlatOf(head, out var flat))
            (blankL, blankW) = (MathF.Max(flat.Length, flat.Width), MathF.Min(flat.Length, flat.Width));
        float formedL = die?.Length ?? blankL, formedW = die?.Width ?? blankW;
        var s = pose.Shape;
        float halfL = (blankL + (formedL - blankL) * s) * 0.5f, halfW = (blankW + (formedW - blankW) * s) * 0.5f;
        var thick = die?.Thick ?? 0.04f;
        var side = die?.SideGrip ?? false;
        var hi = die?.High ?? 0;
        var morph = pose.Morph;
        var from = pose.From;
        var deep = MathF.Max(1.5f, MathF.Max(hi, from?.High ?? 0));
        var pitY = WorkY - 0.55f - deep; // (the pocket's floor: under the die's table and rams at rest)
        float Lift(ConstructPanelFormer.DieInfo? info, float u, float v) => info == null ? 0 : info.LiftAt(sg * v, sg * u);
        float EdgeY(float u, float v) => WorkY + 0.004f + thick * 0.5f + Lift(die, u, v) * s * pose.Hold;

        // ---- The bed's body, both long sides: the post zone and the roller lane, cut through by a slot at each out-rigger rail ----
        var rails = Math.Clamp((int)MathF.Round(2 * lh / 2.2f), 2, _rails.Length);
        for (var side2 = -1; side2 <= 1; side2 += 2)
        {
            var sd = (float)side2;
            var n = Rails(lh, rails, MathF.Sign(coreAt.X) == sd && MathF.Abs(coreAt.X) > wh ? coreAt.Z : float.NaN);
            float xo = sd * wh, xp = sd * laneO, xl = sd * laneI;
            Box(gfx, m, Lo(xl, 0.003f, -lh, xo), Hi(xl, SlotLo, lh, xo), Green); // (under the slots, full length)
            var z = -lh;
            for (var k = 0; k <= n; k++)
            {
                var z1 = k < n ? _rails[k] - SlotHalf : lh;
                if (z1 - z > 0.02f)
                {
                    Box(gfx, m, Lo(xp, SlotLo, z, xo), Hi(xp, WorkY - 0.25f, z1, xo), Green); // (the post zone, to its tread)
                    Box(gfx, m, Lo(xl, SlotLo, z, xp), Hi(xl, WorkY - 0.16f, z1, xp), Green); // (the lane's body, under its rollers)
                    Box(gfx, m, Lo(xp, WorkY - 0.25f, z + 0.004f, xo), Hi(xp, WorkY - 0.236f, z1 - 0.004f, xo), Tread); // (the post zone's tread)
                    var gx = sd * (laneO + 0.02f);
                    Box(gfx, m, Lo(gx - 0.08f, WorkY - 0.234f, z + 0.03f, gx + 0.08f), Hi(gx - 0.08f, WorkY - 0.222f, z1 - 0.03f, gx + 0.08f), Ink); // (the gate rod's way)
                    if (fine)
                        for (var r = z + 0.125f; r < z1 - 0.06f; r += 0.25f) // (the lane's rollers)
                            Cyl(gfx, m, 0, new Vector3((xp + xl) * 0.5f, WorkY - 0.06f, r), 0.12f, MathF.Abs(xp - xl) - 0.12f, Steel);
                    else
                        Box(gfx, m, Lo(xl + sd * 0.06f, WorkY - 0.12f, z, xp - sd * 0.06f), Hi(xl + sd * 0.06f, WorkY - 0.004f, z1, xp - sd * 0.06f), Steel);
                    for (var q = 0; q < 2; q++)
                        Box(gfx, m, new Vector3((q == 0 ? xp : xl) - 0.03f, WorkY - 0.16f, z + 0.004f), new Vector3((q == 0 ? xp : xl) + 0.03f, WorkY - 0.015f, z1 - 0.004f), Base); // (the lane's roller frames)
                    // The lane's inner face, the pocket's mouth: a hazard lip, the faint fill and two strip lamps (glowing, no real light).
                    var fx = sd * (laneI - 0.004f);
                    Box(gfx, m, Lo(fx, WorkY - 0.28f, z, xl), Hi(fx, WorkY - 0.18f, z1, xl), Accent);
                    Glow(gfx, m, Lo(fx, 0.006f, z, xl), Hi(fx, WorkY - 0.284f, z1, xl), Fill);
                    for (var q = 0; q < 2; q++)
                    {
                        var ly = q == 0 ? 0.58f : 0.12f;
                        Box(gfx, m, Lo(sd * (laneI - 0.06f), ly - 0.04f, z + 0.1f, fx), Hi(sd * (laneI - 0.06f), ly + 0.06f, z1 - 0.1f, fx), Base);
                        Glow(gfx, m, Lo(sd * (laneI - 0.066f), ly - 0.02f, z + 0.14f, sd * (laneI - 0.06f)), Hi(sd * (laneI - 0.066f), ly + 0.04f, z1 - 0.14f, sd * (laneI - 0.06f)), Lamp);
                    }
                    Box(gfx, m, Lo(xo, WorkY - 0.4f, z + 0.02f, xo + sd * 0.012f), Hi(xo, WorkY - 0.3f, z1 - 0.02f, xo + sd * 0.012f), Accent); // (the outer hazard band)
                }
                if (k == n)
                    break;
                var zs = _rails[k];
                z = zs + SlotHalf;
                // The slot: dark lining on its floor and walls, hazard over its mouth on the outer face.
                if (fine)
                {
                    Box(gfx, m, Lo(xl, SlotLo - 0.012f, zs - SlotHalf, xo), Hi(xl, SlotLo, zs + SlotHalf, xo), Ink);
                    for (var e = -1f; e <= 1f; e += 2f)
                        Box(gfx, m, Lo(xl, SlotLo, zs + e * SlotHalf - 0.006f, xo), Hi(xl, WorkY - 0.25f, zs + e * SlotHalf + 0.006f, xo), Ink);
                }
                // Its out-rigger rail: from the lane's inner edge out past the bed's side, a leg and foot at its outer end, an end stop.
                float a = sd * laneI, b = sd * (wh + 0.66f);
                Box(gfx, m, Lo(a, SlotLo, zs - 0.22f, b), Hi(a, SlotLo + 0.07f, zs + 0.22f, b), Base);
                for (var dz = -0.15f; dz < 0.2f; dz += 0.3f)
                    Box(gfx, m, Lo(a, SlotLo + 0.07f, zs + dz - 0.03f, b), Hi(a, SlotLo + 0.1f, zs + dz + 0.03f, b), Steel);
                Box(gfx, m, Lo(b - sd * 0.1f, 0.006f, zs - 0.18f, b - sd * 0.02f), Hi(b - sd * 0.1f, SlotLo - 0.003f, zs + 0.18f, b - sd * 0.02f), Base);
                Box(gfx, m, Lo(b - sd * 0.16f, 0.006f, zs - 0.24f, b), Hi(b - sd * 0.16f, 0.036f, zs + 0.24f, b), Base);
                Box(gfx, m, Lo(b - sd * 0.06f, SlotLo + 0.1f, zs - 0.22f, b), Hi(b - sd * 0.06f, SlotLo + 0.2f, zs + 0.22f, b), Accent);
            }
        }

        // ---- The pocket under the floor: a closed steel box (the ground hides what is under it on bare ground), its fill and lamps ----
        if (fine)
        {
            float px = laneI - 0.004f, t = 0.06f;
            Glow(gfx, m, new Vector3(-px - t, pitY - 0.2f, -lh - t), new Vector3(px + t, pitY, lh + t), Fill);
            for (var sd = -1f; sd <= 1f; sd += 2f)
            {
                Glow(gfx, m, Lo(sd * px, pitY, -lh - t, sd * (px + t)), Hi(sd * px, -0.003f, lh + t, sd * (px + t)), Fill);
                Glow(gfx, m, new Vector3(-px, pitY, sd > 0 ? lh : -lh - t), new Vector3(px, -0.003f, sd > 0 ? lh + t : -lh), Fill);
                for (var y = -1f; y > pitY + 0.2f; y -= 1f)
                    Box(gfx, m, Lo(sd * (px - 0.02f), y - 0.04f, -lh, sd * px), Hi(sd * (px - 0.02f), y, lh, sd * px), Base); // (ribs every metre)
                var low = MathF.Max(pitY + 0.4f, -1.6f);
                Glow(gfx, m, Lo(sd * (px - 0.075f), low - 0.05f, -lh + 0.35f, sd * (px - 0.07f)), Hi(sd * (px - 0.075f), low + 0.01f, lh - 0.35f, sd * (px - 0.07f)), Lamp);
                for (var x = -px * 0.5f; x <= px * 0.5f + 0.001f; x += MathF.Max(0.01f, px))
                    Glow(gfx, m, new Vector3(x - 0.15f, -0.2f, sd * (lh - 0.075f) - 0.003f), new Vector3(x + 0.15f, -0.1f, sd * (lh - 0.075f) + 0.003f), Lamp);
            }
        }

        // ---- The die: a table on its rams, a bed of 0.3 m columns on it, their tops the shape (in a changeover, a travelling wave) ----
        var restEnds = WorkY - ConstructPanelFormer.DieBelow - hi - (morph >= 0 ? 0.3f * MathF.Sin(MathF.PI * morph) : 0);
        var ends = restEnds + pose.Rise * (WorkY - restEnds);
        var baseY = MathF.Min(ends, restEnds) - 0.15f;
        var tableY = ends - 0.15f;
        if (fine)
            for (var k = 0; k < Math.Max(1, (int)(2 * lh / 2)); k++)
                Cyl(gfx, m, 1, new Vector3(0, (pitY + tableY - 0.1f) * 0.5f, -lh + 1 + 2 * k), MathF.Min(0.6f, dieX * 0.8f), MathF.Max(0.05f, tableY - 0.1f - pitY), Chrome);
        Box(gfx, m, new Vector3(-dieX + 0.02f, tableY - 0.1f, -dieZ + 0.1f), new Vector3(dieX - 0.02f, tableY, dieZ - 0.1f), Cast);
        var most = fine ? 40 : 10;
        int nx = Math.Clamp((int)MathF.Round(2 * dieX / Col), 1, most), nz = Math.Clamp((int)MathF.Round(2 * dieZ / Col), 1, most);
        float cw = 2 * dieX / nx, cl = 2 * dieZ / nz;
        var blend = morph >= 0 ? Smooth(Math.Clamp((morph - 0.25f) / 0.5f, 0, 1)) : 1;
        var row = nz + 1;
        for (var i = 0; i <= nx; i++)
            for (var j = 0; j <= nz; j++)
            {
                float u = -dieX + i * cw, v = -dieZ + j * cl;
                _grid[i * row + j] = morph >= 0 ? Lift(from, u, v) * (1 - blend) + Lift(die, u, v) * blend : Lift(die, u, v);
            }
        var active = head != null && pose.Stage is >= 1 and <= 4;
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < nz; j++)
            {
                float u0 = -dieX + i * cw, v0 = -dieZ + j * cl, mu = u0 + cw * 0.5f, mv = v0 + cl * 0.5f;
                // Under the sheet it rises with the stroke; past the sheet's edge it stays down (the die is only as big as the sheet needs).
                var under = !active || MathF.Abs(mu) <= halfW + cw * 0.5f && MathF.Abs(mv) <= halfL + cl * 0.5f;
                var at = under ? ends : restEnds;
                if (morph >= 0)
                    at += 0.55f * Ripple * MathF.Sin(MathF.PI * morph) * MathF.Sin(2 * MathF.PI * 2.5f * morph - mv * 1.6f - mu * 0.9f); // (each column steps whole)
                float h00 = _grid[i * row + j] + at, h10 = _grid[(i + 1) * row + j] + at, h01 = _grid[i * row + j + 1] + at, h11 = _grid[(i + 1) * row + j + 1] + at;
                float lo = MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11)), top = MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
                float x0 = u0 + 0.008f, x1 = u0 + cw - 0.008f, z0 = v0 + 0.008f, z1 = v0 + cl - 0.008f;
                if (!fine)
                {
                    Box(gfx, m, new Vector3(x0, tableY, z0), new Vector3(x1, (lo + top) * 0.5f, z1), DieSteel);
                    continue;
                }
                Box(gfx, m, new Vector3(x0, MathF.Min(tableY, lo - FaceThick - 0.01f), z0), new Vector3(x1, lo - FaceThick, z1), DieSteel);
                var mid = new Vector3(mu, (h00 + h10 + h01 + h11) * 0.25f, mv);
                var ta = new Vector3(x1 - x0, (h10 + h11 - h00 - h01) * 0.5f * (x1 - x0) / cw, 0);
                var tb = new Vector3(0, (h01 + h11 - h00 - h10) * 0.5f * (z1 - z0) / cl, z1 - z0);
                var nrm = Vector3.Normalize(Vector3.Cross(tb, ta));
                var plate = FaceThick + (top - lo); // (thick enough to meet its column under its lowest corner)
                Plate(gfx, m, mid - nrm * (plate * 0.5f), ta, tb, nrm, plate, DieFace);
            }

        // ---- The side jaws: a beam each side on carriages riding the out-rigger rails through the slots ----
        var useSide = head != null && side;
        for (var side2 = -1; side2 <= 1; side2 += 2)
        {
            var sd = (float)side2;
            var n = Rails(lh, rails, MathF.Sign(coreAt.X) == sd && MathF.Abs(coreAt.X) > wh ? coreAt.Z : float.NaN);
            var park = sd * (wh + 0.4f);
            float ReadyOf(ConstructPanelFormer.DieInfo? info, float half) => info is { SideGrip: true } ? sd * (half + 0.12f) : park;
            var ready = morph >= 0 ? ReadyOf(from, (from?.Width ?? 0) * 0.5f) + (ReadyOf(die, halfW) - ReadyOf(from, (from?.Width ?? 0) * 0.5f)) * morph
                : useSide ? sd * (halfW + 0.12f) : park;
            var grip = useSide ? pose.Grip : 0;
            var edge = sd * (halfW + 0.02f);
            var face = ready + (edge - ready) * grip;
            var beam = sd * MathF.Max(MathF.Abs(face), laneI + 0.02f); // (its beam stops at the lane: past it, the jaw's lips reach in)
            var gy = useSide ? EdgeY(edge - sd * 0.02f, 0) : WorkY + 0.004f + thick * 0.5f;
            gy = WorkY + 0.004f + thick * 0.5f + (gy - WorkY - 0.004f - thick * 0.5f) * (useSide ? pose.Hold : 0);
            var gap = MathF.Max(0.02f, thick * 0.5f + 0.006f);
            var open = (1 - grip) * 0.12f;
            var len = lh - 0.35f;
            float back = beam + sd * 0.26f;
            Box(gfx, m, Lo(beam, gy - 0.22f, -len, back), Hi(beam, gy - gap, len, back), Base);
            Box(gfx, m, Lo(beam, gy + gap + open, -len, back), Hi(beam, gy + 0.22f + open, len, back), Base);
            Box(gfx, m, Lo(face, gy - gap - 0.016f, -len + 0.02f, beam + sd * 0.2f), Hi(face, gy - gap, len - 0.02f, beam + sd * 0.2f), Accent); // (its grip pad, out to the lips)
            Box(gfx, m, Lo(face, gy + gap + open, -len + 0.02f, beam), Hi(face, gy + gap + open + 0.016f, len - 0.02f, beam), Steel); // (the upper lip)
            Box(gfx, m, Lo(back, gy + 0.06f, -len, back + sd * 0.012f), Hi(back, gy + 0.14f, len, back + sd * 0.012f), Accent); // (hazard on its back)
            for (var k = 0; k < n; k++)
            {
                var zs = _rails[k];
                var cx2 = beam + sd * 0.13f;
                Box(gfx, m, new Vector3(cx2 - 0.24f, SlotLo + 0.103f, zs - 0.28f), new Vector3(cx2 + 0.24f, 0.5f, zs + 0.28f), Base); // (a thick carriage on the rails)
                Box(gfx, m, new Vector3(cx2 - 0.25f, 0.46f, zs - 0.29f), new Vector3(cx2 + 0.25f, 0.5f, zs + 0.29f), Accent);
                Box(gfx, m, new Vector3(cx2 - 0.2f, 0.5f, zs - 0.22f), new Vector3(cx2 + 0.2f, 0.55f, zs + 0.22f), Trim);
                Cyl(gfx, m, 1, new Vector3(cx2, (0.55f + WorkY - 0.24f) * 0.5f, zs), 0.3f, WorkY - 0.79f, Cast); // (its lift cylinder's barrel)
                var rodTop = MathF.Max(WorkY - 0.18f, gy - 0.22f);
                Cyl(gfx, m, 1, new Vector3(cx2, (WorkY - 0.24f + rodTop) * 0.5f, zs), 0.13f, rodTop - (WorkY - 0.24f), Chrome);
            }
        }

        // ---- The end gates: two half-gates at each end of the sheet, hinged on lift cylinders in the post zones, swinging shut across it ----
        var useEnds = head != null && !side;
        var zjMost = lh - 0.16f;
        float EndOf(ConstructPanelFormer.DieInfo? info, float half) => info is { SideGrip: false } ? MathF.Min(half, zjMost) : zjMost;
        var endReady = morph >= 0 ? EndOf(from, (from?.Length ?? 0) * 0.5f) + (EndOf(die, halfL) - EndOf(from, (from?.Length ?? 0) * 0.5f)) * morph
            : useEnds ? MathF.Min(halfL, zjMost) : zjMost;
        var swing = Smooth(useEnds ? pose.Grip : 0);
        var armLen = laneO + 0.02f;
        var gateGap = MathF.Max(0.02f, thick * 0.5f + 0.006f);
        for (var sz = -1; sz <= 1; sz += 2)
        {
            var zz = sz * endReady;
            var gy = useEnds ? EdgeY(0, zz) : WorkY + 0.004f + thick * 0.5f;
            gy += (1 - swing) * 0.25f; // (open, it stands clear over the Out Table and the run)
            for (var sx = -1; sx <= 1; sx += 2)
            {
                var hinge = new Vector3(sx * armLen, gy, zz);
                // Shut: the arm points in across the sheet; open: along the run's side, away from the bed.
                float shutA = MathF.Atan2(0, -sx), openA = MathF.Atan2(sz, 0);
                var dA = openA - shutA;
                while (dA > MathF.PI)
                    dA -= MathF.Tau;
                while (dA < -MathF.PI)
                    dA += MathF.Tau;
                var ang = shutA + dA * (1 - swing);
                var dir = new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang));
                var arm = Matrix4x4.CreateRotationY(MathF.Atan2(-dir.Z, dir.X)) * Matrix4x4.CreateTranslation(hinge) * m; // (local +x along the arm)
                var len = armLen - 0.02f;
                Box(gfx, arm, new Vector3(0.1f, -0.22f, -0.15f), new Vector3(len, -gateGap, 0.15f), Base);
                Box(gfx, arm, new Vector3(0.1f, gateGap, -0.15f), new Vector3(len, 0.22f, 0.15f), Base);
                Box(gfx, arm, new Vector3(0.14f, -gateGap - 0.016f, -0.14f), new Vector3(len - 0.02f, -gateGap, 0.14f), Accent); // (its grip pad)
                Box(gfx, arm, new Vector3(len - 0.3f, 0.06f, 0.153f), new Vector3(len - 0.02f, 0.16f, 0.165f), Accent);
                Cyl(gfx, m, 1, hinge + new Vector3(0, -0.1f, 0), 0.22f, 0.44f, Base); // (its hinge collar on the rod's head)
                var rodLo = WorkY - 0.236f;
                var rodHi = gy - 0.32f;
                if (rodHi > rodLo + 0.01f)
                    Cyl(gfx, m, 1, new Vector3(hinge.X, (rodLo + rodHi) * 0.5f, zz), 0.13f, rodHi - rodLo, Chrome); // (the lift cylinder's rod, up out of the post zone)
            }
        }
    }

    /// <summary>The out-rigger rails' places along one side (into <see cref="_rails"/>, in order): evenly along the bed, those within the Core's base link's reach moved clear of it.</summary>
    private static int Rails(float lh, int n, float coreZ)
    {
        for (var k = 0; k < n; k++)
        {
            var z = -lh + (k + 0.5f) * (2 * lh / n);
            if (!float.IsNaN(coreZ) && MathF.Abs(z - coreZ) < 0.85f)
                z = Math.Clamp(coreZ + (z < coreZ || coreZ > 0 && z <= coreZ ? -0.85f : 0.85f), -lh + 0.4f, lh - 0.4f);
            _rails[k] = z;
        }
        Array.Sort(_rails, 0, n);
        return n;
    }

    private static float Smooth(float t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    // A box between two x values on either side (sd < 0 puts a > b): the lower and upper corners from x a to x b, y and z as given.
    private static Vector3 Lo(float a, float y, float z, float b) => new(MathF.Min(a, b), y, z);
    private static Vector3 Hi(float a, float y, float z, float b) => new(MathF.Max(a, b), y, z);

    /// <summary>A plate centred at <paramref name="at"/>, spanning <paramref name="ta"/> and <paramref name="tb"/>, <paramref name="thick"/> along <paramref name="n"/>.</summary>
    private static void Plate(Renderer gfx, Matrix4x4 frame, Vector3 at, Vector3 ta, Vector3 tb, Vector3 n, float thick, Vector3 colour)
    {
        var up = n * thick;
        var mm = new Matrix4x4(ta.X, ta.Y, ta.Z, 0, up.X, up.Y, up.Z, 0, tb.X, tb.Y, tb.Z, 0, at.X, at.Y, at.Z, 1);
        gfx.Draw(mm * frame, colour);
    }

    private static readonly ushort CylShape = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = 12 });
    private static readonly Matrix4x4 ToX = Matrix4x4.CreateRotationY(MathF.PI / 2), ToY = Matrix4x4.CreateRotationX(-MathF.PI / 2);

    /// <summary>A cylinder along axis <paramref name="axis"/> (0 x, 1 y, 2 z) of <paramref name="diameter"/> and <paramref name="length"/> about <paramref name="centre"/>.</summary>
    private static void Cyl(Renderer gfx, Matrix4x4 frame, int axis, Vector3 centre, float diameter, float length, Vector3 colour)
    {
        if (length < 0.005f)
            return;
        var mm = Matrix4x4.CreateScale(diameter, diameter, length) * (axis == 0 ? ToX : axis == 1 ? ToY : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(centre);
        gfx.Draw(mm * frame, colour, shape: CylShape);
    }

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour)
    {
        if (hi.X - lo.X > 0.001f && hi.Y - lo.Y > 0.001f && hi.Z - lo.Z > 0.001f)
            gfx.Draw(Scale(lo, hi) * frame, colour);
    }

    /// <summary>A glowing box (a lamp, or the pocket's faint fill): lit by itself, no real light.</summary>
    private static void Glow(Renderer gfx, Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour)
    {
        if (hi.X - lo.X > 0.001f && hi.Y - lo.Y > 0.001f && hi.Z - lo.Z > 0.001f)
            gfx.Draw(Scale(lo, hi) * frame, colour, mode: 2);
    }
}
