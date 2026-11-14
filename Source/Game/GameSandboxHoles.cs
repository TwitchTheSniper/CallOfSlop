using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Server: plates that took holes by the journal (HullHoles.cs) go out as <see cref="Msg.Holes"/>, each peer swapping the same parts in
/// place, so a hit on a big ship costs its plates, not the ship's structure resent and rebuilt on every client. A body whose structure
/// is going out anyway (this tick, or held back with a big body's other edits) gets its holes with it instead.
/// </summary>
public sealed partial class Game
{
    private const int HolesPerMessage = 32;

    private void SandboxHolesFlush()
    {
        var holes = Constructs.HolesDirty;
        if (holes.Count == 0)
            return;
        if (Net.IsOffline || Net.ConnectionCount == 0)
        {
            holes.Clear();
            return;
        }
        holes.Sort(static (a, b) => a.Body != b.Body ? a.Body.CompareTo(b.Body) : a.Part.CompareTo(b.Part));
        var w = SbW;
        for (var k = 0; k < holes.Count;)
        {
            var id = holes[k].Body;
            var end = k;
            while (end < holes.Count && holes[end].Body == id)
                end++;
            if (Constructs.Get(id) is { } c && !Constructs.StructureDirty.Contains(id) && !_sbLater.Contains(id))
                for (var from = k; from < end; from += HolesPerMessage)
                {
                    var n = Math.Min(HolesPerMessage, end - from);
                    HullHoles.Write(w, c, holes, from, n);
                    Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
                }
            k = end;
        }
        holes.Clear();
    }
}
