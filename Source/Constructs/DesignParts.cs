using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts (the spawnable ship designs plan, steps 1 and 2, Karl 2026-11-26): a Ship Designer design (Source/ShipDesigner) as one run-time
/// construct part, <c>Design (#hash)</c>, never converted into the catalogue's parts. A spawned design is a body holding that one part.
/// <para>
/// <b>The design.</b> Its JSON, read and written again by <see cref="ShipDesign"/> without indents (<see cref="Canon"/>), so one design always gives
/// one hash. The designs live in a table every peer shares by hash (<see cref="Known"/>), and each world keeps those its bodies use
/// (<see cref="ConstructWorld.ShipDesigns"/>): saved with the world in a section of its own ahead of the part names (WorldSaveWriter.ShipDesignTag),
/// sent to a joiner before any body and to everyone as each comes (<see cref="CBodyKind.ShipDesign"/>), as the Bake Oven's designs are
/// (BakedParts.cs). A design read from the wire or a save is checked against its hash and must read as a design.
/// </para>
/// <para>
/// <b>The part.</b> Drawn as the designer's own meshes (skin, cladding, joins, pieces), merged into one vertex-coloured mesh (<see cref="Look"/>,
/// drawn by Renderer.Design.cs). It collides as convex hulls (ConstructHulls, the narrow phase's hull tests, the collider's tree): the skin hollow, as thin
/// plates where the designer's skin is (each band's facets and the two end caps, openings left open: DesignParts.Hull.cs), and one or a few for
/// each join and each piece (from its mesh's extreme points, kept out of the rooms). Its
/// solid boxes are hidden and stand for no collider (every one is covered by the hulls): they carry the mass, each its own share
/// (<see cref="ConstructPartDef.BoxMass"/>): the skin's area at <see cref="SkinKgPerM2"/>, the rooms' floors at <see cref="DeckKgPerM2"/> and the
/// pieces' surfaces at <see cref="PieceKgPerM2"/>.
/// </para>
/// <para>
/// <b>The files</b> (one partial class; each kind of element is one file's, so builders working on different kinds never share one):
/// <list type="bullet">
/// <item>DesignParts.cs (this one): the table of designs, <see cref="Element"/>, the steps' pipeline (<see cref="ElementSteps"/>), the kinds'
/// tables (<see cref="KindTable"/>), the part a key names (<see cref="PartOf"/>), the body built (<see cref="BuildSteps"/>), the look, the wire and the save.</item>
/// <item>DesignParts.Hull.cs: the bands, the joins and the colliders taken from a mesh. DesignParts.Pieces.cs: the pieces, the drives, RCS,
/// seats and the flight computer. DesignParts.Rooms.cs: the rooms. DesignParts.Doors.cs, DesignParts.Risers.cs, DesignParts.Systems.cs:
/// doors and hatches; ladders and lifts; tow points, lights, clamps, guns and turrets. DesignParts.Templates.cs: the part files a working def starts from.</item>
/// </list>
/// A kind file gives the pipeline a stage (<c>AddX(Steps)</c>, called in order from <see cref="ElementSteps"/>) and fills the tables in its
/// <c>RegisterX(KindTable)</c>: what a piece kind does (<see cref="KindTable.Piece"/>, called for each of its copies) and, for an
/// <see cref="Element.Fn"/>, the def it starts from and what is set on it once the structure's numbers are in (<see cref="KindTable.Fn"/>).
/// </para>
/// </summary>
public static partial class DesignParts
{
    public const string Prefix = "Design (#";
    public const int MaxText = 1 << 20; // bytes of one design's text
    public const int MaxWorld = 256; // designs one world keeps
    public const int MaxHulls = 1024; // colliders one design makes (its bands', joins' and pieces' hulls, its rooms' boxes and thin hulls, one for an element with only its mass box: DesignParts.Hull.cs Colliders; past it the last are left out, logged)

    // ---- Mass: plain figures, all here (Karl's plan: skin area times a plate density, plus decks and pieces) ----
    public const float SkinKgPerM2 = 80f; // a hull plate, about 1 cm of steel with its frames
    public const float DeckKgPerM2 = 60f; // a room's floor with its beams
    public const float PieceKgPerM2 = 150f; // a piece's drawn surface (drives, guns, legs, masts: machinery, denser than skin)
    public const float HpPerKg = 0.5f; // the part's health from its mass

    // ---- Flight (step 3, Karl 2026-11-26): what the design's drives, RCS and seats do, all here --------------------------------------
    // Thrust at Size 1, growing with the square of a piece's size (its bell's mouth). The drives and the RCS run on nothing for now: a
    // design carries no tanks yet, so its thrusters are unfed (EngineFuel.None) in Creative and Survival alike.
    public const float MainDriveN = 3.0e6f; // a 4 m bell
    public const float SmallDriveN = 0.8e6f; // a 2 m bell
    public const float RcsNozzleN = 5e6f; // each of an RCS quad's four nozzles (the Heavy RCS: its scale squared more)
    public const float MainDriveBell = 3.4f, SmallDriveBell = 1.8f; // m from the skin to a bell's mouth (PieceKinds' drawings)
    public const float NozzleShare = 0.04f; // of an RCS quad's mass, each nozzle's part
    public const float SeatKg = 80f, FlightKg = 50f; // a seat with its console; the flight computer

    /// <summary>A design's look: one merged mesh in the part's frame, flat normals and a colour a vertex (drawn by Renderer.Design.cs).</summary>
    public sealed class Look
    {
        public float[] Pos = [], Normal = [];
        public uint[] Colour = [];
        public int Vertices => Pos.Length / 3;
        public Vector3 Min, Max;
        internal object? Gpu; // (the render thread's buffers: Renderer.Design.cs)
    }

    /// <summary>One design: its text, hash and (once its part is made) what it came to.</summary>
    public sealed class Entry
    {
        public string Hash = "", Text = "", Name = "";
    }

    private static readonly Dictionary<string, Entry> Known = new(StringComparer.Ordinal); // by hash
    private static readonly Dictionary<string, ConstructPartDef?> Interned = new(StringComparer.Ordinal); // by key
    private static readonly Dictionary<ConstructPartDef, (Entry E, Element El)> Info = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<string> Unknown = new(StringComparer.Ordinal);

    [ThreadStatic] public static string? LastRefusal;

    /// <summary>The last design made into its elements: how long it took and what it came to (a check reads these).</summary>
    public static double LastBuildMs, LastStepMs; // the last design made: in all, and its longest step (ElementSteps)
    public static string LastSummary = "";

