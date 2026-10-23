using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// How an optic looks when you aim through it: the reticle, how much of the screen its lens fills at full aim (or the
/// eye distance that gives it), the eyepiece rim, the vignette and the glass tint. Optic files give it as their
/// "sight" block (see Attachments/README.md); an optic file without one, and a built-in scope, get
/// <see cref="Sights.Default"/>. Cosmetic only: it never changes where bullets go.
/// </summary>
public sealed class SightSpec
{
    /// <summary>The reticle drawn on the lens (never <see cref="GunModel.Reticle.None"/>).</summary>
    public GunModel.Reticle Reticle = GunModel.Reticle.Dot;

    /// <summary>Colour of the lit part of the reticle (dots, rings, chevrons).</summary>
    public Vector3 ReticleColor = new(1f, 0.15f, 0.1f);

    /// <summary>Reticle scale: 1 = its design size on a 1080p screen.</summary>
    public float ReticleSize = 1f;

    /// <summary>
    /// The lens radius at full aim as a fraction of half the screen height (1 = touches the top and bottom of the screen).
    /// 0 = the optic keeps its own eye distance (the reflex sights: their window stays the size it always was).
    /// </summary>
    public float Screen;

    /// <summary>Metres from the eye to the lens at full aim. 0 = worked out from <see cref="Screen"/>.</summary>
    public float EyeDistance;

    /// <summary>Lens radius in metres (half width for a reflex window). 0 = the model's own eyepiece.</summary>
    public float LensRadius;

    /// <summary>The eyepiece rim's outer radius as a multiple of the lens radius (round lenses). 0 = the model's own.</summary>
    public float Housing;

    /// <summary>0..1: how much the lens darkens towards its rim.</summary>
    public float Vignette = 0.3f;

    /// <summary>The glass tint over the view through the lens, and how strong it is (0..0.5).</summary>
    public Vector3 Tint = new(0.65f, 0.82f, 1f);

    public float TintAlpha = 0.05f;

    public SightSpec Clone() => (SightSpec)MemberwiseClone();
}

/// <summary>
/// The sight maths shared by the viewmodel, the lens pass (<see cref="SightLens"/>) and the self-test: every optic is a
/// physical sight on the gun with a lens (<see cref="GunModel.Lens"/>) you look through. At full aim the lens sits at
/// its eye distance from the camera, so its size on screen is set per optic: 1x reflex sights stay a small window,
/// 2-3x optics fill a good part of the screen, 4x+ most of it and 8x/12x all of it but a thin rim in the corners.
/// The viewmodel camera is fixed (<see cref="ViewModelFovDeg"/>), so a lens of radius r at distance e covers
/// r / (e * tan(fov/2)) of half the screen height whatever the world zoom.
/// </summary>
public static class Sights
{
    /// <summary>The first-person gun's vertical field of view (it never zooms, so the gun doesn't balloon when aiming).</summary>
    public const float ViewModelFovDeg = 70f;

    /// <summary>tan(<see cref="ViewModelFovDeg"/> / 2).</summary>
    public static readonly float TanHalfFov = MathF.Tan(ViewModelFovDeg * MathF.PI / 360f);

    /// <summary>The viewmodel camera's near plane: closer than any lens (<see cref="MinEyeDistance"/>).</summary>
    public const float NearPlane = 0.005f;

    /// <summary>The closest a lens may come to the eye (metres).</summary>
    public const float MinEyeDistance = 0.012f;

