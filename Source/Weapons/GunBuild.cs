using System.Numerics;

namespace CallOfSlop;

/// <summary>What a box of a built gun is (see <see cref="GunPart"/>).</summary>
[Flags]
public enum GunPartFlags : byte
{
    None = 0,

    /// <summary>Part of the muzzle attachment (scene graph grouping only; see GunRig).</summary>
    Muzzle = 1,

    /// <summary>Part of the underbarrel attachment (scene graph grouping only; see GunRig).</summary>
    Underbarrel = 2,

    /// <summary>Drawn with alpha blending (the laser beam).</summary>
    Blend = 4,

    /// <summary>A removable top part (iron sights, carry handle): taken off when any optic or scope is fitted.</summary>
    Top = 16,

    /// <summary>A stock comb or cheek rest (trimmed down under an optic like everything else near it).</summary>
    Comb = 64,

    /// <summary>Part of a removable magazine (it drops out and goes back in during a reload; see GunModel.MagOffset).</summary>
    Mag = 128,
}

/// <summary>One box of a built gun, in model space (origin at the grip, -Z towards the muzzle, +Y up, metres).</summary>
public struct GunPart
{
    public float Sx, Sy, Sz, X, Y, Z, Rx, Ry, Rz, Alpha;
    public Vector3 Color;
    public int Mode;
    public GunPartFlags Flags;

    /// <summary>Scale, then turn about Z, X and Y, then move: the box's model matrix.</summary>
    public readonly Matrix4x4 Matrix()
    {
        var m = Matrix4x4.CreateScale(Sx, Sy, Sz);
        if (Rz != 0)
            m *= Matrix4x4.CreateRotationZ(Rz);
        if (Rx != 0)
            m *= Matrix4x4.CreateRotationX(Rx);
        if (Ry != 0)
            m *= Matrix4x4.CreateRotationY(Ry);
        return m * Matrix4x4.CreateTranslation(X, Y, Z);
    }

    public readonly (Vector3 Min, Vector3 Max) Bounds()
    {
        var m = Matrix();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var p = Vector3.Transform(new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f), m);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return (min, max);
    }
}

/// <summary>A built gun body (no optic): its boxes, plus where the optic, the hands and the muzzle go.</summary>
public sealed class GunMesh
{
    public GunPart[] Parts = [];
    public Matrix4x4[] Matrices = [];

    /// <summary>Top of the highest permanent part under the rail: an optic's rail sits on it.</summary>
    public float Mount;

    /// <summary>Iron sight line height.</summary>
    public float Iron;

    public float OpticZ, RailWidth, RailFront, RailBack, Bore, MuzzleZ, Top;
    public float RecvLen, RecvH, RecvW, Front, Back;

    /// <summary>Pistol grip angle, radians (positive = raked back).</summary>
    public float GripAngle;

    public Vector3 Grip, Support, MagWell, Underbarrel, StockPoint;

    /// <summary>Built weapons (Zombies weapon builder): every barrel's muzzle, for one flash each. Empty for standard guns.</summary>
    public Vector3[] Muzzles = [];

    // What the mesh was built from (rebuilt when any of these change on the same WeaponDef).
    internal GunSpec? Spec;
    internal uint Kit;
    internal OpticKind Optic;
    internal float AdsZoom;
}

/// <summary>
/// Builds a gun body out of boxes from its <see cref="GunSpec"/> (the weapon file's "model" block) plus the attachments in
/// its kit (muzzle devices, barrels, underbarrel parts, magazines, stocks). Optics are added at draw time by
/// <see cref="GunModel"/>. With an optic or a built-in scope fitted, iron sights and carry handles come off and anything
/// else that would reach into the optic is trimmed down to the rail.
///
/// tools/gunviz/gm.py mirrors this file for the contact sheets the models were designed on; the self-test "Gun models"
/// compares part counts and bounds with it.
/// </summary>
public sealed class GunBuilder
{
    private static readonly Vector3 Dark = new(0.21f, 0.21f, 0.23f);
    private static readonly Vector3 Metal = new(0.58f, 0.6f, 0.64f);
    private static readonly Vector3 RedGlow = new(1f, 0.15f, 0.1f);
    private static readonly Vector3 Rubber = new(0.12f, 0.12f, 0.13f);
    private static readonly Vector3 Pad = new(0.35f, 0.12f, 0.1f);
    private static readonly Vector3 ShellRed = new(0.72f, 0.15f, 0.12f);
    private const float PI = MathF.PI;

    private static Vector3 Named(string n) => GunColor.Named[n];

    private readonly WeaponDef _w;
    private readonly GunSpec p;
    private readonly List<GunPart> _parts = new(64);
    private readonly GunMesh _mesh = new();
    private GunPartFlags _flags;

    private Vector3 M, F, D, A, _magc;
    private float RL, RH, RW, _front, _back, _mid, _bore, _gripZ, BL, BW, HL, HH, _hgBottom, _cylZ;
    private Vector3 _gripPt;

    private GunBuilder(WeaponDef w, GunSpec spec)
    {
        _w = w;
        p = spec;
    }

    /// <summary>The finished body for a weapon (stock or kitted) that has a model block.</summary>
    public static GunMesh Build(WeaponDef w)
    {
        var b = new GunBuilder(w, w.Model!);
        var mesh = b.Run();
        if (GunModel.HasScope(w))
        {
            b._parts.RemoveAll(x => (x.Flags & GunPartFlags.Top) != 0);
            b.ClampUnderOptic();
        }
        mesh.Parts = b._parts.ToArray();
        mesh.Matrices = mesh.Parts.Select(x => x.Matrix()).ToArray();
        mesh.Spec = w.Model;
        mesh.Kit = w.Kit;
        mesh.Optic = w.Optic;
        mesh.AdsZoom = w.AdsZoom;
        return mesh;
    }

    private string? Att(AttachSlot slot) => Attachments.Get(slot, Attachments.Slot(_w.Kit, slot))?.Name;

    private static float Sum(Vector3 c) => c.X + c.Y + c.Z;

    private void Box(float sx, float sy, float sz, float x, float y, float z, Vector3 c, float rx = 0, float rz = 0, float ry = 0, int mode = 0, float alpha = 1f,
        GunPartFlags flags = 0) =>
        _parts.Add(new GunPart { Sx = sx, Sy = sy, Sz = sz, X = x, Y = y, Z = z, Color = c, Rx = rx, Rz = rz, Ry = ry, Mode = mode, Alpha = alpha, Flags = flags | _flags });

    /// <summary>A round-ish rod along Z: two boxes, one turned 45 degrees (reads as an octagon).</summary>
    private void Rod(float wd, float len, float x, float y, float z, Vector3 c, GunPartFlags flags = 0)
    {
        Box(wd, wd, len, x, y, z, c, flags: flags);
        Box(wd, wd, len, x, y, z, c * 0.93f, rz: PI / 4, flags: flags);
    }

