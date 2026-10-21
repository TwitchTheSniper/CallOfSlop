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
    public LightSetup Light => (Skybox is { } s ? s.Light() : Lighting.Default) with { OuterFog = OuterFog };

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
            var start = r + Math.Clamp(b.Spec.GroundMargin, 0f, 64f) + 1f;
            _outerFog = new Vector4(c.X, c.Y, start, start + MathF.Max(30f, start * 0.5f));
            _outerFogFor = b;
            return _outerFog;
        }
    }

    private Vector4 _outerFog;
    private MapBoundary? _outerFogFor;
}
