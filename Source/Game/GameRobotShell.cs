using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Robot shells in the game (docs/sandbox/CONSTRUCTS.md, "Robot shells"; Karl, 2026-11-08): a player who sits in a Shell
/// Control Chair for <see cref="ShellLinkSeconds"/> wakes in the nearest free Robot Shell on its channel (ConstructShell.cs).
/// The player is moved onto the shell and the shell's construct is taken away while they wear it, so the shell is played
/// exactly as a player is (every tool, seat and step of the movement code, predicted on the client as ever). What their own
/// body had waits here: its place in the chair, health, hands and backpack; the shell starts with empty hands and an empty pack.
/// Leaving (the Esc menu, <see cref="CActKind.ShellLeave"/>), losing the link (out of range, the chair gone or wrecked) or
/// leaving the game stands the shell where it is again and puts them back in the chair. A hit that would kill them only
/// destroys the shell (<see cref="ShellTakesDeath"/>): they wake in the chair with everything their body had, and what the
/// shell carried drops where it fell. Every peer knows who wears a shell (<see cref="Msg.Shell"/>), and draws them as a robot.
/// Server state is a fixed array by player id: nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    public const double ShellLinkSeconds = 2.0; // s seated before the link wakes them in a shell
    public const int ShellCheckEvery = 10; // ticks between checks of a worn shell's link

    private struct ShellRun
    {
        public bool On; // wearing a shell now
        public ushort Chair; // the chair's construct (its seat is the one they still hold: SeatOf)
        public Vector3 BodyAt; // where the body was when it sat (the chair gone: they wake here)
        public float Yaw, Pitch;
        public int Health;
        public SandboxHand Hand; // the body's hands, seat and all
        public CStore? Pack; // the body's backpack (the shell wears a fresh one meanwhile)
        public ConstructPartDef? ShellDef; // the shell's part, its channel and owner: it stands again as this
        public int Channel;
        public byte Owner;
        public double SatAt, WhyAt; // seated in a chair since (0: not); when the last refusal was said
        public bool Spent; // this sitting has linked once (or came back from a shell): stand up to link again
    }

    private readonly ShellRun[] _sh = new ShellRun[256];

    /// <summary>Is this PC's own player wearing a shell now (every peer hears it: <see cref="Msg.Shell"/>)?</summary>
    public bool ShellLocal => Local is { InShell: true };

    /// <summary>Phase 2's hook (GameDepot.cs): a chair the story owns may stand a shell for this sitter before the search.</summary>
    partial void ShellWanted(Player p, Construct chair, int seat);

    /// <summary>...and hears when a player wakes in a shell or comes back from one (lost: destroyed).</summary>
    partial void ShellWoke(Player p, Construct shell);

    partial void ShellBack(Player p, bool lost);

    // ---- Server -----------------------------------------------------------------------------------------------------

    /// <summary>Server, a new match (from <see cref="OpeningReset"/>): nobody wears a shell.</summary>
    private void ShellResetAll()
    {
        Array.Clear(_sh);
        foreach (var p in Players.Values)
            p.InShell = false;
    }

    /// <summary>Server, a sitter's tick in a shell chair (from the seated loop): after the link-up, into the nearest free shell.</summary>
    private void SandboxShellChair(Player p, ref SandboxHand h, Construct c, int seat, double now)
    {
        ref var run = ref _sh[p.Id];
        if (run.On || run.Spent || OpeningHas(p.Id))
            return;
        if (run.SatAt <= 0)
        {
            run.SatAt = now;
            SandboxHands.Notices.Add((p.Id, "Linking to a robot shell..."));
            return;
        }
        if (now - run.SatAt < ShellLinkSeconds)
            return;
        ShellWanted(p, c, seat);
        if (ConstructShell.Find(Constructs, c, seat, p.Id, out var why) is not { } shell)
        {
            if (now - run.WhyAt > 4)
            {
                run.WhyAt = now;
                SandboxHands.Notices.Add((p.Id, $"No link: {why}"));
            }
            return;
        }
        ShellEnter(p, ref h, c, seat, shell);
    }

    /// <summary>Server: out of a seat (or never in one): the chair's link-up starts again next time they sit.</summary>
    private void ShellNotSeated(byte id)
    {
        ref var run = ref _sh[id];
        if (run.On)
            return;
        run.SatAt = 0;
        run.Spent = false;
    }

    /// <summary>Server: the player wakes in the shell. Their body's state is kept, the shell's construct goes, and they stand where it stood.</summary>
    private void ShellEnter(Player p, ref SandboxHand h, Construct chair, int seat, Construct shell)
    {
        ref var run = ref _sh[p.Id];
        if (h.RemoteBody != 0)
            SandboxRemoteRelease(ref h, p.Id);
        SandboxHands.Release(Constructs, p.Id);
        ref var sp = ref shell.Parts[0];
        run.On = true;
        run.Spent = true;
        run.Chair = chair.Id;
        run.BodyAt = p.Position;
        run.Yaw = p.Yaw;
        run.Pitch = p.Pitch;
        run.Health = p.Health;
        run.Hand = h;
        run.ShellDef = sp.D;
        run.Channel = ConstructShell.ChannelOf(sp);
        run.Owner = shell.Owner;
        var pack = SandboxHands.Packs[p.Id];
        var fresh = new CStore { Handle = pack.Handle, Live = pack.Live };
        fresh.Reset(pack.SlotCount);
        run.Pack = pack;
        SandboxHands.Packs[p.Id] = fresh;
        SandboxHands.PackDirty.Add(p.Id);
        var tool = h.Tool;
        h = SandboxHand.Empty;
        h.Tool = tool == ConstructTool.None ? ConstructTool.Hands : tool;
        var feet = shell.ToWorld(Vector3.Zero) + GravityFrames.UpAt(shell.WorldCom) * 0.03f;
        var fwd = Vector3.Transform(-Vector3.UnitZ, shell.Rot * Rot24.Quat(sp.Rot));
        ShellWoke(p, shell);
        FirstReached(p, Milestone.Shell); // (GameMilestones.cs)
        Constructs.Remove(shell);
        p.Teleport(feet);
        p.Yaw = MathF.Atan2(fwd.X, -fwd.Z);
        p.Pitch = 0;
        p.Health = World.MaxHealth;
        p.InShell = true;
        ShellBroadcast(p);
        SandboxHands.Notices.Add((p.Id, "You wake in the robot shell. Your body is safe in the chair: Esc menu > Leave the shell."));
        Log($"[shell] {p.Name} wakes in a shell (chair {chair.Id}, channel {run.Channel})");
    }

    /// <summary>
    /// Server: back to the body. Lost: the shell was destroyed (nothing stands again); otherwise it stands where they were.
    /// Whatever the shell carried is set down where it stands (or fell). <paramref name="why"/> is said to them.
    /// </summary>
    private void ShellReturn(Player p, bool lost, string why)
    {
        ref var run = ref _sh[p.Id];
        if (!run.On)
            return;
        ref var h = ref SandboxHands.Hand(p.Id);
        var feet = p.Position;
        var yaw = p.Yaw;
        if (h.RemoteBody != 0)
            SandboxRemoteRelease(ref h, p.Id);
        SandboxHands.Release(Constructs, p.Id);
        if (h.Seated && SandboxHands.Stand(Constructs, p.Id) is { } off)
            feet = off; // (out of the lander's seat: the shell stands beside it)
        if (h.Held != ConstructParts.None)
            SandboxHands.Drop(Constructs, p.Id, feet, yaw);
        if (SandboxHands.Packs[p.Id].Items > 0)
            SandboxHands.DropPack(Constructs, p.Id, feet, yaw);
        if (!lost && run.ShellDef != null && ConstructBuild.PlaceNew(Constructs, run.ShellDef, feet, yaw) is { } stood)
        {
            ConstructConfig.Put(ref stood.Parts[0], run.ShellDef, CfgKey.Channel, run.Channel);
            stood.Owner = run.Owner;
            stood.Frozen = true; // (it stands as it was left until someone moves it)
            stood.Rebuild();
            Constructs.StructureDirty.Add(stood.Id);
        }
        if (run.Pack != null)
        {
            SandboxHands.Packs[p.Id] = run.Pack;
            SandboxHands.PackDirty.Add(p.Id);
        }
        h = run.Hand;
        h.RemoteBody = 0;
        h.GrabBody = 0;
        h.Edit = EditKind.None;
        var chair = Constructs.Get(run.Chair);
        var seat = chair?.SeatOf(p.Id) ?? -1;
        if (chair != null && seat >= 0 && !chair.Parts[seat].Broken)
        {
            h.SeatBody = chair.Id;
            h.SeatPart = (ushort)seat;
            p.Teleport(ConstructEdits.SeatFeet(chair, seat));
        }
        else
        {
            if (chair != null && seat >= 0)
                chair.Parts[seat].SitterPlus1 = 0;
            h.SeatBody = 0;
            p.Teleport(run.BodyAt);
        }
        p.Yaw = run.Yaw;
        p.Pitch = run.Pitch;
        p.Health = Math.Clamp(run.Health, 1, World.MaxHealth);
        p.InShell = false;
        run.On = false;
        run.Spent = true; // (still in the chair: they stand up before it links again)
        run.Pack = null;
        ShellBroadcast(p);
        SandboxHands.Notices.Add((p.Id, why));
        Log($"[shell] {p.Name} back in their body ({(lost ? "shell destroyed" : "shell left standing")}): {why}");
        ShellBack(p, lost);
    }

    /// <summary>Server, each tick: a worn shell keeps its link (the chair still there, whole and in range), checked every few ticks.</summary>
    private void ShellTick()
    {
        if (ServerTick % ShellCheckEvery != 0)
            return;
        foreach (var p in Players.Values)
        {
            ref var run = ref _sh[p.Id];
            if (!run.On)
                continue;
            var chair = Constructs.Get(run.Chair);
            var seat = chair?.SeatOf(p.Id) ?? -1;
            if (chair == null || seat < 0 || chair.Parts[seat].Broken)
            {
                ShellReturn(p, lost: false, "The link dropped: the chair is gone. You wake where your body was.");
                continue;
            }
            var range = chair.Parts[seat].D.ShellRange;
            if (Vector3.DistanceSquared(p.Position, chair.WorldCom) > range * range)
                ShellReturn(p, lost: false, $"The link dropped: the shell went more than {range / 1000:0.#} km from the chair.");
        }
    }

    /// <summary>Server, from <see cref="DamagePlayer"/>: a hit that would kill someone in a shell destroys the shell instead. True: they are back in their body.</summary>
    private bool ShellTakesDeath(Player victim)
    {
        if (!_sh[victim.Id].On)
            return false;
        ShellReturn(victim, lost: true, "Your robot shell was destroyed. You wake in the chair, unhurt.");
        return true;
    }

    /// <summary>Server: they asked to leave the shell (the Esc menu).</summary>
    private string? ShellLeaveAct(Player p)
    {
        if (!_sh[p.Id].On)
            return "you aren't in a robot shell";
        ShellReturn(p, lost: false, "You leave the shell standing and wake in the chair.");
        return null;
    }

    /// <summary>Server: a player leaves the game: back in their body first, so their record is the body's and the shell stands.</summary>
    internal void ShellLeaveGame(Player p) => ShellReturn(p, lost: false, "");

    /// <summary>Server, a world save's record of someone in a shell: their body's place, health, hands and backpack.</summary>
    private void ShellCatch(Player p, WorldPlayer rec)
    {
        ref var run = ref _sh[p.Id];
        if (!run.On)
            return;
        var chair = Constructs.Get(run.Chair);
        var seat = chair?.SeatOf(p.Id) ?? -1;
        rec.Pos = chair != null && seat >= 0 ? ConstructEdits.SeatFeet(chair, seat) : run.BodyAt;
        rec.Yaw = run.Yaw;
        rec.Pitch = run.Pitch;
        rec.Health = run.Health;
        rec.HasPlace = true;
        (rec.RideBody, rec.SpaceSeatBody, rec.SpaceSeatPart) = (0, 0, 0);
        rec.Tool = run.Hand.Tool;
        rec.Held = run.Hand.Held;
        if (run.Pack != null)
            WorldStores.Copy(run.Pack, rec.Pack);
    }

    // ---- The wire ---------------------------------------------------------------------------------------------------

    /// <summary>Server: who wears a shell, to everyone (<see cref="Msg.Shell"/>: u8 player, u8 on).</summary>
    private void ShellBroadcast(Player p)
    {
        if (Net.IsOffline)
            return;
        var w = SbW;
        w.Begin(Msg.Shell).U8(p.Id).U8(p.InShell ? (byte)1 : (byte)0);
        Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: everyone wearing a shell, to one joiner (from <see cref="SendSandboxState"/>).</summary>
    private void ShellSendAll(RakNetGUID to)
    {
        foreach (var p in Players.Values)
            if (p.InShell)
            {
                var w = SbW;
                w.Begin(Msg.Shell).U8(p.Id).U8(1);
                Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
            }
    }

    /// <summary>Client: a <see cref="Msg.Shell"/> (after its id).</summary>
    private void ReadShell(ref PacketReader r)
    {
        var id = r.U8();
        var on = r.U8() != 0;
        if (Players.TryGetValue(id, out var p))
            p.InShell = on;
    }

    /// <summary>The Esc menu's button: leave the shell and wake in the chair.</summary>
    public void ShellAskLeave() => SandboxSend(CActKind.ShellLeave);

    private SbText _sbShell;

    /// <summary>The HUD's line while this PC's player wears a shell.</summary>
    private void ShellHud(Player me, List<HudLine> lines, Vector4 orange, Vector4 dim)
    {
        if (!me.InShell)
            return;
        if (_sbShell.Stale(me.Health))
            _sbShell.Text = $"ROBOT SHELL  {me.Health}%  -  your body waits in the chair (Esc menu: leave the shell)";
        lines.Add(new HudLine(_sbShell.Text, orange));
    }
}
