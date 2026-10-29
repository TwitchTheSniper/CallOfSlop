using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a construct part does (docs/sandbox/CONSTRUCTS.md, "Parts").</summary>
public enum PartRole : byte
{
    Frame, // structure: blocks and beams welded together
    Panel, // a plate: structure and cover
    Seat, // a driver's seat: the root of a vehicle, and where the driver sits
    Wheel, // rolls on the ground (a raycast wheel); needs a suspension to drive
    Suspension, // holds a wheel: its spring and travel
    Engine, // drives every wheel of its body while it has fuel and a driver
    FuelTank, // holds the fuel; feeds an engine it touches, or through fuel lines
    FuelLine, // a pipe: carries fuel from a tank to an engine
    Hinge, // a passive hinge joint: what is attached to its leaf swings
    Door, // a door leaf (hangs on a hinge; Interact opens and shuts it)
    Foundation, // anchors a construct to the ground (a base: never moves)
}

/// <summary>The tool a part is attached and removed with (decision 2: editing always needs a tool in hand).</summary>
public enum ConstructTool : byte
{
    None, // bare hands or a gun: use only (drive, sit, open doors), never edit
    Wrench, // bolted parts: wheels, seats, engines, doors...
    Welder, // welded parts: frames, panels, foundations
}

/// <summary>What happens to a part at zero health.</summary>
public enum PartBreak : byte
{
    Detach, // it breaks off (gone; anything it held that is cut off from the root falls away as a wreck)
    Disable, // it stays on but stops working (a dead engine, a holed tank)
}

/// <summary>What kind of attachment point a socket is: two sockets mate only when their kinds match.</summary>
public enum SocketKind : byte
{
    Struct, // frames, panels, seats, engines, tanks...
    Axle, // a wheel onto a suspension
    Leaf, // a door onto a hinge's leaf
}

/// <summary>One box of a part's model and collision, in the part's own frame (metres).</summary>
/// <summary>A detail box is drawn only: it adds no bounds, mass, collision or hit area (and may stick out past a face).</summary>
public readonly record struct CPartBox(Vector3 Center, Vector3 Half, Vector3 Color, bool Detail = false);

/// <summary>An attachment point: where on the part (on a face of its bounds) and which way it faces (an axis, 0..5: +X -X +Y -Y +Z -Z).</summary>
public readonly record struct CSocket(string Name, Vector3 At, byte Dir, SocketKind Kind);

/// <summary>One part type, from a JSON file in Constructs/Parts (loaded by <see cref="ConstructParts"/>).</summary>
public sealed class ConstructPartDef
{
    public byte Id; // index in ConstructParts.All (the wire id)
    public string Name = "";
    public string File = "";
    public string Description = "";
    public PartRole Role;
    public ConstructTool Tool;
    public float AttachSeconds;
    public float Mass;
    public float Health;
    public float Armour; // flat damage taken off every hit
    public string Material = "steel";
    public float Friction = 0.7f;
    public PartBreak OnBreak;
    public CPartBox[] Boxes = []; // solid boxes first, then detail boxes
    public int SolidBoxes; // how many of Boxes are solid
    public CSocket[] Sockets = [];
    public Vector3 BoundsMin, BoundsMax; // round every box, in the part's frame

    // Role data (only what the role uses is set)
    public float EngineForce, TopSpeed; // engine: drive force (N) shared by the wheels, top speed (m/s)
    public float WheelRadius, Grip = 1f; // wheel
    public float Travel, Frequency, Damping; // suspension: travel (m), spring frequency (Hz), damping ratio
    public float FuelCapacity; // fuel tank (litres)
    public float BurnRate; // engine: litres a second at full throttle
    public Vector3 SeatAt; // seat: where the sitter's feet go (part frame)
    public byte JointAxis; // hinge: the axis it turns about (0..5)
    public Vector3 JointPivot; // hinge: a point on the axis (part frame)
    public float JointMin, JointMax; // hinge: limits (radians)
    public string LeafSocket = ""; // hinge: what attaches to this socket hangs on the joint

