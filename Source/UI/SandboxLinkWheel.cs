using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>The linking tool's edit modes (the link wheel's Edit ring).</summary>
public enum LinkMode : byte
{
    Link, // route a new link (or branch off one)
    Delete, // click a link: it goes (and its branches)
    Move, // click a node, then where it goes
    Reroute, // click a link: it goes and a new one starts from the same output
}

/// <summary>
/// The linking tool's second-tier radial (decision 20; docs/sandbox/CONSTRUCTS.md, "Routed links"): with the linking tool
/// out, holding the tool wheel key opens the link wheel instead of the tool wheel (the same <see cref="RadialMenu"/>).
/// Its ring: data, power, pipe, shaft, chute, Edit and Tools. Letting go on a type picks it; resting the pointer on Pipe,
/// Edit or Tools for a moment opens their ring in place: the pipe styles (Auto: what the output carries, or a style to
/// override it; a wrong one is allowed), the edit modes (link, delete, move node, re-route, highlight all), or the tool
/// wheel itself. Nothing allocates per frame.
/// </summary>
public sealed class SandboxLinkWheel
{
    public enum Ring
    {
        Main,
        Pipe,
        Edit,
    }

    private enum MainSlot
    {
        Data,
        Power,
        Pipe,
        Shaft,
        Chute,
        Edit,
        Tools,
        COUNT,
    }

    public const float DwellSeconds = 0.3f; // resting on Pipe, Edit or Tools this long opens its ring

    public readonly RadialMenu Main = new((int)MainSlot.COUNT);
    public readonly RadialMenu Pipes = new(1 + (int)PipeStyle.COUNT);
    public readonly RadialMenu Edits = new(5);

    public Ring Showing { get; private set; }
    public RadialMenu Menu => Showing switch { Ring.Pipe => Pipes, Ring.Edit => Edits, _ => Main };
    public bool IsOpen => Menu.IsOpen;

    // What it picked (the linking tool's state on this PC).
    public RouteType Type = RouteType.Data;
    public byte Style = 255; // a pipe's style: 255 Auto (what the output carries), else the override
    public LinkMode Mode = LinkMode.Link;
    public bool Highlight; // every link drawn bright, through the parts

    /// <summary>Set when the pointer rested on Tools: the caller opens the tool wheel in its place (the key still held).</summary>
    public bool WantsTools;

    private bool _wasHeld;
    private int _dwellOn = -1;
    private float _dwell;

    public static readonly RadialIcon Icons = DrawIcon;

    public SandboxLinkWheel()
    {
        Main.Title = "Links";
        Main.Help = "flick to a type, let go (rest on a ring to open it)";
        SetItem(Main, (int)MainSlot.Data, "Data cable", "signals", 0);
        SetItem(Main, (int)MainSlot.Power, "Power cable", "electricity", 1);
        SetItem(Main, (int)MainSlot.Pipe, "Pipe", "rest: pick a style", 2);
        SetItem(Main, (int)MainSlot.Shaft, "Drive shaft", "torque", 3);
        SetItem(Main, (int)MainSlot.Chute, "Chute", "items", 4);
        SetItem(Main, (int)MainSlot.Edit, "Edit links", "rest: delete, move, re-route", 5);
        SetItem(Main, (int)MainSlot.Tools, "Tools", "rest: the tool wheel", 6);
        Pipes.Title = "Pipe style";
        Pipes.Help = "Auto: what the output carries";
        SetItem(Pipes, 0, "Auto", "from the output", 2);
        SetItem(Pipes, 1, "Fuel pipe", "thin, hazard bands", 10);
        SetItem(Pipes, 2, "Water pipe", "blue, flanges", 11);
        SetItem(Pipes, 3, "Oil pipe", "dark, clamps", 12);
        SetItem(Pipes, 4, "Steam pipe", "lagged: heat, pressure", 13);
        Edits.Title = "Edit links";
        Edits.Help = "flick to a mode, let go";
        SetItem(Edits, 0, "Route links", "click output, nodes, input", 0);
        SetItem(Edits, 1, "Delete", "click a link", 20);
        SetItem(Edits, 2, "Move node", "click a node, then its place", 21);
        SetItem(Edits, 3, "Re-route", "click a link: start it again", 22);
        SetItem(Edits, 4, "Highlight all", "every link, through parts", 23);
    }

