using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Zombies HUD (World at War style): the round number in red tally marks (a number from round 6), everyone's points
/// with +N popups, the interact prompt ("Hold F to buy ... [cost]", with the bound interact key), perks, the downed / bleeding out / revive bars, the
/// round banner and the break countdown. Every player-visible line is plain ASCII.
/// </summary>
public sealed partial class Ui
{
    private static readonly float4 ZRed = new(0.8f, 0.05f, 0.05f, 1f);
    private static readonly float4 ZGold = new(1f, 0.85f, 0.3f, 1f);

    /// <summary>The round as World at War draws it: tally marks up to 5, then the number.</summary>
    public static string RoundTally(int round) => round <= 0 ? "" : round <= 5 ? string.Join(" ", Enumerable.Repeat("I", round)) : round.ToString();

    /// <summary>The match bar in Zombies: the round and what's left of it (or the break).</summary>
    private bool DrawZombiesBar(Game game)
    {
        if (game.Mode != GameMode.Zombies)
            return false;
        ImGui.PushFont(default, 24);
        ImGuiSafe.TextColored(ZRed, game.ZRound > 0 ? $"ROUND {game.ZRound}" : "GET READY");
        ImGui.PopFont();
        var left = game.ZPhase == ZRoundPhase.Round ? $"{game.ZRemaining} zombies left this round"
            : game.ZWaitingReady ? ReadyBarText(game)
            : $"Next round in {Math.Ceiling(game.ZPhaseLeft):0} s";
        ImGuiSafe.TextColored(Dim, $"{left}   {(game.ZPower ? "Power: ON" : "Power: off")}");
        return true;
    }

    /// <summary>The match bar during a ready-up break: "Break: 1/2 ready" (and the fallback's clock).</summary>
    private static string ReadyBarText(Game game)
    {
        var (r, n) = game.ZReadyCount;
        var left = game.ZPhaseLeft;
        return left > 0 ? $"Break: {r}/{n} ready (starts anyway in {(int)left / 60}:{(int)left % 60:00})" : $"Break: {r}/{n} ready";
    }

    /// <summary>The ready-up line in the middle of the screen (both sides; the host also gets the force start).</summary>
    public static string ReadyPromptText(Game game, string key)
    {
        var (r, n) = game.ZReadyCount;
        var mine = game.Local is { } me && game.ZPlayers.TryGetValue(me.Id, out var zp) && zp.RoundReady;
        if (!mine)
            return $"Press [{key}] when ready - {r}/{n} ready";
        return game.IsServer && r < n ? $"You're ready - {r}/{n} ready (host: press [{key}] again to start now)" : $"You're ready - {r}/{n} ready";
    }

    /// <summary>The spectator line: who you watch and the keys to change.</summary>
    public static string SpectateText(Game game, Settings settings)
    {
        var name = game.NameOf(game.ZSpectateId);
        var next = $"{Keybinds.Name(settings.Bind(GameAction.Fire))}] or [{Keybinds.Name(settings.Bind(GameAction.SpectateNext))}";
        var prev = $"{Keybinds.Name(settings.Bind(GameAction.Aim))}] or [{Keybinds.Name(settings.Bind(GameAction.SpectatePrev))}";
        return $"Spectating {name} - [{next}] next / [{prev}] previous";
    }

