using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What players see of their shots into the far crowd (docs/zombies/MASS-HORDE.md, "Combat"): blood where a round
/// that hit ends in the crowd, the shooter's own shots predicted against the crowd as drawn (the tracer stops on the
/// zombie, the hit marker shows at once), and a kill sinks the drawn zombie nearest the hit. Visual only, on both
/// sides: the server's hits are its own (ZombiesCrowd.cs), and nothing here goes on the network.
/// </summary>
public sealed partial class Game
{
    /// <summary>The crowd agents the last frame drew (set by CrowdView.Prepare; null headless or off crowd maps).</summary>
    public CrowdAgents? DrawnCrowd;

    /// <summary>Blood bursts put out on crowd hits (self-test).</summary>
    public long CrowdBloodFx;

    // Client: the drawn agent the latest own shot was predicted to hit (its number; 0: none), sunk if the server says it killed.
    private uint _crowdPredictId;

    /// <summary>Is <paramref name="at"/> in the drawn crowd (a cell with zombies, at body height)?</summary>
    private bool InDrawnCrowd(Vector3 at)
    {
        if (DrawnCrowd is not { } a || a.WantAt(at.X, at.Z) <= 0)
            return false;
        var y = at.Y - a.GroundAt(at.X, at.Z);
        return y > -0.3f && y < HordeCrowd.BodyHeight + 0.4f;
    }

    /// <summary>A burst of blood at <paramref name="at"/> (a round into the crowd).</summary>
    private void CrowdBlood(Vector3 at, Vector3 dir, uint seed)
    {
        CrowdBloodFx++;
        for (var k = 0; k < 7; k++)
        {
            var h = HordeCrowd.Hash(seed + (uint)k * 0x9E3779B9u);
            var spread = new Vector3((h & 255) / 255f - 0.5f, (h >> 8 & 255) / 255f * 0.8f, (h >> 16 & 255) / 255f - 0.5f) * 2.2f;
            var shade = 0.35f + (h >> 24) / 255f * 0.25f;
            FrenzyParticles.Spark(at - dir * 0.15f, -dir * 1.2f + spread, new Vector3(shade, 0.02f, 0.02f), 0.05f, 0.45f, 9f, 2f, 0.02f, 0.95f);
        }
    }

    /// <summary>
    /// Both sides, for a shot's ends (Msg.Shot, or the host's own resolve): blood at each end that lies in the drawn
    /// crowd if the shot hit, and on a kill the drawn zombie nearest those ends sinks (once a shot).
    /// </summary>
    private void CrowdShotFx(ReadOnlySpan<Vector3> ends, Vector3 from, int flags)
    {
        if ((flags & 1) == 0 || DrawnCrowd is not { } a || a.Count == 0)
            return;
        var best = -1;
        var bestD = float.MaxValue;
        for (var i = 0; i < ends.Length; i++)
        {
            var end = ends[i];
            if (!InDrawnCrowd(end))
                continue;
            var dir = end - from;
            dir = dir.LengthSquared() > 1e-6f ? Vector3.Normalize(dir) : Vector3.UnitZ;
            CrowdBlood(end, dir, (uint)(end.X * 131) ^ (uint)(end.Z * 977) ^ (uint)i);
            if ((flags & 4) != 0 && a.NearestStanding(end.X, end.Z, 3f) is var j and >= 0)
            {
                var d = (a.X[j] - end.X) * (a.X[j] - end.X) + (a.Z[j] - end.Z) * (a.Z[j] - end.Z);
                if (d < bestD)
                    (bestD, best) = (d, j);
            }
        }
        if (best >= 0)
            a.Kill(best);
    }

    /// <summary>
    /// Client, per ray of an own shot: the drawn crowd zombie it hits before <paramref name="dist"/> (shortened to it),
    /// with blood there. Returns whether it hit one.
    /// </summary>
    private bool CrowdRayDrawn(Vector3 o, Vector3 d, ref float dist)
    {
        if (!CrowdOn || DrawnCrowd is not { Count: > 0 } a)
            return false;
        var i = a.RayHit(o, d, dist, out var t, HordeCrowd.BodyRadius);
        if (i < 0 || t >= dist)
            return false;
        dist = t;
        if (_crowdPredictId == 0)
            _crowdPredictId = a.Id[i];
        CrowdBlood(o + d * t, d, a.Id[i] * 2654435761u);
        return true;
    }

    /// <summary>Client: the server's answer to an own shot: a kill sinks the drawn zombie it was predicted to hit.</summary>
    private void CrowdOwnShotConfirmed(byte flags)
    {
        var id = _crowdPredictId;
        _crowdPredictId = 0;
        if (id == 0 || (flags & 4) == 0 || DrawnCrowd is not { } a)
            return;
        var i = a.IndexOfId(id);
        if (i >= 0 && a.Standing(i))
            a.Kill(i);
    }
}
