using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The remover tool's selection on this PC (board item dev-sandbox-remover-tool-rt01, decision 30; docs/sandbox/CONSTRUCTS.md,
/// "The remover tool"): the parts and routed links marked for deletion, kept as (construct, kind, index) with what the thing
/// was when it was marked (its part type and rest-frame place, or its link type, end and first node), so a selection that
/// outlives a change to the construct (another player took a part off; a piece split away) drops what no longer matches
/// instead of marking its neighbour (<see cref="Prune"/> runs every frame). Nothing here allocates after construction.
/// </summary>
public sealed class RemoverSelection
{
    public readonly struct Mark
    {
        public readonly Construct Ref; // the construct itself (the draw uses it; Prune checks it is still the live one)
        public readonly ushort Body;
        public readonly bool Route;
        public readonly byte Index;
        public readonly ushort Def; // a part: its type; a link: its type and end part
        public readonly Vector3 Pos; // a part: its rest-frame place; a link: its first node's

        public Mark(Construct c, bool route, int index, ushort def, Vector3 pos)
        {
            Ref = c;
            Body = c.Id;
            Route = route;
            Index = (byte)index;
            Def = def;
            Pos = pos;
        }
    }

    public const int Max = ConstructEdits.MaxRemoveTargets;

    private readonly Mark[] _items = new Mark[Max];
    private readonly HashSet<long> _keys = new(Max + 1);
    private int _count, _links;

    public int Count => _count;

    public int LinkCount => _links;

    public int PartCount => _count - _links;

    public Mark this[int i] => _items[i];

    public static long Key(ushort body, bool route, int index) => ((long)body << 16) | (route ? 0x8000L : 0) | (uint)index;

    public bool Contains(ushort body, bool route, int index) => _keys.Contains(Key(body, route, index));

    /// <summary>Is this selected link on construct <paramref name="body"/>?</summary>
    public bool ContainsLink(ushort body, int route) => _links > 0 && _keys.Contains(Key(body, true, route));

    public bool AddPart(Construct c, int part)
    {
        if (_count >= Max || part < 0 || part >= c.PartCount || !_keys.Add(Key(c.Id, false, part)))
            return false;
        _items[_count++] = new Mark(c, false, part, c.Parts[part].Def, c.Parts[part].Pos);
        return true;
    }

    public bool AddLink(Construct c, int route)
    {
        if (_count >= Max || route < 0 || route >= c.RouteCount || !_keys.Add(Key(c.Id, true, route)))
            return false;
        ref var r = ref c.Routes[route];
        _items[_count++] = new Mark(c, true, route, LinkDef(in r), r.Count > 0 ? c.RouteNodes[r.Start].Pos : Vector3.Zero);
        _links++;
        return true;
    }

    private static ushort LinkDef(in CRoute r) => (ushort)(((int)r.Type << 8) | r.To);

    public bool Remove(ushort body, bool route, int index)
    {
        if (!_keys.Remove(Key(body, route, index)))
            return false;
        for (var i = 0; i < _count; i++)
            if (_items[i].Body == body && _items[i].Route == route && _items[i].Index == index)
            {
                _items[i] = _items[--_count];
                break;
            }
        if (route)
            _links--;
        return true;
    }

    public void Clear()
    {
        _count = _links = 0;
        _keys.Clear();
    }

    /// <summary>Marks every part and link of <paramref name="c"/> (the double-click). Returns how many were added.</summary>
    public int AddAll(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            n += AddPart(c, i) ? 1 : 0;
        for (var k = 0; k < c.RouteCount; k++)
            n += AddLink(c, k) ? 1 : 0;
        return n;
    }

    /// <summary>Unmarks every part and link of construct <paramref name="body"/>. Returns how many were removed.</summary>
    public int RemoveAll(ushort body)
    {
        var n = 0;
        for (var i = _count - 1; i >= 0; i--)
            if (_items[i].Body == body)
            {
                Remove(body, _items[i].Route, _items[i].Index);
                n++;
            }
        return n;
    }

