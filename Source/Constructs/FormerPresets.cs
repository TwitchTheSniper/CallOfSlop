using System.Text;

namespace CallOfSlop;

/// <summary>
/// The Fan and Airframe Formers' presets (docs/sandbox/constructs/production.md "A former's orders"): a catalogue wing, control surface
/// or rotor a former makes from a key of its own, as a Press's preset blank gives a catalogue part. Data: <c>Constructs/Extrusion.json</c>
/// <c>"presets"</c>, each <c>{"part": NAME, "piece": KEY}</c>. A former working an order for that part grows the key's piece, paid as any
/// piece is, and what comes off at the cut is the catalogue part itself (<see cref="ConstructExtruder"/>), so a blueprint that uses it is
/// built from the very part it names. <c>--former-presets</c> compares every catalogue wing and rotor with the key a former would cut.
/// </summary>
public static class FormerPresets
{
    private static Dictionary<ushort, ExtrusionKey>? _byPart;
    private static ExtrusionTable? _of;

    /// <summary>Every preset by its catalogue part's id (built once per table; a name the parts don't have, or a key that doesn't read, is left out).</summary>
    private static Dictionary<ushort, ExtrusionKey> ByPart()
    {
        var t = ExtrusionTable.Current;
        if (_byPart != null && ReferenceEquals(_of, t))
            return _byPart;
        var map = new Dictionary<ushort, ExtrusionKey>();
        foreach (var (part, piece) in t.Presets)
            if (ConstructParts.Find(part) is { } d && d.Id < Extrusion.RuntimeBase && Extrusion.ParseKey(piece) is { Growing: false, Shape: ExShape.Fan or ExShape.Wing } k && Extrusion.Def(k) != null)
                map[(ushort)d.Id] = k;
        _of = t;
        return _byPart = map;
    }

    /// <summary>The key a former cuts for this catalogue part, or null (not a former's preset).</summary>
    public static ExtrusionKey? KeyOf(ushort part) => part != ConstructParts.None && ByPart().TryGetValue(part, out var k) ? k : null;

    public static ExtrusionKey? KeyOf(ConstructPartDef? d) => d == null || d.Id >= Extrusion.RuntimeBase ? null : KeyOf((ushort)d.Id);

    /// <summary>The catalogue part a former's order asks for, when it is a preset; else null.</summary>
    public static ConstructPartDef? PartOf(in WorkOrder o) => o.Part != ConstructParts.None && o.Name == null && KeyOf(o.Part) != null ? ConstructParts.Def(o.Part) : null;

    /// <summary>The key a former works for an order: a piece's own, or a preset part's. False: not a former's order.</summary>
    public static bool KeyFor(in WorkOrder o, out ExtrusionKey key)
    {
        if (o.IsPiece)
        {
            key = o.Key;
            return true;
        }
        if (o.Name == null && KeyOf(o.Part) is { } k)
        {
            key = k;
            return true;
        }
        key = default;
        return false;
    }

    /// <summary>"a Fan Former: Propeller 1.80 m across, 2 straight blades of steel", for a catalogue part that is a preset; else null.</summary>
    public static string? Line(ConstructPartDef? d) =>
        KeyOf(d) is { } k ? $"the {d!.Name} is formed: {Maker(k)} cuts it as a {Extrusion.Label(k)} of {RecyclingTable.Current.NameOf(k.Material)}" : null;

    private static string Maker(in ExtrusionKey k) => k.Shape == ExShape.Fan ? "a Fan Former" : "an Airframe Former";

    /// <summary>The smallest former of the key's kind whose limits take it, or null.</summary>
    public static ConstructPartDef? SmallestFormer(in ExtrusionKey k)
    {
        ConstructPartDef? best = null;
        foreach (var d in ConstructParts.All)
        {
            if (!(k.Shape == ExShape.Fan ? d.IsFanFormer : d.IsAirframeFormer) || Fits(d, k) != null)
                continue;
            if (best == null || d.ExtWheelMax.Y < best.ExtWheelMax.Y)
                best = d;
        }
        return best;
    }

