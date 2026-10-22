using System.Numerics;
using Brutal.GlfwApi;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Decal drawing (see <see cref="DecalRenderer"/>) and its smoke-test views.</summary>
public static unsafe partial class Program
{
    private static readonly DecalRenderer DecalGfx = new();

    // ---- Smoke views (part of the props step, Program.Props.cs) --------------------------------------------------
    // "decals": the Decal Test map's baked decals from above. "decals_impacts": the same map at eye height after a burst
    // of 1200 impacts sprayed from 20 pretend players over the walls and floor in view (more than the High cap, so the
    // ring recycles and the per-player cap fades), then one "[smoke] decals" line with the decal pass's cost.

    /// <summary>Sprays impacts from the eye into a cone around the target (no session needed: straight into the pool).</summary>
    private static void SmokeDecalImpacts(Map map, Vector3 eye, Vector3 target)
    {
        ImpactDecals.Configure(_settings.ImpactDecals == ImpactDecalQuality.Off ? ImpactDecalQuality.High : _settings.ImpactDecals);
        var pool = ImpactDecals.Pool;
        var now = Glfw.Time;
        pool.Clear(map, now);
        var fwd = Vector3.Normalize(target - eye);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Cross(right, fwd);
        var rng = new DecalAtlas.Rng(1234);
        var placed = 0;
        for (var i = 0; i < 1200; i++)
        {
            var dir = Vector3.Normalize(fwd + right * (rng.Next() - 0.5f) * 1.4f + up * (rng.Next() - 0.5f) * 0.9f);
            if (!World.Query.Raycast(eye, dir, 120f, QueryFilter.Shots, out var hit))
                continue;
            if (ImpactDecals.Place(map, hit, dir, (byte)(i % 20), now + i * 0.0001, pellet: i % 7 == 0))
                placed++;
        }
        Console.WriteLine($"[smoke] decals: sprayed 1200 shots, {placed} impacts placed, {pool.Showing()} showing / {pool.Capacity} cap, "
                          + $"merged {pool.Merged}, popped {pool.Popped}, player-capped {pool.PlayerFaded}");
    }

    /// <summary>One "[smoke] decals" line for the view just measured.</summary>
    private static void LogDecalSmoke(string view)
    {
        var w = DecalGfx.Window;
        var mesh = DecalGfx.Uploaded;
        Console.WriteLine($"[smoke] decals {view} ({World.Map.Name}): {mesh?.Placed ?? 0} map decals, {mesh?.Triangles ?? 0} triangles, {w.AvgChunks:0.0} chunks visible, "
            + $"{w.AvgImpacts:0} impact slots drawn, {w.AvgDraws:0.0} draw calls, decals CPU {w.AvgCpuMs:0.000} ms, GPU {(w.AvgGpuMs >= 0 ? $"{w.AvgGpuMs:0.000} ms" : "n/a")} "
            + $"(target under 0.1 ms), setting {_settings.ImpactDecals}, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
    }
}
