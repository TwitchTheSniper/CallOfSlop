using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The trigger state of a built weapon whose receivers fire at their own rates (<see cref="WeaponDef.FireGroups"/>): each
/// receiver's next-shot time, spool-up and burst count, and each feed's rounds and reload. One per weapon slot of the
/// local player (<see cref="WeaponState.Groups"/>) and per bot. Fixed arrays: firing never allocates.
/// </summary>
public sealed class FireGroupState
{
    public const int Max = ReceiverFire.Max;

    /// <summary>The weapon this state is for (a new def resets it).</summary>
    public WeaponDef? For;

    public readonly double[] Next = new double[Max];
    public readonly float[] Spool = new float[Max];
    public readonly int[] BurstLeft = new int[Max];

    /// <summary>Rounds in each feed (index <see cref="ReceiverFire.Feed"/>).</summary>
    public readonly int[] Ammo = new int[Max];

    /// <summary>When each feed's own reload ends (0: not reloading).</summary>
    public readonly double[] ReloadUntil = new double[Max];
    public readonly double[] ReloadStarted = new double[Max];

    /// <summary>The weapon's total rounds as this state last wrote them (anything else changed them: refill, max ammo).</summary>
    public int Seen = -1;

    private double _spoolAt;

    /// <summary>Feeds the weapon has (receivers on a shared feed count once).</summary>
    public static int FeedsOf(ReceiverFire[] fg)
    {
        var n = 0;
        foreach (var f in fg)
            n = Math.Max(n, f.Feed + 1);
        return n;
    }

    /// <summary>The magazine size of feed <paramref name="feed"/>.</summary>
    public static int FeedMag(ReceiverFire[] fg, int feed)
    {
        foreach (var f in fg)
            if (f.Feed == feed)
                return f.FeedMag;
        return 0;
    }

    /// <summary>Rounds in every feed.</summary>
    public int Total(ReceiverFire[] fg)
    {
        var t = 0;
        for (var f = FeedsOf(fg) - 1; f >= 0; f--)
            t += Ammo[f];
        return t;
    }

    /// <summary>Puts <paramref name="total"/> rounds into the feeds, filling them in order; cancels their own reloads.</summary>
    public void Fill(ReceiverFire[] fg, int total)
    {
        var feeds = FeedsOf(fg);
        for (var f = 0; f < feeds; f++)
        {
            var take = Math.Clamp(total, 0, FeedMag(fg, f));
            Ammo[f] = take;
            total -= take;
            ReloadUntil[f] = 0;
        }
    }

    /// <summary>Clears every receiver's trigger state (a new weapon, a respawn).</summary>
    public void Reset(WeaponDef def)
    {
        For = def;
        Array.Clear(Next);
        Array.Clear(Spool);
        Array.Clear(BurstLeft);
        Array.Clear(ReloadUntil);
        Seen = -1;
    }

    /// <summary>Stops every burst in progress (a reload, a weapon switch).</summary>
    public void StopBursts() => Array.Clear(BurstLeft);

    /// <summary>
    /// Keeps the feeds in step with the weapon's round count: a new weapon starts over; a count someone else changed (a
    /// full reload, a refill, Max Ammo) is spread over the feeds again.
    /// </summary>
    public void Sync(WeaponDef def, ref int ammo)
    {
        var fg = def.FireGroups!;
        if (!ReferenceEquals(For, def))
            Reset(def);
        if (ammo != Seen)
        {
            Fill(fg, ammo);
            ammo = Total(fg);
            Seen = ammo;
        }
    }

    /// <summary>Winds each spooling receiver up while the trigger is held, down twice as fast when released. Returns the highest rate.</summary>
    public float UpdateSpool(ReceiverFire[] fg, bool held, double now)
    {
        var dt = (float)Math.Clamp(now - _spoolAt, 0, 0.25);
        _spoolAt = now;
        var best = 0f;
        for (var g = 0; g < fg.Length; g++)
        {
            var f = fg[g];
            if (f.SpoolTime <= 0)
            {
                Spool[g] = 1f;
                best = 1f;
                continue;
            }
            Spool[g] = Math.Clamp(Spool[g] + (held ? dt : -2f * dt) / f.SpoolTime, 0f, 1f);
            best = MathF.Max(best, RateOf(fg, g));
        }
        return fg.Length > 0 ? best : 1f;
    }

    /// <summary>Fraction of its full rate receiver <paramref name="g"/> fires at now (its spool).</summary>
    public float RateOf(ReceiverFire[] fg, int g) => fg[g].SpoolTime > 0 ? float.Lerp(fg[g].SpoolStart, 1f, Spool[g]) : 1f;

