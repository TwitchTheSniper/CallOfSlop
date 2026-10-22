using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>A motion sensor ping on the local player's HUD.</summary>
public struct TacPing
{
    public byte Target;
    public Vector3 Pos;
    public double At;
}

/// <summary>A visual burst (flash, stun, trophy pop, device breaking).</summary>
public readonly record struct TacBurst(Vector3 Pos, double Born, ThrowableKind Kind);

/// <summary>
/// Tactical equipment on the server and clients (tuning and formulas in TacticalGear.cs). Everything is
/// server-authoritative: the server applies flashes and stuns (to people and bots), runs the devices (trophy
/// intercepts, motion sensor scans, decoy bursts), takes their damage and picks the spawn on a Tactical Insertion.
/// Clients get the devices in <see cref="Msg.TacState"/> (with the snapshots while any exist) and the moments in
/// reliable <see cref="Msg.TacEvent"/>s; a flash or stun on the local player is applied from its event, on the client
/// clock, so the white-out and the slow start the moment it's heard. Nothing here allocates per tick.
/// </summary>
public sealed partial class Game
{
    /// <summary>Every tactical device in the world (server: authoritative; client: from the last update).</summary>
    public readonly List<TacDevice> TacDevices = new();

    /// <summary>Visual bursts (flash, stun, intercepts, breaks) for TacticalRender.</summary>
    public readonly List<TacBurst> TacBursts = new();

    /// <summary>Motion sensor pings for the HUD (a ring; <see cref="TacPing.At"/> &lt; 0 is empty).</summary>
    public readonly TacPing[] Pings = new TacPing[32];

    private int _pingNext;
    private double _lastPingSound = -10;
    private byte _nextTacId = 1;
    private bool _tacSent;
    private uint _lastTacTick;
    private double _tacNow; // server: the time of the current tick (ApplyCmd has no clock of its own)
    private double _tacLocalNow; // the last frame's time (local input and look)

    // Server: each player's flash, stun and stim (indexed by player id).
    private readonly double[] _flashAt = new double[256];
    private readonly float[] _flashS = new float[256];
    private readonly double[] _stunAt = new double[256];
    private readonly float[] _stunS = new float[256];
    private readonly double[] _blindUntil = new double[256];
    private readonly double[] _stimUntil = new double[256];
    private readonly float[] _stimCarry = new float[256];

    // Local player (client clock).
    private double _localFlashAt = -100, _localStunAt = -100;
    private float _localFlashS, _localStunS;

    // Counters the self-test reads.
    public int TacInterceptsSeen, TacDecoyShotsPlayed, TacPingsSeen, TacEventsSent;

    /// <summary>Server: how often bots used each tactical item (by kind), for the self-test and the log.</summary>
    public readonly int[] BotTacticalUses = new int[(int)ThrowableKind.Stim + 1];

    /// <summary>Most devices at once (oldest go first); the update stays far inside one datagram.</summary>
    public const int MaxTacDevices = 48;

    /// <summary>Bytes after the message id in a <see cref="Msg.TacEvent"/>.</summary>
    public const int TacEventBytes = 17;

    // =====================================================================
    // Server: using equipment
    // =====================================================================

    /// <summary>Server: a placed device or the stim (thrown items go through ServerThrow as projectiles).</summary>
    private void ServerUseTactical(Player p, ThrowableKind kind, Vector3 dir, double now)
    {
        var flat = new Vector2(dir.X, dir.Z);
        var yaw = flat.LengthSquared() > 1e-4f ? MathF.Atan2(dir.X, -dir.Z) : p.Yaw;
        p.LastCombatTime = now;
        switch (kind)
        {
            case ThrowableKind.Stim:
                ServerStim(p, now);
                break;
            case ThrowableKind.Insertion:
                for (var i = TacDevices.Count - 1; i >= 0; i--)
                    if (TacDevices[i].Owner == p.Id && TacDevices[i].Kind == ThrowableKind.Insertion)
                        RemoveTacDevice(TacDevices[i], byte.MaxValue, now); // one insertion at a time: the new one replaces it
                NewTacDevice(p, kind, PlaceSpot(p, yaw, 0f), yaw, now);
                break;
            case ThrowableKind.Trophy:
                NewTacDevice(p, kind, PlaceSpot(p, yaw, 1.0f), yaw, now);
                break;
            case ThrowableKind.Cover:
                NewTacDevice(p, kind, PlaceSpot(p, yaw, TacticalGear.CoverAhead), yaw, now);
                break;
        }
    }

    /// <summary>
    /// Where a device goes: on the ground <paramref name="ahead"/> metres in front (with a clear line from the eye and
    /// room to stand), or else on the ground at the player's feet. Never inside a wall, a shut door or a prop.
    /// </summary>
    private static Vector3 PlaceSpot(Player p, float yaw, float ahead)
    {
        var up = Vector3.UnitY;
        if (ahead > 0)
        {
            var spot = p.Position + World.FlatForward(yaw) * ahead;
            var down = World.Query.Raycast(spot + up * 1.2f, -up, 2.5f, QueryFilter.All);
            if (down < 2.49f)
            {
                spot.Y += 1.2f - down;
                var look = spot + up * 0.5f - p.Eye;
                var len = look.Length();
                var clear = len < 0.05f || World.Query.Raycast(p.Eye, look / len, len, QueryFilter.All) >= len - 0.05f;
                if (clear && !World.Query.OverlapBody(spot + up * 0.05f, 1.0f, QueryFilter.StaticOnly))
                    return spot;
            }
        }
        var feet = p.Position;
        var below = World.Query.Raycast(feet + up * 0.5f, -up, 3f, QueryFilter.All);
        if (below < 2.99f)
            feet.Y += 0.5f - below;
        return feet;
    }

