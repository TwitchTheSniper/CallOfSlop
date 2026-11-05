using Brutal.ImGuiApi;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Keys and signs in the hand (docs/sandbox/CONSTRUCTS.md, "Keys and panels"). A Numpad or Code Lock: the use key on a key presses
/// that key; on its screen or body it opens a small typing box, so the keyboard can do the lot. A sign or a lamp bank: the use
/// key opens the same box on what it shows now. The box is not a menu: the view stays, the mouse stays captured, Enter sends and
/// Esc leaves. It holds the part as an index, stamped and checked each frame like a lever in the hand.
/// </summary>
public static partial class Program
{
    private static ushort _tyBody;
    private static int _tyPart = -1, _tySlot;
    private static long _tyStamp;
    private static bool _tyOn, _tyStart;
    private static ImInputString? _tyInput;
    private static long _ctKey = -1;
    private static string _ctText = "";

    private static void TypingClose()
    {
        _tyOn = false;
        _tyPart = -1;
    }

    private static bool TypingEsc()
    {
        if (!_tyOn)
            return false;
        TypingClose();
        return true;
    }

    private static void TypingOpen(Construct c, int part, int slot, string start)
    {
        _tyBody = c.Id;
        _tyPart = part;
        _tySlot = slot;
        _tyStamp = LinkStamp(c, part, 255, 0);
        _tyOn = _tyStart = true;
        _tyInput ??= new ImInputString(LogicText.MaxChars + 1);
        ImGuiSafe.SetInput(_tyInput, start);
    }

    /// <summary>The frame's ImGui pass: the box, while it has a part that is still there and still near.</summary>
    private static void DrawTyping()
    {
        if (!_tyOn || _session is not { } g)
            return;
        if (g.Constructs.Get(_tyBody) is not { } c || _tyPart >= c.PartCount || LinkStamp(c, _tyPart, 255, 0) != _tyStamp || c.Parts[_tyPart].Broken
            || g.Local is not { Alive: true } me || !_mouseCaptured || MenusOpen
            || Vector3.Distance(me.RenderEye, ConstructEdits.ReachPoint(c, _tyPart, me.RenderEye)) > ConstructConfig.Reach)
        {
            TypingClose();
            return;
        }
        var d = c.DefOf(_tyPart);
        var keyed = d.Logic is CLogic.Numpad or CLogic.CodeLock;
        if (Ui.DrawTypingBox(d.Name, keyed, _tyInput!, ref _tyStart) is { } typed)
        {
            if (keyed)
            {
                foreach (var ch in typed)
                    if (ch is >= '0' and <= '9')
                        g.SandboxSend(CActKind.KeyPress, body: _tyBody, part: (ushort)_tyPart, flags: (byte)ch);
                g.SandboxSend(CActKind.KeyPress, body: _tyBody, part: (ushort)_tyPart, flags: (byte)'E');
            }
            else
                g.SandboxPartText(_tyBody, _tyPart, _tySlot, typed);
            TypingClose();
        }
    }

    /// <summary>One frame of a key or sign under the crosshair. True when the use key's press went to it.</summary>
    private static bool ControlFrame(Game g, Player me, in SandboxHand h, bool aimed, in ConstructHit hit, bool join)
    {
        if (!aimed || join || hit.SlotPlus1 != 0 || hit.RoutePlus1 != 0 || hit.Part < 0 || hit.Part >= hit.Body.PartCount || hit.Distance > ConstructBuild.UseReach || hit.Body.Loose)
            return false;
        var c = hit.Body;
        var d = c.DefOf(hit.Part);
        ref var p = ref c.Parts[hit.Part];
        var keyed = d.Logic is CLogic.Numpad or CLogic.CodeLock;
        if (p.Broken || !keyed && d.TextSlots == 0 || h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0)
            return false;
        var box = keyed ? LogicInputs.KeyAt(c, hit.Part, hit.Point) : -1;
        var key = box >= 0 ? d.Boxes[box].Key : '\0';
        var slot = d.TextSlots == 2 && p.Reading >= 0.5f && p.Text2 != null ? 1 : 0;
        var why = keyed ? null : g.SandboxConfigRefusal(c, me.Id);
        ConstructRender.AimBody = c;
        ConstructRender.AimPart = hit.Part;
        ConstructRender.AimRefused = why != null;
        ConstructRender.AimHands = why == null;
        ConstructRender.AimKeyBox = box;
        var lookKey = HashCode.Combine(p.Def, key, why, (int)(p.Reading * 100), p.Out >= 0.5f, SandboxKeys.Stamp, slot);
        if (lookKey != _ctKey)
        {
            _ctKey = lookKey;
            var action = key switch
            {
                'E' => "accept",
                'C' => "clear",
                '\0' => "type",
                _ => $"press {key}",
            };
            _ctText = SandboxKeys.Line(d.Name, keyed ? LogicDisplays.Look(p, d) : LogicText.Get(p, slot), why, SandboxKeys.Do(SandboxKeys.Use, action));
        }
        g.SandboxLookText = _ctText;
        if (!Pressed(GameAction.Interact))
            return false;
        if (key != 0)
            g.SandboxSend(CActKind.KeyPress, body: c.Id, part: (ushort)hit.Part, flags: (byte)key);
        else if (why != null)
            g.SandboxSay(why);
        else
            TypingOpen(c, hit.Part, slot, keyed ? "" : LogicText.Get(p, slot));
        return true;
    }
}
