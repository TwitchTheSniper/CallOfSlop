using System.Numerics;
using System.Reflection;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Attachment variety": every weapon (the strange ones too) has a real choice in each slot it takes,
/// every attachment fits something and has at least one downside (nothing is strictly better than an empty slot), each
/// weapon class has attachments of its own so the lists differ between classes, unlock orders are unique and in order,
/// the older attachments never need more weapon XP than they used to, the attachment models build and sit on their
/// anchors, and a loadout.txt with the new attachments (and unknown names) saves and loads cleanly.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Every slot a weapon takes offers at least this many attachments...</summary>
    private const int MinAttachmentsPerSlot = 2;

    /// <summary>...and every weapon takes at least this many slots.</summary>
    private const int MinAttachmentSlots = 3;

    /// <summary>Two classes' attachment lists differ by at least this many attachments.</summary>
    private const int MinClassDifference = 3;

    private static readonly HashSet<string> HigherIsBetter =
    [
        nameof(WeaponDef.Damage), nameof(WeaponDef.HeadMult), nameof(WeaponDef.Pellets), nameof(WeaponDef.RangeM), nameof(WeaponDef.MinDamageMult),
        nameof(WeaponDef.Rpm), nameof(WeaponDef.Mag), nameof(WeaponDef.Mobility), nameof(WeaponDef.RecoilRecovery), nameof(WeaponDef.RecoilRecoveryAmount),
    ];

    private static readonly HashSet<string> LowerIsBetter =
    [
        nameof(WeaponDef.Reload), nameof(WeaponDef.HipSpreadDeg), nameof(WeaponDef.AdsSpreadMult), nameof(WeaponDef.RecoilDeg), nameof(WeaponDef.EquipTime),
        nameof(WeaponDef.AdsTime), nameof(WeaponDef.BurstDelay), nameof(WeaponDef.RecoilVertical), nameof(WeaponDef.RecoilHorizontal), nameof(WeaponDef.RecoilJitter),
        nameof(WeaponDef.RecoilFirstShot), nameof(WeaponDef.RecoilRampVertical), nameof(WeaponDef.RecoilRampHorizontal), nameof(WeaponDef.RecoilSway),
        nameof(WeaponDef.RecoilRecoveryDelay), nameof(WeaponDef.RecoilCrouchVertical), nameof(WeaponDef.RecoilCrouchHorizontal), nameof(WeaponDef.RecoilProneVertical),
        nameof(WeaponDef.RecoilProneHorizontal), nameof(WeaponDef.RecoilAdsVertical), nameof(WeaponDef.RecoilAdsHorizontal), nameof(WeaponDef.RecoilShake),
        nameof(WeaponDef.RecoilPunch), nameof(WeaponDef.RecoilPunchFov), nameof(WeaponDef.RecoilPunchRoll), nameof(WeaponDef.RecoilKickBack),
        nameof(WeaponDef.RecoilKickUp), nameof(WeaponDef.RecoilKickRoll), nameof(WeaponDef.RecoilKickSide),
    ];

    /// <summary>
    /// The stats an attachment makes worse on a weapon (kitted copy vs the stock one). On an optic, changing the zoom
    /// counts as a trade-off too: the view narrows, or a gun's built-in magnification is given up.
    /// </summary>
    private static List<string> Downsides(WeaponDef stock, WeaponDef kitted, bool optic)
    {
        var worse = new List<string>();
        foreach (var f in typeof(WeaponDef).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType != typeof(float) && f.FieldType != typeof(int))
                continue;
            var x = Convert.ToDouble(f.GetValue(stock));
            var y = Convert.ToDouble(f.GetValue(kitted));
            if (x == y)
                continue;
            if (f.Name == nameof(WeaponDef.AdsZoom) ? optic : HigherIsBetter.Contains(f.Name) ? y < x : LowerIsBetter.Contains(f.Name) && y > x)
                worse.Add(f.Name);
        }
        return worse;
    }

    private static void CheckAttachmentVariety()
    {
        var all = Attachments.BySlot.SelectMany(s => s).ToArray();

        // ---- Every weapon: at least MinAttachmentSlots slots, each with at least MinAttachmentsPerSlot options ----
        foreach (var w in Weapons.All.Where(x => x.SourceFile != null && !x.IsLauncher)) // launchers only take their own few (SelfTestLaunchers.cs)
        {
            var list = Attachments.For(w);
            var perSlot = Enumerable.Range(0, Attachments.SlotCount).Select(s => list.Count(a => (int)a.Slot == s)).ToArray();
            var slots = perSlot.Count(n => n > 0);
            if (slots < MinAttachmentSlots || perSlot.Any(n => n is > 0 and < MinAttachmentsPerSlot))
                Fail($"{w.Name}: attachments per slot {string.Join("/", perSlot)} (want at least {MinAttachmentSlots} slots with {MinAttachmentsPerSlot}+ each)");
        }
        var counts = Weapons.All.Select(w => Attachments.For(w).Count).ToArray();
        Line($"{all.Length} attachments; every weapon has {MinAttachmentSlots}+ slots with {MinAttachmentsPerSlot}+ choices each; {counts.Min()}-{counts.Max()} per weapon");

        // ---- Every attachment fits something and has a downside on every weapon it fits ----
        var checkedPairs = 0;
        foreach (var a in all)
        {
            var fits = Weapons.All.Where(a.Fits).ToArray();
            if (fits.Length == 0)
                Fail($"{a.Name}: fits no weapon");
            foreach (var w in fits)
            {
                checkedPairs++;
                var d = Attachments.Apply(w, Attachments.With(0, a.Slot, a.Index));
                if (Downsides(w, d, a.Slot == AttachSlot.Optic).Count == 0)
                    Fail($"{a.Name} on {w.Name}: strictly better than an empty slot (no stat gets worse)");
            }
            if (a.Cons.Length == 0)
                Fail($"{a.Name}: no cons listed");
        }
        Line($"{checkedPairs} weapon x attachment pairs: each has at least one downside");

        // ---- The class matrix: each class has attachments of its own, and no two classes share the same list ----
        var classes = Weapons.All.Select(w => w.Class).Distinct().OrderBy(c => c).ToArray();
        var byClass = classes.ToDictionary(c => c, c => all.Where(a => Weapons.All.Any(w => w.Class == c && a.Fits(w))).ToHashSet());
        foreach (var c in classes)
        {
            var own = all.Where(a => Weapons.All.Where(a.Fits).Select(w => w.Class).Distinct().SequenceEqual(new[] { c })).Select(a => a.Name).ToArray();
            var perSlot = Enumerable.Range(0, Attachments.SlotCount).Select(s => byClass[c].Count(a => (int)a.Slot == s));
            Line($"  {c}: {byClass[c].Count} attachments ({string.Join("/", perSlot)} by slot), {own.Length} of its own: {string.Join(", ", own)}");
            if (own.Length == 0)
                Fail($"{c}: no class-exclusive attachment");
        }
        for (var i = 0; i < classes.Length; i++)
            for (var j = i + 1; j < classes.Length; j++)
            {
                var diff = byClass[classes[i]].Count(a => !byClass[classes[j]].Contains(a)) + byClass[classes[j]].Count(a => !byClass[classes[i]].Contains(a));
                if (diff < MinClassDifference)
                    Fail($"{classes[i]} and {classes[j]} differ by only {diff} attachment(s)");
            }

        // ---- Unlock orders: unique, and each gun's list follows them ----
        foreach (var g in all.GroupBy(a => a.UnlockOrder).Where(g => g.Count() > 1))
            Fail($"unlockOrder {g.Key} is shared by {string.Join(", ", g.Select(a => a.Name))}");
        foreach (var w in Weapons.All)
        {
            var list = Attachments.For(w);
            for (var i = 1; i < list.Count; i++)
                if (list[i].UnlockOrder < list[i - 1].UnlockOrder)
                    Fail($"{w.Name}: {list[i].Name} comes after {list[i - 1].Name} in the unlock list but has a lower unlockOrder");
            foreach (var a in list)
            {
                var xp = Attachments.UnlockXp(w, a);
                if (xp < 50 || xp > Attachments.MaxXp(w))
                    Fail($"{w.Name} + {a.Name}: unlocks at {xp} weapon XP (max {Attachments.MaxXp(w)})");
                // Saves keep working: what a player unlocked under the old rules stays unlocked.
                if (xp > Attachments.LegacyUnlockXp(w, a))
                    Fail($"{w.Name} + {a.Name}: needs {xp} weapon XP, more than the {Attachments.LegacyUnlockXp(w, a)} it used to");
            }
        }
        foreach (var name in Attachments.LegacyNames)
            if (Attachments.Find(name) == null)
                Fail($"'{name}' (shipped before) isn't loaded");

        // ---- Models: every attachment with a "model" changes the gun, stays on it, and sits on its anchor ----
        var models = 0;
        foreach (var a in all.Where(a => a.Look != null))
            foreach (var w in Weapons.All.Where(x => x.Model != null && a.Fits(x)))
            {
                models++;
                var stock = GunModel.Mesh(w)!;
                var mesh = GunModel.Mesh(Attachments.Apply(w, Attachments.With(0, a.Slot, a.Index)))!;
                if (mesh.Parts.Length == stock.Parts.Length && mesh.Matrices.SequenceEqual(stock.Matrices))
                    Fail($"{w.Name} + {a.Name}: the '{a.Look}' model doesn't change the gun");
                var lo = new Vector3(float.MaxValue);
                var hi = new Vector3(float.MinValue);
                foreach (var p in stock.Parts)
                {
                    var (l, h) = p.Bounds();
                    lo = Vector3.Min(lo, l);
                    hi = Vector3.Max(hi, h);
                }
                foreach (var p in mesh.Parts.Where(q => (q.Flags & GunPartFlags.Blend) == 0))
                {
                    var (l, h) = p.Bounds();
                    var c = (l + h) / 2;
                    string? why = null;
                    if (Vector3.Min(l - (lo - new Vector3(0.3f)), hi + new Vector3(0.3f) - h) is var m && (m.X < 0 || m.Y < 0 || m.Z < 0))
                        why = $"a box reaches {l}..{h}, far off the gun ({lo}..{hi})";
                    else if (a.Slot == AttachSlot.Muzzle && (p.Flags & GunPartFlags.Muzzle) != 0 && (h.Z > stock.MuzzleZ + 0.1f || l.Z < stock.MuzzleZ - 0.3f))
                        why = $"a muzzle box at z {l.Z:0.000}..{h.Z:0.000} isn't at the muzzle ({stock.MuzzleZ:0.000})";
                    else if (a.Slot == AttachSlot.Underbarrel && (p.Flags & GunPartFlags.Underbarrel) != 0 && Vector3.Distance(c, mesh.Underbarrel) > 0.45f)
                        why = $"an underbarrel box at {c} is {Vector3.Distance(c, mesh.Underbarrel):0.00} m from the underbarrel mount {mesh.Underbarrel}";
                    if (why != null)
                    {
                        Fail($"{w.Name} + {a.Name}: {why}");
                        break;
                    }
                }
                if (a.Slot == AttachSlot.Muzzle && !mesh.Parts.Any(p => (p.Flags & GunPartFlags.Muzzle) != 0))
                    Fail($"{w.Name} + {a.Name}: no muzzle part drawn");
                if (a.Slot == AttachSlot.Underbarrel && !mesh.Parts.Any(p => (p.Flags & GunPartFlags.Underbarrel) != 0))
                    Fail($"{w.Name} + {a.Name}: no underbarrel part drawn");
            }
        Line($"{models} weapon x attachment models built from their \"model\" looks, each on the gun and on its mount");

        CheckBayonet();

        // ---- Saves: a loadout with new attachments (and an unknown name) round-trips; old kits still load ----
        var backup = LoadoutStore.SaveLines().ToArray();
        try
        {
            var gun = Weapons.All.FirstOrDefault(w => w.Class == WeaponClass.Special && w.Slot == Slot.Primary) ?? Weapons.DefaultPrimary;
            uint want = 0;
            foreach (var a in Attachments.For(gun).Reverse()) // the newest ones come last in unlock order
                if (Attachments.Slot(want, a.Slot) == 0)
                    want = Attachments.With(want, a.Slot, a.Index);
            var names = string.Join(", ", Attachments.InKit(want).Select(a => a.Name));
            LoadoutStore.LoadLines(["selected=0", "loadout1.name=Odd", $"loadout1.primary={gun.Name}", $"loadout1.primary.kit={names}, Banana Scope, ",
                $"kit.{gun.Name}={names}, Banana Scope"]);
            var first = LoadoutStore.Slots[0].PrimaryKit;
            LoadoutStore.LoadLines(LoadoutStore.SaveLines().ToArray());
            var again = LoadoutStore.Slots[0].PrimaryKit;
            Line($"  loadout.txt: {gun.Name} [{Attachments.Describe(first)}] after load, [{Attachments.Describe(again)}] after save + load");
            if (LoadoutStore.Slots[0].Primary != gun.Id || first != want || again != want || LoadoutStore.KitMemory(gun.Id) != want)
                Fail($"a loadout with new attachments didn't round-trip ({Attachments.Describe(want)} -> {Attachments.Describe(first)} -> {Attachments.Describe(again)})");
            // A file from before the new attachments (shipped names only) loads exactly as it did.
            var rifle = Weapons.Find("Vanguard AR") ?? Weapons.DefaultPrimary;
            LoadoutStore.LoadLines(["loadout1.primary=" + rifle.Name, "loadout1.primary.kit=Red Dot, Suppressor, Vertical Grip, Extended Mag, Heavy Stock"]);
            if (Attachments.InKit(LoadoutStore.Slots[0].PrimaryKit).Count() != 5)
                Fail($"an old kit loaded as [{Attachments.Describe(LoadoutStore.Slots[0].PrimaryKit)}]");
        }
        finally
        {
            LoadoutStore.LoadLines(backup);
        }
    }

    /// <summary>The Bayonet: only on muzzle-loading specials (the musket), and with it fitted a melee stab reaches 0.8 m further.</summary>
    private static void CheckBayonet()
    {
        if (Attachments.Find("Bayonet") is not { } bay)
        {
            Fail("the Bayonet isn't loaded");
            return;
        }
        var fits = Weapons.All.Where(bay.Fits).ToArray();
        Line($"  Bayonet: {bay.Slot} slot, +{bay.MeleeReach:0.0} m melee reach, fits {string.Join(", ", fits.Select(w => w.Name))}");
        if (fits.Length == 0 || fits.Any(w => w.Class != WeaponClass.Special || w.Sound != SoundStyle.Boom))
            Fail("the Bayonet should fit the musket-type specials and nothing else");
        if (bay.MeleeReach is < 0.7f or > 0.9f || all().Any(x => x != bay && x.MeleeReach != 0))
            Fail("only the Bayonet adds melee reach, about 0.8 m");
        if (fits.FirstOrDefault() is not { } musket)
            return;
        var kit = Attachments.With(0, bay.Slot, bay.Index);
        if (Attachments.MeleeReachBonus(musket) != 0 || Attachments.MeleeReachBonus(Attachments.Apply(musket, kit)) != bay.MeleeReach)
            Fail($"melee reach bonus: stock {Attachments.MeleeReachBonus(musket)}, with the bayonet {Attachments.MeleeReachBonus(Attachments.Apply(musket, kit))}");

        // A real stab: 2.95 m away (2.55 m to the body) is out of reach without it, in reach with it.
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", musket.Slot == Slot.Primary ? musket.Id : Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var a = game.Players[0];
        var v = game.Players.Values.First(p => p.Id != 0);
        game.SelfTestPerks(a, PerkSet.Empty);
        game.SelfTestPerks(v, PerkSet.Empty);
        a.Primary = a.Weapon = musket.Id;
        var hits = new int[2];
        for (var withBayonet = 0; withBayonet < 2; withBayonet++)
        {
            a.PrimaryKit = withBayonet == 1 ? kit : 0;
            game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
            game.SelfTestPlace(v, new Vector3(5, 0, 7.95f), 0);
            game.SelfTestMelee(a, MathF.PI, 0, 20 + withBayonet * 5);
            hits[withBayonet] = World.MaxHealth - v.Health;
        }
        Line($"  stab at 2.55 m: {hits[0]} damage without the bayonet (reach {Game.MeleeRange} m), {hits[1]} with it ({Game.MeleeRange + bay.MeleeReach:0.0} m)");
        if (hits[0] != 0 || hits[1] <= 0)
            Fail($"bayonet reach: a stab at 2.55 m did {hits[0]} without and {hits[1]} with the bayonet (expected 0 and a hit)");

        static AttachmentDef[] all() => Attachments.BySlot.SelectMany(s => s).ToArray();
    }
}
