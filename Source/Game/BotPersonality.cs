namespace CallOfSlop;

/// <summary>How a bot likes to play. Stacks on top of <see cref="BotDifficulty"/>, which still sets reaction time and aim.</summary>
public enum BotPersonality : byte
{
    /// <summary>Pushes in close with SMGs and shotguns, chases kills.</summary>
    Rusher,

    /// <summary>Holds long sightlines with DMRs and snipers, goes prone, moves after a kill.</summary>
    Marksman,

    /// <summary>Takes side routes and comes at you from behind.</summary>
    Flanker,

    /// <summary>Holds an area or a piece of cover (and the objective, when there is one).</summary>
    Anchor,

    /// <summary>More grenades, pre-aims corners, retreats to cover smartly.</summary>
    Tactician,

    /// <summary>Unpredictable: random guns, random habits.</summary>
    Chaotic,
}

/// <summary>The numbers behind one <see cref="BotPersonality"/>. Plain data, so it's easy to tune or load from a file later.</summary>
public sealed class BotProfile
{
    public required BotPersonality Kind;
    public required string Name;

    /// <summary>How much this personality likes each weapon class (indexed by <see cref="WeaponClass"/>).</summary>
    public required float[] ClassWeight;

    /// <summary>Multiplies the weapon's preferred engagement range.</summary>
    public float RangeMult = 1f;

    /// <summary>0 = cautious, 1 = reckless: pushes towards enemies, chases last-seen spots, ignores low health.</summary>
    public float Aggression = 0.5f;

    /// <summary>Below this health the bot breaks contact and looks for cover.</summary>
    public int RetreatHealth = 35;

    /// <summary>Chance (0..1) of ducking behind cover to reload instead of reloading in the open.</summary>
    public float CoverUse = 0.5f;

    /// <summary>Seconds between strafe direction changes (min, max) and how hard it strafes (0..1).</summary>
    public float StrafeMin = 0.5f, StrafeMax = 1.6f, StrafeStrength = 1f;

    /// <summary>Multiplies the chance of throwing a grenade.</summary>
    public float GrenadeRate = 1f;

    /// <summary>Chance per second of a short crouch-peek in a fight.</summary>
    public float CrouchPeek = 0.3f;

    /// <summary>Goes prone for long shots.</summary>
    public bool GoesProne;

    /// <summary>How far (metres) it bends its route sideways to come at enemies from a flank.</summary>
    public float FlankOffset;

    /// <summary>0..1: how much it cares about the game mode's objective over hunting kills.</summary>
    public float ObjectiveWeight = 0.5f;

    /// <summary>Stays near its anchor point instead of roaming.</summary>
    public bool Holds;

    /// <summary>Perks it likes, per category (one is picked at random each life).</summary>
    public required PerkId[] Movement, Survival, Damage;

    // Weapon class order: Pistol, SMG, AssaultRifle, DMR, Sniper, Shotgun, LMG, Special
}

/// <summary>The built-in personalities.</summary>
public static class BotPersonalities
{
    public const int Count = 6;

