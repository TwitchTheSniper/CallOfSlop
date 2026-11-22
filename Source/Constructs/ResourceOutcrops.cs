using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Underground deposits showing on the surface, so players can find them by eye (docs/sandbox/CONSTRUCTS.md "Gathering"): a
/// long streak of rock clusters down a deposit's long axis, tinted by its kind (<c>Constructs/Resources.json</c>, "outcrop").
/// Worked out from the seed and the deposit like the forest lattice, so they're the same on every peer and nothing is sent;
/// drawn only, and they don't change as a deposit runs out (only the server knows what is left).
/// </summary>
public sealed partial class ResourceField
{
    public const int MaxDrawnOutcrops = 16000;
    private const float StreakEnd = 0.95f; // how far along the deposit's long axis the streak runs (1: its edge)
    private const float ClusterKeep = 0.85f; // the share of spots along the streak that hold a cluster
    private const float ClusterHalf = 3f; // a cluster's rocks lie within this of its middle (metres)
    private const float Sink = 0.18f; // a rock's share of its height in the ground

    /// <summary>Adds the rocks of every underground deposit near the camera to the drawn list.</summary>
    private void AddOutcrops(Vector3 cam, List<FoliageInstance> into)
    {
        var left = MaxDrawnOutcrops;
        for (var di = 0; di < _deposits.Count && left > 0; di++)
        {
            var d = _deposits[di];
            var kind = _table.Kinds[d.Kind];
            var r = d.Rx * 1.25f;
            if (kind.Surface || kind.OutcropSpacing <= 0 || MathF.Abs(d.X - cam.X) > DrawRadius + r || MathF.Abs(d.Z - cam.Z) > DrawRadius + r)
                continue;
            AddStreak(di, d, kind, ref left, into);
        }
    }

    private void AddStreak(int di, in Deposit d, ResourceKind kind, ref int left, List<FoliageInstance> into)
    {
        var spots = (int)(d.Rx * 2 * StreakEnd / kind.OutcropSpacing);
        var wobble = d.Rz * 0.35f;
        var width = MathF.Min(d.Rz * 0.3f, 7f);
        // A planetoid's deposits are big and few (Sean, 2026-11-22): side lanes either side of the streak thicken it, so a
        // deposit reads as a whole field of rocks, and its middle holds a big outcrop with the vein showing (AddCore).
        var lanes = _planet != null ? SideLanes * 2 + 1 : 1;
        var laneGap = MathF.Min(d.Rz * 0.28f, 14f);
        AddCore(di, d, kind, wobble * MathF.Sin(d.Phase), ref left, into);
        for (var lane = 0; lane < lanes; lane++)
        {
            var off = lane == 0 ? 0 : ((lane + 1) / 2) * (lane % 2 == 0 ? -laneGap : laneGap);
            var keep = lane == 0 ? ClusterKeep : SideKeep / ((lane + 1) / 2);
            for (var i = 0; i < spots; i++)
            {
                var h = Mix(Mix(Mix(_seed ^ 0xA17C0DEu, (uint)di), (uint)i), (uint)lane * 0x9E37u);
                if (lane == 0)
                    h = Mix(Mix(_seed ^ 0xA17C0DEu, (uint)di), (uint)i); // (the middle lane as before)
                if (Unit(ref h) >= keep)
                    continue;
                var u = (i + Unit(ref h)) / spots * 2 * StreakEnd - StreakEnd;
                var squeeze = lane == 0 ? 1 : MathF.Sqrt(MathF.Max(0, 1 - u * u)); // (side lanes close in towards the ends)
                var v = wobble * MathF.Sin(u * 2.6f + d.Phase) + (Unit(ref h) - 0.5f) * width + off * squeeze;
                var cx = d.X + u * d.Rx * d.Cos - v * d.Sin;
                var cz = d.Z + u * d.Rx * d.Sin + v * d.Cos;
                if (Strength(d, cx, cz) <= EdgeThin || At(cx, cz, false, false, out _, d.Face) != di)
                    continue; // (past the deposit's wavy edge, or another one is what a drill would find here)
                var rocks = 3 + (int)(Unit(ref h) * 4);
                for (var k = 0; k < rocks && left > 0; k++)
                {
                    var x = cx + (Unit(ref h) * 2 - 1) * ClusterHalf;
                    var z = cz + (Unit(ref h) * 2 - 1) * ClusterHalf;
                    var f = Unit(ref h);
                    var size = kind.OutcropSizeMin + f * f * (kind.OutcropSizeMax - kind.OutcropSizeMin); // (mostly small, a few big)
                    var shade = 0.85f + 0.3f * Unit(ref h);
                    var (yaw, rand) = (Unit(ref h), Unit(ref h));
                    Place(d, kind.OutcropTint * shade, x, z, size, 0, yaw, rand, ref left, into);
                }
            }
        }
    }

