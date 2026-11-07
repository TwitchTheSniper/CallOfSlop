namespace CallOfSlop;

/// <summary>What happened to a construct that the story layer hears of (docs/story/STORY.md, "The appraiser").</summary>
public enum CNoteKind : byte
{
    Disabled, // its first part shot or blown off (By: the attacker, when known)
    Piece, // Body split off from Other (a wreck piece is its construct's remains)
    Gone, // Body left the world
    Strip, // By took a part off Body, a wreck or disabled construct (the story keeps someone else's, and those a faction claims)
    Tow, // By coupled a hitch of Body to Other
    Made, // an Assembler on Body (owned by By) finished a part of def Other, on a machine of tier Tier
}

/// <summary>Where bulk came from, for the story's milestones (<see cref="ConstructWorld.Yield"/>).</summary>
public enum CYieldKind : byte
{
    Gathered, // a drill or saw took it from the ground or a tree, or a bucket dug it
    Refined, // a refining machine made it
}

/// <summary>Bulk gathered or refined since the appraiser last looked, summed per owner and material.</summary>
public struct CYield
{
    public CYieldKind Kind;
    public byte Owner, Material;
    public int Units;
}

public struct CNote
{
    public CNoteKind Kind;
    public byte By; // a player, 255: nobody known
    public ushort Body, Other;
    public byte Tier;
}

/// <summary>
/// Notes for the story layer (host only, and only while it listens: <see cref="NotesOn"/>). The constructs don't know the
/// story exists: they say what happened here, and the game's appraiser (Game/GameStory.cs) reads and clears the list
/// once a tick. Who did damage is the game's to say: it sets <see cref="DamageBy"/> around a player's shots and blasts.
/// </summary>
public sealed partial class ConstructWorld
{
    public const int MaxNotes = 1024;

    /// <summary>Is the story listening (the host in Sandbox)? Off: nothing is noted.</summary>
    public bool NotesOn;

    public readonly List<CNote> Notes = new(256);

    /// <summary>The player whose shot or blast is doing damage now (255: nobody known). The game sets it and puts it back.</summary>
    public byte DamageBy = 255;

    public void Note(CNoteKind kind, ushort body, byte by = 255, ushort other = 0, byte tier = 0)
    {
        if (NotesOn && Notes.Count < MaxNotes)
            Notes.Add(new CNote { Kind = kind, Body = body, By = by, Other = other, Tier = tier });
    }

    public const int MaxYields = 128;

    /// <summary>The bulk gathered and refined since the appraiser last looked (<see cref="YieldCount"/> in use): a sum, not an event a step.</summary>
    public readonly CYield[] Yields = new CYield[MaxYields];

    public int YieldCount;

    /// <summary>Bulk a player's construct gathered or refined (what went into the bin). Nobody's construct, or no story: nothing.</summary>
    public void Yield(CYieldKind kind, Construct c, byte material, int units)
    {
        if (!NotesOn || units <= 0 || c.Owner == 255)
            return;
        for (var i = 0; i < YieldCount; i++)
        {
            ref var y = ref Yields[i];
            if (y.Kind == kind && y.Owner == c.Owner && y.Material == material)
            {
                y.Units += units;
                return;
            }
        }
        if (YieldCount < MaxYields)
            Yields[YieldCount++] = new CYield { Kind = kind, Owner = c.Owner, Material = material, Units = units };
    }

    /// <summary>A construct is disabled (a part of it wrecked): anyone may salvage it now. The first time is noted.</summary>
    public void Disable(Construct c)
    {
        if (!c.Damaged && !c.Wreck)
            Note(CNoteKind.Disabled, c.Id, DamageBy);
        c.Damaged = true;
    }
}