    /// <summary>Drops whatever no longer names the same part or link (gone, renumbered, changed). Returns how many went.</summary>
    public int Prune(ConstructWorld world)
    {
        var kept = 0;
        for (var i = 0; i < _count; i++)
        {
            var it = _items[i];
            var ok = world.Get(it.Body) is { } c && ReferenceEquals(c, it.Ref) && (it.Route ? LinkMatches(c, in it) : PartMatches(c, in it));
            if (ok)
                _items[kept++] = it;
        }
        var gone = _count - kept;
        if (gone == 0)
            return 0;
        _count = kept;
        _keys.Clear();
        _links = 0;
        for (var i = 0; i < _count; i++)
        {
            _keys.Add(Key(_items[i].Body, _items[i].Route, _items[i].Index));
            _links += _items[i].Route ? 1 : 0;
        }
        return gone;
    }

    private static bool PartMatches(Construct c, in Mark it) =>
        it.Index < c.PartCount && c.Parts[it.Index].Def == it.Def && c.Parts[it.Index].Pos == it.Pos;

    private static bool LinkMatches(Construct c, in Mark it)
    {
        if (it.Index >= c.RouteCount)
            return false;
        ref var r = ref c.Routes[it.Index];
        return LinkDef(in r) == it.Def && (r.Count > 0 ? c.RouteNodes[r.Start].Pos : Vector3.Zero) == it.Pos;
    }

    /// <summary>Writes the selection as the server's request (parts and links, any order). Returns how many.</summary>
    public int ToTargets(Span<RemoveTarget> into)
    {
        var n = Math.Min(_count, into.Length);
        for (var i = 0; i < n; i++)
            into[i] = new RemoveTarget(_items[i].Body, _items[i].Route, _items[i].Index);
        return n;
    }
}

/// <summary>What the remover tool's frame reads from the keys (the game fills it from the binds; the self-test scripts it).</summary>
public struct RemoverInput
{
    public bool FireDown; // LMB held
    public bool FirePressed; // LMB went down this frame
    public bool AimPressed; // RMB went down this frame
    public bool Shift;
    public long NowMs; // a clock in milliseconds
}

/// <summary>
/// The remover tool's input on this PC (board item dev-sandbox-remover-tool-rt01): LMB toggles the part or link under the
/// crosshair in the selection (hold LMB and sweep to mark more, each thing once per stroke; a stroke that starts on a marked
/// thing, or with Shift, unmarks as it goes; a second click within <see cref="DoubleClickMs"/> on the same part marks its construct
/// whole); RMB with something selected asks "Delete N parts? Right-click again to confirm" next to the crosshair (the game keeps
/// running) and a second RMB within <see cref="ConfirmMs"/> sends the request; LMB, Esc, putting the tool away, the timeout or
/// a change to the selection cancel the question and keep the selection; Shift+RMB clears the selection. A thing is marked only
/// when the tool may delete it (the aim highlight's rule, <see cref="SandboxAim.Remover"/>). Allocation-free every frame.
/// </summary>
public sealed class RemoverController
{
    public const long ConfirmMs = 4000;
    public const long DoubleClickMs = 350;

    public readonly RemoverSelection Selection = new();

    /// <summary>Decision 19's reason for a construct and a player (survival; Program caches it per construct), or null. Unset: asks <see cref="ConstructGrab.Defended"/> directly.</summary>
    public Func<Game, Construct, byte, string?>? ContestedOf;

    private readonly HashSet<long> _stroke = new(1024);
    private readonly RemoveTarget[] _send = new RemoveTarget[RemoverSelection.Max];
    private bool _stroking, _strokeRemoves, _mute;
    private long _promptAt = -1;
    private int _promptParts, _promptLinks;
    private long _lastClickAt = long.MinValue;
    private long _lastClickKey;
    private int _textParts = -1, _textLinks = -1;
    private long _lookKey = long.MinValue;
    private string _look = "";
    private string _promptText = "", _selectedText = "";

    /// <summary>The question is up (waiting for the second right-click).</summary>
    public bool PromptPending => _promptAt >= 0;

