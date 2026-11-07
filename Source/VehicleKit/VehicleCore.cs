using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop.VehicleKit;

/// <summary>Thrown when a vehicle can't be built as asked: the message names the part, the place and the rule.</summary>
public sealed class VehicleKitException(string message) : Exception(message);

/// <summary>A part already on the vehicle: its blueprint step, its index on the construct being built and its file.</summary>
public sealed record VPart(int Step, int Index, ConstructPartDef Def, int Seg)
{
    public string Name => Def.Name;
}

/// <summary>Which way round a part has to end up. Each rule names a direction of the part and where it must point in the vehicle (x right, y up, -z forward).</summary>
public readonly record struct Orient(Vector3 OwnA, Vector3 WantA, Vector3 OwnB = default, Vector3 WantB = default)
{
    /// <summary>Any turn at all.</summary>
    public static readonly Orient Any = default;

    /// <summary>The part stands the right way up and faces forward: seats, guns, boxes with a front.</summary>
    public static readonly Orient Forward = new(Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, -Vector3.UnitZ);

    /// <summary>Upright, any heading.</summary>
    public static readonly Orient Upright = new(Vector3.UnitY, Vector3.UnitY);

    /// <summary>A beam (its length is its own z) laid along <paramref name="axis"/>, its top up when that isn't its length.</summary>
    public static Orient Along(Vector3 axis) => new(Vector3.UnitZ, axis);

    /// <summary>A plate (its face is its own +y) with its face towards <paramref name="face"/>.</summary>
    public static Orient Facing(Vector3 face) => new(Vector3.UnitY, face);

    public bool Allows(byte rot)
    {
        if (OwnA != default && Vector3.Dot(Rot24.Apply(rot, OwnA), WantA) < 0.99f)
            return false;
        return OwnB == default || Vector3.Dot(Rot24.Apply(rot, OwnB), WantB) >= 0.99f;
    }
}

/// <summary>
/// The kit's bottom layer: a blueprint written step by step against a live construct, through the game's own rules. Every
/// step goes through <see cref="ConstructBuild.Place"/>, <see cref="ConstructBuild.CanWire"/> and
/// <see cref="ConstructRoutes.BuildLink"/> as it is added, so what can't be built throws at once with the game's reason,
/// and nothing here works sockets out by itself: <see cref="Put"/> searches the free sockets for the place asked for,
/// <see cref="Onto"/> takes a socket by name. <see cref="Text"/> writes the ordinary blueprint file.
/// Places are in vehicle coordinates: x right, y up, -z forward, the origin where <see cref="Root"/> says.
/// </summary>
public class BlueprintScript
{
    /// <summary>How far from the wanted centre a part may land before <see cref="Put"/> refuses (sockets are on a 0.25 m grid).</summary>
    public const float DefaultReach = 0.07f;

    protected readonly ConstructWorld World;
    protected readonly Construct C;
    private readonly List<Step> _steps = new();
    private readonly List<(int From, int To)> _wires = new();
    private readonly List<(int Reel, int Inlet)> _hoses = new();
    private readonly List<string> _links = new();
    private readonly List<string> _cargo = new();
    private readonly List<(int Step, string Table)> _loot = new();
    private Vector3 _origin;

    public string Name { get; }
    public string Description { get; set; } = "";
    public string Group { get; set; } = "";

    /// <summary>
    /// Which kit made it (<see cref="VehicleKitVersion"/>) and, from the shape grammar on, the modules it chose ("nose long hood", "body room+bed"...).
    /// A file from kit 2 or later carries both in a comment line after its first brace (blueprints allow comments); kit 1's text is as it always was.
    /// </summary>
    public int Kit { get; set; } = VehicleKitVersion.Legacy;

    public List<string> Modules { get; } = new();

    public int Count => _steps.Count;
    public IReadOnlyList<VPart> Parts => _parts;
    private readonly List<VPart> _parts = new();

    private sealed class Step
    {
        public string Part = "";
        public int On = -1, Spin;
        public string Socket = "", By = "";
        public string? Weld, Config;
        public uint Paint;
    }

