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
    private static bool _seamHeld; // the RMB that broke a seam is still down
    private static Vector3 _seamMin, _seamMax;
    private static string _seamText = "";

    /// <summary>
    /// Every frame with the attach tool out, before the take-off check: lights the seam under the crosshair and sends its break on
    /// RMB. True: this frame's press was the seam's (the take-off below must not also act on it). One press breaks one seam: the
    /// press is left as it is (so held, it is not a new press next frame), and <see cref="_seamHeld"/> keeps a held RMB that broke a
    /// seam from turning into a take-off until it is let go.
    /// </summary>
    private static bool SandboxWeldSeamInput(Game g, Player me, in SandboxHand h, bool aimed, in ConstructHit hit)
    {
        if (_seamHeld)
        {
            if (Down(GameAction.Aim) && !Pressed(GameAction.Aim))
                return true; // (still the press that broke a seam; a new press, let go out of this path, is new)
            _seamHeld = false;
        }
        if (h.Tool != ConstructTool.Welder || h.Seated || !aimed || hit.SlotPlus1 != 0 || hit.RoutePlus1 != 0 || hit.Distance > ConstructBuild.Reach
            || hit.Body.Loose || hit.Part < 0 || hit.Part >= hit.Body.PartCount || SocketWeldBusy || PointWeldBusy)
            return false;
        var shift = Down(GameAction.Sprint) || SweepPointHeld;
        if (shift)
            return false; // (Shift+RMB: the old take-off of the whole part, below)
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
            return false; // (no seam: RMB takes it off as before)
        var why = _seamWhy ?? (c.Driver != 255 ? "someone is driving it" : null) ?? ConstructEdits.SalvageRefusal(c, me.Id) ?? AimContested(g, c, me.Id);
        SocketWeldRender.SeamBody = c;
        SocketWeldRender.SeamSeg = seg;
        SocketWeldRender.SeamMin = _seamMin;
        SocketWeldRender.SeamMax = _seamMax;
        SocketWeldRender.SeamOk = why == null;
        // The look line names both parts; on a socket marker the socket attach's line comes first, the seam's after it.
        var before = SocketWeldRender.Hot >= 0 ? g.SandboxLookText : null;
        var tk = key * 31 + SandboxKeys.Stamp + (why?.GetHashCode() ?? 7) * 1_000_033L + (before != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(before) * 7919L : 0);
        if (tk != _seamTextKey)
        {
            _seamTextKey = tk;
            var a = c.DefOf(hit.Part).Name;
            var b = c.DefOf(_seamOther).Name;
            var take = SandboxKeys.Do($"{SandboxKeys.Sprint}+{SandboxKeys.Aim}", "take the part off");
            _seamText = why != null
                ? SandboxKeys.Line($"Seam {a} - {b}", null, why, take)
                : SandboxKeys.Line($"Seam {a} - {b}", null, null, $"{SandboxKeys.Do(SandboxKeys.Aim, "break this seam")}, {take}");
            if (before is { Length: > 0 })
                _seamText = $"{before}  |  {_seamText}";
        }
        g.SandboxLookText = _seamText;
        if (!Pressed(GameAction.Aim))
            return false;
        _seamHeld = true; // (held on, it is neither another seam nor a take-off)
        if (why != null)
        {
            g.SandboxSay(why);
            BuildSounds.Play(BuildSound.Refuse, default);
            return true;
        }
        g.SandboxSend(CActKind.SeamBreak, body: c.Id, part: (ushort)hit.Part, socket: (ushort)_seamOther);
        _seamKey = long.MinValue; // (the body changes: looked for again)
        return true;
    }
}
