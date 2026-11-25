using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>
/// Main menu > SHIP DESIGNER from the command line (a dev's check of the screen): <c>--ship-designer [hull|pieces|interior|connectors]</c>
/// opens it at start in that mode; <c>--sd-shot N</c> also writes a picture after N frames (smoke_shipdesigner.bmp in the log
/// folder) and quits. Neither writes the kept design (shipdesign.json).
/// </summary>
public static unsafe partial class Program
{
    private static int _sdShotIn = -1;
    private static bool _sdOpenAtStart;
    private static string? _sdStartMode;

    private static void ShipDesignerArgs(string[] args)
    {
        var i = Array.FindIndex(args, a => a.Equals("--ship-designer", StringComparison.OrdinalIgnoreCase));
        if (i < 0)
            return;
        _sdOpenAtStart = true;
        ShipDesignStore.ReadOnly = true;
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            _sdStartMode = args[i + 1].ToLowerInvariant();
        var s = Array.FindIndex(args, a => a.Equals("--sd-shot", StringComparison.OrdinalIgnoreCase));
        if (s >= 0 && s + 1 < args.Length && int.TryParse(args[s + 1], out var n))
            _sdShotIn = n;
        var d = Array.FindIndex(args, a => a.Equals("--sd-do", StringComparison.OrdinalIgnoreCase));
        if (d >= 0 && d + 1 < args.Length)
            foreach (var step in args[d + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _sdSteps.Enqueue(step);
    }

    // --sd-do "wait N; mv X Y; dn; up; rdn; rup; mdn; mup; wheel N; key NAME; ctrl NAME; shot NAME; quit": the headless mouse and keys, a step a frame.
    private static readonly Queue<string> _sdSteps = new();
    private static int _sdWait;
    private static string? _sdRelease;

    private static void ShipDesignerStep()
    {
        if (_sdWait > 0)
        {
            _sdWait--;
            return;
        }
        string? step = null;
        if (_sdRelease != null)
            (step, _sdRelease) = (_sdRelease, null);
        else if (!_sdSteps.TryDequeue(out step))
            return;
        var w = step.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var io = Brutal.ImGuiApi.ImGui.GetIO();
        float F(int i) => float.Parse(w[i], System.Globalization.CultureInfo.InvariantCulture);
        switch (w[0])
        {
            case "wait": _sdWait = (int)F(1); break;
            case "mv": io.AddMousePosEvent(F(1), F(2)); break;
            case "dn": io.AddMouseButtonEvent(0, true); break;
            case "up": io.AddMouseButtonEvent(0, false); break;
            case "rdn": io.AddMouseButtonEvent(1, true); break;
            case "rup": io.AddMouseButtonEvent(1, false); break;
            case "mdn": io.AddMouseButtonEvent(2, true); break;
            case "mup": io.AddMouseButtonEvent(2, false); break;
            case "wheel": io.AddMouseWheelEvent(0, F(1)); break;
            case "key" or "ctrl":
                var k = Enum.Parse<Brutal.ImGuiApi.ImGuiKey>(w[1], true);
                if (w[0] == "ctrl")
                    io.AddKeyEvent(Brutal.ImGuiApi.ImGuiKey.LeftCtrl, true);
                io.AddKeyEvent(k, true);
                _sdRelease = "__release " + w[1] + (w[0] == "ctrl" ? " ctrl" : "");
                break;
            case "__release":
                io.AddKeyEvent(Enum.Parse<Brutal.ImGuiApi.ImGuiKey>(w[1], true), false);
                if (w.Length > 2)
                    io.AddKeyEvent(Brutal.ImGuiApi.ImGuiKey.LeftCtrl, false);
                break;
            case "shot":
                _shotFull = true;
                SaveScreenshot("sd_" + w[1]);
                break;
            case "quit": _quit = true; break;
        }
    }

    private static void ShipDesignerShotFrame()
    {
        if (_sdOpenAtStart && _session == null)
        {
            _sdOpenAtStart = false;
            Ui.OpenShipDesigner();
            if (_sdStartMode != null)
                Ui.ShipDesignerMode(_sdStartMode);
        }
        ShipDesignerStep();
        if (_sdShotIn < 0)
            return;
        if (_sdShotIn-- == 0)
        {
            _shotFull = true;
            SaveScreenshot("shipdesigner");
            _quit = true;
        }
    }
}
