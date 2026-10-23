using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Gravity pulling towards one point (a planet's centre) with the same strength everywhere: "up" at a position is the
/// direction away from <see cref="Center"/>. Declared by a map's <c>"gravity": { "type": "point", ... }</c>.
/// </summary>
public sealed class PointGravity : IGravityField
{
    public Vector3 Center { get; }

    /// <summary>Acceleration in m/s² (the standard is <see cref="World.Gravity"/>).</summary>
    public float Strength { get; }

    /// <summary>
    /// The reference axis the tangent frames' "north" (yaw 0) is projected from (unit length). The frame has no north on
    /// the two points of the surface this axis passes through (any continuous tangent frame on a sphere has such points),
    /// so a map puts something solid there.
    /// </summary>
    public Vector3 Axis { get; }

    public PointGravity(Vector3 center, float strength, Vector3 axis)
    {
        Center = center;
        Strength = strength;
        Axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : -Vector3.UnitZ;
    }

    public Vector3 GravityAt(Vector3 position) => -GravityFrames.UpOf(this, position) * Strength;

    public Vector3 UpAt(Vector3 position) => GravityFrames.UpOf(this, position);

    public Vector3 AxisAt(Vector3 position) => Axis;

    public float HeightOf(Vector3 position) => Vector3.Distance(position, Center);

    /// <summary>Fingerprint data: centre, strength and axis (planet maps keep the fingerprint they had: no kind tag).</summary>
    public void Mix(Action<float> mix)
    {
        mix(Center.X); mix(Center.Y); mix(Center.Z);
        mix(Strength);
        mix(Axis.X); mix(Axis.Y); mix(Axis.Z);
    }
}

/// <summary>
/// Local "gravity frames": which way is up at a position and the tangent frame players' yaw and pitch are measured in.
/// <para>
/// On every ordinary map gravity is uniform and the frame is the world itself (right +X, up +Y, yaw 0 facing -Z), and
/// every function here takes an exact shortcut that returns the same numbers the Y-up code always did, so existing maps
/// behave bit for bit as before. On a map with point gravity (a planet), up is away from the centre and the frame's
/// "north" (yaw 0) is the map's reference axis projected onto the ground plane there. On a map with gravity zones
/// (<see cref="ZonedGravity"/>) each position's field decides: a wall zone's up is sideways, a rock's is away from it. Yaw and pitch on the wire, in input
/// commands and in snapshots are relative to that frame, so nothing new is sent: host and clients derive the frame from
/// the position and the map. The local player's look is carried from frame to frame by <see cref="Transport"/> each
/// tick, so walking round the planet never turns the view.
/// </para>
/// Local coordinates (<see cref="ToLocal"/>, <see cref="ToWorld"/>) follow the world convention: x right, y up, -z north.
/// </summary>
public static class GravityFrames
{
    private static IGravityField? _field;
    private static PointGravity? _point;
    private static ZonedGravity? _zoned;

    /// <summary>The current map's gravity field, or null on an ordinary map (plain straight-down gravity; set by <see cref="World.SetMap"/>).</summary>
    public static IGravityField? Field => _field;

    /// <summary>The current map's point gravity when the whole map is a planet, else null (a zoned map's rocks don't count).</summary>
    public static PointGravity? Point => _point;

    /// <summary>The current map's gravity zones, or null (GravityZones.cs).</summary>
    public static ZonedGravity? Zoned => _zoned;

    /// <summary>Is gravity on the current map anything but the plain straight-down field (so "up" must be asked for per position)?</summary>
    public static bool Curved => _field != null;

    /// <summary>Called when the map changes.</summary>
    public static void Use(IGravityField? field)
    {
        _field = field;
        _point = field as PointGravity;
        _zoned = field as ZonedGravity;
    }

    /// <summary>The gravity field for a map (uniform straight down unless it declares one).</summary>
    public static IGravityField FieldOf(Map map) => map.Gravity ?? UniformGravity.Down;

    /// <summary>Up at a position for a given point field (world +Y at the centre itself).</summary>
    public static Vector3 UpOf(PointGravity g, Vector3 p)
    {
        var d = p - g.Center;
        var len = d.Length();
        return len > 1e-5f ? d / len : Vector3.UnitY;
    }

    /// <summary>Up at a position on the current map.</summary>
    public static Vector3 UpAt(Vector3 p) => _field is { } f ? f.UpAt(p) : Vector3.UnitY;

    /// <summary>Is up exactly world +Y at a position (the plain Y-up code paths apply there)?</summary>
    public static bool PlainUpAt(Vector3 p) => _field == null || _field.UpAt(p) == Vector3.UnitY;

    /// <summary>Is up exactly world +Y all along the line from <paramref name="a"/> to <paramref name="b"/> (sampled every 0.6 m)?</summary>
    public static bool PlainUpAlong(Vector3 a, Vector3 b)
    {
        if (_field == null)
            return true;
        if (_zoned == null)
            return _field.UpAt(a) == Vector3.UnitY && _field.UpAt(b) == Vector3.UnitY;
        var n = Math.Clamp((int)MathF.Ceiling(Vector3.Distance(a, b) / 0.6f), 1, 256);
        for (var k = 0; k <= n; k++)
            if (!_zoned.PlainUpAt(Vector3.Lerp(a, b, k / (float)n)))
                return false;
        return true;
    }

    /// <summary>Gravity acceleration at a position (m/s², pointing down).</summary>
    public static Vector3 GravityAt(Vector3 p) => (_field is { } f ? f.GravityAt(p) : new Vector3(0, -World.Gravity, 0)) * World.GravityScale; // (the host's gravity setting)

    /// <summary>
    /// How high a point is: its distance from the centre on a planet map, its y elsewhere. (Clamps and "is it above this"
    /// checks compare heights.) In a zone, the height along the zone's up.
    /// </summary>
    public static float HeightOf(Vector3 p) => _field is { } f ? f.HeightOf(p) : p.Y;

    /// <summary>The tangent frame at a position: right, up and north (the direction yaw 0 faces), all unit length.</summary>
    public static void Frame(Vector3 p, out Vector3 right, out Vector3 up, out Vector3 north)
    {
        if (_field is not { } f)
        {
            right = Vector3.UnitX;
            up = Vector3.UnitY;
            north = -Vector3.UnitZ;
            return;
        }
        FrameOf(f, p, out right, out up, out north);
    }

    /// <summary>The tangent frame at a position in a given field (see <see cref="Frame"/>).</summary>
    public static void FrameOf(IGravityField f, Vector3 p, out Vector3 right, out Vector3 up, out Vector3 north)
    {
        up = f.UpAt(p);
        var axis = f.AxisAt(p);
        var n = axis - up * Vector3.Dot(axis, up);
        var len = n.Length();
        if (len < 1e-4f)
        {
            // On the axis itself (inside whatever the map put there): any north will do, but always the same one.
            var alt = MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            n = alt - up * Vector3.Dot(alt, up);
            len = n.Length();
        }
        north = n / len;
        right = Vector3.Cross(north, up);
    }

    /// <summary>A local direction (x right, y up, -z north) at <paramref name="p"/> in world space.</summary>
    public static Vector3 ToWorld(Vector3 p, Vector3 local)
    {
        if (_field == null)
            return local;
        Frame(p, out var r, out var u, out var n);
        return r * local.X + u * local.Y - n * local.Z;
    }

    /// <summary>A world direction in the local frame at <paramref name="p"/>.</summary>
    public static Vector3 ToLocal(Vector3 p, Vector3 world)
    {
        if (_field == null)
            return world;
        Frame(p, out var r, out var u, out var n);
        return new Vector3(Vector3.Dot(world, r), Vector3.Dot(world, u), -Vector3.Dot(world, n));
    }

    /// <summary>The look direction for a yaw and pitch at a position (<see cref="World.Forward"/> on uniform maps).</summary>
    public static Vector3 Forward(Vector3 p, float yaw, float pitch) =>
        _field == null ? World.Forward(yaw, pitch) : ToWorld(p, World.Forward(yaw, pitch));

    /// <summary>The ground-plane facing for a yaw at a position (<see cref="World.FlatForward"/> on uniform maps).</summary>
    public static Vector3 FlatForward(Vector3 p, float yaw) => _field == null ? World.FlatForward(yaw) : ToWorld(p, World.FlatForward(yaw));

    /// <summary>The ground-plane right for a yaw at a position (<see cref="World.FlatRight"/> on uniform maps).</summary>
    public static Vector3 FlatRight(Vector3 p, float yaw) => _field == null ? World.FlatRight(yaw) : ToWorld(p, World.FlatRight(yaw));

    /// <summary>A point <paramref name="height"/> metres above <paramref name="p"/> (along up).</summary>
    public static Vector3 Above(Vector3 p, float height) => _field is { } f ? p + f.UpAt(p) * height : p + new Vector3(0, height, 0);

    /// <summary>
    /// The yaw and pitch at <paramref name="p"/> that look along <paramref name="dir"/> (any length). The same formulas the
    /// Y-up code uses (atan2(x, -z), asin(y)), in the local frame.
    /// </summary>
    public static (float Yaw, float Pitch) Angles(Vector3 p, Vector3 dir)
    {
        var l = ToLocal(p, dir);
        var len = l.Length();
        if (len < 1e-6f)
            return (0, 0);
        return (MathF.Atan2(l.X, -l.Z), MathF.Asin(Math.Clamp(l.Y / len, -1f, 1f)));
    }

    /// <summary>A yaw towards a world direction at a position (only the part across up counts).</summary>
    public static float YawOf(Vector3 p, Vector3 dir)
    {
        var l = ToLocal(p, dir);
        return MathF.Atan2(l.X, -l.Z);
    }

    /// <summary>
    /// Carries a look (yaw, pitch measured at <paramref name="from"/>) to <paramref name="to"/>: the look direction is turned
    /// by the smallest rotation taking up at one to up at the other (parallel transport along the short way), then measured
    /// in the new frame. Walking round a planet then never turns the view, and the frame's own twist near its axis points
    /// is cancelled. Across a zone boundary the world look direction is kept exactly (the frame under it changes, the view
    /// doesn't). Uniform maps: nothing changes.
    /// </summary>
    public static void Transport(ref float yaw, ref float pitch, Vector3 from, Vector3 to)
    {
        if (_field is not { } f || from == to)
            return;
        var u0 = f.UpAt(from);
        var u1 = f.UpAt(to);
        var dir = Forward(from, yaw, pitch);
        if (_zoned == null)
            dir = Rotate(dir, u0, u1); // a planet: the look rolls with the ground as it curves away
        else if (u0 == u1)
            return;
        var (y, p) = Angles(to, dir);
        yaw = y;
        pitch = Math.Clamp(p, -1.55f, 1.55f);
    }

    /// <summary>
    /// Eases a camera up towards the local up at <paramref name="eye"/>: on a zoned map the field is a step function of
    /// position, so the horizon rolls round over about a quarter of a second instead of snapping (looks only: nothing in the
    /// simulation uses it). Elsewhere it's the local up, exactly.
    /// </summary>
    public static Vector3 SmoothUp(ref Vector3 state, Vector3 eye, float dt)
    {
        var target = UpAt(eye);
        if (_zoned == null || state.LengthSquared() < 0.5f)
            return state = target;
        var dot = Vector3.Dot(state, target);
        if (dot > 0.99999f)
            return state = target;
        var t = 1f - MathF.Exp(-dt * 10f);
        state = World.Slerp(state, target, t);
        return state;
    }

    /// <summary>Turns <paramref name="v"/> by the smallest rotation taking unit <paramref name="a"/> to unit <paramref name="b"/>.</summary>
    public static Vector3 Rotate(Vector3 v, Vector3 a, Vector3 b)
    {
        var axis = Vector3.Cross(a, b);
        var s = axis.Length();
        var c = Vector3.Dot(a, b);
        if (s < 1e-7f)
            return c > 0 ? v : -v; // the same up (or exactly opposite: a half turn)
        axis /= s;
        // Rodrigues' rotation formula.
        return v * c + Vector3.Cross(axis, v) * s + axis * (Vector3.Dot(axis, v) * (1 - c));
    }

    /// <summary>
    /// The rotation taking local coordinates at <paramref name="p"/> into world space, as a matrix (row-vector convention,
    /// like every System.Numerics matrix): a model drawn upright with world axes, times this, stands on the ground there.
    /// Identity on uniform maps.
    /// </summary>
    public static Matrix4x4 Basis(Vector3 p)
    {
        if (_field == null)
            return Matrix4x4.Identity;
        Frame(p, out var r, out var u, out var n);
        return new Matrix4x4(
            r.X, r.Y, r.Z, 0,
            u.X, u.Y, u.Z, 0,
            -n.X, -n.Y, -n.Z, 0,
            0, 0, 0, 1);
    }

    /// <summary>
    /// The matrix that stands something built upright at <paramref name="p"/> (as if gravity were straight down) onto the
    /// ground there: about <paramref name="p"/>, local axes become the frame's. Identity on uniform maps.
    /// </summary>
    public static Matrix4x4 StandAt(Vector3 p) =>
        _field == null ? Matrix4x4.Identity : Matrix4x4.CreateTranslation(-p) * Basis(p) * Matrix4x4.CreateTranslation(p);

    /// <summary>
    /// A world ray in the local frame of a body standing at <paramref name="feet"/>, re-centred so the body stands at
    /// <paramref name="feet"/> upright (for testing upright hit boxes). Uniform maps: unchanged.
    /// </summary>
    public static void RayToLocal(Vector3 feet, ref Vector3 origin, ref Vector3 dir)
    {
        if (_field == null)
            return;
        origin = feet + ToLocal(feet, origin - feet);
        dir = ToLocal(feet, dir);
    }

    /// <summary>A world point re-expressed as if the body standing at <paramref name="feet"/> were upright (see <see cref="RayToLocal"/>).</summary>
    public static Vector3 PointToLocal(Vector3 feet, Vector3 point) => _field == null ? point : feet + ToLocal(feet, point - feet);

    /// <summary>The inverse of <see cref="PointToLocal"/>.</summary>
    public static Vector3 PointToWorld(Vector3 feet, Vector3 local) => _field == null ? local : feet + ToWorld(feet, local - feet);
}

/// <summary>
/// Simple falling bits (gibs, prop debris, hats) on a planet: their "floor" is a height above the centre
/// (<see cref="GravityFrames.HeightOf"/>) found with a ray towards the centre, so the same floor model works on a sphere.
/// </summary>
public static class CurvedFall
{
    /// <summary>The ground's height (distance from the centre) under <paramref name="p"/>, or 50 m below it when nothing's there.</summary>
    public static float FloorUnder(Vector3 p)
    {
        var up = GravityFrames.UpAt(p);
        var d = World.Query.Raycast(p + up * 0.3f, -up, 12f, QueryFilter.All);
        var h = GravityFrames.HeightOf(p);
        return d >= 12f ? h - 50f : h + 0.3f - d;
    }

    /// <summary>
    /// One frame of fall: gravity (times <paramref name="gravityScale"/>) and the move. Returns true when the piece is at or
    /// below its floor, after putting it on the floor (<paramref name="low"/>: half its smallest size); <paramref name="up"/>
    /// is the local up there and <paramref name="into"/> the speed it hit with (positive, along -up).
    /// </summary>
    public static bool Step(ref Vector3 pos, ref Vector3 vel, float floor, float low, float gravityScale, float dt, out Vector3 up, out float into)
    {
        vel += GravityFrames.GravityAt(pos) * (gravityScale * dt);
        pos += vel * dt;
        up = GravityFrames.UpAt(pos);
        into = -Vector3.Dot(vel, up);
        if (GravityFrames.HeightOf(pos) - low > floor)
            return false;
        pos += up * (floor + low - GravityFrames.HeightOf(pos)); // onto the floor along the local up (a planet: centre + up * height)
        return true;
    }

    /// <summary>The rotation that lays a piece resting at <paramref name="at"/> flat on the ground there, keeping its turn about the local up.</summary>
    public static Quaternion LieFlat(Quaternion rot, Vector3 at)
    {
        var fwd = Vector3.Transform(Vector3.UnitX, rot);
        var l = GravityFrames.ToLocal(at, fwd);
        var m = Matrix4x4.CreateRotationY(MathF.Atan2(-l.Z, l.X)) * GravityFrames.Basis(at); // turn, then onto the ground
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}
