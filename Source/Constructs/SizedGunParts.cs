using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace CallOfSlop;

/// <summary>
/// Sized gun parts (docs/sandbox/design/sized-gun-engine-parts.md 3.4): a pressed barrel owns a weapon builder part of its own, made once
/// with it (<see cref="Pressed"/>): a copy of its pattern's builder part named by the pressed key, with the pattern's curves (Pressing.json
/// "scale" rows with an "op") as stat changes, its muzzle moved to its length and its weight by its mass. <see cref="Parts.Find"/> finds it
/// by the key, so the gun code (ConstructWeapons.BuildReceiver, BuildRules, BuildStats) needs nothing new. Nothing here runs in a tick.
/// </summary>
public static class SizedGunParts
{
    private static readonly Dictionary<string, FieldInfo> Stats = AttachmentFile.EffectFields
        .Where(f => f.FieldType == typeof(float) || f.FieldType == typeof(int))
        .ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Is this a builder stat a curve may set (a number a part file's "stats" may change)?</summary>
    public static bool IsStat(string field) => Stats.ContainsKey(field);

    /// <summary>The sized builder part of a pressed gun part: <paramref name="builder"/> (its pattern's like part's) sized by the blank.</summary>
    public static PartDef Make(PartDef builder, PressDie die, PressPattern p, in ExtrusionKey k, string name)
    {
        var q = builder.Copy();
        q.Name = name;
        q.SourceFile = null;
        q.SizeAcrossCm = Pressed.Across(k);
        q.SizeLengthCm = k.L;
        q.SizeBore = q.SizeAcrossCm / (float)Math.Max(1, Pressed.Across(p.Preset));
        (q.SizeFitLo, q.SizeFitHi) = (die.FitLo, die.FitHi);
        // The length: muzzle, muzzle sockets and hand move along -Z with the blank's calibres (a plain barrel's muzzle is 0.04 m a calibre).
        var along = Pressed.Measure(PressMeasure.Calibres, k) / MathF.Max(1e-6f, Pressed.Measure(PressMeasure.Calibres, p.Preset));
        q.Muzzles = builder.Muzzles.Select(m => m with { Z = m.Z * along }).ToArray();
        q.Sockets = builder.Sockets.Select(s =>
        {
            if (s.Type != "muzzle")
                return s;
            var c = s.Copy();
            c.Pos = s.Pos with { Z = s.Pos.Z * along };
            return c;
        }).ToArray();
        if (builder.Hand is { } hand)
            q.Hand = hand with { Z = hand.Z * along };
        // Its builder weight follows its length in calibres, not its steel: a barrel twice as thick is the barrel a merged receiver twice the
        // size draws, whose builder part weighs what the file's does (BuildStats reads weight for recoil). A construct's mass is the blank's.
        q.Weight = MathF.Max(0.05f, Pressed.Sig3(builder.Weight * along));
        // The stats: the pattern's own, then each curve's value as a change of its own (the identity left out).
        var effects = new List<AttachmentEffect>(builder.Effects);
        foreach (var s in p.Scale)
        {
            if (s.Op.Length == 0 || !Stats.TryGetValue(s.Field, out var field))
                continue;
            var v = Pressed.Scaled(s, 0, p, k);
            if (s.Op == "mul" ? v == 1 : v == 0)
                continue;
            effects.Add(new AttachmentEffect { Field = field, Op = s.Op, Value = v }.Prepare());
        }
        q.Effects = effects.ToArray();
        return q;
    }

    /// <summary>
    /// The bore band (design section 5): why sized gun part <paramref name="part"/> doesn't suit a receiver of bore <paramref name="receiverBore"/>
    /// (its scale times its merge size: 1 for a plain catalogue receiver, N for an N x N merge), or null when it fits. A catalogue part always
    /// fits (a merge draws it to size). Inside the band nothing changes with the mismatch (the user's answer 4).
    /// </summary>
    public static string? FitWhy(PartDef part, float receiverBore)
    {
        if (part.SizeBore <= 0 || part.SizeFitHi <= 0)
            return null;
        var r = part.SizeBore / MathF.Max(1e-3f, receiverBore);
        if (r >= part.SizeFitLo - 1e-4f && r <= part.SizeFitHi + 1e-4f)
            return null;
        var preset = part.SizeAcrossCm / part.SizeBore / 100f; // (m: the pattern's preset across, bore 1)
        var inv = CultureInfo.InvariantCulture;
        return string.Create(inv, $"the barrel is {part.SizeAcrossCm / 100f:0.00} m across: this receiver takes {preset * receiverBore * part.SizeFitLo:0.00} to {preset * receiverBore * part.SizeFitHi:0.00} m");
    }

    /// <summary><c>--press-part</c>'s lines for a sized gun part: its length, muzzle and weight, and each curve's value beside the pattern's preset's.</summary>
    public static IEnumerable<string> Lines(PartDef gun, PartDef preset, PressPattern p, ExtrusionKey k)
    {
        var inv = CultureInfo.InvariantCulture;
        yield return string.Create(inv, $"gun part {gun.Name}: {gun.Category}, {gun.SizeAcrossCm / 100f:0.00} m across, {gun.SizeLengthCm / 100f:0.00} m long, {Pressed.Measure(PressMeasure.Calibres, k):0.##} calibres (the preset {preset.Name}: {Pressed.Measure(PressMeasure.Calibres, p.Preset):0.##})");
        yield return string.Create(inv, $"muzzle {Muzzle(gun):0.###} m (preset {Muzzle(preset):0.###}); builder weight {gun.Weight:0.###} kg (preset {preset.Weight:0.###}); {gun.Effects.Length} stat changes (preset {preset.Effects.Length})");
        foreach (var s in p.Scale)
        {
            if (s.Op.Length == 0)
                continue;
            var v = Pressed.Scaled(s, 0, p, k);
            var at = Pressed.Scaled(s, 0, p, p.Preset);
            yield return string.Create(inv, $"{s.Field} {Op(s, v)} (preset {Op(s, at)}; the curve {string.Join(", ", s.Points!.Select(x => string.Create(inv, $"{x.X:0.##}: {x.Y:0.####}")))}, held to {s.Lo:0.####} to {s.Hi:0.####})");
        }
    }

    private static string Op(PressScale s, float v) =>
        s.Op == "mul" ? "x" + v.ToString("0.####", CultureInfo.InvariantCulture) : v.ToString("+0.####;-0.####;+0", CultureInfo.InvariantCulture);

    private static float Muzzle(PartDef d) => d.Muzzles.Length == 0 ? 0 : MathF.Abs(d.Muzzles[0].Z);
}
