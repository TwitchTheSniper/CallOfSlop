using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Zombies HUD (World at War style): the round number in red tally marks (a number from round 6), everyone's points
/// with +N popups, the interact prompt ("Hold E to buy ... [cost]"), perks, the downed / bleeding out / revive bars, the
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
        var left = game.ZPhase == ZRoundPhase.Round ? $"{game.ZRemaining} zombies left this round" : $"Next round in {Math.Ceiling(game.ZPhaseLeft):0} s";
        ImGuiSafe.TextColored(Dim, $"{left}   {(game.ZPower ? "Power: ON" : "Power: off")}");
        return true;
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
                var c = p.Color;
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
                ImGui.ProgressBar(me2.BleedFraction, new float2(320 * _scale, 10 * _scale), "bleeding out"u8);
                if (me2.BeingRevived > 0)
                    ImGui.ProgressBar(me2.BeingRevived, new float2(320 * _scale, 10 * _scale), "being revived"u8);
            }
            else if (!me.Alive && game.Phase == MatchPhase.Playing)
                ImGuiSafe.TextColored(Red, "You bled out. You'll be back at the start of the next round."u8);
            else if (game.FindZUse(me) is { } use)
            {
                var text = use.Text.Replace("[E]", key);
                var poor = !use.Blocked && use.Cost > me2.Points;
                ImGuiSafe.TextColored(use.Blocked ? Dim : poor ? Red : new float4(1, 1, 1, 1), poor ? $"{text}   (you have {me2.Points})" : text);
                if (use.Kind == ZUseKind.Revive && game.Players.TryGetValue((byte)use.Index, out var dp) && game.ZP(dp).BeingRevived > 0)
                    ImGui.ProgressBar(game.ZP(dp).BeingRevived, new float2(320 * _scale, 10 * _scale), "reviving"u8);
            }
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