    /// <summary>Does this former's machine take the key's size? Null, or why not.</summary>
    public static string? Fits(ConstructPartDef d, in ExtrusionKey k)
    {
        int Cm(float m) => (int)MathF.Round(m * 100);
        if (k.Shape == ExShape.Fan)
        {
            var most = Math.Min(Extrusion.MaxFanCm, Cm(d.ExtWheelMax.Y));
            if (k.H > most)
                return $"a prop {k.H / 100f:0.##} m across is beyond the {d.Name} ({most / 100f:0.##} m)";
            if (k.H < Cm(d.ExtWheelStart.Y))
                return $"a prop {k.H / 100f:0.##} m across is smaller than the {d.Name}'s first ({d.ExtWheelStart.Y:0.##} m)";
            return null;
        }
        var span = Math.Min(Extrusion.MaxLengthCm, Cm(d.ExtWheelMax.Y));
        if (k.W > Cm(d.ExtWheelMax.X) || k.L > span)
            return $"a {k.W / 100f:0.##} m chord, {k.L / 100f:0.##} m span is beyond the {d.Name} ({d.ExtWheelMax.X:0.##} m chord, {span / 100f:0.##} m span)";
        if (k.W < Cm(d.ExtWheelStart.X) || k.L < Cm(d.ExtWheelStart.Y))
            return $"a {k.W / 100f:0.##} m chord, {k.L / 100f:0.##} m span is smaller than the {d.Name}'s first ({d.ExtWheelStart.X:0.##} x {d.ExtWheelStart.Y:0.##} m)";
        return null;
    }

    // ---- --former-presets: every catalogue wing and rotor against the key a former would cut ------------------------------

    private const float Close = 0.005f; // (flight numbers: the same to half a percent)
    private const float MassClose = 0.01f; // (mass: within 1 %, since the piece is paid by its volume and the part comes off in its place)

    /// <summary>The key a former would cut for a catalogue wing, control surface or rotor, by its numbers; or null with why.</summary>
    public static ExtrusionKey? Derive(ConstructPartDef d, out string why)
    {
        why = "";
        var t = ExtrusionTable.Current;
        var mat = RecyclingTable.Current.IdOf(d.Material == "cloth" ? "cloth" : "steel");
        if (d.Rotor is { } r)
        {
            var name = d.Name;
            var kind = name.Contains("Helicopter", StringComparison.Ordinal) ? "heli" : name.Contains("Tail Rotor", StringComparison.Ordinal) ? "tail"
                : name.Contains("Ducted", StringComparison.Ordinal) ? "ducted" : name.Contains("Electric", StringComparison.Ordinal) ? "electric" : "prop";
            var ki = Array.FindIndex(t.FanKinds, x => x.Id == kind);
            if (ki < 0)
            {
                why = $"no fan kind \"{kind}\"";
                return null;
            }
            var across = (int)MathF.Round(r.Radius * 2 * 20) * Extrusion.StepCm;
            return new ExtrusionKey(0, 0, (ushort)across, 0, mat, false, ExShape.Fan, Extrusion.FanWall(ki, 0, Math.Clamp(r.Blades, 2, ConstructRotor.MaxBlades)));
        }
        if (d.Surface is not { } s)
        {
            why = "not a wing or a rotor";
            return null;
        }
        var n = d.Name;
        var id = n.Contains("Swept Wing L", StringComparison.Ordinal) ? "swept-left" : n.Contains("Swept Wing R", StringComparison.Ordinal) ? "swept-right"
            : n.Contains("Tail Fin", StringComparison.Ordinal) ? "fin" : n.Contains("Tail Plane", StringComparison.Ordinal) ? "tail" : n.Contains("Aileron", StringComparison.Ordinal) ? "aileron"
            : n.Contains("Elevator", StringComparison.Ordinal) ? "elevator" : n.Contains("Rudder", StringComparison.Ordinal) ? "rudder" : n.Contains("Flap", StringComparison.Ordinal) ? "flap" : "wing";
        var wi = Array.FindIndex(t.Airframes, x => x.Id == id);
        if (wi < 0)
        {
            why = $"no airframe \"{id}\"";
            return null;
        }
        var ext = d.BoundsMax - d.BoundsMin;
        var spanM = MathF.Max(ext.X, ext.Y); // (every catalogue surface's leading edge is -z: its span is the longer of x and y)
        var span = (int)MathF.Round(spanM * 20) * Extrusion.StepCm;
        var chord = (int)MathF.Round(s.Area / MathF.Max(0.01f, span / 100f) * 20) * Extrusion.StepCm;
        return new ExtrusionKey(0, (ushort)chord, 0, (ushort)span, mat, false, ExShape.Wing, (ushort)wi);
    }

