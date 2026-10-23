using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Per-weapon-class frenzy visuals (the look tables are in <see cref="FrenzyLooks"/>). Everything here is CPU-side and
/// allocation-free per frame; <see cref="FrenzyFxRenderer"/> draws what it builds in one instanced call per pass.
/// <list type="bullet">
/// <item>Tracers: <see cref="OnTracer"/> is called by Game.AddTracer for every tracer anyone fires. A shooter in frenzy
/// gets the class colour and width on the tracer itself, plus a glow sheath / trail and sparks (muzzle, impact, brass)
/// from the particle pool, thinned out with distance.</item>
/// <item>Weapons: <see cref="BuildWorld"/> adds a breathing glow along every other frenzied player's gun (and a slow ring at
/// their feet at level 5+), so everyone sees who's on a spree; <see cref="BuildViewModel"/> adds your own gun's glow and the
/// class's first-person signature (heat shimmer, glint, red-hot LMG barrel...).</item>
/// <item>The screen (edge tint, speed lines, chroma, kill pulse) and the HUD meter are drawn by Ui (UiFrenzy.cs) from the
/// state here.</item>
/// </list>
/// Whose frenzy: the level and class come from <see cref="LevelOf"/> / <see cref="ClassOf"/>: the server-replicated
/// Player.FrenzyLevel / FrenzyClass (the class of the weapon that got the most recent kill) for other players, and the
/// local meter (the same one that drives the music) for you.
/// </summary>
public static partial class FrenzyFx
{
    /// <summary>Settings > Video "Frenzy visuals" (set by Program from the settings each frame).</summary>
    public static FrenzyVisualMode Mode = FrenzyVisualMode.Full;

    /// <summary>Your frenzy level 0..6 and class (set by Program each frame).</summary>
    public static int LocalLevel;

    public static WeaponClass LocalClass = WeaponClass.AssaultRifle;

    /// <summary>The frenzy music's current level (the edge tint pulses with it).</summary>
    public static int MusicLevel;

    /// <summary>Smoke test only: every player shows this level and class.</summary>
    public static (int Level, WeaponClass Class)? Override;

    /// <summary>How far you're aimed down the sights (0..1): your gun's glow fades out as you aim, so it never sits in the sight picture.</summary>
    public static float ViewModelAim;

    /// <summary>The camera, for distance thinning (set each frame before drawing).</summary>
    public static Vector3 Camera;

    /// <summary>Your weapon's heat 0..1 from sustained fire (AR shimmer, LMG barrel glow).</summary>
    public static float Heat { get; private set; }

    /// <summary>When you last got a kill, and when the pistol glint last started (draw or kill).</summary>
    public static double KillAt = -100, GlintAt = -100;

    /// <summary>Seconds the sniper's kill vignette pulse lasts (one slow swell and fade, not a flash).</summary>
    public const double KillPulseSeconds = 0.9;

    /// <summary>Seconds the pistol's glint takes to sweep along the gun.</summary>
    public const double GlintSeconds = 0.4;

    /// <summary>Beyond this distance a frenzied shot spawns no sparks (its tracer still takes the class colour).</summary>
    public const float SparkRange = 90f;

    private static uint _rng = 0x9e3779b9u;
    private static int _rainbowStep;

    /// <summary>CPU time spent in this class's per-frame work and tracer hooks since the last <see cref="TakeCpuMs"/>.</summary>
    private static long _cpuTicks;

    // =====================================================================
    // Whose frenzy

    /// <summary>A player's frenzy level 0..6 (0 for null).</summary>
    public static int LevelOf(Game g, Player? p)
    {
        if (p == null)
            return 0;
        if (Override is { } o)
            return o.Level;
        if (p.Id == g.LocalId)
            return LocalLevel;
        return Math.Clamp(ReplicatedLevel(p), 0, FrenzyMeter.MaxLevel);
    }

    /// <summary>A player's frenzy class (the weapon class of their most recent kill).</summary>
    public static WeaponClass ClassOf(Game g, Player? p)
    {
        if (Override is { } o)
            return o.Class;
        if (p == null)
            return WeaponClass.AssaultRifle;
        if (p.Id == g.LocalId)
            return LocalClass;
        return ReplicatedClass(p);
    }

