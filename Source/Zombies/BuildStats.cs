using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace CallOfSlop;

/// <summary>
/// One barrel of a built weapon: where it fires from (gun model space), how far it's turned off the bore line, which
/// receiver it belongs to (<see cref="Group"/>), and its pellets, damage and spread relative to the weapon's numbers.
/// <see cref="Rotary"/> barrels take turns within their group (a minigun receiver).
/// <see cref="Size"/> is the barrel's own <see cref="WeaponDef.BulletSize"/> (its barrel and muzzle device can make the round
/// bigger or smaller than the next barrel's).
/// </summary>
public readonly record struct BarrelPoint(Vector3 Muzzle, float YawDeg, float PitchDeg, int Group, int Pellets, float DamageMult, float SpreadMult, bool Rotary, float Size = 1f);

/// <summary>One barrel muzzle of a receiver as the stat pipeline sees it (splay: <see cref="SplayDeg"/> sideways, <see cref="PitchDeg"/> up).</summary>
public readonly record struct BarrelLeaf(BuildNode Barrel, int Muzzle, float SplitMult, float SplayDeg, float SpreadMult = 1f, float PitchDeg = 0f);

/// <summary>
/// Turns a build (a tree of parts) into a <see cref="WeaponDef"/> the existing firing code uses. Deterministic: the same
/// tree always gives the same numbers. The pipeline (docs/zombies/WEAPON-BUILDER.md, "Stat pipeline"):
/// <list type="number">
/// <item>Each receiver on its own: its bore's calibre, its action and fire rate, then its parts in stages (receiver, ammo,
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

    private static readonly PartCategory[] ReceiverStages =
        [PartCategory.Receiver, PartCategory.Ammo, PartCategory.Feed, PartCategory.Action, PartCategory.Multiplier, PartCategory.Upgrade, PartCategory.Splitter];

    /// <summary>The action a receiver with no action part would have (only ever used for incomplete builds' previews).</summary>
    private static readonly ActionInfo NoAction = new() { Kind = ActionKind.Semi, Rpm = 60 };

    private static readonly string[] Averaged =
    [
        nameof(WeaponDef.RangeM), nameof(WeaponDef.MinDamageMult), nameof(WeaponDef.HeadMult), nameof(WeaponDef.AdsSpreadMult), nameof(WeaponDef.AdsTime),
        nameof(WeaponDef.EquipTime), nameof(WeaponDef.Mobility), nameof(WeaponDef.BulletSize), nameof(WeaponDef.BulletSpeed),
    ];

    private static readonly PartCategory[] GripStages = [PartCategory.Grip, PartCategory.Stock, PartCategory.Optic, PartCategory.Utility];

    /// <summary>Pellets per barrel one receiver fires (its calibre, then its ammo, feed and barrel effects on pellets).</summary>
    public static int PelletsOf(BuildNode receiver)
    {
        var w = new WeaponDef { Name = "", Class = WeaponClass.AssaultRifle, Damage = 1, RangeM = 1, Rpm = 1, Mag = 1, Reload = 1, HipSpreadDeg = 1 };
        var cls = receiver.Part.Receiver is { } r ? Parts.Registry.ClassOf(r.Bore) : BoreClass.Default;
        w.Pellets = cls.Calibre.Pellets;
        var barrelPellets = cls.Pellets; // only shotguns let a barrel (or an ammo stat) change the pellets; the round block works anywhere
        foreach (var n in receiver.Walk().Where(n => n.Part.Category is PartCategory.Receiver or PartCategory.Ammo or PartCategory.Feed || (barrelPellets && n.Part.Category == PartCategory.Barrel)))
        {
            if (n.Part.Category == PartCategory.Ammo)
            {
                ApplyAmmo(n.Part, w, cls);
                continue;
            }
            foreach (var e in n.Part.Effects.Where(e => e.Field.Name == nameof(WeaponDef.Pellets)))
                e.ApplyTo(w);
        }
        return Math.Clamp(w.Pellets, 1, 30);
    }

    /// <summary>
    /// Pellets each barrel muzzle of a receiver fires, in <see cref="Leaves"/> order: the receiver's own
    /// (<see cref="PelletsOf"/> without the barrels), then that barrel's pellet effects (shotgun bores only) and its muzzle
    /// device's (any bore: a Blunderbuss Flare or Fork Crown splits whatever comes out). For the projectile limit.
    /// </summary>
    public static List<int> PelletsPerBarrel(BuildNode receiver)
    {
        var cls = receiver.Part.Receiver is { } r ? Parts.Registry.ClassOf(r.Bore) : BoreClass.Default;
        var w = new WeaponDef { Name = "", Class = WeaponClass.AssaultRifle, Damage = 1, RangeM = 1, Rpm = 1, Mag = 1, Reload = 1, HipSpreadDeg = 1 };
        w.Pellets = cls.Calibre.Pellets;
        foreach (var n in receiver.Walk().Where(n => n.Part.Category is PartCategory.Receiver or PartCategory.Ammo or PartCategory.Feed))
        {
            if (n.Part.Category == PartCategory.Ammo)
                ApplyAmmo(n.Part, w, cls);
            else
                foreach (var e in n.Part.Effects.Where(e => e.Field.Name == nameof(WeaponDef.Pellets)))
                    e.ApplyTo(w);
        }
        var list = new List<int>();
        foreach (var leaf in Leaves(receiver))
        {
            var c = w.Clone();
            if (cls.Pellets)
                foreach (var e in leaf.Barrel.Part.Effects.Where(e => e.Field.Name == nameof(WeaponDef.Pellets)))
                    e.ApplyTo(c);
            foreach (var (s, dev) in leaf.Barrel.Children())
                if (s.Type == "muzzle")
                    foreach (var e in dev.Part.Effects.Where(e => e.Field.Name == nameof(WeaponDef.Pellets)))
                        e.ApplyTo(c);
            list.Add(Math.Clamp(c.Pellets, 1, 30));
        }
        return list;
    }

    /// <summary>A receiver's barrel muzzles (in socket order), with the splitter multiplier and splay on the way to each.</summary>
    public static List<BarrelLeaf> Leaves(BuildNode receiver)
    {
        var list = new List<BarrelLeaf>();
        Collect(receiver, 1f, 0f, 0f, 1f, list);
        // A receiver with several barrel sockets (the rotary clusters) fires them in turn with a rotary action; with any
        // other action they all fire at once, and like a splitter each gets 1/N of the shot (the total stays the same).
        if (receiver.Part.Receiver != null && WeaponBuild.ActionOf(receiver) is { Kind: not ActionKind.Rotary })
        {
            var filled = receiver.Children().Count(c => c.Socket.Type == "barrel");
            if (filled > 1)
                for (var i = 0; i < list.Count; i++)
                    list[i] = list[i] with { SplitMult = list[i].SplitMult / filled };
        }
        return list;
    }

    private static void Collect(BuildNode n, float mult, float yaw, float pitch, float spread, List<BarrelLeaf> into)
    {
        if (n.Part.Category == PartCategory.Barrel)
        {
            for (var i = 0; i < n.Part.Muzzles.Length; i++)
                into.Add(new BarrelLeaf(n, i, mult, yaw, spread, pitch));
            return;
        }
        var split = n.Part.Split;
        var outs = n.Part.Sockets.Where(s => s.Type == "barrel").ToList();
        // A row of outputs (every output at one height: the Y, Trident, Peacock, Hydra) fans sideways from -splay to +splay
        // across its sockets in order, as it always has. Any other layout (over-under stacks, grids, crosses, rosettes)
        // fans outward from the outputs' centre in both directions: each output turns by splay x its offset from the centre
        // over the farthest output's, so a vertical stack fans up and down and a rosette opens like a flower.
        // An offset splitter (one output) only moves its barrel: no splay, no split (1/1 of the shot).
        var row = outs.Count > 1 && outs.All(o => MathF.Abs(o.Pos.Y - outs[0].Pos.Y) < 1e-4f);
        var centre = Vector2.Zero;
        foreach (var o in outs)
            centre += new Vector2(o.Pos.X, o.Pos.Y);
        centre /= Math.Max(1, outs.Count);
        var reach = 0f;
        foreach (var o in outs)
            reach = MathF.Max(reach, (new Vector2(o.Pos.X, o.Pos.Y) - centre).Length());
        for (var i = 0; i < outs.Count; i++)
        {
            if (n.Child(outs[i].Name) is not { } child)
                continue;
            // Splitters conserve damage: each output gets 1/N of the shot (nested splitters divide again).
            var m = split != null ? mult * SplitInfo.OutputMult(n.Part) : mult;
            var (y, p) = (yaw, pitch);
            if (split != null && outs.Count > 1 && split.SplayDeg != 0)
            {
                if (row)
                    y += split.SplayDeg * (2f * i / (outs.Count - 1) - 1f);
                else if (reach > 1e-5f)
                {
                    y += split.SplayDeg * (outs[i].Pos.X - centre.X) / reach;
                    p += split.SplayDeg * (outs[i].Pos.Y - centre.Y) / reach;
                }
            }
            Collect(child, m, y, p, split != null ? spread * split.SpreadMult : spread, into);
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

    private static WeaponDef BaseDef(string name, ReceiverInfo r, ActionInfo a)
    {
        var w = WeaponDef.Blank();
        w.Name = name;
        w.Class = r.Class;
        // The receiver sets the calibre: its bore's round (damage, range, pellets, fall-off, headshots), before its own
        // stats and the ammo part's relative modifiers.
        var cal = Parts.Registry.ClassOf(r.Bore).Calibre;
        w.Damage = cal.Damage;
        w.RangeM = cal.RangeM;
        w.Pellets = cal.Pellets;
        w.MinDamageMult = cal.MinDamageMult;
        w.HeadMult = cal.HeadMult;
        w.Rpm = a.Rpm;
        w.Mag = 30;
        w.Reload = 2.2f;
        w.HipSpreadDeg = 2f;
        w.AdsZoom = 1.25f;
        w.Mode = ModeOf(a.Kind);
        w.BurstCount = a.BurstCount;
        w.BurstDelay = a.BurstDelay;
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

    /// <summary>The trigger behaviour of a fire type: burst, auto (auto, rotary, chain), or one shot per pull (the rest).</summary>
    public static FireMode ModeOf(ActionKind k) => k switch
    {
        ActionKind.Burst => FireMode.Burst,
        ActionKind.Auto or ActionKind.Rotary or ActionKind.Chain => FireMode.Auto,
        _ => FireMode.Semi,
    };

    /// <summary>
    /// The build's fire type in words, from its receivers' action parts: "3-round burst (Ratchet Burst Group)", or each
    /// distinct one for several receivers ("full auto x2, pump"). "no action" for a receiver missing one.
    /// </summary>
    public static string FireTypeText(WeaponBuild b)
    {
        var rs = b.Receivers;
        if (rs.Count == 0)
            return "no receiver";
        if (rs.Count == 1)
            return WeaponBuild.ActionNodeOf(rs[0]) is { } an ? $"{an.Part.Action!.FireType} ({an.Part.Name})" : "no action";
        return string.Join(", ", rs.Select(r => WeaponBuild.ActionOf(r)?.FireType ?? "no action").GroupBy(x => x)
            .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
    }

    /// <summary>One receiver's numbers on its own, and its barrels (damage per barrel as a float, pellets, hip spread, splay).</summary>
    public sealed class ReceiverResult
    {
        public required BuildNode Node;
        public required WeaponDef Def;

        /// <summary>Fed by the build's shared feed (its magazine is the pool, shared with the other receivers it feeds).</summary>
        public bool SharedFed;

        /// <summary>Its action part's numbers (fire type, rate, spool-up, chamber).</summary>
        public ActionInfo Action = NoAction;
        public readonly List<(BarrelLeaf Leaf, float Damage, int Pellets, float HipSpread, float Size)> Barrels = new();
    }

    /// <summary>Runs the per-receiver part of the pipeline (stages, then barrel paths).</summary>
    public static ReceiverResult ReceiverStats(string name, BuildNode receiver) => ReceiverStats(name, receiver, null);

    /// <summary>
    /// <inheritdoc cref="ReceiverStats(string, BuildNode)"/> A receiver fed by a shared feed (<paramref name="hopper"/>)
    /// takes the shared feed's effects as its feed stage, and the shared feed's ammo when it has none of its own.
    /// </summary>
    public static ReceiverResult ReceiverStats(string name, BuildNode receiver, BuildNode? hopper)
    {
        var r = receiver.Part.Receiver!;
        var act = WeaponBuild.ActionOf(receiver) ?? NoAction;
        var w = BaseDef(name, r, act);
        var nodes = receiver.Walk().ToList();
        if (hopper != null)
        {
            nodes.Add(hopper);
            if (receiver.Child("ammo") == null && hopper.Child("ammo") is { } sharedAmmo)
                nodes.Add(sharedAmmo);
        }
        var cls = Parts.Registry.ClassOf(r.Bore);
        foreach (var cat in ReceiverStages)
            foreach (var n in nodes.Where(n => n.Part.Category == cat))
                if (cat == PartCategory.Ammo)
                    ApplyAmmo(n.Part, w, cls);
                else
                    Apply(n.Part, w);
        // Receiver upgrades change the fire rate as a ratio, so rotary receivers (rate per barrel) scale the same way.
        var rateMult = w.Rpm / (float)Math.Max(1, act.Rpm);
        var res = new ReceiverResult { Node = receiver, Def = w };
        var copies = new List<WeaponDef>();
        foreach (var leaf in Leaves(receiver))
        {
            var c = w.Clone();
            ApplyBarrel(leaf.Barrel.Part, c, cls);
            foreach (var (s, dev) in leaf.Barrel.Children())
                if (s.Type == "muzzle")
                    Apply(dev.Part, c);
            // Hitscan rounds: a faster round carries its full damage further, a slower one drops off sooner (range x speed).
            c.BulletSpeed = Math.Clamp(c.BulletSpeed, WeaponDef.MinBulletSpeed, WeaponDef.MaxBulletSpeed);
            c.BulletSize = Math.Clamp(c.BulletSize, WeaponDef.MinBulletSize, WeaponDef.MaxBulletSize);
            c.RangeM *= c.BulletSpeed;
            copies.Add(c);
            res.Barrels.Add((leaf, c.Damage * leaf.SplitMult, Math.Clamp(c.Pellets, 1, 30), c.HipSpreadDeg * leaf.SpreadMult, c.BulletSize));
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
        if (act.RpmPerBarrel > 0)
            w.Rpm = Math.Min(3000, (int)MathF.Round(act.RpmPerBarrel * Math.Max(1, copies.Count) * rateMult));
        // A single-shot or muzzle-loaded action holds its chamber's worth whatever the feed holds.
        if (act.Chamber > 0)
            w.Mag = Math.Min(w.Mag, act.Chamber);
        res.SharedFed = hopper != null;
        res.Action = act;
        return res;
    }

    /// <summary>
    /// The build's weapon stats, or null with <paramref name="problems"/> filled if it isn't complete and legal. The model
    /// (and with it the muzzle positions and hand anchors) is added by <see cref="WeaponBuilder.Assemble"/>.
    /// </summary>
    public static WeaponDef? Compute(WeaponBuild b, out List<BuildProblem> problems)
    {
        problems = BuildRules.Check(b);
        return problems.Count > 0 || b.IsMelee ? null : ComputeUnchecked(b); // melee builds: MeleeStats
    }

    /// <summary>The stats of a complete build (no rule checks). Muzzle positions are zero until the model is built.</summary>
    public static WeaponDef ComputeUnchecked(WeaponBuild b)
    {
        var grip = b.Root!;
        var frame = grip.Part.Frame!;
        var style = b.EffectiveStyle;
        var name = WeaponBuild.CleanName(b.Name);
        var fed = b.HopperFed();
        var hopper = b.Hopper;
        var per = b.Receivers.Select(r => ReceiverStats(name, r, fed.Contains(r) ? hopper : null)).ToList();
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
        // Magazines: per-receiver feeds as before (volley: the smallest, they run dry together; rotate: they add up). A
        // shared feed is one pool: in a volley each pull takes a round per receiver it feeds, so it lasts pool / k pulls;
        // rotating, each shot takes one round, and the pool counts once.
        // A chambered action (single shot, muzzle-loaded) holds its own round(s): it doesn't draw on the pool.
        var own = per.Where(p => !p.SharedFed || p.Action.Chamber > 0).Select(p => p.Def.Mag).ToList();
        var pooled = per.Where(p => p.SharedFed && p.Action.Chamber == 0).ToList();
        var pool = pooled.Count > 0 ? pooled[0].Def.Mag : 0;
        if (rotate)
            w.Mag = Math.Min(1000, own.Sum() + pool);
        else
        {
            if (pooled.Count > 0)
                own.Add(Math.Max(1, pool / pooled.Count));
            w.Mag = own.Min();
        }
        w.Reload = defs.Max(d => d.Reload) + frame.ReloadPerReceiver * (count - 1);
        w.RecoilDeg = rotate ? defs.Average(d => d.RecoilDeg) : MathF.Sqrt(defs.Sum(d => d.RecoilDeg * d.RecoilDeg));
        w.Loudness = defs.Max(d => d.Loudness); // the loudest receiver's muzzle devices set the report
        foreach (var f in Averaged)
        {
            var fi = typeof(WeaponDef).GetField(f)!;
            fi.SetValue(w, defs.Average(d => (float)fi.GetValue(d)!));
        }
        var barrels = per.SelectMany((p, g) => p.Barrels.Select(x => (Group: g, x.Leaf, x.Damage, x.Pellets, x.HipSpread, x.Size, Rotary: p.Action.Kind == ActionKind.Rotary))).ToList();
        var meanHip = barrels.Count > 0 ? barrels.Average(x => x.HipSpread) : w.HipSpreadDeg;
        w.HipSpreadDeg = meanHip;
        var maxDamage = barrels.Count > 0 ? barrels.Max(x => x.Damage) : w.Damage;
        w.Damage = Math.Max(1, (int)MathF.Round(maxDamage));
        w.Pellets = barrels.Count > 0 ? barrels[0].Pellets : w.Pellets;
        var spooling = per.Where(p => p.Action.SpoolTime > 0).Select(p => p.Action).ToList();
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
            w.MuzzlePoints = barrels.Select(x => new BarrelPoint(Vector3.Zero, x.Leaf.SplayDeg, x.Leaf.PitchDeg, x.Group, x.Pellets, x.Damage / MathF.Max(1, w.Damage),
                x.HipSpread / MathF.Max(0.01f, meanHip), x.Rotary, x.Size)).ToArray();
            w.RotateGroups = rotate;
        }

        // ---- Weight and the frame ----
        var weight = b.Weight;
        var over = MathF.Max(0, weight - b.CarryWeight); // weight relief parts raise what the grip carries
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

    /// <summary>
    /// An ammo part on top of its receiver's calibre (docs/zombies/WEAPON-BUILDER.md, "Universal ammo"): ammo fits every
    /// receiver and only changes the calibre's numbers relatively. Its "round" block first splits the round into N
    /// projectiles sharing the damage (buckshot on a rifle: eight pellets of an eighth; a slug in a shotgun: the eight
    /// pellets as one), then its stats apply (mul / add); a stat on the pellet count only counts where the bore lets
    /// barrels change pellets (shotguns), like a barrel's.
    /// </summary>
    public static void ApplyAmmo(PartDef ammo, WeaponDef w, BoreClass cls)
    {
        if (ammo.Round is { Pellets: > 0 } rd && rd.Pellets != w.Pellets)
        {
            var was = Math.Max(1, w.Pellets);
            w.Damage = Math.Max(1, (int)MathF.Round(w.Damage * was / (float)rd.Pellets));
            w.Pellets = rd.Pellets;
        }
        foreach (var e in ammo.Effects)
        {
            if (e.Field.Name == nameof(WeaponDef.Pellets) && !cls.Pellets)
                continue;
            e.ApplyTo(w);
        }
    }

    /// <summary>
    /// A barrel's modifiers on top of its receiver's calibre (docs/zombies/WEAPON-BUILDER.md, "Barrels and receivers"):
    /// barrels only scale what the receiver and its ammo set (a Long Barrel's x1.4 range is x1.4 of a pistol's range on a
    /// pistol, of an autocannon's on an autocannon). The bore class's strength scales each relative change (x m becomes
    /// x (1 + (m - 1) k), + a becomes + a k; "set" is applied as written), and pellet changes count only where the class
    /// allows them (shotguns).
    /// </summary>
    public static void ApplyBarrel(PartDef barrel, WeaponDef w, BoreClass cls)
    {
        foreach (var e in barrel.Effects)
        {
            if (e.Field.Name == nameof(WeaponDef.Pellets) && !cls.Pellets)
                continue;
            if (MathF.Abs(cls.Strength - 1f) < 1e-4f || e.Op == "set" || e.Value is not float v)
            {
                e.ApplyTo(w);
                continue;
            }
            var k = cls.Strength;
            var scaled = e.Op == "add" ? v * k : 1f + (v - 1f) * k;
            new AttachmentEffect { Field = e.Field, Op = e.Op, Value = scaled, Min = e.Min, Max = e.Max }.ApplyTo(w);
        }
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
        w.Loudness = Math.Clamp(w.Loudness, WeaponDef.MinLoudness, WeaponDef.MaxLoudness);
        w.BulletSize = Math.Clamp(w.BulletSize, WeaponDef.MinBulletSize, WeaponDef.MaxBulletSize);
        w.BulletSpeed = Math.Clamp(w.BulletSpeed, WeaponDef.MinBulletSpeed, WeaponDef.MaxBulletSpeed);
        w.Pellets = Math.Clamp(w.Pellets, 1, 30);
    }
}
