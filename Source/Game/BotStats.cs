namespace CallOfSlop;

/// <summary>
/// Running counters about how bots fight (server only). Cheap to keep, and the self-test reads them to check
/// the headshot share and that personalities really play differently.
/// </summary>
public sealed class BotStats
{
    /// <summary>Kills scored by bots with a gun (not grenades or melee).</summary>
    public int GunKills;

    /// <summary>Kills scored by bots with melee (one hit kills, so the self-test keeps an eye on how often they use it).</summary>
    public int MeleeKills;

    /// <summary>How many of <see cref="GunKills"/> were headshots.</summary>
    public int GunHeadshots;

    /// <summary>Per personality: shots fired and the summed distance to the target at the time.</summary>
    public readonly int[] Shots = new int[BotPersonalities.Count];
    public readonly double[] ShotDistance = new double[BotPersonalities.Count];

    /// <summary>Blind fire into foliage or a cornfield (BotFoliage.cs): shots, bursts, and hidden enemies heard running.</summary>
    public int BlindShots, BlindBursts, FootstepCues;

    public float HeadshotRatio => GunKills == 0 ? 0 : GunHeadshots / (float)GunKills;

    /// <summary>Average distance at which bots of this personality shoot (0 if they never fired).</summary>
    public float AverageRange(BotPersonality p) => Shots[(int)p] == 0 ? 0 : (float)(ShotDistance[(int)p] / Shots[(int)p]);

    public void Add(BotStats o)
    {
        GunKills += o.GunKills;
        GunHeadshots += o.GunHeadshots;
        MeleeKills += o.MeleeKills;
        BlindShots += o.BlindShots;
        BlindBursts += o.BlindBursts;
        FootstepCues += o.FootstepCues;
        for (var i = 0; i < Shots.Length; i++)
        {
            Shots[i] += o.Shots[i];
            ShotDistance[i] += o.ShotDistance[i];
        }
    }
}