    /// <summary>Deletions asked for so far (the self-test).</summary>
    public int Sent { get; private set; }

    /// <summary>Everything forgotten: a new game or match.</summary>
    public void Reset()
    {
        Selection.Clear();
        Idle(null);
    }

    /// <summary>The tool is not out (or the game is not Sandbox): the question and the stroke end; the selection stays.</summary>
    public void Idle(Game? g)
    {
        _promptAt = -1;
        _stroking = _mute = false;
        _lastClickAt = long.MinValue;
        if (_stroke.Count > 0)
            _stroke.Clear();
        if (g != null)
        {
            g.SandboxPrompt = "";
            g.SandboxRemoverHint = "";
        }
    }

    /// <summary>Esc (or anything else that backs out): the question goes, the selection stays. True if there was a question (the key is used up).</summary>
    public bool Cancel(Game? g)
    {
        if (_promptAt < 0)
            return false;
        _promptAt = -1;
        if (g != null)
        {
            g.SandboxPrompt = "";
            g.SandboxSay("delete cancelled (the selection is kept)");
        }
        return true;
    }

    /// <summary>One frame with the remover out and the mouse captured. <paramref name="aimed"/>: the crosshair is on a construct (<paramref name="hit"/>).</summary>
    public void Frame(Game g, Player me, in RemoverInput inp, bool aimed, in ConstructHit hit)
    {
        var now = inp.NowMs;
        Selection.Prune(g.Constructs);
        ConstructRender.RemoverSel = Selection;
        if (_promptAt >= 0 && (now - _promptAt > ConfirmMs || Selection.PartCount != _promptParts || Selection.LinkCount != _promptLinks))
        {
            if (now - _promptAt > ConfirmMs)
                g.SandboxSay("delete cancelled (timed out; the selection is kept)");
            _promptAt = -1;
        }
        if (inp.FirePressed && _promptAt >= 0)
        {
            Cancel(g); // (LMB backs out; that click does not also mark)
            _mute = true;
        }
        if (!inp.FireDown)
        {
            _stroking = _mute = false;
            if (_stroke.Count > 0)
                _stroke.Clear();
        }
        if (inp.AimPressed)
            Rmb(g, in inp);
        var reach = aimed && hit.Distance <= ConstructBuild.Reach;
        var route = reach && hit.RoutePlus1 > 0 && hit.Body.RouteCount >= hit.RoutePlus1;
        var index = route ? hit.RoutePlus1 - 1 : hit.Part;
        string? why = null;
        if (reach)
        {
            why = RefusalFor(g, hit.Body, me.Id);
            Look(g, hit.Body, route, index, why);
        }
        if (inp.FireDown && !_mute && reach)
            Select(g, in inp, hit.Body, route, index, why);
        Texts(g);
    }

    private string? RefusalFor(Game g, Construct c, byte player)
    {
        var contested = g.SandboxInfiniteParts ? null : ContestedOf != null ? ContestedOf(g, c, player) : ConstructGrab.Defended(c, player, g.SandboxAliveAt, g.SandboxNameOf);
        return SandboxAim.Remover(c, player, g.SandboxInfiniteParts, contested).Refusal;
    }

