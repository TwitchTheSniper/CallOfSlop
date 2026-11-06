using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>The kinds in Materials/Particles.json, in its order.</summary>
public enum FxKind : byte
{
    Flame,
    Jet,
    Rocket,
    Smoke,
    DarkSmoke,
    Spark,
    Ember,
    Dust,
    Flash,
    Fireball,
    Steam,
    Count,
}

/// <summary>One particle as the pool holds it, on the GPU or in the fallback's array (48 bytes; the shader's struct P).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FxParticle
{
    public Vector3 Pos;
    public float Age;
    public Vector3 Vel;
    public float Life; // 0: a free slot
    public float Size, Seed, Kind;
    public uint Tint; // RGBA8: rgb times the kind's colour, a times its alpha
}

/// <summary>
/// The game's particles (docs/render/MATERIALS.md, "Effects"): flames, smoke, sparks, dust, blasts. One pool, one kind table
/// (Materials/Particles.json), one way in: everything that wants an effect calls an emitter here, on any peer, from state that
/// peer has; nothing here touches the simulation or goes on the wire. <see cref="Frame"/> hands the frame's births to the
/// pool. On the 4.6 path (<see cref="RenderCaps.GpuParticles"/>) they go through a persistent ring into a storage buffer and compute
/// shaders do the rest, so the CPU's cost is the births only, whatever is alive. On the 4.1 path the same particles, with the same
/// shader, step in <see cref="StepCpu"/> over a pool a tenth the size. Nothing allocates.
/// </summary>
public static class Fx
{
    /// <summary>Births a frame at most; more are dropped (a frame of many blasts thins out, it doesn't stall).</summary>
    public const int MaxBirths = 8192;

    /// <summary>The pool's size by setting: GPU pool, fallback pool.</summary>
    public static int Capacity(VideoLevel level, bool gpu) => gpu
        ? level switch { VideoLevel.Low => 16384, VideoLevel.Medium => 65536, _ => 131072 }
        : level switch { VideoLevel.Low => 512, VideoLevel.Medium => 1024, _ => 2048 };

    /// <summary>Settings > Video > Particles.</summary>
    public static VideoLevel Level = VideoLevel.Medium;

    /// <summary>A window is up and draws them: emitters do nothing otherwise (a dedicated run, the headless self-tests).</summary>
    public static bool On;

    /// <summary>The GPU pool is running (else the fallback).</summary>
    public static bool Gpu;

    /// <summary>What share of what is asked for is made: fewer on the lower settings, and fewer again on the fallback.</summary>
    public static float Density => (Level switch { VideoLevel.Low => 0.4f, VideoLevel.Medium => 0.75f, _ => 1f }) * (Gpu ? 1f : 0.3f);

    internal static readonly FxParticle[] Births = new FxParticle[MaxBirths];
    internal static int BirthCount;

    /// <summary>Particles asked for, and dropped for a full frame (the F9 window; a self-test can read them headless).</summary>
    public static long Emitted, Dropped;

    private static uint _seed = 2463534242;

    private static float Rand()
    {
        _seed ^= _seed << 13;
        _seed ^= _seed >> 17;
        _seed ^= _seed << 5;
        return (_seed >> 8) / 16777216f;
    }

    private static Vector3 RandDir()
    {
        var y = Rand() * 2f - 1f;
        var a = Rand() * MathF.Tau;
        var r = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
        return new Vector3(r * MathF.Cos(a), y, r * MathF.Sin(a));
    }

    public const uint White = 0xFFFFFFFF;

    /// <summary>A tint in the pool's packing (a: 0..1 of the kind's alpha).</summary>
    public static uint Tint(Vector3 c, float a = 1f)
    {
        static uint B(float v) => (uint)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
        return B(c.X) | B(c.Y) << 8 | B(c.Z) << 16 | B(a) << 24;
    }

    /// <summary>One particle.</summary>
    public static void Emit(FxKind kind, Vector3 pos, Vector3 vel, float size, float life, uint tint = White)
    {
        if (!On)
            return;
        Emitted++;
        if (BirthCount >= MaxBirths)
        {
            Dropped++;
            return;
        }
        ref var p = ref Births[BirthCount++];
        p.Pos = pos;
        p.Age = 0;
        p.Vel = vel;
        p.Life = life;
        p.Size = size;
        p.Seed = Rand();
        p.Kind = (float)kind;
        p.Tint = tint;
    }

