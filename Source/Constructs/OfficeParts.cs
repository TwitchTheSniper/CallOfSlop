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
/// The Fax Machine and the Computer as parts (Source/PartKit/Generators/OfficeParts.cs): the part file's key, "plugged in", and their panels.
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

    // ---- The Scammers' adverts (Story/Office/Scammers.json; the server side: Game/GameSandboxScams.cs) ----

    /// <summary>The start of a scammer's message ("[INBOX] Brenda: ..."), and of the computer's own lines ("[PC] ..."), which end a scam on the screens that heard it.</summary>
    public const string Inbox = "[INBOX] ", Pc = "[PC] ";

    /// <summary>A computer's own seed: its place on its construct, the same on every peer and through a save. Its adverts come from it.</summary>
    public static uint Seed(in CPart p, int i) =>
        (uint)i * 0x27d4eb2fu ^ BitConverter.SingleToUInt32Bits(p.Pos.X) * 7u ^ BitConverter.SingleToUInt32Bits(p.Pos.Y) * 131u ^ BitConverter.SingleToUInt32Bits(p.Pos.Z) * 8191u;

    /// <summary>Server: a finger on an advert. The computer is hooked by that advert's scam (<see cref="CPart.State"/> the scam plus one, <see cref="CPart.Rounds"/> its messages sent, <see cref="CPart.Count"/> seconds towards the next).</summary>
    public static string? ClickAdvert(Construct c, int part, int slot)
    {
        if (ScamDesk.Script is not { } desk || desk.AdvertAt(Seed(c.Parts[part], part), slot) is not (>= 0 and var ad))
            return "the advert won't load";
        if (c.Parts[part].D.Office != OfficeKind.Computer)
            return "that isn't a computer";
        if (!PluggedIn(c, part))
            return "no internet: plug the Computer into a battery's circuit";
        ref var p = ref c.Parts[part];
        var scam = desk.Adverts[ad].Scam;
        if (p.State == scam + 1)
            return "it's already open: check your inbox";
        p.State = (byte)(scam + 1);
        p.Rounds = 0;
        p.Count = 0;
        return null;
    }

    // What this peer knows of a hooked computer: the host reads the part; a client remembers the one its player clicked, and what it heard.
    private static ushort _hookedBody = ushort.MaxValue;
    private static int _hookedPart = -1, _hookedScam = -1, _inboxCount;
    private static readonly string[] _inbox = new string[Keep];

    /// <summary>A client's player clicked an advert on this computer (a guess until the scammers write: the server may refuse).</summary>
    public static void Clicked(Construct c, int part, int slot)
    {
        if (ScamDesk.Script is not { } desk || desk.AdvertAt(Seed(c.Parts[part], part), slot) is not (>= 0 and var ad))
            return;
        (_hookedBody, _hookedPart, _hookedScam, _inboxCount) = (c.Id, part, desk.Adverts[ad].Scam, 0);
    }

    /// <summary>A scammer's message arrived (every peer): kept for the screen. Allocates only when one arrives.</summary>
    public static void HeardInbox(string text)
    {
        if (text.StartsWith(Pc, StringComparison.Ordinal))
        {
            (_hookedBody, _hookedPart, _hookedScam, _inboxCount) = (ushort.MaxValue, -1, -1, 0); // (the scam is over)
            return;
        }
        if (!text.StartsWith(Inbox, StringComparison.Ordinal))
            return;
        var body = text[Inbox.Length..];
        var colon = body.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && colon < 40 && ScamDesk.Script is { } desk && desk.Scams.Any(s => body.StartsWith(s.From + ": ", StringComparison.Ordinal)))
        {
            _inboxCount = 0; // (a new message)
            body = body[(colon + 2)..];
        }
        if (_inboxCount < Keep)
            _inbox[_inboxCount++] = body;
    }

    /// <summary>A computer's screen: how to email IT, and two adverts; hooked, the scam's pop-up and its latest message.</summary>
    public static void FillComputer(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        if (p.Broken)
        {
            card.State = PanelState.Fault;
            card.Add(0, "FATAL EXCEPTION 0E");
            Blank(card);
            return;
        }
        var on = PluggedIn(c, part);
        if (!on)
        {
            card.State = PanelState.Off;
            card.Add(0, "NO POWER");
            card.Add(1, "Plug into a");
            card.Add(2, "battery.");
            Blank(card);
            return;
        }
        var desk = ScamDesk.Script;
        if (desk != null)
            for (var t = 0; t < card.Touch.Length && t < 2; t++)
                card.TouchLabels[t] = desk.AdvertAt(Seed(p, part), t) is (>= 0 and var ad) ? desk.Adverts[ad].Label : "";
        else
            Blank(card);
        // Hooked: the host knows from the part; a client from the advert its player clicked here.
        var scam = Host ? p.State - 1 : c.Id == _hookedBody && part == _hookedPart ? _hookedScam : -1;
        if (desk != null && scam >= 0 && scam < desk.Scams.Length)
        {
            var s = desk.Scams[scam];
            card.State = PanelState.Refused;
            card.Add(0, s.Popup);
            int at = 1, column = 0;
            var lines = Host ? p.Rounds > 0 && p.Rounds <= s.Messages.Length ? s.Messages[p.Rounds - 1] : null : null;
            if (!Host && _inboxCount > 0)
            {
                for (var k = 0; k < _inboxCount; k++)
                {
                    card.Wrap(_inbox[k], 16, 1, 4, 0, ref at, ref column);
                    if (column > 0)
                        (at, column) = (at + 1, 0);
                }
                return;
            }
            if (lines != null)
            {
                foreach (var line in lines)
                {
                    card.Wrap(line, 16, 1, 4, 0, ref at, ref column);
                    if (column > 0)
                        (at, column) = (at + 1, 0);
                }
                return;
            }
            card.Add(1, "Connecting to");
            for (var k = 0; k < s.From.Length && k < 16; k++)
                card.Add(2, s.From[k]);
            card.Add(3, "Please wait...");
            return;
        }
        card.State = PanelState.Ok;
        card.Add(0, "IT SERVICE DESK");
        card.Add(1, "Stand here and");
        card.Add(2, "type in chat:");
        card.Add(3, "it emails IT.");
        card.Add(4, "Replies by fax.");
    }

    private static void Blank(PanelCard card)
    {
        for (var t = 0; t < card.Touch.Length; t++)
            card.TouchLabels[t] = "";
    }
}
