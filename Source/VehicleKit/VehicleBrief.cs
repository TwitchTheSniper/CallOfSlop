using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop.VehicleKit;

/// <summary>How a vehicle turns (<see cref="VehicleBrief.Steer"/>).</summary>
public enum BriefSteer : byte
{
    None, // no wheels that steer and no skid steering: it doesn't turn under its own power
    Wheels, // steered wheels on steering pivots: a turning circle
    Skid, // skid steering: one side driven against the other, it turns on the spot
}

/// <summary>How a gun is mounted (<see cref="BriefGun.Mount"/>).</summary>
public enum BriefMount : byte
{
    Fixed, // nothing turns it: the vehicle aims it
    Turret, // a turntable (or swivel) turns it round, and usually a hinge lifts it
    Hinge, // only a hinge lifts it: it points where the vehicle does
}

/// <summary>A gun's range band by its full-damage range (<see cref="BriefGun.Range"/>): close under 25 m, short under 60, medium under 150, else long.</summary>
public enum RangeBand : byte
{
    Close,
    Short,
    Medium,
    Long,
}

/// <summary>What a seat controls (<see cref="BriefSeat.Controls"/>).</summary>
[Flags]
public enum SeatControls : byte
{
    None = 0,
    Drive = 1, // the seat that drives (through a Driving Module)
    Guns = 2, // fires and aims guns (BriefSeat.Guns says which)
    Winch = 4, // a winch is wired to it
    Signals = 8, // horns, lamps, sirens or a radio are wired to it
    Other = 16, // something else is wired to it
}

/// <summary>A way on or off (<see cref="BriefEntry.Kind"/>).</summary>
public enum EntryKind : byte
{
    Door,
    Ladder,
    Hatch,
}

/// <summary>
/// A reference to one part of the vehicle that outlives part breakage, saves and loads: the part's stable <see cref="CPart.Tag"/> (the server's, given when the body
/// joins the world), the part index it was found at last (a hint, checked against the tag), its def (checked when the brief is put on its construct) and a point on it in the
/// body's rest frame (every moving joint at 0: <see cref="Construct.ToWorld"/> puts it in the world for anything rigid with the body).
/// </summary>
public struct BriefRef
{
    public ushort Index, Tag, Def;
    public Vector3 At;

    /// <summary>The part's index on the construct now, or -1 when it has gone (<see cref="VehicleBrief.Refresh"/>; not saved).</summary>
    public short Live;

    public static BriefRef Of(Construct c, int part, Vector3 at) => new() { Index = (ushort)part, Tag = c.Parts[part].Tag, Def = c.Parts[part].Def, At = at, Live = (short)part };
}

/// <summary>One gun: where it is, how it is mounted, what it does at what range, where it can reach and who fires it.</summary>
public struct BriefGun
{
    public BriefRef Part; // the gun part (a receiver or a heavy gun); At: its muzzle at rest
    public BriefMount Mount;
    public GunClass Class; // light, medium or heavy (VehicleRoles.ClassOf's rule on the built gun: flame, harpoons, shells and a blast of 2 m or more heavy, full-auto medium, else light)
    public RangeBand Band;
    public float Range, MaxRange; // m: full damage to here (WeaponDef.RangeM), and the round's reach (CGun.MaxRange)
    public float Dps; // damage a second at full rate in range (damage x rounds a shot / interval)
    public bool FullCircle; // its turntable turns forever
    public float YawMin, YawMax; // degrees about the turntable from where it rests (FullCircle: -180, 180)
    public float ElevMin, ElevMax; // degrees over level the hinge can point it, at rest yaw
    public uint Clear; // bearing bins (VehicleBrief.Bins of 11.25 degrees, 0 dead ahead, clockwise) it can turn to and fire clear of its own vehicle at some elevation
    public uint Level; // ... and clear at the elevation nearest level (a ground target)
    public Vector3 Pivot; // the point it turns about at rest (the turntable's pivot, else the muzzle)
    public sbyte Seat; // the seat that fires and aims it (index into VehicleBrief.Seats), or -1
    public BriefRef Input; // a logic part wired into it (a button, a sensor) that fires it; Tag and Def 0: none
}

/// <summary>A designed weak point: a part to shoot to stop or strip the vehicle, and where it can be hit from.</summary>
public struct BriefWeak
{
    public BriefRef Part; // At: its middle
    public string Kind; // the style's rule label ("fuel", "tyres", "antenna", "Pipe Box"...), or "fuel", "tyres", "ammo", "engine" for anything
    public ushort Bearings; // bits of VehicleBrief.WeakBins (22.5 degrees, 0 dead ahead, clockwise): a standing player 20 m off there hits it first
    public bool Top; // seen from straight above
}