    private GunMesh Run()
    {
        var acc = p.Accent;
        M = p.Finish.Resolve(acc);
        F = p.Furniture.Resolve(acc);
        D = Sum(M) > 1.2f ? M * 0.55f : Dark * 0.8f; // detail colour: darker than the finish
        A = acc;
        _magc = p.MagColor.Resolve(acc);
        if (p.MagColor.IsAccent)
            _magc = acc * 0.85f;
        if (p.AccentStyle != "mag" && p.MagColor.IsAccent)
            _magc = Sum(M) < 2f ? M * 0.8f : Dark * 1.1f;
        var style = p.Receiver;
        RL = p.ReceiverLength;
        RH = p.ReceiverHeight;
        RW = p.ReceiverWidth;
        var bullpup = style == "bullpup";
        if (bullpup)
        {
            _back = RL * 0.68f;
            _front = _back - RL;
        }
        else
        {
            _back = RL * 0.38f;
            _front = -RL * 0.62f;
        }
        _mid = (_front + _back) / 2;
        var pistol = style is "slide" or "revolver";
        _bore = RH * (pistol ? 0.15f : 0.12f);
        _gripZ = bullpup ? _front + RL * 0.24f : pistol ? _back - 0.025f : _back - RL * 0.2f;

        // ---- Barrel length (with barrel attachments) ----
        BL = p.BarrelLength;
        HL = p.Handguard != "none" ? p.HandguardLength : 0f;
        var barrelAtt = Att(AttachSlot.Barrel);
        if (barrelAtt == "Long Barrel")
            BL += 0.08f;
        else if (barrelAtt == "Short Barrel")
            BL -= MathF.Min(0.06f, BL * 0.35f);
        BL = MathF.Max(BL, MathF.Max(HL + 0.015f, 0.02f));
        BW = p.BarrelWidth * (barrelAtt == "Heavy Barrel" ? 1.4f : 1f);
        HH = p.HandguardHeight;

        // ---- Receiver ----
        var top = Receiver(style);

        // ---- Top rail (permanent) ----
        var (railW, railF, railB) = RailSpan(style);
        if (p.TopRail && !pistol)
        {
            Box(railW, 0.008f, railB - railF, 0, top + 0.004f, (railF + railB) / 2, Dark * 0.9f);
            for (var i = 0; i < 4; i++)
                Box(railW * 1.1f, 0.003f, 0.004f, 0, top + 0.0085f, railF + (railB - railF) * (0.12f + i * 0.25f), Dark * 1.3f);
            top += 0.008f;
        }

        // ---- Barrel(s), shroud ----
        var bstart = _front;
        if (style == "revolver")
            bstart = _cylZ - RL * 0.15f; // the barrel screws into the frame right in front of the cylinder
        Barrels(bstart);
        var muzzleZ = bstart - BL;
        Shroud(bstart);

        // ---- Handguard ----
        var hgBottom = HandguardPart(bstart);
        _hgBottom = hgBottom;
        var ubZ = bstart - (HL > 0.06f ? MathF.Min(HL * 0.45f, 0.24f) : MathF.Min(0.05f, BL * 0.5f)); // the off hand stays within reach on long forends
        if (p.Handguard == "pump")
            ubZ = bstart - 0.03f - HL * 0.5f;
        var ubY = hgBottom;

        // ---- Gas block / tube, ramrod, coils ----
        BarrelExtras(bstart);

        // ---- Muzzle device (built in, unless an attachment is fitted) ----
        var muzzleAtt = Att(AttachSlot.Muzzle);
        if (muzzleAtt == null || p.Muzzle is "bell" or "tip")
            muzzleZ = MuzzlePart(muzzleZ);
        _flags = GunPartFlags.Muzzle;
        muzzleZ = MuzzleAttachment(muzzleAtt, muzzleZ);
        _flags = 0;

        // ---- Grip, magazine, stock, receiver extras, underbarrel, sights ----
        _gripPt = GripPart(_gripZ);
        // Detachable magazines are tagged so a reload can take them out (tubes, internal and helical ones stay put).
        _flags = p.Mag is "tube" or "internal" or "helical" or "none" ? 0 : GunPartFlags.Mag;
        var magPt = Magazine(bstart);
        _flags = 0;
        StockPart(style);
        ReceiverExtras(style, top);
        _flags = GunPartFlags.Underbarrel;
        var support = UnderbarrelPart(ubZ, ubY, bstart);
        _flags = 0;
        var iron = SightsPart(top, bstart);

        // ---- Where an optic goes ----
        var m = _mesh;
        m.RailWidth = railW;
        m.RailFront = railF;
        m.RailBack = railB;
        m.Bore = _bore;
        m.Top = top;
        m.Mount = MountHeight(railW, railF, railB, top);
        m.Iron = iron;
        var oz = (railF + railB) / 2;
        if (pistol)
            oz = style == "slide" ? _back - RL * 0.36f : _cylZ;
        m.OpticZ = Math.Clamp(oz, -0.085f, 0.05f);
        m.MuzzleZ = muzzleZ;
        m.Underbarrel = new Vector3(0, ubY, ubZ);
        m.MagWell = magPt;
        m.StockPoint = new Vector3(0, -RH * 0.05f, _back);
        m.Grip = _gripPt;
        m.Support = support;
        m.RecvLen = RL;
        m.RecvH = RH;
        m.RecvW = RW;
        m.Front = _front;
        m.Back = _back;
        m.GripAngle = p.GripAngle * PI / 180;
        return m;
    }

    // ---- Receiver styles ------------------------------------------------------------------------

    private float Receiver(string style)
    {
        float top;
        switch (style)
        {
            case "box":
                Box(RW, RH * 0.55f, RL, 0, RH * 0.2f, _mid, M);
                Box(RW * 0.92f, RH * 0.45f, RL * 0.8f, 0, -RH * 0.28f, _mid + RL * 0.05f, M * 0.85f);
                Box(0.003f, RH * 0.2f, RL * 0.26f, RW / 2 + 0.0015f, RH * 0.2f, _mid - RL * 0.04f, D); // ejection port
                top = RH * 0.475f;
                break;
            case "slab":
                Box(RW, RH, RL, 0, 0, _mid, M);
                Box(RW * 1.02f, RH * 0.05f, RL * 0.92f, 0, -RH * 0.12f, _mid, D); // seam
                Box(0.003f, RH * 0.22f, RL * 0.24f, RW / 2 + 0.0015f, RH * 0.2f, _mid - RL * 0.05f, D);
                top = RH * 0.5f;
                break;
            case "round":
                Rod(RH * 0.7f, RL, 0, _bore, _mid, M);
                Box(RW * 0.8f, RH * 0.42f, RL * 0.62f, 0, -RH * 0.25f, _mid + RL * 0.12f, D);
                top = _bore + RH * 0.7f * 0.7071f;
                break;
            case "slide":
                Box(RW * 0.9f, RH * 0.45f, RL * 0.85f, 0, -RH * 0.25f, _mid + 0.01f, F); // frame
                Box(RW, RH * 0.55f, RL, 0, RH * 0.22f, _mid, M); // slide
                for (var i = 0; i < 3; i++)
                    Box(RW * 1.03f, RH * 0.35f, 0.004f, 0, RH * 0.2f, _back - 0.012f - i * 0.01f, D); // serrations
                top = RH * 0.22f + RH * 0.275f;
                break;
            case "revolver":
            {
                var cz = _back - RL * 0.42f;
                _cylZ = cz;
                Box(RW * 0.8f, RH * 0.55f, RL * 0.62f, 0, -RH * 0.12f, _back - RL * 0.31f, M); // frame
                Rod(RH * 0.72f, RL * 0.3f, 0, RH * 0.1f, cz, M * 1.08f); // cylinder
                for (var i = 0; i < 3; i++)
                    Box(RH * 0.74f, 0.006f, RL * 0.26f, 0, RH * 0.1f, cz, M * 0.7f, rz: i * PI / 3); // flutes
                Box(RW * 0.55f, RH * 0.14f, RL * 0.62f, 0, RH * 0.45f, _back - RL * 0.36f, M); // top strap
                top = RH * 0.1f + RH * 0.72f * 0.7071f;
                break;
            }
            case "bullpup":
                Box(RW, RH * 0.72f, RL, 0, -RH * 0.12f, _mid, F); // shell
                Box(RW * 0.86f, RH * 0.32f, RL * 0.62f, 0, RH * 0.36f, _front + RL * 0.31f, M); // upper receiver
                Box(RW * 1.01f, RH * 0.3f, RL * 0.3f, 0, -RH * 0.2f, _back - RL * 0.17f, F * 0.85f); // butt section
                Box(RW * 0.95f, RH * 0.25f, RL * 0.12f, 0, -RH * 0.42f, _front + RL * 0.06f, F, rx: 0.5f); // chamfered nose
                top = RH * 0.52f;
                break;
            case "lever":
                Box(RW, RH * 0.78f, RL, 0, -RH * 0.06f, _mid, M);
                Box(RW * 0.8f, RH * 0.2f, RL * 0.92f, 0, RH * 0.38f, _mid, M); // rounded top
                Box(RW * 1.02f, RH * 0.32f, RL * 0.4f, 0, -RH * 0.05f, _mid + RL * 0.12f, M * 1.15f); // side plate / loading gate
                top = RH * 0.48f;
                break;
            case "break":
                Box(RW, RH * 0.8f, RL, 0, -RH * 0.08f, _mid, M); // action block
                Box(RW * 1.03f, RH * 0.45f, RL * 0.6f, 0, -RH * 0.15f, _mid + RL * 0.12f, M * 1.2f); // side plates
                Box(RW * 0.4f, RH * 0.35f, RL * 0.25f, 0, -RH * 0.55f, _front + RL * 0.1f, D); // hinge knuckle
                top = RH * 0.32f;
                break;
            case "tool":
                Box(RW, RH, RL * 0.7f, 0, RH * 0.1f, _back - RL * 0.35f, M); // motor housing
                Box(RW * 1.03f, RH * 0.18f, RL * 0.5f, 0, RH * 0.05f, _back - RL * 0.32f, D); // grip band
                Box(RW * 0.6f, RH * 0.55f, RL * 0.32f, 0, _bore - RH * 0.12f, _front + RL * 0.16f, D * 1.3f); // nose block
                Box(RW * 0.9f, RH * 0.1f, RL * 0.68f, 0, RH * 0.62f, _back - RL * 0.35f, D); // top handle bar
                top = RH * 0.67f;
                break;
            case "toy":
                Box(RW, RH * 0.8f, RL, 0, 0, _mid, M);
                Box(RW * 0.8f, RH * 0.2f, RL * 0.85f, 0, RH * 0.45f, _mid, M); // rounded top
                Box(RW * 0.8f, RH * 0.2f, RL * 0.9f, 0, -RH * 0.45f, _mid, M); // rounded belly
                Box(RW * 1.04f, RH * 0.42f, RL * 0.55f, 0, -RH * 0.02f, _mid + RL * 0.05f, F); // side panel
                top = RH * 0.55f;
                break;
            default: // sci
                Box(RW, RH * 0.66f, RL, 0, RH * 0.05f, _mid, M);
                Box(RW * 0.55f, RH * 0.18f, RL * 0.9f, 0, RH * 0.47f, _mid, M * 0.9f); // spine
                Box(RW * 0.86f, RH * 0.32f, RL * 0.55f, 0, -RH * 0.4f, _mid + RL * 0.12f, F); // belly
                Box(RW * 0.7f, RH * 0.5f, RL * 0.14f, 0, -RH * 0.2f, _front + RL * 0.07f, F, rx: 0.45f); // swept nose
                top = RH * 0.56f;
                break;
        }
        // Accents on the receiver
        switch (p.AccentStyle)
        {
            case "stripe":
                Box(RW * 1.03f, RH * 0.08f, RL * 0.7f, 0, RH * 0.3f, _mid - RL * 0.02f, A);
                break;
            case "panel" when style != "toy":
                Box(RW * 1.04f, RH * 0.3f, RL * 0.36f, 0, -RH * 0.05f, _mid + RL * 0.08f, A);
                break;
            case "glow":
                Box(RW * 1.04f, RH * 0.05f, RL * 0.75f, 0, RH * 0.12f, _mid, A, mode: 2);
                break;
        }
        return top;
    }

