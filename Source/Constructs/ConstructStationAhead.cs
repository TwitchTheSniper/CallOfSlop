namespace CallOfSlop;

public sealed partial class StationJob
{
    // ---- Server only: the next kit (ConstructStationAhead.cs).
    public ushort[] NextDef = []; // the kinds of the kit its oven will bake after this one (empty: none, or not an oven's kit station)
    public int[] NextNeed = []; // ...how many of each
    public int NextN;
    public string NextName = ""; // ...the kit's blueprint, by name (for the log)
}

/// <summary>
/// The next kit's pieces ordered ahead (Sean, 2026-11-27: "the entire main hull piece lines ... wait for their hull piece to be built
/// before starting on pieces for the next segment ... there's multiple construction arms on the pads, so we're almost always extrusion
/// limited, so we can keep those systems running continuously"). A rover station whose builds roll along a run onto a Bake Oven's bed
/// builds that oven's kits one at a time; once every piece of the kit it builds is held or asked, it orders the pieces of the kit its oven
/// bakes next (<see cref="ConstructBakeOven.NextKitFor"/>), so its line's Extruders, Panel Formers and Laser Cutters go straight on to
/// them. Those pieces queue on its pick pads as early pieces do (<see cref="Early"/>, half the pad's floor: <see cref="EarlyRoom"/>),
/// the orders outlive the kit's end (<see cref="CancelOrders"/>), and the oven starts the next kit at the station as soon as its bay is
/// clear, even while it bakes (ConstructBakeOvenOrders.cs), where the pieces on its pads count as held.
/// </summary>
public static partial class ConstructStation
{
    /// <summary>Server, every <see cref="BayEvery"/> looks: the kit its oven bakes after the one it builds, as kinds and counts (none when it builds no oven's kit).</summary>
    private static void NoteNextKit(ConstructWorld world, Construct c, int t, StationJob j)
    {
        j.NextN = 0;
        j.NextName = "";
        if (!c.DefOf(t).StationNoHold || j.Role == 1 || j.Bp == null || BakeKits.FindPrint(j.Name) == null || ConstructBakeOven.NextKitFor(world, c, t) is not { } next)
            return;
        var defs = new List<ushort>();
        var need = new List<int>();
        foreach (var s in next.Steps)
        {
            var d = ConstructParts.Find(s.Part) ?? (s.Part.StartsWith(Extrusion.Prefix, StringComparison.OrdinalIgnoreCase) && Extrusion.ParseKey(s.Part) is { } key
                ? Extrusion.Def(key with { Growing = false }) : null); // (an extrusion no machine has made yet isn't found by name)
            if (d == null || IsLaid(d))
                continue; // (a dress piece is laid from stock at the station, never ordered)
            var k = defs.IndexOf((ushort)d.Id);
            if (k < 0)
            {
                defs.Add((ushort)d.Id);
                need.Add(1);
            }
            else
                need[k]++;
        }
        j.NextDef = defs.ToArray();
        j.NextNeed = need.ToArray();
        j.NextN = defs.Count;
        j.NextName = next.Name;
    }

    /// <summary>Is a kind one the next kit needs (<see cref="StationJob.NextDef"/>)?</summary>
    private static bool NextHas(StationJob j, ushort def)
    {
        for (var i = 0; i < j.NextN; i++)
            if (j.NextDef[i] == def)
                return true;
        return false;
    }

    /// <summary>How many of a kind station <paramref name="t"/>'s own pick pads hold (any build's: the next kit's pieces too).</summary>
    private static int OnOwnPads(ConstructWorld world, Construct c, int t, ushort def)
    {
        Span<ushort> pads = stackalloc ushort[MaxOwnPads];
        var n = 0;
        var pn = OwnPads(c, t, pads);
        for (var k = 0; k < pn; k++)
            if (world.StoreOf(c, pads[k]) is { } st)
                n += ConstructCrafting.Holds(st, def);
        return n;
    }

    /// <summary>
    /// Server, a kit station's look: once every piece of the kit it builds is fitted, held or asked, the next kit's pieces are ordered (what
    /// it lacks once the pieces on its pads and the open orders are counted, less what the kit it builds still takes of the same kind).
    /// Allocates nothing.
    /// </summary>
    private static void OrderNextKit(ConstructWorld world, Construct c, int t, StationJob j, ref int placed)
    {
        if (j.NextN == 0)
            return;
        var q = c.Parts[t].Craft;
        for (var i = 0; i < j.KindN; i++)
            if (!j.KindLaid[i] && j.KindNeed[i] - j.KindFit[i] - OnOwnPads(world, c, t, j.KindDef[i]) - Asked(q, j.KindDef[i]) > 0)
                return; // (the kit it builds isn't all asked yet: its own pieces first)
        for (var i = 0; i < j.NextN && placed < OrdersAStep && (q?.Count ?? 0) < ConstructCrafting.MaxOrders; i++)
        {
            var def = j.NextDef[i];
            var now = KindOf(j, def) is var k and >= 0 ? j.KindNeed[k] - j.KindFit[k] : 0; // (the kit it builds takes these first)
            var lack = j.NextNeed[i] + now - OnOwnPads(world, c, t, def) - Asked(q, def);
            if (lack <= 0 || ConstructParts.Get(def) is not { } d)
                continue;
            if (ConstructCrafting.Place(world, c, t, OrderFor(d, lack)) is { } no)
            {
                if (j.BayLooks == BayEvery)
                    Console.WriteLine($"[station] body {c.Id} part {t}: the next kit's {d.Name} ({j.NextName}) isn't ordered ahead: {no}");
                continue;
            }
            placed++;
            q = c.Parts[t].Craft;
            Console.WriteLine($"[station] body {c.Id} part {t}: ordered {lack} x {d.Name} ahead for the next kit ({j.NextName})");
        }
    }

    /// <summary>
    /// Server: station <paramref name="t"/> owns pick pad <paramref name="pad"/> and isn't building, yet an order of its kind is open (the
    /// next kit's, kept past the last kit's end): the station, or -1. A piece landing there counts for it (ConstructLifters.PadArrived).
    /// </summary>
    public static int KeptOrderStation(Construct c, int pad, ushort def)
    {
        Note(c);
        for (var k = 0; k < c.StationPadN; k++)
        {
            if (c.StationPadPairs[2 * k + 1] != pad || c.StationPadPairs[2 * k] is not (var t and < ushort.MaxValue) || t >= c.PartCount
                || c.Parts[t].Station is { State: StationState.Building } || c.Parts[t].Craft is not { Count: > 0 } q)
                continue;
            for (var i = 0; i < q.Count; i++)
                if (!q.Items[i].Done && !q.Items[i].Cancelled && ConstructCrafting.KindOf(ref q.Items[i]) == def)
                    return t;
        }
        return -1;
    }
}