    /// <summary>The viewmodel projection for a screen of this aspect ratio.</summary>
    public static Matrix4x4 ViewModelProjection(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(ViewModelFovDeg * MathF.PI / 180f, aspect, NearPlane, 10f);

    private static readonly Vector3 Red = new(1f, 0.15f, 0.1f);
    private static readonly Vector3 Amber = new(1f, 0.82f, 0.55f);
    private static readonly Vector3 Cool = new(0.65f, 0.82f, 1f);

    /// <summary>True for the sights you look through with a magnified lens (the dark eyepiece turns clear as you aim).</summary>
    public static bool Magnified(WeaponDef d) => d.Optic is not (OpticKind.Iron or OpticKind.RedDot or OpticKind.MiniDot or OpticKind.Holo or OpticKind.Periscope or OpticKind.Bead) || GunModel.HasBuiltInScope(d);

    /// <summary>Has a lens to look through (any optic, or a built-in scope).</summary>
    public static bool HasLens(WeaponDef d) => GunModel.HasScope(d);

    /// <summary>The lens radius at full aim (fraction of half the screen height) a magnified optic of this zoom gets by default.</summary>
    public static float ScreenForZoom(float zoom)
    {
        ReadOnlySpan<float> z = [2f, 2.5f, 3f, 4f, 6f, 8f, 12f];
        ReadOnlySpan<float> s = [0.8f, 0.88f, 0.95f, 1.3f, 1.5f, 1.8f, 1.95f];
        if (zoom <= z[0])
            return MathF.Max(0.6f, s[0] - (z[0] - zoom) * 0.1f);
        for (var i = 1; i < z.Length; i++)
            if (zoom <= z[i])
            {
                var t = (MathF.Log2(zoom) - MathF.Log2(z[i - 1])) / (MathF.Log2(z[i]) - MathF.Log2(z[i - 1]));
                return float.Lerp(s[i - 1], s[i], t);
            }
        return s[^1];
    }

    private static readonly Dictionary<(OpticKind, float, bool), SightSpec> Defaults = new();

    /// <summary>The sight an optic kind gets without a "sight" block (built-in scopes: from their zoom). Cached; don't change it.</summary>
    public static SightSpec Default(OpticKind kind, float zoom, bool builtIn = false)
    {
        lock (Defaults)
        {
            if (Defaults.TryGetValue((kind, zoom, builtIn), out var s))
                return s;
            s = new SightSpec();
            switch (kind)
            {
                case OpticKind.RedDot:
                    (s.Reticle, s.ReticleColor, s.Vignette, s.Tint, s.TintAlpha) = (GunModel.Reticle.Dot, Red, 0, new Vector3(0.6f, 0.9f, 0.7f), 0.12f);
                    break;
                case OpticKind.MiniDot:
                    (s.Reticle, s.ReticleColor, s.ReticleSize, s.Vignette, s.Tint, s.TintAlpha) = (GunModel.Reticle.Dot, new Vector3(0.3f, 1f, 0.35f), 0.8f, 0, new Vector3(0.6f, 0.9f, 0.7f), 0.1f);
                    break;
                case OpticKind.Holo:
                    (s.Reticle, s.ReticleColor, s.Vignette, s.Tint, s.TintAlpha) = (GunModel.Reticle.HoloRing, new Vector3(0.35f, 0.95f, 1f), 0, new Vector3(0.21f, 0.57f, 0.6f), 0.12f);
                    break;
                case OpticKind.Prism2:
                    (s.Reticle, s.ReticleColor, s.Screen, s.Tint, s.TintAlpha) = (GunModel.Reticle.Horseshoe, new Vector3(1f, 0.32f, 0.1f), 0.8f, Amber, 0.06f);
                    break;
                case OpticKind.Acog:
                    (s.Reticle, s.Screen, s.Tint, s.TintAlpha) = (GunModel.Reticle.Chevron, 0.88f, Amber, 0.06f);
                    break;
                case OpticKind.Hybrid3:
                    (s.Reticle, s.Screen, s.Tint, s.TintAlpha) = (GunModel.Reticle.CircleDot, 0.95f, Amber, 0.06f);
                    break;
                case OpticKind.Scope4:
                    (s.Reticle, s.Screen) = (GunModel.Reticle.Duplex, 1.3f);
                    break;
                case OpticKind.Lpvo6:
                    (s.Reticle, s.Screen) = (GunModel.Reticle.Stadia, 1.5f);
                    break;
                case OpticKind.Scope8:
                    (s.Reticle, s.Screen) = (GunModel.Reticle.FineCross, 1.8f);
                    break;
                case OpticKind.Scope12:
                    (s.Reticle, s.Screen) = (GunModel.Reticle.MilDot, 1.95f);
                    break;
                case OpticKind.Scout:
                    (s.Reticle, s.Screen) = (GunModel.Reticle.Duplex, 0.82f);
                    break;
                case OpticKind.NightTube:
                    (s.Reticle, s.ReticleColor, s.Screen, s.Tint, s.TintAlpha) = (GunModel.Reticle.CircleDot, new Vector3(0.75f, 1f, 0.8f), 0.8f, new Vector3(0.25f, 1f, 0.35f), 0.3f);
                    break;
                case OpticKind.Periscope:
                    (s.Reticle, s.ReticleColor, s.Vignette, s.Tint, s.TintAlpha) = (GunModel.Reticle.Dot, Red, 0, new Vector3(0.6f, 0.9f, 0.7f), 0.12f);
                    break;
                case OpticKind.Bead:
                    (s.Reticle, s.ReticleColor, s.ReticleSize, s.Vignette, s.TintAlpha) = (GunModel.Reticle.Dot, new Vector3(1f, 0.8f, 0.35f), 1.2f, 0, 0f); // no glass: the bead is the dot
                    break;
                default: // a built-in scope (or iron sights, which have no lens and never use it)
                    (s.Reticle, s.Screen) = (GunModel.Reticle.Duplex, ScreenForZoom(zoom));
                    break;
            }
            if (s.Screen > 0)
                s.Vignette = 0.35f;
            Defaults[(kind, zoom, builtIn)] = s;
            return s;
        }
    }

    /// <summary>The sight for this weapon copy: its optic file's "sight" block, or the default for its optic kind.</summary>
    public static SightSpec Spec(WeaponDef d) => d.Sight ?? Default(d.Optic, d.AdsZoom, GunModel.HasBuiltInScope(d));

    /// <summary>The lens in model space with the sight's overrides (lens radius, rim) applied. Null without an optic.</summary>
    public static GunModel.OpticLens? Lens(WeaponDef d)
    {
        if (GunModel.Lens(d) is not { } l)
            return null;
        var s = Spec(d);
        if (s.LensRadius > 0)
        {
            var k = s.LensRadius / l.HalfW;
            l = l with { HalfW = l.HalfW * k, HalfH = l.HalfH * k, Rim = l.Rim * k };
        }
        if (s.Housing > 0 && l.Round)
            l = l with { Rim = l.HalfW * s.Housing };
        return l;
    }

    /// <summary>
    /// The eye distance this sight sets (metres from the eye to the lens at full aim), or null when the optic keeps its
    /// own (reflex sights). <see cref="GunModel.AdsZ"/> uses it.
    /// </summary>
    public static float? EyeDistanceOverride(WeaponDef d)
    {
        var s = Spec(d);
        if (s.EyeDistance > 0)
            return MathF.Max(MinEyeDistance, s.EyeDistance);
        if (s.Screen > 0 && Lens(d) is { } l)
            return MathF.Max(MinEyeDistance, MathF.Min(l.HalfW, l.HalfH) / (s.Screen * TanHalfFov));
        return null;
    }

    /// <summary>Metres from the eye to the lens at full aim (0 without an optic).</summary>
    public static float EyeDistance(WeaponDef d) => Lens(d) is { } l ? -(GunModel.AdsZ(d) + l.Center.Z) : 0f;

    // ---- Aim blends ------------------------------------------------------------------------------

    /// <summary>
    /// How much the view through the lens replaces whatever the gun shows behind it (0..1). Reflex sights start clear
    /// (you see the gun through the glass at the hip) and hide the gun parts behind the window as you aim; magnified
    /// lenses start as dark glass and turn into the view as you raise them. Without the see-through pass (old GPU) it's 0.
    /// </summary>
    public static float WorldOpacity(WeaponDef d, float aimT, bool seeThrough = true) =>
        !seeThrough ? 0f : Magnified(d) ? SmoothStep(0.3f, 0.8f, aimT) : GunModel.CutawayFor(aimT);

    /// <summary>Opacity of the dark glass under the view: 1 on a magnified lens (0 on reflex sights, whose model has its own glass).</summary>
    public static float GlassAlpha(WeaponDef d, float aimT, bool seeThrough = true) =>
        !Magnified(d) ? 0f : seeThrough ? 1f : 1f - SmoothStep(0.3f, 0.8f, aimT);

    /// <summary>How visible the reticle is (cross-fades with a reflex sight's own glowing dot).</summary>
    public static float ReticleAlpha(WeaponDef d, float aimT) => Magnified(d) ? SmoothStep(0.3f, 0.8f, aimT) : GunModel.CutawayFor(aimT);

    /// <summary>
    /// The housing cut-away to draw the gun with (<see cref="GunModel.AdsCutaway"/>). A magnified optic keeps its whole
    /// housing (the lens pass shows the view on top of it), so it's 0; reflex sights keep today's fade of the glowing
    /// dot and the holo hood. Without the see-through pass every optic falls back to the old fade-out.
    /// </summary>
    public static float Cutaway(WeaponDef d, float aimT, bool seeThrough = true) => Magnified(d) && seeThrough ? 0f : GunModel.CutawayFor(aimT);

    /// <summary>
    /// 0..1: how much of the gun's sway, bob and kick moves the lens when aimed. A lens a couple of centimetres from the eye
    /// would jump around the screen with the same few millimetres of motion as a red dot 15 cm away, so high-power optics
    /// are steadied (rotations, and so the reticle's recoil, are kept). 1 = untouched (iron sights and reflex sights move exactly as before).
    /// </summary>
    public static float Steadiness(WeaponDef d, float aimT)
    {
        if (!Magnified(d))
            return 1f;
        var keep = Math.Clamp(EyeDistance(d) / 0.2f, 0.05f, 1f);
        return float.Lerp(1f, keep, Math.Clamp(aimT, 0f, 1f));
    }

    // ---- Projection --------------------------------------------------------------------------------

    /// <summary>
    /// Where a lens is on screen. Centre and reticle in NDC (-1..1 each way), sizes in half screen heights (so a round
    /// lens is a circle of radius <see cref="HalfH"/> after scaling x by the aspect ratio). <see cref="Depth"/> = metres
    /// from the eye to the lens plane.
    /// </summary>
    public readonly record struct LensView(bool Valid, Vector2 Center, float HalfW, float HalfH, float Rim, bool Round, Vector2 Reticle, float Depth);

    /// <summary>Projects the lens of <paramref name="d"/> with the gun at <paramref name="pose"/> (gun -> camera space).</summary>
    public static LensView Project(WeaponDef d, in Matrix4x4 pose, float aspect)
    {
        if (Lens(d) is not { } l)
            return default;
        var c = Vector3.Transform(l.Center, pose);
        var depth = -c.Z;
        var fwd = Vector3.TransformNormal(-Vector3.UnitZ, pose);
        var ret = fwd.Z < -1e-4f ? new Vector2(fwd.X / (-fwd.Z * TanHalfFov * aspect), fwd.Y / (-fwd.Z * TanHalfFov)) : Vector2.Zero;
        if (depth < NearPlane)
            return new LensView(false, Vector2.Zero, 0, 0, 0, l.Round, ret, depth);
        var k = 1f / (depth * TanHalfFov);
        return new LensView(true, new Vector2(c.X * k / aspect, c.Y * k), l.HalfW * k, l.HalfH * k, l.Rim * k, l.Round, ret, depth);
    }

    /// <summary>The lens at full aim with the gun exactly on the sight line (no sway, bob or kick).</summary>
    public static LensView ProjectAimed(WeaponDef d, float aspect) =>
        Project(d, Matrix4x4.CreateTranslation(0, -GunModel.SightHeight(d), GunModel.AdsZ(d)), aspect);

    /// <summary>The fraction (0..1) of a screen of this aspect ratio that the lens covers.</summary>
    public static float Coverage(in LensView v, float aspect)
    {
        if (!v.Valid)
            return 0f;
        if (!v.Round)
        {
            // In NDC: half width HalfW / aspect, half height HalfH.
            var w = Overlap(v.Center.X - v.HalfW / aspect, v.Center.X + v.HalfW / aspect, -1, 1);
            var h = Overlap(v.Center.Y - v.HalfH, v.Center.Y + v.HalfH, -1, 1);
            return w * h / 4f;
        }
        // A circle of radius r (half heights) at (cx * aspect, cy); the screen is [-aspect, aspect] x [-1, 1]. Row by row.
        const int rows = 400;
        float r = v.HalfH, cx = v.Center.X * aspect, cy = v.Center.Y, sum = 0;
        for (var i = 0; i < rows; i++)
        {
            var y = -1f + (i + 0.5f) * 2f / rows - cy;
            if (MathF.Abs(y) >= r)
                continue;
            var half = MathF.Sqrt(r * r - y * y);
            sum += Overlap(cx - half, cx + half, -aspect, aspect);
        }
        return sum / rows / (2f * aspect);
    }

    private static float Overlap(float a0, float a1, float b0, float b1) => MathF.Max(0f, MathF.Min(a1, b1) - MathF.Max(a0, b0));

    private static float SmoothStep(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    // ---- Optic files: the "sight" block ------------------------------------------------------------

    /// <summary>The keys a "sight" block may have.</summary>
    public const string Keys = "reticle, reticleColor, reticleSize, screen, eyeDistance, lensRadius, housing, vignette, tint, tintAlpha";

    /// <summary>
    /// Reads an optic file's "sight" block on top of the defaults for its optic kind and zoom (so a block only needs the
    /// keys it changes). Throws <see cref="WeaponFileException"/> with a readable message.
    /// </summary>
    public static SightSpec Read(JsonElement e, OpticKind kind, float zoom, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected {{ ... }} with any of {Keys}");
        var s = Default(kind, zoom).Clone();
        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where}.{p.Name}";
            if (!given.Add(p.Name))
                throw new WeaponFileException($"{w}: given twice");
            switch (p.Name.ToLowerInvariant())
            {
                case "reticle":
                    s.Reticle = WeaponFile.EnumValue<GunModel.Reticle>(p.Value, w);
                    if (s.Reticle == GunModel.Reticle.None)
                        throw new WeaponFileException($"{w}: an optic needs a reticle (one of {string.Join(", ", Enum.GetNames<GunModel.Reticle>().Skip(1))})");
                    break;
                case "reticlecolor": s.ReticleColor = Colour(p.Value, w); break;
                case "reticlesize": s.ReticleSize = Ranged(p.Value, w, 0.25f, 4f); break;
                case "screen": s.Screen = Ranged(p.Value, w, 0.1f, 2.5f); break;
                case "eyedistance": s.EyeDistance = Ranged(p.Value, w, MinEyeDistance, 0.4f); break;
                case "lensradius": s.LensRadius = Ranged(p.Value, w, 0.005f, 0.05f); break;
                case "housing": s.Housing = Ranged(p.Value, w, 1f, 2f); break;
                case "vignette": s.Vignette = Ranged(p.Value, w, 0f, 1f); break;
                case "tint": s.Tint = Colour(p.Value, w); break;
                case "tintalpha": s.TintAlpha = Ranged(p.Value, w, 0f, 0.5f); break;
                default:
                    throw new WeaponFileException($"{where}: unknown key \"{p.Name}\". Known: {Keys}");
            }
        }
        return s;
    }