    public bool UsesTool(ConstructTool t) => t != ConstructTool.None && t == Tool;
    public override string ToString() => Name;
}

/// <summary>A part file that can't be used: the reason names the file and the problem.</summary>
public sealed class ConstructPartException(string message) : Exception(message);

/// <summary>
/// The construct parts library: every JSON file in Constructs/Parts, loaded once at start-up (data first: a new part is a
/// new file). A bad file is refused with a reason (<see cref="Refused"/>; <c>--check-construct-parts</c> prints them) and
/// the rest still load. Ids follow the file names' sorted order, so every PC with the same files gives the same ids.
/// </summary>
public static class ConstructParts
{
    public const string Format = "call-of-slop-construct-part/1";

    /// <summary>The most part types (ids are one byte on the wire; 255 means none).</summary>
    public const int MaxTypes = 250;

    public const byte None = 255;

    private static readonly List<string> _refused = new();
    private static readonly Lazy<ConstructPartDef[]> _all = new(() => LoadFolder(Folder, _refused), LazyThreadSafetyMode.ExecutionAndPublication); // (maps load in parallel)

    public static string Folder => Path.Combine(AppFolders.Data, "Constructs", "Parts");

    public static IReadOnlyList<ConstructPartDef> All => _all.Value;

    /// <summary>Files refused at load, with the reason (one line each).</summary>
    public static IReadOnlyList<string> Refused
    {
        get
        {
            _ = All;
            return _refused;
        }
    }

    /// <summary>A content hash of the loaded parts (the same files give the same hash on every PC).</summary>
    public static uint Hash
    {
        get
        {
            var h = 2166136261u;
            foreach (var d in All)
                foreach (var ch in d.Name + d.Role + d.Mass + d.Health + d.Boxes.Length + d.Sockets.Length)
                    h = (h ^ ch) * 16777619u;
            return h;
        }
    }

    /// <summary><c>--check-construct-parts</c>: every part file and blueprint, loaded or refused with the reason. Exit code: the refusals.</summary>
    public static int CheckCli()
    {
        foreach (var d in All)
            Console.WriteLine($"OK  part {d.Id,3} {d.Name,-14} {d.Role,-10} {d.Tool,-6} {d.Mass,6:0} kg {d.Health,6:0} hp armour {d.Armour:0}, {d.Boxes.Length} boxes, {d.Sockets.Length} sockets ({d.File})");
        foreach (var b in ConstructBuild.Blueprints)
            Console.WriteLine($"OK  blueprint {b.Name}: {b.Steps.Count} steps ({b.File})");
        foreach (var r in Refused)
            Console.WriteLine($"REFUSED {r}");
        foreach (var r in ConstructBuild.RefusedBlueprints)
            Console.WriteLine($"REFUSED {r}");
        return Refused.Count + ConstructBuild.RefusedBlueprints.Count;
    }

    public static ConstructPartDef? Get(byte id) => id < All.Count ? All[id] : null;

