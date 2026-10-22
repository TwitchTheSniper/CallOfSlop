using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Breakable props, server-authoritative: the server applies bullet, melee, explosion and kill-streak damage to the
/// map's breakable props, decides when one breaks and tells everyone (<see cref="Msg.PropBreak"/>); a player who joins
/// mid-match gets the broken set (<see cref="Msg.PropState"/>). Every PC then hides the prop, drops its collision and
/// throws out its debris (<see cref="PropDebris"/>, cosmetic). Props are whole again at the start of every match.
///
/// Bullets stop at a solid prop like at any box (the shot that breaks it stops too); cosmetic breakables (bottles) break
/// as a bullet passes through. Bots plan their routes on the nav graph built from the whole map, so a broken prop just
/// leaves them a little more room.
/// </summary>
public sealed partial class Game
{
    // ---- Server: damage -------------------------------------------------------------------------------------

    /// <summary>Server: a bullet from <paramref name="origin"/> along <paramref name="dir"/> that stopped at <paramref name="dist"/>.</summary>
    internal void ShotProps(Player? shooter, Vector3 origin, Vector3 dir, float dist, float damage, double now)
    {
        var map = World.Map;
        var list = map.BreakableProps;
        if (list.Length == 0 || damage <= 0)
            return;
        var solidHit = -1;
        var solidT = float.MaxValue;
        foreach (var i in list)
        {
            if (map.IsPropBroken(i))
                continue;
            var p = map.PropInstances[i];
            if (p.Bounds.Raycast(origin, dir, dist + 0.05f) is not { } tb)
                continue;
            if (!p.Def.IsSolid)
            {
                if (tb < dist)
                    DamageProp(shooter, i, damage, PropDamage.Bullet, origin, now); // litter breaks as the bullet goes through
                continue;
            }
            for (var k = p.SolidStart; k < p.SolidStart + p.SolidCount; k++)
                if (map.Solid[k].Raycast(origin, dir, dist + 0.05f) is { } t && t >= dist - 0.05f && t < solidT)
                {
                    solidT = t;
                    solidHit = i; // the prop that stopped the bullet
                }
        }
        if (solidHit >= 0)
            DamageProp(shooter, solidHit, damage, PropDamage.Bullet, origin, now);
    }

    /// <summary>Server: a melee swing that hit nobody; the nearest breakable prop in reach along the aim takes the blow.</summary>
    internal bool MeleeProps(Player? attacker, Vector3 origin, Vector3 dir, double now)
    {
        var map = World.Map;
        var best = -1;
        var bestT = MeleeRange + 0.4f;
        foreach (var i in map.BreakableProps)
        {
            if (map.IsPropBroken(i))
                continue;
            var b = map.PropInstances[i].Bounds;
            var grown = b with { Half = b.Half + new Vector3(0.12f) }; // a little forgiving: small props are hard to aim at
            if (grown.Raycast(origin, dir, bestT) is { } t && t < bestT && World.Query.Raycast(origin, dir, t, QueryFilter.All) >= t - 0.25f)
            {
                bestT = t;
                best = i;
            }
        }
        if (best < 0)
            return false;
        DamageProp(attacker, best, MeleeObjectDamage, PropDamage.Melee, origin, now);
        return true;
    }