    public BlueprintScript(string name)
    {
        Name = name;
        World = new ConstructWorld();
        C = World.Create(Vector3.Zero, Quaternion.Identity) ?? throw new VehicleKitException("no room for a construct");
    }

    protected static ConstructPartDef Def(string name) =>
        ConstructParts.Find(name) ?? throw new VehicleKitException($"no construct part named \"{name}\" (--check-construct-parts lists what loaded)");

    protected Vector3 ToRest(Vector3 at) => at - _origin;

    protected Vector3 FromRest(Vector3 rest) => rest + _origin;

    /// <summary>The first part: its bounds' centre at <paramref name="at"/>. Every later place is measured from the same origin.</summary>
    public VPart Root(string part, Vector3 at, Orient o = default)
    {
        if (_steps.Count > 0)
            throw new VehicleKitException("a vehicle has one root");
        var def = Def(part);
        var turn = FirstTurn(o);
        _origin = at - Rot24.Apply(turn, (def.BoundsMin + def.BoundsMax) * 0.5f);
        var i = World.AddPart(C, def, Vector3.Zero, turn);
        if (i < 0)
            throw new VehicleKitException($"the {part} won't start a construct");
        _steps.Add(new Step { Part = part, Spin = turn });
        return Added(i, def);
    }

    private VPart Added(int index, ConstructPartDef def)
    {
        var v = new VPart(_steps.Count - 1, index, def, C.Parts[index].Seg);
        _parts.Add(v);
        return v;
    }

    /// <summary>Fits <paramref name="part"/> by name: onto <paramref name="socket"/> of <paramref name="on"/>, by its own socket <paramref name="by"/> (null: the snap build's pick), turned by <paramref name="spin"/> (null: smart rotation's).</summary>
    public VPart Onto(VPart on, string socket, string part, string? by = null, int? spin = null)
    {
        var def = Def(part);
        var target = C.DefOf(on.Index);
        var ts = Array.FindIndex(target.Sockets, s => s.Name == socket);
        if (ts < 0)
            throw new VehicleKitException($"a {target.Name} has no socket \"{socket}\" ({string.Join(" ", target.Sockets.Select(s => s.Name))})");
        int ds;
        if (by != null)
        {
            ds = Array.FindIndex(def.Sockets, s => s.Name == by);
            if (ds < 0)
                throw new VehicleKitException($"a {def.Name} has no socket \"{by}\" ({string.Join(" ", def.Sockets.Select(s => s.Name))})");
        }
        else if (SnapBuild.Fit(C, def, on.Index, ts, -1, out var fit))
            ds = fit.DefSocket;
        else
            throw new VehicleKitException($"a {def.Name} doesn't fit on the {target.Name}'s \"{socket}\"");
        var sp = spin ?? Math.Max(0, SmartRotation.BestSpin(C, on.Index, ts, def, ds));
        if (ConstructBuild.Place(C, on.Index, ts, def, ds, sp, out var g) is { } bad)
            throw new VehicleKitException($"a {def.Name} on the {target.Name}'s \"{socket}\": {bad}");
        return Commit(on.Index, on.Step, ts, def, ds, sp, g);
    }

    private VPart Commit(int onIndex, int onStep, int ts, ConstructPartDef def, int ds, int spin, in ConstructGhost g)
    {
        var i = ConstructBuild.Attach(World, C, def, g);
        if (i < 0)
            throw new VehicleKitException($"a {def.Name}: too many parts");
        C.Rebuild();
        VehicleGenJob.Yield(); // (a spawn in the game hands back to the tick here: VehicleSpawn.cs)
        _steps.Add(new Step { Part = def.Name, On = onStep, Socket = C.DefOf(onIndex).Sockets[ts].Name, By = def.Sockets[ds].Name, Spin = spin });
        return Added(i, def);
    }