    /// <summary>Where an optic's rail sits: width, front z, back z.</summary>
    private (float W, float Front, float Back) RailSpan(string style)
    {
        var w = MathF.Max(RW * 0.5f, 0.022f);
        switch (style)
        {
            case "bullpup":
                return (w, _front + RL * 0.05f, _front + RL * 0.55f);
            case "slide":
            {
                var oz = _back - RL * 0.36f;
                return (w, oz - 0.025f, oz + 0.025f);
            }
            case "revolver":
                return (w, _cylZ - RL * 0.15f, _back - RL * 0.1f);
            case "round":
                return (w, _front + 0.01f, _back - 0.01f);
            case "tool":
                return (w, _back - RL * 0.62f, _back - RL * 0.08f);
            default:
                return (w, _front + RL * 0.08f, _back - RL * 0.06f);
        }
    }

    // ---- Barrels --------------------------------------------------------------------------------

    private void OneBarrel(float x, float y, float start, Vector3 c)
    {
        var z = start - BL / 2;
        switch (p.BarrelStyle)
        {
            case "square":
                Box(BW, BW, BL, x, y, z, c);
                break;
            case "fluted":
                Box(BW, BW, BL, x, y, z, c);
                for (var i = 0; i < 3; i++)
                    Box(BW * 1.12f, BW * 0.3f, BL * 0.2f, x, y, start - BL * (0.25f + i * 0.25f), c * 0.7f);
                break;
            default:
                Rod(BW, BL, x, y, z, c);
                break;
        }
    }

    private void Barrels(float start)
    {
        var n = p.Barrels;
        var c = Sum(M) > 1.2f ? M * 0.75f : Dark * 0.75f;
        if (p.Finish.IsAccent)
            c = Dark * 0.9f;
        if (n == 1 || p.BarrelLayout == "single")
            OneBarrel(0, _bore, start, c);
        else if (p.BarrelLayout == "side")
            for (var i = 0; i < n; i++)
                OneBarrel((i - (n - 1) / 2f) * BW * 0.98f, _bore, start, c);
        else if (p.BarrelLayout == "over")
            for (var i = 0; i < n; i++)
                OneBarrel(0, _bore - i * BW * 0.98f, start, c);
        else // rotary
        {
            var r = BW * 1.35f;
            for (var i = 0; i < n; i++)
            {
                var a = i * 2 * PI / n;
                Box(BW, BW, BL, MathF.Cos(a) * r, _bore + MathF.Sin(a) * r, start - BL / 2, c);
            }
            Rod(BW * 1.2f, BL, 0, _bore, start - BL / 2, c * 0.8f); // spindle
            foreach (var k in (ReadOnlySpan<float>)[0.08f, 0.55f, 0.97f])
                Rod(r * 2 + BW * 1.6f, 0.012f, 0, _bore, start - BL * k, M * 0.9f); // clamps
        }
    }

    private void Shroud(float start)
    {
        var s = p.Shroud;
        if (s == "none")
            return;
        var ln = BL * p.ShroudLength;
        var z = start - ln / 2;
        switch (s)
        {
            case "jacket":
                Rod(BW * 2f, ln, 0, _bore, z, M * 0.9f);
                break;
            case "perforated":
                Rod(BW * 2f, ln, 0, _bore, z, M * 0.9f);
                for (var i = 0; i < 5; i++)
                    Box(BW * 2.1f, BW * 0.5f, ln * 0.08f, 0, _bore, start - ln * (0.12f + i * 0.18f), Dark * 0.55f);
                break;
            case "heatshield":
                Box(BW * 1.7f, BW * 0.7f, ln, 0, _bore + BW * 0.62f, z, M * 0.95f);
                for (var i = 0; i < 4; i++)
                    Box(BW * 1.75f, BW * 0.25f, ln * 0.1f, 0, _bore + BW * 0.7f, start - ln * (0.15f + i * 0.22f), Dark * 0.55f);
                break;
            case "suppressor":
                Rod(BW * 2.4f, ln, 0, _bore, z, M * 0.85f);
                Rod(BW * 2.55f, 0.008f, 0, _bore, start - ln + 0.004f, D); // end cap
                Rod(BW * 2.55f, 0.008f, 0, _bore, start - 0.004f, D);
                break;
            case "coils":
            {
                var n = Math.Max(p.Coils, 1);
                for (var i = 0; i < n; i++)
                    Box(BW * 2.5f, BW * 2.5f, 0.014f, 0, _bore, start - ln * (0.1f + i * 0.85f / n), A, mode: 2);
                Box(BW * 0.5f, BW * 2.7f, ln, 0, _bore, z, M * 0.9f); // spine through the coils
                break;
            }
            default: // rails: twin prongs instead of a barrel jacket, with a glow between them
            {
                Box(BW * 0.7f, BW * 1.3f, ln, -BW * 1f, _bore, z, M);
                Box(BW * 0.7f, BW * 1.3f, ln, BW * 1f, _bore, z, M);
                Box(BW * 1.2f, BW * 0.35f, ln * 0.92f, 0, _bore, z, A, mode: 2);
                var n = Math.Max(p.Coils, 1);
                for (var i = 0; i < Math.Max(p.Coils, 0); i++)
                    Box(BW * 3f, BW * 2f, 0.016f, 0, _bore, start - ln * (0.12f + i * 0.8f / n), M * 0.75f);
                break;
            }
        }
    }

