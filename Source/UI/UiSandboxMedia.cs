using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Video Screen's or Music Player's side of the configure panel (docs/sandbox/MEDIA.md): a link box, the game's tracks, the list and the
/// transport. Everything goes to the server as a <see cref="MediaOp"/>; what shows here is what the server sent back.
/// </summary>
public sealed partial class Ui
{
    private ImInputString? _cmLink;
    private MusicEntry[] _cmTracks = [];
    private int _cmTrack;
    private string _cmWhy = "";
    private string _cmHeard = "";

    private void SandboxConfigMedia(Game game, Construct c, int part, ConstructPartDef d)
    {
        if (!d.IsMedia || part < 0 || part >= c.PartCount)
            return;
        var body = c.Id;
        var m = c.Parts[part].Media;
        var list = m?.List ?? [];
        ImGui.SeparatorText("Playing"u8);

        // The transport.
        var state = m?.State ?? MediaState.Stopped;
        if (state == MediaState.Playing)
        {
            if (ImGui.Button("Pause"u8))
                game.SandboxMedia(body, part, MediaOp.Pause);
        }
        else if (ImGui.Button("Play"u8) && list.Length > 0)
            game.SandboxMedia(body, part, MediaOp.Resume);
        ImGui.SameLine();
        if (ImGui.Button("Stop"u8))
            game.SandboxMedia(body, part, MediaOp.Stop);
        ImGui.SameLine();
        if (ImGui.Button("Prev"u8) && list.Length > 0)
            game.SandboxMedia(body, part, MediaOp.Previous);
        ImGui.SameLine();
        if (ImGui.Button("Next"u8) && list.Length > 0)
            game.SandboxMedia(body, part, MediaOp.Next);
        var look = MediaDirector.Look(c, part);
        if (look != _cmHeard)
            _cmHeard = look;
        ImGui.TextWrapped(_cmHeard);

        // A link.
        _cmLink ??= new ImInputString(512);
        ImGui.SetNextItemWidth(-1);
        var entered = ImGui.InputTextWithHint("##cmlink"u8, "Paste a YouTube link"u8, _cmLink, ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetTooltip("youtube.com or youtu.be. Only the video's id is kept: a playlist link adds its one video.");
        if (ImGui.Button("Add to list"u8) || entered)
        {
            if (MediaEntry.FromUrl(ImGuiSafe.Text(_cmLink), out var why) is { } line)
            {
                var first = list.Length == 0;
                game.SandboxMedia(body, part, MediaOp.Add, entry: line);
                ImGuiSafe.SetInput(_cmLink, "");
                _cmWhy = "";
                if (first)
                    game.SandboxMedia(body, part, MediaOp.Play, 0); // (a first link plays: that's what it was pasted for)
            }
            else
                _cmWhy = why;
        }
        if (_cmWhy.Length > 0)
            ImGui.TextColored(new float4(1f, 0.6f, 0.3f, 1f), _cmWhy);

        // The game's own tracks: they need no browser, so they play on every PC.
        if (_cmTracks.Length == 0)
            _cmTracks = Array.FindAll(MusicCatalogue.Build(), e => e.Track != null);
        _cmTrack = Math.Clamp(_cmTrack, 0, _cmTracks.Length - 1);
        ImGui.SetNextItemWidth(-90 * _scale);
        if (ImGui.BeginCombo("##cmtrack"u8, _cmTracks[_cmTrack].Name))
        {
            for (var i = 0; i < _cmTracks.Length; i++)
                if (ImGui.Selectable(_cmTracks[i].Name, i == _cmTrack))
                    _cmTrack = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button("Add track"u8))
            game.SandboxMedia(body, part, MediaOp.Add, entry: MediaEntry.ForTrack(_cmTracks[_cmTrack].Track!));
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetTooltip("One of the game's own tracks. One plays at a time, the nearest, and it waits while the game's music is on.");

        // The list.
        ImGui.SeparatorText($"List ({list.Length} of {ConstructMedia.MaxList})");
        for (var i = 0; i < list.Length; i++)
        {
            ImGui.PushID(i);
            var on = m!.State != MediaState.Stopped && m.Index == i;
            if (ImGui.SmallButton(">"u8))
                game.SandboxMedia(body, part, MediaOp.Play, i);
            ImGui.SameLine();
            if (ImGui.SmallButton("^"u8))
                game.SandboxMedia(body, part, MediaOp.Up, i);
            ImGui.SameLine();
            if (ImGui.SmallButton("v"u8))
                game.SandboxMedia(body, part, MediaOp.Down, i);
            ImGui.SameLine();
            if (ImGui.SmallButton("x"u8))
                game.SandboxMedia(body, part, MediaOp.Remove, i);
            ImGui.SameLine();
            if (on)
                ImGui.TextColored(new float4(0.45f, 0.9f, 0.5f, 1f), MediaDirector.Label(list[i]));
            else
                ImGui.Text(MediaDirector.Label(list[i]));
            ImGui.PopID();
        }
    }
}
