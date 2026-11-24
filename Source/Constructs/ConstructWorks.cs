using System.Numerics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// What a works piece is (docs/sandbox/design/ship-works.md 3.1). A head (a drive's nozzle, a gun on a ring, a launcher)
/// lists the kinds it needs; a piece says which kind it is and how much it serves. The order is fixed: a head's needs are
/// kept as bits by this number, so new kinds go at the end.
/// </summary>
public enum WorksKind : byte
{
    None,
    Mount, // a piece of hull a head goes through: works bolted to it belong to the head bolted to it
    Core, // a torch drive's reaction chamber (N of thrust)
    Pump, // a torch drive's fuel pump (N)
    Exchanger, // a torch drive's coolant exchanger (N)
    Turbopump, // a rocket's propellant pump (N)
    Manifold, // a rocket's mixing manifold (N)
    Conditioner, // an ion drive's power conditioner (N)
    Loader, // a ring's autoloader (guns of its size)
    Lasergen, // a laser emitter's generator (kW of beam)
    Hoist, // a launcher's reload hoist (kg of one weapon)
    Injector, // a torch drive's fuel injector (N): the train's fuel goes in through it (a pipe into its Fuel in), and it holds the buffer a cut line runs on
    Train, // an engine train's function piece (Sean 2026-11-24): serves no amount, adds what its TrainFunction says to the bell it is welded behind
}

/// <summary>What an engine train's function piece adds (docs/sandbox/design/engine-trains.md, "Engine pieces"). Append only.</summary>
public enum TrainFunction : byte
{
    None,
    Spacer, // an empty shell: length, and the pass-throughs; any zone
    Buffer, // feed: 15 s more running on a cut line, each one adds
    Jacket, // hot: 10% less fuel at the same push (once)
    Igniter, // feed: the drive lights at once (without one, a bell that needs an injector takes ShipWorks.LightSeconds to light)
    Sensor, // supply: the drive's level, buffer, health and heat on four data outputs (SensorPort)
}

/// <summary>A head's place in its works (rebuilt with the caches): its assembly and what its works let it do.</summary>
public struct CWorksHead
{
    public ushort Part;
    public short Asm; // its assembly (-1: it reaches no mount, or a thruster has no engine in line behind its bell)
    public int Mount; // the mount it goes through (-1: none)
    public bool Taken; // a thruster: the cylinder in line behind its bell was taken by another bell first
    public bool Outside; // its engine is in a nacelle (its head or its mount says "service": "outside"): reported at the Engineer's Panel, mended through the nacelle's hatch
    public float Cap; // 0..1: the share of its rating its works let it have (a thruster never under ShipWorks.BareShare)
    public float Burn; // a thruster: its burn at full level, as a share of its file's burnRate (above Cap when it is short of works: it burns at a jet's rate)
    public WorksKind Short; // the kind that holds it back most (None: nothing does)
    public bool Fed; // a head that needs a Fuel Injector: a pipe from a working tank reaches a working injector's Fuel in in its engine (touching tanks don't count)
    public float BufferMax; // such a head: seconds of full burn its injector holds (ShipWorks.InjectorBuffer, and each working Fuel Buffer's)
    public bool Igniter; // such a head: a working Igniter is in its train, so it lights at once
    public int Stray; // such a head: the first piece welded into its train in the wrong zone or past the piece limit, left out of it (-1: none)
}

/// <summary>
/// The works' numbers (Constructs/ShipWorks.json, docs/sandbox/design/ship-works.md): the thrust over which a ship's
/// thruster must have works, the share a drive short of them keeps, the thrust from which a drive type needs each kind,
/// and what mending a broken piece costs. The defaults match the file.
/// </summary>
public sealed class ShipWorks
{
    public const string Format = "call-of-slop-ship-works/1";

    public float JetMost = 500_000; // N: a Ship thruster over this has works (the part kit refuses one that has none)
    public float BareShare = 0.25f; // a thruster's works let it have at least this share
    public float JetBurn = 3f; // l/s a MN: what a drive short of its works burns for what it pushes
    public string MendMaterial = "metal";
    public float MendPerTonne = 2f; // what a works piece broken by damage takes to mend, a tonne of it (at least 1)
    public float LoaderReload = 0.4f; // a gun of six rounds or fewer on a ring with its autoloaders reloads in this share of its time
    public float HoistSeconds = 6f; // s a Reload Hoist takes to move a weapon of HoistMass (ship-works.md 6)
    public float HoistMass = 80f; // kg: the weapon that takes HoistSeconds; the time grows with the square root of the mass
    public float FuelBufferSeconds = 15f; // s more a Fuel Buffer adds to its train's (each one, Sean 2026-11-24 doc, a first figure)
    public float JacketSaving = 0.1f; // the share of fuel a Cooling Jacket saves at the same push (once a train)
    public float LightSeconds = 3f; // s a bell that needs an injector takes to light with no Igniter in its train (Sean 2026-11-24 doc)
    public int TrainPieces = 16; // the most pieces an engine train holds (its walk and its looks stay bounded)
    public float InjectorBuffer = 10f; // s of full burn a Fuel Injector holds: a bell whose pipe is cut runs this long, then stops (Sean, 2026-11-24, a first figure)
    public string Problem = "";

    /// <summary>Per drive type, the thrust (N) from which each kind is needed, in order: torch, then rocket and ion.</summary>
    public readonly Dictionary<string, (WorksKind Kind, float From)[]> Drives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["torch"] = [(WorksKind.Core, 500_000), (WorksKind.Pump, 2_000_000), (WorksKind.Exchanger, 6_000_000)],
        ["rocket"] = [(WorksKind.Turbopump, 500_000), (WorksKind.Manifold, 500_000)],
        ["ion"] = [(WorksKind.Conditioner, 0)],
    };

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "ShipWorks.json");

    private static ShipWorks? _current;

    /// <summary>The table in use (loaded once; the defaults above when it is missing or refused).</summary>
    public static ShipWorks Current => _current ??= Load(FilePath);

    /// <summary>The kinds a drive of this type and thrust needs (the part kit writes them into the head's file).</summary>
    public WorksKind[] NeedsFor(string type, float force)
    {
        if (!Drives.TryGetValue(type, out var rows))
            throw new ArgumentException($"no drive type \"{type}\" in ShipWorks.json");
        return force <= JetMost && type.Equals("torch", StringComparison.OrdinalIgnoreCase) ? [] : [.. rows.Where(r => force > r.From || r.From == 0).Select(r => r.Kind)];
    }

    public static ShipWorks Load(string path)
    {
        var t = new ShipWorks();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "jetMost" or "bareShare" or "jetBurn" or "mendMaterial" or "mendPerTonne" or "loaderReload" or "hoistSeconds" or "hoistMass" or "injectorBuffer" or "fuelBuffer" or "jacketSaving" or "trainPieces" or "lightSeconds" or "drives"))
                    throw new FormatException($"unknown key \"{p.Name}\"");
            t.JetMost = Num(root, "jetMost", 1, 1e9f);
            t.BareShare = Num(root, "bareShare", 0, 1);
            t.JetBurn = Num(root, "jetBurn", 0.01f, 100);
            t.MendMaterial = root.GetProperty("mendMaterial").GetString() ?? "";
            if (RecyclingTable.Current.IdOf(t.MendMaterial) == 0)
                throw new FormatException($"\"mendMaterial\": no material named \"{t.MendMaterial}\" in Recycling.json");
            t.MendPerTonne = Num(root, "mendPerTonne", 0, 1000);
            t.LoaderReload = Num(root, "loaderReload", 0.05f, 1);
            t.HoistSeconds = Num(root, "hoistSeconds", 0.5f, 120);
            t.HoistMass = Num(root, "hoistMass", 1, 100_000);
            if (root.TryGetProperty("injectorBuffer", out _))
                t.InjectorBuffer = Num(root, "injectorBuffer", 0, 600);
            if (root.TryGetProperty("fuelBuffer", out _))
                t.FuelBufferSeconds = Num(root, "fuelBuffer", 0, 600);
            if (root.TryGetProperty("jacketSaving", out _))
                t.JacketSaving = Num(root, "jacketSaving", 0, 0.9f);
            if (root.TryGetProperty("lightSeconds", out _))
                t.LightSeconds = Num(root, "lightSeconds", 0, 60);
            if (root.TryGetProperty("trainPieces", out _))
                t.TrainPieces = (int)Num(root, "trainPieces", 1, Construct.MaxWorksAssembly);
            t.Drives.Clear();
            foreach (var type in root.GetProperty("drives").EnumerateObject())
            {
                var rows = new List<(WorksKind, float)>();
                foreach (var k in type.Value.EnumerateObject())
                {
                    if (!Enum.TryParse<WorksKind>(k.Name, true, out var kind) || kind is WorksKind.None or WorksKind.Mount || int.TryParse(k.Name, out _))
                        throw new FormatException($"\"drives.{type.Name}\": \"{k.Name}\" is no kind of works");
                    var from = k.Value.GetSingle();
                    if (!float.IsFinite(from) || from < 0 || from > 1e10f)
                        throw new FormatException($"\"drives.{type.Name}.{k.Name}\" is {from}, not 0 to 1e10 N");
                    rows.Add((kind, from));
                }
                if (rows.Count == 0)
                    throw new FormatException($"\"drives.{type.Name}\" names no kind of works");
                t.Drives[type.Name] = [.. rows];
            }
            if (!t.Drives.ContainsKey("torch"))
                throw new FormatException("\"drives\" has no \"torch\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            t = new ShipWorks { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
            Console.WriteLine($"[works] REFUSED {t.Problem}: the defaults are used");
        }
        return t;
    }

    private static float Num(JsonElement root, string key, float min, float max)
    {
        var v = root.GetProperty(key).GetSingle();
        if (!float.IsFinite(v) || v < min || v > max)
            throw new FormatException($"\"{key}\" is {v}, not between {min} and {max}");
        return v;
    }

    /// <summary>What mending a works piece of this mass takes (whole units of <see cref="MendMaterial"/>, at least 1).</summary>
    public int MendCost(float kg) => MendPerTonne <= 0 ? 0 : Math.Max(1, (int)MathF.Round(kg / 1000f * MendPerTonne));
}

