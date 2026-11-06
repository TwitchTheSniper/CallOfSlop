using System.Numerics;

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
    private static bool _scrDown, _scrTook;
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

    private static void PointerOff()
    {
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
            PointerMove(c, part, def, spot, now);
            PanelRender.SetPointer(key, spot, -1);
            PointerSay(g, d, null, "LMB: click, RMB: mouse off (then LMB picks up a loose one)");
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
