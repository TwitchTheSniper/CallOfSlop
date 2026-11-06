using System.Numerics;

namespace CallOfSlop;

/// <summary>The client's side of flight control (docs/sandbox/CONSTRUCTS.md, "Flight control"): a pilot's finger on a Flight Panel, and the balance markers.</summary>
public static partial class Program
{
    /// <summary>Seated on a stabilised craft, the crosshair is a finger on its Flight Panel. One ray a frame, on those seats only.</summary>
    private static void SeatedFinger(Game g, Player me, in SandboxHand h, double now)
    {
        if (g.Constructs.Get(h.SeatBody) is not { StabCount: > 0 })
            return;
        SandboxAimRay(g, me, out var eye, out var dir);
        var aimed = g.Constructs.Raycast(eye, dir, ConstructBuild.UseReach, out var hit, look: true);
        PointerFrame(g, me, in h, eye, dir, aimed, hit, now);
    }

    private const double BalanceHold = 1.0; // s: the markers stay after the crosshair leaves the craft
    private static ushort _balBody;
    private static double _balUntil;
    private static bool _balShown;
    private static Vector3 _balComAt, _balLiftAt, _balThrustAt;
    private static long _balKey = long.MinValue;
    private static string _balLiftText = "", _balThrustText = "";

    private static string Fore(float metres) => MathF.Abs(metres) < 0.05f ? "over the centre of mass" : $"{MathF.Abs(metres):0.0} m {(metres > 0 ? "ahead of" : "behind")} it";

    /// <summary>With a build tool out: the centre of mass, lift and thrust of the flyer under the crosshair, and a moment after.</summary>
    private static void BalanceFrame(Game g, in SandboxHand h, bool aimed, in ConstructHit hit, double now)
    {
        _balShown = false;
        var building = h.Tool is ConstructTool.Welder or ConstructTool.Linker or ConstructTool.Configure or ConstructTool.Selector || h.Held != ConstructParts.None;
        if (!building)
            return;
        if (aimed && ConstructFlight.Flies(hit.Body))
        {
            _balBody = hit.Body.Id;
            _balUntil = now + BalanceHold;
        }
        if (now > _balUntil || g.Constructs.Get(_balBody) is not { } c)
            return;
        var hasLift = c.LiftCentre(out var lift, out var kg);
        var hasThrust = c.ThrustCentre(out var thrust, out var push, out var force);
        ConstructRender.BalanceCom = c.Com;
        ConstructRender.BalanceLift = lift;
        ConstructRender.BalanceThrust = thrust;
        ConstructRender.BalancePush = push;
        ConstructRender.BalanceHasLift = hasLift;
        ConstructRender.BalanceHasThrust = hasThrust;
        ConstructRender.ShowBalance(c);
        _balShown = true;
        _balComAt = c.ToWorld(c.Com);
        _balLiftAt = c.ToWorld(lift);
        _balThrustAt = c.ToWorld(thrust);
        var fwd = c.DriveFwd;
        var ahead = Vector3.Dot(lift - c.Com, fwd);
        var pushAhead = Vector3.Dot(thrust - c.Com, fwd);
        var key = (long)MathF.Round(ahead * 10) * 8191 + (long)MathF.Round(pushAhead * 10) * 131 + (long)(kg / 10) * 524287 + (long)(force / 1000);
        if (key == _balKey)
            return;
        _balKey = key;
        _balLiftText = hasLift ? $"Lift {kg:0} kg: {Fore(ahead)}" : "";
        _balThrustText = hasThrust ? $"Thrust {force / 1000f:0.#} kN: {Fore(pushAhead)}" : "";
    }

    private static void DrawBalanceLabels()
    {
        if (!_balShown)
            return;
        Hud.WorldLabel(_balComAt, "Centre of mass", new Vector3(1f, 0.85f, 0.2f), 0.95f, 13f * Ui.Scale, 60f);
        if (_balLiftText.Length > 0)
            Hud.WorldLabel(_balLiftAt, _balLiftText, new Vector3(0.3f, 0.75f, 1f), 0.95f, 13f * Ui.Scale, 60f);
        if (_balThrustText.Length > 0)
            Hud.WorldLabel(_balThrustAt, _balThrustText, new Vector3(1f, 0.45f, 0.15f), 0.95f, 13f * Ui.Scale, 60f);
    }
}
