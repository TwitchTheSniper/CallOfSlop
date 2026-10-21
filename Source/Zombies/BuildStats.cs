using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace CallOfSlop;

/// <summary>
/// One barrel of a built weapon: where it fires from (gun model space), how far it's turned off the bore line, which
/// receiver it belongs to (<see cref="Group"/>), and its pellets, damage and spread relative to the weapon's numbers.
/// <see cref="Rotary"/> barrels take turns within their group (a minigun receiver).
/// </summary>
public readonly record struct BarrelPoint(Vector3 Muzzle, float YawDeg, float PitchDeg, int Group, int Pellets, float DamageMult, float SpreadMult, bool Rotary);

/// <summary>One barrel muzzle of a receiver as the stat pipeline sees it.</summary>
public readonly record struct BarrelLeaf(BuildNode Barrel, int Muzzle, float SplitMult, float SplayDeg);

/// <summary>
/// Turns a build (a tree of parts) into a <see cref="WeaponDef"/> the existing firing code uses. Deterministic: the same
/// tree always gives the same numbers. The pipeline (docs/zombies/WEAPON-BUILDER.md, "Stat pipeline"):
/// <list type="number">
/// <item>Each receiver on its own: base numbers, its action and fire rate, then its parts in stages (receiver, ammo,
/// feed, splitters), then each barrel path (barrel + muzzle device) on its own copy. Splitter rule: every output gets
/// the incoming shot's damage x the splitter's outputMult (nested splitters multiply; 1/N conserves the shot).</item>
/// <item>The receivers combine by the grip's fire style. Volley: the slowest fire rate, the smallest feed, recoil adds
/// up (root of the sum of squares). Rotate: fire rates add, feeds add, recoil averages. Both: the strictest trigger
/// (semi beats burst beats auto) and the longest reload plus a per-receiver penalty.</item>
/// <item>The grip and what's on it: grip, stock, foregrips, optic, utility parts.</item>
/// <item>Weight against the grip's carry: mobility, aim and draw time, recoil; then the frame's own mobility.</item>
/// <item>Everything is clamped to the ranges weapon files allow.</item>
/// </list>
/// </summary>
public static class BuildStats
{
    private static readonly FieldInfo[] Numeric = AttachmentFile.EffectFields.Where(f => f.FieldType == typeof(float) || f.FieldType == typeof(int)).ToArray();

    private static readonly PartCategory[] ReceiverStages = [PartCategory.Receiver, PartCategory.Ammo, PartCategory.Feed, PartCategory.Splitter];

    private static readonly string[] Averaged =
    [
        nameof(WeaponDef.RangeM), nameof(WeaponDef.MinDamageMult), nameof(WeaponDef.HeadMult), nameof(WeaponDef.AdsSpreadMult), nameof(WeaponDef.AdsTime),
        nameof(WeaponDef.EquipTime), nameof(WeaponDef.Mobility),
    ];

    private static readonly PartCategory[] GripStages = [PartCategory.Grip, PartCategory.Stock, PartCategory.Optic, PartCategory.Utility];

    /// <summary>Pellets per barrel one receiver fires (its ammo, feed and barrel effects on pellets).</summary>
    public static int PelletsOf(BuildNode receiver)
    {
        var w = new WeaponDef { Name = "", Class = WeaponClass.AssaultRifle, Damage = 1, RangeM = 1, Rpm = 1, Mag = 1, Reload = 1, HipSpreadDeg = 1 };
        foreach (var n in receiver.Walk().Where(n => n.Part.Category is PartCategory.Receiver or PartCategory.Ammo or PartCategory.Feed or PartCategory.Barrel))
            foreach (var e in n.Part.Effects.Where(e => e.Field.Name == nameof(WeaponDef.Pellets)))
                e.ApplyTo(w);
        return Math.Clamp(w.Pellets, 1, 30);
    }

    /// <summary>A receiver's barrel muzzles (in socket order), with the splitter multiplier and splay on the way to each.</summary>
    public static List<BarrelLeaf> Leaves(BuildNode receiver)
    {
        var list = new List<BarrelLeaf>();
        Collect(receiver, 1f, 0f, list);
        return list;
    }

