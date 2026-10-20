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
            Movement = [PerkId.Afterburner, PerkId.HeadStart], Survival = [PerkId.Bloodthirst, PerkId.LastStand], Damage = [PerkId.PointBlank],
        },
        new()
        {
            Kind = BotPersonality.Marksman, Name = "Marksman",
            ClassWeight = [0, 0, 0.6f, 4, 5, 0, 0.5f, 0.1f],
            RangeMult = 1.5f, Aggression = 0.2f, RetreatHealth = 45, CoverUse = 0.8f,
            StrafeMin = 1.2f, StrafeMax = 2.5f, StrafeStrength = 0.4f, GrenadeRate = 0.5f, CrouchPeek = 0.2f, GoesProne = true,
            ObjectiveWeight = 0.4f,
            Movement = [PerkId.LowProfile], Survival = [PerkId.Entrenched], Damage = [PerkId.Deadeye],
        },
        new()
        {
            Kind = BotPersonality.Flanker, Name = "Flanker",
            ClassWeight = [0.2f, 3, 3, 0.5f, 0, 1.5f, 0, 0.3f],
            RangeMult = 0.85f, Aggression = 0.7f, RetreatHealth = 30, CoverUse = 0.4f,
            StrafeMin = 0.4f, StrafeMax = 1.2f, StrafeStrength = 1f, GrenadeRate = 0.8f, CrouchPeek = 0.2f, FlankOffset = 14f,
            ObjectiveWeight = 0.35f,
            Movement = [PerkId.HeadStart, PerkId.Parkour], Survival = [PerkId.LastStand], Damage = [PerkId.Flanker],
        },
        new()
        {
            Kind = BotPersonality.Anchor, Name = "Anchor",
            ClassWeight = [0, 0.5f, 3, 1.5f, 0.5f, 1, 4, 0.2f],
            RangeMult = 1.1f, Aggression = 0.3f, RetreatHealth = 30, CoverUse = 0.9f,
            StrafeMin = 0.8f, StrafeMax = 2f, StrafeStrength = 0.6f, GrenadeRate = 1f, CrouchPeek = 0.6f, Holds = true,
            ObjectiveWeight = 0.95f,
            Movement = [PerkId.LowProfile], Survival = [PerkId.Entrenched, PerkId.FlakJacket], Damage = [PerkId.Opener],
        },
        new()
        {
            Kind = BotPersonality.Tactician, Name = "Tactician",
            ClassWeight = [0, 1.5f, 4, 2, 0.3f, 1, 1, 0.3f],
            RangeMult = 1f, Aggression = 0.5f, RetreatHealth = 40, CoverUse = 0.85f,
            StrafeMin = 0.6f, StrafeMax = 1.6f, StrafeStrength = 0.9f, GrenadeRate = 3f, CrouchPeek = 0.5f,
            ObjectiveWeight = 0.6f,
            Movement = [PerkId.Parkour, PerkId.LowProfile], Survival = [PerkId.FlakJacket], Damage = [PerkId.Opener, PerkId.Deadeye],
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
