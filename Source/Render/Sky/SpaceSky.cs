using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The sky of a map in a star system (phases 3 and 4, docs/space/SPACE.md "The bubble", "Seeing space"): each frame, the
/// map's sky takes the real sun from the space clock (<see cref="SpaceBubble.SunDirection"/>), and <see cref="View"/> holds
/// every other body (lit spheres with phases, turning ground and eclipses) and the nearest ships, which the sky shader draws
/// (<see cref="SkyRenderer"/>). The sky's own single moon is switched off meanwhile: the bubble's moon is one of the bodies.
/// The sky's own sun and moon settings come back when the map leaves the bubble. Called before <see cref="SkyRenderer.Use"/>,
/// so the light and fog follow the same sun. No allocation.
/// </summary>
public static class SpaceSky
{
    private static SkySettings? _sky;
    private static Vector3? _sunDirection, _moonDirection;
    private static bool _moon;
    private static float _moonSize;

    /// <summary>Is a map's sky following a bubble now?</summary>
    public static bool Active => _sky != null;

    /// <summary>The far bodies and ships the sky draws (empty when not <see cref="Active"/>).</summary>
    public static readonly SpaceSkyView View = new();

    /// <summary>The sky <see cref="View"/> belongs to (the renderer draws the bodies only into that sky).</summary>
    public static SkySettings? Sky => _sky;

    /// <summary>The map's sky follows the bubble at this time, seen from the camera (the moon's way depends on where you stand).</summary>
    public static void Update(Map map, SpaceSession? space, SpaceTime t, Vector3 camera)
    {
        var sky = map.Skybox;
        if (space == null || sky == null || !space.IsFor(map))
        {
            Restore();
            return;
        }
        if (!ReferenceEquals(_sky, sky))
        {
            Restore();
            _sky = sky;
            (_sunDirection, _moonDirection, _moon, _moonSize) = (sky.SunDirection, sky.MoonDirection, sky.Moon, sky.MoonSize);
        }
        var b = space.Bubble;
        sky.SunDirection = b.SunDirection(t, camera);
        sky.Moon = false; // the moon is one of the View's bodies, lit and turning like the others
        View.Update(b, t, camera);
        if (space.Flight is { } f)
        {
            var ships = f.Ships;
            for (var i = 0; i < ships.Count; i++)
                View.AddShip(ships.Ids[i], ships.PositionOf(i));
        }
        else
            foreach (var (id, _) in space.Ships)
                if (space.SampleShip(id, t, out var pos, out _))
                    View.AddShip(id, pos);
    }

    /// <summary>The sky's own sun and moon back (the map left the bubble, or the game ended).</summary>
    public static void Restore()
    {
        if (_sky is not { } sky)
            return;
        (sky.SunDirection, sky.MoonDirection, sky.Moon, sky.MoonSize) = (_sunDirection, _moonDirection, _moon, _moonSize);
        _sky = null;
        View.BodyCount = View.ShipCount = 0;
    }
}
