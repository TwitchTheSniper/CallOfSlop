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
        UpdateCleaner.Shared?.MenuReached(s); // this build works: mark it, then remove old versions (UpdateCleanup.cs)
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
            ImGuiSafe.TextColored(Dim, $"Installed to {v.InstalledTo}.");
        ImGui.TextColored(Dim, "Updates install into a new folder next to this one; your saves carry over."u8);
        CleanupSettings(s);
    }

    private const string CleanupPopup = "Clean up old versions##cleanup";

    /// <summary>Settings > Player > Updates: the cleanup options and "Clean up old versions now" with its confirmation.</summary>
    private void CleanupSettings(Settings s)
    {
        ImGui.Checkbox("Remove old versions after updating"u8, ref s.UpdateCleanup);
        ImGui.SameLine();
        ImGui.TextColored(Dim, "(only folders the updater installed; never ones with saves)"u8);
        ImGui.Checkbox("Keep previous version for rollback"u8, ref s.UpdateKeepPrevious);
        if (UpdateCleaner.Shared is not { } c)
            return;
        var (preview, message) = c.Snapshot();
        ImGui.BeginDisabled(c.Busy);
        if (ImGui.Button("Clean up old versions now"u8))
        {
            c.Preview(s.UpdateKeepPrevious);
            _cleanupOpen = true;
        }
        ImGui.EndDisabled();
        if (message.Length > 0)
        {
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, message);
        }
        if (_cleanupOpen && preview != null)
        {
            ImGui.OpenPopup(CleanupPopup);
            _cleanupOpen = false;
        }
        ImGui.SetNextWindowSize(new float2(760 * _scale, 0));
        if (ImGui.BeginPopupModal(CleanupPopup, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize))
        {
            var plan = c.Snapshot().Preview;
            if (plan == null)
            {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
                return;
            }
            ImGui.PushTextWrapPos(0);
            ImGuiSafe.TextColored(Dim, $"In {plan.Root}:");
            if (plan.Remove.Count == 0)
                ImGui.TextColored(Green, "Nothing to remove."u8);
            else
            {
                ImGuiSafe.TextColored(Orange, $"This removes {plan.Remove.Count} item(s), {UpdateCleanup.Mb(plan.Bytes)}:");
                foreach (var r in plan.Remove.Take(30))
                    ImGuiSafe.TextWrapped($"  {Path.GetFileName(r.Path)}  ({UpdateCleanup.Mb(r.Bytes)}): {r.Why}");
                if (plan.Remove.Count > 30)
                    ImGuiSafe.TextColored(Dim, $"  ...and {plan.Remove.Count - 30} more");
            }
            if (plan.Kept.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(Dim, "Kept:"u8);
                foreach (var k in plan.Kept.Take(30))
                    ImGuiSafe.TextColored(Dim, $"  {Path.GetFileName(k.Path)}: {k.Why}");
            }
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (plan.Remove.Count > 0 && ImGui.Button($"Remove {plan.Remove.Count} item(s)", new float2(200 * _scale, 30 * _scale)))
            {
                c.RemovePreview(s.UpdateKeepPrevious);
                ImGui.CloseCurrentPopup();
            }
            if (plan.Remove.Count > 0)
                ImGui.SameLine();
            if (ImGui.Button(plan.Remove.Count > 0 ? "Cancel"u8 : "Close"u8, new float2(120 * _scale, 30 * _scale)))
            {
                c.ClosePreview();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private bool _cleanupOpen; // "Clean up old versions now" was pressed: open the dialog when the preview is ready

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
