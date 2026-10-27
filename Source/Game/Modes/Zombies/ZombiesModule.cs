using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Zombies: co-op for 1 to 4 players against endless rounds of zombies, World at War style, only on maps with a "zombies"
/// section. This module is the mode's front door: the rules flags and the hooks route into the Zombies subsystems, which
/// keep their own files (all <c>partial class Game</c>): rounds, the economy, doors, buyables and the workbench
/// (ZombiesMode.cs), the zombies themselves (ZombieAI.cs), the co-op bots (ZombieBots.cs), the packets (ZombiesNet.cs),
/// drops (ZombiesDrops.cs), carts and the slot upgrade (GameZombies.cs), the rules file (Source/Zombies). Its replicated
/// state travels in its own messages (ZWorld, ZEnts...), so the mode-state tail is empty.
/// </summary>
public sealed class ZombiesModule : GameModeModule
{
    public override GameMode Id => GameMode.Zombies;
    public override string Name => "Zombies";
    public override string Short => "ZOM";
    public override string Rules(int limit = 0) =>
        "Co-op for 1 to 4 players against endless rounds of zombies. Earn points, open doors, buy guns off the walls and from the mystery box, and build silly guns at the workbench. The run ends when everyone is down.";
    public override bool CoOp => true;
    public override bool UsesLoadout => false;
    public override bool AllowsStreaks => false;
    public override bool UsesMapVote => false; // a run restarts on its own map
    public override bool HasTimeLimit => false; // the run ends when everyone is down
    public override bool ShowsRespawnMessage => false; // the Zombies HUD says it itself: back next round
    public override bool ShowsKillNotices => false;
    public override bool HostPanelInMatch => true; // Esc > HOST: bots, the next round now or a skip to any round (testing; ZombiesStartRound.cs)
    public override MapNeeds Needs => MapNeeds.ZombiesSection;
    // UsesThrowables stays true and FixedPerks null: the server-side start gear (ZombiesOnSpawn) takes the multiplayer
    // perks and the loadout's throwables away itself and gives the Zombies grenade (ZombiesRules.json "grenades",
    // topped up every round); clients follow the server's count (Msg.ZWorld).
    public override bool ServerSetsThrowables => true;

    public override string MapWarning => "Zombies needs a Zombies map: pick one (Zombies pack: Blackmoor Asylum; Dev pack: Zombies Test, Horde Test), or it plays Free-for-all.";

    public override int MaxPlayers(Map map) => Game.ZombiesMaxPlayers;

    public override string GoalText(Game game) => $"{Name}: {Rules()}";

    // ---- Lifecycle: into the Zombies subsystems ---------------------------------------------------------------

    /// <summary>A fresh run, or a Zombies map's doors opened for another mode (runs for every match, whatever the mode).</summary>
    public override void OnReset(Game game)
    {
        if (World.Map.Zombies != null || game.Mode == GameMode.Zombies)
            game.ResetZombiesRun();
    }

    /// <summary>Client: doors shut, boards up, no zombies (the server resets its run in <see cref="OnReset"/>).</summary>
    public override void OnClientMatchStart(Game game) => game.ResetZombiesWorld();

    /// <summary>Start gear and the start room (ZombiesMode.cs).</summary>
    public override void OnSpawn(Game game, Player p) => game.ZombiesOnSpawn(p);

    /// <summary>Rounds, zombies, buyables, downs; the run ends when everyone is down (no time limit).</summary>
    public override void ServerTickPlaying(Game game, double now) => game.ServerTickZombies(now);

    /// <summary>Co-op bots fight zombies (ZombieBots.cs).</summary>
    public override bool BotTick(Game game, Player bot, BotBrain brain, double now)
    {
        game.ZombieBotTick(bot, brain, now);
        return true;
    }

    /// <summary>The Zombies messages (ZombiesNet.cs).</summary>
    public override bool HandlePacket(Game game, byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.ZUse:
            case (byte)Msg.ZEnts:
            case (byte)Msg.ZWorld:
            case (byte)Msg.ZEvent:
            case (byte)Msg.ZRun:
            case (byte)Msg.ZBuild:
            case (byte)Msg.ZBench:
            case (byte)Msg.ZPickup:
            case (byte)Msg.ZSlotWin:
            case (byte)Msg.ZCasino:
            case (byte)Msg.ZCasinoAct:
                game.OnZombiesPacket(id, ref r, sender, now);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Ready-up between rounds: who's ready on the scoreboard (bots always are; the dead don't count).</summary>
    public override string ScoreboardTag(Game game, Player p)
    {
        if (!game.ZWaitingReady || game.ZPhase != ZRoundPhase.Break || !p.Alive)
            return "";
        if (game.ZPlayers.TryGetValue(p.Id, out var zp) && zp.ReadyVoter)
            return zp.RoundReady ? " [READY]" : " [not ready]";
        return game.ZPlayers.ContainsKey(p.Id) ? " [READY]" : ""; // a bot
    }

    public override string? SelfTestVerify(Game game) => game.ZPlayers.Count == 0 ? "no run started (nobody has a Zombies run)" : null;
}
