using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The penal colony's convict labourers on screen (docs/story/LABOUR.md; the gangs: Game/GameLabour.cs). Each person is
/// the players' own body (<see cref="BodyModel"/>'s torso and head, the legs' boxes) in a Ministry uniform, posed by a
/// short script per kind of work from the server tick, so every PC sees the same shovel come up at the same moment. No
/// AI, no physics, no foot rays per frame: the ground under each spot and along each way is found once (and again a
/// few seconds later, when the constructs have arrived). Up close the full body with mittened arms and its tool; past
/// <see cref="FullRange"/> a body of eight boxes; past <see cref="FarRange"/>, or off screen, nothing. Nothing allocates
/// once the figures are built (they are rebuilt only when the gangs change).
/// </summary>
public static class LabourRender
{
    public const int MaxFigures = LabourScript.MaxPeople + Game.LabourScenesMax * 4;
    public const float DetailRange = 8f, FullRange = 13f, MidRange = 32f, FarRange = 110f, ToolRange = 60f, HeardRange = 16f;
    public const float EveryFrame = 9f; // m: nearer than this a figure is posed every frame
    public const double NearEvery = 1 / 45.0, MidEvery = 1 / 30.0, FarEvery = 1 / 15.0; // s between poses up close, in the middle distance and far off
    public const float Walk = 0.95f, Stride = 1.3f; // m/s and m per gait cycle (two steps)

    private enum Role : byte { Inmate, Guard, Caller, Escort }

    private sealed class Figure
    {
        public int Scene, Slot;
        public Role Role;
        public float Rand, Rand2;
        public Vector3 Pos, Fwd; // standing figures: the ground under them and the way they face
        public bool Grounded;
        public readonly BodyParts Torso = new(), Head = new();
        public HatDef? Hat;
        public LegColors Legs;
        public ArmColors Arms;
        public Vector3 Skin, Cap;
        public int Beat = -1; // the last cycle a sound was made on
        public byte Extra; // 1 head bandage, 2 arm in a sling, 3 trusty's flat cap
        // The boxes of its last pose (world), drawn again until it is posed again.
        public readonly Matrix4x4[] M = new Matrix4x4[BoxCap];
        public readonly Vector3[] C = new Vector3[BoxCap];
        public int N;
        public byte Lod; // 0 not posed yet, 1 far, 2 mid, 3 near, 4 near with details
        public double PosedAt;
        public Vector3 Drawn;
    }

    private const int BoxCap = 96;
    private static Figure? _rec; // the figure being posed: its boxes are kept

    /// <summary>A way along the ground, sampled every <see cref="WayStep"/> m (both ends kept).</summary>
    private sealed class Way
    {
        public Vector3[] At = [];
        public float Length;
        public bool Loop;
        public int Count;
    }

    private const float WayStep = 0.5f;

    private static readonly Figure[] Figures = new Figure[MaxFigures];
    private static int _count;
    private static readonly Way?[] Ways = new Way?[Game.LabourScenesMax];
    private static readonly Vector3[] SceneUp = new Vector3[Game.LabourScenesMax], SceneRight = new Vector3[Game.LabourScenesMax], SceneFwd = new Vector3[Game.LabourScenesMax];
    private static int _version = -1;
    private static Game? _built;
    private static double _builtAt, _groundAgain;
    private static bool _alarm;
    private static double _alarmAt, _paused;
    private static readonly Vector4[] Planes = new Vector4[6];
    private static float _flick;

    /// <summary>The last frame's counts (the perf window and the check): figures drawn up close, far, and boxes.</summary>
    public static int DrawnNear, DrawnFar, Boxes;

    // ---- Colours ----
    private static readonly Vector3 Steel = new(0.42f, 0.43f, 0.45f), Wood = new(0.45f, 0.33f, 0.2f), DarkWood = new(0.3f, 0.22f, 0.14f);
    private static readonly Vector3 Snow = new(0.9f, 0.92f, 0.96f), Coal = new(0.09f, 0.09f, 0.1f), Rock = new(0.44f, 0.4f, 0.36f), RockDark = new(0.34f, 0.31f, 0.28f);
    private static readonly Vector3 Barrow = new(0.3f, 0.36f, 0.27f), Rust = new(0.4f, 0.24f, 0.15f), Ember = new(1f, 0.45f, 0.12f), Paint = new(0.96f, 0.96f, 0.93f);
    private static readonly Vector3 Bandage = new(0.88f, 0.86f, 0.8f), Paper = new(0.9f, 0.88f, 0.8f), Board = new(0.32f, 0.24f, 0.16f);

