using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a part is to the module socket standard (docs/sandbox/design/module-sockets.md).</summary>
public enum ModPart : byte
{
    None,
    Socket, // a Module Socket plate (or a Module Stand: a plate on a frame, where a module is stored, not worked)
    Plug, // a Module Plug: whatever is welded onto it is a module
}

public sealed partial class ConstructPartDef
{
    public ModPart ModPart; // "moduleSocket" / "modulePlug": ModuleSockets.cs
    public byte ModW, ModL; // the footprint, whole metres, W <= L: W along the part's x, L along its z, the face's normal its y
    public bool ModRound; // "round": true: a round plate or plug, W the diameter (W == L): takes only a round one of its diameter (module-sockets.md 2.7)
    public bool ModStand; // "moduleSocket": {"stand": true}: a Module Stand (a module on it is stored: nothing couples)
    public bool ModNoHide; // "moduleSocket": {"hide": false}: no Hide connector setting (a plate that is a whole wall section: the Wall Bay Socket)
    public float ModStandAbove; // "moduleSocket": {"stand": true, "above": m}: a stand's room height when its own part roofs it (a container's cradle); 0: the table's default
    public int ModSocket = -1; // its Module socket's index (the plate's face, +y; the plug's, -y)
    public byte ModData; // its data pass-through ports, "Data 1".."Data n" both ways (2 at 1x1 and 1xN, 4 from 2x2; a stand none): ModuleSockets.PassWires
}

/// <summary>One Module Socket plate of a construct, as the rebuild found it (<see cref="Construct.ModSockets"/>).</summary>
public struct CModSocket
{
    public ushort Plate; // the plate part
    public byte W, L;
    public bool Round; // a round plate: takes only a round plug of its diameter W
    public ModKind Kind;
    public ModStyle Style;
    public bool Keyed, Stand;
    public float Above; // the room's height over the plate face (proud), or over the surface (opening) (m)
    public float Depth; // flush and Well: the well's depth from the plate's face to its floor; Opening: how far it punches on behind the plate's back (m)
    public float Height; // the room's height over the plate face: proud, Well and Opening Above, flush FlushStand (its detail)
    public float Behind; // flush, Well, Opening: how far the room goes back behind the plate's face (its thickness and Depth); proud 0
    public float Seat; // flush and Well: how far behind the plate's face the module's plug seats (the well's floor, Depth); else 0
    public int Module; // the module fitted to it (an index into Construct.Modules), or -1
    public int InTheWay; // the first part of the ship whose solid is in its room (-1: clear)
    public Vector3 RoomMin, RoomMax; // its room in the plate's segment rest frame
    public bool WrongKind; // a restricted socket whose module's primary tag no longer matches it ("not an engine any more")
}

/// <summary>One module of a construct: a Module Plug and every part joined to it on its own side (the plug flood, 4.5).</summary>
public struct CModule
{
    public ushort Plug;
    public int Socket; // the socket it is fitted to (an index into Construct.ModSockets), or -1 (loose, or on a stand: Stand)
    public bool OnStand;
    public ushort Tags; // a bit per ModTag
    public ModTag Primary;
    public int Parts;
    public float Mass, Thrust, Made, Draw; // kg, N, kW made, W drawn
    public int Guns, Stores, Harvest;
    public Vector3 Min, Max; // its bounds (the plug's segment rest frame)
    public bool Bridged; // its parts reach the hull other than through its plug (welded to more than its socket)
}

/// <summary>
/// Module sockets (docs/sandbox/design/module-sockets.md, phase 1: the standard, proud, by hand). A Module Socket plate is a
/// part of a whole-metre footprint whose "Module" socket takes only a Module Plug of the same footprint, at the turns the
/// footprint allows (4 square, 2 oblong, 1 keyed). A module is a plug and everything welded on its own side: the rebuild
/// floods from each plug through its links, never crossing a Module socket, and marks the parts (<see cref="Construct.ModuleOf"/>);
/// its tags and a cheap stats summary come from those parts. Fitting welds the module's body into the ship's
/// (<see cref="ConstructWorld.Merge"/>), so a fitted module is plain parts with no cost per tick; taking it off cuts the
/// plug-plate link's parts out as a body of their own. All of it runs at a rebuild or an edit, never per tick.
/// </summary>
public static class ModuleSockets
{
    public const int MaxSockets = 64; // a construct's sockets the rebuild tracks (more are listed but have no room check: logged)
    public const float Grid = 0.01f;
    public const string PlateName = "Module Socket", PlugName = "Module Plug", StandName = "Module Stand";
    public const float FlushStand = 0.004f; // m: a flush module's face stands this far proud of the surface at most (its detail; z-fighting rule)
    public const float FlushTolerance = 0.01f; // m: a flush module fits when its face is within this of the well's depth (the 1 cm grid)

    // ---- Part files --------------------------------------------------------------------------------------------------

