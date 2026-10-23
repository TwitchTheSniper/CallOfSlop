using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a body's blood marks can go this frame: the world frames and boxes of the head, torso and both thighs.</summary>
public struct BloodFrames
{
    public Matrix4x4 Head, Torso, LegR, LegL;
    public Vector3 HeadC, HeadH, TorsoC, TorsoH, LegC, LegH;

    /// <summary>The thighs' long axis in their frame: 1 = Y (zombies), 2 = Z (soldiers' bones point along -Z).</summary>
    public int LegAxis;

    /// <summary>Body facing (radians): turns the shader's noise with the body so ragged edges don't swim as it turns.</summary>
    public float Yaw;
}

/// <summary>
/// Blood marks on players', bots' and zombies' bodies (the "body blood" half of Settings > Video "Blood overlay").
/// Each body shows up to <see cref="BloodMarks.MaxMarks"/> marks, more as its health / <see cref="World.MaxHealth"/>
/// falls (<see cref="BloodMarks.Thresholds"/>), each fading in and back out smoothly as health drops or regenerates.
/// Mark i sits on the body part of the i-th recorded hit (<see cref="Player.Blood"/>, server-written, in every snapshot),
/// at a spot hashed from the owner's id, the blood generation and i, on that part's box surface: so every PC places
/// the same marks in the same places. Zombies (no recorded parts) get marks spread over the torso and head from their id,
/// in a darker zombie palette.
///
/// Cost: no extra draw calls. The marks are spheres in world space handed to the box shader as uniforms right before
/// the body's own draws (<see cref="Renderer.SetBlood"/>): any lit body box a sphere touches paints a ragged, blocky
/// splat in the fragment shader. CPU: a few matrix transforms per marked body, no allocations. Ragdolls keep the final
/// marks as per-box blood amounts in the instanced debris draw (<see cref="RagdollAmounts"/>).
/// </summary>
public static class BodyBloodRender
{
    public static BloodMode Mode = BloodMode.Full;
    public static BloodColour Colour = BloodColour.Red;

    /// <summary>Player blood: bright red or the dark option.</summary>
    public static Vector3 PlayerColor => Colour == BloodColour.Dark ? new Vector3(0.2f, 0.03f, 0.035f) : new Vector3(0.5f, 0.02f, 0.02f);

    /// <summary>Zombie blood: a dark, brownish ichor.</summary>
    public static readonly Vector3 ZombieColor = new(0.2f, 0.11f, 0.035f);

    private static readonly Vector4[] Data = new Vector4[BloodMarks.MaxMarks * 2];
    private static int _count;
    private static Vector3 _color;
    private static float _yaw;

    // Stats (since the last read of Stats)
    private static long _ticks;
    private static int _bodies, _marks, _frames;
    private static double _lastFrame = double.NaN;

    /// <summary>Marks placed for the body about to be drawn.</summary>
    public static int Count => _count;

    /// <summary>The placed marks (two vec4s each: centre + radius, opacity + seed).</summary>
    public static ReadOnlySpan<Vector4> Marks => Data.AsSpan(0, _count * 2);

    /// <summary>Nothing for the next body.</summary>
    public static void Reset() => _count = 0;

    // ---- Placement ------------------------------------------------------------------------------------

