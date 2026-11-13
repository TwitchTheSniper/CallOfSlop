using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The inventory's locked parts (docs/story/STORY.md "Technologies"): in survival with a story, a part a story faction
/// teaches that the player (or their faction) hasn't learnt is drawn greyed with a padlock, its tooltip says who teaches
/// it, the technology, the standing it is taught at and the player's own, and with none in hand it can't be taken. So
/// the menu reads as a list of goals. Creative worlds and worlds without a story lock nothing. The locks come from the
/// faction table (<see cref="FactionTable.LockOf"/>, per player, which the server sends), worked out again only when the
/// table changes while the window is open: nothing is done while it is closed.
/// </summary>
public sealed partial class Ui
{
    /// <summary>A beacon's window that hides what the network doesn't hold still shows the locked parts (the goals).</summary>
    public bool SandboxShowLocked = true;

    private (int Version, byte Me, bool On, SbItem[]? Items) _sbLockKey = (-1, 255, false, null);

    /// <summary>Do locks apply here: survival (parts are counted) and the story's factions teach something.</summary>
    private static bool SbLocksOn(Game game) => !game.SandboxInfiniteParts && game.FactionTable.StoryTech.Length > 0;

    /// <summary>While the window is open: each part tile's lock, made again when the table, the player or the rules change.</summary>
    private void SbLocksFrame(Game game, byte me)
    {
        var t = game.FactionTable;
        var on = SbLocksOn(game);
        var items = _sbItems[(int)SbTab.Parts];
        var key = (t.Version, me, on, items);
        if (_sbLockKey.Version == key.Version && _sbLockKey.Me == me && _sbLockKey.On == on && ReferenceEquals(_sbLockKey.Items, items))
            return;
        _sbLockKey = key;
        foreach (var it in items)
        {
            if (it?.Part == null)
                continue;
            var was = it.Locked;
            it.Locked = on && t.LockOf(me, it.Part.Id, out it.LockF, out it.LockK);
            if (was != it.Locked)
                _sbFilterVersion++; // (a beacon's window keeps locked parts in view)
        }
    }

    /// <summary>True when a part can't be taken: locked, and none in hand to place (one salvaged or given may still be placed).</summary>
    private bool SbPickLocked(Game game, ConstructPartDef def)
    {
        if (game.Local is not { } me || !SbLocksOn(game) || !game.FactionTable.LockOf(me.Id, def.Id, out var f, out var k))
            return false;
        if ((SandboxCounts?.PartCount(game, me.Id, def.Id) ?? -1) != 0)
            return false;
        var t = game.FactionTable;
        var (tech, tier) = t.TechNames(f, k);
        _sbSpawnNote = $"Locked: {t.StoryNames[f]} teaches the {def.Name} ({tech}{(tier.Length > 0 ? $", at {tier}" : "")})";
        _sbSpawnNoteUntil = ImGui.GetTime() + 4;
        return true;
    }

    /// <summary>The tooltip's lines for a locked part: who teaches it, at what standing, and how far the player is.</summary>
    private void SbLockTooltip(Game game, byte me, SbItem it)
    {
        if (!it.Locked || it.LockF < 0)
            return;
        var t = game.FactionTable;
        if (it.LockF >= t.StoryNames.Length)
            return;
        var (tech, tier) = t.TechNames(it.LockF, it.LockK);
        var need = t.TechNeed(it.LockF, it.LockK);
        var have = (int)t.PlayerStanding[me * FactionTable.MaxStory + it.LockF];
        ImGuiSafe.TextColored(new float4(1f, 0.62f, 0.25f, 1f), $"Locked: {t.StoryNames[it.LockF]} teaches it");
        ImGuiSafe.TextWrapped($"Technology: {tech}. Taught at {(tier.Length > 0 ? tier : "a higher tier")} (standing {need:+0;-0;0}); yours is {have:+0;-0;0}.");
        ImGuiSafe.TextColored(Dim, have >= need
            ? "Your standing is there: it is taught within a moment."
            : $"Earn {need - have} more standing with {t.StoryNames[it.LockF]}: jobs and deeds (F2). Your Assemblers build it once taught.");
    }

    /// <summary>A small padlock in a tile's top left corner (beside the tool dot).</summary>
    private void SbDrawLock(ImDrawListPtr dl, float2 at)
    {
        var s = _scale;
        var col = new ImColor8(255, 170, 70, 255);
        var b0 = new float2(at.X + 17 * s, at.Y + 9 * s);
        var b1 = new float2(b0.X + 12 * s, b0.Y + 9 * s);
        dl.AddRect(new float2(b0.X + 2.5f * s, b0.Y - 6 * s), new float2(b1.X - 2.5f * s, b0.Y + 3 * s), col, 3.5f * s, ImDrawFlags.None, 1.8f * s);
        dl.AddRectFilled(b0, b1, col, 1.5f * s);
        dl.AddRectFilled(new float2(b0.X + 5 * s, b0.Y + 3 * s), new float2(b0.X + 7 * s, b0.Y + 6.5f * s), new ImColor8(40, 26, 10, 255));
    }
}