    public static bool IsName(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool IsDesign(ConstructPartDef d) => d.IsNamedRuntime && IsName(d.Name);

    public static string KeyOf(string hash) => Prefix + hash + ")";

    public static string HashOf(string text) => BakedParts.HashOf(text);

    /// <summary>A design part's look (null: not one).</summary>
    public static Look? LookOf(ConstructPartDef d)
    {
        if (!IsDesign(d))
            return null;
        lock (Interned)
            return Info.TryGetValue(d, out var i) ? i.El.Look : null;
    }

    /// <summary>A design part's design (null: not one).</summary>
    public static Entry? EntryOf(ConstructPartDef d)
    {
        if (!IsDesign(d))
            return null;
        lock (Interned)
            return Info.TryGetValue(d, out var i) ? i.E : null;
    }

    /// <summary>
    /// A blueprint's <c>"shipDesigns"</c> (ConstructClip.ToBlueprint): the design of every design part in it, so a player's blueprint of a
    /// design ship builds in a world that never saw that design. Null when it has none.
    /// </summary>
    public static string? BlueprintBlock(Construct c)
    {
        List<Entry>? used = null;
        for (var i = 0; i < c.PartCount; i++)
            if (EntryOf(c.DefOf(i)) is { } e && !(used ??= []).Contains(e))
                used.Add(e);
        return used == null ? null : "[" + string.Join(", ", used.Select(e => e.Text)) + "]";
    }

    /// <summary>A blueprint's <c>"shipDesigns"</c> read: each one into the table (before its steps name their parts).</summary>
    public static void ReadBlueprintBlock(System.Text.Json.JsonElement v, string file)
    {
        if (v.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new ConstructPartException($"{file}: \"shipDesigns\" is a list of ship designs");
        foreach (var one in v.EnumerateArray())
            if (Canon(one.GetRawText()) is not { } text || Register(text) is null)
                throw new ConstructPartException($"{file}: a ship design in \"shipDesigns\" isn't read: {LastRefusal}");
    }

    public static Entry? Find(string hash)
    {
        lock (Interned)
            return Known.GetValueOrDefault(hash);
    }

    /// <summary>A design's JSON as the table keeps it (read as a design and written again, without indents). Null: it isn't one (<see cref="LastRefusal"/>).</summary>
    public static string? Canon(string json)
    {
        try
        {
            var d = ShipDesign.FromJson(json);
            return JsonNode.Parse(d.ToJson())!.ToJsonString();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException)
        {
            LastRefusal = "not a ship design: " + ex.Message;
            return null;
        }
    }

    /// <summary>Keeps a design's text in the shared table (it must read as a design). Its hash, or null.</summary>
    public static string? Register(string text)
    {
        if (text.Length > MaxText)
        {
            LastRefusal = "the design is too long";
            return null;
        }
        var hash = HashOf(text);
        lock (Interned)
            if (Known.ContainsKey(hash))
                return hash;
        ShipDesign d;
        try
        {
            d = ShipDesign.FromJson(text);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException or KeyNotFoundException)
        {
            LastRefusal = "not a ship design: " + ex.Message;
            return null;
        }
        lock (Interned)
        {
            Known.TryAdd(hash, new Entry { Hash = hash, Text = text, Name = d.Name });
            if (Unknown.Remove(KeyOf(hash)))
                Console.WriteLine($"[design] design {hash} came: its part can be made now");
        }
        return hash;
    }

    // ---- The elements: what a design is made of, one part each ------------------------------------------------------------

    /// <summary>
    /// One element of a design, the part it becomes: a band of a hull body's copy (between two rings), a join (each side of a mirrored one), a
    /// piece (each copy), a room's floor. <see cref="Id"/> names it within its design (the part's key is the design's and this); its geometry is
    /// in the part's frame, which stands at <see cref="Origin"/> in the ship's. A bill of materials reads <see cref="Kind"/>, <see cref="Name"/>,
    /// <see cref="Area"/> and <see cref="Kg"/>.
    /// <para>
    /// While it is made (a stage of <see cref="ElementSteps"/>) everything is in the ship's frame: points, boxes, directions, a door's or a joint's
    /// pivot and axis, a ladder's heights; <see cref="IntoFrames"/> then moves each into the part's own frame (by <see cref="Origin"/>, the middle of
    /// <see cref="BoxMin"/>..<see cref="BoxMax"/>, and <see cref="Rot"/>).
    /// </para>
    /// </summary>
    public sealed class Element
    {
        public string Id = "", Kind = "", Name = "";
        public Vector3 Origin;
        public float Area, Kg;
        public Vector3 BoxMin, BoxMax; // where its mass is (part frame); with Boxes, at least their bounds
        public ConvexHull[] Hulls = [];
        public Look Look = new();
        public readonly List<int> Links = []; // the elements it is welded to (by index)
        /// <summary>What its part does in the game: "" structure, "thrust" (a drive, an RCS nozzle), "seat", "flight" (the flight computer); each a <see cref="KindTable.Fn"/>.</summary>
        public string Fn = "";
        public string Role = ""; // a seat's (DesignSeats.Roles)
        public byte Rot; // the part's turn (Rot24): a seat faces its own way; everything else stands in the ship's frame
        public Vector3 Exhaust, ThrustAt, SeatAt; // thrust: the way its exhaust leaves and where it pushes; a seat: the sitter's feet (the ship's frame, then the part's)
        public float Force; // thrust: N at full
        public float Mouth; // thrust: its bell's or nozzle's mouth radius (m), which sizes its flame

        // ---- Hooks for walkable designs and ship systems (steps 4 and 5): each is applied by PartOf or BuildSteps when set ----
        /// <summary>Solid boxes (a room's floor, walls and ceiling; a door's leaf), in place of the one hidden mass box: they collide as boxes, hidden (its look draws it), each with its share of <see cref="Kg"/>. Null: none.</summary>
        public List<ElementBox>? Boxes;
        /// <summary>An event door's leaf (ConstructDoors): copied onto the def, which becomes PartRole.Door. Its Pivot and Axis as the ship has them while made.</summary>
        public CDoorDef? Door;
        /// <summary>A vertical ladder: its landing's and its foot's heights (the ship's Y while made; the part's frame after). NaN: not a ladder. Its part keeps its +y up.</summary>
        public float LadderTop = float.NaN, LadderFoot = float.NaN;
        /// <summary>A seat's turn about its up past its Rot24 (radians): the designer's free yaw, onto its def's ConstructPartDef.SeatTurn (every seat reader turns by it: SeatRot).</summary>
        public float SeatTurn;
        /// <summary>The segment its part hangs on (CPart.Seg): 0 the body; k the k-th joint the design makes (elements with <see cref="Joint"/>, in element order), which must come before it.</summary>
        public byte Seg;
        /// <summary>Its part is a joint (a hinge, turntable or piston): the parts with <see cref="Seg"/> naming it move on it (BuildSteps adds the joint). Null: not one.</summary>
        public JointSpec? Joint;
        /// <summary>The elements its part's output is wired into (by index; BuildSteps lays each wire, logging one refused).</summary>
        public readonly List<int> Wires = [];

        internal LookBuilder? Mesh; // (while it is made, in the ship's frame)
        internal List<Vector3>? Points; // (ditto: its hulls' points, in runs, before they are moved)
        internal List<int>? Runs;
        /// <summary>It has no collider (its colliders were all left out past <see cref="MaxHulls"/>): PartOf gives it a 1 cm mass box, never its bounds.</summary>
        public bool NoCollider;
    }

    /// <summary>One of an element's solid boxes (<see cref="Element.Boxes"/>): its corners (the ship's frame while made, the part's after) and its mass (0 for all: shared by volume).</summary>
    public record struct ElementBox(Vector3 Min, Vector3 Max, float Kg = 0);

    /// <summary>An element's joint (<see cref="Element.Joint"/>): its type and limits, its pivot and axis (the ship's frame while made, the part's after).</summary>
    public sealed class JointSpec
    {
        public JointType Type = JointType.Passive;
        public Vector3 Pivot;
        public byte Axis; // a direction (0..5: ConstructParts.DirVector)
        public float Min, Max; // radians (metres for a piston)
        public float Speed, Strength; // radians or metres a second; N m or N
        public bool Limited = true;
    }

    /// <summary>What a piece kind's work (<see cref="KindTable.Piece"/>) is handed: the piece, its copy's element and index, its copy's place, the list.</summary>
    private readonly record struct PieceAt(Piece P, Element E, int At, DesignScene.Place W, List<Element> Els)
    {
        public float S => (float)P.Size;
        public Vector3 O => V(W.O) - V(W.Y) * (float)P.Recess; // (its foot on the skin, past its recess)
        public Vector3 X => V(W.X);
        public Vector3 Y => V(W.Y); // (out of the skin)
        public Vector3 Z => V(W.Z);
    }

    private delegate void PieceWork(in PieceAt a);

    /// <summary>
    /// The kinds' tables, filled once by each kind file's <c>RegisterX</c> (listed in <see cref="Make"/>): what a piece kind does, and for each
    /// <see cref="Element.Fn"/> the def its part starts from and what is set on it after <see cref="PartOf"/> has put in the structure's numbers.
    /// </summary>
    private sealed class KindTable
    {
        public readonly Dictionary<string, PieceWork> Pieces = new(StringComparer.Ordinal); // by PieceKinds kind
        public readonly Dictionary<string, (Func<Element, ConstructPartDef> Start, Action<Element, ConstructPartDef>? Finish)> Fns = new(StringComparer.Ordinal); // by Element.Fn

        /// <summary>What a piece of <paramref name="kind"/> does: called for each copy's element once it is in the list (it may add elements of its own, welded to it).</summary>
        public void Piece(string kind, PieceWork work) => Pieces.Add(kind, work);

        /// <summary>
        /// An element function: <paramref name="start"/> gives its part's def (a template's copy: DesignParts.Templates.cs), <paramref name="finish"/>
        /// sets what it does once the boxes, hulls and bounds are in (its mass and health come after, from the element's).
        /// </summary>
        public void Fn(string fn, Func<Element, ConstructPartDef> start, Action<Element, ConstructPartDef>? finish = null) => Fns.Add(fn, (start, finish));

        public static KindTable Make()
        {
            var k = new KindTable();
            RegisterPieces(k);
            RegisterRooms(k);
            RegisterDoors(k);
            RegisterRisers(k);
            RegisterSystems(k);
            return k;
        }
    }

    private static readonly Lazy<KindTable> Kinds = new(KindTable.Make);

    /// <summary>What a piece's element does in the game, from its place (a mirrored copy's own): its kind's work, or nothing (structure).</summary>
    private static void Works(in PieceAt a)
    {
        if (Kinds.Value.Pieces.TryGetValue(a.P.Kind, out var work))
            work(a);
    }

    /// <summary>The working part an element starts from (its Fn's), or null for structure.</summary>
    private static ConstructPartDef? Working(Element e)
    {
        if (e.Fn.Length == 0)
            return null;
        if (Kinds.Value.Fns.TryGetValue(e.Fn, out var f))
            return f.Start(e);
        Console.WriteLine($"[design] an element's function \"{e.Fn}\" ({e.Id}, {e.Name}) is no kind's: it is made as structure");
        return null;
    }

    /// <summary>A design made into its elements once a run (the first of its parts asked for makes them all).</summary>
    private sealed class Made
    {
        public Entry E = null!;
        public List<Element> Elements = [];
        public Dictionary<string, int> ById = new(StringComparer.Ordinal);
        public string Summary = "";
    }

    private static readonly Dictionary<string, Made> Makes = new(StringComparer.Ordinal); // by hash

    /// <summary>
    /// The design's elements, the parts it is built of (the one place that maps a design to parts: a bill of materials reads it too). Every
    /// band, join, piece and floor, with its look, colliders and mass, and the welds between them: each band to the next, a join to the bands
    /// it meets, a piece to the band it stands on, a floor to its deck's band, and whatever that leaves apart to its nearest element, so the
    /// body is one piece.
    /// </summary>
    public static List<Element> ElementsOf(ShipDesign d)
    {
        var els = new List<Element>();
        foreach (var _ in ElementSteps(d, els))
        {
        }
        return els;
    }
    /// <summary>What the stages of <see cref="ElementSteps"/> share: the design, the list, and each hull body's copy's bands (<see cref="AddBands"/> fills them).</summary>
    private sealed class Steps(ShipDesign d, List<Element> els)
    {
        public readonly ShipDesign D = d;
        public readonly List<Element> Els = els;
        public readonly Dictionary<(int Hull, int Copy), (int First, int Count, double[] Z)> BandOf = new();
        /// <summary>The designer's checks of the design (its doorways, placed doors: DesignScene.Run), made once by the first stage that needs them.</summary>
        public DesignScene.Check? Check;

        /// <summary>The band of a body's copy at <paramref name="z"/> (its first copy's when it has no such copy), or -1.</summary>
        public int BandAt(int hull, int copy, double z)
        {
            if (!BandOf.TryGetValue((hull, copy), out var b) && !BandOf.TryGetValue((hull, 0), out b))
                return -1;
            var i = Array.BinarySearch(b.Z, z);
            i = i >= 0 ? i : ~i - 1;
            return b.First + Math.Clamp(i, 0, b.Count - 1);
        }
    }

    /// <summary>
    /// <see cref="ElementsOf"/> a step at a time into <paramref name="els"/>: a step a hull body's copy, a join, a piece or an element's
    /// colliders, so the held spawn makes a new design over several ticks (each a few milliseconds) instead of one long one. The stages run in
    /// this order, each its kind file's; the elements' order (which their welds and a joint's segment number go by) is theirs.
    /// </summary>
    public static IEnumerable<int> ElementSteps(ShipDesign d, List<Element> els)
    {
        var st = new Steps(d, els);
        foreach (var n in AddBands(st)) // DesignParts.Hull.cs
            yield return n;
        foreach (var n in AddJoins(st)) // DesignParts.Hull.cs
            yield return n;
        foreach (var n in AddPieces(st)) // DesignParts.Pieces.cs (and the flight computer)
            yield return n;
        foreach (var n in AddRooms(st)) // DesignParts.Rooms.cs
            yield return n;
        foreach (var n in AddDoors(st)) // DesignParts.Doors.cs
            yield return n;
        foreach (var n in AddRisers(st)) // DesignParts.Risers.cs
            yield return n;
        foreach (var n in AddSystems(st)) // DesignParts.Systems.cs
            yield return n;
        foreach (var n in ClipHulls(st)) // DesignParts.Hull.cs (joins and pieces kept out of the rooms; MaxHulls)
            yield return n;
        foreach (var n in IntoFrames(els))
            yield return n;
        Connect(els);
    }

    /// <summary>The bands' looks and hulls; then each element into its own frame (moved to its origin, then turned back by its Rot).</summary>
    private static IEnumerable<int> IntoFrames(List<Element> els)
    {
        foreach (var e in els)
        {
            if (e.Points != null)
            {
                for (var k = 0; k < e.Runs!.Count; k++)
                {
                    var from = k == 0 ? 0 : e.Runs[k - 1];
                    var pts = e.Points.GetRange(from, e.Runs[k] - from);
                    if (ConvexHull.Of(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pts)) is { } hull)
                        e.Hulls = [.. e.Hulls, hull];
                }
                e.Points = null;
            }
            if (e.Boxes is { Count: > 0 } boxes)
                foreach (var b in boxes)
                    (e.BoxMin, e.BoxMax) = (Vector3.Min(e.BoxMin, b.Min), Vector3.Max(e.BoxMax, b.Max));
            var middle = (e.BoxMin + e.BoxMax) * 0.5f;
            e.Origin = ConstructWorld.Quantise(middle);
            // Into the part's frame: moved to its origin, then turned back by its Rot (a seat facing its own way; 0 for the rest).
            var back = Quaternion.Conjugate(Rot24.Quat(e.Rot));
            var inv = Rot24.Inverse(e.Rot);
            var mid = Vector3.Transform(middle - e.Origin, back);
            var half = Rot24.ApplyHalf(inv, (e.BoxMax - e.BoxMin) * 0.5f);
            (e.BoxMin, e.BoxMax) = (mid - half, mid + half);
            e.Hulls = [.. e.Hulls.Select(hl => hl.MovedInto(null, back, -Vector3.Transform(e.Origin, back)))];
            e.Exhaust = Vector3.Transform(e.Exhaust, back);
            e.ThrustAt = Vector3.Transform(e.ThrustAt - e.Origin, back);
            e.SeatAt = Vector3.Transform(e.SeatAt - e.Origin, back);
            e.Look = e.Mesh?.Done(-e.Origin, back) ?? new Look();
            e.Mesh = null;
            if (e.Boxes != null)
                for (var b = 0; b < e.Boxes.Count; b++)
                {
                    var (bm, bh) = (Vector3.Transform((e.Boxes[b].Min + e.Boxes[b].Max) * 0.5f - e.Origin, back), Rot24.ApplyHalf(inv, (e.Boxes[b].Max - e.Boxes[b].Min) * 0.5f));
                    e.Boxes[b] = e.Boxes[b] with { Min = bm - bh, Max = bm + bh };
                }
            if (e.Door != null)
            {
                e.Door.Pivot = Vector3.Transform(e.Door.Pivot - e.Origin, back);
                e.Door.Axis = Rot24.ApplyDir(inv, e.Door.Axis);
            }
            if (e.Joint != null)
            {
                e.Joint.Pivot = Vector3.Transform(e.Joint.Pivot - e.Origin, back);
                e.Joint.Axis = Rot24.ApplyDir(inv, e.Joint.Axis);
            }
            if (!float.IsNaN(e.LadderTop))
                (e.LadderTop, e.LadderFoot) = (e.LadderTop - e.Origin.Y, float.IsNaN(e.LadderFoot) ? float.NaN : e.LadderFoot - e.Origin.Y);
            if (e.Hulls.Length > 0)
                yield return els.Count;
        }
    }

    private static void Weld(List<Element> els, int a, int b)
    {
        if (a == b || els[a].Links.Contains(b))
            return;
        els[a].Links.Add(b);
        els[b].Links.Add(a);
    }

    /// <summary>Whatever the welds leave apart (bodies that only overlap, a floor with no band under it) goes to its nearest element.</summary>
    private static void Connect(List<Element> els)
    {
        if (els.Count < 2)
            return;
        var comp = new int[els.Count];
        Array.Fill(comp, -1);
        void Flood(int s, int id)
        {
            var stack = new Stack<int>();
            stack.Push(s);
            comp[s] = id;
            while (stack.Count > 0)
                foreach (var n in els[stack.Pop()].Links)
                    if (comp[n] < 0)
                    {
                        comp[n] = id;
                        stack.Push(n);
                    }
        }
        Flood(0, 0);
        for (var i = 1; i < els.Count; i++)
        {
            if (comp[i] >= 0)
                continue;
            Flood(i, i);
            // The nearest element already in the first piece, by their middles.
            var mine = (els[i].BoxMin + els[i].BoxMax) * 0.5f + els[i].Origin;
            int best = 0;
            var bd = float.MaxValue;
            for (var k = 0; k < els.Count; k++)
                if (comp[k] == 0)
                {
                    var o = (els[k].BoxMin + els[k].BoxMax) * 0.5f + els[k].Origin;
                    if (Vector3.DistanceSquared(o, mine) < bd)
                        (bd, best) = (Vector3.DistanceSquared(o, mine), k);
                }
            Weld(els, best, i);
            for (var k = 0; k < els.Count; k++)
                if (comp[k] == i)
                    comp[k] = 0;
        }
    }

    private static DesignMesh Mirror(DesignMesh m, double cx)
    {
        var o = new DesignMesh { Leaves = m.Leaves }; // (its triangles in the same order: its door leaves too)
        for (var t = 0; t < m.Tris; t++)
            for (var v = 2; v >= 0; v--)
            {
                var i = (t * 3 + v) * 3;
                o.P.Add(2 * cx - m.P[i]); o.P.Add(m.P[i + 1]); o.P.Add(m.P[i + 2]);
                o.C.Add(m.C[i]); o.C.Add(m.C[i + 1]); o.C.Add(m.C[i + 2]);
            }
        return o;
    }

    private static DesignScene.Place Mirror(DesignScene.Place w, double cx)
    {
        static double[] F(double[] v) => [-v[0], v[1], v[2]];
        return new DesignScene.Place([2 * cx - w.O[0], w.O[1], w.O[2]], F(w.X), F(w.Y), F(w.Z));
    }

    private static Vector3 V(double[] v) => new((float)v[0], (float)v[1], (float)v[2]);

    /// <summary>The Rot24 turn nearest the one that takes a part's +z to <paramref name="zAxis"/> and its +y to <paramref name="up"/>.</summary>
    private static byte Facing(Vector3 zAxis, Vector3 up)
    {
        zAxis = Vector3.Normalize(zAxis);
        up = Vector3.Normalize(up - zAxis * Vector3.Dot(up, zAxis));
        var xAxis = Vector3.Cross(up, zAxis);
        var m = new Matrix4x4(xAxis.X, xAxis.Y, xAxis.Z, 0, up.X, up.Y, up.Z, 0, zAxis.X, zAxis.Y, zAxis.Z, 0, 0, 0, 0, 1);
        return Rot24.Nearest(Quaternion.CreateFromRotationMatrix(m), out _);
    }

    private static Made? MadeOf(string hash, out string why)
    {
        var r = new MakeRun();
        foreach (var _ in MakeSteps(hash, r))
        {
        }
        why = r.Why;
        return r.Made;
    }

    /// <summary>What <see cref="MakeSteps"/> came to: the made design, or why not.</summary>
    private sealed class MakeRun
    {
        public Made? Made;
        public string Why = "";
        public double LongestMs;
    }

    private static bool Bad(Exception ex) => ex is FormatException or InvalidOperationException or ArgumentException or IndexOutOfRangeException or NullReferenceException or KeyNotFoundException;

    /// <summary>A design's elements made a step at a time (<see cref="ElementSteps"/>), once a process per design; <paramref name="run"/> holds the result.</summary>
    private static IEnumerable<int> MakeSteps(string hash, MakeRun run)
    {
        Entry? e;
        lock (Interned)
        {
            if (Makes.TryGetValue(hash, out var have))
            {
                run.Made = have;
                yield break;
            }
            if (!Known.TryGetValue(hash, out e))
            {
                run.Why = "its design hasn't come to this peer (a design part is made from its world's design store)";
                yield break;
            }
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var els = new List<Element>();
        ShipDesign? d = null;
        try
        {
            d = ShipDesign.FromJson(e.Text);
        }
        catch (Exception ex) when (Bad(ex))
        {
            run.Why = "its design couldn't be read: " + ex.Message;
        }
        if (d == null)
        {
            Console.WriteLine($"[design] REFUSED design {hash} (\"{e.Name}\"): {run.Why}");
            yield break;
        }
        using (var steps = ElementSteps(d, els).GetEnumerator())
        {
            var step = System.Diagnostics.Stopwatch.GetTimestamp();
            while (true)
            {
                bool more;
                try
                {
                    more = steps.MoveNext();
                }
                catch (Exception ex) when (Bad(ex))
                {
                    run.Why = "its design couldn't be made: " + ex.Message;
                    Console.WriteLine($"[design] REFUSED design {hash} (\"{e.Name}\"): {run.Why}");
                    yield break;
                }
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                run.LongestMs = Math.Max(run.LongestMs, (now - step) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                if (!more)
                    break;
                yield return els.Count;
                step = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }
        var made = new Made { E = e, Elements = els };
        for (var k = 0; k < els.Count; k++)
            made.ById[els[k].Id] = k;
        LastBuildMs = clock.Elapsed.TotalMilliseconds;
        LastStepMs = run.LongestMs;
        var tris = els.Sum(x => x.Look.Vertices) / 3;
        var kg = els.Sum(x => x.Kg);
        made.Summary = $"{els.Count} parts ({els.Count(x => x.Kind == "band")} bands, {els.Count(x => x.Kind == "join")} joins, {els.Count(x => x.Kind == "piece")} pieces, {els.Count(x => x.Kind == "floor")} floors), {tris} triangles, {els.Sum(x => x.Hulls.Length)} hulls, {kg / 1000:0.0} t";
        LastSummary = made.Summary;
        Console.WriteLine($"[design] made \"{e.Name}\" ({hash}) in {LastBuildMs:0} ms (its longest step {run.LongestMs:0.0} ms): {made.Summary}");
        lock (Interned)
        {
            if (Makes.TryGetValue(hash, out var raced))
                made = raced;
            else
                Makes[hash] = made;
        }
        run.Made = made;
    }

    /// <summary>A design's elements (made the first time it is asked for), or null when it isn't known here or can't be made.</summary>
    public static IReadOnlyList<Element>? Elements(string hash) => MadeOf(hash, out _)?.Elements;

    // ---- The parts ---------------------------------------------------------------------------------------------------------

    public static string KeyOf(string hash, string element) => KeyOf(hash) + " " + element;

    private static bool Split(string name, out string hash, out string element)
    {
        (hash, element) = ("", "");
        if (!IsName(name) || name.Length < Prefix.Length + 19 || name[Prefix.Length + 16] != ')' || name[Prefix.Length + 17] != ' ')
            return false;
        hash = name.Substring(Prefix.Length, 16);
        element = name[(Prefix.Length + 18)..];
        return element.Length > 0;
    }


    /// <summary>The design part a key names, made the first time it is asked for. Null: not a design key, a design not heard of here, or one that can't be made.</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (!Split(name, out var hash, out var id))
            return null;
        lock (Interned)
            if (Interned.TryGetValue(name, out var have))
            {
                if (have != null)
                    have.Touched = Environment.TickCount64;
                return have;
            }
        if (MadeOf(hash, out var why) is not { } made)
        {
            lock (Interned)
                if (Unknown.Add(name) && Unknown.Count < 64)
                    Console.WriteLine($"[design] REFUSED \"{name}\": {why}");
            return null; // (not interned: the design may still come)
        }
        ConstructPartDef? def = null;
        if (!made.ById.TryGetValue(id, out var k))
            LastRefusal = OldRoom(id)
                ? $"\"{id}\" is a room's floor slab from before walkable rooms (protocol 475), whose shells are elements \"room<n>\" now: an old body keeps its other parts and loses that floor (spawn the design again for its rooms)"
                : $"no element \"{id}\" in its design";
        else
            def = PartOf(made, made.Elements[k], name);
        lock (Interned)
        {
            if (Interned.TryGetValue(name, out var raced))
                return raced;
            if (def != null && !Extrusion.Adopt(def))
            {
                def = null;
                LastRefusal = "the run-time part table is full";
            }
            if (def == null)
                Console.WriteLine($"[design] REFUSED \"{name}\": {LastRefusal}");
            else
                Info[def] = (made.E, made.Elements[k]);
            Interned[name] = def;
            return def;
        }
    }


    /// <summary>
    /// An element id of a room as saves before walkable rooms (protocol 475) hold it: "r{n}", a 0.1 m floor slab under the room. The shell that
    /// replaced it is "room{n}" (its own origin, with its ".k" and ".hk" elements), so an old body's slab is refused by name (logged, and the save
    /// reader skips the part) rather than loaded as the new shell at the slab's origin.
    /// </summary>
    private static bool OldRoom(string id) => id.Length > 1 && id[0] == 'r' && id.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;

    private static ConstructPartDef PartOf(Made made, Element e, string name)
    {
        var t = Template;
        var def = Working(e) ?? t.ShallowCopy();
        def.Name = name;
        def.File = "";
        def.Description = $"{e.Name} of \"{made.E.Name}\", a ship from the Ship Designer.";
        if (e.Boxes is { Count: > 0 } boxes)
        {
            // Its own solid boxes (hidden: its look draws it), each colliding as a box, with its share of the mass.
            def.Boxes = [.. boxes.Select(b => new CPartBox((b.Min + b.Max) * 0.5f, Vector3.Max((b.Max - b.Min) * 0.5f, new Vector3(0.005f)), new Vector3(0.6f), Hidden: true, Paintable: false))];
            def.SolidBoxes = def.Boxes.Length;
            var kg = boxes.Sum(b => b.Kg);
            def.BoxMass = kg > 0 ? [.. boxes.Select(b => b.Kg / kg)] : null;
            (def.BoundsMin, def.BoundsMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            foreach (var b in boxes)
                (def.BoundsMin, def.BoundsMax) = (Vector3.Min(def.BoundsMin, b.Min), Vector3.Max(def.BoundsMax, b.Max));
        }
        else if (e.NoCollider || (e.Kind == "band" && e.Hulls.Length == 0))
        {
            // No collider (a band all of whose skin is open or cut away by another body, or an element whose colliders went past MaxHulls): a
            // 1 cm mass box at the bottom of its bounds' middle (on the keel's line, under any room), never a box as big as its cross-section.
            var half = new Vector3(0.005f);
            var centre = new Vector3((e.BoxMin.X + e.BoxMax.X) * 0.5f, e.BoxMin.Y + half.Y, (e.BoxMin.Z + e.BoxMax.Z) * 0.5f);
            def.Boxes = [new CPartBox(centre, half, new Vector3(0.6f), Hidden: true, Paintable: false)];
            def.SolidBoxes = 1;
            def.BoxMass = null;
            (def.BoundsMin, def.BoundsMax) = (centre - half, centre + half);
        }
        else
        {
            var half = Vector3.Max((e.BoxMax - e.BoxMin) * 0.5f, new Vector3(0.02f));
            var centre = (e.BoxMax + e.BoxMin) * 0.5f;
            def.Boxes = [new CPartBox(centre, half, new Vector3(0.6f), Hidden: true, Paintable: false)];
            def.SolidBoxes = 1;
            def.BoxMass = null;
            (def.BoundsMin, def.BoundsMax) = (centre - half, centre + half);
        }
        def.HasGlass = false;
        foreach (var hl in e.Hulls)
        {
            hl.Bounds(out var mn, out var mx);
            (def.BoundsMin, def.BoundsMax) = (Vector3.Min(def.BoundsMin, mn), Vector3.Max(def.BoundsMax, mx));
        }
        def.Hulls = e.Hulls;
        def.HullCovers = e.Hulls.Length > 0 ? [true] : []; // (its hulls stand for it; a floor, an RCS nozzle or the flight computer, with none, collides as its box: a nozzle's and the computer's are inside their piece's hull)
        // A player meets its hulls as skin (ConstructRideMove.AddSkin, ConstructWorld.SkinBlock), never its bounding box.
        def.HasSkin = def.SkinOnly = e.Hulls.Length > 0;
        def.HullSkin = def.HasSkin ? Enumerable.Repeat(true, e.Hulls.Length).ToArray() : null;
        (def.SkinMin, def.SkinMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var hl in e.Hulls)
        {
            hl.Bounds(out var mn, out var mx);
            (def.SkinMin, def.SkinMax) = (Vector3.Min(def.SkinMin, mn), Vector3.Max(def.SkinMax, mx));
        }
        if (!def.HasSkin)
            (def.SkinMin, def.SkinMax) = (Vector3.Zero, Vector3.Zero);
        def.ColliderBoxes = false;
        def.Sockets = [t.Sockets[0] with { Name = "s0", At = Vector3.Zero }]; // (one socket the welds between elements hang on)
        def.Lamps = [];
        if (e.Door != null)
        {
            def.Door = e.Door; // (an event door's leaf: DesignParts.Doors.cs)
            def.Role = PartRole.Door;
        }
        if (!float.IsNaN(e.LadderTop))
        {
            def.LadderTop = e.LadderTop;
            def.LadderFoot = float.IsNaN(e.LadderFoot) ? def.BoundsMin.Y : e.LadderFoot;
        }
        if (e.Joint is { } j)
        {
            def.Role = j.Type == JointType.Passive ? PartRole.Hinge : PartRole.Joint;
            (def.JointType, def.JointPivot, def.JointAxis, def.JointMin, def.JointMax) = (j.Type, j.Pivot, j.Axis, j.Min, j.Max);
            (def.MotorSpeed, def.MotorStrength, def.JointLimited) = (j.Speed, j.Strength, j.Limited);
        }
        if (e.Fn.Length > 0 && Kinds.Value.Fns.TryGetValue(e.Fn, out var fn))
            fn.Finish?.Invoke(e, def);
        def.Mass = Math.Clamp(e.Kg, 0.5f, ConstructParts.MaxPartMass);
        def.Health = Math.Clamp(e.Kg * HpPerKg, 1f, ConstructParts.MaxPartHealth);
        def.SurfaceOverride = 0;
        def.MatBase = null;
        def.IsNamedRuntime = true;
        def.ExHash = BakedParts.HashOf(name) is var hx ? uint.Parse(hx[8..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
        def.PhysicsOnly = true;
        def.PhysicsWhy = "a ship's";
        return def;
    }

    /// <summary>(RuntimeParts) Drops the doomed parts from the key table; their designs stay known.</summary>
    internal static void Forget(Func<ConstructPartDef, bool> doomed)
    {
        lock (Interned)
        {
            if (Interned.Count == 0)
                return;
            RuntimeParts.DropOrNull(Interned, d => d == null || doomed(d));
            List<ConstructPartDef>? gone = null;
            foreach (var d in Info.Keys)
                if (doomed(d))
                    (gone ??= new()).Add(d);
            if (gone != null)
                foreach (var d in gone)
                    Info.Remove(d);
        }
    }

    /// <summary>A key off the wire, as this machine's part id: a plain part when it can't be made (never a dropped packet).</summary>
    internal static ushort ReadWire(string name) => Parse(name) is { } d ? d.Id : ConstructParts.Need("Frame Beam").Id;

    /// <summary>
    /// The seat F on a design body's hull sits a player in (ConstructEdits.Use; its rooms aren't walkable yet): its helm when free, else its
    /// first free working seat; -1 when <paramref name="part"/> isn't a design part or the body has no free seat. Not per tick.
    /// </summary>
    public static int BoardSeat(Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount || !IsDesign(c.DefOf(part)))
            return -1;
        var best = -1;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = c.DefOf(i);
            if (d.Role != PartRole.Seat || !IsDesign(d) || p.Broken || p.SitterPlus1 != 0 || i == c.DriveSeat && c.Driver != 255)
                continue;
            if (d.Helm)
                return i;
            if (best < 0)
                best = i;
        }
        return best;
    }

    /// <summary>
    /// Whether F on a design part works the part itself rather than sitting the player at the ship's helm (<see cref="BoardSeat"/>,
    /// ConstructEdits.Use): a door, a ladder, a hitch, a dock or a lamp is used where it is.
    /// </summary>
    public static bool KeepsUse(ConstructPartDef d) => d.Door != null || d.LadderTop > 0 || d.Role == PartRole.Hitch || d.DockSide > 0 || d.Lamps.Length > 0 || d.LightRange > 0;

    /// <summary>
    /// A design's body built in <paramref name="c"/> (ConstructBuild.BuildSteps, for a blueprint of <see cref="BlueprintOf"/>): its elements'
    /// parts where the design has them (each on its segment, a joint added on each joint element's part), then the welds, then the wires;
    /// one step an element (the held spawn builds it a slice a tick).
    /// </summary>
    public static IEnumerable<int> BuildSteps(ConstructWorld world, Construct c, string hash, ConstructBuild.BuildRun run)
    {
        var mr = new MakeRun();
        foreach (var k in MakeSteps(hash, mr))
            yield return -1; // (the elements made a step a tick-slice: the first spawn of a design)
        if (mr.Made is not { } made)
        {
            run.Why = mr.Why;
            yield break;
        }
        var partOf = new int[made.Elements.Count];
        var joints = 0;
        var wires = false;
        for (var k = 0; k < made.Elements.Count; k++)
        {
            var e = made.Elements[k];
            if (Parse(KeyOf(hash, e.Id)) is not { } def)
            {
                run.Why = $"its {e.Name} couldn't be made ({LastRefusal})";
                yield break;
            }
            if (e.Seg > joints)
            {
                run.Why = $"its {e.Name} rides on joint {e.Seg}, which isn't made before it";
                yield break;
            }
            partOf[k] = world.AddPart(c, def, e.Origin, e.Rot, e.Seg);
            if (partOf[k] < 0)
            {
                run.Why = "the world is full";
                yield break;
            }
            if (e.Joint != null)
            {
                if (world.AddJoint(c, partOf[k]) != joints + 1)
                {
                    run.Why = $"its {e.Name}'s joint couldn't be added (too many joints)";
                    yield break;
                }
                joints++;
            }
            wires |= e.Wires.Count > 0;
            yield return k;
        }
        yield return made.Elements.Count; // (the welds and the body's rebuild a step of their own)
        for (var k = 0; k < made.Elements.Count; k++)
            foreach (var n in made.Elements[k].Links)
                if (n > k && !world.AddLink(c, partOf[k], 0, partOf[n], 0))
                {
                    run.Why = "too many welds";
                    yield break;
                }
        c.Rebuild();
        if (!wires)
            yield break;
        yield return made.Elements.Count;
        for (var k = 0; k < made.Elements.Count; k++)
            foreach (var n in made.Elements[k].Wires)
            {
                if (ConstructBuild.CanWire(world, c, partOf[k], partOf[n]) is { } no)
                    Console.WriteLine($"[design] a wire from {made.Elements[k].Id} to {made.Elements[n].Id} of \"{made.E.Name}\" is refused: {no}");
                else if (!world.AddWire(c, partOf[k], partOf[n], rebuild: false))
                {
                    run.Why = "too many wires";
                    yield break;
                }
            }
        c.Rebuild();
    }

    // ---- The look ----------------------------------------------------------------------------------------------------------

    /// <summary>The designer's materials as a tint over the vertex colours (UiShipDesignerScene's SdMats): metals greyed, carbon dark.</summary>
    private static Vector3 Tint(string material) => material switch
    {
        "Steel" => Rgb(0xc8ccd0),
        "Brushed aluminium" => Rgb(0xe4e7ea),
        "Titanium" => Rgb(0xb6b2a8),
        "Copper" => Rgb(0xf0a070),
        "Carbon" => Rgb(0x3a3d42),
        _ => Vector3.One,
    };

    private static Vector3 Rgb(uint hex) => new Vector3((hex >> 16) & 255, (hex >> 8) & 255, hex & 255) / 255f;

    /// <summary>Gathers triangles into one mesh: flat normals, each turned out from a middle (a hull's axis), colours tinted and made linear.</summary>
    internal sealed class LookBuilder
    {
        private readonly List<float> _p = new(1024), _n = new(1024);
        private readonly List<uint> _c = new(256);
        public float Area;
        public List<float> Pos => _p; // (its corners so far, x y z a corner: what a collider is cut from)

        /// <param name="middle">What a normal faces away from: a point, or (NaN z) a line along z through x, y; all NaN: the triangle's own side (as drawn).</param>
        public void AddTri(DesignMesh m, int t, Vector3 tint, Vector3 middle)
        {
            var i = t * 9;
            var a = new Vector3((float)m.P[i], (float)m.P[i + 1], (float)m.P[i + 2]);
            var b = new Vector3((float)m.P[i + 3], (float)m.P[i + 4], (float)m.P[i + 5]);
            var c = new Vector3((float)m.P[i + 6], (float)m.P[i + 7], (float)m.P[i + 8]);
            var n = Vector3.Cross(b - a, c - a);
            var len = n.Length();
            if (len < 1e-7f)
                return;
            Area += 0.5f * len;
            n /= len;
            if (!float.IsNaN(middle.X))
            {
                var mid = (a + b + c) / 3;
                var from = float.IsNaN(middle.Z) ? new Vector3(middle.X, middle.Y, mid.Z) : middle;
                if (Vector3.Dot(n, mid - from) < 0)
                    n = -n;
            }
            for (var k = 0; k < 3; k++)
            {
                var v = k == 0 ? a : k == 1 ? b : c;
                _p.Add(v.X); _p.Add(v.Y); _p.Add(v.Z);
                _n.Add(n.X); _n.Add(n.Y); _n.Add(n.Z);
                var ci = (t * 3 + k) * 3;
                var col = ci + 2 < m.C.Count ? new Vector3(m.C[ci], m.C[ci + 1], m.C[ci + 2]) / 255f * tint : tint;
                _c.Add(Pack(col));
            }
        }

        private static uint Pack(Vector3 srgb)
        {
            // As the designer's sRGB colour: the game's box shader lights colours as they are written (a part file's, a paint's) and
            // writes no gamma at the end, so a linear colour came out far too dark (Karl, 2026-11-26: grey plates near black).
            static uint L(float v) => (uint)MathF.Round(Math.Clamp(v, 0, 1) * 255f);
            return L(srgb.X) | L(srgb.Y) << 8 | L(srgb.Z) << 16 | 0xff000000u;
        }

        public Look Done(Vector3 shift, Quaternion turn)
        {
            var l = new Look { Pos = [.. _p], Normal = [.. _n], Colour = [.. _c], Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue) };
            var turned = turn != Quaternion.Identity;
            for (var i = 0; i + 2 < l.Pos.Length; i += 3)
            {
                var v = new Vector3(l.Pos[i] + shift.X, l.Pos[i + 1] + shift.Y, l.Pos[i + 2] + shift.Z);
                if (turned)
                {
                    v = Vector3.Transform(v, turn);
                    var nv = Vector3.Transform(new Vector3(l.Normal[i], l.Normal[i + 1], l.Normal[i + 2]), turn);
                    (l.Normal[i], l.Normal[i + 1], l.Normal[i + 2]) = (nv.X, nv.Y, nv.Z);
                }
                (l.Pos[i], l.Pos[i + 1], l.Pos[i + 2]) = (v.X, v.Y, v.Z);
                (l.Min, l.Max) = (Vector3.Min(l.Min, v), Vector3.Max(l.Max, v));
            }
            return l;
        }
    }

    // ---- The world's store: the wire and the save ---------------------------------------------------------------------------

    /// <summary>One design: u32 bytes, the text (CBodyKind.ShipDesign).</summary>
    public static void Write(PacketWriter w, Entry e)
    {
        var bytes = Encoding.UTF8.GetBytes(e.Text);
        w.Begin(Msg.CBody).U8((byte)CBodyKind.ShipDesign).U32((uint)bytes.Length).Bytes(bytes);
    }

    /// <summary>Client: a design off the wire, checked here, into the shared table and the world's list.</summary>
    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        var n = r.U32();
        if (n > MaxText)
            throw new MalformedPacketException($"a ship design of {n} bytes");
        var text = Encoding.UTF8.GetString(r.Take((int)n));
        if (Register(text) is { } hash)
            world.KeepShipDesign(hash);
        else
            Console.WriteLine($"[design] a design from the host was refused: {LastRefusal}");
    }

    public static void WriteSave(SaveWriter w, IReadOnlyList<string> texts)
    {
        w.I32(texts.Count);
        foreach (var t in texts)
            w.Str(t);
    }

    public static void ReadSave(SaveReader r, ConstructWorld world)
    {
        var n = r.Count(16);
        for (var k = 0; k < n; k++)
        {
            var text = r.Str();
            if (Register(text) is { } hash)
                world.KeepShipDesign(hash);
            else
                Console.WriteLine($"[design] REFUSED a saved design: {LastRefusal}");
        }
    }

    /// <summary>Keeps in <paramref name="world"/> every design its bodies hold (before a save and before a joiner's designs go). Not per tick.</summary>
    public static void NoteInUse(ConstructWorld world)
    {
        foreach (var c in world.Bodies)
            for (var i = 0; i < c.PartCount; i++)
                NoteInUse(world, c.DefOf(i));
    }

    public static void NoteInUse(ConstructWorld world, ConstructPartDef d)
    {
        if (EntryOf(d) is { } e)
            world.KeepShipDesign(e.Hash);
    }

    /// <summary>The world's designs as text, for its save.</summary>
    public static string[] TextsOf(ConstructWorld world)
    {
        var list = new List<string>(world.ShipDesigns.Count);
        lock (Interned)
            foreach (var h in world.ShipDesigns)
                if (Known.TryGetValue(h, out var e))
                    list.Add(e.Text);
        return [.. list];
    }

    /// <summary>A blueprint that builds the design's body (ConstructBuild.BuildSteps: <see cref="BuildSteps"/>), for the held-spawn path that sets blueprints down.</summary>
    public static ConstructBuild.Blueprint BlueprintOf(string hash, string name) => new() { Name = name, File = "a ship design", DesignHash = hash };
}

public sealed partial class ConstructWorld
{
    /// <summary>The ship designs this world's bodies use, by hash (DesignParts.cs): saved with it, sent to every joiner.</summary>
    public readonly List<string> ShipDesigns = new(8);
    private readonly HashSet<string> _shipDesignSet = new(StringComparer.Ordinal);

    /// <summary>Server: designs new this tick, to everyone at the next flush (before the bodies that name them).</summary>
    public readonly List<string> ShipDesignsNew = new(4);

    public void KeepShipDesign(string hash)
    {
        if (_shipDesignSet.Count >= DesignParts.MaxWorld || !_shipDesignSet.Add(hash))
            return;
        ShipDesigns.Add(hash);
        ShipDesignsNew.Add(hash);
    }

    internal void ClearShipDesigns()
    {
        ShipDesigns.Clear();
        _shipDesignSet.Clear();
        ShipDesignsNew.Clear();
    }
}

public sealed partial class Construct
{
    /// <summary>Where part <paramref name="i"/>'s mass sits (its rest bounds' middle, or a design part's boxes by their shares: DesignParts.cs).</summary>
    internal Vector3 MassMiddle(int i, Vector3 mn, Vector3 mx)
    {
        var d = Parts[i].D;
        if (d.BoxMass is not { } share || share.Length < d.SolidBoxes)
            return (mn + mx) * 0.5f;
        var c = Vector3.Zero;
        for (var b = 0; b < d.SolidBoxes; b++)
        {
            BoxOf(i, b, out var centre, out _);
            c += centre * share[b];
        }
        return c;
    }
}
