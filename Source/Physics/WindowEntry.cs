using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Zombies windows from the outside (board item wr01, 2026-10-28). A window's opening is an invisible wall for bodies
/// (<see cref="ZombiesMap"/>: bullets pass), so a player who ended up outside the play area had no way back in. Now a
/// player who is OUTSIDE every area, on the outside side of a window whose boards are all gone and whose area is open,
/// may mantle in through it: the usual mantle (Jump while moving forward, facing the window), which glides them across
/// the opening to just past its inner face at sill height, crouched (they drop to the floor inside). A player inside an
/// area never passes, so nobody leaves through a window, and a window with a board left blocks as before.
/// <para>Both movers ask here before their own ledge search, so the server (authoritative: it runs the same mover on
/// the player's input) and the client's prediction agree. It reads only the map (areas, windows, which doors are
/// open) and <see cref="Boards"/>; nothing is allocated.</para>
/// </summary>
public static class WindowEntry
{
    /// <summary>Boards left on each window: the game's <c>ZBoards</c> (Game keeps this pointing at it). Empty: no windows open.</summary>
    public static byte[] Boards = [];

    /// <summary>False while a zombie's movement is simulated (ZombieAI): these rules are for players (and bots) only.</summary>
    public static bool PlayersOnly = true;

    /// <summary>How far out from the opening's outer face the feet may be (the mantle reaches about this far).</summary>
    public const float Reach = 1.4f;

