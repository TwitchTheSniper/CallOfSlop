using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The configure tool on this PC (docs/sandbox/CONSTRUCTS.md, "The configure tool"; the panel is UiSandboxConfig.cs):
/// with it out, Fire on a part opens that part's settings panel (the mouse comes free for it), Shift+Fire adds a part to
/// the selection the panel changes together (parts of one kind). The panel closes on Esc, its Close button, death, a
/// seat, putting the tool away, the part going or walking out of reach. A key-capture field takes the next key pressed.
/// </summary>
public static partial class Program
{
    private static long _sbCfgLookKey = -1;
    private static string _sbCfgLookText = "";

    /// <summary>The configure tool's keys while the mouse is captured (from SandboxInput).</summary>
    private static void SandboxConfigInput(Game g, Player me, bool aimed, in ConstructHit hit)
    {
        if (!aimed)
        {
            g.SandboxConfigHint = Ui.SandboxConfigSelection.Length > 0
                ? "LMB on a part: its settings. Shift+LMB: select more. RMB: clear the selection"
                : "LMB on a part: its settings. Shift+LMB: select more of the same kind";
            if (Pressed(GameAction.Aim))
                Ui.SandboxConfigClearSelection();
            return;
        }
        var c = hit.Body;
        var d = c.DefOf(hit.Part);
        var why = g.SandboxConfigRefusal(c, me.Id);
        var key = ((long)c.Id << 24) | ((long)hit.Part << 16) | (long)Ui.SandboxConfigSelection.Length << 8 | (why != null ? 1L : 0) | (d.Configurable ? 2L : 0);
        if (key != _sbCfgLookKey)
        {
            _sbCfgLookKey = key;
            _sbCfgLookText = why ?? (d.Configurable
                ? $"{d.Name}: LMB for its {d.Config.Length} setting{(d.Config.Length == 1 ? "" : "s")}{(Ui.SandboxConfigSelection.Length > 0 ? $" ({Ui.SandboxConfigSelection.Length} selected)" : "")}"
                : $"{d.Name}: no settings");
        }
        g.SandboxLookText = _sbCfgLookText;
        if (Pressed(GameAction.Aim))
            Ui.SandboxConfigClearSelection();
        if (!Pressed(GameAction.Fire) || _swallowClick)
            return;
        if (!d.Configurable)
        {
            g.SandboxSay($"a {d.Name} has no settings");
            return;
        }
        if (why != null)
        {
            g.SandboxSay(why);
            return;
        }
        var shift = _window.GetKey(GlfwKey.LeftShift) == GlfwKeyAction.Press || _window.GetKey(GlfwKey.RightShift) == GlfwKeyAction.Press;
        if (shift)
        {
            var n = Ui.SandboxConfigSelect(c, hit.Part);
            g.SandboxSay(n > 0 ? $"{n} {d.Name}{(n == 1 ? "" : "s")} selected (LMB on one opens their settings)" : "selection cleared");
            return;
        }
        if (Ui.SandboxConfigSelection.Length > 0)
            Ui.SandboxConfigSelect(c, hit.Part); // (a click on one of the kind joins the selection, or starts anew)
        Ui.OpenSandboxConfig(c, hit.Part);
        SetMouseCaptured(false);
    }

    /// <summary>The settings panel over the match; it closes (and the mouse goes back to aiming) when it should.</summary>
    private static void DrawSandboxConfigInMatch(double now)
    {
        if (!Ui.SandboxConfigOpen)
        {
            ConstructRender.ClearPreview();
            return;
        }
        var me = _game.Local;
        var gone = _session == null || _mouseCaptured || !_game.SandboxOn || me is not { Alive: true } || _game.Phase != MatchPhase.Playing;
        if (!gone)
        {
            ref var h = ref _game.SandboxHands.Hand(me!.Id);
            var (body, part) = Ui.SandboxConfigTarget;
            gone = h.Seated || h.Tool != ConstructTool.Configure || _game.Constructs.Get(body) is not { } c || part >= c.PartCount
                   || Vector3.Distance(me!.RenderEye, ConstructRender.PartCentre(c, part)) > ConstructConfig.Reach + 2;
        }
        if (!gone && Ui.DrawSandboxConfig(_game, now))
            return;
        Ui.CloseSandboxConfig();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't open it again)
    }

    /// <summary>A key or mouse button while the panel's key field is capturing: the reader key it is bound to, if a seat reads it.</summary>
    private static bool SandboxConfigKey(int code)
    {
        if (Ui.SandboxConfigCapture < 0)
            return false;
        if (code == Keybinds.Key(GlfwKey.Escape))
        {
            Ui.SandboxConfigCaptured(_game, -1, Glfw.Time);
            return true;
        }
        var (body, part) = Ui.SandboxConfigTarget;
        if (_game.Constructs.Get(body) is not { } c || part >= c.PartCount)
        {
            Ui.SandboxConfigCaptured(_game, -1, Glfw.Time);
            return true;
        }
        var d = c.DefOf(part);
        var e = d.Config[Math.Min(Ui.SandboxConfigCapture, d.Config.Length - 1)];
        for (var k = 0; k < e.Options.Length; k++)
            if (ReaderAction(e.Options[k]) is { } a && _settings.Bind(a) == code)
            {
                Ui.SandboxConfigCaptured(_game, k, Glfw.Time);
                return true;
            }
        _game.SandboxSay($"{Keybinds.Name(code)} isn't a key a seat passes on (it reads: {string.Join(", ", e.Options)})");
        return true;
    }

    private static GameAction? ReaderAction(string key) => key switch
    {
        "fire" => GameAction.Fire,
        "aim" => GameAction.Aim,
        "reload" => GameAction.Reload,
        "jump" => GameAction.Jump,
        "sprint" => GameAction.Sprint,
        "forward" => GameAction.MoveForward,
        "back" => GameAction.MoveBack,
        "left" => GameAction.MoveLeft,
        "right" => GameAction.MoveRight,
        _ => null,
    };
}
