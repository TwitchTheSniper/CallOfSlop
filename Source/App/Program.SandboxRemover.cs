using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The remover tool on this PC (board item dev-sandbox-remover-tool-rt01; ConstructRemover.cs holds the selection and the
/// controller): reads the keys (LMB, RMB, Shift), asks the same crosshair ray as every tool, and hands them to the controller.
/// Esc cancels the delete question (not the match), putting the tool away or leaving Sandbox ends the question, and a new
/// game forgets the selection.
/// </summary>
public static partial class Program
{
    private static readonly RemoverController _remover = new() { ContestedOf = RemoverContested };
    private static Game? _removerGame;

    internal static RemoverController Remover => _remover;

    private static string? RemoverContested(Game g, Construct c, byte player) => AimContested(g, c, player);

    /// <summary>Every frame in Sandbox (from SandboxInput): a different game or a match that is not running forgets the selection; any tool but the remover ends the question.</summary>
    internal static void SandboxRemoverTick(Game g, bool toolOut)
    {
        if (!ReferenceEquals(_removerGame, g) || g.Phase != MatchPhase.Playing)
        {
            _removerGame = g;
            _remover.Reset();
        }
        if (!toolOut && (_remover.PromptPending || g.SandboxPrompt.Length > 0 || g.SandboxRemoverHint.Length > 0))
            _remover.Idle(g);
    }

    /// <summary>The remover's frame with the tool out and the mouse captured (from SandboxInput, with the crosshair's hit).</summary>
    private static void SandboxRemoverInput(Game g, Player me, bool aimed, in ConstructHit hit)
    {
        var shift = _window.GetKey(GlfwKey.LeftShift) == GlfwKeyAction.Press || _window.GetKey(GlfwKey.RightShift) == GlfwKeyAction.Press;
        var inp = new RemoverInput
        {
            FireDown = Down(GameAction.Fire) && !_swallowClick,
            FirePressed = Pressed(GameAction.Fire) && !_swallowClick,
            AimPressed = Pressed(GameAction.Aim),
            Shift = shift,
            NowMs = Environment.TickCount64,
        };
        _remover.Frame(g, me, in inp, aimed, in hit);
    }

    /// <summary>
    /// The remover's frame from an eye and a look (what SandboxInput does with the keys it read): the crosshair's ray, then the
    /// controller. The self-test drives the tool through this with scripted keys.
    /// </summary>
    internal static void SandboxRemoverFrame(Game g, Player me, in RemoverInput inp, Vector3 eye, Vector3 dir)
    {
        var wall = World.RaycastWorld(eye, dir, ConstructBuild.Reach + 1);
        var aimed = g.Constructs.Raycast(eye, dir, MathF.Min(wall, ConstructBuild.Reach + 1), out var hit);
        ConstructRender.ClearAim();
        _remover.Frame(g, me, in inp, aimed, in hit);
    }

    /// <summary>Esc: the delete question goes (and the key is used up); otherwise the key goes on to the pause menu.</summary>
    private static bool RemoverEsc() => _game is { SandboxOn: true } g && _remover.Cancel(g);
}
