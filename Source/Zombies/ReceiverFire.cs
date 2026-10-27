namespace CallOfSlop;

/// <summary>
/// One receiver's own trigger on a built weapon whose receivers fire at their own rates (a volley build with several
/// receivers: <see cref="WeaponDef.FireGroups"/>, docs/zombies/WEAPON-BUILDER.md "Independent fire rates"). Index = the
/// receiver's barrel group (<see cref="BarrelPoint.Group"/>). Built by <see cref="BuildStats.ComputeUnchecked"/>; fired by
/// <see cref="FireGroupState"/> (the local player, bots) and checked per receiver by the server's fire-rate budget.
/// </summary>
/// <param name="Rpm">Its own fire rate (its action, upgrades and the grip's parts).</param>
/// <param name="Mode">Its own trigger: auto (auto, rotary, chain), burst, or one shot per pull (semi, pump, bolt, lever...).</param>
/// <param name="Feed">Which magazine it draws from: its own, or the shared feed's pool (receivers on a hopper share one index).</param>
/// <param name="FeedMag">Rounds that magazine holds.</param>
/// <param name="Reload">Seconds to refill that magazine on its own (no per-receiver penalty: the others keep firing).</param>
/// <param name="RecoilShare">Its share of the whole volley's recoil (its recoil squared over the sum of squares; they add up per shot).</param>
/// <param name="FireType">Its fire type in words ("full auto", "pump"...).</param>
public readonly record struct ReceiverFire(int Rpm, FireMode Mode, int BurstCount, float BurstDelay, float SpoolTime, float SpoolStart,
    int Feed, int FeedMag, float Reload, float RecoilShare, string FireType)
{
    /// <summary>Seconds between its shots at full rate.</summary>
    public float FireInterval => 60f / Math.Max(1, Rpm);

    /// <summary>Its shots per second at full rate, bursts' pauses included.</summary>
    public float ShotsPerSecond => Mode == FireMode.Burst ? BurstCount / (BurstCount * FireInterval + BurstDelay) : 1f / FireInterval;

    /// <summary>The most receivers a weapon may fire independently (the biggest grips hold 16; the trigger mask is 16 bits).</summary>
    public const int Max = 16;
}
