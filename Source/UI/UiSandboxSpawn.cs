using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The inventory's Spawn tab (GameSandboxSpawns.cs; creative worlds only): friendly bot, enemy bot or a zombie group;
/// a bot's difficulty and gun (any, a weapon class or one gun) and how many; the zombies' count (1 to 500,000: a slider,
/// a number and presets, a warning above 100,000) and mix. "Place" closes the window and the next Fire spawns at the
/// aimed spot (Program.SandboxSpawn.cs). Clear all, the spawner parts (taken like any part), and for the host the caps
/// and whether everyone may spawn.
/// </summary>
public sealed partial class Ui
{
    private static readonly string[] SpawnKindLabels = ["Friendly bot", "Enemy bot", "Zombie group"];
    private static readonly string[] SpawnSkillLabels = ["Easy", "Normal", "Hard", "Insane"];
    private static readonly string[] SpawnClassLabels = ["Pistol", "SMG", "Rifle", "DMR", "Sniper", "Shotgun", "LMG"];
    private static readonly string[] SpawnPresetLabels = Game.SpawnZombiePresets.Select(n => n >= 1000 ? $"{n / 1000}k##sbzp{n}" : $"{n}##sbzp{n}").ToArray();
    private string[] _spGunLabels = [];
    private byte[] _spGunIds = [];
    private string _spLoadLabel = "", _spCountText = "";
    private int _spLoadKey = -1, _spCountKey = -1;

