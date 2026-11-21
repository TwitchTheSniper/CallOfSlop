using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's script actions that aren't a place's or a tool's: "secs S" waits real seconds, "hold KEYS S" holds keys down,
/// "seat BODY" sits in a construct's Seat, "where" says where the player is, "heard" prints the chat lines heard since the last
/// one; the crew verbs are listed here as known and played by their own files.
/// </summary>
public static partial class Program
{
    private static int _wsHeardFrom;
    private static long _wsSecsUntil, _wsKeysUntil;
    private static readonly List<GameAction> _wsKeys = [];

    /// <summary>PollActions: the "hold" keys, down until their time is up.</summary>
    private static void WsScriptHold(bool[] down)
    {
        if (_wsKeys.Count == 0)
            return;
        if (System.Diagnostics.Stopwatch.GetTimestamp() >= _wsKeysUntil)
        {
            _wsKeys.Clear();
            return;
        }
        foreach (var k in _wsKeys)
            down[(int)k] = true;
    }

    /// <summary>Once a frame: a running "secs" holds the script until its time is up (real seconds, not frames).</summary>
    private static void WsScriptFrame()
    {
        if (_wsSecsUntil == 0)
            return;
        if (System.Diagnostics.Stopwatch.GetTimestamp() >= _wsSecsUntil)
        {
            _wsSecsUntil = 0;
            _wsWait = 0;
        }
        else
            _wsWait = Math.Max(_wsWait, 2);
    }

    private static bool WsScriptKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "heard" or "secs" or "hold" or "where" or "seat" or "crew" or "crewshoot" or "crewbodies" or "crewconvoy" or "crewfleet" or "crewpowers" or "crewterritory" or "crewpolitics" or "crewofficer" or "crewhit" or "npcstanding" or "crewbreak" or "crewkill" or "crewfaction" or "crewgroup" or "crewown" or "crewdisarm" or "crewprisoner" or "crewadmiral";

    private static bool WsScriptVerb(Game g, Player me, string verb, string arg)
    {
        switch (verb)
        {
            case "secs": // "secs S": the script waits S real seconds (the game runs on: a story's minutes)
                _wsSecsUntil = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) * System.Diagnostics.Stopwatch.Frequency);
                _wsWait = 2;
                return true;
            case "hold": // "hold MoveForward+Jump 5": those keys down for 5 real seconds (the script goes on meanwhile: add "secs 5" to wait)
            {
                if (!MenusOpen)
                    SetMouseCaptured(true); // (movement keys are built only while the mouse is held: a window that lost focus to the other one had let it go, so a joined client's held keys never reached its seat)
                var sp = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
                _wsKeys.Clear();
                foreach (var name in sp[0].Split('+'))
                    if (Enum.TryParse<GameAction>(name, true, out var ga))
                        _wsKeys.Add(ga);
                    else
                    {
                        WsFail($"hold: no action \"{name}\"");
                        return true;
                    }
                _wsKeysUntil = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(double.Parse(sp.Length > 1 ? sp[1] : "1", System.Globalization.CultureInfo.InvariantCulture) * System.Diagnostics.Stopwatch.Frequency);
                return true;
            }
            case "seat": // "seat BODY": stand by the first Seat of that construct (by its id) and press the use key on it
            {
                if (!ushort.TryParse(arg, out var seatBody) || g.Constructs.Get(seatBody) is not { } c)
                {
                    WsFail($"seat: no construct {arg}");
                    return true;
                }
                var part = -1;
                for (var i = 0; i < c.PartCount && part < 0; i++)
                    if (c.DefOf(i).Name == "Seat")
                        part = i;
                if (part < 0)
                {
                    WsFail($"seat: {arg} has no Seat");
                    return true;
                }
                var spot = WsPartSpot(c, part, out _);
                var up = GravityFrames.UpAt(spot);
                me.Teleport(spot + up * 0.2f - Vector3.Normalize(spot - c.WorldCom with { }) * 0f);
                WsEyeTo(g, me, spot + up * 1.4f);
                g.LookFrame = me.Position;
                (g.LookYaw, g.LookPitch) = GravityFrames.Curved ? GravityFrames.Angles(me.Position, spot - me.Eye) : (g.LookYaw, -1.2f);
                WsGrabMouse();
                _wsPress = GameAction.Interact;
                _wsWait = 30;
                WsSay($"[seat] {arg} part {part}, {Vector3.Distance(me.Eye, spot):0.0} m");
                return true;
            }
            case "where": // where this player is, and what they sit in
                WsSay($"[where] at {me.Position:0}, seated {g.SandboxHands.Hand(me.Id).Seated}, noclip {me.Sim.Noclip}, riding {me.Sim.RideBody} at {me.Sim.RideLocal:0.00} grounded {me.Sim.Grounded}"
                    + (g.Constructs.Get(me.Sim.RideBody) is { } wc && me.Sim.RideUp != Vector3.Zero ? $", roof over {g.Constructs.Raycast(me.Position + me.Sim.RideUpW * 0.5f, me.Sim.RideUpW, ConstructRide.InteriorRoof, out _, only: wc)}" : ""));
                return true;
            case "heard":
            {
                for (var i = Math.Max(0, Math.Min(_wsHeardFrom, g.Chat.Count)); i < g.Chat.Count; i++)
                    if ((g.Chat[i].Flags & ChatFlags.Say) != 0 || g.Chat[i].From == 255)
                        WsSay($"[heard] {(g.Players.TryGetValue(g.Chat[i].From, out var q) ? q.Name : "-")}: {g.Chat[i].Text}");
                _wsHeardFrom = g.Chat.Count;
                return true;
            }
        }
        return false;
    }
}