    /// <summary>
    /// Fits <paramref name="part"/> with the centre of its bounds at <paramref name="centre"/> and turned as <paramref name="o"/> says, on
    /// whichever free socket puts it nearest (within <paramref name="reach"/> metres): of <paramref name="on"/> when given, else of the vehicle's body.
    /// <paramref name="onTop"/>: only sockets facing up (furniture on a floor, never hung on a wall). <paramref name="fit"/>: half sizes it must take up (0 on an axis: any), which settles a long plate's way round. Refused with the nearest try and its reason.
    /// </summary>
    public VPart Put(string part, Vector3 centre, Orient o = default, float reach = DefaultReach, VPart? on = null, string? by = null, Vector3 fit = default, bool onTop = false)
    {
        var def = Def(part);
        var want = ToRest(centre);
        var mid = (def.BoundsMin + def.BoundsMax) * 0.5f;
        var halfSize = (def.BoundsMax - def.BoundsMin) * 0.5f;
        var bestD = float.MaxValue;
        (int I, int S, int Own, int Spin) best = (-1, 0, 0, 0);
        var nearD = float.MaxValue;
        var nearWhy = "no free socket on it";
        Span<byte> turns = stackalloc byte[4];
        for (var i = on?.Index ?? 0; i < (on == null ? C.PartCount : on.Index + 1); i++)
        {
            if (on == null && C.Parts[i].Seg != 0)
                continue;
            var td = C.DefOf(i);
            for (var s = 0; s < C.SocketCount(i); s++)
            {
                if (C.SocketUsed(i, s) || s >= td.Sockets.Length)
                    continue;
                C.SocketAt(i, s, out var at, out var dir);
                if (onTop && ConstructParts.DirVector(dir).Y < 0.9f)
                    continue;
                for (var ds = 0; ds < def.Sockets.Length; ds++)
                {
                    if (by != null && def.Sockets[ds].Name != by)
                        continue;
                    if (!ConstructBuild.KindsMate(td.Sockets[s].Kind, def.Sockets[ds].Kind, ConstructBuild.OnLeaf(C, i, s)))
                        continue;
                    var n = ConstructBuild.SpinTurns(def.Sockets[ds].Dir, dir, turns);
                    for (var sp = 0; sp < n; sp++)
                    {
                        if (!o.Allows(turns[sp]) || !Fits(Rot24.ApplyHalf(turns[sp], halfSize), fit))
                            continue;
                        var pos = ConstructWorld.Quantise(at - Rot24.Apply(turns[sp], def.Sockets[ds].At));
                        var d = Vector3.Distance(pos + Rot24.Apply(turns[sp], mid), want);
                        if (d >= bestD - 1e-4f || d > reach + 0.5f)
                            continue;
                        VehicleGenJob.Yield();
                        var why = ConstructBuild.Place(C, i, s, def, ds, sp, out _);
                        if (why == null && d <= reach)
                        {
                            bestD = d;
                            best = (i, s, ds, sp);
                        }
                        else if (d < nearD)
                        {
                            nearD = d;
                            nearWhy = why == ConstructBuild.InTheWay ? $"nearest try {d:0.00} m off: the {Blocker(def, pos, turns[sp])} is in the way" : why ?? $"{d:0.00} m from where it was asked for, the nearest a socket puts it";
                        }
                    }
                }
            }
        }
        if (best.I < 0)
            throw new VehicleKitException($"{Name}: no socket takes a {def.Name} at {Show(centre)}: {nearWhy}");
        ConstructBuild.Place(C, best.I, best.S, def, best.Own, best.Spin, out var g);
        return Commit(best.I, _parts.First(p => p.Index == best.I).Step, best.S, def, best.Own, best.Spin, g);
    }

    /// <summary>
    /// <see cref="Put"/> by a socket: the part goes where its own socket <paramref name="by"/> lands on <paramref name="at"/> (a seat's feet, an engine's mount), on <paramref name="on"/>.
    /// </summary>
    public VPart Stand(string part, Vector3 at, Orient o, VPart? on, string by, float reach = 0.03f)
    {
        var def = Def(part);
        var own = Array.FindIndex(def.Sockets, s => s.Name == by);
        if (own < 0)
            throw new VehicleKitException($"a {def.Name} has no socket \"{by}\" ({string.Join(" ", def.Sockets.Select(s => s.Name))})");
        var turn = FirstTurn(o);
        return Put(part, at + Rot24.Apply(turn, (def.BoundsMin + def.BoundsMax) * 0.5f - def.Sockets[own].At), o, reach, on, by);
    }

