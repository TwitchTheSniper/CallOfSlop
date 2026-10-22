namespace CallOfSlop;

/// <summary>
/// The kill-frenzy meter (game thread, pure maths, deterministic for a sequence of times): every kill of the player
/// adds heat, much more for a fast follow-up, a bit more for headshots and multi-kill chains; heat bleeds away
/// slowly for <see cref="HoldSeconds"/> after the last kill, then drains faster until it's gone. Dying wipes it. The
/// heat maps to an intensity level 0 (off) to 6 with hysteresis, which <see cref="FrenzyLayer"/> turns into music.
/// Decay is integrated exactly, so the result doesn't depend on the frame rate.
/// Shared by the server and the client (moved here from Source/Audio/MusicFrenzy.cs): the server runs one per player
/// from its kill events (Source/Game/Frenzy/FrenzyServer.cs) and replicates the level; the client's music meter
/// (<see cref="FrenzyMeter"/>) is the same class, so the same kill sequence gives identical levels on both.
/// Levels 1-4 are the music building up; 5 (FRENZY) is louder than any other music, 6 (MAX FRENZY) is as loud as the
/// gunfire.
/// Tuning (2026-10-22, "attainable even on smaller 8v8 maps"): it depends only on the kill pacing, not on the lobby or
/// map size, and is set for the pace a strong player keeps on a small map (a kill every 4-8 s during a streak), so a
/// small lobby isn't penalised. Kills needed (no headshots): a kill every 3 s or faster: level 4 on the 4th, 5 on the
/// 6th, 6 (MAX FRENZY) on the 8th-10th; every 4 s: 5 on the 7th, 6 on the 13th; every 5 s: 5 on the 8th, 6 only on
/// the 23rd; every 6 s: 4 on the 7th, never 5; every 8 s: 1 on the 2nd, 2 on the 3rd, 3 on the 6th and no higher;
/// every 12 s or slower never starts it. A single kill never does. The pure-maths self-test pins these targets
/// (SelfTestFrenzy.cs) and a Python replica of the model was used to tune it.
/// </summary>
public class FrenzyMeterCore
{
    /// <summary>Heat cap: a couple of fast kills past level 6, so a sustained spree holds the top for the full hold time and a bit.</summary>
    public const float MaxHeat = 240f;

    /// <summary>Heat for any kill, plus up to <see cref="FastBonus"/> for a follow-up within <see cref="FastGap"/> s (none after <see cref="SlowGap"/> s).</summary>
    public const float BaseHeat = 8f, FastBonus = 14f, FastGap = 3f, SlowGap = 12f;

    /// <summary>Kills within <see cref="ChainWindow"/> s of each other form a chain; each link adds <see cref="ChainStep"/> more (up to 4 links).</summary>
    public const float ChainWindow = 5f, ChainStep = 2f, HeadshotBonus = 3f;

    public const int MaxChainLinks = 4;

    /// <summary>Decay: exponential at <see cref="HoldDecay"/>/s for the hold, then <see cref="Decay"/>/s plus a linear <see cref="Drain"/>/s.</summary>
    public const float HoldDecay = 0.03f, Decay = 0.06f, Drain = 2f, HoldSeconds = 8f;

    /// <summary>
    /// Zombies: kills come in hordes, so each is worth less (0.4) and the hold is shorter (5 s): a kill a second takes
    /// about 22 kills to MAX FRENZY, every 2 s about 40, so a horde doesn't max it instantly.
    /// </summary>
    public const float ZombieScale = 0.4f, ZombieHoldSeconds = 5f;

    /// <summary>Heat needed to reach levels 1-6, and the heat below which each level is left again (hysteresis).</summary>
    public static readonly float[] Up = [12f, 28f, 48f, 72f, 120f, 185f], Down = [8f, 22f, 42f, 64f, 110f, 170f];

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