    private float HandguardPart(float start)
    {
        var h = p.Handguard;
        if (h == "none" || HL <= 0)
            return _bore - BW * 0.6f;
        var z = start - HL / 2;
        var y = _bore - HH * 0.1f;
        switch (h)
        {
            case "rail":
                Box(RW * 0.9f, HH, HL, 0, y, z, Dark * 1.2f);
                for (var i = 0; i < 4; i++)
                    Box(RW * 0.92f, HH * 0.25f, HL * 0.1f, 0, y, start - HL * (0.2f + i * 0.2f), Dark * 0.7f);
                return y - HH / 2;
            case "slim":
                Box(RW * 0.78f, HH, HL, 0, y, z, F);
                for (var i = 0; i < 3; i++)
                    Box(RW * 0.8f, HH * 0.3f, HL * 0.12f, 0, y - HH * 0.1f, start - HL * (0.22f + i * 0.27f), F * 0.6f);
                return y - HH / 2;
            case "ribbed":
                Rod(HH, HL, 0, y, z, F);
                for (var i = 0; i < 5; i++)
                    Rod(HH * 1.06f, HL * 0.035f, 0, y, start - HL * (0.1f + i * 0.2f), F * 0.75f);
                return y - HH * 0.71f;
            case "wood":
                Box(RW * 0.86f, HH * 0.7f, HL, 0, y - HH * 0.15f, z, F); // lower
                Box(RW * 0.66f, HH * 0.3f, HL * 0.75f, 0, y + HH * 0.36f, start - HL * 0.42f, F * 0.9f); // upper guard
                return y - HH * 0.5f;
            case "quad":
                Box(RW * 0.92f, HH, HL, 0, y, z, M * 0.9f);
                Box(RW * 0.4f, 0.008f, HL * 0.96f, 0, y - HH / 2 - 0.004f, z, Dark * 0.9f); // bottom rail
                Box(0.008f, HH * 0.4f, HL * 0.96f, RW * 0.46f + 0.004f, y, z, Dark * 0.9f); // side rails
                Box(0.008f, HH * 0.4f, HL * 0.96f, -RW * 0.46f - 0.004f, y, z, Dark * 0.9f);
                return y - HH / 2 - 0.008f;
            case "tube":
                Rod(HH, HL, 0, y, z, Dark * 1.15f);
                for (var i = 0; i < 4; i++)
                {
                    Box(0.004f, HH * 0.3f, HL * 0.12f, HH * 0.5f, y, start - HL * (0.15f + i * 0.22f), Dark * 0.6f);
                    Box(0.004f, HH * 0.3f, HL * 0.12f, -HH * 0.5f, y, start - HL * (0.15f + i * 0.22f), Dark * 0.6f);
                }
                return y - HH * 0.71f;
            case "forend":
            {
                var fy = _bore - BW * 0.5f - HH * 0.35f;
                Box(RW * 0.82f, HH, HL, 0, fy, z, F);
                return fy - HH / 2;
            }
            case "pump":
            {
                var ty = _bore - BW * 0.95f;
                var pz = start - 0.03f - HL / 2;
                Box(RW * 0.95f, HH, HL, 0, ty, pz, F);
                for (var i = 0; i < 5; i++)
                    Box(RW * 0.97f, HH * 0.8f, HL * 0.04f, 0, ty - HH * 0.05f, pz - HL * 0.4f + i * HL * 0.2f, F * 0.7f);
                return ty - HH / 2;
            }
            default: // stock: a long one-piece forend under the barrel (musket, M14, bolt guns)
            {
                var fy = _bore - HH * 0.4f;
                Box(RW * 0.95f, HH, HL, 0, fy, z, F);
                Box(RW * 0.7f, HH * 0.4f, HL * 0.12f, 0, fy - HH * 0.35f, start - HL + HL * 0.06f, F * 0.9f); // tip
                if (p.AccentStyle == "bands")
                    foreach (var k in (ReadOnlySpan<float>)[0.3f, 0.62f, 0.94f])
                        Box(RW * 1f, HH * 0.9f + BW, 0.012f, 0, fy + BW * 0.4f, start - HL * k, Named("brass") * 0.95f);
                return fy - HH / 2;
            }
        }
    }

    private void BarrelExtras(float start)
    {
        if (p.GasBlock)
        {
            var gl = MathF.Min(BL * 0.55f, MathF.Max(p.HandguardLength, 0.1f) + 0.04f);
            Rod(BW * 0.75f, gl, 0, _bore + BW * 1.05f, start - gl / 2, M * 0.9f); // gas tube
            Box(BW * 1.4f, BW * 2.2f, 0.025f, 0, _bore + BW * 0.5f, start - gl, M * 0.85f); // gas block
        }
        if (p.Ramrod)
            Box(0.007f, 0.007f, BL * 0.88f, 0, _bore - BW * 0.5f - p.HandguardHeight * 0.75f, start - BL * 0.5f, Named("steel") * 0.9f);
        if (p.Coils > 0 && p.Shroud is not ("coils" or "rails"))
            for (var i = 0; i < p.Coils; i++)
                Box(BW * 2.4f, BW * 2.4f, 0.012f, 0, _bore, start - BL * (0.15f + i * 0.7f / p.Coils), A, mode: 2);
    }

    private float MuzzlePart(float z)
    {
        switch (p.Muzzle)
        {
            case "crown":
                Rod(BW * 1.2f, 0.012f, 0, _bore, z + 0.006f, M * 1.1f);
                return z;
            case "flash":
                Rod(BW * 1.3f, 0.045f, 0, _bore, z - 0.0225f, Dark * 0.9f);
                for (var i = 0; i < 3; i++)
                    Box(BW * 1.35f, BW * 0.3f, 0.01f, 0, _bore, z - 0.01f - i * 0.013f, Dark * 0.5f);
                return z - 0.045f;
            case "brake":
                Box(BW * 2.8f, BW * 1.5f, 0.075f, 0, _bore, z - 0.0375f, M * 1.1f);
                Box(BW * 2.9f, BW * 0.45f, 0.014f, 0, _bore, z - 0.02f, Dark);
                Box(BW * 2.9f, BW * 0.45f, 0.014f, 0, _bore, z - 0.052f, Dark);
                return z - 0.075f;
            case "bell":
                Rod(BW * 1.7f, 0.03f, 0, _bore, z - 0.015f, M * 1.05f);
                return z - 0.03f;
            case "comp":
                Box(BW * 1.7f, BW * 1.5f, 0.05f, 0, _bore, z - 0.025f, M * 1.1f);
                for (var i = 0; i < 2; i++)
                    Box(BW * 0.8f, BW * 0.4f, 0.008f, 0, _bore + BW * 0.6f, z - 0.012f - i * 0.018f, Dark);
                return z - 0.05f;
            case "tip":
                Box(BW * 1.5f, BW * 1.5f, 0.025f, 0, _bore, z - 0.0125f, p.Receiver == "toy" ? Named("orange") : A * 0.9f);
                return z - 0.025f;
            case "breacher":
                Rod(BW * 1.3f, 0.04f, 0, _bore, z - 0.02f, Dark * 1.1f);
                for (var i = 0; i < 3; i++)
                {
                    var a = i * 2 * PI / 3;
                    Box(BW * 0.3f, BW * 0.3f, 0.02f, MathF.Cos(a) * BW * 0.55f, _bore + MathF.Sin(a) * BW * 0.55f, z - 0.05f, Dark * 1.2f);
                }
                return z - 0.06f;
            case "ported":
                for (var i = 0; i < 3; i++)
                    Box(BW * 0.5f, 0.004f, 0.008f, 0, _bore + BW * 0.5f, z + 0.012f + i * 0.016f, Dark);
                return z;
            default:
                return z;
        }
    }

    private float MuzzleAttachment(string? name, float z)
    {
        switch (name)
        {
            case "Suppressor":
            {
                var ln = _w.Class == WeaponClass.Pistol ? 0.13f : 0.2f;
                var r = MathF.Max(BW * 1.25f, 0.024f);
                Rod(r * 1.7f, ln, 0, _bore, z - ln / 2, Metal * 0.75f);
                for (var i = 1; i < 4; i++)
                    Rod(r * 1.06f * 1.7f, 0.006f, 0, _bore, z - ln * i / 4, Metal * 0.55f);
                return z - ln;
            }
            case "Compensator":
                Box(BW * 1.8f, BW * 1.6f, 0.06f, 0, _bore, z - 0.03f, Metal);
                for (var i = 0; i < 3; i++)
                    Box(BW * 0.9f, BW * 0.45f, 0.008f, 0, _bore + BW * 0.6f, z - 0.012f - i * 0.016f, Dark);
                return z - 0.06f;
            case "Muzzle Brake":
                Box(BW * 3.2f, BW * 1.3f, 0.07f, 0, _bore, z - 0.035f, Metal);
                Box(BW * 3.3f, BW * 0.4f, 0.012f, 0, _bore, z - 0.02f, Dark);
                Box(BW * 3.3f, BW * 0.4f, 0.012f, 0, _bore, z - 0.048f, Dark);
                return z - 0.07f;
            case "Choke":
                Rod(BW * 0.75f * 1.7f, 0.05f, 0, _bore, z - 0.025f, Metal);
                Rod(BW * 0.8f * 1.7f, 0.008f, 0, _bore, z - 0.012f, A);
                return z - 0.05f;
            default:
                return z;
        }
    }

    // ---- Grip -----------------------------------------------------------------------------------