    private TacDevice NewTacDevice(Player owner, ThrowableKind kind, Vector3 pos, float yaw, double now)
    {
        if (TacDevices.Count >= MaxTacDevices)
            RemoveTacDevice(TacDevices[0], byte.MaxValue, now);
        var id = _nextTacId;
        for (var tries = 0; tries < 255 && FindTacDevice(id) != null; tries++)
            id = (byte)(id % 250 + 1);
        _nextTacId = (byte)(id % 250 + 1);
        var e = new TacDevice
        {
            Id = id, Kind = kind, Owner = owner.Id, Pos = pos, Yaw = yaw, Born = now, Until = now + TacticalGear.Life(kind),
            Charges = kind == ThrowableKind.Trophy ? TacticalGear.TrophyCharges : (byte)0, LastUpdate = now,
            NextAction = now + 0.4, Seed = (uint)(owner.Id * 7919 + ServerTick),
        };
        e.Health = e.MaxHealth;
        TacDevices.Add(e);
        EmitTac(TacEventKind.Placed, e.Id, (byte)kind, owner.Id, pos, now);
        return e;
    }

    public TacDevice? FindTacDevice(byte id)
    {
        foreach (var e in TacDevices)
            if (e.Id == id)
                return e;
        return null;
    }

    /// <summary>Server: a device is gone. <paramref name="by"/>: who destroyed it (255: it ran out, was used up or replaced).</summary>
    private void RemoveTacDevice(TacDevice e, byte by, double now)
    {
        if (!TacDevices.Remove(e))
            return;
        EmitTac(TacEventKind.Destroyed, e.Id, (byte)e.Kind, (ushort)(e.Owner | by << 8), e.Pos, now);
    }

    private void ServerStim(Player p, double now)
    {
        if (!p.Alive)
            return;
        p.Health = Math.Min(World.MaxHealth, p.Health + TacticalGear.StimHeal);
        _stimUntil[p.Id] = now + TacticalGear.StimTime;
        _stimCarry[p.Id] = 0;
        _stunS[p.Id] = 0; // it shakes off a stun
        EmitTac(TacEventKind.Stim, p.Id, 0, 0, p.Position, now);
    }

    /// <summary>Server: a thrown tactical item went off (from ServerDetonate).</summary>
    private void ServerTacticalDetonate(Projectile proj, Player? owner, double now)
    {
        switch (proj.Kind)
        {
            case ThrowableKind.Flash:
                ServerFlash(proj.Owner, owner, proj.Pos + new Vector3(0, 0.15f, 0), now);
                break;
            case ThrowableKind.Stun:
                ServerStun(proj.Owner, owner, proj.Pos + new Vector3(0, 0.15f, 0), now);
                break;
            case ThrowableKind.Decoy:
            case ThrowableKind.MotionSensor:
                if (owner != null)
                    NewTacDevice(owner, proj.Kind, proj.Pos, MathF.Atan2(proj.Vel.X, -proj.Vel.Z), now);
                break;
        }
    }

    /// <summary>Can a flash or stun from <paramref name="ownerId"/> affect <paramref name="v"/>? Enemies and the thrower (weaker); teammates are spared.</summary>
    private bool TacticalAffects(byte ownerId, Player? owner, Player v) =>
        v.Alive && (v.Id == ownerId || owner == null || CanDamage(owner, v));

    private bool ClearLine(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var len = d.Length();
        return len < 0.3f || World.Query.Raycast(from, d / len, len, QueryFilter.All) >= len - 0.05f;
    }

    private void ServerFlash(byte ownerId, Player? owner, Vector3 burst, double now)
    {
        foreach (var v in Players.Values)
        {
            if (!TacticalAffects(ownerId, owner, v))
                continue;
            var eye = v.Eye;
            if (Vector3.DistanceSquared(eye, burst) >= TacticalGear.FlashRadius * TacticalGear.FlashRadius)
                continue;
            var s = TacticalGear.FlashStrength(eye, World.Forward(v.Yaw, v.Pitch), burst, ClearLine(eye, burst), v.Perks.Has(PerkId.FlakJacket), v.Id == ownerId);
            if (s > 0)
                Afflict(v, ThrowableKind.Flash, s, burst, now);
        }
        StunZombies(burst, TacticalGear.FlashRadius * 0.6f, TacticalGear.FlashHoldMax, now);
    }

    private void ServerStun(byte ownerId, Player? owner, Vector3 burst, double now)
    {
        foreach (var v in Players.Values)
        {
            if (!TacticalAffects(ownerId, owner, v))
                continue;
            var dist = Vector3.Distance(v.Chest, burst);
            if (dist >= TacticalGear.StunRadius)
                continue;
            var s = TacticalGear.StunStrength(dist, ClearLine(burst, v.Chest), v.Perks.Has(PerkId.FlakJacket), v.Id == ownerId);
            if (s > 0)
                Afflict(v, ThrowableKind.Stun, s, burst, now);
        }
        StunZombies(burst, TacticalGear.StunRadius, TacticalGear.StunTime(1f), now);
    }

    /// <summary>Zombies caught in a flash or stun stagger (stand still) for a moment. (Zombies' start gear carries no equipment today.)</summary>
    private void StunZombies(Vector3 burst, float radius, float seconds, double now)
    {
        foreach (var z in Horde)
            if (z.State != ZState.Dead && Vector3.DistanceSquared(z.Position, burst) < radius * radius)
                z.StunnedUntil = Math.Max(z.StunnedUntil, now + seconds);
    }

