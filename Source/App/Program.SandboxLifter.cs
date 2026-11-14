using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: a lifter's panel in the match (UiLifter.cs; arms-and-cranes.md section 5 "By hand"). The use key on a Loader Arm or a Crane
/// Bridge opens it (a crane's cabinet from the floor under its rails: <see cref="ConstructLifters.PanelReach"/>). It holds the mouse
/// while open and closes when its lifter goes, the player dies or walks off, on Esc and on a local reset.
/// </summary>
public static partial class Program
{
    /// <summary>Is the crosshair's part a lifter whose panel this eye may open? (the use key's branch)</summary>
    private static bool LifterUse(Construct c, int part, float distance) =>
        part >= 0 && part < c.PartCount && ConstructLifters.DefOf(c, part) is { } ld && distance <= ConstructLifters.PanelReach(ld);

    private static void OpenLifterPanel(Construct c, int part)
    {
        if (MenusOpen)
            return;
        Ui.OpenLifter(c.Id, part);
        SetMouseCaptured(false);
    }

    /// <summary>The panel over the match (the Sandbox ImGui pass); it closes (the mouse goes back to aiming) when it should.</summary>
    private static void DrawLifterInMatch(double now)
    {
        if (!Ui.LifterOpen)
            return;
        var g = _game;
        var me = g?.Local;
        var (body, part) = Ui.LifterTarget;
        var c = g?.Constructs.Get(body);
        var gone = _session == null || g is not { SandboxOn: true } || me is not { Alive: true } || g.Phase != MatchPhase.Playing || c == null || _mouseCaptured
            || part < 0 || part >= c.PartCount || ConstructLifters.DefOf(c, part) is not { } ld
            || Vector3.Distance(me!.Position, ConstructEdits.ReachPoint(c, part, me.Position)) > ConstructLifters.PanelReach(ld) + 4;
        if (!gone && Ui.DrawLifter(g!, c!, part, now))
            return;
        Ui.CloseLifter();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't also fire the tool)
    }
}

public static partial class Program
{
    /// <summary>
    /// The use key with nothing at hand: a Crane Bridge's cabinet further along the look, up on its rails, opens its panel when the eye is
    /// within its pendant's reach. True when it did.
    /// </summary>
    private static bool CraneUse(Game g, Vector3 eye, Vector3 dir)
    {
        if (!g.Constructs.Raycast(eye, dir, 40f, out var hit, noTracks: true) || hit.Part < 0 || hit.Part >= hit.Body.PartCount
            || ConstructLifters.DefOf(hit.Body, hit.Part) is not { Bridge: true } || !LifterUse(hit.Body, hit.Part, hit.Distance))
            return false;
        OpenLifterPanel(hit.Body, hit.Part);
        return true;
    }
}

public static partial class Program
{
    private static long _cabKey = -1;
    private static string _cabText = "";

    /// <summary>
    /// Seated: is it a Lifter Cab? Then its lifter's reach view and the hook's shadow are drawn (LifterRender), and the look line says what
    /// it does and which keys steer it (made again only when the lifter's state changes).
    /// </summary>
    private static bool CabSeated(Game g, in SandboxHand h)
    {
        if (g.Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount || !c.DefOf(h.SeatPart).LifterCab)
        {
            LifterRender.CabBody = 0;
            return false;
        }
        (LifterRender.CabBody, LifterRender.CabPart) = (c.Id, h.SeatPart);
        var l = ConstructLifters.CabLifter(g.Constructs, c, h.SeatPart);
        var key = l == null ? -2 : ConstructLifters.LookKey(g.Constructs, c, l.Part) ^ ((long)c.Id << 52) ^ (l.Steered ? 1L << 62 : 0);
        if (key != _cabKey)
        {
            _cabKey = key;
            _cabText = l == null ? "Lifter Cab: no Loader Arm or Bridge Crane on this construct to steer - E to get out"
                : ConstructLifters.Look(g.Constructs, c, l.Part) + " - W A S D move the hook, Space and Ctrl up and down, F grab or set down, E get out";
        }
        g.SandboxLookText = _cabText;
        return true;
    }
}
