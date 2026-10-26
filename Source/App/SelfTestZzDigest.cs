using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
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


    private static void CheckLoadDigestDump()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "loaddigest.txt");
        using var w = new StreamWriter(path, false);
        var errors = new List<string>();
        var t0 = Stopwatch.GetTimestamp();
        var (maps, _) = Maps.LoadFolder(Maps.Folder, errors);
        w.WriteLine($"LoadFolder cold serial {Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms, {maps.Length} maps");
        t0 = Stopwatch.GetTimestamp();
        (maps, _) = Maps.LoadFolder(Maps.Folder, errors);
        w.WriteLine($"LoadFolder warm serial {Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms");
        foreach (var m in maps)
            w.WriteLine($"{m.SourceFile!.Replace('\\', '/')}\t{MapDigest(m):x16}\t{m.Id}");
    }
}
