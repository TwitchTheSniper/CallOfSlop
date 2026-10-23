using System.Diagnostics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The updater's UI (Source/Updates): the "Update available" banner on the main menu, the Updates part of
/// Settings > Player, and this build's version in the main menu corner.
/// </summary>
public sealed partial class Ui
{
    private bool _updateLater; // "Later": no banner for the rest of this session
    private bool _updateDismissed; // a failure the player closed

    /// <summary>The banner on the main menu. Nothing when there's no update (or it was skipped / put off).</summary>
    private void UpdateBanner(Settings s)
    {
        if (Updater.Shared is not { } up)
            return;
        var v = up.Snapshot();
        var skipped = v.Available != null && s.UpdateSkip == v.Available.Version.ToString() && !v.Loud;
        switch (v.State)
        {
            case UpdateState.Available when v.Available is { } m && !_updateLater && !skipped:
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 560 * _scale);
                ImGuiSafe.TextColored(Green, $"Update available: v{m.Version} (you have {BuildInfo.Label})");
                var notes = m.Notes.Length > 0 ? m.Notes : v.Release?.Notes ?? "";
                if (notes.Length > 0)
                    ImGuiSafe.TextColored(Dim, notes);
                if (v.Message.StartsWith("Download paused", StringComparison.Ordinal))
                    ImGuiSafe.TextColored(Dim, v.Message);
                ImGui.PopTextWrapPos();
                if (!m.InstallableHere)
                {
                    ImGui.TextColored(Orange, "This update must be downloaded by hand."u8);
                    if (ImGui.Button("Open release page"u8))
                        OpenReleasePage(v.Release);
                }
                else if (ImGui.Button("Install"u8))
                {
                    s.Save(); // the new build reads the settings straight away
                    _updateDismissed = false;
                    up.Install();
                }
                ImGui.SameLine();
                if (ImGui.Button("Later"u8))
                    _updateLater = true;
                ImGui.SameLine();
                if (ImGui.Button("Skip this version"u8))
                {
                    s.UpdateSkip = m.Version.ToString();
                    s.Save();
                }
                ImGui.Spacing();
                break;

            case UpdateState.Downloading:
                ImGuiSafe.TextColored(Green, v.Message);
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Green);
                ImGui.ProgressBar(v.Total > 0 ? (float)v.Done / v.Total : 0f, new float2(360 * _scale, 18 * _scale),
                    $"{v.Done / (1024 * 1024)} / {Math.Max(1, v.Total / (1024 * 1024))} MB");
                ImGui.PopStyleColor();
                ImGui.SameLine();
                if (ImGui.Button("Pause"u8))
                    up.Cancel();
                ImGui.Spacing();
                break;

            case UpdateState.Installing or UpdateState.Launching:
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 560 * _scale);
                ImGuiSafe.TextColored(Green, v.Message);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                break;

            case UpdateState.Failed when v.Loud && !_updateDismissed:
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 560 * _scale);
                ImGuiSafe.TextColored(Red, v.Message);
                ImGui.PopTextWrapPos();
                if (ImGui.Button("Retry"u8))
                {
                    if (v.Available != null)
                        up.Install();
                    else
                        up.Check(manual: true);
                }
                ImGui.SameLine();
                if (ImGui.Button("Open release page"u8))
                    OpenReleasePage(v.Release);
                ImGui.SameLine();
                if (ImGui.Button("Close"u8))
                    _updateDismissed = true;
                ImGui.Spacing();
                break;
        }
    }

    /// <summary>Settings > Player > Updates.</summary>
    private void UpdateSettings(Settings s)
    {
        ImGui.Spacing();
        ImGui.SeparatorText("Updates"u8);
        ImGuiSafe.TextColored(Dim, $"This build: {BuildInfo.Label}");
        ImGui.Checkbox("Check for updates when the game starts"u8, ref s.UpdateCheck);
        if (Updater.Shared is not { } up)
        {
            ImGui.TextColored(Dim, "The updater is off in this mode."u8);
            return;
        }
        var v = up.Snapshot();
        if (up.Busy)
            ImGui.BeginDisabled(true);
        if (ImGui.Button("Check for updates"u8))
        {
            _updateLater = false;
            _updateDismissed = false;
            up.Check(manual: true);
        }
        if (up.Busy)
            ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Release page"u8))
            OpenReleasePage(v.Release);
        if (v.Message.Length > 0)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 640 * _scale);
            ImGuiSafe.TextColored(v.State == UpdateState.Failed ? Red : v.State == UpdateState.Available ? Green : Dim, v.Message);
            ImGui.PopTextWrapPos();
        }
        if (v.State == UpdateState.Available && v.Available is { InstallableHere: true } && ImGui.Button("Install now"u8))
        {
            s.Save();
            up.Install();
        }
        if (v.CheckedUtc != DateTime.MinValue)
            ImGuiSafe.TextColored(Dim, $"Last asked GitHub: {v.CheckedUtc.ToLocalTime():g} (on its own at most every {UpdateCache.MaxAge.TotalHours:0} hours)");
        if (s.UpdateSkip.Length > 0)
        {
            ImGuiSafe.TextColored(Dim, $"Skipping v{s.UpdateSkip} (a newer release still shows).");
            ImGui.SameLine();
            if (ImGui.Button("Don't skip"u8))
                s.UpdateSkip = "";
        }
        if (v.InstalledTo != null)
            ImGuiSafe.TextColored(Dim, $"Installed to {v.InstalledTo}. Your old build's folder is left as it is: delete it yourself when you no longer need it.");
        ImGui.TextColored(Dim, "Updates install into a new folder next to this one; your saves carry over."u8);
    }

    /// <summary>This build's version in the bottom-right corner of the menus.</summary>
    private void BuildCorner()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X - 16 * _scale, vp.WorkPos.Y + vp.WorkSize.Y - 16 * _scale), ImGuiCond.Always, new float2(1f, 1f));
        if (ImGui.Begin("##buildinfo"u8, Overlay))
            ImGuiSafe.TextColored(Dim, BuildInfo.Label);
        ImGui.End();
    }

    /// <summary>Opens the release's page (or the releases list) in the browser. Only this repo's GitHub pages.</summary>
    private static void OpenReleasePage(ReleaseInfo? release)
    {
        var url = release?.PageUrl is { } p && UpdateManifest.IsTrustedPageUrl(p) ? p : UpdateManifest.ReleasesPage;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[update] couldn't open {url}: {ex.Message}");
        }
    }
}
