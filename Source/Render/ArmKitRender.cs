using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// An Arm Core's chain drawn posed (ArmKit.cs): each piece's own boxes (its pressed size and paint) along its link, from the mast it
/// stands as (every piece straight up on the Core, where it was built) to the arm the two-link solve gives, eased over the fold.
/// The hydraulic rams between the lugs are drawn between the pieces. Drawing only; allocates nothing.
/// </summary>
public static class ArmKitRender
{
    private static readonly Vector3 Ram = new(0.62f, 0.64f, 0.66f), RamBody = new(0.18f, 0.19f, 0.21f), Hose = new(0.08f, 0.08f, 0.09f);

    public static void Draw(Renderer gfx, Construct c, LifterDef ld, ArmSpec s, Matrix4x4 part, float yaw, Vector3 elbow, Vector3 wrist, float fold, int tipHead = -1, float slide = 0)
    {
        var t = fold * fold * (3 - 2 * fold); // (eased: it starts and stops gently)
        var heading = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw));
        var side = new Vector3(-heading.Z, 0, heading.X);
        var sh = ld.Base;
        float Angle(Vector3 d) => MathF.Atan2(d.Y, Vector3.Dot(d, heading));
        const float Up = MathF.PI / 2;
        float au = Up + (Angle(elbow - sh) - Up) * t, af = Up + (Angle(wrist - elbow) - Up) * t, aw = Up + (-Up - Up) * t;
        Vector3 Dir(float a) => heading * MathF.Cos(a) + Vector3.UnitY * MathF.Sin(a);
        Vector3 du = Dir(au), df = Dir(af), dw = Dir(aw);
        var e = sh + du * s.Upper;
        var tip = e + df * s.Fore;
        if (s.Knuckle >= 0) // (a Knuckle at the forearm's end, its drop Boom hanging plumb from it out; up the mast as it stands)
        {
            var ad = Up + (-Up - Up) * t;
            var dd = Dir(ad);
            Piece(gfx, c, s.Knuckle, tip - dd * (s.KnuckleA * 0.5f), dd, side, 1, part);
            Piece(gfx, c, s.DropBoom, tip + dd * (s.KnuckleA * 0.5f), dd, side, 1, part);
            tip += dd * s.Drop;
        }
        var hand = s.WristLen + s.HeadLen;
        var k = hand > 0.45f ? 1 + (0.45f / hand - 1) * t : 1; // (out, the wrist and clamp keep to the hand's drop over the gripper bar)
        Piece(gfx, c, s.Shoulder, sh - du * (s.JointA * 0.5f), du, side, 1, part);
        Piece(gfx, c, s.UpperBoom, sh + du * (s.JointA * 0.5f), du, side, 1, part);
        Piece(gfx, c, s.Elbow, e - df * (s.ElbowA * 0.5f), df, side, 1, part);
        Piece(gfx, c, s.ForeBoom, e + df * (s.ElbowA * 0.5f), df, side, 1, part);
        Piece(gfx, c, s.Wrist, tip, dw, side, k, part);
        var onTip = tipHead >= 0 && tipHead < s.Heads.Length ? s.Heads[tipHead].Part : s.Head;
        Piece(gfx, c, onTip, tip + dw * (s.WristLen * k), dw, side, k, part);
        // The spares on their stands (where they were built, not riding the carriage): a slot whose head is on the Wrist holds the arm's own head instead.
        var still = slide != 0 ? Matrix4x4.CreateTranslation(0, 0, -slide) * part : part;
        for (var h = 1; h < s.Heads.Length; h++)
        {
            ref readonly var hd = ref s.Heads[h];
            Piece(gfx, c, h == tipHead ? s.Heads[0].Part : hd.Part, hd.Slot, hd.SlotUp, hd.SlotSide, 1, still);
        }
        // On a Rail Carriage: the Core, its Pedestals, the Carriage, its Counterweights and Drive Packs, all run along with the arm.
        if (ld.SlideHi > ld.SlideLo)
            for (var q = 0; q < s.Carried.Length; q++)
                Whole(gfx, c, s.Carried[q], s.CarriedM[q] * part, q == 1 ? slide : float.NaN);
        // The rams: the shoulder's from a lug on the Core's top to a third of the way along the upper boom, the elbow's along the upper
        // boom's back to the forearm's root; each a dark barrel and a bright rod, the hose beside it.
        var off = Vector3.Cross(side, du) * (s.JointA * 0.55f);
        var r = MathF.Max(0.05f, s.JointA * 0.16f);
        var foot = sh - Vector3.UnitY * (s.JointA * 0.5f) - heading * (s.JointA * 0.6f);
        RamBetween(gfx, part, foot, sh + du * (s.Upper * 0.35f) + off, r);
        var offF = Vector3.Cross(side, df) * (s.ElbowA * 0.55f);
        RamBetween(gfx, part, sh + du * (s.Upper * 0.55f) + off, e + df * (s.ElbowA * 0.9f) + offF, r * 0.8f);
    }

    private static void Piece(Renderer gfx, Construct c, int i, Vector3 at, Vector3 dir, Vector3 side, float stretch, Matrix4x4 part)
    {
        if (i < 0 || i >= c.PartCount)
            return;
        ref var p = ref c.Parts[i];
        if (p.Broken)
            return;
        var d = p.D;
        var z = Vector3.Normalize(side - dir * Vector3.Dot(side, dir));
        var x = Vector3.Cross(dir, z);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, dir.X, dir.Y, dir.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        var m = Matrix4x4.CreateScale(1, stretch, 1) * basis * Matrix4x4.CreateTranslation(at) * part;
        var paint = p.Paint != 0 ? ConstructPaint.ToVector(p.Paint) : Vector3.Zero;
        for (var b = 0; b < d.Boxes.Length; b++)
        {
            ref readonly var box = ref d.Boxes[b];
            if (!box.Hidden)
                gfx.Draw(box.Model(0, box.Center) * m, p.Paint != 0 && box.Paintable ? paint : box.Color, shape: box.Shape);
        }
    }

    /// <summary>A part drawn whole in its frame <paramref name="m"/>; a Rail Carriage's wheels (its "roll" boxes) turn by how far it ran.</summary>
    private static void Whole(Renderer gfx, Construct c, int i, Matrix4x4 m, float ran)
    {
        if (i < 0 || i >= c.PartCount)
            return;
        ref var p = ref c.Parts[i];
        if (p.Broken)
            return;
        var d = p.D;
        var paint = p.Paint != 0 ? ConstructPaint.ToVector(p.Paint) : Vector3.Zero;
        for (var b = 0; b < d.Boxes.Length; b++)
        {
            ref readonly var box = ref d.Boxes[b];
            if (box.Hidden)
                continue;
            var bm = !float.IsNaN(ran) && box.Roll // (its wheels roll about their axle, x, by how far it ran)
                ? box.Model(0, Vector3.Zero) * Matrix4x4.CreateRotationX(ran / MathF.Max(0.05f, box.Half.Y)) * Matrix4x4.CreateTranslation(box.Center)
                : box.Model(0, box.Center);
            gfx.Draw(bm * m, p.Paint != 0 && box.Paintable ? paint : box.Color, shape: box.Shape);
        }
    }

    private static void RamBetween(Renderer gfx, Matrix4x4 part, Vector3 a, Vector3 b, float r)
    {
        var mid = a + (b - a) * 0.55f;
        Beam(gfx, part, a, mid, r * 2, RamBody);
        Beam(gfx, part, mid - (b - a) * 0.05f, b, r, Ram);
        Beam(gfx, part, a + new Vector3(0, r * 1.4f, 0), mid + new Vector3(0, r * 1.4f, 0), r * 0.35f, Hose);
    }

    private static void Beam(Renderer gfx, Matrix4x4 part, Vector3 a, Vector3 b, float thick, Vector3 colour)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = Vector3.Normalize(Vector3.Cross(MathF.Abs(z.Y) > 0.98f ? Vector3.UnitX : Vector3.UnitY, z));
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(thick, thick, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f) * part, colour);
    }
}
