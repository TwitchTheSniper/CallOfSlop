using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The turret camera (docs/sandbox/constructs/turret-camera.md, Karl 2026-11-22): seated at a gunner's station that works a turret through a
/// Turret Module, the first-person view is the turret's camera and the third-person orbit is round the turret, not the ship. The aim starts at
/// the same camera on every PC and the server (ConstructWeapons.SeatEye), so the guns converge where the crosshair is.
/// </summary>
public sealed partial class Game
{
    /// <summary>Is the local player at a station that looks through a turret? Its construct, seat and the gun whose turret it is.</summary>
    public bool LocalTurret(out Construct c, out int seat, out int gun)
    {
        c = null!;
        seat = gun = -1;
        if (!SandboxOn || Local is not { Alive: true } || LocalDrone() != null)
            return false;
        ref var h = ref SandboxHands.Hand(LocalId);
        if (!h.Seated || Constructs.Get(h.SeatBody) is not { } body || h.SeatPart >= body.PartCount || body.Parts[h.SeatPart].D.Role != PartRole.Seat)
            return false;
        gun = ConstructWeapons.TurretCamGun(body, h.SeatPart);
        if (gun < 0)
            return false;
        c = body;
        seat = h.SeatPart;
        return true;
    }

    /// <summary>The local turret's camera as this PC draws it now, or false when not at one.</summary>
    public bool LocalTurretEye(out Vector3 eye)
    {
        eye = default;
        return LocalTurret(out var c, out _, out var k) && ConstructWeapons.TurretEye(c, k, _alpha, true, out eye, out _, IsServer);
    }

    /// <summary>The third-person orbit round the local turret: its gun's middle, a little up the turret's up, as drawn now.</summary>
    private bool TurretOrbitPoint(out Vector3 point, out float dist, out float zoomMin, out float zoomMax)
    {
        point = default;
        dist = 9;
        zoomMin = 2.5f;
        zoomMax = 80;
        if (!LocalTurret(out var c, out _, out var k))
            return false;
        ref var g = ref c.Guns[k];
        var centre = ConstructRender.DrawnPoint(c, g.Part, Vector3.Zero, _alpha, IsServer);
        DrawnFrame(c, g.Seg, out var q, out _);
        var half = c.DefOf(g.Part).Boxes.Length > 0 ? MathF.Max(0.5f, Size(c, g.Part)) : 1f;
        point = centre + Vector3.Transform(Vector3.UnitY, q) * MathF.Min(2.5f, half * 0.6f);
        dist = Math.Clamp(half * 5f, 5f, 30f);
        return true;
    }

    /// <summary>A part's size: half its biggest box's longest side, scaled (the gun's turret frames the orbit's distance).</summary>
    private static float Size(Construct c, int part)
    {
        var big = 0f;
        var n = c.DefOf(part).Boxes.Length;
        for (var b = 0; b < n; b++)
        {
            c.BoxOf(part, b, out _, out var half);
            big = MathF.Max(big, MathF.Max(half.X, MathF.Max(half.Y, half.Z)));
        }
        return big;
    }
}
