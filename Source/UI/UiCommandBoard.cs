using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The command view's Board pick (docs/space/COMMAND.md "Boarding"): from the order menu on another craft at any zoom, the view
/// flies to that craft and pins on it; a left click on its hologram picks the door to seal on or the hull spot to cut into, sends
/// the Board order (Game/GameNavBoard.cs) and puts the view back as it was. A right click or Esc puts it back without an order.
/// </summary>
public sealed partial class Ui
{
    private ushort _cmdBoardShip, _cmdBoardCarrier; // picking: the craft to board and the carrier whose pod goes (0: not picking)
    private bool _cmdBoardSeal; // a door to seal on, else a hull spot to cut into
    private bool _cmdBoardHadAuto;
    private int _cmdBoardHadBody;
    private ushort _cmdBoardHadShip;
    private double _cmdBoardHadSpan;
    private Vec3d _cmdBoardHadPan;
    private string _cmdBoardBanner = "", _cmdBoardHoverText = "";
    private int _cmdBoardHoverPart = -2;

    /// <summary>Is the view waiting for a click on the craft to board?</summary>
    public bool CommandBoardPicking => _cmdBoardShip != 0;

    /// <summary>Start picking a spot on <paramref name="t"/> for <paramref name="carrier"/>'s pod: the view flies to it.</summary>
    private void CmdBoardStart(Construct carrier, Construct t, bool seal)
    {
        if (_cmdBoardShip == 0)
        {
            _cmdBoardHadAuto = _cmdFocusAuto;
            _cmdBoardHadBody = _cmdFocusBody;
            _cmdBoardHadShip = _cmdFocusShip;
            _cmdBoardHadSpan = _cmdSpanWant;
            _cmdBoardHadPan = _cmdPan;
        }
        _cmdBoardShip = t.Id;
        _cmdBoardCarrier = carrier.Id;
        _cmdBoardSeal = seal;
        _cmdFocusAuto = false;
        _cmdFocusShip = t.Id;
        _cmdFocusBody = -1;
        _cmdPan = Vec3d.Zero;
        _cmdSpanWant = Math.Max(CommandMinSpan, t.Radius * 2.2); // (it fills most of the height: the cut-in spot reads)
        _cmdBoardBanner = (seal ? "BOARDING: LEFT CLICK A DOOR OF " : "BOARDING: LEFT CLICK WHERE TO CUT INTO ") + CmdName(t) + "     RIGHT CLICK OR ESC: CANCEL";
        _cmdBoardHoverPart = -2;
        _cmdCutOn = false;
        _cmdCutPart = _cmdCutWant = -2;
    }

    /// <summary>For the window tool's <c>cmdboard</c>: the Board pick on <paramref name="t"/> for the picked craft's pod. Null, or why not.</summary>
    public string? CommandBoardTry(Game g, Construct t, bool seal)
    {
        if (g.Constructs.Get(_cmdSel) is not { } carrier || Game.BoardPodOf(g.Constructs, carrier) == null)
            return "the picked craft has no boarding pod docked";
        if (carrier == t)
            return "that is the carrier";
        CmdBoardStart(carrier, t, seal);
        return null;
    }

    /// <summary>Stop picking: the view goes back to what it showed before.</summary>
    private void CmdBoardEnd()
    {
        if (_cmdBoardShip == 0)
            return;
        _cmdBoardShip = 0;
        _cmdFocusAuto = _cmdBoardHadAuto;
        _cmdFocusBody = _cmdBoardHadBody;
        _cmdFocusShip = _cmdBoardHadShip;
        _cmdSpanWant = _cmdBoardHadSpan;
        _cmdPan = _cmdBoardHadPan;
    }

    /// <summary>The pick's target and carrier as they are now, or false (and the pick ends) when it can't go on.</summary>
    private bool CmdBoardLive(Game g, out Construct t, out int k)
    {
        t = null!;
        k = CmdFind(_cmdBoardShip);
        if (k >= 0 && g.Constructs.Get(_cmdBoardShip) is { } target && g.Constructs.Get(_cmdBoardCarrier) is { } carrier && Game.BoardPodOf(g.Constructs, carrier) != null)
        {
            t = target;
            return true;
        }
        CmdNote("BOARDING CANCELLED: THE POD OR THE SHIP IS GONE");
        CmdBoardEnd();
        return false;
    }

    /// <summary>Each frame while picking: the banner, and what a click under the cursor would do.</summary>
    private void CmdBoardDraw(ImDrawListPtr dl, Game g, float fade, double now)
    {
        if (_cmdBoardShip == 0 || !CmdBoardLive(g, out var t, out var k))
            return;
        CmdText(dl, new float2(_cvCentre.X, _cvOrg.Y + 72 * _scale), 13, CmdAmber, fade, _cmdBoardBanner, 1);
        if (_cmdOverPanel)
            return;
        var m = ImGui.GetIO().MousePos;
        var part = CmdBoardPick(t, _cmdCraft[k], m, out var part0, out var door, doors: _cmdBoardSeal) && (!_cmdBoardSeal || door) ? part0 : -1;
        if (part != _cmdBoardHoverPart)
        {
            _cmdBoardHoverPart = part; // (the label is made when what is under the cursor changes, not each frame)
            _cmdBoardHoverText = part < 0 ? "" : _cmdBoardSeal ? "SEAL ON " + t.DefOf(part).Name.ToUpperInvariant() : "CUT IN HERE"; // (the host picks the hull piece the pod meets there)
        }
        if (!_cmdBoardSeal)
            CmdCutUpdate(g, t, part, now); // (where it would cut in, drawn on the hologram: UiCommandHolo.cs)
        if (part >= 0)
            CmdText(dl, m + new float2(16 * _scale, 12 * _scale), 12, CmdWhite, fade, _cmdBoardHoverText);
    }

    /// <summary>A left click while picking: the order goes if a door (or a hull spot) is under it.</summary>
    private void CmdBoardClick(Game g, float2 m)
    {
        if (!CmdBoardLive(g, out var t, out var k))
            return;
        if (!CmdBoardPick(t, _cmdCraft[k], m, out var part, out var door, doors: _cmdBoardSeal) || _cmdBoardSeal && !door)
        {
            CmdNote(_cmdBoardSeal ? "LEFT CLICK ON A DOOR OF " + CmdName(t) : "LEFT CLICK ON THE HULL OF " + CmdName(t));
            return;
        }
        g.NavSend(_cmdBoardCarrier, NavKind.Board, 0, new Vec3d(t.Id, part, _cmdBoardSeal ? 1 : 0), 0);
        CmdNote((_cmdBoardSeal ? "POD SENT TO SEAL ON " : "POD SENT TO CUT INTO ") + CmdName(t));
        CmdBoardEnd();
    }
}
