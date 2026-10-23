using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Co-op enemy AI (server; docs/pve/DESIGN.md "AI"): a staggered think with level of detail (10 Hz near players, 3 Hz
/// mid range, 1 Hz far), nav-graph paths with a shared A* budget, cover on low-openness nodes, bursts of hitscan fire
/// with spread at where players are now (no lag compensation: the shooter is on the server), grenades, an officer's aura,
/// unsticking. Near players an enemy moves through the shared player physics; far away it slides along its path.
/// </summary>
public sealed partial class Game
{
    private readonly NavSearch _pveSearch = new();

    /// <summary>Within this distance of a player an enemy moves through the player physics; beyond, along its path without collision.</summary>
    public const float PvePhysicsRange = 90f;

    private readonly Vector3[] _pveOfficers = new Vector3[16];
    private int _pveOfficerCount;
    private double _pveNextGrenadeAny;

    private void RefreshOfficers()
    {
        _pveOfficerCount = 0;
        foreach (var e in Enemies)
            if (e.Alive && e.Arch == PveArch.Officer && _pveOfficerCount < _pveOfficers.Length)
                _pveOfficers[_pveOfficerCount++] = e.Position;
    }

    private bool Buffed(PveEnemy e)
    {
        const float r2 = PveArchetypes.OfficerAuraRadius * PveArchetypes.OfficerAuraRadius;
        for (var i = 0; i < _pveOfficerCount; i++)
            if (Vector3.DistanceSquared(_pveOfficers[i], e.Position) < r2)
                return true;
        return false;
    }

    /// <summary>The point the director sends idle enemies to (the stage's focus, or the nearest player when hunting).</summary>
    public Vector3 PveAttackPoint;

    /// <summary>Enemies hunt the nearest player (true) or converge on <see cref="PveAttackPoint"/> (defend: the zone).</summary>
    public bool PveHuntPlayers = true;

    private static Vector3 EnemyEye(PveEnemy e) => e.Position + new Vector3(0, e.Crouched ? 1.05f : 1.55f, 0);

    private void TickEnemy(PveEnemy e, double now)
    {
        var nearSq = float.MaxValue;
        foreach (var p in Players.Values)
            if (p.Alive)
                nearSq = MathF.Min(nearSq, Vector3.DistanceSquared(p.Position, e.Position));
        e.NearPhysics = nearSq < PvePhysicsRange * PvePhysicsRange;
        if (nearSq < 40f * 40f)
            e.LastSeenByPlayer = now; // close to someone: as good as seen (never recycled from under their nose)
        if (now >= e.NextThink)
        {
            AllocProfile.Mark("coop: think");
            ThinkEnemy(e, now);
            e.NextThink = now + (nearSq < 50f * 50f ? 0.1 : nearSq < 100f * 100f ? 0.33 : 1.0) + PveRand() * 0.03;
        }
        AllocProfile.Mark("coop: move");
        MoveEnemy(e, now);
        AllocProfile.Mark("coop: fire");
        FireEnemy(e, now);
        AllocProfile.Mark("coop: enemies");
    }

    // ---- Thinking ----------------------------------------------------------------------------------------------

    private void ThinkEnemy(PveEnemy e, double now)
    {
        var def = e.Def;
        // The target: the current one while it's still in sight, else the nearest visible living player in range (up to 3 tried).
        var eye = EnemyEye(e);
        Player? target = null;
        var tried = 0;
        if (Players.TryGetValue(e.Target, out var cur) && cur.Alive && Vector3.Distance(cur.Position, e.Position) <= def.MaxRange)
        {
            if (ClearRay(eye, cur.Chest))
                target = cur;
            else
                _pveTriedIds[tried++] = cur.Id;
        }
        while (target == null && tried < _pveTriedIds.Length)
        {
            Player? best = null;
            var bestD = float.MaxValue;
            foreach (var p in Players.Values)
            {
                if (!p.Alive || PveTried(p, tried))
                    continue;
                var d = Vector3.Distance(p.Position, e.Position);
                if (d > def.MaxRange || d >= bestD)
                    continue;
                best = p;
                bestD = d;
            }
            if (best == null)
                break;
            _pveTriedIds[tried++] = best.Id;
            if (ClearRay(eye, best.Chest))
                target = best;
        }
        if (target != null)
        {
            if (target.Id != e.Target || !e.TargetVisible)
                e.TargetSince = now; // the reaction time starts now
            e.Target = target.Id;
            e.TargetVisible = true;
            e.LastSeen = target.Position;
            e.LastSawTarget = now;
            e.LastSeenByPlayer = now; // it sees them: they (mostly) see it
        }
        else
            e.TargetVisible = false;

        // What to do.
        var dist = target != null ? Vector3.Distance(target.Position, e.Position) : float.MaxValue;
        var known = now - e.LastSawTarget < 6 && Players.TryGetValue(e.Target, out var kt) && kt.Alive;
        switch (e.State)
        {
            case PveState.Cover when now < e.CoverUntil && (target != null || known):
                break; // stay in cover a while
            default:
                if (!def.UsesCover && known)
                    SetEnemyState(e, PveState.Rush, now);
                else if (target != null)
                {
                    var wantsCover = def.UsesCover && now >= e.NextCover && (e.Health < e.MaxHealth * 0.75f || PveRand() < 0.35f || e.Ammo <= 2);
                    if (wantsCover && FindCover(e, target, out var spot))
                    {
                        e.CoverSpot = spot;
                        e.CoverUntil = now + 4 + PveRand() * 5;
                        SetEnemyState(e, PveState.Cover, now);
                    }
                    else
                        SetEnemyState(e, dist > def.PreferRange * 1.8f ? PveState.Advance : PveState.Engage, now);
                    e.NextCover = Math.Max(e.NextCover, now + 2);
                }
                else
                    SetEnemyState(e, PveState.Advance, now);
                break;
        }

        // Where to go.
        e.Goal = e.State switch
        {
            PveState.Cover => e.CoverSpot,
            PveState.Rush => known && Players.TryGetValue(e.Target, out var rt) ? rt.Position : e.LastSeen,
            PveState.Engage => e.Position,
            _ => known ? e.LastSeen : IdleGoal(e),
        };
    }

    private readonly byte[] _pveTriedIds = new byte[3];

    private bool PveTried(Player p, int count)
    {
        for (var i = 0; i < count; i++)
            if (_pveTriedIds[i] == p.Id)
                return true;
        return false;
    }

    private static void SetEnemyState(PveEnemy e, PveState s, double now)
    {
        if (e.State == s)
            return;
        e.State = s;
        e.StateAt = now;
    }

    /// <summary>Nobody known: the nearest player when hunting (they always come for you), else the director's attack point.</summary>
    private Vector3 IdleGoal(PveEnemy e)
    {
        if (!PveHuntPlayers)
            return PveAttackPoint;
        Player? best = null;
        var bestD = float.MaxValue;
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var d = Vector3.DistanceSquared(p.Position, e.Position);
            if (d < bestD)
            {
                bestD = d;
                best = p;
            }
        }
        return best != null && bestD < 150f * 150f ? best.Position : PveAttackPoint;
    }

    private readonly List<int> _pveCover = new(512);

    /// <summary>A cover spot: a nav node within 8 m, close to walls (low openness), that still sees the target from standing.</summary>
    private bool FindCover(PveEnemy e, Player target, out Vector3 spot)
    {
        spot = default;
        var nav = NavGraph.Current;
        if (nav.Nodes.Length == 0 || nav.Openness.Length != nav.Nodes.Length)
            return false;
        _pveCover.Clear();
        nav.Within(e.Position, 8f, _pveCover);
        var best = -1;
        var bestScore = float.MaxValue;
        for (var k = 0; k < 10 && _pveCover.Count > 0; k++)
        {
            var i = _pveCover[PveRange(_pveCover.Count)];
            var q = nav.Nodes[i];
            if (MathF.Abs(q.Y - e.Position.Y) > 1.5f || !SpawnableNode(nav, i))
                continue;
            var score = nav.Openness[i] + Vector3.Distance(q, e.Position) * 2f;
            if (Vector3.Distance(q, target.Position) < 6f)
                continue;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        if (best < 0 || !ClearRay(nav.Nodes[best] + new Vector3(0, 1.55f, 0), target.Chest))
            return false;
        spot = nav.Nodes[best];
        return true;
    }

    // ---- Moving -----------------------------------------------------------------------------------------------

    private float EnemySpeed(PveEnemy e) => e.State is PveState.Rush or PveState.Cover || (e.State == PveState.Advance && !e.TargetVisible) ? e.Def.Run : e.Def.Walk;

    private void MoveEnemy(PveEnemy e, double now)
    {
        var goal = e.Goal;
        var toGoal = goal - e.Position;
        var wantsMove = e.State != PveState.Engage && PFlat(toGoal) > (e.State == PveState.Cover ? 0.6f : 1.5f);
        if (e.State == PveState.Rush && e.TargetVisible && PFlat(toGoal) < 3f)
            wantsMove = false; // point blank: stop and fire
        // Crouch: in cover between bursts, heavies and snipers while firing.
        var inCover = e.State == PveState.Cover && !wantsMove;
        e.Crouched = !wantsMove && ((inCover && (e.BurstLeft == 0 || now < e.ReloadUntil)) || (e.Def.CrouchFire && e.TargetVisible));
        e.Moving = wantsMove;

        var moveYaw = e.Yaw;
        if (wantsMove)
        {
            moveYaw = SteerEnemy(e, goal, now);
            if (now < e.UnstickUntil)
                moveYaw = e.UnstickYaw;
        }
        // Facing: at the target when it's in sight, else the way it walks.
        if (e.TargetVisible && Players.TryGetValue(e.Target, out var t))
        {
            var to = t.Chest - EnemyEye(e);
            e.Yaw = World.LerpAngle(e.Yaw, MathF.Atan2(to.X, -to.Z), 0.3f);
            e.Pitch = MathF.Atan2(to.Y, PFlat(to));
        }
        else if (wantsMove)
        {
            e.Yaw = World.LerpAngle(e.Yaw, moveYaw, 0.2f);
            e.Pitch *= 0.9f;
        }

        if (e.NearPhysics || !wantsMove)
        {
            var cmd = new InputCmd
            {
                Buttons = (wantsMove ? Buttons.Forward : Buttons.None) | (e.Crouched ? Buttons.Crouch : Buttons.None),
                Yaw = moveYaw,
                SpeedPct = (byte)Math.Clamp((int)MathF.Round(EnemySpeed(e) / World.WalkSpeed * 100f), 1, 200),
            };
            if (wantsMove && e.PathLen > 0 && e.PathIdx < e.PathLen && (uint)e.Path[e.PathIdx] < (uint)NavGraph.Current.Nodes.Length
                && NavGraph.Current.Nodes[e.Path[e.PathIdx]].Y - e.Position.Y > World.StepHeight)
                cmd.Buttons |= Buttons.Jump; // a ledge: mantle up
            if (e.NearPhysics || e.Sim.Velocity.LengthSquared() > 0.01f || !e.Sim.Grounded)
            {
                AllocProfile.Mark("coop: physics");
                World.Simulate(ref e.Sim, cmd);
                AllocProfile.Mark("coop: move");
                if (!float.IsFinite(e.Sim.Position.X + e.Sim.Position.Y + e.Sim.Position.Z))
                    e.Sim = new MoveState { Position = e.PrevPos, Grounded = true };
            }
        }
        else
            SlideEnemy(e, EnemySpeed(e) * World.TickDt); // far away: along the path, no collision

        // Stuck: barely moved in a second while trying to: walk off sideways for a moment and plan again.
        if (now - e.StuckAt > 1.0)
        {
            var moved = PFlat(e.Position - e.StuckPos);
            if (wantsMove && moved < 0.25f * EnemySpeed(e))
            {
                e.UnstickUntil = now + 0.7;
                e.UnstickYaw = World.WrapAngle(moveYaw + (PveRand() < 0.5f ? 1.5f : -1.5f));
                e.PathLen = 0;
                e.NextPath = now;
                if (e.StuckSince < 0)
                    e.StuckSince = now;
            }
            else if (moved > 1f || !wantsMove)
                e.StuckSince = -1;
            e.StuckPos = e.Position;
            e.StuckAt = now;
        }
    }

    /// <summary>The yaw towards <paramref name="goal"/>: straight when close and in plain view, else along a nav path (planned within the shared budget).</summary>
    private float SteerEnemy(PveEnemy e, Vector3 goal, double now)
    {
        var pos = e.Position;
        var to = goal - pos;
        // Close and in plain view: straight there (the sight check every 6th tick).
        if (e.NearPhysics && PFlat(to) < 12f && MathF.Abs(to.Y) < 1.2f)
        {
            if ((ServerTick + e.Id) % 6 == 0 || e.PathGoal != goal)
                e.DirectOk = NavGraph.Clear(pos, goal);
            if (e.DirectOk)
            {
                e.PathLen = 0;
                e.PathGoal = goal;
                return MathF.Atan2(to.X, -to.Z);
            }
        }
        var nav = NavGraph.Current;
        AllocProfile.Mark("coop: path");
        if ((e.PathLen == 0 || now >= e.NextPath || Vector3.DistanceSquared(goal, e.PathGoal) > 16f) && _pvePathBudget > 0 && nav.Nodes.Length > 0)
        {
            _pvePathBudget--;
            e.NextPath = now + 1.5 + PveRand() * 1.5;
            e.PathGoal = goal;
            var a = nav.Nearest(pos, needSight: false);
            var b = nav.Nearest(goal, needSight: false);
            e.PathLen = a >= 0 && b >= 0 ? _pveSearch.Find(nav, a, b, e.Path, null, 4000) : 0;
            e.PathIdx = e.PathLen > 1 ? 1 : 0;
        }
        AllocProfile.Mark("coop: move");
        while (e.PathLen > 0 && e.PathIdx < e.PathLen && (uint)e.Path[e.PathIdx] < (uint)nav.Nodes.Length)
        {
            var n = nav.Nodes[e.Path[e.PathIdx]];
            if (PFlat(n - pos) < 0.7f && e.PathIdx < e.PathLen - 1)
            {
                e.PathIdx++;
                continue;
            }
            var d = n - pos;
            if (e.PathIdx == e.PathLen - 1 && PFlat(d) < 0.7f)
                break;
            return MathF.Atan2(d.X, -d.Z);
        }
        return MathF.Atan2(to.X, -to.Z);
    }

    /// <summary>Far from every player: slide <paramref name="step"/> metres along the path (node heights), no physics.</summary>
    private void SlideEnemy(PveEnemy e, float step)
    {
        var nav = NavGraph.Current;
        e.Sim.Velocity = Vector3.Zero;
        e.Sim.Grounded = true;
        while (step > 0 && e.PathLen > 0 && e.PathIdx < e.PathLen && (uint)e.Path[e.PathIdx] < (uint)nav.Nodes.Length)
        {
            var n = nav.Nodes[e.Path[e.PathIdx]];
            var d = n - e.Sim.Position;
            var len = d.Length();
            if (len <= step)
            {
                e.Sim.Position = n;
                step -= len;
                e.PathIdx++;
                continue;
            }
            e.Sim.Position += d / len * step;
            return;
        }
    }

    // ---- Shooting ------------------------------------------------------------------------------------------------

    private readonly Player?[] _pveVictims = new Player?[8];
    private readonly float[] _pveVictimDamage = new float[8];
    private readonly bool[] _pveVictimHead = new bool[8];

    private void FireEnemy(PveEnemy e, double now)
    {
        var def = e.Def;
        var gun = def.Def;
        if (e.ReloadUntil > 0)
        {
            if (now < e.ReloadUntil)
                return;
            e.ReloadUntil = 0;
            e.Ammo = gun.Mag;
        }
        if (!e.TargetVisible || !Players.TryGetValue(e.Target, out var target) || !target.Alive || !DamageEnabled)
            return;
        var buffed = Buffed(e);
        var reaction = PveDifficulty.Reaction(BotSkill) * (buffed ? 0.7f : 1f);
        if (now - e.TargetSince < reaction || now < e.NextShot)
            return;
        var eye = EnemyEye(e);
        var dist = Vector3.Distance(eye, target.Chest);
        if (dist > def.MaxRange)
            return;
        if (e.Ammo <= 0)
        {
            e.ReloadUntil = now + gun.Reload;
            e.BurstLeft = 0;
            return;
        }
        if (def.GrenadeCooldown > 0 && now >= e.NextGrenade && now >= _pveNextGrenadeAny && dist is > 10f and < 28f && target.Sim.Velocity.LengthSquared() < 4f)
        {
            ThrowEnemyGrenade(e, target, now);
            return;
        }
        if (e.BurstLeft <= 0)
            e.BurstLeft = def.BurstMin + PveRange(def.BurstMax - def.BurstMin + 1);
        EnemyShot(e, target, eye, buffed, now);
        e.Ammo--;
        e.BurstLeft--;
        e.NextShot = now + Math.Max(gun.FireInterval, gun.Mode == FireMode.Auto ? 0 : 0.22f);
        if (e.BurstLeft <= 0)
            e.NextShot += def.BurstPause * (0.7f + 0.6f * PveRand());
    }

    /// <summary>A random direction within <paramref name="cone"/> radians of <paramref name="dir"/> (uniform over the disc).</summary>
    private Vector3 Cone(Vector3 dir, float cone)
    {
        var side = MathF.Abs(dir.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY)) : Vector3.UnitX;
        var up = Vector3.Cross(side, dir);
        var a = PveRand() * MathF.Tau;
        var r = MathF.Sqrt(PveRand()) * cone;
        return Vector3.Normalize(dir + (side * MathF.Cos(a) + up * MathF.Sin(a)) * MathF.Tan(r));
    }

    /// <summary>
    /// Server: one round (all its pellets) from <paramref name="e"/> at <paramref name="target"/>: a cone round the chest by
    /// the archetype's spread, the difficulty, the target's movement, how long it's been on them and an officer nearby;
    /// rays against the world and every living player's hitbox as they are now; damage by the weapon and the difficulty.
    /// </summary>
    internal void EnemyShot(PveEnemy e, Player target, Vector3 eye, bool buffed, double now)
    {
        var def = e.Def;
        var gun = def.Def;
        var to = target.Chest - eye;
        var dist = to.Length();
        var dir = dist > 1e-3f ? to / dist : World.FlatForward(e.Yaw);
        var spread = def.Spread * PveDifficulty.Spread(BotSkill) * (target.Sim.Velocity.LengthSquared() > 9f ? 1.35f : 1f)
                     * (now - e.TargetSince < 0.6 + PveDifficulty.Reaction(BotSkill) ? 1.8f : 1f) * (buffed ? 0.8f : 1f);
        var aim = Cone(dir, spread);
        var victims = 0;
        Vector3 end = eye + aim * World.Range;
        for (var k = 0; k < Math.Max(1, gun.Pellets); k++)
        {
            var d = gun.Pellets > 1 ? Cone(aim, gun.HipSpreadRad * 0.6f) : aim;
            var len = World.Query.Raycast(eye, d, World.Range, QueryFilter.Shots);
            Player? hit = null;
            foreach (var p in Players.Values)
            {
                if (!p.Alive)
                    continue;
                if (World.RayHitsBody(p.Position, p.Sim.Stance, p.Yaw, eye, d, len) is { } t && t < len)
                {
                    len = t;
                    hit = p;
                }
            }
            if (k == 0)
                end = eye + d * len;
            if (hit == null)
                continue;
            var head = World.IsHeadshot(hit.Position, hit.Sim.Stance, hit.Yaw, eye + d * len);
            var dmg = gun.Damage * gun.DamageMultAt(len) * PveDifficulty.Damage(BotSkill) * def.Damage * (head ? 1.5f : 1f);
            var slot = -1;
            for (var v = 0; v < victims; v++)
                if (_pveVictims[v] == hit)
                    slot = v;
            if (slot < 0 && victims < _pveVictims.Length)
            {
                slot = victims++;
                _pveVictims[slot] = hit;
                _pveVictimDamage[slot] = 0;
                _pveVictimHead[slot] = false;
            }
            if (slot >= 0)
            {
                _pveVictimDamage[slot] += dmg;
                _pveVictimHead[slot] |= head;
            }
        }
        for (var v = 0; v < victims; v++)
        {
            var p = _pveVictims[v]!;
            var amount = Math.Max(1, (int)MathF.Round(_pveVictimDamage[v]));
            Director.AddStress(amount / 500f);
            DamagePlayer(p, p, amount, _pveVictimHead[v], PveKillId, now, DamageKind.Bullet, eye); // the player's own "death by enemy" (no player credited)
            _pveVictims[v] = null;
        }
        e.ShotSeq = (byte)((e.ShotSeq + 1) & 15);
        PveShotsFired++;
        PveShotsHit += victims > 0 ? 1 : 0;
        // The host sees and hears it straight away (clients from the shot counter in Msg.PEnts).
        if (Local is { } me && Vector3.DistanceSquared(me.Position, eye) < 140f * 140f)
            PveShotFx(e, eye, end, now);
    }

    /// <summary>Rounds enemies fired and rounds that hit a player (the self-test checks they hit with their spread).</summary>
    public int PveShotsFired, PveShotsHit;

    /// <summary>Both sides: an enemy's tracer from its muzzle and its gunshot.</summary>
    private void PveShotFx(PveEnemy e, Vector3 eye, Vector3 end, double now)
    {
        var gun = e.Def.Def;
        var muzzle = eye + World.FlatRight(e.Yaw) * 0.22f - new Vector3(0, 0.15f, 0) + World.FlatForward(e.Yaw) * 0.7f;
        AddTracer(muzzle, end, gun, now);
        Audio.Shot(gun, eye, MixBus.CoopEnemies); // the audio mixer's Co-op enemies row on top of the gun rows
    }

    // ---- Grenades ---------------------------------------------------------------------------------------------------

    /// <summary>Server: a frag at a player who stays put (a real projectile; its blast hurts players only: <see cref="PveGrenadeBlast"/>).</summary>
    private void ThrowEnemyGrenade(PveEnemy e, Player target, double now)
    {
        var def = Throwables.Get(ThrowableKind.Frag);
        var eye = EnemyEye(e);
        Throwables.AimPitch(def, eye, target.Position, out var pitch);
        var to = target.Position - e.Position;
        var yaw = MathF.Atan2(to.X, -to.Z) + (PveRand() - 0.5f) * 0.15f * PveDifficulty.Spread(BotSkill);
        var dir = World.Forward(yaw, pitch);
        var proj = new Projectile
        {
            Id = _nextProjectileId++,
            Kind = ThrowableKind.Frag,
            Owner = PveGrenadeOwner,
            Pos = Throwables.LaunchPoint(eye, dir),
            Vel = Throwables.LaunchVelocity(def, dir, Vector3.Zero),
            Born = now,
        };
        if (_nextProjectileId == 0)
            _nextProjectileId = 1;
        Projectiles[proj.Id] = proj;
        _w.Begin(Msg.ProjSpawn).U16(proj.Id).U8((byte)proj.Kind).U8(proj.Owner).Vec(proj.Pos).Vec(proj.Vel);
        Net.Broadcast(_w.Span, Brutal.RakNetApi.PacketReliability.ReliableOrdered);
        Audio.Effect3D(Sfx.Throw, proj.Pos, 0.6f, MixBus.CoopEnemies);
        e.NextGrenade = now + e.Def.GrenadeCooldown * (0.8 + 0.4 * PveRand()) * PveDifficulty.Grenades(BotSkill);
        _pveNextGrenadeAny = now + 2.5;
        e.NextShot = now + 0.8;
        PveGrenadesThrown++;
    }

    public int PveGrenadesThrown;

    /// <summary>Server (Throwables.ServerDetonate): an enemy frag went off: players in reach take damage (walls shield, prone helps). Returns hit flags.</summary>
    internal byte PveGrenadeBlast(Projectile proj, double now)
    {
        var def = Throwables.Get(proj.Kind);
        var center = proj.Pos + new Vector3(0, 0.15f, 0);
        byte flags = 0;
        foreach (var victim in Players.Values)
        {
            if (!victim.Alive)
                continue;
            var to = victim.Chest - center;
            var dist = to.Length();
            if (dist > def.Radius || (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.All) < dist - 0.05f))
                continue;
            var dmg = Throwables.BlastDamage(def, dist) * MathF.Min(1f, PveDifficulty.Damage(BotSkill) * 1.3f);
            if (victim.Sim.Stance == Stance.Prone)
                dmg *= 0.7f;
            var amount = (int)MathF.Round(dmg);
            if (amount <= 0)
                continue;
            flags |= 1;
            _pveBlastVictims.Add(victim);
            _pveBlastAmounts.Add(amount);
        }
        for (var i = 0; i < _pveBlastVictims.Count; i++)
        {
            Director.AddStress(_pveBlastAmounts[i] / 300f);
            DamagePlayer(_pveBlastVictims[i], _pveBlastVictims[i], _pveBlastAmounts[i], false, PveKillId, now, DamageKind.Explosive, center);
        }
        _pveBlastVictims.Clear();
        _pveBlastAmounts.Clear();
        BlastKnockback(null, center, def.Radius, def.Knockback);
        return flags;
    }

    private readonly List<Player> _pveBlastVictims = new(8);
    private readonly List<int> _pveBlastAmounts = new(8);
}
