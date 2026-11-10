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
    private void AddOutcrops(Vector3 cam)
    {
        var left = MaxDrawnOutcrops;
        for (var di = 0; di < _deposits.Count && left > 0; di++)
        {
            var d = _deposits[di];
            var kind = _table.Kinds[d.Kind];
            var r = d.Rx * 1.25f;
            if (kind.Surface || kind.OutcropSpacing <= 0 || MathF.Abs(d.X - cam.X) > DrawRadius + r || MathF.Abs(d.Z - cam.Z) > DrawRadius + r)
                continue;
            AddStreak(di, d, kind, ref left);
        }
    }

    private void AddStreak(int di, in Deposit d, ResourceKind kind, ref int left)
    {
        var spots = (int)(d.Rx * 2 * StreakEnd / kind.OutcropSpacing);
        var wobble = d.Rz * 0.35f;
        var width = MathF.Min(d.Rz * 0.3f, 7f);
        for (var i = 0; i < spots; i++)
        {
            var h = Mix(Mix(_seed ^ 0xA17C0DEu, (uint)di), (uint)i);
            if (Unit(ref h) >= ClusterKeep)
                continue;
            var u = (i + Unit(ref h)) / spots * 2 * StreakEnd - StreakEnd;
            var v = wobble * MathF.Sin(u * 2.6f + d.Phase) + (Unit(ref h) - 0.5f) * width;
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
                if (MathF.Abs(x) > _halfX - 2 || MathF.Abs(z) > _halfZ - 2)
                    continue;
                if (_planet != null)
                {
                    _drawn.Add(new FoliageInstance(FoliageType.Outcrop, SphereSpot(d.Face, x, z, size), size, yaw, rand, kind.OutcropTint * shade));
                    left--;
                    continue;
                }
                if (_blocked.Contains(TreeKey(Lattice(x), Lattice(z))))
                    continue;
                _drawn.Add(new FoliageInstance(FoliageType.Outcrop, new Vector3(x, _map.GroundHeight(x, z) - size * Sink, z), size, yaw, rand, kind.OutcropTint * shade));
                left--;
            }
        }
    }
}