    /// <summary>What a part there would run into, for the refusal.</summary>
    private string Blocker(ConstructPartDef def, Vector3 pos, byte rot)
    {
        for (var b = 0; b < def.SolidBoxes; b++)
        {
            var bc = pos + Rot24.Apply(rot, def.Boxes[b].Center);
            var bh = Rot24.ApplyHalf(rot, def.Boxes[b].Half) - new Vector3(0.01f);
            for (var i = 0; i < C.PartCount; i++)
            for (var k = 0; k < C.DefOf(i).SolidBoxes; k++)
            {
                C.BoxOf(i, k, out var oc, out var oh);
                var gap = Vector3.Abs(bc - oc) - bh - oh;
                if (gap.X < 0 && gap.Y < 0 && gap.Z < 0)
                    return $"{C.DefOf(i).Name} at {Show(FromRest(oc))}";
            }
        }
        return "something";
    }

    /// <summary>A half size against one asked for, where 0 is "any" on that axis.</summary>
    private static bool Fits(Vector3 half, Vector3 want) =>
        (want.X == 0 || MathF.Abs(half.X - want.X) < 0.04f) && (want.Y == 0 || MathF.Abs(half.Y - want.Y) < 0.04f) && (want.Z == 0 || MathF.Abs(half.Z - want.Z) < 0.04f);

    /// <summary>The first turn a rule allows.</summary>
    protected static byte FirstTurn(Orient o)
    {
        for (var r = 0; r < Rot24.Count; r++)
            if (o.Allows((byte)r))
                return (byte)r;
        return 0;
    }

    /// <summary>
    /// A part welded on off the grid (a blueprint's freeform step, board item fw01): its bounds' centre at <paramref name="centre"/>,
    /// turned <paramref name="o"/> and then tilted by <paramref name="pitch"/>, <paramref name="yaw"/>, <paramref name="roll"/> degrees. <paramref name="on"/> is on the body.
    /// </summary>
    public VPart Weld(string part, VPart on, Vector3 centre, Orient o, double pitch, double yaw, double roll)
    {
        var def = Def(part);
        if (on.Seg != 0)
            throw new VehicleKitException($"a {def.Name} is welded onto the body, and the {on.Name} is on a joint");
        var turn = FirstTurn(o);
        // (four decimals in the file: rounded here, so this copy and the file are the same pose)
        var rot = Quaternion.Normalize(RoundQ(Quaternion.CreateFromYawPitchRoll((float)(yaw * Math.PI / 180), (float)(pitch * Math.PI / 180), (float)(roll * Math.PI / 180)) * Rot24.Quat(turn)));
        var pos = RoundV(ToRest(centre) - Vector3.Transform((def.BoundsMin + def.BoundsMax) * 0.5f, rot));
        var i = World.AddFreeform(C, on.Index, def, pos, rot);
        if (i < 0)
            throw new VehicleKitException($"{Name}: a {def.Name} won't weld on: at most {Freeform.MaxPerConstruct} freeform welds a construct");
        C.Rebuild();
        VehicleGenJob.Yield();
        _steps.Add(new Step
        {
            Part = def.Name,
            On = on.Step,
            Weld = string.Create(CultureInfo.InvariantCulture, $"{{\"on\": {on.Step}, \"pos\": [{N(pos.X)}, {N(pos.Y)}, {N(pos.Z)}], \"rot\": [{Q(rot.X)}, {Q(rot.Y)}, {Q(rot.Z)}, {Q(rot.W)}]}}"),
        });
        return Added(i, def);
    }

    private static Quaternion RoundQ(Quaternion q) => new(MathF.Round(q.X, 4), MathF.Round(q.Y, 4), MathF.Round(q.Z, 4), MathF.Round(q.W, 4));

