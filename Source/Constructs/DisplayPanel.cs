using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a panel's frame and title say at a glance (<see cref="PanelCard.State"/>): the colour, so a row of machines reads from across a room.</summary>
public enum PanelState : byte
{
    Off, // nothing to show
    Idle, // waiting, nothing wrong
    Working,
    Ok, // has what it wants
    Wants, // needs something
    Refused, // something was turned away
    Fault, // can't work: no power, wrecked
}

/// <summary>What a card draws besides its text and bar (<see cref="PanelCard.Draw"/>): a part's panel picks one and the card's numbers set it.</summary>
public enum PanelDraw : byte
{
    None,
    BarV, // a column up the right side (Value)
    Dial, // a needle over a 270 degree scale (Value)
    Graph, // a line over the last while (Trace)
    Lamps, // a lamp beside each line of text (Lamps, LampBits)
    Compass, // a tape of bearings past a mark (Value: of a whole turn)
    BarH, // a bar across the whole panel (Value)
    Flight, // a craft from above and from the side, an arrow per effector (Flight and its bounds)
}

/// <summary>How a graph or a bar picks its colour along the value (<see cref="PanelCard.Ramp"/>; a display's "ramp" setting, in this order).</summary>
public enum PanelRamp : byte
{
    Solid, // the display's colour
    Level, // green, yellow, red as it rises
    Gradient, // dim to full, in the display's colour
    Heat, // dark red, red, yellow, white
}

/// <summary>
/// One flat display surface on a part (docs/sandbox/CONSTRUCTS.md, "Display panels"), declared in the part file's
/// <c>"panels"</c> list: where it is, how big, which way it faces, and where its card comes from. <see cref="Source"/> names a
/// <see cref="PanelSources"/> entry and <see cref="Slot"/> tells a source with several surfaces which this is (a press's bay).
/// </summary>
public sealed class PanelDef
{
    public string Source = "";
    public int Slot;
    public Vector3 At; // its centre, part frame; stands a few mm off the surface behind it
    public Vector2 Size; // metres
    public byte Face, Up; // ConstructParts.DirVector indices: the way it faces, and the top of its text
    public int Rows = 4; // text rows it is laid out for
    public float Range; // not drawn further off than this (m)
    public PanelFill? Fill;
    public Vector4 Picture = new(0, 0, 1, 1); // where a page's picture sits: shares of the panel, from its bottom left
    public PanelTouchDef[] Touch = []; // its buttons (PanelTouch.cs)
}

/// <summary>Fills a panel's card from the part as it is on this peer. Runs at <see cref="PanelRender.FillHz"/> for panels in range and in view; must not allocate in a steady state.</summary>
public delegate void PanelFill(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card);

/// <summary>
/// What a panel shows: a few short text lines, a state, an optional colour swatch with an 8 by 8 picture on it (a
/// material and a section), and a bar. It knows nothing of parts, machines or how it is drawn, so anything can fill one: a part's
/// source here, a held tool (<see cref="PanelRender.DrawCard"/>). Plain arrays: filling one allocates nothing, so the text is
/// put in with <see cref="Add(int, string)"/> and friends rather than built as a string.
/// </summary>
public sealed class PanelCard
{
    public const int MaxLines = 8, MaxChars = 32;

    private readonly char[] _text = new char[MaxLines * MaxChars];
    private readonly int[] _len = new int[MaxLines];

    public PanelState State;
    public bool HasIcon;
    public Vector3 IconColour;
    public ulong IconMask; // bit y * 8 + x of the picture, y up (0: a plain swatch)
    public float Bar = -1; // 0 to 1; below 0: none
    public int Picture; // a camera's picture behind the text: its layer in CameraFeeds plus one (0: none)
    public int Video; // a browser page's picture: its layer in VideoFeeds plus one (0: none)
    public int VideoWidth, VideoHeight; // how much of the layer it fills
    public Vector4 VideoRect; // where on the panel (PanelDef.Picture)
    public PanelTouchDef[] Touch = []; // the panel's buttons
    public readonly string?[] TouchLabels = new string?[PanelTouch.MaxRegions]; // a source's own words for them (null: the part file's)
    public int Hover = -1; // the button aimed at on this PC
    public int Mark = -1; // a line drawn on a lit strip (a list's pick); below 0: none
    public PanelDraw Draw;
    public bool HasInk; // text, needle, trace and bars all in Ink, not the state's colours (a display's "ink" setting)
    public Vector3 Ink;
    public PanelRamp Ramp;
    public bool Plain; // every line in the body colour, none as a title
    public bool Fit; // one line as big as the panel takes: a readout
    public float Value; // 0 to 1: what Draw shows
    public int Lamps, LampBits;
    public FlightEffectors? Flight; // a Flight Diagnostic Panel's plot (it isn't copied: a card is filled and laid out at once)
    public Vector3 FlightMin, FlightMax, FlightCom;
    public float[]? Trace; // oldest at TraceAt (it isn't copied: a card is filled and laid out at once)
    public int TraceAt;
    public float TraceLow, TraceHigh; // the graph's scale; the same: it fits the trace

