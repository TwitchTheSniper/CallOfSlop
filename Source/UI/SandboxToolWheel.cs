using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the tool wheel (decision 17; docs/sandbox/CONSTRUCTS.md, "The tool wheel"). Hold the tool wheel key (the
/// middle mouse button by default, <see cref="GameAction.ToolWheel"/>), flick the mouse towards a tool and let go to take
/// it out; letting go in the middle cancels. The view is frozen while it is open and you keep walking. By default nine segments,
/// clockwise from the top: the wrench, the physics gun (it took the hoist's place), the track tool, the welder, the configure tool, hands (the guns on
/// its outer ring), the remover (board item rt01: it took the scanner's place), the ground drill, the linking tool and the paint tool (board item pp01). A slot with no tool in it is greyed out as "coming later" (picking it says so). Taking
/// a tool out goes through the same request as the number keys (3 / 4 / 5, still the quick favourites), so the server
/// decides and multiplayer is unchanged. Not in a seat, and it closes (without choosing) on death, a menu, the chat, the
/// parts menu or the match ending.
/// <para>
/// The wheel is the player's (UiToolWheel.cs; Settings.ToolWheel): which tools sit on it and in what order. <see cref="Slot"/>
/// names a tool and is the default wheel's order; <see cref="Menu"/> holds what is on the wheel now, made again when that
/// changes, so its segment numbers are the slots only on the default wheel: <see cref="SlotAt"/> turns one into the other.
/// </para>
/// <para>
/// A tool with options (<see cref="IToolOptions"/>, given with <see cref="SetOptions"/>: the linking tool's link types and
/// edit modes first, the welder's and physics gun's modes later) shows them on an outer ring while it is pointed at
/// (<see cref="RadialMenu"/>, board item dev-wheel-outer-ring-wo01): letting go on an option takes the tool out with it,
/// letting go on the tool itself takes it out with its last-used option.
/// </para>
/// </summary>
public sealed class SandboxToolWheel
{
    public enum Slot
    {
        PhysicsGun, // (just right of the top; it lifts and moves pieces, decision 19)
        Tracks,
        Welder,
        Configure,
        Hands,
        Remover, // (SW: where the scanner was going to be; board item rt01)
        GroundDrill, // (between the remover and the linking tool)
        Linker,
        Paint, // (NW: where the hand drill was going to be)
        COUNT,
    }

    /// <summary>The wheel as it is now: one segment for each tool on it.</summary>
    public RadialMenu Menu { get; private set; } = new((int)Slot.COUNT);

    private readonly RadialMenu.Item[] _items = new RadialMenu.Item[(int)Slot.COUNT]; // every tool's item, by slot, on the wheel or not
    private int[] _layout = DefaultLayout();
    private int _layoutStamp;
    private string _help = "flick to a tool, let go";

    /// <summary>The tool on segment <paramref name="segment"/>.</summary>
    public int SlotAt(int segment) => _layout[segment];

    /// <summary>A tool's name, on the wheel or not.</summary>
    public string ItemLabel(int slot) => _items[slot].Label;

    /// <summary>The tools on the wheel, clockwise from the top.</summary>
    public ReadOnlySpan<int> Layout => _layout;

    /// <summary>The icon drawer, made once (a method group would make a new delegate on every call).</summary>
    public static readonly RadialIcon Icons = DrawIcon;

    private bool _wasHeld;
    private long _openedAt;

    /// <summary>The wheel was opened and let go at once without choosing (a tap of the key): set for one read by the owner, then cleared.</summary>
    public bool Tapped;
    private long _keysKey = long.MinValue;
    private readonly IToolOptions?[] _options = new IToolOptions?[(int)Slot.COUNT];
    private readonly SandboxHandsRing _handsRing = new();

    /// <summary>Gives a tool its options (its outer ring); null takes them away.</summary>
    public void SetOptions(Slot s, IToolOptions? options)
    {
        _options[(int)s] = options;
        var it = _items[(int)s];
        it.Options = options?.Options;
        it.OptionIcons = options?.Icons;
        it.OptionCurrent = options?.CurrentOption ?? -1;
        it.OptionShift = options?.OptionShift ?? 0;
    }

    public IToolOptions? OptionsOf(Slot s) => _options[(int)s];

    public SandboxToolWheel()
    {
        Array.Copy(Menu.Items, _items, _items.Length);
        Menu.Title = "Tools";
        Menu.Help = _help;
        Set(Slot.Welder, "Attach tool", "parts, pieces, ropes, springs");
        Set(Slot.Linker, "Linking tool", "links");
        Set(Slot.Hands, "Hands", "");
        Set(Slot.PhysicsGun, "Physics gun", "grab, freeze");
        Set(Slot.Configure, "Configure tool", "settings");
        Set(Slot.Remover, "Remover", "delete");
        Set(Slot.Paint, "Paint tool", "colour");
        Set(Slot.GroundDrill, "Ground drill", "dig, fill");
        Set(Slot.Tracks, "Track tool", "wheels, loop");
        SetOptions(Slot.Hands, _handsRing);
        SetKeys(null);
    }

    private void Set(Slot s, string label, string hint, bool off = false)
    {
        var it = _items[(int)s];
        it.Label = label;
        it.Hint = hint;
        it.Enabled = !off;
        it.Icon = (int)s;
        it.Note = off ? "coming later" : "";
    }

    /// <summary>The tool a slot takes out (the guns are on the hands slot's outer ring), or null for one that isn't in the game yet.</summary>
    public static ConstructTool? ToolOf(int slot) =>
        (Slot)slot switch
        {
            Slot.Welder => ConstructTool.Welder,
            Slot.Linker => ConstructTool.Linker,
            Slot.Configure => ConstructTool.Configure,
            Slot.PhysicsGun => ConstructTool.PhysicsGun,
            Slot.Paint => ConstructTool.Paint,
            Slot.Remover => ConstructTool.Remover,
            Slot.GroundDrill => ConstructTool.GroundDrill,
            Slot.Tracks => ConstructTool.Tracks,
            Slot.Hands => ConstructTool.Hands,
            _ => null,
        };

    public static int SlotOf(ConstructTool t) =>
        (int)(t switch
        {
            ConstructTool.Welder => Slot.Welder,
            ConstructTool.Linker => Slot.Linker,
            ConstructTool.Configure => Slot.Configure,
            ConstructTool.PhysicsGun => Slot.PhysicsGun,
            ConstructTool.Paint => Slot.Paint,
            ConstructTool.Remover => Slot.Remover,
            ConstructTool.GroundDrill => Slot.GroundDrill,
            ConstructTool.Tracks => Slot.Tracks,
            _ => Slot.Hands,
        });

    public static int[] DefaultLayout()
    {
        var all = new int[(int)Slot.COUNT];
        for (var i = 0; i < all.Length; i++)
            all[i] = i;
        return all;
    }

    /// <summary>Is Hands at the bottom of a wheel of this many? An even wheel's segment n / 2 hangs straight down, and an odd one's does too (RadialMenu.Tilt).</summary>
    public static bool HandsSouth(List<int> on) => on.IndexOf((int)Slot.Hands) is var h and >= 0 && h == on.Count / 2;

    /// <summary>Turns the wheel, keeping its order, so Hands is at the bottom.</summary>
    public static void TurnHandsSouth(List<int> on)
    {
        var h = on.IndexOf((int)Slot.Hands);
        if (h < 0)
            return;
        var was = on.ToArray();
        for (var i = 0; i < was.Length; i++)
            on[(i + on.Count / 2 - h + was.Length) % was.Length] = was[i];
    }

    /// <summary>
    /// The wheel a player's saved lists make: <paramref name="on"/> in order (null: the default wheel), names that aren't tools
    /// dropped and repeats ignored, and a tool in neither list (new since they saved) added, so a new tool shows up on the wheel.
    /// </summary>
    public static List<int> Resolve(IReadOnlyList<string>? on, IReadOnlyList<string> off)
    {
        if (on == null)
            return DefaultLayout().ToList();
        var list = new List<int>();
        foreach (var name in on)
            if (Enum.TryParse<Slot>(name, out var s) && s is >= 0 and < Slot.COUNT && !int.TryParse(name, out _) && !list.Contains((int)s))
                list.Add((int)s);
        var south = HandsSouth(list);
        for (var s = Slot.PhysicsGun; s < Slot.COUNT; s++)
            if (!list.Contains((int)s) && !off.Contains(s.ToString()))
                list.Add((int)s);
        if (south)
            TurnHandsSouth(list);
        return list;
    }

    /// <summary>Puts these tools on the wheel (a new menu of that many segments; every tool keeps its item, so options and hints stay).</summary>
    public void SetLayout(ReadOnlySpan<int> slots)
    {
        if (Menu.IsOpen)
            Menu.Close();
        _layout = slots.ToArray();
        var menu = new RadialMenu(_layout.Length) { Title = "Tools", Help = _help };
        for (var i = 0; i < _layout.Length; i++)
            menu.Items[i] = _items[_layout[i]];
        Menu = menu;
    }

    /// <summary>The favourite keys in the hints ("key 3: bolted parts"), remade only when the binds change.</summary>
    public void SetKeys(Settings? s)
    {
        if (s != null && s.ToolWheelStamp != _layoutStamp)
        {
            _layoutStamp = s.ToolWheelStamp;
            SetLayout(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Resolve(s.ToolWheel, s.ToolWheelOff)));
        }
        int B(GameAction a) => s != null ? s.Bind(a) : Keybinds.Defaults[(int)a];
        var key = ((long)B(GameAction.Weapon3) * 31 + B(GameAction.Streak1)) * 31 + B(GameAction.Streak2) * 7L + B(GameAction.PrimaryWeapon) + B(GameAction.SecondaryWeapon) * 131L + B(GameAction.Reload) * 1009L;
        if (key == _keysKey)
            return;
        _keysKey = key;
        _items[(int)Slot.Welder].Hint = $"{Keybinds.NameOf(s, GameAction.Streak1)}: parts, pieces, ropes, springs";
        _items[(int)Slot.Linker].Hint = $"{Keybinds.NameOf(s, GameAction.Streak2)}: links";
        _items[(int)Slot.Hands].Hint = "pick up, drop";
        _help = $"flick to a tool, let go, {Keybinds.NameOf(s, GameAction.Reload)}: edit the wheel";
        Menu.Help = _help;
        _handsRing.SetKeys(Keybinds.NameOf(s, GameAction.PrimaryWeapon), Keybinds.NameOf(s, GameAction.SecondaryWeapon));
    }

    /// <summary>
    /// One frame of the wheel: <paramref name="held"/> is the wheel key, <paramref name="delta"/> this frame's mouse
    /// movement, <paramref name="blocked"/> true while a menu, the chat or the parts menu has the screen (or the mouse is
    /// free). Opens on the key going down, follows the mouse while held, and on release takes out the tool picked (the
    /// number keys' request) or cancels. Returns true while it is open (the caller freezes the view and keeps the mouse
    /// buttons from the gun and tools).
    /// </summary>
    public bool Step(Game g, bool held, Vector2 delta, bool blocked, float dt = 1f / 60)
    {
        var pressed = held && !_wasHeld;
        var released = !held && _wasHeld;
        _wasHeld = held;
        var me = g.Local;
        var ok = !blocked && g.SandboxOn && g.Phase == MatchPhase.Playing && me is { Alive: true };
        if (ok && g.SandboxHands.Hand(me!.Id).Seated)
        {
            ok = false;
            if (pressed)
                g.SandboxSay("get out of the seat first");
        }
        if (!ok)
        {
            if (Menu.IsOpen)
                Menu.Close();
            return false;
        }
        ref var h = ref g.SandboxHands.Hand(me!.Id);
        if (_layout.Length == 0)
        {
            // Nothing on the wheel: a tap of the key still works; held, it says where the wheel is set.
            if (pressed)
                _openedAt = Environment.TickCount64;
            else if (released && Environment.TickCount64 - _openedAt < 220)
                Tapped = true;
            else if (released)
                g.SandboxSay(EmptyText);
            return false;
        }
        Menu.Current = Array.IndexOf(_layout, SlotOf(h.Tool));
        _handsRing.Shown(h.Tool, g.ActiveSlot);
        for (var i = 0; i < _options.Length; i++)
            if (_options[i] is { } o)
                _items[i].OptionCurrent = o.CurrentOption;
        if (!Menu.IsOpen)
        {
            if (!pressed)
                return false;
            Menu.Open();
            _openedAt = Environment.TickCount64;
            return true;
        }
        if (held)
        {
            Menu.Move(delta, dt);
            return true;
        }
        var pick = Menu.Release();
        var option = Menu.PickedOption;
        if (pick < 0)
        {
            Tapped = Environment.TickCount64 - _openedAt < 220; // a quick tap of the key (snappy building: it picks the aimed part's kind)
            return false; // let go in the middle: a cancel
        }
        pick = _layout[pick]; // (from here on the tool, wherever it sits)
        if (ToolOf(pick) is not { } tool)
        {
            g.SandboxSay(pick switch
            {
                (int)Slot.Configure => "the configure tool is coming later",
                _ => "that tool is coming later",
            });
            return false;
        }
        if (option >= 0 && _options[pick] is { } opts)
        {
            if (Menu.PickedSub >= 0)
                opts.ChooseSub(option, Menu.PickedSub); // (a third-tier pick)
            else
                opts.Choose(option); // (on the option itself: its last-used sub-option stays)
        }
        // (on the tool itself: its last-used option stays)
        if (pick == (int)Slot.Hands && _handsRing.GunOf(option) is { } gun)
        {
            // A gun on the hands' ring: as its number key does, the weapon switch here and the hands put away.
            tool = ConstructTool.None;
            g.SwitchTo(gun, Brutal.GlfwApi.Glfw.Time);
        }
        if (tool != h.Tool)
            g.SandboxSend(CActKind.Tool, part: (byte)tool);
        return false;
    }

    public const string EmptyText = "the tool wheel is empty: Settings > Tool wheel, or Tab > Tools";

    /// <summary>Draws the wheel (open only) around the screen centre.</summary>
    public void Draw(float scale)
    {
        if (!Menu.IsOpen)
            return;
        var size = ImGui.GetIO().DisplaySize;
        Menu.Draw(ImGui.GetForegroundDrawList(), new float2(size.X * 0.5f, size.Y * 0.5f), scale, Icons);
    }

    // ---- The icons: simple shapes from lines and boxes, in the HUD's flat style ------------------------------------

    private static void DrawIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        float2 P(float x, float y) => new(c.X + x * r, c.Y + y * r);
        void Line(float x0, float y0, float x1, float y1, float w)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            ImDrawListExtensions.AddLine(dl, a, b, col, w * r);
        }
        void Box(float x0, float y0, float x1, float y1, ImColor8 colour, float round = 0.05f)
        {
            var a = P(x0, y0);
            var b = P(x1, y1);
            ImDrawListExtensions.AddRectFilled(dl, a, b, colour, round * r, ImDrawFlags.None);
        }
        void Disc(float x, float y, float rad, ImColor8 colour)
        {
            var p = P(x, y);
            ImDrawListExtensions.AddCircleFilled(dl, p, rad * r, colour, 14);
        }
        switch ((Slot)icon)
        {
            case Slot.Welder: // a torch held at a slant: hose end, grip, valve, nozzle and sparks at the tip
                Line(-0.8f, 0.8f, -0.55f, 0.55f, 0.1f);
                Line(-0.55f, 0.55f, 0.05f, -0.05f, 0.24f);
                Box(-0.3f, 0.05f, -0.05f, 0.3f, bg, 0.02f);
                Disc(-0.17f, 0.17f, 0.1f, col);
                Line(0.05f, -0.05f, 0.42f, -0.42f, 0.09f);
                Line(0.55f, -0.55f, 0.95f, -0.62f, 0.06f);
                Line(0.55f, -0.55f, 0.62f, -0.95f, 0.06f);
                Line(0.55f, -0.55f, 0.88f, -0.88f, 0.06f);
                Line(0.55f, -0.55f, 0.92f, -0.3f, 0.05f);
                Line(0.55f, -0.55f, 0.3f, -0.92f, 0.05f);
                break;
            case Slot.Linker: // two terminals and a wire between them
            {
                Box(-0.85f, 0.25f, -0.45f, 0.65f, col);
                Box(0.45f, -0.65f, 0.85f, -0.25f, col);
                var a = P(-0.45f, 0.45f);
                var c1 = P(0.1f, 0.6f);
                var c2 = P(-0.1f, -0.6f);
                var b = P(0.45f, -0.45f);
                ImDrawListExtensions.PathClear(dl);
                ImDrawListExtensions.PathLineTo(dl, in a);
                ImDrawListExtensions.PathBezierCubicCurveTo(dl, in c1, in c2, in b, 12);
                ImDrawListExtensions.PathStroke(dl, col, ImDrawFlags.None, 0.12f * r);
                break;
            }
            case Slot.Hands:
                SandboxHandsRing.DrawHand(dl, c, r, col);
                break;
            case Slot.Tracks: // a band round three wheels
                Line(-0.72f, 0.5f, 0.72f, 0.5f, 0.14f);
                Line(-0.72f, 0.5f, 0f, -0.55f, 0.14f);
                Line(0.72f, 0.5f, 0f, -0.55f, 0.14f);
                Disc(-0.5f, 0.3f, 0.2f, col);
                Disc(0.5f, 0.3f, 0.2f, col);
                Disc(0f, -0.28f, 0.2f, col);
                break;
            case Slot.PhysicsGun: // a projector low on the left, its beam curving up onto a cube
            {
                Box(-0.9f, 0.35f, -0.35f, 0.65f, col);
                Box(-0.75f, 0.65f, -0.55f, 0.9f, col);
                var a = P(-0.32f, 0.5f);
                var c1 = P(0.15f, 0.45f);
                var c2 = P(0.35f, 0.1f);
                var b = P(0.38f, -0.2f);
                ImDrawListExtensions.PathClear(dl);
                ImDrawListExtensions.PathLineTo(dl, in a);
                ImDrawListExtensions.PathBezierCubicCurveTo(dl, in c1, in c2, in b, 12);
                ImDrawListExtensions.PathStroke(dl, col, ImDrawFlags.None, 0.1f * r);
                Box(0.12f, -0.82f, 0.68f, -0.26f, col);
                Box(0.24f, -0.7f, 0.56f, -0.38f, bg, 0f);
                break;
            }
            case Slot.Configure: // three sliders
                for (var i = -1; i <= 1; i++)
                {
                    Line(-0.75f, i * 0.5f, 0.75f, i * 0.5f, 0.08f);
                    Box(i * 0.35f - 0.12f, i * 0.5f - 0.17f, i * 0.35f + 0.12f, i * 0.5f + 0.17f, col);
                }
                break;
            case Slot.GroundDrill: // a drill's body and bit going into a heap of ground
                Box(-0.8f, -0.75f, 0.1f, -0.3f, col, 0.08f);
                Box(-0.6f, -0.3f, -0.25f, 0.3f, col, 0.04f);
                Line(0.1f, -0.52f, 0.5f, -0.1f, 0.14f);
                Line(0.5f, -0.1f, 0.7f, 0.15f, 0.08f);
                Box(-0.1f, 0.5f, 0.95f, 0.85f, col, 0.08f);
                Box(0.25f, 0.28f, 0.7f, 0.5f, col, 0.04f);
                break;
            case Slot.Remover: // a bin (lid, handle, body, two slots) with a cross struck over a box beside it
            {
                Box(-0.75f, -0.55f, 0.05f, -0.4f, col, 0.04f);
                Box(-0.5f, -0.7f, -0.2f, -0.55f, col, 0.02f);
                Box(-0.65f, -0.35f, -0.05f, 0.85f, col, 0.04f);
                Box(-0.5f, -0.2f, -0.42f, 0.7f, bg, 0f);
                Box(-0.28f, -0.2f, -0.2f, 0.7f, bg, 0f);
                Line(0.2f, -0.05f, 0.85f, 0.6f, 0.12f);
                Line(0.85f, -0.05f, 0.2f, 0.6f, 0.12f);
                break;
            }
            default: // the paint tool: a spray can (body, shoulder, nozzle cap) and a puff of spray dots
            {
                Box(-0.55f, -0.35f, 0.15f, 0.85f, col, 0.1f);
                Box(-0.4f, -0.6f, 0f, -0.35f, col, 0.04f);
                Box(-0.28f, -0.85f, -0.12f, -0.6f, col, 0.03f);
                Box(-0.4f, 0.2f, 0f, 0.4f, bg, 0f); // (the label)
                Disc(0.45f, -0.75f, 0.09f, col);
                Disc(0.7f, -0.55f, 0.08f, col);
                Disc(0.62f, -0.88f, 0.07f, col);
                Disc(0.9f, -0.72f, 0.06f, col);
                Disc(0.82f, -0.36f, 0.06f, col);
                break;
            }
        }
    }
}

/// <summary>
/// A tool's options on the tool wheel's outer ring (its modes: the linking tool's edit modes, later the
/// welder's and the physics gun's). The items are made once; <see cref="CurrentOption"/> is the last-used one (marked);
/// <see cref="Choose"/> applies a pick. Nothing here may allocate (it is asked every frame the wheel is open).
/// </summary>
public interface IToolOptions
{
    RadialMenu.Item[] Options { get; }
    RadialIcon? Icons { get; }
    int CurrentOption { get; }
    void Choose(int option);

    /// <summary>Radians the whole arc turns off the tool's direction (an even arc, to put its default in the middle).</summary>
    float OptionShift => 0;

    /// <summary>
    /// A pick on the third tier: sub-option <paramref name="sub"/> of option <paramref name="option"/> (the option's own
    /// <see cref="RadialMenu.Item.Options"/>, drawn as a partial arc). <see cref="Choose"/> is called instead when the wheel
    /// is let go on the option itself (it keeps its last sub-option). Optional: the default ignores it.
    /// </summary>
    void ChooseSub(int option, int sub) => Choose(option);
}
