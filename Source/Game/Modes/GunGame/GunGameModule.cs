namespace CallOfSlop;

/// <summary>
/// Gun Game's weapon ladder: the weapons in the order Progression unlocks them (starters first), cut down to
/// <see cref="Steps"/> evenly spaced steps so a match doesn't take 50 kills. The first step is the first weapon of the
/// unlock order and the last step is the last one. Everyone plays the ladder whatever they have unlocked.
/// </summary>
public static class GunGame
{
    public const int Steps = 20;

    /// <summary>
    /// Everyone carries the same perks in Gun Game: speed and a full heal after every kill, to keep the climb moving.
    /// No damage perk, so every gun on the ladder hits exactly as hard as it normally does.
    /// </summary>
    public static readonly PerkSet Perks = new(PerkId.Afterburner, PerkId.Bloodthirst, PerkId.None);

    private static WeaponDef[]? _ladder;
    private static WeaponDef[]? _builtFrom;

    public static WeaponDef[] Ladder
    {
        get
        {
            if (_ladder == null || _builtFrom != Weapons.All)
            {
                _builtFrom = Weapons.All;
                // Launchers sit out: a rocket a kill would turn the climb into a splash fest (and their ammo is limited).
                var order = Progress.All.Where(u => u.Weapon is { IsLauncher: false }).Select(u => u.Weapon!).Distinct().ToList();
                _ladder = Build(order.Count > 0 ? order : [Weapons.DefaultPrimary]);
            }
            return _ladder;
        }
    }

    /// <summary>Picks <see cref="Steps"/> evenly spaced weapons from an ordered list (all of them if there are fewer).</summary>
    public static WeaponDef[] Build(IReadOnlyList<WeaponDef> order)
    {
        if (order.Count <= Steps)
            return order.ToArray();
        var r = new WeaponDef[Steps];
        for (var i = 0; i < Steps; i++)
            r[i] = order[(int)Math.Round(i * (order.Count - 1) / (double)(Steps - 1))];
        return r;
    }

    public static int Final => Ladder.Length - 1;

    public static WeaponDef At(int level) => Ladder[Math.Clamp(level, 0, Final)];
}

/// <summary>
/// Gun Game: everyone climbs the same weapon ladder (<see cref="GunGame"/>). Each kill moves you up a weapon; a melee
/// kill also sets the victim back one. A kill with the last gun wins. No loadouts, no streaks, shared perks, no grenades.
/// The ladder step is <see cref="Player.GunLevel"/>, replicated with every player in the shared mode state.
/// </summary>
public sealed class GunGameModule : GameModeModule
{
    public override GameMode Id => GameMode.GunGame;
    public override string Name => "Gun Game";
    public override string Short => "GUN";
    public override string Rules(int limit = 0) =>
        $"Everyone gets the same gun. Each kill moves you up a weapon ({GunGame.Ladder.Length} in all); a melee kill also sets the victim back one. Win with a kill using the last gun. No loadouts: everyone has the same perks (Afterburner, Bloodthirst) and no grenades or equipment.";
    public override bool UsesLoadout => false;
    public override bool AllowsStreaks => false;
    public override PerkSet? FixedPerks => GunGame.Perks;
    public override bool UsesThrowables => false;
    public override bool ServerPicksWeapons => true;
    public override string? LoadoutHint => "Gun Game: everyone gets the same gun, so your loadout isn't used this match.";

    private Player? _winner;

    /// <summary>The player furthest up the ladder (most kills breaks ties).</summary>
    public Player? Leader(Game game) => game.Players.Values.OrderByDescending(p => p.GunLevel).ThenByDescending(p => p.Kills).ThenBy(p => p.Id).FirstOrDefault();

    // ---- Lifecycle ----------------------------------------------------------------------------------------

    public override void OnReset(Game game)
    {
        _winner = null;
        foreach (var p in game.Players.Values)
            p.GunLevel = 0;
    }

    /// <summary>The server hands out the ladder's gun (perks and throwables: the capability flags above).</summary>
    public override void OnSpawn(Game game, Player p) => GiveWeapon(p);

    private static void GiveWeapon(Player p)
    {
        var w = GunGame.At(p.GunLevel);
        p.Primary = p.Secondary = w.Id;
        p.PrimaryKit = p.SecondaryKit = Attachments.DefaultKit(w); // a sniper rung comes with its scope
        p.Weapon = w.Id;
    }

