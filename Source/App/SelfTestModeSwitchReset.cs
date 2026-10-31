using System.Diagnostics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static void CheckModeSwitchReset()
    {
        var yard = Maps.Find(ConstructYardMap);
        var zmap = Maps.All.FirstOrDefault(m => m.Zombies != null && m.Id != yard?.Id);
        var tdm = Maps.Find("Proto Box") ?? Maps.All.First();
        if (yard?.Sandbox == null || zmap == null)
        {
            Fail("mode switch reset: the Construct Yard or a Zombies map is missing");
            return;
        }
        var infinite = yard.Sandbox.InfiniteParts;
        yard.Sandbox.InfiniteParts = true;
        try
        {
            MsrRun(yard, tdm, zmap);
        }
        finally
        {
            yard.Sandbox.InfiniteParts = infinite;
            World.SetMap(Maps.Get(0));
        }
    }

    /// <summary>What a player still holds of a mode (server's view of one player, or a client's view of itself): empty when clean.</summary>
    private static List<string> MsrLeaks(Game g, byte id, bool keepsLoadout)
    {
        var l = new List<string>();
        if (!g.Players.TryGetValue(id, out var p))
            return ["no player"];
        ref var h = ref g.SandboxHands.Hand(id);
        if (h.Tool != ConstructTool.None) l.Add("tool");
        if (h.Held != ConstructParts.None) l.Add("held part");
        if (h.Edit != EditKind.None) l.Add("edit");
        if (h.GrabBody != 0) l.Add("grab");
        if (h.Seated) l.Add("seat");
        if (g.SandboxHands.Pack(id).Items != 0) l.Add("backpack");
        if (g.SandboxInv.Of(id).Scrap != 0) l.Add("scrap");
        if (p.BuiltPrimary != null || p.BuiltSecondary != null || p.BuiltExtra[0] != null || p.BuiltExtra[1] != null) l.Add("built gun");
        if (p.MeleeBuild != null) l.Add("melee build");
        if (p.GrenadeBuild != null) l.Add("grenade build");
        if (p.ExtraSlots) l.Add("extra slots");
        var z = g.ZP(id);
        if (z.Points != 0) l.Add($"zombies points {z.Points}");
        if (z.Perks != 0) l.Add("zombies perks");
        if (z.Downed) l.Add("downed");
        if (z.Slots.Any(s => s != null)) l.Add("zombies slot builds");
        if (p.Streak.Count != 0 || p.Streak.Ready != 0 || p.Streak.Armor != 0) l.Add("streaks");
        if (p.FrenzyLevel != 0) l.Add("frenzy");
        if (keepsLoadout && (p.Primary != p.MpPrimary || p.Secondary != p.MpSecondary)) l.Add("loadout not restored");
        return l;
    }

    /// <summary>Dirty every Sandbox and Zombies thing a player can hold, on the host (the client hears it).</summary>
    private static void MsrDirty(Game server, Player sp, bool zombies)
    {
        if (!zombies)
        {
            ref var h = ref server.SandboxHands.Hand(sp.Id);
            h.Tool = ConstructTool.Wrench;
            h.Held = ConstructParts.Need("Engine").Id;
            server.SandboxHands.Stow(sp.Id);
            h.Held = ConstructParts.Need("Lamp").Id;
            server.SandboxHands.Stow(sp.Id);
            h.Held = ConstructParts.Need("Engine").Id;
            h.GrabBody = 1;
            server.SandboxInv.Of(sp.Id).Scrap = 40;
            var gun = StandardBuilds.All.First(b => b.Build().Kind == BuildKind.Gun).Build();
            server.SandboxBench(sp, Game.BenchSpawn, 0, gun.ToText());
            if (StandardBuilds.All.FirstOrDefault(b => b.Build().Kind == BuildKind.Melee) is { } m)
                server.SandboxBench(sp, Game.BenchSpawn, 0, m.Build().ToText());
        }
        else
        {
            var z = server.ZP(sp);
            z.Points = 7777;
            z.Perks = 3;
        }
        sp.Streak.Count = 3;
        sp.Streak.Ready = 1;
        sp.FrenzyLevel = 3;
    }

    private static void MsrRun(Map yard, Map tdm, Map zmap)
    {
        const ushort port = 26371;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id)
        {
            MapVoteEnabled = false,
            LobbyEnabled = true,
        };
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.2, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sa))
        {
            Fail("mode switch reset: the client didn't join");
            return;
        }
        var host = server.Players[server.LocalId];
        var problems = new List<string>();

        void Start(Map map, GameMode mode, bool dirtyFirst, bool zombiesDirty, string label)
        {
            if (!server.InLobby)
                server.ReturnToLobbyNow(now);
            Pump(server, [a], 0.8, ref now, clock);
            if (World.Map != map)
                server.LobbySetMap(map, false);
            server.LobbySetMode(mode);
            Pump(server, [a], 0.5, ref now, clock);
            server.LobbyStart(true, now);
            PumpFast(server, [a], Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.8, ref now);
            foreach (var (who, g, id) in new[] { ("host", server, host.Id), ("client (host's view)", server, sa.Id), ("client (own view)", a, a.LocalId), ("host (client's view)", a, host.Id) })
            {
                var leaks = MsrLeaks(g, id, keepsLoadout: true);
                if (leaks.Count > 0)
                    problems.Add($"{label}: {who}: {string.Join(", ", leaks)}");
            }
            if (dirtyFirst)
            {
                MsrDirty(server, host, zombiesDirty);
                MsrDirty(server, sa, zombiesDirty);
                Pump(server, [a], 0.6, ref now, clock);
            }
        }

        Start(yard, GameMode.Sandbox, true, false, "sandbox start");
        var dirt = MsrLeaks(server, sa.Id, false);
        Line($"mode switch reset: dirtied in Sandbox: {string.Join(", ", dirt)}");
        Start(tdm, GameMode.TeamDeathmatch, false, false, "Sandbox -> TDM (other map)");
        Start(zmap, GameMode.Zombies, true, true, "TDM -> Zombies");
        Start(yard, GameMode.Sandbox, true, false, "Zombies -> Sandbox");
        Start(yard, GameMode.Zombies, false, false, "Sandbox -> Zombies (other map)");
        Start(yard, GameMode.Sandbox, false, false, "Zombies -> Sandbox (same map, after a Sandbox run)");
        Line($"mode switch reset: {(problems.Count == 0 ? "clean" : string.Join(" | ", problems))}");
        foreach (var p in problems)
            Fail("mode switch reset: " + p);
    }
}
