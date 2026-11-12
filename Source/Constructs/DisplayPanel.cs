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
    public float TouchTop; // the top of the buttons along its bottom, a share of its height (text rows stay above it)
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
    public const int MaxLines = 16, MaxChars = 64; // (16 and 64 since the Text size setting: a small size shows more)

    private readonly char[] _text = new char[MaxLines * MaxChars];
    private readonly int[] _len = new int[MaxLines];
    private readonly byte[] _tintFrom = new byte[MaxLines], _tintTo = new byte[MaxLines]; // a span drawn in its own colour (a state word)
    private readonly Vector3[] _tint = new Vector3[MaxLines];
    private readonly bool[] _dim = new bool[MaxLines]; // a line of detail, in a dimmer colour

    // How it is laid out at the part's Text size (PanelRender sets these before a fill): a source that reflows reads them.
    public int Rows = 4; // text rows the panel is laid out in
    public int Cols = 32; // characters a full line holds
    public int TextRows = 4; // rows from the top that no button along the bottom covers

    // A list longer than the panel (the viewer's own, never on the wire): PanelRender keeps Scroll per panel on this PC.
    public int Scroll; // in: the first entry the viewer asked for; out: as the source clamped it
    public int ScrollMax; // out: the most Scroll can be (0: everything shows; the mouse wheel and Up/Down do nothing)
    public int Follow = -1; // in and out: the source's own memory of what it keeps in view (the console's pick), so a new pick scrolls to it

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
        Array.Clear(_tintTo);
        Array.Clear(_dim);
        ScrollMax = 0;
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

    public int Length(int line) => line is >= 0 and < MaxLines ? _len[line] : 0;

    /// <summary>Characters <paramref name="from"/> to <paramref name="to"/> of a line in their own colour (one span a line): a state word in its lamp's colour.</summary>
    public PanelCard Tint(int line, int from, int to, Vector3 colour)
    {
        if (line is < 0 or >= MaxLines)
            return this;
        _tintFrom[line] = (byte)Math.Clamp(from, 0, MaxChars);
        _tintTo[line] = (byte)Math.Clamp(to, 0, MaxChars);
        _tint[line] = colour;
        return this;
    }

    /// <summary>A line of detail under a row: drawn dimmer than the rest.</summary>
    public PanelCard Dim(int line)
    {
        if (line is >= 0 and < MaxLines)
            _dim[line] = true;
        return this;
    }

    public bool IsDim(int line) => _dim[line];

    /// <summary>The colour of character <paramref name="k"/> of a line, or false for the line's own.</summary>
    public bool TintAt(int line, int k, out Vector3 colour)
    {
        colour = _tint[line];
        return k >= _tintFrom[line] && k < _tintTo[line];
    }

    /// <summary>A line cut to <paramref name="cols"/> characters, ending in ".." when it was longer: nothing is cut off without a mark.</summary>
    public PanelCard Clip(int line, int cols)
    {
        if (line is < 0 or >= MaxLines || cols < 3 || _len[line] <= cols)
            return this;
        _len[line] = cols;
        _text[line * MaxChars + cols - 1] = '.';
        _text[line * MaxChars + cols - 2] = '.';
        return this;
    }

    /// <summary>Line <paramref name="from"/>'s text on the end of <paramref name="line"/>, and <paramref name="from"/> emptied.</summary>
    public PanelCard Join(int line, int from)
    {
        if (from is < 0 or >= MaxLines || line == from)
            return this;
        for (var k = 0; k < _len[from]; k++)
            Add(line, _text[from * MaxChars + k]);
        return ClearLine(from);
    }

    public PanelCard ClearLine(int line)
    {
        if (line is >= 0 and < MaxLines)
        {
            _len[line] = 0;
            _tintTo[line] = 0;
            _dim[line] = false;
        }
        return this;
    }

    /// <summary>A line moved <paramref name="n"/> characters to the right.</summary>
    public PanelCard Indent(int line, int n)
    {
        if (line is < 0 or >= MaxLines || n <= 0)
            return this;
        var len = Math.Min(_len[line] + n, MaxChars);
        for (var k = len - 1; k >= n; k--)
            _text[line * MaxChars + k] = _text[line * MaxChars + k - n];
        for (var k = 0; k < n && k < len; k++)
            _text[line * MaxChars + k] = ' ';
        _len[line] = len;
        return this;
    }

    /// <summary>Spaces up to column <paramref name="col"/>.</summary>
    public PanelCard PadTo(int line, int col)
    {
        while (line is >= 0 and < MaxLines && _len[line] < Math.Min(col, MaxChars))
            Add(line, ' ');
        return this;
    }

    /// <summary>
    /// <paramref name="s"/> in a column <paramref name="width"/> wide: whole when it fits; else, when <paramref name="marquee"/>, a window that slides
    /// along it (two characters a second, a pause at each end) and otherwise cut, ending in "..". Allocates nothing.
    /// </summary>
    public PanelCard Field(int line, string s, int width, bool marquee, double now)
    {
        if (width <= 0)
            return this;
        if (s.Length <= width)
        {
            Add(line, s);
            for (var k = s.Length; k < width; k++)
                Add(line, ' ');
            return this;
        }
        if (!marquee || width < 4)
        {
            for (var k = 0; k < width - 2; k++)
                Add(line, s[k]);
            return Add(line, "..");
        }
        var travel = s.Length - width;
        var cycle = travel + 6; // (3 steps' pause at each end)
        var step = (int)(now * 2) % (2 * cycle);
        var at = step < cycle ? Math.Clamp(step - 3, 0, travel) : Math.Clamp(travel - (step - cycle - 3), 0, travel);
        for (var k = 0; k < width; k++)
            Add(line, s[at + k]);
        return this;
    }

    /// <summary>The scroll mark at the right of a line: "3-8 of 14".</summary>
    public PanelCard ScrollMark(int line, int first, int shown, int total, int cols)
    {
        if (shown >= total || total <= 0 || shown <= 0)
            return this;
        var width = 5 + Digits(first + 1) + Digits(first + shown) + Digits(total);
        var brief = _len[line] + 1 + width > cols; // (short of room: "3-8/14")
        if (brief)
            width -= 3;
        if (_len[line] + 1 + width > cols)
            Clip(line, Math.Max(3, cols - width - 1));
        PadTo(line, cols - width);
        var from = _len[line];
        Add(line, first + 1).Add(line, '-').Add(line, first + shown).Add(line, brief ? "/" : " of ").Add(line, total);
        return Tint(line, from, _len[line], new Vector3(0.62f, 0.7f, 0.78f));
    }

    public static int Digits(int n)
    {
        var d = 1;
        for (n = Math.Abs(n); n >= 10; n /= 10)
            d++;
        return d;
    }

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

    /// <summary>A size with no trailing zeros: 0.3, 0.25, 1 (up to two decimals; a bill's or a queue's sizes).</summary>
    public PanelCard AddShort(int line, float value)
    {
        var v = (int)MathF.Round(Math.Clamp(value, 0, 9e6f) * 100);
        Add(line, v / 100);
        if (v % 100 == 0)
            return this;
        Add(line, '.').Add(line, (char)('0' + v / 10 % 10));
        return v % 10 == 0 ? this : Add(line, (char)('0' + v % 10));
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
            if (_tintTo[l] > _tintFrom[l])
                Mix((uint)_tintFrom[l] | (uint)_tintTo[l] << 8 | (uint)(_tint[l].X * 255) << 16 ^ (uint)(_tint[l].Y * 255) << 4 ^ (uint)(_tint[l].Z * 255) << 24);
            if (_dim[l])
                Mix(0xD1Bu + (uint)l);
        }
        Mix((uint)Rows | (uint)TextRows << 8 | (uint)Cols << 16);
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
        ["line"] = ProductionLines.Fill,
        ["craft"] = ConstructCrafting.FillPanel,
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
        ["story.terminal"] = FragmentScript.FillTerminal,
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
            foreach (var t in p.Touch)
                if (t.At.Y < 0.25f)
                    p.TouchTop = MathF.Max(p.TouchTop, t.At.W);
            made.Add(p);
        }
        d.Panels = made.ToArray();
    }

    /// <summary>Does a part show text on a panel (so it has the Text size setting)? A camera's picture doesn't, nor a media page.</summary>
    public static bool HasText(ConstructPartDef d)
    {
        foreach (var p in d.Panels)
            if (p.Source is not ("camera" or "media"))
                return true;
        return false;
    }

    public const int MinTextSize = 50, MaxTextSize = 150;

    /// <summary>A panel's text rows at the part's Text size: more at a smaller size (100%: the part file's own).</summary>
    public static int RowsAt(PanelDef panel, float percent)
    {
        if (percent is >= 99.5f and <= 100.5f || percent <= 0)
            return panel.Rows;
        return Math.Clamp((int)MathF.Round(panel.Rows * 100f / percent), 1, PanelCard.MaxLines);
    }

    /// <summary>Rows from the top whose bottom is clear of the buttons along a panel's bottom.</summary>
    public static int FreeRows(PanelDef panel, int rows)
    {
        if (panel.TouchTop <= 0)
            return rows;
        var h = panel.Size.Y;
        var pad = h * 0.055f;
        var row = (h - 2 * pad) / rows;
        var n = 0;
        for (var l = 0; l < rows; l++)
            if (-h * 0.5f + pad + (rows - 1 - l) * row >= -h * 0.5f + panel.TouchTop * h - row * 0.12f)
                n++;
        return Math.Max(1, n);
    }
}