    /// <summary>Server: a blast (grenade or kill streak). Damage falls off with distance like it does for players; walls shield.</summary>
    internal void BlastProps(Player? owner, Vector3 center, float radius, float maxDamage, PropDamage kind, double now)
    {
        var map = World.Map;
        foreach (var i in map.BreakableProps)
        {
            if (map.IsPropBroken(i))
                continue;
            var b = map.PropInstances[i].Bounds;
            var closest = Vector3.Clamp(center, b.Min, b.Max);
            var to = closest - center;
            var dist = to.Length();
            if (dist > radius)
                continue;
            if (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.All) < dist - 0.2f)
                continue; // something else in between (the prop's own surface is at dist)
            var dmg = MathF.Max(1f, maxDamage * MathF.Pow(1f - dist / radius, 1.4f));
            if (map.PropInstances[i].Def.Breakable is { IsAnimal: true } animal && dist <= radius * GibBlastFraction)
                dmg = MathF.Max(dmg, animal.Health); // an animal near a blast goes at once (GameGibs.cs)
            DamageProp(owner, i, dmg, kind, center, now);
        }
    }

    private void DamageProp(Player? attacker, int i, float amount, PropDamage kind, Vector3 from, double now)
    {
        if (IsPropLit(i))
        {
            if (attacker != null && World.Map.PropInstances[i].Def.Breakable is { IsMine: true } lb && (lb.BreakBy & kind) != 0)
                CreditLitMine(i, attacker); // shot while clicking (even in the tick it was stepped on): the blast is the shooter's (GameMines.cs)
            return; // already burning: it goes off on its own
        }
        if (attacker != null && World.Map.PropInstances[i].Def.Breakable is { IsMine: true } mb && (mb.BreakBy & kind) != 0)
            NoteMineHit(i, attacker); // whoever last hit a mine gets its kills, even if someone steps on it later (GameMines.cs)
        if (!World.Map.DamageProp(i, Math.Max(1, (int)MathF.Round(amount)), kind))
            return;
        if (World.Map.PropInstances[i].Def.Breakable is { ExplodeRadius: > 0, ExplodeFuse: > 0 } br)
            LightProp(i, kind is PropDamage.Explosion or PropDamage.Streak ? ChainFuse(i, br.ExplodeFuse) : br.ExplodeFuse, attacker, now);
        else
            ServerBreakProp(i, from, kind, attacker, now);
    }

    // ---- Server: fuses (explosive props that burn for a moment before they go off) ---------------------------

    /// <summary>Most explosive props that go off in one server tick; any others due wait for the next ticks, so a big chain stays cheap.</summary>
    public const int MaxPropDetonationsPerTick = 4;

    /// <summary>
    /// Most prop fireballs shown within <see cref="PropBlastWindowTicks"/>: more explosions still hurt players and break
    /// props (with their debris and sound), but send no extra fireball, so a mass chain reaction costs every PC a bounded
    /// number of blast effects. An explosion that hurt someone always shows (the owner's hit marker rides on it).
    /// </summary>
    public const int MaxPropBlastsShown = 20;

    /// <summary>The window (server ticks, 0.6 s: a fireball's life) that <see cref="MaxPropBlastsShown"/> counts over.</summary>
    public const int PropBlastWindowTicks = 36;

    /// <summary>Set on <see cref="Msg.PropBreak"/>'s kind byte when the prop was lit (its fuse started) rather than broken.</summary>
    public const byte PropLitFlag = 0x80;

    /// <summary>Set on <see cref="Msg.PropBreak"/>'s kind byte when a broken prop came back (the Prop op's "respawn"; protocol 31).</summary>
    public const byte PropRestoreFlag = 0x40;

    private readonly List<(int Index, double At, byte Attacker, bool Env)> _propFuses = new();
    private bool[] _propLit = [];
    private Map? _fuseMap;
    private int _fuseMatch = -1;
    private readonly Queue<long> _propBlastTicks = new();

    /// <summary>Explosive props burning right now (lit, not gone off yet).</summary>
    public int PropFusesBurning => _propFuses.Count;

    /// <summary>Explosive props that went off since this game started, and how many of them showed a fireball (for the self-test).</summary>
    public int PropDetonations { get; private set; }

    /// <inheritdoc cref="PropDetonations"/>
    public int PropBlastsShown { get; private set; }

    /// <summary>
    /// Fuse of an explosive prop lit by another explosion: its own fuse, but at most 0.12-0.32 s, varying from prop to prop
    /// (the same on every run), so a cluster goes up in a quick ripple instead of all in one tick.
    /// </summary>
    public static float ChainFuse(int index, float fuse) => MathF.Min(fuse, 0.12f + ((uint)index * 2654435761u >> 24) / 255f * 0.2f);

    /// <summary>Is prop <paramref name="i"/> lit (burning down its fuse) in this match on this map?</summary>
    public bool IsPropLit(int i) => ReferenceEquals(_fuseMap, World.Map) && _fuseMatch == MatchNumber && (uint)i < (uint)_propLit.Length && _propLit[i];

    /// <summary>Starts the per-match fuse (and respawn) state over when the map or the match changed.</summary>
    private void EnsurePropFuseState()
    {
        var map = World.Map;
        if (!ReferenceEquals(_fuseMap, map) || _fuseMatch != MatchNumber || _propLit.Length != map.PropInstances.Count)
        {
            ClearPropFuses();
            _fuseMap = map;
            _fuseMatch = MatchNumber;
            _propLit = new bool[map.PropInstances.Count];
        }
    }

    private void LightProp(int i, float fuse, Player? attacker, double now)
    {
        var map = World.Map;
        EnsurePropFuseState();
        _propLit[i] = true;
        // Neutral (nobody's kill) when nobody set it off: a mine stepped on, or anything lit by a neutral blast.
        var env = attacker == null && (map.PropInstances[i].Def.Breakable is { IsMine: true } || _blastEnv);
        _propFuses.Add((i, now + fuse, attacker?.Id ?? byte.MaxValue, env));
        WritePropBreak(_w, i, (PropDamage)PropLitFlag, map.PropInstances[i].Bounds.Center);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnPropLit(i);
    }

    private void ClearPropFuses()
    {
        _propFuses.Clear();
        Array.Clear(_propLit);
        ClearMineState(); // last hitters and pending respawns (GameMines.cs)
    }

    /// <summary>Server, every tick: explosive props whose fuse has burnt down go off (the earliest first, a few a tick).</summary>
    private void ServerTickPropFuses(double now)
    {
        if (_propFuses.Count == 0)
            return;
        if (!ReferenceEquals(_fuseMap, World.Map) || _fuseMatch != MatchNumber)
        {
            ClearPropFuses(); // a new match or map: every prop is whole again
            return;
        }
        for (var done = 0; done < MaxPropDetonationsPerTick; done++)
        {
            var best = -1;
            for (var k = 0; k < _propFuses.Count; k++)
                if (_propFuses[k].At <= now && (best < 0 || _propFuses[k].At < _propFuses[best].At))
                    best = k;
            if (best < 0)
                break;
            var (i, _, by, env) = _propFuses[best];
            _propFuses.RemoveAt(best);
            Players.TryGetValue(by, out var attacker); // null if they left: the blast still breaks props
            ServerBreakProp(i, World.Map.PropInstances[i].Bounds.Center, PropDamage.Explosion, attacker, now, env);
        }
    }

    /// <summary>Projectile id used for the explosion of an explosive prop (thrown projectiles count up from 1 and only reach it after 65,534 throws).</summary>
    public const ushort PropExplosionId = ushort.MaxValue;

    /// <summary>
    /// Server: breaks prop <paramref name="i"/> and tells everyone. An explosive prop (a fuel drum) then goes off like a
    /// frag grenade owned by whoever broke it: it hurts players and breaks other props nearby (chains are fine: a prop
    /// only ever breaks once). An explosive prop with a fuse gets here when its fuse has burnt down (see <see cref="LightProp"/>).
    /// </summary>
    internal void ServerBreakProp(int i, Vector3 from, PropDamage kind, Player? attacker = null, double now = 0, bool env = false)
    {
        if (!IsServer || !World.Map.BreakProp(i))
            return;
        WritePropBreak(_w, i, kind, from);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnPropBroken(i, from, kind);
        ScheduleRespawn(i, now); // a prop placed with "respawn" comes back later (GameMines.cs)
        var inst = World.Map.PropInstances[i];
        if (inst.Def.Breakable is { ExplodeRadius: > 0 } br)
        {
            env |= attacker == null && (br.IsMine || _blastEnv); // nobody set it off: a stepped-on mine, or a neutral blast's chain
            // A mine's charge sits at ground level: the blast starts just above it, like a grenade on the floor.
            var center = br.IsMine ? inst.Position + new Vector3(0, 0.2f, 0) : inst.Bounds.Center;
            PropExplode(center, br, attacker, env, now);
        }
    }

    /// <summary>Set while a neutral blast (a mine nobody shot) breaks props, so what it sets off is neutral too.</summary>
    private bool _blastEnv;

    /// <summary>
    /// An explosive prop going off. A barrel blast belongs to whoever broke it (their teammates are safe, no friendly fire).
    /// A mine (or anything a neutral blast set off) is neutral: it hurts everyone in reach. Victims the credited player
    /// could hurt anyway (enemies, and themselves) are that player's kills; everyone else's death is nobody's kill
    /// ("Landmine" in the feed, no XP, like a fall). Walls shield, prone and Flak Jacket help, as for grenades.
    /// </summary>
    private void PropExplode(Vector3 center, PropBreakable br, Player? attacker, bool env, double now)
    {
        byte flags = 0;
        var neutral = br.IsMine || env;
        var killId = neutral ? LandmineKillId : (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag);
        if (attacker != null || neutral)
        {
            foreach (var victim in Players.Values.ToList())
            {
                if (!victim.Alive)
                    continue;
                var to = victim.Chest - center;
                var dist = to.Length();
                if (dist > br.ExplodeRadius || (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.All) < dist - 0.05f))
                    continue;
                var dmg = br.ExplodeDamage * MathF.Pow(1f - dist / br.ExplodeRadius, 1.4f);
                if (victim.Sim.Stance == Stance.Prone)
                    dmg *= 0.7f;
                var amount = (int)MathF.Round(dmg);
                if (amount <= 0)
                    continue;
                flags |= 1;
                var by = attacker != null && CanDamage(attacker, victim) ? attacker : neutral ? victim : attacker!; // neutral: the victim's own death
                if (!DamagePlayer(by, victim, amount, false, killId, now, DamageKind.Explosive, center))
                    continue;
                PropBlastKills++;
                LastPropBlastKill = (by.Id, victim.Id, killId);
                if (victim != by)
                    flags |= 4;
            }
        }
        if (neutral)
            BlastZombiesNeutral(attacker, center, br.ExplodeRadius, br.ExplodeDamage * 3f, now); // zombies too (GameMines.cs)
        BlastKnockback(neutral ? null : attacker, center, br.ExplodeRadius, br.ExplodeKnockback); // pushes the living (Knockback.cs); a mine pushes everyone
        PropDetonations++;
        while (_propBlastTicks.Count > 0 && ServerTick - _propBlastTicks.Peek() >= PropBlastWindowTicks)
            _propBlastTicks.Dequeue();
        if (_propBlastTicks.Count < MaxPropBlastsShown || flags != 0)
        {
            _propBlastTicks.Enqueue(ServerTick);
            PropBlastsShown++;
            var look = br.IsMine ? ThrowableKind.Impact : ThrowableKind.Frag; // a mine's fireball is the smaller one (its blast is about an impact grenade's)
            _w.Begin(Msg.Explode).U16(PropExplosionId).U8((byte)look).U8(attacker?.Id ?? byte.MaxValue).Vec(center).U8(flags);
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
            OnDetonated(look, attacker?.Id ?? byte.MaxValue, center, flags, now);
        }
        var wasEnv = _blastEnv;
        _blastEnv = env;
        try
        {
            BlastProps(attacker, center, br.ExplodeRadius, br.ExplodeDamage, PropDamage.Explosion, now);
        }
        finally
        {
            _blastEnv = wasEnv;
        }
        BlastTactical(attacker, center, br.ExplodeRadius, br.ExplodeDamage, now); // tactical devices (GameTactical.cs)
    }

    // ---- Packets ----------------------------------------------------------------------------------------------

    /// <summary>Most broken-prop indices in one <see cref="Msg.PropState"/> packet (2 bytes each, well inside 1400).</summary>
    public const int PropStateBatch = 600;

    /// <summary>The break event: prop index, damage kind, and where the hit came from (for the debris direction). 17 bytes.</summary>
    public static void WritePropBreak(PacketWriter w, int index, PropDamage kind, Vector3 from) =>
        w.Begin(Msg.PropBreak).U16((ushort)index).U8((byte)kind).Vec(from);

    /// <summary>The broken set as one or more packets (the first one says "restore everything first").</summary>
    public static IEnumerable<int> WritePropState(PacketWriter w, IReadOnlyList<int> broken, Action<PacketWriter> send)
    {
        var first = true;
        var at = 0;
        do
        {
            var n = Math.Min(PropStateBatch, broken.Count - at);
            w.Begin(Msg.PropState).U8((byte)(first ? 1 : 0)).U16((ushort)n);
            for (var k = 0; k < n; k++)
                w.U16((ushort)broken[at + k]);
            send(w);
            first = false;
            at += n;
        } while (at < broken.Count);
        return broken;
    }

    /// <summary>Server: sends a joining player the props broken so far this match.</summary>
    private void SendPropState(RakNetGUID to)
    {
        if (World.Map.PropInstances.Count == 0)
            return;
        WritePropState(_w, World.Map.BrokenProps, w => Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered));
    }

    private void OnPropPacket(byte id, ref PacketReader r)
    {
        switch (id)
        {
            case (byte)Msg.PropBreak when !IsServer && r.Has(15):
            {
                int i = r.U16();
                var kind = r.U8();
                var from = r.Vec();
                if ((kind & PropLitFlag) != 0)
                    OnPropLit(i); // an explosive prop's fuse started: it goes off (a second PropBreak) a moment later
                else if ((kind & PropRestoreFlag) != 0)
                    ApplyPropRestore(World.Map, i); // a prop with "respawn" is back (GameMines.cs)
                else
                    ApplyPropBreak(World.Map, i, (PropDamage)kind, from);
                break;
            }
            case (byte)Msg.PropState when !IsServer && r.Has(3):
                ReadPropState(World.Map, ref r);
                break;
        }
    }

    /// <summary>Client: a prop broke on the server.</summary>
    internal void ApplyPropBreak(Map map, int i, PropDamage kind, Vector3 from)
    {
        if ((uint)i >= (uint)map.PropInstances.Count)
            return;
        map.BreakProp(i); // (already broken when host and client share one map in a test: the debris still flies)
        OnPropBroken(i, from, kind);
    }

    /// <summary>Client: the broken set from the server (joining mid-match). No debris: those broke before you arrived.</summary>
    public static void ReadPropState(Map map, ref PacketReader r)
    {
        var reset = r.U8() == 1;
        int n = r.U16();
        if (reset)
            map.RestoreProps();
        for (var k = 0; k < n && r.Has(2); k++)
        {
            int i = r.U16();
            if ((uint)i < (uint)map.PropInstances.Count)
                map.BreakProp(i);
        }
    }

    /// <summary>Both sides: an explosive prop was lit (sparks spurting from its top and a clank). It stays whole until it goes off.</summary>
    private void OnPropLit(int i)
    {
        var map = World.Map;
        if ((uint)i >= (uint)map.PropInstances.Count || map.IsPropBroken(i))
            return;
        var b = map.PropInstances[i].Bounds;
        if (map.PropInstances[i].Def.Breakable is { IsMine: true })
        {
            // A pressure plate: a sharp click (and a glint of two sparks), then the blast a moment later.
            Audio.Effect3D(Sfx.DeployDevice, b.Center, 1f);
            Audio.Effect3D(Sfx.DryFire, b.Center, 0.9f);
            PropDebris.Sparks(new Vector3(b.Center.X, b.Max.Y, b.Center.Z), 2, i * 7919 + (int)(PropDebris.Spawned & 0xFFFF));
            MinesClicked++;
            return;
        }
        PropDebris.Sparks(new Vector3(b.Center.X, b.Max.Y, b.Center.Z), 10, i * 7919 + (int)(PropDebris.Spawned & 0xFFFF));
        Audio.Effect3D(Sfx.BreakMetal, b.Center, 0.55f);
    }

    /// <summary>Both sides: debris and sound for a prop that just broke.</summary>
    private void OnPropBroken(int i, Vector3 from, PropDamage kind)
    {
        var map = World.Map;
        if ((uint)i >= (uint)map.PropInstances.Count)
            return;
        var p = map.PropInstances[i];
        if (p.Def.Breakable is { IsAnimal: true })
        {
            OnAnimalGibbed(i, p, from, kind); // meaty gibs instead of debris (GameGibs.cs)
            return;
        }
        PropDebris.Spawn(p, from, kind, i * 7919 + (int)(PropDebris.Spawned & 0xFFFF));
        if (p.Def.Breakable is { } br)
            Audio.Effect3D(br.Sound switch
            {
                PropBreakSound.Glass => Sfx.BreakGlass,
                PropBreakSound.Metal => Sfx.BreakMetal,
                PropBreakSound.Soft => Sfx.BreakSoft,
                _ => Sfx.BreakWood,
            }, p.Bounds.Center, 0.9f);
    }
}
