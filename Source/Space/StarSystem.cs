namespace CallOfSlop.Space;

/// <summary>
/// One body of a star system: the star (index 0, no parent) or something on rails round a parent. Data only, kept in
/// <see cref="StarSystem.Bodies"/> by index; the parent index is the relationship (docs/space/SPACE.md, "Data").
/// </summary>
public sealed class SpaceBody
{
    public string Name = "";
    /// <summary>The body this one orbits (-1: the star).</summary>
    public int Parent = -1;
    /// <summary>GM in m^3/s^2 (what gravity and every child orbit use).</summary>
    public double Mu;
    /// <summary>Mean radius of the ground (or the star's surface), metres.</summary>
    public double Radius;
    /// <summary>The orbit round <see cref="Parent"/> (unused for the star).</summary>
    public KeplerOrbit Orbit;
    /// <summary>Sidereal rotation period in seconds (0: doesn't turn).</summary>
    public double RotationPeriod;
    /// <summary>Axial tilt (radians): the spin axis leans from the ecliptic's north towards -y about the ecliptic's x.</summary>
    public double Tilt;
    /// <summary>The spin angle at the epoch (radians).</summary>
    public double RotationAtEpoch;
    /// <summary>The map this body is played on, if any (a planetoid map's name).</summary>
    public string? Map;

    /// <summary>Surface gravity, m/s^2.</summary>
    public double SurfaceGravity => Radius > 0 ? Mu / (Radius * Radius) : 0;

    /// <summary>The orientation of the body's inertial frame turned by the tilt (spin axis = local z), before the spin.</summary>
    public QuatD TiltRotation => QuatD.RotX(Tilt);
}

/// <summary>The kinds of frame in a system (the doc's tiers 2 to 5; the galaxy tier places whole systems).</summary>
public enum FrameKind : byte
{
    /// <summary>ECL: the system's inertial frame, centred on the star; z is the ecliptic's north, x zero longitude.</summary>
    Ecliptic,
    /// <summary>ORB: a body's orbit's perifocal frame, centred on its parent; x towards periapsis, z the orbit's normal.</summary>
    Orbit,
    /// <summary>CCE: the ecliptic's axes moved onto a body's centre (inertial: attitudes are stored here).</summary>
    Inertial,
    /// <summary>Body-fixed: centred on a body, z its spin axis, turning with its ground.</summary>
    BodyFixed,
}

/// <summary>A frame in a system: its kind and, for every kind but the ecliptic, the body it belongs to.</summary>
public readonly record struct FrameRef(FrameKind Kind, int Body)
{
    public static readonly FrameRef Ecliptic = new(FrameKind.Ecliptic, 0);
    public override string ToString() => Kind == FrameKind.Ecliptic ? "ECL" : $"{Kind}({Body})";
}

/// <summary>
/// Where a frame is at one instant, in the system's ecliptic frame: its origin (fixed point), its axes (a rotation from
/// the frame's own axes into the ecliptic's), the origin's velocity and the frame's angular velocity (both ecliptic, SI).
/// A position in the frame is <c>Origin + Rot.Rotate(local)</c>.
/// </summary>
public readonly record struct FramePose(SpacePos Origin, QuatD Rot, Vec3d Velocity, Vec3d Spin)
{
    public static readonly FramePose Ecliptic = new(SpacePos.Origin, QuatD.Identity, Vec3d.Zero, Vec3d.Zero);

    /// <summary>A point in this frame as an ecliptic position.</summary>
    public SpacePos ToEcliptic(Vec3d local) => Origin + Rot.Rotate(local);

    /// <summary>An ecliptic position in this frame's coordinates (the subtraction is exact; then one rotation).</summary>
    public Vec3d FromEcliptic(SpacePos p) => Rot.InverseRotate(p - Origin);

    /// <summary>A point in this frame in another frame's coordinates, without rounding to the fixed-point grid.</summary>
    public Vec3d To(in FramePose other, Vec3d local) => other.Rot.InverseRotate((Origin - other.Origin) + Rot.Rotate(local));

    /// <summary>A velocity seen in this frame, at a point in it, as an ecliptic (inertial) velocity: v_frame + w x r + v.</summary>
    public Vec3d VelocityToEcliptic(Vec3d local, Vec3d localVel)
    {
        var r = Rot.Rotate(local);
        return Velocity + Vec3d.Cross(Spin, r) + Rot.Rotate(localVel);
    }

    /// <summary>An ecliptic velocity at an ecliptic position, as seen in this frame.</summary>
    public Vec3d VelocityFromEcliptic(SpacePos p, Vec3d vel)
    {
        var r = p - Origin;
        return Rot.InverseRotate(vel - Velocity - Vec3d.Cross(Spin, r));
    }

    /// <summary>A frame inside this one: <paramref name="offset"/> and <paramref name="rot"/> in this frame's coordinates, not moving in it.</summary>
    public FramePose Child(Vec3d offset, QuatD rot)
    {
        var r = Rot.Rotate(offset);
        return new FramePose(Origin + r, (Rot * rot).Normalized(), Velocity + Vec3d.Cross(Spin, r), Spin);
    }
}

/// <summary>
/// A star system on rails (docs/space/SPACE.md): bodies in parent order (a parent always before its children), every
/// position an exact function of <see cref="SpaceTime"/>. No allocation per query; nothing here is stepped.
/// Loaded from <c>Systems/*.json</c> by <see cref="StarSystemFile"/>.
/// </summary>
public sealed class StarSystem
{
    public string Name = "";
    public string File = "";
    public SpaceBody[] Bodies = [];

