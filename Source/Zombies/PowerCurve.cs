namespace CallOfSlop;

/// <summary>
/// The rarity power curve (docs/zombies/WEAPON-BUILDER.md, "The power curve"): one number per part that says how strong
/// it is next to the other parts of its category, measured with the real stat pipeline, so a rarer part's ceiling can be
/// checked against the commoner ones. Every gun part is fitted to a reference gun (a Workhorse on the Rifle Grip, standard
/// barrel, box mag, FMJ, full-auto sear) and scored by how much it changes the gun's <see cref="GunPower"/>; receivers by
/// their damage per trigger pull; root grips by receiver slots and carry weight; melee parts by <see cref="MeleePower"/>.
/// Only the self-test and the docs use it (nothing in a match does), so it may allocate.
/// </summary>
public static class PowerCurve
{
    /// <summary>
    /// The curve's floors: the best part of a tier is at least this many times the best common part of its category
    /// (damage rows), and never weaker than the best part of the tier below (every row). Common to wonder.
    /// </summary>
    public static readonly float[] DamageFloor = [1f, 1f, 1.1f, 1.3f, 1.6f, 2.2f];

    /// <summary>A row of the curve: a category (grips split into root grips and foregrips) and what its score measures.</summary>
    public sealed record Row(string Name, string Measures, bool Damage, Func<PartDef, bool> Has);

    public static readonly Row[] Rows =
    [
        new("Receiver", "damage per trigger pull (FMJ, standard barrel, its stock action)", true, p => p.Category == PartCategory.Receiver),
        new("Action", "gun power x on the reference rifle", true, p => p.Category == PartCategory.Action),
        new("Ammo", "gun power x on the reference rifle", true, p => p.Category == PartCategory.Ammo),
        new("Multiplier", "gun power x on the reference rifle", true, p => p.Category == PartCategory.Multiplier),
        new("Upgrade", "gun power x on the reference rifle", true, p => p.Category == PartCategory.Upgrade),
        new("Feed", "gun power x on its bore's commonest receiver, against a common feed (shared feeds x root of receivers fed)", false, p => p.Category == PartCategory.Feed),
        new("Grip", "receiver slots x root of carry weight (root grips)", false, p => p.Category == PartCategory.Grip && p.IsRoot),
        new("Foregrip", "gun power x on the reference rifle", false, p => p.Category == PartCategory.Grip && !p.IsRoot),
        new("Stock", "gun power x on the reference rifle, +2% per kg of carry", false, p => p.Category == PartCategory.Stock),
        new("Optic", "gun power x on the reference rifle (zoom counts as reach)", false, p => p.Category == PartCategory.Optic),
        new("Utility", "gun power x on the reference rifle, +2% per kg of carry", false, p => p.Category == PartCategory.Utility),
        new("Hilt", "melee power with a machete blade", true, p => p.Category == PartCategory.Hilt),
        new("Head", "melee power on a wooden handle", true, p => p.Category == PartCategory.Head),
        new("Melee Mod", "melee power x on a machete", true, p => p.Category == PartCategory.MeleeMod),
    ];

