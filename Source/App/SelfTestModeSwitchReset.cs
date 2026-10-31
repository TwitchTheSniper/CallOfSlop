using System.Diagnostics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static void CheckModeSwitchReset()
    {
        MsrReflection();
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
    private static List<string> MsrLeaks(Game g, byte id, bool inZombies)
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
        if (!inZombies && (p.BuiltPrimary != null || p.BuiltSecondary != null || p.BuiltExtra[0] != null || p.BuiltExtra[1] != null)) l.Add("built gun");
        if (!inZombies && p.MeleeBuild != null) l.Add("melee build");
        if (!inZombies && p.GrenadeBuild != null) l.Add("grenade build");
        if (!inZombies && p.ExtraSlots) l.Add("extra slots");
        var z = g.ZP(id);
        if (z.Points != ZombiesRules.Current.StartPoints) l.Add($"zombies points {z.Points}");
        if (z.Perks != 0) l.Add("zombies perks");
        if (z.Downed) l.Add("downed");
        if (!inZombies && z.Slots.Any(s => s != null)) l.Add("zombies slot builds");
        if (p.Streak.Count != 0 || p.Streak.Ready != 0 || p.Streak.Armor != 0) l.Add("streaks");
        if (p.FrenzyLevel != 0) l.Add("frenzy");
        if (!inZombies && (p.Primary != p.MpPrimary || p.Secondary != p.MpSecondary)) l.Add("loadout not restored");
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
                var leaks = MsrLeaks(g, id, mode == GameMode.Zombies);
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

    /// <summary>Player fields that are not per-match: kept (or set again by the spawn, the snapshot or the net) on purpose. Anything not here and not marked [MatchState] fails.</summary>
    private static readonly HashSet<string> MsrPersistent = new()
    {
        // identity and what the person keeps
        "Id", "Name", "Bot", "Prestige", "Level", "Hat", "Look", "SteamId", "TeamChoice", "TeamSwitchAt", "Ready",
        // the loadout they chose (restored over a mode's gear by ResetForMatch) and what hangs off it
        "Primary", "Secondary", "Weapon", "PrimaryKit", "SecondaryKit", "Perks", "WantPrimary", "WantSecondary", "WantPrimaryKit", "WantSecondaryKit",
        "WantPerks", "MpPrimary", "MpSecondary", "MpPrimaryKit", "MpSecondaryKit", "Grenade", "Equipment", "WantGrenade", "WantEquipment",
        "FrenzyBuffsOn",
        // per life: the spawn sets them again
        "Sim", "Yaw", "Pitch", "Health", "Blood", "Alive", "Kills", "Deaths", "Team", "ShieldCharges", "Regenerating", "GrenadesLeft", "EquipmentLeft", "NextThrow",
        "Sprinting", "SprintRecoverTicks", "LastStandUsed", "AfterburnerReadyTick", "ShieldUntil", "LastHurtTime", "RegenCarry", "ImmuneUntil", "NextMelee",
        "WantsRespawn", "SpawnTick", "RespawnAt", "LastShotTime", "ShotClock", "GroupShotClock", "LastCombatTime", "ShieldFlashTime",
        // network, input and history (server) and drawing (client)
        "InputQueue", "LastProcessedSeq", "HighestQueuedSeq", "LastInput", "StarvedTicks", "Buffering", "History", "Snaps", "PrevPosition", "RenderPosition",
        "RenderYaw", "RenderPitch", "RenderVelocity", "RenderGrounded", "RenderMantling", "RenderStance", "RenderEyeHeight", "RenderSeat", "RenderSeatWas",
        "RenderSeatRot", "RenderLookYaw", "RenderWheelR", "RenderWheelL", "RenderWheelRot", "SnapSeatBody", "SnapSeatPart",
    };

    /// <summary>Every [MatchState] field of Player is back to a new player's value after ResetForMatch, and every other field is listed as persistent: a new field can't be forgotten.</summary>
    private static void MsrReflection()
    {
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var fields = typeof(Player).GetFields(all).Where(f => !f.Name.Contains('<')).ToList();
        var unclassified = fields.Where(f => f.GetCustomAttributes(typeof(MatchStateAttribute), false).Length == 0 && !MsrPersistent.Contains(f.Name)).Select(f => f.Name).ToList();
        var stale = MsrPersistent.Where(n => fields.All(f => f.Name != n)).ToList();
        if (unclassified.Count > 0)
            Fail($"mode switch reset: Player fields neither marked [MatchState] nor listed in SelfTest.MsrPersistent: {string.Join(", ", unclassified)}");
        if (stale.Count > 0)
            Fail($"mode switch reset: SelfTest.MsrPersistent names fields Player no longer has: {string.Join(", ", stale)}");
        var marked = fields.Where(f => f.GetCustomAttributes(typeof(MatchStateAttribute), false).Length > 0).ToList();
        var dirty = new Player();
        var fresh = new Player();
        foreach (var f in marked)
            if (!MsrDirtyField(f, dirty))
                Fail($"mode switch reset: the test can't dirty Player.{f.Name} ({f.FieldType.Name}); teach MsrDirtyField about it");
        dirty.Streak.Count = 4;
        dirty.Streak.Ready = 2;
        dirty.Streak.Armor = 50;
        dirty.Streak.Progress[1] = 3;
        // a person's loadout choice is the one thing a reset restores: make the current guns differ from it
        dirty.Primary = (byte)(dirty.MpPrimary + 1);
        dirty.ResetForMatch(restoreLoadout: true);
        var bad = new List<string>();
        foreach (var f in marked)
        {
            var a = f.GetValue(dirty);
            var b = f.GetValue(fresh);
            var same = a is Array xa && b is Array xb ? xa.Cast<object?>().SequenceEqual(xb.Cast<object?>()) : f.FieldType == typeof(StreakState) ? MsrStreakSame((StreakState)a!, (StreakState)b!) : Equals(a, b);
            if (!same)
                bad.Add(f.Name);
        }
        if (dirty.Primary != dirty.MpPrimary)
            bad.Add("(loadout)");
        Line($"mode switch reset: reflection: {fields.Count} Player fields, {marked.Count} marked per-match, {MsrPersistent.Count} persistent, not reset after a dirtying: {(bad.Count == 0 ? "none" : string.Join(", ", bad))}");
        if (bad.Count > 0)
            Fail($"mode switch reset: Player.ResetForMatch leaves these per-match fields dirty: {string.Join(", ", bad)}");
    }

    private static bool MsrStreakSame(StreakState a, StreakState b) =>
        a.Count == b.Count && a.Ready == b.Ready && a.Armor == b.Armor && a.ArmorHeavy == b.ArmorHeavy && a.Progress.SequenceEqual(b.Progress);

    private static bool MsrDirtyField(System.Reflection.FieldInfo f, Player p)
    {
        var t = f.FieldType;
        object? v;
        if (t == typeof(StreakState))
            return true; // (dirtied by hand above)
        if (t.IsArray)
        {
            var arr = (Array)f.GetValue(p)!;
            for (var i = 0; i < arr.Length; i++)
                arr.SetValue(MsrOther(t.GetElementType()!, arr.GetValue(i)), i);
            return true;
        }
        v = MsrOther(t, f.GetValue(p));
        if (v == null && !(Nullable.GetUnderlyingType(t) != null))
            return false;
        f.SetValue(p, v);
        return true;
    }

    /// <summary>A value of <paramref name="t"/> that differs from <paramref name="cur"/>.</summary>
    private static object? MsrOther(Type t, object? cur)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(bool)) return !(bool)(cur ?? false);
        if (u.IsEnum) return Enum.ToObject(u, Convert.ToInt64(cur ?? 0) == 1 ? 2 : 1);
        if (u == typeof(System.Numerics.Vector3)) return new System.Numerics.Vector3(1, 2, 3);
        if (u.IsPrimitive) return Convert.ChangeType(Convert.ToDouble(cur ?? 0) == 1 ? 2 : 1, u);
        if (u.IsClass) return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(u);
        return null;
    }
}
