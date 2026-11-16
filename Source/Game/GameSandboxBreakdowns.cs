using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a <see cref="CActKind.Breakdown"/> says has happened to a part.</summary>
public enum BreakdownStage : byte
{
    Fixed, // the warning was seen to in time
    Warning, // it creaks and groans: it fails when the time runs out
    Failed, // it has stopped working (Broken, its health kept)
    Repaired, // a failed part put right
}

/// <summary>
/// Breakdowns that warn you first (the roadmap's first step, Karl 2026-11-15; docs/sandbox/constructs/ship.md "Breakdowns";
/// the numbers are Constructs/Breakdowns.json), on every crewed player ship in Survival (Karl, 2026-11-16: ship works,
/// docs/sandbox/design/ship-works.md 7). A ship's clock runs only while somebody alive is aboard it. Every so often one of
/// its drive parts starts to go: a works piece of a hull drive (its Drive Core, Fuel Pump, Coolant Exchanger), or a thruster
/// with no works inside (a pod, a whole jet). It creaks and groans, louder and closer together, for
/// <see cref="Breakdowns.WarnSeconds"/>. The attach tool's Fire at it sees to it in that time for nothing; a works piece
/// inside can also be seen to from the engine room's <see cref="BreakdownPanel"/>, while a pod or a jet hangs outside and is
/// only reported there (a space walk, or land). Left alone, it fails with a bang: it stops working (Broken, with its health
/// kept, which is how a breakdown is told from a part shot to pieces, even in a save) until the attach tool repairs it at
/// the part for a little metal. Server-side state is each ship's own (<see cref="Construct.BdNext"/> and the rest); clients
/// hear each creak as a <see cref="CActKind.Breakdown"/> and read a failed part from its body (protocol 321).
/// </summary>
public sealed partial class Game
{
    private const int BdStep = 15; // ticks between looks (4 a second)

    private readonly Random _bdRng = new(5309);

    /// <summary>Both sides, a new match (OpeningReset): no warning heard. (A ship's own clock is on its body, which goes with the world.)</summary>
    private void BreakdownReset()
    {
        Array.Clear(_bdSeen);
        _bdLookKey = -1;
    }