    /// <summary><c>"moduleSocket": {"w", "l", "stand"}</c> or <c>"modulePlug": {"w", "l"}</c>.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        var plate = ConstructParts.Opt(root, "moduleSocket");
        var plug = ConstructParts.Opt(root, "modulePlug");
        if (plate == null && plug == null)
        {
            if (d.Sockets.Any(s => s.Kind == SocketKind.Module))
                throw new ConstructPartException($"{file}: a \"module\" socket belongs to a Module Socket or a Module Plug (\"moduleSocket\" or \"modulePlug\")");
            return;
        }
        if (plate != null && plug != null)
            throw new ConstructPartException($"{file}: a part is a Module Socket or a Module Plug, not both");
        var e = (plate ?? plug)!.Value;
        var key = plate != null ? "moduleSocket" : "modulePlug";
        ConstructParts.Known(e, plate != null ? ["w", "l", "stand", "above", "hide", "round"] : ["w", "l", "round"], $"{file}, {key}");
        var w = (int)ConstructParts.Num(ConstructParts.Need(e, "w", $"{file}, {key}"), file, key + ".w");
        var l = (int)ConstructParts.Num(ConstructParts.Need(e, "l", $"{file}, {key}"), file, key + ".l");
        if (!ModuleSocketTable.ValidSize(w, l))
            throw new ConstructPartException($"{file}: \"{key}\" is {w} x {l}: whole metres, 1 <= w <= l <= {ModuleSocketTable.MostSide}");
        d.ModPart = plate != null ? ModPart.Socket : ModPart.Plug;
        d.ModW = (byte)w;
        d.ModL = (byte)l;
        d.ModRound = ConstructParts.Opt(e, "round") is { ValueKind: JsonValueKind.True };
        if (d.ModRound && w != l)
            throw new ConstructPartException($"{file}: a round \"{key}\" is as wide as it is long (its diameter): {w} x {l}");
        d.ModStand = plate != null && ConstructParts.Opt(e, "stand") is { ValueKind: JsonValueKind.True };
        d.ModNoHide = plate != null && ConstructParts.Opt(e, "hide") is { ValueKind: JsonValueKind.False };
        if (ConstructParts.Opt(e, "above") is { } ab)
        {
            if (!d.ModStand)
                throw new ConstructPartException($"{file}: \"moduleSocket.above\" is a stand's (a plate's room comes from its settings)");
            d.ModStandAbove = (float)ConstructParts.Num(ab, file, key + ".above");
            if (d.ModStandAbove < 0.5f * w || d.ModStandAbove > ModuleSocketTable.DefaultAbove(w))
                throw new ConstructPartException($"{file}: \"moduleSocket.above\" is {d.ModStandAbove} m: from half the stand's width to the default {ModuleSocketTable.DefaultAbove(w)} m");
        }
        var faces = plate != null ? ConstructParts.DirOf("+y", file, key) : ConstructParts.DirOf("-y", file, key);
        for (var s = 0; s < d.Sockets.Length; s++)
            if (d.Sockets[s].Kind == SocketKind.Module)
            {
                if (d.ModSocket >= 0)
                    throw new ConstructPartException($"{file}: a {(plate != null ? "plate" : "plug")} has one \"module\" socket");
                if (d.Sockets[s].Dir != faces)
                    throw new ConstructPartException($"{file}: the \"module\" socket faces {(plate != null ? "+y (out of the plate)" : "-y (out of the plug)")}");
                if (MathF.Abs(d.Sockets[s].At.X - Centre(w)) > 0.006f || MathF.Abs(d.Sockets[s].At.Z - Centre(l)) > 0.006f)
                    throw new ConstructPartException($"{file}: the \"module\" socket stands in the footprint's middle ({Centre(w):0.##}, {Centre(l):0.##})");
                d.ModSocket = s;
            }
        if (d.ModSocket < 0)
            throw new ConstructPartException($"{file}: a {(plate != null ? "plate" : "plug")} has a \"module\" socket (kind \"module\")");
        if (!d.ModStand)
        {
            d.ModData = (byte)DataPorts(w, l);
            d.DataIn = d.DataOut = DataNames[..d.ModData];
            if (plate != null) // (a plate's own controls after its pass-throughs, Sean 2026-11-21: ModuleGuide.WireStep, PortValue)
            {
                d.DataIn = [.. DataNames[..d.ModData], .. ControlIns];
                d.DataOut = [.. DataNames[..d.ModData], .. ControlOuts];
            }
        }
    }

    /// <summary>A size's data pass-through ports (2.5): Data 1-2, and 3-4 from 2x2.</summary>
    public static int DataPorts(int w, int l) => w * l >= 4 ? 4 : 2;

    private static readonly string[] DataNames = ["Data 1", "Data 2", "Data 3", "Data 4"];

    /// <summary>A plate's control inputs after its Data ports: a rise on Attach fits a module near (or ejects the fitted one), a rise on Eject ejects, a high Lock locks.</summary>
    public static readonly string[] ControlIns = ["Attach", "Eject", "Lock"];
    /// <summary>A plate's outputs after its Data ports: 1 while a module is fitted, 1 while it is locked.</summary>
    public static readonly string[] ControlOuts = ["Attached", "Locked"];
    public const int AttachIn = 0, EjectIn = 1, LockIn = 2, AttachedOut = 0, LockedOut = 1; // (offsets after ModData)

    /// <summary>A plate's control port <paramref name="port"/> (a wire's FromPort): its value. A Data port's is the wire it passes (PassWires), so 0 here.</summary>
    public static float PortValue(Construct c, int plate, int port)
    {
        var d = c.DefOf(plate);
        var k = port - d.ModData;
        if (k < 0 || d.ModStand)
            return 0;
        var si = SocketOfPlate(c, plate);
        if (si < 0)
            return 0;
        return k switch
        {
            AttachedOut => c.ModSockets[si].Module >= 0 ? 1 : 0,
            LockedOut => Locked(c, plate) ? 1 : 0,
            _ => 0,
        };
    }

    /// <summary>Is plate <paramref name="plate"/> locked: its slider on, or a high Lock wire (the server's last wire step, kept in the plate's Mem)?</summary>
    public static bool Locked(Construct c, int plate)
    {
        ref var p = ref c.Parts[plate];
        var d = c.DefOf(plate);
        return d.ModPart == ModPart.Socket && !d.ModStand && (ConstructConfig.Get(p, d, CfgKey.ModLock, 0) >= 0.5f || ((int)p.Mem & ModuleGuide.MemLockHigh) != 0);
    }

    // The frame's controls (Sean 2026-11-21): an eject button and a lock slider on the plate's +x side, its -z end, standing out of it; the
    // part kit draws them (ModuleSocketParts.Controls) and the client finds F on them by these. In the plate's own frame.
    public const float CtlY = -0.44f; // the controls' height: the middle of the plate's side (its back at -0.5, its face at -0.38)
    public const float EjectZ = 0.22f, LockZ0 = 0.38f, LockZ1 = 0.58f; // from the -z edge: the button's middle, the slider's track
    public const float CtlReach = 0.09f; // m: F this near a control's middle uses it

    /// <summary>Where a plate's side controls stand, in its own frame: the side's x at a point <paramref name="fromEdge"/> m along it (a round plate's: on its circle).</summary>
    public static Vector3 ControlAt(ConstructPartDef d, float fromEdge)
    {
        var z = -0.5f + fromEdge;
        if (!d.ModRound)
            return new Vector3(d.ModW - 0.5f, CtlY, z);
        float c = (d.ModW - 1) * 0.5f, r = d.ModW * 0.5f;
        var dz = z - c;
        return new Vector3(c + MathF.Sqrt(MathF.Max(0, r * r - dz * dz)), CtlY, z);
    }

    /// <summary>Which of a plate's side controls a point on it (its own frame) is at: 1 eject, 2 the lock slider, 0 neither.</summary>
    public static byte ControlNear(ConstructPartDef d, Vector3 local)
    {
        if (d.ModPart != ModPart.Socket || d.ModStand)
            return 0;
        if (Vector3.Distance(local, ControlAt(d, EjectZ)) <= CtlReach)
            return 1;
        var a = ControlAt(d, LockZ0);
        var b = ControlAt(d, LockZ1);
        var t = Math.Clamp(Vector3.Dot(local - a, b - a) / MathF.Max(1e-4f, (b - a).LengthSquared()), 0, 1);
        return Vector3.Distance(local, a + (b - a) * t) <= CtlReach ? (byte)2 : (byte)0;
    }
    public const int DataWires = 4; // wires each data port takes in (ConstructPartDef.MaxInputs)

    /// <summary>The footprint's middle along a side of n metres: the part's origin is the middle of its first metre cell.</summary>
    public static float Centre(int n) => (n - 1) * 0.5f;

    /// <summary>A plate's settings (kind, style, depth, above, keyed) and a plug's primary tag (ConstructConfig.Parse).</summary>
    public static void AddSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        void Add(CConfigDef e)
        {
            if (!list.Exists(x => x.Id == e.Id))
                list.Add(e);
        }
        var t = ModuleSocketTable.Current;
        if (d.ModPart == ModPart.Socket && !d.ModStand)
        {
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModKind), Id = CfgKey.ModKind, Type = CfgType.Choice, Options = ModuleSocketTable.KindLabels, Label = "Takes", Group = "Module socket",
                Tooltip = "Any module of its size, or engines, reactors or turrets only (by the module's primary tag)." });
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModStyle), Id = CfgKey.ModStyle, Type = CfgType.Choice, Options = ModuleSocketTable.StyleLabels, Label = "Style", Group = "Module socket",
                Tooltip = "Proud: the module stands on the hull. Flush and Well: the plate sits at the foot of a well it cuts out through the hull. Opening: the plate is a frame with a hole, and punches through whatever it is on, Depth deep, so the module can retract into it." });
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModDepth), Id = CfgKey.ModDepth, Type = CfgType.Number, Min = t.DepthStep, Max = MathF.Max(t.DepthStep * 2, t.MostDepth(d.ModW)), Step = t.DepthStep, Default = t.DepthStep, Unit = "m",
                Label = "Depth", Group = "Module socket", Tooltip = "Flush and Well: how deep the well goes below the surface. Opening: how far the hole goes back behind the plate." });
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModAbove), Id = CfgKey.ModAbove, Type = CfgType.Number, Min = 0, Max = t.MostAbove(d.ModL), Step = t.AboveStep, Default = 0, Unit = "m",
                Label = "Room above", Group = "Module socket", Tooltip = "How tall a module may stand over the plate (0: twice its short side). Nothing of the ship may be in this room." });
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModCut), Id = CfgKey.ModCut, Type = CfgType.Number, Hidden = true, Min = 0, Max = 100000, Default = 0, Unit = "s",
                Label = "Cut", Group = "Module socket", Tooltip = "Not on the panel." });
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModKeyed), Id = CfgKey.ModKeyed, Type = CfgType.Toggle, Label = "Keyed", Group = "Module socket",
                Tooltip = "On: a module fits at one turn only (the notched corner), so a nozzle can only face one way." });
        }
        if (d.ModPart == ModPart.Socket) // (a stand too: a fitter sets a module down on one by a weld)
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModWeld), Id = CfgKey.ModWeld, Type = CfgType.Number, Hidden = true, Min = -100000, Max = 100000, Default = 0, Unit = "s",
                Label = "Weld", Group = "Module socket", Tooltip = "Not on the panel." });
        if (d.ModPart == ModPart.Socket && !d.ModStand && !d.ModNoHide) // (Sean, 2026-11-21: decorative flush mounts; after every older setting, so saved slots keep)
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModHide), Id = CfgKey.ModHide, Type = CfgType.Toggle, Label = "Hide connector", Group = "Module socket",
                Tooltip = "On: the plate and a fitted module's plug aren't drawn (they still hold, collide and take hits), for a clean flush mount." });
        if (d.ModPart == ModPart.Socket && !d.ModStand) // (Sean, 2026-11-21: the lock slider on the frame; after every older setting)
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModLock), Id = CfgKey.ModLock, Type = CfgType.Toggle, Label = "Locked", Group = "Module socket",
                Tooltip = "On: the fitted module can't be ejected, pulled off with the physics gun or taken off by hand (a Module Fitter still cuts it free). The slider on the plate's side, or a high Lock wire, does the same." });
        if (d.ModPart == ModPart.Plug)
        {
            var opts = new string[1 + (int)ModTag.COUNT];
            opts[0] = "Auto";
            for (var i = 0; i < (int)ModTag.COUNT; i++)
                opts[i + 1] = TagLabel((ModTag)i);
            // (a choice takes at most 16 options: Auto and 12 tags)
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModPrimary), Id = CfgKey.ModPrimary, Type = CfgType.Choice, Options = opts, Label = "Primary", Group = "Module",
                Tooltip = "What this module counts as for a restricted socket (Auto: the tag with the most mass behind it). It must be one of its tags." });
        }
    }

    /// <summary>
    /// Is part <paramref name="i"/> a socket's connector the player has hidden (Sean, 2026-11-21: "the socket connections appear
    /// invisible ... nice, decorative, flush mount sockets")? A plate with Hide connector on, or the plug of a module fitted to one.
    /// Render only: it still holds, collides and takes hits. Cheap (no allocation): the renderer asks it of every part.
    /// </summary>
    public static bool HiddenLook(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        if (d.ModPart == ModPart.Socket)
            return !d.ModStand && !d.ModNoHide && ConstructConfig.Get(p, d, CfgKey.ModHide, 0) >= 0.5f;
        if (d.ModPart != ModPart.Plug || c.ModuleOf is not { } of || i >= of.Length || of[i] is var m && (m < 0 || m >= c.Modules.Length))
            return false;
        var s = c.Modules[m].Socket;
        if (c.Modules[m].Plug != i || s < 0 || s >= c.ModSockets.Length || c.ModSockets[s].Plate >= c.PartCount)
            return false;
        var plate = c.ModSockets[s].Plate;
        return c.Parts[plate].D.ModPart == ModPart.Socket && HiddenLook(c, plate);
    }

    public static string TagName(ModTag t) => ModuleSocketTable.TagNames[(int)t];

    public static string TagLabel(ModTag t) => t switch
    {
        ModTag.TurretBay => "Turret bay",
        ModTag.PowerStore => "Power store",
        ModTag.LifeSupport => "Life support",
        _ => char.ToUpperInvariant(TagName(t)[0]) + TagName(t)[1..],
    };

    public static string TagsText(ushort tags)
    {
        if (tags == 0)
            return "nothing yet";
        var s = "";
        for (var i = 0; i < (int)ModTag.COUNT; i++)
            if ((tags & (1 << i)) != 0)
                s += (s.Length > 0 ? ", " : "") + TagName((ModTag)i);
        return s;
    }

    /// <summary>The tag the kind of a restricted socket asks for (null: any).</summary>
    public static ModTag? KindTag(ModKind k) => k switch
    {
        ModKind.Engine => ModTag.Engine,
        ModKind.Reactor => ModTag.Reactor,
        ModKind.Turret => ModTag.Turret,
        _ => null,
    };

    // ---- What a part gives a module ---------------------------------------------------------------------------------

    /// <summary>The tag a part gives the module it is in, or COUNT (none: the plug, structure).</summary>
    public static ModTag TagOf(ConstructPartDef d, in CPart p)
    {
        if (d.ModPart == ModPart.Plug)
            return ModTag.COUNT;
        if (d.ModPart == ModPart.Socket)
            return !d.ModStand && (ModKind)(int)ConstructConfig.Get(p, d, CfgKey.ModKind, 0) == ModKind.Turret ? ModTag.TurretBay : ModTag.COUNT;
        if (d.Role is PartRole.Thruster or PartRole.Engine || d.MotorTorque > 0)
            return ModTag.Engine;
        if (d.Role == PartRole.Generator)
            return ModTag.Reactor;
        if (d.Logic == CLogic.BombRack || d.IsModularRack)
            return ModTag.Missile;
        if (d.Role == PartRole.Gun)
            return ModTag.Turret;
        if (d.IsGatherer || d.Scoop != null)
            return ModTag.Harvest;
        if (d.Role == PartRole.Battery || d.CapacitorKJ > 0)
            return ModTag.PowerStore;
        if (d.RadarRange > 0 || d.IsCamera)
            return ModTag.Sensor;
        if (d.Role == PartRole.Seat || d.Name.Contains("Bunk", StringComparison.Ordinal))
            return ModTag.LifeSupport;
        if (d.Role == PartRole.Storage || ConstructCargo.IsVolume(d) || d.Role is PartRole.FuelTank or PartRole.FluidTank)
            return ModTag.Cargo;
        if (d.Section.Contains("Armour", StringComparison.OrdinalIgnoreCase) || d.Folder.Contains("Armour", StringComparison.OrdinalIgnoreCase))
            return ModTag.Armour;
        if (d.Role is PartRole.Frame or PartRole.Panel)
            return ModTag.COUNT;
        return ModTag.Utility;
    }

    // ---- The rebuild ----------------------------------------------------------------------------------------------

    [ThreadStatic] private static int[]? _stack;
    [ThreadStatic] private static float[]? _tagMass;

    /// <summary>
    /// The construct's sockets and modules from its parts (at every rebuild, after the ship works): which plug is on which
    /// plate, the plug flood, each module's tags and stats, each socket's room and what is in it. Bounded: each part is
    /// visited once by the flood, each socket's room is tested against each part's bounds once. Nothing per tick.
    /// </summary>
    public static void Build(Construct c)
    {
        var n = c.PartCount;
        var any = false;
        for (var i = 0; i < n && !any; i++)
            any = c.DefOf(i).ModPart != ModPart.None;
        if (!any)
        {
            if (c.ModSockets.Length > 0 || c.Modules.Length > 0)
            {
                c.ModSockets = [];
                c.Modules = [];
                c.ModuleOf = null;
            }
            return;
        }
        var of = c.ModuleOf is { } old && old.Length >= n ? old : new short[Math.Max(n, 16)];
        Array.Fill(of, (short)-1, 0, n);
        c.ModuleOf = of;
        var sockets = new List<CModSocket>();
        var modules = new List<CModule>();
        var stack = _stack is { } st && st.Length >= n ? st : _stack = new int[Math.Max(n, 64)];
        var tagMass = _tagMass ??= new float[(int)ModTag.COUNT];
        // The plates first (so a module knows the socket it is on).
        for (var i = 0; i < n; i++)
        {
            var d = c.DefOf(i);
            if (d.ModPart != ModPart.Socket)
                continue;
            if (sockets.Count >= MaxSockets)
            {
                Console.WriteLine($"[modules] construct {c.Id}: more than {MaxSockets} module sockets: the {d.Name} (part {i}) is not tracked");
                continue;
            }
            ref var p = ref c.Parts[i];
            var s = new CModSocket
            {
                Plate = (ushort)i, W = d.ModW, L = d.ModL, Round = d.ModRound, Stand = d.ModStand, Module = -1, InTheWay = -1,
                Kind = d.ModStand ? ModKind.Generic : (ModKind)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.ModKind, 0), 0, ModuleSocketTable.KindNames.Length - 1),
                Style = d.ModStand ? ModStyle.Proud : (ModStyle)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.ModStyle, 0), 0, ModuleSocketTable.StyleNames.Length - 1),
                Keyed = !d.ModStand && ConstructConfig.Get(p, d, CfgKey.ModKeyed, 0) > 0.5f,
            };
            var above = d.ModStand ? d.ModStandAbove : ConstructConfig.Get(p, d, CfgKey.ModAbove, 0); // (a stand: its file's room, if it gives one)
            s.Above = above > 0 ? above : ModuleSocketTable.DefaultAbove(d.ModW);
            s.Depth = s.Style == ModStyle.Proud ? 0 : MathF.Max(ModuleSocketTable.Current.DepthStep, ConstructConfig.Get(p, d, CfgKey.ModDepth, ModuleSocketTable.Current.DepthStep));
            // Every style but Proud digs into what the plate is welded to (module-sockets.md 2.3, Sean 2026-11-21): the plate stays on the
            // surface as a frame, and the room goes back through it and the well it cut. Flush and Well seat the module at the well's floor,
            // Depth behind the plate's face (the frame's thickness is part of it); an Opening punches Depth on behind the plate's back.
            s.Behind = s.Style switch { ModStyle.Proud => 0, ModStyle.Through => Thickness(d) + s.Depth, _ => MathF.Max(s.Depth, Thickness(d) + 0.02f) };
            s.Seat = s.Style is ModStyle.Flush or ModStyle.Opening ? s.Behind : 0;
            s.Height = s.Style switch { ModStyle.Flush => FlushStand, ModStyle.Proud => s.Above, _ => s.Above };
            Room(c, i, s.Height, out s.RoomMin, out s.RoomMax, s.Style == ModStyle.Proud ? ModuleSocketTable.Current.Side : ModuleCut.WellSide, s.Behind);
            sockets.Add(s);
        }
        // Each plug and its flood.
        for (var i = 0; i < n; i++)
        {
            var d = c.DefOf(i);
            if (d.ModPart != ModPart.Plug || of[i] >= 0)
                continue;
            var mi = modules.Count;
            if (mi >= short.MaxValue)
                break;
            var m = new CModule { Plug = (ushort)i, Socket = -1 };
            // Which plate its face is on (the plug-plate link on the Module sockets).
            var plate = PlateOf(c, i);
            if (plate >= 0)
            {
                var si = sockets.FindIndex(x => x.Plate == plate);
                if (si >= 0 && sockets[si].Stand)
                {
                    m.OnStand = true;
                    var sst = sockets[si];
                    sst.Module = mi; // (stored on the stand: its room is the module's)
                    sockets[si] = sst;
                }
                else
                    m.Socket = si;
            }
            Array.Clear(tagMass);
            var top = 0;
            stack[top++] = i;
            of[i] = (short)mi;
            m.Min = new Vector3(float.MaxValue);
            m.Max = new Vector3(float.MinValue);
            var seg = c.Parts[i].Seg;
            while (top > 0)
            {
                var at = stack[--top];
                var pd = c.DefOf(at);
                ref var pp = ref c.Parts[at];
                m.Parts++;
                m.Mass += pd.Mass;
                if (pd.Role == PartRole.Thruster)
                    m.Thrust += pd.ThrustForce + pd.AbForce;
                if (pd.Role == PartRole.Generator)
                    m.Made += pd.PlantRate;
                m.Draw += pd.PowerDraw;
                if (pd.Role == PartRole.Gun)
                    m.Guns++;
                if (pd.Role == PartRole.Storage)
                    m.Stores++;
                if (pd.IsGatherer || pd.Scoop != null)
                    m.Harvest++;
                var tag = TagOf(pd, pp);
                if (tag != ModTag.COUNT)
                {
                    m.Tags |= (ushort)(1 << (int)tag);
                    tagMass[(int)tag] += pd.Mass;
                }
                if (pp.Seg == seg)
                {
                    c.PartBounds(at, out var bmn, out var bmx);
                    m.Min = Vector3.Min(m.Min, bmn);
                    m.Max = Vector3.Max(m.Max, bmx);
                }
                for (var e = c.FirstLink(at); e >= 0; e = c.NextLink(e))
                {
                    ref var l = ref c.Links[e >> 1];
                    var other = c.OtherEnd(e);
                    if (KindOf(c, l.A, l.SA) == SocketKind.Module || KindOf(c, l.B, l.SB) == SocketKind.Module)
                        continue; // (never across a plug's or a plate's face: beyond is the ship, or a submodule)
                    if (other == plate)
                        m.Bridged = true;
                    if (of[other] >= 0)
                    {
                        if (of[other] != mi)
                            m.Bridged = true;
                        continue;
                    }
                    if (plate >= 0 && of[other] < 0 && c.DefOf(other).ModPart == ModPart.Plug)
                        m.Bridged = true; // (welded to another module's plug past its face)
                    of[other] = (short)mi;
                    stack[top++] = other;
                }
                // A freeform weld (a face point or an off-grid turn of the socket weld) joins its anchor and its welded-on part
                // with no socket link: what hangs on the module (its anchor a module part) is the module's too, or fitting and
                // taking off cut the module's parts away from what is welded on them. Only outward, anchor to welded-on part:
                // a module part welded onto something else (a module body freeform-welded to a hull) hangs on that, so the
                // flood stops there (a fitted module is then welded to more than its socket), never flooding into the ship.
                for (var j = 0; j < c.JointCount; j++)
                {
                    ref var jt = ref c.Joints[j];
                    if (!jt.Freeform || jt.Cut)
                        continue;
                    if (jt.Child == at && jt.Hinge != at)
                    {
                        if (plate >= 0 && jt.Hinge < n && of[jt.Hinge] != mi)
                            m.Bridged = true;
                        continue;
                    }
                    var other = jt.Hinge == at ? jt.Child : -1;
                    if (other < 0 || other >= n)
                        continue;
                    if (other == plate)
                    {
                        m.Bridged = true;
                        continue;
                    }
                    if (of[other] >= 0)
                    {
                        if (of[other] != mi)
                            m.Bridged = true;
                        continue;
                    }
                    if (plate >= 0 && c.DefOf(other).ModPart == ModPart.Plug)
                        m.Bridged = true;
                    of[other] = (short)mi;
                    stack[top++] = other;
                }
            }
            if (m.Tags == 0 && m.Parts > 1)
                m.Tags = 1 << (int)ModTag.Utility;
            m.Primary = PrimaryOf(c, i, m.Tags, tagMass);
            if (m.Socket >= 0)
            {
                var s = sockets[m.Socket];
                s.Module = mi;
                s.WrongKind = KindTag(s.Kind) is { } want && m.Primary != want;
                sockets[m.Socket] = s;
            }
            modules.Add(m);
        }
        // Each socket's room: the first of the ship's solids in it (not the plate, not its own module's parts).
        var pending = false;
        for (var si = 0; si < sockets.Count; si++)
        {
            var s = sockets[si];
            s.InTheWay = InRoom(c, s, s.Module);
            sockets[si] = s;
            pending |= ModuleCut.Wants(s) || !s.Stand && ConstructConfig.Get(c.Parts[s.Plate], c.DefOf(s.Plate), CfgKey.ModCut, 0) > 0
                || ConstructConfig.Get(c.Parts[s.Plate], c.DefOf(s.Plate), CfgKey.ModWeld, 0) != 0; // (a cut to start, or a stale cut or fitter's weld to clear)
        }
        c.ModCutPending = pending; // (the server's ModuleCut looks at this body: a well wants cutting)
        c.ModSockets = [.. sockets];
        c.Modules = [.. modules];
    }

    private static SocketKind KindOf(Construct c, int part, int socket)
    {
        var d = c.DefOf(part);
        return socket < d.Sockets.Length ? d.Sockets[socket].Kind : SocketKind.Struct;
    }

    /// <summary>The plate whose Module socket plug <paramref name="plug"/>'s face is linked to, or -1.</summary>
    public static int PlateOf(Construct c, int plug)
    {
        var d = c.DefOf(plug);
        for (var e = c.FirstLink(plug); e >= 0; e = c.NextLink(e))
        {
            ref var l = ref c.Links[e >> 1];
            var mine = (e & 1) == 0 ? l.SA : l.SB;
            if (mine != d.ModSocket)
                continue;
            var other = c.OtherEnd(e);
            if (c.DefOf(other).ModPart == ModPart.Socket)
                return other;
        }
        return -1;
    }

    /// <summary>
    /// The pass-throughs (2.5), after the routes' rebuild: a pipe, an ammo feed or a chute leaving a fitted plug's port carries what the
    /// link of that type ending at the plate's port of the same name carries: its source (a tank, a magazine or an Ammo Switch, a store
    /// of the ship), fed only when that link is fed to its end. Then the data wires: <see cref="PassWires"/>. A plug on a stand passes
    /// nothing. Repeated for modules in modules (up to <see cref="PassDepth"/>), then the branches of each link changed take their
    /// root's source again. Nothing allocated; a body with no plug links or wires costs one pass over its routes.
    /// </summary>
    public static void PassThrough(Construct c)
    {
        var any = false;
        for (var k = 0; k < c.RouteCount && !any; k++)
            any = Passes(c.Routes[k].Type) && c.Routes[k].Parent == 255 && c.Routes[k].From < c.PartCount && c.DefOf(c.Routes[k].From).ModPart == ModPart.Plug;
        if (any)
            for (var pass = 0; pass < PassDepth; pass++)
            {
                var changed = false;
                for (var k = 0; k < c.RouteCount; k++)
                {
                    ref var r = ref c.Routes[k];
                    if (!Passes(r.Type) || r.Parent != 255 || r.From >= c.PartCount || c.DefOf(r.From).ModPart != ModPart.Plug)
                        continue;
                    var j = PlateLink(c, r.Type, r.From, r.FromPort);
                    ushort src = r.From;
                    byte port = r.FromPort;
                    var fed = true;
                    if (j >= 0 && j != k)
                        (src, port, fed) = (c.Routes[j].Source, c.Routes[j].SourcePort, c.Routes[j].FedEnd);
                    if (src != r.Source || port != r.SourcePort || fed != r.Fed)
                    {
                        (r.Source, r.SourcePort, r.Fed) = (src, port, fed);
                        changed = true;
                    }
                }
                for (var k = 0; k < c.RouteCount; k++)
                {
                    ref var r = ref c.Routes[k];
                    if (!Passes(r.Type) || r.Parent == 255 || r.Parent >= k)
                        continue;
                    ref var p = ref c.Routes[r.Parent];
                    (r.Source, r.SourcePort, r.Fed) = (p.Source, p.SourcePort, p.Fed && p.FirstCut > r.ParentNode);
                }
                if (!changed)
                    break;
            }
        PassWires(c);
    }

    private static bool Passes(RouteType t) => t is RouteType.Pipe or RouteType.Feed or RouteType.Chute;

    /// <summary>
    /// The data pass-through (2.5): Data k on a fitted plug and on its plate act as one wire end, both ways. A wire leaving the plug's
    /// Data k takes as its source the first wire into the plate's Data k, and a wire leaving the plate's Data k the first wire into the
    /// plug's: so a ship's lever lights a module's lamp, and a module's sensor reaches a ship's display, with no value held on the plate.
    /// Nested modules repeat it (up to <see cref="PassDepth"/> passes). A stand passes nothing; a wire whose source would be itself is
    /// left. At the routes' rebuild, on every peer alike; nothing per tick.
    /// </summary>
    public static void PassWires(Construct c)
    {
        var any = false;
        for (var w = 0; w < c.WireCount && !any; w++)
            any = c.Wires[w].From < c.PartCount && c.DefOf(c.Wires[w].From).ModData > 0;
        if (!any)
            return;
        for (var pass = 0; pass < PassDepth; pass++)
        {
            var changed = false;
            for (var w = 0; w < c.WireCount; w++)
            {
                ref var x = ref c.Wires[w];
                if (x.From >= c.PartCount || c.DefOf(x.From).ModData == 0 || x.FromPort >= c.DefOf(x.From).ModData)
                    continue; // (a plate's own outputs, Attached and Locked, after its Data ports: its own values)
                var d = c.DefOf(x.From);
                var other = d.ModPart == ModPart.Plug ? PlateOf(c, x.From) : PlugOn(c, x.From);
                if (other < 0 || c.DefOf(other).ModStand || d.ModPart == ModPart.Plug && c.DefOf(other).ModStand)
                    continue;
                for (var v = 0; v < c.WireCount; v++)
                    if (v != w && c.Wires[v].To == other && c.Wires[v].ToPort == x.FromPort && c.Wires[v].From < c.PartCount && c.Wires[v].From != x.To)
                    {
                        (x.From, x.FromPort) = (c.Wires[v].From, c.Wires[v].FromPort);
                        changed = true;
                        break;
                    }
            }
            if (!changed)
                break;
        }
    }

    /// <summary>The plug fitted to plate <paramref name="plate"/> (linked at its Module socket), or -1.</summary>
    public static int PlugOn(Construct c, int plate)
    {
        var d = c.DefOf(plate);
        if (d.ModPart != ModPart.Socket || d.ModStand)
            return -1;
        for (var e = c.FirstLink(plate); e >= 0; e = c.NextLink(e))
        {
            ref var l = ref c.Links[e >> 1];
            if (((e & 1) == 0 ? l.SA : l.SB) != d.ModSocket)
                continue;
            var other = c.OtherEnd(e);
            if (c.DefOf(other).ModPart == ModPart.Plug)
                return other;
        }
        return -1;
    }

    /// <summary>The link of type <paramref name="t"/> that ends at the plate plug <paramref name="plug"/> is fitted to, at the plate's port named as the plug's port <paramref name="port"/>, or -1.</summary>
    private static int PlateLink(Construct c, RouteType t, int plug, byte port)
    {
        if (ConstructRoutes.PortOf(c.DefOf(plug), t, port) is not { } np || PlateOf(c, plug) is var plate && plate < 0 || c.DefOf(plate).ModStand)
            return -1;
        foreach (var pp in c.DefOf(plate).NamedPorts)
            if (pp.Type == t && string.Equals(pp.Name, np.Name, StringComparison.OrdinalIgnoreCase))
                for (var j = 0; j < c.RouteCount; j++)
                    if (c.Routes[j].Type == t && c.Routes[j].To == plate && c.Routes[j].ToPort == pp.Byte)
                        return j;
        return -1;
    }

    /// <summary>Why a module can't go on a plate welded on freeform off the 90-degree grid.</summary>
    public const string OffGrid = "this socket is welded on turned off the craft's grid: weld it on square (a multiple of 90 degrees) to fit a module";

    /// <summary>
    /// The frame plate <paramref name="plate"/>'s numbers are in, as a turn and an origin of the body's rest frame: none for a plate
    /// on the body or on a plain joint's segment; a freeform weld's rest pose for one welded on freeform (a face point or an
    /// off-grid spin of the socket weld, as a player's own socket usually is). False when that weld is off the 90-degree grid,
    /// where a module's turn can't be square to both the plate and the body.
    /// </summary>
    public static bool PlateFrame(Construct c, int plate, out byte turn, out Vector3 origin)
    {
        turn = 0;
        origin = default;
        var seg = c.Parts[plate].Seg;
        if (seg == 0 || c.FrameOf(seg) == 0)
            return true;
        c.RestPose(seg, out var q, out origin);
        turn = Rot24.Nearest(q, out var match);
        return match >= 0.999f;
    }

    /// <summary>Plate <paramref name="plate"/>'s turn and its Module socket's face (place and facing) in the body's rest frame; false off the grid (<see cref="PlateFrame"/>).</summary>
    public static bool PlateRest(Construct c, int plate, out byte rot, out Vector3 faceAt, out byte faceDir)
    {
        var d = c.DefOf(plate);
        c.SocketAt(plate, d.ModSocket, out faceAt, out faceDir);
        rot = c.Parts[plate].Rot;
        if (!PlateFrame(c, plate, out var turn, out var origin))
            return false;
        if (turn != 0 || origin != Vector3.Zero)
        {
            faceAt = origin + Rot24.Apply(turn, faceAt);
            faceDir = Rot24.ApplyDir(turn, faceDir);
            rot = Rot24.Compose(turn, rot);
        }
        return true;
    }

    /// <summary>
    /// A module merged onto plate <paramref name="plate"/> by a plan made in the body's rest frame (turn and offset): onto a plate
    /// welded on freeform, the plan goes into that weld's frame and segment, so the module rides the weld with its plate.
    /// </summary>
    public static void PlanOnPlate(Construct c, int plate, ref JoinPlan plan)
    {
        var seg = c.Parts[plate].Seg;
        if (seg == 0 || c.FrameOf(seg) == 0 || !PlateFrame(c, plate, out var turn, out var origin))
            return;
        var back = SubBlueprints.Inverse(turn);
        plan.Turn = Rot24.Compose(back, plan.Turn);
        plan.Offset = ConstructWorld.Quantise(Rot24.Apply(back, plan.Offset - origin));
        plan.Seg = seg;
    }

    /// <summary>A box of segment <paramref name="seg"/>'s frame as an axis-aligned box of the body's rest frame (a freeform weld turns it; RestPose).</summary>
    public static void RestAabb(Construct c, int seg, ref Vector3 centre, ref Vector3 half)
    {
        if (seg == 0)
            return;
        c.RestPose(seg, out var q, out var t);
        if (q == Quaternion.Identity && t == Vector3.Zero)
            return;
        var m = Matrix4x4.CreateFromQuaternion(q);
        centre = t + Vector3.Transform(centre, q);
        half = new Vector3(
            MathF.Abs(m.M11) * half.X + MathF.Abs(m.M21) * half.Y + MathF.Abs(m.M31) * half.Z,
            MathF.Abs(m.M12) * half.X + MathF.Abs(m.M22) * half.Y + MathF.Abs(m.M32) * half.Z,
            MathF.Abs(m.M13) * half.X + MathF.Abs(m.M23) * half.Y + MathF.Abs(m.M33) * half.Z);
    }

    /// <summary>Part <paramref name="part"/>'s bounds in the body's rest frame.</summary>
    public static void RestBounds(Construct c, int part, out Vector3 min, out Vector3 max)
    {
        c.PartBounds(part, out min, out max);
        var mid = (min + max) * 0.5f;
        var half = (max - min) * 0.5f;
        RestAabb(c, c.Parts[part].Seg, ref mid, ref half);
        min = mid - half;
        max = mid + half;
    }

    /// <summary>The plate plug <paramref name="plug"/> is fitted to when it is an engine socket's (a ship-works mount: ConstructWorks.FindMount), else -1.</summary>
    public static int EngineMountOf(Construct c, int plug)
    {
        var plate = PlateOf(c, plug);
        if (plate < 0)
            return -1;
        var d = c.DefOf(plate);
        return !d.ModStand && (ModKind)(int)ConstructConfig.Get(c.Parts[plate], d, CfgKey.ModKind, 0) == ModKind.Engine ? plate : -1;
    }

    public const int PassDepth = 8; // modules in modules a pass-through crosses (the use limit's depth)

    /// <summary>The primary tag: the plug's own setting when it is one of the module's tags, else the tag with the most mass.</summary>
    private static ModTag PrimaryOf(Construct c, int plug, ushort tags, float[] tagMass)
    {
        ref var p = ref c.Parts[plug];
        var set = (int)ConstructConfig.Get(p, p.D, CfgKey.ModPrimary, 0);
        if (set > 0 && set <= (int)ModTag.COUNT && (tags & (1 << (set - 1))) != 0)
            return (ModTag)(set - 1);
        var best = ModTag.Utility;
        var most = -1f;
        for (var t = 0; t < (int)ModTag.COUNT; t++)
            if ((tags & (1 << t)) != 0 && tagMass[t] > most)
                (best, most) = ((ModTag)t, tagMass[t]);
        return best;
    }

    /// <summary>A plate's room (proud): the footprint plus the side clearance, from the plate's face up <paramref name="above"/> m, in the rest frame.</summary>
    public static void Room(Construct c, int plate, float above, out Vector3 min, out Vector3 max, float side, float below = 0)
    {
        var d = c.DefOf(plate);
        ref var p = ref c.Parts[plate];
        var face = d.Sockets[d.ModSocket].At;
        var lo = new Vector3(face.X - d.ModW * 0.5f - side, face.Y - below, face.Z - d.ModL * 0.5f - side);
        var hi = new Vector3(face.X + d.ModW * 0.5f + side, face.Y + above, face.Z + d.ModL * 0.5f + side);
        var mid = p.Pos + Rot24.Apply(p.Rot, (lo + hi) * 0.5f);
        var half = Rot24.ApplyHalf(p.Rot, (hi - lo) * 0.5f);
        RestAabb(c, p.Seg, ref mid, ref half); // (the body's rest frame, as every part's bounds are compared)
        min = mid - half;
        max = mid + half;
    }

    /// <summary>The first part of <paramref name="c"/> (not the plate, not module <paramref name="module"/>'s) whose solid is in socket <paramref name="s"/>'s room, or -1.</summary>
    private static int InRoom(Construct c, in CModSocket s, int module)
    {
        var shrink = new Vector3(0.01f);
        Vector3 lo = s.RoomMin + shrink, hi = s.RoomMax - shrink;
        var of = c.ModuleOf;
        var hits = Construct.PartScratch; // (the parts near the room only: the part tree, as the build's overlap test asks)
        var near = c.PartsInBox(lo, hi, hits);
        for (var h = 0; h < near; h++)
        {
            var j = hits[h];
            if (j == s.Plate || !c.Rigid(c.Parts[j].Seg) || module >= 0 && of != null && of[j] == module)
                continue; // (a joint's moving parts move out of the way; a freeform weld's are rigid: in the rest frame)
            RestBounds(c, j, out var mn, out var mx);
            if (mn.X >= hi.X || mx.X <= lo.X || mn.Y >= hi.Y || mx.Y <= lo.Y || mn.Z >= hi.Z || mx.Z <= lo.Z)
                continue;
            var d = c.DefOf(j);
            for (var k = 0; k < d.SolidBoxes; k++)
            {
                c.BoxOf(j, k, out var oc, out var oh);
                RestAabb(c, c.Parts[j].Seg, ref oc, ref oh);
                if (oc.X - oh.X < hi.X && oc.X + oh.X > lo.X && oc.Y - oh.Y < hi.Y && oc.Y + oh.Y > lo.Y && oc.Z - oh.Z < hi.Z && oc.Z + oh.Z > lo.Z)
                    return j;
            }
        }
        return -1;
    }

    /// <summary>A plate's thickness: its module socket's face over the bottom of its boxes (its back).</summary>
    public static float Thickness(ConstructPartDef d) => d.ModSocket >= 0 ? MathF.Max(0.01f, d.Sockets[d.ModSocket].At.Y - d.BoundsMin.Y) : 0.1f;

    /// <summary>
    /// Is part <paramref name="i"/> drawn as a frame (its "middle" boxes left out)? A plate in the Opening style (<see cref="ModStyle.Through"/>),
    /// and a plug fitted to one, so the module can pass back through both. As the rebuild found the sockets, on every peer.
    /// </summary>
    public static bool FrameLook(Construct c, int i)
    {
        var d = c.DefOf(i);
        if (d.ModPart == ModPart.None || !d.HasMiddle)
            return false;
        var socks = c.ModSockets;
        if (d.ModPart == ModPart.Socket)
        {
            for (var k = 0; k < socks.Length; k++)
                if (socks[k].Plate == i)
                    return socks[k].Style != ModStyle.Proud; // (every dug style: the plate is the well's rim)
            return false;
        }
        var mi = ModuleOfPart(c, i);
        if (mi < 0 || mi >= c.Modules.Length || c.Modules[mi].Plug != i)
            return false;
        var si = c.Modules[mi].Socket;
        return (uint)si < (uint)socks.Length && socks[si].Style == ModStyle.Through;
    }

    public static int SocketOfPlate(Construct c, int plate)
    {
        var s = c.ModSockets;
        for (var i = 0; i < s.Length; i++)
            if (s[i].Plate == plate)
                return i;
        return -1;
    }

    public static int ModuleOfPart(Construct c, int part) => c.ModuleOf is { } of && (uint)part < (uint)c.PartCount && part < of.Length ? of[part] : -1;

    // ---- The fit's rules ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Do the plate's footprint axes and the plug's line up at this turn: the long sides along each other (an oblong
    /// footprint), and a keyed socket the same way round? <paramref name="plateRot"/> and <paramref name="plugRot"/> are
    /// Rot24 turns in one frame.
    /// </summary>
    public static bool TurnFits(ConstructPartDef plate, byte plateRot, ConstructPartDef plug, byte plugRot, bool keyed)
    {
        var pl = Rot24.Apply(plateRot, Vector3.UnitZ);
        var ql = Rot24.Apply(plugRot, Vector3.UnitZ);
        var dot = Vector3.Dot(pl, ql);
        if (keyed)
            return dot > 0.9f && Vector3.Dot(Rot24.Apply(plateRot, Vector3.UnitX), Rot24.Apply(plugRot, Vector3.UnitX)) > 0.9f;
        return plate.ModW == plate.ModL || MathF.Abs(dot) > 0.9f;
    }

    /// <summary>Does plug def <paramref name="plug"/> match socket <paramref name="s"/>'s size and shape (round or square)?</summary>
    public static bool Matches(in CModSocket s, ConstructPartDef plug) => plug.ModW == s.W && plug.ModL == s.L && plug.ModRound == s.Round;

    /// <summary>A plug of this size and the plate's: null when they match, else why not.</summary>
    public static string? SizeRefusal(ConstructPartDef plate, ConstructPartDef plug)
    {
        if (plate.ModPart != ModPart.Socket || plug.ModPart != ModPart.Plug)
            return "a Module Plug goes on a Module Socket's face";
        if (plate.ModW != plug.ModW || plate.ModL != plug.ModL || plate.ModRound != plug.ModRound)
            return $"that's a {ModuleSocketTable.SizeText(plate.ModW, plate.ModL, plate.ModRound)} socket and a {ModuleSocketTable.SizeText(plug.ModW, plug.ModL, plug.ModRound)} module: sizes and shapes match exactly (an adapter module takes a smaller one)";
        return null;
    }

    /// <summary>
    /// Why a module's part (bounds <paramref name="a"/> to <paramref name="b"/>) is out of a socket's room (<paramref name="lo"/> to
    /// <paramref name="hi"/>), in the rest frame: how far it sticks out, past which side, and the setting that would change it
    /// (module-sockets.md 2.4, Sean 2026-11-21).
    /// </summary>
    public static string OutOfRoom(in CModSocket s, string part, Vector3 a, Vector3 b, Vector3 lo, Vector3 hi, int axis, float sign)
    {
        float up = 0, down = 0, side = 0;
        for (var k = 0; k < 3; k++)
        {
            var under = lo[k] - a[k];
            var over = b[k] - hi[k];
            if (k == axis)
            {
                up = MathF.Max(up, sign > 0 ? over : under);
                down = MathF.Max(down, sign > 0 ? under : over);
            }
            else
                side = MathF.Max(side, MathF.Max(under, over));
        }
        if (side >= up && side >= down)
            return $"the {part} sticks out {side:0.00} m past the socket's side ({ModuleSocketTable.SizeText(s.W, s.L, s.Round)}): use a bigger socket";
        if (down >= up)
            return s.Style == ModStyle.Proud
                ? $"the {part} goes {down:0.00} m below the plate's face: a proud module stands on its plate"
                : $"the {part} goes {down:0.00} m past the well's floor ({s.Behind:0.00} m deep): set Depth {MathF.Ceiling(down / ModuleSocketTable.Current.DepthStep) * ModuleSocketTable.Current.DepthStep:0.00} m more";
        return s.Style == ModStyle.Flush
            ? $"the {part} stands {up:0.00} m out of the flush well: a flush module ends level with the hull (set Depth {MathF.Ceiling(up / ModuleSocketTable.Current.DepthStep) * ModuleSocketTable.Current.DepthStep:0.00} m more, or use a Well socket)"
            : $"the {part} sticks out {up:0.00} m over the socket's room ({s.Height:0.##} m over the face): set Room above {MathF.Ceiling(up / ModuleSocketTable.Current.AboveStep) * ModuleSocketTable.Current.AboveStep:0.##} m more";
    }

    /// <summary>
    /// The rules of a fit (3.2) of module <paramref name="m"/> of <paramref name="mover"/> (its plug turned and placed at
    /// <paramref name="plugPos"/> / <paramref name="plugRot"/> in <paramref name="ship"/>'s frame) onto socket <paramref name="si"/>:
    /// the size and turn, the room clear, the module inside the room, its rating, the restricted kind, and in Survival the
    /// hand's limits (<paramref name="byHand"/>). Null, or why not.
    /// </summary>
    public static string? FitRefusal(Construct ship, int si, Construct mover, int mi, Quaternion moverRot, Vector3 moverPos, bool byHand, bool creative, bool moduleOnly = false, float lift = ConstructGrab.SurvivalLiftCap)
    {
        if ((uint)si >= (uint)ship.ModSockets.Length || (uint)mi >= (uint)mover.Modules.Length)
            return "no module there";
        var s = ship.ModSockets[si];
        var m = mover.Modules[mi];
        var plateDef = ship.DefOf(s.Plate);
        var plugDef = mover.DefOf(m.Plug);
        if (SizeRefusal(plateDef, plugDef) is { } size)
            return size;
        if (s.Module >= 0)
            return "a module is already fitted there: take it off first";
        if (s.InTheWay >= 0)
            return $"in the way: the {ship.DefOf(s.InTheWay).Name} (part {s.InTheWay}) is in the socket's room";
        var of = mover.ModuleOf;
        if (of != null && !moduleOnly)
            for (var j = 0; j < mover.PartCount; j++)
                if (of[j] != mi)
                    return $"take the module off what it stands on first (the {mover.DefOf(j).Name} isn't part of it)";
        if (m.Bridged)
            return "that module is welded to more than its plug's face";
        var t = ModuleSocketTable.Current;
        if (m.Mass > t.MassRating(s.W, s.L))
            return $"the module weighs {m.Mass / 1000f:0.#} t: a {ModuleSocketTable.SizeText(s.W, s.L, s.Round)} socket holds {t.MassRating(s.W, s.L) / 1000f:0.#} t";
        if (m.Thrust > t.ThrustRating(s.W, s.L))
            return $"the module pushes {m.Thrust / 1000f:0} kN: a {ModuleSocketTable.SizeText(s.W, s.L, s.Round)} socket takes {t.ThrustRating(s.W, s.L) / 1000f:0} kN";
        if (KindTag(s.Kind) is { } want && m.Primary != want)
            return $"this socket takes {TagName(want)} modules only: this one is {TagName(m.Primary)} ({TagsText(m.Tags)})";
        if (byHand && !creative)
        {
            if (!t.HandSize(s.W, s.L))
                return $"a {ModuleSocketTable.SizeText(s.W, s.L, s.Round)} module is fitted by a Module Fitter: by hand, up to {ModuleSocketTable.SizeText(t.HandUpToW, t.HandUpToL)}";
            if (m.Mass > lift)
                return $"the module weighs {m.Mass / 1000f:0.#} t: by hand up to {lift / 1000f:0.#} t (a Module Fitter lifts more)";
        }
        // The turn: the plug's turn in the ship's frame.
        var inv = Quaternion.Conjugate(ship.Rot);
        var bodyTurn = Rot24.Nearest(inv * moverRot, out var match);
        if (match < 0.999f)
            return "it isn't square to the socket";
        var plugRot = Rot24.Compose(bodyTurn, mover.Parts[m.Plug].Rot);
        if (!PlateRest(ship, s.Plate, out var plateRot, out var faceAt, out var plateDir))
            return OffGrid;
        if (!TurnFits(plateDef, plateRot, plugDef, plugRot, s.Keyed))
            return s.Keyed ? "a keyed socket takes its module one way round only: turn it (the notched corners together)" : "turn it: the module's long side goes along the socket's";
        // Inside the room: every part of the module, in the ship's frame.
        var rel = Vector3.Transform(moverPos - ship.Pos, inv);
        Vector3 lo = s.RoomMin - new Vector3(0.006f), hi = s.RoomMax + new Vector3(0.006f);
        int axis = plateDir >> 1;
        var sign = (plateDir & 1) == 0 ? 1f : -1f;
        var top = 0f; // (the module's highest point over the plate's face: a flush module's face)
        for (var j = 0; j < mover.PartCount; j++)
        {
            if (moduleOnly && of != null && of[j] != mi)
                continue; // (what it stands on: it comes off that first)
            if (!mover.Rigid(mover.Parts[j].Seg))
                continue; // (a joint's moving parts: their sweep is ModuleSweep's; their rest pose is checked with the body by the overlap test)
            RestBounds(mover, j, out var mn, out var mx);
            var c0 = rel + Rot24.Apply(bodyTurn, (mn + mx) * 0.5f);
            var h0 = Rot24.ApplyHalf(bodyTurn, (mx - mn) * 0.5f);
            var a = c0 - h0;
            var b = c0 + h0;
            if (a.X < lo.X || a.Y < lo.Y || a.Z < lo.Z || b.X > hi.X || b.Y > hi.Y || b.Z > hi.Z)
                return OutOfRoom(s, mover.DefOf(j).Name, a, b, lo, hi, axis, sign);
            top = MathF.Max(top, sign > 0 ? b[axis] - faceAt[axis] : faceAt[axis] - a[axis]);
        }
        if (s.Style == ModStyle.Flush && top < -FlushTolerance)
        {
            var tall = top + s.Seat; // (the module's height over the well's floor)
            var step = ModuleSocketTable.Current.DepthStep;
            var depthWant = MathF.Max(step, MathF.Round(tall / step) * step);
            return $"a flush module's face is level with the hull: this one is {tall:0.00} m tall and the well {s.Seat:0.00} m deep, so it ends {-top:0.00} m down (set Depth to {depthWant:0.00} m, or use a Well socket)";
        }
        // Its joints swept through their ranges against the ship (3.3): refused in Survival, a warning in Creative.
        if (ModuleSweep.Clash(ship, si, mover, mi, bodyTurn, rel, moduleOnly) is { } clash)
        {
            if (!creative)
                return clash;
            Console.WriteLine($"[modules] Creative: fitted although {clash}");
        }
        return null;
    }

    /// <summary>
    /// A part placed onto (or beside) a fitted module (4.5): it must lie inside the socket's room and join the module's
    /// parts only, never the ship's as well; and the module stays within its rating. Null, or why not. The ghost and the
    /// server's place both ask (ConstructBuild.GhostRefusal).
    /// </summary>
    public static string? PlaceRefusal(Construct c, ConstructPartDef def, int target, Vector3 pos, byte rot, float scale)
    {
        if (c.ModuleOf is not { } of || c.Modules.Length == 0 || (uint)target >= (uint)c.PartCount)
            return null;
        var mi = target < of.Length ? of[target] : -1;
        // Where the new part's bounds land.
        var mid = pos + Rot24.Apply(rot, (def.BoundsMin + def.BoundsMax) * 0.5f * scale);
        var half = Rot24.ApplyHalf(rot, (def.BoundsMax - def.BoundsMin) * 0.5f * scale);
        Vector3 a = mid - half, b = mid + half;
        if (mi >= 0 && c.Modules[mi].Socket >= 0)
        {
            var s = c.ModSockets[c.Modules[mi].Socket];
            Vector3 lo = s.RoomMin - new Vector3(0.006f), hi = s.RoomMax + new Vector3(0.006f);
            if (a.X < lo.X || a.Y < lo.Y || a.Z < lo.Z || b.X > hi.X || b.Y > hi.Y || b.Z > hi.Z)
                return "keep it inside the socket's room: it would stick out of the module's room";
            var t = ModuleSocketTable.Current;
            if (c.Modules[mi].Mass + def.Mass > t.MassRating(s.W, s.L))
                return $"the module would weigh {(c.Modules[mi].Mass + def.Mass) / 1000f:0.#} t: the socket holds {t.MassRating(s.W, s.L) / 1000f:0.#} t";
            if (def.Role == PartRole.Thruster && c.Modules[mi].Thrust + def.ThrustForce + def.AbForce > t.ThrustRating(s.W, s.L))
                return $"the module would push more than its socket's {t.ThrustRating(s.W, s.L) / 1000f:0} kN";
        }
        // Would its free sockets weld onto a part of another side (the module and the ship, or two modules)?
        var hits = Construct.PartScratch;
        var near = c.PartsInBox(a - new Vector3(0.05f), b + new Vector3(0.05f), hits);
        for (var hi2 = 0; hi2 < near; hi2++)
        {
            var j = hits[hi2];
            if (j == target)
                continue;
            var mj = j < of.Length ? of[j] : -1;
            if (mj == mi)
                continue;
            if (mi < 0 && mj >= 0 && c.Modules[mj].Socket < 0 && !c.Modules[mj].OnStand)
                continue; // (a loose module's body: building it, anything goes)
            if (Touches(c, def, pos, rot, j))
                return mi >= 0 ? "this would weld the module to the hull: keep it inside the socket's room" : "this would weld the hull to a fitted module: keep clear of it";
        }
        return null;
    }

    /// <summary>Would one of a new part's free Struct sockets meet one of part <paramref name="j"/>'s (the rebuild's auto link)?</summary>
    private static bool Touches(Construct c, ConstructPartDef def, Vector3 pos, byte rot, int j)
    {
        var od = c.DefOf(j);
        for (var s = 0; s < def.Sockets.Length; s++)
        {
            if (def.Sockets[s].Kind != SocketKind.Struct)
                continue;
            var at = pos + Rot24.Apply(rot, def.Sockets[s].At);
            var dir = Rot24.ApplyDir(rot, def.Sockets[s].Dir);
            for (var t = 0; t < od.Sockets.Length; t++)
            {
                if (od.Sockets[t].Kind != SocketKind.Struct || od.Sockets[t].Class != def.Sockets[s].Class || c.SocketUsed(j, t))
                    continue;
                c.SocketAt(j, t, out var oat, out var odir);
                if (odir == (dir ^ 1) && Vector3.DistanceSquared(oat, at) < 0.0004f)
                    return true;
            }
        }
        return false;
    }

    // ---- Look text --------------------------------------------------------------------------------------------------

    /// <summary>The look line's module text for part <paramref name="part"/> (a plate, a plug, or any part of a module), or null.</summary>
    public static string? LookText(Construct c, int part)
    {
        if ((uint)part >= (uint)c.PartCount)
            return null;
        var d = c.DefOf(part);
        if (d.ModPart == ModPart.Socket && SocketOfPlate(c, part) is var si and >= 0)
        {
            var s = c.ModSockets[si];
            var size = ModuleSocketTable.SizeText(s.W, s.L, s.Round);
            if (ModuleFitters.LookText(c, part) is { } fitting)
                return $"{size} {(s.Stand ? "stand" : "socket")}: {fitting}";
            if (s.Stand)
                return s.Module >= 0 ? $"{size} stand: {ModuleName(c, s.Module)} stored (F: take it off)" : $"{size} stand: empty (F with a module near: set it on)";
            var kind = s.Kind == ModKind.Generic ? "any module" : $"{TagName(KindTag(s.Kind)!.Value)} modules only";
            var head = $"{size} {ModuleSocketTable.StyleLabels[(int)s.Style].ToLowerInvariant()} socket, {kind}{(s.Keyed ? ", keyed" : "")}";
            if (s.Module >= 0)
                return $"{head}: {ModuleName(c, s.Module)} fitted{(s.WrongKind ? $" - not {Article(TagName(KindTag(s.Kind)!.Value))} any more: it does nothing here" : "")} (F: take it off)";
            if (s.InTheWay >= 0)
                return s.Style == ModStyle.Proud
                    ? $"{head}: blocked, the {c.DefOf(s.InTheWay).Name} is in its room"
                    : $"{head} {s.Depth:0.##} m: {ModuleCut.LookText(c, s, ModuleCut.Seen(c, s.Plate)) ?? $"the {c.DefOf(s.InTheWay).Name} is in its well"}";
            return $"{head}{(s.Style == ModStyle.Proud ? "" : $" {s.Depth:0.##} m")}: free (F with a module within {ModuleSocketTable.Current.HandPull:0.#} m, or in a Module Fitter's reach: fit it)";
        }
        var mi = ModuleOfPart(c, part);
        if (mi < 0)
            return null;
        var m = c.Modules[mi];
        var where = m.Socket >= 0 ? "fitted" : m.OnStand ? "on a stand" : "loose";
        return $"{ModuleName(c, mi)} ({where}): {TagsText(m.Tags)}, primary {TagName(m.Primary)}; {m.Mass / 1000f:0.##} t, {m.Parts} parts{StatsText(m)}";
    }

    private static string Article(string s) => (s.Length > 0 && "aeiou".Contains(s[0]) ? "an " : "a ") + s;

    public static string ModuleName(Construct c, int mi)
    {
        var m = c.Modules[mi];
        var d = c.DefOf(m.Plug);
        return $"{ModuleSocketTable.SizeText(d.ModW, d.ModL, d.ModRound)} module";
    }

    public static string StatsText(in CModule m)
    {
        var s = "";
        if (m.Thrust > 0)
            s += $", {m.Thrust / 1000f:0} kN";
        if (m.Made > 0)
            s += $", makes {m.Made:0} kW";
        if (m.Draw > 0)
            s += $", draws {m.Draw / 1000f:0.#} kW";
        if (m.Guns > 0)
            s += $", {m.Guns} gun{(m.Guns > 1 ? "s" : "")}";
        if (m.Stores > 0)
            s += $", {m.Stores} store{(m.Stores > 1 ? "s" : "")}";
        if (m.Harvest > 0)
            s += $", {m.Harvest} harvester{(m.Harvest > 1 ? "s" : "")}";
        return s;
    }
}

public sealed partial class Construct
{
    public CModSocket[] ModSockets = []; // its Module Socket plates (ModuleSockets.Build, at the rebuild)
    public CModule[] Modules = []; // its modules: a plug and what is welded on its side
    public short[]? ModuleOf; // per part: the module it is in, or -1 (null: no module parts)
}
