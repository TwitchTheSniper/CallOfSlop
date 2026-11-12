using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Why a press isn't stamping (a store's <see cref="CStore.Refused"/> byte: on the wire, so a client's panels and look line say the same).</summary>
public enum PressStall : byte
{
    None,
    NoPower,
    NoDie,
    NoRecipe, // a Die Lathe with nothing chosen
    WrongClass, // a Die Lathe told to cut a die of another size
    Wants, // a bay wants a piece
    TooShort, // a piece has less left than a stroke takes
    TrayFull,
    Blocked, // something stands where a body leaves
    ConveyorFull, // its Piece out conveyor has no room at its start (the stamped parts wait for it)
    ConveyorSmall, // what it stamps is wider or taller than its Piece out conveyor
    ConveyorCut, // its Piece out conveyor is cut
    Bay2Short, // a sized die's pattern wants more of its kit in bay 2 than it holds
}

/// <summary>Why a bay turned a piece away. <see cref="CStore.Blocked"/> holds this times 4 plus the bay (3: the die slot), on the wire.</summary>
public enum BayRefuse : byte
{
    None,
    NoDie, // nothing to tell what it wants
    Material,
    Section,
    Size,
    Room, // no room for its length
    DieSize, // a die of another size class
    Bulk, // a chute of bulk material: a press takes pieces
    Short, // the right piece, but shorter than a stroke
    Bounds, // a sized die's: the piece is outside its sizes (too thin, too long, too stubby: Pressed.Refusal says which)
    Pattern, // the press's Pattern setting doesn't take this section
    Hopper, // bay 2 is the kit hopper: it takes no piece
    Kit, // bay 2: not the kit this pattern wants
}

/// <summary>
/// The Press and the Die Lathe (docs/sandbox/CONSTRUCTS.md, "The Press"). A press stamps parts out of extruded pieces. It has
/// bays and a die slot, each a socket, and what is in them is a part of the machine's own construct, linked to that socket, as
/// a piece on an Extruder's die is: so it counts in the mass, is solid and drawn, and goes through the wire, saves and
/// blueprints with no second path, and everything about it is found from the link. A stroke takes length off each piece (a
/// def swap, as growing is), and when a piece has no length left it goes. A Die Lathe is the same machine with one bay and no
/// die slot: a setting picks what it cuts. Server at 10 Hz, looking only at constructs that have one; the bays' panels read the same
/// parts on either side. Nothing allocates but the bodies it makes.
/// </summary>
public static class ConstructPress
{
    public const int Every = 6; // ticks between steps (10 Hz)
    private const int Milli = 1000;
    private const int RefuseSteps = 50; // a refusal stays on the panel this long after the piece stops being there
    private const int BesideIcon = 13; // characters a bay's screen holds on the lines beside its icon
    private const int CoolSteps = 30; // a piece taken out isn't taken straight back in
    private static readonly List<Construct> _near = new(32);

    /// <summary>A chute's delivery, done at the start of the next step: a chute moves inside the routes' own loop, where parts can't come and go.</summary>
    public readonly record struct Feed(ushort FromBody, ushort FromPart, ushort ToBody, ushort ToPart, ushort Die);

