using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the quick wheel of the last 8 parts you placed (board item dev-sandbox-snappy-build-sb01, decision 24; the
/// generic <see cref="RadialMenu"/>, as the tool wheel). Hold the recent-parts key (L), flick towards a part, let go to take
/// that kind into your hands. The newest is at the top, going clockwise to the oldest. Placing a part puts its kind at
/// the front (a kind already in the list moves up, so the list never repeats one).
/// </summary>
public sealed class SandboxRecentWheel
{
    public const int Size = 8;

    public readonly RadialMenu Menu = new(Size);
    private readonly ushort[] _kinds = new ushort[Size];
    private int _count;
    private bool _wasHeld;

    public SandboxRecentWheel()
    {
        Menu.Title = "Recent";
        Menu.Help = "flick to a part, let go";
        Refresh();
    }

    public int Count => _count;

    public ushort KindAt(int i) => _kinds[i];

    /// <summary>A part of this kind was just placed: it goes to the front.</summary>
    public void Placed(ushort kind)
    {
        var at = Array.IndexOf(_kinds, kind, 0, _count);
        if (at == 0)
            return;
        if (at < 0)
        {
            at = Math.Min(_count, Size - 1);
            if (_count < Size)
                _count++;
        }
        for (var i = at; i > 0; i--)
            _kinds[i] = _kinds[i - 1];
        _kinds[0] = kind;
        Refresh();
    }

    public void Clear()
    {
        _count = 0;
        Refresh();
    }

    private void Refresh()
    {
        for (var i = 0; i < Size; i++)
        {
            var it = Menu.Items[i];
            var d = i < _count ? ConstructParts.Get(_kinds[i]) : null;
            it.Label = d?.Name ?? "";
            it.Hint = d == null ? "" : d.Role.ToString().ToLowerInvariant();
            it.Enabled = d != null;
            it.Note = d == null ? "nothing placed yet" : "";
            it.Icon = i;
        }
    }

    /// <summary>One frame: opens on the key going down, follows the mouse, and on release takes the kind picked. True while open (the view is frozen).</summary>
    public bool Step(Game g, bool held, Vector2 delta, bool blocked, float dt = 1f / 60)
    {
        var pressed = held && !_wasHeld;
        _wasHeld = held;
        var ok = !blocked && g.SandboxOn && g.Phase == MatchPhase.Playing && g.Local is { Alive: true } me && !g.SandboxHands.Hand(me.Id).Seated;
        if (!ok)
        {
            if (Menu.IsOpen)
                Menu.Close();
            return false;
        }
        if (!Menu.IsOpen)
        {
            if (!pressed)
                return false;
            Menu.Open();
            return true;
        }
        if (held)
        {
            Menu.Move(delta, dt);
            return true;
        }
        var pick = Menu.Release();
        if (pick >= 0 && pick < _count)
            g.SandboxSendSnap(Game.SnapTake, body: _kinds[pick]);
        return false;
    }

    public void Draw(float scale)
    {
        if (!Menu.IsOpen)
            return;
        var size = ImGui.GetIO().DisplaySize;
        Menu.Draw(ImGui.GetForegroundDrawList(), new float2(size.X * 0.5f, size.Y * 0.5f), scale, null);
    }
}
