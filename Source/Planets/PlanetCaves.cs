using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Caves in a planetoid's ground (docs/planets/PLANETS.md "Caves"). A <c>"cave"</c> in the features list doesn't shape the
/// ground's heights (a height field has no roofs): it is dug, with the ground drill's own scoops, when a fresh world starts
/// on the map (<see cref="Game"/>'s <c>SandboxCaves</c>), so it saves, reaches a joiner and can be dug further like any
/// other dug ground. An old world gets none: its ground log is its own.
/// <para>
/// A cave is a chamber under <c>lat</c>, <c>lon</c> (its top <c>cover</c> m under the ground there) and a tunnel out of it
/// along <c>bearing</c>, climbing <c>climb</c> degrees, wandering up to <c>wander</c> m either side, until it comes out of
/// the ground: the mouth is found, not given, so a cave set under a ridge opens on its flank wherever the flank is. Where a
/// tunnel has less than <see cref="TerrainEdits.MinRoof"/> over it the roof falls in, so it opens as a cut into the slope.
/// A tunnel that never comes out within <see cref="MaxLength"/> m is sealed (dig to find it).
/// </para>
/// </summary>
public static class PlanetCaves
{
    /// <summary>The longest a tunnel runs looking for daylight (metres).</summary>
    public const float MaxLength = 320;

    /// <summary>Tunnel past its mouth: the cut into the slope (metres).</summary>
    private const float PastMouth = 4;

    /// <summary>A cave as it will be dug: the scoops in order, where it comes out (or null: sealed) and how long the tunnel is.</summary>
    public readonly record struct Plan(List<DigOp> Ops, Vector3? Mouth, float Length, Vector3 Chamber);

    /// <summary>The caves among a planetoid's features.</summary>
    public static IEnumerable<TerrainFeature> Of(Planetoid p) => p.Features.Where(f => f.Type == "cave");

    /// <summary>The cave's scoops, worked out from the planetoid's own heights (the same on every machine).</summary>
    public static Plan Dig(Planetoid p, TerrainFeature f)
    {
        var r = (float)f.Num("radius", 2.6);
        var chamber = (float)f.Num("chamber", 8);
        var cover = (float)Math.Max(f.Num("cover", 14), TerrainEdits.MinRoof + 1);
        var climb = (float)(Math.Tan(f.Num("climb", 8) * Math.PI / 180));
        var wander = (float)Math.Abs(f.Num("wander", r * 2));
        var seed = (int)f.Num("seed", 1);
        double lat = f.Num("lat", 0) * Math.PI / 180, lon = f.Num("lon", 0) * Math.PI / 180, b = f.Num("bearing", 0) * Math.PI / 180;
        var up = new Vector3((float)(Math.Cos(lat) * Math.Sin(lon)), (float)Math.Sin(lat), (float)(Math.Cos(lat) * Math.Cos(lon)));
        // East along increasing longitude, north towards the +Y pole (at a pole, -Z north, as the features do).
        var east = new Vector3(up.Z, 0, -up.X);
        east = east.LengthSquared() < 1e-12f ? Vector3.UnitX : Vector3.Normalize(east);
        var north = Vector3.Cross(up, east);
        var way = Vector3.Normalize(east * (float)Math.Sin(b) + north * (float)Math.Cos(b));
        var side = Vector3.Cross(up, way);
        var radius = (float)p.Radius;
        float Ground(Vector3 dir) => (float)p.HeightAt(dir.X, dir.Y, dir.Z);
        Vector3 World(Vector3 dir, float alt) => p.Center + Vector3.Normalize(dir) * (radius + alt);

        var ops = new List<DigOp>();
        var g0 = Ground(up);
        var floor = g0 - cover - chamber * 2; // (the chamber's floor; the tunnel leaves from it)
        var mid = floor + chamber;
        // The chamber: a squashed dome of balls round its middle, wider than tall.
        if (chamber > 0)
        {
            var c0 = World(up, mid);
            ops.Add(new DigOp(DigKind.Scoop, c0, chamber));
            for (var k = 0; k < 5; k++)
            {
                var a = k * MathF.Tau / 5 + seed;
                var off = (way * MathF.Cos(a) + side * MathF.Sin(a)) * chamber * 0.7f;
                ops.Add(new DigOp(DigKind.Scoop, World(up * radius + off, mid - chamber * 0.35f), chamber * 0.6f));
            }
        }
        // The tunnel: a ball every 0.6 of its radius, climbing out until its roof is in daylight.
        var step = r * 0.6f;
        var alt = floor + r;
        Vector3? mouth = null;
        var s = chamber * 0.6f;
        var past = 0f;
        for (; s < MaxLength; s += step)
        {
            var sway = wander * (float)Planetoid.Noise3(s / 30.0, seed * 0.37, 0.5, seed + 77);
            var dir = Vector3.Normalize(up * radius + way * s + side * sway);
            alt += step * climb;
            ops.Add(new DigOp(DigKind.Scoop, World(dir, alt), r));
            if (mouth == null && Ground(dir) <= alt + r * 0.5f)
                mouth = World(dir, Ground(dir));
            if (mouth != null && (past += step) >= PastMouth)
                break;
        }
        return new Plan(ops, mouth, s, World(up, mid));
    }
}
