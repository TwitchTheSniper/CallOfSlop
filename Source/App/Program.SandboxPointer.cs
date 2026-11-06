using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// Hands on a screen (docs/sandbox/MEDIA.md, "Hands on a screen"). With Hands out and empty, the crosshair on a panel is a finger on its buttons (<see cref="PanelTouch"/>) and,
/// on a page's picture, a mouse inside the browser: hover shows YouTube's controls, LMB clicks and drags, the wheel scrolls. RMB takes the mouse off this part until
/// the crosshair leaves it, and F is never touched. Someone the configure rule refuses gets no mouse and no finger, only the reason. Held as a stamped index, as a lever in the hand is.
/// </summary>
public static partial class Program
{
    private static ushort _scrBody;
    private static int _scrPart = -1, _scrPanel, _scrPage = -1, _scrSentX = -1, _scrSentY = -1, _scrOffPart = -1;
    private static ushort _scrOffBody;
    private static long _scrStamp, _scrLookKey;
    private static bool _scrDown, _scrTook, _scrWeb;
    private static string _scrLook = "";

    /// <summary>The wheel's notches while the crosshair is a mouse.</summary>
    private static double PointerWheel(double notches)
    {
        var page = _scrPage;
        if (page < 0)
            return notches;
        MediaDirector.PageWheel(page, _scrSentX, _scrSentY, (int)(notches * 120), Brutal.GlfwApi.Glfw.Time);
        return 0;
    }

    private static bool _webTyping, _webSaid;

    /// <summary>A key while a page has the keyboard: it goes to the page, and only there. True when the game shouldn't see it. Enter on a page's picture gives it the keyboard, Esc takes it back.</summary>
    private static bool WebTypingKey(GlfwKey key, GlfwKeyAction action, GlfwModifier mods)
    {
        if (!_webTyping)
        {
            if (key != GlfwKey.Enter || action != GlfwKeyAction.Press || _scrPage < 0 || Ui.ChatOpen || !_scrWeb)
                return false;
            _webTyping = true;
            Array.Clear(_down);
            if (!_webSaid && _session is { } g)
                g.SandboxSay("Private page: nothing is saved, and logins end when the game closes. Keys stay on this PC.");
            _webSaid = true;
            return true;
        }
        if (key == GlfwKey.Escape)
        {
            if (action == GlfwKeyAction.Press)
                _webTyping = false;
            return true;
        }
        if (_scrPage >= 0)
            MediaDirector.PageKey(_scrPage, action == GlfwKeyAction.Release ? MediaBlock.KeyUp : MediaBlock.KeyDown, WindowsKey(key), (int)mods & 7, Brutal.GlfwApi.Glfw.Time);
        return true;
    }

    private static void OnWebChar(GlfwWindow window, uint codepoint)
    {
        if (_webTyping && _scrPage >= 0 && codepoint <= 0xFFFF)
            MediaDirector.PageKey(_scrPage, MediaBlock.KeyChar, (int)codepoint, 0, Brutal.GlfwApi.Glfw.Time);
    }

    /// <summary>GLFW's key to the Windows key code CEF wants.</summary>
    private static int WindowsKey(GlfwKey key)
    {
        var k = (int)key;
        return k switch
        {
            >= 'A' and <= 'Z' or >= '0' and <= '9' or 32 => k,
            59 => 186, // ;
            61 => 187, // =
            44 => 188, // ,
            45 => 189, // -
            46 => 190, // .
            47 => 191, // /
            96 => 192, // `
            91 => 219, // [
            92 => 220, // backslash
            93 => 221, // ]
            39 => 222, // '
            257 or 335 => 13, // enter
            258 => 9, // tab
            259 => 8, // backspace
            260 => 45, // insert
            261 => 46, // delete
            262 => 39,
            263 => 37,
            264 => 40,
            265 => 38, // arrows
            266 => 33,
            267 => 34, // page up, down
            268 => 36,
            269 => 35, // home, end
            >= 290 and <= 301 => 112 + k - 290, // F1..F12
            340 or 344 => 16,
            341 or 345 => 17,
            342 or 346 => 18, // shift, control, alt
            _ => 0,
        };
    }

    private static void PointerOff()
    {
        _webTyping = false;
        _scrWeb = false;
        if (_scrDown && _scrPage >= 0)
            MediaDirector.PageButton(_scrPage, _scrSentX, _scrSentY, false, 0);
        if (_scrPage >= 0)
            MediaDirector.PageLeave(_scrPage);
        _scrDown = false;
        _scrPage = -1;
        _scrPart = -1;
        _scrSentX = _scrSentY = -1;
        PanelRender.ClearPointer();
    }

