using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>
/// A big construct's structure on the wire (protocol 117; docs/sandbox/CONSTRUCTS.md, "Size classes"). A whole
/// <see cref="ConstructNet.WriteFull"/> of a 4,000-part hull is over 100 KB, and it used to go out at every edit. A body of
/// <see cref="MinParts"/> parts or more keeps its last Full on both sides (<see cref="ConstructWorld.PatchBase"/>, the same
/// bytes on the server and every client), and an edit sends a byte patch on it: what to copy from the old bytes, and the
/// new bytes between. A client rebuilds the new Full from its copy, checks it by its hash and reads it like any other. A
/// client without that base (a hash that doesn't match) asks for the body whole (<see cref="CActKind.Resync"/>). To keep one
/// base for everyone, a joiner and a resync get the base itself, not a fresh Full, and the body is marked dirty so a patch
/// brings them up to date. RakNet splits what doesn't fit a packet. Edits to a big body go at most
/// <see cref="BroadcastEvery"/> ticks apart, so a battle of breaking parts is not a Full a tick.
/// </summary>
public static class ConstructPatch
{
    public const int MinParts = 128;
    public const int BroadcastEvery = 6; // ticks (10 Hz)
    private const int Block = 16; // the old bytes are indexed every this many
    private const int MinCopy = 24; // a copy shorter than this costs more than the bytes

    public static uint Hash(ReadOnlySpan<byte> data)
    {
        var h = 2166136261u;
        foreach (var b in data)
            h = (h ^ b) * 16777619u;
        return h;
    }

    public static bool Big(Construct c) => c.PartCount >= MinParts;

    /// <summary>Writes a patch from <paramref name="old"/> to <paramref name="now"/> into <paramref name="w"/> as a whole message. Edits only: it allocates its index.</summary>
    public static void Write(PacketWriter w, ushort id, ReadOnlySpan<byte> old, ReadOnlySpan<byte> now)
    {
        w.Reserve(now.Length + 64);
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Patch).U16(id).U32(Hash(old)).U32(Hash(now)).U32((uint)now.Length);
        var index = new Dictionary<ulong, int>(old.Length / Block + 1);
        for (var k = 0; k + 8 <= old.Length; k += Block)
            index.TryAdd(MemoryMarshal.Read<ulong>(old[k..]), k);
        var lit = 0;
        var p = 0;
        void Flush(PacketWriter pw, ReadOnlySpan<byte> src, int from, int to, int copyFrom, int copyLen)
        {
            pw.U24((uint)(to - from));
            pw.Bytes(src[from..to]);
            pw.U24((uint)copyFrom).U24((uint)copyLen);
        }
        while (p + 8 <= now.Length)
        {
            if (index.TryGetValue(MemoryMarshal.Read<ulong>(now[p..]), out var at))
            {
                var a = at;
                var b = p;
                while (a > 0 && b > lit && old[a - 1] == now[b - 1])
                {
                    a--;
                    b--;
                }
                var len = old[a..].CommonPrefixLength(now[b..]);
                if (len >= MinCopy)
                {
                    Flush(w, now, lit, b, a, len);
                    p = lit = b + len;
                    continue;
                }
            }
            p++;
        }
        Flush(w, now, lit, now.Length, 0, 0);
    }

    /// <summary>Rebuilds the new bytes from <paramref name="old"/> and the patch at <paramref name="r"/> (after the hash fields), or null if they don't add up.</summary>
    public static byte[]? Apply(ref PacketReader r, ReadOnlySpan<byte> old, int length)
    {
        if (length > 16 << 20)
            throw new MalformedPacketException($"a patch of {length} bytes");
        var now = new byte[length];
        var at = 0;
        while (true)
        {
            var lit = (int)r.U24();
            if (at + lit > length)
                throw new MalformedPacketException("a patch past its end");
            r.Take(lit).CopyTo(now.AsSpan(at));
            at += lit;
            var from = (int)r.U24();
            var len = (int)r.U24();
            if (len == 0)
                break;
            if (from + len > old.Length || at + len > length)
                return null;
            old.Slice(from, len).CopyTo(now.AsSpan(at));
            at += len;
        }
        return at == length ? now : null;
    }
}
