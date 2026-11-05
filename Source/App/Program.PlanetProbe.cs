using System.Numerics;
namespace CallOfSlop;
// THROWAWAY: remove before finishing.
public static partial class Program
{
    private static void Log(string s) => File.AppendAllText("/tmp/claude-0/-home-claude/ef3c3b89-0692-5695-a5a6-8e1bee51b395/scratchpad/probe.txt", s + "\n");
    private static int PlanetProbe()
    {
        var map = Maps.All.First(m => m.Planetoid != null);
        var pl = map.Planetoid!;
        var r = new PlanetRenderer(headless: true);
        r.Reset(pl);
        Log($"min {pl.MinHeight} max {pl.MaxHeight} levels {pl.Levels} cell {pl.Cell} far {map.FarPlane} fog {map.FogStart}-{map.FogEnd}");
        static Vector3 D(double lat, double lon) { var a = lat * Math.PI / 180; var o = lon * Math.PI / 180; return new Vector3((float)(Math.Cos(a) * Math.Sin(o)), (float)Math.Sin(a), (float)(Math.Cos(a) * Math.Cos(o))); }
        var spots = new (string, Vector3)[] { ("pole", D(89.9, 22)), ("mt2", D(62, 200)), ("mid45", D(45, 45)), ("corner", D(35.26, 45)), ("equator", D(0, 90)) };
        var alts = new[] { 1.7f, 200f, 2500f };
        foreach (var (name, dir) in spots)
        foreach (var alt in alts)
        {
            var eye = pl.Center + dir * (pl.SurfaceRadius(dir, out _) + alt);
            PlanetRenderer.LocalAxes(pl, eye, out var right, out var up, out var south);
            int worst = 0, best = int.MaxValue; string ws = ""; var lv = "";
            foreach (var pitch in new[] { 0f, -0.25f, -0.5f, -1.2f })
            for (var y = 0; y < 4; y++)
            {
                var yaw = y * MathF.PI / 2 + 0.4f;
                var fwd = (MathF.Cos(yaw) * south + MathF.Sin(yaw) * right) * MathF.Cos(pitch) + up * MathF.Sin(pitch);
                var view = Matrix4x4.CreateLookAt(eye, eye + fwd * 100f, up);
                var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.6f, 16f / 9f, 0.05f, map.FarPlane);
                Span<Vector4> planes = stackalloc Vector4[6];
                TerrainRenderer.FrustumPlanes(view * proj, planes);
                for (var k = 0; k < 600; k++) { r.Update(eye, planes); if (r.Settled) break; Thread.Sleep(1); }
                r.Update(eye, planes); r.Update(eye, planes);
                var inst = r.Instances; var n = inst.Length / 8;
                if (n > worst)
                {
                    worst = n; ws = $"pitch {pitch:0.0} yaw {y}";
                    var c = new int[16];
                    for (var i = 0; i < n; i++) c[(int)inst[i * 8 + 6]]++;
                    lv = string.Join(" ", c.Take(pl.Levels));
                }
                best = Math.Min(best, n);
            }
            Log($"{name,-8} alt {alt,6:0.0}  instances {best,4} .. {worst,4}  tris(worst) {worst * PlanetRenderer.TrianglesPerInstance,8}  worst {ws}  by level {lv}");
        }
        return 0;
    }
}
