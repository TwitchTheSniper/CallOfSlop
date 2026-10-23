using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Launchers on the wire and on clients. A spawn message (<see cref="RocketSpawnBytes"/> bytes) starts a projectile
/// everywhere; clients fly it themselves with the same deterministic steps (<see cref="RocketFlight"/>), flown forward
/// by the spawn's age plus their own latency. Guided and stuck ones get a 29-byte correction with every snapshot while
/// they exist (<see cref="Msg.RocketSync"/>), and the explode message (<see cref="RocketBoomBytes"/> bytes) ends it with
/// the server's result. The shooter sees their own rocket at once: a prediction that the server's copy takes over
/// from (matched by the shot's seed) without a jump.
/// </summary>
public sealed partial class Game
{
    public const int RocketSpawnBytes = 38, RocketSyncBytes = 29, RocketBoomBytes = 25;

    /// <summary>Guided rockets per sync message at most (keeps it inside one datagram).</summary>
    private const int MaxSyncPerPacket = 36;

    /// <summary>The local player's own rockets, flown from the trigger pull until the server's spawn arrives.</summary>
    private readonly List<Rocket> _predictedRockets = new();

    /// <summary>Cluster bomblets' visual blasts still to come (clients and host).</summary>
    private readonly List<(double At, Vector3 Pos, float Radius)> _rocketFx = new();
    private uint _lastRocketSyncTick;

    // =====================================================================
    // Packets (static writers so the self-test can round-trip them)
    // =====================================================================

    public static void WriteRocketSpawn(PacketWriter w, Rocket r, byte age) =>
        w.Begin(Msg.RocketSpawn).U16(r.Id).U8(r.Owner).U8(r.WeaponId).U24(r.Kit).U8(r.Target).U8((byte)(r.Wire ? 1 : 0))
            .U32(r.Seed).Vec(r.Pos).Vec(r.Vel).U8(age);

    /// <summary>Reads a spawn into a fresh rocket (its <see cref="Rocket.Def"/> resolved from the weapon and kit). Returns the age byte.</summary>
    public static byte ReadRocketSpawn(ref PacketReader r, Rocket into)
    {
        into.Id = r.U16();
        into.Owner = r.U8();
        into.WeaponId = r.U8();
        into.Kit = r.U24();
        into.Target = r.U8();
        into.Wire = (r.U8() & 1) != 0;
        into.Seed = r.U32();
        into.Pos = r.Vec();
        into.Vel = r.Vec();
        into.Def = LauncherDef(into.WeaponId, into.Kit);
        into.BouncesLeft = into.Def.LauncherBounces;
        return r.U8();
    }

    public static void WriteRocketBoom(PacketWriter w, Rocket r, RocketEnd end, Vector3 pos, byte flags) =>
        w.Begin(Msg.RocketBoom).U16(r.Id).U8(r.Owner).U8(r.WeaponId).U24(r.Kit).U8((byte)end).Vec(pos).U8(flags).U32(r.Seed);

    public static (ushort Id, byte Owner, byte Weapon, uint Kit, RocketEnd End, Vector3 Pos, byte Flags, uint Seed) ReadRocketBoom(ref PacketReader r) =>
        (r.U16(), r.U8(), r.U8(), r.U24(), (RocketEnd)r.U8(), r.Vec(), r.U8(), r.U32());

    /// <summary>One guided or stuck rocket in a sync message.</summary>
    public static void WriteRocketSyncEntry(PacketWriter w, Rocket r) =>
        w.U16(r.Id).U8((byte)((r.Stuck ? 1 : 0) | (r.Decoyed ? 2 : 0) | (r.Armed ? 4 : 0))).U8(r.Target).U8(r.StuckTo).Vec(r.Pos).Vec(r.Stuck ? r.StuckOffset : r.Vel);

    /// <summary>Server -> all, with snapshots: the rockets clients can't fly exactly on their own (guided, decoyed, stuck).</summary>
    private void SendRocketSync()
    {
        if (!IsServer || Net.IsOffline)
            return;
        var n = 0;
        foreach (var r in Rockets.Values)
            if (NeedsSync(r))
                n++;
        if (n == 0)
            return;
        n = Math.Min(n, MaxSyncPerPacket);
        _w.Begin(Msg.RocketSync).U32(ServerTick).U8((byte)n);
        var written = 0;
        foreach (var r in Rockets.Values)
        {
            if (!NeedsSync(r) || written >= n)
                continue;
            WriteRocketSyncEntry(_w, r);
            written++;
        }
        Net.Broadcast(_w.Span, PacketReliability.Unreliable);
    }