    private static Vector3 RoundV(Vector3 v) => new(MathF.Round(v.X, 3), MathF.Round(v.Y, 3), MathF.Round(v.Z, 3));

    /// <summary>A wire (a data link), through the linking tool's rules.</summary>
    public void Wire(VPart from, VPart to)
    {
        if (ConstructBuild.CanWire(World, C, from.Index, to.Index) is { } bad)
            throw new VehicleKitException($"a wire {from.Name} -> {to.Name}: {bad}");
        World.AddWire(C, from.Index, to.Index);
        C.Rebuild();
        VehicleGenJob.Yield();
        _wires.Add((from.Step, to.Step));
    }

    /// <summary>An ammo reel's belt plugged into its socket, as built.</summary>
    public void Hose(VPart reel, VPart inlet)
    {
        World.PlugHose(C, reel.Index, C, inlet.Index);
        C.Rebuild();
        VehicleGenJob.Yield();
        _hoses.Add((reel.Step, inlet.Step));
    }

    /// <summary>A routed link: "pipe", "shaft", "power" or "data", through the linking tool's rules. <paramref name="nodes"/>: the way it goes (vehicle coordinates); none: straight. <paramref name="laid"/>: only its ends and lengths are checked, so it may pass through plates.</summary>
    public void Link(string type, VPart from, VPart to, string? fromPort = null, string? toPort = null, string? paint = null, bool laid = false, params Vector3[] nodes)
    {
        var text = new StringBuilder($"{{\"type\": \"{type}\", \"from\": {from.Step}, \"to\": {to.Step}");
        if (fromPort != null)
            text.Append($", \"fromPort\": \"{fromPort}\"");
        if (toPort != null)
            text.Append($", \"toPort\": \"{toPort}\"");
        if (nodes.Length > 0)
            text.Append(", \"nodes\": [").Append(string.Join(", ", nodes.Select(n => { var r = ToRest(n); return $"[{N(r.X)}, {N(r.Y)}, {N(r.Z)}]"; }))).Append(']');
        if (laid)
            text.Append(", \"laid\": true");
        if (paint != null)
            text.Append($", \"paint\": \"{paint}\"");
        text.Append('}');
        using var doc = JsonDocument.Parse(text.ToString());
        var spec = ConstructRoutes.ParseLink(doc.RootElement, Name, _links.Count);
        var partOf = new int[_steps.Count];
        foreach (var p in _parts)
            partOf[p.Step] = p.Index;
        if (ConstructRoutes.BuildLink(World, C, spec, partOf) is { } bad)
            throw new VehicleKitException($"a {type} link {from.Name} -> {to.Name}: {bad}");
        for (var k = 0; k < C.RouteCount; k++)
            C.Routes[k].Cursor = 0;
        C.Rebuild();
        VehicleGenJob.Yield();
        _links.Add(text.ToString());
    }

    /// <summary>A step's paint; a part with nothing to paint just carries it.</summary>
    public void Paint(VPart p, string colour)
    {
        if (!ConstructPaint.TryParse(colour, out var paint))
            throw new VehicleKitException($"\"{colour}\" isn't a colour like \"#c04020\"");
        _steps[p.Step].Paint = paint;
        C.Parts[p.Index].Paint = paint;
    }

    /// <summary>A step's settings by key (the configure tool's): the values are written as JSON.</summary>
    public void Config(VPart p, params (string Key, object Value)[] values) =>
        _steps[p.Step].Config = "{" + string.Join(", ", values.Select(v => $"\"{v.Key}\": {JsonValue(v.Value)}")) + "}";

    /// <summary>A container that fills from a loot table (Constructs/Loot.json) each time the blueprint is spawned; nothing is fixed in the file.</summary>
    public void Loot(VPart container, string table)
    {
        if (C.DefOf(container.Index).Slots.Length == 0)
            throw new VehicleKitException($"loot goes in a container with slots, and a {container.Name} has none");
        if (!ConstructLoot.Tables.ContainsKey(table))
            throw new VehicleKitException($"no loot table \"{table}\" in Constructs/Loot.json");
        _loot.Add((container.Step, table));
    }