    /// <summary>Server: <paramref name="v"/> is flashed or stunned at strength <paramref name="s"/> (a stronger hit replaces a weaker one).</summary>
    private void Afflict(Player v, ThrowableKind kind, float s, Vector3 origin, double now)
    {
        var id = v.Id;
        if (kind == ThrowableKind.Flash)
        {
            if (TacticalGear.FlashAlpha(s, 0) < TacticalGear.FlashAlpha(_flashS[id], (float)(now - _flashAt[id])))
                return;
            _flashAt[id] = now;
            _flashS[id] = s;
            var blind = TacticalGear.BotBlindTime(s);
            _blindUntil[id] = Math.Max(_blindUntil[id], now + blind);
            if (v.Bot is { } b && blind > 0)
                b.Target = byte.MaxValue; // lost sight of everything
        }
        else
        {
            if (s < TacticalGear.StunNow(_stunS[id], (float)(now - _stunAt[id])))
                return;
            _stunAt[id] = now;
            _stunS[id] = s;
        }
        EmitTac(TacEventKind.Afflict, id, (byte)kind, (ushort)Math.Clamp((int)MathF.Round(s * 65535f), 1, 65535), origin, now);
    }

    /// <summary>Server, from Spawn: a fresh life has no flash, stun or stim; and a waiting Tactical Insertion is where it starts.</summary>
    private Vector3 TacticalSpawnPoint(Player p, Vector3 picked, double now)
    {
        var id = p.Id;
        _flashS[id] = _stunS[id] = 0;
        _blindUntil[id] = _stimUntil[id] = 0;
        if (Phase != MatchPhase.Playing || !Module.UsesThrowables)
            return picked;
        for (var i = 0; i < TacDevices.Count; i++)
        {
            var e = TacDevices[i];
            if (e.Owner != id || e.Kind != ThrowableKind.Insertion)
                continue;
            RemoveTacDevice(e, byte.MaxValue, now); // used up
            EmitTac(TacEventKind.Inserted, id, 0, 0, e.Pos, now);
            return e.Pos + new Vector3(0, 0.02f, 0);
        }
        return picked;
    }

    // =====================================================================
    // Server: every tick
    // =====================================================================

    private void ServerTickTactical(double now)
    {
        _tacNow = now;
        if (Phase != MatchPhase.Playing && TacDevices.Count > 0)
            TacDevices.Clear(); // the match is over: the next update (empty) tells the clients
        foreach (var p in Players.Values)
            StimTick(p, now);
        for (var i = TacDevices.Count - 1; i >= 0; i--)
        {
            if (i >= TacDevices.Count)
                continue;
            var e = TacDevices[i];
            if (now >= e.Until || !Players.TryGetValue(e.Owner, out var owner))
            {
                RemoveTacDevice(e, byte.MaxValue, now); // ran out, or its owner left
                continue;
            }
            switch (e.Kind)
            {
                case ThrowableKind.Trophy:
                    TickTrophy(e, owner, now);
                    break;
                case ThrowableKind.MotionSensor:
                    TickSensor(e, owner, now);
                    break;
                case ThrowableKind.Decoy:
                    TickDecoy(e, owner, now);
                    break;
            }
            // Crushed: a building door swung shut on it, or a mover rolled through it.
            if ((ServerTick + e.Id) % 30 == 0 && e.Kind != ThrowableKind.MotionSensor
                && World.Query.PointInside(e.Hitbox.Center, 0.05f, new QueryFilter(QueryLayers.Doors | QueryLayers.Movers)))
                RemoveTacDevice(e, byte.MaxValue, now);
        }
        if (ServerTick % World.SnapshotEvery == 0)
            SendTactical();
    }

    private void StimTick(Player p, double now)
    {
        var id = p.Id;
        if (_stimUntil[id] <= now || !p.Alive)
            return;
        _stimCarry[id] += TacticalGear.StimRegen * World.TickDt;
        var whole = (int)_stimCarry[id];
        if (whole <= 0)
            return;
        _stimCarry[id] -= whole;
        p.Health = Math.Min(World.MaxHealth, p.Health + whole);
    }

    /// <summary>Shoots down the nearest enemy projectile in range with a clear line (one per re-arm); breaks after its last charge.</summary>
    private void TickTrophy(TacDevice e, Player owner, double now)
    {
        if (now < e.NextAction || Projectiles.Count == 0)
            return;
        var head = e.Pos + new Vector3(0, 0.75f, 0);
        Projectile? hit = null;
        var best = TacticalGear.TrophyRadius;
        foreach (var pr in Projectiles.Values)
        {
            if (pr.Owner == e.Owner || !Players.TryGetValue(pr.Owner, out var thrower) || !StreakIsEnemy(owner, thrower))
                continue;
            var d = Vector3.Distance(pr.Pos, head);
            if (d > best || d < 0.01f)
                continue;
            if (World.Query.Raycast(head, (pr.Pos - head) / d, d, QueryFilter.All) < d - 0.15f)
                continue;
            best = d;
            hit = pr;
        }
        if (hit == null)
            return;
        Projectiles.Remove(hit.Id);
        e.Charges = (byte)Math.Max(0, e.Charges - 1);
        e.NextAction = now + TacticalGear.TrophyRearm;
        EmitTac(TacEventKind.Intercept, e.Id, (byte)hit.Kind, hit.Id, hit.Pos, now);
        if (e.Charges == 0)
            RemoveTacDevice(e, byte.MaxValue, now);
    }

    /// <summary>Pings every enemy moving fast enough within range, through walls (Ghost hides them). A bot owner goes to check.</summary>
    private void TickSensor(TacDevice e, Player owner, double now)
    {
        if (now < e.NextAction)
            return;
        e.NextAction = now + TacticalGear.SensorScan;
        foreach (var o in Players.Values)
        {
            if (!SensorSees(e, owner, o))
                continue;
            EmitTac(TacEventKind.Ping, e.Owner, o.Id, e.Id, o.Position, now);
            if (owner.Bot is { } b && b.Target == byte.MaxValue && Vector3.DistanceSquared(owner.Position, o.Position) > 4f)
            {
                b.HeardAt = o.Position;
                b.HeardTime = now;
            }
        }
    }

