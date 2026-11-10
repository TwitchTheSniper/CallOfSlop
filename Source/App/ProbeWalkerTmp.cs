// THROWAWAY PROBE (feat/walker-interior): remove before the final push.
using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

public static class ProbeT
{
    public static bool On;
    public static readonly double[] Ms = new double[48];
    public static readonly string?[] Names = new string?[48];
    private static long _t;
    public static void Start() { if (On) _t = System.Diagnostics.Stopwatch.GetTimestamp(); }
    public static void Mark(int k, string name)
    {
        if (!On) return;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        Ms[k] += (now - _t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Names[k] = name;
        _t = now;
    }
}

public static unsafe partial class Program
{
    private static int ProbeWalker(string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var file = Flag("--probe-walker") ?? Path.Combine(VehicleRegistry.RepoBlueprints(), "TitanWalkerFitted.json");
        var walkS = float.Parse(Flag("--walk") ?? "6", System.Globalization.CultureInfo.InvariantCulture);
        var bp = ConstructBuild.ParseBlueprint(File.ReadAllText(file), Path.GetFileName(file));
        var map = Maps.Find("Proving Ground") ?? Maps.Find("Construct Yard")!;
        World.SetMap(map);
        World.OpenSky = true;
        var q = World.Query;
        var w = new ConstructWorld();
        var g0 = ConstructWorld.Ground(q, 0, 0);
        var c = ConstructBuild.BuildOnGround(w, bp, g0 + new Vector3(0, 60, 0), 0, out var why);
        if (c == null) { Console.WriteLine($"probe: no build: {why}"); return 1; }
        c.Rebuild();
        c.Pos = c.PrevPos = c.NetPos = c.Pos - new Vector3(0, 59.5f, 0);
        c.Charge = c.ChargeCapacity;
        c.Wake();
        var helm = Enumerable.Range(0, c.PartCount).FirstOrDefault(i => c.DefOf(i).Name == "Bridge Helm", c.DriveSeat);
        Console.WriteLine($"probe: {bp.Name}: {c.PartCount} parts, {c.JointCount} joints, legs {c.LegCount}, mass {c.Mass / 1000:0} t, drive seat {c.DriveSeat} helm {helm} ({c.DefOf(helm).Name}), charge {c.ChargeCapacity:0} kJ, bank {Enumerable.Range(0, c.PartCount).Count(i => c.DefOf(i).Name.Contains("Battery Bank"))}");
        var times = new List<double>();
        var sw = new System.Diagnostics.Stopwatch();
        void Run(string phase, float secs, Buttons b)
        {
            times.Clear();
            Array.Clear(ProbeT.Ms);
            var p0 = c.Pos;
            var yaw0 = Yaw(c.Rot);
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            var n = (int)(secs * World.TickRate);
            var prev = new double[48];
            var spikes = 0;
            for (var t = 0; t < n; t++)
            {
                if (b != Buttons.None)
                {
                    c.Parts[helm].Keys = (ushort)((ushort)b | CPart.Occupied);
                    c.Driver = 0;
                    c.DriverSeat = helm;
                    ConstructEdits.DriveInput(c, b, World.TickDt);
                }
                else if (c.Driver != 255)
                {
                    c.Parts[helm].Keys = CPart.Occupied;
                    ConstructEdits.DriveInput(c, b, World.TickDt);
                }
                sw.Restart();
                ProbeT.On = true;
                w.Step(q, World.TickDt);
                ProbeT.On = false;
                times.Add(sw.Elapsed.TotalMilliseconds);
                var tk = sw.Elapsed.TotalMilliseconds;
                if (tk > 2.5 && spikes < 12)
                {
                    spikes++;
                    Console.WriteLine($"  spike t{t} {tk:0.0} ms gc{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}: " + string.Join(", ", Enumerable.Range(0, 48).Where(k => ProbeT.Ms[k] - prev[k] > 0.3).Select(k => $"{ProbeT.Names[k]} {ProbeT.Ms[k] - prev[k]:0.0}")));
                }
                Array.Copy(ProbeT.Ms, prev, 48);
            }
            var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            var s = times.OrderBy(x => x).ToList();
            var g = ConstructWorld.Ground(q, c.Pos.X, c.Pos.Z);
            var mv = c.Pos - p0;
            Console.WriteLine($"probe {phase}: {secs:0} s median {s[s.Count / 2]:0.00} p95 {s[(int)(s.Count * 0.95)]:0.00} worst {s[^1]:0.00} ms (2nd {s[^2]:0.00}); moved {new Vector2(mv.X, mv.Z).Length() / secs:0.00} m/s, turned {(Yaw(c.Rot) - yaw0) * 180 / MathF.PI:0} deg, deck {c.Pos.Y - g.Y:0.0} m, charge {c.Charge:0} kJ, alloc {alloc / 1024} KB, asleep {c.Asleep}");
            var tot = ProbeT.Ms.Sum();
            Console.WriteLine("  stages (ms per tick): " + string.Join(", ", Enumerable.Range(0, 48).Where(k => ProbeT.Ms[k] / n > 0.05).OrderByDescending(k => ProbeT.Ms[k]).Select(k => $"{ProbeT.Names[k]} {ProbeT.Ms[k] / n:0.00}")));
        }
        static float Yaw(Quaternion r) { var f = Vector3.Transform(Vector3.UnitX, r); return MathF.Atan2(-f.Z, f.X); }
        Run("settle", 4, Buttons.None);
        Run("stand", 4, Buttons.None);
        c.Charge = c.ChargeCapacity;
        Run("walk", walkS, Buttons.Forward);
        c.Charge = c.ChargeCapacity;
        Run("turn", 4, Buttons.Right);
        Run("stop", 3, Buttons.None);
        return 0;
    }
}
