using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox: shell storage pods (ConstructShellPod.cs; docs/sandbox/design/shell-pod.md). A player in a robot shell who stands in an
/// empty pod's frame and presses the use key is sent back to their chair (<see cref="ShellReturn"/> with the pod) and the pod keeps
/// the shell; a 3.5 s dock job then folds it in on every peer. A chair whose nearest shell on its channel is in a pod starts a wake
/// job: the frame rises with the shell folded, it unfolds, and at the end the server stands it as its part and wakes the sitter in it
/// (<see cref="ShellEnter"/>). While a shell is docked its pod charges its power cell from the grid and empties a Mining Shell's
/// hopper into its own bin (a Medium pod's Chute out takes it on). Every peer hears each job (<see cref="Msg.ShellPod"/>). The tick
/// walks only the jobs, and twice a second the bodies that have pods: nothing allocates.
/// </summary>
public sealed partial class Game
{
    public const int PodEvery = 30; // ticks between a docked shell's charge and unload steps
    public const int PodChargeStep = 2; // power cell wear taken off a step (0 new, 255 flat: about a minute from flat to full)
    public const int PodUnloadStep = 60; // hopper units into the pod's bin a step (120 a second)

    /// <summary>
    /// Server, F from inside a shell on a shell pod: stood in its frame, the shell goes into the pod and its wearer back to their
    /// chair. False: not a pod, or they aren't in a shell (F as ever). <paramref name="why"/>: a refusal.
    /// </summary>
    private bool ShellPodUse(Player p, ushort body, int part, out string? why)
    {
        why = null;
        if (!_sh[p.Id].On || Constructs.Get(body) is not { } c || (uint)part >= (uint)c.PartCount || c.DefOf(part).ShellPod == null)
            return false;
        var world = Constructs;
        ref var pp = ref c.Parts[part];
        var frame = p.ShellFrame;
        var mid = p.Position + GravityFrames.UpAt(p.Position) * (World.PlayerHeight * ShellFrames.ScaleOf(frame) * 0.5f);
        if (c.Loose)
            why = $"weld the {pp.D.Name} onto a floor, a wall or a ceiling first";
        else if (pp.Broken)
            why = $"the {pp.D.Name} is wrecked";
        else if (ConstructShellPod.Holds(pp))
            why = $"the {pp.D.Name} holds a shell already";
        else if (ConstructShellPod.JobAt(world, c.Id, part) >= 0)
            why = $"the {pp.D.Name} is busy";
        else if (world.PodJobCount >= ConstructShellPod.MaxJobs)
            why = "too many pods at work: wait a moment";
        else if (ConstructShellPod.FitRefusal(pp.D, frame) is { } big)
            why = big;
        else if (!ConstructShellPod.InFrame(c, part, frame, mid))
            why = "stand in the pod's frame first";
        else if (SandboxHands.DockRefusal(c, p.Id) is { } no)
            why = no;
        if (why != null)
            return true;
        var from = p.Position;
        var yaw = p.Yaw;
        ShellReturn(p, lost: false, "The pod folds your shell away. You wake in the chair.", pod: c, podPart: part);
        if (!ConstructShellPod.Holds(c.Parts[part]))
            return true; // (ShellReturn said why: nothing to fold)
        ref var j = ref world.PodJobs[world.PodJobCount++];
        j = new PodJob { Body = c.Id, Part = (ushort)part, Tag = ConstructShellPod.TagIn(c.Parts[part]), Phase = ConstructShellPod.Dock, Frame = frame, Start = ConstructConveyor.Clock, From = from, Yaw = yaw };
        PodSend(j, true, 0, null);
        Log($"[pod] {p.Name} docks a {ShellFrames.Of(frame).Part} in the {pp.D.Name} on body {c.Id}");
        return true;
    }

