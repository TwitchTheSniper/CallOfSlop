using System.Numerics;

namespace CallOfSlop;

// TODO(lead): replace with the game-modes implementation (Source/Game/BotObjectives.cs) when merging.
// These are the two hooks the bot AI uses for teams and objectives; until game modes land, it's free-for-all
// with no objective.
public sealed partial class Game
{
    /// <summary>Should <paramref name="bot"/> treat <paramref name="other"/> as an enemy (teams, friendly fire)?</summary>
    private bool IsEnemy(Player bot, Player other) => other != bot;

    /// <summary>Where the game mode wants <paramref name="bot"/> to go (hill, flag, carrier), or null for no objective.</summary>
    private Vector3? ObjectiveGoal(Player bot, double now) => null;
}
