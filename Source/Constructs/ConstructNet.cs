using System.Numerics;

namespace CallOfSlop;

/// <summary>What a <see cref="Msg.CBody"/> carries (its first byte).</summary>
public enum CBodyKind : byte
{
    Full, // one construct: parts, links, joints, pose
    Gone, // a construct removed
    Health, // part health changes: (id, part, health) each
    Clear, // every construct removed (a new match)
    Inventory, // server -> one client: that player's own inventory (protocol 81; SandboxInventory)
    Ammo, // protocol 82: rounds and fire changes: (id, part, rounds, burning) each
    Links, // protocol 83: every coupling and wire between constructs (the whole list, when it changes)
    Store, // protocol 85: one container's contents (physical storage): its handle, then ConstructStorage.Write's bytes
    Pack, // protocol 85: one player's backpack: the player id, then ConstructStorage.Write's bytes
    WorldLinks, // protocol 93: every world link and every hose off its reel (the whole list, when it changes; WriteWorldLinks)
}

/// <summary>
/// The constructs on the wire (protocol 88: a construct frozen by the physics gun, and by whom; protocol 83: the charge, power and gear in the structure and the poses, and the links
/// between constructs; protocol 82: guns, ammo boxes and burning parts, Msg.CFire; protocol 81: owners, salvage, the inventory; protocol 78: milestone 2 added logic settings and signals, joint types, wires, and joint
/// speeds and signals in the poses). Structure goes reliably and only when it changes (an edit, a break, a
/// split; and the whole set to a joiner); poses go unreliably at 20 Hz for the bodies that move, with a few sleeping ones
/// refreshed each time so a lost packet never leaves a body wrong for long. Positions of parts travel on their 1 cm grid
/// (<see cref="ConstructWorld.Quantise"/>), so a client's copy is exactly the server's.
/// </summary>
public static class ConstructNet
{
    public const int PoseEvery = 3; // ticks between pose packets (20 Hz)
    public const int SleepingPerPacket = 4;
    private const int PoseBytes = 2 + 12 + 8 + 6 + 1 + 1 + 1 + 1 + 5 + 3; // (protocol 83: charge, power flags, gear, fuel; 86: rpm, drive note)

    private static short Cm(float v) => (short)Math.Clamp(MathF.Round(v * 100), short.MinValue, short.MaxValue);

    private static float FromCm(short v) => v / 100f;

    private static ushort S16(short v) => unchecked((ushort)v);

    private static short R16(ushort v) => unchecked((short)v);

    private static byte AxisDir(Vector3 axis)
    {
        var best = (byte)0;
        var bestDot = float.MinValue;
        for (byte d = 0; d < 6; d++)
        {
            var dot = Vector3.Dot(axis, ConstructParts.DirVector(d));
            if (dot > bestDot)
            {
                bestDot = dot;
                best = d;
            }
        }
        return best;
    }

