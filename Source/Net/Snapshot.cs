using System.Numerics;

namespace CallOfSlop;

/// <summary>What a snapshot carries for one player every time ("fast" fields: movement, aim, health, weapon in hand).</summary>
public struct SnapPlayer
{
    public byte Id;
    public Vector3 Position, Velocity;
    public float Yaw, Pitch;
    public byte Health;
    public bool Alive, Grounded;
    public Stance Stance;
    public byte Weapon, Boost, MantleTicks;
    public Vector3 MantleTo;
    public byte Shield; // Last Stand shield charges left (0-3)
    public bool Regen; // health regenerating
    public uint Blood; // blood marks: hit parts, generation, last hit direction (BloodMarks; protocol 33)
}

/// <summary>Fields that rarely change ("slow"): score, loadout, attachments and perks. Sent when they change, and on a slow rotation.</summary>
public struct SnapSlow : IEquatable<SnapSlow>
{
    public byte Id;
    public ushort Kills, Deaths;
    public byte Primary, Secondary;
    public uint PrimaryKit, SecondaryKit;
    public PerkSet Perks;
    public byte FrenzyLevel; // kill frenzy 0-6 (protocol 26)
    public byte FrenzyClass; // WeaponClass of the most recent kill (low 7 bits); bit 7: the host's frenzy buffs are on (protocol 26)

    // The server compares every player's slow fields with the last ones sent, each snapshot. Without this, Equals is
    // ValueType.Equals(object): it boxes both sides and reads the fields through reflection (about 300 bytes of garbage
    // per player per snapshot, most of a server tick's allocations).
    public readonly bool Equals(SnapSlow o) =>
        Id == o.Id && Kills == o.Kills && Deaths == o.Deaths && Primary == o.Primary && Secondary == o.Secondary
        && PrimaryKit == o.PrimaryKit && SecondaryKit == o.SecondaryKit && Perks == o.Perks && FrenzyLevel == o.FrenzyLevel
        && FrenzyClass == o.FrenzyClass;

    public override readonly bool Equals(object? obj) => obj is SnapSlow o && Equals(o);

    public override readonly int GetHashCode() =>
        HashCode.Combine(HashCode.Combine(Id, Kills, Deaths, Primary, Secondary), PrimaryKit, SecondaryKit, Perks, FrenzyLevel, FrenzyClass);
}

/// <summary>The recipient's own movement at full precision (prediction reconciles against it exactly), plus its slow fields.</summary>
public struct SnapOwn
{
    public Vector3 Position, Velocity, MantleTo;
    public bool Mantling;
    public SnapSlow Slow;
}

/// <summary>The per-recipient header of every snapshot packet.</summary>
public struct SnapHeader
{
    public uint Tick, AckSeq;
    public MatchPhase Phase;
    public float TimeLeft;
    public byte Winner;
    public byte Part, Parts; // this packet is part Part (0-based) of Parts for the tick
    public uint Present; // bit per player id: who is in the game (anyone else is gone)
}

/// <summary>
/// Snapshot wire format (protocol 11; 15 adds shield charges and regen to the flags), built to fit 32 players in one ~1200-byte datagram:
///   header (20 bytes): tick, input ack, phase, time left, winner, part (low nibble) / parts (high nibble), presence mask;
///   own block: flags, then for part 0 the recipient's exact position, velocity (+ mantle target) and slow fields;
///   fast entries (24 bytes, +10 while mantling): id, position (3 x 24-bit at 1/256 m), velocity (3 x 16-bit at
///   1/100 m/s), yaw (16-bit), pitch (16-bit), health, flags (alive, grounded, stance, mantling, 2 bits of Last Stand
///   shield charges, regenerating), weapon, boost, blood marks (32-bit <see cref="BloodMarks"/> word; protocol 33);
///   slow entries (20 bytes): id, kills, deaths, primary, secondary, kits, perks, frenzy level, frenzy class (+ buffs bit; protocol 26).
/// Slow entries go out for a player while their slow fields changed in the last half second (so a lost packet is
/// covered), and every 8th snapshot for everyone (late joiners catch up in ~0.25 s). If a snapshot still doesn't fit
/// <see cref="PacketWriter.Mtu"/>, it's split into parts, each a complete packet (header + whole entries); the
/// recipient's own entry is always in part 0.
/// </summary>
public static class Snapshot
{
    public const int HeaderBytes = 1 + 4 + 4 + 1 + 4 + 1 + 1 + 4;
    public const int FastBytes = 28; // 24 + the blood marks (protocol 33)
    public const int MantleBytes = 10;
    public const int SlowBytes = 20; // 16 + frenzy level and class (protocol 26) + 2 (kits are 4 bytes each since protocol 46)
    public const int OwnBytes = 1 + 24 + SlowBytes; // +12 while mantling
    public const int MaxParts = 8;

    /// <summary>Position resolution: 1/256 m in 24 bits (+-32 km).</summary>
    public const float PosScale = 256f;

    /// <summary>Velocity resolution: 1/100 m/s in 16 bits (+-327 m/s).</summary>
    public const float VelScale = 100f;

