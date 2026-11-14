namespace CallOfSlop;

/// <summary>
/// A story world (one with the opening's colony) starts its people unarmed (Karl, 2026-11-14: no gun at the start): the
/// stock gun of the loadout is never out, so the gun key brings out bare hands, and the server refuses its shots. A gun built
/// at a Gun Workbench (<see cref="Player.BuiltPrimary"/>) is a real gun and works as anywhere. Creative and plain Sandbox
/// worlds, and the story's own people (bots), keep their guns.
/// </summary>
public partial class Game
{
    /// <summary>
    /// Is this a story world? The server knows (the opening's colony); a client goes by the world being a survival one,
    /// which every new survival world's opening makes a story world, so its own tool changes match the server's.
    /// </summary>
    private bool StoryArmsOn => IsServer ? _opOn : SandboxOn && !SandboxInfiniteParts;

    /// <summary>Both sides: does <paramref name="p"/> carry no gun here (a person in a story world with no built gun)?</summary>
    internal bool StoryUnarmed(Player p) => StoryArmsOn && p.Bot == null && p.BuiltPrimary == null && p.BuiltSecondary == null;

    /// <summary>Server: is <paramref name="def"/> a stock gun a story world's person may not fire (anything but a gun they built)?</summary>
    internal bool StoryStockGun(Player p, WeaponDef def) =>
        StoryArmsOn && p.Bot == null && !(p.BuiltPrimary is { } a && a.Id == def.Id) && !(p.BuiltSecondary is { } b && b.Id == def.Id);

    /// <summary>Server: the tool a request gets once the gun is taken out of it (None, the gun, becomes bare hands).</summary>
    private ConstructTool StoryArmsTool(Player p, ConstructTool asked) =>
        asked == ConstructTool.None && StoryUnarmed(p) ? ConstructTool.Hands : asked;

    /// <summary>Server, each Sandbox tick: nobody unarmed has the gun out (a spawn, a saved world's tool, a seat left).</summary>
    private void StoryArmsTick()
    {
        if (!_opOn)
            return;
        foreach (var p in Players.Values)
            if (p.Alive && p.Id < MaxPlayers && SandboxHands.Hand(p.Id).Tool == ConstructTool.None && !SandboxHands.Hand(p.Id).Seated && StoryUnarmed(p))
                SandboxHands.SetTool(p.Id, ConstructTool.Hands, Constructs, p.Eye, p.Yaw);
    }
}