    /// <summary>Server, from <see cref="ShellReturn"/>: the shell they wore goes into the pod, as worn and damaged, with its hopper's load.</summary>
    private void PodKeep(Player p, Construct pod, int part, ConstructPartDef shellDef, int channel, byte owner)
    {
        ref var hp = ref SandboxHands.Hoppers[p.Id];
        ConstructShellPod.Store(Constructs, pod, part, ShellFrames.FrameOf(shellDef), channel, owner, p.ShellWear, p.ShellMods, p.Health, hp.Material, hp.Amount);
        hp = default;
        (p.HopperMaterial, p.HopperAmount) = (0, 0);
        _boring[p.Id] = false;
    }

    /// <summary>Server, a sitter's link reached a docked shell: the pod starts to wake it (the sitter wakes in it when it stands).</summary>
    private void PodWakeAsk(Player p, Construct chair, Construct pod, int part, double now)
    {
        var world = Constructs;
        var k = ConstructShellPod.JobAt(world, pod.Id, part);
        if (k >= 0)
        {
            if (world.PodJobs[k].Phase == ConstructShellPod.Wake && world.PodJobs[k].Player == p.Id)
                return; // (waking: wait)
            if (now - _sh[p.Id].WhyAt > 4)
            {
                _sh[p.Id].WhyAt = now;
                SandboxHands.Notices.Add((p.Id, $"No link yet: the {pod.DefOf(part).Name} is busy"));
            }
            return;
        }
        if (world.PodJobCount >= ConstructShellPod.MaxJobs)
            return;
        var frame = (byte)ConstructShellPod.FrameIn(pod.Parts[part]);
        ref var j = ref world.PodJobs[world.PodJobCount++];
        j = new PodJob { Body = pod.Id, Part = (ushort)part, Tag = ConstructShellPod.TagIn(pod.Parts[part]), Phase = ConstructShellPod.Wake, Frame = frame, Player = p.Id, Chair = chair.Id, Start = ConstructConveyor.Clock };
        PodSend(j, true, 0, null);
        SandboxHands.Notices.Add((p.Id, $"The {pod.DefOf(part).Name} opens and your shell unfolds..."));
        Log($"[pod] {p.Name} wakes the {ShellFrames.Of(frame).Part} in the {pod.DefOf(part).Name} on body {pod.Id}");
    }

    /// <summary>Server, each Sandbox tick: pods' jobs (walks only them), shells out of pods that went, and twice a second the docked shells' charge and unload.</summary>
    private void PodTick()
    {
        var world = Constructs;
        if (!world.PodAuthority)
            PodTagAll(world); // (the first tick of a world: a shell docked before tags gets one, so a join's copy is known)
        world.PodAuthority = true; // (this world releases docked shells from pods that go: ConstructShellPod.Release)
        if (world.PodReleaseCount > 0)
            ConstructShellPod.StandReleases(world);
        if (world.PodJobCount > 0)
        {
            var now = ConstructConveyor.Clock;
            for (var k = world.PodJobCount - 1; k >= 0; k--)
            {
                ref var j = ref world.PodJobs[k];
                var c = world.Get(j.Body);
                var at = c == null ? -1 : ConstructShellPod.PartOfTag(c, j.Tag, j.Part); // (by its shell's tag: a part taken off elsewhere moves the indices)
                if (at < 0 && j.Tag != 0 && PodMoved(world, j.Tag) is { } moved)
                {
                    // (its pod went to another body: a split, as a broken weld seam makes, gives the piece a new id)
                    PodSend(j, false, 0, null);
                    c = moved;
                    at = ConstructShellPod.PartOfTag(moved, j.Tag, -1);
                    j.Body = moved.Id;
                    j.Part = (ushort)at;
                    PodSend(j, true, now - j.Start, null);
                }
                if (c == null || at < 0 || c.Parts[at].Broken)
                {
                    PodEnd(k);
                    continue;
                }
                if (at != j.Part)
                {
                    PodSend(j, false, 0, null); // (peers drop the job at its old index and hear it at the new one)
                    j.Part = (ushort)at;
                    PodSend(j, true, now - j.Start, null);
                }
                var t = now - j.Start;
                if (j.Phase == ConstructShellPod.Dock && t >= ConstructShellPod.DockSeconds)
                    PodEnd(k);
                else if (j.Phase == ConstructShellPod.Wake && t >= ConstructShellPod.WakeSeconds)
                    PodWoken(k, c);
            }
        }
        if (ServerTick % PodEvery != 0)
            return;
        foreach (var c in world.Bodies)
        {
            if (c.PodCount == 0)
                continue;
            for (var q = 0; q < c.PodCount; q++)
            {
                int i = c.Pods[q];
                if (i >= c.PartCount)
                    continue;
                ref var pp = ref c.Parts[i];
                var d = pp.D;
                if (d.ShellPod == null || !ConstructShellPod.Holds(pp) || ConstructShellPod.JobAt(world, c.Id, i) >= 0)
                    continue;
                if (pp.Broken)
                {
                    ConstructShellPod.Release(world, c, i); // (stands next tick)
                    continue;
                }
                PodCharge(world, c, i, ref pp, d);
                PodUnload(world, c, i, ref pp, d);
            }
        }
    }

