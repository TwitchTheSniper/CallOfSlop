using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Small helpers shared by the loadout screens. (The prestige hats' choices and their drawing went with levelling, 2026-11-17.)
/// </summary>
public sealed partial class Ui
{
    /// <summary>Width of each card in the loadout overview's Lethal / Tactical row.</summary>
    internal static float OverviewHalfWidth(float width, float gap) => MathF.Max(0, (width - gap) / 2);
}
