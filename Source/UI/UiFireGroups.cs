using System.Text;

namespace CallOfSlop;

public sealed partial class Ui
{
    private readonly StringBuilder _feedLine = new(64);

    /// <summary>
    /// The HUD's line under the ammo count for a volley build whose receivers fire at their own rates: each magazine's
    /// rounds in receiver order ("12 | 30 | RLD 40%"), a reloading one with its progress; the first six, then "+N".
    /// </summary>
    private string FeedLine(ReceiverFire[] fg, FireGroupState gs, double now)
    {
        var sb = _feedLine.Clear();
        var feeds = FireGroupState.FeedsOf(fg);
        var shown = Math.Min(feeds, 6);
        for (var f = 0; f < shown; f++)
        {
            if (f > 0)
                sb.Append(" | ");
            if (gs.ReloadUntil[f] > now)
            {
                var span = Math.Max(gs.ReloadUntil[f] - gs.ReloadStarted[f], 0.01);
                sb.Append("RLD ").Append((int)(Math.Clamp((now - gs.ReloadStarted[f]) / span, 0, 1) * 100)).Append('%');
            }
            else
                sb.Append(gs.Ammo[f]);
        }
        if (feeds > shown)
            sb.Append("  +").Append(feeds - shown);
        return sb.ToString();
    }
}