/// <summary>A seat: who sits there and what they work.</summary>
public struct BriefSeat
{
    public BriefRef Part;
    public SeatControls Controls;
    public ushort Guns; // bit k: gun k (VehicleBrief.Guns) is fired from here
    public bool Open; // its sitter can be shot: seen from above, or by a standing player from four bearings or more
}

/// <summary>A door, ladder or hatch.</summary>
public struct BriefEntry
{
    public BriefRef Part;
    public EntryKind Kind;
}

/// <summary>A container that rolled loot from a table when the vehicle was set down.</summary>
public struct BriefLoot
{
    public BriefRef Part;
    public string Table;
    public byte Tier; // the table's tier: "bandit 3" is 3, a style's own table 2
}

/// <summary>
/// A piece of its own cargo (docs/vehiclekit/VEHICLEKIT.md, "Cargo and spares"): what it is for, how many, and where: in a container's slot (Container, Slot), or loose
/// in a frozen Cargo Volume (At, its place in the body's rest frame; a carried body's <see cref="Construct.CarriedBy"/> is the vehicle). A loose Ammo Box or Crate cooks off.
/// </summary>
public struct BriefStow
{
    public string Part;
    public StowKind Kind;
    public byte Count;
    public bool Loose;
    public BriefRef Container; // (loose: none, Def 0)
    public sbyte Slot;
    public Vector3 At;
    public bool CooksOff; // a loose box of rounds: shot, it blows
    public uint Guns; // bit k: gun k (VehicleBrief.Guns) can use it (a magazine it takes, rounds or fuel for it, its spare parts)
}

/// <summary>A tow hitch (At: its coupling point) and its kind.</summary>
public struct BriefHitch
{
    public BriefRef Part;
    public HitchKind Kind;
}

/// <summary>
/// What a future AI driver or gunner needs to know about a generated faction vehicle, worked out once when it is made (docs/vehiclekit/VEHICLEKIT.md, "Behaviour data"),
/// so the AI needs no analysis of the vehicle. Server side only: it rides on the construct (<see cref="Construct.Brief"/>), saves and loads with the world, and is
/// never sent to clients. Parts are named by <see cref="BriefRef"/>s, which follow part breakage: <see cref="Refresh"/> after the construct is rebuilt (cheap when it
/// wasn't, allocation free either way), then <see cref="Has"/> and <see cref="Index"/>. Directions are in the vehicle's own frame: bearing 0 is dead ahead
/// (<see cref="Forward"/>, the drive line's forward), counted clockwise seen from above (90 is its right).
/// </summary>
public sealed class VehicleBrief
{
    /// <summary>The save format's version (the first byte); a brief of a later version is skipped on load.</summary>
    public const byte FormatVersion = 2; // (2: its cargo, Stowed; a version 1 brief reads with none)

    /// <summary>Gun bearings: 32 bins of 11.25 degrees. Weak point bearings: 16 bins of 22.5.</summary>
    public const int Bins = 32, WeakBins = 16;

    // ---- Who ------------------------------------------------------------------------------------------------------------

    public string Name = "", Style = "", Shape = "", Role = "", Model = "";
    public byte Tier, Kit;

    /// <summary>A convoy member's place: "leader", "escort" or "cargo" ("" alone); its convoy's seed and formation ("column", "wedge"), its index in the convoy (0 the
    /// leader), its lane (0 the middle, -1 and 1 a wedge's sides) and the leader's construct id when the leader was down first (0: not known).</summary>
    public string Slot = "", Formation = "";
    public int Convoy;
    public byte ConvoyIndex;
    public sbyte Lane;
    public ushort Leader;

    // ---- Driving --------------------------------------------------------------------------------------------------------

    /// <summary>Forward in the body frame (level, unit): bearings are measured from it.</summary>
    public Vector3 Forward = -Vector3.UnitZ;

