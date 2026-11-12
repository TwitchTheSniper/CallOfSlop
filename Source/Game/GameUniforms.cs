namespace CallOfSlop;

/// <summary>
/// Who wears what (Source/Cosmetics/Uniforms.cs, Cosmetics/uniforms.json; docs/sandbox/UNIFORMS.md). Host only, once a
/// second while a story runs: a story person (a bot) wears the uniform their title calls for ("Warden ", "Riot Officer "),
/// and a player is issued the uniform their standing with a faction earns (the Ministry of Slop's boiler suit for an
/// Inmate, its donkey jacket for a Contractor...) unless they picked Own Clothes on the APPEARANCE screen. The roster
/// carries the result (one byte a player), so everyone draws the same; it goes out only when someone's uniform changed.
/// Players are issued uniforms only in a world with the story's opening (the penal colony: a plain Sandbox world has a
/// story too, where everyone would be an Inmate); without a story nobody is in uniform.
/// </summary>
public sealed partial class Game
{
    /// <summary>A player's own uniform from a faction tier, or 0 (a world without the story's opening and its colony, Own Clothes, a tier with nothing issued).</summary>
    private byte IssuedUniform(Player p)
    {
        if (!_opOn || Story is not { } s || !p.Look.WearsUniform || StoryWho(p) is not (var who and >= 0))
            return 0;
        var party = s.Factions.Party(who);
        foreach (var u in Uniforms.All)
        {
            if (u.Tiers.Length == 0 || s.Factions.Find(u.Faction) is not (var f and >= 0))
                continue;
            var tier = s.Factions.Def(f).TierName(s.Factions.Standing(f, party));
            foreach (var t in u.Tiers)
                if (string.Equals(t, tier, StringComparison.OrdinalIgnoreCase))
                    return u.Index;
        }
        return 0;
    }

    /// <summary>The tag a faction's notices go out under ("MINISTRY MEMO"), or its name in capitals.</summary>
    private string VoiceOf(string faction) =>
        Story is { } s && s.Factions.Find(faction) is var f and >= 0 && s.Factions.Def(f).Voice is { Length: > 0 } v ? v : faction.ToUpperInvariant();

    /// <summary>Once a second (the story's tick): everyone's uniform; the roster goes out when one changed.</summary>
    private void UniformsTick(double now)
    {
        if (!IsServer || ServerTick % World.TickRate != 3 || Uniforms.All.Count == 0)
            return;
        var changed = false;
        foreach (var p in Players.Values)
        {
            var u = p.Bot != null ? Uniforms.ForName(p.Name) : IssuedUniform(p);
            if (u == p.Uniform)
                continue;
            var was = Uniforms.Get(p.Uniform);
            p.Uniform = u;
            changed = true;
            if (p.Bot != null || !p.Look.WearsUniform)
                continue; // (staff dress quietly; Own Clothes is the player's own doing)
            if (Uniforms.Get(u) is { } issued)
                ServerSystemChat($"[{VoiceOf(issued.Faction)}] Issued to {p.Name}: one {issued.Name}. {issued.About}", now);
            else if (was != null)
                ServerSystemChat($"[{VoiceOf(was.Faction)}] {p.Name}'s {was.Name} has been withdrawn. Please return it laundered.", now);
        }
        if (changed)
            BroadcastRoster();
    }
}
