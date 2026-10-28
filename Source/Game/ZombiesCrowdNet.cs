using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The mass horde's crowd on the network (docs/zombies/MASS-HORDE.md, "Network"; <see cref="Msg.ZCrowd"/>, protocol 71).
/// Every <see cref="CrowdTuning.NetEvery"/> ticks the server counts the crowd per network cell (count and mean heading)
/// and sends each client the cells that changed since what it was last sent (reliable ordered, so the client's copy
/// always matches the server's record of it); cells far from the client go only every
/// <see cref="CrowdTuning.NetFarEvery"/>th time. A new crowd round, a new map or a new client gets a keyframe (every
/// non-empty cell). The client fills the cells itself (<see cref="CrowdPicture"/>). Individual zombies go in
/// <see cref="Msg.ZEnts"/> as ever.
///
/// The message: kind (0 changes, 1 keyframe), the crowd round (u16); a keyframe adds cols, rows (u8 each), the cell
/// size, the grid's corner x and z and the floor height (f32 each); then the entry count (u16) and per entry the gap
/// from the last entry's cell (1 byte under 128, else 2: the top bit of the first says a second follows), the count
/// (u8) and, for a count over 0, the heading (u8: 1-255 round the compass, 0 standing).
/// </summary>
public sealed partial class Game
{
    /// <summary>What one client has been sent of the crowd: each cell's count and heading, the round, and how many times.</summary>
    public sealed class CrowdNetView
    {
        public byte[] Count = [], Head = [];
        public int Round = -1, Sends;
    }

    /// <summary>Client: the crowd as the server describes it (empty on the host, which draws the particles themselves).</summary>
    public readonly CrowdPicture CrowdSeen = new();

    /// <summary>The crowd round (a new seeding starts a new one: clients get a keyframe).</summary>
    private int _crowdRound;

    private readonly Dictionary<ulong, CrowdNetView> _crowdViews = new();
    private byte[] _crowdCount = [], _crowdHead = [];
    private readonly PacketWriter _wCrowd = new(32 * 1024);

    /// <summary>Server: crowd bytes and messages sent (log, self-test).</summary>
    public long ZCrowdBytesSent;

    public int ZCrowdPacketsSent;

    /// <summary>Most bytes one message may grow to (the rest of the changes go next time).</summary>
    public const int ZCrowdMaxBytes = 16 * 1024;

    /// <summary>Server, from <see cref="ServerSendZombies"/>: the crowd to every client, when due.</summary>
    private void ServerSendCrowd()
    {
        if (!CrowdOn || !Crowd.Ready || ServerTick % Math.Max(1, ZRules.Crowd.NetEvery) != 0)
            return; // (CrowdTick counted the cells this tick)
        foreach (var (guid, id) in _clients.Values)
        {
            if (!_crowdViews.TryGetValue(guid.G, out var view))
                _crowdViews[guid.G] = view = new CrowdNetView(); // (once per client)
            var at = Players.TryGetValue(id, out var cp) ? cp.Position : Vector3.Zero;
            WriteZCrowdFor(_wCrowd, view, at);
            Net.SendTo(guid, _wCrowd.Span, PacketReliability.ReliableOrdered);
            ZCrowdBytesSent += _wCrowd.Length;
            ZCrowdPacketsSent++;
        }
        if (_crowdViews.Count > _clients.Count)
        {
            _zGoneViews.Clear();
            foreach (var g in _crowdViews.Keys)
                if (!_clients.ContainsKey(g))
                    _zGoneViews.Add(g);
            foreach (var g in _zGoneViews)
                _crowdViews.Remove(g);
        }
    }

    /// <summary>Server: the per-cell counts and headings (every NetEvery ticks from <see cref="CrowdTick"/>, offline too: the crowd surface reads them; shared by every client's send).</summary>
    public void CrowdCount()
    {
        var cell = ZRules.Crowd.NetCell;
        var (cols, rows) = Crowd.DensitySize(cell);
        if (_crowdCount.Length != cols * rows)
        {
            _crowdCount = new byte[cols * rows];
            _crowdHead = new byte[cols * rows];
        }
        Crowd.DensityHeading(cell, _crowdCount, _crowdHead);
    }

