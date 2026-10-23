using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Playing with friends: the host's invite code (lobby room header), the host's server list options, and Settings >
/// Multiplayer (server lists, "Run a server list for friends", public address lookup, invite links). Program fills the
/// status fields (Program.Listing.cs). See docs/net/SERVER-DISCOVERY.md.
/// </summary>
public sealed partial class Ui
{
    /// <summary>The host's invite code ("" while unknown or not hosting).</summary>
    public string HostInvite = "";

    /// <summary>The invite holds a LAN address (no public address known): it only works on the host's network.</summary>
    public bool InviteIsLan;

    /// <summary>The host's server list line (HostListing.Status).</summary>
    public string ListStatus = "";

    /// <summary>"Run a server list for friends": its port (0: not running), games on it, and why it couldn't start.</summary>
    public ushort ManualListPort;

    public int ManualListGames;
    public string ManualListError = "";

    private string _inviteCopied = "";
    private double _inviteCopiedUntil;
    private string _urlMessage = "";

    /// <summary>The lobby room header's invite buttons (host only).</summary>
    private void InviteHeader()
    {
        if (HostInvite.Length == 0)
            return;
        ImGui.SameLine(0, 18 * _scale);
        if (ImGui.Button("Copy invite link##invite"u8))
            CopyInvite(CallOfSlop.InviteCode.Link(HostInvite), "Invite link copied: send it to your friends.");
        ImGui.SameLine();
        if (ImGui.Button("Copy code##invite"u8))
            CopyInvite(HostInvite, "Invite code copied.");
        if (Environment.TickCount64 / 1000.0 < _inviteCopiedUntil)
        {
            ImGui.SameLine();
            ImGuiSafe.TextColored(Green, _inviteCopied);
        }
        else if (InviteIsLan || ListStatus.Length > 0)
        {
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, InviteIsLan ? "(LAN code: your public address isn't known)" : ListStatus);
        }
    }

    private void CopyInvite(string text, string message)
    {
        try
        {
            SafeClipboard.Sink(text);
            _inviteCopied = message;
        }
        catch (Exception ex)
        {
            _inviteCopied = $"Couldn't copy ({ex.Message}). The code is {HostInvite}.";
        }
        _inviteCopiedUntil = Environment.TickCount64 / 1000.0 + 4;
    }

    /// <summary>"LAN", "list", "LAN + list" for a browser row.</summary>
    private static string SourceLabel(ServerBrowser.Entry e) => (e.FromLan, e.FromMaster) switch
    {
        (true, true) => "  LAN + list",
        (false, true) => "  list",
        _ => "  LAN",
    };

    /// <summary>The Host tab's sharing options.</summary>
    private void HostSharingOptions(Settings s)
    {
        ImGui.Checkbox("Share on our server lists"u8, ref s.ShareOnLists);
        ImGui.SameLine();
        ImGui.BeginDisabled(!s.ShareOnLists);
        ImGui.Checkbox("Run one if none answers"u8, ref s.AutoListServer);
        ImGui.EndDisabled();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 640 * _scale);
        ImGuiSafe.TextColored(Dim, !s.ShareOnLists
            ? "Not on any server list: friends join with your invite code (shown in the lobby), people on your network see it anyway."
            : s.AutoListServer
                ? "Your game goes on the server lists you know; if none answers, your game runs one for the group and puts it in your invite code, so friends who join get it too."
                : "Your game goes on the server lists you know (Settings > Multiplayer). Friends can always join with your invite code.");
        ImGui.TextColored(Dim, "Anyone with a list's address sees your game's name, map, player count and address (IP and port). Lists are run by players, never by a company."u8);
        ImGui.PopTextWrapPos();
    }

    /// <summary>Settings > Multiplayer.</summary>
    private void MultiplayerSettings(Settings s, float w)
    {
        ImGui.SeparatorText("Server lists"u8);
        ImGui.SetNextItemWidth(w * 1.8f);
        try
        {
            if (ImGui.InputTextWithHint("Server lists"u8, "e.g. 203.0.113.45, mylist.example.com:27051"u8, _masterInput!))
                s.MasterServer = Settings.CleanMasterList(_masterInput!.ToString());
        }
        catch (Exception ex)
        {
            ImGuiSafe.FieldFailed("server lists", ex);
        }
        if (!ImGui.IsItemActive() && _masterInput!.ToString() != s.MasterServer)
            ImGuiSafe.SetInput(_masterInput, s.MasterServer); // a list learned from an invite shows up here
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 700 * _scale);
        ImGui.TextColored(Dim, "Server lists show your friends' games in the server browser. You rarely need to type one: joining a friend by invite code (or on your network) adds their list here, and hosting runs one when none answers. Separate several with commas."u8);
        var shipped = Settings.ShippedMasters;
        if (shipped.Count > 0)
            ImGuiSafe.TextColored(Dim, $"Also used, from masters.txt in the game folder: {string.Join(", ", shipped.Select(m => $"{m.Host}:{m.Port}"))}");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.Checkbox("Run a server list for friends on this PC"u8, ref s.RunListServer);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 700 * _scale);
        if (ManualListPort != 0)
        {
            var addr = PublicAddress.Ip is { } ip ? $"{ip}{(ManualListPort == MasterServerApp.DefaultPort ? "" : $":{ManualListPort}")}" : $"this PC's public address{(ManualListPort == MasterServerApp.DefaultPort ? "" : $" port {ManualListPort}")}";
            ImGuiSafe.TextColored(Green, $"Running on UDP {ManualListPort} with {ManualListGames} game(s). Friends add {addr} under Server lists (or just join you by invite code once).");
        }
        else if (s.RunListServer && ManualListError.Length > 0)
            ImGuiSafe.TextColored(Red, $"Couldn't start it: {ManualListError} (is tools\\RunServerList.bat already running? Then you don't need this.)");
        else
            ImGuiSafe.TextColored(Dim, "Keeps a list running while the game is open (the same as tools\\RunServerList.bat, which runs it without the game). Its UDP port (27050) is opened on your router with UPnP when that's on.");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.SeparatorText("Invites"u8);
        ImGui.Checkbox("Find my public address for invite codes (asks a public STUN server)"u8, ref s.UseStun);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 700 * _scale);
        ImGuiSafe.TextColored(Dim, s.UseStun
            ? $"When you host, a tiny request to Google's or Cloudflare's STUN server tells the game your public address (nothing else is sent).{(PublicAddress.Ip is { } known ? $" Last seen: {known}." : "")}"
            : "Off: invite codes use your router's address (UPnP) when it gives one, else your LAN address.");
        if (PublicAddress.Mismatch)
            ImGui.TextColored(Orange, "Your router's internet address differs from the one the internet sees (carrier-grade NAT): friends outside can't reach a game you host. Let a friend host, or use your network only."u8);
        ImGui.PopTextWrapPos();
        if (UrlProtocol.Supported)
        {
            var registered = UrlProtocol.RegisteredExe();
            if (registered == null)
            {
                if (ImGui.Button("Make invite links open the game"u8))
                    _urlMessage = UrlProtocol.Register() is { Length: > 0 } err ? err : "Done: clicking a callofslop://join/... link now starts this game and joins.";
            }
            else
            {
                if (!UrlProtocol.IsThisBuild && ImGui.Button("Point invite links at this build"u8))
                    _urlMessage = UrlProtocol.Register() is { Length: > 0 } err ? err : "Invite links now open this build.";
                if (!UrlProtocol.IsThisBuild)
                    ImGui.SameLine();
                if (ImGui.Button("Stop invite links opening the game"u8))
                    _urlMessage = UrlProtocol.Unregister() is { Length: > 0 } err ? err : "Invite links no longer open the game.";
                ImGuiSafe.TextColored(Dim, $"Invite links open: {registered}");
            }
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 700 * _scale);
            ImGuiSafe.TextColored(Dim, _urlMessage.Length > 0 ? _urlMessage
                : "Optional: lets a friend click your callofslop://join/... link (in Discord, a browser...) to start the game and join you. Only for your Windows account; the button next to it undoes it.");
            ImGui.PopTextWrapPos();
        }
    }
}
