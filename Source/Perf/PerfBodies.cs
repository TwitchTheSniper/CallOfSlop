using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The F9 window's costliest constructs (Sean, 2026-11-24: "have the construct section go into more detail"): while
/// <see cref="Perf.On"/>, each body's own physics step (its rebuild, forces, drive, wheels, feet, world contacts:
/// <c>ConstructPhysics.Step</c>'s per-body loop; the contacts between bodies and the solver are shared and not billed to
/// a body) and its draw (<c>ConstructRender.Draw</c>'s loop: cull, flames, merged chunks queued or parts box by box) are
/// added up on the body (<see cref="Construct.PerfPhysTicks"/>, <see cref="Construct.PerfDrawTicks"/>). Every
/// <see cref="Every"/> seconds the window takes the <see cref="Shown"/> costliest of each into fixed rows and clears the
/// bodies' sums. Nothing allocates: the rows hold the body's first part's name (a string the def already has).
/// </summary>
public static class PerfBodies
{
    public const int Shown = 10;
    public const double Every = 0.5;

    /// <summary>A body under this (ms a tick or a frame) gets no row: a sleeping block's rounding.</summary>
    public const float MinMs = 0.0005f;

    public struct Row
    {
        public int Id;
        public string Name;
        public int Parts;
        public float PhysMs; // its own physics per server tick (ms)
        public float DrawMs; // its draw per frame (ms)
        public float Boxes; // boxes it drew per frame
        public bool Awake, Framed, Far, Anchored;
    }

    public static readonly Row[] ByPhys = new Row[Shown], ByDraw = new Row[Shown];
    public static int PhysRows, DrawRows;

    /// <summary>Construct physics ticks run since the last look (ConstructWorld.Step adds one while timing is on).</summary>
    public static int Ticks;

    /// <summary>The window's frames since the last look, and the seconds the rows cover.</summary>
    public static int Frames;
    public static float Seconds;

    /// <summary>All the bodies' own physics and draw (ms per tick and per frame) in the last look, for the shares.</summary>
    public static float PhysTotalMs, DrawTotalMs;
    public static int Bodies, Awake;

    private static double _from = -1;

    /// <summary>Adds the time since <paramref name="t0"/> to the body's own physics.</summary>
    public static void Phys(Construct c, long t0) => c.PerfPhysTicks += Stopwatch.GetTimestamp() - t0;

    /// <summary>Adds the time since <paramref name="t0"/> to the body's draw, and the boxes it drew.</summary>
    public static void Draw(Construct c, long t0, int boxes)
    {
        c.PerfDrawTicks += Stopwatch.GetTimestamp() - t0;
        c.PerfDrawBoxes += boxes;
    }

    /// <summary>Once per frame while the window is up: every <see cref="Every"/> s the rows are taken again.</summary>
    public static void Sample(ConstructWorld? world, double now)
    {
        Frames++;
        if (world == null)
        {
            PhysRows = DrawRows = 0;
            return;
        }
        if (_from < 0 || now - _from > Every * 6 || now < _from)
        {
            Restart(world, now); // (first look, or the window was shut: the sums are stale)
            return;
        }
        if (now - _from < Every)
            return;
        var ticks = Math.Max(Ticks, 1);
        var frames = Math.Max(Frames, 1);
        PhysRows = DrawRows = 0;
        PhysTotalMs = DrawTotalMs = 0;
        Bodies = Awake = 0;
        foreach (var c in world.Bodies)
        {
            Bodies++;
            if (!c.Asleep)
                Awake++;
            if (c.PerfPhysTicks == 0 && c.PerfDrawTicks == 0)
                continue;
            var row = new Row
            {
                Id = c.Id, Name = c.PartCount > 0 ? c.DefOf(0).Name : "(empty)", Parts = c.PartCount,
                PhysMs = (float)(c.PerfPhysTicks * Perf.MsPerTick / ticks), DrawMs = (float)(c.PerfDrawTicks * Perf.MsPerTick / frames),
                Boxes = c.PerfDrawBoxes / (float)frames, Awake = !c.Asleep, Framed = c.Framed, Far = c.FarRate > 1, Anchored = c.Anchored,
            };
            PhysTotalMs += row.PhysMs;
            DrawTotalMs += row.DrawMs;
            if (row.PhysMs >= MinMs)
                Insert(ByPhys, ref PhysRows, in row, row.PhysMs, static r => r.PhysMs);
            if (row.DrawMs >= MinMs)
                Insert(ByDraw, ref DrawRows, in row, row.DrawMs, static r => r.DrawMs);
            c.PerfPhysTicks = c.PerfDrawTicks = 0;
            c.PerfDrawBoxes = 0;
        }
        Seconds = (float)(now - _from);
        _from = now;
        Ticks = Frames = 0;
    }

    private static void Restart(ConstructWorld world, double now)
    {
        foreach (var c in world.Bodies)
        {
            c.PerfPhysTicks = c.PerfDrawTicks = 0;
            c.PerfDrawBoxes = 0;
        }
        _from = now;
        Ticks = Frames = 0;
    }

    private static void Insert(Row[] rows, ref int count, in Row row, float key, Func<Row, float> of)
    {
        var at = count;
        while (at > 0 && of(rows[at - 1]) < key)
            at--;
        if (at >= Shown)
            return;
        for (var k = Math.Min(count, Shown - 1); k > at; k--)
            rows[k] = rows[k - 1];
        rows[at] = row;
        if (count < Shown)
            count++;
    }

    /// <summary>The rows as text for the window's Copy snapshot and Write to log (allocates: those buttons only).</summary>
    public static void Append(StringBuilder sb)
    {
        var inv = CultureInfo.InvariantCulture;
        sb.Append(inv, $"constructs: {Bodies} bodies, {Awake} awake; their own physics {PhysTotalMs:0.000} ms a tick, their draw {DrawTotalMs:0.000} ms a frame (over {Seconds:0.0} s)\n");
        for (var k = 0; k < PhysRows; k++)
            sb.Append(inv, $"  physics #{ByPhys[k].Id} {ByPhys[k].Name} ({ByPhys[k].Parts} parts{Flags(in ByPhys[k])}): {ByPhys[k].PhysMs:0.000} ms a tick\n");
        for (var k = 0; k < DrawRows; k++)
            sb.Append(inv, $"  draw #{ByDraw[k].Id} {ByDraw[k].Name} ({ByDraw[k].Parts} parts{Flags(in ByDraw[k])}): {ByDraw[k].DrawMs:0.000} ms a frame, {ByDraw[k].Boxes:0} boxes\n");
    }

    /// <summary>The row's state words (", awake, in a frame, far"); one of a few fixed strings, so nothing allocates.</summary>
    public static string Flags(in Row r) => FlagText[(r.Anchored ? 1 : 0) | (r.Awake ? 2 : 0) | (r.Framed ? 4 : 0) | (r.Far ? 8 : 0)];

    private static readonly string[] FlagText = MakeFlagText();

    private static string[] MakeFlagText()
    {
        var t = new string[16];
        for (var m = 0; m < 16; m++)
            t[m] = ((m & 1) != 0 ? ", anchored" : "") + ((m & 2) != 0 ? ", awake" : ", asleep") + ((m & 4) != 0 ? ", in a frame" : "") + ((m & 8) != 0 ? ", far" : "");
        return t;
    }
}
