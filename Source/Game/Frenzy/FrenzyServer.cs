namespace CallOfSlop;

/// <summary>
/// Server-authoritative kill frenzy per player (the same heat model as the client's music meter,
/// <see cref="FrenzyMeterCore"/>). Kills feed the killer's meter as they happen (<see cref="DamagePlayer"/>, and
/// zombie kills at half weight via the meter's Zombies tuning); dying, or being downed in Zombies, wipes it; outside a
/// live match it's off. The level (0-6) and the frenzy class go to everyone in the snapshot's slow fields (2 bytes,
/// protocol 26), so other features can show any player's frenzy, and the buffs (FrenzyBuffs.cs) are predicted.
/// The class rule: the class of the weapon that got the most recent kill. Kills without a gun (grenades and other
/// throwables, kill streaks, melee, explosive props) still add heat but keep the previous class. Bots have a frenzy
/// and get the buffs too, the same as people (fair, and their frenzy shows like anyone's).
/// </summary>
public sealed partial class Game
{
    /// <summary>Host setting "Frenzy buffs" (default on): the per-class gameplay buffs of a frenzy. Server-owned; clients learn it from the snapshot.</summary>
    public bool FrenzyBuffsEnabled = true;

    private readonly bool[] _frenzyAlive = new bool[256];

    /// <summary>
    /// Server ticks a frenzy the player just lost (a lower level, another class, buffs switched off) is still allowed
    /// for in the checks against client prediction (move speed, minimum spread): the client hears about the change one
    /// snapshot and a ping later.
    /// </summary>
    public const uint FrenzyGraceTicks = 90;

    private readonly (WeaponClass Cls, byte Level, bool On)[] _frenzyNow = new (WeaponClass, byte, bool)[256], _frenzyPrev = new (WeaponClass, byte, bool)[256];
    private readonly uint[] _frenzyPrevUntil = new uint[256];

    /// <summary>Remembers the frenzy a player had before a change, for <see cref="FrenzyGraceTicks"/>.</summary>
    private void FrenzyNoteChange(Player p)
    {
        var cur = (p.FrenzyClass, p.FrenzyLevel, p.FrenzyBuffsOn);
        if (cur == _frenzyNow[p.Id])
            return;
        _frenzyPrev[p.Id] = _frenzyNow[p.Id];
        _frenzyPrevUntil[p.Id] = ServerTick + FrenzyGraceTicks;
        _frenzyNow[p.Id] = cur;
    }

    /// <summary>Server: the highest mobility <paramref name="p"/>'s client may predict with <paramref name="def"/> (now, or just before a frenzy change).</summary>
    private float FrenzyAllowedMobility(Player p, WeaponDef def)
    {
        var m = FrenzyBuffs.Mobility(p, def);
        if (ServerTick <= _frenzyPrevUntil[p.Id] && _frenzyPrev[p.Id] is var (c, l, on))
            m = MathF.Max(m, FrenzyBuffs.MobilityWith(def, FrenzyBuffs.Value(c, l, on, def, FrenzyStat.MovePenalty)));
        return m;
    }

    /// <summary>Server: the smallest cone multiplier <paramref name="p"/>'s client may use with <paramref name="def"/> (now, or just before a frenzy change).</summary>
    private float FrenzyAllowedConeMult(Player p, WeaponDef def)
    {
        var m = FrenzyBuffs.MinSpreadMult(p.FrenzyClass, p.FrenzyLevel, p.FrenzyBuffsOn, def);
        if (ServerTick <= _frenzyPrevUntil[p.Id] && _frenzyPrev[p.Id] is var (c, l, on))
            m = MathF.Min(m, FrenzyBuffs.MinSpreadMult(c, l, on, def));
        return m;
    }

    /// <summary>True when a kill id is a gun (not a streak, melee or throwable): only those set the frenzy class.</summary>
    public static bool FrenzyGunKill(byte killId) => killId < KillInfo.StreakKillIdFirst;

    /// <summary>The frenzy class byte on the wire: the class in the low 7 bits, bit 7 when the buffs are on.</summary>
    internal static byte FrenzyWire(Player p) => (byte)(((byte)p.FrenzyClass & 0x7F) | (p.FrenzyBuffsOn ? 0x80 : 0));

    /// <summary>Client: the frenzy fields from a snapshot slow entry.</summary>
    internal static void FrenzyFromWire(Player p, byte level, byte cls)
    {
        p.FrenzyLevel = (byte)Math.Min((int)level, FrenzyMeterCore.MaxLevel);
        var c = (WeaponClass)(cls & 0x7F);
        p.FrenzyClass = Enum.IsDefined(c) ? c : WeaponClass.Special;
        p.FrenzyBuffsOn = (cls & 0x80) != 0;
    }

