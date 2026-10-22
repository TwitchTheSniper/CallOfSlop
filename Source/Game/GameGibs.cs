using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Meaty gibs, the game side (the pieces themselves are <see cref="Gibs"/>): animal props bursting when they break, and
/// the server's call on whether a blast kill blows the victim apart (the "gibbed" flag on <see cref="KillPacket"/>,
/// protocol 33). Everything visible is built on every PC from the break or kill event it already gets; no new traffic.
/// </summary>
public sealed partial class Game
{
    /// <summary>A blast within this fraction of its radius breaks an animal prop at once, whatever its health.</summary>
    public const float GibBlastFraction = 0.6f;

    /// <summary>
    /// A blast kill blows the victim apart when the killing hit did at least this much damage (a frag grenade does that
    /// within about a quarter of its radius, a landmine within two fifths, a rocket's direct hit always)...
    /// </summary>
    public const int GibDamage = 80;

    /// <summary>...or went this far past the health the victim had left (a weaker blast finishing someone off doesn't).</summary>
    public const int GibOverkill = 60;

    /// <summary>
    /// Server: does this killing hit blow the victim apart? Explosive damage only (grenades, launchers, landmines,
    /// exploding props, strike shells and gunship rounds), not the napalm fire; a big hit, or a lot of overkill. A blast
    /// at the edge of its radius gives a normal ragdoll.
    /// </summary>
    public static bool ShouldGib(DamageKind kind, byte killId, int amount, int healthBefore) =>
        kind == DamageKind.Explosive && killId != KillStreaks.KillId(StreakId.NapalmStrike)
        && (amount >= GibDamage || amount - Math.Max(healthBefore, 0) >= GibOverkill);

    /// <summary>The last kill message the server sent (self-test).</summary>
    public KillPacket LastKill { get; private set; }

    /// <summary>Animal props that burst into gibs on this PC since the game started.</summary>
    public int AnimalsGibbed { get; private set; }

    /// <summary>
    /// Both sides: animal prop <paramref name="i"/> broke: gibs (seeded from the break event, so every PC throws the same
    /// burst), a squelch, and a line in the local log (no kill feed, no XP: it's only a pigeon).
    /// </summary>
    private void OnAnimalGibbed(int i, in PropInstance p, Vector3 from, PropDamage kind)
    {
        var n = Gibs.SpawnProp(p, from, kind, Gibs.SeedFor(i, from));
        AnimalsGibbed++;
        Audio.Effect3D(Gibs.Mode == GoreMode.Off ? Sfx.BreakSoft : Sfx.GibSquelch, p.Bounds.Center, 0.95f);
        Console.WriteLine($"[gibs] {p.Def.Name} went splat ({n} gibs, {kind}). It had a family, probably.");
    }

    /// <summary>Self-test: brings back props placed with "respawn" whose time has come (the server's per-tick check).</summary>
    public void SelfTestTickPropRespawns(double now) => ServerTickPropRespawns(now);
}
