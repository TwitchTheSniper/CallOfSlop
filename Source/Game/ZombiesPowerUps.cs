using System.Numerics;

namespace CallOfSlop;

/// <summary>The World at War power-ups (docs/zombies/ZOMBIES-MODE.md, "Power-ups"). The order is the rules file's "weights" order.</summary>
public enum ZPowerKind : byte
{
    /// <summary>Every player's guns get a full reserve.</summary>
    MaxAmmo,

    /// <summary>Every hit on a zombie kills it, for a while.</summary>
    InstaKill,

    /// <summary>Every point earned counts twice, for a while.</summary>
    DoublePoints,

    /// <summary>Every zombie alive dies at once; everyone gets points.</summary>
    Nuke,

    /// <summary>Every window's boards go back up; everyone gets points.</summary>
    Carpenter,

    /// <summary>Shots don't use ammo and magazines stay full, for a while (protocol 49; new kinds go at the end: the id is a wire byte).</summary>
    InfiniteAmmo,

    COUNT,
}

/// <summary>A power-up floating where a zombie died: walk into it to take it (for everyone).</summary>
public sealed class ZPowerUp
{
    public ushort Id;
    public ZPowerKind Kind;
    public Vector3 At;

    /// <summary>When it vanishes (the server's clock on the host; each client's own clock on clients).</summary>
    public double Until;
}

/// <summary>
/// Zombies power-ups (milestone 3): a kill may drop one (the rules' chance, at most so many a round), it floats glowing
/// for 30 s (blinking at the end) and the first player standing who walks into it takes it for the whole team: Max
/// Ammo, Insta-Kill, Double Points, Nuke, Carpenter, Infinite Ammo. Server-owned; clients get the floating ones and the timers in
/// <see cref="Msg.ZWorld"/>, the feed line and the banner as usual, and Max Ammo as a <see cref="ZEventKind.MaxAmmo"/>.
/// </summary>
public sealed partial class Game
{
    /// <summary>Power-ups floating on the map.</summary>
    public readonly List<ZPowerUp> ZPowerUps = new();

    /// <summary>When Insta-Kill, Double Points and Infinite Ammo run out (the same clock as <see cref="ZPowerUp.Until"/>).</summary>
    public double ZInstaKillUntil, ZDoublePointsUntil, ZInfiniteAmmoUntil;

    /// <summary>Most power-ups floating at once (and in one ZWorld message).</summary>
    public const int MaxPowerUps = 16;

    private int _zPowerThisRound;

    /// <summary>The drop schedule (server): points the team has earned this run, the total at which the next drop is due, the current step.</summary>
    private double _zPowerEarned, _zPowerDueAt, _zPowerStep;

    /// <summary>Self-test and HUD-free info: the team's points earned this run and when the next power-up is due.</summary>
    public double PowerEarned => _zPowerEarned;

    public double PowerDueAt => _zPowerDueAt > 0 ? _zPowerDueAt : Math.Max(1, ZRules.PowerFirstAt);
    private ushort _zNextPower = 1;

    /// <summary>The name players see.</summary>
    public static string PowerName(ZPowerKind k) => k switch
    {
        ZPowerKind.MaxAmmo => "Max Ammo",
        ZPowerKind.InstaKill => "Insta-Kill",
        ZPowerKind.DoublePoints => "Double Points",
        ZPowerKind.Nuke => "Nuke",
        ZPowerKind.Carpenter => "Carpenter",
        ZPowerKind.InfiniteAmmo => "Infinite Ammo",
        _ => "?",
    };

    /// <summary>Its glow and its HUD colour.</summary>
    public static Vector3 PowerColor(ZPowerKind k) => k switch
    {
        ZPowerKind.MaxAmmo => new Vector3(0.35f, 1f, 0.35f),
        ZPowerKind.InstaKill => new Vector3(0.95f, 0.95f, 0.85f),
        ZPowerKind.DoublePoints => new Vector3(1f, 0.85f, 0.25f),
        ZPowerKind.Nuke => new Vector3(1f, 0.45f, 0.15f),
        ZPowerKind.InfiniteAmmo => new Vector3(0.85f, 0.4f, 1f),
        _ => new Vector3(0.45f, 0.75f, 1f),
    };

    /// <summary>Seconds of Insta-Kill / Double Points left at <paramref name="now"/>.</summary>
    public double InstaKillLeft(double now) => Math.Max(0, ZInstaKillUntil - now);

    public double DoublePointsLeft(double now) => Math.Max(0, ZDoublePointsUntil - now);

    public double InfiniteAmmoLeft(double now) => Math.Max(0, ZInfiniteAmmoUntil - now);