    /// <summary>
    /// Server, each tick: the crewed ships' breakdowns. It walks the players, not the bodies (the cost is bounded by the
    /// players, whatever the world holds), and steps each body someone alive rides or sits in once a look.
    /// </summary>
    private void BreakdownTick()
    {
        if (ServerTick % BdStep != 0 || !SandboxSurvivalOption)
            return;
        var t = Breakdowns.Current;
        if (t.Problem.Length > 0)
            return;
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var body = SandboxHands.Hand(p.Id).SeatBody;
            if (body == 0)
                body = p.Sim.RideBody;
            if (body == 0 || Constructs.Get(body) is not { PartCount: > 0 } c || c.BdTick == ServerTick)
                continue;
            c.BdTick = ServerTick; // (one step a look, however many are aboard)
            if (BreakdownShip(c))
                BreakdownStep(c, t);
        }
    }

    /// <summary>
    /// A body that breaks down: a ship (a thruster over ShipWorks.json's jetMost, or a Flight Stabilizer), not a wreck, and no
    /// AI crew fighting for it (an enemy frigate doesn't break down until it is taken).
    /// </summary>
    private bool BreakdownShip(Construct c)
    {
        if (c.Wreck || _shipAi.ContainsKey(c.Id))
            return false;
        if (c.StabCount > 0)
            return true;
        var most = ShipWorks.Current.JetMost;
        for (var k = 0; k < c.ThrusterCount; k++)
            if (c.DefOf(c.Thrusters[k].Part).ThrustForce > most)
                return true;
        return false;
    }

    private void BreakdownStep(Construct c, Breakdowns t)
    {
        if (c.BdNext < 0)
            c.BdNext = (int)(t.FirstMinutes * 60 * World.TickRate);
        if (c.BdDef == 0)
        {
            c.BdNext -= BdStep;
            if (c.BdNext <= 0)
                BreakdownStart(c, t);
            return;
        }
        var part = BreakdownPart(c);
        if (part < 0 || !c.Parts[part].Working)
        {
            c.BdDef = 0; // (it went some other way: shot off, shot out, removed)
            return;
        }
        c.BdLeft -= BdStep;
        if (c.BdLeft <= 0)
        {
            BreakdownFail(c, part);
            return;
        }
        c.BdCreak -= BdStep;
        if (c.BdCreak <= 0)
        {
            // Closer together as the end nears: every 7 s at first, every 2 s at the last.
            var share = c.BdLeft / (t.WarnSeconds * World.TickRate);
            c.BdCreak = (int)((2f + 5f * share) * World.TickRate * (0.8f + 0.4f * (float)_bdRng.NextDouble()));
            BreakdownSend(c, part, BreakdownStage.Warning, false);
        }
    }

    /// <summary>
    /// The k-th part a breakdown may pick (k over thrusters, then works pieces), or -1: a works piece of a drive, or a thruster
    /// with no works behind it (a pod or a whole jet). A hull drive's nozzle is not picked: its works are.
    /// </summary>
    private static int BreakdownCandidate(Construct c, int k)
    {
        if (k < c.ThrusterCount)
        {
            var i = c.Thrusters[k].Part;
            return c.DefOf(i).WorksNeeds == 0 ? i : -1;
        }
        var j = c.WorksPieces[k - c.ThrusterCount];
        return ConstructWorks.IsDrivePiece(c.DefOf(j)) ? j : -1;
    }

    private static int BreakdownCandidates(Construct c) => c.ThrusterCount + c.WorksPieceCount;

    /// <summary>A part that hangs outside the hull, so it is seen to at the part only: a thruster (a pod, a whole jet), not a works piece.</summary>
    private static bool BreakdownOutside(Construct c, int part) => c.DefOf(part).Role == PartRole.Thruster;

    private void BreakdownStart(Construct c, Breakdowns t)
    {
        c.BdNext = (int)((t.MinMinutes + (t.MaxMinutes - t.MinMinutes) * (float)_bdRng.NextDouble()) * 60 * World.TickRate);
        int working = 0, failed = 0, n = BreakdownCandidates(c);
        for (var k = 0; k < n; k++)
        {
            if (BreakdownCandidate(c, k) is not (var i and >= 0))
                continue;
            ref var p = ref c.Parts[i];
            if (p.Working)
                working++;
            else if (p.Health > 0)
                failed++;
        }
        if (working == 0 || failed >= t.MaxFailed)
            return; // (enough is broken already: the next try is a full wait away)
        var pick = _bdRng.Next(working);
        for (var k = 0; k < n; k++)
        {
            if (BreakdownCandidate(c, k) is not (var i and >= 0) || !c.Parts[i].Working || pick-- > 0)
                continue;
            c.BdDef = c.Parts[i].Def;
            c.BdPos = c.Parts[i].Pos;
            c.BdLeft = (int)(t.WarnSeconds * World.TickRate);
            c.BdCreak = 7 * World.TickRate;
            Log($"[breakdown] construct {c.Id}: the {BreakdownWhere(c, i)} starts to go ({t.WarnSeconds:0} s)");
            BreakdownSend(c, i, BreakdownStage.Warning, true);
            return;
        }
    }

    private void BreakdownFail(Construct c, int part)
    {
        c.BdDef = 0;
        ref var p = ref c.Parts[part];
        p.Broken = true; // (its health stays: a breakdown, not a wreck)
        c.Dirty = true;
        Constructs.StructureDirty.Add(c.Id);
        c.Wake();
        Constructs.Events.Add($"construct {c.Id}: {p.D.Name} broke down (stops working)");
        BreakdownSend(c, part, BreakdownStage.Failed, true);
    }

    /// <summary>The part warning now on <paramref name="c"/>, by its def and place (parts are renumbered when others go), or -1.</summary>
    private static int BreakdownPart(Construct c)
    {
        if (c.BdDef == 0)
            return -1;
        var n = BreakdownCandidates(c);
        for (var k = 0; k < n; k++)
            if (BreakdownCandidate(c, k) is var i and >= 0 && c.Parts[i].Def == c.BdDef && Vector3.DistanceSquared(c.Parts[i].Pos, c.BdPos) < 1e-4f)
                return i;
        return -1;
    }

    /// <summary>Server: a stage to every peer (the host's own handled here).</summary>
    private void BreakdownSend(Construct c, int part, BreakdownStage stage, bool say)
    {
        var secs = (byte)Math.Clamp(stage == BreakdownStage.Warning ? c.BdLeft / World.TickRate : 0, 0, 255);
        if (!Net.IsOffline)
        {
            SbW.Begin(Msg.CAct).U8((byte)CActKind.Breakdown).U16(c.Id).U16((ushort)part).U8((byte)stage).U8(secs).U8((byte)(say ? 1 : 0));
            Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
        }
        if (LocalId != byte.MaxValue)
            BreakdownHeard(c.Id, part, stage, secs, say);
    }

    /// <summary>Client: a <see cref="CActKind.Breakdown"/> after its kind.</summary>
    private void BreakdownRead(ref PacketReader r)
    {
        var body = r.U16();
        var part = r.U16();
        var stage = (BreakdownStage)r.U8();
        var secs = r.U8();
        var say = r.U8() != 0;
        BreakdownHeard(body, part, stage, secs, say);
    }

    /// <summary>This peer's view of a warning under way (the look line's): its body and part, and when it was last heard and fails.</summary>
    private struct BdSeen
    {
        public ushort Body; // 0: a free slot
        public int Part;
        public double At, Fails;
    }

    /// <summary>One slot a ship warning at once (each ship has at most one warning; a fifth takes the slot heard longest ago).</summary>
    private readonly BdSeen[] _bdSeen = new BdSeen[4];

    private void BreakdownSaw(ushort body, int part, int secs)
    {
        var slot = 0;
        for (var k = 0; k < _bdSeen.Length; k++)
        {
            if (_bdSeen[k].Body == body)
            {
                slot = k;
                break;
            }
            if (_bdSeen[k].Body == 0 || _bdSeen[k].At < _bdSeen[slot].At && _bdSeen[slot].Body != 0)
                slot = k;
        }
        _bdSeen[slot] = new BdSeen { Body = body, Part = part, At = SandboxNow, Fails = SandboxNow + secs };
    }

    /// <summary>The slot heard for <paramref name="c"/> while its warning is still under way, or -1.</summary>
    private int BreakdownSeenSlot(Construct c)
    {
        for (var k = 0; k < _bdSeen.Length; k++)
        {
            ref var b = ref _bdSeen[k];
            if (b.Body == c.Id && b.Part >= 0 && b.Part < c.PartCount && SandboxNow - b.At < 10 && SandboxNow < b.Fails + 1)
                return k;
        }
        return -1;
    }

    /// <summary>Any peer: a stage heard: the sound at the part, and words for whoever is aboard.</summary>
    private void BreakdownHeard(ushort body, int part, BreakdownStage stage, int secs, bool say)
    {
        if (Constructs.Get(body) is not { } c || part < 0 || part >= c.PartCount)
            return;
        var at = ConstructRender.PartCentre(c, part);
        if (stage == BreakdownStage.Warning)
        {
            BreakdownSaw(body, part, secs);
            var urgency = 1f - Math.Clamp(secs / MathF.Max(1, Breakdowns.Current.WarnSeconds), 0, 1);
            Audio.Effect3D(Sfx.HullCreak, at, 0.55f + 0.45f * urgency);
        }
        else
        {
            for (var k = 0; k < _bdSeen.Length; k++)
                if (_bdSeen[k].Body == body && _bdSeen[k].Part == part)
                    _bdSeen[k].Body = 0;
            if (stage == BreakdownStage.Failed)
                Audio.Effect3D(Sfx.PartFail, at, 1f);
        }
        if (!say || LocalId == byte.MaxValue || !Players.TryGetValue(LocalId, out var me)
            || me.Sim.RideBody != body && SandboxHands.Hand(LocalId).SeatBody != body)
            return;
        var where = BreakdownWhere(c, part);
        var outside = BreakdownOutside(c, part);
        SandboxSay(stage switch
        {
            BreakdownStage.Warning when outside => $"The {where} is groaning. It hangs outside the hull: see to it with the attach tool at the part, on a space walk or landed, before it gives.",
            BreakdownStage.Warning => $"Something in the {where} is groaning. See to it with the attach tool, at the {BreakdownPanel.ToLowerInvariant()} or the part, before it gives.",
            BreakdownStage.Failed when outside => $"The {where} has failed. Repair it with the attach tool at the part, outside ({BreakdownCost()}).",
            BreakdownStage.Failed => $"The {where} has failed. Repair it with the attach tool at the part ({BreakdownCost()}).",
            BreakdownStage.Repaired => $"The {where} is working again.",
            _ => $"The {where} is quiet again.",
        }, 8);
    }

    /// <summary>
    /// The engine room's panel: it reports every breakdown on its ship (which part, where), and the attach tool at it sees to
    /// a warning on a works piece inside the hull. A pod or a jet hangs outside and is seen to only at the part, and a part
    /// that has failed is repaired at the part (docs/sandbox/design/ship-works.md 7, the panel rule).
    /// </summary>
    public const string BreakdownPanel = "Engineer's Panel";

    private static bool IsBreakdownPanel(Construct c, int part) => c.DefOf(part).Name == BreakdownPanel;

    /// <summary>A part of <paramref name="c"/> a breakdown may pick that has broken down (Broken with its health kept), or -1.</summary>
    private static int BreakdownFailed(Construct c)
    {
        var n = BreakdownCandidates(c);
        for (var k = 0; k < n; k++)
            if (BreakdownCandidate(c, k) is var i and >= 0 && c.Parts[i].Broken && c.Parts[i].Health > 0)
                return i;
        return -1;
    }

    /// <summary>A part a breakdown may pick (a thruster, or a drive's works piece).</summary>
    private static bool BreakdownKind(ConstructPartDef d) => d.Role == PartRole.Thruster || ConstructWorks.IsDrivePiece(d);

    /// <summary>The look line on a groaning or broken-down thruster, or on the engine room's panel while one is (null: nothing to say).</summary>
    public string? BreakdownLook(Construct c, int part, out bool failed)
    {
        // (made again only when what it says changes: the look line is asked for every frame)
        var slot = BreakdownSeenSlot(c);
        var seen = slot >= 0 ? _bdSeen[slot].Part : -1;
        var key = ((long)c.Id << 48) ^ ((long)part << 32) ^ ((long)(seen + 1) << 16) ^ (seen >= 0 ? (int)(_bdSeen[slot].Fails - SandboxNow) : 0) ^ ((long)BreakdownFailed(c) + 1 << 40) ^ (c.Parts.Length > part && part >= 0 && c.Parts[part].Broken ? 1L << 62 : 0);
        if (key != _bdLookKey)
        {
            _bdLookKey = key;
            _bdLookText = BreakdownLookMake(c, part, out _bdLookFailed);
        }
        failed = _bdLookFailed;
        return _bdLookText;
    }

    private long _bdLookKey = -1;
    private string? _bdLookText;
    private bool _bdLookFailed;

    private string? BreakdownLookMake(Construct c, int part, out bool failed)
    {
        failed = false;
        if (part < 0 || part >= c.PartCount)
            return null;
        var slot = BreakdownSeenSlot(c);
        if (IsBreakdownPanel(c, part))
        {
            if (slot >= 0)
            {
                var w = _bdSeen[slot].Part;
                var secs = Math.Max(0, (int)(_bdSeen[slot].Fails - SandboxNow));
                return BreakdownOutside(c, w)
                    ? $"{BreakdownPanel}: the {BreakdownWhere(c, w)} is groaning ({secs} s) - it hangs outside: see to it at the part"
                    : $"{BreakdownPanel}: the {BreakdownWhere(c, w)} is groaning ({secs} s) - {SandboxKeys.Fire}: see to it";
            }
            if (BreakdownFailed(c) is var f and >= 0)
                return $"{BreakdownPanel}: the {BreakdownWhere(c, f)} has broken down - repair it at the part{(BreakdownOutside(c, f) ? ", outside" : "")} ({BreakdownCost()})";
            return null;
        }
        if (WorksWrecked(c, part))
        {
            failed = true;
            return $"{c.DefOf(part).Name}: wrecked - {SandboxKeys.Fire}: mend it ({WorksMendCost(c, part)})";
        }
        if (!BreakdownKind(c.DefOf(part)))
            return null;
        if (c.Parts[part].Broken && c.Parts[part].Health > 0)
        {
            failed = true;
            return $"{c.DefOf(part).Name}: broken down - {SandboxKeys.Fire}: repair ({BreakdownCost()})";
        }
        if (slot >= 0 && _bdSeen[slot].Part == part)
            return $"{c.DefOf(part).Name}: groaning, about to fail ({Math.Max(0, (int)(_bdSeen[slot].Fails - SandboxNow))} s) - {SandboxKeys.Fire}: see to it";
        return null;
    }

    /// <summary>A ship's works piece broken by damage (disabled at zero health, still in place): mended where it stands (docs/sandbox/design/ship-works.md 7).</summary>
    private static bool WorksWrecked(Construct c, int part) =>
        part >= 0 && part < c.PartCount && c.Parts[part].Broken && c.Parts[part].Health <= 0 && c.DefOf(part).WorksKind is not (WorksKind.None or WorksKind.Mount);

    private static string WorksMendCost(Construct c, int part) => ShipWorks.Current.MendCost(c.DefOf(part).Mass) is var n and > 0 ? $"{n} {ShipWorks.Current.MendMaterial}" : "free";

    /// <summary>Server: the attach tool mends a works piece broken by damage: it is paid for by its mass, and comes back whole.</summary>
    private string? WorksMend(Player p, Construct c, int part)
    {
        var t = ShipWorks.Current;
        var d = c.DefOf(part);
        if (SandboxHands.MendPay(Constructs, p.Id, p.Eye, t.MendMaterial, t.MendCost(d.Mass)) is { } why)
            return why;
        ref var pp = ref c.Parts[part];
        pp.Broken = false;
        pp.Health = d.Health;
        c.Dirty = true;
        Constructs.StructureDirty.Add(c.Id);
        c.Wake();
        SandboxHands.Done.Add($"player {p.Id}: mended the {d.Name} on construct {c.Id}");
        Constructs.Events.Add($"construct {c.Id}: {d.Name} mended");
        return null;
    }

    private static string BreakdownCost() => Breakdowns.Current is { RepairCost: > 0 } t ? $"{t.RepairCost} {t.RepairMaterial}" : "free";

    private static readonly string[] BreakdownShips = ["Rust Bucket ", "Magpie ", "Zero Day "]; // (the shipped ships' names, off the front of their parts' names)

    /// <summary>Where a part is on its ship, for people: "port drive", "forward starboard lift jet", "aft drive core".</summary>
    private static string BreakdownWhere(Construct c, int part)
    {
        var d = c.DefOf(part);
        var name = d.Name;
        foreach (var ship in BreakdownShips)
            if (name.StartsWith(ship, StringComparison.Ordinal))
                name = name[ship.Length..];
        if (d.WorksKind != WorksKind.None && name.Length > 2 && name[^2] == ' ' && name[^1] is 'S' or 'M' or 'L')
            name = name[..^2]; // ("Drive Core L": the drive core)
        name = name.ToLowerInvariant();
        var pos = c.Parts[part].Pos - c.Com; // (the nose along -z, port at -x)
        var side = MathF.Abs(pos.X) < 0.5f ? "" : pos.X < 0 ? "port " : "starboard ";
        var end = MathF.Abs(pos.Z) < 1.5f || name.Contains("drive") ? "" : pos.Z < 0 ? "forward " : "aft ";
        return end + side + name;
    }

    /// <summary>Client (or host): the attach tool's Fire at a groaning or broken-down part.</summary>
    public void SandboxSendMend(ushort body, int part)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Mend).U16(body).U16((ushort)part);
        SandboxSendBuilt(w);
    }

    /// <summary>Server: a <see cref="CActKind.Mend"/>: null when it worked, else why not.</summary>
    private string? BreakdownMendAct(Player p, ushort body, int part)
    {
        ref var h = ref SandboxHands.Hand(p.Id);
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Welder)
            return "take out the attach tool to see to it";
        if (Constructs.Get(body) is not { } c || part < 0 || part >= c.PartCount)
            return "that part is gone";
        if (Vector3.Distance(p.Eye, ConstructRender.PartCentre(c, part)) > ConstructBuild.Reach + 2.5f)
            return "too far away";
        if (IsBreakdownPanel(c, part))
        {
            // (from the engine room: a warning on a works piece inside; the rest is reported, and seen to at the part)
            var warn = BreakdownPart(c);
            if (warn >= 0 && BreakdownOutside(c, warn))
                return $"the {BreakdownWhere(c, warn)} hangs outside the hull: see to it at the part, on a space walk or landed";
            if (warn < 0)
                return BreakdownFailed(c) is var f and >= 0 ? $"repair the {BreakdownWhere(c, f)} at the part" : "nothing on this ship needs seeing to";
            part = warn;
        }
        else if (WorksWrecked(c, part))
            return WorksMend(p, c, part);
        else if (!BreakdownKind(c.DefOf(part)))
            return "that part is gone";
        if (BreakdownPart(c) == part)
        {
            c.BdDef = 0;
            SandboxHands.Done.Add($"player {p.Id}: saw to the {BreakdownWhere(c, part)} on construct {c.Id} in time");
            BreakdownSend(c, part, BreakdownStage.Fixed, true);
            return null;
        }
        ref var pp = ref c.Parts[part];
        if (!pp.Broken || pp.Health <= 0)
            return "nothing wrong with it";
        var t = Breakdowns.Current;
        if (SandboxHands.MendPay(Constructs, p.Id, p.Eye, t.RepairMaterial, t.RepairCost) is { } why)
            return why;
        pp.Broken = false;
        c.Dirty = true;
        Constructs.StructureDirty.Add(c.Id);
        c.Wake();
        SandboxHands.Done.Add($"player {p.Id}: repaired the {BreakdownWhere(c, part)} on construct {c.Id}");
        BreakdownSend(c, part, BreakdownStage.Repaired, true);
        return null;
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>A breakdown's repair (Game/GameSandboxBreakdowns.cs): its material, carried or from the beacon's network. Null: paid.</summary>
    public string? MendPay(ConstructWorld world, byte player, Vector3 eye, string material, int cost) => BulkPay(world, player, eye, "Repair", material, cost);
}

public sealed partial class Construct
{
    // Breakdowns (Game/GameSandboxBreakdowns.cs): this ship's own clock and the warning under way, kept on its body (no
    // dictionary that grows with bodies). Not saved: a loaded ship starts its wait again.
    public int BdNext = -1; // crewed ticks to the next warning (-1: not set yet)
    public ushort BdDef; // the part warning: its def and place (0: none), found again by them
    public Vector3 BdPos;
    public int BdLeft; // ticks to its failure
    public int BdCreak; // ticks to its next creak
    public uint BdTick; // the server tick it was last stepped (one step a look, however many are aboard)
}
