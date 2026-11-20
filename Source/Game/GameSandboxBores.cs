using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What every PC does with a bore and with scrap (docs/sandbox/design/capital-battles.md; ConstructBores.cs, ConstructBreakUp.cs):
/// the holes marked on its own copy of the constructs, sparks where the round came out into the open, a short hot streak along
/// its way through; a small part shot off a capital-sized body bursts into pieces drawn only (the prop debris pool).
/// </summary>
public sealed partial class Game
{
    public int ConstructBoresSeen, ConstructScrapSeen; // (counts, for a check)

    private void BoreFx(in CFireEvent e, double now)
    {
        if (e.Kind == CFireKind.Scrap)
        {
            ConstructScrapSeen++;
            var half = Vector3.Max(e.Half, new Vector3(0.05f));
            var size = MathF.Max(half.X, MathF.Max(half.Y, half.Z));
            PropDebris.SpawnBox(new Box(e.At, half), e.At, PropDamage.Explosion, (int)(e.At.X * 131 + e.At.Z * 977) ^ (int)(PropDebris.Spawned & 0xFFFF),
                Math.Clamp((int)(size * 10), 8, 24), 0.05f, MathF.Min(0.6f, 0.1f + size * 0.2f), 5f, new Vector3(0.3f, 0.31f, 0.33f), new Vector3(0.52f, 0.5f, 0.47f));
            Audio.Effect3D(Sfx.ImpactMetal, e.At, 1f);
            return;
        }
        ConstructBoresSeen++;
        Span<Vector3> exits = stackalloc Vector3[8];
        var n = ConstructBores.Mark(Constructs, e.At, e.To, e.Radius, now, exits);
        for (var k = 0; k < n && k < 6; k++)
            PropDebris.SpawnBox(new Box(exits[k], new Vector3(0.05f)), exits[k] - Vector3.Normalize(e.To - e.At) * 0.3f, PropDamage.Bullet, (int)(exits[k].X * 131 + exits[k].Z * 977) ^ k,
                10, 0.015f, 0.05f, 6f, new Vector3(1f, 0.6f, 0.2f), new Vector3(1f, 0.95f, 0.65f)); // (spall: hot sparks out of the far side)
        if (Vector3.DistanceSquared(e.At, e.To) > 0.25f)
            Tracers.Add(new Tracer { From = e.At, To = e.To, Color = new Vector3(0.75f, 0.85f, 1f), Width = MathF.Max(0.05f, e.Radius * 0.6f), Born = now }); // (the hot streak through)
        Audio.Effect3D(Sfx.ImpactMetal, e.At, 1f);
    }
}