    // The server-replicated frenzy of other players (Source/Game/Frenzy/FrenzyServer.cs: Player.FrenzyLevel / FrenzyClass,
    // snapshot protocol 26). Bots carry it too, so a bot on a spree glows like a player.
    private static int ReplicatedLevel(Player p) => p.FrenzyLevel;

    private static WeaponClass ReplicatedClass(Player p) => Enum.IsDefined(p.FrenzyClass) ? p.FrenzyClass : WeaponClass.Special;

    /// <summary>Your frenzy class: the server's (the weapon of your most recent kill) while you're in frenzy, else the gun in hand.</summary>
    public static WeaponClass LocalClassOf(Player me, WeaponClass held) => ReplicatedLevel(me) > 0 ? ReplicatedClass(me) : held;

    /// <summary>The look you're showing now.</summary>
    public static FrenzyLook LocalLook => FrenzyLooks.Get(Override?.Class ?? LocalClass);

    /// <summary>Your level as drawn (the smoke override included).</summary>
    public static int LocalShownLevel => Override?.Level ?? LocalLevel;

    // =====================================================================
    // Events

    /// <summary>
    /// Every tracer (Game.AddTracer): a frenzied shooter's tracer takes the class colour and width, and the shot spawns
    /// its glow trail and sparks. Allocation-free.
    /// </summary>
    public static void OnTracer(Game g, Player? shooter, WeaponDef def, Vector3 from, Vector3 to, double now, ref Vector3 color, ref float width)
    {
        if (Mode == FrenzyVisualMode.Off || shooter == null)
            return;
        var level = LevelOf(g, shooter);
        if (level <= 0)
            return;
        var t0 = Stopwatch.GetTimestamp();
        var look = FrenzyLooks.Get(ClassOf(g, shooter));
        var blend = FrenzyLooks.TracerBlend(level, Mode);
        var k = FrenzyLooks.Intensity(level, Mode);
        var tc = look.Rainbow ? FrenzyLooks.Hue((float)(now * 0.35 % 1.0) + _rainbowStep++ * 0.13f) : look.Color;
        color = Vector3.Lerp(color, tc, blend) * (1f + 0.35f * blend);
        width *= 1f + (look.TracerWidth - 1f) * blend;
        var pellets = Math.Max(1, def.Pellets);
        if (shooter.Id == g.LocalId)
            Heat = MathF.Min(1f, Heat + 0.07f / pellets);

        var d = to - from;
        var len = d.Length();
        if (len < 0.05f)
        {
            _cpuTicks += Stopwatch.GetTimestamp() - t0;
            return;
        }
        var dir = d / len;
        var lod = Lod(from);

        // Glow sheath / trail along the tracer (the Sniper's lingers).
        if (look.Sheath > 0)
            FrenzyParticles.Trail(from, to, tc, width * look.Sheath, look.TrailLife * (0.5f + 0.5f * k), 0.18f + 0.3f * k);
        if (look.Signature == FrenzySignature.Glitch && FrenzyLooks.Flicker(now) > 0.5f)
        {
            // The glitch ghost: a split copy of the tracer, offset sideways, in the accent colour.
            var side = SideOf(dir) * (0.05f + 0.06f * k);
            FrenzyParticles.Trail(from + side, to + side, look.Accent, width * 1.4f, 0.12f, 0.35f * k);
        }

        if (lod <= 0f)
        {
            _cpuTicks += Stopwatch.GetTimestamp() - t0;
            return;
        }
        var spark = 0.012f + 0.01f * k;

        // Muzzle sparks (split across a shotgun's pellets).
        var n = Count(look.MuzzleSparks * k * lod / pellets);
        for (var i = 0; i < n; i++)
        {
            var v = (dir * 0.7f + RandUnit() * 0.75f) * look.SparkSpeed * (0.5f + Rand());
            FrenzyParticles.Spark(from, v, Vector3.Lerp(tc, look.Accent, Rand()), spark, look.SparkLife * (0.6f + 0.6f * Rand()), look.SparkGravity * 0.5f);
        }

        // Brass sparks from the ejection port (LMG showers), once per trigger pull.
        n = Count(look.BrassSparks * k * lod / pellets);
        if (n > 0)
        {
            var right = SideOf(dir);
            var port = from - dir * 0.35f + right * 0.06f;
            for (var i = 0; i < n; i++)
            {
                var v = right * (1.8f + Rand() * 1.4f) + Vector3.UnitY * (1.2f + Rand()) + RandUnit() * 0.6f;
                FrenzyParticles.Spark(port, v, Vector3.Lerp(look.Accent, look.Color, Rand()), spark * 1.2f, look.SparkLife * (0.7f + 0.6f * Rand()), look.SparkGravity, 0.6f, 0.03f);
            }
        }

        // Where it lands (nothing for a shot into the sky).
        if (len < World.Range * 0.98f)
        {
            n = Count(look.ImpactSparks * k * lod / (look.Signature == FrenzySignature.Embers ? 1 : MathF.Sqrt(pellets)));
            switch (look.Signature)
            {
                case FrenzySignature.ImpactRings:
                {
                    var u = SideOf(dir);
                    var w = Vector3.Cross(dir, u);
                    var ring = Math.Max(n, 0);
                    for (var i = 0; i < ring; i++)
                    {
                        var a = i * MathF.Tau / ring;
                        var v = (u * MathF.Cos(a) + w * MathF.Sin(a)) * look.SparkSpeed;
                        FrenzyParticles.Spark(to - dir * 0.05f, v, look.Color, spark * 0.9f, look.SparkLife, 0f, 3f, 0.05f);
                    }
                    break;
                }
                case FrenzySignature.Embers:
                    for (var i = 0; i < n; i++)
                    {
                        var v = RandUnit() * look.SparkSpeed + Vector3.UnitY * (0.4f + Rand() * 0.6f);
                        FrenzyParticles.Spark(to - dir * 0.05f, v, Vector3.Lerp(look.Color, look.Accent, Rand()), spark * 1.6f, look.SparkLife * (0.6f + 0.6f * Rand()), look.SparkGravity, 1.2f, 0.06f);
                    }
                    break;
                default:
                    for (var i = 0; i < n; i++)
                    {
                        var v = (-dir * 0.6f + RandUnit()) * look.SparkSpeed * (0.4f + 0.8f * Rand());
                        FrenzyParticles.Spark(to - dir * 0.05f, v, Vector3.Lerp(tc, look.Accent, Rand()), spark, look.SparkLife * (0.6f + 0.6f * Rand()), look.SparkGravity);
                    }
                    break;
            }
        }
        _cpuTicks += Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>You got a kill (the sniper's vignette pulse, the pistol's glint).</summary>
    public static void OnLocalKill(double now)
    {
        KillAt = now;
        if (LocalLook.Signature == FrenzySignature.QuickDrawGlint)
            GlintAt = now;
    }

    /// <summary>You drew a weapon (the pistol's quick-draw glint).</summary>
    public static void OnEquip(double now) => GlintAt = now;

    /// <summary>Steps the particles and your weapon heat by <paramref name="dt"/> seconds.</summary>
    public static void Update(float dt)
    {
        var t0 = Stopwatch.GetTimestamp();
        FrenzyParticles.Update(dt);
        Heat = MathF.Max(0f, Heat - dt * 0.45f);
        _cpuTicks += Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>0..1: the sniper kill pulse right now (one smooth swell and fade after a kill; 0 otherwise).</summary>
    public static float KillPulse(double now)
    {
        var t = (now - KillAt) / KillPulseSeconds;
        return t is < 0 or >= 1 ? 0f : MathF.Sin((float)(t * Math.PI));
    }

    // =====================================================================
    // Per-frame instances

    /// <summary>
    /// The world pass: every live particle, then a glow along each other frenzied player's gun (and a slow ring at their
    /// feet from level 5). Returns the instance count written to <paramref name="dst"/>.
    /// </summary>
    public static int BuildWorld(Game g, double now, float[] dst)
    {
        var t0 = Stopwatch.GetTimestamp();
        var max = dst.Length / FrenzyParticles.FloatsPerInstance;
        var n = Mode == FrenzyVisualMode.Off ? 0 : FrenzyParticles.Write(dst, 0, max);
        if (Mode != FrenzyVisualMode.Off)
        {
            var breathe = 0.85f + 0.15f * FrenzyLooks.Pulse(now, FrenzyLooks.GlowBreathHz);
            foreach (var p in g.Players.Values)
            {
                if (!p.Alive || p.Id == g.LocalId)
                    continue;
                var level = LevelOf(g, p);
                if (level <= 0)
                    continue;
                var look = FrenzyLooks.Get(ClassOf(g, p));
                var glow = FrenzyLooks.Intensity(level) * (Mode == FrenzyVisualMode.Subtle ? 0.6f : 1f);
                var col = look.Rainbow ? FrenzyLooks.Hue((float)(now * 0.2 % 1.0) + p.Id * 0.17f) : look.Color;
                var fwd = GravityFrames.Forward(p.RenderPosition, p.RenderYaw, p.RenderPitch);
                var muzzle = Game.MuzzleWorld(p);
                var gunLen = 0.45f + p.WeaponDef.Length;
                var center = muzzle - fwd * (gunLen * 0.5f);
                n = FrenzyParticles.Put(dst, n, center, fwd * gunLen * 1.1f, 0.14f + 0.12f * glow, col, 0.5f * glow * look.BarrelGlow * breathe);
                n = FrenzyParticles.Put(dst, n, center, fwd * gunLen, 0.06f, look.Accent, 0.45f * glow);
                if (level >= 5 && n + 8 <= max)
                {
                    // A slow ring of glow at their feet: readable from any angle, never over the body.
                    var spin = (float)(now * 0.25 % 1.0) * MathF.Tau;
                    var foot = p.RenderPosition + new Vector3(0, 0.05f, 0);
                    for (var i = 0; i < 8; i++)
                    {
                        var a = spin + i * MathF.Tau / 8;
                        var at = foot + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 0.6f;
                        var tangent = new Vector3(-MathF.Sin(a), 0, MathF.Cos(a)) * 0.32f;
                        n = FrenzyParticles.Put(dst, n, at, tangent, 0.05f, col, 0.4f * glow);
                    }
                }
            }
        }
        _cpuTicks += Stopwatch.GetTimestamp() - t0;
        return n;
    }

    private static WeaponDef? _anchorDef;
    private static Vector3 _anchorMuzzle = new(0, 0.05f, -0.6f);

    /// <summary>
    /// The viewmodel pass (camera space, <paramref name="gunPose"/> = gun-local to camera): your gun's glow and the class's
    /// first-person signature. Returns the instance count.
    /// </summary>
    public static int BuildViewModel(WeaponDef def, in Matrix4x4 gunPose, double now, float[] dst)
    {
        var level = LocalShownLevel;
        if (Mode == FrenzyVisualMode.Off || level <= 0)
            return 0;
        var t0 = Stopwatch.GetTimestamp();
        if (!ReferenceEquals(def, _anchorDef))
        {
            _anchorDef = def;
            _anchorMuzzle = ArmAnchors.For(def).Muzzle; // cached: worked out once per gun
        }
        var look = LocalLook;
        var glow = FrenzyLooks.Intensity(level) * (Mode == FrenzyVisualMode.Subtle ? 0.6f : 1f);
        var k = FrenzyLooks.Intensity(level, Mode);
        var col = look.Rainbow ? FrenzyLooks.Hue((float)(now * 0.2 % 1.0)) : look.Color;
        var muzzle = Vector3.Transform(_anchorMuzzle, gunPose);
        var rear = Vector3.Transform(_anchorMuzzle * 0.25f, gunPose);
        var axis = muzzle - rear;
        var mid = (muzzle + rear) * 0.5f;
        var up = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, gunPose));
        var breathe = 0.85f + 0.15f * FrenzyLooks.Pulse(now, FrenzyLooks.GlowBreathHz);
        glow *= 1f - 0.85f * Math.Clamp(ViewModelAim, 0f, 1f);
        k *= 1f - 0.85f * Math.Clamp(ViewModelAim, 0f, 1f);
        var hot = look.BarrelGlow;
        var n = 0;
        if (look.Signature == FrenzySignature.BrassShower)
        {
            // Red-hot barrel: brighter and redder the longer you hold the trigger.
            hot *= 0.4f + 0.6f * Heat;
            col = Vector3.Lerp(col, new Vector3(1f, 0.25f, 0.05f), Heat);
        }
        n = FrenzyParticles.Put(dst, n, mid, axis * 1.15f, 0.05f + 0.03f * glow, col, 0.32f * glow * hot * breathe);
        n = FrenzyParticles.Put(dst, n, muzzle, axis * 0.12f, 0.03f, look.Accent, 0.5f * glow * hot);

        switch (look.Signature)
        {
            case FrenzySignature.HeatShimmer when k > 0:
            {
                // Heat shimmer: faint wavy slivers rising off the barrel as it heats up.
                var h = Heat * k;
                for (var i = 0; i < 5 && h > 0.02f; i++)
                {
                    var ph = (float)((now * 0.9 + i * 0.2) % 1.0);
                    var along = Vector3.Lerp(rear, muzzle, 0.3f + 0.15f * i);
                    var wob = MathF.Sin((float)(now * 7.0 % Math.Tau) + i * 1.7f) * 0.006f;
                    var at = along + up * (0.02f + ph * 0.07f) + Vector3.Normalize(axis) * wob;
                    n = FrenzyParticles.Put(dst, n, at, up * 0.025f, 0.012f, look.Accent, 0.22f * h * (1f - ph));
                }
                break;
            }
            case FrenzySignature.QuickDrawGlint when k > 0:
            {
                // A glint sweeping from the grip to the muzzle: once per draw or kill.
                var s = (float)((now - GlintAt) / GlintSeconds);
                if (s is >= 0 and < 1)
                {
                    var at = Vector3.Lerp(rear, muzzle, s);
                    n = FrenzyParticles.Put(dst, n, at, up * 0.06f, 0.008f, Vector3.One, 0.9f * MathF.Sin(s * MathF.PI) * k);
                    n = FrenzyParticles.Put(dst, n, at, Vector3.Normalize(axis) * 0.05f, 0.008f, look.Accent, 0.7f * MathF.Sin(s * MathF.PI) * k);
                }
                break;
            }
            case FrenzySignature.Glitch when k > 0:
            {
                // A split ghost of the glow, offset sideways while the glitch flicker is on.
                var f = FrenzyLooks.Flicker(now);
                var side = Vector3.Normalize(Vector3.Cross(axis, up)) * 0.012f;
                n = FrenzyParticles.Put(dst, n, mid + side, axis, 0.03f, look.Accent, 0.3f * f * k);
                n = FrenzyParticles.Put(dst, n, mid - side, axis, 0.03f, new Vector3(1f, 0.2f, 0.4f), 0.3f * f * k);
                break;
            }
        }
        _cpuTicks += Stopwatch.GetTimestamp() - t0;
        return n;
    }

