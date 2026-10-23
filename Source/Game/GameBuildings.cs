using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What happened to a building's door or window (<see cref="Msg.BuildingEvent"/>).</summary>
public enum BuildingEventKind : byte
{
    DoorOpen,
    DoorClose,
    DoorBreak,
    DoorDamage,
    WindowBreak,
}

/// <summary>
/// Building doors and windows, server-authoritative (the way breakable props are, GameProps.cs). The interact key (F by default)
/// opens or shuts the door you're looking at (<see cref="Msg.BuildingUse"/>); bullets, melee and blasts wear doors down
/// and shatter glass; the server decides and tells everyone (<see cref="Msg.BuildingEvent"/>), and a player who joins
/// mid-match gets every door's state and health and the broken windows (<see cref="Msg.BuildingState"/>). Every PC then
/// moves the door's collision box, swings the leaf, and throws out debris. Bots, co-op enemies and zombies deal with doors
/// in Source/Game/Bots/Doors/BotDoors.cs (open ahead of them, shut behind them, bash them).
/// Everything is whole and back as built at the start of every match.
/// </summary>
public sealed partial class Game
{
    /// <summary>How far from your eyes a door can be used (to the leaf's middle).</summary>
    public const float DoorReach = 2.4f;

    /// <summary>Explosions hit doors this many times harder (a frag grenade within about 5 m breaks a wooden door, within 2 m a metal one).</summary>
    public const float DoorBlastMult = 3f;

    // ---- Interact (both sides) -----------------------------------------------------------------------------

    /// <summary>The door the local player is looking at and close enough to use, or -1.</summary>
    public int AimedDoor()
    {
        if (Local is not { Alive: true } me)
            return -1;
        var map = World.Map;
        if (map.BuildingDoors.Count == 0)
            return -1;
        var eye = me.Eye;
        var dir = LookDir(LookYaw, LookPitch);
        return DoorAlong(map, eye, dir);
    }

    /// <summary>The usable door nearest along a look ray from <paramref name="eye"/> (a little forgiving round the leaf), or -1.</summary>
    public static int DoorAlong(Map map, Vector3 eye, Vector3 dir)
    {
        var best = -1;
        var bestT = DoorReach + 0.6f;
        for (var i = 0; i < map.BuildingDoors.Count; i++)
        {
            var d = map.BuildingDoors[i];
            if (d.Broken || Vector3.DistanceSquared(d.Center, eye) > 16f)
                continue;
            var b = d.Plan.Closed;
            var grown = b with { Half = b.Half + new Vector3(0.15f, 0.05f, 0.15f) };
            if (grown.Raycast(eye, dir, bestT) is { } t && t < bestT && Vector3.Distance(eye, d.Center) <= DoorReach + 0.6f)
            {
                // Not through a wall: nothing solid between the eye and the doorway.
                if (World.Query.Raycast(eye, dir, t, QueryFilter.All.Without(QueryLayers.Doors)) < t - 0.1f)
                    continue;
                bestT = t;
                best = i;
            }
        }
        return best;
    }

