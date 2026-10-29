using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The dev tools from the game's side (Source/DevTools, Source/UI/UiDevTools.cs): the look-at inspector (F6, rebindable)
/// names what the crosshair is on, F5 copies its one-line reference to the clipboard for a Playtest Desk report, and F10
/// opens the prop editor on the prop under the crosshair (offline or hosting only; Esc or F10 closes it). Dev builds always
/// have them; a release build only with Settings > Graphics > "Allow the dev tools". Closed, they cost a few bool checks a frame.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>May this build use the dev tools?</summary>
    private static bool DevToolsAllowed => !BuildInfo.IsRelease || _settings.AllowDevTools;

    /// <summary>How far the inspector looks (metres).</summary>
    private const float LookAtRange = 400f;

    private static LookAtHit _devHit;
    private static string _devRef = "";
    private static List<string> _devDetails = new();
    private static (LookAtKind, int, int, int, Vector3) _devHitKey;
    private static Matrix4x4 _devViewProj;
    private static Vector3 _devEye, _devFwd;
    private static PropEditSession? _propEdit;

    /// <summary>A key press for the dev tools: returns true when it used the key.</summary>
    private static bool DevToolsKey(GlfwKey key)
    {
        if (key == GlfwKey.Escape && Ui.PropEditorOpen)
        {
            SetPropEditorOpen(false);
            return true;
        }
        if (key == GlfwKey.Unknown || Ui.ChatOpen || Ui.WantsTextInput || !DevToolsAllowed)
            return false;
        var code = Keybinds.Key(key);
        if (code == _settings.Bind(GameAction.LookAtInspector))
        {
            Ui.LookAtOpen = !Ui.LookAtOpen;
            _devHitKey = default;
            return true;
        }
        if (code == _settings.Bind(GameAction.CopyLookAt) && _session != null)
        {
            if (!Ui.LookAtOpen)
                ProbeLookAt(); // closed: one look, just for the copy
            SafeClipboard.Sink(_devRef);
            Ui.DevToolsMessage("copied: " + _devRef, Glfw.Time);
            return true;
        }
        if (code == _settings.Bind(GameAction.PropEditor) && _session != null)
        {
            if (Ui.PropEditorOpen)
            {
                SetPropEditorOpen(false);
                return true;
            }
            OpenPropEditor();
            return true;
        }
        return false;
    }

    /// <summary>Opens the editor on the prop under the crosshair (or the one already being edited).</summary>
    private static void OpenPropEditor()
    {
        if (_session is not { IsServer: true })
        {
            Ui.DevToolsMessage("the prop editor is for offline games and the host only", Glfw.Time);
            return;
        }
        ProbeLookAt();
        var map = World.Map;
        if (_devHit.Kind == LookAtKind.Prop && (_propEdit == null || _propEdit.Instance != _devHit.Prop || !ReferenceEquals(_propEdit.Map, map)))
        {
            if (_propEdit is { Changed: true } busy)
            {
                Ui.DevToolsMessage($"\"{busy.Def.Name}\" has changes: save a package or revert it first (reopening it)", Glfw.Time);
            }
            else if (PropEditSession.Open(map, _devHit.Prop, out var why) is { } s)
                _propEdit = s;
            else
            {
                Ui.DevToolsMessage("can't edit that prop: " + why, Glfw.Time);
                return;
            }
        }
        else if (_propEdit == null)
        {
            Ui.DevToolsMessage("point at a prop to edit it", Glfw.Time);
            return;
        }
        SetPropEditorOpen(true);
    }

    private static void SetPropEditorOpen(bool open)
    {
        Ui.PropEditorOpen = open;
        if (_session == null)
            return;
        if (open)
            SetMouseCaptured(false); // a menu: the cursor is free for the fields
        else if (CursorPolicy.MayCapture(CursorNow()))
        {
            SetMouseCaptured(true);
            _swallowClick = true;
        }
    }

    /// <summary>
    /// Called from Render once the camera is set: remembers it, puts edits back when the map changed or the match ended,
    /// and runs the inspector's ray while it's open. Closed: a few checks.
    /// </summary>
    private static void DevToolsFrame(Vector3 eye, Vector3 fwd, in Matrix4x4 viewProj)
    {
        if (_propEdit is { } edit && (_session == null || !ReferenceEquals(World.Map, edit.Map)))
        {
            edit.Revert(); // edits are a preview: a new map or the menus see the props as their files have them
            _propEdit = null;
            Ui.PropEditorOpen = false;
        }
        _devEye = eye; // (kept even closed: the copy key looks once from here)
        _devFwd = fwd;
        _devViewProj = viewProj;
        if (!Ui.LookAtOpen && !Ui.PropEditorOpen)
            return;
        if (Ui.LookAtOpen && !Ui.PropEditorOpen)
            ProbeLookAt();
    }

    /// <summary>Casts the inspector's ray from the camera; the text is only rebuilt when what it hits changes.</summary>
    private static void ProbeLookAt()
    {
        var map = World.Map;
        if (_devFwd == Vector3.Zero)
            _devFwd = _game.LookDir(_game.LookYaw, _game.LookPitch);
        if (_devEye == Vector3.Zero && _game.Local is { } me)
            _devEye = me.RenderEye;
        _devHit = LookAt.Probe(map, _devEye, _devFwd, LookAtRange);
        var key = (_devHit.Kind, _devHit.Prop, _devHit.Part, _devHit.Box, Vector3.Round(_devHit.Point * 10f));
        if (key == _devHitKey && _devRef.Length > 0)
            return;
        _devHitKey = key;
        _devRef = LookAt.Reference(map, _devHit);
        _devDetails = LookAt.Details(map, _devHit);
    }

    /// <summary>Draws the inspector and the editor over everything (closed: two bool checks).</summary>
    private static void DrawDevTools(double now)
    {
        if (Ui.LookAtOpen && _session != null)
            Ui.DrawLookAt(World.Map, _devHit, _devRef, _devDetails, _devViewProj, _settings.Bind(GameAction.CopyLookAt), _settings.Bind(GameAction.PropEditor), _settings.Bind(GameAction.LookAtInspector));
        if (Ui.PropEditorOpen)
            Ui.DrawPropEditor(_propEdit, _devViewProj, now, _settings.Bind(GameAction.PropEditor));
        if (!Ui.PropEditorOpen && Ui.DevToolsFlash(now) is { } flash && _session != null)
            Ui.DrawDevFlash(flash);
    }
}
