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
        return GantryStart(c, part, gd, slot, mod);
    }

    /// <summary>Client (or the host itself): fit module <paramref name="module"/> (its index) into slot <paramref name="slot"/> of the gantry's shell. The server says why not.</summary>
    public void SandboxGantry(ushort body, int part, int slot, int module)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Gantry).U16(body).U16((ushort)part).U8((byte)slot).U8((byte)module);
        SandboxCraftSend(w);
    }

    /// <summary>Server: the checks of a swap and its start. Null, or why not.</summary>
    internal string? GantryStart(Construct c, int part, ShellGantryDef gd, int slot, int module)
    {
        var world = Constructs;
        if (ConstructGantry.JobAt(world, c.Id, part) >= 0)
            return "the gantry is busy: wait for its arm";
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
        var table = ConstructGantry.TableOf(c, part);
        var creative = world.Creative;
        if (table < 0 && !creative)
            return "no Shell Parts Table behind the bay in its arms' reach: place one there, on the same base";
        var taken = false;
        if (table >= 0 && world.StoreOf(c, table) is { } st)
        {
            ConstructRackPack.Ensure(c.DefOf(table), st);
            if (ConstructRackPack.LastOf(st, (ushort)nd.Id, out var slab, out var lane) && world.RackTakeAt(c, table, slab, lane) != null)
                taken = true;
        }
        if (!taken && !creative)
            return $"no {nd.Name} on the parts table: make one (an Assembler) and put it there";
        ref var j = ref world.GantryJobs[world.GantryJobCount++];
        j = new GantryJob
        {
            Body = c.Id, Part = (ushort)part, Shell = shell.Id, Table = table >= 0 ? (ushort)table : ushort.MaxValue,
            Slot = (byte)slot, Old = (byte)(old != null ? old.Index + 1 : 0), New = (byte)(m.Index + 1), Arm = ConstructGantry.ArmOf(slot),
            Start = ConstructConveyor.Clock,
        };
        _gantryTaken[world.GantryJobCount - 1] = taken;
        shell.Wake();
        GantrySend(j, true, 0, null);
        Log($"[gantry] {c.DefOf(part).Name} on body {c.Id}: {old?.Name ?? "nothing"} off the {sp.D.Name}'s {ShellFrames.SlotLabel(slot)}, {m.Name} on ({(taken ? "from its table" : "creative")})");
        return null;
    }

    private readonly bool[] _gantryTaken = new bool[ConstructGantry.MaxJobs]; // (server, per job: the new module came off the table, so it goes back there if the job stops)

    /// <summary>Server, each Sandbox tick: the swaps under way, ended, stopped or fitted. Walks only the jobs (none: nothing).</summary>
    private void GantryTick()
    {
        var world = Constructs;
        if (world.GantryJobCount == 0)
            return;
        var now = ConstructConveyor.Clock;
        for (var k = world.GantryJobCount - 1; k >= 0; k--)
        {
            ref var j = ref world.GantryJobs[k];
            var t = now - j.Start;
            var c = world.Get(j.Body);
            var shell = world.Get(j.Shell);
            var gone = c == null || j.Part >= c.PartCount || c.Parts[j.Part].Broken || c.DefOf(j.Part).Gantry is not { } gd
                || shell == null || !ConstructGantry.InBay(c, j.Part, gd, shell);
            if (j.Old != 255 && gone)
            {
                GantryStop(k, c, "the shell or the gantry went before its module was fitted");
                continue;
            }
            if (j.Old != 255 && t >= ConstructGantry.TFitted)
            {
                GantryFit(ref j, c!, shell!, k);
                j.Old = 255; // (fitted: the arm only goes back to rest)
            }
            if (t >= ConstructGantry.Duration)
                GantryEnd(k);
        }
    }

    /// <summary>Server: the module goes on and the old one onto the table (or loose by it, or away in creative with no table).</summary>
    private void GantryFit(ref GantryJob j, Construct c, Construct shell, int k)
    {
        var world = Constructs;
        ref var sp = ref shell.Parts[0];
        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
        ShellFrames.ReadKit(sp, sp.D, kit);
        kit[j.Slot] = j.New;
        ShellFrames.WriteKit(ref sp, sp.D, kit);
        world.StructureDirty.Add(shell.Id); // (peers hear its settings: CBody)
        var all = ShellFrames.Modules;
        if (j.Old > 0 && j.Old - 1 < all.Length && ConstructParts.Find(all[j.Old - 1].Part) is { } od)
            GantryPutBack(c, j.Part, j.Table, od, "the old module");
        _gantryTaken[k] = false;
        Log($"[gantry] body {c.Id}: the {sp.D.Name}'s {ShellFrames.SlotLabel(j.Slot)} is a {(j.New > 0 && j.New - 1 < all.Length ? all[j.New - 1].Name : "?")} now");
    }

    /// <summary>Server: a module's part onto the table, else loose beside the gantry's rear frame (said in the log, never lost silently).</summary>
    private void GantryPutBack(Construct? c, int part, ushort table, ConstructPartDef d, string what)
    {
        var world = Constructs;
        if (c != null && table < c.PartCount && c.Parts[table].D.GantryTable && world.RackPut(c, table, d))
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

    /// <summary>Server: a job stopped before its fit: the new module back to the table (the shell keeps the old one).</summary>
    private void GantryStop(int k, Construct? c, string why)
    {
        ref var j = ref Constructs.GantryJobs[k];
        var all = ShellFrames.Modules;
        if (_gantryTaken[k] && j.New > 0 && j.New - 1 < all.Length && ConstructParts.Find(all[j.New - 1].Part) is { } nd)
            GantryPutBack(c, j.Part, j.Table, nd, "the module it carried");
        Log($"[gantry] body {j.Body}: stopped ({why})");
        GantryEnd(k);
    }

    /// <summary>Both: job <paramref name="k"/> is over (the server says so to every peer).</summary>
    private void GantryEnd(int k)
    {
        var world = Constructs;
        var j = world.GantryJobs[k];
        var last = world.GantryJobCount - 1;
        world.GantryJobs[k] = world.GantryJobs[last];
        _gantryTaken[k] = _gantryTaken[last];
        _gantryTaken[last] = false;
        world.GantryJobCount = last;
        if (IsServer)
            GantrySend(j, false, 0, null);
    }

    /// <summary>Server: a job's start or end to everyone (<paramref name="to"/> null) or to one joiner.</summary>
    private void GantrySend(in GantryJob j, bool on, double run, RakNetGUID? to)
    {
        if (Net.IsOffline)
            return;
        var w = SbW;
        w.Begin(Msg.Gantry).U16(j.Body).U16(j.Part).U8(on ? (byte)1 : (byte)0);
        if (on)
            w.U16(j.Shell).U16(j.Table).U8(j.Slot).U8(j.Old == 255 ? (byte)0 : j.Old).U8(j.New).U8(j.Arm).F32((float)run);
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

    /// <summary>Client: a <see cref="Msg.Gantry"/> (after its id).</summary>
    private void ReadGantry(ref PacketReader r)
    {
        var world = Constructs;
        var body = r.U16();
        var part = r.U16();
        var on = r.U8() != 0;
        var k = ConstructGantry.JobAt(world, body, part);
        if (!on)
        {
            if (k >= 0)
            {
                world.GantryJobs[k] = world.GantryJobs[world.GantryJobCount - 1];
                world.GantryJobCount--;
            }
            return;
        }
        var j = new GantryJob { Body = body, Part = part, Shell = r.U16(), Table = r.U16(), Slot = r.U8(), Old = r.U8(), New = r.U8(), Arm = r.U8() };
        j.Start = ConstructConveyor.Clock - Math.Clamp(r.F32(), 0, ConstructGantry.Duration);
        if (j.Arm >= ConstructGantry.ArmCount || j.Slot >= ShellFrames.SlotCount)
            return;
        if (k < 0)
        {
            if (world.GantryJobCount >= ConstructGantry.MaxJobs)
                return;
            k = world.GantryJobCount++;
        }
        world.GantryJobs[k] = j;
    }
}
