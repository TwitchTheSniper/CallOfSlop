namespace CallOfSlop;

/// <summary>The track tool on the wire (CActKind.Track; ConstructEditsTracks.cs).</summary>
public sealed partial class Game
{
    private string? SandboxTrackAct(Player p, ref PacketReader r, System.Numerics.Vector3 eye)
    {
        var op = (TrackOp)r.U8();
        var body = r.U16();
        if (op == TrackOp.Make)
        {
            int n = r.U8();
            if (n > Construct.MaxTrackWheels)
                return "too many wheels";
            Span<ushort> parts = stackalloc ushort[n];
            for (var k = 0; k < n; k++)
                parts[k] = r.U16();
            return SandboxHands.TrackMake(Constructs, p.Id, eye, body, parts);
        }
        int track = r.U8();
        Span<float> values = stackalloc float[4];
        if (op == TrackOp.Set)
            for (var k = 0; k < 4; k++)
            {
                values[k] = r.F32();
                if (!float.IsFinite(values[k]))
                    return "a track setting that isn't a number";
            }
        return SandboxHands.TrackEdit(Constructs, p.Id, eye, body, op, track, values);
    }

    /// <summary>Client (or host): a new track round these wheels.</summary>
    public void SandboxSendTrackMake(ushort body, ReadOnlySpan<ushort> parts)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Track).U8((byte)TrackOp.Make).U16(body).U8((byte)parts.Length);
        foreach (var p in parts)
            w.U16(p);
        SandboxSendBuilt(w);
    }

    /// <summary>Client (or host): a track comes off, is mended, or takes these settings.</summary>
    public void SandboxSendTrackEdit(ushort body, TrackOp op, int track, ReadOnlySpan<float> values = default)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Track).U8((byte)op).U16(body).U8((byte)track);
        foreach (var v in values)
            w.F32(v);
        SandboxSendBuilt(w);
    }
}