    /// <summary>The interact key at a door: opens or shuts it. False if you're not looking at one (the key does its other jobs).</summary>
    public bool TryUseDoor(double now)
    {
        var i = AimedDoor();
        if (i < 0)
            return false;
        if (IsServer)
        {
            if (Local is { } me)
                ServerUseDoor(me, i, now);
        }
        else
        {
            _w.Begin(Msg.BuildingUse).U16((ushort)i);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
        return true;
    }

    /// <summary>The HUD hint for the door you're looking at, or null.</summary>
    public string? DoorHint()
    {
        var i = AimedDoor();
        if (i < 0)
            return null;
        var d = World.Map.BuildingDoors[i];
        return d.Open ? "close the door" : "open the door";
    }

    // ---- Server -------------------------------------------------------------------------------------------

    /// <summary>Server: <paramref name="p"/> pressed the interact key at door <paramref name="i"/>.</summary>
    internal bool ServerUseDoor(Player p, int i, double now)
    {
        var map = World.Map;
        if (!p.Alive || (uint)i >= (uint)map.BuildingDoors.Count)
            return false;
        var d = map.BuildingDoors[i];
        if (d.Broken || Vector3.Distance(p.Eye, d.Center) > DoorReach + 1.2f) // a little slack for lag
            return false;
        return ServerSetDoor(i, !d.Open, now);
    }

    /// <summary>Server: opens or shuts a door and tells everyone. A door won't shut on someone standing in the doorway.</summary>
    internal bool ServerSetDoor(int i, bool open, double now)
    {
        var map = World.Map;
        if (!IsServer || (uint)i >= (uint)map.BuildingDoors.Count)
            return false;
        var d = map.BuildingDoors[i];
        if (!open)
        {
            var leaf = d.Plan.Closed;
            foreach (var p in Players.Values)
            {
                if (!p.Alive)
                    continue;
                var hb = World.Hitbox(p.Position, p.Sim.Stance, p.Yaw);
                if (hb.Min.X < leaf.Max.X && hb.Max.X > leaf.Min.X && hb.Min.Y < leaf.Max.Y && hb.Max.Y > leaf.Min.Y && hb.Min.Z < leaf.Max.Z && hb.Max.Z > leaf.Min.Z)
                    return false; // someone's in the way
            }
            if (NpcInLeaf(leaf))
                return false; // a co-op enemy or a zombie in the way (BotDoors.cs)
        }
        if (!map.SetBuildingDoor(i, open))
            return false;
        StampDoor(i, now); // when and by whom (bots don't fight over a door: BotDoors.cs)
        BroadcastBuildingEvent(open ? BuildingEventKind.DoorOpen : BuildingEventKind.DoorClose, i, d.Health, d.Center);
        OnDoorMoved(i, open);
        return true;
    }

    /// <summary>Server: a bullet from <paramref name="origin"/> along <paramref name="dir"/> that stopped at <paramref name="dist"/>.</summary>
    internal void ShotBuildings(Player? shooter, Vector3 origin, Vector3 dir, float dist, float damage, double now)
    {
        var map = World.Map;
        if (map.PlacedBuildings.Count == 0 || damage <= 0)
            return;
        var doorHit = -1;
        var doorT = float.MaxValue;
        foreach (var b in map.PlacedBuildings)
        {
            var bb = b.Plan.Bounds;
            if ((bb with { Half = bb.Half + new Vector3(0.5f) }).Raycast(origin, dir, dist + 0.1f) == null)
                continue;
            for (var i = b.DoorStart; i < b.DoorStart + b.DoorCount; i++)
            {
                var d = map.BuildingDoors[i];
                if (d.Broken || d.Open)
                    continue;
                if (d.Plan.Closed.Raycast(origin, dir, dist + 0.05f) is { } t && t >= dist - 0.05f && t < doorT)
                {
                    doorT = t;
                    doorHit = i; // the door that stopped the bullet
                }
            }
            for (var i = b.WindowStart; i < b.WindowStart + b.WindowCount; i++)
            {
                var w = map.BuildingWindows[i];
                if (!w.Broken && w.Plan.Glass.Raycast(origin, dir, dist) is { } t && t < dist)
                    DamageWindow(i, damage, PropDamage.Bullet, origin);
            }
        }
        if (doorHit >= 0)
            DamageDoor(doorHit, damage, PropDamage.Bullet, origin);
    }

    /// <summary>Server: a melee swing that hit nobody: the nearest shut door or whole window in reach along the aim takes it.</summary>
    internal bool MeleeBuildings(Player? attacker, Vector3 origin, Vector3 dir, double now)
    {
        var map = World.Map;
        if (map.PlacedBuildings.Count == 0)
            return false;
        var reach = MeleeRange + 0.3f;
        int door = -1, window = -1;
        var best = reach;
        for (var i = 0; i < map.BuildingDoors.Count; i++)
        {
            var d = map.BuildingDoors[i];
            if (!d.Broken && !d.Open && d.Plan.Closed.Raycast(origin, dir, best) is { } t && t < best)
            {
                best = t;
                door = i;
            }
        }
        for (var i = 0; i < map.BuildingWindows.Count; i++)
        {
            var w = map.BuildingWindows[i];
            if (!w.Broken && (w.Plan.Glass with { Half = w.Plan.Glass.Half + new Vector3(0.05f) }).Raycast(origin, dir, best) is { } t && t < best)
            {
                best = t;
                window = i;
                door = -1;
            }
        }
        if (World.Query.Raycast(origin, dir, best, QueryFilter.All.Without(QueryLayers.Doors)) < best - 0.15f)
            return false; // a wall in the way
        if (window >= 0)
            DamageWindow(window, MeleeObjectDamage, PropDamage.Melee, origin);
        else if (door >= 0)
            DamageDoor(door, MeleeObjectDamage, PropDamage.Melee, origin);
        return window >= 0 || door >= 0;
    }

    /// <summary>Server: a blast (grenade or kill streak). Doors take <see cref="DoorBlastMult"/> times the damage; glass in range shatters.</summary>
    internal void BlastBuildings(Player? owner, Vector3 center, float radius, float maxDamage, PropDamage kind, double now)
    {
        var map = World.Map;
        if (map.PlacedBuildings.Count == 0)
            return;
        foreach (var b in map.PlacedBuildings)
        {
            var bb = b.Plan.Bounds;
            if (Vector3.Distance(Vector3.Clamp(center, bb.Min, bb.Max), center) > radius)
                continue;
            for (var i = b.DoorStart; i < b.DoorStart + b.DoorCount; i++)
            {
                var d = map.BuildingDoors[i];
                if (d.Broken)
                    continue;
                var box = d.Plan.Closed;
                var dist = Vector3.Distance(Vector3.Clamp(center, box.Min, box.Max), center);
                if (dist > radius || Shielded(center, box, dist))
                    continue;
                DamageDoor(i, MathF.Max(1f, maxDamage * DoorBlastMult * MathF.Pow(1f - dist / radius, 1.4f)), kind, center);
            }
            for (var i = b.WindowStart; i < b.WindowStart + b.WindowCount; i++)
            {
                var w = map.BuildingWindows[i];
                if (w.Broken)
                    continue;
                var dist = Vector3.Distance(Vector3.Clamp(center, w.Plan.Glass.Min, w.Plan.Glass.Max), center);
                if (dist > radius || Shielded(center, w.Plan.Glass, dist))
                    continue;
                DamageWindow(i, MathF.Max(1f, maxDamage * MathF.Pow(1f - dist / radius, 1.4f)), kind, center);
            }
        }
    }

    /// <summary>Is a wall between a blast and a door or window (the thing itself doesn't count)?</summary>
    private static bool Shielded(Vector3 center, Box target, float dist)
    {
        if (dist < 0.3f)
            return false;
        var to = Vector3.Clamp(center, target.Min, target.Max) - center;
        return World.Query.Raycast(center, to / dist, dist, QueryFilter.All.Without(QueryLayers.Doors | QueryLayers.Clip)) < dist - 0.2f;
    }

    private void DamageDoor(int i, float amount, PropDamage kind, Vector3 from)
    {
        var map = World.Map;
        var d = map.BuildingDoors[i];
        if (map.DamageBuildingDoor(i, Math.Max(1, (int)MathF.Round(amount))))
        {
            if (map.BreakBuildingDoor(i))
            {
                BroadcastBuildingEvent(BuildingEventKind.DoorBreak, i, 0, from);
                OnDoorBroken(i, from, kind);
            }
        }
        else
            BroadcastBuildingEvent(BuildingEventKind.DoorDamage, i, d.Health, from, PacketReliability.Unreliable);
    }

    private void DamageWindow(int i, float amount, PropDamage kind, Vector3 from)
    {
        var map = World.Map;
        if (map.DamageBuildingWindow(i, Math.Max(1, (int)MathF.Round(amount))) && map.BreakBuildingWindow(i))
        {
            BroadcastBuildingEvent(BuildingEventKind.WindowBreak, i, 0, from);
            OnWindowBroken(i, from, kind);
        }
    }

    // ---- Bots ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Server: a bot walking into a shut door opens it (bots plan their routes through doors, and open the doors on them
    /// before they get there: BotDoors.cs). With a <paramref name="wish"/>, only doors in the way it's heading.
    /// </summary>
    private void BotOpenDoors(Player p, double now, Vector3 wish = default)
    {
        var map = World.Map;
        if (map.BuildingDoors.Count == 0 || !p.Alive)
            return;
        var pos = p.Position;
        for (var i = 0; i < map.BuildingDoors.Count; i++)
        {
            var d = map.BuildingDoors[i];
            if (d.Open || d.Broken)
                continue;
            var b = d.Plan.Closed;
            if (pos.Y < b.Min.Y - 0.6f || pos.Y > b.Max.Y)
                continue;
            var dx = MathF.Max(0, MathF.Max(b.Min.X - pos.X, pos.X - b.Max.X));
            var dz = MathF.Max(0, MathF.Max(b.Min.Z - pos.Z, pos.Z - b.Max.Z));
            if (dx * dx + dz * dz >= 0.95f * 0.95f || (wish != Vector3.Zero && (b.Center.X - pos.X) * wish.X + (b.Center.Z - pos.Z) * wish.Z <= 0))
                continue;
            if (wish != Vector3.Zero && p.Bot is { } bb && (bb.DoorTogglesIn(i, now, 4) > 0 || bb.HoldsShut(i, now)))
                continue; // brushing past a door it just shut (behind it, or for its hold): not open again (BotDoors.cs)
            _doorActor = p.Id;
            var opened = ServerSetDoor(i, true, now);
            _doorActor = byte.MaxValue;
            if (opened && p.Bot is { } brain)
            {
                brain.LogDoorToggle(i, now);
                BotDoorOpens++;
            }
        }
    }

    // ---- Packets ----------------------------------------------------------------------------------------------

    /// <summary>Bytes of a <see cref="Msg.BuildingEvent"/> after its id: kind, index, health, where it came from.</summary>
    public const int BuildingEventBytes = 1 + 2 + 2 + 12;

    /// <summary>Most doors (3 bytes each) or windows (1 bit each) in one <see cref="Msg.BuildingState"/> packet.</summary>
    public const int DoorStateBatch = 300, WindowStateBatch = 6000;

    public static void WriteBuildingEvent(PacketWriter w, BuildingEventKind kind, int index, int health, Vector3 from) =>
        w.Begin(Msg.BuildingEvent).U8((byte)kind).U16((ushort)index).U16((ushort)Math.Clamp(health, 0, ushort.MaxValue)).Vec(from);

    private void BroadcastBuildingEvent(BuildingEventKind kind, int index, int health, Vector3 from, PacketReliability rel = PacketReliability.ReliableOrdered)
    {
        WriteBuildingEvent(_w, kind, index, health, from);
        Net.Broadcast(_w.Span, rel);
        BuildingEventTap?.Invoke(kind, index, health);
    }

    /// <summary>
    /// Every door's state (open, broken, health) and the broken windows, as one or more packets: section 0 doors (3 bytes
    /// each), section 1 windows (a bit each). Full state, so a joining client needs nothing else.
    /// </summary>
    public static int WriteBuildingState(PacketWriter w, Map map, Action<PacketWriter> send)
    {
        var packets = 0;
        for (var at = 0; at < map.BuildingDoors.Count; at += DoorStateBatch)
        {
            var n = Math.Min(DoorStateBatch, map.BuildingDoors.Count - at);
            w.Begin(Msg.BuildingState).U8(0).U16((ushort)at).U16((ushort)n);
            for (var k = 0; k < n; k++)
            {
                var d = map.BuildingDoors[at + k];
                w.U8((byte)((d.Open ? 1 : 0) | (d.Broken ? 2 : 0))).U16((ushort)Math.Clamp(d.Health, 0, ushort.MaxValue));
            }
            send(w);
            packets++;
        }
        for (var at = 0; at < map.BuildingWindows.Count; at += WindowStateBatch)
        {
            var n = Math.Min(WindowStateBatch, map.BuildingWindows.Count - at);
            w.Begin(Msg.BuildingState).U8(1).U16((ushort)at).U16((ushort)n);
            for (var k = 0; k < n; k += 8)
            {
                byte bits = 0;
                for (var j = 0; j < 8 && k + j < n; j++)
                    if (map.BuildingWindows[at + k + j].Broken)
                        bits |= (byte)(1 << j);
                w.U8(bits);
            }
            send(w);
            packets++;
        }
        return packets;
    }

    /// <summary>Client: applies one <see cref="Msg.BuildingState"/> packet (after the id). No debris: those changed before you came.</summary>
    public static void ReadBuildingState(Map map, ref PacketReader r)
    {
        if (!r.Has(5))
            return;
        var section = r.U8();
        int at = r.U16();
        int n = r.U16();
        if (section == 0)
        {
            for (var k = 0; k < n && r.Has(3); k++)
            {
                var flags = r.U8();
                int health = r.U16();
                var i = at + k;
                if ((uint)i >= (uint)map.BuildingDoors.Count)
                    continue;
                if ((flags & 2) != 0)
                    map.BreakBuildingDoor(i);
                else
                {
                    map.SetBuildingDoor(i, (flags & 1) != 0);
                    map.BuildingDoors[i].Health = health;
                }
                map.BuildingDoors[i].Swing = map.BuildingDoors[i].Open ? 1 : 0;
            }
        }
        else if (section == 1)
        {
            for (var k = 0; k < n && r.Has(1); k += 8)
            {
                var bits = r.U8();
                for (var j = 0; j < 8 && k + j < n; j++)
                    if ((bits & (1 << j)) != 0 && (uint)(at + k + j) < (uint)map.BuildingWindows.Count)
                        map.BreakBuildingWindow(at + k + j);
            }
        }
    }

    /// <summary>Server: sends a joining player the doors and windows as they are this match.</summary>
    private void SendBuildingState(RakNetGUID to)
    {
        var map = World.Map;
        if (map.BuildingDoors.Count == 0 && map.BuildingWindows.Count == 0)
            return;
        WriteBuildingState(_w, map, w => Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered));
    }

