using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The sky of a map in a star system (phase 3, docs/space/SPACE.md "The bubble"): each frame, the map's sky takes the real
/// sun from the space clock (<see cref="SpaceBubble.SunDirection"/>) and shows the body's nearest moon where its orbit puts
/// it, at its real size (or no moon when the body has none). The sky's own sun and moon settings come back when the map
/// leaves the bubble. Called before <see cref="SkyRenderer.Use"/>, so the light and fog follow the same sun. No allocation.
/// </summary>
public static class SpaceSky
{
    private static SkySettings? _sky;
    private static Vector3? _sunDirection, _moonDirection;
    private static bool _moon;
    private static float _moonSize;

    /// <summary>Is a map's sky following a bubble now?</summary>
    public static bool Active => _sky != null;

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
        if (b.Moon >= 0 && b.Look(b.Moon, t, camera, out var moonDir, out _, out var angularRadius))
        {
            sky.Moon = true;
            sky.MoonDirection = moonDir;
            sky.MoonSize = Math.Clamp((float)(2 * angularRadius * 180 / Math.PI), 0.3f, 30f);
        }
        else
            sky.Moon = false;
    }

    /// <summary>The sky's own sun and moon back (the map left the bubble, or the game ended).</summary>
    public static void Restore()
    {
        if (_sky is not { } sky)
            return;
        (sky.SunDirection, sky.MoonDirection, sky.Moon, sky.MoonSize) = (_sunDirection, _moonDirection, _moon, _moonSize);
        _sky = null;
    }
}
