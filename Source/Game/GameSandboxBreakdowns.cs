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
/// Breakdowns that warn you first, on the rust bucket (the roadmap's first step, Karl 2026-11-15; docs/sandbox/constructs/ship.md
/// "Breakdowns"; the numbers are Constructs/Breakdowns.json). Survival only, on a ship start's own ship, and its clock runs only
/// while somebody is aboard with the game running. Every so often one of the ship's thrusters starts to go: it creaks and
/// groans, louder and closer together, for <see cref="Breakdowns.WarnSeconds"/>. The attach tool's Fire at it, or at the
/// engine room's <see cref="BreakdownPanel"/> (the thrusters hang outside the hull, out of reach in orbit), sees to it in that
/// time for nothing. Left alone, it fails with a bang: it stops working (Broken, with its health kept, which is how a
/// breakdown is told from a part shot to pieces, even in a save) until the attach tool repairs it for a little metal.
/// Server-side state is the one warning under way; clients hear each creak as a <see cref="CActKind.Breakdown"/> and read a
/// failed part from its body (protocol 319).
/// </summary>
public sealed partial class Game
{
    private const int BdStep = 15; // ticks between looks (4 a second)

    private int _bdNext = -1; // crewed ticks to the next warning (-1: not set yet)
    private ushort _bdDef; // the part warning: its def and place (0: none), found again by them
    private Vector3 _bdPos;
    private int _bdLeft; // ticks to its failure
    private int _bdCreak; // ticks to its next creak
    private readonly Random _bdRng = new(5309);

    /// <summary>Server, each tick: the start ship's breakdowns.</summary>
    private void BreakdownTick()
    {
        if (ServerTick % BdStep != 0 || _opShip == 0 || !SandboxSurvivalOption)
            return;
        var t = Breakdowns.Current;
        if (t.Problem.Length > 0 || Constructs.Get(_opShip) is not { PartCount: > 0 } c || !Crewed(c))
            return; // (nothing happens with nobody aboard)
        if (_bdNext < 0)
            _bdNext = (int)(t.FirstMinutes * 60 * World.TickRate);
        if (_bdDef == 0)
        {
            _bdNext -= BdStep;
            if (_bdNext <= 0)
                BreakdownStart(c, t);
            return;
        }
        var part = BreakdownPart(c);
        if (part < 0 || !c.Parts[part].Working)
        {
            _bdDef = 0; // (it went some other way: shot off, removed)
            return;
        }
        _bdLeft -= BdStep;
        if (_bdLeft <= 0)
        {
            BreakdownFail(c, part);
            return;
        }
        _bdCreak -= BdStep;
        if (_bdCreak <= 0)
        {
            // Closer together as the end nears: every 7 s at first, every 2 s at the last.
            var share = _bdLeft / (t.WarnSeconds * World.TickRate);
            _bdCreak = (int)((2f + 5f * share) * World.TickRate * (0.8f + 0.4f * (float)_bdRng.NextDouble()));
            BreakdownSend(c, part, BreakdownStage.Warning, false);
        }
    }

    /// <summary>Someone alive is aboard <paramref name="c"/>, standing in it or seated.</summary>
    private bool Crewed(Construct c)
    {
        foreach (var p in Players.Values)
            if (p.Alive && (p.Sim.RideBody == c.Id || SandboxHands.Hand(p.Id).SeatBody == c.Id))
                return true;
        return false;
    }