    private Vector3 GripPart(float gz)
    {
        var g = p.Grip;
        var a = p.GripAngle * PI / 180;
        var gy = p.Receiver == "slide" ? -RH * 0.47f : -RH * 0.5f;
        var gc = g != "wood" ? F : Named("wood");
        if (g == "wood" && !(p.Furniture.Name == "polymer"))
            gc = F;
        var gh = p.Receiver is "slide" or "revolver" ? 0.115f : 0.105f;
        if (g != "none")
        {
            // trigger guard + trigger
            var tz = g != "straight" ? gz - 0.035f : _back - 0.045f;
            Box(RW * 0.5f, 0.01f, 0.05f, 0, gy - 0.026f, tz, D);
            Box(RW * 0.5f, 0.026f, 0.008f, 0, gy - 0.013f, tz - 0.025f, D);
            Box(0.006f, 0.018f, 0.006f, 0, gy - 0.009f, tz + 0.004f, Metal * 0.7f);
        }
        switch (g)
        {
            case "pistol":
            case "wood":
            {
                var cy = gy - gh / 2 + 0.006f;
                Box(RW * 0.82f, gh, 0.05f, 0, cy, gz + MathF.Sin(a) * gh * 0.45f, gc, rx: -a);
                return new Vector3(0, cy, gz + MathF.Sin(a) * gh * 0.45f);
            }
            case "birdshead":
                Box(RW * 0.8f, gh * 0.55f, 0.045f, 0, gy - gh * 0.25f, gz + 0.008f, gc, rx: -a);
                Box(RW * 0.82f, gh * 0.4f, 0.05f, 0, gy - gh * 0.62f, gz + 0.03f, gc, rx: -a * 1.8f);
                return new Vector3(0, gy - gh * 0.4f, gz + 0.015f);
            case "straight":
                return new Vector3(0, -RH * 0.3f, _back + 0.05f); // the hand holds the stock's wrist, just behind the receiver
            default:
                return new Vector3(0, gy - 0.03f, gz);
        }
    }

    // ---- Magazine -------------------------------------------------------------------------------

    private Vector3 Magazine(float bstart)
    {
        var mt = p.Mag;
        var att = Att(AttachSlot.Magazine);
        var cap = p.Cap;
        var size = Math.Clamp(cap / 30f, 0.5f, 1.4f);
        var c = _magc;
        var depth = 0.036f + 0.022f * p.Caliber;
        var mz = (p.Receiver == "bullpup" ? _back - RL * 0.3f : _front + RL * 0.22f) + p.MagOffset;
        var my = -RH * 0.5f;
        var ang = p.MagAngle * PI / 180;
        var (sa, ca) = MathF.SinCos(ang);
        var ext = att == "Extended Mag" ? 1.5f : att == "Hollow Point Mag" ? 0.68f : 1f; // hollow points: a short magazine
        var ln = p.MagLength;
        if (att == "Drum Mag" && mt is "box" or "curved" or "stick" or "beltbox" or "side" or "cell" or "topbox" or "pan" or "helical")
            mt = "drum";
        if (att == "Fast Mag" && mt is "box" or "curved" or "stick" or "cell")
        {
            var h = ln > 0 ? ln : 0.15f * size;
            Box(RW * 0.55f, h, depth * 0.85f, -RW * 0.3f, my - h / 2 * ca, mz - h / 2 * sa, c, rx: ang);
            Box(RW * 0.55f, h, depth * 0.85f, RW * 0.3f, my - h / 2 * ca, mz - h / 2 * sa, c * 0.9f, rx: ang);
            Box(RW * 1.25f, 0.02f, depth * 0.95f, 0, my - h * 0.4f, mz - h * 0.4f * sa, Metal); // clamp
            return new Vector3(0, my, mz);
        }
        switch (mt)
        {
            case "box" or "stick" or "cell" or "strip":
            {
                var bh = mt switch { "box" => 0.1f, "stick" => 0.16f, "cell" => 0.075f, _ => 0.13f };
                var h = (ln > 0 ? ln : bh * size) * ext;
                var wd = mt switch { "box" => 0.62f, "stick" => 0.5f, "cell" => 0.75f, _ => 0.32f } * RW;
                var dz = depth * (mt == "stick" ? 0.85f : 1f) * (mt == "cell" ? 1.2f : 1f);
                if (mt == "strip")
                    dz = 0.026f;
                Box(wd, h, dz, 0, my - h / 2 * ca, mz - h / 2 * sa, c, rx: ang);
                if (mt == "cell")
                    Box(wd * 1.04f, h * 0.12f, dz * 0.6f, 0, my - h * 0.55f * ca, mz - h * 0.55f * sa, A, rx: ang, mode: 2);
                if (mt == "strip")
                    for (var i = 0; i < 4; i++)
                    {
                        var k = 0.15f + i * 0.22f;
                        Box(wd * 1.3f, 0.004f, dz * 1.1f, 0, my - h * k * ca, mz - h * k * sa, Named("steel") * 1.2f, rx: ang);
                    }
                else
                    Box(wd * 1.12f, 0.01f, dz * 1.1f, 0, my - h * ca, mz - h * sa, c * 0.75f, rx: ang); // floor plate
                return new Vector3(0, my, mz);
            }
            case "curved":
            {
                var h = (ln > 0 ? ln : 0.19f * size) * ext;
                var (y, z) = (my, mz);
                for (var i = 0; i < 3; i++)
                {
                    var a = ang + 0.1f + i * 0.2f;
                    var (s2, c2) = MathF.SinCos(a);
                    var seg = h / 3;
                    Box(RW * 0.62f, seg * 1.04f, depth, 0, y - seg / 2 * c2, z - seg / 2 * s2, c, rx: a);
                    y -= seg * c2;
                    z -= seg * s2;
                }
                return new Vector3(0, my, mz);
            }
            case "drum":
            {
                var r = cap < 60 || att == "Drum Mag" ? 0.13f : 0.15f;
                Box(0.06f, r, r, 0, my - 0.02f - r * 0.5f, mz, c);
                Box(0.06f, r, r, 0, my - 0.02f - r * 0.5f, mz, c * 0.92f, rx: PI / 4);
                Box(0.066f, r * 0.3f, r * 0.3f, 0, my - 0.02f - r * 0.5f, mz, Metal * 0.6f);
                Box(0.03f, 0.05f, 0.035f, 0, my - 0.012f, mz, c * 0.8f);
                return new Vector3(0, my, mz);
            }
            case "tube":
            {
                var tl = (ln > 0 ? ln : BL * 0.72f) * (att == "Extended Mag" ? 1.25f : 1f);
                tl = MathF.Min(tl, BL * 0.97f);
                Rod(BW * 0.8f, tl, 0, _bore - BW * 0.95f, bstart - tl / 2, Dark * 0.95f);
                Box(BW * 0.9f, BW * 0.9f, 0.01f, 0, _bore - BW * 0.95f, bstart - tl + 0.004f, Metal * 0.6f); // cap
                if (att == "Fast Mag")
                    for (var i = 0; i < 4; i++) // shell carrier on the side
                        Box(0.012f, 0.03f, 0.014f, RW / 2 + 0.006f, -RH * 0.05f, _front + RL * (0.2f + i * 0.17f), ShellRed);
                return new Vector3(0, -RH * 0.5f, _front + RL * 0.3f);
            }
            case "pan":
            {
                var r = 0.12f * MathF.Max(size, 0.8f) * (att == "Extended Mag" ? 1.15f : 1f);
                var py = p.Receiver != "toy" ? RH * 0.62f : RH * 0.68f;
                Box(r, 0.028f, r, 0, py, _mid, c);
                Box(r, 0.028f, r, 0, py, _mid, c * 0.93f, ry: PI / 4);
                Box(r * 0.3f, 0.034f, r * 0.3f, 0, py, _mid, A * 0.9f); // hub
                return new Vector3(0, py - 0.014f, _mid);
            }
            case "topbox":
            {
                var h = 0.034f * (att == "Extended Mag" ? 1.3f : 1f);
                Box(RW * 0.9f, h, RL * 0.62f, 0, RH * 0.52f + h / 2, _front + RL * 0.34f, c);
                Box(RW * 0.92f, h * 0.25f, RL * 0.5f, 0, RH * 0.52f + h * 0.62f, _front + RL * 0.34f, c * 0.7f);
                return new Vector3(0, RH * 0.52f, _front + RL * 0.34f);
            }
            case "side":
            {
                var h = (ln > 0 ? ln : 0.15f * size) * ext;
                Box(h, 0.03f, depth, -RW / 2 - h / 2, _bore, mz, c);
                return new Vector3(-RW / 2, _bore, mz);
            }
            case "beltbox":
            {
                var big = att == "Extended Mag" ? 1.3f : 1f;
                var bw = RW * 1.45f * big;
                var bh = 0.12f * big * Math.Clamp(size, 0.9f, 1.25f);
                Box(bw, bh, 0.14f * big, -RW * 0.35f, my - bh / 2 - 0.005f, mz, c);
                Box(bw * 1.02f, 0.012f, 0.14f * big * 0.8f, -RW * 0.35f, my - 0.008f, mz, c * 0.75f); // lid
                Box(0.03f, 0.016f, 0.05f, -RW * 0.1f, my + 0.006f, mz, Metal * 0.6f); // feed
                for (var i = 0; i < 4; i++)
                    Box(0.012f, 0.008f, 0.02f, -RW * 0.6f, my + 0.01f + i * 0.012f, mz - 0.03f, Named("brass") * 0.9f); // belt
                return new Vector3(0, my, mz);
            }
            case "helical":
            {
                var hl = MathF.Max(BL * 0.55f, 0.15f) * (att == "Extended Mag" ? 1.2f : 1f);
                var hy = _hgBottom - 0.025f;
                Rod(0.05f, hl, 0, hy, _front - hl / 2 + 0.02f, c);
                for (var i = 0; i < 4; i++)
                    Rod(0.053f, 0.006f, 0, hy, _front + 0.02f - hl * (0.15f + i * 0.22f), c * 0.7f);
                return new Vector3(0, hy + 0.025f, _front);
            }
            case "grip":
            {
                // In the pistol grip: only the base plate shows, or a long extension
                var a2 = p.GripAngle * PI / 180;
                var gz = _gripZ + MathF.Sin(a2) * 0.11f;
                var by = -RH * 0.47f - 0.115f;
                if (att == "Extended Mag" || ln > 0)
                {
                    var h = (ln > 0 ? ln : 0f) + (att == "Extended Mag" ? 0.06f : 0f);
                    Box(RW * 0.7f, h, 0.042f, 0, by - h / 2 + 0.005f, gz + MathF.Sin(a2) * h * 0.5f, c, rx: -a2);
                    Box(RW * 0.82f, 0.012f, 0.05f, 0, by - h + 0.004f, gz + MathF.Sin(a2) * h, c * 0.8f, rx: -a2);
                }
                else if (att == "Fast Mag")
                    Box(RW * 1.1f, 0.025f, 0.06f, 0, by, gz, c, rx: -a2);
                else
                    Box(RW, 0.016f, 0.058f, 0, by + 0.005f, gz, c, rx: -a2);
                return new Vector3(0, -RH * 0.47f, _gripZ);
            }
            case "striker":
            {
                var dr = 0.135f * (att == "Extended Mag" ? 1.12f : 1f);
                var dz = _front + 0.065f;
                var dy = _bore - dr * 0.32f;
                Rod(dr, 0.1f, 0, dy, dz, c);
                Rod(dr * 0.4f, 0.106f, 0, dy, dz, Metal * 0.6f); // hub
                Box(0.01f, 0.01f, 0.12f, dr * 0.42f, dy - dr * 0.1f, dz + 0.02f, Metal * 0.7f); // winding lever
                return new Vector3(0, dy, dz);
            }
            case "internal":
                if (att is "Extended Mag" or "Fast Mag")
                {
                    var h = 0.055f * (att == "Extended Mag" ? 1.4f : 1f);
                    Box(RW * 0.6f, h, depth, 0, my - h / 2, mz, Dark * 1.3f);
                }
                else
                    Box(RW * 0.72f, 0.01f, 0.07f, 0, my - 0.004f, mz, Dark * 1.2f); // floor plate
                return new Vector3(0, my, mz);
        }
        // No magazine: where the hand loads the gun
        if (p.Receiver == "revolver")
            return new Vector3(-RH * 0.36f - 0.012f, RH * 0.1f, _cylZ); // left side of the cylinder
        if (p.Receiver == "break")
            return new Vector3(0, _bore + BW * 0.3f, _front - 0.015f); // the breech
        return new Vector3(0, my, mz);
    }