    private void Select(Game g, in RemoverInput inp, Construct c, bool route, int index, string? why)
    {
        var key = RemoverSelection.Key(c.Id, route, index);
        if (inp.FirePressed)
        {
            // The stroke's mode: a press on a marked thing (or with Shift) unmarks as it sweeps; else it marks.
            _stroking = true;
            _strokeRemoves = inp.Shift || Selection.Contains(c.Id, route, index);
            _stroke.Clear();
            var twice = _lastClickAt != long.MinValue && inp.NowMs - _lastClickAt <= DoubleClickMs && _lastClickKey == key;
            _lastClickAt = twice ? long.MinValue : inp.NowMs;
            _lastClickKey = key;
            if (twice && why == null)
            {
                // Double-click: the whole construct (the first click's toggle is then undone for it as part of the whole).
                if (_strokeRemoves && inp.Shift)
                    Selection.RemoveAll(c.Id);
                else
                    Selection.AddAll(c);
                _stroke.Add(key);
                _strokeRemoves = inp.Shift; // (a sweep after the double-click marks, or with Shift unmarks)
                g.SandboxSay(inp.Shift ? "construct unmarked" : $"whole construct marked ({Selection.Count} selected)");
                return;
            }
        }
        if (!_stroking || !_stroke.Add(key))
            return; // (this one is done in this stroke)
        if (_strokeRemoves)
        {
            Selection.Remove(c.Id, route, index);
            return;
        }
        if (why != null)
        {
            if (inp.FirePressed)
                g.SandboxSay(why); // (only on the click: a sweep over a refused construct is not nagged)
            return;
        }
        if (!(route ? Selection.AddLink(c, index) : Selection.AddPart(c, index)) && Selection.Count >= RemoverSelection.Max)
            g.SandboxSay($"at most {RemoverSelection.Max} things at once");
    }

    private void Rmb(Game g, in RemoverInput inp)
    {
        if (inp.Shift)
        {
            Selection.Clear();
            _promptAt = -1;
            g.SandboxSay("selection cleared");
            return;
        }
        if (Selection.Count == 0)
        {
            _promptAt = -1;
            g.SandboxSay("nothing selected: LMB marks parts and links, then RMB twice deletes them");
            return;
        }
        if (_promptAt >= 0 && inp.NowMs - _promptAt <= ConfirmMs && Selection.PartCount == _promptParts && Selection.LinkCount == _promptLinks)
        {
            var n = Selection.ToTargets(_send);
            _promptAt = -1;
            Selection.Clear();
            g.SandboxRemove(_send.AsSpan(0, n));
            Sent++;
            return;
        }
        _promptAt = inp.NowMs;
        _promptParts = Selection.PartCount;
        _promptLinks = Selection.LinkCount;
    }

    private void Look(Game g, Construct c, bool route, int index, string? why)
    {
        var marked = Selection.Contains(c.Id, route, index);
        var key = ((long)c.Id << 24) ^ ((long)index << 8) ^ (route ? 1L << 40 : 0) ^ (marked ? 1L << 41 : 0) ^ ((long)Selection.Count << 42) ^ (why != null ? (long)why.GetHashCode() << 20 : 0);
        if (key != _lookKey)
        {
            _lookKey = key;
            var what = route ? $"{char.ToUpperInvariant(ConstructRoutes.Name(c.Routes[index].Type)[0])}{ConstructRoutes.Name(c.Routes[index].Type)[1..]} link" : c.DefOf(index).Name;
            _look = why != null ? $"{what} - {why}" : $"{what} - LMB: {(marked ? "unmark" : "mark")}, sweep for more, double-click: whole construct";
        }
        g.SandboxLookText = _look;
    }

    /// <summary>The words: the question next to the crosshair and the HUD line (rebuilt only when the counts change).</summary>
    private void Texts(Game g)
    {
        if (_textParts != Selection.PartCount || _textLinks != Selection.LinkCount)
        {
            _textParts = Selection.PartCount;
            _textLinks = Selection.LinkCount;
            var what = Describe(_textParts, _textLinks);
            _promptText = $"Delete {what}? Right-click again to confirm";
            _selectedText = _textParts + _textLinks == 0
                ? "LMB: mark a part or link (hold and sweep for more, double-click: the whole construct, Shift: unmark). RMB with some marked: delete"
                : $"{what} selected - RMB: delete, LMB on a marked one unmarks, Shift+RMB: clear";
        }
        g.SandboxPrompt = _promptAt >= 0 ? _promptText : "";
        g.SandboxRemoverHint = _selectedText;
    }

    /// <summary>"3 parts", "1 part and 2 links": what a count says.</summary>
    public static string Describe(int parts, int links)
    {
        var p = $"{parts} part{(parts == 1 ? "" : "s")}";
        if (links == 0)
            return p;
        var l = $"{links} link{(links == 1 ? "" : "s")}";
        return parts == 0 ? l : $"{p} and {l}";
    }
}
