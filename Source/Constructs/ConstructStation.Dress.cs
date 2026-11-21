using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// The Building Station's Dress job (docs/sandbox/design/hull-dress.md, "The dress station"; Sean's option B, 2026-11-21): picked as the blueprint
/// <see cref="HullLoft.DressJobName"/>, the station waits for a plain hull skin (a <c>Hull Section</c> of style <c>-</c>) lying loose in its bay or
/// held in its hold, makes that skin's dress blueprint from its Dress settings (style, salt, extras: HullLoft.Dress.cs) and builds it: the skin
/// first, then each subpart on its dress anchor. Then it waits for the next skin, as many as its build count (or forever).
/// Dress subparts are <b>laid</b>, not ordered: no machine makes them, so an arm lays each from the station's linked steel, its mass in units
/// (free in creative), as links are laid from stock by the metre.
/// </summary>
public static partial class ConstructStation
{
    public const string LaidStock = "steel"; // what dress subparts are laid from (proposed)

    /// <summary>A part the station lays from stock instead of ordering (a dress subpart).</summary>
    public static bool IsLaid(ConstructPartDef? d) => d != null && HullLoft.IsDressName(d.Name);

    /// <summary>Units of <see cref="LaidStock"/> a laid part takes: its mass in kilograms, at least one.</summary>
    public static int LaidUnits(ConstructPartDef d) => Math.Max(1, (int)MathF.Ceiling(d.Mass));

    /// <summary>Is this job a Dress job (waiting for a skin, or building one's dress)?</summary>
    public static bool Dresses(StationJob j) => j.Name == HullLoft.DressJobName || HullLoft.IsDressBlueprintName(j.Name);

    private static bool LaidFree(ConstructWorld world) => world.Creative;

    private static bool LaidReady(ConstructWorld world, Construct c, int t, ConstructPartDef d) =>
        LaidFree(world) || ConstructCrafting.Bulk(world, c, ConstructCrafting.NetOf(world, c, t), RecyclingTable.Current.IdOf(LaidStock), -1) >= LaidUnits(d);

    /// <summary>Server: a laid part's stock taken from the linked bins as it goes on. False when there isn't enough (nothing is taken).</summary>
    private static bool TakeLaid(ConstructWorld world, Construct c, int t, ConstructPartDef d)
    {
        if (LaidFree(world))
            return true;
        if (!LaidReady(world, c, t, d))
            return false;
        TakeLinked(world, c, t, RecyclingTable.Current.IdOf(LaidStock), LaidUnits(d));
        return true;
    }

    /// <summary>What the build waits for when only laid parts are left and their stock is short (null: nothing).</summary>
    private static string? LaidShort(ConstructWorld world, Construct c, int t, StationJob j)
    {
        for (var s = j.Cursor; s < j.PartOf.Length; s++)
            if (j.PartOf[s] == -1 && j.StepKind[s] is var kind and >= 0 && j.KindLaid[kind] && Ready(j, s)
                && ConstructParts.Get(j.KindDef[kind]) is { } d && !LaidReady(world, c, t, d))
                return $"waits for {LaidUnits(d)} {LaidStock} in a linked bin: stock to lay its dress";
        return null;
    }

    /// <summary>Server: Start with <see cref="HullLoft.DressJobName"/>: a Dress job for <paramref name="repeat"/> + 1 skins (0xFFFF: forever).</summary>
    private static string? StartDress(ConstructWorld world, Construct c, int t, ushort repeat)
    {
        if (DressStyles.Current.Count == 0)
            return "no dress styles (Constructs/DressStyles.json was refused: callofslop.log says why)";
        var j = new StationJob
        {
            Name = HullLoft.DressJobName, State = StationState.Done, Repeat = repeat == None ? None : (ushort)Math.Min(None - 1, repeat + 1),
            Stamp = ConstructStationTree.NewStamp(), OrderAge = world.OrderStamp++, Note = WaitNote,
        };
        c.Parts[t].Station = j;
        world.StationOpened(c);
        world.StationChanged(c);
        return null;
    }

    private const string WaitNote = "waits for a plain hull skin in its bay or its hold";