public sealed partial class Construct
{
    public const int MaxWorksAssembly = 64; // parts one assembly's flood takes in (docs/sandbox/design/ship-works.md 3.2)

    public CWorksHead[] WorksHeads = []; // its heads, in part order (made with the caches: ConstructWorks.Build)
    public int WorksHeadCount;
    public ushort[] WorksPieces = []; // the mounts and pieces its assemblies took in
    public short[] WorksPieceAsm = [];
    public int WorksPieceCount;
    public int WorksAsmCount;
    public float[] WorksNeed = [], WorksServed = []; // by assembly, then kind: what its heads ask and its working pieces give
    public byte[] WorksHave = [], WorksAll = []; // by assembly, then kind: its working pieces, and all of them

    /// <summary>A thruster's push now could be, at full level (N): its force, times what its works let it have.</summary>
    public float ThrustOf(int k) => Parts[Thrusters[k].Part].D.ThrustForce * Thrusters[k].Cap;

    /// <summary>The head entry of part <paramref name="part"/>, or -1 (its heads are in part order).</summary>
    public int WorksHeadOf(int part)
    {
        int lo = 0, hi = WorksHeadCount - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var at = WorksHeads[mid].Part;
            if (at == part)
                return mid;
            if (at < part)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return -1;
    }
}

/// <summary>
/// Ship works (docs/sandbox/design/ship-works.md): a big drive, a turret with an interior and a reloading launcher are
/// assemblies. The part outside is the head; behind it, through the hull, are its works, found by touch at the rebuild: from
/// the head down any joints it stands on to a mount, then every mount and works piece touching that one. What a head can do
/// is set by the working pieces behind it (its cap). Nothing here runs per tick or goes on the wire: every peer finds the
/// same from the parts.
/// </summary>
public static class ConstructWorks
{
    public const int KindCount = 13; // (WorksKind's values)
    private const int MountSteps = 4; // joints a head may stand on between it and its mount (a gun's hinge and ring)

    public static readonly string[] KindNames = ["", "mount", "core", "pump", "exchanger", "turbopump", "manifold", "conditioner", "loader", "lasergen", "hoist", "injector", "train"];

    /// <summary>The piece a player knows a kind by, in a look line ("no Drive Core behind it").</summary>
    public static string KindPiece(WorksKind k) => k switch
    {
        WorksKind.Mount => "Hull Mount",
        WorksKind.Core => "Drive Core",
        WorksKind.Pump => "Fuel Pump",
        WorksKind.Exchanger => "Coolant Exchanger",
        WorksKind.Turbopump => "Turbopump",
        WorksKind.Manifold => "Mixing Manifold",
        WorksKind.Conditioner => "Power Conditioner",
        WorksKind.Loader => "Autoloader",
        WorksKind.Lasergen => "Laser Generator",
        WorksKind.Hoist => "Reload Hoist",
        WorksKind.Injector => "Fuel Injector",
        WorksKind.Train => "engine piece",
        _ => "works",
    };

    public static readonly string[] TrainFunctionNames = ["", "spacer", "buffer", "jacket", "igniter", "sensor"];

    /// <summary>An engine train piece's zone, counted back from the bell (Sean 2026-11-24): 0 hot, 1 feed, 2 supply; -1 any zone (a Spacer) or none.</summary>
    public static int ZoneOf(ConstructPartDef d) => d.WorksKind switch
    {
        WorksKind.Core => 0,
        WorksKind.Injector => 1,
        WorksKind.Pump or WorksKind.Exchanger or WorksKind.Turbopump => 2,
        WorksKind.Train => d.TrainFn switch { TrainFunction.Jacket => 0, TrainFunction.Buffer or TrainFunction.Igniter => 1, TrainFunction.Sensor => 2, _ => -1 },
        _ => -1,
    };

    public static readonly string[] ZoneNames = ["hot", "feed", "supply"];

    /// <summary>Is this part a head (it needs works behind it)?</summary>
    public static bool IsHead(ConstructPartDef d) => d.WorksNeeds != 0;

    /// <summary>Is this part a works piece or a mount?</summary>
    public static bool IsPiece(ConstructPartDef d) => d.WorksKind != WorksKind.None;

    /// <summary>A drive's works piece (a core, pump, exchanger, turbopump, manifold, conditioner or injector): what a breakdown picks on a hull drive. An explicit set: a range would miss a kind appended later.</summary>
    public static bool IsDrivePiece(ConstructPartDef d) => d.WorksKind is WorksKind.Core or WorksKind.Pump or WorksKind.Exchanger or WorksKind.Turbopump or WorksKind.Manifold or WorksKind.Conditioner or WorksKind.Injector or WorksKind.Train;

    /// <summary>Either: it has a works line on the look.</summary>
    public static bool Has(ConstructPartDef d) => IsHead(d) || IsPiece(d);

    /// <summary>
    /// Is this part a Turret Ring: a joint whose base is a mount (ship-works.md 5.2)? Its trunk takes works (autoloaders), and an
    /// ammo feed into its Ammo in feeds every gun standing on it, with no link across the joint.
    /// </summary>
    public static bool IsRing(ConstructPartDef d) => d.HasJoint && d.WorksKind == WorksKind.Mount;

    /// <summary>Does a head need this kind?</summary>
    public static bool Needs(ConstructPartDef d, WorksKind k) => (d.WorksNeeds & (1 << (int)k)) != 0;

    /// <summary>What a head asks of each kind it needs: a thruster its force (N), anything else its rating.</summary>
    public static float NeedOf(ConstructPartDef d) => d.Role == PartRole.Thruster ? d.ThrustForce : MathF.Max(1, d.WorksRating);

