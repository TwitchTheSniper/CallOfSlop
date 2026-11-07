using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// How a massive blast looks and sounds (ConstructMegaBlast.cs; docs/sandbox/CONSTRUCTS.md, "Massive blasts"), on every PC from its ShellLand:
/// a flash that whites out the screen by how near and how big it is, a fireball that swells and rises, a shock ring of dust running out over
/// the ground at the front's own speed, and a plume that climbs into a mushroom over a quarter of a minute; a light on lit maps; the bang
/// arriving at the speed of sound with a shake as the shock passes. An antimatter blast is white and violet, a fusion one whiter and bigger,
/// fission warm. All of it is the particle pool's (<see cref="Fx"/>, both GL paths) plus a full-screen rect, from a fixed pool here: a frame
/// asks for at most a few hundred particles a blast and allocates nothing.
/// </summary>
public static class MegaBlastFx
{
    public const int Max = 4;
    private const float Seconds = 16f; // how long one is drawn
    private const float SoundSpeed = 343f; // m/s: the bang's delay with distance

    private struct One
    {
        public bool Live, Heard;
        public byte Look;
        public Vector3 At;
        public float Radius, Speed;
        public double Born;
    }

    private static readonly One[] Pool = new One[Max];

    /// <summary>Massive blasts shown (a hand check reads it).</summary>
    public static int Shown;

    /// <summary>Is one being drawn?</summary>
    public static bool Any
    {
        get
        {
            foreach (ref readonly var o in Pool.AsSpan())
                if (o.Live)
                    return true;
            return false;
        }
    }

    /// <summary>A massive blast went off (ShellLand with a radius of <see cref="ConstructWorld.MegaRadius"/> or more).</summary>
    public static void Add(Vector3 at, float radius, byte look, double now)
    {
        var k = 0;
        for (var i = 0; i < Pool.Length; i++)
            if (!Pool[i].Live)
            {
                k = i;
                break;
            }
            else if (Pool[i].Born < Pool[k].Born)
                k = i; // (full: the oldest makes room)
        Pool[k] = new One { Live = true, Look = look, At = at, Radius = radius, Speed = MegaBlasts.FrontSpeed(radius), Born = now };
        Shown++;
    }

    /// <summary>Everything gone (a map change).</summary>
    public static void Clear()
    {
        for (var i = 0; i < Pool.Length; i++)
            Pool[i].Live = false;
    }

    private static (Vector3 Flash, Vector3 Fire, Vector3 Ring) Colours(byte look) => look switch
    {
        MegaBlasts.Antimatter => (new Vector3(0.93f, 0.88f, 1f), new Vector3(0.72f, 0.48f, 1f), new Vector3(0.78f, 0.7f, 1f)),
        MegaBlasts.Fusion => (new Vector3(1f, 0.98f, 0.94f), new Vector3(1f, 0.78f, 0.45f), new Vector3(0.92f, 0.9f, 0.86f)),
        MegaBlasts.Fission => (new Vector3(1f, 0.93f, 0.78f), new Vector3(1f, 0.6f, 0.2f), new Vector3(0.85f, 0.78f, 0.66f)),
        _ => (new Vector3(1f, 0.85f, 0.6f), new Vector3(1f, 0.55f, 0.15f), new Vector3(0.75f, 0.68f, 0.58f)),
    };

    private static uint _seed = 0x9E3779B9;

    private static float Rand()
    {
        _seed ^= _seed << 13;
        _seed ^= _seed >> 17;
        _seed ^= _seed << 5;
        return (_seed >> 8) / 16777216f;
    }

    /// <summary>The plume's top over the centre (m) at <paramref name="t"/> s: it climbs fast, then slows.</summary>
    private static float CapHeight(float radius, float t) => radius * 0.95f * (1f - MathF.Exp(-t / 4.5f));