    /// <summary>Everything else of the Zombies HUD (called from DrawHud while a Zombies match is on).</summary>
    private void DrawZombiesHud(Game game, double now, float2 pos, float2 size, Settings settings)
    {
        if (game.Mode != GameMode.Zombies || game.Local is not { } me)
            return;
        var pad = 24 * _scale;
        var key = Keybinds.Name(settings.Bind(GameAction.Interact));

        // Bottom-left, over the health: the round in red.
        if (game.ZRound > 0)
        {
            ImGui.SetNextWindowPos(new float2(pos.X + pad, pos.Y + size.Y - pad - 190 * _scale), ImGuiCond.Always, new float2(0, 1));
            if (ImGui.Begin("##zround"u8, Overlay))
            {
                ImGui.PushFont(default, 64);
                ImGuiSafe.TextColored(ZRed, RoundTally(game.ZRound));
                ImGui.PopFont();
            }
            ImGui.End();
        }

        // Bottom-right, over the ammo: everyone's points (yours first), with your +N popups beside them.
        ImGui.SetNextWindowPos(new float2(pos.X + size.X - pad, pos.Y + size.Y - pad - 190 * _scale), ImGuiCond.Always, new float2(1, 1));
        if (ImGui.Begin("##zpoints"u8, Overlay))
        {
            var mine = game.ZP(me);
            ImGui.PushFont(default, 30);
            ImGuiSafe.TextColored(ZGold, $"{mine.Points}");
            ImGui.PopFont();
            foreach (var p in game.Players.Values.Where(p => p.Id != me.Id).OrderBy(p => p.Id))
            {
                var zp = game.ZP(p);
                var c = game.SideColor(p); // teammates: the friendly colour, not their own palette colour (which can be red)
                ImGuiSafe.TextColored(new float4(c.X, c.Y, c.Z, 1), $"{game.NameOf(p.Id)}: {zp.Points}{(zp.Downed ? "  (DOWN)" : !p.Alive ? "  (out)" : "")}");
            }
        }
        ImGui.End();
        ImGui.SetNextWindowPos(new float2(pos.X + size.X - pad - 150 * _scale, pos.Y + size.Y - pad - 250 * _scale), ImGuiCond.Always, new float2(1, 1));
        if (ImGui.Begin("##zpopups"u8, Overlay))
        {
            foreach (var (t, amount) in game.ZPopups)
            {
                var age = (float)(now - t);
                var a = Math.Clamp(1.6f - age, 0f, 1f);
                ImGuiSafe.TextColored(amount >= 0 ? new float4(1f, 0.85f, 0.3f, a) : new float4(1f, 0.3f, 0.3f, a), amount >= 0 ? $"+{amount}" : $"{amount}");
            }
        }
        ImGui.End();

        // Perks, bottom-left beside the round.
        var perks = game.ZP(me).Perks;
        if (perks != 0)
        {
            ImGui.SetNextWindowPos(new float2(pos.X + pad + 170 * _scale, pos.Y + size.Y - pad - 200 * _scale), ImGuiCond.Always, new float2(0, 1));
            if (ImGui.Begin("##zperks"u8, Overlay))
                for (var i = 0; i < game.ZRules.Perks.Count; i++)
                    if ((perks & (1 << i)) != 0)
                    {
                        var c = game.ZRules.Perks[i].Color;
                        ImGuiSafe.TextColored(new float4(c.X, c.Y, c.Z, 1), $"[{game.ZRules.Perks[i].Name}]");
                    }
            ImGui.End();
        }

        // Power-ups: Insta-Kill and Double Points timers bottom centre (blinking in their last 5 s); the floating ones labelled.
        var insta = game.InstaKillLeft(now);
        var dbl = game.DoublePointsLeft(now);
        if (insta > 0 || dbl > 0)
        {
            ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y - pad - 120 * _scale), ImGuiCond.Always, new float2(0.5f, 1));
            if (ImGui.Begin("##zpowers"u8, Overlay))
            {
                ImGui.PushFont(default, 26);
                foreach (var (kind, left) in new[] { (ZPowerKind.InstaKill, insta), (ZPowerKind.DoublePoints, dbl) })
                {
                    if (left <= 0 || (left < 5 && ((int)(now * 4) & 1) == 0))
                        continue;
                    var c = Game.PowerColor(kind);
                    ImGuiSafe.TextColored(new float4(c.X, c.Y, c.Z, 1), $"{Game.PowerName(kind).ToUpperInvariant()}  {Math.Ceiling(left):0}");
                }
                ImGui.PopFont();
            }
            ImGui.End();
        }
        foreach (var pu in game.ZPowerUps)
        {
            var dx = pu.At.X - me.Position.X;
            var dz = pu.At.Z - me.Position.Z;
            if (dx * dx + dz * dz <= 12 * 12)
                Hud.WorldLabel(pu.At + new System.Numerics.Vector3(0, 1.45f, 0), Game.PowerName(pu.Kind), Game.PowerColor(pu.Kind), 1f, 15f * _scale);
        }

        // The banner (ROUND 3, THE POWER IS ON).
        if (now - game.ZBannerTime < 3.5 && game.ZBannerText.Length > 0)
        {
            ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.22f), ImGuiCond.Always, new float2(0.5f, 0.5f));
            if (ImGui.Begin("##zbanner"u8, Overlay))
            {
                ImGui.PushFont(default, 56);
                ImGuiSafe.TextColored(new float4(0.85f, 0.08f, 0.08f, (float)Math.Clamp(3.5 - (now - game.ZBannerTime), 0, 1)), game.ZBannerText);
                ImGui.PopFont();
            }
            ImGui.End();
        }

        // Downed, bleeding out, being revived; or the prompt for what the interact key does.
        var me2 = game.ZP(me);
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.62f), ImGuiCond.Always, new float2(0.5f, 0.5f));
        if (ImGui.Begin("##zprompt"u8, Overlay))
        {
            if (me.Alive && me2.Downed)
            {
                ImGui.PushFont(default, 28);
                ImGuiSafe.TextColored(Red, me2.SelfReviveAt > 0 ? $"DOWN - {game.ZRules.Perks.FirstOrDefault(d => d.SelfRevive)?.Name ?? "your perk"} is getting you up" : "YOU ARE DOWN - a teammate can revive you");
                ImGui.PopFont();
                ImGuiSafe.TextColored(Dim, "Crawl with the move keys and keep shooting; stay near whoever revives you."u8);
                ImGui.ProgressBar(me2.BleedFraction, new float2(320 * _scale, 10 * _scale), "bleeding out"u8);
                if (me2.BeingRevived > 0)
                    ImGui.ProgressBar(me2.BeingRevived, new float2(320 * _scale, 10 * _scale), "being revived"u8);
            }
            else if (!me.Alive && game.Phase == MatchPhase.Playing)
            {
                ImGuiSafe.TextColored(Red, game.ZPhase == ZRoundPhase.Round ? "You bled out. You'll be back when this round ends."u8 : "You bled out. You'll be back when the next round starts."u8);
                if (game.ZSpectateId != byte.MaxValue)
                    ImGuiSafe.TextColored(new float4(1, 1, 1, 1), SpectateText(game, settings));
            }
            else if (game.FindZUse(me) is { } use)
            {
                var text = use.Text.Replace("[E]", key);
                var poor = !use.Blocked && use.Cost > me2.Points;
                ImGuiSafe.TextColored(use.Blocked ? Dim : poor ? Red : new float4(1, 1, 1, 1), poor ? $"{text}   (you have {me2.Points})" : text);
                if (use.Kind == ZUseKind.Revive && game.Players.TryGetValue((byte)use.Index, out var dp) && game.ZP(dp).BeingRevived > 0)
                    ImGui.ProgressBar(game.ZP(dp).BeingRevived, new float2(320 * _scale, 10 * _scale), "reviving"u8);
            }
            if (me.Alive && game.ZWaitingReady && game.ZPhase == ZRoundPhase.Break && game.Phase == MatchPhase.Playing)
                ImGuiSafe.TextColored(game.ZP(me).RoundReady ? Green : ZGold, ReadyPromptText(game, Keybinds.Name(settings.Bind(GameAction.Ready))));
            // Dropped builds near you: what they are and how long they stay (blinking red in their last seconds). Labelled in the
            // world over the weapon when it's on screen (Hud.WorldLabel, within 15 m); listed here when it's near (8 m) but off screen.
            if (me.Alive)
                foreach (var wd in game.ZWeaponDrops)
                {
                    var dx = wd.At.X - me.Position.X;
                    var dz = wd.At.Z - me.Position.Z;
                    if (dx * dx + dz * dz > 15 * 15)
                        continue;
                    var left = Math.Max(0, wd.Until - now);
                    var warn = left < game.ZRules.BuildDropWarnSeconds;
                    if (warn && ((int)(now * 4) & 1) == 0)
                        continue;
                    var whose = wd.Owner == me.Id ? "Your" : wd.OwnerName.Length > 0 ? $"{wd.OwnerName}'s" : "A";
                    var text = $"{whose} {wd.Build.Name}: {(int)left / 60}:{(int)left % 60:00} left{(warn ? " - about to despawn!" : "")}";
                    var col = warn ? new System.Numerics.Vector3(Red.X, Red.Y, Red.Z) : new System.Numerics.Vector3(ZGold.X, ZGold.Y, ZGold.Z);
                    if (!Hud.WorldLabel(wd.At + new System.Numerics.Vector3(0, 0.7f, 0), text, col, 1f, 15f * _scale) && dx * dx + dz * dz <= 8 * 8)
                        ImGuiSafe.TextColored(warn ? Red : ZGold, text);
                }
            foreach (var p in game.Players.Values)
                if (p != me && p.Alive && game.IsDowned(p))
                    ImGuiSafe.TextColored(Orange, $"{game.NameOf(p.Id)} is down: revive them! ({(int)(game.ZP(p).BleedFraction * game.ZRules.BleedoutSeconds)} s)");
        }
        ImGui.End();
    }
}
