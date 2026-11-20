using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the Shell Gantry's swaps (ConstructGantry.cs; docs/sandbox/design/mining-shell.md "Shell Gantry layout"). A player at the
/// gantry's panel picks a module for a slot of the shell standing in its bay (<see cref="CActKind.Gantry"/>); the server checks reach,
/// power, who may, the slot and the table, takes the new module off the table and starts the job. Its arm takes the old module off,
/// carries it to the table and brings the new one; at <see cref="ConstructGantry.TFitted"/> the old module's part goes onto the table
/// and the shell's settings hold the new one. Every peer hears the job begin and end (<see cref="Msg.Gantry"/>). The tick walks only
/// the jobs under way (a fixed array) and allocates nothing.
/// </summary>
public sealed partial class Game
{
    /// <summary>Server: CActKind.Gantry (u16 body, u16 part, u8 slot, u8 module index).</summary>
    private string? SandboxGantryAct(ref PacketReader r, Player p, Vector3 eye)
    {
        var body = r.U16();
        var part = r.U16();
        var slot = r.U8();
        var mod = r.U8();
        if (Constructs.Get(body) is not { } c || part >= c.PartCount || c.DefOf(part).Gantry is not { } gd)
            return "that gantry is gone";
        if (SandboxConfigRefusal(c, p.Id) is { } no)
            return no;
        if (Vector3.Distance(eye, ConstructEdits.ReachPoint(c, part, eye)) > ConstructConfig.Reach)
            return "too far away";
        if (!ConstructRecycler.Powered(Constructs, c, part))
            return "the Shell Gantry has no power";
        return GantryStart(c, part, gd, slot, mod, p.Id);
    }