    private static float Ranged(JsonElement e, string where, float min, float max)
    {
        var v = WeaponFile.Number(e, where);
        if (!(v >= min && v <= max))
            throw new WeaponFileException($"{where}: {v} is outside {WeaponFile.Num(min)}..{WeaponFile.Num(max)}");
        return v;
    }

    private static Vector3 Colour(JsonElement e, string where)
    {
        var c = (Vector3)WeaponFile.FromJson(e, typeof(Vector3), where);
        if (!(c.X is >= 0 and <= 1 && c.Y is >= 0 and <= 1 && c.Z is >= 0 and <= 1))
            throw new WeaponFileException($"{where}: colour values go from 0 to 1");
        return c;
    }

    /// <summary>The "sight" block as JSON (every key, in a fixed order), for writing optic files back out.</summary>
    public static string ToJson(SightSpec s)
    {
        string N(float f) => WeaponFile.Num(f);
        var parts = new List<string>
        {
            $"\"reticle\": {WeaponFile.Str(s.Reticle.ToString())}",
            $"\"reticleColor\": {WeaponFile.Fmt(s.ReticleColor)}",
            $"\"reticleSize\": {N(s.ReticleSize)}",
        };
        if (s.Screen > 0)
            parts.Add($"\"screen\": {N(s.Screen)}");
        if (s.EyeDistance > 0)
            parts.Add($"\"eyeDistance\": {N(s.EyeDistance)}");
        if (s.LensRadius > 0)
            parts.Add($"\"lensRadius\": {N(s.LensRadius)}");
        if (s.Housing > 0)
            parts.Add($"\"housing\": {N(s.Housing)}");
        parts.Add($"\"vignette\": {N(s.Vignette)}");
        parts.Add($"\"tint\": {WeaponFile.Fmt(s.Tint)}");
        parts.Add($"\"tintAlpha\": {N(s.TintAlpha)}");
        return "{ " + string.Join(", ", parts) + " }";
    }
}