    /// <summary>Both sides: Infinite Ammo is running (shots use no ammo, magazines stay full, no reloads). Never outside Zombies.</summary>
    public bool InfiniteAmmoOn(double now) => now < ZInfiniteAmmoUntil && IsZombies;

    /// <summary>The local player's slots while Infinite Ammo runs (every frame): every magazine full, no reload in progress.</summary>
    private void LocalInfiniteAmmo(double now)
    {
        if (!InfiniteAmmoOn(now))
            return;
        for (var s = 0; s < Slots.Length; s++)
            if (Slots[s] is { } w && w.Def.Mag > 0)
            {
                w.Ammo = w.Def.Mag;
                w.ReloadUntil = 0;
            }
    }

    /// <summary>Both sides: no power-ups, no timers (a new run).</summary>
    private void ResetPowerUps()
    {
        ZPowerUps.Clear();
        ZInstaKillUntil = ZDoublePointsUntil = ZInfiniteAmmoUntil = 0;
        _zPowerThisRound = 0;
        _zPowerEarned = 0;
        _zPowerStep = _zPowerDueAt = 0; // (set from the rules on the first kill: StartPowerSchedule)
    }

    private void StartPowerSchedule()
    {
        if (_zPowerStep <= 0)
            _zPowerStep = _zPowerDueAt = Math.Max(1, ZRules.PowerFirstAt);
    }

    /// <summary>Server: points a player earned (hits, kills, boards): twice as many under Double Points. They count toward the team's power-up schedule.</summary>
    private void EarnPoints(Player p, int amount, double now)
    {
        var paid = now < ZDoublePointsUntil ? amount * 2 : amount;
        AddPoints(p, paid);
        if (paid > 0)
            _zPowerEarned += paid;
    }

    /// <summary>
    /// Server: a zombie died at <paramref name="at"/> (a kill by a player): a power-up drops there when the team's points
    /// schedule says one is due (<see cref="ZombiesRules.PowerFirstAt"/>), or on the optional flat chance; at most the
    /// round's cap (a due drop then waits).
    /// </summary>
    private void RollPowerUp(Vector3 at, double now)
    {
        StartPowerSchedule();
        if (_zPowerThisRound >= ZRules.PowerMaxPerRound)
            return;
        var due = ZRules.PowerFirstAt > 0 && _zPowerEarned >= _zPowerDueAt;
        if (!due && (ZRules.PowerDropChance <= 0 || ZRand() >= ZRules.PowerDropChance))
            return;
        if (due)
            AdvancePowerSchedule();
        var kind = PickPowerKind(ZRand());
        if (kind != ZPowerKind.COUNT)
            SpawnPowerUp(kind, at, now);
    }

    /// <summary>A scheduled drop happened: the next is due one (rising) step after the points earned so far.</summary>
    private void AdvancePowerSchedule()
    {
        _zPowerStep = NextPowerStep(ZRules, _zPowerStep);
        _zPowerDueAt = _zPowerEarned + _zPowerStep;
    }

    /// <summary>The step after <paramref name="step"/>: x <see cref="ZombiesRules.PowerStepGrowth"/>, at most <see cref="ZombiesRules.PowerStepMax"/> (0: no cap).</summary>
    public static double NextPowerStep(ZombiesRules r, double step)
    {
        var next = step * r.PowerStepGrowth;
        return r.PowerStepMax > 0 ? Math.Min(next, Math.Max(r.PowerStepMax, r.PowerFirstAt)) : next;
    }

    /// <summary>Self-test: points earned the way play earns them (Double Points and the power-up schedule included).</summary>
    public void SelfTestEarn(Player p, int amount, double now) => EarnPoints(p, amount, now);

    /// <summary>A power-up kind from the rules' weights (<paramref name="roll"/> in 0-1); the Carpenter only when a board is down. COUNT: none.</summary>
    public ZPowerKind PickPowerKind(float roll)
    {
        Span<float> w = stackalloc float[(int)ZPowerKind.COUNT];
        var total = 0f;
        for (var i = 0; i < w.Length; i++)
        {
            w[i] = i < ZRules.PowerWeights.Length ? ZRules.PowerWeights[i] : 0f;
            if (i == (int)ZPowerKind.Carpenter && !AnyBoardDown())
                w[i] = 0;
            total += w[i];
        }
        if (total <= 0)
            return ZPowerKind.COUNT;
        var pick = roll * total;
        for (var i = 0; i < w.Length; i++)
        {
            pick -= w[i];
            if (pick < 0 && w[i] > 0)
                return (ZPowerKind)i;
        }
        for (var i = w.Length - 1; i >= 0; i--)
            if (w[i] > 0)
                return (ZPowerKind)i;
        return ZPowerKind.COUNT;
    }