    private static void SetItem(RadialMenu m, int i, string label, string hint, int icon)
    {
        m.Items[i].Label = label;
        m.Items[i].Hint = hint;
        m.Items[i].Icon = icon;
    }

    public string ModeName => Mode switch { LinkMode.Delete => "delete links", LinkMode.Move => "move a node", LinkMode.Reroute => "re-route a link", _ => "route links" };

    /// <summary>
    /// One frame (the same shape as the tool wheel's): <paramref name="held"/> the wheel key, <paramref name="delta"/> the
    /// mouse movement, <paramref name="dt"/> the frame's seconds. Returns true while it is open.
    /// </summary>
    public bool Step(bool held, Vector2 delta, float dt, bool blocked)
    {
        var pressed = held && !_wasHeld;
        _wasHeld = held;
        WantsTools = false;
        if (blocked)
        {
            Close();
            return false;
        }
        Main.Current = (int)(Type switch { RouteType.Power => MainSlot.Power, RouteType.Pipe => MainSlot.Pipe, RouteType.Shaft => MainSlot.Shaft, RouteType.Chute => MainSlot.Chute, _ => MainSlot.Data });
        Pipes.Current = Style == 255 ? 0 : Style + 1;
        Edits.Current = (int)Mode;
        if (!Menu.IsOpen)
        {
            if (!pressed)
                return false;
            Showing = Ring.Main;
            Main.Open();
            _dwellOn = -1;
            _dwell = 0;
            return true;
        }
        if (held)
        {
            Menu.Move(delta);
            if (Showing == Ring.Main)
            {
                var on = Main.Hovered;
                if (on != _dwellOn)
                {
                    _dwellOn = on;
                    _dwell = 0;
                }
                else if (on is (int)MainSlot.Pipe or (int)MainSlot.Edit or (int)MainSlot.Tools)
                {
                    _dwell += dt;
                    if (_dwell >= DwellSeconds)
                    {
                        Main.Close();
                        if (on == (int)MainSlot.Tools)
                        {
                            WantsTools = true;
                            return false;
                        }
                        if (on == (int)MainSlot.Pipe)
                            Type = RouteType.Pipe;
                        Showing = on == (int)MainSlot.Pipe ? Ring.Pipe : Ring.Edit;
                        Menu.Open();
                    }
                }
            }
            return true;
        }
        var ring = Showing;
        var pick = Menu.Release();
        Showing = Ring.Main;
        if (pick < 0)
            return false;
        switch (ring)
        {
            case Ring.Main:
                switch ((MainSlot)pick)
                {
                    case MainSlot.Data: Pick(RouteType.Data); break;
                    case MainSlot.Power: Pick(RouteType.Power); break;
                    case MainSlot.Pipe: Pick(RouteType.Pipe); break;
                    case MainSlot.Shaft: Pick(RouteType.Shaft); break;
                    case MainSlot.Chute: Pick(RouteType.Chute); break;
                    case MainSlot.Tools: WantsTools = true; break;
                }
                break;
            case Ring.Pipe:
                Type = RouteType.Pipe;
                Style = pick == 0 ? (byte)255 : (byte)(pick - 1);
                Mode = LinkMode.Link;
                break;
            case Ring.Edit:
                if (pick == 4)
                    Highlight = !Highlight;
                else
                    Mode = (LinkMode)pick;
                break;
        }
        return false;
    }

    private void Pick(RouteType t)
    {
        Type = t;
        Mode = LinkMode.Link;
    }

    public void Close()
    {
        Main.Close();
        Pipes.Close();
        Edits.Close();
        Showing = Ring.Main;
    }

    public void Draw(float scale)
    {
        if (!Menu.IsOpen)
            return;
        var size = ImGui.GetIO().DisplaySize;
        Menu.Draw(ImGui.GetForegroundDrawList(), new float2(size.X * 0.5f, size.Y * 0.5f), scale, Icons);
    }

    // ---- Icons: each link type's profile, the pipe styles, the edit modes ------------------------------------------

