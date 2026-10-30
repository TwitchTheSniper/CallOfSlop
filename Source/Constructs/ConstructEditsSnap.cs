using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The server's half of snappy building (board item dev-sandbox-snappy-build-sb01, decision 24; docs/sandbox/CONSTRUCTS.md,
/// "Snappy building"): a player who asks for quick building, in a creative world (infinite parts), fits and sets down
/// parts at once and keeps holding the part, so a row of them is a sweep of the mouse; the last
/// <see cref="HistoryLength"/> parts each player placed can be undone (checked here: still theirs to take apart, in
/// reach, and the part still where it was put); and a part on a construct can be picked into the hands.
/// </summary>
public sealed partial class ConstructEdits
{
    public const int HistoryLength = 8;

    private struct PlacedPart
    {
        public ushort Body, Def;
        public Vector3 Pos;
        public byte Rot, Seg;
    }

    private readonly bool[] _quick = new bool[256];
    private readonly PlacedPart[]?[] _placed = new PlacedPart[256][];
    private readonly byte[] _placedCount = new byte[256];

    /// <summary>Does this player build the quick way (they asked for it, and the parts are infinite)?</summary>
    public bool QuickBuild(byte player) => _quick[player] && InfiniteParts;

    public void SetQuick(byte player, bool on) => _quick[player] = on;

    public bool QuickAsked(byte player) => _quick[player];

    /// <summary>How many placements this player could undo.</summary>
    public int UndoCount(byte player) => _placedCount[player];

    private void QuickFinish(ConstructWorld world, byte player, ref SandboxHand h, Vector3 eye)
    {
        var kind = h.Edit;
        h.Duration = 0;
        h.Edit = EditKind.None;
        Finish(world, player, ref h, kind, eye);
    }

    /// <summary>After a fit or a set-down: a quick builder keeps holding the part, and a creative one's placement is remembered for undo.</summary>
    private void SnapAfter(byte player, ref SandboxHand h, ConstructPartDef def, Construct c, int part)
    {
        if (!InfiniteParts)
            return;
        if (_quick[player] && h.Held == ConstructParts.None)
            h.Held = def.Id;
        if (part < 0 || part >= c.PartCount)
            return;
        var ring = _placed[player] ??= new PlacedPart[HistoryLength];
        var n = _placedCount[player];
        if (n == HistoryLength)
        {
            Array.Copy(ring, 1, ring, 0, HistoryLength - 1);
            n--;
        }
        ref var p = ref c.Parts[part];
        ring[n] = new PlacedPart { Body = c.Id, Def = def.Id, Pos = p.Pos, Rot = p.Rot, Seg = p.Seg };
        _placedCount[player] = (byte)(n + 1);
    }

    /// <summary>Takes back the player's last placement (creative only): null, or why not.</summary>
    public string? UndoPlaced(ConstructWorld world, byte player, Vector3 eye)
    {
        if (!InfiniteParts)
            return "undo is for creative building";
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        var n = _placedCount[player];
        if (n == 0 || _placed[player] is not { } ring)
            return "nothing to undo";
        var e = ring[n - 1];
        _placedCount[player] = (byte)(n - 1);
        if (world.Get(e.Body) is not { } c)
            return "that construct is gone";
        var at = -1;
        for (var i = 0; i < c.PartCount && at < 0; i++)
            if (c.Parts[i].Def == e.Def && c.Parts[i].Rot == e.Rot && c.Parts[i].Seg == e.Seg && Vector3.DistanceSquared(c.Parts[i].Pos, e.Pos) < 1e-4f)
                at = i;
        if (at < 0)
            return "that part is already gone";
        if (c.Driver != 255)
            return "someone is driving it";
        if (SalvageRefusal(c, player) is { } theirs)
            return theirs;
        c.WorldFrame(c.Parts[at].Seg, out var q, out var t);
        c.PartBounds(at, out var mn, out var mx);
        if (Reachable(eye, t + Vector3.Transform((mn + mx) * 0.5f, q), 12f) is { } far)
            return far;
        var def = world.RemovePart(c, at);
        Done.Add($"player {player}: undo {def.Name} from construct {e.Body}");
        return null;
    }

    /// <summary>The player takes the kind of part at <paramref name="part"/> of a construct in reach into their hands (creative: made; otherwise out of their inventory).</summary>
    public string? PickPart(ConstructWorld world, byte player, Vector3 eye, ushort body, int part)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (world.Get(body) is not { } c || part < 0 || part >= c.PartCount)
            return "that part is gone";
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        if (Reachable(eye, t + Vector3.Transform((mn + mx) * 0.5f, q), ConstructBuild.Reach + 3f) is { } far)
            return far;
        return TakeKind(player, c.DefOf(part));
    }

    /// <summary>The player takes a part of this kind into their hands: made in creative, out of their inventory otherwise (the pick and the recent-parts wheel).</summary>
    public string? TakeKind(byte player, ConstructPartDef def)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Held == def.Id)
            return null;
        if (!InfiniteParts && Inventory != null)
        {
            var inv = Inventory.Of(player);
            if (inv.Count(def.Id) == 0)
                return $"no {def.Name} in your inventory (salvage one)";
            ReturnHeld(player);
            if (h.Held != ConstructParts.None)
                return $"your inventory has no room for the {ConstructParts.Get(h.Held)?.Name ?? "part"} you hold";
            inv.Take(def.Id);
        }
        h.Held = def.Id;
        h.Edit = EditKind.None;
        return null;
    }
}
