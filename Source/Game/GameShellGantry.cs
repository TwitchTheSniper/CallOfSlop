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

    /// <summary>The tallest shell a Shell Gantry's bay works on (m): the Mining Shell's 2.7 m fits; the Construction Shell's 5 m does not (construction-shell.md).</summary>
    public const float GantryTallest = 3f;

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
        if (World.PlayerHeight * ShellFrames.ScaleOf(frame) > GantryTallest)
            return $"a {sp.D.Name} ({World.PlayerHeight * ShellFrames.ScaleOf(frame):0.#} m) is too tall for the Shell Gantry ({GantryTallest:0} m at most)"; // (construction-shell.md: no gantry its size yet)
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
            PartAt = c.Parts[part].Pos, TableAt = tb != null && table >= 0 ? tb.Parts[table].Pos : default,
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
        if (world.GantryJobCount == 0 && world.GantryLockCount == 0)
            return;
        var now = ConstructConveyor.Clock;
        // A removal on a gantry's or a table's body renumbers its parts (ConstructWorld.Compact): first the swaps whose gantry is gone
        // end, under the indices they had (so a clamp opens by its own key), then the rest find their gantry and table again by place.
        for (var k = world.GantryJobCount - 1; k >= 0; k--)
        {
            ref var j = ref world.GantryJobs[k];
            var gc = world.Get(j.Body);
            if (gc != null && ConstructGantry.FindPart(gc, j.Part, j.PartAt) >= 0)
                continue;
            if (j.Old != 255)
                GantryStop(k, gc, "the gantry went before its module was fitted");
            else
                GantryEnd(k); // (fitted, its arm going home: nothing left to draw)
        }
        for (var k = 0; k < world.GantryJobCount; k++)
        {
            ref var j = ref world.GantryJobs[k];
            if (ConstructGantry.FindPart(world.Get(j.Body)!, j.Part, j.PartAt) is var at && at != j.Part)
                GantryRenumber(j.Body, j.PartAt, (ushort)at, now);
            if (j.Table != ushort.MaxValue && world.Get(j.TableBody) is { } tb && ConstructGantry.FindTable(tb, j.Table, j.TableAt) is var ti && ti != j.Table)
            {
                GantrySend(j, false, 0, null);
                Log($"[gantry] body {j.Body}: its parts table {j.TableBody}/{j.Table} is {(ti >= 0 ? $"part {ti} now (renumbered)" : "gone")}");
                j.Table = ti >= 0 ? (ushort)ti : ushort.MaxValue; // (gone: the fit says so in survival)
                GantrySend(j, true, now - j.Start, null);
            }
        }
        for (var k = world.GantryJobCount - 1; k >= 0; k--)
        {
            ref var j = ref world.GantryJobs[k];
            var t = now - j.Start;
            var c = world.Get(j.Body)!;
            var shell = world.Get(j.Shell);
            var gone = c.DefOf(j.Part).Gantry is not { } gd || shell == null || !ConstructGantry.InBay(c, j.Part, gd, shell);
            if (j.Old != 255 && gone)
            {
                GantryStop(k, c, "the shell or the gantry went before its module was fitted");
                continue;
            }
            if (j.Old != 255 && t >= ConstructGantry.TFitted)
            {
                if (GantryFit(ref j, c, shell!) is { } why)
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
        {
            ref var l = ref world.GantryLocks[k];
            if (l.Until < 0)
                GantryPin(in l, now); // (after the swaps' checks: a clamp whose gantry went has opened)
            else if (l.Lowering && (!GantryPin(in l, now) || now - l.Until >= ConstructGantry.LockLift))
                GantryLet(ref l); // (set down, or its gantry gone on the way: let go)
        }
    }

    /// <summary>
    /// Server: a gantry (known by its body and place, <paramref name="partAt"/>) has a new part index: its swaps and its clamp follow, and
    /// the peers hear the swaps again under the new one. Matched by place, never by the old index, which another gantry may have now.
    /// </summary>
    private void GantryRenumber(ushort body, Vector3 partAt, ushort part, double now)
    {
        var world = Constructs;
        var old = -1;
        for (var k = 0; k < world.GantryJobCount; k++)
            if (world.GantryJobs[k].Body == body && world.GantryJobs[k].PartAt == partAt && world.GantryJobs[k].Part != part)
            {
                old = world.GantryJobs[k].Part;
                GantrySend(world.GantryJobs[k], false, 0, null); // (every swap of that gantry: they share its clamp)
                world.GantryJobs[k].Part = part;
            }
        for (var k = 0; k < world.GantryLockCount; k++)
        {
            ref var l = ref world.GantryLocks[k];
            if (l.Body != body)
                continue;
            if ((l.Until < 0 || l.Lowering) && l.PartAt == partAt)
                l.Part = part;
            else if (l.Part == part && l.Until >= 0)
                l.Until = 0; // (another gantry's clamp opening under that index: done, so the keys never meet)
        }
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
    /// in the gantry part's frame (<see cref="GantryPin"/>): where it stood (or lay) until the jaws shut, then lifted upright to
    /// <see cref="ConstructGantry.HoldPoint"/> and held there. False: no clamp entry free (nothing held, no swap begun).
    /// </summary>
    private bool GantryClamp(Construct c, int part, Construct shell, double now)
    {
        var world = Constructs;
        var k = ConstructGantry.Hold(world, c.Id, part, shell.Id, now, out var fresh);
        if (k < 0)
            return false;
        ref var l = ref world.GantryLocks[k];
        if (fresh || shell.HeldBy != c.Id || l.Lowering)
        {
            // (from where the shell is now: a fresh hold, one let go while the jaws opened, or one caught again on its way down)
            Freeform.PartWorld(c, part, out var gp, out var gr);
            var iq = Quaternion.Conjugate(gr);
            l.PartAt = c.Parts[part].Pos;
            l.From = Vector3.Transform(shell.Pos - gp, iq);
            l.FromTurn = Quaternion.Normalize(iq * shell.Rot);
            HoldPose(c.DefOf(part).Gantry!, shell, true, out l.At, out l.Turn);
            if (l.Lowering)
                l.Since = now - ConstructGantry.LockClose; // (the jaws are shut still: it lifts again at once)
            l.Lowering = false;
            Log($"[gantry] body {c.Id}: its clamp holds shell {shell.Id}{(shell.Frame != 0 ? $" (riding body {shell.Frame}'s frame)" : "")} and lifts it");
        }
        shell.HeldBy = c.Id;
        shell.GantryHold = true;
        return true;
    }

    /// <summary>
    /// The shell's body pose in the gantry part's frame with its feet at <see cref="ConstructGantry.HoldPoint"/> (lifted, or down on the
    /// bay's floor), upright and facing out of the bay, as the arms work on it.
    /// </summary>
    private static void HoldPose(ShellGantryDef gd, Construct shell, bool up, out Vector3 at, out Quaternion turn)
    {
        ref var sp = ref shell.Parts[0];
        Freeform.PartWorld(shell, 0, out _, out var sr);
        var partTurn = Quaternion.Normalize(Quaternion.Conjugate(shell.Rot) * sr); // (the shell part's turn on its body)
        turn = Quaternion.Normalize(Quaternion.Conjugate(partTurn));
        at = ConstructGantry.HoldPoint(gd, up) - Vector3.Transform(sp.Pos, turn);
    }

    /// <summary>
    /// Server, each tick while a clamp holds its shell: the shell on its way from where the clamp caught it to where it holds it (or
    /// down again), in the gantry part's frame, moving as the gantry does there. A shell riding a moving ship's frame is posed in that
    /// frame (its frame pose is its truth), so it goes on with the ship; a still shell beside a still gantry is left asleep once the
    /// clamp's move is done (awake while it moves, so the peers hear its pose). False: the gantry part is gone (nothing posed). Nothing
    /// allocates.
    /// </summary>
    private bool GantryPin(in GantryLock l, double now)
    {
        var world = Constructs;
        if (world.Get(l.Body) is not { } c || ConstructGantry.FindPart(c, l.Part, l.PartAt) != l.Part || world.Get(l.Shell) is not { } shell || shell.HeldBy != l.Body)
            return false; // (the gantry part gone, broken or renumbered: never posed in another part's frame)
        Freeform.PartWorld(c, l.Part, out var gp, out var gr);
        var s = ConstructGantry.Carry(l, now);
        var moving = s != ConstructGantry.Carry(l, now - World.TickDt); // (the clamp lifting or lowering it this tick)
        var pos = gp + Vector3.Transform(Vector3.Lerp(l.From, l.At, s), gr);
        var rot = Quaternion.Normalize(gr * Quaternion.Slerp(l.FromTurn, l.Turn, s));
        var still = !c.MovesNow && shell.Frame == 0;
        if (still && !moving && shell.Asleep && Vector3.DistanceSquared(shell.Pos, pos) < 1e-6f && shell.PrevPos == shell.Pos)
            return true;
        if (still && moving && shell.Asleep)
            (shell.PrevPos, shell.PrevRot) = (shell.Pos, shell.Rot); // (drawn between its last pose and this one; the step sets them while it is awake)
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
        else if (still && moving)
            shell.Wake(); // (awake while the clamp moves it: a sleeping body's pose isn't sent)
        else if (still)
        {
            (shell.PrevPos, shell.PrevRot) = (pos, rot);
            shell.Asleep = true; // (held still: no physics until the clamp opens)
            shell.SleepTicks = 0;
        }
        return true;
    }

    /// <summary>
    /// Server: a gantry's last swap is over: its clamp sets the shell down on its feet on the bay's floor (<see cref="ConstructGantry.LockLift"/>),
    /// then lets it go (<see cref="GantryLet"/>) as its jaws open.
    /// </summary>
    private void GantryUnclamp(ushort body, ushort part)
    {
        var world = Constructs;
        var k = ConstructGantry.LockAt(world, body, part);
        if (k < 0 || world.GantryLocks[k].Until >= 0)
            return;
        ref var l = ref world.GantryLocks[k];
        var now = ConstructConveyor.Clock;
        l.Until = now;
        if (world.Get(l.Shell) is not { } shell || shell.HeldBy != body)
            return;
        if (world.Get(body) is { } c && ConstructGantry.FindPart(c, part, l.PartAt) == part && c.DefOf(part).Gantry is { } gd)
        {
            // (from where it is held now, the lift done or not, down to its feet on the floor)
            var s = ConstructGantry.Carry(l, now);
            l.From = Vector3.Lerp(l.From, l.At, s);
            l.FromTurn = Quaternion.Normalize(Quaternion.Slerp(l.FromTurn, l.Turn, s));
            HoldPose(gd, shell, false, out l.At, out l.Turn);
            l.Lowering = true;
            Log($"[gantry] body {body}: its clamp sets shell {shell.Id} down");
            return;
        }
        GantryLet(ref l);
    }

    /// <summary>Server: a clamp lets its shell go (woken, so it stands or falls as it will).</summary>
    private void GantryLet(ref GantryLock l)
    {
        l.Lowering = false;
        if (Constructs.Get(l.Shell) is not { } shell || shell.HeldBy != l.Body)
            return;
        shell.HeldBy = 0;
        shell.GantryHold = false;
        shell.Wake();
        Log($"[gantry] body {l.Body}: its clamp lets shell {shell.Id} go");
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
