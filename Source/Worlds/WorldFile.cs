using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>A save file this build can't use: damaged, or from a newer build.</summary>
public sealed class WorldFileException(string message) : Exception(message);

/// <summary>What the lobby shows of a saved world: read without unpacking the world itself.</summary>
public sealed class WorldMeta
{
    public string Name = "", MapName = "", Build = "";
    public bool Survival;
    public uint MapHash;
    public long CreatedUtc, SavedUtc; // ticks
    public double PlayedSeconds;
    public int Bodies, Parts, Players;
    public byte Death; // survival's death rules (Game.DeathByte: respawn, last checkpoint or permadeath, and keep the backpack); after Players, so an older file reads 0, a plain respawn

    public void Write(SaveWriter w)
    {
        w.Str(Name).Str(MapName).Str(Build);
        w.U8((byte)(Survival ? 1 : 0)).U32(MapHash).I64(CreatedUtc).I64(SavedUtc).F64(PlayedSeconds);
        w.I32(Bodies).I32(Parts).I32(Players);
        w.U8(Death);
    }

    public static WorldMeta Read(SaveReader r) => new()
    {
        Name = r.Str(),
        MapName = r.Str(),
        Build = r.Str(),
        Survival = r.U8() != 0,
        MapHash = r.U32(),
        CreatedUtc = r.I64(),
        SavedUtc = r.I64(),
        PlayedSeconds = r.F64(),
        Bodies = r.I32(),
        Parts = r.I32(),
        Players = r.I32(),
        Death = r.Has(1) ? r.U8() : (byte)0,
    };
}

/// <summary>
/// A world save's bytes (docs/sandbox/WORLD-SAVES.md): a magic, the format, the <see cref="WorldMeta"/> in the clear, then
/// the body, packed with Brotli and guarded by a hash. Saves name parts and materials, never run-time ids
/// (<see cref="WorldSaveWriter"/>), so a save survives parts being added.
/// </summary>
public static class WorldFile
{
    /// <summary>The format this build writes. A file with a higher one is refused and never written over.</summary>
    public const int Format = 1;

    private static ReadOnlySpan<byte> Magic => "COSWORLD"u8;

    /// <summary>The whole file for a world's body.</summary>
    public static byte[] Pack(WorldMeta meta, ReadOnlySpan<byte> body)
    {
        var head = new SaveWriter(1024);
        head.Bytes(Magic).I32(Format);
        var metaBytes = new SaveWriter(256);
        meta.Write(metaBytes);
        head.I32(metaBytes.Length).Bytes(metaBytes.Span);
        var packed = new MemoryStream(body.Length / 4 + 1024);
        using (var z = new BrotliStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(body);
        var raw = packed.GetBuffer().AsSpan(0, (int)packed.Length);
        head.I32(body.Length).I32(raw.Length).U64(Hash(raw)).Bytes(raw);
        return head.Span.ToArray();
    }

    /// <summary>The format and meta of a file's first bytes. Throws <see cref="WorldFileException"/> when they aren't a world save's.</summary>
    public static (int Format, WorldMeta Meta, int BodyAt) ReadHead(ReadOnlySpan<byte> file)
    {
        if (file.Length < 20 || !file[..8].SequenceEqual(Magic))
            throw new WorldFileException("not a world save");
        var r = new SaveReader(file[8..].ToArray());
        var format = r.I32();
        if (format > Format)
            return (format, new WorldMeta(), 0);
        var length = r.I32();
        if (length < 0 || length > 65536 || !r.Has(length))
            throw new WorldFileException("its header is cut short");
        var at = r.Pos;
        var end = r.End;
        r.End = at + length; // (the meta reads no further than its own length: a field added at its end reads as absent in an older file)
        var meta = WorldMeta.Read(r);
        r.End = end;
        r.Pos = at + length;
        return (format, meta, 8 + r.Pos);
    }

    /// <summary>The most a world's body may unpack to (a shared or damaged file can say anything; real worlds are far smaller).</summary>
    public const int MaxRawLength = 1 << 30;

    /// <summary>Unpacks a whole file's body, checking its hash. Throws <see cref="WorldFileException"/> when it is damaged or newer.</summary>
    public static byte[] Unpack(byte[] file, out WorldMeta meta, out int format)
    {
        var (fmt, m, at) = ReadHead(file);
        format = fmt;
        meta = m;
        if (fmt > Format)
            throw new WorldFileException(NewerText(fmt));
        if (file.Length < at + 16)
            throw new WorldFileException("it is cut short");
        var r = new SaveReader(file) { Pos = at };
        var rawLength = r.I32();
        var packedLength = r.I32();
        var hash = r.U64();
        if (rawLength < 0 || packedLength < 0 || packedLength != file.Length - r.Pos)
            throw new WorldFileException("it is cut short");
        if (Hash(file.AsSpan(r.Pos, packedLength)) != hash)
            throw new WorldFileException("its hash doesn't match");
        if (rawLength > MaxRawLength)
            throw new WorldFileException($"it says it unpacks to {rawLength / (1 << 20)} MB, past the {MaxRawLength >> 20} MB a world may be"); // (a damaged or made-up file: never an allocation that takes the game down)
        var body = new byte[rawLength];
        try
        {
            using var z = new BrotliStream(new MemoryStream(file, r.Pos, packedLength), CompressionMode.Decompress);
            z.ReadExactly(body);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            throw new WorldFileException($"it won't unpack ({ex.Message})");
        }
        return body;
    }

    public static string NewerText(int format) => $"saved by a newer build of the game (format {format}, this build knows {Format}): update to load it";

    /// <summary>FNV-1a over 8-byte words (a save's guard, not a secret).</summary>
    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        var h = 14695981039346656037ul ^ (ulong)data.Length;
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(data);
        foreach (var w in words)
            h = (h ^ w) * 1099511628211ul;
        foreach (var b in data[(words.Length * 8)..])
            h = (h ^ b) * 1099511628211ul;
        return h ^ h >> 29;
    }
}

