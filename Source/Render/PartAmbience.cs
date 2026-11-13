using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Movement and sound about a place (docs/sandbox/constructs/buildings.md "Ambience"): parts named in
/// Constructs/Ambience.json smoke, vent steam, sweep a searchlight over the ground or sound a tannoy, on any build, on
/// any world, and a cold storm blows snow along the ground round the camera. Looks and sound only, worked out on each
/// PC from what it already has (the parts, the clock, the storm): nothing on the wire, nothing in the simulation.
///
/// Kept cheap: a body's parts are looked through once a rebuild (<see cref="Construct.AmbParts"/>), bodies past
/// <see cref="Range"/> of the eye are skipped whole, the particles go into the one pool at the Particles setting's share
/// (<see cref="Fx.Share"/>), a searchlight is one of the frame's four dynamic lights, the tannoy one sound a minute or
/// two, and the blown snow one ray every fifth of a second. Nothing allocates in a steady frame.
/// </summary>
public static class PartAmbience
{
    public enum Kind : byte { Smoke, Steam, Searchlight, Tannoy }

    /// <summary>What a part does, from the file.</summary>
    public sealed class Spec
    {
        public Kind Kind;
        /// <summary>Particles a second (smoke, and steam while it vents).</summary>
        public float Rate = 6f;
        /// <summary>A particle's size (m), life (s) and speed away from the ground (m/s).</summary>
        public float Size = 0.8f, Life = 5f, Rise = 1.6f;
        /// <summary>Only the highest part of this name on a body does it (a stack of chimney lengths smokes from its top).</summary>
        public bool Top;
        /// <summary>Steam: seconds from one venting to the next, and how long it vents. Tannoy: seconds between announcements (about).</summary>
        public float Every = 9f, Burst = 2.5f;
        /// <summary>Searchlight: how far out the pool of light sweeps (m) and how wide it is. Tannoy: how far it is heard.</summary>
        public float Reach = 18f, Width = 7f;
    }

    /// <summary>Bodies further than this from the eye do nothing (m); smoke thins from half of it.</summary>
    public const float Range = 150f;

    private static Dictionary<string, Spec>? _table;
    private static uint _seed = 9176;
    private static double _last = -1, _nextGround, _nextTannoy = 12; // (the first announcement soon after the game starts)
    private static float _eyeHeight = 1.6f;

    /// <summary>Particles and sounds asked for so far (the smoke log, a check by hand).</summary>
    public static long Puffs, Announcements;

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Ambience.json");

    /// <summary>The file's parts by name; a bad file is refused with a reason and nothing moves.</summary>
    public static Dictionary<string, Spec> Table => _table ??= Load();