    private static bool NeedsSync(Rocket r) => r.Stuck || r.Wire || r.Target != byte.MaxValue || r.Decoyed;

    private void OnLauncherPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.RocketSpawn when !IsServer && r.Has(RocketSpawnBytes):
            {
                var rk = NewRocket();
                var age = ReadRocketSpawn(ref r, rk);
                if (!rk.Def.IsLauncher || Rockets.ContainsKey(rk.Id))
                {
                    FreeRocket(rk);
                    break;
                }
                rk.Born = now;
                // Fly it forward to the server's present: its age when sent, plus our own latency.
                var lead = Math.Min(age + LeadTicksClient(), MaxLeadTicks * 3);
                for (var i = 0; i < lead && !rk.Hidden; i++)
                    ClientStepRocket(rk);
                rk.PrevPos = rk.Pos;
                rk.RenderPos = rk.Pos;
                Rockets[rk.Id] = rk;
                if (rk.Owner == LocalId && TakePrediction(rk.Seed) is { } mine)
                {
                    rk.RenderOffset = mine.RenderPos - rk.Pos; // the prediction hands over without a jump
                    for (var i = 0; i < mine.TrailCount; i++)
                        rk.PushTrail(mine.TrailAt(mine.TrailCount - 1 - i));
                    FreeRocket(mine);
                }
                else if (rk.Owner != LocalId)
                    Audio.Shot(rk.Def, rk.Pos);
                break;
            }
            case (byte)Msg.RocketSync when !IsServer && r.Has(5):
            {
                var tick = r.U32();
                if (tick < _lastRocketSyncTick)
                    break; // out of order
                _lastRocketSyncTick = tick;
                int n = r.U8();
                var lead = LeadTicksClient();
                for (var i = 0; i < n && r.Has(RocketSyncBytes); i++)
                {
                    var rid = r.U16();
                    var state = r.U8();
                    var target = r.U8();
                    var stuckTo = r.U8();
                    var pos = r.Vec();
                    var vel = r.Vec();
                    if (!Rockets.TryGetValue(rid, out var rk) || rk.Hidden && (state & 1) == 0)
                        continue;
                    var before = rk.RenderPos;
                    rk.Target = target;
                    rk.Decoyed = (state & 2) != 0;
                    rk.Armed = (state & 4) != 0;
                    if ((state & 1) != 0)
                    {
                        rk.Stuck = true;
                        rk.Hidden = false;
                        rk.StuckTo = stuckTo;
                        rk.StuckOffset = vel;
                        rk.Pos = stuckTo != byte.MaxValue && Players.TryGetValue(stuckTo, out var host) ? host.RenderPosition + vel : pos;
                        rk.Vel = Vector3.Zero;
                    }
                    else
                    {
                        rk.Pos = pos;
                        rk.Vel = vel;
                        if (rk.Decoyed)
                            rk.Decoy = pos + Vector3.Normalize(vel + new Vector3(0, 1e-3f, 0)) * 30f + new Vector3(0, -12f, 0);
                        for (var k = 0; k < lead && !rk.Hidden; k++)
                            ClientStepRocket(rk);
                    }
                    rk.PrevPos = rk.Pos;
                    rk.RenderOffset = before - rk.Pos;
                    if (rk.RenderOffset.LengthSquared() > 25f)
                        rk.RenderOffset = Vector3.Zero; // far off: snap
                    rk.LastSync = now;
                }
                break;
            }
            case (byte)Msg.RocketBoom when !IsServer && r.Has(RocketBoomBytes):
            {
                var b = ReadRocketBoom(ref r);
                if (b.End != RocketEnd.Flares && Rockets.Remove(b.Id, out var gone))
                    FreeRocket(gone);
                OnRocketBoom(b.Id, b.Owner, b.Weapon, b.Kit, b.End, b.Pos, b.Flags, b.Seed, now);
                break;
            }
            case (byte)Msg.RocketDetonate when IsServer:
                if (_clients.TryGetValue(sender.G, out var c) && Players.TryGetValue(c.Id, out var p))
                    ServerDetonateStickies(p, now);
                break;
        }
    }

    /// <summary>Client: our one-way latency in ticks (what a server message is behind the server's present).</summary>
    private int LeadTicksClient() =>
        Net.IsOffline || IsServer ? 0 : Math.Clamp((int)MathF.Round(Net.PingTo(Net.ServerGuid) * 0.5f / 1000f * World.TickRate), 0, MaxLeadTicks);

    private Rocket? TakePrediction(uint seed)
    {
        for (var i = 0; i < _predictedRockets.Count; i++)
            if (_predictedRockets[i].Seed == seed)
            {
                var r = _predictedRockets[i];
                _predictedRockets.RemoveAt(i);
                return r;
            }
        return null;
    }

    // =====================================================================
    // Both sides: the explode message
    // =====================================================================

    /// <summary>A projectile went off (or fizzled, or a target popped flares): blast flash, sound, scorch, shake, bomblets.</summary>
    private void OnRocketBoom(ushort id, byte owner, byte weapon, uint kit, RocketEnd end, Vector3 pos, byte flags, uint seed, double now)
    {
        var def = LauncherDef(weapon, kit);
        switch (end)
        {
            case RocketEnd.Explode:
            {
                var radius = def.PlayerBlastRadius * (Players.TryGetValue(owner, out var shooter) ? Perks.BlastRadiusMult(shooter.Perks) : 1f);
                BlastFx(pos, radius, MathF.Min(1f, 0.55f + def.Damage / 300f), owner, now);
                Ragdolls.Blast(pos + new Vector3(0, 0.15f, 0), radius, Knockback.LauncherStrength(def), now); // corpses fly (Source/Render/Ragdolls.cs)
                PropDebris.Push(pos + new Vector3(0, 0.15f, 0), radius, Knockback.LauncherStrength(def)); // and loose debris
                FrenzyFx.OnLauncherBlast(this, shooter, pos, radius, now); // a frenzied launcher's shockwave (FrenzyFx.Launcher.cs)
                for (var i = 0; i < def.LauncherCluster; i++)
                    _rocketFx.Add((now + RocketFlight.ClusterDelay(seed, i), RocketFlight.ClusterPoint(pos, def.LauncherRadius, seed, i, def.LauncherCluster),
                        radius * RocketFlight.ClusterRadiusMult));
                break;
            }
            case RocketEnd.Dud:
                Blasts.Add(new Blast(pos, now, 0.7f, ThrowableKind.Impact));
                Audio.Effect3D(Sfx.HitMetal, pos, 0.7f);
                Audio.Effect3D(Sfx.Bounce, pos, 0.6f);
                break;
            case RocketEnd.Flares:
                Audio.Effect3D(Sfx.FlarePop, pos, 0.9f);
                FlareFx(pos, now);
                break;
        }
        if (!IsServer && owner == LocalId && flags != 0)
            OnOwnShotConfirmed(flags, now);
    }

    private void BlastFx(Vector3 pos, float radius, float gain, byte owner, double now)
    {
        Blasts.Add(new Blast(pos, now, radius, ThrowableKind.Frag));
        Audio.Effect3D(Sfx.Explosion, pos, gain);
        ImpactDecals.OnBlast(World.Map, pos, owner, now); // a scorch on the nearest surface (Source/Decals)
        if (Local is { } me)
        {
            var d = Vector3.Distance(me.RenderEye, pos);
            Shake = MathF.Max(Shake, Math.Clamp(1.1f + radius * 0.05f - d / 18f, 0f, 1f));
        }
    }

    /// <summary>Flares: a shower of hot sparks falling away from the target.</summary>
    private static void FlareFx(Vector3 pos, double now)
    {
        var s = (uint)(now * 1000);
        for (var i = 0; i < 18; i++)
        {
            s = s * 1664525u + 1013904223u;
            var a = (s >> 8) / 16777216f * MathF.Tau;
            var vel = new Vector3(MathF.Cos(a) * 6f, -2f - (s & 7), MathF.Sin(a) * 6f);
            FrenzyParticles.Spark(pos, vel, new Vector3(1f, 0.75f, 0.35f), 0.06f, 1.4f, 4f, 0.8f);
        }
    }

    // =====================================================================
    // Client: flying the copies
    // =====================================================================

    /// <summary>Client tick: fly every copy and our own predictions (from ClientTickStep).</summary>
    private void ClientTickRockets()
    {
        if (Rockets.Count > 0)
        {
            _rocketEnded.Clear();
            foreach (var r in Rockets.Values)
            {
                ClientStepRocket(r);
                if (r.Age * World.TickDt > r.Def.LauncherFuse + 3f)
                    _rocketEnded.Add(r); // its explode message never came: drop it
            }
            foreach (var r in _rocketEnded)
            {
                Rockets.Remove(r.Id);
                FreeRocket(r);
            }
            _rocketEnded.Clear();
        }
        for (var i = _predictedRockets.Count - 1; i >= 0; i--)
        {
            var r = _predictedRockets[i];
            ClientStepRocket(r);
            if (r.Age > World.TickRate * 2)
            {
                _predictedRockets.RemoveAt(i); // the server never confirmed it (refused, or the spawn was lost)
                FreeRocket(r);
            }
        }
    }

    /// <summary>
    /// Client: one tick of a copy (or a prediction). It flies like the server's; when it touches the map, a player or a
    /// machine as this client sees them it stops there, hidden, and waits for the server's explode message.
    /// </summary>
    private void ClientStepRocket(Rocket r)
    {
        r.Age++;
        r.PrevPos = r.Pos;
        if (r.Hidden)
            return;
        if (r.Stuck)
        {
            if (r.StuckTo != byte.MaxValue && Players.TryGetValue(r.StuckTo, out var host))
                r.Pos = host.RenderPosition + r.StuckOffset;
            return;
        }
        var start = r.Pos;
        var contact = RocketFlight.Step(r, RocketSteer(r, server: false), out var hit);
        var seg = r.Pos - start;
        var len = seg.Length();
        if (len > 1e-5f)
        {
            var dir = seg / len;
            var best = len;
            Player? victim = null;
            var stop = false;
            foreach (var o in Players.Values)
            {
                if (!o.Alive || o.Id == r.Owner && r.Age < 20)
                    continue;
                var feet = o.Id == LocalId ? o.Position : o.RenderPosition;
                if (World.Hitbox(feet, o.Id == LocalId ? o.Sim.Stance : o.RenderStance, o.RenderYaw).Raycast(start, dir, best) is { } t && t < best)
                {
                    best = t;
                    victim = o;
                    stop = true;
                }
            }
            foreach (var e in StreakEntities.Values)
            {
                if (e.Owner == r.Owner)
                    continue;
                var box = e.Hitbox;
                if (new Box(box.Center - e.Pos + e.RenderPos, box.Half).Raycast(start, dir, best) is { } t && t < best)
                {
                    best = t;
                    victim = null;
                    stop = true;
                }
            }
            if (stop)
            {
                r.Pos = start + dir * best;
                if (r.Def.LauncherProjectile == LauncherProjectile.Sticky && victim != null)
                {
                    r.Stuck = true;
                    r.StuckTo = victim.Id;
                    r.StuckOffset = r.Pos - victim.RenderPosition;
                }
                else
                    r.Hidden = true;
                return;
            }
        }
        if (contact == RocketContact.World)
        {
            if (r.Def.LauncherProjectile == LauncherProjectile.Sticky)
            {
                r.Stuck = true;
                r.Pos = hit.Point + hit.Normal * RocketFlight.Radius;
            }
            else
                r.Hidden = true;
        }
        else if (contact == RocketContact.OutOfBounds)
            r.Hidden = true;
    }

    // =====================================================================
    // Local player: firing and detonating
    // =====================================================================

    /// <summary>
    /// From <see cref="FireOnce"/>: a launcher shot by the local player. The host fires it straight away; a client
    /// predicts its own rocket (drawn at once) and sends the Fire packet with the lock-on target as a trailing byte.
    /// </summary>
    private void LauncherFireLocal(Player me, WeaponDef def, Vector3 origin, Vector3 dir, float spread, uint seed, double now)
    {
        var claim = Locked ? LockTarget : byte.MaxValue;
        ResetLock();
        if (IsServer)
        {
            ServerFireLauncher(me, def, origin, dir, spread, seed, claim, now);
            return;
        }
        var launch = RocketFlight.LaunchDirection(dir, Math.Clamp(spread, def.MinSpreadRad, def.MaxSpreadRad), seed);
        var r = NewRocket();
        r.Predicted = true;
        r.Owner = LocalId;
        r.WeaponId = def.Id;
        r.Kit = def.Kit;
        r.Def = def;
        r.Seed = seed;
        r.Pos = r.PrevPos = r.RenderPos = RocketFlight.LaunchPoint(origin, launch);
        r.Vel = RocketFlight.LaunchVelocity(def, launch);
        r.BouncesLeft = def.LauncherBounces;
        r.Wire = def.LauncherGuidance == LauncherGuidance.Wire;
        r.Target = claim;
        r.Born = now;
        _predictedRockets.Add(r);
        var tickBase = (uint)Math.Max(0, Math.Floor(RenderTick));
        _w.Begin(Msg.Fire).U8(def.Id).Vec(origin).Vec(dir).F32(spread).U32(seed).U32(tickBase).F32((float)(RenderTick - tickBase)).U8(claim); // protocol 28: + lock target
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Your sticky charges out right now (as this client sees them).</summary>
    public int LocalStickiesOut()
    {
        var n = 0;
        foreach (var r in Rockets.Values)
            if (r.Owner == LocalId && r.Def.LauncherProjectile == LauncherProjectile.Sticky)
                n++;
        foreach (var r in _predictedRockets)
            if (r.Def.LauncherProjectile == LauncherProjectile.Sticky)
                n++;
        return n;
    }

    /// <summary>
    /// Called first by <see cref="UpdateTrigger"/> (after the streak and cart checks): with a sticky launcher in hand and
    /// charges out, pulling the trigger while aiming sets them all off (and never fires). Returns true when it used the trigger.
    /// </summary>
    private bool LauncherTrigger(double now, bool pressed)
    {
        var def = Active.Def;
        if (!def.IsLauncher || def.LauncherProjectile != LauncherProjectile.Sticky || !AimHeld || !pressed)
            return false;
        if (LocalStickiesOut() == 0)
            return false;
        if (IsServer)
        {
            if (Local is { } me)
                ServerDetonateStickies(me, now);
        }
        else
        {
            _w.Begin(Msg.RocketDetonate);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
        Audio.Effect(Sfx.LockTone, 0.6f); // the detonator's click
        return true;
    }

    /// <summary>Makes the local launcher slots carry their reserve rounds (from SyncLocalSlots: launchers aren't endless).</summary>
    private void LauncherReserves()
    {
        foreach (var s in Slots)
            if (s.Def.IsLauncher && s.Reserve < 0)
                s.Reserve = s.Def.LauncherReserve;
    }

    // =====================================================================
    // Every frame: render positions, trails, motor sounds, the lock-on
    // =====================================================================

    /// <summary>Per frame (after the ticks): where each projectile is drawn, its smoke trail, its motor, the lock-on, bomblet flashes.</summary>
    private void UpdateLauncherVisuals(double now, float dt)
    {
        for (var i = _rocketFx.Count - 1; i >= 0; i--)
        {
            var fx = _rocketFx[i];
            if (now < fx.At)
                continue;
            _rocketFx.RemoveAt(i);
            BlastFx(fx.Pos, fx.Radius, 0.6f, byte.MaxValue, now);
        }
        var ease = MathF.Exp(-9f * dt);
        foreach (var r in Rockets.Values)
            VisualRocket(r, now, ease);
        foreach (var r in _predictedRockets)
            VisualRocket(r, now, ease);
        UpdateLauncherLock(now, dt); // GameLaunchersLock.cs
    }

    private void VisualRocket(Rocket r, double now, float ease)
    {
        r.RenderOffset *= ease;
        r.RenderPos = (r.Stuck ? r.Pos : Vector3.Lerp(r.PrevPos, r.Pos, _alpha)) + r.RenderOffset;
        if (r.Hidden || r.Stuck)
            return;
        if (now >= r.NextTrail)
        {
            r.NextTrail = now + 0.03;
            r.PushTrail(r.RenderPos);
            if (r.Def.LauncherProjectile == LauncherProjectile.Rocket && Vector3.DistanceSquared(r.RenderPos, Hud.Camera.Eye) < 150f * 150f)
            {
                // Motor sparks streaming back (the frenzy effects' instanced particle pool: one draw for all of them).
                var back = -Vector3.Normalize(r.Vel + new Vector3(0, 1e-4f, 0));
                FrenzyParticles.Spark(r.RenderPos + back * 0.3f, back * 6f + new Vector3(0, 0.5f, 0), new Vector3(1f, 0.6f, 0.2f), 0.05f, 0.18f, 0f, 2f);
            }
        }
        if (r.Def.LauncherProjectile == LauncherProjectile.Rocket && now >= r.NextMotorSound)
        {
            r.NextMotorSound = now + 0.3;
            Audio.Effect3DSmall(Sfx.RocketMotor, r.RenderPos, 0.55f, 70f);
        }
    }

    /// <summary>The local player's predicted rockets (drawn like the others until the server's copy takes over).</summary>
    public IReadOnlyList<Rocket> PredictedRockets => _predictedRockets;

    /// <summary>Self-test: every rocket this side knows of (server copies, client copies and predictions).</summary>
    public int SelfTestRocketCount => Rockets.Count + _predictedRockets.Count;

    /// <summary>Self-test: a client tick of every copy and prediction (no network).</summary>
    public void SelfTestClientRocketTick() => ClientTickRockets();

    /// <summary>Self-test: the per-frame visual update (render positions, trails, sounds, lock-on).</summary>
    public void SelfTestLauncherVisuals(double now, float dt) => UpdateLauncherVisuals(now, dt);

    /// <summary>Self-test: flies a lone copy of a rocket one client tick (no players or machines in the way).</summary>
    public void SelfTestClientStep(Rocket r) => ClientStepRocket(r);
}
