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
        public ShellWearAcc Acc; // wear not yet a whole step, per work (GameShellWear.cs)
        public double FaultAt; // when a work's fault was last said to them
        public ShellCond Sent; // the wear peers last heard (a 5 % step sends it again)
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
        Array.Clear(SandboxHands.Hoppers);
        foreach (var p in Players.Values)
        {
            p.InShell = false;
            p.ShellChair = p.ShellSeat = 0;
        }
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
        if (now - run.SatAt < ShellLinkSeconds || ServerTick % ShellCheckEvery != 0)
            return; // (the search looks through every ship in reach: a few times a second, not every tick)
        ShellWanted(p, c, seat);
        if (ConstructShell.Find(Constructs, c, seat, p.Id, out var shellPart, out var why) is not { } shell)
        {
            if (now - run.WhyAt > 4)
            {
                run.WhyAt = now;
                SandboxHands.Notices.Add((p.Id, $"No link: {why}"));
            }
            return;
        }
        if (p.CrewSlot < 0 && PowerHolding(shell) is var power and not 0)
        {
            if (now - run.WhyAt > 4)
            {
                run.WhyAt = now;
                SandboxHands.Notices.Add((p.Id, $"No link: that shell is the {PowerPossessive(power)}, worked by their crew")); // (GameShipControl.cs)
            }
            return;
        }
        if (shell.DefOf(shellPart).ShellPod != null)
        {
            PodWakeAsk(p, c, shell, shellPart, now); // (a shell docked in a pod: it unfolds first, GameShellPod.cs)
            return;
        }
        if (shell.PartCount > 1)
        {
            // A shell on a Robot Shell Dock (Karl, 2026-11-17): the dock unclamps it, and it is woken as one standing alone.
            var marks = new bool[shell.PartCount];
            marks[shellPart] = true;
            var at = ConstructRender.PartCentre(shell, shellPart);
            if (Constructs.CutOut(shell, marks, shell.Owner) is not { } piece)
            {
                SandboxHands.Notices.Add((p.Id, "No link: the world is full"));
                return;
            }
            DockSound(false, at);
            Log($"[shell] a docked shell unclamps from construct {shell.Id} (-> {piece.Id})");
            shell = piece;
            seat = c.SeatOf(p.Id); // (the chair may be on the same ship: its parts moved up)
            if (seat < 0)
            {
                Log($"[shell] {p.Name}: the chair lost its seat as the shell came off its dock");
                return;
            }
            h.SeatPart = (ushort)seat;
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
    private void ShellEnter(Player p, ref SandboxHand h, Construct chair, int seat, Construct shell, bool crew = false)
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
        ShellWear.Read(sp, sp.D, ref p.ShellWear); // (its works' wear goes with them: GameShellWear.cs)
        p.ShellFrame = ShellFrames.FrameOf(sp.D); // (its frame: size, pace, modules; Constructs/Shells.json)
        ShellFrames.ReadKit(sp, sp.D, p.ShellMods); // (and its own modules, as a Shell Gantry fitted them)
        ShellFrames.Refit(p);
        p.Sim.Frame = p.ShellFrame;
        HopperOn(p, shell); // (an ore hopper's load goes with them: GameShellHopper.cs)
        var shellHp = ShellWear.HealthOf(sp, sp.D); // (as damaged as it was left: shells don't heal, Health.cs)
        run.Acc = default;
        run.Sent = p.ShellWear;
        var pack = SandboxHands.Packs[p.Id];
        var fresh = new CStore { Handle = pack.Handle, Live = pack.Live };
        fresh.Reset(pack.SlotCount);
        run.Pack = pack;
        SandboxHands.Packs[p.Id] = fresh;
        SandboxHands.PackDirty.Add(p.Id);
        var tool = h.Tool;
        h = SandboxHand.Empty;
        tool = tool == ConstructTool.None ? ConstructTool.Hands : tool;
        h.Tool = ShellFrames.Worn(p).Allows(tool) ? tool : ShellFrames.Worn(p).ForcedTool ?? tool; // (a frame's only tools: the Mining Shell's drill arm, a tool arm the gantry fitted)
        var feet = shell.ToWorld(sp.Pos) + GravityFrames.UpAt(shell.WorldCom) * 0.03f; // (its part's feet: a shell cut off a dock keeps its place in the ship's frame)
        var fwd = Vector3.Transform(-Vector3.UnitZ, shell.Rot * Rot24.Quat(sp.Rot));
        ShellWoke(p, shell);
        var firstShell = !crew && !HasFirst(p.Id, Milestone.Shell);
        if (!crew)
            FirstReached(p, Milestone.Shell); // (GameMilestones.cs; a crew miner's body has none: GameCrewMine.cs)
        var kit = firstShell && ShellSparesKit(p); // (a few spare works for the first shell: GameShellWear.cs)
        Constructs.Remove(shell);
        p.Teleport(feet);
        p.Yaw = MathF.Atan2(fwd.X, -fwd.Z);
        p.Pitch = 0;
        p.Health = shellHp;
        p.InShell = true;
        (p.ShellChair, p.ShellSeat) = (chair.Id, (ushort)seat); // (their body is drawn sitting there: ShellBodyOf)
        ShellBroadcast(p);
        if (!crew)
            SandboxHands.Notices.Add((p.Id, kit ? "You wake in the robot shell, a pouch of shell spares in your inventory (fit them with the attach tool). Esc menu > Leave the shell."
            : "You wake in the robot shell. Your body is safe in the chair: Esc menu > Leave the shell."));
        Log($"[shell] {p.Name} wakes in a shell (chair {chair.Id}, channel {run.Channel})");
    }

    /// <summary>
    /// Server: back to the body. Lost: the shell was destroyed (nothing stands again); otherwise it stands where they were.
    /// Whatever the shell carried is set down where it stands (or fell). <paramref name="why"/> is said to them.
    /// </summary>
    private void ShellReturn(Player p, bool lost, string why, Construct? dock = null, int dockPart = -1, Construct? pod = null, int podPart = -1)
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
        var standing = false;
        if (!lost && pod != null && run.ShellDef != null && (uint)podPart < (uint)pod.PartCount)
        {
            standing = true; // (into a shell pod: kept in its settings, no part: GameShellPod.cs)
            PodKeep(p, pod, podPart, run.ShellDef, run.Channel, run.Owner);
        }
        else if (!lost && run.ShellDef != null && ConstructBuild.PlaceNew(Constructs, run.ShellDef, feet, yaw) is { } stood)
        {
            standing = true;
            ConstructConfig.Put(ref stood.Parts[0], run.ShellDef, CfgKey.Channel, run.Channel);
            ShellWear.Write(ref stood.Parts[0], run.ShellDef, p.ShellWear); // (it stands as worn as they left it)
            ShellFrames.WriteKit(ref stood.Parts[0], run.ShellDef, p.ShellMods); // (with the modules it was woken with)
            ShellWear.PutHealth(ref stood.Parts[0], run.ShellDef, p.Health); // (and as damaged)
            stood.Owner = run.Owner;
            stood.Frozen = dock == null; // (it stands as it was left until someone moves it; one for a dock is clamped on first)
            stood.Rebuild();
            HopperOff(p, stood, feet); // (the hopper's load back into the part's bin: GameShellHopper.cs)
            Constructs.StructureDirty.Add(stood.Id);
            if (dock != null && SandboxHands.ClipOnDock(Constructs, p.Id, dock, dockPart, stood) is { } stuck)
            {
                why = $"The dock couldn't clamp the shell ({stuck}): it stands where you left it. You wake in the chair.";
                stood.Frozen = true;
                stood.Rebuild();
            }
        }
        if (!standing)
            HopperOff(p, null, feet); // (destroyed, or no room to stand it: the load spills where it fell)
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
        p.ShellChair = p.ShellSeat = 0;
        p.ShellWear = default;
        p.ShellFrame = 0;
        Array.Clear(p.ShellMods);
        ShellFrames.Refit(p);
        p.Sim.Frame = 0;
        run.On = false;
        run.Spent = true; // (still in the chair: they stand up before it links again)
        run.Pack = null;
        ShellBroadcast(p);
        SandboxHands.Notices.Add((p.Id, why));
        Log($"[shell] {p.Name} back in their body ({(lost ? "shell destroyed" : dock != null ? "shell to its dock" : pod != null ? "shell into a pod" : "shell left standing")}): {why}");
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
            HopperSync(p); // (its ore hopper's load to every peer: GameShellHopper.cs)
            ShellForceTool(p); // (a frame's only tool stays out)
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
            else if (seat != p.ShellSeat)
            {
                p.ShellSeat = (ushort)seat; // (the chair's construct lost a part and its parts moved up: the body is drawn in the right seat)
                ShellBroadcast(p);
            }
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

    /// <summary>
    /// Server, F from inside a shell (Karl, 2026-11-17): on a free Robot Shell Dock it clamps the shell on and sends them back
    /// to their body; it never happens by walking past one. False: not a shell dock, or they aren't in a shell (F as ever).
    /// </summary>
    private bool ShellDockUse(Player p, Vector3 eye, ushort body, int part, out string? why)
    {
        why = null;
        if (!_sh[p.Id].On || Constructs.Get(body) is not { } c || (uint)part >= (uint)c.PartCount || !c.DefOf(part).ShellBodyFits || ConstructShell.DockHolds(c, part))
            return false; // (a dock holding a shell: F lets it go, as from the body)
        ref var dp = ref c.Parts[part];
        c.WorldFrame(dp.Seg, out var q, out var t);
        if (Vector3.Distance(eye, t + Vector3.Transform(dp.Pos, q)) > ConstructBuild.UseReach + 1.5f)
            why = "too far from the dock";
        else if (dp.Broken)
            why = $"the {dp.D.Name} is wrecked";
        else if (_sh[p.Id].ShellDef is { } worn && worn.Name != dp.D.ToolDockFits)
            why = $"a {worn.Name} doesn't fit a {dp.D.Name}"; // (the Mining Shell is too big for one: docs/sandbox/design/mining-shell.md)
        else if (SandboxHands.DockRefusal(c, p.Id) is { } no)
            why = no;
        else
            ShellReturn(p, lost: false, "You clamp the shell on the dock and wake in the chair.", c, part);
        return true;
    }

    /// <summary>The colours of a shell's oil: what it sprays when hit and the marks on it (BodyBloodRender.OilColor).</summary>
    private static readonly Vector3 OilA = new(0.035f, 0.03f, 0.022f), OilB = new(0.09f, 0.07f, 0.035f);

    /// <summary>
    /// Both sides, a tracer that ends on a player wearing a robot shell (Karl, 2026-11-17): a few drops of dark oil spray off
    /// where it hit, in place of blood. Cosmetic, from the debris pool (nothing allocates). <paramref name="hitFlags"/> as
    /// <see cref="TracerSounds"/>: 0 or a miss flag says it hit nobody.
    /// </summary>
    private void ShellOilFx(Vector3 from, Vector3 to, int hitFlags)
    {
        if (hitFlags == 0 || hitFlags > 0 && (hitFlags & 1) == 0)
            return;
        foreach (var p in Players.Values)
        {
            if (!p.Alive || !p.InShell)
                continue;
            var box = World.Hitbox(p.RenderPosition, p.RenderStance, p.RenderYaw);
            var grown = new Box(box.Center, box.Half + new Vector3(0.25f));
            if (to.X < grown.Min.X || to.Y < grown.Min.Y || to.Z < grown.Min.Z || to.X > grown.Max.X || to.Y > grown.Max.Y || to.Z > grown.Max.Z)
                continue;
            PropDebris.SpawnBox(new Box(to, new Vector3(0.04f)), from, PropDamage.Bullet, (int)(to.X * 131 + to.Z * 977) ^ (int)(PropDebris.Spawned & 0xFFFF),
                7, 0.01f, 0.028f, 2.4f, OilA, OilB);
            return;
        }
    }

    /// <summary>Server: a player leaves the game: back in their body first, so their record is the body's and the shell stands.</summary>
    internal void ShellLeaveGame(Player p) => ShellReturn(p, lost: false, "");

    /// <summary>The share of the survival physics gun's lift a player's body has (<see cref="ConstructEdits.LiftShare"/>): a worn shell frame's "lift", else 1.</summary>
    private float ShellLiftShare(byte player) => Players.TryGetValue(player, out var p) && p.InShell ? ShellFrames.Worn(p).Lift : 1f;

    private readonly List<Construct> _shSaved = [];

    /// <summary>Is this body a world save's stand-in for a worn shell (only while a save captures)?</summary>
    private bool ShellSaveStandIn(Construct c) => _shSaved.Count > 0 && _shSaved.Contains(c);

    /// <summary>
    /// Server, before a world save's capture: each worn shell stands for a moment where its wearer is, as worn and as damaged, with
    /// its ore hopper's load in its bin, so the save holds it (the record holds their body in the chair: <see cref="ShellCatch"/>).
    /// The capture is the same tick, and <see cref="ShellUnstandAfterSave"/> takes them away again: no peer hears of them. A save is
    /// rare, so the list may allocate.
    /// </summary>
    private void ShellStandForSave()
    {
        _shSaved.Clear();
        foreach (var p in Players.Values)
        {
            ref var run = ref _sh[p.Id];
            if (!run.On || run.ShellDef == null)
                continue;
            if (ConstructBuild.PlaceNew(Constructs, run.ShellDef, p.Position, p.Yaw) is not { } c)
            {
                Log($"[shell] {p.Name}'s worn {run.ShellDef.Name} isn't in this save: the world is full");
                continue;
            }
            ConstructConfig.Put(ref c.Parts[0], run.ShellDef, CfgKey.Channel, run.Channel);
            ShellWear.Write(ref c.Parts[0], run.ShellDef, p.ShellWear);
            ShellWear.PutHealth(ref c.Parts[0], run.ShellDef, p.Health);
            ShellFrames.WriteKit(ref c.Parts[0], run.ShellDef, p.ShellMods);
            c.Owner = run.Owner;
            c.Frozen = true;
            c.Rebuild();
            ref var hp = ref SandboxHands.Hoppers[p.Id];
            if (hp.Amount > 0 && Constructs.Deposit(c, 0, hp.Amount, hp.Material) < hp.Amount)
                Log($"[shell] {p.Name}'s hopper didn't all fit the saved {run.ShellDef.Name}'s bin");
            _shSaved.Add(c);
        }
    }

    /// <summary>Server, after the capture: the stand-ins go (the wearers still wear their shells).</summary>
    private void ShellUnstandAfterSave()
    {
        foreach (var c in _shSaved)
            Constructs.Remove(c, quiet: true); // (no peer heard of it: no gone message, no unbolt clunk at the wearer)
        _shSaved.Clear();
    }

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

    /// <summary>Server: who wears a shell, to everyone (<see cref="Msg.Shell"/>: u8 player, u8 on, u16 chair, u16 seat, 8 x u8 the works' wear,
    /// u8 frame, u8 hopper material, u16 hopper units; protocol 353; then a byte a module slot, its kit: protocol 370).</summary>
    private void ShellBroadcast(Player p)
    {
        if (Net.IsOffline)
            return;
        var w = SbW;
        w.Begin(Msg.Shell).U8(p.Id).U8(p.InShell ? (byte)1 : (byte)0).U16(p.ShellChair).U16(p.ShellSeat);
        for (var k = 0; k < ShellWear.Works; k++)
            w.U8(p.ShellWear[k]);
        ShellWriteFrame(w, p);
        Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: everyone wearing a shell, to one joiner (from <see cref="SendSandboxState"/>).</summary>
    private void ShellSendAll(RakNetGUID to)
    {
        foreach (var p in Players.Values)
            if (p.InShell)
            {
                var w = SbW;
                w.Begin(Msg.Shell).U8(p.Id).U8(1).U16(p.ShellChair).U16(p.ShellSeat);
                for (var k = 0; k < ShellWear.Works; k++)
                    w.U8(p.ShellWear[k]);
                ShellWriteFrame(w, p);
                Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
            }
    }

    /// <summary>Client: a <see cref="Msg.Shell"/> (after its id).</summary>
    private void ReadShell(ref PacketReader r)
    {
        var id = r.U8();
        var on = r.U8() != 0;
        var chair = r.U16();
        var seat = r.U16();
        ShellCond wear = default;
        for (var k = 0; k < ShellWear.Works; k++)
            wear[k] = r.U8();
        if (!r.Has(4))
            return;
        var frame = r.U8();
        var hopMat = r.U8();
        var hopAmt = r.U16();
        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
        for (var k = 0; k < kit.Length; k++)
            kit[k] = r.Has(1) ? r.U8() : (byte)0; // (its modules: protocol 370)
        var found = Players.TryGetValue(id, out var p);
        var heard = found ? _shellScratch : new ShellHeard(); // (a hopper's load is sent as it fills: no allocation for a known player)
        (heard.On, heard.Chair, heard.Seat, heard.Wear, heard.Frame, heard.HopMat, heard.HopAmt) = (on, chair, seat, wear, frame, hopMat, hopAmt);
        kit.CopyTo(heard.Kit);
        if (found)
        {
            _shellHeard.Remove(id);
            ApplyShell(p!, heard);
            return;
        }
        // A joiner hears who wears a shell straight after the welcome, before the snapshot that makes their players: kept by id
        // until then (GetOrAddRemote), as the roster's cosmetics are.
        if (on)
            _shellHeard[id] = heard;
        else
            _shellHeard.Remove(id);
        Log($"[shell] player {id} {(on ? "in" : "out of")} a shell before their first snapshot: kept for it");
    }

    /// <summary>A <see cref="Msg.Shell"/> heard on a client, kept for a player not yet in <see cref="Players"/>.</summary>
    private sealed class ShellHeard
    {
        public bool On;
        public ushort Chair, Seat, HopAmt;
        public ShellCond Wear;
        public byte Frame, HopMat;
        public readonly byte[] Kit = new byte[ShellFrames.SlotCount];
    }

    private readonly Dictionary<byte, ShellHeard> _shellHeard = new();
    private readonly ShellHeard _shellScratch = new();

    /// <summary>Client: a new remote player (GameSnapshots.GetOrAddRemote) takes the shell state heard for them before it.</summary>
    private void ApplyHeardShell(Player p)
    {
        if (_shellHeard.Remove(p.Id, out var heard))
            ApplyShell(p, heard);
    }

    private static void ApplyShell(Player p, ShellHeard h)
    {
        var on = h.On;
        p.InShell = on;
        (p.ShellChair, p.ShellSeat) = on ? (h.Chair, h.Seat) : ((ushort)0, (ushort)0);
        p.ShellWear = on ? h.Wear : default;
        p.ShellFrame = on ? h.Frame : (byte)0;
        p.Sim.Frame = p.ShellFrame; // (its size: the mover reads it, World.Body)
        for (var k = 0; k < h.Kit.Length; k++)
            p.ShellMods[k] = on ? h.Kit[k] : (byte)0;
        ShellFrames.Refit(p);
        (p.HopperMaterial, p.HopperAmount) = on ? (h.HopMat, h.HopAmt) : ((byte)0, (ushort)0);
    }

    // ---- The link-up as this PC's player sees it (Karl, 2026-11-14) ---------------------------------------------------
    // Sitting down in a chair, the helmet comes down over their eyes, everything goes black but for a few small lamps, then
    // they open their eyes as the robot. Worked out on each peer from its own seat and the shell flag: no bytes on the wire.

    public const double LinkHelmetSeconds = 0.8; // the helmet coming down
    public const double LinkGiveUpSeconds = ShellLinkSeconds + 2.0; // no shell by then (none free, out of reach): the helmet lifts
    private double _lkFrom = -1, _lkWoke = -1, _lkLift = -1, _lkOut = -1;
    private bool _lkSpent, _lkWasShell;

    /// <summary>
    /// Every frame, host and clients: this PC's player's link-up. <paramref name="helmet"/> is how far down the helmet is
    /// (0 up, 1 over the eyes), <paramref name="dark"/> how black the view is (the lamps show in it). False: nothing to draw.
    /// </summary>
    public bool ShellLinkView(double now, out float helmet, out float dark)
    {
        helmet = dark = 0;
        if (!SandboxOn || Local is not { } me || !me.Alive)
        {
            _lkFrom = _lkWoke = _lkLift = -1;
            _lkWasShell = false; // (dead or out of the Sandbox: no unlink sound when they come back)
            return false;
        }
        if (me.InShell)
        {
            if (!_lkWasShell)
            {
                _lkWoke = now; // (the link took: they open their eyes as the robot)
                LinkCue(Sfx.ShellLinkWake);
            }
            _lkWasShell = _lkSpent = true;
            _lkFrom = -1;
        }
        else
        {
            if (_lkWasShell)
                LinkCue(Sfx.ShellUnlink); // (back in the body: the link spins down, the helmet lifts)
            _lkWasShell = false;
            _lkWoke = -1;
            ref var h = ref SandboxHands.Hand(me.Id);
            var inChair = !OpeningActive && h.SeatBody != 0 && Constructs.Get(h.SeatBody) is { } c && h.SeatPart < c.PartCount && c.Parts[h.SeatPart].D.ShellRange > 0;
            if (!inChair)
            {
                if (_lkOut < 0)
                    _lkOut = now;
                if (now - _lkOut > 0.5)
                    _lkSpent = false; // (they stood up: the next sit links again, as on the server; after a moment, since a client hears the seat back after the shell flag)
                _lkFrom = _lkLift = -1;
            }
            else
            {
                _lkOut = -1;
                if (!_lkSpent && _lkFrom < 0)
                {
                    _lkFrom = now;
                    LinkCue(Sfx.ShellHelmetDown);
                }
            }
        }
        if (_lkWoke >= 0)
        {
            var t = (float)(now - _lkWoke);
            dark = t < 0.25f ? 1 : 1 - (t - 0.25f) / 0.6f; // (a moment of black, then the robot's view comes up)
            helmet = 0;
            if (dark <= 0)
            {
                _lkWoke = -1;
                return false;
            }
            return true;
        }
        if (_lkFrom < 0)
        {
            if (_lkLift < 0)
                return false;
            var t = (float)(now - _lkLift); // (no link: the helmet goes back up)
            helmet = 1 - t / (float)LinkHelmetSeconds;
            dark = 0;
            if (helmet <= 0)
            {
                _lkLift = -1;
                return false;
            }
            return true;
        }
        var since = now - _lkFrom;
        if (since >= LinkGiveUpSeconds)
        {
            _lkSpent = true;
            _lkFrom = -1;
            _lkLift = now;
            LinkCue(Sfx.ShellHelmetUp); // (no link: the helmet goes back up)
            helmet = 1;
            return true;
        }
        helmet = Math.Min(1f, (float)(since / LinkHelmetSeconds));
        dark = since < LinkHelmetSeconds ? 0 : Math.Min(1f, (float)((since - LinkHelmetSeconds) / 0.3));
        if (since > LinkGiveUpSeconds - 0.4)
            dark *= (float)((LinkGiveUpSeconds - since) / 0.4); // (the lamps fade as the link gives up)
        return true;
    }

    private readonly Player?[] _shBodies = new Player?[256]; // (drawing only: each shell wearer's own body, sitting in the chair)

    /// <summary>
    /// Every frame, host and clients (Karl, 2026-11-14): a player wearing a shell, their own body as it is drawn sitting in
    /// the chair they linked from (null: not in a shell, or the chair isn't here). One stand-in per player id, made once and
    /// reused: nothing allocates in a steady frame. The robot itself is the player.
    /// </summary>
    public Player? ShellBodyOf(Player p)
    {
        if (!p.InShell || !SandboxOn || Constructs.Get(p.ShellChair) is not { } c || p.ShellSeat >= c.PartCount || c.Parts[p.ShellSeat].D.Role != PartRole.Seat)
            return null;
        var b = _shBodies[p.Id] ??= new Player();
        ConstructRender.SeatPose(c, p.ShellSeat, _alpha, IsServer, out var q, out var feet);
        b.Id = p.Id;
        b.Name = p.Name;
        b.Team = p.Team;
        b.Look = p.Look;
        b.Uniform = p.Uniform;
        b.Alive = true;
        b.InShell = false;
        b.RenderSeat = b.RenderSeatWas = c.SeatKindOf(p.ShellSeat);
        b.RenderSeatRot = q;
        b.RenderPosition = feet;
        b.RenderStance = Stance.Stand;
        b.RenderGrounded = true;
        b.RenderYaw = SeatYaw(q);
        b.RenderPitch = 0;
        return b;
    }

    /// <summary>The Esc menu's button: leave the shell and wake in the chair.</summary>
    public void ShellAskLeave() => SandboxSend(CActKind.ShellLeave);

    /// <summary>
    /// The HUD's lines while this PC's player wears a shell. The shell's health and its works' are not lines: the bottom-left panel
    /// shows them (UI/UiShellPanel.cs; Karl, 2026-11-18).
    /// </summary>
    private void ShellHud(Player me, List<HudLine> lines, Vector4 orange, Vector4 dim)
    {
        if (!me.InShell)
            return;
        HopperHud(me, lines, dim); // (an ore hopper's load: GameShellHopper.cs)
    }
}