    /// <summary>CPU milliseconds spent since the last call (and resets the count).</summary>
    public static double TakeCpuMs()
    {
        var ms = _cpuTicks * 1000.0 / Stopwatch.Frequency;
        _cpuTicks = 0;
        return ms;
    }

    // =====================================================================
    // Helpers

    /// <summary>Spark density by distance from the camera: full up close, thinning out, none past <see cref="SparkRange"/>.</summary>
    public static float Lod(Vector3 at)
    {
        var d = Vector3.Distance(at, Camera);
        return d >= SparkRange ? 0f : Math.Clamp(1f - (d - 25f) / (SparkRange - 25f), 0.15f, 1f);
    }

    private static Vector3 SideOf(Vector3 dir)
    {
        var s = Vector3.Cross(dir, Vector3.UnitY);
        var l = s.Length();
        return l > 1e-3f ? s / l : Vector3.UnitX;
    }

    private static float Rand()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng & 0xffffff) / 16777216f;
    }

    private static Vector3 RandUnit()
    {
        var v = new Vector3(Rand() * 2 - 1, Rand() * 2 - 1, Rand() * 2 - 1);
        var l = v.Length();
        return l > 1e-3f ? v / l : Vector3.UnitY;
    }

    /// <summary>A fractional count rounded at random (2.3 is 2, or 3 three times in ten), so low levels still spark now and then.</summary>
    private static int Count(float x) => x <= 0 ? 0 : (int)x + (Rand() < x - (int)x ? 1 : 0);
}
