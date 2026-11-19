using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Weld seams on this PC (user, 2026-11-19; ConstructSeams.cs): with the attach tool out, the seam between the aimed part and
/// its neighbour nearest the crosshair is lit (SocketWeldRender.DrawSeam) and RMB breaks only that seam; Shift+RMB takes
/// the whole part off as before. The look line names both parts. The server decides (CActKind.SeamBreak). The seam is
/// worked out again only when the aim moves 2 cm on the part or the body changes, so nothing allocates per frame.
/// </summary>
public static partial class Program
{
    private static long _seamKey = long.MinValue, _seamTextKey = long.MinValue;
    private static int _seamOther = -1, _seamSocket = -1;
    private static string? _seamWhy;
    private static Vector3 _seamMin, _seamMax;
    private static string _seamText = "";

    /// <summary>Every frame with the attach tool out, before the take-off check: lights the seam under the crosshair and sends its break on RMB (then the press is spent).</summary>
    private static void SandboxWeldSeamInput(Game g, Player me, in SandboxHand h, bool aimed, in ConstructHit hit)
    {
        if (h.Tool != ConstructTool.Welder || h.Seated || !aimed || hit.SlotPlus1 != 0 || hit.RoutePlus1 != 0 || hit.Distance > ConstructBuild.Reach
            || hit.Body.Loose || hit.Part < 0 || hit.Part >= hit.Body.PartCount || SocketWeldBusy || PointWeldBusy)
            return;
        var shift = Down(GameAction.Sprint) || SweepPointHeld;
        if (shift)
            return; // (Shift+RMB: the old take-off of the whole part, below)
        var c = hit.Body;
        var seg = c.Parts[hit.Part].Seg;
        c.WorldFrame(seg, out var q, out var t);
        var rest = Vector3.Transform(hit.Point - t, Quaternion.Conjugate(q));
        var qx = (int)MathF.Round(rest.X * 50) & 0x3ff;
        var qy = (int)MathF.Round(rest.Y * 50) & 0x3ff;
        var qz = (int)MathF.Round(rest.Z * 50) & 0x3ff;
        var key = ((((long)c.Id * 65537 + hit.Part) * 1_000_003 + c.RebuildStamp) * 131 + c.LinkCount) * 1_000_003 + (qx | (long)qy << 10 | (long)qz << 20);
        if (key != _seamKey)
        {
            _seamKey = key;
            _seamOther = PartSeams.Nearest(c, hit.Part, rest, out _seamSocket);
            if (_seamOther >= 0)
            {
                PartSeams.Face(c, hit.Part, _seamOther, _seamSocket, out _seamMin, out _seamMax);
                _seamWhy = PartSeams.Refusal(c, hit.Part, _seamOther);
            }
        }
        if (_seamOther < 0 || _seamOther >= c.PartCount)
            return; // (no seam: RMB takes it off as before)
        var why = _seamWhy ?? (c.Driver != 255 ? "someone is driving it" : null) ?? ConstructEdits.SalvageRefusal(c, me.Id) ?? AimContested(g, c, me.Id);
        SocketWeldRender.SeamBody = c;
        SocketWeldRender.SeamSeg = seg;
        SocketWeldRender.SeamMin = _seamMin;
        SocketWeldRender.SeamMax = _seamMax;
        SocketWeldRender.SeamOk = why == null;
        if (SocketWeldRender.Hot < 0) // (a socket marker under the crosshair keeps the socket attach's line)
        {
            var tk = key * 31 + SandboxKeys.Stamp + (why?.GetHashCode() ?? 7) * 1_000_033L;
            if (tk != _seamTextKey)
            {
                _seamTextKey = tk;
                var a = c.DefOf(hit.Part).Name;
                var b = c.DefOf(_seamOther).Name;
                _seamText = why != null
                    ? SandboxKeys.Line($"Seam {a} - {b}", null, why, SandboxKeys.Do($"{SandboxKeys.Sprint}+{SandboxKeys.Aim}", "take the part off"))
                    : SandboxKeys.Line($"Seam {a} - {b}", null, null, $"{SandboxKeys.Do(SandboxKeys.Aim, "break this seam")}, {SandboxKeys.Do($"{SandboxKeys.Sprint}+{SandboxKeys.Aim}", "take the part off")}");
            }
            g.SandboxLookText = _seamText;
        }
        if (!Pressed(GameAction.Aim))
            return;
        _down[(int)GameAction.Aim] = false; // (not also taking the part off)
        if (why != null)
        {
            g.SandboxSay(why);
            BuildSounds.Play(BuildSound.Refuse, default);
            return;
        }
        g.SandboxSend(CActKind.SeamBreak, body: c.Id, part: (ushort)hit.Part, socket: (ushort)_seamOther);
        _seamKey = long.MinValue; // (the body changes: looked for again)
    }
}