    private static void DrawIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        float2 P(float x, float y) => new(c.X + x * r, c.Y + y * r);
        void Line(float x0, float y0, float x1, float y1, float w, ImColor8 colour)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            ImDrawListExtensions.AddLine(dl, a, b, colour, w * r);
        }
        void Box(float x0, float y0, float x1, float y1, ImColor8 colour)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            ImDrawListExtensions.AddRectFilled(dl, a, b, colour, 0.04f * r, ImDrawFlags.None);
        }
        var accent = new ImColor8(240, 200, 60, 255);
        switch (icon)
        {
            case 0: // data: a thin cable with a plug
                Line(-0.8f, 0.4f, 0.5f, -0.4f, 0.1f, col);
                Box(0.4f, -0.6f, 0.8f, -0.2f, col);
                break;
            case 1: // power: a thick cable and a bolt
                Line(-0.8f, 0.5f, 0.3f, -0.2f, 0.2f, col);
                Line(0.3f, -0.8f, 0.05f, -0.2f, 0.1f, accent);
                Line(0.05f, -0.2f, 0.5f, -0.2f, 0.1f, accent);
                Line(0.5f, -0.2f, 0.25f, 0.4f, 0.1f, accent);
                break;
            case 2: // pipe: an elbow with flanges
            case 10:
            case 11:
            case 12:
            case 13:
            {
                var pc = icon switch { 10 => accent, 11 => new ImColor8(80, 140, 230, 255), 12 => new ImColor8(70, 60, 50, 255), 13 => new ImColor8(220, 215, 200, 255), _ => col };
                Line(-0.8f, 0.3f, 0.2f, 0.3f, 0.28f, pc);
                Line(0.2f, 0.3f, 0.2f, -0.8f, 0.28f, pc);
                Box(-0.5f, 0.05f, -0.4f, 0.55f, col);
                Box(-0.05f, -0.5f, 0.45f, -0.4f, col);
                break;
            }
            case 3: // shaft: a rod with a U joint
                Line(-0.85f, 0.45f, -0.1f, 0.05f, 0.14f, col);
                Line(0.1f, -0.05f, 0.85f, -0.45f, 0.14f, col);
                Box(-0.15f, -0.15f, 0.15f, 0.15f, accent);
                break;
            case 4: // chute: a square duct and a crate
                Box(-0.8f, -0.25f, 0.4f, 0.25f, col);
                Box(-0.7f, -0.15f, 0.3f, 0.15f, bg);
                Box(0.45f, -0.4f, 0.85f, 0.4f, col);
                break;
            case 5: // edit: a pencil
                Line(-0.6f, 0.6f, 0.5f, -0.5f, 0.22f, col);
                Line(0.5f, -0.5f, 0.75f, -0.75f, 0.1f, accent);
                break;
            case 6: // tools: a small ring of dots
                for (var i = 0; i < 6; i++)
                {
                    var a = i * MathF.Tau / 6;
                    var p = P(MathF.Sin(a) * 0.55f, -MathF.Cos(a) * 0.55f);
                    ImDrawListExtensions.AddCircleFilled(dl, p, 0.15f * r, col, 10);
                }
                break;
            case 20: // delete: a cable cut by a cross
                Line(-0.8f, 0f, 0.8f, 0f, 0.12f, col);
                Line(-0.35f, -0.4f, 0.35f, 0.4f, 0.12f, new ImColor8(230, 80, 70, 255));
                Line(-0.35f, 0.4f, 0.35f, -0.4f, 0.12f, new ImColor8(230, 80, 70, 255));
                break;
            case 21: // move: a node with arrows
                Box(-0.18f, -0.18f, 0.18f, 0.18f, accent);
                Line(-0.8f, 0f, 0.8f, 0f, 0.07f, col);
                Line(0f, -0.8f, 0f, 0.8f, 0.07f, col);
                break;
            case 22: // re-route: a curved arrow
            {
                var o = P(0, 0);
                ImDrawListExtensions.PathClear(dl);
                ImDrawListExtensions.PathArcTo(dl, in o, 0.55f * r, -MathF.PI * 0.9f, MathF.PI * 0.4f, 14);
                ImDrawListExtensions.PathStroke(dl, col, ImDrawFlags.None, 0.12f * r);
                Line(0.2f, 0.55f, 0.5f, 0.35f, 0.12f, accent);
                break;
            }
            default: // highlight: a bright cable over a box
                Box(-0.6f, -0.6f, 0.6f, 0.6f, bg);
                Line(-0.85f, 0.5f, 0.85f, -0.5f, 0.16f, accent);
                break;
        }
    }
}
