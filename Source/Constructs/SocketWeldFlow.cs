using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's three-step socket weld as this PC walks it (board item dev-sandbox-socket-weld-3step-sw01; the input is
/// Program.SandboxSocketWeld.cs, the rules SocketWeld.cs): step 1 picks the source socket on the piece to move, step 2
/// the target socket or face point on the construct (smart rotation's best guess is the start angle), step 3 spins the
/// piece about the socket axis (15 degree snaps, Shift free, Ctrl 5 degrees; R the next ranked facing) until LMB confirms.
/// RMB steps back (3 to 2 to 1 to nothing), Esc cancels. Kept apart from the input so the self-test walks it headless.
/// Nothing allocates.
/// </summary>
public sealed class SocketWeldFlow
{
    public enum Stage : byte
    {
        Source, // step 1: pick a socket on the piece to move
        Target, // step 2: pick where it goes on the construct
        Rotate, // step 3: spin it, LMB welds it
    }

    public Stage Step;
    public ushort Source;
    public int SourcePart = -1, SourceSocket = -1;
    public WeldTarget Target;
    public float Angle; // degrees: as shown and sent (snapped)
    public float StartAngle; // smart rotation's guess for this target
    public readonly int[] Variants = new int[Rot24.Count];
    public readonly byte[] Rots = new byte[Rot24.Count];
    public int Ranked, RankPos;
    private float _raw;

    public void Cancel()
    {
        Step = Stage.Source;
        Source = 0;
        SourcePart = SourceSocket = -1;
        Target = default;
        Angle = StartAngle = _raw = 0;
        Ranked = RankPos = 0;
    }

    /// <summary>Step 1: the piece's socket is picked (it stays where it is until the weld is confirmed).</summary>
    public void PickSource(ushort body, int part, int socket)
    {
        Cancel();
        Source = body;
        SourcePart = part;
        SourceSocket = socket;
        Step = Stage.Target;
    }

    /// <summary>The start angle for a target (step 2's preview, and step 3's start): smart rotation's best guess.</summary>
    public float Aim(Construct target, in WeldTarget w, Construct mover, SmartRotationMemory? memory)
    {
        if (Ranked > 0 && Target.Body == w.Body && Target.Part == w.Part && Target.Socket == w.Socket && Target.At == w.At && Target.Dir == w.Dir)
            return StartAngle; // (the same target: worked out once)
        Target = w;
        RankPos = 0;
        Ranked = SocketWeld.StartAngle(target, w, mover, SourcePart, SourceSocket, memory, Variants, Rots, out StartAngle);
        if (Ranked == 0)
            StartAngle = 0;
        if (Step != Stage.Rotate)
            Angle = _raw = StartAngle;
        return StartAngle;
    }

    /// <summary>Step 2: the target is picked; the spin starts at smart rotation's guess.</summary>
    public void PickTarget(Construct target, in WeldTarget w, Construct mover, SmartRotationMemory? memory)
    {
        Aim(target, w, mover, memory);
        Angle = _raw = StartAngle;
        Step = Stage.Rotate;
    }

    /// <summary>Step 3: spin by <paramref name="degrees"/> (mouse X or the wheel). True when it passed a multiple of 90 degrees (the tick).</summary>
    public bool Spin(float degrees, bool free, bool fine)
    {
        if (Step != Stage.Rotate || !float.IsFinite(degrees))
            return false;
        var before = Angle;
        _raw += degrees;
        if (MathF.Abs(_raw) > 1e5f)
            _raw = SocketWeld.Wrap(_raw);
        Angle = SocketWeld.Snap(_raw, free, fine);
        if (Angle == before)
            return false;
        // A tick when it lands on, or crosses, a multiple of 90.
        return SocketWeld.OnGrid(Angle) || MathF.Floor(before / 90f) != MathF.Floor(Angle / 90f);
    }

    /// <summary>R: the next facing smart rotation ranked (a one-part piece onto a socket), else half a turn (upside down).</summary>
    public void NextVariant(Construct mover, Construct target)
    {
        if (Step != Stage.Rotate)
            return;
        if (Ranked > 1 && SocketWeld.SourceFrame(mover, SourcePart, SourceSocket, out _, out var sDir) && SocketWeld.TargetFrame(target, Target, out _, out var tDir))
        {
            RankPos = (RankPos + 1) % Ranked;
            Angle = _raw = SocketWeld.AngleOfPartTurn(mover, SourcePart, sDir, tDir, Rots[RankPos]);
            return;
        }
        Angle = _raw = SocketWeld.Wrap(Angle + 180f);
    }

    /// <summary>RMB: one step back (3 to 2, 2 to 1, 1 to nothing). False when there was nothing to step back from.</summary>
    public bool Back()
    {
        switch (Step)
        {
            case Stage.Rotate:
                Step = Stage.Target;
                Angle = _raw = StartAngle;
                return true;
            case Stage.Target:
                Cancel();
                return true;
            default:
                return false;
        }
    }

    /// <summary>On confirm: the correction smart rotation learns (a grid angle onto a real socket that isn't its guess).</summary>
    public bool Learn(Construct target, Construct mover, SmartRotationMemory memory, Quaternion relRot)
    {
        if (Ranked == 0 || !SocketWeld.OnGrid(Angle) || Target.Socket == SocketWeld.FacePoint || mover.PartCount != 1)
            return false;
        var chosen = SocketWeld.PartTurnAt(mover, SourcePart, relRot);
        return SmartRotation.Placed(memory, target, mover.DefOf(SourcePart), Target.Part, Target.Socket, chosen, Rots[0]);
    }
}