    /// <summary>How many of <paramref name="wanted"/> (a rate times the frame, say) to make now: whole, the setting's share of it, the fraction by chance.</summary>
    public static int Share(float wanted)
    {
        if (!On)
            return 0;
        var w = wanted * Density;
        var n = (int)w;
        return n + (Rand() < w - n ? 1 : 0);
    }

    /// <summary>A blast: flash, fireball, sparks, then smoke and a ring of dust.</summary>
    public static void Blast(Vector3 at, float radius, Vector3 tint)
    {
        var r = MathF.Max(radius, 0.5f);
        var t = tint == default ? White : Tint(Vector3.Lerp(tint, Vector3.One, 0.35f));
        Emit(FxKind.Flash, at, Vector3.Zero, r * 1.3f, 0.16f, t);
        for (var k = Share(30f + 14f * r); k > 0; k--)
        {
            var d = RandDir();
            Emit(FxKind.Fireball, at + d * (r * 0.25f * Rand()), d * (r * (1.6f + 2.5f * Rand())), r * (0.3f + 0.25f * Rand()), 0.4f + 0.45f * Rand(), t);
        }
        for (var k = Share(60f + 30f * r); k > 0; k--)
        {
            var d = RandDir();
            Emit(FxKind.Spark, at, d * (r * (3f + 9f * Rand())), 0.05f + 0.05f * Rand(), 0.35f + 0.6f * Rand());
        }
        for (var k = Share(14f + 8f * r); k > 0; k--)
        {
            var d = RandDir();
            d.Y = MathF.Abs(d.Y);
            Emit(FxKind.DarkSmoke, at + d * (r * 0.4f), d * (r * (0.5f + 1.2f * Rand())) + new Vector3(0, r * 0.4f, 0), r * (0.45f + 0.3f * Rand()), 2f + 2.4f * Rand());
        }
        for (var k = Share(10f + 6f * r); k > 0; k--)
        {
            var a = Rand() * MathF.Tau;
            var d = new Vector3(MathF.Cos(a), 0.05f, MathF.Sin(a));
            Emit(FxKind.Dust, at, d * (r * (1.4f + 1.4f * Rand())), r * 0.35f, 1.1f + 1.4f * Rand());
        }
    }

    /// <summary>A round hitting something: sparks off metal, chips and dust off the rest. <paramref name="surface"/> is ImpactDecals' name for it.</summary>
    public static void Impact(Vector3 at, Vector3 normal, string surface)
    {
        var metal = surface is "Metal" or "Grid";
        if (metal)
            for (var k = Share(9f); k > 0; k--)
            {
                var d = Vector3.Normalize(normal + RandDir() * 0.75f);
                Emit(FxKind.Spark, at, d * (3f + 8f * Rand()), 0.018f + 0.016f * Rand(), 0.16f + 0.3f * Rand());
            }
        else
            for (var k = Share(4f); k > 0; k--)
            {
                var d = Vector3.Normalize(normal + RandDir() * 0.5f);
                Emit(FxKind.Dust, at + normal * 0.03f, d * (0.7f + 1.3f * Rand()), 0.07f + 0.09f * Rand(), 0.45f + 0.6f * Rand());
            }
    }

    /// <summary>A shot: a quick flash, a few sparks along the barrel and a wisp of smoke.</summary>
    public static void Muzzle(Vector3 at, Vector3 dir, float size)
    {
        Emit(FxKind.Flash, at + dir * (size * 0.5f), dir * 3f, size, 0.055f);
        for (var k = Share(4f); k > 0; k--)
            Emit(FxKind.Spark, at, (dir + RandDir() * 0.25f) * (6f + 14f * Rand()), 0.012f + 0.012f * Rand(), 0.07f + 0.1f * Rand());
        for (var k = Share(1.5f); k > 0; k--)
            Emit(FxKind.Smoke, at, dir * (0.5f + Rand()) + new Vector3(0, 0.25f, 0), size * 0.5f, 0.6f + 0.6f * Rand());
    }

