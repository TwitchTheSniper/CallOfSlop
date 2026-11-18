using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

public sealed partial class ConstructPartDef
{
    public Vector3 AugerTip; // an Unloading Auger's spout (its file's storage.auger.tip, the part's frame): where its load falls from
    public float AugerRate; // units a second it unloads at (0: not an auger)
    public bool IsAuger => AugerRate > 0;
}

/// <summary>One Unloading Auger of a construct, server only: rebuilt with the construct, never saved (only its store is).</summary>
public struct CAuger
{
    public ushort Part;
    public float Pour; // units on their way out
}

public sealed partial class Construct
{
    public CAuger[]? Augers; // (made the first time a construct has one; ConstructScoops.RebuildScoops finds them)
    public int AugerCount;
}

/// <summary>
/// Unloading Augers (docs/sandbox/constructs/storage-and-cargo.md "Cargo parts"; the user, 2026-11-17: "an output spigot like a combine
/// harvester's unloading auger"). A bin with a long tube: chute or tip loose material into it and, while it has power and its Run input
/// lets it (no Run wire: always), it pours out of its spout at its rate into the store under the spout: an open-top truck's bed, a
/// bunker, a Storage Volume, a machine's input. With nothing under the spout it holds its load (it never heaps the ground). It swings on
/// an Auger Swing Mount (a turntable joint: logic turns it), so it reaches over a lane. As a bin it is on the chute network and the
/// Inventory Beacon's, so the logistics that fill and empty bins use it as a source or a destination. 10 Hz on the server, with the
/// scoops; nothing new on the wire (the store goes as any bin's); no allocation.
/// </summary>
public static class ConstructAugers
{
    public const float MaxRate = 2000f;
    private const float RayStart = 0.05f;

    public static void Parse(ConstructPartDef d, JsonElement e, string file, string where)
    {
        if (e.ValueKind != JsonValueKind.Object || d.BulkCapacity == 0 || d.IsVolumeStore)
            throw new ConstructPartException($"{file}: storage \"auger\" is an object (tip, rate) on a bin with a \"bulk\" block");
        ConstructParts.Known(e, ["tip", "rate"], where + ".auger");
        d.AugerTip = ConstructParts.Vec(ConstructParts.Need(e, "tip", where + ".auger"), file, "storage.auger.tip");
        var rate = ConstructParts.Num(ConstructParts.Need(e, "rate", where + ".auger"), file, "storage.auger.rate");
        ConstructParts.Range(rate, 1, MaxRate, file, "storage.auger.rate");
        d.AugerRate = (float)rate;
        d.DataIn = [ConstructGatherer.RunPort];
    }

    /// <summary>The spout in the world (the part's segment as it stands: a swing mount turns it).</summary>
    public static Vector3 Tip(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, p.D.AugerTip), q);
    }

    /// <summary>Is it unloading now: whole, powered, let run, and holding something?</summary>
    public static bool Running(ConstructWorld world, Construct c, int part) =>
        !c.Parts[part].Broken && !ConstructGatherer.HeldOff(c, part) && ConstructRecycler.Powered(world, c, part);

    internal static void One(ConstructWorld world, Construct c, int k)
    {
        ref var a = ref c.Augers![k];
        if (a.Part >= c.PartCount || world.StoreOf(c, a.Part) is not { } st)
            return;
        var held = ConstructGatherer.HeldOff(c, a.Part);
        ConstructGatherer.SetHeld(world, st, held);
        if (st.Amount <= 0 || !Running(world, c, a.Part))
        {
            a.Pour = 0;
            ConstructScoops.Mark(world, st, ConstructScoops.BlockNone);
            return;
        }
        var d = c.Parts[a.Part].D;
        var tip = Tip(c, a.Part);
        var gravity = ConstructGravity.GravityFor(c, tip);
        var pull = gravity.Length();
        if (pull < 0.01f)
            return; // (no gravity: nothing falls out of the spout)
        var down = gravity / pull;
        a.Pour = MathF.Min(a.Pour + d.AugerRate * ConstructScoops.Every / World.TickRate, d.BulkCapacity);
        var units = Math.Min((int)a.Pour, st.Amount);
        if (units <= 0)
            return;
        var put = ConstructScoops.PourAuger(world, c, a.Part, st, tip + down * RayStart, down, units);
        if (put > 0)
            a.Pour = MathF.Max(0, a.Pour - put);
        else
            a.Pour = 0; // (nothing took it: it waits in the tube)
        ConstructScoops.Mark(world, st, put > 0 ? ConstructScoops.PourInto : ConstructScoops.PourNowhere);
    }
}
