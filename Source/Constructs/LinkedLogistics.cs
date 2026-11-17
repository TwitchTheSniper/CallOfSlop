using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a linked logistics part is (a part file's <c>"logiLink"</c> block: docs/sandbox/design/linked-logistics.md section 3).</summary>
public enum LinkKind : byte
{
    None,
    Transmitter, // a link end that shows a code (one planetoid)
    Receiver, // a link end that takes a code (one planetoid)
    Relay, // both: shows a code and takes one; reaches the whole star system
    Dock, // a factory's staging hold (a Piece Rack whose hold is a terminal-like tray): ships trade through it
    Port, // a ship's hatch: keeps the ship's manifest
    Route, // a Route Computer: a ship's standing route
    Dispatcher, // a factory's fleet of route ships
}

public sealed partial class ConstructPartDef
{
    public LinkKind LinkKind;

    /// <summary>A link end: a Transmitter, a Receiver or a Relay.</summary>
    public bool IsLinkEnd => LinkKind is LinkKind.Transmitter or LinkKind.Receiver or LinkKind.Relay;
}

/// <summary>One manifest line on a ship's Cargo Port: what it carries for a linked order (LinkLine), and where to.</summary>
public struct ManifestLine
{
    public ushort Line; // the ledger's line id (0: none)
    public ushort Kind; // the part (a catalogue part: kinds go by name in saves)
    public ushort Loaded, Delivered;
    public string Dest; // where it goes: "Glacier Yard" (the far end's name)
    public ushort Use; // a submodule's use (a sub-job's body), 0: parts
    public readonly int Aboard => Math.Max(0, Loaded - Delivered);
}

/// <summary>
/// A linked logistics part's own state (<see cref="CPart.Link"/>), so it moves with its part through splits, merges and travel; the
/// LLNK block saves it (ConstructWorldSave: <see cref="LinkedLogistics.SaveBytes"/>), and <see cref="CBodyKind.Link"/> sends what a panel
/// shows. Blueprints never carry it: a stamped Transmitter gets a fresh code.
/// </summary>
public sealed class LinkPart
{
    public uint Id; // its own LinkId (24 bits; a Transmitter's or Relay's code), 0 until the host gives it one
    public uint Pair; // the LinkId it is paired with (a Receiver's or Relay's typed code), 0: none
    public string Name = ""; // its name ("Bluestone Works"); the body's own when empty
    public string Status = ""; // host-made: what its panel says, lines split by '\n'
    public byte Linked; // host-made: link ends answering it (its Linked output)
    public bool Waiting; // host-made: lines wait for a ship here (its Waiting output)
    public LinkOffer[] Offers = []; // host-made: what linked networks hold or make, for this body's terminals (an end's)
    public readonly List<ManifestLine> Manifest = new(4); // a Cargo Port's lines
    public LinkRoute? Route; // a Route Computer's route
    public LinkFleet? Fleet; // a Dispatcher's ships
    public LinkDockRef[] Docks = []; // host-made, a Route Computer's: the Cargo Docks a route may stop at
    public int Version, SentVersion; // goes up when anything here changes (the wire's key); and what clients were last sent
    public float LastFuel; // a Route Computer: fuel at the last stop's leaving (a trip's use is the difference)
}

/// <summary>Something a linked network holds or makes, as a terminal there lists it ("Steel Beam - 40 at Bluestone Works").</summary>
public struct LinkOffer
{
    public ushort Kind;
    public int Free; // unreserved stock there
    public bool Makes; // a machine there takes the order
    public string Where; // the far end's name
    public bool Asleep; // its world is closed: production estimated
}

/// <summary>A Cargo Dock a route may stop at: its world (map), body and part, and its name.</summary>
public struct LinkDockRef
{
    public string World;
    public ushort Body, Part;
    public string Name;
}

/// <summary>
/// Linked logistics (docs/sandbox/design/linked-logistics.md, the user's answers of 2026-11-16 first): link ends pair two constructs'
/// networks for orders and stock (both ways); goods go by cargo ship through Cargo Docks and Ports; Route Computers fly standing routes;
/// Dispatchers send ships. This file: the parts, their codes and pairing, the per-world scan and the wire and save of a part's state.
/// The ledger (LinkedLogisticsLedger.cs) holds ends and lines across worlds, the planner's remote steps are in LinkedLogisticsPlan.cs,
/// the docks' exchange and manifests in LinkedLogisticsDocks.cs, routes in LinkedLogisticsRoutes.cs, the Dispatcher in
/// LinkedLogisticsDispatch.cs. Server work is bounded: the scan every 2 s, ends' reports every 2 s, the docks every second, only bodies
/// with link parts; nothing allocates in a tick without a change.
/// </summary>
public static partial class LinkedLogistics
{
    public const int MaxEndsPerBody = 4; // link ends a network (a body) uses; more are left unlinked, said on their panel
    public const int ScanTicks = 2 * World.TickRate; // the world's link parts are looked for again (a body's own count follows its rebuilds)
    public const int StepTicks = World.TickRate; // docks, lines and routes: once a second
    public const string Alphabet = "ACDEFHJKMNPRTWXY"; // 16 letters: a code's 6 are its 24 bits
    public const uint IdMask = 0xFFFFFF;
    public static readonly string[] EndOut = ["Linked", "Waiting"];
    public static readonly string[] RouteOut = ["On route", "Loading", "Fuel short"];
    public static readonly string[] FleetOut = ["Ships out", "Waiting"];

    // ---- Part files ----------------------------------------------------------------------------------------------------

    /// <summary>A part file's <c>"logiLink"</c> block: <c>kind</c>, one of transmitter, receiver, relay, dock, port, route, dispatcher.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "logiLink") is not { } s)
            return;
        ConstructParts.Known(s, ["kind"], $"{file}, logiLink");
        var kind = ConstructParts.Str(ConstructParts.Need(s, "kind", $"{file}, logiLink"), file, "logiLink.kind");
        d.LinkKind = kind switch
        {
            "transmitter" => LinkKind.Transmitter,
            "receiver" => LinkKind.Receiver,
            "relay" => LinkKind.Relay,
            "dock" => LinkKind.Dock,
            "port" => LinkKind.Port,
            "route" => LinkKind.Route,
            "dispatcher" => LinkKind.Dispatcher,
            _ => throw new ConstructPartException($"{file}: logiLink.kind \"{kind}\" isn't transmitter, receiver, relay, dock, port, route or dispatcher"),
        };
        if (d.LinkKind == LinkKind.Dock && !d.IsCraftTerminal)
            throw new ConstructPartException($"{file}: a Cargo Dock's hold is a crafting tray (storage.pieces.crafting)");
        if (d.LinkKind != LinkKind.Dock)
            d.DataOut = d.IsLinkEnd ? EndOut : d.LinkKind == LinkKind.Route ? RouteOut : d.LinkKind == LinkKind.Dispatcher ? FleetOut : d.DataOut;
    }

    /// <summary>A data output of a link part: Linked and Waiting are its Out and Out1; a third (Fuel short) is its Mem (ConstructModules.Output).</summary>
    public static float Output(in CPart p, int port) => port == 2 ? p.Mem : 0;

    // ---- Codes ------------------------------------------------------------------------------------------------------------

    /// <summary>"KX4-MRB": a LinkId's six letters (24 bits, four a letter), the code a Receiver takes.</summary>
    public static string Code(uint id)
    {
        if (id == 0)
            return "------";
        Span<char> s = stackalloc char[7];
        for (var k = 0; k < 6; k++)
            s[k < 3 ? k : k + 1] = Alphabet[(int)(id >> (20 - 4 * k)) & 15];
        s[3] = '-';
        return new string(s);
    }

    /// <summary>A typed code back to its LinkId (case, spaces and the dash ignored; O is read as zero's look-alike D, I as J). 0: not a code.</summary>
    public static uint ParseCode(string text)
    {
        uint id = 0;
        var n = 0;
        foreach (var ch in text.ToUpperInvariant())
        {
            if (ch is '-' or ' ')
                continue;
            var k = Alphabet.IndexOf(ch == 'O' ? 'D' : ch == 'I' ? 'J' : ch);
            if (k < 0 || n >= 6)
                return 0;
            id = id << 4 | (uint)k;
            n++;
        }
        return n == 6 ? id : 0;
    }

    /// <summary>A new LinkId nobody uses (the ledger knows every end, asleep ones too).</summary>
    public static uint NewId()
    {
        for (var tries = 0; tries < 64; tries++)
        {
            var id = (uint)Random.Shared.Next(1, (int)IdMask + 1);
            if (!LinkLedger.Known(id))
                return id;
        }
        return (uint)Random.Shared.Next(1, (int)IdMask + 1);
    }

    /// <summary>A part's state, made when first asked (server: a link part's own; a client's comes with CBodyKind.Link).</summary>
    public static LinkPart StateOf(ref CPart p) => p.Link ??= new LinkPart();

    /// <summary>The name an end or dock goes by: its own, else its body's place ("construct 12 on Glacier").</summary>
    public static string NameOf(Construct c, int part)
    {
        if (part < c.PartCount && c.Parts[part].Link is { Name.Length: > 0 } lp)
            return lp.Name;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Link is { Name.Length: > 0 } other && c.Parts[i].D.IsLinkEnd)
                return other.Name;
        return $"construct {c.Id}";
    }

    // ---- The world's link parts (server) --------------------------------------------------------------------------------

    /// <summary>Server, every tick from ConstructWorld.Step: the scan, the ledger hand-off, lines, docks and routes, each at its own rate.</summary>
    public static void Step(ConstructWorld world)
    {
        var lw = world.LinkState;
        if (++lw.ScanTick >= ScanTicks)
        {
            lw.ScanTick = 0;
            Scan(world, lw);
        }
        if (lw.Parts.Count == 0 && !LinkLedger.HasWork(lw.WorldKey))
            return;
        if (++lw.StepTick < StepTicks)
            return;
        lw.StepTick = 0;
        var key = WorldKey();
        lw.WorldKey = key;
        LinkLedger.Tick();
        LinkLedger.Seen(key, WorldTitle());
        LinkLedger.Wake(world, key); // (a world that was asleep: what the ledger settled for it while it was closed lands now)
        Ends(world, lw, key);
        LinkPlan.Supply(world, key); // (lines posted to this world's ends: orders at its Cargo Docks)
        LinkPlan.Consume(world, key); // (lines this world posted: what was lost comes off its orders)
        LinkDocks.Step(world, key);
        LinkSub.Step(world, key);
        LinkRoutes.Step(world, key);
        LinkDispatch.Step(world, key);
        Publish(world, lw);
    }

    /// <summary>This world's key in the ledger: its map's name (one Game a planetoid).</summary>
    public static string WorldKey() => World.Map?.Name ?? "";

    /// <summary>"Glacier": the map's own name for a person.</summary>
    public static string WorldTitle() => World.Map?.Name ?? "";

    private static void Scan(ConstructWorld world, LinkWorld lw)
    {
        lw.Parts.Clear();
        foreach (var c in world.Bodies)
        {
            if (c.LinkStamp != c.RebuildStamp || c.LinkParts < 0)
            {
                c.LinkStamp = c.RebuildStamp;
                var n = 0;
                for (var i = 0; i < c.PartCount; i++)
                    if (c.DefOf(i).LinkKind != LinkKind.None)
                        n++;
                c.LinkParts = n;
            }
            if (c.LinkParts == 0)
                continue;
            for (var i = 0; i < c.PartCount && lw.Parts.Count < LinkWorld.MaxParts; i++)
                if (c.DefOf(i).LinkKind != LinkKind.None)
                {
                    lw.Parts.Add((c.Id, (ushort)i));
                    var lp = StateOf(ref c.Parts[i]);
                    if (lp.Id == 0 && !c.DefOf(i).IsLinkEnd)
                        lp.Id = NewId(); // (a Route Computer is known to a Dispatcher by it; ends get theirs in Ends, twins checked)
                }
        }
    }

    /// <summary>Is a link part working: whole, on a body that isn't a wreck, powered?</summary>
    public static bool Working(ConstructWorld world, Construct c, int part) =>
        part < c.PartCount && !c.Parts[part].Broken && !c.Wreck && ConstructRecycler.Powered(world, c, part);

    /// <summary>Every end in this world into the ledger (its report every other second), each with a fresh id if it has none or a twin.</summary>
    private static void Ends(ConstructWorld world, LinkWorld lw, string key)
    {
        LinkLedger.BeginWorld(key);
        Span<ushort> perBody = stackalloc ushort[32];
        Span<byte> perBodyN = stackalloc byte[32];
        var bodies = 0;
        foreach (var (bid, part) in lw.Parts)
        {
            if (world.Get(bid) is not { } c || part >= c.PartCount || !c.DefOf(part).IsLinkEnd)
                continue;
            ref var p = ref c.Parts[part];
            var lp = StateOf(ref p);
            if (lp.Id == 0 || LinkLedger.Twin(lp.Id, key, c.Id, part))
            {
                lp.Id = NewId(); // (a new end, or a copy of one: a code is one end's)
                lp.Version++;
            }
            var k = perBody[..bodies].IndexOf(c.Id);
            if (k < 0 && bodies < perBody.Length)
            {
                k = bodies++;
                (perBody[k], perBodyN[k]) = (c.Id, 0);
            }
            var over = k >= 0 && ++perBodyN[k] > MaxEndsPerBody;
            LinkLedger.Post(world, c, part, lp, c.DefOf(part).LinkKind, key, Working(world, c, part) && !over, over);
        }
        LinkLedger.EndWorld(key);
    }

    /// <summary>The host-made texts and outputs onto the parts, and changed bodies to clients (CBodyKind.Link).</summary>
    private static void Publish(ConstructWorld world, LinkWorld lw)
    {
        foreach (var (bid, part) in lw.Parts)
        {
            if (world.Get(bid) is not { } c || part >= c.PartCount || c.Parts[part].Link is not { } lp)
                continue;
            ref var p = ref c.Parts[part];
            var d = c.DefOf(part);
            string status;
            if (d.IsLinkEnd)
            {
                status = LinkLedger.EndStatus(lp, d.LinkKind, out var linked, out var waiting);
                p.Out = linked > 0 ? 1 : 0;
                p.Out1 = waiting ? 1 : 0;
                lp.Linked = (byte)Math.Min(255, linked);
                lp.Waiting = waiting;
                var offers = LinkLedger.OffersFor(lp.Id, lp.Pair);
                if (!SameOffers(lp.Offers, offers))
                {
                    lp.Offers = offers;
                    lp.Version++;
                }
            }
            else if (d.LinkKind == LinkKind.Dock)
                status = LinkDocks.DockStatus(world, c, part);
            else if (d.LinkKind == LinkKind.Port)
                status = LinkDocks.PortStatus(c, part, lp);
            else if (d.LinkKind == LinkKind.Route)
                status = LinkRoutes.Status(world, c, part, lp);
            else
                status = LinkDispatch.Status(world, c, part, lp);
            if (status != lp.Status)
            {
                lp.Status = status;
                lp.Version++;
            }
            if (lp.Version != lp.SentVersion)
            {
                lp.SentVersion = lp.Version;
                world.LinkChanged(c);
            }
        }
    }

    private static bool SameOffers(LinkOffer[] a, LinkOffer[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i].Kind != b[i].Kind || a[i].Free != b[i].Free || a[i].Makes != b[i].Makes || a[i].Where != b[i].Where || a[i].Asleep != b[i].Asleep)
                return false;
        return true;
    }

    // ---- What a player does (CActKind.Link) -------------------------------------------------------------------------------

    /// <summary>Server: a player's action on a link part (its panel, or the command view for a route). Null, or why not.</summary>
    public static string? Act(ConstructWorld world, Construct c, int part, LinkOp op, uint a, uint b, string text)
    {
        if (part >= c.PartCount || c.DefOf(part).LinkKind == LinkKind.None)
            return "that isn't a linked logistics part";
        var d = c.DefOf(part);
        var lp = StateOf(ref c.Parts[part]);
        text = NetSafety.CleanText(text, 40);
        switch (op)
        {
            case LinkOp.Name:
                lp.Name = text.Trim();
                lp.Version++;
                return null;
            case LinkOp.Pair:
            {
                if (d.LinkKind is not (LinkKind.Receiver or LinkKind.Relay))
                    return "only a Receiver or a Relay takes a code";
                var id = ParseCode(text);
                if (id == 0)
                    return "that isn't a code: six letters, like KX4-MRB";
                if (LinkLedger.PairRefusal(world, c, part, d.LinkKind, id) is { } why)
                    return why;
                LinkLedger.Unpaired(lp.Pair, lp.Id, "the code was changed");
                lp.Pair = id;
                lp.Version++;
                Console.WriteLine($"[linked] {d.Name} {part} on construct {c.Id} paired with {Code(id)}");
                return null;
            }
            case LinkOp.Unpair:
                if (lp.Pair == 0)
                    return "it isn't paired";
                LinkLedger.Unpaired(lp.Pair, lp.Id, "the code was cleared");
                lp.Pair = 0;
                lp.Version++;
                return null;
            case LinkOp.NewCode:
                if (d.LinkKind is not (LinkKind.Transmitter or LinkKind.Relay))
                    return "only a Transmitter or a Relay shows a code";
                LinkLedger.Unpaired(lp.Id, 0, "its code was changed");
                lp.Id = NewId();
                lp.Version++;
                return null;
            default:
                if (op is >= LinkOp.RouteClear and <= LinkOp.RouteRules)
                    return d.LinkKind == LinkKind.Route ? LinkRoutes.Act(world, c, part, lp, op, a, b, text) : "that isn't a Route Computer";
                if (op is >= LinkOp.FleetDrop and <= LinkOp.FleetRules)
                    return d.LinkKind == LinkKind.Dispatcher ? LinkDispatch.Act(world, c, part, lp, op, a, b) : "that isn't a Logistics Dispatcher";
                if (op == LinkOp.CancelLine)
                    return LinkLedger.CancelLine((ushort)a, "cancelled at its manifest");
                return "a bad action";
        }
    }

    // ---- The wire (CBodyKind.Link) ----------------------------------------------------------------------------------------

    public static bool Any(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Link != null)
                return true;
        return false;
    }

    public const int MaxStatusLines = 48;

    /// <summary>A status as its lines (u8 count, then each a str): one str strips the '\n' between them and stops at 255 bytes, so a client's
    /// panel ran its lines together and lost the end of a route's.</summary>
    private static void WriteLines(PacketWriter w, string s)
    {
        var count = 1;
        foreach (var ch in s)
            if (ch == '\n')
                count++;
        count = Math.Min(count, MaxStatusLines);
        w.U8((byte)count);
        var start = 0;
        for (var k = 0; k < count; k++)
        {
            var end = k == count - 1 ? -1 : s.IndexOf('\n', start);
            if (end < 0)
                end = s.Length;
            w.Str(s[start..end]);
            start = Math.Min(s.Length, end + 1);
        }
    }

    private static string ReadLines(ref PacketReader r)
    {
        int count = r.U8();
        if (count <= 1)
            return count == 0 ? "" : r.Str();
        var sb = new System.Text.StringBuilder();
        for (var k = 0; k < count; k++)
            sb.Append(k > 0 ? "\n" : "").Append(r.Str());
        return sb.ToString();
    }

    /// <summary>One body's link parts as its panels show them: ids, names, status lines, offers, a port's manifest, a route computer's docks.</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Link).U16(c.Id);
        var at = w.Length;
        w.U16(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Link is not { } lp)
                continue;
            w.U16((ushort)i).U32(lp.Id).U32(lp.Pair).Str(lp.Name);
            WriteLines(w, lp.Status);
            w.U8(lp.Linked).U8((byte)(lp.Waiting ? 1 : 0));
            var on = Math.Min(lp.Offers.Length, 128);
            w.U8((byte)on);
            for (var k = 0; k < on; k++)
            {
                ref readonly var o = ref lp.Offers[k];
                PartRef.Write(w, o.Kind);
                w.U32((uint)Math.Max(0, o.Free)).U8((byte)((o.Makes ? 1 : 0) | (o.Asleep ? 2 : 0))).Str(o.Where);
            }
            var mn = Math.Min(lp.Manifest.Count, 64);
            w.U8((byte)mn);
            for (var k = 0; k < mn; k++)
            {
                var m = lp.Manifest[k];
                w.U16(m.Line);
                PartRef.Write(w, m.Kind);
                w.U16(m.Loaded).U16(m.Delivered).U16(m.Use).Str(m.Dest ?? "");
            }
            var dn = Math.Min(lp.Docks.Length, 32);
            w.U8((byte)dn);
            for (var k = 0; k < dn; k++)
                w.Str(lp.Docks[k].World).U16(lp.Docks[k].Body).U16(lp.Docks[k].Part).Str(lp.Docks[k].Name);
            n++;
        }
        w.PatchU16(at, (ushort)n);
    }

    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        var id = r.U16();
        int n = r.U16();
        var c = world.Get(id);
        for (var k = 0; k < n; k++)
        {
            var part = r.U16();
            var lp = new LinkPart { Id = r.U32(), Pair = r.U32(), Name = r.Str(), Status = ReadLines(ref r), Linked = r.U8(), Waiting = r.U8() != 0 };
            int on = r.U8();
            lp.Offers = new LinkOffer[on];
            for (var j = 0; j < on; j++)
            {
                var kind = PartRef.Read(ref r);
                var free = (int)Math.Min(int.MaxValue, r.U32());
                var f = r.U8();
                lp.Offers[j] = new LinkOffer { Kind = kind, Free = free, Makes = (f & 1) != 0, Asleep = (f & 2) != 0, Where = r.Str() };
            }
            int mn = r.U8();
            for (var j = 0; j < mn; j++)
            {
                var line = r.U16();
                var kind = PartRef.Read(ref r);
                lp.Manifest.Add(new ManifestLine { Line = line, Kind = kind, Loaded = r.U16(), Delivered = r.U16(), Use = r.U16(), Dest = r.Str() });
            }
            int dn = r.U8();
            lp.Docks = new LinkDockRef[dn];
            for (var j = 0; j < dn; j++)
                lp.Docks[j] = new LinkDockRef { World = r.Str(), Body = r.U16(), Part = r.U16(), Name = r.Str() };
            if (c != null && part < c.PartCount && c.DefOf(part).LinkKind != LinkKind.None)
            {
                lp.Version = (c.Parts[part].Link?.Version ?? 0) + 1;
                c.Parts[part].Link = lp;
            }
        }
        LinkedLogistics.ClientVersion++;
    }

    /// <summary>A client's count of Link messages heard (a panel's text is made again when it changes).</summary>
    public static int ClientVersion;

    // ---- Saves (the LLNK block, after SUBJ) ---------------------------------------------------------------------------------

    /// <summary>A body's link parts' state as bytes, or null when it has none worth keeping: per part its id, pair, name, manifest, route, fleet.</summary>
    public static byte[]? SaveBytes(Construct c)
    {
        var any = false;
        for (var i = 0; i < c.PartCount && !any; i++)
            any = c.Parts[i].Link is { } lp && (lp.Id != 0 || lp.Pair != 0 || lp.Name.Length > 0 || lp.Manifest.Count > 0 || lp.Route != null || lp.Fleet != null);
        if (!any && c.SubLine == 0)
            return null;
        var w = new SaveWriter(256);
        w.U8(1); // (the block's own version)
        w.U32(c.SubStamp).U16(c.SubLine).U16(c.SubUse); // (a linked sub-job's body: which root's use it is and its line)
        var at = w.Length;
        w.U16(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Link is not { } lp)
                continue;
            w.U16((ushort)i).U32(lp.Id).U32(lp.Pair).Str(lp.Name);
            w.U8((byte)Math.Min(lp.Manifest.Count, 64));
            for (var k = 0; k < lp.Manifest.Count && k < 64; k++)
            {
                var m = lp.Manifest[k];
                w.U16(m.Line).Str(ConstructParts.Get(m.Kind)?.Name ?? "").U16(m.Loaded).U16(m.Delivered).U16(m.Use).Str(m.Dest ?? "");
            }
            LinkRoutes.Save(w, lp.Route);
            LinkDispatch.Save(w, lp.Fleet);
            w.F32(lp.LastFuel);
            n++;
        }
        var bytes = w.Span.ToArray();
        BitConverter.TryWriteBytes(bytes.AsSpan(at, 2), (ushort)n);
        return bytes;
    }

    /// <summary>A save's LLNK block onto its body (before parts that don't load go: the states move with their parts). A block that doesn't read is logged and left.</summary>
    public static void LoadBytes(Construct c, byte[] bytes)
    {
        try
        {
            var r = new SaveReader(bytes);
            var version = r.U8();
            if (version != 1)
            {
                Console.WriteLine($"[linked] construct {c.Id}: an LLNK block of version {version} skipped");
                return;
            }
            (c.SubStamp, c.SubLine, c.SubUse) = (r.U32(), r.U16(), r.U16());
            int n = r.U16();
            for (var k = 0; k < n; k++)
            {
                var part = r.U16();
                var lp = new LinkPart { Id = r.U32() & IdMask, Pair = r.U32() & IdMask, Name = r.Str() };
                int mn = r.U8();
                for (var j = 0; j < mn; j++)
                {
                    var line = r.U16();
                    var name = r.Str();
                    var m = new ManifestLine { Line = line, Kind = ConstructParts.Find(name)?.Id is { } kid ? (ushort)kid : ConstructParts.None, Loaded = r.U16(), Delivered = r.U16(), Use = r.U16(), Dest = r.Str() };
                    if (m.Kind != ConstructParts.None || m.Use != 0)
                        lp.Manifest.Add(m);
                    else
                        Console.WriteLine($"[linked] construct {c.Id}: a manifest line of \"{name}\" dropped: no such part in this build");
                }
                lp.Route = LinkRoutes.Load(r);
                lp.Fleet = LinkDispatch.Load(r);
                lp.LastFuel = r.F32();
                if (part < c.PartCount && c.DefOf(part).LinkKind != LinkKind.None)
                    c.Parts[part].Link = lp;
                else
                    Console.WriteLine($"[linked] construct {c.Id}: link state for part {part} dropped: that part isn't a linked logistics part");
            }
        }
        catch (Exception ex) when (ex is WorldFileException or ArgumentException or IndexOutOfRangeException)
        {
            Console.WriteLine($"[linked] construct {c.Id}: an LLNK block skipped: {ex.Message}");
        }
    }
}

