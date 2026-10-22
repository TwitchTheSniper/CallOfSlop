using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The blood overlay for your own health (the screen half of Settings > Video "Blood overlay"): procedural splatters around
/// the edges of the HUD area (<see cref="HudLayout.Span"/>, so 21:9 and 32:9 screens keep them near where you look), none
/// above 85% health and more layers at 70 / 50 / 30 / 15% (<see cref="Steps"/>), each new hit adding a splatter on the
/// side it came from (the replicated hit direction in <see cref="Player.Blood"/>), darker and denser towards death with a
/// gentle pulse below 15% and a faint red vignette at the screen's real edges. Everything fades as health regenerates
/// (a Stim's instant heal fades it in about half a second) and clears at once on death and respawn. Armour soaks hits
/// before health drops, so it shows nothing until health itself falls. All driven by health / <see cref="World.MaxHealth"/>.
///
/// The centre stays clear: splatters sit on the edges and reach at most about an eighth of the height inward. Pure logic,
/// no GL and no allocations per frame; <see cref="BloodOverlayRenderer"/> draws what <see cref="Build"/> writes in one
/// instanced draw call.
/// </summary>
public sealed class BloodOverlay
{
    /// <summary>Health fractions below which each layer of splatters appears.</summary>
    public static readonly float[] Steps = [0.85f, 0.70f, 0.50f, 0.30f, 0.15f];

    public const int PerStep = 3;
    public const int BaseCount = 15; // Steps.Length * PerStep
    public const int MaxHits = 8;

    /// <summary>Most instances <see cref="Build"/> writes: four vignette bands, the layers, the hit splatters.</summary>
    public const int MaxInstances = 4 + BaseCount + MaxHits;

    /// <summary>Health fraction the pulse starts below.</summary>
    public const float PulseBelow = 0.15f;

    /// <summary>Layers fade in over this much health below their step (and out as it regenerates past it).</summary>
    public const float StepBand = 0.05f;

    /// <summary>A hit's splatter is gone once health has regenerated this far above where the hit left it.</summary>
    public const float HitRecover = 0.2f;

    /// <summary>How fast splatters fade when health jumps up (per second of opacity).</summary>
    public const float FadeRate = 2.2f;

    /// <summary>One splatter: centre (0-1 across the HUD span, 0-1 up the screen), size (fraction of the screen height), rotation, texture layer.</summary>
    public struct Splat
    {
        public Vector2 Pos;
        public float Size, Rot, Threshold, Alpha, Band;
        public int Shape;
        public double Born;
        public bool Live;
    }

    private static readonly Splat[] Layout = MakeLayout();
    private readonly Splat[] _base = (Splat[])Layout.Clone();
    private readonly Splat[] _hits = new Splat[MaxHits];
    private int _nextHit;
    private uint _hitSerial;
    private float _frac = 1f;
    private bool _alive;
    private double _now;

    /// <summary>0 (healthy) to 1 (near death): how dark and dense it is.</summary>
    public float Darkness { get; private set; }

    /// <summary>The red vignette's alpha at the screen edges now.</summary>
    public float Vignette { get; private set; }

    /// <summary>The low-health pulse now (1 = none).</summary>
    public float Pulse { get; private set; } = 1f;

    /// <summary>Hits that added a splatter so far (tests).</summary>
    public int HitsAdded { get; private set; }

    /// <summary>The base layers (the self-test reads alphas and positions).</summary>
    public ReadOnlySpan<Splat> Base => _base;

    /// <summary>The hit splatters.</summary>
    public ReadOnlySpan<Splat> Hits => _hits;

    /// <summary>Everything gone at once (death, respawn, leaving a match).</summary>
    public void Clear()
    {
        for (var i = 0; i < _base.Length; i++)
            _base[i].Alpha = 0;
        Array.Clear(_hits);
        Darkness = Vignette = 0;
        Pulse = 1f;
    }

