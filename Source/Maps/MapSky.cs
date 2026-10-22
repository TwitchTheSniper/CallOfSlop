using System.Numerics;

namespace CallOfSlop;

/// <summary>A map's optional sky (the "skybox" section of its file; see <see cref="SkySettings"/> and Maps/README.md "Sky").</summary>
public sealed partial class Map
{
    /// <summary>
    /// The map's sky, or null for the classic look: a flat <see cref="Sky"/> colour as the background and fog, and the
    /// game's usual sun. With a sky, <see cref="Sky"/> is its horizon colour. Visual only: it is not part of
    /// <see cref="ContentHash"/>, so a sky change never stops players with different files from joining each other.
    /// </summary>
    public SkySettings? Skybox;

    /// <summary>
    /// The light every scene shader uses on this map: its sky's (or the classic light), plus <see cref="OuterFog"/>.
    /// </summary>
    public LightSetup Light => (Skybox is { } s ? s.Light() : Lighting.Default) with { OuterFog = OuterFog, OuterStarts = OuterFogStarts };

    /// <summary>How many directions <see cref="OuterFogStarts"/> has (the shader's uOuterR array is this long).</summary>
    public const int OuterFogDirections = 64;

    /// <summary>
    /// With <see cref="OuterFog"/>: the radius (from its centre) where the horizon fade starts in each of
    /// <see cref="OuterFogDirections"/> directions (direction i at angle i * 2 pi / N from +x towards +z; the shader
    /// interpolates between them). Each is just past the floor (outline plus margin) in that direction, so long or
    /// irregular outlines (Old Town is half as deep as it is wide) fade right past their edge on every side instead of at
    /// the farthest point's radius, which left a band of dark far ground past the short sides. Never inside the floor
    /// (see <see cref="OuterStartsFor"/>). Null when <see cref="OuterFog"/> is off.
    /// </summary>
    public float[]? OuterFogStarts => OuterFog.W > OuterFog.Z ? _outerStarts : null;

    /// <summary>
    /// Flat maps with a boundary: centre x, z and the start / end radius past which everything fades fully into the
    /// sky's horizon colour (see <see cref="LightSetup.OuterFog"/>). The start is just past the outline plus its floor
    /// margin, so nothing playable (or the edge itself) changes; the darker far ground beyond then meets the sky's horizon
    /// without a dark band whatever the camera's height. Zero (off) for maps without a boundary and for terrain maps
    /// (their land runs on past the outline as scenery), so those look exactly as before.
    /// </summary>
    public Vector4 OuterFog
    {
        get
        {
            if (Boundary is not { } b || HasTerrain)
                return Vector4.Zero;
            if (_outerFogFor == b)
                return _outerFog;
            var c = b.BoxCenter;
            var r = 0f;
            foreach (var p in b.Outline)
                r = MathF.Max(r, Vector2.Distance(p, c));
            var margin = Math.Clamp(b.Spec.GroundMargin, 0f, 64f) + 1f;
            var start = r + margin;
            _outerFog = new Vector4(c.X, c.Y, start, start + MathF.Max(30f, start * 0.5f));
            _outerStarts = OuterStartsFor(b.Outline, c, margin, start);
            _outerFogFor = b;
            return _outerFog;
        }
    }

    /// <summary>
    /// The per-direction fade start radii (see <see cref="OuterFogStarts"/>); <paramref name="fallback"/> where no floor is.
    /// The floor is the outline (sampled every 2 m) with a disc of <paramref name="margin"/> round each sample. Along
    /// 8 rays per direction step it finds the farthest floor point exactly (the far root of each ray-disc crossing), and
    /// each direction takes the farthest over the rays from one step before to one step after it, so the shader's
    /// interpolation between neighbouring directions never starts the fade inside the floor (plus 0.5 m to spare).
    /// </summary>
    internal static float[] OuterStartsFor(Vector2[] outline, Vector2 c, float margin, float fallback)
    {
        const int n = OuterFogDirections, sub = 8;
        var pts = new List<Vector2>();
        for (var s = 0; s < outline.Length; s++)
        {
            var a = outline[s];
            var b = outline[(s + 1) % outline.Length];
            var pieces = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / 2f));
            for (var k = 0; k < pieces; k++)
                pts.Add(Vector2.Lerp(a, b, k / (float)pieces) - c);
        }
        var r2 = margin * margin;
        var ray = new float[n * sub];
        for (var r = 0; r < ray.Length; r++)
        {
            var th = r * MathF.Tau / ray.Length;
            var u = new Vector2(MathF.Cos(th), MathF.Sin(th));
            var best = 0f;
            foreach (var p in pts)
            {
                var along = Vector2.Dot(p, u);
                var perp2 = p.LengthSquared() - along * along;
                if (perp2 <= r2)
                    best = MathF.Max(best, along + MathF.Sqrt(r2 - perp2));
            }
            ray[r] = best;
        }
        var starts = new float[n];
        for (var i = 0; i < n; i++)
        {
            var m = 0f;
            for (var r = (i - 1) * sub; r <= (i + 1) * sub; r++)
                m = MathF.Max(m, ray[(r % ray.Length + ray.Length) % ray.Length]);
            starts[i] = m > 0f ? MathF.Min(m + 0.5f, fallback) : fallback; // (never past the farthest point's radius)
        }
        return starts;
    }

    private float[]? _outerStarts;
    private Vector4 _outerFog;
    private MapBoundary? _outerFogFor;
}
