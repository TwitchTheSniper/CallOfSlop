using System.Diagnostics;
using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Throw input": the grenade and equipment keys end to end, the way the window uses them
/// (Program.UpdateLocalPlayer calls <see cref="Game.TryThrow"/> on the bind's press). Offline: every lethal and
/// tactical item thrown, placed or used in Playing (a projectile on the server, or the device, or the stim's heal),
/// refused with nothing spent in the warmup, the countdown, the results screen and the lobby, then thrown again right
/// after the lobby starts the match; bots' grenades. Loopback: a client's throws reach the server and come back to it,
/// the host's reach the client, and a grenade-only loadout change in the countdown throws the new kind.
/// Regression for "pressing the grenade key spawns nothing" (ServerThrow sent every non-tactical throwable to the
/// tactical-use path, which has no case for them).
/// </summary>
public static partial class SelfTest
{
    private static void CheckThrowInput()
    {
        CheckThrowKeys();
        CheckThrowOffline();
        CheckThrowBots();
        CheckThrowLoopback();
    }

    /// <summary>The binds the throw keys read: bound, and not shared with another action (today's defaults and an old settings.txt).</summary>
    private static void CheckThrowKeys()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-throwkeys-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var old = (int[])Keybinds.Defaults.Clone(); // binds version 1: Interact E, equipment F, no binds_version line
            old[(int)GameAction.Interact] = Keybinds.Key(GlfwKey.E);
            old[(int)GameAction.Equipment] = Keybinds.Key(GlfwKey.F);
            File.WriteAllLines(tmp, Enumerable.Range(0, old.Length).Select(i => $"bind_{(GameAction)i}={old[i]}"));
            foreach (var (name, binds) in new[] { ("defaults", Keybinds.Defaults), ("old settings.txt", Settings.Load(tmp).Binds) })
                foreach (var a in new[] { GameAction.Grenade, GameAction.Equipment })
                {
                    var code = binds[(int)a];
                    var shared = Enumerable.Range(0, binds.Length).Where(i => i != (int)a && binds[i] == code).Select(i => (GameAction)i).ToArray();
                    if (code == Keybinds.Unbound || shared.Length > 0)
                        Fail($"throw keys ({name}): {a} is {(code == Keybinds.Unbound ? "unbound" : $"shared with {string.Join(", ", shared)}")}");
                }
            var b = Settings.Load(tmp).Binds;
            Line($"throw keys: grenade {(GlfwKey)Keybinds.Defaults[(int)GameAction.Grenade]}, equipment {(GlfwKey)Keybinds.Defaults[(int)GameAction.Equipment]}; "
                 + $"an old settings.txt loads grenade {(GlfwKey)b[(int)GameAction.Grenade]}, equipment {(GlfwKey)b[(int)GameAction.Equipment]}");
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* temp file */ }
        }
    }

    /// <summary>
    /// The local player presses the key (<see cref="Game.TryThrow"/>, as Program does). Returns null when the right
    /// thing appeared and both the HUD's and the server's count went down by one, or what went wrong.
    /// </summary>
    private static string? PressThrow(Game game, bool equipment, ThrowableKind expect, double now)
    {
        var me = game.Local!;
        var serverBefore = equipment ? me.EquipmentLeft : me.GrenadesLeft;
        var localBefore = equipment ? game.EquipmentLeft : game.GrenadesLeft;
        var projBefore = game.Projectiles.Values.Where(pr => pr.Owner == me.Id && pr.Kind == expect).Select(pr => pr.Id).ToHashSet();
        var devBefore = game.TacDevices.Count(d => d.Owner == me.Id && d.Kind == expect);
        if ((equipment ? game.LocalEquipment : game.LocalGrenade) != expect)
            return $"the HUD has {(equipment ? game.LocalEquipment : game.LocalGrenade)} in the slot, not {expect}";
        if (expect == ThrowableKind.Stim)
            me.Health = 40;
        game.TryThrow(equipment, now);
        var serverAfter = equipment ? me.EquipmentLeft : me.GrenadesLeft;
        var localAfter = equipment ? game.EquipmentLeft : game.GrenadesLeft;
        if (localAfter != localBefore - 1)
            return $"the key press was ignored ({localBefore} -> {localAfter} left on the HUD)";
        if (serverAfter != serverBefore - 1)
            return $"the server didn't spend one ({serverBefore} -> {serverAfter})";
        switch (TacticalGear.UseOf(expect))
        {
            case TacticalUse.Placed:
                return game.TacDevices.Count(d => d.Owner == me.Id && d.Kind == expect) > devBefore ? null : "no device was put down";
            case TacticalUse.Self:
                return me.Health > 40 ? null : "the stim didn't heal";
            default:
                return game.Projectiles.Values.Any(pr => pr.Owner == me.Id && pr.Kind == expect && !projBefore.Contains(pr.Id)) ? null : "no projectile was spawned";
        }
    }

    /// <summary>Both keys pressed in a phase that doesn't allow throwing: nothing spawns and nothing is spent.</summary>
    private static void ThrowRefused(Game game, string phase, double now)
    {
        var me = game.Local!;
        var before = (game.Projectiles.Count, game.TacDevices.Count, game.GrenadesLeft, game.EquipmentLeft, me.GrenadesLeft, me.EquipmentLeft);
        game.TryThrow(false, now);
        game.TryThrow(true, now);
        var after = (game.Projectiles.Count, game.TacDevices.Count, game.GrenadesLeft, game.EquipmentLeft, me.GrenadesLeft, me.EquipmentLeft);
        Line($"throw keys in the {phase} ({game.Phase}): refused {before == after}");
        if (before != after)
            Fail($"throw input: a throw went through in the {phase} ({before} -> {after})");
    }

    private static void CheckThrowOffline()
    {
        var small = FindMap("Proto Box", m => m.MaxPlayers >= 4)!;
        var now = 0.0;
        using var game = ModeGame(0, GameMode.FreeForAll, small.Id);
        game.WarmupEnabled = true;
        byte pri = Weapons.DefaultPrimary.Id, sec = Weapons.DefaultSecondary.Id;
        game.ChooseLoadout(pri, sec, ThrowableKind.Frag, ThrowableKind.Flash, 0, 0, now);
        Run(game, ref now, 0.3);
        var me = game.Local!;
        game.SelfTestRespawn(me, now); // a warmup wanderer
        if (game.Phase != MatchPhase.Warmup)
            Fail($"throw input: the offline game opened in {game.Phase}, not the warmup");
        ThrowRefused(game, "warmup", now);

        game.SetReady(true); // single player: ready starts the countdown
        Run(game, ref now, 0.3);
        if (game.Phase != MatchPhase.Countdown || !me.Alive)
            Fail($"throw input: ready didn't start the countdown with the player in the map ({game.Phase}, alive {me.Alive})");
        ThrowRefused(game, "countdown", now);
        Run(game, ref now, Game.CountdownSeconds + 0.2);
        if (game.Phase != MatchPhase.Playing)
            Fail($"throw input: the match is in {game.Phase} after the countdown");

        // Every throwable, from the slot it goes in, with the loadout applied by a spawn (the HUD refills from it).
        var results = new List<string>();
        var kinds = Throwables.All.Where(d => d.Kind != ThrowableKind.None && d.InLoadouts)
            .OrderBy(d => TacticalGear.UseOf(d.Kind) is TacticalUse.Placed or TacticalUse.Self ? 1 : 0).ThenBy(d => d.Kind).ToList();
        foreach (var def in kinds)
        {
            var eq = def.IsEquipment;
            game.ChooseLoadout(pri, sec, eq ? ThrowableKind.None : def.Kind, eq ? def.Kind : ThrowableKind.None, 0, 0, now);
            game.SelfTestRespawn(me, now);
            game.LookYaw = me.Yaw;
            game.LookPitch = 0.5f; // lob it up and away
            var why = PressThrow(game, eq, def.Kind, now);
            results.Add($"{def.Kind} {(why == null ? "ok" : "FAILED")}");
            if (why != null)
                Fail($"throw input (offline, Playing): {def.Name} from the {(eq ? "equipment" : "grenade")} key: {why}");
            Run(game, ref now, 0.85); // the throw cooldowns (0.8 s on the key, 0.7 s on the server)
        }
        Line($"offline, Playing: {string.Join(", ", results)}");

        // The results screen, then the lobby room (hosting), then straight after the lobby starts the match.
        game.LobbyEnabled = true;
        game.ReturnToLobby = true;
        game.ChooseLoadout(pri, sec, ThrowableKind.Frag, ThrowableKind.Flash, 0, 0, now);
        game.SelfTestRespawn(me, now);
        game.DebugEndMatch(now);
        Run(game, ref now, 0.2);
        if (game.Phase == MatchPhase.Ended)
        {
            game.SelfTestRespawn(me, now);
            ThrowRefused(game, "results screen", now);
        }
        else
        {
            Fail($"throw input: ending the match left it in {game.Phase}");
        }
        Run(game, ref now, Game.IntermissionSeconds + 0.3);
        if (game.Phase == MatchPhase.Lobby)
        {
            game.SelfTestRespawn(me, now); // (nobody is in the map in the lobby; forced in to test the phase rule itself)
            ThrowRefused(game, "lobby", now);
            me.Alive = false; // back out of the map
            game.LobbyStart(force: true, now);
            Run(game, ref now, Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.4);
            if (game.Phase != MatchPhase.Playing || !me.Alive)
            {
                Fail($"throw input: after the lobby start the match is in {game.Phase} (alive {me.Alive})");
            }
            else
            {
                game.LookYaw = me.Yaw;
                game.LookPitch = 0.5f;
                var frag = PressThrow(game, false, ThrowableKind.Frag, now);
                Run(game, ref now, 0.85);
                var flash = PressThrow(game, true, ThrowableKind.Flash, now);
                Line($"right after the lobby start: frag {frag ?? "ok"}, flashbang {flash ?? "ok"}");
                if (frag != null || flash != null)
                    Fail($"throw input: after the lobby start: frag {frag ?? "ok"}, flashbang {flash ?? "ok"}");
            }
        }
        else
        {
            Fail($"throw input: \"Return to lobby\" left the game in {game.Phase}, not the lobby");
        }
    }

    /// <summary>Bots still throw: a bot's grenade throw spawns the projectile.</summary>
    private static void CheckThrowBots()
    {
        var small = FindMap("Proto Box", m => m.MaxPlayers >= 4)!;
        var now = 0.0;
        using var game = ModeGame(1, GameMode.FreeForAll, small.Id);
        Run(game, ref now, Game.CountdownSeconds + 0.3);
        var bot = game.Players.Values.First(p => p.Bot != null);
        var thrown = new List<string>();
        foreach (var kind in new[] { ThrowableKind.Frag, ThrowableKind.Impact, ThrowableKind.Smoke })
        {
            game.SelfTestRespawn(bot, now);
            bot.Grenade = kind;
            bot.GrenadesLeft = Throwables.Get(kind).PerLife;
            var before = game.Projectiles.Values.Count(pr => pr.Owner == bot.Id && pr.Kind == kind);
            game.SelfTestBotThrow(bot, bot.Position + World.Forward(bot.Yaw, 0) * 12f, now);
            var ok = game.Projectiles.Values.Count(pr => pr.Owner == bot.Id && pr.Kind == kind) > before;
            thrown.Add($"{kind} {(ok ? "ok" : "FAILED")}");
            if (!ok)
                Fail($"throw input: a bot's {Throwables.Get(kind).Name} spawned nothing (phase {game.Phase})");
            Run(game, ref now, 0.8);
        }
        Line($"bot grenades: {string.Join(", ", thrown)}");
    }

    private static void CheckThrowLoopback()
    {
        var small = FindMap("Proto Box", m => m.MaxPlayers >= 4)!;
        const ushort port = 26233;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(small);
        byte pri = Weapons.DefaultPrimary.Id, sec = Weapons.DefaultSecondary.Id;
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", pri, sec, bots: 0, mapId: small.Id,
            grenade: ThrowableKind.Frag, equipment: ThrowableKind.Flash);
        server.MapVoteEnabled = false;
        server.WarmupEnabled = true;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", pri, sec, grenade: ThrowableKind.Frag, equipment: ThrowableKind.Flash);
        Game[] clients = [a];
        Pump(server, clients, 1.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.ContainsKey(a.LocalId))
        {
            Fail("throw input (loopback): the client didn't join");
            return;
        }
        server.ForceStart();
        Pump(server, clients, 0.6, ref now, clock);
        var ap = server.Players[a.LocalId];
        if (a.Phase != MatchPhase.Countdown || a.Local is not { Alive: true })
            Fail($"throw input (loopback): after the start the client is in {a.Phase}, alive {a.Local?.Alive}");

        // The countdown: refused; and a grenade-only loadout change applies to the keys at once.
        var serverProj = server.Projectiles.Count;
        ThrowRefused(a, "countdown (client)", now);
        a.ChooseLoadout(pri, sec, ThrowableKind.Impact, ThrowableKind.Stun, 0, 0, now);
        Pump(server, clients, 0.4, ref now, clock);
        Line($"loopback countdown: grenade-only change -> client keys {a.LocalGrenade}/{a.LocalEquipment}, server {ap.Grenade}/{ap.Equipment}; server projectiles {serverProj} -> {server.Projectiles.Count}");
        if (server.Projectiles.Count != serverProj)
            Fail("throw input (loopback): a client's throw in the countdown reached the server");
        if (a.LocalGrenade != ThrowableKind.Impact || a.LocalEquipment != ThrowableKind.Stun || ap.Grenade != ThrowableKind.Impact || ap.Equipment != ThrowableKind.Stun)
            Fail($"throw input (loopback): a loadout change in the countdown left the client on {a.LocalGrenade}/{a.LocalEquipment} and the server on {ap.Grenade}/{ap.Equipment}");

        var until = clock.Elapsed.TotalSeconds + Game.CountdownSeconds + 1;
        while (server.Phase != MatchPhase.Playing || a.Phase != MatchPhase.Playing)
        {
            if (clock.Elapsed.TotalSeconds > until)
                break;
            Pump(server, clients, 0.1, ref now, clock);
        }
        if (a.Phase != MatchPhase.Playing)
        {
            Fail($"throw input (loopback): the match didn't start (server {server.Phase}, client {a.Phase})");
            return;
        }

        // Each throw: seen on the server and back on the thrower's client (impact and stun go off on contact, so watch every frame).
        string Throw(Game thrower, bool equipment, ThrowableKind kind, byte owner)
        {
            thrower.LookYaw = thrower.Local!.Yaw;
            thrower.LookPitch = 0.6f;
            bool seenServer = false, seenClient = false;
            var before = server.Projectiles.Values.Where(pr => pr.Owner == owner && pr.Kind == kind).Select(pr => pr.Id).ToHashSet();
            var clientBefore = a.Projectiles.Values.Where(pr => pr.Owner == owner && pr.Kind == kind).Select(pr => pr.Id).ToHashSet();
            thrower.TryThrow(equipment, now);
            var end = clock.Elapsed.TotalSeconds + 1.0;
            while (clock.Elapsed.TotalSeconds < end && !(seenServer && seenClient))
            {
                Pump(server, clients, 0.005, ref now, clock);
                seenServer |= server.Projectiles.Values.Any(pr => pr.Owner == owner && pr.Kind == kind && !before.Contains(pr.Id));
                seenClient |= a.Projectiles.Values.Any(pr => pr.Owner == owner && pr.Kind == kind && !clientBefore.Contains(pr.Id));
            }
            Pump(server, clients, 0.85, ref now, clock); // the cooldowns
            return seenServer && seenClient ? "ok" : $"server {(seenServer ? "saw it" : "spawned nothing")}, client {(seenClient ? "saw it" : "saw nothing")}";
        }
        var impact = Throw(a, false, ThrowableKind.Impact, a.LocalId);
        var stun = Throw(a, true, ThrowableKind.Stun, a.LocalId);
        var hostFrag = Throw(server, false, ThrowableKind.Frag, server.LocalId);
        Line($"loopback, Playing: client impact {impact}, client stun {stun}, host frag -> client {hostFrag}");
        if (impact != "ok")
            Fail($"throw input (loopback): the client's impact grenade: {impact}");
        if (stun != "ok")
            Fail($"throw input (loopback): the client's stun grenade: {stun}");
        if (hostFrag != "ok")
            Fail($"throw input (loopback): the host's frag: {hostFrag}");
    }
}
