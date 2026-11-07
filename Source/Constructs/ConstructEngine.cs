using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a modular engine piece is (docs/sandbox/CONSTRUCTS.md, "Modular engines").</summary>
public enum EnginePiece : byte
{
    None,
    Output, // the flywheel: the part the drivetrain sees as the engine
    Crank, // a crank section: sections end to end make the crank
    Cylinder, // on a crank's side: each adds power; the sides used make the layout
    Fuel, // a carburettor or an injector rail on a head: feeds its bank
    Boost, // a turbo or a supercharger on a head or a fuel system: more from its bank
    Starter, // what a Large or Capital engine needs to turn over
    Exhaust, // an outlet: a smokestack stands on it or takes a pipe from it
    Stack, // a length of smokestack: the smoke leaves the top one
}

/// <summary>Why a modular engine doesn't run, or runs on less than it could.</summary>
public enum EngineFault : byte
{
    None,
    NoCrank,
    NoCylinders,
    MixedSizes,
    CylinderOnEnd,
    NoFuelSystem, // on a bank (the first one with none)
    NoOutput,
    OutputWrecked,
    NoStarter,
    NoTank,
    AllWrecked,
}

/// <summary>
/// The rules (Constructs/ModularEngine.json, written by tools/constructs/engine_parts.py): each size's power a cylinder and
/// its rpm, and the simple sums' numbers. The defaults match the file.
/// </summary>
public sealed class EngineRules
{
    public const string Format = "call-of-slop-modular-engine/1";

    public readonly float[] Kw = [40, 800, 16000, 320000], Idle = [900, 600, 300, 150], Peak = [5000, 2800, 1400, 700], Max = [7000, 4000, 2000, 1000], Scale = [1, 4, 16, 64];
    public readonly bool[] NeedsStarter = [false, false, true, true];
    public float BurnPerKw = 0.00044f, MisfireBelow = 0.5f, MisfirePower = 0.5f, ThirstyAt = 1.15f, FrugalAt = 0.85f;
    public string Problem = ""; // why the file was refused ("" when it loaded; the defaults are used otherwise)

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "ModularEngine.json");

    private static EngineRules? _current;

    /// <summary>The rules in use (loaded once from the file; the defaults above when it is missing or refused).</summary>
    public static EngineRules Current => _current ??= Load(FilePath);

    /// <summary>Sets the rules in use (the self-test; null: load the file again).</summary>
    public static void Use(EngineRules? rules) => _current = rules;

    public static EngineRules Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new EngineRules { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    /// <summary>Reads a rules file; a bad one gives the defaults with <see cref="Problem"/> saying why.</summary>
    public static EngineRules Parse(string json)
    {
        var r = new EngineRules();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("the file is not a JSON object");
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                switch (p.Name)
                {
                    case "format" or "about": break;
                    case "burnPerKw": r.BurnPerKw = Num(p.Value, p.Name, 0, 1); break;
                    case "misfireBelow": r.MisfireBelow = Num(p.Value, p.Name, 0, 1); break;
                    case "misfirePower": r.MisfirePower = Num(p.Value, p.Name, 0, 1); break;
                    case "thirstyAt": r.ThirstyAt = Num(p.Value, p.Name, 1, 10); break;
                    case "frugalAt": r.FrugalAt = Num(p.Value, p.Name, 0, 1); break;
                    case "sizes":
                        if (p.Value.ValueKind != JsonValueKind.Array)
                            throw new FormatException("\"sizes\" is a list");
                        foreach (var s in p.Value.EnumerateArray())
                        {
                            var i = s.TryGetProperty("name", out var nm) ? Array.IndexOf(ConstructEngine.SizeNames, nm.GetString()) : -1;
                            if (i < 0)
                                throw new FormatException($"a size's \"name\" is one of {string.Join(", ", ConstructEngine.SizeNames)}");
                            foreach (var q in s.EnumerateObject())
                                switch (q.Name)
                                {
                                    case "name": break;
                                    case "scale": r.Scale[i] = Num(q.Value, q.Name, 0.1f, 1000); break;
                                    case "kw": r.Kw[i] = Num(q.Value, q.Name, 0.1f, 1e7f); break;
                                    case "idleRpm": r.Idle[i] = Num(q.Value, q.Name, 10, 60000); break;
                                    case "peakRpm": r.Peak[i] = Num(q.Value, q.Name, 10, 60000); break;
                                    case "maxRpm": r.Max[i] = Num(q.Value, q.Name, 10, 60000); break;
                                    case "starter": r.NeedsStarter[i] = q.Value.ValueKind == JsonValueKind.True; break;
                                    default: throw new FormatException($"unknown size key \"{q.Name}\"");
                                }
                            if (r.Idle[i] >= r.Peak[i] || r.Peak[i] > r.Max[i])
                                throw new FormatException($"{ConstructEngine.SizeNames[i]}: idle, peak and limiter rpm go up in that order");
                        }
                        break;
                    default: throw new FormatException($"unknown key \"{p.Name}\"");
                }
            return r;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new EngineRules { Problem = ex.Message };
        }
    }

    private static float Num(JsonElement e, string what, float min, float max)
    {
        if (e.ValueKind != JsonValueKind.Number || e.GetSingle() is var v && (v < min || v > max))
            throw new FormatException($"\"{what}\" is a number from {min} to {max}");
        return e.GetSingle();
    }
}

