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
    /// Server, 2 Hz while a Dress job waits with its bay clear: the first plain skin (loose in the bay, then in the hold) whose dress blueprint
    /// can be made and fits the bay gets it, costed and built as any blueprint. True when one started. A skin refused once is remembered with its
    /// settings (<see cref="DressMade"/>), so a refused skin costs nothing on later looks and the next skin in the hold is tried.
    /// </summary>
    private static bool DressNext(ConstructWorld world, Construct c, int t, StationJob j)
    {
        var (style, salt, extras) = DressSettings(c, t);
        var skins = _dressSkins;
        skins.Clear();
        SkinsInBay(world, c, t, skins);
        if (world.StoreOf(c, t) is { } hold)
            SkinsInHold(hold, skins);
        string? refusal = null;
        ConstructBuild.Blueprint? bp = null;
        ConstructPartDef? skin = null;
        foreach (var d in skins)
        {
            var (b, why, done) = DressMade(d.Name, style, salt, extras);
            if (!done)
            {
                refusal = why; // (being made: the next look goes on with it)
                break;
            }
            if (b != null && QuickRefusal(b, BaySize(c, t)) is { } small)
                (b, why) = (null, small);
            if (b != null)
            {
                (bp, skin) = (b, d);
                break;
            }
            refusal ??= $"can't dress that skin: {why}";
        }
        skins.Clear();
        if (bp == null || skin == null)
        {
            var note = refusal ?? WaitNote;
            if (note.Length > 160)
                note = note[..160];
            if (j.Note != note)
            {
                j.Note = note;
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

    private static readonly List<ConstructPartDef> _dressSkins = new(64);

    private sealed class DressEntry
    {
        public required string Skin, Style, Extras;
        public required int Salt;
        public HullLoft.DressWork? Work;
        public Task? Planning;
        public ConstructBuild.Blueprint? Bp;
        public string Why = "";
        public bool Done;
    }

    private static readonly DressEntry?[] _dressMade = new DressEntry?[32];
    private static int _dressNext;
    private const double DressMs = 3; // (game-thread milliseconds a look spends making a dress's subparts)

    /// <summary>
    /// A skin's dress blueprint for these settings, kept once made (the last 32 asked). Not done yet: its plan is made off the game thread, then
    /// its subparts a few milliseconds a look; null with the reason (Done) when it can't be made, and Why says how far it is while it's made.
    /// </summary>
    private static (ConstructBuild.Blueprint? Bp, string Why, bool Done) DressMade(string skin, string style, int salt, string extras)
    {
        DressEntry? e = null;
        foreach (var x in _dressMade)
            if (x != null && x.Salt == salt && x.Skin == skin && x.Style == style && x.Extras == extras)
            {
                e = x;
                break;
            }
        if (e == null)
        {
            e = new DressEntry { Skin = skin, Style = style, Salt = salt, Extras = extras };
            if (HullLoft.DressBegin(skin, style, salt, extras, out var why) is { } w)
            {
                e.Work = w;
                e.Planning = Task.Run(() => HullLoft.DressPlanOf(w));
            }
            else
                (e.Why, e.Done) = (why, true);
            _dressMade[_dressNext] = e;
            _dressNext = (_dressNext + 1) % _dressMade.Length;
        }
        if (!e.Done && e.Work is { } work && e.Planning is { IsCompleted: true } planning)
        {
            if (planning.IsFaulted)
                (e.Why, e.Done, e.Work) = ($"its dress plan failed: {planning.Exception?.InnerException?.Message}", true, null);
            else if (HullLoft.DressMakeSome(work, DressMs))
            {
                e.Bp = HullLoft.DressFinish(work, out var why);
                (e.Why, e.Done, e.Work) = (why, true, null);
            }
        }
        if (!e.Done)
            return (null, e.Work is { Count: > 0 } wk ? $"makes that skin's dress ({wk.Made} of {wk.Count} subparts)" : "plans that skin's dress", false);
        return (e.Bp, e.Why, true);
    }

    private static bool IsPlainSkin(ConstructPartDef d) =>
        HullLoft.IsSectionName(d.Name) && d.Name.Contains(" - 0 - |", StringComparison.Ordinal) // (a cheap look first: this runs over every body twice a second)
        && HullLoft.SectionOf(d.Name, out _) is { Plain: true };

    /// <summary>The plain skins lying loose in the bay (bodies of one part that nobody holds or grabs), added to <paramref name="into"/>.</summary>
    private static void SkinsInBay(ConstructWorld world, Construct c, int t, List<ConstructPartDef> into)
    {
        Bay(c, t, out var floor, out var rot, out var bay);
        var inv = System.Numerics.Quaternion.Conjugate(rot);
        foreach (var b in world.Bodies)
        {
            if (b.PartCount != 1 || b.HeldBy != 0 || b.GrabbedBy != 255 || !IsPlainSkin(b.DefOf(0)))
                continue;
            var local = System.Numerics.Vector3.Transform(b.WorldCom - floor, inv);
            if (MathF.Abs(local.X) <= bay.X * 0.5f && MathF.Abs(local.Z) <= bay.Z * 0.5f && local.Y >= -0.5f && local.Y <= bay.Y && into.Count < 64)
                into.Add(b.DefOf(0));
        }
    }

    /// <summary>The plain skins in the station's hold, added to <paramref name="into"/>.</summary>
    private static void SkinsInHold(CStore st, List<ConstructPartDef> into)
    {
        for (var k = 0; k < st.KindN && into.Count < 64; k++)
            if (st.KindCount![k] > 0 && ConstructParts.Get(st.KindDef![k]) is { } d && IsPlainSkin(d))
                into.Add(d);
        for (var k = 0; k < st.SlotCount && into.Count < 64; k++)
            if (st.Count[k] > 0 && ConstructParts.Get(st.Def[k]) is { } d && IsPlainSkin(d))
                into.Add(d);
    }
}
