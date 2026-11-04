using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The selector on this PC (Source/Constructs/SelectController.cs holds the selection, the drags and the ghost): reads the keys,
/// casts the crosshair out to the station's reach, and hands them over. Its marquee takes the mouse while RMB is held. A new game
/// forgets the selection; a tool change ends the drag and the ghost (a transition, not a frame: a stamp picked from the Tab
/// inventory is kept while its tool comes out).
/// </summary>
public static partial class Program
{
    private static readonly SelectController _select = new();
    private static Game? _selectGame;
    private static double _selectScroll;

    internal static SelectController Selector => _select;

    /// <summary>Every frame in Sandbox: a different game or a match that isn't running forgets the selection; a blueprint on its way moves on.</summary>
    internal static void SandboxSelectTick(Game g, bool toolOut)
    {
        if (!ReferenceEquals(_selectGame, g) || g.Phase != MatchPhase.Playing)
        {
            _selectGame = g;
            _select.Reset();
        }
        _select.Tick(g, toolOut, Environment.TickCount64);
    }

    /// <summary>The selector's frame with the tool out and the mouse captured (from SandboxInput).</summary>
    private static void SandboxSelectInput(Game g, Player me, Vector3 eye, Vector3 dir, double now)
    {
        var reach = ConstructEdits.AidReach;
        var surface = World.Query.Raycast(eye, dir, reach, QueryFilter.All, out var s);
        var wall = surface ? s.Distance : reach;
        var aimed = g.Constructs.Raycast(eye, dir, wall, out var hit, noTracks: true);
        if (aimed)
            surface = false;
        TurnKey? turn = Pressed(GameAction.RotateLeft) ? TurnKey.Left : Pressed(GameAction.RotateRight) ? TurnKey.Right : Pressed(GameAction.RotateUp) ? TurnKey.Up : Pressed(GameAction.RotateDown) ? TurnKey.Down
            : Pressed(GameAction.RollLeft) ? TurnKey.RollLeft : Pressed(GameAction.RollRight) ? TurnKey.RollRight : null;
        var inp = new SelectInput
        {
            FireDown = Down(GameAction.Fire) && !_swallowClick,
            FirePressed = Pressed(GameAction.Fire) && !_swallowClick,
            AimDown = Down(GameAction.Aim),
            AimPressed = Pressed(GameAction.Aim),
            Shift = ShiftHeld,
            Move = Pressed(GameAction.SelectMove),
            Copy = Pressed(GameAction.SelectCopy),
            Paint = Pressed(GameAction.SelectPaint),
            Weld = Pressed(GameAction.SelectWeld),
            Delete = Pressed(GameAction.SandboxDrop),
            Save = Pressed(GameAction.SelectSave),
            Reload = Pressed(GameAction.Reload),
            SnapSwitch = Pressed(GameAction.SnapSwitch),
            Turn = turn,
            CamRight = Vector3.Normalize(Vector3.Cross(dir, GravityFrames.UpAt(eye))),
            CamUp = GravityFrames.UpAt(eye),
            Scroll = (float)_selectScroll,
            NowMs = Environment.TickCount64,
            Screen = ScreenSize,
            ViewProj = Gfx.ViewProj,
            Colour = _paintWheel.Colour,
            PaintMode = _paintWheel.Mode == PaintMode.Reset ? PaintMode.Reset : PaintMode.Part,
        };
        _selectScroll = 0;
        _select.Frame(g, me, in inp, eye, dir, aimed, in hit, surface, surface ? s.Point : default, surface ? s.Normal : default);
        if (_select.WantSave is { } clip)
        {
            _select.WantSave = null;
            Ui.OpenBlueprintSave(clip);
            SetMouseCaptured(false);
        }
        ConstructRender.SelGhostClip = _select.GhostOn ? _select.Clip : null;
        ConstructRender.SelGhostPos = _select.GhostPos;
        ConstructRender.SelGhostRot = _select.GhostRot;
        ConstructRender.SelGhostValid = _select.GhostOk;
    }

    /// <summary>The mouse with the marquee down: the view holds still. True: this frame's mouse went to it.</summary>
    private static bool SelectMouse(double2 delta)
    {
        if (_game is not { SandboxOn: true } g || g.Local is not { Alive: true } me || g.SandboxHands.Hand(me.Id).Tool != ConstructTool.Selector || !_mouseCaptured || MenusOpen)
            return false;
        return _select.Mouse(new Vector2((float)delta.X, (float)delta.Y), Down(GameAction.Aim));
    }

    /// <summary>The wheel with a ghost out: its distance.</summary>
    private static bool SelectScroll(double y)
    {
        if (_select.Mode == SelectController.Placing.None)
            return false;
        _selectScroll += y;
        return true;
    }

    /// <summary>Esc: the ghost or the question goes (and the key is used up).</summary>
    private static bool SelectEsc() => _game is { SandboxOn: true } g && _select.Cancel(g);

    /// <summary>The ImGui pass: the save window, and the marquee on the screen.</summary>
    private static void DrawSelectInMatch()
    {
        if (Ui.BlueprintSaveOpen)
        {
            var gone = _session == null || _game is not { SandboxOn: true } g || g.Local is not { Alive: true } || g.Phase != MatchPhase.Playing;
            var said = "";
            var open = !gone && Ui.DrawBlueprintSave(out said);
            if (!open)
            {
                if (gone)
                    Ui.CloseBlueprintSave();
                else if (said.Length > 0)
                    _game!.SandboxSay(said);
                if (!MenusOpen)
                    SetMouseCaptured(true);
                _swallowClick = true; // (the click that closed it mustn't also pick)
            }
        }
        if (!_select.MarqueeShown)
            return;
        var dl = ImGui.GetForegroundDrawList();
        var a = _select.MarqueeA;
        var b = _select.MarqueeB;
        var min = new float2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
        var max = new float2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
        var fill = new ImColor8(90, 190, 255, 40);
        var line = new ImColor8(90, 190, 255, 230);
        ImDrawListExtensions.AddRectFilled(dl, min, max, fill, 0f, ImDrawFlags.None);
        ImDrawListExtensions.AddRectFilled(dl, min, new float2(max.X, min.Y + 1.5f), line, 0f, ImDrawFlags.None);
        ImDrawListExtensions.AddRectFilled(dl, new float2(min.X, max.Y - 1.5f), max, line, 0f, ImDrawFlags.None);
        ImDrawListExtensions.AddRectFilled(dl, min, new float2(min.X + 1.5f, max.Y), line, 0f, ImDrawFlags.None);
        ImDrawListExtensions.AddRectFilled(dl, new float2(max.X - 1.5f, min.Y), max, line, 0f, ImDrawFlags.None);
    }
}