    /// <summary>Would motion sensor <paramref name="e"/> (owned by <paramref name="owner"/>) ping <paramref name="o"/> right now?</summary>
    public bool SensorSees(TacDevice e, Player owner, Player o)
    {
        if (!o.Alive || !StreakIsEnemy(owner, o) || o.Perks.Has(PerkId.Ghost))
            return false;
        var v = o.Sim.Velocity;
        if (v.X * v.X + v.Z * v.Z < TacticalGear.SensorMinSpeed * TacticalGear.SensorMinSpeed)
            return false;
        return Vector3.DistanceSquared(o.Position, e.Pos) <= TacticalGear.SensorRadius * TacticalGear.SensorRadius;
    }

    /// <summary>Fires a fake burst of the owner's gun every second or so; enemy bots in earshot go and look.</summary>
    private void TickDecoy(TacDevice e, Player owner, double now)
    {
        if (now < e.NextAction)
            return;
        var def = Weapons.Get(owner.Primary);
        var roll = TacticalGear.Roll(e.Seed, (uint)(now * 10), 3);
        var shots = def.Mode == FireMode.Semi ? 2 + (int)(roll * 3) : 3 + (int)(roll * 4);
        var interval = Math.Clamp(def.FireInterval * (def.Mode == FireMode.Semi ? 2.2f : 1f), 0.07f, 0.5f);
        var ms = (int)MathF.Round(interval * 1000f / 4f);
        e.NextAction = now + shots * interval + 0.9 + roll * 0.8;
        EmitTac(TacEventKind.DecoyBurst, e.Id, (byte)shots, (ushort)(def.Id | Math.Clamp(ms, 1, 255) << 8), e.Pos, now);
        const float r2 = TacticalGear.DecoyHearing * TacticalGear.DecoyHearing;
        foreach (var o in Players.Values)
            if (o.Bot is { } b && o.Alive && b.Target == byte.MaxValue && StreakIsEnemy(owner, o) && Vector3.DistanceSquared(o.Position, e.Pos) < r2)
            {
                b.HeardAt = e.Pos;
                b.HeardTime = now;
            }
    }

    // =====================================================================
    // Server: damage, shots, blasts
    // =====================================================================

    /// <summary>
    /// For shots: the nearest tactical device along the ray closer than <paramref name="dist"/> (shortened to it).
    /// Deployable Cover stops every bullet (both sides, anyone's); the other devices only stop their owner's enemies' bullets.
    /// </summary>
    private TacDevice? TacticalRaycast(Vector3 origin, Vector3 dir, ref float dist, Player shooter)
    {
        TacDevice? best = null;
        for (var i = 0; i < TacDevices.Count; i++)
        {
            var e = TacDevices[i];
            float? t;
            if (e.Kind == ThrowableKind.Cover)
                t = TacticalGear.CoverRay(e.Pos, e.Yaw, origin, dir, dist);
            else if (e.Owner != shooter.Id && Players.TryGetValue(e.Owner, out var owner) && StreakIsEnemy(owner, shooter))
                t = e.Hitbox.Raycast(origin, dir, dist);
            else
                continue;
            if (t is { } d && d < dist)
            {
                dist = d;
                best = e;
            }
        }
        return best;
    }

    /// <summary>Client: how far a predicted tracer goes before a device stops it (covers, and enemies' other devices).</summary>
    public float TacticalRayDrawn(Vector3 origin, Vector3 dir, float dist)
    {
        if (TacDevices.Count == 0 || Local is not { } me)
            return dist;
        TacticalRaycast(origin, dir, ref dist, me);
        return dist;
    }

    /// <summary>Server: a bullet hit a device. Enemies' bullets damage it; returns shot flags for the shooter.</summary>
    private byte ShotTactical(TacDevice e, Player shooter, float damage, double now)
    {
        if (e.Owner == shooter.Id || !Players.TryGetValue(e.Owner, out var owner) || !StreakIsEnemy(owner, shooter))
            return 0; // your own (or a teammate's) cover just stops the bullet
        return DamageTacDevice(e, shooter, (int)MathF.Round(damage), now, DamageKind.Bullet) ? (byte)(ShotHitMachine | 4) : (byte)(ShotHitMachine | 1);
    }

    /// <summary>Damages a device (explosives: Deployable Cover takes double). Returns true if that destroyed it.</summary>
    private bool DamageTacDevice(TacDevice e, Player attacker, int amount, double now, DamageKind kind)
    {
        if (amount <= 0 || !DamageEnabled || !TacDevices.Contains(e))
            return false;
        if (kind == DamageKind.Explosive && e.Kind == ThrowableKind.Cover)
            amount = (int)MathF.Round(amount * TacticalGear.CoverExplosiveMult);
        e.Health -= Math.Max(1, amount);
        e.LastHit = now;
        if (e.Health > 0)
            return false;
        RemoveTacDevice(e, attacker.Id, now);
        return true;
    }

    /// <summary>Explosions (grenades, exploding props, strikes) hurt the attacker's enemies' devices; walls shield them.</summary>
    private void BlastTactical(Player? attacker, Vector3 center, float radius, float maxDamage, double now)
    {
        if (attacker == null || TacDevices.Count == 0)
            return;
        for (var i = TacDevices.Count - 1; i >= 0; i--)
        {
            if (i >= TacDevices.Count)
                continue;
            var e = TacDevices[i];
            if (e.Owner == attacker.Id || !Players.TryGetValue(e.Owner, out var owner) || !StreakIsEnemy(owner, attacker))
                continue;
            DamageTacDevice(e, attacker, (int)MathF.Round(BlastDamage(center, e.Hitbox.Center, radius, maxDamage)), now, DamageKind.Explosive);
        }
    }

