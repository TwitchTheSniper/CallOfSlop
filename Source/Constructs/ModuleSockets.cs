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
    public bool ModStand; // "moduleSocket": {"stand": true}: a Module Stand (a module on it is stored: nothing couples)
    public int ModSocket = -1; // its Module socket's index (the plate's face, +y; the plug's, -y)
}

/// <summary>One Module Socket plate of a construct, as the rebuild found it (<see cref="Construct.ModSockets"/>).</summary>
public struct CModSocket
{
    public ushort Plate; // the plate part
    public byte W, L;
    public ModKind Kind;
    public ModStyle Style;
    public bool Keyed, Stand;
    public float Above; // the room's height over the plate face (proud), or over the surface (opening) (m)
    public float Depth; // flush and opening: the well's depth, plate face to the surface (m)
    public float Height; // the room's whole height over the plate face: proud Above, flush Depth, opening Depth + Above
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
        ConstructParts.Known(e, plate != null ? ["w", "l", "stand"] : ["w", "l"], $"{file}, {key}");
        var w = (int)ConstructParts.Num(ConstructParts.Need(e, "w", $"{file}, {key}"), file, key + ".w");
        var l = (int)ConstructParts.Num(ConstructParts.Need(e, "l", $"{file}, {key}"), file, key + ".l");
        if (!ModuleSocketTable.ValidSize(w, l))
            throw new ConstructPartException($"{file}: \"{key}\" is {w} x {l}: whole metres, 1 <= w <= l <= {ModuleSocketTable.MostSide}");
        d.ModPart = plate != null ? ModPart.Socket : ModPart.Plug;
        d.ModW = (byte)w;
        d.ModL = (byte)l;
        d.ModStand = plate != null && ConstructParts.Opt(e, "stand") is { ValueKind: JsonValueKind.True };
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
    }

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
                Tooltip = "Proud: the module stands on the hull. Flush and opening: the module sits in a well cut through the hull." });
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModDepth), Id = CfgKey.ModDepth, Type = CfgType.Number, Min = t.DepthStep, Max = MathF.Max(t.DepthStep * 2, t.MostDepth(d.ModW)), Step = t.DepthStep, Default = t.DepthStep, Unit = "m",
                Label = "Well depth", Group = "Module socket", Tooltip = "How deep a flush or opening well goes below the surface." });
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModAbove), Id = CfgKey.ModAbove, Type = CfgType.Number, Min = 0, Max = t.MostAbove(d.ModL), Step = t.AboveStep, Default = 0, Unit = "m",
                Label = "Room above", Group = "Module socket", Tooltip = "How tall a module may stand over the plate (0: twice its short side). Nothing of the ship may be in this room." });
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModCut), Id = CfgKey.ModCut, Type = CfgType.Number, Hidden = true, Min = 0, Max = 100000, Default = 0, Unit = "s",
                Label = "Cut", Group = "Module socket", Tooltip = "Not on the panel." });
            Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ModKeyed), Id = CfgKey.ModKeyed, Type = CfgType.Toggle, Label = "Keyed", Group = "Module socket",
                Tooltip = "On: a module fits at one turn only (the notched corner), so a nozzle can only face one way." });
        }
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
                Plate = (ushort)i, W = d.ModW, L = d.ModL, Stand = d.ModStand, Module = -1, InTheWay = -1,
                Kind = d.ModStand ? ModKind.Generic : (ModKind)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.ModKind, 0), 0, ModuleSocketTable.KindNames.Length - 1),
                Style = d.ModStand ? ModStyle.Proud : (ModStyle)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.ModStyle, 0), 0, ModuleSocketTable.StyleNames.Length - 1),
                Keyed = !d.ModStand && ConstructConfig.Get(p, d, CfgKey.ModKeyed, 0) > 0.5f,
            };
            var above = d.ModStand ? 0 : ConstructConfig.Get(p, d, CfgKey.ModAbove, 0);
            s.Above = above > 0 ? above : s.Style == ModStyle.Opening ? 0 : ModuleSocketTable.DefaultAbove(d.ModW);
            s.Depth = s.Style == ModStyle.Proud ? 0 : MathF.Max(ModuleSocketTable.Current.DepthStep, ConstructConfig.Get(p, d, CfgKey.ModDepth, ModuleSocketTable.Current.DepthStep));
            s.Height = s.Style switch { ModStyle.Flush => s.Depth + FlushStand, ModStyle.Opening => s.Depth + s.Above, _ => s.Above };
            Room(c, i, s.Height, out s.RoomMin, out s.RoomMax, s.Style == ModStyle.Proud ? ModuleSocketTable.Current.Side : ModuleCut.WellSide);
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
            pending |= ModuleCut.Wants(s);
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
    /// The pipe pass-through (2.5): route <paramref name="k"/> leaves a fitted plug's pipe port; the pipe that ends at the plate's
    /// port of the same name is what it carries on, so its source (a tank of the ship) is this route's source, and it is fed when that
    /// one is. A plug on a stand passes nothing. Through nested modules (a plug on a plate on a module) up to <see cref="PassDepth"/>.
    /// At the routes' rebuild only.
    /// </summary>
    public static void PassSource(Construct c, int k, ref ushort source, ref byte sourcePort, ref bool fed, int depth = 0)
    {
        var plug = source;
        var pd = c.DefOf(plug);
        if (ConstructRoutes.PortOf(pd, RouteType.Pipe, sourcePort) is not { } port || PlateOf(c, plug) is var plate && plate < 0 || c.DefOf(plate).ModStand)
            return;
        var plateDef = c.DefOf(plate);
        byte want = 0;
        var found = false;
        foreach (var np in plateDef.NamedPorts)
            if (np.Type == RouteType.Pipe && string.Equals(np.Name, port.Name, StringComparison.OrdinalIgnoreCase))
                (want, found) = (np.Byte, true);
        if (!found)
            return;
        for (var j = 0; j < c.RouteCount; j++)
        {
            ref var o = ref c.Routes[j];
            if (o.Type != RouteType.Pipe || o.To != plate || o.ToPort != want)
                continue;
            if (j < k)
            {
                (source, sourcePort, fed) = (o.Source, o.SourcePort, o.FedEnd);
                return;
            }
            // (laid later than this one: its root, from the parts alone)
            var root = j;
            for (var guard = 0; c.Routes[root].Parent != 255 && c.Routes[root].Parent < root && guard < c.RouteCount; guard++)
                root = c.Routes[root].Parent;
            ref var rr = ref c.Routes[root];
            if (rr.From >= c.PartCount)
                return;
            (source, sourcePort) = (rr.From, rr.FromPort);
            if (depth < PassDepth && c.DefOf(source).ModPart == ModPart.Plug)
                PassSource(c, root, ref source, ref sourcePort, ref fed, depth + 1);
            return;
        }
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
    public static void Room(Construct c, int plate, float above, out Vector3 min, out Vector3 max, float side)
    {
        var d = c.DefOf(plate);
        ref var p = ref c.Parts[plate];
        var face = d.Sockets[d.ModSocket].At;
        var lo = new Vector3(face.X - d.ModW * 0.5f - side, face.Y, face.Z - d.ModL * 0.5f - side);
        var hi = new Vector3(face.X + d.ModW * 0.5f + side, face.Y + above, face.Z + d.ModL * 0.5f + side);
        var mid = p.Pos + Rot24.Apply(p.Rot, (lo + hi) * 0.5f);
        var half = Rot24.ApplyHalf(p.Rot, (hi - lo) * 0.5f);
        min = mid - half;
        max = mid + half;
    }

    /// <summary>The first part of <paramref name="c"/> (not the plate, not module <paramref name="module"/>'s) whose solid is in socket <paramref name="s"/>'s room, or -1.</summary>
    private static int InRoom(Construct c, in CModSocket s, int module)
    {
        var seg = c.Parts[s.Plate].Seg;
        var shrink = new Vector3(0.01f);
        Vector3 lo = s.RoomMin + shrink, hi = s.RoomMax - shrink;
        var of = c.ModuleOf;
        var hits = Construct.PartScratch; // (the parts near the room only: the part tree, as the build's overlap test asks)
        var near = c.PartsInBox(lo, hi, hits);
        for (var h = 0; h < near; h++)
        {
            var j = hits[h];
            if (j == s.Plate || c.Parts[j].Seg != seg || module >= 0 && of != null && of[j] == module)
                continue;
            c.PartBounds(j, out var mn, out var mx);
            if (mn.X >= hi.X || mx.X <= lo.X || mn.Y >= hi.Y || mx.Y <= lo.Y || mn.Z >= hi.Z || mx.Z <= lo.Z)
                continue;
            var d = c.DefOf(j);
            for (var k = 0; k < d.SolidBoxes; k++)
            {
                c.BoxOf(j, k, out var oc, out var oh);
                if (oc.X - oh.X < hi.X && oc.X + oh.X > lo.X && oc.Y - oh.Y < hi.Y && oc.Y + oh.Y > lo.Y && oc.Z - oh.Z < hi.Z && oc.Z + oh.Z > lo.Z)
                    return j;
            }
        }
        return -1;
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

    /// <summary>A plug of this size and the plate's: null when they match, else why not.</summary>
    public static string? SizeRefusal(ConstructPartDef plate, ConstructPartDef plug)
    {
        if (plate.ModPart != ModPart.Socket || plug.ModPart != ModPart.Plug)
            return "a Module Plug goes on a Module Socket's face";
        if (plate.ModW != plug.ModW || plate.ModL != plug.ModL)
            return $"that's a {ModuleSocketTable.SizeText(plate.ModW, plate.ModL)} socket and a {ModuleSocketTable.SizeText(plug.ModW, plug.ModL)} module: sizes match exactly (an adapter module takes a smaller one)";
        return null;
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
            return $"the module weighs {m.Mass / 1000f:0.#} t: a {ModuleSocketTable.SizeText(s.W, s.L)} socket holds {t.MassRating(s.W, s.L) / 1000f:0.#} t";
        if (m.Thrust > t.ThrustRating(s.W, s.L))
            return $"the module pushes {m.Thrust / 1000f:0} kN: a {ModuleSocketTable.SizeText(s.W, s.L)} socket takes {t.ThrustRating(s.W, s.L) / 1000f:0} kN";
        if (KindTag(s.Kind) is { } want && m.Primary != want)
            return $"this socket takes {TagName(want)} modules only: this one is {TagName(m.Primary)} ({TagsText(m.Tags)})";
        if (byHand && !creative)
        {
            if (!t.HandSize(s.W, s.L))
                return $"a {ModuleSocketTable.SizeText(s.W, s.L)} module is fitted by a Module Fitter: by hand, up to {ModuleSocketTable.SizeText(t.HandUpToW, t.HandUpToL)}";
            if (m.Mass > lift)
                return $"the module weighs {m.Mass / 1000f:0.#} t: by hand up to {lift / 1000f:0.#} t (a Module Fitter lifts more)";
        }
        // The turn: the plug's turn in the ship's frame.
        var inv = Quaternion.Conjugate(ship.Rot);
        var bodyTurn = Rot24.Nearest(inv * moverRot, out var match);
        if (match < 0.999f)
            return "it isn't square to the socket";
        var plugRot = Rot24.Compose(bodyTurn, mover.Parts[m.Plug].Rot);
        if (!TurnFits(plateDef, ship.Parts[s.Plate].Rot, plugDef, plugRot, s.Keyed))
            return s.Keyed ? "a keyed socket takes its module one way round only: turn it (the notched corners together)" : "turn it: the module's long side goes along the socket's";
        // Inside the room: every part of the module, in the ship's frame.
        var rel = Vector3.Transform(moverPos - ship.Pos, inv);
        Vector3 lo = s.RoomMin - new Vector3(0.006f), hi = s.RoomMax + new Vector3(0.006f);
        var plateDir = Rot24.ApplyDir(ship.Parts[s.Plate].Rot, plateDef.Sockets[plateDef.ModSocket].Dir);
        ship.SocketAt(s.Plate, plateDef.ModSocket, out var faceAt, out _);
        int axis = plateDir >> 1;
        var sign = (plateDir & 1) == 0 ? 1f : -1f;
        var top = 0f; // (the module's highest point over the plate's face: a flush module's face)
        for (var j = 0; j < mover.PartCount; j++)
        {
            if (moduleOnly && of != null && of[j] != mi)
                continue; // (what it stands on: it comes off that first)
            if (mover.Parts[j].Seg != 0)
                continue; // (a joint's moving parts: their sweep is ModuleSweep's; their rest pose is checked with the body by the overlap test)
            mover.PartBounds(j, out var mn, out var mx);
            var c0 = rel + Rot24.Apply(bodyTurn, (mn + mx) * 0.5f);
            var h0 = Rot24.ApplyHalf(bodyTurn, (mx - mn) * 0.5f);
            var a = c0 - h0;
            var b = c0 + h0;
            if (a.X < lo.X || a.Y < lo.Y || a.Z < lo.Z || b.X > hi.X || b.Y > hi.Y || b.Z > hi.Z)
                return s.Style == ModStyle.Flush
                    ? $"the {mover.DefOf(j).Name} stands out of the flush well ({s.W} x {s.L} m, {s.Depth:0.##} m deep): a flush module ends level with the hull"
                    : $"the {mover.DefOf(j).Name} sticks out of the socket's room ({s.W} x {s.L} m, {s.Height:0.#} m tall: the plate's Room above and Well depth set it)";
            top = MathF.Max(top, sign > 0 ? b[axis] - faceAt[axis] : faceAt[axis] - a[axis]);
        }
        if (s.Style == ModStyle.Flush && top < s.Depth - FlushTolerance)
            return $"a flush module's face is level with the hull: this one stands {top:0.00} m and the well is {s.Depth:0.00} m deep (set Well depth to {MathF.Round(top / ModuleSocketTable.Current.DepthStep) * ModuleSocketTable.Current.DepthStep:0.00} m, or use an opening socket)";
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
            var size = ModuleSocketTable.SizeText(s.W, s.L);
            if (s.Stand)
                return s.Module >= 0 ? $"{size} stand: {ModuleName(c, s.Module)} stored (F: take it off)" : $"{size} stand: empty (F with a module near: set it on)";
            var kind = s.Kind == ModKind.Generic ? "any module" : $"{TagName(KindTag(s.Kind)!.Value)} modules only";
            var head = $"{size} {ModuleSocketTable.StyleNames[(int)s.Style]} socket, {kind}{(s.Keyed ? ", keyed" : "")}";
            if (s.Module >= 0)
                return $"{head}: {ModuleName(c, s.Module)} fitted{(s.WrongKind ? $" - not {Article(TagName(KindTag(s.Kind)!.Value))} any more: it does nothing here" : "")} (F: take it off)";
            if (s.InTheWay >= 0)
                return s.Style == ModStyle.Proud
                    ? $"{head}: blocked, the {c.DefOf(s.InTheWay).Name} is in its room"
                    : $"{head} {s.Depth:0.##} m: {ModuleCut.LookText(c, s, ModuleCut.Seen(c, s.Plate)) ?? $"the {c.DefOf(s.InTheWay).Name} is in its well"}";
            return $"{head}{(s.Style == ModStyle.Proud ? "" : $" {s.Depth:0.##} m")}: free (F with a module within {ModuleSocketTable.Current.HandPull:0.#} m: fit it)";
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
        return $"{ModuleSocketTable.SizeText(d.ModW, d.ModL)} module";
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
