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
    public const int MaxLines = 6, MaxChars = 32;

    private readonly char[] _text = new char[MaxLines * MaxChars];
    private readonly int[] _len = new int[MaxLines];

    public PanelState State;
    public bool HasIcon;
    public Vector3 IconColour;
    public ulong IconMask; // bit y * 8 + x of the picture, y up (0: a plain swatch)
    public float Bar = -1; // 0 to 1; below 0: none

    public void Clear()
    {
        Array.Clear(_len);
        State = PanelState.Off;
        HasIcon = false;
        IconColour = default;
        IconMask = 0;
        Bar = -1;
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

    /// <summary>A length in cm as metres: "1.85 m".</summary>
    public PanelCard AddMetres(int line, int cm)
    {
        Add(line, cm / 100).Add(line, '.');
        Add(line, (char)('0' + cm / 10 % 10)).Add(line, (char)('0' + cm % 10));
        return Add(line, " m");
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
        Mix((uint)(int)(Bar * 96));
        Mix((uint)IconMask);
        Mix((uint)(IconMask >> 32));
        Mix((uint)(IconColour.X * 255) | (uint)(IconColour.Y * 255) << 8 | (uint)(IconColour.Z * 255) << 16);
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
            ConstructParts.Known(e, ["source", "slot", "at", "size", "face", "up", "rows", "range"], $"{file}, {where}");
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
            made.Add(p);
        }
        d.Panels = made.ToArray();
    }
}