    public void Clear()
    {
        Array.Clear(_len);
        State = PanelState.Off;
        HasIcon = false;
        IconColour = default;
        IconMask = 0;
        Bar = -1;
        Picture = 0;
        Video = 0;
        Touch = [];
        Array.Clear(TouchLabels);
        Hover = -1;
        Mark = -1;
        Draw = PanelDraw.None;
        HasInk = false;
        Ink = default;
        Ramp = PanelRamp.Solid;
        Plain = false;
        Fit = false;
        Value = 0;
        Lamps = LampBits = 0;
        Trace = null;
        Flight = null;
    }

    public ReadOnlySpan<char> Line(int i) => _text.AsSpan(i * MaxChars, _len[i]);

    public PanelCard Add(int line, string s)
    {
        foreach (var ch in s)
            Add(line, ch);
        return this;
    }

    public PanelCard Add(int line, char ch)
    {
        if (line is >= 0 and < MaxLines && _len[line] < MaxChars)
            _text[line * MaxChars + _len[line]++] = ch >= 32 && ch < 127 ? ch : '?';
        return this;
    }

    public PanelCard Add(int line, int value)
    {
        if (value < 0)
        {
            Add(line, '-');
            value = -value;
        }
        var div = 1;
        while (value / div >= 10)
            div *= 10;
        for (; div > 0; div /= 10)
            Add(line, (char)('0' + value / div % 10));
        return this;
    }

    /// <summary>A number to <paramref name="decimals"/> places: "12.50". Past what a line shows it stays a number, not a wrapped one.</summary>
    public PanelCard AddFixed(int line, float value, int decimals)
    {
        decimals = Math.Clamp(decimals, 0, 4);
        var scale = 1L;
        for (var k = 0; k < decimals; k++)
            scale *= 10;
        var v = (long)MathF.Round(Math.Clamp(value, -9e8f, 9e8f) * scale);
        if (v < 0)
        {
            Add(line, '-');
            v = -v;
        }
        Add(line, (int)(v / scale));
        if (decimals == 0)
            return this;
        Add(line, '.');
        var frac = v % scale;
        for (var div = scale / 10; div > 0; div /= 10)
            Add(line, (char)('0' + frac / div % 10));
        return this;
    }

    /// <summary>A list's row on <paramref name="cols"/> characters: a marker, the name cut to fit, the count at the right edge.</summary>
    public PanelCard AddRow(int line, char marker, string name, int count, int cols)
    {
        var digits = 1;
        for (var n = count; n >= 10; n /= 10)
            digits++;
        var room = Math.Max(0, cols - digits - 3); // (the marker, the space before the x, the x)
        Add(line, marker);
        for (var k = 0; k < room; k++)
            Add(line, k < name.Length ? name[k] : ' ');
        return Add(line, " x").Add(line, count);
    }

    /// <summary>A length in cm as metres: "1.85 m".</summary>
    public PanelCard AddMetres(int line, int cm)
    {
        Add(line, cm / 100).Add(line, '.');
        Add(line, (char)('0' + cm / 10 % 10)).Add(line, (char)('0' + cm % 10));
        return Add(line, " m");
    }

