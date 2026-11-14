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
/// The rules (Constructs/ModularEngine.json, written by the part kit's engine_parts): each size's power a cylinder and
/// its rpm, and the simple sums' numbers. The defaults match the file.
/// </summary>
public sealed class EngineRules
{
    public const string Format = "call-of-slop-modular-engine/2", Format1 = "call-of-slop-modular-engine/1"; // (/2 adds starterFrom; /1 is read with its default: a mod's)

    public readonly float[] Kw = [40, 800, 16000, 320000], Idle = [900, 600, 300, 150], Peak = [5000, 2800, 1400, 700], Max = [7000, 4000, 2000, 1000], Scale = [1, 4, 16, 64];
    public readonly bool[] NeedsStarter = [false, false, true, true];
    public float BurnPerKw = 0.00044f, MisfireBelow = 0.5f, MisfirePower = 0.5f, ThirstyAt = 1.15f, FrugalAt = 0.85f;
    public float StarterFrom = 8f; // an engine whose crank's scale reaches this needs a starter (sized-parts design 6.1: half-way in log between Medium and Large)

    /// <summary>An engine whose crank is this scale needs a starter (<see cref="StarterFrom"/>; a class: its row's scale).</summary>
    public bool StarterAt(float scale) => scale >= StarterFrom;

    /// <summary>An engine of this size class needs a starter (the Engine Kit plans by class).</summary>
    public bool StarterFor(int size) => StarterAt(Scale[Math.Clamp(size, 0, Scale.Length - 1)]);

    /// <summary>
    /// A size's numbers at any scale (sized-parts design 6.1): a straight line between the <c>sizes</c> rows in log-log (knots at their
    /// scales), past the ends the end segment. At a knot it is the row itself, not an interpolation beside it, so every file engine keeps
    /// its exact numbers.
    /// </summary>
    public void At(float scale, out float kw, out float idle, out float peak, out float max)
    {
        var i = 0;
        for (var k = 0; k < Scale.Length; k++)
            if (scale == Scale[k])
            {
                (kw, idle, peak, max) = (Kw[k], Idle[k], Peak[k], Max[k]);
                return;
            }
        while (i < Scale.Length - 2 && scale > Scale[i + 1])
            i++;
        var t = (MathF.Log(MathF.Max(scale, 1e-3f)) - MathF.Log(Scale[i])) / (MathF.Log(Scale[i + 1]) - MathF.Log(Scale[i]));
        float L(float[] v) => MathF.Exp(MathF.Log(v[i]) + t * (MathF.Log(v[i + 1]) - MathF.Log(v[i])));
        (kw, idle, peak, max) = (L(Kw), L(Idle), L(Peak), L(Max));
    }

    /// <summary>A cylinder's (or a fuel system's or booster's) kW at this scale: <see cref="At"/>'s.</summary>
    public float KwAt(float scale)
    {
        At(scale, out var kw, out _, out _, out _);
        return kw;
    }

    /// <summary>The size class nearest a scale in log (the Engine Kit's words, the sound bank's pick, the drawn crank's speed).</summary>
    public byte NearestSize(float scale)
    {
        var best = 0;
        for (var k = 1; k < Scale.Length; k++)
            if (MathF.Abs(MathF.Log(scale / Scale[k])) < MathF.Abs(MathF.Log(scale / Scale[best])))
                best = k;
        return (byte)best;
    }

    /// <summary>A piece's scale (design 6.1): its own (a pressed piece's, from its blank) or its class's row.</summary>
    public float ScaleOf(ConstructPartDef d) => d.PieceScale > 0 ? d.PieceScale : Scale[Math.Min(d.PieceSize, (byte)(Scale.Length - 1))];

    /// <summary>A cylinder's stroke ratio (design 6.3): a pressed one's from its blank (0.6 to 1.6), 1 on a file cylinder.</summary>
    public static float StrokeOf(ConstructPartDef d) => d.PieceStroke > 0 ? d.PieceStroke : 1f;

    /// <summary>
    /// A cylinder's kW by its displacement (stroke power, the user 2026-11-14: "stroke adds power too"): its bore's kW (<see cref="KwAt"/> of its scale, so
    /// about bore squared, main's rule) times its stroke ratio, so power goes with bore squared times stroke. At the plain stroke (1, every file
    /// cylinder and every preset's proportions) exactly what it was. Its tier multiplier (<c>PiecePower</c>) is the caller's.
    /// </summary>
    public float CylinderKw(ConstructPartDef d) => KwAt(ScaleOf(d)) * StrokeOf(d);