    private void BreakdownStart(Construct c, Breakdowns t)
    {
        _bdNext = (int)((t.MinMinutes + (t.MaxMinutes - t.MinMinutes) * (float)_bdRng.NextDouble()) * 60 * World.TickRate);
        int working = 0, failed = 0;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var p = ref c.Parts[c.Thrusters[k].Part];
            if (p.Working)
                working++;
            else if (p.Health > 0)
                failed++;
        }
        if (working == 0 || failed >= t.MaxFailed)
            return; // (enough is broken already: the next try is a full wait away)
        var pick = _bdRng.Next(working);
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            var i = c.Thrusters[k].Part;
            if (!c.Parts[i].Working || pick-- > 0)
                continue;
            _bdDef = c.Parts[i].Def;
            _bdPos = c.Parts[i].Pos;
            _bdLeft = (int)(t.WarnSeconds * World.TickRate);
            _bdCreak = 7 * World.TickRate;
            Log($"[breakdown] construct {c.Id}: the {BreakdownWhere(c, i)} starts to go ({t.WarnSeconds:0} s)");
            BreakdownSend(c, i, BreakdownStage.Warning, true);
            return;
        }
    }

    private void BreakdownFail(Construct c, int part)
    {
        _bdDef = 0;
        ref var p = ref c.Parts[part];
        p.Broken = true; // (its health stays: a breakdown, not a wreck)
        c.Dirty = true;
        Constructs.StructureDirty.Add(c.Id);
        c.Wake();
        Constructs.Events.Add($"construct {c.Id}: {p.D.Name} broke down (stops working)");
        BreakdownSend(c, part, BreakdownStage.Failed, true);
    }

    /// <summary>The part warning now, by its def and place (parts are renumbered when others go), or -1.</summary>
    private int BreakdownPart(Construct c)
    {
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            var i = c.Thrusters[k].Part;
            if (c.Parts[i].Def == _bdDef && Vector3.DistanceSquared(c.Parts[i].Pos, _bdPos) < 1e-4f)
                return i;
        }
        return -1;
    }

    /// <summary>Server: a stage to every peer (the host's own handled here).</summary>
    private void BreakdownSend(Construct c, int part, BreakdownStage stage, bool say)
    {
        var secs = (byte)Math.Clamp(_bdLeft / World.TickRate, 0, 255);
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

    /// <summary>This peer's view of the warning under way (the look line's): its body and part, and when it was last heard.</summary>
    private ushort _bdSeenBody;
    private int _bdSeenPart = -1;
    private double _bdSeenAt, _bdSeenFails;

    /// <summary>Any peer: a stage heard: the sound at the part, and words for whoever is aboard.</summary>
    private void BreakdownHeard(ushort body, int part, BreakdownStage stage, int secs, bool say)
    {
        if (Constructs.Get(body) is not { } c || part < 0 || part >= c.PartCount)
            return;
        var at = ConstructRender.PartCentre(c, part);
        if (stage == BreakdownStage.Warning)
        {
            (_bdSeenBody, _bdSeenPart, _bdSeenAt, _bdSeenFails) = (body, part, SandboxNow, SandboxNow + secs);
            var urgency = 1f - Math.Clamp(secs / MathF.Max(1, Breakdowns.Current.WarnSeconds), 0, 1);
            Audio.Effect3D(Sfx.HullCreak, at, 0.55f + 0.45f * urgency);
        }
        else
        {
            if (_bdSeenBody == body && _bdSeenPart == part)
                _bdSeenPart = -1;
            if (stage == BreakdownStage.Failed)
                Audio.Effect3D(Sfx.PartFail, at, 1f);
        }
        if (!say || LocalId == byte.MaxValue || !Players.TryGetValue(LocalId, out var me)
            || me.Sim.RideBody != body && SandboxHands.Hand(LocalId).SeatBody != body)
            return;
        var where = BreakdownWhere(c, part);
        SandboxSay(stage switch
        {
            BreakdownStage.Warning => $"Something in the {where} is groaning. See to it with the attach tool, at the {BreakdownPanel.ToLowerInvariant()} or the part, before it gives.",
            BreakdownStage.Failed => $"The {where} has failed. Repair it with the attach tool at the {BreakdownPanel.ToLowerInvariant()} ({BreakdownCost()}).",
            BreakdownStage.Repaired => $"The {where} is working again.",
            _ => $"The {where} is quiet again.",
        }, 8);
    }

    /// <summary>
    /// The part the engine room's panel works on: the rust bucket's thrusters hang outside the hull, out of reach in space, so
    /// the attach tool at this panel sees to them from inside (the ship's generator keeps one in the engine room).
    /// </summary>
    public const string BreakdownPanel = "Engineer's Panel";

    private static bool IsBreakdownPanel(Construct c, int part) => c.DefOf(part).Name == BreakdownPanel;

    /// <summary>A thruster of <paramref name="c"/> that has broken down (Broken with its health kept), or -1.</summary>
    private static int BreakdownFailed(Construct c)
    {
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            var i = c.Thrusters[k].Part;
            if (c.Parts[i].Broken && c.Parts[i].Health > 0)
                return i;
        }
        return -1;
    }

    private bool BreakdownSeen(Construct c, int part) =>
        _bdSeenBody == c.Id && _bdSeenPart >= 0 && _bdSeenPart < c.PartCount && (part < 0 || _bdSeenPart == part) && SandboxNow - _bdSeenAt < 10 && SandboxNow < _bdSeenFails + 1;

    /// <summary>The look line on a groaning or broken-down thruster, or on the engine room's panel while one is (null: nothing to say).</summary>
    public string? BreakdownLook(Construct c, int part, out bool failed)
    {
        failed = false;
        if (part < 0 || part >= c.PartCount)
            return null;
        if (IsBreakdownPanel(c, part))
        {
            if (BreakdownSeen(c, -1))
                return $"{BreakdownPanel}: the {BreakdownWhere(c, _bdSeenPart)} is groaning ({Math.Max(0, (int)(_bdSeenFails - SandboxNow))} s) - {SandboxKeys.Fire}: see to it";
            if (BreakdownFailed(c) is var f and >= 0)
            {
                failed = true;
                return $"{BreakdownPanel}: the {BreakdownWhere(c, f)} has broken down - {SandboxKeys.Fire}: repair ({BreakdownCost()})";
            }
            return null;
        }
        if (c.DefOf(part).Role != PartRole.Thruster)
            return null;
        if (c.Parts[part].Broken && c.Parts[part].Health > 0)
        {
            failed = true;
            return $"{c.DefOf(part).Name}: broken down - {SandboxKeys.Fire}: repair ({BreakdownCost()})";
        }
        if (BreakdownSeen(c, part))
            return $"{c.DefOf(part).Name}: groaning, about to fail ({Math.Max(0, (int)(_bdSeenFails - SandboxNow))} s) - {SandboxKeys.Fire}: see to it";
        return null;
    }

    private static string BreakdownCost() => Breakdowns.Current is { RepairCost: > 0 } t ? $"{t.RepairCost} {t.RepairMaterial}" : "free";

    /// <summary>Where a part is on its ship, for people: "port drive", "forward starboard lift jet".</summary>
    private static string BreakdownWhere(Construct c, int part)
    {
        var name = c.DefOf(part).Name;
        if (name.StartsWith("Rust Bucket ", StringComparison.Ordinal))
            name = name["Rust Bucket ".Length..];
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
            // (from the engine room: the warning under way, else the first part broken down)
            part = _bdDef != 0 && body == _opShip ? BreakdownPart(c) : -1;
            if (part < 0)
                part = BreakdownFailed(c);
            if (part < 0)
                return "nothing on this ship needs seeing to";
        }
        else if (c.DefOf(part).Role != PartRole.Thruster)
            return "that part is gone";
        if (_bdDef != 0 && body == _opShip && BreakdownPart(c) == part)
        {
            _bdDef = 0;
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
