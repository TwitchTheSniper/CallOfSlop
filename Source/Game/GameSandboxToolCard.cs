using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The card on a held tool's screen (docs/sandbox/CONSTRUCTS.md, "Tools in hand"): the tool's name and mode, a timed edit's
/// progress, and its keys, from the same lines the HUD showed, so a rebind shows as everywhere else. Only what is about the
/// tool moved: the look line is about what the crosshair is on, changes as fast as you sweep, runs long, and stays on screen
/// with the notices. While a card is shown the HUD leaves these lines out; whenever one isn't (hands, the gun, a view with
/// no tool drawn, a model with no screen) they stay on the HUD. All of it is local, so it needs nothing on the wire.
/// </summary>
public sealed partial class Game
{
    private const double PageSeconds = 4;

    /// <summary>The frame a tool's card was last drawn (CrashLog.Frame).</summary>
    public long SandboxToolCardFrame = -1;

    /// <summary>Is the local tool's screen showing this frame? The HUD then leaves its lines to it.</summary>
    public bool SandboxToolCardShown => SandboxToolCardFrame == CrashLog.Frame;

    /// <summary>A timed edit's line.</summary>
    private string SandboxEditText(in SandboxHand h)
    {
        var pct = (int)(h.Progress(SandboxNow) * 100);
        if (_sbEdit.Stale((long)h.Edit * 1000 + pct))
            _sbEdit.Text = $"{(h.Edit == EditKind.Remove ? "Taking it off" : h.Edit is EditKind.Weld or EditKind.SocketWeld ? "Attaching" : "Fitting")}... {pct}%";
        return _sbEdit.Text;
    }

    /// <summary>The keys of the tool in hand, or of the part carried: cached strings, made again only when they change.</summary>
    public string SandboxToolHint(Player me, in SandboxHand h)
    {
        var held = ConstructParts.Get(h.Held);
        if (h.Tool == ConstructTool.PhysicsGun)
            return SandboxPhysGunHint(h);
        if (h.Tool == ConstructTool.Linker)
            return SandboxWireFrom;
        if (h.Tool == ConstructTool.Configure)
            return SandboxConfigHint;
        if (h.Tool == ConstructTool.Remover && SandboxRemoverHint.Length > 0)
            return SandboxRemoverHint;
        if (h.Tool == ConstructTool.Selector && SandboxSelectHint.Length > 0)
            return SandboxSelectHint;
        if (h.Tool == ConstructTool.Nudge && SandboxNudgeHint.Length > 0)
            return SandboxNudgeHint;
        if (h.Tool == ConstructTool.NoCollide && SandboxNoCollideHint.Length > 0)
            return SandboxNoCollideHint;
        if (h.Tool == ConstructTool.GroundDrill && SandboxSculptHint.Length > 0)
            return SandboxSculptHint;
        if (h.Tool == ConstructTool.Welder && held == null && SandboxWeldHint.Length > 0)
            return SandboxWeldHint;
        if (held == null)
            return SandboxBaseKeys;
        // The counts while holding a part (milestone 3): how many more of it the inventory holds.
        var inv = SandboxInv.Of(me.Id);
        var more = inv.Count(held.Id);
        var onNet = SandboxBeacon(me.Id) is { On: true } beacon && !inv.Infinite ? held.Id < beacon.Counts.Length ? beacon.Counts[held.Id] : 0 : -1; // (a beacon in range: the network's count)
        if (_sbHint.Stale(held.Id * 100000L + more + (inv.Infinite ? 1L << 40 : 0) + ((long)(SandboxTurnKeys.GetHashCode() & 0xffff) << 41) + ((long)(onNet + 1) << 20) + (h.Tool == ConstructTool.None ? 1L << 57 : 0) + (h.Tool == ConstructTool.Hands ? 1L << 56 : 0) + (ConstructBuild.ToolFits(h.Tool, held) ? 1L << 62 : 0) + ((long)(SandboxKeys.Stamp & 15) << 58)))
            _sbHint.Text = (h.Tool == ConstructTool.None ? $"Needs the {held.Tool.Label()}. " : ConstructBuild.ToolFits(h.Tool, held) ? $"hold {SandboxKeys.Fire}: fit, {SandboxTurnKeys}, " : "")
                           + $"{(h.Tool == ConstructTool.Hands ? SandboxKeys.Aim : SandboxKeys.Drop)}: drop"
                           + (inv.Infinite ? "" : onNet >= 0 ? $", {more + onNet} on the network" : more > 0 ? $", {more} left" : ", the last one");
        return _sbHint.Text;
    }

    /// <summary>The keys that hold for every tool.</summary>
    private string SandboxBaseKeys
    {
        get
        {
            if (_sbKeys.Stale(SandboxKeys.Stamp))
                _sbKeys.Text = $"{SandboxKeys.Menu}: parts, hold {SandboxKeys.Wheel}: tools";
            return _sbKeys.Text;
        }
    }

    /// <summary>
    /// The card for a tool's screen of <paramref name="cols"/> characters and <paramref name="rows"/> rows. Line 0 is its name; then its mode; then its
    /// keys, most useful first as every hint is written, a page at a time when they run past the screen.
    /// </summary>
    public void SandboxFillToolCard(Player me, in SandboxHand h, PanelCard card, string mode, int cols, int rows, double now)
    {
        card.Clear();
        card.State = PanelState.Idle;
        card.Add(0, ConstructParts.Get(h.Held)?.Name ?? SandboxHandText(me)); // (the tool you can see; the part you can't read off it)
        var first = 1;
        if (mode.Length > 0)
        {
            card.Add(1, "Mode: ").Add(1, mode);
            first = 2;
        }
        if (h.Edit != EditKind.None)
        {
            card.State = PanelState.Working;
            card.Bar = Math.Clamp(h.Progress(SandboxNow), 0, 1);
            card.Add(first, SandboxEditText(h));
            return;
        }
        var hint = SandboxToolHint(me, in h).AsSpan();
        if (hint.IsEmpty || hint.SequenceEqual(SandboxBaseKeys))
            hint = SandboxKeys.ToolIdle(h.Tool);
        var keys = SandboxBaseKeys.AsSpan();
        var both = hint.SequenceEqual(keys);
        int at = 0, column = 0, lines;
        var room = rows - first;
        card.Wrap(hint, cols, first, -1, int.MaxValue, ref at, ref column);
        lines = both ? (column > 0 ? at + 1 : at) : card.Wrap(keys, cols, first, -1, int.MaxValue, ref at, ref column);
        var pages = Math.Max(1, (lines + room - 1) / room);
        var page = pages > 1 ? (int)(now / PageSeconds) % pages : 0;
        at = column = 0;
        card.Wrap(hint, cols, first, rows - 1, page * room, ref at, ref column);
        if (!both)
            card.Wrap(keys, cols, first, rows - 1, page * room, ref at, ref column);
        if (pages > 1)
            card.Add(0, " ").Add(0, page + 1).Add(0, '/').Add(0, pages);
    }
}