    /// <summary>
    /// Once per frame: <paramref name="frac"/> is your health / max, <paramref name="sector"/> the direction of the most recent
    /// hit (16 sectors of your facing, <see cref="BloodMarks.Sector"/>). A drop in health while alive is a new hit.
    /// </summary>
    public void Update(float frac, bool alive, int sector, double now, float dt)
    {
        frac = Math.Clamp(float.IsFinite(frac) ? frac : 1f, 0f, 1f);
        _now = now;
        if (!alive || !_alive)
        {
            // Dead, or just respawned: nothing on screen (the death tint takes over).
            if (alive != _alive || !alive)
                Clear();
            _alive = alive;
            _frac = frac;
            if (!alive)
                return;
        }
        if (frac < _frac - 1e-4f && frac < Steps[0])
            AddHit(sector, frac, now);
        _frac = frac;

        for (var i = 0; i < _base.Length; i++)
            Ease(ref _base[i], frac, dt);
        for (var i = 0; i < _hits.Length; i++)
        {
            if (!_hits[i].Live)
                continue;
            Ease(ref _hits[i], frac, dt);
            if (_hits[i].Alpha <= 0 && frac >= _hits[i].Threshold)
                _hits[i].Live = false;
        }
        Darkness = Math.Clamp((0.55f - frac) / 0.45f, 0f, 1f);
        var vig = Math.Clamp((0.6f - frac) / 0.6f, 0f, 1f);
        Vignette = 0.42f * MathF.Pow(vig, 1.3f);
        Pulse = frac < PulseBelow ? 0.82f + 0.16f * (0.5f + 0.5f * MathF.Sin((float)(now * MathF.Tau * 1.1))) : 1f;
    }

    private static void Ease(ref Splat s, float frac, float dt)
    {
        var target = Math.Clamp((s.Threshold - frac) / s.Band, 0f, 1f);
        s.Alpha = target >= s.Alpha ? target : MathF.Max(target, s.Alpha - FadeRate * dt);
    }

    /// <summary>A splatter on the screen edge in the hit's direction (in front = top, behind = bottom, right = right edge).</summary>
    private void AddHit(int sector, float frac, double now)
    {
        var h = BloodMarks.Hash(++_hitSerial, (uint)sector, 77);
        ref var s = ref _hits[_nextHit];
        _nextHit = (_nextHit + 1) % MaxHits;
        var p = EdgePoint(sector, 16f / 9f);
        var along = ((h & 0xFF) / 255f - 0.5f) * 0.12f; // spread a little along the edge
        if (MathF.Abs(p.X - 0.5f) > MathF.Abs(p.Y - 0.5f) * (16f / 9f))
            p.Y = Math.Clamp(p.Y + along, 0.06f, 0.94f);
        else
            p.X = Math.Clamp(p.X + along, 0.06f, 0.94f);
        s = new Splat
        {
            Pos = p, Size = 0.15f + 0.07f * ((h >> 8) & 0xFF) / 255f, Rot = (((h >> 16) & 0xFF) / 255f - 0.5f) * 1.2f,
            Shape = (int)((h >> 24) & 3), Threshold = MathF.Min(Steps[0] + StepBand, frac + HitRecover), Band = 0.08f, Alpha = 1f, Born = now, Live = true,
        };
        HitsAdded++;
    }

    /// <summary>Where a hit from <paramref name="sector"/> lands on the edge of the span (0-1 coordinates; <paramref name="aspect"/> = span width / height).</summary>
    public static Vector2 EdgePoint(int sector, float aspect)
    {
        var a = (sector & 15) / 16f * MathF.Tau;
        float dx = MathF.Sin(a) / MathF.Max(aspect, 0.1f), dy = MathF.Cos(a);
        var t = MathF.Min(MathF.Abs(dx) > 1e-4f ? 0.47f / MathF.Abs(dx) : float.MaxValue, MathF.Abs(dy) > 1e-4f ? 0.46f / MathF.Abs(dy) : float.MaxValue);
        return new Vector2(0.5f + dx * t, 0.5f + dy * t);
    }

