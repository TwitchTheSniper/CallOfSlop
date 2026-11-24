namespace CallOfSlop;

/// <summary>
/// A billet press's step (an Extruder Core on Die Blocks; the Extruder Lab's direct press, Sean 2026-11-24): a store's
/// <see cref="CStore.Blocked"/> byte, on the wire, so a client draws what the host does (ExtruderRender.cs).
/// </summary>
public enum PressPhase : byte
{
    Idle, // no charge in the container yet (a new machine): it fills one first
    Fill, // the chute fills the heated feed throat over the container's back end (its sight glass shows it)
    Load, // the throat's slide gate opens and the charge drops into the container
    Upset, // the ram comes up and upsets the charge in the container
    Extrude, // the ram pushes it through the die: the piece grows
    Saw, // the hot saw runs across and parts the piece off (the cut lands at its end)
    Back, // the ram goes back to its rest face
}

/// <summary>
/// The billet press cycle of an Extruder on Die Blocks (docs/sandbox/constructs/production.md "Extrusion"). Each piece is one charge:
/// the chute fills the feed throat, its gate drops the charge into the container, the ram upsets it and pushes it through the die while
/// the piece grows; when the piece is due off the hot saw runs across it and the cut lands as the saw finishes; then the ram goes back
/// and the next charge fills. The step's seconds are part data (<c>storage.extrude.press</c>). A Core on its own die has no press: it
/// runs on as it always did. Server only, at the Extruder's 10 Hz step; the phase is the store's <see cref="CStore.Blocked"/> byte.
/// </summary>
public static partial class ConstructExtruder
{
    /// <summary>Is this machine a billet press: an Extruder Core with Die Blocks in its die and a press cycle in its part data?</summary>
    public static bool IsPress(Construct c, int part)
    {
        var d = c.Parts[part].D;
        return d.ExtPress != null && d.BlockRole == BlockRole.Core && !d.IsWheelFormer && !d.IsGrowFormer && !d.IsPanelFormer && !d.IsLaserCutter
            && MachineBlocks.TryOf(c, part, out var asm) && asm.W > 0;
    }

    /// <summary>The press's step now (Idle for a machine that isn't one).</summary>
    public static PressPhase PhaseOf(CStore st) => st.Blocked <= (byte)PressPhase.Back ? (PressPhase)st.Blocked : PressPhase.Idle;

    /// <summary>How long a step takes (s), from the part's data.</summary>
    public static float PhaseSeconds(ConstructPartDef d, PressPhase ph) =>
        d.ExtPress is { } t && ph >= PressPhase.Fill && ph <= PressPhase.Back && ph != PressPhase.Extrude ? t[ph switch
        {
            PressPhase.Fill => 0,
            PressPhase.Load => 1,
            PressPhase.Upset => 2,
            PressPhase.Saw => 3,
            _ => 4,
        }] : 0;

    private static void SetPhase(ConstructWorld world, CStore st, PressPhase ph)
    {
        st.PressTicks = 0;
        if (st.Blocked == (byte)ph)
            return;
        st.Blocked = (byte)ph;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    /// <summary>A step's time on: true once it has run its seconds.</summary>
    private static bool PhaseDone(ConstructPartDef d, CStore st, PressPhase ph)
    {
        st.PressTicks += Every;
        return st.PressTicks >= PhaseSeconds(d, ph) * World.TickRate - 0.5f;
    }

    /// <summary>
    /// The saw, before a due piece comes off: true while it still runs (the piece waits on the die, the cut asked for kept). Once it has run,
    /// false: the cut goes ahead as it always did, and while the piece waits on the die after it (a full conveyor, a press's bay) the saw stays done.
    /// </summary>
    private static bool Sawing(ConstructWorld world, Construct c, int part, CStore st, bool asked)
    {
        var d = c.Parts[part].D;
        if (PhaseOf(st) != PressPhase.Saw)
            SetPhase(world, st, PressPhase.Saw);
        else if (st.PressTicks >= PhaseSeconds(d, PressPhase.Saw) * World.TickRate - 0.5f || PhaseDone(d, st, PressPhase.Saw))
            return false;
        if (asked)
            c.Parts[part].State |= CutPending; // (a Cut signal is kept until the saw is through)
        ConstructGatherer.SetBusy(world, st, true);
        SetStall(world, st, ExtrudeStall.None);
        return true;
    }

    /// <summary>
    /// The steps before a piece grows: the ram back, the charge filled, loaded and upset. True while one runs (the machine is busy and
    /// nothing grows); false once the press is charged and the piece may grow. <paramref name="product"/>: the piece on the die, or -1.
    /// </summary>
    private static bool Charging(ConstructWorld world, Construct c, int part, CStore st, int product)
    {
        var d = c.Parts[part].D;
        var ph = PhaseOf(st);
        if (product >= 0)
        {
            if (ph != PressPhase.Extrude)
                SetPhase(world, st, PressPhase.Extrude); // (a piece already on the die: a world saved mid-piece, a machine just welded up, or a cut no longer due)
            return false;
        }
        if (ph == PressPhase.Extrude && product < 0 && st.PressTicks == 0)
            return false; // (charged: its piece starts now)
        if (ph is PressPhase.Saw or PressPhase.Extrude)
            SetPhase(world, st, ph = PressPhase.Back); // (the piece went: off the saw, by a press's chute, or taken)
        if (!ConstructRecycler.Powered(world, c, part))
        {
            ConstructGatherer.SetBusy(world, st, false);
            SetStall(world, st, ExtrudeStall.NoPower);
            return true;
        }
        if (ph == PressPhase.Idle)
            SetPhase(world, st, ph = PressPhase.Fill);
        if (ph == PressPhase.Fill && st.Amount <= 0)
        {
            ConstructGatherer.SetBusy(world, st, false);
            SetStall(world, st, ExtrudeStall.NoMaterial); // (the throat fills only from what the bin holds)
            return true;
        }
        if (PhaseDone(d, st, ph))
            SetPhase(world, st, ph = ph switch { PressPhase.Back => PressPhase.Fill, PressPhase.Fill => PressPhase.Load, PressPhase.Load => PressPhase.Upset, _ => PressPhase.Extrude });
        ConstructGatherer.SetBusy(world, st, true);
        SetStall(world, st, ExtrudeStall.None);
        return ph != PressPhase.Extrude;
    }

    /// <summary>The step's name for the look line and the screen.</summary>
    public static string PhaseName(PressPhase ph) => ph switch
    {
        PressPhase.Fill => "filling the charge",
        PressPhase.Load => "loading the charge",
        PressPhase.Upset => "upsetting the charge",
        PressPhase.Saw => "sawing",
        PressPhase.Back => "ram back",
        _ => "extruding",
    };

    /// <summary>Where the press cuts its piece now (cm; 0: it grows to its cap): its order's length or Cut every, inside what it reaches.</summary>
    public static int PressCutCm(Construct c, int part) => CutDueCm(c, part, c.Parts[part], c.Parts[part].D);
}
