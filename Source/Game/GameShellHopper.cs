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
        var frame = ShellFrames.Of(p.ShellFrame);
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

    /// <summary>The frame, hopper material and load at the end of <see cref="Msg.Shell"/> (protocol 352).</summary>
    private static void ShellWriteFrame(PacketWriter w, Player p) => w.U8(p.InShell ? p.ShellFrame : (byte)0).U8(p.InShell ? p.HopperMaterial : (byte)0).U16(p.InShell ? p.HopperAmount : (ushort)0);

    /// <summary>
    /// Server, a ground drill scoop at <paramref name="at"/> by someone in a shell with a drill arm: the deposit under it is tapped
    /// into the hopper, as a Drill Module of the arm's tier would (its spot, its tier, one material at a time). The scoop itself
    /// digs as ever. The rate is the arm's units a second, by the time between scoops (at most DrillGap a scoop). Says why not now and then; nothing when there is no deposit there.
    /// </summary>
    private void ShellDrillTap(Player p, Vector3 at, double now)
    {
        if (!p.InShell || ShellFrames.Of(p.ShellFrame).Find(ShellModuleKind.Drill) is not { } drill || Constructs.Resources is not { } field)
            return;
        ref var hp = ref SandboxHands.Hoppers[p.Id];
        if (hp.Capacity <= 0 || hp.Module == null)
            return;
        var round = field.Planet != null;
        if ((round ? field.TappableAt(at) : field.TappableAt(at.X, at.Z)) is not { } kind)
            return;
        var names = RecyclingTable.Current;
        var mat = names.IdOf(kind.Material);
        var why = field.WorkedOut(at) ? "this spot is worked out: drill a few metres on"
            : drill.Tier < kind.MinTier ? $"the drill arm can't work {kind.Name} (that takes a tier {kind.MinTier} drill)"
            : kind.Deep ? $"the {kind.Name} lies under the rock: a Drill Module bores down to it"
            : !HopperTakes(hp.Module, mat) ? $"the hopper doesn't hold {kind.Material}"
            : hp.Amount > 0 && hp.Material != mat ? $"the hopper holds {names.NameOf(hp.Material)}: tip it out first (F at a bin or a refinery)"
            : hp.Amount >= hp.Capacity ? $"the hopper is full ({hp.Capacity}): F at a bin, a Storage Volume or a refinery tips it"
            : null;
        if (why == null)
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
            why = "this spot is worked out: drill a few metres on";
        }
        if (now - _drillSaid[p.Id] > 3)
        {
            _drillSaid[p.Id] = now;
            SandboxHands.Notices.Add((p.Id, why));
        }
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
        if (ShellFrames.Of(me.ShellFrame).Find(ShellModuleKind.Hopper) == null)
            return;
        var key = me.HopperAmount * 256 + me.HopperMaterial;
        if (_sbHopper.Stale(key))
            _sbHopper.Text = me.HopperAmount > 0
                ? $"ORE HOPPER  {me.HopperAmount} {RecyclingTable.Current.NameOf(me.HopperMaterial)}  -  drill ore with the ground drill, F at a bin or refinery tips it"
                : "ORE HOPPER  empty  -  the ground drill over an ore deposit drills into it";
        lines.Add(new HudLine(_sbHopper.Text, dim));
    }
}