    /// <summary>Seconds to receiver <paramref name="g"/>'s next shot at its current spool.</summary>
    public float IntervalOf(ReceiverFire[] fg, int g) => fg[g].FireInterval / MathF.Max(0.05f, RateOf(fg, g));

    /// <summary>
    /// The receivers that fire now, by their own triggers (bit g = receiver g), taking a round each from their feeds and
    /// scheduling their next shots. <paramref name="first"/>: the first pass this frame (a pull only counts once).
    /// Full auto and spooling receivers fire while held, at their own rate; a burst receiver fires its burst on a pull
    /// and then waits for the next one; semi, pump, bolt and lever fire once a pull, after their own cycle time. A
    /// receiver with an empty feed (or reloading it) doesn't fire. <paramref name="gate"/>: nothing fires before it (the
    /// weapon's draw, a melee). <paramref name="infinite"/>: shots take no rounds.
    /// </summary>
    public ushort Step(ReceiverFire[] fg, double now, bool held, bool pressed, bool first, double gate, float intervalMult, bool infinite)
    {
        ushort mask = 0;
        for (var g = 0; g < fg.Length; g++)
        {
            var f = fg[g];
            if (first)
                Next[g] = Math.Max(Next[g], now - 0.02); // no banking shots while idle; a frame's worth may catch up
            if (ReloadUntil[f.Feed] > 0 || Ammo[f.Feed] <= 0 || now < Next[g] || now < gate)
                continue;
            bool fire;
            switch (f.Mode)
            {
                case FireMode.Auto:
                    fire = held;
                    break;
                case FireMode.Burst:
                    if (BurstLeft[g] == 0 && pressed && first)
                        BurstLeft[g] = f.BurstCount;
                    fire = BurstLeft[g] > 0;
                    break;
                default:
                    fire = pressed && first;
                    break;
            }
            if (!fire)
                continue;
            mask |= (ushort)(1 << g);
            if (!infinite)
                Ammo[f.Feed]--;
            Next[g] += IntervalOf(fg, g) * intervalMult;
            if (f.Mode == FireMode.Burst && --BurstLeft[g] == 0)
                Next[g] += f.BurstDelay;
        }
        return mask;
    }

    /// <summary>The earliest any receiver can fire again (bots wait for it).</summary>
    public double Earliest(ReceiverFire[] fg)
    {
        var t = double.MaxValue;
        for (var g = 0; g < fg.Length; g++)
            t = Math.Min(t, Next[g]);
        return t;
    }

    /// <summary>The share of the whole volley's recoil that the receivers in <paramref name="mask"/> kick with (1: all of them).</summary>
    public static float RecoilScale(ReceiverFire[] fg, ushort mask)
    {
        var share = 0f;
        for (var g = 0; g < fg.Length; g++)
            if ((mask & (1 << g)) != 0)
                share += fg[g].RecoilShare;
        return MathF.Sqrt(share);
    }
}