    public override void OnKill(Game game, Player attacker, Player victim, byte killId, double now)
    {
        if (attacker == victim)
            return; // own grenade: no score either way
        if (killId == Game.MeleeKillId && victim.GunLevel > 0)
        {
            victim.GunLevel--;
            game.ModeEvent($"{victim.Name} was knifed back to the {GunGame.At(victim.GunLevel).Name}", Sfx.MeleeHit);
        }
        var atFinal = attacker.GunLevel >= GunGame.Final;
        if (atFinal && killId == GunGame.At(attacker.GunLevel).Id)
            _winner ??= attacker;
        else if (!atFinal)
            LevelUp(game, attacker, attacker.GunLevel + 1);
    }

    private static void LevelUp(Game game, Player p, int level)
    {
        p.GunLevel = (byte)Math.Clamp(level, 0, GunGame.Final);
        if (p.GunLevel == GunGame.Final)
            game.ModeEvent($"{p.Name} is on the last gun: the {GunGame.At(p.GunLevel).Name}!", Sfx.ReconBeep);
        if (!p.Alive)
            return;
        GiveWeapon(p);
        if (p.Bot is { } brain)
        {
            brain.Ammo = p.DefFor(p.Primary).Mag;
            brain.ReloadUntil = 0;
            brain.BurstLeft = 0;
        }
        if (p.Id == game.LocalId)
            game.SyncLocalSlots(p, refill: false);
    }

    /// <summary>A kill with the last gun wins; at the buzzer the highest ladder step (then kills) wins, a tie at the top is a draw.</summary>
    public override void ServerTickPlaying(Game game, double now)
    {
        if (_winner is { } champ)
        {
            game.EndMatch(champ, $"{game.NameOf(champ.Id)} wins with the {GunGame.At(GunGame.Final).Name}", now);
            return;
        }
        if (game.TimeLeft > 0)
            return;
        var best = game.Players.Values.OrderByDescending(p => p.GunLevel).ThenByDescending(p => p.Kills).FirstOrDefault();
        var tied = best != null && game.Players.Values.Count(p => p.GunLevel == best.GunLevel && p.Kills == best.Kills) > 1;
        if (best == null || tied)
            game.EndMatch(null, "Time! Draw", now);
        else
            game.EndMatch(best, $"Time! {game.NameOf(best.Id)} wins on weapon {best.GunLevel + 1}/{GunGame.Ladder.Length}", now);
    }

    // ---- HUD ----------------------------------------------------------------------------------------------

    public override void HudLines(Game game, Player me, List<HudLine> lines, ref System.Numerics.Vector3? pointAt)
    {
        var lvl = me.GunLevel;
        var last = GunGame.Final;
        lines.Add(new HudLine($"WEAPON {lvl + 1}/{last + 1}: {GunGame.At(lvl).Name}", HudOrange));
        lines.Add(new HudLine(lvl >= last ? "LAST GUN - one kill with it wins!" : $"Next: {GunGame.At(lvl + 1).Name}", lvl >= last ? HudRed : HudDim));
        if (Leader(game) is { } lead)
            lines.Add(new HudLine($"Leader: {(lead.Id == game.LocalId ? "you" : game.NameOf(lead.Id))} ({lead.GunLevel + 1}/{last + 1})", lead.Id == game.LocalId ? HudGreen : HudWhite));
    }

    public override (string Big, string Sub)? ScoreBar(Game game) =>
        ($"GUN {(game.Local?.GunLevel ?? 0) + 1}/{GunGame.Ladder.Length}", "Gun Game - a kill with the last gun wins");

    public override (string First, string Second) ScoreboardColumns => ("Gun", "Kills");

    public override (string First, string Second) ScoreboardColumnsShort => ("Gun", "K");

    public override (string First, string Second) ScoreboardCells(Game game, Player p) => (ScoreboardModel.Fraction(p.GunLevel + 1, GunGame.Ladder.Length), ScoreboardModel.Num(p.Kills));

    public override int RankScore(Player p) => p.GunLevel;

    public override string? ResultsLine(Game game) =>
        $"Gun Game - {GunGame.Ladder.Length} weapons, from the {GunGame.At(0).Name} to the {GunGame.At(GunGame.Final).Name}";

    public override string? SelfTestVerify(Game game)
    {
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        var top = game.Players.Values.Max(p => (int)p.GunLevel);
        return kills > 0 && top == 0 ? "kills happened but nobody moved up the ladder" : null;
    }
}
