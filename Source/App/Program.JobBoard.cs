using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The job board (Source/UI/UiJobBoard.cs, docs/story/STORY.md "The job board"): the use key on a lore item with
/// <c>"opens": "jobs"</c> (the Ministry Work Board in the colony yard) opens every faction's jobs with the cursor free, and
/// sends the ordinary use act so the host counts the look (Game/GameStoryLore.cs). The use key again, Esc, the window's X
/// or walking off closes it. It counts as a menu.
/// </summary>
public static partial class Program
{
    private static ushort _jobBoardBody;
    private static int _jobBoardPart;

    /// <summary>From <see cref="LoreFrame"/>: the use key on the board.</summary>
    private static void JobBoardOpen(Game g, int item, in ConstructHit hit)
    {
        (_jobBoardBody, _jobBoardPart) = (hit.Body.Id, hit.Part);
        Ui.OpenJobBoard(Game.StoryData.Lore.Items[item]);
        SetJobBoardOpen(true);
        g.SandboxSend(CActKind.Use, body: hit.Body.Id, part: (ushort)hit.Part);
    }

    private static void SetJobBoardOpen(bool open)
    {
        Ui.JobBoardOpen = open;
        if (!open)
            _jobBoardBody = 0;
        if (_session == null)
            return;
        if (open)
            SetMouseCaptured(false);
        else if (CursorPolicy.MayCapture(CursorNow()))
        {
            SetMouseCaptured(true);
            _swallowClick = true;
        }
    }

    /// <summary>A key press while the board is up: Esc or the use key closes it. True when it used the key.</summary>
    private static bool JobBoardKey(GlfwKey key)
    {
        if (!Ui.JobBoardOpen || Ui.WantsTextInput)
            return false;
        if (key != GlfwKey.Escape && (key == GlfwKey.Unknown || !_settings.HasBind(GameAction.Interact, Keybinds.Key(key))))
            return false;
        SetJobBoardOpen(false);
        return true;
    }

    /// <summary>The board's window while its part is still there and near (closed: one check).</summary>
    private static void DrawJobBoardUi()
    {
        if (!Ui.JobBoardOpen)
            return;
        var g = _session;
        if (g is not { SandboxOn: true } || g.Local is not { Alive: true } me
            || _jobBoardBody != 0 && (g.Constructs.Get(_jobBoardBody) is not { } c || _jobBoardPart >= c.PartCount
                || Vector3.DistanceSquared(me.Position, c.ToWorld(c.Parts[_jobBoardPart].Pos)) > LoreLeave * LoreLeave))
        {
            SetJobBoardOpen(false);
            return;
        }
        Ui.DrawJobBoard(g, _settings.Bind(GameAction.Interact));
        if (!Ui.JobBoardOpen)
            SetJobBoardOpen(false); // closed with its own X
    }
}
