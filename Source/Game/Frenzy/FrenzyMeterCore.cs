namespace CallOfSlop;

/// <summary>
/// The kill-frenzy meter (game thread, pure maths, deterministic for a sequence of times): every kill of the player
/// adds heat, much more for a fast follow-up, a bit more for headshots and multi-kill chains; heat bleeds away
/// slowly for <see cref="HoldSeconds"/> after the last kill, then drains faster until it's gone. Dying wipes it. The
/// heat maps to an intensity level 0 (off) to 6 with hysteresis, which <see cref="FrenzyLayer"/> turns into music.
/// Decay is integrated exactly, so the result doesn't depend on the frame rate.
/// Shared by the server and the client (moved here from Source/Audio/MusicFrenzy.cs, numbers unchanged): the server runs
/// one per player from its kill events (Source/Game/Frenzy/FrenzyServer.cs) and replicates the level; the client's music
/// meter (<see cref="FrenzyMeter"/>) is the same class, so the same kill sequence gives identical levels on both.
/// Levels 1-4 are the music building up; 5 (FRENZY) is louder than any other music, 6 (MAX FRENZY) is as loud as the
/// gunfire. Kills a second apart reach level 5 on the 9th kill and level 6 on the 15th; a kill every 2 s gets to 5
/// on the 10th and 6 only on the 22nd; every 2.5 s or slower never reaches 6 (every 3 s gets to 5 after 14 kills).
/// </summary>
public class FrenzyMeterCore
{
    /// <summary>Heat cap: about four fast kills past level 6, so a sustained spree holds the top for the full hold time.</summary>
    public const float MaxHeat = 400f;

    /// <summary>Heat for any kill, plus up to <see cref="FastBonus"/> for a follow-up within <see cref="FastGap"/> s (none after <see cref="SlowGap"/> s).</summary>
    public const float BaseHeat = 6f, FastBonus = 14f, FastGap = 2f, SlowGap = 7f;

    /// <summary>Kills within <see cref="ChainWindow"/> s of each other form a chain; each link adds <see cref="ChainStep"/> more (up to 4 links).</summary>
    public const float ChainWindow = 4f, ChainStep = 3f, HeadshotBonus = 3f;

    public const int MaxChainLinks = 4;

    /// <summary>Decay: exponential at <see cref="HoldDecay"/>/s for the hold, then <see cref="Decay"/>/s plus a linear <see cref="Drain"/>/s.</summary>
    public const float HoldDecay = 0.04f, Decay = 0.07f, Drain = 3f, HoldSeconds = 5f;

    /// <summary>Zombies: kills come in hordes, so each is worth less and the hold is shorter.</summary>
    public const float ZombieScale = 0.5f, ZombieHoldSeconds = 3.5f;

    /// <summary>Heat needed to reach levels 1-6, and the heat below which each level is left again (hysteresis).</summary>
    public static readonly float[] Up = [12f, 30f, 52f, 85f, 200f, 330f], Down = [8f, 24f, 44f, 74f, 185f, 310f];

    public const int MaxLevel = 6;

    /// <summary>Zombies tuning (<see cref="ZombieScale"/>, <see cref="ZombieHoldSeconds"/>).</summary>
    public bool Zombies;

    public float Heat { get; private set; }
    public int Level { get; private set; }

    /// <summary>Links in the current multi-kill chain (0 for a lone kill).</summary>
    public int Chain { get; private set; }

    private double _lastKill = double.NegativeInfinity, _clock = double.NaN;

    private float Hold => Zombies ? ZombieHoldSeconds : HoldSeconds;

    /// <summary>Heat a kill would add after a gap of <paramref name="gap"/> seconds with <paramref name="links"/> chain links.</summary>
    public float KillHeat(double gap, int links, bool headshot)
    {
        var fast = (float)Math.Clamp((SlowGap - gap) / (SlowGap - FastGap), 0, 1);
        var add = BaseHeat + FastBonus * fast + ChainStep * Math.Min(links, MaxChainLinks) + (headshot ? HeadshotBonus : 0f);
        return Zombies ? add * ZombieScale : add;
    }

    /// <summary>The local player got a kill at clock time <paramref name="now"/> (seconds).</summary>
    public void OnKill(double now, bool headshot)
    {
        Update(now);
        var gap = now - _lastKill;
        Chain = gap <= ChainWindow ? Chain + 1 : 0;
        Heat = MathF.Min(MaxHeat, Heat + KillHeat(gap, Chain, headshot));
        _lastKill = now;
        UpdateLevel();
    }

    /// <summary>The local player died: the frenzy is over (the music fades out fast).</summary>
    public void OnDeath(double now)
    {
        _clock = now;
        Reset();
    }

    /// <summary>Forgets everything (new match, left the game).</summary>
    public void Reset()
    {
        Heat = 0;
        Level = 0;
        Chain = 0;
        _lastKill = double.NegativeInfinity;
    }

    /// <summary>Advances the decay to <paramref name="now"/>.</summary>
    public void Update(double now)
    {
        if (double.IsNaN(_clock) || now < _clock)
        {
            _clock = now;
            return;
        }
        var dt = now - _clock;
        _clock = now;
        if (Heat <= 0 || dt <= 0)
            return;
        // The hold part of this interval, then the drain part.
        var holdEnd = _lastKill + Hold;
        var start = now - dt;
        var holdDt = Math.Clamp(holdEnd - start, 0, dt);
        var drainDt = dt - holdDt;
        var h = (double)Heat;
        if (holdDt > 0)
            h *= Math.Exp(-HoldDecay * holdDt);
        if (drainDt > 0)
        {
            // dh/dt = -Decay*h - Drain, solved exactly.
            var c = Drain / Decay;
            h = (h + c) * Math.Exp(-Decay * drainDt) - c;
        }
        Heat = (float)Math.Max(0, h);
        UpdateLevel();
    }

    private void UpdateLevel()
    {
        var l = Level;
        while (l < MaxLevel && Heat >= Up[l])
            l++;
        while (l > 0 && Heat < Down[l - 1])
            l--;
        Level = l;
    }
}