    /// <summary>
    /// A list of "key: what" entries, comma-separated, set out on lines of <paramref name="cols"/> characters: entries share a line when
    /// they fit, and a long one breaks at a space. Lines before <paramref name="skip"/> are left out, and what falls past
    /// <paramref name="last"/> too, so a caller pages by asking again. <paramref name="at"/> and <paramref name="column"/> carry on from one list to the next: the
    /// line in the whole text, and how full it is. Returns how many lines the text takes. Allocates nothing.
    /// </summary>
    public int Wrap(ReadOnlySpan<char> text, int cols, int first, int last, int skip, ref int at, ref int column)
    {
        while (text.Length > 0)
        {
            var end = text.IndexOf(", ");
            var entry = end < 0 ? text : text[..end];
            text = end < 0 ? default : text[(end + 2)..];
            if (column > 0 && column + 2 + entry.Length > cols)
            {
                at++;
                column = 0;
            }
            else if (column > 0)
            {
                Put(", ", first, last, skip, at);
                column += 2;
            }
            while (entry.Length > 0)
            {
                var room = cols - column;
                var take = entry.Length;
                if (take > room)
                {
                    take = entry[..Math.Min(room + 1, entry.Length)].LastIndexOf(' ');
                    if (take <= 0)
                        take = room; // (one word longer than the line)
                }
                Put(entry[..take], first, last, skip, at);
                column += take;
                entry = entry[take..].TrimStart(' ');
                if (entry.Length > 0)
                {
                    at++;
                    column = 0;
                }
            }
        }
        return column > 0 ? at + 1 : at;
    }

    private void Put(ReadOnlySpan<char> s, int first, int last, int skip, int at)
    {
        var line = first + at - skip;
        if (at < skip || line > last)
            return;
        foreach (var ch in s)
            Add(line, ch);
    }

    /// <summary>Everything drawn, as one number: a card laid out again only when this changes.</summary>
    public int Hash()
    {
        var h = 2166136261u;
        void Mix(uint v) => h = (h ^ v) * 16777619u;
        for (var l = 0; l < MaxLines; l++)
        {
            Mix((uint)_len[l]);
            for (var k = 0; k < _len[l]; k++)
                Mix(_text[l * MaxChars + k]);
        }
        Mix((uint)State | (HasIcon ? 256u : 0));
        Mix((uint)(int)(Bar * 96) ^ (uint)Picture << 24);
        Mix((uint)Video);
        Mix((uint)(Hover + 1));
        foreach (var label in TouchLabels)
            Mix((uint)(label?.GetHashCode() ?? 0));
        Mix((uint)IconMask);
        Mix((uint)(IconMask >> 32));
        Mix((uint)(IconColour.X * 255) | (uint)(IconColour.Y * 255) << 8 | (uint)(IconColour.Z * 255) << 16);
        Mix((uint)Mark);
        if (HasInk)
            Mix((uint)(Ink.X * 255) | (uint)(Ink.Y * 255) << 8 | (uint)(Ink.Z * 255) << 16 | 1u << 24);
        Mix((uint)Ramp);
        Mix((uint)Draw | (Plain ? 256u : 0) | (Fit ? 512u : 0));
        Mix((uint)(int)(Value * 720));
        Mix((uint)LampBits | (uint)Lamps << 16);
        if (Trace != null)
        {
            Mix((uint)TraceAt);
            foreach (var t in Trace)
                Mix((uint)BitConverter.SingleToInt32Bits(t));
        }
        return (int)h;
    }
}

/// <summary>Where cards come from, by the name a part file gives. A new use of panels is a line here and a <see cref="PanelFill"/>.</summary>
public static class PanelSources
{
    private static readonly Dictionary<string, PanelFill> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ["status"] = Status,
        ["press.bay"] = ConstructPress.FillBay,
        ["press.die"] = ConstructPress.FillDie,
        ["camera"] = ConstructRemote.FillScreen,
        ["media"] = MediaDirector.Fill,
        ["terminal"] = ConstructTerminals.FillPanel,
        ["machine"] = MachineScreens.Fill,
        ["logic.readout"] = LogicDisplays.Readout,
        ["logic.bar"] = LogicDisplays.BarH,
        ["logic.barv"] = LogicDisplays.BarV,
        ["logic.dial"] = LogicDisplays.Dial,
        ["logic.graph"] = LogicDisplays.Graph,
        ["logic.lamps"] = LogicDisplays.Lamps,
        ["logic.sign"] = LogicDisplays.Sign,
        ["logic.compass"] = LogicDisplays.Compass,
        ["logic.altimeter"] = LogicDisplays.Altimeter,
        ["logic.speedometer"] = LogicDisplays.Speedometer,
        ["logic.keypad"] = LogicDisplays.Keypad,
        ["office.fax"] = OfficeParts.FillFax,
        ["office.computer"] = OfficeParts.FillComputer,
        ["flight.panel"] = ConstructFlight.FillPanel,
        ["flight.diagnostic"] = ConstructFlight.FillDiagnostic,
    };

    public static PanelFill? Find(string name) => All.GetValueOrDefault(name);

    public static string Names => string.Join(", ", All.Keys);

    /// <summary>Any part: its name and health.</summary>
    private static void Status(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        card.State = p.Broken ? PanelState.Fault : PanelState.Idle;
        card.Add(0, d.Name);
        card.Bar = Math.Clamp(p.Health / MathF.Max(1, d.Health), 0, 1);
    }
}