    public static void WriteFull(PacketWriter w, Construct c)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Full).U16(c.Id);
        w.U8((byte)((c.Wreck ? 1 : 0) | (c.Anchored ? 2 : 0) | (c.Asleep ? 4 : 0) | (c.Damaged ? 8 : 0) | (c.Pinned ? 16 : 0) | (c.Frozen ? 32 : 0))).U8(c.Owner).U8(c.FrozenBy); // (protocol 84: pinned; 87: frozen by the physics gun, and by whom)
        w.U8((byte)Math.Max(0, c.Root)).U8(c.Driver).U8((byte)(c.DriverSeat < 0 ? 255 : c.DriverSeat));
        w.F32(c.Fuel).F32(c.Charge).U8(PowerFlags(c)).U8((byte)c.Gear).U16(RpmOut(c)).U8((byte)c.Note); // (protocol 83: the charge, power and gear; 86: rpm, drive note)
        w.Vec(c.Pos).F32(c.Rot.X).F32(c.Rot.Y).F32(c.Rot.Z).F32(c.Rot.W).Vec(c.Vel).Vec(c.AngVel);
        w.U8((byte)c.PartCount);
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            w.U16(p.Def).U16(S16(Cm(p.Pos.X))).U16(S16(Cm(p.Pos.Y))).U16(S16(Cm(p.Pos.Z))).U8(p.Rot).U8(p.Seg)
                .U16((ushort)Math.Clamp(MathF.Ceiling(p.Health), 0, 65535)).U8((byte)(p.Broken ? 1 : 0))
                .U8(p.Setting).U8(unchecked((byte)ConstructLogic.Quantise(p.Out)));
        }
        // Protocol 85: the storage parts' store handles (their contents follow in CBodyKind.Store messages).
        var storesAt = w.Length;
        w.U8(0);
        var stores = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Store != 0)
            {
                w.U8((byte)i).U16(c.Parts[i].Store);
                stores++;
            }
        w.PatchU8(storesAt, (byte)stores);
        w.U16((ushort)c.LinkCount);
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            w.U8(l.A).U8(l.B).U8(l.SA).U8(l.SB);
        }
        w.U8((byte)c.JointCount);
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            w.U8(jt.Hinge).U8(jt.ParentSeg).U16(S16(Cm(jt.Pivot.X))).U16(S16(Cm(jt.Pivot.Y))).U16(S16(Cm(jt.Pivot.Z))).U8(AxisDir(jt.Axis))
                .F32(jt.Min).F32(jt.Max).F32(jt.Angle).F32(jt.Vel).U8((byte)((jt.Open ? 1 : 0) | (jt.Locked ? 2 : 0) | (jt.Wired ? 4 : 0))).U8((byte)jt.Type);
        }
        ConstructRoutes.Write(w, c); // (protocol 90: the routed links in place of the wires; the client rebuilds its wires from the data links)
        // Protocol 82: the parts with rounds in them or on fire (ammo boxes, guns).
        var at = w.Length;
        w.U8(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Rounds > 0 || c.Parts[i].Burn > 0 || c.Parts[i].Reload > 0)
            {
                w.U8((byte)i).U16(c.Parts[i].Rounds).U8(AmmoFlags(c.Parts[i]));
                n++;
            }
        w.PatchU8(at, (byte)n);
        ConstructConfig.Write(w, c); // protocol 89: the part settings off their defaults (the configure tool)
    }

    private static byte AmmoFlags(in CPart p) => (byte)((p.Burn > 0 ? 1 : 0) | (p.Reload > 0 ? 2 : 0));

    private static void ReadAmmo(ref CPart p, ushort rounds, byte flags)
    {
        p.Rounds = rounds;
        p.Burn = (ushort)((flags & 1) != 0 ? Math.Max((int)p.Burn, 1) : 0); // (a client only knows it burns)
        p.Reload = (ushort)((flags & 2) != 0 ? 1 : 0);
    }

    /// <summary>A batch of ammo and fire changes (the caller keeps it under the MTU: at most ~200 entries).</summary>
    public static int WriteAmmo(PacketWriter w, ConstructWorld world, List<(ushort Body, byte Part)> changes, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Ammo);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var i = from;
        for (; i < changes.Count && n < 200; i++)
        {
            var (id, part) = changes[i];
            if (world.Get(id) is not { } c || part >= c.PartCount)
                continue;
            w.U16(id).U8(part).U16(c.Parts[part].Rounds).U8(AmmoFlags(c.Parts[part]));
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>Msg.CFire (protocol 82): what the guns did this tick: each shot (body, gun part, where it ended, what it met), blast and cook-off (where, how big); since 87 a glass part shattering (where, its half size in cm).</summary>
    public static void WriteFires(PacketWriter w, ConstructWorld world)
    {
        w.Begin(Msg.CFire).U8((byte)Math.Min(world.FireCount, 255));
        for (var k = 0; k < world.FireCount && k < 255; k++)
        {
            ref var e = ref world.Fires[k];
            w.U8((byte)e.Kind);
            if (e.Kind == CFireKind.Shot)
                w.U16(e.Body).U8(e.Part).Vec(e.At).U8((byte)e.Hit);
            else if (e.Kind == CFireKind.Shatter)
                w.Vec(e.At).U8(HalfByte(e.Half.X)).U8(HalfByte(e.Half.Y)).U8(HalfByte(e.Half.Z)); // (protocol 87)
            else if (e.Kind == CFireKind.LinkSnap)
                w.Vec(e.At).U8(e.Part); // (protocol 93: the link's type)
            else
                w.Vec(e.At).U8((byte)Math.Clamp((int)MathF.Round(e.Radius * 10), 0, 255));
        }
    }

    /// <summary>A shatter's half size on the wire: centimetres, 1 to 255.</summary>
    private static byte HalfByte(float half) => (byte)Math.Clamp((int)MathF.Round(half * 100f), 1, 255);

    /// <summary>Client: a Msg.CFire (after the id byte) into <paramref name="into"/>; returns how many events.</summary>
    public static int ReadFires(ref PacketReader r, Span<CFireEvent> into)
    {
        int n = r.U8();
        var k = 0;
        for (var i = 0; i < n; i++)
        {
            var kind = (CFireKind)r.U8();
            CFireEvent e;
            if (kind == CFireKind.Shot)
                e = new CFireEvent { Kind = kind, Body = r.U16(), Part = r.U8(), At = r.Vec(), Hit = (CFireHit)r.U8() };
            else if (kind is CFireKind.Blast or CFireKind.CookOff)
                e = new CFireEvent { Kind = kind, At = r.Vec(), Radius = r.U8() / 10f };
            else if (kind == CFireKind.Shatter)
                e = new CFireEvent { Kind = kind, At = r.Vec(), Half = new Vector3(r.U8(), r.U8(), r.U8()) * 0.01f };
            else if (kind == CFireKind.LinkSnap)
                e = new CFireEvent { Kind = kind, At = r.Vec(), Part = r.U8() };
            else
                throw new MalformedPacketException($"unknown construct fire event {kind}");
            if (!float.IsFinite(e.At.X + e.At.Y + e.At.Z))
                throw new MalformedPacketException("a construct fire event that isn't a number");
            if (k < into.Length)
                into[k++] = e;
        }
        return k;
    }

    /// <summary>A construct's power on the wire: electrical, power on, the engine running, the starter failed.</summary>
    private static byte PowerFlags(Construct c) =>
        (byte)((c.Electrical ? 1 : 0) | (c.PowerOn ? 2 : 0) | (c.EngineRunning ? 4 : 0) | (c.StarterFailed ? 8 : 0));

    private static ushort RpmOut(Construct c) => (ushort)Math.Clamp(MathF.Round(c.Rpm), 0, 65535);

    private static void SetDrive(Construct c, ushort rpm, byte note)
    {
        c.Rpm = rpm;
        c.Note = note <= (byte)DriveNote.Spooling ? (DriveNote)note : DriveNote.None;
    }

    private static void SetPower(Construct c, byte flags, byte gear)
    {
        c.Electrical = (flags & 1) != 0;
        c.PowerOn = (flags & 2) != 0;
        c.EngineRunning = (flags & 4) != 0;
        c.StarterFailed = (flags & 8) != 0;
        c.Gear = gear;
        c.GearRatio = c.Transmission >= 0 && c.Transmission < c.PartCount && c.DefOf(c.Transmission) is { HasGears: true } d && gear < d.Settings.Length ? d.Settings[gear] : 1f; // (a CVT's ratio isn't sent: the HUD shows its rpm)
    }

    /// <summary>Protocol 83: every coupling and wire between constructs, as the server has them now (resolved: construct ids and part indices).</summary>
    public static void WriteLinks(PacketWriter w, ConstructWorld world)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Links).U8((byte)world.CouplingCount);
        for (var k = 0; k < world.CouplingCount; k++)
        {
            ref var cp = ref world.Couplings[k];
            w.U16(cp.BodyA).U8(cp.PartA).U16(cp.BodyB).U8(cp.PartB).U8((byte)Math.Clamp(MathF.Round(cp.Length * 20), 0, 255));
        }
        w.U8((byte)world.XWireCount);
        for (var k = 0; k < world.XWireCount; k++)
        {
            ref var x = ref world.XWires[k];
            w.U16(x.BodyFrom).U8(x.From).U16(x.BodyTo).U8(x.To);
        }
    }

    /// <summary>
    /// Protocol 93 (board item wl01): every world link (type, its ends' construct ids and parts, its nodes: a construct
    /// id, 0 for the world, a part and a position, world or the part's segment rest frame) and every hose off its reel
    /// (the reel, who holds it, the inlet it is in). Resolved: construct ids and part indices as the server has them now.
    /// </summary>
    public static void WriteWorldLinks(PacketWriter w, ConstructWorld world)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.WorldLinks).U8((byte)world.WorldLinkCount);
        for (var k = 0; k < world.WorldLinkCount; k++)
        {
            ref var l = ref world.WorldLinks[k];
            w.U8((byte)((byte)l.Type | l.Style << 4)).U16(l.BodyFrom).U8(l.From).U8(l.FromPort).U16(l.BodyTo).U8(l.To).U8(l.ToPort).U8(l.Count);
            for (var i = 0; i < l.Count; i++)
            {
                ref var n = ref world.WorldNodes[l.Start + i];
                w.U16(n.Body).U8(n.Part).Vec(n.Pos);
            }
        }
        w.U8((byte)world.HoseCount);
        for (var k = 0; k < world.HoseCount; k++)
        {
            ref var h = ref world.Hoses[k];
            w.U16(h.BodyOut).U8(h.PartOut).U8(h.Holder).U16(h.BodyIn).U8(h.PartIn);
        }
    }

    private static void ReadWorldLinks(ref PacketReader r, ConstructWorld world)
    {
        int n = r.U8();
        if (n > ConstructWorld.MaxWorldLinks)
            throw new MalformedPacketException($"{n} world links");
        world.WorldLinkCount = 0;
        world.WorldNodeCount = 0;
        for (var k = 0; k < n; k++)
        {
            var ts = r.U8();
            var l = new WLink
            {
                Type = (RouteType)(ts & 15), Style = (byte)(ts >> 4), BodyFrom = r.U16(), From = r.U8(), FromPort = r.U8(), BodyTo = r.U16(), To = r.U8(), ToPort = r.U8(), Count = r.U8(),
                Start = (ushort)world.WorldNodeCount,
            };
            if (l.Type >= RouteType.COUNT || l.Count > Construct.MaxNodesPerRoute || world.WorldNodeCount + l.Count > ConstructWorld.MaxWorldNodes)
                throw new MalformedPacketException($"world link {k} doesn't fit");
            for (var i = 0; i < l.Count; i++)
            {
                var node = new WNode { Body = r.U16(), Part = r.U8(), Pos = r.Vec() };
                if (!float.IsFinite(node.Pos.X + node.Pos.Y + node.Pos.Z))
                    throw new MalformedPacketException($"world link {k}: a node that isn't a number");
                world.WorldNodes[world.WorldNodeCount++] = node;
            }
            world.WorldLinks[world.WorldLinkCount++] = l;
        }
        int m = r.U8();
        if (m > ConstructWorld.MaxHoses)
            throw new MalformedPacketException($"{m} hoses");
        world.HoseCount = 0;
        for (var k = 0; k < m; k++)
            world.Hoses[world.HoseCount++] = new CHose { BodyOut = r.U16(), PartOut = r.U8(), Holder = r.U8(), BodyIn = r.U16(), PartIn = r.U8() };
        world.WorldLinksDirty = false;
    }

    private static void ReadLinks(ref PacketReader r, ConstructWorld world)
    {
        int n = r.U8();
        if (n > ConstructWorld.MaxCouplings)
            throw new MalformedPacketException($"{n} couplings");
        world.CouplingCount = 0;
        for (var k = 0; k < n; k++)
            world.Couplings[world.CouplingCount++] = new CCoupling { BodyA = r.U16(), PartA = r.U8(), BodyB = r.U16(), PartB = r.U8(), Length = r.U8() / 20f };
        int m = r.U8();
        if (m > ConstructWorld.MaxXWires)
            throw new MalformedPacketException($"{m} wires between constructs");
        world.XWireCount = 0;
        for (var k = 0; k < m; k++)
            world.XWires[world.XWireCount++] = new CXWire { BodyFrom = r.U16(), From = r.U8(), BodyTo = r.U16(), To = r.U8() };
        world.LinksDirty = false;
    }

    /// <summary>
    /// A player's own inventory, whole (it is small): flags (1: infinite parts), scrap, then each part kind held (id,
    /// count) and each material held (id, count; none yet). About 8 bytes plus 3 a kind.
    /// </summary>
    public static void WriteInventory(PacketWriter w, SandboxInventory inv)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Inventory).U8((byte)(inv.Infinite ? 1 : 0)).U32((uint)inv.Scrap);
        var at = w.Length;
        w.U8(0);
        var n = 0;
        for (var i = 0; i < inv.Parts.Length && n < 255; i++)
            if (inv.Parts[i] > 0)
            {
                w.U16((ushort)i).U16(inv.Parts[i]);
                n++;
            }
        w.PatchU8(at, (byte)n);
        at = w.Length;
        w.U8(0);
        n = 0;
        for (var i = 0; i < inv.Materials.Length; i++)
            if (inv.Materials[i] > 0)
            {
                w.U8((byte)i).U16(inv.Materials[i]);
                n++;
            }
        w.PatchU8(at, (byte)n);
    }

    private static void ReadInventory(ref PacketReader r, SandboxInventory? inv)
    {
        var flags = r.U8();
        var scrap = r.U32();
        int kinds = r.U8();
        if (scrap > SandboxInventory.MaxScrap)
            throw new MalformedPacketException($"an inventory with {scrap} scrap");
        inv?.Clear();
        for (var k = 0; k < kinds; k++)
        {
            var id = r.U16();
            var count = r.U16();
            if (ConstructParts.Get(id) == null || count > SandboxInventory.MaxCount)
                throw new MalformedPacketException($"an inventory of {count} of part {id}");
            if (inv != null)
                inv.Parts[id] = count;
        }
        int mats = r.U8();
        for (var k = 0; k < mats; k++)
        {
            var id = r.U8();
            var count = r.U16();
            if (id >= SandboxInventory.MaxMaterials || count > SandboxInventory.MaxCount)
                throw new MalformedPacketException($"an inventory of {count} of material {id}");
            if (inv != null)
                inv.Materials[id] = count;
        }
        if (inv == null)
            return;
        inv.Scrap = (int)scrap;
        inv.Infinite = (flags & 1) != 0;
        inv.Version++;
    }

    /// <summary>One container's contents (physical storage; protocol 85): its handle, then its slots or bin amount.</summary>
    public static void WriteStore(PacketWriter w, CStore s)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Store).U16(s.Handle);
        ConstructStorage.Write(w, s);
    }

    /// <summary>One player's backpack (protocol 85): everyone's is drawn on their back, so it goes to every client.</summary>
    public static void WritePack(PacketWriter w, byte player, CStore pack)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Pack).U8(player);
        ConstructStorage.Write(w, pack);
    }

    public static void WriteGone(PacketWriter w, ushort id) => w.Begin(Msg.CBody).U8((byte)CBodyKind.Gone).U16(id);

    public static void WriteClear(PacketWriter w) => w.Begin(Msg.CBody).U8((byte)CBodyKind.Clear);

    /// <summary>A batch of part health changes (the caller keeps it under the MTU: at most ~200 entries).</summary>
    public static int WriteHealth(PacketWriter w, ConstructWorld world, List<(ushort Body, byte Part)> changes, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Health);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var i = from;
        for (; i < changes.Count && n < 200; i++)
        {
            var (id, part) = changes[i];
            if (world.Get(id) is not { } c || part >= c.PartCount)
                continue;
            w.U16(id).U8(part).U16((ushort)Math.Clamp(MathF.Ceiling(c.Parts[part].Health), 0, 65535));
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>Reads a <see cref="Msg.CBody"/> (after the id byte) into a client's world. Bad values are refused, never trusted.</summary>
    public static void Read(ref PacketReader r, ConstructWorld world, double now, SandboxInventory? mine = null, ConstructEdits? hands = null)
    {
        var kind = (CBodyKind)r.U8();
        switch (kind)
        {
            case CBodyKind.Clear:
                world.Clear();
                world.Removed.Clear();
                break;
            case CBodyKind.Gone:
                if (world.Get(r.U16()) is { } gone)
                    world.Remove(gone);
                world.Removed.Clear();
                break;
            case CBodyKind.Health:
            {
                int n = r.U8();
                for (var k = 0; k < n; k++)
                {
                    var id = r.U16();
                    var part = r.U8();
                    var hp = r.U16();
                    if (world.Get(id) is { } c && part < c.PartCount)
                        c.Parts[part].Health = hp;
                }
                break;
            }
            case CBodyKind.Full:
                ReadFull(ref r, world, now);
                break;
            case CBodyKind.Links:
                ReadLinks(ref r, world);
                break;
            case CBodyKind.WorldLinks:
                ReadWorldLinks(ref r, world);
                break;
            case CBodyKind.Inventory:
                ReadInventory(ref r, mine);
                break;
            case CBodyKind.Pack:
            {
                var who = r.U8();
                ConstructStorage.Read(ref r, hands?.Pack(who) ?? new CStore()); // (no hands: read and dropped, a test's bare world)
                break;
            }
            case CBodyKind.Store:
            {
                var handle = r.U16();
                if (handle == 0 || handle > ConstructWorld.MaxStores)
                    throw new MalformedPacketException($"construct store {handle}");
                ConstructStorage.Read(ref r, world.Stores[handle]);
                break;
            }
            case CBodyKind.Ammo:
            {
                int n = r.U8();
                for (var k = 0; k < n; k++)
                {
                    var id = r.U16();
                    var part = r.U8();
                    var rounds = r.U16();
                    var flags = r.U8();
                    if (world.Get(id) is { } c && part < c.PartCount)
                    {
                        var was = c.Parts[part].Burn > 0;
                        ReadAmmo(ref c.Parts[part], rounds, flags);
                        c.Burning += (c.Parts[part].Burn > 0 ? 1 : 0) - (was ? 1 : 0);
                    }
                }
                break;
            }
            default:
                throw new MalformedPacketException($"unknown construct message {kind}");
        }
    }

    private static void ReadFull(ref PacketReader r, ConstructWorld world, double now)
    {
        var id = r.U16();
        var flags = r.U8();
        var owner = r.U8();
        var frozenBy = r.U8(); // (protocol 88)
        var root = r.U8();
        var driver = r.U8();
        var seat = r.U8();
        var fuel = r.F32();
        var charge = r.F32();
        var power = r.U8();
        var gear = r.U8();
        var rpm = r.U16();
        var note = r.U8();
        var pos = r.Vec();
        var rot = new Quaternion(r.F32(), r.F32(), r.F32(), r.F32());
        var vel = r.Vec();
        var ang = r.Vec();
        if (!float.IsFinite(pos.X + pos.Y + pos.Z + rot.X + rot.Y + rot.Z + rot.W) || rot.LengthSquared() < 0.5f)
            throw new MalformedPacketException("a construct pose that isn't a number");
        rot = Quaternion.Normalize(rot);
        var c = world.Get(id);
        var fresh = c == null;
        if (c == null)
        {
            c = world.Create(pos, rot, id);
            if (c == null)
                throw new MalformedPacketException("more constructs than the caps allow");
        }
        world.StructureDirty.Remove(id);
        int parts = r.U8();
        if (parts > Construct.MaxParts)
            throw new MalformedPacketException($"a construct of {parts} parts");
        c.PartCount = 0;
        for (var i = 0; i < parts; i++)
        {
            var def = r.U16();
            var p = new Vector3(FromCm(R16(r.U16())), FromCm(R16(r.U16())), FromCm(R16(r.U16())));
            var turn = r.U8();
            var seg = r.U8();
            var hp = r.U16();
            var broken = r.U8() != 0;
            var setting = r.U8();
            var signal = (sbyte)r.U8() / 10f;
            if (ConstructParts.Get(def) is not { } pd || turn >= Rot24.Count)
                throw new MalformedPacketException($"construct part {i}: unknown type {def} or turn {turn}");
            if (setting > 0 && setting >= pd.Settings.Length)
                throw new MalformedPacketException($"construct part {i}: setting {setting} of a {pd.Name}");
            c.Parts[c.PartCount++] = new CPart { Def = def, Pos = p, Rot = turn, Seg = seg, Health = hp, Broken = broken, Setting = setting, Out = signal, OutNext = signal };
        }
        int stores = r.U8();
        for (var k = 0; k < stores; k++)
        {
            var part = r.U8();
            var handle = r.U16();
            if (part >= parts || handle == 0 || handle > ConstructWorld.MaxStores || c.Parts[part].D.Role != PartRole.Storage)
                throw new MalformedPacketException($"a construct store {handle} on part {part}");
            c.Parts[part].Store = handle;
        }
        int links = r.U16();
        if (links > Construct.MaxLinks)
            throw new MalformedPacketException($"a construct of {links} links");
        c.LinkCount = 0;
        for (var k = 0; k < links; k++)
        {
            var l = new CLink { A = r.U8(), B = r.U8(), SA = r.U8(), SB = r.U8() };
            if (l.A >= parts || l.B >= parts)
                throw new MalformedPacketException("a construct link to a part that isn't there");
            c.Links[c.LinkCount++] = l;
        }
        int joints = r.U8();
        if (joints > Construct.MaxJoints)
            throw new MalformedPacketException($"a construct of {joints} joints");
        c.JointCount = 0;
        for (var j = 0; j < joints; j++)
        {
            var jt = new CJoint
            {
                Hinge = r.U8(),
                ParentSeg = r.U8(),
                Pivot = new Vector3(FromCm(R16(r.U16())), FromCm(R16(r.U16())), FromCm(R16(r.U16()))),
                Axis = ConstructParts.DirVector((byte)(r.U8() % 6)),
                Min = r.F32(),
                Max = r.F32(),
                Angle = r.F32(),
                Vel = r.F32(),
            };
            var jflags = r.U8();
            jt.Open = (jflags & 1) != 0;
            jt.Locked = (jflags & 2) != 0;
            jt.Wired = (jflags & 4) != 0;
            var type = r.U8();
            if (type > (byte)JointType.Steering)
                throw new MalformedPacketException($"a construct joint of type {type}");
            jt.Type = (JointType)type;
            if (jt.Hinge >= parts || jt.ParentSeg > j || !float.IsFinite(jt.Angle + jt.Vel) || float.IsNaN(jt.Min) || float.IsNaN(jt.Max))
                throw new MalformedPacketException("a construct joint that doesn't fit");
            jt.PrevAngle = jt.Angle;
            c.Joints[c.JointCount++] = jt;
        }
        ConstructRoutes.Read(ref r, c, parts);
        int ammo = r.U8();
        for (var k = 0; k < ammo; k++)
        {
            var part = r.U8();
            var rounds = r.U16();
            var af = r.U8();
            if (part >= parts)
                throw new MalformedPacketException("construct ammo for a part that isn't there");
            ReadAmmo(ref c.Parts[part], rounds, af);
        }
        ConstructConfig.Read(ref r, c); // protocol 89: the part settings
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Seg > c.JointCount)
                throw new MalformedPacketException("a construct part on a joint that isn't there");
        c.Wreck = (flags & 1) != 0;
        c.Damaged = (flags & 8) != 0;
        c.Pinned = (flags & 16) != 0;
        c.Frozen = (flags & 32) != 0;
        c.FrozenBy = c.Frozen ? frozenBy : (byte)255;
        c.Owner = owner;
        c.Driver = driver;
        c.DriverSeat = seat == 255 || seat >= parts ? -1 : seat;
        c.Rebuild();
        c.Anchored = (flags & 2) != 0;
        c.Asleep = (flags & 4) != 0;
        c.Root = root < parts ? root : 0;
        c.Fuel = fuel;
        c.Charge = float.IsFinite(charge) ? charge : 0;
        SetPower(c, power, gear);
        SetDrive(c, rpm, note);
        c.Vel = vel;
        c.AngVel = ang;
        if (fresh)
        {
            c.Pos = c.PrevPos = c.NetPos = c.NetFromPos = pos;
            c.Rot = c.PrevRot = c.NetRot = c.NetFromRot = rot;
        }
        else
            SetTarget(c, pos, rot, now);
    }

    private static void SetTarget(Construct c, Vector3 pos, Quaternion rot, double now)
    {
        c.NetFromPos = c.Pos;
        c.NetFromRot = c.Rot;
        c.NetPos = pos;
        c.NetRot = rot;
        c.NetAt = now;
    }

    /// <summary>
    /// Server: the poses of the bodies that move, plus a few sleeping ones in turn. Returns false when there is nothing to send.
    /// </summary>
    public static bool WritePoses(PacketWriter w, ConstructWorld world, uint tick, ref int sleepCursor)
    {
        w.Begin(Msg.CPose).U32(tick);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var budget = PacketWriter.Mtu - 8;
        var bodies = world.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var c = bodies[i];
            if ((c.Asleep || c.Anchored && c.JointCount == 0) && c.SigHash == c.SigSent)
                continue; // still, and its signals as last sent
            if (w.Length + PoseSize(c) > budget || n == 255)
                break;
            WritePose(w, c);
            n++;
        }
        for (var k = 0; k < SleepingPerPacket && bodies.Count > 0; k++)
        {
            sleepCursor = (sleepCursor + 1) % bodies.Count;
            var c = bodies[sleepCursor];
            if (!(c.Asleep || c.Anchored && c.JointCount == 0))
                continue;
            if (w.Length + PoseSize(c) > budget || n == 255)
                break;
            WritePose(w, c);
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return n > 0;
    }

    private static int SignalParts(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.IsSignal)
                n++;
        return n;
    }

    /// <summary>A joint's load against its rating in a byte: hundredths, up to 2.55 (the bar's end).</summary>
    public static byte OverByte(float over) => (byte)Math.Clamp(MathF.Round(over * 100), 0, 255);

    private static int PoseSize(Construct c) => PoseBytes + c.JointCount * 5 + 1 + (c.HasLogic ? SignalParts(c) : 0);

    private static void WritePose(PacketWriter w, Construct c)
    {
        var q = c.Rot;
        if (q.W < 0)
            q = -q;
        w.U16(c.Id).Vec(c.Pos)
            .U16(S16((short)MathF.Round(q.X * 32767))).U16(S16((short)MathF.Round(q.Y * 32767))).U16(S16((short)MathF.Round(q.Z * 32767))).U16(S16((short)MathF.Round(q.W * 32767)))
            .U16(S16(Cm(c.Vel.X / 4))).U16(S16(Cm(c.Vel.Y / 4))).U16(S16(Cm(c.Vel.Z / 4)))
            .U8(unchecked((byte)(sbyte)Math.Clamp(MathF.Round(c.Steer * 100), -127, 127)))
            .U8(c.Driver)
            .U8((byte)(c.Asleep ? 1 : 0))
            .U8((byte)MathF.Round(ConstructPower.Share(c) * 255)).U8(PowerFlags(c)).U8((byte)c.Gear).U16((ushort)Math.Clamp(MathF.Round(c.Fuel * 10), 0, 65535)) // (protocol 83)
            .U16(RpmOut(c)).U8((byte)c.Note) // (protocol 86)
            .U8((byte)c.JointCount);
        for (var j = 0; j < c.JointCount; j++)
            w.U16(S16((short)Math.Clamp(MathF.Round(c.Joints[j].Angle * 1000), short.MinValue, short.MaxValue)))
                .U16(S16((short)Math.Clamp(MathF.Round(c.Joints[j].Vel * 100), short.MinValue, short.MaxValue)))
                .U8(OverByte(c.Joints[j].Over)); // (protocol 91: its load against its rating, for the configure panel's bar and the tint)
        // The signal parts' values, in part order (only with logic: the client knows which parts they are).
        var at = w.Length;
        w.U8(0);
        if (!c.HasLogic)
            return;
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.IsSignal)
            {
                w.U8(unchecked((byte)ConstructLogic.Quantise(c.Parts[i].Out)));
                n++;
            }
        w.PatchU8(at, (byte)n);
        c.SigSent = c.SigHash;
    }

    /// <summary>Client: a <see cref="Msg.CPose"/> (after the id byte). Older packets than the last one are ignored.</summary>
    public static void ReadPoses(ref PacketReader r, ConstructWorld world, double now, ref uint lastTick)
    {
        var tick = r.U32();
        int n = r.U8();
        var stale = tick < lastTick;
        if (!stale)
            lastTick = tick;
        for (var k = 0; k < n; k++)
        {
            var id = r.U16();
            var pos = r.Vec();
            var q = new Quaternion(R16(r.U16()) / 32767f, R16(r.U16()) / 32767f, R16(r.U16()) / 32767f, R16(r.U16()) / 32767f);
            var vel = new Vector3(FromCm(R16(r.U16())), FromCm(R16(r.U16())), FromCm(R16(r.U16()))) * 4;
            var steer = (sbyte)r.U8() / 100f;
            var driver = r.U8();
            var asleep = r.U8() != 0;
            var share = r.U8() / 255f;
            var power = r.U8();
            var gear = r.U8();
            var fuel = r.U16() / 10f;
            var rpm = r.U16();
            var note = r.U8();
            int joints = r.U8();
            var c = world.Get(id);
            if (c != null && !stale)
            {
                c.Charge = share * c.ChargeCapacity;
                c.Fuel = fuel;
                SetPower(c, power, gear);
                SetDrive(c, rpm, note);
            }
            for (var j = 0; j < joints; j++)
            {
                var a = R16(r.U16()) / 1000f;
                var v = R16(r.U16()) / 100f;
                var over = r.U8() / 100f;
                if (c != null && !stale && j < c.JointCount)
                {
                    c.Joints[j].Over = over;
                    c.Joints[j].PrevAngle = c.Joints[j].Angle;
                    c.Joints[j].Angle = a;
                    c.Joints[j].Vel = v;
                }
            }
            int signals = r.U8();
            for (int k2 = 0, i = 0; k2 < signals; k2++)
            {
                var sig = (sbyte)r.U8() / 10f;
                if (c == null || stale)
                    continue;
                while (i < c.PartCount && !c.Parts[i].D.IsSignal)
                    i++;
                if (i < c.PartCount)
                {
                    c.Parts[i].Out = sig;
                    i++;
                }
            }
            if (c == null || stale || !float.IsFinite(pos.X + pos.Y + pos.Z) || q.LengthSquared() < 0.5f)
                continue;
            SetTarget(c, pos, Quaternion.Normalize(q), now);
            c.Vel = vel;
            c.Steer = steer;
            c.Driver = driver;
            c.Asleep = asleep;
            c.NetTick = tick;
        }
    }

    /// <summary>Client, every frame: each body drawn between the last two poses heard (over one pose interval).</summary>
    public static void Blend(ConstructWorld world, double now, float dt)
    {
        const double span = PoseEvery / (double)World.TickRate;
        foreach (var c in world.Bodies)
        {
            var t = (float)Math.Clamp((now - c.NetAt) / span, 0, 1.5);
            c.PrevPos = c.Pos;
            c.PrevRot = c.Rot;
            // A little past the last pose (at its velocity) so a moving body doesn't stop and start between packets.
            c.Pos = t <= 1 ? Vector3.Lerp(c.NetFromPos, c.NetPos, t) : c.NetPos + c.Vel * (float)((t - 1) * span);
            c.Rot = Quaternion.Slerp(c.NetFromRot, c.NetRot, MathF.Min(t, 1));
            var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
            for (var k = 0; k < c.WheelCount; k++)
            {
                ref var wh = ref c.Wheels[k];
                wh.Spin += Vector3.Dot(c.Vel, fwd) / wh.Radius * dt;
            }
        }
    }
}