    // ---- Quantisation ----------------------------------------------------------------------------

    private static uint Q24(float v) => (uint)Math.Clamp((int)MathF.Round(v * PosScale), -8388608, 8388607) & 0xFFFFFF;

    private static float D24(uint v) => ((int)(v << 8) >> 8) / PosScale;

    private static ushort Q16(float v, float scale) => (ushort)(short)Math.Clamp((int)MathF.Round(v * scale), short.MinValue, short.MaxValue);

    private static float D16(ushort v, float scale) => (short)v / scale;

    private static ushort QYaw(float yaw) => (ushort)((int)MathF.Round((World.WrapAngle(yaw) + MathF.PI) / MathF.Tau * 65536f) & 0xFFFF);

    private static float DYaw(ushort v) => World.WrapAngle(v / 65536f * MathF.Tau - MathF.PI);

    private const float PitchScale = 32767f / (MathF.PI / 2);

    /// <summary>What a position looks like after a trip through the snapshot (for tests and error bounds).</summary>
    public static Vector3 RoundPosition(Vector3 p) => new(D24(Q24(p.X)), D24(Q24(p.Y)), D24(Q24(p.Z)));

    // ---- Entries ---------------------------------------------------------------------------------

    public static int FastSize(in SnapPlayer p) => FastBytes + (p.MantleTicks > 0 ? MantleBytes : 0);

    public static void WriteFast(PacketWriter w, in SnapPlayer p)
    {
        var mantling = p.MantleTicks > 0;
        w.U8(p.Id)
            .U24(Q24(p.Position.X)).U24(Q24(p.Position.Y)).U24(Q24(p.Position.Z))
            .U16(Q16(p.Velocity.X, VelScale)).U16(Q16(p.Velocity.Y, VelScale)).U16(Q16(p.Velocity.Z, VelScale))
            .U16(QYaw(p.Yaw)).U16(Q16(p.Pitch, PitchScale))
            .U8(p.Health)
            .U8((byte)((p.Alive ? 1 : 0) | (p.Grounded ? 2 : 0) | ((int)p.Stance << 2) | (mantling ? 16 : 0) | (Math.Min((int)p.Shield, 3) << 5)
                       | (p.Regen ? 128 : 0)))
            .U8(p.Weapon).U8(p.Boost).U32(p.Blood);
        if (mantling)
            w.U8(p.MantleTicks).U24(Q24(p.MantleTo.X)).U24(Q24(p.MantleTo.Y)).U24(Q24(p.MantleTo.Z));
    }

    public static SnapPlayer ReadFast(ref PacketReader r)
    {
        var p = new SnapPlayer { Id = r.U8() };
        p.Position = new Vector3(D24(r.U24()), D24(r.U24()), D24(r.U24()));
        p.Velocity = new Vector3(D16(r.U16(), VelScale), D16(r.U16(), VelScale), D16(r.U16(), VelScale));
        p.Yaw = DYaw(r.U16());
        p.Pitch = D16(r.U16(), PitchScale);
        p.Health = r.U8();
        var flags = r.U8();
        p.Alive = (flags & 1) != 0;
        p.Grounded = (flags & 2) != 0;
        p.Stance = (Stance)((flags >> 2) & 3);
        p.Shield = (byte)((flags >> 5) & 3);
        p.Regen = (flags & 128) != 0;
        p.Weapon = r.U8();
        p.Boost = r.U8();
        p.Blood = r.U32();
        if ((flags & 16) != 0 && r.Has(MantleBytes))
        {
            p.MantleTicks = r.U8();
            p.MantleTo = new Vector3(D24(r.U24()), D24(r.U24()), D24(r.U24()));
        }
        return p;
    }

    public static void WriteSlow(PacketWriter w, in SnapSlow s) =>
        w.U8(s.Id).U16(s.Kills).U16(s.Deaths).U8(s.Primary).U8(s.Secondary).U32(s.PrimaryKit).U32(s.SecondaryKit)
            .U8((byte)s.Perks.Movement).U8((byte)s.Perks.Survival).U8((byte)s.Perks.Damage).U8(s.FrenzyLevel).U8(s.FrenzyClass);

    public static SnapSlow ReadSlow(ref PacketReader r) => new()
    {
        Id = r.U8(), Kills = r.U16(), Deaths = r.U16(), Primary = r.U8(), Secondary = r.U8(), PrimaryKit = r.U32(), SecondaryKit = r.U32(),
        Perks = new PerkSet((PerkId)r.U8(), (PerkId)r.U8(), (PerkId)r.U8()), FrenzyLevel = r.U8(), FrenzyClass = r.U8(),
    };

    public static void WriteHeader(PacketWriter w, in SnapHeader h) =>
        w.Begin(Msg.State).U32(h.Tick).U32(h.AckSeq).U8((byte)h.Phase).F32(h.TimeLeft).U8(h.Winner)
            .U8((byte)((h.Part & 15) | ((h.Parts & 15) << 4))).U32(h.Present);

