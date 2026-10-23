using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Co-op: a squad of 1-4 players (up to 6 with bots) plays a mission against waves of enemy soldiers (docs/pve/DESIGN.md).
/// Only on maps with a mission (Maps/&lt;Pack&gt;/Missions). This module is the mode's front door: its flags and hooks
/// route into the PvE subsystems (all <c>partial class Game</c>, Source/Game/Pve): the mission runner and objectives
/// (GamePveMission.cs), the enemies (GamePve.cs, GamePveAi.cs), the spawn director (SpawnDirector.cs), the packets
/// (GamePveNet.cs) and the co-op bots (PveBots.cs). The objective state rides in this module's mode-state tail.
/// </summary>
public sealed class CoopModule : GameModeModule
{
    public override GameMode Id => GameMode.Coop;
    public override string Name => "Co-op";
    public override string Short => "COOP";

    public override string Rules(int limit = 0) =>
        "Co-op missions for 1 to 4 players against waves of enemy soldiers. Complete the objectives in order: kill, blow things up, hold the line, get out. A wipe restarts the objective at its checkpoint.";

    public override bool CoOp => true;
    public override bool AllowsStreaks => false; // kill streaks target players (phase 2: co-op streaks)
    public override bool UsesMapVote => false; // a mission restarts on its own map
    public override bool HasTimeLimit => false; // the mission ends when it's done (or failed)
    public override bool ShowsRespawnMessage => false; // the objective panel says when you're back
    public override bool ShowsKillNotices => false;

    /// <summary>The most players (people and bots) on a mission.</summary>
    public const int MaxSquad = 6;

    public override string MapWarning => "Co-op needs a map with a mission (World Tour: Silo), or it plays Free-for-all.";

    public override bool Fits(Map? map) => Missions.For(map) != null;

    public override int MaxPlayers(Map map) => Math.Min(MaxSquad, map.MaxPlayers);

    public override string GoalText(Game game) =>
        Missions.For(World.Map) is { } m ? $"{Name}: {m.Name}. {m.Description}" : $"{Name}: {Rules()}";

    // ---- Lifecycle -------------------------------------------------------------------------------------------

    public override void OnReset(Game game) => game.ResetCoopRun();

    public override void OnClientMatchStart(Game game) => game.ResetCoopRun();

    public override void OnSpawn(Game game, Player p) => game.CoopOnSpawn(p);

    public override void OnKill(Game game, Player attacker, Player victim, byte killId, double now) => game.CoopOnPlayerDied(victim, now);

    public override bool CanRespawn(Game game, Player p, double now) => game.CoopCanRespawn(p, now);

    public override void ServerTickPlaying(Game game, double now) => game.ServerTickCoop(now);

    public override bool HandlePacket(Game game, byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        if (id is not ((byte)Msg.PEnts or (byte)Msg.PEvent))
            return false;
        game.OnPvePacket(id, ref r, now);
        return true;
    }

    // ---- Bots ----------------------------------------------------------------------------------------------

    public override bool BotTick(Game game, Player bot, BotBrain brain, double now) => game.PveBotTick(bot, brain, now);

    /// <summary>Where a bot heads with no enemy in sight: the objective (the nearest unfinished target, the zone, the extraction).</summary>
    public override Vector3? BotObjective(Game game, Player bot) =>
        game.Coop.Status == CoopStatus.Active ? ObjectivePoint(game, bot.Position) : null;

