using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The Sandbox's handheld built weapon and the Gun Workbench (board item dev-sandbox-weapon-parts-wp01, part 2;
/// docs/sandbox/CONSTRUCTS.md, "The Gun Workbench"). A weapon built with the Zombies weapon builder is held the way
/// Zombies holds one: slot 1 (<see cref="Player.BuiltPrimary"/>) for a gun, <see cref="Player.MeleeBuild"/> and
/// <see cref="Player.GrenadeBuild"/> for the other two, synced as the builder's text (<see cref="Msg.ZBuild"/>, so every PC
/// assembles the same weapon from the same text with the same numbers). One built weapon of each kind at a time. The
/// bench (a storage part with <c>gunBench</c>) is where it is built: F opens the real workbench window, which asks the
/// server through <see cref="CActKind.Bench"/>:
/// <list type="bullet">
/// <item>Creative (the map's <c>infiniteParts</c>): every part, endless; nothing is consumed or returned.</item>
/// <item>Survival: the parts come from the hands, the backpack and the bench's own slots; a build uses them up, a part
/// the build gave up or a disassembly goes back to the bench's slots, then the hands or backpack, then the floor
/// (<see cref="ConstructEdits.BenchGive"/>). The server counts and checks everything; the window only shows what
/// <see cref="Msg.ZRun"/> tells it you have.</item>
/// <item>Creative also spawns a standard build (Parts/Builds) complete, from the Tab inventory's Builds tab.</item>
/// </list>
/// No bench is needed to spawn; every other request must be made within reach of one.
/// </summary>
public sealed partial class Game
{
    /// <summary><see cref="CActKind.Bench"/> kinds (0 put a build in a slot and 3 take a slot apart are the Zombies workbench's numbers).</summary>
    public const int BenchOpen = 6, BenchSpawn = 7;

    /// <summary>A built weapon was given or heard in the Sandbox: the next reset clears the builds (like Zombies' own flag).</summary>
    private bool _sbBuiltGiven;

    private readonly Dictionary<string, int> _sbBenchHave = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The slots the Sandbox takes a build in: the gun in slot 1, the melee weapon and the grenade.</summary>
    public static bool SandboxBenchSlot(int slot) => slot is 0 or MeleeSlot or GrenadeSlot;

    // ---- Client (or host): asking ------------------------------------------------------------------------------------

    /// <summary>Asks the server for a bench change (kinds: 0 put <paramref name="text"/> in slot <paramref name="slot"/>, 3 take it apart, 6 open, 7 spawn a build).</summary>
    public void SandboxSendBench(int kind, int slot, string text)
    {
        var w = SbW;
        var bytes = Encoding.UTF8.GetBytes(text);
        var n = Math.Min(bytes.Length, 6000);
        w.Begin(Msg.CAct).U8((byte)CActKind.Bench).U8((byte)kind).U8((byte)slot).U16((ushort)n).Bytes(bytes.AsSpan(0, n));
        SandboxSendBuilt(w);
    }

    // ---- Server: the request -----------------------------------------------------------------------------------------

    private string? SandboxBenchAct(Player p, ref PacketReader r)
    {
        if (!r.Has(4))
            return "bad bench request";
        var kind = r.U8();
        var slot = r.U8();
        return SandboxBench(p, kind, slot, ReadLong(ref r));
    }