/// <summary>What a player asks of a link part (CActKind.Link's op byte; new ones at the end).</summary>
public enum LinkOp : byte
{
    Name, // text: its name
    Pair, // text: a Transmitter's or Relay's code
    Unpair,
    NewCode, // a Transmitter or Relay: a fresh code (its pairs drop)
    RouteClear, // a Route Computer: no stops, no route
    RouteAdd, // text: the dock's world; a: body << 16 | part; b: the stop's rules (LinkRoutes.Pack)
    RouteStart, // fly it (again)
    RouteStop, // hold it where it is (suspended)
    RouteRules, // a: the stop's index; b: its rules
    FleetDrop, // a Dispatcher: a: the ship's body: no longer one of its fleet
    FleetRules, // a: threshold %, b: timeout minutes
    CancelLine, // a: a manifest line: cancelled (its goods become loose cargo)
}

/// <summary>A world's link parts and its step's clocks (server).</summary>
public sealed class LinkWorld
{
    public const int MaxParts = 512;
    public readonly List<(ushort Body, ushort Part)> Parts = new(16);
    public int ScanTick = LinkedLogistics.ScanTicks - 1, StepTick;
    public string WorldKey = "";
}

public sealed partial class ConstructWorld
{
    public readonly LinkWorld LinkState = new();
    public readonly List<ushort> LinkDirty = new(4); // bodies whose link parts' panels changed (CBodyKind.Link)

    public void LinkChanged(Construct c)
    {
        if (!LinkDirty.Contains(c.Id))
            LinkDirty.Add(c.Id);
    }
}

public sealed partial class Construct
{
    public int LinkStamp = -1, LinkParts = -1; // its link parts were counted at this RebuildStamp (LinkedLogistics.Scan)
    public ushort SubLine, SubUse; // a linked sub-job's finished body: its ledger line and its root's use (LinkedLogisticsSub.cs)
}