    /// <summary>Does a Deployable Cover wall stand between <paramref name="a"/> and <paramref name="b"/>? (Grenade blasts stop at it.)</summary>
    public bool CoverBlocks(Vector3 a, Vector3 b)
    {
        if (TacDevices.Count == 0)
            return false;
        var d = b - a;
        var len = d.Length();
        if (len < 0.01f)
            return false;
        d /= len;
        foreach (var e in TacDevices)
            if (e.Kind == ThrowableKind.Cover && TacticalGear.CoverRay(e.Pos, e.Yaw, a, d, len) != null)
                return true;
        return false;
    }

    /// <summary>ApplyCmd: a stunned player moves slower. Bots are scaled exactly; people's own (already slowed) input is capped.</summary>
    private InputCmd TacticalFilter(Player p, InputCmd cmd)
    {
        var s = TacticalGear.StunNow(_stunS[p.Id], (float)(_tacNow - _stunAt[p.Id]));
        if (s <= 0)
            return cmd;
        var mult = TacticalGear.StunMove(s);
        if (p.Bot != null)
            cmd.SpeedPct = (byte)Math.Clamp((int)MathF.Round(cmd.SpeedPct * mult), 1, 200);
        else
        {
            var fastest = Perks.MoveSpeed(p.Perks, MathF.Max(p.DefFor(p.Primary).Mobility, p.DefFor(p.Secondary).Mobility), false);
            var cap = (byte)Math.Clamp((int)MathF.Ceiling(fastest * mult * 100f) + 1, 1, 200);
            if (cmd.SpeedPct > cap)
                cmd.SpeedPct = cap;
        }
        return cmd;
    }

    /// <summary>Bots: is this bot blinded by a flash right now (no targets)?</summary>
    private bool BotBlinded(Player p, double now) => now < _blindUntil[p.Id];

    /// <summary>Bots: turn-rate multiplier from a stun.</summary>
    private float BotStunTurn(Player p, double now) =>
        TacticalGear.StunLook(TacticalGear.StunNow(_stunS[p.Id], (float)(now - _stunAt[p.Id])));

    // =====================================================================
    // Bots
    // =====================================================================

    /// <summary>Bots use their tactical item when it fits the moment (deterministic rolls, never the shared random sequence).</summary>
    private void BotTactical(Player p, BotBrain b, Player? target, bool visible, float dist, double now)
    {
        if (b.Skill == BotDifficulty.Easy || p.Equipment == ThrowableKind.None || p.EquipmentLeft <= 0 || now < p.NextThrow || !DamageEnabled)
            return;
        var roll = TacticalGear.Roll(p.Id, ServerTick, 0x7AC);
        var lost = now - b.LastSeenTime;
        switch (p.Equipment)
        {
            case ThrowableKind.Flash:
            case ThrowableKind.Stun:
                if (visible && dist is > 5f and < 16f && roll < 0.006)
                    BotUseTactical(p, b, target!.Position, now);
                else if (!visible && lost is > 0.8 and < 3 && Vector3.Distance(p.Position, b.LastSeenAt) is > 6f and < 18f && roll < 0.004)
                    BotUseTactical(p, b, b.LastSeenAt, now);
                break;
            case ThrowableKind.Decoy:
                if (!visible && lost > 4 && roll < 0.0015)
                    BotUseTactical(p, b, p.Position + World.FlatForward(p.Yaw) * 12f, now);
                break;
            case ThrowableKind.MotionSensor:
                if (!visible && roll < 0.002)
                    BotUseTactical(p, b, p.Position + World.FlatForward(p.Yaw) * 9f, now);
                break;
            case ThrowableKind.Trophy:
                if ((visible && dist > 8f && roll < 0.003) || (EnemyGrenadeNear(p, 12f) && roll < 0.3))
                    BotUseTactical(p, b, p.Position + World.FlatForward(p.Yaw), now);
                break;
            case ThrowableKind.Cover:
                if (visible && dist > 12f && roll < 0.004)
                    BotUseTactical(p, b, target!.Position, now);
                break;
            case ThrowableKind.Insertion:
                if (!visible && lost > 4 && roll < 0.002)
                    BotUseTactical(p, b, p.Position, now);
                break;
            case ThrowableKind.Stim:
                if (p.Health < 45 && (!visible || roll < 0.02))
                    BotUseTactical(p, b, p.Position, now);
                break;
        }
    }

    /// <summary>
    /// Hook for other bot logic (harder bots, objectives): bot <paramref name="p"/> uses its tactical item right now,
    /// aimed at <paramref name="at"/> (thrown items are lobbed to land there; placed ones go down facing it; the stim
    /// ignores it). Goes through the normal server rules (counts, cooldown, match phase, modes without equipment).
    /// Returns true if it was used. <see cref="BotWantsTactical"/> says which kind it carries and whether it's ready.
    /// </summary>
    public bool BotUseTacticalNow(Player p, Vector3 at, double now)
    {
        if (p.Bot is not { } b || !BotWantsTactical(p, now))
            return false;
        var before = p.EquipmentLeft;
        BotUseTactical(p, b, at, now);
        return p.EquipmentLeft < before;
    }

    /// <summary>The tactical item bot <paramref name="p"/> could use right now (None if it has none left, or can't use it yet).</summary>
    public ThrowableKind BotReadyTactical(Player p, double now) => BotWantsTactical(p, now) ? p.Equipment : ThrowableKind.None;

    private bool BotWantsTactical(Player p, double now) =>
        p.Alive && TacticalGear.Is(p.Equipment) && p.EquipmentLeft > 0 && now >= p.NextThrow && DamageEnabled && Module.UsesThrowables;

    private bool EnemyGrenadeNear(Player p, float range)
    {
        foreach (var pr in Projectiles.Values)
            if (pr.Owner != p.Id && Vector3.DistanceSquared(pr.Pos, p.Position) < range * range && Players.TryGetValue(pr.Owner, out var t) && IsEnemy(p, t))
                return true;
        return false;
    }

