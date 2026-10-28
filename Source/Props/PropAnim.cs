using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// What an animated prop part does (the "type" of a part's "anim" block, see Props/README.md "Animation"). The numbers
/// are what the prop vertex shader switches on, so don't reorder them.
/// </summary>
public enum PropAnimType : byte
{
    /// <summary>No motion of its own: moves with its "parent" part (a gondola on a ferris wheel, a second fan blade).</summary>
    Follow = 0,

    /// <summary>Turns round and round about an axis (fan blades, windmills, turbines, radar dishes).</summary>
    Spin = 1,

    /// <summary>Swings back and forth about an axis (signs, pendulums, flags, rocking chairs).</summary>
    Swing = 2,

    /// <summary>Moves up and down (or along any axis) and back (buoys, pistons, bobbing ducks).</summary>
    Bob = 3,

    /// <summary>A small fast jitter along an axis (a running generator, a washing machine).</summary>
    Shake = 4,

    /// <summary>Light: on for "duty" of every period, then off (warning lights, neon signs).</summary>
    Blink = 5,

    /// <summary>Light: fades smoothly on and off (beacons, a glowing vending machine).</summary>
    Pulse = 6,

    /// <summary>Light: mostly on with random flickers (a dodgy bulb, a TV screen).</summary>
    Flicker = 7,

    /// <summary>Light: the period is cut into "steps"; it's on in the steps listed in "on" (traffic lights, crossing lamps).</summary>
    Cycle = 8,
}

/// <summary>The "anim" block of one prop part (cosmetic only: it never moves the prop's colliders).</summary>
public sealed class PropPartAnim
{
    public PropAnimType Type;

    /// <summary>Unit axis in the prop's frame (spin, swing: turned about; bob, shake: moved along).</summary>
    public Vector3 Axis = Vector3.UnitY;

    /// <summary>The point it turns about, in the prop's frame. Null in the file means the part's own centre (set by <see cref="PropAnimFile.Validate"/>).</summary>
    public Vector3? Pivot;

    public float Rpm, AngleDeg, Distance, Period = 1f, Phase, Jitter, Wind, Rate = 10f, Duty = 0.5f;

    /// <summary>Swing only: swings out to one side and back, resting in between (a boom gate, a Newton's cradle ball).</summary>
    public bool OneWay;

    /// <summary>Each copy of the prop starts at a different point of the cycle (from its position), so rows don't move in lockstep.</summary>
    public bool RandomPhase = true;

    /// <summary>The part (0-based) this one moves with, or -1.</summary>
    public int Parent = -1;

    /// <summary>Light types: colour when on and when off (null: the part's colour, and a dark version of it).</summary>
    public Vector3? OnColor, OffColor;

    /// <summary>How much it glows (added light that ignores the sun and shade), 0-4. Negative: the type's default.</summary>
    public float Glow = -1f;

    /// <summary>Cycle: how many steps the period is cut into, and a bit per step that's lit.</summary>
    public int Steps, OnMask;

    /// <summary>Beyond this distance (metres) the prop holds still in its rest pose. 0: <see cref="PropAnim.DefaultFreezeDistance"/>.</summary>
    public float FreezeDistance;

    public bool IsLight => Type >= PropAnimType.Blink;
    public bool Moves => Type is PropAnimType.Spin or PropAnimType.Swing or PropAnimType.Bob or PropAnimType.Shake;
}

/// <summary>
/// Reads and checks the "anim" block of prop parts (kept out of <see cref="PropFile"/> so the animation format stays in
/// one place). Every key is checked, like the rest of a prop file: unknown keys and numbers out of range are errors that
/// name the file and the part.
/// </summary>
public static class PropAnimFile
{
    /// <summary>The longest chain of parts moving with each other (part, parent, grandparent...).</summary>
    public const int MaxChain = 4;

    private static readonly string Types = "spin, swing (or oscillate), bob (or translate), shake, blink, pulse, flicker, cycle, follow";