    public static void Step(ConstructWorld world, IWorldQuery query)
    {
        for (var k = 0; k < world.PressFeeds.Count; k++)
            Deliver(world, query, world.PressFeeds[k]);
        world.PressFeeds.Clear();
        if (++world.PressTicks < Every)
            return;
        world.PressTicks = 0;
        var table = PressTable.Current;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasPress)
                continue;
            if (c.Dirty)
                c.Rebuild();
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.IsPress && world.StoreOf(c, i) is { } st && Work(world, query, table, c, i, st))
                    break; // (parts went: the indices moved, and the rest wait for the next step)
        }
    }

    /// <summary>The part on a socket of this machine, or -1.</summary>
    public static int OnSocket(Construct c, int part, int socket)
    {
        for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e)) // (the part's own links: a scan of every link cost 3 to 6 ms a step over a fitted walker's 90 machines)
        {
            ref var l = ref c.Links[e >> 1];
            if ((e & 1) == 0 ? l.SA == socket && l.B < c.PartCount : l.SB == socket && l.A < c.PartCount)
                return c.OtherEnd(e);
        }
        return -1;
    }

    /// <summary>The piece in a bay, or -1.</summary>
    public static int PieceIn(Construct c, int part, int bay)
    {
        var d = c.Parts[part].D;
        if (bay >= d.PressBays.Length)
            return -1;
        var i = OnSocket(c, part, d.PressBays[bay].Socket);
        return i >= 0 && c.Parts[i].D.IsExtrusion ? i : -1;
    }

    /// <summary>The die in the machine, or -1.</summary>
    public static int DieIn(Construct c, int part)
    {
        var d = c.Parts[part].D;
        if (d.PressDieSocket < 0)
            return -1;
        var i = OnSocket(c, part, d.PressDieSocket);
        return i >= 0 && c.Parts[i].D.IsPressDie ? i : -1;
    }

    /// <summary>Is socket <paramref name="s"/> of a machine a bay (a piece on it stays unfinished, as on a die)?</summary>
    public static bool IsBay(ConstructPartDef d, int s)
    {
        foreach (var b in d.PressBays)
            if (b.Socket == s)
                return true;
        return false;
    }

    /// <summary>
    /// The machine a piece or a die is held in: a bay, a die slot, an Extruder's die or a Wheel Former's hub; -1 for any other part. What a
    /// machine holds is a part of its construct, so the crosshair lands on it and not on the machine: F and the physics gun take it out from here.
    /// </summary>
    public static int MachineOf(Construct c, int i)
    {
        if ((uint)i >= (uint)c.PartCount || c.Parts[i].D is not ({ IsExtrusion: true } or { IsPressDie: true }))
            return -1;
        for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
        {
            ref var l = ref c.Links[e >> 1];
            var other = c.OtherEnd(e);
            if (other >= c.PartCount)
                continue;
            var socket = (e & 1) == 0 ? l.SB : l.SA;
            var d = c.DefOf(other);
            if (IsBay(d, socket) || d.IsPress && socket == d.PressDieSocket || d.IsExtruder && socket == d.ExtDieSocket)
                return other;
        }
        return -1;
    }

    /// <summary>The rows this machine can run: a die's, or the one a lathe has chosen.</summary>
    public static ReadOnlySpan<PressRow> RowsOf(PressTable t, Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (d.IsLathe)
        {
            var k = (int)ConstructConfig.Get(p, d, CfgKey.Recipe, 0) - 1;
            return k >= 0 && k < t.Lathe.Length ? t.Lathe.AsSpan(k, 1) : default;
        }
        return default; // (a press's dies are sized dies: SizedDieOf)
    }

    /// <summary>
    /// Which of the machine's bays row bay <paramref name="rb"/> works now. A row of several bays names them in order; a one-bay row (a
    /// Bracket or Housing Die) is every bay's: each bay takes such pieces, and a stroke works the first bay whose piece is ready (one the
    /// row takes, long enough), else the first whose piece the row takes, else the first holding anything, else bay 1. Allocates nothing.
    /// </summary>
    public static int BayOf(PressRow r, Construct c, int part, int rb)
    {
        if (r.Bays.Length != 1)
            return rb;
        var s = r.Bays[0];
        var bays = c.Parts[part].D.PressBays.Length;
        int takes = -1, any = -1;
        for (var b = 0; b < bays; b++)
        {
            var i = PieceIn(c, part, b);
            if (i < 0)
                continue;
            ref readonly var k = ref c.Parts[i].D.ExKey;
            if (s.Takes(k))
            {
                if (k.L >= s.StrokeCm(k))
                    return b;
                if (takes < 0)
                    takes = b;
            }
            if (any < 0)
                any = b;
        }
        return takes >= 0 ? takes : any >= 0 ? any : 0;
    }

    /// <summary>What the machine's bay <paramref name="bay"/> takes under row <paramref name="r"/>, or null when the row doesn't use that bay.</summary>
    private static PressBaySpec? SpecFor(PressRow r, int bay) => r.Bays.Length == 1 ? r.Bays[0] : bay < r.Bays.Length ? r.Bays[bay] : null;

    /// <summary>The row that its bays satisfy, else the first that what is in them doesn't rule out: what the panels ask for. Null: no rows.</summary>
    public static PressRow? RowFor(ReadOnlySpan<PressRow> rows, Construct c, int part)
    {
        PressRow? best = null;
        var bestScore = -1;
        foreach (var r in rows)
        {
            var score = 0;
            var ok = true;
            for (var b = 0; b < r.Bays.Length; b++)
            {
                var piece = PieceIn(c, part, BayOf(r, c, part, b));
                if (piece < 0)
                    continue;
                if (r.Bays[b].Takes(c.Parts[piece].D.ExKey))
                    score++;
                else
                    ok = false;
            }
            if (ok && score == r.Bays.Length)
                return r;
            if (ok && score > bestScore)
            {
                best = r;
                bestScore = score;
            }
        }
        return best ?? (rows.Length > 0 ? rows[0] : null);
    }

    private static void SetStall(ConstructWorld world, CStore st, PressStall s)
    {
        if (st.Refused == (byte)s)
            return;
        st.Refused = (byte)s;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    private static void SetRefusal(ConstructWorld world, CStore st, int bay, BayRefuse why)
    {
        st.RefusedAge = 0;
        var v = why == BayRefuse.None ? (byte)0 : (byte)((int)why * 4 + bay);
        if (st.Blocked == v)
            return;
        st.Blocked = v;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    /// <summary>A step of one machine. True when parts came or went and the construct's indices moved.</summary>
    private static bool Work(ConstructWorld world, IWorldQuery query, PressTable table, Construct c, int part, CStore st)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (st.Blocked != 0 && ++st.RefusedAge >= RefuseSteps)
            SetRefusal(world, st, 0, BayRefuse.None);
        if (st.JobMaterial > 0)
            st.JobMaterial--;
        else if (!p.Broken && TakeIn(world, query, table, c, part, st))
            return true;
        var held = ConstructGatherer.HeldOff(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        if (!d.IsLathe)
        {
            if (LoosenHopperPiece(world, c, part))
                return true;
            if (SizedDieOf(table, c, part) is { } sized)
                return WorkSized(world, query, c, part, st, sized, held);
            SetStall(world, st, PressStall.NoDie); // (no die, or one the table has no data for)
            st.JobTicks = 0;
            ConstructGatherer.SetBusy(world, st, false);
            return false;
        }
        var rows = RowsOf(table, c, part);
        var row = RowFor(rows, c, part);
        var why = StateOf(world, c, part, st, row, out var satisfied);
        SetStall(world, st, why);
        if (why != PressStall.None || held || p.Broken || !satisfied)
        {
            st.JobTicks = 0;
            ConstructGatherer.SetBusy(world, st, false);
            return false;
        }
        if (!world.CreativeRecycling)
        {
            ConstructGatherer.SetBusy(world, st, true);
            st.JobTicks += d.RefineWork;
            if (st.JobTicks < row!.Ticks * Milli)
                return false;
            st.JobTicks = 0;
        }
        return Stroke(world, query, c, part, st, row!);
    }

    /// <summary>Why it can't make a stroke now; <paramref name="satisfied"/>: its bays hold what a row takes.</summary>
    private static PressStall StateOf(ConstructWorld world, Construct c, int part, CStore st, PressRow? row, out bool satisfied)
    {
        var d = c.Parts[part].D;
        satisfied = false;
        if (row == null)
            return d.IsLathe ? PressStall.NoRecipe : PressStall.NoDie;
        if (d.IsLathe && row.Output.Sockets.Length > 0 && row.Output.Sockets[0].Class != d.Sockets[d.PressBays[0].Socket].Class)
            return PressStall.WrongClass;
        for (var b = 0; b < row.Bays.Length; b++)
        {
            var i = PieceIn(c, part, BayOf(row, c, part, b));
            if (i < 0 || !row.Bays[b].Takes(c.Parts[i].D.ExKey))
                return PressStall.Wants;
            if (c.Parts[i].D.ExKey.L < row.Bays[b].StrokeCm(c.Parts[i].D.ExKey))
                return PressStall.TooShort;
        }
        satisfied = true;
        if (OutputFull(d, st, row))
            return PressStall.TrayFull;
        return ConstructRecycler.Powered(world, c, part) ? PressStall.None : PressStall.NoPower;
    }

    private static bool OutputFull(ConstructPartDef d, CStore st, PressRow r) =>
        ConstructAssembling.OutputOf(d, st, r.Output, r.Count) == AsmOut.TrayFull;

    /// <summary>What a stroke makes goes out: to the tray, the Piece out conveyor (whole strokes only) or the floor in front. False (and the stall set) when it can't.</summary>
    private static bool PutOut(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, ConstructPartDef output, int count)
    {
        var d = c.Parts[part].D;
        if (ConstructAssembling.OutputOf(d, st, output, count) == AsmOut.Body && ConstructConveyor.Out(c, part) is var conveyor and >= 0)
        {
            // (a Piece out conveyor takes what no tray holds, whole strokes only; without one it drops in front as before)
            if (!ConstructConveyor.PutMade(world, c, conveyor, output, count, out var full))
            {
                SetStall(world, st, full switch { ExtrudeStall.ConveyorSmall => PressStall.ConveyorSmall, ExtrudeStall.ConveyorCut => PressStall.ConveyorCut, _ => PressStall.ConveyorFull });
                return false;
            }
        }
        else if (ConstructAssembling.OutputOf(d, st, output, count) == AsmOut.Body)
        {
            for (var n = 0; n < count; n++)
            {
                var why = ConstructAssembling.Leave(world, query, c, part, output);
                if (why != AsmBlock.None)
                {
                    SetStall(world, st, PressStall.Blocked);
                    return false;
                }
            }
        }
        else
            for (var n = 0; n < count; n++)
                world.TrayAdd(st, d, output);
        return true;
    }

    /// <summary>A stroke: the output first, then the length off each piece, the highest part first so a piece that goes leaves the others' indices alone. True when a part went.</summary>
    private static bool Stroke(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, PressRow r)
    {
        var d = c.Parts[part].D;
        if (!PutOut(world, query, c, part, st, r.Output, r.Count))
            return false;
        world.Note(CNoteKind.Made, c.Id, c.Owner, (ushort)r.Output.Id, (byte)d.Tier);
        Span<int> pieces = stackalloc int[PressTable.MaxBays];
        Span<int> left = stackalloc int[PressTable.MaxBays];
        for (var b = 0; b < r.Bays.Length; b++)
        {
            pieces[b] = PieceIn(c, part, BayOf(r, c, part, b));
            ref readonly var k = ref c.Parts[pieces[b]].D.ExKey;
            left[b] = k.L - r.Bays[b].StrokeCm(k);
        }
        var gone = false;
        for (var turn = 0; turn < r.Bays.Length; turn++)
        {
            var at = -1;
            for (var b = 0; b < r.Bays.Length; b++)
                if (left[b] >= 0 && (at < 0 || pieces[b] > pieces[at]))
                    at = b;
            if (at < 0)
                break;
            var i = pieces[at];
            if (left[at] <= 0)
            {
                world.RemovePart(c, i);
                gone = true;
            }
            else if (Extrusion.Def(c.Parts[i].D.ExKey with { L = (ushort)left[at] }) is { } shorter)
                ConstructExtruder.Resize(world, c, i, shorter);
            left[at] = -1;
        }
        c.Wake();
        return gone;
    }

    // ---- Sized dies -----------------------------------------------------------------------------------------------
    // A die with Pressing.json /2 data (Bracket, Housing, Plate: PressTable.Dies) presses a bay's whole piece into one part: the preset's
    // catalogue part when the piece is a preset's blank, else a "Pressed Part (...)" sized by it (Pressed.cs). Every bay takes blanks and a
    // stroke works the first ready one, bay 1 first; the pattern is the blank's (PressDie.PatternFor) until the press has a Pattern setting.
    // A long strip makes one long part, not many (the user's decision D8). Dies with no such data stroke their rows as before.

    /// <summary>The fitted die's sized data, or null (no die, a lathe, or a die that strokes rows).</summary>
    public static PressDie? SizedDieOf(PressTable t, Construct c, int part)
    {
        if (t.Dies.Length == 0 || c.Parts[part].D.IsLathe)
            return null;
        var die = DieIn(c, part);
        return die < 0 ? null : t.Die(c.Parts[die].D.DieFamily);
    }

    /// <summary>Why a press with a sized die won't take this piece, or None; with the pattern and preset it would press it to.</summary>
    public static BayRefuse SizedRefusal(PressDie die, Construct c, int part, in ExtrusionKey key, out PressPattern? pattern, out ConstructPartDef? preset)
    {
        pattern = null;
        preset = null;
        var press = c.Parts[part].D;
        var section = Pressed.SectionOf(key);
        if (section < 0 || Array.IndexOf(die.Blanks, section) < 0)
            return BayRefuse.Section;
        if (Array.IndexOf(die.Materials, key.Material) < 0)
            return BayRefuse.Material;
        if (Pressed.Across(key) > press.PressMaxCm)
            return BayRefuse.Size;
        pattern = PatternOf(die, c, part, key, out preset);
        if (pattern == null)
            return SetPattern(die, c, part) != null ? BayRefuse.Pattern : BayRefuse.Section;
        if (!Pressed.Fits(die, pattern, key with { Growing = false }))
        {
            (pattern, preset) = (null, null);
            return BayRefuse.Bounds;
        }
        return BayRefuse.None;
    }

    /// <summary>The first bay (bay 1 first) whose piece the sized die presses, with what it makes; -1 when none.</summary>
    public static int ReadyBay(PressDie die, Construct c, int part, out PressPattern? pattern, out ConstructPartDef? preset)
    {
        var d = c.Parts[part].D;
        for (var b = 0; b < d.PressBays.Length; b++)
            if (b != HopperBay && PieceIn(c, part, b) is var i and >= 0 && SizedRefusal(die, c, part, c.Parts[i].D.ExKey, out pattern, out preset) == BayRefuse.None)
                return b;
        (pattern, preset) = (null, null);
        return -1;
    }

    /// <summary>A sized die's stroke time in ticks at speed 1 (the press's speed is its work a step).</summary>
    public static int SizedTicks(PressDie die, in ExtrusionKey blank) => (int)MathF.Ceiling(Pressed.Seconds(die, blank, 0) * World.TickRate - 0.001f);

    /// <summary>What a card says a sized die makes of a blank: "Makes" the preset's part, else "Sized" and the pattern's label (allocates nothing).</summary>
    private static void SizedMakes(PanelCard card, int line, PressPattern pattern, ConstructPartDef? preset) =>
        card.Add(line, preset != null ? "Makes " : "Sized ").Add(line, preset?.Name ?? pattern.Label);

    /// <summary>A step of a press with a sized die. True when parts came or went.</summary>
    private static bool WorkSized(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, PressDie die, bool held)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var bay = ReadyBay(die, c, part, out var pattern, out var preset);
        var need = bay < 0 ? 0 : Pressed.Bay2For(pattern!, c.Parts[PieceIn(c, part, bay)].D.ExKey with { Growing = false });
        var why = bay < 0 ? PressStall.Wants
            : need > KitHeld(st, pattern!.Bay2) ? PressStall.Bay2Short
            : preset != null && ConstructAssembling.OutputOf(d, st, preset, 1) == AsmOut.TrayFull ? PressStall.TrayFull
            : ConstructRecycler.Powered(world, c, part) ? PressStall.None : PressStall.NoPower;
        SetStall(world, st, why);
        if (why != PressStall.None || held || p.Broken)
        {
            st.JobTicks = 0;
            ConstructGatherer.SetBusy(world, st, false);
            return false;
        }
        var piece = PieceIn(c, part, bay);
        var blank = c.Parts[piece].D.ExKey with { Growing = false };
        if (!world.CreativeRecycling)
        {
            ConstructGatherer.SetBusy(world, st, true);
            st.JobTicks += d.RefineWork;
            if (st.JobTicks < SizedTicks(die, blank) * Milli)
                return false;
            st.JobTicks = 0;
        }
        // The part: a preset is the catalogue part; anything else is made from its key (once a machine, then shared: Pressed.Parse).
        var output = preset ?? Pressed.Parse(Pressed.KeyOf(die, pattern!, blank));
        if (output == null)
        {
            SetStall(world, st, PressStall.Blocked); // (the run-time part table is full)
            return false;
        }
        if (!PutOut(world, query, c, part, st, output, 1))
            return false;
        if (need > 0 && pattern!.Bay2 is { } kit)
        {
            if (kit.Part != null)
                world.AddInputPart(st, kit.Part.Id, -need);
            else
                world.AddInput(st, kit.Material, -need);
        }
        world.Note(CNoteKind.Made, c.Id, c.Owner, (ushort)(preset ?? pattern!.Like).Id, (byte)d.Tier); // (a sized part counts as its like part: its own id is this run's)
        world.RemovePart(c, piece);
        c.Wake();
        return true;
    }

    // ---- The Pattern setting and bay 2 ----------------------------------------------------------------------------
    // A press's Pattern (its "recipe" setting, PressTable.PatternChoices): 0 Auto, else 1 + the place of a pattern in the fitted die's list.
    // Bay 2 is the kit hopper: a pattern's bay 2 part (kinds of the store, as an Assembler's parts) or bulk material (its mixed input) goes
    // in by chute or F, and a stroke takes Pressed.Bay2For of it. A piece never sits there: one that does (an older save) is set loose.

    /// <summary>Bay 2 (its index): the kit hopper on every press.</summary>
    public const int HopperBay = 1;

    /// <summary>The pattern the press's setting names on this die, or null (Auto, or a place past the die's list).</summary>
    public static PressPattern? SetPattern(PressDie die, Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var k = (int)ConstructConfig.Get(p, p.D, CfgKey.Recipe, 0) - 1;
        return k >= 0 && k < die.Patterns.Length ? die.Patterns[k] : null;
    }

    /// <summary>What this press makes of a blank: the set pattern (null when it doesn't take the blank's section), or on Auto the blank's own (PressDie.PatternFor); and the preset's part when it is one.</summary>
    public static PressPattern? PatternOf(PressDie die, Construct c, int part, in ExtrusionKey blank, out ConstructPartDef? preset)
    {
        preset = null;
        if (SetPattern(die, c, part) is not { } set)
            return die.PatternFor(blank, out preset);
        var k = blank with { Growing = false };
        if (!die.Takes(set, Pressed.SectionOf(k)))
            return null;
        foreach (var pr in die.Presets)
            if (ReferenceEquals(pr.Pattern, set) && pr.Blank == k)
            {
                preset = pr.Part;
                break;
            }
        return set;
    }

    /// <summary>How much of a kit the hopper holds (parts of its kind, or units of its material).</summary>
    public static int KitHeld(CStore st, PressAmount? kit)
    {
        if (kit == null)
            return 0;
        if (kit.Part != null)
            return st.KindIndex((ushort)kit.Part.Id) is var k and >= 0 ? st.KindCount![k] : 0;
        for (var i = 0; i < st.MixN; i++)
            if (st.MixMat![i] == kit.Material)
                return st.MixAmount![i];
        return 0;
    }

    /// <summary>The kit bay 2 wants now and how much the next stroke takes: the ready blank's pattern, else the set pattern, else (Auto) the die's first pattern with a kit; null when none wants one.</summary>
    public static PressAmount? KitWanted(PressDie die, Construct c, int part, out int need, out PressPattern? pattern)
    {
        need = 0;
        if (ReadyBay(die, c, part, out pattern, out _) is var bay and >= 0)
        {
            need = Pressed.Bay2For(pattern!, c.Parts[PieceIn(c, part, bay)].D.ExKey with { Growing = false });
            return pattern!.Bay2;
        }
        pattern = SetPattern(die, c, part);
        if (pattern == null)
            foreach (var p in die.Patterns)
                if (p.Bay2 != null)
                {
                    pattern = p;
                    break;
                }
        return pattern?.Bay2;
    }

    /// <summary>How much more of its kit the hopper takes: twice the next stroke's, or a few strokes' when no blank is in.</summary>
    private static int KitRoom(CStore st, PressAmount kit, int need) =>
        Math.Max(0, (need > 0 ? need * 2 : Math.Min(kit.Max, kit.Part != null ? 12 : 60)) - KitHeld(st, kit));

    /// <summary>Server: a part into the hopper (a chute, or F). True when it went in; a part the pattern doesn't want is noted on the bay.</summary>
    public static bool FeedKitPart(ConstructWorld world, Construct c, int part, ConstructPartDef item)
    {
        if (!c.DefOf(part).IsPress || c.DefOf(part).IsLathe || world.StoreOf(c, part) is not { } st || SizedDieOf(PressTable.Current, c, part) is not { } die)
            return false;
        if (KitWanted(die, c, part, out var need, out _) is not { Part: { } want } kit || want.Id != item.Id)
        {
            SetRefusal(world, st, HopperBay, BayRefuse.Kit);
            return false;
        }
        if (KitRoom(st, kit, need) <= 0)
            return false;
        world.AddInputPart(st, item.Id, 1);
        return true;
    }

    /// <summary>Would the hopper take this part now? (A chute looking through its source: nothing is noted.)</summary>
    public static bool WantsKitPart(ConstructWorld world, Construct c, int part, ushort item) =>
        world.StoreOf(c, part) is { } st && SizedDieOf(PressTable.Current, c, part) is { } die && KitWanted(die, c, part, out var need, out _) is { Part: { } want } kit && want.Id == item && KitRoom(st, kit, need) > 0;

    /// <summary>Server: bulk material into the hopper (a chute, or F); how much it took.</summary>
    public static int FeedKitMaterial(ConstructWorld world, Construct c, int part, int amount, byte material)
    {
        if (amount <= 0 || !c.DefOf(part).IsPress || c.DefOf(part).IsLathe || world.StoreOf(c, part) is not { } st || SizedDieOf(PressTable.Current, c, part) is not { } die)
            return 0;
        if (KitWanted(die, c, part, out var need, out _) is not { Part: null } kit || kit.Material != material)
        {
            SetRefusal(world, st, HopperBay, BayRefuse.Kit);
            return 0;
        }
        var n = Math.Min(amount, KitRoom(st, kit, need));
        if (n > 0)
            world.AddInput(st, material, n);
        return n;
    }

    /// <summary>A piece in bay 2 (an older save, from when bay 2 took pieces) slides out of its mouth, loose. True when one did.</summary>
    private static bool LoosenHopperPiece(ConstructWorld world, Construct c, int part)
    {
        if (c.Parts[part].D.PressBays.Length <= HopperBay || PieceIn(c, part, HopperBay) is not (var piece and >= 0))
            return false;
        var name = c.Parts[piece].D.Name;
        if (TakeOut(world, c, part, piece, out _) != null)
            return false;
        Console.WriteLine($"[press] left 1 piece out of the {c.DefOf(part).Name}'s bay 2: {name} (bay 2 takes a kit now)");
        return true;
    }

    // ---- What comes in -------------------------------------------------------------------------------------------

    private static bool Free(Construct b) => b.PartCount == 1 && b.GrabbedBy == 255 && b.CarriedBy == 0 && b.Driver == 255 && !b.Anchored && !b.Frozen && b.CargoCount == 0;

    /// <summary>Loose pieces and dies left in the machine's mouths come in, or are refused. True when one did.</summary>
    private static bool TakeIn(ConstructWorld world, IWorldQuery query, PressTable table, Construct c, int part, CStore st)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        c.WorldFrame(p.Seg, out var q, out var t);
        for (var b = 0; b < d.PressBays.Length; b++)
        {
            if (PieceIn(c, part, b) >= 0 || b == HopperBay && !d.IsLathe)
                continue;
            ref readonly var bay = ref d.PressBays[b];
            var mid = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, (bay.Min + bay.Max) * 0.5f), q);
            var near = world.Near(mid, (bay.Max - bay.Min).Length() * 0.5f + 1f, _near);
            for (var k = 0; k < near.Count; k++)
            {
                var o = near[k];
                if (o == c || !Free(o) || o.Dirty || o.Parts[0].D is not { IsExtrusion: true, ExKey.Shape: not ExShape.Wheel } pd)
                    continue;
                if (o.TakenFrom == c.Id)
                {
                    // A player took it out: it stays out until it has left every mouth, or they pick it up.
                    var inMouth = false;
                    foreach (var m in d.PressBays)
                        inMouth |= EndIn(c, part, o, pd, m.Min, m.Max);
                    if (!inMouth)
                        o.TakenFrom = 0;
                    continue;
                }
                if (!EndIn(c, part, o, pd, bay.Min, bay.Max))
                    continue;
                var why = Refusal(table, c, part, b, pd.ExKey);
                if (why == BayRefuse.None && !Room(world, query, c, part, b, pd, o))
                    why = BayRefuse.Room;
                if (why != BayRefuse.None)
                {
                    SetRefusal(world, st, b, why);
                    continue;
                }
                var health = o.Parts[0].Health / MathF.Max(1, pd.Health);
                var paint = o.Parts[0].Paint;
                if (!FitPiece(world, c, part, b, pd.ExKey, health, paint))
                    continue;
                world.Remove(o);
                SetRefusal(world, st, 0, BayRefuse.None);
                return true;
            }
        }
        if (d.PressDieSocket < 0 || DieIn(c, part) >= 0)
            return false;
        var dieMid = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, (d.PressDieMin + d.PressDieMax) * 0.5f), q);
        var list = world.Near(dieMid, (d.PressDieMax - d.PressDieMin).Length() * 0.5f + 1f, _near);
        for (var k = 0; k < list.Count; k++)
        {
            var o = list[k];
            if (o == c || !Free(o) || o.Dirty || o.Parts[0].D is not { IsPressDie: true } die || !ConstructRecycler.InBox(c, part, d.PressDieMin, d.PressDieMax, o.WorldCom))
                continue;
            if (DieClassOf(die) != d.Sockets[d.PressDieSocket].Class)
            {
                SetRefusal(world, st, 3, BayRefuse.DieSize);
                continue;
            }
            if (!FitDie(world, c, part, die))
                continue;
            world.Remove(o);
            SetRefusal(world, st, 0, BayRefuse.None);
            return true;
        }
        return false;
    }

    /// <summary>
    /// A piece off a conveyor into bay <paramref name="bay"/> (its implicit Piece in: ConstructPorts.cs), by the same rules as a
    /// loose piece left in its mouth: true when it went in (fitted to the bay, a part of the machine's construct). Server.
    /// </summary>
    public static bool TakeFromConveyor(ConstructWorld world, IWorldQuery query, Construct c, int part, int bay, ConstructPartDef pd, float health, uint paint)
    {
        var d = c.Parts[part].D;
        if (d.IsPress && !d.IsLathe && bay == HopperBay)
        {
            // Bay 2 is the kit hopper: its Piece in feeds the first free bay that takes blanks (bay 3, else bay 1).
            bay = -1;
            for (var b = d.PressBays.Length - 1; b >= 0 && bay < 0; b--)
                if (b != HopperBay && PieceIn(c, part, b) < 0)
                    bay = b;
            if (bay < 0)
                return false;
        }
        if (!d.IsPress || bay >= d.PressBays.Length || c.Parts[part].Broken || PieceIn(c, part, bay) >= 0 || world.StoreOf(c, part) is not { } st)
            return false;
        if (!pd.IsExtrusion || pd.ExKey.Shape == ExShape.Wheel)
            return false; // (a stamped part riding into a bay waits: only pieces go in)
        var why = Refusal(PressTable.Current, c, part, bay, pd.ExKey);
        if (why == BayRefuse.None && !Room(world, query, c, part, bay, pd, null))
            why = BayRefuse.Room;
        if (why != BayRefuse.None)
        {
            if (st.Blocked == 0)
                SetRefusal(world, st, bay, why);
            return false;
        }
        if (!FitPiece(world, c, part, bay, pd.ExKey, health, paint))
            return false;
        SetRefusal(world, st, 0, BayRefuse.None);
        return true;
    }

    /// <summary>Why a bay won't take this piece, or None.</summary>
    public static BayRefuse Refusal(PressTable table, Construct c, int part, int bay, in ExtrusionKey key)
    {
        var d = c.Parts[part].D;
        if (!d.IsLathe)
        {
            if (bay == HopperBay)
                return BayRefuse.Hopper;
            return SizedDieOf(table, c, part) is { } sized ? SizedRefusal(sized, c, part, key, out _, out _) : BayRefuse.NoDie;
        }
        if (PressBaySpec.Across(key) > d.PressMaxCm)
            return BayRefuse.Size;
        var rows = RowsOf(table, c, part);
        if (rows.Length == 0)
            return BayRefuse.NoDie;
        var section = PressBaySpec.SectionOf(key);
        var material = false;
        var shape = false;
        var fits = false;
        foreach (var r in rows)
        {
            if (SpecFor(r, bay) is not { } s)
                continue; // (a one-bay row is every bay's: BayOf)
            if (Array.IndexOf(s.Materials, key.Material) < 0)
                continue;
            material = true;
            if (Array.IndexOf(s.Sections, section) < 0)
                continue;
            shape = true;
            if (!s.Takes(key))
                continue;
            fits = true;
            if (key.L >= s.StrokeCm(key))
                return BayRefuse.None;
        }
        return fits ? BayRefuse.Short : !material ? BayRefuse.Material : !shape ? BayRefuse.Section : BayRefuse.Size;
    }

    /// <summary>Is the end of a piece, or its middle, in a box of the machine?</summary>
    private static bool EndIn(Construct c, int part, Construct o, ConstructPartDef pd, Vector3 min, Vector3 max)
    {
        ref var op = ref o.Parts[0];
        o.WorldFrame(op.Seg, out var q, out var t);
        var a = t + Vector3.Transform(op.Pos, q);
        var axis = Vector3.Transform(Rot24.Apply(op.Rot, Vector3.UnitZ), q);
        return ConstructRecycler.InBox(c, part, min, max, a) || ConstructRecycler.InBox(c, part, min, max, a + axis * (pd.ExKey.L * 0.01f)) || ConstructRecycler.InBox(c, part, min, max, a + axis * (pd.ExKey.L * 0.005f));
    }

    private static byte BayTurn(byte rot, byte dir)
    {
        // The piece's +z leaves the bay; its +y stays as the machine's where that can be.
        for (byte r = 0; r < Rot24.Count; r++)
            if (Rot24.ApplyDir(r, 4) == dir && (dir >> 1 == 1 ? Rot24.ApplyDir(r, 0) == 0 : Rot24.ApplyDir(r, 2) == 2))
                return Rot24.Compose(rot, r);
        return rot;
    }

    /// <summary>Does the piece fit where it would stand, out of the bay's mouth? The ground, its own construct and other bodies all count; the body being taken doesn't.</summary>
    private static bool Room(ConstructWorld world, IWorldQuery query, Construct c, int part, int bay, ConstructPartDef piece, Construct? self)
    {
        ref var p = ref c.Parts[part];
        var s = p.D.Sockets[p.D.PressBays[bay].Socket];
        c.WorldFrame(p.Seg, out var q, out var t);
        var turn = q * Rot24.Quat(BayTurn(p.Rot, Rot24.ApplyDir(p.Rot, s.Dir)));
        var from = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, s.At), q);
        var mid = (piece.BoundsMin + piece.BoundsMax) * 0.5f;
        var half = Vector3.Max((piece.BoundsMax - piece.BoundsMin) * 0.5f - new Vector3(0.02f), new Vector3(0.01f));
        var obb = new Obb(from + Vector3.Transform(mid, turn), half, turn);
        if (query.OverlapBox(obb, QueryFilter.All))
            return false;
        var near = world.Near(obb.Center, half.Length() + 0.5f, _near2);
        for (var k = 0; k < near.Count; k++)
        {
            var o = near[k];
            if (o == self)
                continue;
            if (o.Dirty)
                o.Rebuild();
            if (ConstructExtruder.Hits(o, o == c ? part : -1, obb))
                return false;
        }
        return true;
    }

    private static readonly List<Construct> _near2 = new(32);

    /// <summary>A piece goes on a bay's socket: a part of this construct, unfinished as long as it stays there.</summary>
    internal static bool FitPiece(ConstructWorld world, Construct c, int part, int bay, in ExtrusionKey key, float health, uint paint)
    {
        if (c.PartCount >= Construct.MaxParts || Extrusion.Def(key with { Growing = true }) is not { } def)
            return false;
        ref var p = ref c.Parts[part];
        var d = p.D;
        var s = d.Sockets[d.PressBays[bay].Socket];
        var at = ConstructWorld.Quantise(p.Pos + Rot24.Apply(p.Rot, s.At));
        var i = world.AddPart(c, def, at, BayTurn(p.Rot, Rot24.ApplyDir(p.Rot, s.Dir)), p.Seg);
        if (i < 0)
            return false;
        world.AddLink(c, part, d.PressBays[bay].Socket, i, 0);
        c.Parts[i].Health = MathF.Max(1, def.Health * health);
        c.Parts[i].Paint = paint;
        c.Wake();
        return true;
    }

    /// <summary>A die's size class, the one its mount socket fits.</summary>
    public static byte DieClassOf(ConstructPartDef die) => die.Sockets[0].Class;

    /// <summary>A die goes on the machine's die socket.</summary>
    public static bool FitDie(ConstructWorld world, Construct c, int part, ConstructPartDef die)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var s = d.Sockets[d.PressDieSocket];
        var at = ConstructWorld.Quantise(p.Pos + Rot24.Apply(p.Rot, s.At - die.Sockets[0].At));
        var i = world.AddPart(c, die, at, p.Rot, p.Seg);
        if (i < 0)
            return false;
        world.AddLink(c, part, d.PressDieSocket, i, 0);
        if (world.StoreOf(c, part) is { } st)
            SetRefusal(world, st, 0, BayRefuse.None); // (what the last die turned away says nothing now)
        c.Wake();
        return true;
    }

    /// <summary>The bay a piece would go in, or -1 (and why not, for the last bay tried).</summary>
    private static int BayFor(PressTable table, ConstructWorld world, IWorldQuery? query, Construct c, int part, in ExtrusionKey key, ConstructPartDef def, out BayRefuse why, out int refusedBay)
    {
        why = BayRefuse.NoDie;
        refusedBay = 0;
        var bays = c.Parts[part].D.PressBays.Length;
        for (var b = 0; b < bays; b++)
        {
            if (PieceIn(c, part, b) >= 0)
                continue;
            var w = Refusal(table, c, part, b, key);
            if (w == BayRefuse.None && query != null && !Room(world, query, c, part, b, def, null))
                w = BayRefuse.Room;
            if (w == BayRefuse.None)
                return b;
            why = w;
            refusedBay = b;
        }
        return -1;
    }

    // ---- Chutes --------------------------------------------------------------------------------------------------

    /// <summary>Does a chute run from this part into a press? An Extruder with one hands its pieces to the chute rather than letting them fall.</summary>
    public static bool ChutesToPress(ConstructWorld world, Construct c, int part)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type == RouteType.Chute && r.Source == part && r.To < c.PartCount && c.Parts[r.To].D.IsPress)
                return true;
        }
        for (var k = 0; k < world.WorldLinkCount; k++)
        {
            ref var l = ref world.WorldLinks[k];
            if (l.Type == RouteType.Chute && l.BodyFrom == c.Id && l.From == part && world.Get(l.BodyTo) is { } to && l.To < to.PartCount && to.Parts[l.To].D.IsPress)
                return true;
        }
        return false;
    }

    /// <summary>
    /// A chute's move into a press: a finished piece from an Extruder, a die from a shelf if the slot is empty. Bulk is refused: a press
    /// takes pieces. The delivery itself waits for the next step (<see cref="Feed"/>). True when something is on its way.
    /// </summary>
    public static bool Chute(ConstructWorld world, Construct cf, int from, CStore src, ConstructPartDef fd, Construct ct, int to, CStore into)
    {
        if (fd.IsExtruder && !fd.IsWheelFormer)
        {
            var piece = ConstructExtruder.ProductOf(cf, from);
            if (piece < 0 || !cf.Parts[piece].D.IsExtrusion || !ConstructExtruder.DueOff(cf, from, piece))
                return false;
            var key = cf.Parts[piece].D.ExKey;
            if (BayFor(PressTable.Current, world, null, ct, to, key, cf.Parts[piece].D, out var why, out var bay) < 0)
            {
                SetRefusal(world, into, bay, why);
                return false;
            }
            Queue(world, new Feed(cf.Id, (ushort)from, ct.Id, (ushort)to, ConstructParts.None));
            return true;
        }
        var d = ct.DefOf(to);
        if (d.PressDieSocket >= 0 && DieIn(ct, to) < 0)
            for (var s = 0; s < src.SlotCount; s++)
                if (ConstructParts.Get(src.Def[s]) is { IsPressDie: true } die && DieClassOf(die) == d.Sockets[d.PressDieSocket].Class && world.TakeOut(cf, from, s) is { } moved)
                {
                    Queue(world, new Feed(cf.Id, (ushort)from, ct.Id, (ushort)to, moved.Id));
                    return true;
                }
        if (!d.IsLathe)
        {
            // Bay 2's kit: bulk from a bin, a part from a store's slots or kinds (as into an Assembler's input).
            if ((fd.IsBin || fd.IsTerminal) && src.Amount > 0)
            {
                var put = FeedKitMaterial(world, ct, to, Math.Min(Math.Max(fd.ChuteLoad, d.ChuteLoad), src.Amount), src.Material);
                if (put > 0)
                    world.Withdraw(cf, from, put);
                return put > 0;
            }
            for (var s = 0; s < src.SlotCount; s++)
                if (src.Def[s] != ConstructParts.None && WantsKitPart(world, ct, to, src.Def[s]))
                    return world.TakeOut(cf, from, s) is { } moved && FeedKitPart(world, ct, to, moved);
            if (!fd.IsAssembler && !fd.IsPress)
                for (var k = src.KindN - 1; k >= 0; k--)
                    if (WantsKitPart(world, ct, to, src.KindDef![k]))
                        return world.TakePart(cf, from, src.KindDef[k]) is { } moved && FeedKitPart(world, ct, to, moved);
            return false;
        }
        if (fd.IsBin && src.Amount > 0)
            SetRefusal(world, into, 0, BayRefuse.Bulk);
        return false;
    }

    private static void Queue(ConstructWorld world, in Feed f)
    {
        if (world.PressFeeds.Count < world.PressFeeds.Capacity)
            world.PressFeeds.Add(f);
    }

    private static void Deliver(ConstructWorld world, IWorldQuery query, in Feed f)
    {
        if (world.Get(f.ToBody) is not { } to)
            return;
        if (to.Dirty)
            to.Rebuild();
        if (f.ToPart >= to.PartCount || !to.Parts[f.ToPart].D.IsPress || world.StoreOf(to, f.ToPart) is not { } st)
            return;
        if (f.Die != ConstructParts.None)
        {
            if (ConstructParts.Get(f.Die) is { } die && (DieIn(to, f.ToPart) >= 0 || !FitDie(world, to, f.ToPart, die)))
                world.TrayAdd(st, to.Parts[f.ToPart].D, die); // (the slot filled meanwhile: it isn't lost)
            return;
        }
        if (world.Get(f.FromBody) is not { } from)
            return;
        if (from.Dirty)
            from.Rebuild();
        if (f.FromPart >= from.PartCount || !from.Parts[f.FromPart].D.IsExtruder)
            return;
        var piece = ConstructExtruder.ProductOf(from, f.FromPart);
        if (piece < 0 || from.Parts[piece].D is not { IsExtrusion: true } pd)
            return;
        var bay = BayFor(PressTable.Current, world, query, to, f.ToPart, pd.ExKey, pd, out var why, out var at);
        if (bay < 0)
        {
            SetRefusal(world, st, at, why);
            return;
        }
        var health = from.Parts[piece].Health / MathF.Max(1, pd.Health);
        if (FitPiece(world, to, f.ToPart, bay, pd.ExKey, health, from.Parts[piece].Paint))
            world.RemovePart(from, piece);
    }

    // ---- Hands ---------------------------------------------------------------------------------------------------

    /// <summary>What F takes out of the machine: the nearest of what is in its bays and die slot to the eye. A part index, or -1.</summary>
    public static int Nearest(Construct c, int part, Vector3 eye)
    {
        var d = c.Parts[part].D;
        var best = -1;
        var dist = float.MaxValue;
        void Try(int i, Vector3 at)
        {
            if (i < 0)
                return;
            var f = Vector3.DistanceSquared(at, eye);
            if (f < dist)
            {
                dist = f;
                best = i;
            }
        }
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        for (var b = 0; b < d.PressBays.Length; b++)
            Try(PieceIn(c, part, b), t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, d.Sockets[d.PressBays[b].Socket].At), q));
        if (d.PressDieSocket >= 0)
            Try(DieIn(c, part), t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, (d.PressDieMin + d.PressDieMax) * 0.5f), q));
        return best;
    }

    /// <summary>Server: a piece or die leaves the machine, in any state: whole, with its health and paint (a stroke takes its length only when it ends, so one under way just stops). A piece slides out of its mouth as a body and stays out until it is moved. Null, or why not.</summary>
    public static string? TakeOut(ConstructWorld world, Construct c, int part, int held, out ConstructPartDef? die) => TakeOut(world, c, part, held, out die, out _);

    public static string? TakeOut(ConstructWorld world, Construct c, int part, int held, out ConstructPartDef? die, out Construct? piece)
    {
        die = null;
        piece = null;
        var d = c.Parts[held].D;
        if (d.IsPressDie)
        {
            die = world.RemovePart(c, held);
            return null;
        }
        var st = world.StoreOf(c, part);
        var body = ConstructExtruder.Cut(world, c, held);
        if (body == null)
            return "the piece has no room to come off";
        piece = body;
        if (st != null && c.DefOf(part).IsPress)
        {
            st.JobMaterial = CoolSteps;
            body.TakenFrom = c.Id;
        }
        var axis = Vector3.Transform(Vector3.UnitZ, body.Rot);
        body.Vel += axis * 0.6f;
        return null;
    }

    // ---- What it says --------------------------------------------------------------------------------------------

    private static string WhyText(BayRefuse why) => why switch
    {
        BayRefuse.NoDie => "needs a die first",
        BayRefuse.Material => "wrong material",
        BayRefuse.Section => "wrong section",
        BayRefuse.Size => "wrong size",
        BayRefuse.Room => "no room for it",
        BayRefuse.DieSize => "wrong die size",
        BayRefuse.Bulk => "takes pieces, not bulk",
        BayRefuse.Short => "too short",
        BayRefuse.Bounds => "out of range",
        BayRefuse.Pattern => "not its pattern",
        BayRefuse.Hopper => "kits only",
        BayRefuse.Kit => "not its kit",
        _ => "",
    };

    /// <summary>A sized die's reason: a piece too wide names this press's class, so the player knows to take it to a bigger one.</summary>
    private static string SizedWhy(BayRefuse why, ConstructPartDef press) => why == BayRefuse.Size ? PressWide[Math.Clamp(Pressed.PressClass(press.PressMaxCm), 0, PressWide.Length - 1)] : WhyText(why);

    private static readonly string[] PressWide = ["too wide: Medium Press", "too wide: Large Press", "too wide: Capital Press", "too wide for any press"];

    /// <summary>The same on a bay's card, which is narrow (12 characters on a Medium Press): its range line beside it says how wide.</summary>
    private static string SizedWhyShort(BayRefuse why) => why == BayRefuse.Size ? "too wide" : WhyText(why);

    /// <summary>The bay a refusal is about, and why (from the wire's byte).</summary>
    private static BayRefuse RefusedAt(CStore st, out int bay)
    {
        bay = st.Blocked & 3;
        return (BayRefuse)(st.Blocked >> 2);
    }

    private static string SectionName(int section) => section switch
    {
        PressBaySpec.RoundSection => "Round",
        PressBaySpec.PipeSection => "Pipe",
        >= 0 and < 12 => Extrusion.ProfileLabel(Extrusion.Presets[Math.Min(section, Extrusion.Presets.Length - 1)].Mask),
        _ => "Custom",
    };

    private static ulong SectionMask(int section) => section switch
    {
        PressBaySpec.RoundSection => Extrusion.RoundMask(0),
        PressBaySpec.PipeSection => Extrusion.RoundMask(0.6f),
        >= 0 when section < Extrusion.Presets.Length => Extrusion.Presets[section].Mask,
        _ => 0,
    };

    /// <summary>A bay's panel: what it wants on this die, and how it stands. The card a player reads from where they feed it.</summary>
    public static void FillBay(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var bay = panel.Slot;
        var names = RecyclingTable.Current;
        card.Add(0, "Bay ").Add(0, bay + 1);
        var rows = RowsOf(PressTable.Current, c, part);
        var row = RowFor(rows, c, part);
        var st = world.StoreOf(c, part);
        var at = 0;
        var refused = st == null ? BayRefuse.None : RefusedAt(st, out at);
        if (at != bay)
            refused = BayRefuse.None;
        var piece = PieceIn(c, part, bay);
        if (!d.IsLathe && SizedDieOf(PressTable.Current, c, part) is { } sized)
        {
            if (bay == HopperBay)
                FillHopper(c, part, card, sized, st, refused);
            else
                FillSizedBay(c, part, card, sized, piece, refused);
            return;
        }
        if (row == null)
        {
            card.State = piece >= 0 ? PanelState.Wants : PanelState.Idle;
            card.Add(1, d.IsLathe ? "No die chosen" : "No die fitted");
            card.Add(2, d.IsLathe ? "Pick one: configure" : "Fit a die first");
            return;
        }
        if (SpecFor(row, bay) is not { } spec)
        {
            card.State = PanelState.Idle;
            card.Add(1, "Not used");
            card.Add(2, "by this die");
            return;
        }
        card.Add(0, ": ").Add(0, spec.Label);
        // What the bay takes over all this die's rows, unless a piece has settled which row it is: the panel says what will do, not one line of the table.
        var one = piece >= 0 || rows.Length == 1;
        var material = spec.Materials[0];
        var section = spec.Sections[0];
        card.HasIcon = true;
        card.IconColour = names.ColourOf(material);
        card.IconMask = SectionMask(section);
        var lo = spec.MinCm;
        var hi = spec.MaxCm;
        var mats = 0;
        var secs = 0;
        Span<byte> seen = stackalloc byte[3];
        Span<int> shown = stackalloc int[2];
        foreach (var r in rows)
        {
            if (one && r != row || SpecFor(r, bay) is not { } s)
                continue;
            lo = Math.Min(lo, s.MinCm);
            hi = Math.Max(hi, s.MaxCm);
            foreach (var m in s.Materials)
                if (mats < 3 && seen[..mats].IndexOf(m) < 0)
                {
                    seen[mats++] = m;
                    Choice(card, 1, names.NameOf(m), mats);
                }
            foreach (var k in s.Sections)
                if (secs < 2 && shown[..secs].IndexOf(k) < 0)
                {
                    shown[secs++] = k;
                    Choice(card, 2, SectionName(k), secs);
                }
        }
        card.AddMetres(3, lo).Add(3, " to ").AddMetres(3, Math.Min(hi, d.PressMaxCm));
        if (refused != BayRefuse.None)
        {
            card.State = PanelState.Refused;
            card.Add(4, WhyText(refused));
            return;
        }
        if (piece < 0)
        {
            card.State = PanelState.Wants;
            card.Add(4, "Wants a piece");
            return;
        }
        ref readonly var key = ref c.Parts[piece].D.ExKey;
        var use = spec.StrokeCm(key);
        card.State = spec.Takes(key) ? key.L >= use ? PanelState.Ok : PanelState.Wants : PanelState.Refused;
        card.AddMetres(4, key.L);
        if (card.State == PanelState.Ok)
            card.Add(4, ": ").Add(4, key.L / use).Add(4, " left");
        else if (card.State == PanelState.Wants)
            card.Add(4, ": too short");
        else
            card.Add(4, ": wrong");
    }

    /// <summary>
    /// A bay's card under a sized die: what blanks it takes (materials, sections, the sizes across on this press) until a piece is in, then
    /// that piece (material, section, size) and what the stroke will make of it: the preset's name, or the pattern's (it is sized by the piece).
    /// </summary>
    private static void FillSizedBay(Construct c, int part, PanelCard card, PressDie die, int piece, BayRefuse refused)
    {
        var d = c.Parts[part].D;
        var names = RecyclingTable.Current;
        card.Add(0, ": Blank");
        card.HasIcon = true;
        if (piece >= 0 && refused == BayRefuse.None)
        {
            ref readonly var k = ref c.Parts[piece].D.ExKey;
            card.IconColour = names.ColourOf(k.Material);
            card.IconMask = k.Mask;
            card.Add(1, names.NameOf(k.Material));
            card.Add(2, Extrusion.PresetOf(k.Mask) is var sec and >= 0 ? SectionName(sec) : "Custom");
            card.AddShort(3, k.W / 100f).Add(3, 'x').AddShort(3, k.H / 100f).Add(3, 'x').AddShort(3, k.L / 100f).Add(3, 'm');
            var why = SizedRefusal(die, c, part, k, out var pattern, out var preset);
            if (why == BayRefuse.None)
            {
                card.State = PanelState.Ok;
                SizedMakes(card, 4, pattern!, preset);
            }
            else
            {
                card.State = PanelState.Refused;
                card.Add(4, SizedWhyShort(why));
            }
            return;
        }
        card.IconColour = names.ColourOf(die.Materials[0]);
        card.IconMask = SectionMask(die.Blanks[0]);
        for (var m = 0; m < die.Materials.Length; m++)
            Choice(card, 1, names.NameOf(die.Materials[m]), m + 1);
        for (var b = 0; b < die.Blanks.Length && b < 2; b++) // (two at most, as a row's card: a "+" after two would run off a Small Press's card)
            Choice(card, 2, SectionName(die.Blanks[b]), b + 1);
        card.AddShort(3, die.AcrossMin / 100f).Add(3, '-').AddShort(3, Math.Min(die.AcrossMax, d.PressMaxCm) / 100f).Add(3, " m");
        if (refused != BayRefuse.None)
        {
            card.State = PanelState.Refused;
            card.Add(4, SizedWhyShort(refused));
            return;
        }
        card.State = PanelState.Wants;
        card.Add(4, "Wants a blank");
    }

    /// <summary>
    /// Bay 2's card: the kit the pattern wants (a part or a material), what the next stroke takes of it for the blank in, and what the hopper
    /// holds; read before the stroke, so a player sees "needs 3, holds 1" and tops it up.
    /// </summary>
    private static void FillHopper(Construct c, int part, PanelCard card, PressDie die, CStore? st, BayRefuse refused)
    {
        card.Add(0, ": Kit");
        var kit = KitWanted(die, c, part, out var need, out _);
        if (kit == null)
        {
            card.State = PanelState.Idle;
            card.Add(1, "No kit");
            card.Add(2, "for this");
            card.Add(3, "pattern");
            return;
        }
        var names = RecyclingTable.Current;
        card.HasIcon = kit.Part == null;
        if (kit.Part == null)
            card.IconColour = names.ColourOf(kit.Material);
        card.Add(1, kit.What);
        var held = st == null ? 0 : KitHeld(st, kit);
        if (need > 0)
            card.Add(2, "needs ").Add(2, need);
        else
            card.Add(2, "per blank");
        card.Add(3, "holds ").Add(3, held);
        if (refused != BayRefuse.None)
        {
            card.State = PanelState.Refused;
            card.Add(4, WhyText(refused));
            return;
        }
        card.State = need > 0 && held >= need ? PanelState.Ok : PanelState.Wants;
        card.Add(4, need == 0 ? "Wants a blank" : held >= need ? "Enough" : "Too little");
    }

    /// <summary>Touch "press.pattern": the next of the fitted die's patterns, then Auto (the Pattern setting, as the configure tool sets it).</summary>
    public static string? NextPattern(ConstructWorld world, Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!d.IsPress || d.IsLathe || d.CfgSlot[(int)CfgKey.Recipe] < 0)
            return "this machine has no patterns";
        if (SizedDieOf(PressTable.Current, c, part) is not { } die)
            return "fit a die first";
        var next = ((int)ConstructConfig.Get(p, d, CfgKey.Recipe, 0) + 1) % (die.Patterns.Length + 1);
        ConstructConfig.Put(ref p, d, CfgKey.Recipe, next);
        return null;
    }

    /// <summary>What a bay's line beside the icon holds of a list: as many as fit, then a "+". Text never runs off the screen.</summary>
    private static void Choice(PanelCard card, int line, string name, int nth)
    {
        var had = card.Line(line).Length;
        if (nth == 1)
            card.Add(line, name);
        else if (had > 0 && card.Line(line)[had - 1] == '+')
            return;
        else if (had + 1 + name.Length <= BesideIcon)
            card.Add(line, '/').Add(line, name);
        else
            card.Add(line, '+');
    }

    /// <summary>The machine's own panel: its die, what it is making, why not.</summary>
    public static void FillDie(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var rows = RowsOf(PressTable.Current, c, part);
        var row = RowFor(rows, c, part);
        var die = DieIn(c, part);
        card.Add(0, d.Name);
        if (d.IsLathe)
            card.Add(1, "Cuts: ").Add(1, row == null ? "nothing" : row.Output.Name);
        else
            card.Add(1, "Die: ").Add(1, die >= 0 ? c.Parts[die].D.Name : "none");
        if (!d.IsLathe && SizedDieOf(PressTable.Current, c, part) is { } sized)
        {
            // The Pattern: the set one's label, or on Auto what the ready blank makes.
            if (SetPattern(sized, c, part) is { } set)
                card.Add(2, "Pattern: ").Add(2, set.Label);
            else if (ReadyBay(sized, c, part, out var pattern, out var preset) >= 0)
                card.Add(2, "Auto: ").Add(2, preset?.Name ?? pattern!.Label);
            else
                card.Add(2, "Pattern: auto");
        }
        else if (row != null && !d.IsLathe)
            card.Add(2, "Makes ").Add(2, row.Output.Name);
        var st = world.StoreOf(c, part);
        if (st == null)
            return;
        if (st.Items > 0)
        {
            card.Add(0, ", tray ").Add(0, st.Items);
        }
        if (st.Held)
        {
            card.State = PanelState.Idle;
            card.Add(3, "Held off");
            return;
        }
        var why = (PressStall)st.Refused;
        card.State = st.Busy ? PanelState.Working : why switch
        {
            PressStall.None => PanelState.Ok,
            PressStall.NoDie or PressStall.Wants or PressStall.TooShort or PressStall.NoRecipe or PressStall.Bay2Short => PanelState.Wants,
            _ => PanelState.Fault,
        };
        card.Add(3, st.Busy ? (d.IsLathe ? "Cutting" : "Stamping") : StallText(why));
    }

    internal static string StallText(PressStall s) => s switch
    {
        PressStall.NoPower => "No power",
        PressStall.NoDie => "Fit a die",
        PressStall.NoRecipe => "Choose a die",
        PressStall.WrongClass => "Wrong size of lathe",
        PressStall.Wants => "Bays need pieces",
        PressStall.TooShort => "A piece is too short",
        PressStall.TrayFull => "Tray full",
        PressStall.Blocked => "Output blocked",
        PressStall.ConveyorFull => "Output conveyor full",
        PressStall.ConveyorSmall => "Conveyor too narrow",
        PressStall.ConveyorCut => "Output conveyor cut",
        PressStall.Bay2Short => "Bay 2 needs its kit",
        _ => "Ready",
    };

    /// <summary>The look line: <c>Name: state - problem - KEY: action</c>.</summary>
    public static string Look(Construct c, int part, CStore st, bool near, ConstructPartDef? held, Vector3 eye)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var row = RowFor(RowsOf(PressTable.Current, c, part), c, part);
        var die = DieIn(c, part);
        var sized = SizedDieOf(PressTable.Current, c, part);
        var state = d.IsLathe ? row == null ? "nothing chosen" : "cuts " + row.Output.Name : die < 0 ? "no die" : c.Parts[die].D.Name;
        if (st.Items > 0)
            state += $", tray {st.Items}";
        string? problem = p.Broken ? "wrecked"
            : st.Held ? "held off by its Run wire"
            : st.Busy ? null
            : (PressStall)st.Refused switch
            {
                PressStall.None => null,
                PressStall.Bay2Short when sized != null && KitWanted(sized, c, part, out var need, out _) is { } kit => $"needs {need} {kit.What} in bay 2, holds {KitHeld(st, kit)}",
                PressStall.Wants when sized != null => $"needs a blank in a bay: {SectionName(sized.Blanks[0]).ToLowerInvariant()}{(sized.Blanks.Length > 1 ? " or another" : "")}, {sized.AcrossMin / 100f:0.00} to {Math.Min(sized.AcrossMax, d.PressMaxCm) / 100f:0.00} m across",
                PressStall.Wants when row != null => "needs " + Wants(row, c, part),
                var s => StallText(s).ToLowerInvariant(),
            };
        var why = RefusedAt(st, out var bay);
        if (why != BayRefuse.None && sized != null && bay != 3)
            problem = $"bay {bay + 1}: {SizedWhy(why, d)}" + (why == BayRefuse.Bounds
                ? $", the {sized.Family} Die takes {sized.AcrossMin / 100f:0.00} to {Math.Min(sized.AcrossMax, d.PressMaxCm) / 100f:0.00} m across, {sized.LengthMin / 100f:0.00} to {sized.LengthMax / 100f:0.00} m long, {sized.AspectMin:0.##} to {sized.AspectMax:0.##} times as long"
                : "");
        else if (why != BayRefuse.None)
            problem = (bay == 3 ? "die slot: " : $"bay {bay + 1}: ") + WhyText(why) + (why is BayRefuse.Material or BayRefuse.Section or BayRefuse.Size && row != null && SpecFor(row, bay) is { } want ? ", wants " + Spec(want) : "");
        string? action = null;
        if (near)
        {
            var take = Nearest(c, part, eye);
            action = held is { IsPressDie: true } ? SandboxKeys.Do(SandboxKeys.Use, die >= 0 ? "swap the die" : "fit the die")
                : take >= 0 ? SandboxKeys.Do(SandboxKeys.Use, c.Parts[take].D.IsPressDie ? "take the die out" : "take the piece out")
                : ConstructAssembling.TopItem(st) is { } top ? SandboxKeys.Do(SandboxKeys.Use, "take the " + top.Name)
                : null;
        }
        return SandboxKeys.Line(d.Name, state, problem, action);
    }

    /// <summary>The look line's key: what is in the bays and die slot, which the store's version doesn't follow.</summary>
    public static int LookKey(Construct c, int part)
    {
        var h = 17;
        for (var b = 0; b < c.Parts[part].D.PressBays.Length; b++)
            h = h * 31 + (PieceIn(c, part, b) is var i and >= 0 ? (int)c.Parts[i].D.ExKey.Stable() : 0);
        return h * 31 + (DieIn(c, part) is var k and >= 0 ? c.Parts[k].Def : 0);
    }

    private static string Spec(PressBaySpec s) => $"{RecyclingTable.Current.NameOf(s.Materials[0])} {SectionName(s.Sections[0]).ToLowerInvariant()}";

    private static string Wants(PressRow r, Construct c, int part)
    {
        if (r.Bays.Length == 1)
            return $"{Spec(r.Bays[0])} in a bay"; // (a one-bay row is every bay's: BayOf)
        for (var b = 0; b < r.Bays.Length; b++)
            if (PieceIn(c, part, b) < 0)
                return $"{Spec(r.Bays[b])} in bay {b + 1}";
        return "the right pieces";
    }

    // ---- Part files ----------------------------------------------------------------------------------------------

    /// <summary>A part file's <c>storage.press</c> block: a press, or with <c>"lathe": true</c> a Die Lathe.</summary>
    public static void Parse(ConstructPartDef d, JsonElement b, string file, int slots)
    {
        var bw = $"{file}, storage.press";
        if (b.ValueKind != JsonValueKind.Object || d.BulkCapacity != 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner || d.IsAssembler || slots == 0)
            throw new ConstructPartException($"{file}: storage \"press\" is an object (bays, die, maxAcross, speed, outAt, outDir) on a part with \"slots\", its output tray, and no \"bulk\"");
        ConstructParts.Known(b, ["bays", "die", "lathe", "maxAcross", "speed", "outAt", "outDir"], bw);
        d.IsLathe = ConstructParts.Opt(b, "lathe") is { ValueKind: JsonValueKind.True };
        var list = ConstructParts.Need(b, "bays", bw);
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > PressTable.MaxBays)
            throw new ConstructPartException($"{bw}: \"bays\" lists 1 to {PressTable.MaxBays} bays ({{socket, min, max}})");
        var bays = new List<PressBayDef>();
        foreach (var e in list.EnumerateArray())
        {
            ConstructParts.Known(e, ["socket", "min", "max"], bw);
            var name = ConstructParts.Str(ConstructParts.Need(e, "socket", bw), file, "storage.press.bays.socket");
            var s = Array.FindIndex(d.Sockets, x => x.Name == name && x.Kind == SocketKind.Die);
            if (s < 0)
                throw new ConstructPartException($"{bw}: bay socket \"{name}\" isn't one of the part's sockets of kind \"die\"");
            bays.Add(new PressBayDef(s, ConstructParts.Vec(ConstructParts.Need(e, "min", bw), file, "storage.press.bays.min"), ConstructParts.Vec(ConstructParts.Need(e, "max", bw), file, "storage.press.bays.max")));
        }
        d.PressBays = bays.ToArray();
        if (ConstructParts.Opt(b, "die") is { } die)
        {
            if (d.IsLathe)
                throw new ConstructPartException($"{bw}: a lathe has no die slot");
            ConstructParts.Known(die, ["socket", "min", "max"], bw);
            var name = ConstructParts.Str(ConstructParts.Need(die, "socket", bw), file, "storage.press.die.socket");
            d.PressDieSocket = Array.FindIndex(d.Sockets, x => x.Name == name && x.Kind == SocketKind.PressDie);
            if (d.PressDieSocket < 0)
                throw new ConstructPartException($"{bw}: die socket \"{name}\" isn't one of the part's sockets of kind \"pressdie\"");
            d.PressDieMin = ConstructParts.Vec(ConstructParts.Need(die, "min", bw), file, "storage.press.die.min");
            d.PressDieMax = ConstructParts.Vec(ConstructParts.Need(die, "max", bw), file, "storage.press.die.max");
        }
        else if (!d.IsLathe)
            throw new ConstructPartException($"{bw}: a press has a \"die\" slot");
        var across = ConstructParts.Num(ConstructParts.Need(b, "maxAcross", bw), file, "storage.press.maxAcross");
        ConstructParts.Range(across, 0.1f, Extrusion.MaxSectionCm / 100f, file, "storage.press.maxAcross");
        d.PressMaxCm = (int)MathF.Round(across * 100);
        var speed = ConstructParts.Opt(b, "speed") is { } sp ? ConstructParts.Num(sp, file, "storage.press.speed") : 1f;
        ConstructParts.Range(speed, 0.1f, 100, file, "storage.press.speed");
        d.RefineWork = (int)MathF.Round(speed * ConstructRefining.BaseWork);
        d.AsmOutAt = ConstructParts.Vec(ConstructParts.Need(b, "outAt", bw), file, "storage.press.outAt");
        var dir = ConstructParts.Str(ConstructParts.Need(b, "outDir", bw), file, "storage.press.outDir");
        d.AsmOutDir = ConstructParts.DirVector(ConstructParts.DirOf(dir, file, "storage.press.outDir"));
        d.IsPress = true;
        d.DataIn = [ConstructGatherer.RunPort];
    }

    /// <summary>A part file's <c>"pressDie"</c>: its family in Pressing.json. Its first socket is the mount.</summary>
    public static void ParseDie(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "pressDie") is not { } e)
            return;
        d.DieFamily = ConstructParts.Str(e, file, "pressDie");
        if (d.DieFamily.Length == 0 || d.Sockets.Length == 0 || d.Sockets[0].Kind != SocketKind.PressDie)
            throw new ConstructPartException($"{file}: a die names its family, and its first socket, of kind \"pressdie\", is its mount");
        d.IsPressDie = true;
    }
}

/// <summary>One bay of a press: its socket and the box a loose piece's end is taken from.</summary>
public readonly record struct PressBayDef(int Socket, Vector3 Min, Vector3 Max);

/// <summary>The press's side of the world.</summary>
public sealed partial class ConstructWorld
{
    public int PressTicks;
    public readonly List<ConstructPress.Feed> PressFeeds = new(32);
}
