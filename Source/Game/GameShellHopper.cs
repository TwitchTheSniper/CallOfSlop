using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A robot shell's ore hopper and drill arm (docs/sandbox/design/mining-shell.md; the user, 2026-11-17). A frame whose back slot holds a
/// <see cref="ShellModuleKind.Hopper"/> stands as a part with a bin (the Mining Shell's <c>storage.bulk</c>): worn, that bin's load is
/// the player's hopper (<see cref="ConstructEdits.Hoppers"/>), put back into the part when it stands again, spilt as a Material Bundle
/// when it is destroyed. A <see cref="ShellModuleKind.Drill"/> arm taps the deposit under a ground drill scoop into the hopper, by a Drill
/// Module's rules (<see cref="ResourceField.Tap"/>, a spot worked out, the tier). F at a bin or a refinery tips the hopper first
/// (<see cref="ConstructEdits.TipHopper"/>). Every peer hears the load with <see cref="Msg.Shell"/>, sent again on a 2 % step.
/// Server state is fixed arrays by player id: nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    private readonly double[] _drillSaid = new double[256], _drillAt = new double[256];
    private readonly float[] _drillAcc = new float[256]; // (ore not yet a whole unit)

    /// <summary>The longest gap between two scoops that counts as drilling all along (s): a first scoop after a pause taps this much.</summary>
    private const double DrillGap = 0.5;

    /// <summary>Server, waking in a shell (before its construct goes): its bin's load becomes the hopper.</summary>
    private void HopperOn(Player p, Construct shell)
    {
        ref var hp = ref SandboxHands.Hoppers[p.Id];
        hp = default;
        var frame = ShellFrames.Worn(p);
        if (frame.Find(ShellModuleKind.Hopper) is not { } mod || shell.PartCount < 1 || !shell.DefOf(0).IsBin)
            return;
        hp.Module = mod;
        hp.Capacity = shell.DefOf(0).BulkCapacity;
        if (Constructs.StoreOf(shell, 0) is { Amount: > 0 } st)
            (hp.Material, hp.Amount) = (st.Material, Math.Min(st.Amount, hp.Capacity));
        (p.HopperMaterial, p.HopperAmount) = (hp.Material, (ushort)Math.Min(hp.Amount, ushort.MaxValue));
    }

    /// <summary>Server, leaving a shell: the hopper's load into the standing shell's bin, or (<paramref name="stood"/> null: destroyed) spilt at <paramref name="at"/>.</summary>
    private void HopperOff(Player p, Construct? stood, Vector3 at)
    {
        ref var hp = ref SandboxHands.Hoppers[p.Id];
        if (hp.Amount > 0)
        {
            var left = hp.Amount;
            if (stood != null)
                left -= Constructs.Deposit(stood, 0, left, hp.Material);
            if (left > 0 && !Constructs.SpillBundle(hp.Material, left, at + GravityFrames.UpAt(at) * 1.5f, Vector3.Zero))
                Log($"[shell] {p.Name}: {left} {RecyclingTable.Current.NameOf(hp.Material)} from the hopper lost (no room in the world)");
            else if (left > 0)
                Log($"[shell] {p.Name}: {left} {RecyclingTable.Current.NameOf(hp.Material)} spilt from the hopper");
        }
        hp = default;
        (p.HopperMaterial, p.HopperAmount) = (0, 0);
        _boring[p.Id] = false;
    }

    /// <summary>Server, from <see cref="ShellTick"/>: peers hear a hopper's load again when it moved by 2 % of its room, or emptied or changed material.</summary>
    private void HopperSync(Player p)
    {
        ref var hp = ref SandboxHands.Hoppers[p.Id];
        if (hp.Capacity <= 0)
            return;
        var step = Math.Max(1, hp.Capacity / 50);
        if (hp.Material == p.HopperMaterial && (hp.Amount == 0) == (p.HopperAmount == 0) && Math.Abs(hp.Amount - p.HopperAmount) < step && !(hp.Amount == hp.Capacity && p.HopperAmount != hp.Capacity))
            return;
        (p.HopperMaterial, p.HopperAmount) = (hp.Amount > 0 ? hp.Material : (byte)0, (ushort)Math.Min(hp.Amount, ushort.MaxValue));
        ShellBroadcast(p);
    }

    /// <summary>The frame, hopper material and load at the end of <see cref="Msg.Shell"/> (protocol 353).</summary>
    private static void ShellWriteFrame(PacketWriter w, Player p)
    {
        w.U8(p.InShell ? p.ShellFrame : (byte)0).U8(p.InShell ? p.HopperMaterial : (byte)0).U16(p.InShell ? p.HopperAmount : (ushort)0);
        for (var k = 0; k < ShellFrames.SlotCount; k++)
            w.U8(p.InShell ? p.ShellMods[k] : (byte)0); // (its kit: the Shell Gantry, protocol 370)
    }

    /// <summary>
    /// Server, a ground drill scoop at <paramref name="at"/> by someone in a shell with a drill arm: the deposit under it is tapped
    /// into the hopper, as a Drill Module of the arm's tier would (its spot, its tier, one material at a time). The scoop itself
    /// digs as ever. The rate is the arm's units a second, by the time between scoops (at most DrillGap a scoop). Says why not now and then; nothing when there is no deposit there.
    /// </summary>
    private void ShellDrillTap(Player p, Vector3 at, double now)
    {
        if (!p.InShell || ShellFrames.Worn(p).Find(ShellModuleKind.Drill) is not { } drill || Constructs.Resources is not { } field)
            return;
        ref var hp = ref SandboxHands.Hoppers[p.Id];
        if (hp.Capacity <= 0 || hp.Module == null)
            return;
        var round = field.Planet != null;
        if ((round ? field.TappableAt(at) : field.TappableAt(at.X, at.Z)) is not { } kind)
            return;
        var mat = RecyclingTable.Current.IdOf(kind.Material);
        var why = field.WorkedOut(at) ? DrillWhy.WorkedOut
            : drill.Tier < kind.MinTier ? DrillWhy.Tier
            : kind.Deep ? DrillWhy.Deep
            : !HopperTakes(hp.Module, mat) ? DrillWhy.Takes
            : hp.Amount > 0 && hp.Material != mat ? DrillWhy.Other
            : hp.Amount >= hp.Capacity ? DrillWhy.Full
            : DrillWhy.None;
        if (why == DrillWhy.None)
        {
            // By time, not by scoop: the arm taps its rate a second while it is held on the ore, however fast the drill motor scoops.
            var dt = Math.Clamp(now - _drillAt[p.Id], 0, DrillGap);
            _drillAt[p.Id] = now;
            _drillAcc[p.Id] += (float)(drill.Rate * dt);
            var whole = (int)_drillAcc[p.Id];
            if (whole <= 0)
                return;
            _drillAcc[p.Id] -= whole;
            var want = Math.Min(whole, hp.Capacity - hp.Amount);
            var got = round ? field.Tap(at, want, out var m) : field.Tap(at.X, at.Z, want, out m);
            if (got > 0)
            {
                hp.Material = m;
                hp.Amount += got;
                return;
            }
            why = DrillWhy.WorkedOut;
        }
        if (now - _drillSaid[p.Id] <= 3)
            return; // (the line is made only when it is said: nothing allocates while a full hopper keeps scooping)
        _drillSaid[p.Id] = now;
        var names = RecyclingTable.Current;
        SandboxHands.Notices.Add((p.Id, why switch
        {
            DrillWhy.WorkedOut => "this spot is worked out: drill a few metres on",
            DrillWhy.Tier => $"the drill arm can't work {kind.Name} (that takes a tier {kind.MinTier} drill)",
            DrillWhy.Deep => $"the {kind.Name} lies under the rock: a Drill Module bores down to it",
            DrillWhy.Takes => $"the hopper doesn't hold {kind.Material}",
            DrillWhy.Other => $"the hopper holds {names.NameOf(hp.Material)}: tip it out first (F at a bin or a refinery)",
            _ => $"the hopper is full ({hp.Capacity}): F at a bin, a Storage Volume or a refinery tips it",
        }));
    }

    private enum DrillWhy : byte { None, WorkedOut, Tier, Deep, Takes, Other, Full }

    private readonly Vector3[] _boreFrom = new Vector3[256]; // where a drill arm's bore into an ore spot began (the ground it first struck there)
    private readonly bool[] _boring = new bool[256];

    /// <summary>
    /// Server: a scoop by a drill arm boring into ore (the user, 2026-11-17: a sustained dig taps the arm's whole rate). Only where the
    /// point is over a deposit the arm can tap, and only once the bore is <see cref="DrillShaft.Depth"/> deep, measured from where this
    /// bore first struck the ground (a scoop more than a spot's width across starts a new bore): then the hole ends there, as a Drill
    /// Module's does, so the crosshair stays on its floor within reach and the arm taps on, and a player in it can still mantle out.
    /// Anywhere else (a hillside, a cliff, a cave, ground with no ore) the scoop digs as the ground drill's does.
    /// </summary>
    private bool ShellDrillBored(Player p, Vector3 at)
    {
        if (!p.InShell || p.ShellFrame == 0 || ShellFrames.Worn(p).Find(ShellModuleKind.Drill) is not { } drill || Constructs.Resources is not { } field)
            return false;
        var kind = field.Planet != null ? field.TappableAt(at) : field.TappableAt(at.X, at.Z);
        if (kind == null || kind.MinTier > drill.Tier || kind.Deep)
        {
            _boring[p.Id] = false;
            return false;
        }
        var up = GravityFrames.UpAt(at);
        var d = at - _boreFrom[p.Id];
        var across = d - up * Vector3.Dot(d, up);
        if (!_boring[p.Id] || across.LengthSquared() > ResourceField.SpotSize * ResourceField.SpotSize)
        {
            (_boreFrom[p.Id], _boring[p.Id]) = (at, true);
            return false;
        }
        return Vector3.Dot(_boreFrom[p.Id] - at, up) > DrillShaft.Depth;
    }

    /// <summary>
    /// Both sides: a tool asked for by someone in a shell whose frame forces one (a module's "tool", Constructs/Shells.json: the Mining
    /// Shell's drill arm is its only tool; the user, 2026-11-17). Null: allowed (no forced tool, or the forced one).
    /// </summary>
    internal static string? ShellToolRefusal(Player p, ConstructTool asked)
    {
        if (!p.InShell || p.ShellFrame == 0)
            return null;
        var f = ShellFrames.Worn(p);
        if (f.ForcedTool is not { } only || asked == only)
            return null;
        return $"the {f.Part}'s only tool is its {f.ToolModule!.Name}: leave the shell for your own tools";
    }

    /// <summary>Server, from <see cref="ShellTick"/>: a worn shell with a forced tool has it out (after a seat, a part picked up, a wake).</summary>
    private void ShellForceTool(Player p)
    {
        if (p.ShellFrame == 0 || ShellFrames.Worn(p).ForcedTool is not { } only)
            return;
        ref var h = ref SandboxHands.Hand(p.Id);
        if (h.Tool != only && !h.Seated)
            SandboxHands.SetTool(p.Id, only, Constructs, p.Eye, p.Yaw);
    }

    private static bool HopperTakes(ShellModule mod, byte material)
    {
        var names = RecyclingTable.Current;
        foreach (var t in mod.Takes)
            if (names.IdOf(t) == material)
                return true;
        return false;
    }

    private SbText _sbHopper;

    /// <summary>The HUD's hopper line while this PC's player wears a shell with one (from <see cref="ShellHud"/>).</summary>
    private void HopperHud(Player me, List<HudLine> lines, Vector4 dim)
    {
        if (ShellFrames.Worn(me).Find(ShellModuleKind.Hopper) == null)
            return;
        var key = me.HopperAmount * 256 + me.HopperMaterial;
        if (_sbHopper.Stale(key))
            _sbHopper.Text = me.HopperAmount > 0
                ? $"ORE HOPPER  {me.HopperAmount} {RecyclingTable.Current.NameOf(me.HopperMaterial)}  -  drill ore with the ground drill, F at a bin or refinery tips it"
                : "ORE HOPPER  empty  -  the ground drill over an ore deposit drills into it";
        lines.Add(new HudLine(_sbHopper.Text, dim));
    }
}
