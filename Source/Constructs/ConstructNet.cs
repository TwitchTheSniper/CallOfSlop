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
    WorldLinks, // protocol 95: every world link and every hose off its reel (the whole list, when it changes; WriteWorldLinks)
    Paint, // protocol 98: paint changes (the paint tool): (id, part or link, colour) each (ConstructPaint.WriteChanges)
    Digest, // protocol 101: the body digest (every body's id, structure hash, sleep state and a sleeping one's pose), in chunks; a client that differs asks for those bodies again (CActKind.Resync)
    Beacon, // protocol 115: server -> one client: what its beacons show it, as far as it changed (ConstructBeacons.Write)
    Aids, // protocol 128: server -> one client: its build aids' states and mirror plane (BuildAidView.Write)
    Extrude, // protocol 119: extrusions that changed length: (id, part, length in cm) each (ConstructNet.WriteExtrude)
    Patch, // protocol 121: a big body's structure as a byte patch on its last Full (ConstructPatch.cs)
    Analog, // protocol 133: Analog Levers that moved: (id, part, value, y) each (AnalogLever.cs; y, a Joystick's, since 211)
    Connectors, // protocol 134: ropes and springs: u8 0 and the whole list (a joiner, a big change), or u8 1 and a patch (ConstructNet.WriteConnectors, WriteConnectorPatch)
    Fluid, // protocol 138: Fluid Tanks that changed: u16 body, u16 part, u8 fluid, f32 fill each (ConstructFluid.cs)
    Reading, // protocol 153: displays and keypads that changed: (id, part, f32) each (LogicDisplays.cs)
    Terminal, // protocol 142: server -> one client: the Smart Terminal it stands at (u16 store handle, 0: none), then its network's stock as far as it changed (ConstructBeacons.Write)
    NoCollide, // protocol 154: the no-collide pairs, the whole list: u16 count, each u16 body, u16 part, u16 body, u16 part, u8 flags (ConstructNet.WriteNoCollides)
    Flight, // protocol 174: what a stabilizer commands, for Flight Diagnostic Panels (ConstructFlight.WriteDiagnostic)
    WebState, // protocol 181: one Browser Screen's page: u16 body, u16 part, u8 lead, u16 scroll, u8 inner, u32 tick, f32 seconds (ConstructMedia.cs)
    Finish, // protocol 192: texture changes (the texture paint tool): (id, part, material name) each (ConstructFinish.WriteChanges)
    Door, // protocol 226: event doors that started moving or settled: (id, part, flags) each (ConstructDoors.Write)
}

/// <summary>
/// The constructs on the wire (protocol 88: a construct frozen by the physics gun, and by whom; protocol 83: the charge, power and gear in the structure and the poses, and the links
/// between constructs; protocol 82: guns, ammo boxes and burning parts, Msg.CFire; protocol 81: owners, salvage, the inventory; protocol 78: milestone 2 added logic settings and signals, joint types, wires, and joint
/// speeds and signals in the poses). Structure goes reliably and only when it changes (an edit, a break, a
/// split; and the whole set to a joiner); poses go unreliably at 20 Hz for the bodies that move, with a few sleeping ones
/// refreshed each time so a lost packet never leaves a body wrong for long. Positions of parts travel on their 1 cm grid
/// (<see cref="ConstructWorld.Quantise"/>), so a client's copy is exactly the server's.
/// </summary>
public static partial class ConstructNet
{
    public const int PoseEvery = 3; // ticks between pose packets (20 Hz)
    public const int SleepingPerPacket = 4;
    private const int PoseBytes = 2 + 12 + 8 + 6 + 6 + 1 + 1 + 1 + 1 + 7 + 3 + 2 + 2; // (+ 2: protocol 117's joint window; 7: fuel as a float; + 2: thrust and smoke)
    private const int PoseJoints = 96, PoseSignals = 256; // the most of a body's joints and signals a pose carries: a bigger body's take turns (they must fit a packet) // (protocol 83: charge, power flags, gear, fuel; 86: rpm, drive note; 102: the turn rate)

    private static short Cm(float v) => (short)Math.Clamp(MathF.Round(v * 100), short.MinValue, short.MaxValue);

    private static float FromCm(short v) => v / 100f;

    private static ushort S16(short v) => unchecked((ushort)v);

    private static short R16(ushort v) => unchecked((short)v);

    /// <summary>A part's health in three bytes (a capital hull's runs to millions).</summary>
    private static uint HealthOut(float hp) => (uint)Math.Clamp(MathF.Ceiling(hp), 0, 16_777_215f);

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

