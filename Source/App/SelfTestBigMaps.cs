using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Map loading cost (docs/perf/STREAMING.md): a digest of everything a map load produces, so a faster load path can be
/// proven to give exactly the same maps.
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// A 64-bit digest of what loading a map built: every box (exact bits) and colour, every spawn and waypoint (as
    /// validated), every prop instance, the plant count, the fingerprint and its parts. Two loads with the same digest
    /// built the same map.
    /// </summary>
    internal static ulong MapDigest(Map m)
    {
        var h = 14695981039346656037ul;
        void U(uint v)
        {
            for (var i = 0; i < 4; i++)
            {
                h ^= (v >> (i * 8)) & 0xFF;
                h *= 1099511628211ul;
            }
        }
        void F(float f) => U(BitConverter.SingleToUInt32Bits(f));
        void V(Vector3 v)
        {
            F(v.X);
            F(v.Y);
            F(v.Z);
        }
        foreach (var ch in m.Name)
            U(ch);
        U(m.ContentHash);
        U(m.PropHash);
        U(m.FoliageHash);
        U((uint)m.Solid.Count);
        foreach (var b in m.Solid)
        {
            V(b.Center);
            V(b.Half);
        }
        foreach (var c in m.Colors)
            V(c);
        U((uint)m.Spawns.Count);
        foreach (var s in m.Spawns)
            V(s);
        U((uint)m.Waypoints.Count);
        foreach (var w in m.Waypoints)
            V(w);
        U((uint)m.PropInstances.Count);
        foreach (var p in m.PropInstances)
        {
            foreach (var ch in p.Def.Name)
                U(ch);
            V(p.Position);
            F(p.Yaw);
            F(p.Scale);
            F(p.Rand);
            U((uint)p.SolidStart);
            U((uint)p.SolidCount);
        }
        U((uint)m.FoliageInstances.Length);
        U((uint)m.ClutterStart);
        return h;
    }

    // TEMPORARY (removed before the branch is finished): dumps every map's digest and load time.
    private static void CheckLoadDigestDump()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "loaddigest.txt");
        using var w = new StreamWriter(path, false);
        var t0 = Stopwatch.GetTimestamp();
        var errors = new List<string>();
        var (maps, _) = Maps.LoadFolder(Maps.Folder, errors, parallel: false);
        w.WriteLine($"LoadFolder {Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms, {maps.Length} maps, {errors.Count} errors");
        t0 = Stopwatch.GetTimestamp();
        var (par, _) = Maps.LoadFolder(Maps.Folder, errors, parallel: true);
        w.WriteLine($"LoadFolder parallel {Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms, {par.Length} maps, {Environment.ProcessorCount} cores, same: {par.Length == maps.Length && par.Zip(maps).All(x => MapDigest(x.First) == MapDigest(x.Second) && x.First.Id == x.Second.Id)}");
        foreach (var m in maps)
        {
            var file = Path.Combine(Maps.Folder, m.SourceFile!);
            var rel = m.SourceFile!.Replace('\\', '/');
            var packDir = rel.Contains('/') ? Path.Combine(Maps.Folder, rel[..rel.IndexOf('/')]) : "";
            var text = File.ReadAllText(file);
            var a = Stopwatch.GetTimestamp();
            var again = MapFile.Load(text, m.SourceFile!, packDir);
            var loadMs = Stopwatch.GetElapsedTime(a).TotalMilliseconds;
            a = Stopwatch.GetTimestamp();
            again.Validate();
            var validateMs = Stopwatch.GetElapsedTime(a).TotalMilliseconds;
            w.WriteLine($"{rel}\t{MapDigest(m):x16}\t{MapDigest(again):x16}\t{m.ContentHash:x8}\tload {loadMs:0}\tvalidate {validateMs:0}\tprops {m.PropInstances.Count}\tboxes {m.Solid.Count}");
        }
        Line($"wrote {path}");
    }
}