    /// <summary>Where the run-time piece's numbers differ from the catalogue part's ("" when they agree), mass apart.</summary>
    public static string Differences(ConstructPartDef cat, ConstructPartDef piece)
    {
        var sb = new StringBuilder();
        void Num(string what, float a, float b, float close = Close)
        {
            if (MathF.Abs(a - b) > close * MathF.Max(MathF.Abs(a), MathF.Abs(b)) + 1e-4f)
                sb.Append(sb.Length > 0 ? ", " : "").Append(what).Append(' ').Append(a.ToString("0.###")).Append(" vs ").Append(b.ToString("0.###"));
        }
        if (cat.Surface is { } s && piece.Surface is { } p)
        {
            Num("area", s.Area, p.Area);
            Num("cl0", s.Cl0, p.Cl0);
            Num("stall", s.Stall * 180 / MathF.PI, p.Stall * 180 / MathF.PI);
            Num("cd0", s.Cd0, p.Cd0);
            Num("slope", s.Slope, p.Slope);
            Num("induced", s.Induced, p.Induced);
            Num("control", s.Control * 180 / MathF.PI, p.Control * 180 / MathF.PI);
            Num("effect", s.Effect, p.Effect);
            Num("sign", s.Sign, p.Sign);
            if (s.Tears != p.Tears)
                sb.Append(sb.Length > 0 ? ", " : "").Append(s.Tears ? "tears vs not" : "not vs tears");
        }
        else if (cat.Rotor is { } r && piece.Rotor is { } q)
        {
            Num("radius", r.Radius, q.Radius);
            Num("kW", r.RatedKw, q.RatedKw);
            Num("rpm", r.RatedOmega * 30 / MathF.PI, q.RatedOmega * 30 / MathF.PI);
            Num("motor kW", r.MotorKw, q.MotorKw);
            Num("blades", r.Blades, q.Blades);
            Num("top", r.TopSpeed, q.TopSpeed);
            Num("spool", r.Spool, q.Spool);
            Num("duct", r.Duct, q.Duct);
            if (r.Heli != q.Heli)
                sb.Append(sb.Length > 0 ? ", " : "").Append("heli ").Append(r.Heli).Append(" vs ").Append(q.Heli);
            if (r.Shaft != (q.MotorKw <= 0))
                sb.Append(sb.Length > 0 ? ", " : "").Append(r.Shaft ? "shaft vs motor" : "motor vs shaft");
        }
        else
            sb.Append("not the same kind of part");
        return sb.ToString();
    }

    /// <summary>
    /// <c>--former-presets</c>: every catalogue wing, control surface and rotor with the key a former would cut for it, the smallest former that
    /// takes it, its mass against the piece's, and where their flight numbers differ; then whether <c>Extrusion.json</c>'s presets are those
    /// that agree. Returns the number of problems (a preset whose numbers differ, or one missing).
    /// </summary>
    public static int Cli()
    {
        var bad = 0;
        var sb = new StringBuilder();
        var presets = ByPart();
        foreach (var d in ConstructParts.All)
        {
            if (d.Id >= Extrusion.RuntimeBase || d.IsExtrusion || d.Stage || d.Section is not ("Wings" or "Control surfaces" or "Rotors") || d.Surface == null && d.Rotor == null)
                continue;
            var listed = presets.TryGetValue((ushort)d.Id, out var have);
            if (Derive(d, out var why) is not { } k)
            {
                sb.Append($"STAYS  {d.Name}: {why}\n");
                continue;
            }
            var piece = Extrusion.Def(k);
            var former = SmallestFormer(k);
            if (piece == null)
            {
                sb.Append($"STAYS  {d.Name}: {Extrusion.Name(k)} doesn't build\n");
                continue;
            }
            var diff = Differences(d, piece);
            var ratio = piece.Mass / MathF.Max(0.01f, d.Mass);
            var massOk = MathF.Abs(ratio - 1) <= MassClose;
            var ok = diff.Length == 0 && massOk && former != null;
            var verdict = ok ? "PRESET" : "STAYS ";
            sb.Append($"{verdict} {d.Name}: {Extrusion.Name(k)} ({former?.Name ?? "no former takes it"}, tier {former?.Tier ?? 0}); mass {d.Mass:0.#} vs {piece.Mass:0.#} kg ({ratio:0.00})");
            if (diff.Length > 0)
                sb.Append("; differs: ").Append(diff);
            if (listed && have != k)
                sb.Append($"; LISTED AS {Extrusion.Name(have)}");
            sb.Append('\n');
            if (ok != listed || listed && have != k)
            {
                bad++;
                sb.Append(ok ? $"  PROBLEM: not in Extrusion.json's presets: {{\"part\": \"{d.Name}\", \"piece\": \"{Extrusion.Name(k)}\"}}\n" : "  PROBLEM: listed as a preset, but a former can't make it exactly\n");
            }
        }
        foreach (var (part, piece) in ExtrusionTable.Current.Presets)
            if (ConstructParts.Find(part) == null || Extrusion.ParseKey(piece) == null)
            {
                bad++;
                sb.Append($"PROBLEM: preset {part} / {piece} doesn't read\n");
            }
        sb.Append($"{presets.Count} presets, {bad} problems\n");
        Console.Write(sb.ToString());
        return bad;
    }
}