    /// <summary>Puffs of engine or stack smoke at <paramref name="at"/>, moving at <paramref name="vel"/>; <paramref name="dark"/>: 0 thin and grey, 1 a black column.</summary>
    public static void EngineSmoke(Vector3 at, Vector3 vel, float size, float life, float dark)
    {
        Emit(dark > 0.5f ? FxKind.DarkSmoke : FxKind.Smoke, at, vel, size * 1.3f, life, Tint(Vector3.One, 0.45f + 0.55f * MathF.Abs(dark * 2f - 1f)));
    }

    /// <summary>
    /// A nozzle or burner for this frame: particles leave <paramref name="at"/> along <paramref name="dir"/> at a speed that sets
    /// how long the flame is. <paramref name="level"/> 0..1 is how hard it burns, <paramref name="carry"/> the speed of what it is
    /// fixed to (a flame leaves with its ship), <paramref name="dt"/> the frame.
    /// </summary>
    public static void Flame(FxKind kind, Vector3 at, Vector3 dir, Vector3 carry, float radius, float length, float level, float dt, uint tint = White)
    {
        // A particle lives about as long as it takes to reach the flame's tip, so the count alive fits the flame, not the frame rate.
        var len = length * (0.35f + 0.65f * level);
        var life = 0.1f + MathF.Min(0.26f, len * 0.05f);
        var speed = len / life;
        var rate = (260f + 60f * MathF.Min(radius, 4f) * MathF.Min(len, 12f)) * (0.3f + 0.7f * level);
        for (var k = Share(rate * dt); k > 0; k--)
        {
            var a = Rand() * MathF.Tau;
            var r = radius * 0.6f * MathF.Sqrt(Rand());
            var side = Vector3.Cross(dir, MathF.Abs(dir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
            side = Vector3.Normalize(side);
            var up = Vector3.Cross(dir, side);
            var off = side * (MathF.Cos(a) * r) + up * (MathF.Sin(a) * r);
            var along = 0.75f + 0.5f * Rand();
            Emit(kind, at + off + dir * (Rand() * speed * dt), carry + dir * (speed * along) + off * 0.8f, radius * (0.85f + 0.5f * Rand()), life * (0.7f + 0.6f * Rand()), tint);
        }
    }

    // ---- The fallback's step: the same maths as the compute shader (FxGpu.UpdateSrc), over a small array ----------------

    internal static FxParticle[] CpuPool = [];
    internal static int CpuCursor, CpuAlive;
    internal static readonly FxParticle[] CpuDraw = new FxParticle[Capacity(VideoLevel.High, false)];

    /// <summary>Takes the frame's births and moves everything on. No GL: the self-test can run it.</summary>
    public static void StepCpu(float dt)
    {
        var cap = Capacity(Level, false);
        if (CpuPool.Length != cap)
        {
            CpuPool = new FxParticle[cap];
            CpuCursor = 0;
        }
        var kinds = FxKinds.Rows;
        for (var i = 0; i < BirthCount; i++)
        {
            CpuPool[CpuCursor] = Births[i];
            CpuCursor = (CpuCursor + 1) % cap;
        }
        BirthCount = 0;
        var n = 0;
        var pool = CpuPool;
        for (var i = 0; i < pool.Length; i++)
        {
            ref var p = ref pool[i];
            if (p.Life <= 0)
                continue;
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                p.Life = 0;
                continue;
            }
            ref readonly var k = ref kinds[(int)p.Kind];
            p.Vel = p.Vel * MathF.Max(0f, 1f - k.Drag * dt) - new Vector3(0, k.Gravity * dt, 0);
            p.Pos += p.Vel * dt;
            CpuDraw[n++] = p;
        }
        CpuAlive = n;
    }

    /// <summary>Clears the air (a new map, a self-test).</summary>
    public static void Clear()
    {
        BirthCount = 0;
        Array.Clear(CpuPool);
        CpuAlive = 0;
        FxGpu.ClearPool = true;
    }
}

/// <summary>Materials/Particles.json as the C# step and the shaders read it (the shaders' FxKinds block: five vec4s a kind).</summary>
public static class FxKinds
{
    public struct Row
    {
        public Vector3 C0, C1, C2;
        public float SizeFrom, SizeTo, Mid, Alpha, FadeIn, FadePow, Glow, Lit, Gravity, Drag, Turbulence, Stretch;
    }