/// <summary>One modular engine on a construct: worked out with the caches, never stored, the same on every peer.</summary>
public sealed class ModEngine
{
    public const int MaxEmits = 4;
    public int Output = -1; // the flywheel
    public byte Size; // the crank's
    public int Cranks, Cylinders, Wrecked, Misfiring, Banks, Boxer; // Boxer: two opposite banks
    public int Fed; // banks with a fuel system
    public float Kw, Burn, Spool;
    public bool HasStarter, Fuelled, HasEngine; // HasEngine: it has a crank or a cylinder (a lone stack isn't an engine)
    public EngineFault Fault; // the first thing wrong
    public int FaultBank; // NoFuelSystem: which (1 up)
    public readonly int[] Emit = new int[MaxEmits]; // where the smoke leaves
    public int EmitCount;

    /// <summary>It can run (maybe on less than it was built for: <see cref="Fault"/> says why).</summary>
    public bool Complete => Output >= 0 && Kw > 0 && Fault is EngineFault.None or EngineFault.NoFuelSystem or EngineFault.NoTank;
}

/// <summary>
/// Modular engines (docs/sandbox/CONSTRUCTS.md, "Modular engines"). Pieces that touch (socket links, a seam, a freeform weld's
/// bead, on one rigid piece) are one engine; the sums are simple: power is each cylinder's kW times its bank's fuel system
/// and booster. <see cref="Build"/> runs with the caches, derives everything from the parts and links (so nothing goes on
/// the wire and every peer agrees), allocates nothing past the first engine on a construct, and leaves one set of numbers
/// (<see cref="Construct.EngineDef"/>) for <see cref="ConstructDrive.Tick"/>: no per-tick work per cylinder.
/// </summary>
public static class ConstructEngine
{
    public const int MaxEngines = 32, MaxBanks = 8; // modular engines a construct (was 8; EngineOf is a byte, the bank scratch is 256 slots on the stack); past this the pieces are logged and left out

    public static readonly string[] SizeNames = ["small", "medium", "large", "capital"];

    private static readonly string[] BankText = ["no fuel system: fit a carburettor or injector rail on a cylinder", "no fuel system on bank 1", "no fuel system on bank 2", "no fuel system on bank 3", "no fuel system on bank 4", "no fuel system on bank 5", "no fuel system on bank 6", "no fuel system on bank 7", "no fuel system on bank 8"];

    private static bool Edge(Construct c, int k, out int a, out int b)
    {
        (a, b) = (-1, -1);
        if (k < c.LinkCount)
            (a, b) = (c.Links[k].A, c.Links[k].B);
        else if (k < c.LinkCount + c.SeamCount)
            (a, b) = (c.SeamLinks[k - c.LinkCount].A, c.SeamLinks[k - c.LinkCount].B); // (sockets that meet across a freeform seam)
        else if (c.Joints[k - c.LinkCount - c.SeamCount] is { Freeform: true, Cut: false } j)
            (a, b) = (j.Hinge, j.Child); // (a freeform weld's bead)
        return a >= 0 && a < c.PartCount && b >= 0 && b < c.PartCount;
    }

