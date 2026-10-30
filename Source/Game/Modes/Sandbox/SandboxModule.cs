using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox (milestone 1 of the sandbox survival mode, docs/sandbox/CONSTRUCTS.md): build vehicles and bases from parts
/// with a tool in hand, drive them, open their doors and shoot their parts off. Only on maps with a "sandbox" section
/// (Dev: Construct Yard). No score or time limit (like Test Range): the host leaves through Esc > HOST. Dev-only while
/// the construct system grows; the construct state travels in its own messages (Msg.CBody, Msg.CPose, Msg.CAct) and
/// each player's hands (tool, held part, seat, edit) in this module's state tail. The work is in GameSandbox.cs and
/// Source/Constructs.
/// </summary>
public sealed class SandboxModule : GameModeModule
{
    public override GameMode Id => GameMode.Sandbox;
    public override string Name => "Sandbox";
    public override string Short => "SBX";
    public override string Rules(int limit = 0) =>
        "Build and drive: take parts from the rack, fit them with the wrench (3) or the welder (4), wire them with the linking tool (5), sit in a seat to drive. No score or time limit.";
    public override string Description => "Build vehicles and bases from parts with tools in hand, drive them, and shoot their parts off.";
    public override bool DevOnly => true;
    public override bool UsesMapVote => false;
    public override bool HasTimeLimit => false;
    public override bool Endless => true;
    public override bool HostPanelInMatch => true;
    public override bool AllowsStreaks => false;
    public override MapNeeds Needs => MapNeeds.SandboxSection;
    public override string MapWarning => "Sandbox needs a sandbox map: pick one (Dev pack: Construct Yard), or it plays Free-for-all.";
    public override int MaxPlayers(Map map) => Math.Min(map.MaxPlayers, 8);

    public override bool Fits(Map? map) => map?.Sandbox != null;

    public override void OnReset(Game game) => game.ResetSandbox();

    public override void OnClientMatchStart(Game game) => game.ResetSandboxClient();

    public override void OnSpawn(Game game, Player p) => game.SandboxOnSpawn(p);

    /// <summary>Spawned bots never come back (GameSandboxSpawns.cs): dead, they go.</summary>
    public override bool CanRespawn(Game game, Player p, double now) => !p.SbSpawned;

    /// <summary>Spawned bots: friendly ones follow (or fight zombies near them), enemy ones hunt (GameSandboxSpawns.cs).</summary>
    public override bool BotTick(Game game, Player bot, BotBrain brain, double now) => game.SandboxBotTick(bot, brain, now);

    public override void OnPlayerLeave(Game game, Player p)
    {
        game.SandboxStand(p, teleport: false);
        game.SandboxLeave(p);
    }

    /// <summary>The constructs, the edits, the drivers (GameSandbox.cs). Nothing ends the match.</summary>
    public override void ServerTickPlaying(Game game, double now) => game.ServerTickSandbox(now);

    public override void OnClientJoined(Game game, RakNetGUID to) => game.SendSandboxState(to);