    /// <summary>Server, a world's first pod tick: every docked shell without a tag is given one. Once a world: not per tick.</summary>
    private static void PodTagAll(ConstructWorld world)
    {
        foreach (var c in world.Bodies)
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.ShellPod != null && ConstructShellPod.Holds(c.Parts[i]) && ConstructShellPod.TagIn(c.Parts[i]) == 0)
                    ConstructConfig.Put(ref c.Parts[i], c.Parts[i].D, CfgKey.PodTag, Random.Shared.Next(1, 1 << 24));
    }

    /// <summary>Server: a docked shell's power cell charges while its pod has power (its other works are a service's, not a charge's).</summary>
    private static void PodCharge(ConstructWorld world, Construct c, int i, ref CPart pp, ConstructPartDef d)
    {
        ShellCond w = default;
        ShellWear.Read(pp, d, ref w);
        var at = (int)ShellWork.Power;
        if (w[at] == 0 || !ConstructRecycler.Powered(world, c, i))
            return;
        w[at] = (byte)Math.Max(0, w[at] - PodChargeStep);
        ShellWear.Write(ref pp, d, w); // (peers don't need it until it wakes: Msg.Shell carries the wear then)
    }

    /// <summary>Server: a docked Mining Shell's hopper into the pod's own bin, a step at a time (the bin's Chute out takes it on).</summary>
    private static void PodUnload(ConstructWorld world, Construct c, int i, ref CPart pp, ConstructPartDef d)
    {
        var units = (int)ConstructConfig.Get(pp, d, CfgKey.PodOreUnits, 0);
        var ore = (byte)ConstructConfig.Get(pp, d, CfgKey.PodOre, 0);
        if (units <= 0 || ore == 0 || !d.IsBin)
            return;
        var put = world.Deposit(c, i, Math.Min(units, PodUnloadStep), ore);
        if (put <= 0)
            return; // (its bin is full, or holds another material: it waits)
        units -= put;
        ConstructConfig.Put(ref pp, d, CfgKey.PodOreUnits, units);
        if (units == 0)
            ConstructConfig.Put(ref pp, d, CfgKey.PodOre, 0);
    }

    /// <summary>Server: a wake's frame is up and the shell unfolded: it stands and the sitter wakes in it (or, gone from the chair, the pod shuts on it again).</summary>
    private void PodWoken(int k, Construct c)
    {
        var world = Constructs;
        ref var j = ref world.PodJobs[k];
        var part = j.Part;
        Players.TryGetValue(j.Player, out var p);
        var chair = world.Get(j.Chair);
        var seat = p != null && chair != null ? chair.SeatOf(p.Id) : -1;
        var owner = ConstructShellPod.OwnerIn(c.Parts[part]);
        var mayWake = p != null && (owner == 255 || owner == p.Id || Factions.Shares(owner, p.Id))
            && seat >= 0 && ConstructShellPod.ChannelIn(c.Parts[part]) == ConstructShell.ChannelOf(chair!.Parts[seat]); // (asked again at the stand: Find's checks)
        if (p != null && mayWake && seat >= 0 && !chair!.Parts[seat].Broken && !_sh[p.Id].On && ConstructShellPod.Holds(c.Parts[part]) && ConstructShellPod.TagIn(c.Parts[part]) == j.Tag)
        {
            var feet = ConstructShellPod.StandFeet(c, part, (byte)ConstructShellPod.FrameIn(c.Parts[part]), out var fwd, out _);
            if (ConstructShellPod.TakeOut(world, c, part, feet, ConstructShellPod.YawOf(fwd)) is { } stood)
            {
                PodEnd(k);
                ref var h = ref SandboxHands.Hand(p.Id);
                ShellEnter(p, ref h, chair, seat, stood);
                return;
            }
            SandboxHands.Notices.Add((p.Id, "No link: no room for the shell to stand"));
            Log($"[pod] {p.Name}: the shell in body {c.Id}'s pod had no room to stand");
        }
        // (nobody to wake in it now: the frame draws it back in, folded)
        j.Phase = ConstructShellPod.Dock;
        j.Start = ConstructConveyor.Clock - ConstructShellPod.FoldEnd;
        j.From = Vector3.Zero;
        PodSend(j, true, ConstructShellPod.FoldEnd, null);
    }

    /// <summary>Both: job <paramref name="k"/> is over (the server says so to every peer).</summary>
    private void PodEnd(int k)
    {
        var world = Constructs;
        var j = world.PodJobs[k];
        world.PodJobs[k] = world.PodJobs[world.PodJobCount - 1];
        world.PodJobCount--;
        if (IsServer)
            PodSend(j, false, 0, null);
    }

    /// <summary>Server: a job's start or end to everyone (<paramref name="to"/> null) or to one joiner.</summary>
    private void PodSend(in PodJob j, bool on, double run, RakNetGUID? to)
    {
        if (Net.IsOffline)
            return;
        var w = SbW;
        w.Begin(Msg.ShellPod).U16(j.Body).U16(j.Part).U8(on ? (byte)1 : (byte)0);
        if (on)
            w.U8(j.Phase).U8(j.Frame).U8(j.Player).Vec(j.From).F32(j.Yaw).F32((float)run);
        if (to is { } g)
            Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
        else
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: every pod at work, to a joiner (from <see cref="SendSandboxState"/>).</summary>
    private void PodSendAll(RakNetGUID to)
    {
        var now = ConstructConveyor.Clock;
        for (var k = 0; k < Constructs.PodJobCount; k++)
            PodSend(Constructs.PodJobs[k], true, now - Constructs.PodJobs[k].Start, to);
    }

    /// <summary>Client: a <see cref="Msg.ShellPod"/> (after its id).</summary>
    private void ReadShellPod(ref PacketReader r)
    {
        var world = Constructs;
        var body = r.U16();
        var part = r.U16();
        var on = r.U8() != 0;
        var k = ConstructShellPod.JobAt(world, body, part);
        if (!on)
        {
            if (k >= 0)
            {
                world.PodJobs[k] = world.PodJobs[world.PodJobCount - 1];
                world.PodJobCount--;
            }
            return;
        }
        var j = new PodJob { Body = body, Part = part, Phase = r.U8(), Frame = r.U8(), Player = r.U8(), From = r.Vec(), Yaw = r.F32() };
        j.Start = ConstructConveyor.Clock - Math.Clamp(r.F32(), 0, ConstructShellPod.DockSeconds + ConstructShellPod.WakeSeconds);
        if (j.Phase is not (ConstructShellPod.Dock or ConstructShellPod.Wake) || j.Frame >= ShellFrames.All.Length)
            return;
        if (k < 0)
        {
            if (world.PodJobCount >= ConstructShellPod.MaxJobs)
                return;
            k = world.PodJobCount++;
        }
        world.PodJobs[k] = j;
    }

    /// <summary>The body whose pod holds the docked shell with <paramref name="tag"/> (a pod job whose body lost it), or null.</summary>
    private static Construct? PodMoved(ConstructWorld world, int tag)
    {
        foreach (var b in world.Bodies)
            if (b.PodCount > 0 || b.Dirty)
                if (ConstructShellPod.PartOfTag(b, tag, -1) >= 0)
                    return b;
        return null;
    }
}