    public float Mass, Length, Width, Height; // kg; m along, across, up
    public float TopSpeed; // m/s, the cheap estimate (VehicleRoles.Speed: peak power at 60 % against rolling resistance and drag)
    public bool Measured; // the drive check ran: the three below are its
    public float PulseSpeed, PulseMoved, Accel; // m/s reached and m covered in the check's 1.25 s throttle pulse from rest; m/s2 (2 d / t2)
    public BriefSteer Steer;
    public float MaxSteer, Wheelbase, Track, TurnRadius; // degrees; m; m; m (0 for skid steering: on the spot)
    public byte Wheels, Driven;
    public float Clearance; // m between the ground and the lowest fixed part (not wheels or springs)
    public float ComHeight, TipSide, TipEnd; // m over the ground; degrees of tilt sideways and nose or tail down before it tips over (static)
    public float Climb; // degrees: a hint of the steepest slope it climbs (power at a crawl, and grip on its driven wheels)
    public float Fuel, Burn; // litres; litres a second at full throttle
    public bool Electric;
    public float Charge; // kJ its batteries hold (an electric drive's range)
    public float Endurance, Range; // minutes at full throttle; km at full throttle at the estimated top speed

    // ---- Weapons, weak points, crew, cargo -------------------------------------------------------------------------------------

    public BriefGun[] Guns = [];

    /// <summary>The range (m) to fight at: the full-damage range of the guns, weighed by their damage (a median); 0 with no guns.</summary>
    public float Engage;

    /// <summary>The bearing bin (of <see cref="Bins"/>) where the most gun damage reaches clear, and how much (damage a second); its side, 0 front, 1 right, 2 back, 3 left.</summary>
    public byte BestBin, BestSide;

    public float BestDps;

    public BriefWeak[] Weak = [];

    /// <summary>The side to keep away from the enemy (0 front, 1 right, 2 back, 3 left): the one its weak points are most exposed on; 255 with none.</summary>
    public byte KeepAway = 255;

    public BriefSeat[] Seats = [];
    public BriefEntry[] Entries = [];
    public BriefLoot[] Loot = [];
    public BriefHitch[] Hitches = [];
    public BriefRef[] Winches = [];
    public int Cargo; // loose cargo pieces it was built carrying
    public byte FuelOpen; // fuel weak points that can be hit

    /// <summary>Its own cargo: ammo, spares and spare guns, in its slots or loose on its decks (kit 4's cargo pass; empty for anything else).</summary>
    public BriefStow[] Stowed = [];

    /// <summary>The construct's rebuild stamp when the references were last resolved (not saved).</summary>
    private int _stamp = int.MinValue;

    // ---- Sides and bearings ---------------------------------------------------------------------------------------------------

    public static readonly string[] SideNames = ["front", "right", "back", "left"];

    /// <summary>The side (0 front, 1 right, 2 back, 3 left) a bearing in degrees falls on: front is -45 to 45.</summary>
    public static int SideOf(float degrees) => (int)(((degrees + 45) % 360 + 360) % 360 / 90) & 3;

    /// <summary>A gun bin's bearing (degrees).</summary>
    public static float BinBearing(int bin) => bin * 360f / Bins;

    /// <summary>A direction in the body frame for a bearing (degrees) on the level.</summary>
    public Vector3 Way(float degrees)
    {
        var a = degrees * MathF.PI / 180;
        var right = Vector3.Cross(Forward, Vector3.UnitY);
        return Forward * MathF.Cos(a) + right * MathF.Sin(a);
    }

    // ---- References -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Finds every reference's part again after the construct was rebuilt (a part broke off, the world loaded): by its hint, else by its tag. Nothing to do when
    /// the construct hasn't been rebuilt since the last call; allocates nothing.
    /// </summary>
    public void Refresh(Construct c)
    {
        if (c.RebuildStamp == _stamp)
            return;
        _stamp = c.RebuildStamp;
        for (var k = 0; k < Guns.Length; k++)
        {
            Find(c, ref Guns[k].Part);
            if (Guns[k].Input.Tag != 0)
                Find(c, ref Guns[k].Input);
        }
        for (var k = 0; k < Weak.Length; k++)
            Find(c, ref Weak[k].Part);
        for (var k = 0; k < Seats.Length; k++)
            Find(c, ref Seats[k].Part);
        for (var k = 0; k < Entries.Length; k++)
            Find(c, ref Entries[k].Part);
        for (var k = 0; k < Loot.Length; k++)
            Find(c, ref Loot[k].Part);
        for (var k = 0; k < Hitches.Length; k++)
            Find(c, ref Hitches[k].Part);
        for (var k = 0; k < Winches.Length; k++)
            Find(c, ref Winches[k]);
        for (var k = 0; k < Stowed.Length; k++)
            if (!Stowed[k].Loose)
                Find(c, ref Stowed[k].Container);
    }