    private void BotUseTactical(Player p, BotBrain b, Vector3 at, double now)
    {
        var kind = p.Equipment;
        var def = Throwables.Get(kind);
        var to = at - p.Position;
        var yaw = to.X * to.X + to.Z * to.Z > 0.01f ? MathF.Atan2(to.X, -to.Z) : p.Yaw;
        var pitch = 0f;
        if (TacticalGear.UseOf(kind) == TacticalUse.Thrown)
        {
            Throwables.AimPitch(def, p.Eye, at, out pitch);
            yaw += (float)(TacticalGear.Roll(p.Id, ServerTick, 0x51) - 0.5) * b.AimError;
        }
        var dir = World.Forward(yaw, pitch);
        var before = p.EquipmentLeft;
        ServerThrow(p, kind, Throwables.LaunchPoint(p.Eye, dir), dir, now);
        if (p.EquipmentLeft < before)
            BotTacticalUses[(int)kind]++;
    }

    // =====================================================================
    // Events (both sides)
    // =====================================================================

    public static void WriteTacEvent(PacketWriter w, TacEventKind kind, byte a, byte b, ushort c, Vector3 pos) =>
        w.Begin(Msg.TacEvent).U8((byte)kind).U8(a).U8(b).U16(c).Vec(pos);

    /// <summary>Server: tells everyone (reliable) and plays it here too.</summary>
    private void EmitTac(TacEventKind kind, byte a, byte b, ushort c, Vector3 pos, double now)
    {
        TacEventsSent++;
        WriteTacEvent(_w, kind, a, b, c, pos);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnTacEvent(kind, a, b, c, pos, now);
    }

    private void OnTacEvent(TacEventKind kind, byte a, byte b, ushort c, Vector3 pos, double now)
    {
        if (!float.IsFinite(pos.X + pos.Y + pos.Z))
            return;
        switch (kind)
        {
            case TacEventKind.Afflict:
                if (a == LocalId)
                    LocalAfflict((ThrowableKind)b, c / 65535f, now);
                break;
            case TacEventKind.Intercept:
            {
                if (!IsServer)
                    Projectiles.Remove(c);
                TacInterceptsSeen++;
                var from = pos + new Vector3(0, 1f, 0);
                if (FindTacDevice(a) is { } e)
                {
                    e.LastHit = now;
                    if (!IsServer)
                        e.Charges = (byte)Math.Max(0, e.Charges - 1);
                    from = e.Pos + new Vector3(0, 0.75f, 0);
                }
                Tracers.Add(new Tracer { From = from, To = pos, Color = new Vector3(0.5f, 0.95f, 1f), Width = 0.025f, Born = now });
                TacBursts.Add(new TacBurst(pos, now, ThrowableKind.Trophy));
                Audio.Effect3D(Sfx.TrophyZap, pos, 0.8f);
                break;
            }
            case TacEventKind.Destroyed:
            {
                var owner = (byte)(c & 0xFF);
                var by = (byte)(c >> 8);
                if (!IsServer && FindTacDevice(a) is { } gone)
                    TacDevices.Remove(gone);
                if (by == byte.MaxValue)
                    break; // ran out, used up or replaced: it just goes
                TacBursts.Add(new TacBurst(pos, now, ThrowableKind.Impact));
                Audio.Effect3D(Sfx.BreakMetal, pos, 0.7f);
                var name = Throwables.Get((ThrowableKind)b).Name;
                string Who(byte id) => id == LocalId ? "You" : NameOf(id);
                var whose = owner == LocalId ? "your" : $"{NameOf(owner)}'s";
                Log($"{Who(by)} destroyed {whose} {name}", by == LocalId || owner == LocalId);
                break;
            }
            case TacEventKind.DecoyBurst:
                if (FindTacDevice(a) is { } decoy)
                {
                    decoy.BurstLeft = Math.Min((int)b, 12);
                    decoy.BurstWeapon = (byte)(c & 0xFF);
                    decoy.ShotInterval = Math.Max(0.04f, (c >> 8) * 4 / 1000f);
                    decoy.NextShot = now;
                }
                break;
            case TacEventKind.Ping:
                OnPing(a, b, pos, now);
                break;
            case TacEventKind.Stim:
                if (a == LocalId)
                {
                    _localStunS = 0;
                    Audio.Effect(Sfx.StimHiss, 0.7f);
                }
                else
                    Audio.Effect3D(Sfx.StimHiss, pos + new Vector3(0, 1.2f, 0), 0.5f);
                break;
            case TacEventKind.Placed:
                Audio.Effect3D(Sfx.DeployDevice, pos, 0.7f);
                if (c == LocalId && (ThrowableKind)b == ThrowableKind.Insertion)
                    Log("Tactical Insertion down: you'll spawn here next", true);
                break;
            case TacEventKind.Inserted:
                if (a == LocalId)
                    Log("Spawned on your Tactical Insertion", true);
                break;
        }
    }

    private void OnPing(byte sensorOwner, byte target, Vector3 pos, double now)
    {
        if (Local is not { } me || !Players.TryGetValue(sensorOwner, out var owner) || (owner != me && AreEnemies(me, owner)))
            return; // only the sensor's owner and their team see its pings
        if (LocalFlashHidesHud(now))
            return;
        TacPingsSeen++;
        Pings[_pingNext] = new TacPing { Target = target, Pos = pos, At = now };
        _pingNext = (_pingNext + 1) % Pings.Length;
        if (now - _lastPingSound > 0.35)
        {
            _lastPingSound = now;
            Audio.Effect(Sfx.MotionPing, 0.35f);
        }
    }

    /// <summary>The local player was flashed or stunned (from the server's event; the host calls it directly).</summary>
    private void LocalAfflict(ThrowableKind kind, float s, double now)
    {
        if (kind == ThrowableKind.Flash)
        {
            if (TacticalGear.FlashAlpha(s, 0) < LocalFlashAlpha(now))
                return;
            _localFlashAt = now;
            _localFlashS = s;
            Audio.Tinnitus(s);
        }
        else if (kind == ThrowableKind.Stun)
        {
            _localStunAt = now;
            _localStunS = s;
        }
    }