    /// <summary>
    /// Each frame (every PC): the particles of every blast being drawn, its sound when it reaches <paramref name="ear"/>, and the shake
    /// (into <paramref name="shake"/>) as its front passes.
    /// </summary>
    public static void Frame(double now, float dt, Vector3 ear, Audio audio, ref float shake)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);
        for (var i = 0; i < Pool.Length; i++)
        {
            ref var o = ref Pool[i];
            if (!o.Live)
                continue;
            var t = (float)(now - o.Born);
            if (t > Seconds || t < 0)
            {
                o.Live = false;
                continue;
            }
            var dist = Vector3.Distance(ear, o.At);
            if (!o.Heard && t >= dist / SoundSpeed)
            {
                o.Heard = true;
                var gain = Math.Clamp(1.8f * o.Radius / (o.Radius + dist * 0.4f), 0.15f, 1.6f);
                var pitch = o.Look switch { MegaBlasts.Fusion => 0.42f, MegaBlasts.Antimatter => 0.62f, _ => 0.5f };
                var toward = dist > 1f ? (o.At - ear) / dist : Vector3.UnitZ;
                audio.Effect3DPitched(Sfx.Explosion, ear + toward * MathF.Min(dist, 25f), gain, pitch); // (near the ear, from its way: heard at any range)
                audio.Effect3DPitched(Sfx.Explosion, ear + toward * MathF.Min(dist, 60f), gain * 0.7f, pitch * 0.7f);
            }
            var front = MathF.Min(o.Radius, o.Speed * t);
            if (dist < o.Radius * 2.5f && MathF.Abs(dist - o.Speed * t) < o.Speed * 0.25f)
                shake = MathF.Max(shake, Math.Clamp(1.3f - dist / (o.Radius * 2f), 0.15f, 1f)); // (the shock passing)
            Emit(ref o, t, dt, front);
        }
    }

    private static void Emit(ref One o, float t, float dt, float front)
    {
        var (flashC, fireC, ringC) = Colours(o.Look);
        var r = o.Radius;
        var up = GravityFrames.UpAt(o.At);
        var side = Vector3.Normalize(Vector3.Cross(up, MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var fwd = Vector3.Cross(side, up);
        var rate = dt * 60f; // (asks are per sixtieth of a second, so a frame rate doesn't change the look)
        // The flash: its first frames.
        if (t < 0.12f)
        {
            Fx.Emit(FxKind.Flash, o.At + up * (r * 0.1f), Vector3.Zero, r * 1.4f, 0.6f, Fx.Tint(flashC));
            Fx.Emit(FxKind.Flash, o.At + up * (r * 0.25f), Vector3.Zero, r * 0.8f, 1.1f, Fx.Tint(flashC));
        }
        // The fireball: swells to a third of the radius, glowing, and rises.
        if (t < 3f)
        {
            var ball = r * 0.32f * MathF.Min(1f, MathF.Sqrt(t / 0.9f + 0.02f));
            var lift = CapHeight(r, t) * 0.55f;
            for (var k = Fx.Share(36f * rate); k > 0; k--)
            {
                var d = Dir();
                d = Vector3.Normalize(d + up * 0.25f);
                Fx.Emit(FxKind.Fireball, o.At + up * lift + d * (ball * (0.4f + 0.6f * Rand())), d * (r * 0.03f) + up * (r * 0.05f), r * (0.09f + 0.07f * Rand()), 1.0f + 1.4f * Rand(), Fx.Tint(fireC));
            }
        }
        // The shock ring: dust thrown up where the front is, running out over the ground.
        if (front < o.Radius && t < o.Radius / o.Speed + 0.1f)
            for (var k = Fx.Share(70f * rate); k > 0; k--)
            {
                var a = Rand() * MathF.Tau;
                var dir = side * MathF.Cos(a) + fwd * MathF.Sin(a);
                var at = o.At + dir * front + up * (2f + r * 0.01f * Rand());
                Fx.Emit(FxKind.Dust, at, dir * (o.Speed * 0.12f) + up * (r * 0.02f * Rand()), r * (0.03f + 0.03f * Rand()), 1.6f + 1.8f * Rand(), Fx.Tint(ringC, 0.85f));
            }
        // The plume: a stem of smoke, and the cap rolling round its top.
        if (t > 0.6f && t < Seconds - 6f)
        {
            var top = CapHeight(r, t);
            var stem = r * 0.07f;
            for (var k = Fx.Share(14f * rate); k > 0; k--)
            {
                var h = top * Rand();
                var a = Rand() * MathF.Tau;
                var off = (side * MathF.Cos(a) + fwd * MathF.Sin(a)) * (stem * Rand());
                Fx.Emit(t < 3f ? FxKind.Fireball : FxKind.DarkSmoke, o.At + up * h + off, up * (r * 0.035f), r * (0.06f + 0.04f * Rand()), 3.5f + 2.5f * Rand(), t < 3f ? Fx.Tint(fireC) : Fx.White);
            }
            var ring = r * (0.12f + 0.16f * MathF.Min(1f, t / 6f));
            for (var k = Fx.Share(22f * rate); k > 0; k--)
            {
                var a = Rand() * MathF.Tau;
                var dir = side * MathF.Cos(a) + fwd * MathF.Sin(a);
                var roll = Rand() * MathF.Tau;
                var tube = r * 0.09f;
                var at = o.At + up * (top + MathF.Sin(roll) * tube) + dir * (ring + MathF.Cos(roll) * tube);
                var glow = t < 4f;
                Fx.Emit(glow ? FxKind.Fireball : FxKind.Smoke, at, dir * (r * 0.012f) + up * (r * 0.012f), r * (0.08f + 0.06f * Rand()), glow ? 1.6f + Rand() : 5f + 3f * Rand(), glow ? Fx.Tint(fireC) : Fx.White);
            }
        }
    }

    private static Vector3 Dir()
    {
        var y = Rand() * 2f - 1f;
        var a = Rand() * MathF.Tau;
        var s = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
        return new Vector3(s * MathF.Cos(a), y, s * MathF.Sin(a));
    }

    /// <summary>How white the screen is from the flashes now (0..0.92): bigger and nearer is brighter, an antimatter flash lasts longest.</summary>
    public static float FlashAlpha(double now, Vector3 eye, out Vector3 colour)
    {
        colour = Vector3.One;
        var best = 0f;
        foreach (ref readonly var o in Pool.AsSpan())
        {
            if (!o.Live)
                continue;
            var t = (float)(now - o.Born);
            var fade = o.Look == MegaBlasts.Antimatter ? 2.6f : o.Look == MegaBlasts.Fusion ? 2.0f : 1.4f;
            if (t < 0 || t > fade)
                continue;
            var near = Math.Clamp(1.4f - Vector3.Distance(eye, o.At) / (o.Radius * 8f), 0f, 1f);
            var a = near * 0.92f * (t < 0.05f ? t / 0.05f : MathF.Pow(1f - (t - 0.05f) / (fade - 0.05f), 1.6f));
            if (a > best)
            {
                best = a;
                colour = Colours(o.Look).Flash;
            }
        }
        return best;
    }

    /// <summary>Full-screen flash (the HUD's GL pass, after the flashbang's).</summary>
    public static void DrawOverlay(Renderer gfx, double now, Vector3 eye, float w, float h)
    {
        var a = FlashAlpha(now, eye, out var c);
        if (a > 0.002f)
            gfx.Rect(0, 0, w, h, c, a);
    }

    /// <summary>The fireball's light on a lit map (LightGrid's dynamic lights), fading over four seconds.</summary>
    public static void AddLights(double now)
    {
        foreach (ref readonly var o in Pool.AsSpan())
        {
            if (!o.Live)
                continue;
            var t = (float)(now - o.Born);
            if (t < 0 || t > 4f)
                continue;
            var c = Colours(o.Look).Fire;
            LightGrid.AddDynamic(o.At + GravityFrames.UpAt(o.At) * CapHeight(o.Radius, t) * 0.5f, c * (5f * (1f - t / 4f)), o.Radius * 3f);
        }
    }
}