    private static void Find(Construct c, ref BriefRef r)
    {
        if (r.Tag == 0)
        {
            r.Live = (short)(r.Index < c.PartCount && c.Parts[r.Index].Def == r.Def ? r.Index : -1); // (a brief that never joined a world: by index)
            return;
        }
        if (r.Index < c.PartCount && c.Parts[r.Index].Tag == r.Tag)
        {
            r.Live = (short)r.Index;
            return;
        }
        r.Live = -1;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Tag == r.Tag)
            {
                r.Index = (ushort)i;
                r.Live = (short)i;
                return;
            }
    }

    /// <summary>Is the referenced part still on the construct and working (not broken)? Call <see cref="Refresh"/> first.</summary>
    public static bool Has(Construct c, in BriefRef r) => r.Live >= 0 && r.Live < c.PartCount && !c.Parts[r.Live].Broken;

    /// <summary>The referenced part's index now, or -1. Call <see cref="Refresh"/> first.</summary>
    public static int Index(in BriefRef r) => r.Live;

    /// <summary>
    /// Puts a brief made at generation on the construct the spawn built from the same blueprint: every reference takes its part's tag. The parts are checked by
    /// def first; a brief that doesn't fit (the build came out differently) is refused with the reason and nothing is changed.
    /// </summary>
    public string? Attach(Construct c)
    {
        string? why = null;
        void Check(in BriefRef r, string what)
        {
            if (why == null && (r.Index >= c.PartCount || c.Parts[r.Index].Def != r.Def))
                why = $"{what}: part {r.Index} isn't a {ConstructParts.Def(r.Def).Name} on the built body";
        }
        foreach (var g in Guns)
        {
            Check(g.Part, "a gun");
            if (g.Input.Def != 0)
                Check(g.Input, "a gun's input");
        }
        foreach (var w in Weak)
            Check(w.Part, "a weak point");
        foreach (var s in Seats)
            Check(s.Part, "a seat");
        foreach (var e in Entries)
            Check(e.Part, "an entry");
        foreach (var l in Loot)
            Check(l.Part, "loot");
        foreach (var h in Hitches)
            Check(h.Part, "a hitch");
        foreach (var w in Winches)
            Check(w, "a winch");
        foreach (var x in Stowed)
            if (!x.Loose)
                Check(x.Container, "a store");
        if (why != null)
            return why;
        for (var k = 0; k < Guns.Length; k++)
        {
            Tag(c, ref Guns[k].Part);
            if (Guns[k].Input.Def != 0)
                Tag(c, ref Guns[k].Input);
        }
        for (var k = 0; k < Weak.Length; k++)
            Tag(c, ref Weak[k].Part);
        for (var k = 0; k < Seats.Length; k++)
            Tag(c, ref Seats[k].Part);
        for (var k = 0; k < Entries.Length; k++)
            Tag(c, ref Entries[k].Part);
        for (var k = 0; k < Loot.Length; k++)
            Tag(c, ref Loot[k].Part);
        for (var k = 0; k < Hitches.Length; k++)
            Tag(c, ref Hitches[k].Part);
        for (var k = 0; k < Winches.Length; k++)
            Tag(c, ref Winches[k]);
        for (var k = 0; k < Stowed.Length; k++)
            if (!Stowed[k].Loose)
                Tag(c, ref Stowed[k].Container);
        _stamp = int.MinValue;
        c.Brief = this;
        return null;

        static void Tag(Construct c, ref BriefRef r)
        {
            r.Tag = c.Parts[r.Index].Tag;
            r.Live = (short)r.Index;
        }
    }

    // ---- The save ----------------------------------------------------------------------------------------------------------------

    /// <summary>The brief as bytes (a world save's body, after its tracks: WorldSaveBodies.cs).</summary>
    public void Write(SaveWriter w)
    {
        w.U8(FormatVersion);
        w.Str(Name).Str(Style).Str(Shape).Str(Role).Str(Model).U8(Tier).U8(Kit);
        w.Str(Slot).Str(Formation).I32(Convoy).U8(ConvoyIndex).U8((byte)Lane).U16(Leader);
        w.Vec(Forward).F32(Mass).F32(Length).F32(Width).F32(Height).F32(TopSpeed);
        w.U8((byte)(Measured ? 1 : 0)).F32(PulseSpeed).F32(PulseMoved).F32(Accel);
        w.U8((byte)Steer).F32(MaxSteer).F32(Wheelbase).F32(Track).F32(TurnRadius).U8(Wheels).U8(Driven);
        w.F32(Clearance).F32(ComHeight).F32(TipSide).F32(TipEnd).F32(Climb);
        w.F32(Fuel).F32(Burn).U8((byte)(Electric ? 1 : 0)).F32(Charge).F32(Endurance).F32(Range);
        w.I32(Guns.Length);
        foreach (var g in Guns)
        {
            Ref(w, g.Part);
            w.U8((byte)g.Mount).U8((byte)g.Class).U8((byte)g.Band).F32(g.Range).F32(g.MaxRange).F32(g.Dps);
            w.U8((byte)(g.FullCircle ? 1 : 0)).F32(g.YawMin).F32(g.YawMax).F32(g.ElevMin).F32(g.ElevMax).U32(g.Clear).U32(g.Level).Vec(g.Pivot).U8((byte)g.Seat);
            Ref(w, g.Input);
        }
        w.F32(Engage).U8(BestBin).U8(BestSide).F32(BestDps);
        w.I32(Weak.Length);
        foreach (var x in Weak)
        {
            Ref(w, x.Part);
            w.Str(x.Kind).U16(x.Bearings).U8((byte)(x.Top ? 1 : 0));
        }
        w.U8(KeepAway);
        w.I32(Seats.Length);
        foreach (var s in Seats)
        {
            Ref(w, s.Part);
            w.U8((byte)s.Controls).U16(s.Guns).U8((byte)(s.Open ? 1 : 0));
        }
        w.I32(Entries.Length);
        foreach (var e in Entries)
        {
            Ref(w, e.Part);
            w.U8((byte)e.Kind);
        }
        w.I32(Loot.Length);
        foreach (var l in Loot)
        {
            Ref(w, l.Part);
            w.Str(l.Table).U8(l.Tier);
        }
        w.I32(Hitches.Length);
        foreach (var h in Hitches)
        {
            Ref(w, h.Part);
            w.U8((byte)h.Kind);
        }
        w.I32(Winches.Length);
        foreach (var r in Winches)
            Ref(w, r);
        w.I32(Cargo).U8(FuelOpen);
        w.I32(Stowed.Length);
        foreach (var x in Stowed)
        {
            w.Str(x.Part).U8((byte)x.Kind).U8(x.Count).U8((byte)((x.Loose ? 1 : 0) | (x.CooksOff ? 2 : 0))).U8((byte)x.Slot).U32(x.Guns);
            if (x.Loose)
                w.Vec(x.At);
            else
                Ref(w, x.Container);
        }
    }

    private static void Ref(SaveWriter w, in BriefRef r) => w.U16(r.Index).U16(r.Tag).Vec(r.At);

    /// <summary>
    /// A brief from bytes <see cref="Write"/> wrote, with the parts' defs taken from the loaded construct (a def isn't saved: names change between builds, tags don't).
    /// Null for a later format. Throws <see cref="WorldFileException"/> on bytes that don't fit.
    /// </summary>
    public static VehicleBrief? Read(SaveReader r)
    {
        var version = r.U8();
        if (version is < 1 or > FormatVersion)
            return null;
        var b = new VehicleBrief
        {
            Name = r.Str(), Style = r.Str(), Shape = r.Str(), Role = r.Str(), Model = r.Str(), Tier = r.U8(), Kit = r.U8(),
            Slot = r.Str(), Formation = r.Str(), Convoy = r.I32(), ConvoyIndex = r.U8(), Lane = (sbyte)r.U8(), Leader = r.U16(),
            Forward = r.Vec(), Mass = r.F32(), Length = r.F32(), Width = r.F32(), Height = r.F32(), TopSpeed = r.F32(),
            Measured = r.U8() != 0, PulseSpeed = r.F32(), PulseMoved = r.F32(), Accel = r.F32(),
            Steer = (BriefSteer)r.U8(), MaxSteer = r.F32(), Wheelbase = r.F32(), Track = r.F32(), TurnRadius = r.F32(), Wheels = r.U8(), Driven = r.U8(),
            Clearance = r.F32(), ComHeight = r.F32(), TipSide = r.F32(), TipEnd = r.F32(), Climb = r.F32(),
            Fuel = r.F32(), Burn = r.F32(), Electric = r.U8() != 0, Charge = r.F32(), Endurance = r.F32(), Range = r.F32(),
        };
        b.Guns = new BriefGun[r.Count(40)];
        for (var k = 0; k < b.Guns.Length; k++)
        {
            ref var g = ref b.Guns[k];
            g.Part = ReadRef(r);
            (g.Mount, g.Class, g.Band, g.Range, g.MaxRange, g.Dps) = ((BriefMount)r.U8(), (GunClass)r.U8(), (RangeBand)r.U8(), r.F32(), r.F32(), r.F32());
            (g.FullCircle, g.YawMin, g.YawMax, g.ElevMin, g.ElevMax, g.Clear, g.Level, g.Pivot, g.Seat) = (r.U8() != 0, r.F32(), r.F32(), r.F32(), r.F32(), r.U32(), r.U32(), r.Vec(), (sbyte)r.U8());
            g.Input = ReadRef(r);
        }
        (b.Engage, b.BestBin, b.BestSide, b.BestDps) = (r.F32(), r.U8(), r.U8(), r.F32());
        b.Weak = new BriefWeak[r.Count(20)];
        for (var k = 0; k < b.Weak.Length; k++)
            b.Weak[k] = new BriefWeak { Part = ReadRef(r), Kind = r.Str(), Bearings = r.U16(), Top = r.U8() != 0 };
        b.KeepAway = r.U8();
        b.Seats = new BriefSeat[r.Count(20)];
        for (var k = 0; k < b.Seats.Length; k++)
            b.Seats[k] = new BriefSeat { Part = ReadRef(r), Controls = (SeatControls)r.U8(), Guns = r.U16(), Open = r.U8() != 0 };
        b.Entries = new BriefEntry[r.Count(17)];
        for (var k = 0; k < b.Entries.Length; k++)
            b.Entries[k] = new BriefEntry { Part = ReadRef(r), Kind = (EntryKind)r.U8() };
        b.Loot = new BriefLoot[r.Count(21)];
        for (var k = 0; k < b.Loot.Length; k++)
            b.Loot[k] = new BriefLoot { Part = ReadRef(r), Table = r.Str(), Tier = r.U8() };
        b.Hitches = new BriefHitch[r.Count(17)];
        for (var k = 0; k < b.Hitches.Length; k++)
            b.Hitches[k] = new BriefHitch { Part = ReadRef(r), Kind = (HitchKind)r.U8() };
        b.Winches = new BriefRef[r.Count(16)];
        for (var k = 0; k < b.Winches.Length; k++)
            b.Winches[k] = ReadRef(r);
        (b.Cargo, b.FuelOpen) = (r.I32(), r.U8());
        if (version >= 2)
        {
            b.Stowed = new BriefStow[r.Count(12)];
            for (var k = 0; k < b.Stowed.Length; k++)
            {
                ref var x = ref b.Stowed[k];
                (x.Part, x.Kind, x.Count) = (r.Str(), (StowKind)r.U8(), r.U8());
                var flags = r.U8();
                (x.Loose, x.CooksOff, x.Slot, x.Guns) = ((flags & 1) != 0, (flags & 2) != 0, (sbyte)r.U8(), r.U32());
                if (x.Loose)
                    x.At = r.Vec();
                else
                    x.Container = ReadRef(r);
            }
        }
        return b;

        static BriefRef ReadRef(SaveReader r) => new() { Index = r.U16(), Tag = r.U16(), At = r.Vec(), Live = -1 };
    }

    /// <summary>After a load: each reference takes its def from the part its tag finds on <paramref name="c"/> (the save names no defs), then the brief rides on it.</summary>
    public void Loaded(Construct c)
    {
        _stamp = int.MinValue;
        Refresh(c);
        void Def(ref BriefRef r)
        {
            if (r.Live >= 0)
                r.Def = c.Parts[r.Live].Def;
        }
        for (var k = 0; k < Guns.Length; k++)
        {
            Def(ref Guns[k].Part);
            if (Guns[k].Input.Tag != 0)
                Def(ref Guns[k].Input);
        }
        for (var k = 0; k < Weak.Length; k++)
            Def(ref Weak[k].Part);
        for (var k = 0; k < Seats.Length; k++)
            Def(ref Seats[k].Part);
        for (var k = 0; k < Entries.Length; k++)
            Def(ref Entries[k].Part);
        for (var k = 0; k < Loot.Length; k++)
            Def(ref Loot[k].Part);
        for (var k = 0; k < Hitches.Length; k++)
            Def(ref Hitches[k].Part);
        for (var k = 0; k < Winches.Length; k++)
            Def(ref Winches[k]);
        for (var k = 0; k < Stowed.Length; k++)
            if (!Stowed[k].Loose)
                Def(ref Stowed[k].Container);
        c.Brief = this;
    }

    // ---- In words --------------------------------------------------------------------------------------------------------------

    /// <summary>The bins of a mask as bearing ranges ("315-45, 90-135"): <paramref name="bins"/> bins round, 0 dead ahead.</summary>
    public static string Arcs(uint mask, int bins)
    {
        if (mask == 0)
            return "none";
        var full = bins == 32 ? uint.MaxValue : (1u << bins) - 1;
        if ((mask & full) == full)
            return "all round";
        var step = 360f / bins;
        var parts = new List<string>();
        var start = 0;
        while (start < bins && (mask >> start & 1) != 0)
            start++; // (begin at a gap, so an arc over dead ahead stays whole)
        for (var k = 0; k < bins; k++)
        {
            var i = (start + k) % bins;
            if ((mask >> i & 1) == 0)
                continue;
            var j = k;
            while (j + 1 < bins && (mask >> ((start + j + 1) % bins) & 1) != 0)
                j++;
            var a = (i * step - step / 2 + 360) % 360;
            var e = (((start + j) % bins) * step + step / 2) % 360;
            parts.Add($"{a:0}-{e:0}");
            k = j;
        }
        return string.Join(", ", parts);
    }

    private static string F(float x, string fmt = "0.0") => x.ToString(fmt, CultureInfo.InvariantCulture);

    /// <summary>The brief as lines of text (<c>--brief</c>, the dev overlay's panel). Allocates: for tools only.</summary>
    public List<string> Describe(Construct? c = null)
    {
        if (c != null)
            Refresh(c);
        string Part(in BriefRef r) => ConstructParts.Def(r.Def).Name + (c == null ? $" #{r.Index}" : r.Live < 0 ? " (gone)" : c.Parts[r.Live].Broken ? $" #{r.Live} (broken)" : $" #{r.Live}");
        var l = new List<string>
        {
            $"{Name}: {Style} {Shape}{(Role.Length > 0 ? $", {Role} tier {Tier}" : "")}, kit {Kit}{(Model.Length > 0 ? $", model {Model}" : "")}",
        };
        if (Slot.Length > 0)
            l.Add($"  convoy {Convoy}: {Slot}, place {ConvoyIndex} in a {Formation}, lane {Lane}{(Leader != 0 ? $", leader construct {Leader}" : "")}");
        l.Add($"  driving: {F(Mass / 1000, "0.00")} t, {F(Length)} x {F(Width)} x {F(Height)} m, est. top speed {F(TopSpeed, "0")} m/s" + (Measured ? $", pulse {F(PulseMoved, "0.00")} m to {F(PulseSpeed)} m/s (accel {F(Accel, "0.00")} m/s2)" : ", not drive-checked"));
        l.Add($"  turning: {Steer switch { BriefSteer.Wheels => $"steered wheels, {F(MaxSteer, "0")} deg lock, turn radius {F(TurnRadius)} m", BriefSteer.Skid => "skid steer, turns on the spot", _ => "doesn't steer" }}; {Wheels} wheels, {Driven} driven, wheelbase {F(Wheelbase, "0.00")} m, track {F(Track, "0.00")} m");
        l.Add($"  ground: clearance {F(Clearance, "0.00")} m, centre of mass {F(ComHeight, "0.00")} m up, tips at {F(TipSide, "0")} deg sideways and {F(TipEnd, "0")} deg end on, climbs about {F(Climb, "0")} deg");
        l.Add(Electric
            ? $"  power: electric, {F(Charge, "0")} kJ, {F(Endurance)} min at full throttle, about {F(Range)} km"
            : $"  fuel: {F(Fuel, "0")} l at {F(Burn, "0.000")} l/s full throttle: {F(Endurance)} min, about {F(Range)} km");
        l.Add(Guns.Length == 0 ? "  guns: none" : $"  guns: {Guns.Length}; engage at {F(Engage, "0")} m; best bearing {F(BinBearing(BestBin), "0")} deg ({SideNames[BestSide]}, {F(BestDps, "0")} dps)");
        for (var k = 0; k < Guns.Length; k++)
        {
            var g = Guns[k];
            var mount = g.Mount switch
            {
                BriefMount.Turret => g.FullCircle ? "turret all round" : $"turret {F(g.YawMin, "0")} to {F(g.YawMax, "0")} deg",
                BriefMount.Hinge => "hinged",
                _ => "fixed",
            };
            var fire = g.Seat >= 0 ? $"seat {g.Seat}" : g.Input.Def != 0 ? Part(g.Input) : "nobody";
            l.Add($"    gun {k}: {Part(g.Part)}, {mount}, elevation {F(g.ElevMin, "0")} to {F(g.ElevMax, "0")} deg, {g.Class.ToString().ToLowerInvariant()}, {g.Band.ToString().ToLowerInvariant()} ({F(g.Range, "0")} m, reach {F(g.MaxRange, "0")}), {F(g.Dps, "0")} dps, fired by {fire}");
            l.Add($"      clear {Arcs(g.Clear, Bins)}; level {Arcs(g.Level, Bins)}");
        }
        l.Add(Weak.Length == 0 ? "  weak points: none" : $"  weak points: {Weak.Length}, keep the {(KeepAway < 4 ? SideNames[KeepAway] : "-")} away; fuel in the open {FuelOpen}");
        foreach (var w in Weak)
            l.Add($"    {w.Kind}: {Part(w.Part)}, hit from {Arcs(w.Bearings, WeakBins)}{(w.Top ? ", from above" : "")}");
        l.Add($"  crew: {Seats.Length} seats");
        for (var k = 0; k < Seats.Length; k++)
        {
            var s = Seats[k];
            var what = new List<string>();
            if ((s.Controls & SeatControls.Drive) != 0)
                what.Add("drives");
            if (s.Guns != 0)
                what.Add("guns " + string.Join("+", Enumerable.Range(0, 16).Where(i => (s.Guns >> i & 1) != 0)));
            if ((s.Controls & SeatControls.Winch) != 0)
                what.Add("winch");
            if ((s.Controls & SeatControls.Signals) != 0)
                what.Add("signals");
            if ((s.Controls & SeatControls.Other) != 0)
                what.Add("other wired parts");
            l.Add($"    seat {k}: {Part(s.Part)}, {(what.Count > 0 ? string.Join(", ", what) : "rides")}{(s.Open ? ", open" : "")}");
        }
        if (Entries.Length > 0)
            l.Add("  ways in: " + string.Join("; ", Entries.Select(e => $"{e.Kind.ToString().ToLowerInvariant()} {Part(e.Part)} at {F(e.Part.At.Y, "0.0")} m up")));
        l.Add($"  loot: {Loot.Length} containers{(Loot.Length > 0 ? " (" + string.Join(", ", Loot.GroupBy(x => x.Table).Select(g => $"{g.Count()} {g.Key}")) + ")" : "")}, {Cargo} cargo pieces"
            + (Hitches.Length > 0 ? "; hitches " + string.Join(", ", Hitches.Select(h => $"{h.Kind.ToString().ToLowerInvariant()} {Part(h.Part)}")) : "")
            + (Winches.Length > 0 ? "; winches " + string.Join(", ", Winches.Select(w => Part(w))) : ""));
        if (Stowed.Length > 0)
        {
            l.Add($"  its own cargo: {Stowed.Sum(x => x.Count)} pieces, {Stowed.Where(x => x.Loose).Sum(x => x.Count)} loose ({Stowed.Where(x => x.CooksOff).Sum(x => x.Count)} cook off)");
            foreach (var x in Stowed)
                l.Add($"    {Vehicle.KindWord(x.Kind)}: {x.Count} {x.Part} {(x.Loose ? $"loose at [{F(x.At.X, "0.0")}, {F(x.At.Y, "0.0")}, {F(x.At.Z, "0.0")}]{(x.CooksOff ? ", cooks off" : "")}" : $"in {Part(x.Container)} slot {x.Slot}")}"
                    + (x.Guns != 0 ? ", for gun " + string.Join("+", Enumerable.Range(0, 32).Where(i => (x.Guns >> i & 1) != 0)) : ""));
        }
        return l;
    }

    public override string ToString() => string.Join("\n", Describe());

    /// <summary>A short line for the log ("brief: 2 guns engage 40 m, best right; 5 weak points, keep the back away; 3 seats").</summary>
    public string Summary =>
        new StringBuilder().Append(Guns.Length).Append(Guns.Length == 1 ? " gun" : " guns")
            .Append(Guns.Length > 0 ? $" engage {F(Engage, "0")} m, best {SideNames[BestSide]}" : "")
            .Append($"; {Weak.Length} weak points{(KeepAway < 4 ? ", keep the " + SideNames[KeepAway] + " away" : "")}; {Seats.Length} seats; top {F(TopSpeed, "0")} m/s, {Steer.ToString().ToLowerInvariant()} steer")
            .Append(Stowed.Length > 0 ? $"; cargo {Stowed.Sum(x => x.Count)} ({Stowed.Where(x => x.Kind is StowKind.Ammo or StowKind.Magazine).Sum(x => x.Count)} ammo and magazines, {Stowed.Where(x => x.CooksOff).Sum(x => x.Count)} loose rounds)" : "").ToString();
}
