using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The turret camera on this PC (Game/GameTurretView.cs has the rules, docs/sandbox/constructs/turret-camera.md): at a gunner's station that works
/// a turret, first person looks out of the turret's camera, with the gunsight's zoom on the wheel; the readouts are UI/GunnerHud.cs.
/// </summary>
public static partial class Program
{
    private static readonly float[] TurretZooms = [1f, 2f, 4f, 8f];
    private static int _turretZoom; // (an index into TurretZooms)
    private static ulong _turretZoomKey; // the station the zoom belongs to (body << 16 | seat + 1)
    private static bool _turretView; // this frame's first-person view is a turret's camera

    /// <summary>The gunsight's magnification now (1 off a turret's camera, and in third person).</summary>
    private static float TurretZoomNow() => _turretView && !ThirdPerson.Active ? TurretZooms[_turretZoom] : 1f;

    /// <summary>In the frame, first person: puts the camera on the local station's turret. True when it did (the body is drawn in its chair).</summary>
    private static bool TurretCamera(ref Vector3 eye, ref float fov)
    {
        _turretView = false;
        if (!_game.LocalTurret(out var c, out var seat, out _) || !_game.LocalTurretEye(out var at))
        {
            _turretZoomKey = 0;
            return false;
        }
        var key = (ulong)c.Id << 16 | (uint)(seat + 1);
        if (key != _turretZoomKey)
        {
            _turretZoomKey = key;
            _turretZoom = 0; // (a station sat at again starts unmagnified)
        }
        _turretView = true;
        eye = at;
        fov /= TurretZooms[_turretZoom];
        return true;
    }

    /// <summary>The wheel steps the gunsight's zoom while looking through a turret's camera.</summary>
    private static bool TurretScroll(double y)
    {
        if (!_turretView || ThirdPerson.Active)
            return false;
        _turretZoom = Math.Clamp(_turretZoom + (y > 0 ? 1 : -1), 0, TurretZooms.Length - 1);
        return true;
    }
}