/// <summary>Reads a part file's <c>"panels"</c> list.</summary>
public static class ConstructPanels
{
    public const int MaxPanels = 8;

    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "panels") is not { } list)
            return;
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > MaxPanels)
            throw new ConstructPartException($"{file}: \"panels\" lists 1 to {MaxPanels} display panels ({{source, at, size, face, up}})");
        var made = new List<PanelDef>();
        var n = 0;
        foreach (var e in list.EnumerateArray())
        {
            var where = $"panels[{n++}]";
            ConstructParts.Known(e, ["source", "slot", "at", "size", "face", "up", "rows", "range", "picture", "touch"], $"{file}, {where}");
            var p = new PanelDef { Source = ConstructParts.Str(ConstructParts.Need(e, "source", $"{file}, {where}"), file, where + ".source") };
            p.Fill = PanelSources.Find(p.Source) ?? throw new ConstructPartException($"{file}: {where}.source \"{p.Source}\" isn't one of {PanelSources.Names}");
            p.Slot = ConstructParts.Opt(e, "slot") is { } sl ? (int)ConstructParts.Num(sl, file, where + ".slot") : 0;
            ConstructParts.Range(p.Slot, 0, 7, file, where + ".slot");
            p.At = ConstructParts.Vec(ConstructParts.Need(e, "at", $"{file}, {where}"), file, where + ".at");
            var size = ConstructParts.Need(e, "size", $"{file}, {where}");
            if (size.ValueKind != JsonValueKind.Array || size.GetArrayLength() != 2)
                throw new ConstructPartException($"{file}: {where}.size is [width, height]");
            p.Size = new Vector2(ConstructParts.Num(size[0], file, where + ".size"), ConstructParts.Num(size[1], file, where + ".size"));
            ConstructParts.Range(p.Size.X, 0.05f, 128, file, where + ".size");
            ConstructParts.Range(p.Size.Y, 0.05f, 128, file, where + ".size");
            p.Face = ConstructParts.DirOf(ConstructParts.Str(ConstructParts.Need(e, "face", $"{file}, {where}"), file, where + ".face"), file, where);
            p.Up = ConstructParts.Opt(e, "up") is { } up ? ConstructParts.DirOf(ConstructParts.Str(up, file, where + ".up"), file, where) : (byte)(p.Face >> 1 == 1 ? 5 : 2);
            if (p.Face >> 1 == p.Up >> 1)
                throw new ConstructPartException($"{file}: {where}: \"up\" lies in the panel, square to \"face\"");
            p.Rows = ConstructParts.Opt(e, "rows") is { } r ? (int)ConstructParts.Num(r, file, where + ".rows") : 4;
            ConstructParts.Range(p.Rows, 1, PanelCard.MaxLines, file, where + ".rows");
            // (a 6 cm row reads at about 8 m and shows as a coloured card to 25: past that it is a few pixels, so it isn't drawn)
            var row = p.Size.Y / p.Rows;
            p.Range = ConstructParts.Opt(e, "range") is { } g ? ConstructParts.Num(g, file, where + ".range") : Math.Clamp(row * 400, 12, 120);
            ConstructParts.Range(p.Range, 1, 400, file, where + ".range");
            PanelTouch.Parse(p, e, file, where);
            made.Add(p);
        }
        d.Panels = made.ToArray();
    }
}
