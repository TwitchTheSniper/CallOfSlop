using System.Diagnostics;

namespace CallOfSlop;

/// <summary>"Built gun after death": a workbench gun still fires after its owner dies and respawns, host and client. The respawn button sends the saved loadout, which isn't the built gun: left alone, the server took the shots of a gun the player no longer owned as nothing.</summary>
public static partial class SelfTest
{
    private static void CheckBuiltRespawn()
    {
        var yard = Maps.Find("Proving Ground")!;
        yard.RestoreProps();
        World.SetMap(yard);
        const ushort port = 26403;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.5, ref now, clock);
        if (!server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("built gun after death: the client never joined");
            return;
        }
        void Spawn()
        {
            for (var i = 0; i < 40 && (!sp.Alive || server.Local is { Alive: false }); i++)
            {
                a.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now); // (what the RESPAWN button does)
                a.RequestRespawn();
                server.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now);
                server.RequestRespawn();
                Pump(server, [a], 0.3, ref now, clock);
            }
        }
        bool Fires(Game g, Player p)
        {
            var t = Brutal.GlfwApi.Glfw.Time;
            var seen = p.LastShotTime;
            g.NextFireTime = 0;
            g.EquipUntil = 0;
            g.SelfTestWeaponFrame(t, 0.02f);
            g.UpdateTrigger(t, true, true);
            g.UpdateTrigger(t, false, false);
            Pump(server, [a], 0.4, ref now, clock);
            return p.LastShotTime != seen;
        }
        Spawn();
        var build = StandardBuilds.All.Select(b => b.Build()).First(b => b.Kind == BuildKind.Gun && b.Receivers.Count == 1).ToText();
        var hp = server.Local!;
        // A tool has its own speed, not the slow gun's, and the server lets it.
        var heavy = StandardBuilds.All.Select(b => b.Build()).Where(b => b.Kind == BuildKind.Gun).OrderBy(b => BuildStats.Compute(b, out _)?.Mobility ?? 9f).First().ToText();
        server.SandboxBench(sp, Game.BenchSpawn, 0, heavy);
        Pump(server, [a], 0.6, ref now, clock);
        var gunSpeed = a.LocalMoveSpeed(a.Slots[0].Def);
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.PhysicsGun);
        a.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.PhysicsGun;
        Pump(server, [a], 0.4, ref now, clock);
        var toolSpeed = a.LocalMoveSpeed(a.Slots[0].Def);
        var cap = server.MaxMoveSpeed(sp);
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        a.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.None;
        Pump(server, [a], 0.3, ref now, clock);
        Line($"speed: {a.Slots[0].Def.Name} {gunSpeed:0.00}, the physics gun {toolSpeed:0.00}, the server's cap {cap}%");
        if (gunSpeed >= 0.99f || MathF.Abs(toolSpeed - 1f) > 0.001f || cap < 100)
            Fail($"built gun after death: a tool's speed: gun {gunSpeed:0.00}, tool {toolSpeed:0.00}, cap {cap}");
        server.SandboxBench(sp, Game.BenchSpawn, 0, build);
        server.SandboxBench(hp, Game.BenchSpawn, 0, build);
        Pump(server, [a], 0.6, ref now, clock);
        foreach (var p in (Player[])[sp, hp])
        {
            p.Health = 1;
            server.DamagePlayer(p, p, 50, false, Throwables.KillIdBase, now, DamageKind.Explosive, p.Position);
        }
        Pump(server, [a], 0.4, ref now, clock);
        Spawn();
        Pump(server, [a], 0.5, ref now, clock);
        var client = Fires(a, sp);
        var host = Fires(server, hp);
        Line($"after death: client {sp.WeaponDef.Name} fired {client}, ammo {a.Slots[0].Ammo}/{a.Slots[0].Def.Mag}; host {hp.WeaponDef.Name} fired {host}");
        if (!client || !host || sp.WeaponDef != sp.BuiltPrimary || hp.WeaponDef != hp.BuiltPrimary || a.Slots[0].Ammo != a.Slots[0].Def.Mag - 1)
            Fail($"built gun after death: client fired {client}, host {host}, holding {sp.WeaponDef.Name} and {hp.WeaponDef.Name}");
    }
}