    // ---- Stock ----------------------------------------------------------------------------------

    private void StockPart(string style)
    {
        var att = Att(AttachSlot.Stock);
        var ln = p.StockLength;
        var y = -RH * 0.05f;
        var st = p.Stock;
        var drop = p.StockDrop * PI / 180;
        var back = _back;
        switch (att)
        {
            case "No Stock":
                Box(RW * 0.6f, RH * 0.5f, 0.03f, 0, y, back + 0.015f, Dark);
                return;
            case "Light Stock":
                Box(0.012f, 0.012f, ln, 0, y + RH * 0.3f, back + ln / 2, Metal);
                Box(0.012f, 0.012f, ln, 0, y - RH * 0.3f, back + ln / 2, Metal);
                Box(RW * 0.6f, RH * 1.1f, 0.02f, 0, y, back + ln, Metal * 0.8f);
                return;
            case "Heavy Stock":
                Box(RW * 1.1f, RH * 1.05f, ln, 0, y - 0.005f, back + ln / 2, Metal * 0.7f);
                Box(RW * 0.9f, RH * 0.3f, ln * 0.6f, 0, y + RH * 0.55f, back + ln * 0.55f, Metal * 0.6f, flags: GunPartFlags.Comb);
                Box(RW * 1.15f, RH * 1.15f, 0.03f, 0, y - 0.005f, back + ln, Pad);
                return;
        }
        if (st == "none")
        {
            if (style is not ("slide" or "revolver" or "break" or "tool"))
                Box(RW * 0.7f, RH * 0.6f, 0.02f, 0, y, back + 0.01f, M * 0.8f); // end cap
            return;
        }
        var dy = -MathF.Sin(drop) * ln * 0.5f; // the stock droops by its angle
        switch (st)
        {
            case "fixed":
                Box(RW * 0.6f, RH * 0.45f, ln * 0.6f, 0, y + RH * 0.12f, back + ln * 0.3f, F);
                Box(RW * 0.8f, RH * 1f, ln * 0.5f, 0, y - 0.01f + dy, back + ln * 0.72f, F, rx: drop);
                Box(RW * 0.82f, RH * 1.02f, 0.012f, 0, y - 0.01f + dy * 1.6f, back + ln * 0.97f, Rubber, rx: drop);
                break;
            case "crane":
                Rod(RH * 0.32f, ln * 0.85f, 0, RH * 0.18f, back + ln * 0.42f, Dark * 1.1f); // buffer tube
                Box(RW * 0.75f, RH * 0.95f, ln * 0.42f, 0, y + dy, back + ln * 0.74f, F, rx: drop);
                Box(RW * 0.55f, RH * 0.3f, ln * 0.3f, 0, y + RH * 0.5f + dy, back + ln * 0.72f, F * 0.85f, rx: drop, flags: GunPartFlags.Comb); // cheek weld
                Box(RW * 0.8f, RH * 1f, 0.012f, 0, y + dy * 1.6f, back + ln * 0.96f, Rubber, rx: drop);
                break;
            case "skeleton":
                Box(0.015f, 0.015f, ln, 0, y + RH * 0.25f, back + ln / 2, Dark * 1.3f);
                Box(0.015f, 0.015f, ln * 1.05f, 0, y - RH * 0.3f, back + ln / 2, Dark * 1.3f, rx: 0.08f);
                Box(RW * 0.7f, RH * 0.95f, 0.025f, 0, y, back + ln, Dark * 1.2f);
                break;
            case "folding":
                Box(0.01f, 0.01f, ln * 0.9f, RW * 0.3f, y + RH * 0.2f, back + ln * 0.45f, Dark * 1.4f);
                Box(0.01f, 0.01f, ln * 0.9f, -RW * 0.3f, y + RH * 0.2f, back + ln * 0.45f, Dark * 1.4f);
                Box(RW * 0.7f, RH * 0.9f, 0.015f, 0, y, back + ln * 0.9f, Dark * 1.3f);
                break;
            case "wire":
                Box(0.008f, 0.008f, ln, 0, y + RH * 0.3f, back + ln / 2, Metal * 0.8f);
                Box(0.008f, 0.008f, ln * 0.95f, 0, y - RH * 0.32f, back + ln * 0.48f, Metal * 0.8f);
                Box(0.03f, RH * 0.75f, 0.008f, 0, y, back + ln, Metal * 0.8f);
                break;
            case "wood":
                Box(RW * 0.9f, RH * 0.8f, ln, 0, y - 0.02f + dy, back + ln / 2, F, rx: -0.06f + drop);
                Box(RW * 0.95f, RH * 1.1f, 0.025f, 0, y - 0.03f + dy * 2, back + ln, Dark, rx: drop);
                break;
            case "straight":
                Box(RW * 0.62f, RH * 0.5f, ln * 0.36f, 0, -RH * 0.12f, back + ln * 0.18f, F); // wrist
                Box(RW * 0.8f, RH * 0.95f, ln * 0.7f, 0, -RH * 0.22f + dy * 1.3f, back + ln * 0.62f, F, rx: drop);
                Box(RW * 0.82f, RH * 1f, 0.012f, 0, -RH * 0.22f + dy * 2.6f, back + ln * 0.97f, F * 0.55f, rx: drop);
                break;
            case "thumbhole":
                Box(RW * 0.75f, RH * 0.32f, ln * 0.85f, 0, y + RH * 0.28f, back + ln * 0.5f, F, flags: GunPartFlags.Comb); // comb
                Box(RW * 0.75f, RH * 0.28f, ln * 0.7f, 0, y - RH * 0.55f, back + ln * 0.6f, F, rx: -0.12f); // lower bar
                Box(RW * 0.82f, RH * 1.25f, ln * 0.28f, 0, y - RH * 0.1f, back + ln * 0.86f, F); // butt
                Box(RW * 0.84f, RH * 1.27f, 0.012f, 0, y - RH * 0.1f, back + ln, Rubber);
                break;
            case "chassis":
                Box(RW * 0.5f, RH * 0.35f, ln * 0.9f, 0, y + RH * 0.15f, back + ln * 0.45f, D * 1.1f); // spine
                Box(RW * 0.65f, RH * 0.32f, ln * 0.4f, 0, y + RH * 0.45f, back + ln * 0.6f, D * 1.3f, flags: GunPartFlags.Comb); // cheek riser
                Box(RW * 0.75f, RH * 1.2f, 0.03f, 0, y - RH * 0.15f, back + ln * 0.92f, D * 1.2f); // butt
                Box(0.012f, RH * 0.6f, 0.012f, 0, y - RH * 0.85f, back + ln * 0.8f, Dark * 0.9f); // monopod
                break;
            case "bullpup":
                Box(RW * 1.02f, RH * 0.8f, 0.02f, 0, -RH * 0.12f, back + 0.01f, Rubber);
                if (p.CheekRest)
                    Box(RW * 0.7f, RH * 0.18f, RL * 0.25f, 0, RH * 0.3f, back - RL * 0.15f, F * 0.9f, flags: GunPartFlags.Comb);
                return;
            case "toy":
                Box(RW * 1.1f, RH * 1.15f, ln * 0.6f, 0, y, back + ln * 0.3f, F);
                Box(RW * 1.12f, RH * 0.45f, ln * 0.4f, 0, y, back + ln * 0.5f, A);
                Box(RW * 1f, RH * 1.2f, 0.02f, 0, y, back + ln * 0.62f, M * 0.9f);
                break;
            case "spade":
                // twin spade handles behind the receiver (no shoulder stock)
                Box(0.016f, 0.016f, ln * 0.5f, RW * 0.32f, y, back + ln * 0.25f, Dark * 1.2f);
                Box(0.016f, 0.016f, ln * 0.5f, -RW * 0.32f, y, back + ln * 0.25f, Dark * 1.2f);
                Box(RW * 0.8f, 0.02f, 0.02f, 0, y, back + ln * 0.5f, Dark * 1.2f);
                Box(0.022f, RH * 0.8f, 0.022f, RW * 0.32f, y - RH * 0.1f, back + ln * 0.5f, F);
                Box(0.022f, RH * 0.8f, 0.022f, -RW * 0.32f, y - RH * 0.1f, back + ln * 0.5f, F);
                break;
        }
        if (p.CheekRest && st is not ("crane" or "thumbhole" or "chassis" or "bullpup" or "spade"))
            Box(RW * 0.7f, RH * 0.25f, ln * 0.5f, 0, y + RH * 0.55f, back + ln * 0.55f, F * 0.85f, flags: GunPartFlags.Comb);
        if (p.AccentStyle == "tips")
            Box(RW * 0.84f, RH * 0.3f, 0.014f, 0, y + RH * 0.2f, back + ln * 0.97f, A);
    }