    public static readonly Row[] Rows = Load();

    public static string FilePath => Path.Combine(AppFolders.Data, "Materials", "Particles.json");

    private static Row[] Load()
    {
        var rows = new Row[(int)FxKind.Count];
        for (var i = 0; i < rows.Length; i++)
            rows[i] = new Row { C0 = Vector3.One, C1 = Vector3.One, C2 = Vector3.One, SizeFrom = 1, SizeTo = 1, Mid = 0.5f, Alpha = 0.5f, FadePow = 1 };
        try
        {
            if (File.Exists(FilePath))
                Parse(File.ReadAllText(FilePath), rows);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            Console.WriteLine($"[fx] {FilePath} refused: {ex.Message}");
        }
        return rows;
    }

    private static Vector3 Hex(string text)
    {
        var rgb = Convert.ToUInt32(text[1..], 16);
        return new Vector3((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
    }

    public static void Parse(string json, Row[] rows)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var e in doc.RootElement.GetProperty("kinds").EnumerateArray())
        {
            if (!Enum.TryParse<FxKind>(e.GetProperty("name").GetString(), true, out var kind) || kind == FxKind.Count)
                throw new InvalidDataException($"no particle kind \"{e.GetProperty("name").GetString()}\"");
            ref var r = ref rows[(int)kind];
            foreach (var p in e.EnumerateObject())
                switch (p.Name)
                {
                    case "name": break;
                    case "sizeFrom": r.SizeFrom = p.Value.GetSingle(); break;
                    case "sizeTo": r.SizeTo = p.Value.GetSingle(); break;
                    case "colours":
                        r.C0 = Hex(p.Value[0].GetString()!);
                        r.C1 = Hex(p.Value[1].GetString()!);
                        r.C2 = Hex(p.Value[2].GetString()!);
                        break;
                    case "mid": r.Mid = p.Value.GetSingle(); break;
                    case "alpha": r.Alpha = p.Value.GetSingle(); break;
                    case "fadeIn": r.FadeIn = p.Value.GetSingle(); break;
                    case "fadePow": r.FadePow = p.Value.GetSingle(); break;
                    case "glow": r.Glow = p.Value.GetSingle(); break;
                    case "lit": r.Lit = p.Value.GetSingle(); break;
                    case "gravity": r.Gravity = p.Value.GetSingle(); break;
                    case "drag": r.Drag = p.Value.GetSingle(); break;
                    case "turbulence": r.Turbulence = p.Value.GetSingle(); break;
                    case "stretch": r.Stretch = p.Value.GetSingle(); break;
                    default: throw new InvalidDataException($"a particle kind has an unknown key \"{p.Name}\"");
                }
        }
    }

    /// <summary>The block's data: 5 vec4s for each of 16 kinds.</summary>
    public static float[] Block()
    {
        var b = new float[16 * 20];
        for (var i = 0; i < Rows.Length; i++)
        {
            ref readonly var r = ref Rows[i];
            var o = i * 20;
            b[o] = r.C0.X; b[o + 1] = r.C0.Y; b[o + 2] = r.C0.Z; b[o + 3] = r.Mid;
            b[o + 4] = r.C1.X; b[o + 5] = r.C1.Y; b[o + 6] = r.C1.Z; b[o + 7] = r.Alpha;
            b[o + 8] = r.C2.X; b[o + 9] = r.C2.Y; b[o + 10] = r.C2.Z; b[o + 11] = r.Glow;
            b[o + 12] = r.SizeFrom; b[o + 13] = r.SizeTo; b[o + 14] = r.FadeIn; b[o + 15] = r.FadePow;
            b[o + 16] = r.Gravity; b[o + 17] = r.Drag; b[o + 18] = r.Turbulence; b[o + 19] = r.Stretch + 10f * r.Lit; // (stretch below 10, lit in the tens)
        }
        return b;
    }
}
