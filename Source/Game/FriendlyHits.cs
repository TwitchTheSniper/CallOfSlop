namespace CallOfSlop;

/// <summary>
/// Friendly hit feedback: when your bullet, melee swing or grenade reaches a teammate (team modes, and everyone in Zombies), the
/// server sets <see cref="ShotHitFriendly"/> in the hit flags it already sends (Msg.Shot, Msg.MeleeFx, Msg.Explode) instead of the
/// normal hit bit, since no damage is done. The shooter then sees a cyan no-entry marker with "FRIENDLY" under the crosshair and
/// hears a muted low "bonk" (<see cref="Sfx.FriendlyHit"/>), never the enemy hit tick or the headshot crunch. Free-for-all never
/// sets it (everyone is an enemy). A shot that also hurt an enemy shows the normal marker.
/// </summary>
public sealed partial class Game
{
    /// <summary>Hit flag (Msg.Shot, Msg.MeleeFx, Msg.Explode): it hit a teammate, who took no damage.</summary>
    public const byte ShotHitFriendly = 32;

    /// <summary>The hit marker showing now is a friendly one (see <see cref="HitMarkerUntil"/>).</summary>
    public bool HitMarkerFriendly;

    /// <summary>The flags of the last hit confirmation the local player got (self-test).</summary>
    public byte LastHitFlags;

    /// <summary>How long the friendly marker stays up (a touch longer than the enemy one so it reads).</summary>
    public const double FriendlyMarkerSeconds = 0.3;

    /// <summary>Server: is hitting <paramref name="victim"/> a friendly hit for <paramref name="attacker"/> (a teammate, so no damage)?</summary>
    private bool IsFriendlyHit(Player attacker, Player victim) => attacker != victim && !CanDamage(attacker, victim);

    /// <summary>The shooter's feedback for a hit that only reached teammates: the friendly marker and the muted bonk.</summary>
    private void OnOwnFriendlyHit(double now)
    {
        HitMarkerUntil = now + FriendlyMarkerSeconds;
        HitMarkerHead = false;
        HitMarkerFriendly = true;
        Audio.Effect(Sfx.FriendlyHit, 0.4f); // mixed under the enemy hit sounds
    }
}
