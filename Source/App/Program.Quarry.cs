using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The quarry duty's crew bus scene on this machine (GameQuarry.cs, docs/story/DUTIES.md): while the player rides, nothing
/// moves or acts, and the camera follows the bus away from the stop, goes black for a moment, then watches the other stop's bus
/// come in. Both buses are STAND-IN craft (Source/StandIns), posed from the server tick like everyone else's.
/// </summary>
public static partial class Program
{
    /// <summary>The black over the screen at <paramref name="t"/> s into the ride (0 to 1).</summary>
    public static float QuarryRideBlack(double t)
    {
        var a = Game.QuarryRideAway;
        var b = a + Game.QuarryRideBlack;
        if (t < a - 0.6 || t > b + 0.6)
            return 0;
        if (t < a)
            return (float)((t - (a - 0.6)) / 0.6);
        if (t > b)
            return (float)(1 - (t - b) / 0.6);
        return 1;
    }

    /// <summary>One frame of input on the bus: nothing moves or acts. False: not riding.</summary>
    private static bool QuarryRideInput(double now)
    {
        if (!_game.QuarryRiding)
            return false;
        _crouchToggle = _proneToggle = false;
        _game.HeldButtons = Buttons.None;
        _game.AimHeld = false;
        _game.UpdateTrigger(now, false, false);
        return true;
    }

    /// <summary>The bus scene's camera: true when it has the view this frame.</summary>
    private static bool QuarryRideCamera(ref Vector3 eye, ref Vector3 fwd, ref Vector3 up)
    {
        if (!_game.QuarryRiding)
            return false;
        var tick = _game.MoverRenderTick;
        var t = (tick - _game.RideFrom) / World.TickRate;
        var id = t < Game.QuarryRideAway + Game.QuarryRideBlack * 0.5 ? _game.RideBusA : _game.RideBusB;
        for (var k = 0; k < _game.StandInCount; k++)
        {
            ref var c = ref _game.StandIns[k];
            if (c.Id != id)
                continue;
            if (!StandInPath.Pose(ref c, tick, out var frame, out _, out _))
            {
                // (the arriving bus before it shows: from its stop, looking back the way it comes)
                var u0 = StandInPath.Up(c.Land);
                eye = c.Land + u0 * 4f + c.Way * 10f;
                fwd = Vector3.Normalize(c.Land - c.Way * 30f - eye);
                up = u0;
                return true;
            }
            var right = new Vector3(frame.M11, frame.M12, frame.M13);
            var u = new Vector3(frame.M21, frame.M22, frame.M23);
            var ahead = new Vector3(frame.M31, frame.M32, frame.M33);
            var at = frame.Translation;
            if (id == _game.RideBusA)
                eye = at - ahead * 13f + u * 5f + right * 2f; // (behind it as it pulls away)
            else
                eye = c.Land + StandInPath.Up(c.Land) * 3.5f + c.Way * 9f + Vector3.Cross(StandInPath.Up(c.Land), c.Way) * 7f; // (at the stop, watching it come in)
            var look = at + u * 1.5f - eye;
            fwd = look.LengthSquared() > 0.01f ? Vector3.Normalize(look) : ahead;
            up = u;
            return true;
        }
        return false;
    }
}
