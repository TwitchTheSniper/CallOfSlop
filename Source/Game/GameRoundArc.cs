using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Sandbox hand-held built gun's rounds fall, as a mounted gun's do (ConstructWeapons.Round): a shot stays a hitscan, resolved at once,
/// but along its arc. <see cref="ArcShot"/> walks a few straight pieces of the arc and hands back the first that meets something; the
/// shot's usual code then runs on that one piece. Stock guns, beams, flames and every other mode keep their straight line.
/// </summary>
public sealed partial class Game
{
    private const int HandArcPieces = 8; // (at most this many casts a pellet, each short enough that the arc stays smooth: nothing allocates)

    /// <summary>The speed this gun's round falls at here, or 0: a straight shot.</summary>
    private float ArcSpeed(WeaponDef def) => SandboxOn ? PowderCharge.HandSpeed(def) : 0f;

    /// <summary>
    /// Moves a shot's ray onto the piece of its arc that meets something (or the last), and says how far it had flown by then. A fast round
    /// reaches further: its reach grows with its charge. <paramref name="targets"/>: the server's rewound players; null on a client, which
    /// uses the players as it sees them.
    /// </summary>
    private void ArcShot(float speed, Player shooter, List<(Player P, Box Hitbox, HistoryEntry At)>? targets, ref Vector3 o, ref Vector3 d, ref float reach, out float flown)
    {
        flown = 0;
        if (speed <= 0)
            return;
        var total = reach * MathF.Max(1f, speed / ConstructWeaponParts.Current.RoundSpeed);
        var seg = total / HandArcPieces;
        var t = seg / speed;
        var vel = d * speed;
        var pos = o;
        for (var k = 0; k < HandArcPieces; k++)
        {
            var pull = ConstructShells.GravityAt(pos);
            var chord = vel * t + pull * (0.5f * t * t);
            var len = chord.Length();
            var dir = chord / len;
            vel += pull * t;
            if (k == HandArcPieces - 1 || ArcBlocked(shooter, targets, pos, dir, len))
            {
                o = pos;
                d = dir;
                reach = len;
                return;
            }
            pos += dir * len;
            flown += len;
        }
    }

    private bool ArcBlocked(Player shooter, List<(Player P, Box Hitbox, HistoryEntry At)>? targets, Vector3 pos, Vector3 dir, float len)
    {
        if (World.Query.Raycast(pos, dir, len, QueryFilter.Shots) < len - 1e-3f || Constructs.Raycast(pos, dir, len, out _))
            return true;
        if (targets != null)
        {
            foreach (var t in targets)
                if (t.Hitbox.Raycast(pos, dir, len) != null)
                    return true;
            return false;
        }
        foreach (var other in Players.Values)
            if (other != shooter && other.Alive && World.RayHitsBody(other.RenderPosition, other.RenderStance, other.RenderYaw, pos, dir, len) != null)
                return true;
        return false;
    }
}
