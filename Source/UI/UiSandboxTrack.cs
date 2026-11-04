using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>Sandbox: a track's settings (ConstructTracks.cs): the track tool's Reload or the configure tool's Fire on one of its wheels. Changes go to the server when a slider is let go.</summary>
public sealed partial class Ui
{
    public bool SandboxTrackOpen { get; private set; }

    private ushort _tcBody;
    private int _tcTrack;
    private float _tcWidth, _tcLink, _tcTension, _tcGrip;
    private bool _tcDirty;
    private double _tcSent;

    public void OpenSandboxTrack(Construct c, int track)
    {
        ref var t = ref c.Tracks[track];
        SandboxTrackOpen = true;
        _tcBody = c.Id;
        _tcTrack = track;
        _tcWidth = t.Width;
        _tcLink = t.LinkLength;
        _tcTension = t.Tension;
        _tcGrip = t.Grip;
        _tcDirty = false;
    }

    public void CloseSandboxTrack() => SandboxTrackOpen = false;

    /// <summary>The panel. False once it has been closed or its track is gone.</summary>
    public bool DrawSandboxTrack(Game game)
    {
        if (game.Constructs.Get(_tcBody) is not { } c || _tcTrack >= c.TrackCount)
            return false;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X * 0.62f, vp.WorkPos.Y + vp.WorkSize.Y * 0.3f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(380 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.82f);
        var open = true;
        if (ImGui.Begin("Track###sbtrack"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
        {
            var any = ImGui.SliderFloat("Width"u8, ref _tcWidth, ConstructTracks.MinWidth, MathF.Max(2f, _tcWidth * 2), "%.2f m"u8);
            any |= ImGui.SliderFloat("Link length"u8, ref _tcLink, ConstructTracks.MinLink, MathF.Max(1f, _tcLink * 2), "%.2f m"u8);
            any |= ImGui.SliderFloat("Tension"u8, ref _tcTension, 0f, 1f, "%.2f"u8);
            any |= ImGui.SliderFloat("Friction"u8, ref _tcGrip, ConstructTracks.MinGrip, ConstructTracks.MaxGrip, "%.2f"u8);
            _tcDirty |= any;
            var now = ImGui.GetTime();
            if (_tcDirty && now - _tcSent >= 0.1)
            {
                _tcSent = now;
                _tcDirty = false;
                Span<float> v = [_tcWidth, _tcLink, _tcTension, _tcGrip];
                game.SandboxSendTrackEdit(_tcBody, TrackOp.Set, _tcTrack, v);
            }
            if (ImGui.Button("Close"u8))
                open = false;
        }
        ImGui.End();
        return open;
    }
}