    private static void Collect(BuildNode n, float mult, float splay, List<BarrelLeaf> into)
    {
        if (n.Part.Category == PartCategory.Barrel)
        {
            for (var i = 0; i < n.Part.Muzzles.Length; i++)
                into.Add(new BarrelLeaf(n, i, mult, splay));
            return;
        }
        var split = n.Part.Split;
        var outs = n.Part.Sockets.Where(s => s.Type == "barrel").ToList();
        for (var i = 0; i < outs.Count; i++)
        {
            if (n.Child(outs[i].Name) is not { } child)
                continue;
            var m = split != null ? mult * split.OutputMult : mult;
            // Outputs fan out evenly from -splay to +splay across the splitter's barrel sockets.
            var sp = split != null && outs.Count > 1 ? splay + split.SplayDeg * (2f * i / (outs.Count - 1) - 1f) : splay;
            Collect(child, m, sp, into);
        }
    }

    /// <summary>Every barrel muzzle of the build, receiver by receiver (group = receiver index): the order of <see cref="WeaponDef.MuzzlePoints"/>.</summary>
    public static List<(int Group, BarrelLeaf Leaf)> AllLeaves(WeaponBuild b)
    {
        var list = new List<(int, BarrelLeaf)>();
        var rs = b.Receivers;
        for (var g = 0; g < rs.Count; g++)
            foreach (var l in Leaves(rs[g]))
                list.Add((g, l));
        return list;
    }

    private static WeaponDef BaseDef(string name, ReceiverInfo r)
    {
        var w = WeaponDef.Blank();
        w.Name = name;
        w.Class = r.Class;
        w.Damage = 20;
        w.RangeM = 25;
        w.Rpm = r.Rpm;
        w.Mag = 30;
        w.Reload = 2.2f;
        w.HipSpreadDeg = 2f;
        w.AdsZoom = 1.25f;
        w.Mode = r.Action switch
        {
            ActionKind.Burst => FireMode.Burst,
            ActionKind.Auto or ActionKind.Rotary or ActionKind.Chain => FireMode.Auto,
            _ => FireMode.Semi,
        };
        w.BurstCount = r.BurstCount;
        w.BurstDelay = r.BurstDelay;
        Recoil.ApplyClassDefaults(w);
        w.RecoilDeg = r.Class switch
        {
            WeaponClass.SMG => 0.45f,
            WeaponClass.LMG => 0.6f,
            WeaponClass.Shotgun => 3f,
            WeaponClass.Sniper => 3.5f,
            WeaponClass.DMR => 1.4f,
            WeaponClass.Pistol => 0.9f,
            WeaponClass.Special => 1f,
            _ => 0.7f,
        };
        return w;
    }

    /// <summary>One receiver's numbers on its own, and its barrels (damage per barrel as a float, pellets, hip spread, splay).</summary>
    public sealed class ReceiverResult
    {
        public required BuildNode Node;
        public required WeaponDef Def;
        public readonly List<(BarrelLeaf Leaf, float Damage, int Pellets, float HipSpread)> Barrels = new();
    }

    /// <summary>Runs the per-receiver part of the pipeline (stages, then barrel paths).</summary>
    public static ReceiverResult ReceiverStats(string name, BuildNode receiver)
    {
        var r = receiver.Part.Receiver!;
        var w = BaseDef(name, r);
        var nodes = receiver.Walk().ToList();
        foreach (var cat in ReceiverStages)
            foreach (var n in nodes.Where(n => n.Part.Category == cat))
                Apply(n.Part, w);
        var res = new ReceiverResult { Node = receiver, Def = w };
        var copies = new List<WeaponDef>();
        foreach (var leaf in Leaves(receiver))
        {
            var c = w.Clone();
            Apply(leaf.Barrel.Part, c);
            foreach (var (s, dev) in leaf.Barrel.Children())
                if (s.Type == "muzzle")
                    Apply(dev.Part, c);
            copies.Add(c);
            res.Barrels.Add((leaf, c.Damage * leaf.SplitMult, Math.Clamp(c.Pellets, 1, 30), c.HipSpreadDeg));
        }
        if (copies.Count > 0)
        {
            foreach (var f in Numeric)
            {
                double sum = 0;
                foreach (var c in copies)
                    sum += Convert.ToDouble(f.GetValue(c), CultureInfo.InvariantCulture);
                var avg = sum / copies.Count;
                f.SetValue(w, f.FieldType == typeof(int) ? (object)(int)Math.Round(avg) : (float)avg);
            }
            w.Damage = Math.Max(1, (int)MathF.Round(res.Barrels.Average(x => x.Damage)));
        }
        if (r.RpmPerBarrel > 0)
            w.Rpm = r.RpmPerBarrel * Math.Max(1, copies.Count);
        return res;
    }