    /// <summary>
    /// Where a new game's space clock starts (the file's <c>"start"</c>; default the epoch). A persistent world saves its
    /// own time later; this only places a fresh one (the Dev system starts in the Planetoid's northern summer, so its
    /// polar spawn has the sun up).
    /// </summary>
    public SpaceTime Start;

    /// <summary>The deepest parent chain allowed (star, planet, moon, moon of a moon...).</summary>
    public const int MaxDepth = 8;

    public int Count => Bodies.Length;

    public int IndexOf(string name)
    {
        for (var i = 0; i < Bodies.Length; i++)
            if (Bodies[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>A body's centre at a time, in the ecliptic frame.</summary>
    public SpacePos PositionOf(int body, SpaceTime t)
    {
        var p = SpacePos.Origin;
        for (var b = body; Bodies[b].Parent >= 0; b = Bodies[b].Parent)
            p += Bodies[b].Orbit.PositionAt(t);
        return p;
    }

    /// <summary>A body's centre and velocity at a time, in the ecliptic frame.</summary>
    public void StateOf(int body, SpaceTime t, out SpacePos pos, out Vec3d vel)
    {
        pos = SpacePos.Origin;
        vel = Vec3d.Zero;
        for (var b = body; Bodies[b].Parent >= 0; b = Bodies[b].Parent)
        {
            Bodies[b].Orbit.StateAt(t, out var p, out var v);
            pos += p;
            vel += v;
        }
    }

    /// <summary>A body's spin at a time: its body-fixed axes in the ecliptic's (tilt, then the spin about the tilted z).</summary>
    public QuatD SpinRotation(int body, SpaceTime t)
    {
        var b = Bodies[body];
        var angle = b.RotationAtEpoch;
        if (b.RotationPeriod > 0)
            angle += 2 * Math.PI * KeplerOrbit.PhaseSeconds(t.Ns, b.RotationPeriod) / b.RotationPeriod;
        return (b.TiltRotation * QuatD.RotZ(angle)).Normalized();
    }

    /// <summary>A body's angular velocity (rad/s, ecliptic axes).</summary>
    public Vec3d SpinRate(int body)
    {
        var b = Bodies[body];
        return b.RotationPeriod > 0 ? b.TiltRotation.Rotate(Vec3d.UnitZ) * (2 * Math.PI / b.RotationPeriod) : Vec3d.Zero;
    }

    /// <summary>Where a frame is at a time.</summary>
    public FramePose PoseOf(FrameRef f, SpaceTime t)
    {
        switch (f.Kind)
        {
            case FrameKind.Ecliptic:
                return FramePose.Ecliptic;
            case FrameKind.Orbit:
            {
                var body = Bodies[f.Body];
                if (body.Parent < 0)
                    return FramePose.Ecliptic;
                StateOf(body.Parent, t, out var pp, out var pv);
                return new FramePose(pp, body.Orbit.Rotation, pv, Vec3d.Zero);
            }
            case FrameKind.Inertial:
            {
                StateOf(f.Body, t, out var p, out var v);
                return new FramePose(p, QuatD.Identity, v, Vec3d.Zero);
            }
            case FrameKind.BodyFixed:
            {
                StateOf(f.Body, t, out var p, out var v);
                return new FramePose(p, SpinRotation(f.Body, t), v, SpinRate(f.Body));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(f));
        }
    }

    /// <summary>
    /// The game world's frame on a body's ground (the doc's tier 5, the bubble): its origin on the ground at a latitude,
    /// longitude and height, with the game's axes: +Y up (away from the centre), -Z north (towards the spin axis's
    /// north), +X east. At latitude 90 this matches a planetoid map (Source/Planets): its centre is the game's
    /// (0, -radius, 0). Turns with the ground.
    /// </summary>
    public FramePose BubbleOn(int body, double latitude, double longitude, double height, SpaceTime t)
    {
        var (up, north, east) = SurfaceAxes(latitude, longitude);
        var origin = up * (Bodies[body].Radius + height);
        // Game axes in body-fixed coordinates: x = east, y = up, z = south.
        var rot = QuatD.FromAxes(east, up, -north);
        return PoseOf(new FrameRef(FrameKind.BodyFixed, body), t).Child(origin, rot);
    }

    /// <summary>Up, north and east (body-fixed, unit) at a latitude and longitude (radians). At the poles north is -x / +x.</summary>
    public static (Vec3d Up, Vec3d North, Vec3d East) SurfaceAxes(double latitude, double longitude)
    {
        var (sl, cl) = Math.SinCos(latitude);
        var (so, co) = Math.SinCos(longitude);
        var up = new Vec3d(cl * co, cl * so, sl);
        var east = new Vec3d(-so, co, 0);
        var north = Vec3d.Cross(up, east);
        return (up, north, east);
    }

    /// <summary>The Hill radius of a body (how far its own pull wins over its parent's), metres; infinite for the star.</summary>
    public double HillRadius(int body)
    {
        var b = Bodies[body];
        if (b.Parent < 0)
            return double.PositiveInfinity;
        var parent = Bodies[b.Parent];
        return b.Orbit.A * (1 - b.Orbit.E) * Math.Cbrt(b.Mu / (3 * parent.Mu));
    }

    /// <summary>How many parents up to the star.</summary>
    public int Depth(int body)
    {
        var d = 0;
        for (var b = body; Bodies[b].Parent >= 0; b = Bodies[b].Parent)
            d++;
        return d;
    }
}