    /// <summary>Reads the header (after the message id).</summary>
    public static SnapHeader ReadHeader(ref PacketReader r)
    {
        var h = new SnapHeader { Tick = r.U32(), AckSeq = r.U32(), Phase = (MatchPhase)r.U8(), TimeLeft = r.F32(), Winner = r.U8() };
        var parts = r.U8();
        h.Part = (byte)(parts & 15);
        h.Parts = (byte)Math.Max(1, parts >> 4);
        h.Present = r.U32();
        return h;
    }

    private static void WriteOwn(PacketWriter w, in SnapOwn? own)
    {
        if (own is not { } o)
        {
            w.U8(0);
            return;
        }
        w.U8((byte)(1 | (o.Mantling ? 2 : 0))).Vec(o.Position).Vec(o.Velocity);
        if (o.Mantling)
            w.Vec(o.MantleTo);
        WriteSlow(w, o.Slow);
    }

    private static SnapOwn? ReadOwn(ref PacketReader r)
    {
        if (!r.Has(1))
            return null;
        var flags = r.U8();
        if ((flags & 1) == 0 || !r.Has(24 + SlowBytes))
            return null;
        var o = new SnapOwn { Position = r.Vec(), Velocity = r.Vec(), Mantling = (flags & 2) != 0 };
        if (o.Mantling)
            o.MantleTo = r.Vec();
        o.Slow = ReadSlow(ref r);
        return o;
    }

    // ---- Whole packets ---------------------------------------------------------------------------

    /// <summary>A decoded snapshot packet.</summary>
    public sealed class Packet
    {
        public SnapHeader Header;
        public SnapOwn? Own;
        public readonly List<SnapPlayer> Fast = new();
        public readonly List<SnapSlow> Slow = new();
    }

    /// <summary>Decodes one snapshot packet (after the message id) into <paramref name="into"/>. False if it's too short.</summary>
    public static bool Read(ref PacketReader r, Packet into)
    {
        into.Fast.Clear();
        into.Slow.Clear();
        if (!r.Has(HeaderBytes - 1 + 1))
            return false;
        into.Header = ReadHeader(ref r);
        into.Own = ReadOwn(ref r);
        if (!r.Has(1))
            return true;
        int nf = r.U8();
        for (var i = 0; i < nf && r.Has(FastBytes); i++)
            into.Fast.Add(ReadFast(ref r));
        if (!r.Has(1))
            return true;
        int ns = r.U8();
        for (var i = 0; i < ns && r.Has(SlowBytes); i++)
            into.Slow.Add(ReadSlow(ref r));
        return true;
    }

    /// <summary>
    /// Builds the packets of one snapshot for one recipient: <paramref name="ownIndex"/> is the recipient's entry in
    /// <paramref name="fast"/> (-1 for none), written first in part 0 next to <paramref name="own"/>. Whole entries are
    /// packed into parts of at most <paramref name="budget"/> bytes. Returns the number of parts written into
    /// <paramref name="parts"/> (its writers are reused; it grows as needed).
    /// </summary>
    public static int Build(in SnapHeader header, in SnapOwn? own, IReadOnlyList<SnapPlayer> fast, int ownIndex, IReadOnlyList<SnapSlow> slow,
        List<PacketWriter> parts, int budget = PacketWriter.Mtu)
    {
        var count = 0;
        var fi = 0; // next fast entry (in order, skipping ownIndex) to place
        var si = 0;
        var ownDone = ownIndex < 0;
        while (count == 0 || fi < fast.Count || si < slow.Count || !ownDone)
        {
            if (count >= MaxParts)
                break; // can't happen with 32 players; drop the rest rather than loop
            if (parts.Count <= count)
                parts.Add(new PacketWriter());
            var w = parts[count];
            var h = header;
            h.Part = (byte)count;
            WriteHeader(w, h);
            WriteOwn(w, count == 0 ? own : null);
            var fastCountAt = w.Length;
            w.U8(0);
            var nf = 0;
            if (!ownDone)
            {
                WriteFast(w, fast[ownIndex]);
                nf++;
                ownDone = true;
            }
            while (fi < fast.Count && nf < 255)
            {
                if (fi == ownIndex)
                {
                    fi++;
                    continue;
                }
                if (w.Length + FastSize(fast[fi]) + 1 > budget && nf > 0)
                    break;
                WriteFast(w, fast[fi++]);
                nf++;
            }
            if (fi == ownIndex)
                fi++;
            w.PatchU8(fastCountAt, (byte)nf);
            var slowCountAt = w.Length;
            w.U8(0);
            var ns = 0;
            while (si < slow.Count && ns < 255 && w.Length + SlowBytes <= budget)
            {
                WriteSlow(w, slow[si++]);
                ns++;
            }
            w.PatchU8(slowCountAt, (byte)ns);
            count++;
            if (nf == 0 && ns == 0 && count > 1)
                break; // nothing fits any more (a budget smaller than one entry)
        }
        for (var i = 0; i < count; i++)
            parts[i].PatchU8(1 + 4 + 4 + 1 + 4 + 1, (byte)(i | (count << 4)));
        return count;
    }
}