    /// <summary>
    /// Writes the instances to draw (two vec4s each, <see cref="BloodOverlayRenderer"/>): first up to four vignette bands
    /// along the screen edges (a = rect x0,y0,x1,y1; b = alpha, -1, darkness, band depth), then the splatters inside the
    /// HUD span starting at <paramref name="spanLeft"/> (a = centre px, half size px, rotation; b = alpha, layer, darkness, 0).
    /// Returns the instance count; 0 with <see cref="BloodMode.Off"/> or nothing showing.
    /// </summary>
    public int Build(Span<Vector4> a, Span<Vector4> b, float screenW, float screenH, float spanLeft, float spanW, BloodMode mode)
    {
        if (mode == BloodMode.Off || !_alive || screenW <= 0 || screenH <= 0)
            return 0;
        var subtle = mode == BloodMode.Subtle;
        var n = 0;
        var cap = Math.Min(a.Length, b.Length);
        var vig = Vignette * Pulse * (subtle ? 0.5f : 1f);
        if (vig > 0.004f && cap >= 4)
        {
            var depth = screenH * 0.2f;
            var dark = Darkness;
            Band(a, b, ref n, new Vector4(0, 0, screenW, depth), vig, dark, depth); // bottom
            Band(a, b, ref n, new Vector4(0, screenH - depth, screenW, screenH), vig, dark, depth); // top
            Band(a, b, ref n, new Vector4(0, depth, depth, screenH - depth), vig, dark, depth); // left
            Band(a, b, ref n, new Vector4(screenW - depth, depth, screenW, screenH - depth), vig, dark, depth); // right
        }
        var pulse = Pulse;
        for (var i = 0; i < _base.Length && n < cap; i++)
        {
            ref readonly var s = ref _base[i];
            if (s.Alpha <= 0.004f || (subtle && i % PerStep == 2))
                continue;
            Splatter(a, b, ref n, s, s.Alpha * pulse, 1f, screenH, spanLeft, spanW, subtle);
        }
        for (var i = 0; i < _hits.Length && n < cap; i++)
        {
            ref readonly var s = ref _hits[i];
            if (!s.Live || s.Alpha <= 0.004f)
                continue;
            var pop = (float)Math.Clamp((_now - s.Born) / 0.12, 0, 1); // the splat lands: a quick grow
            Splatter(a, b, ref n, s, s.Alpha * pulse, 0.7f + 0.3f * pop, screenH, spanLeft, spanW, subtle);
        }
        return n;
    }

    private static void Band(Span<Vector4> a, Span<Vector4> b, ref int n, Vector4 rect, float alpha, float dark, float depth)
    {
        a[n] = rect;
        b[n] = new Vector4(alpha, -1f, dark, depth);
        n++;
    }

    private void Splatter(Span<Vector4> a, Span<Vector4> b, ref int n, in Splat s, float alpha, float grow, float h, float left, float w, bool subtle)
    {
        var size = s.Size * h * 0.5f * grow * (subtle ? 0.85f : 1f) * (1f + 0.25f * Darkness);
        a[n] = new Vector4(left + s.Pos.X * w, s.Pos.Y * h, size, s.Rot);
        b[n] = new Vector4(MathF.Min(1f, alpha * (subtle ? 0.55f : 0.95f)), s.Shape, Darkness, 0);
        n++;
    }

    /// <summary>The fixed layout of the base layers: three per step, corners and edges first, spread round the frame.</summary>
    private static Splat[] MakeLayout()
    {
        (float X, float Y)[] at =
        [
            (0.04f, 0.1f), (0.95f, 0.9f), (0.02f, 0.58f), // 85%
            (0.07f, 0.93f), (0.95f, 0.08f), (0.98f, 0.44f), // 70%
            (0.3f, 0.02f), (0.68f, 0.98f), (0.01f, 0.3f), // 50%
            (0.22f, 0.97f), (0.99f, 0.7f), (0.76f, 0.03f), // 30%
            (0.02f, 0.8f), (0.5f, 0.01f), (0.44f, 0.99f), // 15%
        ];
        var l = new Splat[BaseCount];
        for (var i = 0; i < l.Length; i++)
        {
            var step = i / PerStep;
            var h = BloodMarks.Hash((uint)i, 4242, 9);
            l[i] = new Splat
            {
                Pos = new Vector2(at[i].X, at[i].Y), Size = 0.13f + 0.025f * step + 0.04f * ((h & 0xFF) / 255f), Rot = (((h >> 8) & 0xFF) / 255f - 0.5f) * 1.0f,
                Shape = (int)((h >> 16) % BloodTextures.Layers), Threshold = Steps[step], Band = StepBand, Live = true,
            };
        }
        return l;
    }
}