    /// <summary>The <c>"works"</c> and <c>"worksPiece"</c> keys of a part file (after its role's block).</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "works") is { } w)
        {
            ConstructParts.Known(w, ["needs", "service", "rating"], $"{file}: \"works\"");
            if (ConstructParts.Opt(w, "needs") is { } needs)
            {
                if (needs.ValueKind != JsonValueKind.Array)
                    throw new ConstructPartException($"{file}: \"works.needs\" lists kinds of works ({string.Join(", ", KindNames.Skip(2))})");
                foreach (var n in needs.EnumerateArray())
                {
                    var k = KindOf(ConstructParts.Str(n, file, "works.needs"), file, "works.needs");
                    if (k is WorksKind.Mount or WorksKind.Train)
                        throw new ConstructPartException($"{file}: \"works.needs\" names kinds of works, not a mount or an engine train's function piece (a head finds those by touch)");
                    d.WorksNeeds |= (ushort)(1 << (int)k);
                }
            }
            var service = ConstructParts.Opt(w, "service") is { } sv ? ConstructParts.Str(sv, file, "works.service") : "inside";
            d.WorksOutside = service switch
            {
                "inside" => false,
                "outside" => true,
                _ => throw new ConstructPartException($"{file}: \"works.service\" is inside or outside, not \"{service}\""),
            };
            if (ConstructParts.Opt(w, "rating") is { } rt)
            {
                d.WorksRating = ConstructParts.Num(rt, file, "works.rating");
                ConstructParts.Range(d.WorksRating, 0, 1e10f, file, "works.rating");
            }
            if (d.WorksNeeds == 0)
                throw new ConstructPartException($"{file}: \"works\" needs something (\"needs\"): a drive in a nacelle (\"service\": \"outside\") has its engine parts behind its bell too");
        }
        if (ConstructParts.Opt(root, "worksPiece") is { } wp)
        {
            ConstructParts.Known(wp, ["kind", "serves", "service", "function"], $"{file}: \"worksPiece\"");
            d.WorksKind = KindOf(ConstructParts.Str(ConstructParts.Need(wp, "kind", $"{file}: \"worksPiece\""), file, "worksPiece.kind"), file, "worksPiece.kind");
            if (d.WorksKind == WorksKind.Mount)
            {
                if (ConstructParts.Opt(wp, "serves") != null)
                    throw new ConstructPartException($"{file}: a mount serves nothing: no \"worksPiece.serves\"");
                var service = ConstructParts.Opt(wp, "service") is { } sv ? ConstructParts.Str(sv, file, "worksPiece.service") : "inside";
                d.WorksOutside = service switch
                {
                    "inside" => false,
                    "outside" => true, // (a nacelle's tail plate: the drives through it are serviced outside, through its hatch)
                    _ => throw new ConstructPartException($"{file}: \"worksPiece.service\" is inside or outside, not \"{service}\""),
                };
            }
            else if (d.WorksKind == WorksKind.Train)
            {
                var fn = ConstructParts.Str(ConstructParts.Need(wp, "function", $"{file}: \"worksPiece\" of kind train"), file, "worksPiece.function");
                var at = Array.IndexOf(TrainFunctionNames, fn);
                d.TrainFn = at >= 1 ? (TrainFunction)at : throw new ConstructPartException($"{file}: \"worksPiece.function\" is one of {string.Join(", ", TrainFunctionNames.Skip(1))}, not \"{fn}\"");
                if (d.TrainFn == TrainFunction.Sensor)
                    d.DataOut = SensorPorts;
                if (ConstructParts.Opt(wp, "serves") != null || ConstructParts.Opt(wp, "service") != null)
                    throw new ConstructPartException($"{file}: an engine train's function piece serves no amount (no \"worksPiece.serves\" or \"service\")");
            }
            else
            {
                if (ConstructParts.Opt(wp, "function") != null)
                    throw new ConstructPartException($"{file}: only a piece of kind train has a \"worksPiece.function\"");
                if (ConstructParts.Opt(wp, "service") != null)
                    throw new ConstructPartException($"{file}: only a mount says where it is serviced (\"worksPiece.service\")");
                d.WorksServes = ConstructParts.Num(ConstructParts.Need(wp, "serves", $"{file}: \"worksPiece\""), file, "worksPiece.serves");
                ConstructParts.Range(d.WorksServes, 1, 1e10f, file, "worksPiece.serves");
            }
            if (IsHead(d))
                throw new ConstructPartException($"{file}: a part is a head (\"works\") or a works piece (\"worksPiece\"), not both");
        }
    }

    private static WorksKind KindOf(string s, string file, string what)
    {
        var i = Array.IndexOf(KindNames, s.ToLowerInvariant());
        return i >= 1 ? (WorksKind)i : throw new ConstructPartException($"{file}: \"{what}\" is one of {string.Join(", ", KindNames.Skip(1))}, not \"{s}\"");
    }

    /// <summary>
    /// With the caches, before the thrusters (they take their caps): every head's assembly and cap. One pass over the special
    /// parts (heads, mounts and pieces are all special), a walk from each head to its mount, and a walk that visits each piece
    /// once. A body with no heads pays one loop.
    ///
    /// A thruster has an engine of its own (docs/sandbox/design/ship-works.md 15, rules 1 to 4): its bell, the plate it goes
    /// through, and the cylinder behind that plate on its thrust line. Its assembly is the works piece on its mount that lies on
    /// that line and every piece bolted on behind it, through works pieces only (never through a mount), so four bells on one
    /// stern plate are four engines and nothing is shared between them. Guns, emitters and racks share what is on their mount.
    /// </summary>
    public static void Build(Construct c)
    {
        c.WorksHeadCount = c.WorksPieceCount = c.WorksAsmCount = c.HoistCount = 0;
        int heads = 0, rings = 0, racks = 0;
        foreach (var i in c.Specials)
            if (IsHead(c.Parts[i].D))
                heads++;
            else if (IsRing(c.Parts[i].D))
                rings++;
            else if (c.Parts[i].D.Logic == CLogic.BombRack)
                racks++;
        if (heads == 0 && rings == 0 && racks == 0)
            return;
        if (c.WorksHeads.Length < heads)
            Array.Resize(ref c.WorksHeads, Math.Max(heads, c.WorksHeads.Length * 2));
        using var scratch = Scratch.Begin();
        var asmOf = scratch.Take<short>(c.PartCount);
        asmOf.Fill(-1);
        var queue = scratch.Take<ushort>(c.PartCount);
        foreach (var i in c.Specials)
            if (IsHead(c.Parts[i].D))
            {
                var mount = FindMount(c, i, queue);
                c.WorksHeads[c.WorksHeadCount++] = new CWorksHead { Part = (ushort)i, Asm = -1, Mount = mount, Outside = c.Parts[i].D.WorksOutside || mount >= 0 && c.Parts[mount].D.WorksOutside, Cap = 1, Burn = 1, Stray = -1 };
            }
        // The thrusters first, each taking the engine in line behind its bell; then the rest share what is left on their mounts.
        for (var h = 0; h < c.WorksHeadCount; h++)
        {
            ref var wh = ref c.WorksHeads[h];
            if (wh.Mount < 0 || c.Parts[wh.Part].D.Role != PartRole.Thruster)
                continue;
            var first = InLine(c, wh.Part, wh.Mount, asmOf, out wh.Taken);
            wh.Stray = -1;
            if (first >= 0)
            {
                var from = c.WorksPieceCount;
                wh.Asm = Stack(c, first, asmOf, queue);
                if (InjectorFedHead(c.Parts[wh.Part].D))
                    wh.Stray = Zones(c, wh.Part, from);
            }
        }
        for (var h = 0; h < c.WorksHeadCount; h++)
        {
            ref var wh = ref c.WorksHeads[h];
            if (wh.Mount < 0 || c.Parts[wh.Part].D.Role == PartRole.Thruster)
                continue;
            wh.Asm = asmOf[wh.Mount] >= 0 ? asmOf[wh.Mount] : Flood(c, wh.Mount, asmOf, queue);
        }
        // A Turret Ring is an assembly whether or not a head reaches it: the guns standing on it take its autoloaders (BuildGuns, after this).
        if (rings > 0)
            foreach (var i in c.Specials)
                if (IsRing(c.Parts[i].D) && asmOf[i] < 0)
                    Flood(c, i, asmOf, queue);
        // A launcher rack on a mount: the Reload Hoists in its mount's assembly load it (ship-works.md 6; ConstructHoists.cs).
        if (racks > 0)
            foreach (var i in c.Specials)
                if (c.Parts[i].D.Logic == CLogic.BombRack && FindMount(c, i, queue) is var m and >= 0)
                    Hoists(c, i, asmOf[m] >= 0 ? asmOf[m] : Flood(c, m, asmOf, queue));
        // What each assembly's heads ask of each kind, and what its working pieces give.
        var cells = c.WorksAsmCount * KindCount;
        if (c.WorksNeed.Length < cells)
        {
            var n = Math.Max(cells, c.WorksNeed.Length * 2);
            Array.Resize(ref c.WorksNeed, n);
            Array.Resize(ref c.WorksServed, n);
            Array.Resize(ref c.WorksHave, n);
            Array.Resize(ref c.WorksAll, n);
        }
        Array.Clear(c.WorksNeed, 0, cells);
        Array.Clear(c.WorksServed, 0, cells);
        Array.Clear(c.WorksHave, 0, cells);
        Array.Clear(c.WorksAll, 0, cells);
        for (var k = 0; k < c.WorksPieceCount; k++)
        {
            ref var p = ref c.Parts[c.WorksPieces[k]];
            var d = p.D;
            if (d.WorksKind == WorksKind.Mount || d.ModPart == ModPart.Socket)
                continue; // (a mount, or an engine socket's plate standing as one, serves nothing)
            var cell = c.WorksPieceAsm[k] * KindCount + (int)d.WorksKind;
            c.WorksAll[cell] = (byte)Math.Min(255, c.WorksAll[cell] + 1);
            if (!p.Working)
                continue;
            c.WorksHave[cell] = (byte)Math.Min(255, c.WorksHave[cell] + 1);
            c.WorksServed[cell] += d.WorksServes;
        }
        for (var h = 0; h < c.WorksHeadCount; h++)
        {
            ref var wh = ref c.WorksHeads[h];
            if (wh.Asm < 0)
                continue;
            var d = c.Parts[wh.Part].D;
            var need = NeedOf(d);
            for (var k = 2; k < KindCount; k++)
                if (Needs(d, (WorksKind)k))
                    c.WorksNeed[wh.Asm * KindCount + k] += need;
        }
        var t = ShipWorks.Current;
        for (var h = 0; h < c.WorksHeadCount; h++)
        {
            ref var wh = ref c.WorksHeads[h];
            var d = c.Parts[wh.Part].D;
            var cap = 1f;
            for (var k = 2; k < KindCount; k++)
            {
                if (!Needs(d, (WorksKind)k))
                    continue;
                var share = wh.Asm < 0 ? 0f : MathF.Min(1, c.WorksServed[wh.Asm * KindCount + k] / MathF.Max(1, c.WorksNeed[wh.Asm * KindCount + k]));
                if (share < cap)
                {
                    cap = share;
                    wh.Short = (WorksKind)k;
                }
            }
            if (d.Role == PartRole.Thruster)
            {
                cap = MathF.Max(cap, t.BareShare); // (a bare nozzle still pushes at a quarter: the ship can limp, Karl answer 2)
                if (cap < 0.999f && d.ThrustFuel == EngineFuel.Fuel && d.ThrustBurn > 0)
                    wh.Burn = cap * MathF.Max(1, t.JetBurn * d.ThrustForce / 1e6f / d.ThrustBurn); // (short of its works it burns as a jet does for what it pushes)
                else
                    wh.Burn = cap;
            }
            wh.Cap = cap;
            wh.Fed = Needs(d, WorksKind.Injector) && wh.Asm >= 0 && InjectorFed(c, wh.Asm);
            wh.BufferMax = t.InjectorBuffer;
            if (wh.Asm >= 0 && d.Role == PartRole.Thruster)
                TrainFunctions(c, ref wh, t); // (any drive's engine: a Cooling Jacket welded behind a Drive Core saves fuel too)
        }
    }

    /// <summary>
    /// The zones of the train just stacked for bell <paramref name="head"/> (its pieces from <paramref name="from"/> on): counted back from the
    /// bell along its axis, hot pieces first, then feed, then supply, a Spacer anywhere, at most <see cref="ShipWorks.TrainPieces"/>. A piece
    /// behind one of a later zone, or past the limit, is taken out of the train (it serves nothing); the first such is returned, else -1.
    /// Bounded by the train's size (at most <see cref="Construct.MaxWorksAssembly"/>), at the rebuild only.
    /// </summary>
    private static int Zones(Construct c, int head, int from)
    {
        var (at, dir) = ThrustLine(c, head);
        var stray = -1;
        var limit = ShipWorks.Current.TrainPieces;
        for (var k = from; k < c.WorksPieceCount; k++)
        {
            var i = c.WorksPieces[k];
            var zi = ZoneOf(c.Parts[i].D);
            var back = -Vector3.Dot(c.Parts[i].Pos - at, dir);
            var ahead = 0; // (pieces nearer the bell)
            var wrong = false;
            for (var j = from; j < c.WorksPieceCount; j++)
            {
                if (j == k)
                    continue;
                var o = c.WorksPieces[j];
                var bj = -Vector3.Dot(c.Parts[o].Pos - at, dir);
                if (bj < back - 1e-3f || bj <= back + 1e-3f && j < k)
                {
                    ahead++;
                    if (zi >= 0 && ZoneOf(c.Parts[o].D) > zi)
                        wrong = true;
                }
            }
            if (!wrong && ahead < limit)
                continue;
            if (stray < 0 || -Vector3.Dot(c.Parts[stray].Pos - at, dir) > back)
                stray = i;
        }
        if (stray < 0)
            return -1;
        // Out of the train: the stray piece and every piece behind it (the walk stops at the first broken rule).
        var cut = -Vector3.Dot(c.Parts[stray].Pos - at, dir);
        var n = from;
        for (var k = from; k < c.WorksPieceCount; k++)
        {
            var i = c.WorksPieces[k];
            if (-Vector3.Dot(c.Parts[i].Pos - at, dir) >= cut - 1e-3f)
                continue;
            c.WorksPieces[n] = i;
            c.WorksPieceAsm[n++] = c.WorksPieceAsm[k];
        }
        c.WorksPieceCount = n;
        return stray;
    }

    /// <summary>Why the piece <see cref="CWorksHead.Stray"/> is left out of its bell's train, for a look line or the works report.</summary>
    private static string StrayWhy(Construct c, in CWorksHead h)
    {
        var sd = c.Parts[h.Stray].D;
        var z = ZoneOf(sd);
        return z >= 0
            ? $"the {sd.Name} and what is behind it are out of the engine: a {ZoneNames[z]} piece goes before {(z == 0 ? "the feed and supply pieces" : "the supply pieces")} (or past {ShipWorks.Current.TrainPieces} pieces)"
            : $"the {sd.Name} and what is behind it are out of the engine: a train holds at most {ShipWorks.Current.TrainPieces} pieces";
    }

    public static readonly string[] SensorPorts = ["level", "buffer", "health", "heat"]; // (a Sensor Ring's data outputs, by index: add at the end)

    /// <summary>
    /// A Sensor Ring's output <paramref name="port"/> (<see cref="SensorPorts"/>), read by the logic tick: its bell's level (% of full push),
    /// buffer (s), health (% of its weakest working piece, the bell's own included) and heat (%), each a whole number so a wire's value
    /// changes only as the drive does. 0 when the ring is in no bell's train. Bounded by the body's works pieces and thrusters.
    /// </summary>
    public static float SensorPort(Construct c, int part, int port)
    {
        var asm = AsmOfPiece(c, part);
        if (asm < 0)
            return 0;
        var head = -1;
        for (var k = 0; k < c.WorksHeadCount && head < 0; k++)
            if (c.WorksHeads[k].Asm == asm)
                head = c.WorksHeads[k].Part;
        if (head < 0)
            return 0;
        if (port == 2)
        {
            var low = Share(c.Parts[head]);
            for (var k = 0; k < c.WorksPieceCount; k++)
                if (c.WorksPieceAsm[k] == asm)
                    low = MathF.Min(low, Share(c.Parts[c.WorksPieces[k]]));
            return MathF.Round(low * 100);
        }
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            if (t.Part != head)
                continue;
            return port switch
            {
                0 => MathF.Round(t.Level * t.Cap * 100),
                1 => MathF.Round(t.Buffer),
                3 => MathF.Round(t.Heat * 100),
                _ => 0,
            };
        }
        return 0;

        static float Share(in CPart p) => p.Broken ? 0 : p.D.Health > 0 ? Math.Clamp(p.Health / p.D.Health, 0, 1) : 1;
    }

    /// <summary>What a train's working function pieces add to its bell (read at the rebuild): each Fuel Buffer's seconds, a Cooling Jacket's saving.</summary>
    private static void TrainFunctions(Construct c, ref CWorksHead wh, ShipWorks t)
    {
        var jacket = false;
        for (var k = 0; k < c.WorksPieceCount; k++)
        {
            if (c.WorksPieceAsm[k] != wh.Asm || c.Parts[c.WorksPieces[k]] is not { Working: true } p || p.D.WorksKind != WorksKind.Train)
                continue;
            if (p.D.TrainFn == TrainFunction.Buffer)
                wh.BufferMax += t.FuelBufferSeconds;
            else if (p.D.TrainFn == TrainFunction.Jacket)
                jacket = true;
            else if (p.D.TrainFn == TrainFunction.Igniter)
                wh.Igniter = true;
        }
        if (jacket)
            wh.Burn *= 1 - t.JacketSaving;
    }

    /// <summary>Does a pipe from a working tank reach the Fuel in of a working Fuel Injector in assembly <paramref name="asm"/> (Sean, 2026-11-24: a pipe only; a tank touching the train feeds nothing)?</summary>
    private static bool InjectorFed(Construct c, short asm)
    {
        for (var k = 0; k < c.WorksPieceCount; k++)
            if (c.WorksPieceAsm[k] == asm && c.Parts[c.WorksPieces[k]] is { Working: true } p && p.D.WorksKind == WorksKind.Injector && ConstructRoutes.PipeFeeds(c, c.WorksPieces[k]))
                return true;
        return false;
    }

    /// <summary>Is a thruster a head whose fuel comes only through its engine's Fuel Injector (its works need one)?</summary>
    public static bool InjectorFedHead(ConstructPartDef d) => (d.WorksNeeds & (1 << (int)WorksKind.Injector)) != 0;

    /// <summary>A head that takes its fuel through an injector: is a pipe into one (<see cref="CWorksHead.Fed"/>)? Called at the thrusters' rebuild.</summary>
    public static bool TrainFed(Construct c, int part) => c.WorksHeadOf(part) is var h and >= 0 && c.WorksHeads[h].Fed;

    private const float LineReach = 0.15f; // m: how far off a thruster's thrust line a works piece's axis may lie and still be in line with it
    private static readonly float LineCos = MathF.Cos(5f * MathF.PI / 180f); // (and within 5 degrees of it)

    /// <summary>A thruster's thrust line in the body's rest frame: through its <c>at</c>, along its exhaust.</summary>
    public static (Vector3 At, Vector3 Dir) ThrustLine(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        return (p.Pos + Rot24.Apply(p.Rot, d.ThrustAt), ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, d.ThrustDir)));
    }

    /// <summary>Does works piece <paramref name="piece"/>'s cylinder (its origin on the axis, the axis along its -z) lie on this line?</summary>
    public static bool OnLine(Construct c, int piece, Vector3 at, Vector3 dir)
    {
        ref var p = ref c.Parts[piece];
        var axis = Rot24.Apply(p.Rot, -Vector3.UnitZ);
        if (MathF.Abs(Vector3.Dot(axis, dir)) < LineCos)
            return false;
        var off = p.Pos - at;
        return (off - dir * Vector3.Dot(off, dir)).LengthSquared() <= LineReach * LineReach;
    }

    /// <summary>
    /// The drive piece bolted to <paramref name="mount"/> on thruster <paramref name="head"/>'s line and not yet in an engine, or -1;
    /// <paramref name="taken"/>: there was one on its line, but another bell took it first.
    /// </summary>
    private static int InLine(Construct c, int head, int mount, Span<short> asmOf, out bool taken)
    {
        taken = false;
        var (at, dir) = ThrustLine(c, head);
        // A bell welded straight to its train (an engine module on a Round Module Plug, Sean 2026-11-24): the piece welded to its back.
        // Only a bell fed through an injector looks there, so older bells find their pieces as they always did.
        for (var e = InjectorFedHead(c.Parts[head].D) ? c.FirstLink(head) : -1; e >= 0; e = c.NextLink(e))
        {
            var o = c.OtherEnd(e);
            if (!IsDrivePiece(c.Parts[o].D) || !OnLine(c, o, at, dir))
                continue;
            if (asmOf[o] >= 0)
            {
                taken = true;
                continue;
            }
            return o;
        }
        for (var e = c.FirstLink(mount); e >= 0; e = c.NextLink(e))
        {
            var o = c.OtherEnd(e);
            if (!IsDrivePiece(c.Parts[o].D) || !OnLine(c, o, at, dir))
                continue;
            if (asmOf[o] >= 0)
            {
                taken = true;
                continue;
            }
            return o;
        }
        return -1;
    }

    /// <summary>
    /// An engine socket's plate bolted onto a mount (a retrofit's plate on a pod's tail plate, module-sockets.md 11) passes the mount
    /// through when that mount has a drive piece on the head's thrust line: the drive's engine is behind that tail plate. Otherwise the
    /// plate is the mount itself (a Drive Core bolted to its back on the line, a plate on a Hull Mount beside it).
    /// </summary>
    private static int MountUnder(Construct c, int head, int plate)
    {
        var (at, dir) = ThrustLine(c, head);
        for (var e = c.FirstLink(plate); e >= 0; e = c.NextLink(e))
        {
            var o = c.OtherEnd(e);
            if (c.Parts[o].D.WorksKind != WorksKind.Mount)
                continue;
            for (var f = c.FirstLink(o); f >= 0; f = c.NextLink(f))
                if (c.OtherEnd(f) is var p && IsDrivePiece(c.Parts[p].D) && OnLine(c, p, at, dir))
                    return o;
        }
        return plate;
    }

    /// <summary>From a head down the joints it stands on (a gun's hinge and ring) to the first mount it touches, or -1.</summary>
    private static int FindMount(Construct c, int head, Span<ushort> queue)
    {
        int n = 0, at = 0;
        queue[n++] = (ushort)head;
        var depthEnd = n;
        for (var depth = 0; depth < MountSteps && at < n; depth++)
        {
            for (; at < depthEnd; at++)
                for (var e = c.FirstLink(queue[at]); e >= 0; e = c.NextLink(e))
                {
                    var o = c.OtherEnd(e);
                    var d = c.Parts[o].D;
                    if (d.WorksKind == WorksKind.Mount)
                        return o;
                    if (d.ModPart == ModPart.Plug && ModuleSockets.EngineMountOf(c, o) is var plate and >= 0)
                        return MountUnder(c, head, plate); // (a module's thruster on its plug: an engine socket's plate is its mount, module-sockets.md 2.2)
                    if (d.ModPart == ModPart.Plug && c.Parts[head].D.Role == PartRole.Thruster && InjectorFedHead(c.Parts[head].D))
                        return o; // (a bell on any other plug, a whole engine module: the plug is its mount and its train is welded to the bell, Sean 2026-11-24)
                    if (d.HasJoint && n < queue.Length && queue[..n].IndexOf((ushort)o) < 0)
                        queue[n++] = (ushort)o;
                }
            depthEnd = n;
        }
        return -1;
    }

    /// <summary>The hoists of assembly <paramref name="asm"/> that load rack <paramref name="rack"/>: each hoist loads the launcher nearest it (two drums on one mount, a hoist behind each), and a rack bolted to the hoist is its magazine, never its launcher.</summary>
    private static void Hoists(Construct c, int rack, short asm)
    {
        for (var k = 0; k < c.WorksPieceCount; k++)
        {
            if (c.WorksPieceAsm[k] != asm || c.Parts[c.WorksPieces[k]].D.WorksKind != WorksKind.Hoist)
                continue;
            var hoist = c.WorksPieces[k];
            var magazine = false;
            for (var e = c.FirstLink(hoist); e >= 0 && !magazine; e = c.NextLink(e))
                magazine = c.OtherEnd(e) == rack;
            if (magazine)
                continue;
            var had = -1;
            for (var h = 0; h < c.HoistCount && had < 0; h++)
                if (c.Hoists[h].Part == hoist)
                    had = h;
            if (had >= 0)
            {
                var at = c.Parts[hoist].Pos;
                if (Vector3.DistanceSquared(at, c.Parts[rack].Pos) < Vector3.DistanceSquared(at, c.Parts[c.Hoists[had].Launcher].Pos))
                    c.Hoists[had].Launcher = (ushort)rack;
                continue;
            }
            if (c.Hoists.Length == c.HoistCount)
                Array.Resize(ref c.Hoists, Math.Max(2, c.HoistCount * 2));
            c.Hoists[c.HoistCount++] = new CHoist { Part = hoist, Launcher = (ushort)rack };
        }
    }

    /// <summary>A thruster's engine: <paramref name="first"/> and every works piece bolted on behind it, through works pieces only (never a mount).</summary>
    private static short Stack(Construct c, int first, Span<short> asmOf, Span<ushort> queue)
    {
        var id = (short)c.WorksAsmCount++;
        int n = 0, at = 0;
        asmOf[first] = id;
        queue[n++] = (ushort)first;
        while (at < n)
        {
            var i = queue[at++];
            AddPiece(c, i, id);
            for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
            {
                var o = c.OtherEnd(e);
                var d = c.Parts[o].D;
                if (asmOf[o] >= 0 || !IsDrivePiece(d)) // (a drive's stack is drive pieces only: a gun's generator bolted on behind it stays the gun's)
                    continue;
                if (n >= Construct.MaxWorksAssembly)
                {
                    Caps.Hit("parts in one ship works assembly", Construct.MaxWorksAssembly, n + 1);
                    return id;
                }
                asmOf[o] = id;
                queue[n++] = (ushort)o;
            }
        }
        return id;
    }

    /// <summary>A gun's, an emitter's or a rack's assembly: every mount and works piece touching <paramref name="mount"/>, through each
    /// other (at most <see cref="Construct.MaxWorksAssembly"/>). A drive's pieces are never taken in: they belong to the bell in line.</summary>
    private static short Flood(Construct c, int mount, Span<short> asmOf, Span<ushort> queue)
    {
        var id = (short)c.WorksAsmCount++;
        int n = 0, at = 0;
        asmOf[mount] = id;
        queue[n++] = (ushort)mount;
        while (at < n)
        {
            var i = queue[at++];
            AddPiece(c, i, id);
            for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
            {
                var o = c.OtherEnd(e);
                var d = c.Parts[o].D;
                if (asmOf[o] >= 0 || !IsPiece(d) || IsDrivePiece(d))
                    continue;
                if (n >= Construct.MaxWorksAssembly)
                {
                    Caps.Hit("parts in one ship works assembly", Construct.MaxWorksAssembly, n + 1);
                    return id;
                }
                asmOf[o] = id;
                queue[n++] = (ushort)o;
            }
        }
        return id;
    }

    private static void AddPiece(Construct c, int part, short asm)
    {
        if (c.WorksPieces.Length <= c.WorksPieceCount)
        {
            var n = Math.Max(16, c.WorksPieces.Length * 2);
            Array.Resize(ref c.WorksPieces, n);
            Array.Resize(ref c.WorksPieceAsm, n);
        }
        c.WorksPieces[c.WorksPieceCount] = (ushort)part;
        c.WorksPieceAsm[c.WorksPieceCount++] = asm;
    }

    /// <summary>The Turret Ring part <paramref name="part"/> (a gun) stands on, down its joints, or -1.</summary>
    public static int RingUnder(Construct c, int part)
    {
        using var scratch = Scratch.Begin();
        var queue = scratch.Take<ushort>(Math.Min(c.PartCount, 64));
        var m = FindMount(c, part, queue);
        return m >= 0 && IsRing(c.Parts[m].D) ? m : -1;
    }

    /// <summary>What the working autoloaders in a ring's assembly serve (guns), or 0.</summary>
    public static float LoadersAt(Construct c, int ring)
    {
        var asm = ring >= 0 ? AsmOfPiece(c, ring) : -1;
        return asm >= 0 && asm < c.WorksAsmCount ? c.WorksServed[asm * KindCount + (int)WorksKind.Loader] : 0;
    }

    /// <summary>A thruster's cap and burn share (BuildThrusters; 1 and 1 when it is no head).</summary>
    public static void CapOf(Construct c, int part, out float cap, out float burn)
    {
        var h = c.WorksHeadOf(part);
        (cap, burn) = h >= 0 ? (c.WorksHeads[h].Cap, c.WorksHeads[h].Burn) : (1f, 1f);
    }

    /// <summary>The assembly a mount or piece was taken into, or -1.</summary>
    public static int AsmOfPiece(Construct c, int part)
    {
        for (var k = 0; k < c.WorksPieceCount; k++)
            if (c.WorksPieces[k] == part)
                return c.WorksPieceAsm[k];
        return -1;
    }

    // ---- What the player is told ---------------------------------------------------------------------------------

    /// <summary>A head's rating for people: "9.0 MN", "600 kN", "40 kW".</summary>
    public static string Rating(ConstructPartDef d) => d.Role == PartRole.Thruster ? Force(d.ThrustForce) : $"{NeedOf(d):0} kW";

    /// <summary>What a head's works serve, in its own unit: a thruster's newtons, else kW (a laser's beam).</summary>
    public static string Amount(ConstructPartDef head, float v) => head.Role == PartRole.Thruster ? Force(v) : $"{v:0} kW";

    public static string Force(float n) => n >= 1e6f ? (n / 1e6f).ToString("0.0", CultureInfo.InvariantCulture) + " MN" : (n / 1000f).ToString("0", CultureInfo.InvariantCulture) + " kN";

    /// <summary>Why a head is held back, or "" when it isn't: "no Drive Core behind it", "Fuel Pump 1 of 2 working".</summary>
    public static string Why(Construct c, in CWorksHead h)
    {
        var d = c.Parts[h.Part].D;
        if (d.WorksNeeds == 0 || h.Short == WorksKind.None)
            return "";
        if (h.Mount < 0)
            return "on no Hull Mount: its works can't reach it";
        if (h.Asm < 0)
            return d.Role != PartRole.Thruster ? $"no {KindPiece(h.Short)} behind it" : h.Taken ? "the engine in line behind it serves another bell" : "no engine in line behind this bell";
        var cell = h.Asm * KindCount + (int)h.Short;
        var all = c.WorksAll[cell];
        var have = c.WorksHave[cell];
        if (all == 0)
            return $"no {KindPiece(h.Short)} behind it";
        if (have < all)
            return $"{KindPiece(h.Short)} {have} of {all} working";
        return $"{KindPiece(h.Short)} too small: serves {Amount(d, c.WorksServed[cell])} of {Amount(d, c.WorksNeed[cell])}";
    }

    /// <summary>A head's state: "works 100%", "works 25%".</summary>
    private static string State(Construct c, in CWorksHead h) =>
        h.Outside ? $"works {h.Cap * 100:0}%, in a nacelle: serviced outside, through its hatch" : $"works {h.Cap * 100:0}%";

    /// <summary>The look line on a head, a mount or a works piece (any tool in hand): its works, its health and <paramref name="action"/> (the tool's hint).</summary>
    public static string Look(Construct c, int part, string? action = null)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var wrecked = p.Broken ? p.Health > 0 ? "broken down" : "wrecked" : p.Health < d.Health - 0.5f ? $"{p.Health:0}/{d.Health:0} hp" : "";
        if (IsHead(d))
        {
            var k = c.WorksHeadOf(part);
            if (k < 0)
                return SandboxKeys.Line(d.Name, SandboxKeys.Join(Rating(d), wrecked), null, action);
            ref var h = ref c.WorksHeads[k];
            var why = Why(c, h);
            if (why.Length > 0 && d.Role == PartRole.Thruster && h.Cap <= ShipWorks.Current.BareShare + 1e-3f)
                why += ": running at a quarter";
            if (why.Length == 0 && InjectorFedHead(d) && !h.Fed)
                why = "no fuel pipe into its Fuel Injector: it runs on the injector's buffer, then stops";
            if (why.Length == 0 && h.Stray >= 0)
                why = StrayWhy(c, h);
            return SandboxKeys.Line(d.Name, SandboxKeys.Join(Rating(d), State(c, h), wrecked), why, action);
        }
        if (IsRing(d))
            return RingLook(c, part, wrecked, action);
        var asm = AsmOfPiece(c, part);
        if (asm < 0 && d.WorksKind == WorksKind.Mount && HeadsThrough(c, part) is var through and > 0)
            return SandboxKeys.Line(d.Name, SandboxKeys.Join(through == 1 ? "mount for 1 drive" : $"mount for {through} drives", wrecked), null, action);
        if (asm < 0 && (d.WorksKind == WorksKind.Train || d.WorksKind == WorksKind.Injector))
            return SandboxKeys.Line(d.Name, SandboxKeys.Join(d.WorksKind == WorksKind.Train ? "" : $"serves {PieceServes(d)}", wrecked), "not in an engine: a train behind a drive's bell takes hot pieces first, then feed, then supply, and at most 16 (the bell's look says what it left out)", action);
        if (asm < 0 && IsDrivePiece(d))
            return SandboxKeys.Line(d.Name, SandboxKeys.Join($"serves {PieceServes(d)}", wrecked), "in line with no bell: bolt it to a Hull Mount right behind a drive's nozzle, on its axis", action);
        if (d.WorksKind == WorksKind.Loader)
            return LoaderLook(c, part, asm, wrecked, action);
        if (d.WorksKind == WorksKind.Hoist)
            return ConstructHoists.Look(c, part, wrecked, action);
        if (asm < 0)
            return SandboxKeys.Line(d.Name, SandboxKeys.Join(d.WorksKind == WorksKind.Mount ? "" : $"serves {PieceServes(d)}", wrecked), d.WorksKind == WorksKind.Mount ? "no head through it" : "bolt it to a Hull Mount, or to works on one", action);
        var heads = 0;
        var least = 1f;
        for (var k = 0; k < c.WorksHeadCount; k++)
            if (c.WorksHeads[k].Asm == asm)
            {
                heads++;
                least = MathF.Min(least, c.WorksHeads[k].Cap);
            }
        var of = heads == 0 ? "" : heads == 1 ? "works for 1 head" : $"works for {heads} heads";
        if (d.WorksKind == WorksKind.Mount)
            return SandboxKeys.Line(d.Name, SandboxKeys.Join(heads == 0 ? "" : $"mount, {of}", wrecked), heads == 0 ? "no head through it" : null, action);
        if (d.WorksKind == WorksKind.Train)
            return SandboxKeys.Line(d.Name, SandboxKeys.Join(TrainAdds(d), of, wrecked), null, action);
        var cell = asm * KindCount + (int)d.WorksKind;
        var need = c.WorksNeed[cell];
        var load = need <= 0 ? "nothing needs it" : $"{MathF.Min(1, c.WorksServed[cell] / need) * 100:0}% of what they need";
        return SandboxKeys.Line(d.Name, SandboxKeys.Join($"serves {PieceServes(d)}", of, wrecked), need <= 0 ? load : heads > 0 && least < 0.999f ? load : null, action);
    }

    /// <summary>The thrusters that go through mount <paramref name="mount"/> (each with its own engine behind it).</summary>
    private static int HeadsThrough(Construct c, int mount)
    {
        var n = 0;
        for (var k = 0; k < c.WorksHeadCount; k++)
            if (c.WorksHeads[k].Mount == mount && c.Parts[c.WorksHeads[k].Part].D.Role == PartRole.Thruster)
                n++;
        return n;
    }

    /// <summary>The head a works piece serves (its assembly's first), or -1.</summary>
    public static int HeadOfPiece(Construct c, int part)
    {
        var asm = AsmOfPiece(c, part);
        if (asm < 0)
            return -1;
        for (var k = 0; k < c.WorksHeadCount; k++)
            if (c.WorksHeads[k].Asm == asm)
                return c.WorksHeads[k].Part;
        return -1;
    }

    /// <summary>Is this works piece in a nacelle (its drive is serviced outside: it is mended through the nacelle's hatch, never at the Engineer's Panel)?</summary>
    public static bool PieceOutside(Construct c, int part)
    {
        var asm = AsmOfPiece(c, part);
        if (asm < 0)
            return false;
        for (var k = 0; k < c.WorksHeadCount; k++)
            if (c.WorksHeads[k].Asm == asm)
                return c.WorksHeads[k].Outside;
        return false;
    }

    /// <summary>A Turret Ring's look: what feeds it, the guns on it and how well its autoloaders serve them.</summary>
    private static string RingLook(Construct c, int ring, string wrecked, string? action)
    {
        var d = c.Parts[ring].D;
        int guns = 0;
        var loaded = 0f;
        for (var k = 0; k < c.GunCount; k++)
            if (c.Guns[k].Ring == ring)
            {
                guns++;
                loaded = MathF.Max(loaded, c.Guns[k].Loaded);
            }
        var on = guns == 0 ? "" : guns == 1 ? "1 gun on it" : $"{guns} guns on it";
        var load = loaded <= 0 ? "" : loaded >= 0.999f ? "autoloaded" : $"autoloaded {loaded * 100:0}%";
        var feed = ConstructAmmo.RingFeed(c, ring);
        var problem = feed < 0 ? "no ammo feed into its Ammo in" : null;
        return SandboxKeys.Line(d.Name, SandboxKeys.Join(on, feed >= 0 ? "fed" : "", load, wrecked), problem, action);
    }

    /// <summary>An Autoloader's look: the guns it loads on its ring, or why it loads none.</summary>
    private static string LoaderLook(Construct c, int part, int asm, string wrecked, string? action)
    {
        var d = c.Parts[part].D;
        int guns = 0, fed = 0;
        var anyRing = false;
        for (var k = 0; k < c.GunCount; k++)
            if (c.Guns[k].Ring >= 0 && asm >= 0 && AsmOfPiece(c, c.Guns[k].Ring) == asm)
            {
                guns++;
                if (c.Guns[k].RingFed)
                    fed++;
            }
        for (var k = 0; k < c.WorksPieceCount && !anyRing; k++)
            anyRing = c.WorksPieceAsm[k] == asm && IsRing(c.Parts[c.WorksPieces[k]].D);
        var serves = $"serves {PieceServes(d)}";
        if (asm < 0 || !anyRing)
            return SandboxKeys.Line(d.Name, SandboxKeys.Join(serves, wrecked), "bolt it to a Turret Ring's trunk", action);
        if (guns == 0)
            return SandboxKeys.Line(d.Name, SandboxKeys.Join(serves, wrecked), "no gun stands on its ring", action);
        if (fed == 0)
            return SandboxKeys.Line(d.Name, SandboxKeys.Join(serves, wrecked), "its ring has no ammo feed: it loads nothing", action);
        var served = c.WorksServed[asm * KindCount + (int)WorksKind.Loader];
        var of = fed == 1 ? "loads 1 gun" : $"loads {fed} guns";
        return SandboxKeys.Line(d.Name, SandboxKeys.Join(serves, of, wrecked), served < fed ? $"{MathF.Min(1, served / fed) * 100:0}% of what they need" : null, action);
    }

    private static string PieceServes(ConstructPartDef d) => d.WorksKind switch
    {
        WorksKind.Lasergen => $"{d.WorksServes:0} kW",
        WorksKind.Hoist => $"{d.WorksServes:0} kg",
        WorksKind.Loader => d.WorksServes == 1 ? "1 gun" : $"{d.WorksServes:0} guns",
        _ => Force(d.WorksServes),
    };

    /// <summary>What a function piece adds, for its look line: "+15 s of buffer", "10% less fuel".</summary>
    private static string TrainAdds(ConstructPartDef d) => d.TrainFn switch
    {
        TrainFunction.Buffer => $"+{ShipWorks.Current.FuelBufferSeconds:0} s of buffer",
        TrainFunction.Jacket => $"{ShipWorks.Current.JacketSaving * 100:0}% less fuel",
        TrainFunction.Igniter => "lights the drive at once",
        TrainFunction.Sensor => "level, buffer, health and heat out",
        TrainFunction.Spacer => "spacer",
        _ => "",
    };

    /// <summary>What decides <see cref="Look"/> beyond the part (the look line's cache key).</summary>
    public static long LookKey(Construct c, int part)
    {
        var key = (long)c.RebuildStamp;
        var k = c.WorksHeadOf(part);
        if (k >= 0)
            key = key * 31 + (int)(c.WorksHeads[k].Cap * 1000) + (c.WorksHeads[k].Fed ? 7 : 0);
        if (c.Parts[part].D.WorksKind == WorksKind.Hoist)
            key = key * 31 + (c.Parts[part].Powered ? 1 : 0) + (c.PowerOn ? 2 : 0); // (a hoist says when it has no power)
        return key;
    }

    /// <summary>
    /// The Engineer's Panel's report: every drive with an engine, its share and why ("" when the ship has none). One line: the
    /// drives at full are counted, the worst one is named; a drive in a nacelle is reported, and mended through its hatch.
    /// </summary>
    public static string PanelLook(Construct c)
    {
        if (c.WorksHeadCount == 0)
            return "";
        int full = 0, all = 0, outside = 0, worst = -1;
        for (var k = 0; k < c.WorksHeadCount; k++)
        {
            ref var h = ref c.WorksHeads[k];
            var d = c.Parts[h.Part].D;
            if (d.Role != PartRole.Thruster)
                continue;
            all++;
            if (h.Outside)
                outside++;
            if (h.Cap >= 0.999f)
                full++;
            else if (worst < 0 || h.Cap < c.WorksHeads[worst].Cap)
                worst = k;
        }
        if (all == 0)
            return "";
        var sb = new StringBuilder();
        sb.Append(all == 1 ? "1 drive" : $"{all} drives").Append($", {full} of {all} at full");
        if (outside > 0)
            sb.Append(outside == all ? " (in nacelles: serviced outside)" : $" ({outside} in nacelles: serviced outside)");
        if (worst >= 0)
        {
            ref var h = ref c.WorksHeads[worst];
            var d = c.Parts[h.Part].D;
            sb.Append($"; the {d.Name} at {h.Cap * 100:0}%: {Why(c, h)}");
            if (h.Outside)
                sb.Append(" - mend it through the nacelle's hatch, outside");
        }
        return sb.ToString();
    }

    /// <summary>The panel report's cache key.</summary>
    public static long PanelKey(Construct c)
    {
        var key = (long)c.RebuildStamp;
        for (var k = 0; k < c.WorksHeadCount; k++)
            key = key * 31 + (int)(c.WorksHeads[k].Cap * 1000) + (c.Parts[c.WorksHeads[k].Part].Broken ? 7 : 0);
        return key;
    }

    /// <summary>
    /// <c>--check-vehicle</c>'s <c>works:</c> lines, one a head: its kinds, what was found, its cap. A head whose works don't
    /// carry it whole (a thruster at its floor, a gun with no generator) is a failure, and so are a drive piece in line with no
    /// bell and a main drive (a thruster over jetMost pushing the craft forward) with no engine.
    /// </summary>
    public static void Report(Construct c, List<string> notes, List<string> failures)
    {
        for (var k = 0; k < c.WorksHeadCount; k++)
        {
            ref var h = ref c.WorksHeads[k];
            var d = c.Parts[h.Part].D;
            var sb = new StringBuilder($"works: {d.Name} (part {h.Part}), {Rating(d)}{(h.Outside ? ", in a nacelle" : "")}: needs ");
            var first = true;
            for (var kind = 2; kind < KindCount; kind++)
            {
                if (!Needs(d, (WorksKind)kind))
                    continue;
                sb.Append(first ? "" : ", ").Append(KindNames[kind]);
                first = false;
                if (h.Asm >= 0)
                {
                    var cell = h.Asm * KindCount + kind;
                    sb.Append($" ({c.WorksHave[cell]} of {c.WorksAll[cell]} working, {Amount(d, c.WorksServed[cell])} for {Amount(d, c.WorksNeed[cell])})");
                }
            }
            if (h.Asm >= 0)
            {
                sb.Append("; its pieces:");
                for (var j = 0; j < c.WorksPieceCount; j++)
                    if (c.WorksPieceAsm[j] == h.Asm && c.Parts[c.WorksPieces[j]].D.WorksKind != WorksKind.Mount)
                        sb.Append($" {c.Parts[c.WorksPieces[j]].D.Name} (part {c.WorksPieces[j]})");
            }
            sb.Append($"; cap {h.Cap * 100:0}%");
            if (InjectorFedHead(d))
                sb.Append(h.Fed ? "; fed by a pipe into its Fuel Injector" : "; NOT FED: no pipe from a working tank into its Fuel Injector (it runs on the buffer, then stops)");
            if (InjectorFedHead(d))
                sb.Append(CultureInfo.InvariantCulture, $"; buffer {h.BufferMax:0} s, burn {h.Burn * 100:0}%, {(h.Igniter ? "lights at once" : $"lights in {ShipWorks.Current.LightSeconds:0} s (no Igniter)")}");
            if (h.Stray >= 0)
                sb.Append($"; LEFT OUT: {StrayWhy(c, h)}");
            if (h.Cap < 0.999f)
            {
                sb.Append(": ").Append(Why(c, h));
                failures.Add(sb.ToString());
            }
            else
                notes.Add(sb.ToString());
        }
        foreach (var i in c.Specials)
            if (IsDrivePiece(c.Parts[i].D) && AsmOfPiece(c, i) < 0)
                failures.Add($"works: {c.Parts[i].D.Name} (part {i}) is in line with no bell: no drive's nozzle is on its axis, through the mount it is bolted to");
        for (var k = 0; k < c.HoistCount; k++)
        {
            var h = c.Hoists[k];
            var line = $"works: {c.DefOf(h.Part).Name} (part {h.Part}) loads the {c.DefOf(h.Launcher).Name} (part {h.Launcher}), {ConstructHoists.InMagazine(c, h)} in its magazine";
            notes.Add(ConstructHoists.Next(c, h, out var w, out _, out _, out _) ? line + $", {ConstructHoists.Seconds(c.DefOf(w)):0.0} s a load" : line + ", nothing to move now");
        }
        var most = ShipWorks.Current.JetMost;
        var cos = MathF.Cos(30f * MathF.PI / 180f);
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            var d = c.Parts[c.Thrusters[k].Part].D;
            if (d.Group == "Ship" && d.ThrustForce > most && d.WorksNeeds == 0 && c.Thrusters[k].MixForward >= cos) // (ships only, as the part kit's old refusal was: an aircraft's jet has no works)
                failures.Add($"works: {d.Name} (part {c.Thrusters[k].Part}), {Rating(d)}, is a main drive with no engine: over {Force(most)} pushing the craft forward, it needs works (Works.Head) and its own engine behind its bell");
        }
        // Guns on Turret Rings: what feeds them through the ring, and their autoloaders (ship-works.md 5.2, 5.3).
        for (var k = 0; k < c.GunCount; k++)
        {
            ref var g = ref c.Guns[k];
            if (g.Ring < 0 || g.Part >= c.PartCount || g.NoAmmo)
                continue; // (a laser on a ring needs no feed: its generators are its works, reported with the heads)
            var line = $"works: {c.Parts[g.Part].D.Name} (part {g.Part}) on {c.Parts[g.Ring].D.Name} (part {g.Ring})";
            if (!g.RingFed)
            {
                notes.Add($"{line}: no feed into the ring's Ammo in");
                continue;
            }
            var fed = ConstructAmmo.FedFrom(null, c, k, out var sc, out var src) && sc == c && src >= 0 && src < c.PartCount;
            line += fed ? $": fed from {c.Parts[src].D.Name} (part {src})" : ": its ring's feed reaches no store";
            line += g.Loaded > 0 ? $", autoloaded {g.Loaded * 100:0}%, reload {g.ReloadTicks / (float)World.TickRate:0.##} s" : $", reload {g.ReloadTicks / (float)World.TickRate:0.##} s";
            (fed ? notes : failures).Add(line);
        }
    }
}
