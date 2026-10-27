using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Where a shot is SEEN to start: the tracer and the muzzle flash leave the muzzle of the barrel that fired
/// (<see cref="ShotRay.Barrel"/>; a rotary receiver's turning barrel, one receiver of a rotating grip, every barrel of a
/// volley), on the gun as it is drawn. The hit ray itself still starts at the eye (moved sideways by the barrel's
/// offset on a built gun, <see cref="ShotRays"/>), so the crosshair stays the truth; the tracer runs from the muzzle to
/// where that ray ended.
///
/// - Your own shots: the view model's muzzle (<see cref="ViewGun"/>, set by Program every frame), put into the world at
///   the spot on screen where it is drawn (<see cref="GunMuzzles.ViewToWorld"/>; the view model has its own field of
///   view). Manning a push cart: its gun in the world.
/// - Everyone else's: the gun in their hands as last drawn (<see cref="ThirdPersonArms.DrawnGun"/>), or where it rests
///   before the arm animation when it hasn't been drawn (<see cref="ThirdPersonArms.RestGun"/>). Msg.Shot carries each
///   end's barrel (protocol 61) so every client picks the same barrel the server fired.
/// </summary>
public sealed partial class Game
{
    /// <summary>The first-person gun as last drawn (Program sets it after the view model each frame).</summary>
    public struct FirstPersonGun
    {
        /// <summary>The weapon drawn (null: none drawn yet).</summary>
        public WeaponDef? Def;

        /// <summary>Gun space -> view model camera space, or gun space -> world when <see cref="InWorld"/>.</summary>
        public Matrix4x4 Pose;

        /// <summary>The world camera's camera -> world matrix and tan(fov / 2) for that frame.</summary>
        public Matrix4x4 InvView;

        public float TanHalfFov;

        /// <summary>The gun is drawn in the world (a manned push cart), not as the view model.</summary>
        public bool InWorld;

        /// <summary>When it was drawn (the frame clock).</summary>
        public double At;
    }

    /// <summary>Your gun as Program last drew it (for your tracers).</summary>
    public FirstPersonGun ViewGun;

    /// <summary>How long a third-person muzzle flash shows (the same as your own, <see cref="MuzzleFlashUntil"/>).</summary>
    public const double ShotFlashTime = 0.05;

    private readonly ulong[] _shotFlashMask = new ulong[256];
    private readonly double[] _shotFlashAt = InitFlashTimes();

    private static double[] InitFlashTimes()
    {
        var t = new double[256];
        Array.Fill(t, -100.0);
        return t;
    }

    /// <summary>
    /// The barrels (bit i: barrel i, mod 64) of player <paramref name="id"/>'s last shot if it was fired within
    /// <paramref name="window"/> seconds, else 0: where the muzzle flash shows.
    /// </summary>
    public ulong FlashBarrels(byte id, double now, double window = ShotFlashTime)
    {
        var age = now - _shotFlashAt[id];
        return age >= 0 && age < window ? _shotFlashMask[id] : 0;
    }

    /// <summary>A tracer of <paramref name="def"/> fired from <paramref name="barrel"/>: where it starts, and the flash it makes there.</summary>
    internal Vector3 TracerStart(Player p, WeaponDef def, int barrel, double now)
    {
        if (_shotFlashAt[p.Id] != now)
        {
            _shotFlashAt[p.Id] = now;
            _shotFlashMask[p.Id] = 0; // a new shot
        }
        _shotFlashMask[p.Id] |= 1UL << (barrel & 63);
        return MuzzlePoint(p, def, barrel, now);
    }

    /// <summary>The world position of barrel <paramref name="barrel"/>'s muzzle on <paramref name="p"/>'s gun as it is drawn (see the class summary).</summary>
    public Vector3 MuzzlePoint(Player p, WeaponDef def, int barrel, double now)
    {
        var local = GunMuzzles.Local(def, barrel);
        if (p.Id == LocalId && ViewGun.Def is { } vd && ReferenceEquals(vd, def) && now - ViewGun.At is >= -0.25 and < 0.25)
        {
            var at = Vector3.Transform(local, ViewGun.Pose);
            return ViewGun.InWorld ? at : GunMuzzles.ViewToWorld(at, ViewGun.InvView, ViewGun.TanHalfFov);
        }
        return Vector3.Transform(local, ThirdPersonArms.DrawnGun(p.Id, def, now, out var pose) ? pose : ThirdPersonArms.RestGun(p));
    }

    /// <summary>Msg.Shot after the ends (and the wall origin): a count, then each end's barrel (only built guns with several barrels).</summary>
    private void WriteShotBarrels(WeaponDef def, List<int> barrels)
    {
        if (def.MuzzlePoints is not { Length: > 1 })
            return;
        _w.U8((byte)barrels.Count);
        foreach (var b in barrels)
            _w.U8((byte)Math.Clamp(b, 0, 255));
    }

    private byte[]? _shotBarrelsIn;

    /// <summary>Reads what <see cref="WriteShotBarrels"/> wrote (all barrel 0 when it isn't there).</summary>
    private ReadOnlySpan<byte> ReadShotBarrels(ref PacketReader r, int ends)
    {
        var into = _shotBarrelsIn ??= new byte[256];
        Array.Clear(into, 0, Math.Min(ends, into.Length));
        if (r.Has(1))
        {
            int n = r.U8();
            for (var i = 0; i < n && r.Has(1); i++)
            {
                var b = r.U8();
                if (i < into.Length)
                    into[i] = b;
            }
        }
        return into.AsSpan(0, Math.Min(ends, into.Length));
    }
}