    /// <summary>Server: one bench request from <paramref name="p"/>. Returns why it was refused, or null.</summary>
    public string? SandboxBench(Player p, int kind, int slot, string text)
    {
        if (!SandboxOn || !p.Alive)
            return "Not now.";
        SandboxRules();
        var hands = SandboxHands;
        var creative = hands.InfiniteParts;
        if (kind == BenchSpawn)
        {
            if (!creative)
                return "Spawning a build needs a creative world (endless parts).";
            if (ParseBuild(text, out var sb, out var swhy) == null)
                return swhy;
            var sslot = SlotOfKind(sb!.Kind);
            GiveSandboxBuild(p, sslot, sb, full: true);
            hands.Notices.Add((p.Id, $"{sb.Name} spawned in your hands"));
            return null;
        }
        if (ConstructEdits.FindGunBench(Constructs, p.Eye) is not { } at)
            return "You're not at a Gun Workbench.";
        var (bench, part) = at;
        switch (kind)
        {
            case BenchOpen:
                SandboxBenchRefresh(p, bench, part);
                return null;
            case 0:
            {
                if (!SandboxBenchSlot(slot))
                    return "That slot isn't yours.";
                if (ParseBuild(text, out var nb, out var why) == null)
                    return why;
                if (SlotKind(slot) != nb!.Kind)
                    return slot == MeleeSlot ? "The melee slot takes a melee build." : slot == GrenadeSlot ? "The grenade slot takes a grenade build." : "The gun slot takes a gun build.";
                var zp = ZP(p);
                if (!creative)
                {
                    var oldCount = PartCounts(zp.Slots[slot]);
                    var newCount = PartCounts(nb);
                    hands.BenchCollect(Constructs, p.Id, bench, part, _sbBenchHave);
                    foreach (var (name, n) in newCount)
                    {
                        if (ConstructWeaponParts.DefFor(name) == null)
                            return $"The {name} is a creative-only part: it has no item to build with here.";
                        var have = oldCount.GetValueOrDefault(name) + _sbBenchHave.GetValueOrDefault(name);
                        if (n > have)
                            return $"You don't have the {name} (have {have}, the build needs {n}).";
                    }
                    // Take what the build adds, then give back what it gave up (the takes first, so a freed slot is there for the gives).
                    foreach (var (name, n) in newCount)
                        for (var i = n - oldCount.GetValueOrDefault(name); i > 0; i--)
                            hands.BenchTake(Constructs, p.Id, bench, part, name);
                    foreach (var (name, n) in oldCount)
                        for (var i = n - newCount.GetValueOrDefault(name); i > 0; i--)
                            if (ConstructWeaponParts.DefFor(name) is { } back)
                                hands.BenchGive(Constructs, p.Id, bench, part, back);
                }
                GiveSandboxBuild(p, slot, nb, full: true);
                SandboxBenchRefresh(p, bench, part);
                hands.Notices.Add((p.Id, $"Workbench: {nb.Name} is in your hands"));
                return null;
            }
            case BenchDisassemble:
            {
                if (!SandboxBenchSlot(slot))
                    return "That slot isn't yours.";
                var zp = ZP(p);
                if (zp.Slots[slot] is not { } cur)
                    return "Nothing there to take apart.";
                var n = 0;
                if (!creative)
                    foreach (var node in cur.Nodes)
                    {
                        if (ConstructWeaponParts.DefFor(node.Part.Name) is { } back)
                        {
                            hands.BenchGive(Constructs, p.Id, bench, part, back);
                            n++;
                        }
                        else
                            Console.WriteLine($"[sandbox] the {node.Part.Name} from a taken-apart build has no item (creative-only part): not returned");
                    }
                ClearSlot(p, slot);
                SandboxBenchRefresh(p, bench, part);
                hands.Notices.Add((p.Id, creative ? $"Took the {cur.Name} apart (endless parts: none kept)" : $"Took the {cur.Name} apart: {n} part{(n == 1 ? "" : "s")} back"));
                return null;
            }
        }
        return "Unknown bench request.";
    }

    private static int SlotOfKind(BuildKind k) => k == BuildKind.Melee ? MeleeSlot : k == BuildKind.Grenade ? GrenadeSlot : 0;

