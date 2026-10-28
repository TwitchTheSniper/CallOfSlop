using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies grenades": the board bug "grenades do nothing in Zombies". ZombiesOnSpawn took every
/// grenade away on the server while the client kept its loadout count, so the key played the throw sound, the HUD
/// counted down and the server dropped the throw ("not in their loadout"). Now: the rules file's grenade and count
/// formula (start + floor((round - 1) / roundsPerExtra), topped up every round: 1, 2, 4, 5 at rounds 1, 4, 10 and 13);
/// the host throws a real grenade (the key, the server's projectile and fuse) that kills a seeded cluster of zombies
/// with the kill credit and points, staggers a bystander (the horde's blast reaction) and opens no Zombies door;
/// loopback: a client's grenade does the same, the client's HUD count follows the server's, and the round's refill
/// reaches it.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesGrenades()
    {
        GrenadeRules();
        var now = 0.0;
        GrenadeHost(ref now);
        GrenadeLoopback();
    }

    /// <summary>The formula, the rules file's numbers and a broken grenades section refused.</summary>
    private static void GrenadeRules()
    {
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        int[] rounds = [1, 2, 3, 4, 6, 7, 9, 10, 12, 13, 30];
        int[] want = [1, 1, 1, 2, 2, 3, 3, 4, 4, 5, 10];
        var got = rounds.Select(rules.GrenadesForRound).ToArray();
        Line($"zombies grenades: {rules.GrenadeKind}, start {rules.GrenadeStart}, +1 every {rules.GrenadeRoundsPerExtra} rounds: "
             + string.Join(", ", rounds.Select((r, i) => $"r{r} {got[i]}")));
        if (rules.GrenadeKind != ThrowableKind.Frag || !got.SequenceEqual(want))
            Fail($"zombies grenades: the rules should give Frags as 1 + floor((round - 1) / 3) ({string.Join(" ", want)}), not {rules.GrenadeKind} {string.Join(" ", got)}");
        var flat = ZombiesRules.Load("""{"format": "call-of-slop-zombies/1", "grenades": {"kind": "impact", "start": 2, "roundsPerExtra": 0}}""", "flat rules");
        if (flat.GrenadeKind != ThrowableKind.Impact || flat.GrenadesForRound(1) != 2 || flat.GrenadesForRound(40) != 2)
            Fail("zombies grenades: a rules file's kind, start and roundsPerExtra 0 (no growth) don't apply");
        foreach (var bad in new[] { """{"kind": "Smoke"}""", """{"start": -1}""", """{"cap": 4}""" })
            try
            {
                ZombiesRules.Load($$"""{"format": "call-of-slop-zombies/1", "grenades": {{bad}}}""", "bad rules");
                Fail($"zombies grenades: a broken grenades section loaded: {bad}");
            }
            catch (WeaponFileException)
            {
            }
    }

    /// <summary>
    /// Four zombies held still around a spot a few metres from <paramref name="from"/> in its area, and a bystander
    /// (chasing) beyond the kill radius but inside the horde's scatter radius when there is room. Returns the spot.
    /// </summary>
    private static (Vector3 spot, List<Zombie> cluster, Zombie? bystander) GrenadeTargets(Game g, Map map, ZombiesMap zm, Vector3 from, float radius, double now)
    {
        var area = zm.Areas.FindIndex(a => a.Contains(from));
        var a = zm.Areas[Math.Max(0, area)];
        bool Free(Vector3 p) => a.Contains(p) && !World.Blocked(p + new Vector3(0, 0.05f, 0)) && !World.Blocked(p + new Vector3(0, 1.1f, 0));
        bool Clear(Vector3 x, Vector3 y)
        {
            var d = y - x;
            var len = d.Length();
            return len < 0.01f || World.Query.Raycast(x, d / len, len, QueryFilter.Shots) >= len - 0.05f;
        }
        Vector3[] ring = [new(0.8f, 0, 0.8f), new(-0.8f, 0, 0.8f), new(0.8f, 0, -0.8f), new(-0.8f, 0, -0.8f)];
        var spot = from;
        for (var k = 0; k < 32; k++)
        {
            var dir = new Vector3(MathF.Sin(k * MathF.Tau / 32), 0, MathF.Cos(k * MathF.Tau / 32));
            var p = from + dir * 3.5f;
            p.Y = map.GroundHeight(p.X, p.Z);
            if (Free(p) && ring.All(o => Free(p + o) && Clear(p + new Vector3(0, 0.45f, 0), p + o + new Vector3(0, 1.1f, 0))))
            {
                spot = p;
                break;
            }
        }
        var cluster = new List<Zombie>();
        foreach (var o in ring)
        {
            var z = ZPlace(g, zm, spot + o, Math.Max(0, area), ZSpeed.Walk, now);
            z.StunnedUntil = now + 1000; // held still (no walking out of the blast, no swings)
            cluster.Add(z);
        }
        Zombie? bystander = null;
        for (var r = radius + 0.8f; r < radius * 1.4f && bystander == null; r += 0.5f)
            for (var k = 0; k < 24 && bystander == null; k++)
            {
                var p = spot + new Vector3(MathF.Sin(k * MathF.Tau / 24) * r, 0, MathF.Cos(k * MathF.Tau / 24) * r);
                p.Y = map.GroundHeight(p.X, p.Z);
                var ai = zm.Areas.FindIndex(x => x.Contains(p));
                if (ai >= 0 && !World.Blocked(p + new Vector3(0, 0.05f, 0)) && !World.Blocked(p + new Vector3(0, 1.1f, 0)))
                    bystander = ZPlace(g, zm, p, ai, ZSpeed.Walk, now);
            }
        return (spot, cluster, bystander);
    }

    /// <summary>The newest projectile of <paramref name="owner"/>'s on <paramref name="server"/>, moved to <paramref name="at"/> and stopped there.</summary>
    private static Projectile? GrenadeTo(Game server, byte owner, HashSet<ushort> before, Vector3 at)
    {
        var proj = server.Projectiles.Values.Where(pr => pr.Owner == owner && !before.Contains(pr.Id)).OrderByDescending(pr => pr.Id).FirstOrDefault();
        if (proj == null)
            return null;
        proj.Pos = at;
        proj.Vel = Vector3.Zero;
        return proj;
    }

    private static void GrenadeHost(ref double now)
    {
        var map = ZTestMap!;
        var zm = map.Zombies!;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        Run(g, ref now, 0.1);
        var spawnCount = g.ZRules.GrenadesForRound(g.ZRound);
        Line($"zombies grenades (host): after the spawn the server has {me.Grenade} x{me.GrenadesLeft}, the HUD {g.LocalGrenade} x{g.GrenadesLeft}, equipment {g.LocalEquipment}; round {g.ZRound}");
        if (me.Grenade != ThrowableKind.Frag || me.GrenadesLeft != spawnCount || g.LocalGrenade != ThrowableKind.Frag || g.GrenadesLeft != spawnCount)
            Fail($"zombies grenades: a Zombies spawn should carry {spawnCount} Frag on the server and the HUD (server {me.Grenade} x{me.GrenadesLeft}, HUD {g.LocalGrenade} x{g.GrenadesLeft})");
        if (g.LocalEquipment != ThrowableKind.None || me.Equipment != ThrowableKind.None)
            Fail($"zombies grenades: Zombies has no equipment, but the HUD shows {g.LocalEquipment} (server {me.Equipment})");

        // The key, as Program presses it: a projectile on the server, one spent on both counts.
        var radius = Throwables.Get(ThrowableKind.Frag).Radius;
        var (spot, cluster, bystander) = GrenadeTargets(g, map, zm, me.Position, radius, now);
        var zp = g.ZP(me);
        int kills0 = zp.Kills, points0 = zp.Points, playerKills0 = me.Kills;
        var doors0 = Enumerable.Range(0, zm.Doors.Count).Count(map.IsDoorOpen);
        var before = g.Projectiles.Keys.ToHashSet();
        g.LookYaw = me.Yaw;
        g.LookPitch = 0.5f;
        var press = PressThrow(g, false, ThrowableKind.Frag, now);
        if (press != null)
            Fail($"zombies grenades: the host's grenade key: {press}");
        var proj = GrenadeTo(g, me.Id, before, spot + new Vector3(0, 0.3f, 0));
        var fuse = Throwables.Get(ThrowableKind.Frag).Fuse;
        var t0 = now;
        Run(g, ref now, fuse + 0.3);
        var dead = cluster.Count(z => z.State == ZState.Dead);
        var staggered = bystander != null && bystander.State != ZState.Dead && bystander.StunnedUntil > t0;
        var doors1 = Enumerable.Range(0, zm.Doors.Count).Count(map.IsDoorOpen);
        Line($"zombies grenades (host): thrown {press ?? "ok"}, gone off {proj != null && !g.Projectiles.ContainsKey(proj.Id)}; {dead} of {cluster.Count} zombies killed (health {cluster[0].MaxHealth}), "
             + $"kills {kills0} -> {zp.Kills} (player {playerKills0} -> {me.Kills}), points {points0} -> {zp.Points}; bystander {(bystander == null ? "no room" : staggered ? "staggered" : "not staggered")}; "
             + $"doors open {doors0} -> {doors1}; left: server {me.GrenadesLeft}, HUD {g.GrenadesLeft}");
        if (proj == null || g.Projectiles.ContainsKey(proj.Id))
            Fail("zombies grenades: the host's grenade didn't fly and go off");
        if (dead != cluster.Count)
            Fail($"zombies grenades: a Frag at their feet killed {dead} of {cluster.Count} round-{Math.Max(1, g.ZRound)} zombies");
        if (zp.Kills - kills0 != dead || me.Kills - playerKills0 != dead || zp.Points - points0 < dead * g.ZRules.KillPoints)
            Fail($"zombies grenades: the thrower should get the {dead} kills and at least {dead * g.ZRules.KillPoints} points (kills +{zp.Kills - kills0}, player kills +{me.Kills - playerKills0}, points +{zp.Points - points0})");
        if (bystander != null && !staggered)
            Fail("zombies grenades: a zombie in the horde's scatter radius wasn't staggered by the blast (HordeExplosion)");
        if (doors1 != doors0)
            Fail("zombies grenades: a grenade opened a Zombies door (they're bought)");
        if (me.GrenadesLeft != spawnCount - 1 || g.GrenadesLeft != spawnCount - 1)
            Fail($"zombies grenades: after one throw the server has {me.GrenadesLeft} and the HUD {g.GrenadesLeft}, not {spawnCount - 1}");

        // None left: the key is refused and nothing is spent.
        Run(g, ref now, 1.0);
        var count = g.Projectiles.Count;
        g.TryThrow(false, now);
        if (g.Projectiles.Count != count || g.GrenadesLeft != 0 || me.GrenadesLeft != 0)
            Fail($"zombies grenades: with none left a throw went through (projectiles {count} -> {g.Projectiles.Count}, left {g.GrenadesLeft}/{me.GrenadesLeft})");

        // The refill: topped up at every round's start to the formula's count; never taken down.
        var seen = new List<string>();
        foreach (var (round, want) in new[] { (1, 1), (4, 2), (10, 4), (13, 5) })
        {
            me.GrenadesLeft = 0;
            g.SelfTestBeginRound(round, now);
            Run(g, ref now, 0.05);
            seen.Add($"r{round} {me.GrenadesLeft}/{g.GrenadesLeft}");
            if (me.GrenadesLeft != want || g.GrenadesLeft != want)
                Fail($"zombies grenades: round {round} should top up to {want} (server {me.GrenadesLeft}, HUD {g.GrenadesLeft})");
        }
        me.GrenadesLeft = 9;
        g.SelfTestBeginRound(14, now);
        var kept = me.GrenadesLeft;
        g.SelfTestRespawn(me, now); // a spawn in round 14 (back after bleeding out): the round's count
        Run(g, ref now, 0.05);
        Line($"zombies grenades (host): refills (server/HUD) {string.Join(", ", seen)}; 9 held at round 14 -> {kept}; a spawn in round 14 -> {me.GrenadesLeft}/{g.GrenadesLeft}");
        if (kept != 9)
            Fail($"zombies grenades: the round refill took grenades away (9 -> {kept})");
        if (me.GrenadesLeft != g.ZRules.GrenadesForRound(14) || g.GrenadesLeft != me.GrenadesLeft)
            Fail($"zombies grenades: a spawn in round 14 should carry {g.ZRules.GrenadesForRound(14)} (server {me.GrenadesLeft}, HUD {g.GrenadesLeft})");
    }

    /// <summary>A client's grenade: thrown by its key, killing on the server with the client's credit; its HUD follows the server's count and the refill.</summary>
    private static void GrenadeLoopback()
    {
        var map = ZTestMap!;
        var zm = map.Zombies!;
        const ushort port = 26297;
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            LobbyEnabled = true,
            WarmupEnabled = true,
        };
        server.ZRules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        server.ZRules.FirstRoundDelay = 9999;
        server.ZSeed = 11;
        server.Mode = GameMode.Zombies;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            grenade: ThrowableKind.Impact, equipment: ThrowableKind.Flash); // a multiplayer loadout: Zombies ignores it
        PumpFast(server, [a], 1.0, ref now);
        server.LobbyStart(true, now);
        PumpFast(server, [a], Game.LobbyCountdownSeconds + 0.5, ref now);
        server.ForceStart();
        PumpFast(server, [a], Game.CountdownSeconds + 0.7, ref now);
        if (server.Phase != MatchPhase.Playing || a.Phase != MatchPhase.Playing || !server.Players.TryGetValue(a.LocalId, out var ap))
        {
            Fail($"zombies grenades (loopback): the match didn't start (server {server.Phase}, client {a.Phase})");
            return;
        }
        if (!ap.Alive)
        {
            ap.WantsRespawn = true;
            ap.RespawnAt = now;
            PumpFast(server, [a], 0.5, ref now);
        }
        ap.ImmuneUntil = 1e9;
        ap.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        PumpFast(server, [a], 0.4, ref now);
        var want = server.ZRules.GrenadesForRound(server.ZRound);
        Line($"zombies grenades (loopback): the client's player on the server {ap.Grenade} x{ap.GrenadesLeft}; the client's HUD {a.LocalGrenade} x{a.GrenadesLeft}, equipment {a.LocalEquipment}");
        if (ap.Grenade != ThrowableKind.Frag || ap.GrenadesLeft != want || a.LocalGrenade != ThrowableKind.Frag || a.GrenadesLeft != want || a.LocalEquipment != ThrowableKind.None)
            Fail($"zombies grenades (loopback): the client should hold {want} Frag and no equipment like the server (server {ap.Grenade} x{ap.GrenadesLeft}, client {a.LocalGrenade} x{a.GrenadesLeft}, {a.LocalEquipment})");

        var radius = Throwables.Get(ThrowableKind.Frag).Radius;
        var (spot, cluster, _) = GrenadeTargets(server, map, zm, ap.Position, radius, now);
        var zp = server.ZP(ap);
        int kills0 = zp.Kills, points0 = zp.Points;
        var before = server.Projectiles.Keys.ToHashSet();
        a.LookYaw = a.Local!.Yaw;
        a.LookPitch = 0.5f;
        a.TryThrow(false, now);
        Projectile? proj = null;
        for (var k = 0; k < 20 && proj == null; k++)
        {
            PumpFast(server, [a], 0.05, ref now);
            proj = GrenadeTo(server, ap.Id, before, spot + new Vector3(0, 0.3f, 0));
        }
        PumpFast(server, [a], Throwables.Get(ThrowableKind.Frag).Fuse + 1.5, ref now);
        var dead = cluster.Count(z => z.State == ZState.Dead);
        var clientZp = a.ZP(a.LocalId);
        Line($"zombies grenades (loopback): the server got the throw {proj != null}; {dead} of {cluster.Count} killed, the client's kills {kills0} -> {zp.Kills} (its copy {clientZp.Kills}), "
             + $"points {points0} -> {zp.Points}; left: server {ap.GrenadesLeft}, client HUD {a.GrenadesLeft}");
        if (proj == null)
            Fail("zombies grenades (loopback): the client's grenade key reached no projectile on the server (the original bug)");
        if (dead != cluster.Count || zp.Kills - kills0 != dead || zp.Points - points0 < dead * server.ZRules.KillPoints)
            Fail($"zombies grenades (loopback): the client's grenade should kill all {cluster.Count} with its credit (killed {dead}, kills +{zp.Kills - kills0}, points +{zp.Points - points0})");
        if (ap.GrenadesLeft != want - 1 || a.GrenadesLeft != want - 1)
            Fail($"zombies grenades (loopback): after one throw the server has {ap.GrenadesLeft} and the client's HUD {a.GrenadesLeft}, not {want - 1}");

        server.SelfTestBeginRound(10, now);
        PumpFast(server, [a], 0.5, ref now);
        Line($"zombies grenades (loopback): round 10 refill: server {ap.GrenadesLeft}, client HUD {a.LocalGrenade} x{a.GrenadesLeft}");
        if (ap.GrenadesLeft != 4 || a.GrenadesLeft != 4 || a.LocalGrenade != ThrowableKind.Frag)
            Fail($"zombies grenades (loopback): the round 10 refill should reach the client as 4 Frags (server {ap.GrenadesLeft}, client {a.LocalGrenade} x{a.GrenadesLeft})");
    }
}
