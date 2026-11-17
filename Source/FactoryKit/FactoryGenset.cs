using System.Numerics;
using CallOfSlop.EngineKit;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.FactoryKit;

/// <summary>
/// An engine generator set as a factory's power block (docs/sandbox/design/engine-generators-notes.md "Factory kit"): the hook the kit's plan and script call,
/// additive, nothing in the kit calls it yet. <see cref="For"/> sizes the set to a programme's power (its machines' draw plus the margin, FactoryProgramme.PowerKw)
/// at the style's tier; <see cref="Footprint"/> is the ground it needs, to reserve in the plan's power rect; <see cref="Place"/> puts it on the floor in a script.
/// One engine a body: a construct's engine is its first that runs (ConstructEngine), so a factory has one set, sized for all of it, never a row of them.
/// </summary>
public static class FactoryGenset
{
    /// <summary>A set's plan: the engine request, the engine the kit makes for it, the head and the stores.</summary>
    public sealed record Set(EngineRequest Request, EngineResult Engine, string Head, int Tanks, int Batteries, float Kw);

    /// <summary>
    /// The set for <paramref name="powerKw"/> into the circuit at <paramref name="tier"/>: the engine's power is that over the head's efficiency and the governor's
    /// headroom, Small pieces with nothing pointing down. Null with <paramref name="why"/> when the kit can't make it (past what a tier's Small engine gives, the note says).
    /// </summary>
    public static Set? For(double powerKw, int tier, int seed, out string why)
    {
        why = "";
        var want = (float)powerKw;
        var engineKw = want;
        string head = EngineKitGenset.HeadFor(engineKw);
        for (var round = 0; round < 2; round++) // (the head's efficiency depends on the head, and the head on the engine's power)
        {
            var hd = ConstructParts.Find(head) ?? throw new FactoryKitException($"no {head}");
            engineKw = want / (hd.PlantShaft * ConstructGenset.Headroom);
            head = EngineKitGenset.HeadFor(engineKw);
        }
        var r = EngineKitGenset.Request(new EngineRequest { PowerKw = engineKw, Tier = Math.Clamp(tier, 1, ConstructParts.MaxTier), Seed = seed });
        var engine = EngineKit.EngineKit.Request(r);
        if (!engine.Built)
        {
            why = engine.Note;
            return null;
        }
        head = EngineKitGenset.HeadFor(engine.Kw);
        var (tanks, batteries) = EngineKitGenset.Stores(engine);
        var h = ConstructParts.Find(head)!;
        return new Set(r, engine, head, tanks, batteries, MathF.Min(h.PlantRate, engine.Kw * h.PlantShaft * ConstructGenset.Headroom));
    }

    /// <summary>The ground a set takes from its head's feet: x and z corners (the engine back along the script's -z, the tanks and batteries ahead along +z).</summary>
    public static (float X0, float X1, float Z0, float Z1) Footprint(Set set) => EngineKitGenset.Footprint(set.Engine, set.Head, set.Tanks, set.Batteries);

    /// <summary>
    /// Puts <paramref name="set"/> on the floor in <paramref name="s"/> with its head's feet at <paramref name="feet"/> (a Foundation's top: y 0.3, x and z on the
    /// tops' 0.5 m socket grid, x.25 and x.75), in <paramref name="style"/>'s colours. The head's run input is the set's switch (wire the main switch to it);
    /// its Load output (0 to 1) can drive a gauge. Throws <see cref="VehicleKitException"/> with the build rules' reason.
    /// </summary>
    public static EngineKitGenset.Placed Place(BlueprintScript s, Set set, Vector3 feet, FactoryStyle style, Dice d) =>
        EngineKitGenset.Place(s, feet, set.Request, set.Engine.Plan ?? throw new FactoryKitException("the set's engine has no plan"), set.Head, set.Tanks, set.Batteries,
            style.Colour(d, "frame"), style.Colour(d, "power"), style.Colour(d, "machine"));
}
