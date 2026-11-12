using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Looking closely at the colony's lore items (docs/story/STORY.md "Lore"; the host's side is Game/GameStoryLore.cs): the
/// crosshair on a lore part within use reach shows "Name  F: look closer", and the use key opens what it says
/// (UiStoryLore.cs) on this player's screen, every peer having the words (Story/Lore), and sends the ordinary use act so the
/// host counts the look. The mouse stays with the game: the words stay up until F again, Esc, or walking off.
/// </summary>
public static partial class Program
{
    private static int _loreOpen = -1; // the item being read (an index into Story/Lore's items), -1: none
    private static ushort _loreBody;
    private static int _lorePart;
    private static long _loreLookKey = -1;
    private static string _loreLook = "";

    /// <summary>How far from the item the words close (m).</summary>
    public const float LoreLeave = 4.5f;

    /// <summary>One frame of the Sandbox input: a lore part under the crosshair. True when the use key's press went to it (or would).</summary>
    private static bool LoreFrame(Game g, bool aimed, in ConstructHit hit)
    {
        if (!aimed || hit.Part < 0 || hit.Part >= hit.Body.PartCount || hit.Distance > ConstructBuild.UseReach)
            return false;
        var d = hit.Body.DefOf(hit.Part);
        var item = Game.StoryData.Lore.ByPart(d.Name);
        if (item < 0)
            return false;
        var reading = _loreOpen == item && _loreBody == hit.Body.Id;
        var key = (long)hit.Body.Id << 20 | (long)item << 1 | (reading ? 1L : 0L);
        if (key != _loreLookKey)
            (_loreLookKey, _loreLook) = (key, SandboxKeys.Line(d.Name, null, null, SandboxKeys.Do(SandboxKeys.Use, reading ? "put it down" : "look closer")));
        g.SandboxLookText = _loreLook;
        if (!Pressed(GameAction.Interact))
            return true;
        if (reading)
            LoreClose();
        else
        {
            (_loreOpen, _loreBody, _lorePart) = (item, hit.Body.Id, hit.Part);
            g.SandboxSend(CActKind.Use, body: hit.Body.Id, part: (ushort)hit.Part); // (the host counts the look: GameStoryLore.cs)
        }
        return true;
    }

    private static void LoreClose()
    {
        _loreOpen = -1;
        _loreBody = 0;
    }

    /// <summary>Esc: puts the item down (not the match).</summary>
    private static bool LoreEsc()
    {
        if (_loreOpen < 0)
            return false;
        LoreClose();
        return true;
    }

    /// <summary>The frame's ImGui pass: the words while the item is still there and near.</summary>
    private static void DrawLoreInMatch()
    {
        if (_loreOpen < 0)
            return;
        var items = Game.StoryData.Lore.Items;
        var g = _game;
        if (_session == null || !g.SandboxOn || _loreOpen >= items.Length || g.Local is not { Alive: true } me || g.Constructs.Get(_loreBody) is not { } c
            || _lorePart >= c.PartCount || Vector3.DistanceSquared(me.Position, c.ToWorld(c.Parts[_lorePart].Pos)) > LoreLeave * LoreLeave)
        {
            LoreClose();
            return;
        }
        Ui.DrawStoryLore(items[_loreOpen]);
    }
}