    /// <summary>A loose part riding in a frozen Cargo Volume, its origin at <paramref name="at"/>. It stays a body of its own: shot off, stolen or salvaged like anything loose.</summary>
    public void Cargo(string part, Vector3 at, int turn = 0)
    {
        _ = Def(part);
        var r = ToRest(at);
        _cargo.Add($"{{\"part\": \"{part}\", \"at\": [{N(r.X)}, {N(r.Y)}, {N(r.Z)}], \"turn\": {turn}}}");
    }

    private static string JsonValue(object v) => v switch
    {
        bool b => b ? "true" : "false",
        string s => $"\"{s}\"",
        double d => N(d),
        float f => N(f),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => throw new VehicleKitException($"a setting of type {v.GetType().Name}"),
    };

    protected static string N(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Q(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

    protected static string Show(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"[{v.X:0.00}, {v.Y:0.00}, {v.Z:0.00}]");

    /// <summary>The blueprint file. One step a line, so a change reads as a change; the same script always writes the same text.</summary>
    /// <summary>The blueprint spawned coupled behind this one: its first eye on this one's first ball.</summary>
    public string Tow { get; set; } = "";

    public string Text()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        if (Kit > VehicleKitVersion.Legacy)
            sb.Append("\n  // kit ").Append(Kit).Append(Modules.Count > 0 ? "; " + string.Join(", ", Modules) : "");
        sb.Append("\n  \"format\": \"").Append(ConstructBuild.BlueprintFormat).Append("\",\n  \"name\": ").Append(Quote(Name)).Append(",\n");
        if (Group.Length > 0)
            sb.Append("  \"group\": ").Append(Quote(Group)).Append(",\n");
        sb.Append("  \"description\": ").Append(Quote(Description)).Append(",\n  \"steps\": [\n");
        for (var k = 0; k < _steps.Count; k++)
        {
            var s = _steps[k];
            sb.Append("    {\"part\": ").Append(Quote(s.Part));
            if (s.Weld != null)
                sb.Append(", \"weld\": ").Append(s.Weld);
            else if (k > 0)
                sb.Append($", \"on\": {s.On}, \"socket\": {Quote(s.Socket)}, \"by\": {Quote(s.By)}");
            if (s.Spin != 0)
                sb.Append($", \"spin\": {s.Spin}");
            if (s.Config != null)
                sb.Append(", \"config\": ").Append(s.Config);
            if (s.Paint != 0)
                sb.Append(", \"paint\": ").Append(Quote(ConstructPaint.ToHex(s.Paint)));
            sb.Append(k + 1 < _steps.Count ? "},\n" : "}\n");
        }
        sb.Append("  ]");
        if (_wires.Count > 0)
            sb.Append(",\n  \"wires\": [").Append(string.Join(", ", _wires.Select(w => $"[{w.From}, {w.To}]"))).Append(']');
        if (_hoses.Count > 0)
            sb.Append(",\n  \"hoses\": [").Append(string.Join(", ", _hoses.Select(h => $"[{h.Reel}, {h.Inlet}]"))).Append(']');
        if (_links.Count > 0)
            sb.Append(",\n  \"links\": [\n    ").Append(string.Join(",\n    ", _links)).Append("\n  ]");
        if (_cargo.Count > 0)
            sb.Append(",\n  \"cargo\": [\n    ").Append(string.Join(",\n    ", _cargo)).Append("\n  ]");
        if (Tow.Length > 0)
            sb.Append(",\n  \"tow\": ").Append(Quote(Tow));
        if (_loot.Count > 0)
            sb.Append(",\n  \"loot\": [").Append(string.Join(", ", _loot.Select(l => $"{{\"step\": {l.Step}, \"table\": {Quote(l.Table)}}}"))).Append(']');
        sb.Append("\n}\n");
        return sb.ToString();
    }

    private static string Quote(string s) => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
