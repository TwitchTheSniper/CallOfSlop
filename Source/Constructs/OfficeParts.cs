using System.Text.Json;

namespace CallOfSlop;

/// <summary>What an office part does (a part file's <c>"office"</c>): the IT Department's only lines in and out (docs/story/STORY.md, "The IT Department's fax").</summary>
public enum OfficeKind : byte
{
    None,

    /// <summary>Prints the IT Department's pages while it is on a battery's circuit (<see cref="ItDesk"/>).</summary>
    Fax,

    /// <summary>Emails the IT Department what a player standing at it types in chat.</summary>
    Computer,
}

/// <summary>
/// The Fax Machine and the Computer as parts (tools/constructs/office_parts.py): the part file's key, "plugged in", and their panels.
/// The server side (what is printed, when, and who hears it) is Game/GameSandboxOffice.cs.
/// </summary>
public static class OfficeParts
{
    /// <summary>The start of every chat line a fax prints; a page's first line names who sent it ("[FAX] IT Department: ...").</summary>
    public const string Prefix = "[FAX] ";

    /// <summary>A part file's <c>"office"</c> key.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "office") is not { } o)
            return;
        d.Office = o.GetString() switch
        {
            "fax" => OfficeKind.Fax,
            "computer" => OfficeKind.Computer,
            _ => throw new ConstructPartException($"{file}: \"office\" is \"fax\" or \"computer\""),
        };
        if (d.PowerDraw <= 0)
            throw new ConstructPartException($"{file}: an office part draws power (\"power\"): it works only plugged in");
        if (d.Panels.Length == 0)
            throw new ConstructPartException($"{file}: an office part has a panel (\"panels\"), which is how it is found");
    }

    /// <summary>
    /// Plugged in: on a construct with a battery, a working battery's links reach it and there is charge. A construct with no battery
    /// runs everything without power (docs/sandbox/CONSTRUCTS.md, "Power"), but a fax needs a wall socket: that one doesn't count.
    /// </summary>
    public static bool PluggedIn(Construct c, int i) => c.Electrical && i < c.PartCount && !c.Parts[i].Broken && ConstructPower.On(c, i);

    // ---- What this peer has heard: a client's copy of a fax's state isn't sent, so its panel shows the last page heard ----

    private const int Keep = 6;
    private static readonly string[] _heard = new string[Keep];
    private static int _heardCount;

    /// <summary>A chat line arrived (every peer): a fax line is kept for the panels. Allocates only when a fax prints.</summary>
    public static void Heard(string text, string from)
    {
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            return;
        var body = text[Prefix.Length..];
        if (body.StartsWith(from + ": ", StringComparison.Ordinal))
        {
            _heardCount = 0; // (a new page)
            body = body[(from.Length + 2)..];
        }
        if (_heardCount < Keep)
            _heard[_heardCount++] = body;
    }

    public static void ForgetHeard() => _heardCount = 0;

    /// <summary>This peer runs the IT desk (a host): its faxes know their pages, so the panels don't need what was heard.</summary>
    public static bool Host;

    // ---- Panels (DisplayPanel.cs) ----

    /// <summary>A fax's page: the last it printed (the host knows which), else the last page this peer heard.</summary>
    public static void FillFax(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        if (p.Broken)
        {
            card.State = PanelState.Fault;
            card.Add(0, "FAX - OUT OF ORDER");
            return;
        }
        var on = PluggedIn(c, part);
        card.State = on ? PanelState.Ok : PanelState.Off;
        card.Add(0, on ? "FAX - LINE OPEN" : "FAX - NO POWER");
        var desk = ItDesk.Script;
        var page = desk != null && p.State > 0 ? desk.Page(p.State - 1) : null;
        int at = 0, column = 0;
        if (page != null)
        {
            foreach (var line in page)
            {
                card.Wrap(line, PanelCard.MaxChars, 1, 5, 0, ref at, ref column);
                if (column > 0)
                    (at, column) = (at + 1, 0);
            }
            return;
        }
        if (_heardCount > 0 && !Host)
        {
            for (var k = 0; k < _heardCount; k++)
            {
                card.Wrap(_heard[k], PanelCard.MaxChars, 1, 5, 0, ref at, ref column);
                if (column > 0)
                    (at, column) = (at + 1, 0);
            }
            return;
        }
        card.Add(1, on ? "Waiting for a call." : "Plug it into a battery's");
        card.Add(2, on ? "" : "circuit, and wait.");
    }

    /// <summary>A computer's screen: how to email IT.</summary>
    public static void FillComputer(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        if (p.Broken)
        {
            card.State = PanelState.Fault;
            card.Add(0, "FATAL EXCEPTION 0E");
            return;
        }
        var on = PluggedIn(c, part);
        card.State = on ? PanelState.Ok : PanelState.Off;
        card.Add(0, on ? "IT SERVICE DESK" : "NO POWER");
        if (on)
        {
            card.Add(1, "Stand here and type");
            card.Add(2, "in chat to email IT.");
            card.Add(3, "Replies come by fax.");
        }
        else
            card.Add(1, "Plug into a battery.");
    }
}