    /// <summary>
    /// Server: the crowd for a client at <paramref name="viewer"/>, against what <paramref name="v"/> says it has: a
    /// keyframe if it has nothing of this round, else the cells that changed (far ones every few sends). Updates
    /// <paramref name="v"/>. Call <see cref="CrowdCount"/> first. No allocation once the view has its arrays.
    /// </summary>
    public void WriteZCrowdFor(PacketWriter w, CrowdNetView v, Vector3 viewer)
    {
        var cell = ZRules.Crowd.NetCell;
        var (cols, rows) = Crowd.DensitySize(cell);
        cols = Math.Min(cols, 255);
        rows = Math.Min(rows, 255);
        var n = cols * rows;
        var key = v.Round != _crowdRound || v.Count.Length != n;
        if (key)
        {
            if (v.Count.Length != n)
            {
                v.Count = new byte[n];
                v.Head = new byte[n];
            }
            else
            {
                Array.Clear(v.Count);
                Array.Clear(v.Head);
            }
            v.Round = _crowdRound;
        }
        w.Begin(Msg.ZCrowd).U8((byte)(key ? 1 : 0)).U16((ushort)_crowdRound);
        if (key)
            w.U8((byte)cols).U8((byte)rows).F32(cell).F32(Crowd.MinX).F32(Crowd.MinZ).F32(Crowd.Y);
        var countAt = w.Length;
        w.U16(0);
        var farTurn = key || v.Sends % Math.Max(1, ZRules.Crowd.NetFarEvery) == 0;
        var far2 = ZRules.Crowd.NetFarRange * ZRules.Crowd.NetFarRange;
        var last = -1;
        var entries = 0;
        for (var i = 0; i < n; i++)
        {
            var c = _crowdCount[i];
            var h = _crowdHead[i];
            if (c == v.Count[i] && (c == 0 || HeadClose(h, v.Head[i])))
                continue;
            if (!farTurn)
            {
                var dx = Crowd.MinX + (i % cols + 0.5f) * cell - viewer.X;
                var dz = Crowd.MinZ + (i / cols + 0.5f) * cell - viewer.Z;
                if (dx * dx + dz * dz > far2)
                    continue;
            }
            if (w.Length > ZCrowdMaxBytes - 8)
                break; // (the rest next time: the view only records what went)
            var gap = i - last - 1;
            if (gap < 128)
                w.U8((byte)gap);
            else
                w.U8((byte)(0x80 | (gap & 0x7F))).U8((byte)(gap >> 7));
            w.U8(c);
            if (c > 0)
                w.U8(h);
            v.Count[i] = c;
            v.Head[i] = h;
            last = i;
            entries++;
        }
        w.PatchU16(countAt, (ushort)entries);
        v.Sends++;
    }

    /// <summary>Headings within about 17 degrees count as the same (no resend for a wobble).</summary>
    private static bool HeadClose(byte a, byte b)
    {
        if (a == 0 || b == 0)
            return a == b;
        var d = Math.Abs(a - b);
        return Math.Min(d, 255 - d) <= 12;
    }

    /// <summary>Client: a <see cref="Msg.ZCrowd"/> message.</summary>
    internal void ReadZCrowd(ref PacketReader r)
    {
        if (!r.Has(3))
            return;
        var key = r.U8() == 1;
        var round = r.U16();
        var pic = CrowdSeen;
        if (key)
        {
            if (!r.Has(2 + 16))
                return;
            int cols = r.U8(), rows = r.U8();
            var cell = r.F32();
            float minX = r.F32(), minZ = r.F32(), y = r.F32();
            if (cols == 0 || rows == 0 || !(cell > 0) || !float.IsFinite(minX) || !float.IsFinite(minZ) || !float.IsFinite(y))
                return;
            pic.Reset(round, cols, rows, cell, minX, minZ, y);
        }
        else if (round != pic.Round)
            return; // (changes to a picture this client doesn't have: wait for the keyframe)
        if (!r.Has(2))
            return;
        var entries = r.U16();
        var at = -1;
        for (var e = 0; e < entries && r.Has(2); e++)
        {
            int gap = r.U8();
            if ((gap & 0x80) != 0)
            {
                if (!r.Has(1))
                    break;
                gap = (gap & 0x7F) | (r.U8() << 7);
            }
            at += gap + 1;
            if (!r.Has(1))
                break;
            var c = r.U8();
            byte h = 0;
            if (c > 0)
            {
                if (!r.Has(1))
                    break;
                h = r.U8();
            }
            pic.Set(at, c, h);
        }
        pic.Reads++;
    }

    /// <summary>Self-test: a client reads one <see cref="Msg.ZCrowd"/> message (the id byte first).</summary>
    public void SelfTestReadCrowd(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet);
        r.U8();
        ReadZCrowd(ref r);
        CrowdSeen.BytesRead += packet.Length;
    }
}