    /// <summary>Draws every labourer the camera can see, at this frame's server tick (nothing when the game has no gangs).</summary>
    private static long _dbgTicks, _dbgPose; private static int _dbgN, _dbgPoses, _dbgBoxes;
    public static void Draw(Renderer g, Game? game)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        g.BeginBatch();
        DrawInner(g, game);
        _dbgTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        g.EndBatch();
        _dbgBoxes += Boxes;
        if (++_dbgN == 200)
        {
            var us = 1e6 / System.Diagnostics.Stopwatch.Frequency;
            Console.WriteLine($"[labour] DEBUG draw {_dbgTicks * us / _dbgN:0.0} us a frame, of which posing {_dbgPose * us / _dbgN:0.0} us ({_dbgPoses / (double)_dbgN:0.0} poses, {_dbgPose * us / Math.Max(_dbgPoses, 1):0.00} us each); the rest {(_dbgTicks - _dbgPose) * us / _dbgN:0.0} us for {_dbgBoxes / (double)_dbgN:0} boxes; near {DrawnNear} far {DrawnFar}");
            _dbgTicks = _dbgPose = 0;
            _dbgN = _dbgPoses = _dbgBoxes = 0;
        }
    }

    private static void DrawInner(Renderer g, Game? game)
    {
        DrawnNear = DrawnFar = Boxes = 0;
        if (game == null || game.LabourCount == 0 || !game.LabourOf(World.Map))
            return;
        if (!ReferenceEquals(_built, game) || _version != game.LabourVersion)
            Build(game);
        var raw = game.MoverRenderTick / World.TickRate;
        if (raw - _builtAt > _groundAgain)
            Ground(game, raw);
        // The colony's alarm: everyone stops where they are (the clock stands), the inmates kneel, hands on heads.
        var alarm = game.ColonyAlarmOn;
        if (alarm && !_alarm)
            _alarmAt = raw;
        else if (!alarm && _alarm)
            _paused += raw - _alarmAt;
        _alarm = alarm;
        var t = (alarm ? _alarmAt : raw) - _paused;
        _flick = (float)(raw % 1000.0);
        SetFrustum(g.ViewProj);
        var cam = g.CameraPosition;
        for (var i = 0; i < _count; i++)
        {
            var f = Figures[i];
            ref readonly var s = ref game.LabourScenes[f.Scene];
            // Posed again only so often past arm's length (the boxes are kept and drawn again in between).
            var d2 = Vector3.DistanceSquared(f.Drawn, cam);
            var lod = d2 < DetailRange * DetailRange ? (byte)4 : d2 < FullRange * FullRange ? (byte)3 : d2 < MidRange * MidRange ? (byte)2 : (byte)1;
            var every = d2 < EveryFrame * EveryFrame ? 0 : lod >= 3 ? NearEvery : lod == 2 ? MidEvery : FarEvery;
            var pose = f.Lod != lod || raw - f.PosedAt >= every || f.N == 0;
            Vector3 pos = default, fwd = default;
            float moving = 0, phase = 0;
            if (pose)
            {
                Place(f, s, t, out pos, out fwd, out moving, out phase);
                f.Drawn = pos;
                d2 = Vector3.DistanceSquared(pos, cam);
            }
            if (d2 > FarRange * FarRange || !Visible(f.Drawn + SceneUp[f.Scene], s.Kind == LabourKind.Barrow ? 2.2f : 1.4f))
            {
                f.N = 0; // (posed afresh when it comes into view)
                continue;
            }
            if (pose)
            {
                var tp = System.Diagnostics.Stopwatch.GetTimestamp();
                _dbgPoses++;
                var up = SceneUp[f.Scene];
                var right = Vector3.Normalize(Vector3.Cross(fwd, up));
                var w = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -fwd.X, -fwd.Y, -fwd.Z, 0, pos.X, pos.Y, pos.Z, 1);
                var p = PoseOf(f, s, t, moving, phase, alarm);
                f.N = 0;
                _rec = f;
                if (lod >= 2)
                {
                    f.Torso.ShowDetail = f.Head.ShowDetail = lod == 4;
                    DrawFull(g, f, s, w, p, right, mid: lod == 2);
                }
                else
                    DrawFar(g, f, w, p, d2 < ToolRange * ToolRange);
                _rec = null;
                f.PosedAt = f.Lod == 0 ? raw - f.Rand * every : raw; // (the first time: spread over the interval, so they don't all pose on one frame)
                f.Lod = lod;
                _dbgPose += System.Diagnostics.Stopwatch.GetTimestamp() - tp;
            }
            for (var b = 0; b < f.N; b++)
                g.Draw(f.M[b], f.C[b]);
            Boxes += f.N;
            if (lod >= 2)
            {
                DrawnNear++;
                if (d2 < HeardRange * HeardRange && !alarm)
                    Sound(game, f, s, t, f.Drawn, MathF.Sqrt(d2));
            }
            else
                DrawnFar++;
        }
        for (var k = 0; k < game.LabourCount; k++)
            DrawProps(g, game.LabourScenes[k], k, cam);
    }

    // ---- Building the figures ----------------------------------------------------------------------------------------

    private static void Build(Game game)
    {
        _built = game;
        _version = game.LabourVersion;
        _count = 0;
        _builtAt = game.MoverRenderTick / World.TickRate;
        _groundAgain = 0;
        _alarm = false;
        _paused = 0;
        var inmate = Uniforms.Find("ministry_inmate");
        var hiVis = Uniforms.Find("ministry_high_vis");
        var trusty = Uniforms.Find("ministry_trusty");
        var officer = Uniforms.Find("ministry_officer");
        var sergeant = Uniforms.Find("ministry_sergeant");
        var foreman = Uniforms.Find("ministry_foreman");
        for (var k = 0; k < game.LabourCount; k++)
        {
            ref readonly var s = ref game.LabourScenes[k];
            var up = StandInPath.Up(s.P0);
            SceneUp[k] = up;
            SceneFwd[k] = StandInPath.Along(s.Facing, s.P0);
            SceneRight[k] = Vector3.Normalize(Vector3.Cross(SceneFwd[k], up));
            Ways[k] = s.PointCount >= 2 ? MakeWay(s, s.Kind == LabourKind.March) : null;
            var rng = new Random(s.Seed * 7919 + k);
            for (var i = 0; i < s.Inmates + s.Guards && _count < MaxFigures; i++)
            {
                var f = Figures[_count] ??= new Figure();
                f.Scene = k;
                f.Slot = i < s.Inmates ? i : i - s.Inmates;
                f.Role = i < s.Inmates ? Role.Inmate : s.Kind == LabourKind.RollCall && i == s.Inmates ? Role.Caller : s.Kind == LabourKind.March ? Role.Escort : Role.Guard;
                f.Rand = (float)rng.NextDouble();
                f.Rand2 = (float)rng.NextDouble();
                f.Grounded = false;
                f.Beat = -1;
                f.Extra = 0;
                f.N = 0;
                f.Lod = 0;
                var v = new BodyVariant((byte)rng.Next(BodyModel.SkinTones.Length), (byte)rng.Next(BodyModel.HairColors.Length),
                    rng.Next(10) switch { < 5 => Headwear.Buzz, < 7 => Headwear.Hair, < 9 => Headwear.Bandana, _ => Headwear.Cap }, rng.Next(3) == 0, (byte)rng.Next(4));
                UniformDef? u;
                if (f.Role == Role.Inmate)
                {
                    var roll = rng.Next(12);
                    u = roll == 0 ? hiVis : roll == 1 && i == 0 ? trusty : inmate;
                    if (s.Kind == LabourKind.Sit)
                        f.Extra = (byte)(1 + rng.Next(2));
                }
                else
                    u = (s.Flags & LabourScene.FlagForeman) != 0 ? foreman : f.Role == Role.Caller ? sergeant : officer;
                var c = u?.Colors(v) ?? BodyColors.For(new Vector3(0.5f, 0.47f, 0.35f), v);
                if (f.Role == Role.Inmate && u == inmate)
                {
                    // (worn and patched: a little lighter or darker than the issue, and the odd patch of another cloth)
                    var wear = 0.86f + 0.24f * (float)rng.NextDouble();
                    c.Shirt *= wear;
                    c.Sleeve *= wear * (0.96f + 0.08f * (float)rng.NextDouble());
                }
                // In uniform with its own hat, the body's headwear goes (Build's hat flag); otherwise their own (cold-weather) headwear stays.
                f.Hat = u?.Hat;
                BodyModel.Build(f.Torso, f.Head, 0, v, c, f.Hat != null, u);
                f.Legs = u?.Legs ?? LegColors.For(new Vector3(0.5f, 0.47f, 0.35f));
                f.Arms = u?.Arms ?? ArmColors.For(c.Sleeve);
                f.Skin = c.Skin;
                f.Cap = v.Headwear == Headwear.Hair ? c.Hair : c.Gear;
                _count++;
            }
        }
    }

    /// <summary>Finds the ground under every standing figure and along every way (the constructs' floors and slabs first, else the server's terrain point).</summary>
    private static void Ground(Game game, double raw)
    {
        _groundAgain = _groundAgain < 1 ? 3 : _groundAgain < 4 ? 12 : _groundAgain < 13 ? 40 : double.PositiveInfinity;
        for (var k = 0; k < game.LabourCount; k++)
            if (Ways[k] is { } way)
                for (var i = 0; i < way.Count; i++)
                    way.At[i] = GroundAt(way.At[i]);
        for (var i = 0; i < _count; i++)
        {
            var f = Figures[i];
            ref readonly var s = ref game.LabourScenes[f.Scene];
            Spot(f, s, out f.Pos, out f.Fwd);
            f.Pos = GroundAt(f.Pos);
            f.Grounded = true;
        }
    }

    /// <summary>The ground at a point: a short ray down onto whatever stands there (a slab, a floor), else the point on its own ground.</summary>
    private static Vector3 GroundAt(Vector3 p)
    {
        var up = StandInPath.Up(p);
        var g = StandInPath.Ground(p + up * 2f);
        var from = g + up * 1.4f;
        if (World.Query.Raycast(from, -up, 2.4f, QueryFilter.All, out var hit) && Vector3.Dot(hit.Normal, up) > 0.6f)
            return hit.Point;
        return g;
    }

    private static Way MakeWay(in LabourScene s, bool loop)
    {
        var n = s.PointCount;
        var len = 0f;
        for (var i = 0; i < n - 1 + (loop ? 1 : 0); i++)
            len += Vector3.Distance(s.Point(i), s.Point((i + 1) % n));
        var count = Math.Clamp((int)MathF.Ceiling(len / WayStep) + 1, 2, 1024);
        var way = new Way { At = new Vector3[count], Length = len, Loop = loop, Count = count };
        var seg = 0;
        var segStart = 0f;
        var segLen = Vector3.Distance(s.Point(0), s.Point(1 % n));
        for (var i = 0; i < count; i++)
        {
            var d = len * i / (count - 1);
            while (d > segStart + segLen && seg < n - 2 + (loop ? 1 : 0))
            {
                segStart += segLen;
                seg++;
                segLen = Vector3.Distance(s.Point(seg % n), s.Point((seg + 1) % n));
            }
            var u = segLen > 1e-4f ? Math.Clamp((d - segStart) / segLen, 0f, 1f) : 0f;
            way.At[i] = Vector3.Lerp(s.Point(seg % n), s.Point((seg + 1) % n), u);
        }
        return way;
    }

    /// <summary>A point <paramref name="d"/> m along a way (wrapped on a loop, clamped otherwise) and its direction there.</summary>
    private static Vector3 Along(Way w, float d, out Vector3 dir)
    {
        if (w.Loop)
        {
            d %= w.Length;
            if (d < 0)
                d += w.Length;
        }
        else
            d = Math.Clamp(d, 0, w.Length);
        var x = w.Length > 0 ? d / w.Length * (w.Count - 1) : 0;
        var i = Math.Min((int)x, w.Count - 2);
        var a = w.At[i];
        var b = w.At[i + 1];
        var p = Vector3.Lerp(a, b, x - i);
        // The direction over a metre and a bit, so corners turn rather than snap.
        var i0 = w.Loop ? (i - 2 + w.Count - 1) % (w.Count - 1) : Math.Max(i - 2, 0);
        var i1 = w.Loop ? (i + 3) % (w.Count - 1) : Math.Min(i + 3, w.Count - 1);
        var v = w.At[i1] - w.At[i0];
        dir = v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : Vector3.UnitZ;
        return p;
    }

    // ---- Where each one is ----------------------------------------------------------------------------------------

    /// <summary>A standing figure's spot and facing (before the ground), by its gang's layout.</summary>
    private static void Spot(Figure f, in LabourScene s, out Vector3 pos, out Vector3 fwd)
    {
        var k = f.Scene;
        var right = SceneRight[k];
        fwd = SceneFwd[k];
        var p0 = s.P0;
        var gap = MathF.Max(s.Gap, 0.5f);
        var n = Math.Max((int)s.Inmates, 1);
        if (f.Role != Role.Inmate)
        {
            // Guards: to the side of the gang, a little behind, looking on.
            var span = s.Kind switch { LabourKind.RollCall => 2.5f, LabourKind.Queue => 1.4f, LabourKind.Huddle or LabourKind.Hammer => gap + 1.8f, _ => (n - 1) * gap * 0.5f + 2.4f };
            var side = (f.Slot & 1) == 0 ? -1f : 1f;
            pos = s.Kind switch
            {
                LabourKind.Queue => p0 + right * 1.6f - fwd * (f.Slot * 2.5f + 0.5f),
                LabourKind.RollCall => p0 + fwd * 2.2f + right * (side * span),
                LabourKind.Huddle or LabourKind.Hammer => p0 + right * (side * span) - fwd * 1.2f,
                _ when Ways[k] != null => Along(Ways[k]!, Ways[k]!.Length * (0.5f + 0.25f * side), out _) + right * 1.8f,
                _ => p0 + right * ((n - 1) * gap * 0.5f + side * span) - fwd * 1.8f,
            };
            var look = (s.Kind is LabourKind.Huddle or LabourKind.Hammer ? p0 : p0 + right * ((n - 1) * gap * 0.5f)) - pos;
            if (s.Kind == LabourKind.RollCall)
                look = -fwd;
            fwd = StandInPath.Along(look.LengthSquared() > 0.01f ? look : fwd, pos);
            return;
        }
        var i = f.Slot;
        switch (s.Kind)
        {
            case LabourKind.RollCall:
            {
                var cols = Math.Min(5, n);
                var row = i / cols;
                var col = i % cols;
                pos = p0 + right * ((col - (cols - 1) * 0.5f) * gap) - fwd * (row * gap * 1.15f);
                return;
            }
            case LabourKind.Huddle:
            case LabourKind.Hammer:
            {
                var a = MathF.Tau * (i + 0.5f) / n + (f.Rand - 0.5f) * 0.4f;
                var at = p0 + (right * MathF.Sin(a) - fwd * MathF.Cos(a)) * gap;
                pos = at;
                fwd = StandInPath.Along(p0 - at, at);
                return;
            }
            case LabourKind.Queue:
                pos = p0 - fwd * (i * gap);
                return;
            default:
            {
                // A row along the gang's right, each turned a little their own way.
                pos = p0 + right * (i * gap) + fwd * ((f.Rand2 - 0.5f) * 0.3f);
                var turn = (f.Rand - 0.5f) * 0.5f;
                fwd = Vector3.Normalize(fwd * MathF.Cos(turn) + right * MathF.Sin(turn));
                return;
            }
        }
    }

    /// <summary>Where a figure is now and the way it faces; for those who walk, how fast and the gait's phase.</summary>
    private static void Place(Figure f, in LabourScene s, double t, out Vector3 pos, out Vector3 fwd, out float moving, out float phase)
    {
        moving = 0;
        phase = 0;
        pos = f.Pos;
        fwd = f.Fwd;
        if (!f.Grounded)
        {
            Spot(f, s, out pos, out fwd);
            f.Pos = pos;
            f.Fwd = fwd;
        }
        var way = Ways[f.Scene];
        var n = Math.Max((int)s.Inmates, 1);
        if (f.Role == Role.Inmate && way != null)
            switch (s.Kind)
            {
                case LabourKind.March:
                {
                    var speed = 0.85f;
                    var d = (float)(t * speed % way.Length) - f.Slot * MathF.Max(s.Gap, 0.6f);
                    pos = Along(way, d, out fwd);
                    moving = speed;
                    phase = d / Stride;
                    return;
                }
                case LabourKind.Haul:
                case LabourKind.Barrow:
                case LabourKind.Sweep:
                case LabourKind.Paint:
                {
                    var speed = s.Kind switch { LabourKind.Haul => 0.9f, LabourKind.Barrow => 1f, LabourKind.Sweep => 0.32f, _ => 0.05f };
                    var rest = s.Kind is LabourKind.Haul or LabourKind.Barrow ? 3.5f : 0.6f;
                    var walk = way.Length / speed;
                    var cycle = 2 * (walk + rest);
                    var u = (float)((t + cycle * f.Slot / n) % cycle);
                    float d;
                    var back = false;
                    if (u < walk)
                    {
                        d = u * speed;
                        moving = speed;
                    }
                    else if (u < walk + rest)
                        d = way.Length;
                    else if (u < 2 * walk + rest)
                    {
                        d = way.Length - (u - walk - rest) * speed;
                        moving = speed;
                        back = true;
                    }
                    else
                    {
                        d = 0;
                        back = true;
                    }
                    pos = Along(way, d, out var dir);
                    fwd = s.Kind == LabourKind.Paint ? SceneFwd[f.Scene] : back ? -dir : dir;
                    phase = (back ? way.Length * 2 - d : d) / Stride;
                    return;
                }
            }
        if (f.Role == Role.Escort && way != null)
        {
            // The march's escort walks beside the middle of the column, on its right.
            var speed = 0.85f;
            var d = (float)(t * speed % way.Length) - (n - 1) * 0.5f * MathF.Max(s.Gap, 0.6f) - f.Slot * 2.5f;
            var at = Along(way, d, out fwd);
            var right = Vector3.Normalize(Vector3.Cross(fwd, SceneUp[f.Scene]));
            pos = at + right * (f.Slot == 0 ? 1.3f : -1.3f);
            moving = speed;
            phase = d / Stride + 0.3f;
            return;
        }
        if (s.Kind == LabourKind.Queue && f.Role == Role.Inmate && n > 1)
        {
            // The queue moves up one every so often; the one at the front goes round to the back (and queues again).
            const float every = 14f, shuffle = 1.6f;
            var gap = MathF.Max(s.Gap, 0.5f);
            var step = (long)Math.Floor(t / every);
            var u = (float)(t - step * every);
            var slot = (int)(((f.Slot - step) % n + n) % n);
            var p0 = s.P0;
            var fw = SceneFwd[f.Scene];
            var right = SceneRight[f.Scene];
            if (slot == n - 1)
            {
                // Just left the front: out to the side, down the line, back in at its end.
                var side = right * 1.1f;
                var lineLen = (n - 1) * gap;
                var total = 1.1f * 2 + lineLen;
                var d = u * 1.05f;
                if (d < total)
                {
                    Vector3 a, b;
                    float x;
                    if (d < 1.1f)
                    {
                        a = p0;
                        b = p0 + side;
                        x = d / 1.1f;
                    }
                    else if (d < 1.1f + lineLen)
                    {
                        a = p0 + side;
                        b = p0 + side - fw * lineLen;
                        x = (d - 1.1f) / lineLen;
                    }
                    else
                    {
                        a = p0 + side - fw * lineLen;
                        b = p0 - fw * lineLen;
                        x = (d - 1.1f - lineLen) / 1.1f;
                    }
                    pos = GroundNear(Vector3.Lerp(a, b, x), f.Pos);
                    fwd = StandInPath.Along(b - a, pos);
                    moving = 1.05f;
                    phase = d / Stride;
                    return;
                }
                pos = GroundNear(p0 - fw * lineLen, f.Pos);
                fwd = fw;
                return;
            }
            var from = slot + 1;
            var k = Math.Clamp(u / shuffle, 0f, 1f);
            var e = k * k * (3 - 2 * k);
            pos = GroundNear(p0 - fw * (gap * (from + (slot - from) * e)), f.Pos);
            fwd = fw;
            if (k < 1)
            {
                moving = gap / shuffle;
                phase = k * 0.6f + f.Rand;
            }
            return;
        }
        if (f.Role == Role.Caller)
        {
            // The sergeant walks along the front row to whoever is being called, and faces them.
            var cols = Math.Min(5, n);
            var call = (int)(t / 3.2 % n);
            var gap = MathF.Max(s.Gap, 0.5f);
            var x0 = ((call % cols) - (cols - 1) * 0.5f) * gap;
            var prev = (((call - 1 + n) % n) % cols - (cols - 1) * 0.5f) * gap;
            var k = Math.Clamp((float)(t % 3.2) / 1.1f, 0f, 1f);
            var x = float.Lerp(prev, x0, k * k * (3 - 2 * k));
            pos = GroundNear(s.P0 + SceneFwd[f.Scene] * 1.6f + SceneRight[f.Scene] * x, f.Pos);
            fwd = -SceneFwd[f.Scene];
            if (k < 1 && MathF.Abs(x0 - prev) > 0.1f)
            {
                moving = 0.7f;
                phase = k * 1.2f;
            }
        }
    }

    /// <summary>A point brought to the ground height of a nearby grounded one (the queue's and the roll call's short moves: no ray per frame).</summary>
    private static Vector3 GroundNear(Vector3 p, Vector3 grounded)
    {
        var up = StandInPath.Up(grounded);
        return p + up * Vector3.Dot(grounded - p, up);
    }

    // ---- Poses -----------------------------------------------------------------------------------------------------

    private enum Tool : byte { None, Shovel, Pick, Hammer, Broom, Brush, Clipboard, Rock, Barrow, Sling }

    /// <summary>
    /// A body's pose: the hips (height, forward), the upper body's lean (negative forward), twist and roll about the hips,
    /// the head's turn, the ankles in the body's frame (feet at the origin, -Z ahead) and the wrists in the upper body's.
    /// </summary>
    private struct Pose
    {
        public float PelvisY, PelvisZ, Lean, Twist, Roll, HeadYaw, HeadPitch;
        public Vector3 FootR, FootL, HandR, HandL;
        public Tool Tool;
        public bool KneesUp, Loaded;
        public float Wheel; // the barrow's wheel turn (rad)

        public static Pose Standing => new()
        {
            PelvisY = 0.89f, FootR = new(0.16f, 0.09f, 0.02f), FootL = new(-0.16f, 0.09f, -0.02f),
            HandR = new(0.3f, 0.88f, 0.02f), HandL = new(-0.3f, 0.88f, 0.02f),
        };

        public static Pose Lerp(in Pose a, in Pose b, float t) => new()
        {
            PelvisY = float.Lerp(a.PelvisY, b.PelvisY, t), PelvisZ = float.Lerp(a.PelvisZ, b.PelvisZ, t), Lean = float.Lerp(a.Lean, b.Lean, t),
            Twist = float.Lerp(a.Twist, b.Twist, t), Roll = float.Lerp(a.Roll, b.Roll, t), HeadYaw = float.Lerp(a.HeadYaw, b.HeadYaw, t),
            HeadPitch = float.Lerp(a.HeadPitch, b.HeadPitch, t), FootR = Vector3.Lerp(a.FootR, b.FootR, t), FootL = Vector3.Lerp(a.FootL, b.FootL, t),
            HandR = Vector3.Lerp(a.HandR, b.HandR, t), HandL = Vector3.Lerp(a.HandL, b.HandL, t), Tool = a.Tool, KneesUp = a.KneesUp, Loaded = a.Loaded, Wheel = a.Wheel,
        };
    }

    private static float Smooth(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3 - 2 * x);
    }

    /// <summary>Between keys at <paramref name="at"/> (rising cycle fractions) over a cycle position <paramref name="u"/> (0-1), eased.</summary>
    private static Pose Keys(float u, ReadOnlySpan<float> at, ReadOnlySpan<Pose> keys)
    {
        for (var i = 0; i < at.Length - 1; i++)
            if (u < at[i + 1])
                return Pose.Lerp(keys[i], keys[i + 1], Smooth((u - at[i]) / (at[i + 1] - at[i])));
        return keys[^1];
    }

    private static readonly Pose[] ShovelKeys = MakeShovel(), PickKeys = MakePick(), HammerKeys = MakeHammer();
    private static readonly float[] ShovelAt = [0f, 0.25f, 0.45f, 0.6f, 1f], PickAt = [0f, 0.5f, 0.6f, 0.82f, 1f], HammerAt = [0f, 0.55f, 0.66f, 0.85f, 1f];

    private static Pose[] MakeShovel()
    {
        var dig = Pose.Standing with { PelvisY = 0.8f, Lean = -0.5f, FootR = new(0.2f, 0.09f, 0.22f), FootL = new(-0.16f, 0.09f, -0.28f), HandR = new(0.14f, 0.95f, -0.05f), HandL = new(0.02f, 0.82f, -0.48f), Tool = Tool.Shovel, HeadPitch = -0.3f };
        var push = dig with { Lean = -0.58f, HandR = new(0.13f, 0.9f, -0.18f), HandL = new(0.02f, 0.75f, -0.6f) };
        var lift = dig with { PelvisY = 0.86f, Lean = -0.25f, HandR = new(0.16f, 1.02f, -0.1f), HandL = new(0.0f, 1.08f, -0.5f), HeadPitch = -0.1f };
        var toss = lift with { Lean = -0.12f, Twist = -0.85f, HandR = new(0.22f, 1.12f, -0.05f), HandL = new(0.08f, 1.3f, -0.45f), HeadYaw = -0.4f };
        return [dig, push, lift, toss, dig];
    }

    private static Pose[] MakePick()
    {
        var strike = Pose.Standing with { PelvisY = 0.84f, Lean = -0.45f, FootR = new(0.2f, 0.09f, 0.18f), FootL = new(-0.18f, 0.09f, -0.22f), HandR = new(0.06f, 0.98f, -0.42f), HandL = new(0.02f, 0.85f, -0.62f), Tool = Tool.Pick, HeadPitch = -0.35f };
        var raise = strike with { PelvisY = 0.9f, Lean = 0.12f, HandR = new(0.1f, 1.62f, -0.12f), HandL = new(0.05f, 1.86f, 0.04f), HeadPitch = 0.05f };
        return [strike, raise, strike, strike with { Lean = -0.4f }, strike];
    }

    private static Pose[] MakeHammer()
    {
        var keys = MakePick();
        for (var i = 0; i < keys.Length; i++)
            keys[i] = keys[i] with { Tool = Tool.Hammer, Lean = keys[i].Lean * 1.15f };
        return keys;
    }

    /// <summary>A walk: the ankles along a stride, lifted in the swing; the hips bob a little (Stride m per cycle).</summary>
    private static void Gait(ref Pose p, float phase, float amount)
    {
        const float half = 0.3f;
        for (var i = 0; i < 2; i++)
        {
            var s = (phase + i * 0.5f) % 1f;
            if (s < 0)
                s += 1;
            float z, y;
            if (s < 0.6f)
            {
                z = float.Lerp(-half, half, s / 0.6f);
                y = 0;
            }
            else
            {
                var u = (s - 0.6f) / 0.4f;
                z = float.Lerp(half, -half, Smooth(u));
                y = MathF.Sin(MathF.PI * u) * 0.11f;
            }
            ref var foot = ref i == 0 ? ref p.FootR : ref p.FootL;
            foot = Vector3.Lerp(foot, new Vector3(i == 0 ? 0.14f : -0.14f, 0.09f + y, z), amount);
        }
        p.PelvisY -= (0.02f + 0.02f * MathF.Cos(phase * MathF.Tau * 2)) * amount;
    }

    private static Pose PoseOf(Figure f, in LabourScene s, double t, float moving, float phase, bool alarm)
    {
        var tf = (float)(t % 3600.0);
        var r = f.Rand;
        var walks = moving > 0.01f;
        if (alarm)
        {
            if (f.Role != Role.Inmate)
                return Pose.Standing with { FootR = new(0.2f, 0.09f, 0.05f), FootL = new(-0.2f, 0.09f, -0.05f), HandR = new(0.14f, 1.12f, -0.36f), HandL = new(-0.1f, 1.16f, -0.32f), HeadYaw = 0.6f * MathF.Sin(tf * 0.8f + r * 9), Tool = Tool.None };
            return Kneel with { HandR = new(0.1f, 1.88f, -0.02f), HandL = new(-0.1f, 1.88f, -0.02f), Lean = 0.02f, HeadPitch = -0.35f };
        }
        Pose p;
        if (f.Role != Role.Inmate)
        {
            p = Pose.Standing with { FootR = new(0.2f, 0.09f, 0.02f), FootL = new(-0.2f, 0.09f, -0.02f), Lean = 0.04f, HandR = new(0.06f, 1.02f, 0.2f), HandL = new(-0.06f, 1.02f, 0.22f) };
            p.HeadYaw = 0.45f * MathF.Sin(tf * 0.27f + r * 20);
            if (f.Role == Role.Caller || ((s.Flags & LabourScene.FlagForeman) != 0 && f.Slot == 0))
                p = p with { HandL = new(-0.08f, 1.15f, -0.34f), HandR = new(0.1f, 1.2f, -0.3f), Tool = Tool.Clipboard, HeadPitch = -0.2f, HeadYaw = 0, Lean = 0 };
            if (walks)
                Gait(ref p, phase, 1f);
            else
                p.Roll = 0.02f * MathF.Sin(tf * 0.6f + r * 7); // (rocking on their heels)
            return p;
        }
        switch (s.Kind)
        {
            case LabourKind.Shovel:
            {
                var u = (tf / 2.8f + r) % 1f;
                p = Keys(u, ShovelAt, ShovelKeys);
                p.Loaded = u is > 0.25f and < 0.58f && (s.Flags & LabourScene.FlagHeapMask) != 0;
                return Pause(p, f, tf);
            }
            case LabourKind.Pick:
                return Pause(Keys((tf / 1.7f + r) % 1f, PickAt, PickKeys), f, tf);
            case LabourKind.Hammer:
                return Pause(Keys((tf / 2.3f + r) % 1f, HammerAt, HammerKeys), f, tf);
            case LabourKind.Haul:
            {
                p = Pose.Standing with { Lean = -0.14f, HandR = new(0.19f, 1.05f, -0.36f), HandL = new(-0.19f, 1.05f, -0.36f), Tool = Tool.Rock, HeadPitch = -0.15f };
                if (walks)
                    Gait(ref p, phase, 1f);
                else
                    p = p with { PelvisY = 0.72f, Lean = -0.62f, HandR = new(0.2f, 0.78f, -0.48f), HandL = new(-0.2f, 0.78f, -0.48f), FootR = new(0.2f, 0.09f, 0.08f), FootL = new(-0.2f, 0.09f, -0.04f) };
                return p;
            }
            case LabourKind.Barrow:
            {
                p = Pose.Standing with { Lean = -0.18f, Tool = Tool.Barrow, HeadPitch = -0.1f, Wheel = phase * Stride / 0.2f };
                // Full both ways, or (loaded) full out and empty back.
                p.Loaded = (s.Flags & LabourScene.FlagHeapMask) != 0 && ((s.Flags & LabourScene.FlagLoaded) == 0 || Ways[f.Scene] is not { } wy || phase * Stride <= wy.Length);
                if (walks)
                    Gait(ref p, phase, 1f);
                return p; // (the hands go on the handles in DrawFull)
            }
            case LabourKind.Sweep:
            {
                var stroke = MathF.Sin(tf * MathF.Tau / 1.2f + r * 6);
                p = Pose.Standing with { Lean = -0.2f, HandR = new(0.16f + 0.1f * stroke, 1.0f, -0.12f), HandL = new(0.02f + 0.2f * stroke, 0.72f, -0.42f), Tool = Tool.Broom, HeadPitch = -0.3f, Twist = 0.15f * stroke };
                if (walks)
                    Gait(ref p, phase, 0.6f);
                return p;
            }
            case LabourKind.Paint:
            {
                var stroke = MathF.Sin(tf * MathF.Tau * 1.6f + r * 6);
                return Kneel with { Lean = -0.42f, HandR = new(0.1f + 0.1f * stroke, 0.66f, -0.52f), HandL = new(-0.18f, 0.78f, -0.22f), Tool = Tool.Brush, HeadPitch = -0.45f };
            }
            case LabourKind.Sit:
            {
                var rock = MathF.Sin(tf * 1.1f + r * 8);
                p = Pose.Standing with
                {
                    PelvisY = 0.24f, PelvisZ = 0.05f, Lean = 0.06f * rock - 0.12f, KneesUp = true, HeadPitch = -0.35f,
                    FootR = new(0.19f, 0.09f, -0.58f), FootL = new(-0.19f, 0.09f, -0.55f),
                    HandR = new(-0.1f, 1.12f, -0.42f), HandL = new(0.1f, 1.14f, -0.42f),
                };
                if (f.Extra == 2)
                    p = p with { HandR = new(-0.04f, 1.18f, -0.24f), Tool = Tool.Sling };
                return p;
            }
            case LabourKind.RollCall:
            {
                p = Pose.Standing with { FootR = new(0.12f, 0.09f, 0f), FootL = new(-0.12f, 0.09f, 0f), HandR = new(0.31f, 0.86f, 0f), HandL = new(-0.31f, 0.86f, 0f) };
                var n = Math.Max((int)s.Inmates, 1);
                var call = (int)(t / 3.2 % n);
                if (call == f.Slot && t % 3.2 is > 1.0 and < 2.4)
                    p.HandR = new(0.28f, 1.98f, -0.06f); // "Present."
                else
                    p.HeadYaw = 0.15f * MathF.Sin(tf * 0.4f + r * 9); // (eyes front, mostly)
                return p;
            }
            case LabourKind.Huddle:
            {
                var rub = MathF.Sin(tf * 7f + r * 10) * 0.04f;
                p = Pose.Standing with { Lean = -0.14f, HandR = new(0.12f, 1.12f, -0.46f + rub), HandL = new(-0.12f, 1.12f, -0.46f - rub), HeadPitch = -0.15f };
                return Fidget(p, f, tf, warm: true);
            }
            case LabourKind.March:
            {
                p = Pose.Standing with { Lean = -0.1f, HeadPitch = -0.3f, HandL = new(-0.3f, 0.88f + 0.05f * MathF.Sin(phase * MathF.Tau), 0.1f * MathF.Sin(phase * MathF.Tau)) };
                // A hand on the shoulder of the one in front (the first in the column has none).
                p.HandR = f.Slot > 0 ? new Vector3(0.26f, 1.36f, -0.72f) : new Vector3(0.3f, 0.88f + 0.05f * MathF.Sin(phase * MathF.Tau + MathF.PI), -0.1f * MathF.Sin(phase * MathF.Tau));
                Gait(ref p, phase, 1f);
                return p;
            }
            default: // Stand, Queue
            {
                p = Pose.Standing with { Lean = -0.08f, HandR = new(-0.11f, 1.17f, -0.24f), HandL = new(0.11f, 1.21f, -0.22f), HeadPitch = -0.08f };
                if (walks)
                {
                    Gait(ref p, phase, 1f);
                    return p;
                }
                return Fidget(p, f, tf, warm: false);
            }
        }
    }

    private static readonly Pose Kneel = Pose.Standing with { PelvisY = 0.48f, FootR = new(0.18f, 0.09f, 0.45f), FootL = new(-0.18f, 0.09f, 0.42f) };

    /// <summary>The cold: now and then a foot stamped, hands blown into, a look round.</summary>
    private static Pose Fidget(Pose p, Figure f, float tf, bool warm)
    {
        var slot = (int)(tf / 6.5f + f.Rand * 13);
        var u = tf / 6.5f + f.Rand * 13 - slot;
        var pick = (slot * 7 + (int)(f.Rand2 * 97)) % 5;
        p.Roll = 0.025f * MathF.Sin(tf * 0.9f + f.Rand * 6);
        switch (pick)
        {
            case 0 when u < 0.25f: // a stamp, then the other foot
            {
                var k = u / 0.25f;
                var lift = MathF.Max(0, MathF.Sin(k * MathF.PI * 4)) * 0.09f;
                if (k < 0.5f)
                    p.FootR.Y += lift;
                else
                    p.FootL.Y += lift;
                break;
            }
            case 1 when u < 0.35f && !warm: // blowing into the hands
                p.HandR = new(0.05f, 1.52f, -0.27f);
                p.HandL = new(-0.05f, 1.52f, -0.27f);
                p.HeadPitch = -0.1f;
                break;
            case 2 when u < 0.4f: // a look round
                p.HeadYaw = 0.7f * MathF.Sin(u / 0.4f * MathF.PI) * (f.Rand2 > 0.5f ? 1 : -1);
                break;
        }
        return p;
    }

    /// <summary>A worker now and then stops for breath: leans on the tool, straightens their back.</summary>
    private static Pose Pause(Pose work, Figure f, float tf)
    {
        var slot = (int)(tf / 23f + f.Rand2 * 7);
        if ((slot * 5 + (int)(f.Rand * 31)) % 4 != 0)
            return work;
        var u = tf / 23f + f.Rand2 * 7 - slot;
        if (u > 0.18f)
            return work;
        var rest = Pose.Standing with { Lean = 0.1f, Tool = work.Tool, HandR = new(0.1f, 1.08f, -0.32f), HandL = new(0.0f, 1.12f, -0.34f), HeadPitch = 0.15f, FootR = work.FootR, FootL = work.FootL };
        var k = u < 0.03f ? u / 0.03f : u > 0.15f ? (0.18f - u) / 0.03f : 1f;
        return Pose.Lerp(work, rest, Smooth(k));
    }

    // ---- Drawing ---------------------------------------------------------------------------------------------------

    private static Matrix4x4 UpperOf(in Pose p) =>
        Matrix4x4.CreateTranslation(0, -LegAnimator.StandPelvis, 0) * Matrix4x4.CreateRotationX(p.Lean) * Matrix4x4.CreateRotationZ(p.Roll)
        * Matrix4x4.CreateRotationY(p.Twist) * Matrix4x4.CreateTranslation(0, p.PelvisY, p.PelvisZ);

    private static void Box(Renderer g, Vector3 size, Vector3 center, in Matrix4x4 frame, Vector3 color)
    {
        // (scale then move, made in one: one multiply a box)
        var local = new Matrix4x4(size.X, 0, 0, 0, 0, size.Y, 0, 0, 0, 0, size.Z, 0, center.X, center.Y, center.Z, 1);
        Emit(g, local * frame, color);
    }

    /// <summary>A box in the world: kept with the figure being posed (drawn by the caller), else drawn now.</summary>
    private static void Emit(Renderer g, in Matrix4x4 m, Vector3 color)
    {
        if (_rec is { } f)
        {
            if (f.N < BoxCap)
            {
                f.M[f.N] = m;
                f.C[f.N++] = color;
            }
            return;
        }
        g.Draw(m, color);
        Boxes++;
    }

    /// <summary>A body's torso or head parts (as <see cref="BodyParts.DrawImmediate"/>, through <see cref="Emit"/>; no jaw: they don't talk).</summary>
    private static void Parts(Renderer g, BodyParts parts, in Matrix4x4 frame)
    {
        for (var i = 0; i < parts.Count; i++)
            if (parts.ShowDetail || !parts.Detail[i])
                Emit(g, parts.Local[i] * frame, parts.Draws[i].Color);
    }

    private static void DrawFull(Renderer g, Figure f, in LabourScene s, in Matrix4x4 w, in Pose pose, Vector3 right, bool mid)
    {
        var p = pose;
        var upper = UpperOf(p);
        // The barrow's handles are where the hands go (in the body's frame, so back into the upper body's).
        if (p.Tool == Tool.Barrow && Matrix4x4.Invert(upper, out var inv))
        {
            p.HandR = Vector3.Transform(new Vector3(0.27f, 0.74f, -0.48f), inv);
            p.HandL = Vector3.Transform(new Vector3(-0.27f, 0.74f, -0.48f), inv);
        }
        var uw = upper * w;
        var st = BodyModel.Stances[0];
        var head = Matrix4x4.CreateRotationX(p.HeadPitch) * Matrix4x4.CreateRotationY(p.HeadYaw) * Matrix4x4.CreateTranslation(st.HeadAt) * uw;
        if (mid)
        {
            // (a coat, a collar, a head and the hat's crown: the body's parts are for up close)
            Box(g, st.TorsoSize * new Vector3(0.82f, 1f, 0.86f), Vector3.Zero, st.TorsoFrame * uw, f.Arms.Sleeve);
            Box(g, new Vector3(0.36f, 0.06f, 0.33f), new Vector3(0, st.TorsoSize.Y * 0.5f + 0.02f, 0.01f), st.TorsoFrame * uw, f.Arms.Cuff);
            Box(g, st.HeadSize * new Vector3(0.94f, 0.92f, 0.94f), Vector3.Zero, head, f.Skin);
        }
        else
        {
            Parts(g, f.Torso, st.TorsoFrame * uw);
            Parts(g, f.Head, head);
        }
        if (f.Hat != null)
        {
            var hat = Matrix4x4.CreateScale(st.HeadSize.X / HatRender.ModelHeadWidth) * Matrix4x4.CreateTranslation(0, st.HeadSize.Y / 2, 0) * head;
            foreach (var b in f.Hat.Boxes)
            {
                Emit(g, b.Local * hat, b.Color);
                if (mid)
                    break;
            }
        }
        else if (mid)
            Box(g, new Vector3(0.46f, 0.1f, 0.46f), new Vector3(0, st.HeadSize.Y * 0.5f, 0), head, f.Cap);
        if (f.Extra == 1)
            Box(g, new Vector3(0.47f, 0.07f, 0.47f), new Vector3(0, 0.07f, 0), head, Bandage); // (a bandage round the head)

        // Legs: the hips, then each leg's two bones by IK to the ankle, the feet flat.
        var pelvis = Matrix4x4.CreateRotationY(p.Twist * 0.35f) * Matrix4x4.CreateTranslation(0, p.PelvisY, p.PelvisZ);
        var pp = LegAnimator.PelvisPart;
        Box(g, pp.Size, pp.Center, pelvis * w, f.Legs.Of(pp.Paint));
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? 1f : -1f;
            var hip = Vector3.Transform(new Vector3(side * 0.19f, -0.04f, 0), pelvis);
            var pole = p.KneesUp ? Vector3.Normalize(new Vector3(side * 0.15f, 1f, -0.5f)) : Vector3.Normalize(new Vector3(side * 0.15f, 0, -1f));
            Arms.SolveTwoBone(hip, i == 0 ? p.FootR : p.FootL, LegAnimator.Thigh, LegAnimator.Shin, pole, out var knee, out var ankle);
            var thigh = Arms.BoneFrame(hip, knee, pole) * w;
            var shin = Arms.BoneFrame(knee, ankle, pole) * w;
            var foot = Matrix4x4.CreateTranslation(ankle) * w;
            foreach (var part in LegAnimator.LegParts)
                if (!mid || part.Paint is 1 or 3) // (mid: thigh, shin and boot; no knee pad or sole)
                    Box(g, part.Size, part.Center, part.Bone switch { 1 => thigh, 2 => shin, _ => foot }, f.Legs.Of(part.Paint));
        }

        // Arms: shoulder to wrist by IK, the elbows out and down; mittens.
        var wristR = Vector3.Transform(p.HandR, uw);
        var wristL = Vector3.Transform(p.HandL, uw);
        Arm(g, f, Vector3.Transform(new Vector3(0.33f, 1.38f, 0.05f), uw), wristR, Vector3.Normalize(Vector3.TransformNormal(new Vector3(1f, -0.8f, 0.3f), uw)));
        Arm(g, f, Vector3.Transform(new Vector3(-0.33f, 1.38f, 0.05f), uw), wristL, Vector3.Normalize(Vector3.TransformNormal(new Vector3(-1f, -0.8f, 0.3f), uw)));
        DrawTool(g, f, s, w, p, uw, wristR, wristL, right);
    }

    private static void Arm(Renderer g, Figure f, Vector3 shoulder, Vector3 target, Vector3 pole)
    {
        const float upper = 0.36f, fore = 0.34f, t = 0.11f;
        Arms.SolveTwoBone(shoulder, target, upper, fore, pole, out var elbow, out var wrist);
        var a = Arms.BoneFrame(shoulder, elbow, pole);
        var b = Arms.BoneFrame(elbow, wrist, pole);
        Box(g, new Vector3(t, t, upper + 0.03f), new Vector3(0, 0, -upper * 0.5f), a, f.Arms.Sleeve);
        Box(g, new Vector3(t * 0.86f, t * 0.82f, fore * 0.86f), new Vector3(0, 0, -fore * 0.45f), b, f.Arms.Sleeve * 0.95f);
        Box(g, new Vector3(0.1f, 0.115f, 0.13f), new Vector3(0, -0.005f, -fore - 0.045f), b, f.Arms.Glove);
    }

    private static void DrawTool(Renderer g, Figure f, in LabourScene s, in Matrix4x4 w, in Pose p, in Matrix4x4 uw, Vector3 r, Vector3 l, Vector3 right)
    {
        switch (p.Tool)
        {
            case Tool.Shovel:
            case Tool.Pick:
            case Tool.Hammer:
            case Tool.Broom:
            {
                // A two-handed tool: its shaft from behind the right hand through the left, its head at the far end.
                var frame = Arms.BoneFrame(r, l, right);
                var len = p.Tool switch { Tool.Shovel => 1.12f, Tool.Broom => 1.3f, Tool.Hammer => 0.86f, _ => 0.9f };
                Box(g, new Vector3(0.035f, 0.035f, len), new Vector3(0, 0, -len * 0.5f + 0.1f), frame, Wood);
                var end = -len + 0.1f;
                switch (p.Tool)
                {
                    case Tool.Shovel:
                        Box(g, new Vector3(0.02f, 0.25f, 0.3f), new Vector3(0, 0, end - 0.13f), frame, Steel);
                        if (p.Loaded)
                            Box(g, new Vector3(0.12f, 0.2f, 0.22f), new Vector3(0.06f, 0, end - 0.14f), frame, (s.Flags & LabourScene.FlagHeapMask) == LabourScene.FlagCoal ? Coal : Snow);
                        break;
                    case Tool.Pick:
                        Box(g, new Vector3(0.58f, 0.05f, 0.06f), new Vector3(0.03f, 0, end), frame, Steel * 0.8f);
                        break;
                    case Tool.Hammer:
                        Box(g, new Vector3(0.24f, 0.13f, 0.13f), new Vector3(0, 0, end), frame, Steel * 0.6f);
                        break;
                    default:
                        Box(g, new Vector3(0.09f, 0.42f, 0.08f), new Vector3(0, 0, end), frame, DarkWood);
                        Box(g, new Vector3(0.1f, 0.4f, 0.1f), new Vector3(-0.07f, 0, end), frame, new Vector3(0.55f, 0.46f, 0.3f));
                        break;
                }
                break;
            }
            case Tool.Brush:
                Box(g, new Vector3(0.05f, 0.05f, 0.16f), new Vector3(0, -0.03f, -0.12f), At(w, r), Paint * 0.95f);
                Box(g, new Vector3(0.17f, 0.17f, 0.17f), new Vector3(-0.36f, 0.085f, -0.34f), w, new Vector3(0.36f, 0.38f, 0.4f)); // (the paint pot)
                Box(g, new Vector3(0.15f, 0.02f, 0.15f), new Vector3(-0.36f, 0.175f, -0.34f), w, Paint);
                break;
            case Tool.Clipboard:
                Box(g, new Vector3(0.24f, 0.32f, 0.02f), new Vector3(0.1f, 0.06f, -0.04f), Matrix4x4.CreateRotationX(-0.9f) * At(w, l), Board);
                Box(g, new Vector3(0.2f, 0.27f, 0.01f), new Vector3(0.1f, 0.06f, -0.055f), Matrix4x4.CreateRotationX(-0.9f) * At(w, l), Paper);
                break;
            case Tool.Rock:
            {
                var mid = (r + l) * 0.5f;
                var heap = s.Flags & LabourScene.FlagHeapMask;
                Box(g, new Vector3(0.34f, 0.28f, 0.3f), new Vector3(0, 0.06f, -0.08f), Matrix4x4.CreateRotationY(f.Rand) * At(w, mid),
                    heap == LabourScene.FlagCoal ? Coal : heap == LabourScene.FlagSnow ? Snow : Rock);
                break;
            }
            case Tool.Sling:
                Box(g, new Vector3(0.26f, 0.13f, 0.14f), new Vector3(-0.05f, 1.12f, -0.24f), uw, Bandage);
                break;
            case Tool.Barrow:
            {
                var tray = (s.Flags & LabourScene.FlagHeapMask) switch { LabourScene.FlagCoal => Coal, LabourScene.FlagSnow => Snow, _ => Rock };
                Box(g, new Vector3(0.04f, 0.04f, 0.72f), new Vector3(0.24f, 0.62f, -0.82f), w, Wood);
                Box(g, new Vector3(0.04f, 0.04f, 0.72f), new Vector3(-0.24f, 0.62f, -0.82f), w, Wood);
                Box(g, new Vector3(0.62f, 0.26f, 0.78f), new Vector3(0, 0.56f, -1.25f), w, Barrow);
                Box(g, new Vector3(0.05f, 0.34f, 0.05f), new Vector3(0.21f, 0.25f, -1.0f), w, Rust);
                Box(g, new Vector3(0.05f, 0.34f, 0.05f), new Vector3(-0.21f, 0.25f, -1.0f), w, Rust);
                Box(g, new Vector3(0.08f, 0.4f, 0.4f), Vector3.Zero, Matrix4x4.CreateRotationX(-p.Wheel) * Matrix4x4.CreateTranslation(0, 0.2f, -1.66f) * w, new Vector3(0.12f, 0.12f, 0.12f));
                if (p.Loaded)
                    Box(g, new Vector3(0.5f, 0.16f, 0.62f), new Vector3(0, 0.72f, -1.25f), w, tray);
                break;
            }
        }
    }

    /// <summary>The body's frame moved to a world point (for things held in a hand, turned as the body is).</summary>
    private static Matrix4x4 At(in Matrix4x4 w, Vector3 point)
    {
        var m = w;
        m.Translation = point;
        return m;
    }

    /// <summary>Past <see cref="FullRange"/>: six to eight boxes (a body, a head, the hat, two legs, two arms; nearer than <see cref="ToolRange"/> the tool's shaft).</summary>
    private static void DrawFar(Renderer g, Figure f, in Matrix4x4 w, in Pose p, bool tool)
    {
        var upper = UpperOf(p);
        var uw = upper * w;
        Box(g, new Vector3(0.64f, 0.52f, 0.42f), new Vector3(0, 1.15f, 0), uw, f.Arms.Sleeve);
        Box(g, new Vector3(0.38f, 0.38f, 0.4f), new Vector3(0, 1.6f, 0), uw, f.Skin);
        if (f.Hat != null)
            Box(g, new Vector3(0.44f, 0.12f, 0.44f), new Vector3(0, 1.82f, 0), uw, f.Hat.Boxes.Count > 0 ? f.Hat.Boxes[0].Color : f.Arms.Sleeve);
        var pelvis = Matrix4x4.CreateTranslation(0, p.PelvisY, p.PelvisZ);
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? 1f : -1f;
            var hip = Vector3.Transform(new Vector3(side * 0.17f, 0.05f, 0), pelvis);
            var ankle = i == 0 ? p.FootR : p.FootL;
            var len = Vector3.Distance(hip, ankle);
            Box(g, new Vector3(0.2f, 0.2f, len), Vector3.Zero, Shift((hip + ankle) * 0.5f, hip) * w, f.Legs.Trousers);
            var sh = Vector3.Transform(new Vector3(side * 0.36f, 1.36f, 0.04f), upper);
            var hand = Vector3.Transform(i == 0 ? p.HandR : p.HandL, upper);
            var alen = MathF.Min(Vector3.Distance(sh, hand), 0.72f);
            Box(g, new Vector3(0.12f, 0.12f, alen), Vector3.Zero, Shift((sh + hand) * 0.5f, sh) * w, f.Arms.Sleeve);
        }
        if (tool && p.Tool is Tool.Shovel or Tool.Pick or Tool.Hammer or Tool.Broom)
        {
            var r = Vector3.Transform(p.HandR, upper);
            var l = Vector3.Transform(p.HandL, upper);
            var dir = l - r;
            var len = p.Tool == Tool.Broom ? 1.3f : 1f;
            if (dir.LengthSquared() > 1e-4f)
                Box(g, new Vector3(0.06f, 0.06f, len), Vector3.Zero, Shift(r + Vector3.Normalize(dir) * (len * 0.5f - 0.1f), r) * w, Wood);
        }
    }

    /// <summary>A frame at <paramref name="mid"/> with -Z pointing away from <paramref name="from"/> (a bone's box centred on its middle).</summary>
    private static Matrix4x4 Shift(Vector3 mid, Vector3 from)
    {
        var m = Arms.BoneFrame(from, mid + (mid - from), Vector3.UnitZ);
        m.Translation = mid;
        return m;
    }

    // ---- Props: heaps, boulders, the brazier, the white kerb ---------------------------------------------------------

    private static void DrawProps(Renderer g, in LabourScene s, int k, Vector3 cam)
    {
        if (Vector3.DistanceSquared(s.P0, cam) > (FarRange + 30) * (FarRange + 30) || !Visible(s.P0, 12f))
            return;
        var up = SceneUp[k];
        var fwd = SceneFwd[k];
        var right = SceneRight[k];
        var heap = s.Flags & LabourScene.FlagHeapMask;
        var col = heap switch { LabourScene.FlagCoal => Coal, LabourScene.FlagSnow => Snow, _ => Rock };
        var dark = heap switch { LabourScene.FlagCoal => Coal * 1.5f, LabourScene.FlagSnow => Snow * 0.93f, _ => RockDark };
        switch (s.Kind)
        {
            case LabourKind.Shovel:
            case LabourKind.Pick:
                if (heap == 0)
                    return;
                // A heap (or a boulder) in front of each worker.
                for (var i = 0; i < _count; i++)
                {
                    var f = Figures[i];
                    if (f.Scene != k || f.Role != Role.Inmate)
                        continue;
                    var at = f.Pos + f.Fwd * (s.Kind == LabourKind.Pick ? 1.15f : 1.05f);
                    var m = Frame(at, f.Fwd, up);
                    var big = s.Kind == LabourKind.Pick ? 1.25f : 1f;
                    Box(g, new Vector3(0.95f, 0.36f, 0.8f) * big, new Vector3(0, 0.15f * big, 0), Matrix4x4.CreateRotationY(f.Rand) * m, col);
                    Box(g, new Vector3(0.58f, 0.31f, 0.52f) * big, new Vector3(0.08f, 0.42f * big, 0.05f), Matrix4x4.CreateRotationY(f.Rand * 2.3f) * m, dark);
                }
                return;
            case LabourKind.Hammer:
            {
                var m = Frame(s.P0, fwd, up);
                Box(g, new Vector3(1.1f, 0.4f, 0.95f), new Vector3(0, 0.16f, 0), m, col);
                Box(g, new Vector3(0.66f, 0.33f, 0.7f), new Vector3(0.12f, 0.45f, -0.05f), Matrix4x4.CreateRotationY(0.7f) * m, dark);
                Box(g, new Vector3(0.38f, 0.27f, 0.31f), new Vector3(-0.42f, 0.13f, 0.47f), Matrix4x4.CreateRotationY(0.3f) * m, dark);
                return;
            }
            case LabourKind.Haul:
            {
                // A pile at each end: the rocks go to the other pile, and back.
                if (Ways[k] is not { } way)
                    return;
                for (var e = 0; e < 2; e++)
                {
                    var at = Along(way, e == 0 ? 0 : way.Length, out var dir);
                    var m = Frame(at + dir * (e == 0 ? -0.9f : 0.9f), dir, up);
                    Box(g, new Vector3(1.05f, 0.42f, 0.9f), new Vector3(0, 0.17f, 0), m, col);
                    Box(g, new Vector3(0.62f, 0.32f, 0.56f), new Vector3(-0.1f, 0.47f, 0.06f), Matrix4x4.CreateRotationY(0.6f + e) * m, dark);
                }
                return;
            }
            case LabourKind.Huddle when (s.Flags & LabourScene.FlagBrazier) != 0:
            {
                var m = Frame(s.P0, fwd, up);
                Box(g, new Vector3(0.56f, 0.74f, 0.56f), new Vector3(0, 0.37f, 0), m, Rust);
                var flicker = 0.85f + 0.15f * MathF.Sin(_flick * 13f) * MathF.Sin(_flick * 7.3f);
                g.Draw(Matrix4x4.CreateScale(0.46f, 0.07f, 0.46f) * Matrix4x4.CreateTranslation(0, 0.76f, 0) * m, Ember * flicker, 1f, mode: 2);
                Boxes++;
                return;
            }
            case LabourKind.Paint:
            {
                // The kerb, painted white, a stone at a time, along the way and in front of the painters.
                if (Ways[k] is not { } way || Vector3.DistanceSquared(s.P0, cam) > FullRange * FullRange * 2)
                    return;
                for (var d = 0.5f; d < way.Length; d += 1f)
                {
                    var at = Along(way, d, out var dir) + fwd * 0.62f;
                    Box(g, new Vector3(0.94f, 0.04f, 0.2f), new Vector3(0, 0.02f, 0), Frame(at, fwd, up), Paint);
                }
                return;
            }
        }
    }

    /// <summary>A frame at a point on the ground facing <paramref name="fwd"/> (-Z), up <paramref name="up"/>.</summary>
    private static Matrix4x4 Frame(Vector3 at, Vector3 fwd, Vector3 up)
    {
        var right = Vector3.Normalize(Vector3.Cross(fwd, up));
        var f = Vector3.Cross(up, right);
        return new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -f.X, -f.Y, -f.Z, 0, at.X, at.Y, at.Z, 1);
    }

    // ---- Sound -----------------------------------------------------------------------------------------------------

    /// <summary>The picks' and the hammers' strikes, the shovels' bite, heard up close (one sound per stroke).</summary>
    private static void Sound(Game game, Figure f, in LabourScene s, double t, Vector3 pos, float dist)
    {
        if (f.Role != Role.Inmate)
            return;
        var (period, at, sfx, pitch) = s.Kind switch
        {
            LabourKind.Pick => (1.7, 0.6, Sfx.ImpactConcrete, 0.75f),
            LabourKind.Hammer => (2.3, 0.66, Sfx.ImpactConcrete, 0.55f),
            LabourKind.Shovel => (2.8, 0.12, Sfx.ImpactDirt, 0.9f),
            _ => (0.0, 0.0, Sfx.ImpactDirt, 1f),
        };
        if (period <= 0)
            return;
        var cycle = t / period + f.Rand - at;
        var beat = (int)Math.Floor(cycle);
        if (beat == f.Beat)
            return;
        var first = f.Beat < 0;
        f.Beat = beat;
        if (first || cycle - beat > 0.2)
            return; // (only on the stroke itself, not when coming into earshot)
        var gain = 0.22f * (1f - dist / HeardRange);
        game.Audio.Effect3DPitched(sfx, pos + SceneUp[f.Scene] * 0.4f + f.Fwd * 0.9f, gain, pitch + 0.15f * f.Rand2);
    }

    // ---- Culling ---------------------------------------------------------------------------------------------------

    private static void SetFrustum(in Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        Planes[0] = c4 + c1;
        Planes[1] = c4 - c1;
        Planes[2] = c4 + c2;
        Planes[3] = c4 - c2;
        Planes[4] = c4 + c3;
        Planes[5] = c4 - c3;
        for (var i = 0; i < 6; i++)
        {
            var l = new Vector3(Planes[i].X, Planes[i].Y, Planes[i].Z).Length();
            if (l > 1e-6f)
                Planes[i] /= l;
        }
    }

    private static bool Visible(Vector3 c, float r)
    {
        foreach (var p in Planes)
            if (p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W < -r)
                return false;
        return true;
    }
}