    /// <summary>The point the HUD arrow and the bots aim for right now (null: none).</summary>
    public static Vector3? ObjectivePoint(Game game, Vector3 from)
    {
        var c = game.Coop;
        if (c.StageDef is not { } st)
            return null;
        switch (st.Kind)
        {
            case StageKind.Destroy:
            {
                Vector3? best = null;
                var bestD = float.MaxValue;
                for (var i = 0; i < c.TargetCount; i++)
                {
                    if (c.Targets[i] == CoopTarget.Destroyed)
                        continue;
                    var at = CoopMarkers.Target(game, i);
                    var d = Vector3.DistanceSquared(at, from);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = at;
                    }
                }
                return best;
            }
            case StageKind.Collect:
            {
                Vector3? best = null;
                var bestD = float.MaxValue;
                for (var i = 0; i < c.TargetCount; i++)
                {
                    if (c.Targets[i] != CoopTarget.Intact)
                        continue;
                    var d = Vector3.DistanceSquared(c.ItemPos[i], from);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = c.ItemPos[i];
                    }
                }
                return best ?? CoopMarkers.Deliver(game);
            }
            case StageKind.Eliminate when c.NamedAlive:
                return c.NamedPos;
            default:
                return CoopMarkers.Focus(game);
        }
    }

    // ---- Replicated state (the objective panel) -------------------------------------------------------------

    public override byte StateVersion => 1;

    public override void WriteState(Game game, PacketWriter w)
    {
        var c = game.Coop;
        w.U8((byte)(c.Mission + 1)).U8((byte)c.Stage).U8((byte)c.Kind).U8((byte)c.Status).U16((ushort)Math.Clamp(c.Progress, 0, 65535)).U16((ushort)Math.Clamp(c.Goal, 0, 65535))
            .U16((ushort)Math.Clamp(c.Timer * 10f, 0, 65535)).U8((byte)Math.Clamp(c.Lose * 255f, 0, 255)).U8((byte)Math.Min(c.Wipes, 255))
            .U8((byte)Math.Min(c.Retries, 255)).U8((byte)Math.Min(c.Alive, 255)).U8((byte)Math.Min(c.Budget, 255)).U8((byte)((byte)c.Phase | (Math.Min(c.Wave, 63) << 2)))
            .U8((byte)Math.Clamp(c.RespawnSeconds, 0, 255)).U8((byte)c.Difficulty).U8((byte)(Math.Min(c.InZone, 15) | (Math.Min(c.NeedInZone, 15) << 4))).U8((byte)c.TargetCount);
        for (var i = 0; i < c.TargetCount; i++)
            w.U8((byte)c.Targets[i]).U8((byte)Math.Clamp(c.Targets[i] == CoopTarget.Planted ? c.TargetValue[i] * 4f : c.TargetValue[i] * 255f, 0, 255));
        var items = c.Kind == StageKind.Collect;
        w.U8((byte)((c.NamedAlive ? 1 : 0) | (items ? 2 : 0)));
        if (c.NamedAlive)
            WriteVec(w, c.NamedPos);
        if (items)
            for (var i = 0; i < c.TargetCount; i++)
                WriteVec(w, c.ItemPos[i]);
    }

    private static void WriteVec(PacketWriter w, Vector3 v) =>
        w.U16((ushort)(short)Math.Clamp((int)MathF.Round(v.X * 32), short.MinValue, short.MaxValue)).U16((ushort)(short)Math.Clamp((int)MathF.Round(v.Y * 64), short.MinValue, short.MaxValue))
            .U16((ushort)(short)Math.Clamp((int)MathF.Round(v.Z * 32), short.MinValue, short.MaxValue));

    private static Vector3 ReadVec(ref PacketReader r) => new((short)r.U16() / 32f, (short)r.U16() / 64f, (short)r.U16() / 32f);

    public override void ReadState(Game game, ref PacketReader r)
    {
        if (!r.Has(20))
            return;
        var c = game.Coop;
        c.Mission = r.U8() - 1;
        c.StageCount = c.MissionDef?.Stages.Count ?? 0;
        c.Stage = r.U8();
        c.Kind = (StageKind)Math.Min((int)r.U8(), (int)StageKind.Collect);
        c.Status = (CoopStatus)Math.Min((int)r.U8(), (int)CoopStatus.Failed);
        c.Progress = r.U16();
        c.Goal = r.U16();
        c.Timer = r.U16() / 10f;
        c.Lose = r.U8() / 255f;
        c.Wipes = r.U8();
        c.Retries = r.U8();
        if (c.Retries == 255)
            c.Retries = int.MaxValue;
        c.Alive = r.U8();
        c.Budget = r.U8();
        var pw = r.U8();
        c.Phase = (DirectorPhase)Math.Min(pw & 3, 2);
        c.Wave = pw >> 2;
        c.RespawnSeconds = r.U8();
        c.Difficulty = (BotDifficulty)Math.Min((int)r.U8(), (int)BotDifficulty.Insane);
        var zone = r.U8();
        c.InZone = zone & 15;
        c.NeedInZone = zone >> 4;
        c.TargetCount = Math.Min((int)r.U8(), CoopObjective.MaxTargets);
        for (var i = 0; i < c.TargetCount && r.Has(2); i++)
        {
            c.Targets[i] = (CoopTarget)Math.Min((int)r.U8(), 2);
            var v = r.U8();
            c.TargetValue[i] = c.Targets[i] == CoopTarget.Planted ? v / 4f : v / 255f;
        }
        if (!r.Has(1))
            return;
        var flags = r.U8();
        c.NamedAlive = (flags & 1) != 0;
        if (c.NamedAlive && r.Has(6))
            c.NamedPos = ReadVec(ref r);
        if ((flags & 2) != 0)
            for (var i = 0; i < c.TargetCount && r.Has(6); i++)
                c.ItemPos[i] = ReadVec(ref r);
    }

    // ---- HUD ------------------------------------------------------------------------------------------------

    private static string Bar(float f)
    {
        var n = Math.Clamp((int)MathF.Round(f * 10), 0, 10);
        return "[" + new string('#', n) + new string('-', 10 - n) + "]";
    }

    private static string Clock(float s) => $"{(int)s / 60}:{(int)s % 60:00}";

    public override void HudLines(Game game, Player me, List<HudLine> lines, ref Vector3? pointAt)
    {
        var c = game.Coop;
        if (c.StageDef is not { } st || c.MissionDef is not { } m)
        {
            if (c.Status == CoopStatus.Idle)
                lines.Add(new HudLine("No mission on this map", HudDim));
            return;
        }
        lines.Add(new HudLine($"OBJECTIVE {c.Stage + 1}/{m.Stages.Count}: {st.Text}", HudOrange));
        switch (c.Status)
        {
            case CoopStatus.Regroup:
                lines.Add(new HudLine("Regrouping at the checkpoint...", HudRed));
                return;
            case CoopStatus.Complete:
                lines.Add(new HudLine("MISSION COMPLETE", HudGreen));
                return;
            case CoopStatus.Failed:
                lines.Add(new HudLine("MISSION FAILED", HudRed));
                return;
        }
        switch (st.Kind)
        {
            case StageKind.Eliminate:
                if (c.NamedAlive)
                    lines.Add(new HudLine($"Kill {st.TargetName}", HudWhite));
                if (st.Count > 0)
                    lines.Add(new HudLine($"Enemies killed {Math.Min(c.Progress, c.Goal)}/{c.Goal} {Bar(c.Goal > 0 ? c.Progress / (float)c.Goal : 0)}", HudWhite));
                break;
            case StageKind.Destroy:
                for (var i = 0; i < c.TargetCount; i++)
                {
                    var name = i < st.Targets.Count ? st.Targets[i].Name : "?";
                    var text = c.Targets[i] switch
                    {
                        CoopTarget.Destroyed => $"{name}: destroyed",
                        CoopTarget.Planted => $"{name}: charge set, {c.TargetValue[i]:0.0} s",
                        _ => c.TargetValue[i] > 0 ? $"{name}: planting {Bar(c.TargetValue[i])}" : $"{name}: stand at it to plant a charge",
                    };
                    lines.Add(new HudLine(text, c.Targets[i] == CoopTarget.Destroyed ? HudDim : c.Targets[i] == CoopTarget.Planted ? HudRed : HudWhite));
                }
                break;
            case StageKind.Defend:
                lines.Add(new HudLine($"Hold for {Clock(c.Timer)} {Bar(c.Goal > 0 ? c.Progress / (float)c.Goal : 0)}", HudWhite));
                if (c.Lose > 0.02f)
                    lines.Add(new HudLine($"LOSING THE POINT {Bar(c.Lose)} - get back in!", HudRed));
                break;
            case StageKind.Reach:
                lines.Add(new HudLine($"At the extraction point: {c.InZone}/{c.NeedInZone}", HudWhite));
                break;
            case StageKind.Collect:
                lines.Add(new HudLine($"Items {c.Progress}/{c.Goal}", HudWhite));
                break;
        }
        if (st.Kind != StageKind.Defend && st.TimeLimit > 0)
            lines.Add(new HudLine($"Time left {Clock(c.Timer)}", c.Timer < 30 ? HudRed : HudWhite));
        if (!me.Alive)
        {
            var left = Math.Max(0, game.LocalDeathTime + c.RespawnSeconds - Brutal.GlfwApi.Glfw.Time);
            lines.Add(new HudLine(left > 0 ? $"Reinforcements in {left:0} s" : "Press Respawn to rejoin the squad", HudOrange));
        }
        lines.Add(new HudLine($"Enemies {c.Alive}  wave {c.Wave}  {c.Difficulty}{(c.Retries < 255 ? $"  wipes {c.Wipes}/{c.Retries}" : "")}", HudDim));
        pointAt = ObjectivePoint(game, me.Position);
    }

    public override (string Big, string Sub)? ScoreBar(Game game) =>
        game.Coop.StageDef is { } st && game.Coop.MissionDef is { } m ? ($"{game.Coop.Stage + 1}/{m.Stages.Count}", $"{m.Name}: {st.Text}") : ("CO-OP", "No mission");

    public override string? ResultsLine(Game game) =>
        game.Coop.MissionDef is { } m ? $"{m.Name}: {game.CoopKills} enemies killed, {game.Coop.Wipes} wipe(s)" : null;

    public override void DrawObjectives(Renderer gfx, Game game, double now) => CoopMarkers.Draw(gfx, game, now);

    public override string? SelfTestVerify(Game game)
    {
        if (Missions.For(World.Map) == null)
            return null; // (a map without a mission plays nothing)
        if (game.Coop.Mission < 0)
            return "no mission started";
        if (game.AliveEnemies > game.Director.Budget || game.PvePeakAlive > Math.Max(game.Director.Budget, 1) + 8)
            return $"enemies over the budget: {game.AliveEnemies} alive, peak {game.PvePeakAlive}, budget {game.Director.Budget}";
        return null;
    }
}

