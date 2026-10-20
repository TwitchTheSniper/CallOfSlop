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
        DamageProp(attacker, best, MeleeDamage, PropDamage.Melee, origin, now);
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
            DamageProp(owner, i, dmg, kind, center, now);
        }
    }

    private void DamageProp(Player? attacker, int i, float amount, PropDamage kind, Vector3 from, double now)
    {
        if (World.Map.DamageProp(i, Math.Max(1, (int)MathF.Round(amount)), kind))
            ServerBreakProp(i, from, kind, attacker, now);
    }

    /// <summary>Projectile id used for the explosion of an explosive prop (thrown projectiles count up from 1 and only reach it after 65,534 throws).</summary>
    public const ushort PropExplosionId = ushort.MaxValue;

    /// <summary>
    /// Server: breaks prop <paramref name="i"/> and tells everyone. An explosive prop (a fuel drum) then goes off like a
    /// frag grenade owned by whoever broke it: it hurts players and breaks other props nearby (chains are fine: a prop
    /// only ever breaks once).
    /// </summary>
    internal void ServerBreakProp(int i, Vector3 from, PropDamage kind, Player? attacker = null, double now = 0)
    {
        if (!IsServer || !World.Map.BreakProp(i))
            return;
        WritePropBreak(_w, i, kind, from);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnPropBroken(i, from, kind);
        if (World.Map.PropInstances[i].Def.Breakable is { ExplodeRadius: > 0 } br)
            PropExplode(World.Map.PropInstances[i].Bounds.Center, br, attacker, now);
    }

    private void PropExplode(Vector3 center, PropBreakable br, Player? attacker, double now)
    {
        byte flags = 0;
        if (attacker != null)
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
                if (DamagePlayer(attacker, victim, amount, false, (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag), now, DamageKind.Explosive, center) && victim != attacker)
                    flags |= 4;
            }
        }
        _w.Begin(Msg.Explode).U16(PropExplosionId).U8((byte)ThrowableKind.Frag).U8(attacker?.Id ?? byte.MaxValue).Vec(center).U8(flags);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnDetonated(ThrowableKind.Frag, attacker?.Id ?? byte.MaxValue, center, flags, now);
        BlastProps(attacker, center, br.ExplodeRadius, br.ExplodeDamage, PropDamage.Explosion, now);
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
                var kind = (PropDamage)r.U8();
                var from = r.Vec();
                ApplyPropBreak(World.Map, i, kind, from);
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

    /// <summary>Both sides: debris and sound for a prop that just broke.</summary>
    private void OnPropBroken(int i, Vector3 from, PropDamage kind)
    {
        var map = World.Map;
        if ((uint)i >= (uint)map.PropInstances.Count)
            return;
        var p = map.PropInstances[i];
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