    /// <summary>
    /// The build's weapon stats, or null with <paramref name="problems"/> filled if it isn't complete and legal. The model
    /// (and with it the muzzle positions and hand anchors) is added by <see cref="WeaponBuilder.Assemble"/>.
    /// </summary>
    public static WeaponDef? Compute(WeaponBuild b, out List<BuildProblem> problems)
    {
        problems = BuildRules.Check(b);
        return problems.Count > 0 ? null : ComputeUnchecked(b);
    }

    /// <summary>The stats of a complete build (no rule checks). Muzzle positions are zero until the model is built.</summary>
    public static WeaponDef ComputeUnchecked(WeaponBuild b)
    {
        var grip = b.Root!;
        var frame = grip.Part.Frame!;
        var style = b.EffectiveStyle;
        var name = WeaponBuild.CleanName(b.Name);
        var per = b.Receivers.Select(r => ReceiverStats(name, r)).ToList();
        var lead = per[0];
        var w = lead.Def.Clone();
        var count = per.Count;
        var rotate = style == FireStyle.Rotate && count > 1;

        // ---- Combine the receivers ----
        var defs = per.Select(p => p.Def).ToList();
        w.Mode = defs.Any(d => d.Mode == FireMode.Semi) ? FireMode.Semi : defs.Any(d => d.Mode == FireMode.Burst) ? FireMode.Burst : FireMode.Auto;
        if (defs.FirstOrDefault(d => d.Mode == FireMode.Burst) is { } burst)
            (w.BurstCount, w.BurstDelay) = (burst.BurstCount, burst.BurstDelay);
        w.Rpm = rotate ? Math.Min(3000, defs.Sum(d => d.Rpm)) : defs.Min(d => d.Rpm);
        w.Mag = rotate ? Math.Min(1000, defs.Sum(d => d.Mag)) : defs.Min(d => d.Mag);
        w.Reload = defs.Max(d => d.Reload) + frame.ReloadPerReceiver * (count - 1);
        w.RecoilDeg = rotate ? defs.Average(d => d.RecoilDeg) : MathF.Sqrt(defs.Sum(d => d.RecoilDeg * d.RecoilDeg));
        foreach (var f in Averaged)
        {
            var fi = typeof(WeaponDef).GetField(f)!;
            fi.SetValue(w, defs.Average(d => (float)fi.GetValue(d)!));
        }
        var barrels = per.SelectMany((p, g) => p.Barrels.Select(x => (Group: g, x.Leaf, x.Damage, x.Pellets, x.HipSpread, Rotary: p.Node.Part.Receiver!.Action == ActionKind.Rotary))).ToList();
        var meanHip = barrels.Count > 0 ? barrels.Average(x => x.HipSpread) : w.HipSpreadDeg;
        w.HipSpreadDeg = meanHip;
        var maxDamage = barrels.Count > 0 ? barrels.Max(x => x.Damage) : w.Damage;
        w.Damage = Math.Max(1, (int)MathF.Round(maxDamage));
        w.Pellets = barrels.Count > 0 ? barrels[0].Pellets : w.Pellets;
        var spooling = per.Where(p => p.Node.Part.Receiver!.SpoolTime > 0).Select(p => p.Node.Part.Receiver!).ToList();
        w.SpoolTime = spooling.Count > 0 ? spooling.Max(r => r.SpoolTime) : 0f;
        w.SpoolStart = spooling.Count > 0 ? spooling.Min(r => r.SpoolStart) : 1f;

        // ---- The grip and what's on it (including receivers' utility rails) ----
        var gripParts = b.Nodes.Where(n => n.ReceiverNode() == null || n.Part.Category == PartCategory.Utility).ToList();
        foreach (var cat in GripStages)
            foreach (var n in gripParts.Where(n => n.Part.Category == cat))
                Apply(n.Part, w);
        if (gripParts.FirstOrDefault(n => n.Part.Category == PartCategory.Optic) is { } optic)
        {
            w.Optic = optic.Part.Optic;
            w.Sight = optic.Part.Sight;
        }

        // ---- Barrels: per-barrel damage, pellets and spread relative to the weapon's numbers ----
        var single = barrels.Count == 1;
        if (!single)
        {
            w.MuzzlePoints = barrels.Select(x => new BarrelPoint(Vector3.Zero, x.Leaf.SplayDeg, 0f, x.Group, x.Pellets, x.Damage / MathF.Max(1, w.Damage),
                x.HipSpread / MathF.Max(0.01f, meanHip), x.Rotary)).ToArray();
            w.RotateGroups = rotate;
        }

        // ---- Weight and the frame ----
        var weight = b.Weight;
        var over = MathF.Max(0, weight - frame.Carry);
        w.Mobility = w.Mobility * frame.Mobility - 0.012f * weight - 0.03f * over;
        w.AdsTime += 0.008f * weight + 0.02f * over;
        w.EquipTime += 0.03f * weight + 0.05f * over;
        w.RecoilDeg *= Math.Clamp(MathF.Sqrt(6f / (weight + 4f)), 0.55f, 1.25f);
        if (!frame.Ads)
        {
            w.NoAds = true;
            w.AdsZoom = 1f;
            w.Optic = OpticKind.Iron;
            w.Sight = null;
        }

        // ---- Flags ----
        var flags = b.Nodes.SelectMany(n => n.Part.Flags).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToArray();
        w.BuildFlags = flags;

        // ---- Sound host (the lead receiver's), look, text ----
        var sound = Weapons.Find(lead.Node.Part.Receiver!.SoundOf) ?? Weapons.DefaultPrimary;
        w.Id = sound.Id;
        w.Sound = sound.Sound == SoundStyle.Suppressed ? SoundStyle.Gun : sound.Sound;
        w.Caliber = sound.Caliber;
        w.Bright = sound.Bright;
        w.Tail = sound.Tail;
        w.Suppressor = flags.Contains("suppressed");
        if (w.Suppressor)
            w.Sound = SoundStyle.Suppressed;
        w.Accent = PartDef.RarityColor(b.Nodes.Max(n => n.Part.Rarity));
        w.Blurb = count == 1 ? $"Built on the {grip.Part.Name} with the {lead.Node.Part.Name}." : $"Built on the {grip.Part.Name} with {count} receivers.";
        w.Order = 10000;
        w.Kit = 0;

        Clamp(w);
        return w;
    }

