using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Builds every gun out of boxes. Each weapon class has its own silhouette (pistol slide, SMG with
/// folding stock and stick mag, AR with handguard and curved mag, DMR with long barrel and cheek
/// stock, wooden bolt-action sniper with bipod, pump shotgun with tube, chunky LMG with box mag and
/// carry handle, and oddballs for the specials), plus a small library of attachment models that is
/// shared between all guns: 10 optics, 4 muzzle devices, 3 barrels, 3 underbarrel parts, 3 magazine
/// types and 3 stocks. Attachments are drawn in lighter metal so they stand out from the dark body.
///
/// Model space: origin at the grip, -Z towards the muzzle, +Y up, metres.
///
/// Optics are see-through at full ADS: Program sets <see cref="AdsCutaway"/> before drawing the
/// viewmodel, and the optic's housing, tubes and mounts fade out with it (reflex sights keep their
/// thin window frame). The lens, tint and reticle are then drawn flat in the HUD pass by
/// <see cref="ScopeOverlay"/> from <see cref="Look"/>, centred on the screen where the sight line is.
/// Fitting any optic removes the parts it would clash with (iron sights, LMG carry handles).
/// </summary>
public static class GunModel
{
    private static readonly Vector3 Dark = new(0.21f, 0.21f, 0.23f);
    private static readonly Vector3 Metal = new(0.58f, 0.6f, 0.64f); // attachments: noticeably lighter than the gun
    private static readonly Vector3 OpticBody = new(0.36f, 0.38f, 0.42f);
    private static readonly Vector3 Wood = new(0.55f, 0.36f, 0.2f);
    private static readonly Vector3 Polymer = new(0.31f, 0.31f, 0.33f);
    private static readonly Vector3 Tan = new(0.62f, 0.54f, 0.4f);
    private static readonly Vector3 Olive = new(0.33f, 0.36f, 0.24f);
    private static readonly Vector3 RedGlow = new(1f, 0.15f, 0.1f);
    private static readonly Vector3 CyanGlow = new(0.35f, 0.95f, 1f);
    private static readonly Vector3 GreenGlow = new(0.3f, 1f, 0.35f);
    private static readonly Vector3 Rubber = new(0.12f, 0.12f, 0.13f);

    // ---- See-through optics ---------------------------------------------------------

    /// <summary>
    /// 0..1: how much of the optic's housing, tube and mount is cut away (faded out). Program sets it
    /// from <see cref="CutawayFor"/> right before drawing the first-person gun and back to 0 after,
    /// so third-person guns and the gunsmith preview always draw the whole optic. It never moves the
    /// sight line: <see cref="SightHeight"/> and <see cref="AdsZ"/> don't read it.
    /// </summary>
    public static float AdsCutaway;

    /// <summary>The cut-away for an aim-down-sights blend: none at the hip, complete by 85% aimed.</summary>
    public static float CutawayFor(float aimT) => SmoothStep(0.5f, 0.85f, aimT);

    /// <summary>Optics at this zoom or more get the full scope view (black surround, round lens, no viewmodel).</summary>
    public const float FullScopeZoom = 4f;

    /// <summary>True when aiming this gun shows the full scope view instead of the see-through lens.</summary>
    public static bool FullScope(WeaponDef d) => d.AdsZoom >= FullScopeZoom;

    /// <summary>The reticle styles drawn by <see cref="ScopeOverlay"/>.</summary>
    public enum Reticle : byte
    {
        None,
        Dot,
        HoloRing,
        Chevron,
        Horseshoe,
        CircleDot,
        Duplex,
        FineCross,
        Stadia,
        MilDot,
    }

    /// <summary>
    /// What you see through an optic at full ADS. <paramref name="LensRadius"/> is the eyepiece radius as a
    /// fraction of the screen height (0 = an open reflex window: reticle only). <paramref name="Size"/> scales
    /// the reticle (1 = its design size at 1080p). <paramref name="Tint"/> is the faint lens colour.
    /// </summary>
    public readonly record struct OpticLook(float LensRadius, Reticle Reticle, Vector3 Color, float Size, Vector3 Tint);

    private static readonly Vector3 AmberTint = new(1f, 0.82f, 0.55f);
    private static readonly Vector3 CoolTint = new(0.65f, 0.82f, 1f);

    /// <summary>The see-through view for the gun's sight (Reticle.None for plain iron sights).</summary>
    public static OpticLook Look(WeaponDef d) => d.Optic switch
    {
        OpticKind.RedDot => new(0, Reticle.Dot, RedGlow, 1f, Vector3.Zero),
        OpticKind.MiniDot => new(0, Reticle.Dot, GreenGlow, 0.8f, Vector3.Zero),
        OpticKind.Holo => new(0, Reticle.HoloRing, CyanGlow, 1f, Vector3.Zero),
        OpticKind.Prism2 => new(0.42f, Reticle.Horseshoe, new Vector3(1f, 0.32f, 0.1f), 1f, AmberTint),
        OpticKind.Acog => new(0.46f, Reticle.Chevron, RedGlow, 1f, AmberTint),
        OpticKind.Hybrid3 => new(0.44f, Reticle.CircleDot, RedGlow, 1f, AmberTint),
        OpticKind.Scope4 => new(0.47f, Reticle.Duplex, RedGlow, 1f, CoolTint),
        OpticKind.Lpvo6 => new(0.47f, Reticle.Stadia, RedGlow, 1f, CoolTint),
        OpticKind.Scope8 => new(0.47f, Reticle.FineCross, RedGlow, 1f, CoolTint),
        OpticKind.Scope12 => new(0.47f, Reticle.MilDot, RedGlow, 1f, CoolTint),
        _ => HasBuiltInScope(d) ? new OpticLook(0.46f, Reticle.Duplex, RedGlow, 1f, CoolTint) : new OpticLook(0, Reticle.None, Vector3.Zero, 1f, Vector3.Zero),
    };

    private static float SmoothStep(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    // ---- Part recording (self-test) ---------------------------------------------------

    /// <summary>One box of a recorded model, in model space. Optic = part of the optic or its rail; Clear = glass, glow or faded.</summary>
    public readonly record struct Part(Vector3 Min, Vector3 Max, bool Optic, bool Clear);

    private static List<Part>? _record;
    private static bool _inOptic;

    /// <summary>
    /// Builds the gun's box list without drawing anything (for the self-test): every box's model-space
    /// bounds, with the optic's parts flagged. <paramref name="cutaway"/> is the <see cref="AdsCutaway"/> to build with.
    /// </summary>
    public static List<Part> RecordParts(WeaponDef d, float cutaway)
    {
        var keep = AdsCutaway;
        var list = new List<Part>();
        _record = list;
        AdsCutaway = cutaway;
        try
        {
            Draw(null!, d, Matrix4x4.Identity, false);
        }
        finally
        {
            _record = null;
            _inOptic = false;
            AdsCutaway = keep;
        }
        return list;
    }

    /// <summary>The top of the receiver, where an optic's rail or base sits (on the round sniper receiver: above its corners).</summary>
    private static float MountTop(WeaponDef d, Frame f) =>
        d.Class == WeaponClass.Sniper ? f.RecvH * (0.12f + 0.42f * 1.7f * 0.70711f) + 0.001f : f.RecvH * 0.5f;

    /// <summary>The proportions a class is built from.</summary>
    private readonly record struct Frame(float RecvLen, float RecvH, float RecvW, float BarrelLen, float BarrelW, float GuardLen, float GuardH, Vector3 Furniture);

    private static Frame FrameFor(WeaponDef d)
    {
        var L = d.Length;
        var b = d.Bulk;
        return d.Class switch
        {
            WeaponClass.Pistol => new(L * 0.95f, 0.075f * b, 0.05f * b, 0.03f, 0.022f, 0, 0, Polymer),
            WeaponClass.SMG => new(L * 0.55f, 0.09f * b, 0.062f * b, L * 0.28f, 0.026f, L * 0.22f, 0.07f, Polymer),
            WeaponClass.AssaultRifle => new(L * 0.48f, 0.1f * b, 0.068f * b, L * 0.28f, 0.03f, L * 0.42f, 0.085f, d.Accent.X > 0.6f && d.Accent.Y > 0.45f ? Tan : Polymer),
            WeaponClass.DMR => new(L * 0.42f, 0.095f * b, 0.064f * b, L * 0.42f, 0.03f, L * 0.42f, 0.075f, Olive),
            WeaponClass.Sniper => new(L * 0.34f, 0.085f * b, 0.06f * b, L * 0.6f, 0.032f, 0, 0, Wood),
            WeaponClass.Shotgun => new(L * 0.36f, 0.11f * b, 0.07f * b, L * 0.55f, 0.05f, L * 0.3f, 0.07f, Wood),
            WeaponClass.LMG => new(L * 0.46f, 0.13f * b, 0.09f * b, L * 0.3f, 0.04f, L * 0.36f, 0.1f, Olive * 0.9f),
            _ => new(L * 0.5f, 0.1f * b, 0.075f * b, L * 0.3f, 0.035f, L * 0.3f, 0.08f, Polymer),
        };
    }

    private static string? Att(WeaponDef d, AttachSlot slot) => Attachments.Get(slot, Attachments.Slot(d.Kit, slot))?.Name;

    /// <summary>Has a scope built into the gun itself (long-range rifles with no optic attachment).</summary>
    private static bool BuiltInScope(WeaponDef d) => d.Optic == OpticKind.Iron && d.AdsZoom >= 1.8f && d.Class != WeaponClass.Pistol;

    /// <summary>Has a scope built into the gun itself (long-range rifles with no optic attachment).</summary>
    public static bool HasBuiltInScope(WeaponDef d) => BuiltInScope(d);

    /// <summary>Height of the sight line above the grip: the viewmodel puts this at the centre of the screen when aiming.</summary>
    public static float SightHeight(WeaponDef d)
    {
        var f = FrameFor(d);
        var top = f.RecvH * 0.5f;
        var mount = MountTop(d, f);
        return d.Optic switch
        {
            OpticKind.RedDot => mount + 0.035f,
            OpticKind.MiniDot => mount + 0.026f,
            OpticKind.Holo => mount + 0.04f,
            OpticKind.Prism2 => mount + 0.042f,
            OpticKind.Acog => mount + 0.045f,
            OpticKind.Hybrid3 => mount + 0.045f,
            OpticKind.Lpvo6 => mount + 0.05f,
            OpticKind.Scope4 or OpticKind.Scope8 => mount + 0.055f,
            OpticKind.Scope12 => mount + 0.062f,
            _ => BuiltInScope(d) ? top + 0.05f : top + 0.02f,
        };
    }

    /// <summary>Where the optic sits along the gun (model Z), shared by drawing and the aim distance.</summary>
    private static float OpticZ(WeaponDef d)
    {
        var f = FrameFor(d);
        return d.Class == WeaponClass.Pistol ? f.RecvLen * 0.38f - f.RecvLen * 0.35f : -f.RecvLen * 0.12f + 0.01f;
    }

    /// <summary>
    /// How far in front of the eye the gun's grip sits when fully aimed (viewmodel Z, negative = forward).
    /// Iron sights stay at arm's length; optics are brought up so their rear end is a set distance from
    /// the eye. Magnified optics come close, but their housing is cut away at full ADS (see <see cref="AdsCutaway"/>),
    /// so what you see is the HUD lens, not the inside of a tube.
    /// </summary>
    public static float AdsZ(WeaponDef d)
    {
        var z = OpticZ(d);
        float rear, relief;
        switch (d.Optic)
        {
            case OpticKind.RedDot:
                rear = z - 0.012f;
                relief = 0.15f;
                break;
            case OpticKind.MiniDot:
                rear = z - 0.004f;
                relief = 0.12f;
                break;
            case OpticKind.Holo:
                rear = z - 0.0005f;
                relief = 0.13f;
                break;
            case OpticKind.Prism2:
                rear = z + 0.045f;
                relief = 0.06f;
                break;
            case OpticKind.Hybrid3:
                rear = z + 0.071f;
                relief = 0.055f;
                break;
            case OpticKind.Lpvo6:
                rear = z + 0.16f;
                relief = 0.05f;
                break;
            case OpticKind.Scope12:
                rear = z + 0.24f;
                relief = 0.05f;
                break;
            case OpticKind.Acog:
                rear = z + 0.05f;
                relief = 0.055f;
                break;
            case OpticKind.Scope4:
            case OpticKind.Scope8:
                rear = z + 0.26f * (d.Optic == OpticKind.Scope8 ? 1.25f : 1f) * 0.63f;
                relief = 0.05f;
                break;
            default:
                if (BuiltInScope(d))
                {
                    rear = z + 0.26f * 0.85f * 0.63f;
                    relief = 0.05f;
                    break;
                }
                return -0.32f; // irons
        }
        return -relief - rear;
    }

    // ---- Drawing helpers ------------------------------------------------------------

    private static Renderer _g = null!;
    private static Matrix4x4 _pose;

    private static void Box(float sx, float sy, float sz, float x, float y, float z, Vector3 c, float rotX = 0, float rotZ = 0, int mode = 0, float alpha = 1f)
    {
        var m = Matrix4x4.CreateScale(sx, sy, sz);
        if (rotZ != 0)
            m *= Matrix4x4.CreateRotationZ(rotZ);
        if (rotX != 0)
            m *= Matrix4x4.CreateRotationX(rotX);
        m *= Matrix4x4.CreateTranslation(x, y, z);
        if (_record != null)
        {
            Record(m, mode == 2 || alpha < 0.999f);
            return;
        }
        _g.Draw(m * _pose, c, alpha, mode);
    }

    private static void Record(in Matrix4x4 m, bool clear)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var p = Vector3.Transform(new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f), m);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        _record!.Add(new Part(min, max, _inOptic, clear));
    }

    private static void Blend(bool on)
    {
        if (_record == null)
            _g.SetBlend(on);
    }

    /// <summary>An optic housing box: fades out as <see cref="AdsCutaway"/> rises (gone when fully aimed).</summary>
    private static void Shell(float sx, float sy, float sz, float x, float y, float z, Vector3 c, float rotZ = 0, int mode = 0, float alpha = 1f)
    {
        var a = alpha * (1f - AdsCutaway);
        if (a <= 0.02f)
            return;
        if (a >= 0.999f)
        {
            Box(sx, sy, sz, x, y, z, c, rotZ: rotZ, mode: mode);
            return;
        }
        Blend(true);
        if (_record == null)
            _g.SetDepthWrite(false);
        Box(sx, sy, sz, x, y, z, c, rotZ: rotZ, mode: mode, alpha: a);
        if (_record == null)
            _g.SetDepthWrite(true);
        Blend(false);
    }

    /// <summary><see cref="Cyl"/> made of <see cref="Shell"/> boxes.</summary>
    private static void ShellCyl(float r, float len, float x, float y, float z, Vector3 c)
    {
        Shell(r * 1.7f, r * 1.7f, len, x, y, z, c);
        Shell(r * 1.7f, r * 1.7f, len, x, y, z, c * 0.93f, rotZ: MathF.PI / 4);
    }

    /// <summary><see cref="Tube"/> made of <see cref="Shell"/> boxes.</summary>
    private static void ShellTube(float halfInner, float wall, float len, float x, float y, float z, Vector3 c)
    {
        var o = halfInner + wall / 2;
        var w = halfInner * 2 + wall * 2;
        Shell(w, wall, len, x, y + o, z, c);
        Shell(w, wall, len, x, y - o, z, c * 0.9f);
        Shell(wall, w, len, x - o, y, z, c * 0.95f);
        Shell(wall, w, len, x + o, y, z, c * 0.95f);
    }

    /// <summary>A rough cylinder along Z: two boxes, one turned 45 degrees (reads as an octagon).</summary>
    private static void Cyl(float r, float len, float x, float y, float z, Vector3 c)
    {
        Box(r * 1.7f, r * 1.7f, len, x, y, z, c);
        Box(r * 1.7f, r * 1.7f, len, x, y, z, c * 0.93f, rotZ: MathF.PI / 4);
    }

    /// <summary>A hollow square tube along Z (four walls) so you can look straight through it when aiming.</summary>
    private static void Tube(float halfInner, float wall, float len, float x, float y, float z, Vector3 c)
    {
        var o = halfInner + wall / 2;
        var w = halfInner * 2 + wall * 2;
        Box(w, wall, len, x, y + o, z, c);
        Box(w, wall, len, x, y - o, z, c * 0.9f);
        Box(wall, w, len, x - o, y, z, c * 0.95f);
        Box(wall, w, len, x + o, y, z, c * 0.95f);
    }

    // ---- The gun ----------------------------------------------------------------------

    public static void Draw(Renderer g, WeaponDef d, in Matrix4x4 pose, bool flash, float time = 0f)
    {
        _g = g;
        _pose = pose;
        var f = FrameFor(d);
        var top = f.RecvH * 0.5f;
        var recvFront = -f.RecvLen * 0.62f;
        var recvBack = f.RecvLen * 0.38f;
        var barrelY = d.Class == WeaponClass.Pistol ? f.RecvH * 0.15f : f.RecvH * 0.12f;
        var barrel = Att(d, AttachSlot.Barrel);
        var heavy = barrel == "Heavy Barrel";
        var barrelW = f.BarrelW * (heavy ? 1.5f : 1f) * (0.9f + d.Caliber * 0.25f);

        // ---- Body by class -----------------------------------------------------------
        float muzzleZ;
        switch (d.Class)
        {
            case WeaponClass.Pistol:
                muzzleZ = DrawPistol(d, f, top, recvFront, recvBack);
                break;
            case WeaponClass.Shotgun:
                muzzleZ = DrawShotgun(d, f, top, recvFront, recvBack, barrelY, barrelW);
                break;
            case WeaponClass.Sniper:
                muzzleZ = DrawSniper(d, f, top, recvFront, recvBack, barrelY, barrelW);
                break;
            default:
                muzzleZ = DrawRifleFamily(d, f, top, recvFront, recvBack, barrelY, barrelW, heavy, time);
                break;
        }

        // ---- Muzzle device -------------------------------------------------------------
        switch (Att(d, AttachSlot.Muzzle))
        {
            case "Suppressor":
            {
                var len = d.Class == WeaponClass.Pistol ? 0.13f : 0.2f;
                var r = MathF.Max(barrelW * 1.25f, 0.024f);
                Cyl(r, len, 0, barrelY, muzzleZ - len / 2, Metal * 0.75f);
                for (var i = 1; i < 4; i++)
                    Cyl(r * 1.06f, 0.006f, 0, barrelY, muzzleZ - len * i / 4f, Metal * 0.55f); // ribs
                muzzleZ -= len;
                break;
            }
            case "Compensator":
                Box(barrelW * 1.8f, barrelW * 1.6f, 0.06f, 0, barrelY, muzzleZ - 0.03f, Metal);
                for (var i = 0; i < 3; i++)
                    Box(barrelW * 0.9f, barrelW * 0.45f, 0.008f, 0, barrelY + barrelW * 0.6f, muzzleZ - 0.012f - i * 0.016f, Dark); // top ports
                muzzleZ -= 0.06f;
                break;
            case "Muzzle Brake":
                Box(barrelW * 3.2f, barrelW * 1.3f, 0.07f, 0, barrelY, muzzleZ - 0.035f, Metal);
                Box(barrelW * 3.3f, barrelW * 0.4f, 0.012f, 0, barrelY, muzzleZ - 0.02f, Dark);
                Box(barrelW * 3.3f, barrelW * 0.4f, 0.012f, 0, barrelY, muzzleZ - 0.048f, Dark);
                muzzleZ -= 0.07f;
                break;
            case "Choke":
                Cyl(barrelW * 0.75f, 0.05f, 0, barrelY, muzzleZ - 0.025f, Metal);
                Cyl(barrelW * 0.8f, 0.008f, 0, barrelY, muzzleZ - 0.012f, d.Accent);
                muzzleZ -= 0.05f;
                break;
        }

        // ---- Optics --------------------------------------------------------------------
        DrawOptic(d, f, top, recvFront, recvBack);

        // ---- Underbarrel -----------------------------------------------------------------
        var ubZ = d.Class == WeaponClass.Pistol ? recvFront + 0.04f : recvFront - f.GuardLen * 0.45f;
        var ubY = d.Class == WeaponClass.Pistol ? -f.RecvH * 0.15f : -f.GuardH * 0.5f;
        switch (Att(d, AttachSlot.Underbarrel))
        {
            case "Vertical Grip":
                Box(0.034f, 0.12f, 0.034f, 0, ubY - 0.06f, ubZ, Metal * 0.8f);
                Box(0.04f, 0.012f, 0.04f, 0, ubY - 0.12f, ubZ, Metal * 0.6f);
                break;
            case "Angled Grip":
                Box(0.036f, 0.045f, 0.11f, 0, ubY - 0.025f, ubZ + 0.01f, Metal * 0.8f, rotX: -0.45f);
                break;
            case "Laser":
            {
                Box(0.035f, 0.035f, 0.07f, 0, ubY - 0.02f, ubZ, Metal * 0.7f);
                Box(0.014f, 0.014f, 0.004f, 0, ubY - 0.02f, ubZ - 0.037f, RedGlow, mode: 2);
                // The beam: a long, faint emissive sliver.
                Blend(true);
                Box(0.004f, 0.004f, 6f, 0, ubY - 0.02f, ubZ - 3.04f, RedGlow, mode: 2, alpha: 0.35f);
                Blend(false);
                break;
            }
        }

        // ---- Muzzle flash ------------------------------------------------------------------
        if (flash && _record == null)
        {
            _g.SetBlend(true);
            var size = 0.08f + d.Caliber * 0.12f + (d.Pellets > 1 ? 0.06f : 0);
            if (d.Suppressor)
                size *= 0.4f;
            var color = d.Sound == SoundStyle.Zap ? CyanGlow : new Vector3(1f, 0.85f, 0.3f);
            _g.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationZ(time * 50f) * Matrix4x4.CreateTranslation(0, barrelY, muzzleZ - size * 0.4f) * _pose, color,
                d.Sound == SoundStyle.Suppressed ? 0.3f : 0.9f, mode: 2);
            _g.SetBlend(false);
        }
    }

    // ---- Classes ------------------------------------------------------------------------

    private static float DrawPistol(WeaponDef d, Frame f, float top, float front, float back)
    {
        var revolver = d.Mag <= 6;
        // Frame + grip
        Box(f.RecvW * 0.9f, f.RecvH * 0.45f, f.RecvLen * 0.85f, 0, -f.RecvH * 0.25f, (front + back) / 2 + 0.01f, f.Furniture);
        Box(f.RecvW * 0.95f, 0.12f, 0.055f, 0, -f.RecvH * 0.5f - 0.05f, back - 0.02f, f.Furniture, rotX: -0.25f);
        Box(f.RecvW * 0.6f, 0.02f, 0.035f, 0, -f.RecvH * 0.5f - 0.005f, back - f.RecvLen * 0.3f, Dark); // trigger guard
        if (revolver)
        {
            // Cylinder and a long round barrel
            Cyl(f.RecvH * 0.42f, 0.05f, 0, f.RecvH * 0.08f, back - f.RecvLen * 0.45f, Metal * 0.7f);
            Box(f.RecvW * 0.7f, f.RecvH * 0.5f, f.RecvLen * 0.75f, 0, f.RecvH * 0.2f, front + f.RecvLen * 0.3f, Dark * 1.2f);
            if (d.Optic == OpticKind.Iron)
                Box(0.01f, 0.016f, 0.012f, 0, top - 0.01f, front + 0.01f, d.Accent); // front sight (off when an optic is fitted)
        }
        else
        {
            // Slide with an accent stripe and serrations
            Box(f.RecvW, f.RecvH * 0.55f, f.RecvLen, 0, f.RecvH * 0.22f, (front + back) / 2, Dark * 1.3f);
            Box(f.RecvW * 1.02f, f.RecvH * 0.1f, f.RecvLen * 0.7f, 0, f.RecvH * 0.32f, (front + back) / 2 - 0.01f, d.Accent);
            for (var i = 0; i < 4; i++)
                Box(f.RecvW * 1.03f, f.RecvH * 0.35f, 0.004f, 0, f.RecvH * 0.2f, back - 0.012f - i * 0.01f, Dark);
            if (d.Optic == OpticKind.Iron) // optics-ready slide: the iron sights come off with a sight fitted
            {
                Box(0.012f, 0.012f, 0.01f, 0, top + 0.004f, front + 0.012f, Dark); // front sight
                Box(0.03f, 0.012f, 0.01f, 0, top + 0.004f, back - 0.01f, Dark); // rear sight
            }
        }
        // Magazine
        var mag = Att(d, AttachSlot.Magazine);
        var baseY = -f.RecvH * 0.5f - 0.11f;
        if (mag == "Extended Mag")
            Box(f.RecvW * 0.8f, 0.07f, 0.045f, 0, baseY - 0.03f, back - 0.045f, d.Accent * 0.8f, rotX: -0.25f);
        else if (mag == "Fast Mag")
            Box(f.RecvW * 1.1f, 0.025f, 0.06f, 0, baseY, back - 0.005f, d.Accent, rotX: -0.25f);
        else
            Box(f.RecvW, 0.016f, 0.058f, 0, baseY + 0.005f, back - 0.005f, d.Accent * 0.9f, rotX: -0.25f);
        return front - (revolver ? 0.05f : 0.01f);
    }

    private static float DrawRifleFamily(WeaponDef d, Frame f, float top, float front, float back, float barrelY, float barrelW, bool heavy, float time)
    {
        var cls = d.Class;
        var special = cls == WeaponClass.Special;
        var body = special && d.Sound == SoundStyle.Toy ? d.Accent : Dark;

        // Receiver (upper + lower), grip, trigger guard
        Box(f.RecvW, f.RecvH * 0.55f, f.RecvLen, 0, f.RecvH * 0.2f, (front + back) / 2, body);
        Box(f.RecvW * 0.92f, f.RecvH * 0.45f, f.RecvLen * 0.8f, 0, -f.RecvH * 0.28f, (front + back) / 2 + f.RecvLen * 0.05f, special && d.Sound == SoundStyle.Toy ? Vector3.One * 0.9f : f.Furniture);
        Box(f.RecvW * 0.8f, 0.11f, 0.05f, 0, -f.RecvH * 0.5f - 0.045f, back - f.RecvLen * 0.2f, f.Furniture * 1.1f, rotX: -0.3f);
        Box(f.RecvW * 0.5f, 0.012f, 0.05f, 0, -f.RecvH * 0.5f - 0.004f, back - f.RecvLen * 0.4f, Dark);

        // Handguard
        var guardFront = front - f.GuardLen;
        if (f.GuardLen > 0)
        {
            var gc = cls == WeaponClass.DMR || cls == WeaponClass.LMG ? f.Furniture : Dark * 1.25f;
            Box(f.RecvW * 0.9f, f.GuardH, f.GuardLen, 0, f.RecvH * 0.05f, front - f.GuardLen / 2, gc);
            if (cls is WeaponClass.AssaultRifle or WeaponClass.SMG)
                for (var i = 0; i < 4; i++) // vent holes / rail slots
                    Box(f.RecvW * 0.92f, f.GuardH * 0.25f, f.GuardLen * 0.1f, 0, f.RecvH * 0.05f, front - f.GuardLen * (0.2f + i * 0.2f), Dark * 0.7f);
            if (cls == WeaponClass.LMG)
                for (var i = 0; i < 5; i++) // perforated shroud
                    Box(f.RecvW * 0.94f, f.GuardH * 0.3f, f.GuardLen * 0.08f, 0, f.RecvH * 0.12f, front - f.GuardLen * (0.12f + i * 0.18f), Dark * 0.6f);
        }

        // Barrel (or a rotary cluster / energy coils)
        var barrelLen = f.BarrelLen;
        if (d.Name.Contains("Rotary"))
        {
            for (var i = 0; i < 6; i++)
            {
                var a = i * MathF.Tau / 6 + time * 0;
                Box(0.016f, 0.016f, barrelLen + f.GuardLen, MathF.Cos(a) * 0.028f, barrelY + MathF.Sin(a) * 0.028f, guardFront + f.GuardLen / 2 - barrelLen / 2, Metal * 0.6f);
            }
        }
        else
        {
            Box(barrelW, barrelW, barrelLen, 0, barrelY, guardFront - barrelLen / 2, Dark * 0.75f);
            if (heavy)
                for (var i = 0; i < 3; i++)
                    Box(barrelW * 1.15f, barrelW * 0.3f, barrelLen * 0.2f, 0, barrelY, guardFront - barrelLen * (0.2f + i * 0.28f), Metal * 0.5f); // fluting
        }
        if (special && d.Sound == SoundStyle.Zap)
            for (var i = 0; i < 4; i++)
                Box(barrelW * 2.4f, barrelW * 2.4f, 0.014f, 0, barrelY, guardFront - barrelLen * (0.15f + i * 0.22f), CyanGlow, mode: 2);
        if (special && d.Sound == SoundStyle.Pneumatic)
        {
            Cyl(0.035f, 0.12f, 0, -f.RecvH * 0.2f, back + 0.03f, d.Accent); // air canister
            Box(0.012f, 0.012f, 0.2f, f.RecvW * 0.6f, -f.RecvH * 0.1f, back - 0.05f, Dark * 0.6f); // hose
        }

        // Front sight post (when using irons)
        if (d.Optic == OpticKind.Iron && !BuiltInScope(d))
        {
            Box(0.01f, 0.03f, 0.01f, 0, top + 0.012f, guardFront + 0.02f, Dark);
            Box(0.03f, 0.016f, 0.012f, 0, top + 0.008f, back - 0.02f, Dark); // rear notch
        }

        // Carry handle on LMGs (it comes off when an optic is fitted, leaving the rail)
        if (cls == WeaponClass.LMG)
        {
            if (d.Optic == OpticKind.Iron)
            {
                Box(0.014f, 0.035f, 0.012f, 0, top + 0.015f, front + 0.03f, Dark);
                Box(0.014f, 0.035f, 0.012f, 0, top + 0.015f, front + 0.12f, Dark);
                Box(0.018f, 0.012f, 0.11f, 0, top + 0.035f, front + 0.075f, Dark);
            }
            // Bipod (folded back unless something's mounted there)
            if (Att(d, AttachSlot.Underbarrel) == null)
            {
                Box(0.008f, 0.008f, 0.16f, 0.02f, -f.GuardH * 0.4f, guardFront + 0.1f, Metal * 0.6f);
                Box(0.008f, 0.008f, 0.16f, -0.02f, -f.GuardH * 0.4f, guardFront + 0.1f, Metal * 0.6f);
            }
        }

        DrawMagazine(d, f, front, back);
        DrawStock(d, f, back, cls switch
        {
            WeaponClass.SMG => StockStyle.Folding,
            WeaponClass.DMR => StockStyle.Cheek,
            WeaponClass.LMG => StockStyle.Skeleton,
            WeaponClass.Special => d.Sound == SoundStyle.Toy ? StockStyle.Toy : StockStyle.Fixed,
            _ => StockStyle.Fixed,
        });
        return guardFront - barrelLen;
    }

    private static float DrawShotgun(WeaponDef d, Frame f, float top, float front, float back, float barrelY, float barrelW)
    {
        var doubleBarrel = d.Name.Contains("Double");
        var lever = d.Name.Contains("Lever");
        Box(f.RecvW, f.RecvH * 0.7f, f.RecvLen, 0, f.RecvH * 0.1f, (front + back) / 2, Dark * 1.1f);
        Box(f.RecvW * 0.75f, 0.11f, 0.05f, 0, -f.RecvH * 0.5f - 0.035f, back - 0.02f, f.Furniture, rotX: -0.35f);
        var barrelLen = f.BarrelLen;
        if (doubleBarrel)
        {
            Cyl(barrelW * 0.5f, barrelLen, -barrelW * 0.48f, barrelY, front - barrelLen / 2, Dark * 0.75f);
            Cyl(barrelW * 0.5f, barrelLen, barrelW * 0.48f, barrelY, front - barrelLen / 2, Dark * 0.75f);
            Box(f.RecvW * 0.9f, 0.04f, barrelLen * 0.45f, 0, barrelY - barrelW * 0.6f, front - barrelLen * 0.25f, f.Furniture); // forend
        }
        else
        {
            Cyl(barrelW * 0.5f, barrelLen, 0, barrelY + 0.005f, front - barrelLen / 2, Dark * 0.75f);
            var mag = Att(d, AttachSlot.Magazine);
            var tubeLen = barrelLen * (mag == "Extended Mag" ? 0.95f : 0.75f);
            if (d.Mag >= 10 && mag != "Extended Mag")
            {
                Box(0.07f, 0.13f, 0.13f, 0, -f.RecvH * 0.5f - 0.06f, front + 0.04f, d.Accent * 0.8f); // drum (Street Sweeper)
                Box(0.07f, 0.13f, 0.13f, 0, -f.RecvH * 0.5f - 0.06f, front + 0.04f, d.Accent * 0.72f, rotX: MathF.PI / 4);
            }
            else
                Cyl(barrelW * 0.4f, tubeLen, 0, barrelY - barrelW * 0.95f, front - tubeLen / 2, Dark * 0.9f); // magazine tube
            // Pump (or a lever loop)
            if (!lever)
                Box(f.RecvW * 0.95f, 0.05f, f.GuardLen, 0, barrelY - barrelW * 0.95f, front - f.GuardLen / 2 - 0.03f, f.Furniture * 1.15f);
        }
        if (lever)
            Box(0.012f, 0.03f, 0.08f, 0, -f.RecvH * 0.5f - 0.03f, back - 0.07f, Metal * 0.6f);
        if (d.Optic == OpticKind.Iron)
            Box(0.012f, 0.012f, 0.012f, 0, barrelY + barrelW * 0.6f, front - barrelLen + 0.01f, d.Accent); // bead sight
        DrawStock(d, f, back, StockStyle.Wood);
        return front - barrelLen;
    }

    private static float DrawSniper(WeaponDef d, Frame f, float top, float front, float back, float barrelY, float barrelW)
    {
        // Round receiver, bolt handle, long free-floated barrel, full wooden stock with forend
        Cyl(f.RecvH * 0.42f, f.RecvLen, 0, f.RecvH * 0.12f, (front + back) / 2, Dark * 1.15f);
        Box(0.06f, 0.012f, 0.012f, f.RecvW * 0.7f, f.RecvH * 0.12f, back - 0.03f, Metal * 0.7f); // bolt handle
        Box(0.02f, 0.02f, 0.02f, f.RecvW * 0.7f + 0.035f, f.RecvH * 0.08f, back - 0.03f, Metal * 0.8f); // bolt knob
        var barrelLen = f.BarrelLen;
        Box(barrelW * 0.75f, barrelW * 0.75f, barrelLen, 0, barrelY, front - barrelLen / 2, Dark * 0.75f);
        // Stock: one long wooden piece under the receiver and the first half of the barrel
        var wood = d.Name.Contains("Musket") ? Wood * 1.1f : f.Furniture;
        Box(f.RecvW * 1.05f, f.RecvH * 0.55f, f.RecvLen + barrelLen * 0.45f, 0, -f.RecvH * 0.2f, (back + front - barrelLen * 0.45f) / 2, wood);
        Box(f.RecvW * 0.8f, 0.1f, 0.05f, 0, -f.RecvH * 0.45f - 0.04f, back - 0.02f, wood, rotX: -0.4f); // pistol grip
        // Bipod legs folded under the forend (unless an underbarrel attachment is there)
        if (Att(d, AttachSlot.Underbarrel) == null && !d.Name.Contains("Musket"))
        {
            Box(0.008f, 0.008f, 0.14f, 0.022f, -f.RecvH * 0.48f, front - barrelLen * 0.35f, Metal * 0.6f);
            Box(0.008f, 0.008f, 0.14f, -0.022f, -f.RecvH * 0.48f, front - barrelLen * 0.35f, Metal * 0.6f);
        }
        if (d.Mag > 1)
            DrawMagazine(d, f, front, back);
        DrawStock(d, f, back, StockStyle.Wood);
        return front - barrelLen;
    }

    // ---- Shared parts ---------------------------------------------------------------------

    private static void DrawMagazine(WeaponDef d, Frame f, float front, float back)
    {
        var mag = Att(d, AttachSlot.Magazine);
        var magZ = front + f.RecvLen * 0.22f;
        var magY = -f.RecvH * 0.5f;
        var color = d.Class == WeaponClass.Special && d.Sound == SoundStyle.Toy ? Vector3.One * 0.85f : d.Accent * 0.85f;
        var size = Math.Clamp(d.Mag / 30f, 0.5f, 1.4f);

        if (d.Class == WeaponClass.LMG)
        {
            // Box magazine hanging under/left of the receiver, plus a belt
            var big = mag == "Drum Mag" ? 1.35f : mag == "Extended Mag" ? 1.18f : 1f;
            Box(f.RecvW * 1.5f * big, 0.12f * big, 0.14f * big, -f.RecvW * 0.35f, magY - 0.06f * big, magZ, color);
            Box(0.03f, 0.01f, 0.05f, 0, magY + 0.01f, magZ, Metal * 0.6f);
            return;
        }
        switch (mag)
        {
            case "Drum Mag":
                // Round drum standing sideways (axis along X)
                Box(0.06f, 0.13f, 0.13f, 0, magY - 0.09f, magZ, color);
                Box(0.06f, 0.13f, 0.13f, 0, magY - 0.09f, magZ, color * 0.92f, rotX: MathF.PI / 4);
                Box(0.066f, 0.04f, 0.04f, 0, magY - 0.09f, magZ, Metal * 0.6f);
                Box(0.03f, 0.05f, 0.035f, 0, magY - 0.02f, magZ, color * 0.8f);
                return;
            case "Fast Mag":
                // Two mags clamped side by side
                Box(f.RecvW * 0.55f, 0.16f * size, 0.04f, -f.RecvW * 0.3f, magY - 0.08f * size, magZ, color, rotX: d.Class == WeaponClass.AssaultRifle ? 0.2f : 0);
                Box(f.RecvW * 0.55f, 0.16f * size, 0.04f, f.RecvW * 0.3f, magY - 0.08f * size, magZ, color * 0.9f, rotX: d.Class == WeaponClass.AssaultRifle ? 0.2f : 0);
                Box(f.RecvW * 1.25f, 0.02f, 0.045f, 0, magY - 0.06f, magZ, Metal); // clamp
                return;
        }
        var ext = mag == "Extended Mag" ? 1.55f : 1f;
        if (d.Class == WeaponClass.AssaultRifle)
        {
            // Curved: two segments
            var h = 0.09f * size * ext;
            Box(f.RecvW * 0.65f, h, 0.05f, 0, magY - h * 0.5f, magZ, color, rotX: 0.12f);
            Box(f.RecvW * 0.65f, h, 0.05f, 0, magY - h * 1.4f, magZ - h * 0.35f, color, rotX: 0.45f);
        }
        else
        {
            var h = (d.Class == WeaponClass.SMG ? 0.17f : 0.11f) * size * ext;
            Box(f.RecvW * 0.6f, h, 0.04f, 0, magY - h * 0.5f, magZ, color);
        }
    }

    private enum StockStyle
    {
        Fixed,
        Folding,
        Cheek,
        Skeleton,
        Wood,
        Toy,
    }

    private static void DrawStock(WeaponDef d, Frame f, float back, StockStyle style)
    {
        var stock = Att(d, AttachSlot.Stock);
        var len = d.Class == WeaponClass.Sniper || d.Class == WeaponClass.Shotgun ? 0.26f : 0.22f;
        var y = -f.RecvH * 0.05f;
        switch (stock)
        {
            case "No Stock":
                Box(f.RecvW * 0.6f, f.RecvH * 0.5f, 0.03f, 0, y, back + 0.015f, Dark); // end cap
                return;
            case "Light Stock":
                // Skeletal: two thin rails and a butt plate
                Box(0.012f, 0.012f, len, 0, y + f.RecvH * 0.3f, back + len / 2, Metal);
                Box(0.012f, 0.012f, len, 0, y - f.RecvH * 0.3f, back + len / 2, Metal);
                Box(f.RecvW * 0.6f, f.RecvH * 1.1f, 0.02f, 0, y, back + len, Metal * 0.8f);
                return;
            case "Heavy Stock":
                Box(f.RecvW * 1.1f, f.RecvH * 1.05f, len, 0, y - 0.005f, back + len / 2, Metal * 0.7f);
                Box(f.RecvW * 0.9f, f.RecvH * 0.3f, len * 0.6f, 0, y + f.RecvH * 0.55f, back + len * 0.55f, Metal * 0.6f); // cheek rest
                Box(f.RecvW * 1.15f, f.RecvH * 1.15f, 0.03f, 0, y - 0.005f, back + len, new Vector3(0.35f, 0.12f, 0.1f)); // rubber pad
                return;
        }
        switch (style)
        {
            case StockStyle.Folding:
                Box(0.01f, 0.01f, len * 0.9f, f.RecvW * 0.3f, y + f.RecvH * 0.2f, back + len * 0.45f, Dark * 1.4f);
                Box(0.01f, 0.01f, len * 0.9f, -f.RecvW * 0.3f, y + f.RecvH * 0.2f, back + len * 0.45f, Dark * 1.4f);
                Box(f.RecvW * 0.7f, f.RecvH * 0.9f, 0.015f, 0, y, back + len * 0.9f, Dark * 1.3f);
                break;
            case StockStyle.Cheek:
                Box(f.RecvW * 0.85f, f.RecvH * 0.9f, len, 0, y - 0.01f, back + len / 2, f.Furniture);
                Box(f.RecvW * 0.7f, f.RecvH * 0.25f, len * 0.5f, 0, y + f.RecvH * 0.55f, back + len * 0.5f, f.Furniture * 0.85f);
                break;
            case StockStyle.Skeleton:
                Box(0.015f, 0.015f, len, 0, y + f.RecvH * 0.25f, back + len / 2, Dark * 1.3f);
                Box(0.015f, 0.015f, len * 1.05f, 0, y - f.RecvH * 0.3f, back + len / 2, Dark * 1.3f, rotX: 0.08f);
                Box(f.RecvW * 0.7f, f.RecvH * 0.95f, 0.025f, 0, y, back + len, Dark * 1.2f);
                break;
            case StockStyle.Wood:
                Box(f.RecvW * 0.9f, f.RecvH * 0.8f, len, 0, y - 0.02f, back + len / 2, f.Furniture, rotX: -0.06f);
                Box(f.RecvW * 0.95f, f.RecvH * 1.1f, 0.025f, 0, y - 0.03f, back + len, Dark);
                break;
            case StockStyle.Toy:
                Box(f.RecvW * 1.2f, f.RecvH * 1.2f, len * 0.7f, 0, y, back + len * 0.35f, Vector3.One * 0.9f);
                break;
            default:
                // Fixed rifle stock: sloped tube + butt
                Box(f.RecvW * 0.6f, f.RecvH * 0.45f, len * 0.6f, 0, y + f.RecvH * 0.12f, back + len * 0.3f, f.Furniture);
                Box(f.RecvW * 0.8f, f.RecvH * 1.0f, len * 0.5f, 0, y - 0.01f, back + len * 0.72f, f.Furniture);
                break;
        }
    }

    private static void DrawOptic(WeaponDef d, Frame f, float top, float front, float back)
    {
        _inOptic = true;
        var z = OpticZ(d);
        var hasOptic = d.Optic != OpticKind.Iron;
        var mount = hasOptic ? MountTop(d, f) : top;
        if (hasOptic && d.Class != WeaponClass.Pistol)
            Box(f.RecvW * 0.5f, 0.008f, f.RecvLen * 0.8f, 0, mount + 0.004f, (front + back) / 2, Dark * 1.5f); // rail
        var sight = SightHeight(d);
        var cut = AdsCutaway;
        switch (d.Optic)
        {
            case OpticKind.RedDot:
            {
                // Open hoop with a glowing dot on the glass (the hoop is already a thin rim, so it stays)
                Box(0.034f, 0.012f, 0.05f, 0, mount + 0.008f, z, OpticBody);
                Box(0.006f, 0.04f, 0.012f, -0.017f, sight - 0.002f, z - 0.018f, OpticBody);
                Box(0.006f, 0.04f, 0.012f, 0.017f, sight - 0.002f, z - 0.018f, OpticBody);
                Box(0.04f, 0.006f, 0.012f, 0, sight + 0.019f, z - 0.018f, OpticBody);
                Blend(true);
                Box(0.028f, 0.028f, 0.002f, 0, sight, z - 0.018f, new Vector3(0.6f, 0.9f, 0.7f), mode: 2, alpha: 0.12f); // glass
                Blend(false);
                Shell(0.004f, 0.004f, 0.002f, 0, sight, z - 0.019f, RedGlow, mode: 2); // the HUD draws the dot at full ADS
                break;
            }
            case OpticKind.MiniDot:
            {
                // Micro reflex: a low plate, a thin square hood and a tiny green dot
                Box(0.03f, 0.012f, 0.03f, 0, mount + 0.006f, z, OpticBody * 0.9f);
                Box(0.004f, 0.028f, 0.008f, -0.013f, sight - 0.001f, z - 0.008f, OpticBody);
                Box(0.004f, 0.028f, 0.008f, 0.013f, sight - 0.001f, z - 0.008f, OpticBody);
                Box(0.03f, 0.004f, 0.008f, 0, sight + 0.013f, z - 0.008f, OpticBody);
                Box(0.006f, 0.004f, 0.008f, 0.012f, mount + 0.014f, z + 0.008f, Metal * 0.7f); // brightness button
                Blend(true);
                Box(0.022f, 0.022f, 0.0015f, 0, sight, z - 0.008f, new Vector3(0.6f, 0.9f, 0.7f), mode: 2, alpha: 0.1f); // glass
                Blend(false);
                Shell(0.003f, 0.003f, 0.002f, 0, sight, z - 0.009f, GreenGlow, mode: 2);
                break;
            }
            case OpticKind.Holo:
            {
                // Boxy housing with a big rectangular window; the window's hood thins to a rim when aiming
                Box(0.05f, 0.018f, 0.075f, 0, mount + 0.01f, z, OpticBody);
                var hood = float.Lerp(0.035f, 0.008f, cut);
                Tube(0.022f, 0.006f, hood, 0, sight, z - 0.0355f + hood / 2, OpticBody * 1.1f);
                Box(0.012f, 0.014f, 0.02f, 0.03f, mount + 0.015f, z + 0.02f, OpticBody * 0.8f); // battery cap
                Blend(true);
                Box(0.042f, 0.042f, 0.002f, 0, sight, z - 0.034f, CyanGlow * 0.6f, mode: 2, alpha: 0.12f);
                Blend(false);
                break;
            }
            case OpticKind.Prism2:
            {
                // Stubby square prism: riser, short hollow body, front lip, rubber eyecup, illumination knob
                Shell(0.036f, 0.014f, 0.06f, 0, mount + 0.007f, z, OpticBody * 0.85f);
                Shell(0.028f, 0.006f, 0.05f, 0, mount + 0.016f, z, OpticBody * 0.8f);
                ShellTube(0.017f, 0.007f, 0.07f, 0, sight, z, OpticBody);
                ShellTube(0.019f, 0.004f, 0.012f, 0, sight, z - 0.041f, OpticBody * 1.1f);
                ShellTube(0.018f, 0.005f, 0.01f, 0, sight, z + 0.04f, Rubber);
                Shell(0.02f, 0.008f, 0.03f, 0, sight + 0.028f, z, OpticBody * 0.9f);
                Shell(0.01f, 0.016f, 0.016f, 0.029f, sight, z + 0.005f, Metal * 0.7f);
                Shell(0.034f, 0.034f, 0.002f, 0, sight, z - 0.046f, new Vector3(1f, 0.8f, 0.5f) * 0.5f, mode: 2, alpha: 0.25f); // amber glass
                break;
            }
            case OpticKind.Acog:
            {
                // Short prism scope: hollow tube with a wider objective, a top housing and a red fibre
                Shell(0.026f, 0.03f, 0.05f, 0, mount + 0.015f, z, OpticBody * 0.9f);
                ShellTube(0.016f, 0.006f, 0.12f, 0, sight, z - 0.01f, OpticBody);
                ShellTube(0.021f, 0.006f, 0.03f, 0, sight, z - 0.075f, OpticBody * 0.9f);
                Shell(0.006f, 0.006f, 0.06f, 0, sight + 0.024f, z, RedGlow, mode: 2);
                break;
            }
            case OpticKind.Hybrid3:
            {
                // Tall reflex up front, a 3x magnifier on a flip mount behind it
                Shell(0.03f, 0.024f, 0.04f, 0, mount + 0.012f, z - 0.04f, OpticBody * 0.85f);
                ShellTube(0.016f, 0.005f, 0.03f, 0, sight, z - 0.045f, OpticBody);
                Shell(0.004f, 0.004f, 0.002f, 0, sight, z - 0.05f, RedGlow, mode: 2);
                Shell(0.03f, 0.02f, 0.03f, 0, mount + 0.01f, z + 0.03f, OpticBody * 0.8f); // magnifier mount
                Shell(0.008f, 0.026f, 0.026f, 0.022f, sight - 0.008f, z + 0.03f, Metal * 0.65f); // flip hinge
                ShellTube(0.014f, 0.005f, 0.06f, 0, sight, z + 0.03f, OpticBody * 0.95f);
                ShellTube(0.016f, 0.004f, 0.012f, 0, sight, z + 0.065f, Rubber);
                break;
            }
            case OpticKind.Lpvo6:
            {
                // Slim variable scope: small objective, big eyepiece with a throw lever, low capped turrets
                const float len = 0.22f, r = 0.017f;
                Shell(0.02f, sight - mount, 0.014f, 0, (mount + sight) / 2, z - len * 0.22f, OpticBody * 0.8f); // rings
                Shell(0.02f, sight - mount, 0.014f, 0, (mount + sight) / 2, z + len * 0.22f, OpticBody * 0.8f);
                ShellCyl(r, len, 0, sight, z, OpticBody * 0.85f);
                ShellCyl(r * 1.25f, len * 0.12f, 0, sight, z - len * 0.5f, OpticBody * 0.8f); // objective
                ShellCyl(r * 1.45f, 0.05f, 0, sight, z + len * 0.5f + 0.025f, OpticBody * 0.78f); // eyepiece
                Shell(0.008f, 0.03f, 0.008f, r * 1.4f, sight + 0.018f, z + len * 0.5f + 0.01f, Metal * 0.75f); // throw lever
                Shell(0.016f, 0.012f, 0.016f, 0, sight + r * 1.35f, z, Metal * 0.65f); // capped turrets
                Shell(0.012f, 0.016f, 0.016f, r * 1.35f, sight, z, Metal * 0.65f);
                Shell(0.012f, 0.018f, 0.018f, -r * 1.35f, sight, z + 0.01f, d.Accent); // illumination dial
                Shell(r * 2.4f, r * 2.4f, 0.002f, 0, sight, z - len * 0.56f, CyanGlow * 0.4f, mode: 2); // lens glint
                break;
            }
            case OpticKind.Scope12:
            {
                // Long-range scope: huge objective bell, long tube, tall target turrets, side focus wheel, three rings
                const float len = 0.36f, r = 0.02f;
                for (var i = -1; i <= 1; i++)
                    Shell(0.022f, sight - mount, 0.016f, 0, (mount + sight) / 2, z + i * len * 0.24f, OpticBody * 0.8f);
                ShellCyl(r, len, 0, sight, z, OpticBody * 0.85f);
                ShellCyl(r * 1.4f, 0.03f, 0, sight, z - len * 0.5f - 0.015f, OpticBody * 0.82f); // bell taper
                ShellCyl(r * 1.8f, 0.09f, 0, sight, z - len * 0.5f - 0.075f, OpticBody * 0.8f); // objective bell
                ShellCyl(r * 1.3f, 0.06f, 0, sight, z + len * 0.5f + 0.03f, OpticBody * 0.8f); // eyepiece
                Shell(0.026f, 0.034f, 0.026f, 0, sight + r * 1.6f, z, Metal * 0.7f); // elevation turret
                Shell(0.03f, 0.026f, 0.026f, r * 1.6f, sight, z, Metal * 0.7f); // windage turret
                Shell(0.006f, 0.05f, 0.05f, -r * 1.7f, sight, z - 0.01f, Metal * 0.6f); // side focus wheel
                Shell(r * 3.2f, r * 3.2f, 0.002f, 0, sight, z - len * 0.5f - 0.121f, CyanGlow * 0.4f, mode: 2); // lens glint
                break;
            }
            case OpticKind.Scope4:
            case OpticKind.Scope8:
                DrawScope(d.Optic == OpticKind.Scope8 ? 1.25f : 1f, mount, sight, z);
                break;
            default:
                if (BuiltInScope(d))
                    DrawScope(0.85f, top, sight, z);
                break;
        }
        _inOptic = false;
    }

    /// <summary>Full scope: rings, long tube, objective bell, eyepiece and turrets (all cut away when fully aimed).</summary>
    private static void DrawScope(float k, float top, float sight, float z)
    {
        var len = 0.26f * k;
        var r = 0.02f * k;
        Shell(0.02f, sight - top, 0.016f, 0, (top + sight) / 2, z - len * 0.25f, OpticBody * 0.8f); // rings
        Shell(0.02f, sight - top, 0.016f, 0, (top + sight) / 2, z + len * 0.25f, OpticBody * 0.8f);
        ShellCyl(r, len, 0, sight, z, OpticBody * 0.85f);
        ShellCyl(r * 1.6f, len * 0.22f, 0, sight, z - len * 0.55f, OpticBody * 0.8f); // objective bell
        ShellCyl(r * 1.35f, len * 0.16f, 0, sight, z + len * 0.55f, OpticBody * 0.8f); // eyepiece
        Shell(0.018f * k, 0.022f * k, 0.018f * k, 0, sight + r * 1.5f, z, Metal * 0.7f); // elevation turret
        Shell(0.022f * k, 0.018f * k, 0.018f * k, r * 1.5f, sight, z, Metal * 0.7f); // windage turret
        Shell(r * 2.2f, r * 2.2f, 0.002f, 0, sight, z - len * 0.66f, CyanGlow * 0.4f, mode: 2); // lens glint
    }
}