/// <summary>The plain little-endian bytes of a world save: fixed-size fields, with sections a reader can skip.</summary>
public sealed class SaveWriter(int capacity = 65536)
{
    private byte[] _b = new byte[Math.Max(capacity, 16)];
    private int _n;

    public int Length => _n;
    public ReadOnlySpan<byte> Span => _b.AsSpan(0, _n);

    private Span<byte> Room(int n)
    {
        if (_n + n > _b.Length)
            Array.Resize(ref _b, Math.Max(_b.Length * 2, _n + n));
        var s = _b.AsSpan(_n, n);
        _n += n;
        return s;
    }

    public void Clear() => _n = 0;

    public SaveWriter Bytes(ReadOnlySpan<byte> v)
    {
        v.CopyTo(Room(v.Length));
        return this;
    }

    public SaveWriter U8(byte v)
    {
        Room(1)[0] = v;
        return this;
    }

    public SaveWriter U16(ushort v)
    {
        BitConverter.TryWriteBytes(Room(2), v);
        return this;
    }

    public SaveWriter I32(int v)
    {
        BitConverter.TryWriteBytes(Room(4), v);
        return this;
    }

    public SaveWriter U32(uint v)
    {
        BitConverter.TryWriteBytes(Room(4), v);
        return this;
    }

    public SaveWriter I64(long v)
    {
        BitConverter.TryWriteBytes(Room(8), v);
        return this;
    }

    public SaveWriter U64(ulong v)
    {
        BitConverter.TryWriteBytes(Room(8), v);
        return this;
    }

    public SaveWriter F32(float v)
    {
        BitConverter.TryWriteBytes(Room(4), v);
        return this;
    }

    public SaveWriter F64(double v)
    {
        BitConverter.TryWriteBytes(Room(8), v);
        return this;
    }

    public void PatchI32(int at, int v) => BitConverter.TryWriteBytes(_b.AsSpan(at, 4), v);

    public SaveWriter Vec(Vector3 v) => F32(v.X).F32(v.Y).F32(v.Z);

    public SaveWriter Quat(Quaternion q) => F32(q.X).F32(q.Y).F32(q.Z).F32(q.W);

    public SaveWriter Str(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        return I32(bytes.Length).Bytes(bytes);
    }

    /// <summary>A section: its tag, its length, then what <paramref name="body"/> writes. A reader that doesn't know the tag skips it.</summary>
    public void Section(uint tag, Action body)
    {
        U32(tag);
        var at = _n;
        I32(0);
        body();
        BitConverter.TryWriteBytes(_b.AsSpan(at, 4), _n - at - 4);
    }
}

/// <summary>Reads what <see cref="SaveWriter"/> wrote; running off the end is a <see cref="WorldFileException"/>, never a wrong value.</summary>
public sealed class SaveReader(byte[] data)
{
    public int Pos;
    public int End = data.Length;

    public bool Has(int n) => n >= 0 && Pos + n <= End;

    private ReadOnlySpan<byte> Take(int n)
    {
        if (!Has(n))
            throw new WorldFileException("it ends too soon");
        var s = data.AsSpan(Pos, n);
        Pos += n;
        return s;
    }

    public byte U8() => Take(1)[0];

    public ushort U16() => BitConverter.ToUInt16(Take(2));

    public int I32() => BitConverter.ToInt32(Take(4));

    public uint U32() => BitConverter.ToUInt32(Take(4));

    public long I64() => BitConverter.ToInt64(Take(8));

    public ulong U64() => BitConverter.ToUInt64(Take(8));

    public float F32() => BitConverter.ToSingle(Take(4));

    public double F64() => BitConverter.ToDouble(Take(8));

    public Vector3 Vec() => new(F32(), F32(), F32());

    /// <summary>How far a point in the World moved since the file was written (an older file's planetoid centre: ReadOrigin).</summary>
    public Vector3 Shift;

    /// <summary>A point in the World (not one relative to a body): <see cref="Vec"/> moved by <see cref="Shift"/>.</summary>
    public Vector3 At() => Vec() + Shift;

    public Quaternion Quat() => new(F32(), F32(), F32(), F32());

    /// <summary>The next <paramref name="n"/> bytes as their own array (a file inside a file: GameTravel.cs).</summary>
    public byte[] Bytes(int n) => n < 0 ? throw new WorldFileException("a length that can't be") : Take(n).ToArray();

    public string Str()
    {
        var n = I32();
        return Encoding.UTF8.GetString(Take(n));
    }

    /// <summary>A count that must fit the bytes left at <paramref name="bytesEach"/> each.</summary>
    public int Count(int bytesEach = 1)
    {
        var n = I32();
        if (n < 0 || !Has((int)Math.Min(n * (long)bytesEach, int.MaxValue)))
            throw new WorldFileException($"a list of {n} doesn't fit");
        return n;
    }
}