    /// <summary>Reads a part's "anim" block (<paramref name="where"/> names the file and part for errors).</summary>
    public static PropPartAnim Read(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: \"anim\" is an object like {{ \"type\": \"spin\", \"axis\": \"y\", \"rpm\": 60 }}");
        var a = new PropPartAnim();
        string? type = null;
        var axisGiven = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"anim\" \"{p.Name}\")";
            if (!seen.Add(p.Name))
                throw new PropFileException($"{w}: given twice");
            switch (p.Name.ToLowerInvariant())
            {
                case "type":
                    type = Text(p.Value, w).ToLowerInvariant();
                    break;
                case "axis":
                    a.Axis = Axis(p.Value, w);
                    axisGiven = true;
                    break;
                case "pivot":
                    a.Pivot = Vec(p.Value, w);
                    break;
                case "rpm":
                    a.Rpm = Range(Num(p.Value, w), -600, 600, w);
                    if (MathF.Abs(a.Rpm) < 0.1f)
                        throw new PropFileException($"{w}: at least 0.1 turns a minute (negative turns the other way)");
                    break;
                case "angle":
                    a.AngleDeg = Range(Num(p.Value, w), 0.1f, 180, w);
                    break;
                case "distance":
                    a.Distance = Range(Num(p.Value, w), 0.001f, 3, w);
                    break;
                case "period":
                    a.Period = Range(Num(p.Value, w), 0.05f, 120, w);
                    break;
                case "phase":
                    a.Phase = Range(Num(p.Value, w), 0, 1, w);
                    break;
                case "oneway":
                    a.OneWay = Bool(p.Value, w);
                    break;
                case "randomphase":
                    a.RandomPhase = Bool(p.Value, w);
                    break;
                case "jitter":
                    a.Jitter = Range(Num(p.Value, w), 0, 0.5f, w);
                    break;
                case "wind":
                    a.Wind = Range(Num(p.Value, w), 0, 1, w);
                    break;
                case "rate":
                    a.Rate = Range(Num(p.Value, w), 0.5f, 60, w);
                    break;
                case "duty":
                    a.Duty = Range(Num(p.Value, w), 0, 1, w);
                    break;
                case "color":
                case "colour":
                    a.OnColor = PropFile.Color(p.Value, w);
                    break;
                case "off":
                    a.OffColor = PropFile.Color(p.Value, w);
                    break;
                case "glow":
                    a.Glow = Range(Num(p.Value, w), 0, 4, w);
                    break;
                case "steps":
                    a.Steps = (int)Range(Num(p.Value, w), 2, 16, w);
                    break;
                case "on":
                    if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() == 0)
                        throw new PropFileException($"{w}: a list of step numbers (0 = the first step), e.g. [0, 1]");
                    foreach (var s in p.Value.EnumerateArray())
                    {
                        var step = Num(s, w);
                        if (step != MathF.Floor(step) || step < 0 || step > 15)
                            throw new PropFileException($"{w}: step numbers are whole numbers 0 to 15, got {s}");
                        a.OnMask |= 1 << (int)step;
                    }
                    break;
                case "parent":
                    var n = Num(p.Value, w);
                    if (n != MathF.Floor(n) || n < 1 || n > PropFile.MaxParts)
                        throw new PropFileException($"{w}: the number of the part it moves with (1 = the first part in \"parts\")");
                    a.Parent = (int)n - 1;
                    break;
                case "freezedistance":
                    a.FreezeDistance = Range(Num(p.Value, w), 10, 400, w);
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. \"anim\" uses: type, axis, pivot, rpm, angle, distance, period, phase, oneWay, randomPhase, "
                        + "jitter, wind, rate, duty, color, off, glow, steps, on, parent, freezeDistance");
            }
        }
        if (type == null)
            throw new PropFileException($"{where}: \"anim\" needs a \"type\": {Types}");
        a.Type = type switch
        {
            "spin" or "rotate" => PropAnimType.Spin,
            "swing" or "oscillate" => PropAnimType.Swing,
            "bob" or "translate" => PropAnimType.Bob,
            "shake" or "vibrate" => PropAnimType.Shake,
            "blink" => PropAnimType.Blink,
            "pulse" => PropAnimType.Pulse,
            "flicker" => PropAnimType.Flicker,
            "cycle" => PropAnimType.Cycle,
            "follow" => PropAnimType.Follow,
            _ => throw new PropFileException($"{where}: unknown anim type \"{type}\". Types: {Types}"),
        };
        if (!axisGiven && a.Type == PropAnimType.Swing)
            a.Axis = Vector3.UnitX;
        // Each type's own keys must be there; keys that don't apply are errors (a typo'd intent shouldn't do nothing).
        void Need(bool ok, string what)
        {
            if (!ok)
                throw new PropFileException($"{where}: anim type \"{type}\" needs {what}");
        }
        void NotFor(bool given, string key)
        {
            if (given)
                throw new PropFileException($"{where}: \"{key}\" doesn't apply to anim type \"{type}\"");
        }
        switch (a.Type)
        {
            case PropAnimType.Spin:
                Need(a.Rpm != 0, "\"rpm\" (turns a minute; negative turns the other way)");
                NotFor(seen.Contains("period"), "period");
                NotFor(seen.Contains("angle"), "angle");
                NotFor(seen.Contains("distance"), "distance");
                break;
            case PropAnimType.Swing:
                Need(a.AngleDeg > 0, "\"angle\" (degrees either way)");
                break;
            case PropAnimType.Bob:
            case PropAnimType.Shake:
                Need(a.Distance > 0, "\"distance\" (metres either way)");
                if (a.Type == PropAnimType.Shake && !seen.Contains("period"))
                    a.Period = 0.08f;
                break;
            case PropAnimType.Cycle:
                Need(a.Steps >= 2, "\"steps\" (2 to 16)");
                Need(a.OnMask != 0, "\"on\": the steps it's lit in, e.g. [0]");
                if (a.OnMask >> a.Steps != 0)
                    throw new PropFileException($"{where}: \"on\" lists a step past the last one ({a.Steps} steps are numbered 0 to {a.Steps - 1})");
                break;
            case PropAnimType.Follow:
                Need(a.Parent >= 0, "\"parent\": the number of the part it moves with");
                break;
        }
        if (!a.Moves && a.Type != PropAnimType.Follow)
        {
            NotFor(seen.Contains("rpm"), "rpm");
            NotFor(seen.Contains("angle"), "angle");
            NotFor(seen.Contains("distance"), "distance");
            NotFor(seen.Contains("axis"), "axis");
            NotFor(seen.Contains("pivot"), "pivot");
            NotFor(seen.Contains("wind"), "wind");
        }
        if (a.Type != PropAnimType.Cycle)
        {
            NotFor(seen.Contains("steps"), "steps");
            NotFor(seen.Contains("on"), "on");
        }
        if (a.Type != PropAnimType.Flicker)
            NotFor(seen.Contains("rate"), "rate");
        if (a.Type != PropAnimType.Swing)
            NotFor(seen.Contains("oneWay"), "oneWay");
        if (!a.IsLight)
        {
            NotFor(seen.Contains("off"), "off");
            NotFor(seen.Contains("duty"), "duty");
        }
        if (a.Type == PropAnimType.Follow)
            NotFor(seen.Count > 2 + (seen.Contains("freezeDistance") ? 1 : 0), "anything but parent (and freezeDistance)");
        return a;
    }

    /// <summary>
    /// Checks the anim blocks of a whole prop once its parts are read: parents exist, move and don't loop, chains are
    /// short, pivots are near the model. Sets each part's default pivot (its centre).
    /// </summary>
    public static void Validate(PropDef def, string source)
    {
        var parts = def.Parts;
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].Anim is not { } a)
                continue;
            var where = $"{source}, part #{i + 1} (\"anim\")";
            a.Pivot ??= parts[i].Offset;
            var b = def.Bounds;
            var pv = a.Pivot.Value;
            if (pv.X < b.Min.X - 1 || pv.X > b.Max.X + 1 || pv.Y < b.Min.Y - 1 || pv.Y > b.Max.Y + 1 || pv.Z < b.Min.Z - 1 || pv.Z > b.Max.Z + 1)
                throw new PropFileException($"{where}: pivot [{F(pv.X)}, {F(pv.Y)}, {F(pv.Z)}] is more than 1 m outside the model; the pivot is in the prop's frame like \"offset\"");
            if (a.Parent < 0)
                continue;
            if (a.Parent == i)
                throw new PropFileException($"{where}: \"parent\" is the part itself");
            if (a.Parent >= parts.Count)
                throw new PropFileException($"{where}: \"parent\" is part #{a.Parent + 1} but the prop has {parts.Count} parts");
            if (parts[a.Parent].Anim == null)
                throw new PropFileException($"{where}: \"parent\" part #{a.Parent + 1} has no \"anim\" block, so there's nothing to move with");
            var depth = 1;
            var at = i;
            while (parts[at].Anim is { Parent: >= 0 } link)
            {
                at = link.Parent;
                if (at == i || ++depth > MaxChain)
                    throw new PropFileException(at == i
                        ? $"{where}: parents go round in a loop"
                        : $"{where}: a chain of more than {MaxChain} parts moving with each other");
            }
        }
    }

    // ---- Value helpers (same messages as PropFile's) ------------------------------------------------

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Text(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new PropFileException($"{where}: expected text, got {e}");

    private static float Num(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle()) ? e.GetSingle() : throw new PropFileException($"{where}: expected a number, got {e}");

    private static bool Bool(JsonElement e, string where) =>
        e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : throw new PropFileException($"{where}: expected true or false, got {e}");

    private static float Range(float v, float lo, float hi, string where) =>
        v >= lo && v <= hi ? v : throw new PropFileException($"{where}: {v.ToString(CultureInfo.InvariantCulture)} is outside {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");

    private static Vector3 Vec(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3
            ? new Vector3(Num(e[0], where), Num(e[1], where), Num(e[2], where))
            : throw new PropFileException($"{where}: expected [x, y, z], got {e}");

    private static Vector3 Axis(JsonElement e, string where)
    {
        if (e.ValueKind == JsonValueKind.String)
            return e.GetString()!.ToLowerInvariant() switch
            {
                "x" => Vector3.UnitX,
                "y" => Vector3.UnitY,
                "z" => Vector3.UnitZ,
                "-x" => -Vector3.UnitX,
                "-y" => -Vector3.UnitY,
                "-z" => -Vector3.UnitZ,
                var s => throw new PropFileException($"{where}: \"x\", \"y\", \"z\" (or \"-x\"...) or [x, y, z], got \"{s}\""),
            };
        var v = Vec(e, where);
        if (v.Length() < 1e-3f)
            throw new PropFileException($"{where}: the axis [0, 0, 0] has no direction");
        return Vector3.Normalize(v);
    }
}

/// <summary>
/// Prop animation maths shared by the CPU (self-test, reference) and the prop vertex shader, which mirrors
/// <see cref="Animate"/> line for line. Animation is a pure function of time and the instance's position (its seed):
/// nothing is stored or stepped per frame.
/// </summary>
public static class PropAnim
{
    /// <summary>Beyond this distance (metres, along the ground) animated props hold still in their rest pose unless a part says otherwise.</summary>
    public const float DefaultFreezeDistance = 80f;

    /// <summary>Over the last part of the freeze distance swings, bobs and wind fade out, so they settle instead of stopping dead.</summary>
    public const float FadeBand = 0.15f;

    /// <summary>The shader's time wraps after this many seconds (keeps float precision; one jump every ~68 minutes).</summary>
    public const double TimeWrap = 4096.0;

    /// <summary>Shader time for a game clock reading.</summary>
    public static float ShaderTime(double now) => (float)(now % TimeWrap);

    public static float Fract(float x) => x - MathF.Floor(x);

    /// <summary>A 0..1 seed from a prop's position (deterministic, the same in the shader): each copy gets its own phase.</summary>
    public static float Seed(Vector3 pos)
    {
        var x = MathF.Floor(pos.X * 8f + 0.5f);
        var z = MathF.Floor(pos.Z * 8f + 0.5f);
        return Fract(52.9829189f * Fract(x * 0.06711056f + z * 0.00583715f));
    }

    /// <summary>
    /// Where a prop is in the foliage's gust waves (they travel across the map), so flags and turbines gust with the grass
    /// next to them. Add the seed's share (seed * 2) for the phase the shader uses.
    /// </summary>
    public static float GustWave(Vector3 pos) => pos.X * 0.11f + pos.Z * 0.07f;

    /// <summary>The speed multiplier from a part's jitter (0..0.5) and the seed.</summary>
    public static float SpeedMul(float jitter, float seed) => 1f + jitter * (2f * Fract(seed * 7.13f + 0.37f) - 1f);

    /// <summary>Should a chunk whose nearest point is <paramref name="nearDistance"/> metres away (along the ground) be animated at all?</summary>
    public static bool ShouldAnimate(float nearDistance, float freezeDistance) => nearDistance < freezeDistance;

    /// <summary>The motion strength for an instance <paramref name="dist"/> metres away: 1 near, fading to 0 at the freeze distance.</summary>
    public static float Strength(float dist, float freezeDistance) =>
        Math.Clamp((freezeDistance - dist) / (freezeDistance * FadeBand), 0f, 1f);

    /// <summary>
    /// A vertex of an animated part, in the prop's frame: where it is at time <paramref name="t"/> (shader time) for an
    /// instance with this seed and gust wave (<see cref="GustWave"/>), <paramref name="dist"/> metres from the camera. <paramref name="light"/> is the light
    /// level 0..1 of the part's own light type, or -1. Beyond the freeze distance everything is the rest pose.
    /// </summary>
    public static Vector3 Animate(float[] records, int record, Vector3 p, float t, float seed, float wave, float dist, float freeze, out float light)
    {
        light = -1f;
        if (record < 0 || dist >= freeze)
            return p;
        var k = Strength(dist, freeze);
        var r = record;
        for (var depth = 0; depth < PropAnimFile.MaxChain && r >= 0; depth++)
        {
            var o = r * PropAnimData.RecordFloats;
            var type = (PropAnimType)(int)records[o];
            var parent = (int)records[o + 1];
            var rate = records[o + 2];
            var amp = records[o + 3];
            var pivot = new Vector3(records[o + 4], records[o + 5], records[o + 6]);
            var phase = records[o + 7];
            var axis = new Vector3(records[o + 8], records[o + 9], records[o + 10]);
            var jitter = records[o + 11];
            var duty = records[o + 19];
            var steps = records[o + 20];
            var mask = records[o + 21];
            var wind = records[o + 22] * k;
            var randomPhase = records[o + 23];
            var u = t * rate * SpeedMul(jitter, seed) + phase + randomPhase * seed;
            var gustPhase = wave + seed * 2f;
            var gust = MathF.Sin(t * 1.3f - gustPhase) * 0.6f + MathF.Sin(t * 2.7f - gustPhase * 1.7f) * 0.3f;
            switch (type)
            {
                case PropAnimType.Spin:
                {
                    // Gusts speed it up and slow it down: the integral of the foliage's gust wave, so the angle stays smooth.
                    var angle = MathF.Tau * u + wind * MathF.Tau * rate * (-MathF.Cos(t * 1.3f - gustPhase) * 0.46f - MathF.Cos(t * 2.7f - gustPhase * 1.7f) * 0.11f);
                    p = pivot + Vector3.Transform(p - pivot, Quaternion.CreateFromAxisAngle(axis, angle));
                    break;
                }
                case PropAnimType.Swing:
                {
                    var sv = MathF.Sin(MathF.Tau * u);
                    if (duty > 0.5f)
                        sv = MathF.Max(sv, 0f); // one way: out and back, resting in between
                    var angle = amp * k * sv * (1f + wind * gust * 0.6f);
                    p = pivot + Vector3.Transform(p - pivot, Quaternion.CreateFromAxisAngle(axis, angle));
                    break;
                }
                case PropAnimType.Bob:
                    p += axis * (amp * k * MathF.Sin(MathF.Tau * u) * (1f + wind * gust * 0.6f));
                    break;
                case PropAnimType.Shake:
                    p += axis * (amp * k * (MathF.Sin(MathF.Tau * u) * 0.6f + MathF.Sin(MathF.Tau * u * 2.37f + 1.3f) * 0.4f));
                    break;
                default:
                    if (depth == 0 && type >= PropAnimType.Blink)
                        light = type == PropAnimType.Flicker && steps < -0.5f ? FlameLevel(LightFlicker.PropLightSeed(seed, (int)(-steps - 0.5f)), mask, duty, t)
                            : Light(type, u, duty, steps, mask, seed);
                    break;
            }
            r = parent;
        }
        return p;
    }

    /// <summary>A flame part's light level 0..1 at shader time <paramref name="t"/>: its light's own flicker (<see cref="LightFlicker"/>), 0.72 on average.</summary>
    public static float FlameLevel(float lightSeed, float speed, float amount, float t)
    {
        var (z, mix) = LightFlicker.Params(lightSeed, speed, amount);
        var r = z.Length();
        return r < 1e-5f ? 0.72f : Math.Clamp(0.72f + 0.28f * LightFlicker.Wave(z, mix, t) / r, 0f, 1f);
    }

    /// <summary>The light level 0..1 of a light type at cycle position <paramref name="u"/>.</summary>
    public static float Light(PropAnimType type, float u, float duty, float steps, float mask, float seed)
    {
        switch (type)
        {
            case PropAnimType.Blink:
                return Fract(u) < duty ? 1f : 0f;
            case PropAnimType.Pulse:
                return 0.5f - 0.5f * MathF.Cos(MathF.Tau * u);
            case PropAnimType.Flicker:
            {
                var n = Fract(52.9829189f * Fract(MathF.Floor(u) * 0.06711056f + seed * 17.0f));
                return n < duty ? 1f : 0.15f + 0.3f * n;
            }
            case PropAnimType.Cycle:
            {
                var step = MathF.Floor(Fract(u) * steps);
                return MathF.Floor(mask / MathF.Pow(2f, step)) % 2f >= 1f ? 1f : 0f;
            }
            default:
                return -1f;
        }
    }
}

/// <summary>
/// The GPU-ready animation data for a prop library (no GL here, so the self-test can check it), built alongside
/// <see cref="PropMeshes"/> and leaving it untouched: static props' vertices are exactly what they were.
/// <list type="bullet">
/// <item><see cref="VertexRecord"/>: one float per mesh vertex, the animation record of its part or -1 (a second vertex
/// stream only the animated draw path reads).</item>
/// <item><see cref="Records"/>: <see cref="RecordTexels"/> RGBA texels per animated part (a float texture the vertex
/// shader reads with texelFetch): type, parent record, rate (cycles a second), amplitude | pivot, phase | axis, jitter |
/// on colour, glow | off colour, duty (swing: 1 = one way) | steps, on-mask, wind, random phase.</item>
/// </list>
/// </summary>
public sealed class PropAnimData
{
    public const int RecordTexels = 6;
    public const int RecordFloats = RecordTexels * 4;

    public float[] VertexRecord = [];
    public float[] Records = [];
    public int RecordCount;

    /// <summary>[def index]: does the prop have any animated part?</summary>
    public bool[] Animated = [];

    /// <summary>[def index]: how far (prop units) an animated vertex can get from its rest position (grows the cull box).</summary>
    public float[] Reach = [];

    /// <summary>[def index]: beyond this distance the prop is drawn still, by the plain prop shader.</summary>
    public float[] FreezeDistance = [];

    /// <summary>[def index]: the first record of each animated part, by part (-1 for still parts).</summary>
    public int[][] PartRecord = [];

    public int AnimatedTypes, AnimatedParts;

    /// <summary>The number of the flickering light nearest <paramref name="at"/> in the prop's lights, or -1 (none flickers).</summary>
    public static int FlameLight(PropDef def, Vector3 at)
    {
        var best = -1;
        var bestD = float.MaxValue;
        for (var k = 0; k < def.Lights.Length; k++)
        {
            if (!def.Lights[k].Flicker)
                continue;
            var d = Vector3.DistanceSquared(def.Lights[k].Pos, at);
            if (d < bestD)
            {
                bestD = d;
                best = k;
            }
        }
        return best;
    }

    public static PropAnimData Build(PropMeshes meshes)
    {
        var defs = meshes.Defs;
        var vertexCount = meshes.Vertices.Length / PropMeshes.FloatsPerVertex;
        var data = new PropAnimData
        {
            VertexRecord = new float[vertexCount],
            Animated = new bool[defs.Length],
            Reach = new float[defs.Length],
            FreezeDistance = new float[defs.Length],
            PartRecord = new int[defs.Length][],
        };
        Array.Fill(data.VertexRecord, -1f);
        var records = new List<float>();
        for (var d = 0; d < defs.Length; d++)
        {
            var def = defs[d];
            var parts = def.Parts;
            data.PartRecord[d] = new int[parts.Count];
            Array.Fill(data.PartRecord[d], -1);
            data.FreezeDistance[d] = PropAnim.DefaultFreezeDistance;
            if (!parts.Any(p => p.Anim != null))
                continue;
            data.Animated[d] = true;
            data.AnimatedTypes++;
            var first = records.Count / RecordFloats;
            var n = 0;
            for (var i = 0; i < parts.Count; i++)
                if (parts[i].Anim != null)
                    data.PartRecord[d][i] = first + n++;
            var freeze = 0f;
            for (var i = 0; i < parts.Count; i++)
            {
                if (parts[i].Anim is not { } a)
                    continue;
                // A flickering part on a prop with a flickering light (a torch's flame, a candle) follows that light's own
                // flicker (the nearest one, LightFlicker), so the flame and the light it throws move together.
                var flame = a.Type == PropAnimType.Flicker ? FlameLight(def, parts[i].Offset) : -1;
                data.AnimatedParts++;
                freeze = MathF.Max(freeze, a.FreezeDistance);
                var pv = a.Pivot ?? parts[i].Offset;
                var rate = a.Type switch
                {
                    PropAnimType.Spin => a.Rpm / 60f,
                    PropAnimType.Flicker => a.Rate,
                    _ => 1f / a.Period,
                };
                var amp = a.Type == PropAnimType.Swing ? a.AngleDeg * (MathF.PI / 180f) : a.Distance;
                var on = a.OnColor ?? parts[i].Color;
                var off = a.OffColor ?? parts[i].Color * 0.28f;
                var glow = a.Glow >= 0 ? a.Glow : a.IsLight ? 0.6f : 0f;
                var parent = a.Parent >= 0 ? data.PartRecord[d][a.Parent] : -1;
                records.AddRange([
                    (float)a.Type, parent, rate, amp,
                    pv.X, pv.Y, pv.Z, a.Phase,
                    a.Axis.X, a.Axis.Y, a.Axis.Z, a.Jitter,
                    on.X, on.Y, on.Z, glow,
                    off.X, off.Y, off.Z, a.Type == PropAnimType.Swing ? (a.OneWay ? 1f : 0f) : flame >= 0 ? def.Lights[flame].FlickerAmount : a.Duty,
                    flame >= 0 ? -1f - flame : a.Steps, flame >= 0 ? def.Lights[flame].FlickerSpeed : a.OnMask, a.Wind, a.RandomPhase ? 1f : 0f,
                ]);
            }
            data.FreezeDistance[d] = freeze > 0 ? freeze : PropAnim.DefaultFreezeDistance;

            // Each part's vertices in the shared buffer: parts are laid out in order from the prop's first vertex, and a
            // part's vertex count is what PropMeshes makes of it alone (so this follows any change to the mesh builder).
            var range = meshes.Ranges[d];
            var vStart = int.MaxValue;
            for (var k = 0; k < range.IndexCount; k++)
                vStart = Math.Min(vStart, (int)meshes.Indices[range.FirstIndex + k]);
            var v = vStart;
            var reach = 0f;
            for (var i = 0; i < parts.Count; i++)
            {
                var one = new PropDef { Name = "" };
                one.Parts.Add(parts[i]);
                var count = PropMeshes.Build([one]).Vertices.Length / PropMeshes.FloatsPerVertex;
                var rec = data.PartRecord[d][i];
                if (rec >= 0)
                {
                    for (var k = 0; k < count && v + k < vertexCount; k++)
                        data.VertexRecord[v + k] = rec;
                    reach = MathF.Max(reach, ChainReach(parts, i));
                }
                v += count;
            }
            data.Reach[d] = MathF.Min(reach, PropFile.MaxSize);
        }
        data.Records = records.ToArray();
        data.RecordCount = data.Records.Length / RecordFloats;
        return data;
    }

    /// <summary>A bound on how far part <paramref name="i"/>'s corners can move: every link of its chain added up.</summary>
    private static float ChainReach(List<PropPart> parts, int i)
    {
        var b = parts[i].Bounds;
        var total = 0f;
        var at = i;
        for (var depth = 0; depth < PropAnimFile.MaxChain && at >= 0 && parts[at].Anim is { } a; depth++)
        {
            var pv = a.Pivot ?? parts[at].Offset;
            var far = Vector3.Max(Vector3.Abs(b.Min - pv), Vector3.Abs(b.Max - pv)).Length() + total;
            total += a.Type switch
            {
                PropAnimType.Spin => 2f * far,
                PropAnimType.Swing => 2f * far * MathF.Sin(MathF.Min(a.AngleDeg * 1.6f, 90f) * MathF.PI / 180f),
                PropAnimType.Bob or PropAnimType.Shake => a.Distance * 1.6f,
                _ => 0f,
            };
            at = a.Parent;
        }
        return total;
    }
}