/// <summary>
/// The overlay's splatter shapes, generated at startup (no image files): <see cref="Layers"/> single-channel
/// <see cref="Size"/>-pixel masks, each a ragged blob of overlapping lobes with flecks thrown around it and drips running
/// down, soft at the edges and thicker (brighter in the mask) in the middle, fading to zero before the texture border.
/// Deterministic, so the self-test can check coverage.
/// </summary>
public static class BloodTextures
{
    public const int Size = 128, Layers = 4;

    public static byte[] Generate()
    {
        var px = new byte[Size * Size * Layers];
        Span<Vector3> circles = stackalloc Vector3[48];
        Span<Vector4> drips = stackalloc Vector4[4];
        for (var layer = 0; layer < Layers; layer++)
        {
            var rng = new Foliage.Rng(0xB100D + (uint)layer * 7919u);
            var nc = 0;
            var core = 0.16f + 0.05f * rng.Next();
            circles[nc++] = new Vector3(0.5f, 0.5f, core);
            var lobes = 6 + (int)(rng.Next() * 5);
            for (var i = 0; i < lobes; i++)
            {
                var ang = rng.Next() * MathF.Tau;
                var d = core * (0.6f + 0.7f * rng.Next());
                circles[nc++] = new Vector3(0.5f + MathF.Cos(ang) * d, 0.5f + MathF.Sin(ang) * d, core * (0.35f + 0.4f * rng.Next()));
            }
            var flecks = 14 + (int)(rng.Next() * 14);
            for (var i = 0; i < flecks && nc < circles.Length; i++)
            {
                var ang = rng.Next() * MathF.Tau;
                var d = core * 1.5f + rng.Next() * (0.44f - core * 1.5f);
                circles[nc++] = new Vector3(0.5f + MathF.Cos(ang) * d, 0.5f + MathF.Sin(ang) * d, 0.006f + 0.02f * rng.Next() * (1.2f - d));
            }
            var nd = 1 + (int)(rng.Next() * 3);
            for (var i = 0; i < nd; i++)
                drips[i] = new Vector4(0.5f + (rng.Next() - 0.5f) * core * 1.4f, 0.5f, 0.1f + 0.15f * rng.Next(), 0.012f + 0.012f * rng.Next()); // x, top, bottom, half width
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f) / Size, (y + 0.5f) / Size);
                    var best = float.MaxValue; // signed distance (negative inside), in texture units
                    var thick = 0f;
                    for (var i = 0; i < nc; i++)
                    {
                        var c = circles[i];
                        var dist = Vector2.Distance(p, new Vector2(c.X, c.Y)) - c.Z;
                        best = MathF.Min(best, dist);
                        if (i < 1 + lobes)
                            thick = MathF.Max(thick, 1f - Vector2.Distance(p, new Vector2(c.X, c.Y)) / (c.Z * 1.6f));
                    }
                    for (var i = 0; i < nd; i++)
                    {
                        var dr = drips[i];
                        var wy = p.Y < dr.Z ? Vector2.Distance(p, new Vector2(dr.X, dr.Z)) - dr.W * 1.4f // the drop at the end
                            : p.Y > dr.Y ? float.MaxValue : MathF.Abs(p.X - dr.X) - dr.W;
                        best = MathF.Min(best, wy);
                    }
                    var edge = Math.Clamp(0.5f - best * Size / 1.6f, 0f, 1f); // ~1.6 px soft edge
                    var border = Math.Clamp(MathF.Min(MathF.Min(p.X, 1 - p.X), MathF.Min(p.Y, 1 - p.Y)) * Size / 3f, 0f, 1f);
                    var grain = 0.88f + 0.12f * ((BloodMarks.Hash((uint)x, (uint)y, (uint)layer) & 0xFF) / 255f);
                    var v = edge * border * (0.62f + 0.38f * Math.Clamp(thick, 0f, 1f)) * grain;
                    px[(layer * Size + y) * Size + x] = (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
                }
        }
        return px;
    }
}