    private void OnBuildingPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.BuildingUse when IsServer && r.Has(2):
                if (_clients.TryGetValue(sender.G, out var c) && Players.TryGetValue(c.Id, out var p))
                    ServerUseDoor(p, r.U16(), now);
                break;
            case (byte)Msg.BuildingEvent when !IsServer && r.Has(BuildingEventBytes):
            {
                var kind = (BuildingEventKind)r.U8();
                int i = r.U16();
                int health = r.U16();
                var from = r.Vec();
                ApplyBuildingEvent(World.Map, kind, i, health, from);
                break;
            }
            case (byte)Msg.BuildingState when !IsServer:
                ReadBuildingState(World.Map, ref r);
                break;
        }
    }

    /// <summary>Client: a door or window changed on the server.</summary>
    internal void ApplyBuildingEvent(Map map, BuildingEventKind kind, int i, int health, Vector3 from)
    {
        switch (kind)
        {
            case BuildingEventKind.DoorOpen or BuildingEventKind.DoorClose when (uint)i < (uint)map.BuildingDoors.Count:
                map.SetBuildingDoor(i, kind == BuildingEventKind.DoorOpen);
                map.BuildingDoors[i].Health = health;
                OnDoorMoved(i, kind == BuildingEventKind.DoorOpen);
                break;
            case BuildingEventKind.DoorDamage when (uint)i < (uint)map.BuildingDoors.Count:
                map.BuildingDoors[i].Health = health;
                break;
            case BuildingEventKind.DoorBreak when (uint)i < (uint)map.BuildingDoors.Count:
                map.BreakBuildingDoor(i);
                OnDoorBroken(i, from, PropDamage.Bullet);
                break;
            case BuildingEventKind.WindowBreak when (uint)i < (uint)map.BuildingWindows.Count:
                map.BreakBuildingWindow(i);
                OnWindowBroken(i, from, PropDamage.Bullet);
                break;
        }
    }

    // ---- Effects (both sides) ----------------------------------------------------------------------------------

    private void OnDoorMoved(int i, bool open)
    {
        var d = World.Map.BuildingDoors[i];
        Audio.Effect3DSmall(d.Plan.Material == DoorMaterial.Metal ? Sfx.ImpactMetal : Sfx.ImpactWood, d.Center, open ? 0.45f : 0.6f, 30f);
    }

    private void OnDoorBroken(int i, Vector3 from, PropDamage kind)
    {
        var d = World.Map.BuildingDoors[i];
        var c = d.Plan.Color;
        var (count, lo, hi, sfx) = d.Plan.Material switch
        {
            DoorMaterial.Metal => (14, 0.08f, 0.3f, Sfx.BreakMetal),
            DoorMaterial.Glass => (22, 0.04f, 0.14f, Sfx.BreakGlass),
            _ => (18, 0.08f, 0.45f, Sfx.BreakWood),
        };
        PropDebris.SpawnBox(d.Plan.Closed, from, kind, i * 7919 + (int)(PropDebris.Spawned & 0xFFFF), count, lo, hi, 4.5f, c, c * 0.7f);
        Audio.Effect3D(sfx, d.Center, 1f);
    }

    private void OnWindowBroken(int i, Vector3 from, PropDamage kind)
    {
        var w = World.Map.BuildingWindows[i];
        var g = w.Plan.Glass;
        var area = g.Half.X * g.Half.Y + g.Half.Z * g.Half.Y;
        PropDebris.SpawnBox(g, from, kind, i * 104729 + (int)(PropDebris.Spawned & 0xFFFF), Math.Clamp((int)(area * 14), 6, 24), 0.03f, 0.12f, 3.5f,
            Map.GlassColor, new Vector3(0.85f, 0.93f, 0.97f));
        Audio.Effect3D(Sfx.BreakGlass, g.Center, 0.9f);
    }
}