    // ---- Receiver extras ------------------------------------------------------------------------

    private void ReceiverExtras(string style, float top)
    {
        switch (p.Bolt)
        {
            case "side":
                Box(0.055f, 0.011f, 0.011f, RW * 0.5f + 0.026f, _bore, _back - 0.035f, Metal * 0.7f);
                Box(0.022f, 0.022f, 0.022f, RW * 0.5f + 0.055f, _bore - 0.006f, _back - 0.03f, Metal * 0.8f);
                break;
            case "rear":
                Rod(0.018f, 0.035f, 0, _bore, _back + 0.012f, Metal * 0.75f);
                break;
            case "charging":
                Box(RW * 0.4f, 0.008f, 0.02f, 0, top - 0.006f, _back - 0.01f, Dark * 1.4f);
                Box(RW * 1f, 0.008f, 0.01f, 0, top - 0.006f, _back - 0.004f, Dark * 1.4f);
                break;
            case "top":
                Box(0.012f, 0.016f, 0.03f, RW * 0.5f + 0.004f, RH * 0.3f, _front + RL * 0.4f, A * 0.9f);
                break;
        }
        if (p.Hammer)
        {
            var hz = style is not ("break" or "lever") ? _back + 0.004f : _back - 0.012f;
            var hy = style != "revolver" ? top - 0.012f : RH * 0.32f;
            Box(0.009f, 0.022f, 0.012f, 0, hy, hz, Dark * 1.2f, rx: -0.45f);
        }
        if (p.Lever)
        {
            var ly = -RH * 0.5f;
            Box(0.012f, 0.012f, 0.11f, 0, ly - 0.048f, _back - 0.04f, Metal * 0.65f); // loop bottom
            Box(0.012f, 0.045f, 0.012f, 0, ly - 0.025f, _back - 0.095f, Metal * 0.65f); // loop front
            Box(0.012f, 0.03f, 0.012f, 0, ly - 0.03f, _back + 0.012f, Metal * 0.65f); // loop rear
        }
        if (p.Canister)
        {
            Rod(0.05f, 0.12f, 0, -RH * 0.55f, _back + 0.05f, A * 0.85f);
            Box(0.01f, 0.01f, 0.18f, RW * 0.55f, -RH * 0.3f, _back - 0.04f, Dark * 0.6f); // hose
        }
        if (p.SideSaddle)
        {
            Box(0.008f, RH * 0.5f, RL * 0.55f, -RW / 2 - 0.004f, RH * 0.05f, _front + RL * 0.4f, Dark * 1.2f);
            for (var i = 0; i < 4; i++)
                Box(0.012f, RH * 0.38f, 0.016f, -RW / 2 - 0.01f, RH * 0.05f, _front + RL * (0.2f + i * 0.13f), ShellRed);
        }
    }

    // ---- Underbarrel ----------------------------------------------------------------------------

