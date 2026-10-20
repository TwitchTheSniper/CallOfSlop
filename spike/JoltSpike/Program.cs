// The game project (CallOfSlop.csproj) compiles every .cs under the repo, so the spike's code only exists
// when JOLT_SPIKE is defined (JoltSpike.csproj defines it).
#if JOLT_SPIKE
using JoltSpike;

// Usage: JoltSpike <section>   (a = basic world, b = character, c = determinism + movement perf,
//                                d = ray/build perf, e = lag compensation, f = glTF + scene graph)
// Each section is its own process run in CI so a native crash only takes out one section,
// and each gets its own budget of 10 notice annotations.
var section = args.Length > 0 ? args[0] : "a";
Report.Open(section);
Report.Log($"JoltSpike section {section} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} " +
           $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, .NET {Environment.Version}, {Environment.ProcessorCount} cpus");
try
{
    switch (section)
    {
        case "a": TestsWorldAndMovement.Basic(); break;
        case "b": TestsWorldAndMovement.Character(); break;
        case "c": TestsDeterminism.Determinism(); break;
        case "d": TestsPerfAndLagComp.Perf(); break;
        case "e": TestsPerfAndLagComp.LagComp(); break;
        case "f": TestsAssets.Assets(); break;
        default: Report.Check("section", false, $"unknown section {section}"); break;
    }
}
catch (Exception e)
{
    Report.Check("exception", false, $"{e.GetType().Name}: {e.Message} @ {e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
}
Report.Close();
return Report.Failures;
#endif