    private static Dictionary<string, Spec> Load()
    {
        var table = new Dictionary<string, Spec>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(FilePath))
                Parse(File.ReadAllText(FilePath), table);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            table.Clear();
            Console.WriteLine($"[ambience] {FilePath} refused: {ex.Message}");
        }
        return table;
    }

    public static void Parse(string json, Dictionary<string, Spec> table)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var part in doc.RootElement.GetProperty("parts").EnumerateObject())
        {
            var s = new Spec();
            foreach (var p in part.Value.EnumerateObject())
                switch (p.Name)
                {
                    case "kind":
                        if (!Enum.TryParse(p.Value.GetString(), true, out s.Kind))
                            throw new InvalidDataException($"\"{part.Name}\": no kind \"{p.Value.GetString()}\" (smoke, steam, searchlight, tannoy)");
                        break;
                    case "rate": s.Rate = In(p, part.Name, 0, 40); break;
                    case "size": s.Size = In(p, part.Name, 0.05f, 6); break;
                    case "life": s.Life = In(p, part.Name, 0.2f, 12); break;
                    case "rise": s.Rise = In(p, part.Name, 0, 12); break;
                    case "top": s.Top = p.Value.GetBoolean(); break;
                    case "every": s.Every = In(p, part.Name, 1, 3600); break;
                    case "burst": s.Burst = In(p, part.Name, 0.1f, 600); break;
                    case "reach": s.Reach = In(p, part.Name, 1, 400); break;
                    case "width": s.Width = In(p, part.Name, 1, 30); break;
                    default: throw new InvalidDataException($"\"{part.Name}\" has an unknown key \"{p.Name}\"");
                }
            table[part.Name] = s;
        }
    }

    private static float In(JsonProperty p, string part, float lo, float hi)
    {
        var v = p.Value.GetSingle();
        return v >= lo && v <= hi ? v : throw new InvalidDataException($"\"{part}\": \"{p.Name}\" is {lo} to {hi}, got {v}");
    }

    private static float Rand()
    {
        _seed = _seed * 1664525u + 1013904223u;
        return (_seed >> 8) / 16777216f;
    }

    /// <summary>A number 0..1 that is a part's own (so two valves don't vent together).</summary>
    private static float Own(Construct c, int i) => (uint)(c.Id * 2654435761u + (uint)i * 40503u) % 1024 / 1024f;

    /// <summary>A body's parts that do something, gathered again after a rebuild (the only allocation).</summary>
    private static void Gather(Construct c)
    {
        c.AmbStamp = c.RebuildStamp;
        var table = Table;
        var count = 0;
        if (table.Count > 0)
            for (var i = 0; i < c.PartCount; i++)
                if (Does(c, i, table))
                    count++;
        if (count == 0)
        {
            c.AmbParts = [];
            return;
        }
        var parts = new int[count];
        count = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (Does(c, i, table))
                parts[count++] = i;
        c.AmbParts = parts;
    }

    private static bool Does(Construct c, int i, Dictionary<string, Spec> table)
    {
        var d = c.DefOf(i);
        if (!table.TryGetValue(d.Name, out var s))
            return false;
        if (!s.Top)
            return true;
        // (the highest of its name on the body: a stack's top length)
        for (var k = 0; k < c.PartCount; k++)
            if (k != i && ReferenceEquals(c.DefOf(k), d) && (c.Parts[k].Pos.Y > c.Parts[i].Pos.Y || c.Parts[k].Pos.Y == c.Parts[i].Pos.Y && k < i))
                return false;
        return true;
    }

    /// <summary>The middle of a part's top face, in the world.</summary>
    private static Vector3 TopOf(Construct c, int i, Vector3 pos, Quaternion rot)
    {
        ref var p = ref c.Parts[i];
        var d = c.DefOf(i);
        var local = new Vector3((d.BoundsMin.X + d.BoundsMax.X) * 0.5f, d.BoundsMax.Y, (d.BoundsMin.Z + d.BoundsMax.Z) * 0.5f);
        var rest = p.Pos + Rot24.Apply(p.Rot, local);
        return c.FrameOf(p.Seg) == 0 ? pos + Vector3.Transform(rest, rot) : c.SegToWorld(p.Seg, rest);
    }

    /// <summary>A part that is doing something now: within <see cref="Range"/> of the eye at the last look round.</summary>
    private struct Live
    {
        public Construct Body;
        public int Part;
        public Spec Spec;
        public float Own;
    }

    private static Live[] _live = new Live[16];
    private static int _liveCount, _light = -1;
    private static double _nextLook, _nextDrop;
    private static float _drop = 40f;

    /// <summary>
    /// Twice a second: the parts near enough to do anything (<see cref="_live"/>) and the nearest searchlight among them.
    /// The frames between go through that short list alone, never the world's bodies.
    /// </summary>
    private static void LookRound(ConstructWorld world, Vector3 eye, double now)
    {
        if (now < _nextLook && now > _nextLook - 1.0)
            return;
        _nextLook = now + 0.5;
        Array.Clear(_live, 0, _liveCount); // (lets go of bodies that have gone)
        _liveCount = 0;
        _light = -1;
        var table = Table;
        if (table.Count == 0)
            return;
        var best = float.MaxValue;
        foreach (var c in world.Bodies)
        {
            if (c.SpaceAway != 0)
                continue;
            if (c.AmbStamp != c.RebuildStamp)
                Gather(c);
            if (c.AmbParts.Length == 0)
                continue;
            var d2 = Vector3.DistanceSquared(c.Pos, eye);
            if (d2 > (Range + 30f) * (Range + 30f))
                continue; // (a body's parts are within 30 m of its origin)
            foreach (var i in c.AmbParts)
            {
                if (c.Parts[i].Broken || !table.TryGetValue(c.DefOf(i).Name, out var s))
                    continue;
                if (_liveCount == _live.Length)
                    Array.Resize(ref _live, _live.Length * 2);
                if (s.Kind == Kind.Searchlight && d2 < best && d2 < 110f * 110f)
                {
                    best = d2;
                    _light = _liveCount;
                }
                _live[_liveCount++] = new Live { Body = c, Part = i, Spec = s, Own = Own(c, i) };
            }
        }
    }

    /// <summary>Is a live part still what it was (its body not rebuilt since the look round)?</summary>
    private static bool Still(in Live l) => l.Body.AmbStamp == l.Body.RebuildStamp && l.Part < l.Body.PartCount && !l.Body.Parts[l.Part].Broken;

    /// <summary>
    /// The frame's searchlight, before the lamps take what lights are left (<see cref="ConstructLamps.AddLights"/>): the
    /// nearest one within reach sweeps a pool of light over the ground, in the dark only (<see cref="ConstructLamps.Dark"/>).
    /// One dynamic light at most; the ground under the pool is found by one ray ten times a second.
    /// </summary>
    public static void AddLights(ConstructWorld world, Vector3 eye, double now, float alpha, bool blendTicks)
    {
        var dark = ConstructLamps.Dark;
        if (dark < 0.15f || LightGrid.DynamicFree <= 1)
            return;
        LookRound(world, eye, now);
        if (_light < 0 || _light >= _liveCount || !Still(in _live[_light]))
            return;
        ref var l = ref _live[_light];
        var at = TopOf(l.Body, l.Part, l.Body.Pos, l.Body.Rot);
        var dist = Vector3.Distance(at, eye);
        var fade = Math.Clamp((110f - dist) / 20f, 0f, 1f) * Math.Clamp((dark - 0.15f) / 0.3f, 0f, 1f);
        if (fade <= 0.01f)
            return;
        var up = GravityFrames.UpAt(at);
        var east = Vector3.Normalize(Vector3.Cross(MathF.Abs(up.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX, up));
        var north = Vector3.Cross(up, east);
        // A slow sweep to and fro over most of a circle, further out and nearer in as it goes.
        var t = now * 0.11 + l.Own * 40.0;
        var angle = (float)(Math.Sin(t) * 2.6 + l.Own * MathF.Tau);
        var reach = l.Spec.Reach * (0.75f + 0.25f * (float)Math.Sin(t * 2.3 + 1.0));
        var over = at + (east * MathF.Cos(angle) + north * MathF.Sin(angle)) * reach;
        if (now >= _nextDrop || now < _nextDrop - 1.0)
        {
            _nextDrop = now + 0.1;
            _drop = World.RaycastWorld(over, -up, 40f);
        }
        if (_drop < 40f)
            LightGrid.AddDynamic(over - up * (_drop - 2.2f), new Vector3(0.85f, 0.95f, 1.15f) * (1.5f * fade), l.Spec.Width);
    }

    /// <summary>Once a frame while a Sandbox world is drawn: the smoke, the steam, the tannoy and the snow along the ground.</summary>
    public static void Frame(ConstructWorld world, Vector3 eye, double now, float alpha, bool blendTicks, Audio? audio)
    {
        var dt = _last < 0 ? 0f : (float)Math.Clamp(now - _last, 0, 0.1);
        _last = now;
        if (dt <= 0)
            return;
        LookRound(world, eye, now);
        var wind = ColdStorm.Active ? ColdStorm.Wind : Vector3.Zero;
        for (var k = 0; k < _liveCount; k++)
        {
            ref var l = ref _live[k];
            var s = l.Spec;
            if (s.Kind == Kind.Searchlight || !Still(in l))
                continue;
            if (s.Kind == Kind.Steam && (now + l.Own * s.Every) % s.Every >= s.Burst)
                continue; // (between ventings)
            if (s.Kind == Kind.Tannoy && (audio == null || now < _nextTannoy && _nextTannoy - now < 900))
                continue;
            var at = TopOf(l.Body, l.Part, l.Body.Pos, l.Body.Rot);
            var dist = Vector3.Distance(at, eye);
            switch (s.Kind)
            {
                case Kind.Smoke:
                    Puff(FxKind.Smoke, at, s, wind, s.Rate * Math.Clamp((Range - dist) / (Range * 0.5f), 0f, 1f) * dt, 0.75f); // (fewer puffs far off, none at the range)
                    break;
                case Kind.Steam:
                    if (dist < 90f)
                        Puff(FxKind.Steam, at, s, wind, s.Rate * dt, 1f);
                    break;
                case Kind.Tannoy:
                    if (dist < s.Reach)
                        Announce(audio!, at, s, now);
                    break;
            }
        }
        if (ColdStorm.Active)
            BlownSnow(eye, now, dt);
    }

    private static void Puff(FxKind kind, Vector3 at, Spec s, Vector3 wind, float wanted, float alpha)
    {
        var up = GravityFrames.UpAt(at);
        for (var k = Fx.Share(wanted); k > 0; k--)
        {
            var jitter = new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) * 0.5f;
            Fx.Emit(kind, at + jitter * 0.3f, up * (s.Rise * (0.7f + 0.6f * Rand())) + wind * 0.45f + jitter, s.Size * (0.8f + 0.4f * Rand()), s.Life * (0.7f + 0.6f * Rand()), Fx.Tint(Vector3.One, alpha));
            Puffs++;
        }
    }

    /// <summary>One announcement now and then, from the first tannoy in earshot that the frame comes to.</summary>
    private static void Announce(Audio audio, Vector3 at, Spec s, double now)
    {
        _nextTannoy = now + s.Every * (0.7f + 0.6f * Rand());
        audio.Effect3D(Sfx.Tannoy, at, 0.9f, MixBus.Weather);
        Announcements++;
    }

    /// <summary>
    /// Snow snaking along the ground in a storm: thin streaks born on the ground round the camera, upwind more than down,
    /// and carried off by the wind. The ground is taken as level round you, at the height of one ray down a fifth of a second.
    /// </summary>
    private static void BlownSnow(Vector3 eye, double now, float dt)
    {
        var amount = ColdStorm.Level * ColdStorm.Outside;
        var wind = ColdStorm.Wind;
        var speed = wind.Length();
        if (amount < 0.1f || speed < 2f)
            return;
        var up = ColdStorm.Up;
        if (now >= _nextGround)
        {
            _nextGround = now + 0.2;
            _eyeHeight = World.RaycastWorld(eye, -up, 30f);
        }
        if (_eyeHeight >= 30f)
            return; // (flying high: no ground to blow along)
        var dir = wind / speed;
        var side = Vector3.Normalize(Vector3.Cross(up, dir));
        for (var k = Fx.Share(26f * amount * dt); k > 0; k--)
        {
            // (born up to 24 m upwind and 10 m down, 16 m either side: they blow through the view)
            var at = eye - up * (_eyeHeight - 0.08f - 0.25f * Rand()) + dir * (10f - 34f * Rand()) + side * (32f * Rand() - 16f);
            Fx.Emit(FxKind.Drift, at, wind * (0.75f + 0.35f * Rand()) + side * (Rand() - 0.5f) * 1.5f, 0.35f + 0.35f * Rand(), 1.2f + 1.2f * Rand());
            Puffs++;
        }
    }

    /// <summary>A new world or the menus: the clocks start afresh.</summary>
    public static void Reset()
    {
        _last = -1;
        _nextGround = _nextLook = _nextDrop = 0;
        Array.Clear(_live, 0, _liveCount);
        _liveCount = 0;
        _light = -1;
    }
}
