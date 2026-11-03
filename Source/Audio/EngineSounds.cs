using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Which running modular engines are heard: the <see cref="Voices"/> nearest the listener, each with the loop of its cylinder
/// count, pitched by its rpm, louder with load and size. Runs on the rpm and load every peer has; nothing allocates.
/// </summary>
public sealed class EngineSounds
{
    public const int Voices = 4;

    public readonly ushort[] Body = new ushort[Voices];
    public readonly int[] Variant = new int[Voices];
    public readonly float[] Gain = new float[Voices], Pitch = new float[Voices], Scale = new float[Voices];
    public readonly Vector3[] At = new Vector3[Voices];
    public int Count;

    /// <summary>What should sound now.</summary>
    public void Pick(ConstructWorld world, Vector3 listener)
    {
        Count = 0;
        Span<float> dist = stackalloc float[Voices];
        foreach (var c in world.Bodies)
        {
            if (c.ActiveEngine is not { } e || !c.EngineRunning || c.Engine >= c.PartCount || e.Cylinders == 0)
                continue;
            var at = c.ToWorld(c.Parts[c.Engine].Pos);
            var d = Vector3.DistanceSquared(at, listener);
            var slot = Count < Voices ? Count++ : -1;
            if (slot < 0)
            {
                var far = 0;
                for (var k = 1; k < Voices; k++)
                    if (dist[k] > dist[far])
                        far = k;
                if (d >= dist[far])
                    continue;
                slot = far;
            }
            dist[slot] = d;
            var rules = EngineRules.Current;
            Body[slot] = c.Id;
            At[slot] = at;
            Variant[slot] = Synth.EngineLoopVariant(e.Cylinders, e.Misfiring + e.Wrecked > 0);
            Scale[slot] = rules.Scale[e.Size];
            // The firing rate in the loop follows the rpm; a big engine turns slowly and sounds it.
            Pitch[slot] = MathF.Max(c.Rpm, 1f) / Synth.EngineLoopRpm;
            Gain[slot] = Math.Clamp((0.35f + 0.65f * c.EngineLoad) * (0.45f + 0.05f * MathF.Min(e.Cylinders, 12)), 0, 1);
        }
    }

    /// <summary>Plays what <see cref="Pick"/> chose; each voice stays with its engine while it can.</summary>
    public void Play(Audio audio)
    {
        for (var v = 0; v < Count; v++)
            audio.EngineLoopAt(v, Variant[v], At[v], Gain[v], Pitch[v], Scale[v]);
        for (var v = Count; v < Voices; v++)
            audio.EngineLoopStop(v);
    }
}