    private const int SideLanes = 2; // a planetoid's deposit: lanes of rock clusters either side of its streak
    private const float SideKeep = 0.55f; // the share of spots in the nearest side lanes that hold a cluster (halved per lane further out)
    private const float CoreHalfLength = 9f; // a core outcrop's host rocks lie within this of its middle along the deposit (m)
    private static readonly Vector3 HostRock = new(0.46f, 0.45f, 0.43f); // the weathered rock a core's vein runs through

    /// <summary>
    /// The deposit's middle: a big rocky outcrop of weathered host rock, its kind's colour only hinted at, with the vein's own
    /// chunks breaking out of its flanks along the deposit's long axis (Sean, 2026-11-22: bigger ore signs at a node's middle,
    /// the small boulders kept round it).
    /// </summary>
    private void AddCore(int di, in Deposit d, ResourceKind kind, float v0, ref int left, List<FoliageInstance> into)
    {
        var cx = d.X - v0 * d.Sin;
        var cz = d.Z + v0 * d.Cos;
        if (At(cx, cz, false, false, out _, d.Face) != di)
            return; // (another deposit is what a drill would find here)
        var h = Mix(Mix(_seed ^ 0xC0BE0u, (uint)di), 0x51u);
        var grow = Math.Clamp(d.Rx / 120f, 0.6f, 1.6f); // (a bigger deposit, a bigger outcrop)
        var host = Vector3.Lerp(HostRock, kind.OutcropTint, 0.3f);
        var hosts = 3 + (int)(Unit(ref h) * 3);
        for (var k = 0; k < hosts && left > 0; k++)
        {
            var u = (Unit(ref h) * 2 - 1) * CoreHalfLength * grow;
            var v = (Unit(ref h) * 2 - 1) * 2.5f * grow;
            var size = (1.8f + 2.4f * Unit(ref h)) * grow * (1 - 0.35f * MathF.Abs(u) / (CoreHalfLength * grow)); // (tallest in the middle)
            var shade = 0.85f + 0.25f * Unit(ref h);
            var (yaw, rand) = (Unit(ref h), Unit(ref h));
            Place(d, host * shade, cx + u * d.Cos - v * d.Sin, cz + u * d.Sin + v * d.Cos, size, 0, yaw, rand, ref left, into);
        }
        // The vein: brighter chunks of the kind's own colour in a ragged line through the host rocks, set up their sides.
        var vein = Vector3.Min(kind.OutcropTint * 1.25f, Vector3.One);
        var chunks = 7 + (int)(Unit(ref h) * 6);
        for (var k = 0; k < chunks && left > 0; k++)
        {
            var u = ((k + Unit(ref h)) / chunks * 2 - 1) * (CoreHalfLength + 3) * grow;
            var v = (Unit(ref h) - 0.5f) * 1.6f * grow;
            var size = (0.5f + 0.9f * Unit(ref h)) * grow;
            var lift = Unit(ref h) * 1.6f * grow * (1 - MathF.Abs(u) / ((CoreHalfLength + 3) * grow));
            var (yaw, rand) = (Unit(ref h), Unit(ref h));
            Place(d, vein * (0.9f + 0.2f * Unit(ref h)), cx + u * d.Cos - v * d.Sin, cz + u * d.Sin + v * d.Cos, size, lift, yaw, rand, ref left, into);
        }
    }

    /// <summary>One drawn rock of a deposit, set on the ground (raised <paramref name="lift"/> metres), unless it's off the field, in a planetoid's settlement or under the map's boxes.</summary>
    private void Place(in Deposit d, Vector3 color, float x, float z, float size, float lift, float yaw, float rand, ref int left, List<FoliageInstance> into)
    {
        if (left <= 0 || MathF.Abs(x) > _halfX - 2 || MathF.Abs(z) > _halfZ - 2)
            return;
        if (_planet != null)
        {
            var spot = SphereSpot(d.Face, x, z, size, lift);
            if (NearSpawn(spot))
                return; // (a planetoid's spawns are its settlement: no rocks in the yard)
            into.Add(new FoliageInstance(FoliageType.Outcrop, spot, size, yaw, rand, color));
            left--;
            return;
        }
        if (_blocked.Contains(TreeKey(Lattice(x), Lattice(z))))
            return;
        into.Add(new FoliageInstance(FoliageType.Outcrop, new Vector3(x, _map.GroundHeight(x, z) - size * Sink + lift, z), size, yaw, rand, color));
        left--;
    }

    private const float SpawnClear = 80f; // metres round a planetoid's spawns kept free of drawn outcrops (its settlement)

    private bool NearSpawn(Vector3 p)
    {
        foreach (var s in _map.Spawns)
            if (Vector3.DistanceSquared(s, p) < SpawnClear * SpawnClear)
                return true;
        return false;
    }
}