/// <summary>
/// Firing built weapons whose receivers have their own fire rates (<see cref="WeaponDef.FireGroups"/>): the local
/// trigger (predicted; the Fire message says which receivers fired), each feed's own reload, the server's per-receiver
/// fire-rate budget and bots' triggers.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// The local trigger for a <see cref="WeaponDef.FireGroups"/> weapon (from <see cref="UpdateTrigger"/>): every ready
    /// receiver fires on the same pass as one shot (one message, one sound), up to four passes a frame for fast receivers
    /// at a low frame rate.
    /// </summary>
    private void TriggerGroups(WeaponState w, WeaponDef def, double now, bool held, bool pressed)
    {
        var fg = def.FireGroups!;
        var gs = w.Groups ??= new FireGroupState();
        gs.Sync(def, ref w.Ammo);
        SpoolRate = gs.UpdateSpool(fg, held, now);
        FinishFeedReloads(w, gs, fg, now);
        var infinite = InfiniteAmmoOn(now);
        var mult = ZombieFireIntervalMult(Local);
        for (var i = 0; i < 4; i++)
        {
            var mask = gs.Step(fg, now, held, pressed, i == 0, NextFireTime, mult, infinite);
            if (mask == 0)
                break;
            w.Ammo = gs.Seen = gs.Total(fg);
            FireOnce(now, mask);
        }
        StartFeedReloads(w, gs, fg, now);
    }

    /// <summary>A feed that ran dry while the others still have rounds reloads on its own, in its receiver's own time.</summary>
    private void StartFeedReloads(WeaponState w, FireGroupState gs, ReceiverFire[] fg, double now)
    {
        if (w.Ammo <= 0 || w.Reserve == 0)
            return; // all dry: the whole weapon reloads (UpdateTrigger); no reserve: nothing to reload with
        foreach (var f in fg)
        {
            if (gs.Ammo[f.Feed] > 0 || gs.ReloadUntil[f.Feed] > 0)
                continue;
            gs.ReloadStarted[f.Feed] = now;
            gs.ReloadUntil[f.Feed] = now + f.Reload * StreakReloadMult(Local, now) * Perks.ReloadMult(Local!.Perks) * FrenzyBuffs.Value(Local, w.Def, FrenzyStat.Reload);
        }
    }

    private static void FinishFeedReloads(WeaponState w, FireGroupState gs, ReceiverFire[] fg, double now)
    {
        var changed = false;
        foreach (var f in fg)
        {
            if (gs.ReloadUntil[f.Feed] <= 0 || now < gs.ReloadUntil[f.Feed])
                continue;
            gs.ReloadUntil[f.Feed] = 0;
            var take = f.FeedMag - gs.Ammo[f.Feed];
            if (w.Reserve >= 0)
            {
                take = Math.Min(take, w.Reserve); // Zombies: the reload comes out of the reserve
                w.Reserve -= take;
            }
            gs.Ammo[f.Feed] += Math.Max(0, take);
            changed = true;
        }
        if (changed)
            w.Ammo = gs.Seen = gs.Total(fg);
    }

    /// <summary>A reload or a weapon switch stops any receiver's burst (as it stops a plain gun's).</summary>
    private void StopGroupBursts() => Active.Groups?.StopBursts();

    /// <summary>
    /// Server: the per-receiver fire-rate budget of a <see cref="WeaponDef.FireGroups"/> shot. Each receiver in
    /// <paramref name="mask"/> (0: all of them) costs about one of its own fire intervals, with up to 0.3 s banked; a
    /// receiver over its budget is dropped from the shot. Returns the receivers that fire (0: none).
    /// </summary>
    private ushort ServerGroupBudget(Player shooter, ReceiverFire[] fg, ushort mask, double now)
    {
        var all = (ushort)((1 << fg.Length) - 1);
        mask = mask == 0 ? all : (ushort)(mask & all);
        var clocks = shooter.GroupShotClock;
        var mult = ZombieFireIntervalMult(shooter);
        for (var g = 0; g < fg.Length; g++)
        {
            if ((mask & (1 << g)) == 0)
                continue;
            var c = Math.Max(clocks[g], now - 0.3);
            if (c > now + 0.05)
            {
                mask &= (ushort)~(1 << g);
                continue;
            }
            clocks[g] = c + fg[g].FireInterval * 0.85 * mult; // a spooling receiver only ever fires slower
        }
        return mask;
    }

    /// <summary>
    /// Bots with a <see cref="WeaponDef.FireGroups"/> weapon: the same per-receiver trigger with it held and pulled every
    /// time (bursts back to back after their pause, semi receivers at their own cycle). Returns the receivers that fire
    /// now (0: none ready) and moves <paramref name="nextFire"/> to the next time any can.
    /// </summary>
    internal ushort BotFireGroups(BotBrain b, Player p, WeaponDef def, double now, ref double nextFire)
    {
        var fg = def.FireGroups!;
        var gs = b.FireGroups ??= new FireGroupState();
        if (!ReferenceEquals(gs.For, def))
            gs.Reset(def);
        for (var f = FireGroupState.FeedsOf(fg) - 1; f >= 0; f--)
            gs.Ammo[f] = int.MaxValue; // bots count their rounds in BotBrain.Ammo
        gs.UpdateSpool(fg, true, now); // a bot's trigger is held while it shoots
        var mask = gs.Step(fg, now, true, true, true, 0, ZombieFireIntervalMult(p), true);
        nextFire = gs.Earliest(fg);
        return mask;
    }

    /// <summary>The client's predicted rays of its own shot, reused (no list per shot).</summary>
    private readonly List<ShotRay> _predictRays = new(16);

    /// <summary>Self-test: the server's per-receiver budget for one shot of <paramref name="def"/> naming <paramref name="mask"/>.</summary>
    internal ushort SelfTestGroupBudget(Player p, WeaponDef def, ushort mask, double now) => ServerGroupBudget(p, def.FireGroups!, mask, now);

    /// <summary>Receivers in a fire mask.</summary>
    public static int GroupCount(ushort mask) => BitOperations.PopCount(mask);
}