    /// <summary>
    /// One construct whole. <paramref name="tick"/> is the server tick its pose is from (protocol 101): a client keeps a
    /// newer pose it already has (a Full sent again after a loss arrives late), and takes this one only when it is as new
    /// or newer; 0 means "take it" (a test's bare world).
    /// </summary>
    public static void WriteFull(PacketWriter w, Construct c, uint tick = 0)
    {
        w.Reserve(4096 + c.PartCount * 64 + c.LinkCount * 6 + c.JointCount * 64 + c.RouteCount * 16 + c.RouteNodeCount * 16);
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Full).U16(c.Id).U32(tick);
        w.U8((byte)((c.Wreck ? 1 : 0) | (c.Anchored ? 2 : 0) | (c.Asleep ? 4 : 0) | (c.Damaged ? 8 : 0) | (c.Pinned ? 16 : 0) | (c.Frozen ? 32 : 0))).U8(c.Owner).U8(c.FrozenBy); // (protocol 84: pinned; 87: frozen by the physics gun, and by whom)
        w.U16((ushort)Math.Max(0, c.Root)).U8(c.Driver).U16((ushort)(c.DriverSeat < 0 ? ushort.MaxValue : c.DriverSeat));
        w.F32(c.Fuel).F32(c.Charge).U8(PowerFlags(c)).U8((byte)c.Gear).U16(RpmOut(c)).U8((byte)c.Note).U8(ShareOut(c.ThrustShow)).U8(ShareOut(MathF.Max(c.SmokeLevel, c.BurnShow))); // (protocol 83: the charge, power and gear; 86: rpm, drive note; 117: thrust, smoke)
        w.Vec(c.Pos).F32(c.Rot.X).F32(c.Rot.Y).F32(c.Rot.Z).F32(c.Rot.W).Vec(c.Vel).Vec(c.AngVel);
        w.U16((ushort)c.PartCount);
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.D.RuntimeMade)
                Extrusion.Write(w, p.D); // (protocol 119: a piece is its key, never this machine's id)
            else
                w.U16(p.Def);
            w.CmVec(p.Pos).U8(p.Rot).U8(p.Seg)
                .U24(HealthOut(p.Health)).U8((byte)(p.Broken ? 1 : 0))
                .U8(p.Setting).U8(unchecked((byte)ConstructLogic.Quantise(p.Out)));
        }
        // Protocol 85: the storage parts' store handles (their contents follow in CBodyKind.Store messages).
        var storesAt = w.Length;
        w.U16(0);
        var stores = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Store != 0)
            {
                w.U16((ushort)i).U16(c.Parts[i].Store);
                stores++;
            }
        w.PatchU16(storesAt, (ushort)stores);
        w.U16((ushort)c.LinkCount);
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            w.U16(l.A).U16(l.B).U8(l.SA).U8(l.SB);
        }
        w.U8((byte)c.JointCount);
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            w.U16(jt.Hinge).U8(jt.ParentSeg).CmVec(jt.Pivot).U8(AxisDir(jt.Axis))
                .F32(jt.Min).F32(jt.Max).F32(jt.Angle).F32(jt.Vel).U8((byte)((jt.Open ? 1 : 0) | (jt.Locked ? 2 : 0) | (jt.Wired ? 4 : 0) | (jt.Frozen ? 8 : 0))).U8((byte)jt.Type); // (protocol 139: 8, its piece is frozen)
        }
        ConstructRoutes.Write(w, c); // (protocol 90: the routed links in place of the wires; the client rebuilds its wires from the data links)
        // Protocol 82: the parts with rounds in them or on fire (ammo boxes, guns).
        var at = w.Length;
        w.U16(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Rounds > 0 || c.Parts[i].Burn > 0 || c.Parts[i].Reload > 0)
            {
                w.U16((ushort)i).U16(c.Parts[i].Rounds).U8(AmmoFlags(c.Parts[i]));
                n++;
            }
        w.PatchU16(at, (ushort)n);
        ConstructConfig.Write(w, c); // protocol 89: the part settings off their defaults (the configure tool)
        LogicDisplays.Write(w, c); // protocol 153: what the displays show
        LogicText.Write(w, c); // ...and the signs' messages
        ConstructMedia.Write(w, c); // protocol 166: the screens' and music players' lists
        // Protocol 93: carried by a frozen cargo volume (ConstructCargo.cs): the carrier, and the pose in its volume's segment frame.
        w.U16(c.CarriedBy);
        if (c.CarriedBy != 0)
            w.U8(c.CarrySeg).Vec(c.CarryPos).F32(c.CarryRot.X).F32(c.CarryRot.Y).F32(c.CarryRot.Z).F32(c.CarryRot.W);
        // Protocol 97: the freeform welds (board item fw01): each one's joint, welded-on part and fixed pose (exact floats).
        var fwAt = w.Length;
        w.U8(0);
        var fw = 0;
        for (var j = 0; j < c.JointCount; j++)
            if (c.Joints[j].Freeform)
            {
                ref var jt = ref c.Joints[j];
                w.U8((byte)j).U16(jt.Child).Vec(jt.FixPos).F32(jt.FixRot.X).F32(jt.FixRot.Y).F32(jt.FixRot.Z).F32(jt.FixRot.W);
                fw++;
            }
        w.PatchU8(fwAt, (byte)fw);
        // Protocol 98: the paint tool's colours (board item pp01): the painted parts, then the painted links.
        ConstructPaint.WriteFull(w, c);
        ConstructTracks.Write(w, c); // (protocol 136)
        ConstructFinish.WriteFull(w, c); // (protocol 192: the texture paint tool's materials, by name)
        ConstructFuse.WriteFull(w, c); // (protocol 193: the fused units, runs of parts and their tags)
    }

    /// <summary>
    /// A hash of a construct's structure as both sides hold it (FNV-1a, no allocation): its flags that come with the
    /// structure, each part's type, grid position (cm), turn, segment and broken flag, the links and the joints' shape,
    /// and the cargo carrier. Nothing that moves or streams (pose, joint angles, signals, fuel, health) is in it.
    /// </summary>
    public static uint StructureHash(Construct c)
    {
        var h = 2166136261u;
        static void Mix(ref uint h, int v)
        {
            h = (h ^ (uint)v) * 16777619u;
        }
        Mix(ref h, (c.Wreck ? 1 : 0) | (c.Pinned ? 2 : 0) | (c.Frozen ? 4 : 0));
        Mix(ref h, c.PartCount);
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            Mix(ref h, p.D.RuntimeMade ? (int)p.D.ExHash : p.Def); // (an extrusion's id is this machine's own)
            Mix(ref h, Cm(p.Pos.X));
            Mix(ref h, Cm(p.Pos.Y));
            Mix(ref h, Cm(p.Pos.Z));
            Mix(ref h, p.Rot | p.Seg << 8 | (p.Broken ? 1 << 16 : 0));
        }
        Mix(ref h, c.LinkCount);
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            Mix(ref h, l.A | l.B << 16);
            Mix(ref h, l.SA | l.SB << 8);
        }
        Mix(ref h, c.JointCount);
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            Mix(ref h, jt.Hinge | jt.ParentSeg << 8 | (int)jt.Type << 16);
        }
        Mix(ref h, c.CarriedBy);
        for (var k = 0; k < c.TrackCount; k++)
            Mix(ref h, c.Tracks[k].Count + (c.Tracks[k].Health > 0 ? 256 : 0));
        return h;
    }

    private static byte AmmoFlags(in CPart p) => (byte)((p.Burn > 0 ? 1 : 0) | (p.Reload > 0 ? 2 : 0));

    private static void ReadAmmo(ref CPart p, ushort rounds, byte flags)
    {
        p.Rounds = rounds;
        p.Burn = (ushort)((flags & 1) != 0 ? Math.Max((int)p.Burn, 1) : 0); // (a client only knows it burns)
        p.Reload = (ushort)((flags & 2) != 0 ? 1 : 0);
    }

    /// <summary>A batch of ammo and fire changes (the caller keeps it under the MTU: at most ~200 entries).</summary>
    public static int WriteAmmo(PacketWriter w, ConstructWorld world, List<(ushort Body, ushort Part)> changes, int from)
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
            w.U16(id).U16(part).U16(c.Parts[part].Rounds).U8(AmmoFlags(c.Parts[part]));
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
                w.U16(e.Body).U16(e.Part).Vec(e.At).U8((byte)e.Hit).U8(e.Muzzle); // (since 103: which muzzle)
            else if (e.Kind == CFireKind.Shatter)
                w.Vec(e.At).U8(HalfByte(e.Half.X)).U8(HalfByte(e.Half.Y)).U8(HalfByte(e.Half.Z)); // (protocol 87)
            else if (e.Kind == CFireKind.LinkSnap)
                w.Vec(e.At).U8((byte)e.Part); // (protocol 95: the link's type)
            else if (e.Kind == CFireKind.ConnectorSnap)
                w.Vec(e.At).Vec(e.To).U8((byte)e.Part); // (protocol 134)
            else if (e.Kind == CFireKind.ShellOut)
            {
                w.U16(e.Body).U16(e.Part).U16(e.Shell).Vec(e.At).Vec(e.To).U8(e.Muzzle); // (protocol 150)
                if ((ShellKind)e.Muzzle == ShellKind.Bomb)
                    w.Vec(e.Half); // (protocol 180: its size; a designed bomb's id is this machine's own)
                if ((ShellKind)e.Muzzle >= ShellKind.Rocket)
                    w.F32(e.Thrust).F32(e.Turn).U16(e.Burn).U16(e.Target); // (protocol 178: every PC flies it alike)
                if ((ShellKind)e.Muzzle >= ShellKind.Bomb)
                {
                    w.U8(e.Ext); // (protocol 183: a designed weapon's flight, OrdnanceFlight.cs)
                    if ((e.Ext & 1) != 0)
                    {
                        if ((ShellKind)e.Muzzle < ShellKind.Rocket)
                            w.F32(e.Thrust).F32(e.Turn).U16(e.Burn).U16(e.Target);
                        if ((ShellKind)e.Muzzle != ShellKind.Bomb)
                            w.Vec(e.Half);
                        w.F32(e.AirPush).F32(e.TurnAir).F32(e.TurnBurn).F32(e.Drag).F32(e.Lift).F32(e.MinSpeed).U16(e.Wait).U16(e.Deploy).U8((byte)e.Flags).Vec(e.Aim).U32((uint)Math.Max(0, e.Life));
                    }
                }
                if (ShellLooks.Carries((ShellKind)e.Muzzle))
                    ShellLooks.Write(w, in e); // (protocol 184: the model it had on its rack, and how it hung there)
            }
            else if (e.Kind == CFireKind.RoundOut)
                w.Vec(e.At).Vec(e.To);
            else if (e.Kind == CFireKind.ShellLand)
                w.U16(e.Shell).Vec(e.At).F32(e.Radius).U8(e.Muzzle).U8(e.Ext); // (protocol 150; the look, protocol 195: MegaLook)
            else if (e.Kind == CFireKind.ShellStop)
                w.U16(e.Shell).Vec(e.At);
            else if (e.Kind == CFireKind.ShellSteer)
            {
                w.U16(e.Shell).U16(e.Target).Vec(e.At).Vec(e.To).U8(e.Ext); // (protocol 183: an aim point, a stage, a guide)
                if ((e.Ext & 1) != 0)
                    w.Vec(e.Aim);
                if ((e.Ext & 2) != 0)
                    w.F32(e.Thrust).F32(e.AirPush).U16(e.Burn).F32(e.TurnBurn).F32(e.MinSpeed).U8((byte)e.Flags);
            }
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
                e = new CFireEvent { Kind = kind, Body = r.U16(), Part = r.U16(), At = r.Vec(), Hit = (CFireHit)r.U8(), Muzzle = r.U8() };
            else if (kind is CFireKind.Blast or CFireKind.CookOff)
                e = new CFireEvent { Kind = kind, At = r.Vec(), Radius = r.U8() / 10f };
            else if (kind == CFireKind.Shatter)
                e = new CFireEvent { Kind = kind, At = r.Vec(), Half = new Vector3(r.U8(), r.U8(), r.U8()) * 0.01f };
            else if (kind == CFireKind.LinkSnap)
                e = new CFireEvent { Kind = kind, At = r.Vec(), Part = r.U8() };
            else if (kind == CFireKind.ConnectorSnap)
                e = new CFireEvent { Kind = kind, At = r.Vec(), To = r.Vec(), Part = r.U8() };
            else if (kind == CFireKind.ShellOut)
            {
                e = new CFireEvent { Kind = kind, Body = r.U16(), Part = r.U16(), Shell = r.U16(), At = r.Vec(), To = r.Vec(), Muzzle = r.U8() };
                if ((ShellKind)e.Muzzle == ShellKind.Bomb)
                    e.Half = r.Vec();
                if ((ShellKind)e.Muzzle >= ShellKind.Rocket)
                {
                    e.Thrust = r.F32();
                    e.Turn = r.F32();
                    e.Burn = r.U16();
                    e.Target = r.U16();
                }
                if ((ShellKind)e.Muzzle >= ShellKind.Bomb)
                {
                    e.Ext = r.U8();
                    if ((e.Ext & 1) != 0)
                    {
                        if ((ShellKind)e.Muzzle < ShellKind.Rocket)
                        {
                            e.Thrust = r.F32();
                            e.Turn = r.F32();
                            e.Burn = r.U16();
                            e.Target = r.U16();
                        }
                        if ((ShellKind)e.Muzzle != ShellKind.Bomb)
                            e.Half = r.Vec();
                        e.AirPush = r.F32();
                        e.TurnAir = r.F32();
                        e.TurnBurn = r.F32();
                        e.Drag = r.F32();
                        e.Lift = r.F32();
                        e.MinSpeed = r.F32();
                        e.Wait = r.U16();
                        e.Deploy = r.U16();
                        e.Flags = (ShellFlags)r.U8();
                        e.Aim = r.Vec();
                        e.Life = (int)Math.Min(r.U32(), int.MaxValue);
                        if (!float.IsFinite(e.AirPush + e.TurnAir + e.TurnBurn + e.Drag + e.Lift + e.MinSpeed + e.Aim.X + e.Aim.Y + e.Aim.Z))
                            throw new MalformedPacketException("a designed shell's flight that isn't a number");
                    }
                }
                if (ShellLooks.Carries((ShellKind)e.Muzzle))
                    ShellLooks.Read(ref r, ref e);
                if (!float.IsFinite(e.Thrust + e.Turn))
                    throw new MalformedPacketException("a shell's flight that isn't a number");
            }
            else if (kind == CFireKind.RoundOut)
                e = new CFireEvent { Kind = kind, At = r.Vec(), To = r.Vec() };
            else if (kind == CFireKind.ShellLand)
                e = new CFireEvent { Kind = kind, Shell = r.U16(), At = r.Vec(), Radius = r.F32(), Muzzle = r.U8(), Ext = r.U8() };
            else if (kind == CFireKind.ShellStop)
                e = new CFireEvent { Kind = kind, Shell = r.U16(), At = r.Vec() };
            else if (kind == CFireKind.ShellSteer)
            {
                e = new CFireEvent { Kind = kind, Shell = r.U16(), Target = r.U16(), At = r.Vec(), To = r.Vec(), Ext = r.U8() };
                if ((e.Ext & 1) != 0)
                    e.Aim = r.Vec();
                if ((e.Ext & 2) != 0)
                {
                    e.Thrust = r.F32();
                    e.AirPush = r.F32();
                    e.Burn = r.U16();
                    e.TurnBurn = r.F32();
                    e.MinSpeed = r.F32();
                    e.Flags = (ShellFlags)r.U8();
                }
                if (!float.IsFinite(e.Aim.X + e.Aim.Y + e.Aim.Z + e.Thrust + e.AirPush + e.TurnBurn + e.MinSpeed))
                    throw new MalformedPacketException("a shell's steer that isn't a number");
            }
            else
                throw new MalformedPacketException($"unknown construct fire event {kind}");
            if (!float.IsFinite(e.At.X + e.At.Y + e.At.Z + e.To.X + e.To.Y + e.To.Z))
                throw new MalformedPacketException("a construct fire event that isn't a number");
            if (k < into.Length)
                into[k++] = e;
        }
        return k;
    }

    /// <summary>A construct's power on the wire: electrical, power on, the engine running, the starter failed, the hover hold; the top three bits the engine's load (0 to 7; protocol 122: the smoke and the sound).</summary>
    internal static byte PowerFlags(Construct c) =>
        (byte)((c.Electrical ? 1 : 0) | (c.PowerOn ? 2 : 0) | (c.EngineRunning ? 4 : 0) | (c.StarterFailed ? 8 : 0) | (c.HoverOn ? 16 : 0) | (int)MathF.Round(Math.Clamp(c.EngineLoad, 0, 1) * 7) << 5);

    private static byte ShareOut(float share) => (byte)Math.Clamp(MathF.Ceiling(share * 255), 0, 255);

    private static ushort RpmOut(Construct c) => (ushort)Math.Clamp(MathF.Round(c.Rpm), 0, 65535);

    private static void SetDrive(Construct c, ushort rpm, byte note)
    {
        c.Rpm = rpm;
        c.Note = note <= (byte)DriveNote.Spooling ? (DriveNote)note : DriveNote.None;
    }

    internal static void SetPower(Construct c, byte flags, byte gear)
    {
        c.Electrical = (flags & 1) != 0;
        c.PowerOn = (flags & 2) != 0;
        c.EngineRunning = (flags & 4) != 0;
        c.StarterFailed = (flags & 8) != 0;
        c.HoverOn = (flags & 16) != 0;
        c.EngineLoad = (flags >> 5) / 7f;
        c.Gear = gear;
        c.GearRatio = c.Transmission >= 0 && c.Transmission < c.PartCount && c.DefOf(c.Transmission) is { HasGears: true } d && gear < d.Settings.Length ? d.Settings[gear] : 1f; // (a CVT's ratio isn't sent: the HUD shows its rpm)
    }

    /// <summary>Protocol 83: every coupling and wire between constructs, as the server has them now (resolved: construct ids and part indices).</summary>
    public static void WriteLinks(PacketWriter w, ConstructWorld world)
    {
        w.Reserve(16 + world.CouplingCount * 10 + world.XWireCount * 8); // (255 of each is 4.6 KB)
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Links).U8((byte)world.CouplingCount);
        for (var k = 0; k < world.CouplingCount; k++)
        {
            ref var cp = ref world.Couplings[k];
            w.U16(cp.BodyA).U16(cp.PartA).U16(cp.BodyB).U16(cp.PartB).U16((ushort)Math.Clamp(MathF.Round(cp.Length * 100), 0, 65535));
        }
        w.U8((byte)world.XWireCount);
        for (var k = 0; k < world.XWireCount; k++)
        {
            ref var x = ref world.XWires[k];
            w.U16(x.BodyFrom).U16(x.From).U16(x.BodyTo).U16(x.To);
        }
    }

    /// <summary>
    /// Protocol 93 (board item wl01): every world link (type, its ends' construct ids and parts, its nodes: a construct
    /// id, 0 for the world, a part and a position, world or the part's segment rest frame) and every hose off its reel
    /// (the reel, who holds it, the inlet it is in). Resolved: construct ids and part indices as the server has them now.
    /// </summary>
    public static void WriteWorldLinks(PacketWriter w, ConstructWorld world)
    {
        w.Reserve(64 + world.WorldLinkCount * 16 + world.WorldNodeCount * 16 + world.HoseCount * 16); // (long links outgrow the usual buffer)
        w.Begin(Msg.CBody).U8((byte)CBodyKind.WorldLinks).U8((byte)world.WorldLinkCount);
        for (var k = 0; k < world.WorldLinkCount; k++)
        {
            ref var l = ref world.WorldLinks[k];
            w.U8((byte)l.Type).U16(l.BodyFrom).U16(l.From).U8(l.FromPort).U16(l.BodyTo).U16(l.To).U8(l.ToPort).U16(l.Count);
            for (var i = 0; i < l.Count; i++)
            {
                ref var n = ref world.WorldNodes[l.Start + i];
                w.U16(n.Body).U16(n.Part).Vec(n.Pos);
            }
        }
        w.U8((byte)world.HoseCount);
        for (var k = 0; k < world.HoseCount; k++)
        {
            ref var h = ref world.Hoses[k];
            w.U16(h.BodyOut).U16(h.PartOut).U8(h.Holder).U16(h.BodyIn).U16(h.PartIn);
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
                Type = (RouteType)(ts & 15), BodyFrom = r.U16(), From = r.U16(), FromPort = r.U8(), BodyTo = r.U16(), To = r.U16(), ToPort = r.U8(), Count = r.U16(),
                Start = (ushort)world.WorldNodeCount,
            };
            if (l.Type >= RouteType.COUNT || l.Count > Construct.MaxNodesPerRoute || world.WorldNodeCount + l.Count > ConstructWorld.MaxWorldNodes)
                throw new MalformedPacketException($"world link {k} doesn't fit");
            world.EnsureWorldNodes(world.WorldNodeCount + l.Count);
            for (var i = 0; i < l.Count; i++)
            {
                var node = new WNode { Body = r.U16(), Part = r.U16(), Pos = r.Vec() };
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
            world.Hoses[world.HoseCount++] = new CHose { BodyOut = r.U16(), PartOut = r.U16(), Holder = r.U8(), BodyIn = r.U16(), PartIn = r.U16() };
        world.WorldLinksDirty = false;
    }

    private static void WriteConnector(PacketWriter w, in CConnector c) =>
        w.U16(c.Id).U8((byte)c.Kind).U8(c.Size).U16(c.A.Body).U16(c.A.Part).Vec(c.A.Local).U16(c.B.Body).U16(c.B.Part).Vec(c.B.Local).F32(c.Length).F32(c.Rating).F32(c.Stiffness).F32(c.Damping).U8(c.A.Player).U8(c.B.Player);

    private static CConnector ReadConnector(ref PacketReader r)
    {
        var c = new CConnector { Id = r.U16(), Kind = (ConnectorKind)r.U8(), Size = r.U8() };
        c.A = new ConnectorEnd { Body = r.U16(), Part = r.U16(), Local = r.Vec() };
        c.B = new ConnectorEnd { Body = r.U16(), Part = r.U16(), Local = r.Vec() };
        c.Length = r.F32();
        c.Rating = r.F32();
        c.Stiffness = r.F32();
        c.Damping = r.F32();
        c.A.Player = r.U8();
        c.B.Player = r.U8();
        if (c.Kind >= ConnectorKind.COUNT || c.Size >= ConstructWorld.ConnectorSizes.Length || !float.IsFinite(c.A.Local.X + c.A.Local.Y + c.A.Local.Z + c.B.Local.X + c.B.Local.Y + c.B.Local.Z + c.Length + c.Rating + c.Stiffness + c.Damping))
            throw new MalformedPacketException("a bad connector");
        return c;
    }

    /// <summary>Protocol 134: every rope and spring, resolved: construct ids and u16 part indices as the server has them now. A joiner gets this after the bodies.</summary>
    public static void WriteConnectors(PacketWriter w, ConstructWorld world)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Connectors).U8(0).U16((ushort)world.ConnectorCount);
        for (var k = 0; k < world.ConnectorCount; k++)
            WriteConnector(w, world.Connectors[k]);
    }

    /// <summary>Protocol 134: the connectors made or changed since the last message (those marked Queued), then the ids that went.</summary>
    public static void WriteConnectorPatch(PacketWriter w, ConstructWorld world)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Connectors).U8(1).U16((ushort)world.ConnectorsChanged);
        for (var k = 0; k < world.ConnectorCount; k++)
            if (world.Connectors[k].Queued)
                WriteConnector(w, world.Connectors[k]);
        w.U16((ushort)world.ConnectorsGone.Count);
        foreach (var id in world.ConnectorsGone)
            w.U16(id);
    }

    private static void ReadConnectors(ref PacketReader r, ConstructWorld world)
    {
        var whole = r.U8() == 0;
        int n = r.U16();
        if (n > ConstructWorld.MaxConnectors)
            throw new MalformedPacketException($"{n} connectors");
        if (whole)
            world.ClientClearConnectors();
        for (var k = 0; k < n; k++)
            world.ClientPutConnector(ReadConnector(ref r));
        if (whole)
            return;
        int m = r.U16();
        for (var k = 0; k < m; k++)
            world.ClientDropConnector(r.U16());
    }

    public static void WriteNoCollides(PacketWriter w, ConstructWorld world)
    {
        w.Reserve(16 + world.NoCollideCount * 9);
        w.Begin(Msg.CBody).U8((byte)CBodyKind.NoCollide).U16((ushort)world.NoCollideCount);
        for (var k = 0; k < world.NoCollideCount; k++)
        {
            ref var p = ref world.NoCollides[k];
            w.U16(p.A.Body).U16(p.A.Part).U16(p.B.Body).U16(p.B.Part).U8((byte)((p.A.Whole ? 1 : 0) | (p.B.Whole ? 2 : 0)));
        }
    }

    private static void ReadNoCollides(ref PacketReader r, ConstructWorld world)
    {
        int n = r.U16();
        if (n > ConstructWorld.MaxNoCollide)
            throw new MalformedPacketException($"{n} no-collide pairs");
        var list = new CNoCollide[n];
        for (var k = 0; k < n; k++)
        {
            var a = new NoCollideEnd { Body = r.U16(), Part = r.U16() };
            var b = new NoCollideEnd { Body = r.U16(), Part = r.U16() };
            var flags = r.U8();
            a.Whole = (flags & 1) != 0;
            b.Whole = (flags & 2) != 0;
            list[k] = new CNoCollide { A = a, B = b, Next = -1 };
        }
        world.ClientPutNoCollides(list);
    }

    private static void ReadLinks(ref PacketReader r, ConstructWorld world)
    {
        int n = r.U8();
        if (n > ConstructWorld.MaxCouplings)
            throw new MalformedPacketException($"{n} couplings");
        world.CouplingCount = 0;
        for (var k = 0; k < n; k++)
            world.Couplings[world.CouplingCount++] = new CCoupling { BodyA = r.U16(), PartA = r.U16(), BodyB = r.U16(), PartB = r.U16(), Length = r.U16() / 100f };
        int m = r.U8();
        if (m > ConstructWorld.MaxXWires)
            throw new MalformedPacketException($"{m} wires between constructs");
        world.XWireCount = 0;
        for (var k = 0; k < m; k++)
            world.XWires[world.XWireCount++] = new CXWire { BodyFrom = r.U16(), From = r.U16(), BodyTo = r.U16(), To = r.U16() };
        world.LinksDirty = false;
    }

    /// <summary>
    /// A player's own inventory, whole (it is small): flags (1: infinite parts), then each part kind held (id,
    /// count) and each material held (id, count). About 4 bytes plus 4 a kind (protocol 106: no scrap).
    /// </summary>
    public static void WriteInventory(PacketWriter w, SandboxInventory inv)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Inventory).U8((byte)(inv.Infinite ? 1 : 0));
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
        int kinds = r.U8();
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

    /// <summary>
    /// Extrusions that changed length (protocol 119, 121: part indices u16): up to 200 (id, part, length in cm); returns where the next packet starts. A
    /// part that isn't a piece any more goes unsent (the structure that followed says so).
    /// </summary>
    public static int WriteExtrude(PacketWriter w, ConstructWorld world, List<(ushort Body, ushort Part)> changes, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Extrude);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var i = from;
        for (; i < changes.Count && n < 200; i++)
        {
            var (id, part) = changes[i];
            if (world.Get(id) is not { } c || part >= c.PartCount || !c.DefOf(part).IsExtrusion)
                continue;
            var k = c.DefOf(part).ExKey;
            w.U16(id).U16(part).U16(k.W).U16(k.H).U16(k.L); // (protocol 143: a wheel grows in width and depth)
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>A batch of Analog Lever moves (CBodyKind.Analog): u8 count, then u16 body, u16 part, u16 value, u16 y (a Joystick's; 0 on a lever) each. Returns where it got to.</summary>
    public static int WriteAnalog(PacketWriter w, ConstructWorld world, List<(ushort Body, ushort Part)> changes, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Analog);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var i = from;
        for (; i < changes.Count && n < 200; i++)
        {
            var (id, part) = changes[i];
            if (world.Get(id) is not { } c || part >= c.PartCount || c.DefOf(part).Logic != CLogic.AnalogLever)
                continue;
            var d = c.DefOf(part);
            w.U16(id).U16(part).U16(AnalogLever.ToWire(AnalogLever.Value(c.Parts[part], d))).U16(AnalogLever.ToWire(AnalogLever.ValueY(c.Parts[part], d)));
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>
    /// A batch of Fluid Tanks that changed (CBodyKind.Fluid): u8 count, then u16 body, u16 part, u8 fluid, f32 fill, f32 hole each, going
    /// on from <paramref name="body"/> and <paramref name="tank"/>. False when there was none left to write.
    /// </summary>
    public static bool WriteFluid(PacketWriter w, ConstructWorld world, ref int body, ref int tank)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Fluid);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        for (; body < world.Bodies.Count && n < ConstructFluid.MaxEntries; body++, tank = 0)
        {
            var c = world.Bodies[body];
            if (!c.FluidStale)
                continue;
            for (; tank < c.FluidTankCount && n < ConstructFluid.MaxEntries; tank++)
            {
                if (!ConstructFluid.Current(c, tank))
                    continue;
                n++;
                var part = c.FluidTanks[tank];
                ref var p = ref c.Parts[part];
                w.U16(c.Id).U16(part).U8((byte)ConstructConfig.Get(p, p.D, CfgKey.Fluid, 0)).F32(ConstructConfig.Get(p, p.D, CfgKey.Fill, 0)).F32(p.D.IsEnvelope ? ConstructConfig.Get(p, p.D, CfgKey.Hole, 0) : 0);
            }
            if (tank < c.FluidTankCount)
                break; // (the rest of its tanks in the next packet)
            c.FluidStale = false;
        }
        w.PatchU8(countAt, (byte)n);
        return n > 0;
    }

    /// <summary>A batch of part health changes (the caller keeps it under the MTU: at most ~200 entries).</summary>
    public static int WriteHealth(PacketWriter w, ConstructWorld world, List<(ushort Body, ushort Part)> changes, int from)
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
            w.U16(id).U16(part).U24(HealthOut(c.Parts[part].Health));
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>Reads a <see cref="Msg.CBody"/> (after the id byte) into a client's world. Bad values are refused, never trusted.</summary>
    public static void Read(ref PacketReader r, ConstructWorld world, double now, SandboxInventory? mine = null, ConstructEdits? hands = null, BeaconView? beacon = null, BuildAidView? aids = null, TerminalView? terminal = null)
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
                    var part = r.U16();
                    var hp = r.U24();
                    if (world.Get(id) is { } c && part < c.PartCount)
                    {
                        ConstructEngine.HealthChanging(c, part, hp); // (a modular engine's cylinder starting to misfire)
                        c.Parts[part].Health = hp;
                        if (c.Dirty)
                            c.Rebuild();
                    }
                }
                break;
            }
            case CBodyKind.Full:
            {
                var start = r.Pos;
                ReadFull(ref r, world, now);
                KeepBase(world, r.Since(start));
                break;
            }
            case CBodyKind.Flight:
                ConstructFlight.ReadDiagnostic(ref r, world);
                break;
            case CBodyKind.WebState:
                ConstructMedia.ReadWeb(ref r, world);
                break;
            case CBodyKind.Reading:
                LogicDisplays.ReadChanges(ref r, world);
                break;
            case CBodyKind.Door:
                ConstructDoors.Read(ref r, world);
                break;
            case CBodyKind.Analog:
            {
                int n = r.U8();
                for (var k = 0; k < n; k++)
                {
                    var id = r.U16();
                    var part = r.U16();
                    var value = r.U16();
                    var y = r.U16();
                    if (world.Get(id) is { } c && part < c.PartCount)
                        AnalogLever.SetValue(ref c.Parts[part], c.DefOf(part), AnalogLever.FromWire(value), AnalogLever.FromWire(y));
                }
                break;
            }
            case CBodyKind.Fluid:
            {
                int n = r.U8();
                Construct? last = null;
                for (var k = 0; k < n; k++)
                {
                    var id = r.U16();
                    var part = r.U16();
                    var fluid = r.U8();
                    var fill = r.F32();
                    var hole = r.F32();
                    if (world.Get(id) is not { } c || part >= c.PartCount || c.DefOf(part).Role != PartRole.FluidTank)
                        continue;
                    ConstructFluid.Apply(c, part, fluid, fill, hole);
                    if (c.CellCount > 0)
                        ConstructLift.Refresh(c);
                    world.LoadChanged |= c.HasTankVolume;
                    if (last != null && last != c)
                        ConstructFluid.Refresh(last);
                    last = c;
                }
                if (last != null)
                    ConstructFluid.Refresh(last);
                break;
            }
            case CBodyKind.Patch:
                ReadPatch(ref r, world, now);
                break;
            case CBodyKind.Extrude:
            {
                int n = r.U8();
                for (var k = 0; k < n; k++)
                {
                    var id = r.U16();
                    var part = r.U16();
                    var wide = r.U16();
                    var tall = r.U16();
                    var cm = r.U16();
                    if (world.Get(id) is { } c && part < c.PartCount && c.DefOf(part) is { IsExtrusion: true } was)
                    {
                        if (Extrusion.Def(was.ExKey with { W = wide, H = tall, L = cm }) is not { } longer)
                            throw new MalformedPacketException($"an extrusion of {wide} by {tall} by {cm} cm");
                        c.Parts[part].Def = longer.Id;
                        world.ShapeChanged(c);
                    }
                }
                break;
            }
            case CBodyKind.Links:
                ReadLinks(ref r, world);
                break;
            case CBodyKind.WorldLinks:
                ReadWorldLinks(ref r, world);
                break;
            case CBodyKind.Connectors:
                ReadConnectors(ref r, world);
                break;
            case CBodyKind.NoCollide:
                ReadNoCollides(ref r, world);
                break;
            case CBodyKind.Inventory:
                ReadInventory(ref r, mine);
                break;
            case CBodyKind.Aids:
                aids?.Read(ref r);
                break;
            case CBodyKind.Beacon:
                ConstructBeacons.Read(ref r, beacon);
                break;
            case CBodyKind.Terminal:
            {
                var at = r.U16();
                if (at > ConstructWorld.MaxStores)
                    throw new MalformedPacketException($"a terminal of store {at}");
                terminal?.Heard(at);
                ConstructBeacons.Read(ref r, terminal?.Stock);
                break;
            }
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
                world.LoadChanged = true;
                break;
            }
            case CBodyKind.Paint:
                ConstructPaint.ReadChanges(ref r, world);
                break;
            case CBodyKind.Finish:
                ConstructFinish.ReadChanges(ref r, world);
                break;
            case CBodyKind.Digest:
                ReadDigest(ref r, world);
                break;
            case CBodyKind.Ammo:
            {
                int n = r.U8();
                for (var k = 0; k < n; k++)
                {
                    var id = r.U16();
                    var part = r.U16();
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
        var tick = r.U32(); // (protocol 101: the server tick of the pose)
        var flags = r.U8();
        var owner = r.U8();
        var frozenBy = r.U8(); // (protocol 88)
        var root = r.U16();
        var driver = r.U8();
        var seat = r.U16();
        var fuel = r.F32();
        var charge = r.F32();
        var power = r.U8();
        var gear = r.U8();
        var rpm = r.U16();
        var note = r.U8();
        var thrust = r.U8() / 255f;
        var smoke = r.U8() / 255f;
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
        int parts = r.U16();
        if (parts > Construct.MaxParts)
            throw new MalformedPacketException($"a construct of {parts} parts");
        c.ReserveParts(parts);
        c.PartCount = 0;
        for (var i = 0; i < parts; i++)
        {
            var def = r.U16();
            if (def == Extrusion.WireMark)
                def = Extrusion.Read(ref r);
            var p = r.CmVec();
            var turn = r.U8();
            var seg = r.U8();
            var hp = r.U24();
            var broken = r.U8() != 0;
            var setting = r.U8();
            var signal = (sbyte)r.U8() / 10f;
            if (ConstructParts.Get(def) is not { } pd || turn >= Rot24.Count)
                throw new MalformedPacketException($"construct part {i}: unknown type {def} or turn {turn}");
            if (setting > 0 && setting >= pd.Settings.Length)
                throw new MalformedPacketException($"construct part {i}: setting {setting} of a {pd.Name}");
            c.Parts[c.PartCount++] = new CPart { Def = def, Pos = p, Rot = turn, Seg = seg, Health = hp, Broken = broken, Setting = setting, Out = signal, OutNext = signal };
        }
        int stores = r.U16();
        for (var k = 0; k < stores; k++)
        {
            var part = r.U16();
            var handle = r.U16();
            if (part >= parts || handle == 0 || handle > ConstructWorld.MaxStores || c.Parts[part].D.Role != PartRole.Storage)
                throw new MalformedPacketException($"a construct store {handle} on part {part}");
            c.Parts[part].Store = handle;
        }
        int links = r.U16();
        if (links > Construct.MaxLinks)
            throw new MalformedPacketException($"a construct of {links} links");
        c.ReserveLinks(links);
        c.LinkCount = 0;
        c.LinksChanged();
        for (var k = 0; k < links; k++)
        {
            var l = new CLink { A = r.U16(), B = r.U16(), SA = r.U8(), SB = r.U8() };
            if (l.A >= parts || l.B >= parts)
                throw new MalformedPacketException("a construct link to a part that isn't there");
            c.Links[c.LinkCount++] = l;
        }
        int joints = r.U8();
        if (joints > Construct.MaxJoints)
            throw new MalformedPacketException($"a construct of {joints} joints");
        c.ReserveJoints(joints);
        c.JointCount = 0;
        for (var j = 0; j < joints; j++)
        {
            var jt = new CJoint
            {
                Hinge = r.U16(),
                ParentSeg = r.U8(),
                Pivot = r.CmVec(),
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
            jt.Frozen = (jflags & 8) != 0;
            var type = r.U8();
            if (type > (byte)JointType.Swivel)
                throw new MalformedPacketException($"a construct joint of type {type}");
            jt.Type = (JointType)type;
            if (jt.Hinge >= parts || jt.ParentSeg > j || !float.IsFinite(jt.Angle + jt.Vel) || float.IsNaN(jt.Min) || float.IsNaN(jt.Max))
                throw new MalformedPacketException("a construct joint that doesn't fit");
            jt.PrevAngle = jt.NetShown = jt.NetFrom = jt.NetDrawn = jt.Angle;
            jt.NetT = 1;
            c.Joints[c.JointCount++] = jt;
        }
        ConstructRoutes.Read(ref r, c, parts);
        int ammo = r.U16();
        for (var k = 0; k < ammo; k++)
        {
            var part = r.U16();
            var rounds = r.U16();
            var af = r.U8();
            if (part >= parts)
                throw new MalformedPacketException("construct ammo for a part that isn't there");
            ReadAmmo(ref c.Parts[part], rounds, af);
        }
        ConstructConfig.Read(ref r, c); // protocol 89: the part settings
        LogicDisplays.Read(ref r, c); // protocol 153
        LogicText.Read(ref r, c);
        ConstructMedia.Read(ref r, c); // protocol 166
        c.CarriedBy = r.U16(); // protocol 93: carried by a frozen cargo volume
        if (c.CarriedBy != 0)
        {
            c.CarrySeg = r.U8();
            c.CarryPos = r.Vec();
            var cr = new Quaternion(r.F32(), r.F32(), r.F32(), r.F32());
            if (c.CarriedBy == id || !float.IsFinite(c.CarryPos.X + c.CarryPos.Y + c.CarryPos.Z + cr.X + cr.Y + cr.Z + cr.W) || cr.LengthSquared() < 0.5f || c.CarryPos.Length() > 1000)
                throw new MalformedPacketException("a carried construct's pose that doesn't fit");
            c.CarryRot = Quaternion.Normalize(cr);
        }
        int welds = r.U8(); // protocol 97: the freeform welds
        var posed = 0;
        for (var k = 0; k < welds; k++)
        {
            int j = r.U8();
            var child = r.U16();
            var fp = r.Vec();
            var fq = new Quaternion(r.F32(), r.F32(), r.F32(), r.F32());
            if (j >= c.JointCount || c.Joints[j].Type != JointType.Fixed || child >= parts || c.Parts[child].Seg != j + 1
                || !float.IsFinite(fp.X + fp.Y + fp.Z + fq.X + fq.Y + fq.Z + fq.W) || fq.LengthSquared() < 0.5f || fp.Length() > 2000)
                throw new MalformedPacketException("a freeform weld that doesn't fit");
            c.Joints[j].Child = child;
            c.Joints[j].FixPos = fp;
            c.Joints[j].FixRot = Quaternion.Normalize(fq);
            posed++;
        }
        for (var j = 0; j < c.JointCount; j++)
            if (c.Joints[j].Freeform)
                posed--;
        if (posed != 0)
            throw new MalformedPacketException("a freeform weld without its pose");
        ConstructPaint.ReadFull(ref r, c); // protocol 98: the paint
        ConstructTracks.Read(ref r, c, parts); // protocol 136: the tracks
        ConstructFinish.ReadFull(ref r, c); // protocol 192: the texture paint tool's materials
        ConstructFuse.ReadFull(ref r, c); // protocol 193: the fused units
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Seg > c.JointCount)
                throw new MalformedPacketException("a construct part on a joint that isn't there");
        c.Wreck = (flags & 1) != 0;
        c.Damaged = (flags & 8) != 0;
        c.Pinned = (flags & 16) != 0;
        c.Frozen = (flags & 32) != 0;
        c.FrozenBy = frozenBy;
        c.Owner = owner;
        c.Driver = driver;
        c.DriverSeat = seat >= parts ? -1 : seat;
        c.Rebuild();
        c.Anchored = (flags & 2) != 0;
        var newer = fresh || tick == 0 || tick >= c.NetTick; // (an older Full keeps the newer pose, and its sleep state, the client has)
        if (newer)
            c.Asleep = (flags & 4) != 0;
        c.Root = root < parts ? root : 0;
        c.Fuel = fuel;
        c.Charge = float.IsFinite(charge) ? charge : 0;
        SetPower(c, power, gear);
        SetDrive(c, rpm, note);
        c.ThrustShow = thrust;
        c.SmokeLevel = smoke;
        if (fresh)
        {
            c.Pos = c.PrevPos = c.NetPos = c.NetFromPos = pos;
            c.Rot = c.PrevRot = c.NetRot = c.NetFromRot = rot;
            c.NetHistReset();
            if (tick != 0)
                c.NetHistAdd(tick, pos, rot);
        }
        else if (newer)
            SetTarget(c, pos, rot, now, tick);
        if (newer)
        {
            c.Vel = vel;
            c.AngVel = ang;
            c.NetTick = Math.Max(c.NetTick, tick);
        }
    }

    /// <summary>Client: keeps a big body's Full (its id is its first two bytes) as the base of the next patch.</summary>
    private static void KeepBase(ConstructWorld world, ReadOnlySpan<byte> full)
    {
        var id = (ushort)(full[0] | full[1] << 8);
        if (world.Get(id) is { } c && ConstructPatch.Big(c))
            world.PatchBase[id] = full.ToArray();
        else
            world.PatchBase.Remove(id);
    }

    private static void ReadPatch(ref PacketReader r, ConstructWorld world, double now)
    {
        var id = r.U16();
        var baseHash = r.U32();
        var newHash = r.U32();
        var length = (int)r.U32();
        byte[]? made = null;
        if (world.PatchBase.TryGetValue(id, out var old) && ConstructPatch.Hash(old) == baseHash)
            made = ConstructPatch.Apply(ref r, old, length);
        if (made == null || ConstructPatch.Hash(made) != newHash)
        {
            // Not the base the server has: ask for the body (the digest's request; a joiner's copy arrives ordered before any patch, so this is rare).
            while (r.Remaining > 0)
                r.Take(r.Remaining);
            if (world.ResyncCount < world.Resync.Length)
                world.Resync[world.ResyncCount++] = id;
            world.ResyncReady = true;
            world.PatchMisses++;
            return;
        }
        var inner = new PacketReader(made);
        ReadFull(ref inner, world, now);
        if (world.Get(id) is { } c && ConstructPatch.Big(c))
            world.PatchBase[id] = made;
        else
            world.PatchBase.Remove(id);
        world.PatchesRead++;
    }

    private static void SetTarget(Construct c, Vector3 pos, Quaternion rot, double now, uint tick)
    {
        c.NetFromPos = c.Pos;
        c.NetFromRot = c.Rot;
        c.NetPos = pos;
        c.NetRot = rot;
        c.NetAt = now;
        if (tick == 0)
            c.NetHistReset(); // (a pose with no tick: drawn the old way, from where it is, until ticked poses come again)
        else
            c.NetHistAdd(tick, pos, rot);
    }

    /// <summary>Ticks between body digests (2 s; protocol 101).</summary>
    public const int DigestEvery = 120;

    private const int DigestSleeping = 2 + 4 + 1 + 12 + 8, DigestAwake = 2 + 4 + 1;

    /// <summary>
    /// Server: one chunk of the body digest (protocol 101), from body <paramref name="from"/>; returns where the next
    /// chunk starts (the body count when this was the last). Sent reliably and in order with the structure messages, so
    /// a client holds exactly the bodies a digest lists when it reads it: each one's id, structure hash
    /// (<see cref="StructureHash"/>) and sleep state, and a sleeping one's pose (exact position, rotation as a pose
    /// sends it). Moving bodies' poses stream at 20 Hz anyway. A chunk says whether it is the first and the last.
    /// </summary>
    public static int WriteDigest(PacketWriter w, ConstructWorld world, uint tick, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Digest).U32(tick);
        var flagsAt = w.Length;
        w.U8(0);
        var countAt = w.Length;
        w.U8(0);
        var bodies = world.Bodies;
        var n = 0;
        var i = from;
        for (; i < bodies.Count && n < 255; i++)
        {
            var c = bodies[i];
            var asleep = Still(c);
            if (w.Length + (asleep ? DigestSleeping : DigestAwake) > PacketWriter.Mtu - 8)
                break;
            w.U16(c.Id).U32(StructureHash(c)).U8((byte)(asleep ? 1 : 0));
            if (asleep)
            {
                var q = c.Rot;
                if (q.W < 0)
                    q = -q;
                w.Vec(c.Pos).U16(S16((short)MathF.Round(q.X * 32767))).U16(S16((short)MathF.Round(q.Y * 32767))).U16(S16((short)MathF.Round(q.Z * 32767))).U16(S16((short)MathF.Round(q.W * 32767)));
            }
            n++;
        }
        w.PatchU8(flagsAt, (byte)((from == 0 ? 1 : 0) | (i >= bodies.Count ? 2 : 0)));
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>
    /// Client: a digest chunk. A body it lists that this client lacks, holds with another structure, or holds asleep
    /// somewhere else (over 2 cm or 1 degree off, or awake; unless a newer pose came since) goes on
    /// <see cref="ConstructWorld.Resync"/>; at the last chunk, a body no chunk listed (the host has no such body any
    /// more) goes, and <see cref="ConstructWorld.ResyncReady"/> says the request can go to the server.
    /// </summary>
    private static void ReadDigest(ref PacketReader r, ConstructWorld world)
    {
        var tick = r.U32();
        var flags = r.U8();
        int n = r.U8();
        if ((flags & 1) != 0)
        {
            world.DigestRound++;
            world.ResyncCount = 0;
        }
        for (var k = 0; k < n; k++)
        {
            var id = r.U16();
            var hash = r.U32();
            var asleep = (r.U8() & 1) != 0;
            var pos = Vector3.Zero;
            var rot = Quaternion.Identity;
            if (asleep)
            {
                pos = r.Vec();
                rot = new Quaternion(R16(r.U16()) / 32767f, R16(r.U16()) / 32767f, R16(r.U16()) / 32767f, R16(r.U16()) / 32767f);
                if (!float.IsFinite(pos.X + pos.Y + pos.Z) || rot.LengthSquared() < 0.5f)
                    throw new MalformedPacketException("a body digest pose that isn't a number");
                rot = Quaternion.Normalize(rot);
            }
            var c = world.Get(id);
            var wrong = c == null || StructureHash(c) != hash;
            if (c != null)
            {
                c.DigestRound = world.DigestRound;
                if (!wrong && asleep && c.NetTick <= tick)
                    wrong = !c.Asleep || Vector3.DistanceSquared(c.NetPos, pos) > 0.02f * 0.02f || MathF.Abs(Quaternion.Dot(c.NetRot, rot)) < 0.99996f; // (2 cm, about 1 degree)
            }
            if (wrong && world.ResyncCount < world.Resync.Length)
                world.Resync[world.ResyncCount++] = id;
        }
        if ((flags & 2) == 0)
            return;
        for (var i = world.Bodies.Count - 1; i >= 0; i--)
            if (world.Bodies[i].DigestRound != world.DigestRound)
                world.Remove(world.Bodies[i]);
        world.Removed.Clear();
        world.ResyncReady = true;
    }

    /// <summary>cj01: a moving body someone holds with the physics gun or drives: in every pose packet.</summary>
    public static bool Priority(Construct c) => (c.GrabbedBy != 255 || c.Driver != 255 || c.JointHeld > 0 || c.NudgeBy != 255) && !Still(c);

    /// <summary>A body that needs no pose this packet: asleep or anchored without joints, its signals as last sent, its final pose sent enough times.</summary>
    private static bool Still(Construct c) => (c.Asleep || c.Anchored && c.JointCount == 0) && !ConstructPhysics.TreadMoving(c) && c.NudgeBy == 255; // (cs03: an anchored base turning its wheels sends its rpm: clients turn them)

    /// <summary>
    /// Server: the poses of the bodies that move, plus a few sleeping ones in turn. Returns false when there is nothing
    /// to send. More moving bodies than one packet holds take turns (<see cref="ConstructWorld.PoseCursor"/>: the next
    /// packet starts at the first one left out), and room for the sleeping ones is kept, so no body goes without a pose
    /// for long however many move (before protocol 101 the packet always started at the first body, and the bodies
    /// past what one packet holds, the newest ones, got none while the others moved). A body that goes still sends its
    /// final pose <see cref="StillRepeats"/> more times in a row, so one lost packet doesn't leave it where it was.
    /// </summary>
    public static bool WritePoses(PacketWriter w, ConstructWorld world, uint tick, ref int sleepCursor)
    {
        w.Begin(Msg.CPose).U32(tick);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var budget = PacketWriter.Mtu - 8;
        var bodies = world.Bodies;
        var count = bodies.Count;
        var sleeping = 0;
        for (var i = 0; i < count; i++)
            if (Still(bodies[i]))
                sleeping++;
        var moveBudget = budget - Math.Min(sleeping, SleepingPerPacket) * (PoseBytes + 2); // (room kept for the sleeping ones' turn)
        // cj01: what a player holds with the physics gun or drives goes in every packet, first (its holder and everyone
        // watching see it move most; taking turns with the rest made its poses arrive unevenly).
        for (var i = 0; i < count; i++)
        {
            var c = bodies[i];
            if (!Priority(c) || w.Length + PoseSize(c) > moveBudget || n == 255)
                continue;
            WritePose(w, c);
            n++;
            c.PoseRepeats = StillRepeats;
        }
        var start = count == 0 ? 0 : (world.PoseCursor % count + count) % count;
        var next = start;
        for (var k = 0; k < count; k++)
        {
            var i = (start + k) % count;
            var c = bodies[i];
            if (Priority(c))
                continue; // (sent above)
            var still = Still(c);
            if (still && c.SigHash == c.SigSent && c.PoseRepeats == 0)
                continue; // still, and its signals and final pose as last sent
            if (w.Length + PoseSize(c) > moveBudget || n == 255)
            {
                next = i; // (the next packet starts here)
                break;
            }
            WritePose(w, c);
            n++;
            c.PoseRepeats = still ? (byte)Math.Max(0, c.PoseRepeats - 1) : StillRepeats;
        }
        world.PoseCursor = next;
        for (var k = 0; k < SleepingPerPacket && count > 0; k++)
        {
            sleepCursor = (sleepCursor + 1) % count;
            var c = bodies[sleepCursor];
            if (!Still(c))
                continue;
            if (w.Length + PoseSize(c) > budget || n == 255)
                break;
            WritePose(w, c);
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return n > 0;
    }

    /// <summary>How many more times a body's pose goes after it went still (its final pose, and that it sleeps).</summary>
    public const byte StillRepeats = 3;

    // ---- Big worlds: interest and streaming (board item cl01) --------------------------------------------------------

    /// <summary>Pose packets one client gets at most each pose tick in a big world (each up to the MTU).</summary>
    public const int MaxPosePackets = 6;

    /// <summary>Pose ticks a body that changed stays "moving" for, so a far client (sent to less often) still hears its last pose.</summary>
    public const int HotPoseTicks = 8;

    /// <summary>Joiner streaming: bytes of structure a joiner gets a tick at most (about 1 MB/s at 60 Hz).</summary>
    public const int StreamBytesPerTick = 16 * 1024;

    // Distance bands: a body this far from a client (or further) is sent every 1, 2, 4 or 8 pose packets.
    public const float NearBand = 80f, MidBand = 200f, FarBand = 400f;

    /// <summary>How many pose ticks apart a body <paramref name="dist"/> metres from a client is sent.</summary>
    public static int PosePeriod(float dist) => dist < NearBand ? 1 : dist < MidBand ? 2 : dist < FarBand ? 4 : 8;

    private static readonly List<Construct> _moving = new(256), _due = new(256);

    /// <summary>
    /// Server, a big world, once a pose tick before the clients' packets: the bodies that move or changed (and those that
    /// did within the last <see cref="HotPoseTicks"/> pose ticks). Their signals count as sent.
    /// </summary>
    public static void PoseMovers(ConstructWorld world, uint poseTick)
    {
        _moving.Clear();
        foreach (var c in world.Bodies)
        {
            var still = Still(c) && c.SigHash == c.SigSent;
            if (!still)
                c.PoseHot = poseTick + HotPoseTicks;
            if (!still || poseTick < c.PoseHot)
                _moving.Add(c);
        }
    }

    /// <summary>The movers <see cref="PoseMovers"/> found (the self-test).</summary>
    public static int MoverCount => _moving.Count;

    /// <summary>
    /// Server, a big world: one client's pose packets this pose tick, into <paramref name="send"/> one at a time. The
    /// movers near the client (<paramref name="at"/>) go every time, further ones less often (<see cref="PosePeriod"/>),
    /// staggered by id; a few sleeping bodies are refreshed in turn (<paramref name="sleepCursor"/>, shared by every client).
    /// At most <see cref="MaxPosePackets"/> packets: past that, the start turns round with the tick so nobody is starved.
    /// Returns the poses sent.
    /// </summary>
    public static int WritePosesFor(PacketWriter w, ConstructWorld world, uint tick, uint poseTick, Vector3 at, int sleepCursor, Action<PacketWriter> send)
    {
        _due.Clear();
        foreach (var c in _moving)
            if (Priority(c))
                _due.Add(c); // (cj01: held or driven: every time, whatever the distance, and first)
        var first = _due.Count;
        foreach (var c in _moving)
        {
            if (Priority(c))
                continue;
            var period = PosePeriod(Vector3.Distance(c.WorldCom, at) - c.Radius);
            if (period == 1 || (poseTick + c.Id) % (uint)period == 0)
                _due.Add(c);
        }
        var bodies = world.Bodies;
        for (var k = 0; k < SleepingPerPacket && bodies.Count > 0; k++)
        {
            var c = bodies[(sleepCursor + k) % bodies.Count];
            if (Still(c) && poseTick >= c.PoseHot)
                _due.Add(c);
        }
        var sent = 0;
        var rest = _due.Count - first;
        var start = rest > 0 ? (int)(poseTick * 97 % (uint)rest) : 0;
        var budget = PacketWriter.Mtu - 8;
        var k2 = 0;
        for (var packet = 0; packet < MaxPosePackets && k2 < _due.Count; packet++)
        {
            w.Begin(Msg.CPose).U32(tick);
            var countAt = w.Length;
            w.U8(0);
            var n = 0;
            for (; k2 < _due.Count && n < 255; k2++)
            {
                var c = k2 < first ? _due[k2] : _due[first + (start + k2 - first) % rest];
                if (w.Length + PoseSize(c) > budget)
                    break;
                var keep = c.SigSent;
                WritePose(w, c);
                c.SigSent = keep; // (the signals count as sent once every client had its chance: PoseMovers)
                n++;
            }
            if (n == 0)
                break;
            w.PatchU8(countAt, (byte)n);
            send(w);
            sent += n;
        }
        return sent;
    }

    /// <summary>Server, a big world, after every client's packets: the movers' signals count as sent.</summary>
    public static void PoseSent()
    {
        foreach (var c in _moving)
            c.SigSent = c.SigHash;
    }

    private static int SignalParts(Construct c) => Signals(c).Length;

    /// <summary>
    /// The signal parts in part order, kept with the construct and found again only after a rebuild or a part count
    /// change (a pose once walked every part three times for them: 16,000 parts a body each pose; docs/perf/LIMITS.md).
    /// Grows by doubling, so a steady pose allocates nothing.
    /// </summary>
    internal static ReadOnlySpan<ushort> Signals(Construct c)
    {
        if (c.SigListStamp != c.RebuildStamp || c.SigListParts != c.PartCount || c.Dirty)
        {
            var n = 0;
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D is { IsSignal: true, Mains: false }) // (a mains lamp's light comes from the circuit on every peer: ConstructLamps.Lit)
                {
                    if (n == c.SigList.Length)
                        Array.Resize(ref c.SigList, Math.Max(16, n * 2));
                    c.SigList[n++] = (ushort)i;
                }
            c.SigListCount = n;
            c.SigListStamp = c.RebuildStamp;
            c.SigListParts = c.Dirty ? -1 : c.PartCount; // (a dirty body is walked again next time: its parts may change before the rebuild)
        }
        return c.SigList.AsSpan(0, c.SigListCount);
    }

    /// <summary>A joint's load against its rating in a byte: hundredths, up to 2.55 (the bar's end).</summary>
    public static byte OverByte(float over) => (byte)Math.Clamp(MathF.Round(over * 100), 0, 255);

    private static int PoseSize(Construct c) => PoseBytes + Math.Min(c.JointCount, PoseJoints) * 5 + 4 + (c.HasLogic ? Math.Min(SignalParts(c), PoseSignals) : 0) + 3 + Math.Min(c.RotorCount + c.ControlCount, PoseAir) + WalkSize(c);

    /// <summary>Rotors and control surfaces a pose carries at most: a window that turns (protocol 120).</summary>
    public const int PoseAir = 24;

    private static void WriteAir(PacketWriter w, Construct c)
    {
        var total = c.RotorCount + c.ControlCount;
        var n = Math.Min(total, PoseAir);
        var first = total <= PoseAir ? 0 : c.PoseAirAt % total;
        w.U16((ushort)first).U8((byte)n); // (protocol 189: first was a byte, and a body has up to 32 rotors and 512 surfaces)
        for (var k = 0; k < n; k++)
        {
            var at = (first + k) % total;
            if (at < c.RotorCount)
            {
                ref var r = ref c.Rotors[at];
                var share = r.Part < c.PartCount && c.Parts[r.Part].D.Rotor is { } d ? r.Omega / d.RatedOmega : 0;
                w.U8((byte)Math.Clamp(MathF.Round(share * 100), 0, 255));
            }
            else
            {
                ref var s = ref c.Surfaces[c.Controls[at - c.RotorCount]];
                var most = s.Part < c.PartCount && c.Parts[s.Part].D.Surface is { } d ? d.Control : 1;
                w.U8(unchecked((byte)(sbyte)Math.Clamp(MathF.Round(s.Deflect / most * 127), -127, 127)));
            }
        }
        c.PoseAirAt = (first + n) % Math.Max(1, total);
    }

    private static void ReadAir(ref PacketReader r, Construct? c)
    {
        int first = r.U16();
        int n = r.U8();
        for (var k = 0; k < n; k++)
        {
            var b = r.U8();
            var total = c == null ? 0 : c.RotorCount + c.ControlCount;
            if (c == null || total == 0)
                continue;
            var at = (first + k) % total;
            if (at < c.RotorCount)
            {
                ref var ro = ref c.Rotors[at];
                if (ro.Part < c.PartCount && c.Parts[ro.Part].D.Rotor is { } d)
                    ro.Omega = b / 100f * d.RatedOmega;
            }
            else
            {
                ref var s = ref c.Surfaces[c.Controls[at - c.RotorCount]];
                if (s.Part < c.PartCount && c.Parts[s.Part].D.Surface is { } d)
                    s.Deflect = (sbyte)b / 127f * d.Control;
            }
        }
    }

    /// <summary>A turn rate in thousandths of a radian a second (+-32 rad/s).</summary>
    private static short Mrad(float v) => (short)Math.Clamp(MathF.Round(v * 1000), short.MinValue, short.MaxValue);

    private static void WritePose(PacketWriter w, Construct c)
    {
        var q = c.Rot;
        if (q.W < 0)
            q = -q;
        w.U16(c.Id).Vec(c.Pos)
            .U16(S16((short)MathF.Round(q.X * 32767))).U16(S16((short)MathF.Round(q.Y * 32767))).U16(S16((short)MathF.Round(q.Z * 32767))).U16(S16((short)MathF.Round(q.W * 32767)))
            .U16(S16(Cm(c.Vel.X / 4))).U16(S16(Cm(c.Vel.Y / 4))).U16(S16(Cm(c.Vel.Z / 4)))
            .U16(S16(Mrad(c.AngVel.X))).U16(S16(Mrad(c.AngVel.Y))).U16(S16(Mrad(c.AngVel.Z))) // (protocol 102: riders leave with the deck's turning speed too; ConstructRide.cs)
            .U8(unchecked((byte)(sbyte)Math.Clamp(MathF.Round(c.Steer * 100), -127, 127)))
            .U8(c.Driver)
            .U8((byte)((c.Asleep ? PoseAsleep : 0) | (c.LegCount > 0 ? PoseWalker : 0))) // (protocol 229: a walk block follows the rotors)
            .U8((byte)MathF.Round(ConstructPower.Share(c) * 255)).U8(PowerFlags(c)).U8((byte)c.Gear).F32(c.Fuel) // (protocol 83; 117: a float, a capital tank holds millions of litres)
            .U16(RpmOut(c)).U8((byte)c.Note) // (protocol 86)
            .U8(ShareOut(c.ThrustShow)).U8(ShareOut(MathF.Max(c.SmokeLevel, c.BurnShow))); // (protocol 117)
        // Joints: all of them, or a window that turns on a body with more than a packet holds (protocol 117).
        var jn = Math.Min(c.JointCount, PoseJoints);
        var js = c.JointCount <= PoseJoints ? 0 : c.PoseJointAt % c.JointCount;
        w.U8((byte)js).U8((byte)jn);
        for (var k = 0; k < jn; k++)
        {
            var j = (js + k) % c.JointCount;
            w.U16(S16((short)Math.Clamp(MathF.Round(c.Joints[j].Angle * ConstructLinear.PoseScale(c.Joints[j])), short.MinValue, short.MaxValue)))
                .U16(S16((short)Math.Clamp(MathF.Round(c.Joints[j].Vel * 100), short.MinValue, short.MaxValue)))
                .U8(OverByte(c.Joints[j].Over)); // (protocol 91: its load against its rating, for the configure panel's bar and the tint)
        }
        c.PoseJointAt = (js + jn) % Math.Max(1, c.JointCount);
        WriteAir(w, c);
        if (c.LegCount > 0)
            WriteWalk(w, c); // (protocol 229: the gait and the feet: ConstructNetWalk.cs)
        // The signal parts' values, in part order (only with logic: the client knows which parts they are): the first
        // ordinal (u16) and how many (u16): all of them, or a window that turns (protocol 117).
        var at = w.Length;
        w.U16(0).U16(0);
        if (!c.HasLogic)
            return;
        var sigs = Signals(c);
        var total = sigs.Length;
        var first = total <= PoseSignals ? 0 : c.PoseSignalAt % total;
        var n = 0;
        for (var k = first; k < total && n < PoseSignals; k++)
        {
            w.U8(unchecked((byte)ConstructLogic.Quantise(c.Parts[sigs[k]].Out)));
            n++;
        }
        w.PatchU16(at, (ushort)first);
        w.PatchU16(at + 2, (ushort)n);
        var end = first + n >= total;
        c.PoseSignalAt = end ? 0 : first + n;
        if (end)
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
            var ang = new Vector3(R16(r.U16()), R16(r.U16()), R16(r.U16())) / 1000f;
            var steer = (sbyte)r.U8() / 100f;
            var driver = r.U8();
            var sleepByte = r.U8();
            var asleep = (sleepByte & PoseAsleep) != 0;
            var share = r.U8() / 255f;
            var power = r.U8();
            var gear = r.U8();
            var fuel = r.F32();
            var rpm = r.U16();
            var note = r.U8();
            var thrust = r.U8() / 255f;
            var smoke = r.U8() / 255f;
            int jointAt = r.U8();
            int joints = r.U8();
            var c = world.Get(id);
            if (c != null && tick < c.NetTick)
                c = null; // (a Full newer than this packet came first: its pose stands)
            if (c != null && !stale)
            {
                c.Charge = share * c.ChargeCapacity;
                c.Fuel = fuel;
                SetPower(c, power, gear);
                SetDrive(c, rpm, note);
                c.ThrustShow = thrust;
                c.SmokeLevel = smoke;
            }
            for (var k3 = 0; k3 < joints; k3++)
            {
                var raw = R16(r.U16());
                var v = R16(r.U16()) / 100f;
                var over = r.U8() / 100f;
                var j = c == null || c.JointCount == 0 ? 0 : (jointAt + k3) % c.JointCount;
                var a = c != null && j < c.JointCount ? raw / ConstructLinear.PoseScale(c.Joints[j]) : 0;
                if (c != null && !stale && j < c.JointCount)
                {
                    c.Joints[j].Over = over;
                    c.Joints[j].NetShown = a; // (with a tick, kept with the body's pose: SampleJoints draws it at the body's tick, never as it arrives)
                    if (tick == 0 || double.IsNaN(world.NetRenderTick))
                    {
                        if (c.Joints[j].Rail || c.Joints[j].Type == JointType.Swivel)
                        {
                            c.Joints[j].NetFrom = c.Joints[j].NetDrawn; // (drawn along to it, every frame: ConstructLinear.Follow)
                            c.Joints[j].NetT = 0;
                        }
                        else
                        {
                            c.Joints[j].PrevAngle = c.Joints[j].Angle;
                            c.Joints[j].Angle = a;
                        }
                    }
                    c.Joints[j].Vel = v;
                }
            }
            ReadAir(ref r, c != null && !stale ? c : null);
            if ((sleepByte & PoseWalker) != 0)
                ReadWalk(ref r, c != null && !stale ? c : null, pos);
            int signalFrom = r.U16();
            int signals = r.U16();
            var sigs = c != null && !stale ? Signals(c) : default;
            for (var k2 = 0; k2 < signals; k2++)
            {
                var sig = (sbyte)r.U8() / 10f;
                if (signalFrom + k2 < sigs.Length)
                    c!.Parts[sigs[signalFrom + k2]].Out = sig;
            }
            if (c == null || stale || !float.IsFinite(pos.X + pos.Y + pos.Z) || q.LengthSquared() < 0.5f)
                continue;
            SetTarget(c, pos, Quaternion.Normalize(q), now, tick);
            c.Vel = vel;
            c.AngVel = ang;
            c.Steer = steer;
            c.Driver = driver;
            c.Asleep = asleep;
            c.NetTick = tick;
        }
    }

    /// <summary>
    /// cj01: ticks the bodies are drawn further behind the game's interpolation clock (which runs <see cref="World.SnapshotEvery"/>
    /// x 2 ticks plus the measured jitter behind the server). Poses come every <see cref="PoseEvery"/> ticks, so the bodies sit
    /// two pose intervals and a tick behind: a pose lost on the way is bridged by the next one, never by guessing ahead.
    /// </summary>
    public const int BodyExtraDelayTicks = PoseEvery * 2 + 1 - World.SnapshotEvery * 2;

    /// <summary>How far past its newest pose a body is carried at its velocity (ticks) when the poses are late; then it waits.</summary>
    public const float MaxAheadTicks = PoseEvery;

    /// <summary>
    /// cj01: the body drawn at server tick <paramref name="rt"/>, between the two poses heard either side of it (the
    /// server's tick stamps, not when the packets arrived, so packets arriving unevenly don't show), or the oldest one
    /// before them all, or a little past the newest (at its velocity) when the poses are late.
    /// </summary>
    public static void SampleAt(Construct c, double rt)
    {
        var n = c.NetHistCount;
        var ticks = c.NetHistTick;
        if (rt >= ticks[n - 1])
        {
            var ahead = (float)Math.Min(rt - ticks[n - 1], MaxAheadTicks) * World.TickDt;
            c.Pos = c.NetHistPos[n - 1] + (c.Asleep ? Vector3.Zero : c.Vel * ahead);
            c.Rot = c.NetHistRot[n - 1];
            return;
        }
        if (rt <= ticks[0])
        {
            c.Pos = c.NetHistPos[0];
            c.Rot = c.NetHistRot[0];
            return;
        }
        var i = n - 1;
        while (i > 0 && ticks[i - 1] > rt)
            i--;
        // A cubic through the poses (Catmull-Rom on their ticks): straight lines between 20 Hz poses turned a corner at
        // every pose, which shows as a shudder on anything moving along a curve.
        var a = i - 1;
        var pos = c.NetHistPos;
        float h = Math.Max(1u, ticks[i] - ticks[a]);
        var line = (pos[i] - pos[a]) / h;
        var ma = a > 0 ? (pos[i] - pos[a - 1]) / (ticks[i] - ticks[a - 1]) : line;
        var mb = i + 1 < n ? (pos[i + 1] - pos[a]) / (ticks[i + 1] - ticks[a]) : line;
        var t = (float)((rt - ticks[a]) / h);
        var t2 = t * t;
        var t3 = t2 * t;
        c.Pos = (2 * t3 - 3 * t2 + 1) * pos[a] + (t3 - 2 * t2 + t) * h * ma + (-2 * t3 + 3 * t2) * pos[i] + (t3 - t2) * h * mb;
        // The turn the same way, on the quaternions' four numbers (all on q[a]'s side), then normalised: close to a
        // spline on the sphere for the small turns between poses, with no corner at each pose either.
        var rots = c.NetHistRot;
        var qa = Q4(rots[a]);
        var qb = Side(Q4(rots[i]), qa);
        var lineQ = (qb - qa) / h;
        var ra = a > 0 ? (qb - Side(Q4(rots[a - 1]), qa)) / (ticks[i] - ticks[a - 1]) : lineQ;
        var rb = i + 1 < n ? (Side(Q4(rots[i + 1]), qa) - qa) / (ticks[i + 1] - ticks[a]) : lineQ;
        var q = (2 * t3 - 3 * t2 + 1) * qa + (t3 - 2 * t2 + t) * h * ra + (-2 * t3 + 3 * t2) * qb + (t3 - t2) * h * rb;
        c.Rot = q.LengthSquared() > 1e-8f ? Quaternion.Normalize(new Quaternion(q.X, q.Y, q.Z, q.W)) : rots[i];
    }

    /// <summary>
    /// Client, after <see cref="SampleAt"/>: every joint at the same server tick as the body, through the same cubic. A joint set as its pose
    /// arrived was a round trip's delay ahead of a body drawn on the ticks, so the links of a moving chain were out of step
    /// with the body and with each other (the user's report: a thrown arm looked bent). A free joint's angle goes the short way round.
    /// </summary>
    public static void SampleJoints(Construct c, double rt)
    {
        var joints = c.JointCount;
        var n = c.NetHistCount;
        var hist = c.NetHistAngle;
        if (joints == 0 || hist.Length != Construct.NetHistMax * joints)
            return;
        var ticks = c.NetHistTick;
        var last = (n - 1) * joints;
        if (rt >= ticks[n - 1] || rt <= ticks[0])
        {
            var from = rt <= ticks[0] ? 0 : last;
            var ahead = from == 0 ? 0 : (float)Math.Min(rt - ticks[n - 1], MaxAheadTicks) * World.TickDt;
            for (var j = 0; j < joints; j++)
            {
                ref var jt = ref c.Joints[j];
                jt.PrevAngle = jt.Angle;
                PutAngle(ref jt, hist[from + j] + (c.Asleep ? 0 : jt.Vel * ahead));
            }
            c.NetJointsDone = from == last;
            return;
        }
        var i = n - 1;
        while (i > 0 && ticks[i - 1] > rt)
            i--;
        var a = i - 1;
        float h = Math.Max(1u, ticks[i] - ticks[a]);
        var t = (float)((rt - ticks[a]) / h);
        var t2 = t * t;
        var t3 = t2 * t;
        for (var j = 0; j < joints; j++)
        {
            ref var jt = ref c.Joints[j];
            var wraps = !float.IsFinite(jt.Min);
            var pa = hist[a * joints + j];
            float Near(int row)
            {
                var v = hist[row * joints + j];
                return wraps ? pa + World.WrapAngle(v - pa) : v;
            }
            var pb = Near(i);
            var line = (pb - pa) / h;
            var ma = a > 0 ? (pb - Near(a - 1)) / (ticks[i] - ticks[a - 1]) : line;
            var mb = i + 1 < n ? (Near(i + 1) - pa) / (ticks[i + 1] - ticks[a]) : line;
            jt.PrevAngle = jt.Angle;
            PutAngle(ref jt, (2 * t3 - 3 * t2 + 1) * pa + (t3 - 2 * t2 + t) * h * ma + (-2 * t3 + 3 * t2) * pb + (t3 - t2) * h * mb);
        }
        c.NetJointsDone = false;
    }

    private static void PutAngle(ref CJoint jt, float angle)
    {
        jt.Angle = float.IsFinite(jt.Min) ? Math.Clamp(angle, jt.Min, jt.Max) : World.WrapAngle(angle);
        jt.NetDrawn = jt.Angle; // (what a holder's prediction of a carriage starts from: GameSandboxPhysGun.PredictJoint)
    }

    /// <summary>How fast a change of the drawn curve is eased out (rad/s of a critically damped spring: most of it gone in 0.3 s).</summary>
    public const float SmoothRate = 8f;

    /// <summary>A change of the curve bigger than this (m, rad) is shown at once, not eased: the body really is somewhere else.</summary>
    private const float SmoothMaxPos = 1f, SmoothMaxRot = 0.5f;

    /// <summary>
    /// Client, after <see cref="SampleAt"/>: when a pose arrives the curve through the poses changes under the body (the
    /// last segment's end tangent moves, or a late or lost pose had it carried on at its velocity), which drew as a kink of
    /// hundreds of m/s2, more on a loaded machine's uneven frames. The new curve is sampled where last frame was drawn (and
    /// the frame before, for its speed); the difference from what was drawn there, in place and speed (turn and spin), goes
    /// into an offset that a critically damped spring eases out, so the drawn body stays continuous in place and speed.
    /// </summary>
    private static void Smooth(Construct c, double rt, float dt)
    {
        var raw = c.Pos;
        var rawRot = c.Rot;
        var gap = rt - c.NetRawRt; // (NaN when there was no raw sample last frame)
        if (!(gap > 0 && gap < 30))
        {
            c.NetRawVel = c.Asleep ? Vector3.Zero : c.Vel;
            c.NetRawSpin = c.Asleep ? Vector3.Zero : c.AngVel;
            if (!(gap == 0))
            {
                c.NetOffPos = c.NetOffVel = c.NetOffRot = c.NetOffSpin = Vector3.Zero;
                c.NetRawRt0 = double.NaN;
            }
        }
        else
        {
            var span = (float)(gap * World.TickDt);
            var fromPos = c.NetRawPos;
            var fromRot = c.NetRawRot;
            if (c.NetRawVer != c.NetHistVer)
            {
                // The poses changed: the new curve where last frame was drawn, and its speed there, against what was drawn.
                SampleAt(c, c.NetRawRt);
                fromPos = c.Pos;
                fromRot = c.Rot;
                c.NetOffPos += c.NetRawPos - fromPos;
                c.NetOffRot += RotVec(c.NetRawRot * Quaternion.Conjugate(fromRot));
                var gap0 = c.NetRawRt - c.NetRawRt0;
                if (gap0 > 0.06 && gap0 < 30)
                {
                    var span0 = (float)(gap0 * World.TickDt);
                    SampleAt(c, c.NetRawRt0);
                    c.NetOffVel += c.NetRawVel - (fromPos - c.Pos) / span0;
                    c.NetOffSpin += c.NetRawSpin - RotVec(fromRot * Quaternion.Conjugate(c.Rot)) / span0;
                }
                if (c.NetOffPos.Length() > SmoothMaxPos || c.NetOffRot.Length() > SmoothMaxRot || !float.IsFinite(c.NetOffVel.X + c.NetOffVel.Y + c.NetOffVel.Z + c.NetOffSpin.X + c.NetOffSpin.Y + c.NetOffSpin.Z))
                    c.NetOffPos = c.NetOffVel = c.NetOffRot = c.NetOffSpin = Vector3.Zero;
            }
            if (gap > 0.06)
            {
                c.NetRawVel = (raw - fromPos) / span;
                c.NetRawSpin = RotVec(rawRot * Quaternion.Conjugate(fromRot)) / span;
            }
            // The offset (as it was at last frame's drawing) eased on to this frame: the exact critically damped step, stable
            // for any frame time.
            var e = MathF.Exp(-SmoothRate * dt);
            Damp(ref c.NetOffPos, ref c.NetOffVel, dt, e);
            Damp(ref c.NetOffRot, ref c.NetOffSpin, dt, e);
            if (c.NetOffPos.LengthSquared() < 1e-10f && c.NetOffVel.LengthSquared() < 1e-8f && c.NetOffRot.LengthSquared() < 1e-10f && c.NetOffSpin.LengthSquared() < 1e-8f)
                c.NetOffPos = c.NetOffVel = c.NetOffRot = c.NetOffSpin = Vector3.Zero;
            c.NetRawRt0 = c.NetRawRt;
        }
        c.NetRawPos = raw;
        c.NetRawRot = rawRot;
        c.NetRawRt = rt;
        c.NetRawVer = c.NetHistVer;
        c.Pos = raw;
        c.Rot = rawRot;
        if (c.NetOffPos != Vector3.Zero || c.NetOffRot != Vector3.Zero)
        {
            c.Pos = raw + c.NetOffPos;
            c.Rot = Quaternion.Normalize(FromRotVec(c.NetOffRot) * rawRot);
        }
    }

    private static void Damp(ref Vector3 x, ref Vector3 v, float t, float e)
    {
        const float w = SmoothRate;
        var b = v + w * x;
        x = (x + b * t) * e;
        v = (v - w * b * t) * e;
    }

    /// <summary>A turn as a rotation vector (axis times angle, radians), the short way round.</summary>
    private static Vector3 RotVec(Quaternion q)
    {
        if (q.W < 0)
            q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        var v = new Vector3(q.X, q.Y, q.Z);
        var s = v.Length();
        if (s < 1e-7f)
            return v * 2f;
        return v * (2f * MathF.Atan2(s, q.W) / s);
    }

    private static Quaternion FromRotVec(Vector3 v)
    {
        var a = v.Length();
        return a < 1e-8f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(v / a, a);
    }

    private static Vector4 Q4(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    private static Vector4 Side(Vector4 q, Vector4 like) => Vector4.Dot(q, like) < 0 ? -q : q;

    /// <summary>Client, every frame: each body drawn between the poses heard (on the server's ticks when the game has a clock: cj01).</summary>
    public static void Blend(ConstructWorld world, double now, float dt)
    {
        const double span = PoseEvery / (double)World.TickRate;
        var rt = world.NetRenderTick;
        var ticked = !double.IsNaN(rt);
        foreach (var c in world.Bodies)
        {
            if (c.FreeLeafCount > 0 && !(ticked && c.NetHistCount > 0))
                ConstructLinear.Follow(c, dt * World.TickRate / (PoseEvery * ConstructLinear.FollowPoses));
            if (c.Asleep && c.WheelCount == 0 && c.NetJointsDone && c.Pos == c.NetPos && c.Rot == c.NetRot && c.PrevPos == c.Pos && c.PrevRot == c.Rot)
                continue; // (a sleeping body drawn where its last pose put it: nothing to blend; thousands cost a test each)
            c.PrevPos = c.Pos;
            c.PrevRot = c.Rot;
            if (ticked && c.NetHistCount > 0)
            {
                SampleAt(c, rt);
                Smooth(c, rt, dt);
                SampleJoints(c, rt);
                SampleFeet(c, rt);
            }
            else
            {
                c.NetRawRt = double.NaN;
                var t = (float)Math.Clamp((now - c.NetAt) / span, 0, 1.5);
                // A little past the last pose (at its velocity) so a moving body doesn't stop and start between packets.
                c.Pos = t <= 1 ? Vector3.Lerp(c.NetFromPos, c.NetPos, t) : c.NetPos + c.Vel * (float)((t - 1) * span);
                c.Rot = t >= 1 ? c.NetRot : Quaternion.Slerp(c.NetFromRot, c.NetRot, t);
                SampleFeet(c, double.NaN); // (as last heard)
            }
            var fwd = Vector3.Transform(c.DriveFwd, c.Rot);
            var anchoredDrive = c.Anchored && c.EngineRunning && c.HasPowertrain; // (cs03: an anchored base's wheels turn at the engine's speed, from the rpm in its pose)
            for (var k = 0; k < c.WheelCount; k++)
            {
                ref var wh = ref c.Wheels[k];
                // (a track runs at its side's speed, the turn's share too: in a skid turn the sides run opposite ways)
                wh.Spin += anchoredDrive ? ConstructDrive.AirOmega(c, k, wh.Driven, 1, 0, dt) * dt : Vector3.Dot(wh.Track || c.SkidDrive ? c.PointVel(c.ToWorld(wh.Centre)) : c.Vel, fwd) / wh.Radius * dt;
            }
            for (var k = 0; k < c.RotorCount; k++)
            {
                ref var ro = ref c.Rotors[k];
                ro.Angle = ro.Omega > 0 ? (ro.Angle + ro.Sense * ro.Omega * dt) % MathF.Tau : 0; // (drawn turning, from its speed in the pose)
            }
        }
        ConstructCargo.FollowAll(world); // (what frozen cargo volumes carry, where their carriers are drawn now)
        world.GridMoved(); // (the client's broadphase: its ray tests and player blocking see the bodies where they are drawn)
    }
}