    private FrenzyMeterCore FrenzyMeterOf(Player p) => p.FrenzyHeat ??= new FrenzyMeterCore();

    /// <summary>Server, every tick: decay, deaths and downs, the live-match gate and the buffs setting.</summary>
    private void ServerTickFrenzy(double now)
    {
        var live = Phase == MatchPhase.Playing;
        var zombies = Mode == GameMode.Zombies;
        foreach (var p in Players.Values)
        {
            var m = FrenzyMeterOf(p);
            p.FrenzyBuffsOn = FrenzyBuffsEnabled;
            var alive = p.Alive && !(zombies && IsDowned(p));
            if (!live)
            {
                m.Reset();
                p.FrenzyLevel = 0;
                _frenzyAlive[p.Id] = alive;
                FrenzyNoteChange(p);
                continue;
            }
            m.Zombies = zombies;
            if (_frenzyAlive[p.Id] && !alive)
                m.OnDeath(now);
            _frenzyAlive[p.Id] = alive;
            m.Update(now);
            p.FrenzyLevel = alive ? (byte)m.Level : (byte)0;
            FrenzyNoteChange(p);
        }
    }

    /// <summary>Server: a player kill (from <see cref="DamagePlayer"/>): the victim's frenzy ends, the killer's grows.</summary>
    private void FrenzyOnKill(Player attacker, Player victim, byte killId, bool head, double now)
    {
        var vm = FrenzyMeterOf(victim);
        vm.OnDeath(now);
        victim.FrenzyLevel = 0;
        _frenzyAlive[victim.Id] = false;
        FrenzyNoteChange(victim);
        if (attacker == victim)
            return; // suicides don't count (the client meter ignores them too)
        FrenzyAddKill(attacker, head, FrenzyGunKill(killId) ? attacker.DefFor(killId) : null, now);
    }

    /// <summary>Server: a zombie kill (half weight). <paramref name="noGun"/>: melee or an explosion, which keep the class.</summary>
    private void FrenzyOnZombieKill(Player attacker, bool head, bool noGun, double now) =>
        FrenzyAddKill(attacker, head, noGun ? null : attacker.WeaponDef, now);

    private void FrenzyAddKill(Player p, bool head, WeaponDef? gun, double now)
    {
        if (Phase != MatchPhase.Playing)
            return;
        var m = FrenzyMeterOf(p);
        m.Zombies = Mode == GameMode.Zombies;
        m.OnKill(now, head);
        if (gun != null)
            p.FrenzyClass = gun.Class;
        p.FrenzyBuffsOn = FrenzyBuffsEnabled;
        var alive = p.Alive && !(m.Zombies && IsDowned(p));
        p.FrenzyLevel = alive ? (byte)m.Level : (byte)0;
        FrenzyNoteChange(p);
        // Special's novelty buff: a kill with a Special weapon gives health back (from frenzy level 3).
        if (gun != null && alive)
        {
            var heal = (int)MathF.Round(FrenzyBuffs.Value(p, gun, FrenzyStat.KillHeal));
            if (heal > 0)
                p.Health = Math.Min(World.MaxHealth, p.Health + heal);
        }
    }

    /// <summary>Self-test: run the frenzy tick now.</summary>
    public void SelfTestFrenzyTick(double now) => ServerTickFrenzy(now);

    /// <summary>Self-test: <paramref name="attacker"/> kills <paramref name="victim"/> with <paramref name="killId"/> through the normal damage path.</summary>
    public bool SelfTestFrenzyKill(Player attacker, Player victim, byte killId, bool head, double now) =>
        DamagePlayer(attacker, victim, 10000, head, killId, now, killId == MeleeKillId ? DamageKind.Melee : DamageKind.Bullet);

    /// <summary>Self-test: what the server allows <paramref name="p"/>'s client to predict with <paramref name="def"/> (mobility, cone multiplier).</summary>
    public (float Mobility, float Cone) SelfTestFrenzyAllowed(Player p, WeaponDef def) => (FrenzyAllowedMobility(p, def), FrenzyAllowedConeMult(p, def));

    /// <summary>Self-test: advance the server tick (for the grace window) without simulating anything.</summary>
    public void SelfTestAdvanceTicks(uint ticks) => ServerTick += ticks;
}