/// <summary>Co-op objective positions on either side (resolved from the mission once per stage) and their world markers.</summary>
public static class CoopMarkers
{
    private static int _mission = -2, _stage = -1;
    private static readonly Vector3[] _targets = new Vector3[CoopObjective.MaxTargets];
    private static Vector3 _focus, _at, _deliver;

    private static void Ensure(Game game)
    {
        var c = game.Coop;
        if (c.Mission == _mission && c.Stage == _stage)
            return;
        _mission = c.Mission;
        _stage = c.Stage;
        if (c.StageDef is not { } st)
            return;
        for (var i = 0; i < Math.Min(st.Targets.Count, _targets.Length); i++)
            _targets[i] = st.Targets[i].At.Resolve();
        _focus = st.FocusPoint.Resolve();
        _at = st.At?.Resolve() ?? _focus;
        _deliver = st.Deliver?.Resolve() ?? _at;
    }

    /// <summary>Forget the cached positions (a new map).</summary>
    public static void Invalidate() => _mission = -2;

    public static Vector3 Target(Game game, int i)
    {
        Ensure(game);
        return _targets[Math.Clamp(i, 0, _targets.Length - 1)];
    }

    public static Vector3 Focus(Game game)
    {
        Ensure(game);
        return _focus;
    }

    public static Vector3 Deliver(Game game)
    {
        Ensure(game);
        return _deliver;
    }