    /// <summary>The Spawn tab's body (the tab bar is drawn; this fills the window below it).</summary>
    private void SbDrawSpawn(Game game, Player me)
    {
        if (!game.SandboxCreativeSpawns)
        {
            ImGui.Spacing();
            ImGui.TextColored(Dim, "No spawning in a survival world: its enemies come from the world."u8);
            return;
        }
        if (_spGunIds.Length == 0)
        {
            var guns = Weapons.All.Where(Game.SpawnGunFits).OrderBy(w => w.Class).ThenBy(w => w.Name).ToArray();
            _spGunIds = guns.Select(w => w.Id).ToArray();
            _spGunLabels = guns.Select(w => $"{w.Name} ({w.Class})").ToArray();
        }
        ImGui.Spacing();
        // What.
        for (var k = 0; k < SpawnKindLabels.Length; k++)
        {
            if (k > 0)
                ImGui.SameLine();
            if (ImGui.RadioButton(SpawnKindLabels[k], (int)game.SpawnPick == k))
                game.SpawnPick = (SbSpawnKind)k;
        }
        ImGui.Separator();
        if (game.SpawnPick != SbSpawnKind.Zombies)
        {
            ImGui.TextColored(Dim, game.SpawnPick == SbSpawnKind.FriendlyBot ? "On your side: follows you, fights enemy bots and zombies."u8 : "Fights players and friendly bots."u8);
            var skill = (int)game.SpawnSkill;
            for (var k = 0; k < SpawnSkillLabels.Length; k++)
            {
                if (k > 0)
                    ImGui.SameLine();
                if (ImGui.RadioButton(SpawnSkillLabels[k], skill == k))
                    game.SpawnSkill = (BotDifficulty)k;
            }
            var lk = (int)game.SpawnLoad * 256 + game.SpawnLoadValue;
            if (lk != _spLoadKey)
            {
                _spLoadKey = lk;
                _spLoadLabel = game.SpawnLoad switch
                {
                    SbLoadKind.Class when game.SpawnLoadValue < SpawnClassLabels.Length => $"Class: {SpawnClassLabels[game.SpawnLoadValue]}",
                    SbLoadKind.Gun => Weapons.Get(game.SpawnLoadValue).Name,
                    _ => "Any gun (its personality's pick)",
                };
            }
            ImGui.SetNextItemWidth(320 * _scale);
            if (ImGui.BeginCombo("Gun##sbgun"u8, _spLoadLabel))
            {
                if (ImGui.Selectable("Any gun (its personality's pick)"u8, game.SpawnLoad == SbLoadKind.Any))
                    game.SpawnLoad = SbLoadKind.Any;
                for (var c = 0; c < SpawnClassLabels.Length; c++)
                    if (ImGui.Selectable(SpawnClassLabels[c], game.SpawnLoad == SbLoadKind.Class && game.SpawnLoadValue == c))
                        (game.SpawnLoad, game.SpawnLoadValue) = (SbLoadKind.Class, (byte)c);
                ImGui.Separator();
                for (var g = 0; g < _spGunIds.Length; g++)
                    if (ImGui.Selectable(_spGunLabels[g], game.SpawnLoad == SbLoadKind.Gun && game.SpawnLoadValue == _spGunIds[g]))
                        (game.SpawnLoad, game.SpawnLoadValue) = (SbLoadKind.Gun, _spGunIds[g]);
                ImGui.EndCombo();
            }
            var cap = Math.Max(1, Math.Min(game.SandboxBotCap, Game.SpawnBotCapMax));
            ImGui.SetNextItemWidth(320 * _scale);
            ImGui.SliderInt("How many##sbbots"u8, ref game.SpawnBots, 1, cap);
            game.SpawnBots = Math.Clamp(game.SpawnBots, 1, Game.SpawnBotCapMax);
        }
        else
        {
            ImGui.TextColored(Dim, "Hunt players and friendly bots, and swipe at constructs in the way."u8);
            ImGui.SetNextItemWidth(320 * _scale);
            ImGui.SliderInt("How many##sbzs"u8, ref game.SpawnZombies, 1, Game.SpawnZombieMax, "%d"u8, ImGuiSliderFlags.Logarithmic);
            ImGui.SetNextItemWidth(320 * _scale);
            ImGui.InputInt("##sbzn"u8, ref game.SpawnZombies, 10, 1000);
            game.SpawnZombies = Math.Clamp(game.SpawnZombies, 1, Game.SpawnZombieMax);
            for (var k = 0; k < SpawnPresetLabels.Length; k++)
            {
                if (k > 0)
                    ImGui.SameLine();
                if (ImGui.Button(SpawnPresetLabels[k]))
                    game.SpawnZombies = Game.SpawnZombiePresets[k];
            }
            if (game.SpawnZombies > Game.SpawnZombieWarn)
                ImGui.TextColored(new float4(1f, 0.7f, 0.3f, 1f), "Over 100,000: may be slow on this PC."u8);
            if (game.SpawnZombies != _spCountKey)
            {
                _spCountKey = game.SpawnZombies;
                _spCountText = game.SpawnZombies <= Game.SpawnIndividualsMax
                    ? $"{game.SpawnZombies:N0}: each one a full zombie, rising at the spot"
                    : $"{game.SpawnZombies:N0}: into the mass horde round the spot (the nearest wake as full zombies)";
            }
            ImGuiSafe.TextColored(Dim, _spCountText);
            var mix = game.SpawnMix == 1;
            if (ImGui.Checkbox("Mix in the zombie types"u8, ref mix))
                game.SpawnMix = (byte)(mix ? 1 : 0);
        }
        ImGui.Spacing();
        if (BigButton("PLACE##sbplace"))
        {
            game.SpawnPlacing = true;
            CloseSandboxParts(); // (the next Fire spawns at the aimed spot: Program.SandboxSpawn.cs)
            return;
        }
        ImGui.SameLine();
        if (BigButton("CLEAR ALL##sbclear"))
            game.SandboxSendClearSpawns();
        ImGui.Separator();
        // The spawner parts: taken like any part.
        ImGui.TextColored(Dim, "Spawner parts (set them up with the configure tool, wire them with the linking tool):"u8);
        foreach (var name in SpawnerParts)
            if (ConstructParts.Find(name) is { } d)
            {
                if (ImGui.Button(name))
                    SandboxPartsPick(game, d);
                ImGui.SameLine();
            }
        ImGui.NewLine();
        if (game.IsServer)
        {
            ImGui.Separator();
            ImGui.TextColored(Dim, "Host"u8);
            ImGui.Checkbox("Everyone can spawn and clear (off: only you)"u8, ref game.SandboxSpawnAnyone);
            ImGui.SetNextItemWidth(320 * _scale);
            ImGui.SliderInt("Most bots##sbbotcap"u8, ref game.SandboxBotCap, 1, Game.SpawnBotCapMax);
            ImGui.SetNextItemWidth(320 * _scale);
            ImGui.SliderInt("Most zombies##sbzcap"u8, ref game.SandboxZombieCap, 100, Game.SpawnZombieMax, "%d"u8, ImGuiSliderFlags.Logarithmic);
        }
        ImGuiSafe.TextColored(Dim, $"Spawned now: {game.SpawnedBotsNow} bots, {game.SpawnedZombiesNow:N0} zombies");
    }

    private static readonly string[] SpawnerParts = ["Bot Spawner", "Zombie Spawner"];
}