    private static void Apply(PartDef p, WeaponDef w)
    {
        foreach (var e in p.Effects)
            e.ApplyTo(w);
    }

    /// <summary>Keeps every number inside the ranges weapon files allow (and recoil fields inside theirs).</summary>
    public static void Clamp(WeaponDef w)
    {
        foreach (var (field, (min, max)) in WeaponFile.Ranges)
        {
            var f = typeof(WeaponDef).GetField(field)!;
            if (f.FieldType == typeof(int))
                f.SetValue(w, Math.Clamp((int)f.GetValue(w)!, (int)MathF.Ceiling(min), (int)max));
            else
                f.SetValue(w, Math.Clamp((float)f.GetValue(w)!, min, max));
        }
        foreach (var f in Recoil.Fields)
        {
            var a = f.GetCustomAttribute<RecoilKeyAttribute>()!;
            if (f.FieldType == typeof(int))
                f.SetValue(w, Math.Clamp((int)f.GetValue(w)!, (int)MathF.Ceiling(a.Min), (int)a.Max));
            else
                f.SetValue(w, Math.Clamp((float)f.GetValue(w)!, a.Min, a.Max));
        }
        w.Mobility = Math.Clamp(w.Mobility, 0.4f, 1.2f);
        w.AdsTime = Math.Clamp(w.AdsTime, 0.08f, 0.8f);
        w.Pellets = Math.Clamp(w.Pellets, 1, 30);
    }
}