    /// <summary>Underground (a cave room under terrain): the shared ring would follow the surface above, so draw it flat at the point's height.</summary>
    private static bool Underground(Vector3 at) => World.Map.HasTerrain && at.Y < World.Map.GroundHeight(at.X, at.Z) - 1.5f;

    private static void RingAt(Renderer gfx, Vector3 center, float radius, Vector3 color, float alpha, float height)
    {
        if (!Underground(center))
        {
            ObjectiveRender.Ring(gfx, center, radius, color, alpha, height);
            return;
        }
        const int segments = 28;
        var seg = MathF.Tau * radius / segments * 1.08f;
        for (var i = 0; i < segments; i++)
        {
            var a = i * MathF.Tau / segments;
            var pos = new Vector3(center.X + MathF.Cos(a) * radius, center.Y + 0.04f + height / 2, center.Z + MathF.Sin(a) * radius);
            gfx.Draw(Matrix4x4.CreateScale(0.14f, height, seg) * Matrix4x4.CreateRotationY(-a) * Matrix4x4.CreateTranslation(pos), color, alpha, mode: 2);
        }
    }

    /// <summary>Rings, halos and beams for the stage's objectives (inside the blended pass).</summary>
    public static void Draw(Renderer gfx, Game game, double now)
    {
        var c = game.Coop;
        if (c.StageDef is not { } st || c.Status != CoopStatus.Active)
            return;
        Ensure(game);
        var t = (float)now;
        var orange = new Vector3(1f, 0.6f, 0.15f);
        switch (st.Kind)
        {
            case StageKind.Destroy:
                for (var i = 0; i < c.TargetCount; i++)
                {
                    var at = _targets[i];
                    if (c.Targets[i] == CoopTarget.Destroyed)
                        continue;
                    var planted = c.Targets[i] == CoopTarget.Planted;
                    var blink = planted && ((int)(now * (c.TargetValue[i] < 2 ? 8 : 3)) & 1) == 0;
                    var col = planted ? new Vector3(1f, 0.2f, 0.15f) * (blink ? 1.3f : 0.6f) : orange;
                    RingAt(gfx, at, st.Targets[i].Radius, col, 0.8f, 0.1f);
                    ObjectiveRender.Beam(gfx, at, 0.25f, 25f, col, 0.15f);
                    if (planted || c.TargetValue[i] > 0)
                        gfx.Draw(Matrix4x4.CreateScale(0.35f, 0.25f, 0.25f) * Matrix4x4.CreateTranslation(at + new Vector3(0, 0.15f, 0)), planted ? col : new Vector3(0.4f, 0.35f, 0.25f), 1f, mode: 2);
                }
                break;
            case StageKind.Defend:
            {
                var col = c.Lose > 0.02f ? new Vector3(1f, 0.25f, 0.2f) * (0.75f + 0.25f * MathF.Sin(t * 10f)) : new Vector3(0.3f, 0.65f, 1f);
                RingAt(gfx, _at, st.Radius, col, 0.85f, 0.12f);
                if (!Underground(_at))
                    ObjectiveRender.Halo(gfx, _at, st.Radius, 2.5f, col, 0.35f);
                break;
            }
            case StageKind.Reach:
                RingAt(gfx, _at, st.Radius, new Vector3(0.3f, 1f, 0.45f), 0.85f, 0.12f);
                ObjectiveRender.Beam(gfx, _at, 0.4f, 60f, new Vector3(0.3f, 1f, 0.45f), 0.2f);
                break;
            case StageKind.Collect:
                for (var i = 0; i < c.TargetCount; i++)
                    if (c.Targets[i] == CoopTarget.Intact)
                    {
                        gfx.Draw(Matrix4x4.CreateScale(0.4f) * Matrix4x4.CreateRotationY(t * 1.5f + i) * Matrix4x4.CreateTranslation(c.ItemPos[i] + new Vector3(0, 0.5f + 0.08f * MathF.Sin(t * 3 + i), 0)), orange, 1f, mode: 2);
                        ObjectiveRender.Beam(gfx, c.ItemPos[i], 0.2f, 20f, orange, 0.15f);
                    }
                if (st.Deliver != null)
                    RingAt(gfx, _deliver, st.DeliverRadius, new Vector3(0.3f, 1f, 0.45f), 0.85f, 0.12f);
                break;
            case StageKind.Eliminate when c.NamedAlive:
                ObjectiveRender.Beam(gfx, c.NamedPos, 0.3f, 30f, new Vector3(1f, 0.2f, 0.15f), 0.18f);
                break;
        }
    }
}
