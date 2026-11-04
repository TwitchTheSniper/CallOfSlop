using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Saved worlds on the menus (docs/sandbox/WORLD-SAVES.md): in the lobby room, or on the Single player page, with Sandbox
/// chosen, the host starts a new world or picks a saved one. The list is read from disk when the panel opens and after a change, not every frame.
/// </summary>
public sealed partial class Ui
{
    private static readonly int[] AutosaveChoices = [0, 1, 5, 10, 15, 30];
    private List<WorldEntry>? _worlds;
    private string[] _worldLines = [];
    private ImInputString? _worldNameInput;
    private string _worldNote = "";
    private string? _worldDeleting; // the folder asked about
    private WorldSession? _spWorld; // the Single player page's pick
    private string _spNewWorld = "";

    private void RefreshWorlds()
    {
        _worlds = WorldStore.List();
        _worldLines = _worlds.Select(WorldLine).ToArray();
    }

    private static string WorldLine(WorldEntry e)
    {
        if (e.Problem.Length > 0)
            return $"{e.Meta.Name}: {e.Problem}";
        var m = e.Meta;
        var mode = m.Survival ? "Survival" : "Creative";
        var when = m.SavedUtc > 0 ? new DateTime(m.SavedUtc, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "not played";
        return $"{m.Name} - {m.MapName} - {mode} - {when}{(e.FromBackup ? " (from its backup)" : "")}";
    }

    /// <summary>The list and New world. Returns the world picked or made this frame, or null.</summary>
    private (WorldEntry? Saved, string NewName) DrawWorldPicker()
    {
        if (_worlds == null)
            RefreshWorlds();
        WorldEntry? picked = null;
        var newName = "";
        ImGui.SetNextItemWidth(240 * _scale);
        _worldNameInput ??= new ImInputString(WorldStore.MaxNameLength + 1);
        ImGui.InputTextWithHint("##worldname"u8, "World name"u8, _worldNameInput);
        ImGui.SameLine();
        if (ImGui.Button("New world"u8))
        {
            var name = ImGuiSafe.Text(_worldNameInput);
            if (WorldStore.NameRefusal(name) is { } refused)
                _worldNote = refused;
            else
            {
                newName = WorldStore.CleanName(name);
                _worldNote = "";
            }
        }
        if (_worlds!.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Dim, "Saved worlds"u8);
            for (var i = 0; i < _worlds.Count; i++)
            {
                var e = _worlds[i];
                ImGui.PushID(i);
                if (_worldDeleting == e.Folder)
                {
                    ImGuiSafe.TextColored(Orange, $"Delete {e.Meta.Name} for good?");
                    ImGui.SameLine();
                    if (ImGui.Button("Yes, delete"u8))
                    {
                        _worldNote = WorldStore.Delete(e.Folder) is { } why ? $"Couldn't delete it: {why}" : "";
                        _worldDeleting = null;
                        ImGui.PopID();
                        RefreshWorlds();
                        break;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Keep it"u8))
                        _worldDeleting = null;
                }
                else
                {
                    ImGui.BeginDisabled(e.Problem.Length > 0);
                    if (ImGui.Button("Continue"u8))
                        picked = e;
                    ImGui.EndDisabled();
                    ImGui.SameLine();
                    if (ImGui.Button("Delete"u8))
                        _worldDeleting = e.Folder;
                    ImGui.SameLine();
                    ImGuiSafe.TextColored(e.Problem.Length > 0 ? Orange : Dim, _worldLines[i]);
                }
                ImGui.PopID();
            }
        }
        if (_worldNote.Length > 0)
            ImGuiSafe.TextColored(Orange, _worldNote);
        return (picked, newName);
    }

    private void AutosavePicker(Settings s)
    {
        ImGui.SetNextItemWidth(160 * _scale);
        if (ImGui.BeginCombo("Autosave##world"u8, s.WorldAutosaveMinutes == 0 ? "Off" : $"Every {s.WorldAutosaveMinutes} min"))
        {
            foreach (var m in AutosaveChoices)
                if (ImGui.Selectable(m == 0 ? "Off" : $"Every {m} min", m == s.WorldAutosaveMinutes))
                {
                    s.WorldAutosaveMinutes = m;
                    s.Save();
                }
            ImGui.EndCombo();
        }
    }

    /// <summary>The lobby room with Sandbox chosen: which world this game is. Clients see its name.</summary>
    private void DrawRoomSavedWorld(Game game, Settings s, bool host)
    {
        ImGui.Spacing();
        if (!host)
        {
            if (game.WorldShownName.Length > 0)
                ImGuiSafe.TextColored(Green, $"World: {game.WorldShownName}");
            return;
        }
        if (game.WorldSession is { } ws)
        {
            var m = ws.Meta;
            ImGuiSafe.TextColored(Green, $"World: {m.Name} ({m.MapName}, {(m.Survival ? "Survival" : "Creative")})");
            ImGui.TextColored(Dim, "The map and mode stay with the world."u8);
            AutosavePicker(s);
            game.WorldAutosaveMinutes = s.WorldAutosaveMinutes;
            if (ImGui.Button("Leave this world"u8) && game.LobbySetWorld(null, out _))
                _worlds = null;
            ImGui.Spacing();
            return;
        }
        ImGui.TextColored(Orange, "No world: this game won't be saved."u8);
        var (saved, newName) = DrawWorldPicker();
        WorldSession? pick = null;
        if (saved != null && (pick = Game.WorldOpen(saved, out var why)) == null)
            _worldNote = why;
        if (newName.Length > 0)
            pick = Game.WorldNew(newName, World.Map, game.SandboxSurvivalOption);
        if (pick != null && !game.LobbySetWorld(pick, out var refused))
            _worldNote = refused;
        ImGui.Spacing();
    }

    /// <summary>The Single player page with Sandbox chosen: which world START plays. True: a saved world, which brings its own map and mode.</summary>
    private bool DrawSinglePlayerWorld(Settings s)
    {
        if (_spWorld is { } ws)
        {
            var m = ws.Meta;
            ImGuiSafe.TextColored(Green, $"World: {m.Name} ({m.MapName}, {(m.Survival ? "Survival" : "Creative")})");
            AutosavePicker(s);
            if (ImGui.Button("Pick another world"u8))
                _spWorld = null;
            return true;
        }
        if (_spNewWorld.Length > 0)
        {
            ImGuiSafe.TextColored(Green, $"New world: {_spNewWorld}");
            AutosavePicker(s);
            if (ImGui.Button("Pick another world"u8))
                _spNewWorld = "";
            return false;
        }
        ImGui.TextColored(Orange, "No world: this game won't be saved."u8);
        var (saved, newName) = DrawWorldPicker();
        if (saved != null && (_spWorld = Game.WorldOpen(saved, out var why)) == null)
            _worldNote = why;
        if (newName.Length > 0)
            _spNewWorld = newName;
        return false;
    }

    /// <summary>The world the Single player page picked (a new one goes on <paramref name="map"/>), once. Null: none.</summary>
    public WorldSession? TakeSinglePlayerWorld(Map map, bool survival)
    {
        var world = _spWorld ?? (_spNewWorld.Length > 0 ? Game.WorldNew(_spNewWorld, map, survival) : null);
        _spWorld = null;
        _spNewWorld = "";
        _worlds = null;
        return world;
    }

}