    private bool AnyBoardDown()
    {
        if (World.Map.Zombies is not { } zm)
            return false;
        for (var i = 0; i < zm.Windows.Count && i < ZBoards.Length; i++)
            if (ZBoards[i] < zm.Windows[i].Boards)
                return true;
        return false;
    }

    /// <summary>
    /// Server: a power-up of <paramref name="kind"/> appears at <paramref name="at"/> (moved inside when it's outside the
    /// areas: a zombie killed at a window drops it at the window's inside spot, where players can reach it).
    /// </summary>
    public ZPowerUp SpawnPowerUp(ZPowerKind kind, Vector3 at, double now)
    {
        if (World.Map.Zombies is { } zm && !zm.Inside(at))
        {
            var best = -1;
            var bestD = float.MaxValue;
            for (var i = 0; i < zm.Windows.Count; i++)
            {
                var d = Vector3.DistanceSquared(zm.Windows[i].Outside, at);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            if (best >= 0)
                at = zm.Windows[best].Inside;
        }
        if (ZPowerUps.Count >= MaxPowerUps)
            ZPowerUps.RemoveAt(0);
        var pu = new ZPowerUp { Id = _zNextPower++, Kind = kind, At = at, Until = now + ZRules.PowerLifetime };
        if (_zNextPower == 0)
            _zNextPower = 1;
        ZPowerUps.Add(pu);
        _zPowerThisRound++;
        Audio.Effect3D(Sfx.StreakCall, at + new Vector3(0, 1f, 0), 0.7f, MixBus.Zombies);
        return pu;
    }

    /// <summary>Server, every tick: power-ups run out, standing players walk into them.</summary>
    private void TickPowerUps(double now)
    {
        for (var i = ZPowerUps.Count - 1; i >= 0; i--)
            if (ZPowerUps[i].Until <= now)
                ZPowerUps.RemoveAt(i);
        if (ZPowerUps.Count == 0)
            return;
        var r2 = ZRules.PowerPickupRange * ZRules.PowerPickupRange;
        foreach (var p in Players.Values)
        {
            if (!IsStanding(p))
                continue;
            for (var i = ZPowerUps.Count - 1; i >= 0; i--)
            {
                var pu = ZPowerUps[i];
                var dx = pu.At.X - p.Position.X;
                var dz = pu.At.Z - p.Position.Z;
                if (dx * dx + dz * dz > r2 || MathF.Abs(pu.At.Y - p.Position.Y) > 2f)
                    continue;
                ZPowerUps.RemoveAt(i);
                GrabPowerUp(p, pu.Kind, now);
            }
        }
    }

    /// <summary>Server: <paramref name="p"/> took a power-up: it works for the whole team.</summary>
    public void GrabPowerUp(Player p, ZPowerKind kind, double now)
    {
        var name = PowerName(kind);
        var sound = Sfx.StreakReady;
        switch (kind)
        {
            case ZPowerKind.MaxAmmo:
                foreach (var o in Players.Values)
                    if (o.Alive)
                        MaxAmmoFor(o);
                sound = Sfx.CrateLand;
                break;
            case ZPowerKind.InstaKill:
                ZInstaKillUntil = now + ZRules.DurationOf(kind); // a second one restarts the clock (it doesn't add up)
                break;
            case ZPowerKind.DoublePoints:
                ZDoublePointsUntil = now + ZRules.DurationOf(kind);
                sound = Sfx.LevelUp;
                break;
            case ZPowerKind.InfiniteAmmo:
                // Timed, like Insta-Kill: a second one restarts the clock. Every player's magazines fill now (the host's
                // own slots at once, the clients' from the timer in the next ZWorld) and stay full until it runs out.
                ZInfiniteAmmoUntil = now + ZRules.DurationOf(kind);
                foreach (var o in Players.Values)
                    if (o.Bot is { } ib)
                    {
                        ib.Ammo = o.DefFor(o.Weapon).Mag;
                        ib.ReloadUntil = 0;
                    }
                LocalInfiniteAmmo(now);
                sound = Sfx.LockedTone;
                break;
            case ZPowerKind.Nuke:
            {
                // Every zombie alive dies (no kill points, no drops: the Nuke's own points instead).
                foreach (var z in Horde)
                    if (z.State != ZState.Dead)
                    {
                        z.Health = 0;
                        SetZState(z, ZState.Dead, now);
                    }
                foreach (var o in Players.Values)
                    if (o.Alive)
                        AddPoints(o, ZRules.NukePoints);
                sound = Sfx.Explosion;
                break;
            }
            case ZPowerKind.Carpenter:
                if (World.Map.Zombies is { } zm)
                    for (var i = 0; i < zm.Windows.Count && i < ZBoards.Length; i++)
                        ZBoards[i] = (byte)zm.Windows[i].Boards;
                foreach (var o in Players.Values)
                    if (o.Alive)
                        AddPoints(o, ZRules.CarpenterPoints);
                sound = Sfx.BreakWood;
                break;
        }
        ModeEvent(kind switch
        {
            ZPowerKind.MaxAmmo => $"{p.Name} grabbed Max Ammo: full reserve for everyone!",
            ZPowerKind.InstaKill => $"{p.Name} grabbed Insta-Kill: every hit kills for {ZRules.DurationOf(kind):0} seconds!",
            ZPowerKind.DoublePoints => $"{p.Name} grabbed Double Points for {ZRules.DurationOf(kind):0} seconds!",
            ZPowerKind.InfiniteAmmo => $"{p.Name} grabbed Infinite Ammo: no reloads for {ZRules.DurationOf(kind):0} seconds!",
            ZPowerKind.Nuke => $"{p.Name} grabbed a Nuke! +{ZRules.NukePoints} for everyone.",
            _ => $"{p.Name} grabbed the Carpenter: every barrier rebuilt, +{ZRules.CarpenterPoints} for everyone.",
        }, sound);
        ZBanner(name.ToUpperInvariant() + "!");
    }

    /// <summary>Server: <paramref name="p"/>'s guns get a full reserve (Max Ammo; the magazines stay as they are).</summary>
    private void MaxAmmoFor(Player p)
    {
        if (p.Bot is { } b)
            b.Ammo = p.DefFor(p.Weapon).Mag;
        if (p.Id == LocalId)
            LocalMaxAmmo();
        else
            SendZEvent(p, ZEventKind.MaxAmmo, 0, "");
    }

    /// <summary>The local player's guns: a full reserve in every slot.</summary>
    private void LocalMaxAmmo()
    {
        for (var s = 0; s < 4 && s < Slots.Length; s++)
            if (Slots[s] is { } w)
                w.Reserve = w.Def.Mag * ZRules.ReserveMags;
    }

    // ---- Network (the tail of ZWorld) --------------------------------------------------------------------

    /// <summary>The floating power-ups and the three timers, at the end of <see cref="Msg.ZWorld"/> (Infinite Ammo's: protocol 49).</summary>
    private void WritePowerUps(PacketWriter w, double now)
    {
        var n = Math.Min(ZPowerUps.Count, MaxPowerUps);
        w.U8((byte)n);
        for (var i = 0; i < n; i++)
        {
            var pu = ZPowerUps[i];
            w.U16(pu.Id).U8((byte)pu.Kind).U16(Q(pu.At.X, 32)).U16(Q(pu.At.Y, 64)).U16(Q(pu.At.Z, 32))
                .U16((ushort)Math.Clamp((int)Math.Round((pu.Until - now) * 10), 0, 65535));
        }
        w.U16(Tenths(InstaKillLeft(now))).U16(Tenths(DoublePointsLeft(now))).U16(Tenths(InfiniteAmmoLeft(now)));
    }

    private static ushort Tenths(double seconds) => (ushort)Math.Clamp((int)Math.Round(seconds * 10), 0, 65535);

    private void ReadPowerUps(ref PacketReader r, double now)
    {
        if (!r.Has(1))
            return;
        int n = r.U8();
        ZPowerUps.Clear();
        for (var i = 0; i < n && r.Has(11); i++)
        {
            var id = r.U16();
            var kind = (ZPowerKind)Math.Min(r.U8(), (byte)(ZPowerKind.COUNT - 1));
            var at = new Vector3(DQ(r.U16(), 32), DQ(r.U16(), 64), DQ(r.U16(), 32));
            var left = r.U16() / 10.0;
            ZPowerUps.Add(new ZPowerUp { Id = id, Kind = kind, At = at, Until = now + left });
        }
        if (!r.Has(4))
            return;
        ZInstaKillUntil = now + r.U16() / 10.0;
        ZDoublePointsUntil = now + r.U16() / 10.0;
        if (!r.Has(2))
            return;
        var wasOn = InfiniteAmmoOn(now);
        ZInfiniteAmmoUntil = now + r.U16() / 10.0;
        if (!wasOn)
            LocalInfiniteAmmo(now); // (it just started: the magazines fill now, not on the next frame)
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------

    public int SelfTestPowerThisRound => _zPowerThisRound;

    public void SelfTestTickPowerUps(double now) => TickPowerUps(now);
}