    /// <summary>Client (or the host itself): fit module <paramref name="module"/> (its index) into slot <paramref name="slot"/> of the gantry's shell. The server says why not.</summary>
    public void SandboxGantry(ushort body, int part, int slot, int module)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Gantry).U16(body).U16((ushort)part).U8((byte)slot).U8((byte)module);
        SandboxCraftSend(w);
    }

    /// <summary>Server: the checks of a swap and its start. Null, or why not.</summary>
    internal string? GantryStart(Construct c, int part, ShellGantryDef gd, int slot, int module, byte player)
    {
        var world = Constructs;
        if ((uint)slot < ShellFrames.SlotCount && ConstructGantry.JobAt(world, c.Id, part, slot) >= 0)
            return $"the {ShellFrames.SlotLabel(slot)} is being changed already: wait for its arm";
        if (world.GantryJobCount >= ConstructGantry.MaxJobs)
            return "too many gantries at work: wait a moment";
        if ((uint)slot >= ShellFrames.SlotCount)
            return "no such slot";
        var all = ShellFrames.Modules;
        if ((uint)module >= (uint)all.Length)
            return "no such module";
        var m = all[module];
        if (ConstructGantry.ShellIn(world, c, part) is not { } shell)
            return "no shell stands in the bay: walk one in and leave it, or set one down there";
        if (shell.Owner != 255 && shell.Owner != player && !Factions.Shares(shell.Owner, player))
            return $"that shell is {Players.GetValueOrDefault(shell.Owner)?.Name ?? $"player {shell.Owner}"}'s: only they (or their faction) may change it"; // (a chair's rule for waking it: ConstructShell.Find)
        if (SandboxConfigRefusal(shell, player) is { } notYours)
            return notYours; // (and the configure rule, as for the gantry)
        if (shell.HeldBy != 0 && shell.HeldBy != c.Id || shell.GrabbedBy != 255 || shell.NudgeBy != 255)
            return "something else holds that shell: let it go first";
        ref var sp = ref shell.Parts[0];
        var frame = ShellFrames.FrameOf(sp.D);
        if (ShellFrames.FitRefusal(frame, slot, m) is { } why)
            return why;
        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
        ShellFrames.ReadKit(sp, sp.D, kit);
        var old = ShellFrames.ModuleAt(frame, kit, slot);
        if (old == m)
            return $"the {ShellFrames.SlotLabel(slot)} is a {m.Name} already";
        if (ConstructParts.Find(m.Part) is not { } nd)
        {
            Log($"[gantry] REFUSED: module {m.Name}'s part \"{m.Part}\" isn't loaded");
            return $"no part named {m.Part}";
        }
        var table = ConstructGantry.TableOf(world, c, part, out var tb);
        var creative = world.Creative;
        if (table < 0 && !creative)
            return "no Shell Parts Table behind the rear frame in its arms' reach: place one there";
        if (tb != null && tb != c && (tb.Owner != 255 && tb.Owner != player && !Factions.Shares(tb.Owner, player) ? "that parts table is someone else's" : SandboxConfigRefusal(tb, player)) is { } notYourTable)
            return notYourTable; // (and a table on another body: its modules are its owner's)
        var queued = 0; // (modules already promised to this gantry's queued swaps: each needs one of its own on the table)
        for (var q = 0; q < world.GantryJobCount; q++)
            if (world.GantryJobs[q].Body == c.Id && world.GantryJobs[q].Part == part && world.GantryJobs[q].New == m.Index + 1 && world.GantryJobs[q].Old != 255)
                queued++; // (a fitted one, its arm going home, took its module already)
        var onTable = ConstructGantry.OnTable(world, tb, table, m) > queued;
        if (!onTable && !creative)
            return $"no {nd.Name} on the parts table: make one (an Assembler) and put it there";
        var now = ConstructConveyor.Clock;
        if (!GantryClamp(c, part, shell, now))
            return "too many gantries at work: wait a moment";
        // (the module stays on the table until the fit, when it is taken and the old one set down in the same tick: a save or a world
        // stopping mid-swap finds the shell with its old module and the table with the new one, nothing lost or doubled)
        ref var j = ref world.GantryJobs[world.GantryJobCount++];
        j = new GantryJob
        {
            Body = c.Id, Part = (ushort)part, Shell = shell.Id, TableBody = tb?.Id ?? 0, Table = table >= 0 ? (ushort)table : ushort.MaxValue,
            Slot = (byte)slot, Old = (byte)(old != null ? old.Index + 1 : 0), New = (byte)(m.Index + 1), Arm = ConstructGantry.ArmOf(slot),
            PartAt = c.Parts[part].Pos,
        };
        j.Start = ConstructGantry.NextStart(world, c.Id, part, j.Arm, now); // (behind this gantry's swaps already asked: one sequence)
        GantrySend(j, true, now - j.Start, null);
        Log($"[gantry] {c.DefOf(part).Name} on body {c.Id}{(j.Start > now ? $" (queued, in {j.Start - now:0.0} s)" : "")}: {old?.Name ?? "nothing"} off the {sp.D.Name}'s {ShellFrames.SlotLabel(slot)}, {m.Name} on ({(onTable ? "from its table" : "creative")})");
        return null;
    }

    /// <summary>The window tool's "shellgantry drive SPEED TURN" (a test, never set in play): every gantry's body is driven as a ship is, hovering, at this speed along its front and this turn (rad/s).</summary>
    internal static float GantryTestSpeed, GantryTestTurn;

    /// <summary>Server, each Sandbox tick: the swaps under way, ended, stopped or fitted. Walks only the jobs (none: nothing).</summary>
    private void GantryTick()
    {
        var world = Constructs;
        if (GantryTestSpeed != 0 || GantryTestTurn != 0)
            GantryTestDrive(world);
        if (world.GantryJobCount == 0)
            return;
        var now = ConstructConveyor.Clock;
        for (var k = world.GantryJobCount - 1; k >= 0; k--)
        {
            ref var j = ref world.GantryJobs[k];
            var t = now - j.Start;
            var c = world.Get(j.Body);
            var shell = world.Get(j.Shell);
            var at = c != null ? ConstructGantry.FindPart(c, j.Part, j.PartAt) : -1; // (the body's parts renumbered by a removal: the gantry found again by its place)
            if (at >= 0 && at != j.Part)
                GantryRenumber(ref j, (ushort)at, now);
            var gone = at < 0 || c!.DefOf(j.Part).Gantry is not { } gd || shell == null || !ConstructGantry.InBay(c, j.Part, gd, shell);
            if (j.Old != 255 && gone)
            {
                GantryStop(k, c, "the shell or the gantry went before its module was fitted");
                continue;
            }
            if (at < 0)
            {
                GantryEnd(k); // (fitted, its arm going home, and the gantry went: nothing left to draw)
                continue;
            }
            if (j.Old != 255 && t >= ConstructGantry.TFitted)
            {
                if (GantryFit(ref j, c!, shell!) is { } why)
                {
                    GantryStop(k, c, why);
                    continue;
                }
                j.Old = 255; // (fitted: the arm only goes back to rest)
            }
            if (t >= ConstructGantry.Duration)
                GantryEnd(k);
        }
        for (var k = 0; k < world.GantryLockCount; k++)
            if (world.GantryLocks[k].Until < 0)
                GantryPin(in world.GantryLocks[k]); // (after the swaps' checks: a clamp whose gantry went has opened)
    }

    /// <summary>Server: a swap's gantry part has a new index (its body's parts were renumbered): the swap and its clamp follow, and the peers hear it again under the new one.</summary>
    private void GantryRenumber(ref GantryJob j, ushort part, double now)
    {
        var world = Constructs;
        var old = j.Part;
        var body = j.Body;
        for (var k = 0; k < world.GantryJobCount; k++)
            if (world.GantryJobs[k].Body == body && world.GantryJobs[k].Part == old)
            {
                GantrySend(world.GantryJobs[k], false, 0, null); // (every swap of that gantry: they share its clamp)
                world.GantryJobs[k].Part = part;
            }
        if (ConstructGantry.LockAt(world, body, old) is var l and >= 0)
            world.GantryLocks[l].Part = part;
        for (var k = 0; k < world.GantryJobCount; k++)
            if (world.GantryJobs[k].Body == body && world.GantryJobs[k].Part == part)
                GantrySend(world.GantryJobs[k], true, now - world.GantryJobs[k].Start, null);
        Log($"[gantry] body {body}: its gantry part {old} is part {part} now (the body's parts were renumbered)");
    }

    /// <summary>Server, in one tick: the new module off the table and onto the shell, the old one onto the table (or loose by it, or away in creative with no table). Null, or why it couldn't.</summary>
    private string? GantryFit(ref GantryJob j, Construct c, Construct shell)
    {
        var world = Constructs;
        var all = ShellFrames.Modules;
        if (j.New == 0 || j.New - 1 >= all.Length || ConstructParts.Find(all[j.New - 1].Part) is not { } nd)
            return "its module isn't known";
        var taken = false;
        if (world.Get(j.TableBody) is { } tb && j.Table < tb.PartCount && tb.Parts[j.Table].D.GantryTable && world.StoreOf(tb, j.Table) is { } st)
        {
            ConstructRackPack.Ensure(tb.DefOf(j.Table), st);
            taken = ConstructRackPack.LastOf(st, (ushort)nd.Id, out var slab, out var lane) && world.RackTakeAt(tb, j.Table, slab, lane) != null;
        }
        if (!taken && !world.Creative)
            return $"the {nd.Name} left the parts table before the arm fitted it";
        ref var sp = ref shell.Parts[0];
        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
        ShellFrames.ReadKit(sp, sp.D, kit);
        kit[j.Slot] = j.New;
        ShellFrames.WriteKit(ref sp, sp.D, kit);
        world.StructureDirty.Add(shell.Id); // (peers hear its settings: CBody)
        if (j.Old > 0 && j.Old - 1 < all.Length && ConstructParts.Find(all[j.Old - 1].Part) is { } od)
            GantryPutBack(c, j.Part, j.TableBody, j.Table, od, "the old module");
        Log($"[gantry] body {c.Id}: the {sp.D.Name}'s {ShellFrames.SlotLabel(j.Slot)} is a {all[j.New - 1].Name} now");
        return null;
    }

    /// <summary>Server: a module's part onto the table, else loose beside the gantry's rear frame (said in the log, never lost silently).</summary>
    private void GantryPutBack(Construct? c, int part, ushort tableBody, ushort table, ConstructPartDef d, string what)
    {
        var world = Constructs;
        if (world.Get(tableBody) is { } tb && table < tb.PartCount && tb.Parts[table].D.GantryTable && world.RackPut(tb, table, d))
            return;
        if (c == null || part >= c.PartCount)
        {
            if (!world.Creative)
                Log($"[gantry] {what}, a {d.Name}, is lost: its gantry went");
            return;
        }
        if (world.Creative && table == ushort.MaxValue)
            return; // (creative with no table: the module was never taken from anywhere)
        Freeform.PartWorld(c, part, out var pos, out var rot);
        var gd = c.DefOf(part).Gantry;
        var at = pos + Vector3.Transform(new Vector3(0, 0.5f, (gd?.BayMax.Z ?? 2.5f) + 1f), rot);
        if (ConstructEdits.DropLoose(world, d, at, rot) is { } b)
        {
            b.Owner = c.Owner;
            Log($"[gantry] {what}, a {d.Name}, has no room on the parts table: it lies behind the gantry");
        }
        else
            Log($"[gantry] {what}, a {d.Name}, is lost: no room on the table and none in the world");
    }

    /// <summary>Server: a job stopped before its fit (the shell keeps the old module, the table the new one: nothing moved yet).</summary>
    private void GantryStop(int k, Construct? c, string why)
    {
        Log($"[gantry] body {Constructs.GantryJobs[k].Body}{(c == null ? " (gone)" : "")}: stopped ({why})");
        GantryEnd(k);
    }

    /// <summary>Server: job <paramref name="k"/> is over (said to every peer); the gantry's last one opens its clamp.</summary>
    private void GantryEnd(int k)
    {
        var world = Constructs;
        var j = world.GantryJobs[k];
        var last = world.GantryJobCount - 1;
        world.GantryJobs[k] = world.GantryJobs[last];
        world.GantryJobCount = last;
        GantrySend(j, false, 0, null);
        if (!ConstructGantry.AnyJob(world, j.Body, j.Part))
            GantryUnclamp(j.Body, j.Part);
    }

    private static void GantryTestDrive(ConstructWorld world)
    {
        foreach (var c in world.Bodies)
            if (c.GantryCount > 0 && !c.Anchored)
            {
                var vel = Vector3.Transform(-Vector3.UnitZ, c.Rot) * GantryTestSpeed;
                c.Vel = vel / 0.999f - ConstructGravity.GravityFor(c, c.WorldCom) * World.TickDt; // (as RideCheck.Drive: the step's damping and fall made up)
                c.AngVel = new Vector3(0, GantryTestTurn / 0.985f, 0);
                c.SpeedKept = c.Vel.Length() + 1f;
                c.Wake();
            }
    }

    /// <summary>
    /// Server: the gantry's waist clamp shuts on the shell (the first swap of a change): the shell is held by the gantry
    /// (<see cref="Construct.HeldBy"/>: no grab, no edit, no waking; ConstructEdits.Contested says why) and from now on posed every tick
    /// where it stands in the gantry part's frame (<see cref="GantryPin"/>). False: no clamp entry free (nothing held, no swap begun).
    /// </summary>
    private bool GantryClamp(Construct c, int part, Construct shell, double now)
    {
        var world = Constructs;
        var k = ConstructGantry.Hold(world, c.Id, part, shell.Id, now, out var fresh);
        if (k < 0)
            return false;
        if (fresh || shell.HeldBy != c.Id)
        {
            Freeform.PartWorld(c, part, out var gp, out var gr);
            var iq = Quaternion.Conjugate(gr);
            world.GantryLocks[k].PartAt = c.Parts[part].Pos;
            world.GantryLocks[k].At = Vector3.Transform(shell.Pos - gp, iq);
            world.GantryLocks[k].Turn = Quaternion.Normalize(iq * shell.Rot);
            Log($"[gantry] body {c.Id}: its clamp holds shell {shell.Id}{(shell.Frame != 0 ? $" (riding body {shell.Frame}'s frame)" : "")}");
        }
        shell.HeldBy = c.Id;
        shell.GantryHold = true;
        return true;
    }

    /// <summary>
    /// Server, each tick while a clamp is shut: the shell where the clamp holds it in the gantry part's frame, moving as the gantry does
    /// there. A shell riding a moving ship's frame is posed in that frame (its frame pose is its truth), so it goes on with the ship; a
    /// still shell beside a still gantry is left asleep. Nothing allocates.
    /// </summary>
    private void GantryPin(in GantryLock l)
    {
        var world = Constructs;
        if (world.Get(l.Body) is not { } c || ConstructGantry.FindPart(c, l.Part, l.PartAt) != l.Part || world.Get(l.Shell) is not { } shell || shell.HeldBy != l.Body)
            return; // (the gantry part gone, broken or renumbered: never posed in another part's frame)
        Freeform.PartWorld(c, l.Part, out var gp, out var gr);
        var pos = gp + Vector3.Transform(l.At, gr);
        var rot = Quaternion.Normalize(gr * l.Turn);
        var still = !c.MovesNow && shell.Frame == 0;
        if (still && shell.Asleep && Vector3.DistanceSquared(shell.Pos, pos) < 1e-6f)
            return;
        shell.Pos = pos;
        shell.Rot = rot;
        shell.Vel = still ? Vector3.Zero : c.PointVel(shell.WorldCom);
        shell.AngVel = still ? Vector3.Zero : c.AngVel;
        if (shell.Frame != 0 && world.Get(shell.Frame) is { } f)
        {
            var iq = Quaternion.Conjugate(f.Rot);
            shell.FramePos = Vector3.Transform(pos - f.Pos, iq);
            shell.FrameRot = Quaternion.Normalize(iq * rot);
            shell.FrameVel = shell.FrameSpin = Vector3.Zero;
        }
        else if (still)
        {
            (shell.PrevPos, shell.PrevRot) = (pos, rot);
            shell.Asleep = true; // (held still: no physics until the clamp opens)
            shell.SleepTicks = 0;
        }
    }

    /// <summary>Server: a gantry's last swap is over: its clamp opens and lets the shell go (woken, so it stands or falls as it will).</summary>
    private void GantryUnclamp(ushort body, ushort part)
    {
        var world = Constructs;
        var k = ConstructGantry.LockAt(world, body, part);
        if (k < 0 || world.GantryLocks[k].Until >= 0)
            return;
        ref var l = ref world.GantryLocks[k];
        l.Until = ConstructConveyor.Clock;
        if (world.Get(l.Shell) is not { } shell)
            return;
        if (shell.HeldBy == body)
            shell.HeldBy = 0;
        shell.GantryHold = false;
        shell.Wake();
        Log($"[gantry] body {body}: its clamp lets shell {shell.Id} go");
    }

    /// <summary>Server: a job's start or end to everyone (<paramref name="to"/> null) or to one joiner.</summary>
    private void GantrySend(in GantryJob j, bool on, double run, RakNetGUID? to)
    {
        if (Net.IsOffline)
            return;
        var w = SbW;
        w.Begin(Msg.Gantry).U16(j.Body).U16(j.Part).U8(on ? (byte)1 : (byte)0).U8(j.Slot);
        if (on)
            w.U16(j.Shell).U16(j.TableBody).U16(j.Table).U8(j.Old == 255 ? (byte)0 : j.Old).U8(j.New).U8(j.Arm).F32((float)run);
        if (to is { } g)
            Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
        else
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: every swap under way, to a joiner (from <see cref="SendSandboxState"/>).</summary>
    private void GantrySendAll(RakNetGUID to)
    {
        var now = ConstructConveyor.Clock;
        for (var k = 0; k < Constructs.GantryJobCount; k++)
            GantrySend(Constructs.GantryJobs[k], true, now - Constructs.GantryJobs[k].Start, to);
    }

    /// <summary>Client: a <see cref="Msg.Gantry"/> (after its id). A swap heard begins its clamp; the gantry's last one ended opens it.</summary>
    private void ReadGantry(ref PacketReader r)
    {
        var world = Constructs;
        var body = r.U16();
        var part = r.U16();
        var on = r.U8() != 0;
        var slot = r.U8();
        var k = ConstructGantry.JobAt(world, body, part, slot);
        var now = ConstructConveyor.Clock;
        if (!on)
        {
            if (k >= 0)
            {
                world.GantryJobs[k] = world.GantryJobs[world.GantryJobCount - 1];
                world.GantryJobCount--;
            }
            if (!ConstructGantry.AnyJob(world, body, part) && ConstructGantry.LockAt(world, body, part) is var lk and >= 0 && world.GantryLocks[lk].Until < 0)
            {
                world.GantryLocks[lk].Until = now;
                if (world.Get(world.GantryLocks[lk].Shell) is { } freed)
                    freed.GantryHold = false;
            }
            return;
        }
        var j = new GantryJob { Body = body, Part = part, Slot = slot, Shell = r.U16(), TableBody = r.U16(), Table = r.U16(), Old = r.U8(), New = r.U8(), Arm = r.U8() };
        j.Start = now - Math.Clamp(r.F32(), -ConstructGantry.Duration * ShellFrames.SlotCount, ConstructGantry.Duration); // (negative: queued behind another slot)
        if (j.Arm >= ConstructGantry.ArmCount || j.Slot >= ShellFrames.SlotCount)
            return;
        if (k < 0)
        {
            if (world.GantryJobCount >= ConstructGantry.MaxJobs)
                return;
            k = world.GantryJobCount++;
        }
        world.GantryJobs[k] = j;
        if (ConstructGantry.Hold(world, body, part, j.Shell, Math.Min(now, j.Start), out _) >= 0 && world.Get(j.Shell) is { } held)
            held.GantryHold = true; // (drawn part by part while held: its modules come off)
    }
}
