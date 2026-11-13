namespace CallOfSlop;

/// <summary>
/// Caves dug into a planetoid's ground when a fresh world starts on it (Planets/PlanetCaves.cs, docs/planets/PLANETS.md
/// "Caves"): the map's "cave" features, each a chamber and a tunnel out to the daylight. They are ground ops like a
/// player's drill, so they save and reach joiners with the rest of the dug ground. A saved world keeps its own ground log:
/// a world from before the caves gets none.
/// </summary>
public sealed partial class Game
{
    /// <summary>Server, a fresh Sandbox world on a planetoid with caves: every cave dug.</summary>
    private void SandboxCaves()
    {
        if (!IsServer || World.Map.Planetoid is not { } pl || DigGround == null)
            return;
        int caves = 0, ops = 0, sealedOnes = 0;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var f in PlanetCaves.Of(pl))
        {
            var plan = PlanetCaves.Dig(pl, f);
            foreach (var op in plan.Ops)
                Dig(op);
            caves++;
            ops += plan.Ops.Count;
            if (plan.Mouth == null)
                sealedOnes++;
        }
        if (caves > 0)
            Log($"[caves] {caves} caves dug on {World.Map.Name} ({sealedOnes} sealed), {ops} ground ops, {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms");
    }
}