    /// <summary>One frame. True while the crosshair is a mouse or a finger: LMB and RMB are the screen's, and Hands don't pick up or drop with them. RMB, then LMB, picks up a loose screen that is playing.</summary>
    private static bool PointerFrame(Game g, Player me, in SandboxHand h, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit, double now)
    {
        // A drag holds on past the panel's edge, until the button comes up or the part is gone.
        if (_scrDown && g.Constructs.Get(_scrBody) is { } held && _scrPart < held.PartCount && LinkStamp(held, _scrPart, 255, 0) == _scrStamp && h.Tool == ConstructTool.Hands)
        {
            if (Down(GameAction.Fire))
            {
                var panel = held.DefOf(_scrPart).Panels[_scrPanel];
                if (PanelTouch.Project(held, _scrPart, panel, eye, dir, out var at))
                    PointerMove(held, _scrPart, panel, at, now);
                _lvWheelOk = true;
                return true;
            }
            MediaDirector.PageButton(_scrPage, _scrSentX, _scrSentY, false, now);
            _scrDown = false;
            return true;
        }
        _scrDown = false;
        var usable = aimed && h.Tool == ConstructTool.Hands && !h.Seated && h.Held == ConstructParts.None && hit.SlotPlus1 == 0 && hit.RoutePlus1 == 0
            && hit.Part >= 0 && hit.Part < hit.Body.PartCount && hit.Distance <= ConstructBuild.UseReach && hit.Body.DefOf(hit.Part).Panels.Length > 0 && !hit.Body.Parts[hit.Part].Broken;
        if (!usable || PanelTouch.Find(hit.Body, hit.Part, eye, dir, out var spot) is not (>= 0 and var n))
        {
            _scrOffPart = -1;
            PointerOff();
            return false;
        }
        var c = hit.Body;
        var part = hit.Part;
        var d = c.DefOf(part);
        var def = d.Panels[n];
        var page = d.IsMedia ? MediaDirector.PageOf(c.Id, part) : -1;
        var region = PanelTouch.Region(def, spot);
        if (region < 0 && page < 0 || c.Id == _scrOffBody && part == _scrOffPart)
        {
            PointerOff();
            return false;
        }
        var why = g.SandboxHands.Contested(c, me.Id) ?? g.SandboxConfigRefusal(c, me.Id);
        if (why != null)
        {
            PointerOff();
            PointerSay(g, d, why, null);
            return false;
        }
        var onPicture = page >= 0 && region < 0 && PanelRender.InPicture(def, spot);
        if (!onPicture && region < 0)
        {
            PointerOff(); // (the case or the border: a loose screen is picked up there, as anything is)
            return false;
        }
        if (_scrBody != c.Id || _scrPart != part || _scrPanel != n || _scrPage != (onPicture ? page : -1))
            PointerOff();
        _scrBody = c.Id;
        _scrPart = part;
        _scrPanel = n;
        _scrStamp = LinkStamp(c, part, 255, 0);
        _lvWheelOk = onPicture;
        var key = (long)c.Id << 24 | (long)part << 8 | (uint)n;
        if (onPicture)
        {
            _scrPage = page;
            _scrWeb = d.IsWeb;
            PointerMove(c, part, def, spot, now);
            PanelRender.SetPointer(key, spot, -1);
            PointerSay(g, d, null, _webTyping ? "typing to the page: Esc stops" : d.IsWeb ? "LMB: click, Enter: type, RMB: mouse off" : "LMB: click, RMB: mouse off (then LMB picks up a loose one)");
            if (Pressed(GameAction.Aim))
            {
                _scrOffBody = c.Id;
                _scrOffPart = part;
                PointerOff();
                return true;
            }
            if (Pressed(GameAction.Fire) && !_swallowClick)
            {
                _scrDown = true;
                MediaDirector.PageButton(page, _scrSentX, _scrSentY, true, now);
            }
            return true;
        }
        PanelRender.SetPointer(key, spot, region);
        PointerSay(g, d, null, $"LMB: {(region >= 0 ? def.Touch[region].Label : "")}");
        if (region >= 0 && Pressed(GameAction.Fire) && !_swallowClick)
            g.SandboxPanelTouch(c.Id, part, n, region);
        return true;
    }

    private static void PointerMove(Construct c, int part, PanelDef panel, Vector2 at, double now)
    {
        var d = c.DefOf(part);
        PanelRender.PageSpot(panel, at, d.MediaWidth, d.MediaHeight, out var x, out var y);
        if (x == _scrSentX && y == _scrSentY)
            return;
        _scrSentX = x;
        _scrSentY = y;
        if (_scrPage >= 0)
            MediaDirector.PageMove(_scrPage, x, y, _scrDown);
    }

    private static void PointerSay(Game g, ConstructPartDef d, string? why, string? keys)
    {
        var key = HashCode.Combine(d.Name, why, keys);
        if (key != _scrLookKey)
        {
            _scrLookKey = key;
            _scrLook = SandboxKeys.Line(d.Name, keys, why, null);
        }
        g.SandboxLookText = _scrLook;
    }
}