    /// <summary>Both sides, from OnDetonated: the burst and the bang of a thrown tactical item.</summary>
    private void OnTacticalDetonated(ThrowableKind kind, byte owner, Vector3 pos, double now)
    {
        switch (kind)
        {
            case ThrowableKind.Flash:
                TacBursts.Add(new TacBurst(pos, now, kind));
                Audio.Effect3D(Sfx.FlashBang, pos, 1f);
                break;
            case ThrowableKind.Stun:
                TacBursts.Add(new TacBurst(pos, now, kind));
                Audio.Effect3D(Sfx.StunPop, pos, 0.9f);
                break;
        }
        _ = owner;
    }

    // =====================================================================
    // Local effects (both sides, every frame)
    // =====================================================================

    /// <summary>White-out opacity for the local player right now (0..1).</summary>
    public float LocalFlashAlpha(double now) => TacticalGear.FlashAlpha(_localFlashS, (float)(now - _localFlashAt));

    /// <summary>How stunned the local player is right now (0..1).</summary>
    public float LocalStun(double now) => TacticalGear.StunNow(_localStunS, (float)(now - _localStunAt));

    /// <summary>Flashed hard enough that name tags, recon and pings are washed out.</summary>
    public bool LocalFlashHidesHud(double now) => LocalFlashAlpha(now) > 0.5f;

    /// <summary>Mouse look multiplier (a stun slows turning).</summary>
    public float TacticalLookMult => TacticalGear.StunLook(LocalStun(_tacLocalNow));

    private float LocalStunMove() => TacticalGear.StunMove(LocalStun(_tacLocalNow));

    private float TacticalAdsMult(double now) => TacticalGear.StunAds(LocalStun(now));

    private void UpdateTacticalEffects(double now, float dt)
    {
        _tacLocalNow = now;
        Audio.Deafen = TacticalGear.FlashDeafen(_localFlashS, (float)(now - _localFlashAt));
        for (var i = TacBursts.Count - 1; i >= 0; i--)
            if (now - TacBursts[i].Born > 0.8)
                TacBursts.RemoveAt(i);
        for (var i = TacDevices.Count - 1; i >= 0; i--)
        {
            var e = TacDevices[i];
            if (!IsServer && now - e.LastUpdate > 1.0)
            {
                TacDevices.RemoveAt(i); // updates stopped (the empty one got lost)
                continue;
            }
            if (e.Kind == ThrowableKind.Decoy && e.BurstLeft > 0 && now >= e.NextShot)
            {
                e.BurstLeft--;
                e.NextShot = Math.Max(e.NextShot + e.ShotInterval, now - 0.1);
                e.LastHit = now;
                TacDecoyShotsPlayed++;
                Audio.Shot(Weapons.Get(e.BurstWeapon), e.Pos + new Vector3(0, 0.3f, 0)); // the stock gun's sound: no tracer, no hit
            }
            else if (e.Kind == ThrowableKind.MotionSensor && now >= e.NextShot)
            {
                e.NextShot = now + 1.5;
                Audio.Effect3D(Sfx.SensorBeep, e.Pos, 0.35f); // anyone can hear it and hunt it down
            }
        }
        _ = dt;
    }

    /// <summary>Both sides: a new match (or a reset) clears every device and effect.</summary>
    private void ClearTactical()
    {
        TacDevices.Clear();
        TacBursts.Clear();
        Array.Clear(Pings);
        Array.Clear(_flashS);
        Array.Clear(_stunS);
        Array.Clear(_blindUntil);
        Array.Clear(_stimUntil);
        _localFlashS = _localStunS = 0;
        Audio.Deafen = 0;
    }

    // =====================================================================
    // Replication
    // =====================================================================

    /// <summary>Server -> all: every device, with each snapshot while any exist (one empty update after the last goes).</summary>
    private void SendTactical()
    {
        if (!IsServer || Net.IsOffline || (TacDevices.Count == 0 && !_tacSent))
            return;
        _tacSent = TacDevices.Count > 0;
        WriteTacState(_w, ServerTick, TacDevices);
        Net.Broadcast(_w.Span, PacketReliability.Unreliable);
    }

    public static void WriteTacState(PacketWriter w, uint tick, List<TacDevice> devices)
    {
        w.Begin(Msg.TacState).U32(tick).U8((byte)Math.Min(devices.Count, MaxTacDevices));
        for (var i = 0; i < devices.Count && i < MaxTacDevices; i++)
            WriteTacDevice(w, devices[i]);
    }

    /// <summary>One device: id, kind, owner, position, yaw, health (0..255 of its max) and charges.</summary>
    public static void WriteTacDevice(PacketWriter w, TacDevice e) =>
        w.U8(e.Id).U8((byte)e.Kind).U8(e.Owner).Vec(e.Pos)
            .U16((ushort)Math.Clamp(World.WrapAngle(e.Yaw) / MathF.Tau * 65536f + 32768f, 0, 65535))
            .U8((byte)Math.Clamp(e.Health * 255 / Math.Max(1, e.MaxHealth), 0, 255))
            .U8(e.Charges);