    /// <summary>What a flag is worth in the score (its Zombies effect, roughly). Flags not listed count 1.</summary>
    public static readonly Dictionary<string, float> FlagWorth = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explosive"] = 1.3f, ["incendiary"] = 1.2f, ["shock"] = 1.25f, ["plasma"] = 1.15f, ["penetrating"] = 1.15f, ["bouncy"] = 1.15f,
        ["toasty"] = 1.2f, ["party"] = 1.05f, ["refreshing"] = 1.15f, ["rocket"] = 1.2f, ["launch"] = 1.4f, ["marks"] = 1.3f,
        ["squeaky"] = 1.05f, ["fish"] = 1.1f, ["bonk"] = 1.1f, ["nails"] = 1.1f, ["chainsaw"] = 1.15f, ["bayonet"] = 1.05f, ["hop"] = 1.2f,
    };

    private static float Flags(IEnumerable<string> flags)
    {
        var m = 1f;
        foreach (var f in flags.Distinct(StringComparer.OrdinalIgnoreCase))
            m *= FlagWorth.GetValueOrDefault(f, 1f);
        return m;
    }

    /// <summary>
    /// A gun's overall power: sustained body damage per second (a magazine and its reload), headshots counted a quarter of
    /// the time, accuracy (hip and aimed spread, recoil), reach (range times zoom), handling (mobility, aim time).
    /// </summary>
    public static float GunPower(WeaponDef w, IEnumerable<string>? flags = null)
    {
        var dps = w.Dps;
        if (dps <= 0)
            return 0;
        var magTime = w.Mag * w.ShotDamage / dps;
        var sustained = dps * magTime / (magTime + w.Reload);
        var head = 0.75f + 0.25f * w.HeadMult / 2f;
        var accuracy = 1f / (1f + 0.04f * w.HipSpreadDeg + 0.06f * w.HipSpreadDeg * w.AdsSpreadMult + 0.03f * w.RecoilDeg);
        var reach = MathF.Pow(Math.Clamp(MathF.Min(w.RangeM, 200f) * w.AdsZoom / 35f, 0.05f, 50f), 0.25f);
        var handling = MathF.Sqrt(Math.Max(0.1f, w.Mobility)) / (1f + 0.5f * w.AdsTime);
        return sustained * head * accuracy * reach * handling * Flags(flags ?? w.BuildFlags ?? []);
    }

    /// <summary>A melee weapon's power: damage per second, with cleave, stun and reach counted, and its flags.</summary>
    public static float MeleePower(MeleeDef m) =>
        m.Dps * (1f + 0.3f * (m.Cleave - 1)) * (1f + 0.4f * m.Stun) * MathF.Sqrt(m.Reach / 2f) * Flags(m.Flags);

    private static PartDef P(string name) => Parts.Find(name) ?? throw new InvalidOperationException($"the power curve needs the part '{name}'");

    private static WeaponDef? Stats(WeaponBuild b) => BuildStats.Compute(b, out _);

    /// <summary>The reference rifle: a Workhorse on the Rifle Grip (standard barrel, box mag, FMJ, full-auto sear), with <paramref name="tweak"/> applied.</summary>
    public static WeaponBuild Reference(Action<BuildNode, BuildNode>? tweak = null)
    {
        var b = new WeaponBuild("Power Reference", P("Rifle Grip"));
        var r = b.Root!.Attach("receiver_1", P("Workhorse Receiver"));
        r.Attach("barrel", P("Standard Barrel"));
        r.Attach("feed", P("Box Mag"));
        r.Attach("ammo", P("FMJ"));
        r.Attach("action", P("Full-Auto Sear"));
        tweak?.Invoke(b.Root!, r);
        return b;
    }

    private static float RefPower(Action<BuildNode, BuildNode>? tweak = null) => Stats(Reference(tweak)) is { } w ? GunPower(w) : 0;

    /// <summary>A part's score in its row (see <see cref="Rows"/>), or null if it can't be measured (it doesn't fit the reference).</summary>
    public static float? Score(PartDef p)
    {
        switch (p.Category)
        {
            case PartCategory.Receiver: return ReceiverScore(p);
            case PartCategory.Feed: return FeedScore(p);
            case PartCategory.Grip when p.IsRoot:
                return p.ReceiverCapacity * MathF.Sqrt(p.Frame!.Carry);
            case PartCategory.Hilt:
                return MeleeOf(p, P("Machete Blade"), null) is { } mh ? MeleePower(mh) : null;
            case PartCategory.Head:
                return MeleeOf(P("Wooden Handle"), p, null) is { } md ? MeleePower(md) : null;
            case PartCategory.MeleeMod:
                return MeleeOf(P("Wooden Handle"), P("Machete Blade"), p) is { } mm && MeleeOf(P("Wooden Handle"), P("Machete Blade"), null) is { } m0
                    ? MeleePower(mm) / MeleePower(m0) : null;
        }
        var socket = p.Category switch
        {
            PartCategory.Action => "action",
            PartCategory.Ammo => "ammo",
            PartCategory.Multiplier => "amp",
            PartCategory.Upgrade => "upgrade_rear",
            PartCategory.Utility => "utility",
            PartCategory.Grip => "foregrip",
            PartCategory.Stock => "stock",
            PartCategory.Optic => "optic",
            _ => null,
        };
        if (socket == null)
            return null;
        var onGrip = p.Category is PartCategory.Grip or PartCategory.Stock or PartCategory.Optic;
        var b = Reference((g, r) => (onGrip ? g : r).Attach(socket, p));
        if (Stats(b) is not { } w)
            return null;
        var s = GunPower(w) / RefPower();
        return s * (1f + 0.02f * p.CarryBonus);
    }

    /// <summary>The commonest single-barrel receiver of <paramref name="bore"/> (the feed curve's test bed).</summary>
    public static PartDef? BoreReference(string bore) => Parts.InCategory(PartCategory.Receiver)
        .Where(r => r.Receiver!.Bore.Equals(bore, StringComparison.OrdinalIgnoreCase) && r.Receiver.MinBarrels <= 1)
        .OrderBy(r => r.Rarity).ThenBy(r => r.Name, StringComparer.Ordinal).FirstOrDefault();

    private static readonly Dictionary<ActionKind, string> ActionFor = new()
    {
        [ActionKind.Auto] = "Full-Auto Sear", [ActionKind.Chain] = "Chain Drive Motor", [ActionKind.Rotary] = "Electric Rotary Drive",
    };

    /// <summary>A receiver of the bore with an action <paramref name="feed"/> works with and that feed (or a shared feed on the grip's hopper).</summary>
    private static WeaponBuild? FeedBed(PartDef receiver, PartDef feed, PartDef? actionFor)
    {
        var stock = ActionMigration.StockActionOf(receiver, Parts.Find);
        var need = actionFor?.RequiresAction ?? [];
        var action = stock != null && (need.Length == 0 || need.Contains(stock.Action!.Kind)) ? stock : need.Length > 0 && ActionFor.TryGetValue(need[0], out var an) ? P(an) : stock;
        if (action == null)
            return null;
        var b = new WeaponBuild("Feed Bed", P("Rifle Grip"));
        var r = b.Root!.Attach("receiver_1", receiver);
        r.Attach("barrel", P("Standard Barrel"));
        r.Attach("ammo", P("FMJ"));
        r.Attach("action", action);
        if (feed.Shared != null)
            b.Root.Attach("hopper", feed);
        else
            r.Attach("feed", feed);
        return b;
    }

    private static float? FeedScore(PartDef f)
    {
        float? best = null;
        foreach (var bore in f.Bores.Length > 0 ? f.Bores : ["rifle"])
        {
            if (BoreReference(bore) is not { } rec)
                continue;
            var baseFeed = Parts.InCategory(PartCategory.Feed).Where(x => x.Shared == null && x.RequiresAction.Length == 0 && x.FitsBore(bore))
                .OrderBy(x => x.Rarity).ThenBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault();
            if (baseFeed == null || FeedBed(rec, f, f) is not { } withF || FeedBed(rec, baseFeed, f) is not { } withBase)
                continue;
            if (Stats(withF) is not { } a || Stats(withBase) is not { } b)
                continue;
            var s = GunPower(a) / GunPower(b) * (f.Shared != null ? MathF.Sqrt(f.Shared.Feeds) : 1f);
            best = best is { } x ? MathF.Max(x, s) : s;
        }
        return best;
    }

    /// <summary>A receiver's damage per trigger pull: FMJ, a standard barrel in every barrel socket it needs, its stock action, a feed of its bore.</summary>
    public static float? ReceiverScore(PartDef rec)
    {
        if (rec.Receiver is not { } info || ActionMigration.StockActionOf(rec, Parts.Find) is not { } action)
            return null;
        var feed = Parts.InCategory(PartCategory.Feed).Where(x => x.Shared == null && x.FitsBore(info.Bore) && (x.RequiresAction.Length == 0 || x.RequiresAction.Contains(action.Action!.Kind)))
            .OrderBy(x => x.Rarity).ThenBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault();
        if (feed == null)
            return null;
        var b = new WeaponBuild("Receiver Bed", P("Rifle Grip"));
        var r = b.Root!.Attach("receiver_1", rec);
        var barrels = rec.Sockets.Where(s => s.Type == "barrel").Take(Math.Max(1, info.MinBarrels)).ToList();
        foreach (var s in barrels)
            r.Attach(s.Name, P("Standard Barrel"));
        r.Attach("feed", feed);
        r.Attach("ammo", P("FMJ"));
        r.Attach("action", action);
        if (Stats(b) is not { } w)
            return null;
        return w.Damage * w.Pellets * Flags(rec.Flags);
    }

    private static MeleeDef? MeleeOf(PartDef hilt, PartDef head, PartDef? mod)
    {
        var b = new WeaponBuild("Melee Bed", hilt);
        b.Root!.Attach("head", head);
        if (mod != null)
            b.Root.Attach("mod_1", mod);
        if (BuildRules.Check(b).Count > 0)
            return null;
        return MeleeStats.Compute(b, WeaponDef.Blank());
    }

    /// <summary>Every row's parts with their scores, by tier (unmeasurable parts left out).</summary>
    public static List<(Row Row, List<(PartDef Part, float Score)>[] Tiers)> Table()
    {
        var list = new List<(Row, List<(PartDef, float)>[])>();
        foreach (var row in Rows)
        {
            var tiers = Enumerable.Range(0, 6).Select(_ => new List<(PartDef, float)>()).ToArray();
            foreach (var p in Parts.All.Where(row.Has))
                if (Score(p) is { } s)
                    tiers[(int)p.Rarity].Add((p, s));
            foreach (var t in tiers)
                t.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            list.Add((row, tiers));
        }
        return list;
    }
}