    public static readonly BotProfile[] All =
    [
        new()
        {
            Kind = BotPersonality.Rusher, Name = "Rusher",
            ClassWeight = [0, 5, 1, 0.1f, 0, 4, 0.3f, 0.4f],
            RangeMult = 0.6f, Aggression = 0.95f, RetreatHealth = 12, CoverUse = 0.1f,
            StrafeMin = 0.25f, StrafeMax = 0.8f, StrafeStrength = 1f, GrenadeRate = 0.6f, CrouchPeek = 0.1f,
            ObjectiveWeight = 0.25f,
            Movement = [PerkId.Afterburner, PerkId.HeadStart], Survival = [PerkId.Bloodthirst, PerkId.LastStand, PerkId.QuickFix],
            Damage = [PerkId.PointBlank, PerkId.SteadyAim, PerkId.SleightOfHand],
        },
        new()
        {
            Kind = BotPersonality.Marksman, Name = "Marksman",
            ClassWeight = [0, 0, 0.6f, 4, 5, 0, 0.5f, 0.1f],
            RangeMult = 1.5f, Aggression = 0.2f, RetreatHealth = 45, CoverUse = 0.8f,
            StrafeMin = 1.2f, StrafeMax = 2.5f, StrafeStrength = 0.4f, GrenadeRate = 0.5f, CrouchPeek = 0.2f, GoesProne = true,
            ObjectiveWeight = 0.4f,
            Movement = [PerkId.LowProfile, PerkId.Stalker], Survival = [PerkId.Entrenched, PerkId.ColdBlooded], Damage = [PerkId.Deadeye],
        },
        new()
        {
            Kind = BotPersonality.Flanker, Name = "Flanker",
            ClassWeight = [0.2f, 3, 3, 0.5f, 0, 1.5f, 0, 0.3f],
            RangeMult = 0.85f, Aggression = 0.7f, RetreatHealth = 30, CoverUse = 0.4f,
            StrafeMin = 0.4f, StrafeMax = 1.2f, StrafeStrength = 1f, GrenadeRate = 0.8f, CrouchPeek = 0.2f, FlankOffset = 14f,
            ObjectiveWeight = 0.35f,
            Movement = [PerkId.HeadStart, PerkId.Parkour, PerkId.DeadSilence, PerkId.Ghost], Survival = [PerkId.LastStand, PerkId.QuickFix],
            Damage = [PerkId.Flanker],
        },
        new()
        {
            Kind = BotPersonality.Anchor, Name = "Anchor",
            ClassWeight = [0, 0.5f, 3, 1.5f, 0.5f, 1, 4, 0.2f],
            RangeMult = 1.1f, Aggression = 0.3f, RetreatHealth = 30, CoverUse = 0.9f,
            StrafeMin = 0.8f, StrafeMax = 2f, StrafeStrength = 0.6f, GrenadeRate = 1f, CrouchPeek = 0.6f, Holds = true,
            ObjectiveWeight = 0.95f,
            Movement = [PerkId.LowProfile, PerkId.Lightweight], Survival = [PerkId.Entrenched, PerkId.FlakJacket, PerkId.Hardline],
            Damage = [PerkId.Opener, PerkId.SleightOfHand],
        },
        new()
        {
            Kind = BotPersonality.Tactician, Name = "Tactician",
            ClassWeight = [0, 1.5f, 4, 2, 0.3f, 1, 1, 0.3f],
            RangeMult = 1f, Aggression = 0.5f, RetreatHealth = 40, CoverUse = 0.85f,
            StrafeMin = 0.6f, StrafeMax = 1.6f, StrafeStrength = 0.9f, GrenadeRate = 3f, CrouchPeek = 0.5f,
            ObjectiveWeight = 0.6f,
            Movement = [PerkId.Parkour, PerkId.LowProfile, PerkId.Ghost], Survival = [PerkId.FlakJacket, PerkId.Scavenger, PerkId.Hardline],
            Damage = [PerkId.Opener, PerkId.Deadeye, PerkId.Demolitions],
        },
        new()
        {
            Kind = BotPersonality.Chaotic, Name = "Chaotic",
            ClassWeight = [1, 1, 1, 1, 1, 1, 1, 3],
            RangeMult = 1f, Aggression = 0.75f, RetreatHealth = 20, CoverUse = 0.25f,
            StrafeMin = 0.15f, StrafeMax = 1.8f, StrafeStrength = 1f, GrenadeRate = 1.5f, CrouchPeek = 0.8f,
            ObjectiveWeight = 0.3f,
            Movement = [], Survival = [], Damage = [], // empty = anything
        },
    ];

    public static BotProfile Get(BotPersonality p) => All[(int)p];
}

public sealed partial class Game
{
    /// <summary>The personality the lobby has least of (random among ties), so a full lobby gets a mix.</summary>
    private BotPersonality NextPersonality()
    {
        Span<int> count = stackalloc int[BotPersonalities.Count];
        foreach (var p in Players.Values)
            if (p.Bot != null)
                count[(int)p.Bot.Personality]++;
        // Chaotic counts double: one is fun, several is noise.
        count[(int)BotPersonality.Chaotic] *= 2;
        var min = int.MaxValue;
        foreach (var c in count)
            min = Math.Min(min, c);
        var ties = 0;
        foreach (var c in count)
            if (c == min)
                ties++;
        var pick = _rng.Next(ties);
        for (var i = 0; i < count.Length; i++)
            if (count[i] == min && pick-- == 0)
                return (BotPersonality)i;
        return BotPersonality.Tactician;
    }