    /// <summary>
    /// Places mark spheres for a body into <paramref name="into"/> (two vec4s per mark); returns the number of marks.
    /// Pure and deterministic: the same blood word, owner, health fraction and frames give the same marks on every PC.
    /// </summary>
    public static int Place(uint owner, uint blood, float frac, in BloodFrames f, BloodMode mode, Span<Vector4> into)
    {
        if (mode == BloodMode.Off || frac >= BloodMarks.Thresholds[0])
            return 0;
        var gen = (uint)BloodMarks.Gen(blood);
        var near = Math.Clamp(1f - frac, 0f, 1f); // bigger splats close to death
        var n = 0;
        for (var i = 0; i < BloodMarks.MaxMarks && (n + 1) * 2 <= into.Length; i++)
        {
            if (mode == BloodMode.Subtle && (i & 1) == 1)
                continue; // Subtle: half the marks, fainter and smaller
            var alpha = BloodMarks.MarkAlpha(i, frac);
            if (alpha <= 0.01f)
                continue;
            var part = BloodMarks.MarkPart(blood, owner, i);
            var h = BloodMarks.Hash(owner, gen, (uint)i);
            var u = (h & 0xFF) / 127.5f - 1f;
            var v = ((h >> 8) & 0xFF) / 127.5f - 1f;
            var pick = (int)((h >> 16) & 0xFF);
            var size = (h >> 24) / 255f;
            Vector3 local;
            Matrix4x4 frame;
            var radius = (0.065f + 0.065f * size) * (0.8f + 0.55f * near);
            switch (part)
            {
                case BloodPart.Head:
                    frame = f.Head;
                    local = Surface(f.HeadC, f.HeadH, (pick % 5) switch { 0 or 1 => 5, 2 => 0, 3 => 1, _ => 4 }, u, v * 0.7f);
                    radius *= 0.75f;
                    break;
                case BloodPart.Back:
                    frame = f.Torso;
                    local = Surface(f.TorsoC, f.TorsoH, 4, u, v);
                    break;
                case BloodPart.ArmR or BloodPart.ArmL:
                    frame = f.Torso;
                    local = Surface(f.TorsoC, f.TorsoH, part == BloodPart.ArmR ? 0 : 1, u, 0.25f + 0.35f * (v + 1f));
                    radius *= 0.85f;
                    break;
                case BloodPart.LegR or BloodPart.LegL:
                {
                    frame = part == BloodPart.LegR ? f.LegR : f.LegL;
                    // The four faces around the thigh's long axis.
                    var face = f.LegAxis == 1 ? (pick & 3) switch { 0 => 5, 1 => 0, 2 => 1, _ => 4 } : (pick & 3) switch { 0 => 3, 1 => 0, 2 => 1, _ => 2 };
                    local = Surface(f.LegC, f.LegH, face, u, v);
                    radius *= 0.8f;
                    break;
                }
                default:
                    frame = f.Torso;
                    local = Surface(f.TorsoC, f.TorsoH, 5, u, v);
                    break;
            }
            if (mode == BloodMode.Subtle)
            {
                alpha *= 0.6f;
                radius *= 0.8f;
            }
            var world = Vector3.Transform(local, frame);
            into[n * 2] = new Vector4(world, radius);
            into[n * 2 + 1] = new Vector4(alpha, ((h >> 4) & 0xFFF) / 4095f, 0, 0);
            n++;
        }
        return n;
    }

    /// <summary>A point on face <paramref name="face"/> (0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z) of a box, <paramref name="u"/>, <paramref name="v"/> in -1..1 across it (kept off the edges).</summary>
    public static Vector3 Surface(Vector3 c, Vector3 h, int face, float u, float v)
    {
        u = Math.Clamp(u, -1f, 1f) * 0.8f;
        v = Math.Clamp(v, -1f, 1f) * 0.8f;
        return face switch
        {
            0 => new Vector3(c.X + h.X, c.Y + v * h.Y, c.Z + u * h.Z),
            1 => new Vector3(c.X - h.X, c.Y + v * h.Y, c.Z + u * h.Z),
            2 => new Vector3(c.X + u * h.X, c.Y + h.Y, c.Z + v * h.Z),
            3 => new Vector3(c.X + u * h.X, c.Y - h.Y, c.Z + v * h.Z),
            4 => new Vector3(c.X + u * h.X, c.Y + v * h.Y, c.Z + h.Z),
            _ => new Vector3(c.X + u * h.X, c.Y + v * h.Y, c.Z - h.Z),
        };
    }