    /// <summary>The scale whose <see cref="KwAt"/> is <paramref name="kw"/> (the inverse, by the same lines in log-log): the size of fuel system or booster that feeds a cylinder of this power whole.</summary>
    public float ScaleForKw(float kw)
    {
        for (var k = 0; k < Scale.Length; k++)
            if (kw == Kw[k])
                return Scale[k];
        var i = 0;
        while (i < Scale.Length - 2 && kw > Kw[i + 1])
            i++;
        var t = (MathF.Log(MathF.Max(kw, 1e-3f)) - MathF.Log(Kw[i])) / (MathF.Log(Kw[i + 1]) - MathF.Log(Kw[i]));
        return MathF.Exp(MathF.Log(Scale[i]) + t * (MathF.Log(Scale[i + 1]) - MathF.Log(Scale[i])));
    }

    /// <summary>
    /// An engine whose crank is this scale and whose cylinders have this stroke needs a starter: its displacement's scale, the crank's times the cube
    /// root of the stroke (a long stroke is more to turn over), reaches <see cref="StarterFrom"/>. At stroke 1 it is <see cref="StarterAt(float)"/>.
    /// </summary>
    public bool StarterAt(float scale, float stroke) => StarterAt(stroke == 1f ? scale : scale * MathF.Cbrt(stroke));
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
        var firstStarter = int.MaxValue; // (the first size a /1 file flags "starter")
        var starterFrom = false;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("the file is not a JSON object");
            if (!root.TryGetProperty("format", out var f) || f.GetString() is not (Format or Format1))
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                switch (p.Name)
                {
                    case "format" or "about": break;
                    case "starterFrom": r.StarterFrom = Num(p.Value, p.Name, 0.1f, 1000); starterFrom = true; break;
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
                                    case "starter": if (q.Value.ValueKind == JsonValueKind.True) firstStarter = Math.Min(firstStarter, i); break; // (a /1 file's per-size flag: read into starterFrom below when the file gives none)
                                    default: throw new FormatException($"unknown size key \"{q.Name}\"");
                                }
                            if (r.Idle[i] >= r.Peak[i] || r.Peak[i] > r.Max[i])
                                throw new FormatException($"{ConstructEngine.SizeNames[i]}: idle, peak and limiter rpm go up in that order");
                        }
                        for (var i = 1; i < r.Scale.Length; i++)
                            if (r.Scale[i] <= r.Scale[i - 1])
                                throw new FormatException("the sizes' scales go up from small to capital"); // (EngineRules.At reads them as knots)
                        break;
                    default: throw new FormatException($"unknown key \"{p.Name}\"");
                }
            if (!starterFrom && firstStarter < r.Scale.Length) // (a /1 file: its first starter size, half-way in log from the one below)
                r.StarterFrom = firstStarter == 0 ? r.Scale[0] : MathF.Sqrt(r.Scale[firstStarter - 1] * r.Scale[firstStarter]);
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
    public byte Size; // the size class nearest the crank's scale (the kit's words, the sound bank, the drawn crank)
    public float Scale = 1f; // the crank's: its largest section's (sized-parts design 6.1); it sets the revs and whether it needs a starter
    public float Stroke = 1f; // the cylinders' stroke ratio, their mean weighted by kW (design 6.3): rpm is the crank's over it; each cylinder's kW is already times its own (stroke power), so a long stroke gives more power at lower revs, torque by its square
    private float _strokeKw, _kwForStroke;
    public int Starved; // the first bank (1 up) whose fuel system or booster is small for its cylinders, or 0 (design 6.4)
    public float StarvedShare; // what that bank's fuel system feeds (0 to 1), or its booster gives of its boost when the feed is whole
    public bool StarvedBoost; // the booster is the small one, not the fuel system
    public ConstructPartDef? StarvedBy; // the small piece (its name for the text)
    public int Cranks, Cylinders, Wrecked, Misfiring, Banks, Boxer; // Boxer: two opposite banks
    public int Fed; // banks with a fuel system
    public float Kw, Burn, Spool;
    public bool HasStarter, Fuelled, HasEngine; // HasEngine: it has a crank or a cylinder (a lone stack isn't an engine)
    public EngineFault Fault; // the first thing wrong
    public int FaultBank; // NoFuelSystem: which (1 up)
    public readonly int[] Emit = new int[MaxEmits]; // where the smoke leaves
    public int EmitCount;

    /// <summary>The engine's revs: the crank's scale's (<see cref="EngineRules.At"/>) over its <see cref="Stroke"/> (design 6.3; 1 on every file cylinder).</summary>
    public void Rpm(EngineRules rules, out float idle, out float peak, out float max)
    {
        rules.At(Scale, out _, out idle, out peak, out max);
        if (Stroke != 1f)
            (idle, peak, max) = (idle / Stroke, peak / Stroke, max / Stroke);
    }

    internal void AddStroke(float kw, float r) => (_strokeKw, _kwForStroke) = (_strokeKw + kw * r, _kwForStroke + kw);

    internal void EndStroke() => Stroke = _kwForStroke > 0 && MathF.Abs(_strokeKw / _kwForStroke - 1f) > 1e-4f ? _strokeKw / _kwForStroke : 1f; // (exactly 1 when every cylinder's is: file engines keep their rows)

    internal void ResetStroke() => (Stroke, _strokeKw, _kwForStroke) = (1f, 0, 0);

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
            if (p.D.Piece == EnginePiece.Crank && (e.Cranks++ == 0 || rules.ScaleOf(p.D) > e.Scale))
            {
                // Sections of any sizes join into one crank; the largest sets its scale and so the revs (the heaviest to turn: design 6.1).
                e.Scale = rules.ScaleOf(p.D);
                e.Size = rules.NearestSize(e.Scale);
            }
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
        Span<float> bankKw = stackalloc float[bankSlots], fuelFeed = stackalloc float[bankSlots], boostShare = stackalloc float[bankSlots]; // (a bank's largest cylinder's kW at its bore; what its fuel system feeds; its booster's share)
        Span<int> fuelBy = stackalloc int[bankSlots], boostBy = stackalloc int[bankSlots];
        fuelMul.Clear();
        bankKw.Clear();
        fuelFeed.Fill(1f);
        boostShare.Fill(1f);
        fuelBy.Fill(-1);
        boostBy.Fill(-1);
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
                else if (e.Output < 0) // (a flywheel of any size works: design 6.4, anything bolts on)
                {
                    e.Output = b;
                    if (e.Fault == EngineFault.OutputWrecked)
                        e.Fault = EngineFault.None;
                }
                continue;
            }
            if (pb.Piece != EnginePiece.Cylinder)
                continue; // (a cylinder of any size goes on: it gives its own bore's power, design 6.4)
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
            bankKw[er * MaxBanks + be] = MathF.Max(bankKw[er * MaxBanks + be], rules.CylinderKw(pb)); // (the bank's kW: its largest cylinder's, by its bore and stroke)
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
                bank[b] = bank[a];
                var at = (c.EngineOf[b] - 1) * MaxBanks + bank[a];
                // Any size bolts on (design 6.4): a fuel system feeds its bank by its kW over the bank's largest cylinder's, at most all of
                // it; a booster's gain is scaled by the same ratio and is never more than its own. Pieces of the bank's size: every ratio 1.
                var share = bankKw[at] > 0 ? MathF.Min(1f, rules.KwAt(rules.ScaleOf(d)) / bankKw[at]) : 1f;
                if (d.Piece == EnginePiece.Fuel && d.PiecePower * share > fuelMul[at])
                {
                    (fuelMul[at], fuelThirst[at]) = (d.PiecePower * share, d.PieceThirst);
                    (fuelFeed[at], fuelBy[at]) = (share, b);
                }
                else if (d.Piece == EnginePiece.Boost && Gain(d.PiecePower, share) > boostMul[at])
                {
                    (boostMul[at], boostThirst[at]) = (Gain(d.PiecePower, share), d.PieceThirst);
                    (boostShare[at], boostBy[at]) = (share, b);
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
            var kw = rules.CylinderKw(d) * d.PiecePower * share * fuelMul[at] * boostMul[at]; // (a cylinder's own power by its bore and stroke, its displacement: stroke power 2026-11-14; PiecePower 1, more for a tier cylinder's)
            e.Kw += kw;
            e.AddStroke(kw, EngineRules.StrokeOf(d));
            e.Burn += kw * d.PieceThirst * fuelThirst[at] * boostThirst[at] * rules.BurnPerKw;
        }
        for (var k = 0; k < c.ModCount; k++)
        {
            var e = c.ModEngines[k];
            e.EndStroke();
            for (var q = 0; q < e.Banks; q++)
            {
                var at = k * MaxBanks + q;
                if (fuelMul[at] > 0)
                    e.Fed++;
                else if (e.FaultBank == 0)
                    e.FaultBank = q + 1;
                if (e.Starved == 0 && fuelMul[at] > 0 && (fuelFeed[at] < 0.999f || boostShare[at] < 0.999f))
                {
                    // The first bank a small piece costs power: it runs, and its look line says so where a fault would be (design 6.4).
                    e.Starved = q + 1;
                    e.StarvedBoost = fuelFeed[at] >= 0.999f;
                    e.StarvedShare = e.StarvedBoost ? boostShare[at] : fuelFeed[at];
                    var by = e.StarvedBoost ? boostBy[at] : fuelBy[at];
                    e.StarvedBy = by >= 0 ? c.Parts[by].D : null;
                }
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
                    : NeedsStarter(e, rules) && !e.HasStarter ? EngineFault.NoStarter
                    : e.Fed < e.Banks ? EngineFault.NoFuelSystem
                    : EngineFault.None;
        }
        Emits(c, bank);
    }

    private static bool Joins(Construct c, int i) => c.Parts[i].D.Piece != EnginePiece.None && c.Parts[i].Working;

    /// <summary>A booster's gain on a bank it feeds this share of: its own multiplier when the share is whole (exactly the file's), else 1 + (power - 1) x share.</summary>
    private static float Gain(float power, float share) => share >= 1f ? power : 1f + (power - 1f) * share;

    /// <summary>Does this engine need a starter to turn over? Its crank's scale, with its stroke's cube root (its displacement), reaches the rules' starterFrom (design 6.1; stroke power).</summary>
    public static bool NeedsStarter(ModEngine e, EngineRules rules) => rules.StarterAt(e.Scale, e.Stroke);

    private static void Reset(ModEngine e)
    {
        e.Output = -1;
        e.Size = 0;
        e.Scale = 1f;
        e.Starved = 0;
        e.ResetStroke();
        e.StarvedShare = 1f;
        e.StarvedBoost = false;
        e.StarvedBy = null;
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
        e.Rpm(r, out var idle, out var peak, out var max); // (the crank's scale sets the revs, the stroke divides them: design 6.1, 6.3; at a size's own scale and stroke 1 its row)
        (d.IdleRpm, d.PeakRpm, d.MaxRpm) = (idle, peak, max);
        d.EngineTorque = e.Kw * 1000f / (d.PeakRpm / ConstructDrive.RpmPerRadS);
        d.BurnRate = e.Burn;
        d.SpoolSeconds = e.Spool;
        d.NeedsStarter = NeedsStarter(e, r);
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
        _ => StarvedText(e),
    };

    /// <summary>
    /// The first bank a small piece costs power (design 6.4; "" for none): "bank 1 starved: the Small Carburettor feeds 5% of its cylinders",
    /// "bank 1: the Small Turbo is small for its cylinders (40% of its boost)". It still runs on that.
    /// </summary>
    public static string StarvedText(ModEngine e) => e.Starved == 0 ? ""
        : e.StarvedBoost ? $"bank {e.Starved}: the {e.StarvedBy?.Name ?? "booster"} is small for its cylinders ({e.StarvedShare * 100:0}% of its boost)"
        : $"bank {e.Starved} starved: the {e.StarvedBy?.Name ?? "fuel system"} feeds {e.StarvedShare * 100:0.#}% of its cylinders";

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
        return SandboxKeys.Line(d.Name, SandboxKeys.Join(health, share, Sums(e), running), SandboxKeys.Join(FaultText(e), e.Fault != EngineFault.None ? StarvedText(e) : "", battery));
    }

    /// <summary>What decides <see cref="Look"/> beyond the part (the look line's cache key).</summary>
    public static long LookKey(Construct c, int part)
    {
        var k = c.EngineOf[part] - 1;
        if (k < 0 || k >= c.ModCount)
            return 0;
        var e = c.ModEngines![k];
        return (long)e.Fault ^ (long)e.FaultBank << 4 ^ (long)e.Cylinders << 8 ^ (long)(int)e.Kw << 16 ^ (e.Fuelled ? 1L << 15 : 0) ^ (long)e.Misfiring << 42 ^ (long)e.Starved << 50 ^ (long)(int)(e.StarvedShare * 1000) << 54;
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
