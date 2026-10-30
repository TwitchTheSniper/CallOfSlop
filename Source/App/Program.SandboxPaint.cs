using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The paint tool on this PC (board item dev-sandbox-paint-tool-pp01, decision 29; ConstructPaint.cs, SandboxPaintWheel.cs).
/// LMB paints the part (or link) under the crosshair; holding it and sweeping paints each part passed over once per stroke
/// (a set of the parts already done, cleared when the button goes up, so a part is never acted on twice in a stroke); RMB
/// picks up the colour of what is aimed at (the eyedropper: read off this PC's copy, nothing is sent). The mode (Part,
/// Whole construct, Reset) and the colour come from the tool wheel's ring round the paint tool; "Custom..." opens the
/// colour picker (UiSandboxPaint.cs). In survival, painting a whole construct asks for a second click. Each stroke is a
/// request to the server (ConstructEdits.Paint), which checks the tool, the reach and the contested-construct rule.
/// </summary>
public static partial class Program
{
    private static readonly SandboxPaintWheel _paintWheel = new();
    private static readonly PaintStroke _ptStroke = new();
    private static ushort _ptConfirmBody;
    private static long _ptConfirmAt;
    private static long _ptLookKey = long.MinValue;
    private static string _ptLook = "";

    /// <summary>Every frame with the paint tool out and the mouse captured (from SandboxInput, with the crosshair's hit).</summary>
    private static void SandboxPaintInput(Game g, Player me, bool aimed, in ConstructHit hit)
    {
        var inReach = aimed && hit.Distance <= ConstructBuild.Reach;
        var route = inReach && hit.RoutePlus1 > 0 && hit.Body.RouteCount >= hit.RoutePlus1;
        var index = route ? hit.RoutePlus1 - 1 : hit.Part;
        var fire = Down(GameAction.Fire) && !_swallowClick;
        if (!fire)
            _ptStroke.Clear();
        var mode = _paintWheel.Mode;
        if (inReach)
        {
            var c = hit.Body;
            uint now = route ? c.Routes[index].Paint : c.Parts[index].Paint;
            var key = ((long)c.Id << 24) ^ ((long)index << 4) ^ (route ? 1L << 20 : 0) ^ ((long)mode << 1) ^ ((long)now << 32) ^ (_paintWheel.Colour << 7);
            if (key != _ptLookKey)
            {
                _ptLookKey = key;
                var what = route ? char.ToUpperInvariant(ConstructRoutes.Name(c.Routes[index].Type, c.Routes[index].Style)[0]) + ConstructRoutes.Name(c.Routes[index].Type, c.Routes[index].Style)[1..] : c.DefOf(index).Name;
                var has = ConstructPaint.IsSet(now) ? $"painted {ConstructPaint.ToHex(now)}" : "own colours";
                var does = mode switch { PaintMode.Whole => "whole construct", PaintMode.Reset => "reset its colour", _ => $"paint {ConstructPaint.ToHex(_paintWheel.Colour)}" };
                _ptLook = $"{what} ({has}) - LMB: {does}, RMB: pick its colour";
            }
            g.SandboxLookText = _ptLook;
        }
        else
            _ptLookKey = long.MinValue;
        if (!inReach)
            return;
        var body = hit.Body;
        // The eyedropper.
        if (Pressed(GameAction.Aim))
        {
            var picked = route
                ? ConstructPaint.IsSet(body.Routes[index].Paint) ? body.Routes[index].Paint : ConstructPaint.FromVector(RouteRender.Colour(body.Routes[index].Type, body.Routes[index].Style))
                : ConstructPaint.Sample(body.Parts[index]);
            _paintWheel.SetColour(picked);
            g.SandboxSay($"picked up {ConstructPaint.ToHex(picked)}");
            return;
        }
        if (!fire)
            return;
        if (!_ptStroke.Try(body.Id, route, index, whole: mode == PaintMode.Whole))
            return; // (this part is done in this stroke)
        if (mode == PaintMode.Whole && !g.SandboxInfiniteParts)
        {
            // Survival: the whole construct takes a second click.
            var t = Environment.TickCount64;
            if (_ptConfirmBody != body.Id || t - _ptConfirmAt > 4000)
            {
                _ptConfirmBody = body.Id;
                _ptConfirmAt = t;
                g.SandboxSay("paint the whole construct? click again to confirm");
                return;
            }
            _ptConfirmBody = 0;
        }
        if (mode != PaintMode.Reset)
            _paintWheel.Remember(_paintWheel.Colour);
        g.SandboxPaint(body.Id, mode, route, index, _paintWheel.Colour);
    }

    /// <summary>Every frame (from DrawSandboxPaintInMatch): the stroke is over when the paint tool isn't out.</summary>
    private static void PaintIdle()
    {
        _ptStroke.Clear();
        _ptConfirmBody = 0;
    }

    /// <summary>The colour picker over the match ("Custom..." on the wheel's ring); it closes (the mouse goes back to aiming) when it should.</summary>
    private static void DrawSandboxPaintInMatch()
    {
        if (_paintWheel.WantCustom)
        {
            _paintWheel.WantCustom = false;
            if (_game is { SandboxOn: true } && !MenusOpen)
            {
                Ui.OpenSandboxPaintPicker(_paintWheel.Colour);
                SetMouseCaptured(false);
            }
        }
        if (!Ui.SandboxPaintPickerOpen)
            return;
        var me = _game?.Local;
        var gone = _session == null || _mouseCaptured || _game is not { SandboxOn: true } || me is not { Alive: true } || _game.Phase != MatchPhase.Playing;
        if (!gone)
        {
            ref var h = ref _game!.SandboxHands.Hand(me!.Id);
            gone = h.Seated || h.Tool != ConstructTool.Paint;
        }
        if (!gone)
        {
            var open = Ui.DrawSandboxPaintPicker(_paintWheel.Recent, out var chosen);
            if (chosen != 0)
                _paintWheel.SetColour(chosen);
            if (open)
                return;
        }
        Ui.CloseSandboxPaintPicker();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't also paint)
    }
}