    /// <summary>The style, salt and extras the station's Dress settings name.</summary>
    public static (string Style, int Salt, string Extras) DressSettings(Construct c, int t)
    {
        var d = c.DefOf(t);
        var styles = DressStyles.Current.Values;
        var si = Math.Clamp((int)ConstructConfig.Get(c.Parts[t], d, CfgKey.DressStyle, 0), 0, Math.Max(0, styles.Count - 1));
        var style = styles.Count == 0 ? "" : styles.ElementAt(si).Name;
        var salt = Math.Clamp((int)ConstructConfig.Get(c.Parts[t], d, CfgKey.DressSalt, 0), 0, HullLoft.SectionMaxSalt);
        var ei = Math.Clamp((int)ConstructConfig.Get(c.Parts[t], d, CfgKey.DressExtras, 0), 0, HullLoft.DressExtraChoices.Length - 1);
        return (style, salt, HullLoft.DressExtraChoices[ei]);
    }

    /// <summary>
    /// Server, 2 Hz while a Dress job waits with its bay clear: the next plain skin (loose in the bay, else in the hold) gets its dress blueprint,
    /// which is costed and built as any blueprint. True when one started.
    /// </summary>
    private static bool DressNext(ConstructWorld world, Construct c, int t, StationJob j)
    {
        var skin = SkinInBay(world, c, t) ?? (world.StoreOf(c, t) is { } hold ? SkinInHold(hold) : null);
        if (skin == null)
        {
            if (j.Note != WaitNote)
            {
                j.Note = WaitNote;
                world.StationChanged(c);
            }
            return false;
        }
        var (style, salt, extras) = DressSettings(c, t);
        if (HullLoft.DressBlueprint(skin.Name, style, salt, extras, out var why) is not { } bp)
        {
            var note = $"can't dress that skin: {why}";
            if (j.Note != note)
            {
                j.Note = note.Length > 160 ? note[..160] : note;
                world.StationChanged(c);
            }
            return false;
        }
        if (QuickRefusal(bp, BaySize(c, t)) is { } small)
        {
            if (j.Note != small)
            {
                j.Note = small;
                world.StationChanged(c);
            }
            return false;
        }
        if (j.Repeat != None)
            j.Repeat--;
        (j.Bp, j.Name, j.Steps) = (bp, bp.Name, (ushort)Math.Min(None - 1, bp.Steps.Count));
        j.Body = 0;
        j.PartOf = [];
        j.KindFit = [];
        j.LinkAt = j.Cursor = 0;
        j.Fitted = j.LinksDone = 0;
        j.LastPart = None;
        j.NoteKey = j.FillKey = 0;
        j.Note = "";
        j.SetAside.Clear();
        j.Started = Stopwatch.GetTimestamp();
        ResetArms(world, c, j);
        BeginCosting(world, j);
        world.StationChanged(c);
        Console.WriteLine($"[station] dress: {bp.Steps.Count - 1} subparts in {style} (salt {salt}, extras {(extras.Length == 0 ? "-" : extras)}) for {skin.Name}");
        return true;
    }

    private static bool IsPlainSkin(ConstructPartDef d) =>
        HullLoft.IsSectionName(d.Name) && HullLoft.SectionOf(d.Name, out _) is { Plain: true };

    /// <summary>A plain skin lying loose in the bay (a body of one part that nobody holds or grabs), or null.</summary>
    private static ConstructPartDef? SkinInBay(ConstructWorld world, Construct c, int t)
    {
        Bay(c, t, out var floor, out var rot, out var bay);
        var inv = System.Numerics.Quaternion.Conjugate(rot);
        foreach (var b in world.Bodies)
        {
            if (b.PartCount != 1 || b.HeldBy != 0 || b.GrabbedBy != 255 || !IsPlainSkin(b.DefOf(0)))
                continue;
            var local = System.Numerics.Vector3.Transform(b.WorldCom - floor, inv);
            if (MathF.Abs(local.X) <= bay.X * 0.5f && MathF.Abs(local.Z) <= bay.Z * 0.5f && local.Y >= -0.5f && local.Y <= bay.Y)
                return b.DefOf(0);
        }
        return null;
    }

    /// <summary>A plain skin in the station's hold, or null.</summary>
    private static ConstructPartDef? SkinInHold(CStore st)
    {
        for (var k = 0; k < st.KindN; k++)
            if (st.KindCount![k] > 0 && ConstructParts.Get(st.KindDef![k]) is { } d && IsPlainSkin(d))
                return d;
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Count[k] > 0 && ConstructParts.Get(st.Def[k]) is { } d && IsPlainSkin(d))
                return d;
        return null;
    }
}
