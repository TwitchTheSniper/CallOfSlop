using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the tool wheel (decision 17; docs/sandbox/CONSTRUCTS.md, "The tool wheel"). Hold the tool wheel key (the
/// middle mouse button by default, <see cref="GameAction.ToolWheel"/>), flick the mouse towards a tool and let go to take
/// it out; letting go in the middle cancels. The view is frozen while it is open and you keep walking. Eight segments,
/// clockwise from the top: the wrench (N), the hoist (NE), the welder (E), the configure tool (SE), hands / gun (S: out of
/// build mode), the scanner (SW), the linking tool (W) and the hand drill (NW). The four tools that exist sit on the
/// compass points, the four still to come on the diagonals, greyed out as "coming later" (picking one says so). Taking
/// a tool out goes through the same request as the number keys (3 / 4 / 5, still the quick favourites), so the server
/// decides and multiplayer is unchanged. Not in a seat, and it closes (without choosing) on death, a menu, the chat, the
/// parts menu or the match ending.
/// </summary>
public sealed class SandboxToolWheel
{
    public enum Slot
    {
        Wrench,
        Hoist,
        Welder,
        Configure,
        Hands,
        Scanner,
        Linker,
        Drill,
        COUNT,
    }

    public readonly RadialMenu Menu = new((int)Slot.COUNT);

    /// <summary>The icon drawer, made once (a method group would make a new delegate on every call).</summary>
    public static readonly RadialIcon Icons = DrawIcon;

    private bool _wasHeld;
    private long _keysKey = long.MinValue;

    public SandboxToolWheel()
    {
        Menu.Title = "Tools";
        Menu.Help = "flick to a tool, let go";
        Set(Slot.Wrench, "Wrench", "bolted parts");
        Set(Slot.Welder, "Welder", "frames, panels");
        Set(Slot.Linker, "Linking tool", "wires, settings");
        Set(Slot.Hands, "Hands / gun", "out of build mode");
        Set(Slot.Hoist, "Hoist", "", off: true);
        Set(Slot.Configure, "Configure tool", "part settings");
        Set(Slot.Scanner, "Scanner", "", off: true);
        Set(Slot.Drill, "Hand drill", "", off: true);
        SetKeys(null);
    }

    private void Set(Slot s, string label, string hint, bool off = false)
    {
        var it = Menu.Items[(int)s];
        it.Label = label;
        it.Hint = hint;
        it.Enabled = !off;
        it.Icon = (int)s;
        it.Note = off ? "coming later" : "";
    }

    /// <summary>The tool a slot takes out (None for hands / gun), or null for one that isn't in the game yet.</summary>
    public static ConstructTool? ToolOf(int slot) =>
        (Slot)slot switch
        {
            Slot.Wrench => ConstructTool.Wrench,
            Slot.Welder => ConstructTool.Welder,
            Slot.Linker => ConstructTool.Linker,
            Slot.Configure => ConstructTool.Configure,
            Slot.Hands => ConstructTool.None,
            _ => null,
        };

    public static int SlotOf(ConstructTool t) =>
        (int)(t switch
        {
            ConstructTool.Wrench => Slot.Wrench,
            ConstructTool.Welder => Slot.Welder,
            ConstructTool.Linker => Slot.Linker,
            ConstructTool.Configure => Slot.Configure,
            _ => Slot.Hands,
        });

    /// <summary>The favourite keys in the hints ("key 3: bolted parts"), remade only when the binds change.</summary>
    public void SetKeys(Settings? s)
    {
        int B(GameAction a) => s != null ? s.Bind(a) : Keybinds.Defaults[(int)a];
        var key = ((long)B(GameAction.Weapon3) * 31 + B(GameAction.Streak1)) * 31 + B(GameAction.Streak2) * 7L + B(GameAction.PrimaryWeapon);
        if (key == _keysKey)
            return;
        _keysKey = key;
        Menu.Items[(int)Slot.Wrench].Hint = $"key {Keybinds.NameOf(s, GameAction.Weapon3)}: bolted parts";
        Menu.Items[(int)Slot.Welder].Hint = $"key {Keybinds.NameOf(s, GameAction.Streak1)}: frames, panels";
        Menu.Items[(int)Slot.Linker].Hint = $"key {Keybinds.NameOf(s, GameAction.Streak2)}: wires, settings";
        Menu.Items[(int)Slot.Hands].Hint = $"key {Keybinds.NameOf(s, GameAction.PrimaryWeapon)} / {Keybinds.NameOf(s, GameAction.SecondaryWeapon)}: your guns";
    }

    /// <summary>
    /// One frame of the wheel: <paramref name="held"/> is the wheel key, <paramref name="delta"/> this frame's mouse
    /// movement, <paramref name="blocked"/> true while a menu, the chat or the parts menu has the screen (or the mouse is
    /// free). Opens on the key going down, follows the mouse while held, and on release takes out the tool picked (the
    /// number keys' request) or cancels. Returns true while it is open (the caller freezes the view and keeps the mouse
    /// buttons from the gun and tools).
    /// </summary>
    public bool Step(Game g, bool held, Vector2 delta, bool blocked)
    {
        var pressed = held && !_wasHeld;
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
        Menu.Current = SlotOf(h.Tool);
        if (!Menu.IsOpen)
        {
            if (!pressed)
                return false;
            Menu.Open();
            return true;
        }
        if (held)
        {
            Menu.Move(delta);
            return true;
        }
        var pick = Menu.Release();
        if (pick < 0)
            return false; // let go in the middle: a cancel
        if (ToolOf(pick) is not { } tool)
        {
            g.SandboxSay(pick switch
            {
                (int)Slot.Hoist => "the hoist is coming later",
                (int)Slot.Configure => "the configure tool is coming later",
                (int)Slot.Scanner => "the scanner is coming later",
                _ => "the hand drill is coming later",
            });
            return false;
        }
        if (tool != h.Tool)
            g.SandboxSend(CActKind.Tool, part: (byte)tool);
        return false;
    }

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
            case Slot.Wrench: // a slanted handle with an open jaw at the top
                Line(-0.6f, 0.6f, 0.25f, -0.25f, 0.2f);
                Disc(0.38f, -0.38f, 0.34f, col);
                Box(0.38f, -0.95f, 0.62f, -0.38f, bg, 0f); // (the jaw's gap)
                Disc(0.5f, -0.5f, 0.13f, bg);
                Disc(-0.6f, 0.6f, 0.13f, col);
                break;
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
            case Slot.Hands: // a pistol from boxes
                Box(-0.75f, -0.4f, 0.7f, -0.1f, col);
                Box(-0.6f, -0.1f, -0.2f, 0.7f, col);
                Box(-0.2f, -0.1f, 0.05f, 0.15f, col);
                Box(0.55f, -0.5f, 0.65f, -0.4f, col, 0f);
                break;
            case Slot.Hoist: // a chain and a hook
            {
                Line(-0.6f, -0.8f, 0.6f, -0.8f, 0.14f);
                Line(0f, -0.8f, 0f, 0.05f, 0.1f);
                var hook = P(0f, 0.35f);
                ImDrawListExtensions.PathClear(dl);
                ImDrawListExtensions.PathArcTo(dl, in hook, 0.3f * r, -MathF.PI / 2, MathF.PI * 1.15f, 14);
                ImDrawListExtensions.PathStroke(dl, col, ImDrawFlags.None, 0.12f * r);
                break;
            }
            case Slot.Configure: // three sliders
                for (var i = -1; i <= 1; i++)
                {
                    Line(-0.75f, i * 0.5f, 0.75f, i * 0.5f, 0.08f);
                    Box(i * 0.35f - 0.12f, i * 0.5f - 0.17f, i * 0.35f + 0.12f, i * 0.5f + 0.17f, col);
                }
                break;
            case Slot.Scanner: // a handset with a screen and waves
            {
                Box(-0.7f, -0.3f, 0.05f, 0.75f, col);
                Box(-0.58f, -0.18f, -0.07f, 0.3f, bg);
                var o = P(0.05f, -0.3f);
                for (var i = 1; i <= 2; i++)
                {
                    ImDrawListExtensions.PathClear(dl);
                    ImDrawListExtensions.PathArcTo(dl, in o, (0.3f + i * 0.25f) * r, -MathF.PI * 0.45f, -MathF.PI * 0.05f, 8);
                    ImDrawListExtensions.PathStroke(dl, col, ImDrawFlags.None, 0.08f * r);
                }
                break;
            }
            default: // the hand drill: a motor body, a pistol grip, a chuck and a long twisted bit
            {
                Box(-0.85f, -0.45f, -0.05f, -0.05f, col, 0.12f);
                Box(-0.65f, -0.05f, -0.35f, 0.75f, col);
                Box(-0.05f, -0.35f, 0.2f, -0.15f, col, 0f);
                Line(0.2f, -0.25f, 0.8f, -0.25f, 0.1f);
                var tip = P(0.98f, -0.25f);
                var t0 = P(0.8f, -0.31f);
                var t1 = P(0.8f, -0.19f);
                ImDrawListExtensions.AddTriangleFilled(dl, in t0, in tip, in t1, col);
                for (var i = 0; i < 4; i++)
                    Line(0.27f + i * 0.14f, -0.15f, 0.35f + i * 0.14f, -0.35f, 0.04f);
                break;
            }
        }
    }
}
