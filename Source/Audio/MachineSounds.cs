using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Which reactors are heard (Karl, 2026-11-17): the <see cref="Reactors"/> nearest the listener, each on two looping voices,
/// a light hum and a heavy one faded across by how hard the body's power plants work (<see cref="Construct.PlantShow"/>,
/// in every peer's pose). A steady tone (Karl, 2026-11-18): the pitch never moves and the loudness hardly does; load only
/// makes the hum fuller, eased over several seconds so a change in power use is never heard as a ramp. A reactor is a power plant that burns nothing, of at
/// least <see cref="MinKw"/> (a hamster wheel isn't one). A reactor whose ship has its mains off is quiet. Nothing allocates.
/// </summary>
public static class MachineSounds
{
    /// <summary>Reactors heard at once.</summary>
    public const int Reactors = 3;

    /// <summary>Looping voices: a light and a heavy hum for each reactor heard.</summary>
    public const int Voices = Reactors * 2;

    /// <summary>The smallest plant (kW) that hums: the Magpie's 400 kW cell stack does, anything smaller doesn't.</summary>
    public const float MinKw = 100f;

    /// <summary>Beyond this a reactor isn't picked (m); its own falloff ends sooner for a small one.</summary>
    private const float PickRange = 140f;

    /// <summary>The hum's gain before the bus at idle and at full load.</summary>
    public const float IdleGain = 0.22f, FullGain = 0.27f;

    private static readonly Construct?[] Body = new Construct?[Reactors];
    private static readonly int[] Part = new int[Reactors];
    private static readonly float[] Dist = new float[Reactors];

    /// <summary>Does this part hum?</summary>
    public static bool Hums(ConstructPartDef d) => d.Role == PartRole.Generator && d.PlantFuel == EngineFuel.None && d.PlantShaft <= 0 && d.PlantRate >= MinKw; // (a Generator Head is heard as its engine: ConstructGenset.cs)

    /// <summary>
    /// Reactors hum at all. Off for now (Karl, 2026-11-18: a loud noise repeating in the Zero Day's engine room that
    /// didn't sound right): every voice stays stopped until the hum is remade and this is turned back on.
    /// </summary>
    public static readonly bool HumOn = false;

    /// <summary>Every Sandbox frame, host and clients: the nearest reactors' hums.</summary>
    public static void Frame(ConstructWorld world, Audio audio, Vector3 listener, float dt)
    {
        if (!audio.Enabled)
            return;
        if (!HumOn)
        {
            for (var v = 0; v < Voices; v++)
                audio.MachineLoopStop(v);
            return;
        }
        var count = 0;
        foreach (var c in world.Bodies)
        {
            if (c.PlantCount == 0 || c.SpaceAway != 0)
                continue;
            // (eased here, so a pose's byte steps don't step the hum)
            c.HumShow += (c.PlantShow - c.HumShow) * Math.Clamp(dt * 0.3f, 0f, 1f);
            if (c.MainsOff)
                continue;
            if (Vector3.DistanceSquared(c.Pos, listener) > (PickRange + 40f) * (PickRange + 40f))
                continue;
            for (var k = 0; k < c.PlantCount; k++)
            {
                int i = c.Plants[k];
                if (i >= c.PartCount || c.Parts[i].Broken || !Hums(c.DefOf(i)))
                    continue;
                var d2 = Vector3.DistanceSquared(c.ToWorld(c.Parts[i].Pos), listener);
                if (d2 > PickRange * PickRange)
                    continue;
                var slot = count < Reactors ? count++ : -1;
                if (slot < 0)
                {
                    var far = 0;
                    for (var s = 1; s < Reactors; s++)
                        if (Dist[s] > Dist[far])
                            far = s;
                    if (d2 >= Dist[far])
                        continue;
                    slot = far;
                }
                Body[slot] = c;
                Part[slot] = i;
                Dist[slot] = d2;
            }
        }
        for (var s = 0; s < count; s++)
        {
            var c = Body[s]!;
            var d = c.DefOf(Part[s]);
            var at = ConstructRender.PartCentre(c, Part[s]);
            var load = Math.Clamp(c.HumShow, 0f, 1f);
            var scale = Math.Clamp(MathF.Sqrt(d.PlantRate / 10000f), 0.6f, 3f);
            var gain = IdleGain + (FullGain - IdleGain) * load;
            var heavy = Math.Clamp((load - 0.2f) / 0.6f, 0f, 1f); // (the fuller hum comes in past a fifth, all of it by four fifths)
            var pitch = scale > 1.5f ? 0.9f : 1f; // (a big core sits a little lower; load never moves it)
            // (equal power across the two, and the heavy voice always running a little, so neither starts or stops as load moves)
            audio.MachineLoopAt(s * 2, Sfx.ReactorHumLight, at, gain * MathF.Cos(heavy * MathF.PI / 2), pitch, scale);
            audio.MachineLoopAt(s * 2 + 1, Sfx.ReactorHumHeavy, at, gain * MathF.Max(0.02f, MathF.Sin(heavy * MathF.PI / 2)), pitch, scale);
        }
        for (var s = count; s < Reactors; s++)
        {
            Body[s] = null; // (lets a gone body go)
            audio.MachineLoopStop(s * 2);
            audio.MachineLoopStop(s * 2 + 1);
        }
    }
}