    /// <summary>The frames of a soldier body posed by <paramref name="legs"/> under <paramref name="body"/> (feet at the origin, -Z = facing).</summary>
    public static void SoldierFrames(in Matrix4x4 body, LegPose legs, int stance, float yaw, out BloodFrames f)
    {
        var s = BodyModel.Stances[stance];
        var upper = legs.Upper * body;
        var pelvis = legs.PelvisLocal * body;
        var thigh = LegAnimator.LegParts[0];
        f = new BloodFrames
        {
            Head = Matrix4x4.CreateTranslation(s.HeadAt) * upper, HeadC = Vector3.Zero, HeadH = s.HeadSize * 0.5f,
            Torso = s.TorsoFrame * upper, TorsoC = Vector3.Zero, TorsoH = BodyModel.RefTorso * s.TorsoScale * 0.5f,
            LegR = legs.ThighLocal[0] * pelvis, LegL = legs.ThighLocal[1] * pelvis, LegC = thigh.Center, LegH = thigh.Size * 0.5f,
            LegAxis = 2, Yaw = yaw,
        };
    }

    /// <summary>The frames of a zombie (Program.DrawZombie's pose): hip-hinged legs swinging by <paramref name="swing"/>, the hunched upper body.</summary>
    public static void ZombieFrames(in Matrix4x4 at, in Matrix4x4 upper, float swing, float yaw, out BloodFrames f)
    {
        var hips = at; // (a local function can't use an "in" parameter)
        Matrix4x4 Leg(int side) => Matrix4x4.CreateTranslation(0, -0.42f, 0) * Matrix4x4.CreateRotationX(side * swing) * Matrix4x4.CreateTranslation(side * 0.15f, 0.86f, 0) * hips;
        f = new BloodFrames
        {
            Head = upper, HeadC = new Vector3(0, 0.8f, -0.05f), HeadH = new Vector3(0.175f, 0.17f, 0.175f),
            Torso = upper, TorsoC = new Vector3(0, 0.3f, 0), TorsoH = new Vector3(0.31f, 0.31f, 0.18f),
            LegR = Leg(1), LegL = Leg(-1), LegC = Vector3.Zero, LegH = new Vector3(0.11f, 0.425f, 0.12f),
            LegAxis = 1, Yaw = yaw,
        };
    }

    /// <summary>A player's or bot's marks for the body about to be drawn (call <see cref="Bind"/> next). Dead or full health: none.</summary>
    public static void PlacePlayer(Player p, LegPose legs, in Matrix4x4 body, int stance)
    {
        _count = 0;
        if (Mode == BloodMode.Off || !p.Alive)
            return;
        var frac = p.Health / (float)World.MaxHealth;
        if (frac >= BloodMarks.Thresholds[0])
            return;
        var t0 = Stopwatch.GetTimestamp();
        SoldierFrames(body, legs, stance, p.RenderYaw, out var f);
        _count = Place(p.Id, p.Blood, frac, f, Mode, Data);
        _color = PlayerColor;
        _yaw = p.RenderYaw;
        Note(t0);
    }

    /// <summary>A zombie's health as a fraction: the host knows its hit points, clients get a byte.</summary>
    public static float ZombieFrac(Zombie z) => z.MaxHealth > 0 ? z.Health / (float)z.MaxHealth : z.HealthByte / 255f;

    /// <summary>A zombie's marks for the body about to be drawn (call <see cref="Bind"/> next).</summary>
    public static void PlaceZombie(Zombie z, in Matrix4x4 at, in Matrix4x4 upper, float swing)
    {
        _count = 0;
        if (Mode == BloodMode.Off)
            return;
        var frac = z.State == ZState.Dead ? 0f : ZombieFrac(z);
        if (frac >= BloodMarks.Thresholds[0])
            return;
        var t0 = Stopwatch.GetTimestamp();
        ZombieFrames(at, upper, swing, z.RenderYaw, out var f);
        _count = Place(0x10000u + z.Id, 0, frac, f, Mode, Data);
        _color = ZombieColor;
        _yaw = z.RenderYaw;
        Note(t0);
    }