    private Vector3 UnderbarrelPart(float z, float y, float bstart)
    {
        var att = Att(AttachSlot.Underbarrel);
        switch (att)
        {
            case "Vertical Grip":
                Box(0.034f, 0.12f, 0.034f, 0, y - 0.06f, z, Metal * 0.8f);
                Box(0.04f, 0.012f, 0.04f, 0, y - 0.12f, z, Metal * 0.6f);
                return new Vector3(0, y - 0.055f, z);
            case "Angled Grip":
                Box(0.036f, 0.045f, 0.11f, 0, y - 0.025f, z + 0.01f, Metal * 0.8f, rx: -0.45f);
                return new Vector3(0, y - 0.03f, z + 0.01f);
            case "Laser":
                Box(0.035f, 0.035f, 0.07f, 0, y - 0.02f, z, Metal * 0.7f);
                Box(0.014f, 0.014f, 0.004f, 0, y - 0.02f, z - 0.037f, RedGlow, mode: 2);
                Box(0.004f, 0.004f, 6f, 0, y - 0.02f, z - 3.04f, RedGlow, mode: 2, alpha: 0.35f, flags: GunPartFlags.Blend);
                break;
        }
        if (att == null)
        {
            switch (p.Foregrip)
            {
                case "vertical":
                    Box(0.032f, 0.085f, 0.034f, 0, y - 0.0425f, z, F);
                    return new Vector3(0, y - 0.045f, z);
                case "stub":
                    Box(0.03f, 0.04f, 0.03f, 0, y - 0.02f, z, F);
                    break;
                case "folding":
                    Box(0.022f, 0.07f, 0.022f, 0, y - 0.033f, z, Dark * 1.3f, rx: 0.35f);
                    return new Vector3(0, y - 0.04f, z - 0.01f);
                case "strap":
                    Box(RW * 0.6f, 0.008f, 0.06f, 0, y - 0.035f, z, Dark * 1.2f);
                    Box(RW * 0.6f, 0.035f, 0.008f, 0, y - 0.018f, z - 0.03f, Dark * 1.2f);
                    break;
            }
            switch (p.Bipod)
            {
                case "folded":
                    Box(0.008f, 0.008f, 0.15f, 0.02f, y - 0.006f, bstart - BL * 0.62f + 0.075f, Metal * 0.6f);
                    Box(0.008f, 0.008f, 0.15f, -0.02f, y - 0.006f, bstart - BL * 0.62f + 0.075f, Metal * 0.6f);
                    Box(0.05f, 0.014f, 0.02f, 0, y - 0.004f, bstart - BL * 0.62f, Dark * 1.1f);
                    break;
                case "deployed":
                {
                    var by = _bore - BW * 0.5f - 0.01f;
                    var bz = bstart - BL * 0.8f;
                    Box(0.04f, 0.016f, 0.022f, 0, by, bz, Dark * 1.1f);
                    Box(0.009f, 0.16f, 0.009f, 0.02f, by - 0.075f, bz - 0.025f, Metal * 0.6f, rx: 0.3f, rz: -0.12f);
                    Box(0.009f, 0.16f, 0.009f, -0.02f, by - 0.075f, bz - 0.025f, Metal * 0.6f, rx: 0.3f, rz: 0.12f);
                    break;
                }
            }
        }
        if (p.Handguard == "pump")
            return new Vector3(0, y, z);
        if (p.Handguard == "none" && p.Receiver is "slide" or "revolver" or "break" && p.Stock == "none")
            return new Vector3(0, _gripPt.Y - 0.01f, _gripPt.Z - 0.012f); // a handgun: the support hand cups the shooting hand
        return new Vector3(0, y, z);
    }

    // ---- Sights ---------------------------------------------------------------------------------

    private float SightsPart(float top, float bstart)
    {
        var s = p.Sights;
        var pistol = p.Receiver is "slide" or "revolver";
        var rise = pistol ? 0.01f : 0.02f;
        var iron = top + rise;
        const GunPartFlags T = GunPartFlags.Top;
        if (p.CarryHandle)
        {
            const float h = 0.036f;
            var hz0 = _front + RL * 0.12f;
            var hz1 = _front + RL * 0.12f + MathF.Min(0.12f, RL * 0.55f);
            Box(0.014f, h, 0.012f, 0, top + h / 2, hz0, Dark * 1.1f, flags: T);
            Box(0.014f, h, 0.012f, 0, top + h / 2, hz1, Dark * 1.1f, flags: T);
            Box(0.02f, 0.012f, hz1 - hz0 + 0.012f, 0, top + h, (hz0 + hz1) / 2, Dark * 1.1f, flags: T);
            iron = top + h + 0.012f;
        }
        // The sight line must clear everything permanent along the middle of the gun (a top-mounted magazine, a tall
        // receiver hump...): otherwise aiming looks straight into a block. Posts and notches grow to the raised line.
        iron = MathF.Max(iron, LineOfSightFloor() + 0.008f);
        if (s == "none")
            return iron;
        // The front sight sits at the end of the handguard or near the muzzle
        float fz;
        if (p.Receiver == "slide")
            fz = _front + 0.01f;
        else if (pistol)
            fz = bstart - BL + 0.012f;
        else if (p.Handguard is "none" or "forend" or "pump" or "stock" || HL < 0.04f)
            fz = bstart - BL + 0.02f;
        else
            fz = bstart - HL - 0.01f;
        if (p.GasBlock && !pistol)
            fz = bstart - MathF.Min(BL * 0.55f, MathF.Max(p.HandguardLength, 0.1f) + 0.04f);
        var bse = _bore + BW * 0.45f;
        if (s == "bead")
        {
            if (iron - bse > 0.016f)
                Box(0.008f, iron - 0.008f - bse, 0.03f, 0, (iron - 0.008f + bse) / 2, fz - 0.005f, Dark * 1.2f, flags: T); // ramp
            Box(0.01f, 0.01f, 0.01f, 0, iron - 0.005f, fz, p.AccentStyle != "none" ? A : Named("brass"), flags: T);
            return iron;
        }
        var tall = s == "tall";
        var bw = tall ? 0.024f : 0.014f;
        if (iron - bse > 0.004f)
            Box(bw, iron - bse, tall ? 0.016f : 0.01f, 0, (iron + bse) / 2, fz, Dark * 1.1f, flags: T); // post
        if (tall)
            Box(0.026f, 0.008f, 0.02f, 0, iron - 0.004f, fz, Dark * 0.9f, flags: T); // hood
        if (p.CarryHandle)
            return iron;
        var rz = !pistol ? _back - 0.02f : _back - 0.012f;
        if (p.Receiver == "bullpup")
            rz = _front + RL * 0.5f;
        if (s is "aperture" or "ghost")
        {
            Box(0.026f, 0.006f, 0.01f, 0, iron - 0.003f, rz, Dark * 1.1f, flags: T);
            Box(0.006f, iron - top + 0.008f, 0.01f, -0.01f, (iron + top) / 2, rz, Dark * 1.1f, flags: T);
            Box(0.006f, iron - top + 0.008f, 0.01f, 0.01f, (iron + top) / 2, rz, Dark * 1.1f, flags: T);
            if (s == "ghost")
            {
                Box(0.005f, iron - top + 0.014f, 0.014f, -0.017f, (iron + top) / 2 + 0.003f, rz, Dark * 1.2f, flags: T);
                Box(0.005f, iron - top + 0.014f, 0.014f, 0.017f, (iron + top) / 2 + 0.003f, rz, Dark * 1.2f, flags: T);
            }
        }
        else
            Box(!pistol ? 0.03f : 0.026f, iron - top, 0.012f, 0, (iron + top) / 2, rz, Dark * 1.1f, flags: T); // rear notch
        return iron;
    }

    /// <summary>The top of the highest permanent part across the middle of the gun (where the eye looks along it).</summary>
    private float LineOfSightFloor()
    {
        var m = float.NegativeInfinity;
        foreach (var pt in _parts)
        {
            if ((pt.Flags & (GunPartFlags.Top | GunPartFlags.Blend)) != 0)
                continue;
            var (lo, hi) = pt.Bounds();
            if (hi.X > -0.012f && lo.X < 0.012f)
                m = MathF.Max(m, hi.Y);
        }
        return m;
    }

    // ---- Optic mount ----------------------------------------------------------------------------

    /// <summary>The highest permanent part under the rail: the optic's rail sits on it.</summary>
    private float MountHeight(float rw, float rf, float rb, float top)
    {
        var m = top;
        foreach (var pt in _parts)
        {
            if ((pt.Flags & (GunPartFlags.Top | GunPartFlags.Blend)) != 0)
                continue;
            var (lo, hi) = pt.Bounds();
            if (hi.X > -rw / 2 && lo.X < rw / 2 && hi.Z > rf && lo.Z < rb)
                m = MathF.Max(m, hi.Y);
        }
        return m;
    }

    /// <summary>With an optic fitted, nothing on the gun may rise into it: parts near it are trimmed (or lowered) to the mount.</summary>
    private void ClampUnderOptic()
    {
        var z0 = _mesh.OpticZ - 0.38f;
        var z1 = _mesh.OpticZ + 0.27f;
        var lim = _mesh.Mount + 0.001f;
        for (var i = 0; i < _parts.Count; i++)
        {
            var pt = _parts[i];
            var (lo, hi) = pt.Bounds();
            if (hi.Y > lim && hi.Z > z0 && lo.Z < z1 && hi.X > -0.056f && lo.X < 0.056f && (pt.Flags & GunPartFlags.Blend) == 0)
            {
                var ex = hi.Y - lim;
                if (pt.Rx == 0 && pt.Rz == 0 && pt.Ry == 0 && lo.Y < lim - 0.004f)
                {
                    pt.Y -= ex / 2;
                    pt.Sy -= ex;
                }
                else
                    pt.Y -= ex;
                _parts[i] = pt;
            }
        }
    }
}