    /// <summary>
    /// The window mantle for a player at <paramref name="s"/> facing <paramref name="yaw"/>: true with the stance,
    /// target (feet) and ledge top to hand to <see cref="ClassicMover.StartMantle"/>. <paramref name="room"/> as in
    /// <see cref="ClassicMover.FindLedge"/> (null: the classic body check).
    /// </summary>
    public static bool TryFind(in MoveState s, float yaw, out Stance stance, out Vector3 target, out float top, Func<Vector3, Stance, bool>? room = null)
    {
        stance = Stance.Crouch;
        target = default;
        top = 0;
        if (!PlayersOnly || World.Map.Zombies is not { } zm || Boards.Length == 0 || Boards.Length != zm.Windows.Count)
            return false;
        var feet = s.Position;
        if (zm.Inside(feet))
            return false; // inside: windows stay shut that way
        var f = World.FlatForward(yaw);
        var maxReach = s.MovePerk == PerkId.Parkour ? Perks.ParkourReach : World.MantleReach;
        for (var i = 0; i < zm.Windows.Count; i++)
        {
            if (Boards[i] != 0 || !Allowed(zm, i, feet, f, maxReach, out var spot))
                continue;
            var fits = room?.Invoke(spot, Stance.Crouch) ?? !World.Blocked(spot, World.HeightOf(Stance.Crouch));
            if (!fits)
                continue;
            target = spot;
            top = spot.Y;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Would window <paramref name="window"/> take a player at <paramref name="feet"/> facing <paramref name="forward"/>
    /// (flat) in, ignoring its boards? Outside every area, on its outside side within <see cref="Reach"/> of its outer
    /// face and within its width, facing in, its sill within mantle reach, and its area open. <paramref name="spot"/>: where
    /// the mantle ends (just past the inner face, at sill height, in line with the feet).
    /// </summary>
    public static bool Allowed(ZombiesMap zm, int window, Vector3 feet, Vector3 forward, float maxReach, out Vector3 spot)
    {
        spot = default;
        var w = zm.Windows[window];
        var o = w.Opening;
        // The opening's thin axis is the way through; inward is towards the window's "inside" point.
        var thinX = o.Half.X <= o.Half.Z;
        var axis = thinX ? Vector3.UnitX : Vector3.UnitZ;
        var lat = thinX ? Vector3.UnitZ : Vector3.UnitX;
        var depth = thinX ? o.Half.X : o.Half.Z;
        var width = thinX ? o.Half.Z : o.Half.X;
        var inward = Vector3.Dot(w.Inside - w.Outside, axis) >= 0 ? axis : -axis;
        var rel = feet - o.Center;
        var along = Vector3.Dot(rel, inward);
        if (along > -depth || along < -depth - Reach)
            return false; // not on the outside side, or too far out
        var side = Vector3.Dot(rel, lat);
        if (MathF.Abs(side) > width + 0.15f)
            return false; // beside it, not in front of it
        if (Vector3.Dot(forward, inward) < 0.5f)
            return false; // not facing in
        var sill = o.Min.Y;
        if (sill - feet.Y > maxReach || sill - feet.Y < -0.3f)
            return false; // sill out of reach (or well below the feet)
        if (zm.Inside(feet) || !AreaOpen(zm, w.Area))
            return false; // inside already, or a locked area (no way round its doors)
        var across = MathF.Max(0, width - World.PlayerRadius);
        spot = o.Center + inward * (depth + World.PlayerRadius + 0.05f) + lat * Math.Clamp(side, -across, across);
        spot.Y = sill;
        return true;
    }

    /// <summary>How far from every area a player's feet must be to count as outside the play area.</summary>
    public const float OutsideMargin = 0.8f;

    /// <summary>
    /// Is <paramref name="feet"/> outside the play area: more than <see cref="OutsideMargin"/> from every area? (The thin
    /// gaps the areas leave at walls and doors, and between banded floors, never count.)
    /// </summary>
    public static bool OutsidePlayArea(ZombiesMap zm, Vector3 feet)
    {
        for (var i = 0; i < zm.Areas.Count; i++)
            if (zm.Areas[i].Distance(feet) <= OutsideMargin)
                return false;
        return true;
    }

    /// <summary>
    /// The other half of the rule, against leaks (2026-10-28): in a Zombies game, a mantle that starts in the play area
    /// must end inside an area (not in the gaps the areas leave over walls). Before this, chained mantles off crates,
    /// graves and railings put players on top of the outer walls (and from there outside) all round Blackmoor and
    /// Ashgrave, and on the organ loft's balustrade over the nave. A player already outside may climb anything (it can
    /// only help them back).
    /// </summary>
    /// <para>And no mantle up through a ceiling: crouched on a 2.3 m cabinet, the ledge search found Blackmoor's roof
    /// slab (3.6-3.9 m) over the head, with room on top of it, and the climb went up through it (onto the roof, then
    /// down outside). The body's whole column, from the feet to the ledge top plus its height, must be clear.</para>
    public static bool LeavesPlayArea(Vector3 feet, Vector3 target, Stance stance) =>
        PlayersOnly && World.Map.Zombies is { } zm && Boards.Length == zm.Windows.Count && Boards.Length > 0 && !OutsidePlayArea(zm, feet)
        && (!InsideBy(zm, target + new Vector3(0, 0.05f, 0), MantleInset)
            || World.BlockedByBoxes(feet + new Vector3(0, 0.02f, 0), target.Y + World.HeightOf(stance) - feet.Y - 0.02f));

    /// <summary>How far inside an area's edge (flat) a mantle from the play area must end: an outer wall's top, even where it overhangs the area's edge a little, is never a ledge.</summary>
    public const float MantleInset = 0.45f;

    /// <summary>Is <paramref name="p"/> in an area box shrunk by <paramref name="inset"/> on the flat (its heights as they are)?</summary>
    public static bool InsideBy(ZombiesMap zm, Vector3 p, float inset)
    {
        for (var i = 0; i < zm.Areas.Count; i++)
        {
            var a = zm.Areas[i];
            if (p.X >= a.X0 + inset && p.X <= a.X1 - inset && p.Z >= a.Z0 + inset && p.Z <= a.Z1 - inset && p.Y >= a.Y0 && p.Y <= a.Y1)
                return true;
            foreach (var m in a.More)
                if (p.X >= m.Min.X + inset && p.X <= m.Max.X - inset && p.Z >= m.Min.Z + inset && p.Z <= m.Max.Z - inset && p.Y >= m.Min.Y && p.Y <= m.Max.Y)
                    return true;
        }
        return false;
    }

    /// <summary>Is a Zombies area unlocked (the start area, or behind an open door)? The map-only twin of <c>Game.AreaOpen</c>.</summary>
    public static bool AreaOpen(ZombiesMap zm, int area)
    {
        if (area == zm.StartArea)
            return true;
        for (var d = 0; d < zm.Doors.Count; d++)
            if ((zm.Doors[d].AreaA == area || zm.Doors[d].AreaB == area) && World.Map.IsDoorOpen(d))
                return true;
        return false;
    }
}
