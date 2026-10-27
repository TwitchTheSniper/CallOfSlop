namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The parts the splitters, barrels and muzzles pass added (2026-10-27): every one is in a standard build.</summary>
    private static readonly string[] MechanicsParts =
    [
        "Narrow Twin Splitter", "Wide Y Splitter", "Diagonal Splitter", "Stepped Splitter", "Tower Splitter", "Pyramid Splitter",
        "Quad Line Splitter", "Totem Splitter", "Cross Splitter", "Ladder Splitter", "Rosette Splitter", "Wall Splitter",
        "Riser Offset", "Drop Offset", "Sidestep Offset", "Dogleg Offset", "Canted Offset",
        "Heavy Bore Barrel", "Bloat Barrel", "Cannon Sleeve", "Squeeze Bore Barrel", "Needle Barrel", "Flechette Sleeve",
        "Sawn-Off Barrel", "Ported Barrel", "Heatsink Barrel", "Marksman Barrel",
        "Recoil Eater", "Dragon Mouth", "Velocity Booster", "Long Choke", "Fork Crown", "Tumbler Ring", "Hush Baffles",
    ];

    /// <summary>The fewest standard builds a tier holds (common .. wonder): guns, then melee builds.</summary>
    private static readonly int[] MinTierGuns = [8, 15, 20, 18, 18, 18];
    private static readonly int[] MinTierMelee = [3, 5, 6, 4, 4, 5];

    /// <summary>
    /// The standard build set (docs/zombies/WEAPON-BUILDER.md, "The prefab builds"): every file loads, the names are
    /// unique (also ignoring case, spaces and punctuation), each tier holds enough guns and melee builds, every part the
    /// splitters/barrels/muzzles and late-game passes added is in at least one build, the descriptions are short, and the
    /// power rises with the tier (the median gun and melee power of each tier above the tier below; every wonder box gun
    /// stronger than the median epic one). Then the damage cap: builds have none (developer, 2026-10-27), weapon files
    /// keep 1000.
    /// </summary>
    private static void CheckStandardBuildSet()
    {
        var all = StandardBuilds.All;
        foreach (var e in StandardBuilds.LoadErrors)
            Fail($"standard builds: {e}");
        string Key(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        foreach (var dup in all.GroupBy(b => Key(b.Name)).Where(g => g.Count() > 1))
            Fail($"standard builds: names too alike: {string.Join(", ", dup.Select(b => b.Name))}");
        var gunPower = new List<float>[6];
        var meleePower = new List<float>[6];
        for (var i = 0; i < 6; i++)
        {
            gunPower[i] = new List<float>();
            meleePower[i] = new List<float>();
        }
        var atCap = new List<string>();
        var wonderBox = new List<(string Name, float Power)>();
        foreach (var b in all)
        {
            var build = b.Build();
            var a = WeaponBuilder.Assemble(build);
            var t = (int)b.Tier;
            var src = string.Join("/", b.Sources);
            if (b.Flavor.Length > 160)
                Fail($"standard build '{b.Name}': its description is {b.Flavor.Length} characters (160 at most: a short line)");
            if (a.Melee is { } m)
            {
                var p = PowerCurve.MeleePower(m);
                meleePower[t].Add(p);
                Line($"{b.Tier} [{src}] '{b.Name}': melee {F(m.Damage)} dmg, {F(m.Swing)} s, cleave {m.Cleave}, stun {F(m.Stun)} s, {F(m.Dps)} DPS, power {p:0}");
            }
            else if (a.Def is { } d)
            {
                var p = PowerCurve.GunPower(d);
                gunPower[t].Add(p);
                if (d.Damage > 1000)
                    atCap.Add($"{b.Name} ({d.Damage})");
                if (b.Tier == Rarity.Wonder && b.Sources.Contains("box"))
                    wonderBox.Add((b.Name, p));
                Line($"{b.Tier} [{src}] '{b.Name}': {build.Receivers.Count} receiver(s), {d.Damage} a projectile, {F(d.ShotDamage)} a pull, {d.Rpm} rpm {d.ModeName}, {F(d.Dps)} DPS, mag {d.Mag}, power {p:0} [{string.Join(",", d.BuildFlags)}]");
            }
            else
                Fail($"standard build '{b.Name}' doesn't assemble");
        }
        Line($"{all.Length} standard builds: " + string.Join(", ", Enum.GetValues<Rarity>().Select(r => $"{r} {gunPower[(int)r].Count} guns + {meleePower[(int)r].Count} melee")));
        for (var t = 0; t < 6; t++)
            if (gunPower[t].Count < MinTierGuns[t] || meleePower[t].Count < MinTierMelee[t])
                Fail($"standard builds: {(Rarity)t} has {gunPower[t].Count} guns and {meleePower[t].Count} melee builds (want {MinTierGuns[t]} and {MinTierMelee[t]} or more)");

        // ---- Every new part is in a build ------------------------------------------------------------------------------
        var used = all.SelectMany(b => b.Build().Nodes.Select(n => n.Part.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unused = MechanicsParts.Concat(LateGameParts).Where(n => !used.Contains(n)).ToList();
        Line($"new parts in standard builds: {MechanicsParts.Length + LateGameParts.Length - unused.Count} of {MechanicsParts.Length + LateGameParts.Length}");
        if (unused.Count > 0)
            Fail($"standard builds: no build uses {string.Join(", ", unused)}");
        foreach (var n in MechanicsParts.Where(n => Parts.Find(n) == null))
            Fail($"standard builds: the part '{n}' is gone (update the list)");

        // ---- Power rises with the tier ---------------------------------------------------------------------------------
        static float Median(List<float> v)
        {
            if (v.Count == 0)
                return 0;
            var s = v.OrderBy(x => x).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }
        Line("median power by tier: guns " + string.Join(", ", Enumerable.Range(0, 6).Select(t => $"{(Rarity)t} {Median(gunPower[t]):0}"))
             + "; melee " + string.Join(", ", Enumerable.Range(0, 6).Select(t => $"{(Rarity)t} {Median(meleePower[t]):0}")));
        for (var t = 1; t < 6; t++)
        {
            if (Median(gunPower[t]) <= Median(gunPower[t - 1]))
                Fail($"standard builds: the median {(Rarity)t} gun ({Median(gunPower[t]):0}) should be stronger than the median {(Rarity)(t - 1)} one ({Median(gunPower[t - 1]):0})");
            if (Median(meleePower[t]) <= Median(meleePower[t - 1]))
                Fail($"standard builds: the median {(Rarity)t} melee build ({Median(meleePower[t]):0}) should be stronger than the median {(Rarity)(t - 1)} one ({Median(meleePower[t - 1]):0})");
        }
        var epic = Median(gunPower[(int)Rarity.Epic]);
        foreach (var (name, p) in wonderBox.Where(w => w.Power <= epic))
            Fail($"standard builds: the wonder box gun '{name}' (power {p:0}) is no stronger than the median epic gun ({epic:0})");

        // ---- Late builds stack: every receiver takes every multiplier and upgrade in one piece ------------------------------
        var stackFits = 0;
        foreach (var rec in Parts.InCategory(PartCategory.Receiver))
            foreach (var add in Parts.InCategory(PartCategory.Multiplier).Concat(Parts.InCategory(PartCategory.Upgrade)))
                foreach (var sock in rec.Sockets.Where(s => s.Type == add.Mount))
                {
                    var tb = new WeaponBuild("Stack Fit", Parts.Find("Rifle Grip")!);
                    tb.Root!.Attach("receiver_1", rec).Attach(sock.Name, add);
                    CheckBuildConnected($"{add.Name} in the {rec.Name}'s {sock.Name}", tb);
                    stackFits++;
                }
        Line($"stacking: {stackFits} receiver x multiplier/upgrade placements checked for floating boxes");

        // ---- No damage cap for builds; weapon files keep theirs ---------------------------------------------------------
        Line($"over 1000 a projectile (builds have no cap): {(atCap.Count == 0 ? "none" : string.Join(", ", atCap))}");
        var stacked = WeaponBuild.Parse("Cap Test|grip|Rifle Grip{receiver_1:Bunker Buster Receiver{barrel:Cannon Sleeve,feed:Heavy Box,ammo:Depleted Core,action:Thunderclap Hammer,amp:Five-Alarm Chamber,upgrade_rear:Doom Hammer,upgrade_side:Overkill Coprocessor}}", Parts.Find);
        var sd = WeaponBuilder.Assemble(stacked).Def;
        var refused = false;
        try
        {
            var wf = WeaponFile.ToJson(Weapons.DefaultPrimary).Replace($"\"damage\": {Weapons.DefaultPrimary.Damage},", "\"damage\": 5000,");
            WeaponFile.Load(wf, "cap test");
        }
        catch (WeaponFileException ex) when (ex.Message.Contains("damage"))
        {
            refused = true;
        }
        Line($"damage cap: a stacked Bunker Buster build does {sd?.Damage} a projectile; a weapon file with 5000 damage refused {refused} (cap {WeaponFile.Ranges[nameof(WeaponDef.Damage)].Max})");
        if (sd == null || sd.Damage <= 1000 || sd.Damage > BuildStats.MaxDamage)
            Fail("damage cap: a stacked late build should do more than 1000 a projectile (builds have no damage cap)");
        if (!refused || WeaponFile.Ranges[nameof(WeaponDef.Damage)].Max != 1000)
            Fail("damage cap: weapon files should still be capped at 1000 damage");
    }
}