    public override bool HandlePacket(Game game, byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.CBody:
            case (byte)Msg.CPose:
            case (byte)Msg.CAct:
            case (byte)Msg.CFire:
                game.OnSandboxPacket(id, ref r, sender, now);
                return true;
            case (byte)Msg.ZEnts:
            case (byte)Msg.ZEvent:
            case (byte)Msg.ZCrowd:
                game.OnZombiesPacket(id, ref r, sender, now); // the spawned zombies (GameSandboxSpawns.cs)
                return true;
            default:
                return false;
        }
    }

    // ---- Each player's hands, ~10 Hz -------------------------------------------------------------------------------

    public override byte StateVersion => 3; // 2: the linking tool (milestone 2); 3: the physics gun's held construct and grab point (protocol 88)

    public override void WriteState(Game game, PacketWriter w)
    {
        var hands = game.SandboxHands;
        w.U8((byte)Math.Min(game.Players.Count, 64));
        var n = 0;
        foreach (var p in game.Players.Values)
        {
            if (n++ == 64)
                break;
            ref var h = ref hands.Hand(p.Id);
            w.U8(p.Id).U8((byte)h.Tool).U16(h.Held).U16(h.SeatBody).U8(h.SeatPart).U8((byte)h.Edit)
                .U16((ushort)Math.Clamp(h.Duration * 100, 0, 65535)).U16((ushort)Math.Clamp((game.SandboxNow - h.Start) * 100, 0, 65535));
            w.U16(h.GrabBody);
            if (h.GrabBody != 0)
                w.U16(Cm(h.GrabLocal.X)).U16(Cm(h.GrabLocal.Y)).U16(Cm(h.GrabLocal.Z));
        }
    }

    public override void ReadState(Game game, ref PacketReader r)
    {
        int n = r.U8();
        for (var k = 0; k < n; k++)
        {
            var id = r.U8();
            var tool = ConstructToolWire.From(r.U8());
            var held = r.U16();
            var seat = r.U16();
            var seatPart = r.U8();
            var edit = (EditKind)Math.Min(r.U8(), (byte)EditKind.Remove);
            var duration = r.U16() / 100.0;
            var elapsed = r.U16() / 100.0;
            var grab = r.U16();
            var grabAt = grab == 0 ? Vector3.Zero : new Vector3((short)r.U16(), (short)r.U16(), (short)r.U16()) / 100f;
            if (game.IsServer)
                continue;
            ref var h = ref game.SandboxHands.Hand(id);
            if (id != game.LocalId)
                h.Tool = tool; // (the local player's own tool is what it chose: the server follows the client's request)
            h.Held = ConstructParts.Get(held) != null ? held : ConstructParts.None;
            h.SeatBody = seat;
            h.SeatPart = seatPart;
            h.Edit = edit;
            h.Duration = duration;
            h.Start = game.SandboxNow - elapsed;
            if (id != game.LocalId || game.SandboxNow > h.GrabAskedAt + 0.4) // (what the local player just asked for stands until the server has heard it)
            {
                h.GrabBody = grab;
                h.GrabLocal = grabAt;
            }
        }
    }

    private static ushort Cm(float v) => unchecked((ushort)(short)Math.Clamp(MathF.Round(v * 100), short.MinValue, short.MaxValue));

    // ---- HUD -----------------------------------------------------------------------------------------------------------

    public override string GoalText(Game game) => $"{Name}: {Rules()}";

    public override (string Big, string Sub)? ScoreBar(Game game) => ("SANDBOX", game.SandboxBarText());

    public override void HudLines(Game game, Player me, List<HudLine> lines, ref Vector3? pointAt) => game.SandboxHud(me, lines, HudWhite, HudDim, HudOrange, HudGreen, HudRed);

    /// <summary>The constructs' glass, the ghost sockets of the held part and the part under the crosshair (blended pass; ConstructRender.cs).</summary>
    public override void DrawObjectives(Renderer gfx, Game game, double now)
    {
        ConstructRender.DrawGlass(gfx, gfx.CameraPosition); // glass panes, back to front, over everything opaque
        ConstructRender.DrawGhosts(gfx, now);
        ConstructRender.DrawWires(gfx, game.Constructs); // (only while the linking tool is out)
        ConstructRender.DrawConfigPreview(gfx, game.Constructs); // (only while a part's settings panel is open)
        ConstructRender.DrawSpawnGhost(gfx, game, now); // (only while placing a spawn: GameSandboxSpawns.cs)
    }

    public override string? SelfTestVerify(Game game) =>
        game.Phase == MatchPhase.Ended ? $"the match ended: {game.MatchResult}"
        : World.Map.Sandbox != null && game.Constructs.Bodies.Count == 0 ? "the sandbox map's constructs never appeared" : null;
}