    /// <summary>The frames of a zombie posed by <see cref="ZombieAnim"/>: head and torso frames, the thigh bones (Y along them).</summary>
    public static void ZombieFrames(in ZombiePose p, float yaw, out BloodFrames f) => f = new BloodFrames
    {
        Head = p.Head, HeadC = new Vector3(0, 0.8f, -0.05f), HeadH = new Vector3(0.175f, 0.17f, 0.175f),
        Torso = p.Upper, TorsoC = new Vector3(0, 0.3f, 0), TorsoH = new Vector3(0.31f, 0.31f, 0.18f),
        LegR = p.LegUpR, LegL = p.LegUpL, LegC = Vector3.Zero, LegH = new Vector3(0.105f, (ZombieAnim.LegUp + 0.04f) * 0.5f, 0.115f),
        LegAxis = 1, Yaw = yaw,
    };

    /// <summary>A posed zombie's marks for the body about to be drawn (call <see cref="Bind"/> next).</summary>
    public static void PlaceZombie(Zombie z, in ZombiePose pose, float yaw)
    {
        _count = 0;
        if (Mode == BloodMode.Off)
            return;
        var frac = z.State == ZState.Dead ? 0f : ZombieFrac(z);
        if (frac >= BloodMarks.Thresholds[0])
            return;
        var t0 = Stopwatch.GetTimestamp();
        ZombieFrames(pose, yaw, out var f);
        _count = Place(0x10000u + z.Id, 0, frac, f, Mode, Data);
        _color = ZombieColor;
        _yaw = yaw;
        Note(t0);
    }

    private static void Note(long t0)
    {
        _ticks += Stopwatch.GetTimestamp() - t0;
        if (_count > 0)
        {
            _bodies++;
            _marks += _count;
        }
    }

    /// <summary>Hands the placed marks to the box shader for the next draws (nothing placed: nothing to do).</summary>
    public static void Bind(Renderer g)
    {
        if (_count > 0)
            g.SetBlood(Data, _count, _color, _yaw);
    }

    /// <summary>After the body: the next draws are clean again.</summary>
    public static void Unbind(Renderer g)
    {
        if (_count == 0)
            return;
        g.ClearBlood();
        _count = 0;
    }

    /// <summary>Call once per rendered frame (for the per-frame averages in <see cref="Stats"/>).</summary>
    public static void BeginFrame(double now)
    {
        if (now != _lastFrame)
        {
            _lastFrame = now;
            _frames++;
        }
    }

    /// <summary>One line for the log: bodies and marks per frame, CPU per frame (since the last read).</summary>
    public static string Stats
    {
        get
        {
            var f = Math.Max(_frames, 1);
            var s = $"body blood {Mode}: {_bodies / (double)f:0.0} marked bodies, {_marks / (double)f:0.0} marks per frame, CPU {_ticks * 1e6 / Stopwatch.Frequency / f:0.0} us/frame, 0 extra draw calls";
            _ticks = 0;
            _bodies = _marks = _frames = 0;
            return s;
        }
    }

    // ---- Ragdolls --------------------------------------------------------------------------------------

    /// <summary>
    /// A ragdoll's final marks as a blood amount per body part (indexed by <see cref="BloodPart"/>, 0-1): every visible mark
    /// at <paramref name="frac"/> adds to its part. The ragdoll paints each box with its part's amount.
    /// </summary>
    public static void RagdollAmounts(uint owner, uint blood, float frac, BloodMode mode, Span<float> perPart)
    {
        perPart.Clear();
        if (mode == BloodMode.Off)
            return;
        for (var i = 0; i < BloodMarks.MaxMarks; i++)
        {
            if (mode == BloodMode.Subtle && (i & 1) == 1)
                continue;
            var a = BloodMarks.MarkAlpha(i, frac) * (mode == BloodMode.Subtle ? 0.25f : 0.38f);
            if (a <= 0)
                continue;
            var p = (int)BloodMarks.MarkPart(blood, owner, i);
            if (p < perPart.Length)
                perPart[p] = MathF.Min(1f, perPart[p] + a);
        }
    }
}