    /// <summary>Client: applies a <see cref="Msg.TacState"/> body (after the id). Devices missing from it are gone.</summary>
    private void ReadTacState(ref PacketReader r, double now)
    {
        var tick = r.U32();
        if (tick < _lastTacTick)
            return; // out of order
        _lastTacTick = tick;
        int n = r.U8();
        Span<ulong> seen = stackalloc ulong[4];
        for (var i = 0; i < n && r.Has(TacDevice.NetBytes); i++)
        {
            var id = r.U8();
            var kind = (ThrowableKind)r.U8();
            var owner = r.U8();
            var pos = r.Vec();
            var yaw = (r.U16() - 32768f) / 65536f * MathF.Tau;
            var health = r.U8();
            var charges = r.U8();
            if (!TacticalGear.IsDevice(kind) || !float.IsFinite(pos.X + pos.Y + pos.Z))
                continue;
            seen[id >> 6] |= 1UL << (id & 63);
            var e = FindTacDevice(id);
            if (e == null || e.Kind != kind)
            {
                if (e != null)
                    TacDevices.Remove(e);
                e = new TacDevice { Id = id, Kind = kind, Born = now, NextShot = now + 0.5 };
                TacDevices.Add(e);
            }
            e.Owner = owner;
            e.Pos = pos;
            e.Yaw = yaw;
            if (health * e.MaxHealth / 255 < e.Health)
                e.LastHit = now;
            e.Health = health * e.MaxHealth / 255;
            e.Charges = charges;
            e.LastUpdate = now;
        }
        for (var i = TacDevices.Count - 1; i >= 0; i--)
        {
            var id = TacDevices[i].Id;
            if ((seen[id >> 6] & (1UL << (id & 63))) == 0)
                TacDevices.RemoveAt(i);
        }
    }

    private void OnTacticalPacket(byte id, ref PacketReader r, double now)
    {
        switch (id)
        {
            case (byte)Msg.TacState when !IsServer && r.Has(5):
                ReadTacState(ref r, now);
                break;
            case (byte)Msg.TacEvent when !IsServer && r.Has(TacEventBytes):
            {
                var kind = (TacEventKind)r.U8();
                var a = r.U8();
                var b = r.U8();
                var c = r.U16();
                var pos = r.Vec();
                OnTacEvent(kind, a, b, c, pos, now);
                break;
            }
        }
    }

    // =====================================================================
    // Self-test access
    // =====================================================================

    public void SelfTestTacticalTick(double now)
    {
        ServerTick++;
        ServerTickTactical(now);
    }

    public void SelfTestUseTactical(Player p, ThrowableKind kind, Vector3 dir, double now) => ServerUseTactical(p, kind, dir, now);

    public void SelfTestTacticalDetonate(Player owner, ThrowableKind kind, Vector3 pos, double now) =>
        ServerTacticalDetonate(new Projectile { Kind = kind, Owner = owner.Id, Pos = pos }, owner, now);

    public (float Strength, double At) SelfTestFlashOf(Player p) => (_flashS[p.Id], _flashAt[p.Id]);

    public (float Strength, double At) SelfTestStunOf(Player p) => (_stunS[p.Id], _stunAt[p.Id]);

    public bool SelfTestBotBlinded(Player p, double now) => BotBlinded(p, now);

    public byte SelfTestShotTactical(TacDevice e, Player shooter, float damage, double now) => ShotTactical(e, shooter, damage, now);

    public TacDevice? SelfTestTacticalRay(Vector3 origin, Vector3 dir, float dist, Player shooter) => TacticalRaycast(origin, dir, ref dist, shooter);

    public void SelfTestBlastTactical(Player attacker, Vector3 center, float radius, float damage, double now) => BlastTactical(attacker, center, radius, damage, now);

    public Vector3 SelfTestTacticalSpawn(Player p, Vector3 picked, double now) => TacticalSpawnPoint(p, picked, now);

    public InputCmd SelfTestTacticalFilter(Player p, InputCmd cmd, double now)
    {
        _tacNow = now;
        return TacticalFilter(p, cmd);
    }

    public void SelfTestBotTactical(Player p, Player? target, bool visible, float dist, double now) => BotTactical(p, p.Bot!, target, visible, dist, now);

    public void SelfTestLocalAfflict(ThrowableKind kind, float s, double now) => LocalAfflict(kind, s, now);

    public void SelfTestTacticalPacket(ReadOnlySpan<byte> packet, double now)
    {
        var r = new PacketReader(packet[1..]);
        OnTacticalPacket(packet[0], ref r, now);
    }

    public void SelfTestUpdateTacticalEffects(double now, float dt) => UpdateTacticalEffects(now, dt);

    public void SelfTestClearTactical() => ClearTactical();

    /// <summary>One server tick with the match over (devices must go).</summary>
    public void SelfTestEndMatchTactical(double now)
    {
        var phase = Phase;
        Phase = MatchPhase.Ended;
        ServerTickTactical(now);
        Phase = phase;
    }

    /// <summary>
    /// Smoke test (host only): lays out one of each device in a fan in front of <paramref name="p"/> (looking along
    /// <paramref name="yaw"/>), each on the ground and clear of walls where it can be. Returns how many went down.
    /// </summary>
    public int SmokePlaceTactical(Player p, float yaw, double now)
    {
        if (!IsServer)
            return 0;
        var fwd = World.FlatForward(yaw);
        var right = World.FlatRight(yaw);
        var free = World.RaycastWorld(p.Eye, fwd, 12f);
        var reach = Math.Clamp(free - 1f, 2.5f, 7f);
        (ThrowableKind Kind, float Ahead, float Side)[] layout =
        [
            (ThrowableKind.Cover, reach, 0f), (ThrowableKind.Trophy, reach * 0.6f, -1.6f), (ThrowableKind.MotionSensor, reach * 0.6f, 1.6f),
            (ThrowableKind.Insertion, reach * 0.4f, -0.6f), (ThrowableKind.Decoy, reach * 0.4f, 0.8f),
        ];
        var placed = 0;
        foreach (var (kind, ahead, side) in layout)
        {
            var spot = p.Position + fwd * ahead + right * side;
            var down = World.Query.Raycast(spot + new Vector3(0, 1.5f, 0), -Vector3.UnitY, 3f, QueryFilter.All);
            if (down < 2.99f)
                spot.Y += 1.5f - down;
            var e = NewTacDevice(p, kind, spot, yaw + MathF.PI, now); // facing back at the camera
            e.Until = now + 30;
            placed++;
        }
        return placed;
    }
}