    private static int Find(Span<int> up, int i)
    {
        while (up[i] != i)
            i = up[i] = up[up[i]];
        return i;
    }

    /// <summary>Which pieces make which engine and what it adds up to (with the caches, before the drivetrain reads it).</summary>
    public static void Build(Construct c)
    {
        var n = c.PartCount;
        c.ModCount = 0;
        c.ModActive = -1;
        var any = false;
        Array.Clear(c.EngineOf, 0, n);
        Array.Clear(c.EngineSlot, 0, n);
        foreach (var i in c.Specials) // (engine pieces are special parts)
            any |= c.Parts[i].D.Piece != EnginePiece.None;
        if (!any)
            return;
        c.ModEngines ??= new ModEngine[MaxEngines];
        var rules = EngineRules.Current;
        var edges = c.LinkCount + c.SeamCount + c.JointCount;
        // Pieces that touch, working and on one rigid piece, are one engine.
        using var scratch = Scratch.Begin(); // (part-sized spans: Scratch.cs)
        Span<int> up = scratch.Take<int>(n); // (was short: an index a part, past 32,767 parts it would wrap)
        for (var i = 0; i < n; i++)
            up[i] = i;
        for (var k = 0; k < edges; k++)
        {
            if (!Edge(c, k, out var a, out var b) || !Joins(c, a) || !Joins(c, b) || c.MoveSeg(c.Parts[a].Seg) != c.MoveSeg(c.Parts[b].Seg))
                continue;
            int ra = Find(up, a), rb = Find(up, b);
            if (ra != rb)
                up[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
        for (var i = 0; i < n; i++)
        {
            if (!Joins(c, i))
                continue;
            var root = Find(up, i);
            if (c.EngineOf[root] == 0 && c.ModCount >= MaxEngines && Joins(c, root))
                Caps.Hit("modular engines a construct", MaxEngines);
            if (c.EngineOf[root] == 0 && c.ModCount < MaxEngines)
            {
                var m = c.ModEngines[c.ModCount] ??= new ModEngine();
                Reset(m);
                c.EngineOf[root] = (byte)++c.ModCount;
            }
            c.EngineOf[i] = c.EngineOf[root];
            if (c.EngineOf[i] == 0)
                continue;
            ref var p = ref c.Parts[i];
            var e = c.ModEngines[c.EngineOf[i] - 1];
            e.HasStarter |= p.D.Piece == EnginePiece.Starter;
            if (p.D.Piece == EnginePiece.Crank && e.Cranks++ == 0)
                e.Size = p.D.PieceSize;
            e.HasEngine |= p.D.Piece is EnginePiece.Crank or EnginePiece.Cylinder or EnginePiece.Output or EnginePiece.Fuel or EnginePiece.Boost;
        }
        if (c.ModCount == 0)
            return;
        // On the crank: the cylinders (each on a bank, by which way it points; a wrecked one keeps its place and gives
        // nothing), the flywheel.
        Span<sbyte> bank = scratch.Take<sbyte>(n);
        bank.Fill(-1);
        var bankSlots = MaxEngines * MaxBanks;
        Span<Vector3> bankDir = stackalloc Vector3[bankSlots];
        Span<float> fuelMul = stackalloc float[bankSlots], fuelThirst = stackalloc float[bankSlots], boostMul = stackalloc float[bankSlots], boostThirst = stackalloc float[bankSlots];
        fuelMul.Clear();
        boostMul.Fill(1f);
        boostThirst.Fill(1f);
        fuelThirst.Fill(1f);
        for (var k = 0; k < edges; k++)
        {
            if (!Edge(c, k, out var a, out var b) || c.MoveSeg(c.Parts[a].Seg) != c.MoveSeg(c.Parts[b].Seg))
                continue;
            if (c.Parts[b].D.Piece == EnginePiece.Crank)
                (a, b) = (b, a);
            var pa = c.Parts[a].D;
            var pb = c.Parts[b].D;
            if (pa.Piece != EnginePiece.Crank || c.Parts[a].Broken || c.EngineOf[a] == 0)
                continue;
            var e = c.ModEngines[c.EngineOf[a] - 1];
            if (pb.Piece == EnginePiece.Output)
            {
                if (c.Parts[b].Broken)
                {
                    if (e.Output < 0)
                        e.Fault = EngineFault.OutputWrecked;
                }
                else if (pb.PieceSize != e.Size)
                    e.Fault = EngineFault.MixedSizes;
                else if (e.Output < 0)
                {
                    e.Output = b;
                    if (e.Fault == EngineFault.OutputWrecked)
                        e.Fault = EngineFault.None;
                }
                continue;
            }
            if (pb.Piece != EnginePiece.Cylinder)
                continue;
            if (pb.PieceSize != pa.PieceSize)
            {
                e.Fault = EngineFault.MixedSizes;
                continue;
            }
            var axis = c.DirToRest(c.Parts[a].Seg, Rot24.Apply(c.Parts[a].Rot, Vector3.UnitZ));
            var out1 = c.ToRest(c.Parts[b].Seg, c.Parts[b].Pos) - c.ToRest(c.Parts[a].Seg, c.Parts[a].Pos);
            out1 -= axis * Vector3.Dot(axis, out1);
            if (out1.LengthSquared() < 1e-4f)
            {
                e.Fault = EngineFault.CylinderOnEnd;
                continue;
            }
            out1 = Vector3.Normalize(out1);
            var er = c.EngineOf[a] - 1;
            var be = -1;
            for (var q = 0; q < e.Banks && be < 0; q++)
                if (Vector3.Dot(bankDir[er * MaxBanks + q], out1) > 0.85f)
                    be = q;
            if (be < 0 && e.Banks < MaxBanks)
            {
                be = e.Banks++;
                bankDir[er * MaxBanks + be] = out1;
            }
            if (be < 0)
                continue;
            bank[b] = (sbyte)be;
            c.EngineOf[b] = c.EngineOf[a]; // (a wrecked cylinder stays part of the engine: its piston stands still)
            c.EngineSlot[b] = (byte)Math.Min(255, e.Cylinders);
            e.Cylinders++;
            if (c.Parts[b].Broken)
                e.Wrecked++;
        }
        // The fuel systems and boosters: on a cylinder of the bank, or on each other.
        for (var pass = 0; pass < 3; pass++)
            for (var k = 0; k < edges; k++)
            {
                if (!Edge(c, k, out var a, out var b) || bank[a] < 0 == bank[b] < 0)
                    continue;
                if (bank[a] < 0)
                    (a, b) = (b, a);
                var d = c.Parts[b].D;
                if (d.Piece is not (EnginePiece.Fuel or EnginePiece.Boost or EnginePiece.Exhaust) || c.Parts[b].Broken || c.EngineOf[b] != c.EngineOf[a] || c.EngineOf[b] == 0)
                    continue;
                var e = c.ModEngines[c.EngineOf[b] - 1];
                if (d.PieceSize != e.Size)
                {
                    e.Fault = e.Fault == EngineFault.None ? EngineFault.MixedSizes : e.Fault;
                    continue;
                }
                bank[b] = bank[a];
                var at = (c.EngineOf[b] - 1) * MaxBanks + bank[a];
                if (d.Piece == EnginePiece.Fuel && d.PiecePower > fuelMul[at])
                    (fuelMul[at], fuelThirst[at]) = (d.PiecePower, d.PieceThirst);
                else if (d.Piece == EnginePiece.Boost && d.PiecePower > boostMul[at])
                {
                    (boostMul[at], boostThirst[at]) = (d.PiecePower, d.PieceThirst);
                    e.Spool = MathF.Max(e.Spool, d.PieceSpool);
                }
            }
        // The sums.
        for (var i = 0; i < n; i++)
        {
            var d = c.Parts[i].D;
            if (d.Piece != EnginePiece.Cylinder || bank[i] < 0)
                continue;
            var e = c.ModEngines[c.EngineOf[i] - 1];
            var at = (c.EngineOf[i] - 1) * MaxBanks + bank[i];
            var share = Share(c, i, rules);
            if (share < 1 && share > 0)
                e.Misfiring++;
            if (fuelMul[at] <= 0)
                continue;
            var kw = rules.Kw[e.Size] * share * fuelMul[at] * boostMul[at];
            e.Kw += kw;
            e.Burn += kw * fuelThirst[at] * boostThirst[at] * rules.BurnPerKw;
        }
        for (var k = 0; k < c.ModCount; k++)
        {
            var e = c.ModEngines[k];
            for (var q = 0; q < e.Banks; q++)
            {
                if (fuelMul[k * MaxBanks + q] > 0)
                    e.Fed++;
                else if (e.FaultBank == 0)
                    e.FaultBank = q + 1;
            }
            if (e.Banks == 2 && Vector3.Dot(bankDir[k * MaxBanks], bankDir[k * MaxBanks + 1]) < -0.7f)
                e.Boxer = 1;
            if (e.Fault == EngineFault.None)
                e.Fault = !e.HasEngine ? EngineFault.None
                    : e.Cranks == 0 ? EngineFault.NoCrank
                    : e.Cylinders == 0 ? EngineFault.NoCylinders
                    : e.Wrecked >= e.Cylinders ? EngineFault.AllWrecked
                    : e.Fed == 0 ? EngineFault.NoFuelSystem
                    : e.Output < 0 ? EngineFault.NoOutput
                    : rules.NeedsStarter[e.Size] && !e.HasStarter ? EngineFault.NoStarter
                    : e.Fed < e.Banks ? EngineFault.NoFuelSystem
                    : EngineFault.None;
        }
        Emits(c, bank);
    }

    private static bool Joins(Construct c, int i) => c.Parts[i].D.Piece != EnginePiece.None && c.Parts[i].Working;

    private static void Reset(ModEngine e)
    {
        e.Output = -1;
        e.Size = 0;
        e.Cranks = e.Cylinders = e.Wrecked = e.Misfiring = e.Banks = e.Boxer = e.Fed = e.EmitCount = e.FaultBank = 0;
        e.Kw = e.Burn = e.Spool = 0;
        e.HasStarter = e.Fuelled = e.HasEngine = false;
        e.Fault = EngineFault.None;
    }

    /// <summary>What share of its power a cylinder gives: all of it, the misfire's share when it is shot up, nothing wrecked.</summary>
    public static float Share(Construct c, int part, EngineRules rules)
    {
        ref var p = ref c.Parts[part];
        return p.Broken ? 0f : p.Health < p.D.Health * rules.MisfireBelow ? rules.MisfirePower : 1f;
    }

    /// <summary>Where each engine's smoke leaves: the tops of the stacks it feeds (on it, or at the end of a pipe from an outlet), else its outlets, else one head a bank.</summary>
    private static void Emits(Construct c, Span<sbyte> bank)
    {
        var n = c.PartCount;
        for (var i = 0; i < n; i++)
        {
            var d = c.Parts[i].D;
            if (d.Piece != EnginePiece.Stack || c.Parts[i].Broken || c.SocketUsed(i, d.SmokeSocket))
                continue;
            var owner = c.EngineOf[i] - 1;
            var fed = owner >= 0 && c.ModEngines![owner].Cylinders > 0;
            for (var k = 0; k < c.RouteCount && !fed; k++)
            {
                ref var r = ref c.Routes[k];
                if (r.Type == RouteType.Pipe && r.Source < n && r.To < n && c.EngineOf[r.To] == c.EngineOf[i] && c.EngineOf[i] != 0 && c.Parts[r.Source].D.Piece == EnginePiece.Exhaust && c.EngineOf[r.Source] != 0)
                {
                    owner = c.EngineOf[r.Source] - 1;
                    fed = true;
                }
            }
            if (fed)
                AddEmit(c.ModEngines![owner], i);
        }
        for (var want = 0; want < 2; want++)
            for (var i = 0; i < n; i++)
            {
                var piece = c.Parts[i].D.Piece;
                if (c.EngineOf[i] == 0 || c.Parts[i].Broken || bank[i] < 0 || piece != (want == 0 ? EnginePiece.Exhaust : EnginePiece.Cylinder))
                    continue;
                var e = c.ModEngines![c.EngineOf[i] - 1];
                if (want == 0 ? e.EmitCount == 0 || c.Parts[e.Emit[0]].D.Piece != EnginePiece.Stack : e.EmitCount == 0 || c.Parts[e.Emit[0]].D.Piece == EnginePiece.Cylinder)
                    AddEmit(e, i);
            }
    }

    private static void AddEmit(ModEngine e, int part)
    {
        if (e.EmitCount < ModEngine.MaxEmits)
            e.Emit[e.EmitCount++] = part;
    }

    /// <summary>Makes engine <paramref name="k"/> the construct's: one set of numbers for the tick.</summary>
    public static void Apply(Construct c, int k)
    {
        var e = c.ModEngines![k];
        var r = EngineRules.Current;
        var d = c.ModDef ??= new ConstructPartDef { Name = "Modular engine", Role = PartRole.Engine, EngineModular = true, Curve = EngineCurve.Petrol, FuelKind = EngineFuel.Fuel, Noise = 0.8f };
        d.IdleRpm = r.Idle[e.Size];
        d.PeakRpm = r.Peak[e.Size];
        d.MaxRpm = r.Max[e.Size];
        d.EngineTorque = e.Kw * 1000f / (d.PeakRpm / ConstructDrive.RpmPerRadS);
        d.BurnRate = e.Burn;
        d.SpoolSeconds = e.Spool;
        d.NeedsStarter = r.NeedsStarter[e.Size];
        c.ModActive = k;
    }

    /// <summary>A cylinder's health is about to change: crossing into a misfire changes the sums, so they are worked out again.</summary>
    public static void HealthChanging(Construct c, int part, float health)
    {
        ref var p = ref c.Parts[part];
        if (p.D.Piece != EnginePiece.Cylinder)
            return;
        var below = p.D.Health * EngineRules.Current.MisfireBelow;
        if (p.Health < below != health < below)
            c.Dirty = true;
    }

    /// <summary>"V8", "I4", "B6", "W12", "R16".</summary>
    public static string Layout(ModEngine e) => $"{(e.Banks switch { <= 1 => 'I', 2 => e.Boxer != 0 ? 'B' : 'V', 3 => 'W', _ => 'R' })}{e.Cylinders}";

    /// <summary>"480 kW", "1.6 GW".</summary>
    public static string PowerText(float kw) => kw >= 1e6f ? $"{kw / 1e6f:0.#} GW" : kw >= 1e4f ? $"{kw / 1e3f:0.#} MW" : $"{kw:0} kW";

    /// <summary>What is wrong with an engine, in plain words ("" for nothing).</summary>
    public static string FaultText(ModEngine e) => e.Fault switch
    {
        EngineFault.NoCrank => "no crank: a cylinder goes on a crank section",
        EngineFault.NoCylinders => "no cylinders on the crank",
        EngineFault.MixedSizes => "mixed sizes: use one size",
        EngineFault.CylinderOnEnd => "a cylinder goes on the crank's side, not its end",
        EngineFault.NoFuelSystem => BankText[e.FaultBank < BankText.Length ? e.FaultBank : 0],
        EngineFault.NoOutput => "no flywheel on the end of the crank",
        EngineFault.OutputWrecked => "the flywheel is wrecked",
        EngineFault.NoStarter => "needs a starter motor",
        EngineFault.NoTank => "no fuel: link a fuel tank",
        EngineFault.AllWrecked => "every cylinder is wrecked",
        _ => "",
    };

    /// <summary>What the pieces add up to, then what is wrong: "V8 320 kW, thirsty - no fuel: link a fuel tank".</summary>
    public static string Readout(ModEngine e) => SandboxKeys.Line(Sums(e), null, FaultText(e)).TrimStart(' ', '-');

    /// <summary>What the pieces add up to: "V8 320 kW, thirsty".</summary>
    private static string Sums(ModEngine e)
    {
        var rules = EngineRules.Current;
        var word = e.Kw <= 0 ? "" : e.Burn / e.Kw / rules.BurnPerKw is var t && t >= rules.ThirstyAt ? ", thirsty" : t <= rules.FrugalAt ? ", frugal" : "";
        var hurt = e.Wrecked > 0 ? $", {e.Wrecked} wrecked" : "";
        hurt += e.Misfiring > 0 ? $", {e.Misfiring} misfiring" : "";
        return e.Cylinders > 0 ? $"{Layout(e)} {PowerText(e.Kw)}{word}{hurt}" : "";
    }

    /// <summary>The look line for an engine piece (any tool in hand).</summary>
    public static string Look(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var health = p.Broken ? "wrecked" : "";
        var k = c.EngineOf[part] - 1;
        if (k < 0 || k >= c.ModCount)
            return SandboxKeys.Line(d.Name, health, d.Piece == EnginePiece.Crank ? "no cylinders on the crank" : null);
        var e = c.ModEngines![k];
        if (!e.HasEngine)
            return SandboxKeys.Line(d.Name, health, d.Piece == EnginePiece.Stack ? "stand it on an exhaust outlet, or pipe one into it" : "bolt it on a cylinder head");
        var running = c.ModActive == k && c.EngineRunning ? $"{c.Rpm:0} rpm" : "";
        var share = d.Piece == EnginePiece.Cylinder && !p.Broken && Share(c, part, EngineRules.Current) < 1 ? "misfiring" : "";
        var battery = c.ModActive == k && c.EngineDef.NeedsStarter && !c.HasBattery ? "no battery for the starter" : "";
        return SandboxKeys.Line(d.Name, SandboxKeys.Join(health, share, Sums(e), running), SandboxKeys.Join(FaultText(e), battery));
    }

    /// <summary>What decides <see cref="Look"/> beyond the part (the look line's cache key).</summary>
    public static long LookKey(Construct c, int part)
    {
        var k = c.EngineOf[part] - 1;
        if (k < 0 || k >= c.ModCount)
            return 0;
        var e = c.ModEngines![k];
        return (long)e.Fault ^ (long)e.FaultBank << 4 ^ (long)e.Cylinders << 8 ^ (long)(int)e.Kw << 16 ^ (e.Fuelled ? 1L << 15 : 0) ^ (long)e.Misfiring << 42;
    }

    /// <summary>Why no modular engine on the construct runs, for the HUD ("" when there are no pieces).</summary>
    public static string Problem(Construct c)
    {
        for (var k = 0; k < c.ModCount; k++)
        {
            var e = c.ModEngines![k];
            if (!e.HasEngine)
                continue;
            if (!e.Complete || e.Fault == EngineFault.NoTank)
                return FaultText(e);
        }
        return "";
    }
}

public sealed partial class Construct
{
    public byte[] EngineOf = new byte[FirstParts]; // which modular engine a part belongs to (1 up, 0: none)
    public byte[] EngineSlot = new byte[FirstParts]; // a cylinder's place in its engine (its firing order)
    public ModEngine[]? ModEngines; // made with the first piece on a construct
    public int ModCount; // modular engines found
    public int ModActive = -1; // the one that is this construct's Engine
    public ConstructPartDef? ModDef; // its numbers
    public float EngineLoad; // 0..1: how hard the engine is working now (the smoke, the sound; on the wire)
    public float EngineAngle; // the crank's drawn angle (client, the renderer)
    public double EngineDrawn; // when it was last advanced
    public Vector3 EngineShake; // the running engine's rock, in the body frame (the renderer)

    /// <summary>The numbers of the engine in use: its part's, or a modular engine's (call with <see cref="Engine"/> valid).</summary>
    public ConstructPartDef EngineDef => ModActive >= 0 && ModDef != null ? ModDef : DefOf(Engine);

    /// <summary>The running modular engine in use, or null.</summary>
    public ModEngine? ActiveEngine => ModActive >= 0 ? ModEngines?[ModActive] : null;

    /// <summary>Is <paramref name="part"/> the engine, or a piece of the modular engine in use?</summary>
    public bool PartOfEngine(int part) => part == Engine || ModActive >= 0 && (uint)part < (uint)PartCount && EngineOf[part] == ModActive + 1;
}
