using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox (milestone 1 of the sandbox survival mode, docs/sandbox/CONSTRUCTS.md): build vehicles and bases from parts
/// with a tool in hand, drive them, open their doors and shoot their parts off. Only on maps with a "sandbox" section
/// (Sandbox pack: Construct Yard). No score or time limit (like Test Range): the host leaves through Esc > HOST. Dev-only while
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
        "Build and drive. No score or time limit.";
    public override string Description => "Build vehicles and bases from parts with tools in hand, drive them, and shoot their parts off.";
    public override bool DevOnly => true;
    public override bool UsesMapVote => false;
    public override bool HasTimeLimit => false;
    public override bool Endless => true;
    public override bool HostPanelInMatch => true;
    public override bool AllowsStreaks => false;
    public override PerkSet? FixedPerks => PerkSet.Empty; // no perks: nobody carries one, so none of their effects apply
    public override bool LoadoutAtStart => false; // you walk straight into the world
    public override bool StartsAtOnce => true; // no waiting for anyone, no countdown
    public override bool UsesEquipment => false; // grenades stay, tactical gear doesn't
    public override bool ArenaExtras => false;
    public override string? LoadoutHint => "Sandbox: your guns and grenade, used the next time you spawn. Perks, tactical gear and kill streaks aren't used here.";
    public override MapNeeds Needs => MapNeeds.SandboxSection;
    public override string MapWarning => "Sandbox needs a sandbox map: pick one (Sandbox pack: Construct Yard), or it plays Free-for-all.";
    public override int MaxPlayers(Map map) => Math.Min(map.MaxPlayers, 8);

    public override bool Fits(Map? map) => map?.Sandbox != null;

    public override void OnReset(Game game) => game.ResetSandbox();

    public override void OnClientMatchStart(Game game) => game.ResetSandboxClient();

    public override void OnPlayerReset(Game game, Player p) => game.SandboxResetPlayer(p);

    public override void OnSpawn(Game game, Player p) => game.SandboxOnSpawn(p);

    /// <summary>Spawned bots never come back (GameSandboxSpawns.cs): dead, they go. Nor does anyone dead for good (permadeath, GameSandboxDeath.cs).</summary>
    public override bool CanRespawn(Game game, Player p, double now) => !p.SbSpawned && !game.SandboxFallen(p.Id);

    /// <summary>Spawned bots: friendly ones follow (or fight zombies near them), enemy ones hunt (GameSandboxSpawns.cs).</summary>
    public override bool BotTick(Game game, Player bot, BotBrain brain, double now) => game.SandboxBotTick(bot, brain, now);

    public override void OnPlayerLeave(Game game, Player p)
    {
        game.StoryLeave(p); // (first: who they are is forgotten as they go; GameStory.cs)
        game.SandboxStand(p, teleport: false);
        game.SandboxLeave(p);
    }

    /// <summary>A kill or a death, for the story (GameStory.cs). Nothing is scored.</summary>
    public override void OnKill(Game game, Player attacker, Player victim, byte killId, double now) => game.StoryKill(attacker, victim, now);

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
            case (byte)Msg.ZBuild:
            case (byte)Msg.ZRun:
                game.OnSandboxBuildPacket(id, ref r, sender, now); // a built weapon, the parts you have at the Gun Workbench (GameSandboxBench.cs)
                return true;
            default:
                return false;
        }
    }

    // ---- Each player's hands, ~10 Hz -------------------------------------------------------------------------------

    public override byte StateVersion => 4; // 2: the linking tool (milestone 2); 3: the physics gun's held construct and grab point (protocol 88); 4: tool upgrade levels (protocol 215)

    public override void WriteState(Game game, PacketWriter w)
    {
        var hands = game.SandboxHands;
        // People always (every seat); a spawned body only while its hand holds something, and three times after it goes
        // idle (the state is unreliable): hundreds of bots with empty hands would be kilobytes ten times a second.
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        for (var pass = 0; pass < 2; pass++)
            foreach (var p in game.Players.Values)
            {
                if ((p.Id < Game.MaxPeople) != (pass == 0) || n == 255)
                    continue;
                ref var h = ref hands.Hand(p.Id);
                if (pass == 1)
                {
                    var idle = h.Tool == default && h.Held == 0 && h.SeatBody == 0 && h.Edit == EditKind.None && h.GrabBody == 0;
                    ref var quiet = ref game.SbHandQuiet[p.Id];
                    quiet = idle ? (byte)Math.Min(quiet + 1, 255) : (byte)0;
                    if (quiet > 3)
                        continue;
                }
                n++;
                WriteHand(game, w, p, ref h);
            }
        w.PatchU8(countAt, (byte)n);
    }

    private static void WriteHand(Game game, PacketWriter w, Player p, ref SandboxHand h)
    {
        {
            w.U8(p.Id).U8((byte)h.Tool).U16(h.Held).U16(h.SeatBody).U16(h.SeatPart).U8((byte)h.Edit)
                .U16((ushort)Math.Clamp(h.Duration * 100, 0, 65535)).U16((ushort)Math.Clamp((game.SandboxNow - h.Start) * 100, 0, 65535));
            w.U16(h.GrabBody);
            if (h.GrabBody != 0)
                w.CmVec(h.GrabLocal).U8(h.GrabSeg); // (protocol 137: a free joint held on its own, GrabLocal in its frame)
            var levels = game.SandboxHands.LevelsOf(p.Id); // (tool upgrades, protocol 215: a byte a stat, ConstructEditsUpgrades.cs)
            w.U8((byte)ToolStat.COUNT);
            for (var s = 0; s < (int)ToolStat.COUNT; s++)
                w.U8(levels[s]);
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
            var seatPart = r.U16();
            var edit = (EditKind)Math.Min(r.U8(), (byte)EditKind.SocketWeld);
            var duration = r.U16() / 100.0;
            var elapsed = r.U16() / 100.0;
            var grab = r.U16();
            var grabAt = grab == 0 ? Vector3.Zero : r.CmVec();
            var grabSeg = grab == 0 ? (byte)0 : r.U8();
            int stats = r.U8();
            if (!game.IsServer)
            {
                var levels = game.SandboxHands.LevelsOf(id);
                for (var s = 0; s < stats; s++)
                {
                    var v = r.U8();
                    if (s < ToolUpgrades.Slots)
                        levels[s] = v;
                }
            }
            else
                for (var s = 0; s < stats; s++)
                    r.U8();
            if (game.IsServer)
                continue;
            ref var h = ref game.SandboxHands.Hand(id);
            if (id != game.LocalId || game.SandboxNow > h.ToolAskedAt + Game.SandboxToolLag)
                h.Tool = tool; // (what the local player just chose stands until the server has heard it; after that the server's word holds, or a spawn or a saved world's tool would never show here)
            if (held != h.HeldGone)
                h.HeldGone = ConstructParts.None; // (the server has heard: from here on a part is a new one)
            if (held != h.HeldGone || game.SandboxNow > h.HeldGoneUntil)
                h.Held = ConstructParts.Get(held) != null ? held : ConstructParts.None; // (a part the local player's tool change just put away stays gone: this snapshot may be older)
            if (id != game.LocalId) // (the local player's seat comes with their own move: GameSnapshots.cs)
            {
                h.SeatBody = seat;
                h.SeatPart = seatPart;
            }
            h.Edit = edit;
            h.Duration = duration;
            h.Start = game.SandboxNow - elapsed;
            if (id != game.LocalId || game.SandboxNow > h.GrabAskedAt + 0.4) // (what the local player just asked for stands until the server has heard it)
            {
                h.GrabBody = grab;
                h.GrabLocal = grabAt;
                h.GrabSeg = grabSeg;
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
        game.RideDrawnPose(); // (the ghosts sit on the construct as it is drawn: GameSandboxRide.cs)
        ConstructRender.DrawGhosts(gfx, now);
        SnapRender.Draw(gfx, now); // (snappy building's connection points; only while a part is held)
        NudgeRender.Draw(gfx); // (the Nudge tool's triad or arcs: Program.SandboxNudge.cs)
        SocketWeldRender.Draw(gfx, now); // (the welder's socket weld: markers and the ghost; Program.SandboxSocketWeld.cs)
        ConstructRender.DrawWires(gfx, game.Constructs); // (only while the linking tool is out)
        ConstructRender.DrawConfigPreview(gfx, game.Constructs); // (only while a part's settings panel is open)
        ConstructRender.DrawSpawnGhost(gfx, game, now); // (only while placing a spawn: GameSandboxSpawns.cs)
        game.RideTickPose();
    }

    public override string? SelfTestVerify(Game game) =>
        game.Phase == MatchPhase.Ended ? $"the match ended: {game.MatchResult}"
        : World.Map.Sandbox != null && game.Constructs.Bodies.Count == 0 ? "the sandbox map's constructs never appeared" : null;
}
