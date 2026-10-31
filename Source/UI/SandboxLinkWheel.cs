using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>The linking tool's edit modes (options on the tool wheel's outer ring round the linking tool).</summary>
public enum LinkMode : byte
{
    Link, // route a new link (or branch off one)
    Delete, // click a link: it goes (and its branches)
    Move, // click a node, then where it goes
    Reroute, // click a link: it goes and a new one starts from the same output
}

/// <summary>
/// The linking tool's options (decision 20; docs/sandbox/CONSTRUCTS.md, "Routed links"): the outer ring the tool wheel shows
/// round the linking tool (board item dev-wheel-outer-ring-wo01). Board item dev-link-auto-type-at01: there is NO link type
/// choice any more; the type comes from the port the player picks on a device (data, power, pipe, shaft or chute). The ring
/// holds the modes only: "Route links" (the normal mode), delete links, move node, re-route, and "highlight all" (a toggle).
/// The wheel's third-tier mechanism stays for other tools. The state is this PC's only (nothing on the wire). Nothing allocates per frame.
/// </summary>
public sealed class SandboxLinkWheel : IToolOptions
{
    /// <summary>The outer ring's options, in order clockwise.</summary>
    public enum Option
    {
        Route,
        Delete,
        MoveNode,
        Reroute,
        Highlight,
        COUNT,
    }

    private readonly RadialMenu.Item[] _options = new RadialMenu.Item[(int)Option.COUNT];
    public RadialMenu.Item[] Options => _options;

    RadialIcon? IToolOptions.Icons => Icons;

    // What it picked (the linking tool's state on this PC).
    public LinkMode Mode = LinkMode.Link;
    public bool Highlight; // every link drawn bright, through the parts

    /// <summary>The icons (the port picker uses the link types' too: 0 data, 1 power, 2 pipe, 3 shaft, 4 chute), made once.</summary>
    public static readonly RadialIcon Icons = DrawIcon;

    public SandboxLinkWheel()
    {
        Set(Option.Route, "Route links", "click a device, pick a port", 24);
        Set(Option.Delete, "Delete links", "click a link", 20);
        Set(Option.MoveNode, "Move node", "click a node, then its place", 21);
        Set(Option.Reroute, "Re-route", "click a link: start it again", 22);
        Set(Option.Highlight, "Highlight all", "every link, through parts (on / off)", 23);
    }

    private void Set(Option o, string label, string hint, int icon) =>
        _options[(int)o] = new RadialMenu.Item { Label = label, Hint = hint, Icon = icon };

    public string ModeName => Mode switch { LinkMode.Delete => "delete links", LinkMode.Move => "move a node", LinkMode.Reroute => "re-route a link", _ => "route links" };

    /// <summary>The option the linking tool has now (the mode); "highlight all" shows as a toggle.</summary>
    public int CurrentOption
    {
        get
        {
            _options[(int)Option.Highlight].Marked = Highlight;
            return (int)(Mode switch
            {
                LinkMode.Delete => Option.Delete,
                LinkMode.Move => Option.MoveNode,
                LinkMode.Reroute => Option.Reroute,
                _ => Option.Route,
            });
        }
    }

    /// <summary>Applies an outer ring pick: a mode; "highlight all" toggles.</summary>
    public void Choose(int option)
    {
        switch ((Option)option)
        {
            case Option.Route: Mode = LinkMode.Link; break;
            case Option.Delete: Mode = LinkMode.Delete; break;
            case Option.MoveNode: Mode = LinkMode.Move; break;
            case Option.Reroute: Mode = LinkMode.Reroute; break;
            case Option.Highlight: Highlight = !Highlight; break;
        }
    }

    /// <summary>The colour of a link type's ports in the picker and the markers (0..1 rgb).</summary>
    public static System.Numerics.Vector3 TypeColour(RouteType t) => t switch
    {
        RouteType.Data => new(0.45f, 0.8f, 1f),
        RouteType.Power => new(1f, 0.85f, 0.3f),
        RouteType.Pipe => new(0.4f, 1f, 0.6f),
        RouteType.Shaft => new(1f, 0.6f, 0.3f),
        _ => new(0.8f, 0.6f, 1f),
    };

    // ---- Icons: each link type's profile, the edit modes ------------------------------------------

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
            {
                Line(-0.8f, 0.3f, 0.2f, 0.3f, 0.28f, col);
                Line(0.2f, 0.3f, 0.2f, -0.8f, 0.28f, col);
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
            case 24: // route: a cable between two plugs
                Line(-0.55f, 0.35f, 0.55f, -0.35f, 0.12f, col);
                Box(-0.85f, 0.15f, -0.5f, 0.55f, accent);
                Box(0.5f, -0.55f, 0.85f, -0.15f, accent);
                break;
            case 5: // edit: a pencil
                Line(-0.6f, 0.6f, 0.5f, -0.5f, 0.22f, col);
                Line(0.5f, -0.5f, 0.75f, -0.75f, 0.1f, accent);
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