    public static ConstructPartDef? Find(string name)
    {
        foreach (var d in All)
            if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    public static ConstructPartDef Need(string name) => Find(name) ?? throw new ConstructPartException($"no construct part named \"{name}\"");

    /// <summary>Loads every part file of a folder (sorted by file name), refusing bad ones into <paramref name="refused"/>.</summary>
    public static ConstructPartDef[] LoadFolder(string folder, List<string> refused)
    {
        var list = new List<ConstructPartDef>();
        if (!Directory.Exists(folder))
        {
            refused.Add($"{folder}: the construct parts folder is missing");
            return [];
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), StringComparer.Ordinal))
        {
            try
            {
                var def = Parse(File.ReadAllText(file), Path.GetFileName(file));
                if (!names.Add(def.Name))
                    throw new ConstructPartException($"{Path.GetFileName(file)}: another file already has the name \"{def.Name}\"");
                if (list.Count >= MaxTypes)
                    throw new ConstructPartException($"{Path.GetFileName(file)}: more than {MaxTypes} part types");
                def.Id = (byte)list.Count;
                list.Add(def);
            }
            catch (Exception ex) when (ex is ConstructPartException or JsonException or IOException)
            {
                refused.Add(ex is ConstructPartException ? ex.Message : $"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return list.ToArray();
    }

    // ---- Parsing ---------------------------------------------------------------------------------------------

    private static readonly string[] TopKeys =
    [
        "format", "name", "description", "role", "tool", "attachSeconds", "mass", "health", "armour", "material", "friction",
        "onBreak", "color", "boxes", "sockets", "engine", "wheel", "suspension", "fuelTank", "seat", "hinge",
    ];

    /// <summary>Parses one part file's text. Throws <see cref="ConstructPartException"/> with the reason for anything wrong.</summary>
    public static ConstructPartDef Parse(string json, string file)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: a part file is one JSON object");
        Known(root, TopKeys, file);
        if (Opt(root, "format") is { } fmt && fmt.GetString() != Format)
            throw new ConstructPartException($"{file}: \"format\" must be \"{Format}\"");
        var d = new ConstructPartDef { File = file };
        d.Name = StrKey(root, "name", file);
        if (d.Name.Length is 0 or > 32 || d.Name.Any(c => c < 32 || c > 126))
            throw new ConstructPartException($"{file}: \"name\" must be 1-32 plain ASCII characters");
        d.Description = Opt(root, "description") is { } desc ? AsciiText(desc, file, "description") : "";
        d.Role = EnumOf<PartRole>(StrKey(root, "role", file), file, "role");
        d.Tool = EnumOf<ConstructTool>(StrKey(root, "tool", file), file, "tool");
        if (d.Tool == ConstructTool.None)
            throw new ConstructPartException($"{file}: \"tool\" is \"wrench\" or \"welder\"");
        d.AttachSeconds = Opt(root, "attachSeconds") is { } secs ? Num(secs, file, "attachSeconds") : d.Tool == ConstructTool.Welder ? 2.5f : 1.5f;
        Range(d.AttachSeconds, 0.2f, 20f, file, "attachSeconds");
        d.Mass = Num(Need(root, "mass", file), file, "mass");
        Range(d.Mass, 0.5f, 5000f, file, "mass");
        d.Health = Num(Need(root, "health", file), file, "health");
        Range(d.Health, 1f, 100000f, file, "health");
        d.Armour = Opt(root, "armour") is { } arm ? Num(arm, file, "armour") : 0f;
        Range(d.Armour, 0f, 1000f, file, "armour");
        d.Material = Opt(root, "material") is { } mat ? Str(mat, file, "material") : "steel";
        d.Friction = Opt(root, "friction") is { } fr ? Num(fr, file, "friction") : d.Material == "rubber" ? 1.0f : 0.6f;
        Range(d.Friction, 0f, 2f, file, "friction");
        d.OnBreak = Opt(root, "onBreak") is { } ob ? EnumOf<PartBreak>(Str(ob, file, "onBreak"), file, "onBreak")
            : d.Role is PartRole.Engine or PartRole.FuelTank or PartRole.Seat ? PartBreak.Disable : PartBreak.Detach;
        var colour = Opt(root, "color") is { } col ? Colour(col, file, "color") : new Vector3(0.5f);

        // Boxes
        if (Opt(root, "boxes") is not { ValueKind: JsonValueKind.Array } boxes || boxes.GetArrayLength() == 0)
            throw new ConstructPartException($"{file}: \"boxes\" must list at least one box");
        if (boxes.GetArrayLength() > 16)
            throw new ConstructPartException($"{file}: at most 16 boxes a part");
        var list = new List<CPartBox>();
        var bi = 0;
        foreach (var b in boxes.EnumerateArray())
        {
            var where = $"boxes[{bi++}]";
            Known(b, ["min", "max", "at", "size", "color", "detail"], $"{file}, {where}");
            Vector3 min, max;
            if (Opt(b, "min") is { } mn && Opt(b, "max") is { } mx)
            {
                min = Vec(mn, file, where + ".min");
                max = Vec(mx, file, where + ".max");
            }
            else if (Opt(b, "size") is { } sz)
            {
                var size = Vec(sz, file, where + ".size");
                var at = Opt(b, "at") is { } a ? Vec(a, file, where + ".at") : Vector3.Zero;
                min = at - size / 2;
                max = at + size / 2;
            }
            else
                throw new ConstructPartException($"{file}: {where} needs \"min\" and \"max\", or \"size\" (and \"at\")");
            if (max.X - min.X < 0.01f || max.Y - min.Y < 0.01f || max.Z - min.Z < 0.01f)
                throw new ConstructPartException($"{file}: {where} is thinner than 1 cm (or inside out)");
            if (Vector3.Max(Vector3.Abs(min), Vector3.Abs(max)) is var far && MathF.Max(far.X, MathF.Max(far.Y, far.Z)) > 4f)
                throw new ConstructPartException($"{file}: {where} reaches more than 4 m from the part's origin");
            var c = Opt(b, "color") is { } bc ? Colour(bc, file, where + ".color") : colour;
            list.Add(new CPartBox((min + max) / 2, (max - min) / 2, c, Opt(b, "detail") is { ValueKind: JsonValueKind.True }));
        }
        if (list.All(x => x.Detail))
            throw new ConstructPartException($"{file}: every box is a detail: at least one must be solid");
        // Solid boxes first (the physics walks those), details after.
        d.Boxes = list.Where(x => !x.Detail).Concat(list.Where(x => x.Detail)).ToArray();
        d.SolidBoxes = list.Count(x => !x.Detail);
        d.BoundsMin = new Vector3(float.MaxValue);
        d.BoundsMax = new Vector3(float.MinValue);
        foreach (var b in d.Boxes.AsSpan(0, d.SolidBoxes))
        {
            d.BoundsMin = Vector3.Min(d.BoundsMin, b.Center - b.Half);
            d.BoundsMax = Vector3.Max(d.BoundsMax, b.Center + b.Half);
        }

        // Sockets: each on a face of the part's bounds, facing out of it.
        if (Opt(root, "sockets") is not { ValueKind: JsonValueKind.Array } sockets || sockets.GetArrayLength() == 0)
            throw new ConstructPartException($"{file}: \"sockets\" must list at least one attachment point");
        if (sockets.GetArrayLength() > 40)
            throw new ConstructPartException($"{file}: at most 40 sockets a part");
        var socks = new List<CSocket>();
        var si = 0;
        foreach (var s in sockets.EnumerateArray())
        {
            var where = $"sockets[{si++}]";
            Known(s, ["name", "at", "dir", "kind"], $"{file}, {where}");
            var name = Opt(s, "name") is { } sn ? Str(sn, file, where + ".name") : $"s{si - 1}";
            if (socks.Any(x => x.Name == name))
                throw new ConstructPartException($"{file}: two sockets are called \"{name}\"");
            var at = Vec(Need(s, "at", $"{file}, {where}"), file, where + ".at");
            var dir = DirOf(Str(Need(s, "dir", $"{file}, {where}"), file, where + ".dir"), file, where);
            var kind = Opt(s, "kind") is { } k ? EnumOf<SocketKind>(Str(k, file, where + ".kind"), file, where + ".kind") : SocketKind.Struct;
            var axis = dir >> 1;
            var face = (dir & 1) == 0 ? d.BoundsMax[axis] : d.BoundsMin[axis];
            if (MathF.Abs(at[axis] - face) > 0.005f)
                throw new ConstructPartException($"{file}: socket \"{name}\" faces {DirName(dir)} but is not on that face of the part (at {at[axis]:0.###}, the face is at {face:0.###})");
            for (var a = 0; a < 3; a++)
                if (a != axis && (at[a] < d.BoundsMin[a] - 0.005f || at[a] > d.BoundsMax[a] + 0.005f))
                    throw new ConstructPartException($"{file}: socket \"{name}\" is outside the part");
            socks.Add(new CSocket(name, at, dir, kind));
        }
        d.Sockets = socks.ToArray();

        // Role data
        string[] roleKeys = ["engine", "wheel", "suspension", "fuelTank", "seat", "hinge"];
        var mine = d.Role switch
        {
            PartRole.Engine => "engine",
            PartRole.Wheel => "wheel",
            PartRole.Suspension => "suspension",
            PartRole.FuelTank => "fuelTank",
            PartRole.Seat => "seat",
            PartRole.Hinge => "hinge",
            _ => "",
        };
        foreach (var key in roleKeys)
            if (key != mine && Opt(root, key) != null)
                throw new ConstructPartException($"{file}: \"{key}\" is only for {key} parts (this one is a {d.Role.ToString().ToLowerInvariant()})");
        var block = mine.Length > 0 ? Opt(root, mine) : null;
        if (mine.Length > 0 && block is not { ValueKind: JsonValueKind.Object })
            throw new ConstructPartException($"{file}: a {mine} part needs a \"{mine}\" block");
        var bw = $"{file}, {mine}";
        switch (d.Role)
        {
            case PartRole.Engine:
                Known(block!.Value, ["force", "topSpeed", "burnRate"], bw);
                d.EngineForce = Num(Need(block.Value, "force", bw), file, "engine.force");
                Range(d.EngineForce, 10, 1_000_000, file, "engine.force");
                d.TopSpeed = Num(Need(block.Value, "topSpeed", bw), file, "engine.topSpeed");
                Range(d.TopSpeed, 0.5f, 120, file, "engine.topSpeed");
                d.BurnRate = Opt(block.Value, "burnRate") is { } br ? Num(br, file, "engine.burnRate") : 0.05f;
                Range(d.BurnRate, 0, 100, file, "engine.burnRate");
                break;
            case PartRole.Wheel:
                Known(block!.Value, ["radius", "grip"], bw);
                d.WheelRadius = Num(Need(block.Value, "radius", bw), file, "wheel.radius");
                Range(d.WheelRadius, 0.05f, 3, file, "wheel.radius");
                d.Grip = Opt(block.Value, "grip") is { } g ? Num(g, file, "wheel.grip") : 1f;
                Range(d.Grip, 0.05f, 4, file, "wheel.grip");
                if (!d.Sockets.Any(s => s.Kind == SocketKind.Axle))
                    throw new ConstructPartException($"{file}: a wheel needs an \"axle\" socket (it bolts onto a suspension)");
                break;
            case PartRole.Suspension:
                Known(block!.Value, ["travel", "frequency", "damping"], bw);
                d.Travel = Num(Need(block.Value, "travel", bw), file, "suspension.travel");
                Range(d.Travel, 0.02f, 2, file, "suspension.travel");
                d.Frequency = Opt(block.Value, "frequency") is { } f ? Num(f, file, "suspension.frequency") : 2f;
                Range(d.Frequency, 0.3f, 6, file, "suspension.frequency"); // stiffer than 6 Hz is unstable at 60 Hz
                d.Damping = Opt(block.Value, "damping") is { } dm ? Num(dm, file, "suspension.damping") : 0.6f;
                Range(d.Damping, 0, 2, file, "suspension.damping");
                if (!d.Sockets.Any(s => s.Kind == SocketKind.Axle))
                    throw new ConstructPartException($"{file}: a suspension needs an \"axle\" socket for its wheel");
                break;
            case PartRole.FuelTank:
                Known(block!.Value, ["capacity"], bw);
                d.FuelCapacity = Num(Need(block.Value, "capacity", bw), file, "fuelTank.capacity");
                Range(d.FuelCapacity, 0.1f, 10000, file, "fuelTank.capacity");
                break;
            case PartRole.Seat:
                Known(block!.Value, ["at"], bw);
                d.SeatAt = Vec(Need(block.Value, "at", bw), file, "seat.at");
                break;
            case PartRole.Hinge:
                Known(block!.Value, ["axis", "pivot", "min", "max", "leaf"], bw);
                d.JointAxis = DirOf(Str(Need(block.Value, "axis", bw), file, "hinge.axis"), file, "hinge.axis");
                d.JointPivot = Opt(block.Value, "pivot") is { } pv ? Vec(pv, file, "hinge.pivot") : Vector3.Zero;
                d.JointMin = Num(Need(block.Value, "min", bw), file, "hinge.min") * MathF.PI / 180f;
                d.JointMax = Num(Need(block.Value, "max", bw), file, "hinge.max") * MathF.PI / 180f;
                if (d.JointMin > 0 || d.JointMax < 0 || d.JointMax - d.JointMin < 0.05f || d.JointMax - d.JointMin > MathF.Tau)
                    throw new ConstructPartException($"{file}: hinge \"min\" must be 0 or less and \"max\" 0 or more (degrees), at most 360 apart");
                d.LeafSocket = Str(Need(block.Value, "leaf", bw), file, "hinge.leaf");
                if (!d.Sockets.Any(s => s.Name == d.LeafSocket && s.Kind == SocketKind.Leaf))
                    throw new ConstructPartException($"{file}: hinge \"leaf\" must name one of its \"leaf\" sockets");
                break;
        }
        if (d.Role == PartRole.Door && !d.Sockets.Any(s => s.Kind == SocketKind.Leaf))
            throw new ConstructPartException($"{file}: a door needs a \"leaf\" socket (it hangs on a hinge)");
        if (d.Role is not (PartRole.Wheel or PartRole.Suspension) && d.Sockets.Any(s => s.Kind == SocketKind.Axle))
            throw new ConstructPartException($"{file}: only wheels and suspensions have \"axle\" sockets");
        if (d.Role is not (PartRole.Hinge or PartRole.Door) && d.Sockets.Any(s => s.Kind == SocketKind.Leaf))
            throw new ConstructPartException($"{file}: only hinges and doors have \"leaf\" sockets");
        return d;
    }

    // ---- Small helpers ------------------------------------------------------------------------------------

    private static readonly string[] DirNames = ["+x", "-x", "+y", "-y", "+z", "-z"];

    public static string DirName(byte dir) => DirNames[dir % 6];

    public static Vector3 DirVector(byte dir) => dir switch
    {
        0 => Vector3.UnitX,
        1 => -Vector3.UnitX,
        2 => Vector3.UnitY,
        3 => -Vector3.UnitY,
        4 => Vector3.UnitZ,
        _ => -Vector3.UnitZ,
    };

    private static byte DirOf(string s, string file, string where)
    {
        var i = Array.IndexOf(DirNames, s.ToLowerInvariant());
        return i >= 0 ? (byte)i : throw new ConstructPartException($"{file}: {where}: a direction is one of {string.Join(", ", DirNames)}");
    }

    private static void Known(JsonElement obj, string[] keys, string where)
    {
        foreach (var p in obj.EnumerateObject())
            if (!keys.Any(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ConstructPartException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", keys)})");
    }

    private static JsonElement? Opt(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.Null ? null : p.Value;
        return null;
    }

    private static JsonElement Need(JsonElement obj, string name, string where) =>
        Opt(obj, name) ?? throw new ConstructPartException($"{where}: missing \"{name}\"");

    private static string StrKey(JsonElement obj, string name, string file) => Str(Need(obj, name, file), file, name);

    private static string Str(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new ConstructPartException($"{file}: \"{what}\" must be text");

    private static string AsciiText(JsonElement e, string file, string what)
    {
        var s = Str(e, file, what);
        return s.Any(c => c < 32 || c > 126) ? throw new ConstructPartException($"{file}: \"{what}\" must be plain ASCII") : s;
    }

    private static float Num(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle()) ? e.GetSingle() : throw new ConstructPartException($"{file}: \"{what}\" must be a number");

    private static void Range(float v, float lo, float hi, string file, string what)
    {
        if (v < lo || v > hi)
            throw new ConstructPartException($"{file}: \"{what}\" is {v.ToString(CultureInfo.InvariantCulture)}, it must be {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");
    }

    private static Vector3 Vec(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3
            ? new Vector3(Num(e[0], file, what), Num(e[1], file, what), Num(e[2], file, what))
            : throw new ConstructPartException($"{file}: \"{what}\" must be [x, y, z]");

    private static Vector3 Colour(JsonElement e, string file, string what)
    {
        try
        {
            return MapFile.ToColour(e, $"\"{what}\"", file + ": ");
        }
        catch (MapFileException ex)
        {
            throw new ConstructPartException(ex.Message);
        }
    }

    private static T EnumOf<T>(string s, string file, string what) where T : struct, Enum =>
        Enum.TryParse<T>(s, true, out var v) && Enum.IsDefined(v) && !int.TryParse(s, out _)
            ? v
            : throw new ConstructPartException($"{file}: \"{what}\" is one of {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}, not \"{s}\"");
}

/// <summary>
/// The 24 turns that keep a box's faces on the axes (a part is placed in one of these, so every part box stays axis-aligned
/// in its body's frame and two parts' overlap is a plain box test). Index 0 is no turn. The order is fixed: it is the wire id.
/// </summary>
public static class Rot24
{
    public const int Count = 24;

    // Each turn as its matrix: column c is where the local axis c goes (as a signed axis index into body space).
    private static readonly sbyte[][] Cols = Build();
    private static readonly Quaternion[] Quats = Cols.Select(QuatOf).ToArray();

    private static sbyte[][] Build()
    {
        var list = new List<sbyte[]>();
        int[][] perms = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var p in perms)
            for (var signs = 0; signs < 8; signs++)
            {
                var m = new float[3, 3];
                for (var c = 0; c < 3; c++)
                    m[p[c], c] = (signs >> c & 1) == 0 ? 1 : -1;
                var det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
                if (det < 0)
                    continue;
                var col = new sbyte[3];
                for (var c = 0; c < 3; c++)
                    col[c] = (sbyte)(p[c] * 2 + ((signs >> c & 1) == 0 ? 0 : 1)); // a signed axis as a dir index (+x 0, -x 1...)
                list.Add(col);
            }
        return list.ToArray();
    }

    private static Quaternion QuatOf(sbyte[] col)
    {
        var x = ConstructParts.DirVector((byte)col[0]);
        var y = ConstructParts.DirVector((byte)col[1]);
        var z = ConstructParts.DirVector((byte)col[2]);
        var m = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    public static Quaternion Quat(byte r) => Quats[r % Count];

    /// <summary>A vector from the part's frame into its body's (exact: only swaps and sign changes).</summary>
    public static Vector3 Apply(byte r, Vector3 v)
    {
        var c = Cols[r % Count];
        var o = Vector3.Zero;
        o = Add(o, c[0], v.X);
        o = Add(o, c[1], v.Y);
        o = Add(o, c[2], v.Z);
        return o;

        static Vector3 Add(Vector3 o, sbyte dir, float k)
        {
            var s = (dir & 1) == 0 ? k : -k;
            switch (dir >> 1)
            {
                case 0: o.X += s; break;
                case 1: o.Y += s; break;
                default: o.Z += s; break;
            }
            return o;
        }
    }

    /// <summary>A box's half size turned (always positive).</summary>
    public static Vector3 ApplyHalf(byte r, Vector3 half) => Vector3.Abs(Apply(r, half));

    /// <summary>A direction index (0..5) turned.</summary>
    public static byte ApplyDir(byte r, byte dir)
    {
        var c = Cols[r % Count][dir >> 1];
        return (byte)((dir & 1) == 0 ? c : c ^ 1);
    }

    /// <summary>The turns that take direction <paramref name="from"/> (part frame) to <paramref name="to"/> (body frame), in a fixed order: four of them.</summary>
    public static int Matching(byte from, byte to, Span<byte> into)
    {
        var n = 0;
        for (byte r = 0; r < Count && n < into.Length; r++)
            if (ApplyDir(r, from) == to)
                into[n++] = r;
        return n;
    }
}