    /// <summary>A build's text as a legal, complete build (null and the reason if not).</summary>
    private static WeaponBuild? ParseBuild(string text, out WeaponBuild? build, out string why)
    {
        build = null;
        why = "";
        var dropped = new List<string>();
        try
        {
            build = WeaponBuild.Parse(text, Parts.Find, dropped, migrate: false);
        }
        catch (FormatException ex)
        {
            why = $"That isn't a build: {ex.Message}";
            return null;
        }
        if (dropped.Count > 0)
        {
            why = $"Unknown parts: {string.Join(", ", dropped)}";
            return build = null;
        }
        var asm = AssembleCached(build);
        if (!asm.Ok)
        {
            why = asm.Problems.Count > 0 ? asm.Problems[0].Message : "That build isn't complete.";
            return build = null;
        }
        return build;
    }

    /// <summary>Server: the build is in <paramref name="p"/>'s hands (slot 0 the gun, 4 the melee weapon, 5 the grenade), told to everyone.</summary>
    internal void GiveSandboxBuild(Player p, int slot, WeaponBuild b, bool full)
    {
        GiveBuild(p, slot, b, full);
        _zBuildsGiven = false; // (Zombies' own reset flag: the Sandbox clears its builds itself)
        _sbBuiltGiven = true;
    }

    /// <summary>Server: what <paramref name="p"/> has at the bench goes into their <see cref="ZPlayer.Parts"/> (the window's stock) and to their client.</summary>
    private void SandboxBenchRefresh(Player p, Construct bench, int part)
    {
        var zp = ZP(p);
        SandboxHands.BenchCollect(Constructs, p.Id, bench, part, _sbBenchHave);
        zp.Parts.Clear();
        foreach (var (name, n) in _sbBenchHave)
            zp.Parts[name] = n;
        SendZRun(p);
    }

    /// <summary>The window's stock in the test: the parts the server counted for this player.</summary>
    public IReadOnlyDictionary<string, int> SandboxBenchParts(Player p) => ZP(p).Parts;

    // ---- Joiners, resets ---------------------------------------------------------------------------------------------

    /// <summary>Server: a joiner gets everyone's built weapons (the builds as text, so they assemble the same).</summary>
    private void SendSandboxBuildsTo(RakNetGUID to)
    {
        if (!_sbBuiltGiven)
            return;
        foreach (var p in Players.Values)
            if (ZPlayers.TryGetValue(p.Id, out var zp))
                for (var s = 0; s < BuildSlots; s++)
                    if (zp.Slots[s] is { } b)
                    {
                        WriteBuild(_w, p.Id, s, s == 0 ? p.Primary : (byte)0, 0, b.ToText());
                        Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
                    }
    }

    /// <summary>A new match (server and client): the built weapons go, and the person's own multiplayer guns come back.</summary>
    private void SandboxClearBuilds()
    {
        if (!_sbBuiltGiven)
            return;
        _sbBuiltGiven = false;
        ZPlayers.Clear();
        foreach (var p in Players.Values)
        {
            if (p.Bot == null)
            {
                (p.WantPrimary, p.WantSecondary, p.WantPrimaryKit, p.WantSecondaryKit) = (p.MpPrimary, p.MpSecondary, p.MpPrimaryKit, p.MpSecondaryKit);
                (p.Primary, p.Secondary, p.PrimaryKit, p.SecondaryKit, p.Weapon) = (p.MpPrimary, p.MpSecondary, p.MpPrimaryKit, p.MpSecondaryKit, p.MpPrimary);
            }
            p.BuiltPrimary = p.BuiltSecondary = null;
            p.BuiltExtra[0] = p.BuiltExtra[1] = null;
            p.MeleeBuild = null;
            p.GrenadeBuild = null;
        }
        if (Local != null)
            SyncLocalSlots(Local, refill: true);
    }

    /// <summary>A packet the Sandbox shares with the Zombies subsystems (a built weapon, the parts you have): the same handlers.</summary>
    internal void OnSandboxBuildPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        if (id == (byte)Msg.ZBuild && !IsServer)
            _sbBuiltGiven = true;
        OnZombiesPacket(id, ref r, sender, now);
    }
}