    /// <summary>
    /// A loadout to suit the bot's personality: weighted by weapon class, the occasional silly special, all from what its
    /// gear level has unlocked (the lobby's level, BotGear.cs). At the max level the draws are exactly the old ones (same
    /// shared random numbers in the same order), so seeded matches replay the same. Allocates nothing: it runs every life.
    /// </summary>
    private void PickBotLoadout(Player bot, BotProfile prof)
    {
        RefreshBotGear(); // people come and go between lives
        var level = BotGearLevel(bot);
        if (bot.Bot is { } brain)
            brain.GearLevel = level;
        var all = Weapons.All;

        // Primary: a random one as the fallback, then a roll weighted by class (the same draws as before).
        var primaryLevel = BotWeaponCount(all, Slot.Primary, false, level) > 0 ? level : BotGear.Full; // nothing unlocked (odd weapon files): anything
        var count = BotWeaponCount(all, Slot.Primary, false, primaryLevel);
        var sum = 0.0; // a double sum cast to float, like Enumerable.Sum over floats
        foreach (var w in all)
            if (w.Slot == Slot.Primary && BotGear.Weapon(w, primaryLevel))
                sum += BotClassWeight(w, prof);
        var total = (float)sum;
        var primary = BotNthWeapon(all, Slot.Primary, false, primaryLevel, _rng.Next(count));
        if (total > 0)
        {
            var r = _rng.NextDouble() * total;
            foreach (var w in all)
                if (w.Slot == Slot.Primary && BotGear.Weapon(w, primaryLevel) && (r -= BotClassWeight(w, prof)) <= 0)
                {
                    primary = w;
                    break;
                }
        }
        var secondaryLevel = BotWeaponCount(all, Slot.Secondary, true, level) > 0 ? level : BotGear.Full; // launchers: their own roll below
        bot.WantPrimary = primary.Id;
        bot.WantSecondary = BotNthWeapon(all, Slot.Secondary, true, secondaryLevel, _rng.Next(BotWeaponCount(all, Slot.Secondary, true, secondaryLevel))).Id;
        var grenade = _rng.NextDouble() < (prof.Kind == BotPersonality.Tactician ? 0.95 : 0.8) ? ThrowableKind.Frag : ThrowableKind.Impact;
        if (!BotGear.Throwable(grenade, level))
            grenade = grenade == ThrowableKind.Frag ? ThrowableKind.Impact : ThrowableKind.Frag; // the other one, if that's unlocked
        bot.WantGrenade = BotGear.Throwable(grenade, level) ? grenade : ThrowableKind.None; // a level-1 player has no grenade yet
        bot.WantEquipment = BotGear.Equipment(prof.Kind, TacticalGear.Roll(bot.Id, bot.SpawnTick, 0xE0), level); // own roll: the shared random sequence is untouched
        bot.WantPrimaryKit = RandomKit(Weapons.Get(bot.WantPrimary), level);
        bot.WantSecondaryKit = RandomKit(Weapons.Get(bot.WantSecondary), level);
        bot.WantPerks = BotPerks(prof, level);
        BotPickLauncher(bot, prof, level); // some bots carry a launcher instead (BotLaunchers.cs; its own roll, the shared random sequence is untouched)
    }

    /// <summary>How much the personality likes the gun (specials are rarer for everyone but Chaotic).</summary>
    private static float BotClassWeight(WeaponDef w, BotProfile prof)
    {
        var c = (int)w.Class;
        var weight = c < prof.ClassWeight.Length ? prof.ClassWeight[c] : 0f;
        return w.Class == WeaponClass.Special && prof.Kind != BotPersonality.Chaotic ? weight * 0.4f : weight;
    }

    private static bool BotWeaponFits(WeaponDef w, Slot slot, bool noLaunchers, int level) =>
        w.Slot == slot && !(noLaunchers && w.IsLauncher) && BotGear.Weapon(w, level);

    /// <summary>How many guns for the slot the gear level has unlocked.</summary>
    private static int BotWeaponCount(WeaponDef[] all, Slot slot, bool noLaunchers, int level)
    {
        var n = 0;
        foreach (var w in all)
            if (BotWeaponFits(w, slot, noLaunchers, level))
                n++;
        return n;
    }

    /// <summary>The n-th of those guns (in weapon list order).</summary>
    private static WeaponDef BotNthWeapon(WeaponDef[] all, Slot slot, bool noLaunchers, int level, int n)
    {
        foreach (var w in all)
            if (BotWeaponFits(w, slot, noLaunchers, level) && n-- == 0)
                return w;
        return slot == Slot.Primary ? Weapons.DefaultPrimary : Weapons.DefaultSecondary;
    }

    /// <summary>Perks the personality likes (one per category), or random ones for an empty preference list; all unlocked at the gear level.</summary>
    private PerkSet BotPerks(BotProfile prof, int level)
    {
        var random = RandomPerks(level);
        PerkId Pick(PerkId[] options, PerkId fallback)
        {
            if (options.Length == 0)
                return fallback;
            var start = _rng.Next(options.Length);
            for (var i = 0; i < options.Length; i++) // a locked favourite: the next favourite that's unlocked
                if (options[(start + i) % options.Length] is var id && BotGear.Perk(id, level))
                    return id;
            return fallback; // none unlocked yet: the random one
        }
        return new PerkSet(Pick(prof.Movement, random.Movement), Pick(prof.Survival, random.Survival), Pick(prof.Damage, random.Damage)).Sanitized();
    }
}
