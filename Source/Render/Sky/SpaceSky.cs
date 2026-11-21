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
    private static float _moonSize, _sunSize;

    // The star as the scene's light (SkySettings.Light): "up" at the camera, how far the limb of the ground under it is below
    // level (sin; 1: no ground in the way, deep space) and how much of the star's disc other bodies leave it.
    private static Vector3 _up = Vector3.UnitY;
    private static float _dip, _seen = 1f;

    /// <summary>The smallest sun disc drawn (degrees): a far star is still a disc you can find, not a point.</summary>
    public const float MinSunSize = 0.6f;

    /// <summary>Is a map's sky following a bubble now?</summary>
    public static bool Active => _sky != null;

    /// <summary>The far bodies and ships the sky draws (empty when not <see cref="Active"/>).</summary>
    public static readonly SpaceSkyView View = new();

    /// <summary>Is the sky another star system's, round a ship out there (<see cref="UpdateAway"/>)? Then there's no air: it's black.</summary>
    public static bool Away { get; private set; }

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
            (_sunDirection, _moonDirection, _moon, _moonSize, _sunSize) = (sky.SunDirection, sky.MoonDirection, sky.Moon, sky.MoonSize, sky.SunSize);
        }
        Away = false;
        var b = space.Bubble;
        sky.SunDirection = b.SunDirection(t, camera);
        sky.Moon = false; // the moon is one of the View's bodies, lit and turning like the others
        View.Update(b, t, camera);
        SetStarLight(sky, map, b.System, t, b.Body, camera);
        AddShips(space, space.GalaxyIndex, 0, t);
    }

    /// <summary>
    /// The sky of another star system round a ship the local player rides there (phase 6, Game.SpaceAwayView): that
    /// system's star as the sun, its bodies, its other ships and the galaxy's other stars, from <paramref name="pose"/>
    /// (game coordinates in that system's ecliptic). The map's own sky settings stay, only the sun and what's in it change.
    /// </summary>
    public static void UpdateAway(Map map, SpaceSession space, StarSystem sys, in FramePose pose, int galaxy, Construct own, SpaceTime t, Vector3 camera)
    {
        var sky = map.Skybox;
        if (sky == null)
        {
            Restore();
            return;
        }
        if (!ReferenceEquals(_sky, sky))
        {
            Restore();
            _sky = sky;
            (_sunDirection, _moonDirection, _moon, _moonSize, _sunSize) = (sky.SunDirection, sky.MoonDirection, sky.Moon, sky.MoonSize, sky.SunSize);
        }
        sky.Moon = false;
        Away = true;
        View.Update(sys, pose, -1, t, camera);
        sky.SunDirection = View.StarDirection;
        SetStarLight(sky, null, sys, t, -1, camera);
        AddShips(space, galaxy, own.Id, t, own.SpaceShip);
    }

    /// <summary>
    /// The system's star is the light (Karl, 2026-11-21: the star at the centre is the global light): its disc at its real size
    /// from here, and the frame <see cref="SkySettings.Light"/> lights the scene in. On a planetoid "up" is away from its centre
    /// where the camera is, so the day side is lit and the night side dark wherever you stand, not only near the map's top; in
    /// orbit the star shines until the planetoid's limb covers it, from any side (a ship is lit from below too); other
    /// bodies in front of the star (eclipses) dim it. Away from any ground (<paramref name="map"/> null) only eclipses dim it.
    /// No allocation.
    /// </summary>
    private static void SetStarLight(SkySettings sky, Map? map, StarSystem sys, SpaceTime t, int own, Vector3 camera)
    {
        sky.SunSize = MathF.Max((float)(View.StarAngularRadius * 2 * 180 / Math.PI), MinSunSize);
        _up = Vector3.UnitY;
        _dip = map == null ? 1f : 0f;
        if (map?.Planetoid is { } pl)
        {
            var rel = camera - pl.Center;
            if (rel.LengthSquared() > 1e-6f)
                _up = Vector3.Normalize(rel);
            _dip = PlanetRenderer.HorizonDip(pl, camera);
        }
        _seen = (float)SpaceSkyView.SunVisible(sys, t, View.CameraInSystem, own);
    }

    /// <summary>
    /// The frame the star lights <paramref name="sky"/> in (<see cref="SetStarLight"/>): false, with world up, no dip and
    /// the full star, for a sky that isn't following a star system (the map's own fixed sun, lit as it always was).
    /// </summary>
    public static bool StarFrame(SkySettings sky, out Vector3 up, out float dip, out float seen)
    {
        if (!ReferenceEquals(_sky, sky))
        {
            (up, dip, seen) = (Vector3.UnitY, 0f, 1f);
            return false;
        }
        (up, dip, seen) = (_up, _dip, _seen);
        return true;
    }

    /// <summary>The ships of one system (galaxy index) as points, but not the one the camera rides (by construct, or by ship on the host).</summary>
    private static void AddShips(SpaceSession space, int galaxy, ushort ownConstruct, SpaceTime t, ushort ownShip = 0)
    {
        if (space.IsHost)
        {
            if (space.FlightOf(galaxy) is not { } f)
                return;
            var ships = f.Ships;
            for (var i = 0; i < ships.Count; i++)
                if (ships.Ids[i] != ownShip || ownShip == 0)
                    View.AddShip(ships.Ids[i], ships.PositionOf(i));
            return;
        }
        foreach (var (id, v) in space.Ships)
            if (v.System == galaxy && (ownConstruct == 0 || v.Construct != ownConstruct) && space.SampleShip(id, t, out var pos, out _))
                View.AddShip(id, pos);
    }

    /// <summary>The sky's own sun and moon back (the map left the bubble, or the game ended).</summary>
    public static void Restore()
    {
        Away = false;
        if (_sky is not { } sky)
            return;
        (sky.SunDirection, sky.MoonDirection, sky.Moon, sky.MoonSize, sky.SunSize) = (_sunDirection, _moonDirection, _moon, _moonSize, _sunSize);
        _sky = null;
        (_up, _dip, _seen) = (Vector3.UnitY, 0f, 1f);
        View.BodyCount = View.ShipCount = View.StarCount = 0;
    }
}
