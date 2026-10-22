using System.Numerics;

namespace CallOfSlop;

/// <summary>The squad side of a bot's brain (Insane bots in team modes; null squad otherwise).</summary>
public sealed partial class BotBrain
{
    /// <summary>The squad this bot belongs to (server only), or null.</summary>
    public BotSquad? Squad;

    public SquadRole Role;

    /// <summary>The planner's current order for this bot.</summary>
    public SquadOrder Order;

    /// <summary>Flanking unseen: don't give the position away by shooting from a bad angle (BotShoot checks it).</summary>
    public bool HoldFire;

    internal uint SquadLifeTick = uint.MaxValue; // the life the squad-side per-life setup was done for
    internal double SuppressLosAt = -100; // when the line to the suppression point was last checked
    internal bool SuppressLos;
    internal bool SuppressBlind; // the line to the spot is hidden by foliage or corn: suppressive fire goes in blind and wide
    internal Vector3 SuppressAim; // the point suppressive fire goes at (the spot, or just above it over low cover)

    public bool Insane => Skill == BotDifficulty.Insane;

    /// <summary>
    /// The squad's extra route cost for one edge (added to <see cref="EdgePenalty"/>): on a flank route, nodes the
    /// target's last known position can see cost a lot more (checked with a ray cast, at most a few dozen per search,
    /// remembered for the whole plan), and so do nodes within 9 m of it; on an assault route, nodes a squadmate's route
    /// already uses cost a little more, so the squad doesn't all pile through the same door.
    /// </summary>
    internal float SquadEdgePenalty(int to, float cost)
    {
        var s = Squad!;
        var g = SearchGraph!;
        if (s.ScratchGraph != g)
            return 0;
        var extra = 0f;
        if (FlankWidth > 0 && s.ExposureOn)
        {
            byte e;
            if (s.ExposureStamp[to] == s.ExposureGen)
                e = s.Exposure[to];
            else if (s.ExposureRays > 0)
            {
                s.ExposureRays--;
                var node = g.Nodes[to] + new Vector3(0, 1.1f, 0);
                var d = node - s.ExposureEye;
                var len = d.Length();
                e = len < 45f && (len < 0.01f || World.Query.Raycast(s.ExposureEye, d / len, len, QueryFilter.All) >= len - 0.1f) ? (byte)2 : (byte)1;
                s.Exposure[to] = e;
                s.ExposureStamp[to] = s.ExposureGen;
            }
            else
                e = 0;
            if (e == 2)
                extra += cost * 4f;
            // And keep well off the target itself: going round means going round, not brushing past them.
            var q = g.Nodes[to];
            var dx = q.X - s.ExposureEye.X;
            var dz = q.Z - s.ExposureEye.Z;
            var near = MathF.Sqrt(dx * dx + dz * dz);
            if (near < 9f)
                extra += cost * 5f * (1f - near / 9f);
        }
        if (s.ExposureOn && s.ClaimStamp[to] == s.ClaimGen && s.ClaimBy[to] != SquadSlot)
            extra += cost * 1.2f;
        return extra;
    }

    internal byte SquadSlot = byte.MaxValue;
}
